using System.Globalization;
using System.Text.Json.Serialization;
using Journal.Core.Persistence;

namespace Journal.Core.Documents;

/// <summary>Bold / italic / underline / strikethrough flags for a run of text.</summary>
[Flags]
public enum RunFormat
{
    None = 0,
    Bold = 1,
    Italic = 2,
    Underline = 4,
    Strikethrough = 8,
}

public enum ParagraphStyle { Normal, Heading1, Heading2 }

public enum ListKind { None, Bulleted, Numbered, Checklist }

public enum TextAlign { Left, Center, Right, Justify }

/// <summary>An ARGB color stored as a value object and serialized as "#AARRGGBB".</summary>
[JsonConverter(typeof(ArgbColorJsonConverter))]
public readonly record struct ArgbColor(byte A, byte R, byte G, byte B)
{
    public string ToHex() => $"#{A:X2}{R:X2}{G:X2}{B:X2}";

    public override string ToString() => ToHex();

    public static ArgbColor FromRgb(byte r, byte g, byte b) => new(255, r, g, b);

    public static bool TryParse(string? text, out ArgbColor color)
    {
        color = default;
        if (string.IsNullOrWhiteSpace(text)) return false;
        var hex = text.Trim().TrimStart('#');
        if (hex.Length == 6) hex = "FF" + hex;
        if (hex.Length != 8 || !uint.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var v))
            return false;
        color = new ArgbColor((byte)(v >> 24), (byte)(v >> 16), (byte)(v >> 8), (byte)v);
        return true;
    }

    public static ArgbColor Parse(string text) =>
        TryParse(text, out var c) ? c : throw new FormatException($"'{text}' is not a color in #AARRGGBB or #RRGGBB form.");
}

/// <summary>A run of text with uniform formatting. Null font, size or color means "use the default".</summary>
public sealed record TextRun
{
    public string Text { get; init; } = "";
    public RunFormat Format { get; init; }
    public string? FontFamily { get; init; }
    /// <summary>Font size in points.</summary>
    public double? FontSize { get; init; }
    public ArgbColor? Foreground { get; init; }
    public ArgbColor? Highlight { get; init; }

    public TextRun() { }

    public TextRun(string text, RunFormat format = RunFormat.None)
    {
        Text = text;
        Format = format;
    }

    /// <summary>True when the two runs differ only in their text.</summary>
    public bool HasSameFormatting(TextRun other) =>
        Format == other.Format && FontFamily == other.FontFamily && FontSize == other.FontSize &&
        Foreground == other.Foreground && Highlight == other.Highlight;
}
