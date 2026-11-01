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

    public static func heatmapSeconds(sessionSeconds: Int, tickedMinutes: Int) -> Int {
        max(sessionSeconds, tickedMinutes * 60)
    }

    // MARK: Lessons

    public static func status(for ref: PlanDayRef, ticks: Set<String>, schedule: PlanSchedule) -> DayStatus {
        let day = schedule.day(for: ref)
        let checked = Set(day.blocks.filter { ticks.contains(schedule.tickKey(ref, $0.kind)) }.map(\.kind.rawValue))
        return status(of: day, checked: checked)
    }

    private static func isWeekComplete(_ sequenceIndex: Int, ticks: Set<String>, schedule: PlanSchedule) -> Bool {
        (0..<7).allSatisfy { weekday in
            let status = status(for: .plan(sequenceIndex: sequenceIndex, weekday: weekday), ticks: ticks, schedule: schedule)
            return status == .rest || status == .complete
        }
    }

    /// The biggest thing a lesson's ticks complete; nil unless that day itself is complete.
    public static func celebration(for ref: PlanDayRef, ticks: Set<String>, schedule: PlanSchedule) -> Celebration? {
        guard status(for: ref, ticks: ticks, schedule: schedule) == .complete else { return nil }
        guard case let .plan(sequenceIndex, _) = ref, isWeekComplete(sequenceIndex, ticks: ticks, schedule: schedule) else { return .day }
        let weekIndex = schedule.sequence[sequenceIndex].weekIndex
        let book = schedule.plan.weeks[weekIndex].book
        let bookDone = schedule.plan.weeks.indices.filter { schedule.plan.weeks[$0].book == book }.allSatisfy { week in
            schedule.sequence.indices.contains { schedule.sequence[$0].weekIndex == week && isWeekComplete($0, ticks: ticks, schedule: schedule) }
        }
        return bookDone ? .book : .week
    }

    /// A practice day has a recorded session or at least one tick counting toward it.
    public static func practiceDays(ticks: [PlanTick], sessionDays: [Date], calendar: Calendar) -> Set<Date> {
        var days = Set(sessionDays.map(calendar.startOfDay(for:)))
        for tick in ticks {
            if let day = PlanDayKey.date(tick.countsOn, calendar: calendar) { days.insert(day) }
        }
        return days
    }

    /// How much was ticked on each day ticks count toward.
    public static func tickSummary(_ ticks: [PlanTick], schedule: PlanSchedule) -> [String: (count: Int, minutes: Int)] {
        var minutes: [String: Int] = [:]
        for index in schedule.sequence.indices {
            for weekday in 0..<7 {
                let ref = PlanDayRef.plan(sequenceIndex: index, weekday: weekday)
                for block in schedule.day(for: ref).blocks { minutes[schedule.tickKey(ref, block.kind)] = block.minutes }
            }
        }
        var summary: [String: (count: Int, minutes: Int)] = [:]
        for tick in ticks {
            var entry = summary[tick.countsOn] ?? (0, 0)
            entry.count += 1
            entry.minutes += minutes[tick.key] ?? foreverMinutes(tick.key, schedule: schedule)
            summary[tick.countsOn] = entry
        }
        return summary
    }

    private static func foreverMinutes(_ key: String, schedule: PlanSchedule) -> Int {
        // "f-yyyy-MM-dd-kind"
        let parts = key.split(separator: "-")
        guard parts.first == "f", parts.count == 5 else { return 0 }
        let ref = PlanDayRef.forever(day: parts[1...3].joined(separator: "-"))
        return schedule.day(for: ref).blocks.first { $0.kind.rawValue == parts[4] }?.minutes ?? 0
    }
}
