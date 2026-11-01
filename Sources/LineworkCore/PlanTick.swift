import Foundation

/// One ticked block of one lesson. `countsOn` is the day it adds to the streak and calendar.
public struct PlanTick: Codable, Hashable, Sendable {
    public let key: String
    public let countsOn: String
    public let checkedAt: Date

    public init(key: String, countsOn: String, checkedAt: Date) {
        self.key = key
        self.countsOn = countsOn
        self.checkedAt = checkedAt
    }

    /// The lesson's scheduled day or today, whichever is earlier: filling in a missed day counts on that day,
    /// and working ahead counts today, never on a day that hasn't happened.
    public static func countsOn(scheduled: Date, today: Date, calendar: Calendar) -> String {
        PlanDayKey.key(min(calendar.startOfDay(for: scheduled), calendar.startOfDay(for: today)), calendar: calendar)
    }
}
