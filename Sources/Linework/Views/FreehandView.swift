import LineworkCore
import SwiftUI

/// The canvas, filling the area under the top bar. Back to front: clock, input, images and selection.
/// The images ignore clicks, so everything reaches the input view, and the store does the hit-testing.
struct FreehandView: View {
    @Environment(FreehandStore.self) private var store

    var body: some View {
        ZStack {
            FreehandClockLayer(showsEmptyLine: store.document.items.isEmpty)
            CanvasInputView(store: store, focusTick: store.focusTick)
            FreehandImageLayer(store: store).allowsHitTesting(false)
            FreehandHandles(store: store)
            FreehandRadialMenu(store: store)
        }
        .overlay(alignment: .bottom) {
            FreehandBar(store: store)
                .overlay(alignment: .top) {
                    SeaNoteView(store: store).alignmentGuide(.top) { $0[.bottom] + 12 }
                }
                .padding(.bottom, FreehandBar.bottomInset)
        }
        .frame(maxWidth: .infinity, maxHeight: .infinity)
        .clipped()
        .background { zoomShortcuts }
        .dropDestination(for: URL.self) { urls, location in
            Task { await store.importFiles(urls, at: location) }
            return true
        }
        .sheet(isPresented: Binding(get: { store.cropID != nil }, set: { if !$0 { store.endCrop() } })) {
            if let item = store.document.items.first(where: { $0.id == store.cropID }) {
                CropEditorView(item: item, store: store)
            }
        }
        .onGeometryChange(for: CGSize.self, of: \.size) { store.viewport = $0 }
        .onAppear { store.loadIfNeeded() }
        .onDisappear { store.leave() }
    }

    /// ⌘= is ⌘+ on keyboards where + needs Shift.
    private var zoomShortcuts: some View {
        Group {
            Button("Zoom In") { store.zoomStep(in: true) }.keyboardShortcut("+", modifiers: .command)
            Button("Zoom In") { store.zoomStep(in: true) }.keyboardShortcut("=", modifiers: .command)
            Button("Zoom Out") { store.zoomStep(in: false) }.keyboardShortcut("-", modifiers: .command)
        }
        .frame(width: 0, height: 0)
        .opacity(0)
        .accessibilityHidden(true)
    }
}

/// The clock in the exact middle of the canvas, with the empty-state line hanging under it.
/// Its own view, so the 1-second tick redraws only this and canvas changes leave it alone.
private struct FreehandClockLayer: View {
    let showsEmptyLine: Bool

    var body: some View {
        TimelineView(.periodic(from: .now, by: 1)) { timeline in
            HeroClock(now: timeline.date)
        }
        .overlay(alignment: .bottom) {
            if showsEmptyLine {
                Text(LineworkCopy.freehandEmpty)
                    .font(Theme.font(15))
                    .foregroundStyle(Theme.muted)
                    .fixedSize()
                    .alignmentGuide(.bottom) { $0[.top] - 12 }
            }
        }
        .frame(maxWidth: .infinity, maxHeight: .infinity)
        .allowsHitTesting(false)
    }
}

/// The images in stacking order, in greyscale when that's on, and the outline of the selected one.
struct FreehandImageLayer: View {
    let store: FreehandStore

    var body: some View {
        let camera = store.document.camera
        ZStack {
            ZStack {
                ForEach(store.document.items) { item in
                    FreehandImageView(item: item, url: store.files.imageURL(item.fileName), camera: camera, viewport: store.viewport)
                }
            }
            .grayscale(store.document.greyscale ? 1 : 0)
            if let selected = store.document.items.first(where: { $0.id == store.selectedID }) {
                Rectangle()
                    .strokeBorder(Theme.ink, lineWidth: 1)
                    .frame(width: selected.visibleSize.width * camera.zoom,
                           height: selected.visibleSize.height * camera.zoom)
                    .rotationEffect(.degrees(selected.rotation))
                    .position(camera.toScreen(selected.center))
            }
        }
    }
}

/// One image on the canvas, positioned in screen space. A hairline frame shows until it's decoded.
private struct FreehandImageView: View {
    let item: FreehandItem
    let url: URL
    let camera: FreehandCamera
    let viewport: CGSize

    /// The last decode, with the crop it shows. Another crop's pixels must never fill this frame.
    @State private var decoded: (crop: CGRect, image: CGImage)?

    private var screenSize: CGSize {
        CGSize(width: item.visibleSize.width * camera.zoom, height: item.visibleSize.height * camera.zoom)
    }

    private var request: RenderRequest {
        RenderRequest(url: url, rotation: 0, flipped: false, crop: item.crop,
                      maxPixel: FreehandGeometry.decodePixels(for: item, camera: camera, viewport: viewport))
    }

    var body: some View {
        let request = request
        Group {
            if let shown = RenderCache.shared[request.key] ?? (decoded?.crop == item.crop ? decoded?.image : nil) {
                // Over the ground, so a transparent PNG doesn't let the clock through.
                Image(decorative: shown, scale: 1).resizable().background(Theme.ground)
            } else {
                Rectangle().strokeBorder(Theme.hairline, lineWidth: 1)
            }
        }
        .frame(width: screenSize.width, height: screenSize.height)
        .scaleEffect(x: item.flipped ? -1 : 1)
        .rotationEffect(.degrees(item.rotation))
        .position(camera.toScreen(item.center))
        .task(id: request) {
            let result = await DecodeGate.freehand.image(for: request)
            if let result, !Task.isCancelled { decoded = (request.crop, result) }
        }
    }
}

extension CropEditorView {
    /// Freehand's editor: the image looks as it does on the canvas, and the canvas keeps its own Rotate and Flip.
    init(item: FreehandItem, store: FreehandStore) {
        self.init(imageURL: store.files.imageURL(item.fileName),
                  initial: CropEdit(crop: item.shownCrop, rotation: 0, flipped: item.flipped),
                  showsOrientation: false) { store.applyCrop(item.storedCrop(fromShown: $0.crop)) }
    }
}
