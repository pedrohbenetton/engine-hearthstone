using HsEngine.Simulation;
using SabberStoneCore.Enums;
using SabberStoneCore.Model;
using SabberStoneCore.Tasks.PlayerTasks;

namespace HsEngine.Simulation.Tests;

public class DeterminizationTests
{
    private static ulong PlayArcaneMissiles(long seed)
    {
        Game game = new ScenarioBuilder().WithMana(1).WithHand("Arcane Missiles")
            .WithOpponentBoard("Bloodfen Raptor", "Goldshire Footman", "Wolfrider").Build()
            .CloneWithSeed(seed);
        PlayerTask play = game.CurrentPlayer.Options().First(o => o.PlayerTaskType == PlayerTaskType.PLAY_CARD);
        Assert.True(game.Process(play));
        return StateHasher.Compute(game);
    }

    [Fact]
    public void Same_seed_reproduces_random_effects() =>
        Assert.Equal(PlayArcaneMissiles(42), PlayArcaneMissiles(42));

    [Fact]
    public void Different_seeds_sample_different_outcomes() =>
        Assert.True(Enumerable.Range(0, 20).Select(s => PlayArcaneMissiles(s)).Distinct().Count() > 1);
}
