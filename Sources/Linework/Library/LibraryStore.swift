import AppKit
import Foundation
import LineworkCore
import Observation

/// The open library's data, for views to read, and the only thing that writes to it.
@MainActor @Observable
final class LibraryStore {
    let root: URL
    let manifest: LibraryManifest
    @ObservationIgnored let files: LibraryFiles

    private(set) var photos: [PhotoRecord] = []
    private(set) var benchmarkData = BenchmarksFile()
    private(set) var sets: [SetRecord] = []
    private(set) var presets: [PresetRecord] = []
    private(set) var sessions: [LineworkCore.SessionRecord] = []
    private(set) var ticks: [PlanTick] = []
    private(set) var tickKeys: Set<String> = []
    private(set) var plan: PlanRecord
    private(set) var schedule: PlanSchedule

    /// A save that failed, for the user to see. Clears when a save succeeds.
    private(set) var saveFailure: String?
    private(set) var damagedFiles: [String] = []
    var notice: String?

    private enum Part: Hashable { case photos, benchmarks, sets, presets, sessions, checks, plan }

    @ObservationIgnored private var dirty: Set<Part> = []
    @ObservationIgnored private var saveTask: Task<Void, Never>?
    @ObservationIgnored private var loadedDates: [Part: Date] = [:]

    private static let saveDelay: Duration = .milliseconds(500)
    private static let defaultPreset = "Default class"

    var calendar: Calendar { PlanStore.calendar }

    /// Throws `LibraryError` if the library can't be read by this version of the app.
    init(root: URL) throws {
        self.root = root
        files = LibraryFiles(root: root)
        let data = try Data(contentsOf: root.appending(path: LibraryFolder.manifestName))
        manifest = try LibraryFiles.decoder.decode(LibraryManifest.self, from: data)
        guard manifest.format <= LibraryManifest.currentFormat else { throw LibraryError.newerFormat(file: LibraryFolder.manifestName) }
        let cal = PlanStore.calendar
        let start = LibraryFolder.monday(of: PlanClock.now, calendar: cal)
        plan = PlanRecord(weekOneStart: start)
        schedule = Self.makeSchedule(PlanRecord(weekOneStart: start), calendar: cal)
        try loadAll()
    }

    private static func makeSchedule(_ plan: PlanRecord, calendar: Calendar) -> PlanSchedule {
        let start = PlanDayKey.date(plan.weekOneStart, calendar: calendar) ?? calendar.startOfDay(for: .now)
        return PlanSchedule(plan: .shared, start: start, repeats: plan.repeats, calendar: calendar)
    }

    // MARK: Loading

    private func value<T: Equatable>(_ file: LibraryFile<T>, default fallback: T) throws -> T {
        switch try files.read(file) {
        case .missing: return fallback
        case let .loaded(value): return value
        case .damaged:
            damagedFiles.append(file.name)
            return fallback
        }
    }

    private func loadAll() throws {
        for part in [Part.photos, .benchmarks, .sets, .presets, .sessions, .checks, .plan] { try load(part) }
        if presets.isEmpty, try files.read(.presets) == .missing {
            presets = [PresetRecord(id: UUID(), name: Self.defaultPreset, steps: QueueBuilder.defaultClass)]
            markDirty(.presets)
        }
    }

    private func load(_ part: Part) throws {
        switch part {
        case .photos:
            photos = try value(.photos, default: [])
            loadedDates[part] = files.modificationDate(of: .photos)
        case .benchmarks:
            benchmarkData = try value(.benchmarks, default: BenchmarksFile())
            loadedDates[part] = files.modificationDate(of: .benchmarks)
        case .sets:
            sets = try value(.sets, default: [])
            loadedDates[part] = files.modificationDate(of: .sets)
        case .presets:
            presets = try value(.presets, default: [])
            loadedDates[part] = files.modificationDate(of: .presets)
        case .sessions:
            sessions = try value(.sessions, default: [])
            loadedDates[part] = files.modificationDate(of: .sessions)
        case .checks:
            ticks = try value(.checks, default: [])
            tickKeys = Set(ticks.map(\.key))
            loadedDates[part] = files.modificationDate(of: .checks)
        case .plan:
            plan = try value(.plan, default: plan)
            schedule = Self.makeSchedule(plan, calendar: calendar)
            loadedDates[part] = files.modificationDate(of: .plan)
        }
    }

    private func diskDate(_ part: Part) -> Date? {
        switch part {
        case .photos: files.modificationDate(of: .photos)
        case .benchmarks: files.modificationDate(of: .benchmarks)
        case .sets: files.modificationDate(of: .sets)
        case .presets: files.modificationDate(of: .presets)
        case .sessions: files.modificationDate(of: .sessions)
        case .checks: files.modificationDate(of: .checks)
        case .plan: files.modificationDate(of: .plan)
        }
    }

    /// Picks up files changed outside the app. The latest save wins; anything unsaved here is kept.
    func reloadIfChanged() {
        for part in [Part.photos, .benchmarks, .sets, .presets, .sessions, .checks, .plan] where !dirty.contains(part) {
            guard let onDisk = diskDate(part), onDisk != loadedDates[part] else { continue }
            try? load(part)
        }
    }

    // MARK: Saving

    private func markDirty(_ part: Part) {
        dirty.insert(part)
        saveTask?.cancel()
        saveTask = Task { [weak self] in
            try? await Task.sleep(for: Self.saveDelay)
            guard !Task.isCancelled else { return }
            self?.flush()
        }
    }

    func flush() {
        saveTask?.cancel()
        for part in dirty { save(part) }
    }

    private func save(_ part: Part) {
        do {
            switch part {
            case .photos: try files.write(photos, to: .photos)
            case .benchmarks: try files.write(benchmarkData, to: .benchmarks)
            case .sets: try files.write(sets, to: .sets)
            case .presets: try files.write(presets, to: .presets)
            case .sessions: try files.write(sessions, to: .sessions)
            case .checks: try files.write(ticks, to: .checks)
            case .plan: try files.write(plan, to: .plan)
            }
            dirty.remove(part)
            loadedDates[part] = diskDate(part)
            saveFailure = nil
        } catch {
            // Stays dirty and in memory, so the next save tries again.
            saveFailure = FileManager.default.fileExists(atPath: root.path)
                ? "Couldn't save your library." : "Your library's folder can't be found, so changes aren't being saved."
        }
    }

    // MARK: Plan

    func isTicked(_ ref: PlanDayRef, _ kind: PlanBlockKind) -> Bool {
        tickKeys.contains(schedule.tickKey(ref, kind))
    }

    /// Returns true if the block is now ticked.
    @discardableResult
    func toggleTick(_ ref: PlanDayRef, _ kind: PlanBlockKind) -> Bool {
        let key = schedule.tickKey(ref, kind)
        if tickKeys.contains(key) {
            ticks.removeAll { $0.key == key }
            tickKeys.remove(key)
            dirty.insert(.checks)
            save(.checks)
            return false
        }
        addTick(key, ref)
        return true
    }

    /// Ticks the block if it isn't already. Returns true if this added the tick.
    @discardableResult
    func ensureTick(_ ref: PlanDayRef, _ kind: PlanBlockKind) -> Bool {
        let key = schedule.tickKey(ref, kind)
        guard !tickKeys.contains(key) else { return false }
        addTick(key, ref)
        return true
    }

    private func addTick(_ key: String, _ ref: PlanDayRef) {
        let now = PlanClock.now
        ticks.append(PlanTick(key: key, countsOn: PlanTick.countsOn(scheduled: schedule.date(of: ref), today: now, calendar: calendar), checkedAt: now))
        tickKeys.insert(key)
        dirty.insert(.checks)
        save(.checks)
    }

    var practiceDays: Set<Date> {
        PlanProgress.practiceDays(ticks: ticks, sessionDays: sessions.map(\.startedAt), calendar: calendar)
    }

    /// Repeats the last `weekCount` weeks, right after the week containing `date`.
    func addRepeat(weekCount: Int, on date: Date) {
        guard case let .plan(sequenceIndex, _)? = schedule.ref(on: date) else { return }
        let number = (plan.repeats.map(\.number).max() ?? 0) + 1
        plan.repeats.append(PlanRepeat(number: number, afterSequenceIndex: sequenceIndex, weekCount: weekCount))
        schedule = Self.makeSchedule(plan, calendar: calendar)
        dirty.insert(.plan)
        save(.plan)
    }

    /// Removes the latest repeat and any ticks on its lessons.
    func undoLastRepeat() {
        guard let last = plan.repeats.popLast() else { return }
        let suffix = "-r\(last.number)"
        ticks.removeAll { $0.key.hasSuffix(suffix) }
        tickKeys = Set(ticks.map(\.key))
        schedule = Self.makeSchedule(plan, calendar: calendar)
        dirty.formUnion([.plan, .checks])
        flush()
    }

    // MARK: Photos

    var visiblePhotos: [PhotoRecord] { photos.filter { !$0.missing } }

    func photo(_ id: UUID) -> PhotoRecord? { photos.first { $0.id == id } }

    var photosDir: URL { root.appending(path: "Photos", directoryHint: .isDirectory) }
    var drawingsDir: URL { root.appending(path: "Drawings", directoryHint: .isDirectory) }
    var canvasesDir: URL { root.appending(path: "Canvases", directoryHint: .isDirectory) }

    func photoURL(_ record: PhotoRecord) -> URL { photosDir.appending(path: record.fileName) }
    func drawingURL(_ name: String) -> URL { drawingsDir.appending(path: name) }

    func addPhotos(_ records: [PhotoRecord], toSet setID: UUID?) {
        guard !records.isEmpty else { return }
        photos += records
        markDirty(.photos)
        if let setID, let index = sets.firstIndex(where: { $0.id == setID }) {
            sets[index].photoIDs += records.map(\.id)
            markDirty(.sets)
        }
    }

    func update(_ id: UUID, _ change: (inout PhotoRecord) -> Void) {
        guard let index = photos.firstIndex(where: { $0.id == id }) else { return }
        change(&photos[index])
        markDirty(.photos)
    }

    /// Moves the photo and its attempts' drawings to the Trash, then removes their records. A failed move changes nothing.
    func deletePhoto(_ id: UUID) {
        guard let record = photo(id) else { return }
        let attempts = benchmarkData.attempts.filter { $0.photoID == id }
        let urls = [photoURL(record)] + attempts.compactMap { $0.drawingFileName.map(drawingURL) }
        do {
            try Trash.move(urls)
        } catch {
            notice = "Couldn't move that photo to the Trash."
            return
        }
        photos.removeAll { $0.id == id }
        benchmarkData.attempts.removeAll { $0.photoID == id }
        benchmarkData.benchmarks.removeAll { $0.photoID == id }
        for index in sets.indices { sets[index].photoIDs.removeAll { $0 == id } }
        dirty.formUnion([.photos, .benchmarks, .sets])
        flush()
    }

    // MARK: Sets

    @discardableResult
    func createSet(name: String) -> SetRecord {
        let set = SetRecord(id: UUID(), name: name, createdAt: .now, photoIDs: [])
        sets.append(set)
        markDirty(.sets)
        return set
    }

    func renameSet(_ id: UUID, to name: String) {
        guard let index = sets.firstIndex(where: { $0.id == id }) else { return }
        sets[index].name = name
        markDirty(.sets)
    }

    func deleteSet(_ id: UUID) {
        sets.removeAll { $0.id == id }
        markDirty(.sets)
    }

    func setPhoto(_ photoID: UUID, inSet setID: UUID, _ member: Bool) {
        guard let index = sets.firstIndex(where: { $0.id == setID }) else { return }
        sets[index].photoIDs.removeAll { $0 == photoID }
        if member { sets[index].photoIDs.append(photoID) }
        markDirty(.sets)
    }

    // MARK: Benchmarks

    func benchmark(_ photoID: UUID) -> BenchmarkRecord? { benchmarkData.benchmarks.first { $0.photoID == photoID } }
    func isBenchmark(_ photoID: UUID) -> Bool { benchmark(photoID) != nil }
    func attempts(of photoID: UUID) -> [AttemptRecord] { benchmarkData.attempts.filter { $0.photoID == photoID }.sorted { $0.date < $1.date } }

    /// Unmarking keeps attempts, so marking it again brings them back.
    func toggleBenchmark(_ photoID: UUID) {
        if isBenchmark(photoID) {
            benchmarkData.benchmarks.removeAll { $0.photoID == photoID }
        } else {
            benchmarkData.benchmarks.append(BenchmarkRecord(photoID: photoID, seconds: 120, interval: 10, markedAt: .now))
        }
        markDirty(.benchmarks)
    }

    func updateBenchmark(_ photoID: UUID, seconds: Int? = nil, interval: Int? = nil) {
        guard let index = benchmarkData.benchmarks.firstIndex(where: { $0.photoID == photoID }) else { return }
        if let seconds { benchmarkData.benchmarks[index].seconds = seconds }
        if let interval { benchmarkData.benchmarks[index].interval = interval }
        markDirty(.benchmarks)
    }

    /// When the benchmark's interval counts from.
    func benchmarkSince(_ record: BenchmarkRecord) -> Date {
        attempts(of: record.photoID).last?.date ?? record.markedAt
    }

    func dueBenchmarks() -> [DueBenchmark] {
        let dates = sessions.map(\.startedAt)
        return benchmarkData.benchmarks
            .filter { record in
                photo(record.photoID)?.missing == false
                    && BenchmarkSchedule.isDue(since: benchmarkSince(record), interval: record.interval, sessionDates: dates)
            }
            .map { DueBenchmark(imageID: $0.photoID, seconds: $0.seconds, number: attempts(of: $0.photoID).count + 1) }
    }

    /// Copies a drawing into `Drawings/` under a readable name.
    private func copyDrawing(_ source: URL) throws -> String {
        guard ImageFiles.isReadableImage(source) else { throw StorageError.unreadableImage }
        let existing = Set((try? FileManager.default.contentsOfDirectory(atPath: drawingsDir.path)) ?? [])
        let name = PhotoScan.uniqueName(source.lastPathComponent, taken: existing)
        try FileManager.default.createDirectory(at: drawingsDir, withIntermediateDirectories: true)
        try FileManager.default.copyItem(at: source, to: drawingURL(name))
        return name
    }

    func addAttempt(photoID: UUID, sessionID: UUID, drawing: URL?) throws {
        let name = try drawing.map(copyDrawing)
        benchmarkData.attempts.append(AttemptRecord(id: UUID(), photoID: photoID, date: .now, sessionID: sessionID, drawingFileName: name))
        dirty.insert(.benchmarks)
        save(.benchmarks)
    }

    func attachDrawing(attemptID: UUID, from source: URL) throws {
        guard let index = benchmarkData.attempts.firstIndex(where: { $0.id == attemptID }) else { return }
        benchmarkData.attempts[index].drawingFileName = try copyDrawing(source)
        dirty.insert(.benchmarks)
        save(.benchmarks)
    }

    // MARK: Sessions and presets

    func recordSession(_ record: LineworkCore.SessionRecord) {
        sessions.append(record)
        dirty.insert(.sessions)
        save(.sessions)
    }

    @discardableResult
    func addPreset(name: String, steps: [ClassStep]) -> PresetRecord {
        let preset = PresetRecord(id: UUID(), name: name, steps: steps)
        presets.append(preset)
        markDirty(.presets)
        return preset
    }

    // MARK: Finder changes

    func applyScan(_ result: PhotoScanResult, added: [PhotoRecord]) {
        guard !result.folderUnavailable else {
            notice = "Your Photos folder is out of reach, so nothing was changed."
            return
        }
        for (id, name) in result.adopted {
            if let index = photos.firstIndex(where: { $0.id == id }) {
                photos[index].fileName = name
                photos[index].missing = false
            }
        }
        for id in result.missing {
            if let index = photos.firstIndex(where: { $0.id == id }) { photos[index].missing = true }
        }
        for id in result.restored {
            if let index = photos.firstIndex(where: { $0.id == id }) { photos[index].missing = false }
        }
        photos += added
        if !result.adopted.isEmpty || !result.missing.isEmpty || !result.restored.isEmpty || !added.isEmpty {
            markDirty(.photos)
        }
    }
}
