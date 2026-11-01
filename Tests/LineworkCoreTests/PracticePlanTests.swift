import Foundation
import Testing
@testable import LineworkCore

@Suite struct PracticePlanTests {
    let plan = PracticePlan.shared

    private func steps(_ pairs: (Int, Int)...) -> [ClassStep] { pairs.map { ClassStep(count: $0.0, seconds: $0.1) } }
    private func block(_ week: Int, _ day: Int, _ kind: PlanBlockKind) -> PlanBlock? {
        plan.weeks[week - 1].days[day].blocks.first { $0.kind == kind }
    }

    @Test func shape() {
        #expect(plan.weeks.count == 38)
        #expect(plan.weeks.allSatisfy { $0.days.count == 7 })
        #expect(plan.foreverWeek.count == 7)
        #expect(plan.weeks.map(\.number) == Array(1...38))
    }

    @Test func sundaysRest() {
        for week in plan.weeks {
            let sunday = week.days[6]
            #expect(sunday.isRest && sunday.blocks.isEmpty, "week \(week.number)")
            #expect(sunday.restText?.isEmpty == false, "week \(week.number)")
        }
    }

    @Test func weekdayShape() {
        for week in plan.weeks {
            for (i, day) in week.days.prefix(5).enumerated() {
                if week.number == 1, i == 0 {
                    #expect(day.blocks.map(\.kind) == [.focus, .check])
                } else {
                    #expect(day.blocks.map(\.kind) == [.warmup, .focus, .check], "week \(week.number) day \(i)")
                    #expect(day.blocks.map(\.minutes) == [5, 20, 5], "week \(week.number) day \(i)")
                }
            }
        }
    }

    @Test func saturdayShape() {
        for week in plan.weeks {
            let kinds = week.days[5].blocks.map(\.kind)
            #expect(kinds == [.warmup, .exercise] || kinds == [.warmup, .blockA, .blockB], "week \(week.number): \(kinds)")
        }
    }

    @Test func week1Text() throws {
        let week = plan.weeks[0]
        let warmup = try #require(block(1, 1, .warmup))
        #expect(warmup.text.contains("Ten 30-s gestures with the no-menu rule"))
        #expect(!warmup.text.contains("Brush kit"))
        #expect(try #require(block(1, 1, .focus)).text.hasPrefix("Brush kit (Ch. 2)"))
        #expect(week.dailyCheck.hasPrefix("Flip the canvas"))
        #expect(week.reading.contains("Book 00"))
        #expect(week.theme == "Tools become invisible (one-week book)")
    }

    @Test func noPageFurniture() {
        for week in plan.weeks {
            for block in week.days.flatMap(\.blocks) {
                let label = "week \(week.number) \(block.kind): \(block.text.prefix(60))"
                #expect(!block.text.contains("Daily practice plan"), "\(label)")
                #expect(!block.text.contains("☐"), "\(label)")
                #expect(!block.text.contains("\n"), "\(label)")
                #expect(!block.text.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty, "\(label)")
            }
        }
    }

    @Test func bookTable() {
        var seen: [Int: Int] = [:]
        for week in plan.weeks { seen[week.book] = week.weeksInBook }
        #expect(seen == [1: 1, 2: 4, 3: 4, 4: 5, 5: 5, 6: 5, 7: 5, 8: 4, 13: 3, 14: 2])
    }

    @Test func saturdayMinutes() {
        #expect(block(1, 5, .exercise)?.minutes == 60)
        #expect(block(11, 5, .exercise)?.minutes == 45)
        #expect(block(23, 5, .blockA)?.minutes == 30)
        #expect(block(23, 5, .blockB)?.minutes == 30)
    }

    @Test func timedParsing() {
        #expect(block(1, 1, .warmup)?.timed == steps((10, 30)))
        #expect(block(2, 0, .warmup)?.timed == nil)
        #expect(block(2, 0, .focus)?.timed == steps((20, 60)))
        #expect(block(2, 4, .focus)?.timed == steps((10, 30), (10, 60)))
        #expect(block(23, 0, .warmup)?.timed == steps((6, 30), (2, 60)))
        let classThenPose = plan.weeks.flatMap { $0.days.flatMap(\.blocks) }.first { $0.text.hasPrefix("Short class mode") }
        #expect(classThenPose?.timed == steps((10, 30), (5, 60), (1, 300)))
        #expect(block(23, 2, .warmup)?.timed == steps((5, 60)))
        #expect(block(23, 3, .warmup)?.timed == steps((10, 30)))
        #expect(block(23, 4, .warmup)?.timed == nil)
        for block in plan.weeks.flatMap({ $0.days.flatMap(\.blocks) }) where block.text.contains("own hand") {
            #expect(block.timed == nil, "\(block.text.prefix(60))")
        }
    }

    @Test func dailyChecksAreWhole() {
        for week in plan.weeks {
            #expect(week.dailyCheck.hasSuffix(".") || week.dailyCheck.hasSuffix("?"), "week \(week.number): \(week.dailyCheck)")
        }
    }

    static func everyPlanString() -> [String] {
        let plan = PracticePlan.shared
        var out = [plan.guideText, plan.monthlyRitual] + plan.monthlyRitualBullets
        let days = plan.weeks.flatMap(\.days) + plan.foreverWeek
        for week in plan.weeks { out += [week.theme, week.reading, week.readingShort, week.dailyCheck] }
        for day in days {
            out += day.restText.map { [$0] } ?? []
            for block in day.blocks { out += [block.text, block.title] + block.bullets }
        }
        return out
    }

    @Test func planNamesNoCalendarDates() throws {
        let months = "Jan|Feb|Mar|Apr|May|Jun|Jul|Aug|Sep|Oct|Nov|Dec"
        let dated = try Regex("\\b(\(months))[a-z]* \\d{1,4}\\b|\\b\\d{1,2} (\(months))\\b|\\b20\\d\\d\\b")
        let strings = Self.everyPlanString()
        for text in strings { #expect(text.firstMatch(of: dated) == nil, "\(text.prefix(80))") }
        #expect(strings.contains { $0.contains("Day 29") })
    }

    @Test func benchmarkRedrawsNameTheirWeek() {
        #expect(block(5, 5, .exercise)?.text.contains("Week 2") == true)
        #expect(block(9, 5, .exercise)?.text.contains("Week 6") == true || block(9, 5, .blockA)?.text.contains("Week 6") == true)
    }

    @Test func guideUsesWeekRanges() {
        #expect(plan.guideText.contains("Weeks 2–5"))
        #expect(plan.guideText.contains("Weeks 37–38"))
    }

    @Test func wordedCountsAreTimed() {
        #expect(block(2, 3, .focus)?.timed == steps((20, 60)))          // 12 Nov: twenty 1-min lines of action
        #expect(block(11, 4, .focus)?.timed == steps((2, 600)))         // 15 Jan: the chart from memory comes after
        #expect(block(15, 2, .focus)?.timed == steps((20, 45)))         // 10 Feb: twenty heads, 45 s each
        #expect(block(15, 3, .focus)?.timed == steps((20, 45)))         // 11 Feb: 15 min for twenty more heads
        #expect(block(17, 0, .focus)?.timed == steps((10, 120)))        // 22 Feb: ten studies, 2 min each
        #expect(block(19, 0, .focus)?.timed == steps((2, 600)))         // 8 Mar: two figures, 10 min each
    }

    @Test func onePhotoSeveralWaysStaysUntimed() {
        #expect(block(17, 5, .blockB)?.timed == nil)  // the same head four times
        #expect(firstBlock("Three questions")?.timed == nil)  // the same photo three times
    }

    @Test func sameStructureInheritsTheDayBefore() {
        let monday = block(5, 0, .focus)?.timed
        #expect(monday == steps((10, 30), (5, 60), (1, 300)))
        for day in 1...4 { #expect(block(5, day, .focus)?.timed == monday, "day \(day)") }
    }

    @Test func foreverClassModeIsTimed() {
        #expect(PracticePlan.shared.foreverWeek[0].blocks[0].timed == steps((6, 30), (2, 60)))
    }

    private func firstBlock(_ prefix: String) -> PlanBlock? {
        plan.weeks.flatMap { $0.days.flatMap(\.blocks) }.first { $0.text.hasPrefix(prefix) }
    }

    @Test func classModeBlocksAreTimed() {
        #expect(firstBlock("Class-mode session")?.timed?.isEmpty == false)
        #expect(firstBlock("Class mode (10 × 30 s")?.timed == steps((10, 30), (5, 60), (3, 120), (1, 300)))
    }

    @Test func classDaysInheritTheWeeksSteps() {
        let monday = block(4, 0, .focus)?.timed
        #expect(monday != nil)
        for day in 1...4 { #expect(block(4, day, .focus)?.timed == monday, "day \(day)") }
    }

    @Test func fractionalPoseMinutes() {
        let block = plan.weeks.flatMap { $0.days.flatMap(\.blocks) }.first { $0.text.contains("Ten 1.5-min poses") }
        #expect(block?.timed == steps((10, 90)))
    }

    @Test func minuteRangesUseTheFirstNumber() {
        #expect(block(37, 5, .exercise)?.text.contains("60-90 min") == true)
        #expect(block(37, 5, .exercise)?.minutes == 60)
    }

    @Test func benchmarksHaveNoStartButton() {
        for block in plan.weeks.flatMap({ $0.days.flatMap(\.blocks) }) where block.text.hasPrefix("Benchmark") {
            #expect(block.timed == nil, "\(block.text.prefix(60))")
        }
    }

    @Test func guideFormatting() {
        #expect(plan.guideText.contains("• Ten minutes counts."))
        #expect(!plan.guideText.contains("•\n"))
        #expect(plan.guideText.contains("\nEvery session, in this order\n"))
    }

    @Test func setupDayMinutes() {
        #expect(block(1, 0, .focus)?.minutes == 45)
    }

    @Test func foreverWeek() {
        let monday = plan.foreverWeek[0]
        #expect(monday.blocks.map(\.kind) == [.warmup, .focus, .check])
        #expect(monday.blocks[0].text.contains("5-min class mode"))
        #expect(plan.foreverWeek[6].isRest)
        #expect(plan.monthlyRitual.contains("benchmark"))
        #expect(plan.guideText.contains("Ten minutes counts"))
    }
}
