import AppKit
import LineworkCore
import SwiftUI

/// How long the notes wait. `live` is the real timing; a debug build can inject shorter ones.
struct SeaNoteTiming: Sendable {
    var first: Duration
    var interval: @Sendable () -> Duration
    var visible: Duration
    var recheck: Duration = .milliseconds(500)

    static let live = SeaNoteTiming(
        first: .seconds(SeaNotes.firstDelay),
        interval: {
            var rng = SystemRandomNumberGenerator()
            return .seconds(SeaNotes.nextInterval(using: &rng))
        },
        visible: .seconds(SeaNotes.visibleSeconds))
}

/// The small notes from s.e.a. A thin glass capsule that ignores clicks. Present for the whole visit, empty when
/// no note is showing; the loop in `.task` decides when one appears.
struct SeaNoteView: View {
    let store: FreehandStore

    @AppStorage("linework.notes") private var enabled = true
    @AppStorage("linework.name") private var name = ""
    @Environment(LibraryStore.self) private var library
    @Environment(\.accessibilityReduceMotion) private var reduceMotion
    @State private var shown: String?
    @State private var restarts = 0
    @State private var usual: Int?

    private struct Visit: Equatable {
        var enabled: Bool
        var restarts: Int
    }

    private static let fade = 0.4
#if DEBUG
    /// Debug builds only: timing and text for snapshots and harness checks.
    @MainActor static var debugTiming: SeaNoteTiming?
    @MainActor static var debugLine: String?
    /// The text on screen right now, for the harness.
    @MainActor static var debugShown: String?
    /// The visit seconds the latest note was chosen at.
    @MainActor static var debugVisitSeconds: Int?
#endif

    var body: some View {
        Group {
            if let shown {
                SeaNoteCapsule(text: shown)
            }
        }
        .allowsHitTesting(false)
        .onChange(of: library.sessions.map(\.drawingSeconds), initial: true) { _, seconds in
            usual = SeaNotes.usualSeconds(sessionSeconds: seconds)
        }
        // Sleep ends the visit and hides the note; waking starts a new one.
        .onReceive(NSWorkspace.shared.notificationCenter.publisher(for: NSWorkspace.willSleepNotification)) { _ in
            set(nil, animated: false)
        }
        .onReceive(NSWorkspace.shared.notificationCenter.publisher(for: NSWorkspace.didWakeNotification)) { _ in
            restarts += 1
        }
        .task(id: Visit(enabled: enabled, restarts: restarts)) { await run() }
#if DEBUG
        .onChange(of: shown) { _, text in Self.debugShown = text }
#endif
    }

    private var timing: SeaNoteTiming {
#if DEBUG
        if let injected = Self.debugTiming { return injected }
#endif
        return .live
    }

    private func set(_ line: String?, animated: Bool) {
        if animated && !reduceMotion {
            withAnimation(.easeInOut(duration: Self.fade)) { shown = line }
        } else {
            var transaction = Transaction()
            transaction.disablesAnimations = true
            withTransaction(transaction) { shown = line }
        }
    }

    /// One visit: the first delay, then a note, then a random wait, until cancelled. A new visit starts
    /// when the setting turns on or the Mac sleeps; turning it off ends the loop and clears the note at once.
    private func run() async {
        set(nil, animated: false)
        guard enabled else { return }
        // Suspending clock: time asleep is never counted, even if the wake notification is late.
        let clock = SuspendingClock()
        let start = clock.now
        var state = SeaNoteState()
        var rng = SystemRandomNumberGenerator()
        let timing = timing
        do {
            try await Task.sleep(for: timing.first, clock: clock)
            while true {
                while store.isInteracting || store.ringAt != nil { try await Task.sleep(for: timing.recheck, clock: clock) }
                let visit = Int((clock.now - start).components.seconds)
                var line = SeaNotes.next(visitSeconds: visit, name: name, usualSeconds: usual, state: &state, using: &rng)
#if DEBUG
                if let forced = Self.debugLine { line = forced }
#endif
#if DEBUG
                Self.debugVisitSeconds = visit
#endif
                set(line, animated: true)
                SoundCue.note()
                try await Task.sleep(for: timing.visible, clock: clock)
                set(nil, animated: true)
                try await Task.sleep(for: timing.interval(), clock: clock)
            }
        } catch {
            // Cancelled: the next visit, if any, clears the note itself.
        }
    }
}

/// A note from s.e.a.: a thin glass capsule, signed.
struct SeaNoteCapsule: View {
    let text: String

    var body: some View {
        Text("\(Text(text).foregroundStyle(Theme.ink))  \(Text("— s.e.a.").foregroundStyle(Theme.muted))")
            .font(Theme.font(14, .medium))
            .lineLimit(1)
            .fixedSize()
            .padding(.horizontal, 18)
            .padding(.vertical, 10)
            .background(Capsule(style: .circular).fill(.ultraThinMaterial))
            .overlay(Capsule(style: .circular).strokeBorder(Theme.hairline, lineWidth: 1))
            .transition(.opacity)
            .accessibilityElement(children: .ignore)
            .accessibilityLabel("\(text) s.e.a.")
    }
}

/// The notes during a timed session: milestones as poses are reached, encouragement in the long stretches between.
/// Quiet during countdowns and pauses. Give it a new identity per session, so the milestones start over.
struct PracticeNoteView: View {
    let poseIndex: Int
    let poseCount: Int
    let isDrawing: Bool

    @AppStorage("linework.notes") private var enabled = true
    @AppStorage("linework.name") private var name = ""
    @Environment(\.accessibilityReduceMotion) private var reduceMotion
    @State private var shown: String?
    @State private var notes = PracticeNoteState()
    @State private var hideAt: SuspendingClock.Instant?
    @State private var nextLineAt: SuspendingClock.Instant?

    private struct Moment: Equatable {
        var poseIndex: Int
        var isDrawing: Bool
        var enabled: Bool
    }

    var body: some View {
        // Always some content: in a stack, an empty view is dropped and its task never runs.
        ZStack {
            Color.clear.frame(width: 1, height: 1)
            if let shown { SeaNoteCapsule(text: shown) }
        }
        .allowsHitTesting(false)
        .task(id: Moment(poseIndex: poseIndex, isDrawing: isDrawing, enabled: enabled)) { await run() }
    }

    private func set(_ line: String?) {
        if reduceMotion {
            shown = line
        } else {
            withAnimation(.easeInOut(duration: 0.4)) { shown = line }
        }
    }

    private func run() async {
        guard enabled, isDrawing else {
            set(nil)
            hideAt = nil
            return
        }
        let clock = SuspendingClock()
        var rng = SystemRandomNumberGenerator()
        func show(_ line: String) {
            set(line)
            SoundCue.note()
            hideAt = clock.now + .seconds(SeaNotes.practiceVisibleSeconds)
            nextLineAt = clock.now + .seconds(SeaNotes.nextPracticeInterval(using: &rng))
        }
        if nextLineAt == nil { nextLineAt = clock.now + .seconds(SeaNotes.practiceFirstDelay) }
        if let milestone = SeaNotes.practiceMilestone(poseIndex: poseIndex, poseCount: poseCount, state: &notes) { show(milestone) }
        do {
            while true {
                guard let wake = [hideAt, nextLineAt].compactMap({ $0 }).min() else { return }
                try await Task.sleep(until: wake, clock: clock)
                if let hideAt, clock.now >= hideAt {
                    set(nil)
                    self.hideAt = nil
                }
                if let nextLineAt, clock.now >= nextLineAt {
                    show(SeaNotes.practiceLine(name: name, state: &notes, using: &rng))
                }
            }
        } catch {
            // Cancelled: the pose or the drawing state changed, and the next run picks up from the saved times.
        }
    }
}
