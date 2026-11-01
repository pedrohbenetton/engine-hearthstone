using HsEngine.Ingestion;
using HsEngine.Search;
using HsEngine.Simulation;
using Xunit.Abstractions;

namespace HsEngine.Search.Tests;

/// <summary>End to end: Power.log → state machine → observation → SabberStone game → ISMCTS recommendation.</summary>
public class RecommendationSmokeTests(ITestOutputHelper output)
{
    private static ObservedState ObservedAtMainAction(int turn)
    {
        var state = new GameStateMachine();
        foreach (PowerEvent e in PowerLogParser.Parse(File.ReadLines(Path.Combine(AppContext.BaseDirectory, "Fixtures", "standard-game1.log"))))
        {
            state.Apply(e);
            if (e is TagChange { Tag: "STEP", Value: "MAIN_ACTION" } && state.Turn == turn)
                break;
        }
        return Observation.From(state);
    }

    [Fact]
    public void Recommends_a_turn_from_a_real_standard_position()
    {
        ConvertedGame converted = ObservedGameBuilder.Build(ObservedAtMainAction(9));

        SearchResult result = new Ismcts().Search(converted.Game, new SearchOptions { Iterations = 100, Seed = 1 });

        foreach (RankedAction a in result.Actions.Take(5))
            output.WriteLine($"{a.WinProbability:P0} ({a.Visits} visits) {a.Description} | {string.Join(" > ", a.Continuation)}");
        output.WriteLine($"proxies: {string.Join(", ", converted.Report.Proxies)}");

        Assert.Equal(100, result.Iterations);
        Assert.NotEmpty(result.Actions);
        Assert.All(result.Actions, a => Assert.NotEqual("Hidden card", a.SourceCardName));
        Assert.All(result.Actions, a => Assert.InRange(a.WinProbability, 0.0, 1.0));
    }
}
