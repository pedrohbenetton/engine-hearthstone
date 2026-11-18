using System.Diagnostics;
using System.Runtime.InteropServices;

namespace HsEngine.Overlay;

/// <summary>
/// Locates the Hearthstone window and positions the overlay through the Win32 window API only (ADR-0009):
/// no memory access, no input injection.
/// </summary>
internal static partial class GameWindow
{
    private static readonly IntPtr HwndTopmost = new(-1);
    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoMove = 0x0002;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpFrameChanged = 0x0020;
    private const uint SwpShowWindow = 0x0040;
    private const uint MonitorDefaultToNearest = 2;

    private static (uint Pid, string? Name) _foregroundName;

    /// <summary>Client area of the game in screen pixels, or the reason there is none.</summary>
    public static AnchorResult Locate(IntPtr overlay, bool alwaysVisible = false)
    {
        IntPtr game = FindGame();
        if (game == IntPtr.Zero)
            return alwaysVisible ? PrimaryScreenFallback() : new AnchorResult(null, "janela do Hearthstone não encontrada (jogo fechado ou ainda carregando)");
        if (IsIconic(game))
            return alwaysVisible ? PrimaryScreenFallback() : new AnchorResult(null, "janela do Hearthstone minimizada");

        if (!alwaysVisible)
        {
            GetWindowThreadProcessId(game, out uint gamePid);
            IntPtr foreground = GetForegroundWindow();
            GetWindowThreadProcessId(foreground, out uint foregroundPid);
            uint ownPid = (uint)Environment.ProcessId;

            // Process name only when it can matter: the game and the overlay itself are decided by PID.
            string? name = foregroundPid == gamePid || foregroundPid == ownPid ? null : ProcessName(foregroundPid);
            if (!ForegroundPolicy.KeepsOverlayVisible(foregroundPid, gamePid, ownPid, name))
                return new AnchorResult(null, $"foco em outra janela: {name ?? "?"} (PID {foregroundPid})");
        }

        if (!GetClientRect(game, out NativeRect client) || client.Right <= client.Left || client.Bottom <= client.Top)
            return new AnchorResult(null, "área cliente do Hearthstone vazia");
        var origin = new NativePoint();
        if (!ClientToScreen(game, ref origin))
            return new AnchorResult(null, "não foi possível converter a área cliente para a tela");
        return new AnchorResult(new PixelRect(origin.X, origin.Y, client.Right - client.Left, client.Bottom - client.Top), null);
    }

    /// <summary>Lets clicks through, keeps the overlay out of Alt+Tab/focus, and flushes cached Win32 frame styles.</summary>
    public static void MakeClickThrough(IntPtr window)
    {
        const int GwlExStyle = -20;
        long style = GetWindowLongPtr(window, GwlExStyle).ToInt64();
        SetWindowLongPtr(window, GwlExStyle, new IntPtr(ClickThrough.Apply(style)));
        SetWindowPos(window, HwndTopmost, 0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpNoActivate | SwpFrameChanged);
    }

    /// <summary>
    /// Places the overlay directly in Win32 screen pixels and re-asserts <c>HWND_TOPMOST</c> without activating it,
    /// keeping it above Unity and companion overlays (HDT) across focus changes and mixed-DPI multi-monitor setups.
    /// </summary>
    public static void PlaceTopmost(IntPtr window, PixelRect area, int heightPixels) =>
        SetWindowPos(window, HwndTopmost, area.X, area.Y, area.Width, heightPixels, SwpNoActivate | SwpShowWindow);

    public static double DpiScale(IntPtr window)
    {
        uint dpi = window != IntPtr.Zero ? GetDpiForWindow(window) : 96;
        return dpi > 0 ? dpi / 96.0 : 1.0;
    }

    public static bool CoversMonitor(IntPtr window, PixelRect area)
    {
        IntPtr monitor = MonitorFromWindow(window, MonitorDefaultToNearest);
        if (monitor == IntPtr.Zero)
            return false;
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (!GetMonitorInfo(monitor, ref info))
            return false;
        NativeRect m = info.Monitor;
        return area.X == m.Left && area.Y == m.Top && area.Width == m.Right - m.Left && area.Height == m.Bottom - m.Top;
    }

    private static AnchorResult PrimaryScreenFallback()
    {
        int width = GetSystemMetrics(0);
        int height = GetSystemMetrics(1);
        return width > 0 && height > 0
            ? new AnchorResult(new PixelRect(0, 0, width, height), null)
            : new AnchorResult(null, "área da tela principal indisponível");
    }

    // The class name and title are the usual route; the process list covers a game window that differs from them.
    private static IntPtr FindGame()
    {
        IntPtr game = FindWindow("UnityWndClass", "Hearthstone");
        if (game != IntPtr.Zero)
            return game;
        foreach (Process process in Process.GetProcessesByName("Hearthstone"))
            using (process)
                if (process.MainWindowHandle != IntPtr.Zero)
                    return process.MainWindowHandle;
        return IntPtr.Zero;
    }

    private static string? ProcessName(uint pid)
    {
        if (_foregroundName.Pid == pid)
            return _foregroundName.Name;
        string? name = null;
        try
        {
            using Process process = Process.GetProcessById((int)pid);
            name = process.ProcessName;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            // The window's process ended between the two calls.
        }
        _foregroundName = (pid, name);
        return name;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left, Top, Right, Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X, Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public int Size;
        public NativeRect Monitor;
        public NativeRect Work;
        public uint Flags;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindow(string className, string windowName);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsIconic(IntPtr window);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetClientRect(IntPtr window, out NativeRect rect);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ClientToScreen(IntPtr window, ref NativePoint point);

    [DllImport("user32.dll")]
    private static extern IntPtr GetWindowLongPtr(IntPtr window, int index);

    [DllImport("user32.dll")]
    private static extern IntPtr SetWindowLongPtr(IntPtr window, int index, IntPtr value);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(IntPtr window, IntPtr insertAfter, int x, int y, int cx, int cy, uint flags);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr window);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr window, uint flags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int index);
}
