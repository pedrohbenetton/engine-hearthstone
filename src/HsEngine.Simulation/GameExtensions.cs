using System.Reflection;
using SabberStoneCore.Enums;
using SabberStoneCore.Model;
using SabberStoneCore.Tasks.PlayerTasks;

namespace HsEngine.Simulation;

public static class GameExtensions
{
    /// <summary>Deep copy that keeps the random seed, so the copy replays exactly like the original.</summary>
    public static Game DeterministicClone(this Game game) =>
        game.Clone(logging: false, resetRandomSeed: false, history: false);

    /// <summary>
    /// Deep copy whose random effects follow <paramref name="seed"/>: one sampled world per seed, reproducibly.
    /// SabberStone only offers a fresh unseeded RNG on clone and keeps <c>Game.Random</c> internal,
    /// so the setter is bound once by reflection rather than patching the submodule.
    /// </summary>
    public static Game CloneWithSeed(this Game game, long seed)
    {
        Game clone = game.DeterministicClone();
        SetRandom(clone, new Util.DeepCloneableRandom(seed));
        return clone;
    }

    private static readonly Action<Game, Util.DeepCloneableRandom> SetRandom =
        typeof(Game).GetProperty("Random", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetMethod!.CreateDelegate<Action<Game, Util.DeepCloneableRandom>>();

    private static readonly Func<Game, Util.DeepCloneableRandom> GetRandom =
        typeof(Game).GetProperty("Random", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetMethod!.CreateDelegate<Func<Game, Util.DeepCloneableRandom>>();

    private static readonly Action<Game, bool> RaiseRandomHappened =
        typeof(Game).GetMethod("OnRandomHappened", BindingFlags.Instance | BindingFlags.NonPublic)!
            .CreateDelegate<Action<Game, bool>>();

    /// <summary>Draws a reproducible integer in <c>[0, maxExclusive)</c> from <paramref name="game"/>'s RNG and flags randomness.</summary>
    public static int NextRandomInt(this Game game, int maxExclusive)
    {
        if (maxExclusive <= 1)
            return 0;
        RaiseRandomHappened(game, true);
        return GetRandom(game).Next(0, maxExclusive);
    }

    public static bool OpponentDefeated(this Game game) =>
        game.CurrentOpponent.Hero.IsDead || game.CurrentOpponent.PlayState is PlayState.LOSING or PlayState.LOST;

    /// <summary>
    /// Applies a line given as indices into <c>CurrentPlayer.Options()</c> at each step.
    /// Indices are stable across deterministic clones, unlike <see cref="PlayerTask"/> references.
    /// </summary>
    public static void ApplyOptionPath(this Game game, IEnumerable<int> path)
    {
        foreach (int index in path)
        {
            List<PlayerTask> options = game.CurrentPlayer.Options();
            if (!game.Process(options[index]))
                throw new InvalidOperationException($"Option {index} ({options[index]}) was rejected.");
        }
    }
}
