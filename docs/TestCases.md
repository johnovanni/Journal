# Journal: Test Cases

| | |
|---|---|
| **Version** | 1.0 (implemented; automated cases pass) |
| **Date** | 2026-10-15 |
| **Based on** | Requirements v0.1, Architecture & Design v0.2 |

## 1. Approach

| Level | Code | Project / method | Runs |
|---|---|---|---|
| Unit | **U** | `Journal.Core.Tests` (xUnit). Fakes for `IClock`, `IKeyProtector`, repositories. | Every build |
| Integration | **I** | `Journal.Infrastructure.Tests` (xUnit). Real AES-GCM, PBKDF2, HKDF and file vault in a temp folder; fake Hello API. | Every build |
| UI logic | **A** | `Journal.App.Tests` (xUnit on an STA thread). `FlowDocumentMapper`, view models, calendar, editor commands without showing windows. | Every build |
| Manual | **M** | Checklist run on a real PC with Windows Hello (fingerprint or PIN), before each release. | Release |

**Test data:** `TestVaultBuilder` creates vaults with N generated entries (generated sample sentences, tags from a fixed list, images from small embedded PNGs, checklists) for repeatable integration and performance tests. All tests use a fixed `FakeClock` set to **2026-10-15 10:00 local** unless stated.

**Pass criteria for release:** all U, I and A tests pass; all *Must* manual cases pass; no open defect of severity High or above; `Journal.Core` line coverage ≥ 85 %.

## 2. Test cases

### 2.1 Vault setup and unlocking

| ID | Req | Lvl | Steps / input | Expected result |
|---|---|---|---|---|
| TC-SEC-01 | FR-SEC-01 | A | Start `ShellViewModel` with an empty settings store and no vault. | Setup wizard is shown, not the unlock window. |
| TC-SEC-02 | FR-SEC-02 | A | Open wizard's location page with a fake environment where OneDrive path exists; then where it does not. | Default `%LOCALAPPDATA%\Journal\Vault` preselected; OneDrive option visible only in the first case; *Browse…* always present. |
| TC-SEC-03 | FR-SEC-03 | I | `VaultInitializer.CreateVaultAsync` with fake Hello API returning deterministic signatures. | Vault created with a `windows-hello` key slot; fake API received two sign calls with the same challenge. |
| TC-SEC-04 | FR-SEC-04 | I | Same as TC-SEC-03 but fake API returns different signatures per call. | No `windows-hello` slot; a `hello-dpapi` slot is created; result flags `WeakerProtection = true`. |
| TC-SEC-05 | FR-SEC-05 | A | Wizard with fake Hello API `IsSupported = false`. | Explanation text with link to Windows Settings shown; *Continue with recovery code only* enabled; resulting vault has only a recovery slot. |
| TC-SEC-06 | FR-SEC-06 | U | Generate 1,000 recovery codes. | Each matches `^[0-9A-HJKMNP-TV-Z]{5}(-[0-9A-HJKMNP-TV-Z]{5}){4}$`; no duplicates; `TryParse(Formatted)` round-trips. |
| TC-SEC-07 | FR-SEC-07 | A | On the confirm page type a wrong last group, then the correct one (lowercase, with spaces). | *Finish* disabled for wrong input; enabled for correct input regardless of case and spaces. |
| TC-SEC-08 | FR-SEC-08 | I | Create vault, write 3 entries, dispose session, unlock with `WindowsHelloKeyProtector` using the same fake signature. | Unlock succeeds without any password; all 3 entries readable. |
| TC-SEC-09 | FR-SEC-09 | A | Unlock VM with Hello protector throwing `UserCancelled`, then `NotAvailable`. | Both cases show *Try Windows Hello again* and *Use recovery code*; no crash; vault stays locked. |
| TC-SEC-10 | FR-SEC-10 | I | Unlock with recovery code typed as lowercase, without dashes, with `O` for `0` and `l` for `1`. | Unlock succeeds in every variant. |
| TC-SEC-11 | FR-SEC-11 | A | Enter wrong recovery code 6 times with `FakeClock`. | Generic "That recovery code didn't work" message; 6th attempt rejected until clock advances 30 s; no slot or position info in message. |
| TC-SEC-12 | FR-SEC-12 | A | Unlock by recovery code with vault whose only Hello slot names machine `OTHER-PC`. | Offer to set up Windows Hello on this PC appears; accepting adds a slot for this machine name. |
| TC-SEC-13 | FR-SEC-13 | U | `AutoLockPolicy` timeout 10 min; activity at 10:00; tick at 10:09:59, then 10:10:00. Repeat with *Never*. | No lock at 10:09:59; lock at 10:10:00. With *Never*, no lock after 24 h. Activity at 10:05 resets the timer. |
| TC-SEC-14 | FR-SEC-14 | A | Raise fake `SessionSwitch(SessionLock)` and `PowerModeChanged(Suspend)` events. | Session locks on each event. |
| TC-SEC-14M | FR-SEC-14 | M | Unlock the journal, press Win+L, sign back in to Windows. | Journal shows the unlock window; Hello prompt required. |
| TC-SEC-15 | FR-SEC-15 | A | Execute `LockCommand`; also send Ctrl+L key gesture to the main view's input bindings. | Session locked in both cases. |
| TC-SEC-16 | FR-SEC-16, FR-ENT-05 | A | Type in the editor, lock within 1 s (before the autosave delay). | Repository received `SaveAsync` with the new text before `Locked` was raised. |
| TC-SEC-17 | FR-SEC-17 | I | Unlock, open an attachment (temp file created), lock. | Temp folder empty; `SecretKey` buffers are all zero; main VM's `CurrentEntry` is null; unlock window requested. |
| TC-SEC-18 | FR-SEC-18 | I | Regenerate recovery code; try old code; try new code. Re-enroll Hello. Remove other machine's Hello slot. | Old code fails, new code works; Hello slot replaced; removed slot no longer in `vault.json`; journal content unchanged. |
| TC-SEC-22 | FR-SEC-20 | A/I | Fake Windows Hello that waits for the user: cancel the request; use the recovery code while it waits; close the unlock window; close the setup window during creation; pass cancellation down to the Windows Hello API; check the UI test host isn't the real app. | Each request ends promptly and the fake sees the cancellation; the journal stays locked unless the recovery code was used; the test host never starts the app. |
| TC-SEC-23 | FR-SEC-08 | A | Show the real unlock window for a vault with a Windows Hello slot and wait; then click Unlock with Windows Hello. | No Windows Hello request until the button; then exactly one, and the journal unlocks. |
| TC-SEC-24 | NFR-USE-03 | M | On a PC with several displays: put the journal on a side display, lock it, click Unlock with Windows Hello; repeat with the unlock window on the main display. | Side display: the hint "Windows Hello opens on your main display" shows and the dialog opens on the main display. Main display: no hint. |
| TC-SEC-25 | NFR-USE-03 | A/I | Unlock and setup view models on and off the main display, with and without Windows Hello; the real unlock window moved between the main and a side display; the fallback unlock with a window handle. | Hint only when Windows Hello is offered and the window is on a side display; the real window shows and hides it as it moves; the fallback passes the window to Windows Hello. |
| TC-SEC-26 | FR-SEC-21 | A/M | Automated, on a real window with the app's close handling: close when locking finishes at once (nothing to save, the case that failed); close while a save takes time, clicking close again meanwhile; close when saving fails; close after the handling is detached (a normal lock). Manual: close Journal with the X after editing and without editing, then start it again. | Each close needs one click; save-and-lock runs once; the window closes and shutdown is requested exactly once; a failing save is reported and the app still shuts down; after detaching, the window just closes. Manually, Journal leaves Task Manager within a few seconds and starts again normally. |
| TC-SEC-19 | FR-SEC-19 | M | Launch the app twice. | One window; second launch brings the first to the front. |
| TC-SEC-20 | FR-SEC-03, FR-SEC-08 | M | On a real PC: run setup with fingerprint/face, close app, relaunch; check no Windows Hello prompt appears until Unlock with Windows Hello is clicked; unlock with biometric; repeat with PIN. | No prompt before the click; unlock works with biometric and with PIN. |
| TC-SEC-21 | FR-SEC-10, FR-SEC-12 | M | Copy the vault to a second PC (or new Windows user), unlock with recovery code, accept Hello enrollment, relaunch. | Recovery unlock works; after enrollment the second PC unlocks with Hello. |

### 2.2 Entries and days

| ID | Req | Lvl | Steps / input | Expected result |
|---|---|---|---|---|
| TC-ENT-01 | FR-ENT-01 | I | Save an entry for 2026-10-15, then save a different entry object for the same date. | Exactly one entry file for that date; reading returns the second version. |
| TC-ENT-02 | FR-ENT-02 | A | Select 2020-02-29, 2026-10-15 and 2031-12-31 and type text in each. | All three are editable and saved; no warning about past or future dates. |
| TC-ENT-03 | FR-ENT-03 | A | Unlock with `FakeClock` = 2026-10-15. | `CurrentEntry.Date` = 2026-10-15; calendar shows October 2026 with the 15th selected. |
| TC-ENT-04 | FR-ENT-04 | A | Open 2026-10-15 with culture en-US. | Heading = "Thursday, October 15, 2026". |
| TC-ENT-05 | FR-ENT-05 | A | Type, wait 1.9 s (fake scheduler): no save. Wait to 2.0 s: save. Type, then switch date / switch tab / close. | Save count 0 at 1.9 s, 1 at 2.0 s; each of switch-date, switch-tab and close causes a save. |
| TC-ENT-06 | FR-ENT-06 | A | Save succeeds at 10:42; then repository throws `IOException`. | Status "Saved 10:42"; then error status with retry; text remains in editor. |
| TC-ENT-07 | FR-ENT-07 | I | Create entry with text, save; clear all text and save again. Separately save an entry that was never non-empty. | First: entry file deleted, catalog no longer has the date. Second: no file written. |
| TC-ENT-08 | FR-ENT-08 | I | Entry with 1 image and 1 file; delete with confirmation; cancel in a second attempt. | Confirmed: entry, image blob and file blob removed. Cancelled: nothing removed. |
| TC-ENT-09 | FR-ENT-09 | I | Corrupt one byte of one entry file; unlock and open that day and another day. | That day shows "This entry could not be read"; file bytes unchanged on disk; editor read-only for that day; other days open normally. |

### 2.3 Editor

| ID | Req | Lvl | Steps / input | Expected result |
|---|---|---|---|---|
| TC-EDT-01 | FR-EDT-01, FR-EDT-13 | M | Type a paragraph, cut/paste a sentence, undo, redo. Copy a checklist (one item ticked), an image and a file to another day and paste; cut an image and paste it further down; drag a heading to another place; lock and unlock. Paste the checklist into Notepad. | Works as in WordPad. The pasted checklist, image and file match the originals; the cut image is still there after locking and unlocking; the dragged heading moved and kept its style. Notepad shows ☐ and ☑. |
| TC-EDT-02 | FR-EDT-02, FR-EDT-11 | A | Map a document with runs: bold, italic, underline, strikethrough, bold+italic+strikethrough to `FlowDocument` and back. | Round-trip model equals original; `TextDecorations` contain Underline / Strikethrough as expected. |
| TC-EDT-03 | FR-EDT-02 | A | Select text, execute the Ctrl+Shift+X command. | Selection gets strikethrough; executing again removes it. |
| TC-EDT-04 | FR-EDT-03, FR-EDT-04, FR-EDT-11 | A | Runs with `FontFamily = "Georgia"`, size 8, 12, 72; one run with defaults. Round trip. | Values preserved; default run keeps null font/size in the model. |
| TC-EDT-05 | FR-EDT-04 | A | Set size 7 and 73 via toolbar VM. | Values clamped to 8 and 72. |
| TC-EDT-06 | FR-EDT-05, FR-EDT-11 | A | Runs with foreground `#FFC00000`, highlight `#FFFFFF00`, then *Reset*. Round trip. | Colors preserved; after reset both null. |
| TC-EDT-07 | FR-EDT-06, FR-EDT-11 | A | Paragraphs with Heading1, Heading2, Normal. Round trip. | Styles preserved. |
| TC-EDT-08 | FR-EDT-07, FR-EDT-11 | A | Numbered list of 3 items with a nested bulleted list (2 items, level 1) under item 2, then a level-3 item. Round trip; then delete item 1. | Structure and levels preserved; WPF `List` markers Decimal/Disc; after deletion remaining numbers render as 1, 2. |
| TC-EDT-09 | FR-EDT-07 | A | Press Tab on a level-3 item; Shift+Tab on a level-0 item. | Level stays 3 (max 4 levels, 0–3); Shift+Tab at level 0 turns item into normal paragraph. |
| TC-EDT-10 | FR-EDT-08, FR-EDT-11 | A | Two checklist items, second ticked. Round trip. Toggle first via `ToggleChecked`. | `IsChecked` preserved; each item's first inline is a `CheckBox`; toggle sets first item checked. |
| TC-EDT-11 | FR-EDT-09 | A | Tick a checklist item, then untick. Render a ticked item with and without its strikethrough. | Rendered runs get strikethrough when ticked, lose it when unticked; stored run format unchanged (display-only). The checkbox and the gap before the text render identically with and without the strikethrough (no line through the checkbox, ISS-014); the text area differs. |
| TC-EDT-12 | FR-EDT-10 | A | Place caret in a bold Georgia 14 pt red run in a numbered list; call `SyncFromSelection`. | Toolbar VM: IsBold, Font Georgia, Size 14, red, CurrentList = Numbered. Mixed selection shows empty font/size. |
| TC-EDT-13 | FR-EDT-11 | A | Property-based test: 500 random documents (all block kinds, run formats, list levels). Round trip model → FlowDocument → model. | All equal original. |
| TC-EDT-14 | FR-EDT-11 | I | Save and reload through the encrypted repository a document with every feature. | Loaded document equals saved document. |
| TC-EDT-16 | FR-EDT-10 | A | Open a day in the real main window without clicking in the text. | Font box shows "Segoe UI" and size box "11". |
| TC-EDT-17 | FR-EDT-13 | A | Through the editor's Copy command and pasting handler (data captured, the Windows clipboard untouched): copy and paste plain text, bold/italic/underline, strikethrough, font, size, colour and highlight, a line break, Heading 1 and 2, a centred paragraph, bulleted and nested lists, a checklist (unticked, ticked, indented), an image with caption and size, and a file, each between two paragraphs. Also: part of one paragraph; a selection from the middle of one paragraph into a checklist item; a whole heading on its own; cut and paste of an image; paste over a selection, then undo; a drop; the plain text for other apps. | Every pasted document equals the original. Part of one plain paragraph is left to WPF's copy; across paragraphs only the selected text is kept, with the checklist tick. The cut image keeps its vault reference. Paste replaces the selection and one undo restores it. A drop inserts the content and lets the move finish. Plain text has "☐ " and "☑ " prefixes. Without the fix, 17 of these fail. |
| TC-EDT-15 | FR-EDT-12 | M | Paste from Word and from a web page: bold, italic, underlined text, a bulleted list and line breaks. | Those formats kept; nothing crashes; unknown styles dropped. |

### 2.4 Images

| ID | Req | Lvl | Steps / input | Expected result |
|---|---|---|---|---|
| TC-IMG-01 | FR-IMG-01 | A | `InsertImageCommand` with a file path; paste a `BitmapSource` from fake clipboard; simulate drop of a `.jpg`. | Each produces an `ImageBlock` at the caret and a blob in the store. |
| TC-IMG-02 | FR-IMG-02 | I | Add sample PNG, JPEG, GIF (animated), BMP, TIFF; add a `.txt` renamed `.png`. | Valid formats accepted with correct pixel size (GIF first frame); invalid file rejected with "This file isn't an image Windows can open". |
| TC-IMG-03 | FR-IMG-03 | A | Caret in the middle of "Hello world" after "Hello "; insert image. | Blocks: Paragraph "Hello ", ImageBlock, Paragraph "world". |
| TC-IMG-04 | FR-IMG-03, FR-EDT-11 | A | Document: paragraph, image, paragraph, image, image, paragraph. Round trip. | Order and count preserved; images are `BlockUIContainer`s between paragraphs. |
| TC-IMG-05 | FR-IMG-04 | A | Edit caption text in `ImageBlockView`; map back. | `ImageBlock.Caption` updated; `DocumentChanged` raised. |
| TC-IMG-06 | FR-IMG-05 | A | 4000 px wide image in a 700 px editor; choose *Small*. | Displayed width ≤ editor width, aspect kept; *Small* sets `DisplayWidth` and survives round trip. |
| TC-IMG-07 | FR-IMG-06 | I | Insert JPEG, save, export blob back. | Exported bytes SHA-256 equals original file; blob file on disk does not contain the JPEG header bytes `FF D8 FF`. |
| TC-IMG-08 | FR-IMG-07 | M | Select an image block, press Delete, then Ctrl+Z. | Image removed; undo restores it with its caption. |
| TC-IMG-09 | FR-IMG-08 | M | Double-click an image. | Full-size viewer opens inside the app; Esc closes it. |
| TC-IMG-10 | FR-IMG-09 | A | Insert 50 MB + 1 byte image (fake file system). | Rejected with size message; no blob written. |

### 2.5 Data files

| ID | Req | Lvl | Steps / input | Expected result |
|---|---|---|---|---|
| TC-ATT-01 | FR-ATT-01 | A | `AttachFileCommand` with `data.xlsx`; drop a `.csv` file onto editor. | `AttachmentBlock` at caret with file name, size, content type; blob stored. |
| TC-ATT-02 | FR-ATT-02, FR-EDT-11 | A | Round trip a document containing an attachment with description "tomato yields". | `AttachmentChip` shows name, size "24 KB", description; model preserved. |
| TC-ATT-03 | FR-ATT-03 | I | `ExportAttachmentToTempAsync`. | File created under the app's private temp folder with original name and identical bytes; folder ACL limited to current user. |
| TC-ATT-03M | FR-ATT-03 | M | Double-click an attached `.xlsx` and `.pdf`. | Opened in Excel / PDF viewer. |
| TC-ATT-04 | FR-ATT-04 | A | Execute *Save As…* to a fake path, *Edit description*, *Remove*. | File written with identical bytes; description updated; block removed. |
| TC-ATT-05 | FR-ATT-05 | A | Attach 250 MB + 1 byte file (fake file system). | Rejected with size message. |
| TC-ATT-06 | FR-ATT-06 | I | Create temp files; then (a) lock, (b) dispose app, (c) leave files and run startup cleanup. | Temp folder empty in all three cases. |
| TC-ATT-07 | FR-ATT-07 | A | Open attachment first time, then second time. | Notice "Changes to this copy aren't saved to your journal" shown once only. |
| TC-ATT-08 | FR-ATT-01, NFR-PERF-05 | I | Attach a 200 MB generated file; read it back while measuring peak managed memory. | Bytes identical; extra memory < 64 MB. |

### 2.6 Tabs and tags

| ID | Req | Lvl | Steps / input | Expected result |
|---|---|---|---|---|
| TC-TAB-01 | FR-TAB-01 | U | `JournalEntry.Create(date)`. | One tab, title "Journal", `Tag` null. |
| TC-TAB-02 | FR-TAB-02 | A | `AddTabCommand` with dialog returning "Family". | New tab titled "Family", selected; tag suggestions came from `AllTags()`. |
| TC-TAB-03 | FR-TAB-03 | U | Add tab "work", then "WORK". | Second call returns the existing tab; tab count unchanged. |
| TC-TAB-04 | FR-TAB-04 | U | Rename tab "work" to "career"; rename to an existing tab's tag. | First: tag changed. Second: `DuplicateTagException`, nothing changed. |
| TC-TAB-05 | FR-TAB-05 | U/A | Remove *Journal* tab; remove empty tab; remove tab with content (confirm, then cancel). | *Journal*: `InvalidOperationException`. Empty: removed with no prompt. With content: prompt shown; removed only on confirm. |
| TC-TAB-06 | FR-TAB-06 | A | Drag tab 3 before tab 2; try to drag a tab before *Journal*. | Order updated; *Journal* stays at index 0. |
| TC-TAB-07 | FR-TAB-07 | I | Different text, image and file in two tabs; save and reload. | Each tab's document unchanged and independent. |
| TC-TAG-01 | FR-TAG-01 | U | `AddTag("gratitude")` on an entry. | Tag present in `ExtraTags`; tab count unchanged. |
| TC-TAG-02 | FR-TAG-02 | U | Entry with tabs "family", "work" and extra tag "gratitude". | `AllTags()` = {family, work, gratitude}. |
| TC-TAG-03 | FR-TAG-03 | U | `Tag.Parse` with "  Family ", "family", "", 41 chars, "a,b", "a:b", 40 chars. | First two equal and display "Family"; empty, 41 chars, comma and colon rejected; 40 chars accepted. |
| TC-TAG-04 | FR-TAG-04 | A | Tag bar for entry with tab tag "work" and extra tag "gratitude". | "work" chip shows tab icon and no remove button; "gratitude" removable. |
| TC-TAG-05 | FR-TAG-05 | A | Click "family" chip. | Search text becomes `tag:family`; search executed. |

### 2.7 Calendar

| ID | Req | Lvl | Steps / input | Expected result |
|---|---|---|---|---|
| TC-CAL-01 | FR-CAL-01 | A | Unlock with clock 2026-10-15. | Calendar title "October 2026". |
| TC-CAL-02 | FR-CAL-02 | U/A | Build grid for Oct 2026 (Sunday start), Feb 2026 (Sunday start), Feb 2026 (Monday start), Mar 2026. | 42 cells each; Oct 2026 first cell Sun Sep 27; Feb 2026 Sunday start first cell Sun Feb 1; Monday start first cell Mon Jan 26; heading order matches setting. |
| TC-CAL-03 | FR-CAL-03 | A | Entries on Oct 5, 8, 13. Refresh. | `HasEntry` true only for those three cells. (Mood tints: TC-MOOD-03.) |
| TC-CAL-04 | FR-CAL-04 | A | Entries on Oct 13 and Oct 20 (today Oct 15). | Oct 20 `IsFuture && HasEntry` (dashed style); Oct 13 not future. |
| TC-CAL-05 | FR-CAL-05 | A | Oct 20 has an unticked checklist item, Oct 13 only ticked ones. | `HasOpenItems` true for Oct 20 only. |
| TC-CAL-06 | FR-CAL-06 | A | Select Oct 20 with today Oct 15. | Oct 15 `IsToday`, Oct 20 `IsSelected`; Sep 27–30 and Nov 1–7 `IsInCurrentMonth = false`. |
| TC-CAL-07 | FR-CAL-07 | A | Next ×3 from Oct 2026, Previous ×13, then Today. | Jan 2027; Dec 2025; then Oct 2026 with Oct 15 selected. PgUp/PgDn bindings invoke the same commands. |
| TC-CAL-08 | FR-CAL-08 | A | Click cell Oct 8; arrow-right then Enter. | `DateSelected` raised with Oct 8, then Oct 9; entry opened. |
| TC-CAL-09 | FR-CAL-09 | A | Type in empty Oct 22 and let autosave run; then clear it. | Oct 22 highlighted after first save; not highlighted after clearing. |
| TC-CAL-10 | FR-CAL-01, FR-CAL-03, FR-MOOD-03 | M | Visual check in light theme at 100 % and 150 % scaling, with sad, neutral and happy days. | Highlights, mood tints and small faces, today ring, selection and dashed future outline are clearly distinguishable. |

### 2.8 Search

Integration tests run against a fixture vault: Oct 13 (tab *family*: "Planted the garden with the kids", image caption "Garden at sunrise"), Sep 28 (*Journal*: "Prayer meeting at church", attachment `harvest-weights.xlsx` "tomato yields", checklist: "Buy seeds" open), Aug 2 (tag *family*, "Café visit after the walk"), Nov 3 2026 (future, checklist "Doctor appointment" done).

| ID | Req | Lvl | Steps / input | Expected result |
|---|---|---|---|---|
| TC-SRCH-01 | FR-SRCH-01 | A | Type "gar" then "garden" within 250 ms (fake scheduler). | One search executed, for "garden", 250 ms after the last keystroke. |
| TC-SRCH-02 | FR-SRCH-02 | I | Search `garden`, `sunrise`, `harvest`, `tomato`, `family`. | Oct 13 (text); Oct 13 (caption); Sep 28 (file name); Sep 28 (file description); Oct 13 and Aug 2 (tag). |
| TC-SRCH-03 | FR-SRCH-03 | U | Search `GARDEN`, `gard`, `cafe`, `arden`. | Oct 13; Oct 13; Aug 2 (accent-insensitive); no results (not a word start). |
| TC-SRCH-04 | FR-SRCH-04 | U | Search `garden kids`, `garden church`. | Oct 13; no results. |
| TC-SRCH-05 | FR-SRCH-05 | U | Search `"prayer meeting"`, `"meeting prayer"`. | Sep 28; no results. |
| TC-SRCH-06 | FR-SRCH-06 | U | `tag:family`, `tag:FAMILY garden`, `tag:family tag:work`. | Oct 13 + Aug 2; Oct 13; no results. |
| TC-SRCH-07 | FR-SRCH-07 | U | `in:captions garden`, `in:text sunrise`, `in:files tomato`. | Oct 13 (caption source); none; Sep 28. |
| TC-SRCH-08 | FR-SRCH-08 | U | `from:2026-09-01 to:2026-09-30 prayer`; `from:today` (clock Oct 15); `to:2026-08-02`. | Sep 28; Nov 3 only; Aug 2 (inclusive). |
| TC-SRCH-09 | FR-SRCH-09 | U | `is:open`, `is:done`. | Sep 28; Nov 3. |
| TC-SRCH-10 | FR-SRCH-10 | U | Search `garden` on 3 dates; snippet for a 2,000-character body with match in the middle. | Results newest first; one row per matching tab; `MatchSource` set; snippet ≤ 160 chars with "…" and the match marked. |
| TC-SRCH-11 | FR-SRCH-11 | A | Execute `OpenResultCommand` on the Oct 13 *family* result. | Date Oct 13 opened; *family* tab selected. |
| TC-SRCH-12 | FR-SRCH-12 | U | Parse `foo:bar garden`, `from:2026-13-45 garden`. | First: `foo:bar` is treated as the plain words foo and bar, plus garden. Second: date ignored, `Warnings` contains invalid date; term garden kept. |
| TC-SRCH-13 | FR-SRCH-13 | A | Type `tag:fa`. | Suggestions = ["family"]. |
| TC-SRCH-14 | FR-SRCH-14 | A | Ctrl+F gesture; Esc in search box. | Search box focused; text cleared and results emptied. |
| TC-SRCH-15 | FR-SRCH-02 | U | Save entry with new caption "Lighthouse"; search immediately; delete entry; search again. | Found after save; not found after delete (index updated incrementally). |

### 2.9 Daily verse and quote

| ID | Req | Lvl | Steps / input | Expected result |
|---|---|---|---|---|
| TC-INSP-01 | FR-INSP-01, FR-INSP-02, FR-INSP-17 | A | Open Oct 15 with defaults. Show a philosophy quote, a literature quote and a quote whose category is "Science". | Verse text, reference and "WEB" shown; quote text and author shown. The card label reads "PHILOSOPHY", "LITERATURE" and "SCIENCE" respectively. |
| TC-INSP-02 | FR-INSP-03 | I | Load embedded verse list. | ≥ 366 items; all distinct references; all `Translation = "WEB"`; no empty text; references parse as Book Chapter:Verse(-Verse). |
| TC-INSP-03 | FR-INSP-04 | I | Load embedded quote list. | ≥ 366 distinct texts; ≥ 20 distinct authors; every item has a tradition. |
| TC-INSP-16 | FR-INSP-16 | I | Load embedded quote list and take the Literature quotes. | Categories are exactly Literature and Philosophy; 394 philosophy quotes; ≥ 366 distinct literature texts, none also a philosophy quote; ≥ 30 authors; ≥ 8 traditions including "Latin American" and "African"; every literature quote has a work; both categories appear among the first 366 quotes (used by day-of-year mode). |
| TC-INSP-04 | FR-INSP-05 | A | Turn verse off; turn quote off. | Verse panel hidden; then quote panel hidden; layout has no empty gap. |
| TC-INSP-05 | FR-INSP-06, FR-INSP-17 | U/A | Filter verses to Psalms; quotes to Stoic. Filter to a book with no items. Turn off Literature, then Philosophy, then both. Add a "Science" quote to the content only. Load a stored quote saved without a category. In Settings, untick a category, tick traditions, save and reopen; open settings saved with a bare tradition. Tick Chinese under Philosophy only. Open the real Settings window. | Every selected verse from Psalms; every quote Stoic. Empty filter: falls back to full list and returns a warning. Category filters give only the ticked category; all off falls back with a warning; with defaults both categories appear. Science is listed with its tradition and can be shown with no code change. The stored quote reads as Philosophy. Settings show one section per category, Philosophy first, each with its own traditions alphabetically and every category ticked; choices save as "Category/Tradition" and reopen that way; a bare tradition from older settings shows ticked in each category that has it. With Philosophy › Chinese ticked, the only philosophy quotes are Chinese, and all literature quotes, Chinese or not, still appear. In the real window, Philosophy's section is above Literature's, the tradition lists run down columns alphabetically, the books run down columns in Bible order, and no tick box is cut off. |
| TC-INSP-06 | FR-INSP-07 | U | Day-of-year mode for 2026-10-15 and 2027-10-15. Random mode for same dates and two vault ids. | Day-of-year: same item both years. Random: different between years in this sample; differs between vault ids; stable on repeat. |
| TC-INSP-07 | FR-INSP-08 | U | Call `For(2026-10-15, settings)` 100 times. | Same verse and quote every call. |
| TC-INSP-08 | FR-INSP-09 | U | `Shuffle` 20 times for one date. | Never returns the item currently shown. |
| TC-INSP-09 | FR-INSP-10 | I | Save entry for Oct 15; change selection mode and filters; reopen Oct 15 and an empty Oct 16. | Oct 15 shows stored verse/quote unchanged; Oct 16 follows new settings. |
| TC-INSP-10 | FR-INSP-11 | I | Import a CSV of 10 verses (`reference,text,translation`) and JSON of 5 quotes; select them. Import quotes with and without a `category` column. | Sources selectable; items come from imported lists; vault file for imported lists is encrypted (no plaintext verse text on disk). A quote's category comes from the file, or is "Quote" when the file has none. |
| TC-INSP-13 | FR-INSP-13 | I/U | Check every built-in verse; open a saved day whose stored verse is 1 Corinthians 13:7 only. | Every verse starts with a capital letter and ends with sentence punctuation; the saved day shows 1 Corinthians 13:4-7. |
| TC-INSP-14 | FR-INSP-14 | A | Copy verse, copy quote, copy both; then copy while the clipboard is busy. | Clipboard holds the text plus "— reference (WEB)" / "— author, work, source"; status says what was copied; busy clipboard shows "Couldn't copy". |
| TC-INSP-15 | FR-INSP-15 | A | Open the real main window at 1240×820 with the longest verse (Ecclesiastes 3:1-8) and quote. | Card ≤ 230 px tall; the verse text area scrolls; Copy verse button visible. |
| TC-INSP-11 | FR-INSP-12 | I | Import CSV with 8 valid rows and 2 rows missing text; import a file with no valid rows. | "Imported 8, skipped 2"; second import rejected with message. |

### 2.10 Settings and backup

| ID | Req | Lvl | Steps / input | Expected result |
|---|---|---|---|---|
| TC-SET-01 | FR-SET-01 | A | Open `SettingsViewModel`. | Exposes auto-lock, week start, verse/quote options, imports, recovery code, Hello enrollment and vault location. |
| TC-SET-02 | FR-SET-02 | I | Change auto-lock to 20, week start Monday, quote off; recreate settings store from disk. | Values persisted. |
| TC-SET-03 | FR-SET-03 | I | Move vault with 50 entries to new folder; simulate copy failure in a second run. | Success: new folder unlocks with all entries, old folder removed. Failure: old vault untouched and still in use. |
| TC-SET-04 | FR-SET-04 | I | Export backup. | Zip contains `vault.json`, `catalog.jcat`, `entries/*`, `blobs/*`; no plaintext entry text found in zip bytes. |
| TC-SET-05 | FR-SET-05 | I | Restore zip to empty folder; unlock with recovery code. Restore into non-empty folder. | All entries readable; non-empty folder rejected. |

### 2.11 Appearance

| ID | Req | Lvl | Steps / input | Expected result |
|---|---|---|---|---|
| TC-UI-01 | FR-UI-01 | A | Read the app's icon resource; create the main, unlock, setup and settings windows. | Icon contains 16, 24, 32, 48, 64 and 256 px images; every window's Icon is set; main, unlock and setup windows show the quill logo. |
| TC-UI-02 | FR-UI-01 | M | Run the app at 100 % and 200 % display scaling; look at the taskbar, Alt+Tab, title bars, Explorer and the Start menu. | The quill shows everywhere, sharp at both scalings. |

### 2.12 Day mood

| ID | Req | Lvl | Steps / input | Expected result |
|---|---|---|---|---|
| TC-MOOD-01 | FR-MOOD-01 | U/A | Set Happy on an empty day, change to Sad, then clear it. Try a mood on a day after today, in the service and with the face buttons. | Each change is stored and reloads; clearing an otherwise empty day removes it. The service refuses a mood for a future day; on a future day the faces are disabled, the hint says a mood can be chosen once the day arrives, and clicking does nothing. |
| TC-MOOD-02 | FR-MOOD-02 | U/I | Save a day with a mood and one without; read a catalog entry written before moods; save a mood in a real vault. | The entry JSON has `"mood":"neutral"`; a day without a mood writes no mood field; the old catalog entry reads as no mood; the catalog round-trips the mood. In the real vault neither "mood" nor "happy" appears in plaintext, and the mood reloads. |
| TC-MOOD-03 | FR-MOOD-03 | A | Give three earlier days sad, neutral and happy; leave a fourth without; reopen a day. | Calendar cells carry Sad, Neutral, Happy and None; the days with a mood count as entries; the reopened day's face is selected; the screen-reader name ends with e.g. ", happy day". |
| TC-MOOD-04 | FR-MOOD-04 | U | With today 2026-10-15: moods on today, 29 and 30 days back, 2025-10-16 and 2025-10-15, plus a day with text only. | Last 30 days: 1 sad, 0 neutral, 1 happy. Last 12 months: 1 sad, 1 neutral, 2 happy (2025-10-15 and the text-only day aren't counted). |
| TC-MOOD-05 | FR-MOOD-05 | A | Click Happy, then Sad, then Sad again. | Each click saves at once (no autosave wait); the calendar cell and both counts follow each click; after the last click the day has no mood and, being empty, is removed. |
| TC-MOOD-06 | FR-MOOD-01, FR-MOOD-03, FR-MOOD-04 | A/M | Open the real main window at its smallest size (1000×650) and choose a mood. Then look at it in light theme at 100 % and 150 % scaling. | The three faces are visible to the right of the heading without overlapping it; the counts card shows six counts with the happy day counted. Faces, tints and counts are easy to read. |

### 2.13 Non-functional

| ID | Req | Lvl | Steps / input | Expected result |
|---|---|---|---|---|
| TC-NFR-01 | NFR-SEC-01, NFR-SEC-08 | I | Encrypt the same plaintext 10,000 times. | Ciphertexts all differ; nonces all unique and 12 bytes; decrypt returns plaintext. |
| TC-NFR-02 | NFR-SEC-01 | I | Create vault with entries containing the marker text `ZZQX-MARKER`, a caption, a tag and a file containing it; scan every byte of the vault folder (UTF-8 and UTF-16). | Marker not found anywhere. |
| TC-NFR-03 | NFR-SEC-02 | I | Scan `vault.json` and all files for the raw 32-byte data key. | Not found. |
| TC-NFR-04 | NFR-SEC-03 | I | (a) Flip one ciphertext bit; (b) flip one tag bit; (c) rename Oct 13's entry file to Oct 14's file name; (d) copy an entry file into another vault. | All four fail with `CryptographicException` → reported as unreadable; nothing overwritten. |
| TC-NFR-05 | NFR-SEC-03 | I | Decrypt with a wrong data key. | Fails; no partial plaintext returned. |
| TC-NFR-06 | NFR-SEC-04 | I | List `entries/` after saving 2026-10-15. | No file name contains `2026`, `1015` or any date pattern; name is 64 hex chars; same date always yields same name in one vault, different names in two vaults. |
| TC-NFR-07 | NFR-SEC-05 | I | Inspect recovery slot in `vault.json`. | `kdf = PBKDF2-SHA256`, iterations ≥ 600000, salt 16 bytes, salt differs between two vaults. |
| TC-NFR-08 | NFR-SEC-06 | I | Run a scripted session (unlock with recovery code, write marker text, tag, caption, attach file, search) with logging at Trace; scan log files. | No marker text, tag, caption, file name, recovery code or base64 key material in logs. |
| TC-NFR-09 | NFR-SEC-07 | I | After TC-NFR-08, scan `%APPDATA%\Journal\settings.json`. | No marker text, tags or imported list content. |
| TC-NFR-10 | NFR-REL-01 | I | Inject failure after writing the temp file but before rename; then during temp write. | Original entry file still decrypts to the previous version; no `*.tmp` left after next unlock. |
| TC-NFR-11 | NFR-REL-02 | I | Delete `catalog.jcat`; unlock. Then corrupt it; unlock. | Catalog rebuilt both times; calendar and search results equal those before. |
| TC-NFR-12 | NFR-REL-03 | I | Remove an image from an entry and save; also remove the image, then undo before save. | First: blob deleted at the next clean-up (lock or unlock), never while the editor could still undo. Second: blob kept. Blob referenced by another entry is never deleted. |
| TC-NFR-13 | NFR-PERF-01 | I | Vault with 3,650 generated entries; measure unlock (fake Hello) to catalog loaded and index built. | ≤ 2 s on the reference PC (release build, median of 5 runs). |
| TC-NFR-14 | NFR-PERF-02 | A | Open text-only entry and an entry with 10 × 1 MB images from the 3,650-entry vault. | ≤ 300 ms and ≤ 1 s respectively (median of 5). |
| TC-NFR-15 | NFR-PERF-03 | U | 3,650-entry index; run 20 mixed queries. | Each ≤ 200 ms; median ≤ 50 ms. |
| TC-NFR-16 | NFR-PERF-04 | M | Type continuously for 30 s in a long entry while autosave runs. | No visible stutter; save happens on a background thread (verified in log timing). |
| TC-NFR-17 | NFR-USE-01 | M | Do a full session with keyboard only: unlock, change month, open day, write, add tag/tab, search, lock. | All possible without mouse; Accessibility Insights shows names for calendar cells, image captions, attachment chips. |
| TC-NFR-18 | NFR-USE-02 | M | Run at 100 %, 150 %, 200 % scaling and window size 1000×650. | No clipped controls or overlapping text. |
| TC-NFR-19 | NFR-COMP-01 | M | Install and run smoke test on Windows 10 22H2 x64 and Windows 11 (x64; ARM64 if available). | Setup, unlock, write, search, lock work. |
| TC-NFR-20 | NFR-MNT-01 | U | Architecture test: inspect `Journal.Core` assembly references. | No reference to `PresentationFramework`, `WindowsBase`, `Microsoft.Windows.SDK.NET` or `Journal.Infrastructure`. |
| TC-NFR-21 | NFR-MNT-02 | I | Read every persisted JSON (header, entry, catalog, settings, imported lists). | Each has `formatVersion`; reading a file with a higher unknown version reports "made by a newer version" instead of failing silently. |
| TC-NFR-22 | NFR-MNT-03 | U | Run coverage with coverlet. | `Journal.Core` line coverage ≥ 85 %. |
| TC-NFR-24 | NFR-USE-02, FR-EDT-01 | A | Open the real main window at 1000×650 with the longest verse and quote. | Editor text area ≥ 100 px tall and fully inside the window. |
| TC-NFR-23 | NFR-LIC-01 | I | Check embedded content metadata and package licenses (`dotnet list package` + license scan). | Verse source = WEB (public domain); every quote records a public-domain source edition (e.g. George Long's 1862 Marcus Aurelius); all packages MIT/Apache/BSD. |

## 3. Traceability matrix

146 requirements, 161 test cases. Every requirement is covered by at least one test.

| Requirement | Pri | Test cases |
|---|---|---|
| FR-SEC-01 | M | TC-SEC-01 (A) |
| FR-SEC-02 | M | TC-SEC-02 (A) |
| FR-SEC-03 | M | TC-SEC-03 (I), TC-SEC-20 (M) |
| FR-SEC-04 | S | TC-SEC-04 (I) |
| FR-SEC-05 | M | TC-SEC-05 (A) |
| FR-SEC-06 | M | TC-SEC-06 (U) |
| FR-SEC-07 | M | TC-SEC-07 (A) |
| FR-SEC-08 | M | TC-SEC-08 (I), TC-SEC-23 (A), TC-SEC-20 (M) |
| FR-SEC-09 | M | TC-SEC-09 (A) |
| FR-SEC-10 | M | TC-SEC-10 (I), TC-SEC-21 (M) |
| FR-SEC-11 | M | TC-SEC-11 (A) |
| FR-SEC-12 | M | TC-SEC-12 (A), TC-SEC-21 (M) |
| FR-SEC-13 | M | TC-SEC-13 (U) |
| FR-SEC-14 | M | TC-SEC-14 (A), TC-SEC-14M (M) |
| FR-SEC-15 | M | TC-SEC-15 (A) |
| FR-SEC-16 | M | TC-SEC-16 (A) |
| FR-SEC-17 | M | TC-SEC-17 (I) |
| FR-SEC-18 | M | TC-SEC-18 (I) |
| FR-SEC-20 | M | TC-SEC-22 (A/I) |
| FR-SEC-19 | S | TC-SEC-19 (M) |
| FR-SEC-21 | M | TC-SEC-26 (A/M) |
| FR-ENT-01 | M | TC-ENT-01 (I) |
| FR-ENT-02 | M | TC-ENT-02 (A) |
| FR-ENT-03 | M | TC-ENT-03 (A) |
| FR-ENT-04 | M | TC-ENT-04 (A) |
| FR-ENT-05 | M | TC-SEC-16 (A), TC-ENT-05 (A) |
| FR-ENT-06 | M | TC-ENT-06 (A) |
| FR-ENT-07 | M | TC-ENT-07 (I) |
| FR-ENT-08 | M | TC-ENT-08 (I) |
| FR-ENT-09 | M | TC-ENT-09 (I) |
| FR-EDT-01 | M | TC-EDT-01 (M), TC-NFR-24 (A) |
| FR-EDT-02 | M | TC-EDT-02 (A), TC-EDT-03 (A) |
| FR-EDT-03 | M | TC-EDT-04 (A) |
| FR-EDT-04 | M | TC-EDT-04 (A), TC-EDT-05 (A) |
| FR-EDT-05 | M | TC-EDT-06 (A) |
| FR-EDT-06 | M | TC-EDT-07 (A) |
| FR-EDT-07 | M | TC-EDT-08 (A), TC-EDT-09 (A) |
| FR-EDT-08 | M | TC-EDT-10 (A) |
| FR-EDT-09 | S | TC-EDT-11 (A) |
| FR-EDT-10 | S | TC-EDT-12 (A), TC-EDT-16 (A) |
| FR-EDT-11 | M | TC-EDT-02 (A), TC-EDT-04 (A), TC-EDT-06 (A), TC-EDT-07 (A), TC-EDT-08 (A), TC-EDT-10 (A), TC-EDT-13 (A), TC-EDT-14 (I), TC-IMG-04 (A), TC-ATT-02 (A) |
| FR-EDT-12 | S | TC-EDT-15 (M) |
| FR-EDT-13 | M | TC-EDT-01 (M), TC-EDT-17 (A) |
| FR-IMG-01 | M | TC-IMG-01 (A) |
| FR-IMG-02 | M | TC-IMG-02 (I) |
| FR-IMG-03 | M | TC-IMG-03 (A), TC-IMG-04 (A) |
| FR-IMG-04 | M | TC-IMG-05 (A) |
| FR-IMG-05 | S | TC-IMG-06 (A) |
| FR-IMG-06 | M | TC-IMG-07 (I) |
| FR-IMG-07 | M | TC-IMG-08 (M) |
| FR-IMG-08 | S | TC-IMG-09 (M) |
| FR-IMG-09 | M | TC-IMG-10 (A) |
| FR-ATT-01 | M | TC-ATT-01 (A), TC-ATT-08 (I) |
| FR-ATT-02 | M | TC-ATT-02 (A) |
| FR-ATT-03 | M | TC-ATT-03 (I), TC-ATT-03M (M) |
| FR-ATT-04 | M | TC-ATT-04 (A) |
| FR-ATT-05 | M | TC-ATT-05 (A) |
| FR-ATT-06 | M | TC-ATT-06 (I) |
| FR-ATT-07 | S | TC-ATT-07 (A) |
| FR-TAB-01 | M | TC-TAB-01 (U) |
| FR-TAB-02 | M | TC-TAB-02 (A) |
| FR-TAB-03 | M | TC-TAB-03 (U) |
| FR-TAB-04 | M | TC-TAB-04 (U) |
| FR-TAB-05 | M | TC-TAB-05 (U/A) |
| FR-TAB-06 | C | TC-TAB-06 (A) |
| FR-TAB-07 | M | TC-TAB-07 (I) |
| FR-TAG-01 | M | TC-TAG-01 (U) |
| FR-TAG-02 | M | TC-TAG-02 (U) |
| FR-TAG-03 | M | TC-TAG-03 (U) |
| FR-TAG-04 | M | TC-TAG-04 (A) |
| FR-TAG-05 | S | TC-TAG-05 (A) |
| FR-CAL-01 | M | TC-CAL-01 (A), TC-CAL-10 (M) |
| FR-CAL-02 | M | TC-CAL-02 (U/A) |
| FR-CAL-03 | M | TC-CAL-03 (A), TC-CAL-10 (M) |
| FR-CAL-04 | S | TC-CAL-04 (A) |
| FR-CAL-05 | S | TC-CAL-05 (A) |
| FR-CAL-06 | M | TC-CAL-06 (A) |
| FR-CAL-07 | M | TC-CAL-07 (A) |
| FR-CAL-08 | M | TC-CAL-08 (A) |
| FR-CAL-09 | M | TC-CAL-09 (A) |
| FR-SRCH-01 | M | TC-SRCH-01 (A) |
| FR-SRCH-02 | M | TC-SRCH-02 (I), TC-SRCH-15 (U) |
| FR-SRCH-03 | M | TC-SRCH-03 (U) |
| FR-SRCH-04 | M | TC-SRCH-04 (U) |
| FR-SRCH-05 | M | TC-SRCH-05 (U) |
| FR-SRCH-06 | M | TC-SRCH-06 (U) |
| FR-SRCH-07 | S | TC-SRCH-07 (U) |
| FR-SRCH-08 | S | TC-SRCH-08 (U) |
| FR-SRCH-09 | S | TC-SRCH-09 (U) |
| FR-SRCH-10 | M | TC-SRCH-10 (U) |
| FR-SRCH-11 | M | TC-SRCH-11 (A) |
| FR-SRCH-12 | M | TC-SRCH-12 (U) |
| FR-SRCH-13 | S | TC-SRCH-13 (A) |
| FR-SRCH-14 | S | TC-SRCH-14 (A) |
| FR-INSP-01 | M | TC-INSP-01 (A) |
| FR-INSP-02 | M | TC-INSP-01 (A) |
| FR-INSP-03 | M | TC-INSP-02 (I) |
| FR-INSP-04 | M | TC-INSP-03 (I) |
| FR-INSP-05 | M | TC-INSP-04 (A) |
| FR-INSP-06 | S | TC-INSP-05 (U/A) |
| FR-INSP-07 | M | TC-INSP-06 (U) |
| FR-INSP-08 | M | TC-INSP-07 (U) |
| FR-INSP-09 | S | TC-INSP-08 (U) |
| FR-INSP-10 | M | TC-INSP-09 (I) |
| FR-INSP-11 | S | TC-INSP-10 (I) |
| FR-INSP-12 | S | TC-INSP-11 (I) |
| FR-INSP-13 | M | TC-INSP-13 (I/U) |
| FR-INSP-15 | M | TC-INSP-15 (A) |
| FR-INSP-14 | M | TC-INSP-14 (A) |
| FR-INSP-16 | M | TC-INSP-16 (I) |
| FR-INSP-17 | M | TC-INSP-01 (A), TC-INSP-05 (U/A) |
| FR-SET-01 | M | TC-SET-01 (A) |
| FR-SET-02 | M | TC-SET-02 (I) |
| FR-SET-03 | S | TC-SET-03 (I) |
| FR-SET-04 | S | TC-SET-04 (I) |
| FR-SET-05 | S | TC-SET-05 (I) |
| FR-UI-01 | S | TC-UI-01 (A), TC-UI-02 (M) |
| FR-MOOD-01 | M | TC-MOOD-01 (U/A), TC-MOOD-06 (A/M) |
| FR-MOOD-02 | M | TC-MOOD-02 (U/I) |
| FR-MOOD-03 | M | TC-CAL-10 (M), TC-MOOD-03 (A), TC-MOOD-06 (A/M) |
| FR-MOOD-04 | M | TC-MOOD-04 (U), TC-MOOD-06 (A/M) |
| FR-MOOD-05 | M | TC-MOOD-05 (A) |
| NFR-SEC-01 | M | TC-NFR-01 (I), TC-NFR-02 (I) |
| NFR-SEC-02 | M | TC-NFR-03 (I) |
| NFR-SEC-03 | M | TC-NFR-04 (I), TC-NFR-05 (I) |
| NFR-SEC-04 | M | TC-NFR-06 (I) |
| NFR-SEC-05 | M | TC-NFR-07 (I) |
| NFR-SEC-06 | M | TC-NFR-08 (I) |
| NFR-SEC-07 | M | TC-NFR-09 (I) |
| NFR-SEC-08 | M | TC-NFR-01 (I) |
| NFR-REL-01 | M | TC-NFR-10 (I) |
| NFR-REL-02 | M | TC-NFR-11 (I) |
| NFR-REL-03 | M | TC-NFR-12 (I) |
| NFR-PERF-01 | M | TC-NFR-13 (I) |
| NFR-PERF-02 | M | TC-NFR-14 (A) |
| NFR-PERF-03 | M | TC-NFR-15 (U) |
| NFR-PERF-04 | M | TC-NFR-16 (M) |
| NFR-PERF-05 | M | TC-ATT-08 (I) |
| NFR-USE-01 | S | TC-NFR-17 (M) |
| NFR-USE-03 | S | TC-SEC-24 (M), TC-SEC-25 (A/I) |
| NFR-USE-02 | M | TC-NFR-18 (M), TC-NFR-24 (A) |
| NFR-COMP-01 | M | TC-NFR-19 (M) |
| NFR-MNT-01 | M | TC-NFR-20 (U) |
| NFR-MNT-02 | M | TC-NFR-21 (I) |
| NFR-MNT-03 | S | TC-NFR-22 (U) |
| NFR-LIC-01 | M | TC-NFR-23 (I) |
