namespace HsEngine.Overlay;

/// <summary>Screen rectangle in pixels.</summary>
internal readonly record struct PixelRect(int X, int Y, int Width, int Height);

/// <param name="Area">Client area of the game, or null when the overlay cannot anchor.</param>
/// <param name="Reason">Why there is no area (shown in the console).</param>
internal readonly record struct AnchorResult(PixelRect? Area, string? Reason);

/// <summary>DPI-aware WPF dimensions and safe Win32 window height for the overlay panel.</summary>
internal readonly record struct PanelPlacement(
    int WindowHeightPixels,
    double DipWidth,
    double DipHeight,
    double PanelLeft,
    double PanelTop,
    double PanelWidth,
    double FontSize,
    double FooterFontSize);

/// <summary>Which foreground window still counts as "the game is in front" (ADR-0009).</summary>
internal static class ForegroundPolicy
{
    // Deck trackers draw their own transparent windows over the game and can take focus from it.
    private static readonly string[] Companions =
        ["Hearthstone", "Hearthstone Deck Tracker", "HearthstoneDeckTracker", "Firestone"];

    public static bool KeepsOverlayVisible(uint foregroundPid, uint gamePid, uint ownPid, string? foregroundProcessName) =>
        foregroundPid != 0
        && (foregroundPid == gamePid
            || foregroundPid == ownPid
            || (foregroundProcessName is not null && Companions.Contains(foregroundProcessName, StringComparer.OrdinalIgnoreCase)));
}

/// <summary>Turns anchoring results into console messages, one per change (not one per 250 ms tick).</summary>
internal sealed class AnchorLog
{
    private string? _last;

    public string? Describe(AnchorResult result)
    {
        string state = result.Area is null ? $"lost:{result.Reason}" : "anchored";
        if (state == _last)
            return null;
        _last = state;

        return result.Area is { } area
            ? $"[Overlay] Ancorado na janela do jogo ({area.X}, {area.Y}, {area.Width}, {area.Height})"
            : $"[Overlay] Sem ancoragem: {result.Reason}";
    }
}

/// <summary>Extended window style of the overlay: clicks go through to the game, no focus, no Alt+Tab entry.</summary>
internal static class ClickThrough
{
    private const long Transparent = 0x20, ToolWindow = 0x80, NoActivate = 0x8000000;

    // WS_EX_LAYERED is left alone: WPF sets it itself for AllowsTransparency, and a window that does not have it
    // must not get it from here.
    public static long Apply(long style) => style | Transparent | ToolWindow | NoActivate;
}

/// <summary>
/// Converts the physical game client rectangle and monitor DPI scale into device-independent WPF metrics.
/// When the game covers an entire monitor (borderless fullscreen), window height is kept 1 px shy of the monitor
/// height so Windows DWM does not suppress layered composition via independent-flip / fullscreen optimizations.
/// </summary>
internal static class PanelLayout
{
    public static PanelPlacement Compute(PixelRect area, double dpiScale, bool coversFullMonitor = false)
    {
        double scale = dpiScale > 0 ? dpiScale : 1.0;
        int windowHeight = coversFullMonitor && area.Height > 1 ? area.Height - 1 : area.Height;
        double dipWidth = area.Width / scale;
        double dipHeight = windowHeight / scale;
        double fontSize = Math.Clamp(dipHeight * 0.019, 11, 22);
        double panelWidth = Math.Clamp(dipWidth * 0.22, 240, 460);

        return new PanelPlacement(
            windowHeight,
            dipWidth,
            dipHeight,
            PanelLeft: dipWidth * 0.015,
            PanelTop: dipHeight * 0.2,
            PanelWidth: panelWidth,
            FontSize: fontSize,
            FooterFontSize: fontSize * 0.8);
    }
}
