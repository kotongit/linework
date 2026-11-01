import Foundation
import LineworkCore

/// Brings photos into a library: copied in under readable names, hashed and auto-cropped, a few at a time.
@MainActor
enum ImportService {
    private static let maxConcurrent = 4

    /// Unreadable files are skipped.
    static func importURLs(_ urls: [URL], into store: LibraryStore, setID: UUID?) async -> (imported: Int, skipped: Int) {
        let scoped = urls.filter { $0.startAccessingSecurityScopedResource() }
        defer { scoped.forEach { $0.stopAccessingSecurityScopedResource() } }
        store.importsRunning += 1
        defer { store.importsRunning -= 1 }

        // Names are settled here, before any copying starts, so two files can't claim one.
        var taken = Set((try? FileManager.default.contentsOfDirectory(atPath: store.photosDir.path)) ?? [])
        var jobs: [(source: URL, target: URL)] = []
        for url in urls {
            let name = PhotoScan.uniqueName(url.lastPathComponent, taken: taken)
            taken.insert(name)
            jobs.append((url, store.photosDir.appending(path: name)))
        }
        let records = await prepare(jobs) { job in try await copyIn(job.source, to: job.target) }
        store.addPhotos(records, toSet: setID)
        return (records.count, urls.count - records.count)
    }

    /// Records for files already in `Photos/`, found by the Finder check.
    static func records(forExisting urls: [URL]) async -> [PhotoRecord] {
        await prepare(urls.map { ($0, $0) }) { job in try await describe(job.target) }
    }

    private static func prepare(_ jobs: [(source: URL, target: URL)],
                                _ work: @escaping @Sendable ((source: URL, target: URL)) async throws -> PhotoRecord) async -> [PhotoRecord] {
        var records: [PhotoRecord] = []
        await withTaskGroup(of: PhotoRecord?.self) { group in
            var pending = jobs.makeIterator()
            for _ in 0..<maxConcurrent {
                guard let job = pending.next() else { break }
                group.addTask { try? await work(job) }
            }
            for await record in group {
                if let job = pending.next() { group.addTask { try? await work(job) } }
                if let record { records.append(record) }
            }
        }
        return records
    }

    @concurrent
    private nonisolated static func copyIn(_ source: URL, to target: URL) async throws -> PhotoRecord {
        guard ImageFiles.isReadableImage(source) else { throw StorageError.unreadableImage }
        try FileManager.default.copyItem(at: source, to: target)
        do {
            return try await describe(target)
        } catch {
            try? FileManager.default.removeItem(at: target)
            throw error
        }
    }

    /// Hashing, decoding and detection, all off the main actor.
    @concurrent
    private nonisolated static func describe(_ url: URL) async throws -> PhotoRecord {
        guard ImageFiles.isReadableImage(url) else { throw StorageError.unreadableImage }
        let hash = try FileHash.sha256(of: url)
        var crop = (rect: CropMath.unit, found: false)
        if let image = ImageRendering.loadCG(url: url, maxPixel: 1600) {
            crop = await AutoCropper.crop(for: image)
        }
        return PhotoRecord(id: UUID(), fileName: url.lastPathComponent, sha256: hash, importedAt: .now, crop: NormalizedRect(crop.rect),
                           rotation: 0, flipped: false, noFigureFound: !crop.found, missing: false)
    }
}
