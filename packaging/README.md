# 打包与安装

Windows 安装包用 **Inno Setup 6**：按用户安装、不需要管理员，安装最后一步自动部署本地模型。

```
packaging/
├── build-installer.ps1     # 一条命令出安装包：publish → 取 llama.cpp → ISCC
├── installer.iss           # Inno Setup 脚本（安装界面、快捷方式、模型自动部署）
├── deploy-model.ps1        # 下载 + 校验运行时与模型，安装时调用，之后也能单独重跑
└── model-manifest.json     # 模型大小/SHA256/镜像、llama.cpp 版本
```

## 一条命令

```powershell
winget install --id JRSoftware.InnoSetup -e   # 只需一次
pwsh -File .\packaging\build-installer.ps1
```

产物：`artifacts\installer\HoverTrans-<version>-win-x64-setup.exe`（约 67 MB，含 llama.cpp CPU 运行时）。

构建机会去 GitHub Releases 下 llama.cpp 的 zip；国内网络经常连不上或断流（实测 `curl: (28) Failed to connect to github.com:443`）。这时用 `-RuntimeSource "$env:LOCALAPPDATA\HoverTrans\runtime"` 复用已装好的运行时，或 `-SkipRuntime` 让用户在安装时下载。

常用参数：

| 参数 | 用途 |
| --- | --- |
| `-Version 0.2.0` | 指定版本号（默认取 `git describe --tags --always`） |
| `-RuntimeVariant cuda` | 改带 NVIDIA 版 llama.cpp；默认 `cpu`（哪台机器都能跑） |
| `-RuntimeSource <目录>` | 复用本机已有的 `llama-server.exe` 目录，不联网 |
| `-SkipRuntime` | 不内置运行时，安装后由 `deploy-model.ps1` 一起下载 |
| `-SkipPublish` | 复用 `artifacts\win-x64`，只重打安装包 |
| `-Iscc <路径>` | 指定 `ISCC.exe`（默认查 PATH 和常见安装位置） |

## 安装时实际发生了什么

1. 应用本体解到 `%LOCALAPPDATA%\Programs\HoverTrans`（`HoverTrans.exe`，自包含单文件）。
2. llama.cpp 运行时解到 `%LOCALAPPDATA%\HoverTrans\runtime` —— 正是 `AppPaths.DefaultServerPath`，装完可直接用。
3. 桌面/开始菜单快捷方式（桌面图标默认不勾）。
4. 勾选了「下载本地翻译模型」时，调用 `deploy-model.ps1`：从 ModelScope 下 `Hy-MT2-1.8B-Q4_K_M.gguf`（失败换 Hugging Face），断点续传，落到 `%LOCALAPPDATA%\HoverTrans\models`；已存在完整模型则直接跳过。窗口可见、有进度条。
5. 勾了「立即运行」则启动应用。

模型目录是模型自己下载的，所以 **未必需要网络**：先把 `%LOCALAPPDATA%\HoverTrans\models\Hy-MT2-1.8B-Q4_K_M.gguf` 准备好，安装时那一步会秒过。

为什么模型不塞进安装包（判断，别改回去）：

- 安装包从 50 MB 涨到 1.2 GB，GitHub Release 单文件上限 2 GB 但用户体验差，且模型更新就要重发整包；
- 模型是 Apache-2.0 的第三方权重，`docs/model.md` 已经定了「让用户自行下载模型」，随包分发要另做许可与来源说明；
- 下载失败不该让安装失败：脚本失败只提示「联网后重跑一次」，应用照常装上。

## 静默安装与企业分发

```powershell
# Inno 的 setup.exe 是 GUI 子系统程序：PowerShell 里用 & 调用不会等它结束，要 -Wait。
$p = Start-Process .\HoverTrans-0.1.0-win-x64-setup.exe -Wait -PassThru `
    -ArgumentList '/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART','/LOG="$env:TEMP\hovertrans-install.log"'
$p.ExitCode
```

静默安装 **不会** 下载模型（`skipifsilent`）。需要预置时在装完后自己跑：

```powershell
# 每台机器一次；-SkipModel 只补运行时
powershell -NoProfile -ExecutionPolicy Bypass -File "%LOCALAPPDATA%\Programs\HoverTrans\deploy-model.ps1"
```

## 卸载

- 删除安装目录、开始菜单项、`%LOCALAPPDATA%\HoverTrans\runtime`；
- **保留** `%LOCALAPPDATA%\HoverTrans\models`（1.1 GB，重下太贵）与 `%APPDATA%\HoverTrans\settings.json`（用户设置），日志也留着以便排查。

## 中文安装界面

Inno Setup 官方不带简体中文语言文件。把 `ChineseSimplified.isl`（Inno Setup 6 的第三方翻译，覆盖到 6.x）放到 `packaging\ChineseSimplified.isl`，`build-installer.ps1` 会自动发现并把它设为默认语言；没有该文件时界面是英文，功能不受影响。

## 冒烟测试清单（干净 Windows 10 1809+ / 11 虚拟机）

已在开发机验证过：1（静默装到临时目录，exit 0，文件、快捷方式、卸载信息都对）、2 的跳过分支（模型已存在时秒过）、3（装出来的 `HoverTrans.exe` 能启动、窗口正常绘制）。

还**没**在这台机器上验证，需要在干净虚拟机上补：2 的真实下载（1.1 GB）、4 的悬停与闲置退出回归、5 的覆盖安装、6 的卸载。卸载器的 `[UninstallDelete]` 与保留策略是我照 Inno 语义写的，没有实跑过。

1. 双击安装包 → 目录是 `%LOCALAPPDATA%\Programs\HoverTrans`，全程没弹 UAC。
2. 勾选下载模型 → 进度条走完，`%LOCALAPPDATA%\HoverTrans\models` 里出现 1.1 GB 的 gguf。
3. 装完自动启动，顶栏模型胶囊显示就绪；翻一句英文，5–20 秒出结果。
4. 悬停外文 → 浮窗出译文；闲置 10 分钟后 `%LOCALAPPDATA%\HoverTrans\logs\llama-server.log` 出现闲置退出。
5. 再装一次 → 复用同一目录，模型那一步秒过（不是重下）。
6. 卸载 → 程序目录和 `runtime` 没了，`models` 与 `settings.json` 还在。

## 还没做的（别当成已完成）

- **代码签名**：没有证书，SmartScreen 首次运行会报「未知发布者」，需要「更多信息 → 仍要运行」。要消掉得买代码签名证书（或至少用 Azure Trusted Signing），属发布阶段（w8）。
- **应用图标**：仓库里还没有 `.ico`，安装包和 exe 都用默认图标；补 `HoverTrans.ico` 后加 `SetupIconFile` 与 `<ApplicationIcon>`。
- **托盘常驻与开机自启**：应用当前没有托盘图标，关窗即退出；所以安装包故意不做「开机自启」任务，等 w6 的托盘一起做。
- **模型校验和未固定**：`model-manifest.json` 的 `size`/`sha256` 还是空值，脚本只查 900 MB 下限。发版前先跑一次 `deploy-model.ps1 -Force`，把打印出来的 size 和 sha256 填进清单，之后安装包就能拒绝被截断或被替换的模型。
- **macOS**：第二阶段，计划 `.pkg`（pkgbuild/productbuild）或 `.dmg` + `deploy-model.sh`，复用同样的 `~/Library/Application Support/HoverTrans` 布局，等 Accessibility/Vision 实现到位再打包。

## 为什么不用别的

- **MSIX**：安装体验最好，但自包含 .NET + Avalonia 的 MSIX 要么要求签名证书，要么走商店；模型外置下载在 MSIX 里也没现成位置。
- **NSIS**：能做，但 Inno 的向导、任务页和 `{autopf}` 按用户安装更省事，社区对 Avalonia 应用的 Inno 模板也更多。
- **自解压 ZIP**：最简单，但没有卸载项、没有快捷方式、杀毒误报更多；可以作为「便携版」补充，不适合主安装包。
