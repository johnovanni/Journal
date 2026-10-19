using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using Journal.Core.Documents;
using Journal.Core.Domain;
using Journal.Core.Persistence;
using Journal.Core.Security;
using Journal.Core.Tests.Support;
using Journal.Infrastructure.Content;
using Journal.Infrastructure.Crypto;
using Journal.Infrastructure.Hello;
using Journal.Infrastructure.Logging;
using Journal.Infrastructure.Storage;
using Microsoft.Extensions.Logging;

namespace Journal.Infrastructure.Tests;

public class CryptoTests
{
    private static VaultCrypto NewCrypto(Guid? vault = null) => new(SecretKey.Generate(), vault ?? Guid.NewGuid());

    [Fact(DisplayName = "TC-NFR-01 Same plaintext encrypts differently every time with unique 12-byte nonces")]
    public void UniqueNonces()
    {
        using var crypto = NewCrypto();
        var plain = "same text"u8.ToArray();
        var nonces = new HashSet<string>();
        for (int i = 0; i < 10_000; i++)
        {
            var sealedData = crypto.Seal(FileKind.Entry, "id", plain);
            Assert.True(nonces.Add(Convert.ToHexString(sealedData, VaultCrypto.HeaderLength, 12)));
            if (i % 1000 == 0) Assert.Equal(plain, crypto.Open(FileKind.Entry, "id", sealedData));
        }
    }

    [Fact(DisplayName = "TC-NFR-04/05 Tampering, wrong id, wrong kind and wrong key are all rejected")]
    public void TamperDetection()
    {
        var vault = Guid.NewGuid();
        using var crypto = NewCrypto(vault);
        var sealedData = crypto.Seal(FileKind.Entry, "a", "secret"u8);
        var flipped = (byte[])sealedData.Clone();
        flipped[VaultCrypto.HeaderLength + 13] ^= 1;
        Assert.ThrowsAny<CryptographicException>(() => crypto.Open(FileKind.Entry, "a", flipped));
        var tag = (byte[])sealedData.Clone();
        tag[^1] ^= 1;
        Assert.ThrowsAny<CryptographicException>(() => crypto.Open(FileKind.Entry, "a", tag));
        Assert.ThrowsAny<CryptographicException>(() => crypto.Open(FileKind.Entry, "b", sealedData));
        Assert.ThrowsAny<CryptographicException>(() => crypto.Open(FileKind.Catalog, "a", sealedData));
        using var otherVault = NewCrypto(vault);
        Assert.ThrowsAny<CryptographicException>(() => otherVault.Open(FileKind.Entry, "a", sealedData));
    }

    [Fact(DisplayName = "Key wrapping round trip; wrong KEK or AAD fails")]
    public void KeyWrap()
    {
        using var kek = SecretKey.Generate();
        using var key = SecretKey.Generate();
        var wrapped = KeyWrapper.Wrap(kek, key, [1, 2]);
        using var back = KeyWrapper.Unwrap(kek, wrapped, [1, 2]);
        Assert.Equal(key.Span.ToArray(), back.Span.ToArray());
        using var wrong = SecretKey.Generate();
        Assert.ThrowsAny<CryptographicException>(() => KeyWrapper.Unwrap(wrong, wrapped, [1, 2]));
        Assert.ThrowsAny<CryptographicException>(() => KeyWrapper.Unwrap(kek, wrapped, [1, 3]));
    }

    [Theory(DisplayName = "Chunked stream encryption round trip at chunk boundaries")]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(VaultCrypto.ChunkSize)]
    [InlineData(VaultCrypto.ChunkSize + 1)]
    [InlineData(3 * VaultCrypto.ChunkSize - 7)]
    public async Task StreamRoundTrip(int size)
    {
        using var crypto = NewCrypto();
        var data = RandomNumberGenerator.GetBytes(size);
        var path = Path.GetTempFileName();
        try
        {
            string sha;
            await using (var fs = File.Create(path)) (sha, _) = await crypto.EncryptStreamAsync(new MemoryStream(data), fs, "blob");
            Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(data)), sha);
            using var output = new MemoryStream();
            await using (var fs = File.OpenRead(path)) await crypto.DecryptStreamAsync(fs, output, "blob");
            Assert.Equal(data, output.ToArray());
        }
        finally { File.Delete(path); }
    }

    [Fact(DisplayName = "Truncated chunked blobs are rejected")]
    public async Task TruncatedBlob()
    {
        using var crypto = NewCrypto();
        var data = RandomNumberGenerator.GetBytes(VaultCrypto.ChunkSize * 2 + 10);
        using var enc = new MemoryStream();
        await crypto.EncryptStreamAsync(new MemoryStream(data), enc, "blob");
        var bytes = enc.ToArray();
        // Drop the last chunk: the previous chunk was not encrypted as final.
        int lastChunk = 12 + 4 + 10 + 16;
        using var truncated = new MemoryStream(bytes[..^lastChunk]);
        await Assert.ThrowsAnyAsync<CryptographicException>(() => crypto.DecryptStreamAsync(truncated, new MemoryStream(), "blob"));
    }
}

public class VaultTests
{
    [Fact(DisplayName = "TC-EDT-14 A document with every feature survives save and reload through the encrypted vault")]
    public async Task FullDocumentRoundTrip()
    {
        await using var v = await TempVault.CreateAsync();
        var e = (await v.Journal.OpenDayAsync(TestData.Today)).Entry!;
        var img = await v.Journal.AddBlobAsync(new MemoryStream([1, 2, 3]));
        e.DefaultTab.Document.Blocks.AddRange([
            new ParagraphBlock(new TextRun("Title")) { Style = ParagraphStyle.Heading1 },
            new ParagraphBlock(new TextRun("bold", RunFormat.Bold), new TextRun(" red", RunFormat.Strikethrough) { Foreground = ArgbColor.Parse("#C00000"), FontFamily = "Georgia", FontSize = 14 }),
            new ParagraphBlock(new TextRun("item")) { List = ListKind.Numbered, ListLevel = 1 },
            new ParagraphBlock(new TextRun("todo")) { List = ListKind.Checklist, IsChecked = true },
            new ImageBlock { Image = img, Caption = "Garden at sunrise", DisplayWidth = 240 },
        ]);
        e.AddTab(Tag.Parse("family")).Document.Blocks.Add(ParagraphBlock.OfText("kids"));
        await v.Journal.SaveAsync(e);
        var before = JournalJson.SerializeDocument(e.DefaultTab.Document);
        await v.ReopenAsync();
        var back = (await v.Journal.OpenDayAsync(TestData.Today)).Entry!;
        Assert.Equal(before, JournalJson.SerializeDocument(back.DefaultTab.Document));
        Assert.Equal("kids", back.Tabs[1].Document.ExtractPlainText());
    }

    [Fact(DisplayName = "TC-IMG-07 Stored image bytes are the original, and the blob file is encrypted")]
    public async Task ImageBlobIsOriginalAndEncrypted()
    {
        await using var v = await TempVault.CreateAsync();
        byte[] jpeg = [0xFF, 0xD8, 0xFF, 0xE0, .. RandomNumberGenerator.GetBytes(5000)];
        var blob = await v.Journal.AddBlobAsync(new MemoryStream(jpeg));
        Assert.Equal(jpeg, await v.Journal.ReadBlobAsync(blob));
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(jpeg)), blob.Sha256);
        var file = v.Services.Blobs.PathFor(blob.Id);
        Assert.Equal(-1, TempVault.IndexOf(File.ReadAllBytes(file), [0xFF, 0xD8, 0xFF, 0xE0]));
    }

    [Fact(DisplayName = "TC-NFR-02 No plaintext text, tags, captions or file content anywhere in the vault")]
    public async Task NoPlaintextOnDisk()
    {
        const string marker = "ZZQX-MARKER";
        await using var v = await TempVault.CreateAsync();
        var e = (await v.Journal.OpenDayAsync(TestData.Today)).Entry!;
        e.DefaultTab.Document.Blocks.Add(ParagraphBlock.OfText($"Body {marker}"));
        var file = await v.Journal.AddBlobAsync(new MemoryStream(System.Text.Encoding.UTF8.GetBytes($"file {marker}")));
        e.DefaultTab.Document.Blocks.Add(new ImageBlock { Image = file, Caption = $"caption {marker}" });
        e.DefaultTab.Document.Blocks.Add(new AttachmentBlock { File = file, FileName = $"{marker}.txt", Description = marker });
        e.AddTag(Tag.Parse(marker));
        await v.Journal.SaveAsync(e);
        await v.Services.ImportQuotesAsync([new Quote(marker, "A", null, "T")]);
        Assert.False(v.ContainsPlaintext(marker));
        Assert.False(v.ContainsPlaintext("Body"));
    }

    [Fact(DisplayName = "TC-MOOD-02 A day's mood is stored encrypted and survives reopening")]
    public async Task MoodEncrypted()
    {
        await using var v = await TempVault.CreateAsync();
        var e = (await v.Journal.OpenDayAsync(TestData.Today)).Entry!;
        e.SetMood(Mood.Happy, TestData.Today);
        await v.Journal.SaveAsync(e);
        Assert.False(v.ContainsPlaintext("happy"));
        Assert.False(v.ContainsPlaintext("mood"));
        Assert.Equal(Mood.Happy, (await v.Journal.OpenDayAsync(TestData.Today)).Entry!.Mood);
    }

    [Fact(DisplayName = "TC-NFR-03 The raw data key never appears on disk")]
    public async Task DataKeyNotOnDisk()
    {
        await using var v = await TempVault.CreateAsync();
        await v.Journal.SaveAsync(TestData.Entry(TestData.Today, "x"));
        var key = v.Session.DataKey.Span.ToArray();
        Assert.All(v.AllFiles(), f => Assert.Equal(-1, TempVault.IndexOf(File.ReadAllBytes(f), key)));
        var header = File.ReadAllText(Path.Combine(v.Folder, "vault.json"));
        Assert.DoesNotContain(Convert.ToBase64String(key), header);
    }

    [Fact(DisplayName = "TC-NFR-06 Entry file names don't reveal dates and differ between vaults")]
    public async Task FileNamesHideDates()
    {
        await using var v = await TempVault.CreateAsync();
        await using var w = await TempVault.CreateAsync();
        await v.Journal.SaveAsync(TestData.Entry(TestData.Today, "x"));
        var name = Path.GetFileNameWithoutExtension(Assert.Single(Directory.GetFiles(Path.Combine(v.Folder, "entries"))));
        Assert.Matches("^[0-9a-f]{64}$", name);
        Assert.DoesNotContain("2026", name);
        Assert.DoesNotContain("1015", name);
        Assert.Equal(name, Path.GetFileNameWithoutExtension(v.Services.Entries.PathFor(TestData.Today)));
        Assert.NotEqual(name, Path.GetFileNameWithoutExtension(w.Services.Entries.PathFor(TestData.Today)));
    }

    [Fact(DisplayName = "TC-NFR-04 An entry file renamed to another date's name is rejected")]
    public async Task SwappedEntryRejected()
    {
        await using var v = await TempVault.CreateAsync();
        await v.Journal.SaveAsync(TestData.Entry(new DateOnly(2026, 10, 13), "thirteenth"));
        var from = v.Services.Entries.PathFor(new DateOnly(2026, 10, 13));
        var to = v.Services.Entries.PathFor(new DateOnly(2026, 10, 14));
        File.Copy(from, to);
        var load = await v.Journal.OpenDayAsync(new DateOnly(2026, 10, 14));
        Assert.True(load.IsUnreadable);
    }

    [Fact(DisplayName = "TC-ENT-09 A damaged entry is reported, left unchanged, and other days still open")]
    public async Task DamagedEntry()
    {
        await using var v = await TempVault.CreateAsync();
        await v.Journal.SaveAsync(TestData.Entry(TestData.Today, "today"));
        await v.Journal.SaveAsync(TestData.Entry(TestData.Today.AddDays(-1), "yesterday"));
        var path = v.Services.Entries.PathFor(TestData.Today);
        var bytes = File.ReadAllBytes(path);
        bytes[40] ^= 0xFF;
        File.WriteAllBytes(path, bytes);
        var load = await v.Journal.OpenDayAsync(TestData.Today);
        Assert.True(load.IsUnreadable);
        Assert.Equal(bytes, File.ReadAllBytes(path));
        Assert.Equal("yesterday", (await v.Journal.OpenDayAsync(TestData.Today.AddDays(-1))).Entry!.DefaultTab.Document.ExtractPlainText());
    }

    [Fact(DisplayName = "TC-NFR-07 Recovery slot uses PBKDF2-SHA256, ≥600,000 iterations, 16-byte random salt")]
    public async Task RecoverySlotParameters()
    {
        await using var v = await TempVault.CreateAsync(iterations: Crypto.KeyDerivation.RecoveryIterations);
        await using var w = await TempVault.CreateAsync(iterations: Crypto.KeyDerivation.RecoveryIterations);
        var slot = v.Session.Header!.SlotsOfType(KeySlotTypes.RecoveryCode).Single();
        Assert.Equal("PBKDF2-SHA256", slot.Kdf);
        Assert.True(slot.Iterations >= 600_000);
        Assert.Equal(16, slot.Salt!.Length);
        Assert.NotEqual(slot.Salt, w.Session.Header!.SlotsOfType(KeySlotTypes.RecoveryCode).Single().Salt);
        Assert.Equal(600_000, new RecoveryCodeKeyProtector().ProtectAsync(SecretKey.Generate(), v.Session.Header, new UnlockContext(RecoveryCode.Generate())).Result.Iterations);
    }

    [Fact(DisplayName = "TC-NFR-10 Interrupted writes leave the previous version intact and no temp files after unlock")]
    public async Task AtomicWrites()
    {
        await using var v = await TempVault.CreateAsync();
        await v.Journal.SaveAsync(TestData.Entry(TestData.Today, "version one"));
        v.Writer.BeforeRename = _ => throw new IOException("power cut");
        await Assert.ThrowsAsync<IOException>(() => v.Journal.SaveAsync(TestData.Entry(TestData.Today, "version two")));
        v.Writer.BeforeRename = null;
        File.WriteAllText(Path.Combine(v.Folder, "entries", "leftover.jent.tmp"), "partial");
        await v.ReopenAsync();
        Assert.Equal("version one", (await v.Journal.OpenDayAsync(TestData.Today)).Entry!.DefaultTab.Document.ExtractPlainText());
        Assert.Empty(Directory.GetFiles(v.Folder, "*.tmp", SearchOption.AllDirectories));
    }

    [Fact(DisplayName = "TC-NFR-11 Catalog is rebuilt after deletion or damage, with the same search results")]
    public async Task CatalogRebuild()
    {
        await using var v = await TempVault.CreateAsync();
        await v.Journal.SaveAsync(TestData.Entry(TestData.Today, "garden party", "family"));
        var catalogPath = Path.Combine(v.Folder, "catalog.jcat");
        File.Delete(catalogPath);
        await v.ReopenAsync();
        Assert.Single(v.Journal.Search.Search("tag:family garden").Hits);
        File.WriteAllBytes(catalogPath, [1, 2, 3, 4]);
        await v.ReopenAsync();
        Assert.True(v.Journal.HasEntry(TestData.Today));
    }

    [Fact(DisplayName = "TC-ATT-08 / NFR-PERF-05 A 200 MB file streams through encryption with bounded memory")]
    public async Task LargeAttachmentStreams()
    {
        await using var v = await TempVault.CreateAsync();
        var source = Path.Combine(v.Folder, "big.bin");
        const long size = 200L * 1024 * 1024;
        await using (var fs = File.Create(source))
        {
            var chunk = RandomNumberGenerator.GetBytes(1024 * 1024);
            for (int i = 0; i < size / chunk.Length; i++) await fs.WriteAsync(chunk);
        }
        GC.Collect();
        long before = GC.GetTotalMemory(true);
        long peak = 0;
        using var timer = new Timer(_ => peak = Math.Max(peak, GC.GetTotalMemory(false)), null, 0, 20);
        BlobRef blob;
        await using (var fs = File.OpenRead(source)) blob = await v.Journal.AddBlobAsync(fs);
        var target = Path.Combine(v.Folder, "out.bin");
        await using (var fs = File.Create(target)) await v.Journal.CopyBlobToAsync(blob, fs);
        Assert.True(peak - before < 64L * 1024 * 1024, $"extra memory {(peak - before) / 1024 / 1024} MB");
        await using var a = File.OpenRead(source);
        await using var b = File.OpenRead(target);
        Assert.Equal(SHA256.HashData(a), SHA256.HashData(b));
    }

    [Fact(DisplayName = "TC-NFR-21 Every persisted JSON file carries formatVersion")]
    public async Task FormatVersions()
    {
        await using var v = await TempVault.CreateAsync();
        await v.Journal.SaveAsync(TestData.Entry(TestData.Today, "x"));
        Assert.Contains("\"formatVersion\"", File.ReadAllText(Path.Combine(v.Folder, "vault.json")));
        var crypto = new VaultCrypto(v.Session.DataKey, v.Session.Header!.VaultId);
        var entryPath = v.Services.Entries.PathFor(TestData.Today);
        var json = crypto.Open(FileKind.Entry, Path.GetFileNameWithoutExtension(entryPath), File.ReadAllBytes(entryPath));
        Assert.Contains("\"formatVersion\"", System.Text.Encoding.UTF8.GetString(json));
        var catalog = crypto.Open(FileKind.Catalog, "catalog", File.ReadAllBytes(Path.Combine(v.Folder, "catalog.jcat")));
        Assert.Contains("\"formatVersion\"", System.Text.Encoding.UTF8.GetString(catalog));
        var settings = new JsonSettingsStore(Path.Combine(v.Folder, "s.json"));
        settings.Save(new Core.Settings.AppSettings());
        Assert.Contains("\"formatVersion\"", File.ReadAllText(settings.Path));
    }

    [PerfFact(DisplayName = "TC-NFR-13 Unlock to ready with 3,650 entries in ≤ 2 s")]
    public async Task UnlockPerformance()
    {
        await using var v = await TempVault.CreateAsync();
        for (int i = 0; i < 3650; i++)
            await v.Journal.SaveAsync(TestData.Entry(new DateOnly(2016, 1, 1).AddDays(i), $"Day {i} " + string.Concat(Enumerable.Repeat("words of the day ", 60))));
        var times = new List<double>();
        for (int r = 0; r < 5; r++)
        {
            v.Services.Dispose();
            await v.Session.LockAsync(LockReason.User);
            var sw = Stopwatch.StartNew();
            await v.Session.UnlockWithHelloAsync();
            var services = await VaultServices.OpenAsync(v.Session, v.Folder, v.Clock, () => v.Inspiration);
            times.Add(sw.Elapsed.TotalSeconds);
            services.Dispose();
            await v.Session.LockAsync(LockReason.User);
            await v.Session.UnlockWithHelloAsync();
            typeof(TempVault).GetProperty("Services")!.SetValue(v, await VaultServices.OpenAsync(v.Session, v.Folder, v.Clock, () => v.Inspiration));
        }
        Assert.True(times.Order().ElementAt(2) <= 2.0, $"median {times.Order().ElementAt(2):0.00} s");
    }
}

/// <summary>Performance tests run only when JOURNAL_PERF=1 (use a Release build).</summary>
public sealed class PerfFactAttribute : FactAttribute
{
    public PerfFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("JOURNAL_PERF") != "1")
            Skip = "Performance test: set JOURNAL_PERF=1 and run in Release.";
    }
}

public class KeyProtectorTests
{
    [Fact(DisplayName = "TC-SEC-03 Setup signs the challenge twice and creates a Hello slot")]
    public async Task HelloSlotCreated()
    {
        await using var v = await TempVault.CreateAsync();
        Assert.Equal(2, v.Hello.SignCalls.Count);
        Assert.Equal(v.Hello.SignCalls[0].Data, v.Hello.SignCalls[1].Data);
        Assert.Contains(v.Session.Header!.KeySlots, s => s.Type == KeySlotTypes.WindowsHello && s.Machine == "TEST-PC");
    }

    [Fact(DisplayName = "TC-SEC-04 Non-deterministic signatures fall back to the DPAPI slot")]
    public async Task FallbackToDpapi()
    {
        var v = new TempVault();
        v.Hello.Deterministic = false;
        var session = v.NewSession();
        var result = await session.CreateVaultAsync(useHello: true);
        Assert.Equal(HelloProtection.Weaker, result.Hello);
        Assert.DoesNotContain(session.Header!.KeySlots, s => s.Type == KeySlotTypes.WindowsHello);
        Assert.Contains(session.Header!.KeySlots, s => s.Type == KeySlotTypes.HelloDpapi);
        var key = session.DataKey.Span.ToArray();
        await session.LockAsync(LockReason.User);
        await session.UnlockWithHelloAsync();
        Assert.Equal(key, session.DataKey.Span.ToArray());
        v.Consent.Verified = false;
        await session.LockAsync(LockReason.User);
        var e = await Assert.ThrowsAsync<UnlockFailedException>(() => session.UnlockWithHelloAsync());
        Assert.Equal(UnlockFailure.Cancelled, e.Reason);
        Directory.Delete(v.Folder, true);
    }

    [Fact(DisplayName = "TC-SEC-08 Windows Hello unlock opens the same entries")]
    public async Task HelloUnlock()
    {
        await using var v = await TempVault.CreateAsync();
        for (int i = 0; i < 3; i++) await v.Journal.SaveAsync(TestData.Entry(TestData.Today.AddDays(-i), $"entry {i}"));
        v.Services.Dispose();
        await v.Session.LockAsync(LockReason.User);
        var session = v.NewSession();
        await session.UnlockWithHelloAsync();
        using var services = await VaultServices.OpenAsync(session, v.Folder, v.Clock, () => v.Inspiration);
        Assert.Equal(3, services.Journal.Catalog.Count);
        Assert.Equal("entry 2", (await services.Journal.OpenDayAsync(TestData.Today.AddDays(-2))).Entry!.DefaultTab.Document.ExtractPlainText());
    }

    [Fact(DisplayName = "TC-SEC-10 Real recovery code unlock with lenient input")]
    public async Task RecoveryUnlock()
    {
        await using var v = await TempVault.CreateAsync();
        await v.Session.LockAsync(LockReason.User);
        var session = v.NewSession();
        var typed = v.Code.Normalized.ToLowerInvariant().Replace('0', 'o').Replace('1', 'l');
        Assert.True(RecoveryCode.TryParse(typed, out var code));
        await session.UnlockWithRecoveryCodeAsync(code);
        Assert.True(session.IsUnlocked);
    }

    [Fact(DisplayName = "TC-SEC-18 Regenerating the code keeps journal content readable")]
    public async Task RegenerateKeepsContent()
    {
        await using var v = await TempVault.CreateAsync();
        await v.Journal.SaveAsync(TestData.Entry(TestData.Today, "kept"));
        var newCode = await v.Session.RegenerateRecoveryCodeAsync();
        v.Services.Dispose();
        await v.Session.LockAsync(LockReason.User);
        var session = v.NewSession();
        await Assert.ThrowsAsync<UnlockFailedException>(() => session.UnlockWithRecoveryCodeAsync(v.Code));
        await session.UnlockWithRecoveryCodeAsync(newCode);
        using var services = await VaultServices.OpenAsync(session, v.Folder, v.Clock, () => v.Inspiration);
        Assert.Equal("kept", (await services.Journal.OpenDayAsync(TestData.Today)).Entry!.DefaultTab.Document.ExtractPlainText());
    }
}

public class ContentTests
{
    [Fact(DisplayName = "TC-INSP-02 Built-in verses: ≥366 distinct WEB verses with valid references")]
    public void Verses()
    {
        var verses = new EmbeddedVerseSource().All();
        Assert.True(verses.Count >= 366, $"{verses.Count} verses");
        Assert.Equal(verses.Count, verses.Select(v => v.Reference).Distinct().Count());
        Assert.All(verses, v =>
        {
            Assert.Equal("WEB", v.Translation);
            Assert.False(string.IsNullOrWhiteSpace(v.Text));
            Assert.Matches(@"^(\d )?[A-Z][A-Za-z ]+ \d+:\d+(-\d+)?$", v.Reference);
        });
    }

    [Fact(DisplayName = "TC-INSP-13 Every built-in verse is a complete passage (starts and ends a sentence)")]
    public void VersesAreComplete()
    {
        var verses = new EmbeddedVerseSource().All();
        Assert.All(verses, v =>
        {
            var firstLetter = v.Text.First(char.IsLetter);
            Assert.True(char.IsUpper(firstLetter), $"{v.Reference} starts mid-sentence: {v.Text[..Math.Min(40, v.Text.Length)]}");
            Assert.True(v.Text.TrimEnd().EndsWith('.') || v.Text.TrimEnd().EndsWith('!') || v.Text.TrimEnd().EndsWith('?') ||
                        v.Text.TrimEnd().EndsWith('”') || v.Text.TrimEnd().EndsWith('’') || v.Text.TrimEnd().EndsWith(')'),
                $"{v.Reference} ends mid-sentence: …{v.Text[^Math.Min(40, v.Text.Length)..]}");
        });
        Assert.StartsWith("Love is patient", verses.Single(v => v.Reference == "1 Corinthians 13:4-7").Text);
    }

    [Fact(DisplayName = "TC-INSP-03 Built-in quotes: ≥366 distinct, ≥20 authors, all with a tradition")]
    public void Quotes()
    {
        var quotes = new EmbeddedQuoteSource().All();
        Assert.True(quotes.Count >= 366, $"{quotes.Count} quotes");
        Assert.Equal(quotes.Count, quotes.Select(q => q.Text).Distinct().Count());
        Assert.True(quotes.Select(q => q.Author).Distinct().Count() >= 20);
        Assert.All(quotes, q => Assert.False(string.IsNullOrWhiteSpace(q.Tradition)));
    }

    [Fact(DisplayName = "TC-INSP-16 Built-in literature: ≥366 distinct quotes, ≥30 authors, ≥8 regions incl. Latin America and Africa")]
    public void LiteratureQuotes()
    {
        var all = new EmbeddedQuoteSource().All();
        Assert.Equal(["Literature", "Philosophy"], all.Select(q => q.Category).Distinct().Order());
        var lit = all.Where(q => q.Category == QuoteCategories.Literature).ToList();
        var phil = all.Where(q => q.Category == QuoteCategories.Philosophy).ToList();
        Assert.True(lit.Count >= 366, $"{lit.Count} literature quotes");
        Assert.Equal(394, phil.Count);
        Assert.Equal(lit.Count, lit.Select(q => q.Text).Distinct().Count());
        Assert.Empty(lit.Select(q => q.Text).Intersect(phil.Select(q => q.Text)));
        Assert.True(lit.Select(q => q.Author).Distinct().Count() >= 30);
        var regions = lit.Select(q => q.Tradition).Distinct().ToList();
        Assert.True(regions.Count >= 8, string.Join(", ", regions));
        Assert.Contains("Latin American", regions);
        Assert.Contains("African", regions);
        Assert.All(lit, q => Assert.False(string.IsNullOrWhiteSpace(q.Work), q.Text));
        // "By day of year" uses the first 366 quotes, so both categories must be among them.
        Assert.Equal(2, all.Take(366).Select(q => q.Category).Distinct().Count());
    }

    [Fact(DisplayName = "TC-NFR-23 Every built-in quote records a public-domain source edition")]
    public void QuoteSources()
    {
        Assert.All(new EmbeddedQuoteSource().All(), q => Assert.False(string.IsNullOrWhiteSpace(q.Source)));
        Assert.Contains("public domain", new EmbeddedVerseSource().Description, StringComparison.OrdinalIgnoreCase);
    }
}

public class FileServicesTests
{
    private static string NewDir() => Path.Combine(Path.GetTempPath(), "journal-tests", Guid.NewGuid().ToString("N"));

    [Fact(DisplayName = "TC-ATT-03 Temp copies go to a folder only the current user can access")]
    public void PrivateTempFolder()
    {
        var temp = new TempFileManager(NewDir());
        var path = temp.NewFilePath("data.xlsx");
        File.WriteAllText(path, "x");
        Assert.Equal("data.xlsx", Path.GetFileName(path));
        Assert.True(TempFileManager.IsPrivate(temp.SessionFolder));
        temp.CleanAll();
    }

    [Fact(DisplayName = "TC-ATT-06 Temp files are deleted by CleanAll (lock, exit, startup)")]
    public void TempCleanup()
    {
        var root = NewDir();
        var temp = new TempFileManager(root);
        for (int i = 0; i < 3; i++) File.WriteAllText(temp.NewFilePath($"f{i}.txt"), "x");
        Assert.Equal(0, temp.CleanAll());
        Assert.Empty(Directory.GetFiles(root, "*", SearchOption.AllDirectories));
        File.WriteAllText(new TempFileManager(root).NewFilePath("leftover.txt"), "x");
        new TempFileManager(root).CleanAll();
        Assert.Empty(Directory.GetFiles(root, "*", SearchOption.AllDirectories));
    }

    [Fact(DisplayName = "TC-SET-02 Settings persist between runs")]
    public void SettingsPersist()
    {
        var path = Path.Combine(NewDir(), "settings.json");
        var store = new JsonSettingsStore(path);
        var s = store.Load();
        s.AutoLockMinutes = 20;
        s.WeekStart = DayOfWeek.Monday;
        s.Inspiration.ShowQuote = false;
        store.Save(s);
        var back = new JsonSettingsStore(path).Load();
        Assert.Equal(20, back.AutoLockMinutes);
        Assert.Equal(DayOfWeek.Monday, back.WeekStart);
        Assert.False(back.Inspiration.ShowQuote);
    }

    [Fact(DisplayName = "TC-SET-03 Moving the vault copies and verifies; a failed copy leaves the old vault in place")]
    public async Task MoveVault()
    {
        await using var v = await TempVault.CreateAsync();
        for (int i = 0; i < 50; i++) await v.Journal.SaveAsync(TestData.Entry(TestData.Today.AddDays(-i), $"e{i}"));
        v.Services.Dispose();
        var target = NewDir();
        VaultMover.OnCopy = f => { if (f.EndsWith(".jcat")) throw new IOException("disk full"); };
        Assert.Throws<IOException>(() => VaultMover.Move(v.Folder, target));
        VaultMover.OnCopy = null;
        Assert.True(File.Exists(Path.Combine(v.Folder, "vault.json")));
        Assert.False(Directory.Exists(target));

        VaultMover.Move(v.Folder, target);
        Assert.False(Directory.Exists(v.Folder));
        await v.Session.LockAsync(LockReason.User);
        var session = new VaultSession(new JsonVaultHeaderStore(new VaultPaths(target), new AtomicFileWriter()),
            [new RecoveryCodeKeyProtector(1_000)], v.Machine, v.Clock);
        await session.UnlockWithRecoveryCodeAsync(v.Code);
        using var services = await VaultServices.OpenAsync(session, target, v.Clock, () => v.Inspiration);
        Assert.Equal(50, services.Journal.Catalog.Count);
        Directory.Delete(target, true);
    }

    [Fact(DisplayName = "TC-SET-04/05 Backup export contains the encrypted vault and restores into an empty folder")]
    public async Task BackupRestore()
    {
        await using var v = await TempVault.CreateAsync();
        await v.Journal.SaveAsync(TestData.Entry(TestData.Today, "ZZQX backed up"));
        var zip = Path.Combine(NewDir(), "backup.zip");
        Directory.CreateDirectory(Path.GetDirectoryName(zip)!);
        BackupService.Export(v.Folder, zip);
        using (var archive = System.IO.Compression.ZipFile.OpenRead(zip))
        {
            var names = archive.Entries.Select(e => e.FullName).ToList();
            Assert.Contains("vault.json", names);
            Assert.Contains("catalog.jcat", names);
            Assert.Contains(names, n => n.StartsWith("entries/"));
        }
        Assert.Equal(-1, TempVault.IndexOf(File.ReadAllBytes(zip), "ZZQX"u8.ToArray()));

        var restoreTo = NewDir();
        BackupService.Restore(zip, restoreTo);
        var session = new VaultSession(new JsonVaultHeaderStore(new VaultPaths(restoreTo), new AtomicFileWriter()),
            [new RecoveryCodeKeyProtector(1_000)], v.Machine, v.Clock);
        await session.UnlockWithRecoveryCodeAsync(v.Code);
        using var services = await VaultServices.OpenAsync(session, restoreTo, v.Clock, () => v.Inspiration);
        Assert.Equal("ZZQX backed up", (await services.Journal.OpenDayAsync(TestData.Today)).Entry!.DefaultTab.Document.ExtractPlainText());
        Assert.Throws<IOException>(() => BackupService.Restore(zip, restoreTo));
    }

    [Fact(DisplayName = "TC-NFR-08 Logger writes the message and the error type, never the exception text")]
    public void LoggerOmitsContent()
    {
        var dir = NewDir();
        var logger = new FileLoggerProvider(dir).CreateLogger("Test");
        logger.LogError(new IOException("ZZQX secret path"), "Saving failed.");
        var text = File.ReadAllText(Directory.GetFiles(dir).Single());
        Assert.Contains("Saving failed.", text);
        Assert.Contains("IOException", text);
        Assert.DoesNotContain("ZZQX", text);
    }
}
