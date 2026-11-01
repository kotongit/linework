# Linework 2.0: Design

Date: 2026-11-01
Repo: https://github.com/kotongit/linework (private)
Starting point: the `GestureDrawing` project, which stays in place as an archive.

## Purpose

Rebuild Linework as a clean new version with the same features, in a new repo, with the final name. The app holds nothing of the user's: all their data lives in a normal folder they can open, back up and move, like an Obsidian vault. It installs from a `.dmg` on any Mac that runs macOS 27.

## Success criteria

- Every feature in the current app works the same, apart from the changes listed in §10.
- The existing data arrives complete: 59 photos, 5 sessions, 1 benchmark, 1 preset, plan ticks and the canvas. Counts are checked.
- Quitting the app and opening the library folder in Finder shows photos and readable text files, with nothing needed from the app to understand them.
- Deleting the app and reinstalling it, then choosing the same folder, restores everything except per-machine preferences.
- A fresh user can install from the `.dmg`, create a library, import photos and run a session.
- The word `GestureDrawing` appears nowhere in the code, the app or the library. The import script (§7) is the one exception, because it reads the old data.
- Any lesson can be ticked at any time, and the plan never names a calendar date (§4A).

## Out of scope

- Developer ID signing and notarization. The scripts leave one clear place to add them later.
- iCloud or other sync features. The folder can be put in a synced location, but the app does nothing special for it beyond §3.5.
- Intel Macs, iOS, more than one window onto a library, plugins, search, or editing the plan in the app.
- New features other than the Import button and canvas list.
- Fixing the other known limits: multi-select, deleting presets and benchmark attempts, and showing due benchmarks on Today.

## 1. Identity

| Item | Value |
|---|---|
| App | `Linework.app`, shown as "Linework" |
| Full name, in About only | Linework Drawing Companion, by hyprsea solutions |
| Bundle ID | `com.hyprsea.linework` |
| Version | 2.0, kept in one place that the build reads |
| Minimum system | macOS 27, Apple silicon |

The old app's data and preferences aren't reused, so both apps can run side by side until the switch.

## 2. Repo

```
Package.swift
README.md
LICENSES/            Inter Tight's Open Font License
Resources/           Info.plist, AppIcon.icns, Fonts/
Sources/LineworkCore/  Pure logic and file formats, no UI
Sources/Linework/      The app: SwiftUI views, stores, Vision, rendering, sound
Tests/LineworkCoreTests/
scripts/             build-app.sh, make-dmg.sh, generate-plan.py,
                     plan-summaries.json, make-icon.swift,
                     import-gesturedrawing.py
docs/superpowers/    This spec and the implementation plan
```

- The history starts fresh. The old repo's 60 commits stay in the archive.
- Swift Package Manager and scripts only, with no Xcode project.
- Source is moved across piece by piece and tidied as it comes, rather than rewritten. Its existing tests come with it.
- Build outputs and personal data are ignored. The plan PDF is not committed; the generated Swift file is.

## 3. The library folder

### 3.1 Layout

```
Linework Library/
  library.json        Marks the folder as a library, with its format version
  Photos/             Reference photos
  Drawings/           Photos of benchmark attempts
  Canvases/
    Figure refs/
      canvas.json
      Images/
  Progress/
    photos.json       Per-photo details
    benchmarks.json   Benchmark settings and attempts
    sets.json
    presets.json
    plan.json         The library's plan start and repeated weeks
    checks.json       Plan ticks
    sessions.json     Session history
```

### 3.2 File rules

- All text files are indented, sorted JSON with ISO 8601 dates and a `version` number, so they stay readable and compare cleanly.
- Every write is atomic, so a crash can't leave half a file.
- A file that can't be read is set aside as `<name>.damaged.json`, and the user is told. It is never overwritten.
- A file with a newer `version` than the app understands stops the library from opening, with a message to update the app. Nothing is changed.
- Ticks and finished sessions save immediately. Other edits save within half a second, and everything is saved when the app moves to the background or quits.

### 3.3 Photos

- An imported photo is copied into `Photos/` under its original name, with a number added if that name is taken.
- Each entry in `photos.json` holds a stable ID, file name, SHA-256 hash, import date, crop, rotation, flip and the no-figure flag.
- Sessions, sets and benchmarks refer to the ID, so they survive a rename.
- Thumbnails are a disposable cache in `~/Library/Caches/com.hyprsea.linework/`, not in the library.

### 3.4 Changes made in Finder

The library is re-checked when it opens and each time the app comes to the front. The check runs off the main thread.

- **New image file:** added and auto-cropped. Its import date is when it was found.
- **Renamed or moved within `Photos/`:** recognized by its hash, keeping its crop, sets and benchmark.
- **Deleted file:** the photo disappears from the app, but its entry stays, marked missing. Restoring the file reconnects it, so benchmark history isn't lost by accident.
- **`Photos/` missing or empty while entries exist:** treated as an unavailable folder rather than mass deletion. Nothing is marked missing, and the user is told.
- **Non-image files:** ignored and left alone.
- **Changed text files:** reloaded if the file on disk is newer than the last load. There is no merging: the latest save wins.

### 3.5 Synced folders

The app doesn't require or manage sync. Files still in the cloud and not yet downloaded are skipped until they arrive, and conflict copies such as `checks 2.json` are ignored. The README advises against opening one library on two Macs at the same time.

### 3.6 Deleting

Deleting a photo or canvas inside the app moves its files to the Trash rather than erasing them, because they're in the user's own folder. Deleting a photo also removes its entry and its attempts. Their drawings go to the Trash too.

### 3.7 Choosing a library

- With no library remembered, the first screen offers **Create Library** (suggesting `~/Documents/Linework Library`) and **Open Library**. The welcome and name steps follow, if not yet done.
- File > Open Library switches at any time, after saving the current one. A running session must end first.
- File > Reveal Library in Finder opens the folder.
- The last library is remembered with a bookmark, so it's still found after being moved or renamed.
- Choosing a folder:
  - with `library.json`: opens it.
  - empty: makes it a library.
  - with other contents: asks before adding Linework's folders, and changes nothing that's already there.
- If the remembered library can't be found, for example on an unplugged drive, the first screen says so and offers to try again or choose another. Nothing is created in its place.
- Streak, plan progress and everything else in the layout belong to the library.
- Per-machine preferences stay with the app: name, clock format, seconds, notes on or off, sound, transparency and last session settings.

## 4. Canvases

- Freehand gets a canvas menu showing the current canvas, with **New**, **Rename**, **Duplicate** and **Delete**.
- Everything autosaves, as it does now. There is no Save button.
- Each canvas is a folder in `Canvases/`, named after the canvas, holding `canvas.json` and copies of its images. Image copies keep readable names.
- New canvases are named "Untitled", then "Untitled 2" and so on. Names that can't be folder names are rejected with a message, and so are duplicates.
- A library always has at least one canvas. Deleting the last one leaves a new empty one. Delete asks first and moves the folder to the Trash.
- The app remembers the last open canvas per library. Switching saves the current one first.
- Behaviour inside a canvas is unchanged: tools, zoom limits, greyscale, damaged-file handling and notes from s.e.a.
- Canvas folders added or renamed in Finder appear in the menu at the next check.

## 4A. Plan timing and ticking

The plan is defined by week and weekday, not by calendar date. The artist's knowledge doesn't have to line up with the schedule, so nothing is locked.

### No hardcoded dates

- `startDate` leaves the plan data, and a plan is "Week 3, Monday: Gestures", never "9 Nov".
- The "Your plan starts Monday 2 Nov" state and its copy are removed. There is no "not started" state.
- Plan text is rewritten wherever it names a date (31 items, plus the guide's schedule table):
  - "side by side with Nov 14" becomes a reference to the plan, such as "side by side with your Week 2 benchmark".
  - "Save as 2026-11-14 · gesture benchmark" becomes "Save as YYYY-MM-DD · gesture benchmark".
  - "the same photo as Jan 4" names the week of that benchmark.
  - Personal dates such as "your Sep 2024 WIP" become "an old work in progress", and "the Aug 2024 light study" becomes "an old light study".
  - The guide's "Dates" column becomes week ranges, such as "Weeks 2–5".
  - "Day 29" and "Day 30" stay, because they count within a book rather than naming a date.
- A test fails the build if plan text contains a month with a number, or a year.
- The rewrites live in `plan-summaries.json` and the generator, so regenerating the plan keeps them. The original PDF isn't changed.

### Where the calendar still applies

- Each library's `plan.json` stores when Week 1 began, always a Monday. It's a data value, never shown as plan text.
- A new library's Week 1 is the week the library is created. Earlier days that week can simply be ticked or ignored.
- Today maps the current date to its week and weekday, and the card says "Week 3 · Monday". A library whose start is still ahead shows Week 1, Monday.
- After the last week, the forever week repeats by weekday.
- The Calendar stays a calendar of real dates, because it's a record of what happened. Clicking a day opens the lesson scheduled for it, or says there was none for days before Week 1.
- Path circles show the weekday (M, T, W…) under week headings rather than a date number.

### Ticking any lesson

- Every lesson can be ticked, unticked and started at any time: past, today or future, from Today, Path or Calendar. The "You can tick this on the day" rule is removed.
- A tick belongs to the **lesson**: week, weekday, block and repeat pass. It doesn't move if the schedule shifts. Forever-week ticks use the date, since those lessons repeat indefinitely.
- Each tick also stores the day it counts toward, which is the lesson's scheduled date or today, **whichever is earlier**:
  - Ticking today's lesson counts today.
  - Ticking a missed lesson from last week counts on its own day, so a missed day can be filled in and the streak mended, as it can now.
  - Ticking a future lesson counts today, so working ahead keeps a streak going and never fills future squares.
- Streak and the heatmap read that day.
- A future day with ticks shows as done or partly done on the Path, like any other.
- Finishing a whole week or book still celebrates, however far ahead it is.
- Celebration wording says "today" only when the finished lesson is today's. Otherwise it says "You finished that day."
- Timed sessions tick their lesson the same way. The session itself is recorded on the real date it ran.

## 5. Architecture

### 5.1 Storage

SwiftData is removed. It's replaced by:

- **`LineworkCore`:** plain `Codable` value types for every file, encoding and decoding, version handling, damaged-file handling, the Finder scan's decision logic, and canvas folder naming. All of it takes a folder URL, so tests run against temporary folders.
- **`LibraryStore` (app):** an `@Observable` store holding the open library's data for views to read, and the only thing that writes to it. Views that used `@Query` read the store instead.
- **`FreehandStore` (app):** works as today, but takes the current canvas's folder.

The old rule stays: decisions live in `LineworkCore`, and stores only read, write and publish.

### 5.2 Components

Only long-standing public Apple frameworks: SwiftUI, AppKit, Foundation, ImageIO, Core Image, Core Text, Vision and CryptoKit. No third-party code and no private APIs. This is the best protection available against macOS changes, but it can't guarantee future behaviour. The user's data is safe either way because it's in plain files.

### 5.3 Clean-up

- Rename `GestureCore` to `LineworkCore`, `GESTURE_*` variables to `LINEWORK_*`, and preference keys to a consistent `linework.*` form.
- Remove unused code, including the old streak line, and XP and levels, which nothing shows. The XP rules remain in the old repo if they're wanted back.
- Debug tools (`--snapshots`, behaviour checks, `--print-counts`, date override) compile only in debug builds, and shipped code no longer refers to them.
- Split files over about 300 lines that do more than one job.
- Replace `try?` where a failure should be reported, so failed saves are visible rather than silent.

### 5.4 Performance

The app has no known performance problems, so this work is targeted rather than speculative:

- Measure with a generated library of 2,000 photos: opening it, the Finder check, scrolling the grid and starting a session.
- Heavy work stays off the main thread.
- Stop recomputing plan values on the main menu's one-second clock tick.
- Only changes that the measurements justify are made.

## 6. Import button

The Library gets an **Import** button, with File > Import Photos and ⌘I, opening the standard file picker with multiple selection. It uses the same import path as drag and drop, and adds to the current set if one is selected.

## 7. Bringing over existing data

`scripts/import-gesturedrawing.py` copies the old data into a new library. It's a one-time tool in the repo rather than part of the app, because nobody else has old data.

- It only reads the old data, and opens the old database in read-only mode, so the old app keeps working.
- **Source formats, checked on a copy:** IDs are 16-byte blobs, session image IDs are `NSKeyedArchiver` property lists, preset steps are JSON, and dates are seconds since 2001.
- It sets the library's Week 1 to Monday 2 November 2026 and converts each dated tick to its lesson, keeping that date as the day it counts toward. Repeats convert to `plan.json`.
- It copies photos, drawings, the canvas (as "Canvas") and all records, and generates hashes.
- It offers to copy the name, clock, transparency and session preferences to the new app.
- It refuses to write into a folder that already has a library unless told to replace one it created earlier, so it can be re-run just before switching.
- It finishes by comparing old and new counts, and reports any difference. The new app's first open of the result must also show the same counts.

**Switching over:** quit the old app, re-run the script so nothing logged is lost, open the new app and check it. The old app and its data remain untouched until the user chooses to remove them.

## 8. Build and installer

- `scripts/build-app.sh` makes a release build of `Linework.app` for Apple silicon, with the icon and fonts, ad-hoc signed.
- `scripts/make-dmg.sh` produces `Linework-2.0.dmg`, using only tools included with macOS: the app, an Applications shortcut, and a short "How to open Linework" note. The drag-to-Applications window layout is applied if the Finder allows it, and the `.dmg` still works if not.
- **First open on another Mac:** macOS blocks it until the user chooses System Settings > Privacy & Security > Open Anyway. The note and the README show these steps.
- **Folder permission:** with an ad-hoc signature, macOS may ask again for access to protected folders such as Documents after each app update.
- **Adding signing later:** both scripts read a signing identity if one is provided, and otherwise sign ad-hoc. A notarization step can be added there without other changes.
- **Sharing:** because the repo is private, people need access to download from its releases. Publishing a release is done only when the user asks.

## 9. Testing

**Unit tests (`swift test`):**

- All existing `GestureCore` tests, moved and renamed. XP and level tests go with that code.
- Every file format: round trip, missing file, damaged file set aside, newer version refused, atomic writes leaving no temporary files.
- Finder scan decisions: new, renamed, deleted, restored, `Photos/` missing, non-images, conflict copies.
- Choosing a folder: library, empty, other contents, missing.
- Canvas folders: naming, duplicates, rename, duplicate, deleting the last.
- Moved plan, streak and session logic, now fed by the new data types.
- Plan timing: weekday and week mapping from any start, a start still ahead, repeats, forever week, no dates in plan text.
- Ticking: past, today and future; the "counts toward" rule; ticks staying with their lesson after a repeat; streak and heatmap never using a future day.

**Debug harness:** the existing screen renders and behaviour checks, updated, against scratch folders only, plus a real offscreen session confirming that the keep-awake hold, ticks and session recording all reach the files.

**Import script:** run on a copy of the real library, with counts compared, then opened in the new app. Includes a re-run.

**Installer:** build the `.dmg`, mount it, copy the app, add the quarantine flag to simulate a download, and confirm that the app launches once allowed. Also confirm that the shipped binary contains no debug tools or `GestureDrawing` strings.

**Performance:** the §5.4 measurements, with results recorded in the implementation notes.

**Checks only the user can do:**

- Install on a second Mac, including the Open Anyway step.
- Live fullscreen, session keys, real glass blur, sounds, confetti and note fade.
- Dragging from Finder, and changes made in Finder while the app runs.
- Using a real synced folder, if wanted.

## 10. What changes for the user

| Area | Change |
|---|---|
| App name and icon location | `Linework.app` in Applications, rather than a build folder |
| Data | In a folder of their choosing, rather than hidden in Application Support |
| First launch | Create or open a library before the welcome screen |
| File menu | Open Library, Reveal Library in Finder, Import Photos |
| Library | Import button, and photos added or removed in Finder are picked up |
| Deleting | Goes to the Trash rather than being erased |
| Freehand | Named canvases instead of one |
| XP and levels | Removed, as nothing showed them |
| Plan | Named by week and weekday, with no calendar dates in its text |
| Ticking | Any lesson, any time, including future ones |
| Path circles | Show the weekday rather than a date number |

Nothing else about how the app looks or behaves changes.

## 11. Risks

| Risk | Handling |
|---|---|
| Moving 17 SwiftData files to a new store breaks a view's behaviour | Logic moves under tests first. Views change one section at a time, with renders and the debug harness after each. |
| A Finder scan misreads an unavailable folder as deleted photos | Missing photos only ever get marked, never erased, and an empty or absent `Photos/` changes nothing. |
| Data loss when switching | The old app and its data are never written to. Counts are compared, and the script can be re-run. |
| The plan started on 2 Nov 2026, so old-app ticks accumulate during the rebuild | The script's re-run brings them over at the switch. |
| Rewriting plan text changes the author's wording | Rewrites are listed in `plan-summaries.json` and shown to the user for review before they ship. |
| Live window behaviour can't be seen from the build environment | Listed as user checks. Offscreen renders aren't claimed as proof of it. |
| Gatekeeper's steps change in a future macOS | The note and README are easy to update. Signing remains available. |
