import CoreGraphics
import Foundation
import Testing
@testable import LineworkCore

private func near(_ a: CGPoint, _ b: CGPoint, tol: CGFloat = 0.001) -> Bool {
    abs(a.x - b.x) < tol && abs(a.y - b.y) < tol
}

private func make(size: CGSize, center: CGPoint = .zero, rotation: Double = 0,
                  flipped: Bool = false, crop: CGRect = CropMath.unit) -> FreehandItem {
    FreehandItem(id: UUID(), fileName: "a.png", center: center, fullSize: size,
                 rotation: rotation, flipped: flipped, crop: crop)
}

private func rot(_ v: CGPoint, _ degrees: Double) -> CGPoint {
    let t = degrees * .pi / 180
    return CGPoint(x: v.x * cos(t) - v.y * sin(t), y: v.x * sin(t) + v.y * cos(t))
}

@Suite struct FreehandEditTests {
    @Test func hitPicksTopmost() {
        let a = make(size: CGSize(width: 100, height: 100))
        let b = make(size: CGSize(width: 100, height: 100), center: CGPoint(x: 20, y: 0))
        #expect(FreehandGeometry.hit([a, b], at: CGPoint(x: 10, y: 0)) == b.id)
        #expect(FreehandGeometry.hit([b, a], at: CGPoint(x: 10, y: 0)) == a.id)
        #expect(FreehandGeometry.hit([a, b], at: CGPoint(x: 500, y: 0)) == nil)
    }

    @Test func hitMissesRotatedCorner() {
        let a = make(size: CGSize(width: 200, height: 20), rotation: 45)
        #expect(FreehandGeometry.hit([a], at: CGPoint(x: 63.6, y: 63.6)) == a.id)
        #expect(FreehandGeometry.hit([a], at: CGPoint(x: 90, y: 0)) == nil)
    }

    @Test func hitUsesVisibleCrop() {
        let a = make(size: CGSize(width: 100, height: 100),
                     crop: CGRect(x: 0, y: 0, width: 0.5, height: 1))
        // visible 50x100 centered at origin
        #expect(FreehandGeometry.hit([a], at: CGPoint(x: 20, y: 0)) == a.id)
        #expect(FreehandGeometry.hit([a], at: CGPoint(x: 40, y: 0)) == nil)
    }

    @Test func frontKeepsOthersOrder() {
        let s = CGSize(width: 10, height: 10)
        let a = make(size: s), b = make(size: s), c = make(size: s)
        #expect(FreehandGeometry.broughtToFront([a, b, c], id: a.id).map(\.id) == [b.id, c.id, a.id])
    }

    @Test func resizeKeepsProportions() {
        for rotation in [0.0, 37, 180] {
            for corner in FreehandGeometry.Corner.allCases {
                let it = make(size: CGSize(width: 300, height: 200), center: CGPoint(x: 50, y: -30),
                              rotation: rotation, crop: CGRect(x: 0.1, y: 0.2, width: 0.6, height: 0.5))
                let pts = FreehandGeometry.corners(of: it)
                let target = pts[corner.rawValue]
                let opp = pts[(corner.rawValue + 2) % 4]
                // drag outward by 40% along the diagonal, plus a perpendicular wiggle
                let dir = CGPoint(x: target.x - opp.x, y: target.y - opp.y)
                let p = CGPoint(x: opp.x + dir.x * 1.4 + dir.y * 0.05, y: opp.y + dir.y * 1.4 - dir.x * 0.05)
                let r = FreehandGeometry.resized(it, corner: corner, to: p)
                let aspect0 = it.visibleSize.width / it.visibleSize.height
                #expect(abs(r.visibleSize.width / r.visibleSize.height - aspect0) < 1e-9)
                #expect(r.visibleSize.width > it.visibleSize.width)
                let newOpp = FreehandGeometry.corners(of: r)[(corner.rawValue + 2) % 4]
                #expect(near(newOpp, opp))
                #expect(r.crop == it.crop && r.rotation == it.rotation)
            }
        }
    }

    @Test func resizeKeepsOppositeCorner() {
        for rotation in [0.0, 37, 180] {
            for corner in FreehandGeometry.Corner.allCases {
                let it = make(size: CGSize(width: 160, height: 120), center: CGPoint(x: -10, y: 70), rotation: rotation)
                let opp = FreehandGeometry.corners(of: it)[(corner.rawValue + 2) % 4]
                let moved = CGPoint(x: opp.x + 33, y: opp.y - 12)
                let r = FreehandGeometry.resized(it, corner: corner, to: moved)
                #expect(near(FreehandGeometry.corners(of: r)[(corner.rawValue + 2) % 4], opp))
            }
        }
    }

    @Test func resizeHonoursMinimum() {
        let it = make(size: CGSize(width: 200, height: 100))
        let opp = FreehandGeometry.corners(of: it)[0]
        // drag the bottom-right corner past the top-left opposite corner
        let r = FreehandGeometry.resized(it, corner: .bottomRight, to: CGPoint(x: opp.x - 300, y: opp.y - 300))
        #expect(abs(max(r.visibleSize.width, r.visibleSize.height) - 32) < 1e-9)
        #expect(near(FreehandGeometry.corners(of: r)[0], opp))
    }

    @Test func rotateFreeAndSnap() {
        let it = make(size: CGSize(width: 100, height: 100), center: CGPoint(x: 10, y: 10))
        func pointer(bearing: Double) -> CGPoint {
            let t = bearing * .pi / 180
            return CGPoint(x: 10 + 50 * sin(t), y: 10 - 50 * cos(t))
        }
        #expect(abs(FreehandGeometry.rotated(it, pointer: CGPoint(x: 100, y: 10), snapping: false).rotation - 90) < 1e-9)
        #expect(abs(FreehandGeometry.rotated(it, pointer: pointer(bearing: 97), snapping: false).rotation - 97) < 1e-6)
        #expect(abs(FreehandGeometry.rotated(it, pointer: pointer(bearing: 97), snapping: true).rotation - 90) < 1e-9)
        #expect(abs(FreehandGeometry.rotated(it, pointer: pointer(bearing: 98), snapping: true).rotation - 105) < 1e-9)
        let up = FreehandGeometry.rotated(it, pointer: pointer(bearing: 0), snapping: false).rotation
        #expect(up >= 0 && up < 360 && abs(up) < 1e-6)
        let left = FreehandGeometry.rotated(it, pointer: pointer(bearing: 270), snapping: false).rotation
        #expect(abs(left - 270) < 1e-6)
    }

    @Test func flipKeepsCenter() {
        let it = make(size: CGSize(width: 100, height: 60), center: CGPoint(x: 5, y: 6), rotation: 33)
        let f = FreehandGeometry.flipped(it)
        #expect(f.center == it.center && f.rotation == it.rotation && f.flipped == true)
        #expect(FreehandGeometry.flipped(f).flipped == false)
    }

    @Test func cropKeepsContentInPlace() {
        let region = CGRect(x: 0.2, y: 0.1, width: 0.5, height: 0.6)
        func contentPoint(_ it: FreehandItem) -> CGPoint {
            // canvas position of the full image's center-relative point at the kept region's center
            let w = it.fullSize.width, h = it.fullSize.height
            // image-space point P (relative to image center) mapped to canvas for this item:
            // visible center sits at `center`; image center is offset by -off(crop) (in flipped/rotated frame)
            var off = CGPoint(x: (it.crop.midX - 0.5) * w, y: (it.crop.midY - 0.5) * h)
            if it.flipped { off.x = -off.x }
            let r = rot(off, it.rotation)
            return CGPoint(x: it.center.x - r.x, y: it.center.y - r.y) // canvas position of image center
        }
        for rotation in [0.0, 90, 37] {
            for flipped in [false, true] {
                let it = make(size: CGSize(width: 300, height: 200), center: CGPoint(x: 40, y: -20),
                              rotation: rotation, flipped: flipped)
                let c = FreehandGeometry.cropped(it, to: region)
                #expect(c.fullSize == it.fullSize && c.crop == region)
                #expect(near(contentPoint(c), contentPoint(it)))
                let back = FreehandGeometry.cropped(c, to: CropMath.unit)
                #expect(near(back.center, it.center))
                #expect(back.crop == CropMath.unit)
            }
        }
    }

    @Test func rotateHandleSitsAboveTopEdge() {
        let it = make(size: CGSize(width: 100, height: 60), center: CGPoint(x: 10, y: 20), rotation: 90)
        // rotated 90 cw: local up (0,-1) points to canvas +x
        let h = FreehandGeometry.rotateHandle(of: it, zoom: 2)
        #expect(near(h, CGPoint(x: 10 + 30 + 14, y: 20)))
    }

    // MARK: - Decode size

    private static let viewport = CGSize(width: 1000, height: 700)

    @Test func onScreenFollowsTheCamera() {
        let item = make(size: CGSize(width: 100, height: 100), center: CGPoint(x: 500, y: 350))
        let visible = FreehandCamera.home
        #expect(FreehandGeometry.isOnScreen(item, camera: visible, viewport: Self.viewport))
        #expect(!FreehandGeometry.isOnScreen(item, camera: visible.panned(by: CGSize(width: 2000, height: 0)), viewport: Self.viewport))
        #expect(!FreehandGeometry.isOnScreen(item, camera: visible.panned(by: CGSize(width: 0, height: -2000)), viewport: Self.viewport))
        // Zooming in around a far point carries it out of the window.
        #expect(!FreehandGeometry.isOnScreen(item, camera: visible.zoomed(by: 8, around: .zero), viewport: Self.viewport))
    }

    @Test func onScreenEdgesAndViewportCases() {
        let item = make(size: CGSize(width: 100, height: 100), center: CGPoint(x: -49, y: 350))
        #expect(FreehandGeometry.isOnScreen(item, camera: .home, viewport: Self.viewport))
        var outside = item
        outside.center.x = -51
        #expect(!FreehandGeometry.isOnScreen(outside, camera: .home, viewport: Self.viewport))
        #expect(!FreehandGeometry.isOnScreen(item, camera: .home, viewport: .zero))
    }

    @Test func screenBoundsIncludeRotationAndCrop() {
        // 45 degrees: a 100pt square reaches 70.7pt from its center.
        let turned = make(size: CGSize(width: 100, height: 100), center: CGPoint(x: -60, y: 350), rotation: 45)
        #expect(FreehandGeometry.isOnScreen(turned, camera: .home, viewport: Self.viewport))
        var square = turned
        square.rotation = 0
        #expect(!FreehandGeometry.isOnScreen(square, camera: .home, viewport: Self.viewport))
        // Only the visible part counts: the full image would reach the window, the crop doesn't.
        let cropped = make(size: CGSize(width: 400, height: 400), center: CGPoint(x: -60, y: 350),
                           crop: CGRect(x: 0, y: 0, width: 0.1, height: 0.1))
        let bounds = FreehandGeometry.screenBounds(of: cropped, camera: .home)
        #expect(abs(bounds.width - 40) < 0.001 && abs(bounds.midX + 60) < 0.001)
        #expect(!FreehandGeometry.isOnScreen(cropped, camera: .home, viewport: Self.viewport))
    }

    @Test func sharpDecodeStartsPast1200OnScreen() {
        let item = make(size: CGSize(width: 400, height: 300), center: CGPoint(x: 500, y: 350))
        func pixels(_ zoom: Double) -> Int {
            FreehandGeometry.decodePixels(for: item, camera: FreehandCamera(offset: CGPoint(x: 500 - 500 * zoom, y: 350 - 350 * zoom), zoom: zoom),
                                          viewport: Self.viewport)
        }
        #expect(pixels(1) == 1600)
        #expect(pixels(3) == 1600)      // exactly 1200
        #expect(pixels(3.01) == 3000)
    }

    @Test func tightCropIsJudgedByTheFullImage() {
        // Visible long edge 200pt on screen, but the whole image is 2000pt: the decode is cropped from it.
        let tight = make(size: CGSize(width: 2000, height: 1000), center: CGPoint(x: 500, y: 350),
                         crop: CGRect(x: 0.4, y: 0.4, width: 0.1, height: 0.1))
        #expect(FreehandGeometry.decodePixels(for: tight, camera: .home, viewport: Self.viewport) == 3000)
    }

    @Test func offScreenImagesKeepTheStandardDecode() {
        let item = make(size: CGSize(width: 2000, height: 1000), center: CGPoint(x: 500, y: 350))
        #expect(FreehandGeometry.decodePixels(for: item, camera: .home, viewport: Self.viewport) == 3000)
        let away = FreehandCamera.home.panned(by: CGSize(width: 5000, height: 0))
        #expect(FreehandGeometry.decodePixels(for: item, camera: away, viewport: Self.viewport) == 1600)
    }

    // MARK: - Crop as shown

    @Test func shownCropMirrorsOnlyFlippedItems() {
        let rect = CGRect(x: 0.1, y: 0.2, width: 0.3, height: 0.4)
        let plain = make(size: CGSize(width: 100, height: 100), crop: rect)
        #expect(plain.shownCrop == rect)
        #expect(plain.storedCrop(fromShown: rect) == rect)
        let flipped = make(size: CGSize(width: 100, height: 100), flipped: true, crop: rect)
        #expect(abs(flipped.shownCrop.minX - 0.6) < 1e-9 && flipped.shownCrop.minY == rect.minY)
        let back = flipped.storedCrop(fromShown: flipped.shownCrop)
        #expect(abs(back.minX - rect.minX) < 1e-9 && back.width == rect.width)
    }

    @Test func cropFramedOnAFlippedImageKeepsTheKeptPartInPlace() {
        // The canvas shows the image mirrored and turned. Framing the right part as seen must keep exactly
        // what was on the right, where it was.
        let whole = make(size: CGSize(width: 300, height: 200), center: CGPoint(x: 50, y: 80), rotation: 30, flipped: true)
        let shown = CGRect(x: 0.5, y: 0.25, width: 0.5, height: 0.5)
        let result = FreehandGeometry.cropped(whole, to: whole.storedCrop(fromShown: shown))
        #expect(abs(result.crop.minX) < 1e-9)   // unflipped space: the seen right part is the file's left part
        let expected = rot(CGPoint(x: 0.25 * 300, y: 0), 30)
        #expect(near(result.center, CGPoint(x: whole.center.x + expected.x, y: whole.center.y + expected.y)))
        #expect(abs(result.visibleSize.width - 150) < 1e-9)
        #expect(result.fullSize == whole.fullSize)
    }
}
