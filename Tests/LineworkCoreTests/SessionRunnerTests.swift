import Foundation
import Testing
@testable import LineworkCore

private func pose(_ s: Int, bench: Int? = nil, step: Int? = nil) -> Pose {
    Pose(imageID: UUID(), seconds: s, benchmarkNumber: bench, stepIndex: step)
}

@Suite struct SessionRunnerTests {
    @Test func getReadyThenPose() {
        var r = SessionRunner(poses: [pose(30)])
        #expect(r.phase == .getReady(remaining: 3))
        _ = r.tick(3.0)
        #expect(r.phase == .pose)
        #expect(r.remaining == 30)
    }

    @Test func pauseExcludesTime() {
        var r = SessionRunner(poses: [pose(30)])
        _ = r.tick(3); _ = r.tick(1)
        r.togglePause()
        _ = r.tick(10)
        #expect(r.drawingSeconds == 1)
        #expect(r.remaining == 29)
    }

    @Test func tickEventsInLastFiveSeconds() {
        var r = SessionRunner(poses: [pose(30), pose(30)])
        _ = r.tick(3)
        var ticks = 0
        for _ in 0..<30 {
            let e = r.tick(1.0)
            if e.contains(.tick) { ticks += 1 }
            if e.contains(.poseChanged) { break }
        }
        #expect(ticks == 5)
        #expect(r.index == 1)
    }

    @Test func previousRestartsPose() {
        var r = SessionRunner(poses: [pose(30), pose(30)])
        _ = r.tick(3); _ = r.tick(10)
        _ = r.next()
        _ = r.tick(2)
        _ = r.previous()
        #expect(r.index == 0)
        #expect(r.remaining == 30)
        #expect(r.drawingSeconds == 12)
    }

    @Test func benchmarkAwaitsAttempt() {
        var r = SessionRunner(poses: [pose(5, bench: 1), pose(30)])
        _ = r.tick(3)
        let e = r.tick(5)
        #expect(e.contains(.benchmarkEnded))
        #expect(r.phase == .awaitingAttempt)
        _ = r.tick(100)
        #expect(r.phase == .awaitingAttempt && r.index == 0)
        _ = r.attemptHandled()
        #expect(r.index == 1 && r.phase == .pose)
    }

    @Test func skippingBenchmarkAlsoAwaitsAttempt() {
        var r = SessionRunner(poses: [pose(5, bench: 1), pose(30)])
        _ = r.tick(3)
        #expect(r.next().contains(.benchmarkEnded))
        #expect(r.phase == .awaitingAttempt)
    }

    @Test func stepChangeGetsReady() {
        var r = SessionRunner(poses: [pose(5, step: 0), pose(30, step: 1)])
        _ = r.tick(3)
        let e = r.tick(5)
        #expect(e.contains(.stepChanged) && e.contains(.poseChanged))
        #expect(r.phase == .getReady(remaining: 3))
    }

    @Test func drawingStartIsAnnouncedAfterCountdown() {
        var r = SessionRunner(poses: [pose(5, step: 0), pose(30, step: 1)])
        #expect(r.tick(1) == [])
        #expect(r.tick(2).contains(.drawingStarted))
        let changed = r.tick(5)
        #expect(!changed.contains(.drawingStarted))
        #expect(r.tick(3).contains(.drawingStarted))
    }

    @Test func noCountdownMeansNoStartEvent() {
        var r = SessionRunner(poses: [pose(5), pose(5)])
        _ = r.tick(3)
        let e = r.tick(5)
        #expect(e.contains(.poseChanged) && !e.contains(.drawingStarted))
    }

    @Test func shouldRecordThreshold() {
        var a = SessionRunner(poses: [pose(30)])
        _ = a.tick(3); _ = a.tick(4.9)
        #expect(!a.shouldRecord)
        var b = SessionRunner(poses: [pose(30)])
        _ = b.tick(3); _ = b.tick(5.0)
        #expect(b.shouldRecord)
    }

    @Test func finishAfterLast() {
        var r = SessionRunner(poses: [pose(30)])
        _ = r.tick(3)
        #expect(r.next().contains(.finished))
        #expect(r.phase == .finished)
    }

    @Test func previousDoesNotReturnToHandledBenchmark() {
        var r = SessionRunner(poses: [pose(5, bench: 1), pose(30), pose(30)])
        _ = r.tick(3); _ = r.tick(5)
        _ = r.attemptHandled()
        _ = r.tick(4)
        _ = r.previous()
        #expect(r.index == 1)
        #expect(r.remaining == 30)
        #expect(r.phase == .pose)
    }

    @Test func previousDuringOpeningCountdownKeepsCountdown() {
        var r = SessionRunner(poses: [pose(30), pose(30)])
        _ = r.tick(1)
        #expect(r.previous() == [])
        #expect(r.phase == .getReady(remaining: 2))
    }

    @Test func naturalFinishByTimer() {
        var r = SessionRunner(poses: [pose(5)])
        _ = r.tick(3); _ = r.tick(5)
        #expect(r.completedNaturally)
    }

    @Test func naturalFinishBySkipping() {
        var r = SessionRunner(poses: [pose(30), pose(30)])
        _ = r.tick(3); _ = r.next(); _ = r.next()
        #expect(r.completedNaturally)
    }

    @Test func escIsNotNatural() {
        var r = SessionRunner(poses: [pose(30)])
        _ = r.tick(3); r.end()
        #expect(!r.completedNaturally)
        var done = SessionRunner(poses: [pose(5)])
        _ = done.tick(3); _ = done.tick(5); done.end()
        #expect(done.completedNaturally)
    }

    @Test func runningIsNotNatural() {
        var r = SessionRunner(poses: [pose(30)])
        _ = r.tick(3); _ = r.tick(1)
        #expect(!r.completedNaturally)
        #expect(!SessionRunner(poses: []).completedNaturally)
    }

    @Test func emptyIsFinished() {
        #expect(SessionRunner(poses: []).phase == .finished)
    }
}
