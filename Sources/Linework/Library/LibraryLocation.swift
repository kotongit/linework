import Foundation

/// Remembers which library was open, in a way that survives moving or renaming its folder.
enum LibraryLocation {
    private static let key = "linework.library"

    static var remembered: URL? {
        #if DEBUG
        if let path = ProcessInfo.processInfo.environment["LINEWORK_LIBRARY"] { return URL(fileURLWithPath: path, isDirectory: true) }
        // The harness overwrites and clears things, so it must never reach real data.
        if !Set(CommandLine.arguments).isDisjoint(with: ["--snapshots", "--library-checks", "--print-counts"]) {
            FileHandle.standardError.write(Data("This needs LINEWORK_LIBRARY set to a scratch folder. Nothing was run.\n".utf8))
            exit(2)
        }
        #endif
        guard let data = UserDefaults.standard.data(forKey: key) else { return nil }
        var stale = false
        return try? URL(resolvingBookmarkData: data, options: [.withoutUI], relativeTo: nil, bookmarkDataIsStale: &stale)
    }

    static var hasRemembered: Bool {
        #if DEBUG
        if ProcessInfo.processInfo.environment["LINEWORK_LIBRARY"] != nil { return true }
        if !Set(CommandLine.arguments).isDisjoint(with: ["--snapshots", "--library-checks", "--print-counts"]) { return true }
        #endif
        return UserDefaults.standard.data(forKey: key) != nil
    }

    static func remember(_ url: URL) {
        #if DEBUG
        if ProcessInfo.processInfo.environment["LINEWORK_LIBRARY"] != nil { return }
        #endif
        if let data = try? url.bookmarkData(options: [], includingResourceValuesForKeys: nil, relativeTo: nil) {
            UserDefaults.standard.set(data, forKey: key)
        }
    }
}
