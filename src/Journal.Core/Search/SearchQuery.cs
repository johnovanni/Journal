using System.Globalization;
using System.Text;
using Journal.Core.Services;

namespace Journal.Core.Search;

[Flags]
public enum SearchScope
{
    Text = 1,
    Captions = 2,
    Files = 4,
    Tags = 8,
    All = Text | Captions | Files | Tags,
}

public enum ChecklistFilter { Any, Open, Done }

public enum MatchSource { Text, ImageCaption, Attachment, Tag }

public sealed record SearchQuery
{
    public IReadOnlyList<string> Terms { get; init; } = [];
    public IReadOnlyList<string> Phrases { get; init; } = [];
    public IReadOnlyList<string> Tags { get; init; } = [];
    public DateOnly? From { get; init; }
    public DateOnly? To { get; init; }
    public SearchScope Scope { get; init; } = SearchScope.All;
    public ChecklistFilter Checklist { get; init; } = ChecklistFilter.Any;
    public IReadOnlyList<string> Warnings { get; init; } = [];

    public bool HasTextCriteria => Terms.Count > 0 || Phrases.Count > 0;
    public bool IsEmpty => !HasTextCriteria && Tags.Count == 0 && From is null && To is null && Checklist == ChecklistFilter.Any;
}

/// <summary>
/// Case- and accent-insensitive folding that keeps string length, so match positions in the
/// folded text are positions in the original text.
/// </summary>
public static class TextFolding
{
    public static string Fold(string text)
    {
        var chars = new char[text.Length];
        for (int i = 0; i < text.Length; i++) chars[i] = FoldChar(text[i]);
        return new string(chars);
    }

    public static char FoldChar(char c)
    {
        if (c < 128) return char.ToLowerInvariant(c);
        var decomposed = c.ToString().Normalize(NormalizationForm.FormD);
        var b = decomposed[0];
        if (CharUnicodeInfo.GetUnicodeCategory(b) == UnicodeCategory.NonSpacingMark) b = c;
        return b switch
        {
            '’' or '‘' => '\'',
            '“' or '”' => '"',
            _ => char.ToLowerInvariant(b),
        };
    }

    public static bool IsWordChar(char c) => char.IsLetterOrDigit(c) || c == '\'';

    /// <summary>Splits folded text into words.</summary>
    public static IEnumerable<string> Words(string folded)
    {
        int start = -1;
        for (int i = 0; i <= folded.Length; i++)
        {
            bool w = i < folded.Length && IsWordChar(folded[i]);
            if (w && start < 0) start = i;
            else if (!w && start >= 0)
            {
                yield return folded[start..i].Trim('\'');
                start = -1;
            }
        }
    }

    /// <summary>Folded words joined by single spaces, for phrase matching.</summary>
    public static string WordString(string text) => " " + string.Join(' ', Words(Fold(text))) + " ";
}

/// <summary>Parses the search box syntax (Design §8.1).</summary>
public sealed class SearchQueryParser(IClock clock)
{
    public SearchQuery Parse(string? text)
    {
        var terms = new List<string>();
        var phrases = new List<string>();
        var tags = new List<string>();
        var warnings = new List<string>();
        DateOnly? from = null, to = null;
        var scope = SearchScope.All;
        var checklist = ChecklistFilter.Any;

        foreach (var (token, quoted) in Tokenize(text ?? ""))
        {
            if (quoted)
            {
                var words = TextFolding.Words(TextFolding.Fold(token)).ToList();
                if (words.Count > 0) phrases.Add(string.Join(' ', words));
                continue;
            }
            var colon = token.IndexOf(':');
            var key = colon > 0 ? token[..colon].ToLowerInvariant() : "";
            var value = colon > 0 ? token[(colon + 1)..] : "";
            switch (key)
            {
                case "tag" when value.Length > 0:
                    tags.Add(value.Trim());
                    break;
                case "in" when value.Length > 0:
                    var s = value.ToLowerInvariant() switch
                    {
                        "text" => SearchScope.Text,
                        "captions" or "caption" => SearchScope.Captions,
                        "files" or "file" => SearchScope.Files,
                        "tags" => SearchScope.Tags,
                        _ => (SearchScope?)null,
                    };
                    if (s is null) warnings.Add($"Unknown search area \"{value}\". Use in:text, in:captions or in:files.");
                    else scope = scope == SearchScope.All ? s.Value : scope | s.Value;
                    break;
                case "from" or "to" when value.Length > 0:
                    if (TryParseDate(value, out var d))
                    {
                        if (key == "from") from = d; else to = d;
                    }
                    else warnings.Add($"\"{value}\" isn't a date. Use yyyy-mm-dd or today.");
                    break;
                case "is" when value.Equals("open", StringComparison.OrdinalIgnoreCase):
                    checklist = ChecklistFilter.Open;
                    break;
                case "is" when value.Equals("done", StringComparison.OrdinalIgnoreCase):
                    checklist = ChecklistFilter.Done;
                    break;
                default:
                    // Unknown operators are plain words (FR-SRCH-12).
                    terms.AddRange(TextFolding.Words(TextFolding.Fold(token)));
                    break;
            }
        }

        return new SearchQuery
        {
            Terms = terms, Phrases = phrases, Tags = tags, From = from, To = to,
            Scope = scope, Checklist = checklist, Warnings = warnings,
        };
    }

    private bool TryParseDate(string value, out DateOnly date)
    {
        if (value.Equals("today", StringComparison.OrdinalIgnoreCase))
        {
            date = clock.Today;
            return true;
        }
        return DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date);
    }

    private static IEnumerable<(string Token, bool Quoted)> Tokenize(string text)
    {
        int i = 0;
        while (i < text.Length)
        {
            if (char.IsWhiteSpace(text[i])) { i++; continue; }
            if (text[i] is '"' or '“' or '”')
            {
                int end = text.IndexOfAny(['"', '“', '”'], i + 1);
                if (end < 0) end = text.Length;
                yield return (text[(i + 1)..end], true);
                i = end + 1;
                continue;
            }
            int start = i;
            while (i < text.Length && !char.IsWhiteSpace(text[i]) && text[i] is not ('"' or '“' or '”')) i++;
            yield return (text[start..i], false);
        }
    }
}
