import CoreGraphics
import Testing
@testable import LineworkCore

private func approx(_ a: CGRect, _ b: CGRect, tol: CGFloat = 1e-9) -> Bool {
    abs(a.minX - b.minX) < tol && abs(a.minY - b.minY) < tol
        && abs(a.width - b.width) < tol && abs(a.height - b.height) < tol
}

@Suite struct CropMathTests {
    @Test func fromVisionFlipsY() {
        let r = CropMath.fromVision(CGRect(x: 0.1, y: 0.2, width: 0.3, height: 0.4))
        #expect(approx(r, CGRect(x: 0.1, y: 0.4, width: 0.3, height: 0.4)))
    }

    @Test func unionOfTwo() {
        let u = CropMath.union([CGRect(x: 0, y: 0, width: 0.2, height: 0.2),
                                CGRect(x: 0.5, y: 0.5, width: 0.1, height: 0.1)])
        #expect(u.map { approx($0, CGRect(x: 0, y: 0, width: 0.6, height: 0.6)) } == true)
        #expect(CropMath.union([]) == nil)
    }

    @Test func paddedGrowsAndClamps() {
        let p = CropMath.padded(CGRect(x: 0.4, y: 0.4, width: 0.2, height: 0.2))
        #expect(approx(p, CGRect(x: 0.384, y: 0.384, width: 0.232, height: 0.232)))
        #expect(approx(CropMath.padded(CropMath.unit), CropMath.unit))
    }

    @Test func autoCropNilForNoBoxes() {
        #expect(CropMath.autoCrop(visionBoxes: []) == nil)
    }

    @Test func fitAspect() {
        let r = CropMath.fitAspect(CropMath.unit, aspect: 2.0 / 3.0, imageAspect: 1.0)
        #expect(approx(r, CGRect(x: 1.0 / 6.0, y: 0, width: 2.0 / 3.0, height: 1)))
    }

    @Test func pixelRect() {
        let r = CropMath.pixelRect(CGRect(x: 0.25, y: 0.5, width: 0.5, height: 0.25),
                                   imageSize: CGSize(width: 400, height: 200))
        #expect(r == CGRect(x: 100, y: 100, width: 200, height: 50))
    }
}

@Suite struct MaskBoundsTests {
    @Test func boundsOfSetPixels() {
        // 10×5 mask with pixels set in columns 2…4, rows 1…3 (row 0 = top)
        let r = CropMath.maskBounds(width: 10, height: 5) { x, y in (2...4).contains(x) && (1...3).contains(y) }
        #expect(r == CGRect(x: 0.2, y: 0.2, width: 0.3, height: 0.6))
    }

    @Test func emptyMaskIsNil() {
        #expect(CropMath.maskBounds(width: 4, height: 4) { _, _ in false } == nil)
    }
}
