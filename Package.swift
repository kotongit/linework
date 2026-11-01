// swift-tools-version: 6.2
import PackageDescription

let package = Package(
    name: "Linework",
    platforms: [.macOS("27.0")],
    targets: [
        .target(name: "LineworkCore"),
        .executableTarget(name: "Linework", dependencies: ["LineworkCore"]),
        .testTarget(name: "LineworkCoreTests", dependencies: ["LineworkCore"]),
    ]
)
