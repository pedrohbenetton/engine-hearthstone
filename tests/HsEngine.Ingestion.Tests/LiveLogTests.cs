using HsEngine.Ingestion;

namespace HsEngine.Ingestion.Tests;

public sealed class LiveLogTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("hsengine-logs-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private string Session(string name, bool withPowerLog = true)
    {
        string folder = Directory.CreateDirectory(Path.Combine(_dir, name)).FullName;
        if (withPowerLog)
            File.WriteAllText(Path.Combine(folder, "Power.log"), "");
        return folder;
    }

    [Fact]
    public void Locator_picks_the_newest_session_with_a_power_log()
    {
        Session("Hearthstone_2026_10_28_15_38_24");
        string newest = Session("Hearthstone_2026_10_31_23_17_13");
        Session("Hearthstone_2026_11_01_09_00_00", withPowerLog: false); // game just started, nothing logged yet

        Assert.Equal(Path.Combine(newest, "Power.log"), LogLocator.LatestPowerLog(_dir));
    }

    [Fact]
    public void Locator_returns_null_when_there_is_no_log()
    {
        Assert.Null(LogLocator.LatestPowerLog(_dir));
        Assert.Null(LogLocator.LatestPowerLog(Path.Combine(_dir, "missing")));
    }

    [Fact]
    public void Tail_returns_only_complete_lines_appended_since_the_last_read()
    {
        string path = Path.Combine(Session("Hearthstone_2026_11_01_10_00_00"), "Power.log");
        File.WriteAllText(path, "first\nsecond\n");
        using var tail = new LogTail(path);

        Assert.Equal(["first", "second"], tail.ReadNewLines());
        Assert.Empty(tail.ReadNewLines());

        File.AppendAllText(path, "thi");
        Assert.Empty(tail.ReadNewLines());

        File.AppendAllText(path, "rd\r\nfourth\n");
        Assert.Equal(["third", "fourth"], tail.ReadNewLines());
    }

    [Fact]
    public void Tail_restarts_when_the_file_is_truncated()
    {
        string path = Path.Combine(Session("Hearthstone_2026_11_01_11_00_00"), "Power.log");
        File.WriteAllText(path, "old line one\nold line two\n");
        using var tail = new LogTail(path);
        tail.ReadNewLines();

        File.WriteAllText(path, "new\n");

        Assert.Equal(["new"], tail.ReadNewLines());
    }
}
