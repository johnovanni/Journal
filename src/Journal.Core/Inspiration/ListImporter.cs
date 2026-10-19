using System.Text;
using System.Text.Json;
using Journal.Core.Domain;

namespace Journal.Core.Inspiration;

public sealed record ImportResult<T>(IReadOnlyList<T> Items, int Skipped)
{
    public string Summary => $"Imported {Items.Count}, skipped {Skipped}.";
}

public sealed class ImportException(string message) : Exception(message);

/// <summary>
/// Reads verse lists (reference,text,translation) and quote lists (text,author,work,tradition,category)
/// from CSV or JSON (FR-INSP-11, FR-INSP-12).
/// </summary>
public static class ListImporter
{
    public static ImportResult<Verse> ImportVerses(string content, string fileName)
    {
        var rows = Rows(content, fileName, ["reference", "text", "translation"]);
        var items = new List<Verse>();
        int skipped = 0;
        foreach (var r in rows)
        {
            var reference = r.GetValueOrDefault("reference")?.Trim();
            var text = r.GetValueOrDefault("text")?.Trim();
            if (string.IsNullOrEmpty(reference) || string.IsNullOrEmpty(text)) { skipped++; continue; }
            var translation = r.GetValueOrDefault("translation")?.Trim();
            items.Add(new Verse(reference, text, string.IsNullOrEmpty(translation) ? "" : translation));
        }
        if (items.Count == 0) throw new ImportException("The file has no valid verses. Each row needs a reference and text.");
        return new ImportResult<Verse>(items, skipped);
    }

    public static ImportResult<Quote> ImportQuotes(string content, string fileName)
    {
        var rows = Rows(content, fileName, ["text", "author", "work", "tradition", "category"]);
        var items = new List<Quote>();
        int skipped = 0;
        foreach (var r in rows)
        {
            var text = r.GetValueOrDefault("text")?.Trim();
            var author = r.GetValueOrDefault("author")?.Trim();
            if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(author)) { skipped++; continue; }
            var work = r.GetValueOrDefault("work")?.Trim();
            var tradition = r.GetValueOrDefault("tradition")?.Trim();
            var category = r.GetValueOrDefault("category")?.Trim();
            items.Add(new Quote(text, author, string.IsNullOrEmpty(work) ? null : work,
                string.IsNullOrEmpty(tradition) ? "Imported" : tradition)
            {
                Category = string.IsNullOrEmpty(category) ? QuoteCategories.Imported : category,
            });
        }
        if (items.Count == 0) throw new ImportException("The file has no valid quotes. Each row needs text and an author.");
        return new ImportResult<Quote>(items, skipped);
    }

    private static List<Dictionary<string, string>> Rows(string content, string fileName, string[] defaultColumns)
    {
        content = content.TrimStart('﻿');
        if (fileName.EndsWith(".json", StringComparison.OrdinalIgnoreCase) || content.TrimStart().StartsWith('[') || content.TrimStart().StartsWith('{'))
            return JsonRows(content);
        return CsvRows(content, defaultColumns);
    }

    private static List<Dictionary<string, string>> JsonRows(string content)
    {
        try
        {
            using var doc = JsonDocument.Parse(content);
            var root = doc.RootElement;
            if (root.ValueKind == JsonValueKind.Object)
            {
                // Accept {"verses":[...]} / {"quotes":[...]} as written by the app's own content files.
                root = root.EnumerateObject().FirstOrDefault(p => p.Value.ValueKind == JsonValueKind.Array).Value;
            }
            if (root.ValueKind != JsonValueKind.Array) throw new ImportException("The JSON file must contain a list.");
            var rows = new List<Dictionary<string, string>>();
            foreach (var item in root.EnumerateArray())
            {
                var row = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                if (item.ValueKind == JsonValueKind.Object)
                    foreach (var p in item.EnumerateObject())
                        if (p.Value.ValueKind == JsonValueKind.String) row[p.Name] = p.Value.GetString()!;
                rows.Add(row);
            }
            return rows;
        }
        catch (JsonException e)
        {
            throw new ImportException($"The file isn't valid JSON: {e.Message}");
        }
    }

    private static List<Dictionary<string, string>> CsvRows(string content, string[] defaultColumns)
    {
        var records = ParseCsv(content);
        if (records.Count == 0) return [];
        var header = records[0].Select(h => h.Trim().ToLowerInvariant()).ToArray();
        bool hasHeader = header.Any(h => defaultColumns.Contains(h));
        var columns = hasHeader ? header : defaultColumns;
        return records.Skip(hasHeader ? 1 : 0)
            .Where(r => r.Any(f => f.Length > 0))
            .Select(r =>
            {
                var row = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                for (int i = 0; i < Math.Min(columns.Length, r.Count); i++) row[columns[i]] = r[i];
                return row;
            }).ToList();
    }

    /// <summary>RFC 4180-style CSV: quoted fields, doubled quotes, newlines inside quotes.</summary>
    internal static List<List<string>> ParseCsv(string content)
    {
        var records = new List<List<string>>();
        var record = new List<string>();
        var field = new StringBuilder();
        bool inQuotes = false;
        for (int i = 0; i < content.Length; i++)
        {
            char c = content[i];
            if (inQuotes)
            {
                if (c == '"')
                {
                    if (i + 1 < content.Length && content[i + 1] == '"') { field.Append('"'); i++; }
                    else inQuotes = false;
                }
                else field.Append(c);
                continue;
            }
            switch (c)
            {
                case '"': inQuotes = true; break;
                case ',': record.Add(field.ToString()); field.Clear(); break;
                case '\r': break;
                case '\n':
                    record.Add(field.ToString()); field.Clear();
                    records.Add(record); record = [];
                    break;
                default: field.Append(c); break;
            }
        }
        if (field.Length > 0 || record.Count > 0)
        {
            record.Add(field.ToString());
            records.Add(record);
        }
        return records;
    }
}
