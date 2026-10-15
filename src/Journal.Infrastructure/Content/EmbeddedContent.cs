using System.Text.Json;
using Journal.Core.Domain;
using Journal.Core.Inspiration;
using Journal.Core.Persistence;

namespace Journal.Infrastructure.Content;

/// <summary>Built-in World English Bible verses (public domain), embedded in the assembly (D10).</summary>
public sealed class EmbeddedVerseSource : IVerseSource
{
    private readonly Lazy<IReadOnlyList<Verse>> _verses = new(() =>
        EmbeddedJson.Load<VerseFile>("Journal.Content.verses.json").Verses
            .Select(v => new Verse(v.Reference, v.Text, v.Translation) { Book = v.Book ?? Verse.BookOf(v.Reference) }).ToList());

    public string Name => SourceNames.BuiltIn;
    public string Description => "World English Bible (public domain)";
    public IReadOnlyList<Verse> All() => _verses.Value;

    private sealed record VerseFile(int FormatVersion, string Source, List<VerseRow> Verses);
    private sealed record VerseRow(string Reference, string? Book, string Text, string Translation);
}

/// <summary>Built-in quotes from public-domain editions of classical authors (D10).</summary>
public sealed class EmbeddedQuoteSource : IQuoteSource
{
    private readonly Lazy<IReadOnlyList<Quote>> _quotes = new(() =>
        EmbeddedJson.Load<QuoteFile>("Journal.Content.quotes.json").Quotes
            .Select(q => new Quote(q.Text, q.Author, q.Work, q.Tradition) { Source = q.Source }).ToList());

    public string Name => SourceNames.BuiltIn;
    public IReadOnlyList<Quote> All() => _quotes.Value;

    private sealed record QuoteFile(int FormatVersion, string Source, List<QuoteRow> Quotes);
    private sealed record QuoteRow(string Text, string Author, string? Work, string Tradition, string? Source);
}

internal static class EmbeddedJson
{
    public static T Load<T>(string resource)
    {
        using var stream = typeof(EmbeddedJson).Assembly.GetManifestResourceStream(resource)
                           ?? throw new InvalidOperationException($"Missing embedded resource {resource}.");
        return JsonSerializer.Deserialize<T>(stream, JournalJson.Options) ?? throw new InvalidOperationException($"Empty resource {resource}.");
    }
}
