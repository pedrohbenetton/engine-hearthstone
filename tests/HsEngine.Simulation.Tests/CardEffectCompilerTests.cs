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
        // Test CardEffectCompiler directly without mutating SabberStone's process-wide card table mid-run.
        CompiledCardEffect buff = CardEffectCompiler.TryCompile(
            new CardData("TST_BUFF_1", 990001, "Holy Blessing", "SPELL", "TST", Cost: 2, CardClass: "PALADIN",
                Text: "Give a friendly minion +2/+3 and <b>Taunt</b>."),
            CardType.SPELL)!;
        CompiledCardEffect archer = CardEffectCompiler.TryCompile(
            new CardData("TST_BC_DMG", 990002, "Flame Archer", "MINION", "TST", Cost: 3, Attack: 3, Health: 2, CardClass: "NEUTRAL",
                Mechanics: ["BATTLECRY"], Text: "<b>Battlecry:</b> Deal 2 damage to an enemy."),
            CardType.MINION)!;
        CompiledCardEffect freeze = CardEffectCompiler.TryCompile(
            new CardData("TST_FREEZE_AOE", 990003, "Frost Wave", "SPELL", "TST", Cost: 3, CardClass: "MAGE",
                Text: "Freeze all enemy minions. Draw a card."),
            CardType.SPELL)!;

        Assert.NotNull(buff.Power?.PowerTask);
        Assert.Contains(PlayReq.REQ_FRIENDLY_TARGET, buff.PlayRequirements.Keys);
        Assert.Contains(PlayReq.REQ_TARGET_IF_AVAILABLE, archer.PlayRequirements.Keys);
        Assert.NotNull(freeze.Power?.PowerTask);

        Game game = new ScenarioBuilder()
            .WithMana(10)
            .WithDeck("Chillwind Yeti")
            .WithBoard("Bloodfen Raptor")
            .WithOpponentBoard("River Crocolisk")
            .Build();

        Controller me = game.CurrentPlayer;
        Minion raptor = me.BoardZone[0];
        Minion enemyCrocolisk = game.CurrentOpponent.BoardZone[0];

        buff.Power!.PowerTask.Process(game, me, me.Hero, raptor);
        Assert.Equal((5, 5, true), (raptor.AttackDamage, raptor.Health, raptor.HasTaunt));

        archer.Power!.PowerTask.Process(game, me, raptor, enemyCrocolisk);
        Assert.Equal(1, enemyCrocolisk.Health);

        freeze.Power!.PowerTask.Process(game, me, me.Hero, null);
        Assert.True(enemyCrocolisk.IsFrozen);
        Assert.Contains(me.HandZone, c => c.Card.Name == "Chillwind Yeti");
    }

    [Fact]
    public void Conditional_or_unknown_cards_stay_honest_proxies()
    {
        // JAIL_328 has "If your deck has no Neutral cards..." — must NOT be miscompiled.
        Assert.True(ProxyCards.IsProxy("JAIL_328"));
        Assert.Null(Cards.FromId("JAIL_328").Power);
    }
}
