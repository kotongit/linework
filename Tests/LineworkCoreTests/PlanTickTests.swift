import Foundation
import Testing
@testable import LineworkCore

@Suite struct PlanTickTests {
    private var cal: Calendar {
        var c = Calendar(identifier: .gregorian)
        c.timeZone = TimeZone(identifier: "America/Mexico_City")!
        return c
    }
    private func d(_ key: String, _ c: Calendar? = nil) -> Date { PlanDayKey.date(key, calendar: c ?? cal)! }
    private func schedule(start: String = "2026-11-02", repeats: [PlanRepeat] = []) -> PlanSchedule {
        PlanSchedule(plan: .shared, start: d(start), repeats: repeats, calendar: cal)
    }

    @Test func countsOnEarlierDay() {
        #expect(PlanTick.countsOn(scheduled: d("2026-11-10"), today: d("2026-11-12"), calendar: cal) == "2026-11-10")
        #expect(PlanTick.countsOn(scheduled: d("2026-11-20"), today: d("2026-11-12"), calendar: cal) == "2026-11-12")
        #expect(PlanTick.countsOn(scheduled: d("2026-11-12"), today: d("2026-11-12"), calendar: cal) == "2026-11-12")
    }

    @Test func keysNameTheLesson() {
        #expect(schedule().tickKey(.plan(sequenceIndex: 2, weekday: 0), .focus) == "w03-mon-focus")
        #expect(schedule().tickKey(.plan(sequenceIndex: 0, weekday: 5), .blockA) == "w01-sat-blockA")
        #expect(schedule().tickKey(.forever(day: "2027-08-02"), .warmup) == "f-2027-08-02-warmup")
    }

    @Test func tickStaysWithLessonAfterARepeat() {
        let before = schedule()
        let key = before.tickKey(.plan(sequenceIndex: 5, weekday: 1), .focus)
        let after = before.adding(PlanRepeat(number: 1, afterSequenceIndex: 1, weekCount: 1))
        #expect(after.tickKey(.plan(sequenceIndex: 6, weekday: 1), .focus) == key)
    }

    @Test func repeatedLessonHasItsOwnKey() {
        let s = schedule(repeats: [PlanRepeat(number: 3, afterSequenceIndex: 1, weekCount: 1)])
        #expect(s.tickKey(.plan(sequenceIndex: 1, weekday: 0), .focus) == "w02-mon-focus")
        #expect(s.tickKey(.plan(sequenceIndex: 2, weekday: 0), .focus) == "w02-mon-focus-r3")
    }

    @Test func aRepeatOfARepeatKeepsLessonsApart() {
        // Week 5 repeated, then during that repeat "this week and last" copies Week 5 twice under one repeat number.
        let s = schedule(repeats: [PlanRepeat(number: 1, afterSequenceIndex: 4, weekCount: 1), PlanRepeat(number: 2, afterSequenceIndex: 5, weekCount: 2)])
        let keys = (4...7).map { s.tickKey(.plan(sequenceIndex: $0, weekday: 0), .focus) }
        #expect(Set(keys).count == 4)
        #expect(keys[0] == "w05-mon-focus")
        #expect(keys[2] == "w05-mon-focus-r2")
        #expect(keys[3] == "w05-mon-focus-r2c2")
    }

    @Test func anyStartMondayMapsWeeks() {
        let s = schedule(start: "2030-03-04")
        #expect(s.ref(on: d("2030-03-04")) == .plan(sequenceIndex: 0, weekday: 0))
        #expect(s.ref(on: d("2030-03-12")) == .plan(sequenceIndex: 1, weekday: 1))
        #expect(s.title(for: .plan(sequenceIndex: 1, weekday: 1)) == "Week 2 · Tuesday")
    }

    @Test func startStillAheadShowsWeekOneMonday() {
        let s = schedule(start: "2030-03-04")
        #expect(s.ref(on: d("2030-01-01")) == nil)
        #expect(s.todayRef(d("2030-01-01")) == .plan(sequenceIndex: 0, weekday: 0))
    }

    @Test func afterTheLastWeekIsForever() {
        let s = schedule()
        let ref = s.ref(on: d("2027-08-02"))
        #expect(ref == .forever(day: "2027-08-02"))
        #expect(s.title(for: ref!) == "Forever week · Monday")
        #expect(s.day(for: ref!).blocks.isEmpty == false)
    }

    @Test func dateOfRefRoundTrips() {
        let s = schedule()
        #expect(s.date(of: .plan(sequenceIndex: 1, weekday: 2)) == d("2026-11-11"))
        #expect(s.date(of: .forever(day: "2027-08-02")) == d("2027-08-02"))
    }

    @Test func futureTickNeverCountsOnAFutureDay() {
        let today = d("2026-11-12")
        let tick = PlanTick(key: "w10-mon-focus", countsOn: PlanTick.countsOn(scheduled: d("2027-01-04"), today: today, calendar: cal), checkedAt: today)
        let days = PlanProgress.practiceDays(ticks: [tick], sessionDays: [], calendar: cal)
        #expect(days == [today])
        #expect(PlanProgress.currentStreak(practiceDays: days, today: today, calendar: cal) == 1)
    }

    @Test func missedLessonMendsStreak() {
        let today = d("2026-11-12")
        let ticks = ["2026-11-10", "2026-11-11", "2026-11-12"].map { PlanTick(key: $0, countsOn: $0, checkedAt: today) }
        let days = PlanProgress.practiceDays(ticks: ticks, sessionDays: [], calendar: cal)
        #expect(PlanProgress.currentStreak(practiceDays: days, today: today, calendar: cal) == 3)
    }

    @Test func statusAndCelebrationReadLessonKeys() {
        let s = schedule()
        let ref = PlanDayRef.plan(sequenceIndex: 1, weekday: 0)
        let keys = s.day(for: ref).blocks.map { s.tickKey(ref, $0.kind) }
        #expect(PlanProgress.status(for: ref, ticks: [keys[0]], schedule: s) == .partial)
        #expect(PlanProgress.status(for: ref, ticks: Set(keys), schedule: s) == .complete)
        #expect(PlanProgress.celebration(for: ref, ticks: Set(keys), schedule: s) == .day)
        #expect(PlanProgress.celebration(for: ref, ticks: [keys[0]], schedule: s) == nil)
    }

    @Test func finishingAWeekAheadStillCelebrates() {
        let s = schedule()
        var keys = Set<String>()
        for weekday in 0..<7 {
            let ref = PlanDayRef.plan(sequenceIndex: 3, weekday: weekday)
            for block in s.day(for: ref).blocks { keys.insert(s.tickKey(ref, block.kind)) }
        }
        #expect(PlanProgress.celebration(for: .plan(sequenceIndex: 3, weekday: 0), ticks: keys, schedule: s) == .week)
    }

    @Test func mappingSurvivesTimeZoneAndDST() {
        for zone in ["America/Mexico_City", "America/New_York", "Asia/Tokyo"] {
            var c = Calendar(identifier: .gregorian)
            c.timeZone = TimeZone(identifier: zone)!
            let s = PlanSchedule(plan: .shared, start: d("2027-03-08", c), repeats: [], calendar: c)
            #expect(s.ref(on: d("2027-03-15", c)) == .plan(sequenceIndex: 1, weekday: 0), "\(zone)")
            let late = c.date(byAdding: .hour, value: 22, to: d("2027-03-14", c))!
            #expect(s.ref(on: late) == .plan(sequenceIndex: 0, weekday: 6), "\(zone)")
            #expect(PlanTick.countsOn(scheduled: d("2027-03-20", c), today: late, calendar: c) == "2027-03-14", "\(zone)")
        }
    }
}

@Suite struct PlanTickSummaryTests {
    private var cal: Calendar {
        var c = Calendar(identifier: .gregorian)
        c.timeZone = TimeZone(identifier: "America/Mexico_City")!
        return c
    }

    @Test func ticksAddUpByTheDayTheyCountOn() {
        let start = PlanDayKey.date("2026-11-02", calendar: cal)!
        let s = PlanSchedule(plan: .shared, start: start, repeats: [], calendar: cal)
        let ref = PlanDayRef.plan(sequenceIndex: 1, weekday: 0)
        let ticks = s.day(for: ref).blocks.map { PlanTick(key: s.tickKey(ref, $0.kind), countsOn: "2026-11-09", checkedAt: start) }
        let summary = PlanProgress.tickSummary(ticks, schedule: s)
        #expect(summary["2026-11-09"]?.count == ticks.count)
        #expect(summary["2026-11-09"]?.minutes == s.day(for: ref).blocks.reduce(0) { $0 + $1.minutes })
    }

    @Test func foreverTicksHaveMinutesToo() {
        let s = PlanSchedule(plan: .shared, start: PlanDayKey.date("2026-11-02", calendar: cal)!, repeats: [], calendar: cal)
        let ref = PlanDayRef.forever(day: "2027-08-02")
        let tick = PlanTick(key: s.tickKey(ref, .warmup), countsOn: "2027-08-02", checkedAt: .now)
        #expect(PlanProgress.tickSummary([tick], schedule: s)["2027-08-02"]?.minutes == 5)
    }

    @Test func nextTimedBlockUsesLessonKeys() {
        let s = PlanSchedule(plan: .shared, start: PlanDayKey.date("2026-11-02", calendar: cal)!, repeats: [], calendar: cal)
        let ref = PlanDayRef.plan(sequenceIndex: 0, weekday: 1)
        #expect(s.nextTimedBlock(ref, ticks: [])?.kind == .warmup)
        #expect(s.nextTimedBlock(ref, ticks: [s.tickKey(ref, .warmup)]) == nil)
    }
}
