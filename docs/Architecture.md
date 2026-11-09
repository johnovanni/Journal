# Journal: Architecture & Design

| | |
|---|---|
| **Version** | 1.2 (as built, including changes through ISS-024) |
| **Date** | 2026-11-04 |
| **Platform** | Windows 10/11, .NET 10, WPF |
| **Status** | Approved 2026-10-15 and implemented. This document describes the code as it is; later changes are logged in [Issues.md](Issues.md). |

## 1. Purpose and scope

A private, single-user desktop journal and planner for Windows. Each calendar day, past or future, has at most one entry. An entry is written in a simple word-processor-style editor that mixes formatted text, lists and checklists, inline images (each with a searchable caption) and attached data files. An entry can be split into tabs, one per tag. A month calendar highlights the days that have entries. Every day shows a Bible verse and a philosophy or literature quote, both configurable. Each day up to today can be marked sad, neutral or happy; the calendar tints those days and counts them. Everything is encrypted at rest and unlocked with **Windows Hello** (fingerprint, face or PIN), so there is no journal password to remember.

**Out of scope for v1:** multi-user, cloud sync built into the app (the vault folder can be placed in OneDrive instead), mobile clients, collaborative editing, spell-check beyond what WPF provides.

## 2. Key design decisions

| # | Decision | Rationale |
|---|---|---|
| D1 | **.NET 10 WPF, MVVM** with CommunityToolkit.Mvvm; objects are wired by hand in `AppController`, with no DI container | Matches your toolchain; MVVM keeps UI logic testable. |
| D2 | **Layered architecture**: `Journal.Core` (no UI, no OS), `Journal.Infrastructure` (crypto, storage, Windows Hello), `Journal.App` (WPF) | Domain logic and search are unit-testable without Windows Hello or a UI. |
| D3 | **Own document model** (`JournalDocument` → `DocBlock` subclasses), mapped to and from WPF `FlowDocument` | Images and attachments become first-class objects with captions; avoids storing fragile XAML; makes text, captions and file names searchable. |
| D4 | **Envelope encryption**: one random 256-bit *data key* encrypts everything with AES-256-GCM. The data key is stored only in *wrapped* form, once per unlock method. | Lets us add or replace unlock methods (new PC, new fingerprint) without re-encrypting the journal. |
| D5 | **Primary unlock = Windows Hello key credential.** A TPM-backed RSA key, created with `KeyCredentialManager`, signs a fixed vault challenge. The signature is run through HKDF to produce the key that unwraps the data key. | The key never leaves the TPM and can only be used after a biometric/PIN check. No password. |
| D6 | **Backup unlock = 25-character recovery code**, shown once at setup for you to print or store in a safe place. | Windows Hello keys are tied to this PC and this Windows install. Without a recovery code, a new PC, Windows reinstall or TPM reset would lose the journal permanently. |
| D7 | **File-per-entry vault** (`entries/*.jent`, `blobs/*.jblob`) plus an encrypted **catalog** used for the calendar and search | Small writes per save, works well inside a OneDrive/backup folder, a corrupted file affects one day only. |
| D8 | **In-memory search index** built from the catalog on unlock | Encrypted data can't be indexed on disk in plaintext. A decade of entries is a few MB of text, so in-memory search is instant. |
| D9 | **Verse and quote are stored with the entry** once the entry is first saved | Past days keep showing the verse/quote you saw that day, even if you later change the settings. |
| D10 | **Built-in public-domain content**: verses from the **World English Bible (WEB)**, a modern-English translation, and, if the user ticks them, passages from public-domain translations of other scriptures (Hebrew Bible, Quran, Bhagavad Gita, Dhammapada, Tao Te Ching, Chuang Tzu and Sikh hymns; ISS-012) and passages from atheist and agnostic writers (ISS-029), and quotes from classical authors (Marcus Aurelius, Seneca, Epictetus, Confucius, Lao Tzu, Plato, Aristotle, Pascal, etc.) and from world literature (Shakespeare, Tagore, Rumi, Dickinson, Machado de Assis, Olive Schreiner, etc.; ISS-011) and science (Darwin, Faraday, Poincaré, Laplace, Leonardo, etc.; ISS-012) and humanism (Erasmus, Paine, Wollstonecraft, Mill, Russell, etc.; ISS-028), plus user-importable lists | WEB reads as modern English and is public domain, so it can ship inside the app and work offline. The NRSV (the text of the *New Oxford Annotated Bible*), NIV and ESV are copyrighted and cannot be bundled; you can bring verses from a translation you own in through the import feature (§8.2). |
| D11 | **Any date can have an entry, including future dates** | The journal doubles as a planner: write plans and checklists on a future day, then tick them off. |

## 3. Architecture overview

```mermaid
flowchart TB
    subgraph App["Journal.App (WPF, net10.0-windows)"]
        direction TB
        Views["Views (XAML)<br/>MainWindow · UnlockWindow · SetupWindow<br/>SettingsWindow"]
        Controls["Custom controls<br/>MonthCalendarControl · JournalEditor (RichTextBox)<br/>ImageBlockView · AttachmentChip · ChecklistCheckBox<br/>MoodFace · MoodCount · ColumnPanel"]
        VMs["ViewModels<br/>MainViewModel · CalendarViewModel · EntryViewModel<br/>EntryTabViewModel · SearchViewModel · MoodSummaryViewModel<br/>UnlockViewModel · SetupViewModel · SettingsViewModel"]
        Mapper["Editor<br/>FlowDocumentMapper (JournalDocument ⇄ FlowDocument)<br/>JournalDocumentEditor · JournalClipboard"]
        AppSvc["App services<br/>AppController · LockCoordinator · MainWindowCloser<br/>SettingsActions · DialogService · ImageCache · AppInfo"]
        Views --> VMs
        Controls --> Mapper
        Views --> Controls
        VMs --> AppSvc
    end

    subgraph Core["Journal.Core (net10.0, no UI / no OS dependencies)"]
        direction TB
        Domain["Domain model<br/>JournalEntry · EntryTab · Tag<br/>JournalDocument · DocBlock hierarchy"]
        Services["Application services<br/>JournalService (facade) · VaultSession<br/>SearchService · DailyInspirationService"]
        Ports["Ports (interfaces)<br/>IEntryRepository · IBlobStore · ICatalogStore · IUserListStore<br/>IVaultHeaderStore · IKeyProtector · IMachineIdentity<br/>IVerseSource · IQuoteSource · ISettingsStore · IClock · IScheduler"]
        Search["Search engine<br/>SearchQueryParser · InMemorySearchIndex · SnippetBuilder"]
        Services --> Domain
        Services --> Ports
        Services --> Search
    end

    subgraph Infra["Journal.Infrastructure (net10.0-windows10.0.19041)"]
        direction TB
        Crypto["Crypto<br/>VaultCrypto · KeyWrapper · KeyDerivation"]
        Keys["Key protectors<br/>WindowsHelloKeyProtector · HelloGatedDpapiProtector<br/>RecoveryCodeKeyProtector"]
        Store["File vault<br/>FileEntryRepository · FileBlobStore · FileCatalogStore<br/>FileUserListStore · JsonVaultHeaderStore · BackupService"]
        Content["Content sources<br/>EmbeddedVerseSource · EmbeddedQuoteSource"]
        Settings["JsonSettingsStore"]
    end

    VMs --> Services
    Mapper --> Domain
    Infra -. implements .-> Ports

    Hello[("Windows Hello<br/>TPM-backed key")]
    Disk[("Vault folder<br/>%LOCALAPPDATA%\\Journal\\Vault<br/>(or OneDrive)")]
    Keys --> Hello
    Store --> Disk
```

**Dependency rule:** arrows point inward. `Journal.Core` references nothing else in the solution. `Journal.Infrastructure` implements Core's interfaces. `App.xaml.cs` starts `AppController`, which composes everything by hand. Imported lists use `ListVerseSource` and `ListQuoteSource` from Core.

### 3.1 Solution layout

```
Journal/
├─ Journal.sln
├─ docs/                         Architecture.md, Requirements.md, TestCases.md
├─ src/
│  ├─ Journal.Core/              Domain, Documents, Calendar, Persistence, Search, Inspiration, Security, Services, Settings
│  ├─ Journal.Infrastructure/    Crypto, Hello, Storage, Content (embedded verses.json, quotes.json), Logging
│  └─ Journal.App/               Views, ViewModels, Controls, Editor (FlowDocumentMapper, JournalDocumentEditor), Services, Themes
└─ tests/
   ├─ Journal.Core.Tests/            xUnit: domain, document model, search, inspiration, session, moods
   ├─ Journal.Infrastructure.Tests/  xUnit: crypto round trips, tamper detection, file vault, recovery code, content
   └─ Journal.App.Tests/             xUnit (STA): mapper round trips, view models with fakes, editor and clipboard,
                                     real windows shown off screen (layout), locking and exit
```

## 4. Screen design

```
┌──────────────────────────────────────────────────────────────────────────────────┐
│ Journal (quill logo)                                             Settings   Lock │
├───────────────────────────┬──────────────────────────────────────────────────────┤
│   <   October 2026    >   │ Thursday, October 15, 2026      :(  :|  :)    [del]  │
│   Su Mo Tu We Th Fr Sa    │ ┌──────────────────────────────────────────────────┐ │
│                1  2  3    │ │ BIBLE                    LITERATURE              │ │
│    4 [5] 6  7 [8] 9 10    │ │ Trust in Yahweh with...  All the world's a...    │ │
│   11 12 [13]14 (15)16 17  │ │ Proverbs 3:5 WEB  Copy   Shakespeare  Copy       │ │
│   18 19 20 21 22 23 24    │ └──────────────────────────────────────────────────┘ │
│   25 26 27 28 29 30 31    │ Tags: [family x] [work x] [+ add tag]                │
│   Today  o entry  . open  │ [ Journal ][ family ][ work ]              [ + Tab ] │
│ ------------------------- │ ┌──────────────────────────────────────────────────┐ │
│ MOODS                     │ │ Font v  12 v  A  H | B I U S | H1 H2 P | * 1. [] │ │
│ 30 days     :(2 :|1 :)2   │ │ Image  File                                      │ │
│ 12 months   :(2 :|1 :)2   │ │                                                  │ │
│ ------------------------- │ │ Spent the morning in the garden with the kids.   │ │
│ Search: tag:family garden │ │ +------------------------+                       │ │
│ Results (3)               │ │ |       [ photo ]        |                       │ │
│ > Oct 13  family          │ │ | Garden at sunrise      |  <- caption           │ │
│   ...planted the garden.. │ │ +------------------------+                       │ │
│ > Sep 28  caption         │ │ [file] harvest-weights.xlsx (24 KB), yields      │ │
│                           │ └──────────────────────────────────────────────────┘ │
├───────────────────────────┴──────────────────────────────────────────────────────┤
│ Saved 10:42                                  Locks after 10 min without activity │
└──────────────────────────────────────────────────────────────────────────────────┘
  [5] = day with an entry (highlighted)   (15) = today / selected
  :( :| :) = sad, neutral, happy. A day with a mood is tinted (red = sad, green = happy,
  normal = neutral) and shows a small face. The MOODS card counts them.
  future days with an entry use the same highlight with a dashed outline (planned)
  the label over the quote is its category: PHILOSOPHY or LITERATURE
```

**Tabs and tags.** Every entry starts with one untagged tab named **Journal**. Adding a tab asks for a tag (autocomplete from existing tags); the tab is titled with that tag. The entry's tag set is the union of its tab tags plus any extra tags added on the tag bar, so an entry can be tagged without splitting it into tabs.

**Editor.** A `RichTextBox` with a small toolbar: font family, font size, text color, highlight color, bold, italic, underline, strikethrough, Heading 1, Heading 2, bulleted list, numbered list, checklist, Insert Image (file picker, paste from clipboard or drag-drop) and Attach File. Lists can be indented (Tab / Shift+Tab, up to 4 levels). A checklist item shows a checkbox you can tick with the mouse or Ctrl+Enter; ticked items are shown with strikethrough. The font list is the fonts installed in Windows; colors come from a 16-swatch palette plus a custom picker. Images are placed on their own line between paragraphs at the caret, with an editable caption beneath. Attached files appear as a chip showing the name, size and description; double-click opens it, right-click offers *Save As*, *Edit description* and *Remove*. Copy, cut, paste and drag within the journal keep all of this (§9).

**Mood.** Three faces beside the day's heading set the day's mood; clicking the chosen face again clears it (§8.1a).

**Version.** Settings → General and the unlock window show the app's name and version.

## 5. Domain and document model

```mermaid
classDiagram
    direction LR
    class JournalEntry {
        +DateOnly Date
        +DateTimeOffset CreatedUtc
        +DateTimeOffset ModifiedUtc
        +IReadOnlyList~EntryTab~ Tabs
        +IReadOnlySet~Tag~ ExtraTags
        +DailyInspiration Inspiration
        +bool IsInspirationPinned
        +Mood Mood
        +void SetMood(Mood mood, DateOnly today)
        +IReadOnlyList~Tag~ AllTags()
        +EntryTab AddTab(Tag tag)
        +void RemoveTab(Guid tabId)
        +void RetagTab(Guid tabId, Tag tag)
        +void MoveTab(Guid tabId, int newIndex)
        +bool AddTag(Tag tag)
        +bool RemoveTag(Tag tag)
        +bool IsEmpty()
        +IEnumerable~BlobRef~ ReferencedBlobs()
    }
    class EntryTab {
        +Guid Id
        +Tag? Tag
        +string Title
        +JournalDocument Document
    }
    class Mood {
        <<enum>>
        None
        Sad
        Neutral
        Happy
    }
    class Tag {
        <<value object>>
        +string Name
        +string Normalized
        +static Tag Parse(string)
    }
    class DailyInspiration {
        <<value object>>
        +Verse? Verse
        +Quote? Quote
    }
    class Verse {
        <<value object>>
        +string Reference
        +string Text
        +string Translation
        +string Book
        +string? Tradition
        +string? Scripture
        +string CardLabel()
    }
    class Quote {
        <<value object>>
        +string Text
        +string Author
        +string? Work
        +string Tradition
        +string? Source
        +string Category
    }
    class JournalDocument {
        +IList~DocBlock~ Blocks
        +string ExtractPlainText()
        +IEnumerable~ImageBlock~ Images()
        +IEnumerable~AttachmentBlock~ Attachments()
        +IEnumerable~ParagraphBlock~ Paragraphs()
        +bool IsEmpty()
        +IEnumerable~BlobRef~ ReferencedBlobs()
    }
    class DocBlock {
        <<abstract>>
        +Guid Id
        +abstract void Accept(IDocBlockVisitor v)
        +abstract bool IsEmpty()
        +virtual IEnumerable~BlobRef~ ReferencedBlobs()
    }
    class ParagraphBlock {
        +ParagraphStyle Style
        +ListKind List
        +int ListLevel
        +bool IsChecked
        +TextAlign Alignment
        +IList~TextRun~ Runs
        +string PlainText
    }
    class TextRun {
        <<value object>>
        +string Text
        +RunFormat Format
        +string? FontFamily
        +double? FontSize
        +ArgbColor? Foreground
        +ArgbColor? Highlight
    }
    class RunFormat {
        <<flags enum>>
        None
        Bold
        Italic
        Underline
        Strikethrough
    }
    class ParagraphStyle {
        <<enum>>
        Normal
        Heading1
        Heading2
    }
    class ListKind {
        <<enum>>
        None
        Bulleted
        Numbered
        Checklist
    }
    class ArgbColor {
        <<value object>>
        +byte A
        +byte R
        +byte G
        +byte B
        +string ToHex()
    }
    class ImageBlock {
        +BlobRef Image
        +string Caption
        +double? DisplayWidth
        +int PixelWidth
        +int PixelHeight
    }
    class AttachmentBlock {
        +BlobRef File
        +string FileName
        +string ContentType
        +long SizeBytes
        +string Description
    }
    class BlobRef {
        <<value object>>
        +Guid Id
        +string Sha256
    }
    class IDocBlockVisitor {
        <<interface>>
        +Visit(ParagraphBlock)
        +Visit(ImageBlock)
        +Visit(AttachmentBlock)
    }

    JournalEntry "1" *-- "1..*" EntryTab
    JournalEntry "1" o-- "*" Tag : ExtraTags
    JournalEntry "1" *-- "1" DailyInspiration
    JournalEntry --> Mood
    DailyInspiration o-- Verse
    DailyInspiration o-- Quote
    EntryTab o-- "0..1" Tag
    EntryTab "1" *-- "1" JournalDocument
    JournalDocument "1" *-- "*" DocBlock
    DocBlock <|-- ParagraphBlock
    DocBlock <|-- ImageBlock
    DocBlock <|-- AttachmentBlock
    ParagraphBlock *-- "*" TextRun
    TextRun --> RunFormat
    TextRun --> ArgbColor
    ParagraphBlock --> ParagraphStyle
    ParagraphBlock --> ListKind
    ImageBlock --> BlobRef
    AttachmentBlock --> BlobRef
    DocBlock ..> IDocBlockVisitor
```

**Invariants enforced by `JournalEntry`:**

- At most one entry per `DateOnly` (the date is the entry's natural key). Past, present and future dates are all allowed (D11).
- A run's null font, size or color means "use the default", so documents stay small and follow future theme changes.
- `IsChecked` is only meaningful when `List == Checklist`; consecutive list paragraphs with the same kind and level form one list, and numbering is computed when rendering, not stored.
- At least one tab; at most one untagged tab; no two tabs share a tag.
- A mood can't be set on a day after today (`SetMood` refuses it). A mood alone keeps the entry: `IsEmpty` is false while a mood is set.
- Tags are case-insensitive, trimmed, 1–40 characters, no commas; `Tag.Normalized` is the lower-invariant form used for matching.
- Removing a tab or block does not delete its blobs immediately. Orphaned blobs are removed by `JournalService.CollectGarbageAsync` when the journal locks or unlocks, so undo inside the editor stays safe.

**Blocks answer for themselves.** Each block says whether it is empty and which vault files it uses, and `JournalDocument` asks its blocks. Code that must handle every kind of block, such as `FlowDocumentMapper`, implements `IDocBlockVisitor`, so a new kind won't compile until it is handled.

**Serialization:** `System.Text.Json` with polymorphic `DocBlock` (`[JsonDerivedType]` discriminators `p`, `img`, `file`) and a `formatVersion` field on every persisted object for future migrations.

## 6. Security design

### 6.1 Threat model

| Threat | Mitigation |
|---|---|
| Someone copies the vault folder or a backup | Every file is AES-256-GCM encrypted; keys are not on disk in usable form. |
| Someone uses the PC while you are signed in but away | Auto-lock after idle time (default 10 min), lock on Windows session lock / sleep, lock button. Unlocking requires Windows Hello again. |
| Tampering with or swapping encrypted files | GCM authentication tag; file kind and identity are bound in associated data, so a day's file cannot be renamed into another day. |
| Dates leaking through file names | Entry file names are `HMAC-SHA256(dataKey, date)`, not the date. |
| Lost PC, new PC, Windows reinstall, TPM reset | Recovery code unlock (D6), then re-enroll Windows Hello on the new machine. |
| Plaintext left on disk | Opened attachments are decrypted to a private temp folder and deleted on lock and on exit; stale ones are removed at startup. No plaintext caches or thumbnails are written. |
| Key material lingering in memory | `SecretKey` holds keys in pinned arrays and zeroes them on lock/dispose. (Managed memory gives no hard guarantee; this is best effort.) |

**Not protected against:** malware running as you while the journal is unlocked, or someone who can pass your Windows Hello check.

### 6.2 Vault format

```
<VaultFolder>/
├─ vault.json            plaintext header: formatVersion, vaultId, helloChallenge, keySlots[]
├─ catalog.jcat          encrypted: per-entry date, tags, tab titles, extracted searchable text
├─ entries/
│  └─ 3f9a…c1.jent       encrypted JournalEntry JSON (name = HMAC(date))
└─ blobs/
   └─ 7d0e…42.jblob      encrypted image or data file (name = blob GUID)
```

Every encrypted file: `magic "JRNL" | version (1 byte) | kind (1 byte) | nonce (12 bytes) | ciphertext | tag (16 bytes)`. Associated data = `magic | version | kind | vaultId | logical id` (date or blob id). Writes go to a temp file and are then atomically renamed, so a crash never leaves a half-written entry. Blobs larger than 64 MB are encrypted in 1 MB chunks (each its own GCM segment with a chunk index in the associated data) to keep memory bounded. Attachment size limit: 250 MB per file.

`keySlots[]` holds one wrapped copy of the data key per unlock method:

```json
{ "type": "windows-hello", "credentialName": "Journal.Vault.<vaultId>", "machine": "DESKTOP-ABC",
  "wrappedKey": "base64(nonce|ciphertext|tag)" }
{ "type": "recovery-code", "kdf": "PBKDF2-SHA256", "iterations": 600000, "salt": "base64",
  "wrappedKey": "base64(...)" }
```

### 6.3 Key derivation

```mermaid
flowchart LR
    subgraph Hello["Windows Hello slot"]
        C["helloChallenge<br/>(32 random bytes, in vault.json)"] --> S["KeyCredential.RequestSignAsync<br/>(TPM RSA-2048, user verifies<br/>with face / fingerprint / PIN)"]
        S --> Sig["signature (256 bytes)"]
        Sig --> H1["HKDF-SHA256<br/>info = 'journal/hello-kek/v1'"]
        H1 --> K1["KEK₁"]
    end
    subgraph Recovery["Recovery code slot"]
        RC["recovery code<br/>XXXXX-XXXXX-XXXXX-XXXXX-XXXXX<br/>(125 bits, Crockford base32)"] --> P["PBKDF2-SHA256<br/>600 000 iterations + salt"]
        P --> K2["KEK₂"]
    end
    K1 --> U1["AES-GCM unwrap"]
    K2 --> U2["AES-GCM unwrap"]
    U1 --> DEK["Data key (256-bit random)"]
    U2 --> DEK
    DEK --> E["Encrypt / decrypt<br/>entries, blobs, catalog"]
    DEK --> N["HMAC file names"]
```

**Why this works without a password:** Windows Hello key credentials sign with RSA PKCS#1 v1.5, which is deterministic, so the same challenge always produces the same signature, and only after you pass the biometric/PIN check. During setup the app signs the challenge twice and confirms the signatures match before relying on it. If a machine ever produced non-deterministic signatures, setup falls back to `HelloGatedDpapiProtector`: the data key is protected with Windows DPAPI (bound to your Windows account) and released only after a successful `UserConsentVerifier` check. That fallback is weaker and the setup screen says so.

**Windows Hello prerequisite:** Windows Hello must be set up (a PIN at minimum; fingerprint or face recommended). If it is not, setup explains how to enable it in Windows Settings and offers recovery-code-only mode until then.

### 6.4 Security classes

```mermaid
classDiagram
    direction TB
    class IKeyProtector {
        <<interface>>
        +string SlotType
        +Task~bool~ IsAvailableAsync()
        +Task~KeySlot~ ProtectAsync(SecretKey dataKey, VaultHeader header, UnlockContext ctx)
        +Task~SecretKey~ UnprotectAsync(KeySlot slot, VaultHeader header, UnlockContext ctx)
    }
    class WindowsHelloKeyProtector {
        -IHelloCredentialApi hello
        -IMachineIdentity machine
    }
    class RecoveryCodeKeyProtector
    class HelloGatedDpapiProtector {
        -IUserConsentApi consent
        -IMachineIdentity machine
    }
    class IHelloCredentialApi {
        <<interface>>
        +Task~bool~ IsSupportedAsync()
        +Task CreateAsync(string name, nint window, CancellationToken ct)
        +Task~byte[]~ SignAsync(string name, byte[] data, nint window, CancellationToken ct)
        +Task DeleteAsync(string name)
    }
    class IUserConsentApi {
        <<interface>>
        +Task~bool~ IsAvailableAsync()
        +Task~bool~ VerifyAsync(string message, nint window, CancellationToken ct)
    }
    class WinRtHelloCredentialApi
    class WinRtUserConsentApi
    class RecoveryCode {
        <<value object>>
        +string Formatted
        +static RecoveryCode Generate()
        +static bool TryParse(string, out RecoveryCode)
    }
    class SecretKey {
        <<IDisposable>>
        +ReadOnlySpan~byte~ Span
        +static SecretKey Generate()
        +void Dispose()
    }
    class KeyWrapper {
        <<static>>
        +byte[] Wrap(SecretKey kek, SecretKey key, byte[] aad)
        +SecretKey Unwrap(SecretKey kek, byte[] wrapped, byte[] aad)
    }
    class KeyDerivation {
        <<static>>
        +SecretKey HelloKek(signature, challenge)
        +SecretKey RecoveryKek(RecoveryCode code, byte[] salt, int iterations)
        +SecretKey SubKey(SecretKey dataKey, string purpose)
    }
    class VaultCrypto {
        <<IDisposable>>
        +Guid VaultId
        +string EntryFileName(DateOnly date)
        +byte[] Seal(FileKind kind, string logicalId, plain)
        +byte[] Open(FileKind kind, string logicalId, sealed)
        +Task EncryptStreamAsync(input, output, id)
        +Task DecryptStreamAsync(input, output, id)
    }
    class VaultSession {
        +VaultHeader? Header
        +bool IsUnlocked
        +SecretKey DataKey
        +bool HasHelloSlotForThisMachine
        +UnlockThrottle Throttle
        +event Unlocked
        +event Locked
        +Task LoadHeaderAsync()
        +void RegisterBeforeLock(handler)
        +Task UnlockWithHelloAsync(nint window, CancellationToken ct)
        +Task UnlockWithRecoveryCodeAsync(RecoveryCode code)
        +Task LockAsync(LockReason reason)
        +Task~SetupResult~ CreateVaultAsync(bool useHello, nint window, CancellationToken ct)
        +Task EnrollHelloAsync(nint window)
        +Task~RecoveryCode~ RegenerateRecoveryCodeAsync()
        +Task RemoveHelloSlotAsync(string machineName)
    }
    class AutoLockPolicy {
        +TimeSpan? Timeout
        +TimeSpan? Remaining
        +bool IsExpired
        +event LockRequested
        +void OnActivity()
        +void Tick()
    }

    IKeyProtector <|.. WindowsHelloKeyProtector
    IKeyProtector <|.. RecoveryCodeKeyProtector
    IKeyProtector <|.. HelloGatedDpapiProtector
    WindowsHelloKeyProtector --> IHelloCredentialApi
    HelloGatedDpapiProtector --> IUserConsentApi
    IHelloCredentialApi <|.. WinRtHelloCredentialApi
    IUserConsentApi <|.. WinRtUserConsentApi
    WindowsHelloKeyProtector ..> KeyWrapper
    WindowsHelloKeyProtector ..> KeyDerivation
    RecoveryCodeKeyProtector ..> KeyWrapper
    RecoveryCodeKeyProtector ..> KeyDerivation
    RecoveryCodeKeyProtector ..> RecoveryCode
    VaultSession --> "1..*" IKeyProtector
    VaultSession --> SecretKey
    VaultCrypto ..> KeyDerivation
    AutoLockPolicy ..> VaultSession : lock requested
```

`IKeyProtector` is a **Strategy**: the session tries Windows Hello first and offers the recovery code as an alternative on the unlock screen. Tests use a `FakeKeyProtector` so nothing in the test suite needs real Windows Hello.

### 6.5 Unlock sequence

```mermaid
sequenceDiagram
    actor User
    participant UW as UnlockWindow
    participant VS as VaultSession
    participant HP as WindowsHelloKeyProtector
    participant WH as Windows Hello (TPM)
    participant AC as AppController
    participant VC as VaultServices
    participant MW as MainWindow

    User->>UW: launch app, or the journal locked
    UW->>VS: LoadHeaderAsync()
    Note over UW,User: Nothing is asked until the user acts (ISS-008)
    User->>UW: click Unlock with Windows Hello, or press Enter
    UW->>UW: show "Waiting for Windows Hello..."
    UW->>VS: UnlockWithHelloAsync(window, cancellation)
    VS->>HP: UnprotectAsync(helloSlot, header, context)
    HP->>WH: allow the dialog to the foreground, then RequestSignAsync(challenge)
    WH-->>User: fingerprint / face / PIN prompt
    User-->>WH: verified
    WH-->>HP: signature
    HP->>HP: HKDF → KEK, AES-GCM unwrap
    HP-->>VS: SecretKey dataKey
    VS-->>UW: unlocked
    UW->>AC: Unlocked
    AC->>VC: OpenAsync: load catalog, rebuild search index, remove unused files
    AC->>MW: show, select today
    Note over UW,User: If Hello fails or is cancelled, the window says so and offers<br/>Unlock with Windows Hello again and Use recovery code
```

**Where the dialog appears.** The Windows Hello key-signing dialog belongs to Windows and takes no window: it always opens on the main display, and Windows refuses to let an app move or close it (ISS-007, ISS-009). So:

- on a side display, the unlock window says the dialog opens on the main display;
- Journal allows the dialog to take the foreground just before asking, and doesn't pull its own window forward at that moment (ISS-023);
- while a request waits, the unlock window says how to find the dialog if it can't be seen;
- closing the window, exiting or using the recovery code cancels a waiting request. The dialog itself stays until the user cancels it.

## 7. Persistence

```mermaid
classDiagram
    direction LR
    class IEntryRepository {
        <<interface>>
        +Task~JournalEntry?~ GetAsync(DateOnly date)
        +Task SaveAsync(JournalEntry entry)
        +Task DeleteAsync(DateOnly date)
        +Task LoadAllAsync()
    }
    class IBlobStore {
        <<interface>>
        +Task~BlobRef~ PutAsync(Stream content)
        +Task CopyToAsync(BlobRef blob, Stream destination)
        +Task DeleteAsync(Guid blobId)
        +Task ListAsync()
        +bool Exists(Guid blobId)
    }
    class ICatalogStore {
        <<interface>>
        +Task~Catalog?~ LoadAsync()
        +Task SaveAsync(Catalog catalog)
    }
    class IUserListStore {
        <<interface>>
        +Task LoadVersesAsync()
        +Task SaveVersesAsync(verses)
        +Task LoadQuotesAsync()
        +Task SaveQuotesAsync(quotes)
    }
    class Catalog {
        +IReadOnlyCollection~CatalogEntry~ Entries
        +CatalogEntry? Get(DateOnly date)
        +bool Contains(DateOnly date)
        +void Upsert(CatalogEntry e)
        +bool Remove(DateOnly d)
        +ISet~DateOnly~ DatesInRange(from, to)
        +ISet~DateOnly~ DatesWithOpenItems(from, to)
        +IReadOnlyDictionary MoodsInRange(from, to)
        +MoodCounts CountMoods(from, to)
        +IReadOnlyList~string~ AllTags()
        +ISet~Guid~ ReferencedBlobIds()
    }
    class CatalogEntry {
        +DateOnly Date
        +IReadOnlyList~string~ Tags
        +IReadOnlyList~CatalogTab~ Tabs
        +int OpenChecklistItems
        +int DoneChecklistItems
        +IReadOnlyList~Guid~ BlobIds
        +Mood Mood
        +static CatalogEntry From(JournalEntry e)
    }
    class CatalogTab {
        +Guid Id
        +string Title
        +string? Tag
        +string BodyText
        +IReadOnlyList~string~ ImageCaptions
        +IReadOnlyList~string~ AttachmentText
    }
    class FileEntryRepository
    class FileBlobStore
    class FileCatalogStore
    class FileUserListStore
    class AtomicFileWriter {
        +Task WriteAsync(string path, byte[] data)
    }
    class VaultCrypto {
        +byte[] Seal(FileKind kind, string logicalId, plain)
        +byte[] Open(FileKind kind, string logicalId, sealed)
    }

    IEntryRepository <|.. FileEntryRepository
    IBlobStore <|.. FileBlobStore
    ICatalogStore <|.. FileCatalogStore
    IUserListStore <|.. FileUserListStore
    FileEntryRepository --> VaultCrypto
    FileBlobStore --> VaultCrypto
    FileCatalogStore --> VaultCrypto
    FileUserListStore --> VaultCrypto
    FileEntryRepository --> AtomicFileWriter
    FileCatalogStore --> AtomicFileWriter
    ICatalogStore ..> Catalog
    Catalog *-- CatalogEntry
    CatalogEntry *-- CatalogTab
```

**Saving:** the editor marks the entry dirty on every change. `JournalService` saves 2 seconds after the last keystroke, and also when you switch day, switch tab, lock or close. A save writes the entry file, then updates the catalog. If the catalog is ever missing or fails to decrypt, it is rebuilt from the entry files at unlock. An entry with no text, images, files, tags or mood is deleted rather than saved, so the calendar only highlights days with real content. Choosing a mood saves at once, so the calendar and the counts update immediately. `JournalService.CollectGarbageAsync` removes vault files no entry uses, at unlock and at lock.

## 8. Services: search and daily inspiration

```mermaid
classDiagram
    direction TB
    class JournalService {
        <<facade>>
        +SearchService Search
        +DailyInspirationService Inspiration
        +Catalog Catalog
        +event EntryChanged
        +Task InitializeAsync()
        +Task~EntryLoadResult~ OpenDayAsync(DateOnly date)
        +Task~SaveOutcome~ SaveAsync(JournalEntry entry)
        +Task DeleteEntryAsync(DateOnly date)
        +DailyInspiration Shuffle(JournalEntry entry)
        +Task~BlobRef~ AddBlobAsync(Stream content)
        +Task CopyBlobToAsync(BlobRef blob, Stream destination)
        +Task~byte[]~ ReadBlobAsync(BlobRef blob)
        +Task~int~ CollectGarbageAsync(protectedIds)
        +ISet~DateOnly~ EntryDates(from, to)
        +ISet~DateOnly~ OpenItemDates(from, to)
        +IReadOnlyDictionary Moods(from, to)
        +MoodSummary GetMoodSummary()
        +IReadOnlyList~string~ AllTags()
        +bool HasEntry(DateOnly date)
    }
    class SearchService {
        +SearchResult Search(string text)
    }
    class SearchQueryParser {
        +SearchQuery Parse(string text)
    }
    class SearchQuery {
        +IReadOnlyList~string~ Terms
        +IReadOnlyList~string~ Phrases
        +IReadOnlyList~Tag~ Tags
        +DateOnly? From
        +DateOnly? To
        +SearchScope Scope
        +ChecklistFilter Checklist
    }
    class ISearchIndex {
        <<interface>>
        +void Rebuild(Catalog c)
        +void Upsert(CatalogEntry e)
        +void Remove(DateOnly d)
        +IReadOnlyList~SearchHit~ Query(SearchQuery q, int maxHits)
    }
    class InMemorySearchIndex
    class SearchHit {
        +DateOnly Date
        +Guid? TabId
        +string TabTitle
        +MatchSource Source
        +string Snippet
        +IReadOnlyList~HighlightRange~ Highlights
    }
    class MatchSource {
        <<enum>>
        Text
        ImageCaption
        Attachment
        Tag
    }
    class SnippetBuilder {
        +static Build(text, terms)
    }
    class DailyInspirationService {
        +InspirationResult For(DateOnly date, InspirationSettings s, Guid vaultId)
        +DailyInspiration Shuffle(DailyInspiration current, InspirationSettings s)
        +IReadOnlyList~string~ AvailableBooks()
        +IReadOnlyList~VerseTradition~ AvailableVerseTraditions()
        +IReadOnlyList~QuoteCategory~ AvailableCategories()
    }
    class IVerseSource {
        <<interface>>
        +string Name
        +IReadOnlyList~Verse~ All()
    }
    class IQuoteSource {
        <<interface>>
        +string Name
        +IReadOnlyList~Quote~ All()
    }
    class EmbeddedVerseSource
    class EmbeddedQuoteSource
    class ListVerseSource
    class ListQuoteSource
    class ISelectionStrategy {
        <<interface>>
        +int SelectIndex(int count, DateOnly date, Guid vaultId, string salt)
    }
    class DayOfYearStrategy
    class SeededRandomStrategy
    class InspirationSettings {
        +bool ShowVerse
        +bool ShowQuote
        +string VerseSource
        +IReadOnlySet~string~ VerseBooks
        +IReadOnlySet~string~ VerseTraditions
        +string QuoteSource
        +IReadOnlySet~string~ Traditions
        +IReadOnlySet~string~ HiddenCategories
        +SelectionMode Mode
    }

    JournalService --> SearchService : notifies index
    SearchService --> SearchQueryParser
    SearchService --> ISearchIndex
    SearchService --> SnippetBuilder
    ISearchIndex <|.. InMemorySearchIndex
    SearchQueryParser ..> SearchQuery
    ISearchIndex ..> SearchHit
    SearchHit --> MatchSource
    DailyInspirationService --> IVerseSource
    DailyInspirationService --> IQuoteSource
    DailyInspirationService --> ISelectionStrategy
    DailyInspirationService ..> InspirationSettings
    IVerseSource <|.. EmbeddedVerseSource
    IVerseSource <|.. ListVerseSource
    IQuoteSource <|.. EmbeddedQuoteSource
    IQuoteSource <|.. ListQuoteSource
    ISelectionStrategy <|.. DayOfYearStrategy
    ISelectionStrategy <|.. SeededRandomStrategy
```

### 8.1 Search syntax

| Query | Meaning |
|---|---|
| `garden` | Entries containing *garden* in text, captions, attachment names/descriptions or tags |
| `"morning walk"` | Exact phrase |
| `tag:family` | Entries tagged *family* (entry tag or tab tag) |
| `tag:family garden` | Tagged *family* **and** mentioning *garden* |
| `in:captions sunrise` | Only image captions (also `in:text`, `in:files`) |
| `from:2026-01-01 to:2026-03-31 prayer` | Date range |
| `is:open` / `is:done` | Entries with unticked / ticked checklist items (useful for planning; combine with `from:today`) |
| `from:today` | Today and later (the keyword `today` works in `from:` and `to:`) |

Matching is case- and accent-insensitive, terms are combined with AND, and a term matches word prefixes (`gard` finds *garden*). Results are newest first, one row per tab that matched, with a highlighted snippet and an icon for where it matched (text, caption, file, tag). Clicking a result opens that day and tab. The search box also offers tag autocomplete, and clicking a tag chip anywhere runs `tag:<name>`.

### 8.1a Day mood (ISS-013)

Each day up to today can be marked sad, neutral or happy with three faces next to the day's heading. The mood is part of the encrypted entry (`JournalEntry.Mood`) and is copied into the encrypted catalog (`CatalogEntry.Mood`), so the calendar can tint days (light red for sad, light green for happy, with a small face on every marked day) and the summary under the calendar can count the last 30 days and the last 12 months without opening every entry. Both periods end today and include today (`MoodSummary`). Days with no mood aren't counted. Choosing a face saves at once. Moods aren't part of search.

### 8.2 Daily verse and quote options

The verse card's label is the passage's scripture in capitals ("BIBLE", "QURAN", "TAO TE CHING"; FR-INSP-19). Each built-in passage in `verses.json` records its tradition, scripture, book and translation; a row without a tradition is a World English Bible verse, and a day saved before ISS-012 shows "BIBLE". Settings has a section per tradition, Christianity first, and only Christianity is ticked until the user ticks others. With several ticked, the day first picks a tradition evenly and then a passage within it, so the Bible's 477 verses don't crowd out the rest; by day of year the days cycle through the ticked traditions (FR-INSP-20). With only Christianity ticked the choice is exactly what it was before. Book choices are saved as "Tradition/Book", since the Bible and the Hebrew Bible share book names; a plain book name, as saved before ISS-012, is a Bible book.

The quote card's label is the quote's category in capitals ("PHILOSOPHY", "LITERATURE"). Categories are data: each quote in `quotes.json` names its category, and the label, Settings and filters are built from the categories present (FR-INSP-17). The settings store the categories turned *off*, so a category added to the content later is shown until the user turns it off. Philosophy and literature quotes alternate in `quotes.json`, and a science quote and a humanism quote follow every two others, so the day-of-year mode, which only reaches the first 366 quotes, shows all four.

| Setting | Options | Default |
|---|---|---|
| Show a verse | on / off | on |
| Verse source | Built-in scriptures · imported list | Built-in |
| Verse traditions | one section per tradition, Christianity first: Christianity (World English Bible, 477 verses), Atheism (writers from Lucretius to Chapman Cohen, labelled ATHEISM), Buddhism (Dhammapada), Hinduism (Bhagavad Gita), Islam (Quran), Judaism (Hebrew Bible, JPS 1917), Sikhism (Japji, Asa ki War, Sohila), Taoism (Tao Te Ching, Chuang Tzu), each with an *Include* tick box | Christianity only |
| Verse books filter | per tradition with more than one book, in the scripture's order down columns. Saved as "Tradition/Book" (a Bible book by its plain name); a tradition with none ticked keeps all its passages | all |
| Show a quote | on / off | on |
| Quote source | Built-in quotes (394 philosophy, 395 literature, 187 science, 201 humanism) · imported list | Built-in |
| Quote categories | one section per category in the content, Philosophy first (Philosophy, Humanism, Literature, Science; a category added to the content appears with no code change), each with an *Include* tick box | all ticked |
| Traditions filter | per category: Stoic, Greek, Chinese, … for philosophy; English, Persian, Chinese, Latin American, African, … for literature. Saved as "Category/Tradition"; a category with none ticked keeps all its quotes. Listed alphabetically down columns | all |
| Selection | Same each year by day-of-year · Random per day (stable for that date) | Day-of-year |
| Per-day | **Shuffle** button for a new verse/quote today; it is saved with the entry | |

Import format: a JSON or CSV file (`reference,text,translation` and optionally `scripture`, which becomes the card label, for verses; `text,author,work,tradition,category` for quotes; a quote without a category is labelled "Quote"). Use this to bring in verses from a copyrighted translation you own, such as the NRSV from the *New Oxford Annotated Bible*. Imported lists are stored inside the encrypted vault.

## 9. Presentation layer

```mermaid
classDiagram
    direction TB
    class MainViewModel {
        +CalendarViewModel Calendar
        +SearchViewModel Search
        +MoodSummaryViewModel Moods
        +EntryViewModel? CurrentEntry
        +string StatusText
        +string LockText
        +IRelayCommand LockCommand
        +IRelayCommand OpenSettingsCommand
        +IAsyncRelayCommand DeleteEntryCommand
        +Task SelectDateAsync(DateOnly d, Guid? tabId)
        +Task FlushAsync()
    }
    class CalendarViewModel {
        +int Year
        +int Month
        +string Title
        +DateOnly SelectedDate
        +DayOfWeek WeekStart
        +ObservableCollection~DayCellViewModel~ Days
        +IRelayCommand PreviousMonthCommand
        +IRelayCommand NextMonthCommand
        +IRelayCommand TodayCommand
        +event DateSelected
        +void Refresh()
    }
    class DayCellViewModel {
        +DateOnly Date
        +int DayNumber
        +bool IsInCurrentMonth
        +bool IsToday
        +bool HasEntry
        +bool IsFuture
        +bool IsPlanned
        +bool HasOpenItems
        +bool IsSelected
        +Mood Mood
    }
    class MoodSummaryViewModel {
        +MoodCounts Last30Days
        +MoodCounts Last12Months
        +void Update(MoodSummary s)
    }
    class EntryViewModel {
        +DateOnly Date
        +string Heading
        +InspirationViewModel Inspiration
        +ObservableCollection~EntryTabViewModel~ Tabs
        +EntryTabViewModel SelectedTab
        +ObservableCollection~TagChipViewModel~ Tags
        +IReadOnlyList~MoodChoiceViewModel~ MoodChoices
        +bool CanSetMood
        +bool IsDirty
        +string SaveStatus
        +IAsyncRelayCommand ChooseMoodCommand
        +IRelayCommand AddTabCommand
        +IRelayCommand RemoveTabCommand
        +IRelayCommand AddTagCommand
        +Task SaveAsync()
        +Task FlushAsync()
    }
    class MoodChoiceViewModel {
        +Mood Mood
        +string Label
        +bool IsSelected
    }
    class EntryTabViewModel {
        +string Title
        +EntryTab Model
        +IDocumentEditorHost? Host
        +IAsyncRelayCommand InsertImageCommand
        +IAsyncRelayCommand AttachFileCommand
        +Task LoadImageAsync(BlobRef blob, int width)
    }
    class InspirationViewModel {
        +string VerseText
        +string VerseReference
        +string QuoteText
        +string QuoteAuthor
        +string QuoteLabel
        +IRelayCommand CopyVerseCommand
        +IRelayCommand CopyQuoteCommand
        +IRelayCommand CopyBothCommand
    }
    class SearchViewModel {
        +string QueryText
        +ObservableCollection~SearchHitViewModel~ Results
        +ObservableCollection~string~ TagSuggestions
        +event ResultOpened
    }
    class UnlockViewModel {
        +bool CanUseHello
        +bool IsHelloPending
        +bool ShowMainDisplayHint
        +string VersionText
        +IAsyncRelayCommand TryHelloCommand
        +event Unlocked
        +void CancelPendingHello()
    }
    class SettingsViewModel {
        +ObservableCollection~CheckItem~ Books
        +ObservableCollection~QuoteCategorySection~ CategorySections
        +AutoLockOption AutoLock
        +string VaultFolder
        +string VersionText
        +IAsyncRelayCommand RegenerateRecoveryCodeCommand
        +IAsyncRelayCommand EnrollHelloCommand
        +IAsyncRelayCommand ImportVersesCommand
        +IAsyncRelayCommand ImportQuotesCommand
        +IAsyncRelayCommand SaveCommand
    }
    class ISettingsHost {
        <<interface>>
    }
    class SettingsActions
    class AppController {
        +void Start()
        +void OnApplicationExit()
    }
    class LockCoordinator {
        +bool IsExiting
        +TimeSpan WaitLimit
        +event StatusChanged
        +event LockProblem
        +void BeginMonitoring(TimeSpan? timeout)
        +void EndMonitoring()
        +void Tick()
        +Task LockAsync(LockReason reason)
        +bool LockAndWait(LockReason reason)
        +Task LockForExitAsync()
        +void Shutdown()
    }
    class MainWindowCloser {
        +bool IsExiting
        +void Detach()
    }
    class JournalEditor {
        <<WPF control>>
        +RichTextBox TextBox
        +JournalDocument Capture()
        +void InsertImage(ImageBlock b)
        +void InsertAttachment(AttachmentBlock b)
    }
    class JournalDocumentEditor {
        +void AttachClipboard(canPaste, pasted)
        +JournalDocument? SelectionFragment()
        +void InsertFragment(JournalDocument f)
        +void InsertBlockAtCaret(Block b)
        +void ToggleChecklist()
        +bool ToggleChecked()
        +bool Indent(int delta)
        +void SetParagraphStyle(ParagraphStyle s)
        +FormattingState ReadFormatting()
    }
    class JournalClipboard {
        <<static>>
        +string Format
        +void AddTo(data, fragment)
        +JournalDocument? Read(data)
        +string PlainText(fragment)
    }
    class FlowDocumentMapper {
        +FlowDocument ToFlowDocument(JournalDocument d)
        +JournalDocument FromFlowDocument(FlowDocument f)
        +JournalDocument FromRange(FlowDocument f, start, end)
    }
    class BlockEvents {
        <<routed events>>
        +ContentEditedEvent
        +AttachmentActionEvent
        +ImageActionEvent
        +ImageLoadEvent
    }
    class ImageBlockView {
        <<Control>>
        +string Caption (two-way)
        +ImageBlock ToBlock()
    }
    class AttachmentChip {
        <<Control>>
        +string Description (two-way)
        +AttachmentBlock ToBlock()
    }
    class ChecklistCheckBox {
        <<Control>>
    }
    class MoodFace {
        <<FrameworkElement>>
        +Mood Mood
    }
    class MonthCalendarControl {
        <<UserControl>>
    }

    MainViewModel *-- CalendarViewModel
    MainViewModel *-- SearchViewModel
    MainViewModel *-- MoodSummaryViewModel
    MainViewModel o-- EntryViewModel
    CalendarViewModel *-- "42" DayCellViewModel
    EntryViewModel *-- "1..*" EntryTabViewModel
    EntryViewModel *-- InspirationViewModel
    EntryViewModel *-- "3" MoodChoiceViewModel
    SettingsViewModel --> ISettingsHost
    ISettingsHost <|.. SettingsActions
    AppController --> LockCoordinator
    AppController --> MainWindowCloser
    AppController --> SettingsActions
    AppController ..> UnlockViewModel
    AppController ..> MainViewModel
    SettingsActions --> LockCoordinator
    MainWindowCloser ..> LockCoordinator : lock for exit, shut down
    MonthCalendarControl ..> CalendarViewModel : binds
    JournalEditor ..> EntryTabViewModel : binds
    JournalEditor --> FlowDocumentMapper
    JournalEditor --> JournalDocumentEditor : editing, clipboard
    JournalDocumentEditor --> FlowDocumentMapper
    JournalDocumentEditor ..> JournalClipboard
    FlowDocumentMapper ..> ImageBlockView : BlockUIContainer
    FlowDocumentMapper ..> AttachmentChip : BlockUIContainer
    FlowDocumentMapper ..> ChecklistCheckBox : InlineUIContainer
    ImageBlockView ..> BlockEvents : raises
    AttachmentChip ..> BlockEvents : raises
    JournalEditor ..> BlockEvents : handles its own
    MonthCalendarControl ..> MoodFace : small face
```

**How images sit between lines of text.** `FlowDocumentMapper` turns each `ImageBlock` into a `BlockUIContainer` that hosts an `ImageBlockView` (the picture with its caption box underneath) and tags the container with the block's id. Paragraphs map to `Paragraph`/`Run` with font family, size, colors, bold, italic, underline and strikethrough (`TextDecorations`). Headings map to styled paragraphs. Bulleted and numbered paragraphs map to nested WPF `List` elements (`MarkerStyle` Disc or Decimal). Checklist items map to a `Paragraph` whose first inline is an `InlineUIContainer` holding a `ChecklistCheckBox`, so the tick sits on the same line as the text and moves with it. A ticked item's strikethrough is on the paragraph for display only, not stored in its runs; the checkbox paints the editor's background under itself and the gap before the text, so the line shows only through the text (ISS-014). Inserting an image at the caret splits the current paragraph and places the image block between the two halves. Mapping back walks the `FlowDocument` and rebuilds the block list, so the stored format never contains XAML. Images are decoded at display size (`DecodePixelWidth`) to keep memory low; the original bytes stay encrypted in the blob store.

**Blocks and their editor.** WPF re-creates embedded controls from XAML on undo, so they can't be wired up one by one. Blocks raise routed events (`BlockEvents`) that bubble to the editor holding them, including an image asking for its bitmap (`ImageLoadEvent`), which the editor answers from its open tab. Each editor therefore hears only its own blocks, and a closed day gives no answer, so nothing can load after a lock (ISS-019).

**Copy and paste.** WPF's copy leaves out embedded controls and the journal's heading marker. So `JournalDocumentEditor` also puts the selection on the clipboard in the journal's own format (`JournalClipboard.Format`, the document model as JSON) and reads it back on paste and drop, as one undo step. Pasted images and files refer to the same vault file, so cutting and pasting never loses them. Other apps get the standard formats, with checklist lines as ☐ and ☑ (ISS-010).

**Calendar highlighting.** `CalendarViewModel.Refresh` gets the dates that have entries, the dates with open items and the moods from the catalog, and sets them on each of the 42 cells. The control styles `HasEntry` with a filled accent background, `IsToday` with a ring, `IsSelected` with a bold outline, and days outside the month in a muted color. A future day with an entry (`IsFuture && HasEntry`) gets a dashed outline so planned days stand out, and a small dot marks days with open checklist items. A day with a mood is tinted instead of highlighted (light red for sad, light green for happy, normal background for neutral) and shows a small face, so the mood doesn't depend on colour. Saving or deleting an entry raises `JournalService.EntryChanged`, which refreshes the month and the mood counts.

**Locking and exiting.** `LockCoordinator` is the one place that decides when to lock:

- **Auto-lock:** it watches input in the app and runs `AutoLockPolicy` on a timer.
- **Windows lock and sleep:** it listens to `SystemEvents`. On sleep and on Windows shutdown it uses `LockAndWait`, which returns once the save and lock have finished or after 5 seconds. .NET delivers these notifications on the UI thread, and the save continues on that thread, so `LockAndWait` keeps it processing work while waiting rather than blocking it (ISS-024).
- **The close button:** `MainWindowCloser` cancels the window's first close, leaves the closing event, saves and locks, then closes the window and shuts down, even if saving failed. WPF refuses to close a window from inside its own closing event (ISS-016).

When the lock finishes, `AppController` clears everything that holds journal content and returns to the unlock window, unless the app is exiting.

## 10. Cross-cutting concerns

| Concern | Approach |
|---|---|
| **Composition** | `AppController` wires the app by hand and runs the window flow (setup → unlock → main window → unlock). `LockCoordinator` decides when to lock or exit: auto-lock, Windows lock and sleep, the Lock button, closing. `SettingsActions` carries out what the Settings window asks for. Vault-dependent services are created after unlock; locking disposes them and returns to the unlock window. |
| **Single instance** | Named mutex; a second launch activates the existing window. |
| **Auto-lock** | `LockCoordinator` watches input in the app; `SystemEvents.SessionSwitch` and power events lock immediately. Unsaved changes are saved before locking (§9). |
| **Error handling** | A file that fails to decrypt is reported for that day only ("This entry could not be read; it may be damaged") and never overwritten. Global handler logs and shows a friendly message. |
| **Logging** | `Microsoft.Extensions.Logging` abstractions with the app's own `FileLoggerProvider`, writing a daily file in `%LOCALAPPDATA%\Journal\logs`. Never logs entry content, tags, captions or key material. |
| **Settings** | `%APPDATA%\Journal\settings.json` (vault location, auto-lock, inspiration options). Contains no journal content. |
| **Backup** | *Export backup* copies the encrypted vault folder to a zip. Restoring needs the recovery code (or the same PC's Windows Hello). |
| **Accessibility** | Keyboard navigation for calendar (arrow keys, PgUp/PgDn months), editor shortcuts (Ctrl+B/I/U, Ctrl+Shift+X strikethrough, Ctrl+Shift+L checklist, Ctrl+Enter tick item, Ctrl+F focuses search), AutomationProperties on custom controls. |
| **Version** | One place: `<Version>` in `Directory.Build.props`. `AppInfo` reads it from the running program for Settings and the unlock window. A `v*` tag builds the Windows zip and publishes a GitHub Release (`.github/workflows/release.yml`). |
| **Theming** | Light theme in v1 with all colors in a resource dictionary so a dark theme can be added later. |

## 11. Design patterns used

| Pattern | Where |
|---|---|
| MVVM | All views/view models |
| Facade | `JournalService` hides repositories, blob store, catalog and index from view models |
| Repository | `IEntryRepository`, `IBlobStore`, `ICatalogStore` |
| Strategy | `IKeyProtector` (unlock methods), `ISelectionStrategy` (verse/quote choice) |
| Composite + Visitor | `JournalDocument` / `DocBlock` hierarchy; `IDocBlockVisitor` for mapping to the editor; blocks answer `IsEmpty` and `ReferencedBlobs` themselves |
| Adapter | `FlowDocumentMapper` (domain ⇄ WPF), `WinRtHelloCredentialApi` (WinRT ⇄ testable interface) |
| Observer | `JournalService.EntryChanged`, `VaultSession.Locked/Unlocked`, `LockCoordinator.StatusChanged`, routed `BlockEvents` |
| Value Object | `Tag`, `BlobRef`, `Verse`, `Quote`, `RecoveryCode`, `DailyInspiration` |

## 12. Test strategy (detail is in [TestCases.md](TestCases.md))

| Level | Project | Examples |
|---|---|---|
| Unit | `Journal.Core.Tests` | Entry/tab/tag invariants, document text extraction, query parser, search ranking and snippets, verse/quote selection determinism, auto-lock timing with a fake clock |
| Integration | `Journal.Infrastructure.Tests` | Encrypt/decrypt round trip, tamper and swap detection, wrong-key failure, recovery-code wrap/unwrap, file vault save/load/delete in a temp folder, catalog rebuild, chunked large blobs, atomic writes |
| UI logic | `Journal.App.Tests` (STA) | FlowDocument ⇄ model round trips (fonts, colors, strikethrough, nested bulleted/numbered lists, checklists with ticked state, image between paragraphs, captions, attachments), calendar grid for any month, view models against fakes, copy and paste, locking and exit, sleep with a real vault under a watchdog, real windows shown off screen for layout |
| Manual | Checklist | Real Windows Hello enrol/unlock/cancel and where its dialog appears, recovery on a second PC, Win+L, a PC really going to sleep |

Target: ≥ 85% line coverage on `Journal.Core`.

## 13. Third-party packages

| Package | Use | License |
|---|---|---|
| CommunityToolkit.Mvvm | MVVM source generators, commands | MIT |
| Microsoft.Extensions.Logging.Abstractions | Logging interfaces | MIT |
| System.Security.Cryptography.ProtectedData | DPAPI for the fallback unlock | MIT |
| xUnit, xunit.runner.visualstudio, Microsoft.NET.Test.Sdk | Tests (fakes are hand-written) | Apache-2.0 / MIT |
| coverlet.collector | Coverage | MIT |

Crypto uses only built-in .NET (`AesGcm`, `HKDF`, `Rfc2898DeriveBytes.Pbkdf2`, `HMACSHA256`, `RandomNumberGenerator`). Windows Hello uses the built-in WinRT projection via the `net10.0-windows10.0.19041.0` target framework.

## 14. Risks

| Risk | Impact | Mitigation |
|---|---|---|
| Losing both the PC's Windows Hello key and the recovery code | Journal is unrecoverable (by design, nobody else can open it either) | Setup requires you to confirm part of the recovery code before continuing; Settings can show reminders and regenerate the code while unlocked. |
| Windows Hello prompt opening behind the WPF window | Confusing unlock | Allow the dialog to the foreground before the call, and say how to find it while it waits (§6.5, ISS-023). Windows gives no way to place the key-signing dialog. |
| Switching days is slower than NFR-PERF-02 asks | About 1.1 s against 300 ms for text, 1.3 s against 1 s with images (TC-NFR-14) | Logged as ISS-026: part of it is an unneeded save on every switch. |
| A slow save when the PC sleeps | The wait stops at 5 seconds, so the PC may sleep before the journal has locked | Normally about 0.1 second; past the limit it is logged, and the lock finishes on waking (ISS-024). |
| Very large attachments | Memory and save time | Streamed, chunked encryption; 250 MB limit per file. |
| `RichTextBox` quirks with embedded controls (undo, copy/paste of image blocks) | Lost captions or images on paste | The journal's own clipboard format (§9, ISS-010); covered by automated copy-and-paste tests. |

## 15. Decisions from review (v0.2)

| # | Question | Decision |
|---|---|---|
| 1 | Tab model (default untagged *Journal* tab, one tab per tag, entry tags without tabs) | Approved as designed. |
| 2 | One-time recovery code at setup | Approved. |
| 3 | Vault location | `%LOCALAPPDATA%\Journal\Vault` by default; the setup wizard offers your OneDrive folder as a one-click option, and Settings can move the vault later. |
| 4 | Bible translation | **World English Bible** built in instead of KJV. The Oxford edition's text (NRSV) is copyrighted and can't be shipped in the app; it can be used through *Import verses* with text you own. |
| 5 | Editor features | Added fonts, font size, text and highlight colors, numbered lists, checklists and strikethrough (§4, §5, §9). |
| 6 | Future dates | Not blocked. The journal doubles as a planner: future entries are highlighted with a dashed outline and `is:open` finds unticked checklist items (§8.1, §9). |
