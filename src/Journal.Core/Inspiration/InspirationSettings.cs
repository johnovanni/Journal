namespace Journal.Core.Inspiration;

public enum SelectionMode
{
    /// <summary>The same date gets the same item every year.</summary>
    DayOfYear,
    /// <summary>A random item per date, stable for that date and vault.</summary>
    RandomPerDay,
}

public static class SourceNames
{
    public const string BuiltIn = "builtin";
    public const string Imported = "imported";
}

public sealed record InspirationSettings
{
    public bool ShowVerse { get; init; } = true;
    public bool ShowQuote { get; init; } = true;
    public string VerseSource { get; init; } = SourceNames.BuiltIn;
    public IReadOnlySet<string> VerseBooks { get; init; } = new HashSet<string>();
    public string QuoteSource { get; init; } = SourceNames.BuiltIn;
    public IReadOnlySet<string> Traditions { get; init; } = new HashSet<string>();
    /// <summary>
    /// Quote categories the user turned off (FR-INSP-17). Stored as the ones turned off, so a category
    /// added to the content later is shown until the user turns it off.
    /// </summary>
    public IReadOnlySet<string> HiddenCategories { get; init; } = new HashSet<string>();
    public SelectionMode Mode { get; init; } = SelectionMode.DayOfYear;
}

/// <summary>A quote category and the traditions its quotes come from.</summary>
public sealed record QuoteCategory(string Name, IReadOnlyList<string> Traditions);

/// <summary>A ticked tradition saved with its category, e.g. "Philosophy/Chinese" (ISS-015).</summary>
public static class TraditionChoice
{
    public const char Separator = '/';

    public static string Make(string category, string tradition) => $"{category}{Separator}{tradition}";

    public static bool TryParse(string choice, out string category, out string tradition)
    {
        var i = choice.IndexOf(Separator);
        category = i > 0 ? choice[..i] : "";
        tradition = i > 0 ? choice[(i + 1)..] : choice;
        return i > 0 && tradition.Length > 0;
    }
}
