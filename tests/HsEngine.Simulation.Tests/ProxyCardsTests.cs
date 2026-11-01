using HsEngine.Simulation;
using SabberStoneCore.Enums;
using SabberStoneCore.Model;
using SabberStoneCore.Model.Entities;
using SabberStoneCore.Tasks.PlayerTasks;

namespace HsEngine.Simulation.Tests;

/// <summary>
/// ADR-0008: cards SabberStone does not know become proxies with stats and keywords from the HearthstoneJSON
/// snapshot (data/cards). The ids below come from real Standard games (Power.log of 2026-10-31).
/// </summary>
public class ProxyCardsTests
{
    public ProxyCardsTests() => ProxyCards.EnsureRegistered();

    [Fact]
    public void Snapshot_loads_the_engine_fields()
    {
        CardData chromatus = CardSnapshot.Default.Single(c => c.Id == "CATA_432");

        Assert.Equal(("MINION", 8, 8, 8, "PALADIN"), (chromatus.Type, chromatus.Cost, chromatus.Attack, chromatus.Health, chromatus.CardClass));
        Assert.Contains("ELUSIVE", chromatus.Mechanics!);
    }

    [Fact]
    public void Unknown_minion_becomes_a_proxy_with_stats_and_keywords()
    {
        Card card = Cards.FromId("CATA_432"); // Chromatus: Taunt, Divine Shield, Lifesteal, Elusive

        Assert.True(ProxyCards.IsProxy("CATA_432"));
        Assert.Equal((CardType.MINION, 8, 8, 8), (card.Type, card.Cost, card.ATK, card.Health));
        Assert.Equal("Chromatus", card.Name);
        Assert.Equal(CardClass.PALADIN, card.Class);
        Assert.True(card.Taunt && card.DivineShield && card.LifeSteal && card.CantBeTargetedBySpells);
        Assert.Equal(1, card[GameTag.CANT_BE_TARGETED_BY_HERO_POWERS]);
        Assert.Null(card.Power);
    }

    [Fact]
    public void Rush_minion_can_attack_minions_the_turn_it_is_played()
    {
        Game game = new ScenarioBuilder().WithMana(1).WithHand("CAP_004").WithOpponentBoard("Bloodfen Raptor").Build();

        game.Process(PlayCardTask.Minion(game.CurrentPlayer, game.CurrentPlayer.HandZone[0]));
        Minion operative = game.CurrentPlayer.BoardZone[0];

        Assert.Equal(("CAP_004", 4, 2), (operative.Card.Id, operative.AttackDamage, operative.Health));
        Assert.True(operative.CanAttack);
    }

    [Fact]
    public void Proxy_spell_is_playable_and_has_no_effect()
    {
        Game game = new ScenarioBuilder().WithMana(4).WithHand("CATA_479").WithOpponentBoard("Bloodfen Raptor").Build();

        game.Process(PlayCardTask.Spell(game.CurrentPlayer, game.CurrentPlayer.HandZone[0]));

        Assert.Empty(game.CurrentPlayer.HandZone);
        Assert.Equal(0, game.CurrentPlayer.RemainingMana);
        Assert.Equal(2, game.CurrentOpponent.BoardZone[0].Health);
    }

    [Fact]
    public void Proxy_weapon_keeps_its_durability()
    {
        Game game = new ScenarioBuilder().WithMana(7).WithHand("JAIL_329").Build();

        game.Process(PlayCardTask.Any(game.CurrentPlayer, game.CurrentPlayer.HandZone[0]));
        Weapon weapon = game.CurrentPlayer.Hero.Weapon;

        Assert.Equal((3, 3), (weapon.AttackDamage, weapon.Durability));
    }

    [Fact]
    public void Core_reprint_with_unchanged_stats_reuses_the_implemented_card()
    {
        Card reprint = Cards.FromId("CORE_EX1_012"); // Bloodmage Thalnos, same 2-mana 1/1 as EX1_012

        Assert.False(ProxyCards.IsProxy("CORE_EX1_012"));
        Assert.Equal("CORE_EX1_012", reprint.Id);
        Assert.Same(Cards.FromId("EX1_012").Power, reprint.Power);
    }

    [Fact]
    public void Core_reprint_with_changed_cost_is_a_proxy()
    {
        // Fan of Knives costs 2 in Core but 3 in SabberStone's EX1_129.
        Assert.True(ProxyCards.IsProxy("CORE_EX1_129"));
        Assert.Equal(2, Cards.FromId("CORE_EX1_129").Cost);
    }

    [Fact]
    public void Implemented_cards_are_left_untouched()
    {
        Assert.False(ProxyCards.IsProxy("EX1_116"));
        Assert.NotNull(Cards.FromId("EX1_116").Power);
    }

    [Fact]
    public void Registration_is_idempotent()
    {
        ProxyRegistration again = ProxyCards.Register(CardSnapshot.Default);

        Assert.Equal(0, again.Proxied + again.Aliased);
    }

    [Fact]
    public void Playable_cards_from_the_recorded_games_resolve()
    {
        string[] seen = ["BE_228", "CATA_131", "CATA_472", "CATA_557", "EDR_000", "END_007", "JAIL_035", "TIME_009"];

        Assert.All(seen, id => Assert.Equal(id, Cards.FromId(id).Id));
    }
}
