# 参与开发

## 环境

- .NET 11 SDK
- Windows 10 1809+（悬停取词与 OCR 只在 Windows 构建中启用）
- 可选：支持 Vulkan 的显卡（不装也能跑，CPU 内核自带 AVX2/AVX-512/NEON）

## 本地构建

```powershell
dotnet build HoverTrans.slnx
dotnet test HoverTrans.slnx
dotnet run --project HoverTrans -f net11.0-windows10.0.19041.0
```

模型权重来自 NuGet 包（`Sdcb.GgufWeights.Hy-MT3-1.8B-Q4_K_M` 等 6 个包，共约 1.3 GB），`restore`
时自动还原并随构建复制到输出目录；不提交到仓库，也不需要手工下载或安装运行时。

跑真实模型的端到端用例（默认跳过，因为要加载 1.3 GB 权重、约 4.4 GB 内存）：

```powershell
$env:HOVERTRANS_SLOW_TESTS = 1
dotnet test HoverTrans.Tests --filter FullyQualifiedName~The_model_translates
```

## 代码约定

- 平台相关代码放在 `Platform/Windows`，用 `#if WINDOWS` 包裹；Core 不引用 Avalonia。
- 所有阻塞调用必须可取消，UI 线程不做 I/O；推理在 `Task.Run` 里跑，文本经 `Channel` 回传。
- UI 文案用中文；注释解释「为什么」，不复述代码。
- 新增语言：`LanguageCatalog.All` 加一行，`LanguageDetector` 如需识别新文字系统再加分支。
- 新增平台：实现 `IHoverTextSource`、`IOcrService`，在 `AppServices` 的 `#if` 分支注册。
- 换模型或量化：改 `HunyuanEngine` 里的权重包 using 与 `HoverTrans.csproj` 的包引用，并在
  `docs/model.md` 记下实测的加载时间、内存与解码速度。

## 提交

- 提交信息说明「做了什么 + 为什么」，不要只写「update」。
- 涉及 UI 的改动附截图或录屏。
- 不在 PR 中提交 `bin/`、`obj/`、`artifacts/` 与模型权重。
