# 架构与关键流程

## 目标

桌面常驻的本地翻译工具：鼠标悬停取词即译，同时提供文本与图片输入；模型按需启动，闲置 10 分钟退出。Windows 先做，macOS 复用同一套 Core 与 UI，仅替换平台层。

## 分层

| 层 | 位置 | 职责 |
| --- | --- | --- |
| Core | `HoverTrans/Core` | 语言检测、分段、提示词、llama-server 生命周期、OpenAI 兼容客户端、翻译历史存储。不依赖 Avalonia。 |
| Services | `HoverTrans/Services` | `TranslationService` 编排；`HoverTranslationService` 轮询悬停；`HoverWindowManager` 显示浮窗。 |
| Platform | `HoverTrans/Platform` | `IOcrService`、`IHoverTextSource` 抽象；Windows 用 UIA3 + Windows.Media.Ocr；其他平台 `Null*`。 |
| UI | `HoverTrans/Views`、`ViewModels`、`Navigation` | Avalonia 12 + TKCode.Arc；侧栏导航的翻译 / 历史 / 设置三页、应用级状态、悬停浮窗。界面设计见 [ui.md](ui.md)。 |
| Tests | `HoverTrans.Tests` | 纯逻辑 + ViewModel（Avalonia headless）。 |

## 翻译流程

```
用户操作
  ├─ 悬停：HoverTranslationService 每 300 ms 调 IHoverTextSource.GetTextAtCursor()
  │     └─ 文本稳定 550 ms 且非中文 → TranslationService
  └─ 文本/图片：MainViewModel.TranslateCommand
        └─ 图片先 IOcrService.RecognizeAsync()，识别文本填入原文

TranslationService.TranslateAsync
  1. 若模型未运行 → LlamaServerManager.EnsureStartedAsync()
       启动 llama-server → 轮询 /health（最长 120 s）
  2. TextChunker 按 1800 字符切分
  3. 每段：TranslationPrompt.Build → POST /v1/chat/completions (stream)
       解析 SSE，逐段回调 IProgress<TranslationProgress>
  4. 请求期间 EnterRequest() 阻止闲置计时器误杀
  5. 请求结束 Touch()；连续 10 分钟无请求 → StopCoreAsync()
```

## 悬停取词

- Windows：`FlaUI.UIA3` 的 `UIA3Automation.FromPoint`，按 `Name → TextPattern → ValuePattern → LegacyIAccessible` 顺序取文本。
- 过滤：自身进程窗口（`WindowFromPoint` + 进程 ID）、密码字段（`IsPassword`）、空文本、过短/过长文本（> 2000 字符）。
- 去抖与重复抑制：文本变化后需稳定 550 ms；同一文本 30 秒内不重复翻译。
- 浮窗：`WindowDecorations=None`、`ShowActivated=False`、`Topmost`，定位在鼠标右下方并夹到工作区内；12 秒无更新自动隐藏。
- macOS（待实现）：`AXUIElementCopyElementAtPosition` + `AXValue/AXTitle`，需要「辅助功能」授权；OCR 换 `VNRecognizeTextRequest`。

## 模型生命周期

`LlamaServerManager` 是一个状态机：`Stopped → Starting → Running → Stopping → Stopped`，失败进入 `Failed`。

- 启动：校验 exe/模型存在 → `ProcessStartInfo.ArgumentList`（避免拼接转义）→ 等待 `/health` 返回 2xx。
- 空闲退出：后台每 5 秒检查 `_activeRequests == 0 && now - lastActivity >= IdleTimeout`，满足才停。
- 停止：`Kill(entireProcessTree: true)`，最多等 10 秒；下次翻译重新启动。
- 日志：`%LOCALAPPDATA%\HoverTrans\logs\llama-server.log`，只记录进程输出。

## 线程模型

- UI 线程：Avalonia 消息循环；`MainViewModel` 的 `Progress<T>` 回到 UI 线程更新结果。
- 悬停轮询：`Task.Run` 后台循环；`HoverWindowManager` 用 `Dispatcher.UIThread.Post` 切回 UI。
- UIA 调用可能阻塞，全部包在 try/catch；进程退出或控件消失时返回 null，不抛出。

## 失败与边界

- 模型文件缺失：抛出带下载指引的 `FileNotFoundException`，UI 状态栏显示。
- 端口占用：llama-server 启动失败或健康检查超时，状态栏显示日志路径；用户可在设置改端口后重启。
- 无 OCR 语言包：`WindowsOcrService` 抛出可读错误，建议安装语言组件。
- 悬停读不到文本：静默跳过，不打扰用户；连续出错时浮窗显示一次错误并等待 1.5 秒。
