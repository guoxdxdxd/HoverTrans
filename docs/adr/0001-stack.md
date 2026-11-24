# ADR 0001：技术栈选择

- 状态：已接受
- 日期：2026-11-24

## 背景

做一个 Windows 优先、兼容 macOS 的桌面翻译工具：悬停取词、文本/图片翻译、本地模型按需启停。要求 UI 有完整桌面感、能常驻托盘、体积可控、源码可公开。

## 决策

1. **.NET 11 + Avalonia 12**
   - 一套 C# 代码覆盖 Windows/macOS，原生窗口、置顶、无边框浮窗、剪贴板/文件选择齐全。
   - .NET 11 为当前 LTS 线，Avalonia 12 是 TKCode.Arc 的要求。

2. **TKCode.Arc 作为 UI 组件库**
   - MIT，提供 Arc 设计语言的 112 个组件、设计令牌、明暗主题和弹簧动效。
   - 官方模板直接生成 `net11.0` 工程，减少从零搭 UI 的时间。
   - 风险：库较新（2026-10 发布），升级时需跟随 Avalonia 12.x；因此 UI 只依赖稳定控件（Card/Button/Switch/ThemeSwitcher）和标准 Avalonia 控件。

3. **Hy-MT2 1.8B Q4_K_M + llama.cpp**
   - 1.1 GB，CPU 可跑，翻译专用，33 种语言。
   - llama.cpp 提供 Windows/macOS 预编译二进制和 OpenAI 兼容 SSE 接口；应用可以完全掌控进程启停。
   - 相比云端 API：离线、隐私、无按次成本；相比通用 7B：内存和首译延迟更低。

4. **Windows 平台层用 UI Automation + Windows.Media.Ocr**
   - UIA 是系统无障碍树，覆盖浏览器、Office、WinUI/WPF/WinForms 和多数 Electron 应用，不需要逐应用适配。
   - Windows OCR 无额外运行时，使用系统语言包；macOS 阶段替换为 Accessibility + Vision。

5. **多目标框架**
   - `net11.0`：macOS/Linux 构建，平台层为 `Null*`。
   - `net11.0-windows10.0.19041.0`：启用 FlaUI 与 WinRT OCR。
   - 用 `#if WINDOWS` 隔离平台代码，避免 macOS 构建被 Windows API 污染。

## 被否方案

- **WPF/WinForms**：只能 Windows，macOS 需要重写。
- **Electron/Tauri**：悬停取词仍需原生 UIA/AX，且安装包和内存更大。
- **云端翻译 API**：违背本地/隐私目标，且按量计费。
- **自带通用 LLM**：7B 以上内存占用高，翻译质量不一定优于专用 1.8B。
- **把模型打进安装包**：仓库和安装包过大，且模型许可/下载源需要用户可见；改为脚本下载 + 校验。

## 后果

- 需要 .NET 11 SDK 和 Windows 10 1809+。
- 悬停能力受目标应用 UIA 实现限制，需要真实场景回归清单。
- TKCode.Arc 处于早期，升级需跑 UI 冒烟测试。
- macOS 版本需要单独实现 UIA 等价层（AXUIElement）与 OCR（Vision），Core/UI 可复用。
