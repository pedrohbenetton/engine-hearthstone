using HsEngine.Simulation;
using SabberStoneCore.Enums;
using SabberStoneCore.Model;
using SabberStoneCore.Tasks.PlayerTasks;

namespace HsEngine.Search;

/// <param name="IsLethal">True when some sequence of this turn's actions kills the opponent.</param>
/// <param name="OptionPath">Indices into <c>CurrentPlayer.Options()</c> per step; empty when not lethal.</param>
/// <param name="Line">Human-readable description of each step.</param>
/// <param name="NodesExpanded">Search nodes visited.</param>
/// <param name="Exhausted">True when the node budget ran out before the search space was covered.</param>
/// <param name="IsDeterministic">True when the lethal line kills without relying on RNG rolls.</param>
public sealed record LethalResult(
    bool IsLethal,
    IReadOnlyList<int> OptionPath,
    IReadOnlyList<string> Line,
    int NodesExpanded,
    bool Exhausted,
    bool IsDeterministic = false);

/// <summary>
/// Exact single-turn lethal search (ADR-0004): depth-first over the current player's actions,
/// excluding end turn, with a transposition table keyed by <see cref="StateHasher"/>.
/// Assumes deterministic effects; random outcomes follow the game's fixed seed and set <see cref="LethalResult.IsDeterministic"/> to false.
/// </summary>
public sealed class LethalSolver(int maxNodes = 200_000)
{
    public LethalResult Solve(Game game)
    {
        var search = new Search(maxNodes);
        bool found = search.Visit(game.DeterministicClone());
        int[] path = found ? search.Path.ToArray() : [];
        bool deterministic = found && IsDeterministicLine(game, path);

        return new LethalResult(
            found,
            path,
            found ? search.Line.ToArray() : [],
            search.Nodes,
            search.Exhausted,
            deterministic);
    }

    private static bool IsDeterministicLine(Game root, int[] path)
    {
        foreach (long seed in (ReadOnlySpan<long>)[17L, 999_983L])
        {
            Game copy = root.CloneWithSeed(seed);
            bool randomFired = false;
            copy.RandomHappenedEvent += (_, _) => randomFired = true;

            foreach (int optionIndex in path)
            {
                List<PlayerTask> opts = copy.CurrentPlayer.Options();
                if ((uint)optionIndex >= (uint)opts.Count || !copy.Process(opts[optionIndex]))
                    return false;
            }

            if (randomFired || !copy.OpponentDefeated())
                return false;
        }
        return true;
    }

    private sealed class Search(int maxNodes)
    {
        private readonly HashSet<ulong> _visited = [];

        public List<int> Path { get; } = [];
        public List<string> Line { get; } = [];
        public int Nodes { get; private set; }
        public bool Exhausted { get; private set; }

        public bool Visit(Game game)
        {
            if (game.OpponentDefeated())
                return true;
            if (game.State == State.COMPLETE)
                return false;
            if (Nodes >= maxNodes)
            {
                Exhausted = true;
                return false;
            }
            Nodes++;

            if (!_visited.Add(StateHasher.Compute(game)))
                return false;

            List<PlayerTask> options = game.CurrentPlayer.Options();
            for (int i = 0; i < options.Count; i++)
            {
                if (options[i].PlayerTaskType is PlayerTaskType.END_TURN or PlayerTaskType.CONCEDE
                    || !BoardPlacement.IsCanonicalOption(options[i]))
                    continue;

                // Options are recomputed on the child so the task binds to the child's entities.
                Game child = game.DeterministicClone();
                PlayerTask task = child.CurrentPlayer.Options()[i];
                if (!child.Process(task))
                    continue;

                Path.Add(i);
                Line.Add(task.ToString());
                if (Visit(child))
                    return true;
                Path.RemoveAt(Path.Count - 1);
                Line.RemoveAt(Line.Count - 1);

                if (Exhausted)
                    return false;
            }
            return false;
        }
    }
}
