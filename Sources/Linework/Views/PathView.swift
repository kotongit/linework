import LineworkCore
import SwiftUI

/// The whole plan as one straight line of day circles, grouped by book.
struct PathView: View {
    @Environment(LibraryStore.self) private var library
    @Environment(SessionStore.self) private var store

    private static let foreverID = -1

    var body: some View {
        let schedule = library.schedule
        let ticks = library.tickKeys
        let today = store.today
        ScrollViewReader { proxy in
            ScrollView {
                LazyVStack(spacing: 0) {
                    ForEach(schedule.sequence.indices, id: \.self) { index in
                        let week = schedule.plan.weeks[schedule.sequence[index].weekIndex]
                        let previousBook = index > 0 ? schedule.plan.weeks[schedule.sequence[index - 1].weekIndex].book : nil
                        WeekPathSection(schedule: schedule, sequenceIndex: index, week: week,
                                        startsBook: week.book != previousBook, ticks: ticks, today: today)
                            .id(index)
                    }
                    ForeverNode(ref: .forever(day: PlanDayKey.key(schedule.date(sequenceIndex: schedule.sequence.count, weekday: 0), calendar: schedule.calendar)))
                        .id(Self.foreverID)
                }
                .padding(.vertical, 24)
                .frame(maxWidth: .infinity)
            }
            .onAppear { proxy.scrollTo(anchor(schedule, today: today), anchor: .center) }
        }
    }

    /// The week containing today, the first week before the start, the forever node after the end.
    private func anchor(_ schedule: PlanSchedule, today: Date) -> Int {
        switch schedule.todayRef(today) {
        case let .plan(sequenceIndex, _): sequenceIndex
        case .forever: Self.foreverID
        }
    }
}

private struct WeekPathSection: View {
    let schedule: PlanSchedule
    let sequenceIndex: Int
    let week: PlanWeek
    let startsBook: Bool
    let ticks: Set<String>
    let today: Date

    private static let rowHeight: CGFloat = 52

    var body: some View {
        VStack(spacing: 10) {
            if startsBook {
                VStack(spacing: 12) {
                    PillLabel("Book \(String(format: "%02d", week.book))")
                    Text(week.bookTitle)
                        .font(Theme.font(26, .medium))
                        .foregroundStyle(Theme.ink)
                }
                .multilineTextAlignment(.center)
                .frame(maxWidth: 560)
                .padding(.top, 36)
                .padding(.bottom, 10)
            }
            HStack(spacing: 8) {
                Text("Week \(week.weekOfBook) · \(week.theme)")
                    .font(Theme.font(14))
                    .foregroundStyle(Theme.muted)
                if schedule.sequence[sequenceIndex].isRepeat {
                    PillLabel("Repeat")
                }
            }
            ZStack {
                Rectangle().fill(Theme.muted).frame(width: 1)
                VStack(spacing: 0) {
                    ForEach(0..<7, id: \.self) { weekday in
                        let ref = PlanDayRef.plan(sequenceIndex: sequenceIndex, weekday: weekday)
                        DayNode(ref: ref, status: PlanProgress.status(for: ref, ticks: ticks, schedule: schedule),
                                isToday: ref == schedule.todayRef(today), isFuture: schedule.date(of: ref) > today, weekday: weekday,
                                help: schedule.title(for: ref))
                            .frame(height: Self.rowHeight)
                    }
                }
            }
            .frame(height: Self.rowHeight * 7)
        }
        .padding(.bottom, 10)
    }
}

private struct DayNode: View {
    let ref: PlanDayRef
    let status: PlanProgress.DayStatus
    let isToday: Bool
    let isFuture: Bool
    /// 0 for Monday.
    let weekday: Int
    let help: String
    @State private var showing = false

    private static let initials = ["M", "T", "W", "T", "F", "S", "S"]

    var body: some View {
        Button {
            showing = true
        } label: {
            node(status)
        }
        .buttonStyle(.plain)
        .help(help)
        .popover(isPresented: $showing) {
            ScrollView {
                DayPlanCard(ref: ref, bordered: false).padding(12)
            }
            .frame(width: 520, height: 460)
        }
    }

    @ViewBuilder
    private func node(_ status: PlanProgress.DayStatus) -> some View {
        let size: CGFloat = isToday ? 44 : 32
        // Future days are drawn in hairline rather than faded, so their letters stay readable on glass.
        let outline = isFuture ? Theme.hairline : Theme.muted
        ZStack {
            switch status {
            case .rest:
                Circle().strokeBorder(isToday ? Color.teal : outline, lineWidth: isToday ? 2 : 1)
                Image(systemName: "moon.fill").font(.system(size: 10)).foregroundStyle(Theme.muted)
            case .complete:
                Circle().fill(Color.teal)
                Image(systemName: "checkmark").font(.system(size: isToday ? 18 : 13, weight: .bold)).foregroundStyle(.white)
            case .partial:
                Circle().strokeBorder(Color.teal, lineWidth: 2)
                Text(Self.initials[weekday]).font(Theme.font(13, .semibold)).foregroundStyle(Theme.ink)
            case .empty:
                Circle().strokeBorder(isToday ? Color.teal : outline, lineWidth: isToday ? 2 : 1)
                Text(Self.initials[weekday])
                    .font(Theme.font(13, isToday ? .semibold : .regular))
                    .foregroundStyle(isToday ? Theme.ink : Theme.muted)
            }
        }
        .frame(width: status == .rest && !isToday ? 22 : size, height: status == .rest && !isToday ? 22 : size)
        .background(Circle().fill(Theme.nodeBacking))  // hides the line at any transparency
        .shadow(color: isToday ? Color.teal.opacity(0.7) : .clear, radius: 12)
        .contentShape(Circle())
    }
}

private struct ForeverNode: View {
    let ref: PlanDayRef
    @State private var showing = false

    var body: some View {
        Button {
            showing = true
        } label: {
            VStack(spacing: 8) {
                Asterisk(size: 28)
                Text("Forever week").font(Theme.font(26, .medium)).foregroundStyle(Theme.ink)
            }
            .padding(.vertical, 18)
        }
        .buttonStyle(.plain)
        .popover(isPresented: $showing) {
            ScrollView {
                DayPlanCard(ref: ref, bordered: false).padding(12)
            }
            .frame(width: 520, height: 400)
        }
    }
}
