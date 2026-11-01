import Foundation
import LineworkCore

/// The current date for plan views. Debug builds can pretend it's another day with `LINEWORK_TODAY=yyyy-MM-dd`.
enum PlanClock {
    static var now: Date {
        #if DEBUG
        if let override = ProcessInfo.processInfo.environment["LINEWORK_TODAY"],
           let date = PlanDayKey.date(override, calendar: .current) {
            return date.addingTimeInterval(12 * 3600)
        }
        #endif
        return .now
    }
}
