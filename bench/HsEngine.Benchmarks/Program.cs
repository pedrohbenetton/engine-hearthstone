using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;
using HsEngine.Evaluation;
using HsEngine.Search;
using HsEngine.Simulation;
using SabberStoneCore.Enums;
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

    [Benchmark]
    public ulong StructuralHash() => StateHasher.Compute(_game);
}

/// <summary>Where an ISMCTS iteration spends its time, on a Basic Mage mirror a few turns in.</summary>
[MemoryDiagnoser]
public class SearchBenchmarks
{
    private readonly BoardEvaluator _evaluator = new();
    private Game _game = null!;

    [GlobalSetup]
    public void Setup()
    {
        _game = GameFactory.CreateWithDecks(CardClass.MAGE, Decks.BasicMage, CardClass.MAGE, Decks.BasicMage, seed: 11);
        var policy = new GreedyOpponentPolicy(_evaluator);
        for (int i = 0; i < 8; i++)
            policy.PlayTurn(_game, new Random(i));
    }

    [Benchmark]
    public Game Determinize() => new HiddenInformationDeterminizer().Sample(_game, new Random(1));

    [Benchmark]
    public double Evaluate() => _evaluator.Evaluate(_game, _game.CurrentPlayer);

    /// <summary>One rollout turn of the light policy (it runs twice per iteration).</summary>
    [Benchmark]
    public int GreedyTurn()
    {
        Game clone = _game.DeterministicClone();
        new GreedyOpponentPolicy(_evaluator).PlayTurn(clone, new Random(2));
        return clone.Turn;
    }

    [Benchmark(OperationsPerInvoke = 100)]
    public int SearchPerIteration() =>
        new Ismcts(_evaluator).Search(_game, new SearchOptions { Iterations = 100, Seed = 3 }).Iterations;
}
