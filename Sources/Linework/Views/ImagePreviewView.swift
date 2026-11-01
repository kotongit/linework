import SwiftUI

/// Large preview of library images: ← → to step, O to compare with the uncropped original, E to edit the crop.
struct ImagePreviewView: View {
    let images: [RefImage]
    let onSelect: (RefImage.ID) -> Void
    @Environment(\.dismiss) private var dismiss
    @State private var index: Int
    @State private var showOriginal = false
    @State private var editing: RefImage?
    @FocusState private var focused: Bool

    init(images: [RefImage], startIndex: Int, onSelect: @escaping (RefImage.ID) -> Void) {
        self.images = images
        self.onSelect = onSelect
        _index = State(initialValue: min(max(startIndex, 0), max(images.count - 1, 0)))
    }

    private var current: RefImage? { images.indices.contains(index) ? images[index] : nil }

    var body: some View {
        VStack(spacing: 0) {
            ZStack {
                Color.black
                if let current {
                    ThumbnailView(ref: current, maxPixel: 2000, showCrop: !showOriginal)
                        .padding(20)
                        .id(current.id)
                    // Pre-warm the neighbours so stepping feels instant.
                    ForEach([index - 1, index + 1].filter(images.indices.contains), id: \.self) { i in
                        ThumbnailView(ref: images[i], maxPixel: 2000).frame(width: 0, height: 0).hidden()
                    }
                }
                HStack {
                    navButton("chevron.left", disabled: index == 0) { step(-1) }
                    Spacer()
                    navButton("chevron.right", disabled: index >= images.count - 1) { step(1) }
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
            CropEditorView(image: ref)
        }
    }

    private var toolbar: some View {
        HStack(spacing: 12) {
            Text("\(index + 1) of \(images.count)")
                .font(Theme.font(13))
                .monospacedDigit()
                .foregroundStyle(Theme.muted)
            if let current {
                if current.isBenchmark {
                    PillLabel("Benchmark")
                }
                Spacer()
                Toggle("Original", isOn: $showOriginal)
                    .toggleStyle(CapsuleToggleStyle())
                    .help("Show the whole photo (O)")
                Button(current.isBenchmark ? "Unmark benchmark" : "Mark benchmark") {
                    current.toggleBenchmark()
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
        let next = min(max(index + delta, 0), images.count - 1)
        guard next != index else { return }
        index = next
        if let id = current?.id { onSelect(id) }
    }
}
