using HsEngine.Search;

namespace HsEngine.Search.Tests;

public class ReplayAnalyzerTests
{
    [Fact]
    public void Analyzes_a_specific_turn_of_recorded_standard_game_and_formats_post_game_summary()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "standard-game1.log");
        var analyzer = new ReplayAnalyzer(new SearchOptions { Iterations = 25, Seed = 1 });

        ReplayReport report = analyzer.Analyze(File.ReadLines(path), onlyTurn: 9);

        TurnReview turn9 = Assert.Single(report.Turns);
        Assert.Equal(9, turn9.Turn);
        Assert.Equal("PALADIN", report.LocalClass);
        Assert.Equal("DRUID", report.OpponentClass);
        Assert.NotNull(report.InferredOpponentArchetype);
        Assert.InRange(turn9.ActualEndTurnEquity, 0.0, 1.0);
        Assert.InRange(turn9.EquityLoss, 0.0, 1.0);

        string text = ReplayReportFormatter.Format(report);
        Assert.Contains("Revisão Pós-Jogo: PALADIN vs DRUID", text);
        Assert.Contains("Turno  9", text);
    }
}
