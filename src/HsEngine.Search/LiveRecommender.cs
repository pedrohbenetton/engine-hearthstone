using System.Globalization;
using System.Text;
using System.Text.Json;
using HsEngine.Ingestion;
using HsEngine.Simulation;

namespace HsEngine.Search;

public sealed record Recommendation(ObservedState Observed, ConversionReport Report, SearchResult Result);

/// <summary>
/// Live loop core (ADR-0007): feeds Power.log lines into the state machine and decides when a recommendation is
/// due. That is the local player's main phase, with no power block still resolving and something new since the
/// last recommendation, so every action the player takes triggers a fresh one.
/// </summary>
public sealed class LiveRecommender(SearchOptions options, Ismcts? search = null)
{
    private readonly PowerLogParser _parser = new();
    private readonly Ismcts _search = search ?? new Ismcts();
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

    public bool ShouldRecommend =>
        _version != _recommendedVersion
        && _blockDepth == 0
        && State.LocalPlayerId is int local
        && Game() is { } game
        && game["STATE"] != "COMPLETE"
        && game["STEP"] == "MAIN_ACTION"
        && CurrentPlayerId() == local;

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
        SearchResult result = _search.Search(converted.Game, options);
        return new Recommendation(observed, converted.Report, result);
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

public static class RecommendationFormatter
{
    public static string Format(Recommendation recommendation, int top = 3)
    {
        ObservedState observed = recommendation.Observed;
        ObservedSide me = observed.Me, opponent = observed.Opponent;
        int available = me.Resources + me.TemporaryResources - me.OverloadLocked;
        int remaining = available - me.ResourcesUsed;
        CultureInfo culture = CultureInfo.InvariantCulture;

        var text = new StringBuilder();
        text.AppendLine(culture, $"Turn {observed.Turn} | mana {remaining}/{available} | you {Life(me.Hero)} | opponent {Life(opponent.Hero)} | " +
                                 $"{recommendation.Report.Proxies.Count} proxies, {recommendation.Report.HiddenCards} hidden cards | " +
                                 $"{recommendation.Result.Iterations} iterations in {recommendation.Result.Elapsed.TotalSeconds:F1} s");
        if (recommendation.Report.Unsupported.Count > 0)
            text.AppendLine(culture, $"  left out: {string.Join(", ", recommendation.Report.Unsupported)}");

        int rank = 1;
        foreach (RankedAction action in recommendation.Result.Actions.Take(top))
        {
            // ASCII only: the Windows console code page garbles "±".
            text.AppendLine(culture, $"  {rank++}. {action.WinProbability * 100,3:F0}% +/-{action.ConfidenceInterval95 * 100:F0}%  {action.Description}");
            if (action.Continuation.Count > 0)
                text.AppendLine(culture, $"       then {string.Join(" > ", action.Continuation)}");
        }
        return text.ToString();
    }

    private static string Life(ObservedHero hero) =>
        hero.Armor > 0 ? $"{hero.Health - hero.Damage}+{hero.Armor} hp" : $"{hero.Health - hero.Damage} hp";
}
