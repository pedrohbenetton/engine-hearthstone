using System.Windows;
using HsEngine.Ingestion;
using HsEngine.Search;

namespace HsEngine.Overlay;

// Usage: HsEngine.Overlay [--logs DIR] [--deck CODE] [--time-ms T] [--workers W] [--always-visible]
// Shows the live recommendation over the game window (ADR-0009). Ctrl+C in this console closes it.
internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        string logs = Option(args, "--logs") ?? LogLocator.DetectLogsDirectory();
        bool alwaysVisible = args.Contains("--always-visible", StringComparer.Ordinal);
        Console.WriteLine($"Pasta de logs: {logs}");
        var options = new SearchOptions
        {
            TimeLimit = TimeSpan.FromMilliseconds(int.Parse(Option(args, "--time-ms") ?? "3000")),
            Workers = int.Parse(Option(args, "--workers") ?? Environment.ProcessorCount.ToString()),
            Seed = Environment.TickCount,
        };

        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        var window = new OverlayWindow(alwaysVisible);
        using var stop = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            stop.Cancel();
            app.Dispatcher.BeginInvoke(() => app.Shutdown());
        };

        var session = new LiveSession(logs, options, Option(args, "--deck"));
        session.Status += Console.WriteLine;
        session.Failed += ex => Console.Error.WriteLine($"Não foi possível recomendar esta posição: {ex.Message}");
        session.LocalTurnChanged += localTurn =>
        {
            PanelContent state = localTurn ? PanelText.Thinking : PanelText.Waiting;
            window.Dispatcher.BeginInvoke(() => window.Show(state));
        };
        session.Recommended += recommendation =>
        {
            Console.WriteLine(RecommendationFormatter.Format(recommendation));
            PanelContent panel = PanelText.From(recommendation);
            window.Dispatcher.BeginInvoke(() => window.Show(panel, recommendation));
        };
        Task live = Task.Run(() => session.Run(stop.Token));

        Console.WriteLine("Overlay ativo. Ctrl+C para sair.");
        window.Show();
        app.Run();
        stop.Cancel();
        live.Wait(TimeSpan.FromSeconds(5));
        return 0;
    }

    private static string? Option(string[] args, string name)
    {
        int i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }
}
