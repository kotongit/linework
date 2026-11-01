import LineworkCore
import SwiftUI
import UniformTypeIdentifiers

struct BenchmarkProgressView: View {
    @Environment(LibraryStore.self) private var library
    @State private var comparing: AttemptRecord?

    private var benchmarks: [PhotoRecord] {
        library.visiblePhotos.filter { library.isBenchmark($0.id) }.sorted { $0.importedAt < $1.importedAt }
    }

    var body: some View {
        ScrollView {
            if benchmarks.isEmpty {
                Text(LineworkCopy.emptyBenchmarks)
                    .font(Theme.font(26, .medium))
                    .foregroundStyle(Theme.ink)
                    .frame(maxWidth: .infinity, alignment: .leading)
                    .padding(.horizontal, 72)
                    .padding(.top, 96)
            } else {
                LazyVStack(alignment: .leading, spacing: 44) {
                    ForEach(benchmarks) { ref in
                        BenchmarkRow(ref: ref, sessionDates: library.sessions.map(\.startedAt)) { comparing = $0 }
                    }
                }
                .padding(.horizontal, 72)
                .padding(.vertical, 40)
            }
        }
        .sheet(item: $comparing) { attempt in
            ComparisonSheet(attempt: attempt)
        }
    }
}

private struct BenchmarkRow: View {
    let ref: PhotoRecord
    let sessionDates: [Date]
    let onCompare: (AttemptRecord) -> Void
    @Environment(LibraryStore.self) private var library
    @State private var attaching: AttemptRecord?
    @State private var attachFailed = false

    private var record: BenchmarkRecord { library.benchmark(ref.id) ?? BenchmarkRecord(photoID: ref.id, seconds: 120, interval: 10, markedAt: .now) }
    private var attempts: [AttemptRecord] { library.attempts(of: ref.id) }

    private var dueText: String {
        let left = max(record.interval, 1) - BenchmarkSchedule.sessionsSince(library.benchmarkSince(record), sessionDates: sessionDates)
        return left <= 0 ? "Due next session" : "Next in \(left) session\(left == 1 ? "" : "s")"
    }

    var body: some View {
        VStack(alignment: .leading, spacing: 16) {
            HStack(spacing: 24) {
                Text(dueText).font(Theme.font(20, .medium))
                Spacer(minLength: 0)
                Stepper("Duration \(DurationFormat.short(record.seconds))",
                        value: Binding(get: { record.seconds }, set: { library.updateBenchmark(ref.id, seconds: $0) }), in: 15...1800, step: 15)
                    .fixedSize()
                Stepper("Every \(record.interval) sessions",
                        value: Binding(get: { record.interval }, set: { library.updateBenchmark(ref.id, interval: $0) }), in: 1...100)
                    .fixedSize()
            }
            ScrollView(.horizontal) {
                HStack(alignment: .top, spacing: 16) {
                    VStack(spacing: 6) {
                        ThumbnailView(photo: ref, store: library).frame(width: 150, height: 190)
                        Text("Reference").font(Theme.font(12, .semibold))
                    }
                    Rectangle().fill(Theme.hairline).frame(width: 1, height: 190)
                    if attempts.isEmpty {
                        Text("No attempts yet").foregroundStyle(Theme.muted).frame(height: 190)
                    }
                    ForEach(attempts) { attempt in
                        VStack(spacing: 6) {
                            attemptTile(attempt)
                            Text(attempt.date, format: .dateTime.month(.abbreviated).day().year())
                                .font(Theme.font(12))
                                .foregroundStyle(Theme.muted)
                        }
                    }
                }
                .padding(.bottom, 8)
            }
        }
        .font(Theme.font(13))
        .foregroundStyle(Theme.ink)
        .fileImporter(isPresented: Binding(get: { attaching != nil }, set: { if !$0 { attaching = nil } }),
                      allowedContentTypes: [.image]) { result in
            guard case let .success(url) = result, let attempt = attaching else { return }
            let access = url.startAccessingSecurityScopedResource()
            defer { if access { url.stopAccessingSecurityScopedResource() } }
            do {
                try library.attachDrawing(attemptID: attempt.id, from: url)
            } catch {
                attachFailed = true
            }
        }
        .alert("That file couldn’t be read as an image.", isPresented: $attachFailed) {}
    }

    @ViewBuilder
    private func attemptTile(_ attempt: AttemptRecord) -> some View {
        if let name = attempt.drawingFileName {
            DrawingImageView(url: library.drawingURL(name))
                .frame(width: 150, height: 190)
                .clipShape(RoundedRectangle(cornerRadius: 8))
                .contentShape(Rectangle())
                .onTapGesture { onCompare(attempt) }
                .help("Compare with reference")
        } else {
            Button { attaching = attempt } label: {
                VStack(spacing: 6) {
                    Image(systemName: "plus").font(.system(size: 14, weight: .light))
                    Text("Add drawing").font(Theme.font(13))
                }
                .foregroundStyle(Theme.ink)
                .frame(width: 150, height: 190)
                .overlay(RoundedRectangle(cornerRadius: 8).strokeBorder(Theme.hairline, lineWidth: 1))
                .contentShape(Rectangle())
            }
            .buttonStyle(.plain)
        }
    }
}

private struct ComparisonSheet: View {
    let attempt: AttemptRecord
    @Environment(LibraryStore.self) private var library
    @Environment(\.dismiss) private var dismiss

    var body: some View {
        VStack(spacing: 20) {
            HStack(spacing: 24) {
                if let ref = library.photo(attempt.photoID) {
                    VStack(spacing: 10) {
                        ThumbnailView(photo: ref, store: library, maxPixel: 1600)
                        Text("Reference").font(Theme.font(14, .semibold))
                    }
                }
                if let name = attempt.drawingFileName {
                    VStack(spacing: 10) {
                        DrawingImageView(url: library.drawingURL(name), maxPixel: 1600)
                        Text(attempt.date, format: .dateTime.month(.wide).day().year()).font(Theme.font(14, .semibold))
                    }
                }
            }
            HStack {
                Spacer()
                Button("Done") { dismiss() }
                    .buttonStyle(CapsuleButtonStyle(height: 30, filled: true))
                    .keyboardShortcut(.defaultAction)
            }
        }
        .foregroundStyle(Theme.ink)
        .padding(28)
        .frame(minWidth: 900, minHeight: 640)
        .background(Theme.ground)
    }
}
