import Foundation
import Testing
@testable import LineworkCore

@Suite struct PlanProgressTests {
    let cal: Calendar = {
        var c = Calendar(identifier: .gregorian)
        c.timeZone = TimeZone(identifier: "America/Mexico_City")!
        return c
    }()
    let plan = PracticePlan.shared
    var schedule: PlanSchedule { PlanSchedule(plan: plan, start: PlanDayKey.date("2026-11-02", calendar: cal)!, repeats: [], calendar: cal) }

    func date(_ y: Int, _ m: Int, _ d: Int) -> Date {
        cal.startOfDay(for: cal.date(from: DateComponents(year: y, month: m, day: d, hour: 12))!)
    }

    /// Every block of one sequence entry, ticked.
    func completeWeek(_ s: PlanSchedule, sequenceIndex: Int, into checks: inout [String: Set<String>]) {
        for wd in 0..<7 {
            let d = s.date(sequenceIndex: sequenceIndex, weekday: wd)
            let kinds = Set(s.day(on: d)?.blocks.map(\.kind.rawValue) ?? [])
            if !kinds.isEmpty { checks[PlanDayKey.key(d, calendar: cal)] = kinds }
        }
    }

    @Test func status() {
        let day = plan.weeks[1].days[0]
        #expect(PlanProgress.status(of: plan.weeks[1].days[6], checked: []) == .rest)
        #expect(PlanProgress.status(of: day, checked: []) == .empty)
        #expect(PlanProgress.status(of: day, checked: ["warmup"]) == .partial)
        #expect(PlanProgress.status(of: day, checked: ["warmup", "focus", "check", "extra"]) == .complete)
        #expect(PlanProgress.status(of: day, checked: ["extra"]) == .empty)
    }

    // 2026-11-07 is a Saturday, 11-08 a Sunday, 11-09 a Monday.
    @Test func streakSundayBridge() {
        let sat = date(2026, 11, 7), sun = date(2026, 11, 8), mon = date(2026, 11, 9)
        #expect(PlanProgress.currentStreak(practiceDays: [sat, mon], today: mon, calendar: cal) == 2)
        #expect(PlanProgress.currentStreak(practiceDays: [sat, sun, mon], today: mon, calendar: cal) == 3)
    }

    @Test func streakNotYetToday() {
        let days: Set = [date(2026, 11, 9), date(2026, 11, 10)]
        #expect(PlanProgress.currentStreak(practiceDays: days, today: date(2026, 11, 11), calendar: cal) == 2)
        #expect(PlanProgress.currentStreak(practiceDays: days, today: date(2026, 11, 12), calendar: cal) == 0)
    }

    @Test func streakBreak() {
        let days: Set = [date(2026, 11, 9), date(2026, 11, 11)]
        #expect(PlanProgress.currentStreak(practiceDays: days, today: date(2026, 11, 11), calendar: cal) == 1)
        let sat: Set = [date(2026, 11, 6), date(2026, 11, 7)]
        #expect(PlanProgress.currentStreak(practiceDays: sat, today: date(2026, 11, 8), calendar: cal) == 2)
    }

    @Test func longest() {
        #expect(PlanProgress.longestStreak(practiceDays: [], calendar: cal) == 0)
        let bridged: Set = [date(2026, 11, 6), date(2026, 11, 7), date(2026, 11, 9), date(2026, 11, 20)]
        #expect(PlanProgress.longestStreak(practiceDays: bridged, calendar: cal) == 3)
    }

    @Test func finishingABookIsTheBiggestCelebration() {
        let s = schedule
        var keys = Set<String>()
        for weekday in 0..<7 {
            let ref = PlanDayRef.plan(sequenceIndex: 0, weekday: weekday)
            for block in s.day(for: ref).blocks { keys.insert(s.tickKey(ref, block.kind)) }
        }
        #expect(PlanProgress.celebration(for: .plan(sequenceIndex: 0, weekday: 0), ticks: keys, schedule: s) == .book)
    }

    @Test func heatmapMax() {
        #expect(PlanProgress.heatmapSeconds(sessionSeconds: 1800, tickedMinutes: 30) == 1800)
        #expect(PlanProgress.heatmapSeconds(sessionSeconds: 300, tickedMinutes: 25) == 1500)
        #expect(PlanProgress.heatmapSeconds(sessionSeconds: 0, tickedMinutes: 0) == 0)
    }
}
