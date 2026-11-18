using HsEngine.Ingestion;
using HsEngine.Simulation;

namespace HsEngine.Simulation.Tests;

public class DeckLibraryTests : IDisposable
{
    private readonly string _tempDir = Directory.CreateTempSubdirectory("hsengine-decks-").FullName;

    public void Dispose() => Directory.Delete(_tempDir, recursive: true);

    private static readonly IReadOnlyList<CardData> SampleSnapshot =
    [
        new("HERO_08", 637, "Jaina Proudmoore", "HERO", "CORE", CardClass: "MAGE"),
        new("CS2_029", 315, "Fireball", "SPELL", "CORE", Cost: 4, CardClass: "MAGE", Collectible: true),
        new("CS2_024", 662, "Frostbolt", "SPELL", "CORE", Cost: 2, CardClass: "MAGE", Collectible: true),
        new("EX1_277", 564, "Arcane Missiles", "SPELL", "CORE", Cost: 1, CardClass: "MAGE", Collectible: true),
    ];

    [Fact]
    public void Encodes_and_decodes_deckstrings_preserving_counts_and_class()
    {
        var original = new DeckDefinition("Tempo Mage", "MAGE", new Dictionary<string, int>
        {
            ["EX1_277"] = 1,
            ["CS2_029"] = 2,
            ["CS2_024"] = 2,
        });

        string code = DeckStringCodec.Encode(original, format: 2, SampleSnapshot);
        string withComments = $"### Tempo Mage\n# Class: Mage\n# Format: Standard\n#\n{code}\n#";
        DeckDefinition decoded = DeckStringCodec.Decode(withComments, snapshot: SampleSnapshot);

        Assert.Equal("Tempo Mage", decoded.Name);
        Assert.Equal("MAGE", decoded.HeroClass);
        Assert.Equal(original.Cards, decoded.Cards);
    }

    [Fact]
    public void Remaining_after_subtracts_only_spent_copies()
    {
        var deck = new DeckDefinition("Mage", "MAGE", new Dictionary<string, int>
        {
            ["CS2_029"] = 2,
            ["EX1_277"] = 1,
        });

        List<string> remaining = deck.RemainingAfter(["CS2_029", "EX1_277", "NOT_IN_DECK"]);

        Assert.Equal(["CS2_029"], remaining);
    }

    [Fact]
    public void Parses_decks_log_and_hdt_player_decks_xml()
    {
        var deck = new DeckDefinition("Burn Mage", "MAGE", new Dictionary<string, int>
        {
            ["CS2_029"] = 2, ["CS2_024"] = 2, ["EX1_277"] = 2,
            ["CS2_023"] = 2, ["CS2_032"] = 2, ["CS2_172"] = 2,
            ["CS2_120"] = 2, ["CS2_182"] = 2,
        });
        List<CardData> snap =
        [
            .. SampleSnapshot,
            new("CS2_023", 555, "Arcane Intellect", "SPELL", "CORE", Cost: 3, CardClass: "MAGE", Collectible: true),
            new("CS2_032", 1004, "Flamestrike", "SPELL", "CORE", Cost: 7, CardClass: "MAGE", Collectible: true),
            new("CS2_172", 216, "Bloodfen Raptor", "MINION", "CORE", Cost: 2, Attack: 3, Health: 2, CardClass: "NEUTRAL", Collectible: true),
            new("CS2_120", 1369, "River Crocolisk", "MINION", "CORE", Cost: 2, Attack: 2, Health: 3, CardClass: "NEUTRAL", Collectible: true),
            new("CS2_182", 90, "Chillwind Yeti", "MINION", "CORE", Cost: 4, Attack: 4, Health: 5, CardClass: "NEUTRAL", Collectible: true),
        ];
        string code = DeckStringCodec.Encode(deck, snapshot: snap);

        List<DeckDefinition> fromLog = LocalDeckSources.ParseDecksLogLines(
        [
            "I 12:00:00.0000000 DeckPickerTrayDisplay - Finding Game With Deck:",
            "I 12:00:00.0000000 DeckPickerTrayDisplay - # Burn Mage",
            $"I 12:00:00.0000000 DeckPickerTrayDisplay - {code}",
        ], snap);
        Assert.Single(fromLog);
        Assert.Equal("Burn Mage", fromLog[0].Name);

        string xmlPath = Path.Combine(_tempDir, "PlayerDecks.xml");
        File.WriteAllText(xmlPath, """
            <Decks>
              <Deck>
                <Name>Paladin Handbuff</Name>
                <Class>Paladin</Class>
                <Archived>false</Archived>
                <IsArenaDeck>false</IsArenaDeck>
                <LastEdited>2026-11-01T10:00:00</LastEdited>
                <Cards>
                  <Card><Id>JAIL_328</Id><Count>2</Count></Card>
                  <Card><Id>CATA_474</Id><Count>2</Count></Card>
                </Cards>
              </Deck>
              <Deck>
                <Name>Old Archived</Name>
                <Class>Paladin</Class>
                <Archived>true</Archived>
                <Cards><Card><Id>JAIL_328</Id><Count>2</Count></Card></Cards>
              </Deck>
            </Decks>
            """);

        List<DeckDefinition> hdt = LocalDeckSources.ReadHdtPlayerDecks(xmlPath);
        DeckDefinition single = Assert.Single(hdt);
        Assert.Equal(("Paladin Handbuff", "PALADIN", 4), (single.Name, single.HeroClass, single.TotalCards));
    }

    [Fact]
    public void Matches_active_local_deck_from_known_cards_and_hero_class()
    {
        var wrongClass = new DeckDefinition("Mage", "MAGE", new Dictionary<string, int> { ["CATA_474"] = 2, ["JAIL_328"] = 2 });
        var otherPaladin = new DeckDefinition("Aggro Pally", "PALADIN", new Dictionary<string, int> { ["CS2_087"] = 2 });
        var realPaladin = new DeckDefinition("Dragon Pally", "PALADIN", new Dictionary<string, int>
        {
            ["CATA_474"] = 2, ["TIME_009"] = 1, ["JAIL_328"] = 2, ["CORE_CS2_094"] = 2,
        });

        var me = new ObservedSide(
            1,
            new ObservedHero("HERO_04", "PALADIN", 30, 0, 0, 0, false, 0, false),
            5, 0, 0, 0, null, [], [], 20, 0,
            KnownDeckCards: ["CATA_474", "TIME_009", "JAIL_328"]);

        DeckDefinition? matched = DeckMatcher.MatchLocalDeck(me, [wrongClass, otherPaladin, realPaladin]);

        Assert.NotNull(matched);
        Assert.Equal("Dragon Pally", matched.Name);
    }
}
