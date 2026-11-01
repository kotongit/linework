import AppKit
import CoreText
import SwiftUI

/// Linework design tokens: dynamic colors, the Inter Tight font and its registration.
enum Theme {
    private static func dynamic(light: NSColor, dark: NSColor) -> Color {
        Color(nsColor: NSColor(name: nil) { appearance in
            appearance.bestMatch(from: [.aqua, .darkAqua]) == .darkAqua ? dark : light
        })
    }

    private static func hex(_ value: UInt32, alpha: CGFloat = 1) -> NSColor {
        NSColor(srgbRed: CGFloat((value >> 16) & 0xFF) / 255,
                green: CGFloat((value >> 8) & 0xFF) / 255,
                blue: CGFloat(value & 0xFF) / 255, alpha: alpha)
    }

    static let ink = dynamic(light: hex(0x111111), dark: hex(0xF4F4F2))
    /// Ink at 63%, so it keeps its contrast over whatever shows through the glass.
    static let muted = dynamic(light: hex(0x111111, alpha: 0.63), dark: hex(0xF4F4F2, alpha: 0.63))
    static let hairline = dynamic(light: hex(0x111111, alpha: 0.18), dark: hex(0xF4F4F2, alpha: 0.18))
    static let groundLight = dynamic(light: hex(0xF4F4F2), dark: hex(0xF4F4F2))
    static let groundDark = dynamic(light: hex(0x0E0E0E), dark: hex(0x0E0E0E))
    /// The ground tint for the current appearance.
    static let ground = dynamic(light: hex(0xF4F4F2), dark: hex(0x0E0E0E))

    /// Solid ground, for anything that must hide what's behind it at any transparency.
    static let nodeBacking = ground

    private static let fontFamily = "Inter Tight"
    nonisolated(unsafe) private static var fontsRegistered = false

    /// Inter Tight if registered, otherwise the system font.
    static func font(_ size: CGFloat, _ weight: Font.Weight = .regular) -> Font {
        if fontsRegistered {
            return .custom(fontFamily, size: size).weight(weight)
        }
        return .system(size: size, weight: weight)
    }

    /// Registers the bundled fonts. Looks in `Resources/Fonts`, or `LINEWORK_FONT_DIR` in debug builds.
    static func registerFonts() {
        var dirs: [URL] = []
        if let res = Bundle.main.resourceURL { dirs.append(res.appendingPathComponent("Fonts")) }
        #if DEBUG
        if let path = ProcessInfo.processInfo.environment["LINEWORK_FONT_DIR"] {
            dirs.append(URL(fileURLWithPath: path))
        }
        #endif
        for dir in dirs {
            let files = (try? FileManager.default.contentsOfDirectory(at: dir, includingPropertiesForKeys: nil)) ?? []
            for file in files where file.pathExtension.lowercased() == "ttf" {
                if CTFontManagerRegisterFontsForURL(file as CFURL, .process, nil) {
                    fontsRegistered = true
                }
            }
        }
        if !fontsRegistered {
            // Already registered earlier in this process counts as available.
            fontsRegistered = !(NSFontManager.shared.availableMembers(ofFontFamily: fontFamily) ?? []).isEmpty
        }
    }
}
