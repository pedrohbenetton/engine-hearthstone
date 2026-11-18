using HsEngine.Simulation;
using SabberStoneCore.Enums;
using SabberStoneCore.Model;
using SabberStoneCore.Model.Entities;
using SabberStoneCore.Tasks.PlayerTasks;

namespace HsEngine.Simulation.Tests;

public class CardEffectCompilerTests
{
    public CardEffectCompilerTests() => ProxyCards.EnsureRegistered();

    [Fact]
    public void Hammer_of_Wrath_deals_three_damage_and_draws_at_its_core_cost()
    {
        Game game = new ScenarioBuilder()
            .WithMana(3)
            .WithHand("CORE_CS2_094")
            .WithDeck("Chillwind Yeti")
            .WithOpponentBoard("River Crocolisk")
            .Build();

        PlayerTask castOnCrocolisk = game.CurrentPlayer.Options()
            .First(t => t is PlayCardTask { Target: Minion { Card.Name: "River Crocolisk" } });

        Assert.True(game.Process(castOnCrocolisk));
        Assert.Empty(game.CurrentOpponent.BoardZone);
        Assert.Single(game.CurrentPlayer.HandZone);
        Assert.Equal("Chillwind Yeti", game.CurrentPlayer.HandZone[0].Card.Name);
    }

    [Fact]
    public void Taelan_Fordring_deathrattle_draws_highest_cost_minion()
    {
        Assert.False(ProxyCards.IsProxy("CS3_024"));

        Game game = new ScenarioBuilder()
            .WithMana(5)
            .WithHand("CS3_024", "Fireball")
            .WithDeck("Bloodfen Raptor", "Boulderfist Ogre", "Fireball")
            .Build();

        // Play Taelan (5/3/3 Divine Shield Taunt), then destroy him via a test kill to trigger Deathrattle.
        Assert.True(game.Process(PlayCardTask.Minion(game.CurrentPlayer, game.CurrentPlayer.HandZone[0])));
        Minion taelan = Assert.Single(game.CurrentPlayer.BoardZone);
        Assert.True(taelan.HasTaunt && taelan.HasDivineShield && taelan.HasDeathrattle);

        taelan.Destroy();
        game.DeathProcessingAndAuraUpdate();

        Assert.Contains(game.CurrentPlayer.HandZone, c => c.Card.Name == "Boulderfist Ogre");
    }

    [Fact]
    public void Ysera_Emerald_Aspect_battlecry_grants_three_full_mana_crystals()
    {
        Assert.False(ProxyCards.IsProxy("EDR_000"));

        Game game = new ScenarioBuilder()
            .WithMana(9)
            .WithHand("EDR_000")
            .Build();

        Assert.True(game.Process(PlayCardTask.Minion(game.CurrentPlayer, game.CurrentPlayer.HandZone[0])));
        Assert.Equal(3, game.CurrentPlayer.RemainingMana);
    }

    [Fact]
    public void Compiles_custom_standard_buff_damage_freeze_and_battlecry_cards()
    {
        ProxyCards.Register(
        [
            new CardData("TST_BUFF_1", 990001, "Holy Blessing", "SPELL", "TST", Cost: 2, CardClass: "PALADIN",
                Text: "Give a friendly minion +2/+3 and <b>Taunt</b>."),
            new CardData("TST_BC_DMG", 990002, "Flame Archer", "MINION", "TST", Cost: 3, Attack: 3, Health: 2, CardClass: "NEUTRAL",
                Mechanics: ["BATTLECRY"], Text: "<b>Battlecry:</b> Deal 2 damage to an enemy."),
            new CardData("TST_FREEZE_AOE", 990003, "Frost Wave", "SPELL", "TST", Cost: 3, CardClass: "MAGE",
                Text: "Freeze all enemy minions. Draw a card."),
        ]);

        Assert.False(ProxyCards.IsProxy("TST_BUFF_1"));
        Assert.False(ProxyCards.IsProxy("TST_BC_DMG"));
        Assert.False(ProxyCards.IsProxy("TST_FREEZE_AOE"));

        Game game = new ScenarioBuilder()
            .WithMana(10)
            .WithHand("TST_BUFF_1", "TST_BC_DMG", "TST_FREEZE_AOE")
            .WithDeck("Chillwind Yeti")
            .WithBoard("Bloodfen Raptor")
            .WithOpponentBoard("River Crocolisk")
            .Build();

        // 1. Buff friendly Raptor (+2/+3 and Taunt -> 5/5 Taunt)
        PlayerTask buff = game.CurrentPlayer.Options().First(t => t is PlayCardTask { Source.Card.Id: "TST_BUFF_1" });
        Assert.True(game.Process(buff));
        Minion raptor = game.CurrentPlayer.BoardZone[0];
        Assert.Equal((5, 5, true), (raptor.AttackDamage, raptor.Health, raptor.HasTaunt));

        // 2. Play Flame Archer targeting enemy Crocolisk (3 -> 1 HP)
        PlayerTask archer = game.CurrentPlayer.Options()
            .First(t => t is PlayCardTask { Source.Card.Id: "TST_BC_DMG", Target: Minion });
        Assert.True(game.Process(archer));
        Assert.Equal(1, game.CurrentOpponent.BoardZone[0].Health);

        // 3. Cast Frost Wave -> freezes enemy Crocolisk and draws Chillwind Yeti
        PlayerTask freeze = game.CurrentPlayer.Options().First(t => t is PlayCardTask { Source.Card.Id: "TST_FREEZE_AOE" });
        Assert.True(game.Process(freeze));
        Assert.True(game.CurrentOpponent.BoardZone[0].IsFrozen);
        Assert.Contains(game.CurrentPlayer.HandZone, c => c.Card.Name == "Chillwind Yeti");
    }

    [Fact]
    public void Conditional_or_unknown_cards_stay_honest_proxies()
    {
        // JAIL_328 has "If your deck has no Neutral cards..." — must NOT be miscompiled.
        Assert.True(ProxyCards.IsProxy("JAIL_328"));
        Assert.Null(Cards.FromId("JAIL_328").Power);
    }
}
