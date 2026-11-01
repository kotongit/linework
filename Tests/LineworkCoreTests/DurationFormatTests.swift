import Testing
@testable import LineworkCore

@Suite struct DurationFormatTests {
    @Test func short() {
        #expect(DurationFormat.short(30) == "30s")
        #expect(DurationFormat.short(60) == "1 min")
        #expect(DurationFormat.short(90) == "1m 30s")
        #expect(DurationFormat.short(300) == "5 min")
    }

    @Test func clock() {
        #expect(DurationFormat.clock(65.2) == "1:06")
        #expect(DurationFormat.clock(9) == "0:09")
        #expect(DurationFormat.clock(0) == "0:00")
    }

    @Test func total() {
        #expect(DurationFormat.total(1080) == "18 min")
        #expect(DurationFormat.total(3900) == "1 h 5 min")
        #expect(DurationFormat.total(20) == "20s")
    }
}
