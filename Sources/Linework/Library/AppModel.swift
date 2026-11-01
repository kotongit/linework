import AppKit
import Foundation
import LineworkCore
import Observation

/// Which library is open, and how one gets opened. No library open means the chooser shows.
@MainActor @Observable
final class AppModel {
    enum ChooserMessage: Equatable {
        case missing(URL)
        case newerFormat
        case failed(String)
    }

    private(set) var library: LibraryStore?
    /// One per library, so there's only ever one over a canvas folder; it deletes copies no item uses.
    private(set) var freehand: FreehandStore?
    var message: ChooserMessage?
    /// A folder with other things in it, waiting for the user to agree before Linework adds to it.
    var pendingAdoption: URL?

    @ObservationIgnored private var observers: [NSObjectProtocol] = []

    init() {
        let center = NotificationCenter.default
        for name in [NSApplication.didResignActiveNotification, NSApplication.willTerminateNotification] {
            observers.append(center.addObserver(forName: name, object: nil, queue: nil) { [weak self] _ in
                MainActor.assumeIsolated {
                    self?.library?.flush()
                    self?.freehand?.flush()
                }
            })
        }
        openRemembered()
    }

    func openRemembered() {
        guard LibraryLocation.hasRemembered else { return }
        guard let url = LibraryLocation.remembered else {
            message = .failed("Linework can't find your library. It may have been deleted or be on a drive that isn't connected.")
            return
        }
        #if DEBUG
        if ProcessInfo.processInfo.environment["LINEWORK_LIBRARY"] != nil, LibraryFolder.classify(url) != .library {
            try? FileManager.default.createDirectory(at: url, withIntermediateDirectories: true)
            create(at: url)
            return
        }
        #endif
        open(url)
    }

    /// Opens whatever the chosen folder turns out to be, asking first if it holds other things.
    func open(_ url: URL) {
        switch LibraryFolder.classify(url) {
        case .library: load(url)
        case .empty: create(at: url)
        case .otherContents: pendingAdoption = url
        case .unavailable: message = .missing(url)
        case .newerFormat: message = .newerFormat
        }
    }

    func create(at url: URL) {
        do {
            try FileManager.default.createDirectory(at: url, withIntermediateDirectories: true)
            let start = LibraryFolder.monday(of: PlanClock.now, calendar: PlanStore.calendar)
            try LibraryFolder.create(at: url, weekOneStart: start, now: .now)
            load(url)
        } catch {
            message = .failed("Linework couldn't create a library there. Check that you can write to that folder, or choose another.")
        }
    }

    #if DEBUG
    /// The harness writes canvases directly, then wants them read afresh.
    func reloadFreehand() {
        guard let library else { return }
        freehand = FreehandStore(canvases: CanvasDirectory(canvasesRoot: library.canvasesDir), libraryID: library.manifest.id)
        freehand?.loadIfNeeded()
    }
    #endif

    func closeForSwitch() {
        library?.flush()
        freehand?.leave()
        freehand = nil
        library = nil
        message = nil
    }

    private func load(_ url: URL) {
        do {
            let opened = try LibraryStore(root: url)
            library = opened
            freehand = FreehandStore(canvases: CanvasDirectory(canvasesRoot: opened.canvasesDir), libraryID: opened.manifest.id)
            LibraryLocation.remember(url)
            message = nil
        } catch LibraryError.newerFormat {
            message = .newerFormat
        } catch {
            message = .failed("Linework couldn't open that library.")
        }
    }
}
