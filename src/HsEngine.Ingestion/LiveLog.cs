using System.Text;

namespace HsEngine.Ingestion;

/// <summary>Finds the Power.log of the current game session.</summary>
public static class LogLocator
{
    /// <summary>
    /// The game writes one folder per launch, named Hearthstone_yyyy_MM_dd_HH_mm_ss, so the newest one sorts last.
    /// Folders without a Power.log yet (game still loading) are skipped.
    /// </summary>
    public static string? LatestPowerLog(string logsDirectory)
    {
        if (!Directory.Exists(logsDirectory))
            return null;
        return Directory.GetDirectories(logsDirectory, "Hearthstone_*")
            .Order(StringComparer.Ordinal)
            .Select(d => Path.Combine(d, "Power.log"))
            .LastOrDefault(File.Exists);
    }

    public const string DefaultLogsDirectory = @"C:\Program Files (x86)\Hearthstone\Logs";

    private static readonly string[] InstallFolders =
    [
        @"Battle.net\Hearthstone", @"Program Files (x86)\Hearthstone", @"Program Files\Hearthstone", @"Hearthstone",
        @"Program Files (x86)\Battle.net\Hearthstone", @"Games\Hearthstone",
    ];

    /// <summary>The Logs folder the game writes next to its executable.</summary>
    public static string? LogsNextTo(string? gameExecutable) =>
        Path.GetDirectoryName(gameExecutable) is { Length: > 0 } directory ? Path.Combine(directory, "Logs") : null;

    /// <summary>Usual install places of the game on each drive.</summary>
    public static IEnumerable<string> CandidateDirectories(IEnumerable<string> driveRoots) =>
        driveRoots
            .SelectMany(root => InstallFolders.Select(folder => Path.Combine(root, folder, "Logs")))
            .Distinct(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The running game's own folder wins. Otherwise the first candidate that already has a Power.log, then the
    /// first that exists, then <paramref name="fallback"/>.
    /// </summary>
    public static string Choose(string? runningGameLogs, IEnumerable<string> candidates, string fallback)
    {
        if (runningGameLogs is not null)
            return runningGameLogs;
        List<string> existing = candidates.Where(Directory.Exists).ToList();
        return existing.FirstOrDefault(d => LatestPowerLog(d) is not null) ?? existing.FirstOrDefault() ?? fallback;
    }

    /// <summary>Finds the Logs folder of the installed game (running process first, then the usual places).</summary>
    public static string DetectLogsDirectory() =>
        Choose(LogsNextTo(RunningGameExecutable()),
            CandidateDirectories(DriveInfo.GetDrives().Where(d => d.DriveType == DriveType.Fixed).Select(d => d.RootDirectory.FullName)),
            DefaultLogsDirectory);

    private static string? RunningGameExecutable()
    {
        foreach (System.Diagnostics.Process process in System.Diagnostics.Process.GetProcessesByName("Hearthstone"))
            using (process)
            {
                try
                {
                    if (process.MainModule?.FileName is { } path)
                        return path;
                }
                catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or NotSupportedException)
                {
                    // Access denied (elevated or other-bitness process): fall back to the usual places.
                }
            }
        return null;
    }
}

/// <summary>
/// Follows a log file the game is still writing (ADR-0005: passive read only). Each call returns the complete
/// lines appended since the previous one; a trailing partial line waits for its newline. Lines are split on bytes,
/// so a UTF-8 character cut across two writes is decoded intact.
/// </summary>
public sealed class LogTail(string path) : IDisposable
{
    private readonly FileStream _stream = new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
    private readonly List<byte> _partial = [];
    private readonly byte[] _buffer = new byte[64 * 1024];

    public string Path { get; } = path;

    public List<string> ReadNewLines()
    {
        if (_stream.Length < _stream.Position)
        {
            // Truncated or rewritten: start over.
            _stream.Position = 0;
            _partial.Clear();
        }

        var lines = new List<string>();
        int read;
        while ((read = _stream.Read(_buffer, 0, _buffer.Length)) > 0)
        {
            int start = 0;
            for (int i = 0; i < read; i++)
            {
                if (_buffer[i] != (byte)'\n')
                    continue;
                _partial.AddRange(_buffer.AsSpan(start, i - start));
                lines.Add(Encoding.UTF8.GetString([.. _partial]).TrimEnd('\r'));
                _partial.Clear();
                start = i + 1;
            }
            _partial.AddRange(_buffer.AsSpan(start, read - start));
        }
        return lines;
    }

    public void Dispose() => _stream.Dispose();
}
