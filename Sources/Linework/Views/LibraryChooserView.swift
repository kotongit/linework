import AppKit
import LineworkCore
import SwiftUI

/// The first screen when no library is open: make a new one or open an existing folder.
struct LibraryChooserView: View {
    @Environment(AppModel.self) private var app

    var body: some View {
        @Bindable var app = app
        VStack(alignment: .leading, spacing: 24) {
            Asterisk(size: 40)
            Text("Your library")
                .font(Theme.font(64, .semibold))
                .foregroundStyle(Theme.ink)
            Text("Linework keeps your photos, canvases and progress in a folder you can open any time.")
                .font(Theme.font(20))
                .foregroundStyle(Theme.muted)
                .fixedSize(horizontal: false, vertical: true)
                .frame(maxWidth: 520, alignment: .leading)
            if let text = messageText {
                Text(text)
                    .font(Theme.font(15, .medium))
                    .foregroundStyle(Theme.ink)
                    .fixedSize(horizontal: false, vertical: true)
                    .frame(maxWidth: 520, alignment: .leading)
            }
            HStack(spacing: 12) {
                Button("Create Library") { chooseNew() }
                    .buttonStyle(CapsuleButtonStyle(filled: true))
                Button("Open Library") { chooseExisting() }
                    .buttonStyle(CapsuleButtonStyle())
                if canRetry {
                    Button("Try Again") {
                        app.message = nil
                        app.openRemembered()
                    }
                    .buttonStyle(CapsuleButtonStyle())
                }
            }
            .padding(.top, 8)
        }
        .padding(.horizontal, 96)
        .frame(maxWidth: .infinity, maxHeight: .infinity, alignment: .leading)
        .alert("Use this folder for your library?", isPresented: Binding(get: { app.pendingAdoption != nil }, set: { if !$0 { app.pendingAdoption = nil } })) {
            Button("Use Folder") {
                if let url = app.pendingAdoption { app.create(at: url) }
                app.pendingAdoption = nil
            }
            Button("Cancel", role: .cancel) { app.pendingAdoption = nil }
        } message: {
            Text("It already has other files in it. Linework will add its own folders and won't change what's there.")
        }
    }

    private var canRetry: Bool {
        if case .missing = app.message { return true }
        return LibraryLocation.hasRemembered && app.message != nil
    }

    private var messageText: String? {
        switch app.message {
        case nil: nil
        case let .missing(url): "Linework can't find “\(url.lastPathComponent)”. It may be on a drive that isn't connected."
        case .newerFormat: "That library was made by a newer version of Linework. Update the app to open it."
        case let .failed(text): text
        }
    }

    private func chooseNew() {
        let panel = NSSavePanel()
        panel.title = "Create Library"
        panel.prompt = "Create"
        panel.nameFieldLabel = "Name:"
        panel.nameFieldStringValue = LibraryFolder.suggestedLocation.lastPathComponent
        panel.directoryURL = LibraryFolder.suggestedLocation.deletingLastPathComponent()
        panel.canCreateDirectories = true
        if panel.runModal() == .OK, let url = panel.url { app.open(url, creating: true) }
    }

    private func chooseExisting() {
        let panel = NSOpenPanel()
        panel.title = "Open Library"
        panel.prompt = "Open"
        panel.canChooseFiles = false
        panel.canChooseDirectories = true
        panel.canCreateDirectories = true
        panel.allowsMultipleSelection = false
        if panel.runModal() == .OK, let url = panel.url { app.open(url) }
    }
}

extension AppModel {
    /// A new name from the save panel doesn't exist yet, so it's created rather than reported as missing.
    func open(_ url: URL, creating: Bool) {
        if creating, !FileManager.default.fileExists(atPath: url.path) {
            create(at: url)
        } else {
            open(url)
        }
    }
}
