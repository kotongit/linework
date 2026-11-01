import Foundation

/// The window's see-through setting: transparency is what the user picks, tint opacity is what gets drawn.
public enum Glass {
    public static let defaultTransparency = 0.33
    public static let range = 0.0...0.8

    public static func tintOpacity(transparency: Double) -> Double {
        1 - min(max(transparency, range.lowerBound), range.upperBound)
    }
}
