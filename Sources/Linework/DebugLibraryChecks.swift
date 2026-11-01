#if DEBUG
import AppKit
import Foundation
import LineworkCore

/// Debug-only: `Linework --library-checks` with LINEWORK_LIBRARY set to a scratch folder.
/// Drives a real `LibraryStore` against real files and prints a line per check.
@MainActor
enum DebugLibraryChecks {
    static func check(_ name: String, _ ok: Bool) { print("check", ok ? "pass" : "FAIL", name) }

    static func run() {
        guard let path = ProcessInfo.processInfo.environment["LINEWORK_LIBRARY"] else {
            print("check FAIL needs LINEWORK_LIBRARY")
            exit(2)
        }
        let root = URL(fileURLWithPath: path, isDirectory: true)
        let app = AppModel()
        check("A scratch library opens", app.library != nil)
        for name in LibraryFolder.folders + [LibraryFolder.manifestName] {
            check("The library has \(name)", FileManager.default.fileExists(atPath: root.appending(path: name).path))
        }
        guard let store = app.library else { exit(1) }
        store.flush()
        check("A new library has one preset", store.presets.count == 1)
        let ref = store.schedule.todayRef(PlanClock.now)
        check("A tick saves at once", store.toggleTick(ref, .focus) && FileManager.default.fileExists(atPath: store.files.url(of: .checks).path))
        let reopened = try? LibraryStore(root: root)
        check("A reopened library has the tick", reopened?.isTicked(ref, .focus) == true)

        // Review focus: the folder goes away while the app is open.
        try? FileManager.default.moveItem(at: root, to: root.appendingPathExtension("away"))
        let kept = store.toggleTick(ref, .check)
        check("A missing library reports the failed save", store.saveFailure != nil)
        check("A missing library isn't recreated", !FileManager.default.fileExists(atPath: root.path))
        check("The edit stays in memory", kept && store.isTicked(ref, .check))
        try? FileManager.default.moveItem(at: root.appendingPathExtension("away"), to: root)
        store.flush()
        check("The save succeeds once the folder is back", store.saveFailure == nil && (try? LibraryStore(root: root))?.isTicked(ref, .check) == true)
        exit(0)
    }
}
#endif
