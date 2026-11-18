using HsEngine.Overlay;

namespace HsEngine.Overlay.Tests;

/// <summary>The decisions behind the overlay's anchoring, kept free of window handles so they can be tested.</summary>
public class ForegroundPolicyTests
{
    private const uint Game = 100, Own = 200;

    [Fact]
    public void Game_and_overlay_processes_keep_the_overlay_visible()
    {
        Assert.True(ForegroundPolicy.KeepsOverlayVisible(Game, Game, Own, "Hearthstone"));
        Assert.True(ForegroundPolicy.KeepsOverlayVisible(Own, Game, Own, "dotnet"));
    }

    [Theory]
    [InlineData("Hearthstone Deck Tracker")]
    [InlineData("HearthstoneDeckTracker")]
    [InlineData("hearthstone deck tracker")]
    [InlineData("Firestone")]
    public void Deck_tracker_windows_keep_the_overlay_visible(string processName)
    {
        // Another PID: HDT draws its own transparent windows over the game and can take focus.
        Assert.True(ForegroundPolicy.KeepsOverlayVisible(300, Game, Own, processName));
    }

    [Theory]
    [InlineData("chrome")]
    [InlineData("explorer")]
    [InlineData(null)]
    public void Other_applications_hide_the_overlay(string? processName)
    {
        Assert.False(ForegroundPolicy.KeepsOverlayVisible(300, Game, Own, processName));
    }

    [Fact]
    public void No_foreground_window_hides_the_overlay()
    {
        Assert.False(ForegroundPolicy.KeepsOverlayVisible(0, Game, Own, null));
    }
}

public class AnchorLogTests
{
    private static readonly AnchorResult Anchored = new(new PixelRect(10, 20, 1280, 720), null);

    [Fact]
    public void Reports_the_first_anchoring_with_position_and_size()
    {
        Assert.Equal("[Overlay] Ancorado na janela do jogo (10, 20, 1280, 720)", new AnchorLog().Describe(Anchored));
    }

    [Fact]
    public void Stays_quiet_while_nothing_changes()
    {
        var log = new AnchorLog();
        log.Describe(Anchored);

        Assert.Null(log.Describe(Anchored with { Area = new PixelRect(30, 40, 1280, 720) })); // window dragged
        Assert.Null(log.Describe(Anchored));
    }

    [Fact]
    public void Reports_why_the_anchor_was_lost_once_and_again_when_it_comes_back()
    {
        var log = new AnchorLog();
        var lost = new AnchorResult(null, "janela do Hearthstone minimizada");

        Assert.Equal("[Overlay] Sem ancoragem: janela do Hearthstone minimizada", log.Describe(lost));
        Assert.Null(log.Describe(lost));
        Assert.StartsWith("[Overlay] Ancorado", log.Describe(Anchored));
        Assert.Equal("[Overlay] Sem ancoragem: janela do Hearthstone minimizada", log.Describe(lost));
    }

    [Fact]
    public void Reports_a_change_of_reason()
    {
        var log = new AnchorLog();
        log.Describe(new AnchorResult(null, "Hearthstone não está em execução"));

        Assert.Equal("[Overlay] Sem ancoragem: foco em outra janela: chrome (PID 7)",
            log.Describe(new AnchorResult(null, "foco em outra janela: chrome (PID 7)")));
    }
}

public class ClickThroughTests
{
    private const long Layered = 0x80000, Transparent = 0x20, ToolWindow = 0x80, NoActivate = 0x8000000;

    [Fact]
    public void Adds_transparent_without_touching_the_layered_bit()
    {
        long style = ClickThrough.Apply(0x100);

        Assert.NotEqual(0, style & Transparent);
        Assert.Equal(0, style & Layered);
        Assert.Equal(0x100, style & 0x100);
    }

    [Fact]
    public void Keeps_the_layered_bit_wpf_set_for_transparency()
    {
        Assert.Equal(Layered, ClickThrough.Apply(Layered) & Layered);
    }

    [Fact]
    public void Keeps_the_overlay_out_of_alt_tab_and_focus()
    {
        long style = ClickThrough.Apply(0);

        Assert.NotEqual(0, style & ToolWindow);
        Assert.NotEqual(0, style & NoActivate);
    }
}

public class PanelLayoutTests
{
    [Fact]
    public void Scales_physical_pixels_to_dips_under_high_dpi()
    {
        // 1920x1080 client area on a 150% (144 DPI) monitor -> 1280x720 DIPs.
        PanelPlacement layout = PanelLayout.Compute(new PixelRect(0, 0, 1920, 1080), dpiScale: 1.5);

        Assert.Equal(1080, layout.WindowHeightPixels);
        Assert.Equal(1280, layout.DipWidth, precision: 3);
        Assert.Equal(720, layout.DipHeight, precision: 3);
        Assert.Equal(19.2, layout.PanelLeft, precision: 3);
        Assert.Equal(144, layout.PanelTop, precision: 3);
        Assert.InRange(layout.PanelWidth, 240, 460);
        Assert.InRange(layout.FontSize, 11, 22);
        Assert.Equal(layout.FontSize * 0.8, layout.FooterFontSize, precision: 3);
    }

    [Fact]
    public void Shrinks_height_by_one_pixel_when_covering_full_monitor_to_avoid_dwm_occlusion()
    {
        PanelPlacement windowed = PanelLayout.Compute(new PixelRect(50, 50, 1920, 1080), dpiScale: 1.0, coversFullMonitor: false);
        PanelPlacement borderless = PanelLayout.Compute(new PixelRect(0, 0, 1920, 1080), dpiScale: 1.0, coversFullMonitor: true);

        Assert.Equal(1080, windowed.WindowHeightPixels);
        Assert.Equal(1079, borderless.WindowHeightPixels);
    }

    [Fact]
    public void Non_positive_dpi_scale_falls_back_to_identity()
    {
        PanelPlacement layout = PanelLayout.Compute(new PixelRect(0, 0, 1280, 720), dpiScale: 0);

        Assert.Equal(1280, layout.DipWidth);
        Assert.Equal(720, layout.DipHeight);
    }
}
