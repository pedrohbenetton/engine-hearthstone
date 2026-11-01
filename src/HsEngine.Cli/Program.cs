using System.Collections.Concurrent;
using System.Diagnostics;
using HsEngine.Search;

// Usage: HsEngine.Cli arena [--deals N] [--iterations K] [--time-ms T] [--seed S] [--parallel P]
// Each deal is played twice, once from each seat, so seat advantage cancels out.
if (args.Length == 0 || args[0] != "arena")
{
    Console.Error.WriteLine("usage: HsEngine.Cli arena [--deals N] [--iterations K] [--time-ms T] [--seed S] [--parallel P]");
    return 1;
}

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
var arena = new Arena(search);
var results = new ConcurrentBag<MatchResult>();
var clock = Stopwatch.StartNew();

Console.WriteLine($"ISMCTS vs greedy | Basic Mage mirror | {deals} deals x 2 seats | " +
                  (timeMs > 0 ? $"{timeMs} ms" : $"{iterations} iterations") + $" per decision | {parallel} games in parallel");

Parallel.For(0, deals * 2, new ParallelOptions { MaxDegreeOfParallelism = parallel }, i =>
{
    MatchResult r = arena.Play(seed + i / 2, ismctsPlayer: i % 2 + 1);
    results.Add(r);
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

int Option(string name, int fallback)
{
    int i = Array.IndexOf(args, name);
    return i >= 0 && i + 1 < args.Length ? int.Parse(args[i + 1]) : fallback;
}
