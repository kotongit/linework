import Foundation

extension PlanSchedule {
    /// One line naming the day's blocks, e.g. "Gestures, brush kit, one rule".
    public func practiceLine(on date: Date) -> String {
        guard let day = day(on: date) else { return "Starts Monday 2 Nov" }
        if day.isRest { return "Rest" }
        var titles: [String] = []
        for block in day.blocks where !titles.contains(block.title) {
            titles.append(block.title)
        }
        return titles.joined(separator: ", ")
    }

    /// The first unchecked block that runs as a timed session from photos.
    public func nextTimedBlock(on date: Date, checked: Set<String>) -> PlanBlock? {
        day(on: date)?.blocks.first { $0.timed != nil && !checked.contains($0.kind.rawValue) }
    }
}
