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

    /// `Images/`, created on demand.
    public var imagesDir: URL {
        let url = root.appendingPathComponent("Images", isDirectory: true)
        try? FileManager.default.createDirectory(at: url, withIntermediateDirectories: true)
        return url
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
            removeOrphans(keeping: Set(doc.items.map(\.fileName)))
        }
        return doc
    }

    /// Atomic write of `canvas.json`.
    public func save(_ doc: FreehandDocument) throws {
        try FileManager.default.createDirectory(at: root, withIntermediateDirectories: true)
        let data = try JSONEncoder().encode(doc)
        try data.write(to: canvasURL, options: .atomic)
    }

    public func removeImage(_ fileName: String) {
        try? FileManager.default.removeItem(at: imagesDir.appendingPathComponent(fileName))
    }

    private func setAsideCanvas() {
        let fm = FileManager.default
        try? fm.removeItem(at: damagedURL)
        do {
            try fm.moveItem(at: canvasURL, to: damagedURL)
        } catch {
            // Could not keep the bad file: leave it where it is rather than lose it.
        }
    }

    private func removeOrphans(keeping used: Set<String>) {
        let fm = FileManager.default
        let names = (try? fm.contentsOfDirectory(atPath: imagesDir.path)) ?? []
        for name in names where !used.contains(name) {
            try? fm.removeItem(at: imagesDir.appendingPathComponent(name))
        }
    }
}
