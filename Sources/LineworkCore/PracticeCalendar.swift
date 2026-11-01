import Foundation

public struct DayStat: Equatable, Sendable {
    public let day: Date
    public var sessions: Int
    public var seconds: Int
}

public enum PracticeCalendar {
    /// Lower bounds (in drawing seconds) for heatmap levels 1–4.
    public static let levelThresholds = [1, 600, 1200, 2400]

    public static func dailyStats(_ sessions: [(start: Date, seconds: Int)], calendar: Calendar) -> [Date: DayStat] {
        var stats: [Date: DayStat] = [:]
        for s in sessions {
            let day = calendar.startOfDay(for: s.start)
            stats[day, default: DayStat(day: day, sessions: 0, seconds: 0)].sessions += 1
            stats[day]!.seconds += s.seconds
        }
        return stats
    }

    public static func level(seconds: Int) -> Int {
        levelThresholds.count { seconds >= $0 }
    }

    /// `weeks` columns of 7 days (Monday…Sunday); the last column holds today, later days are nil.
    public static func grid(today: Date, weeks: Int = 52, calendar: Calendar) -> [[Date?]] {
        var cal = calendar
        cal.firstWeekday = 2
        let todayStart = cal.startOfDay(for: today)
        guard let thisMonday = cal.dateInterval(of: .weekOfYear, for: todayStart)?.start,
              let firstMonday = cal.date(byAdding: .weekOfYear, value: -(weeks - 1), to: thisMonday)
        else { return [] }
        return (0..<weeks).map { w in
            (0..<7).map { d -> Date? in
                guard let day = cal.date(byAdding: .day, value: w * 7 + d, to: firstMonday) else { return nil }
                return day > todayStart ? nil : day
            }
        }
    }
}
