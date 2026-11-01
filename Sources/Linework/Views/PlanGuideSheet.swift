import LineworkCore
import SwiftUI

/// "How this plan works": the rules and warm-up table from the plan.
struct PlanGuideSheet: View {
    @Environment(\.dismiss) private var dismiss

    var body: some View {
        VStack(alignment: .leading, spacing: 0) {
            ScrollView {
                Text(PracticePlan.shared.guideText)
                    .font(Theme.font(14))
                    .foregroundStyle(Theme.ink)
                    .lineSpacing(3)
                    .textSelection(.enabled)
                    .frame(maxWidth: .infinity, alignment: .leading)
                    .padding(32)
            }
            Rectangle().fill(Theme.hairline).frame(height: 1)
            HStack {
                Spacer()
                Button("Done") { dismiss() }
                    .buttonStyle(CapsuleButtonStyle(height: 30, filled: true))
                    .keyboardShortcut(.defaultAction)
            }
            .padding(16)
        }
        .frame(width: 640, height: 640)
        .background(Theme.ground)
    }
}
