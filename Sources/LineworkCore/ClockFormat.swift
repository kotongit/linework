import Foundation

public struct ClockParts: Equatable, Sendable {
    public let time: String
    public let seconds: String
    public let meridiem: String?

    public init(time: String, seconds: String, meridiem: String?) {
        self.time = time
        self.seconds = seconds
        self.meridiem = meridiem
    }
}

/// Built from date components with zero padding, so it never depends on locale formatting.
public enum ClockFormat {
    public static func parts(_ date: Date, use24h: Bool, calendar: Calendar) -> ClockParts {
        let c = calendar.dateComponents([.hour, .minute, .second], from: date)
        let hour = c.hour ?? 0
        let shown = use24h ? hour : (hour % 12 == 0 ? 12 : hour % 12)
        return ClockParts(
            time: "\(pad(shown)):\(pad(c.minute ?? 0))",
            seconds: pad(c.second ?? 0),
            meridiem: use24h ? nil : (hour < 12 ? "AM" : "PM")
        )
    }

    private static func pad(_ value: Int) -> String { value < 10 ? "0\(value)" : "\(value)" }
}
