import LineworkCore
import SwiftUI

enum AspectChoice: String, CaseIterable, Identifiable {
    case free = "Free", twoThree = "2:3", threeFour = "3:4", square = "1:1"
    var id: Self { self }

    /// Pixel width / height, or nil for free.
    var value: CGFloat? {
        switch self {
        case .free: nil
        case .twoThree: 2.0 / 3.0
        case .threeFour: 3.0 / 4.0
        case .square: 1
        }
    }
}

/// A crop, rotation and flip, as edited together.
struct CropEdit: Equatable {
    var crop: CGRect
    var rotation: Int
    var flipped: Bool
}

/// Edits a draft of an image's crop, rotation and flip; nothing is reported until Save.
struct CropEditorView: View {
    let imageURL: URL
    let initial: CropEdit
    let showsOrientation: Bool
    let onSave: (CropEdit) -> Void
    @Environment(\.dismiss) private var dismiss

    init(imageURL: URL, initial: CropEdit, showsOrientation: Bool = true, onSave: @escaping (CropEdit) -> Void) {
        self.imageURL = imageURL
        self.initial = initial
        self.showsOrientation = showsOrientation
        self.onSave = onSave
    }

    @State private var base: CGImage?
    @State private var oriented: CGImage?
    @State private var crop = CropMath.unit
    @State private var rotation = 0
    @State private var flipped = false
    @State private var aspect = AspectChoice.free
    @State private var dragStart: CGRect?
    @State private var detecting = false

    private var imageAspect: CGFloat {
        guard let oriented else { return 1 }
        return CGFloat(oriented.width) / CGFloat(oriented.height)
    }

    var body: some View {
        VStack(spacing: 0) {
            canvas
                .frame(minWidth: 840, minHeight: 480)
                .background(Color.black)
            controls
                .padding(.horizontal, 16)
                .padding(.vertical, 12)
                .background(Theme.ground)
        }
        .frame(idealWidth: 900, idealHeight: 700)
        .task {
            crop = initial.crop
            rotation = initial.rotation
            flipped = initial.flipped
            let url = imageURL
            base = await Task.detached { ImageRendering.loadCG(url: url, maxPixel: 1600) }.value
            reorient()
        }
    }

    private var canvas: some View {
        GeometryReader { geo in
            if let oriented {
                let frame = fittedFrame(imageSize: CGSize(width: oriented.width, height: oriented.height), in: geo.size)
                let box = CGRect(x: frame.minX + crop.minX * frame.width, y: frame.minY + crop.minY * frame.height,
                                 width: crop.width * frame.width, height: crop.height * frame.height)
                ZStack(alignment: .topLeading) {
                    Image(decorative: oriented, scale: 1)
                        .resizable()
                        .frame(width: frame.width, height: frame.height)
                        .offset(x: frame.minX, y: frame.minY)
                    Path { p in
                        p.addRect(frame)
                        p.addRect(box)
                    }
                    .fill(Color.black.opacity(0.55), style: FillStyle(eoFill: true))
                    .allowsHitTesting(false)
                    Rectangle()
                        .strokeBorder(Color.white, lineWidth: 1)
                        .contentShape(Rectangle())
                        .frame(width: box.width, height: box.height)
                        .offset(x: box.minX, y: box.minY)
                        .gesture(dragGesture(frame: frame) { start, delta in CropMath.moved(start, by: delta) })
                    ForEach(CropHandle.allCases, id: \.self) { handle in
                        handleView(handle, box: box, frame: frame)
                    }
                }
            } else {
                ProgressView().frame(maxWidth: .infinity, maxHeight: .infinity)
            }
        }
        .padding(20)
    }

    private func handleView(_ handle: CropHandle, box: CGRect, frame: CGRect) -> some View {
        let (hx, hy) = handle.direction
        let x = hx < 0 ? box.minX : hx > 0 ? box.maxX : box.midX
        let y = hy < 0 ? box.minY : hy > 0 ? box.maxY : box.midY
        return RoundedRectangle(cornerRadius: 2)
            .fill(Color.white)
            .overlay(RoundedRectangle(cornerRadius: 2).strokeBorder(Color.black, lineWidth: 1))
            .frame(width: 12, height: 12)
            .frame(width: 24, height: 24)
            .contentShape(Rectangle())
            .position(x: x, y: y)
            .gesture(dragGesture(frame: frame) { start, delta in
                CropMath.resized(start, handle: handle, by: delta, aspect: aspect.value, imageAspect: imageAspect)
            })
    }

    private func dragGesture(frame: CGRect, apply: @escaping (CGRect, CGSize) -> CGRect) -> some Gesture {
        DragGesture(minimumDistance: 0)
            .onChanged { value in
                let start = dragStart ?? crop
                dragStart = start
                let delta = CGSize(width: value.translation.width / frame.width, height: value.translation.height / frame.height)
                crop = apply(start, delta)
            }
            .onEnded { _ in dragStart = nil }
    }

    private var controls: some View {
        HStack(spacing: 10) {
            HStack(spacing: 4) {
                ForEach(AspectChoice.allCases) { choice in
                    Button(choice.rawValue) { aspect = choice }
                        .buttonStyle(CapsuleButtonStyle(height: 30, filled: aspect == choice))
                        .accessibilityAddTraits(aspect == choice ? .isSelected : [])
                }
            }
            .accessibilityElement(children: .contain)
            .accessibilityLabel("Aspect")
            .onChange(of: aspect) { _, new in
                if let value = new.value { crop = CropMath.fitAspect(crop, aspect: value, imageAspect: imageAspect) }
            }

            Button("Auto") { runAuto() }
                .disabled(oriented == nil || detecting)
                .opacity(oriented == nil || detecting ? 0.4 : 1)
            Button("Reset") { crop = CropMath.unit }
            if showsOrientation {
                Button("Rotate") {
                    // Rotation is applied before the mirror, so a flipped image turns the other way.
                    rotation = (rotation + (flipped ? 270 : 90)) % 360
                    crop = CropMath.unit  // the old normalized crop no longer matches the rotated frame
                    reorient()
                }
                .help("Rotate 90°")
                Button("Flip") {
                    flipped.toggle()
                    crop = CropMath.mirroredX(crop)
                    reorient()
                }
            }
            if detecting { ProgressView().controlSize(.small) }

            Spacer()
            Button("Cancel", role: .cancel) { dismiss() }
                .keyboardShortcut(.cancelAction)
            Button("Save") { save() }
                .buttonStyle(CapsuleButtonStyle(height: 30, filled: true))
                .keyboardShortcut(.defaultAction)
        }
        .buttonStyle(CapsuleButtonStyle(height: 30))
    }

    private func reorient() {
        guard let base else { return }
        oriented = ImageRendering.oriented(base, rotation: rotation, flipped: flipped)
    }

    private func runAuto() {
        guard let oriented else { return }
        detecting = true
        Task {
            let result = await AutoCropper.crop(for: oriented)
            crop = result.rect
            if let value = aspect.value { crop = CropMath.fitAspect(crop, aspect: value, imageAspect: imageAspect) }
            detecting = false
        }
    }

    private func save() {
        onSave(CropEdit(crop: crop, rotation: rotation, flipped: flipped))
        dismiss()
    }

    private func fittedFrame(imageSize: CGSize, in size: CGSize) -> CGRect {
        let scale = min(size.width / imageSize.width, size.height / imageSize.height)
        let w = imageSize.width * scale, h = imageSize.height * scale
        return CGRect(x: (size.width - w) / 2, y: (size.height - h) / 2, width: w, height: h)
    }
}

extension CropEditorView {
    /// The Library's editor: writes the result back to the image, clearing "no figure found" once cropped.
    init(image: RefImage) {
        self.init(imageURL: Storage.imageURL(fileName: image.fileName),
                  initial: CropEdit(crop: image.cropRect, rotation: image.rotation, flipped: image.flipped)) { edit in
            image.cropRect = edit.crop
            image.rotation = edit.rotation
            image.flipped = edit.flipped
            if edit.crop != CropMath.unit { image.noFigureFound = false }
        }
    }
}
