#if DEBUG
import AppKit
import LineworkCore
import SwiftUI

extension DebugSnapshots {
    /// A store on the seeded canvas with the rotated, cropped image selected, for the tool snapshots.
    static func freehandToolStore(tool: FreehandStore.Tool = .none, ringAt: CGPoint? = nil) -> FreehandStore {
        let store = freehandStore()
        store.loadIfNeeded()
        store.selectedID = store.document.items.first(where: { $0.flipped && $0.crop != CropMath.unit })?.id
        store.tool = tool
        store.ringAt = ringAt
        return store
    }

    /// Drives the circular menu and its tools on a live `FreehandView` with synthetic events and prints a line per check.
    static func freehandToolChecks(sources: [URL]) async {
        guard let source = sources.first else { return print("check FAIL needs an image") }
        func check(_ name: String, _ ok: Bool) { print("check", ok ? "pass" : "FAIL", name) }
        let canvas = freehandStore()
        let size = CGSize(width: 1000, height: 700)
        let window = NSWindow(contentRect: NSRect(x: -10000, y: -10000, width: size.width, height: size.height),
                              styleMask: [.titled, .resizable, .fullSizeContentView], backing: .buffered, defer: false)
        let host = NSHostingView(rootView: FreehandView().ignoresSafeArea().environment(canvas).environment(library).environment(app))
        window.contentView = host
        window.orderFrontRegardless()
        window.makeKey()
        try? await Task.sleep(for: .seconds(0.5))

        func mouse(_ type: NSEvent.EventType, _ p: CGPoint, clicks: Int = 1) {
            let event = NSEvent.mouseEvent(with: type, location: NSPoint(x: p.x, y: size.height - p.y), modifierFlags: [],
                                           timestamp: ProcessInfo.processInfo.systemUptime, windowNumber: window.windowNumber,
                                           context: nil, eventNumber: 0, clickCount: clicks, pressure: 1)!
            guard let view = host.hitTest(NSPoint(x: p.x, y: size.height - p.y)) else { return print("check FAIL nothing hit at", p) }
            switch type {
            case .leftMouseDown: view.mouseDown(with: event)
            case .leftMouseDragged: view.mouseDragged(with: event)
            default: view.mouseUp(with: event)
            }
        }
        func click(_ p: CGPoint, clicks: Int = 1) {
            mouse(.leftMouseDown, p, clicks: clicks)
            mouse(.leftMouseUp, p, clicks: clicks)
        }
        func drag(from a: CGPoint, to b: CGPoint) {
            mouse(.leftMouseDown, a)
            mouse(.leftMouseDragged, b)
            mouse(.leftMouseUp, b)
        }
        func key(_ code: UInt16) -> NSEvent {
            NSEvent.keyEvent(with: .keyDown, location: .zero, modifierFlags: [], timestamp: ProcessInfo.processInfo.systemUptime,
                             windowNumber: window.windowNumber, context: nil, characters: "", charactersIgnoringModifiers: "",
                             isARepeat: false, keyCode: code)!
        }
        func item() -> FreehandItem { canvas.document.items[0] }
        func screen(_ p: CGPoint) -> CGPoint { canvas.document.camera.toScreen(p) }
        func near(_ a: CGPoint, _ b: CGPoint) -> Bool { hypot(a.x - b.x, a.y - b.y) < 1e-6 }
        func ringButton(_ index: Int, around at: CGPoint) -> CGPoint {
            let center = FreehandRadialMenu.center(for: at, in: size, barWidth: canvas.barWidth)
            let offset = FreehandRadialMenu.offsets()[index]
            return CGPoint(x: center.x + offset.x, y: center.y + offset.y)
        }
        // Straight events skip SwiftUI's buttons, so these go through the window's own event dispatch.
        func press(at p: CGPoint) async {
            for type in [NSEvent.EventType.leftMouseDown, .leftMouseUp] {
                window.sendEvent(NSEvent.mouseEvent(with: type, location: NSPoint(x: p.x, y: size.height - p.y), modifierFlags: [],
                                                    timestamp: ProcessInfo.processInfo.systemUptime, windowNumber: window.windowNumber,
                                                    context: nil, eventNumber: 0, clickCount: 1, pressure: 1)!)
                try? await Task.sleep(for: .milliseconds(60))
            }
        }
        func openRing(at p: CGPoint) async {
            click(p, clicks: 2)
            try? await Task.sleep(for: .seconds(0.3))
        }

        canvas.clear()
        await canvas.importFiles([source], at: CGPoint(x: 500, y: 350))
        let middle = screen(item().center)

        // The ring: real buttons above the images, shifted to stay inside the canvas.
        await openRing(at: middle)
        check("double click opens the ring", canvas.ringAt == middle)
        check("ring buttons take the click", !(host.hitTest(NSPoint(x: ringButton(2, around: middle).x,
                                                                    y: size.height - ringButton(2, around: middle).y)) is CanvasInputView.InputNSView))
        check("elsewhere still reaches the input view", host.hitTest(NSPoint(x: 950, y: size.height - 650)) is CanvasInputView.InputNSView)
        let offsets = FreehandRadialMenu.offsets()
        check("five buttons on a 64pt radius, first straight up",
              offsets.count == 5 && abs(offsets[0].x) < 1e-9 && abs(offsets[0].y + 64) < 1e-9
                  && offsets.allSatisfy { abs(hypot($0.x, $0.y) - 64) < 1e-9 })
        for corner in [CGPoint(x: 3, y: 3), CGPoint(x: 997, y: 3), CGPoint(x: 3, y: 697), CGPoint(x: 997, y: 697)] {
            let c = FreehandRadialMenu.center(for: corner, in: size, barWidth: canvas.barWidth)
            let inside = offsets.allSatisfy {
                c.x + $0.x - 21 >= -1e-9 && c.x + $0.x + 21 <= size.width + 1e-9
                    && c.y + $0.y - 21 >= -1e-9 && c.y + $0.y + 21 <= size.height + 1e-9
            }
            check("ring near the corner at \(Int(corner.x)),\(Int(corner.y)) stays inside", inside)
        }
        check("ring in the open isn't moved", FreehandRadialMenu.center(for: middle, in: size, barWidth: canvas.barWidth) == middle)

        // Flip from the ring: immediate, and the image stays where it is.
        let before = item()
        await press(at: ringButton(2, around: middle))
        check("Flip mirrors at once, in place", item().flipped != before.flipped && item().center == before.center
              && item().rotation == before.rotation && canvas.ringAt == nil)

        // Resize.
        await openRing(at: middle)
        await press(at: ringButton(0, around: middle))
        check("Resize enters resize mode and closes the ring", canvas.tool == .resize && canvas.ringAt == nil)
        var start = item()
        var corners = FreehandGeometry.corners(of: start).map(screen)
        drag(from: CGPoint(x: corners[2].x + 5, y: corners[2].y + 3), to: CGPoint(x: corners[2].x + 65, y: corners[2].y + 33))
        var grown = item()
        check("dragging a corner scales it around the opposite corner",
              grown.fullSize.width > start.fullSize.width
                  && near(FreehandGeometry.corners(of: grown)[0], FreehandGeometry.corners(of: start)[0]))
        check("proportions kept", abs(grown.fullSize.width / grown.fullSize.height - start.fullSize.width / start.fullSize.height) < 1e-9)
        check("the grab offset doesn't make it jump", abs(grown.fullSize.width - start.fullSize.width) < 130
              && grown.fullSize.width > start.fullSize.width + 20)
        start = item()
        corners = FreehandGeometry.corners(of: start).map(screen)
        drag(from: corners[2], to: corners[0])
        grown = item()
        check("long edge stops at 32",
              abs(max(grown.visibleSize.width, grown.visibleSize.height) - 32) < 1e-6
                  && near(FreehandGeometry.corners(of: grown)[0], FreehandGeometry.corners(of: start)[0]))

        // The 24pt area is the same on screen at any zoom.
        canvas.magnify(by: 2, at: CGPoint(x: 500, y: 350))
        let zoomedBefore = item()
        let corner = screen(FreehandGeometry.corners(of: zoomedBefore)[2])
        drag(from: CGPoint(x: corner.x + 11, y: corner.y), to: CGPoint(x: corner.x + 60, y: corner.y + 40))
        check("a press 11pt from a handle grabs it, at 200%", item().fullSize.width > zoomedBefore.fullSize.width)
        let offBefore = canvas.document.camera.offset
        let corner2 = screen(FreehandGeometry.corners(of: item())[2])
        drag(from: CGPoint(x: corner2.x + 13, y: corner2.y + 13), to: CGPoint(x: corner2.x + 33, y: corner2.y + 13))
        check("13pt away isn't the handle", canvas.document.camera.offset != offBefore)
        canvas.magnify(by: 0.5, at: CGPoint(x: 500, y: 350))

        // Moving still works in a mode; Esc ends it.
        let moving = item()
        let body = screen(moving.center)
        drag(from: body, to: CGPoint(x: body.x + 30, y: body.y + 20))
        check("dragging the body moves it in a mode", canvas.tool == .resize
              && abs(item().center.x - moving.center.x - 30 / canvas.document.camera.zoom) < 1e-9)
        window.sendEvent(key(53))
        check("Esc ends the mode", canvas.tool == .none && canvas.selectedID != nil)

        // Restore a visible size, then rotate.
        canvas.tool = .resize
        let small = screen(FreehandGeometry.corners(of: item())[2])
        drag(from: small, to: CGPoint(x: 700, y: 560))
        canvas.tool = .none
        let at = screen(item().center)
        await openRing(at: at)
        await press(at: ringButton(1, around: at))
        check("Rotate enters rotate mode", canvas.tool == .rotate)
        let handle = screen(FreehandGeometry.rotateHandle(of: item(), zoom: canvas.document.camera.zoom))
        let center = screen(item().center)
        mouse(.leftMouseDown, handle)
        mouse(.leftMouseDragged, CGPoint(x: center.x + 200, y: center.y))
        check("dragging the handle turns it freely", abs(item().rotation - 90) < 1e-6)
        let angle = 37.0 * .pi / 180
        canvas.mouseDragged(to: CGPoint(x: center.x + 200 * sin(angle), y: center.y - 200 * cos(angle)), shift: true)
        check("Shift snaps to 15 degrees", abs(item().rotation - 30) < 1e-6)
        mouse(.leftMouseUp, center)

        // Empty space ends a mode.
        click(CGPoint(x: 950, y: 650))
        check("a click on empty space ends the mode", canvas.tool == .none && canvas.selectedID == nil)

        // Crop: the sheet owns the keys while it's up, and the canvas gets them back.
        click(screen(item().center))
        await openRing(at: screen(item().center))
        let id = canvas.selectedID
        await press(at: ringButton(3, around: screen(item().center)))
        try? await Task.sleep(for: .seconds(1.0))
        check("Crop opens the sheet", canvas.cropID == id && window.attachedSheet != nil)
        let sheet = window.attachedSheet
        let keyed = sheet?.isKeyWindow == true || NSApp.keyWindow === sheet
        if keyed {
            check("the sheet is the key window", true)
        } else {
            print("check SKIP sheet can't become key off-screen: key-in-sheet checks below only show the canvas stays untouched")
        }
        check("the canvas keeps its first responder under the sheet", window.firstResponder is CanvasInputView.InputNSView)
        let count = canvas.document.items.count
        sheet?.sendEvent(key(51))
        check("Delete in the sheet removes nothing", canvas.document.items.count == count && canvas.selectedID == id)
        window.makeFirstResponder(host)
        sheet?.sendEvent(key(53))
        check("Esc in the sheet doesn't touch the canvas selection", canvas.selectedID == id)
        try? await Task.sleep(for: .seconds(0.8))
        print("info Esc closed the sheet itself:", window.attachedSheet == nil)
        if let open = window.attachedSheet { window.endSheet(open) }
        try? await Task.sleep(for: .seconds(0.8))
        check("closing the sheet ends the crop", canvas.cropID == nil && window.attachedSheet == nil)
        check("the canvas has the keyboard again", window.firstResponder is CanvasInputView.InputNSView)

        // A crop leaves the kept part where it was, and the new crop gets its own decode.
        func canvasPoint(of item: FreehandItem, u: Double, v: Double) -> CGPoint {
            let x = (u - item.crop.midX) * item.fullSize.width * (item.flipped ? -1 : 1)
            let y = (v - item.crop.midY) * item.fullSize.height
            let t = item.rotation * .pi / 180
            return CGPoint(x: item.center.x + x * cos(t) - y * sin(t), y: item.center.y + x * sin(t) + y * cos(t))
        }
        let whole = item()
        canvas.cropID = whole.id
        canvas.applyCrop(CGRect(x: 0.25, y: 0.25, width: 0.5, height: 0.5))
        canvas.endCrop()
        check("kept part stays in place (rotated and flipped)",
              near(canvasPoint(of: whole, u: 0.4, v: 0.6), canvasPoint(of: item(), u: 0.4, v: 0.6))
                  && item().crop == CGRect(x: 0.25, y: 0.25, width: 0.5, height: 0.5)
                  && abs(item().visibleSize.width - whole.visibleSize.width / 2) < 1e-9)
        try? await Task.sleep(for: .seconds(1.0))
        let request = RenderRequest(url: canvas.files.imageURL(item().fileName), rotation: 0, flipped: false, crop: item().crop,
                                    maxPixel: FreehandGeometry.decodePixels(for: item(), camera: canvas.document.camera, viewport: canvas.viewport))
        check("the new crop is decoded on its own", RenderCache.shared[request.key] != nil)

        // Remove: no confirmation, canvas copy and file go.
        let removing = item()
        canvas.selectedID = removing.id
        await openRing(at: screen(removing.center))
        await press(at: ringButton(4, around: screen(removing.center)))
        check("Remove deletes the image and its file", canvas.document.items.isEmpty && canvas.ringAt == nil
              && !FileManager.default.fileExists(atPath: canvas.files.imageURL(removing.fileName).path))
        // Leaving Freehand drops the ring and the tool; the store outlives the view.
        canvas.selectedID = nil
        canvas.clear()
        await canvas.importFiles([source], at: CGPoint(x: 500, y: 350))
        canvas.tool = .rotate
        canvas.ringAt = CGPoint(x: 400, y: 300)
        canvas.leave()
        check("leaving clears the ring and the tool", canvas.ringAt == nil && canvas.tool == .none)

        // The ring belongs to the spot it opened on: anything that moves the image from under it closes it.
        let spot = screen(item().center)
        await openRing(at: spot)
        canvas.scroll(by: CGSize(width: 5, height: 5))
        check("scrolling closes the ring", canvas.ringAt == nil)
        await openRing(at: screen(item().center))
        canvas.magnify(by: 1.1, at: CGPoint(x: 500, y: 350))
        check("zooming closes the ring", canvas.ringAt == nil)
        await openRing(at: screen(item().center))
        canvas.scroll(by: .zero)
        check("a scroll that moves nothing leaves the ring", canvas.ringAt != nil)
        canvas.ringAt = nil
        let press2 = screen(item().center)
        mouse(.leftMouseDown, press2, clicks: 2)
        check("the second press opens the ring", canvas.ringAt == press2)
        mouse(.leftMouseDragged, CGPoint(x: press2.x + 1, y: press2.y))
        check("a wobble on the second press leaves the ring", canvas.ringAt == press2)
        mouse(.leftMouseDragged, CGPoint(x: press2.x + 30, y: press2.y))
        check("dragging past the threshold closes the ring", canvas.ringAt == nil)
        mouse(.leftMouseUp, CGPoint(x: press2.x + 30, y: press2.y))
        canvas.fit()

        // The ring keeps clear of the floating bar, but only where it would reach it.
        let barWidth = canvas.barWidth
        check("the bar's width is measured", barWidth > 100 && barWidth < size.width)
        check("the bar reserves 66pt", FreehandBar.reservedHeight == 66)
        func lowest(_ c: CGPoint) -> Double { offsets.map { c.y + $0.y + 21 }.max() ?? 0 }
        let overBar = FreehandRadialMenu.center(for: CGPoint(x: 500, y: 695), in: size, barWidth: barWidth)
        check("over the bar, the ring stops above it", overBar.x == 500 && abs(lowest(overBar) - (size.height - 66)) < 1e-9)
        let beside = FreehandRadialMenu.center(for: CGPoint(x: 100, y: 695), in: size, barWidth: barWidth)
        check("beside the bar, the ring can use the bottom", abs(lowest(beside) - size.height) < 1e-9)
        let ringHalf = (offsets.map(\.x).max() ?? 0) + 21
        let barLeft = (size.width - barWidth) / 2
        let clear = FreehandRadialMenu.center(for: CGPoint(x: barLeft - ringHalf - 1, y: 695), in: size, barWidth: barWidth)
        let touching = FreehandRadialMenu.center(for: CGPoint(x: barLeft - ringHalf + 1, y: 695), in: size, barWidth: barWidth)
        check("the reserve starts where the ring's x meets the bar's",
              abs(lowest(clear) - size.height) < 1e-9 && abs(lowest(touching) - (size.height - 66)) < 1e-9)
        canvas.selectedID = item().id
        let flipBefore = item().flipped
        let low = CGPoint(x: 500, y: 695)
        canvas.ringAt = low
        try? await Task.sleep(for: .seconds(0.3))
        await press(at: ringButton(2, around: low))
        check("a low button by the bar still works, on the real views", item().flipped != flipBefore && canvas.ringAt == nil)
        window.orderOut(nil)
        await freehandCropSheetChecks(source: source)
        await freehandBarChecks(source: source)
    }

    /// Stands in for moving to another section: takes `FreehandView` away and brings it back.
    private final class SectionSwitch: ObservableObject {
        @Published var showsFreehand = true
    }

    private struct SectionHost: View {
        @ObservedObject var section: SectionSwitch
        let canvas: FreehandStore

        var body: some View {
            Group {
                if section.showsFreehand { FreehandView().environment(canvas) } else { Color.gray }
            }
        }
    }

    /// The crop sheet doesn't come back after leaving, and on a flipped image it frames what the canvas shows.
    private static func freehandCropSheetChecks(source: URL) async {
        func check(_ name: String, _ ok: Bool) { print("check", ok ? "pass" : "FAIL", name) }
        let canvas = freehandStore()
        let section = SectionSwitch()
        let window = NSWindow(contentRect: NSRect(x: -10000, y: -10000, width: 1000, height: 700),
                              styleMask: [.titled, .resizable], backing: .buffered, defer: false)
        window.contentView = NSHostingView(rootView: SectionHost(section: section, canvas: canvas).environment(library).environment(app))
        window.orderFrontRegardless()
        try? await Task.sleep(for: .seconds(0.5))
        canvas.clear()
        await canvas.importFiles([source], at: CGPoint(x: 500, y: 350))

        canvas.beginCrop()
        try? await Task.sleep(for: .seconds(1.5))
        check("the sheet is up before leaving", canvas.cropID != nil && window.attachedSheet != nil)
        section.showsFreehand = false
        try? await Task.sleep(for: .seconds(1.5))
        check("leaving ends the crop", canvas.cropID == nil && window.attachedSheet == nil)
        section.showsFreehand = true
        try? await Task.sleep(for: .seconds(1.5))
        check("coming back doesn't reopen the sheet", canvas.cropID == nil && window.attachedSheet == nil)

        // Flipped: in the sheet the crop is mirrored, as seen; in the store it stays in the file's space.
        guard let id = canvas.selectedID else { return print("check FAIL nothing selected for the flipped crop") }
        canvas.flipSelected()
        var item: FreehandItem { canvas.document.items.first { $0.id == id }! }
        let center = item.center, fullWidth = item.fullSize.width
        canvas.cropID = id
        let opening = CropEditorView(item: item, store: canvas)
        check("the sheet opens flipped, on the whole image", opening.initial == CropEdit(crop: CropMath.unit, rotation: 0, flipped: true))
        opening.onSave(CropEdit(crop: CGRect(x: 0.5, y: 0, width: 0.5, height: 1), rotation: 0, flipped: true))
        check("what's framed on the right, as seen, is stored on the left of the file",
              item.crop == CGRect(x: 0, y: 0, width: 0.5, height: 1))
        check("the kept part stays where it was on the canvas",
              abs(item.center.x - (center.x + fullWidth / 4)) < 1e-9 && item.center.y == center.y)
        let reopening = CropEditorView(item: item, store: canvas)
        check("reopening shows the crop where it was framed", reopening.initial.crop == CGRect(x: 0.5, y: 0, width: 0.5, height: 1))
        canvas.endCrop()
        section.showsFreehand = false
        try? await Task.sleep(for: .seconds(0.2))
        window.orderOut(nil)
    }

    /// The floating bar's actions on the store, and the notes' switch, deferral and timing, on a live `SeaNoteView`.
    static func freehandBarChecks(source: URL) async {
        func check(_ name: String, _ ok: Bool) { print("check", ok ? "pass" : "FAIL", name) }
        let canvas = freehandStore()
        canvas.viewport = CGSize(width: 1000, height: 700)
        canvas.clear()
        await canvas.importFiles([source], at: CGPoint(x: 500, y: 350))
        await canvas.importFiles([source], at: CGPoint(x: 900, y: 650))
        let names = canvas.document.items.map(\.fileName)

        canvas.setGreyscale(true)
        canvas.magnify(by: 4, at: CGPoint(x: 100, y: 100))
        canvas.fit()
        let fitted = canvas.document.camera
        check("Fit stays at or under 100% and matches the geometry",
              fitted.zoom <= 1 && fitted == FreehandGeometry.fitted(canvas.document.items, in: canvas.viewport))

        let beforeClear = canvas.document.camera
        canvas.clear()
        check("Clear removes every image and its file",
              canvas.document.items.isEmpty && names.allSatisfy { !FileManager.default.fileExists(atPath: canvas.files.imageURL($0).path) })
        check("Clear keeps greyscale, zoom and position", canvas.document.greyscale && canvas.document.camera == beforeClear)
        canvas.fit()
        check("Fit with no images returns home", canvas.document.camera == .home)
        canvas.setGreyscale(false)

        // Notes: a note shows at once under the injected timing, waits for the ring, and the setting hides it.
        let defaults = UserDefaults.standard
        defaults.set(true, forKey: "linework.notes")
        SeaNoteView.debugShown = nil
        SeaNoteView.debugTiming = SeaNoteTiming(first: .milliseconds(100), interval: { .seconds(3600) }, visible: .seconds(3600),
                                                recheck: .milliseconds(50))
        SeaNoteView.debugLine = "You can do it."
        canvas.ringAt = CGPoint(x: 100, y: 100)
        let window = NSWindow(contentRect: NSRect(x: -10000, y: -10000, width: 600, height: 300),
                              styleMask: [.titled], backing: .buffered, defer: false)
        window.contentView = NSHostingView(rootView: SeaNoteView(store: canvas).environment(library).environment(app))
        window.orderFrontRegardless()
        try? await Task.sleep(for: .seconds(0.6))
        check("A due note waits while the circular menu is open", SeaNoteView.debugShown == nil)
        canvas.ringAt = nil
        try? await Task.sleep(for: .seconds(0.8))
        check("The note appears once the menu closes", SeaNoteView.debugShown == "You can do it.")
        defaults.set(false, forKey: "linework.notes")
        try? await Task.sleep(for: .seconds(0.3))
        check("Turning the setting off hides the note at once", SeaNoteView.debugShown == nil)
        defaults.set(true, forKey: "linework.notes")
        try? await Task.sleep(for: .seconds(0.8))
        check("Turning it on starts a new visit", SeaNoteView.debugShown == "You can do it.")
        // Sleep hides the note; waking starts a new visit whose clock begins again.
        let center = NSWorkspace.shared.notificationCenter
        center.post(name: NSWorkspace.willSleepNotification, object: nil)
        try? await Task.sleep(for: .seconds(0.1))
        check("Sleep hides the note", SeaNoteView.debugShown == nil)
        SeaNoteView.debugTiming = SeaNoteTiming(first: .milliseconds(100), interval: { .milliseconds(100) },
                                                visible: .milliseconds(100), recheck: .milliseconds(50))
        center.post(name: NSWorkspace.didWakeNotification, object: nil)
        try? await Task.sleep(for: .seconds(2.6))
        let before = SeaNoteView.debugVisitSeconds ?? 0
        center.post(name: NSWorkspace.didWakeNotification, object: nil)
        try? await Task.sleep(for: .seconds(0.35))
        check("Wake restarts the visit clock (was \(before)s, now \(SeaNoteView.debugVisitSeconds ?? -1)s)",
              before >= 2 && SeaNoteView.debugVisitSeconds == 0)
        defaults.removeObject(forKey: "linework.notes")
        window.orderOut(nil)
        SeaNoteView.debugTiming = nil
        SeaNoteView.debugLine = nil
        SeaNoteView.debugShown = nil
    }
}
#endif
