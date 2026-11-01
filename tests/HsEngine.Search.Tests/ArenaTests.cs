using HsEngine.Search;
using SabberStoneCore.Enums;

namespace HsEngine.Search.Tests;

public class ArenaTests
{
    private static readonly SearchOptions Tiny = new() { Iterations = 8 };

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void Plays_a_full_game_from_either_seat(int ismctsPlayer)
    {
        MatchResult result = new Arena(Tiny).Play(seed: 21, ismctsPlayer);

        Assert.Equal(ismctsPlayer, result.IsmctsPlayer);
        Assert.Contains(result.Outcome, new[] { PlayState.WON, PlayState.LOST, PlayState.TIED });
        Assert.True(result.Decisions > 0);
        Assert.True(result.Turns > 1);
    }

    [Fact]
    public void Same_seed_replays_the_same_game()
    {
        MatchResult a = new Arena(Tiny).Play(seed: 22, ismctsPlayer: 1);
        MatchResult b = new Arena(Tiny).Play(seed: 22, ismctsPlayer: 1);

        Assert.Equal((a.Outcome, a.Turns, a.Decisions), (b.Outcome, b.Turns, b.Decisions));
    }

    [Fact]
    public void Turn_limit_ends_the_game_as_a_tie()
    {
        MatchResult result = new Arena(Tiny, maxTurns: 2).Play(seed: 23, ismctsPlayer: 1);

        Assert.Equal(PlayState.TIED, result.Outcome);
    }

    [Fact]
    public void Summary_reports_win_rate_with_wilson_interval()
    {
        MatchSummary summary = MatchSummary.From(
        [
            new MatchResult(0, 1, PlayState.WON, 10, 20, TimeSpan.Zero),
            new MatchResult(0, 2, PlayState.WON, 10, 20, TimeSpan.Zero),
            new MatchResult(1, 1, PlayState.LOST, 10, 20, TimeSpan.Zero),
            new MatchResult(1, 2, PlayState.TIED, 10, 20, TimeSpan.Zero),
        ]);

        Assert.Equal((2, 1, 1), (summary.Wins, summary.Losses, summary.Ties));
        // Ties count as half a win.
        Assert.Equal(0.625, summary.WinRate, precision: 9);
        Assert.True(summary.Lower95 < summary.WinRate && summary.WinRate < summary.Upper95);
    }
}
