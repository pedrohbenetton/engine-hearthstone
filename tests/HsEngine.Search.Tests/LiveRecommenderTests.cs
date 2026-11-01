using HsEngine.Search;

namespace HsEngine.Search.Tests;

/// <summary>When the live loop recommends, driven by the real log of Fixtures/standard-game1.log.</summary>
public class LiveRecommenderTests
{
    private static readonly string[] Lines = File.ReadAllLines(Path.Combine(AppContext.BaseDirectory, "Fixtures", "standard-game1.log"));

    private static LiveRecommender NewRecommender() => new(new SearchOptions { Iterations = 20, Seed = 1 });

    /// <summary>Feeds the log line by line until the main action step of <paramref name="turn"/> begins.</summary>
    private static void FeedUntilMainAction(LiveRecommender live, int turn)
    {
        foreach (string line in Lines)
        {
            live.Feed([line]);
            if (live.State.Turn == turn && line.Contains("tag=STEP value=MAIN_ACTION", StringComparison.Ordinal))
                return;
        }
        throw new InvalidOperationException($"turn {turn} not found");
    }

    [Fact]
    public void Nothing_to_recommend_before_the_game_starts()
    {
        Assert.False(NewRecommender().ShouldRecommend);
    }

    [Fact]
    public void Recommends_once_at_the_start_of_the_local_main_phase()
    {
        LiveRecommender live = NewRecommender();
        FeedUntilMainAction(live, 9);

        Assert.True(live.ShouldRecommend);
        Recommendation recommendation = live.Recommend()!;

        Assert.Equal(9, recommendation.Observed.Turn);
        Assert.NotEmpty(recommendation.Result.Actions);
        Assert.False(live.ShouldRecommend);
    }

    [Fact]
    public void Does_not_recommend_on_the_opponents_turn()
    {
        LiveRecommender live = NewRecommender();
        FeedUntilMainAction(live, 10);

        Assert.False(live.ShouldRecommend);
        Assert.Null(live.Recommend());
    }

    [Fact]
    public void Waits_for_open_blocks_to_resolve_and_then_recommends_again()
    {
        LiveRecommender live = NewRecommender();
        FeedUntilMainAction(live, 9);
        live.Recommend();

        live.Feed(["D 23:30:00.0000000 GameState.DebugPrintPower() - BLOCK_START BlockType=PLAY Entity=17 EffectCardId=System.Collections.Generic.List`1[System.String] EffectIndex=0 Target=0 SubOption=-1 "]);
        Assert.False(live.ShouldRecommend);

        live.Feed(["D 23:30:00.0000000 GameState.DebugPrintPower() - BLOCK_END"]);
        Assert.True(live.ShouldRecommend);
    }

    [Fact]
    public void Skips_log_changes_that_leave_the_visible_position_unchanged()
    {
        LiveRecommender live = NewRecommender();
        FeedUntilMainAction(live, 9);
        live.Recommend();

        live.Feed(["D 23:30:00.0000000 GameState.DebugPrintPower() - TAG_CHANGE Entity=GameEntity tag=NUM_MINIONS_KILLED_THIS_TURN value=0 "]);
        Assert.Null(live.Recommend());

        live.Feed(["D 23:30:00.0000000 GameState.DebugPrintPower() - TAG_CHANGE Entity=[entityName=Scarlet Bruiser id=80 zone=PLAY zonePos=1 cardId=JAIL_328 player=1] tag=DAMAGE value=1 "]);
        Assert.NotNull(live.Recommend());
    }

    [Fact]
    public void Stops_when_the_game_is_over()
    {
        LiveRecommender live = NewRecommender();
        live.Feed(Lines);

        Assert.False(live.ShouldRecommend);
    }

    [Fact]
    public void Formats_the_top_lines_with_win_probability()
    {
        LiveRecommender live = NewRecommender();
        FeedUntilMainAction(live, 9);

        string text = RecommendationFormatter.Format(live.Recommend()!, top: 3);

        Assert.Contains("Turn 9", text);
        Assert.Contains("mana 5/5", text);
        Assert.Matches(@"1\. +\d+% \+/-\d+%", text);
        Assert.Contains("proxies", text);
        Assert.Contains("1.", text);
    }
}
