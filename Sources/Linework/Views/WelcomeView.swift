import LineworkCore
import SwiftUI

/// First-launch screen: s.e.a. introduces itself and asks for the user's name.
struct WelcomeView: View {
    var onDone: () -> Void

    @AppStorage("linework.name") private var storedName = ""
    @AppStorage("linework.welcomed") private var welcomed = false
    @State private var name = ""
    @FocusState private var focused: Bool

    private var trimmed: String { LineworkCopy.normalizedName(name) }

    var body: some View {
        ZStack {
            VStack(alignment: .leading, spacing: 24) {
                Asterisk(size: 40)
                Text("Hi, I'm s.e.a.")
                    .font(Theme.font(64, .semibold))
                    .foregroundStyle(Theme.ink)
                Text("Your synthetic empathetic ally. I'll be here while you practice. What should I call you?")
                    .font(Theme.font(20))
                    .foregroundStyle(Theme.muted)
                    .fixedSize(horizontal: false, vertical: true)
                    .frame(maxWidth: 520, alignment: .leading)
                VStack(spacing: 6) {
                    TextField("", text: $name)
                        .textFieldStyle(.plain)
                        .font(Theme.font(40, .medium))
                        .foregroundStyle(Theme.ink)
                        .accessibilityLabel("Your name")
                        .focused($focused)
                        .onSubmit(continueTapped)
                        .onChange(of: name) { _, new in
                            if new.count > LineworkCopy.maxNameLength { name = String(new.prefix(LineworkCopy.maxNameLength)) }
                        }
                    Rectangle().fill(Theme.ink).frame(height: 1)
                }
                .frame(maxWidth: 520)
                HStack(spacing: 20) {
                    Button("Continue", action: continueTapped)
                        .buttonStyle(CapsuleButtonStyle(filled: true))
                        .disabled(trimmed.isEmpty)
                        .opacity(trimmed.isEmpty ? 0.4 : 1)
                    Button("Skip for now") {
                        welcomed = true
                        onDone()
                    }
                    .buttonStyle(.plain)
                    .font(Theme.font(14, .medium))
                    .foregroundStyle(Theme.muted)
                }
                .padding(.top, 8)
            }
            .padding(.horizontal, 96)
            .frame(maxWidth: .infinity, maxHeight: .infinity, alignment: .leading)
        }
        .onAppear { focused = true }
    }

    private func continueTapped() {
        guard !trimmed.isEmpty else { return }
        storedName = trimmed
        welcomed = true
        onDone()
    }
}
