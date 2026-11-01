import Foundation
import LineworkCore
import SwiftData

@MainActor
enum ImportService {
    private struct Prepared: Sendable {
        let id: UUID
        let fileName: String
        let crop: CGRect
        let found: Bool
    }

    /// Copies each file into the library, makes a thumbnail and auto-crops it, a few files at a time.
    /// Unreadable files are skipped.
    static func importURLs(_ urls: [URL], into context: ModelContext, set: ImageSet?) async -> (imported: Int, skipped: Int) {
        let scoped = urls.filter { $0.startAccessingSecurityScopedResource() }
        defer { scoped.forEach { $0.stopAccessingSecurityScopedResource() } }

        var imported = 0, skipped = 0
        await withTaskGroup(of: Prepared?.self) { group in
            var pending = urls.makeIterator()
            for _ in 0..<maxConcurrentImports {
                guard let url = pending.next() else { break }
                group.addTask { try? await prepare(url) }
            }
            for await prepared in group {
                if let url = pending.next() { group.addTask { try? await prepare(url) } }
                guard let prepared else {
                    skipped += 1
                    continue
                }
                let ref = RefImage(id: prepared.id, fileName: prepared.fileName)
                ref.cropRect = prepared.crop
                ref.noFigureFound = !prepared.found
                context.insert(ref)
                if let set { ref.sets.append(set) }
                imported += 1
            }
        }
        try? context.save()
        return (imported, skipped)
    }

    private static let maxConcurrentImports = 4

    /// File copying, decoding and detection, all off the main actor.
    @concurrent
    private nonisolated static func prepare(_ url: URL) async throws -> Prepared {
        let (id, fileName) = try Storage.importCopy(url)
        do {
            try Storage.makeThumbnail(id: id, fileName: fileName)
        } catch {
            try? FileManager.default.removeItem(at: Storage.imageURL(fileName: fileName))
            throw error
        }
        var crop = (rect: CropMath.unit, found: false)
        if let image = ImageRendering.loadCG(url: Storage.imageURL(fileName: fileName), maxPixel: 1600) {
            crop = await AutoCropper.crop(for: image)
        }
        return Prepared(id: id, fileName: fileName, crop: crop.rect, found: crop.found)
    }
}
