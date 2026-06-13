import Cocoa

struct QuotaState {
    var primaryPercent: Int?
    var secondaryPercent: Int?
    var status: String
    var isError: Bool
}

final class CodexQuotaReader {
    func read() throws -> QuotaState {
        let codex = try resolveCodexPath()
        let process = Process()
        process.executableURL = URL(fileURLWithPath: codex)
        process.arguments = ["app-server", "--listen", "stdio://"]

        let stdin = Pipe()
        let stdout = Pipe()
        let stderr = Pipe()
        process.standardInput = stdin
        process.standardOutput = stdout
        process.standardError = stderr

        try process.run()

        let input = stdin.fileHandleForWriting
        try send(input, id: 1, method: "initialize", params: [
            "clientInfo": [
                "name": "codex-led-widget-native-mac",
                "title": "Codex LED Widget",
                "version": "0.1.0"
            ],
            "capabilities": NSNull()
        ])
        _ = try readResult(stdout.fileHandleForReading, id: 1)

        try send(input, id: 2, method: "account/rateLimits/read", params: nil)
        let result = try readResult(stdout.fileHandleForReading, id: 2)

        process.terminate()
        return try parseQuota(result)
    }

    private func resolveCodexPath() throws -> String {
        var candidates: [String] = []
        if let explicit = ProcessInfo.processInfo.environment["CODEX_CLI_PATH"], !explicit.isEmpty {
            candidates.append(explicit)
        }

        let home = FileManager.default.homeDirectoryForCurrentUser.path
        candidates.append(home + "/.local/bin/codex")
        candidates.append(home + "/.codex/packages/standalone/current/bin/codex")
        candidates.append("/opt/homebrew/bin/codex")
        candidates.append("/usr/local/bin/codex")

        for path in candidates where FileManager.default.isExecutableFile(atPath: path) {
            return path
        }

        throw NSError(domain: "CodexLedWidget", code: 1, userInfo: [
            NSLocalizedDescriptionKey: "未找到 Codex CLI"
        ])
    }

    private func send(_ input: FileHandle, id: Int, method: String, params: Any?) throws {
        var payload: [String: Any] = ["id": id, "method": method]
        if let params {
            payload["params"] = params
        }

        let data = try JSONSerialization.data(withJSONObject: payload)
        input.write(data)
        input.write(Data("\n".utf8))
    }

    private func readResult(_ output: FileHandle, id: Int) throws -> Any {
        var buffer = Data()
        let deadline = Date().addingTimeInterval(12)

        while Date() < deadline {
            if let line = readLineData(from: output, buffer: &buffer),
               let object = try JSONSerialization.jsonObject(with: line) as? [String: Any],
               object["id"] as? Int == id {
                if let error = object["error"] as? [String: Any] {
                    let message = error["message"] as? String ?? "Codex app-server error"
                    throw NSError(domain: "CodexLedWidget", code: 2, userInfo: [
                        NSLocalizedDescriptionKey: message
                    ])
                }

                if let result = object["result"] {
                    return result
                }
            }
        }

        throw NSError(domain: "CodexLedWidget", code: 3, userInfo: [
            NSLocalizedDescriptionKey: "读取 Codex 额度超时"
        ])
    }

    private func readLineData(from output: FileHandle, buffer: inout Data) -> Data? {
        while true {
            if let range = buffer.firstRange(of: Data("\n".utf8)) {
                let line = buffer.subdata(in: 0..<range.lowerBound)
                buffer.removeSubrange(0..<range.upperBound)
                return line
            }

            let data = output.availableData
            if data.isEmpty {
                return nil
            }

            buffer.append(data)
        }
    }

    private func parseQuota(_ result: Any) throws -> QuotaState {
        guard let root = result as? [String: Any] else {
            throw NSError(domain: "CodexLedWidget", code: 4, userInfo: [
                NSLocalizedDescriptionKey: "Codex 响应格式异常"
            ])
        }

        let snapshot = resolveSnapshot(root)
        let primary = remainingPercent(snapshot["primary"])
        let secondary = remainingPercent(snapshot["secondary"])
        return QuotaState(
            primaryPercent: primary,
            secondaryPercent: secondary,
            status: "已更新 \(DateFormatter.shortTime.string(from: Date()))",
            isError: false)
    }

    private func resolveSnapshot(_ root: [String: Any]) -> [String: Any] {
        if let map = root["rateLimitsByLimitId"] as? [String: Any] {
            if let codex = map["codex"] as? [String: Any] {
                return codex
            }

            for item in map.values {
                if let snapshot = item as? [String: Any] {
                    return snapshot
                }
            }
        }

        return root["rateLimits"] as? [String: Any] ?? root
    }

    private func remainingPercent(_ value: Any?) -> Int? {
        guard let window = value as? [String: Any] else {
            return nil
        }

        let used = (window["usedPercent"] as? NSNumber)?.intValue ?? 0
        return min(100, max(0, 100 - used))
    }
}

final class OrbView: NSView {
    var state = QuotaState(primaryPercent: nil, secondaryPercent: nil, status: "读取中", isError: false) {
        didSet { needsDisplay = true }
    }

    var onClick: (() -> Void)?
    var onDoubleClick: (() -> Void)?
    var onRightClick: ((NSPoint) -> Void)?
    private var dragStart: NSPoint?
    private var windowStart: NSPoint?
    private var wasDragged = false

    override var isFlipped: Bool { true }

    override func draw(_ dirtyRect: NSRect) {
        super.draw(dirtyRect)

        let orb = bounds.insetBy(dx: 4, dy: 4)
        let orbPath = NSBezierPath(ovalIn: orb)

        NSGraphicsContext.saveGraphicsState()
        orbPath.addClip()
        if let shellGradient = NSGradient(colors: [
            NSColor(calibratedRed: 0.98, green: 1.00, blue: 1.00, alpha: 0.82),
            NSColor(calibratedRed: 0.83, green: 0.94, blue: 1.00, alpha: 0.48),
            NSColor(calibratedRed: 0.98, green: 1.00, blue: 1.00, alpha: 0.64)
        ]) {
            shellGradient.draw(in: orb, angle: -42)
        }
        NSGraphicsContext.restoreGraphicsState()

        drawSegment(percent: state.primaryPercent, rect: orb, isLeft: true)
        drawSegment(percent: state.secondaryPercent, rect: orb, isLeft: false)

        NSGraphicsContext.saveGraphicsState()
        orbPath.addClip()
        if let glossGradient = NSGradient(colors: [
            NSColor(calibratedWhite: 1, alpha: 0.42),
            NSColor(calibratedWhite: 1, alpha: 0.06),
            NSColor(calibratedWhite: 1, alpha: 0)
        ]) {
            glossGradient.draw(in: NSRect(x: orb.minX + 10, y: orb.minY + 4, width: orb.width - 20, height: orb.height * 0.48), angle: -90)
        }
        NSColor(calibratedWhite: 1, alpha: 0.42).setStroke()
        let glint = NSBezierPath()
        glint.appendArc(
            withCenter: NSPoint(x: orb.midX, y: orb.midY),
            radius: orb.width * 0.39,
            startAngle: 214,
            endAngle: 314)
        glint.lineWidth = 2
        glint.stroke()
        NSGraphicsContext.restoreGraphicsState()

        NSColor(calibratedWhite: 1, alpha: 0.78).setStroke()
        orbPath.lineWidth = 1.3
        orbPath.stroke()
        NSColor(calibratedRed: 0.08, green: 0.20, blue: 0.30, alpha: 0.16).setStroke()
        NSBezierPath(ovalIn: orb.insetBy(dx: 1.4, dy: 1.4)).stroke()

        NSColor(calibratedWhite: 1, alpha: 0.42).setStroke()
        let divider = NSBezierPath()
        divider.move(to: NSPoint(x: orb.midX, y: orb.minY + 12))
        divider.line(to: NSPoint(x: orb.midX, y: orb.maxY - 12))
        divider.lineWidth = 1
        divider.stroke()
        NSColor(calibratedRed: 0.08, green: 0.16, blue: 0.24, alpha: 0.16).setStroke()
        let dividerShadow = NSBezierPath()
        dividerShadow.move(to: NSPoint(x: orb.midX + 1, y: orb.minY + 13))
        dividerShadow.line(to: NSPoint(x: orb.midX + 1, y: orb.maxY - 13))
        dividerShadow.lineWidth = 1
        dividerShadow.stroke()

        drawText("5h", at: NSPoint(x: orb.minX + orb.width * 0.27, y: orb.midY - 22), size: 13, bold: true)
        drawText("\(state.primaryPercent.map(String.init) ?? "--")%", at: NSPoint(x: orb.minX + orb.width * 0.27, y: orb.midY - 2), size: 21, bold: true)
        drawText("1w", at: NSPoint(x: orb.minX + orb.width * 0.73, y: orb.midY - 22), size: 13, bold: true)
        drawText("\(state.secondaryPercent.map(String.init) ?? "--")%", at: NSPoint(x: orb.minX + orb.width * 0.73, y: orb.midY - 2), size: 21, bold: true)
    }

    override func mouseDown(with event: NSEvent) {
        if event.clickCount >= 2 {
            onDoubleClick?()
            return
        }

        dragStart = event.locationInWindow
        windowStart = window?.frame.origin
        wasDragged = false
    }

    override func mouseDragged(with event: NSEvent) {
        guard let dragStart, let windowStart, let window else {
            return
        }

        let current = event.locationInWindow
        let delta = NSPoint(x: current.x - dragStart.x, y: current.y - dragStart.y)
        if abs(delta.x) < 4 && abs(delta.y) < 4 {
            return
        }

        wasDragged = true
        window.setFrameOrigin(NSPoint(x: windowStart.x + delta.x, y: windowStart.y + delta.y))
    }

    override func mouseUp(with event: NSEvent) {
        if !wasDragged {
            onClick?()
        }
    }

    override func rightMouseDown(with event: NSEvent) {
        onRightClick?(event.locationInWindow)
    }

    private func drawSegment(percent: Int?, rect: NSRect, isLeft: Bool) {
        guard let percent else {
            return
        }

        NSGraphicsContext.saveGraphicsState()
        let half = rect.width / 2
        let fillHeight = max(6, rect.height * CGFloat(percent) / 100)
        let clip = NSRect(
            x: isLeft ? rect.minX : rect.midX,
            y: rect.maxY - fillHeight,
            width: half,
            height: fillHeight)
        clip.clip()
        let orbPath = NSBezierPath(ovalIn: rect)
        orbPath.addClip()
        let colors = isLeft
            ? [
                NSColor(calibratedRed: 0.40, green: 1.00, blue: 0.82, alpha: 0.72),
                NSColor(calibratedRed: 0.02, green: 0.84, blue: 0.58, alpha: 0.76),
                NSColor(calibratedRed: 0.00, green: 0.58, blue: 0.52, alpha: 0.66)
            ]
            : [
                NSColor(calibratedRed: 0.52, green: 0.88, blue: 1.00, alpha: 0.70),
                NSColor(calibratedRed: 0.18, green: 0.50, blue: 1.00, alpha: 0.78),
                NSColor(calibratedRed: 0.47, green: 0.28, blue: 0.94, alpha: 0.64)
            ]
        if let gradient = NSGradient(colors: colors) {
            gradient.draw(in: rect, angle: isLeft ? -42 : -58)
        }

        NSColor(calibratedWhite: 1, alpha: 0.18).setFill()
        NSBezierPath(ovalIn: rect.insetBy(dx: 9, dy: 9)).fill()
        NSGraphicsContext.restoreGraphicsState()
    }

    private func drawText(_ text: String, at center: NSPoint, size: CGFloat, bold: Bool) {
        let attributes: [NSAttributedString.Key: Any] = [
            .font: bold ? NSFont.boldSystemFont(ofSize: size) : NSFont.systemFont(ofSize: size),
            .foregroundColor: NSColor(red: 0.07, green: 0.13, blue: 0.20, alpha: 1)
        ]
        let attributed = NSAttributedString(string: text, attributes: attributes)
        let textSize = attributed.size()
        attributed.draw(at: NSPoint(x: center.x - textSize.width / 2, y: center.y - textSize.height / 2))
    }
}

final class AppDelegate: NSObject, NSApplicationDelegate {
    private let reader = CodexQuotaReader()
    private let orbView = OrbView(frame: NSRect(x: 0, y: 0, width: 138, height: 138))
    private var panel: NSPanel!
    private var statusItem: NSStatusItem!
    private var timer: Timer?

    func applicationDidFinishLaunching(_ notification: Notification) {
        NSApp.setActivationPolicy(.accessory)
        createPanel()
        createStatusItem()
        orbView.onClick = { [weak self] in self?.openWidgetPanel() }
        orbView.onDoubleClick = { [weak self] in self?.openWidgetPanel() }
        orbView.onRightClick = { [weak self] point in self?.showOrbMenu(at: point) }
        refresh()
        timer = Timer.scheduledTimer(withTimeInterval: 300, repeats: true) { [weak self] _ in
            self?.refresh()
        }
    }

    private func createPanel() {
        panel = NSPanel(
            contentRect: NSRect(x: 0, y: 0, width: 138, height: 138),
            styleMask: [.borderless, .nonactivatingPanel],
            backing: .buffered,
            defer: false)
        panel.isFloatingPanel = true
        panel.level = .floating
        panel.collectionBehavior = [.canJoinAllSpaces, .fullScreenAuxiliary]
        panel.backgroundColor = .clear
        panel.isOpaque = false
        panel.hasShadow = true
        panel.contentView = orbView
        placeTopRight()
        panel.orderFrontRegardless()
    }

    private func createStatusItem() {
        statusItem = NSStatusBar.system.statusItem(withLength: NSStatusItem.variableLength)
        statusItem.button?.title = "Codex"
        statusItem.menu = buildMenu()
    }

    private func buildMenu() -> NSMenu {
        let menu = NSMenu()
        menu.addItem(NSMenuItem(title: "显示/隐藏悬浮球", action: #selector(togglePanel), keyEquivalent: ""))
        menu.addItem(NSMenuItem(title: "打开面板", action: #selector(openWidgetPanelFromMenu), keyEquivalent: "o"))
        menu.addItem(NSMenuItem(title: "刷新额度", action: #selector(refreshFromMenu), keyEquivalent: "r"))
        menu.addItem(NSMenuItem.separator())
        menu.addItem(NSMenuItem(title: "退出", action: #selector(quit), keyEquivalent: "q"))
        return menu
    }

    @objc private func togglePanel() {
        if panel.isVisible {
            panel.orderOut(nil)
        } else {
            panel.orderFrontRegardless()
        }
    }

    @objc private func refreshFromMenu() {
        refresh()
    }

    @objc private func openWidgetPanelFromMenu() {
        openWidgetPanel()
    }

    @objc private func quit() {
        NSApp.terminate(nil)
    }

    private func showOrbMenu(at point: NSPoint) {
        let menu = NSMenu()
        menu.addItem(NSMenuItem(title: "打开面板", action: #selector(openWidgetPanelFromMenu), keyEquivalent: ""))
        menu.addItem(NSMenuItem(title: panel.isVisible ? "隐藏悬浮球" : "显示悬浮球", action: #selector(togglePanel), keyEquivalent: ""))
        menu.addItem(NSMenuItem(title: "刷新额度", action: #selector(refreshFromMenu), keyEquivalent: ""))
        menu.addItem(NSMenuItem.separator())
        menu.addItem(NSMenuItem(title: "退出", action: #selector(quit), keyEquivalent: ""))
        NSMenu.popUpContextMenu(menu, with: NSApp.currentEvent ?? NSEvent(), for: orbView)
    }

    private func openWidgetPanel() {
        guard let app = containingWidgetAppURL() else {
            return
        }

        terminateWidgetPanelIfRunning()
        startWidgetPanel(from: app)
        panel.orderOut(nil)
    }

    private func startWidgetPanel(from app: URL) {
        let executable = app
            .appendingPathComponent("Contents")
            .appendingPathComponent("MacOS")
            .appendingPathComponent("CodexLedWidget.Mac")

        if FileManager.default.isExecutableFile(atPath: executable.path) {
            let process = Process()
            process.executableURL = executable
            process.currentDirectoryURL = executable.deletingLastPathComponent()
            try? process.run()
            return
        }

        NSWorkspace.shared.openApplication(at: app, configuration: NSWorkspace.OpenConfiguration())
    }

    private func containingWidgetAppURL() -> URL? {
        let executable = URL(fileURLWithPath: CommandLine.arguments[0]).standardizedFileURL
        let contents = executable
            .deletingLastPathComponent()
            .deletingLastPathComponent()

        if contents.lastPathComponent == "Contents" {
            return contents.deletingLastPathComponent()
        }

        let fallback = URL(fileURLWithPath: "/Users/iris/Applications/Codex LED Widget.app")
        return FileManager.default.fileExists(atPath: fallback.path) ? fallback : nil
    }

    private func terminateWidgetPanelIfRunning() {
        let process = Process()
        process.executableURL = URL(fileURLWithPath: "/usr/bin/pkill")
        process.arguments = ["-f", "CodexLedWidget.Mac"]
        try? process.run()
        process.waitUntilExit()
    }

    private func refresh() {
        DispatchQueue.global(qos: .userInitiated).async { [weak self] in
            guard let self else { return }

            let state: QuotaState
            do {
                state = try reader.read()
            } catch {
                state = QuotaState(
                    primaryPercent: nil,
                    secondaryPercent: nil,
                    status: error.localizedDescription,
                    isError: true)
            }

            DispatchQueue.main.async {
                self.orbView.state = state
                self.statusItem.button?.title = state.isError ? "Codex !" : "Codex \(state.primaryPercent.map { "\($0)%" } ?? "--")"
            }
        }
    }

    private func placeTopRight() {
        guard let screen = NSScreen.main else {
            return
        }

        let frame = screen.visibleFrame
        panel.setFrameOrigin(NSPoint(x: frame.maxX - 162, y: frame.maxY - 162))
    }
}

private extension DateFormatter {
    static let shortTime: DateFormatter = {
        let formatter = DateFormatter()
        formatter.dateFormat = "HH:mm"
        return formatter
    }()
}

let app = NSApplication.shared
let delegate = AppDelegate()
app.delegate = delegate
app.run()
