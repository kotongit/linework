import LineworkCore
import SwiftData
import SwiftUI
import UniformTypeIdentifiers

struct BenchmarkProgressView: View {
    @Query(filter: #Predicate<RefImage> { $0.isBenchmark }, sort: \RefImage.importedAt) private var benchmarks: [RefImage]
    @Query private var sessions: [SessionRecord]
    @State private var comparing: BenchmarkAttempt?

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
                        BenchmarkRow(ref: ref, sessionDates: sessions.map(\.startedAt)) { comparing = $0 }
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
    @Bindable var ref: RefImage
    let sessionDates: [Date]
    let onCompare: (BenchmarkAttempt) -> Void
    @Environment(\.modelContext) private var context
    @State private var attaching: BenchmarkAttempt?
    @State private var attachFailed = false

    private var dueText: String {
        let left = max(ref.benchmarkInterval, 1) - BenchmarkSchedule.sessionsSince(ref.benchmarkSince, sessionDates: sessionDates)
        return left <= 0 ? "Due next session" : "Next in \(left) session\(left == 1 ? "" : "s")"
    }

    var body: some View {
        VStack(alignment: .leading, spacing: 16) {
            HStack(spacing: 24) {
                Text(dueText).font(Theme.font(20, .medium))
                Spacer(minLength: 0)
                Stepper("Duration \(DurationFormat.short(ref.benchmarkSeconds))", value: $ref.benchmarkSeconds, in: 15...1800, step: 15)
                    .fixedSize()
                Stepper("Every \(ref.benchmarkInterval) sessions", value: $ref.benchmarkInterval, in: 1...100)
                    .fixedSize()
            }
            ScrollView(.horizontal) {
                HStack(alignment: .top, spacing: 16) {
                    VStack(spacing: 6) {
                        ThumbnailView(ref: ref).frame(width: 150, height: 190)
                        Text("Reference").font(Theme.font(12, .semibold))
                    }
                    Rectangle().fill(Theme.hairline).frame(width: 1, height: 190)
                    if ref.attempts.isEmpty {
                        Text("No attempts yet").foregroundStyle(Theme.muted).frame(height: 190)
                    }
                    ForEach(ref.sortedAttempts) { attempt in
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
            if let fileName = try? Storage.copyDrawing(url) {
                attempt.drawingFileName = fileName
                try? context.save()
            } else {
                attachFailed = true
            }
        }
        .alert("That file couldn’t be read as an image.", isPresented: $attachFailed) {}
    }

    @ViewBuilder
    private func attemptTile(_ attempt: BenchmarkAttempt) -> some View {
        if let name = attempt.drawingFileName {
            DrawingImageView(url: Storage.drawingURL(fileName: name))
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
    let attempt: BenchmarkAttempt
    @Environment(\.dismiss) private var dismiss

    var body: some View {
        VStack(spacing: 20) {
            HStack(spacing: 24) {
                if let ref = attempt.image {
                    VStack(spacing: 10) {
                        ThumbnailView(ref: ref, maxPixel: 1600)
                        Text("Reference").font(Theme.font(14, .semibold))
                    }
                }
                if let name = attempt.drawingFileName {
                    VStack(spacing: 10) {
                        DrawingImageView(url: Storage.drawingURL(fileName: name), maxPixel: 1600)
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
