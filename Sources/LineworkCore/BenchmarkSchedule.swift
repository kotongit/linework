import Foundation

public enum BenchmarkSchedule {
    /// Sessions that started strictly after `date`.
    public static func sessionsSince(_ date: Date, sessionDates: [Date]) -> Int {
        sessionDates.count { $0 > date }
    }

    /// Due once the sessions since the last attempt (or since marking) reach the interval.
    public static func isDue(since: Date, interval: Int, sessionDates: [Date]) -> Bool {
        sessionsSince(since, sessionDates: sessionDates) >= max(interval, 1)
    }
}
