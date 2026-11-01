import LineworkCore
import SwiftUI

/// Shows an image file (a drawing photo) as-is, through `RenderCache`.
struct DrawingImageView: View {
    let request: RenderRequest
    @State private var image: CGImage?

    init(url: URL, maxPixel: Int = 600) {
        request = RenderRequest(url: url, rotation: 0, flipped: false, crop: CropMath.unit, maxPixel: maxPixel)
    }

    var body: some View {
        Group {
            if let image = image ?? RenderCache.shared[request.key] {
                Image(decorative: image, scale: 1).resizable().scaledToFit()
            } else {
                Rectangle().fill(Theme.hairline)
            }
        }
        .task(id: request) {
            image = await RenderCache.shared.image(for: request)
        }
    }
}
