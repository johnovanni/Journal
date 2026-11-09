using Journal.Core.Domain;

namespace Journal.Core.Inspiration;

public interface IVerseSource
{
    string Name { get; }
    IReadOnlyList<Verse> All();
}

public interface IQuoteSource
{
    string Name { get; }
    IReadOnlyList<Quote> All();
}

/// <summary>A verse or quote source backed by an in-memory list (imported lists, tests).</summary>
public sealed class ListVerseSource(string name, Func<IReadOnlyList<Verse>> items) : IVerseSource
{
    public string Name => name;
    public IReadOnlyList<Verse> All() => items();
}

public sealed class ListQuoteSource(string name, Func<IReadOnlyList<Quote>> items) : IQuoteSource
{
    public string Name => name;
    public IReadOnlyList<Quote> All() => items();
}
