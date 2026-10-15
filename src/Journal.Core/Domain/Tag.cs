namespace Journal.Core.Domain;

/// <summary>
/// A case-insensitive label for an entry or a tab. Equality uses <see cref="Normalized"/>;
/// <see cref="Name"/> keeps the case the user typed.
/// </summary>
public sealed class Tag : IEquatable<Tag>
{
    public const int MaxLength = 40;

    public string Name { get; }
    public string Normalized { get; }

    private Tag(string name)
    {
        Name = name;
        Normalized = name.ToLowerInvariant();
    }

    public static Tag Parse(string? text) =>
        TryParse(text, out var tag, out var error) ? tag : throw new ArgumentException(error, nameof(text));

    public static bool TryParse(string? text, out Tag tag, out string error)
    {
        tag = null!;
        var trimmed = (text ?? "").Trim();
        error = trimmed switch
        {
            "" => "A tag can't be empty.",
            { Length: > MaxLength } => $"A tag can be at most {MaxLength} characters.",
            _ when trimmed.Contains(',') => "A tag can't contain a comma.",
            _ when trimmed.Contains(':') => "A tag can't contain a colon.",
            _ => "",
        };
        if (error.Length > 0) return false;
        tag = new Tag(trimmed);
        return true;
    }

    public bool Equals(Tag? other) => other is not null && Normalized == other.Normalized;
    public override bool Equals(object? obj) => Equals(obj as Tag);
    public override int GetHashCode() => Normalized.GetHashCode(StringComparison.Ordinal);
    public override string ToString() => Name;

    public static bool operator ==(Tag? a, Tag? b) => a is null ? b is null : a.Equals(b);
    public static bool operator !=(Tag? a, Tag? b) => !(a == b);
}
