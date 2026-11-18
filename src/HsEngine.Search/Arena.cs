using System.Diagnostics;
using HsEngine.Evaluation;
using HsEngine.Simulation;
using SabberStoneCore.Enums;
using SabberStoneCore.Model;
using SabberStoneCore.Model.Entities;
using SabberStoneCore.Tasks.PlayerTasks;

namespace HsEngine.Search;

/// <param name="Seed">Deal seed; each deal is meant to be played once from each seat.</param>
/// <param name="IsmctsPlayer">Seat (1 goes first, 2 gets The Coin) played by the ISMCTS agent.</param>
/// <param name="Outcome">Result for the ISMCTS agent; TIED also covers the turn limit.</param>
/// <param name="Decisions">Searches run by the ISMCTS agent (one per action).</param>
public sealed record MatchResult(
    long Seed,
    int IsmctsPlayer,
    PlayState Outcome,
    int Turns,
    int Decisions,
    TimeSpan Elapsed,
    IReadOnlyList<double>? Player1TurnScores = null);

/// <param name="WinRate">Wins plus half the ties, over games.</param>
/// <param name="Lower95">Wilson score interval, 95%.</param>
public sealed record MatchSummary(int Games, int Wins, int Losses, int Ties, double WinRate, double Lower95, double Upper95)
{
    public static MatchSummary From(IReadOnlyCollection<MatchResult> results)
    {
        int n = results.Count;
        int wins = results.Count(r => r.Outcome == PlayState.WON);
        int losses = results.Count(r => r.Outcome == PlayState.LOST);
        int ties = n - wins - losses;
        double p = n == 0 ? 0 : (wins + 0.5 * ties) / n;

        const double z = 1.96;
        double denominator = 1 + z * z / n;
        double centre = (p + z * z / (2 * n)) / denominator;
        double margin = z * Math.Sqrt(p * (1 - p) / n + z * z / (4.0 * n * n)) / denominator;
        return new MatchSummary(n, wins, losses, ties, p, centre - margin, centre + margin);
    }
}

/// <summary>
/// Slice 0 acceptance harness (ARCH-0001 §2.2): ISMCTS against the greedy policy in a Basic Mage mirror.
/// The ISMCTS agent re-searches before every action and only sees what its seat could see: the search
/// resamples hidden information on its own (<see cref="HiddenInformationDeterminizer"/>).
/// </summary>
public sealed class Arena(SearchOptions search, int maxTurns = 60, int maxActionsPerTurn = 40, EvaluationWeights? weights = null)
{
    private readonly BoardEvaluator _evaluator = new(weights);
    private readonly LethalSolver _lethalSolver = new(maxNodes: 10_000);

    public MatchResult Play(long seed, int ismctsPlayer)
    {
        var clock = Stopwatch.StartNew();
        Game game = GameFactory.CreateWithDecks(CardClass.MAGE, Decks.BasicMage, CardClass.MAGE, Decks.BasicMage, seed);
        var greedy = new GreedyOpponentPolicy(_evaluator);
        var rng = new Random(unchecked((int)seed * 31 + ismctsPlayer));
        var p1Scores = new List<double>();
        int decisions = 0;

        while (game.State != State.COMPLETE && game.Turn <= maxTurns)
        {
            double rawScore = _evaluator.Evaluate(game, game.Player1);
            if (Math.Abs(rawScore) < BoardEvaluator.WinScore)
                p1Scores.Add(rawScore);

            if (game.CurrentPlayer.PlayerId != ismctsPlayer)
            {
                greedy.PlayTurn(game, rng);
                continue;
            }

            int turn = game.Turn;
            for (int step = 0; game.State != State.COMPLETE && game.Turn == turn; step++)
            {
                PlayerTask task = step < maxActionsPerTurn ? Decide(game, decisions++, seed) : EndTurnTask.Any(game.CurrentPlayer);
                if (!game.Process(task))
                    game.Process(EndTurnTask.Any(game.CurrentPlayer));
            }
        }

        Controller me = game.Player1.PlayerId == ismctsPlayer ? game.Player1 : game.Player2;
        PlayState outcome = game.State == State.COMPLETE && me.PlayState is PlayState.WON or PlayState.LOST
            ? me.PlayState
            : PlayState.TIED;
        return new MatchResult(seed, ismctsPlayer, outcome, game.Turn, decisions, clock.Elapsed, p1Scores);
    }

    private PlayerTask Decide(Game game, int decision, long seed)
    {
        List<PlayerTask> options = game.CurrentPlayer.Options();
        LethalResult lethal = _lethalSolver.Solve(game);
        if (lethal is { IsLethal: true, IsDeterministic: true, OptionPath.Count: > 0 }
            && (uint)lethal.OptionPath[0] < (uint)options.Count)
        {
            return options[lethal.OptionPath[0]];
        }

        SearchResult result = new Ismcts(_evaluator).Search(game, search with { Seed = Seeds.Derive(seed, decision) });
        ActionKey best = result.Actions[0].Key;
        return options.FirstOrDefault(t => BoardPlacement.IsCanonicalOption(t) && ActionKey.From(t) == best)
               ?? options.FirstOrDefault(t => ActionKey.From(t) == best)
               ?? EndTurnTask.Any(game.CurrentPlayer);
    }
}
