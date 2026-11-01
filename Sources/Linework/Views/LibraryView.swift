import LineworkCore
import SwiftUI

enum LibraryFilter: Hashable {
    case all, benchmarks, set(UUID)
}

struct LibraryView: View {
    @State private var filter: LibraryFilter = .all
    @State private var renaming: SetRecord?
    @State private var newName = ""
    @Environment(LibraryStore.self) private var library
    @State private var selection: UUID?
    @State private var previewing: PreviewTarget?
    @State private var editing: PhotoRecord?
    @State private var pendingDelete: PhotoRecord?
    @State private var choosingFiles = false
    @State private var notice: String?
    @State private var importing = false
    @State private var dropTargeted = false
    @FocusState private var gridFocused: Bool

    private var images: [PhotoRecord] {
        let all = library.visiblePhotos.sorted { $0.importedAt > $1.importedAt }
        switch filter {
        case .benchmarks: return all.filter { library.isBenchmark($0.id) }
        case .set:
            let members = Set(currentSet?.photoIDs ?? [])
            return all.filter { members.contains($0.id) }
        case .all: return all
        }
    }

    private var currentSet: SetRecord? {
        if case let .set(id) = filter { library.sets.first { $0.id == id } } else { nil }
    }

    var body: some View {
        VStack(spacing: 0) {
            filterRow
            grid
        }
        .alert("Set name", isPresented: Binding(get: { renaming != nil }, set: { if !$0 { renaming = nil } })) {
            TextField("Name", text: $newName)
            Button("Save") {
                let trimmed = newName.trimmingCharacters(in: .whitespaces)
                if !trimmed.isEmpty, let id = renaming?.id { library.renameSet(id, to: trimmed) }
                renaming = nil
            }
            Button("Cancel", role: .cancel) { renaming = nil }
        }
        .fileImporter(isPresented: $choosingFiles, allowedContentTypes: [.image], allowsMultipleSelection: true) { result in
            if case let .success(urls) = result { importDropped(urls) }
        }
        .onReceive(NotificationCenter.default.publisher(for: .importPhotosRequested)) { _ in choosingFiles = true }
    }

    private var filterRow: some View {
        ScrollView(.horizontal, showsIndicators: false) {
            HStack(spacing: 6) {
                filterPill("All", filter: .all)
                filterPill("Benchmarks", filter: .benchmarks)
                ForEach(library.sets) { set in
                    filterPill(set.name, filter: .set(set.id))
                        .contextMenu {
                            Button("Rename…") {
                                newName = set.name
                                renaming = set
                            }
                            Button("Delete Set", role: .destructive) { delete(set.id) }
                        }
                }
                Button("+") { createSet() }
                    .buttonStyle(CapsuleButtonStyle(height: 30))
                    .help("New set")
                    .accessibilityLabel("New set")
                Button("Import") { choosingFiles = true }
                    .buttonStyle(CapsuleButtonStyle(height: 30))
                    .help("Add photos to your library")
            }
            .padding(.horizontal, 72)
            .padding(.top, 24)
            .padding(.bottom, 16)
        }
    }

    private func filterPill(_ title: String, filter target: LibraryFilter) -> some View {
        Button(title) { filter = target }
            .buttonStyle(CapsuleButtonStyle(height: 30, filled: filter == target))
            .accessibilityAddTraits(filter == target ? .isSelected : [])
    }

    private func createSet() {
        let set = library.createSet(name: "New Set")
        newName = set.name
        renaming = set
        filter = .set(set.id)
    }

    private func delete(_ id: UUID) {
        if filter == .set(id) { filter = .all }
        library.deleteSet(id)
    }

    private var grid: some View {
        let images = images
        return ScrollView {
            if images.isEmpty {
                VStack(alignment: .leading, spacing: 8) {
                    Text(emptyTitle)
                        .font(Theme.font(26, .medium))
                        .foregroundStyle(Theme.ink)
                    if let currentSet {
                        Text("They'll go into “\(currentSet.name)”.")
                            .font(Theme.font(15))
                            .foregroundStyle(Theme.muted)
                    }
                }
                .frame(maxWidth: .infinity, alignment: .leading)
                .padding(.horizontal, 72)
                .padding(.top, 96)
            } else {
                LazyVGrid(columns: [GridItem(.adaptive(minimum: 160), spacing: 16)], spacing: 16) {
                    ForEach(images) { ref in
                        cell(ref, in: images)
                    }
                }
                .padding(.horizontal, 72)
                .padding(.top, 8)
                .padding(.bottom, 40)
            }
        }
        .focusable()
        .focusEffectDisabled()
        .focused($gridFocused)
        .onKeyPress(.space) { openPreview(in: images) }
        .onKeyPress(.return) { openPreview(in: images) }
        .onKeyPress(.leftArrow) { moveSelection(-1, in: images) }
        .onKeyPress(.rightArrow) { moveSelection(1, in: images) }
        .overlay {
            if dropTargeted {
                RoundedRectangle(cornerRadius: 16).strokeBorder(Theme.ink, lineWidth: 1).padding(16)
            }
        }
        .overlay(alignment: .bottom) {
            if importing || notice != nil {
                HStack(spacing: 8) {
                    if importing { ProgressView().controlSize(.small) }
                    Text(importing ? "Importing…" : notice ?? "")
                }
                .font(Theme.font(13, .medium))
                .foregroundStyle(Theme.ink)
                .padding(.horizontal, 16).padding(.vertical, 8)
                .background(.ultraThinMaterial, in: Capsule())
                .overlay(Capsule().strokeBorder(Theme.ink, lineWidth: 1))
                .padding(16)
            }
        }
        .dropDestination(for: URL.self) { urls, _ in
            importDropped(urls)
            return true
        } isTargeted: { dropTargeted = $0 }
        .sheet(item: $previewing) { target in
            ImagePreviewView(ids: target.ids, startIndex: target.index) { selection = $0 }
        }
        .sheet(item: $editing) { photo in
            CropEditorView(photo: photo, store: library)
        }
        .confirmationDialog("Delete this image?", isPresented: Binding(get: { pendingDelete != nil }, set: { if !$0 { pendingDelete = nil } })) {
            Button("Delete", role: .destructive) {
                if let photo = pendingDelete { delete(photo) }
                pendingDelete = nil
            }
        } message: {
            Text("It moves to the Trash. Its crop and any benchmark attempts are removed too.")
        }
    }

    private var emptyTitle: String {
        filter == .benchmarks ? LineworkCopy.emptyBenchmarks : LineworkCopy.emptyLibrary
    }

    private func cell(_ ref: PhotoRecord, in images: [PhotoRecord]) -> some View {
        let selected = selection == ref.id
        return ThumbnailView(photo: ref, store: library)
            .frame(height: 180)
            .frame(maxWidth: .infinity)
            .clipShape(RoundedRectangle(cornerRadius: 8))
            .overlay {
                RoundedRectangle(cornerRadius: 8).strokeBorder(Theme.hairline, lineWidth: 1)
            }
            .overlay {
                // Sits just outside the tile, so it reads on any photo.
                if selected {
                    RoundedRectangle(cornerRadius: 11).strokeBorder(Theme.ink, lineWidth: 1.5).padding(-3)
                }
            }
            .overlay(alignment: .topTrailing) {
                HStack(spacing: 4) {
                    if ref.noFigureFound { badge("questionmark", help: "No figure found — crop by hand") }
                    if library.isBenchmark(ref.id) { badge("star.fill", help: "Benchmark") }
                }
                .padding(6)
            }
            .contentShape(Rectangle())
            .onTapGesture(count: 2) {
                selection = ref.id
                previewing = PreviewTarget(ids: images.map(\.id), index: images.firstIndex(of: ref) ?? 0)
            }
            .onTapGesture {
                selection = ref.id
                gridFocused = true
            }
            .contextMenu { menu(ref, in: images) }
            .help("Double-click or press Space to preview")
    }

    private func badge(_ symbol: String, help: String) -> some View {
        Image(systemName: symbol)
            .font(.system(size: 10, weight: .semibold))
            .foregroundStyle(Theme.ink)
            .frame(width: 22, height: 22)
            .background(.ultraThinMaterial, in: Circle())
            .overlay(Circle().strokeBorder(Theme.hairline, lineWidth: 1))
            .help(help)
    }

    @ViewBuilder
    private func menu(_ ref: PhotoRecord, in images: [PhotoRecord]) -> some View {
        Button("Preview") { previewing = PreviewTarget(ids: images.map(\.id), index: images.firstIndex(of: ref) ?? 0) }
        Button("Edit Crop…") { editing = ref }
        Menu("Add to Set") {
            ForEach(library.sets) { set in
                Button(set.name) { library.setPhoto(ref.id, inSet: set.id, true) }
                    .disabled(set.photoIDs.contains(ref.id))
            }
            if library.sets.isEmpty { Text("No sets yet") }
        }
        if let set = currentSet {
            Button("Remove from “\(set.name)”") { library.setPhoto(ref.id, inSet: set.id, false) }
        }
        Button(library.isBenchmark(ref.id) ? "Unmark as Benchmark" : "Mark as Benchmark") { library.toggleBenchmark(ref.id) }
        Divider()
        Button("Delete…", role: .destructive) { pendingDelete = ref }
    }

    private func openPreview(in images: [PhotoRecord]) -> KeyPress.Result {
        guard let index = images.firstIndex(where: { $0.id == selection }) ?? (images.isEmpty ? nil : 0) else { return .ignored }
        previewing = PreviewTarget(ids: images.map(\.id), index: index)
        return .handled
    }

    private func moveSelection(_ step: Int, in images: [PhotoRecord]) -> KeyPress.Result {
        guard !images.isEmpty else { return .ignored }
        let current = images.firstIndex { $0.id == selection } ?? -step
        selection = images[min(max(current + step, 0), images.count - 1)].id
        return .handled
    }

    private func delete(_ photo: PhotoRecord) {
        if selection == photo.id { selection = nil }
        library.deletePhoto(photo.id)
    }

    private func importDropped(_ urls: [URL]) {
        importing = true
        notice = nil
        let setID = currentSet?.id
        Task {
            let result = await ImportService.importURLs(urls, into: library, setID: setID)
            importing = false
            var parts = ["Imported \(result.imported)"]
            if result.skipped > 0 { parts.append("\(result.skipped) skipped (not images)") }
            notice = parts.joined(separator: " · ")
            try? await Task.sleep(for: .seconds(4))
            notice = nil
        }
    }
}

extension Notification.Name {
    /// Posted by File > Import Photos to open the Library's file picker.
    static let importPhotosRequested = Notification.Name("linework.importPhotosRequested")
}

/// The images a preview can step through, frozen when it opens.
struct PreviewTarget: Identifiable {
    let id = UUID()
    let ids: [UUID]
    let index: Int
}
