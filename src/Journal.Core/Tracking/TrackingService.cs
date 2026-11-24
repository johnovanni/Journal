using System.Globalization;
using Journal.Core.Persistence;
using Journal.Core.Services;

namespace Journal.Core.Tracking;

/// <summary>The list of measures, stored encrypted inside the vault (FR-TRK-03).</summary>
public interface IMeasureStore
{
    Task<IReadOnlyList<Measure>> LoadMeasuresAsync(CancellationToken ct = default);
    Task SaveMeasuresAsync(IReadOnlyList<Measure> measures, CancellationToken ct = default);
}

/// <summary>Keeps measures in memory only (tests, and journals opened without a vault).</summary>
public sealed class MemoryMeasureStore : IMeasureStore
{
    public IReadOnlyList<Measure> Saved { get; private set; } = [];
    public int SaveCount { get; private set; }

    public Task<IReadOnlyList<Measure>> LoadMeasuresAsync(CancellationToken ct = default) => Task.FromResult(Saved);

    public Task SaveMeasuresAsync(IReadOnlyList<Measure> measures, CancellationToken ct = default)
    {
        Saved = [.. measures];
        SaveCount++;
        return Task.CompletedTask;
    }
}

/// <summary>One point of a measure's graph: the period, the day its value was written, and the value.</summary>
public sealed record SeriesPoint(DateOnly PeriodStart, DateOnly Date, TrackedValue Value);

/// <summary>Raised when the list of measures is saved.</summary>
public sealed class MeasuresChangedEventArgs(IReadOnlyList<Measure> measures) : EventArgs
{
    public IReadOnlyList<Measure> Measures { get; } = measures;
}

/// <summary>
/// Tracked values (ISS-030). Values are read from the encrypted catalog, which already holds each tab's text, so the
/// whole journal counts at once, including days written before a measure was set up, and no entry is opened
/// (FR-TRK-07). Readings are cached per catalog entry and read again only when that day changes.
/// </summary>
public sealed class TrackingService(IMeasureStore store, Func<Catalog> catalog, IClock clock)
{
    private readonly Dictionary<DateOnly, (CatalogEntry Source, IReadOnlyList<Reading> Readings)> _cache = [];
    private IReadOnlyList<Measure> _measures = [];

    public IReadOnlyList<Measure> Measures => _measures;

    /// <summary>Today, by the journal's clock.</summary>
    public DateOnly Today => clock.Today;

    public event EventHandler<MeasuresChangedEventArgs>? MeasuresChanged;

    /// <summary>Loads the measures. A damaged list is treated as empty, and the journal still opens.</summary>
    public async Task LoadAsync(CancellationToken ct = default)
    {
        try
        {
            _measures = await store.LoadMeasuresAsync(ct);
        }
        catch (UnreadableVaultFileException)
        {
            _measures = [];
        }
    }

    /// <summary>Why the list can't be saved, or null: names must be valid and different, and at most two shown.</summary>
    public static string? Problem(IReadOnlyList<Measure> measures)
    {
        foreach (var m in measures)
            if (Measure.NameProblem(m.Name) is { } p) return p;
        var duplicate = measures.GroupBy(m => m.Key).FirstOrDefault(g => g.Count() > 1);
        if (duplicate is not null) return $"There are two tracked values called \"{duplicate.First().Name.Trim()}\".";
        if (measures.Count(m => m.ShowOnMain) > Measure.MaxOnMain)
            return $"At most {Measure.MaxOnMain} tracked values can be shown on the main screen.";
        if (measures.Any(m => m.Unit.Trim().Length > Measure.MaxUnitLength))
            return $"A unit can be at most {Measure.MaxUnitLength} characters.";
        return null;
    }

    public async Task SaveMeasuresAsync(IReadOnlyList<Measure> measures, CancellationToken ct = default)
    {
        if (Problem(measures) is { } problem) throw new ArgumentException(problem, nameof(measures));
        var cleaned = measures.Select(m => m with { Name = m.Name.Trim(), Unit = m.Unit.Trim() }).ToList();
        await store.SaveMeasuresAsync(cleaned, ct);
        _measures = cleaned;
        MeasuresChanged?.Invoke(this, new MeasuresChangedEventArgs(cleaned));
    }

    /// <summary>The Sunday or Monday that starts the week holding <paramref name="date"/>, or the first of its month.</summary>
    public static DateOnly PeriodStart(DateOnly date, MeasurePeriod period, DayOfWeek weekStart) => period switch
    {
        MeasurePeriod.Monthly => new DateOnly(date.Year, date.Month, 1),
        _ => date.AddDays(-(((int)date.DayOfWeek - (int)weekStart + 7) % 7)),
    };

    /// <summary>"Week of Nov 17" or "November 2026".</summary>
    public static string PeriodLabel(DateOnly periodStart, MeasurePeriod period, CultureInfo? culture = null)
    {
        culture ??= CultureInfo.CurrentCulture;
        return period == MeasurePeriod.Monthly
            ? periodStart.ToString("MMMM yyyy", culture)
            : "Week of " + periodStart.ToString("MMM d, yyyy", culture);
    }

    /// <summary>
    /// The measure's values, one per week or month, oldest first (FR-TRK-06). Values on days after today are ignored
    /// (FR-TRK-05). Within a period the latest day counts, and within a day the last value written.
    /// </summary>
    public IReadOnlyList<SeriesPoint> Series(Measure measure, DayOfWeek weekStart, DateOnly? from = null)
    {
        var today = clock.Today;
        var key = measure.Key;
        var byPeriod = new SortedDictionary<DateOnly, SeriesPoint>();
        foreach (var entry in catalog().Entries)
        {
            if (entry.Date > today) continue;
            TrackedValue? last = null;
            foreach (var r in ReadingsOf(entry))
                if (r.Key == key) last = r.Value;
            if (last is not { } value) continue;
            var start = PeriodStart(entry.Date, measure.Period, weekStart);
            if (from is { } f && start < PeriodStart(f, measure.Period, weekStart)) continue;
            if (!byPeriod.TryGetValue(start, out var existing) || existing.Date < entry.Date)
                byPeriod[start] = new SeriesPoint(start, entry.Date, value);
        }
        return [.. byPeriod.Values];
    }

    /// <summary>The last <paramref name="count"/> periods' values, ending with the current week or month.</summary>
    public IReadOnlyList<SeriesPoint> Recent(Measure measure, DayOfWeek weekStart, int count)
    {
        var current = PeriodStart(clock.Today, measure.Period, weekStart);
        var from = measure.Period == MeasurePeriod.Monthly ? current.AddMonths(-(count - 1)) : current.AddDays(-7 * (count - 1));
        return Series(measure, weekStart, from);
    }

    private IReadOnlyList<Reading> ReadingsOf(CatalogEntry entry)
    {
        if (_cache.TryGetValue(entry.Date, out var cached) && ReferenceEquals(cached.Source, entry)) return cached.Readings;
        var readings = ValueReader.Read(entry);
        _cache[entry.Date] = (entry, readings);
        return readings;
    }
}
