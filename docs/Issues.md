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
| ISS-007 | Windows Hello prompts are left open after the app closes or a test ends | Bug | Medium | FR-SEC-08, FR-SEC-09; gap → FR-SEC-20 (proposed) | TC-SEC-09 (gap) → TC-SEC-22 | Verified |
| ISS-008 | Windows Hello prompts automatically on the unlock screen, including after every lock | Change | Medium | FR-SEC-08 (change) | TC-SEC-20 (change), TC-SEC-23 | Verified |
| ISS-009 | Windows Hello prompt appears on the main display, not the one showing the app | Bug | Low | Design §14; gap → NFR-USE-03 | TC-SEC-20 (manual) fails on multiple displays; gap → TC-SEC-24 (manual), TC-SEC-25 | Fixed (awaiting verification) |
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

---

## ISS-007: Windows Hello prompts are left open after the app closes or a test ends

| | |
|---|---|
| **Reported** | 2026-10-16 |
| **Type / Severity** | Bug / Medium |
| **Area** | Unlocking, app lifecycle, test tooling |
| **Failing requirements** | FR-SEC-08 and FR-SEC-09 (the unlock flow): a Windows Hello request outlives the unlock window and the app. No requirement says a pending request must be cancelled, so this is also a requirement gap. Proposed **FR-SEC-20**: a pending Windows Hello request shall be cancelled when the unlock window closes, when the app exits, and when the journal is unlocked another way (recovery code). |
| **Failing tests** | None failed. **Test gap:** TC-SEC-09 only checks that a cancelled or failed prompt leaves the journal locked; nothing checks that closing the window cancels an open prompt. |
| **Status** | Verified by the owner 2026-10-16. Approved 2026-10-16; option (a) chosen. |

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
| **Status** | Verified by the owner 2026-10-16. Approved 2026-10-16. |

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
| **Status** | Fixed (awaiting verification). Approved 2026-10-16; option C chosen. |

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
