import SwiftUI

struct AboutView: View {
    private var version: String {
        Bundle.main.infoDictionary?["CFBundleShortVersionString"] as? String ?? "dev"
    }

    var body: some View {
        ZStack {
            GlassBackground()
            VStack(alignment: .leading, spacing: 10) {
                Asterisk(size: 32).padding(.bottom, 8)
                Text("Linework Drawing Companion")
                    .font(Theme.font(24, .semibold))
                Text("Version \(version)").foregroundStyle(Theme.muted)
                Text("by hyprsea solutions")
                Text("s.e.a., your synthetic empathetic ally").foregroundStyle(Theme.muted)
            }
            .font(Theme.font(14))
            .foregroundStyle(Theme.ink)
            .padding(32)
            .frame(maxWidth: .infinity, maxHeight: .infinity, alignment: .leading)
        }
        .frame(width: 420, height: 240)
        .lineworkWindow()
    }
}
