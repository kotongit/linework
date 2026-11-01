import CoreGraphics
import CoreImage
import Foundation
import LineworkCore
import ImageIO

enum ImageRendering {
    private static let ciContext = CIContext(options: [.cacheIntermediates: false])

    /// Loads with EXIF orientation applied, downscaled to `maxPixel` on the long edge.
    static func loadCG(url: URL, maxPixel: Int = 3000) -> CGImage? {
        guard let src = CGImageSourceCreateWithURL(url as CFURL, [kCGImageSourceShouldCache: false] as CFDictionary) else { return nil }
        return CGImageSourceCreateThumbnailAtIndex(src, 0, [
            kCGImageSourceCreateThumbnailFromImageAlways: true,
            kCGImageSourceCreateThumbnailWithTransform: true,
            kCGImageSourceShouldCacheImmediately: true,
            kCGImageSourceThumbnailMaxPixelSize: maxPixel,
        ] as CFDictionary)
    }

    /// Rotates clockwise by `rotation` degrees (0/90/180/270), then mirrors horizontally if `flipped`.
    static func oriented(_ img: CGImage, rotation: Int, flipped: Bool) -> CGImage {
        let r = ((rotation % 360) + 360) % 360
        guard r != 0 || flipped else { return img }
        var ci = CIImage(cgImage: img)
        if r != 0 {
            // CIImage's y axis points up, so a clockwise turn is a negative angle.
            ci = ci.transformed(by: CGAffineTransform(rotationAngle: -CGFloat(r) * .pi / 180))
        }
        if flipped {
            ci = ci.transformed(by: CGAffineTransform(scaleX: -1, y: 1))
        }
        ci = ci.transformed(by: CGAffineTransform(translationX: -ci.extent.minX, y: -ci.extent.minY))
        return ciContext.createCGImage(ci, from: ci.extent) ?? img
    }

    static func cropped(_ img: CGImage, rect: CGRect) -> CGImage {
        let px = CropMath.pixelRect(CropMath.clampedToUnit(rect), imageSize: CGSize(width: img.width, height: img.height))
        return img.cropping(to: px) ?? img
    }

    /// Loads, orients and crops. Cropping first in normalized space means only the visible part is kept in memory.
    static func render(url: URL, rotation: Int, flipped: Bool, crop: CGRect, maxPixel: Int) -> CGImage? {
        guard let base = loadCG(url: url, maxPixel: maxPixel) else { return nil }
        return cropped(oriented(base, rotation: rotation, flipped: flipped), rect: crop)
    }
}

/// Process-wide cache of rendered images, so scrolling the grid or reopening a preview doesn't re-decode files.
final class RenderCache: @unchecked Sendable {
    static let shared = RenderCache()
    private let cache = NSCache<NSString, CGImage>()

    private init() {
        cache.totalCostLimit = 256 * 1024 * 1024
    }

    subscript(key: String) -> CGImage? {
        cache.object(forKey: key as NSString)
    }

    func insert(_ image: CGImage, for key: String) {
        cache.setObject(image, forKey: key as NSString, cost: image.bytesPerRow * image.height)
    }

    /// Renders through the cache, off the calling actor.
    @concurrent
    func image(for request: RenderRequest) async -> CGImage? {
        if let hit = self[request.key] { return hit }
        guard let image = ImageRendering.render(url: request.url, rotation: request.rotation, flipped: request.flipped,
                                                crop: request.crop, maxPixel: request.maxPixel)
        else { return nil }
        insert(image, for: request.key)
        return image
    }
}

/// Lets a few decodes run at once, through `RenderCache`. A task cancelled while waiting leaves the line without decoding.
actor DecodeGate {
    /// The Freehand canvas's images.
    static let freehand = DecodeGate(limit: 4)

    private let limit: Int
    private var running = 0
    private var waiting: [(id: UUID, turn: CheckedContinuation<Bool, Never>)] = []

    init(limit: Int) {
        self.limit = limit
    }

    func image(for request: RenderRequest) async -> CGImage? {
        if let hit = RenderCache.shared[request.key] { return hit }
        guard await enter() else { return nil }
        defer { leave() }
        guard !Task.isCancelled else { return nil }
        return await RenderCache.shared.image(for: request)
    }

    private func enter() async -> Bool {
        guard running >= limit else {
            running += 1
            return true
        }
        let id = UUID()
        return await withTaskCancellationHandler {
            await withCheckedContinuation { turn in
                if Task.isCancelled {
                    turn.resume(returning: false)
                } else {
                    waiting.append((id, turn))
                }
            }
        } onCancel: {
            Task { await self.stopWaiting(id) }
        }
    }

    /// Hands the slot straight to the next in line, so `running` stays right.
    private func leave() {
        if waiting.isEmpty {
            running -= 1
        } else {
            waiting.removeFirst().turn.resume(returning: true)
        }
    }

    private func stopWaiting(_ id: UUID) {
        guard let index = waiting.firstIndex(where: { $0.id == id }) else { return }
        waiting.remove(at: index).turn.resume(returning: false)
    }
}

/// Everything needed to render one reference image, captured as plain values so it can leave the main actor.
struct RenderRequest: Sendable, Hashable {
    let url: URL
    let rotation: Int
    let flipped: Bool
    let crop: CGRect
    let maxPixel: Int

    var key: String { "\(url.lastPathComponent)|\(rotation)|\(flipped)|\(crop.debugDescription)|\(maxPixel)" }
}

extension RefImage {
    /// `useThumbnail` renders from the 400px thumbnail file; otherwise from the original. `showCrop: false` shows the whole frame.
    @MainActor
    func renderRequest(maxPixel: Int, useThumbnail: Bool = false, showCrop: Bool = true) -> RenderRequest {
        RenderRequest(url: useThumbnail ? Storage.thumbnailURL(id: id) : Storage.imageURL(fileName: fileName),
                      rotation: rotation, flipped: flipped, crop: showCrop ? cropRect : CropMath.unit, maxPixel: maxPixel)
    }
}
