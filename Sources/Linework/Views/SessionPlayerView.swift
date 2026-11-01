import LineworkCore
import SwiftUI

struct SessionPlayerView: View {
    @Environment(SessionStore.self) private var store
    @Environment(LibraryStore.self) private var library
    @Environment(\.dismissWindow) private var dismissWindow
    @State private var runner: SessionRunner?
    @State private var sessionID = UUID()
    @State private var startedAt = Date()
    @State private var rendered: [Int: CGImage] = [:]
    @State private var grayscale = false
    @State private var showTimer = true
    @State private var showHints = true
    @State private var recorded = false
    @State private var saved = false
    @State private var attemptFor: PhotoRecord?
    @State private var window: NSWindow?
    /// The session's images, taken once at start, so a change in the library can't disturb a running session.
    @State private var imagesByID: [UUID: PhotoRecord] = [:]
    /// Captured at start, so a later Start can't change which block this session ticks.
    @State private var activeBlock: PlanBlockRef?
    @State private var tickedKind: PlanBlockKind?
    /// Keeps the screen on: while drawing on another device, nothing here counts as input.
    @State private var keepAwake: (any NSObjectProtocol)?
    @FocusState private var focused: Bool

    var body: some View {
        ZStack {
            Color.black.ignoresSafeArea()
            if let runner {
                content(runner)
            }
        }
        .background(WindowAccessor { w in
            window = w
            #if DEBUG
            if DebugSnapshots.active { return }
            #endif
            if !w.styleMask.contains(.fullScreen) { w.toggleFullScreen(nil) }
        })
        .focusable()
        .focusEffectDisabled()
        .focused($focused)
        .onKeyPress(action: handleKey)
        .task(id: store.token) { await run() }
        .onDisappear {
            finish()
            store.isSessionActive = false
        }
        .onReceive(NotificationCenter.default.publisher(for: NSApplication.willTerminateNotification)) { _ in finish() }
        .sheet(item: $attemptFor) { ref in
            AttemptSheet(photo: ref, number: runner?.current?.benchmarkNumber ?? 1, sessionID: sessionID) {
                attemptFor = nil
                handle(runner?.attemptHandled() ?? [])
                focused = true
            }
        }
        .preferredColorScheme(.dark)
    }

    @ViewBuilder
    private func content(_ runner: SessionRunner) -> some View {
        if runner.phase == .finished {
            summaryCard(runner)
        } else {
            if let image = rendered[runner.index] {
                Image(decorative: image, scale: 1)
                    .resizable()
                    .scaledToFit()
                    .grayscale(grayscale ? 1 : 0)
                    .padding(24)
                    .opacity(isGetReady(runner) ? 0.25 : 1)
            }
            VStack {
                HStack(alignment: .top) {
                    if let n = runner.current?.benchmarkNumber {
                        Text("Benchmark #\(n)")
                            .font(Theme.font(12, .medium))
                            .textCase(.uppercase)
                            .tracking(0.7)
                            .padding(.horizontal, 14).padding(.vertical, 8)
                            .background(.ultraThinMaterial, in: Capsule())
                            .overlay(Capsule().strokeBorder(.white, lineWidth: 1))
                    }
                    Spacer()
                    if showTimer {
                        Text(DurationFormat.clock(runner.remaining))
                            .font(Theme.font(28, .medium))
                            .monospacedDigit()
                            .padding(.horizontal, 16).padding(.vertical, 4)
                            .background(.ultraThinMaterial, in: Capsule())
                    }
                }
                Spacer()
                if runner.isPaused {
                    Text("Paused — press Space")
                        .font(Theme.font(15, .medium))
                        .padding(.horizontal, 16).padding(.vertical, 8)
                        .background(.ultraThinMaterial, in: Capsule())
                }
                if showHints {
                    Text("Space pause · ← → move · G grayscale · T timer · Esc end")
                        .font(Theme.font(13))
                        .padding(.horizontal, 14).padding(.vertical, 6)
                        .background(.ultraThinMaterial, in: Capsule())
                        .padding(.bottom, 8)
                }
                Text("\(runner.index + 1) / \(runner.poses.count)")
                    .font(Theme.font(12, .medium))
                    .monospacedDigit()
                    .padding(.horizontal, 10).padding(.vertical, 4)
                    .background(.ultraThinMaterial, in: Capsule())
            }
            .padding(20)
            .foregroundStyle(.white)
            .environment(\.colorScheme, .dark)

            VStack {
                PracticeNoteView(poseIndex: runner.index, poseCount: runner.poses.count,
                                 isDrawing: runner.phase == .pose && !runner.isPaused && attemptFor == nil)
                    .id(sessionID)
                Spacer()
            }
            .padding(.top, 24)
            .environment(\.colorScheme, .dark)

            if case let .getReady(left) = runner.phase {
                VStack(spacing: 12) {
                    Text("\(Int(left.rounded(.up)))")
                        .font(Theme.font(160, .medium))
                        .monospacedDigit()
                    if let pose = runner.current {
                        Text(pose.benchmarkNumber != nil ? "Benchmark: \(DurationFormat.short(pose.seconds))"
                             : "Next: \(DurationFormat.short(pose.seconds)) poses")
                            .font(Theme.font(20, .medium))
                    }
                }
                .foregroundStyle(.white)
            }

            VStack {
                Spacer()
                GeometryReader { geo in
                    let total = Double(runner.current?.seconds ?? 1)
                    let fraction = runner.phase == .pose ? max(0, min(1, 1 - runner.remaining / total)) : 0
                    Rectangle().fill(.white).frame(width: geo.size.width * fraction)
                }
                .frame(height: 3)
            }
            .ignoresSafeArea()
        }
    }

    private func summaryCard(_ runner: SessionRunner) -> some View {
        let poses = runner.poseElapsed.count { $0 >= 5 }
        return VStack(alignment: .leading, spacing: 10) {
            Text("That's your session.")
                .font(Theme.font(52, .medium))
                .tracking(-1)
            Text("\(poses) poses · \(DurationFormat.total(Int(runner.drawingSeconds)))")
                .font(Theme.font(20))
                .foregroundStyle(.white.opacity(0.65))
            if let tickedKind {
                HStack(spacing: 8) {
                    Image(systemName: "checkmark.circle.fill").foregroundStyle(.teal)
                    Text("\(tickedKind.label) ticked")
                }
                .font(Theme.font(15, .medium))
                .padding(.top, 8)
            } else if !recorded {
                Text(LineworkCopy.sessionTooShort)
                    .font(Theme.font(15))
                    .foregroundStyle(.white.opacity(0.65))
                    .padding(.top, 8)
            }
            Button("Close") { close() }
                .buttonStyle(CapsuleButtonStyle())
                .keyboardShortcut(.defaultAction)
                .padding(.top, 20)
        }
        .foregroundStyle(.white)
        .frame(maxWidth: .infinity, alignment: .leading)
        .padding(.horizontal, 72)
        .environment(\.colorScheme, .dark)
    }

    private func isGetReady(_ runner: SessionRunner) -> Bool {
        if case .getReady = runner.phase { true } else { false }
    }

    // MARK: - Driving the runner

    private func run() async {
        guard let queue = store.queue else { return }
        finish()  // a new Start while a session is open records the old one first
        activeBlock = store.planBlock
        tickedKind = nil
        store.isSessionActive = true
        keepAwake = ProcessInfo.processInfo.beginActivity(options: [.idleDisplaySleepDisabled, .userInitiated],
                                                          reason: "Timed drawing session")
        let ids = Set(queue.poses.map(\.imageID))
        imagesByID = Dictionary(library.photos.filter { ids.contains($0.id) }.map { ($0.id, $0) }, uniquingKeysWith: { a, _ in a })
        runner = SessionRunner(poses: queue.poses)
        sessionID = UUID()
        startedAt = .now
        rendered = [:]
        recorded = false
        saved = false
        grayscale = store.startGrayscale
        showHints = true
        focused = true
        prefetch(around: 0)

        Task {
            try? await Task.sleep(for: .seconds(5))
            withAnimation { showHints = false }
        }

        // Monotonic, so system clock changes can't make time run backwards. Suspending, so a closed lid doesn't use up the pose.
        let clock = SuspendingClock()
        var last = clock.now
        while !Task.isCancelled, runner?.phase != .finished {
            try? await Task.sleep(for: .milliseconds(100))
            let now = clock.now
            let elapsed = (now - last).components
            let dt = Double(elapsed.seconds) + Double(elapsed.attoseconds) / 1e18
            last = now
            guard attemptFor == nil else { continue }
            handle(runner?.tick(dt) ?? [])
        }
    }

    private func handle(_ events: RunnerEvent) {
        guard let runner else { return }
        if events.contains(.tick), store.soundOn { SoundCue.tick() }
        if events.contains(.drawingStarted), store.soundOn { SoundCue.chime() }
        if events.contains(.benchmarkEnded) {
            if store.soundOn { SoundCue.chime() }
            attemptFor = runner.current.flatMap { imagesByID[$0.imageID] }
            if attemptFor == nil { handle(self.runner?.attemptHandled() ?? []) }
            return
        }
        if events.contains(.poseChanged) {
            if store.soundOn { SoundCue.chime() }
            prefetch(around: runner.index)
        }
        if events.contains(.finished) { finish() }
    }

    private func handleKey(_ press: KeyPress) -> KeyPress.Result {
        if runner == nil {
            if press.key == .escape { close(); return .handled }
            return .ignored
        }
        guard attemptFor == nil else { return .ignored }
        switch press.key {
        case .space:
            runner?.togglePause()
        case .rightArrow:
            handle(runner?.next() ?? [])
        case .leftArrow:
            handle(runner?.previous() ?? [])
            if let i = runner?.index { prefetch(around: i) }
        case .escape:
            if runner?.phase == .finished { close() } else { runner?.end(); finish() }
        default:
            switch press.characters.lowercased() {
            case "g": grayscale.toggle()
            case "t": showTimer.toggle()
            default: return .ignored
            }
        }
        return .handled
    }

    /// Long edge, in pixels, that fills the screen; full-size originals would waste memory.
    private var screenPixels: Int {
        guard let screen = window?.screen ?? NSScreen.main else { return 3000 }
        return Int(max(screen.frame.width, screen.frame.height) * screen.backingScaleFactor)
    }

    /// Renders the current and next pose off the main thread and drops images outside the previous…next window.
    private func prefetch(around index: Int) {
        guard let runner else { return }
        let keep = RenderWindow.indices(around: index, count: runner.poses.count)
        rendered = rendered.filter { keep.contains($0.key) }
        let token = store.token
        let maxPixel = screenPixels
        for i in [index, index + 1] where runner.poses.indices.contains(i) && rendered[i] == nil {
            guard let photo = imagesByID[runner.poses[i].imageID] else { continue }
            let request = library.renderRequest(photo, maxPixel: maxPixel)
            Task {
                let image = await RenderCache.shared.image(for: request)
                // Ignore late results from a previous session or a pose we've moved away from.
                if let image, token == store.token, let current = self.runner?.index,
                   RenderWindow.indices(around: current, count: self.runner?.poses.count ?? 0).contains(i) {
                    rendered[i] = image
                }
            }
        }
    }

    /// A plan session that ran to its end ticks its block. Ending with Esc doesn't.
    private func tickPlanBlock(_ runner: SessionRunner) {
        guard runner.completedNaturally, let activeBlock else { return }
        tickedKind = activeBlock.kind
        if library.ensureTick(activeBlock.ref, activeBlock.kind),
           let celebration = PlanProgress.celebration(for: activeBlock.ref, ticks: library.tickKeys, schedule: library.schedule) {
            store.celebrate(celebration, isToday: activeBlock.ref == library.schedule.todayRef(store.today))
        }
    }

    /// Records the session once, if any pose ran long enough.
    private func finish() {
        if let keepAwake {
            ProcessInfo.processInfo.endActivity(keepAwake)
            self.keepAwake = nil
        }
        guard let runner, !saved else { return }
        saved = true
        tickPlanBlock(runner)
        guard runner.shouldRecord else { return }
        let shown = zip(runner.poses, runner.poseElapsed).filter { $0.1 > 0 }.map(\.0.imageID)
        library.recordSession(SessionRecord(id: sessionID, startedAt: startedAt, drawingSeconds: Int(runner.drawingSeconds),
                                            poseCount: runner.poseElapsed.count { $0 >= 5 }, photoIDs: shown))
        recorded = true
    }

    private func close() {
        finish()
        store.queue = nil
        if let window, window.styleMask.contains(.fullScreen) { window.toggleFullScreen(nil) }
        dismissWindow(id: "session")
    }
}
