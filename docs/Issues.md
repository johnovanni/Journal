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
| ISS-001 | Some daily verses start or end mid-sentence | Bug | Medium | FR-INSP-01, FR-INSP-03 | TC-INSP-01, TC-INSP-02 (gap) → TC-INSP-13 | Closed |
| ISS-002 | Verse and quote can't be copied | Change | Low | FR-INSP-14 (new) | TC-INSP-14 (new) | Closed |
| ISS-003 | Editor disappears at the minimum window size when the verse is long | Bug | High | NFR-USE-02, FR-EDT-01 | TC-NFR-18 (manual, not run); gap → TC-NFR-24 | Closed |
| ISS-004 | Long verses crowd out the writing area at the default window size | Change | Medium | FR-INSP-15 (new) | TC-INSP-15 (new) | Closed |
| ISS-005 | Font and size boxes are empty until the text is clicked | Bug | Low | FR-EDT-10 | TC-EDT-12 (gap) → TC-EDT-16 | Closed |
| ISS-006 | Add a quill logo shown while the app runs | Change | Low | FR-UI-01 (new) | TC-UI-01, TC-UI-02 (new) | Closed |
| ISS-007 | Windows Hello prompts are left open after the app closes or a test ends | Bug | Medium | FR-SEC-08, FR-SEC-09; gap → FR-SEC-20 (proposed) | TC-SEC-09 (gap) → TC-SEC-22 | Closed |
| ISS-008 | Windows Hello prompts automatically on the unlock screen, including after every lock | Change | Medium | FR-SEC-08 (change) | TC-SEC-20 (change), TC-SEC-23 | Closed |
| ISS-009 | Windows Hello prompt appears on the main display, not the one showing the app | Bug | Low | Design §14; gap → NFR-USE-03 | TC-SEC-20 (manual) fails on multiple displays; gap → TC-SEC-24 (manual), TC-SEC-25 | Closed |
| ISS-010 | Copy and paste drops checkboxes, images, attached files and headings | Bug | High | FR-EDT-01; FR-EDT-06, FR-EDT-08, FR-IMG-04, FR-ATT-02; new FR-EDT-13 | TC-EDT-01 (manual, text only); gap → TC-EDT-17 (new) | Verified |
| ISS-011 | Add quotes from great literature (Shakespeare and world authors) | Change | Low | FR-INSP-02, FR-INSP-04, FR-INSP-06 (changed); FR-INSP-16, FR-INSP-17 (new) | TC-INSP-01, TC-INSP-05, TC-INSP-10 (changed); TC-INSP-16 (new) | Verified |
| ISS-012 | Add world-religion texts to the verse and Science as a quote category (future) | Change | Low | FR-INSP-01, FR-INSP-03 (change); new requirements to be written | New tests to be written | Open |
| ISS-013 | Mark each day sad, neutral or happy; tint the calendar and count moods | Change | Medium | FR-CAL-03 (change); new FR-MOOD-01…05 | TC-CAL-03 (change); new TC-MOOD-01…06 | Verified |
| ISS-014 | A ticked checklist item's strikethrough also runs through its checkbox | Bug | Low | FR-EDT-09 | TC-EDT-11 (extended) | Verified |
| ISS-015 | Settings: a section per quote category, and tick-box lists in alphabetical columns | Change | Low | FR-INSP-06 (change) | TC-INSP-05 (change); new layout test | Verified |
| ISS-016 | Closing the window doesn't exit; the app keeps running in the background | Bug | High | FR-ENT-05, FR-ATT-06, FR-SEC-19; new FR-SEC-21 | None (gap: no test closes the real main window); new TC-SEC-26 | Verified |

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
| **Status** | Closed 2026-10-16. Verified by the owner 2026-10-15. Fixed before this workflow was agreed. |

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
| **Status** | Closed 2026-10-16. Verified by the owner 2026-10-15. Fixed before this workflow was agreed. |

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
| **Status** | Closed 2026-10-16. Verified by the owner 2026-10-15. Approved 2026-10-15. |

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
| **Status** | Closed 2026-10-16. Verified by the owner 2026-10-15. Approved 2026-10-15. |

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
| **Status** | Closed 2026-10-16. Verified by the owner 2026-10-15. Approved 2026-10-15. |

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
| **Status** | Closed 2026-10-16. Verified by the owner 2026-10-15. |

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

---

## ISS-007: Windows Hello prompts are left open after the app closes or a test ends

| | |
|---|---|
| **Reported** | 2026-10-16 |
| **Type / Severity** | Bug / Medium |
| **Area** | Unlocking, app lifecycle, test tooling |
| **Failing requirements** | FR-SEC-08 and FR-SEC-09 (the unlock flow): a Windows Hello request outlives the unlock window and the app. No requirement says a pending request must be cancelled, so this is also a requirement gap. Proposed **FR-SEC-20**: a pending Windows Hello request shall be cancelled when the unlock window closes, when the app exits, and when the journal is unlocked another way (recovery code). |
| **Failing tests** | None failed. **Test gap:** TC-SEC-09 only checks that a cancelled or failed prompt leaves the journal locked; nothing checks that closing the window cancels an open prompt. |
| **Status** | Closed 2026-10-16. Verified by the owner 2026-10-16. Approved 2026-10-16; option (a) chosen. |

**Description.** Windows Hello dialogs stay on screen after the app is closed or testing is finished, sometimes several at once.

**Verification (2026-10-16).**
- No dialogs were open when checked this morning (no Journal or Windows Hello broker processes running), so the evidence comes from the app's log and the code.
- **Cause 1: the screenshot tool started the full app.** On 2026-10-15 I rendered screenshots for ISS-003 to ISS-006 with an offscreen tool. Its five runs at 11:46 (×2), 12:08 (×2) and 12:25 each match a log entry "Started" followed by "Exited" one to two seconds later. The tool called `Application.Run()`, which runs the app's normal startup. Each run therefore showed the unlock window, which immediately asked Windows Hello (see ISS-008), and then shut down while the request was open. That accounts for several of the dialogs. **Correction (2026-10-16):** the earlier statement that the automated tests don't do this was wrong. WPF runs an Application's startup as soon as its dispatcher starts, even without `Application.Run()`. The UI test host created the real `App`, so every UI test run since ISS-003 started the real app on this PC, and its unlock window asked Windows Hello. This was confirmed at 10:05:51 on 2026-10-16: a test run logged "Started" and a Windows Hello dialog opened two seconds later.
- **Cause 2: the app never cancels a pending request.** `UnlockViewModel.TryHello` awaits Windows Hello with no way to cancel it. Closing the unlock window or exiting the app leaves the request running; the dialog belongs to Windows, not to the app, so it stays. The log also shows six starts on 2026-10-15 with no matching exit (12:06, 12:06, 12:07, 12:08, 12:25, 12:26). Those processes ended without a normal shutdown, for example by stopping the debugger, which leaves any open prompt behind.
- **Related, found while checking:** while a Windows Hello prompt is open, the recovery-code Unlock button does nothing (`IsBusy` blocks it), so the user must dismiss the prompt first.

**Proposed fix.**
1. Pass a cancellation token through `VaultSession.UnlockWithHelloAsync` → `IKeyProtector.UnprotectAsync` → `IHelloCredentialApi.SignAsync` and the consent call. Cancel it when the unlock window closes, when the app exits, and when a recovery-code unlock starts. Cancelling the WinRT operation should close the Windows Hello dialog; to be confirmed on this PC.
2. Allow recovery-code unlock while a Windows Hello prompt is open (cancel the prompt first).
3. ISS-008 (no automatic prompt) removes most of these prompts in the first place.
4. Tooling: screenshot and UI tools must never run the app's startup. They will build windows the way the layout tests do, so no unlock window or Windows Hello request can start.

**Proposed tests.** TC-SEC-22 (automated, fake Windows Hello): closing the unlock window cancels the pending request; starting a recovery-code unlock cancels it and succeeds; exiting cancels it. Each should fail before the fix.

**Progress (2026-10-16).**
- **Done and tested:**
  - The UI test host and the screenshot tool no longer create the real `App`, so neither can start the app or ask Windows Hello. Checked: a full test run and a screenshot run each added no "Started" entry to the app's log. Guard test TC-SEC-22 "UI tests never start the real app".
  - Windows Hello requests now take a cancellation token, from the unlock and setup windows down to the WinRT call. Closing the unlock or setup window, exiting the app, or starting a recovery-code unlock cancels the request, so the app stops waiting.
  - The recovery code now works while a Windows Hello request is waiting. Before, it was blocked.
  - With ISS-008, the app no longer starts requests on its own.
  - Tests TC-SEC-22 (five cases) failed before the change and pass after.
- **Finding that changes the plan:** an experiment on this PC showed that **cancelling a Windows Hello request does not close its dialog.** The request ends, but the Windows Security dialog stays open until the user cancels it. Asking the dialog to close (a standard window-close message) is also ignored. The app therefore cannot close a dialog the user opened. Forcibly ending the Windows Security process would close it, but an app shouldn't kill a Windows security component, so that isn't proposed.
- **Effect:** the dialogs the user saw came from the app (and tests) asking Windows Hello without being asked. Those causes are now gone. One case remains: if the user clicks **Unlock with Windows Hello** and then closes the app without answering, the dialog stays until the user cancels it.
- **Options for the owner:**
  - **(a) Accept this remaining case** and document it in the README (recommended).
  - **(b) Keep the app open while a request it started is waiting:** closing the unlock window would then say "Answer or cancel the Windows Hello prompt first."

**Decision (2026-10-16).** Owner chose **(a)**: accept the remaining case (the user clicked the button, then closed the app without answering) and document it. The README's "Windows Hello notes" now says the dialog belongs to Windows and stays until cancelled. FR-SEC-20 records what the app guarantees.

---

## ISS-008: Windows Hello prompts automatically on the unlock screen, including after every lock

| | |
|---|---|
| **Reported** | 2026-10-16 |
| **Type / Severity** | Change / Medium |
| **Area** | Unlock window |
| **Requirements to change** | FR-SEC-08 currently says the app *shall prompt* for Windows Hello on launch, and the unlock window does the same after every lock. Proposed: the unlock window shall not start Windows Hello by itself; it waits until the user clicks **Unlock with Windows Hello** (the default button, so Enter also works). |
| **Tests to change** | TC-SEC-20 (manual) changes to "click the button, then verify". New TC-SEC-23 (automated): showing the unlock window makes no Windows Hello request; clicking the button makes exactly one. |
| **Status** | Closed 2026-10-16. Verified by the owner 2026-10-16. Approved 2026-10-16. |

**Description.** After the journal locks, the Windows Hello dialog appears at once and waits on screen while the user is doing other things. The user wants it only when they click the unlock button.

**Verification (2026-10-16).** Confirmed in the code. `UnlockWindow` starts Windows Hello in its `ContentRendered` handler (`Views/WindowCode.cs`, lines 14–19), so every time the unlock window appears the prompt opens on its own. That happens at launch and after every lock: the Lock button, auto-lock after idle time, Win+L and sleep. After Win+L or sleep, the prompt is already waiting when the user signs back in to Windows.

**Proposed change.** Remove the automatic prompt; the button starts it. **Question for the owner:** should this also apply when the app is first launched (where the user has just asked to open the journal), or only after a lock? The proposal is button-only everywhere, for consistency. **Owner's answer (2026-10-16):** button-only everywhere, including the first launch.

**Resolution.** The unlock window no longer starts Windows Hello when it appears, at launch or after any lock. Windows Hello starts when the user clicks **Unlock with Windows Hello** (the default button, so Enter also works). FR-SEC-08 changed accordingly.

**Tests.** TC-SEC-23 (new, real unlock window): failed before the change (Windows Hello was asked once as soon as the window appeared) and passes after (no request until the button, then exactly one).

---

## ISS-009: Windows Hello prompt appears on the main display, not the one showing the app

| | |
|---|---|
| **Reported** | 2026-10-16 |
| **Type / Severity** | Bug / Low |
| **Area** | Unlocking, Windows Hello integration |
| **Failing requirements** | Design §14 promised to pass the window handle to Windows Hello "where supported"; the code doesn't. No requirement covers which display the prompt uses: a requirement gap. Proposed **NFR-USE-03**: authentication prompts shall appear on the display that shows the app. |
| **Failing tests** | TC-SEC-20 (manual: "Hello prompt appears in front of the app") fails on a multi-display PC; it was never run with more than one display. **Test gap:** no test covers multiple displays. |
| **Status** | Closed 2026-10-16. Verified by the owner 2026-10-16 (option C). |

**Description.** With more than one display, the Windows Hello dialog opens on the main display even when the journal is on another one.

**Verification (2026-10-16).**
- This PC has three displays: DISPLAY1 (primary, 1920×1080) in the middle, with 2400×1350 displays on either side.
- In the code, the only thing passed to Windows is a request to bring the app to the front (`SetForegroundWindow` in `Hello/WinRtHello.cs`). The Windows Hello calls themselves (`KeyCredentialManager.RequestCreateAsync`, `KeyCredential.RequestSignAsync`, `UserConsentVerifier.RequestVerificationAsync`) take no window, so Windows chooses where to show the dialog and uses the primary display.
- **Limitation:** the Windows SDK on this PC (10.0.19041) provides a window-aware version only for the consent check (`IUserConsentVerifierInterop.RequestVerificationForWindowAsync`, used by the weaker fallback unlock). There is no window-aware version of the key-signing call that normal unlocking uses. I couldn't see your screens, so the dialog's position itself is from your report.

**Proposed fix (options for the owner).**
- **A. Fallback unlock:** use `RequestVerificationForWindowAsync` with the journal's window, so that prompt follows the app. This affects only PCs on the weaker fallback, not this one.
- **B. Normal unlock:** run a short experiment on this PC first, e.g. whether the prompt follows the app when its window is active on the other display, or whether a newer Windows SDK offers a window-aware key-signing call. Report the findings before changing anything.
- **C. If Windows offers no way to choose the display:** when the journal isn't on the main display, the unlock window says "Look for the Windows Hello prompt on your main display" next to the button.
- The proposal is A + B, falling back to C if B finds nothing. **Owner (2026-10-16):** try A + B.

**Proposed tests.** If A or B is possible: an automated test that the window handle is passed to the Windows Hello call (fake API). In every case: a new manual test, TC-SEC-24, on a PC with several displays.

**Experiment (2026-10-16).** A test window was opened and made active on DISPLAY5 (a side display); the primary display is DISPLAY1. Each kind of Windows Hello request was made, and the dialog's position was read from Windows:

| # | Request | Dialog appeared on |
|---|---|---|
| 1 | Key signing, as the normal unlock uses | DISPLAY1 (primary) |
| 2 | Key signing with the mouse pointer over the app window | No new dialog: test 1's dialog was still open, so this result doesn't count |
| 3 | Consent check without a window, as the fallback unlock uses | DISPLAY1 (primary) |
| 4 | Consent check for the app's window (option A) | DISPLAY1 (primary) |

- The .NET Windows SDK does provide the window-aware consent check (`UserConsentVerifierInterop`). Even with it, Windows opened the dialog on the primary display, so **option A does not fix this** and was not built in.
- There is no window-aware key-signing call, and making the app the active window didn't help (test 1).
- The only untested idea is whether the dialog follows the mouse pointer (test 2). Testing it needs the user to cancel each dialog by hand, since the app can't close them (see ISS-007).
- **Conclusion so far:** Windows places these dialogs on the primary display, and the app has no reliable way to change that.

**Options for the owner.**
- **(C) Recommended:** when the journal isn't on the primary display, the unlock window says "Windows Hello will open on your main display" next to the button.
- **(B2):** rerun test 2 with the user cancelling the dialog by hand, to see whether the pointer position matters. If it does, the app could ask Windows Hello while the pointer is over its window, which it normally would be after clicking the button.

**Correction (2026-10-16, from the owner).** During the experiment, the owner saw one Windows Hello dialog on a side display, not the primary. That was test 2, the request made with the mouse pointer moved over the app window on DISPLAY5. The experiment missed it because it only looked for *new* dialog windows: test 1's dialog was still open, and Windows reused it for test 2, moving it to the pointer's display. The process count agrees: four Windows Hello processes for five requests. So "Conclusion so far" above is wrong. **Windows opens the dialog on the display where the mouse pointer is**, not where the app is.

**What this means.**
- The dialogs the owner reported on the main display came from the old automatic prompt (ISS-008), when the pointer could be anywhere.
- Now that Windows Hello starts only from a click on **Unlock with Windows Hello**, the pointer is over the journal's window, so the dialog should open on the journal's display with no further change.
- **Remaining gap:** pressing Enter (keyboard) while the pointer is on another display.

**Proposed next step.**
1. **Confirm in the real app (manual, owner):** put the journal on a side display, click Unlock with Windows Hello, and check the dialog opens there. Then press Enter with the pointer on another display, to see the gap. This becomes manual test TC-SEC-24.
2. **For the Enter case:**
   - **(i) Accept it.** It's documented in the README ("opens on the display where the mouse pointer is").
   - **(ii) Show a hint:** when the pointer isn't on the journal's display, the unlock window says "Windows Hello will open where your mouse pointer is."
   - Moving the user's pointer automatically is not proposed: moving the pointer without being asked is unexpected.
- Option A (window-aware consent check) still doesn't help and is not proposed.

**Owner's test in the real app (2026-10-16, TC-SEC-24).** The journal's unlock window opens on the display with the mouse pointer. But clicking **Unlock with Windows Hello** there, and pressing Enter with the pointer elsewhere, both opened the Windows Hello dialog **on the main display every time**. So the pointer theory above is **wrong**: the pointer decides where the journal's own windows open, not where Windows Hello opens. The one dialog seen on a side display during the experiment is unexplained. It came while another Windows Hello dialog was already open and the pointer had been moved by the experiment, a situation that doesn't occur in normal use.

**Conclusion.** For the Windows Hello calls the journal uses, Windows always opens the dialog on the main display. Nothing the app controls (active window, window handle, pointer position) changed that in normal use. TC-SEC-24 records this.

**Options for the owner.**
- **(C) Recommended:** when the journal is on a side display, the unlock window says "Windows Hello opens on your main display" next to the button. Small change; one automated test (hint shown on a side display, hidden on the main one) plus TC-SEC-24 manual.
- **(D)** Close as a Windows limitation, with only the README note.
- Proposed **NFR-USE-03** is reworded to what is possible: the user shall be told where the authentication prompt will appear when it isn't on the app's display.

**Docs corrected (2026-10-16).** The README's Windows Hello note now says the dialog opens on the main display. It had said the dialog follows the pointer, based on the experiment.

**Root cause found (2026-10-16, experiments v2–v4 with a 100 ms window log and full-desktop screenshots).**
- **Why the first experiment was wrong:** a Windows Hello dialog first appears as a full-screen window at 0,0 on the main display, then about half a second later moves to its real position. The first experiment read the position immediately, so it always reported the main display.
- **What put a dialog on a side display:** the window-aware consent check (option A, `UserConsentVerifierInterop.RequestVerificationForWindowAsync` with the journal's window). Re-run on its own with the test window on DISPLAY5 and the mouse pointer on the main display, its dialog settled at 3219,−213 on DISPLAY5, over the test window; the screenshot confirms it. This was the dialog the owner saw: test 4 of the first experiment.
- **The mouse pointer doesn't matter:** replaying test 1 then test 2 showed test 2's request never opened a dialog (Windows shows one at a time), and key signing again used the main display.
- **Key signing can't be placed or moved:** the normal unlock signs with the Windows Hello key (`KeyCredential.RequestSignAsync`). Neither the 10.0.19041 nor the newest 10.0.26100 Windows SDK has a window-aware version; the only window-aware Windows Hello call is the consent check. Moving the key-signing dialog after it appears was refused by Windows (`SetWindowPos`: access denied, error 5), as was closing it. Windows protects these dialogs from other programs.

**Conclusion.** The dialog can follow the journal only with the consent check, and the consent check can't protect the key the way key signing does:

| | Key signing (current normal unlock) | Window-aware consent check |
|---|---|---|
| Dialog appears | Always on the main display | On the journal's display |
| How the journal key is protected | Unwrapped with a signature from the PC's security chip that Windows only produces after Windows Hello succeeds | Protected by Windows for your account (DPAPI); the journal only releases it after the consent check succeeds |
| A program running as you while you're signed in could read the key without Windows Hello | No | Yes: the check is enforced by the journal, not by the security chip |

**Options for the owner.**
- **(C) Recommended:** keep key signing; when the journal is on a side display, the unlock window says "Windows Hello opens on your main display" next to the button. Security unchanged.
- **(E)** Switch the normal unlock to the window-aware consent check. The dialog follows the journal, but the key protection becomes the weaker fallback (D5 in the design would change).
- **(F)** Offer both as a setting, "Show Windows Hello on the journal's display (less secure)", off by default.
- In every case: the fallback unlock (already consent-based) uses the window-aware call, so its dialog follows the journal at no cost (option A).

**Resolution (option C).**
- Key signing is unchanged, so security is unchanged.
- When the unlock window is on a side display and Windows Hello is offered, it shows "Windows Hello opens on your main display" under the **Unlock with Windows Hello** button. The setup wizard shows the same note when Windows Hello is available. The note follows the window as it moves between displays.
- The fallback unlock (consent check) now uses the window-aware call, so on PCs that need it the dialog opens over the journal's window.
- NFR-USE-03 added.

**Tests.**
- TC-SEC-25 (new, automated): the hint logic for the unlock and setup windows, and the real unlock window moved between this PC's main and side displays. These three failed before the change (no hint) and pass after.
- Also in TC-SEC-25: the fallback passes the journal's window to Windows Hello. That plumbing already existed, so this test passed before the change. It guards the window-aware call, which itself can only be checked by hand.
- TC-SEC-24 (manual) is for the owner.

---

## ISS-010: Copy and paste drops checkboxes, images, attached files and headings

| | |
|---|---|
| **Reported** | 2026-10-18 (checkboxes); owner asked to check other elements such as images |
| **Type / Severity** | Bug / High (cut and paste can permanently lose images and files; see Impact) |
| **Area** | Editor: copy, cut and paste |
| **Failing requirements** | FR-EDT-01 (the editor shall behave like a simple word processor, including cut, copy and paste). Content those requirements define is lost on paste: FR-EDT-08 (checklist items and their ticks), FR-IMG-04 (images with captions), FR-ATT-02 (attached-file chips), FR-EDT-06 (Heading 1 and 2). |
| **Failing tests** | None failed. TC-EDT-01 (manual) only cuts and pastes a plain sentence. **Test gap:** no test copies and pastes checklists, images, files or headings. The automated test "Embedded blocks survive WPF's XAML copy (used by undo and copy/paste)" gave false assurance: it serializes the blocks with `XamlWriter` directly, which is not the path the editor's Copy command uses. |
| **Status** | Verified by the owner 2026-10-19. Released in version 1.1.0. |

**Description.** Copying journal content that contains checkboxes, then pasting it into another entry, loses the checkboxes and whether they were ticked. Asked to check other elements too.

**Steps to reproduce.** In an entry, make a checklist with one ticked and one unticked item. Select it, press Ctrl+C, open another day and press Ctrl+V. The items arrive as plain text lines, each starting with a space, with no checkboxes.

**Verification (2026-10-18).** A test program copied each kind of content exactly as Ctrl+C does in the journal's editor. It captured the copied data instead of writing your clipboard, pasted it into a second editor the way Ctrl+V reads it, and compared the result with the original. Each item was surrounded by normal text and copied on its own:

| Content | After copy and paste |
|---|---|
| Plain text, bold, italic, underline, strikethrough | Kept |
| Font, font size, text colour, highlight | Kept |
| Line breaks, centred paragraphs | Kept |
| Bulleted, numbered and nested lists | Kept |
| Heading 1 and Heading 2 | **Heading style lost**: the line comes back as normal text |
| Checklist items (unticked, ticked, indented) | **Checkbox, tick and indent lost**: plain text with a leading space |
| Image (with caption, or resized) | **Lost**: an empty line with a single space |
| Attached file | **Lost**: an empty line with a single space |

**Cause.**
- The editor uses the standard WPF rich-text box. When it copies, it writes the selection as XAML and RTF, but it **leaves out every embedded control**: the checkbox in a checklist line, and the image and file-chip blocks. Each becomes a single space. They are missing from the copied data, so nothing on the paste side can bring them back.
- It also copies only standard formatting, not the journal's own paragraph marker for Heading 1 and 2 (`ParagraphProps.Style`). So a pasted heading becomes a normal paragraph when the entry is saved.
- The copy is never the image-capable "XamlPackage" format; WPF offers that only for its own Image elements.

**Impact.**
- Copy then paste: checkboxes, images, files and headings silently disappear from the pasted copy.
- **Cut then paste is worse:** the original is removed and the pasted copy lacks the image or file. When the entry is saved, nothing references that image or file any more, so it is deleted from the vault at the next lock or unlock (NFR-REL-03 clean-up), and the loss becomes permanent. Undo (Ctrl+Z) brings it back only until then.
- Moving a selection by dragging it within the editor uses the same mechanism, so it very likely has the same effect (not separately tested).

**Proposed fix.**
1. **The journal's own clipboard format.** When the editor copies or cuts, it also puts the selected content on the clipboard in a journal-only format: the selection converted to the journal's document model (paragraphs, runs, headings, lists, checklist items with ticks and indents, images with captions and sizes, file chips with descriptions). Partly selected paragraphs keep only the selected text. The standard text, RTF and XAML formats stay, so pasting into other apps keeps working.
2. **Paste.** When the journal's format is on the clipboard, Ctrl+V inserts that content at the caret through the same code that loads entries, so everything comes back exactly. Otherwise paste works as now.
3. **Images and files** keep pointing to the same encrypted file in the vault, so nothing is duplicated and the clean-up keeps it while any entry uses it. If the source journal is a different vault (another journal on this PC), the image or file can't be read there; the pasted block will say "Image unavailable" or "File unavailable" rather than silently disappearing.
4. **Pasting into other apps** (Word, email): checklist lines are also copied as plain text "☐ Buy seeds" / "☑ Water beans", so the ticks survive as characters. Images and files are not copied to other apps (unchanged).
5. **Drag-and-drop within the editor** uses the same format, so moving content by dragging keeps it too.

**Proposed tests.**
- **TC-EDT-17 (new, automated):** for every content type in the table above, copy within one editor and paste into another through the editor's own copy and paste handlers. It captures the data, so the real clipboard isn't touched. The pasted content must equal the original.
- Further cases:
  - a partly selected paragraph pastes only the selected text, with its formatting;
  - cut and paste of an image keeps the same image and nothing is deleted at the next clean-up;
  - the plain-text copy shows ☐ and ☑.
- Each should fail before the fix and pass after.
- **TC-EDT-01 (manual)** gains a step: copy and paste a checklist, an image and a file between two days.
- **Also:** the existing "Embedded blocks survive WPF's XAML copy" test is renamed to say it covers undo only.

**Fix (2026-10-19).**
- **New requirement FR-EDT-13:** copy, cut, paste and drag within the journal keep everything the editor holds; pasted images and files refer to the same vault file; pasting is one undo step; other apps get ☐ / ☑ for checklist lines.
- **`JournalClipboard`** (new, `src/Journal.App/Editor/JournalClipboard.cs`):
  - On copy, cut and drag start, it adds the selection to the clipboard in the journal's own format (the document model as JSON, format name `Journal.DocumentFragment.v1`).
  - It also replaces the plain text with one line per paragraph, checklist lines starting "☐ " or "☑ ".
  - WPF's RTF and XAML formats are left as they were, so pasting into Word or email works as before.
  - A selection inside one plain paragraph is left to WPF's own copy, which already keeps its formatting.
- **Paste:** when the journal format is on the clipboard, the editor inserts that content at the caret through the same code that loads entries.
  - It replaces any selection, and the whole paste is one undo step.
  - On an empty line, the pasted content takes the place of that line.
  - Pasted blocks get new ids. Images and files keep pointing to the same encrypted file, so cut and paste no longer loses them at the next clean-up.
  - A partly selected paragraph keeps only the selected text, with its heading, list or checklist settings.
- **Drag-and-drop** inside the editor uses the same format. The drop inserts the content, then lets WPF finish the move so the original is removed.
- **Supporting changes:**
  - `FlowDocumentMapper.FromRange` reads the selected part of the document. It cuts each run's text by offset, because WPF's selected text of a list item includes the bullet ("•" and a tab).
  - `EditorOperations.InsertBlocksAtCaret` inserts several blocks where it used to insert one image or file.
- **Another journal on this PC:** pasting an image or file copied from a different vault shows the existing "Image unavailable" placeholder, and the file can't be opened. Nothing is lost from either vault.
- **Tests:**
  - New TC-EDT-17 (19 automated tests in `CopyPasteTests.cs`): every content type in the table above, partial selections, a whole heading on its own, cut and paste of an image, paste over a selection then undo, a drop, and the plain text for other apps.
  - With the journal clipboard turned off, 17 of the 19 fail. The other two test the selection reader directly.
  - TC-EDT-01 (manual) gains the copy, cut and drag steps.
  - The XAML-copy test is renamed to say it covers undo.
  - All tests pass: Core 63, App 82, Infrastructure 39 (plus the skipped performance test).
- **Not automated:** a real mouse drag and the Windows clipboard itself. They are covered by the manual steps in TC-EDT-01.

**To verify:**
1. In an entry, select a heading, a checklist (one item ticked), an image and a file. Copy, open another day and paste: everything comes back the same.
2. Cut an image and paste it lower down; lock and unlock; the image is still there.
3. Drag a checklist line to another place in the entry; it moves and keeps its checkbox.
4. Copy a checklist and paste it into Notepad; the lines start with ☐ and ☑.

---

## ISS-011: Add quotes from great literature (Shakespeare and world authors)

| | |
|---|---|
| **Reported** | 2026-10-19 |
| **Type / Severity** | Change / Low |
| **Area** | Daily quote (built-in content, settings, quote card) |
| **Failing requirements** | None (new capability). |
| **Requirements to change** | FR-INSP-02: the day shows a philosophy *or literature* quote. FR-INSP-06: quotes can also be filtered by category (Philosophy, Literature). New **FR-INSP-17**: quote categories are defined by the content, not the code; the card label, settings and filters list whatever categories exist. New **FR-INSP-16**: a built-in literature list of at least 366 distinct public-domain quotes from at least 30 authors across at least 8 languages or regions, each with author, work and source edition. |
| **Tests to change** | TC-INSP-01 (the card label follows the quote's category); TC-INSP-05 (category filter). New **TC-INSP-16**: the literature list's size, authors, regions, no duplicates, and a source edition on every quote. |
| **Status** | Verified by the owner 2026-10-19. Released in version 1.1.0. |

**Description.** The owner wants quotes from great literature alongside the philosophy quotes: Shakespeare, and great authors from around the world.

**Current state (checked 2026-10-19).** The quote list has 394 philosophy quotes from 25 authors, grouped into 11 traditions (Stoic, Christian, Chinese, Renaissance, Roman, Buddhist, Modern European, Greek, Transcendentalist, Enlightenment, French Moralist). The card is labelled "PHILOSOPHY" and Settings says "Show a philosophy quote each day".

**Proposed change.**
1. **Content.** About 400 literature quotes from public-domain texts, extracted word-for-word from Project Gutenberg editions as the philosophy quotes were, each with author, work and source edition. Translations are limited to public-domain editions. Proposed authors:
   - **England:** Shakespeare (plays and sonnets), Milton, Jane Austen, Charles Dickens, George Eliot
   - **America:** Walt Whitman, Emily Dickinson, Mark Twain, Herman Melville
   - **Spain:** Cervantes
   - **Italy:** Dante
   - **Germany:** Goethe
   - **France:** Victor Hugo, Molière
   - **Russia:** Tolstoy, Dostoevsky, Chekhov
   - **Ancient Greece and Rome:** Homer, Virgil
   - **Persia:** Rumi, Hafez, Omar Khayyám
   - **India:** Kalidasa, Rabindranath Tagore
   - **Japan:** Murasaki Shikibu
   - **China:** Tang poets such as Li Bai and Du Fu
   - **Arabic:** One Thousand and One Nights
   - **Lebanon:** Kahlil Gibran (*The Prophet*, 1923)
   - **Central and South America** (owner's request, 2026-10-19): Sor Juana Inés de la Cruz (Mexico), José Martí (Cuba), Rubén Darío (Nicaragua), Machado de Assis (Brazil), Domingo Sarmiento (Argentina), in public-domain English translations.
   - **Africa** (owner's request, 2026-10-19): Olaudah Equiano (Nigeria; wrote in English, 1789), Olive Schreiner (South Africa), Sol Plaatje (South Africa, *Mhudi*, 1930), the *Kebra Nagast* (Ethiopia), Apuleius (*The Golden Ass*, North Africa), the Instruction of Ptahhotep (ancient Egypt), and collections of African proverbs from the 1800s.
   - **Not possible: Jorge Luis Borges** (owner's suggestion). He died in 1986, and his works and their English translations are still under copyright, so they can't be shipped in the app. You can still add Borges quotes yourself through *Import quotes* (FR-INSP-11).
   - Final choice depends on what public-domain editions exist; the Latin American and African authors have fewer public-domain English translations, so some may yield only a few quotes.
2. **Category, open to future categories** (owner, 2026-10-19). Each quote gets a category: **Philosophy** (the existing 394) or **Literature** (new). Categories are data, not fixed in the code. The content file names each quote's category, and the label, settings choices and filters are built from the categories present. A future category, such as Science (ISS-012), then needs only content, not code changes. Literature quotes are grouped like the philosophy traditions, by language or region (English, Russian, Persian, Latin American, African, …), so the existing tradition filter keeps working.
3. **Settings.** "Show a philosophy quote each day" becomes "Show a quote each day", with a tick box for each category (Philosophy, Literature, and any future ones). **Default: all ticked** (owner, 2026-10-19). The tradition filter lists the groups of the ticked categories.
4. **Quote card.** The label switches with the quote's category, "PHILOSOPHY" or "LITERATURE" (owner, 2026-10-19), and would show any future category's name the same way. Copy (ISS-002) works as before, with author, work and source edition.
5. **Effect on existing days.** Saved days keep the quote they had (FR-INSP-10). Unsaved days get a different quote than before, because the list changes, as happened with the verses in ISS-001.
6. **Before building it in:** the owner reviews a list of the chosen quotes (author, work, text) and can remove any.

**Needs the owner's OK.** This means downloading about 25 more public-domain texts from Project Gutenberg, used only to extract the quotes, as before.

**Owner's answers (2026-10-19).**
- The card label switches between "PHILOSOPHY" and "LITERATURE", and the code must allow future categories.
- Default: both categories on.
- Add authors from Central and South America and from Africa (done above; Borges isn't possible, see above).
- World-religion texts for the VERSE card and Science quotes are a separate future request: ISS-012.

**Proposed tests.**
- **TC-INSP-16 (new, automated):** the literature list has at least 366 distinct quotes from at least 30 authors and 8 languages or regions (including Latin America and Africa), each with author, work, category and source edition, and none duplicates a philosophy quote.
- **TC-INSP-05 (extended):** each combination of ticked categories limits the quotes to those categories; a category added only in the content file appears in Settings and on the card with no code change.
- **TC-INSP-01 (extended):** the card label matches the quote's category.

**Owner's review of the quote list (2026-10-19).** The owner reviewed the list on a review page (quotes numbered L001 onward):
- Fixed: L021 and L037 were too short (L021 now runs to the end of the sentence; L037 is the first four lines of Sonnet 18 and L038 its closing couplet), L217 ended with a poem number, L239 had line breaks in the middle of sentences (fixed for all hard-wrapped prose), L341 started with the speaker's name.
- Removed: L032, L044, L087, L399.
- Added: ten morals from Aesop's fables (Joseph Jacobs, 1894), of which nine remain.
- Approved: 395 literature quotes from 50 authors in 16 regions.

**Fix (2026-10-19).**
- **Content:** `quotes.json` (format version 2) holds the 394 philosophy quotes, unchanged and in the same order, and the 395 literature quotes, each with a `category`. The two categories alternate, so the day-of-year mode (which uses the first 366 quotes) shows both. Literature is spread across regions so consecutive days don't repeat one author.
- **Core:** `Quote.Category` (default Philosophy, so days saved before this change keep a philosophy label); `InspirationSettings.HiddenCategories`; `DailyInspirationService.AvailableCategories()` lists each category with its traditions; the quote filter drops hidden categories and falls back with a warning if all are off. The importer reads an optional `category` column; quotes without one are labelled "Quote".
- **App:** the card label is bound to the quote's category in capitals. Settings: "Show a quote each day", "Built-in quotes", a CATEGORIES row of tick boxes (all ticked by default), and the traditions list shows only the ticked categories' traditions.
- **Requirements:** FR-INSP-02 (quote is philosophy or literature; label shows the category), FR-INSP-04 (now about the philosophy list), FR-INSP-06 (category filter), new FR-INSP-16 (literature list) and FR-INSP-17 (data-driven categories).
- **Tests:** TC-INSP-01, TC-INSP-05 and TC-INSP-10 extended; TC-INSP-16 new. New automated tests: card label per category, category filters and the all-off fallback, a content-only "Science" category, a stored quote without a category, the import category column, Settings category ticks and traditions, and the built-in literature list. They fail before the fix (the category doesn't exist) and pass after. All tests pass: Core 63, App 63, Infrastructure 39 (plus the skipped performance test).
- **Known effect:** days not yet saved get different quotes than before, because the list changed (as with ISS-001). Saved days keep theirs. Quotes imported before this change have no stored category and show as PHILOSOPHY; re-importing the file fixes that.

**To verify:**
1. Open several unsaved days; the label switches between PHILOSOPHY and LITERATURE with the quote.
2. Settings → Verse & quote: both categories are ticked; unticking Literature hides the literature traditions and, after saving, unsaved days show only philosophy quotes.
3. A saved day still shows the quote it had.

---

## ISS-012: Add world-religion texts to the verse and Science as a quote category (future)

| | |
|---|---|
| **Reported** | 2026-10-19 |
| **Type / Severity** | Change / Low |
| **Area** | Daily verse and quote (built-in content, settings) |
| **Failing requirements** | None (new capability). |
| **Requirements to change** | FR-INSP-01 and FR-INSP-03 (the verse card is a Bible verse from the World English Bible today); new requirements for religion sources and a Science category, to be written when this is taken up. |
| **Status** | Open. The owner asked for this as a separate request **for the future**; no analysis or work until the owner says to take it up. |

**Description.** The owner would like:
1. **World religions under the VERSE card:** passages from other religions' scriptures, as options alongside the Bible.
2. **Science quotes:** a Science category for the daily quote, alongside Philosophy and Literature.

**Notes for when it is taken up.**
- **Science:** ISS-011 makes quote categories data-driven, so a Science category would be mostly content work. Public-domain candidates include Newton, Galileo, Kepler, Darwin, Faraday, Pasteur, Ada Lovelace, Maxwell, Poincaré, Ramón y Cajal, and Einstein's *Relativity* (1920 translation). Many modern scientists' writings are still under copyright.
- **Religions:** the verse card and its settings assume one Bible translation (reference, book filter, WEB label). Supporting several scriptures needs the verse source, label and book filter to work per tradition, similar to quote categories. Public-domain English translations exist for, for example, the Quran (Rodwell 1861, Palmer 1880, Pickthall 1930), the Bhagavad Gita (Edwin Arnold 1885), the Hebrew Bible (JPS 1917), the Dhammapada (Müller 1881) and Sikh scripture (Macauliffe 1909). Which traditions to include, and how they appear on the card, are the owner's choice.

---

## ISS-013: Mark each day sad, neutral or happy; tint the calendar and count moods

| | |
|---|---|
| **Reported** | 2026-10-19 |
| **Type / Severity** | Change / Medium |
| **Area** | Entry heading, month calendar, a new summary under the calendar, storage |
| **Failing requirements** | None (new capability). |
| **Requirements to change** | FR-CAL-03 (days with an entry are highlighted today; the mood tint has to work with that highlight). New requirements, proposed as FR-MOOD-01…05 below. |
| **Tests to change** | TC-CAL-03 (highlight), TC-CAL-10 (manual visual check). New tests TC-MOOD-01…06 below. |
| **Status** | Verified by the owner 2026-10-19. Released in version 1.1.0. |

**Description.** The owner wants:
1. For each day, the user can choose a sad, neutral or happy face.
2. Calendar days are tinted red (sad), default (neutral) or green (happy) by that choice. A day with no choice shows the default color.
3. A section under the calendar counts the sad, neutral and happy days over the last 30 days and over the last year.

**Current state (checked 2026-10-19).** A day has no mood today. The calendar (`MonthCalendarControl`, `CalendarDayViewModel`) highlights days that have an entry (FR-CAL-03), outlines planned future days (FR-CAL-04) and shows a dot for open checklist items (FR-CAL-05). The calendar gets those flags from the encrypted catalog (`JournalService.HasEntry`), so it doesn't have to open every entry.

**Proposed change.**
1. **Choosing the mood.** Three face buttons (sad, neutral, happy) next to the day's heading above the editor. Clicking one sets the day's mood; clicking the selected one again clears it. Keyboard and screen-reader names: "Sad day", "Neutral day", "Happy day". **Future days can't be marked** (owner): on a future day the buttons are disabled, with a tooltip saying a mood can be set from that day on.
2. **Storage.** The mood is saved with the day's entry (encrypted, like the rest of the entry) and copied into the encrypted catalog, so the calendar and the counts don't open every entry. Setting a mood on an empty day saves the day, so the mood isn't lost. A mood counts as content: the day is highlighted as having an entry.
3. **Calendar tint.** Sad days get a light red background, happy days a light green one, and neutral or unset days the normal background. Both themes get tints that keep the day number readable. **Each day with a mood also shows a tiny face in the corner** (owner), so the mood can be read without colour. **A day with an entry and no mood keeps today's entry highlight; a mood tint replaces it** (owner). The existing marks stay: today's ring, the selected day, the dashed outline for planned days and the open-item dot.
4. **Counts under the calendar.** A small section, for example:
   - Last 30 days: 😞 4 · 😐 10 · 🙂 12
   - Last 12 months: 😞 40 · 😐 120 · 🙂 150

   Both windows end today and include today (owner): the last 30 days are today and the 29 days before it; the last 12 months run from the same date a year ago plus one day through today. Days with no mood aren't counted. Counts update as soon as a mood changes.
5. **Proposed requirements.**
   - FR-MOOD-01: each day up to and including today can have one mood (sad, neutral or happy) or none; the user sets and clears it from the day's heading. Future days can't be given a mood.
   - FR-MOOD-02: the mood is stored encrypted with the entry and kept in the catalog.
   - FR-MOOD-03: calendar days are tinted red for sad, green for happy, and left at the default background for neutral or no mood, in both themes. Every day with a mood also shows a small face, so the mood doesn't depend on colour. A day with an entry and no mood keeps the entry highlight (FR-CAL-03).
   - FR-MOOD-04: a summary under the calendar counts sad, neutral and happy days over the last 30 days and the last 12 months, both ending today and including today.
   - FR-MOOD-05: the counts and tints update immediately when a mood changes.

**Owner's answers (2026-10-19).**
1. Colour blindness: yes, each day with a mood also shows a tiny face.
2. A day with an entry and no mood keeps the entry highlight; a mood tint replaces it.
3. "Last year" means the 12 months ending today.
4. Future days can't be marked with a mood.
5. Moods aren't searchable.

**Proposed tests.**
- TC-MOOD-01 (automated): set, change and clear a day's mood; it survives locking and reopening. On a future day the face buttons are disabled and the service refuses a mood.
- TC-MOOD-02 (automated): the mood is in the encrypted entry and catalog; no plaintext mood on disk.
- TC-MOOD-03 (automated): calendar cells report sad, happy, neutral and unset correctly; the tint and the small face follow them; a day with an entry and no mood keeps the entry highlight.
- TC-MOOD-04 (automated): counts for a known set of days, including today, the first and last day inside each window, the day just outside each window, and unset days.
- TC-MOOD-05 (automated): changing a mood updates the tint and the counts at once.
- TC-MOOD-06 (manual): tints and marks are distinguishable in light and dark themes at 100 % and 150 % scaling.


**Fix (2026-10-19).**
- **Core:**
  - `Mood` (None, Sad, Neutral, Happy) on `JournalEntry`. `SetMood` refuses a mood for a day after today, and a mood alone keeps the entry.
  - The entry file stores `mood` only when set, so files without a mood look as before.
  - `CatalogEntry.Mood` holds the mood in the catalog; catalogs written before have none.
  - `JournalService.Moods(from, to)` returns the moods for the calendar; `GetMoodSummary()` returns the counts for the last 30 days (today and the 29 days before) and the last 12 months (from the same date a year ago plus one day, through today).
- **Day heading:**
  - Three face buttons sit to the right of the date. Each face is drawn (`MoodFace`), so it looks the same everywhere without needing a colour-emoji font.
  - Clicking a face sets the mood and saves at once; clicking it again clears it.
  - On future days the faces are disabled, and the tooltip says the mood can be chosen once the day arrives.
- **Calendar:**
  - Sad days get a light red background and happy days a light green one; neutral keeps the normal background. A mood tint replaces the entry highlight; days with an entry and no mood keep it.
  - Every day with a mood shows a small face in its top-right corner (owner: colour blindness). The selected day keeps its dark fill and still shows its face.
  - The screen-reader name adds, for example, ", happy day".
- **Counts:** a MOODS card under the calendar shows "Last 30 days" and "Last 12 months", each with a sad, neutral and happy face and its count. It updates whenever a day is saved.
- **Docs:** new requirements FR-MOOD-01…05 (Requirements §2.12) and tests TC-MOOD-01…06 (TestCases §2.12); FR-CAL-03, TC-CAL-03 and TC-CAL-10 updated; architecture §8.1a.
- **Tests:**
  - New: `MoodTests` (Core), `MoodViewModelTests` (App), the encrypted-vault check (Infrastructure), and a real-window layout test at the smallest window size.
  - A screenshot of the main window with sad, neutral and happy days was checked by eye.
  - All tests pass: Core 67, App 88, Infrastructure 40 (plus the skipped performance test).

**To verify:**
1. On today, click the happy face. Today's date turns green with a small smile, and the MOODS card counts it.
2. Click the sad face: the day turns red. Click it again: the mood is cleared.
3. Open a day next week: the faces are greyed out.
4. Mark a few earlier days and check the counts for the last 30 days and the last 12 months.
5. Lock and unlock: the moods are still there.


---

## ISS-014: A ticked checklist item's strikethrough also runs through its checkbox

| | |
|---|---|
| **Reported** | 2026-10-19, with a screenshot (October 13, 2026: "More new stuff" ticked) |
| **Type / Severity** | Bug / Low (display only; nothing stored is wrong) |
| **Area** | Editor: checklist items |
| **Failing requirements** | FR-EDT-09: ticked checklist items are shown with strikethrough. The strikethrough is meant for the item's text; it is also drawn through the checkbox. |
| **Failing tests** | None failed. **Test gap:** TC-EDT-11 checks that the text is struck through when ticked and not after unticking, and that the stored format is unchanged. It doesn't check that the checkbox stays clear of the line. |
| **Status** | Verified by the owner 2026-10-19. Released in version 1.1.0. |

**Description.** When a checklist item is ticked, its text is struck through as expected, but a second line also appears. It runs behind the checkbox and sits slightly lower than the strikethrough on the text.

**Steps to reproduce.** Make a checklist item with some text and tick it. A line crosses the checkbox, a little below the line through the text.

**Cause.** When an item is ticked, the editor puts the strikethrough on the whole paragraph (`EditorOperations.ApplyCheckedDecoration` sets `Paragraph.TextDecorations`), so the strikethrough is not stored in the text runs. The paragraph also holds the checkbox, which is the paragraph's first inline. So WPF draws the strikethrough across the checkbox too. The checkbox is centred on the line rather than sitting on the text's baseline, so the line through it is drawn at a slightly different height from the line through the text.

**Proposed fix.**
1. Keep the strikethrough display-only (FR-EDT-09, TC-EDT-11), but stop it reaching the checkbox. The checkbox's container gets its own empty decoration setting, which overrides the paragraph's. If WPF still draws the line there, the fallback is to put the strikethrough on the text inlines only and mark it as display-only, so saving doesn't store it as the user's own formatting.
2. Ticking, unticking, loading a saved entry with ticked items, and pasting ticked items (ISS-010) all go through the same code, so all four are fixed together.

**Proposed tests.**
- **TC-EDT-11 (extended, automated):** after ticking, the checkbox's container has no strikethrough while the text does. After unticking, neither does. Also check this for an item loaded already ticked and for a pasted ticked item. The stored run format stays unchanged. This fails before the fix.
- **TC-EDT-11 (manual step):** tick an item and check that no line crosses the checkbox, in light and dark themes at 100 % and 150 % scaling.

**Fix (2026-10-19).**
- **First attempt, which didn't work:** giving the checkbox's container its own empty decoration setting. WPF adds the paragraph's decorations to everything inside it, so the line stayed.
- **Fix:**
  - The checkbox is now a `ChecklistCheckBox` (`src/Journal.App/Controls/BlockControls.cs`). It takes up the checkbox plus the 6-pixel gap before the text, and paints the editor's background under both.
  - WPF draws the paragraph's strikethrough behind embedded controls, so the line now shows only through the text.
  - The strikethrough stays display-only on the paragraph, so nothing stored changes.
  - Saved entries, newly ticked items and pasted items (ISS-010) all create their checkbox the same way, so all of them are fixed.
- **Side effects:**
  - Clicking in the small gap between the checkbox and the text now ticks the item.
  - If you select a ticked line, the selection colour doesn't show in that gap.
- **Test:** TC-EDT-11 is extended with an automated pixel test. It renders a ticked item with and without its strikethrough. The checkbox and the gap must match pixel for pixel, while the text area must differ. It failed before the fix and passes after.
- All tests pass: Core 63, App 83, Infrastructure 39 (plus the skipped performance test).

**To verify:** tick a checklist item. The line goes through the text only, with nothing behind or beside the checkbox. Untick it, and the line is gone.

---

## ISS-015: Settings: a section per quote category, and tick-box lists in alphabetical columns

| | |
|---|---|
| **Reported** | 2026-10-19 |
| **Type / Severity** | Change / Low |
| **Area** | Settings window, *Verse & quote* tab |
| **Failing requirements** | None (layout change). |
| **Requirements to change** | FR-INSP-06 (how the category and tradition choices are laid out, and that tradition choices belong to their category). |
| **Tests to change** | TC-INSP-05 (Settings part); a new automated layout check. |
| **Status** | Verified by the owner 2026-10-19. Released in version 1.1.0. |

**Description.** The owner wants:
1. Philosophy and Literature each in their own section of the quote settings, with their own choices. Philosophy comes first; the user scrolls down to reach Literature.
2. The lists of tick boxes ordered alphabetically **down columns** (A, B, C down the first column, then on to the next), not across rows.

**Current state (checked 2026-10-19).**
- The quote card has "Show a quote each day", the source (built-in or imported), then one CATEGORIES row (Literature, Philosophy) and one TRADITIONS list. That list holds the traditions of all ticked categories mixed together, flowing left to right across rows. The verse card's BOOKS list also flows across rows, in Bible order.
- **Shared tradition name:** "Chinese" is a tradition in both Philosophy (Confucius, Lao Tzu…) and Literature (Li Po, Tu Fu…). The filter matches traditions by name only, so ticking "Chinese" picks Chinese quotes from both categories. With separate sections, ticking Chinese under Philosophy would also affect Literature, which would be confusing.

**Proposed change.**
1. **One section per category**, Philosophy first, then the others alphabetically (Literature, and later Science, ISS-012). Each section has:
   - a heading (PHILOSOPHY, LITERATURE);
   - a tick box "Include philosophy quotes" (on by default), which replaces the CATEGORIES row;
   - "Only these traditions (none ticked = all)", listing only that category's traditions; it is greyed out when the category is turned off.
   The settings tab already scrolls, so Literature sits below Philosophy.
2. **Tradition choices belong to their category.** A ticked tradition is saved together with its category, for example Philosophy › Chinese. Ticking Chinese under Philosophy no longer pulls in Chinese literature. Settings saved before this change are read as before: a tradition ticked there applies to each category that has it.
3. **Column order.** Each tick-box list is laid out in columns, filled top to bottom and then left to right (3 columns across the window's width). Traditions are sorted alphabetically; the BOOKS list keeps Bible order (owner).
4. "Show a quote each day" and the built-in / imported choice stay at the top of the quote card.

**Owner's answers (2026-10-19).**
1. **Bible books:** columns, in Bible order (Genesis … Revelation down the columns), not alphabetical.
2. **One card:**
   - The main window keeps a single quote card. Its label says PHILOSOPHY or LITERATURE, depending on the quote shown; that is already how it works since ISS-011, so the main window doesn't change.
   - In Settings, the Philosophy section and the Literature section each have their own tick box.
   - Both sections sit in the same scrollable quote card, under the shared "Show a quote each day" and source choice.

**Proposed tests.**
- **TC-INSP-05 (extended, automated):**
  - Settings list one section per category, Philosophy first.
  - Each section lists only its own traditions, alphabetically.
  - Turning a category off greys out its traditions.
  - Ticking Chinese under Philosophy saves Philosophy › Chinese, and the day's quotes then include no Chinese literature.
  - Settings saved before the change still load.
- **New automated layout test:** in the real Settings window, the tradition lists run top to bottom in each column, in alphabetical order, and the BOOKS list runs down the columns in Bible order, with no item cut off.

**Fix (2026-10-19).**
- **Settings, Verse & quote tab:**
  - Under "Show a quote each day" and the source choice, the quote card has one section per category: PHILOSOPHY first, then LITERATURE (and later ones alphabetically), all in the same scrolling tab.
  - Each section has an "Include … quotes" tick box and its own "Only these traditions" list, greyed out when the category is off.
  - The main window is unchanged: one quote card, labelled by the quote's category.
- **Columns:** a new `ColumnPanel` (`src/Journal.App/Controls/ColumnPanel.cs`) lays out tick boxes in 3 equal columns, filled top to bottom and then left to right. Traditions are alphabetical; BOOKS keeps Bible order (owner).
- **Tradition choices belong to their category:**
  - A ticked tradition is saved as "Category/Tradition", for example "Philosophy/Chinese".
  - A category with nothing ticked keeps all its quotes, so ticking Chinese under Philosophy no longer pulls in Chinese literature.
  - A tradition saved before this change (no category) still applies to every category that has it, and shows ticked in each of those sections.
- **Docs:** FR-INSP-06 and TC-INSP-05 updated; architecture §8.2 updated.
- **Tests:**
  - TC-INSP-05 extended. Core: the per-category filter, and the shared "Chinese" tradition with old and new settings. App: the view model's sections. A new real-window layout test (`SettingsLayoutTests`) checks section order, column order for traditions and books, and that no tick box is cut off.
  - The tradition test changed meaning: with Philosophy › Stoic ticked, philosophy quotes are all Stoic, and literature quotes still appear.
  - All tests pass: Core 64, App 84, Infrastructure 39 (plus the skipped performance test).

**To verify:** open Settings → Verse & quote.
1. PHILOSOPHY and its traditions come first; scroll down to LITERATURE and its traditions.
2. The lists read down the columns: traditions A to Z, books Genesis onward.
3. Unticking "Include literature quotes" greys out the literature traditions; after saving, unsaved days show only philosophy quotes.
4. Ticking Chinese under Philosophy only and saving gives Chinese philosophy as the only philosophy, while literature quotes still appear.

---

## ISS-016: Closing the window doesn't exit; the app keeps running in the background

| | |
|---|---|
| **Reported** | 2026-10-19 |
| **Type / Severity** | Bug / High (the app can't be closed normally; a hidden copy blocks starting it again) |
| **Area** | Closing the app (`AppController`) |
| **Failing requirements** | No requirement says plainly that closing the main window exits the app; that is the expected behaviour (proposed new **FR-SEC-21**). Affected: FR-ENT-05 (save on close), FR-ATT-06 (temporary files deleted on exit), FR-SEC-19 (one instance: a leftover hidden copy blocks a new start). |
| **Failing tests** | None failed. **Test gap:** no test closes the real main window through its close button. TC-ENT-05 covers saving on close through the view model only. |
| **Status** | Verified by the owner 2026-10-19. Released in version 1.1.0. |

**Description.** Clicking the window's close button (X) makes the journal content disappear, but the window and the app stay open. A second click closes the window, yet Journal sometimes stays in Task Manager's process list. **Expected:** one click saves, locks, closes the window and ends the process.

**Evidence (2026-10-19).**
- **App log:** a normal exit logs "Locked (Exit)" followed by "Exited". Today at 16:21, 16:46 and 16:58 the log has "Locked (Exit)" with no "Exited", so the shutdown didn't finish. At 16:05 and 16:25 it did.
- **Experiment:** a window that, while closing, cancels the close and calls `Close()` again gets an `InvalidOperationException` ("Cannot … call … Close … while a Window is closing") and stays visible. This matches what the owner saw.

**Cause.**
1. When the main window is closing, `AppController.OnMainClosing` cancels the close and starts `ExitAsync`: save, lock, then shut down. It doesn't wait for that to finish.
2. Locking runs straight through when nothing needs to wait, for example when nothing is left to save. Then, still inside the window's closing event, the lock code (`OnLocked`):
   - clears the window's content (the journal disappears);
   - unhooks the closing handler;
   - calls `Close()` on the window that is already closing. WPF refuses that and throws.
3. The error stops the exit sequence before it shuts the app down. It is swallowed, because nothing waits for `ExitAsync`. So:
   - the window stays open and empty;
   - `Application.Shutdown()` is never called;
   - the vault services aren't disposed and temporary files aren't cleaned up.
4. On the second click the closing handler is gone, so the window simply closes. The app is set to shut down only when told to, and nobody tells it to, so the process keeps running with no window.
5. That hidden copy still holds the single-instance lock. Starting Journal again just signals the hidden copy, so the app seems not to start until the process is ended in Task Manager.
6. It happens only sometimes, because when locking does have to wait (a save, for example), the lock code runs after the closing event has finished, and then everything works.

**Proposed fix.**
1. **Close without fighting WPF:** when the X is clicked, cancel that close as now, run the save and lock, and only then close the window and shut down. The lock code must not call `Close()` on a window that is closing; it hides the window, or leaves closing to the exit step.
2. **Always shut down:**
   - The exit step runs inside `try/finally`, so `Application.Shutdown()` happens even if saving or locking fails. A failure is logged and shown, as other errors are.
   - The main window's `Closed` event also shuts down whenever the app is exiting, so no path can leave the process with no window.
3. **Safety net:** once shutdown starts, anything left running in the background (timers, the single-instance listener) can't keep the process alive. The listener thread is already a background thread; this is checked.
4. **Requirement:** new **FR-SEC-21**: closing the main window saves the open day, locks the vault, deletes temporary files and ends the process, in one click, even when nothing needs saving.

**Proposed tests.**
- **TC-SEC-26 (new, automated):** with the real `AppController` and main window on a test vault, click close (`Close()`) in two cases: nothing to save (the case that fails today), and unsaved text.
  - In both cases the window closes on the first try and shutdown is requested exactly once.
  - The text is saved, the vault is locked, and no error is raised.
  - This fails before the fix.
- **TC-SEC-26 (automated):** make the save fail during close. The app still shuts down and logs the error.
- **TC-SEC-26 (manual):** close with the X, both after editing and without editing. The window closes at once and Journal disappears from Task Manager within a few seconds; starting it again works.

**Fix (2026-10-19).**
- **`MainWindowCloser`** (new, `src/Journal.App/Services/MainWindowCloser.cs`) handles the main window's close:
  - The first close is cancelled, and the save and lock start only after the closing event has returned.
  - Then, in a `finally` block, it closes the window for real and asks the app to shut down. This happens even when saving or locking fails; the failure is logged and shown.
  - Further clicks while it's working are ignored.
- **`AppController`:**
  - The lock code no longer closes the main window while the app is exiting; the closer does that once locking has finished.
  - For a normal lock (the Lock button, idle, Windows lock), the closer is detached and the window closes as before.
  - `Exit()` can safely run more than once.
- **Requirement and tests:**
  - New requirement **FR-SEC-21**.
  - New test **TC-SEC-26**, because TC-SEC-21 was already taken. Its automated part is 4 tests in `CloseTests.cs`, run on a real window: nothing to save (the failing case), a slow save with a second click, a failing save, and a detached closer.
  - The experiment recorded above reproduces the old failure.
  - All tests pass: Core 67, App 92, Infrastructure 40 (plus the skipped performance test).
- **Leftover process:** the hidden Journal process from 4:54 PM (PID 42932) was this bug. Its log ends with "Locked (Exit)", so the journal was saved and locked before it hung. The owner ended it in Task Manager.

**To verify:**
1. Open Journal, don't type anything, and click the X. The window closes at once, and Journal leaves Task Manager within a few seconds.
2. Open it again (it starts normally), type something, and click the X. It closes in one click, and the text is there next time.
3. Lock and unlock with the Lock button. That still works as before.

