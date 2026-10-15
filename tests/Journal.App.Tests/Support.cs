using System.Runtime.ExceptionServices;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Journal.App.Services;
using Journal.App.ViewModels;
using Journal.Core.Documents;
using Journal.Core.Settings;
using Journal.Core.Tests.Support;
using Journal.Infrastructure.Storage;

namespace Journal.App.Tests;

/// <summary>Runs a test on a fresh STA thread with a WPF dispatcher and synchronization context.</summary>
public static class Sta
{
    public static void Run(Func<Task> test)
    {
        Exception? error = null;
        var thread = new Thread(() =>
        {
            var dispatcher = Dispatcher.CurrentDispatcher;
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(dispatcher));
            var frame = new DispatcherFrame();
            dispatcher.BeginInvoke(async () =>
            {
                try { await test(); }
                catch (Exception e) { error = e; }
                finally { frame.Continue = false; }
            });
            Dispatcher.PushFrame(frame);
            dispatcher.InvokeShutdown();
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (error is not null) ExceptionDispatchInfo.Capture(error).Throw();
    }

    public static void Run(Action test) => Run(() => { test(); return Task.CompletedTask; });

    /// <summary>Lets queued dispatcher work run.</summary>
    public static void DoEvents()
    {
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background, () => frame.Continue = false);
        Dispatcher.PushFrame(frame);
    }

    public static byte[] Png(int width = 8, int height = 4)
    {
        var pixels = new byte[width * height * 4];
        new Random(1).NextBytes(pixels);
        var bmp = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, width * 4);
        return ImageProbe.EncodePng(bmp);
    }
}

public sealed class FakeDialogs : IDialogService
{
    public Queue<string?> TagAnswers { get; } = new();
    public Queue<bool> ConfirmAnswers { get; } = new();
    public string? NextOpenFile { get; set; }
    public string[] NextOpenFiles { get; set; } = [];
    public string? NextSaveFile { get; set; }
    public string? NextFolder { get; set; }
    public List<string> Errors { get; } = [];
    public List<string> Infos { get; } = [];
    public List<string> Confirms { get; } = [];
    public List<string> ShownImages { get; } = [];
    public List<string> ShownCodes { get; } = [];

    public string? OpenFile(string title, string filter) => NextOpenFile;
    public string[] OpenFiles(string title, string filter) => NextOpenFiles;
    public string? SaveFile(string title, string fileName, string filter) => NextSaveFile;
    public string? PickFolder(string title, string? initial = null) => NextFolder;
    public bool Confirm(string title, string message, string confirmText = "OK")
    {
        Confirms.Add(message);
        return ConfirmAnswers.Count > 0 && ConfirmAnswers.Dequeue();
    }
    public void Info(string title, string message) => Infos.Add(message);
    public void Error(string title, string message) => Errors.Add(message);
    public string? PromptTag(string title, IReadOnlyList<string> suggestions, string? initial = null) => TagAnswers.Count > 0 ? TagAnswers.Dequeue() : null;
    public void ShowImage(ImageSource image, string caption) => ShownImages.Add(caption);
    public void ShowRecoveryCode(string code) => ShownCodes.Add(code);
}

public sealed class FakeClipboard : IClipboardService
{
    public string? Text { get; private set; }
    public bool Busy { get; set; }
    public bool TrySetText(string text)
    {
        if (Busy) return false;
        Text = text;
        return true;
    }
}

public sealed class FakeShell : IShellLauncher
{
    public List<string> Opened { get; } = [];
    public void Open(string path) => Opened.Add(path);
}

/// <summary>Editor stand-in: holds a document and records inserted blocks.</summary>
public sealed class FakeHost(JournalDocument? doc = null) : IDocumentEditorHost
{
    public JournalDocument Document { get; set; } = doc ?? new JournalDocument();
    public List<DocBlock> Inserted { get; } = [];
    public JournalDocument Capture() => Document;
    public void InsertImage(ImageBlock block) { Inserted.Add(block); Document.Blocks.Add(block); }
    public void InsertAttachment(AttachmentBlock block) { Inserted.Add(block); Document.Blocks.Add(block); }
}

/// <summary>A main view model over an in-memory journal with manual time.</summary>
public sealed class AppHarness
{
    public FakeClock Clock { get; } = new();
    public ManualScheduler Scheduler { get; } = new();
    public FakeDialogs Dialogs { get; } = new();
    public FakeShell Shell { get; } = new();
    public FakeClipboard Clipboard { get; } = new();
    public AppSettings Settings { get; set; } = new();
    public int SettingsSaves { get; private set; }
    public Core.Services.JournalService Journal { get; }
    public InMemoryEntryRepository Repo { get; }
    public InMemoryBlobStore Blobs { get; }
    public JournalContext Context { get; }
    public string TempRoot { get; } = Path.Combine(Path.GetTempPath(), "journal-app-tests", Guid.NewGuid().ToString("N"));

    public AppHarness()
    {
        (Journal, Repo, Blobs, _) = TestData.Service(Clock, () => Settings.Inspiration.ToSettings());
        Journal.InitializeAsync().GetAwaiter().GetResult();
        Context = new JournalContext
        {
            Journal = Journal,
            Dialogs = Dialogs,
            Scheduler = Scheduler,
            Clock = Clock,
            Temp = new TempFileManager(TempRoot),
            Shell = Shell,
            Clipboard = Clipboard,
            Images = new ImageCache(b => Journal.ReadBlobAsync(b)),
            Settings = () => Settings,
            SaveSettings = s => { Settings = s; SettingsSaves++; },
        };
    }

    public async Task<MainViewModel> MainAsync()
    {
        var vm = new MainViewModel(Context);
        await vm.InitializeAsync();
        return vm;
    }

    /// <summary>Attaches fake editors to every tab of the current entry.</summary>
    public static void AttachHosts(EntryViewModel entry)
    {
        foreach (var t in entry.Tabs) t.Host ??= new FakeHost(t.Model.Document);
    }

    public static void Type(EntryTabViewModel tab, string text)
    {
        var host = (FakeHost)tab.Host!;
        host.Document = new JournalDocument([ParagraphBlock.OfText(text)]);
        tab.MarkDirty();
    }
}
