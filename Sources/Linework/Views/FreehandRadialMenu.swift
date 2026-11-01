import LineworkCore
import SwiftUI

/// The circular menu opened by double-clicking an image: five round buttons on a 64pt radius, starting at the top.
/// Real buttons, drawn above the images at constant screen size.
struct FreehandRadialMenu: View {
    let store: FreehandStore

    static let radius = 64.0
    static let buttonSize = 36.0
    /// The glass disc reaches a little past the button's ink ring.
    private static let discSize = buttonSize + 6

    private struct Entry {
        let symbol: String
        let name: String
        let action: (FreehandStore) -> Void
    }

    private static let entries: [Entry] = [
        Entry(symbol: "arrow.up.left.and.arrow.down.right", name: "Resize") { $0.chooseTool(.resize) },
        Entry(symbol: "rotate.right", name: "Rotate") { $0.chooseTool(.rotate) },
        Entry(symbol: "arrow.left.and.right.righttriangle.left.righttriangle.right", name: "Flip") { $0.flipSelected() },
        Entry(symbol: "crop", name: "Crop") { $0.beginCrop() },
        Entry(symbol: "trash", name: "Remove") { $0.deleteSelected() },
    ]

    /// Button centers relative to the ring's center, evenly spaced, the first straight up.
    static func offsets() -> [CGPoint] {
        entries.indices.map { index in
            let angle = (-90 + 360 * Double(index) / Double(entries.count)) * .pi / 180
            return CGPoint(x: radius * cos(angle), y: radius * sin(angle))
        }
    }

    /// `point`, moved inward just far enough that every disc stays inside `viewport`, and above the floating
    /// bar, which is `barWidth` wide at the bottom center, if the ring would reach it sideways.
    static func center(for point: CGPoint, in viewport: CGSize, barWidth: Double) -> CGPoint {
        let reach = discSize / 2
        let all = offsets()
        let low = CGPoint(x: -(all.map { -$0.x }.max() ?? 0) - reach, y: -(all.map { -$0.y }.max() ?? 0) - reach)
        let high = CGPoint(x: (all.map(\.x).max() ?? 0) + reach, y: (all.map(\.y).max() ?? 0) + reach)
        func fit(_ value: Double, _ lowOffset: Double, _ highOffset: Double, _ extent: Double) -> Double {
            let lower = -lowOffset, upper = extent - highOffset
            return lower > upper ? extent / 2 : min(max(value, lower), upper)
        }
        let x = fit(point.x, low.x, high.x, viewport.width)
        let overBar = barWidth > 0 && x + low.x < (viewport.width + barWidth) / 2 && x + high.x > (viewport.width - barWidth) / 2
        let floor = viewport.height - (overBar ? FreehandBar.reservedHeight : 0)
        return CGPoint(x: x, y: fit(point.y, low.y, high.y, floor))
    }

    var body: some View {
        if let at = store.ringAt {
            let center = Self.center(for: at, in: store.viewport, barWidth: store.barWidth)
            ZStack {
                ForEach(Array(Self.entries.enumerated()), id: \.offset) { index, entry in
                    let offset = Self.offsets()[index]
                    Button { entry.action(store) } label: {
                        Image(systemName: entry.symbol).font(.system(size: 14, weight: .regular))
                    }
                    .buttonStyle(CircleButtonStyle(size: Self.buttonSize))
                    .background(Circle().fill(.ultraThinMaterial).frame(width: Self.discSize, height: Self.discSize))
                    .background(Circle().fill(Theme.ground.opacity(0.55)).frame(width: Self.discSize, height: Self.discSize))
                    .help(entry.name)
                    .accessibilityLabel(entry.name)
                    .position(x: center.x + offset.x, y: center.y + offset.y)
                }
            }
            .frame(maxWidth: .infinity, maxHeight: .infinity)
        }
    }
}

/// Resize corners or the rotate handle on the selected image, drawn at constant screen size.
/// They don't take hits: the store tests for them on mouse down.
struct FreehandHandles: View {
    let store: FreehandStore

    private static let markSize = 10.0

    var body: some View {
        let camera = store.document.camera
        if let item = store.document.items.first(where: { $0.id == store.selectedID }), store.tool != .none {
            ZStack {
                switch store.tool {
                case .none:
                    EmptyView()
                case .resize:
                    ForEach(Array(FreehandGeometry.corners(of: item).enumerated()), id: \.offset) { entry in
                        Rectangle()
                            .fill(Theme.ground)
                            .overlay(Rectangle().strokeBorder(Theme.ink, lineWidth: 1))
                            .frame(width: Self.markSize, height: Self.markSize)
                            .position(camera.toScreen(entry.element))
                    }
                case .rotate:
                    let corners = FreehandGeometry.corners(of: item)
                    let top = camera.toScreen(CGPoint(x: (corners[0].x + corners[1].x) / 2, y: (corners[0].y + corners[1].y) / 2))
                    let handle = camera.toScreen(FreehandGeometry.rotateHandle(of: item, zoom: camera.zoom))
                    Path { p in
                        p.move(to: top)
                        p.addLine(to: handle)
                    }
                    .stroke(Theme.ink, lineWidth: 1)
                    Circle()
                        .fill(Theme.ground)
                        .overlay(Circle().strokeBorder(Theme.ink, lineWidth: 1))
                        .frame(width: Self.markSize + 4, height: Self.markSize + 4)
                        .position(handle)
                }
            }
            .allowsHitTesting(false)
        }
    }
}
