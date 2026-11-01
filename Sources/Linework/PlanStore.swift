import Foundation
import LineworkCore
import SwiftData
import SwiftUI

/// Reads and writes plan progress. Every decision about what it means lives in LineworkCore.
@MainActor
enum PlanStore {
    static var calendar: Calendar { .current }

    static func schedule(_ records: [PlanRepeatRecord]) -> PlanSchedule {
        let repeats = records.sorted { $0.createdAt < $1.createdAt }
            .map { PlanRepeat(afterSequenceIndex: $0.afterSequenceIndex, weekCount: $0.weekCount) }
        return PlanSchedule(plan: .shared, repeats: repeats, calendar: calendar)
    }

    static func checksByDay(_ checks: [PlanCheck]) -> [String: Set<String>] {
        var byDay: [String: Set<String>] = [:]
        for check in checks { byDay[check.dayKey, default: []].insert(check.blockKey) }
        return byDay
    }

    /// A practice day has a recorded session or at least one tick.
    static func practiceDays(checks: [PlanCheck], sessions: [SessionRecord]) -> Set<Date> {
        var days = Set(sessions.map { calendar.startOfDay(for: $0.startedAt) })
        for check in checks {
            if let day = PlanDayKey.date(check.dayKey, calendar: calendar) { days.insert(day) }
        }
        return days
    }

    /// Today and past dates can be ticked. Keys sort chronologically.
    static func canEdit(dayKey: String, today: Date) -> Bool {
        dayKey <= PlanDayKey.key(today, calendar: calendar)
    }

    private static func rows(dayKey: String, kind: PlanBlockKind, in context: ModelContext) -> [PlanCheck] {
        let block = kind.rawValue
        let descriptor = FetchDescriptor<PlanCheck>(predicate: #Predicate { $0.dayKey == dayKey && $0.blockKey == block })
        return (try? context.fetch(descriptor)) ?? []
    }

    /// Returns true if the block is now ticked.
    @discardableResult
    static func toggle(dayKey: String, kind: PlanBlockKind, in context: ModelContext) -> Bool {
        let existing = rows(dayKey: dayKey, kind: kind, in: context)
        existing.forEach(context.delete)
        if existing.isEmpty { context.insert(PlanCheck(dayKey: dayKey, blockKey: kind.rawValue)) }
        try? context.save()
        return existing.isEmpty
    }

    /// Ticks the block if it isn't already. Returns true if this added the tick.
    @discardableResult
    static func ensureChecked(dayKey: String, kind: PlanBlockKind, in context: ModelContext) -> Bool {
        guard rows(dayKey: dayKey, kind: kind, in: context).isEmpty else { return false }
        context.insert(PlanCheck(dayKey: dayKey, blockKey: kind.rawValue))
        try? context.save()
        return true
    }

    /// What a tick that was just added completes. A new tick means the day wasn't complete before it.
    static func celebration(dayKey: String, in context: ModelContext) -> PlanProgress.Celebration? {
        guard let date = PlanDayKey.date(dayKey, calendar: calendar) else { return nil }
        let checks = (try? context.fetch(FetchDescriptor<PlanCheck>())) ?? []
        return PlanProgress.celebration(on: date, schedule: schedule(allRepeats(in: context)),
                                        checksByDay: checksByDay(checks), calendar: calendar)
    }

    static func allRepeats(in context: ModelContext) -> [PlanRepeatRecord] {
        (try? context.fetch(FetchDescriptor<PlanRepeatRecord>(sortBy: [SortDescriptor(\.createdAt)]))) ?? []
    }

    /// Repeats the last `weekCount` weeks, right after the week containing `date`.
    static func addRepeat(weekCount: Int, on date: Date, in context: ModelContext) {
        guard case let .day(sequenceIndex, _, _, _) = schedule(allRepeats(in: context)).scheduled(on: date) else { return }
        context.insert(PlanRepeatRecord(afterSequenceIndex: sequenceIndex, weekCount: weekCount))
        try? context.save()
    }

    static func undoLastRepeat(in context: ModelContext) {
        guard let last = allRepeats(in: context).last else { return }
        context.delete(last)
        try? context.save()
    }

    /// A class session of the block's steps: today's imports first, then random library images. No benchmarks.
    static func sessionQueue(for block: PlanBlock, images: [RefImage]) -> SessionQueue? {
        guard let timed = block.timed, !images.isEmpty else { return nil }
        let today = PlanDayKey.key(PlanClock.now, calendar: calendar)
        let isToday = { (ref: RefImage) in PlanDayKey.key(ref.importedAt, calendar: calendar) == today }
        var rng = SystemRandomNumberGenerator()
        return QueueBuilder.build(imageIDs: images.filter(isToday).map(\.id), fillIDs: images.filter { !isToday($0) }.map(\.id),
                                  mode: .classSteps(timed), shuffle: true, benchmarks: [], using: &rng)
    }

    /// Starts a timed block as a session in the player window. False when there is nothing to start.
    @discardableResult
    static func start(block: PlanBlock, dayKey: String, images: [RefImage], store: SessionStore,
                      openWindow: OpenWindowAction) -> Bool {
        guard let queue = sessionQueue(for: block, images: images) else { return false }
        let defaults = UserDefaults.standard
        store.queue = queue
        store.planBlock = PlanBlockRef(dayKey: dayKey, kind: block.kind)
        store.token = UUID()
        store.soundOn = defaults.object(forKey: "linework.sound") as? Bool ?? true
        store.startGrayscale = defaults.object(forKey: "linework.setup.grayscale") as? Bool ?? false
        openWindow(id: "session")
        return true
    }
}
