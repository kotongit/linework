import Foundation

public enum DurationFormat {
    /// Pose length label: "30s", "1 min", "1m 30s".
    public static func short(_ seconds: Int) -> String {
        let m = seconds / 60, s = seconds % 60
        if m == 0 { return "\(s)s" }
        return s == 0 ? "\(m) min" : "\(m)m \(s)s"
    }

    /// Countdown display, rounding up so a pose never shows 0:00 while running.
    public static func clock(_ seconds: Double) -> String {
        let total = max(Int(seconds.rounded(.up)), 0)
        return String(format: "%d:%02d", total / 60, total % 60)
    }

    /// Session total: "20s", "18 min", "1 h 5 min".
    public static func total(_ seconds: Int) -> String {
        if seconds < 60 { return "\(seconds)s" }
        let minutes = seconds / 60
        return minutes < 60 ? "\(minutes) min" : "\(minutes / 60) h \(minutes % 60) min"
    }
}
