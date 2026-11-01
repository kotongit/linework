import Foundation

/// Everything derived from ticks and sessions. Nothing here is stored, so unticking corrects it.
public enum PlanProgress {
    public enum DayStatus: Equatable, Sendable { case rest, empty, partial, complete }
    public enum Celebration: Equatable, Sendable { case day, week, book }

    public static func status(of day: PlanDay, checked: Set<String>) -> DayStatus {
        guard !day.blocks.isEmpty else { return .rest }
        let done = day.blocks.count { checked.contains($0.kind.rawValue) }
        return done == 0 ? .empty : done == day.blocks.count ? .complete : .partial
    }

    private static func isWeekComplete(_ sequenceIndex: Int, schedule: PlanSchedule, checksByDay: [String: Set<String>], calendar: Calendar) -> Bool {
        (0..<7).allSatisfy { weekday in
            let date = schedule.date(sequenceIndex: sequenceIndex, weekday: weekday)
            guard let day = schedule.day(on: date) else { return false }
            if day.isRest { return true }
            return status(of: day, checked: checksByDay[PlanDayKey.key(date, calendar: calendar)] ?? []) == .complete
        }
    }

    /// Consecutive practice days ending today, or yesterday if today has no practice yet. Sundays never break it.
    public static func currentStreak(practiceDays: Set<Date>, today: Date, calendar: Calendar) -> Int {
        var day = calendar.startOfDay(for: today)
        if !practiceDays.contains(day), let yesterday = calendar.date(byAdding: .day, value: -1, to: day) {
            day = yesterday
        }
        var count = 0
        while true {
            if practiceDays.contains(day) {
                count += 1
            } else if calendar.component(.weekday, from: day) != 1 {
                return count
            }
            guard let previous = calendar.date(byAdding: .day, value: -1, to: day) else { return count }
            day = previous
        }
    }

    public static func longestStreak(practiceDays: Set<Date>, calendar: Calendar) -> Int {
        var best = 0, run = 0
        var last: Date?
        for day in practiceDays.sorted() {
            let gap = last.flatMap { calendar.dateComponents([.day], from: $0, to: day).day }
            let bridgesSunday = gap == 2 && last.flatMap { calendar.date(byAdding: .day, value: 1, to: $0) }
                .map { calendar.component(.weekday, from: $0) == 1 } == true
            run = gap == 1 || bridgesSunday ? run + 1 : 1
            best = max(best, run)
            last = day
        }
        return best
    }

    /// The biggest thing a date's ticks complete; nil unless that day itself is complete.
    public static func celebration(on date: Date, schedule: PlanSchedule, checksByDay: [String: Set<String>], calendar: Calendar) -> Celebration? {
        guard let day = schedule.day(on: date),
              status(of: day, checked: checksByDay[PlanDayKey.key(date, calendar: calendar)] ?? []) == .complete
        else { return nil }
        guard case let .day(sequenceIndex, weekIndex, _, _) = schedule.scheduled(on: date),
              isWeekComplete(sequenceIndex, schedule: schedule, checksByDay: checksByDay, calendar: calendar)
        else { return .day }
        let book = schedule.plan.weeks[weekIndex].book
        let bookDone = schedule.plan.weeks.indices.filter { schedule.plan.weeks[$0].book == book }.allSatisfy { week in
            schedule.sequence.indices.contains { schedule.sequence[$0].weekIndex == week
                && isWeekComplete($0, schedule: schedule, checksByDay: checksByDay, calendar: calendar) }
        }
        return bookDone ? .book : .week
    }

    public static func heatmapSeconds(sessionSeconds: Int, tickedMinutes: Int) -> Int {
        max(sessionSeconds, tickedMinutes * 60)
    }
}
