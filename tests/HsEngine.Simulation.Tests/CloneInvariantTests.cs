using HsEngine.Simulation;
using SabberStoneCore.Enums;
using SabberStoneCore.Model;
using SabberStoneCore.Tasks.PlayerTasks;

namespace HsEngine.Simulation.Tests;

public class CloneInvariantTests
{
    public static TheoryData<long, int> Midgames => new()
    {
        { 1, 0 }, { 2, 15 }, { 3, 40 }, { 4, 80 }, { 5, 120 },
    };

    [Theory, MemberData(nameof(Midgames))]
    public void Clone_has_identical_state(long seed, int actions)
    {
        Game game = GameFactory.CreateMidgame(seed, actions);

        Assert.Equal(game.Hash(), game.DeterministicClone().Hash());
    }

    [Theory, MemberData(nameof(Midgames))]
    public void Mutating_clone_leaves_original_untouched(long seed, int actions)
    {
        Game game = GameFactory.CreateMidgame(seed, actions);
        string before = game.Hash();

        Game clone = game.DeterministicClone();
        for (int i = 0; i < 10 && clone.State != State.COMPLETE; i++)
            clone.Process(clone.CurrentPlayer.Options()[0]);

        Assert.Equal(before, game.Hash());
    }

    [Theory, MemberData(nameof(Midgames))]
    public void Same_seed_and_actions_reproduce_same_state(long seed, int actions)
    {
        Assert.Equal(
            GameFactory.CreateMidgame(seed, actions).Hash(),
            GameFactory.CreateMidgame(seed, actions).Hash());
    }

    [Theory, MemberData(nameof(Midgames))]
    public void Deterministic_clone_replays_like_original(long seed, int actions)
    {
        Game game = GameFactory.CreateMidgame(seed, actions);
        Game clone = game.DeterministicClone();

        // SabberStone can list options that Process then rejects (seen: Warsong Commander attacks);
        // the invariant is that original and clone agree, rejections included.
        for (int i = 0; i < 10 && game.State != State.COMPLETE; i++)
        {
            List<PlayerTask> options = game.CurrentPlayer.Options();
            List<PlayerTask> cloneOptions = clone.CurrentPlayer.Options();
            Assert.Equal(options.Count, cloneOptions.Count);

            int pick = i % options.Count;
            Assert.Equal(game.Process(options[pick]), clone.Process(cloneOptions[pick]));
        }

        Assert.Equal(game.Hash(), clone.Hash());
    }
}
