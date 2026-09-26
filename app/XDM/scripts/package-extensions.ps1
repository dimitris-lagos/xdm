[CmdletBinding()]
param(
    [string]$OutputDirectory = (Join-Path $PSScriptRoot '..\packages')
)

$ErrorActionPreference = 'Stop'
$xdmRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$stagingRoot = Join-Path $xdmRoot 'obj\extension-packaging'
$resolvedOutput = [System.IO.Path]::GetFullPath($OutputDirectory)

$packages = @(
    @{
        Name = 'xdm-integration-module-chromium'
        Source = Join-Path $xdmRoot 'chrome-extension'
        Files = @(
            'manifest.json',
            'app.js', 'connector.js', 'extension-state.mjs', 'logger.js', 'main.js', 'media-filter.mjs', 'request-watcher.js',
            'popup.html', 'popup.js', 'styles.css',
            'error.html', 'error.js', 'disabled.html',
            'register.html', 'register.js', 'xdm-logo-orange.png',
            'icon16.png', 'icon16-mono.png', 'icon48.png', 'icon48-mono.png', 'icon128.png', 'icon128-mono.png'
        )
    },
    @{
        Name = 'xdm-download-controller-chromium'
        Source = Join-Path $xdmRoot 'download-controller-extension'
        Files = @(
            'manifest.json',
            'api.js', 'controller-state.js', 'download-scope.js', 'service-worker.js',
            'popup.html', 'popup.js', 'popup.css',
            'icon16.png', 'icon48.png', 'icon128.png'
        )
    }
)

New-Item -ItemType Directory -Path $resolvedOutput -Force | Out-Null
New-Item -ItemType Directory -Path $stagingRoot -Force | Out-Null

foreach ($package in $packages) {
    Get-ChildItem -LiteralPath $resolvedOutput -Filter ($package.Name + '-v*.zip') -File -ErrorAction SilentlyContinue |
        Remove-Item -Force
}

$artifacts = foreach ($package in $packages) {
    $manifestPath = Join-Path $package.Source 'manifest.json'
    $manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
    if ([string]::IsNullOrWhiteSpace($manifest.version)) {
        throw "Manifest has no version: $manifestPath"
    }

    $stage = Join-Path $stagingRoot $package.Name
    if (Test-Path -LiteralPath $stage) {
        $resolvedStage = (Resolve-Path -LiteralPath $stage).Path
        if (-not $resolvedStage.StartsWith($stagingRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
            throw "Unsafe staging path: $resolvedStage"
        }
        Remove-Item -LiteralPath $resolvedStage -Recurse -Force
    }
    New-Item -ItemType Directory -Path $stage | Out-Null

    foreach ($relativeFile in $package.Files) {
        $sourceFile = Join-Path $package.Source $relativeFile
        if (-not (Test-Path -LiteralPath $sourceFile -PathType Leaf)) {
            throw "Required extension file is missing: $sourceFile"
        }
        $destinationFile = Join-Path $stage $relativeFile
        $destinationFolder = Split-Path -Parent $destinationFile
        New-Item -ItemType Directory -Path $destinationFolder -Force | Out-Null
        Copy-Item -LiteralPath $sourceFile -Destination $destinationFile -Force
    }

    $artifact = Join-Path $resolvedOutput ("{0}-v{1}.zip" -f $package.Name, $manifest.version)
    if (Test-Path -LiteralPath $artifact) {
        Remove-Item -LiteralPath $artifact -Force
    }
    Compress-Archive -Path (Join-Path $stage '*') -DestinationPath $artifact -CompressionLevel Optimal
    Get-Item -LiteralPath $artifact
}

$checksumLines = $artifacts | ForEach-Object {
    $hash = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
    "$hash  $($_.Name)"
}
Set-Content -LiteralPath (Join-Path $resolvedOutput 'SHA256SUMS.txt') -Value $checksumLines -Encoding ascii

$artifacts | Select-Object Name, Length, FullName
