import Foundation

/// Per-visit memory for the notes: which general lines were used lately, and which time notes were shown.
public struct SeaNoteState: Sendable {
    public init() {}
    public var recentGeneral: [String] = []
    public var shownTimeNotes: Set<String> = []
}

/// Per-session memory for the notes in a timed session.
public struct PracticeNoteState: Sendable {
    public init() {}
    public var shownMilestones: Set<String> = []
    public var recentLines: [String] = []
}

/// Fixed lines that s.e.a. says while Freehand or a timed session is open. Pure logic, nothing is saved.
public enum SeaNotes {
    public static let firstDelay = 300
    public static let visibleSeconds = 8

    public static let generalLines: [String] = [
        "You can do it.",
        "What are we drawing today?",
        "Are we there yet?",
        "Loosen your shoulders.",
        "Don't forget to blink.",
        "Have some water. I'll wait.",
        "That line was braver than you think.",
        "Ugly drawings count too.",
        "Look at it upside down. Trust me.",
        "I like this one.",
        "Still with you, {name}.",
    ]

    public static let practiceLines: [String] = [
        "You can do it.",
        "Keep your hand moving.",
        "You're doing it.",
        "Loose lines win.",
        "Ugly drawings count too.",
        "That line was braver than you think.",
        "Breathe. Then the next line.",
        "Still with you, {name}.",
    ]

    public static let practiceFirstDelay = 120
    public static let practiceVisibleSeconds = 6

    private static let repeatWindow = 5
    private static let twentyMinutes = 20 * 60
    private static let sixtyMinutes = 60 * 60
    private static let usualKey = "usual"
    private static let twentyKey = "20"
    private static let sixtyKey = "60"

    /// "25 minutes", "1 hour", "1 hour 10 minutes". Minutes round down.
    public static func timeText(seconds: Int) -> String {
        let total = max(0, seconds) / 60
        let hours = total / 60
        let minutes = total % 60
        func unit(_ n: Int, _ word: String) -> String { n == 1 ? "\(n) \(word)" : "\(n) \(word)s" }
        if hours == 0 { return unit(minutes, "minute") }
        if minutes == 0 { return unit(hours, "hour") }
        return "\(unit(hours, "hour")) \(unit(minutes, "minute"))"
    }

    /// Average session length; nil until there are at least 3 sessions.
    public static func usualSeconds(sessionSeconds: [Int]) -> Int? {
        guard sessionSeconds.count >= 3 else { return nil }
        return sessionSeconds.reduce(0, +) / sessionSeconds.count
    }

    /// Seconds until the next note after one has appeared: 600...1200.
    public static func nextInterval<R: RandomNumberGenerator>(using rng: inout R) -> Int {
        Int.random(in: 600...1200, using: &rng)
    }

    /// The next note for this visit. Time notes take priority (usual, then 60 minutes, then 20 minutes).
    public static func next<R: RandomNumberGenerator>(visitSeconds: Int, name: String, usualSeconds: Int?,
                                                       state: inout SeaNoteState, using rng: inout R) -> String {
        let time = timeText(seconds: visitSeconds)
        if let usual = usualSeconds, visitSeconds > usual, !state.shownTimeNotes.contains(usualKey) {
            state.shownTimeNotes.formUnion([usualKey, twentyKey])
            return "You've been drawing for \(time). That's longer than your usual practice."
        }
        if visitSeconds >= sixtyMinutes, !state.shownTimeNotes.contains(sixtyKey) {
            state.shownTimeNotes.formUnion([twentyKey, sixtyKey])
            return "\(time) already. Maybe stretch for a second?"
        }
        if visitSeconds >= twentyMinutes, !state.shownTimeNotes.contains(twentyKey) {
            state.shownTimeNotes.insert(twentyKey)
            return "You've been drawing for \(time)."
        }

        return pickLine(from: generalLines, name: name, recent: &state.recentGeneral, using: &rng)
    }

    /// The note for reaching a pose, once per session: the first, halfway (in longer sessions), three left, the last.
    public static func practiceMilestone(poseIndex: Int, poseCount: Int, state: inout PracticeNoteState) -> String? {
        let left = poseCount - poseIndex
        let line: String
        if poseIndex == 0 {
            line = "You just began, you can do it."
        } else if left == 1 {
            line = "Last one. Make it count."
        } else if left == 3 {
            line = "Only 3 left."
        } else if poseCount >= 8, poseIndex == poseCount / 2 {
            line = "Halfway there. Keep going."
        } else {
            return nil
        }
        return state.shownMilestones.insert(line).inserted ? line : nil
    }

    /// Seconds until the next encouragement in a session: 300...480.
    public static func nextPracticeInterval<R: RandomNumberGenerator>(using rng: inout R) -> Int {
        Int.random(in: 300...480, using: &rng)
    }

    /// Encouragement for the stretches between milestones, like a long pose.
    public static func practiceLine<R: RandomNumberGenerator>(name: String, state: inout PracticeNoteState, using rng: inout R) -> String {
        pickLine(from: practiceLines, name: name, recent: &state.recentLines, using: &rng)
    }

    private static func pickLine<R: RandomNumberGenerator>(from lines: [String], name: String, recent: inout [String], using rng: inout R) -> String {
        let cleanName = LineworkCopy.normalizedName(name)
        let candidates = lines.filter { line in
            (cleanName.isEmpty ? !line.contains("{name}") : true) && !recent.contains(line)
        }
        let pick = candidates.randomElement(using: &rng) ?? lines[0]
        recent.append(pick)
        if recent.count > repeatWindow {
            recent.removeFirst(recent.count - repeatWindow)
        }
        return pick.replacingOccurrences(of: "{name}", with: cleanName)
    }
}
