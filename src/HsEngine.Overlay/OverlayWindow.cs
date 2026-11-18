using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using HsEngine.Search;

namespace HsEngine.Overlay;

/// <summary>
/// Transparent, click-through window laid over the game's client area (ADR-0009, phases 1 &amp; 2: panel + board cues).
/// It follows the game window and hides itself whenever the game is not in front (unless <c>--always-visible</c> is set).
/// </summary>
internal sealed class OverlayWindow : Window
{
    private readonly bool _alwaysVisible;
    private readonly Canvas _cues = new() { IsHitTestVisible = false };
    private readonly Border _panel;
    private readonly TextBlock _header = Text(FontWeights.Bold, Color.FromRgb(0xFF, 0xD7, 0x6E));
    private readonly StackPanel _steps = new();
    private readonly TextBlock _footer = Text(FontWeights.Normal, Color.FromRgb(0xB8, 0xB8, 0xB8));
    private readonly DispatcherTimer _follow = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private readonly AnchorLog _anchorLog = new();
    private PanelPlacement _currentLayout = PanelLayout.Compute(new PixelRect(0, 0, 1280, 720), 1.0);
    private Recommendation? _currentRecommendation;
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
        canvas.Children.Add(_cues);
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

    public void Show(PanelContent content, Recommendation? recommendation = null)
    {
        _currentRecommendation = recommendation;
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
        RenderCues();
    }

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
        bool sizeChanged = Math.Abs(layout.DipWidth - _currentLayout.DipWidth) > 0.5
                           || Math.Abs(layout.DipHeight - _currentLayout.DipHeight) > 0.5;
        _currentLayout = layout;

        GameWindow.PlaceTopmost(_handle, area, layout.WindowHeightPixels);
        ApplyTextSizes(layout);
        _panel.Width = layout.PanelWidth;
        Canvas.SetLeft(_panel, layout.PanelLeft);
        Canvas.SetTop(_panel, layout.PanelTop);
        if (sizeChanged)
            RenderCues();
        Opacity = 1;
    }

    private void RenderCues()
    {
        _cues.Children.Clear();
        if (BoardGeometry.FromRecommendation(_currentRecommendation, _currentLayout.DipWidth, _currentLayout.DipHeight) is not { } cue)
            return;

        if (cue.Source is { } source)
            AddRing(source);
        if (cue.Destination is { } dest)
            AddRing(dest);
        if (cue.Arrow is { } arrow)
            AddArrow(arrow);
    }

    private void AddRing(HighlightRing ring)
    {
        Color stroke = Palette(ring.Accent);

        // Soft outer halo for legibility over bright board art.
        var halo = new Ellipse
        {
            Width = ring.RadiusX * 2 + 8,
            Height = ring.RadiusY * 2 + 8,
            Stroke = new SolidColorBrush(Color.FromArgb(0x66, stroke.R, stroke.G, stroke.B)),
            StrokeThickness = 7,
            Fill = new SolidColorBrush(Color.FromArgb(0x1E, stroke.R, stroke.G, stroke.B)),
        };
        Canvas.SetLeft(halo, ring.Center.X - ring.RadiusX - 4);
        Canvas.SetTop(halo, ring.Center.Y - ring.RadiusY - 4);
        _cues.Children.Add(halo);

        var core = new Ellipse
        {
            Width = ring.RadiusX * 2,
            Height = ring.RadiusY * 2,
            Stroke = new SolidColorBrush(stroke),
            StrokeThickness = 3,
        };
        Canvas.SetLeft(core, ring.Center.X - ring.RadiusX);
        Canvas.SetTop(core, ring.Center.Y - ring.RadiusY);
        _cues.Children.Add(core);
    }

    private void AddArrow(OverlayArrow arrow)
    {
        Color color = Palette(arrow.Accent);
        var strokeBrush = new SolidColorBrush(color);
        var shadowBrush = new SolidColorBrush(Color.FromArgb(0x90, 0x00, 0x00, 0x00));

        var curveGeo = new PathGeometry([
            new PathFigure(
                new Point(arrow.Start.X, arrow.Start.Y),
                [new QuadraticBezierSegment(new Point(arrow.Control.X, arrow.Control.Y), new Point(arrow.End.X, arrow.End.Y), isStroked: true)],
                closed: false),
        ]);

        _cues.Children.Add(new System.Windows.Shapes.Path
        {
            Data = curveGeo,
            Stroke = shadowBrush,
            StrokeThickness = 7,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
        });
        _cues.Children.Add(new System.Windows.Shapes.Path
        {
            Data = curveGeo,
            Stroke = strokeBrush,
            StrokeThickness = 3.5,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
        });

        var headGeo = new PathGeometry([
            new PathFigure(
                new Point(arrow.End.X, arrow.End.Y),
                [new PolyLineSegment([new Point(arrow.WingLeft.X, arrow.WingLeft.Y), new Point(arrow.WingRight.X, arrow.WingRight.Y)], isStroked: true)],
                closed: true),
        ]);
        _cues.Children.Add(new System.Windows.Shapes.Path
        {
            Data = headGeo,
            Fill = strokeBrush,
            Stroke = shadowBrush,
            StrokeThickness = 1.5,
            StrokeLineJoin = PenLineJoin.Round,
        });
    }

    private static Color Palette(OverlayAccent accent) => accent switch
    {
        OverlayAccent.Crimson => Color.FromRgb(0xFF, 0x4D, 0x4D),
        OverlayAccent.Emerald => Color.FromRgb(0x4A, 0xDE, 0x80),
        OverlayAccent.Cyan => Color.FromRgb(0x38, 0xBD, 0xF8),
        _ => Color.FromRgb(0xFF, 0xD7, 0x6E),
    };

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
