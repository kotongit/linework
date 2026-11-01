import Foundation

public struct PlanRepeat: Codable, Hashable, Sendable {
    /// This repeat's lasting identity, which its lessons' ticks carry.
    public let number: Int
    public let afterSequenceIndex: Int
    public let weekCount: Int

    public init(number: Int = 0, afterSequenceIndex: Int, weekCount: Int) {
        self.number = number
        self.afterSequenceIndex = afterSequenceIndex
        self.weekCount = weekCount
    }
}

/// One day of practice, by its place in the plan rather than its date.
public enum PlanDayRef: Hashable, Sendable {
    /// `weekday` is 0 for Monday.
    case plan(sequenceIndex: Int, weekday: Int)
    /// The forever week repeats indefinitely, so only a date can tell its days apart.
    case forever(day: String)
}

public enum ScheduledDay: Equatable, Sendable {
    case notStarted
    /// `weekday` is 0 for Monday.
    case day(sequenceIndex: Int, weekIndex: Int, weekday: Int, isRepeat: Bool)
    case forever(weekday: Int)
}

/// Calendar-date keys ("yyyy-MM-dd") for storing progress per day.
public enum PlanDayKey {
    public static func key(_ date: Date, calendar: Calendar) -> String {
        let c = calendar.dateComponents([.year, .month, .day], from: date)
        return String(format: "%04d-%02d-%02d", c.year ?? 0, c.month ?? 0, c.day ?? 0)
    }

    public static func date(_ key: String, calendar: Calendar) -> Date? {
        let parts = key.split(separator: "-").compactMap { Int($0) }
        guard parts.count == 3 else { return nil }
        let components = DateComponents(year: parts[0], month: parts[1], day: parts[2])
        guard components.isValidDate(in: calendar) else { return nil }
        return calendar.date(from: components).map(calendar.startOfDay(for:))
    }
}

/// Maps calendar dates to plan days, taking repeated weeks into account.
public struct PlanSchedule: Sendable {
    public let plan: PracticePlan
    public let repeats: [PlanRepeat]
    public let calendar: Calendar
    public let sequence: [(weekIndex: Int, isRepeat: Bool, repeatNumber: Int)]
    private let start: Date

    public init(plan: PracticePlan, start: Date, repeats: [PlanRepeat], calendar: Calendar) {
        self.plan = plan
        self.repeats = repeats
        self.calendar = calendar
        self.start = calendar.startOfDay(for: start)
        var sequence = plan.weeks.indices.map { (weekIndex: $0, isRepeat: false, repeatNumber: 0) }
        for r in repeats where sequence.indices.contains(r.afterSequenceIndex) {
            let first = max(r.afterSequenceIndex - r.weekCount + 1, 0)
            let copies = sequence[first...r.afterSequenceIndex].map { (weekIndex: $0.weekIndex, isRepeat: true, repeatNumber: r.number) }
            sequence.insert(contentsOf: copies, at: r.afterSequenceIndex + 1)
        }
        self.sequence = sequence
    }

    private func dayOffset(_ date: Date) -> Int {
        calendar.dateComponents([.day], from: start, to: calendar.startOfDay(for: date)).day ?? 0
    }

    public func scheduled(on date: Date) -> ScheduledDay {
        let offset = dayOffset(date)
        guard offset >= 0 else { return .notStarted }
        let week = offset / 7, weekday = offset % 7
        guard sequence.indices.contains(week) else { return .forever(weekday: weekday) }
        return .day(sequenceIndex: week, weekIndex: sequence[week].weekIndex, weekday: weekday, isRepeat: sequence[week].isRepeat)
    }

    public func date(sequenceIndex: Int, weekday: Int) -> Date {
        calendar.date(byAdding: .day, value: sequenceIndex * 7 + weekday, to: start) ?? start
    }

    /// The last Sunday of the sequence.
    public var endDate: Date {
        date(sequenceIndex: sequence.count - 1, weekday: 6)
    }

    /// What's planned on a date; nil before the plan starts. The first Saturday of a month after the plan adds the ritual.
    public func day(on date: Date) -> PlanDay? {
        switch scheduled(on: date) {
        case .notStarted:
            return nil
        case let .day(_, weekIndex, weekday, _):
            return plan.weeks[weekIndex].days[weekday]
        case let .forever(weekday):
            let day = plan.foreverWeek[weekday]
            guard weekday == 5, calendar.component(.day, from: date) <= 7 else { return day }
            let ritual = PlanBlock(kind: .ritual, text: plan.monthlyRitual, minutes: 30, title: "Monthly ritual", bullets: plan.monthlyRitualBullets)
            return PlanDay(isRest: false, restText: nil, blocks: day.blocks + [ritual])
        }
    }

    public func adding(_ r: PlanRepeat) -> PlanSchedule {
        PlanSchedule(plan: plan, start: start, repeats: repeats + [r], calendar: calendar)
    }

    public func canAddRepeat(weekCount: Int, on date: Date) -> Bool {
        guard case let .day(sequenceIndex, _, _, _) = scheduled(on: date) else { return false }
        return sequenceIndex + 1 >= weekCount
    }

    /// A repeat can be undone until its first repeated date arrives.
    public func canUndoLastRepeat(today: Date) -> Bool {
        guard let last = repeats.last else { return false }
        return self.date(sequenceIndex: last.afterSequenceIndex + 1, weekday: 0) > calendar.startOfDay(for: today)
    }

    // MARK: Lessons

    private static let weekdayNames = ["Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday", "Sunday"]

    /// Where a date falls; nil before Week 1.
    public func ref(on date: Date) -> PlanDayRef? {
        switch scheduled(on: date) {
        case .notStarted: nil
        case let .day(sequenceIndex, _, weekday, _): .plan(sequenceIndex: sequenceIndex, weekday: weekday)
        case .forever: .forever(day: PlanDayKey.key(date, calendar: calendar))
        }
    }

    /// The lesson to show today. A plan that hasn't begun yet shows its first day.
    public func todayRef(_ date: Date) -> PlanDayRef {
        ref(on: date) ?? .plan(sequenceIndex: 0, weekday: 0)
    }

    public func date(of ref: PlanDayRef) -> Date {
        switch ref {
        case let .plan(sequenceIndex, weekday): date(sequenceIndex: sequenceIndex, weekday: weekday)
        case let .forever(day): PlanDayKey.date(day, calendar: calendar) ?? start
        }
    }

    public func day(for ref: PlanDayRef) -> PlanDay {
        switch ref {
        case let .plan(sequenceIndex, weekday):
            plan.weeks[sequence[sequenceIndex].weekIndex].days[weekday]
        case .forever:
            day(on: date(of: ref)) ?? PlanDay(isRest: true, restText: nil, blocks: [])
        }
    }

    public func title(for ref: PlanDayRef) -> String {
        switch ref {
        case let .plan(sequenceIndex, weekday):
            "Week \(plan.weeks[sequence[sequenceIndex].weekIndex].number) · \(Self.weekdayNames[weekday])"
        case let .forever(day):
            "Forever week · \(Self.weekdayNames[foreverWeekday(day)])"
        }
    }

    private func foreverWeekday(_ day: String) -> Int {
        let offset = PlanDayKey.date(day, calendar: calendar).map(dayOffset) ?? 0
        return ((offset % 7) + 7) % 7
    }

    /// Names one block of one lesson, so its tick stays with it however the schedule shifts.
    public func tickKey(_ ref: PlanDayRef, _ kind: PlanBlockKind) -> String {
        switch ref {
        case let .plan(sequenceIndex, weekday):
            let entry = sequence[sequenceIndex]
            let week = String(format: "%02d", plan.weeks[entry.weekIndex].number)
            let name = Self.weekdayNames[weekday].prefix(3).lowercased()
            let pass = entry.repeatNumber == 0 ? "" : "-r\(entry.repeatNumber)"
            return "w\(week)-\(name)-\(kind.rawValue)\(pass)"
        case let .forever(day):
            return "f-\(day)-\(kind.rawValue)"
        }
    }
}
