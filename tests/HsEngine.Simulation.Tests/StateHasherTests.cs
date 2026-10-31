using HsEngine.Simulation;
using SabberStoneCore.Enums;
using SabberStoneCore.Model;
using SabberStoneCore.Tasks.PlayerTasks;

namespace HsEngine.Simulation.Tests;

public class StateHasherTests
{
    [Theory, MemberData(nameof(CloneInvariantTests.Midgames), MemberType = typeof(CloneInvariantTests))]
    public void Clone_hashes_equal(long seed, int actions)
    {
        Game game = GameFactory.CreateMidgame(seed, actions);

        Assert.Equal(StateHasher.Compute(game), StateHasher.Compute(game.DeterministicClone()));
    }

    [Theory, MemberData(nameof(CloneInvariantTests.Midgames), MemberType = typeof(CloneInvariantTests))]
    public void Same_seed_and_actions_hash_equal(long seed, int actions)
    {
        Assert.Equal(
            StateHasher.Compute(GameFactory.CreateMidgame(seed, actions)),
            StateHasher.Compute(GameFactory.CreateMidgame(seed, actions)));
    }

    [Fact]
    public void Identical_cards_in_hand_do_not_cancel_out()
    {
        Game one = new ScenarioBuilder().WithHand("Bloodfen Raptor").Build();
        Game three = new ScenarioBuilder().WithHand("Bloodfen Raptor", "Bloodfen Raptor", "Bloodfen Raptor").Build();

        Assert.NotEqual(StateHasher.Compute(one), StateHasher.Compute(three));
    }

    [Fact]
    public void Buff_placement_changes_hash()
    {
        Game start = new ScenarioBuilder()
            .WithBoard("Bloodfen Raptor", "Bloodfen Raptor").WithHand("Abusive Sergeant").Build();
        Game buffFirst = start.DeterministicClone();
        buffFirst.ApplyOptionPath([PlayCardOption(buffFirst, "Abusive Sergeant", targetBoardIndex: 0)]);
        Game buffSecond = start.DeterministicClone();
        buffSecond.ApplyOptionPath([PlayCardOption(buffSecond, "Abusive Sergeant", targetBoardIndex: 1)]);

        Assert.NotEqual(StateHasher.Compute(buffFirst), StateHasher.Compute(buffSecond));
    }

    /// <summary>
    /// Soundness for transposition tables: siblings that SabberStone's full string hash tells apart must not collide.
    /// A collision would make a search prune a genuinely different state.
    /// </summary>
    [Theory]
    [InlineData(11)]
    [InlineData(12)]
    [InlineData(13)]
    public void Distinguishes_every_sibling_the_reference_hash_distinguishes(long seed)
    {
        Game game = GameFactory.CreateStarted(seed);
        var rng = new Random((int)seed);
        int compared = 0;

        for (int step = 0; step < 150 && game.State != State.COMPLETE; step++)
        {
            var byOurs = new Dictionary<ulong, string>();
            List<PlayerTask> options = game.CurrentPlayer.Options();
            for (int i = 0; i < options.Count; i++)
            {
                Game child = game.DeterministicClone();
                if (!child.Process(child.CurrentPlayer.Options()[i]))
                    continue;

                string reference = child.Hash();
                ulong ours = StateHasher.Compute(child);
                if (byOurs.TryGetValue(ours, out string? seen))
                    Assert.Equal(seen, reference);
                byOurs[ours] = reference;
                compared++;
            }
            game.Process(options[rng.Next(options.Count)]);
        }

        Assert.True(compared > 100, $"only {compared} siblings compared");
    }

    private static int PlayCardOption(Game game, string cardName, int targetBoardIndex)
    {
        int index = game.CurrentPlayer.Options().FindIndex(o =>
            o.PlayerTaskType == PlayerTaskType.PLAY_CARD &&
            o.Source.Card.Name == cardName &&
            o.Target == game.CurrentPlayer.BoardZone[targetBoardIndex]);
        Assert.True(index >= 0, $"no option plays {cardName} on board[{targetBoardIndex}]");
        return index;
    }
}
