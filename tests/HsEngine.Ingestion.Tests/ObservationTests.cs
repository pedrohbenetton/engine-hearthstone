using System.Text.Json;
using HsEngine.Ingestion;

namespace HsEngine.Ingestion.Tests;

/// <summary>
/// ADR-0005 item 3 / ADR-0007: the recommender only sees what the local player sees. Expected values for the main
/// action of turn 9 of Fixtures/standard-game1.log come from an independent Python replay.
/// </summary>
public class ObservationTests
{
    private static readonly Lazy<List<PowerEvent>> Events = new(() =>
        PowerLogParser.Parse(File.ReadLines(Path.Combine(AppContext.BaseDirectory, "Fixtures", "standard-game1.log"))).ToList());

    /// <summary>Replays until the main action step of <paramref name="turn"/> begins, then applies <paramref name="extra"/>.</summary>
    internal static GameStateMachine AtMainAction(int turn, IEnumerable<PowerEvent>? extra = null)
    {
        var state = new GameStateMachine();
        foreach (PowerEvent e in Events.Value)
        {
            state.Apply(e);
            if (e is TagChange { Tag: "STEP", Value: "MAIN_ACTION" } && state.Turn == turn)
                break;
        }
        foreach (PowerEvent e in extra ?? [])
            state.Apply(e);
        return state;
    }

    [Fact]
    public void Local_side_is_fully_visible()
    {
        ObservedState observed = Observation.From(AtMainAction(9));
        ObservedSide me = observed.Me;

        Assert.Equal((9, 1, 1), (observed.Turn, observed.CurrentPlayerId, me.PlayerId));
        Assert.Equal(("HERO_04", "PALADIN", 30, 0, 0), (me.Hero.CardId, me.Hero.Class, me.Hero.Health, me.Hero.Damage, me.Hero.Armor));
        Assert.False(me.Hero.HeroPowerUsed);
        Assert.Equal((5, 0), (me.Resources, me.ResourcesUsed));
        Assert.Equal(new ObservedWeapon("CATA_472", 2, 2, 1), me.Weapon);

        ObservedMinion bruiser = Assert.Single(me.Board);
        Assert.Equal(("JAIL_328", 2, 2, 0, false, 6), (bruiser.CardId, bruiser.Attack, bruiser.Health, bruiser.Damage, bruiser.Exhausted, bruiser.TurnsInPlay));

        Assert.Equal(["CATA_474", "TIME_009", "CS3_024", "CORE_CS2_094", "TIME_700", "TIME_009t1"], me.Hand.Select(c => c.CardId));
        Assert.Equal([4, 8, 5, 1, 3, 4], me.Hand.Select(c => c.Cost));
        Assert.Equal(32, me.DeckCount);
    }

    [Fact]
    public void Opponent_hidden_cards_carry_no_identity()
    {
        ObservedSide opponent = Observation.From(AtMainAction(9)).Opponent;

        Assert.Equal(("HERO_06bo", "DRUID", 1), (opponent.Hero.CardId, opponent.Hero.Class, opponent.Hero.Damage));
        Assert.Equal(["CATA_131"], opponent.Board.Select(m => m.CardId));
        Assert.Equal([null, null, null, "EDR_000", null, null], opponent.Hand.Select(c => c.CardId));
        Assert.All(opponent.Hand.Where(c => c.CardId is null), c => Assert.Null(c.Cost));
        Assert.Equal(17, opponent.DeckCount);
    }

    [Fact]
    public void Minion_keywords_come_from_the_log_tags()
    {
        ObservedState observed = Observation.From(AtMainAction(9,
            [new TagChange(new EntityRef(80), "TAUNT", "1"), new TagChange(new EntityRef(80), "FROZEN", "1")]));

        Assert.Equal(["FROZEN", "TAUNT"], observed.Me.Board[0].Keywords.Order());
    }

    [Fact]
    public void Hidden_information_does_not_change_the_observation()
    {
        // Differences the client never shows: tags on a hidden opponent hand card (44), and the identity of cards
        // in either deck. Two states that only differ there must produce the same observation.
        PowerEvent[] hidden =
        [
            new TagChange(new EntityRef(44), "COST", "3"),
            new TagChange(new EntityRef(44), "ATK", "7"),
            new FullEntity(9001, "EX1_116", new Dictionary<string, string> { ["ZONE"] = "DECK", ["CONTROLLER"] = "2", ["CARDTYPE"] = "MINION" }),
            new FullEntity(9002, "EX1_116", new Dictionary<string, string> { ["ZONE"] = "DECK", ["CONTROLLER"] = "1", ["CARDTYPE"] = "MINION" }),
        ];
        PowerEvent[] placeholders =
        [
            new FullEntity(9001, "", new Dictionary<string, string> { ["ZONE"] = "DECK", ["CONTROLLER"] = "2" }),
            new FullEntity(9002, "", new Dictionary<string, string> { ["ZONE"] = "DECK", ["CONTROLLER"] = "1" }),
        ];

        string withSecrets = JsonSerializer.Serialize(Observation.From(AtMainAction(9, hidden)));
        string without = JsonSerializer.Serialize(Observation.From(AtMainAction(9, placeholders)));

        Assert.Equal(without, withSecrets);
    }

    [Fact]
    public void Needs_a_known_local_player()
    {
        Assert.Throws<InvalidOperationException>(() => Observation.From(new GameStateMachine()));
    }

    [Fact]
    public void Tracks_mulligan_keeps_created_cards_and_spent_vs_known_deck_cards()
    {
        // At turn 1 main action in standard-game1.log:
        // - Local player mulliganed CATA_479 back into DECK, so it is in KnownDeckCards but NOT in SpentDeckCards.
        // - Opponent kept 2 opening cards in mulligan, replaced 2, and received The Coin (IsCreated = true).
        ObservedState turn1 = Observation.From(AtMainAction(1));

        Assert.Contains("CATA_479", turn1.Me.KnownDeckCards!);
        Assert.DoesNotContain("CATA_479", turn1.Me.SpentDeckCards!);

        Assert.Equal(2, turn1.Opponent.Hand.Count(c => c.KeptInMulligan));
        Assert.Contains(turn1.Opponent.Hand, c => c.IsCreated && !c.KeptInMulligan);
        Assert.Empty(turn1.Opponent.KnownDeckCards!);
    }

    [Fact]
    public void Sanitizes_opponent_secrets_while_preserving_local_secrets_and_fatigue()
    {
        PowerEvent[] secretAndFatigue =
        [
            new FullEntity(9101, "EX1_289", new Dictionary<string, string> { ["ZONE"] = "SECRET", ["CONTROLLER"] = "1", ["CARDTYPE"] = "SPELL" }),
            new FullEntity(9102, "EX1_287", new Dictionary<string, string> { ["ZONE"] = "SECRET", ["CONTROLLER"] = "2", ["CARDTYPE"] = "SPELL" }),
            new TagChange(new EntityRef(3), "FATIGUE", "4"),
        ];
        PowerEvent[] maskedOpponentSecret =
        [
            new FullEntity(9101, "EX1_289", new Dictionary<string, string> { ["ZONE"] = "SECRET", ["CONTROLLER"] = "1", ["CARDTYPE"] = "SPELL" }),
            new FullEntity(9102, "", new Dictionary<string, string> { ["ZONE"] = "SECRET", ["CONTROLLER"] = "2" }),
            new TagChange(new EntityRef(3), "FATIGUE", "4"),
        ];

        ObservedState a = Observation.From(AtMainAction(9, secretAndFatigue));
        ObservedState b = Observation.From(AtMainAction(9, maskedOpponentSecret));

        Assert.Equal(["EX1_289"], a.Me.Secrets!);
        Assert.Equal([null], a.Opponent.Secrets!);
        Assert.Equal(4, a.Opponent.Fatigue);
        Assert.Equal(JsonSerializer.Serialize(b), JsonSerializer.Serialize(a));
    }
}
