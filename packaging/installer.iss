; 悬译 HoverTrans —— Windows 安装包（Inno Setup 6.3+）
;

; 构建：packaging/build-installer.ps1 会先 dotnet publish，再用 ISCC 调本脚本，并传入
; /DAppVersion、/DPayloadDir、/DRuntimePayloadDir、/DOutputDir、/DRepoRoot 等定义。
; 也直接在 Inno Setup IDE 里打开本文件按 F9 —— 下面的 #ifndef 默认值指向仓库内的构建产物。
;
; 设计取舍（详见 packaging/README.md）：
;   * 按用户安装到 %LOCALAPPDATA%\Programs\HoverTrans，不需要管理员：托盘常驻的小工具不该弹 UAC。
;   * llama.cpp 运行时（约 40 MB）随安装包分发，解到 %LOCALAPPDATA%\HoverTrans\runtime ——
;     这是应用默认查找的位置，装完即用。
;   * 模型 1.1 GB 不塞进安装包：体积、GitHub Release 限制、模型许可证与更新频率都不划算。
;     改成安装最后一步调用 deploy-model.ps1 下载 + 校验，装完即可翻译。

#ifndef AppVersion
  #define AppVersion "0.1.0"
#endif

#ifndef PayloadDir
  #define PayloadDir "..\artifacts\win-x64"
#endif

#ifndef RuntimePayloadDir
  #define RuntimePayloadDir "..\artifacts\llama-runtime"
#endif

#ifndef OutputDir
  #define OutputDir "..\artifacts\installer"
#endif

#ifndef RepoRoot
  #define RepoRoot ".."
#endif

; 有 packaging\ChineseSimplified.isl 时 build-installer.ps1 会传进来，安装界面就是中文。
; 没有就退回英文（Inno Setup 官方不附带简体中文语言文件）。

[Setup]
; 同一个 AppId 保证升级安装复用目录并替换旧版本，不产生第二个「悬译」。
AppId={{A6F1B7C4-2D5E-4F80-9B31-7C2E5A4D9E17}
AppName=悬译 HoverTrans
AppVersion={#AppVersion}
AppVerName=悬译 HoverTrans {#AppVersion}
AppPublisher=HoverTrans
DefaultDirName={autopf}\HoverTrans
DefaultGroupName=悬译 HoverTrans
DisableProgramGroupPage=yes
; 按用户安装：不弹 UAC，卸载也不需要管理员。
PrivilegesRequired=lowest
OutputDir={#OutputDir}
OutputBaseFilename=HoverTrans-{#AppVersion}-win-x64-setup
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
; 与 HoverTrans.csproj 的 SupportedOSPlatformVersion 一致：Windows 10 1809 起。
MinVersion=10.0.17763
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
UninstallDisplayIcon={app}\HoverTrans.exe
UninstallDisplayName=悬译 HoverTrans
CloseApplications=yes
RestartApplications=no
LicenseFile={#RepoRoot}\LICENSE

[Languages]
#ifdef ChineseIsl
Name: "zh"; MessagesFile: "{#ChineseIsl}"
#endif
Name: "en"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "创建桌面快捷方式"; GroupDescription: "附加任务："; Flags: unchecked
Name: "downloadmodel"; Description: "下载本地翻译模型 Hy-MT2 1.8B（约 1.1 GB，需要联网）"; GroupDescription: "本地模型："

[Files]
; 应用本体：dotnet publish 的自包含单文件输出（HoverTrans.exe 加它自解压需要的原生库）。
; 原生包会带 .pdb，只用于调试，不打进安装包。
Source: "{#PayloadDir}\*"; DestDir: "{app}"; Excludes: "*.pdb,*.xml"; Flags: ignoreversion recursesubdirs createallsubdirs
; llama.cpp 运行时：放到应用默认查找的 %LOCALAPPDATA%\HoverTrans\runtime，装完无需再下。
#ifndef NoBundledRuntime
Source: "{#RuntimePayloadDir}\*"; DestDir: "{localappdata}\HoverTrans\runtime"; Flags: ignoreversion recursesubdirs createallsubdirs skipifsourcedoesntexist
#endif
; 模型部署脚本与校验清单：安装时由 [Run] 调用，之后用户也能自己重跑（换 GPU 版本、补下载）。
Source: "deploy-model.ps1"; DestDir: "{app}"; Flags: ignoreversion
Source: "model-manifest.json"; DestDir: "{app}"; Flags: ignoreversion
; 第三方许可：应用 MIT、llama.cpp MIT、模型 Apache-2.0。
Source: "{#RepoRoot}\LICENSE"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\悬译 HoverTrans"; Filename: "{app}\HoverTrans.exe"
Name: "{group}\卸载 悬译 HoverTrans"; Filename: "{uninstallexe}"
Name: "{userdesktop}\悬译 HoverTrans"; Filename: "{app}\HoverTrans.exe"; Tasks: desktopicon

[Run]
; 模型自动部署：可见的 PowerShell 窗口里显示下载进度，装完就能翻译。
; 已经存在完整模型时 ShouldDeployModel 会返回 False，跳过这一步。
Filename: "{sys}\WindowsPowerShell\v1.0\powershell.exe"; \
  Parameters: "-NoLogo -NoProfile -ExecutionPolicy Bypass -File ""{app}\deploy-model.ps1"" -RuntimeDir ""{localappdata}\HoverTrans\runtime"" -ModelDir ""{localappdata}\HoverTrans\models"""; \
  WorkingDir: "{app}"; StatusMsg: "正在准备本地翻译模型（约 1.1 GB）……"; \
  Flags: waituntilterminated skipifsilent; Check: ShouldDeployModel
; 静默安装（/VERYSILENT）不下载模型：批量部署请自己调用 deploy-model.ps1。
Filename: "{app}\HoverTrans.exe"; Description: "立即运行 悬译 HoverTrans"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
; 运行时是安装包铺下来的派生物，卸载时清掉；模型目录故意保留 —— 重新下载 1.1 GB 太贵。
Type: filesandordirs; Name: "{localappdata}\HoverTrans\runtime"

[Code]
function ModelFilePresent: Boolean;
var
  ModelPath: String;
begin
  ModelPath := ExpandConstant('{localappdata}\HoverTrans\models\Hy-MT2-1.8B-Q4_K_M.gguf');
  Result := FileExists(ModelPath);
end;

function ShouldDeployModel: Boolean;
begin
  Result := WizardIsTaskSelected('downloadmodel') and not ModelFilePresent;
end;
