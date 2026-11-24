using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Journal.App.Controls;

/// <summary>One point of a graph: the day it was written, its label ("Week of Nov 17") and one or two values.</summary>
public sealed record ChartPoint(DateOnly Date, string Label, double First, double? Second, string ValueText);

/// <summary>
/// A small line graph for tracked values (ISS-030), drawn with WPF shapes so no charting package is needed.
/// A paired value such as blood pressure draws two lines. Hovering over a point shows its date and value; clicking it
/// runs <see cref="PointCommand"/> with the point. With <see cref="ShowAxes"/> off it is a plain sparkline.
/// </summary>
public sealed class LineChart : FrameworkElement
{
    public static readonly DependencyProperty PointsProperty = DependencyProperty.Register(
        nameof(Points), typeof(IReadOnlyList<ChartPoint>), typeof(LineChart),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ShowAxesProperty = DependencyProperty.Register(
        nameof(ShowAxes), typeof(bool), typeof(LineChart),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty PointCommandProperty = DependencyProperty.Register(
        nameof(PointCommand), typeof(ICommand), typeof(LineChart));

    public IReadOnlyList<ChartPoint>? Points
    {
        get => (IReadOnlyList<ChartPoint>?)GetValue(PointsProperty);
        set => SetValue(PointsProperty, value);
    }

    public bool ShowAxes
    {
        get => (bool)GetValue(ShowAxesProperty);
        set => SetValue(ShowAxesProperty, value);
    }

    public ICommand? PointCommand
    {
        get => (ICommand?)GetValue(PointCommandProperty);
        set => SetValue(PointCommandProperty, value);
    }

    private int _hover = -1;
    private readonly List<Point> _positions = [];

    public LineChart()
    {
        SnapsToDevicePixels = true;
        ToolTipService.SetInitialShowDelay(this, 0);
        ToolTipService.SetBetweenShowDelay(this, 0);
    }

    private Brush Res(string key, Color fallback) => TryFindResource(key) as Brush ?? new SolidColorBrush(fallback);

    private Thickness PlotMargin => ShowAxes ? new Thickness(48, 10, 14, 24) : new Thickness(4, 4, 4, 4);

    protected override void OnRender(DrawingContext dc)
    {
        _positions.Clear();
        var size = new Size(ActualWidth, ActualHeight);
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(size));   // so the whole area takes the mouse
        var points = Points ?? [];
        var m = PlotMargin;
        var plot = new Rect(m.Left, m.Top, Math.Max(1, size.Width - m.Left - m.Right), Math.Max(1, size.Height - m.Top - m.Bottom));
        var muted = Res("InkMuted", Colors.Gray);
        var line = Res("Line", Colors.LightGray);
        var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var typeface = new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
        FormattedText Text(string s, Brush b) => new(s, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, typeface, 11, b, dpi);

        if (points.Count == 0)
        {
            if (ShowAxes)
            {
                var t = Text("No values yet", muted);
                dc.DrawText(t, new Point((size.Width - t.Width) / 2, (size.Height - t.Height) / 2));
            }
            return;
        }

        var values = points.SelectMany(p => p.Second is { } s ? new[] { p.First, s } : [p.First]).ToList();
        double lo = values.Min(), hi = values.Max();
        if (hi - lo < 1e-9) { lo -= 1; hi += 1; }
        var pad = (hi - lo) * 0.12;
        lo -= pad;
        hi += pad;
        double first = points[0].Date.DayNumber, last = points[^1].Date.DayNumber;
        double X(DateOnly d) => last == first ? plot.Left + plot.Width / 2 : plot.Left + (d.DayNumber - first) / (last - first) * plot.Width;
        double Y(double v) => plot.Bottom - (v - lo) / (hi - lo) * plot.Height;

        if (ShowAxes)
        {
            var grid = new Pen(line, 1);
            for (int i = 0; i <= 2; i++)
            {
                var v = lo + (hi - lo) * (0.12 / 1.24 + i * 0.5 / 1.24);
                var y = Math.Round(Y(v)) + 0.5;
                dc.DrawLine(grid, new Point(plot.Left, y), new Point(plot.Right, y));
                var label = Text(v.ToString("0.#", CultureInfo.CurrentCulture), muted);
                dc.DrawText(label, new Point(plot.Left - label.Width - 6, y - label.Height / 2));
            }
            var from = Text(points[0].Date.ToString("MMM d, yyyy", CultureInfo.CurrentCulture), muted);
            dc.DrawText(from, new Point(plot.Left, plot.Bottom + 6));
            if (points.Count > 1)
            {
                var to = Text(points[^1].Date.ToString("MMM d, yyyy", CultureInfo.CurrentCulture), muted);
                dc.DrawText(to, new Point(plot.Right - to.Width, plot.Bottom + 6));
            }
        }

        var accent = Res("Accent", Color.FromRgb(0x1F, 0x6F, 0x78));
        var second = Res("Today", Color.FromRgb(0xB7, 0x79, 0x1F));
        double radius = ShowAxes ? 3.5 : 2.2;
        double thickness = ShowAxes ? 2 : 1.5;
        DrawSeries(dc, points.Select(p => new Point(X(p.Date), Y(p.First))).ToList(), accent, thickness, radius);
        if (points.Any(p => p.Second is not null))
            DrawSeries(dc, points.Where(p => p.Second is not null).Select(p => new Point(X(p.Date), Y(p.Second!.Value))).ToList(), second, thickness, radius);
        _positions.AddRange(points.Select(p => new Point(X(p.Date), Y(p.First))));

        if (_hover >= 0 && _hover < _positions.Count)
            dc.DrawEllipse(null, new Pen(accent, 2), _positions[_hover], radius + 3, radius + 3);
    }

    private static void DrawSeries(DrawingContext dc, List<Point> pts, Brush brush, double thickness, double radius)
    {
        if (pts.Count > 1)
        {
            var geometry = new StreamGeometry();
            using (var ctx = geometry.Open())
            {
                ctx.BeginFigure(pts[0], false, false);
                ctx.PolyLineTo(pts.Skip(1).ToList(), true, true);
            }
            geometry.Freeze();
            dc.DrawGeometry(null, new Pen(brush, thickness) { LineJoin = PenLineJoin.Round }, geometry);
        }
        foreach (var p in pts) dc.DrawEllipse(brush, null, p, radius, radius);
    }

    /// <summary>The point nearest the mouse across, if it is within 24 pixels.</summary>
    private int Nearest(Point mouse)
    {
        int best = -1;
        double bestDistance = 24;
        for (int i = 0; i < _positions.Count; i++)
        {
            var d = Math.Abs(_positions[i].X - mouse.X);
            if (d < bestDistance) { best = i; bestDistance = d; }
        }
        return best;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var i = Nearest(e.GetPosition(this));
        if (i == _hover) return;
        _hover = i;
        var points = Points ?? [];
        ToolTip = i >= 0 && i < points.Count ? $"{points[i].Label}: {points[i].ValueText}" : null;
        Cursor = i >= 0 && PointCommand is not null ? Cursors.Hand : null;
        InvalidateVisual();
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        _hover = -1;
        ToolTip = null;
        InvalidateVisual();
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        var points = Points ?? [];
        var i = Nearest(e.GetPosition(this));
        if (i < 0 || i >= points.Count || PointCommand is not { } command || !command.CanExecute(points[i])) return;
        command.Execute(points[i]);
        e.Handled = true;
    }
}
