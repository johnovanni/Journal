namespace Journal.Core.Services;

public interface IClock
{
    DateTimeOffset UtcNow { get; }
    DateTimeOffset LocalNow { get; }
    DateOnly Today => DateOnly.FromDateTime(LocalNow.DateTime);
}

public sealed class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
    public DateTimeOffset LocalNow => DateTimeOffset.Now;
}

/// <summary>
/// Schedules callbacks after a delay. The WPF app uses a dispatcher-backed implementation;
/// tests use a manual one so timing can be advanced deterministically.
/// </summary>
public interface IScheduler
{
    IDisposable Schedule(TimeSpan delay, Action action);
}

/// <summary>Runs an action once input has been quiet for <see cref="Delay"/> (autosave, search-as-you-type).</summary>
public sealed class Debouncer(IScheduler scheduler, TimeSpan delay, Action action) : IDisposable
{
    private IDisposable? _pending;

    public TimeSpan Delay { get; } = delay;
    public bool IsPending => _pending is not null;

    public void Trigger()
    {
        _pending?.Dispose();
        _pending = scheduler.Schedule(Delay, Fire);
    }

    /// <summary>Runs the action now if one is pending.</summary>
    public void Flush()
    {
        if (_pending is null) return;
        Fire();
    }

    public void Cancel()
    {
        _pending?.Dispose();
        _pending = null;
    }

    private void Fire()
    {
        _pending?.Dispose();
        _pending = null;
        action();
    }

    public void Dispose() => Cancel();
}
