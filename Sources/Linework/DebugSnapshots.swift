import AppKit
import LineworkCore
import SwiftData
import SwiftUI

/// Debug-only: `Linework --snapshots <outDir> <image>...` (with LINEWORK_LIBRARY set to a scratch folder)
/// imports the images, seeds sample history, renders each screen offscreen to PNG and quits.
@MainActor
enum DebugSnapshots {
    static var active = false
    /// What the snapshot harness draws in place of the blur. Nil outside a transparency render.
    static var backdrop: Color?
    static var container: ModelContainer?

    /// `Linework --print-counts`: row counts for every model, for checking a library upgrade.
    static func printCounts() {
        #if DEBUG
        guard let context = container?.mainContext else { return }
        func count<T: PersistentModel>(_: T.Type) -> Int { (try? context.fetchCount(FetchDescriptor<T>())) ?? -1 }
        print("images", count(RefImage.self), "sets", count(ImageSet.self), "presets", count(ClassPreset.self),
              "sessions", count(SessionRecord.self), "attempts", count(BenchmarkAttempt.self),
              "checks", count(PlanCheck.self), "repeats", count(PlanRepeatRecord.self))
        exit(0)
        #endif
    }

    static func run(outDir: URL, sources: [URL]) async {
        #if DEBUG
        guard let container else { return }
        active = true
        NSApp.windows.forEach { $0.orderOut(nil) }
        try? FileManager.default.createDirectory(at: outDir, withIntermediateDirectories: true)
        let context = container.mainContext

        let store = SessionStore()
        // LINEWORK_TRANSPARENCY sets the glass, over a mid-grey that stands in for the blur offscreen.
        let transparency = ProcessInfo.processInfo.environment["LINEWORK_TRANSPARENCY"].flatMap(Double.init)
        if let transparency {
            UserDefaults.standard.set(transparency, forKey: "linework.transparency")
            backdrop = Color(white: 0.5)
        } else {
            UserDefaults.standard.removeObject(forKey: "linework.transparency")
        }
        // LINEWORK_SHOTS=a,b limits which screens are rendered.
        let only = ProcessInfo.processInfo.environment["LINEWORK_SHOTS"]?.split(separator: ",").map(String.init)
        func shell(_ route: AppRoute) -> some View {
            store.route = route
            return AppShell()
        }
        func shoot(_ name: String, size: CGSize, wait: Double = 1.5, scheme: ColorScheme = .light, _ view: some View) async {
            if let only, !only.contains(name) { return }
            // The shell's own store, loaded afresh; views that bring their own store override it.
            let root = view.modelContainer(container).environment(store).environment(FreehandStore()).environment(\.colorScheme, scheme)
                .background(Color(nsColor: .windowBackgroundColor))
            let window = NSWindow(contentRect: NSRect(x: -10000, y: -10000, width: size.width, height: size.height),
                                  styleMask: [.titled, .resizable, .fullSizeContentView], backing: .buffered, defer: false)
            window.appearance = NSAppearance(named: scheme == .dark ? .darkAqua : .aqua)
            let host = NSHostingView(rootView: root)
            window.contentView = host
            window.orderFrontRegardless()
            try? await Task.sleep(for: .seconds(wait))
            if let rep = host.bitmapImageRepForCachingDisplay(in: host.bounds) {
                host.cacheDisplay(in: host.bounds, to: rep)
                try? rep.representation(using: .png, properties: [:])?.write(to: outDir.appending(path: "\(name).png"))
            }
            window.orderOut(nil)
            print("shot", name)
        }

        // Freehand first, before anything touches the Library. LINEWORK_FREEHAND_ONLY stops after it,
        // so `--print-counts` before and after shows what Freehand itself changed.
        UserDefaults.standard.set(true, forKey: "linework.welcomed")
        if only?.contains("freehand-import") == true { await freehandImportProbe(sources: sources, outDir: outDir) }
        let large = CGSize(width: 1280, height: 800), small = CGSize(width: 1100, height: 700)
        await shoot("freehand-empty", size: large, shell(.section(.freehand)))
        await shoot("freehand-empty-dark", size: large, scheme: .dark, shell(.section(.freehand)))
        await shoot("freehand-empty-1100", size: small, shell(.section(.freehand)))
        seedFreehand(sources: sources)
        await shoot("freehand-images", size: large, shell(.section(.freehand)))
        await shoot("freehand-images-dark", size: large, scheme: .dark, shell(.section(.freehand)))
        await shoot("freehand-images-1100", size: small, shell(.section(.freehand)))
        let selecting = FreehandStore()
        selecting.loadIfNeeded()
        selecting.selectedID = selecting.document.items.first(where: { $0.rotation != 0 })?.id
        await shoot("freehand-selected", size: large, ZStack { GlassBackground(); FreehandView().padding(.top, 52) }.ignoresSafeArea().environment(selecting))
        func toolShot(_ name: String, scheme: ColorScheme = .light, tool: FreehandStore.Tool = .none, ring: CGPoint? = nil) async {
            let tools = freehandToolStore(tool: tool, ringAt: ring)
            await shoot(name, size: large, scheme: scheme, ZStack { GlassBackground(); FreehandView().padding(.top, 52) }.ignoresSafeArea().environment(tools))
        }
        await toolShot("freehand-ring", ring: CGPoint(x: 330, y: 520))
        await toolShot("freehand-ring-dark", scheme: .dark, ring: CGPoint(x: 330, y: 520))
        await toolShot("freehand-ring-low", ring: CGPoint(x: 640, y: 740))
        await toolShot("freehand-resize", tool: .resize)
        await toolShot("freehand-rotate", tool: .rotate)
        // The crop sheet on the flipped, cropped image: mirrored as on the canvas, its crop box on the side it looks.
        let flippedStore = freehandToolStore()
        if let flipped = flippedStore.document.items.first(where: { $0.id == flippedStore.selectedID }) {
            await shoot("freehand-crop-flipped", size: CGSize(width: 900, height: 700), wait: 3, CropEditorView(item: flipped, store: flippedStore))
        }
        // The bar is always there; a note is forced through the debug timing and cleared afterwards.
        seedFreehand(sources: sources)
        func noteShot(_ name: String, size: CGSize, scheme: ColorScheme = .light, line: String) async {
            SeaNoteView.debugTiming = SeaNoteTiming(first: .zero, interval: { .seconds(3600) }, visible: .seconds(3600))
            SeaNoteView.debugLine = line
            await shoot(name, size: size, scheme: scheme, ZStack { GlassBackground(); FreehandView().padding(.top, 52) }.ignoresSafeArea().environment(FreehandStore()))
            SeaNoteView.debugTiming = nil
            SeaNoteView.debugLine = nil
        }
        await shoot("freehand-bar", size: large, ZStack { GlassBackground(); FreehandView().padding(.top, 52) }.ignoresSafeArea().environment(FreehandStore()))
        await noteShot("freehand-note", size: large, line: "Look at it upside down. Trust me.")
        await noteShot("freehand-note-dark", size: large, scheme: .dark, line: "Look at it upside down. Trust me.")
        await noteShot("freehand-1100", size: small, line: "You've been drawing for 1 hour 10 minutes. That's longer than your usual practice.")
        seedFreehand(sources: sources, greyscale: true)
        // cacheDisplay leaves SwiftUI's .grayscale out, so these go through ImageRenderer. It can't draw
        // AppKit views or run .task, hence the layers without the input view, decoded ahead of time.
        let grey = FreehandStore()
        grey.loadIfNeeded()
        for item in grey.document.items {
            _ = await RenderCache.shared.image(for: RenderRequest(url: grey.files.imageURL(item.fileName), rotation: 0, flipped: false,
                                                                  crop: item.crop, maxPixel: 1600))
        }
        func shootGrey(_ name: String, size: CGSize, scheme: ColorScheme = .light) {
            if let only, !only.contains(name) { return }
            let view = ZStack {
                Theme.ground
                HeroClock(now: .now)
                FreehandImageLayer(store: grey)
            }
            .frame(width: size.width, height: size.height)
            .environment(\.colorScheme, scheme)
            let renderer = ImageRenderer(content: view)
            renderer.scale = 2
            if let image = renderer.nsImage, let tiff = image.tiffRepresentation {
                try? NSBitmapImageRep(data: tiff)?.representation(using: .png, properties: [:])?.write(to: outDir.appending(path: "\(name).png"))
            }
            print("shot", name)
        }
        shootGrey("freehand-grey", size: large)
        shootGrey("freehand-grey-dark", size: large, scheme: .dark)
        shootGrey("freehand-grey-1100", size: small)
        if only?.contains("freehand-checks") ?? true { await freehandChecks(sources: sources); await freehandToolChecks(sources: sources) }
        if ProcessInfo.processInfo.environment["LINEWORK_FREEHAND_ONLY"] != nil { exit(0) }

        let set = ImageSet(name: "Figures")
        context.insert(set)
        let result = await ImportService.importURLs(sources, into: context, set: set)
        print("imported \(result.imported) skipped \(result.skipped)")
        let refs = (try? context.fetch(FetchDescriptor<RefImage>(sortBy: [SortDescriptor(\.importedAt)]))) ?? []
        for ref in refs {
            print("crop", ref.fileName, ref.noFigureFound, ref.cropRect)
        }

        // Sample history: sessions on scattered days, one benchmark with two attempts.
        let cal = Calendar.current
        for daysAgo in stride(from: 200, through: 0, by: -1) where daysAgo % 3 == 0 || daysAgo < 6 {
            let start = cal.date(byAdding: .day, value: -daysAgo, to: .now)!
            let seconds = [45, 420, 900, 1500, 3000][daysAgo % 5]
            context.insert(SessionRecord(id: UUID(), startedAt: start, drawingSeconds: seconds, poseCount: 10, imageIDs: []))
        }
        if let bench = refs.first {
            bench.isBenchmark = true
            bench.benchmarkMarkedAt = cal.date(byAdding: .day, value: -60, to: .now)
            let drawing = try? Storage.copyDrawing(Storage.imageURL(fileName: refs.last!.fileName))
            context.insert(BenchmarkAttempt(image: bench, date: cal.date(byAdding: .day, value: -30, to: .now)!, sessionID: UUID(), drawingFileName: drawing))
            context.insert(BenchmarkAttempt(image: bench, date: cal.date(byAdding: .day, value: -2, to: .now)!, sessionID: UUID(), drawingFileName: nil))
        }
        try? context.save()

        await shoot("welcome", size: CGSize(width: 1100, height: 700), ZStack { GlassBackground(); WelcomeView {} }.frame(width: 1100, height: 700))
        let defaults = UserDefaults.standard
        defaults.set("Ana", forKey: "linework.name")
        let menu = MainMenuView(onOpen: { _ in }, onOpenSettings: {}).ignoresSafeArea(.container, edges: .top)
        await shoot("menu", size: CGSize(width: 1280, height: 800), menu.frame(width: 1280, height: 800))
        await shoot("menu-min", size: CGSize(width: 1100, height: 700), menu.frame(width: 1100, height: 700))
        defaults.set("Maximiliana Alexandrina de la Fuente Ruiz", forKey: "linework.name")
        await shoot("menu-longname", size: CGSize(width: 1100, height: 700), menu.frame(width: 1100, height: 700))
        defaults.set("Ana", forKey: "linework.name")
        await shoot("menu-dark", size: CGSize(width: 1280, height: 800), scheme: .dark, menu.frame(width: 1280, height: 800))
        defaults.removeObject(forKey: "linework.name")
        defaults.set(true, forKey: "linework.welcomed")
        await shoot("shell-menu", size: CGSize(width: 1280, height: 800), shell(.menu).frame(width: 1280, height: 800))
        await shoot("settings", size: CGSize(width: 420, height: 290), LineworkSettingsView())
        await shoot("about", size: CGSize(width: 420, height: 240), AboutView())

        let todayKey = PlanDayKey.key(PlanClock.now, calendar: cal)
        context.insert(PlanCheck(dayKey: todayKey, blockKey: "warmup"))
        // Ticks on recent days with no sessions behind them, to show them on the heatmap.
        for back in 1...9 where back % 2 == 1 {
            let key = PlanDayKey.key(cal.date(byAdding: .day, value: -back, to: PlanClock.now)!, calendar: cal)
            for block in ["warmup", "focus", "check"] { context.insert(PlanCheck(dayKey: key, blockKey: block)) }
        }
        try? context.save()
        await shoot("calendar-today", size: CGSize(width: 1100, height: 900), shell(.section(.calendar)))
        await shoot("today", size: CGSize(width: 1100, height: 760), shell(.section(.today)))
        await shoot("path", size: CGSize(width: 1100, height: 800), wait: 2, shell(.section(.path)))
        await shoot("path-dark", size: CGSize(width: 1100, height: 800), wait: 2, scheme: .dark, shell(.section(.path)))
        store.pendingCelebration = .week
        await shoot("celebration", size: CGSize(width: 1100, height: 760), wait: 1.2, shell(.section(.today)))
        store.pendingCelebration = nil
        await shoot("today-dark", size: CGSize(width: 1100, height: 760), scheme: .dark, shell(.section(.today)))
        await shoot("card-popover", size: CGSize(width: 520, height: 460), scheme: .light,
                    ScrollView { DayPlanCard(date: PlanClock.now, bordered: false).padding(12) })
        await shoot("guide", size: CGSize(width: 640, height: 640), PlanGuideSheet())
        await shoot("library", size: CGSize(width: 1100, height: 700), shell(.section(.library)))
        await shoot("library-dark", size: CGSize(width: 1100, height: 700), scheme: .dark, shell(.section(.library)))
        await shoot("calendar", size: CGSize(width: 1100, height: 800), shell(.section(.calendar)))
        await shoot("calendar-dark", size: CGSize(width: 1100, height: 800), scheme: .dark, shell(.section(.calendar)))
        await shoot("progress", size: CGSize(width: 1100, height: 800), shell(.section(.progress)))
        if let ref = refs.first {
            await shoot("crop-editor", size: CGSize(width: 900, height: 700), wait: 3, CropEditorView(image: ref))
            await shoot("preview", size: CGSize(width: 1000, height: 760), wait: 2.5,
                        ImagePreviewView(images: refs, startIndex: 0) { _ in })
            await shoot("attempt", size: CGSize(width: 520, height: 420), AttemptSheet(image: ref, number: 3, sessionID: UUID()) {})
        }
        await shoot("setup", size: CGSize(width: 520, height: 640), SessionSetupView())
        defaults.set("Class", forKey: "linework.setup.mode")
        await shoot("setup-class", size: CGSize(width: 520, height: 640), SessionSetupView())
        defaults.removeObject(forKey: "linework.setup.mode")
        // Pretend all but one image were imported earlier, then show the Today source.
        for ref in refs.dropFirst() { ref.importedAt = cal.date(byAdding: .day, value: -3, to: ref.importedAt)! }
        UserDefaults.standard.set(true, forKey: "linework.setup.today")
        await shoot("setup-today", size: CGSize(width: 520, height: 640), SessionSetupView())
        UserDefaults.standard.removeObject(forKey: "linework.setup.today")

        var rng = SystemRandomNumberGenerator()
        store.queue = QueueBuilder.build(imageIDs: refs.map(\.id), mode: .classSteps(QueueBuilder.defaultClass), shuffle: false,
                                         benchmarks: [], using: &rng)
        store.token = UUID()
        await shoot("player-getready", size: CGSize(width: 1200, height: 800), wait: 1.2, scheme: .dark, SessionPlayerView())
        await shoot("player-pose", size: CGSize(width: 1200, height: 800), wait: 5, scheme: .dark, SessionPlayerView())
        exit(0)
        #endif
    }

    #if DEBUG
    /// Writes a canvas of generated images: one plain, one rotated, one cropped and flipped, one over the clock.
    private static func seedFreehand(sources: [URL], greyscale: Bool = false) {
        let files = FreehandFiles(root: Storage.freehandDir)
        let places: [(center: CGPoint, rotation: Double, flipped: Bool, crop: CGRect)] = [
            (CGPoint(x: 220, y: 200), 0, false, CropMath.unit),
            (CGPoint(x: 980, y: 220), 20, false, CropMath.unit),
            (CGPoint(x: 240, y: 560), -12, true, CGRect(x: 0, y: 0, width: 0.6, height: 0.6)),
            (CGPoint(x: 900, y: 540), 0, false, CropMath.unit),
        ]
        var items: [FreehandItem] = []
        for (place, source) in zip(places, sources) {
            guard let size = Storage.orientedPixelSize(source) else { continue }
            let fileName = "\(UUID().uuidString).\(source.pathExtension.lowercased())"
            try? FileManager.default.copyItem(at: source, to: files.imageURL(fileName))
            let scale = 340 / max(size.width, size.height)
            items.append(FreehandItem(fileName: fileName, center: place.center,
                                      fullSize: CGSize(width: size.width * scale, height: size.height * scale),
                                      rotation: place.rotation, flipped: place.flipped, crop: place.crop))
        }
        try? files.save(FreehandDocument(greyscale: greyscale, items: items))
    }

    /// `LINEWORK_SHOTS=freehand-import`: drops every source on a live canvas and prints how long the import and decodes
    /// took and the longest the main actor went without running. Run under `/usr/bin/time -l` for peak memory.
    private static func freehandImportProbe(sources: [URL], outDir: URL) async {
        let canvas = FreehandStore()
        let size = CGSize(width: 1280, height: 800)
        let window = NSWindow(contentRect: NSRect(x: -10000, y: -10000, width: size.width, height: size.height),
                              styleMask: [.titled, .resizable, .fullSizeContentView], backing: .buffered, defer: false)
        let host = NSHostingView(rootView: FreehandView().ignoresSafeArea().environment(canvas))
        window.contentView = host
        window.orderFrontRegardless()
        try? await Task.sleep(for: .seconds(0.5))

        var longestGap = 0.0
        let ticker = Task { @MainActor in
            var last = ContinuousClock.now
            while !Task.isCancelled {
                try? await Task.sleep(for: .milliseconds(5))
                let now = ContinuousClock.now
                longestGap = max(longestGap, Double((now - last) / .milliseconds(1)))
                last = now
            }
        }
        func decodedCount() -> Int {
            canvas.document.items.filter {
                RenderCache.shared[RenderRequest(url: canvas.files.imageURL($0.fileName), rotation: 0, flipped: false,
                                                 crop: $0.crop, maxPixel: 1600).key] != nil
            }.count
        }
        let start = ContinuousClock.now
        await canvas.importFiles(sources, at: CGPoint(x: 240, y: 180))
        print("import", canvas.document.items.count, "of", sources.count, "status", canvas.status ?? "none", "after", ContinuousClock.now - start)
        print("main actor longest gap during import ms", longestGap)
        var waited = 0
        while decodedCount() < canvas.document.items.count, waited < 1200 {
            try? await Task.sleep(for: .milliseconds(100))
            waited += 1
        }
        print("decoded", decodedCount(), "of", canvas.document.items.count, "after", ContinuousClock.now - start)
        print("main actor longest gap overall ms", longestGap)
        ticker.cancel()
        canvas.flush()
        if let rep = host.bitmapImageRepForCachingDisplay(in: host.bounds) {
            host.cacheDisplay(in: host.bounds, to: rep)
            try? rep.representation(using: .png, properties: [:])?.write(to: outDir.appending(path: "freehand-import.png"))
        }
        exit(0)
    }

    /// The app target has no tests, so this drives a live `FreehandView` with synthetic events and prints a line per check.
    private static func freehandChecks(sources: [URL]) async {
        guard sources.count >= 2 else { return print("check FAIL needs two images") }
        func check(_ name: String, _ ok: Bool) { print("check", ok ? "pass" : "FAIL", name) }
        let files = FreehandFiles(root: Storage.freehandDir)
        let canvasURL = Storage.freehandDir.appending(path: "canvas.json")
        func savedDocument() -> FreehandDocument? {
            (try? Data(contentsOf: canvasURL)).flatMap { try? JSONDecoder().decode(FreehandDocument.self, from: $0) }
        }
        func imageCount() -> Int { ((try? FileManager.default.contentsOfDirectory(atPath: files.imagesDir.path)) ?? []).count }

        let canvas = FreehandStore()
        let size = CGSize(width: 1000, height: 700)
        let window = NSWindow(contentRect: NSRect(x: -10000, y: -10000, width: size.width, height: size.height),
                              styleMask: [.titled, .resizable, .fullSizeContentView], backing: .buffered, defer: false)
        window.isMovableByWindowBackground = true
        let host = NSHostingView(rootView: FreehandView().ignoresSafeArea().environment(canvas))
        window.contentView = host
        window.orderFrontRegardless()
        window.makeKey()
        try? await Task.sleep(for: .seconds(0.5))

        // Straight to the view the hit test finds: an inactive app's window keeps a first click for itself.
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
        func key(_ code: UInt16, _ text: String = "", _ flags: NSEvent.ModifierFlags = []) -> NSEvent {
            NSEvent.keyEvent(with: .keyDown, location: .zero, modifierFlags: flags, timestamp: ProcessInfo.processInfo.systemUptime,
                             windowNumber: window.windowNumber, context: nil, characters: text,
                             charactersIgnoringModifiers: text, isARepeat: false, keyCode: code)!
        }
        func topCenter() -> CGPoint { canvas.document.camera.toScreen(canvas.document.items.last!.center) }

        // Start from nothing.
        canvas.clear()
        check("clear deletes every copy", canvas.document.items.isEmpty && imageCount() == 0)
        check("viewport is the canvas area", canvas.viewport == size)

        // Import: same file twice, plus one that isn't an image.
        let scratch = FileManager.default.temporaryDirectory.appending(path: "not-an-image-\(UUID().uuidString).txt")
        try? Data("not an image".utf8).write(to: scratch)
        await canvas.importFiles([sources[0], sources[1], scratch, sources[0]], at: CGPoint(x: 300, y: 250))
        let items = canvas.document.items
        check("three placed, one skipped", items.count == 3 && canvas.status == "Skipped 1 file" && imageCount() == 3)
        check("first centered on the drop", items.first?.center == CGPoint(x: 300, y: 250))
        check("each further one 32pt down and right", items.last?.center == CGPoint(x: 364, y: 314))
        check("long edge 400", items.allSatisfy { abs(max($0.visibleSize.width, $0.visibleSize.height) - 400) < 0.001 })
        check("last one selected", canvas.selectedID == items.last?.id)
        check("two copies of one file are separate files", Set(items.map(\.fileName)).count == 3)

        // Input arrives at the input view, and that view doesn't move the window.
        let hit = host.hitTest(NSPoint(x: 300, y: size.height - 250))
        check("clicks over an image reach the input view", hit is CanvasInputView.InputNSView)
        check("input view doesn't move the window", hit?.mouseDownCanMoveWindow == false)
        check("input view is first responder", window.firstResponder is CanvasInputView.InputNSView)

        // Click and drag an image; the one underneath comes to the front.
        let frame = window.frame
        let bottomID = items[0].id
        let bottomCorner = CGPoint(x: items[0].center.x - items[0].visibleSize.width / 2 + 2,
                                   y: items[0].center.y - items[0].visibleSize.height / 2 + 2)
        mouse(.leftMouseDown, bottomCorner)
        check("mouse down selects and fronts at once", canvas.selectedID == bottomID && canvas.document.items.last?.id == bottomID)
        check("interacting during a press", canvas.isInteracting)
        mouse(.leftMouseDragged, CGPoint(x: bottomCorner.x + 1, y: bottomCorner.y))
        check("a wobble doesn't move it", canvas.document.items.last?.center == items[0].center)
        mouse(.leftMouseDragged, CGPoint(x: bottomCorner.x + 50, y: bottomCorner.y + 20))
        mouse(.leftMouseUp, CGPoint(x: bottomCorner.x + 50, y: bottomCorner.y + 20))
        let moved = canvas.document.items.last!.center
        check("drag moves the image", moved == CGPoint(x: items[0].center.x + 50, y: items[0].center.y + 20))
        check("interaction ends on mouse up", !canvas.isInteracting)
        check("window didn't move", window.frame == frame)

        // Debounce: nothing yet, then written.
        check("not saved straight away", savedDocument()?.items.last?.center != moved)
        try? await Task.sleep(for: .seconds(0.8))
        check("saved after the delay", savedDocument()?.items.last?.center == moved)

        // Pan by dragging empty space keeps the selection; a click there clears it.
        let empty = CGPoint(x: 950, y: 650)
        let before = canvas.document.camera.offset
        mouse(.leftMouseDown, empty)
        mouse(.leftMouseDragged, CGPoint(x: empty.x - 40, y: empty.y - 30))
        mouse(.leftMouseUp, CGPoint(x: empty.x - 40, y: empty.y - 30))
        check("empty drag pans", canvas.document.camera.offset == CGPoint(x: before.x - 40, y: before.y - 30))
        check("pan keeps the selection", canvas.selectedID == bottomID)
        mouse(.leftMouseDown, empty)
        mouse(.leftMouseUp, empty)
        check("empty click clears the selection", canvas.selectedID == nil)

        // Scroll: precise as given, wheel multiplied.
        if let input = hit as? CanvasInputView.InputNSView {
            func scroll(_ units: CGScrollEventUnit) -> NSEvent {
                NSEvent(cgEvent: CGEvent(scrollWheelEvent2Source: nil, units: units, wheelCount: 2, wheel1: 3, wheel2: 2, wheel3: 0)!)!
            }
            let start = canvas.document.camera.offset
            let precise = scroll(.pixel)
            input.scrollWheel(with: precise)
            check("trackpad scroll moves by its deltas",
                  canvas.document.camera.offset == CGPoint(x: start.x + precise.scrollingDeltaX, y: start.y + precise.scrollingDeltaY))
            let afterPrecise = canvas.document.camera.offset
            let wheel = scroll(.line)
            input.scrollWheel(with: wheel)
            check("wheel scroll is multiplied",
                  !wheel.hasPreciseScrollingDeltas
                      && canvas.document.camera.offset.y == afterPrecise.y + wheel.scrollingDeltaY * 10)
        }

        // Zoom shortcuts, keeping the middle of the canvas fixed.
        let middle = CGPoint(x: size.width / 2, y: size.height / 2)
        let middleCanvas = canvas.document.camera.toCanvas(middle)
        let zoomBefore = canvas.document.camera.zoom
        _ = window.performKeyEquivalent(with: key(24, "=", .command))
        try? await Task.sleep(for: .seconds(0.1))
        check("command = zooms in by 1.25", abs(canvas.document.camera.zoom - zoomBefore * 1.25) < 1e-9)
        let centerAfter = canvas.document.camera.toScreen(middleCanvas)
        check("zoom keeps the middle fixed", abs(centerAfter.x - middle.x) < 1e-6 && abs(centerAfter.y - middle.y) < 1e-6)
        _ = window.performKeyEquivalent(with: key(27, "-", .command))
        try? await Task.sleep(for: .seconds(0.1))
        check("command - zooms back out", abs(canvas.document.camera.zoom - zoomBefore) < 1e-9)
        _ = window.performKeyEquivalent(with: key(24, "+", .command))
        try? await Task.sleep(for: .seconds(0.1))
        check("command + zooms in too", abs(canvas.document.camera.zoom - zoomBefore * 1.25) < 1e-9)
        for _ in 0..<40 { canvas.zoomStep(in: true) }
        check("zoom stops at 800%", canvas.document.camera.zoom == 8)
        canvas.magnify(by: 0.0001, at: middle)
        check("zoom stops at 10%", canvas.document.camera.zoom == 0.1)
        canvas.fit()
        check("fit never exceeds 100%", canvas.document.camera.zoom <= 1)

        // A double click on an image sets the ring point; Esc steps back one thing at a time.
        let doubleAt = topCenter()
        mouse(.leftMouseDown, doubleAt, clicks: 2)
        mouse(.leftMouseUp, doubleAt, clicks: 2)
        check("double click opens the ring", canvas.ringAt == doubleAt)
        canvas.tool = .resize
        window.sendEvent(key(53))
        check("Esc closes the ring first", canvas.ringAt == nil && canvas.tool == .resize && canvas.selectedID != nil)
        window.sendEvent(key(53))
        check("then the tool", canvas.tool == .none && canvas.selectedID != nil)
        window.sendEvent(key(53))
        check("then the selection", canvas.selectedID == nil)

        // Delete removes one copy and leaves the other's file; a key with nothing selected does nothing.
        window.sendEvent(key(51))
        check("Delete with no selection removes nothing", canvas.document.items.count == 3)
        mouse(.leftMouseDown, topCenter())
        mouse(.leftMouseUp, topCenter())
        let deleting = canvas.document.items.last!
        window.sendEvent(key(51))
        check("Delete removes the selected image", !canvas.document.items.contains { $0.id == deleting.id })
        check("and saves before the file goes", savedDocument()?.items.contains { $0.id == deleting.id } == false
              && !FileManager.default.fileExists(atPath: files.imageURL(deleting.fileName).path))
        check("the other images keep their files",
              canvas.document.items.allSatisfy { FileManager.default.fileExists(atPath: files.imageURL($0.fileName).path) })

        // Failed saves show until one works, and a removal keeps its file until then.
        let attributes: (Int) -> [FileAttributeKey: Any] = { [.posixPermissions: $0] }
        try? FileManager.default.setAttributes(attributes(0o555), ofItemAtPath: Storage.freehandDir.path)
        let keptFile = canvas.document.items.last!
        canvas.remove(keptFile.id)
        check("failed save shows", canvas.status != nil)
        try? await Task.sleep(for: .seconds(4.2))
        check("failure outlasts the skip note", canvas.status == "Couldn't save your canvas")
        check("removal keeps the file when the save failed",
              FileManager.default.fileExists(atPath: files.imageURL(keptFile.fileName).path))
        try? FileManager.default.setAttributes(attributes(0o755), ofItemAtPath: Storage.freehandDir.path)
        canvas.flush()
        check("a later save clears it", canvas.status == nil)

        // Quitting saves a pending edit straight away.
        canvas.setGreyscale(true)
        NotificationCenter.default.post(name: NSApplication.willTerminateNotification, object: nil)
        check("quit flushes the last edit", savedDocument()?.greyscale == true)

        // Reloading finds the same canvas, and cleans up the file the failed save left behind.
        let reloaded = FreehandStore()
        reloaded.loadIfNeeded()
        check("reload matches", reloaded.document == canvas.document)
        check("orphan is gone after a good load", imageCount() == canvas.document.items.count)
        window.orderOut(nil)
        try? FileManager.default.removeItem(at: scratch)
    }
    #endif
}
