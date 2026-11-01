import SwiftUI

/// Capsule button: 1pt ink border, 12pt medium uppercase. `filled` inverts to ink fill.
struct CapsuleButtonStyle: ButtonStyle {
    var height: CGFloat = 44
    var filled = false

    func makeBody(configuration: Configuration) -> some View {
        configuration.label
            .font(Theme.font(12, .medium))
            .textCase(.uppercase)
            .tracking(0.7)
            .foregroundStyle(filled ? Theme.ground : Theme.ink)
            .padding(.horizontal, 18)
            .frame(minHeight: height)
            .background(Capsule(style: .circular).fill(filled ? Theme.ink : Color.clear))
            .overlay(Capsule(style: .circular).strokeBorder(Theme.ink, lineWidth: 1))
            .contentShape(Capsule(style: .circular))
            .opacity(configuration.isPressed ? 0.6 : 1)
    }
}

/// A toggle drawn as a capsule: filled in ink when on.
struct CapsuleToggleStyle: ToggleStyle {
    var height: CGFloat = 30

    func makeBody(configuration: Configuration) -> some View {
        Button { configuration.isOn.toggle() } label: { configuration.label }
            .buttonStyle(CapsuleButtonStyle(height: height, filled: configuration.isOn))
            .accessibilityAddTraits(configuration.isOn ? .isSelected : [])
    }
}

/// Round icon button with a 1pt ink border.
struct CircleButtonStyle: ButtonStyle {
    var size: CGFloat = 44

    func makeBody(configuration: Configuration) -> some View {
        configuration.label
            .font(Theme.font(12, .medium))
            .foregroundStyle(Theme.ink)
            .frame(width: size, height: size)
            .overlay(Circle().strokeBorder(Theme.ink, lineWidth: 1))
            .contentShape(Circle())
            .opacity(configuration.isPressed ? 0.6 : 1)
    }
}

/// Non-interactive label in the same capsule shape.
struct PillLabel: View {
    let text: String

    init(_ text: String) { self.text = text }

    var body: some View {
        Text(text)
            .font(Theme.font(12, .medium))
            .textCase(.uppercase)
            .tracking(0.7)
            .foregroundStyle(Theme.ink)
            .padding(.horizontal, 12)
            .padding(.vertical, 5)
            .overlay(Capsule(style: .circular).strokeBorder(Theme.ink, lineWidth: 1))
    }
}

/// Six-armed mark: three crossing 2pt strokes. Decorative.
struct Asterisk: View {
    var size: CGFloat = 24
    var color: Color = Theme.ink

    var body: some View {
        Canvas { context, canvasSize in
            let c = CGPoint(x: canvasSize.width / 2, y: canvasSize.height / 2)
            let r = min(canvasSize.width, canvasSize.height) / 2 - 1
            for k in 0..<3 {
                let a = Double(k) * .pi / 3 + .pi / 2
                var p = Path()
                p.move(to: CGPoint(x: c.x + r * cos(a), y: c.y + r * sin(a)))
                p.addLine(to: CGPoint(x: c.x - r * cos(a), y: c.y - r * sin(a)))
                context.stroke(p, with: .foreground, style: StrokeStyle(lineWidth: 2, lineCap: .round))
            }
        }
        .frame(width: size, height: size)
        .foregroundStyle(color)
        .accessibilityHidden(true)
    }
}
