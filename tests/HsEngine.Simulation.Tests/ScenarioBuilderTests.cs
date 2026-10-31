using HsEngine.Simulation;
using SabberStoneCore.Model;

namespace HsEngine.Simulation.Tests;

public class ScenarioBuilderTests
{
    [Fact]
    public void Builds_requested_position()
    {
        Game game = new ScenarioBuilder()
            .WithMana(4)
            .WithOpponentHero(health: 7, armor: 2)
            .WithHand("Fireball")
            .WithBoard("Bloodfen Raptor")
            .WithOpponentBoard("Goldshire Footman")
            .Build();

        Assert.Equal(4, game.CurrentPlayer.RemainingMana);
        Assert.Equal(30, game.CurrentPlayer.Hero.Health);
        Assert.Equal(7, game.CurrentOpponent.Hero.Health);
        Assert.Equal(2, game.CurrentOpponent.Hero.Armor);
        Assert.Equal(["Fireball"], game.CurrentPlayer.HandZone.Select(c => c.Card.Name));
        Assert.Equal(["Bloodfen Raptor"], game.CurrentPlayer.BoardZone.Select(m => m.Card.Name));
        Assert.Equal(["Goldshire Footman"], game.CurrentOpponent.BoardZone.Select(m => m.Card.Name));
        Assert.True(game.CurrentPlayer.BoardZone[0].CanAttack);
    }

    [Fact]
    public void Next_empty_deck_draw_deals_one_fatigue()
    {
        Game game = new ScenarioBuilder().WithOpponentHero(5).Build();

        game.ApplyOptionPath([game.CurrentPlayer.Options().FindIndex(o => o.PlayerTaskType == SabberStoneCore.Tasks.PlayerTasks.PlayerTaskType.END_TURN)]);

        Assert.Equal(4, game.Player2.Hero.Health);
    }

    [Fact]
    public void Opponent_hand_is_exactly_what_was_requested()
    {
        Game game = new ScenarioBuilder().WithOpponentHand("Fireball").Build();

        Assert.Equal(["Fireball"], game.CurrentOpponent.HandZone.Select(c => c.Card.Name));
    }

    [Fact]
    public void Unknown_card_name_fails_fast()
    {
        Assert.Throws<ArgumentException>(() => new ScenarioBuilder().WithHand("Not A Card").Build());
    }
}
