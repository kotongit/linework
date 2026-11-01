import Foundation

/// Every line the app says to the user, in one place so the voice stays consistent and tested.
public enum LineworkCopy {
    public static let underGreeting = "A few minutes of practice a day keeps s.e.a. growing — and your hand steady."
    public static let freehandEmpty = "Your canvas is empty. Drag your images here."
    public static let restDay = "Rest today. You've earned it."
    public static let noLessonThatDay = "No lesson that day."
    public static let emptyLibrary = "Drag your photos here"
    public static let emptyBenchmarks = "Right-click a photo to make it your benchmark."
    public static let sessionTooShort = "That one was too short to count on your calendar."

    public static let maxNameLength = 40

    /// Hours 5–11 are morning, 12–18 afternoon, the rest evening.
    public static func greeting(hour: Int, name: String) -> String {
        let part = switch hour {
        case 5...11: "morning"
        case 12...18: "afternoon"
        default: "evening"
        }
        let who = name.isEmpty ? "" : ", \(name)"
        return "Good \(part)\(who). Take care of s.e.a."
    }

    public static func celebration(_ kind: PlanProgress.Celebration, isToday: Bool = true) -> String {
        switch kind {
        case .day: isToday ? "You finished today." : "You finished that day."
        case .week: isToday ? "You finished this week." : "You finished that week."
        case .book: isToday ? "You finished this book." : "You finished that book."
        }
    }

    public static func normalizedName(_ raw: String) -> String {
        String(raw.trimmingCharacters(in: .whitespacesAndNewlines).prefix(maxNameLength))
    }

    public static func shouldShowWelcome(name: String, welcomed: Bool) -> Bool {
        name.isEmpty && !welcomed
    }
}
