# Journal

A private daily journal and planner for Windows (.NET 10, WPF). Each day has one entry made of tabs (one per tag) with formatted text, lists, checklists, images with searchable captions, and attached files. A month calendar shows the days with entries; each day shows a Bible verse and a philosophy or literature quote and can be marked sad, neutral or happy. Everything is encrypted with AES-256-GCM and unlocked with Windows Hello, so there is no password to remember. A one-time recovery code is the backup way in.

Design, requirements and test cases are in [docs/](docs/):
[Architecture.md](docs/Architecture.md), [Requirements.md](docs/Requirements.md), [TestCases.md](docs/TestCases.md). Bugs and changes are logged in [Issues.md](docs/Issues.md).

## Build and run

```bash
dotnet build Journal.sln
```

```bash
dotnet run --project src/Journal.App
```

Requires Windows 10 (19041) or later and the .NET 10 SDK. Windows Hello (PIN at minimum, ideally fingerprint or face) must be set up in Windows Settings > Accounts > Sign-in options to unlock without the recovery code.

## Windows Hello notes

- The journal asks Windows Hello only when you click **Unlock with Windows Hello** (or press Enter).
- The Windows Hello dialog belongs to Windows. If you close the journal while that dialog is waiting, the dialog stays open until you cancel it; the journal can't close it for you. Cancelling it is harmless.
- Windows decides where the dialog appears. For the journal's normal unlock it always opens on your main display, even when the journal is on another display; Windows doesn't let an app place or move it.
- While the journal waits for Windows Hello, the unlock window says so. If you can't see the dialog, press Alt+Tab to find it.

## Tests

```bash
dotnet test Journal.sln
```

The 3,650-entry unlock performance test (TC-NFR-13) is skipped by default. Run it in Release:

```bash
JOURNAL_PERF=1 dotnet test tests/Journal.Infrastructure.Tests -c Release --filter "DisplayName~TC-NFR-13"
```

The day-switching performance test (TC-NFR-14) is skipped the same way. It currently misses its target; see ISS-026 in [docs/Issues.md](docs/Issues.md).

```bash
JOURNAL_PERF=1 dotnet test tests/Journal.App.Tests -c Release --filter "DisplayName~TC-NFR-14"
```

Manual test cases (real Windows Hello, Win+L, display scaling, a second PC) are listed in [docs/TestCases.md](docs/TestCases.md) with level **M**.

## Versions and releases

The version is set in one place, `<Version>` in `Directory.Build.props`, and the app shows it in Settings → General and on the unlock screen. Pushing a `v*` tag builds the Windows zip and publishes a GitHub Release.

## Where things are kept

| What | Where |
|---|---|
| Encrypted journal (default) | `%LOCALAPPDATA%\Journal\Vault` (can be moved to OneDrive in Settings) |
| Settings (no journal content) | `%APPDATA%\Journal\settings.json` |
| Logs (no journal content) | `%LOCALAPPDATA%\Journal\logs` |
| Opened attachment copies | `%LOCALAPPDATA%\Journal\temp`, deleted on lock and exit |

## Content sources

- Bible verses: World English Bible (public domain), from ebible.org.
- Other scriptures (ISS-012), all public-domain translations: Hebrew Bible, JPS 1917 (Wikisource); Quran, tr. Marmaduke Pickthall (1930); Bhagavad Gita, tr. Edwin Arnold (1885); Dhammapada, tr. F. Max Müller (1881); Tao Te Ching, tr. James Legge (1891); Chuang Tzu, tr. Herbert A. Giles (1889), all from Project Gutenberg; Sikh hymns (Japji, Asa ki War, Sohila), tr. Max Arthur Macauliffe (1909), from the archive.org scan, corrected by hand. Each passage records its tradition, scripture and translation in `src/Journal.Infrastructure/Content/verses.json`.
- Atheism (ISS-029): public-domain works of Lucretius (tr. Leonard), d'Holbach, Shelley, Feuerbach (tr. George Eliot), Bradlaugh, Foote, Ingersoll, Huxley, Haeckel (tr. McCabe), Blatchford, Goldman and Chapman Cohen, from Project Gutenberg.
- Philosophy, literature, science and humanism quotes: public-domain editions from Project Gutenberg. Each quote records its category and source edition in `src/Journal.Infrastructure/Content/quotes.json`.
