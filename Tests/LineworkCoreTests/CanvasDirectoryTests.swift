import Foundation
import Testing
@testable import LineworkCore

@Suite struct CanvasDirectoryTests {
    private let fm = FileManager.default
    private func dir() -> CanvasDirectory {
        let url = fm.temporaryDirectory.appending(path: "cd-\(UUID().uuidString)")
        try! fm.createDirectory(at: url, withIntermediateDirectories: true)
        return CanvasDirectory(canvasesRoot: url)
    }

    @Test func firstIsUntitled() throws { #expect(try dir().create(name: nil) == "Untitled") }

    @Test func nextIsUntitled2() throws {
        let d = dir()
        _ = try d.create(name: nil)
        #expect(try d.create(name: nil) == "Untitled 2")
        #expect(d.list() == ["Untitled", "Untitled 2"])
    }

    @Test func renameMovesFolder() throws {
        let d = dir()
        _ = try d.create(name: "Old")
        try d.rename("Old", to: "New")
        #expect(d.list() == ["New"])
    }

    @Test func rejectsSlashColonDotsAndEmpty() throws {
        let d = dir()
        for bad in ["a/b", "a:b", ".hidden", "", "   ", ".", ".."] {
            #expect(throws: CanvasNameError.invalid, "\(bad)") { _ = try d.create(name: bad) }
        }
    }

    @Test func namesAreTrimmed() throws { #expect(try dir().create(name: "  Hands  ") == "Hands") }

    @Test func duplicateCopiesImages() throws {
        let d = dir()
        _ = try d.create(name: "Refs")
        let images = d.url(of: "Refs").appending(path: "Images")
        try fm.createDirectory(at: images, withIntermediateDirectories: true)
        try Data("x".utf8).write(to: images.appending(path: "a.jpg"))
        let copy = try d.duplicate("Refs")
        #expect(copy == "Refs copy")
        #expect(fm.fileExists(atPath: d.url(of: copy).appending(path: "Images/a.jpg").path))
        #expect(try d.duplicate("Refs") == "Refs copy 2")
    }

    @Test func ensureOneCreatesWhenEmpty() throws {
        let d = dir()
        #expect(try d.ensureOne() == "Untitled")
        _ = try d.create(name: "Another")
        #expect(try d.ensureOne() == "Another")
        #expect(d.list().count == 2)
    }

    @Test func listIgnoresFiles() throws {
        let d = dir()
        _ = try d.create(name: "Real")
        try Data().write(to: d.canvasesRoot.appending(path: ".DS_Store"))
        try Data().write(to: d.canvasesRoot.appending(path: "note.txt"))
        #expect(d.list() == ["Real"])
    }

    @Test func caseOnlyRenameSucceeds() throws {
        let d = dir()
        _ = try d.create(name: "hands")
        try d.rename("hands", to: "Hands")
        #expect(d.list() == ["Hands"])
    }

    @Test func unicodeFormsAreOneName() throws {
        let d = dir()
        _ = try d.create(name: "caf\u{E9}")
        #expect(throws: CanvasNameError.duplicate) { _ = try d.create(name: "cafe\u{301}") }
    }

    @Test func trueDuplicateRefused() throws {
        let d = dir()
        _ = try d.create(name: "A")
        _ = try d.create(name: "B")
        #expect(throws: CanvasNameError.duplicate) { try d.rename("B", to: "a") }
        #expect(d.list() == ["A", "B"])
    }
}
