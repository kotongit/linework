import CryptoKit
import Foundation

public struct ScannedFile: Sendable {
    public let name: String
    public let size: Int
    /// In a synced folder and not yet downloaded.
    public let isPlaceholder: Bool

    public init(name: String, size: Int, isPlaceholder: Bool) {
        self.name = name
        self.size = size
        self.isPlaceholder = isPlaceholder
    }
}

public struct PhotoScanResult: Sendable {
    public var added: [String] = []
    public var adopted: [(recordID: UUID, newName: String)] = []
    public var missing: [UUID] = []
    public var restored: [UUID] = []
    /// `Photos/` is absent or empty while photos are on record. Treated as out of reach, never as deletion.
    public var folderUnavailable = false
}

/// What changes made in Finder mean. Pure logic: the caller lists the folder and applies the result.
public enum PhotoScan {
    private static let imageExtensions: Set<String> = ["jpg", "jpeg", "png", "heic", "heif", "webp", "tif", "tiff", "gif", "bmp"]

    public static func isImageName(_ name: String) -> Bool {
        imageExtensions.contains((name as NSString).pathExtension.lowercased())
    }

    private static func comparable(_ name: String) -> String {
        name.precomposedStringWithCanonicalMapping.lowercased()
    }

    public static func decide(records: [PhotoRecord], files: [ScannedFile], folderExists: Bool, hash: (String) throws -> String) -> PhotoScanResult {
        var result = PhotoScanResult()
        let visible = files.filter { !$0.name.hasPrefix(".") && isImageName($0.name) }
        if !records.isEmpty, !folderExists || visible.isEmpty {
            result.folderUnavailable = true
            return result
        }
        let names = Set(visible.map { comparable($0.name) })
        let known = Set(records.map { comparable($0.fileName) })
        var gone: [PhotoRecord] = []
        for record in records {
            if names.contains(comparable(record.fileName)) {
                if record.missing { result.restored.append(record.id) }
            } else {
                gone.append(record)
            }
        }
        for file in visible where !known.contains(comparable(file.name)) && !file.isPlaceholder {
            // Hashing is only worth it if the new file might be a renamed one.
            if !gone.isEmpty, let digest = try? hash(file.name), let index = gone.firstIndex(where: { $0.sha256 == digest }) {
                result.adopted.append((gone[index].id, file.name))
                gone.remove(at: index)
            } else {
                result.added.append(file.name)
            }
        }
        result.missing = gone.filter { !$0.missing }.map(\.id)
        return result
    }

    /// `wanted`, or "name 2.ext" and so on if it's taken. Compared the way the disk compares names.
    public static func uniqueName(_ wanted: String, taken: Set<String>) -> String {
        let used = Set(taken.map(comparable))
        guard used.contains(comparable(wanted)) else { return wanted }
        let ns = wanted as NSString
        let suffix = ns.pathExtension.isEmpty ? "" : ".\(ns.pathExtension)"
        var number = 2
        while used.contains(comparable("\(ns.deletingPathExtension) \(number)\(suffix)")) { number += 1 }
        return "\(ns.deletingPathExtension) \(number)\(suffix)"
    }
}

public enum FileHash {
    /// Reads in pieces, so a very large file isn't held in memory.
    public static func sha256(of url: URL) throws -> String {
        let handle = try FileHandle(forReadingFrom: url)
        defer { try? handle.close() }
        var hasher = SHA256()
        while let chunk = try handle.read(upToCount: 1 << 20), !chunk.isEmpty { hasher.update(data: chunk) }
        return hasher.finalize().map { String(format: "%02x", $0) }.joined()
    }
}
