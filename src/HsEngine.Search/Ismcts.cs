using System.Diagnostics;
using HsEngine.Evaluation;
using SabberStoneCore.Enums;
using SabberStoneCore.Model;
using SabberStoneCore.Model.Entities;
using SabberStoneCore.Tasks.PlayerTasks;

namespace HsEngine.Search;

public sealed record SearchOptions
{
    /// <summary>Iterations per worker. When both this and <see cref="TimeLimit"/> are set, the first reached stops.</summary>
    public int? Iterations { get; init; }
    public TimeSpan? TimeLimit { get; init; }
    /// <summary>Independent trees merged at the root (root parallelization).</summary>
    public int Workers { get; init; } = 1;
    public int Seed { get; init; }
    /// <summary>UCB exploration constant; rewards are win probabilities in [0, 1].</summary>
    public double Exploration { get; init; } = 0.7;
    /// <summary>Guards against endless turns: past this many actions the turn is ended.</summary>
    public int MaxActionsPerTurn { get; init; } = 40;
}

/// <param name="Key">Determinization-independent action identity.</param>
/// <param name="Description">The action as SabberStone prints it in the first world it was seen in.</param>
/// <param name="SourceCardName">Card behind the action, or null for end turn.</param>
/// <param name="WinProbability">Mean reward (estimated Win Equity) of iterations through this action.</param>
/// <param name="StdDev">Standard deviation of those rewards: the line's risk.</param>
/// <param name="ConfidenceInterval95">Half-width of the 95% interval around <see cref="WinProbability"/>.</param>
/// <param name="Continuation">Most-visited follow-up actions within the turn.</param>
public sealed record RankedAction(
    ActionKey Key,
    string Description,
    string? SourceCardName,
    int Visits,
    double WinProbability,
    double StdDev,
    double ConfidenceInterval95,
    IReadOnlyList<string> Continuation);

/// <param name="Actions">Root actions, most visited first (robust child).</param>
public sealed record SearchResult(IReadOnlyList<RankedAction> Actions, int Iterations, TimeSpan Elapsed);

/// <summary>
/// Single-observer ISMCTS over the searching player's turn (ADR-0004). Each iteration samples a world,
/// walks the action tree with availability-aware UCB, ends the turn, lets <see cref="IOpponentPolicy"/> answer,
/// and scores the result with the evaluator's Win Equity from the root player's perspective.
/// </summary>
public sealed class Ismcts(BoardEvaluator? evaluator = null, IDeterminizer? determinizer = null, IOpponentPolicy? opponent = null)
{
    private readonly BoardEvaluator _evaluator = evaluator ?? new BoardEvaluator();
    private readonly IDeterminizer _determinizer = determinizer ?? new ChanceDeterminizer();
    private readonly IOpponentPolicy _opponent = opponent ?? new GreedyOpponentPolicy(evaluator ?? new BoardEvaluator());

    public SearchResult Search(Game root, SearchOptions options)
    {
        if (options.Iterations is null && options.TimeLimit is null)
            throw new ArgumentException("Set Iterations, TimeLimit or both.", nameof(options));

        var clock = Stopwatch.StartNew();
        var trees = new Node[options.Workers];
        var counts = new int[options.Workers];

        Parallel.For(0, options.Workers, new ParallelOptions { MaxDegreeOfParallelism = options.Workers }, w =>
        {
            var rng = new Random(HashCode.Combine(options.Seed, w));
            var tree = new Node(null, null);
            int n = 0;
            while ((options.Iterations is not int max || n < max) &&
                   (options.TimeLimit is not TimeSpan limit || clock.Elapsed < limit))
            {
                Iterate(root, tree, rng, options);
                n++;
            }
            trees[w] = tree;
            counts[w] = n;
        });

        return new SearchResult(Merge(trees), counts.Sum(), clock.Elapsed);
    }

    private void Iterate(Game root, Node tree, Random rng, SearchOptions options)
    {
        Game world = _determinizer.Sample(root, rng);
        int me = root.CurrentPlayer.PlayerId;
        var path = new List<Node>();
        Node node = tree;

        // Tree phase: select with UCB down the known part of our turn, expand one new action, then stop.
        for (int step = 0; world.State != State.COMPLETE && world.CurrentPlayer.PlayerId == me; step++)
        {
            if (step >= options.MaxActionsPerTurn)
            {
                world.Process(EndTurnTask.Any(world.CurrentPlayer));
                break;
            }

            var legal = new Dictionary<ActionKey, PlayerTask>();
            foreach (PlayerTask task in world.CurrentPlayer.Options())
                if (task.PlayerTaskType != PlayerTaskType.CONCEDE && !node.Rejected.Contains(ActionKey.From(task)))
                    legal.TryAdd(ActionKey.From(task), task);

            foreach (Node child in node.Children.Values)
                if (legal.ContainsKey(child.Key!.Value))
                    child.Availability++;

            List<ActionKey> untried = legal.Keys.Where(k => !node.Children.ContainsKey(k)).ToList();
            Node? next;
            if (untried.Count > 0)
            {
                // ADR-0004: information-revealing actions first; uniform among the remaining ties.
                List<ActionKey> preferred = untried.Where(k => InformationActions.RevealsInformation(legal[k])).ToList();
                List<ActionKey> pool = preferred.Count > 0 ? preferred : untried;
                ActionKey key = pool[rng.Next(pool.Count)];
                next = new Node(key, legal[key].ToString()) { Availability = 1, SourceCardName = legal[key].Source?.Card.Name };
                node.Children[key] = next;
            }
            else
            {
                next = SelectUcb(node, legal, options.Exploration);
                if (next is null)
                    break;
            }

            if (!world.Process(legal[next.Key!.Value]))
            {
                // SabberStone can list options it then rejects; never offer this one from here again.
                node.Children.Remove(next.Key.Value);
                node.Rejected.Add(next.Key.Value);
                continue;
            }

            path.Add(next);
            node = next;
            if (untried.Count > 0)
                break;
        }

        // Rollout: the light policy finishes our turn, then plays the opponent's reply.
        if (world.State != State.COMPLETE && world.CurrentPlayer.PlayerId == me)
            _opponent.PlayTurn(world, rng);
        if (world.State != State.COMPLETE)
            _opponent.PlayTurn(world, rng);

        Controller perspective = world.Player1.PlayerId == me ? world.Player1 : world.Player2;
        double reward = _evaluator.WinProbability(world, perspective);
        foreach (Node visited in path)
            visited.Record(reward);
    }

    private static Node? SelectUcb(Node node, Dictionary<ActionKey, PlayerTask> legal, double c)
    {
        Node? best = null;
        double bestValue = double.NegativeInfinity;
        foreach (Node child in node.Children.Values)
        {
            if (!legal.ContainsKey(child.Key!.Value))
                continue;
            double value = child.Visits == 0
                ? double.PositiveInfinity
                : child.Mean + c * Math.Sqrt(Math.Log(child.Availability) / child.Visits);
            if (value > bestValue)
                (best, bestValue) = (child, value);
        }
        return best;
    }

    private static List<RankedAction> Merge(Node[] trees)
    {
        var merged = new Dictionary<ActionKey, (Node First, int Visits, double Sum, double SumSquares, Node MostVisited)>();
        foreach (Node tree in trees)
        foreach (Node child in tree.Children.Values)
        {
            ActionKey key = child.Key!.Value;
            merged[key] = merged.TryGetValue(key, out var acc)
                ? (acc.First, acc.Visits + child.Visits, acc.Sum + child.Sum, acc.SumSquares + child.SumSquares,
                   child.Visits > acc.MostVisited.Visits ? child : acc.MostVisited)
                : (child, child.Visits, child.Sum, child.SumSquares, child);
        }

        return merged.Values
            .Select(m =>
            {
                double mean = m.Visits == 0 ? 0 : m.Sum / m.Visits;
                double variance = m.Visits == 0 ? 0 : Math.Max(0, m.SumSquares / m.Visits - mean * mean);
                double sd = Math.Sqrt(variance);
                double ci = m.Visits == 0 ? 1 : Math.Min(1, 1.96 * sd / Math.Sqrt(m.Visits));
                return new RankedAction(m.First.Key!.Value, m.First.Description!, m.First.SourceCardName,
                    m.Visits, mean, sd, ci, Continuation(m.MostVisited));
            })
            .OrderByDescending(a => a.Visits)
            .ThenByDescending(a => a.WinProbability)
            .ToList();
    }

    private static List<string> Continuation(Node node)
    {
        var line = new List<string>();
        for (Node? n = node; n.Children.Count > 0; )
        {
            n = n.Children.Values.MaxBy(c => c.Visits)!;
            line.Add(n.Description!);
        }
        return line;
    }

    private sealed class Node(ActionKey? key, string? description)
    {
        public ActionKey? Key { get; } = key;
        public string? Description { get; } = description;
        public string? SourceCardName { get; init; }
        public Dictionary<ActionKey, Node> Children { get; } = [];
        public HashSet<ActionKey> Rejected { get; } = [];
        public int Visits { get; private set; }
        /// <summary>Iterations in which this action was legal (SO-ISMCTS availability count).</summary>
        public int Availability { get; set; }
        public double Sum { get; private set; }
        public double SumSquares { get; private set; }
        public double Mean => Visits == 0 ? 0 : Sum / Visits;

        public void Record(double reward)
        {
            Visits++;
            Sum += reward;
            SumSquares += reward * reward;
        }
    }
}
