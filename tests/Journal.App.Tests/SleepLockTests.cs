using System.IO;
using System.Runtime.ExceptionServices;
using System.Windows.Threading;
using Journal.App.Services;
using Journal.Core.Documents;
using Journal.Core.Domain;
using Journal.Core.Security;
using Journal.Core.Tests.Support;
using Journal.Infrastructure;
using Microsoft.Win32;

namespace Journal.App.Tests;

/// <summary>
/// ISS-024: locking on sleep must finish a real save without freezing. Each test runs on its own UI thread under a
/// watchdog, so a freeze fails the test instead of hanging the test run.
/// </summary>
public class SleepLockTests
{
    private static readonly TimeSpan Watchdog = TimeSpan.FromSeconds(60);

    /// <summary>
    /// Long enough that only a real freeze fails these tests: with hundreds of tests running in parallel, a real
    /// save was measured taking up to 4 seconds, against about 0.1 second on a quiet PC.
    /// </summary>
    private static readonly TimeSpan GenerousLimit = TimeSpan.FromSeconds(45);

    private sealed class Rig : IDisposable
    {
        public string Folder { get; } = Path.Combine(Path.GetTempPath(), "journal-app-tests", Guid.NewGuid().ToString("N"));
        public FakeClock Clock { get; } = new();
        public VaultSession Session { get; private set; } = null!;
        public VaultServices Services { get; private set; } = null!;
        public JournalEntry Entry { get; private set; } = null!;
        public LockCoordinator Locks { get; private set; } = null!;
        public List<LockReason> Locked { get; } = [];
        public int ExistsAfterLock { get; private set; }

        /// <summary>An unlocked real vault with unsaved text; locking saves it through real file writes.</summary>
        public static async Task<Rig> CreateAsync()
        {
            var r = new Rig();
            Directory.CreateDirectory(r.Folder);
            var machine = new FakeMachine();
            r.Session = new VaultSession(new InMemoryHeaderStore(),
                [new FakeKeyProtector(KeySlotTypes.WindowsHello) { Machine = machine }, new FakeKeyProtector(KeySlotTypes.RecoveryCode)], machine, r.Clock);
            await r.Session.CreateVaultAsync(useHello: true);
            r.Services = await VaultServices.OpenAsync(r.Session, r.Folder, r.Clock, () => new Journal.Core.Inspiration.InspirationSettings());
            r.Entry = (await r.Services.Journal.OpenDayAsync(r.Clock.Today)).Entry!;
            r.Entry.DefaultTab.Document.Blocks.Add(ParagraphBlock.OfText("unsaved text"));
            r.Session.RegisterBeforeLock(async _ =>
            {
                await r.Services.Journal.SaveAsync(r.Entry);
                await r.Services.Journal.CollectGarbageAsync([]);
            });
            r.Session.Locked += (_, reason) =>
            {
                r.Locked.Add(reason);
                r.ExistsAfterLock = r.Services.Journal.HasEntry(r.Clock.Today) ? 1 : 0;
            };
            r.Locks = new LockCoordinator(r.Clock, () => r.Session, () => { }, Dispatcher.CurrentDispatcher) { WaitLimit = GenerousLimit };
            return r;
        }

        public void Dispose()
        {
            Services?.Dispose();
            try { Directory.Delete(Folder, recursive: true); } catch (IOException) { }
        }
    }

    /// <summary>Runs a test on a UI thread of its own; a freeze fails it after the watchdog time.</summary>
    private static void RunGuarded(Func<Task> test)
    {
        ExceptionDispatchInfo? error = null;
        using var done = new ManualResetEventSlim();
        var thread = new Thread(() =>
        {
            var dispatcher = Dispatcher.CurrentDispatcher;
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(dispatcher));
            dispatcher.BeginInvoke(async () =>
            {
                try { await test(); }
                catch (Exception e) { error = ExceptionDispatchInfo.Capture(e); }
                finally
                {
                    done.Set();
                    dispatcher.BeginInvokeShutdown(DispatcherPriority.Background);
                }
            });
            Dispatcher.Run();
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(done.Wait(Watchdog), "the app froze: the test didn't finish");
        error?.Throw();
    }

    [Fact(DisplayName = "TC-SEC-28 Going to sleep with unsaved text saves and locks without freezing")]
    public void SleepOnUiThread() => RunGuarded(async () =>
    {
        using var r = await Rig.CreateAsync();
        r.Locks.HandlePowerMode(PowerModes.Suspend);
        Assert.Equal([LockReason.Suspend], r.Locked);
        Assert.False(r.Session.IsUnlocked);
        Assert.Equal(1, r.ExistsAfterLock);
    });

    [Fact(DisplayName = "TC-SEC-28 The sleep notification also works when it arrives on another thread")]
    public void SleepFromOtherThread() => RunGuarded(async () =>
    {
        using var r = await Rig.CreateAsync();
        await Task.Run(() => r.Locks.HandlePowerMode(PowerModes.Suspend));
        Assert.Equal([LockReason.Suspend], r.Locked);
        Assert.Equal(1, r.ExistsAfterLock);
    });

    [Fact(DisplayName = "TC-SEC-28 Windows shutting down saves and locks without freezing")]
    public void WindowsShutdown() => RunGuarded(async () =>
    {
        using var r = await Rig.CreateAsync();
        r.Locks.PrepareExit();
        Assert.True(r.Locks.LockAndWait(LockReason.Exit));
        Assert.Equal([LockReason.Exit], r.Locked);
        Assert.Equal(1, r.ExistsAfterLock);
    });

    [Fact(DisplayName = "TC-SEC-28 Sleeping during a lock waits for that lock instead of starting another")]
    public void SleepDuringLock() => RunGuarded(async () =>
    {
        using var r = await Rig.CreateAsync();
        var locking = r.Locks.LockAsync(LockReason.Idle);
        r.Locks.HandlePowerMode(PowerModes.Suspend);

        // By the time the sleep notification returns, the lock already under way has finished, and no other started.
        Assert.False(r.Session.IsUnlocked);
        Assert.Equal([LockReason.Idle], r.Locked);
        Assert.Equal(1, r.ExistsAfterLock);

        // The first caller's own wrapper finishes on the next turn of the UI thread, which is harmless.
        await locking;
        Assert.Equal([LockReason.Idle], r.Locked);
    });

    [Fact(DisplayName = "TC-SEC-28 A save that takes too long can't hold up sleep: the wait stops at its limit and is logged")]
    public void WaitLimit() => RunGuarded(async () =>
    {
        var clock = new FakeClock();
        var machine = new FakeMachine();
        var session = new VaultSession(new InMemoryHeaderStore(),
            [new FakeKeyProtector(KeySlotTypes.WindowsHello) { Machine = machine }, new FakeKeyProtector(KeySlotTypes.RecoveryCode)], machine, clock);
        await session.CreateVaultAsync(useHello: true);
        var release = new TaskCompletionSource();
        session.RegisterBeforeLock(_ => release.Task);
        var locks = new LockCoordinator(clock, () => session, () => { }, Dispatcher.CurrentDispatcher) { WaitLimit = TimeSpan.FromMilliseconds(200) };
        var problems = new List<string>();
        locks.LockProblem += (_, p) => problems.Add(p);

        Assert.False(locks.LockAndWait(LockReason.Suspend));
        Assert.Contains("didn't finish", Assert.Single(problems));
        Assert.True(session.IsUnlocked);

        // The save carries on afterwards and the lock still completes.
        release.SetResult();
        await Dispatcher.Yield(DispatcherPriority.Background);
        for (int i = 0; i < 50 && session.IsUnlocked; i++) await Task.Delay(10);
        Assert.False(session.IsUnlocked);
    });

    [Fact(DisplayName = "TC-SEC-28 A failure while locking is reported, not thrown at Windows")]
    public void FailureReported() => RunGuarded(async () =>
    {
        using var r = await Rig.CreateAsync();
        r.Session.Locked += (_, _) => throw new InvalidOperationException("window trouble");
        var problems = new List<string>();
        r.Locks.LockProblem += (_, p) => problems.Add(p);
        Assert.False(r.Locks.LockAndWait(LockReason.Suspend));
        Assert.Contains("InvalidOperationException", Assert.Single(problems));
    });
}
