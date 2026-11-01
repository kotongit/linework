import CoreGraphics
import Testing
@testable import LineworkCore

private func near(_ a: CGRect, _ b: CGRect) -> Bool {
    abs(a.minX - b.minX) < 1e-9 && abs(a.minY - b.minY) < 1e-9 && abs(a.width - b.width) < 1e-9 && abs(a.height - b.height) < 1e-9
}

@Suite struct CropEditingTests {
    let box = CGRect(x: 0.2, y: 0.2, width: 0.4, height: 0.4)

    @Test func moveStaysInside() {
        let r = CropMath.moved(CGRect(x: 0.5, y: 0.5, width: 0.4, height: 0.4), by: CGSize(width: 0.3, height: -0.7))
        #expect(near(r, CGRect(x: 0.6, y: 0, width: 0.4, height: 0.4)))
    }

    @Test func resizeCornerFree() {
        let r = CropMath.resized(box, handle: .bottomRight, by: CGSize(width: 0.1, height: 0.2), aspect: nil, imageAspect: 1)
        #expect(near(r, CGRect(x: 0.2, y: 0.2, width: 0.5, height: 0.6)))
    }

    @Test func resizeKeepsMinimumSize() {
        let r = CropMath.resized(box, handle: .topLeft, by: CGSize(width: 0.9, height: 0.9), aspect: nil, imageAspect: 1)
        #expect(near(r, CGRect(x: 0.55, y: 0.55, width: 0.05, height: 0.05)))
    }

    @Test func resizeClampsToUnit() {
        let r = CropMath.resized(box, handle: .right, by: CGSize(width: 1, height: 0), aspect: nil, imageAspect: 1)
        #expect(near(r, CGRect(x: 0.2, y: 0.2, width: 0.8, height: 0.4)))
    }

    @Test func resizeAspectLocked() {
        let r = CropMath.resized(CGRect(x: 0, y: 0, width: 0.5, height: 0.5), handle: .bottomRight,
                                 by: CGSize(width: 0.1, height: 0), aspect: 1, imageAspect: 1)
        #expect(near(r, CGRect(x: 0, y: 0, width: 0.6, height: 0.6)))
        // A 2:3 lock on a square image keeps the pixel aspect.
        let t = CropMath.resized(CGRect(x: 0, y: 0, width: 0.4, height: 0.6), handle: .bottomRight,
                                 by: CGSize(width: 0.2, height: 0), aspect: 2.0 / 3.0, imageAspect: 1)
        #expect(abs(t.width / t.height - 2.0 / 3.0) < 1e-9)
        #expect(t.maxY <= 1 + 1e-9)
    }

    @Test func mirroredX() {
        #expect(near(CropMath.mirroredX(CGRect(x: 0.1, y: 0.2, width: 0.3, height: 0.4)), CGRect(x: 0.6, y: 0.2, width: 0.3, height: 0.4)))
    }
}
