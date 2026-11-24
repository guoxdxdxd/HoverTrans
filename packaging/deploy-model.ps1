#!/usr/bin/env powershell
<#
.SYNOPSIS
    Puts the llama.cpp runtime and the Hy-MT2 1.8B model where HoverTrans looks for them.

.DESCRIPTION
    The installer, a portable ZIP and a source checkout all need the same two downloads, so this
    script is the single place that knows how to fetch and verify them.

    Files land where the app already looks by default (HoverTrans/Core/Platform/AppPaths.cs):

        %LOCALAPPDATA%\HoverTrans\runtime\llama-server.exe
        %LOCALAPPDATA%\HoverTrans\models\Hy-MT2-1.8B-Q4_K_M.gguf

    Both steps are idempotent: an intact file is left alone, so re-running the script, an
    in-place upgrade install, or "安装时忘了勾选下载" all cost nothing but the check. Interrupted
    downloads resume from the .part file on the next run.

    Exit codes: 0 ok or already present; 2 runtime download failed; 3 model download failed or
    incomplete; 4 the downloaded model does not match the pinned SHA256.

.PARAMETER Variant
    llama.cpp Windows build: cpu (default, works everywhere), cuda (NVIDIA GPU), vulkan (modern
    AMD/Intel GPU). The installer ships the cpu build; re-run with -Variant cuda -Force to swap it.

.PARAMETER RuntimeDir
    Where llama-server.exe goes. Defaults to %LOCALAPPDATA%\HoverTrans\runtime.

.PARAMETER ModelDir
    Where the .gguf goes. Defaults to %LOCALAPPDATA%\HoverTrans\models.

.PARAMETER Manifest
    model-manifest.json holding the pinned size, SHA256 and mirrors. Defaults to the copy next to
    this script. Missing manifest is not fatal: the script then checks the file size only.

.PARAMETER LlamaTag
    llama.cpp release tag to fetch, e.g. b12662. Defaults to the manifest value, then b12662.

.PARAMETER Force
    Download again even when both files are already present.

.PARAMETER SkipRuntime
    Do not touch the llama.cpp runtime.

.PARAMETER SkipModel
    Do not touch the model weights.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File .\deploy-model.ps1

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File .\deploy-model.ps1 -Variant cuda -Force -SkipModel
#>
[CmdletBinding()]
param(
    [ValidateSet('cpu', 'cuda', 'vulkan')]
    [string]$Variant = 'cpu',
    [string]$RuntimeDir = '',
    [string]$ModelDir = '',
    [string]$Manifest = '',
    [string]$LlamaTag = '',
    [switch]$SkipRuntime,
    [switch]$SkipModel,
    [switch]$Force
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$appRoot = Join-Path $env:LOCALAPPDATA 'HoverTrans'
if (-not $RuntimeDir) { $RuntimeDir = Join-Path $appRoot 'runtime' }
if (-not $ModelDir) { $ModelDir = Join-Path $appRoot 'models' }
if (-not $Manifest) { $Manifest = Join-Path $scriptRoot 'model-manifest.json' }

$serverExe = Join-Path $RuntimeDir 'llama-server.exe'
$modelName = 'Hy-MT2-1.8B-Q4_K_M.gguf'
$modelPath = Join-Path $ModelDir $modelName

# The real file is about 1.1 GB. Anything under this is a truncated download or an error page.
$minModelBytes = 900MB

$mirrors = @(
    'https://modelscope.cn/models/Tencent-Hunyuan/Hy-MT2-1.8B-GGUF/resolve/master/Hy-MT2-1.8B-Q4_K_M.gguf',
    'https://huggingface.co/tencent/Hy-MT2-1.8B-GGUF/resolve/main/Hy-MT2-1.8B-Q4_K_M.gguf?download=true'
)
$pinnedSha256 = ''
if (-not $LlamaTag) { $LlamaTag = 'b12662' }

if (Test-Path -LiteralPath $Manifest) {
    try {
        # Windows PowerShell 5.1 默认按 ANSI 读无 BOM 文件，中文会变乱码并破坏 JSON：显式指定 UTF8。
        $manifestData = Get-Content -LiteralPath $Manifest -Raw -Encoding UTF8 | ConvertFrom-Json
        if ($manifestData.sources) { $mirrors = @($manifestData.sources) }
        if ($manifestData.sha256 -and "$($manifestData.sha256)".Trim()) { $pinnedSha256 = "$($manifestData.sha256)".Trim().ToLowerInvariant() }
        if ($manifestData.size -and [long]$manifestData.size -gt $minModelBytes) { $minModelBytes = [long]$manifestData.size }
        if (-not $PSBoundParameters.ContainsKey('LlamaTag') -and $manifestData.llama -and $manifestData.llama.tag) { $LlamaTag = "$($manifestData.llama.tag)" }
    }
    catch {
        Write-Warning "无法读取 $Manifest，改用内置默认值：$($_.Exception.Message)"
    }
}
else {
    Write-Warning "没有找到 $Manifest，只做大小检查，不校验 SHA256。"
}

function Format-Size {
    param([long]$Bytes)
    if ($Bytes -ge 1GB) { return ('{0:N2} GB' -f ($Bytes / 1GB)) }
    return ('{0:N0} MB' -f ($Bytes / 1MB))
}

function Get-Curl {
    $curl = Get-Command curl.exe -ErrorAction SilentlyContinue
    if (-not $curl) { return $null }
    return $curl.Source
}

function Get-Sha256 {
    param([string]$Path)
    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
}

function Invoke-Download {
    <# Downloads to $Destination: curl with resume and retries first, Invoke-WebRequest as fallback. #>
    param(
        [string]$Url,
        [string]$Destination,
        [string]$Curl
    )

    if ($Curl) {
        & $Curl -L --fail --retry 5 --retry-delay 3 --retry-all-errors --http1.1 -C - --progress-bar -o $Destination $Url
        if ($LASTEXITCODE -eq 0) { return $true }
        Write-Warning "curl 退出码 $LASTEXITCODE，改用 Invoke-WebRequest 重试。"
    }

    try {
        Invoke-WebRequest -Uri $Url -OutFile $Destination -UseBasicParsing -TimeoutSec 1800
        return $true
    }
    catch {
        Write-Warning "下载失败：$($_.Exception.Message)"
        return $false
    }
}

function Resolve-LlamaDownloads {
    <#
        Which zips to fetch for this tag and variant. Asking the release API first is not optional:
        asset names carry versions (the CUDA build is llama-<tag>-bin-win-cuda-12.4-x64.zip and needs
        the separate cudart archive), so a fixed name pattern only holds for cpu and vulkan.
    #>
    param(
        [string]$Tag,
        [string]$BuildVariant
    )

    $downloads = New-Object System.Collections.ArrayList
    $assets = @()
    try {
        $release = Invoke-RestMethod `
            -Uri "https://api.github.com/repos/ggml-org/llama.cpp/releases/tags/$Tag" `
            -Headers @{ 'User-Agent' = 'HoverTrans-setup' } `
            -TimeoutSec 30
        $assets = @($release.assets)
    }
    catch {
        Write-Warning "查询 llama.cpp $Tag 的资产失败：$($_.Exception.Message)。改用固定文件名。"
    }

    if ($assets.Count -gt 0) {
        if ($BuildVariant -eq 'cuda') {
            $cudart = $assets | Where-Object { $_.name -match '^cudart-llama-.*-x64\.zip$' } | Select-Object -First 1
            if ($cudart) {
                [void]$downloads.Add([pscustomobject]@{ Name = $cudart.name; Url = $cudart.browser_download_url })
            }
            else {
                Write-Warning "没有找到 CUDA 运行时包（cudart），llama-server 可能起不来。"
            }
        }

        $pattern = '^llama-.*-bin-win-' + $BuildVariant + '(-.*)?-x64\.zip$'
        $main = $assets | Where-Object { $_.name -match $pattern } | Select-Object -First 1
        if ($main) {
            [void]$downloads.Add([pscustomobject]@{ Name = $main.name; Url = $main.browser_download_url })
            return $downloads
        }

        Write-Warning "llama.cpp $Tag 里没有 $BuildVariant 的 Windows x64 包，改用固定文件名。"
    }

    # Offline or unexpected API answer: the plain names have been stable for cpu and vulkan.
    $fallbackName = "llama-$Tag-bin-win-$BuildVariant-x64.zip"
    [void]$downloads.Add([pscustomobject]@{
            Name = $fallbackName
            Url  = "https://github.com/ggml-org/llama.cpp/releases/download/$Tag/$fallbackName"
        })
    return $downloads
}

function Expand-RuntimeArchive {
    param(
        [string]$ZipPath,
        [string]$Destination
    )

    $extract = Join-Path $env:TEMP ('hovertrans-llama-' + [guid]::NewGuid().ToString('N'))
    try {
        Expand-Archive -LiteralPath $ZipPath -DestinationPath $extract -Force

        # The cuda/vulkan builds need their DLLs beside the exe, so the whole containing folder is copied.
        $found = Get-ChildItem -LiteralPath $extract -Recurse -Filter 'llama-server.exe' | Select-Object -First 1
        if ($found) {
            Copy-Item -Path (Join-Path $found.Directory.FullName '*') -Destination $Destination -Recurse -Force
            return $true
        }

        # The cudart archive ships only DLLs; they belong in the same folder.
        $dll = Get-ChildItem -LiteralPath $extract -Recurse -Filter '*.dll' | Select-Object -First 1
        if ($dll) {
            Copy-Item -Path (Join-Path $dll.Directory.FullName '*') -Destination $Destination -Recurse -Force
            return $true
        }

        throw "压缩包里既没有 llama-server.exe 也没有 DLL：$ZipPath"
    }
    finally {
        Remove-Item -LiteralPath $extract -Recurse -Force -ErrorAction SilentlyContinue
    }
}

function Install-Runtime {
    if ((Test-Path -LiteralPath $serverExe) -and -not $Force) {
        Write-Host "==> llama-server 已存在：$serverExe（-Force 可更新）" -ForegroundColor DarkGray
        return $true
    }

    New-Item -ItemType Directory -Force -Path $RuntimeDir | Out-Null

    $curl = Get-Curl
    if (-not $curl) {
        Write-Warning '需要 curl.exe（Windows 10 1803+ 自带）。'
    }

    Write-Host "==> llama.cpp $LlamaTag ($Variant)" -ForegroundColor Cyan
    $applied = $false
    foreach ($download in (Resolve-LlamaDownloads -Tag $LlamaTag -BuildVariant $Variant)) {
        $zip = Join-Path $env:TEMP $download.Name
        Write-Host "    $($download.Url)"
        try {
            if (Test-Path -LiteralPath $zip) { Remove-Item -LiteralPath $zip -Force -ErrorAction SilentlyContinue }
            if (-not (Invoke-Download -Url $download.Url -Destination $zip -Curl $curl)) { throw '下载失败' }
            if (-not (Test-Path -LiteralPath $zip)) { throw '下载失败（没有生成文件）' }
            if (Expand-RuntimeArchive -ZipPath $zip -Destination $RuntimeDir) { $applied = $true }
        }
        catch {
            Write-Warning "  $($download.Name)：$($_.Exception.Message)"
        }
        finally {
            Remove-Item -LiteralPath $zip -Force -ErrorAction SilentlyContinue
        }
    }

    if ($applied -and (Test-Path -LiteralPath $serverExe)) {
        Write-Host "    -> $RuntimeDir" -ForegroundColor Green
        return $true
    }

    Write-Warning 'llama-server 没有装好。'
    Write-Host '    应用本身可以安装，只是第一次翻译会提示缺少运行时；' -ForegroundColor Yellow
    Write-Host '    联网后重新运行一次本脚本即可（NVIDIA 显卡加 -Variant cuda）。' -ForegroundColor Yellow
    return $false
}

function Install-Model {
    if ((Test-Path -LiteralPath $modelPath) -and -not $Force) {
        $size = (Get-Item -LiteralPath $modelPath).Length
        if ($size -ge $minModelBytes) {
            Write-Host "==> 模型已存在：$modelPath（$(Format-Size $size)，-Force 可重新下载）" -ForegroundColor DarkGray
            if ($pinnedSha256) {
                $hash = Get-Sha256 -Path $modelPath
                if ($hash -ne $pinnedSha256) {
                    Write-Warning "已有模型的 SHA256 与清单不一致：$hash"
                    Write-Warning '删除它，或加 -Force 重新下载。'
                    return $false
                }
            }
            return $true
        }

        Write-Warning "模型文件不完整（$(Format-Size $size)），重新下载。"
    }

    New-Item -ItemType Directory -Force -Path $ModelDir | Out-Null

    $curl = Get-Curl
    if (-not $curl) {
        Write-Warning '需要 curl.exe（Windows 10 1803+ 自带）。'
    }

    $part = "$modelPath.part"
    $partUrl = "$modelPath.part.url"
    $downloaded = $false

    foreach ($url in $mirrors) {
        # Resuming across mirrors would splice two different files: the marker guards that.
        if ((Test-Path -LiteralPath $part) -and (Test-Path -LiteralPath $partUrl)) {
            $previous = (Get-Content -LiteralPath $partUrl -Raw).Trim()
            if ($previous -ne $url) {
                Remove-Item -LiteralPath $part -Force -ErrorAction SilentlyContinue
            }
        }
        Set-Content -LiteralPath $partUrl -Value $url -Encoding ASCII

        Write-Host "==> 下载 Hy-MT2 1.8B Q4_K_M（约 1.1 GB，断点续传）" -ForegroundColor Cyan
        Write-Host "    $url"
        if (Invoke-Download -Url $url -Destination $part -Curl $curl) {
            if ((Test-Path -LiteralPath $part) -and (Get-Item -LiteralPath $part).Length -ge $minModelBytes) {
                $downloaded = $true
                break
            }
            Write-Warning "文件不完整（$(Format-Size (Get-Item -LiteralPath $part).Length)），换下一个镜像。"
            Remove-Item -LiteralPath $part -Force -ErrorAction SilentlyContinue
        }
    }

    if (-not $downloaded) {
        Write-Warning '两个镜像都失败了。'
        Write-Host "    可以稍后重新运行本脚本，或手动把 $modelName 放到 $ModelDir。" -ForegroundColor Yellow
        return $false
    }

    $hash = Get-Sha256 -Path $part
    if ($pinnedSha256 -and $hash -ne $pinnedSha256) {
        # 不删：万一是上游重新上传了权重，1.1 GB 重下太贵；改名留着，应用不会把它当成模型加载。
        $rejected = "$modelPath.mismatch"
        Move-Item -LiteralPath $part -Destination $rejected -Force
        Remove-Item -LiteralPath $partUrl -Force -ErrorAction SilentlyContinue
        Write-Warning "SHA256 不匹配：得到 $hash，清单要求 $pinnedSha256。"
        Write-Host "    已保留为 $rejected，没有装成模型。" -ForegroundColor Yellow
        Write-Host '    确认上游没问题后：用新值更新 packaging\model-manifest.json，或删掉这个文件和清单里的 sha256 再重试。' -ForegroundColor Yellow
        return $false
    }

    Move-Item -LiteralPath $part -Destination $modelPath -Force
    Remove-Item -LiteralPath $partUrl -Force -ErrorAction SilentlyContinue
    Write-Host "    -> $modelPath（$(Format-Size (Get-Item -LiteralPath $modelPath).Length)）" -ForegroundColor Green
    Write-Host "    sha256: $hash"
    return $true
}

Write-Host ''
Write-Host '悬译 HoverTrans · 本地运行时与模型部署' -ForegroundColor White
Write-Host ''

$runtimeOk = $true
$modelOk = $true

if (-not $SkipRuntime) { $runtimeOk = Install-Runtime }
if (-not $SkipModel) { $modelOk = Install-Model }

Write-Host ''
if ($runtimeOk -and $modelOk) {
    Write-Host '完成：模型和运行时都就位了。' -ForegroundColor Green
    Write-Host '  运行时: ' + $serverExe
    Write-Host '  模型:   ' + $modelPath
    Write-Host '  首次翻译要加载模型，通常 5–20 秒；之后同一进程内是毫秒级。'
    exit 0
}

if (-not $runtimeOk -and -not $modelOk) {
    Write-Warning '模型和运行时都没装好，应用可以打开，但翻译会提示缺少文件。'
    exit 3
}
if (-not $runtimeOk) {
    Write-Warning '运行时没装好；模型已就位。联网后重新运行本脚本即可。'
    exit 2
}

if ($pinnedSha256) {
    Write-Warning '模型没装好（下载失败或校验不通过）。联网后重新运行本脚本即可。'
}
else {
    Write-Warning '模型没装好（下载失败）。联网后重新运行本脚本即可。'
}
exit 3
