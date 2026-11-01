import LineworkCore
import SwiftUI

extension PlanBlockKind {
    var label: String {
        switch self {
        case .warmup: "Warm-up"
        case .focus: "Focus"
        case .check: "Check"
        case .exercise: "Exercise"
        case .blockA: "Block A"
        case .blockB: "Block B"
        case .ritual: "Monthly ritual"
        }
    }
}

/// One day's practice: what the plan says, with a checkmark per block. Any lesson can be ticked at any time.
struct DayPlanCard: View {
    let ref: PlanDayRef
    /// Off where the card already sits in a frame of its own, like a popover.
    var bordered = true
    /// Today's list: just the blocks, for reading at a glance. The full card stays in the Path and Calendar.
    var glance = false
    @Environment(LibraryStore.self) private var library
    @Environment(SessionStore.self) private var store
    @Environment(\.openWindow) private var openWindow

    var body: some View {
        let schedule = library.schedule
        let day = schedule.day(for: ref)
        VStack(alignment: .leading, spacing: 20) {
            header(schedule)
            if day.isRest {
                if !glance { rule }
                Text(LineworkCopy.restDay).font(Theme.font(20, .medium))
            } else {
                rows(day)
            }
        }
        .padding(glance ? 0 : bordered ? 28 : 8)
        .frame(maxWidth: .infinity, alignment: .leading)
        .overlay {
            if bordered, !glance { RoundedRectangle(cornerRadius: 16).strokeBorder(Theme.hairline, lineWidth: 1) }
        }
        .foregroundStyle(Theme.ink)
    }

    private var rule: some View {
        Rectangle().fill(Theme.hairline).frame(height: 1)
    }

    @ViewBuilder
    private func header(_ schedule: PlanSchedule) -> some View {
        if glance {
            Text(schedule.title(for: ref))
                .font(Theme.font(14))
                .foregroundStyle(Theme.muted)
        } else {
            VStack(alignment: .leading, spacing: 6) {
                Text(schedule.title(for: ref))
                    .font(Theme.font(26, .medium))
                    .padding(.bottom, 4)
                if case let .plan(sequenceIndex, _) = ref {
                    let entry = schedule.sequence[sequenceIndex]
                    let week = schedule.plan.weeks[entry.weekIndex]
                    HStack(spacing: 10) {
                        Text("Book \(String(format: "%02d", week.book)) · Week \(week.weekOfBook) of \(week.weeksInBook) · \(week.theme)")
                            .font(Theme.font(14, .semibold))
                        if entry.isRepeat { PillLabel("Repeat") }
                    }
                    Group {
                        Text(week.bookTitle)
                        Text("Read: \(week.readingShort)")
                    }
                    .font(Theme.font(14))
                    .foregroundStyle(Theme.muted)
                } else {
                    Text("Your focus is your current weakest skill")
                        .font(Theme.font(14, .semibold))
                }
            }
        }
    }

    private func rows(_ day: PlanDay) -> some View {
        VStack(alignment: .leading, spacing: 0) {
            ForEach(day.blocks, id: \.kind) { block in
                if !glance || block.kind != day.blocks.first?.kind { rule }
                PlanBlockRow(block: block, isChecked: library.isTicked(ref, block.kind), glance: glance,
                             hasImages: !library.visiblePhotos.isEmpty, onToggle: { toggle(block) }, onStart: { start(block) })
                    .padding(.top, glance && block.kind == day.blocks.first?.kind ? 0 : 18)
                    .padding(.bottom, block.kind == day.blocks.last?.kind ? 0 : 18)
            }
        }
        // A new day starts with every row collapsed.
        .id(ref)
    }

    private func toggle(_ block: PlanBlock) {
        guard library.toggleTick(ref, block.kind) else { return }
        if let celebration = PlanProgress.celebration(for: ref, ticks: library.tickKeys, schedule: library.schedule) {
            store.celebrate(celebration, isToday: ref == library.schedule.todayRef(store.today))
        }
    }

    private func start(_ block: PlanBlock) {
        PlanStore.start(block: block, ref: ref, library: library, store: store, openWindow: openWindow)
    }
}

/// A block as a title and bullets. More reveals the plan's full text in place.
private struct PlanBlockRow: View {
    let block: PlanBlock
    let isChecked: Bool
    let glance: Bool
    let hasImages: Bool
    let onToggle: () -> Void
    let onStart: () -> Void
    @State private var expanded = false

    var body: some View {
        HStack(alignment: .top, spacing: 16) {
            Button(action: onToggle) {
                ZStack {
                    if isChecked {
                        Circle().fill(Color.teal)
                        Image(systemName: "checkmark")
                            .font(.system(size: 12, weight: .bold))
                            .foregroundStyle(.white)
                    } else {
                        Circle().strokeBorder(Theme.ink, lineWidth: 1)
                    }
                }
                .frame(width: 26, height: 26)
                .contentShape(Circle())
            }
            .buttonStyle(.plain)
            .help(isChecked ? "Untick" : "Tick")

            VStack(alignment: .leading, spacing: 6) {
                if glance {
                    Text(block.kind == .warmup ? block.kind.label : block.title)
                        .font(Theme.font(20, .medium))
                } else {
                    Text("\(block.kind.label) · \(block.minutes) min · \(block.title)")
                        .font(Theme.font(15, .semibold))
                        .padding(.top, 3)
                }
                ForEach(block.bullets, id: \.self) { bullet in
                    HStack(alignment: .firstTextBaseline, spacing: 8) {
                        Text("–").foregroundStyle(Theme.muted)
                        Text(bullet)
                    }
                    .font(Theme.font(14))
                    .foregroundStyle(isChecked ? Theme.muted : Theme.ink)
                }
                if expanded {
                    Text(block.text)
                        .font(Theme.font(14))
                        .foregroundStyle(Theme.muted)
                        .lineSpacing(3)
                        .fixedSize(horizontal: false, vertical: true)
                        .textSelection(.enabled)
                        .padding(.top, 4)
                }
                // Long class sessions don't fit beside More in a popover.
                ViewThatFits(in: .horizontal) {
                    HStack(spacing: 16) { actions }
                    VStack(alignment: .leading, spacing: 4) { actions }
                }
                .padding(.top, 4)
            }
            Spacer(minLength: 0)
        }
    }

    @ViewBuilder
    private var actions: some View {
        if let timed = block.timed {
            Button("Start \(stepsLabel(timed))", action: onStart)
                .buttonStyle(CapsuleButtonStyle(height: 30))
                .fixedSize()
                .disabled(!hasImages)
                .opacity(hasImages ? 1 : 0.4)
                .help(hasImages ? "Timed session; finishing it ticks this" : "Add photos to your library first")
        }
        if !glance {
            Button(expanded ? "Less" : "More") { expanded.toggle() }
                .buttonStyle(.plain)
                .font(Theme.font(13, .medium))
                .foregroundStyle(Theme.muted)
                .underline()
                .frame(minHeight: 30)
                .contentShape(Rectangle())
        }
    }

    private func stepsLabel(_ steps: [ClassStep]) -> String {
        steps.map { "\($0.count) × \(DurationFormat.short($0.seconds))" }.joined(separator: " + ")
    }
}
