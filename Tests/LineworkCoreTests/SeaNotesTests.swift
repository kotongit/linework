import Foundation
import Testing
@testable import LineworkCore

private struct SeededGenerator: RandomNumberGenerator {
    var state: UInt64
    init(seed: UInt64) { state = seed }
    mutating func next() -> UInt64 {
        state &+= 0x9E3779B97F4A7C15
        var z = state
        z = (z ^ (z >> 30)) &* 0xBF58476D1CE4E5B9
        z = (z ^ (z >> 27)) &* 0x94D049BB133111EB
        return z ^ (z >> 31)
    }
}

private let timeLines: Set<String> = [
    "You've been drawing for 25 minutes.",
    "1 hour already. Maybe stretch for a second?",
]

@Suite struct SeaNotesTests {
    @Test func timeWording() {
        #expect(SeaNotes.timeText(seconds: 60) == "1 minute")
        #expect(SeaNotes.timeText(seconds: 1500) == "25 minutes")
        #expect(SeaNotes.timeText(seconds: 3600) == "1 hour")
        #expect(SeaNotes.timeText(seconds: 4200) == "1 hour 10 minutes")
        #expect(SeaNotes.timeText(seconds: 7200) == "2 hours")
    }

    @Test func usualNeedsThree() {
        #expect(SeaNotes.usualSeconds(sessionSeconds: []) == nil)
        #expect(SeaNotes.usualSeconds(sessionSeconds: [100, 200]) == nil)
        #expect(SeaNotes.usualSeconds(sessionSeconds: [100, 200, 300]) == 200)
    }

    @Test func intervalsInRange() {
        var rng = SeededGenerator(seed: 1)
        for _ in 0..<1000 {
            #expect((600...1200).contains(SeaNotes.nextInterval(using: &rng)))
        }
    }

    @Test func noTimeNoteBefore20Min() {
        var rng = SeededGenerator(seed: 2)
        var state = SeaNoteState()
        for _ in 0..<200 {
            let line = SeaNotes.next(visitSeconds: 1199, name: "Ana", usualSeconds: nil,
                                     state: &state, using: &rng)
            let general = SeaNotes.generalLines.map { $0.replacingOccurrences(of: "{name}", with: "Ana") }
            #expect(general.contains(line))
        }
    }

    @Test func twentyMinuteNoteOnce() {
        var rng = SeededGenerator(seed: 3)
        var state = SeaNoteState()
        let first = SeaNotes.next(visitSeconds: 1500, name: "", usualSeconds: nil, state: &state, using: &rng)
        #expect(first == "You've been drawing for 25 minutes.")
        let second = SeaNotes.next(visitSeconds: 1500, name: "", usualSeconds: nil, state: &state, using: &rng)
        #expect(SeaNotes.generalLines.contains(second))
    }

    @Test func hourNote() {
        var rng = SeededGenerator(seed: 4)
        var state = SeaNoteState()
        _ = SeaNotes.next(visitSeconds: 1500, name: "", usualSeconds: nil, state: &state, using: &rng)
        let line = SeaNotes.next(visitSeconds: 3600, name: "", usualSeconds: nil, state: &state, using: &rng)
        #expect(line == "1 hour already. Maybe stretch for a second?")
    }

    @Test func hourMarksTwentyShownToo() {
        var rng = SeededGenerator(seed: 5)
        var state = SeaNoteState()
        let first = SeaNotes.next(visitSeconds: 3600, name: "", usualSeconds: nil, state: &state, using: &rng)
        #expect(first == "1 hour already. Maybe stretch for a second?")
        let second = SeaNotes.next(visitSeconds: 3600, name: "", usualSeconds: nil, state: &state, using: &rng)
        #expect(SeaNotes.generalLines.contains(second))
    }

    @Test func usualComesFirst() {
        var rng = SeededGenerator(seed: 6)
        var state = SeaNoteState()
        let line = SeaNotes.next(visitSeconds: 1500, name: "", usualSeconds: 1000, state: &state, using: &rng)
        #expect(line == "You've been drawing for 25 minutes. That's longer than your usual practice.")
        // It stated the time, so the plain 20-minute note is spent; the hour note can still follow.
        let next = SeaNotes.next(visitSeconds: 3600, name: "", usualSeconds: 1000, state: &state, using: &rng)
        #expect(next == "1 hour already. Maybe stretch for a second?")
    }

    @Test func usualNeedsPassingAverage() {
        var rng = SeededGenerator(seed: 7)
        var state = SeaNoteState()
        let line = SeaNotes.next(visitSeconds: 500, name: "", usualSeconds: 1000, state: &state, using: &rng)
        #expect(SeaNotes.generalLines.contains(line))
        for _ in 0..<300 {
            let l = SeaNotes.next(visitSeconds: 1500, name: "", usualSeconds: nil, state: &state, using: &rng)
            #expect(!l.contains("usual practice"))
        }
    }

    @Test func nameLineNeedsName() {
        var rng = SeededGenerator(seed: 8)
        var state = SeaNoteState()
        for _ in 0..<500 {
            let l = SeaNotes.next(visitSeconds: 500, name: "", usualSeconds: nil, state: &state, using: &rng)
            #expect(!l.contains("{name}") && !l.contains("Still with you"))
        }
        state = SeaNoteState()
        var sawName = false
        for _ in 0..<500 {
            let l = SeaNotes.next(visitSeconds: 500, name: "Ana", usualSeconds: nil, state: &state, using: &rng)
            #expect(!l.contains("{name}"))
            if l == "Still with you, Ana." { sawName = true }
        }
        #expect(sawName)
    }

    @Test func generalDoesNotRepeatWithinFive() {
        var rng = SeededGenerator(seed: 9)
        for name in ["", "Ana"] {
            var state = SeaNoteState()
            var history: [String] = []
            for _ in 0..<500 {
                history.append(SeaNotes.next(visitSeconds: 500, name: name, usualSeconds: nil,
                                             state: &state, using: &rng))
            }
            for (i, line) in history.enumerated() {
                let window = history[max(0, i - 5)..<i]
                #expect(!window.contains(line))
            }
        }
    }

    private func milestones(poses: Int) -> [Int: String] {
        var state = PracticeNoteState()
        var out: [Int: String] = [:]
        for index in 0..<poses {
            if let line = SeaNotes.practiceMilestone(poseIndex: index, poseCount: poses, state: &state) { out[index] = line }
        }
        return out
    }

    @Test func practiceMilestonesFollowTheSession() {
        #expect(milestones(poses: 10) == [
            0: "You just began, you can do it.",
            5: "Halfway there. Keep going.",
            7: "Only 3 left.",
            9: "Last one. Make it count.",
        ])
    }

    @Test func shortSessionsDontCrowd() {
        #expect(milestones(poses: 1) == [0: "You just began, you can do it."])
        #expect(milestones(poses: 2) == [0: "You just began, you can do it.", 1: "Last one. Make it count."])
        #expect(milestones(poses: 3) == [0: "You just began, you can do it.", 2: "Last one. Make it count."])
        #expect(milestones(poses: 6) == [0: "You just began, you can do it.", 3: "Only 3 left.", 5: "Last one. Make it count."])
    }

    @Test func goingBackDoesntRepeatAMilestone() {
        var state = PracticeNoteState()
        #expect(SeaNotes.practiceMilestone(poseIndex: 7, poseCount: 10, state: &state) == "Only 3 left.")
        #expect(SeaNotes.practiceMilestone(poseIndex: 6, poseCount: 10, state: &state) == nil)
        #expect(SeaNotes.practiceMilestone(poseIndex: 7, poseCount: 10, state: &state) == nil)
    }

    @Test func practiceTiming() {
        #expect(SeaNotes.practiceFirstDelay == 120)
        var rng = SystemRandomNumberGenerator()
        for _ in 0..<50 { #expect((300...480).contains(SeaNotes.nextPracticeInterval(using: &rng))) }
    }

    @Test func practiceLinesDontRepeatWithinFive() {
        var state = PracticeNoteState()
        var rng = SystemRandomNumberGenerator()
        var last: [String] = []
        for _ in 0..<40 {
            let line = SeaNotes.practiceLine(name: "", state: &state, using: &rng)
            #expect(!last.contains(line))
            #expect(!line.contains("{name}"))
            last.append(line)
            if last.count > 5 { last.removeFirst() }
        }
    }

    @Test func practiceLineUsesName() {
        var rng = SystemRandomNumberGenerator()
        var seen = Set<String>()
        for _ in 0..<60 {
            var state = PracticeNoteState()
            seen.insert(SeaNotes.practiceLine(name: "Ana", state: &state, using: &rng))
        }
        #expect(seen.contains("Still with you, Ana."))
    }
}
