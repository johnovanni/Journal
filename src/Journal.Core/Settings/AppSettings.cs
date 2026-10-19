using Journal.Core.Inspiration;

namespace Journal.Core.Settings;

/// <summary>User preferences stored outside the vault. Contains no journal content (NFR-SEC-07).</summary>
public sealed class AppSettings
{
    public const int CurrentFormatVersion = 1;
    public const int MinAutoLockMinutes = 1;
    public const int MaxAutoLockMinutes = 120;

    public int FormatVersion { get; set; } = CurrentFormatVersion;
    public string? VaultFolder { get; set; }
    /// <summary>Minutes of inactivity before locking; 0 means never.</summary>
    public int AutoLockMinutes { get; set; } = 10;
    public DayOfWeek WeekStart { get; set; } = DayOfWeek.Sunday;
    public bool AttachmentNoticeShown { get; set; }
    public InspirationOptions Inspiration { get; set; } = new();

    public TimeSpan? AutoLockTimeout =>
        AutoLockMinutes <= 0 ? null : TimeSpan.FromMinutes(Math.Clamp(AutoLockMinutes, MinAutoLockMinutes, MaxAutoLockMinutes));
}

/// <summary>Serializable form of <see cref="InspirationSettings"/>.</summary>
public sealed class InspirationOptions
{
    public bool ShowVerse { get; set; } = true;
    public bool ShowQuote { get; set; } = true;
    public string VerseSource { get; set; } = SourceNames.BuiltIn;
    public List<string> VerseBooks { get; set; } = [];
    public string QuoteSource { get; set; } = SourceNames.BuiltIn;
    public List<string> Traditions { get; set; } = [];
    /// <summary>Quote categories turned off; empty means all are shown (FR-INSP-17).</summary>
    public List<string> HiddenCategories { get; set; } = [];
    public SelectionMode Mode { get; set; } = SelectionMode.DayOfYear;

    public InspirationSettings ToSettings() => new()
    {
        ShowVerse = ShowVerse,
        ShowQuote = ShowQuote,
        VerseSource = VerseSource,
        VerseBooks = VerseBooks.ToHashSet(StringComparer.OrdinalIgnoreCase),
        QuoteSource = QuoteSource,
        Traditions = Traditions.ToHashSet(StringComparer.OrdinalIgnoreCase),
        HiddenCategories = HiddenCategories.ToHashSet(StringComparer.OrdinalIgnoreCase),
        Mode = Mode,
    };
}

public interface ISettingsStore
{
    AppSettings Load();
    void Save(AppSettings settings);
}
