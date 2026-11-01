import SwiftUI

/// The floating bar at the bottom center of the canvas: Greyscale, the zoom percentage (fits all images) and Clear.
/// A status message hangs off the right edge as its own small capsule, so showing it never moves the controls.
struct FreehandBar: View {
    let store: FreehandStore
    @State private var confirmingClear = false

    private static let controlHeight = 30.0
    private static let padding = 8.0
    static let bottomInset = 20.0
    /// What the bar takes from the bottom of the canvas.
    static let reservedHeight = controlHeight + 2 * padding + bottomInset

    var body: some View {
        HStack(spacing: 10) {
            Toggle("Greyscale", isOn: Binding(get: { store.document.greyscale }, set: { store.setGreyscale($0) }))
                .toggleStyle(CapsuleToggleStyle(height: Self.controlHeight))
            Button("\(Int((store.document.camera.zoom * 100).rounded()))%") { store.fit() }
                .buttonStyle(CapsuleButtonStyle(height: Self.controlHeight))
                .monospacedDigit()
                .frame(minWidth: 72)
                .help("Fit all images in the window")
                .accessibilityLabel("Zoom, fit all images")
            Button("Clear") { confirmingClear = true }
                .buttonStyle(CapsuleButtonStyle(height: Self.controlHeight))
                .disabled(store.document.items.isEmpty)
                .opacity(store.document.items.isEmpty ? 0.4 : 1)
        }
        .padding(Self.padding)
        .background(Capsule(style: .circular).fill(.ultraThinMaterial))
        .overlay(Capsule(style: .circular).strokeBorder(Theme.hairline, lineWidth: 1))
        .onGeometryChange(for: CGFloat.self, of: \.size.width) { store.barWidth = $0 }
        .overlay(alignment: .trailing) {
            if let status = store.status {
                Text(status)
                    .font(Theme.font(12, .medium))
                    .foregroundStyle(Theme.muted)
                    .lineLimit(1)
                    .fixedSize()
                    .padding(.horizontal, 12)
                    .frame(height: Self.controlHeight + 2 * Self.padding)
                    .background(Capsule(style: .circular).fill(.ultraThinMaterial))
                    .overlay(Capsule(style: .circular).strokeBorder(Theme.hairline, lineWidth: 1))
                    .alignmentGuide(.trailing) { $0[.leading] - 8 }
                    .transition(.opacity)
            }
        }
        .confirmationDialog("Clear your canvas?", isPresented: $confirmingClear) {
            Button("Clear", role: .destructive) { store.clear() }
            Button("Cancel", role: .cancel) {}
        } message: {
            Text("This removes all images from Freehand. Your original files are safe.")
        }
    }
}
