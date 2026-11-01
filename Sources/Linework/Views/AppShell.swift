import LineworkCore
import SwiftUI

enum AppSection: String, CaseIterable, Hashable {
    case today, path, calendar, library, progress, freehand

    var title: String {
        switch self {
        case .today: "Today"
        case .path: "Path"
        case .calendar: "Calendar"
        case .library: "Library"
        case .progress: "Progress"
        case .freehand: "Freehand"
        }
    }
}

enum AppRoute: Hashable {
    case menu
    case section(AppSection)
}

/// The main window: welcome on first launch, otherwise the main menu or a section under the top bar.
struct AppShell: View {
    @Environment(AppModel.self) private var app
    @Environment(SessionStore.self) private var store
    @Environment(\.openSettings) private var openSettings
    @AppStorage("linework.name") private var name = ""
    @AppStorage("linework.welcomed") private var welcomed = false
    @State private var showingSetup = false

    var body: some View {
        @Bindable var store = store
        ZStack {
            GlassBackground()
            if let library = app.library {
                if LineworkCopy.shouldShowWelcome(name: name, welcomed: welcomed) {
                    WelcomeView { }
                } else {
                    routed.environment(library).environment(app.freehand!)
                }
            } else {
                LibraryChooserView()
            }
        }
        .frame(minWidth: 1100, minHeight: 700)
        .ignoresSafeArea(.container, edges: .top)
        .lineworkWindow()
        .onReceive(SessionStore.dayChanges) { _ in store.refreshToday() }
        .overlay {
            // Waits for the session window to close, so it's seen rather than played behind it.
            if let celebration = store.pendingCelebration, !store.isSessionActive {
                CelebrationView(kind: celebration, isToday: store.pendingCelebrationIsToday) { store.pendingCelebration = nil }
                    .id(celebration)
            }
        }
        .overlay(alignment: .bottom) {
            if let library = app.library, let text = library.saveFailure ?? library.notice ?? damagedText(library) {
                Text(text)
                    .font(Theme.font(13, .medium))
                    .foregroundStyle(Theme.ink)
                    .padding(.horizontal, 16).padding(.vertical, 8)
                    .background(.ultraThinMaterial, in: Capsule())
                    .overlay(Capsule().strokeBorder(Theme.ink, lineWidth: 1))
                    .padding(16)
            }
        }
        .sheet(isPresented: $showingSetup) {
            if let library = app.library { SessionSetupView().environment(library) }
        }
        .onReceive(NotificationCenter.default.publisher(for: NSApplication.didBecomeActiveNotification)) { _ in
            app.library?.reloadIfChanged()
            app.freehand?.refreshList()
            scan()
        }
        .task(id: app.library?.root) { scan() }
        .onReceive(NotificationCenter.default.publisher(for: .startSessionRequested)) { _ in
            showingSetup = true
        }
    }

    private func damagedText(_ library: LibraryStore) -> String? {
        library.damagedFiles.first.map { "“\($0)” couldn't be read, so it was set aside with “damaged” in its name." }
    }

    private func scan() {
        guard let library = app.library else { return }
        Task { await PhotoScanner.run(on: library) }
    }

    @ViewBuilder
    private var routed: some View {
        @Bindable var store = store
        switch store.route {
        case .menu:
            MainMenuView(onOpen: { store.route = .section($0) }, onOpenSettings: { openSettings() })
        case let .section(section):
            VStack(spacing: 0) {
                TopBar(section: $store.route) { showingSetup = true }
                switch section {
                case .today: TodayView()
                case .path: PathView()
                case .calendar: CalendarHeatmapView()
                case .library: LibraryView()
                case .progress: BenchmarkProgressView()
                case .freehand: FreehandView()
                }
            }
        }
    }
}

extension Notification.Name {
    /// Posted by the Start Session command (⌘R) to open the setup sheet in the main window.
    static let startSessionRequested = Notification.Name("linework.startSessionRequested")
}
