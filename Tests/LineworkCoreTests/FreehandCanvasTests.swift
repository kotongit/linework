import CoreGraphics
import Foundation
import Testing
@testable import LineworkCore

private func near(_ a: CGPoint, _ b: CGPoint, tol: CGFloat = 1e-6) -> Bool {
    abs(a.x - b.x) < tol && abs(a.y - b.y) < tol
}

private func item(size: CGSize, center: CGPoint = .zero, rotation: Double = 0,
                  crop: CGRect = CropMath.unit) -> FreehandItem {
    FreehandItem(id: UUID(), fileName: "a.png", center: center, fullSize: size,
                 rotation: rotation, flipped: false, crop: crop)
}

@Suite struct FreehandCanvasTests {
    @Test func screenCanvasRoundTrip() {
        for zoom in [0.1, 1, 8] {
            for offset in [CGPoint.zero, CGPoint(x: -300, y: 120)] {
                let c = FreehandCamera(offset: offset, zoom: zoom)
                let p = CGPoint(x: 123.5, y: -77.25)
                #expect(near(c.toCanvas(c.toScreen(p)), p))
            }
        }
    }

    @Test func zoomKeepsFocusFixed() {
        let c = FreehandCamera(offset: CGPoint(x: -300, y: 120), zoom: 1.3)
        let f = CGPoint(x: 400, y: 250)
        #expect(near(c.zoomed(by: 1.7, around: f).toCanvas(f), c.toCanvas(f)))
    }

    @Test func zoomClamps() {
        let c = FreehandCamera(offset: CGPoint(x: 10, y: 20), zoom: 1)
        let f = CGPoint(x: 400, y: 250)
        let up = c.zoomed(by: 100, around: f)
        let down = c.zoomed(by: 0.001, around: f)
        #expect(up.zoom == 8)
        #expect(down.zoom == 0.1)
        #expect(near(up.toCanvas(f), c.toCanvas(f)))
        #expect(near(down.toCanvas(f), c.toCanvas(f)))
    }

    @Test func panMovesOffset() {
        let c = FreehandCamera.home.panned(by: CGSize(width: 5, height: -3))
        #expect(c.offset == CGPoint(x: 5, y: -3))
    }

    @Test func visibleSizeFollowsCrop() {
        let i = item(size: CGSize(width: 400, height: 200),
                     crop: CGRect(x: 0, y: 0, width: 0.5, height: 0.5))
        #expect(i.visibleSize == CGSize(width: 200, height: 100))
    }

    @Test func fitEmptyIsHome() {
        #expect(FreehandGeometry.fitted([], in: CGSize(width: 1000, height: 800)) == .home)
    }

    @Test func fitNeverOver100() {
        let v = CGSize(width: 1000, height: 800)
        let c = FreehandGeometry.fitted([item(size: CGSize(width: 10, height: 10))], in: v)
        #expect(c.zoom == 1)
        #expect(near(c.toScreen(.zero), CGPoint(x: 500, y: 400)))
    }

    @Test func fitUsesMargin() {
        let v = CGSize(width: 1000, height: 800)
        let c = FreehandGeometry.fitted([item(size: CGSize(width: 2000, height: 1000))], in: v)
        #expect(abs(c.zoom - 0.46) < 1e-9)
        #expect(near(c.toScreen(.zero), CGPoint(x: 500, y: 400)))
    }

    @Test func fitCountsRotation() {
        let v = CGSize(width: 1000, height: 800)
        let c = FreehandGeometry.fitted(
            [item(size: CGSize(width: 1000, height: 100), rotation: 90)], in: v)
        #expect(abs(c.zoom - 0.72) < 1e-9)
    }

    @Test func cornersRotateClockwise() {
        let i = item(size: CGSize(width: 100, height: 100), rotation: 90)
        let corners = FreehandGeometry.corners(of: i)
        #expect(corners.count == 4)
        #expect(near(corners[0], CGPoint(x: 50, y: -50)))
    }
}
