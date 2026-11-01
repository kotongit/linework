import Foundation
import Testing
@testable import LineworkCore

/// The fixtures the Windows tests check against, made by the real Swift encoders.
/// `LINEWORK_WRITE_FIXTURES=1 swift test --filter SharedFixtureTests` writes them; otherwise this checks they still match.
@Suite struct SharedFixtureTests {
    private static let root = URL(filePath: #filePath).deletingLastPathComponent().deletingLastPathComponent().deletingLastPathComponent()
        .appending(path: "shared/fixtures")
    private let now = Date(timeIntervalSince1970: 1_793_620_800) // 2026-11-02T12:00:00Z
    private let png = Data(base64Encoded: "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M/wHwAEBgIApD5fRAAAAABJRU5ErkJggg==")!

    private func id(_ n: Int) -> UUID { UUID(uuidString: String(format: "00000000-AAAA-4BBB-8CCC-%012d", n))! }

    private var cal: Calendar {
        var c = Calendar(identifier: .gregorian)
        c.timeZone = TimeZone(identifier: "UTC")!
        return c
    }

    private func writeLibrary(at root: URL) throws {
        let fm = FileManager.default
        for name in LibraryFolder.folders { try fm.createDirectory(at: root.appending(path: name), withIntermediateDirectories: true) }
        try png.write(to: root.appending(path: "Photos/pose one.png"))
        try png.write(to: root.appending(path: "Photos/pose-two.png"))
        try png.write(to: root.appending(path: "Drawings/attempt.png"))
        let digest = try FileHash.sha256(of: root.appending(path: "Photos/pose one.png"))
        let manifest = try LibraryFiles.encoder.encode(LibraryManifest(id: id(1), createdAt: now))
        try manifest.write(to: root.appending(path: LibraryFolder.manifestName))
        let files = LibraryFiles(root: root)
        try files.write([
            PhotoRecord(id: id(10), fileName: "pose one.png", sha256: digest, importedAt: now,
                        crop: NormalizedRect(x: 0.1, y: 0.30000000000000004, width: 0.5, height: 1), rotation: 90, flipped: true, noFigureFound: false, missing: false),
            PhotoRecord(id: id(11), fileName: "pose-two.png", sha256: digest, importedAt: now,
                        crop: NormalizedRect(x: 0, y: 0, width: 1, height: 1), rotation: 0, flipped: false, noFigureFound: true, missing: false),
            PhotoRecord(id: id(12), fileName: "gone/é 日本.jpg", sha256: "abc", importedAt: now,
                        crop: NormalizedRect(x: 0.25, y: 0.125, width: 0.0625, height: 0.333333333333), rotation: 270, flipped: false, noFigureFound: false, missing: true),
        ], to: .photos)
        try files.write(BenchmarksFile(benchmarks: [BenchmarkRecord(photoID: id(10), seconds: 120, interval: 10, markedAt: now)],
                                       attempts: [AttemptRecord(id: id(20), photoID: id(10), date: now, sessionID: id(30), drawingFileName: "attempt.png"),
                                                  AttemptRecord(id: id(21), photoID: id(10), date: now, sessionID: id(30), drawingFileName: nil)]), to: .benchmarks)
        try files.write([SetRecord(id: id(40), name: "Figures / gesto é", createdAt: now, photoIDs: [id(10), id(11)]),
                         SetRecord(id: id(41), name: "Empty", createdAt: now, photoIDs: [])], to: .sets)
        try files.write([PresetRecord(id: id(50), name: "Default class", steps: QueueBuilder.defaultClass)], to: .presets)
        try files.write([PlanTick(key: "w01-mon-focus", countsOn: "2026-11-02", checkedAt: now),
                         PlanTick(key: "w02-mon-focus-r1", countsOn: "2026-11-16", checkedAt: now)], to: .checks)
        try files.write(PlanRecord(weekOneStart: "2026-11-02", repeats: [PlanRepeat(number: 1, afterSequenceIndex: 1, weekCount: 1)]), to: .plan)
        try files.write([SessionRecord(id: id(30), startedAt: now, drawingSeconds: 150, poseCount: 2, photoIDs: [id(10), id(11)])], to: .sessions)
        let canvas = root.appending(path: "Canvases/Untitled")
        try fm.createDirectory(at: canvas.appending(path: "Images"), withIntermediateDirectories: true)
        try png.write(to: canvas.appending(path: "Images/board.png"))
        try FreehandFiles(root: canvas).save(canvasSample)
    }

    private var canvasSample: FreehandDocument {
        FreehandDocument(greyscale: true, camera: FreehandCamera(offset: CGPoint(x: 12.5, y: -4), zoom: 1.5),
                         items: [FreehandItem(id: id(60), fileName: "board.png", center: CGPoint(x: 100, y: 200.25), fullSize: CGSize(width: 640, height: 480),
                                              rotation: 15, flipped: true, crop: CGRect(x: 0.1, y: 0.2, width: 0.5, height: 0.75))])
    }

    struct Streak: Codable { let days: [String], today: String, current: Int, longest: Int }
    struct TickSum: Codable { let count: Int, minutes: Int }
    struct Expectations: Codable {
        let start: String
        let repeats: [PlanRepeat]
        /// date → "plan:seq:weekday", "forever:day" or "none"
        let refs: [String: String]
        let titles: [String: String]
        let practiceLines: [String: String]
        /// "seq/weekday/kind" → key, every block of the whole sequence
        let tickKeys: [String: String]
        let endDate: String
        let streaks: [Streak]
        let ticks: [PlanTick]
        let tickSummary: [String: TickSum]
        let heatmap: [[Int]]
    }

    private func expectations() -> Expectations {
        let start = "2026-11-02"
        let repeats = [PlanRepeat(number: 1, afterSequenceIndex: 1, weekCount: 1), PlanRepeat(number: 2, afterSequenceIndex: 2, weekCount: 2)]
        let s = PlanSchedule(plan: .shared, start: PlanDayKey.date(start, calendar: cal)!, repeats: repeats, calendar: cal)
        func k(_ d: Date) -> String { PlanDayKey.key(d, calendar: cal) }
        var refs: [String: String] = [:], titles: [String: String] = [:], lines: [String: String] = [:]
        for offset in stride(from: -3, to: 7 * (s.sequence.count + 6), by: 3) {
            let date = cal.date(byAdding: .day, value: offset, to: PlanDayKey.date(start, calendar: cal)!)!
            switch s.ref(on: date) {
            case nil: refs[k(date)] = "none"
            case let .plan(q, w)?: refs[k(date)] = "plan:\(q):\(w)"
            case let .forever(day)?: refs[k(date)] = "forever:\(day)"
            }
            let ref = s.todayRef(date)
            titles[k(date)] = s.title(for: ref)
            lines[k(date)] = s.practiceLine(ref)
        }
        var keys: [String: String] = [:]
        for q in s.sequence.indices {
            for w in 0..<7 {
                for block in s.day(for: .plan(sequenceIndex: q, weekday: w)).blocks {
                    keys["\(q)/\(w)/\(block.kind.rawValue)"] = s.tickKey(.plan(sequenceIndex: q, weekday: w), block.kind)
                }
            }
        }
        keys["forever/2027-11-06/ritual"] = s.tickKey(.forever(day: "2027-11-06"), .ritual)
        let streaks = [(["2026-11-02", "2026-11-03", "2026-11-04"], "2026-11-04"), (["2026-11-02", "2026-11-03"], "2026-11-04"),
                       (["2026-11-02"], "2026-11-04"), (["2026-11-07", "2026-11-09"], "2026-11-09"), (["2026-11-06", "2026-11-09", "2026-11-10"], "2026-11-10"),
                       ([], "2026-11-04")].map { days, today -> Streak in
            let set = Set(days.map { PlanDayKey.date($0, calendar: cal)! })
            return Streak(days: days, today: today, current: PlanProgress.currentStreak(practiceDays: set, today: PlanDayKey.date(today, calendar: cal)!, calendar: cal),
                          longest: PlanProgress.longestStreak(practiceDays: set, calendar: cal))
        }
        let firstBlock = s.day(for: .plan(sequenceIndex: 0, weekday: 0)).blocks[0].kind
        let ticks = [PlanTick(key: s.tickKey(.plan(sequenceIndex: 0, weekday: 0), firstBlock), countsOn: "2026-11-02", checkedAt: now),
                     PlanTick(key: s.tickKey(.plan(sequenceIndex: 2, weekday: 0), firstBlock), countsOn: "2026-11-16", checkedAt: now),
                     PlanTick(key: "f-2027-11-06-ritual", countsOn: "2026-11-16", checkedAt: now),
                     PlanTick(key: "no-such-key", countsOn: "2026-11-17", checkedAt: now)]
        let summary = PlanProgress.tickSummary(ticks, schedule: s).mapValues { TickSum(count: $0.count, minutes: $0.minutes) }
        let heatmap = [[100, 1], [100, 2], [0, 0]].map { [$0[0], $0[1], PlanProgress.heatmapSeconds(sessionSeconds: $0[0], tickedMinutes: $0[1])] }
        return Expectations(start: start, repeats: repeats, refs: refs, titles: titles, practiceLines: lines, tickKeys: keys, endDate: k(s.endDate),
                            streaks: streaks, ticks: ticks, tickSummary: summary, heatmap: heatmap)
    }

    private func relativeFiles(_ root: URL) -> [String] {
        let all = FileManager.default.enumerator(atPath: root.path)?.compactMap { $0 as? String } ?? []
        return all.filter { !$0.hasSuffix(".DS_Store") && !$0.hasSuffix("/") }.sorted().filter {
            var isDir: ObjCBool = false
            FileManager.default.fileExists(atPath: root.appending(path: $0).path, isDirectory: &isDir)
            return !isDir.boolValue
        }
    }

    @Test func fixturesMatchTheEncoders() throws {
        let fm = FileManager.default
        let library = Self.root.appending(path: "library")
        let plan = try LibraryFiles.encoder.encode(expectations())
        let canvas = try JSONEncoder().encode(canvasSample)
        if ProcessInfo.processInfo.environment["LINEWORK_WRITE_FIXTURES"] == "1" {
            try? fm.removeItem(at: library)
            try writeLibrary(at: library)
            try plan.write(to: Self.root.appending(path: "plan-expectations.json"))
            try canvas.write(to: Self.root.appending(path: "canvas-sample.json"))
        }
        let scratch = fm.temporaryDirectory.appending(path: "lw-fixture-\(UUID().uuidString)")
        defer { try? fm.removeItem(at: scratch) }
        try writeLibrary(at: scratch)
        #expect(relativeFiles(scratch) == relativeFiles(library))
        for name in relativeFiles(scratch) where !name.hasSuffix("canvas.json") {
            #expect(try Data(contentsOf: scratch.appending(path: name)) == Data(contentsOf: library.appending(path: name)), "\(name)")
        }
        #expect(try Data(contentsOf: Self.root.appending(path: "plan-expectations.json")) == plan)
        let decoded = try JSONDecoder().decode(FreehandDocument.self, from: Data(contentsOf: Self.root.appending(path: "canvas-sample.json")))
        #expect(decoded == canvasSample)
    }

    @Test func decodesFilesWrittenByWindows() throws {
        let folder = Self.root.appending(path: "written-by-windows")
        guard FileManager.default.fileExists(atPath: folder.path) else { return }
        let files = LibraryFiles(root: folder)
        guard case let .loaded(photos) = try files.read(.photos), photos.count == 3 else { Issue.record("photos"); return }
        #expect(photos[0].crop.y == 0.30000000000000004)
        guard case let .loaded(benchmarks) = try files.read(.benchmarks), benchmarks.attempts.count == 2 else { Issue.record("benchmarks"); return }
        #expect(benchmarks.attempts[1].drawingFileName == nil)
        guard case .loaded = try files.read(.sets), case .loaded = try files.read(.presets), case .loaded = try files.read(.checks),
              case .loaded = try files.read(.plan), case .loaded = try files.read(.sessions) else { Issue.record("other"); return }
        let manifest = try LibraryFiles.decoder.decode(LibraryManifest.self, from: Data(contentsOf: folder.appending(path: "library.json")))
        #expect(manifest.format == 1)
        let canvas = FreehandFiles(root: folder.appending(path: "Canvases/Untitled"))
        #expect(canvas.load() == canvasSample)
    }

}
