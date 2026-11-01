import Foundation
import Testing
@testable import LineworkCore

@Suite struct PlanSummaryTests {
    var cal: Calendar {
        var c = Calendar(identifier: .gregorian)
        c.timeZone = TimeZone(identifier: "America/Mexico_City")!
        return c
    }
    let plan = PracticePlan.shared

    func date(_ y: Int, _ m: Int, _ d: Int) -> Date {
        cal.date(from: DateComponents(year: y, month: m, day: d, hour: 12))!
    }

    var schedule: PlanSchedule { PlanSchedule(plan: plan, start: PlanDayKey.date("2026-11-02", calendar: cal)!, repeats: [], calendar: cal) }

    @Test func everyBlockSummarized() {
        let days = plan.weeks.flatMap(\.days) + plan.foreverWeek
        for block in days.flatMap(\.blocks) {
            #expect(!block.title.isEmpty && block.title.count <= 28, "title: \(block.title)")
            #expect((1...3).contains(block.bullets.count), "\(block.title): \(block.bullets.count) bullets")
            for bullet in block.bullets {
                #expect(!bullet.isEmpty && bullet.count <= 40, "bullet: \(bullet)")
            }
        }
    }

    @Test func readingShortened() {
        for week in plan.weeks {
            #expect(!week.readingShort.isEmpty && week.readingShort.count <= 60 && !week.readingShort.contains("("), "\(week.readingShort)")
        }
    }

    @Test func ritualBullets() {
        #expect((1...5).contains(plan.monthlyRitualBullets.count))
        #expect(plan.monthlyRitualBullets.allSatisfy { $0.count <= 40 })
        let ritual = schedule.day(on: date(2027, 8, 7))?.blocks.last
        #expect(ritual?.kind == .ritual)
        #expect(ritual?.title == "Monthly ritual")
        #expect(ritual?.bullets == plan.monthlyRitualBullets)
    }

    @Test func fullTextKept() {
        let focus = plan.weeks[0].days[1].blocks.first { $0.kind == .focus }
        #expect(focus?.text.hasPrefix("Brush kit (Ch. 2)") == true)
    }

    @Test func exampleSummary() {
        let focus = plan.weeks[0].days[1].blocks.first { $0.kind == .focus }
        #expect(focus?.title == "Brush kit")
        #expect(focus?.bullets.contains("Set up 5 brushes") == true)
    }

    @Test func practiceLine() {
        let s = schedule
        let sunday = s.todayRef(date(2026, 11, 8))
        #expect(s.practiceLine(sunday) == "Rest")
        #expect(s.practiceLine(s.todayRef(date(2026, 11, 3))).contains("Brush kit"))
        let monday = s.todayRef(date(2026, 11, 9))
        var titles: [String] = []
        for b in s.day(for: monday).blocks where !titles.contains(b.title) { titles.append(b.title) }
        #expect(s.practiceLine(monday) == titles.joined(separator: ", "))
    }

    @Test func aPlanNotYetBegunShowsItsFirstDay() {
        let s = schedule
        #expect(s.practiceLine(s.todayRef(date(2026, 11, 1))).contains("Set up Procreate"))
    }

    @Test func nextTimed() {
        let s = schedule
        let monday = s.todayRef(date(2026, 11, 9))
        let focus = s.day(for: monday).blocks.first { $0.kind == .focus }
        #expect(focus?.timed != nil)
        #expect(s.nextTimedBlock(monday, ticks: []) == focus)
        #expect(s.nextTimedBlock(monday, ticks: [s.tickKey(monday, .focus)]) == nil)
        #expect(s.nextTimedBlock(s.todayRef(date(2026, 11, 3)), ticks: [])?.kind == .warmup)
        #expect(s.nextTimedBlock(s.todayRef(date(2026, 11, 8)), ticks: []) == nil)
    }
}
