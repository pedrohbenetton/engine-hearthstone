using HsEngine.Ingestion;

namespace HsEngine.Ingestion.Tests;

/// <summary>
/// Replays Fixtures/standard-game1.log: a ranked Standard game (build 255790), local player 1 (Paladin, HERO_04)
/// against player 2 (Druid, HERO_06bo), lost by player 1 on turn 20. Expected values come from an independent
/// Python replay of the same file, not from this parser.
/// </summary>
public class GameStateMachineTests
{
    private static readonly Lazy<List<PowerEvent>> Events = new(() =>
        PowerLogParser.Parse(File.ReadLines(Path.Combine(AppContext.BaseDirectory, "Fixtures", "standard-game1.log"))).ToList());

    private static GameStateMachine ReplayAll()
    {
        var state = new GameStateMachine();
        foreach (PowerEvent e in Events.Value)
            state.Apply(e);
        return state;
    }

    /// <summary>Replays up to and including the event that starts <paramref name="turn"/>.</summary>
    private static GameStateMachine ReplayUntilTurn(int turn)
    {
        var state = new GameStateMachine();
        foreach (PowerEvent e in Events.Value)
        {
            state.Apply(e);
            if (e is TagChange { Tag: "TURN" } && state.Turn == turn)
                break;
        }
        return state;
    }

    [Fact]
    public void Reads_game_info_and_identifies_the_local_player()
    {
        GameStateMachine state = ReplayAll();

        Assert.Equal("255790", state.Info["BuildNumber"]);
        Assert.Equal("FT_STANDARD", state.Info["FormatType"]);
        Assert.Equal(1, state.LocalPlayerId);
        Assert.Equal(2, state.OpponentPlayerId);
    }

    [Fact]
    public void Players_map_to_their_entities_and_heroes()
    {
        GameStateMachine state = ReplayAll();

        Assert.Equal(("HERO_04", 71), (state.Player(1).Hero.CardId, state.Player(1).Hero.Id));
        Assert.Equal(("HERO_06bo", 73), (state.Player(2).Hero.CardId, state.Player(2).Hero.Id));
    }

    [Fact]
    public void Opponent_battletag_revealed_mid_game_resolves_to_the_unknown_player()
    {
        GameStateMachine state = ReplayAll();

        Assert.Equal("LOST", state.Player(1).Entity["PLAYSTATE"]);
        Assert.Equal("WON", state.Player(2).Entity["PLAYSTATE"]);
        Assert.Equal(0, state.UnresolvedReferences);
    }

    [Fact]
    public void Start_of_turn_9_matches_the_log()
    {
        GameStateMachine state = ReplayUntilTurn(9);
        PlayerState me = state.Player(1), opponent = state.Player(2);

        Assert.Equal((4, 4), (me.Resources, me.ResourcesUsed));
        Assert.Equal([("JAIL_328", 2, 2)], me.Board.Select(m => (m.CardId, m.Int("ATK"), m.Int("HEALTH"))));
        Assert.Equal([17, 21, 22, 104, 114], me.Hand.Select(c => c.Id).Order());
        Assert.Equal(["CATA_474", "CORE_CS2_094", "CS3_024", "TIME_009", "TIME_700"], me.Hand.Select(c => c.CardId).Order());

        Assert.Equal(["CATA_131"], opponent.Board.Select(m => m.CardId));
        Assert.Equal(6, opponent.Hand.Count);
        Assert.Equal(["EDR_000"], opponent.Hand.Where(c => c.CardId.Length > 0).Select(c => c.CardId));
    }

    [Fact]
    public void Final_position_matches_the_log()
    {
        GameStateMachine state = ReplayAll();
        PlayerState me = state.Player(1), opponent = state.Player(2);

        Assert.Equal(20, state.Turn);
        Assert.Equal(36, me.Hero.Int("DAMAGE"));
        Assert.Equal((18, 1), (opponent.Hero.Int("DAMAGE"), opponent.Hero.Int("ARMOR")));
        Assert.Equal(
            ["CATA_473", "CATA_478", "CATA_478t", "CATA_478t", "CS2_101t2", "TIME_019", "TIME_700t"],
            me.Board.Select(m => m.CardId).Order());
        Assert.Equal([1, 2, 3, 4, 5, 6, 7], me.Board.Select(m => m.Int("ZONE_POSITION")));
        Assert.Equal(
            [("EDR_453", 17, 12), ("EDR_453", 34, 24), ("EDR_453", 68, 48), ("EDR_846t3", 6, 10), ("END_010", 5, 5)],
            opponent.Board.Select(m => (m.CardId, m.Int("ATK"), m.Int("HEALTH"))).Order());
        Assert.Equal(9, me.Hand.Count);
        Assert.Equal((20, 1), (me.DeckCount, opponent.DeckCount));
    }

    [Fact]
    public void Create_game_resets_the_state()
    {
        GameStateMachine state = ReplayAll();

        state.Apply(new CreateGame());

        Assert.Empty(state.Entities);
        Assert.Equal(0, state.Turn);
        Assert.Null(state.LocalPlayerId);
    }

    [Fact]
    public void Unknown_references_are_counted_not_thrown()
    {
        var state = new GameStateMachine();

        state.Apply(new TagChange(new EntityRef(null, "Nobody#1234"), "ZONE", "PLAY"));
        state.Apply(new TagChange(new EntityRef(999), "ZONE", "PLAY"));

        Assert.Equal(1, state.UnresolvedReferences);
        Assert.Equal("PLAY", state.Entities[999]["ZONE"]);
    }
}
