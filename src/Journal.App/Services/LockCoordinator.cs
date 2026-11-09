using System.Windows.Input;
using System.Windows.Threading;
using Journal.Core.Security;
using Journal.Core.Services;
using Microsoft.Win32;

namespace Journal.App.Services;

/// <summary>
/// Decides when the journal locks and when the app ends: auto-lock after inactivity, Windows lock and sleep,
/// the Lock button, and exiting (FR-SEC-13..16, FR-SEC-21). Separate from the windows so it can be tested.
/// </summary>
public sealed class LockCoordinator
{
    private readonly Func<VaultSession?> _session;
    private readonly Action _shutdown;
    private readonly Dispatcher _dispatcher;
    private readonly AutoLockPolicy _autoLock;
    private readonly DispatcherTimer _idleTimer;
    private bool _locking;
    private Task? _lockInProgress;
    private bool _shutdownRequested;
    private bool _monitoring;
    private bool _systemEventsHooked;

    /// <param name="session">The current vault session, if any.</param>
    /// <param name="shutdown">Ends the app; called at most once.</param>
    public LockCoordinator(IClock clock, Func<VaultSession?> session, Action shutdown, Dispatcher dispatcher)
    {
        _session = session;
        _shutdown = shutdown;
        _dispatcher = dispatcher;
        _autoLock = new AutoLockPolicy(clock);
        _autoLock.LockRequested += async (_, _) => await LockAsync(LockReason.Idle);
        _idleTimer = new DispatcherTimer(DispatcherPriority.Background, dispatcher) { Interval = TimeSpan.FromSeconds(5) };
        _idleTimer.Tick += (_, _) => Tick();
    }

    /// <summary>True once the app has started to exit; a lock then doesn't return to the unlock window.</summary>
    public bool IsExiting { get; private set; }

    /// <summary>Raised on every idle tick with the text for the status bar.</summary>
    public event EventHandler<string>? StatusChanged;

    /// <summary>Raised when <see cref="LockAndWait"/> didn't finish in time or failed; for the log.</summary>
    public event EventHandler<string>? LockProblem;

    /// <summary>
    /// How long <see cref="LockAndWait"/> waits. Windows won't wait long for an app when sleeping or shutting
    /// down, and a save that takes longer carries on afterwards.
    /// </summary>
    public TimeSpan WaitLimit { get; init; } = TimeSpan.FromSeconds(5);

    public TimeSpan? Timeout
    {
        get => _autoLock.Timeout;
        set => _autoLock.Timeout = value;
    }

    public string StatusText => _autoLock.Remaining is { } r
        ? $"Locks after {Math.Max(1, (int)Math.Ceiling(r.TotalMinutes))} min without activity"
        : "Auto-lock is off";

    public void NoteActivity() => _autoLock.OnActivity();

    /// <summary>Checks the auto-lock timeout and updates the status text.</summary>
    public void Tick()
    {
        _autoLock.Tick();
        StatusChanged?.Invoke(this, StatusText);
    }

    /// <summary>Locks unless the journal is already locked or a lock is under way.</summary>
    public async Task LockAsync(LockReason reason)
    {
        if (_locking || _session() is not { IsUnlocked: true } session) return;
        _locking = true;
        try
        {
            _lockInProgress = session.LockAsync(reason);
            await _lockInProgress;
        }
        finally
        {
            _locking = false;
        }
    }

    public void PrepareExit() => IsExiting = true;

    /// <summary>
    /// Saves and locks on the way out (used by <see cref="MainWindowCloser"/>). If a lock is already under way,
    /// waits for it, so the app doesn't end in the middle of its save.
    /// </summary>
    public Task LockForExitAsync()
    {
        PrepareExit();
        return LockOrJoin(LockReason.Exit);
    }

    private Task LockOrJoin(LockReason reason) =>
        _locking && _lockInProgress is { } running ? running : LockAsync(reason);

    /// <summary>
    /// Locks and doesn't return until that has finished or <see cref="WaitLimit"/> has passed, for when Windows is
    /// about to sleep or shut down (FR-SEC-14). Returns true when the lock finished.
    /// The lock's save continues on the UI thread, so that thread must not simply be blocked (ISS-024).
    /// </summary>
    public bool LockAndWait(LockReason reason)
    {
        var task = _dispatcher.CheckAccess()
            ? LockOrJoin(reason)
            : _dispatcher.InvokeAsync(() => LockOrJoin(reason)).Task.Unwrap();
        if (!WaitForLock(task))
        {
            LockProblem?.Invoke(this, $"Locking ({reason}) didn't finish within {WaitLimit.TotalSeconds:0} seconds.");
            return false;
        }
        if (task.IsFaulted)
        {
            LockProblem?.Invoke(this, $"Locking ({reason}) failed: {task.Exception!.GetBaseException().GetType().Name}.");
            return false;
        }
        return true;
    }

    private bool WaitForLock(Task task)
    {
        if (task.IsCompleted) return true;
        if (!_dispatcher.CheckAccess())
            return Task.WhenAny(task, Task.Delay(WaitLimit)).GetAwaiter().GetResult() == task;

        // On the UI thread: keep it processing work while waiting, so the save can go on.
        var frame = new DispatcherFrame();
        Task.WhenAny(task, Task.Delay(WaitLimit)).ContinueWith(_ => frame.Continue = false, TaskScheduler.Default);
        Dispatcher.PushFrame(frame);
        return task.IsCompleted;
    }

    /// <summary>Ends the app. Safe to call more than once.</summary>
    public void Shutdown()
    {
        PrepareExit();
        if (_shutdownRequested) return;
        _shutdownRequested = true;
        UnhookSystemEvents();
        _shutdown();
    }

    // ------------------------------------------------------------------ while the journal is open

    /// <summary>Starts the auto-lock clock and watches for activity.</summary>
    public void BeginMonitoring(TimeSpan? timeout)
    {
        Timeout = timeout;
        NoteActivity();
        if (!_monitoring)
        {
            _monitoring = true;
            InputManager.Current.PreProcessInput += OnInput;
            _idleTimer.Start();
        }
        Tick();
    }

    public void EndMonitoring()
    {
        _idleTimer.Stop();
        if (_monitoring) InputManager.Current.PreProcessInput -= OnInput;
        _monitoring = false;
    }

    private void OnInput(object sender, PreProcessInputEventArgs e)
    {
        if (e.StagingItem.Input is KeyboardEventArgs or MouseButtonEventArgs or MouseWheelEventArgs ||
            e.StagingItem.Input is MouseEventArgs && e.StagingItem.Input.RoutedEvent == Mouse.MouseMoveEvent)
            NoteActivity();
    }

    // ------------------------------------------------------------------ Windows lock and sleep

    public void HookSystemEvents()
    {
        if (_systemEventsHooked) return;
        _systemEventsHooked = true;
        SystemEvents.SessionSwitch += OnSessionSwitch;
        SystemEvents.PowerModeChanged += OnPowerModeChanged;
    }

    private void UnhookSystemEvents()
    {
        if (!_systemEventsHooked) return;
        _systemEventsHooked = false;
        SystemEvents.SessionSwitch -= OnSessionSwitch;
        SystemEvents.PowerModeChanged -= OnPowerModeChanged;
    }

    private void OnSessionSwitch(object sender, SessionSwitchEventArgs e) => HandleSessionSwitch(e.Reason);

    private void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e) => HandlePowerMode(e.Mode);

    internal void HandleSessionSwitch(SessionSwitchReason reason)
    {
        if (reason is SessionSwitchReason.SessionLock or SessionSwitchReason.ConsoleDisconnect or SessionSwitchReason.RemoteDisconnect)
            _dispatcher.InvokeAsync(() => LockAsync(LockReason.SessionLock));
    }

    internal void HandlePowerMode(PowerModes mode)
    {
        if (mode == PowerModes.Suspend) LockAndWait(LockReason.Suspend);
    }
}
