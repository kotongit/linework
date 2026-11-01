import CoreGraphics
import Foundation

/// One image placed on the Freehand canvas.
public struct FreehandItem: Codable, Equatable, Identifiable, Sendable {
    public var id: UUID
    public var fileName: String
    /// Center of the visible (cropped) rect, in canvas points.
    public var center: CGPoint
    /// Uncropped image size, in canvas points.
    public var fullSize: CGSize
    /// Degrees, clockwise.
    public var rotation: Double
    public var flipped: Bool
    /// Normalized, top-left origin; `CropMath.unit` when whole.
    public var crop: CGRect

    public init(id: UUID = UUID(), fileName: String, center: CGPoint, fullSize: CGSize,
                rotation: Double = 0, flipped: Bool = false, crop: CGRect = CropMath.unit) {
        self.id = id
        self.fileName = fileName
        self.center = center
        self.fullSize = fullSize
        self.rotation = rotation
        self.flipped = flipped
        self.crop = crop
    }

    public var visibleSize: CGSize {
        CGSize(width: fullSize.width * crop.width, height: fullSize.height * crop.height)
    }

    /// The crop as it looks on the canvas, where a flipped image is mirrored. `crop` itself stays in unflipped image space.
    public var shownCrop: CGRect {
        flipped ? CropMath.mirroredX(crop) : crop
    }

    /// The value for `crop`, from a crop framed on the image as it looks on the canvas.
    public func storedCrop(fromShown shown: CGRect) -> CGRect {
        flipped ? CropMath.mirroredX(shown) : shown
    }
}

/// Pan and zoom: `screen = canvas * zoom + offset`.
public struct FreehandCamera: Codable, Equatable, Sendable {
    public var offset: CGPoint
    public var zoom: Double

    public static let zoomRange: ClosedRange<Double> = 0.1...8
    public static let home = FreehandCamera(offset: .zero, zoom: 1)

    public init(offset: CGPoint, zoom: Double) {
        self.offset = offset
        self.zoom = zoom
    }

    public func toScreen(_ p: CGPoint) -> CGPoint {
        CGPoint(x: p.x * zoom + offset.x, y: p.y * zoom + offset.y)
    }

    public func toCanvas(_ p: CGPoint) -> CGPoint {
        CGPoint(x: (p.x - offset.x) / zoom, y: (p.y - offset.y) / zoom)
    }

    /// Zooms by `factor`, clamped to `zoomRange`, keeping the canvas point under `screenPoint` fixed.
    public func zoomed(by factor: Double, around screenPoint: CGPoint) -> FreehandCamera {
        let target = min(max(zoom * factor, Self.zoomRange.lowerBound), Self.zoomRange.upperBound)
        let ratio = target / zoom
        return FreehandCamera(
            offset: CGPoint(x: screenPoint.x - (screenPoint.x - offset.x) * ratio,
                            y: screenPoint.y - (screenPoint.y - offset.y) * ratio),
            zoom: target)
    }

    public func panned(by delta: CGSize) -> FreehandCamera {
        FreehandCamera(offset: CGPoint(x: offset.x + delta.width, y: offset.y + delta.height),
                       zoom: zoom)
    }
}

public struct FreehandDocument: Codable, Equatable, Sendable {
    public static let currentVersion = 1

    public var version: Int
    public var greyscale: Bool
    public var camera: FreehandCamera
    /// Back to front.
    public var items: [FreehandItem]

    public init(version: Int = FreehandDocument.currentVersion, greyscale: Bool = false,
                camera: FreehandCamera = .home, items: [FreehandItem] = []) {
        self.version = version
        self.greyscale = greyscale
        self.camera = camera
        self.items = items
    }

    public static let empty = FreehandDocument()
}

public enum FreehandGeometry {
    static let fitMargin: Double = 40

    /// Camera that shows every item with a 40pt margin, never zoomed past 100%.
    public static func fitted(_ items: [FreehandItem], in viewport: CGSize) -> FreehandCamera {
        let points = items.flatMap(corners(of:))
        guard let first = points.first else { return .home }
        var minX = first.x, maxX = first.x, minY = first.y, maxY = first.y
        for p in points {
            minX = min(minX, p.x); maxX = max(maxX, p.x)
            minY = min(minY, p.y); maxY = max(maxY, p.y)
        }
        let w = max(maxX - minX, 1e-6)
        let h = max(maxY - minY, 1e-6)
        let raw = min((viewport.width - 2 * fitMargin) / w,
                      (viewport.height - 2 * fitMargin) / h, 1)
        let zoom = min(max(raw, FreehandCamera.zoomRange.lowerBound),
                       FreehandCamera.zoomRange.upperBound)
        let mid = CGPoint(x: (minX + maxX) / 2, y: (minY + maxY) / 2)
        return FreehandCamera(
            offset: CGPoint(x: viewport.width / 2 - mid.x * zoom,
                            y: viewport.height / 2 - mid.y * zoom),
            zoom: zoom)
    }

    /// Visible-rect corners (TL, TR, BR, BL) rotated clockwise about the item center, in canvas space.
    public static func corners(of item: FreehandItem) -> [CGPoint] {
        let hw = item.visibleSize.width / 2
        let hh = item.visibleSize.height / 2
        let theta = item.rotation * .pi / 180
        let c = cos(theta), s = sin(theta)
        return [(-hw, -hh), (hw, -hh), (hw, hh), (-hw, hh)].map { x, y in
            CGPoint(x: item.center.x + x * c - y * s, y: item.center.y + x * s + y * c)
        }
    }

    // MARK: - Decode size

    public static let standardDecodePixels = 1600
    public static let sharpDecodePixels = 3000
    static let sharpDecodeThreshold: Double = 1200

    /// Bounding box of the item's visible rect, rotation included, in screen points.
    public static func screenBounds(of item: FreehandItem, camera: FreehandCamera) -> CGRect {
        let points = corners(of: item).map(camera.toScreen)
        let xs = points.map(\.x), ys = points.map(\.y)
        let minX = xs.min() ?? 0, minY = ys.min() ?? 0
        return CGRect(x: minX, y: minY, width: (xs.max() ?? 0) - minX, height: (ys.max() ?? 0) - minY)
    }

    public static func isOnScreen(_ item: FreehandItem, camera: FreehandCamera, viewport: CGSize) -> Bool {
        screenBounds(of: item, camera: camera).intersects(CGRect(origin: .zero, size: viewport))
    }

    /// How large to decode an item. The whole image is decoded and then cropped, so a tight crop needs the
    /// full image's size on screen. The sharp size is for what can be seen: it costs too much to keep for the rest.
    public static func decodePixels(for item: FreehandItem, camera: FreehandCamera, viewport: CGSize) -> Int {
        let fullLongEdge = max(item.fullSize.width, item.fullSize.height) * camera.zoom
        return fullLongEdge > sharpDecodeThreshold && isOnScreen(item, camera: camera, viewport: viewport)
            ? sharpDecodePixels : standardDecodePixels
    }

    // MARK: - Item edits

    public enum Corner: Int, CaseIterable, Sendable { case topLeft, topRight, bottomRight, bottomLeft }

    public static let minimumLongEdge: Double = 32
    static let rotateHandleOffset: Double = 28

    private static func rotate(_ v: CGPoint, degrees: Double) -> CGPoint {
        let t = degrees * .pi / 180
        let c = cos(t), s = sin(t)
        return CGPoint(x: v.x * c - v.y * s, y: v.x * s + v.y * c)
    }

    /// Topmost item whose visible rect contains the point (items are back to front).
    public static func hit(_ items: [FreehandItem], at canvasPoint: CGPoint) -> FreehandItem.ID? {
        for item in items.reversed() {
            let local = rotate(CGPoint(x: canvasPoint.x - item.center.x, y: canvasPoint.y - item.center.y),
                               degrees: -item.rotation)
            if abs(local.x) <= item.visibleSize.width / 2 && abs(local.y) <= item.visibleSize.height / 2 {
                return item.id
            }
        }
        return nil
    }

    public static func broughtToFront(_ items: [FreehandItem], id: FreehandItem.ID) -> [FreehandItem] {
        guard let moved = items.first(where: { $0.id == id }) else { return items }
        return items.filter { $0.id != id } + [moved]
    }

    /// Scales proportionally by dragging `corner` to `canvasPoint`; the opposite corner stays fixed.
    public static func resized(_ item: FreehandItem, corner: Corner, to canvasPoint: CGPoint) -> FreehandItem {
        let hw = item.visibleSize.width / 2
        let hh = item.visibleSize.height / 2
        let signs: [(Double, Double)] = [(-1, -1), (1, -1), (1, 1), (-1, 1)]
        let (sx, sy) = signs[corner.rawValue]
        let dragged = CGPoint(x: sx * hw, y: sy * hh)             // local, relative to center
        let opposite = CGPoint(x: -dragged.x, y: -dragged.y)
        let oppositeCanvas = CGPoint(x: item.center.x + rotate(opposite, degrees: item.rotation).x,
                                     y: item.center.y + rotate(opposite, degrees: item.rotation).y)
        let pointer = rotate(CGPoint(x: canvasPoint.x - oppositeCanvas.x, y: canvasPoint.y - oppositeCanvas.y),
                             degrees: -item.rotation)
        let diagonal = CGPoint(x: 2 * dragged.x, y: 2 * dragged.y)
        let lengthSquared = diagonal.x * diagonal.x + diagonal.y * diagonal.y
        guard lengthSquared > 0 else { return item }
        let projected = (pointer.x * diagonal.x + pointer.y * diagonal.y) / lengthSquared
        let longEdge = max(item.visibleSize.width, item.visibleSize.height)
        let scale = max(projected, minimumLongEdge / longEdge)

        var result = item
        result.fullSize = CGSize(width: item.fullSize.width * scale, height: item.fullSize.height * scale)
        let half = rotate(CGPoint(x: diagonal.x * scale / 2, y: diagonal.y * scale / 2), degrees: item.rotation)
        result.center = CGPoint(x: oppositeCanvas.x + half.x, y: oppositeCanvas.y + half.y)
        return result
    }

    /// Points the item's up direction at the pointer; snaps to 15 degree steps when `snapping`.
    public static func rotated(_ item: FreehandItem, pointer: CGPoint, snapping: Bool) -> FreehandItem {
        var degrees = atan2(pointer.y - item.center.y, pointer.x - item.center.x) * 180 / .pi + 90
        if snapping { degrees = (degrees / 15).rounded() * 15 }
        degrees = degrees.truncatingRemainder(dividingBy: 360)
        if degrees < 0 { degrees += 360 }
        var result = item
        result.rotation = degrees
        return result
    }

    public static func flipped(_ item: FreehandItem) -> FreehandItem {
        var result = item
        result.flipped.toggle()
        return result
    }

    /// Changes the crop while keeping the image content where it is on the canvas.
    public static func cropped(_ item: FreehandItem, to crop: CGRect) -> FreehandItem {
        func offset(_ c: CGRect) -> CGPoint {
            let x = (c.midX - 0.5) * item.fullSize.width
            return CGPoint(x: item.flipped ? -x : x, y: (c.midY - 0.5) * item.fullSize.height)
        }
        let old = offset(item.crop), new = offset(crop)
        let shift = rotate(CGPoint(x: new.x - old.x, y: new.y - old.y), degrees: item.rotation)
        var result = item
        result.crop = crop
        result.center = CGPoint(x: item.center.x + shift.x, y: item.center.y + shift.y)
        return result
    }

    /// Canvas point 28 screen points above the visible top edge's midpoint, along the item's up direction.
    public static func rotateHandle(of item: FreehandItem, zoom: Double) -> CGPoint {
        let local = CGPoint(x: 0, y: -(item.visibleSize.height / 2 + rotateHandleOffset / zoom))
        let r = rotate(local, degrees: item.rotation)
        return CGPoint(x: item.center.x + r.x, y: item.center.y + r.y)
    }
}
