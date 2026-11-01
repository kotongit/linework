import LineworkCore
import SwiftUI

struct TodayView: View {
    @Environment(LibraryStore.self) private var library
    @Environment(SessionStore.self) private var store
    @State private var pendingRepeat: Int?
    @State private var showingGuide = false

    var body: some View {
        let today = store.today
        let schedule = library.schedule

        ScrollView {
            DayPlanCard(ref: schedule.todayRef(today), glance: true)
                .overlay(alignment: .topTrailing) { optionsMenu(schedule, today: today) }
                .padding(.horizontal, 72)
                .padding(.vertical, 40)
                .frame(maxWidth: 1100, alignment: .leading)
                .frame(maxWidth: .infinity, alignment: .leading)
        }
        .sheet(isPresented: $showingGuide) { PlanGuideSheet() }
        .alert(repeatTitle, isPresented: Binding(get: { pendingRepeat != nil }, set: { if !$0 { pendingRepeat = nil } })) {
            Button("Repeat") {
                if let pendingRepeat { library.addRepeat(weekCount: pendingRepeat, on: today) }
                pendingRepeat = nil
            }
            Button("Cancel", role: .cancel) { pendingRepeat = nil }
        } message: {
            Text(repeatMessage(schedule, today: today))
        }
    }

    private var repeatTitle: String {
        pendingRepeat == 2 ? "Repeat this week and last week?" : "Repeat this week?"
    }

    /// They run again after the current week; nothing before next Monday moves.
    private func repeatMessage(_ schedule: PlanSchedule, today: Date) -> String {
        guard let pendingRepeat else { return "" }
        let total = schedule.sequence.count + pendingRepeat
        return "\(pendingRepeat == 2 ? "They run" : "It runs") again from next Monday, and your plan will now run for \(total) weeks."
    }

    private func optionsMenu(_ schedule: PlanSchedule, today: Date) -> some View {
        Menu {
            Button("Repeat This Week…") { pendingRepeat = 1 }
                .disabled(!schedule.canAddRepeat(weekCount: 1, on: today))
            Button("Repeat This Week and Last…") { pendingRepeat = 2 }
                .disabled(!schedule.canAddRepeat(weekCount: 2, on: today))
            Button("Undo Last Repeat") { library.undoLastRepeat() }
                .disabled(!schedule.canUndoLastRepeat(today: today))
            Divider()
            Button("How This Plan Works") { showingGuide = true }
        } label: {
            Image(systemName: "ellipsis")
                .font(.system(size: 13, weight: .regular))
        }
        .menuStyle(.button)
        .buttonStyle(CircleButtonStyle(size: 30))
        .menuIndicator(.hidden)
        .fixedSize()
        .accessibilityLabel("Plan options")
    }
}
