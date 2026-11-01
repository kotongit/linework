import LineworkCore
import SwiftData
import SwiftUI

enum LibraryFilter: Hashable {
    case all, benchmarks, set(ImageSet)
}

struct LibraryView: View {
    @State private var filter: LibraryFilter = .all
    @State private var renaming: ImageSet?
    @State private var newName = ""
    @Environment(\.modelContext) private var context
    @Query(sort: \RefImage.importedAt, order: .reverse) private var allImages: [RefImage]
    @Query(sort: \ImageSet.createdAt) private var sets: [ImageSet]
    @State private var selection: RefImage.ID?
    @State private var previewing: PreviewTarget?
    @State private var editing: RefImage?
    @State private var pendingDelete: RefImage?
    @State private var notice: String?
    @State private var importing = false
    @State private var dropTargeted = false
    @FocusState private var gridFocused: Bool

    private var images: [RefImage] {
        switch filter {
        case .benchmarks: allImages.filter(\.isBenchmark)
        case let .set(set): set.images.sorted { $0.importedAt > $1.importedAt }
        default: allImages
        }
    }

    private var currentSet: ImageSet? {
        if case let .set(set) = filter { set } else { nil }
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
                if !trimmed.isEmpty { renaming?.name = trimmed }
                renaming = nil
            }
            Button("Cancel", role: .cancel) { renaming = nil }
        }
    }

    private var filterRow: some View {
        ScrollView(.horizontal, showsIndicators: false) {
            HStack(spacing: 6) {
                filterPill("All", filter: .all)
                filterPill("Benchmarks", filter: .benchmarks)
                ForEach(sets) { set in
                    filterPill(set.name, filter: .set(set))
                        .contextMenu {
                            Button("Rename…") {
                                newName = set.name
                                renaming = set
                            }
                            Button("Delete Set", role: .destructive) { delete(set) }
                        }
                }
                Button("+") { createSet() }
                    .buttonStyle(CapsuleButtonStyle(height: 30))
                    .help("New set")
                    .accessibilityLabel("New set")
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
        let set = ImageSet(name: "New Set")
        context.insert(set)
        newName = set.name
        renaming = set
        filter = .set(set)
    }

    private func delete(_ set: ImageSet) {
        if filter == .set(set) { filter = .all }
        context.delete(set)
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
            ImagePreviewView(images: target.images, startIndex: target.index) { selection = $0 }
        }
        .sheet(item: $editing) { ref in
            CropEditorView(image: ref)
        }
        .confirmationDialog("Delete this image?", isPresented: Binding(get: { pendingDelete != nil }, set: { if !$0 { pendingDelete = nil } })) {
            Button("Delete", role: .destructive) {
                if let ref = pendingDelete { delete(ref) }
                pendingDelete = nil
            }
        } message: {
            Text("Its crop and any benchmark attempts are removed too.")
        }
    }

    private var emptyTitle: String {
        filter == .benchmarks ? LineworkCopy.emptyBenchmarks : LineworkCopy.emptyLibrary
    }

    private func cell(_ ref: RefImage, in images: [RefImage]) -> some View {
        let selected = selection == ref.id
        return ThumbnailView(ref: ref)
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
                    if ref.isBenchmark { badge("star.fill", help: "Benchmark") }
                }
                .padding(6)
            }
            .contentShape(Rectangle())
            .onTapGesture(count: 2) {
                selection = ref.id
                previewing = PreviewTarget(images: images, index: images.firstIndex(of: ref) ?? 0)
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
    private func menu(_ ref: RefImage, in images: [RefImage]) -> some View {
        Button("Preview") { previewing = PreviewTarget(images: images, index: images.firstIndex(of: ref) ?? 0) }
        Button("Edit Crop…") { editing = ref }
        Menu("Add to Set") {
            ForEach(sets) { set in
                Button(set.name) { if !ref.sets.contains(set) { ref.sets.append(set) } }
                    .disabled(ref.sets.contains(set))
            }
            if sets.isEmpty { Text("No sets yet") }
        }
        if let set = currentSet {
            Button("Remove from “\(set.name)”") { ref.sets.removeAll { $0 == set } }
        }
        Button(ref.isBenchmark ? "Unmark as Benchmark" : "Mark as Benchmark") { ref.toggleBenchmark() }
        Divider()
        Button("Delete…", role: .destructive) { pendingDelete = ref }
    }

    private func openPreview(in images: [RefImage]) -> KeyPress.Result {
        guard let index = images.firstIndex(where: { $0.id == selection }) ?? (images.isEmpty ? nil : 0) else { return .ignored }
        previewing = PreviewTarget(images: images, index: index)
        return .handled
    }

    private func moveSelection(_ step: Int, in images: [RefImage]) -> KeyPress.Result {
        guard !images.isEmpty else { return .ignored }
        let current = images.firstIndex { $0.id == selection } ?? -step
        selection = images[min(max(current + step, 0), images.count - 1)].id
        return .handled
    }

    /// Deletes the rows first; files go only once the save succeeds, so a failed save never leaves rows without files.
    private func delete(_ ref: RefImage) {
        let files = Storage.files(of: ref)
        if selection == ref.id { selection = nil }
        context.delete(ref)
        do {
            try context.save()
            Storage.remove(files)
        } catch {
            context.rollback()
        }
    }

    private func importDropped(_ urls: [URL]) {
        importing = true
        notice = nil
        let set = currentSet
        Task {
            let result = await ImportService.importURLs(urls, into: context, set: set)
            importing = false
            var parts = ["Imported \(result.imported)"]
            if result.skipped > 0 { parts.append("\(result.skipped) skipped (not images)") }
            notice = parts.joined(separator: " · ")
            try? await Task.sleep(for: .seconds(4))
            notice = nil
        }
    }
}

/// The images a preview can step through, frozen when it opens.
struct PreviewTarget: Identifiable {
    let id = UUID()
    let images: [RefImage]
    let index: Int
}
