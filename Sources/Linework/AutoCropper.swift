import CoreGraphics
import CoreVideo
import LineworkCore
import Vision

enum AutoCropper {
    /// Finds the figure(s) in an already-oriented image. Falls back to the foreground mask, then to the full frame.
    static func crop(for image: CGImage) async -> (rect: CGRect, found: Bool) {
        var humans = DetectHumanRectanglesRequest()
        humans.upperBodyOnly = false
        if let observations = try? await humans.perform(on: image),
           let rect = CropMath.autoCrop(visionBoxes: observations.map(\.boundingBox.cgRect)) {
            return (rect, true)
        }

        if let mask = try? await GenerateForegroundInstanceMaskRequest().perform(on: image),
           let buffer = try? mask.generateMask(for: mask.allInstances),
           let bounds = maskBounds(buffer) {
            return (CropMath.padded(bounds), true)
        }
        return (CropMath.unit, false)
    }

    /// Bounds of non-zero mask pixels; pixel buffer row 0 is the top of the image.
    private static func maskBounds(_ buffer: CVPixelBuffer) -> CGRect? {
        CVPixelBufferLockBaseAddress(buffer, .readOnly)
        defer { CVPixelBufferUnlockBaseAddress(buffer, .readOnly) }
        guard let base = CVPixelBufferGetBaseAddress(buffer) else { return nil }
        let width = CVPixelBufferGetWidth(buffer), height = CVPixelBufferGetHeight(buffer)
        let rowBytes = CVPixelBufferGetBytesPerRow(buffer)
        switch CVPixelBufferGetPixelFormatType(buffer) {
        case kCVPixelFormatType_OneComponent32Float:
            return CropMath.maskBounds(width: width, height: height) { x, y in
                base.advanced(by: y * rowBytes).assumingMemoryBound(to: Float32.self)[x] > 0.5
            }
        case kCVPixelFormatType_OneComponent8:
            return CropMath.maskBounds(width: width, height: height) { x, y in
                base.advanced(by: y * rowBytes).assumingMemoryBound(to: UInt8.self)[x] > 127
            }
        default:
            return nil
        }
    }
}
