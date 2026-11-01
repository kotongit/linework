import Foundation

public enum FolderKind: Equatable, Sendable {
    case library, empty, otherContents, unavailable, newerFormat
}

/// What a chosen folder is, and how to make it a library.
public enum LibraryFolder {
    public static let folders = ["Photos", "Drawings", "Canvases", "Progress"]
    public static let manifestName = "library.json"

    public static var suggestedLocation: URL {
        FileManager.default.urls(for: .documentDirectory, in: .userDomainMask)[0].appending(path: "Linework Library", directoryHint: .isDirectory)
    }

    public static func classify(_ url: URL) -> FolderKind {
        let fm = FileManager.default
        var isDirectory: ObjCBool = false
        guard fm.fileExists(atPath: url.path, isDirectory: &isDirectory), isDirectory.boolValue,
              let contents = try? fm.contentsOfDirectory(atPath: url.path)
        else { return .unavailable }
        if let data = try? Data(contentsOf: url.appending(path: manifestName)) {
            guard let manifest = try? LibraryFiles.decoder.decode(LibraryManifest.self, from: data) else { return .otherContents }
            return manifest.format > LibraryManifest.currentFormat ? .newerFormat : .library
        }
        return contents.allSatisfy { $0.hasPrefix(".") } ? .empty : .otherContents
    }

    /// Adds Linework's folders and files, changing nothing already there. The manifest goes last,
    /// so a failure can't leave something that looks like a library; anything made is removed again.
    public static func create(at url: URL, weekOneStart: String, now: Date) throws {
        let fm = FileManager.default
        var made: [URL] = []
        do {
            for name in folders {
                let folder = url.appending(path: name, directoryHint: .isDirectory)
                let existed = fm.fileExists(atPath: folder.path)
                try fm.createDirectory(at: folder, withIntermediateDirectories: true)
                if !existed { made.append(folder) }
            }
            let files = LibraryFiles(root: url)
            if !FileManager.default.fileExists(atPath: files.url(of: .plan).path) {
                try files.write(PlanRecord(weekOneStart: weekOneStart), to: .plan)
            }
            let manifest = try LibraryFiles.encoder.encode(LibraryManifest(createdAt: now))
            try manifest.write(to: url.appending(path: manifestName), options: .atomic)
        } catch {
            for folder in made { try? fm.removeItem(at: folder) }
            throw error
        }
    }

    /// The Monday of the week holding `date`, as a day key.
    public static func monday(of date: Date, calendar: Calendar) -> String {
        var cal = calendar
        cal.firstWeekday = 2
        let start = cal.dateInterval(of: .weekOfYear, for: date)?.start ?? date
        return PlanDayKey.key(start, calendar: cal)
    }
}
