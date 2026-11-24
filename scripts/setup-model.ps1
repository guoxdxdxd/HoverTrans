#!/usr/bin/env powershell
<#
.SYNOPSIS
    Downloads llama-server and the Hy-MT2 1.8B Q4_K_M model for HoverTrans (Windows).

.DESCRIPTION
    The model and the llama.cpp runtime are not committed to git (they are ~1.1 GB together).
    This script puts them under %LOCALAPPDATA%\HoverTrans, where the app looks by default.

.PARAMETER Variant
    llama.cpp Windows build: cpu (default), cuda, vulkan. Use cuda for an NVIDIA GPU,
    vulkan for a modern AMD/Intel GPU; cpu works everywhere.

.PARAMETER SkipModel
    Only download/update llama-server.

.PARAMETER Force
    Re-download even when the files already exist.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File ./scripts/setup-model.ps1
    powershell -ExecutionPolicy Bypass -File ./scripts/setup-model.ps1 -Variant cuda
#>
[CmdletBinding()]
param(
    [ValidateSet('cpu', 'cuda', 'vulkan')]
    [string]$Variant = 'cpu',
    [switch]$SkipModel,
    [switch]$Force
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

$root = Join-Path $env:LOCALAPPDATA 'HoverTrans'
$runtime = Join-Path $root 'runtime'
$models = Join-Path $root 'models'
New-Item -ItemType Directory -Force -Path $runtime, $models | Out-Null

function Get-Curl {
    $curl = Get-Command curl.exe -ErrorAction SilentlyContinue
    if (-not $curl) {
        throw 'curl.exe is required (bundled with Windows 10 1803+).'
    }
    return $curl.Source
}

function Get-LlamaAsset {
    param([string]$BuildVariant)

    $api = 'https://api.github.com/repos/ggml-org/llama.cpp/releases?per_page=15'
    try {
        $releases = Invoke-RestMethod -Uri $api -Headers @{ 'User-Agent' = 'HoverTrans-setup' } -TimeoutSec 30
        $release = $releases |
            Where-Object {
                $_.tag_name -match '^b\d+$' -and
                ($_.assets.name -match "llama-.*-bin-win-$BuildVariant-x64\.zip$")
            } |
            Select-Object -First 1

        if ($release) {
            $asset = $release.assets |
                Where-Object { $_.name -match "llama-.*-bin-win-$BuildVariant-x64\.zip$" } |
                Select-Object -First 1
            return [pscustomobject]@{
                Tag  = $release.tag_name
                Name = $asset.name
                Url  = $asset.browser_download_url
                Size = $asset.size
            }
        }
    }
    catch {
        Write-Warning "Could not query the latest llama.cpp release: $($_.Exception.Message). Using b12662."
    }

    $fallbackTag = 'b12662'
    return [pscustomobject]@{
        Tag  = $fallbackTag
        Name = "llama-$fallbackTag-bin-win-$BuildVariant-x64.zip"
        Url  = "https://github.com/ggml-org/llama.cpp/releases/download/$fallbackTag/llama-$fallbackTag-bin-win-$BuildVariant-x64.zip"
        Size = 0
    }
}

$curl = Get-Curl
$serverExe = Join-Path $runtime 'llama-server.exe'

if ($Force -or -not (Test-Path $serverExe)) {
    $asset = Get-LlamaAsset -BuildVariant $Variant
    Write-Host "==> llama.cpp $($asset.Tag) ($Variant)" -ForegroundColor Cyan
    Write-Host "    $($asset.Url)"

    $zip = Join-Path $env:TEMP $asset.Name
    & $curl -L --fail --retry 5 --retry-delay 2 --retry-all-errors --http1.1 --progress-bar -o $zip $asset.Url
    if ($LASTEXITCODE -ne 0) {
        Write-Warning "curl failed (exit code $LASTEXITCODE); retrying with Invoke-WebRequest."
        Invoke-WebRequest -Uri $asset.Url -OutFile $zip -UseBasicParsing -TimeoutSec 600
    }

    $extract = Join-Path $env:TEMP ("hovertrans-llama-" + [guid]::NewGuid().ToString('N'))
    Expand-Archive -LiteralPath $zip -DestinationPath $extract -Force
    $found = Get-ChildItem $extract -Recurse -Filter 'llama-server.exe' | Select-Object -First 1
    if (-not $found) {
        throw "No llama-server.exe in the archive: $zip"
    }

    # The release zip keeps everything in one folder; flatten it into runtime/.
    Copy-Item -Path (Join-Path $found.Directory.FullName '*') -Destination $runtime -Recurse -Force
    Remove-Item -LiteralPath $extract -Recurse -Force -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath $zip -Force -ErrorAction SilentlyContinue
    Write-Host "    -> $runtime" -ForegroundColor Green
}
else {
    Write-Host "==> llama-server already present: $serverExe (use -Force to update)" -ForegroundColor DarkGray
}

if (-not $SkipModel) {
    $modelPath = Join-Path $models 'Hy-MT2-1.8B-Q4_K_M.gguf'
    if ($Force -or -not (Test-Path $modelPath)) {
        # ModelScope is reachable from mainland China; Hugging Face is the fallback.
        $urls = @(
            'https://modelscope.cn/models/Tencent-Hunyuan/Hy-MT2-1.8B-GGUF/resolve/master/Hy-MT2-1.8B-Q4_K_M.gguf',
            'https://huggingface.co/tencent/Hy-MT2-1.8B-GGUF/resolve/main/Hy-MT2-1.8B-Q4_K_M.gguf?download=true'
        )

        $downloaded = $false
        foreach ($url in $urls) {
            Write-Host "==> Downloading Hy-MT2-1.8B-Q4_K_M (about 1.1 GB)" -ForegroundColor Cyan
            Write-Host "    $url"
            & $curl -L --fail --retry 5 --retry-delay 2 --retry-all-errors --http1.1 -C - --progress-bar -o $modelPath $url
            if ($LASTEXITCODE -eq 0) {
                $downloaded = $true
                break
            }

            try {
                Invoke-WebRequest -Uri $url -OutFile $modelPath -UseBasicParsing -TimeoutSec 1800
                $downloaded = $true
                break
            }
            catch {
                Write-Warning "Download failed, trying the next mirror: $($_.Exception.Message)"
            }
        }

        if (-not $downloaded) {
            throw 'Model download failed. Check the network, or download Hy-MT2-1.8B-Q4_K_M.gguf manually into models/.'
        }
    }
    else {
        Write-Host "==> Model already present: $modelPath (use -Force to re-download)" -ForegroundColor DarkGray
    }

    $size = (Get-Item $modelPath).Length
    if ($size -lt 900MB) {
        throw "The model file looks incomplete ($([math]::Round($size / 1MB)) MB). Delete it and run the script again."
    }

    $hash = (Get-FileHash -LiteralPath $modelPath -Algorithm SHA256).Hash.ToLowerInvariant()
    Write-Host ''
    Write-Host 'Done.' -ForegroundColor Green
    Write-Host "  llama-server: $serverExe"
    Write-Host "  model:        $modelPath"
    Write-Host "  sha256:       $hash"
    Write-Host ''
    Write-Host 'Run: dotnet run --project HoverTrans -f net11.0-windows10.0.19041.0'
}