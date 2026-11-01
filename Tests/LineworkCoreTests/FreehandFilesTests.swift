import CoreGraphics
import Foundation
import Testing
@testable import LineworkCore

private func withTempRoot(_ body: (URL, FreehandFiles) throws -> Void) throws {
    let root = FileManager.default.temporaryDirectory
        .appendingPathComponent("freehand-files-\(UUID().uuidString)", isDirectory: true)
    try FileManager.default.createDirectory(at: root, withIntermediateDirectories: true)
    defer { try? FileManager.default.removeItem(at: root) }
    let files = FreehandFiles(root: root)
    try files.prepareImagesDir()
    try body(root, files)
}

private func makeItem(_ files: FreehandFiles, name: String = "\(UUID().uuidString).png",
                      write: Bool = true, center: CGPoint = .zero) -> FreehandItem {
    if write { try? Data([1, 2, 3]).write(to: files.imageURL(name)) }
    return FreehandItem(fileName: name, center: center, fullSize: CGSize(width: 100, height: 50))
}

private func exists(_ url: URL) -> Bool { FileManager.default.fileExists(atPath: url.path) }

@Suite struct FreehandFilesTests {
    @Test func loadMissingIsEmpty() throws {
        try withTempRoot { _, files in
            #expect(files.load() == .empty)
            #expect(!files.hasDamagedFile)
        }
    }

    @Test func saveLoadRoundTrip() throws {
        try withTempRoot { _, files in
            let a = makeItem(files, center: CGPoint(x: 5, y: 6))
            var b = makeItem(files, center: CGPoint(x: -20, y: 30))
            b.rotation = 45; b.flipped = true
            let doc = FreehandDocument(greyscale: true,
                                       camera: FreehandCamera(offset: CGPoint(x: 12, y: -7), zoom: 2.5),
                                       items: [a, b])
            try files.save(doc)
            #expect(files.load() == doc)
        }
    }

    @Test func damagedFileIsSetAside() throws {
        try withTempRoot { root, files in
            let item = makeItem(files)
            try Data("garbage{".utf8).write(to: root.appendingPathComponent("canvas.json"))
            #expect(files.load() == .empty)
            let damaged = root.appendingPathComponent("canvas.damaged.json")
            #expect(try Data(contentsOf: damaged) == Data("garbage{".utf8))
            #expect(!exists(root.appendingPathComponent("canvas.json")))
            #expect(exists(files.imageURL(item.fileName)))
            #expect(files.hasDamagedFile)
        }
    }

    @Test func newerVersionIsSetAside() throws {
        try withTempRoot { root, files in
            let item = makeItem(files)
            let newer = FreehandDocument(version: 99, items: [item])
            let data = try JSONEncoder().encode(newer)
            try data.write(to: root.appendingPathComponent("canvas.json"))
            #expect(files.load() == .empty)
            #expect(try Data(contentsOf: root.appendingPathComponent("canvas.damaged.json")) == data)
            #expect(exists(files.imageURL(item.fileName)))
        }
    }

    @Test func damagedKeepsEarlierDamaged() throws {
        try withTempRoot { root, files in
            try Data("first".utf8).write(to: root.appendingPathComponent("canvas.damaged.json"))
            try Data("second".utf8).write(to: root.appendingPathComponent("canvas.json"))
            #expect(files.load() == .empty)
            #expect(try Data(contentsOf: root.appendingPathComponent("canvas.damaged.json")) == Data("first".utf8))
            #expect(try Data(contentsOf: root.appendingPathComponent("canvas.damaged 2.json")) == Data("second".utf8))
        }
    }

    @Test func itemWithMissingFileIsDropped() throws {
        try withTempRoot { _, files in
            let kept = makeItem(files)
            let gone = makeItem(files, write: false)
            try files.save(FreehandDocument(items: [gone, kept]))
            #expect(files.load().items == [kept])
        }
    }

    @Test func orphansAreDeletedAfterGoodLoad() throws {
        try withTempRoot { _, files in
            let kept = makeItem(files)
            let orphan = makeItem(files)
            try files.save(FreehandDocument(items: [kept]))
            #expect(files.load().items == [kept])
            #expect(exists(files.imageURL(kept.fileName)))
            #expect(!exists(files.imageURL(orphan.fileName)))
        }
    }

    @Test func damagedFileBlocksOrphanCleanupLater() throws {
        try withTempRoot { root, files in
            let old = makeItem(files)
            try Data("garbage".utf8).write(to: root.appendingPathComponent("canvas.json"))
            _ = files.load()
            let fresh = makeItem(files)
            try files.save(FreehandDocument(items: [fresh]))
            #expect(files.load().items == [fresh])
            #expect(exists(files.imageURL(old.fileName)))
        }
    }

    @Test func removingOneOfTwoCopiesKeepsTheOther() throws {
        try withTempRoot { _, files in
            let source = Data([9, 8, 7, 6])
            let a = makeItem(files, write: false)
            let b = makeItem(files, write: false)
            try source.write(to: files.imageURL(a.fileName))
            try source.write(to: files.imageURL(b.fileName))
            try files.save(FreehandDocument(items: [a, b]))
            files.removeImage(a.fileName)
            #expect(!exists(files.imageURL(a.fileName)))
            #expect(try Data(contentsOf: files.imageURL(b.fileName)) == source)
            #expect(files.load().items == [b])
        }
    }

    @Test func saveLeavesNoTempFiles() throws {
        try withTempRoot { root, files in
            try files.save(FreehandDocument(items: [makeItem(files)]))
            try files.save(FreehandDocument(greyscale: true))
            let names = try FileManager.default.contentsOfDirectory(atPath: root.path).sorted()
            #expect(names == ["Images", "canvas.json"])
        }
    }

    @Test func loadSanitizesCamera() throws {
        try withTempRoot { root, files in
            // Out-of-range finite zoom is clamped; offset is kept.
            try files.save(FreehandDocument(camera: FreehandCamera(offset: CGPoint(x: 3, y: 4), zoom: 100)))
            #expect(files.load().camera == FreehandCamera(offset: CGPoint(x: 3, y: 4), zoom: 8))
            try files.save(FreehandDocument(camera: FreehandCamera(offset: CGPoint(x: 3, y: 4), zoom: 0.001)))
            #expect(files.load().camera.zoom == 0.1)

            // JSONDecoder rejects overflowing literals such as 1e999, so such a file is
            // treated as damaged rather than loaded; non-finite values are covered below.
            let json = #"{"version":1,"greyscale":false,"camera":{"offset":[0,0],"zoom":1e999},"items":[]}"#
            try Data(json.utf8).write(to: root.appendingPathComponent("canvas.json"))
            #expect(files.load() == .empty)
            #expect(files.hasDamagedFile)

            // Non-finite values, which the load path applies after decoding.
            let nan = Double.nan
            #expect(FreehandCamera(offset: CGPoint(x: nan, y: 0), zoom: 1).sanitized == .home)
            #expect(FreehandCamera(offset: .zero, zoom: nan).sanitized == .home)
            #expect(FreehandCamera(offset: .zero, zoom: .infinity).sanitized == .home)
            #expect(FreehandCamera(offset: CGPoint(x: 0, y: -.infinity), zoom: 2).sanitized == .home)
        }
    }

    @Test func askingForTheImagesPathCreatesNothing() {
        let root = FileManager.default.temporaryDirectory.appending(path: "gone-\(UUID().uuidString)")
        _ = FreehandFiles(root: root.appending(path: "Canvases/A")).imagesDir
        #expect(!FileManager.default.fileExists(atPath: root.path))
    }

    @Test func savingCantRecreateAMissingLibrary() {
        let root = FileManager.default.temporaryDirectory.appending(path: "gone-\(UUID().uuidString)")
        #expect(throws: (any Error).self) { try FreehandFiles(root: root.appending(path: "Canvases/A")).save(.empty) }
        #expect(!FileManager.default.fileExists(atPath: root.path))
    }

    @Test func aSecondDamagedCanvasDoesNotReplaceTheFirst() throws {
        let root = FileManager.default.temporaryDirectory.appending(path: "dm-\(UUID().uuidString)")
        try FileManager.default.createDirectory(at: root, withIntermediateDirectories: true)
        let files = FreehandFiles(root: root)
        try Data("first".utf8).write(to: root.appending(path: "canvas.json"))
        _ = files.load()
        try Data("second".utf8).write(to: root.appending(path: "canvas.json"))
        _ = files.load()
        #expect(try Data(contentsOf: root.appending(path: "canvas.damaged.json")) == Data("first".utf8))
        #expect(try Data(contentsOf: root.appending(path: "canvas.damaged 2.json")) == Data("second".utf8))
    }
}
