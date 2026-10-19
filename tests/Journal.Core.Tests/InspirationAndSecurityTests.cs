using Journal.Core.Calendar;
using Journal.Core.Domain;
using Journal.Core.Inspiration;
using Journal.Core.Security;
using Journal.Core.Tests.Support;

namespace Journal.Core.Tests;

public class InspirationTests
{
    private static readonly Guid Vault = Guid.Parse("11111111-2222-3333-4444-555555555555");

    [Fact(DisplayName = "TC-INSP-05 Book and tradition filters; empty filter falls back with a warning")]
    public void Filters()
    {
        var svc = TestData.Inspiration();
        var s = new InspirationSettings { VerseBooks = new HashSet<string> { "Psalms" }, Traditions = new HashSet<string> { "Philosophy/Stoic" } };
        var categories = new HashSet<string>();
        for (int d = 0; d < 60; d++)
        {
            var r = svc.For(TestData.Today.AddDays(d), s, Vault);
            Assert.Equal("Psalms", r.Inspiration.Verse!.Book);
            var q = r.Inspiration.Quote!;
            categories.Add(q.Category);
            // The Stoic choice narrows Philosophy only; Literature, with nothing ticked, keeps all its quotes (ISS-015).
            if (q.Category == QuoteCategories.Philosophy) Assert.Equal("Stoic", q.Tradition);
        }
        Assert.Equal(2, categories.Count);
        var none = svc.For(TestData.Today, s with { VerseBooks = new HashSet<string> { "Obadiah" } }, Vault);
        Assert.NotNull(none.Inspiration.Verse);
        Assert.Single(none.Warnings);
    }

    [Fact(DisplayName = "TC-INSP-05 Quote categories filter the quotes; turning all off falls back with a warning")]
    public void CategoryFilter()
    {
        var svc = TestData.Inspiration();
        Assert.Equal(["Literature", "Philosophy"], svc.AvailableCategories().Select(c => c.Name));
        Assert.Equal(["English"], svc.AvailableCategories()[0].Traditions);
        Assert.Equal(["Greek", "Stoic"], svc.AvailableCategories()[1].Traditions);

        var noLit = new InspirationSettings { HiddenCategories = new HashSet<string> { "Literature" } };
        var noPhil = new InspirationSettings { HiddenCategories = new HashSet<string> { "philosophy" } };
        var both = new HashSet<string>();
        for (int d = 0; d < 60; d++)
        {
            var day = TestData.Today.AddDays(d);
            Assert.Equal(QuoteCategories.Philosophy, svc.For(day, noLit, Vault).Inspiration.Quote!.Category);
            Assert.Equal(QuoteCategories.Literature, svc.For(day, noPhil, Vault).Inspiration.Quote!.Category);
            both.Add(svc.For(day, new InspirationSettings(), Vault).Inspiration.Quote!.Category);
        }
        Assert.Equal(2, both.Count);   // default: all categories on

        var none = svc.For(TestData.Today, new InspirationSettings { HiddenCategories = new HashSet<string> { "Literature", "Philosophy" } }, Vault);
        Assert.NotNull(none.Inspiration.Quote);
        Assert.Single(none.Warnings);
    }

    [Fact(DisplayName = "TC-INSP-05 A tradition ticked under one category doesn't pick that tradition in another (ISS-015)")]
    public void TraditionsBelongToTheirCategory()
    {
        var quotes = new List<Quote>
        {
            new("The Way that can be told is not the eternal Way.", "Lao Tzu", null, "Chinese"),
            new("Virtue is not left to stand alone.", "Confucius", null, "Chinese"),
            new("Man is the measure of all things.", "Protagoras", null, "Greek"),
            new("We cannot keep the gold of yesterday.", "Li Po", null, "Chinese") { Category = QuoteCategories.Literature },
            new("All the world's a stage.", "William Shakespeare", null, "English") { Category = QuoteCategories.Literature },
        };
        var svc = new DailyInspirationService([], [new ListQuoteSource(SourceNames.BuiltIn, () => quotes)]);
        IReadOnlyList<Quote> Shown(params string[] traditions)
        {
            var s = new InspirationSettings { ShowVerse = false, Traditions = traditions.ToHashSet(), Mode = SelectionMode.RandomPerDay };
            return Enumerable.Range(0, 200).Select(d => svc.For(TestData.Today.AddDays(d), s, Vault).Inspiration.Quote!).Distinct().ToList();
        }

        // Philosophy › Chinese: Chinese philosophy only from Philosophy; all of Literature, which has nothing ticked.
        var philosophyChinese = Shown(TraditionChoice.Make("Philosophy", "Chinese"));
        Assert.DoesNotContain(philosophyChinese, q => q.Author == "Protagoras");
        Assert.Contains(philosophyChinese, q => q.Author == "Li Po");
        Assert.Contains(philosophyChinese, q => q.Author == "William Shakespeare");

        // Literature › English only: Li Po is out; Philosophy keeps everything.
        var literatureEnglish = Shown(TraditionChoice.Make("Literature", "English"));
        Assert.DoesNotContain(literatureEnglish, q => q.Author == "Li Po");
        Assert.Contains(literatureEnglish, q => q.Author == "Protagoras");

        // Settings saved before ISS-015 ("Chinese" with no category) apply to every category that has it.
        var legacy = Shown("Chinese");
        Assert.All(legacy, q => Assert.Equal("Chinese", q.Tradition));
        Assert.Contains(legacy, q => q.Author == "Li Po");
        Assert.Contains(legacy, q => q.Author == "Confucius");
    }

    [Fact(DisplayName = "TC-INSP-05 A category that exists only in the content needs no code change")]
    public void FutureCategory()
    {
        var quotes = TestData.Quotes().Append(new Quote("Nothing in life is to be feared.", "Marie Curie", null, "French") { Category = "Science" }).ToList();
        var svc = new DailyInspirationService([], [new ListQuoteSource(SourceNames.BuiltIn, () => quotes)]);
        Assert.Contains(svc.AvailableCategories(), c => c.Name == "Science" && c.Traditions.SequenceEqual(["French"]));
        var onlyScience = new InspirationSettings { ShowVerse = false, HiddenCategories = new HashSet<string> { "Literature", "Philosophy" } };
        Assert.Equal("Marie Curie", svc.For(TestData.Today, onlyScience, Vault).Inspiration.Quote!.Author);
    }

    [Fact(DisplayName = "TC-INSP-05 Days saved before categories existed keep a philosophy label")]
    public void StoredQuoteWithoutCategory()
    {
        var json = "{\"text\":\"Know thyself\",\"author\":\"Socrates\",\"work\":null,\"tradition\":\"Greek\"}";
        var q = System.Text.Json.JsonSerializer.Deserialize<Quote>(json, Journal.Core.Persistence.JournalJson.Options)!;
        Assert.Equal(QuoteCategories.Philosophy, q.Category);
    }

    [Fact(DisplayName = "TC-INSP-06 Day-of-year repeats yearly; random differs by year and vault but is stable")]
    public void SelectionModes()
    {
        var svc = TestData.Inspiration();
        var doy = new InspirationSettings();
        Assert.Equal(svc.For(new DateOnly(2026, 10, 15), doy, Vault).Inspiration, svc.For(new DateOnly(2025, 10, 15), doy, Vault).Inspiration);
        var rnd = new InspirationSettings { Mode = SelectionMode.RandomPerDay };
        var a = svc.For(new DateOnly(2026, 10, 15), rnd, Vault).Inspiration;
        Assert.Equal(a, svc.For(new DateOnly(2026, 10, 15), rnd, Vault).Inspiration);
        Assert.NotEqual(a, svc.For(new DateOnly(2027, 10, 15), rnd, Vault).Inspiration);
        Assert.NotEqual(a, svc.For(new DateOnly(2026, 10, 15), rnd, Guid.NewGuid()).Inspiration);
    }

    [Fact(DisplayName = "TC-INSP-07 Same date and settings always give the same verse and quote")]
    public void Deterministic()
    {
        var svc = TestData.Inspiration();
        var first = svc.For(TestData.Today, new InspirationSettings(), Vault).Inspiration;
        for (int i = 0; i < 100; i++) Assert.Equal(first, svc.For(TestData.Today, new InspirationSettings(), Vault).Inspiration);
    }

    [Fact(DisplayName = "TC-INSP-08 Shuffle never returns the current items")]
    public void Shuffle()
    {
        var svc = TestData.Inspiration();
        var current = svc.For(TestData.Today, new InspirationSettings(), Vault).Inspiration;
        for (int i = 0; i < 20; i++)
        {
            var next = svc.Shuffle(current, new InspirationSettings(), new Random(i));
            Assert.NotEqual(current.Verse, next.Verse);
            Assert.NotEqual(current.Quote, next.Quote);
            current = next;
        }
    }

    [Fact(DisplayName = "TC-INSP-04 Turning the verse or quote off hides it")]
    public void Visibility()
    {
        var svc = TestData.Inspiration();
        var r = svc.For(TestData.Today, new InspirationSettings { ShowVerse = false }, Vault).Inspiration;
        Assert.Null(r.Verse);
        Assert.NotNull(r.Quote);
        var stored = svc.For(TestData.Today, new InspirationSettings(), Vault).Inspiration;
        Assert.Null(DailyInspirationService.ApplyVisibility(stored, new InspirationSettings { ShowQuote = false }).Quote);
    }

    [Fact(DisplayName = "TC-INSP-11/12 Import verses from CSV and quotes from JSON; report skipped rows")]
    public void Import()
    {
        var csv = "reference,text,translation\nJohn 3:16,\"For God so loved the world, that he gave\",NRSV\nPsalm 1:1,,NRSV\n\"Romans 8:28\",All things work together,NRSV\n";
        var verses = ListImporter.ImportVerses(csv, "v.csv");
        Assert.Equal(2, verses.Items.Count);
        Assert.Equal(1, verses.Skipped);
        Assert.Equal("Imported 2, skipped 1.", verses.Summary);
        Assert.Equal("For God so loved the world, that he gave", verses.Items[0].Text);

        var json = "[{\"text\":\"Know thyself\",\"author\":\"Socrates\",\"tradition\":\"Greek\"},{\"text\":\"\",\"author\":\"x\"}]";
        var quotes = ListImporter.ImportQuotes(json, "q.json");
        Assert.Single(quotes.Items);
        Assert.Equal(1, quotes.Skipped);
        Assert.Equal(QuoteCategories.Imported, quotes.Items[0].Category);
        var withCategory = ListImporter.ImportQuotes("text,author,work,tradition,category\nAll that glisters,Shakespeare,,English,Literature\n", "q.csv");
        Assert.Equal(QuoteCategories.Literature, withCategory.Items[0].Category);

        Assert.Throws<ImportException>(() => ListImporter.ImportVerses("reference,text\n,\n", "bad.csv"));
    }
}

public class SecurityTests
{
    [Fact(DisplayName = "TC-SEC-06 Recovery codes: 5x5 Crockford base32, unique, round-trip")]
    public void RecoveryCodes()
    {
        var seen = new HashSet<string>();
        var pattern = new System.Text.RegularExpressions.Regex("^[0-9A-HJKMNP-TV-Z]{5}(-[0-9A-HJKMNP-TV-Z]{5}){4}$");
        for (int i = 0; i < 1000; i++)
        {
            var c = RecoveryCode.Generate();
            Assert.Matches(pattern, c.Formatted);
            Assert.True(seen.Add(c.Normalized));
            Assert.True(RecoveryCode.TryParse(c.Formatted, out var back));
            Assert.Equal(c, back);
        }
    }

    [Fact(DisplayName = "TC-SEC-10 Recovery code input ignores case, spaces, dashes; O=0, I/L=1")]
    public void LenientParsing()
    {
        Assert.True(RecoveryCode.TryParse("01ABC-DEFGH-JKMNP-QRSTV-WXYZ0", out var code));
        Assert.True(RecoveryCode.TryParse("oiabc defgh jkmnp qrstv wxyzo", out var lenient));
        Assert.Equal(code, lenient);
        Assert.True(RecoveryCode.TryParse("0LABCDEFGHJKMNPQRSTVWXYZ0", out var noDashes));
        Assert.Equal(code, noDashes);
        Assert.False(RecoveryCode.TryParse("too-short", out _));
        Assert.False(RecoveryCode.TryParse("UUUUU-DEFGH-JKMNP-QRSTV-WXYZ0", out _)); // U is not in the alphabet
    }

    [Fact(DisplayName = "TC-SEC-13 Auto-lock after the idle timeout; activity resets; Never never locks")]
    public void AutoLock()
    {
        var clock = new FakeClock();
        var policy = new AutoLockPolicy(clock) { Timeout = TimeSpan.FromMinutes(10) };
        int locks = 0;
        policy.LockRequested += (_, _) => locks++;
        policy.OnActivity();
        clock.Advance(TimeSpan.FromMinutes(5));
        policy.OnActivity();
        clock.Advance(TimeSpan.FromMinutes(9) + TimeSpan.FromSeconds(59));
        policy.Tick();
        Assert.Equal(0, locks);
        clock.Advance(TimeSpan.FromSeconds(1));
        policy.Tick();
        policy.Tick();
        Assert.Equal(1, locks);

        policy.Timeout = null;
        policy.OnActivity();
        clock.Advance(TimeSpan.FromHours(24));
        policy.Tick();
        Assert.Equal(1, locks);
    }

    [Fact(DisplayName = "TC-SEC-11 After 5 wrong codes, attempts wait 30 seconds")]
    public void Throttle()
    {
        var clock = new FakeClock();
        var t = new UnlockThrottle(clock);
        for (int i = 0; i < 5; i++)
        {
            Assert.True(t.CanAttempt(out _));
            t.RecordFailure();
        }
        Assert.False(t.CanAttempt(out var wait));
        Assert.Equal(30, wait.TotalSeconds, 0);
        clock.Advance(TimeSpan.FromSeconds(30));
        Assert.True(t.CanAttempt(out _));
    }

    [Fact(DisplayName = "TC-SEC-17 SecretKey is zeroed on dispose")]
    public void SecretKeyZeroed()
    {
        var key = SecretKey.Generate();
        Assert.False(key.IsZeroed());
        key.Dispose();
        Assert.True(key.IsZeroed());
        Assert.Throws<ObjectDisposedException>(() => key.Span.Length);
    }

    private static (VaultSession Session, InMemoryHeaderStore Store, FakeKeyProtector Hello) NewSession(FakeMachine? machine = null)
    {
        machine ??= new FakeMachine();
        var store = new InMemoryHeaderStore();
        var hello = new FakeKeyProtector(KeySlotTypes.WindowsHello) { Machine = machine };
        var session = new VaultSession(store, [hello, new FakeKeyProtector(KeySlotTypes.RecoveryCode)], machine, new FakeClock());
        return (session, store, hello);
    }

    [Fact(DisplayName = "TC-SEC-08 Unlock with Windows Hello after creating a vault")]
    public async Task CreateAndUnlockWithHello()
    {
        var (session, store, _) = NewSession();
        var result = await session.CreateVaultAsync(useHello: true);
        Assert.Equal(HelloProtection.Strong, result.Hello);
        var key = session.DataKey.Span.ToArray();
        await session.LockAsync(LockReason.User);
        Assert.False(session.IsUnlocked);

        var (again, _, _) = NewSession();
        var reopened = new VaultSession(store, [new FakeKeyProtector(KeySlotTypes.WindowsHello), new FakeKeyProtector(KeySlotTypes.RecoveryCode)], new FakeMachine(), new FakeClock());
        await reopened.UnlockWithHelloAsync();
        Assert.Equal(key, reopened.DataKey.Span.ToArray());
    }

    [Fact(DisplayName = "TC-SEC-09 Hello cancelled or unavailable keeps the vault locked")]
    public async Task HelloFailures()
    {
        var (session, store, hello) = NewSession();
        await session.CreateVaultAsync(useHello: true);
        await session.LockAsync(LockReason.User);
        hello.FailWith = UnlockFailure.Cancelled;
        var e = await Assert.ThrowsAsync<UnlockFailedException>(() => session.UnlockWithHelloAsync());
        Assert.Equal(UnlockFailure.Cancelled, e.Reason);
        Assert.False(session.IsUnlocked);

        var other = new VaultSession(store, [hello], new FakeMachine("OTHER-PC"), new FakeClock());
        var na = await Assert.ThrowsAsync<UnlockFailedException>(() => other.UnlockWithHelloAsync());
        Assert.Equal(UnlockFailure.NotAvailable, na.Reason);
    }

    [Fact(DisplayName = "TC-SEC-05 No Windows Hello: vault has only a recovery slot")]
    public async Task NoHello()
    {
        var (session, store, hello) = NewSession();
        hello.Available = false;
        var r = await session.CreateVaultAsync(useHello: true);
        Assert.Equal(HelloProtection.None, r.Hello);
        Assert.Equal([KeySlotTypes.RecoveryCode], store.Header!.KeySlots.Select(s => s.Type));
    }

    [Fact(DisplayName = "TC-SEC-11/18 Wrong code throttled; regenerated code replaces the old one")]
    public async Task RecoveryCodeFlow()
    {
        var (session, _, _) = NewSession();
        var setup = await session.CreateVaultAsync(useHello: false);
        await session.LockAsync(LockReason.User);
        var wrong = RecoveryCode.Generate();
        var e = await Assert.ThrowsAsync<UnlockFailedException>(() => session.UnlockWithRecoveryCodeAsync(wrong));
        Assert.Equal("That recovery code didn't work.", e.Message);
        await session.UnlockWithRecoveryCodeAsync(setup.RecoveryCode);

        var newCode = await session.RegenerateRecoveryCodeAsync();
        await session.LockAsync(LockReason.User);
        await Assert.ThrowsAsync<UnlockFailedException>(() => session.UnlockWithRecoveryCodeAsync(setup.RecoveryCode));
        await session.UnlockWithRecoveryCodeAsync(newCode);
        Assert.True(session.IsUnlocked);
    }

    [Fact(DisplayName = "TC-SEC-12/18 Enroll Hello on another PC; remove another PC's slot")]
    public async Task EnrollAndRemoveMachines()
    {
        var machine = new FakeMachine("FIRST-PC");
        var (session, store, hello) = NewSession(machine);
        var setup = await session.CreateVaultAsync(useHello: true);
        await session.LockAsync(LockReason.User);

        machine.MachineName = "SECOND-PC";
        Assert.False(session.HasHelloSlotForThisMachine);
        await session.UnlockWithRecoveryCodeAsync(setup.RecoveryCode);
        await session.EnrollHelloAsync();
        Assert.True(session.HasHelloSlotForThisMachine);
        Assert.Equal(["FIRST-PC", "SECOND-PC"], session.HelloMachines().Order());

        await session.RemoveHelloSlotAsync("FIRST-PC");
        Assert.Equal(["SECOND-PC"], session.HelloMachines());
    }

    [Fact(DisplayName = "TC-SEC-16 Before-lock handlers run before the key is wiped")]
    public async Task BeforeLockRuns()
    {
        var (session, _, _) = NewSession();
        await session.CreateVaultAsync(useHello: false);
        bool keyWasAvailable = false;
        var key = session.DataKey;
        session.RegisterBeforeLock(_ =>
        {
            keyWasAvailable = session.IsUnlocked && !key.IsDisposed;
            return Task.CompletedTask;
        });
        LockReason? locked = null;
        session.Locked += (_, r) => locked = r;
        await session.LockAsync(LockReason.Idle);
        Assert.True(keyWasAvailable);
        Assert.True(key.IsZeroed());
        Assert.Equal(LockReason.Idle, locked);
    }
}

public class CalendarGridTests
{
    [Fact(DisplayName = "TC-CAL-02 Month grids have 42 days starting on the configured weekday")]
    public void Grids()
    {
        var oct = MonthGrid.Build(2026, 10, DayOfWeek.Sunday);
        Assert.Equal(42, oct.Count);
        Assert.Equal(new DateOnly(2026, 9, 27), oct[0]);
        Assert.Equal(new DateOnly(2026, 2, 1), MonthGrid.Build(2026, 2, DayOfWeek.Sunday)[0]);
        Assert.Equal(new DateOnly(2026, 1, 26), MonthGrid.Build(2026, 2, DayOfWeek.Monday)[0]);
        Assert.Equal(DayOfWeek.Monday, MonthGrid.WeekdayOrder(DayOfWeek.Monday)[0]);
        Assert.All(MonthGrid.Build(2026, 3, DayOfWeek.Sunday), d => Assert.True(d >= new DateOnly(2026, 2, 22)));
    }
}

public class ArchitectureTests
{
    [Fact(DisplayName = "TC-NFR-20 Journal.Core has no UI, WinRT or infrastructure references")]
    public void CoreHasNoForbiddenReferences()
    {
        var refs = typeof(JournalEntry).Assembly.GetReferencedAssemblies().Select(a => a.Name).ToList();
        Assert.DoesNotContain("PresentationFramework", refs);
        Assert.DoesNotContain("WindowsBase", refs);
        Assert.DoesNotContain("Microsoft.Windows.SDK.NET", refs);
        Assert.DoesNotContain("Journal.Infrastructure", refs);
    }
}
