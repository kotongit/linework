import LineworkCore
import SwiftUI

/// Freehand's canvas list: the current canvas, the others, and what can be done with them. Everything autosaves.
struct CanvasMenu: View {
    let store: FreehandStore
    @State private var renaming = false
    @State private var newName = ""
    @State private var confirmingDelete = false
    @State private var problem: String?

    var body: some View {
        Menu {
            ForEach(store.names, id: \.self) { name in
                Button {
                    store.open(name)
                } label: {
                    if name == store.currentName { Label(name, systemImage: "checkmark") } else { Text(name) }
                }
            }
            Divider()
            Button("New Canvas") { attempt { try store.newCanvas() } }
            Button("Rename…") {
                newName = store.currentName
                renaming = true
            }
            Button("Duplicate") { attempt { try store.duplicateCurrent() } }
            Button("Delete…", role: .destructive) { confirmingDelete = true }
        } label: {
            Text(store.currentName).lineLimit(1)
        }
        .menuStyle(.button)
        .buttonStyle(CapsuleButtonStyle(height: 30))
        .menuIndicator(.hidden)
        .fixedSize()
        .accessibilityLabel("Canvas, \(store.currentName)")
        .onAppear { store.refreshList() }
        .alert("Rename canvas", isPresented: $renaming) {
            TextField("Name", text: $newName)
            Button("Rename") { attempt { try store.renameCurrent(to: newName) } }
            Button("Cancel", role: .cancel) {}
        }
        .confirmationDialog("Delete “\(store.currentName)”?", isPresented: $confirmingDelete) {
            Button("Delete", role: .destructive) { attempt { try store.deleteCurrent() } }
            Button("Cancel", role: .cancel) {}
        } message: {
            Text("The canvas and its image copies move to the Trash.")
        }
        .alert(problem ?? "", isPresented: Binding(get: { problem != nil }, set: { if !$0 { problem = nil } })) {}
    }

    private func attempt(_ action: () throws -> Void) {
        do {
            try action()
        } catch CanvasNameError.invalid {
            problem = "That name can't be used. Names can't be empty, start with a dot, or contain / or :."
        } catch CanvasNameError.duplicate {
            problem = "You already have a canvas with that name."
        } catch {
            problem = "That didn't work. Check that the library's folder is available."
        }
    }
}
