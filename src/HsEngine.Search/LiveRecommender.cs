using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using HsEngine.Evaluation;
using HsEngine.Ingestion;
using HsEngine.Simulation;

namespace HsEngine.Search;

/// <param name="Plans">Step plans of the top lines (ADR-0009), best first.</param>
/// <param name="Range">Bayesian archetype posterior and matched local deck (ADR-0006).</param>
/// <param name="IsGuaranteedLethal">True when <see cref="LethalSolver"/> proved an exact deterministic lethal.</param>
public sealed record Recommendation(
    ObservedState Observed,
    ConversionReport Report,
    SearchResult Result,
    IReadOnlyList<TurnPlan> Plans,
    RangeSummary? Range = null,
    bool IsGuaranteedLethal = false);

/// <summary>
/// Live loop core (ADR-0007): feeds Power.log lines into the state machine and decides when a recommendation is
/// due. First checks exact deterministic lethal via <see cref="LethalSolver"/> (ADR-0004), then runs SO-ISMCTS
/// over worlds sampled from <see cref="RangeDeterminizer"/> (ADR-0006).
/// </summary>
public sealed class LiveRecommender(
    SearchOptions options,
    Ismcts? search = null,
    BayesianRangeEstimator? rangeEstimator = null,
    IReadOnlyList<DeckDefinition>? candidateDecks = null,
    BoardEvaluator? evaluator = null)
{
    private const int PlannedLines = 3;
    private const int LiveLethalMaxNodes = 25_000;

    private readonly PowerLogParser _parser = new();
    private readonly Ismcts? _customSearch = search;
    private readonly BayesianRangeEstimator _rangeEstimator = rangeEstimator ?? new BayesianRangeEstimator();
    private readonly IReadOnlyList<DeckDefinition> _candidateDecks = candidateDecks ?? [];
    private readonly BoardEvaluator _evaluator = evaluator ?? new BoardEvaluator();
    private readonly LethalSolver _lethalSolver = new(LiveLethalMaxNodes);
    private long _version;
    private long _recommendedVersion = -1;
    private int _blockDepth;
    private string? _lastObservation;

    public GameStateMachine State { get; } = new();

    public void Feed(IEnumerable<string> lines)
    {
        foreach (string line in lines)
            foreach (PowerEvent e in _parser.Feed(line))
                Apply(e);
    }

    /// <summary>The game is running and the local player is in the main phase of their turn.</summary>
    public bool IsLocalMainPhase =>
        State.LocalPlayerId is int local
        && Game() is { } game
        && game["STATE"] != "COMPLETE"
        && game["STEP"] == "MAIN_ACTION"
        && CurrentPlayerId() == local;

    public bool ShouldRecommend => _version != _recommendedVersion && _blockDepth == 0 && IsLocalMainPhase;

    public Recommendation? Recommend()
    {
        if (!ShouldRecommend)
            return null;

        // Marked before the work: a position that fails to convert is not retried until the log moves on.
        _recommendedVersion = _version;
        ObservedState observed = Observation.From(State);

        // Many log lines (timers, counters, UI hints) change nothing the player can see.
        string fingerprint = JsonSerializer.Serialize(observed);
        if (fingerprint == _lastObservation)
            return null;
        _lastObservation = fingerprint;

        ConvertedGame converted = ObservedGameBuilder.Build(observed, options.Seed);
        DeckDefinition? matchedLocalDeck = DeckMatcher.MatchLocalDeck(observed.Me, _candidateDecks);
        var determinizer = new RangeDeterminizer(observed, _rangeEstimator, matchedLocalDeck);

        // ADR-0004 item 1: exact deterministic lethal short-circuits MCTS (unseen deck cards are inert HSE_HIDDEN here).
        var lethalClock = Stopwatch.StartNew();
        LethalResult lethal = _lethalSolver.Solve(converted.Game);
        lethalClock.Stop();
        if (lethal is { IsLethal: true, IsDeterministic: true })
        {
            (TurnPlan lethalPlan, RankedAction lethalAction) = TurnPlanner.FromOptionPath(converted.Game, lethal.OptionPath);
            var lethalSearch = new SearchResult([lethalAction], lethal.NodesExpanded, lethalClock.Elapsed);
            return new Recommendation(observed, converted.Report, lethalSearch, [lethalPlan], determinizer.Summary, IsGuaranteedLethal: true);
        }

        Ismcts engine = _customSearch ?? new Ismcts(_evaluator, determinizer);
        SearchResult result = engine.Search(converted.Game, options);
        List<TurnPlan> plans = result.Actions.Take(PlannedLines).Select(a => TurnPlanner.From(converted.Game, a)).ToList();
        return new Recommendation(observed, converted.Report, result, plans, determinizer.Summary, IsGuaranteedLethal: false);
    }

    private void Apply(PowerEvent e)
    {
        switch (e)
        {
            case CreateGame:
                _blockDepth = 0;
                _lastObservation = null;
                break;
            case BlockStart:
                _blockDepth++;
                break;
            case BlockEnd:
                _blockDepth = Math.Max(0, _blockDepth - 1);
                break;
        }
        State.Apply(e);
        _version++;
    }

    private TrackedEntity? Game() => State.Entities.Values.FirstOrDefault(x => x["CARDTYPE"] == "GAME");

    private int CurrentPlayerId() =>
        State.Entities.Values.Where(x => x["CARDTYPE"] == "PLAYER" && x["CURRENT_PLAYER"] == "1").Select(x => x.Int("PLAYER_ID")).FirstOrDefault();
}

/// <summary>What the overlay panel shows (ADR-0009): a header, numbered steps and a coverage footer.</summary>
public sealed record PanelContent(string Header, IReadOnlyList<string> Steps, string Footer);

public static class PanelText
{
    public static PanelContent Waiting { get; } = new("Aguardando seu turno", [], "");
    public static PanelContent Thinking { get; } = new("Calculando melhor jogada…", [], "");

    public static PanelContent From(Recommendation recommendation)
    {
        CultureInfo culture = CultureInfo.GetCultureInfo("pt-BR");
        TurnPlan? best = recommendation.Plans.FirstOrDefault();
        if (best is null)
            return Waiting;

        ConversionReport report = recommendation.Report;
        var footer = new StringBuilder();
        if (recommendation.Range is { TopArchetypeName: { Length: > 0 } arch })
            footer.Append(culture, $"Oponente: {arch} ({recommendation.Range.TopArchetypeProbability * 100:F0}%) · ");

        footer.Append(culture,
            $"{report.Proxies.Count} cartas sem efeito (proxy) · busca {recommendation.Result.Elapsed.TotalSeconds:F1} s");

        if (recommendation.Plans.Count > 1 && recommendation.Plans[1].Steps.Count > 0)
            footer.Append(culture,
                $"\nAlternativa ({recommendation.Plans[1].WinProbability * 100:F0}%): {PlanText.Describe(recommendation.Plans[1].Steps[0])}");

        string header = recommendation.IsGuaranteedLethal
            ? string.Create(culture, $"LETAL GARANTIDO (100%) · turno {recommendation.Observed.Turn}")
            : string.Create(culture, $"Vitória estimada {best.WinProbability * 100:F0}% · turno {recommendation.Observed.Turn}");

        return new PanelContent(
            header,
            best.Steps.Select((s, i) => $"{i + 1}. {PlanText.Describe(s)}").ToList(),
            footer.ToString());
    }
}

/// <summary>Terminal text of a recommendation, in Portuguese like the overlay.</summary>
public static class RecommendationFormatter
{
    public static string Format(Recommendation recommendation, int top = 3)
    {
        ObservedState observed = recommendation.Observed;
        ObservedSide me = observed.Me, opponent = observed.Opponent;
        (int remaining, int available) = Mana(me);
        CultureInfo culture = CultureInfo.InvariantCulture;

        var text = new StringBuilder();
        string rangeInfo = recommendation.Range is { TopArchetypeName: { Length: > 0 } arch }
            ? $" | range {arch} ({recommendation.Range.TopArchetypeProbability * 100:F0}%)"
            : "";
        string lethalTag = recommendation.IsGuaranteedLethal ? " | LETAL EXATO" : "";

        text.AppendLine(culture, $"Turno {observed.Turn} | mana {remaining}/{available} | você {Life(me.Hero)} | oponente {Life(opponent.Hero)}" +
                                 $"{rangeInfo} | {recommendation.Report.Proxies.Count} cartas sem efeito (proxy) | busca {recommendation.Result.Elapsed.TotalSeconds:F1} s{lethalTag}");
        if (recommendation.Report.Unsupported.Count > 0)
            text.AppendLine(culture, $"  fora da simulação: {string.Join(", ", recommendation.Report.Unsupported)}");

        int rank = 1;
        foreach (TurnPlan plan in recommendation.Plans.Take(top))
            text.AppendLine(culture, $"  {rank++}. {plan.WinProbability * 100,3:F0}%  {string.Join(" > ", plan.Steps.Select(PlanText.Describe))}");
        return text.ToString();
    }

    private static (int Remaining, int Available) Mana(ObservedSide side)
    {
        int available = side.Resources + side.TemporaryResources - side.OverloadLocked;
        return (Math.Max(0, available - side.ResourcesUsed), Math.Max(0, available));
    }

    private static string Life(ObservedHero hero)
    {
        int hp = hero.Health - hero.Damage;
        return hero.Armor > 0
            ? string.Create(CultureInfo.InvariantCulture, $"{hp}+{hero.Armor} hp")
            : string.Create(CultureInfo.InvariantCulture, $"{hp} hp");
    }
}
