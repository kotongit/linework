import SwiftData
import SwiftUI
import UniformTypeIdentifiers

/// Shown after a benchmark pose; records the attempt with or without a drawing photo.
struct AttemptSheet: View {
    let image: RefImage
    let number: Int
    let sessionID: UUID
    let onDone: () -> Void

    @Environment(\.modelContext) private var context
    @State private var drawingURL: URL?
    @State private var choosing = false
    @State private var targeted = false
    @State private var error: String?

    var body: some View {
        VStack(alignment: .leading, spacing: 20) {
            Text("Benchmark #\(number) done").font(Theme.font(26, .medium))
            HStack(spacing: 16) {
                ThumbnailView(ref: image)
                    .frame(width: 220, height: 280)
                dropZone
                    .frame(width: 220, height: 280)
            }
            if let error { Text(error).font(Theme.font(14, .medium)) }
            HStack {
                Button("Choose…") { choosing = true }
                Spacer()
                Button("Skip") { onDone() }
                    .help("Don’t count this attempt; the benchmark stays due")
                Button("Later") { record(nil) }
                    .keyboardShortcut(.cancelAction)
                Button("Save") { save() }
                    .buttonStyle(CapsuleButtonStyle(height: 30, filled: true))
                    .keyboardShortcut(.defaultAction)
                    .disabled(drawingURL == nil)
                    .opacity(drawingURL == nil ? 0.4 : 1)
            }
            .buttonStyle(CapsuleButtonStyle(height: 30))
        }
        .foregroundStyle(Theme.ink)
        .padding(28)
        .frame(width: 520)
        .background(Theme.ground)
        .fileImporter(isPresented: $choosing, allowedContentTypes: [.image]) { result in
            if case let .success(url) = result { drawingURL = url }
        }
    }

    private var dropZone: some View {
        ZStack {
            RoundedRectangle(cornerRadius: 8)
                .strokeBorder(targeted ? Theme.ink : Theme.muted, style: StrokeStyle(lineWidth: 1, dash: [4]))
            if let drawingURL {
                DrawingImageView(url: drawingURL).padding(8)
            } else {
                VStack(spacing: 8) {
                    Image(systemName: "plus").font(.system(size: 16, weight: .light))
                    Text("Drop a photo of your drawing").font(Theme.font(14)).multilineTextAlignment(.center)
                }
                .foregroundStyle(Theme.muted)
                .padding()
            }
        }
        .dropDestination(for: URL.self) { urls, _ in
            drawingURL = urls.first
            return drawingURL != nil
        } isTargeted: { targeted = $0 }
    }

    private func save() {
        guard let drawingURL else { return }
        let access = drawingURL.startAccessingSecurityScopedResource()
        defer { if access { drawingURL.stopAccessingSecurityScopedResource() } }
        do {
            record(try Storage.copyDrawing(drawingURL))
        } catch {
            self.error = "That file couldn’t be read as an image."
        }
    }

    private func record(_ fileName: String?) {
        context.insert(BenchmarkAttempt(image: image, sessionID: sessionID, drawingFileName: fileName))
        try? context.save()
        onDone()
    }
}
