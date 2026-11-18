using HsEngine.Ingestion;
using HsEngine.Search;
using HsEngine.Simulation;
using SabberStoneCore.Model;

namespace HsEngine.Search.Tests;

public class BayesianRangeTests
{
    [Fact]
    public void Built_in_catalog_produces_normalized_posteriors_for_every_class()
    {
        var estimator = new BayesianRangeEstimator();
        string[] classes =
        [
            "DEATHKNIGHT", "DEMONHUNTER", "DRUID", "HUNTER", "MAGE", "PALADIN",
            "PRIEST", "ROGUE", "SHAMAN", "WARLOCK", "WARRIOR",
        ];

        foreach (string cls in classes)
        {
            IReadOnlyList<ArchetypePosterior> posteriors = estimator.InferArchetypes(cls, []);
            Assert.NotEmpty(posteriors);
            Assert.Equal(1.0, posteriors.Sum(p => p.Probability), precision: 6);
        }
    }

    [Fact]
    public void Revealed_cards_shift_posterior_toward_matching_archetype_and_survive_off_meta_tech()
    {
        var aggro = new ArchetypeDefinition("Aggro Mage", "MAGE", 0.5,
            new DeckDefinition("Aggro Mage", "MAGE", new Dictionary<string, int> { ["EX1_277"] = 2, ["CS2_024"] = 2, ["NEW1_012"] = 2 }));
        var control = new ArchetypeDefinition("Control Mage", "MAGE", 0.5,
            new DeckDefinition("Control Mage", "MAGE", new Dictionary<string, int> { ["CS2_032"] = 2, ["CS2_028"] = 2, ["CS2_029"] = 2 }));

        var estimator = new BayesianRangeEstimator(new ArchetypeCatalog([aggro, control]));

        IReadOnlyList<ArchetypePosterior> afterFlamestrike = estimator.InferArchetypes("MAGE", ["CS2_032", "CS2_028", "TECH_CARD_999"]);

        Assert.Equal("Control Mage", afterFlamestrike[0].Archetype.Name);
        Assert.True(afterFlamestrike[0].Probability > 0.90, $"P = {afterFlamestrike[0].Probability}");
        Assert.Equal(1.0, afterFlamestrike.Sum(p => p.Probability), precision: 6);
    }

    [Fact]
    public void Mulligan_keep_favors_cheap_cards_and_old_hand_slots_favor_spells_over_cheap_minions()
    {
        ProxyCards.EnsureRegistered();
        Card twoDrop = Cards.FromName("Bloodfen Raptor");
        Card sevenCostSpell = Cards.FromName("Flamestrike");
        Card fourCostSpell = Cards.FromName("Fireball");

        var mulliganKept = new ObservedHandCard(null, null, TurnEnteredHand: 1, KeptInMulligan: true);
        Assert.True(BayesianRangeEstimator.SlotCardWeight(twoDrop, mulliganKept, currentTurn: 2)
                    > BayesianRangeEstimator.SlotCardWeight(sevenCostSpell, mulliganKept, currentTurn: 2));

        // Held from turn 2 until turn 11 (opponent had up to 5-6 mana across multiple turns without playing it):
        var longHeld = new ObservedHandCard(null, null, TurnEnteredHand: 2, KeptInMulligan: false);
        Assert.True(BayesianRangeEstimator.SlotCardWeight(fourCostSpell, longHeld, currentTurn: 11)
                    > BayesianRangeEstimator.SlotCardWeight(twoDrop, longHeld, currentTurn: 11));
    }

    [Fact]
    public void Range_determinizer_replaces_hidden_placeholders_with_playable_cards_and_keeps_revealed_slots()
    {
        var state = new GameStateMachine();
        foreach (PowerEvent e in PowerLogParser.Parse(File.ReadLines(Path.Combine(AppContext.BaseDirectory, "Fixtures", "standard-game1.log"))))
        {
            state.Apply(e);
            if (e is TagChange { Tag: "STEP", Value: "MAIN_ACTION" } && state.Turn == 9)
                break;
        }
        ObservedState observed = Observation.From(state);
        ConvertedGame converted = ObservedGameBuilder.Build(observed, seed: 1);

        var determinizer = new RangeDeterminizer(observed);
        Game world = determinizer.Sample(converted.Game, new Random(42));

        Assert.Equal("EDR_000", world.CurrentOpponent.HandZone[3].Card.Id);
        Assert.DoesNotContain(world.CurrentOpponent.HandZone, c => c.Card.Id == ProxyCards.HiddenCardId);
        Assert.DoesNotContain(world.CurrentOpponent.DeckZone, c => c.Card.Id == ProxyCards.HiddenCardId);
        Assert.DoesNotContain(world.CurrentPlayer.DeckZone, c => c.Card.Id == ProxyCards.HiddenCardId);
    }

    [Fact]
    public void Deterministic_lethal_is_distinguished_from_rng_lethal()
    {
        Game deterministic = new ScenarioBuilder()
            .WithMana(4)
            .WithOpponentHero(6)
            .WithHand("Fireball")
            .Build();
        LethalResult burn = new LethalSolver().Solve(deterministic);
        Assert.True(burn.IsLethal);
        Assert.True(burn.IsDeterministic);

        // Opponent has a 2/3 minion and 3 HP hero: Arcane Missiles (3 random missiles) can only kill on lucky rolls.
        Game rngOnly = new ScenarioBuilder()
            .WithMana(1)
            .WithOpponentHero(3)
            .WithOpponentBoard("River Crocolisk")
            .WithHand("Arcane Missiles")
            .Build();
        LethalResult missiles = new LethalSolver().Solve(rngOnly);
        Assert.False(missiles.IsDeterministic);
    }
}
