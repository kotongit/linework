import Foundation
import LineworkCore

/// Notices photos added, renamed or removed in Finder.
@MainActor
enum PhotoScanner {
    static func run(on store: LibraryStore) async {
        guard store.importsRunning == 0, !store.isScanning else { return }
        store.isScanning = true
        defer { store.isScanning = false }
        let folder = store.photosDir
        let records = store.photos
        let (result, exists) = await decide(records: records, folder: folder)
        guard store.importsRunning == 0 else { return }
        let added = await ImportService.records(forExisting: result.added.map { folder.appending(path: $0) })
        store.applyScan(result, added: added, folderExists: exists)
    }

    @concurrent
    private nonisolated static func decide(records: [PhotoRecord], folder: URL) async -> (PhotoScanResult, Bool) {
        let keys: Set<URLResourceKey> = [.fileSizeKey, .ubiquitousItemDownloadingStatusKey]
        let exists = FileManager.default.fileExists(atPath: folder.path)
        let urls = (try? FileManager.default.contentsOfDirectory(at: folder, includingPropertiesForKeys: Array(keys))) ?? []
        let files = urls.map { url -> ScannedFile in
            let values = try? url.resourceValues(forKeys: keys)
            let status = values?.ubiquitousItemDownloadingStatus
            return ScannedFile(name: url.lastPathComponent, size: values?.fileSize ?? 0, isPlaceholder: status == .notDownloaded)
        }
        let result = PhotoScan.decide(records: records, files: files, folderExists: exists) { name in
            try FileHash.sha256(of: folder.appending(path: name))
        }
        return (result, exists)
    }
}
