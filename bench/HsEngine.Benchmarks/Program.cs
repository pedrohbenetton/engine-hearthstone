using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;
using HsEngine.Simulation;
using SabberStoneCore.Model;
using SabberStoneCore.Tasks.PlayerTasks;

BenchmarkSwitcher.FromAssembly(typeof(CloneBenchmarks).Assembly).Run(args);

/// <summary>
/// Clone cost is the expected MCTS bottleneck (ARCH-0001 §3.4, risk R2).
/// Each search iteration clones once per determinization and once per expanded action.
/// </summary>
[MemoryDiagnoser]
public class CloneBenchmarks
{
    private Game _game = null!;

    /// <summary>Random actions played before measuring: 0 = turn 1, larger = busier board and history.</summary>
    [Params(0, 40, 80)]
    public int Actions { get; set; }

    [GlobalSetup]
    public void Setup() => _game = GameFactory.CreateMidgame(seed: 7, Actions);

    [Benchmark(Baseline = true)]
    public Game Clone() => _game.DeterministicClone();

    [Benchmark]
    public bool CloneAndApplyFirstOption()
    {
        Game clone = _game.DeterministicClone();
        List<PlayerTask> options = clone.CurrentPlayer.Options();
        return clone.Process(options[0]);
    }

    [Benchmark]
    public int Options() => _game.CurrentPlayer.Options().Count;

    [Benchmark]
    public string Hash() => _game.Hash();
}
