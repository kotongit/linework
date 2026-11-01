import CoreGraphics
import Foundation

/// A crop as stored: normalized, top-left origin, applied after rotation and flip.
public struct NormalizedRect: Codable, Hashable, Sendable {
    public var x, y, width, height: Double

    public init(x: Double, y: Double, width: Double, height: Double) {
        self.x = x
        self.y = y
        self.width = width
        self.height = height
    }

    public init(_ rect: CGRect) {
        self.init(x: rect.minX, y: rect.minY, width: rect.width, height: rect.height)
    }

    public var cgRect: CGRect { CGRect(x: x, y: y, width: width, height: height) }
}

/// `library.json`: marks a folder as a library.
public struct LibraryManifest: Codable, Equatable, Sendable {
    public static let currentFormat = 1
    public let format: Int
    public let id: UUID
    public let createdAt: Date

    public init(format: Int = Self.currentFormat, id: UUID = UUID(), createdAt: Date) {
        self.format = format
        self.id = id
        self.createdAt = createdAt
    }
}

public struct PhotoRecord: Codable, Equatable, Identifiable, Sendable {
    public let id: UUID
    public var fileName: String
    public var sha256: String
    public var importedAt: Date
    public var crop: NormalizedRect
    public var rotation: Int
    public var flipped: Bool
    public var noFigureFound: Bool
    /// Its file isn't in Photos/ right now. Restoring the file reconnects it.
    public var missing: Bool

    public init(id: UUID, fileName: String, sha256: String, importedAt: Date, crop: NormalizedRect, rotation: Int, flipped: Bool,
                noFigureFound: Bool, missing: Bool) {
        self.id = id
        self.fileName = fileName
        self.sha256 = sha256
        self.importedAt = importedAt
        self.crop = crop
        self.rotation = rotation
        self.flipped = flipped
        self.noFigureFound = noFigureFound
        self.missing = missing
    }
}

public struct BenchmarkRecord: Codable, Equatable, Sendable {
    public let photoID: UUID
    public var seconds: Int
    public var interval: Int
    public var markedAt: Date

    public init(photoID: UUID, seconds: Int, interval: Int, markedAt: Date) {
        self.photoID = photoID
        self.seconds = seconds
        self.interval = interval
        self.markedAt = markedAt
    }
}

public struct AttemptRecord: Codable, Equatable, Identifiable, Sendable {
    public let id: UUID
    public let photoID: UUID
    public let date: Date
    public let sessionID: UUID
    public var drawingFileName: String?

    public init(id: UUID, photoID: UUID, date: Date, sessionID: UUID, drawingFileName: String?) {
        self.id = id
        self.photoID = photoID
        self.date = date
        self.sessionID = sessionID
        self.drawingFileName = drawingFileName
    }
}

public struct BenchmarksFile: Codable, Equatable, Sendable {
    public var benchmarks: [BenchmarkRecord]
    public var attempts: [AttemptRecord]

    public init(benchmarks: [BenchmarkRecord] = [], attempts: [AttemptRecord] = []) {
        self.benchmarks = benchmarks
        self.attempts = attempts
    }
}

public struct SetRecord: Codable, Equatable, Identifiable, Sendable {
    public let id: UUID
    public var name: String
    public let createdAt: Date
    public var photoIDs: [UUID]

    public init(id: UUID, name: String, createdAt: Date, photoIDs: [UUID]) {
        self.id = id
        self.name = name
        self.createdAt = createdAt
        self.photoIDs = photoIDs
    }
}

public struct PresetRecord: Codable, Equatable, Identifiable, Sendable {
    public let id: UUID
    public var name: String
    public var steps: [ClassStep]

    public init(id: UUID, name: String, steps: [ClassStep]) {
        self.id = id
        self.name = name
        self.steps = steps
    }
}

public struct SessionRecord: Codable, Equatable, Identifiable, Sendable {
    public let id: UUID
    public let startedAt: Date
    public let drawingSeconds: Int
    public let poseCount: Int
    public let photoIDs: [UUID]

    public init(id: UUID, startedAt: Date, drawingSeconds: Int, poseCount: Int, photoIDs: [UUID]) {
        self.id = id
        self.startedAt = startedAt
        self.drawingSeconds = drawingSeconds
        self.poseCount = poseCount
        self.photoIDs = photoIDs
    }
}

/// When this library's Week 1 began (always a Monday), and its repeated weeks.
public struct PlanRecord: Codable, Equatable, Sendable {
    public var weekOneStart: String
    public var repeats: [PlanRepeat]

    public init(weekOneStart: String, repeats: [PlanRepeat] = []) {
        self.weekOneStart = weekOneStart
        self.repeats = repeats
    }
}
