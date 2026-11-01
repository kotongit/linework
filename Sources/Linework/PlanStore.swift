import Foundation
import LineworkCore
import SwiftUI

/// Starting plan blocks as sessions. Every decision about what progress means lives in LineworkCore.
@MainActor
enum PlanStore {
    static var calendar: Calendar { .current }

    /// A class session of the block's steps: today's imports first, then random library photos. No benchmarks.
    static func sessionQueue(for block: PlanBlock, photos: [PhotoRecord]) -> SessionQueue? {
        guard let timed = block.timed, !photos.isEmpty else { return nil }
        let today = PlanDayKey.key(PlanClock.now, calendar: calendar)
        let isToday = { (photo: PhotoRecord) in PlanDayKey.key(photo.importedAt, calendar: calendar) == today }
        var rng = SystemRandomNumberGenerator()
        return QueueBuilder.build(imageIDs: photos.filter(isToday).map(\.id), fillIDs: photos.filter { !isToday($0) }.map(\.id),
                                  mode: .classSteps(timed), shuffle: true, benchmarks: [], using: &rng)
    }

    /// Starts a timed block as a session in the player window. False when there is nothing to start.
    @discardableResult
    static func start(block: PlanBlock, ref: PlanDayRef, library: LibraryStore, store: SessionStore, openWindow: OpenWindowAction) -> Bool {
        guard let queue = sessionQueue(for: block, photos: library.visiblePhotos) else { return false }
        let defaults = UserDefaults.standard
        store.queue = queue
        store.planBlock = PlanBlockRef(ref: ref, kind: block.kind)
        store.token = UUID()
        store.soundOn = defaults.object(forKey: "linework.sound") as? Bool ?? true
        store.startGrayscale = defaults.object(forKey: "linework.setup.grayscale") as? Bool ?? false
        openWindow(id: "session")
        return true
    }
}
