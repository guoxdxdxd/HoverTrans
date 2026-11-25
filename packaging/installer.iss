; 悬译 HoverTrans —— Windows 安装包（Inno Setup 6.3+）
;

; 构建：packaging/build-installer.ps1 先 dotnet publish，再把版本与路径写进
; packaging/installer.generated.iss，最后用 ISCC 调本脚本。
; 不把路径放在命令行 -D 上：PowerShell 7 会重新剖析命令行的引号，带空格/中文的值传过去就成了
; 「OutputBaseFileName is invalid」这类错误（GitHub Actions 的 shell 就是 pwsh）。
; 也直接在 Inno Setup IDE 里打开本文件按 F9 —— 没有生成文件时下面的默认值指向仓库内的构建产物。
;
; 设计取舍（详见 packaging/README.md）：
;   * 按用户安装到 %LOCALAPPDATA%\Programs\HoverTrans，不需要管理员：托盘常驻的小工具不该弹 UAC。
;   * 模型权重（Hy-MT3 1.8B Q4_K_M，约 1.35 GB）随应用本体分发：推理在进程内，权重就是应用的
;     一部分（Sdcb.GgufWeights 的 6 个程序集），没有可后补的「模型文件」，也没有安装时的下载步骤。
;     代价是安装包从 67 MB 涨到约 1.3 GB；这是把 llama.cpp 换掉、去掉外部进程换来的。
;   * 因此压缩用 lzma2/fast、不做 solid：GGUF 权重几乎不可压缩，max 要慢十几倍却只省几个百分点。

#ifexist "installer.generated.iss"
  #include "installer.generated.iss"
#endif

#ifndef AppVersion
  #define AppVersion "0.1.0"
#endif

#ifndef PayloadDir
  #define PayloadDir "..\artifacts\win-x64"
#endif

#ifndef OutputDir
  #define OutputDir "..\artifacts\installer"
#endif

#ifndef RepoRoot
  #define RepoRoot ".."
#endif

; 生成文件里定义了 ChineseIsl 时（packaging\ChineseSimplified.isl 存在），安装界面就是中文；
; 否则退回英文（Inno Setup 官方不附带简体中文语言文件）。

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
Compression=lzma2/fast
SolidCompression=no
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

[Files]
; 应用本体：dotnet publish 的 self-contained 输出（HoverTrans.exe、原生库，以及约 1.35 GB 的模型权重
; 程序集）。.pdb 只用于调试，不打进安装包。
Source: "{#PayloadDir}\*"; DestDir: "{app}"; Excludes: "*.pdb,*.xml"; Flags: ignoreversion recursesubdirs createallsubdirs
; 第三方许可：应用 MIT、Sdcb.HyMT2Sharp 与推理内核 Apache-2.0、模型权重 Apache-2.0。
Source: "{#RepoRoot}\LICENSE"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#RepoRoot}\packaging\THIRD-PARTY-NOTICES.txt"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\悬译 HoverTrans"; Filename: "{app}\HoverTrans.exe"
Name: "{group}\卸载 悬译 HoverTrans"; Filename: "{uninstallexe}"
Name: "{userdesktop}\悬译 HoverTrans"; Filename: "{app}\HoverTrans.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\HoverTrans.exe"; Description: "立即运行 悬译 HoverTrans"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
; 应用数据（设置、历史、日志）故意保留：重装后设置和历史还在，那些文件也很小。
