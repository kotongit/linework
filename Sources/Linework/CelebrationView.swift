import AppKit
import LineworkCore
import SwiftUI

/// Confetti and a banner when a day, week or book is finished. Reduce Motion shows the banner only.
struct CelebrationView: View {
    let kind: PlanProgress.Celebration
    var isToday = true
    let onDone: () -> Void
    @Environment(\.accessibilityReduceMotion) private var reduceMotion
    @AppStorage("linework.sound") private var soundOn = true
    @State private var start = Date.now

    private static let duration = 2.5

    private var particleCount: Int {
        switch kind {
        case .day: 60
        case .week: 140
        case .book: 260
        }
    }

    var body: some View {
        ZStack {
            if !reduceMotion {
                TimelineView(.animation) { context in
                    Canvas { canvas, size in
                        draw(in: &canvas, size: size, elapsed: context.date.timeIntervalSince(start))
                    }
                }
            }
            Text(LineworkCopy.celebration(kind, isToday: isToday))
                .font(Theme.font(26, .medium))
                .foregroundStyle(Theme.ground)
                .padding(.horizontal, 32).padding(.vertical, 16)
                .background(Theme.ink, in: Capsule())
        }
        .allowsHitTesting(false)
        .task {
            start = .now
            if soundOn { NSSound(named: "Hero")?.play() }
            try? await Task.sleep(for: .seconds(Self.duration))
            onDone()
        }
    }

    private func draw(in canvas: inout GraphicsContext, size: CGSize, elapsed: Double) {
        let palette: [Color] = [.teal, Color(red: 0.37, green: 0.79, blue: 0.75), Color(red: 0.04, green: 0.43, blue: 0.40), Theme.ink]
        for i in 0..<particleCount {
            // Stable per-particle values, so each one follows a smooth path.
            let a = Double((i &* 2654435761) % 1000) / 1000, b = Double((i &* 40503) % 1000) / 1000
            let x = size.width * a + sin(elapsed * 3 + b * 6) * 24
            let y = -30 + (size.height + 60) * min(elapsed / Self.duration * (0.6 + b), 1.2) - b * 120
            let side = 5 + b * 7
            var piece = canvas
            piece.translateBy(x: x, y: y)
            piece.rotate(by: .radians(elapsed * 5 * (a - 0.5) * 4))
            piece.fill(Path(CGRect(x: -side / 2, y: -side / 2, width: side, height: side * 0.6)),
                       with: .color(palette[i % palette.count]))
        }
    }
}
