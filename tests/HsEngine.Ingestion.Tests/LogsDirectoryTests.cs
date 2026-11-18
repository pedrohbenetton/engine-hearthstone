using HsEngine.Ingestion;

namespace HsEngine.Ingestion.Tests;

/// <summary>Finding the Hearthstone Logs folder without --logs: the running game first, then common installs.</summary>
public sealed class LogsDirectoryTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("hsengine-install-").FullName;

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private string Logs(string install, bool withPowerLog)
    {
        string logs = Directory.CreateDirectory(Path.Combine(_root, install, "Logs")).FullName;
        if (withPowerLog)
        {
            string session = Directory.CreateDirectory(Path.Combine(logs, "Hearthstone_2026_11_01_12_00_00")).FullName;
            File.WriteAllText(Path.Combine(session, "Power.log"), "");
        }
        return logs;
    }

    [Fact]
    public void Running_game_gives_the_logs_folder_next_to_its_executable()
    {
        Assert.Equal(Path.Combine(@"E:\Battle.net\Hearthstone", "Logs"),
            LogLocator.LogsNextTo(@"E:\Battle.net\Hearthstone\Hearthstone.exe"));
        Assert.Null(LogLocator.LogsNextTo(null));
    }

    [Fact]
    public void Running_game_wins_over_any_candidate()
    {
        string candidate = Logs("candidate", withPowerLog: true);

        string chosen = LogLocator.Choose(runningGameLogs: @"D:\Games\Hearthstone\Logs", [candidate], fallback: "fallback");

        Assert.Equal(@"D:\Games\Hearthstone\Logs", chosen);
    }

    [Fact]
    public void Without_a_running_game_the_candidate_that_has_a_power_log_wins()
    {
        string empty = Logs("empty", withPowerLog: false);
        string played = Logs("played", withPowerLog: true);

        Assert.Equal(played, LogLocator.Choose(null, [empty, played], "fallback"));
    }

    [Fact]
    public void Without_any_power_log_the_first_existing_candidate_is_used()
    {
        string first = Logs("first", withPowerLog: false);
        string second = Logs("second", withPowerLog: false);

        Assert.Equal(first, LogLocator.Choose(null, [Path.Combine(_root, "missing", "Logs"), first, second], "fallback"));
    }

    [Fact]
    public void Falls_back_when_nothing_exists()
    {
        Assert.Equal("fallback", LogLocator.Choose(null, [Path.Combine(_root, "missing", "Logs")], "fallback"));
    }

    [Fact]
    public void Candidates_cover_the_usual_install_places_on_every_drive()
    {
        List<string> candidates = LogLocator.CandidateDirectories([@"C:\", @"E:\"]).ToList();

        Assert.Contains(@"C:\Program Files (x86)\Hearthstone\Logs", candidates);
        Assert.Contains(@"E:\Battle.net\Hearthstone\Logs", candidates);
        Assert.Contains(@"C:\Program Files\Hearthstone\Logs", candidates);
        Assert.Contains(@"E:\Hearthstone\Logs", candidates);
        Assert.Equal(candidates.Count, candidates.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }
}
