using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Journal.App.Controls;
using Journal.Core.Services;
using Journal.Core.Settings;
using Journal.Core.Tracking;

namespace Journal.App.ViewModels;

/// <summary>Turns a measure's values into graph points and text (ISS-030).</summary>
public static class TrackingText
{
    public static string WithUnit(TrackedValue value, Measure measure) =>
        measure.Unit.Length == 0 ? value.Format() : $"{value.Format()} {measure.Unit}";

    public static string PeriodName(MeasurePeriod period) => period == MeasurePeriod.Monthly ? "Monthly" : "Weekly";

    public static IReadOnlyList<ChartPoint> Points(IReadOnlyList<SeriesPoint> series, Measure measure) =>
        series.Select(p => new ChartPoint(p.Date,
            $"{TrackingService.PeriodLabel(p.PeriodStart, measure.Period)} ({p.Date.ToString("MMM d", CultureInfo.CurrentCulture)})",
            p.Value.First, p.Value.Second, WithUnit(p.Value, measure))).ToList();
}

/// <summary>A tracked value shown on the main screen, below MOODS (FR-TRK-08).</summary>
public sealed class MeasureCardViewModel(Measure measure, IReadOnlyList<SeriesPoint> recent)
{
    public Measure Measure { get; } = measure;
    public string Name => Measure.Name;
    public IReadOnlyList<ChartPoint> Points { get; } = TrackingText.Points(recent, measure);
    public bool HasValue => recent.Count > 0;
    public string LatestText => recent.Count == 0 ? "No value yet" : TrackingText.WithUnit(recent[^1].Value, Measure);
    public string WhenText => recent.Count == 0
        ? (Measure.Period == MeasurePeriod.Monthly ? "None in the last 12 months" : "None in the last 12 weeks")
        : recent[^1].Date.ToString("MMM d", CultureInfo.CurrentCulture);
    public string AutomationName => $"{Name}: {LatestText}" + (recent.Count == 0 ? "" : $", {recent[^1].Date.ToString("D", CultureInfo.CurrentCulture)}") +
                                    ". Opens the graphs.";
}

/// <summary>The tracked-value cards under MOODS: only measures ticked for the main screen, at most two (FR-TRK-08).</summary>
public sealed partial class TrackedValuesViewModel(JournalService journal, Func<AppSettings> settings) : ObservableObject
{
    /// <summary>How many weeks or months the small graphs cover.</summary>
    public const int RecentPeriods = 12;

    public ObservableCollection<MeasureCardViewModel> Cards { get; } = [];

    /// <summary>The feature is turned on in Settings (FR-TRK-01).</summary>
    [ObservableProperty] private bool _isEnabled;
    /// <summary>There is room for one line per value but not for the small graphs.</summary>
    [ObservableProperty] private bool _isCompact;
    /// <summary>
    /// There is room for the card, and no search is showing results. Otherwise the card steps aside and the values
    /// stay one click away under Graphs (FR-TRK-08, NFR-USE-02).
    /// </summary>
    [ObservableProperty] private bool _hasRoom = true;

    public bool HasCards => IsEnabled && Cards.Count > 0;
    public bool ShowCards => HasCards && HasRoom;

    /// <summary>Asks the main window to open the graphs, at a measure or at the top.</summary>
    public event EventHandler<Measure?>? GraphsRequested;

    public void Refresh()
    {
        var s = settings();
        IsEnabled = s.TrackValues;
        Cards.Clear();
        if (IsEnabled)
        {
            foreach (var m in journal.Tracking.Measures.Where(m => m.ShowOnMain).Take(Measure.MaxOnMain))
                Cards.Add(new MeasureCardViewModel(m, journal.Tracking.Recent(m, s.WeekStart, RecentPeriods)));
        }
        OnPropertyChanged(nameof(HasCards));
        OnPropertyChanged(nameof(ShowCards));
    }

    partial void OnIsEnabledChanged(bool value) { OnPropertyChanged(nameof(HasCards)); OnPropertyChanged(nameof(ShowCards)); }
    partial void OnHasRoomChanged(bool value) => OnPropertyChanged(nameof(ShowCards));

    /// <summary>Height of the card around its rows (padding, border and the gap above it).</summary>
    public const double CardChrome = 26;
    /// <summary>Height of one value with its small graph, and of one compact line.</summary>
    public const double FullRowHeight = 40;
    public const double CompactRowHeight = 22;
    /// <summary>
    /// Chooses how the card is shown from the space left in the column after the calendar, MOODS and the search box:
    /// with graphs, one line per value, or not at all. While a search is showing results the card steps aside, so the
    /// results keep the whole column (FR-TRK-08).
    /// </summary>
    public void FitTo(double spare, bool searching)
    {
        int rows = Math.Max(Cards.Count, 1);
        IsCompact = spare < CardChrome + rows * FullRowHeight;
        HasRoom = !searching && spare >= CardChrome + rows * CompactRowHeight;
    }

    [RelayCommand]
    private void OpenGraphs(MeasureCardViewModel? card) => GraphsRequested?.Invoke(this, card?.Measure);
}

/// <summary>One row of a graph's table of values, so the values can be read without the graph.</summary>
public sealed record ValueRowViewModel(DateOnly Date, string PeriodText, string DateText, string ValueText);

/// <summary>One measure in the Graphs window.</summary>
public sealed class MeasureGraphViewModel
{
    public MeasureGraphViewModel(Measure measure, IReadOnlyList<SeriesPoint> series)
    {
        Measure = measure;
        Points = TrackingText.Points(series, measure);
        Rows = series.Reverse().Select(p => new ValueRowViewModel(p.Date,
            TrackingService.PeriodLabel(p.PeriodStart, measure.Period),
            p.Date.ToString("ddd, MMM d, yyyy", CultureInfo.CurrentCulture),
            TrackingText.WithUnit(p.Value, measure))).ToList();
        LatestText = series.Count == 0 ? "No values in this range" : $"Latest: {TrackingText.WithUnit(series[^1].Value, measure)}";
    }

    public Measure Measure { get; }
    public string Name => Measure.Name;
    public string Heading => Measure.Name.ToUpper(CultureInfo.CurrentCulture);
    public string Details => TrackingText.PeriodName(Measure.Period) + (Measure.Unit.Length > 0 ? $" · {Measure.Unit}" : "") +
                             (Points.Any(p => p.Second is not null) ? " · two values (first line teal, second amber)" : "");
    public string LatestText { get; }
    public IReadOnlyList<ChartPoint> Points { get; }
    /// <summary>Newest first.</summary>
    public IReadOnlyList<ValueRowViewModel> Rows { get; }
    public string AutomationName => $"Graph of {Name}, {Points.Count} values. {LatestText}.";
}

public sealed record RangeOption(string Label, int? Months);

/// <summary>The Graphs window: every measure's graph over a chosen range (FR-TRK-09).</summary>
public sealed partial class GraphsViewModel : ObservableObject
{
    private readonly JournalService _journal;
    private readonly Func<AppSettings> _settings;
    private readonly Func<DateOnly, Task> _openDay;

    public IReadOnlyList<RangeOption> Ranges { get; } = [new("3 months", 3), new("1 year", 12), new("All", null)];

    [ObservableProperty] private RangeOption _range;

    public ObservableCollection<MeasureGraphViewModel> Graphs { get; } = [];

    /// <summary>The measure to scroll to when the window opens, if any.</summary>
    public Measure? Focus { get; set; }

    public bool HasMeasures => Graphs.Count > 0;

    public GraphsViewModel(JournalService journal, Func<AppSettings> settings, Func<DateOnly, Task> openDay, Measure? focus = null)
    {
        _journal = journal;
        _settings = settings;
        _openDay = openDay;
        _range = Ranges[1];
        Focus = focus;
        _journal.EntryChanged += OnEntryChanged;
        _journal.Tracking.MeasuresChanged += OnMeasuresChanged;
        Refresh();
    }

    private void OnEntryChanged(object? sender, EntryChangedEventArgs e) => Refresh();
    private void OnMeasuresChanged(object? sender, MeasuresChangedEventArgs e) => Refresh();

    /// <summary>Stops following the journal (the window has closed).</summary>
    public void Detach()
    {
        _journal.EntryChanged -= OnEntryChanged;
        _journal.Tracking.MeasuresChanged -= OnMeasuresChanged;
    }

    partial void OnRangeChanged(RangeOption value) => Refresh();

    public void Refresh()
    {
        var weekStart = _settings().WeekStart;
        DateOnly? from = Range.Months is { } months ? _journal.Tracking.Today.AddMonths(-months).AddDays(1) : null;
        Graphs.Clear();
        foreach (var measure in _journal.Tracking.Measures)
            Graphs.Add(new MeasureGraphViewModel(measure, _journal.Tracking.Series(measure, weekStart, from)));
        OnPropertyChanged(nameof(HasMeasures));
    }

    [RelayCommand]
    private async Task OpenDay(object? item)
    {
        DateOnly? date = item switch
        {
            ChartPoint p => p.Date,
            ValueRowViewModel r => r.Date,
            _ => null,
        };
        if (date is { } d) await _openDay(d);
    }
}
