import Foundation

public enum CanvasNameError: Error, Equatable {
    case invalid, duplicate
}

/// The library's `Canvases/` folder, where each canvas is a folder named after it.
public struct CanvasDirectory: Sendable {
    public let canvasesRoot: URL

    public init(canvasesRoot: URL) {
        self.canvasesRoot = canvasesRoot
    }

    public func url(of name: String) -> URL {
        canvasesRoot.appending(path: name, directoryHint: .isDirectory)
    }

    public func list() -> [String] {
        let urls = (try? FileManager.default.contentsOfDirectory(at: canvasesRoot, includingPropertiesForKeys: [.isDirectoryKey],
                                                                 options: .skipsHiddenFiles)) ?? []
        return urls.filter { (try? $0.resourceValues(forKeys: [.isDirectoryKey]).isDirectory) == true }
            .map(\.lastPathComponent)
            .sorted { $0.localizedStandardCompare($1) == .orderedAscending }
    }

    private func same(_ a: String, _ b: String) -> Bool {
        a.precomposedStringWithCanonicalMapping.lowercased() == b.precomposedStringWithCanonicalMapping.lowercased()
    }

    private func clean(_ name: String) throws -> String {
        let trimmed = name.trimmingCharacters(in: .whitespacesAndNewlines)
        guard !trimmed.isEmpty, !trimmed.hasPrefix("."), !trimmed.contains("/"), !trimmed.contains(":") else { throw CanvasNameError.invalid }
        return trimmed
    }

    private func nextFree(_ base: String) -> String {
        let existing = list()
        if !existing.contains(where: { same($0, base) }) { return base }
        var number = 2
        while existing.contains(where: { same($0, "\(base) \(number)") }) { number += 1 }
        return "\(base) \(number)"
    }

    /// A nil name picks the next free "Untitled".
    @discardableResult
    public func create(name: String?) throws -> String {
        let chosen = try name.map(clean) ?? nextFree("Untitled")
        if list().contains(where: { same($0, chosen) }) { throw CanvasNameError.duplicate }
        // No intermediates: if the library has gone, this fails rather than making a new one.
        try FileManager.default.createDirectory(at: url(of: chosen), withIntermediateDirectories: false)
        return chosen
    }

    public func rename(_ old: String, to new: String) throws {
        let chosen = try clean(new)
        if chosen == old { return }
        if list().contains(where: { same($0, chosen) && !same($0, old) }) { throw CanvasNameError.duplicate }
        let fm = FileManager.default
        if same(old, chosen) {
            // The disk sees these as one name, so it takes two steps.
            let step = url(of: ".renaming-\(UUID().uuidString)")
            try fm.moveItem(at: url(of: old), to: step)
            try fm.moveItem(at: step, to: url(of: chosen))
        } else {
            try fm.moveItem(at: url(of: old), to: url(of: chosen))
        }
    }

    public func duplicate(_ name: String) throws -> String {
        let copy = nextFree("\(name) copy")
        try FileManager.default.copyItem(at: url(of: name), to: url(of: copy))
        return copy
    }

    /// A library always has a canvas. Returns the first, making one if needed.
    public func ensureOne() throws -> String {
        try list().first ?? create(name: nil)
    }
}
