import Foundation

/// Files in the library are the user's own, so deleting moves them to the Trash.
enum Trash {
    /// Files already gone are fine. Stops at the first that can't be moved.
    static func move(_ urls: [URL]) throws {
        let fm = FileManager.default
        for url in urls where fm.fileExists(atPath: url.path) {
            try fm.trashItem(at: url, resultingItemURL: nil)
        }
    }
}
