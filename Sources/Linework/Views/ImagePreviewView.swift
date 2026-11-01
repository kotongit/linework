import LineworkCore
import SwiftUI

/// Large preview of library images: ← → to step, O to compare with the uncropped original, E to edit the crop.
struct ImagePreviewView: View {
    let ids: [UUID]
    let onSelect: (UUID) -> Void
    @Environment(LibraryStore.self) private var store
    @Environment(\.dismiss) private var dismiss
    @State private var index: Int
    @State private var showOriginal = false
    @State private var editing: PhotoRecord?
    @FocusState private var focused: Bool

    init(ids: [UUID], startIndex: Int, onSelect: @escaping (UUID) -> Void) {
        self.ids = ids
        self.onSelect = onSelect
        _index = State(initialValue: min(max(startIndex, 0), max(ids.count - 1, 0)))
    }

    private var current: PhotoRecord? { ids.indices.contains(index) ? store.photo(ids[index]) : nil }

    var body: some View {
        VStack(spacing: 0) {
            ZStack {
                Color.black
                if let current {
                    ThumbnailView(photo: current, store: store, maxPixel: 2000, showCrop: !showOriginal)
                        .padding(20)
                        .id(current.id)
                    // Pre-warm the neighbours so stepping feels instant.
                    ForEach([index - 1, index + 1].filter(ids.indices.contains), id: \.self) { i in
                        if let neighbour = store.photo(ids[i]) {
                            ThumbnailView(photo: neighbour, store: store, maxPixel: 2000).frame(width: 0, height: 0).hidden()
                        }
                    }
                }
                HStack {
                    navButton("chevron.left", disabled: index == 0) { step(-1) }
                    Spacer()
                    navButton("chevron.right", disabled: index >= ids.count - 1) { step(1) }
                }
                .padding(.horizontal, 12)
            }
            toolbar
                .padding(.horizontal, 16)
                .padding(.vertical, 12)
                .background(Theme.ground)
        }
        .frame(minWidth: 760, idealWidth: 1000, minHeight: 600, idealHeight: 800)
        .focusable()
        .focusEffectDisabled()
        .focused($focused)
        .onAppear { focused = true }
        .onKeyPress(.leftArrow) { step(-1); return .handled }
        .onKeyPress(.rightArrow) { step(1); return .handled }
        .onKeyPress(.space) { dismiss(); return .handled }
        .onKeyPress(characters: .letters) { press in
            switch press.characters.lowercased() {
            case "o": showOriginal.toggle()
            case "e": editing = current
            default: return .ignored
            }
            return .handled
        }
        .sheet(item: $editing, onDismiss: { focused = true }) { ref in
            CropEditorView(photo: ref, store: store)
        }
    }

    private var toolbar: some View {
        HStack(spacing: 12) {
            Text("\(index + 1) of \(ids.count)")
                .font(Theme.font(13))
                .monospacedDigit()
                .foregroundStyle(Theme.muted)
            if let current {
                if store.isBenchmark(current.id) {
                    PillLabel("Benchmark")
                }
                Spacer()
                Toggle("Original", isOn: $showOriginal)
                    .toggleStyle(CapsuleToggleStyle())
                    .help("Show the whole photo (O)")
                Button(store.isBenchmark(current.id) ? "Unmark benchmark" : "Mark benchmark") {
                    store.toggleBenchmark(current.id)
                }
                Button("Edit crop…") {
                    editing = current
                }
                .help("Edit the crop (E)")
            } else {
                Spacer()
            }
            Button("Done") { dismiss() }
                .buttonStyle(CapsuleButtonStyle(height: 30, filled: true))
                .keyboardShortcut(.defaultAction)
        }
        .buttonStyle(CapsuleButtonStyle(height: 30))
    }

    private func navButton(_ symbol: String, disabled: Bool, action: @escaping () -> Void) -> some View {
        Button(action: action) {
            Image(systemName: symbol)
                .font(.system(size: 16, weight: .light))
                .foregroundStyle(.white)
                .frame(width: 44, height: 44)
                .background(.ultraThinMaterial, in: Circle())
                .overlay(Circle().strokeBorder(.white, lineWidth: 1))
                .environment(\.colorScheme, .dark)
        }
        .buttonStyle(.plain)
        .opacity(disabled ? 0 : 1)
        .disabled(disabled)
    }

    private func step(_ delta: Int) {
        let next = min(max(index + delta, 0), ids.count - 1)
        guard next != index else { return }
        index = next
        if let id = current?.id { onSelect(id) }
    }
}
