# Journal: Requirements Specification

| | |
|---|---|
| **Version** | 1.0 (approved; updated during build) |
| **Date** | 2026-10-15 |
| **Based on** | Architecture & Design v0.2 (approved) |

## 1. Conventions

- Each requirement has a unique ID: `FR-<area>-<nn>` for functional, `NFR-<area>-<nn>` for non-functional.
- **Priority:** **M** = Must (v1 is not done without it), **S** = Should (planned for v1, can slip), **C** = Could (only if time allows).
- "The app" is the Journal desktop application. "The user" is you, the single owner of the journal. "Vault" is the encrypted folder holding the journal (Design §6.2).
- Every requirement is verified by at least one test case in *TestCases.md* (see its traceability matrix).

## 2. Functional requirements

### 2.1 Vault setup and unlocking (SEC)

| ID | Requirement | Pri |
|---|---|---|
| FR-SEC-01 | On first launch with no vault, the app shall run a setup wizard that creates a new vault. | M |
| FR-SEC-02 | The setup wizard shall offer `%LOCALAPPDATA%\Journal\Vault` as the default location, a one-click OneDrive location when OneDrive is detected, and a folder picker. | M |
| FR-SEC-03 | Setup shall enroll a Windows Hello key credential for the vault and verify that signing the vault challenge twice yields identical signatures before relying on it. | M |
| FR-SEC-04 | If the signature check in FR-SEC-03 fails, setup shall fall back to the Hello-gated DPAPI protector and tell the user it is weaker. | S |
| FR-SEC-05 | If Windows Hello is not set up on the PC, setup shall explain how to enable it and allow the user to continue in recovery-code-only mode. | M |
| FR-SEC-06 | Setup shall generate a recovery code of 25 Crockford base32 characters (5 groups of 5), display it once, and offer *Copy* and *Save to file*. | M |
| FR-SEC-07 | Setup shall not finish until the user re-types the last group of the recovery code correctly. | M |
| FR-SEC-08 | With an existing vault (at launch and after every lock), the unlock window shall offer **Unlock with Windows Hello** as its default button. Windows Hello shall be asked only when the user clicks it (or presses Enter), never automatically; on success the journal opens with no password. | M |
| FR-SEC-09 | If Windows Hello fails, is cancelled, or is unavailable, the unlock window shall offer *Try Windows Hello again* and *Use recovery code*. | M |
| FR-SEC-10 | Entering a valid recovery code shall unlock the vault. Input shall ignore case, spaces and dashes, and treat `O` as `0` and `I`/`L` as `1`. | M |
| FR-SEC-11 | An invalid recovery code shall be rejected with a message and no information about which part was wrong. After 5 failed attempts, further attempts shall be delayed by 30 seconds. | M |
| FR-SEC-12 | After unlocking with the recovery code on a PC with no Hello slot for this machine, the app shall offer to enroll Windows Hello on this PC. | M |
| FR-SEC-13 | The app shall lock automatically after a configurable period without keyboard or mouse input in the app (default 10 minutes, range 1–120, or *Never*). | M |
| FR-SEC-14 | The app shall lock immediately when Windows is locked (Win+L), the session is switched, or the PC sleeps. | M |
| FR-SEC-15 | A *Lock* button and the shortcut Ctrl+L shall lock the journal immediately. | M |
| FR-SEC-16 | Before locking, the app shall save any unsaved changes. | M |
| FR-SEC-17 | Locking shall clear all journal content from the window, discard decryption keys from memory, delete decrypted temporary attachment files, and show the unlock window. | M |
| FR-SEC-18 | Settings shall allow, while unlocked: regenerating the recovery code (the old one stops working), re-enrolling Windows Hello, and removing the Hello slot of another PC. | M |
| FR-SEC-20 | The app shall never leave a Windows Hello request waiting that the user can't see: tests and tools shall not start the app; closing the unlock or setup window, exiting, or starting a recovery-code unlock shall cancel a waiting request; and the recovery code shall work while a request is waiting. (Windows doesn't let an app close the dialog itself; see ISS-007.) | M |
| FR-SEC-19 | Only one instance of the app shall run; launching again shall bring the existing window to the front. | S |

### 2.2 Entries and days (ENT)

| ID | Requirement | Pri |
|---|---|---|
| FR-ENT-01 | Each calendar date shall have at most one journal entry. | M |
| FR-ENT-02 | The user shall be able to open and edit an entry for any date: past, today or future. | M |
| FR-ENT-03 | After unlocking, the app shall open today's date. | M |
| FR-ENT-04 | The entry page shall show the full date as a heading, e.g. "Thursday, October 15, 2026". | M |
| FR-ENT-05 | Changes shall be saved automatically 2 seconds after the last edit, and also when the user switches date, switches tab, locks, or closes the app. | M |
| FR-ENT-06 | The status bar shall show the save state: "Saving…", "Saved hh:mm", or an error. | M |
| FR-ENT-07 | An entry whose tabs contain no text, images or files and that has no extra tags shall not be stored; if it was stored before, it shall be deleted. | M |
| FR-ENT-08 | The user shall be able to delete a day's entry after confirming; the entry and its images and files shall be removed from the vault. | M |
| FR-ENT-09 | If an entry file cannot be decrypted or read, the app shall show an error for that day only, keep the file untouched, and keep every other day usable. | M |

### 2.3 Editor (EDT)

| ID | Requirement | Pri |
|---|---|---|
| FR-EDT-01 | Each tab shall contain a rich-text editor that behaves like a simple word processor (typing, selection, cut/copy/paste, undo/redo). | M |
| FR-EDT-02 | The user shall be able to apply bold, italic, underline and strikethrough to selected text, by toolbar and by Ctrl+B, Ctrl+I, Ctrl+U and Ctrl+Shift+X. | M |
| FR-EDT-03 | The user shall be able to choose the font family from the fonts installed in Windows. | M |
| FR-EDT-04 | The user shall be able to choose a font size from 8 to 72 points. | M |
| FR-EDT-05 | The user shall be able to set text color and highlight color from a 16-swatch palette or a custom color picker, and reset either to default. | M |
| FR-EDT-06 | The user shall be able to apply Heading 1, Heading 2 and Normal paragraph styles. | M |
| FR-EDT-07 | The user shall be able to create bulleted and numbered lists, nested up to 4 levels with Tab / Shift+Tab. Numbering shall update automatically when items are added, removed or moved. | M |
| FR-EDT-08 | The user shall be able to create checklist items (Ctrl+Shift+L). Each item shows a checkbox that can be ticked with the mouse or Ctrl+Enter. | M |
| FR-EDT-09 | Ticked checklist items shall be shown with strikethrough; unticking removes it. | S |
| FR-EDT-10 | Toolbar controls shall reflect the formatting at the caret or selection. | S |
| FR-EDT-11 | All formatting, lists, checklist states, images, captions and attachments shall be preserved exactly after saving and reopening. | M |
| FR-EDT-12 | Pasting text from other applications shall keep bold, italic, underline, lists and line breaks; other formatting may be dropped. | S |

### 2.4 Images (IMG)

| ID | Requirement | Pri |
|---|---|---|
| FR-IMG-01 | The user shall be able to insert an image at the caret from a file picker, from the clipboard (Ctrl+V), or by dragging an image file onto the editor. | M |
| FR-IMG-02 | Supported formats: PNG, JPEG, GIF (first frame), BMP, TIFF, and HEIC/WebP where Windows has the codec. | M |
| FR-IMG-03 | An inserted image shall appear on its own line between paragraphs; inserting in the middle of a paragraph splits it around the image. | M |
| FR-IMG-04 | Every image shall have a caption field directly below it, editable in place. | M |
| FR-IMG-05 | Images shall display scaled to fit the editor width while keeping the aspect ratio; the user can set a smaller display width by choosing Small / Medium / Full from the image's right-click menu. (Corner dragging is deferred.) | S |
| FR-IMG-06 | The original image bytes shall be stored encrypted; display shall not alter the stored original. | M |
| FR-IMG-07 | Images shall be deletable like any other content (select + Delete), and undo shall restore them with their caption. | M |
| FR-IMG-08 | Double-clicking an image shall open it full size in a viewer window inside the app. | S |
| FR-IMG-09 | Images larger than 50 MB shall be rejected with a message. | M |

### 2.5 Data files (ATT)

| ID | Requirement | Pri |
|---|---|---|
| FR-ATT-01 | The user shall be able to attach any file at the caret, by toolbar button or by dragging a non-image file onto the editor. | M |
| FR-ATT-02 | An attachment shall appear in the text as a chip showing icon, file name, size and an editable description. | M |
| FR-ATT-03 | Double-clicking an attachment shall decrypt it to a private temporary folder and open it with its default Windows application. | M |
| FR-ATT-04 | The attachment's context menu shall offer *Open*, *Save As…*, *Edit description* and *Remove*. | M |
| FR-ATT-05 | Files larger than 250 MB shall be rejected with a message. | M |
| FR-ATT-06 | Decrypted temporary files shall be deleted on lock, on exit, and (if left over after a crash) at the next startup. | M |
| FR-ATT-07 | Changes the user makes to an opened temporary copy are not saved back to the journal; the app shall say so the first time a file is opened. | S |

### 2.6 Tabs and tags (TAB / TAG)

| ID | Requirement | Pri |
|---|---|---|
| FR-TAB-01 | Every entry shall have a first, untagged tab titled *Journal*. | M |
| FR-TAB-02 | The user shall be able to add a tab with a "+" button; the app asks for a tag (with autocomplete of existing tags) and titles the tab with that tag. | M |
| FR-TAB-03 | An entry shall not have two tabs with the same tag (case-insensitive); choosing an existing tab's tag switches to that tab instead. | M |
| FR-TAB-04 | The user shall be able to rename a tab, which changes its tag. | M |
| FR-TAB-05 | The user shall be able to delete a tab other than *Journal*; if it has content the app asks for confirmation. | M |
| FR-TAB-06 | The user shall be able to reorder tabs by dragging, except *Journal*, which stays first. | C |
| FR-TAB-07 | Each tab shall have its own independent document (text, images, files). | M |
| FR-TAG-01 | The user shall be able to add tags to an entry from the tag bar without creating a tab. | M |
| FR-TAG-02 | An entry's tags shall be the union of its tab tags and its tag-bar tags; the tag bar shows all of them. | M |
| FR-TAG-03 | Tags shall be trimmed, case-insensitive for matching, 1–40 characters, and may not contain commas or the `:` character. Display keeps the case first used. | M |
| FR-TAG-04 | On the tag bar, tags that belong to a tab shall show a tab icon and cannot be removed there; the user removes or renames the tab instead. | M |
| FR-TAG-05 | Clicking a tag chip shall run a search for that tag. | S |

### 2.7 Calendar (CAL)

| ID | Requirement | Pri |
|---|---|---|
| FR-CAL-01 | The main window shall display a month calendar showing the current month on startup. | M |
| FR-CAL-02 | The calendar shall show a 6×7 grid with weekday headings, starting on Sunday (setting: Sunday or Monday). | M |
| FR-CAL-03 | Days that have a journal entry shall be highlighted. | M |
| FR-CAL-04 | Future days that have an entry shall additionally show a dashed outline. | S |
| FR-CAL-05 | Days with unticked checklist items shall show a small dot. | S |
| FR-CAL-06 | Today shall be marked distinctly from the selected day; days outside the displayed month shall be muted. | M |
| FR-CAL-07 | Previous / Next buttons and PgUp / PgDn shall change month; a *Today* button returns to the current month and selects today. | M |
| FR-CAL-08 | Clicking a day (or arrow keys + Enter) shall open that day's entry. | M |
| FR-CAL-09 | Highlights shall update immediately when an entry is created or deleted. | M |

### 2.8 Search (SRCH)

| ID | Requirement | Pri |
|---|---|---|
| FR-SRCH-01 | A search box shall search all entries; results appear as the user types (after 250 ms pause). | M |
| FR-SRCH-02 | Plain words shall match body text, image captions, attachment file names and descriptions, and tags. | M |
| FR-SRCH-03 | Matching shall be case-insensitive and accent-insensitive, and a word shall match the start of words (`gard` matches *garden*). | M |
| FR-SRCH-04 | Multiple words shall all have to match (AND) within the same tab or entry tags. | M |
| FR-SRCH-05 | Text in double quotes shall match as an exact phrase. | M |
| FR-SRCH-06 | `tag:<name>` shall restrict results to entries with that tag; multiple `tag:` terms are combined with AND. | M |
| FR-SRCH-07 | `in:text`, `in:captions` and `in:files` shall restrict where words match. | S |
| FR-SRCH-08 | `from:<date>` and `to:<date>` (ISO `yyyy-mm-dd` or `today`) shall restrict the date range, inclusive. | S |
| FR-SRCH-09 | `is:open` / `is:done` shall restrict to entries with unticked / ticked checklist items. | S |
| FR-SRCH-10 | Results shall list date, tab, where it matched (text, caption, file, tag) and a snippet with matches highlighted, newest first. At most the newest 200 results are shown. | M |
| FR-SRCH-11 | Clicking a result shall open that day and tab. | M |
| FR-SRCH-12 | Unknown operators (e.g. `foo:bar`) shall be treated as plain words; malformed dates shall show an inline hint and be ignored. | M |
| FR-SRCH-13 | The search box shall autocomplete tag names after `tag:`. | S |
| FR-SRCH-14 | Ctrl+F shall focus the search box; Esc shall clear it. | S |

### 2.9 Daily verse and quote (INSP)

| ID | Requirement | Pri |
|---|---|---|
| FR-INSP-01 | Each day's page shall display a Bible verse with its reference and translation abbreviation. | M |
| FR-INSP-02 | Each day's page shall display a philosophy quote with its author (and work, if known). | M |
| FR-INSP-03 | The built-in verse list shall be from the World English Bible and contain at least 366 distinct verses. | M |
| FR-INSP-04 | The built-in quote list shall contain at least 366 distinct public-domain quotes from at least 20 authors, each labeled with a tradition. | M |
| FR-INSP-05 | Settings shall allow turning the verse and the quote on or off independently. | M |
| FR-INSP-06 | Settings shall allow filtering verses by book and quotes by tradition; if a filter leaves nothing, the app shall say so and use the unfiltered list. | S |
| FR-INSP-07 | Settings shall offer two selection modes: *by day of year* (same date each year gets the same item) and *random per day* (stable for a given date and vault). | M |
| FR-INSP-08 | The same date shall always show the same verse and quote for unchanged settings. | M |
| FR-INSP-09 | A *Shuffle* button shall pick a different verse and quote for the displayed day. | S |
| FR-INSP-10 | When an entry is saved, its verse and quote shall be stored with it and shown thereafter, regardless of later setting changes. | M |
| FR-INSP-11 | The user shall be able to import verse and quote lists from JSON or CSV and select them as the source. Imported lists are stored encrypted in the vault. | S |
| FR-INSP-12 | Import shall reject files with no valid rows and report the number of rows imported and skipped. | S |
| FR-INSP-13 | Every built-in verse shall be a complete passage that starts and ends a sentence (multi-verse ranges where needed). A saved day whose stored verse is only part of a sentence shall show the full passage. | M |
| FR-INSP-15 | The verse and quote card shall not crowd out the editor: each text area shows at most about seven lines (five when the window is shorter than 760 px) and scrolls beyond that, while the reference, author and Copy buttons stay visible. | M |
| FR-INSP-14 | The verse and quote text shall be selectable, and Copy buttons shall copy the verse (with reference and translation), the quote (with author, work and source edition), or both, for pasting into other documents. | M |

### 2.10 Settings and backup (SET)

| ID | Requirement | Pri |
|---|---|---|
| FR-SET-01 | A Settings window shall contain the options in FR-SEC-13, FR-SEC-18, FR-CAL-02 and FR-INSP-05…11, plus the vault location. | M |
| FR-SET-02 | Settings shall persist between runs. | M |
| FR-SET-03 | The user shall be able to move the vault to another folder; the app copies, verifies and then removes the old copy. | S |
| FR-SET-04 | *Export backup* shall write the encrypted vault to a single `.zip` file chosen by the user. | S |
| FR-SET-05 | *Open backup* shall restore a vault from such a zip into an empty folder; it then unlocks with the recovery code (or Windows Hello on the same PC). | S |

### 2.11 Appearance (UI)

| ID | Requirement | Pri |
|---|---|---|
| FR-UI-01 | The app shall have a quill logo, used as the program icon (Explorer, Start menu, shortcuts), as the title-bar, taskbar and Alt+Tab icon of every window, and shown in the main window's top bar and at the top of the unlock and setup windows. | S |

## 3. Non-functional requirements

| ID | Requirement | Pri |
|---|---|---|
| NFR-SEC-01 | All journal content (entries, tags, captions, images, files, catalog, imported lists) shall be encrypted at rest with AES-256-GCM using a random 256-bit data key. | M |
| NFR-SEC-02 | The data key shall never be written to disk unwrapped. | M |
| NFR-SEC-03 | Each encrypted file shall bind its kind, vault id and logical id (date or blob id) as associated data, so modified, swapped or renamed files fail to decrypt. | M |
| NFR-SEC-04 | Entry file names shall not reveal dates (HMAC-SHA256 of the date under a key derived from the data key). | M |
| NFR-SEC-05 | Recovery-code key derivation shall use PBKDF2-SHA256 with at least 600,000 iterations and a 16-byte random salt. | M |
| NFR-SEC-06 | Logs shall never contain entry text, tags, captions, file names, recovery codes or key material. | M |
| NFR-SEC-07 | Settings files outside the vault shall contain no journal content. | M |
| NFR-SEC-08 | Every nonce shall be 96 bits from a cryptographic RNG; no nonce is reused with the same key. | M |
| NFR-REL-01 | Writes shall be atomic (write temp file, flush, rename) so a crash or power loss never leaves a partially written entry, blob or catalog. | M |
| NFR-REL-02 | If the catalog is missing or unreadable, it shall be rebuilt from entry files at unlock. | M |
| NFR-REL-03 | Images and files no longer referenced by any saved entry shall be removed from the vault when the journal locks or unlocks, never while the editor could still undo their removal. | M |
| NFR-PERF-01 | Unlock to usable main window (after Hello verification) in ≤ 2 s with 3,650 entries on a typical PC. | M |
| NFR-PERF-02 | Switching days shall display the entry in ≤ 300 ms for entries without images, ≤ 1 s with 10 images. | M |
| NFR-PERF-03 | Search results shall appear in ≤ 200 ms over 3,650 entries. | M |
| NFR-PERF-04 | Typing latency in the editor shall stay under 50 ms; autosave runs off the UI thread. | M |
| NFR-PERF-05 | Attachments up to 250 MB shall be encrypted and decrypted by streaming, using under 64 MB of extra memory. | M |
| NFR-USE-01 | All main functions shall be usable from the keyboard; custom controls expose UI Automation names. | S |
| NFR-USE-03 | When Windows will show an authentication prompt on a different display from the app (Windows always shows the Windows Hello key-signing dialog on the main display), the app shall say where it will appear. Where Windows allows it (the consent check), the prompt shall open over the app's window. | S |
| NFR-USE-02 | The UI shall be usable at 100–200 % Windows display scaling and at a minimum window size of 1000×650. | M |
| NFR-COMP-01 | The app shall run on Windows 10 (19041 or later) and Windows 11, x64 and ARM64, on .NET 10. | M |
| NFR-MNT-01 | Code shall follow the layering in Design §3: `Journal.Core` has no references to WPF, WinRT or file-system-specific vault code. | M |
| NFR-MNT-02 | Every persisted format shall carry a `formatVersion` field. | M |
| NFR-MNT-03 | Line coverage of `Journal.Core` by automated tests shall be ≥ 85 %. | S |
| NFR-LIC-01 | All bundled verse and quote text shall be public domain, including the translation used for quotes originally in other languages (each quote records its source edition); all third-party packages shall be under permissive licenses (MIT, Apache-2.0, BSD). | M |

## 4. Assumptions and constraints

- Windows Hello (PIN at minimum) is available on the user's PC for password-free unlock; otherwise recovery-code-only mode is used.
- The user keeps the recovery code somewhere safe. Losing both the PC's Hello key and the recovery code makes the journal unrecoverable; this is intended.
- If the vault is in OneDrive, only one PC should have the journal open at a time. Simultaneous editing from two PCs is not supported in v1; conflicting copies created by OneDrive are left untouched.
