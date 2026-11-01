# Linework for Windows Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build a WPF version of Linework 2.0 in `windows/` that reads the same library folder, in three stages that each end with an installer.

**Architecture:** Port `LineworkCore` type by type to a platform-free `Linework.Core`, tested on the Mac against fixtures made by the Mac code. Then build `Linework.App` screens on an observable `LibraryStore`, verified by a debug harness and screenshots on GitHub's Windows machines.

**Tech Stack:** C#, .NET 10, WPF, System.Text.Json, xUnit, ONNX Runtime, Inno Setup, GitHub Actions.

**Spec:** `docs/superpowers/specs/2026-11-01-linework-windows-design.md`. Behaviour authority for anything unstated: the matching file in `Sources/`.

## Global Constraints

- .NET 10, x64, Windows 10 1809 minimum. Third-party code: ONNX Runtime, one model, and xUnit in tests only.
- Never change app code in `Sources/`. Test-only additions under `Tests/` and `shared/fixtures/` are allowed.
- Library files must stay readable by the Swift decoders: indented sorted-key JSON, `{ "items": …, "version": 1 }`, dates exactly `yyyy-MM-ddTHH:mm:ssZ` with no fractional seconds, uppercase UUIDs, null optionals omitted, `/` unescaped.
- All parsing and formatting of files uses invariant culture and the Gregorian calendar.
- Copy matches the Mac app exactly, apart from Ctrl for ⌘.
- Debug tools compile only in Debug and refuse to run without `LINEWORK_LIBRARY`.
- Commit after each task with no attribution lines. Push only at stage ends, which trigger Windows CI. Mac test command: `dotnet test windows/Linework.Core.Tests`.
- Don't claim offscreen renders prove Mica, sound, fullscreen, drag and drop or DPI behaviour.

## Review Focus

1. **Non-English Windows** (es-MX decimal commas, non-Gregorian calendars): files and dates are unaffected. Test in Task 3.
2. **A file briefly locked** by antivirus, indexer or sync while saving: retried, then reported, never half-written. Test in Task 4.
3. **File names from a Mac in decomposed Unicode (NFD)**, with JSON and disk differing in form: matched as one file, not marked missing. Test in Task 4.
4. **Paths over 260 characters or with non-ASCII folders:** work or report clearly. Test in Task 4.
5. **High-DPI or a session opened on a second monitor:** fullscreen covers the right monitor, and images render at device pixels. Harness assertion in Task 7, then live check.

---

## Stage 1: Library and sessions

### Task 1: Solution, CI and shared fixtures

**Files:**
- Create: `windows/Linework.sln`, `windows/Linework.Core/*`, `windows/Linework.Core.Tests/*`, `windows/Linework.App/*`, `windows/.gitignore`, `windows/README.md`, `windows/Directory.Build.props`, `.github/workflows/windows.yml`, `scripts/extract-plan-json.py`, `shared/fixtures/*`
- Test: `Tests/LineworkCoreTests/SharedFixtureTests.swift`

**Interfaces:**
- Produces: `LineworkVersion.Current` read from `Directory.Build.props`; embedded resource `plan.json`; fixtures `shared/fixtures/library/` (a complete sample library), `shared/fixtures/plan-expectations.json` (schedule mappings, tick keys including `-r1`, `-r2c2` and forever, streaks, tick summaries), `canvas-sample.json`.

- [ ] **Step 1:** Create the solution, with `Linework.App` using `EnableWindowsTargeting` so it compiles here. Run `dotnet build windows/Linework.sln -p:EnableWindowsTargeting=true`. Expected: success.
- [ ] **Step 2:** Add `SharedFixtureTests.swift`. With `LINEWORK_WRITE_FIXTURES=1` it writes the fixtures using the real Swift encoders; otherwise it asserts they still decode and match. Run `LINEWORK_WRITE_FIXTURES=1 swift test --filter SharedFixtureTests`, then `swift test`. Expected: fixtures exist and all pass.
- [ ] **Step 3:** Write `extract-plan-json.py` and add the failing test `PlanDataMatchesSwiftSource`. Run it, then generate. Expected: fails, then passes.
- [ ] **Step 4:** Add `windows.yml` on `windows-latest`, triggered by push to `main` touching `windows/**` or `shared/**`, and by hand: restore, test, publish, harness, screenshots and installer artifacts.
- [ ] **Step 5:** Commit: `feat: Windows solution, CI and shared fixtures`.

### Task 2: Plan logic

**Files:** `windows/Linework.Core/Plan/{PracticePlan,PlanSchedule,PlanTick,PlanProgress,TodaysPractice,PracticeCalendar,BenchmarkSchedule,PlanDayKey}.cs`; tests beside them.

**Interfaces:**
- Produces, mirroring Swift: `PlanDayRef` (`PlanRef(SequenceIndex, Weekday)`, `ForeverRef(Day)`); `PlanSchedule(plan, DateOnly start, repeats)` with `RefOn`, `TodayRef`, `DateOf`, `DayFor`, `TitleFor`, `TickKey`, `CanAddRepeat`, `CanUndoLastRepeat`, `Adding`; `PlanTick { Key, CountsOn, CheckedAt }` and `PlanTick.CountsOnFor(scheduled, today)`; `PlanProgress.{Status, Celebration, PracticeDays, CurrentStreak, LongestStreak, TickSummary, HeatmapSeconds}`.
- Dates are `DateOnly`, with no time zone maths inside Core. The App converts `DateTime.Now` to `DateOnly`.

- [ ] **Step 1:** Port `PlanTickTests`, `PlanScheduleTests`, `PlanProgressTests`, `PracticeCalendarTests`, `BenchmarkScheduleTests`, `PlanSummaryTests`, plus `MatchesSharedExpectations` over `plan-expectations.json`. Run: expected compile failure.
- [ ] **Step 2:** Implement. Run `dotnet test windows/Linework.Core.Tests`. Expected: all pass.
- [ ] **Step 3:** Commit: `feat: plan logic for Windows`.

### Task 3: Sessions, crops, notes and copy

**Files:** `windows/Linework.Core/{Session/{SessionRunner,SessionQueue,DurationFormat},Crop/CropMath,Copy/{LineworkCopy,SeaNotes},Glass,ClockFormat}.cs`; tests.

**Interfaces:**
- Produces: `SessionRunner` with `Tick(dt)`, `Next`, `Previous`, `TogglePause`, `AttemptHandled`, `End`, `RunnerEvent` flags including `DrawingStarted`; `QueueBuilder.Build`; `SessionLength`; `CropMath` using `NormalizedRect`; `SeaNotes.{Next, PracticeMilestone, PracticeLine, NextInterval, NextPracticeInterval}`; `LineworkCopy` strings.

- [ ] **Step 1:** Port `SessionRunnerTests`, `SessionQueueTests`, `SessionLengthTests`, `DurationFormatTests`, `CropMathTests`, `CropEditingTests`, `SeaNotesTests`, `LineworkCopyTests`. Add `FormattingIgnoresCulture`, which runs duration and JSON number formatting under `es-MX` and `ar-SA`. Run: expected compile failure.
- [ ] **Step 2:** Implement, using an injected `Random` for tests. Run the Mac test command. Expected: all pass.
- [ ] **Step 3:** Commit: `feat: session, crop and notes logic for Windows`.

### Task 4: Library files

**Files:** `windows/Linework.Core/Library/{LibraryRecords,LibraryJson,LibraryFiles,LibraryFolder,PhotoScan,FileHash}.cs`, `Freehand/{FreehandCanvas,FreehandFiles,CanvasDirectory}.cs`; tests.

**Interfaces:**
- Produces: records matching Task 4 of the Mac plan; `LibraryFiles(root)` with `Read<T>` returning `Missing|Loaded|Damaged` and throwing `NewerFormatException` or `UnreadableException`; `Write<T>`; `FreeAsideName`; `LibraryFolder.{Classify, Create, MondayOf}`; `PhotoScan.{Decide, RecordsLookComplete, IsImageName, UniqueName}`; `CanvasDirectory`; `FreehandFiles`.
- `canvas.json` geometry follows Swift's encoding: `CGPoint` as `[x,y]`, `CGSize` as `[w,h]`, `CGRect` as `[[x,y],[w,h]]`, confirmed by `canvas-sample.json`.

- [ ] **Step 1:** Port `LibraryFilesTests`, `LibraryFolderTests`, `PhotoScanTests`, `CanvasDirectoryTests`, `FreehandFilesTests`, `FreehandCanvasTests`, `FreehandEditTests`. Add:

```csharp
[Fact] void OpensTheMacLibraryFixture()      // every file Loaded; counts match
[Fact] void RewritesMacFilesByteForByte()    // read then write equals the fixture
[Fact] void ALockedFileIsRetriedThenReported() // retries for about 2 s; on failure throws, leaving the original
[Fact] void NfdNameOnDiskMatchesNfcRecord()  // not marked missing or re-added
[Fact] void LongAndNonAsciiPathsWork()       // over 260 characters, with "é" and "日本"
[Fact] void WindowsReservedCanvasNamesRejected() // CON, NUL, "a?", "a.", "a "
```

Tests of locking, long paths and reserved names that only mean something on Windows run there and skip elsewhere.

- [ ] **Step 2:** Run: expected compile failure.
- [ ] **Step 3:** Implement. `System.Text.Json` with a contract modifier sorting properties, `UnsafeRelaxedJsonEscaping`, and custom date and UUID converters. Atomic writes use a temp file and `File.Replace`/`File.Move`.
- [ ] **Step 4:** Run the Mac test command, then `swift test --filter SharedFixtureTests`, which also decodes files written by C# from `shared/fixtures/written-by-windows/`. Expected: all pass both ways.
- [ ] **Step 5:** Commit: `feat: library files for Windows, readable by both apps`.

### Task 5: App shell

**Files:** `windows/Linework.App/{App.xaml, MainWindow.xaml, Theme/*, Library/{LibraryStore,AppModel,LibraryLocation,Settings,RecycleBin}.cs, Views/{LibraryChooserView,WelcomeView,MainMenuView,TopBar,AppShell,SettingsWindow,AboutWindow,HeroClock}, Resources/Fonts/*, Debug/Harness.cs}`

**Interfaces:**
- Produces: `LibraryStore : ObservableObject` with the Swift store's public operations and same save rules (ticks and sessions at once, others after 0.5 s, flush on deactivate and exit); `AppModel.{Open, Create, CloseForSwitch, SaveOrConfirmLosing}`; `Settings` values from `%APPDATA%`; `Harness.Check(name, ok)`, `Harness.Shoot(name, element, w, h)`; `Theme` light and dark resources from `Theme.swift`; `GlassWindow` applying Mica if available.

- [ ] **Step 1:** Build static font instances using fontTools in the scratch folder, and commit only the TTFs with the license.
- [ ] **Step 2:** Implement the store and model, and port the store checks from `DebugLibraryChecks.swift` (missing library, unreadable, damaged, lost records, name reuse, plan rewrite, time zone) to the harness. Run under `dotnet build`. Expected: compiles here; they execute in Task 8.
- [ ] **Step 3:** Implement the views to match the Mac layouts, with Ctrl shortcuts and the slim File and Go menu, and add harness shots for chooser, welcome, menu, settings, about, in light and dark.
- [ ] **Step 4:** Commit: `feat: Windows app shell, library store and chooser`.

### Task 6: Library, import and auto-crop

**Files:** `Views/{LibraryView,ImagePreviewView,CropEditorView,ThumbnailView}`, `Library/{ImportService,PhotoScanner,ThumbnailCache,ImageRendering,AutoCropper}.cs`, `LICENSES/*`

**Interfaces:**
- Produces: `ImportService.ImportAsync(urls, store, setId)`, `PhotoScanner.RunAsync(store)`, `ImageRendering.RenderAsync(request)` with an LRU cache capped at 256 MB, `AutoCropper.CropAsync(path) -> (NormalizedRect, bool found)`.

- [ ] **Step 1:** Choose the model: small, person class, CPU, Apache or MIT licensed. Record its name, source, hash and license in `LICENSES/`. If nothing suitable is under 25 MB, ship manual crop only: no ONNX Runtime or model, every import arrives as the full frame with no "no figure found" badge, the crop editor's Auto button is hidden, and this is reported to the user. The user decided this.
- [ ] **Step 2:** Add Core tests for detection post-processing: `UnionOfPersonBoxesIsPadded`, `NoPersonFallsBack`. Run, watch fail, implement in Core, pass.
- [ ] **Step 3:** Implement the rest. Images keep the EXIF orientation. Unreadable formats get a placeholder and a notice.
- [ ] **Step 4:** Port the photo checks from `DebugLibraryChecks` to the harness, and add `ModelFindsAPersonInTheSample`, using a redistributable test image. Add library, preview and crop shots.
- [ ] **Step 5:** Commit: `feat: Windows library, import and auto-crop`.

### Task 7: Timed sessions

**Files:** `Views/{SessionSetupWindow,SessionPlayerWindow,AttemptSheet,PracticeNoteView}`, `Platform/{KeepAwake,SoundCue}.cs`, `Resources/Sounds/*`, `scripts/make-sounds.py`

**Interfaces:**
- Produces: `KeepAwake.Begin() -> IDisposable`, `SoundCue.{Tick, Chime, Note, Celebrate}`, `SessionStore` with the Swift fields.

- [ ] **Step 1:** Generate four short original WAVs with `make-sounds.py` (standard library only). Commit them with the script.
- [ ] **Step 2:** Implement setup and player. The timer uses real elapsed time and pauses across sleep. Fullscreen covers the monitor the main window is on. Keys match the Mac. Plan-block starts come in Stage 2.
- [ ] **Step 3:** Port the session checks: natural end records, Esc records, skipping doesn't record, keep-awake held and released (`powercfg /requests` on CI, or a flag where denied). Add `PlayerCoversItsMonitorAtDevicePixels` and shots.
- [ ] **Step 4:** Commit: `feat: Windows timed sessions`.

### Task 8: Stage 1 installer and gate

**Files:** `windows/installer/Linework.iss`, `windows/README.md`, `windows.yml`

- [ ] **Step 1:** Write the Inno Setup script: per-user install, Start menu, uninstaller that leaves the library. Hide Stage 2 and 3 sections through `FeatureFlags`.
- [ ] **Step 2:** Push. Run `gh run watch` on `windows.yml`. Expected: all checks pass, zero `FAIL`, artifacts present. Fix anything red.
- [ ] **Step 3:** Download screenshots with `gh run download`, review them at low resolution against the Mac's, and correct visible differences.
- [ ] **Step 4:** Give the user the installer and the Stage 1 live checks. Don't wait for them to continue.
- [ ] **Step 5:** Commit: `feat: Windows stage 1 installer`.

## Stage 2: Plan and s.e.a.

### Task 9: Today, Path and Calendar

**Files:** `Views/{TodayView,DayPlanCard,PathView,CalendarHeatmapView,PlanGuideWindow}`, plan-start wiring in player and main menu.

- [ ] **Step 1:** Implement to match the Mac. Any lesson can be ticked, repeats work, and the Calendar opens a popup card.
- [ ] **Step 2:** Port the ticking checks from `DebugLibraryChecks` and add shots in light and dark.
- [ ] **Step 3:** Commit: `feat: Windows plan screens`.

### Task 10: Celebrations and notes

**Files:** `Views/{CelebrationView,PracticeNoteView,SeaNoteCapsule}`

- [ ] **Step 1:** Implement confetti, honouring Windows' "animation effects" setting, and timed-session notes with chime, using the Core logic.
- [ ] **Step 2:** Add harness checks that the first-pose note appears and hides while paused, and a confetti shot.
- [ ] **Step 3:** Enable Stage 2 flags, push, watch CI and review shots as in Task 8. Give the user the installer.
- [ ] **Step 4:** Commit: `feat: Windows stage 2`.

## Stage 3: Benchmarks and Freehand

### Task 11: Progress

**Files:** `Views/{BenchmarkProgressView,ComparisonWindow}`; attempt sheet wired fully.

- [ ] **Step 1:** Implement. Add harness checks for a due benchmark playing first, Later, Skip and Save, and shots.
- [ ] **Step 2:** Commit: `feat: Windows benchmarks`.

### Task 12: Freehand

**Files:** `Views/{FreehandView,FreehandBar,CanvasMenu,FreehandRadialMenu,CanvasInput}`, `Library/FreehandStore.cs`

- [ ] **Step 1:** Implement the store from `FreehandStore.swift`, and the views. WPF hit-tests through the store as on Mac. Include time notes.
- [ ] **Step 2:** Port the relevant Freehand checks from `DebugFreehandToolChecks.swift`, and canvas and review checks from `DebugLibraryChecks`. Add shots.
- [ ] **Step 3:** Commit: `feat: Windows Freehand`.

### Task 13: Final verification and hand-over

- [ ] **Step 1:** Open the importer's real library copy with a read-only count check on the Mac: `dotnet test` with `LINEWORK_REAL_LIBRARY` pointing at a scratch copy. Expected: same counts as the Swift `--print-counts`.
- [ ] **Step 2:** Enable all flags, push, watch CI. Expected: all pass, zero `FAIL`.
- [ ] **Step 3:** Run one fresh whole-branch review on the highest-risk areas: file compatibility, saving, deleting and threading. Fix Critical and Important findings with a failing test first.
- [ ] **Step 4:** Write the README's install section and SmartScreen steps.
- [ ] **Step 5:** Push. Give the user the installer, the rulings, the deferred minors and the live check list.
