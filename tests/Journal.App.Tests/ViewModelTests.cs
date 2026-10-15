using System.Globalization;
using Journal.App.ViewModels;
using Journal.Core.Documents;
using Journal.Core.Domain;
using Journal.Core.Security;
using Journal.Core.Settings;
using Journal.Core.Tests.Support;

namespace Journal.App.Tests;

public class EntryViewModelTests
{
    [Fact(DisplayName = "TC-ENT-03/TC-CAL-01 Unlock opens today and shows the current month")]
    public void OpensToday() => Sta.Run(async () =>
    {
        var h = new AppHarness();
        var main = await h.MainAsync();
        Assert.Equal(TestData.Today, main.CurrentEntry!.Date);
        Assert.Equal(TestData.Today, main.Calendar.SelectedDate);
        Assert.Equal(new DateTime(2026, 10, 1).ToString("MMMM yyyy", CultureInfo.CurrentCulture), main.Calendar.Title);
    });

    [Fact(DisplayName = "TC-ENT-04 Heading shows the full date")]
    public void Heading() => Sta.Run(async () =>
    {
        var previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = new CultureInfo("en-US");
        try
        {
            var main = await new AppHarness().MainAsync();
            Assert.Equal("Thursday, October 15, 2026", main.CurrentEntry!.Heading);
        }
        finally { CultureInfo.CurrentCulture = previous; }
    });

    [Fact(DisplayName = "TC-ENT-05 Autosave 2 s after the last edit, and on switching day or tab")]
    public void Autosave() => Sta.Run(async () =>
    {
        var h = new AppHarness();
        var main = await h.MainAsync();
        var entry = main.CurrentEntry!;
        AppHarness.AttachHosts(entry);
        AppHarness.Type(entry.Tabs[0], "first words");
        h.Scheduler.Advance(TimeSpan.FromMilliseconds(1900));
        Assert.Equal(0, h.Repo.SaveCount);
        h.Scheduler.Advance(TimeSpan.FromMilliseconds(100));
        Sta.DoEvents();
        Assert.Equal(1, h.Repo.SaveCount);

        // Switching tab saves.
        h.Dialogs.TagAnswers.Enqueue("family");
        entry.AddTabCommand.Execute(null);
        AppHarness.AttachHosts(entry);
        AppHarness.Type(entry.Tabs[1], "family news");
        entry.SelectedTab = entry.Tabs[0];
        Sta.DoEvents();
        Assert.Equal(2, h.Repo.SaveCount);

        // Switching day saves.
        AppHarness.Type(entry.Tabs[0], "more words");
        await main.SelectDateAsync(TestData.Today.AddDays(1));
        Assert.Equal(3, h.Repo.SaveCount);
        Assert.Equal("more words", (await h.Journal.OpenDayAsync(TestData.Today)).Entry!.DefaultTab.Document.ExtractPlainText());
    });

    [Fact(DisplayName = "TC-ENT-06 Status shows the save time, then an error that keeps the text")]
    public void SaveStatus() => Sta.Run(async () =>
    {
        var h = new AppHarness();
        h.Clock.LocalNow = new DateTimeOffset(2026, 10, 15, 10, 42, 0, TimeSpan.Zero);
        var main = await h.MainAsync();
        var entry = main.CurrentEntry!;
        AppHarness.AttachHosts(entry);
        AppHarness.Type(entry.Tabs[0], "hello");
        await entry.SaveAsync();
        Assert.Equal("Saved 10:42", main.StatusText);
        h.Repo.FailNextSave = new IOException("Disk full.");
        AppHarness.Type(entry.Tabs[0], "hello again");
        await entry.SaveAsync();
        Assert.StartsWith("Couldn't save: Disk full.", main.StatusText);
        Assert.True(entry.IsDirty);
        Assert.Equal("hello again", entry.Tabs[0].Model.Document.ExtractPlainText());
    });

    [Fact(DisplayName = "TC-SEC-16 Flushing before lock saves unsaved typing")]
    public void FlushBeforeLock() => Sta.Run(async () =>
    {
        var h = new AppHarness();
        var main = await h.MainAsync();
        AppHarness.AttachHosts(main.CurrentEntry!);
        AppHarness.Type(main.CurrentEntry!.Tabs[0], "typed just now");
        await main.FlushAsync();
        Assert.Equal(1, h.Repo.SaveCount);
        Assert.Equal(0, h.Scheduler.Pending);
    });

    [Fact(DisplayName = "TC-TAB-02 Adding a tab asks for a tag and selects the new tab")]
    public void AddTab() => Sta.Run(async () =>
    {
        var h = new AppHarness();
        var entry = (await h.MainAsync()).CurrentEntry!;
        h.Dialogs.TagAnswers.Enqueue("Family");
        entry.AddTabCommand.Execute(null);
        Assert.Equal(["Journal", "Family"], entry.Tabs.Select(t => t.Title));
        Assert.Same(entry.Tabs[1], entry.SelectedTab);
        h.Dialogs.TagAnswers.Enqueue("family");
        entry.AddTabCommand.Execute(null);
        Assert.Equal(2, entry.Tabs.Count);
        h.Dialogs.TagAnswers.Enqueue("bad,tag");
        entry.AddTabCommand.Execute(null);
        Assert.Single(h.Dialogs.Errors);
    });

    [Fact(DisplayName = "TC-TAB-05 Deleting a tab with content asks first; cancel keeps it")]
    public void RemoveTab() => Sta.Run(async () =>
    {
        var h = new AppHarness();
        var entry = (await h.MainAsync()).CurrentEntry!;
        h.Dialogs.TagAnswers.Enqueue("empty");
        entry.AddTabCommand.Execute(null);
        entry.RemoveTabCommand.Execute(entry.Tabs[1]);
        Assert.Single(entry.Tabs);
        Assert.Empty(h.Dialogs.Confirms);

        h.Dialogs.TagAnswers.Enqueue("work");
        entry.AddTabCommand.Execute(null);
        AppHarness.AttachHosts(entry);
        AppHarness.Type(entry.Tabs[1], "notes");
        h.Dialogs.ConfirmAnswers.Enqueue(false);
        entry.RemoveTabCommand.Execute(entry.Tabs[1]);
        Assert.Equal(2, entry.Tabs.Count);
        h.Dialogs.ConfirmAnswers.Enqueue(true);
        entry.RemoveTabCommand.Execute(entry.Tabs[1]);
        Assert.Single(entry.Tabs);
        entry.RemoveTabCommand.Execute(entry.Tabs[0]);
        Assert.Single(entry.Tabs);
    });

    [Fact(DisplayName = "TC-TAB-06 Dragging tabs reorders them; Journal stays first")]
    public void MoveTab() => Sta.Run(async () =>
    {
        var h = new AppHarness();
        var entry = (await h.MainAsync()).CurrentEntry!;
        foreach (var t in new[] { "a", "b", "c" }) { h.Dialogs.TagAnswers.Enqueue(t); entry.AddTabCommand.Execute(null); }
        entry.MoveTab(entry.Tabs[3], 1);
        Assert.Equal(["Journal", "c", "a", "b"], entry.Tabs.Select(t => t.Title));
        entry.MoveTab(entry.Tabs[2], 0);
        Assert.Equal("Journal", entry.Tabs[0].Title);
    });

    [Fact(DisplayName = "TC-TAG-04/05 Tab tags can't be removed from the tag bar; clicking a tag searches it")]
    public void TagChips() => Sta.Run(async () =>
    {
        var h = new AppHarness();
        var main = await h.MainAsync();
        var entry = main.CurrentEntry!;
        h.Dialogs.TagAnswers.Enqueue("work");
        entry.AddTabCommand.Execute(null);
        h.Dialogs.TagAnswers.Enqueue("gratitude");
        entry.AddTagCommand.Execute(null);
        var work = entry.Tags.Single(t => t.Name == "work");
        var gratitude = entry.Tags.Single(t => t.Name == "gratitude");
        Assert.True(work.IsTabTag);
        Assert.False(work.CanRemove);
        Assert.True(gratitude.CanRemove);
        work.RemoveCommand.Execute(null);
        Assert.Equal(2, entry.Tags.Count);
        gratitude.SearchCommand.Execute(null);
        Assert.Equal("tag:gratitude", main.Search.QueryText);
        gratitude.RemoveCommand.Execute(null);
        Assert.Single(entry.Tags);
    });

    [Fact(DisplayName = "TC-INSP-01/04 The day shows a WEB verse and a quote; hiding the verse removes it")]
    public void Inspiration() => Sta.Run(async () =>
    {
        var h = new AppHarness();
        var entry = (await h.MainAsync()).CurrentEntry!;
        Assert.True(entry.Inspiration.HasVerse);
        Assert.EndsWith("· WEB", entry.Inspiration.VerseReference);
        Assert.StartsWith("— ", entry.Inspiration.QuoteAuthor);
        h.Settings.Inspiration.ShowVerse = false;
        await entry.RefreshInspirationAsync();
        Assert.False(entry.Inspiration.HasVerse);
        Assert.True(entry.Inspiration.HasAny);
    });
}

public class CopyInspirationTests
{
    [Fact(DisplayName = "TC-INSP-14 Copy puts the verse or quote on the clipboard with its citation")]
    public void CopyVerseAndQuote() => Sta.Run(async () =>
    {
        var h = new AppHarness();
        var main = await h.MainAsync();
        var entry = main.CurrentEntry!;
        var verse = entry.Entry!.Inspiration.Verse!;
        var quote = entry.Entry.Inspiration.Quote!;

        entry.Inspiration.CopyVerseCommand.Execute(null);
        Assert.Equal($"{verse.Text}{Environment.NewLine}— {verse.Reference} (WEB)", h.Clipboard.Text);
        Assert.Equal("Verse copied", main.StatusText);

        entry.Inspiration.CopyQuoteCommand.Execute(null);
        Assert.Equal($"“{quote.Text}”{Environment.NewLine}— {quote.Author}, {quote.Work}", h.Clipboard.Text);

        entry.Inspiration.CopyBothCommand.Execute(null);
        Assert.Contains(verse.Text, h.Clipboard.Text);
        Assert.Contains(quote.Text, h.Clipboard.Text);
        Assert.Equal("Verse and quote copied", main.StatusText);

        h.Clipboard.Busy = true;
        entry.Inspiration.CopyVerseCommand.Execute(null);
        Assert.StartsWith("Couldn't copy", main.StatusText);
    });

    [Fact(DisplayName = "TC-INSP-14 Copied quotes include the source edition when known")]
    public void QuoteCitation()
    {
        var q = new Quote("Waste no more time arguing what a good man should be. Be one.", "Marcus Aurelius", "Meditations", "Stoic") { Source = "tr. George Long (1862)" };
        Assert.Equal($"“{q.Text}”{Environment.NewLine}— Marcus Aurelius, Meditations, tr. George Long (1862)", InspirationViewModel.FormatQuote(q));
    }
}

public class AttachmentViewModelTests
{
    private static async Task<(AppHarness H, EntryTabViewModel Tab, FakeHost Host)> Setup()
    {
        var h = new AppHarness();
        var entry = (await h.MainAsync()).CurrentEntry!;
        var host = new FakeHost();
        entry.Tabs[0].Host = host;
        return (h, entry.Tabs[0], host);
    }

    private static string TempFile(AppHarness h, string name, byte[] content)
    {
        Directory.CreateDirectory(h.TempRoot);
        var path = Path.Combine(h.TempRoot, name);
        File.WriteAllBytes(path, content);
        return path;
    }

    [Fact(DisplayName = "TC-IMG-01 Images from a file, the clipboard and a drop become image blocks")]
    public void InsertImages() => Sta.Run(async () =>
    {
        var (h, tab, host) = await Setup();
        var png = Sta.Png(8, 4);
        h.Dialogs.NextOpenFile = TempFile(h, "a.png", png);
        await tab.InsertImageCommand.ExecuteAsync(null);
        await tab.AddPastedImageAsync(png);
        await tab.AddDroppedFilesAsync([TempFile(h, "b.png", png)]);
        Assert.Equal(3, host.Inserted.OfType<ImageBlock>().Count());
        Assert.All(host.Inserted.OfType<ImageBlock>(), i => Assert.Equal((8, 4), (i.PixelWidth, i.PixelHeight)));
        Assert.Equal(3, h.Blobs.Blobs.Count);
        Assert.True(tab.Owner.IsDirty);
    });

    [Fact(DisplayName = "TC-IMG-02 A file that isn't an image is rejected")]
    public void NotAnImage() => Sta.Run(async () =>
    {
        var (h, tab, host) = await Setup();
        Assert.Null(await tab.AddImageFileAsync(TempFile(h, "fake.png", "hello"u8.ToArray())));
        Assert.Contains("isn't an image", Assert.Single(h.Dialogs.Errors));
        Assert.Empty(host.Inserted);
        Assert.Empty(h.Blobs.Blobs);
    });

    [Fact(DisplayName = "TC-IMG-10 / TC-ATT-05 Size limits: 50 MB images, 250 MB files")]
    public void SizeLimits() => Sta.Run(async () =>
    {
        var (h, tab, host) = await Setup();
        var big = TempFile(h, "big.png", []);
        using (var fs = File.OpenWrite(big)) fs.SetLength(JournalContext.MaxImageBytes + 1);
        Assert.Null(await tab.AddImageFileAsync(big));
        var huge = TempFile(h, "huge.bin", []);
        using (var fs = File.OpenWrite(huge)) fs.SetLength(JournalContext.MaxAttachmentBytes + 1);
        Assert.Null(await tab.AddAttachmentFileAsync(huge));
        Assert.Equal(2, h.Dialogs.Errors.Count);
        Assert.Empty(host.Inserted);
    });

    [Fact(DisplayName = "TC-ATT-01/04 Attach, save as, and open a file (with a one-time notice)")]
    public void Attachments() => Sta.Run(async () =>
    {
        var (h, tab, host) = await Setup();
        var content = "a,b\n1,2"u8.ToArray();
        h.Dialogs.NextOpenFiles = [TempFile(h, "data.csv", content)];
        await tab.AttachFileCommand.ExecuteAsync(null);
        var att = Assert.IsType<AttachmentBlock>(Assert.Single(host.Inserted));
        Assert.Equal("data.csv", att.FileName);
        Assert.Equal("text/csv", att.ContentType);
        Assert.Equal(content.Length, att.SizeBytes);

        h.Dialogs.NextSaveFile = Path.Combine(h.TempRoot, "saved.csv");
        Assert.True(await tab.SaveAttachmentAsAsync(att));
        Assert.Equal(content, File.ReadAllBytes(h.Dialogs.NextSaveFile));

        var opened = await tab.OpenAttachmentAsync(att);
        await tab.OpenAttachmentAsync(att);
        Assert.Equal(content, File.ReadAllBytes(opened!));
        Assert.Equal(2, h.Shell.Opened.Count);
        Assert.Single(h.Dialogs.Infos); // FR-ATT-07 notice only once
        Assert.True(h.Settings.AttachmentNoticeShown);
        h.Context.Temp.CleanAll();
    });
}

public class CalendarAndSearchViewModelTests
{
    [Fact(DisplayName = "TC-CAL-03/04/05/06 Highlights: entries, planned days, open items, today and selection")]
    public void Highlights() => Sta.Run(async () =>
    {
        var h = new AppHarness();
        foreach (var d in new[] { 5, 8, 13 }) await h.Journal.SaveAsync(TestData.Entry(new DateOnly(2026, 10, d), "x"));
        var planned = TestData.Entry(new DateOnly(2026, 10, 20));
        planned.DefaultTab.Document.Blocks.Add(new ParagraphBlock(new TextRun("Doctor")) { List = ListKind.Checklist });
        await h.Journal.SaveAsync(planned);
        var main = await h.MainAsync();
        main.Calendar.ShowSelected(new DateOnly(2026, 10, 20));
        var cells = main.Calendar.Days.ToDictionary(c => c.Date);
        Assert.Equal(42, cells.Count);
        Assert.Equal([5, 8, 13, 20], cells.Values.Where(c => c.HasEntry).Select(c => c.Date.Day));
        Assert.True(cells[new DateOnly(2026, 10, 20)].IsPlanned);
        Assert.False(cells[new DateOnly(2026, 10, 13)].IsPlanned);
        Assert.Equal([20], cells.Values.Where(c => c.HasOpenItems).Select(c => c.Date.Day));
        Assert.True(cells[TestData.Today].IsToday);
        Assert.True(cells[new DateOnly(2026, 10, 20)].IsSelected);
        Assert.False(cells[new DateOnly(2026, 9, 27)].IsInCurrentMonth);
        Assert.False(cells[new DateOnly(2026, 11, 7)].IsInCurrentMonth);
    });

    [Fact(DisplayName = "TC-CAL-07 Month navigation and Today")]
    public void Navigation() => Sta.Run(async () =>
    {
        var main = await new AppHarness().MainAsync();
        var cal = main.Calendar;
        for (int i = 0; i < 3; i++) cal.NextMonthCommand.Execute(null);
        Assert.Equal((2027, 1), (cal.Year, cal.Month));
        for (int i = 0; i < 13; i++) cal.PreviousMonthCommand.Execute(null);
        Assert.Equal((2025, 12), (cal.Year, cal.Month));
        cal.TodayCommand.Execute(null);
        Assert.Equal((2026, 10), (cal.Year, cal.Month));
        Assert.Equal(TestData.Today, cal.SelectedDate);
    });

    [Fact(DisplayName = "TC-CAL-08 Clicking a day or arrow + Enter opens it")]
    public void OpenDays() => Sta.Run(async () =>
    {
        var main = await new AppHarness().MainAsync();
        var opened = new List<DateOnly>();
        main.Calendar.DateSelected += (_, d) => opened.Add(d);
        main.Calendar.SelectDayCommand.Execute(main.Calendar.Days.First(c => c.Date == new DateOnly(2026, 10, 8)));
        main.Calendar.MoveSelectionCommand.Execute(1);
        main.Calendar.OpenSelectedCommand.Execute(null);
        Assert.Equal([new DateOnly(2026, 10, 8), new DateOnly(2026, 10, 9)], opened);
        Sta.DoEvents();
        Assert.Equal(new DateOnly(2026, 10, 9), main.CurrentEntry!.Date);
    });

    [Fact(DisplayName = "TC-CAL-09 Highlights update when an entry is created or emptied")]
    public void LiveHighlights() => Sta.Run(async () =>
    {
        var h = new AppHarness();
        var main = await h.MainAsync();
        await main.SelectDateAsync(new DateOnly(2026, 10, 22));
        var entry = main.CurrentEntry!;
        AppHarness.AttachHosts(entry);
        AppHarness.Type(entry.Tabs[0], "plans");
        await entry.SaveAsync();
        Assert.True(main.Calendar.Days.Single(c => c.Date.Day == 22 && c.IsInCurrentMonth).HasEntry);
        ((FakeHost)entry.Tabs[0].Host!).Document = new JournalDocument();
        entry.MarkDirty();
        await entry.SaveAsync();
        Assert.False(main.Calendar.Days.Single(c => c.Date.Day == 22 && c.IsInCurrentMonth).HasEntry);
    });

    [Fact(DisplayName = "TC-SRCH-01 Search runs 250 ms after typing stops")]
    public void SearchDebounce() => Sta.Run(async () =>
    {
        var h = new AppHarness();
        var main = await h.MainAsync();
        main.Search.QueryText = "gar";
        h.Scheduler.Advance(TimeSpan.FromMilliseconds(100));
        main.Search.QueryText = "garden";
        h.Scheduler.Advance(TimeSpan.FromMilliseconds(249));
        Assert.Equal(0, main.Search.SearchCount);
        h.Scheduler.Advance(TimeSpan.FromMilliseconds(1));
        Assert.Equal(1, main.Search.SearchCount);
    });

    [Fact(DisplayName = "TC-SRCH-11/13/14 Open a result's day and tab; tag suggestions; Esc clears")]
    public void SearchResults() => Sta.Run(async () =>
    {
        var h = new AppHarness();
        var oct13 = TestData.Entry(new DateOnly(2026, 10, 13), "", "family");
        oct13.Tabs[1].Document.Blocks.Add(ParagraphBlock.OfText("Planted the garden"));
        await h.Journal.SaveAsync(oct13);
        var main = await h.MainAsync();
        main.Search.QueryText = "tag:fa";
        Assert.Equal(["family"], main.Search.TagSuggestions);
        main.Search.QueryText = "garden";
        main.Search.Run();
        var hit = Assert.Single(main.Search.Results);
        main.Search.OpenResultCommand.Execute(hit);
        Sta.DoEvents();
        Assert.Equal(new DateOnly(2026, 10, 13), main.CurrentEntry!.Date);
        Assert.Equal("family", main.CurrentEntry.SelectedTab!.Title);
        main.Search.ClearCommand.Execute(null);
        Assert.Equal("", main.Search.QueryText);
        Assert.Empty(main.Search.Results);
    });

    [Fact(DisplayName = "TC-ENT-08 Delete entry asks first and clears the day")]
    public void DeleteEntry() => Sta.Run(async () =>
    {
        var h = new AppHarness();
        await h.Journal.SaveAsync(TestData.Entry(TestData.Today, "to delete"));
        var main = await h.MainAsync();
        h.Dialogs.ConfirmAnswers.Enqueue(false);
        await main.DeleteEntryCommand.ExecuteAsync(null);
        Assert.True(h.Journal.HasEntry(TestData.Today));
        h.Dialogs.ConfirmAnswers.Enqueue(true);
        await main.DeleteEntryCommand.ExecuteAsync(null);
        Assert.False(h.Journal.HasEntry(TestData.Today));
        Assert.True(main.CurrentEntry!.Entry!.IsEmpty());
    });
}

public class SecurityViewModelTests
{
    private static (VaultSession Session, InMemoryHeaderStore Store, FakeKeyProtector Hello, FakeMachine Machine) NewSession()
    {
        var machine = new FakeMachine();
        var store = new InMemoryHeaderStore();
        var hello = new FakeKeyProtector(KeySlotTypes.WindowsHello) { Machine = machine };
        return (new VaultSession(store, [hello, new FakeKeyProtector(KeySlotTypes.RecoveryCode)], machine, new FakeClock()), store, hello, machine);
    }

    [Fact(DisplayName = "TC-SEC-07 Setup only finishes after the last group is typed correctly")]
    public void SetupConfirm() => Sta.Run(async () =>
    {
        var (session, _, _, _) = NewSession();
        var folder = Path.Combine(Path.GetTempPath(), "journal-app-tests", Guid.NewGuid().ToString("N"));
        var vm = new SetupViewModel(_ => session, () => Task.FromResult(true), new FakeDialogs(), () => 0, folder, null);
        await vm.InitializeAsync();
        await vm.CreateCommand.ExecuteAsync(null);
        Assert.Equal(SetupStep.ShowCode, vm.Step);
        var last = vm.RecoveryCodeText[^5..];
        vm.ConfirmInput = "XXXXX";
        Assert.False(vm.CanFinish);
        vm.ConfirmInput = string.Join(" ", last.ToLowerInvariant().ToCharArray());
        Assert.True(vm.CanFinish);
        bool completed = false;
        vm.Completed += (_, _) => completed = true;
        vm.FinishCommand.Execute(null);
        Assert.True(completed);
        Directory.Delete(folder, true);
    });

    [Fact(DisplayName = "TC-SEC-02/05 Setup offers OneDrive only when present and explains missing Windows Hello")]
    public void SetupOptions() => Sta.Run(async () =>
    {
        var (session, _, _, _) = NewSession();
        var withOneDrive = new SetupViewModel(_ => session, () => Task.FromResult(false), new FakeDialogs(), () => 0, @"C:\default", @"C:\OneDrive\Journal");
        await withOneDrive.InitializeAsync();
        Assert.True(withOneDrive.HasOneDrive);
        Assert.Contains("Sign-in options", withOneDrive.HelloMessage);
        withOneDrive.UseOneDriveCommand.Execute(null);
        Assert.Equal(@"C:\OneDrive\Journal", withOneDrive.VaultFolder);
        Assert.False(new SetupViewModel(_ => session, () => Task.FromResult(true), new FakeDialogs(), () => 0, @"C:\default", null).HasOneDrive);
    });

    [Fact(DisplayName = "TC-SEC-09 Cancelled Windows Hello offers retry and recovery code")]
    public void HelloCancelled() => Sta.Run(async () =>
    {
        var (session, _, hello, _) = NewSession();
        await session.CreateVaultAsync(useHello: true);
        await session.LockAsync(LockReason.User);
        hello.FailWith = UnlockFailure.Cancelled;
        var vm = new UnlockViewModel(session, () => 0);
        await vm.InitializeAsync();
        Assert.True(vm.CanUseHello);
        await vm.TryHelloCommand.ExecuteAsync(null);
        Assert.Contains("cancelled", vm.Error);
        Assert.False(session.IsUnlocked);
    });

    [Fact(DisplayName = "TC-SEC-11/12 Wrong recovery code shows a generic message; success on a new PC offers Hello")]
    public void RecoveryCodeUnlock() => Sta.Run(async () =>
    {
        var (session, store, _, machine) = NewSession();
        var setup = await session.CreateVaultAsync(useHello: true);
        await session.LockAsync(LockReason.User);
        machine.MachineName = "OTHER-PC";
        var vm = new UnlockViewModel(session, () => 0);
        await vm.InitializeAsync();
        Assert.False(vm.CanUseHello);
        Assert.True(vm.ShowRecovery);
        vm.RecoveryInput = "nonsense";
        await vm.SubmitRecoveryCodeCommand.ExecuteAsync(null);
        Assert.Equal("That recovery code didn't work.", vm.Error);
        UnlockedEventArgs? args = null;
        vm.Unlocked += (_, a) => args = a;
        vm.RecoveryInput = setup.RecoveryCode.Formatted.ToLowerInvariant();
        await vm.SubmitRecoveryCodeCommand.ExecuteAsync(null);
        Assert.NotNull(args);
        Assert.True(args!.OfferHelloEnrollment);
    });

    [Fact(DisplayName = "TC-SET-01 Settings expose every option and build the saved settings")]
    public void SettingsViewModel() => Sta.Run(async () =>
    {
        var (session, _, _, _) = NewSession();
        await session.CreateVaultAsync(useHello: true);
        var host = new FakeSettingsHost(session);
        var dialogs = new FakeDialogs();
        var vm = new SettingsViewModel(host, dialogs, new AppSettings());
        Assert.Contains(vm.AutoLockOptions, o => o.Minutes == 0);
        Assert.Equal(10, vm.AutoLock.Minutes);
        Assert.Equal(["TEST-PC"], vm.HelloMachines);
        vm.AutoLock = vm.AutoLockOptions.First(o => o.Minutes == 30);
        vm.WeekStart = DayOfWeek.Monday;
        vm.ShowQuote = false;
        vm.Books.First(b => b.Name == "Psalms").IsChecked = true;
        vm.RandomPerDay = true;
        await vm.SaveCommand.ExecuteAsync(null);
        var saved = host.Applied!;
        Assert.Equal(30, saved.AutoLockMinutes);
        Assert.Equal(DayOfWeek.Monday, saved.WeekStart);
        Assert.False(saved.Inspiration.ShowQuote);
        Assert.Equal(["Psalms"], saved.Inspiration.VerseBooks);
        Assert.Equal(Core.Inspiration.SelectionMode.RandomPerDay, saved.Inspiration.Mode);

        dialogs.ConfirmAnswers.Enqueue(true);
        await vm.RegenerateRecoveryCodeCommand.ExecuteAsync(null);
        Assert.Single(dialogs.ShownCodes);
    });

    private sealed class FakeSettingsHost(VaultSession session) : ISettingsHost
    {
        public AppSettings? Applied { get; private set; }
        public VaultSession Session => session;
        public string VaultFolder => @"C:\vault";
        public IReadOnlyList<string> AvailableBooks => ["Psalms", "Proverbs"];
        public IReadOnlyList<string> AvailableTraditions => ["Stoic", "Greek"];
        public int ImportedVerseCount => 0;
        public int ImportedQuoteCount => 0;
        public Task<string> ImportVersesAsync(string path) => Task.FromResult("Imported 1, skipped 0.");
        public Task<string> ImportQuotesAsync(string path) => Task.FromResult("Imported 1, skipped 0.");
        public Task MoveVaultAsync(string newFolder) => Task.CompletedTask;
        public void ExportBackup(string zipPath) { }
        public Task ApplySettingsAsync(AppSettings settings)
        {
            Applied = settings;
            return Task.CompletedTask;
        }
    }
}
