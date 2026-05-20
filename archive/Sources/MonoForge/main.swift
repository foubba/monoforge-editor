import AppKit

final class SceneObject: Codable {
    var id: String
    var name: String
    var type: String
    var x: CGFloat
    var y: CGFloat
    var width: CGFloat
    var height: CGFloat
    var color: String
    var layer: Int
    var visible: Bool

    init(id: String, name: String, type: String, x: CGFloat, y: CGFloat, width: CGFloat, height: CGFloat, color: String, layer: Int, visible: Bool = true) {
        self.id = id
        self.name = name
        self.type = type
        self.x = x
        self.y = y
        self.width = width
        self.height = height
        self.color = color
        self.layer = layer
        self.visible = visible
    }
}

struct SceneDocument: Codable {
    var name: String
    var objects: [SceneObject]
}

extension NSColor {
    convenience init(hex: String) {
        let raw = hex.trimmingCharacters(in: CharacterSet(charactersIn: "#"))
        let scanner = Scanner(string: raw)
        var value: UInt64 = 0
        scanner.scanHexInt64(&value)
        let r = CGFloat((value >> 16) & 0xff) / 255.0
        let g = CGFloat((value >> 8) & 0xff) / 255.0
        let b = CGFloat(value & 0xff) / 255.0
        self.init(calibratedRed: r, green: g, blue: b, alpha: 1.0)
    }
}

final class SceneCanvasView: NSView {
    var objects: [SceneObject] = []
    var selectedId: String?
    var showGrid = true
    var zoom: CGFloat = 1.0
    var camera = CGPoint(x: 90, y: 78)
    var onSelect: ((String?) -> Void)?
    var onMove: (() -> Void)?
    var onAddSprite: ((CGPoint) -> Void)?
    var currentTool: String = "select"

    private var dragObject: SceneObject?
    private var dragOffset = CGPoint.zero

    override var acceptsFirstResponder: Bool { true }
    override var isFlipped: Bool { true }

    override func draw(_ dirtyRect: NSRect) {
        NSColor(hex: "111318").setFill()
        bounds.fill()
        if showGrid {
            drawGrid()
        }
        for object in objects.sorted(by: { $0.layer < $1.layer }) where object.visible {
            draw(object)
        }
        drawHud()
    }

    private func drawGrid() {
        let size = max(8, 32 * zoom)
        NSColor(hex: "252a33").setStroke()
        let path = NSBezierPath()
        path.lineWidth = 1
        var x = camera.x.truncatingRemainder(dividingBy: size)
        while x < bounds.width {
            path.move(to: CGPoint(x: x, y: 0))
            path.line(to: CGPoint(x: x, y: bounds.height))
            x += size
        }
        var y = camera.y.truncatingRemainder(dividingBy: size)
        while y < bounds.height {
            path.move(to: CGPoint(x: 0, y: y))
            path.line(to: CGPoint(x: bounds.width, y: y))
            y += size
        }
        path.stroke()
    }

    private func draw(_ object: SceneObject) {
        let origin = worldToScreen(CGPoint(x: object.x, y: object.y))
        let rect = CGRect(x: origin.x, y: origin.y, width: object.width * zoom, height: object.height * zoom)
        let fill = NSColor(hex: object.color).withAlphaComponent(object.type == "Marker" ? 0.35 : 0.92)
        fill.setFill()
        rect.fill()

        let border = object.id == selectedId ? NSColor.white : NSColor(hex: "4a5260")
        border.setStroke()
        let borderPath = NSBezierPath(rect: rect)
        borderPath.lineWidth = object.id == selectedId ? 2 : 1
        borderPath.stroke()

        if object.type == "Marker" {
            let cross = NSBezierPath()
            NSColor(hex: object.color).setStroke()
            cross.move(to: rect.origin)
            cross.line(to: CGPoint(x: rect.maxX, y: rect.maxY))
            cross.move(to: CGPoint(x: rect.maxX, y: rect.minY))
            cross.line(to: CGPoint(x: rect.minX, y: rect.maxY))
            cross.stroke()
        }

        let attrs: [NSAttributedString.Key: Any] = [
            .foregroundColor: NSColor(hex: "dfe6f0"),
            .font: NSFont.systemFont(ofSize: 12, weight: .medium)
        ]
        object.name.draw(at: CGPoint(x: rect.minX, y: rect.minY - 18), withAttributes: attrs)
    }

    private func drawHud() {
        let text = "\(Int(zoom * 100))%   2D"
        let attrs: [NSAttributedString.Key: Any] = [
            .foregroundColor: NSColor(hex: "d7dce5"),
            .font: NSFont.monospacedSystemFont(ofSize: 12, weight: .medium)
        ]
        text.draw(at: CGPoint(x: bounds.width - 82, y: bounds.height - 30), withAttributes: attrs)
    }

    override func mouseDown(with event: NSEvent) {
        let point = convert(event.locationInWindow, from: nil)
        let world = screenToWorld(point)
        if currentTool == "rect" {
            onAddSprite?(world)
            return
        }
        let hit = objects.sorted(by: { $0.layer > $1.layer }).first { object in
            object.visible &&
            world.x >= object.x &&
            world.y >= object.y &&
            world.x <= object.x + object.width &&
            world.y <= object.y + object.height
        }
        selectedId = hit?.id
        dragObject = hit
        if let hit {
            dragOffset = CGPoint(x: world.x - hit.x, y: world.y - hit.y)
        }
        onSelect?(hit?.id)
        needsDisplay = true
    }

    override func mouseDragged(with event: NSEvent) {
        guard let object = dragObject else { return }
        let point = convert(event.locationInWindow, from: nil)
        let world = screenToWorld(point)
        object.x = round((world.x - dragOffset.x) / 4) * 4
        object.y = round((world.y - dragOffset.y) / 4) * 4
        onMove?()
        needsDisplay = true
    }

    override func mouseUp(with event: NSEvent) {
        dragObject = nil
    }

    override func scrollWheel(with event: NSEvent) {
        zoom = min(2.5, max(0.35, zoom + (event.deltaY > 0 ? 0.08 : -0.08)))
        needsDisplay = true
    }

    private func worldToScreen(_ point: CGPoint) -> CGPoint {
        CGPoint(x: point.x * zoom + camera.x, y: point.y * zoom + camera.y)
    }

    private func screenToWorld(_ point: CGPoint) -> CGPoint {
        CGPoint(x: (point.x - camera.x) / zoom, y: (point.y - camera.y) / zoom)
    }
}

final class PixelEditorView: NSView {
    var activeColor = "#65a7ff"
    var pixels = Array(repeating: Array(repeating: "", count: 16), count: 16)
    var onPaint: ((String) -> Void)?

    override var isFlipped: Bool { true }

    override func draw(_ dirtyRect: NSRect) {
        NSColor(hex: "101217").setFill()
        bounds.fill()
        let cell = min(bounds.width, bounds.height) / 16
        for y in 0..<16 {
            for x in 0..<16 {
                NSColor(hex: pixels[y][x].isEmpty ? "101217" : pixels[y][x]).setFill()
                CGRect(x: CGFloat(x) * cell, y: CGFloat(y) * cell, width: cell, height: cell).fill()
                NSColor(hex: "242a33").setStroke()
                NSBezierPath(rect: CGRect(x: CGFloat(x) * cell, y: CGFloat(y) * cell, width: cell, height: cell)).stroke()
            }
        }
    }

    override func mouseDown(with event: NSEvent) {
        let point = convert(event.locationInWindow, from: nil)
        let cell = min(bounds.width, bounds.height) / 16
        let x = Int(point.x / cell)
        let y = Int(point.y / cell)
        guard x >= 0, y >= 0, x < 16, y < 16 else { return }
        pixels[y][x] = activeColor
        onPaint?(activeColor)
        needsDisplay = true
    }
}

final class EditorRootView: NSView {
    weak var menuBarView: NSView?
    weak var toolbarView: NSView?
    weak var mainView: NSView?
    weak var bottomView: NSView?
    weak var statusView: NSView?

    override var isFlipped: Bool { true }

    func install(menuBar: NSView, toolbar: NSView, main: NSView, bottom: NSView, status: NSView) {
        menuBarView = menuBar
        toolbarView = toolbar
        mainView = main
        bottomView = bottom
        statusView = status

        for view in [menuBar, toolbar, main, bottom, status] {
            view.translatesAutoresizingMaskIntoConstraints = true
            addSubview(view)
        }
    }

    override func layout() {
        super.layout()
        let width = bounds.width
        let height = bounds.height
        let menuHeight: CGFloat = 38
        let toolbarHeight: CGFloat = 38
        let bottomHeight = max(188, min(230, height * 0.26))
        let statusHeight: CGFloat = 25
        let mainY = menuHeight + toolbarHeight
        let mainHeight = max(220, height - menuHeight - toolbarHeight - bottomHeight - statusHeight)

        menuBarView?.frame = CGRect(x: 0, y: 0, width: width, height: menuHeight)
        toolbarView?.frame = CGRect(x: 0, y: menuHeight, width: width, height: toolbarHeight)
        mainView?.frame = CGRect(x: 0, y: mainY, width: width, height: mainHeight)
        bottomView?.frame = CGRect(x: 0, y: mainY + mainHeight, width: width, height: bottomHeight)
        statusView?.frame = CGRect(x: 0, y: height - statusHeight, width: width, height: statusHeight)
    }
}

@MainActor
final class EditorController: NSObject, NSWindowDelegate {
    private var window: NSWindow!
    private let sceneCanvas = SceneCanvasView()
    private let pixelEditor = PixelEditorView()
    private let assetsStack = NSStackView()
    private let outlineStack = NSStackView()
    private let propertiesStack = NSStackView()
    private let consoleText = NSTextView()
    private let statusLabel = NSTextField(labelWithString: "/sample_project/main.collection")
    private var selectedId: String? = "player"
    private var currentTool = "select"
    private var activeColor = "#65a7ff"

    private var scene = SceneDocument(
        name: "main.collection",
        objects: [
            SceneObject(id: "player", name: "Player", type: "Sprite", x: 120, y: 96, width: 64, height: 64, color: "#65a7ff", layer: 2),
            SceneObject(id: "crate", name: "Crate", type: "Sprite", x: 320, y: 160, width: 96, height: 72, color: "#c7a76c", layer: 1),
            SceneObject(id: "spawn", name: "SpawnPoint", type: "Marker", x: 96, y: 260, width: 32, height: 32, color: "#7bd88f", layer: 3),
        ]
    )

    func show() {
        window = NSWindow(
            contentRect: NSRect(x: 0, y: 0, width: 1320, height: 820),
            styleMask: [.titled, .closable, .miniaturizable, .resizable],
            backing: .buffered,
            defer: false
        )
        window.title = "MonoForge Editor"
        window.minSize = NSSize(width: 1040, height: 680)
        window.center()
        window.delegate = self
        buildInterface()
        renderAll()
        log("MonoForge native editor ready", kind: "ok")
        window.makeKeyAndOrderFront(nil)
    }

    private func buildInterface() {
        let root = EditorRootView()
        root.wantsLayer = true
        root.layer?.backgroundColor = NSColor(hex: "15171b").cgColor
        window.contentView = root

        let menu = menuBar()
        let tools = toolbar()

        let mainSplit = NSSplitView()
        mainSplit.isVertical = true
        mainSplit.dividerStyle = .thin

        let assetsPane = pane(title: "Assets", content: scroll(assetsStack), footer: changedFiles())
        mainSplit.addArrangedSubview(assetsPane)
        mainSplit.addArrangedSubview(centerPane())

        let rightSplit = NSSplitView()
        rightSplit.isVertical = false
        rightSplit.dividerStyle = .thin
        rightSplit.addArrangedSubview(pane(title: "Outline", content: scroll(outlineStack), footer: outlineActions()))
        rightSplit.addArrangedSubview(pane(title: "Properties", content: scroll(propertiesStack), footer: nil))
        mainSplit.addArrangedSubview(rightSplit)

        mainSplit.setPosition(260, ofDividerAt: 0)
        mainSplit.setPosition(1000, ofDividerAt: 1)
        rightSplit.setPosition(260, ofDividerAt: 0)

        let bottomSplit = NSSplitView()
        bottomSplit.isVertical = true
        bottomSplit.dividerStyle = .thin
        bottomSplit.addArrangedSubview(consolePane())
        bottomSplit.addArrangedSubview(spritePane())

        statusLabel.backgroundColor = NSColor(hex: "1b1e24")
        statusLabel.textColor = NSColor(hex: "8e96a3")
        statusLabel.isBezeled = false
        statusLabel.drawsBackground = true

        root.install(menuBar: menu, toolbar: tools, main: mainSplit, bottom: bottomSplit, status: statusLabel)

        DispatchQueue.main.async {
            mainSplit.setPosition(260, ofDividerAt: 0)
            mainSplit.setPosition(max(620, mainSplit.bounds.width - 320), ofDividerAt: 1)
            rightSplit.setPosition(max(180, rightSplit.bounds.height * 0.48), ofDividerAt: 0)
            bottomSplit.setPosition(max(520, bottomSplit.bounds.width - 270), ofDividerAt: 0)
        }
    }

    private func menuBar() -> NSView {
        let bar = horizontalStack()
        bar.wantsLayer = true
        bar.layer?.backgroundColor = NSColor(hex: "1b1e24").cgColor
        bar.heightAnchor.constraint(equalToConstant: 38).isActive = true
        bar.addArrangedSubview(label("◆  MonoForge", color: "eef2f8", weight: .bold))
        for title in ["File", "Edit", "View", "Project", "Debug", "Help"] {
            bar.addArrangedSubview(flatButton(title, action: nil))
        }
        bar.addArrangedSubview(spacer())
        bar.addArrangedSubview(flatButton("Save", action: #selector(saveScene)))
        bar.addArrangedSubview(flatButton("Load", action: #selector(loadScene)))
        bar.addArrangedSubview(primaryButton("Build", action: #selector(buildProject)))
        return bar
    }

    private func toolbar() -> NSView {
        let bar = horizontalStack()
        bar.wantsLayer = true
        bar.layer?.backgroundColor = NSColor(hex: "20242b").cgColor
        bar.heightAnchor.constraint(equalToConstant: 38).isActive = true
        for (title, tool) in [("S", "select"), ("M", "move"), ("R", "rect")] {
            let button = flatButton(title, action: #selector(selectTool(_:)))
            button.identifier = NSUserInterfaceItemIdentifier(tool)
            bar.addArrangedSubview(button)
        }
        bar.addArrangedSubview(flatButton("F", action: #selector(frameScene)))
        bar.addArrangedSubview(flatButton("Grid", action: #selector(toggleGrid)))
        bar.addArrangedSubview(spacer())
        bar.addArrangedSubview(label("Ready", color: "8e96a3", weight: .regular))
        return bar
    }

    private func centerPane() -> NSView {
        let stack = verticalStack()
        let tabs = horizontalStack()
        tabs.wantsLayer = true
        tabs.layer?.backgroundColor = NSColor(hex: "1b1e24").cgColor
        tabs.heightAnchor.constraint(equalToConstant: 34).isActive = true
        tabs.addArrangedSubview(label("  main.collection  ", color: "f3f7ff", weight: .bold, background: "111318"))
        tabs.addArrangedSubview(label("  player.sprite  ", color: "8e96a3", weight: .regular, background: "1b1e24"))
        tabs.addArrangedSubview(spacer())
        stack.addArrangedSubview(tabs)

        sceneCanvas.objects = scene.objects
        sceneCanvas.selectedId = selectedId
        sceneCanvas.onSelect = { [weak self] id in
            self?.selectedId = id
            self?.renderOutline()
            self?.renderProperties()
            self?.updateStatus()
        }
        sceneCanvas.onMove = { [weak self] in
            self?.renderProperties()
            self?.updateStatus()
        }
        sceneCanvas.onAddSprite = { [weak self] point in
            self?.addSprite(at: point)
        }
        stack.addArrangedSubview(sceneCanvas)
        return stack
    }

    private func consolePane() -> NSView {
        let stack = verticalStack()
        let tabs = horizontalStack()
        tabs.heightAnchor.constraint(equalToConstant: 34).isActive = true
        tabs.wantsLayer = true
        tabs.layer?.backgroundColor = NSColor(hex: "1b1e24").cgColor
        tabs.addArrangedSubview(label("  Console  ", color: "eef6ff", weight: .bold, background: "263d5f"))
        tabs.addArrangedSubview(label("  Build Errors  ", color: "8e96a3", weight: .regular, background: "1b1e24"))
        tabs.addArrangedSubview(label("  Search Results  ", color: "8e96a3", weight: .regular, background: "1b1e24"))
        tabs.addArrangedSubview(spacer())
        stack.addArrangedSubview(tabs)

        consoleText.isEditable = false
        consoleText.backgroundColor = NSColor(hex: "191c21")
        consoleText.textColor = NSColor(hex: "d7dce5")
        consoleText.font = NSFont.monospacedSystemFont(ofSize: 12, weight: .regular)
        stack.addArrangedSubview(scroll(consoleText))
        return stack
    }

    private func spritePane() -> NSView {
        let stack = verticalStack()
        stack.widthAnchor.constraint(equalToConstant: 270).isActive = true
        stack.addArrangedSubview(label("  Sprite Editor", color: "f3f7ff", weight: .bold))
        pixelEditor.activeColor = activeColor
        pixelEditor.onPaint = { [weak self] color in
            guard let self else { return }
            self.selectedObject()?.color = color
            self.renderProperties()
            self.sceneCanvas.needsDisplay = true
        }
        pixelEditor.widthAnchor.constraint(equalToConstant: 192).isActive = true
        pixelEditor.heightAnchor.constraint(equalToConstant: 192).isActive = true
        stack.addArrangedSubview(pixelEditor)
        let palette = horizontalStack()
        for color in ["65a7ff", "7bd88f", "ffd166", "ff6b6b", "d7dce5", "15171b"] {
            let button = NSButton(title: "", target: self, action: #selector(selectColor(_:)))
            button.identifier = NSUserInterfaceItemIdentifier("#\(color)")
            button.bezelStyle = .regularSquare
            button.wantsLayer = true
            button.layer?.backgroundColor = NSColor(hex: color).cgColor
            button.widthAnchor.constraint(equalToConstant: 24).isActive = true
            button.heightAnchor.constraint(equalToConstant: 24).isActive = true
            palette.addArrangedSubview(button)
        }
        stack.addArrangedSubview(palette)
        return stack
    }

    private func pane(title: String, content: NSView, footer: NSView?) -> NSView {
        let stack = verticalStack()
        stack.wantsLayer = true
        stack.layer?.backgroundColor = NSColor(hex: "202329").cgColor
        let header = label("  \(title)", color: "f3f7ff", weight: .bold, background: "202329")
        header.heightAnchor.constraint(equalToConstant: 34).isActive = true
        stack.addArrangedSubview(header)
        stack.addArrangedSubview(content)
        if let footer {
            stack.addArrangedSubview(footer)
        }
        return stack
    }

    private func changedFiles() -> NSView {
        let stack = verticalStack()
        stack.heightAnchor.constraint(equalToConstant: 112).isActive = true
        stack.addArrangedSubview(label("  Changed Files", color: "f3f7ff", weight: .bold, background: "191c21"))
        stack.addArrangedSubview(label("  scenes/main.scene.json", color: "8e96a3", weight: .regular, background: "191c21"))
        stack.addArrangedSubview(label("  sprites/player.sprite", color: "8e96a3", weight: .regular, background: "191c21"))
        return stack
    }

    private func outlineActions() -> NSView {
        let stack = horizontalStack()
        stack.heightAnchor.constraint(equalToConstant: 44).isActive = true
        stack.addArrangedSubview(flatButton("Duplicate", action: #selector(duplicateSelected)))
        stack.addArrangedSubview(flatButton("Delete", action: #selector(deleteSelected)))
        stack.addArrangedSubview(spacer())
        return stack
    }

    private func renderAll() {
        renderAssets()
        renderOutline()
        renderProperties()
        updateStatus()
        sceneCanvas.objects = scene.objects
        sceneCanvas.selectedId = selectedId
        sceneCanvas.needsDisplay = true
    }

    private func renderAssets() {
        assetsStack.arrangedSubviews.forEach { $0.removeFromSuperview() }
        for item in ["assets", "  • sprites/player.sprite", "  • sprites/crate.sprite", "  • scenes/main.scene.json", "  • scripts/player.cs"] {
            assetsStack.addArrangedSubview(label("  \(item)", color: item == "assets" ? "d7dce5" : "8e96a3", weight: item == "assets" ? .bold : .regular))
        }
    }

    private func renderOutline() {
        outlineStack.arrangedSubviews.forEach { $0.removeFromSuperview() }
        for object in scene.objects {
            let button = flatButton("\(object.name)   \(object.visible ? "show" : "hide")", action: #selector(selectOutline(_:)))
            button.identifier = NSUserInterfaceItemIdentifier(object.id)
            if object.id == selectedId {
                button.wantsLayer = true
                button.layer?.backgroundColor = NSColor(hex: "2b3442").cgColor
            }
            outlineStack.addArrangedSubview(button)
        }
        sceneCanvas.selectedId = selectedId
        sceneCanvas.needsDisplay = true
    }

    private func renderProperties() {
        propertiesStack.arrangedSubviews.forEach { $0.removeFromSuperview() }
        guard let object = selectedObject() else {
            propertiesStack.addArrangedSubview(label("  Select an object to inspect it.", color: "8e96a3", weight: .regular))
            return
        }
        addProperty("Name", value: object.name, key: "name")
        addProperty("X", value: "\(Int(object.x))", key: "x")
        addProperty("Y", value: "\(Int(object.y))", key: "y")
        addProperty("Width", value: "\(Int(object.width))", key: "width")
        addProperty("Height", value: "\(Int(object.height))", key: "height")
        addProperty("Layer", value: "\(object.layer)", key: "layer")
        addProperty("Color", value: object.color, key: "color")
    }

    private func addProperty(_ title: String, value: String, key: String) {
        let row = horizontalStack()
        row.heightAnchor.constraint(equalToConstant: 32).isActive = true
        let titleLabel = label(title, color: "8e96a3", weight: .regular)
        titleLabel.widthAnchor.constraint(equalToConstant: 78).isActive = true
        let field = NSTextField(string: value)
        field.identifier = NSUserInterfaceItemIdentifier(key)
        field.target = self
        field.action = #selector(applyProperty(_:))
        field.backgroundColor = NSColor(hex: "16191f")
        field.textColor = NSColor(hex: "d7dce5")
        field.isBordered = true
        row.addArrangedSubview(titleLabel)
        row.addArrangedSubview(field)
        propertiesStack.addArrangedSubview(row)
    }

    @objc private func selectTool(_ sender: NSButton) {
        currentTool = sender.identifier?.rawValue ?? "select"
        sceneCanvas.currentTool = currentTool
    }

    @objc private func toggleGrid() {
        sceneCanvas.showGrid.toggle()
        sceneCanvas.needsDisplay = true
    }

    @objc private func frameScene() {
        sceneCanvas.zoom = 1
        sceneCanvas.camera = CGPoint(x: 90, y: 78)
        sceneCanvas.needsDisplay = true
    }

    @objc private func selectOutline(_ sender: NSButton) {
        selectedId = sender.identifier?.rawValue
        renderOutline()
        renderProperties()
        updateStatus()
    }

    @objc private func applyProperty(_ sender: NSTextField) {
        guard let key = sender.identifier?.rawValue, let object = selectedObject() else { return }
        switch key {
        case "name": object.name = sender.stringValue
        case "x": object.x = CGFloat(Double(sender.stringValue) ?? Double(object.x))
        case "y": object.y = CGFloat(Double(sender.stringValue) ?? Double(object.y))
        case "width": object.width = CGFloat(Double(sender.stringValue) ?? Double(object.width))
        case "height": object.height = CGFloat(Double(sender.stringValue) ?? Double(object.height))
        case "layer": object.layer = Int(sender.stringValue) ?? object.layer
        case "color": object.color = sender.stringValue
        default: break
        }
        renderOutline()
        renderProperties()
        updateStatus()
        sceneCanvas.needsDisplay = true
    }

    @objc private func selectColor(_ sender: NSButton) {
        activeColor = sender.identifier?.rawValue ?? "#65a7ff"
        pixelEditor.activeColor = activeColor
    }

    @objc private func duplicateSelected() {
        guard let object = selectedObject() else { return }
        let copy = SceneObject(id: "\(object.id)_copy_\(scene.objects.count + 1)", name: "\(object.name) Copy", type: object.type, x: object.x + 24, y: object.y + 24, width: object.width, height: object.height, color: object.color, layer: object.layer + 1)
        scene.objects.append(copy)
        selectedId = copy.id
        log("Duplicated \(object.name)", kind: "ok")
        renderAll()
    }

    @objc private func deleteSelected() {
        guard let id = selectedId else { return }
        scene.objects.removeAll { $0.id == id }
        selectedId = scene.objects.first?.id
        log("Deleted object", kind: "warn")
        renderAll()
    }

    private func addSprite(at point: CGPoint) {
        let id = "sprite_\(scene.objects.count + 1)"
        let object = SceneObject(id: id, name: "NewSprite", type: "Sprite", x: round(point.x / 8) * 8, y: round(point.y / 8) * 8, width: 64, height: 64, color: activeColor, layer: scene.objects.count + 1)
        scene.objects.append(object)
        selectedId = id
        log("Added NewSprite", kind: "ok")
        renderAll()
    }

    @objc private func saveScene() {
        let panel = NSSavePanel()
        panel.nameFieldStringValue = "main.scene.json"
        panel.allowedContentTypes = [.json]
        guard panel.runModal() == .OK, let url = panel.url else { return }
        do {
            let data = try JSONEncoder().encode(scene)
            try data.write(to: url)
            log("Saved \(url.lastPathComponent)", kind: "ok")
        } catch {
            alert("Could not save scene: \(error.localizedDescription)")
        }
    }

    @objc private func loadScene() {
        let panel = NSOpenPanel()
        panel.allowedContentTypes = [.json]
        guard panel.runModal() == .OK, let url = panel.url else { return }
        do {
            let data = try Data(contentsOf: url)
            scene = try JSONDecoder().decode(SceneDocument.self, from: data)
            selectedId = scene.objects.first?.id
            log("Loaded \(url.lastPathComponent)", kind: "ok")
            renderAll()
        } catch {
            alert("Could not load scene: \(error.localizedDescription)")
        }
    }

    @objc private func buildProject() {
        let panel = NSSavePanel()
        panel.nameFieldStringValue = "MonoForgeScene.generated.cs"
        panel.allowedContentTypes = [.sourceCode]
        guard panel.runModal() == .OK, let url = panel.url else { return }
        do {
            try generateMonoGameScene().write(to: url, atomically: true, encoding: .utf8)
            log("Generated MonoGame C# scene file", kind: "ok")
        } catch {
            alert("Could not generate C# file: \(error.localizedDescription)")
        }
    }

    private func selectedObject() -> SceneObject? {
        scene.objects.first { $0.id == selectedId }
    }

    private func updateStatus() {
        if let object = selectedObject() {
            statusLabel.stringValue = "/sample_project/main.collection     \(object.name) x:\(Int(object.x)) y:\(Int(object.y))"
        } else {
            statusLabel.stringValue = "/sample_project/main.collection     No selection"
        }
    }

    private func log(_ message: String, kind: String) {
        let prefix = kind == "ok" ? "[OK]" : kind == "warn" ? "[WARN]" : "[INFO]"
        consoleText.string = "\(prefix) \(message)\n" + consoleText.string
    }

    private func alert(_ message: String) {
        let alert = NSAlert()
        alert.messageText = "MonoForge"
        alert.informativeText = message
        alert.runModal()
    }

    private func generateMonoGameScene() -> String {
        let rows = scene.objects.map { object -> String in
            let color = object.color.trimmingCharacters(in: CharacterSet(charactersIn: "#"))
            let scanner = Scanner(string: color)
            var value: UInt64 = 0
            scanner.scanHexInt64(&value)
            let r = (value >> 16) & 0xff
            let g = (value >> 8) & 0xff
            let b = value & 0xff
            return "        new SceneSprite(\"\(object.id)\", \"\(object.name)\", new Rectangle(\(Int(object.x)), \(Int(object.y)), \(Int(object.width)), \(Int(object.height))), new Color(\(r), \(g), \(b)), \(object.layer), \(object.visible))"
        }.joined(separator: ",\n")

        let className = csharpIdentifier(scene.name.replacingOccurrences(of: ".collection", with: "")) + "Scene"
        return """
        using Microsoft.Xna.Framework;
        using Microsoft.Xna.Framework.Graphics;
        using System.Collections.Generic;

        namespace MonoForge.Generated;

        public readonly record struct SceneSprite(
            string Id,
            string Name,
            Rectangle Bounds,
            Color Tint,
            int Layer,
            bool Visible
        );

        public static class \(className)
        {
            public static IReadOnlyList<SceneSprite> Sprites { get; } = new[]
            {
        \(rows)
            };

            public static void Draw(SpriteBatch spriteBatch, Texture2D pixel)
            {
                foreach (var sprite in Sprites)
                {
                    if (!sprite.Visible)
                    {
                        continue;
                    }

                    spriteBatch.Draw(pixel, sprite.Bounds, sprite.Tint);
                }
            }
        }
        """
    }

    private func csharpIdentifier(_ value: String) -> String {
        let cleaned = value.map { char in
            char.isLetter || char.isNumber || char == "_" ? char : "_"
        }
        let safe = String(cleaned).isEmpty ? "Object" : String(cleaned)
        return safe.first?.isNumber == true ? "_\(safe)" : safe
    }

    private func verticalStack() -> NSStackView {
        let stack = NSStackView()
        stack.orientation = .vertical
        stack.spacing = 0
        stack.translatesAutoresizingMaskIntoConstraints = false
        return stack
    }

    private func horizontalStack() -> NSStackView {
        let stack = NSStackView()
        stack.orientation = .horizontal
        stack.alignment = .centerY
        stack.spacing = 4
        stack.edgeInsets = NSEdgeInsets(top: 0, left: 8, bottom: 0, right: 8)
        return stack
    }

    private func label(_ text: String, color: String, weight: NSFont.Weight, background: String = "202329") -> NSTextField {
        let label = NSTextField(labelWithString: text)
        label.textColor = NSColor(hex: color)
        label.font = NSFont.systemFont(ofSize: 13, weight: weight)
        label.backgroundColor = NSColor(hex: background)
        label.drawsBackground = true
        label.isBezeled = false
        return label
    }

    private func flatButton(_ title: String, action: Selector?) -> NSButton {
        let button = NSButton(title: title, target: action == nil ? nil : self, action: action)
        button.bezelStyle = .texturedSquare
        button.isBordered = false
        button.contentTintColor = NSColor(hex: "d7dce5")
        return button
    }

    private func primaryButton(_ title: String, action: Selector) -> NSButton {
        let button = flatButton(title, action: action)
        button.wantsLayer = true
        button.layer?.backgroundColor = NSColor(hex: "263d5f").cgColor
        return button
    }

    private func spacer() -> NSView {
        let view = NSView()
        view.setContentHuggingPriority(.defaultLow, for: .horizontal)
        return view
    }

    private func scroll(_ view: NSView) -> NSScrollView {
        let scroll = NSScrollView()
        scroll.documentView = view
        scroll.hasVerticalScroller = true
        scroll.drawsBackground = true
        scroll.backgroundColor = NSColor(hex: "202329")
        if let stack = view as? NSStackView {
            stack.orientation = .vertical
            stack.alignment = .leading
            stack.spacing = 2
            stack.edgeInsets = NSEdgeInsets(top: 8, left: 8, bottom: 8, right: 8)
        }
        return scroll
    }
}

@MainActor
final class AppDelegate: NSObject, NSApplicationDelegate {
    private var controller: EditorController?

    func applicationDidFinishLaunching(_ notification: Notification) {
        NSApp.setActivationPolicy(.regular)
        controller = EditorController()
        controller?.show()
        NSApp.activate(ignoringOtherApps: true)
    }

    func applicationShouldTerminateAfterLastWindowClosed(_ sender: NSApplication) -> Bool {
        true
    }
}

@main
@MainActor
enum MonoForgeMain {
    private static var delegate: AppDelegate?

    static func main() {
        let app = NSApplication.shared
        let appDelegate = AppDelegate()
        delegate = appDelegate
        app.delegate = appDelegate
        app.run()
    }
}
