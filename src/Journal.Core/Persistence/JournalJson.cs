using System.Text.Json;
using System.Text.Json.Serialization;
using Journal.Core.Documents;
using Journal.Core.Domain;

namespace Journal.Core.Persistence;

public sealed class ArgbColorJsonConverter : JsonConverter<ArgbColor>
{
    public override ArgbColor Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        ArgbColor.Parse(reader.GetString() ?? "");

    public override void Write(Utf8JsonWriter writer, ArgbColor value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.ToHex());
}

/// <summary>Thrown when a persisted file was written by a newer version of the app.</summary>
public sealed class UnsupportedFormatVersionException(string what, int version)
    : Exception($"This {what} was made by a newer version of Journal (format {version}). Please update the app.")
{
    public int Version { get; } = version;
}

/// <summary>Shared JSON settings and the persisted shape of an entry.</summary>
public static class JournalJson
{
    public const int EntryFormatVersion = 1;

    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
        WriteIndented = false,
    };

    public static byte[] SerializeEntry(JournalEntry entry) =>
        JsonSerializer.SerializeToUtf8Bytes(EntryDto.From(entry), Options);

    public static JournalEntry DeserializeEntry(ReadOnlySpan<byte> json)
    {
        var dto = JsonSerializer.Deserialize<EntryDto>(json, Options) ?? throw new JsonException("Empty entry.");
        if (dto.FormatVersion > EntryFormatVersion) throw new UnsupportedFormatVersionException("entry", dto.FormatVersion);
        return dto.ToEntry();
    }

    public static string SerializeDocument(JournalDocument doc) => JsonSerializer.Serialize(doc, Options);

    public static JournalDocument DeserializeDocument(string json) =>
        JsonSerializer.Deserialize<JournalDocument>(json, Options) ?? new JournalDocument();

    internal sealed class EntryDto
    {
        public int FormatVersion { get; set; } = EntryFormatVersion;
        public DateOnly Date { get; set; }
        public DateTimeOffset CreatedUtc { get; set; }
        public DateTimeOffset ModifiedUtc { get; set; }
        public List<TabDto> Tabs { get; set; } = [];
        public List<string> ExtraTags { get; set; } = [];
        public Verse? Verse { get; set; }
        public Quote? Quote { get; set; }
        public bool InspirationPinned { get; set; }
        /// <summary>Absent when the day has no mood, so older files and files without a mood look the same.</summary>
        public Mood? Mood { get; set; }

        public static EntryDto From(JournalEntry e) => new()
        {
            Date = e.Date,
            CreatedUtc = e.CreatedUtc,
            ModifiedUtc = e.ModifiedUtc,
            Tabs = e.Tabs.Select(t => new TabDto { Id = t.Id, Tag = t.Tag?.Name, Document = t.Document }).ToList(),
            ExtraTags = e.ExtraTags.Select(t => t.Name).ToList(),
            Verse = e.Inspiration.Verse,
            Quote = e.Inspiration.Quote,
            InspirationPinned = e.IsInspirationPinned,
            Mood = e.Mood == Domain.Mood.None ? null : e.Mood,
        };

        public JournalEntry ToEntry() => JournalEntry.Restore(
            Date, CreatedUtc, ModifiedUtc,
            Tabs.Select(t => new EntryTab(t.Tag is null ? null : Tag.Parse(t.Tag), t.Document ?? new JournalDocument()) { Id = t.Id }),
            ExtraTags.Select(Tag.Parse),
            new DailyInspiration(Verse, Quote),
            InspirationPinned,
            Mood ?? Domain.Mood.None);
    }

    internal sealed class TabDto
    {
        public Guid Id { get; set; }
        public string? Tag { get; set; }
        public JournalDocument? Document { get; set; }
    }
}
