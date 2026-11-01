import AppKit
import Combine
import Foundation
import LineworkCore
import Observation

/// Hands the configured session from the setup sheet to the player window.
struct PlanBlockRef: Equatable, Sendable {
    let ref: PlanDayRef
    let kind: PlanBlockKind
}

@MainActor @Observable
final class SessionStore {
    var queue: SessionQueue?
    /// Changes on every start so an already-open player window restarts.
    var token = UUID()
    var soundOn = true
    var startGrayscale = false
    /// The plan block this session ticks when it finishes; nil for ordinary sessions.
    var planBlock: PlanBlockRef?
    var isSessionActive = false
    /// Where the main window is; commands set it too.
    var route: AppRoute = .menu
    var pendingCelebration: PlanProgress.Celebration?
    /// False when the celebrated tick belongs to an earlier day.
    var pendingCelebrationIsToday = true
    /// Today, for plan views. Kept current so an app left open overnight moves on to the new day.
    var today = PlanClock.now

    /// `isToday` says whether the finished lesson is today's, which sets the wording.
    func celebrate(_ celebration: PlanProgress.Celebration, isToday: Bool) {
        pendingCelebrationIsToday = isToday
        pendingCelebration = celebration
    }

    func refreshToday() {
        let now = PlanClock.now
        if !PlanStore.calendar.isDate(now, inSameDayAs: today) { today = now }
    }

    /// Everything that can change which day it is.
    static var dayChanges: AnyPublisher<Notification, Never> {
        let center = NotificationCenter.default
        return Publishers.MergeMany(
            center.publisher(for: .NSCalendarDayChanged),
            center.publisher(for: .NSSystemClockDidChange),
            center.publisher(for: .NSSystemTimeZoneDidChange),
            center.publisher(for: NSApplication.didBecomeActiveNotification)
        )
        .receive(on: DispatchQueue.main)
        .eraseToAnyPublisher()
    }
}
