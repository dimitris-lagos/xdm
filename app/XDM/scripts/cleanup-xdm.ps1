[CmdletBinding(SupportsShouldProcess)]
param([switch]$Apply, [switch]$IncludeInstallation, [string]$InstallDirectory='C:\Program Files (x86)\XDM')
$ErrorActionPreference='Stop'
$root=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$targets=[Collections.Generic.List[string]]::new()
function AddTarget([string]$path,[string]$boundary) {
    if (-not (Test-Path -LiteralPath $path)) { return }
    $full=(Resolve-Path -LiteralPath $path).Path
    $base=[IO.Path]::GetFullPath($boundary).TrimEnd('\')+'\'
    if (-not $full.StartsWith($base,[StringComparison]::OrdinalIgnoreCase)) { throw "Outside cleanup boundary: $full" }
    $item=Get-Item -LiteralPath $full -Force
    if ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw "Refusing linked cleanup target: $full" }
    $targets.Add($full)
}
$packages=Join-Path $root 'packages'
Get-ChildItem -LiteralPath $packages -Force -ErrorAction SilentlyContinue | Where-Object {
    $_.Name -match '^xdm-(integration-module|download-controller)-chromium-v' -or $_.Name -eq 'SHA256SUMS.txt'
} | ForEach-Object { AddTarget $_.FullName $packages }
$obj=Join-Path $root 'obj'
if (Test-Path -LiteralPath $obj) {
    Get-ChildItem -LiteralPath $obj -Force | Where-Object { $_.Name -ne 'release' } |
        ForEach-Object { AddTarget $_.FullName $obj }
}
# Preserve the current release build; old compiler/media caches are redundant.
Get-ChildItem -LiteralPath $root -Directory | Where-Object { Get-ChildItem -LiteralPath $_.FullName -Filter '*.csproj' -File } | ForEach-Object {
    foreach ($name in @('bin','obj','TestResults')) { AddTarget (Join-Path $_.FullName $name) $_.FullName }
}
if ($IncludeInstallation) {
    $install=[IO.Path]::GetFullPath($InstallDirectory)
    Get-ChildItem -LiteralPath $install -Directory | Where-Object { $_.Name -like 'backup-before-*' } |
        ForEach-Object { AddTarget $_.FullName $install }
    foreach ($name in @('Font','Gif','Icon','xdm-guide.pdb','yt-dlp_x86.exe.old','System.IO.Compression.dll','ffmpeg-x86.exe','yt-dlp_x86.exe')) {
        AddTarget (Join-Path $install $name) $install
    }
    $otherArch=if([Environment]::Is64BitOperatingSystem){'x86'}else{'x64'}
    AddTarget (Join-Path $install $otherArch) $install
    $data=Join-Path $env:USERPROFILE '.xdm-app-data'
    AddTarget (Join-Path $data 'chrome-extension') $data
}
foreach ($target in $targets) {
    Write-Output $target
    if ($Apply -and $PSCmdlet.ShouldProcess($target,'Delete stale XDM artifact')) {
        Remove-Item -LiteralPath $target -Recurse -Force
    }
}
