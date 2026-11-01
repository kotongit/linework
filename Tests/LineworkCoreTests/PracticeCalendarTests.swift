import Foundation
import Testing
@testable import LineworkCore

@Suite struct PracticeCalendarTests {
    let cal: Calendar = {
        var c = Calendar(identifier: .gregorian)
        c.firstWeekday = 2
        c.timeZone = TimeZone(identifier: "America/Mexico_City")!
        return c
    }()

    func date(_ m: Int, _ d: Int, _ h: Int = 12, _ min: Int = 0) -> Date {
        cal.date(from: DateComponents(year: 2026, month: m, day: d, hour: h, minute: min))!
    }

    @Test func levels() {
        let cases = [(0, 0), (30, 1), (599, 1), (600, 2), (1199, 2), (1200, 3), (2399, 3), (2400, 4)]
        for (s, l) in cases { #expect(PracticeCalendar.level(seconds: s) == l, "seconds \(s)") }
    }

    @Test func dailyStatsBucketsLocalDay() {
        let stats = PracticeCalendar.dailyStats([(date(10, 21, 23, 50), 120), (date(10, 22, 0, 10), 300), (date(10, 22, 9), 60)], calendar: cal)
        #expect(stats.count == 2)
        let d22 = stats[cal.startOfDay(for: date(10, 22))]
        #expect(d22?.sessions == 2 && d22?.seconds == 360)
        #expect(stats[cal.startOfDay(for: date(10, 21))]?.seconds == 120)
    }

    @Test func gridShape() {
        let today = date(10, 22) // Thursday
        let g = PracticeCalendar.grid(today: today, weeks: 52, calendar: cal)
        #expect(g.count == 52)
        #expect(g.allSatisfy { $0.count == 7 })
        let last = g[51]
        #expect(last[3] == cal.startOfDay(for: today))
        #expect(last[0] == cal.startOfDay(for: date(10, 19)))
        #expect(last[4] == nil && last[6] == nil)
        #expect(g[50][6] == cal.startOfDay(for: date(10, 18)))
    }
}
