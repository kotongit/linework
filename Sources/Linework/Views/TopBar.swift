import LineworkCore
import SwiftData
import SwiftUI

/// Slim strip at the top of every section: menu mark, section pills, streak and Start Session.
/// 52pt tall, with room on the left for the traffic lights.
struct TopBar: View {
    @Binding var section: AppRoute
    let onStartSession: () -> Void

    @Environment(SessionStore.self) private var store
    @Query private var checks: [PlanCheck]
    @Query private var sessions: [SessionRecord]

    private var streak: Int {
        PlanProgress.currentStreak(practiceDays: PlanStore.practiceDays(checks: checks, sessions: sessions),
                                   today: store.today, calendar: PlanStore.calendar)
    }

    var body: some View {
        ZStack {
            HStack(spacing: 6) {
                ForEach(AppSection.allCases, id: \.self) { item in
                    Button(item.title) { section = .section(item) }
                        .buttonStyle(CapsuleButtonStyle(height: 30, filled: section == .section(item)))
                        .accessibilityAddTraits(section == .section(item) ? .isSelected : [])
                }
            }
            HStack {
                Button { section = .menu } label: {
                    Asterisk(size: 20).frame(width: 30, height: 30).contentShape(Rectangle())
                }
                .buttonStyle(.plain)
                .accessibilityLabel("Main menu")
                .help("Main menu")
                .padding(.leading, 84)
                Spacer()
                HStack(spacing: 4) {
                    Image(systemName: "flame.fill")
                    Text("\(streak)").monospacedDigit()
                }
                .font(Theme.font(13, .medium))
                .foregroundStyle(Theme.ink)
                .accessibilityElement(children: .ignore)
                .accessibilityLabel("Streak, \(streak) days")
                Button("Start Session", action: onStartSession)
                    .buttonStyle(CapsuleButtonStyle(height: 30, filled: true))
                    .padding(.leading, 12)
                    .padding(.trailing, 16)
            }
        }
        .frame(height: 52)
        .overlay(alignment: .bottom) { Rectangle().fill(Theme.hairline).frame(height: 1) }
    }
}
