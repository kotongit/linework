import Foundation
import Testing
@testable import LineworkCore

@Suite struct LineworkCopyTests {
    private func clockDate(_ hour: Int, _ minute: Int, _ second: Int) -> (Date, Calendar) {
        var calendar = Calendar(identifier: .gregorian)
        calendar.timeZone = TimeZone(identifier: "UTC")!
        let date = calendar.date(from: DateComponents(year: 2026, month: 10, day: 31, hour: hour, minute: minute, second: second))!
        return (date, calendar)
    }

    @Test func greetingHours() {
        for hour in [5, 11] { #expect(LineworkCopy.greeting(hour: hour, name: "").hasPrefix("Good morning")) }
        for hour in [12, 18] { #expect(LineworkCopy.greeting(hour: hour, name: "").hasPrefix("Good afternoon")) }
        for hour in [19, 4, 0] { #expect(LineworkCopy.greeting(hour: hour, name: "").hasPrefix("Good evening")) }
    }

    @Test func greetingName() {
        #expect(LineworkCopy.greeting(hour: 20, name: "Ana") == "Good evening, Ana. Take care of s.e.a.")
        #expect(LineworkCopy.greeting(hour: 20, name: "") == "Good evening. Take care of s.e.a.")
    }

    @Test func underGreeting() {
        #expect(LineworkCopy.underGreeting == "A few minutes of practice a day keeps s.e.a. growing — and your hand steady.")
    }

    @Test func freehandEmpty() {
        #expect(LineworkCopy.freehandEmpty == "Your canvas is empty. Drag your images here.")
    }

    @Test func celebrations() {
        #expect(LineworkCopy.celebration(.day) == "You finished today.")
        #expect(LineworkCopy.celebration(.week) == "You finished this week.")
        #expect(LineworkCopy.celebration(.book) == "You finished this book.")
    }

    @Test func celebrationsForAnEarlierDay() {
        #expect(LineworkCopy.celebration(.day, isToday: false) == "You finished that day.")
        #expect(LineworkCopy.celebration(.week, isToday: false) == "You finished that week.")
        #expect(LineworkCopy.celebration(.book, isToday: false) == "You finished that book.")
    }

    @Test func fixedStrings() {
        #expect(LineworkCopy.restDay == "Rest today. You've earned it.")
        #expect(LineworkCopy.emptyLibrary == "Drag your photos here")
        #expect(LineworkCopy.emptyBenchmarks == "Right-click a photo to make it your benchmark.")
        #expect(LineworkCopy.sessionTooShort == "That one was too short to count on your calendar.")
    }

    @Test func names() {
        #expect(LineworkCopy.normalizedName("  Ana  ") == "Ana")
        #expect(LineworkCopy.normalizedName(String(repeating: "a", count: 60)).count == 40)
        #expect(LineworkCopy.normalizedName("   \n ") == "")
    }

    @Test func welcome() {
        #expect(LineworkCopy.shouldShowWelcome(name: "", welcomed: false))
        #expect(!LineworkCopy.shouldShowWelcome(name: "", welcomed: true))
        #expect(!LineworkCopy.shouldShowWelcome(name: "Ana", welcomed: false))
    }

    @Test func tint() {
        for (transparency, expected) in [(0.0, 1.0), (0.33, 0.67), (0.8, 0.2), (2.0, 0.2), (-1.0, 1.0)] {
            #expect(abs(Glass.tintOpacity(transparency: transparency) - expected) < 1e-9)
        }
    }

    @Test func clock24() {
        let (evening, calendar) = clockDate(20, 44, 13)
        #expect(ClockFormat.parts(evening, use24h: true, calendar: calendar) == ClockParts(time: "20:44", seconds: "13", meridiem: nil))
        let (night, _) = clockDate(0, 5, 9)
        #expect(ClockFormat.parts(night, use24h: true, calendar: calendar) == ClockParts(time: "00:05", seconds: "09", meridiem: nil))
    }

    @Test func clock12() {
        let (evening, calendar) = clockDate(20, 44, 13)
        #expect(ClockFormat.parts(evening, use24h: false, calendar: calendar) == ClockParts(time: "08:44", seconds: "13", meridiem: "PM"))
        let (night, _) = clockDate(0, 5, 9)
        #expect(ClockFormat.parts(night, use24h: false, calendar: calendar) == ClockParts(time: "12:05", seconds: "09", meridiem: "AM"))
        let (noon, _) = clockDate(12, 0, 0)
        #expect(ClockFormat.parts(noon, use24h: false, calendar: calendar) == ClockParts(time: "12:00", seconds: "00", meridiem: "PM"))
    }
}
