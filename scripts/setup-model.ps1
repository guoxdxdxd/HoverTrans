#!/usr/bin/env powershell
<#
.SYNOPSIS
    Source-checkout shortcut: fetch llama.cpp and the Hy-MT2 1.8B model for HoverTrans.

.DESCRIPTION
    Both go to %LOCALAPPDATA%\HoverTrans, which is where the app looks by default
    (HoverTrans/Core/Platform/AppPaths.cs).

    The real logic lives in packaging\deploy-model.ps1 — the installer runs the same script, so a
    fixed mirror or a renamed llama.cpp asset only has to be fixed in one place. See
    packaging\README.md for the installer and the pinned versions.

.PARAMETER Variant
    llama.cpp Windows build: cpu (default, runs everywhere), cuda (NVIDIA GPU), vulkan (modern
    AMD/Intel GPU).

.PARAMETER SkipModel
    Only fetch/update llama-server.

.PARAMETER Force
    Download again even when the files are already present.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File ./scripts/setup-model.ps1

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File ./scripts/setup-model.ps1 -Variant cuda
#>
[CmdletBinding()]
param(
    [ValidateSet('cpu', 'cuda', 'vulkan')]
    [string]$Variant = 'cpu',
    [switch]$SkipModel,
    [switch]$Force
)

$deploy = Join-Path (Split-Path -Parent $PSScriptRoot) 'packaging\deploy-model.ps1'
if (-not (Test-Path -LiteralPath $deploy)) {
    throw "找不到打包脚本：$deploy"
}

$forward = @{
    Variant = $Variant
    Force   = [bool]$Force
}
if ($SkipModel) { $forward['SkipModel'] = $true }

& $deploy @forward
exit $LASTEXITCODE
