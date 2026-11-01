import Foundation
import ImageIO
import UniformTypeIdentifiers

enum StorageError: Error {
    case unreadableImage
}

enum Storage {
    static let root: URL = {
        if let override = ProcessInfo.processInfo.environment["LINEWORK_LIBRARY"] {
            return URL(fileURLWithPath: override, isDirectory: true)
        }
        #if DEBUG
        // The snapshot harness overwrites and clears the canvas, so it must never reach the real data.
        // Here rather than in the harness: this runs before the app opens the Library, which the harness comes after.
        if CommandLine.arguments.contains("--snapshots") {
            FileHandle.standardError.write(Data("--snapshots needs LINEWORK_LIBRARY set to a scratch folder. Nothing was run.\n".utf8))
            exit(2)
        }
        #endif
        let base = FileManager.default.urls(for: .applicationSupportDirectory, in: .userDomainMask)[0]
        return base.appending(path: "Linework-dev", directoryHint: .isDirectory)
    }()

    static var imagesDir: URL { ensure(root.appending(path: "Images", directoryHint: .isDirectory)) }
    static var drawingsDir: URL { ensure(root.appending(path: "Drawings", directoryHint: .isDirectory)) }
    static var freehandDir: URL { ensure(root.appending(path: "Freehand", directoryHint: .isDirectory)) }

    private static func ensure(_ url: URL) -> URL {
        try? FileManager.default.createDirectory(at: url, withIntermediateDirectories: true)
        return url
    }

    static func imageURL(fileName: String) -> URL { imagesDir.appending(path: fileName) }
    static func thumbnailURL(id: UUID) -> URL { imagesDir.appending(path: "\(id.uuidString)_thumb.jpg") }
    static func drawingURL(fileName: String) -> URL { drawingsDir.appending(path: fileName) }

    static func isReadableImage(_ url: URL) -> Bool {
        guard let src = CGImageSourceCreateWithURL(url as CFURL, nil) else { return false }
        return CGImageSourceGetCount(src) > 0
    }

    /// Pixel size as displayed (EXIF orientation applied), read from the header without decoding.
    static func orientedPixelSize(_ url: URL) -> CGSize? {
        guard let src = CGImageSourceCreateWithURL(url as CFURL, nil),
              let props = CGImageSourceCopyPropertiesAtIndex(src, 0, nil) as? [CFString: Any],
              let width = props[kCGImagePropertyPixelWidth] as? Double,
              let height = props[kCGImagePropertyPixelHeight] as? Double,
              width > 0, height > 0
        else { return nil }
        // EXIF orientations 5 to 8 turn the image a quarter.
        let turned = (props[kCGImagePropertyOrientation] as? Int).map { (5...8).contains($0) } ?? false
        return turned ? CGSize(width: height, height: width) : CGSize(width: width, height: height)
    }

    /// Copies an image file into the library; throws if it isn't a readable image.
    static func importCopy(_ src: URL) throws -> (id: UUID, fileName: String) {
        guard isReadableImage(src) else { throw StorageError.unreadableImage }
        let id = UUID()
        let ext = src.pathExtension.isEmpty ? "img" : src.pathExtension.lowercased()
        let fileName = "\(id.uuidString).\(ext)"
        try FileManager.default.copyItem(at: src, to: imageURL(fileName: fileName))
        return (id, fileName)
    }

    /// 400px long-edge JPEG thumbnail, EXIF orientation baked in.
    static func makeThumbnail(id: UUID, fileName: String) throws {
        guard let src = CGImageSourceCreateWithURL(imageURL(fileName: fileName) as CFURL, nil),
              let thumb = CGImageSourceCreateThumbnailAtIndex(src, 0, [
                  kCGImageSourceCreateThumbnailFromImageAlways: true,
                  kCGImageSourceCreateThumbnailWithTransform: true,
                  kCGImageSourceThumbnailMaxPixelSize: 400,
              ] as CFDictionary),
              let dest = CGImageDestinationCreateWithURL(thumbnailURL(id: id) as CFURL, UTType.jpeg.identifier as CFString, 1, nil)
        else { throw StorageError.unreadableImage }
        CGImageDestinationAddImage(dest, thumb, [kCGImageDestinationLossyCompressionQuality: 0.85] as CFDictionary)
        guard CGImageDestinationFinalize(dest) else { throw StorageError.unreadableImage }
    }

    static func copyDrawing(_ src: URL) throws -> String {
        guard isReadableImage(src) else { throw StorageError.unreadableImage }
        let ext = src.pathExtension.isEmpty ? "img" : src.pathExtension.lowercased()
        let fileName = "\(UUID().uuidString).\(ext)"
        try FileManager.default.copyItem(at: src, to: drawingURL(fileName: fileName))
        return fileName
    }

    /// Files owned by an image: the original, its thumbnail, and its attempts' drawings.
    @MainActor static func files(of image: RefImage) -> [URL] {
        [imageURL(fileName: image.fileName), thumbnailURL(id: image.id)]
            + image.attempts.compactMap { $0.drawingFileName.map(drawingURL(fileName:)) }
    }

    static func remove(_ urls: [URL]) {
        for url in urls { try? FileManager.default.removeItem(at: url) }
    }
}
