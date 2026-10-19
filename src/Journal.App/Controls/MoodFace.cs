using System.Windows;
using System.Windows.Media;
using Journal.Core.Domain;

namespace Journal.App.Controls;

/// <summary>
/// A small line-drawn face for a mood: frown (sad), straight mouth (neutral) or smile (happy) (FR-MOOD-01, FR-MOOD-03).
/// Drawn rather than an emoji so it looks the same at every size and doesn't depend on a colour-emoji font.
/// </summary>
public sealed class MoodFace : FrameworkElement
{
    public static readonly DependencyProperty MoodProperty = DependencyProperty.Register(
        nameof(Mood), typeof(Mood), typeof(MoodFace), new FrameworkPropertyMetadata(Mood.None, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty StrokeProperty = DependencyProperty.Register(
        nameof(Stroke), typeof(Brush), typeof(MoodFace), new FrameworkPropertyMetadata(Brushes.Black, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty FillProperty = DependencyProperty.Register(
        nameof(Fill), typeof(Brush), typeof(MoodFace), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public Mood Mood
    {
        get => (Mood)GetValue(MoodProperty);
        set => SetValue(MoodProperty, value);
    }

    public Brush Stroke
    {
        get => (Brush)GetValue(StrokeProperty);
        set => SetValue(StrokeProperty, value);
    }

    public Brush? Fill
    {
        get => (Brush?)GetValue(FillProperty);
        set => SetValue(FillProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize) => new(
        double.IsNaN(Width) ? 16 : Width, double.IsNaN(Height) ? 16 : Height);

    protected override void OnRender(DrawingContext dc)
    {
        if (Mood == Mood.None) return;
        double size = Math.Min(ActualWidth, ActualHeight);
        if (size <= 0) return;
        double thickness = Math.Max(1, size / 12);
        var pen = new Pen(Stroke, thickness) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        var c = new Point(ActualWidth / 2, ActualHeight / 2);
        double r = size / 2 - thickness / 2;
        dc.DrawEllipse(Fill, pen, c, r, r);

        // Eyes.
        double eye = Math.Max(0.8, size / 14);
        dc.DrawEllipse(Stroke, null, new Point(c.X - r * 0.36, c.Y - r * 0.22), eye, eye);
        dc.DrawEllipse(Stroke, null, new Point(c.X + r * 0.36, c.Y - r * 0.22), eye, eye);

        // Mouth.
        var left = new Point(c.X - r * 0.42, c.Y + r * 0.36);
        var right = new Point(c.X + r * 0.42, c.Y + r * 0.36);
        if (Mood == Mood.Neutral)
        {
            dc.DrawLine(pen, left, right);
            return;
        }
        double bend = r * 0.3 * (Mood == Mood.Happy ? 1 : -1);
        if (Mood == Mood.Sad)
        {
            left.Y += r * 0.12;
            right.Y += r * 0.12;
        }
        var mouth = new StreamGeometry();
        using (var g = mouth.Open())
        {
            g.BeginFigure(left, false, false);
            g.QuadraticBezierTo(new Point(c.X, left.Y + bend * 1.6), right, true, true);
        }
        mouth.Freeze();
        dc.DrawGeometry(null, pen, mouth);
    }
}

/// <summary>A face and a number, e.g. a smile and "12", for the mood counts under the calendar (FR-MOOD-04).</summary>
public sealed class MoodCount : System.Windows.Controls.StackPanel
{
    public static readonly DependencyProperty MoodProperty = DependencyProperty.Register(
        nameof(Mood), typeof(Mood), typeof(MoodCount), new PropertyMetadata(Mood.None, (d, _) => ((MoodCount)d).Refresh()));

    public static readonly DependencyProperty CountProperty = DependencyProperty.Register(
        nameof(Count), typeof(int), typeof(MoodCount), new PropertyMetadata(0, (d, _) => ((MoodCount)d).Refresh()));

    private readonly MoodFace _face = new() { Width = 14, Height = 14, VerticalAlignment = VerticalAlignment.Center };
    private readonly System.Windows.Controls.TextBlock _text = new()
    {
        Margin = new Thickness(4, 0, 0, 0), MinWidth = 22, VerticalAlignment = VerticalAlignment.Center,
    };

    public MoodCount()
    {
        Orientation = System.Windows.Controls.Orientation.Horizontal;
        Margin = new Thickness(10, 0, 0, 0);
        Children.Add(_face);
        Children.Add(_text);
        Loaded += (_, _) => Refresh();
    }

    public Mood Mood
    {
        get => (Mood)GetValue(MoodProperty);
        set => SetValue(MoodProperty, value);
    }

    public int Count
    {
        get => (int)GetValue(CountProperty);
        set => SetValue(CountProperty, value);
    }

    /// <summary>The number shown (for tests).</summary>
    public string Text => _text.Text;

    private void Refresh()
    {
        _face.Mood = Mood;
        if (TryFindResource($"Mood{Mood}Ink") is Brush ink) _face.Stroke = ink;
        _text.Text = Count.ToString(System.Globalization.CultureInfo.CurrentCulture);
        var label = $"{Count} {Mood.ToString().ToLowerInvariant()} {(Count == 1 ? "day" : "days")}";
        System.Windows.Automation.AutomationProperties.SetName(this, label);
        ToolTip = label;
    }
}
