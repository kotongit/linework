import LineworkCore
import SwiftData
import SwiftUI

/// The home screen: a live clock, a greeting, section links and today's facts.
struct MainMenuView: View {
    let onOpen: (AppSection) -> Void
    let onOpenSettings: () -> Void

    @Environment(SessionStore.self) private var store
    @Environment(\.openWindow) private var openWindow
    @Query private var checks: [PlanCheck]
    @Query private var repeatRecords: [PlanRepeatRecord]
    @Query private var images: [RefImage]
    @AppStorage("linework.name") private var name = ""

    private var calendar: Calendar { PlanStore.calendar }

    var body: some View {
        TimelineView(.periodic(from: .now, by: 1)) { timeline in
            content(timeline.date)
        }
        .padding(.top, 52)
        .padding(.horizontal, 72)
        .padding(.bottom, 56)
        .frame(maxWidth: .infinity, maxHeight: .infinity)
    }

    private func content(_ now: Date) -> some View {
        let schedule = PlanStore.schedule(repeatRecords)
        return VStack(alignment: .leading, spacing: 0) {
            header(schedule)
            Spacer(minLength: 24)
            hero(now)
            Spacer(minLength: 24)
            footer(now, schedule: schedule)
        }
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
        let dayKey = PlanDayKey.key(store.today, calendar: calendar)
        let ticked = PlanStore.checksByDay(checks)[dayKey] ?? []
        if let block = schedule.nextTimedBlock(on: store.today, checked: ticked),
           !images.isEmpty,
           PlanStore.start(block: block, dayKey: dayKey, images: images, store: store, openWindow: openWindow) {
            return
        }
        onOpen(.today)
    }

    // MARK: Hero

    private func hero(_ now: Date) -> some View {
        let hour = calendar.component(.hour, from: now)
        return VStack(alignment: .leading, spacing: 0) {
            HeroClock(now: now)
            Text(LineworkCopy.greeting(hour: hour, name: name))
                .font(Theme.font(26, .medium))
                .foregroundStyle(Theme.ink)
                .lineLimit(2)
                .fixedSize(horizontal: false, vertical: true)
                .frame(maxWidth: 760, alignment: .leading)
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

    private func footer(_ now: Date, schedule: PlanSchedule) -> some View {
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
                Text(now.formatted(.dateTime.weekday(.wide).month(.wide).day().year().locale(Locale(identifier: "en_US"))))
            }
            column("Local time zone") {
                Text(calendar.timeZone.identifier.replacingOccurrences(of: "_", with: " "))
            }
            column("Today's practice") {
                Text(schedule.practiceLine(on: store.today)).lineLimit(2)
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
