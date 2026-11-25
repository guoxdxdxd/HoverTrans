# 打包与安装

Windows 安装包用 **Inno Setup 6**：按用户安装、不需要管理员。模型权重（约 1.35 GB）在应用本体里，
安装包约 1.3 GB，没有安装时的下载步骤。

```
packaging/
├── build-installer.ps1          # 一条命令出安装包：publish → ISCC
├── installer.iss                # Inno Setup 脚本（安装界面、快捷方式、卸载项）
├── THIRD-PARTY-NOTICES.txt      # 随包安装的第三方清单（MIT / Apache-2.0）
└── installer.generated.iss      # 构建时生成的版本与路径（不提交）
```

## 一条命令

```powershell
winget install --id JRSoftware.InnoSetup -e   # 只需一次
pwsh -File .\packaging\build-installer.ps1
```

产物：`artifacts\installer\HoverTrans-<version>-win-x64-setup.exe`（约 1.3 GB）。

常用参数：

| 参数 | 用途 |
| --- | --- |
| `-Version 0.2.0` | 指定版本号（默认取 `git describe --tags --always`） |
| `-SkipPublish` | 复用 `artifacts\win-x64`，只重打安装包 |
| `-Iscc <路径>` | 指定 `ISCC.exe`（默认查 PATH 和常见安装位置） |

构建耗时参考（8 核开发机）：publish 约 40 秒（1.59 GB 输出），ISCC 约 87 秒。

## 安装时实际发生了什么

1. 应用本体解到 `%LOCALAPPDATA%\Programs\HoverTrans`：`HoverTrans.exe`、.NET 运行时、以及 6 个
   Hy-MT3 权重程序集（共约 1.35 GB）。
2. 桌面/开始菜单快捷方式（桌面图标默认不勾）。
3. 勾了「立即运行」则启动应用。

第一次翻译时引擎读权重（2–20 秒），之后一直留在内存里。

## 为什么模型进了安装包（判断，别改回去）

上一版是「安装包 67 MB + 安装时下载 1.1 GB 模型」，理由是体积、GitHub Release 限制和许可证。
现在这条判断被推翻，原因是**推理改到了进程内**（[ADR 0002](../docs/adr/0002-in-process-inference.md)）：

- 进程内推理没有「模型文件路径」这个概念，权重就是应用的 6 个程序集；要外置就得自己实现
  「从外部 GGUF 加载」的分支，等于把刚删掉的那套部署逻辑再造一遍。
- 「装完就能用」比「安装包小」更重要：下载失败以前会让应用装上了却翻不了。
- 代价是明确的、已测量的：安装包 1.3 GB（近 GitHub 单文件 2 GB 上限），发布前换更大权重要先算体积。

因此 Inno 的压缩用 `lzma2/fast` 且不做 solid compression：GGUF 权重几乎不可压缩，`lzma2/max`
要慢十几倍却只省几个百分点。发布也**不开** `PublishSingleFile`——把 1.3 GB 程序集打进单文件，
只是让首次启动再把它们解出来。

## 静默安装与企业分发

```powershell
# Inno 的 setup.exe 是 GUI 子系统程序：PowerShell 里用 & 调用不会等它结束，要 -Wait。
$p = Start-Process .\HoverTrans-0.1.0-win-x64-setup.exe -Wait -PassThru `
    -ArgumentList '/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART','/LOG="$env:TEMP\hovertrans-install.log"'
$p.ExitCode
```

装完后应用就在 `%LOCALAPPDATA%\Programs\HoverTrans\HoverTrans.exe`，模型已随包，无需再联网。
每台机器的部署成本就是 1.3 GB 的安装包本身。

## 卸载

- 删除安装目录、开始菜单项、快捷方式（权重在安装目录里，一并删除）；
- **保留** `%LOCALAPPDATA%\HoverTrans\logs`（日志）、`%APPDATA%\HoverTrans\settings.json`（设置）与历史记录。

从 llama.cpp 版本升级上来的人，`%LOCALAPPDATA%\HoverTrans` 下的 `models`（约 1.1 GB）和 `runtime`
（约 63 MB）已经没人用了——新版本不读它们，也不自动删（那是用户磁盘上的文件）。想回收空间就手动删掉这两个目录；
`settings.json` 里遗留的 `ServerPath`/`ModelPath`/`Port`/`IdleMinutes` 会被忽略，下次保存时自然消失。

## 中文安装界面

Inno Setup 官方不带简体中文语言文件。把 `ChineseSimplified.isl`（Inno Setup 6 的第三方翻译，覆盖到 6.x）放到 `packaging\ChineseSimplified.isl`，`build-installer.ps1` 会自动发现并把它设为默认语言；没有该文件时界面是英文，功能不受影响。

## 许可提醒

Inno Setup 6.5 起对**商业使用**收费（编译器会打印 `Non-commercial use only`）。本项目以 MIT 开源、
非商业分发，用免费版即可；若要商业分发，需先购买 Inno Setup 许可，或换 WiX/NSIS。

## 冒烟测试清单（干净 Windows 10 1809+ / 11 虚拟机）

开发机上已实测：安装包能打出来（1.33 GB）、静默安装 exit 0、装出来的 `HoverTrans.exe` 能启动。
需要在干净虚拟机上补的：真实加载与首译、覆盖安装、卸载。

1. 双击安装包 → 目录是 `%LOCALAPPDATA%\Programs\HoverTrans`，全程没弹 UAC。
2. 装完自动启动，顶栏模型胶囊显示「模型按需加载」；翻一句英文，首次 2–20 秒（加载权重），之后 0.5 秒内出结果。
3. 悬停外文 → 浮窗出译文；`%LOCALAPPDATA%\HoverTrans\logs\engine.log` 里有 `model ready in …s, backend=…`。
4. 关窗 → 进程退出，内存归还（没有残留的模型进程，因为根本没有子进程）。
5. 再装一次 → 复用同一目录，覆盖升级。
6. 卸载 → 程序目录没了，`settings.json`、历史和日志还在。

## 还没做的（别当成已完成）

- **代码签名**：没有证书，SmartScreen 首次运行会报「未知发布者」，需要「更多信息 → 仍要运行」。要消掉得买代码签名证书（或至少用 Azure Trusted Signing），属发布阶段（w8）。
- **应用图标**：仓库里还没有 `.ico`，安装包和 exe 都用默认图标；补 `HoverTrans.ico` 后加 `SetupIconFile` 与 `<ApplicationIcon>`。
- **托盘常驻与开机自启**：应用当前没有托盘图标，关窗即退出；所以安装包故意不做「开机自启」任务。
- **macOS**：第二阶段，计划 `.pkg`（pkgbuild/productbuild）或 `.dmg`，权重同样随包（NuGet 的 6 个程序集跨平台）。

## 为什么不用别的

- **MSIX**：安装体验最好，但自包含 .NET + Avalonia 的 MSIX 要么要求签名证书，要么走商店；1.3 GB 的包在商店里也不现实。
- **NSIS**：能做，但 Inno 的向导、任务页和 `{autopf}` 按用户安装更省事，社区对 Avalonia 应用的 Inno 模板也更多。
- **自解压 ZIP**：最简单，但没有卸载项、没有快捷方式、杀毒误报更多；可以作为「便携版」补充，不适合主安装包。
