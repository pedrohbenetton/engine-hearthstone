using System.Collections.Concurrent;
using System.Diagnostics;
using HsEngine.Ingestion;
using HsEngine.Search;
using HsEngine.Simulation;

const string Usage =
    """
    usage:
      HsEngine.Cli live    [--logs DIR] [--deck CODE] [--time-ms T] [--workers W] [--top N]
      HsEngine.Cli analyze [--log FILE | --logs DIR] [--turn T] [--deck CODE] [--time-ms T] [--workers W]
      HsEngine.Cli arena   [--deals N] [--iterations K] [--time-ms T] [--seed S] [--parallel P] [--out results.csv]
    """;

return args.FirstOrDefault() switch
{
    "live" => RunLive(),
    "analyze" => RunAnalyze(),
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
    Console.OutputEncoding = System.Text.Encoding.UTF8;
    string logs = OptionText("--logs") ?? HsEngine.Ingestion.LogLocator.DetectLogsDirectory();
    Console.WriteLine($"Pasta de logs: {logs}");
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

    var session = new LiveSession(logs, options, OptionText("--deck"));
    session.Status += message => Console.WriteLine($"{message} (Ctrl+C para sair)");
    session.Recommended += recommendation => Console.WriteLine(RecommendationFormatter.Format(recommendation, top));
    session.Failed += ex => Console.Error.WriteLine($"Não foi possível recomendar esta posição: {ex.Message}");
    session.Run(stop.Token);
    return 0;
}

int RunAnalyze()
{
    Console.OutputEncoding = System.Text.Encoding.UTF8;
    string logs = OptionText("--logs") ?? LogLocator.DetectLogsDirectory();
    string? logFile = OptionText("--log") ?? LogLocator.LatestPowerLog(logs);
    if (logFile is null || !File.Exists(logFile))
    {
        Console.Error.WriteLine($"Nenhum Power.log encontrado (procurei em {logFile ?? logs}).");
        return 1;
    }

    int? onlyTurn = OptionText("--turn") is { } t ? int.Parse(t) : null;
    var options = new SearchOptions
    {
        TimeLimit = TimeSpan.FromMilliseconds(Option("--time-ms", 1500)),
        Workers = Option("--workers", Environment.ProcessorCount),
        Seed = Option("--seed", 1),
    };

    var decks = new List<DeckDefinition>();
    if (OptionText("--deck") is { } code && DeckStringCodec.TryDecode(code, "Deck informado") is { } explicitDeck)
        decks.Add(explicitDeck with { LastEdited = DateTimeOffset.MaxValue });
    decks.AddRange(LocalDeckSources.ReadDecksLog(logs));
    decks.AddRange(LocalDeckSources.ReadHdtPlayerDecks());

    Console.WriteLine($"Analisando {logFile}...");
    var analyzer = new ReplayAnalyzer(options, decks);
    ReplayReport report = analyzer.Analyze(File.ReadLines(logFile), onlyTurn);
    Console.Write(ReplayReportFormatter.Format(report));
    return 0;
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
