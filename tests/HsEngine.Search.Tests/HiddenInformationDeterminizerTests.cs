using HsEngine.Evaluation;
using HsEngine.Search;
using HsEngine.Simulation;
using SabberStoneCore.Enums;
using SabberStoneCore.Model;
using SabberStoneCore.Model.Entities;

namespace HsEngine.Search.Tests;

public class HiddenInformationDeterminizerTests
{
    /// <summary>A mirror match a few turns in, so hands, decks and boards are all non-trivial.</summary>
    private static Game Observed()
    {
        Game game = GameFactory.CreateWithDecks(CardClass.MAGE, Decks.BasicMage, CardClass.MAGE, Decks.BasicMage, seed: 5);
        var policy = new GreedyOpponentPolicy(new BoardEvaluator());
        var rng = new Random(0);
        for (int i = 0; i < 6 && game.State != State.COMPLETE; i++)
            policy.PlayTurn(game, rng);
        return game;
    }

    private static Controller Me(Game g) => g.CurrentPlayer;
    private static Controller Them(Game g) => g.CurrentOpponent;
    private static List<string> Ids(IEnumerable<IPlayable> zone) => zone.Select(c => c.Card.Id).ToList();

    [Fact]
    public void Keeps_everything_the_searching_player_can_see()
    {
        Game observed = Observed();
        Game world = new HiddenInformationDeterminizer().Sample(observed, new Random(1));

        Assert.Equal(Ids(Me(observed).HandZone), Ids(Me(world).HandZone));
        Assert.Equal(Ids(Me(observed).BoardZone), Ids(Me(world).BoardZone));
        Assert.Equal(Ids(Them(observed).BoardZone), Ids(Them(world).BoardZone));
        Assert.Equal(Them(observed).HandZone.Count, Them(world).HandZone.Count);
        Assert.Equal(Them(observed).DeckZone.Count, Them(world).DeckZone.Count);
        Assert.Equal(Me(observed).DeckZone.Count, Me(world).DeckZone.Count);
        Assert.Equal(Me(observed).Hero.Health, Me(world).Hero.Health);
        Assert.Equal(Them(observed).Hero.Health, Them(world).Hero.Health);
    }

    [Fact]
    public void Redistributes_only_the_opponents_hidden_cards()
    {
        Game observed = Observed();
        Game world = new HiddenInformationDeterminizer().Sample(observed, new Random(2));

        Assert.Equal(
            Ids(Them(observed).HandZone.Concat(Them(observed).DeckZone)).Order(),
            Ids(Them(world).HandZone.Concat(Them(world).DeckZone)).Order());
        Assert.Equal(Ids(Me(observed).DeckZone).Order(), Ids(Me(world).DeckZone).Order());
    }

    [Fact]
    public void Samples_vary_the_opponent_hand_and_both_deck_orders()
    {
        Game observed = Observed();
        var determinizer = new HiddenInformationDeterminizer();
        var rng = new Random(3);
        List<Game> worlds = Enumerable.Range(0, 30).Select(_ => determinizer.Sample(observed, rng)).ToList();

        Assert.True(worlds.Select(w => string.Join(",", Ids(Them(w).HandZone).Order())).Distinct().Count() > 1);
        Assert.True(worlds.Select(w => string.Join(",", Ids(Them(w).DeckZone))).Distinct().Count() > 1);
        Assert.True(worlds.Select(w => string.Join(",", Ids(Me(w).DeckZone))).Distinct().Count() > 1);
    }

    [Fact]
    public void Same_random_state_gives_same_world()
    {
        Game observed = Observed();

        Assert.Equal(
            StateHasher.Compute(new HiddenInformationDeterminizer().Sample(observed, new Random(9))),
            StateHasher.Compute(new HiddenInformationDeterminizer().Sample(observed, new Random(9))));
    }

    [Fact]
    public void Sampled_worlds_remain_playable()
    {
        Game observed = Observed();
        Game world = new HiddenInformationDeterminizer().Sample(observed, new Random(4));
        var policy = new GreedyOpponentPolicy(new BoardEvaluator());

        for (int i = 0; i < 6 && world.State != State.COMPLETE; i++)
            policy.PlayTurn(world, new Random(i));

        Assert.True(world.Turn > observed.Turn);
    }

    [Fact]
    public void Does_not_mutate_the_observed_game()
    {
        Game observed = Observed();
        ulong before = StateHasher.Compute(observed);

        new HiddenInformationDeterminizer().Sample(observed, new Random(5));

        Assert.Equal(before, StateHasher.Compute(observed));
    }
}
