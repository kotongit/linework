import LineworkCore
import SwiftUI

/// The home screen: a live clock, a greeting, section links and today's facts.
struct MainMenuView: View {
    let onOpen: (AppSection) -> Void
    let onOpenSettings: () -> Void

    @Environment(SessionStore.self) private var store
    @Environment(\.openWindow) private var openWindow
    @Environment(LibraryStore.self) private var library
    @AppStorage("linework.name") private var name = ""

    private var calendar: Calendar { PlanStore.calendar }

    var body: some View {
        let schedule = library.schedule
        VStack(alignment: .leading, spacing: 0) {
            header(schedule)
            Spacer(minLength: 24)
            hero
            Spacer(minLength: 24)
            footer(schedule: schedule)
        }
        .padding(.top, 52)
        .padding(.horizontal, 72)
        .padding(.bottom, 56)
        .frame(maxWidth: .infinity, maxHeight: .infinity)
    }

    // MARK: Header

    private func header(_ schedule: PlanSchedule) -> some View {
        HStack(spacing: 0) {
            Asterisk(size: 22, color: Theme.muted)
            Text("Linework")
                .font(Theme.font(14, .semibold))
                .foregroundStyle(Theme.ink)
                .padding(.leading, 10)
            PillLabel("Drawing companion").padding(.leading, 20)
            Spacer()
            Button("Practice") { onOpen(.today) }
                .buttonStyle(CapsuleButtonStyle())
            Button { startNext(schedule) } label: {
                Image(systemName: "arrow.down.right").font(.system(size: 16, weight: .light))
            }
            .buttonStyle(CircleButtonStyle())
            .accessibilityLabel("Start today's session")
            .padding(.leading, 12)
        }
    }

    private func startNext(_ schedule: PlanSchedule) {
        let ref = schedule.todayRef(store.today)
        if let block = schedule.nextTimedBlock(ref, ticks: library.tickKeys),
           PlanStore.start(block: block, ref: ref, library: library, store: store, openWindow: openWindow) {
            return
        }
        onOpen(.today)
    }

    // MARK: Hero

    private var hero: some View {
        VStack(alignment: .leading, spacing: 0) {
            TimelineView(.periodic(from: .now, by: 1)) { HeroClock(now: $0.date) }
            TimelineView(.everyMinute) { timeline in
                Text(LineworkCopy.greeting(hour: calendar.component(.hour, from: timeline.date), name: name))
                    .font(Theme.font(26, .medium))
                    .foregroundStyle(Theme.ink)
                    .lineLimit(2)
                    .fixedSize(horizontal: false, vertical: true)
                    .frame(maxWidth: 760, alignment: .leading)
            }
            .padding(.top, 28)
            Text(LineworkCopy.underGreeting)
                .font(Theme.font(15))
                .foregroundStyle(Theme.muted)
                .frame(maxWidth: 760, alignment: .leading)
                .padding(.top, 8)
            HStack(spacing: 28) {
                ForEach(AppSection.allCases, id: \.self) { section in
                    Button(section.title) { onOpen(section) }
                        .buttonStyle(SectionLinkStyle())
                }
            }
            .padding(.top, 28)
        }
    }

    // MARK: Footer

    private func footer(schedule: PlanSchedule) -> some View {
        HStack(alignment: .top, spacing: 48) {
            column("Artist") {
                if name.isEmpty {
                    Button("Add your name", action: onOpenSettings)
                        .buttonStyle(.plain)
                        .font(Theme.font(14, .semibold))
                        .foregroundStyle(Theme.ink)
                        .underline()
                } else {
                    Text(name).lineLimit(2)
                }
            }
            column("Date") {
                TimelineView(.everyMinute) { timeline in
                    Text(timeline.date.formatted(.dateTime.weekday(.wide).month(.wide).day().year().locale(Locale(identifier: "en_US"))))
                }
            }
            column("Local time zone") {
                Text(calendar.timeZone.identifier.replacingOccurrences(of: "_", with: " "))
            }
            column("Today's practice") {
                Text(schedule.practiceLine(schedule.todayRef(store.today))).lineLimit(2)
            }
        }
    }

    private func column(_ label: String, @ViewBuilder value: () -> some View) -> some View {
        VStack(alignment: .leading, spacing: 4) {
            Text(label).font(Theme.font(12)).foregroundStyle(Theme.muted)
            value()
                .font(Theme.font(14, .semibold))
                .foregroundStyle(Theme.ink)
                .fixedSize(horizontal: false, vertical: true)
        }
        .frame(maxWidth: .infinity, alignment: .leading)
    }
}

private struct SectionLinkStyle: ButtonStyle {
    func makeBody(configuration: Configuration) -> some View {
        configuration.label
            .font(Theme.font(15, .medium))
            .foregroundStyle(Theme.ink)
            .frame(minHeight: 44)
            .contentShape(Rectangle())
            .opacity(configuration.isPressed ? 0.6 : 1)
    }
}
