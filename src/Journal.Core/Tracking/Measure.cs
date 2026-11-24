using System.Globalization;
using System.Text.RegularExpressions;

namespace Journal.Core.Tracking;

/// <summary>How often a measure is tracked: one value per week or per month (ISS-030).</summary>
public enum MeasurePeriod { Weekly, Monthly }

/// <summary>
/// A value the user tracks, such as "Weight" or "Blood pressure" (FR-TRK-02). Measures are journal content, so the
/// list is saved encrypted in the vault, never in the settings file (FR-TRK-03).
/// </summary>
public sealed record Measure(string Name, string Unit = "", MeasurePeriod Period = MeasurePeriod.Weekly, bool ShowOnMain = false)
{
    /// <summary>At most this many measures are shown on the main screen (FR-TRK-08).</summary>
    public const int MaxOnMain = 2;
    public const int MaxNameLength = 40;
    public const int MaxUnitLength = 12;

    /// <summary>The name as it is matched: case and extra spaces ignored.</summary>
    public string Key => KeyOf(Name);

    public static string KeyOf(string name) => Regex.Replace(name.Trim(), @"\s+", " ").ToLowerInvariant();

    /// <summary>Why a name can't be used, or null when it can.</summary>
    public static string? NameProblem(string? name)
    {
        var trimmed = (name ?? "").Trim();
        if (trimmed.Length == 0) return "A tracked value needs a name.";
        if (trimmed.Length > MaxNameLength) return $"A name can be at most {MaxNameLength} characters.";
        if (trimmed.IndexOfAny(['#', '=', '\n', '\r']) >= 0) return "A name can't contain #, = or a line break.";
        return null;
    }
}

/// <summary>One reading: a number, or two numbers such as blood pressure "120/80" (FR-TRK-05).</summary>
public readonly record struct TrackedValue(double First, double? Second = null)
{
    public bool IsPair => Second is not null;

    public string Format(CultureInfo? culture = null)
    {
        culture ??= CultureInfo.CurrentCulture;
        var first = First.ToString("0.##", culture);
        return Second is { } s ? $"{first}/{s.ToString("0.##", culture)}" : first;
    }

    public override string ToString() => Format(CultureInfo.InvariantCulture);
}
