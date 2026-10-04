$ErrorActionPreference = 'Stop'
$XdmRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$RepoRoot = [IO.Path]::GetFullPath((Join-Path $XdmRoot '..\..'))
if (-not $ConfigPath) { $ConfigPath = Join-Path $XdmRoot 'release-config.json' }
$ConfigPath = [IO.Path]::GetFullPath($ConfigPath)
$ReleaseConfig = Get-Content -LiteralPath $ConfigPath -Raw | ConvertFrom-Json
$ReleaseWork = Join-Path $XdmRoot 'obj\release'
$ReleaseOutput = Join-Path $XdmRoot 'dist'
if ($ReleaseConfig.version -notmatch '^\d+\.\d+\.\d+$') { throw 'Invalid release version' }
if (@($ReleaseConfig.architectures) -join ',' -ne 'x86,x64') { throw 'Both x86 and x64 are required' }
function Run-Checked([string]$Executable, [string[]]$Arguments) {
    & $Executable @Arguments
    if ($LASTEXITCODE -ne 0) { throw "$Executable exited with $LASTEXITCODE" }
}
function Reset-ReleaseDirectory([string]$Directory) {
    $target = [IO.Path]::GetFullPath($Directory)
    if (-not $target.StartsWith($ReleaseWork + '\', [StringComparison]::OrdinalIgnoreCase)) { throw "Unsafe build path: $target" }
    if (Test-Path -LiteralPath $target) {
        $item = Get-Item -LiteralPath $target
        if ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Build directory is a link' }
        Remove-Item -LiteralPath $target -Recurse -Force
    }
    New-Item -ItemType Directory -Path $target -Force | Out-Null
}
function Resolve-ReleaseAsset($Asset) {
    $base = Join-Path $XdmRoot 'release-assets'
    $file = [IO.Path]::GetFullPath((Join-Path $base $Asset.asset))
    if (-not $file.StartsWith($base + '\',[StringComparison]::OrdinalIgnoreCase)) { throw 'Invalid asset path' }
    if ((Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash.ToLowerInvariant() -ne $Asset.sha256) { throw "Asset checksum mismatch: $file" }
    return $file
}
function Prepare-ReleaseAssets {
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    foreach ($arch in $ReleaseConfig.architectures) {
        $dir = Join-Path $ReleaseWork "dependencies\$arch"
        Reset-ReleaseDirectory $dir
        $media = $ReleaseConfig.media.architectures.$arch
        [IO.Compression.ZipFile]::ExtractToDirectory((Resolve-ReleaseAsset $media.ffmpeg),$dir)
        Copy-Item -LiteralPath (Resolve-ReleaseAsset $media.ytdlp) -Destination (Join-Path $dir 'yt-dlp.exe')
        Move-Item -LiteralPath (Join-Path $dir 'LICENSE.txt') -Destination (Join-Path $dir 'FFmpeg-LICENSE.txt')
    }
    $wix = Join-Path $ReleaseWork 'dependencies\wix'
    Reset-ReleaseDirectory $wix
    [IO.Compression.ZipFile]::ExtractToDirectory((Resolve-ReleaseAsset $ReleaseConfig.wix),$wix)
}
function Get-ReleaseFingerprint {
    $rows = Get-ChildItem -LiteralPath $XdmRoot -Recurse -File | Where-Object {
        $_.FullName -notmatch '\\(obj|bin|dist|recovery|release-assets)\\' -and
        $_.Extension -in @('.cs','.csproj','.projitems','.xaml','.json','.js','.mjs','.html','.css','.ps1','.wxs','.txt','.md','.manifest')
    } | Sort-Object FullName | ForEach-Object {
        $_.FullName.Substring($XdmRoot.Length) + ':' + (Get-FileHash -LiteralPath $_.FullName).Hash
    }
    $sha = [Security.Cryptography.SHA256]::Create()
    try { return ([BitConverter]::ToString($sha.ComputeHash([Text.Encoding]::UTF8.GetBytes(($rows -join [Environment]::NewLine))))).Replace('-','').ToLowerInvariant() }
    finally { $sha.Dispose() }
}
