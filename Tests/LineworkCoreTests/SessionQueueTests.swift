import Foundation
import Testing
@testable import LineworkCore

@Suite struct SessionQueueTests {
    @Test func simpleUsesAllOnce() {
        var rng = SplitMix64(seed: 1)
        let pool = ids(3)
        let q = QueueBuilder.build(imageIDs: pool, mode: .simple(seconds: 60, maxPoses: nil),
                                   shuffle: false, benchmarks: [], using: &rng)
        #expect(q.poses.map(\.imageID) == pool)
        #expect(q.poses.allSatisfy { $0.seconds == 60 && $0.stepIndex == nil })
        #expect(q.repeatedImages == false)
    }

    @Test func simpleMaxPosesRepeats() {
        var rng = SplitMix64(seed: 1)
        let q = QueueBuilder.build(imageIDs: ids(2), mode: .simple(seconds: 30, maxPoses: 5),
                                   shuffle: false, benchmarks: [], using: &rng)
        #expect(q.poses.count == 5)
        #expect(q.repeatedImages)
    }

    @Test func classStepsExpand() {
        var rng = SplitMix64(seed: 1)
        let q = QueueBuilder.build(imageIDs: ids(20), mode: .classSteps(QueueBuilder.defaultClass),
                                   shuffle: false, benchmarks: [], using: &rng)
        #expect(q.poses.count == 17)
        #expect(q.poses.map(\.seconds) == Array(repeating: 30, count: 10) + Array(repeating: 60, count: 5) + [300, 300])
        #expect(q.poses.map(\.stepIndex) == Array(repeating: 0, count: 10) + Array(repeating: 1, count: 5) + [2, 2])
        #expect(q.repeatedImages == false)
    }

    @Test func zeroCountStepsSkipped() {
        var rng = SplitMix64(seed: 1)
        let q = QueueBuilder.build(imageIDs: ids(5), mode: .classSteps([.init(count: 0, seconds: 30), .init(count: 2, seconds: 60)]),
                                   shuffle: false, benchmarks: [], using: &rng)
        #expect(q.poses.map(\.seconds) == [60, 60])
    }

    @Test func shuffleIsPermutation() {
        var rng = SplitMix64(seed: 42)
        let pool = ids(10)
        let q = QueueBuilder.build(imageIDs: pool, mode: .simple(seconds: 30, maxPoses: nil),
                                   shuffle: true, benchmarks: [], using: &rng)
        #expect(Set(q.poses.map(\.imageID)) == Set(pool))
        #expect(q.poses.map(\.imageID) != pool)
    }

    @Test func benchmarksFirstAndNotDuplicated() {
        var rng = SplitMix64(seed: 1)
        let pool = ids(3)
        let bench = DueBenchmark(imageID: pool[0], seconds: 120, number: 3)
        let q = QueueBuilder.build(imageIDs: pool, mode: .simple(seconds: 30, maxPoses: nil),
                                   shuffle: false, benchmarks: [bench], using: &rng)
        #expect(q.poses == [
            Pose(imageID: pool[0], seconds: 120, benchmarkNumber: 3, stepIndex: nil),
            Pose(imageID: pool[1], seconds: 30, benchmarkNumber: nil, stepIndex: nil),
            Pose(imageID: pool[2], seconds: 30, benchmarkNumber: nil, stepIndex: nil),
        ])
    }

    @Test func emptyPoolOnlyBenchmarks() {
        var rng = SplitMix64(seed: 1)
        let b = DueBenchmark(imageID: UUID(), seconds: 120, number: 1)
        let withBench = QueueBuilder.build(imageIDs: [], mode: .simple(seconds: 30, maxPoses: 5),
                                           shuffle: true, benchmarks: [b], using: &rng)
        #expect(withBench.poses.count == 1)
        let none = QueueBuilder.build(imageIDs: [], mode: .classSteps(QueueBuilder.defaultClass),
                                      shuffle: true, benchmarks: [], using: &rng)
        #expect(none.poses.isEmpty)
    }

    @Test func repeatsNeverBackToBack() {
        for seed in 0..<50 as Range<UInt64> {
            var rng = SplitMix64(seed: seed)
            let q = QueueBuilder.build(imageIDs: ids(3), mode: .simple(seconds: 30, maxPoses: 30),
                                       shuffle: true, benchmarks: [], using: &rng)
            let order = q.poses.map(\.imageID)
            #expect(zip(order, order.dropFirst()).allSatisfy { $0 != $1 }, "seed \(seed)")
        }
    }

    @Test func renderWindowKeepsNeighbours() {
        #expect(RenderWindow.indices(around: 0, count: 10) == 0...1)
        #expect(RenderWindow.indices(around: 5, count: 10) == 4...6)
        #expect(RenderWindow.indices(around: 9, count: 10) == 8...9)
    }
}
