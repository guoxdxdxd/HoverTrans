<#
.SYNOPSIS
    Builds the Windows installer (HoverTrans-<version>-win-x64-setup.exe) for HoverTrans.

.DESCRIPTION
    Two steps, each skippable:

      1. dotnet publish  -> artifacts\win-x64   (self-contained, win-x64; the model weights ship here)
      2. ISCC installer.iss -> artifacts\installer\HoverTrans-<version>-win-x64-setup.exe

    The model is part of the app now: inference runs in-process (Sdcb.HyMT2Sharp) and the weights come
    from the Sdcb.GgufWeights.Hy-MT3-1.8B-Q4_K_M package, so publish output is ~1.4 GB and the installer
    is ~1.3 GB. There is no runtime to download and no model download step any more.

    Publish is deliberately NOT single-file: bundling 1.3 GB of assemblies into one exe only makes the
    first start extract them again. A folder publish installs straight into %LOCALAPPDATA%.

.PARAMETER Version
    Version shown in the installer and the file name. Defaults to `git describe --tags --always`,
    falling back to 0.1.0 when git is unavailable.

.PARAMETER SkipPublish
    Reuse artifacts\win-x64 from a previous run (fast iteration on installer.iss only).

.PARAMETER Iscc
    Full path to ISCC.exe. Defaults to PATH and the usual Inno Setup install locations.

.EXAMPLE
    pwsh -File .\packaging\build-installer.ps1

.EXAMPLE
    pwsh -File .\packaging\build-installer.ps1 -Version 0.2.0
#>
[CmdletBinding()]
param(
    [string]$Version = '',
    [switch]$SkipPublish,
    [string]$Iscc = ''
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

$packagingDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = Split-Path -Parent $packagingDir
$artifacts = Join-Path $repoRoot 'artifacts'
$payload = Join-Path $artifacts 'win-x64'
$outputDir = Join-Path $artifacts 'installer'

function Format-Step {
    param([string]$Message)
    Write-Host ''
    Write-Host "==> $Message" -ForegroundColor Cyan
}

function Resolve-Version {
    if ($Version) { return (($Version -replace '^v', '') -replace '[^0-9A-Za-z\.\+\-]', '-') }

    $described = ''
    try {
        if (Get-Command git -ErrorAction SilentlyContinue) {
            $described = (& git -C $repoRoot describe --tags --always --dirty 2>$null | Select-Object -First 1)
        }
    }
    catch { $described = '' }

    if (-not $described) { $described = '0.1.0' }
    $described = $described.Trim() -replace '^v', ''
    return ($described -replace '[^0-9A-Za-z\.\+\-]', '-')
}

function Resolve-Iscc {
    if ($Iscc) {
        if (-not (Test-Path -LiteralPath $Iscc)) { throw "找不到 ISCC.exe：$Iscc" }
        return (Resolve-Path -LiteralPath $Iscc).Path
    }

    $onPath = Get-Command ISCC.exe -ErrorAction SilentlyContinue
    if ($onPath) { return $onPath.Source }

    $candidates = @(
        (Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe'),
        'C:\Program Files (x86)\Inno Setup 6\ISCC.exe',
        'C:\Program Files\Inno Setup 6\ISCC.exe'
    )
    foreach ($candidate in $candidates) {
        if (Test-Path -LiteralPath $candidate) { return $candidate }
    }

    throw @'
没找到 Inno Setup 6 的 ISCC.exe。装一个（免费）：
  winget install --id JRSoftware.InnoSetup -e
或从 https://jrsoftware.org/isdl.php 下载安装，然后用 -Iscc 指定路径。
'@
}

function Invoke-Publish {
    param([string]$PublishVersion)

    Format-Step "dotnet publish (win-x64, self-contained, 版本 $PublishVersion)"
    if (Test-Path -LiteralPath $payload) { Remove-Item -LiteralPath $payload -Recurse -Force }

    # -p:Version 进的是应用自己的版本号；「检查更新」拿它和 GitHub 上最新 release 的 tag 比，
    # 所以这里必须是干净的 0.1.0，不能是 git describe 的 0.1.0-11-g778f5aa。
    # 不开 PublishSingleFile：1.3 GB 的模型程序集打进单文件只会让首次启动再把它们解出来。
    & dotnet publish (Join-Path $repoRoot 'HoverTrans\HoverTrans.csproj') `
        -f net11.0-windows10.0.19041.0 `
        -c Release `
        -r win-x64 `
        --self-contained true `
        -p:PublishSingleFile=false `
        -p:DebugType=None `
        -p:DebugSymbols=false `
        -p:Version=$PublishVersion `
        -o $payload
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish 失败（退出码 $LASTEXITCODE）。" }

    $exe = Join-Path $payload 'HoverTrans.exe'
    if (-not (Test-Path -LiteralPath $exe)) { throw "发布输出里没有 HoverTrans.exe：$payload" }

    # 少了权重程序集也不会报错，只会在第一次翻译时加载失败：在这里就挡住。
    $weights = @(Get-ChildItem -LiteralPath $payload -Filter 'Sdcb.GgufWeights.Hy-MT3-1.8B-Q4_K_M*.dll')
    if ($weights.Count -eq 0) { throw "发布输出里没有 Hy-MT3 权重程序集：$payload" }

    $sizeMb = [math]::Round(((Get-ChildItem -LiteralPath $payload -Recurse -File | Measure-Object Length -Sum).Sum / 1MB), 0)
    Write-Host "    -> $exe（$sizeMb MB，含 $($weights.Count) 个权重程序集）" -ForegroundColor Green
}

function Write-InstallerDefinitions {
    param([string]$ResolvedVersion)

    # 路径和版本写进一个 ISPP 文件，而不是命令行 -D：PowerShell 7（GitHub Actions 的 shell）会把
    # "/DName="带空格的值"" 重新剖析，引号留在值里，ISCC 就报 OutputBaseFileName is invalid。
    $lines = @(
        '; 由 packaging/build-installer.ps1 生成：不要手改，也不要提交（.gitignore 已忽略）。'
        "#define AppVersion `"$ResolvedVersion`""
        "#define PayloadDir `"$payload`""
        "#define OutputDir `"$outputDir`""
        "#define RepoRoot `"$repoRoot`""
    )

    $chinese = Join-Path $packagingDir 'ChineseSimplified.isl'
    if (Test-Path -LiteralPath $chinese) { $lines += "#define ChineseIsl `"$chinese`"" }

    $path = Join-Path $packagingDir 'installer.generated.iss'
    [IO.File]::WriteAllText(
        $path,
        ($lines -join [Environment]::NewLine) + [Environment]::NewLine,
        (New-Object System.Text.UTF8Encoding($true)))
    Write-Host "    -> $path" -ForegroundColor DarkGray
}

function Invoke-Iscc {
    param([string]$ResolvedVersion)

    $isccPath = Resolve-Iscc
    Format-Step "ISCC ($isccPath)"

    New-Item -ItemType Directory -Force -Path $outputDir | Out-Null
    Write-InstallerDefinitions -ResolvedVersion $ResolvedVersion

    & $isccPath (Join-Path $packagingDir 'installer.iss')
    if ($LASTEXITCODE -ne 0) { throw "ISCC 失败（退出码 $LASTEXITCODE）。" }
}

$resolvedVersion = Resolve-Version
# 应用自己的版本号取 tag 的三段数字：v0.2.0 和 0.2.0-11-g778f5aa 都出 0.2.0，更新检查才比得准。
$publishVersion = if ($resolvedVersion -match '^(\d+\.\d+\.\d+)') { $Matches[1] } else { '0.1.0' }
Write-Host "悬译 HoverTrans 安装包构建 · 版本 $resolvedVersion（应用内版本 $publishVersion）" -ForegroundColor White

if (-not $SkipPublish) { Invoke-Publish -PublishVersion $publishVersion }
elseif (-not (Test-Path -LiteralPath (Join-Path $payload 'HoverTrans.exe'))) {
    throw "跳过了发布，但 $payload 里没有 HoverTrans.exe。"
}

Invoke-Iscc -ResolvedVersion $resolvedVersion

$setup = Get-ChildItem -LiteralPath $outputDir -Filter '*.exe' |
    Sort-Object LastWriteTime -Descending |
    Select-Object -First 1

Write-Host ''
if ($setup) {
    Write-Host "安装包：$($setup.FullName)" -ForegroundColor Green
    Write-Host ("大小：{0:N1} MB" -f ($setup.Length / 1MB))
}
else {
    Write-Warning "ISCC 成功但 $outputDir 里没有 exe。"
}
