import Foundation

public enum LibraryError: Error, Equatable {
    case newerFormat(file: String)
    /// It's there but can't be read: an undownloaded file while offline, a disk error, a permission problem.
    case unreadable(file: String)
    /// The library's folder isn't there, for example on an unplugged drive.
    case unavailable
}

/// One text file in a library, and what it holds.
public struct LibraryFile<Payload: Codable & Sendable>: Sendable {
    public let name: String
}

extension LibraryFile where Payload == [PhotoRecord] { public static var photos: Self { .init(name: "photos.json") } }
extension LibraryFile where Payload == BenchmarksFile { public static var benchmarks: Self { .init(name: "benchmarks.json") } }
extension LibraryFile where Payload == [SetRecord] { public static var sets: Self { .init(name: "sets.json") } }
extension LibraryFile where Payload == [PresetRecord] { public static var presets: Self { .init(name: "presets.json") } }
extension LibraryFile where Payload == [PlanTick] { public static var checks: Self { .init(name: "checks.json") } }
extension LibraryFile where Payload == PlanRecord { public static var plan: Self { .init(name: "plan.json") } }
extension LibraryFile where Payload == [SessionRecord] { public static var sessions: Self { .init(name: "sessions.json") } }

public enum LoadResult<T: Equatable & Sendable>: Equatable, Sendable {
    case missing
    case loaded(T)
    case damaged(setAsideAs: URL)
}

/// Reads and writes the text files under a library's `Progress/` folder.
public struct LibraryFiles: Sendable {
    public static let currentVersion = 1
    public let root: URL

    public init(root: URL) {
        self.root = root
    }

    private struct Envelope<T: Codable>: Codable {
        let version: Int
        let items: T
    }

    private struct VersionOnly: Decodable {
        let version: Int
    }

    public static var encoder: JSONEncoder {
        let encoder = JSONEncoder()
        encoder.outputFormatting = [.prettyPrinted, .sortedKeys, .withoutEscapingSlashes]
        encoder.dateEncodingStrategy = .iso8601
        return encoder
    }

    public static var decoder: JSONDecoder {
        let decoder = JSONDecoder()
        decoder.dateDecodingStrategy = .iso8601
        return decoder
    }

    /// Beside `url`, with "damaged" in its name. Never one that's already there, so nothing set aside gets replaced.
    public static func freeAsideURL(for url: URL) -> URL {
        let folder = url.deletingLastPathComponent()
        let stem = url.deletingPathExtension().lastPathComponent
        let taken = Set((try? FileManager.default.contentsOfDirectory(atPath: folder.path)) ?? [])
        return folder.appending(path: PhotoScan.uniqueName("\(stem).damaged.json", taken: taken))
    }

    private var progress: URL { root.appending(path: "Progress", directoryHint: .isDirectory) }

    public func url<T>(of file: LibraryFile<T>) -> URL { progress.appending(path: file.name) }

    public func modificationDate<T>(of file: LibraryFile<T>) -> Date? {
        try? FileManager.default.attributesOfItem(atPath: url(of: file).path)[.modificationDate] as? Date
    }

    public func read<T>(_ file: LibraryFile<T>) throws -> LoadResult<T> {
        let url = url(of: file)
        guard FileManager.default.fileExists(atPath: url.path) else { return .missing }
        guard let data = try? Data(contentsOf: url) else { throw LibraryError.unreadable(file: file.name) }
        let decoder = Self.decoder
        if let version = try? decoder.decode(VersionOnly.self, from: data).version, version > Self.currentVersion {
            throw LibraryError.newerFormat(file: file.name)
        }
        if let envelope = try? decoder.decode(Envelope<T>.self, from: data) { return .loaded(envelope.items) }
        let aside = LibraryFiles.freeAsideURL(for: url)
        try FileManager.default.moveItem(at: url, to: aside)
        return .damaged(setAsideAs: aside)
    }

    /// Atomic. Never recreates a library that has gone away.
    public func write<T>(_ value: T, to file: LibraryFile<T>) throws {
        guard FileManager.default.fileExists(atPath: root.path) else { throw LibraryError.unavailable }
        try FileManager.default.createDirectory(at: progress, withIntermediateDirectories: true)
        let data = try Self.encoder.encode(Envelope(version: Self.currentVersion, items: value))
        try data.write(to: url(of: file), options: .atomic)
    }
}
