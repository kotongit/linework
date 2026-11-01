import Foundation
import Testing
@testable import LineworkCore

@Suite struct LibraryFolderTests {
    private let fm = FileManager.default
    private func temp() -> URL {
        let url = fm.temporaryDirectory.appending(path: "lf-\(UUID().uuidString)")
        try! fm.createDirectory(at: url, withIntermediateDirectories: true)
        return url
    }
    private let now = Date(timeIntervalSinceReferenceDate: 800_000_000)

    @Test func classifiesLibrary() throws {
        let url = temp()
        try LibraryFolder.create(at: url, weekOneStart: "2026-11-02", now: now)
        #expect(LibraryFolder.classify(url) == .library)
    }

    @Test func classifiesEmpty() throws {
        let url = temp()
        #expect(LibraryFolder.classify(url) == .empty)
        try Data().write(to: url.appending(path: ".DS_Store"))
        #expect(LibraryFolder.classify(url) == .empty)
    }

    @Test func classifiesOtherContents() throws {
        let url = temp()
        try Data().write(to: url.appending(path: "notes.txt"))
        #expect(LibraryFolder.classify(url) == .otherContents)
    }

    @Test func classifiesMissingAsUnavailable() {
        #expect(LibraryFolder.classify(fm.temporaryDirectory.appending(path: "nope-\(UUID().uuidString)")) == .unavailable)
    }

    @Test func classifiesNewerFormat() throws {
        let url = temp()
        try Data(#"{"format":99,"id":"\#(UUID().uuidString)","createdAt":"2026-11-01T00:00:00Z"}"#.utf8).write(to: url.appending(path: "library.json"))
        #expect(LibraryFolder.classify(url) == .newerFormat)
    }

    @Test func createMakesTheLayout() throws {
        let url = temp()
        try LibraryFolder.create(at: url, weekOneStart: "2026-11-02", now: now)
        for name in ["library.json", "Photos", "Drawings", "Canvases", "Progress", "Progress/plan.json"] {
            #expect(fm.fileExists(atPath: url.appending(path: name).path), "\(name)")
        }
        #expect(try LibraryFiles(root: url).read(.plan) == .loaded(PlanRecord(weekOneStart: "2026-11-02")))
    }

    @Test func createKeepsExistingFiles() throws {
        let url = temp()
        let keep = url.appending(path: "notes.txt")
        try Data("mine".utf8).write(to: keep)
        try LibraryFolder.create(at: url, weekOneStart: "2026-11-02", now: now)
        #expect(try Data(contentsOf: keep) == Data("mine".utf8))
    }

    @Test func mondayOfAnyWeekday() {
        var cal = Calendar(identifier: .gregorian)
        cal.timeZone = TimeZone(identifier: "America/Mexico_City")!
        for day in ["2026-11-02", "2026-11-05", "2026-11-08"] {
            #expect(LibraryFolder.monday(of: PlanDayKey.date(day, calendar: cal)!, calendar: cal) == "2026-11-02", "\(day)")
        }
    }

    @Test func readOnlyFolderFailsCleanly() throws {
        let url = temp()
        try fm.setAttributes([.posixPermissions: 0o555], ofItemAtPath: url.path)
        defer { try? fm.setAttributes([.posixPermissions: 0o755], ofItemAtPath: url.path) }
        #expect(throws: (any Error).self) { try LibraryFolder.create(at: url, weekOneStart: "2026-11-02", now: now) }
        #expect(try fm.contentsOfDirectory(atPath: url.path).isEmpty)
    }

    @Test func partlyFailedCreateLeavesNothing() throws {
        let url = temp()
        // A file where a folder must go makes the create fail midway.
        try Data().write(to: url.appending(path: "Progress"))
        #expect(throws: (any Error).self) { try LibraryFolder.create(at: url, weekOneStart: "2026-11-02", now: now) }
        #expect(try fm.contentsOfDirectory(atPath: url.path) == ["Progress"])
    }

    @Test func createKeepsAnExistingPlan() throws {
        let url = temp()
        try fm.createDirectory(at: url.appending(path: "Progress"), withIntermediateDirectories: true)
        let plan = PlanRecord(weekOneStart: "2026-11-02", repeats: [PlanRepeat(number: 1, afterSequenceIndex: 2, weekCount: 1)])
        try LibraryFiles(root: url).write(plan, to: .plan)
        try LibraryFolder.create(at: url, weekOneStart: "2030-01-07", now: now)
        #expect(try LibraryFiles(root: url).read(.plan) == .loaded(plan))
    }
}
