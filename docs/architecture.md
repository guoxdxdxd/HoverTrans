# 架构与关键流程

## 目标

桌面常驻的本地翻译工具：鼠标悬停取词即译，同时提供文本与图片输入。Windows 先做，macOS 复用同一套 Core 与 UI，仅替换平台层。

模型在**本进程内**推理（Sdcb.HyMT2Sharp + 内置的 Hy-MT3 权重），不启动外部服务、不占端口。权重在第一次翻译时加载，闲置到设定时长（默认 10 分钟，可设 0 = 常驻）后释放回操作系统，下次翻译再加载——释放的是内存，不是进程，所以没有端口、健康检查和进程树那一套。

## 分层

| 层 | 位置 | 职责 |
| --- | --- | --- |
| Core | `HoverTrans/Core` | 语言检测、分段、提示词、进程内推理引擎（`HunyuanEngine`）、翻译历史存储、版本比较与 GitHub 更新检查。不依赖 Avalonia。 |
| Services | `HoverTrans/Services` | `TranslationService` 编排；`HoverTranslationService` 轮询悬停；`HoverWindowManager` 显示浮窗。 |
| Platform | `HoverTrans/Platform` | `IOcrService`、`IHoverTextSource` 抽象；Windows 用 UIA3 + Windows.Media.Ocr；其他平台 `Null*`。 |
| UI | `HoverTrans/Views`、`ViewModels`、`Navigation` | Avalonia 12 + TKCode.Arc；侧栏导航的翻译 / 历史 / 设置三页、应用级状态、悬停浮窗。界面设计见 [ui.md](ui.md)。 |
| Tests | `HoverTrans.Tests` | 纯逻辑 + ViewModel（Avalonia headless）；真实模型的端到端用例默认跳过。 |

## 翻译流程

```
用户操作
  ├─ 悬停：HoverTranslationService 每 300 ms 调 IHoverTextSource.GetTextAtCursor()
  │     └─ 文本稳定 550 ms 且非中文 → TranslationService
  └─ 文本/图片：MainViewModel.TranslateCommand
        └─ 图片先 IOcrService.RecognizeAsync()，识别文本填入原文

TranslationService.TranslateAsync
  1. 模型未加载 → progress.StartingModel()，HunyuanEngine.EnsureLoadedAsync()
       读 6 个权重程序集（约 1.3 GB）→ 选后端 → 约 2–20 秒
  2. TextChunker 按 1800 字符切分
  3. 每段：TranslationPrompt.Build → HunyuanEngine.StreamAsync()
       分词 → 渲染 hunyuan-dense 对话模板 → prefill → 逐 token 采样
       每次 yield 的是「到目前为止的译文」，逐段回调 IProgress<TranslationProgress>
  4. 请求串行化：HunyuanDenseModel 不是线程安全的，_requestGate 一次只跑一个请求
  5. 生成在 Task.Run 的工作线程上，经 Channel 把文本送回调用方，UI 线程不参与计算
```

## 悬停取词

- Windows：`FlaUI.UIA3` 的 `UIA3Automation.FromPoint`，按 `Name → TextPattern → ValuePattern → LegacyIAccessible` 顺序取文本。
- 过滤：自身进程窗口（`WindowFromPoint` + 进程 ID）、密码字段（`IsPassword`）、空文本、过短/过长文本（> 2000 字符）。
- 去抖与重复抑制：文本变化后需稳定 550 ms；同一文本只翻一次，光标移开或换文字才重翻。
- 浮窗：`WindowDecorations=None`、`ShowActivated=False`、`Topmost`，定位在鼠标右下方并夹到工作区内；12 秒无更新自动隐藏。
- macOS（待实现）：`AXUIElementCopyElementAtPosition` + `AXValue/AXTitle`，需要「辅助功能」授权；OCR 换 `VNRecognizeTextRequest`。

## 模型引擎

`HunyuanEngine` 是一个状态机：`NotLoaded → Loading → Ready`，失败进入 `Failed`（`LastError` 说明原因）。

- 加载：`Task.Run` 里读 `Weights.Model()`（6 个程序集拼成一个流）→ `BackendFactory.Create(backend)` → `new HunyuanDenseModel(...)`。
- 后端回退：`vulkan` 可能枚举出设备却在加载权重时失败（驱动没有 host-visible 堆）；此时丢掉后端、用 CPU 重新加载，并在日志里记下原因。状态行显示实际在用的后端。
- 生成：`Sampler`（temperature 0.1 / top-p 0.6）逐 token 采样，`IsStop` 收尾；`VisibleText` 把跨 token 被切开的 UTF-8 字符挡住，不闪乱码。
- 缓存：`KvCacheConfig.Memory`，KV 缓冲常驻（约 250 MB），请求之间用 `EndRequest()` 复位，下一次翻译不必重新分配。
- 闲置释放：后台循环每「超时 ÷ 4」（1–30 秒）看一次，满足「无在飞请求且空闲 ≥ 超时」才 `Dispose()` 掉模型。
  每个请求在 `_activeRequests` 上持一个租约，从「加载」到「拿到请求闸门」这段窗口也计入，所以释放不会把模型从请求脚下抽走；
  释放后调 `SetProcessWorkingSetSize(-1,-1)`，让任务管理器里的数字当场回落，而不是等 Windows 需要内存。
- 内存：加载后工作集约 4.4 GB，其中私有内存约 3.3 GB（Q4_K_M 权重 + panel 重排副本 + KV 缓存）才是真正吃掉的 RAM；
  释放后私有内存回落到约 100 MB（实测 3268 MB → 100 MB）。整体仍比 llama.cpp 方案高，换来的是没有外部进程、没有端口、没有下载与部署步骤。
- 重新加载：权重程序集留在磁盘上，页缓存还热时约 1.7 秒，冷启动约 11 秒。
- 日志：`%LOCALAPPDATA%\HoverTrans\logs\engine.log`，只记加载耗时、后端、回退、释放与失败原因，不含原文/译文。

## 检查更新

- GitHubUpdateChecker 读 `GET /repos/guoxdxdxd/HoverTrans/releases/latest`（未认证；每小时 60 次/IP 足够点按），
  用 `SemanticVersion` 与 `AppVersion.Current` 比较：**草稿和预发布不提示**，tag 解析不出来算检查失败而不是新版本。
- 失败分三类回给界面：连不上（Network）、限流（RateLimited）、GitHub 返回异常或内容看不懂（HttpError / BadResponse）；
  只有用户主动点「检查更新」才发请求，启动时不联网。
- 应用版本来自 `<Version>`（`packaging/build-installer.ps1` 用 tag 的数字部分注入，见 [../packaging/README.md](../packaging/README.md)），
  所以「已是最新」有具体版本号可比。

## 线程模型

- UI 线程：Avalonia 消息循环；`MainViewModel` 的 `Progress<T>` 回到 UI 线程更新结果。
- 推理：`Task.Run` 内的生成循环 + 有界 `Channel` 回传文本；取消时 `CancellationTokenSource` 一起取消，生产者立刻停下。
- 悬停轮询：`Task.Run` 后台循环；`HoverWindowManager` 用 `Dispatcher.UIThread.Post` 切回 UI。
- UIA 调用可能阻塞，全部包在 try/catch；进程退出或控件消失时返回 null，不抛出。

## 失败与边界

- 模型加载失败：状态行显示原因，`engine.log` 有细节；应用本身照常可用（设置、历史）。
- 显卡后端不可用：自动回退 CPU，不打断翻译。
- 提示词超过 4096 token：抛出可读错误，由 `TextChunker` 分段保证正常路径不会碰到。
- 无 OCR 语言包：`WindowsOcrService` 抛出可读错误，建议安装语言组件。
- 悬停读不到文本：静默跳过，不打扰用户；连续出错时浮窗显示一次错误并等待 1.5 秒。
