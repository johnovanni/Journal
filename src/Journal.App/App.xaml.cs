using System.Threading;
using System.Windows;
using System.Windows.Threading;
using Journal.App.Services;
using Journal.Infrastructure.Logging;
using Journal.Infrastructure.Storage;
using Microsoft.Extensions.Logging;

namespace Journal.App;

public partial class App : Application
{
    private const string MutexName = @"Local\Journal.App.SingleInstance";
    private const string ActivateEventName = @"Local\Journal.App.Activate";

    private Mutex? _mutex;
    private EventWaitHandle? _activate;
    private AppController? _controller;
    private ILogger _log = null!;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Only one instance; a second launch brings the first to the front (FR-SEC-19).
        _mutex = new Mutex(initiallyOwned: true, MutexName, out bool isFirst);
        _activate = new EventWaitHandle(false, EventResetMode.AutoReset, ActivateEventName);
        if (!isFirst)
        {
            _activate.Set();
            Shutdown();
            return;
        }
        var listener = new Thread(() =>
        {
            while (_activate.WaitOne())
                Dispatcher.BeginInvoke(BringToFront);
        }) { IsBackground = true };
        listener.Start();

        _log = new FileLoggerProvider(AppPaths.LogFolder).CreateLogger("Journal");
        DispatcherUnhandledException += OnUnhandledException;
        _log.LogInformation("Started.");

        _controller = new AppController(new JsonSettingsStore(AppPaths.SettingsFile), _log);
        _controller.Start();
    }

    private void BringToFront()
    {
        var window = Windows.OfType<Window>().FirstOrDefault(w => w.IsVisible) ?? MainWindow;
        if (window is null) return;
        if (window.WindowState == WindowState.Minimized) window.WindowState = WindowState.Normal;
        window.Activate();
        window.Topmost = true;
        window.Topmost = false;
        window.Focus();
    }

    private void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        // Log the error type only; never content.
        _log.LogError(e.Exception, "Unhandled error.");
        new DialogService().Error("Something went wrong",
            "Journal hit an unexpected problem. Your saved entries are safe. If this keeps happening, restart the app.");
        e.Handled = true;
    }

    protected override void OnSessionEnding(SessionEndingCancelEventArgs e)
    {
        _controller?.OnApplicationExit();
        base.OnSessionEnding(e);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _log?.LogInformation("Exited.");
        _mutex?.Dispose();
        base.OnExit(e);
    }
}
