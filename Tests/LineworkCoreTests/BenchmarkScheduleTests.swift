import Foundation
import Testing
@testable import LineworkCore

@Suite struct BenchmarkScheduleTests {
    let t0 = Date(timeIntervalSince1970: 1_800_000_000)

    func sessions(_ n: Int) -> [Date] { (1...max(n, 1)).prefix(n).map { t0.addingTimeInterval(Double($0) * 3600) } }

    @Test func dueAfterInterval() {
        #expect(!BenchmarkSchedule.isDue(since: t0, interval: 10, sessionDates: sessions(9)))
        #expect(BenchmarkSchedule.isDue(since: t0, interval: 10, sessionDates: sessions(10)))
        #expect(BenchmarkSchedule.sessionsSince(t0, sessionDates: [t0, t0.addingTimeInterval(-5)] + sessions(2)) == 2)
    }

    @Test func intervalZeroTreatedAsOne() {
        #expect(!BenchmarkSchedule.isDue(since: t0, interval: 0, sessionDates: []))
        #expect(BenchmarkSchedule.isDue(since: t0, interval: 0, sessionDates: sessions(1)))
    }
}
