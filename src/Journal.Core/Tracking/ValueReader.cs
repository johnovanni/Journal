using System.Globalization;
using System.Text.RegularExpressions;
using Journal.Core.Persistence;

namespace Journal.Core.Tracking;

/// <summary>A value found in a day: the measure's key (see <see cref="Measure.KeyOf"/>) and the value.</summary>
public readonly record struct Reading(string Key, TrackedValue Value);

/// <summary>
/// Finds tracked values in a day's text (FR-TRK-04, FR-TRK-05): <c>##name=value</c> anywhere, and the first number
/// in a tab whose tag is a measure's name. Anything that isn't a valid value is ignored without a message.
/// </summary>
public static partial class ValueReader
{
    private const string Number = @"[-+]?\d+(?:[.,]\d+)?";

    // ##name=value: the name runs to the "=" (no # or line break inside), the value is the word after it.
    [GeneratedRegex(@"##(?<name>[^#=\r\n]{1,60}?)[ \t]*=[ \t]*(?<value>[^\s]*)")]
    private static partial Regex TaggedValue();

    [GeneratedRegex($@"^(?<a>{Number})(?:/(?<b>{Number}))?$")]
    private static partial Regex WholeValue();

    // A number (or pair) that isn't part of a longer word, such as "181.9" in "181.9 after a run".
    [GeneratedRegex($@"(?<![\w.,/]){Number}(?:/{Number})?(?![\w/]|[.,]\d)")]
    private static partial Regex AnyNumber();

    /// <summary>
    /// Parses a whole value: a number with a point or a comma as the decimal mark (no thousands separator), or two
    /// numbers joined by "/". A full stop or other punctuation that ends a sentence after the value is allowed.
    /// </summary>
    public static bool TryParseValue(string? text, out TrackedValue value)
    {
        value = default;
        if (string.IsNullOrWhiteSpace(text)) return false;
        var t = text.Trim().TrimEnd('.', ',', ';', ':', '!', '?', ')');
        var m = WholeValue().Match(t);
        if (!m.Success || !TryNumber(m.Groups["a"].Value, out var a)) return false;
        double? b = null;
        if (m.Groups["b"].Success)
        {
            if (!TryNumber(m.Groups["b"].Value, out var second)) return false;
            b = second;
        }
        value = new TrackedValue(a, b);
        return true;
    }

    private static bool TryNumber(string s, out double d) =>
        double.TryParse(s.Replace(',', '.'), NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out d)
        && double.IsFinite(d);

    /// <summary>The <c>##name=value</c> readings in a text, in the order they appear, with their positions.</summary>
    public static IEnumerable<(int Index, Reading Reading)> FromText(string text)
    {
        foreach (Match m in TaggedValue().Matches(text))
        {
            var name = m.Groups["name"].Value;
            if (Measure.NameProblem(name) is not null) continue;
            if (TryParseValue(m.Groups["value"].Value, out var v)) yield return (m.Index, new Reading(Measure.KeyOf(name), v));
        }
    }

    /// <summary>
    /// The first number in a text, or null (FR-TRK-04: a tab named after a measure). Numbers inside
    /// <c>##name=value</c> belong to that name, so they are skipped.
    /// </summary>
    public static (int Index, TrackedValue Value)? FirstNumber(string text)
    {
        var masked = TaggedValue().Replace(text, m => new string(' ', m.Length));
        foreach (Match m in AnyNumber().Matches(masked))
            if (TryParseValue(m.Value, out var v)) return (m.Index, v);
        return null;
    }

    /// <summary>
    /// Every reading in a day, in the order written: tab by tab, top to bottom. A tab with a tag also gives its first
    /// number under the tag's name; whether that name is a measure is decided later (FR-TRK-04).
    /// </summary>
    public static IReadOnlyList<Reading> Read(CatalogEntry entry)
    {
        var all = new List<Reading>();
        foreach (var tab in entry.Tabs)
        {
            var found = FromText(tab.BodyText).ToList();
            if (tab.Tag is { } tag && FirstNumber(tab.BodyText) is { } first)
                found.Add((first.Index, new Reading(Measure.KeyOf(tag), first.Value)));
            all.AddRange(found.OrderBy(f => f.Index).Select(f => f.Reading));
        }
        return all;
    }
}
