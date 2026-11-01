import LineworkCore
import SwiftData
import SwiftUI

@main
struct LineworkApp: App {
    let container: ModelContainer = {
        do {
            return try ModelContainer(for: RefImage.self, ImageSet.self, ClassPreset.self, SessionRecord.self, BenchmarkAttempt.self,
                                      PlanCheck.self, PlanRepeatRecord.self,
                                      configurations: ModelConfiguration(url: Storage.root.appending(path: "Library.store")))
        } catch {
            fatalError("Could not open library: \(error)")
        }
    }()

    init() {
        Theme.registerFonts()
        _ = Storage.imagesDir
        DebugSnapshots.container = container
        let context = container.mainContext
        if (try? context.fetchCount(FetchDescriptor<ClassPreset>())) == 0 {
            context.insert(ClassPreset(name: "Default class", steps: QueueBuilder.defaultClass))
            try? context.save()
        }
    }

    @State private var app = AppModel()
    @State private var store = SessionStore()
    /// Here so there's only ever one over the folder: it deletes copies no item uses, and the canvas outlives windows and sections.
    @State private var freehand = FreehandStore()
    @NSApplicationDelegateAdaptor private var appDelegate: AppDelegate

    var body: some Scene {
        WindowGroup {
            AppShell()
                .environment(app)
                .environment(store)
                .environment(freehand)
        }
        .modelContainer(container)
        .windowStyle(.hiddenTitleBar)
        .defaultSize(width: 1280, height: 800)
        .commands {
            CommandGroup(replacing: .newItem) {}
            LibraryCommands(app: app, store: store)
            GoCommands(store: store)
        }

        Window("Session", id: "session") {
            SessionPlayerView()
                .environment(store)
        }
        .modelContainer(container)
        .windowStyle(.hiddenTitleBar)
        .restorationBehavior(.disabled)

        Settings { LineworkSettingsView() }

        Window("About Linework", id: "about") {
            AboutView()
        }
        .windowStyle(.hiddenTitleBar)
        .windowResizability(.contentSize)
        .restorationBehavior(.disabled)
        .commands {
            CommandGroup(replacing: .appInfo) {
                AboutMenuButton()
            }
        }
    }
}

private struct AboutMenuButton: View {
    @Environment(\.openWindow) private var openWindow
    var body: some View {
        Button("About Linework") { openWindow(id: "about") }
    }
}

final class AppDelegate: NSObject, NSApplicationDelegate {
    func applicationDidFinishLaunching(_ notification: Notification) {
        #if DEBUG
        if CommandLine.arguments.contains("--library-checks") {
            Task { @MainActor in DebugLibraryChecks.run() }
            return
        }
        #endif
        if CommandLine.arguments.contains("--print-counts") {
            Task { @MainActor in DebugSnapshots.printCounts() }
        } else if let i = CommandLine.arguments.firstIndex(of: "--snapshots"), CommandLine.arguments.count > i + 1 {
            Task { @MainActor in
                await DebugSnapshots.run(outDir: URL(fileURLWithPath: CommandLine.arguments[i + 1]),
                                         sources: CommandLine.arguments[(i + 2)...].map { URL(fileURLWithPath: $0) })
            }
        }
    }
}

private struct LibraryCommands: Commands {
    let app: AppModel
    let store: SessionStore

    var body: some Commands {
        CommandGroup(after: .newItem) {
            Button("Open Library…") { app.closeForSwitch() }
                .disabled(store.isSessionActive)
            Button("Reveal Library in Finder") {
                if let root = app.library?.root { NSWorkspace.shared.activateFileViewerSelecting([root]) }
            }
            .disabled(app.library == nil)
        }
    }
}

private struct GoCommands: Commands {
    let store: SessionStore
    @AppStorage("linework.name") private var name = ""
    @AppStorage("linework.welcomed") private var welcomed = false

    private var welcoming: Bool { LineworkCopy.shouldShowWelcome(name: name, welcomed: welcomed) }

    var body: some Commands {
        CommandMenu("Go") {
            Button("Main Menu") { store.route = .menu }
                .keyboardShortcut("0", modifiers: .command)
                .disabled(welcoming)
            ForEach(Array(AppSection.allCases.enumerated()), id: \.element) { index, section in
                Button(section.title) { store.route = .section(section) }
                    .keyboardShortcut(KeyEquivalent(Character("\(index + 1)")), modifiers: .command)
                    .disabled(welcoming)
            }
            Divider()
            Button("Start Session") {
                NotificationCenter.default.post(name: .startSessionRequested, object: nil)
            }
            .keyboardShortcut("r", modifiers: .command)
            .disabled(welcoming || store.isSessionActive)
        }
    }
}
