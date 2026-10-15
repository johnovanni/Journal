using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Journal.App.Services;
using Journal.Core.Documents;
using Journal.Core.Domain;
using Journal.Core.Services;

namespace Journal.App.ViewModels;

/// <summary>The day's verse and quote, with copy commands for pasting elsewhere.</summary>
public sealed partial class InspirationViewModel(IClipboardService? clipboard = null) : ObservableObject
{
    private DailyInspiration _current = DailyInspiration.None;

    [ObservableProperty] private string _verseText = "";
    [ObservableProperty] private string _verseReference = "";
    [ObservableProperty] private string _quoteText = "";
    [ObservableProperty] private string _quoteAuthor = "";
    [ObservableProperty] private bool _hasVerse;
    [ObservableProperty] private bool _hasQuote;

    public bool HasAny => HasVerse || HasQuote;

    /// <summary>Raised after a copy with a short confirmation for the status bar.</summary>
    public event EventHandler<string>? Copied;

    public void Update(DailyInspiration inspiration)
    {
        _current = inspiration;
        HasVerse = inspiration.Verse is not null;
        VerseText = inspiration.Verse?.Text ?? "";
        VerseReference = inspiration.Verse is { } v
            ? (string.IsNullOrEmpty(v.Translation) ? v.Reference : $"{v.Reference} · {v.Translation}")
            : "";
        HasQuote = inspiration.Quote is not null;
        QuoteText = inspiration.Quote?.Text ?? "";
        QuoteAuthor = inspiration.Quote is { } q ? (q.Work is null ? $"— {q.Author}" : $"— {q.Author}, {q.Work}") : "";
        OnPropertyChanged(nameof(HasAny));
    }

    /// <summary>Verse as pasted text: the passage, then "— Reference (Translation)".</summary>
    public static string FormatVerse(Verse v) =>
        $"{v.Text}{Environment.NewLine}— {v.Reference}" + (string.IsNullOrEmpty(v.Translation) ? "" : $" ({v.Translation})");

    /// <summary>Quote as pasted text: the quote, then "— Author, Work, source edition".</summary>
    public static string FormatQuote(Quote q)
    {
        var cite = string.Join(", ", new[] { q.Author, q.Work, q.Source }.Where(s => !string.IsNullOrWhiteSpace(s)));
        return $"“{q.Text}”{Environment.NewLine}— {cite}";
    }

    [RelayCommand]
    private void CopyVerse()
    {
        if (_current.Verse is { } v) Copy(FormatVerse(v), "Verse copied");
    }

    [RelayCommand]
    private void CopyQuote()
    {
        if (_current.Quote is { } q) Copy(FormatQuote(q), "Quote copied");
    }

    [RelayCommand]
    private void CopyBoth()
    {
        var parts = new List<string>();
        if (_current.Verse is { } v) parts.Add(FormatVerse(v));
        if (_current.Quote is { } q) parts.Add(FormatQuote(q));
        if (parts.Count > 0) Copy(string.Join(Environment.NewLine + Environment.NewLine, parts), "Verse and quote copied");
    }

    private void Copy(string text, string message)
    {
        if (clipboard is null) return;
        Copied?.Invoke(this, clipboard.TrySetText(text) ? message : "Couldn't copy: another app is using the clipboard. Try again.");
    }
}

public sealed partial class TagChipViewModel(string name, bool isTabTag, EntryViewModel owner) : ObservableObject
{
    public string Name { get; } = name;
    public bool IsTabTag { get; } = isTabTag;
    public bool CanRemove => !IsTabTag && !owner.IsReadOnly;

    [RelayCommand]
    private void Remove() => owner.RemoveTag(this);

    [RelayCommand]
    private void Search() => owner.RequestTagSearch(Name);
}

/// <summary>One tab of the open entry. Talks to its editor through <see cref="IDocumentEditorHost"/>.</summary>
public sealed partial class EntryTabViewModel : ObservableObject
{
    private readonly JournalContext _ctx;

    public EntryTab Model { get; }
    public EntryViewModel Owner { get; }
    public IDocumentEditorHost? Host { get; set; }

    public EntryTabViewModel(JournalContext ctx, EntryViewModel owner, EntryTab model)
    {
        _ctx = ctx;
        Owner = owner;
        Model = model;
    }

    public string Title => Model.Title;
    public bool IsDefault => Model.IsDefault;
    public bool CanClose => !IsDefault && !Owner.IsReadOnly;
    public bool IsReadOnly => Owner.IsReadOnly;
    public JournalDocument Document => Model.Document;

    public void RefreshTitle() => OnPropertyChanged(nameof(Title));

    /// <summary>Copies the editor's content into the model.</summary>
    public void Capture()
    {
        if (Host is not null && !IsReadOnly) Model.Document = Host.Capture();
    }

    public void MarkDirty() => Owner.MarkDirty();

    [RelayCommand]
    private async Task InsertImage()
    {
        var path = _ctx.Dialogs.OpenFile("Insert image", "Images|*.png;*.jpg;*.jpeg;*.gif;*.bmp;*.tif;*.tiff;*.heic;*.webp|All files|*.*");
        if (path is not null) await AddImageFileAsync(path);
    }

    [RelayCommand]
    private async Task AttachFile()
    {
        foreach (var path in _ctx.Dialogs.OpenFiles("Attach files", "All files|*.*"))
            await AddAttachmentFileAsync(path);
    }

    /// <summary>Handles files dropped on the editor: images become image blocks, other files attachments.</summary>
    public async Task AddDroppedFilesAsync(IEnumerable<string> paths)
    {
        foreach (var path in paths.Where(File.Exists))
        {
            if (ImageProbe.LooksLikeImage(path)) await AddImageFileAsync(path);
            else await AddAttachmentFileAsync(path);
        }
    }

    public async Task<ImageBlock?> AddImageFileAsync(string path)
    {
        if (IsReadOnly) return null;
        var length = new FileInfo(path).Length;
        if (length > JournalContext.MaxImageBytes)
        {
            _ctx.Dialogs.Error("Image too large", "Images can be at most 50 MB.");
            return null;
        }
        await using var fs = File.OpenRead(path);
        return await AddImageStreamAsync(fs);
    }

    public async Task<ImageBlock?> AddPastedImageAsync(byte[] png)
    {
        if (IsReadOnly) return null;
        if (png.LongLength > JournalContext.MaxImageBytes)
        {
            _ctx.Dialogs.Error("Image too large", "Images can be at most 50 MB.");
            return null;
        }
        using var ms = new MemoryStream(png);
        return await AddImageStreamAsync(ms);
    }

    private async Task<ImageBlock?> AddImageStreamAsync(Stream stream)
    {
        var size = ImageProbe.TryGetSize(stream);
        if (size is null)
        {
            _ctx.Dialogs.Error("Can't insert image", "This file isn't an image Windows can open.");
            return null;
        }
        var blob = await _ctx.Journal.AddBlobAsync(stream);
        var block = new ImageBlock { Image = blob, PixelWidth = size.Value.Width, PixelHeight = size.Value.Height };
        Host?.InsertImage(block);
        MarkDirty();
        return block;
    }

    public async Task<AttachmentBlock?> AddAttachmentFileAsync(string path)
    {
        if (IsReadOnly) return null;
        var info = new FileInfo(path);
        if (info.Length > JournalContext.MaxAttachmentBytes)
        {
            _ctx.Dialogs.Error("File too large", "Attached files can be at most 250 MB.");
            return null;
        }
        BlobRef blob;
        await using (var fs = info.OpenRead()) blob = await _ctx.Journal.AddBlobAsync(fs);
        var block = new AttachmentBlock
        {
            File = blob,
            FileName = info.Name,
            ContentType = ContentTypes.For(info.Name),
            SizeBytes = info.Length,
        };
        Host?.InsertAttachment(block);
        MarkDirty();
        return block;
    }

    /// <summary>Decrypts the file to the private temp folder and opens it (FR-ATT-03, FR-ATT-07).</summary>
    public async Task<string?> OpenAttachmentAsync(AttachmentBlock block)
    {
        var settings = _ctx.Settings();
        if (!settings.AttachmentNoticeShown)
        {
            _ctx.Dialogs.Info("Opening a copy",
                "Changes to this copy aren't saved to your journal. The copy is deleted when the journal locks or closes.");
            settings.AttachmentNoticeShown = true;
            _ctx.SaveSettings(settings);
        }
        var path = _ctx.Temp.NewFilePath(block.FileName);
        try
        {
            await using (var fs = new FileStream(path, FileMode.CreateNew, FileAccess.Write))
                await _ctx.Journal.CopyBlobToAsync(block.File, fs);
            File.SetAttributes(path, FileAttributes.ReadOnly);
            _ctx.Shell.Open(path);
            return path;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception or Journal.Core.Persistence.UnreadableVaultFileException)
        {
            _ctx.Dialogs.Error("Can't open file", e.Message);
            return null;
        }
    }

    public async Task<bool> SaveAttachmentAsAsync(AttachmentBlock block)
    {
        var target = _ctx.Dialogs.SaveFile("Save attached file", block.FileName, "All files|*.*");
        if (target is null) return false;
        await using var fs = new FileStream(target, FileMode.Create, FileAccess.Write);
        await _ctx.Journal.CopyBlobToAsync(block.File, fs);
        return true;
    }

    public async Task ViewImageAsync(ImageBlock block)
    {
        var bytes = await _ctx.Journal.ReadBlobAsync(block.Image);
        _ctx.Dialogs.ShowImage(ImageCache.Decode(bytes), block.Caption);
    }
}

/// <summary>The open day: tabs, tags, verse and quote, autosave (FR-ENT-*, FR-TAB-*, FR-TAG-*).</summary>
public sealed partial class EntryViewModel : ObservableObject
{
    private readonly JournalContext _ctx;
    private readonly Debouncer _autosave;
    private bool _saving;

    public JournalEntry? Entry { get; }
    public DateOnly Date { get; }
    public string Heading { get; }
    public bool IsReadOnly { get; }
    public string? LoadError { get; }
    public InspirationViewModel Inspiration { get; }
    public ObservableCollection<EntryTabViewModel> Tabs { get; } = [];
    public ObservableCollection<TagChipViewModel> Tags { get; } = [];

    [ObservableProperty] private EntryTabViewModel? _selectedTab;
    [ObservableProperty] private bool _isDirty;
    [ObservableProperty] private string _saveStatus = "";

    public event EventHandler<string>? TagSearchRequested;
    public event EventHandler<SaveOutcome>? Saved;

    public bool IsFuture => Date > _ctx.Clock.Today;

    public EntryViewModel(JournalContext ctx, DateOnly date, EntryLoadResult load)
    {
        _ctx = ctx;
        Date = date;
        Heading = date.ToString("dddd, MMMM d, yyyy", CultureInfo.CurrentCulture);
        _autosave = new Debouncer(ctx.Scheduler, JournalContext.AutosaveDelay, () => _ = SaveAsync());
        Inspiration = new InspirationViewModel(ctx.Clipboard);
        Inspiration.Copied += (_, message) => SaveStatus = message;
        Entry = load.Entry;
        IsReadOnly = load.IsUnreadable || load.Entry is null;
        LoadError = load.IsUnreadable ? "This entry could not be read; it may be damaged. It has been left unchanged." : load.Error;

        if (Entry is not null)
        {
            foreach (var t in Entry.Tabs) Tabs.Add(new EntryTabViewModel(ctx, this, t));
            Inspiration.Update(ctx.Journal.DisplayInspiration(Entry));
        }
        SelectedTab = Tabs.FirstOrDefault();
        RefreshTags();
    }

    public void MarkDirty()
    {
        if (IsReadOnly) return;
        IsDirty = true;
        SaveStatus = "Editing…";
        _autosave.Trigger();
    }

    partial void OnSelectedTabChanged(EntryTabViewModel? oldValue, EntryTabViewModel? newValue)
    {
        // Save when switching tabs (FR-ENT-05). The old tab's editor is still attached at this point.
        if (oldValue is not null && IsDirty) _ = SaveAsync();
    }

    public void CaptureAll()
    {
        foreach (var t in Tabs) t.Capture();
    }

    public async Task<SaveOutcome?> SaveAsync()
    {
        if (IsReadOnly || Entry is null || _saving) return null;
        _autosave.Cancel();
        _saving = true;
        try
        {
            CaptureAll();
            SaveStatus = "Saving…";
            var outcome = await _ctx.Journal.SaveAsync(Entry);
            IsDirty = false;
            SaveStatus = outcome switch
            {
                SaveOutcome.Deleted => "Empty entry removed",
                SaveOutcome.Skipped => "",
                _ => $"Saved {_ctx.Clock.LocalNow:HH:mm}",
            };
            Saved?.Invoke(this, outcome);
            return outcome;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            SaveStatus = $"Couldn't save: {e.Message} Your text is still here; it will be saved again on the next change.";
            return null;
        }
        finally
        {
            _saving = false;
        }
    }

    /// <summary>Discards a pending autosave (used before deleting the entry).</summary>
    public void CancelPendingSave()
    {
        _autosave.Cancel();
        IsDirty = false;
    }

    /// <summary>Saves now if there are unsaved changes (switching day, locking, closing).</summary>
    public async Task FlushAsync()
    {
        _autosave.Cancel();
        if (IsDirty || Tabs.Any(t => t.Host is not null)) await SaveIfChangedAsync();
    }

    private async Task SaveIfChangedAsync()
    {
        if (IsReadOnly || Entry is null) return;
        if (!IsDirty)
        {
            // Captions and checkboxes may change without a text change; compare before saving.
            var before = Tabs.Select(t => Journal.Core.Persistence.JournalJson.SerializeDocument(t.Model.Document)).ToList();
            CaptureAll();
            var after = Tabs.Select(t => Journal.Core.Persistence.JournalJson.SerializeDocument(t.Model.Document)).ToList();
            if (before.SequenceEqual(after)) return;
        }
        await SaveAsync();
    }

    public void RefreshTags()
    {
        Tags.Clear();
        if (Entry is null) return;
        foreach (var t in Entry.AllTags()) Tags.Add(new TagChipViewModel(t.Name, Entry.IsTabTag(t), this));
    }

    [RelayCommand]
    private void AddTab()
    {
        if (IsReadOnly || Entry is null) return;
        var name = _ctx.Dialogs.PromptTag("Add a tab", _ctx.Journal.AllTags());
        if (name is null) return;
        if (!Tag.TryParse(name, out var tag, out var error))
        {
            _ctx.Dialogs.Error("Add a tab", error);
            return;
        }
        var existing = Entry.FindTab(tag);
        var tab = Entry.AddTab(tag);
        if (existing is not null)
        {
            SelectedTab = Tabs.First(t => t.Model == existing);
            return;
        }
        var vm = new EntryTabViewModel(_ctx, this, tab);
        Tabs.Add(vm);
        SelectedTab = vm;
        RefreshTags();
        MarkDirty();
    }

    [RelayCommand]
    private void RemoveTab(EntryTabViewModel? tab)
    {
        if (tab is null || tab.IsDefault || IsReadOnly || Entry is null) return;
        tab.Capture();
        if (!tab.Model.Document.IsEmpty() &&
            !_ctx.Dialogs.Confirm("Delete tab", $"Delete the \"{tab.Title}\" tab and everything in it?", "Delete tab"))
            return;
        if (SelectedTab == tab) SelectedTab = Tabs[0];
        Entry.RemoveTab(tab.Model.Id);
        tab.Host = null;
        Tabs.Remove(tab);
        RefreshTags();
        MarkDirty();
    }

    [RelayCommand]
    private void RenameTab(EntryTabViewModel? tab)
    {
        if (tab is null || tab.IsDefault || IsReadOnly || Entry is null) return;
        var name = _ctx.Dialogs.PromptTag("Rename tab", _ctx.Journal.AllTags(), tab.Title);
        if (name is null) return;
        if (!Tag.TryParse(name, out var tag, out var error))
        {
            _ctx.Dialogs.Error("Rename tab", error);
            return;
        }
        try
        {
            Entry.RetagTab(tab.Model.Id, tag);
        }
        catch (DuplicateTagException e)
        {
            _ctx.Dialogs.Error("Rename tab", e.Message);
            return;
        }
        tab.RefreshTitle();
        RefreshTags();
        MarkDirty();
    }

    /// <summary>Moves a tab (drag and drop); the Journal tab stays first (FR-TAB-06).</summary>
    public void MoveTab(EntryTabViewModel tab, int newIndex)
    {
        if (Entry is null || tab.IsDefault) return;
        Entry.MoveTab(tab.Model.Id, newIndex);
        var target = Entry.Tabs.ToList().IndexOf(tab.Model);
        Tabs.Move(Tabs.IndexOf(tab), target);
        MarkDirty();
    }

    [RelayCommand]
    private void AddTag()
    {
        if (IsReadOnly || Entry is null) return;
        var name = _ctx.Dialogs.PromptTag("Add a tag", _ctx.Journal.AllTags());
        if (name is null) return;
        if (!Tag.TryParse(name, out var tag, out var error))
        {
            _ctx.Dialogs.Error("Add a tag", error);
            return;
        }
        if (Entry.AddTag(tag))
        {
            RefreshTags();
            MarkDirty();
        }
    }

    public void RemoveTag(TagChipViewModel chip)
    {
        if (Entry is null || chip.IsTabTag || IsReadOnly) return;
        if (Entry.RemoveTag(Tag.Parse(chip.Name)))
        {
            RefreshTags();
            MarkDirty();
        }
    }

    public void RequestTagSearch(string name) => TagSearchRequested?.Invoke(this, name);

    [RelayCommand]
    private void Shuffle()
    {
        if (Entry is null || IsReadOnly) return;
        Inspiration.Update(_ctx.Journal.Shuffle(Entry));
        if (!Entry.IsEmpty()) MarkDirty();
    }

    /// <summary>Re-applies verse/quote settings: unsaved days get a fresh pick, saved days keep theirs.</summary>
    public async Task RefreshInspirationAsync()
    {
        if (Entry is null) return;
        if (!Entry.IsInspirationPinned && (await _ctx.Journal.OpenDayAsync(Date)).Entry is { } fresh)
            Entry.Inspiration = fresh.Inspiration;
        Inspiration.Update(_ctx.Journal.DisplayInspiration(Entry));
    }
}
