using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using HsEngine.Search;

namespace HsEngine.Overlay;

/// <summary>
/// Transparent, click-through window laid over the game's client area (ADR-0009, phase 1: panel). It follows the
/// game window and hides itself whenever the game is not in front (unless <c>--always-visible</c> is set).
/// </summary>
internal sealed class OverlayWindow : Window
{
    private readonly bool _alwaysVisible;
    private readonly Border _panel;
    private readonly TextBlock _header = Text(FontWeights.Bold, Color.FromRgb(0xFF, 0xD7, 0x6E));
    private readonly StackPanel _steps = new();
    private readonly TextBlock _footer = Text(FontWeights.Normal, Color.FromRgb(0xB8, 0xB8, 0xB8));
    private readonly DispatcherTimer _follow = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private readonly AnchorLog _anchorLog = new();
    private IntPtr _handle;

    public OverlayWindow(bool alwaysVisible = false)
    {
        _alwaysVisible = alwaysVisible;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = false;
        ResizeMode = ResizeMode.NoResize;
        Left = 0;
        Top = 0;
        Width = 1;
        Height = 1;
        Opacity = 0;

        var content = new StackPanel();
        content.Children.Add(_header);
        content.Children.Add(_steps);
        content.Children.Add(_footer);
        _panel = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0xD9, 0x14, 0x10, 0x0C)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x8A, 0x6A, 0x2E)),
            BorderThickness = new Thickness(2),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(12, 10, 12, 10),
            Child = content,
        };
        var canvas = new Canvas();
        canvas.Children.Add(_panel);
        Content = canvas;

        Show(PanelText.Waiting);
        SourceInitialized += (_, _) =>
        {
            _handle = new WindowInteropHelper(this).Handle;
            GameWindow.MakeClickThrough(_handle);
            FollowGame();
        };
        _follow.Tick += (_, _) => FollowGame();
        _follow.Start();
    }

    public void Show(PanelContent content)
    {
        _header.Text = content.Header;
        _steps.Children.Clear();
        foreach (string step in content.Steps)
        {
            TextBlock line = Text(FontWeights.Normal, Colors.White);
            line.Text = step;
            line.Margin = new Thickness(0, 4, 0, 0);
            _steps.Children.Add(line);
        }
        _footer.Text = content.Footer;
        _footer.Visibility = content.Footer.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        ApplyTextSizes(_currentLayout);
    }

    private PanelPlacement _currentLayout = PanelLayout.Compute(new PixelRect(0, 0, 1280, 720), 1.0);

    private void FollowGame()
    {
        if (_handle == IntPtr.Zero)
            return;

        AnchorResult anchor = GameWindow.Locate(_handle, _alwaysVisible);
        if (_anchorLog.Describe(anchor) is { } message)
            Console.WriteLine(message);
        if (anchor.Area is not { } area)
        {
            Opacity = 0;
            return;
        }

        double scale = GameWindow.DpiScale(_handle);
        bool fullMonitor = GameWindow.CoversMonitor(_handle, area);
        PanelPlacement layout = PanelLayout.Compute(area, scale, fullMonitor);
        _currentLayout = layout;

        GameWindow.PlaceTopmost(_handle, area, layout.WindowHeightPixels);
        ApplyTextSizes(layout);
        _panel.Width = layout.PanelWidth;
        Canvas.SetLeft(_panel, layout.PanelLeft);
        Canvas.SetTop(_panel, layout.PanelTop);
        Opacity = 1;
    }

    private void ApplyTextSizes(PanelPlacement layout)
    {
        _header.FontSize = layout.FontSize;
        _footer.FontSize = layout.FooterFontSize;
        foreach (TextBlock step in _steps.Children.OfType<TextBlock>())
            step.FontSize = layout.FontSize;
    }

    private static TextBlock Text(FontWeight weight, Color color) => new()
    {
        FontWeight = weight,
        Foreground = new SolidColorBrush(color),
        TextWrapping = TextWrapping.Wrap,
        FontFamily = new FontFamily("Segoe UI"),
    };
}
