import Foundation

public enum RunnerPhase: Equatable, Sendable {
    case getReady(remaining: Double)
    case pose
    case awaitingAttempt
    case finished
}

public struct RunnerEvent: OptionSet, Sendable {
    public let rawValue: Int
    public init(rawValue: Int) { self.rawValue = rawValue }

    /// A whole second passed during the last 5 seconds of a pose.
    public static let tick = RunnerEvent(rawValue: 1 << 0)
    public static let poseChanged = RunnerEvent(rawValue: 1 << 1)
    public static let stepChanged = RunnerEvent(rawValue: 1 << 2)
    public static let benchmarkEnded = RunnerEvent(rawValue: 1 << 3)
    public static let finished = RunnerEvent(rawValue: 1 << 4)
    /// A countdown ended and the pose's time is running.
    public static let drawingStarted = RunnerEvent(rawValue: 1 << 5)
}

/// Timer state machine for a session; the view drives it with real elapsed time.
public struct SessionRunner: Sendable {
    public static let tickWindow = 5

    public let poses: [Pose]
    public let getReadySeconds: Double
    public private(set) var index = 0
    public private(set) var phase: RunnerPhase
    public private(set) var remaining: Double
    public private(set) var isPaused = false
    public private(set) var drawingSeconds: Double = 0
    public private(set) var poseElapsed: [Double]
    /// ← never goes below this; set past a benchmark once its attempt is recorded.
    private var minIndex = 0
    private var endedEarly = false

    public init(poses: [Pose], getReadySeconds: Double = 3) {
        self.poses = poses
        self.getReadySeconds = getReadySeconds
        phase = poses.isEmpty ? .finished : .getReady(remaining: getReadySeconds)
        remaining = Double(poses.first?.seconds ?? 0)
        poseElapsed = Array(repeating: 0, count: poses.count)
    }

    public var current: Pose? { poses.indices.contains(index) ? poses[index] : nil }

    public var shouldRecord: Bool { poseElapsed.contains { $0 >= 5 } }

    public mutating func tick(_ dt: Double) -> RunnerEvent {
        guard !isPaused else { return [] }
        switch phase {
        case let .getReady(r):
            let left = r - dt
            if left <= 0 {
                phase = .pose
                remaining = Double(poses[index].seconds)
                return .drawingStarted
            }
            phase = .getReady(remaining: left)
            return []
        case .pose:
            let before = remaining
            let spent = min(dt, before)
            remaining = before - dt
            drawingSeconds += spent
            poseElapsed[index] += spent
            var events: RunnerEvent = []
            if (1...Self.tickWindow).contains(where: { Double($0) < before && Double($0) >= remaining }) {
                events.insert(.tick)
            }
            if remaining <= 0 { events.formUnion(advance()) }
            return events
        case .awaitingAttempt, .finished:
            return []
        }
    }

    public mutating func togglePause() {
        isPaused.toggle()
    }

    public mutating func next() -> RunnerEvent {
        switch phase {
        case .pose, .getReady: advance()
        case .awaitingAttempt, .finished: []
        }
    }

    /// Goes back one pose with its full time; at the first pose (or just after a handled benchmark), restarts it.
    public mutating func previous() -> RunnerEvent {
        switch phase {
        case .getReady where index <= minIndex:
            return []
        case .pose, .getReady:
            let moved = index > minIndex
            if moved { index -= 1 }
            remaining = Double(poses[index].seconds)
            phase = .pose
            return moved ? .poseChanged : []
        case .awaitingAttempt, .finished:
            return []
        }
    }

    public mutating func attemptHandled() -> RunnerEvent {
        guard phase == .awaitingAttempt else { return [] }
        minIndex = index + 1
        return move(to: index + 1)
    }

    public mutating func end() {
        if phase != .finished { endedEarly = true }
        phase = .finished
    }

    /// The session ran to its end, rather than being ended with Esc.
    public var completedNaturally: Bool { phase == .finished && !poses.isEmpty && !endedEarly }

    private mutating func advance() -> RunnerEvent {
        if poses[index].benchmarkNumber != nil {
            phase = .awaitingAttempt
            return .benchmarkEnded
        }
        return move(to: index + 1)
    }

    private mutating func move(to i: Int) -> RunnerEvent {
        guard i < poses.count else {
            phase = .finished
            return .finished
        }
        let previousStep = poses[index].stepIndex
        index = i
        remaining = Double(poses[i].seconds)
        var events: RunnerEvent = .poseChanged
        if let step = poses[i].stepIndex, step != previousStep {
            events.insert(.stepChanged)
            phase = .getReady(remaining: getReadySeconds)
        } else {
            phase = .pose
        }
        return events
    }
}

/// Which pose images the player keeps rendered: the previous, current and next.
public enum RenderWindow {
    public static func indices(around index: Int, count: Int) -> ClosedRange<Int> {
        max(index - 1, 0)...min(index + 1, max(count - 1, 0))
    }
}
