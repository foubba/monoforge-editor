// swift-tools-version: 6.0
import PackageDescription

let package = Package(
    name: "MonoForge",
    platforms: [.macOS(.v14)],
    products: [
        .executable(name: "MonoForge", targets: ["MonoForge"])
    ],
    targets: [
        .executableTarget(name: "MonoForge")
    ]
)
