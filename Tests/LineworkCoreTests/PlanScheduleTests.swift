import Foundation
import Testing
@testable import LineworkCore

private func makeCalendar(_ zone: String) -> Calendar {
    var c = Calendar(identifier: .gregorian)
    c.timeZone = TimeZone(identifier: zone)!
    return c
}

@Suite struct PlanScheduleTests {
    let cal = makeCalendar("America/Mexico_City")
    let plan = PracticePlan.shared

    func date(_ y: Int, _ m: Int, _ d: Int, _ h: Int = 12, _ min: Int = 0, calendar: Calendar? = nil) -> Date {
        (calendar ?? cal).date(from: DateComponents(year: y, month: m, day: d, hour: h, minute: min))!
    }

    func schedule(_ repeats: [PlanRepeat] = []) -> PlanSchedule {
        PlanSchedule(plan: plan, start: PlanDayKey.date("2026-11-02", calendar: cal)!, repeats: repeats, calendar: cal)
    }

    @Test func beforeStart() {
        let s = schedule()
        #expect(s.scheduled(on: date(2026, 11, 1)) == .notStarted)
        #expect(s.day(on: date(2026, 11, 1)) == nil)
    }

    @Test func firstDay() {
        #expect(schedule().scheduled(on: date(2026, 11, 2)) == .day(sequenceIndex: 0, weekIndex: 0, weekday: 0, isRepeat: false))
    }

    @Test func sundayMapping() {
        let s = schedule()
        #expect(s.scheduled(on: date(2026, 11, 8)) == .day(sequenceIndex: 0, weekIndex: 0, weekday: 6, isRepeat: false))
        #expect(s.day(on: date(2026, 11, 8))?.isRest == true)
    }

    @Test func lastDay() {
        let s = schedule()
        #expect(s.scheduled(on: date(2027, 7, 25)) == .day(sequenceIndex: 37, weekIndex: 37, weekday: 6, isRepeat: false))
        #expect(s.scheduled(on: date(2027, 7, 26)) == .forever(weekday: 0))
        #expect(PlanDayKey.key(s.endDate, calendar: cal) == "2027-07-25")
    }

    @Test func repeatOne() {
        let s = schedule([PlanRepeat(afterSequenceIndex: 4, weekCount: 1)])
        #expect(s.scheduled(on: date(2026, 12, 6)) == .day(sequenceIndex: 4, weekIndex: 4, weekday: 6, isRepeat: false))
        #expect(s.scheduled(on: date(2026, 12, 7)) == .day(sequenceIndex: 5, weekIndex: 4, weekday: 0, isRepeat: true))
        #expect(s.scheduled(on: date(2026, 12, 14)) == .day(sequenceIndex: 6, weekIndex: 5, weekday: 0, isRepeat: false))
        #expect(PlanDayKey.key(s.endDate, calendar: cal) == "2027-08-01")
    }

    @Test func repeatTwo() {
        let s = schedule([PlanRepeat(afterSequenceIndex: 4, weekCount: 2)])
        #expect(s.sequence[5].weekIndex == 3 && s.sequence[5].isRepeat)
        #expect(s.sequence[6].weekIndex == 4 && s.sequence[6].isRepeat)
        #expect(s.sequence[7].weekIndex == 5 && !s.sequence[7].isRepeat)
        #expect(PlanDayKey.key(s.endDate, calendar: cal) == "2027-08-08")
    }

    @Test func severalRepeats() {
        let s = schedule([PlanRepeat(afterSequenceIndex: 1, weekCount: 1), PlanRepeat(afterSequenceIndex: 2, weekCount: 2)])
        #expect(s.sequence.prefix(7).map(\.weekIndex) == [0, 1, 1, 1, 1, 2, 3])
        #expect(s.sequence.count == 41)
        #expect(s.repeats.count == 2)
    }

    @Test func addingMatchesInit() {
        let r = PlanRepeat(afterSequenceIndex: 3, weekCount: 1)
        #expect(schedule().adding(r).endDate == schedule([r]).endDate)
    }

    @Test func dateForSequence() {
        let s = schedule()
        #expect(PlanDayKey.key(s.date(sequenceIndex: 1, weekday: 2), calendar: cal) == "2026-11-11")
    }

    @Test func canAdd() {
        let s = schedule()
        #expect(s.canAddRepeat(weekCount: 1, on: date(2026, 11, 3)))
        #expect(!s.canAddRepeat(weekCount: 2, on: date(2026, 11, 3)))
        #expect(s.canAddRepeat(weekCount: 2, on: date(2026, 11, 10)))
        #expect(!s.canAddRepeat(weekCount: 1, on: date(2026, 10, 31)))
        #expect(!s.canAddRepeat(weekCount: 1, on: date(2027, 8, 1)))
    }

    @Test func canUndo() {
        let none = schedule()
        #expect(!none.canUndoLastRepeat(today: date(2026, 11, 3)))
        let s = schedule([PlanRepeat(afterSequenceIndex: 0, weekCount: 1)])
        #expect(s.canUndoLastRepeat(today: date(2026, 11, 8)))   // first repeated date is Nov 9
        #expect(!s.canUndoLastRepeat(today: date(2026, 11, 9)))
    }

    @Test func foreverRitual() {
        let s = schedule()
        // 7 Aug 2027 is the first Saturday of August; 14 Aug the second.
        #expect(s.day(on: date(2027, 8, 7))?.blocks.last?.kind == .ritual)
        #expect(s.day(on: date(2027, 8, 7))?.blocks.last?.minutes == 30)
        #expect(s.day(on: date(2027, 8, 14))?.blocks.contains { $0.kind == .ritual } == false)
        #expect(s.day(on: date(2027, 8, 8))?.isRest == true)
    }

    @Test func keysRoundTrip() {
        #expect(PlanDayKey.key(date(2026, 11, 2), calendar: cal) == "2026-11-02")
        #expect(PlanDayKey.date("2026-11-02", calendar: cal) == cal.startOfDay(for: date(2026, 11, 2)))
        #expect(PlanDayKey.date("nope", calendar: cal) == nil)
        #expect(PlanDayKey.date("2026-13-45", calendar: cal) == nil)
    }

    @Test func dstStable() {
        let ny = makeCalendar("America/New_York")
        let s = PlanSchedule(plan: plan, start: PlanDayKey.date("2026-11-02", calendar: ny)!, repeats: [], calendar: ny)
        for (y, m, d) in [(2026, 11, 2), (2027, 3, 14), (2027, 3, 15)] {
            let early = date(y, m, d, 0, 30, calendar: ny), late = date(y, m, d, 23, 30, calendar: ny)
            #expect(s.scheduled(on: early) == s.scheduled(on: late), "\(y)-\(m)-\(d)")
        }
        // Every day of the plan maps to the next plan day, across both clock changes.
        var expected = 0
        var day = date(2026, 11, 2, 12, calendar: ny)
        for _ in 0..<266 {
            guard case let .day(seq, _, weekday, _) = s.scheduled(on: day) else { Issue.record("not a plan day"); return }
            #expect(seq * 7 + weekday == expected)
            expected += 1
            day = ny.date(byAdding: .day, value: 1, to: day)!
        }
    }

    @Test func timeZoneKeys() {
        // 23:30 local is already tomorrow in UTC.
        #expect(PlanDayKey.key(date(2026, 11, 2, 23, 30), calendar: cal) == "2026-11-02")
    }
}
