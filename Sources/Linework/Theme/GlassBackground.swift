import AppKit
import LineworkCore
import SwiftUI

/// Behind-window blur under the ground tint. Opacity of the tint comes from the Transparency setting.
struct GlassBackground: View {
    @AppStorage("linework.transparency") private var transparency = Glass.defaultTransparency

    var body: some View {
        ZStack {
            #if DEBUG
            // The blur paints an opaque material offscreen, so snapshots swap in their backdrop.
            if let backdrop = DebugSnapshots.backdrop {
                backdrop
            } else {
                VisualEffect()
            }
            #else
            VisualEffect()
            #endif
            Theme.ground.opacity(Glass.tintOpacity(transparency: transparency))
        }
        .ignoresSafeArea()
    }

    private struct VisualEffect: NSViewRepresentable {
        func makeNSView(context: Context) -> NSVisualEffectView {
            let view = NSVisualEffectView()
            view.blendingMode = .behindWindow
            view.state = .active
            view.material = .underWindowBackground
            return view
        }

        func updateNSView(_ nsView: NSVisualEffectView, context: Context) {}
    }
}

private struct LineworkWindow: ViewModifier {
    func body(content: Content) -> some View {
        content.background(WindowAccessor { window in
            window.isOpaque = false
            window.backgroundColor = .clear
            window.titlebarAppearsTransparent = true
            window.isMovableByWindowBackground = true
        })
    }
}

extension View {
    /// Makes the hosting window non-opaque with a transparent title bar so the glass shows through.
    func lineworkWindow() -> some View { modifier(LineworkWindow()) }
}
