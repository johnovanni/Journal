using Journal.App.Services;
using Journal.Core.Documents;
using Journal.Core.Services;
using Journal.Core.Settings;
using Journal.Infrastructure.Storage;

namespace Journal.App.ViewModels;

/// <summary>What the editor exposes to its tab view model (implemented by JournalEditor; faked in tests).</summary>
public interface IDocumentEditorHost
{
    /// <summary>Reads the editor's current content into the document model.</summary>
    JournalDocument Capture();
    void InsertImage(ImageBlock block);
    void InsertAttachment(AttachmentBlock block);
}

/// <summary>Services shared by the view models of an unlocked journal.</summary>
public sealed class JournalContext
{
    public required JournalService Journal { get; init; }
    public required IDialogService Dialogs { get; init; }
    public required IScheduler Scheduler { get; init; }
    public required IClock Clock { get; init; }
    public required TempFileManager Temp { get; init; }
    public required IShellLauncher Shell { get; init; }
    public required IClipboardService Clipboard { get; init; }
    public required ImageCache Images { get; init; }
    public required Func<AppSettings> Settings { get; init; }
    public required Action<AppSettings> SaveSettings { get; init; }

    public const long MaxImageBytes = 50L * 1024 * 1024;
    public const long MaxAttachmentBytes = 250L * 1024 * 1024;
    public static readonly TimeSpan AutosaveDelay = TimeSpan.FromSeconds(2);
    public static readonly TimeSpan SearchDelay = TimeSpan.FromMilliseconds(250);
}
