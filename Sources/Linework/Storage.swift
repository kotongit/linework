import Foundation
import ImageIO
import UniformTypeIdentifiers

enum StorageError: Error {
    case unreadableImage
}

/// What images are, read from their files.
enum ImageFiles {
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
}

/// 400px thumbnails, kept outside the library because they can always be made again.
enum ThumbnailCache {
    private static let directory: URL = {
        let base = FileManager.default.urls(for: .cachesDirectory, in: .userDomainMask)[0]
        let url = base.appending(path: "com.hyprsea.linework/Thumbnails", directoryHint: .isDirectory)
        try? FileManager.default.createDirectory(at: url, withIntermediateDirectories: true)
        return url
    }()

    static func url(forHash hash: String) -> URL { directory.appending(path: "\(hash).jpg") }

    /// Makes the thumbnail if it isn't there. A failure just means the next render tries again.
    static func ensure(hash: String, source: URL) {
        let target = url(forHash: hash)
        guard !FileManager.default.fileExists(atPath: target.path),
              let src = CGImageSourceCreateWithURL(source as CFURL, nil),
              let thumb = CGImageSourceCreateThumbnailAtIndex(src, 0, [
                  kCGImageSourceCreateThumbnailFromImageAlways: true,
                  kCGImageSourceCreateThumbnailWithTransform: true,
                  kCGImageSourceThumbnailMaxPixelSize: 400,
              ] as CFDictionary),
              let dest = CGImageDestinationCreateWithURL(target as CFURL, UTType.jpeg.identifier as CFString, 1, nil)
        else { return }
        CGImageDestinationAddImage(dest, thumb, [kCGImageDestinationLossyCompressionQuality: 0.85] as CFDictionary)
        CGImageDestinationFinalize(dest)
    }
}
