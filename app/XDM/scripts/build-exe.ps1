[CmdletBinding()]
param([string]$ConfigPath='')
. (Join-Path $PSScriptRoot 'release-common.ps1')
Prepare-ReleaseAssets
$feed=Join-Path $XdmRoot 'release-assets\nuget'
foreach($arch in $ReleaseConfig.architectures) {
    $build=Join-Path $ReleaseWork "build\$arch"
    Reset-ReleaseDirectory $build
    $v=$ReleaseConfig.version
    Run-Checked 'dotnet' @('build',(Join-Path $XdmRoot 'XDM.Wpf.UI\XDM.Wpf.UI.csproj'),'-c','Release','-o',$build,'-v:quiet',"/p:Platform=$arch","/p:PlatformTarget=$arch",'/p:WarningLevel=0',"/p:Version=$v","/p:AssemblyVersion=$v.0","/p:FileVersion=$v.0","/p:RestoreSources=$feed","/p:RestorePackagesPath=$ReleaseWork\nuget")
    foreach($name in @('ffmpeg.exe','yt-dlp.exe','FFmpeg-LICENSE.txt')) {
        Copy-Item -LiteralPath (Join-Path $ReleaseWork "dependencies\$arch\$name") -Destination $build
    }
    & (Join-Path $PSScriptRoot 'verify-media.ps1') -MediaDirectory $build -WorkDirectory (Join-Path $ReleaseWork "verification\$arch") -ExpectedYtdlpVersion $ReleaseConfig.media.ytdlp.version
    # Load SQLite with the same process architecture as the installed application.
    $hostExe=if($arch -eq 'x86'){Join-Path $env:WINDIR 'SysWOW64\WindowsPowerShell\v1.0\powershell.exe'}else{Join-Path $env:WINDIR 'System32\WindowsPowerShell\v1.0\powershell.exe'}
    Run-Checked $hostExe @('-NoProfile','-ExecutionPolicy','Bypass','-File',(Join-Path $PSScriptRoot 'verify-sqlite.ps1'),'-RuntimeDirectory',$build,'-Architecture',$arch,'-Version',$v,'-WorkDirectory',(Join-Path $ReleaseWork "verification\$arch\sqlite"))
}
Run-Checked 'dotnet' @('test',(Join-Path $XdmRoot 'XDM.Controller.Tests\XDM.Controller.Tests.csproj'),'-c','Release','-v:quiet',"/p:RestoreSources=$feed","/p:RestorePackagesPath=$ReleaseWork\nuget")
foreach($folder in @('chrome-extension','download-controller-extension')) {
    $tests=@(Get-ChildItem -LiteralPath (Join-Path $XdmRoot "$folder\tests") -File | Where-Object Extension -in '.js','.mjs' | ForEach-Object FullName)
    Run-Checked 'node' (@('--test')+$tests)
}
@{version=$ReleaseConfig.version;fingerprint=(Get-ReleaseFingerprint);testsPassed=$true} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $ReleaseWork 'build-state.json')
Write-Output 'Both architectures built and verified.'
