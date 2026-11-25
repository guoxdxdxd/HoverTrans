# 悬译 HoverTrans

Windows 优先、兼容 macOS 的本地 AI 翻译工具：鼠标悬停在外文上自动取词并译为中文，也支持输入文字、粘贴/拖入图片翻译。模型是腾讯 **Hy-MT3 1.8B（Q4_K_M GGUF）**，由 **Sdcb.HyMT2Sharp** 在**本进程内**推理——不依赖 llama.cpp、不启动外部服务、**不上传云端**。

> 当前状态：Windows MVP 可编译、可运行、测试通过；悬停取词、文本/图片翻译、直译/意译、进程内推理已接通。macOS 的 Accessibility/Vision 适配在路线图中（接口已留好，推理库本身跨平台）。

## 功能

- **悬停即译**：UI Automation 读取鼠标下控件的文本；识别为非中文且稳定约 0.5 秒后，自动翻译为中文，并在鼠标旁弹出结果。
- **文本翻译**：输入或粘贴任意文本，自动检测源语言，也可手动指定源语言/目标语言（中文简繁、英、日、韩、法、德、西、俄等 20+ 种）。
- **直译 / 意译**：语言对右侧勾选「意译」后按意思翻——口语化、俚语和习语换成地道说法；不勾选按原文字面直译。选择会被记住，悬停浮窗始终直译。
- **图片翻译**：`Ctrl+V` 粘贴剪贴板图片，或选择图片文件；Windows 用系统 OCR 识别文字后再翻译。
- **进程内推理**：模型权重随应用分发，第一次翻译时加载（约 2–20 秒）；闲置 10 分钟（0–120 分钟可调，0 = 常驻）后把内存还给系统，下次翻译再加载（约 2 秒）。没有外部进程、端口和 API key，也没有下载/部署步骤。
- **流式结果**：译文边生成边显示，可取消、可复制。
- **主题**：跟随系统的明暗主题，Arc 组件库的动效与设计令牌。
- **检查更新**：设置页「检查更新」直接读 GitHub 最新 release，比当前版本新就提示，并可一键打开下载页（[`guoxdxdxd/HoverTrans`](https://github.com/guoxdxdxd/HoverTrans)）。

## 快速开始（Windows）

### 1. 环境

- Windows 10 1809+ 或 Windows 11
- [.NET 11 SDK](https://dotnet.microsoft.com/download/dotnet/11.0)
- 约 3 GB 磁盘空间（应用 + 模型权重约 1.4 GB，NuGet 缓存另计）与约 4.5 GB 可用内存

### 2. 运行

```powershell
dotnet run --project HoverTrans -f net11.0-windows10.0.19041.0
```

第一次 `restore` 会从 NuGet 拉 `Sdcb.GgufWeights.Hy-MT3-1.8B-Q4_K_M`（含 Part2–Part6，共约 1.3 GB，慢的话要几分钟），之后都在本地缓存里。不需要下载模型文件，也没有运行时脚本。

> **别跑错目标框架**：`net11.0` 是 macOS/Linux 目标，在 Windows 上也能启动，但没有 UI Automation 取词和系统 OCR——顶栏「悬停取词」开关是灰的、点不动，设置页会说明原因。Windows 上统一用上面的 `-f net11.0-windows10.0.19041.0`（Visual Studio 里同样把启动的目标框架切到它）。

### 3. 使用

- **翻译**：在左侧输入或粘贴文字，右侧逐字显示译文。页面顶部选源语言/目标语言，中间的 ⇄ 交换两者；翻口语、俚语或整段对话时勾上语言对右边的「意译」，按意思译而不是逐字直译。`Ctrl+Enter` 翻译，`Esc` 取消。截图后 `Ctrl+V` 或把图片拖进原文卡片，先识别文字再翻译。译文可以拖选一段再 `Ctrl+C`，右上角「复制」复制全文。
- **悬停取词**：顶栏的「悬停取词」开关对所有页面生效；把鼠标停在外文上约半秒，译文浮窗出现在鼠标旁，不抢焦点。浮窗右上角可以复制译文、打开主窗口或直接 `×` 收起；触发延迟可在设置里调。
- **历史**：左侧「历史」保留最近 120 条翻译（同一段文字重译只留最新一条），可以搜索、复制、放回翻译页重译或删除。
- **设置**：本地模型（推理后端：自动 / 仅 CPU / 显卡；闲置释放内存 0–120 分钟（0 = 常驻）；立即释放）、悬停取词（开关、触发延迟）、外观（主题、强调色、动效）和关于。后端与闲置释放时长在下次启动应用时生效，其余立即生效。

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
│  TranslationService ──► HunyuanEngine (in-process, Sdcb.HyMT2Sharp)                            │
│      │                    │  lazy load of the embedded Hy-MT3 weights, one request at a time  │
│      │                    └── sampler → tokens → text so far (Channel, worker thread)          │
│      └── TextChunker + TranslationPrompt                                                       │
│                                                                                               │
│  Windows: WindowsHoverTextSource (FlaUI/UIA3)   WindowsOcrService (Windows.Media.Ocr)         │
│  macOS:   planned AXUIElement + Vision                                                        │
└───────────────────────────────────────────────────────────────────────────────────────────────┘
```

- `HoverTrans/Core/`：不依赖 UI 的语言检测、分段、提示词、进程内推理引擎与翻译历史存储。
- `HoverTrans/Views/`、`HoverTrans/ViewModels/`：Arc 侧栏导航的三个页面（翻译 / 历史 / 设置）、应用级状态（模型、悬停）与悬停浮窗；页面首次显示时创建并保留，切页有淡入过渡。
- `HoverTrans/Platform/Windows/`：Windows 专属的 UIA 取词和 OCR；`#if WINDOWS` 条件编译，macOS/Linux 使用 `Null*` 实现。
- `HoverTrans/Services/`：翻译编排、悬停轮询、浮窗管理。
- `HoverTrans.Tests/`：核心逻辑与 ViewModel 的 headless 测试。

更多细节见 [docs/architecture.md](docs/architecture.md)、[docs/model.md](docs/model.md) 和 [docs/adr/0001-stack.md](docs/adr/0001-stack.md)、[docs/adr/0002-in-process-inference.md](docs/adr/0002-in-process-inference.md)。

## 为什么是这些选择

- **.NET 11 + Avalonia 12**：一套代码跑 Windows/macOS，原生窗口与托盘能力完整；.NET 11 是当前 LTS 线。
- **TKCode.Arc**：[Arc 设计语言的原生 Avalonia 控件](https://github.com/AIDotNet/TKCode.Arc)，MIT，支持 `net11.0`、暗色/强调色/动效，UI 不用从零搭。
- **Hy-MT3 1.8B Q4_K_M**：约 1.35 GB、CPU 可跑，翻译任务专用；比通用 7B 模型更适合常驻桌面。
- **Sdcb.HyMT2Sharp**：纯 C# 的 hunyuan-dense 推理库（Apache-2.0），权重直接来自 NuGet，进程内调用，没有子进程与端口；CPU 用 AVX2/AVX-512/NEON，能用显卡时走 Vulkan/Metal。代价是内存更高（约 4.4 GB），详见 [docs/model.md](docs/model.md)。

## 开发

```powershell
dotnet build HoverTrans.slnx
dotnet test HoverTrans.slnx

# 只构建跨平台（macOS/Linux）目标
dotnet build HoverTrans/HoverTrans.csproj -f net11.0

# Windows 自包含发布（约 1.4 GB，含模型权重）
dotnet publish HoverTrans/HoverTrans.csproj -f net11.0-windows10.0.19041.0 -c Release -r win-x64 --self-contained true

# 打 Windows 安装包（Inno Setup 6：winget install --id JRSoftware.InnoSetup -e）
pwsh -File ./packaging/build-installer.ps1
```

需要 .NET 11 SDK。仓库根的 `global.json` 固定 `11.0.100`，并允许 11.0.x 的 feature band 向上滚动。

真实模型的端到端用例默认跳过（要加载 1.3 GB 权重）：

```powershell
$env:HOVERTRANS_SLOW_TESTS = 1
dotnet test HoverTrans.Tests --filter FullyQualifiedName~The_model_translates
```

## 安装包

```powershell
winget install --id JRSoftware.InnoSetup -e   # 只装一次
pwsh -File ./packaging/build-installer.ps1    # -> artifacts\installer\HoverTrans-<版本>-win-x64-setup.exe
```

安装包按用户装到 `%LOCALAPPDATA%\Programs\HoverTrans`，不需要管理员。因为模型在进程内推理，
权重（约 1.35 GB 的 6 个程序集）就在应用本体里，安装包约 1.3 GB——没有安装时的下载步骤，也
不会因为联网失败装出一个不能翻译的应用。细节、静默安装参数、冒烟测试清单见 [packaging/README.md](packaging/README.md)。

### 发布一个新版本

两种方式，产物一样（安装包 + GitHub Release，Release 说明自动取「自上个版本以来的提交」）：

```powershell
git tag v0.2.0 && git push origin v0.2.0      # A：推 tag，版本号就是 tag
```

**B：去 Actions → release → Run workflow**，填版本号（`0.2.0` 或 `v0.2.0` 都行），
留空则取最新 tag 的 patch +1；勾 `dry_run` 只构建不发布，产物在本次运行的 Artifacts 里。
两条路都会：按版本号构建安装包 → 建 tag（手动运行时建在当前提交上）→ 发布 Release 并附上 exe。

失败会明确报出来：版本号格式不对、这个版本的 release 已经存在、或者 tag 已存在但指向别的提交。

构建时会用 `-p:Version` 把 tag 的数字版本写进应用（`v0.2.0` → `0.2.0`，`git describe` 的
`0.2.0-11-g778f5aa` 也只取 `0.2.0`），否则应用报的版本永远和 tag 对不上，更新检查就永远说「已是最新」。
手动运行建出来的 tag 只存在于远端，本地记得 `git fetch --tags`。

> 安装包 1.3 GB 已经接近 GitHub Release 单文件 2 GB 的上限；再换更大的权重（比如 Q8_0 或 MT2 的高精度量化）之前先算一下。

## 已知限制

- **悬停取词的覆盖率取决于目标应用的 UI Automation 实现**。浏览器、Office、WinUI/WPF/WinForms、多数 Electron 应用可用；游戏、自绘控件、部分 Java 应用可能读不到文本。
- 悬停浮窗是普通置顶窗口，不做鼠标穿透；浮窗出现后鼠标移开即恢复轮询。
- Windows OCR 需要系统安装对应语言的 OCR 组件（中文/英文通常已装）；装不上时会在状态栏提示。
- 图片翻译暂不保留版面，只输出识别到的纯文本。
- macOS 端目前只有接口和构建目标，Accessibility/Vision 实现待补；推理引擎已在 macOS 目标下编译通过（Metal 后端待真机验证）。
- **内存与加载**：首次加载 2–20 秒（取决于磁盘与是否已在页缓存），加载后工作集约 4.4 GB、私有内存约 3.3 GB；闲置 10 分钟（可设 0 = 常驻）不翻译就释放，私有内存回落到约 100 MB，再翻译重新加载约 2 秒。
- **显卡后端**：Vulkan 在部分驱动上能在探测阶段通过、却在加载权重时失败（`no suitable memory type`）。引擎会自动回退 CPU 并在 `engine.log` 记录原因，设置页状态行显示实际后端。

## 隐私

- 翻译完全在本进程内完成：没有本地 HTTP 服务，没有任何网络请求参与翻译。
- 没有遥测、没有账号、没有云端 API。
- 日志只写 `%LOCALAPPDATA%\HoverTrans\logs\engine.log`（加载耗时、后端、回退与失败原因），不记录原文/译文。
- 设置保存在 `%APPDATA%\HoverTrans\settings.json`。

## 许可

本项目代码以 [MIT](LICENSE) 许可开源。

第三方组件：

- [Sdcb.HyMT2Sharp](https://github.com/sdcb/HyMT2Sharp) — Apache-2.0
- [Hy-MT3 1.8B](https://huggingface.co/tencent/Hy-MT3-1.8B-GGUF) — Apache-2.0（权重以 NuGet 包随应用分发）
- [Avalonia](https://avaloniaui.net/) — MIT
- [TKCode.Arc](https://github.com/AIDotNet/TKCode.Arc) — MIT
- [FlaUI](https://github.com/FlaUI/FlaUI) — MIT

完整清单见 [packaging/THIRD-PARTY-NOTICES.txt](packaging/THIRD-PARTY-NOTICES.txt)。

---

## English

**HoverTrans** is a local-first AI translator for Windows (macOS planned). Hover over foreign text and it reads the control through UI Automation, detects non-Chinese text, and translates it to Chinese with Tencent **Hy-MT3 1.8B Q4_K_M** running **in-process** through [Sdcb.HyMT2Sharp](https://github.com/sdcb/HyMT2Sharp) — no llama.cpp, no local server, no cloud. Text input and clipboard/file images are supported too.

The weights ship as NuGet packages and load on the first translation. After ten idle minutes (configurable, 0 keeps them loaded) the model releases its memory again — about 3.3 GB of private bytes go back to the OS — and the next translation reloads it in ~2 seconds.

```powershell
dotnet run --project HoverTrans -f net11.0-windows10.0.19041.0
```

MIT licensed. Third-party components and their licences are listed in [packaging/THIRD-PARTY-NOTICES.txt](packaging/THIRD-PARTY-NOTICES.txt).
