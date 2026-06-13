# Codex LED Widget Native

一个让你更加焦虑地盯着 Codex 额度流失的桌面小组件。

它会悬在桌面上，用一个看起来很无辜的玻璃小球提醒你：今天的 5h 额度和 1week 额度正在安静地蒸发。

这是原生桌面版：Windows 使用 WPF / .NET 9，macOS 使用 Avalonia / .NET 10 + Swift/AppKit 原生悬浮球。目标是保留液态玻璃小组件的呈现效果，同时摆脱 Electron 打包后“一个额度提醒器比论文还重”的尴尬。

> 本项目受 [xicunwus2025-sys/codex-led-widget](https://github.com/xicunwus2025-sys/codex-led-widget) 启发，并保留对原项目的署名说明。

## 功能

- 显示 Codex 5 小时窗口额度和 7 天窗口额度，让焦虑有据可依
- 左侧双半圆液体仪表：左边 5h，右边 1w，像一个小小的精神压力容器
- 显示重置时间，例如 `6月8日 19:03`，方便你和时间谈判
- 支持中文 / English 切换，焦虑也可以国际化
- 支持窗口置顶、隐藏、刷新、退出，想看就看，想逃避也可以
- macOS 支持一键收起为液态玻璃悬浮球，单击悬浮球可重新打开面板，右键菜单可刷新或退出
- 支持等比缩放，避免文字位图拉伸发糊
- 原生 WPF 窗口，框架依赖版约 214KB，单文件自包含版约 71MB

## 运行要求

源码构建需要：

- Windows 10/11
- .NET SDK 9
- 已安装并登录 Codex CLI

macOS 试验版需要：

- macOS 13+
- .NET SDK 10
- Xcode Command Line Tools
- Python 3 + Pillow（用于生成 AppIcon）
- 已安装并登录 Codex CLI

单文件发布版已经包含 .NET 运行时，但仍需要使用者本机已安装并登录 Codex CLI，否则无法读取额度。

## 开发

```powershell
dotnet restore .\CodexLedWidgetNative.sln
dotnet test .\CodexLedWidgetNative.sln
dotnet build .\CodexLedWidgetNative.sln
```

调试运行：

```powershell
dotnet run --project .\CodexLedWidget.Wpf\CodexLedWidget.Wpf.csproj
```

macOS 试验版：

```bash
dotnet restore ./CodexLedWidget.Mac/CodexLedWidget.Mac.csproj
dotnet test ./CodexLedWidget.Tests/CodexLedWidget.Tests.csproj
dotnet run --project ./CodexLedWidget.Mac/CodexLedWidget.Mac.csproj
```

## 发布

框架依赖版，体积最小，但目标机器需要安装 .NET 9 Desktop Runtime：

```powershell
dotnet publish .\CodexLedWidget.Wpf\CodexLedWidget.Wpf.csproj `
  -c Release `
  -r win-x64 `
  --self-contained false `
  -p:PublishSingleFile=false `
  -o .\publish\framework-dependent
```

单文件自包含版，可以直接发送一个 exe：

```powershell
dotnet publish .\CodexLedWidget.Wpf\CodexLedWidget.Wpf.csproj `
  -c Release `
  -r win-x64 `
  --self-contained true `
  -p:PublishSingleFile=true `
  -p:IncludeNativeLibrariesForSelfExtract=true `
  -p:EnableCompressionInSingleFile=true `
  -p:DebugType=None `
  -p:DebugSymbols=false `
  -o .\publish\single-file
```

macOS Apple Silicon 试验版：

```bash
./scripts/package-macos-app.sh
open ./publish/Codex\ LED\ Widget.app
```

生成的 `.app` 是自包含发布包，不要求目标机器安装 .NET Runtime；目标机器仍需要安装并登录 Codex CLI。

## 隐私说明

本工具通过本机 Codex CLI 的 app-server 接口读取额度信息，不保存、不上传、不显示认证 Token。

## 许可证

MIT License。详见 [LICENSE](LICENSE)。
