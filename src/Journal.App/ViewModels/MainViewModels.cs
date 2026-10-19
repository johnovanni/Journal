using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Journal.Core.Calendar;
using Journal.Core.Domain;
using Journal.Core.Search;
using Journal.Core.Services;

namespace Journal.App.ViewModels;

public sealed partial class DayCellViewModel(DateOnly date) : ObservableObject
{
    public DateOnly Date { get; } = date;
    public int DayNumber => Date.Day;
    public string AutomationName => Date.ToString("D", CultureInfo.CurrentCulture) + (HasEntry ? ", has an entry" : "") +
                                    (Mood == Mood.None ? "" : $", {Mood.ToString().ToLowerInvariant()} day");

    [ObservableProperty] private bool _isInCurrentMonth;
    [ObservableProperty] private bool _isToday;
    [ObservableProperty] private bool _hasEntry;
    [ObservableProperty] private bool _isFuture;
    [ObservableProperty] private bool _hasOpenItems;
    [ObservableProperty] private bool _isSelected;
    /// <summary>The day's mood: tints the day red or green and shows a small face (FR-MOOD-03).</summary>
    [ObservableProperty] private Mood _mood;

    public bool IsPlanned => IsFuture && HasEntry;

    partial void OnMoodChanged(Mood value) => OnPropertyChanged(nameof(AutomationName));

    partial void OnHasEntryChanged(bool value) { OnPropertyChanged(nameof(IsPlanned)); OnPropertyChanged(nameof(AutomationName)); }
    partial void OnIsFutureChanged(bool value) => OnPropertyChanged(nameof(IsPlanned));
}

/// <summary>Month calendar with entry highlighting (FR-CAL-*).</summary>
public sealed partial class CalendarViewModel : ObservableObject
{
    private readonly IClock _clock;
    private readonly Func<DateOnly, DateOnly, (ISet<DateOnly> Entries, ISet<DateOnly> Open)> _dates;
    private readonly Func<DateOnly, DateOnly, IReadOnlyDictionary<DateOnly, Mood>> _moods;

    [ObservableProperty] private int _year;
    [ObservableProperty] private int _month;
    [ObservableProperty] private string _title = "";
    [ObservableProperty] private DateOnly _selectedDate;
    [ObservableProperty] private DayOfWeek _weekStart;

    public ObservableCollection<DayCellViewModel> Days { get; } = [];
    public ObservableCollection<string> WeekdayNames { get; } = [];

    /// <summary>Raised when the user opens a day (click or Enter).</summary>
    public event EventHandler<DateOnly>? DateSelected;

    public CalendarViewModel(IClock clock, Func<DateOnly, DateOnly, (ISet<DateOnly>, ISet<DateOnly>)> dates, DayOfWeek weekStart,
        Func<DateOnly, DateOnly, IReadOnlyDictionary<DateOnly, Mood>>? moods = null)
    {
        _clock = clock;
        _dates = dates;
        _moods = moods ?? ((_, _) => new Dictionary<DateOnly, Mood>());
        _weekStart = weekStart;
        var today = clock.Today;
        _selectedDate = today;
        ShowMonth(today.Year, today.Month);
    }

    public void ShowMonth(int year, int month)
    {
        Year = year;
        Month = month;
        Title = new DateTime(year, month, 1).ToString("MMMM yyyy", CultureInfo.CurrentCulture);
        Refresh();
    }

    partial void OnWeekStartChanged(DayOfWeek value) => Refresh();

    partial void OnSelectedDateChanged(DateOnly value)
    {
        foreach (var d in Days) d.IsSelected = d.Date == value;
    }

    /// <summary>Rebuilds the grid and highlights (call after entries change).</summary>
    public void Refresh()
    {
        if (Year == 0) return;
        var grid = MonthGrid.Build(Year, Month, WeekStart);
        var (entries, open) = _dates(grid[0], grid[^1]);
        var moods = _moods(grid[0], grid[^1]);
        var today = _clock.Today;
        WeekdayNames.Clear();
        foreach (var dow in MonthGrid.WeekdayOrder(WeekStart))
            WeekdayNames.Add(CultureInfo.CurrentCulture.DateTimeFormat.AbbreviatedDayNames[(int)dow][..2]);
        Days.Clear();
        foreach (var date in grid)
        {
            Days.Add(new DayCellViewModel(date)
            {
                IsInCurrentMonth = date.Month == Month,
                IsToday = date == today,
                HasEntry = entries.Contains(date),
                IsFuture = date > today,
                HasOpenItems = open.Contains(date),
                IsSelected = date == SelectedDate,
                Mood = moods.GetValueOrDefault(date),
            });
        }
    }

    [RelayCommand]
    private void PreviousMonth()
    {
        var d = new DateOnly(Year, Month, 1).AddMonths(-1);
        ShowMonth(d.Year, d.Month);
    }

    [RelayCommand]
    private void NextMonth()
    {
        var d = new DateOnly(Year, Month, 1).AddMonths(1);
        ShowMonth(d.Year, d.Month);
    }

    [RelayCommand]
    private void Today()
    {
        var today = _clock.Today;
        ShowMonth(today.Year, today.Month);
        Open(today);
    }

    [RelayCommand]
    private void SelectDay(DayCellViewModel? cell)
    {
        if (cell is not null) Open(cell.Date);
    }

    /// <summary>Moves the highlighted day with the arrow keys without opening it.</summary>
    [RelayCommand]
    private void MoveSelection(int days)
    {
        var target = SelectedDate.AddDays(days);
        if (target.Year != Year || target.Month != Month) ShowMonth(target.Year, target.Month);
        SelectedDate = target;
    }

    [RelayCommand]
    private void OpenSelected() => Open(SelectedDate);

    /// <summary>Selects and opens <paramref name="date"/>, switching month when needed.</summary>
    public void Open(DateOnly date)
    {
        if (date.Year != Year || date.Month != Month) ShowMonth(date.Year, date.Month);
        SelectedDate = date;
        DateSelected?.Invoke(this, date);
    }

    /// <summary>Marks <paramref name="date"/> as selected without raising <see cref="DateSelected"/>.</summary>
    public void ShowSelected(DateOnly date)
    {
        if (date.Year != Year || date.Month != Month) ShowMonth(date.Year, date.Month);
        SelectedDate = date;
    }
}

public sealed record SearchHitViewModel(SearchHit Hit)
{
    public string DateText => Hit.Date.ToString("MMM d, yyyy", CultureInfo.CurrentCulture);
    public string TabTitle => Hit.TabTitle;
    public string Snippet => Hit.Snippet;
    public IReadOnlyList<HighlightRange> Highlights => Hit.Highlights;
    public string SourceText => Hit.Source switch
    {
        MatchSource.ImageCaption => "Caption",
        MatchSource.Attachment => "File",
        MatchSource.Tag => "Tag",
        _ => "Text",
    };
    /// <summary>Segoe Fluent / MDL2 glyph for the match source.</summary>
    public string SourceGlyph => Hit.Source switch
    {
        MatchSource.ImageCaption => "",
        MatchSource.Attachment => "",
        MatchSource.Tag => "",
        _ => "",
    };
}

/// <summary>Search box with results as you type (FR-SRCH-*).</summary>
public sealed partial class SearchViewModel : ObservableObject
{
    private readonly Func<SearchService> _search;
    private readonly Func<IReadOnlyList<string>> _tags;
    private readonly Debouncer _debouncer;

    [ObservableProperty] private string _queryText = "";
    [ObservableProperty] private string _warning = "";
    [ObservableProperty] private string _summary = "";

    public ObservableCollection<SearchHitViewModel> Results { get; } = [];
    public ObservableCollection<string> TagSuggestions { get; } = [];

    public event EventHandler<SearchHit>? ResultOpened;

    public int SearchCount { get; private set; }

    public SearchViewModel(Func<SearchService> search, Func<IReadOnlyList<string>> tags, IScheduler scheduler)
    {
        _search = search;
        _tags = tags;
        _debouncer = new Debouncer(scheduler, JournalContext.SearchDelay, Run);
    }

    partial void OnQueryTextChanged(string value)
    {
        UpdateTagSuggestions();
        _debouncer.Trigger();
    }

    public void Run()
    {
        _debouncer.Cancel();
        SearchCount++;
        Results.Clear();
        if (string.IsNullOrWhiteSpace(QueryText))
        {
            Warning = "";
            Summary = "";
            return;
        }
        var result = _search().Search(QueryText);
        foreach (var hit in result.Hits) Results.Add(new SearchHitViewModel(hit));
        Warning = string.Join(" ", result.Warnings);
        Summary = result.Hits.Count switch
        {
            0 => "No results",
            1 => "1 result",
            var n when result.Truncated => $"Newest {n} results",
            var n => $"{n} results",
        };
    }

    /// <summary>Re-runs the current query after entries change.</summary>
    public void RefreshIfActive()
    {
        if (!string.IsNullOrWhiteSpace(QueryText)) Run();
    }

    public void SearchForTag(string tag)
    {
        QueryText = tag.Contains(' ') ? $"tag:\"{tag}\"" : $"tag:{tag}";
        Run();
    }

    private void UpdateTagSuggestions()
    {
        TagSuggestions.Clear();
        var last = QueryText.Split(' ').LastOrDefault() ?? "";
        if (!last.StartsWith("tag:", StringComparison.OrdinalIgnoreCase)) return;
        var prefix = last[4..];
        foreach (var t in _tags().Where(t => t.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) &&
                                             !t.Equals(prefix, StringComparison.OrdinalIgnoreCase)).Take(8))
            TagSuggestions.Add(t);
    }

    [RelayCommand]
    private void ApplySuggestion(string? tag)
    {
        if (tag is null) return;
        var parts = QueryText.Split(' ').ToList();
        parts[^1] = "tag:" + tag;
        QueryText = string.Join(' ', parts) + " ";
        Run();
    }

    [RelayCommand]
    private void OpenResult(SearchHitViewModel? hit)
    {
        if (hit is not null) ResultOpened?.Invoke(this, hit.Hit);
    }

    [RelayCommand]
    private void Clear()
    {
        QueryText = "";
        Run();
    }
}

/// <summary>Counts of sad, neutral and happy days, shown under the calendar (FR-MOOD-04).</summary>
public sealed partial class MoodSummaryViewModel : ObservableObject
{
    [ObservableProperty] private MoodCounts _last30Days = new(0, 0, 0);
    [ObservableProperty] private MoodCounts _last12Months = new(0, 0, 0);

    public void Update(MoodSummary summary)
    {
        Last30Days = summary.Last30Days;
        Last12Months = summary.Last12Months;
    }
}

/// <summary>The main window: calendar, search and the open day.</summary>
public sealed partial class MainViewModel : ObservableObject
{
    private readonly JournalContext _ctx;

    public CalendarViewModel Calendar { get; }
    public SearchViewModel Search { get; }
    public MoodSummaryViewModel Moods { get; } = new();

    [ObservableProperty] private EntryViewModel? _currentEntry;
    [ObservableProperty] private string _statusText = "";
    [ObservableProperty] private string _lockText = "";

    public event EventHandler? LockRequested;
    public event EventHandler? SettingsRequested;

    public MainViewModel(JournalContext ctx)
    {
        _ctx = ctx;
        Calendar = new CalendarViewModel(ctx.Clock,
            (from, to) => (ctx.Journal.EntryDates(from, to), ctx.Journal.OpenItemDates(from, to)),
            ctx.Settings().WeekStart,
            ctx.Journal.Moods);
        Search = new SearchViewModel(() => ctx.Journal.Search, ctx.Journal.AllTags, ctx.Scheduler);
        Calendar.DateSelected += async (_, d) => await SelectDateAsync(d);
        Search.ResultOpened += async (_, hit) => await SelectDateAsync(hit.Date, hit.TabId);
        ctx.Journal.EntryChanged += (_, _) =>
        {
            Calendar.Refresh();
            Search.RefreshIfActive();
            Moods.Update(ctx.Journal.GetMoodSummary());
        };
        Moods.Update(ctx.Journal.GetMoodSummary());
    }

    public Task InitializeAsync() => SelectDateAsync(_ctx.Clock.Today);

    public async Task SelectDateAsync(DateOnly date, Guid? tabId = null)
    {
        if (CurrentEntry is { } current)
        {
            if (current.Date == date)
            {
                SelectTab(current, tabId);
                return;
            }
            await current.FlushAsync();
            current.TagSearchRequested -= OnTagSearch;
            current.PropertyChanged -= OnEntryPropertyChanged;
        }
        var load = await _ctx.Journal.OpenDayAsync(date);
        var vm = new EntryViewModel(_ctx, date, load);
        vm.TagSearchRequested += OnTagSearch;
        vm.PropertyChanged += OnEntryPropertyChanged;
        SelectTab(vm, tabId);
        CurrentEntry = vm;
        Calendar.ShowSelected(date);
        StatusText = vm.LoadError ?? "";
    }

    private static void SelectTab(EntryViewModel vm, Guid? tabId)
    {
        if (tabId is { } id && vm.Tabs.FirstOrDefault(t => t.Model.Id == id) is { } tab) vm.SelectedTab = tab;
    }

    private void OnTagSearch(object? sender, string tag) => Search.SearchForTag(tag);

    private void OnEntryPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(EntryViewModel.SaveStatus) && sender is EntryViewModel vm) StatusText = vm.SaveStatus;
    }

    /// <summary>Saves the open day; called before locking and closing.</summary>
    public Task FlushAsync() => CurrentEntry?.FlushAsync() ?? Task.CompletedTask;

    [RelayCommand]
    private void Lock() => LockRequested?.Invoke(this, EventArgs.Empty);

    [RelayCommand]
    private void OpenSettings() => SettingsRequested?.Invoke(this, EventArgs.Empty);

    [RelayCommand]
    private async Task DeleteEntry()
    {
        if (CurrentEntry is not { } entry || !_ctx.Journal.HasEntry(entry.Date)) return;
        if (!_ctx.Dialogs.Confirm("Delete entry",
                $"Delete the entry for {entry.Heading}, including its images and files? This can't be undone.", "Delete entry"))
            return;
        entry.CancelPendingSave();
        foreach (var t in entry.Tabs) t.Host = null;
        await _ctx.Journal.DeleteEntryAsync(entry.Date);
        entry.TagSearchRequested -= OnTagSearch;
        entry.PropertyChanged -= OnEntryPropertyChanged;
        CurrentEntry = null;
        await SelectDateAsync(entry.Date);
        StatusText = "Entry deleted";
    }

    public bool CanDeleteCurrent => CurrentEntry is { } e && _ctx.Journal.HasEntry(e.Date);
}
