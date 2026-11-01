import Foundation

public struct ClassStep: Codable, Hashable, Sendable {
    public var count: Int
    public var seconds: Int
    public init(count: Int, seconds: Int) {
        self.count = count
        self.seconds = seconds
    }
}

public enum SessionMode: Hashable, Sendable {
    case simple(seconds: Int, maxPoses: Int?)
    case classSteps([ClassStep])
}

public struct DueBenchmark: Hashable, Sendable {
    public let imageID: UUID
    public let seconds: Int
    /// Attempt number: existing attempts + 1.
    public let number: Int
    public init(imageID: UUID, seconds: Int, number: Int) {
        self.imageID = imageID
        self.seconds = seconds
        self.number = number
    }
}

public struct Pose: Hashable, Sendable {
    public let imageID: UUID
    public let seconds: Int
    public let benchmarkNumber: Int?
    /// Class step index; nil in simple mode and for benchmarks.
    public let stepIndex: Int?
    public init(imageID: UUID, seconds: Int, benchmarkNumber: Int?, stepIndex: Int?) {
        self.imageID = imageID
        self.seconds = seconds
        self.benchmarkNumber = benchmarkNumber
        self.stepIndex = stepIndex
    }
}

public struct SessionQueue: Sendable {
    public let poses: [Pose]
    public let repeatedImages: Bool
}

public enum QueueBuilder {
    public static let defaultClass: [ClassStep] = [
        .init(count: 10, seconds: 30), .init(count: 5, seconds: 60), .init(count: 2, seconds: 300),
    ]

    /// `imageIDs` play first; `fillIDs` (e.g. the rest of the library) fill any remaining poses.
    public static func build(imageIDs: [UUID], fillIDs: [UUID] = [], mode: SessionMode, shuffle: Bool,
                             benchmarks: [DueBenchmark], using rng: inout some RandomNumberGenerator) -> SessionQueue {
        let benchmarkIDs = Set(benchmarks.map(\.imageID))
        let priority = imageIDs.filter { !benchmarkIDs.contains($0) }
        let priorityIDs = Set(priority)
        let fill = fillIDs.filter { !benchmarkIDs.contains($0) && !priorityIDs.contains($0) }
        let pool = priority + fill
        var poses = benchmarks.map {
            Pose(imageID: $0.imageID, seconds: $0.seconds, benchmarkNumber: $0.number, stepIndex: nil)
        }

        // (seconds, stepIndex) for each regular pose.
        let slots: [(Int, Int?)]
        switch mode {
        case let .simple(seconds, maxPoses):
            slots = Array(repeating: (seconds, nil), count: maxPoses ?? pool.count)
        case let .classSteps(steps):
            slots = steps.enumerated().flatMap { i, step in
                Array(repeating: (step.seconds, Optional(i)), count: max(step.count, 0))
            }
        }

        guard !pool.isEmpty else { return SessionQueue(poses: poses, repeatedImages: false) }

        // First pass: priority images, then fillers; later passes mix everything.
        var order = shuffle ? priority.shuffled(using: &rng) + fill.shuffled(using: &rng) : pool
        while order.count < slots.count {
            var pass = shuffle ? pool.shuffled(using: &rng) : pool
            // Don't show the same image twice in a row across passes.
            if pass.count > 1, pass.first == order.last { pass.swapAt(0, pass.count - 1) }
            order += pass
        }
        for (slot, id) in zip(slots, order) {
            poses.append(Pose(imageID: id, seconds: slot.0, benchmarkNumber: nil, stepIndex: slot.1))
        }
        return SessionQueue(poses: poses, repeatedImages: slots.count > pool.count)
    }
}

/// Sizes a session to a target length.
public enum SessionLength {
    public static let defaultTargetSeconds = 30 * 60

    /// Pose count whose total is closest to the target (at least one).
    public static func poses(targetSeconds: Int, poseSeconds: Int) -> Int {
        max(Int((Double(targetSeconds) / Double(max(poseSeconds, 1))).rounded()), 1)
    }

    /// Scales each step's count so the class totals about `toSeconds`, keeping every step.
    public static func scaled(_ steps: [ClassStep], toSeconds target: Int) -> [ClassStep] {
        let total = steps.reduce(0) { $0 + max($1.count, 0) * $1.seconds }
        guard total > 0 else { return steps }
        let factor = Double(target) / Double(total)
        return steps.map { ClassStep(count: max(Int((Double($0.count) * factor).rounded()), 1), seconds: $0.seconds) }
    }
}
