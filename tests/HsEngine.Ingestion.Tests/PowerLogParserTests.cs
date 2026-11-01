using HsEngine.Ingestion;

namespace HsEngine.Ingestion.Tests;

/// <summary>
/// Golden tests against an anonymised excerpt of a real Power.log (Fixtures/bg-opening.log: first 4000 GameState
/// lines of a game, BattleTags replaced). Expected counts were taken with grep, independently of the parser.
/// </summary>
public class PowerLogParserTests
{
    private const string Prefix = "D 17:16:51.7759592 GameState.DebugPrintPower() - ";

    private static readonly Lazy<List<PowerEvent>> Fixture = new(() =>
        PowerLogParser.Parse(File.ReadLines(Path.Combine(AppContext.BaseDirectory, "Fixtures", "bg-opening.log"))).ToList());

    [Fact]
    public void Emits_one_event_per_power_line_kind()
    {
        List<PowerEvent> events = Fixture.Value;

        Assert.Single(events.OfType<CreateGame>());
        Assert.Single(events.OfType<GameEntityCreated>());
        Assert.Equal(2, events.OfType<PlayerCreated>().Count());
        Assert.Equal(78, events.OfType<FullEntity>().Count());
        Assert.Equal(13, events.OfType<ShowEntity>().Count());
        Assert.Equal(9, events.OfType<HideEntity>().Count());
        Assert.Equal(1718, events.OfType<TagChange>().Count());
        Assert.Equal(118, events.OfType<BlockStart>().Count());
        Assert.Equal(117, events.OfType<BlockEnd>().Count());
    }

    [Fact]
    public void Full_entity_collects_its_indented_tags()
    {
        FullEntity first = Fixture.Value.OfType<FullEntity>().First();

        Assert.Equal(29, first.Id);
        Assert.Equal("TB_BaconShop_HERO_PH", first.CardId);
        Assert.Equal("3", first.Tags["CONTROLLER"]);
        Assert.Equal("HERO", first.Tags["CARDTYPE"]);
        Assert.Equal("30", first.Tags["HEALTH"]);
    }

    [Fact]
    public void Bracket_reference_with_localised_name_is_fully_parsed()
    {
        TagChange change = Fixture.Value.OfType<TagChange>().First(t => t.Entity.Id == 127 && t.Tag == "3026");

        Assert.Equal("Silas Negraluna", change.Entity.Name);
        Assert.Equal("SETASIDE", change.Entity.Zone);
        Assert.Equal("TB_BaconShop_HERO_90_SKIN_G", change.Entity.CardId);
        Assert.Equal(11, change.Entity.Player);
        Assert.Equal("16", change.Value);
    }

    [Fact]
    public void Player_name_reference_is_kept_unresolved()
    {
        TagChange change = Fixture.Value.OfType<TagChange>().First(t => t.Tag == "MULLIGAN_STATE");

        Assert.Null(change.Entity.Id);
        Assert.Equal("Player1#0000", change.Entity.Name);
        Assert.Equal("INPUT", change.Value);
    }

    [Fact]
    public void Name_reference_may_contain_spaces_and_commas()
    {
        TagChange change = Fixture.Value.OfType<TagChange>().First(t => t.Entity.Name == "Bob, o Bartender");

        Assert.Null(change.Entity.Id);
        Assert.Equal("NUM_TURNS_IN_PLAY", change.Tag);
    }

    [Fact]
    public void Game_info_lines_give_mode_and_player_names()
    {
        List<PowerEvent> events = Fixture.Value;

        Assert.Contains(new GameInfo("GameType", "GT_BATTLEGROUNDS"), events);
        Assert.Contains(new PlayerName(3, "Player1#0000"), events);
        Assert.Contains(new PlayerName(11, "Player2#0000"), events);
    }

    [Fact]
    public void Block_start_parses_entity_and_target()
    {
        BlockStart play = Fixture.Value.OfType<BlockStart>().First(b => b.BlockType == "PLAY");

        Assert.Equal(358, play.Entity.Id);
        Assert.NotNull(play.Target);
        Assert.NotNull(play.Target!.Value.Id);
    }

    [Fact]
    public void Ignores_power_task_list_and_option_lines()
    {
        var parser = new PowerLogParser();

        Assert.Empty(parser.Feed("D 17:16:53.0185014 PowerTaskList.DebugPrintPower() -     TAG_CHANGE Entity=5 tag=ZONE value=PLAY "));
        Assert.Empty(parser.Feed("D 17:16:53.0185014 GameState.DebugPrintOptions() - id=1"));
        Assert.Empty(parser.Flush());
    }

    [Fact]
    public void Entity_with_tags_is_emitted_when_its_tag_block_ends()
    {
        var parser = new PowerLogParser();

        Assert.Empty(parser.Feed(Prefix + "FULL_ENTITY - Creating ID=40 CardID=CS2_182"));
        Assert.Empty(parser.Feed(Prefix + "    tag=ZONE value=HAND"));
        List<PowerEvent> emitted = parser.Feed(Prefix + "TAG_CHANGE Entity=40 tag=COST value=4 ");

        FullEntity entity = Assert.IsType<FullEntity>(emitted[0]);
        Assert.Equal("HAND", entity.Tags["ZONE"]);
        Assert.IsType<TagChange>(emitted[1]);
    }

    [Fact]
    public void Flush_emits_a_pending_entity()
    {
        var parser = new PowerLogParser();
        parser.Feed(Prefix + "SHOW_ENTITY - Updating Entity=41 CardID=EX1_116");
        parser.Feed(Prefix + "    tag=COST value=4");

        ShowEntity shown = Assert.IsType<ShowEntity>(Assert.Single(parser.Flush()));

        Assert.Equal(41, shown.Entity.Id);
        Assert.Equal("EX1_116", shown.CardId);
        Assert.Equal("4", shown.Tags["COST"]);
    }

    [Fact]
    public void Hide_and_change_entity_are_parsed()
    {
        List<PowerEvent> events = PowerLogParser.Parse(
        [
            Prefix + "HIDE_ENTITY - Entity=[entityName=Leeroy Jenkins id=42 zone=HAND zonePos=2 cardId=EX1_116 player=1] tag=ZONE value=DECK",
            Prefix + "CHANGE_ENTITY - Updating Entity=[entityName=Polymorph Target id=43 zone=PLAY zonePos=1 cardId=EX1_116 player=2] CardID=CS2_tk1",
        ]).ToList();

        HideEntity hide = Assert.IsType<HideEntity>(events[0]);
        Assert.Equal((42, "DECK"), (hide.Entity.Id, hide.Tags["ZONE"]));
        ChangeEntity change = Assert.IsType<ChangeEntity>(events[1]);
        Assert.Equal((43, "CS2_tk1"), (change.Entity.Id, change.CardId));
    }
}
