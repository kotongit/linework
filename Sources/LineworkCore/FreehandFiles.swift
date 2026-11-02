import CoreGraphics
import Foundation

extension FreehandCamera {
    /// A camera that cannot produce NaN: finite zoom is clamped into `zoomRange`;
    /// any non-finite offset or zoom gives `home`.
    var sanitized: FreehandCamera {
        guard offset.x.isFinite, offset.y.isFinite, zoom.isFinite else { return .home }
        return FreehandCamera(offset: offset,
                              zoom: min(max(zoom, Self.zoomRange.lowerBound), Self.zoomRange.upperBound))
    }
}

/// Reads and writes the Freehand folder: `canvas.json`, `Images/`, `canvas.damaged.json`.
public struct FreehandFiles: Sendable {
    private let root: URL

    /// `root` is the Freehand folder.
    public init(root: URL) {
        self.root = root
    }

    private var canvasURL: URL { root.appendingPathComponent("canvas.json") }
    private var damagedURL: URL { root.appendingPathComponent("canvas.damaged.json") }

    public var imagesDir: URL { root.appendingPathComponent("Images", isDirectory: true) }

    /// Makes `Images/` if needed. Fails if the canvas has gone, rather than recreating it.
    public func prepareImagesDir() throws {
        try FileManager.default.createDirectory(at: imagesDir, withIntermediateDirectories: false)
    }

    public func imageURL(_ fileName: String) -> URL {
        imagesDir.appendingPathComponent(fileName)
    }

    public var hasDamagedFile: Bool {
        FileManager.default.fileExists(atPath: damagedURL.path)
    }

    /// Missing file: empty. Damaged or newer file: set aside as `canvas.damaged.json`, empty.
    /// Items whose image is gone are dropped; after a good load, unused images are deleted
    /// unless a damaged file exists.
    public func load() -> FreehandDocument {
        let fm = FileManager.default
        guard fm.fileExists(atPath: canvasURL.path) else { return .empty }
        guard let data = try? Data(contentsOf: canvasURL),
              var doc = try? JSONDecoder().decode(FreehandDocument.self, from: data),
              doc.version >= 1, doc.version <= FreehandDocument.currentVersion
        else {
            setAsideCanvas()
            return .empty
        }
        doc.camera = doc.camera.sanitized
        let images = imagesDir
        doc.items = doc.items.filter { fm.fileExists(atPath: images.appendingPathComponent($0.fileName).path) }
        if !hasDamagedFile {
            removeOrphans(keeping: Set(doc.items.map { PhotoScan.comparable($0.fileName) }))
        }
        return doc
    }

    /// Atomic write of `canvas.json`.
    public func save(_ doc: FreehandDocument) throws {
        // No intermediates: if the library has gone, this fails rather than making a new one.
        if !FileManager.default.fileExists(atPath: root.path) {
            try FileManager.default.createDirectory(at: root, withIntermediateDirectories: false)
        }
        let data = try JSONEncoder().encode(doc)
        try data.write(to: canvasURL, options: .atomic)
    }

    public func removeImage(_ fileName: String) {
        try? FileManager.default.removeItem(at: imagesDir.appendingPathComponent(fileName))
    }

    private func setAsideCanvas() {
        let fm = FileManager.default
        do {
            try fm.moveItem(at: canvasURL, to: LibraryFiles.freeAsideURL(for: canvasURL))
        } catch {
            // Could not keep the bad file: leave it where it is rather than lose it.
        }
    }

    private func removeOrphans(keeping used: Set<String>) {
        let fm = FileManager.default
        let names = (try? fm.contentsOfDirectory(atPath: imagesDir.path)) ?? []
        for name in names where !used.contains(PhotoScan.comparable(name)) {
            try? fm.removeItem(at: imagesDir.appendingPathComponent(name))
        }
    }
}
