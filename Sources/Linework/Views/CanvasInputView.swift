import AppKit
import SwiftUI

/// Takes the canvas's mouse, scroll, pinch and key events and hands them to the store.
struct CanvasInputView: NSViewRepresentable {
    let store: FreehandStore
    /// A new value takes the keyboard back for the canvas.
    var focusTick = 0

    func makeNSView(context: Context) -> InputNSView {
        let view = InputNSView()
        view.store = store
        return view
    }

    func updateNSView(_ nsView: InputNSView, context: Context) {
        nsView.store = store
        if nsView.focusTick != focusTick {
            nsView.focusTick = focusTick
            DispatchQueue.main.async { [weak nsView] in
                guard let nsView, let window = nsView.window else { return }
                window.makeFirstResponder(nsView)
            }
        }
    }

    final class InputNSView: NSView {
        var store: FreehandStore?
        var focusTick = 0

        private enum KeyCode {
            static let delete: UInt16 = 51
            static let forwardDelete: UInt16 = 117
            static let escape: UInt16 = 53
        }

        /// A wheel reports lines, not points.
        private static let wheelLineHeight = 10.0

        // Top-left coordinates, as the store expects.
        override var isFlipped: Bool { true }
        // The window is movable by its background; the canvas must not move it.
        override var mouseDownCanMoveWindow: Bool { false }
        override var acceptsFirstResponder: Bool { true }

        override func viewDidMoveToWindow() {
            super.viewDidMoveToWindow()
            window?.makeFirstResponder(self)
        }

        private func location(of event: NSEvent) -> CGPoint {
            convert(event.locationInWindow, from: nil)
        }

        override func mouseDown(with event: NSEvent) {
            window?.makeFirstResponder(self)
            store?.mouseDown(at: location(of: event), clickCount: event.clickCount)
        }

        override func mouseDragged(with event: NSEvent) {
            store?.mouseDragged(to: location(of: event), shift: event.modifierFlags.contains(.shift))
        }

        override func mouseUp(with event: NSEvent) {
            store?.mouseUp()
        }

        override func scrollWheel(with event: NSEvent) {
            let scale = event.hasPreciseScrollingDeltas ? 1 : Self.wheelLineHeight
            store?.scroll(by: CGSize(width: event.scrollingDeltaX * scale, height: event.scrollingDeltaY * scale))
        }

        override func magnify(with event: NSEvent) {
            store?.magnify(by: 1 + event.magnification, at: location(of: event))
        }

        override func keyDown(with event: NSEvent) {
            let plain = event.modifierFlags.intersection([.command, .option, .control]).isEmpty
            switch event.keyCode {
            case KeyCode.delete where plain, KeyCode.forwardDelete where plain:
                store?.deleteSelected()
            case KeyCode.escape where plain:
                store?.escape()
            default:
                super.keyDown(with: event)
            }
        }
    }
}
