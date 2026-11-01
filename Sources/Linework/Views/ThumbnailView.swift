import LineworkCore
import SwiftUI

/// Renders a reference image with its orientation and crop applied, off the main thread and through `RenderCache`.
struct ThumbnailView: View {
    let request: RenderRequest
    @State private var image: CGImage?

    init(photo: PhotoRecord, store: LibraryStore, maxPixel: Int = 400, showCrop: Bool = true) {
        request = store.renderRequest(photo, maxPixel: maxPixel, useThumbnail: maxPixel <= 400, showCrop: showCrop)
    }

    var body: some View {
        Group {
            // A cache hit draws immediately, without a placeholder flash.
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
