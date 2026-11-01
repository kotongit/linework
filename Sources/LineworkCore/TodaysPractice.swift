import Foundation

extension PlanSchedule {
    /// One line naming the day's blocks, e.g. "Gestures, brush kit, one rule".
    public func practiceLine(_ ref: PlanDayRef) -> String {
        let day = day(for: ref)
        if day.isRest { return "Rest" }
        var titles: [String] = []
        for block in day.blocks where !titles.contains(block.title) {
            titles.append(block.title)
        }
        return titles.joined(separator: ", ")
    }

    /// The first unticked block that runs as a timed session from photos.
    public func nextTimedBlock(_ ref: PlanDayRef, ticks: Set<String>) -> PlanBlock? {
        day(for: ref).blocks.first { $0.timed != nil && !ticks.contains(tickKey(ref, $0.kind)) }
    }
}
