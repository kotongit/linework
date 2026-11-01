import Foundation
import Testing
@testable import LineworkCore

@Suite struct LibraryFilesTests {
    private func tempRoot() -> URL {
        let url = FileManager.default.temporaryDirectory.appending(path: "lw-\(UUID().uuidString)")
        try! FileManager.default.createDirectory(at: url.appending(path: "Progress"), withIntermediateDirectories: true)
        return url
    }
    private let date = Date(timeIntervalSinceReferenceDate: 800_000_000)

    private func photo() -> PhotoRecord {
        PhotoRecord(id: UUID(), fileName: "pose.jpg", sha256: "abc", importedAt: date, crop: NormalizedRect(x: 0.1, y: 0.2, width: 0.5, height: 0.6),
                    rotation: 90, flipped: true, noFigureFound: false, missing: false)
    }

    @Test func roundTripsEveryFile() throws {
        let files = LibraryFiles(root: tempRoot())
        let id = UUID()
        let photos = [photo()]
        let benchmarks = BenchmarksFile(benchmarks: [BenchmarkRecord(photoID: id, seconds: 120, interval: 10, markedAt: date)],
                                        attempts: [AttemptRecord(id: UUID(), photoID: id, date: date, sessionID: UUID(), drawingFileName: "a.jpg")])
        let sets = [SetRecord(id: UUID(), name: "Figures", createdAt: date, photoIDs: [id])]
        let presets = [PresetRecord(id: UUID(), name: "Default class", steps: [ClassStep(count: 2, seconds: 30)])]
        let checks = [PlanTick(key: "w01-mon-focus", countsOn: "2026-11-02", checkedAt: date)]
        let plan = PlanRecord(weekOneStart: "2026-11-02", repeats: [PlanRepeat(number: 1, afterSequenceIndex: 2, weekCount: 1)])
        let sessions = [SessionRecord(id: UUID(), startedAt: date, drawingSeconds: 60, poseCount: 2, photoIDs: [id])]
        try files.write(photos, to: .photos)
        try files.write(benchmarks, to: .benchmarks)
        try files.write(sets, to: .sets)
        try files.write(presets, to: .presets)
        try files.write(checks, to: .checks)
        try files.write(plan, to: .plan)
        try files.write(sessions, to: .sessions)
        #expect(try files.read(.photos) == .loaded(photos))
        #expect(try files.read(.benchmarks) == .loaded(benchmarks))
        #expect(try files.read(.sets) == .loaded(sets))
        #expect(try files.read(.presets) == .loaded(presets))
        #expect(try files.read(.checks) == .loaded(checks))
        #expect(try files.read(.plan) == .loaded(plan))
        #expect(try files.read(.sessions) == .loaded(sessions))
    }

    @Test func missingFileIsMissing() throws {
        #expect(try LibraryFiles(root: tempRoot()).read(.photos) == .missing)
    }

    @Test func damagedFileIsSetAsideNotOverwritten() throws {
        let root = tempRoot()
        let url = root.appending(path: "Progress/checks.json")
        try Data("not json".utf8).write(to: url)
        let files = LibraryFiles(root: root)
        let aside = root.appending(path: "Progress/checks.damaged.json")
        #expect(try files.read(.checks) == .damaged(setAsideAs: aside))
        #expect(try Data(contentsOf: aside) == Data("not json".utf8))
        #expect(!FileManager.default.fileExists(atPath: url.path))
    }

    @Test func newerVersionThrowsAndChangesNothing() throws {
        let root = tempRoot()
        let url = root.appending(path: "Progress/checks.json")
        let bytes = Data(#"{"version":99,"items":[]}"#.utf8)
        try bytes.write(to: url)
        #expect(throws: LibraryError.newerFormat(file: "checks.json")) { try LibraryFiles(root: root).read(.checks) }
        #expect(try Data(contentsOf: url) == bytes)
    }

    @Test func writesAreIndentedSortedISO() throws {
        let root = tempRoot()
        try LibraryFiles(root: root).write([PlanTick(key: "k", countsOn: "2026-11-02", checkedAt: date)], to: .checks)
        let text = try String(contentsOf: root.appending(path: "Progress/checks.json"), encoding: .utf8)
        #expect(text.contains("\"version\" : 1"))
        #expect(text.contains("\n  "))
        #expect(text.contains("2026-") || text.contains("2025-"))
        #expect(text.range(of: "\"items\"")!.lowerBound < text.range(of: "\"version\"")!.lowerBound)
    }

    @Test func atomicWriteLeavesNoTemporaryFiles() throws {
        let root = tempRoot()
        try LibraryFiles(root: root).write([photo()], to: .photos)
        #expect(try FileManager.default.contentsOfDirectory(atPath: root.appending(path: "Progress").path) == ["photos.json"])
    }

    @Test func conflictCopiesIgnored() throws {
        let root = tempRoot()
        try Data("junk".utf8).write(to: root.appending(path: "Progress/checks 2.json"))
        let files = LibraryFiles(root: root)
        #expect(try files.read(.checks) == .missing)
        #expect(FileManager.default.fileExists(atPath: root.appending(path: "Progress/checks 2.json").path))
    }

    @Test func writingToAMissingLibraryFailsAndCreatesNothing() throws {
        let root = FileManager.default.temporaryDirectory.appending(path: "lw-gone-\(UUID().uuidString)")
        #expect(throws: LibraryError.unavailable) { try LibraryFiles(root: root).write([photo()], to: .photos) }
        #expect(!FileManager.default.fileExists(atPath: root.path))
    }
}
