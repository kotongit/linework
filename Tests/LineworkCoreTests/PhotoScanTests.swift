import Foundation
import Testing
@testable import LineworkCore

@Suite struct PhotoScanTests {
    private func record(_ name: String, hash: String = "h", missing: Bool = false, id: UUID = UUID()) -> PhotoRecord {
        PhotoRecord(id: id, fileName: name, sha256: hash, importedAt: .now, crop: .init(x: 0, y: 0, width: 1, height: 1), rotation: 0,
                    flipped: false, noFigureFound: false, missing: missing)
    }
    private func file(_ name: String, placeholder: Bool = false) -> ScannedFile { ScannedFile(name: name, size: 10, isPlaceholder: placeholder) }

    @Test func newImageIsAdded() {
        let result = PhotoScan.decide(records: [], files: [file("a.jpg")], folderExists: true) { _ in "x" }
        #expect(result.added == ["a.jpg"])
    }

    @Test func renameKeepsRecord() {
        let r = record("old.jpg", hash: "same")
        let result = PhotoScan.decide(records: [r], files: [file("new.jpg")], folderExists: true) { _ in "same" }
        #expect(result.adopted.count == 1 && result.adopted[0].recordID == r.id && result.adopted[0].newName == "new.jpg")
        #expect(result.added.isEmpty && result.missing.isEmpty)
    }

    @Test func deletedIsMarkedMissingNotRemoved() {
        let r = record("a.jpg")
        let other = record("b.jpg")
        let result = PhotoScan.decide(records: [r, other], files: [file("b.jpg")], folderExists: true) { _ in "z" }
        #expect(result.missing == [r.id])
    }

    @Test func restoredFileReconnects() {
        let r = record("a.jpg", missing: true)
        let result = PhotoScan.decide(records: [r], files: [file("a.jpg")], folderExists: true) { _ in "h" }
        #expect(result.restored == [r.id] && result.added.isEmpty)
    }

    @Test func emptyOrAbsentFolderChangesNothing() {
        let r = record("a.jpg")
        for exists in [true, false] {
            let result = PhotoScan.decide(records: [r], files: [], folderExists: exists) { _ in "h" }
            #expect(result.folderUnavailable && result.missing.isEmpty, "\(exists)")
        }
        #expect(!PhotoScan.decide(records: [], files: [], folderExists: true) { _ in "h" }.folderUnavailable)
    }

    @Test func nonImagesIgnored() {
        #expect(PhotoScan.decide(records: [], files: [file("notes.txt"), file("x.pdf")], folderExists: true) { _ in "h" }.added.isEmpty)
    }

    @Test func placeholdersSkipped() {
        let result = PhotoScan.decide(records: [], files: [file("a.jpg", placeholder: true), file(".b.jpg.icloud")], folderExists: true) { _ in "h" }
        #expect(result.added.isEmpty)
    }

    @Test func placeholderOfAKnownPhotoIsNotMissing() {
        let r = record("a.jpg")
        let other = record("b.jpg")
        let result = PhotoScan.decide(records: [r, other], files: [file("a.jpg", placeholder: true), file("b.jpg")], folderExists: true) { _ in "h" }
        #expect(result.missing.isEmpty)
    }

    @Test func hiddenFilesIgnored() {
        #expect(PhotoScan.decide(records: [], files: [file(".DS_Store"), file(".hidden.jpg")], folderExists: true) { _ in "h" }.added.isEmpty)
    }

    @Test func onlyNewFilesAreHashed() {
        var calls = 0
        let known = record("a.jpg")
        let gone = record("gone.jpg", hash: "g")
        _ = PhotoScan.decide(records: [known, gone], files: [file("a.jpg"), file("new.jpg")], folderExists: true) { _ in calls += 1; return "n" }
        #expect(calls == 1)
    }

    @Test func noHashingWithoutAMissingRecord() {
        var calls = 0
        _ = PhotoScan.decide(records: [record("a.jpg")], files: [file("a.jpg"), file("new.jpg")], folderExists: true) { _ in calls += 1; return "n" }
        #expect(calls == 0)
    }

    @Test func identicalCopiesAdoptOnce() {
        let r = record("old.jpg", hash: "same")
        let result = PhotoScan.decide(records: [r], files: [file("one.jpg"), file("two.jpg")], folderExists: true) { _ in "same" }
        #expect(result.adopted.count == 1)
        #expect(result.added.count == 1)
        #expect(Set(result.added + [result.adopted[0].newName]) == ["one.jpg", "two.jpg"])
    }

    @Test func imageNames() {
        for name in ["a.JPG", "b.jpeg", "c.png", "d.heic", "e.webp", "f.tiff", "g.gif", "h.bmp"] { #expect(PhotoScan.isImageName(name), "\(name)") }
        for name in ["a.txt", "b", "c.json"] { #expect(!PhotoScan.isImageName(name), "\(name)") }
    }

    @Test func uniqueNameHandlesCaseAndUnicode() {
        #expect(PhotoScan.uniqueName("pose.jpg", taken: []) == "pose.jpg")
        #expect(PhotoScan.uniqueName("pose.jpg", taken: ["POSE.JPG"]) == "pose 2.jpg")
        #expect(PhotoScan.uniqueName("pose.jpg", taken: ["pose.jpg", "pose 2.jpg"]) == "pose 3.jpg")
        #expect(PhotoScan.uniqueName("caf\u{E9}.jpg", taken: ["cafe\u{301}.jpg"]) == "caf\u{E9} 2.jpg")
    }

    @Test func sha256OfAKnownFile() throws {
        let url = FileManager.default.temporaryDirectory.appending(path: "h-\(UUID().uuidString)")
        try Data("abc".utf8).write(to: url)
        #expect(try FileHash.sha256(of: url) == "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad")
    }
}
