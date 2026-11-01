import Foundation
import Testing
@testable import LineworkCore

@Suite struct SessionLengthTests {
    @Test func posesForTarget() {
        #expect(SessionLength.poses(targetSeconds: 1800, poseSeconds: 60) == 30)
        #expect(SessionLength.poses(targetSeconds: 1800, poseSeconds: 120) == 15)
        #expect(SessionLength.poses(targetSeconds: 1800, poseSeconds: 300) == 6)
        #expect(SessionLength.poses(targetSeconds: 1800, poseSeconds: 420) == 4)   // 28 min beats 35 min
        #expect(SessionLength.poses(targetSeconds: 100, poseSeconds: 600) == 1)    // never zero
    }

    @Test func scaledClassAveragesToTarget() {
        let scaled = SessionLength.scaled(QueueBuilder.defaultClass, toSeconds: 1800)
        #expect(scaled.map(\.seconds) == [30, 60, 300])
        #expect(scaled.map(\.count) == [15, 8, 3])
        let total = scaled.reduce(0) { $0 + $1.count * $1.seconds }
        #expect(abs(total - 1800) <= 60)
    }

    @Test func scaledKeepsEveryStep() {
        let scaled = SessionLength.scaled([.init(count: 1, seconds: 30), .init(count: 2, seconds: 600)], toSeconds: 600)
        #expect(scaled.allSatisfy { $0.count >= 1 })
    }
}

@Suite struct FillPoolTests {
    @Test func priorityImagesComeFirstThenFill() {
        var rng = SplitMix64(seed: 7)
        let today = ids(3), rest = ids(10)
        let q = QueueBuilder.build(imageIDs: today, fillIDs: rest, mode: .simple(seconds: 60, maxPoses: 8),
                                   shuffle: true, benchmarks: [], using: &rng)
        let order = q.poses.map(\.imageID)
        #expect(order.count == 8)
        #expect(Set(order.prefix(3)) == Set(today))
        #expect(Set(order.dropFirst(3)).isSubset(of: Set(rest)))
        #expect(Set(order).count == 8)          // no repeats while the library has enough
        #expect(q.repeatedImages == false)
    }

    @Test func fillOnlyUsedWhenNeeded() {
        var rng = SplitMix64(seed: 7)
        let today = ids(5)
        let q = QueueBuilder.build(imageIDs: today, fillIDs: ids(10), mode: .simple(seconds: 60, maxPoses: 4),
                                   shuffle: true, benchmarks: [], using: &rng)
        #expect(Set(q.poses.map(\.imageID)).isSubset(of: Set(today)))
    }

    @Test func emptyPriorityStillFills() {
        var rng = SplitMix64(seed: 7)
        let q = QueueBuilder.build(imageIDs: [], fillIDs: ids(4), mode: .simple(seconds: 60, maxPoses: 3),
                                   shuffle: true, benchmarks: [], using: &rng)
        #expect(q.poses.count == 3)
    }
}
