#!/usr/bin/env swift
// Draws the Linework asterisk icon and writes Resources/AppIcon.icns.
// Usage: swift scripts/make-icon.swift   (run from the repo root)
import AppKit

let outURL = URL(fileURLWithPath: "Resources/AppIcon.icns")
let iconset = FileManager.default.temporaryDirectory
    .appendingPathComponent("linework-\(UUID().uuidString).iconset")
try FileManager.default.createDirectory(at: iconset, withIntermediateDirectories: true)
defer { try? FileManager.default.removeItem(at: iconset) }

func render(pixels: Int) -> Data {
    let rep = NSBitmapImageRep(
        bitmapDataPlanes: nil, pixelsWide: pixels, pixelsHigh: pixels, bitsPerSample: 8,
        samplesPerPixel: 4, hasAlpha: true, isPlanar: false, colorSpaceName: .deviceRGB,
        bytesPerRow: 0, bitsPerPixel: 0)!
    NSGraphicsContext.saveGraphicsState()
    NSGraphicsContext.current = NSGraphicsContext(bitmapImageRep: rep)
    let s = CGFloat(pixels)
    let tile = NSRect(x: 0, y: 0, width: s, height: s).insetBy(dx: s * 0.05, dy: s * 0.05)
    NSColor(srgbRed: 0xF4 / 255, green: 0xF4 / 255, blue: 0xF2 / 255, alpha: 1).setFill()
    NSBezierPath(roundedRect: tile, xRadius: s * 0.2, yRadius: s * 0.2).fill()

    let ink = NSColor(srgbRed: 0x11 / 255, green: 0x11 / 255, blue: 0x11 / 255, alpha: 1)
    ink.setStroke()
    let c = NSPoint(x: s / 2, y: s / 2)
    let r = s * 0.29
    for k in 0..<3 {
        let a = CGFloat(k) * .pi / 3 + .pi / 2
        let p = NSBezierPath()
        p.lineWidth = s * 0.065
        p.lineCapStyle = .round
        p.move(to: NSPoint(x: c.x + r * cos(a), y: c.y + r * sin(a)))
        p.line(to: NSPoint(x: c.x - r * cos(a), y: c.y - r * sin(a)))
        p.stroke()
    }
    NSGraphicsContext.restoreGraphicsState()
    return rep.representation(using: .png, properties: [:])!
}

for base in [16, 32, 128, 256, 512] {
    try render(pixels: base).write(to: iconset.appendingPathComponent("icon_\(base)x\(base).png"))
    try render(pixels: base * 2).write(to: iconset.appendingPathComponent("icon_\(base)x\(base)@2x.png"))
}

let p = Process()
p.executableURL = URL(fileURLWithPath: "/usr/bin/iconutil")
p.arguments = ["-c", "icns", iconset.path, "-o", outURL.path]
try p.run()
p.waitUntilExit()
exit(p.terminationStatus)
