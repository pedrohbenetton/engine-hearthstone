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
    /// <summary>UCB exploration constant; calibrated for dense [0, 1] Win% rewards (typical midgame spread ~0.12).</summary>
    public double Exploration { get; init; } = 0.25;
    /// <summary>
    /// Turn-scoped RAVE/AMAF equivalence parameter (virtual visits before direct Monte Carlo dominates; 0 disables RAVE).
    /// Commuting actions within a turn share action-value estimates at low visit counts and decay O(1/N) to exact MC.
    /// </summary>
    public double RaveEquivalence { get; init; } = 20.0;
    /// <summary>
    /// Coulom robust max-mean backup weight in [0, 1] within our own turn: blends a node's sample mean with its
    /// most-visited follow-up child's value so setup plays (e.g. The Coin) are not dragged down by exploratory blunders.
    /// </summary>
    public double MaxBackupWeight { get; init; } = 0.5;
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
/// <param name="ContinuationKeys">The same follow-up actions as keys, to replay the line (TurnPlanner).</param>
/// <param name="LethalProbability">Empirical probability [0, 1] that this line kills the opponent during our turn.</param>
public sealed record RankedAction(
    ActionKey Key,
    string Description,
    string? SourceCardName,
    int Visits,
    double WinProbability,
    double StdDev,
    double ConfidenceInterval95,
    IReadOnlyList<string> Continuation,
    IReadOnlyList<ActionKey>? ContinuationKeys = null,
    double LethalProbability = 0.0);

/// <param name="Actions">Root actions, most visited first (robust child).</param>
public sealed record SearchResult(IReadOnlyList<RankedAction> Actions, int Iterations, TimeSpan Elapsed);

/// <summary>
/// Single-observer ISMCTS over the searching player's turn (ADR-0004) with availability-aware UCB + turn-scoped RAVE
/// (AMAF), three-tier progressive action expansion, and stochastic turn-lethal tracking. Each iteration samples a
/// world, walks the action tree, finishes the turn, lets <see cref="IOpponentPolicy"/> answer, and scores the result
/// with the evaluator's Win Equity from the root player's perspective.
/// </summary>
public sealed class Ismcts(BoardEvaluator? evaluator = null, IDeterminizer? determinizer = null, IOpponentPolicy? opponent = null)
{
    private readonly BoardEvaluator _evaluator = evaluator ?? new BoardEvaluator();
    private readonly IDeterminizer _determinizer = determinizer ?? new HiddenInformationDeterminizer();
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
            var rng = new Random(Seeds.Derive(options.Seed, w));
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

        // Tree phase: select with availability-UCB + RAVE down the known part of our turn, expand one new action, then stop.
        for (int step = 0; world.State != State.COMPLETE && world.CurrentPlayer.PlayerId == me; step++)
        {
            if (step >= options.MaxActionsPerTurn)
            {
                world.Process(EndTurnTask.Any(world.CurrentPlayer));
                break;
            }

            var legal = new Dictionary<ActionKey, PlayerTask>();
            foreach (PlayerTask task in world.CurrentPlayer.Options())
                if (task.PlayerTaskType != PlayerTaskType.CONCEDE
                    && BoardPlacement.IsCanonicalOption(task)
                    && !BoardPlacement.IsDominatedOption(task))
                {
                    ActionKey key = ActionKey.From(task);
                    if (!node.Rejected.Contains(key))
                        legal.TryAdd(key, task);
                }

            foreach (Node child in node.Children.Values)
                if (legal.ContainsKey(child.Key!.Value))
                    child.Availability++;

            List<ActionKey> untried = legal.Keys.Where(k => !node.Children.ContainsKey(k)).ToList();
            Node? next;
            if (untried.Count > 0)
            {
                // 3-tier expansion: (1) information-revealing actions (ADR-0004), (2) active plays, (3) END_TURN.
                List<ActionKey> preferred = untried.Where(k => InformationActions.RevealsInformation(legal[k])).ToList();
                List<ActionKey> active = preferred.Count > 0
                    ? preferred
                    : untried.Where(k => k.Type != PlayerTaskType.END_TURN).ToList();
                List<ActionKey> pool = active.Count > 0 ? active : untried;
                ActionKey key = pool[rng.Next(pool.Count)];
                next = new Node(key, legal[key].ToString()) { Availability = 1, SourceCardName = legal[key].Source?.Card.Name };
                node.Children[key] = next;
            }
            else
            {
                next = SelectUcb(node, legal, options.Exploration, options.RaveEquivalence, options.MaxBackupWeight);
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

        // Rollout: the light policy finishes our turn, check whether opponent died on our turn, then play opponent's reply.
        if (world.State != State.COMPLETE && world.CurrentPlayer.PlayerId == me)
            _opponent.PlayTurn(world, rng);

        Controller perspective = world.Player1.PlayerId == me ? world.Player1 : world.Player2;
        bool turnLethal = perspective.PlayState == PlayState.WON
                          || (perspective.Opponent.Hero.IsDead && !perspective.Hero.IsDead);

        if (world.State != State.COMPLETE)
            _opponent.PlayTurn(world, rng);

        double reward = _evaluator.WinProbability(world, perspective);
        foreach (Node visited in path)
            visited.Record(reward, turnLethal);

        if (options.RaveEquivalence > 0 && path.Count > 0)
        {
            var seen = new HashSet<ActionKey>();
            for (int i = path.Count - 1; i >= 0; i--)
            {
                seen.Add(path[i].Key!.Value);
                Node parent = i == 0 ? tree : path[i - 1];
                foreach (ActionKey subsequent in seen)
                    parent.RecordAmaf(subsequent, reward);
            }
        }
    }

    private static Node? SelectUcb(Node node, Dictionary<ActionKey, PlayerTask> legal, double c, double raveK, double maxMix)
    {
        Node? best = null;
        double bestValue = double.NegativeInfinity;
        foreach (Node child in node.Children.Values)
        {
            ActionKey key = child.Key!.Value;
            if (!legal.ContainsKey(key))
                continue;

            double value;
            if (child.Visits == 0)
            {
                value = double.PositiveInfinity;
            }
            else
            {
                double q = child.BackedUpValue(maxMix);
                if (raveK > 0 && node.Amaf.TryGetValue(key, out (int Visits, double Sum) amaf) && amaf.Visits > 0)
                {
                    double beta = raveK / (raveK + child.Visits);
                    q = (1.0 - beta) * q + beta * (amaf.Sum / amaf.Visits);
                }
                value = q + c * Math.Sqrt(Math.Log(child.Availability) / child.Visits);
            }

            if (value > bestValue)
                (best, bestValue) = (child, value);
        }
        return best;
    }

    private static List<RankedAction> Merge(Node[] trees)
    {
        var merged = new Dictionary<ActionKey, (Node First, int Visits, int Lethals, double Sum, double SumSquares, Node MostVisited)>();
        foreach (Node tree in trees)
        foreach (Node child in tree.Children.Values)
        {
            ActionKey key = child.Key!.Value;
            merged[key] = merged.TryGetValue(key, out var acc)
                ? (acc.First, acc.Visits + child.Visits, acc.Lethals + child.Lethals, acc.Sum + child.Sum, acc.SumSquares + child.SumSquares,
                   child.Visits > acc.MostVisited.Visits ? child : acc.MostVisited)
                : (child, child.Visits, child.Lethals, child.Sum, child.SumSquares, child);
        }

        return merged.Values
            .Select(m =>
            {
                double mean = m.Visits == 0 ? 0 : m.Sum / m.Visits;
                double variance = m.Visits == 0 ? 0 : Math.Max(0, m.SumSquares / m.Visits - mean * mean);
                double sd = Math.Sqrt(variance);
                double ci = m.Visits == 0 ? 1 : Math.Min(1, 1.96 * sd / Math.Sqrt(m.Visits));
                double lethalProb = m.Visits == 0 ? 0 : (double)m.Lethals / m.Visits;
                List<Node> cont = Continuation(m.MostVisited);
                return new RankedAction(
                    m.First.Key!.Value,
                    m.First.Description!,
                    m.First.SourceCardName,
                    m.Visits,
                    mean,
                    sd,
                    ci,
                    cont.Select(n => n.Description!).ToList(),
                    cont.Select(n => n.Key!.Value).ToList(),
                    lethalProb);
            })
            .OrderByDescending(a => a.Visits)
            .ThenByDescending(a => a.WinProbability)
            .ToList();
    }

    private static List<Node> Continuation(Node node)
    {
        var line = new List<Node>();
        for (Node? n = node; n.Children.Count > 0; )
        {
            n = n.Children.Values.MaxBy(c => c.Visits)!;
            line.Add(n);
        }
        return line;
    }

    private sealed class Node(ActionKey? key, string? description)
    {
        public ActionKey? Key { get; } = key;
        public string? Description { get; } = description;
        public string? SourceCardName { get; init; }
        public Dictionary<ActionKey, Node> Children { get; } = [];
        public Dictionary<ActionKey, (int Visits, double Sum)> Amaf { get; } = [];
        public HashSet<ActionKey> Rejected { get; } = [];
        public int Visits { get; private set; }
        public int Lethals { get; private set; }
        /// <summary>Iterations in which this action was legal (SO-ISMCTS availability count).</summary>
        public int Availability { get; set; }
        public double Sum { get; private set; }
        public double SumSquares { get; private set; }
        public double Mean => Visits == 0 ? 0 : Sum / Visits;

        public double BackedUpValue(double maxMix)
        {
            double mean = Mean;
            if (maxMix <= 0 || Children.Count == 0)
                return mean;

            Node? robustChild = Children.Values.MaxBy(c => c.Visits);
            if (robustChild is null || robustChild.Visits < 4)
                return mean;

            return (1.0 - maxMix) * mean + maxMix * robustChild.BackedUpValue(maxMix);
        }

        public void Record(double reward, bool turnLethal)
        {
            Visits++;
            if (turnLethal)
                Lethals++;
            Sum += reward;
            SumSquares += reward * reward;
        }

        public void RecordAmaf(ActionKey action, double reward)
        {
            Amaf[action] = Amaf.TryGetValue(action, out (int Visits, double Sum) acc)
                ? (acc.Visits + 1, acc.Sum + reward)
                : (1, reward);
        }
    }
}
