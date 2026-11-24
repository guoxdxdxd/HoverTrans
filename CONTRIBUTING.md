# 参与开发

## 环境

- .NET 11 SDK
- Windows 10 1809+（悬停取词与 OCR 只在 Windows 构建中启用）
- 可选：NVIDIA GPU + CUDA 版 llama.cpp

## 本地构建

```powershell
dotnet build HoverTrans.slnx
dotnet test HoverTrans.slnx
dotnet run --project HoverTrans -f net11.0-windows10.0.19041.0
```

模型与运行时用脚本下载，不提交到仓库：

```powershell
powershell -ExecutionPolicy Bypass -File ./scripts/setup-model.ps1
```

## 代码约定

- 平台相关代码放在 `Platform/Windows`，用 `#if WINDOWS` 包裹；Core 不引用 Avalonia。
- 所有阻塞调用必须可取消，UI 线程不做 I/O。
- UI 文案用中文；注释解释「为什么」，不复述代码。
- 新增语言：`LanguageCatalog.All` 加一行，`LanguageDetector` 如需识别新文字系统再加分支。
- 新增平台：实现 `IHoverTextSource`、`IOcrService`，在 `AppServices` 的 `#if` 分支注册。

## 提交

- 提交信息说明「做了什么 + 为什么」，不要只写「update」。
- 涉及 UI 的改动附截图或录屏。
- 不在 PR 中提交 `bin/`、`obj/`、模型、llama.cpp 二进制。
