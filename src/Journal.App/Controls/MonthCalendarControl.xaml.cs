using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using Journal.Core.Search;

namespace Journal.App.Controls;

public partial class MonthCalendarControl : UserControl
{
    public MonthCalendarControl()
    {
        InitializeComponent();
        // Clicking anywhere on the calendar gives it keyboard focus for arrow-key navigation.
        PreviewMouseDown += (_, _) => Focus();
    }
}

/// <summary>Boxed command parameters for calendar arrow keys.</summary>
public static class CalendarKeys
{
    public static readonly object Back1 = -1;
    public static readonly object Forward1 = 1;
    public static readonly object Back7 = -7;
    public static readonly object Forward7 = 7;
}

/// <summary>Attached property that renders a search snippet with its matches highlighted.</summary>
public static class Highlighting
{
    public static readonly DependencyProperty HitProperty = DependencyProperty.RegisterAttached(
        "Hit", typeof(ViewModels.SearchHitViewModel), typeof(Highlighting), new PropertyMetadata(null, OnHitChanged));

    public static ViewModels.SearchHitViewModel? GetHit(DependencyObject d) => (ViewModels.SearchHitViewModel?)d.GetValue(HitProperty);
    public static void SetHit(DependencyObject d, ViewModels.SearchHitViewModel? value) => d.SetValue(HitProperty, value);

    private static void OnHitChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not TextBlock tb) return;
        tb.Inlines.Clear();
        if (e.NewValue is not ViewModels.SearchHitViewModel hit) return;
        foreach (var inline in Build(hit.Snippet, hit.Highlights)) tb.Inlines.Add(inline);
    }

    public static IEnumerable<Inline> Build(string text, IReadOnlyList<HighlightRange> ranges)
    {
        int pos = 0;
        foreach (var r in ranges.OrderBy(r => r.Start))
        {
            if (r.Start < pos || r.Start + r.Length > text.Length) continue;
            if (r.Start > pos) yield return new Run(text[pos..r.Start]);
            yield return new Run(text.Substring(r.Start, r.Length))
            {
                FontWeight = FontWeights.SemiBold,
                Background = (System.Windows.Media.Brush)Application.Current?.TryFindResource("Highlight")! ?? System.Windows.Media.Brushes.LightYellow,
            };
            pos = r.Start + r.Length;
        }
        if (pos < text.Length) yield return new Run(text[pos..]);
    }
}
