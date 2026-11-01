import AppKit
import LineworkCore
import Observation

/// Owns the Freehand canvas: the document, its edits, the debounced save and file imports.
/// All points are in the canvas area's top-left coordinates, in screen points.
///
/// One canvas folder is open at a time; the rest wait in `Canvases/`. Loads on first appearing, through `loadIfNeeded()`.
@MainActor @Observable
final class FreehandStore {
    enum Tool: Equatable { case none, resize, rotate }

    private enum Drag {
        case pan
        case item(UUID)
        /// `grab` is how far from the corner the press landed, so the corner doesn't jump to the pointer.
        case resize(UUID, FreehandGeometry.Corner, grab: CGSize)
        case rotate(UUID)
    }

    /// Hit area of a handle: 24pt across, at any zoom.
    static let handleHitSize = 24.0

    private(set) var document = FreehandDocument.empty
    var selectedID: UUID?
    var tool: Tool = .none
    /// Where the circular menu is open, in screen points.
    var ringAt: CGPoint?
    var viewport: CGSize = .zero
    /// The floating bar's width, which the circular menu keeps clear of.
    var barWidth = 0.0
    /// The image whose crop sheet is open.
    var cropID: UUID?
    /// Bumped when the canvas should take the keyboard back, e.g. after the crop sheet closes.
    private(set) var focusTick = 0

    /// What the floating bar should say. A skip note shows over a save failure, which returns after it.
    var status: String? { skipNote ?? (saveFailed ? "Couldn't save your canvas" : nil) }
    var isInteracting: Bool { drag != nil }

    @ObservationIgnored private(set) var files: FreehandFiles
    @ObservationIgnored let canvases: CanvasDirectory
    @ObservationIgnored private let libraryID: UUID
    private(set) var currentName = ""
    private(set) var names: [String] = []

    private var skipNote: String?
    private var saveFailed = false
    private var drag: Drag?
    @ObservationIgnored private var dragLast: CGPoint = .zero
    @ObservationIgnored private var dragStart: CGPoint = .zero
    @ObservationIgnored private var dragMoved = false
    @ObservationIgnored private var isLoaded = false
    @ObservationIgnored private var isDirty = false
    @ObservationIgnored private var saveTask: Task<Void, Never>?
    @ObservationIgnored private var skipTask: Task<Void, Never>?

    private static let saveDelay: Duration = .milliseconds(500)
    private static let skipNoteDuration: Duration = .seconds(4)
    private static let maxConcurrentImports = 4
    private static let zoomStepFactor = 1.25
    private static let newImageLongEdge = 400.0
    private static let dropStagger = 32.0
    /// Below this, a press that wobbles is still a click, not a drag.
    private static let dragThreshold = 3.0

    init(canvases: CanvasDirectory, libraryID: UUID) {
        self.canvases = canvases
        self.libraryID = libraryID
        files = FreehandFiles(root: canvases.canvasesRoot)
    }

    private var lastOpenKey: String { "linework.canvas.\(libraryID.uuidString)" }

    // MARK: - Canvases

    func refreshList() {
        names = canvases.list()
    }

    /// Shows a canvas, saving the current one first.
    func open(_ name: String) {
        if isLoaded { flush() }
        cancelDrag()
        ringAt = nil
        tool = .none
        selectedID = nil
        cropID = nil
        currentName = name
        files = FreehandFiles(root: canvases.url(of: name))
        document = files.load()
        UserDefaults.standard.set(name, forKey: lastOpenKey)
        refreshList()
    }

    func newCanvas() throws {
        open(try canvases.create(name: nil))
    }

    func renameCurrent(to name: String) throws {
        flush()
        try canvases.rename(currentName, to: name)
        let trimmed = name.trimmingCharacters(in: .whitespacesAndNewlines)
        currentName = trimmed
        files = FreehandFiles(root: canvases.url(of: trimmed))
        UserDefaults.standard.set(trimmed, forKey: lastOpenKey)
        refreshList()
    }

    func duplicateCurrent() throws {
        flush()
        open(try canvases.duplicate(currentName))
    }

    /// Moves the current canvas to the Trash, then opens another. A library always has one.
    func deleteCurrent() throws {
        isDirty = false
        saveTask?.cancel()
        try Trash.move([canvases.url(of: currentName)])
        open(try canvases.ensureOne())
    }

    func loadIfNeeded() {
        guard !isLoaded else { return }
        isLoaded = true
        let remembered = UserDefaults.standard.string(forKey: lastOpenKey)
        let list = canvases.list()
        let name = remembered.flatMap { list.contains($0) ? $0 : nil } ?? list.first ?? (try? canvases.ensureOne()) ?? "Untitled"
        open(name)
        // No queue: runs before the app exits, rather than being scheduled for a turn that may not come.
        NotificationCenter.default.addObserver(forName: NSApplication.willTerminateNotification, object: nil,
                                               queue: nil) { [weak self] _ in
            MainActor.assumeIsolated { self?.flush() }
        }
    }

    // MARK: - Pointer

    func mouseDown(at point: CGPoint, clickCount: Int) {
        ringAt = nil
        dragLast = point
        dragStart = point
        dragMoved = false
        if let grabbed = handleDrag(at: point) {
            drag = grabbed
            return
        }
        guard let id = FreehandGeometry.hit(document.items, at: document.camera.toCanvas(point)) else {
            drag = .pan
            return
        }
        if selectedID != id { tool = .none }
        selectedID = id
        if document.items.last?.id != id {
            document.items = FreehandGeometry.broughtToFront(document.items, id: id)
            scheduleSave()
        }
        if clickCount == 2 { ringAt = point }
        drag = .item(id)
    }

    func mouseDragged(to point: CGPoint, shift: Bool) {
        guard let drag else { return }
        if !dragMoved {
            guard hypot(point.x - dragStart.x, point.y - dragStart.y) >= Self.dragThreshold else { return }
            dragMoved = true
            ringAt = nil
        }
        let delta = CGSize(width: point.x - dragLast.x, height: point.y - dragLast.y)
        dragLast = point
        switch drag {
        case .pan:
            setCamera(document.camera.panned(by: delta))
        case let .resize(id, corner, grab):
            guard let index = document.items.firstIndex(where: { $0.id == id }) else { return }
            let pointer = document.camera.toCanvas(CGPoint(x: point.x - grab.width, y: point.y - grab.height))
            document.items[index] = FreehandGeometry.resized(document.items[index], corner: corner, to: pointer)
            scheduleSave()
        case let .rotate(id):
            guard let index = document.items.firstIndex(where: { $0.id == id }) else { return }
            document.items[index] = FreehandGeometry.rotated(document.items[index],
                                                             pointer: document.camera.toCanvas(point), snapping: shift)
            scheduleSave()
        case let .item(id):
            guard let index = document.items.firstIndex(where: { $0.id == id }) else { return }
            document.items[index].center.x += delta.width / document.camera.zoom
            document.items[index].center.y += delta.height / document.camera.zoom
            scheduleSave()
        }
    }

    func mouseUp() {
        if case .pan = drag, !dragMoved {
            selectedID = nil
            tool = .none
        }
        drag = nil
    }

    /// The view went away: the store outlives it, so nothing may be left open for the next visit.
    func leave() {
        cancelDrag()
        ringAt = nil
        tool = .none
        cropID = nil
        flush()
    }

    /// Ends a drag whose mouse up will never arrive, because the view went away.
    func cancelDrag() {
        drag = nil
    }

    // MARK: - Tools

    /// The handle under a press, if a tool is active: corners for resize, the rotate handle for rotate.
    private func handleDrag(at point: CGPoint) -> Drag? {
        guard tool != .none, let item = document.items.first(where: { $0.id == selectedID }) else { return nil }
        let camera = document.camera
        let reach = Self.handleHitSize / 2
        func grab(_ target: CGPoint) -> CGSize? {
            abs(point.x - target.x) <= reach && abs(point.y - target.y) <= reach
                ? CGSize(width: point.x - target.x, height: point.y - target.y) : nil
        }
        switch tool {
        case .none:
            return nil
        case .resize:
            for (corner, p) in zip(FreehandGeometry.Corner.allCases, FreehandGeometry.corners(of: item)) {
                if let offset = grab(camera.toScreen(p)) { return .resize(item.id, corner, grab: offset) }
            }
            return nil
        case .rotate:
            let handle = camera.toScreen(FreehandGeometry.rotateHandle(of: item, zoom: camera.zoom))
            return grab(handle) == nil ? nil : .rotate(item.id)
        }
    }

    /// A choice from the circular menu. It closes the menu.
    func chooseTool(_ chosen: Tool) {
        ringAt = nil
        tool = chosen
    }

    func flipSelected() {
        ringAt = nil
        guard let index = document.items.firstIndex(where: { $0.id == selectedID }) else { return }
        document.items[index] = FreehandGeometry.flipped(document.items[index])
        scheduleSave()
    }

    func beginCrop() {
        ringAt = nil
        tool = .none
        cropID = selectedID
    }

    /// Applies the crop from the sheet. The kept part stays where it was, so nothing else moves.
    func applyCrop(_ crop: CGRect) {
        guard let index = document.items.firstIndex(where: { $0.id == cropID }) else { return }
        let clamped = CropMath.clampedToUnit(crop)
        guard clamped.width > 0, clamped.height > 0 else { return }
        document.items[index] = FreehandGeometry.cropped(document.items[index], to: clamped)
        scheduleSave()
    }

    /// The crop sheet closed: end it and hand the keyboard back to the canvas.
    func endCrop() {
        cropID = nil
        focusTick += 1
    }

    // MARK: - Camera

    func scroll(by delta: CGSize) {
        setCamera(document.camera.panned(by: delta))
    }

    func magnify(by factor: Double, at point: CGPoint) {
        guard factor > 0 else { return }
        setCamera(document.camera.zoomed(by: factor, around: point))
    }

    func zoomStep(in zoomingIn: Bool) {
        let center = CGPoint(x: viewport.width / 2, y: viewport.height / 2)
        magnify(by: zoomingIn ? Self.zoomStepFactor : 1 / Self.zoomStepFactor, at: center)
    }

    func fit() {
        setCamera(FreehandGeometry.fitted(document.items, in: viewport))
    }

    private func setCamera(_ camera: FreehandCamera) {
        guard camera != document.camera else { return }
        document.camera = camera
        ringAt = nil
        scheduleSave()
    }

    // MARK: - Edits

    func setGreyscale(_ on: Bool) {
        guard on != document.greyscale else { return }
        document.greyscale = on
        scheduleSave()
    }

    func deleteSelected() {
        guard let selectedID else { return }
        remove(selectedID)
    }

    /// Removes one image and its canvas copy. Everything that removes a single image comes through here.
    func remove(_ id: UUID) {
        removeItems { $0.id == id }
    }

    func clear() {
        removeItems { _ in true }
    }

    /// Closes the circular menu, otherwise the tool, otherwise the selection.
    func escape() {
        if ringAt != nil {
            ringAt = nil
        } else if tool != .none {
            tool = .none
        } else {
            selectedID = nil
        }
    }

    /// The file goes only after a save no longer refers to it. If the save fails the file stays,
    /// and the orphan cleanup takes it once a later save has succeeded.
    private func removeItems(where shouldRemove: (FreehandItem) -> Bool) {
        let removed = document.items.filter(shouldRemove)
        guard !removed.isEmpty else { return }
        document.items.removeAll(where: shouldRemove)
        if let selectedID, removed.contains(where: { $0.id == selectedID }) {
            self.selectedID = nil
            tool = .none
            ringAt = nil
        }
        isDirty = true
        guard saveNow() else { return }
        for item in removed { files.removeImage(item.fileName) }
    }

    // MARK: - Saving

    func flush() {
        guard isDirty else { return }
        saveNow()
    }

    private func scheduleSave() {
        isDirty = true
        saveTask?.cancel()
        saveTask = Task { [weak self] in
            try? await Task.sleep(for: Self.saveDelay)
            guard !Task.isCancelled else { return }
            self?.flush()
        }
    }

    @discardableResult
    private func saveNow() -> Bool {
        saveTask?.cancel()
        do {
            try files.save(document)
            isDirty = false
            saveFailed = false
            return true
        } catch {
            saveFailed = true
            return false
        }
    }

    // MARK: - Importing

    private struct Copied: Sendable {
        let fileName: String
        let pixelSize: CGSize
    }

    /// Copies each readable image in, a few at a time and off the main actor, then places them
    /// centered on `point`, in the order given. The last one ends up on top and selected.
    func importFiles(_ urls: [URL], at point: CGPoint) async {
        let scoped = urls.filter { $0.startAccessingSecurityScopedResource() }
        defer { scoped.forEach { $0.stopAccessingSecurityScopedResource() } }

        let files = files
        // Names are settled here, so two files can't claim one.
        var taken = Set((try? FileManager.default.contentsOfDirectory(atPath: files.imagesDir.path)) ?? [])
        let targets = urls.map { url -> String in
            let name = PhotoScan.uniqueName(url.lastPathComponent, taken: taken)
            taken.insert(name)
            return name
        }
        var copied = [Copied?](repeating: nil, count: urls.count)
        await withTaskGroup(of: (Int, Copied?).self) { group in
            var pending = urls.enumerated().makeIterator()
            func addNext() {
                guard let (index, url) = pending.next() else { return }
                let target = targets[index]
                group.addTask { (index, await Self.copyIn(url, as: target, files: files)) }
            }
            for _ in 0..<Self.maxConcurrentImports { addNext() }
            for await (index, result) in group {
                copied[index] = result
                addNext()
            }
        }
        place(copied.compactMap { $0 }, at: point)
        noteSkipped(urls.count - copied.compactMap { $0 }.count)
    }

    private func place(_ images: [Copied], at point: CGPoint) {
        let camera = document.camera
        var lastID: UUID?
        for (step, image) in images.enumerated() {
            let offset = Double(step) * Self.dropStagger
            let center = camera.toCanvas(CGPoint(x: point.x + offset, y: point.y + offset))
            let scale = Self.newImageLongEdge / camera.zoom / max(image.pixelSize.width, image.pixelSize.height)
            let item = FreehandItem(fileName: image.fileName, center: center,
                                    fullSize: CGSize(width: image.pixelSize.width * scale,
                                                     height: image.pixelSize.height * scale))
            document.items.append(item)
            lastID = item.id
        }
        guard let lastID else { return }
        selectedID = lastID
        tool = .none
        ringAt = nil
        scheduleSave()
    }

    private func noteSkipped(_ count: Int) {
        guard count > 0 else { return }
        skipNote = count == 1 ? "Skipped 1 file" : "Skipped \(count) files"
        skipTask?.cancel()
        skipTask = Task { [weak self] in
            try? await Task.sleep(for: Self.skipNoteDuration)
            guard !Task.isCancelled else { return }
            self?.skipNote = nil
        }
    }

    /// Reads only the file's header for its size, so a very large image isn't decoded here.
    @concurrent
    private nonisolated static func copyIn(_ url: URL, as fileName: String, files: FreehandFiles) async -> Copied? {
        guard url.isFileURL, ImageFiles.isReadableImage(url), let size = ImageFiles.orientedPixelSize(url) else { return nil }
        do {
            try FileManager.default.copyItem(at: url, to: files.imageURL(fileName))
        } catch {
            return nil
        }
        return Copied(fileName: fileName, pixelSize: size)
    }
}
