using SabberStoneCore.Enums;
using SabberStoneCore.Model;
using SabberStoneCore.Tasks.PlayerTasks;

namespace HsEngine.Simulation;

public static class GameExtensions
{
    /// <summary>Deep copy that keeps the random seed, so the copy replays exactly like the original.</summary>
    public static Game DeterministicClone(this Game game) =>
        game.Clone(logging: false, resetRandomSeed: false, history: false);

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
