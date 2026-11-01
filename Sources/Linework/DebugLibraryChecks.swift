#if DEBUG
import AppKit
import Foundation
import LineworkCore
import SwiftUI

/// Debug-only: `Linework --library-checks` with LINEWORK_LIBRARY set to a scratch folder.
/// Drives a real `LibraryStore` against real files and prints a line per check.
@MainActor
enum DebugLibraryChecks {
    /// `--perf`: times the slow-looking work on a generated 2,000-photo library.
    static func performance(source: URL) async {
        guard let path = ProcessInfo.processInfo.environment["LINEWORK_LIBRARY"] else { exit(2) }
        let root = URL(fileURLWithPath: path, isDirectory: true)
        let fm = FileManager.default
        try? LibraryFolder.create(at: root, weekOneStart: "2026-11-02", now: .now)
        let hash = (try? FileHash.sha256(of: source)) ?? "x"
        let records = (0..<2000).map { index -> PhotoRecord in
            let name = "photo-\(index).\(source.pathExtension)"
            try? fm.linkItem(at: source, to: root.appending(path: "Photos/\(name)"))
            return PhotoRecord(id: UUID(), fileName: name, sha256: hash, importedAt: .now, crop: .init(x: 0, y: 0, width: 1, height: 1),
                               rotation: 0, flipped: false, noFigureFound: false, missing: false)
        }
        try? LibraryFiles(root: root).write(records, to: .photos)
        let clock = ContinuousClock()
        func timed(_ name: String, _ work: () async -> Void) async {
            let elapsed = await clock.measure { await work() }
            print("perf", name, elapsed)
        }
        var opened: LibraryStore?
        await timed("open library") { opened = try? LibraryStore(root: root) }
        guard let store = opened else { exit(1) }
        await timed("Finder check, nothing changed") { await PhotoScanner.run(on: store) }
        await timed("visible photos sorted") { _ = store.visiblePhotos.sorted { $0.importedAt > $1.importedAt } }
        await timed("save photos.json") { try? store.files.write(store.photos, to: .photos) }
        await timed("tick summary") { _ = PlanProgress.tickSummary(store.ticks, schedule: store.schedule) }
        await timed("session queue") { _ = PlanStore.sessionQueue(for: PlanBlock(kind: .warmup, text: "", minutes: 5, timed: [ClassStep(count: 10, seconds: 30)]), photos: store.visiblePhotos) }
        exit(0)
    }

    static func check(_ name: String, _ ok: Bool) { print("check", ok ? "pass" : "FAIL", name) }

    static func run(sources: [URL]) async {
        guard let path = ProcessInfo.processInfo.environment["LINEWORK_LIBRARY"] else {
            print("check FAIL needs LINEWORK_LIBRARY")
            exit(2)
        }
        let root = URL(fileURLWithPath: path, isDirectory: true)
        let app = AppModel()
        check("A scratch library opens", app.library != nil)
        for name in LibraryFolder.folders + [LibraryFolder.manifestName] {
            check("The library has \(name)", FileManager.default.fileExists(atPath: root.appending(path: name).path))
        }
        guard let store = app.library else { exit(1) }
        store.flush()
        check("A new library has one preset", store.presets.count == 1)
        let ref = store.schedule.todayRef(PlanClock.now)
        check("A tick saves at once", store.toggleTick(ref, .focus) && FileManager.default.fileExists(atPath: store.files.url(of: .checks).path))
        let reopened = try? LibraryStore(root: root)
        check("A reopened library has the tick", reopened?.isTicked(ref, .focus) == true)

        // Review focus: the folder goes away while the app is open.
        try? FileManager.default.moveItem(at: root, to: root.appendingPathExtension("away"))
        let kept = store.toggleTick(ref, .check)
        check("A missing library reports the failed save", store.saveFailure != nil)
        check("A missing library isn't recreated", !FileManager.default.fileExists(atPath: root.path))
        check("The edit stays in memory", kept && store.isTicked(ref, .check))
        try? FileManager.default.moveItem(at: root.appendingPathExtension("away"), to: root)
        store.flush()
        check("The save succeeds once the folder is back", store.saveFailure == nil && (try? LibraryStore(root: root))?.isTicked(ref, .check) == true)
        store.toggleTick(ref, .focus)
        store.toggleTick(ref, .check)

        await photos(store, sources: sources)
        ticking(store)
        await sessions(store)
        canvases(store)
        await reviewFindings(root: root, sources: sources)
        exit(0)
    }

    /// The failure cases found in review, each against a real library and real files.
    private static func reviewFindings(root: URL, sources: [URL]) async {
        let fm = FileManager.default
        let base = root.deletingLastPathComponent()
        func fresh(_ name: String) throws -> LibraryStore {
            let url = base.appending(path: "\(name)-\(UUID().uuidString)", directoryHint: .isDirectory)
            try fm.createDirectory(at: url, withIntermediateDirectories: true)
            try LibraryFolder.create(at: url, weekOneStart: "2026-11-02", now: .now)
            return try LibraryStore(root: url)
        }
        do {
            // A file that's there but can't be read must stop the library opening, not empty it.
            let a = try fresh("unreadable")
            let ref = a.schedule.todayRef(PlanClock.now)
            a.toggleTick(ref, .focus)
            a.flush()
            let ticks = a.files.url(of: .checks)
            let bytes = try Data(contentsOf: ticks)
            try fm.setAttributes([.posixPermissions: 0o000], ofItemAtPath: ticks.path)
            check("An unreadable file stops the library opening", (try? LibraryStore(root: a.root)) == nil)
            a.reloadIfChanged()
            check("Reloading keeps what's in memory", a.isTicked(ref, .focus))
            try fm.setAttributes([.posixPermissions: 0o644], ofItemAtPath: ticks.path)
            check("The unreadable file is untouched", try Data(contentsOf: ticks) == bytes)

            // A file damaged while open must not empty memory, and memory saves back.
            try Data("junk".utf8).write(to: ticks)
            try fm.setAttributes([.modificationDate: Date(timeIntervalSinceNow: 5)], ofItemAtPath: ticks.path)
            a.reloadIfChanged()
            check("A file damaged while open doesn't empty memory", a.isTicked(ref, .focus))
            check("It's reported", a.damagedFiles.contains("checks.json"))

            // Lost photo records must not make every file a new photo.
            let b = try fresh("lost-records")
            _ = await ImportService.importURLs(Array(sources.prefix(2)), into: b, setID: nil)
            let keep = b.visiblePhotos[0]
            b.toggleBenchmark(keep.id)
            let set = b.createSet(name: "Set")
            b.setPhoto(keep.id, inSet: set.id, true)
            b.flush()
            try Data("junk".utf8).write(to: b.files.url(of: .photos))
            let damaged = try LibraryStore(root: b.root)
            await PhotoScanner.run(on: damaged)
            check("Lost photo records don't turn files into new photos", damaged.photos.isEmpty && damaged.notice != nil)
            damaged.flush()
            let again = try LibraryStore(root: b.root)
            check("The set and benchmark still point at the old photo", again.sets.first?.photoIDs == [keep.id] && again.isBenchmark(keep.id))

            // A name held by a missing record isn't free.
            let c = try fresh("shared-name")
            _ = await ImportService.importURLs([sources[0]], into: c, setID: nil)
            let first = c.visiblePhotos[0]
            try fm.moveItem(at: c.photoURL(first), to: base.appending(path: "aside-\(UUID().uuidString)"))
            await PhotoScanner.run(on: c)
            _ = await ImportService.importURLs([sources[0]], into: c, setID: nil)
            check("Importing can't take a missing photo's name", Set(c.photos.map(\.fileName)).count == 2)

            // A missing plan file is saved, so Week 1 can't drift.
            let e = try fresh("plan")
            try fm.removeItem(at: e.files.url(of: .plan))
            let replanned = try LibraryStore(root: e.root)
            replanned.flush()
            check("A missing plan is written back", fm.fileExists(atPath: e.files.url(of: .plan).path))

            // Time zone changes while the app is open.
            let f = try fresh("zone")
            let original = NSTimeZone.default
            NSTimeZone.default = TimeZone(identifier: "Asia/Tokyo")!
            f.refreshCalendar()
            check("The schedule follows a time zone change", f.schedule.calendar.timeZone.identifier == "Asia/Tokyo")
            NSTimeZone.default = original
            f.refreshCalendar()

            // Notices don't stay forever.
            f.notice = "Hello"
            try? await Task.sleep(for: .seconds(LibraryStore.noticeSeconds + 0.5))
            check("A notice clears itself", f.notice == nil)

            // The library goes away with a canvas open.
            let g = try fresh("gone")
            let directory = CanvasDirectory(canvasesRoot: g.canvasesDir)
            let canvas = FreehandStore(canvases: directory, libraryID: g.manifest.id)
            canvas.loadIfNeeded()
            canvas.flush()
            let away = g.root.appendingPathExtension("away")
            try fm.moveItem(at: g.root, to: away)
            _ = canvas.files.imagesDir
            canvas.setGreyscale(!canvas.document.greyscale)
            check("A failed canvas save says so", canvas.flush() == false && canvas.status != nil)
            check("The library isn't recreated by a canvas", !fm.fileExists(atPath: g.root.path))
            let name = canvas.currentName
            canvas.open("Elsewhere")
            check("A failed save stops a canvas switch", canvas.currentName == name)
            try fm.moveItem(at: away, to: g.root)
            check("The save succeeds once it's back", canvas.flush())

            // An import that outlives a canvas switch.
            let h = try fresh("import-switch")
            let directoryH = CanvasDirectory(canvasesRoot: h.canvasesDir)
            let moving = FreehandStore(canvases: directoryH, libraryID: h.manifest.id)
            moving.loadIfNeeded()
            let startedIn = moving.currentName
            let importing = Task { await moving.importFiles(Array(sources.prefix(2)), at: .zero) }
            await Task.yield()  // Lets the import start, so the switch happens while it's copying.
            try moving.newCanvas()
            await importing.value
            check("An import doesn't land on another canvas", moving.document.items.isEmpty)
            let leftovers = (try? fm.contentsOfDirectory(atPath: directoryH.url(of: startedIn).appending(path: "Images").path)) ?? []
            check("An abandoned import leaves no copies", leftovers.isEmpty)
        } catch {
            check("Review checks ran (\(error))", false)
        }
    }

    private static func photos(_ store: LibraryStore, sources: [URL]) async {
        let fm = FileManager.default
        let notImage = store.root.deletingLastPathComponent().appending(path: "not-an-image-\(UUID().uuidString).txt")
        try? Data("hello".utf8).write(to: notImage)
        let result = await ImportService.importURLs(sources + [notImage], into: store, setID: nil)
        try? fm.removeItem(at: notImage)
        check("Import takes photos and skips the rest", result.imported == sources.count && result.skipped == 1)
        check("Imports keep readable names", store.photos.allSatisfy { sources.map(\.lastPathComponent).contains($0.fileName) })
        let again = await ImportService.importURLs(Array(sources.prefix(1)), into: store, setID: nil)
        check("A second copy gets its own name", again.imported == 1 && store.photos.contains { $0.fileName.contains(" 2.") })

        guard let photo = store.visiblePhotos.first(where: { !$0.fileName.contains(" 2.") }) else { return check("needs a photo", false) }
        let crop = NormalizedRect(x: 0.2, y: 0.2, width: 0.4, height: 0.4)
        store.update(photo.id) { $0.crop = crop }
        let url = store.photoURL(photo)
        let aside = store.root.deletingLastPathComponent().appending(path: "aside-\(UUID().uuidString).img")

        try? fm.moveItem(at: url, to: aside)
        await PhotoScanner.run(on: store)
        check("A photo deleted in Finder is hidden, not forgotten", store.photo(photo.id)?.missing == true && !store.visiblePhotos.contains { $0.id == photo.id })
        try? fm.moveItem(at: aside, to: url)
        await PhotoScanner.run(on: store)
        check("Restoring the file brings it back with its crop", store.photo(photo.id)?.missing == false && store.photo(photo.id)?.crop == crop)

        let renamed = store.photosDir.appending(path: "renamed-\(UUID().uuidString).\(url.pathExtension)")
        try? fm.moveItem(at: url, to: renamed)
        await PhotoScanner.run(on: store)
        check("A rename in Finder keeps its crop", store.photo(photo.id)?.fileName == renamed.lastPathComponent && store.photo(photo.id)?.crop == crop)

        let count = store.visiblePhotos.count
        let extra = store.photosDir.appending(path: "dropped-in.\(url.pathExtension)")
        try? fm.copyItem(at: sources.last!, to: extra)
        await PhotoScanner.run(on: store)
        check("A file added in Finder appears", store.visiblePhotos.count == count + 1 && store.photos.contains { $0.fileName == extra.lastPathComponent })

        let holding = store.photosDir.deletingLastPathComponent().appending(path: "Photos-away")
        try? fm.moveItem(at: store.photosDir, to: holding)
        await PhotoScanner.run(on: store)
        check("A missing Photos folder changes nothing", store.visiblePhotos.count == count + 1 && store.notice != nil)
        try? fm.moveItem(at: holding, to: store.photosDir)
        store.notice = nil

        if let victim = store.photos.first(where: { $0.fileName == extra.lastPathComponent }) {
            let path = store.photoURL(victim).path
            store.deletePhoto(victim.id)
            check("Deleting removes the record and the file", store.photo(victim.id) == nil && !fm.fileExists(atPath: path))
        }
        store.flush()
        let reopened = try? LibraryStore(root: store.root)
        check("Photo records survive reopening", reopened?.photos.count == store.photos.count)
    }

    private static func ticking(_ store: LibraryStore) {
        let today = PlanClock.now
        let key = PlanDayKey.key(today, calendar: store.calendar)
        let schedule = store.schedule
        guard case let .plan(current, _) = schedule.todayRef(today) else { return check("needs a plan day", false) }
        let ahead = PlanDayRef.plan(sequenceIndex: current + 4, weekday: 0)
        let kind = schedule.day(for: ahead).blocks[0].kind
        store.toggleTick(ahead, kind)
        let tick = store.ticks.first { $0.key == schedule.tickKey(ahead, kind) }
        check("A future lesson can be ticked", store.isTicked(ahead, kind))
        check("It counts today, not on a future day", tick?.countsOn == key)
        check("It shows on the Path", PlanProgress.status(for: ahead, ticks: store.tickKeys, schedule: schedule) != .empty)
        check("The streak includes today", PlanProgress.currentStreak(practiceDays: store.practiceDays, today: today, calendar: store.calendar) >= 1)
        store.addRepeat(weekCount: 1, on: today)
        let shifted = PlanDayRef.plan(sequenceIndex: current + 5, weekday: 0)
        check("A repeat before it leaves the tick on its lesson", store.isTicked(shifted, kind))
        let copy = PlanDayRef.plan(sequenceIndex: current + 1, weekday: 0)
        store.toggleTick(copy, store.schedule.day(for: copy).blocks[0].kind)
        let before = store.ticks.count
        store.undoLastRepeat()
        check("Undo removes only that repeat's ticks", store.ticks.count == before - 1 && store.isTicked(ahead, kind))
        store.toggleTick(ahead, kind)
    }

    private static func sessions(_ store: LibraryStore) async {
        DebugSnapshots.active = true
        let session = SessionStore()
        let ref = store.schedule.todayRef(PlanClock.now)
        func open(_ kind: PlanBlockKind, seconds: Int) -> NSWindow {
            let block = PlanBlock(kind: kind, text: "check", minutes: 5, timed: [ClassStep(count: 2, seconds: seconds)])
            session.queue = PlanStore.sessionQueue(for: block, photos: store.visiblePhotos)
            session.planBlock = PlanBlockRef(ref: ref, kind: kind)
            session.token = UUID()
            session.soundOn = false
            let window = NSWindow(contentRect: NSRect(x: -10000, y: -10000, width: 1200, height: 800), styleMask: [.titled], backing: .buffered, defer: false)
            window.contentView = NSHostingView(rootView: SessionPlayerView().environment(store).environment(session))
            window.isReleasedWhenClosed = false
            window.orderFrontRegardless()
            window.makeKey()
            return window
        }
        func key(_ window: NSWindow, _ code: UInt16, _ text: String) {
            for type in [NSEvent.EventType.keyDown, .keyUp] {
                window.sendEvent(NSEvent.keyEvent(with: type, location: .zero, modifierFlags: [], timestamp: ProcessInfo.processInfo.systemUptime,
                                                  windowNumber: window.windowNumber, context: nil, characters: text, charactersIgnoringModifiers: text,
                                                  isARepeat: false, keyCode: code)!)
            }
        }
        func shut(_ window: NSWindow) async {
            window.contentView = nil
            window.close()
            try? await Task.sleep(for: .seconds(0.3))
        }
        func keepAwakeHeld() -> Bool {
            let shell = Process()
            let pipe = Pipe()
            shell.executableURL = URL(fileURLWithPath: "/usr/bin/pmset")
            shell.arguments = ["-g", "assertions"]
            shell.standardOutput = pipe
            try? shell.run()
            let output = String(decoding: pipe.fileHandleForReading.readDataToEndOfFile(), as: UTF8.self)
            shell.waitUntilExit()
            return output.contains("pid \(ProcessInfo.processInfo.processIdentifier)(") && output.contains("PreventUserIdleDisplaySleep")
        }
        func onDisk() -> (ticks: [String], sessions: Int) {
            let files = LibraryFiles(root: store.root)
            var ticks: [String] = []
            var sessions = 0
            if case let .loaded(value)? = try? files.read(.checks) { ticks = value.map(\.key) }
            if case let .loaded(value)? = try? files.read(.sessions) { sessions = value.count }
            return (ticks, sessions)
        }

        let startSessions = onDisk().sessions
        let natural = open(.warmup, seconds: 6)
        try? await Task.sleep(for: .seconds(5))
        check("The screen is kept awake during a session", keepAwakeHeld())
        try? await Task.sleep(for: .seconds(11))
        let warmup = store.schedule.tickKey(ref, .warmup)
        check("A session run to its end ticks its lesson on disk", onDisk().ticks.contains(warmup))
        check("A session run to its end is recorded on disk", onDisk().sessions == startSessions + 1)
        check("The keep-awake hold is released at the end", !keepAwakeHeld())
        await shut(natural)

        let escape = open(.focus, seconds: 6)
        try? await Task.sleep(for: .seconds(10.5))
        key(escape, 53, "\u{1b}")
        try? await Task.sleep(for: .seconds(0.4))
        check("Esc records without ticking", onDisk().sessions == startSessions + 2 && !onDisk().ticks.contains(store.schedule.tickKey(ref, .focus)))
        await shut(escape)

        let skipping = open(.check, seconds: 60)
        try? await Task.sleep(for: .seconds(1))
        let arrow = String(UnicodeScalar(NSRightArrowFunctionKey)!)
        key(skipping, 124, arrow)
        try? await Task.sleep(for: .seconds(0.3))
        key(skipping, 124, arrow)
        try? await Task.sleep(for: .seconds(0.3))
        check("Skipping every pose ticks without recording", onDisk().ticks.contains(store.schedule.tickKey(ref, .check)) && onDisk().sessions == startSessions + 2)
        await shut(skipping)
        DebugSnapshots.active = false
    }

    private static func canvases(_ store: LibraryStore) {
        let fm = FileManager.default
        let directory = CanvasDirectory(canvasesRoot: store.canvasesDir)
        let canvas = FreehandStore(canvases: directory, libraryID: store.manifest.id)
        canvas.loadIfNeeded()
        check("A library starts with a canvas", directory.list().count == 1 && !canvas.currentName.isEmpty)
        let first = canvas.currentName
        canvas.setGreyscale(true)
        do {
            try canvas.newCanvas()
            check("New makes and opens another", canvas.currentName != first && directory.list().count == 2 && !canvas.document.greyscale)
            canvas.open(first)
            check("Switching back keeps its state", canvas.document.greyscale)
            try canvas.renameCurrent(to: "Figure refs")
            check("Rename moves the folder", directory.list().contains("Figure refs") && canvas.currentName == "Figure refs")
            canvas.open("Figure refs")
            check("A renamed canvas keeps its state", canvas.document.greyscale)
            try canvas.duplicateCurrent()
            check("Duplicate opens the copy", canvas.currentName == "Figure refs copy" && canvas.document.greyscale)
            try fm.createDirectory(at: directory.url(of: "From Finder"), withIntermediateDirectories: true)
            canvas.refreshList()
            check("A folder added in Finder appears", canvas.names.contains("From Finder"))
            for _ in directory.list() { try canvas.deleteCurrent() }
            check("Deleting them all leaves one canvas", directory.list().count == 1)
        } catch {
            check("Canvas actions ran (\(error))", false)
        }
    }
}
#endif
