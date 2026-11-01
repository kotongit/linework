import AppKit
import Foundation

/// Files in the library are the user's own, so deleting moves them to the Trash.
@MainActor
enum Trash {
    /// Files already gone are fine. Stops at the first that can't be moved, unless the user agrees to erase it,
    /// for example on a network drive with no Trash.
    static func move(_ urls: [URL]) throws {
        let fm = FileManager.default
        for url in urls where fm.fileExists(atPath: url.path) {
            do {
                try fm.trashItem(at: url, resultingItemURL: nil)
            } catch {
                guard confirmErasing(url) else { throw error }
                try fm.removeItem(at: url)
            }
        }
    }

    private static func confirmErasing(_ url: URL) -> Bool {
        #if DEBUG
        if DebugSnapshots.active { return false }
        #endif
        let alert = NSAlert()
        alert.messageText = "“\(url.lastPathComponent)” can't go to the Trash."
        alert.informativeText = "The drive it's on may not have a Trash. Deleting it now can't be undone."
        alert.addButton(withTitle: "Delete Permanently")
        alert.addButton(withTitle: "Cancel")
        return alert.runModal() == .alertFirstButtonReturn
    }
}
