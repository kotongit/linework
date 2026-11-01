import Foundation

public enum PlanBlockKind: String, Codable, Sendable, CaseIterable {
    case warmup, focus, check, exercise, blockA, blockB, ritual
}

public struct PlanBlock: Codable, Hashable, Sendable {
    public let kind: PlanBlockKind
    public let text: String
    public let minutes: Int
    /// A short name for the checklist, e.g. "Brush kit".
    public let title: String
    /// One to three plain bullets; `text` keeps the full description.
    public let bullets: [String]
    /// Steps for a timed session from photos; nil for work done elsewhere.
    public let timed: [ClassStep]?

    public init(kind: PlanBlockKind, text: String, minutes: Int, title: String = "", bullets: [String] = [], timed: [ClassStep]? = nil) {
        self.kind = kind
        self.text = text
        self.minutes = minutes
        self.title = title
        self.bullets = bullets
        self.timed = timed
    }
}

public struct PlanDay: Codable, Hashable, Sendable {
    public let isRest: Bool
    public let restText: String?
    public let blocks: [PlanBlock]

    public init(isRest: Bool, restText: String?, blocks: [PlanBlock]) {
        self.isRest = isRest
        self.restText = restText
        self.blocks = blocks
    }
}

public struct PlanWeek: Codable, Hashable, Sendable {
    public let number: Int
    public let book: Int
    public let bookTitle: String
    public let weekOfBook: Int
    public let weeksInBook: Int
    public let theme: String
    public let reading: String
    /// A short form of `reading`, e.g. "Book 02, Ch. 1-2".
    public let readingShort: String
    public let dailyCheck: String
    /// Monday first.
    public let days: [PlanDay]
}

public struct PracticePlan: Codable, Sendable {
    public let startDate: String
    public let weeks: [PlanWeek]
    /// Monday first.
    public let foreverWeek: [PlanDay]
    public let monthlyRitual: String
    public let monthlyRitualBullets: [String]
    public let guideText: String

    /// The built-in plan. Decoding it is covered by the tests, so a failure here is a build mistake.
    public static let shared: PracticePlan = {
        do {
            return try JSONDecoder().decode(PracticePlan.self, from: Data(PracticePlanData.json.utf8))
        } catch {
            fatalError("Bad practice plan data: \(error)")
        }
    }()
}
