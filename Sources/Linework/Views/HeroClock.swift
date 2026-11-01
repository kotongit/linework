import LineworkCore
import SwiftUI

/// The big clock: `HH:MM`, seconds and AM/PM. Follows the 24-hour and Show seconds settings.
/// The caller owns the `TimelineView` that supplies `now`.
struct HeroClock: View {
    let now: Date

    @AppStorage("linework.clock24h") private var use24h = true
    @AppStorage("linework.clockSeconds") private var showSeconds = true

    init(now: Date) { self.now = now }

    var body: some View {
        let parts = ClockFormat.parts(now, use24h: use24h, calendar: PlanStore.calendar)
        HStack(alignment: .top, spacing: 8) {
            Text(parts.time)
                .font(Theme.font(216, .medium))
                .monospacedDigit()
                .tracking(-9)
                .lineLimit(1)
                .minimumScaleFactor(0.5)
                .foregroundStyle(Theme.ink)
                .accessibilityElement(children: .ignore)
                .accessibilityLabel(parts.time + (parts.meridiem.map { " \($0)" } ?? ""))
            VStack(alignment: .leading, spacing: 4) {
                if showSeconds {
                    Text(parts.seconds)
                        .font(Theme.font(52))
                        .monospacedDigit()
                        .foregroundStyle(Theme.muted)
                }
                if let meridiem = parts.meridiem {
                    Text(meridiem)
                        .font(Theme.font(14, .semibold))
                        .tracking(1)
                        .foregroundStyle(Theme.muted)
                }
            }
            .padding(.top, 22)
            .accessibilityHidden(true)
        }
    }
}
