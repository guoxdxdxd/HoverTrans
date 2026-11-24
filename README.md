# 悬译 HoverTrans

Windows 优先、兼容 macOS 的本地 AI 翻译工具：鼠标悬停在外文上自动取词并译为中文，也支持输入文字、粘贴/拖入图片翻译。模型是腾讯 **Hy-MT2 1.8B（Q4_K_M GGUF）**，由 **llama.cpp** 在本机运行，**不上传云端**。

> 当前状态：Windows MVP 脚手架可编译、可运行、单元测试通过；悬停取词、文本/图片翻译、模型按需启动和 10 分钟闲置退出已接通。macOS 的 Accessibility/Vision 适配在路线图中（接口已留好）。模型与运行时体积较大，不随仓库分发。

## 功能

- **悬停即译**：UI Automation 读取鼠标下控件的文本；识别为非中文且稳定约 0.5 秒后，自动翻译为中文，并在鼠标旁弹出结果。
- **文本翻译**：输入或粘贴任意文本，自动检测源语言，也可手动指定源语言/目标语言（中文简繁、英、日、韩、法、德、西、俄等 20+ 种）。
- **图片翻译**：`Ctrl+V` 粘贴剪贴板图片，或选择图片文件；Windows 用系统 OCR 识别文字后再翻译。
- **本地模型生命周期**：第一次翻译才启动 `llama-server`；连续 10 分钟没有翻译请求自动退出，避免常驻内存。
- **流式结果**：译文边生成边显示，可取消、可复制。
- **主题**：跟随系统的明暗主题，Arc 组件库的动效与设计令牌。

## 快速开始（Windows）

### 1. 环境

- Windows 10 1809+ 或 Windows 11
- [.NET 11 SDK](https://dotnet.microsoft.com/download/dotnet/11.0)
- 约 2 GB 磁盘空间（运行时 ~30 MB + 模型 ~1.1 GB）

### 2. 下载运行时和模型

```powershell
powershell -ExecutionPolicy Bypass -File ./scripts/setup-model.ps1
# NVIDIA 显卡可换：powershell -ExecutionPolicy Bypass -File ./scripts/setup-model.ps1 -Variant cuda
```

脚本会查询 llama.cpp 最新 Windows 构建，下载到 `%LOCALAPPDATA%\HoverTrans\runtime`，并从 ModelScope（失败时 Hugging Face）下载 `Hy-MT2-1.8B-Q4_K_M.gguf` 到 `%LOCALAPPDATA%\HoverTrans\models`，最后打印 SHA256。

### 3. 运行

```powershell
dotnet run --project HoverTrans -f net11.0-windows10.0.19041.0
```

首次翻译会加载模型，通常需要 5–20 秒；之后同一进程内翻译只需数百毫秒。第一次加载的进度显示在状态栏。

### 4. 使用

- **悬停取词**：打开右上角「悬停取词」开关，把鼠标停在外文上约半秒，翻译浮窗会出现在鼠标旁。
- **文本**：在左侧输入，选择目标语言，点「翻译」。
- **图片**：截图后 `Ctrl+V`，或点「选择图片」，再点「翻译」。
- **设置**：展开左下角「设置」，可改 llama-server 路径、GGUF 路径、端口和闲置退出分钟数；模型路径/端口修改后重启应用生效。

## 架构

```
┌──────────────────────────── HoverTrans (Avalonia 12 + TKCode.Arc) ───────────────────────────┐
│  MainWindow / MainView                 HoverWindow (topmost, non-activating)                  │
│  MainViewModel                         HoverViewModel                                         │
│      │                                     ▲                                                  │
│      ▼                                     │                                                  │
│  HoverTranslationService ── HoverSnapshot ─┘                                                  │
│      │  polls every 300 ms, debounces 550 ms, suppresses duplicates                           │
│      ▼                                                                                         │
│  TranslationService ──► LlamaServerManager ──► llama-server (localhost:18080)                 │
│      │                     │  starts on demand, /health wait, idle stop after 10 min          │
│      └──► LlamaTranslationClient ──► POST /v1/chat/completions (SSE stream)                  │
│                                                                                               │
│  Windows: WindowsHoverTextSource (FlaUI/UIA3)   WindowsOcrService (Windows.Media.Ocr)         │
│  macOS:   planned AXUIElement + Vision                                                        │
└───────────────────────────────────────────────────────────────────────────────────────────────┘
```

- `HoverTrans/Core/`：不依赖 UI 的语言检测、分段、提示词、模型进程管理与 OpenAI 兼容客户端。
- `HoverTrans/Platform/Windows/`：Windows 专属的 UIA 取词和 OCR；`#if WINDOWS` 条件编译，macOS/Linux 使用 `Null*` 实现。
- `HoverTrans/Services/`：翻译编排、悬停轮询、浮窗管理。
- `HoverTrans.Tests/`：核心逻辑与 ViewModel 的 headless 测试。

更多细节见 [docs/architecture.md](docs/architecture.md)、[docs/model.md](docs/model.md) 和 [docs/adr/0001-stack.md](docs/adr/0001-stack.md)。

## 为什么是这些选择

- **.NET 11 + Avalonia 12**：一套代码跑 Windows/macOS，原生窗口与托盘能力完整；.NET 11 是当前 LTS 线。
- **TKCode.Arc**：[Arc 设计语言的原生 Avalonia 控件](https://github.com/AIDotNet/TKCode.Arc)，MIT，支持 `net11.0`、暗色/强调色/动效，UI 不用从零搭。
- **Hy-MT2 1.8B Q4_K_M**：1.1 GB、CPU 可跑、33 种语言，翻译任务专用；比通用 7B 模型更适合常驻桌面。
- **llama.cpp**：Windows/macOS 都有预编译二进制，OpenAI 兼容接口，启动/退出由应用控制。

## 开发

```powershell
dotnet build HoverTrans.slnx
dotnet test HoverTrans.slnx

# 只构建跨平台（macOS/Linux）目标
dotnet build HoverTrans/HoverTrans.csproj -f net11.0

# Windows 自包含发布
dotnet publish HoverTrans/HoverTrans.csproj -f net11.0-windows10.0.19041.0 -c Release -r win-x64 --self-contained true
```

需要 .NET 11 SDK。仓库根的 `global.json` 固定 `11.0.100`，并允许 11.0.x 的 feature band 向上滚动。

## 已知限制

- **悬停取词的覆盖率取决于目标应用的 UI Automation 实现**。浏览器、Office、WinUI/WPF/WinForms、多数 Electron 应用可用；游戏、自绘控件、部分 Java 应用可能读不到文本。
- 悬停浮窗是普通置顶窗口，不做鼠标穿透；浮窗出现后鼠标移开即恢复轮询。
- Windows OCR 需要系统安装对应语言的 OCR 组件（中文/英文通常已装）；装不上时会在状态栏提示。
- 图片翻译暂不保留版面，只输出识别到的纯文本。
- macOS 端目前只有接口和构建目标，Accessibility/Vision 实现待补。
- 首次加载模型 5–20 秒；1.8B Q4 运行时约占 1.2–1.8 GB 内存，闲置 10 分钟后释放。

## 隐私

- 所有翻译请求只发往 `127.0.0.1` 上由本应用启动的 llama-server。
- 没有遥测、没有账号、没有云端 API。
- 日志只写 `%LOCALAPPDATA%\HoverTrans\logs\llama-server.log`（llama.cpp 自身输出），不记录原文/译文。
- 设置保存在 `%APPDATA%\HoverTrans\settings.json`。

## 许可

本项目代码以 [MIT](LICENSE) 许可开源。

第三方组件：

- [TKCode.Arc](https://github.com/AIDotNet/TKCode.Arc) — MIT
- [Avalonia](https://avaloniaui.net/) — MIT
- [llama.cpp](https://github.com/ggml-org/llama.cpp) — MIT
- [FlaUI](https://github.com/FlaUI/FlaUI) — MIT
- [Hy-MT2 1.8B](https://huggingface.co/tencent/Hy-MT2-1.8B-GGUF) — Apache-2.0（模型权重不随本仓库分发）

---

## English

**HoverTrans** is a local-first AI translator for Windows (macOS planned). Hover over foreign text and it reads the control through UI Automation, detects non-Chinese text, and translates it to Chinese with Tencent **Hy-MT2 1.8B Q4_K_M** running in **llama.cpp**. Text input and clipboard/file images are supported too.

The model process starts on the first translation and stops after ten idle minutes. Nothing leaves the machine: all requests go to `127.0.0.1`.

```powershell
powershell -ExecutionPolicy Bypass -File ./scripts/setup-model.ps1
dotnet run --project HoverTrans -f net11.0-windows10.0.19041.0
```

MIT licensed. The model and llama.cpp binaries are downloaded separately, not committed.
