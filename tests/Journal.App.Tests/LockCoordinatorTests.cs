using System.IO;
using System.Windows.Threading;
using Journal.App.Services;
using Journal.App.ViewModels;
using Journal.Core.Security;
using Journal.Core.Settings;
using Journal.Core.Tests.Support;
using Journal.Infrastructure;
using Microsoft.Win32;

namespace Journal.App.Tests;

/// <summary>ISS-018: locking, auto-lock and exiting, and the Settings actions, tested without the app's windows.</summary>
public class LockCoordinatorTests
{
    private sealed class Rig
    {
        public FakeClock Clock { get; } = new();
        public VaultSession Session { get; private set; } = null!;
        public LockCoordinator Locks { get; private set; } = null!;
        public List<LockReason> Locked { get; } = [];
        public int Shutdowns { get; private set; }

        public static async Task<Rig> CreateAsync()
        {
            var r = new Rig();
            var machine = new FakeMachine();
            r.Session = new VaultSession(new InMemoryHeaderStore(),
                [new FakeKeyProtector(KeySlotTypes.WindowsHello) { Machine = machine }, new FakeKeyProtector(KeySlotTypes.RecoveryCode)], machine, r.Clock);
            await r.Session.CreateVaultAsync(useHello: true);
            r.Session.Locked += (_, reason) => r.Locked.Add(reason);
            r.Locks = new LockCoordinator(r.Clock, () => r.Session, () => r.Shutdowns++, Dispatcher.CurrentDispatcher);
            return r;
        }
    }

    /// <summary>Lets a queued dispatcher action run.</summary>
    private static async Task Pump() => await Dispatcher.Yield(DispatcherPriority.Background);

    [Fact(DisplayName = "TC-SEC-13 Auto-lock fires once the timeout passes, and activity resets it")]
    public void AutoLock() => Sta.Run(async () =>
    {
        var r = await Rig.CreateAsync();
        r.Locks.Timeout = TimeSpan.FromMinutes(10);
        r.Locks.NoteActivity();
        string status = "";
        r.Locks.StatusChanged += (_, text) => status = text;

        r.Clock.Advance(TimeSpan.FromMinutes(9));
        r.Locks.Tick();
        Assert.Empty(r.Locked);
        Assert.Equal("Locks after 1 min without activity", status);

        r.Locks.NoteActivity();                     // activity restarts the wait
        r.Clock.Advance(TimeSpan.FromMinutes(9));
        r.Locks.Tick();
        Assert.Empty(r.Locked);

        r.Clock.Advance(TimeSpan.FromMinutes(1));
        r.Locks.Tick();
        await Pump();
        Assert.Equal([LockReason.Idle], r.Locked);
    });

    [Fact(DisplayName = "TC-SEC-13 With auto-lock off, the journal never locks by itself")]
    public void AutoLockOff() => Sta.Run(async () =>
    {
        var r = await Rig.CreateAsync();
        r.Locks.Timeout = null;
        string status = "";
        r.Locks.StatusChanged += (_, text) => status = text;
        r.Clock.Advance(TimeSpan.FromDays(30));
        r.Locks.Tick();
        await Pump();
        Assert.Empty(r.Locked);
        Assert.Equal("Auto-lock is off", status);
    });

    [Theory(DisplayName = "TC-SEC-14 Locking Windows or disconnecting locks the journal; other session changes don't")]
    [InlineData(SessionSwitchReason.SessionLock, true)]
    [InlineData(SessionSwitchReason.ConsoleDisconnect, true)]
    [InlineData(SessionSwitchReason.RemoteDisconnect, true)]
    [InlineData(SessionSwitchReason.SessionUnlock, false)]
    [InlineData(SessionSwitchReason.ConsoleConnect, false)]
    public void WindowsLock(SessionSwitchReason reason, bool locks) => Sta.Run(async () =>
    {
        var r = await Rig.CreateAsync();
        r.Locks.HandleSessionSwitch(reason);
        await Pump();
        await Pump();
        Assert.Equal(locks ? [LockReason.SessionLock] : [], r.Locked);
    });

    [Fact(DisplayName = "TC-SEC-14 Going to sleep locks the journal before returning; waking doesn't")]
    public void Sleep() => Sta.Run(async () =>
    {
        var r = await Rig.CreateAsync();
        r.Locks.HandlePowerMode(PowerModes.Resume);
        Assert.Empty(r.Locked);
        r.Locks.HandlePowerMode(PowerModes.Suspend);
        Assert.Equal([LockReason.Suspend], r.Locked);
    });

    [Fact(DisplayName = "TC-SEC-15 Two lock requests at once lock once")]
    public void LockOnce() => Sta.Run(async () =>
    {
        var r = await Rig.CreateAsync();
        var release = new TaskCompletionSource();
        r.Session.RegisterBeforeLock(_ => release.Task);
        var first = r.Locks.LockAsync(LockReason.User);
        var second = r.Locks.LockAsync(LockReason.Idle);
        Assert.True(second.IsCompleted);   // the second request does nothing
        release.SetResult();
        await first;
        Assert.Equal([LockReason.User], r.Locked);
    });

    [Fact(DisplayName = "TC-SEC-26 Exiting while a lock is under way waits for it, then shuts down once")]
    public void ExitDuringLock() => Sta.Run(async () =>
    {
        var r = await Rig.CreateAsync();
        var release = new TaskCompletionSource();
        r.Session.RegisterBeforeLock(_ => release.Task);
        var locking = r.Locks.LockAsync(LockReason.Idle);

        var exiting = r.Locks.LockForExitAsync();
        Assert.True(r.Locks.IsExiting);
        Assert.False(exiting.IsCompleted);   // the save is still running, so the app must not end yet

        release.SetResult();
        await exiting;
        await locking;
        Assert.Equal([LockReason.Idle], r.Locked);

        r.Locks.Shutdown();
        r.Locks.Shutdown();
        Assert.Equal(1, r.Shutdowns);
    });

    [Fact(DisplayName = "TC-SEC-26 Exiting with the journal already locked just shuts down")]
    public void ExitWhenLocked() => Sta.Run(async () =>
    {
        var r = await Rig.CreateAsync();
        await r.Locks.LockAsync(LockReason.User);
        await r.Locks.LockForExitAsync();
        r.Locks.Shutdown();
        Assert.Equal([LockReason.User], r.Locked);
        Assert.Equal(1, r.Shutdowns);
    });

    // ------------------------------------------------------------------ Settings actions

    private sealed class FakeOpenJournal : IOpenJournal
    {
        public AppSettings Settings { get; set; } = new();
        public string VaultFolder { get; set; } = @"C:\old";
        public VaultSession? Session { get; set; }
        public VaultServices? Services { get; set; }
        public MainViewModel? Main { get; set; }
    }

    private sealed class FakeStore : ISettingsStore
    {
        public int Saves { get; private set; }
        public AppSettings? Saved { get; private set; }
        public AppSettings Load() => new();
        public void Save(AppSettings settings) { Saves++; Saved = settings; }
    }

    [Fact(DisplayName = "TC-SET-01 Applying settings saves them and updates auto-lock and the calendar")]
    public void ApplySettings() => Sta.Run(async () =>
    {
        var r = await Rig.CreateAsync();
        var h = new AppHarness();
        var main = await h.MainAsync();
        var journal = new FakeOpenJournal { Session = r.Session, Main = main };
        var store = new FakeStore();
        var actions = new SettingsActions(journal, store, r.Locks, new FakeDialogs(), () => { });

        var settings = new AppSettings { AutoLockMinutes = 30, WeekStart = DayOfWeek.Monday };
        await actions.ApplySettingsAsync(settings);

        Assert.Same(settings, journal.Settings);
        Assert.Same(settings, store.Saved);
        Assert.Equal(TimeSpan.FromMinutes(30), r.Locks.Timeout);
        Assert.Equal(DayOfWeek.Monday, main.Calendar.WeekStart);
    });

    [Fact(DisplayName = "TC-SET-03 Moving the journal saves first, moves, remembers the folder, then locks")]
    public void MoveVault() => Sta.Run(async () =>
    {
        var r = await Rig.CreateAsync();
        var h = new AppHarness();
        var main = await h.MainAsync();
        var entry = main.CurrentEntry!;
        entry.MarkDirty();
        var journal = new FakeOpenJournal { Session = r.Session, Main = main };
        var store = new FakeStore();
        var steps = new List<string>();
        var actions = new SettingsActions(journal, store, r.Locks, new FakeDialogs(),
            () => steps.Add("close settings"),
            (from, to) => steps.Add($"move {from} to {to}; unsaved={entry.IsDirty}"));

        await actions.MoveVaultAsync(@"C:\new");

        Assert.Equal([@"move C:\old to C:\new; unsaved=False", "close settings"], steps);
        Assert.Equal(@"C:\new", journal.Settings.VaultFolder);
        Assert.Equal(1, store.Saves);
        Assert.Equal([LockReason.User], r.Locked);
    });

    [Fact(DisplayName = "TC-INSP-10 Importing quotes through Settings updates the count and reports rows")]
    public void ImportQuotes() => Sta.Run(async () =>
    {
        var folder = Path.Combine(Path.GetTempPath(), "journal-app-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var r = await Rig.CreateAsync();
        using var services = await VaultServices.OpenAsync(r.Session, folder, r.Clock, () => new Journal.Core.Inspiration.InspirationSettings());
        var journal = new FakeOpenJournal { Session = r.Session, Services = services };
        var actions = new SettingsActions(journal, new FakeStore(), r.Locks, new FakeDialogs(), () => { });
        Assert.Equal(0, actions.ImportedQuoteCount);

        var file = Path.Combine(folder, "quotes.csv");
        await File.WriteAllTextAsync(file, "text,author\nKnow thyself,Socrates\n,nobody\n");
        Assert.Equal("Imported 1, skipped 1.", await actions.ImportQuotesAsync(file));
        Assert.Equal(1, actions.ImportedQuoteCount);
        Assert.Same(r.Session, actions.Session);
    });

    [Fact(DisplayName = "TC-SET-01 Settings actions refuse to reach a locked journal")]
    public void LockedJournal() => Sta.Run(async () =>
    {
        var r = await Rig.CreateAsync();
        var actions = new SettingsActions(new FakeOpenJournal(), new FakeStore(), r.Locks, new FakeDialogs(), () => { });
        Assert.Throws<InvalidOperationException>(() => actions.Session);
        Assert.Equal(0, actions.ImportedVerseCount);
        Assert.Empty(actions.VerseTraditions);
    });
}
