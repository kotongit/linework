import CoreGraphics
import Foundation
import LineworkCore
import SwiftData

@Model
final class RefImage {
    @Attribute(.unique) var id: UUID
    var fileName: String
    var importedAt: Date
    var cropX: Double = 0
    var cropY: Double = 0
    var cropW: Double = 1
    var cropH: Double = 1
    var rotation: Int = 0
    var flipped: Bool = false
    var noFigureFound: Bool = false
    var isBenchmark: Bool = false
    var benchmarkSeconds: Int = 120
    var benchmarkInterval: Int = 10
    var benchmarkMarkedAt: Date?
    var sets: [ImageSet] = []
    @Relationship(deleteRule: .cascade, inverse: \BenchmarkAttempt.image)
    var attempts: [BenchmarkAttempt] = []

    init(id: UUID, fileName: String, importedAt: Date = .now) {
        self.id = id
        self.fileName = fileName
        self.importedAt = importedAt
    }

    /// Normalized, top-left origin, applied after rotation/flip.
    var cropRect: CGRect {
        get { CGRect(x: cropX, y: cropY, width: cropW, height: cropH) }
        set {
            cropX = newValue.minX
            cropY = newValue.minY
            cropW = newValue.width
            cropH = newValue.height
        }
    }

    func toggleBenchmark() {
        isBenchmark.toggle()
        if isBenchmark { benchmarkMarkedAt = .now }
    }

    var sortedAttempts: [BenchmarkAttempt] { attempts.sorted { $0.date < $1.date } }

    /// Date the benchmark interval counts from.
    var benchmarkSince: Date { attempts.map(\.date).max() ?? benchmarkMarkedAt ?? importedAt }
}

@Model
final class ImageSet {
    @Attribute(.unique) var id: UUID
    var name: String
    var createdAt: Date
    @Relationship(inverse: \RefImage.sets)
    var images: [RefImage] = []

    init(name: String) {
        id = UUID()
        self.name = name
        createdAt = .now
    }
}

@Model
final class ClassPreset {
    @Attribute(.unique) var id: UUID
    var name: String
    var steps: [ClassStep]

    init(name: String, steps: [ClassStep]) {
        id = UUID()
        self.name = name
        self.steps = steps
    }
}

@Model
final class SessionRecord {
    @Attribute(.unique) var id: UUID
    var startedAt: Date
    var drawingSeconds: Int
    var poseCount: Int
    var imageIDs: [UUID]

    init(id: UUID, startedAt: Date, drawingSeconds: Int, poseCount: Int, imageIDs: [UUID]) {
        self.id = id
        self.startedAt = startedAt
        self.drawingSeconds = drawingSeconds
        self.poseCount = poseCount
        self.imageIDs = imageIDs
    }
}

@Model
final class BenchmarkAttempt {
    @Attribute(.unique) var id: UUID
    var image: RefImage?
    var date: Date
    var sessionID: UUID
    var drawingFileName: String?

    init(image: RefImage, date: Date = .now, sessionID: UUID, drawingFileName: String?) {
        id = UUID()
        self.image = image
        self.date = date
        self.sessionID = sessionID
        self.drawingFileName = drawingFileName
    }
}

/// One ticked block of the practice plan, for the calendar date it belongs to. Unticking deletes the row.
@Model
final class PlanCheck {
    @Attribute(.unique) var id: UUID
    var dayKey: String
    var blockKey: String
    var checkedAt: Date

    init(dayKey: String, blockKey: String, checkedAt: Date = .now) {
        id = UUID()
        self.dayKey = dayKey
        self.blockKey = blockKey
        self.checkedAt = checkedAt
    }
}

/// A repeated stretch of the plan. Applied in `createdAt` order.
@Model
final class PlanRepeatRecord {
    @Attribute(.unique) var id: UUID
    var afterSequenceIndex: Int
    var weekCount: Int
    var createdAt: Date

    init(afterSequenceIndex: Int, weekCount: Int, createdAt: Date = .now) {
        id = UUID()
        self.afterSequenceIndex = afterSequenceIndex
        self.weekCount = weekCount
        self.createdAt = createdAt
    }
}
