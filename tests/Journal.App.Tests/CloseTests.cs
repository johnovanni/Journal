using System.Windows;
using Journal.App.Services;

namespace Journal.App.Tests;

/// <summary>ISS-016: one click on the main window's close button saves, locks, closes and shuts down.</summary>
public class CloseTests
{
    private sealed class Run
    {
        public Window Window { get; } = new() { Left = -20000, Width = 200, Height = 100, ShowActivated = false, ShowInTaskbar = false };
        public int Shutdowns;
        public int SaveAndLockCalls;
        public List<Exception> Errors { get; } = [];
        public TaskCompletionSource Done { get; } = new();
        public MainWindowCloser Closer { get; }

        /// <param name="saveAndLock">What locking does; it gets the window, as the real lock does.</param>
        public Run(Func<Window, Task> saveAndLock)
        {
            Window.Show();
            Closer = new MainWindowCloser(Window,
                () => { SaveAndLockCalls++; return saveAndLock(Window); },
                () => { Shutdowns++; Done.TrySetResult(); },
                Errors.Add);
        }
    }

    /// <summary>
    /// What the real lock does when nothing needs saving: it finishes at once and clears the window. Before
    /// ISS-016 this ran inside the Closing event and the window's own Close() then failed, leaving the app open.
    /// </summary>
    private static Task LockAtOnce(Window w)
    {
        w.Content = null;
        w.Close();   // the lock closes the main window; during an exit the closer finishes this
        return Task.CompletedTask;
    }

    [Fact(DisplayName = "TC-SEC-26 Closing with nothing to save closes the window and shuts down on the first click")]
    public void NothingToSave() => Sta.Run(async () =>
    {
        var r = new Run(LockAtOnce);
        r.Window.Close();
        await r.Done.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(1, r.SaveAndLockCalls);
        Assert.Equal(1, r.Shutdowns);
        Assert.False(r.Window.IsVisible);
        Assert.Empty(r.Errors);
    });

    [Fact(DisplayName = "TC-SEC-26 Closing with unsaved text waits for the save, then closes and shuts down")]
    public void SlowSave() => Sta.Run(async () =>
    {
        bool saved = false;
        var r = new Run(async w =>
        {
            await Task.Delay(50);
            saved = true;
            await LockAtOnce(w);
        });
        r.Window.Close();
        Assert.True(r.Window.IsVisible);   // still open while saving
        r.Window.Close();                  // a second click while saving is ignored
        await r.Done.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.True(saved);
        Assert.Equal(1, r.SaveAndLockCalls);
        Assert.Equal(1, r.Shutdowns);
        Assert.False(r.Window.IsVisible);
        Assert.Empty(r.Errors);
    });

    [Fact(DisplayName = "TC-SEC-26 If saving or locking fails, the app still closes and shuts down")]
    public void FailingSave() => Sta.Run(async () =>
    {
        var r = new Run(_ => throw new IOException("disk full"));
        r.Window.Close();
        await r.Done.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(1, r.Shutdowns);
        Assert.False(r.Window.IsVisible);
        Assert.IsType<IOException>(Assert.Single(r.Errors));
    });

    [Fact(DisplayName = "TC-SEC-26 After Detach (locking without exiting) the window closes normally and the app doesn't shut down")]
    public void Detached() => Sta.Run(async () =>
    {
        var r = new Run(LockAtOnce);
        r.Closer.Detach();
        r.Window.Close();
        await Task.Delay(50);
        Assert.False(r.Window.IsVisible);
        Assert.Equal(0, r.SaveAndLockCalls);
        Assert.Equal(0, r.Shutdowns);
    });
}
