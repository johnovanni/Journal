using System.Security.Cryptography;
using System.Text;

namespace Journal.Core.Security;

/// <summary>
/// A 25-character Crockford base32 recovery code (125 bits), shown as five groups of five.
/// Parsing ignores case, spaces and dashes and accepts O for 0 and I/L for 1.
/// </summary>
public sealed class RecoveryCode : IEquatable<RecoveryCode>
{
    public const string Alphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";
    public const int CharCount = 25;
    public const int GroupSize = 5;

    /// <summary>The 25 canonical characters without separators.</summary>
    public string Normalized { get; }

    public string Formatted => string.Join("-", Enumerable.Range(0, CharCount / GroupSize)
        .Select(i => Normalized.Substring(i * GroupSize, GroupSize)));

    public string LastGroup => Normalized[^GroupSize..];

    private RecoveryCode(string normalized) => Normalized = normalized;

    public static RecoveryCode Generate()
    {
        Span<char> chars = stackalloc char[CharCount];
        for (int i = 0; i < CharCount; i++)
            chars[i] = Alphabet[RandomNumberGenerator.GetInt32(Alphabet.Length)];
        return new RecoveryCode(new string(chars));
    }

    public static bool TryParse(string? text, out RecoveryCode code)
    {
        code = null!;
        if (text is null) return false;
        var sb = new StringBuilder(CharCount);
        foreach (var raw in text)
        {
            if (raw is ' ' or '-' or '\t') continue;
            var c = NormalizeChar(raw);
            if (c is null) return false;
            sb.Append(c.Value);
        }
        if (sb.Length != CharCount) return false;
        code = new RecoveryCode(sb.ToString());
        return true;
    }

    /// <summary>Normalizes user input for comparing a single group (e.g. the confirm step).</summary>
    public static string NormalizeGroup(string text)
    {
        var sb = new StringBuilder();
        foreach (var raw in text)
        {
            if (raw is ' ' or '-' or '\t') continue;
            sb.Append(NormalizeChar(raw) ?? '?');
        }
        return sb.ToString();
    }

    private static char? NormalizeChar(char raw)
    {
        var c = char.ToUpperInvariant(raw);
        c = c switch { 'O' => '0', 'I' or 'L' => '1', _ => c };
        return Alphabet.Contains(c) ? c : null;
    }

    /// <summary>Bytes fed to the key-derivation function.</summary>
    public byte[] ToKdfInput() => Encoding.ASCII.GetBytes(Normalized);

    public bool Equals(RecoveryCode? other) => other is not null && Normalized == other.Normalized;
    public override bool Equals(object? obj) => Equals(obj as RecoveryCode);
    public override int GetHashCode() => Normalized.GetHashCode(StringComparison.Ordinal);
    public override string ToString() => Formatted;
}
