using System.Diagnostics;
using HsEngine.Ingestion;

namespace HsEngine.Search;

/// <summary>
/// The live loop shared by the terminal and the overlay (ADR-0007, ADR-0009): waits for the newest Power.log under
/// the logs folder, follows it, and raises a recommendation at every decision point of the local player. It moves to
/// a newer session folder when the game restarts. Passive read only (ADR-0005).
/// </summary>
public sealed class LiveSession(string logsDirectory, SearchOptions options)
{
    private static readonly TimeSpan Poll = TimeSpan.FromMilliseconds(200);
    private static readonly TimeSpan SessionCheck = TimeSpan.FromSeconds(5);

    public event Action<string>? Status;
    public event Action<Recommendation>? Recommended;
    /// <summary>True when the local main phase begins, false when it ends.</summary>
    public event Action<bool>? LocalTurnChanged;
    public event Action<Exception>? Failed;

    public void Run(CancellationToken stop)
    {
        Status?.Invoke($"Aguardando um Power.log em {logsDirectory}");
        while (!stop.IsCancellationRequested)
        {
            string? path = LogLocator.LatestPowerLog(logsDirectory);
            if (path is null)
            {
                stop.WaitHandle.WaitOne(TimeSpan.FromSeconds(1));
                continue;
            }
            Follow(path, stop);
        }
    }

    // Returns when a newer session folder appears or on cancellation.
    private void Follow(string path, CancellationToken stop)
    {
        Status?.Invoke($"Seguindo {path}");
        using var tail = new LogTail(path);
        var live = new LiveRecommender(options);
        var sessionCheck = Stopwatch.StartNew();
        bool localTurn = false;

        while (!stop.IsCancellationRequested)
        {
            List<string> lines = tail.ReadNewLines();
            if (lines.Count > 0)
            {
                live.Feed(lines);
                continue;
            }

            // Caught up with the game.
            if (live.IsLocalMainPhase != localTurn)
            {
                localTurn = !localTurn;
                LocalTurnChanged?.Invoke(localTurn);
            }

            if (live.ShouldRecommend)
            {
                try
                {
                    if (live.Recommend() is { } recommendation)
                        Recommended?.Invoke(recommendation);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    Failed?.Invoke(ex);
                }
                continue;
            }

            if (sessionCheck.Elapsed > SessionCheck)
            {
                sessionCheck.Restart();
                if (LogLocator.LatestPowerLog(logsDirectory) is { } latest && latest != path)
                    return;
            }
            stop.WaitHandle.WaitOne(Poll);
        }
    }
}
