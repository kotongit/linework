import LineworkCore
import SwiftUI

struct LineworkSettingsView: View {
    @AppStorage("linework.name") private var storedName = ""
    @AppStorage("linework.welcomed") private var welcomed = false
    @AppStorage("linework.clock24h") private var use24h = true
    @AppStorage("linework.clockSeconds") private var showSeconds = true
    @AppStorage("linework.notes") private var freehandNotes = true
    @AppStorage("linework.transparency") private var transparency = Glass.defaultTransparency
    @State private var name = ""

    var body: some View {
        VStack(alignment: .leading, spacing: 20) {
            VStack(alignment: .leading, spacing: 6) {
                Text("Your name").font(Theme.font(12, .medium)).foregroundStyle(Theme.muted)
                TextField("", text: $name)
                    .textFieldStyle(.plain)
                    .font(Theme.font(20, .medium))
                    .accessibilityLabel("Your name")
                    .onSubmit(save)
                    .onChange(of: name) { _, new in
                        if new.count > LineworkCopy.maxNameLength { name = String(new.prefix(LineworkCopy.maxNameLength)) }
                        save()
                    }
                Rectangle().fill(Theme.ink).frame(height: 1)
            }
            Toggle("24-hour clock", isOn: $use24h)
            Toggle("Show seconds", isOn: $showSeconds)
            Toggle("Notes from s.e.a.", isOn: $freehandNotes)
            VStack(alignment: .leading, spacing: 6) {
                HStack {
                    Text("Transparency")
                    Spacer()
                    Text("\(Int((clamped * 100).rounded()))%").foregroundStyle(Theme.muted).monospacedDigit()
                }
                Slider(value: Binding(get: { clamped }, set: { transparency = $0 }), in: Glass.range)
            }
        }
        .font(Theme.font(14))
        .foregroundStyle(Theme.ink)
        .padding(28)
        .frame(width: 420)
        .background(GlassBackground())
        .lineworkWindow()
        .onAppear { name = storedName }
    }

    private var clamped: Double { min(max(transparency, Glass.range.lowerBound), Glass.range.upperBound) }

    private func save() {
        let normalized = LineworkCopy.normalizedName(name)
        if normalized != storedName { storedName = normalized }
        welcomed = true
    }
}
