using HsEngine.Ingestion;
using HsEngine.Simulation;
using SabberStoneCore.Enums;
using SabberStoneCore.Model;
using SabberStoneCore.Model.Entities;

namespace HsEngine.Simulation.Tests;

/// <summary>Observed state of Fixtures/standard-game1.log (main action of turn 9) rebuilt as a SabberStone game.</summary>
public class ObservedGameBuilderTests
{
    internal static ObservedState ObservedAtMainAction(int turn)
    {
        var state = new GameStateMachine();
        foreach (PowerEvent e in PowerLogParser.Parse(File.ReadLines(Path.Combine(AppContext.BaseDirectory, "Fixtures", "standard-game1.log"))))
        {
            state.Apply(e);
            if (e is TagChange { Tag: "STEP", Value: "MAIN_ACTION" } && state.Turn == turn)
                break;
        }
        return Observation.From(state);
    }

    private static readonly Lazy<ObservedState> Turn9 = new(() => ObservedAtMainAction(9));

    [Fact]
    public void Local_player_is_to_act_with_the_observed_resources()
    {
        Game game = ObservedGameBuilder.Build(Turn9.Value).Game;
        Controller me = game.CurrentPlayer;

        Assert.Equal(CardClass.PALADIN, me.HeroClass);
        Assert.Equal(CardClass.DRUID, game.CurrentOpponent.HeroClass);
        Assert.Equal(5, me.RemainingMana);
        Assert.False(me.Hero.HeroPower.IsExhausted);
        Assert.Equal((30, 29), (me.Hero.Health, game.CurrentOpponent.Hero.Health));
        Assert.Equal((32, 17), (me.DeckZone.Count, game.CurrentOpponent.DeckZone.Count));
    }

    [Fact]
    public void Board_hand_and_weapon_match_the_observation()
    {
        Game game = ObservedGameBuilder.Build(Turn9.Value).Game;
        Controller me = game.CurrentPlayer;

        Minion bruiser = Assert.Single(me.BoardZone.GetAll());
        Assert.Equal(("JAIL_328", 2, 2), (bruiser.Card.Id, bruiser.AttackDamage, bruiser.Health));
        Assert.True(bruiser.CanAttack);
        Assert.Equal(["CATA_474", "TIME_009", "CS3_024", "CORE_CS2_094", "TIME_700", "TIME_009t1"], me.HandZone.Select(c => c.Card.Id));
        Assert.Equal([4, 8, 5, 1, 3, 4], me.HandZone.Select(c => c.Cost));
        Assert.Equal((2, 1), (me.Hero.Weapon.AttackDamage, me.Hero.Weapon.Durability));
        Assert.Equal(["CATA_131"], game.CurrentOpponent.BoardZone.Select(m => m.Card.Id));
    }

    [Fact]
    public void Opponent_hidden_hand_cards_are_placeholders()
    {
        Game game = ObservedGameBuilder.Build(Turn9.Value).Game;

        Assert.Equal(
            [ProxyCards.HiddenCardId, ProxyCards.HiddenCardId, ProxyCards.HiddenCardId, "EDR_000", ProxyCards.HiddenCardId, ProxyCards.HiddenCardId],
            game.CurrentOpponent.HandZone.Select(c => c.Card.Id));
        Assert.All(game.CurrentOpponent.DeckZone, c => Assert.Equal(ProxyCards.HiddenCardId, c.Card.Id));
    }

    [Fact]
    public void Observed_stats_override_the_card_definition()
    {
        ObservedState observed = Turn9.Value;
        ObservedMinion buffed = observed.Me.Board[0] with { Attack = 5, Health = 7, Damage = 3, Keywords = ["TAUNT"], Exhausted = true };
        observed = observed with { Me = observed.Me with { Board = [buffed] } };

        Minion minion = ObservedGameBuilder.Build(observed).Game.CurrentPlayer.BoardZone[0];

        Assert.Equal((5, 4, true, false), (minion.AttackDamage, minion.Health, minion.HasTaunt, minion.CanAttack));
    }

    [Fact]
    public void Report_counts_proxies_and_hidden_cards()
    {
        ConversionReport report = ObservedGameBuilder.Build(Turn9.Value).Report;

        Assert.Contains("JAIL_328", report.Proxies);
        // CORE_CS2_094 (Hammer of Wrath), CS3_024 (Taelan Fordring) and EDR_000 (Ysera) now have real effects!
        Assert.DoesNotContain("CORE_CS2_094", report.Proxies);
        Assert.DoesNotContain("CS3_024", report.Proxies);
        Assert.DoesNotContain("EDR_000", report.Proxies);
        Assert.Empty(report.Unsupported);
        Assert.Equal(5 + 32 + 17, report.HiddenCards);
    }

    [Fact]
    public void Refuses_a_position_where_the_opponent_is_to_act()
    {
        ObservedState observed = Turn9.Value with { CurrentPlayerId = 2 };

        Assert.Throws<ArgumentException>(() => ObservedGameBuilder.Build(observed));
    }

    [Fact]
    public void Activates_known_local_secret_and_preserves_fatigue_counters()
    {
        ObservedState observed = Turn9.Value with
        {
            Me = Turn9.Value.Me with { SecretCount = 1, Secrets = ["EX1_289"], Fatigue = 2 },
            Opponent = Turn9.Value.Opponent with { SecretCount = 1, Secrets = [null], Fatigue = 5 },
        };

        Game game = ObservedGameBuilder.Build(observed).Game;
        Assert.Equal((2, 5), (game.CurrentPlayer.Hero.Fatigue, game.CurrentOpponent.Hero.Fatigue));
        Assert.Equal((1, 1), (game.CurrentPlayer.SecretZone.Count, game.CurrentOpponent.SecretZone.Count));

        // End our turn and let opponent's minion attack our hero: Ice Barrier (EX1_289) must trigger (+8 Armor)!
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.EndTurnTask.Any(game.CurrentPlayer)));
        var faceAttack = game.CurrentPlayer.Options().First(t => t.Target == game.CurrentOpponent.Hero);
        Assert.True(game.Process(faceAttack));
        Assert.True(game.Player1.Hero.Armor > 0);
        Assert.Equal(0, game.Player1.SecretZone.Count);
    }
}
