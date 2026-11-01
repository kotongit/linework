import LineworkCore
import SwiftData
import SwiftUI

struct CalendarHeatmapView: View {
    @Query private var sessions: [SessionRecord]
    @Query private var checks: [PlanCheck]
    @Query private var repeatRecords: [PlanRepeatRecord]
    @Environment(SessionStore.self) private var store
    @Environment(\.colorScheme) private var scheme

    private static let lightRamp: [UInt32] = [0xEBEFF0, 0xB2E5E0, 0x5FC9BE, 0x20A396, 0x0B6E65]
    private static let darkRamp: [UInt32] = [0x1E2526, 0x0F4A45, 0x16756C, 0x22A99B, 0x4FE3D2]
    private let cell: CGFloat = 13
    private let gap: CGFloat = 3

    private var calendar: Calendar {
        var c = Calendar.current
        c.firstWeekday = 2
        return c
    }

    var body: some View {
        let cal = calendar
        let stats = daySummaries(calendar: cal)
        let grid = PracticeCalendar.grid(today: store.today, weeks: 52, calendar: cal)

        VStack(alignment: .leading, spacing: 24) {
            Button("Today") { store.route = .section(.today) }
                .buttonStyle(CapsuleButtonStyle(height: 30))
            HStack(alignment: .top, spacing: gap) {
                weekdayLabels
                    .padding(.vertical, 4)
                ScrollView(.horizontal) {
                    HStack(alignment: .top, spacing: gap) {
                        ForEach(grid.indices, id: \.self) { w in
                            VStack(alignment: .leading, spacing: gap) {
                                Text(monthLabel(grid, column: w, calendar: cal))
                                    .font(Theme.font(11))
                                    .foregroundStyle(Theme.muted)
                                    .fixedSize()
                                    .frame(width: cell, height: 14, alignment: .leading)
                                ForEach(0..<7, id: \.self) { d in
                                    square(grid[w][d], stats: stats)
                                }
                            }
                        }
                    }
                    .padding(.vertical, 4)
                    .padding(.trailing, 8)
                }
                .defaultScrollAnchor(.trailing, for: .initialOffset)
            }
            legend
        }
        .padding(.horizontal, 72)
        .frame(maxWidth: 1100, alignment: .leading)
        .frame(maxWidth: .infinity, maxHeight: .infinity, alignment: .leading)
    }

    private var weekdayLabels: some View {
        VStack(alignment: .trailing, spacing: gap) {
            Color.clear.frame(width: 26, height: 14)
            ForEach(0..<7, id: \.self) { d in
                Text(["Mon", "", "Wed", "", "Fri", "", ""][d])
                    .font(Theme.font(11))
                    .foregroundStyle(Theme.muted)
                    .frame(width: 26, height: cell, alignment: .trailing)
            }
        }
    }

    @ViewBuilder
    private func square(_ day: Date?, stats: [Date: DaySummary]) -> some View {
        if let day {
            let summary = stats[day] ?? DaySummary()
            CalendarDaySquare(day: day, cell: cell, tooltip: tooltip(day, summary)) {
                tile(level: PracticeCalendar.level(seconds: summary.seconds))
            }
        } else {
            Color.clear.frame(width: cell, height: cell)
        }
    }

    private var legend: some View {
        HStack(spacing: gap) {
            Text("Less").font(Theme.font(12)).foregroundStyle(Theme.muted).padding(.trailing, 5)
            ForEach(0..<5, id: \.self) { level in
                tile(level: level).frame(width: cell, height: cell)
            }
            Text("More").font(Theme.font(12)).foregroundStyle(Theme.muted).padding(.leading, 5)
        }
    }

    /// Level 0 is outlined so an empty day still reads on light glass.
    private func tile(level: Int) -> some View {
        RoundedRectangle(cornerRadius: 3)
            .fill(color(level: level))
            .overlay {
                if level == 0 { RoundedRectangle(cornerRadius: 3).strokeBorder(Theme.hairline, lineWidth: 1) }
            }
    }

    private func color(level: Int) -> Color {
        let hex = (scheme == .dark ? Self.darkRamp : Self.lightRamp)[min(max(level, 0), 4)]
        return Color(.sRGB, red: Double((hex >> 16) & 0xFF) / 255, green: Double((hex >> 8) & 0xFF) / 255,
                     blue: Double(hex & 0xFF) / 255)
    }

    private func tooltip(_ day: Date, _ summary: DaySummary) -> String {
        let date = day.formatted(.dateTime.month(.abbreviated).day())
        var parts: [String] = []
        if summary.sessions > 0 { parts.append("\(summary.sessions) session\(summary.sessions == 1 ? "" : "s")") }
        if summary.seconds > 0 { parts.append(DurationFormat.total(summary.seconds)) }
        if summary.ticks > 0 { parts.append("\(summary.ticks) of \(summary.blocks) checks") }
        return parts.isEmpty ? "\(date): No practice" : "\(date): \(parts.joined(separator: ", "))"
    }

    /// What each day adds up to: timed sessions and ticked blocks, without counting the same work twice.
    private func daySummaries(calendar: Calendar) -> [Date: DaySummary] {
        var sessionSeconds: [Date: Int] = [:]
        var result: [Date: DaySummary] = [:]
        for (day, stat) in PracticeCalendar.dailyStats(sessions.map { ($0.startedAt, $0.drawingSeconds) }, calendar: calendar) {
            sessionSeconds[day] = stat.seconds
            result[day] = DaySummary(seconds: stat.seconds, sessions: stat.sessions)
        }
        let schedule = PlanStore.schedule(repeatRecords)
        for (key, checked) in PlanStore.checksByDay(checks) {
            guard let day = PlanDayKey.date(key, calendar: calendar), let plan = schedule.day(on: day) else { continue }
            let ticked = plan.blocks.filter { checked.contains($0.kind.rawValue) }
            var summary = result[day] ?? DaySummary()
            summary.ticks = ticked.count
            summary.blocks = plan.blocks.count
            summary.seconds = PlanProgress.heatmapSeconds(sessionSeconds: sessionSeconds[day] ?? 0,
                                                          tickedMinutes: ticked.reduce(0) { $0 + $1.minutes })
            result[day] = summary
        }
        return result
    }

    /// Month name above the first column that contains the 1st of a month (and the first column).
    private func monthLabel(_ grid: [[Date?]], column w: Int, calendar: Calendar) -> String {
        let days = grid[w].compactMap { $0 }
        guard let first = days.first else { return "" }
        if w == 0 || days.contains(where: { calendar.component(.day, from: $0) == 1 }) {
            let anchor = days.first(where: { calendar.component(.day, from: $0) == 1 }) ?? first
            return anchor.formatted(.dateTime.month(.abbreviated))
        }
        return ""
    }
}

/// One day of the heatmap. Clicking it opens that day's card, outlined while it's open.
private struct CalendarDaySquare<Tile: View>: View {
    let day: Date
    let cell: CGFloat
    let tooltip: String
    @ViewBuilder let tile: () -> Tile
    @State private var showing = false

    var body: some View {
        Button { showing = true } label: {
            tile()
                .overlay {
                    if showing { RoundedRectangle(cornerRadius: 3).strokeBorder(Theme.ink, lineWidth: 2) }
                }
                .frame(width: cell, height: cell)
        }
        .buttonStyle(.plain)
        .help(tooltip)
        .popover(isPresented: $showing) {
            ScrollView {
                DayPlanCard(date: day, bordered: false).padding(12)
            }
            .frame(width: 520, height: 460)
        }
    }
}

private struct DaySummary {
    var seconds = 0
    var sessions = 0
    var ticks = 0
    var blocks = 0
}
