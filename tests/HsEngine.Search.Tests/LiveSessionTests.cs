using HsEngine.Search;

namespace HsEngine.Search.Tests;

/// <summary>The live loop shared by the terminal and the overlay, run against a log folder it follows.</summary>
public sealed class LiveSessionTests : IDisposable
{
    private readonly string _logs = Directory.CreateTempSubdirectory("hsengine-live-").FullName;

    public void Dispose() => Directory.Delete(_logs, recursive: true);

    [Fact]
    public async Task Follows_the_newest_log_and_recommends_on_the_local_turn()
    {
        string[] lines = File.ReadAllLines(Path.Combine(AppContext.BaseDirectory, "Fixtures", "standard-game1.log"));
        int turn9 = Array.FindIndex(lines, l => l.Contains("tag=TURN value=9 ", StringComparison.Ordinal));
        int mainAction = Array.FindIndex(lines, turn9, l => l.Contains("tag=STEP value=MAIN_ACTION", StringComparison.Ordinal));
        string session = Directory.CreateDirectory(Path.Combine(_logs, "Hearthstone_2026_11_01_12_00_00")).FullName;
        File.WriteAllLines(Path.Combine(session, "Power.log"), lines[..(mainAction + 1)]);

        var live = new LiveSession(_logs, new SearchOptions { Iterations = 20, Seed = 1 });
        var recommended = new TaskCompletionSource<Recommendation>(TaskCreationOptions.RunContinuationsAsynchronously);
        var turns = new List<bool>();
        live.Recommended += r => recommended.TrySetResult(r);
        live.LocalTurnChanged += turns.Add;

        using var stop = new CancellationTokenSource();
        Task run = Task.Run(() => live.Run(stop.Token));
        Recommendation recommendation = await recommended.Task.WaitAsync(TimeSpan.FromSeconds(30));
        await stop.CancelAsync();
        await run.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal(9, recommendation.Observed.Turn);
        Assert.NotEmpty(recommendation.Plans);
        Assert.Equal([true], turns);
    }
}
