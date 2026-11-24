# 悬译 HoverTrans

Windows 优先、兼容 macOS 的本地 AI 翻译工具：鼠标悬停在外文上自动取词并译为中文，也支持输入文字、粘贴/拖入图片翻译。模型是腾讯 **Hy-MT2 1.8B（Q4_K_M GGUF）**，由 **llama.cpp** 在本机运行，**不上传云端**。

> 当前状态：Windows MVP 脚手架可编译、可运行、单元测试通过；悬停取词、文本/图片翻译、模型按需启动和 10 分钟闲置退出已接通。macOS 的 Accessibility/Vision 适配在路线图中（接口已留好）。模型与运行时体积较大，不随仓库分发。

## 功能

- **悬停即译**：UI Automation 读取鼠标下控件的文本；识别为非中文且稳定约 0.5 秒后，自动翻译为中文，并在鼠标旁弹出结果。
- **文本翻译**：输入或粘贴任意文本，自动检测源语言，也可手动指定源语言/目标语言（中文简繁、英、日、韩、法、德、西、俄等 20+ 种）。
- **直译 / 意译**：语言对右侧勾选「意译」后按意思翻——口语化、俚语和习语换成地道说法；不勾选按原文字面直译。选择会被记住，悬停浮窗始终直译。
- **图片翻译**：`Ctrl+V` 粘贴剪贴板图片，或选择图片文件；Windows 用系统 OCR 识别文字后再翻译。
- **本地模型生命周期**：第一次翻译才启动 `llama-server`；连续 10 分钟没有翻译请求自动退出，避免常驻内存。
- **流式结果**：译文边生成边显示，可取消、可复制。
- **主题**：跟随系统的明暗主题，Arc 组件库的动效与设计令牌。
- **检查更新**：设置页「检查更新」直接读 GitHub 最新 release，比当前版本新就提示，并可一键打开下载页（[`guoxdxdxd/HoverTrans`](https://github.com/guoxdxdxd/HoverTrans)）。

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

首次翻译会加载模型，通常需要 5–20 秒；之后同一进程内翻译只需数百毫秒。第一次加载的进度显示在译文卡的底部状态行。

> **别跑错目标框架**：`net11.0` 是 macOS/Linux 目标，在 Windows 上也能启动，但没有 UI Automation 取词和系统 OCR——顶栏「悬停取词」开关是灰的、点不动，设置页会说明原因。Windows 上统一用上面的 `-f net11.0-windows10.0.19041.0`（Visual Studio 里同样把启动的目标框架切到它）。

### 4. 使用

- **翻译**：在左侧输入或粘贴文字，右侧逐字显示译文。页面顶部选源语言/目标语言，中间的 ⇄ 交换两者；翻口语、俚语或整段对话时勾上语言对右边的「意译」，按意思译而不是逐字直译。`Ctrl+Enter` 翻译，`Esc` 取消。截图后 `Ctrl+V` 或把图片拖进原文卡片，先识别文字再翻译。译文可以拖选一段再 `Ctrl+C`，右上角「复制」复制全文。
- **悬停取词**：顶栏的「悬停取词」开关对所有页面生效；把鼠标停在外文上约半秒，译文浮窗出现在鼠标旁，不抢焦点。浮窗右上角可以复制译文、打开主窗口或直接 `×` 收起；触发延迟可在设置里调。
- **历史**：左侧「历史」保留最近 120 条翻译（同一段文字重译只留最新一条），可以搜索、复制、放回翻译页重译或删除。
- **设置**：模型与运行（llama-server 路径、GGUF 路径、端口、闲置退出）、悬停取词（开关、触发延迟）、外观（主题、强调色、动效）和关于。模型相关修改在下次启动应用时生效，其余立即生效。

界面结构、状态与动效规则见 [docs/ui.md](docs/ui.md)。

## 架构

```
┌──────────────────────────── HoverTrans (Avalonia 12 + TKCode.Arc) ───────────────────────────┐
│  MainWindow → MainView (Arc 侧栏)            HoverWindow (topmost, non-activating)             │
│    翻译 / 历史 / 设置 三个页面                HoverViewModel                                   │
│    MainViewModel + AppState                       │                                           │
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

- `HoverTrans/Core/`：不依赖 UI 的语言检测、分段、提示词、模型进程管理、OpenAI 兼容客户端与翻译历史存储。
- `HoverTrans/Views/`、`HoverTrans/ViewModels/`：Arc 侧栏导航的三个页面（翻译 / 历史 / 设置）、应用级状态（模型、悬停）与悬停浮窗；页面首次显示时创建并保留，切页有淡入过渡。
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

# 打 Windows 安装包（Inno Setup 6：winget install --id JRSoftware.InnoSetup -e）
pwsh -File ./packaging/build-installer.ps1
```

需要 .NET 11 SDK。仓库根的 `global.json` 固定 `11.0.100`，并允许 11.0.x 的 feature band 向上滚动。

## 安装包

```powershell
winget install --id JRSoftware.InnoSetup -e   # 只装一次
pwsh -File ./packaging/build-installer.ps1    # -> artifacts\installer\HoverTrans-<版本>-win-x64-setup.exe
```

安装包按用户装到 `%LOCALAPPDATA%\Programs\HoverTrans`，不需要管理员；llama.cpp 运行时随包铺到
`%LOCALAPPDATA%\HoverTrans\runtime`；模型（1.1 GB）不进安装包，由安装最后一步调用
`packaging\deploy-model.ps1` 下载并校验到 `%LOCALAPPDATA%\HoverTrans\models`，装完即可翻译。
卸载保留模型和设置。细节、静默安装参数、冒烟测试清单见 [packaging/README.md](packaging/README.md)。

### 发布一个新版本

```powershell
git tag v0.2.0 && git push origin v0.2.0
```

打 tag 会触发 `.github/workflows/release.yml`：Windows 上打包出安装包、挂到 GitHub Release（直接发布，不是草稿）。
应用内的「检查更新」读的是 `releases/latest`，所以草稿版本它看不见——要让人升级，就得真的发出去。

构建时会用 `-p:Version` 把 tag 的数字版本写进应用（`v0.2.0` → `0.2.0`，`git describe` 的
`0.2.0-11-g778f5aa` 也只取 `0.2.0`），否则应用报的版本永远和 tag 对不上，更新检查就永远说「已是最新」。

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
