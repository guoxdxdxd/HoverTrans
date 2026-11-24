#!/usr/bin/env powershell
<#
.SYNOPSIS
    Builds the Windows installer (HoverTrans-<version>-win-x64-setup.exe) for HoverTrans.

.DESCRIPTION
    Three steps, each skippable:

      1. dotnet publish  -> artifacts\win-x64        (self-contained, single file, win-x64)
      2. llama.cpp zip    -> artifacts\llama-runtime  (the runtime the installer ships)
      3. ISCC installer.iss -> artifacts\installer\HoverTrans-<version>-win-x64-setup.exe

    The model is deliberately not part of the installer: the installer runs deploy-model.ps1 after
    copying the files, which downloads and verifies it on the user's machine (see packaging\README.md).

.PARAMETER Version
    Version shown in the installer and the file name. Defaults to `git describe --tags --always`,
    falling back to 0.1.0 when git is unavailable.

.PARAMETER RuntimeVariant
    llama.cpp build to bundle: cpu (default, runs everywhere), cuda, vulkan.

.PARAMETER LlamaTag
    llama.cpp release tag, e.g. b12662. Defaults to the tag in packaging\model-manifest.json.

.PARAMETER RuntimeSource
    Reuse an existing llama.cpp folder (the one holding llama-server.exe) instead of downloading.

.PARAMETER SkipRuntime
    Do not bundle a runtime. The installer then only installs the app, and deploy-model.ps1
    fetches llama-server on the user's machine together with the model.

.PARAMETER SkipPublish
    Reuse artifacts\win-x64 from a previous run (fast iteration on installer.iss only).

.PARAMETER Iscc
    Full path to ISCC.exe. Defaults to PATH and the usual Inno Setup install locations.

.EXAMPLE
    pwsh -File .\packaging\build-installer.ps1

.EXAMPLE
    pwsh -File .\packaging\build-installer.ps1 -Version 0.2.0 -RuntimeVariant cuda
#>
[CmdletBinding()]
param(
    [string]$Version = '',
    [ValidateSet('cpu', 'cuda', 'vulkan')]
    [string]$RuntimeVariant = 'cpu',
    [string]$LlamaTag = '',
    [string]$RuntimeSource = '',
    [switch]$SkipRuntime,
    [switch]$SkipPublish,
    [string]$Iscc = ''
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

$packagingDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = Split-Path -Parent $packagingDir
$artifacts = Join-Path $repoRoot 'artifacts'
$payload = Join-Path $artifacts 'win-x64'
$runtimePayload = Join-Path $artifacts 'llama-runtime'
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

    Format-Step "dotnet publish (win-x64, self-contained, single file, 版本 $PublishVersion)"
    if (Test-Path -LiteralPath $payload) { Remove-Item -LiteralPath $payload -Recurse -Force }

    # -p:Version 进的是应用自己的版本号；「检查更新」拿它和 GitHub 上最新 release 的 tag 比，
    # 所以这里必须是干净的 0.1.0，不能是 git describe 的 0.1.0-11-g778f5aa。
    & dotnet publish (Join-Path $repoRoot 'HoverTrans\HoverTrans.csproj') `
        -f net11.0-windows10.0.19041.0 `
        -c Release `
        -r win-x64 `
        --self-contained true `
        -p:PublishSingleFile=true `
        -p:DebugType=None `
        -p:DebugSymbols=false `
        -p:Version=$PublishVersion `
        -o $payload
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish 失败（退出码 $LASTEXITCODE）。" }

    $exe = Join-Path $payload 'HoverTrans.exe'
    if (-not (Test-Path -LiteralPath $exe)) { throw "发布输出里没有 HoverTrans.exe：$payload" }
    Write-Host "    -> $exe" -ForegroundColor Green
}

function Install-RuntimePayload {
    Format-Step "llama.cpp 运行时（$RuntimeVariant）"

    if ($RuntimeSource) {
        if (-not (Test-Path -LiteralPath (Join-Path $RuntimeSource 'llama-server.exe'))) {
            throw "RuntimeSource 里没有 llama-server.exe：$RuntimeSource"
        }
        if (Test-Path -LiteralPath $runtimePayload) { Remove-Item -LiteralPath $runtimePayload -Recurse -Force }
        New-Item -ItemType Directory -Force -Path $runtimePayload | Out-Null
        Copy-Item -Path (Join-Path $RuntimeSource '*') -Destination $runtimePayload -Recurse -Force
        Write-Host "    <- $RuntimeSource" -ForegroundColor Green
        return
    }

    if (-not $LlamaTag) {
        $manifestPath = Join-Path $packagingDir 'model-manifest.json'
        if (Test-Path -LiteralPath $manifestPath) {
            # Windows PowerShell 5.1 默认按 ANSI 读无 BOM 文件，中文会变乱码并破坏 JSON：显式指定 UTF8。
            $manifestData = Get-Content -LiteralPath $manifestPath -Raw -Encoding UTF8 | ConvertFrom-Json
            if ($manifestData.llama -and $manifestData.llama.tag) { $LlamaTag = "$($manifestData.llama.tag)" }
        }
    }

    # 复用部署脚本：它知道 llama.cpp 的资产名带版本号（cuda 还要额外的 cudart 包），
    # 这样「构建时内置」和「安装时下载」共用一套下载逻辑，不会各走各的漂移。
    if (Test-Path -LiteralPath $runtimePayload) { Remove-Item -LiteralPath $runtimePayload -Recurse -Force }

    $deployArgs = @{
        RuntimeDir = $runtimePayload
        Variant    = $RuntimeVariant
        SkipModel  = $true
        Force      = $true
    }
    if ($LlamaTag) { $deployArgs['LlamaTag'] = $LlamaTag }

    & (Join-Path $packagingDir 'deploy-model.ps1') @deployArgs
    if ($LASTEXITCODE -ne 0) { throw "取 llama.cpp 运行时失败（退出码 $LASTEXITCODE）。" }
    if (-not (Test-Path -LiteralPath (Join-Path $runtimePayload 'llama-server.exe'))) {
        throw "运行时目录里没有 llama-server.exe：$runtimePayload"
    }
    Write-Host "    -> $runtimePayload" -ForegroundColor Green
}

function Invoke-Iscc {
    param([string]$ResolvedVersion)

    $isccPath = Resolve-Iscc
    Format-Step "ISCC ($isccPath)"

    New-Item -ItemType Directory -Force -Path $outputDir | Out-Null

    # 值一律加引号交给 ISPP，路径里有空格或中文时不会被当成表达式解析。
    $defines = @(
        "/DAppVersion=`"$ResolvedVersion`"",
        "/DPayloadDir=`"$payload`"",
        "/DRuntimePayloadDir=`"$runtimePayload`"",
        "/DOutputDir=`"$outputDir`"",
        "/DRepoRoot=`"$repoRoot`""
    )
    if ($SkipRuntime) { $defines += '/DNoBundledRuntime' }

    $chinese = Join-Path $packagingDir 'ChineseSimplified.isl'
    if (Test-Path -LiteralPath $chinese) { $defines += "/DChineseIsl=`"$chinese`"" }

    & $isccPath @defines (Join-Path $packagingDir 'installer.iss')
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

if (-not $SkipRuntime) { Install-RuntimePayload }
else { Write-Host '（跳过 llama.cpp 运行时：安装后由 deploy-model.ps1 下载）' -ForegroundColor DarkGray }

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
