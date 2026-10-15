# Journal: Issue Log

Bugs and change requests reported for the Journal app. Issues are recorded here first. **Nothing is fixed until the owner approves it.**

## Workflow

| Status | Meaning |
|---|---|
| **Open** | Recorded and analysed (cause, affected requirements, proposed fix). Not being worked on. |
| **Approved** | The owner has said to fix it. |
| **Fixed** | Change made and automated tests pass. Waiting for the owner to check it. |
| **Verified** | The owner confirmed the fix in the app. |
| **Closed** | Verified, or rejected / won't fix (with reason). |
| **Deferred** | Accepted but scheduled for later. |

Every **Bug** names the requirements it fails and the test cases that fail because of it. When no existing test fails, the entry says so and names the test that should have caught it: that is a test gap, and the fix closes it. Every **Change** names the requirements it adds or changes and the tests that will cover it.

Each fix also updates the affected requirements and test cases, and adds or corrects the test so it fails before the fix and passes after.

**Severity:** *High* (data loss, security, crash, or a core feature unusable) · *Medium* (feature wrong or incomplete, workaround exists) · *Low* (cosmetic or minor).
**Type:** *Bug* (doesn't meet an approved requirement) · *Change* (new or changed requirement).

## Summary

| ID | Title | Type | Severity | Requirements | Tests | Status |
|---|---|---|---|---|---|---|
| ISS-001 | Some daily verses start or end mid-sentence | Bug | Medium | FR-INSP-01, FR-INSP-03 | TC-INSP-01, TC-INSP-02 (gap) → TC-INSP-13 | Verified |
| ISS-002 | Verse and quote can't be copied | Change | Low | FR-INSP-14 (new) | TC-INSP-14 (new) | Verified |
| ISS-003 | Editor disappears at the minimum window size when the verse is long | Bug | High | NFR-USE-02, FR-EDT-01 | TC-NFR-18 (manual, not run); gap → TC-NFR-24 | Verified |
| ISS-004 | Long verses crowd out the writing area at the default window size | Change | Medium | FR-INSP-15 (new) | TC-INSP-15 (new) | Verified |
| ISS-006 | Add a quill logo shown while the app runs | Change | Low | FR-UI-01 (new) | TC-UI-01, TC-UI-02 (new) | Verified |
| ISS-005 | Font and size boxes are empty until the text is clicked | Bug | Low | FR-EDT-10 | TC-EDT-12 (gap) → TC-EDT-16 | Verified |

---

## ISS-001: Some daily verses start or end mid-sentence

| | |
|---|---|
| **Reported** | 2026-10-15 |
| **Type / Severity** | Bug / Medium |
| **Area** | Daily verse (built-in content) |
| **Failing requirements** | FR-INSP-01 (each day shall display a Bible verse: a sentence fragment isn't a usable verse); FR-INSP-03 (built-in list of verses). |
| **Failing tests** | None failed. **Test gap:** TC-INSP-02 checked count, translation and reference format but not that each verse is a complete sentence; TC-INSP-01 only checks that a verse is shown. |
| **Requirements changed** | FR-INSP-13 added (verses are complete passages; saved partial verses are shown in full). |
| **Status** | Verified by the owner 2026-10-15. Fixed before this workflow was agreed. |

**Description.** The verse for October 13, 2026 appeared incomplete.

**Steps to reproduce.** Open October 13, 2026 with default verse settings.

**Expected.** A complete passage. **Actual.** 1 Corinthians 13:7 "bears all things, believes all things, hopes all things, and endures all things." The sentence starts in verse 4.

**Cause.** The built-in list was extracted by single-verse references without checking sentence boundaries. 41 verses ended and 28 started mid-sentence.

**Resolution.** The extraction now widens each reference to whole sentences (e.g. 1 Corinthians 13:4-7, Ecclesiastes 3:1-8) and drops overlapping passages, giving 477 verses. Saved days whose stored verse is partial now show the full passage. Because the list changed, unsaved days get a different verse than before.

**Tests added.** TC-INSP-13: every built-in verse starts and ends a sentence, and a stored partial verse is upgraded. It fails against the old verse list (69 partial verses) and passes after the fix.

---

## ISS-002: Verse and quote can't be copied

| | |
|---|---|
| **Reported** | 2026-10-15 |
| **Type / Severity** | Change / Low |
| **Area** | Daily verse and quote card |
| **Failing requirements** | None (new capability). |
| **Requirements changed** | FR-INSP-14 added (selectable text and Copy buttons with citations). |
| **Tests** | TC-INSP-14 added. |
| **Status** | Verified by the owner 2026-10-15. Fixed before this workflow was agreed. |

**Description.** The owner wants to copy the verse and quote to paste into another document.

**Resolution.** Verse and quote text are selectable (Ctrl+C). Copy buttons copy the verse with "— reference (WEB)", the quote with "— author, work, source edition", or both. The status bar confirms the copy, or reports a busy clipboard.

**Tests added.** TC-INSP-14: copy verse, quote and both; busy clipboard.

---

## ISS-003: Editor disappears at the minimum window size when the verse is long

| | |
|---|---|
| **Reported** | 2026-10-15 (found while rendering the longest verse and quote at the owner's request) |
| **Type / Severity** | Bug / High |
| **Area** | Main window layout, verse and quote card |
| **Failing requirements** | NFR-USE-02 (the UI shall be usable at a minimum window size of 1000×650); FR-EDT-01 (each tab shall contain a rich-text editor). |
| **Failing tests** | TC-NFR-18 (manual: 100–200 % scaling and 1000×650, no clipped controls) would fail; it hasn't been run. **Test gap:** no automated test checks layout at the minimum size. |
| **Status** | Verified by the owner 2026-10-15. Approved 2026-10-15. |

**Steps to reproduce.** Size the window to 1000×650. Open a day whose verse is Ecclesiastes 3:1-8 (688 characters, the longest built-in verse) with the Franklin quote (230 characters, the longest quote).

**Expected.** The tabs and editor stay visible and usable. **Actual.** The card grows to about 360 px tall and pushes the tab strip and editor off the bottom of the window; only a sliver of the toolbar shows.

**Cause.** The card's height is set by its content. Nothing limits it, and the editor gets only the space left over.

**Proposed fix (owner's direction).** Cap the card's height and let its text scroll inside the card. The same cap fixes ISS-004.

**Proposed tests.** A new automated UI test renders the main window at 1000×650 with the longest built-in verse and quote, and asserts the editor is visible with at least a minimum usable height. It should fail before the fix and pass after.

---

**Resolution.** The verse and quote text areas now have a maximum height and scroll (the mouse wheel scrolls them directly). The reference, author and Copy buttons stay visible below. Seven lines (140 px) fit at normal window sizes, but at 1000×650 that still left the editor only 94 px. So below a 760 px window height the cap drops to five lines (100 px), which leaves the editor about 135 px. This departs slightly from the approved "about seven lines": seven lines still apply at normal sizes.

**Tests.** TC-NFR-24 (new, automated, real main window at 1000×650 with the longest verse and quote): failed before the fix with an editor text area of 0 px; with a fixed seven-line cap it failed at 94 px; it passes after the fix.

## ISS-004: Long verses crowd out the writing area at the default window size

| | |
|---|---|
| **Reported** | 2026-10-15 |
| **Type / Severity** | Change / Medium |
| **Area** | Main window layout, verse and quote card |
| **Failing requirements** | None explicitly: a design gap. No requirement limited the card's height. |
| **Requirements to change** | Add FR-INSP-15: the verse and quote card shall not exceed a fixed maximum height; longer text scrolls within the card, and the Copy and Shuffle buttons stay visible. |
| **Tests** | New: render at the default 1240×820 with the longest verse and quote; assert the card height is at most the cap and the editor keeps most of the right-hand column. |
| **Status** | Verified by the owner 2026-10-15. Approved 2026-10-15. |

**Description.** At the default 1240×820 window, the longest verse makes the card about 280 px tall, leaving about 230 px for the editor. On long-verse days the page reads as a verse page rather than a journal.

**Proposed change (owner's direction).** Cap the card's height (proposal: about 160 px, roughly seven lines of verse text) and let the verse and quote columns scroll independently inside it. Short verses and quotes are unaffected.

---

**Resolution.** Same change as ISS-003: each text area shows at most about seven lines (140 px) and scrolls, five lines on windows shorter than 760 px. The card is now about 220 px tall at most, down from about 280 px for the longest verse. FR-INSP-15 added.

**Tests.** TC-INSP-15 (new, real main window at 1240×820): card ≤ 230 px, the verse scrolls, Copy stays visible. Failed before the fix (no capped card) and passes after.

## ISS-005: Font and size boxes are empty until the text is clicked

| | |
|---|---|
| **Reported** | 2026-10-15 |
| **Type / Severity** | Bug / Low |
| **Area** | Editor toolbar |
| **Failing requirements** | FR-EDT-10 (toolbar controls shall reflect the formatting at the caret or selection). |
| **Failing tests** | None failed. **Test gap:** TC-EDT-12 tests the formatting reader on a RichTextBox directly, not the editor's toolbar right after a day or tab loads. |
| **Status** | Verified by the owner 2026-10-15. Approved 2026-10-15. |

**Steps to reproduce.** Open any day. Look at the font and size boxes before clicking in the text.

**Expected.** They show the current font and size (Segoe UI, 11 pt). **Actual.** Both are blank until the caret moves.

**Cause.** The toolbar updates only on the editor's SelectionChanged event, which doesn't fire when a document is loaded.

**Proposed fix (owner's direction).** Load the font and size boxes immediately: update the toolbar when a document is loaded into the editor (day or tab change), not only when the selection changes.

**Proposed tests.** A new UI test creates the editor, binds it to a tab, and asserts the font box shows "Segoe UI" and the size box "11" with no user interaction. It should fail before the fix and pass after.

**Resolution.** The editor now updates the toolbar as soon as a day or tab is loaded, as well as when the selection changes.

**Tests.** TC-EDT-16 (new, real main window): failed before the fix (font box empty) and passes after (Segoe UI, 11).

---

## ISS-006: Add a quill logo shown while the app runs

| | |
|---|---|
| **Reported** | 2026-10-15 |
| **Type / Severity** | Change / Low |
| **Area** | Branding: app icon and windows |
| **Failing requirements** | None (new capability). |
| **Requirements to change** | Add FR-UI-01: the app shall have a quill logo, used as the program icon (Start menu, desktop shortcut, Explorer), the window and taskbar icon of every window, and in the top bar of the main window, unlock window and setup wizard. |
| **Status** | Verified by the owner 2026-10-15. |

**Description.** The owner wants a logo, an icon of a quill, displayed when the app is running.

**Proposed change.**
- Draw an original quill icon as vector art in the app's palette (teal accent on a light tile), so it stays sharp at every size.
- Produce a multi-size Windows icon file (16, 24, 32, 48, 64 and 256 px) and set it as the program icon, so Explorer, the Start menu and shortcuts show the quill.
- Set it as the icon of every window (main, unlock, setup, settings, dialogs). Windows then shows it in the title bar, taskbar and Alt+Tab.
- Show the quill beside "Journal" in the main window's top bar, and in place of the lock symbol at the top of the unlock and setup windows.
- Before building it in, show the owner a preview of the design for approval.

**Proposed tests.**
- TC-UI-01 (automated): the icon resource has all six sizes; every app window has its Icon set; the main window top bar contains the logo image.
- TC-UI-02 (manual): the quill shows in the taskbar, Alt+Tab, Explorer and the Start menu at 100 % and 200 % display scaling.

**Resolution.** The approved quill is a vector drawing (`src/Journal.App/Assets/Quill.xaml`). `Journal.ico` is rendered from it at 16, 24, 32, 48, 64 and 256 px. The icon is the program icon (embedded in Journal.exe) and is set on every app window through the shared window style. The logo appears beside "Journal" in the main window's top bar, and replaces the lock symbol on the unlock screen and heads the setup wizard. FR-UI-01 added.

**Tests.** TC-UI-01 (new, automated): failed before the change (no icon resource) and passes after. TC-UI-02 (manual) is for you: check the taskbar, Alt+Tab, Explorer and the Start menu.
