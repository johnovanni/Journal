using System.ComponentModel;
using System.Windows;
using System.Windows.Threading;

namespace Journal.App.Services;

/// <summary>
/// Closing the main window saves, locks and exits in one click (FR-SEC-21, ISS-016).
/// The first close is cancelled so the journal can be saved and locked; that work starts only after the
/// window's Closing event has returned, because WPF refuses to close (or show) a window from inside its own
/// Closing event. When it is done, or if it fails, the window is closed for real and the app is told to shut
/// down, so the process never stays behind without a window.
/// </summary>
public sealed class MainWindowCloser
{
    private readonly Window _window;
    private readonly Func<Task> _saveAndLock;
    private readonly Action _shutdown;
    private readonly Action<Exception> _onError;
    private bool _exiting;
    private bool _allowClose;

    public MainWindowCloser(Window window, Func<Task> saveAndLock, Action shutdown, Action<Exception> onError)
    {
        _window = window;
        _saveAndLock = saveAndLock;
        _shutdown = shutdown;
        _onError = onError;
        _window.Closing += OnClosing;
    }

    /// <summary>True once the user has asked to close and the app is saving, locking and exiting.</summary>
    public bool IsExiting => _exiting;

    /// <summary>Stops handling the window's close (when the window is closed for another reason, such as locking).</summary>
    public void Detach() => _window.Closing -= OnClosing;

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (_allowClose) return;   // the final close below
        e.Cancel = true;
        if (_exiting) return;      // already exiting; ignore further clicks
        _exiting = true;
        _ = ExitAsync();
    }

    private async Task ExitAsync()
    {
        try
        {
            // Leave the Closing event before anything can try to close or change the window.
            await Dispatcher.Yield(DispatcherPriority.Background);
            await _saveAndLock();
        }
        catch (Exception e)
        {
            _onError(e);
        }
        finally
        {
            _allowClose = true;
            try
            {
                if (PresentationSource.FromVisual(_window) is not null || _window.IsVisible) _window.Close();
            }
            catch (InvalidOperationException e)
            {
                _onError(e);
            }
            _shutdown();
        }
    }
}
