import LineworkCore
import SwiftData
import SwiftUI

enum SessionSource: Hashable {
    /// Images imported today first, the rest of the session filled from the library, sized to a target length.
    case today
    case all, benchmarks, set(ImageSet)
}

/// A class step with a stable identity so rows can be removed and reordered safely.
struct EditableStep: Identifiable {
    let id = UUID()
    var step: ClassStep
}

enum SetupMode: String, CaseIterable, Identifiable {
    case simple = "Simple", classMode = "Class"
    var id: Self { self }
}

struct SessionSetupView: View {
    @Environment(\.dismiss) private var dismiss
    @Environment(\.openWindow) private var openWindow
    @Environment(\.modelContext) private var context
    @Environment(SessionStore.self) private var store
    @Query(sort: \RefImage.importedAt) private var images: [RefImage]
    @Query(sort: \ImageSet.createdAt) private var sets: [ImageSet]
    @Query(sort: \ClassPreset.name) private var presets: [ClassPreset]
    @Query private var sessions: [SessionRecord]

    @AppStorage("linework.setup.mode") private var mode = SetupMode.simple
    @AppStorage("linework.setup.seconds") private var seconds = 60
    @AppStorage("linework.setup.maxPoses") private var maxPoses = 0
    @AppStorage("linework.setup.shuffle") private var shuffle = true
    @AppStorage("linework.sound") private var soundOn = true
    @AppStorage("linework.setup.grayscale") private var startGrayscale = false
    @AppStorage("linework.setup.today") private var prefersToday = false
    @AppStorage("linework.setup.targetMinutes") private var targetMinutes = SessionLength.defaultTargetSeconds / 60
    @State private var source = SessionSource.all
    @State private var presetID: UUID?
    @State private var steps: [EditableStep] = QueueBuilder.defaultClass.map { EditableStep(step: $0) }
    @State private var savingPreset = false
    @State private var presetName = ""

    static let presetSeconds = [30, 60, 120, 300]
    static let stepDurations = [15, 30, 45, 60, 90, 120, 180, 300, 600, 900, 1200]

    var body: some View {
        // Due benchmarks and the preview queue are derived once per redraw.
        let due = dueBenchmarks
        let todayCount = todayIDs.count
        let preview = previewQueue(due)
        VStack(alignment: .leading, spacing: 0) {
            Text("New session")
                .font(Theme.font(26, .medium))
                .padding(.horizontal, 28)
                .padding(.top, 28)
                .padding(.bottom, 16)
            ScrollView {
                VStack(alignment: .leading, spacing: 18) {
                    Picker("Images", selection: $source) {
                        Text("Today’s Images (\(todayCount))").tag(SessionSource.today)
                        Divider()
                        Text("All Images (\(images.count))").tag(SessionSource.all)
                        Text("Benchmarks (\(images.count(where: \.isBenchmark)))").tag(SessionSource.benchmarks)
                        ForEach(sets) { set in
                            Text("\(set.name) (\(set.images.count))").tag(SessionSource.set(set))
                        }
                    }

                    HStack(spacing: 6) {
                        ForEach(SetupMode.allCases) { choice in
                            Button(choice.rawValue) { mode = choice }
                                .buttonStyle(CapsuleButtonStyle(height: 30, filled: mode == choice))
                                .accessibilityAddTraits(mode == choice ? .isSelected : [])
                        }
                    }

                    rule
                    if source == .today {
                        todaySection(todayCount: todayCount)
                        rule
                    }
                    if mode == .simple { simpleSection } else { classSection }
                    rule

                    HStack(spacing: 6) {
                        Toggle("Shuffle", isOn: $shuffle)
                        Toggle("Sound cues", isOn: $soundOn)
                        Toggle("Start in grayscale", isOn: $startGrayscale)
                    }
                    .toggleStyle(CapsuleToggleStyle())

                    if !due.isEmpty {
                        Text("\(due.count) benchmark\(due.count == 1 ? "" : "s") due — \(due.count == 1 ? "it" : "they")’ll play first.")
                            .font(Theme.font(14, .medium))
                    }
                }
                .frame(maxWidth: .infinity, alignment: .leading)
                .padding(.horizontal, 28)
                .padding(.bottom, 16)
            }

            rule
            HStack(spacing: 8) {
                Text(summary(preview))
                    .font(Theme.font(13))
                    .foregroundStyle(Theme.muted)
                Spacer()
                Button("Cancel", role: .cancel) { dismiss() }
                    .buttonStyle(CapsuleButtonStyle(height: 30))
                    .keyboardShortcut(.cancelAction)
                Button("Start") { start(due) }
                    .buttonStyle(CapsuleButtonStyle(height: 30, filled: true))
                    .keyboardShortcut(.defaultAction)
                    .disabled(preview.poses.isEmpty)
                    .opacity(preview.poses.isEmpty ? 0.4 : 1)
            }
            .padding(.horizontal, 28)
            .padding(.vertical, 16)
        }
        .font(Theme.font(14))
        .foregroundStyle(Theme.ink)
        .frame(width: 520, height: 640)
        .background(Theme.ground)
        .onChange(of: source) { _, new in prefersToday = new == .today }
        .onAppear {
            if prefersToday { source = .today }
            if presetID == nil, let first = presets.first {
                presetID = first.id
                steps = first.steps.map { EditableStep(step: $0) }
            }
        }
        .alert("Save class preset", isPresented: $savingPreset) {
            TextField("Name", text: $presetName)
            Button("Save") {
                let name = presetName.trimmingCharacters(in: .whitespaces)
                guard !name.isEmpty else { return }
                let preset = ClassPreset(name: name, steps: steps.map(\.step))
                context.insert(preset)
                presetID = preset.id
            }
            Button("Cancel", role: .cancel) {}
        }
    }

    private var rule: some View {
        Rectangle().fill(Theme.hairline).frame(height: 1)
    }

    private func sectionLabel(_ text: String) -> some View {
        Text(text).font(Theme.font(12)).foregroundStyle(Theme.muted)
    }

    private var simpleSection: some View {
        VStack(alignment: .leading, spacing: 12) {
            sectionLabel("Time per pose")
            HStack(spacing: 6) {
                ForEach(Self.presetSeconds, id: \.self) { s in
                    Button(DurationFormat.short(s)) { seconds = s }
                        .buttonStyle(CapsuleButtonStyle(height: 30, filled: seconds == s))
                        .accessibilityAddTraits(seconds == s ? .isSelected : [])
                }
            }
            Stepper("Custom: \(DurationFormat.short(seconds))", value: $seconds, in: 5...3600, step: seconds < 60 ? 5 : 15)
            if source != .today {
                Stepper(maxPoses == 0 ? "Poses: all images" : "Poses: \(maxPoses)", value: $maxPoses, in: 0...500)
            }
        }
    }

    private func todaySection(todayCount: Int) -> some View {
        VStack(alignment: .leading, spacing: 8) {
            Stepper("Session length: \(targetMinutes) min", value: $targetMinutes, in: 5...180, step: 5)
            Text(todayCount == 0
                 ? "No images imported today yet — the session uses random images from your library."
                 : "Today’s \(todayCount) \(todayCount == 1 ? "image plays" : "images play") first, then random images from your library fill the rest.")
                .font(Theme.font(13))
                .foregroundStyle(Theme.muted)
                .fixedSize(horizontal: false, vertical: true)
        }
    }

    private var classSection: some View {
        VStack(alignment: .leading, spacing: 12) {
            sectionLabel("Class steps")
            Picker("Preset", selection: $presetID) {
                ForEach(presets) { Text($0.name).tag(Optional($0.id)) }
            }
            .onChange(of: presetID) { _, id in
                if let preset = presets.first(where: { $0.id == id }) { steps = preset.steps.map { EditableStep(step: $0) } }
            }
            List {
                ForEach($steps) { $row in
                    HStack {
                        Stepper("\(row.step.count) ×", value: $row.step.count, in: 1...100)
                            .fixedSize()
                        Picker("", selection: $row.step.seconds) {
                            ForEach(Self.stepDurations, id: \.self) { Text(DurationFormat.short($0)).tag($0) }
                        }
                        .labelsHidden()
                        .frame(width: 100)
                        Spacer()
                        Button {
                            steps.removeAll { $0.id == row.id }
                        } label: { Image(systemName: "minus.circle").foregroundStyle(Theme.ink) }
                            .buttonStyle(.borderless)
                            .disabled(steps.count == 1)
                            .opacity(steps.count == 1 ? 0.4 : 1)
                    }
                    .listRowBackground(Color.clear)
                }
                .onMove { steps.move(fromOffsets: $0, toOffset: $1) }
            }
            .listStyle(.plain)
            .scrollContentBackground(.hidden)
            // Lives inside the sheet's scroll view, so it needs a height of its own.
            .frame(height: CGFloat(min(max(steps.count, 3), 6)) * 34)
            HStack {
                Button("Add step") { steps.append(EditableStep(step: ClassStep(count: 2, seconds: 120))) }
                Spacer()
                Button("Save as preset…") {
                    presetName = ""
                    savingPreset = true
                }
            }
            .buttonStyle(CapsuleButtonStyle(height: 30))
        }
    }

    private var todayIDs: [UUID] {
        images.filter { Calendar.current.isDateInToday($0.importedAt) }.map(\.id)
    }

    private var sourceIDs: [UUID] {
        switch source {
        case .today: todayIDs
        case .all: images.map(\.id)
        case .benchmarks: images.filter(\.isBenchmark).map(\.id)
        case let .set(set): set.images.map(\.id)
        }
    }

    private var dueBenchmarks: [DueBenchmark] {
        let dates = sessions.map(\.startedAt)
        return images
            .filter { $0.isBenchmark && BenchmarkSchedule.isDue(since: $0.benchmarkSince, interval: $0.benchmarkInterval, sessionDates: dates) }
            .map { DueBenchmark(imageID: $0.id, seconds: $0.benchmarkSeconds, number: $0.attempts.count + 1) }
    }

    /// The rest of the library, used to fill a Today session.
    private var fillIDs: [UUID] {
        guard source == .today else { return [] }
        let today = Set(todayIDs)
        return images.map(\.id).filter { !today.contains($0) }
    }

    /// For Today, poses (simple) or step counts (class) are sized so the session, benchmarks included, lasts about the target.
    private func sessionMode(_ due: [DueBenchmark]) -> SessionMode {
        let classSteps = steps.map(\.step)
        guard source == .today else {
            return mode == .simple ? .simple(seconds: seconds, maxPoses: maxPoses == 0 ? nil : maxPoses) : .classSteps(classSteps)
        }
        let target = max(targetMinutes * 60 - due.reduce(0) { $0 + $1.seconds }, 60)
        return mode == .simple
            ? .simple(seconds: seconds, maxPoses: SessionLength.poses(targetSeconds: target, poseSeconds: seconds))
            : .classSteps(SessionLength.scaled(classSteps, toSeconds: target))
    }

    private func buildQueue(_ due: [DueBenchmark], shuffle: Bool) -> SessionQueue {
        var rng = SystemRandomNumberGenerator()
        return QueueBuilder.build(imageIDs: sourceIDs, fillIDs: fillIDs, mode: sessionMode(due), shuffle: shuffle,
                                  benchmarks: due, using: &rng)
    }

    private func previewQueue(_ due: [DueBenchmark]) -> SessionQueue {
        buildQueue(due, shuffle: false)
    }

    private func summary(_ q: SessionQueue) -> String {
        if q.poses.isEmpty { return "No images in this source — drag some photos in first." }
        let total = q.poses.reduce(0) { $0 + $1.seconds }
        var text = "\(q.poses.count) poses · \(DurationFormat.total(total))"
        if q.repeatedImages { text += " · images will repeat" }
        return text
    }

    private func start(_ due: [DueBenchmark]) {
        let queue = buildQueue(due, shuffle: shuffle)
        guard !queue.poses.isEmpty else { return }
        store.queue = queue
        store.planBlock = nil
        store.token = UUID()
        store.soundOn = soundOn
        store.startGrayscale = startGrayscale
        dismiss()
        openWindow(id: "session")
    }
}
