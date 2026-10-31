using HsEngine.Evaluation;
using HsEngine.Search;
using HsEngine.Simulation;
using SabberStoneCore.Enums;
using SabberStoneCore.Model;
using SabberStoneCore.Tasks.PlayerTasks;

namespace HsEngine.Search.Tests;

public class IsmctsTests
{
    private static SearchResult Run(Game game, int iterations = 400, int workers = 1, int seed = 1) =>
        new Ismcts().Search(game, new SearchOptions { Iterations = iterations, Workers = workers, Seed = seed });

    [Fact]
    public void Finds_the_only_lethal_ordering()
    {
        // Wolfrider (charge 3/1) must come before Abusive Sergeant's +2 to deal 5.
        Game game = new ScenarioBuilder().WithMana(4).WithOpponentHero(5)
            .WithHand("Abusive Sergeant", "Wolfrider").Build();

        SearchResult result = Run(game);

        RankedAction best = result.Actions[0];
        Assert.Equal("Wolfrider", best.SourceCardName);
        // The node mean includes exploratory visits to worse follow-ups, so it approaches 1 without reaching it.
        Assert.True(best.WinProbability > 0.9, $"p = {best.WinProbability}");
        Assert.True(result.Actions.Skip(1).All(a => a.WinProbability < best.WinProbability));
    }

    [Fact]
    public void Prefers_developing_a_minion_over_passing()
    {
        Game game = new ScenarioBuilder().WithMana(2).WithHand("Bloodfen Raptor").Build();

        SearchResult result = Run(game);

        Assert.Equal(PlayerTaskType.PLAY_CARD, result.Actions[0].Key.Type);
        Assert.True(result.Actions[0].WinProbability > result.Actions.Single(a => a.Key.Type == PlayerTaskType.END_TURN).WinProbability);
    }

    [Fact]
    public void Same_seed_gives_same_ranking()
    {
        Game game = new ScenarioBuilder().WithMana(5).WithOpponentHero(20)
            .WithHand("Bloodfen Raptor", "Wolfrider", "Fireball").WithOpponentBoard("Goldshire Footman").Build();

        SearchResult a = Run(game, seed: 7);
        SearchResult b = Run(game, seed: 7);

        Assert.Equal(
            a.Actions.Select(x => (x.Key, x.Visits, x.WinProbability)),
            b.Actions.Select(x => (x.Key, x.Visits, x.WinProbability)));
    }

    [Fact]
    public void Every_iteration_visits_exactly_one_root_action()
    {
        Game game = new ScenarioBuilder().WithMana(3).WithHand("Bloodfen Raptor", "Wolfrider").Build();

        SearchResult result = Run(game, iterations: 250);

        Assert.Equal(250, result.Iterations);
        Assert.Equal(250, result.Actions.Sum(a => a.Visits));
    }

    [Fact]
    public void Root_parallel_workers_are_merged()
    {
        Game game = new ScenarioBuilder().WithMana(3).WithHand("Bloodfen Raptor", "Wolfrider").Build();

        SearchResult result = Run(game, iterations: 100, workers: 4);

        Assert.Equal(400, result.Iterations);
        Assert.Equal(400, result.Actions.Sum(a => a.Visits));
    }

    [Fact]
    public void Time_budget_stops_the_search()
    {
        Game game = new ScenarioBuilder().WithMana(5).WithHand("Bloodfen Raptor", "Wolfrider", "Fireball").Build();

        SearchResult result = new Ismcts().Search(game, new SearchOptions { TimeLimit = TimeSpan.FromMilliseconds(300) });

        Assert.True(result.Iterations > 0);
        Assert.True(result.Elapsed < TimeSpan.FromSeconds(2), $"took {result.Elapsed}");
    }

    [Fact]
    public void Search_does_not_mutate_input()
    {
        Game game = new ScenarioBuilder().WithMana(4).WithHand("Abusive Sergeant", "Wolfrider").Build();
        ulong before = StateHasher.Compute(game);

        Run(game, iterations: 100);

        Assert.Equal(before, StateHasher.Compute(game));
    }

    [Fact]
    public void Reports_confidence_and_continuation()
    {
        Game game = new ScenarioBuilder().WithMana(4).WithOpponentHero(5).WithHand("Abusive Sergeant", "Wolfrider").Build();

        RankedAction best = Run(game).Actions[0];

        Assert.True(best.Visits > 0);
        Assert.InRange(best.ConfidenceInterval95, 0, 1);
        Assert.NotEmpty(best.Continuation);
        Assert.Contains("Abusive Sergeant", best.Continuation[0]);
    }

    [Fact]
    public void Information_generating_actions_are_expanded_first()
    {
        // ADR-0004: with one iteration only the first expansion happens; it must be the draw.
        Game game = new ScenarioBuilder().WithMana(10).WithHand("Bloodfen Raptor", "Arcane Intellect").Build();

        SearchResult result = Run(game, iterations: 1);

        RankedAction expanded = Assert.Single(result.Actions, a => a.Visits > 0);
        Assert.Equal("Arcane Intellect", expanded.SourceCardName);
    }

    [Fact]
    public void Greedy_opponent_takes_lethal()
    {
        Game game = new ScenarioBuilder().WithHero(3).WithOpponentBoard("Bloodfen Raptor").Build();
        game.Process(EndTurnTask.Any(game.CurrentPlayer));

        new GreedyOpponentPolicy(new BoardEvaluator()).PlayTurn(game, new Random(0));

        Assert.Equal(PlayState.LOST, game.Player1.PlayState);
    }
}
