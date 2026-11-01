import LineworkCore
import SwiftUI

@main
struct LineworkApp: App {
    init() {
        Theme.registerFonts()
    }

    @State private var app = {
        #if DEBUG
        if !Set(CommandLine.arguments).isDisjoint(with: ["--snapshots", "--print-counts"]) { return DebugSnapshots.app }
        #endif
        return AppModel()
    }()
    @State private var store = SessionStore()
    @NSApplicationDelegateAdaptor private var appDelegate: AppDelegate

    var body: some Scene {
        WindowGroup {
            AppShell()
                .environment(app)
                .environment(store)
        }
        .windowStyle(.hiddenTitleBar)
        .defaultSize(width: 1280, height: 800)
        .commands {
            CommandGroup(replacing: .newItem) {}
            LibraryCommands(app: app, store: store)
            GoCommands(store: store)
        }

        Window("Session", id: "session") {
            if let library = app.library {
                SessionPlayerView()
                    .environment(library)
                    .environment(store)
            }
        }
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
        if let i = CommandLine.arguments.firstIndex(of: "--perf"), CommandLine.arguments.count > i + 1 {
            Task { @MainActor in await DebugLibraryChecks.performance(source: URL(fileURLWithPath: CommandLine.arguments[i + 1])) }
            return
        }
        if CommandLine.arguments.contains("--library-checks") {
            Task { @MainActor in
                let i = CommandLine.arguments.firstIndex(of: "--library-checks")!
                await DebugLibraryChecks.run(sources: CommandLine.arguments[(i + 1)...].map { URL(fileURLWithPath: $0) })
            }
            return
        }
        #endif
        #if DEBUG
        if CommandLine.arguments.contains("--print-counts") {
            Task { @MainActor in DebugSnapshots.printCounts() }
        } else if let i = CommandLine.arguments.firstIndex(of: "--snapshots"), CommandLine.arguments.count > i + 1 {
            Task { @MainActor in
                await DebugSnapshots.run(outDir: URL(fileURLWithPath: CommandLine.arguments[i + 1]),
                                         sources: CommandLine.arguments[(i + 2)...].map { URL(fileURLWithPath: $0) })
            }
        }
        #endif
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
            Divider()
            Button("Import Photos…") {
                store.route = .section(.library)
                // After the Library appears, if it wasn't showing.
                Task { @MainActor in NotificationCenter.default.post(name: .importPhotosRequested, object: nil) }
            }
            .keyboardShortcut("i", modifiers: .command)
            .disabled(app.library == nil || store.isSessionActive)
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
