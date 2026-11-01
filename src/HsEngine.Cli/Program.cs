using System.Collections.Concurrent;
using System.Diagnostics;
using HsEngine.Ingestion;
using HsEngine.Search;

const string Usage =
    """
    usage:
      HsEngine.Cli live  [--logs DIR] [--time-ms T] [--workers W] [--top N]
      HsEngine.Cli arena [--deals N] [--iterations K] [--time-ms T] [--seed S] [--parallel P] [--out results.csv]
    """;

return args.FirstOrDefault() switch
{
    "live" => RunLive(),
    "arena" => RunArena(),
    _ => Fail(),
};

int Fail()
{
    Console.Error.WriteLine(Usage);
    return 1;
}

// Follows the newest Power.log under --logs and prints a recommendation at every decision point of the local
// player (ADR-0007). Passive read only (ADR-0005): nothing is sent to the game.
int RunLive()
{
    string logs = OptionText("--logs") ?? @"C:\Program Files (x86)\Hearthstone\Logs";
    int top = Option("--top", 3);
    var options = new SearchOptions
    {
        TimeLimit = TimeSpan.FromMilliseconds(Option("--time-ms", 3000)),
        Workers = Option("--workers", Environment.ProcessorCount),
        Seed = Environment.TickCount,
    };

    using var stop = new CancellationTokenSource();
    Console.CancelKeyPress += (_, e) =>
    {
        e.Cancel = true;
        stop.Cancel();
    };

    Console.WriteLine($"Waiting for a Power.log under {logs} (Ctrl+C to quit)...");
    while (!stop.IsCancellationRequested)
    {
        string? path = LogLocator.LatestPowerLog(logs);
        if (path is null)
        {
            Thread.Sleep(1000);
            continue;
        }
        Follow(path, options, top, logs, stop.Token);
    }
    return 0;
}

// Returns when a newer game session (new log folder) appears or on Ctrl+C.
static void Follow(string path, SearchOptions options, int top, string logs, CancellationToken stop)
{
    Console.WriteLine($"Following {path}");
    using var tail = new LogTail(path);
    var live = new LiveRecommender(options);
    var sessionCheck = Stopwatch.StartNew();

    while (!stop.IsCancellationRequested)
    {
        List<string> lines = tail.ReadNewLines();
        if (lines.Count > 0)
        {
            live.Feed(lines);
            continue;
        }

        // Caught up with the game: recommend if it is the local player's decision.
        if (live.ShouldRecommend)
        {
            try
            {
                if (live.Recommend() is { } recommendation)
                    Console.WriteLine(RecommendationFormatter.Format(recommendation, top));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                Console.Error.WriteLine($"Could not recommend this position: {ex.Message}");
            }
            continue;
        }

        if (sessionCheck.Elapsed > TimeSpan.FromSeconds(5))
        {
            sessionCheck.Restart();
            if (LogLocator.LatestPowerLog(logs) is { } latest && latest != path)
                return;
        }
        Thread.Sleep(200);
    }
}

// Each deal is played twice, once from each seat, so seat advantage cancels out.
// With --out every finished game is appended at once and games already in the file are skipped, so an
// interrupted run resumes where it stopped.
int RunArena()
{
    int deals = Option("--deals", 10);
    int iterations = Option("--iterations", 200);
    int timeMs = Option("--time-ms", 0);
    int seed = Option("--seed", 1000);
    int parallel = Option("--parallel", Environment.ProcessorCount);

    var search = new SearchOptions
    {
        Iterations = timeMs > 0 ? null : iterations,
        TimeLimit = timeMs > 0 ? TimeSpan.FromMilliseconds(timeMs) : null,
    };
    string? outPath = OptionText("--out");
    var arena = new Arena(search);
    var results = new ConcurrentBag<MatchResult>(outPath is not null && File.Exists(outPath) ? ReadResults(outPath) : []);
    var done = results.Select(r => (r.Seed, r.IsmctsPlayer)).ToHashSet();
    var writeLock = new object();
    var clock = Stopwatch.StartNew();

    Console.WriteLine($"ISMCTS vs greedy | Basic Mage mirror | {deals} deals x 2 seats | " +
                      (timeMs > 0 ? $"{timeMs} ms" : $"{iterations} iterations") + $" per decision | {parallel} games in parallel" +
                      (done.Count > 0 ? $" | resuming, {done.Count} games already in {outPath}" : ""));

    Parallel.For(0, deals * 2, new ParallelOptions { MaxDegreeOfParallelism = parallel }, i =>
    {
        (long dealSeed, int seat) = (seed + i / 2, i % 2 + 1);
        if (done.Contains((dealSeed, seat)))
            return;

        MatchResult r = arena.Play(dealSeed, seat);
        results.Add(r);
        if (outPath is not null)
            lock (writeLock)
                File.AppendAllText(outPath,
                    $"{r.Seed},{r.IsmctsPlayer},{r.Outcome},{r.Turns},{r.Decisions},{r.Elapsed.TotalMilliseconds:F0}{Environment.NewLine}");
        Console.WriteLine($"  deal {r.Seed} seat {r.IsmctsPlayer}: {r.Outcome,-4} turns {r.Turns,2} decisions {r.Decisions,3} " +
                          $"{r.Elapsed.TotalSeconds,6:F1}s ({r.Elapsed.TotalMilliseconds / Math.Max(1, r.Decisions),5:F0} ms/decision)");
    });

    MatchSummary s = MatchSummary.From(results.ToList());
    Console.WriteLine($"games {s.Games}  W {s.Wins}  L {s.Losses}  T {s.Ties}  " +
                      $"win rate {s.WinRate:P1}  95% CI [{s.Lower95:P1}, {s.Upper95:P1}]  wall {clock.Elapsed.TotalMinutes:F1} min");
    foreach (int seat in new[] { 1, 2 })
    {
        MatchSummary bySeat = MatchSummary.From(results.Where(r => r.IsmctsPlayer == seat).ToList());
        Console.WriteLine($"  seat {seat}: win rate {bySeat.WinRate:P1} over {bySeat.Games}");
    }
    return 0;
}

int Option(string name, int fallback) => OptionText(name) is { } text ? int.Parse(text) : fallback;

string? OptionText(string name)
{
    int i = Array.IndexOf(args, name);
    return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
}

static IEnumerable<MatchResult> ReadResults(string path) =>
    File.ReadLines(path)
        .Where(line => line.Length > 0)
        .Select(line => line.Split(','))
        .Select(f => new MatchResult(long.Parse(f[0]), int.Parse(f[1]), Enum.Parse<SabberStoneCore.Enums.PlayState>(f[2]),
            int.Parse(f[3]), int.Parse(f[4]), TimeSpan.FromMilliseconds(double.Parse(f[5], System.Globalization.CultureInfo.InvariantCulture))));
