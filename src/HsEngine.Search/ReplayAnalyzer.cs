using System.Globalization;
using System.Text;
using HsEngine.Evaluation;
using HsEngine.Ingestion;
using HsEngine.Simulation;
using SabberStoneCore.Model;

namespace HsEngine.Search;

public enum TurnVerdict { Optimal, Inaccuracy, Mistake, Blunder }

public sealed record TurnReview(
    int Turn,
    Recommendation OpeningRecommendation,
    double ActualEndTurnEquity,
    double EquityLoss,
    bool MissedLethal,
    TurnVerdict Verdict);

public sealed record ReplayReport(
    string LocalClass,
    string OpponentClass,
    string? InferredOpponentArchetype,
    IReadOnlyList<TurnReview> Turns)
{
    public double AverageEquityLoss => Turns.Count == 0 ? 0 : Turns.Average(t => t.EquityLoss);
    public int MissedLethals => Turns.Count(t => t.MissedLethal);
    public int Blunders => Turns.Count(t => t.Verdict == TurnVerdict.Blunder);
}

/// <summary>
/// Offline post-game GTO-style replay analyzer (ARCH-0001 §6 decision 2): replays a <c>Power.log</c>, computes the
/// optimal line at the start of each local turn, compares it against the actual board state reached at the end of that
/// turn, and flags EV losses and missed lethals.
/// </summary>
public sealed class ReplayAnalyzer(
    SearchOptions options,
    IReadOnlyList<DeckDefinition>? candidateDecks = null,
    BoardEvaluator? evaluator = null)
{
    private readonly BoardEvaluator _evaluator = evaluator ?? new BoardEvaluator();

    public ReplayReport Analyze(IEnumerable<string> logLines, int? onlyTurn = null)
    {
        var live = new LiveRecommender(options, candidateDecks: candidateDecks, evaluator: _evaluator);
        var reviews = new List<TurnReview>();

        int? pendingTurn = null;
        Recommendation? pendingOpening = null;
        ObservedState? lastInTurnObserved = null;

        foreach (string line in logLines)
        {
            live.Feed([line]);

            if (live.IsLocalMainPhase)
            {
                int currentTurn = live.State.Turn;
                if (pendingTurn != currentTurn && (onlyTurn is null || onlyTurn == currentTurn) && live.ShouldRecommend)
                {
                    if (pendingOpening is not null && lastInTurnObserved is not null)
                        reviews.Add(CloseTurn(pendingOpening, lastInTurnObserved, killedOpponent: false));

                    pendingOpening = live.Recommend();
                    pendingTurn = pendingOpening is not null ? currentTurn : null;
                }

                if (pendingTurn == currentTurn && live.State.LocalPlayerId is not null)
                    lastInTurnObserved = Observation.From(live.State);
            }
            else if (pendingOpening is not null && lastInTurnObserved is not null)
            {
                bool killed = OpponentDied(live.State);
                reviews.Add(CloseTurn(pendingOpening, lastInTurnObserved, killed));
                pendingOpening = null;
                pendingTurn = null;
                lastInTurnObserved = null;
            }
        }

        if (pendingOpening is not null && lastInTurnObserved is not null)
            reviews.Add(CloseTurn(pendingOpening, lastInTurnObserved, OpponentDied(live.State)));

        ObservedState? finalObs = live.State.LocalPlayerId is not null ? Observation.From(live.State) : null;
        string? topArch = reviews.LastOrDefault()?.OpeningRecommendation.Range?.TopArchetypeName;

        return new ReplayReport(
            finalObs?.Me.Hero.Class ?? "",
            finalObs?.Opponent.Hero.Class ?? "",
            topArch,
            reviews);
    }

    private TurnReview CloseTurn(Recommendation opening, ObservedState endOfTurn, bool killedOpponent)
    {
        double bestWin = opening.Plans.FirstOrDefault()?.WinProbability ?? 0.5;
        if (killedOpponent)
            return new TurnReview(opening.Observed.Turn, opening, 1.0, 0.0, false, TurnVerdict.Optimal);

        double actualEquity = EstimateEndTurnEquity(endOfTurn);
        bool missedLethal = opening.IsGuaranteedLethal;
        double loss = missedLethal ? Math.Max(0.15, 1.0 - actualEquity) : Math.Max(0.0, bestWin - actualEquity);

        TurnVerdict verdict = (missedLethal, loss) switch
        {
            (true, _) or (_, >= 0.15) => TurnVerdict.Blunder,
            (_, >= 0.08) => TurnVerdict.Mistake,
            (_, >= 0.03) => TurnVerdict.Inaccuracy,
            _ => TurnVerdict.Optimal,
        };

        return new TurnReview(opening.Observed.Turn, opening, actualEquity, loss, missedLethal, verdict);
    }

    private double EstimateEndTurnEquity(ObservedState endOfTurn)
    {
        ObservedState asLocal = endOfTurn with { CurrentPlayerId = endOfTurn.Me.PlayerId };
        Game game = ObservedGameBuilder.Build(asLocal, options.Seed).Game;
        var determinizer = new RangeDeterminizer(asLocal);
        var opponentPolicy = new GreedyOpponentPolicy(_evaluator);
        var rng = new Random(Seeds.Derive(options.Seed, asLocal.Turn));

        const int samples = 8;
        double sum = 0;
        for (int i = 0; i < samples; i++)
        {
            Game world = determinizer.Sample(game, rng);
            world.Process(SabberStoneCore.Tasks.PlayerTasks.EndTurnTask.Any(world.CurrentPlayer));
            if (world.State != SabberStoneCore.Enums.State.COMPLETE)
                opponentPolicy.PlayTurn(world, rng);
            sum += _evaluator.WinProbability(world, world.Player1);
        }
        return sum / samples;
    }

    private static bool OpponentDied(GameStateMachine state)
    {
        if (state.OpponentPlayerId is not int oppId)
            return false;
        PlayerState opp = state.Player(oppId);
        return opp.Entity["PLAYSTATE"] is "LOST" or "LOSING" || opp.Hero.Int("DAMAGE") >= opp.Hero.Int("HEALTH") + opp.Hero.Int("ARMOR") && opp.Hero.Int("HEALTH") > 0;
    }
}

public static class ReplayReportFormatter
{
    public static string Format(ReplayReport report)
    {
        CultureInfo c = CultureInfo.InvariantCulture;
        var sb = new StringBuilder();
        string arch = report.InferredOpponentArchetype is { Length: > 0 } a ? $" ({a})" : "";
        sb.AppendLine(c, $"=== Revisão Pós-Jogo: {report.LocalClass} vs {report.OpponentClass}{arch} ===");
        sb.AppendLine(c, $"Turnos analisados: {report.Turns.Count} | Perda média de Win%: {report.AverageEquityLoss * 100:F1} p.p. | " +
                         $"Erros graves: {report.Blunders} | Letais perdidos: {report.MissedLethals}");
        sb.AppendLine();

        foreach (TurnReview t in report.Turns)
        {
            string label = t.Verdict switch
            {
                TurnVerdict.Blunder => t.MissedLethal ? "[LETAL PERDIDO]" : "[ERRO GRAVE]",
                TurnVerdict.Mistake => "[ERRO]",
                TurnVerdict.Inaccuracy => "[IMPRECISÃO]",
                _ => "[OK]",
            };
            TurnPlan? best = t.OpeningRecommendation.Plans.FirstOrDefault();
            string bestLine = best is not null ? string.Join(" > ", best.Steps.Select(PlanText.Describe)) : "-";
            double bestPct = (best?.WinProbability ?? 0) * 100;
            sb.AppendLine(c, $"Turno {t.Turn,2} {label,-15} melhor ({bestPct,3:F0}% → real {t.ActualEndTurnEquity * 100,3:F0}%, Δ {-t.EquityLoss * 100:+0.0;-0.0;0.0} p.p.): {bestLine}");
        }
        return sb.ToString();
    }
}
