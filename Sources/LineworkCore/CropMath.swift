import CoreGraphics

/// Crop rectangles are normalized (0–1) with a top-left origin.
public enum CropMath {
    public static let unit = CGRect(x: 0, y: 0, width: 1, height: 1)

    /// Vision uses a bottom-left origin; flip to top-left.
    public static func fromVision(_ r: CGRect) -> CGRect {
        CGRect(x: r.minX, y: 1 - r.minY - r.height, width: r.width, height: r.height)
    }

    public static func union(_ rects: [CGRect]) -> CGRect? {
        guard let first = rects.first else { return nil }
        return rects.dropFirst().reduce(first) { $0.union($1) }
    }

    public static func clampedToUnit(_ r: CGRect) -> CGRect {
        let c = r.standardized.intersection(unit)
        return c.isNull ? unit : c
    }

    public static func padded(_ r: CGRect, fraction: CGFloat = 0.08) -> CGRect {
        clampedToUnit(r.insetBy(dx: -r.width * fraction, dy: -r.height * fraction))
    }

    public static func autoCrop(visionBoxes: [CGRect]) -> CGRect? {
        union(visionBoxes.map(fromVision)).map { padded($0) }
    }

    /// Shrinks `r` around its center so its pixel aspect (w/h) equals `aspect`.
    public static func fitAspect(_ r: CGRect, aspect: CGFloat, imageAspect: CGFloat) -> CGRect {
        let pixelAspect = r.width * imageAspect / r.height
        var w = r.width, h = r.height
        if pixelAspect > aspect {
            w = aspect * h / imageAspect
        } else {
            h = w * imageAspect / aspect
        }
        return CGRect(x: r.midX - w / 2, y: r.midY - h / 2, width: w, height: h)
    }

    public static let minimumCropSize: CGFloat = 0.05

    public static func mirroredX(_ r: CGRect) -> CGRect {
        CGRect(x: 1 - r.minX - r.width, y: r.minY, width: r.width, height: r.height)
    }

    /// Translates `r`, stopping at the unit rect's edges.
    public static func moved(_ r: CGRect, by d: CGSize) -> CGRect {
        CGRect(x: min(max(r.minX + d.width, 0), 1 - r.width),
               y: min(max(r.minY + d.height, 0), 1 - r.height),
               width: r.width, height: r.height)
    }

    /// Drags one handle of `r` by `d`. With `aspect` (pixel w/h) set, the opposite side stays anchored
    /// and the box keeps that aspect, centered on the untouched axis for edge handles.
    public static func resized(_ r: CGRect, handle: CropHandle, by d: CGSize, aspect: CGFloat?,
                               imageAspect: CGFloat, minSize: CGFloat = minimumCropSize) -> CGRect {
        let (hx, hy) = handle.direction
        var minX = r.minX, maxX = r.maxX, minY = r.minY, maxY = r.maxY
        if hx < 0 { minX = min(max(minX + d.width, 0), maxX - minSize) }
        if hx > 0 { maxX = max(min(maxX + d.width, 1), minX + minSize) }
        if hy < 0 { minY = min(max(minY + d.height, 0), maxY - minSize) }
        if hy > 0 { maxY = max(min(maxY + d.height, 1), minY + minSize) }
        guard let aspect else { return CGRect(x: minX, y: minY, width: maxX - minX, height: maxY - minY) }

        let k = aspect / imageAspect  // normalized width / height
        var w = maxX - minX, h = maxY - minY
        if hx != 0 { h = w / k } else { w = h * k }
        let maxW = hx > 0 ? 1 - r.minX : hx < 0 ? r.maxX : 2 * min(r.midX, 1 - r.midX)
        let maxH = hy > 0 ? 1 - r.minY : hy < 0 ? r.maxY : 2 * min(r.midY, 1 - r.midY)
        if w > maxW { w = maxW; h = w / k }
        if h > maxH { h = maxH; w = h * k }
        if w < minSize { w = minSize; h = w / k }
        if h < minSize { h = minSize; w = h * k }
        let x = hx > 0 ? r.minX : hx < 0 ? r.maxX - w : r.midX - w / 2
        let y = hy > 0 ? r.minY : hy < 0 ? r.maxY - h : r.midY - h / 2
        return CGRect(x: x, y: y, width: w, height: h)
    }

    /// Normalized top-left bounds of the set pixels in a mask whose row 0 is the top; nil if none are set.
    public static func maskBounds(width: Int, height: Int, isSet: (_ x: Int, _ y: Int) -> Bool) -> CGRect? {
        var minX = width, minY = height, maxX = -1, maxY = -1
        for y in 0..<height {
            for x in 0..<width where isSet(x, y) {
                minX = min(minX, x); maxX = max(maxX, x)
                minY = min(minY, y); maxY = max(maxY, y)
            }
        }
        guard maxX >= 0 else { return nil }
        let w = CGFloat(width), h = CGFloat(height)
        return CGRect(x: CGFloat(minX) / w, y: CGFloat(minY) / h,
                      width: CGFloat(maxX - minX + 1) / w, height: CGFloat(maxY - minY + 1) / h)
    }

    public static func pixelRect(_ norm: CGRect, imageSize: CGSize) -> CGRect {
        CGRect(x: norm.minX * imageSize.width, y: norm.minY * imageSize.height,
               width: norm.width * imageSize.width, height: norm.height * imageSize.height)
            .integral
    }
}

public enum CropHandle: CaseIterable, Sendable {
    case topLeft, top, topRight, right, bottomRight, bottom, bottomLeft, left

    /// Which edges move: -1 = min edge, +1 = max edge, 0 = neither.
    public var direction: (x: Int, y: Int) {
        switch self {
        case .topLeft: (-1, -1)
        case .top: (0, -1)
        case .topRight: (1, -1)
        case .right: (1, 0)
        case .bottomRight: (1, 1)
        case .bottom: (0, 1)
        case .bottomLeft: (-1, 1)
        case .left: (-1, 0)
        }
    }
}
