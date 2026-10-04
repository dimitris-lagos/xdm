[CmdletBinding()]
param([string]$ConfigPath='')
. (Join-Path $PSScriptRoot 'release-common.ps1')
$state=Get-Content -LiteralPath (Join-Path $ReleaseWork 'build-state.json') -Raw | ConvertFrom-Json
if(-not $state.testsPassed -or $state.version -ne $ReleaseConfig.version -or $state.fingerprint -ne (Get-ReleaseFingerprint)) { throw 'Source changed or executables were not verified. Run build-exe.ps1 first.' }
$work=Join-Path $ReleaseWork 'installer'
Reset-ReleaseDirectory $work
New-Item -ItemType Directory -Path $ReleaseOutput -Force | Out-Null
$wix=Join-Path $ReleaseWork 'dependencies\wix'
$manifests=@{}
$msiHashes=@{}
foreach($arch in $ReleaseConfig.architectures) {
    $stage=Join-Path $work "$arch\payload"
    New-Item -ItemType Directory -Path $stage -Force | Out-Null
    $build=Join-Path $ReleaseWork "build\$arch"
    $files=@('xdm-app.exe','xdm-app.exe.config','xdm-guide.exe','xdm-guide.exe.config','xdm-logo.ico',
        'Newtonsoft.Json.dll','System.Buffers.dll','System.Data.SQLite.dll','System.ValueTuple.dll',
        "$arch\SQLite.Interop.dll")
    foreach($file in $files){
        $dest=Join-Path $stage $file
        New-Item -ItemType Directory -Path (Split-Path $dest -Parent) -Force | Out-Null
        Copy-Item -LiteralPath (Join-Path $build $file) -Destination $dest
    }
    Copy-Item -LiteralPath (Join-Path $XdmRoot 'Lang') -Destination $stage -Recurse
    Copy-Item -LiteralPath (Join-Path $build 'images') -Destination $stage -Recurse
    foreach($folder in @('chrome-extension','download-controller-extension')) {
        $dest=Join-Path $stage $folder
        New-Item -ItemType Directory -Path $dest | Out-Null
        Get-ChildItem -LiteralPath (Join-Path $XdmRoot $folder) -File | Where-Object {
            $_.Extension -in @('.js','.mjs','.json','.html','.css','.png') -and $_.Name -ne 'package.json'
        } | ForEach-Object { Copy-Item -LiteralPath $_.FullName -Destination $dest }
    }
    Copy-Item -LiteralPath (Join-Path $RepoRoot 'LICENSE') -Destination (Join-Path $stage 'LICENSE.txt')
    $payload=@(Get-ChildItem -LiteralPath $stage -Recurse -File)
    if($payload | Where-Object Name -match '\.(db|dat|pdb|old|log)$'){throw 'Personal/debug files found in payload'}
    if($payload | Where-Object Name -match '^(ffmpeg.*\.exe|yt-dlp.*\.exe|FFmpeg-LICENSE\.txt)$'){throw 'Downloaded media must not be embedded in the installer'}
    $manifests[$arch]=@($payload | ForEach-Object {
        @{path=$_.FullName.Substring($stage.Length+1);sha256=(Get-FileHash -LiteralPath $_.FullName).Hash.ToLowerInvariant()}
    })
    $fragment=Join-Path $work "$arch\payload.wxs"
    Run-Checked (Join-Path $wix 'heat.exe') @('dir',$stage,'-nologo','-scom','-sreg','-srd','-ag','-cg','Payload','-dr','INSTALLFOLDER','-var','var.Stage','-out',$fragment)
    $win64=if($arch -eq 'x64'){'yes'}else{'no'}
    [xml]$xml=Get-Content -LiteralPath $fragment
    foreach($component in $xml.SelectNodes("//*[local-name()='Component']")) { $component.SetAttribute('Win64',$win64) }
    $xml.Save($fragment)
    $archWork=Join-Path $work $arch
    Run-Checked (Join-Path $wix 'candle.exe') @('-nologo','-arch',$arch,'-ext','WixUtilExtension',"-dArch=$arch","-dWin64=$win64","-dVersion=$($ReleaseConfig.version)","-dStage=$stage",'-out',($archWork+'\'),(Join-Path $XdmRoot 'XDM.Win.Installer\setup.wxs'),$fragment)
    $msi=Join-Path $work "xdm-$arch.msi"
    Run-Checked (Join-Path $wix 'light.exe') @('-nologo','-spdb','-ext','WixUtilExtension','-sval','-out',$msi,(Join-Path $archWork 'setup.wixobj'),(Join-Path $archWork 'payload.wixobj'))
    $msiHashes[$arch]=(Get-FileHash -LiteralPath $msi).Hash.ToLowerInvariant()
}
$msiHashes | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $work 'package-hashes.json') -Encoding UTF8
$manifest=Join-Path $ReleaseOutput 'payload-manifest.json'
$manifests | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $manifest -Encoding UTF8
$v=$ReleaseConfig.version
$launcher=Join-Path $work 'launcher'
$feed=Join-Path $XdmRoot 'release-assets\nuget'
Run-Checked 'dotnet' @('build',(Join-Path $XdmRoot 'XDM.Setup\XDM.Setup.csproj'),'-c','Release','-o',$launcher,'-v:quiet',"/p:InstallerWork=$work","/p:ReleaseConfigPath=$ConfigPath","/p:Version=$v","/p:AssemblyVersion=$v.0","/p:FileVersion=$v.0","/p:RestoreSources=$feed")
$setup=Join-Path $ReleaseOutput 'XDM-Setup.exe'
Copy-Item -LiteralPath (Join-Path $launcher 'XDM-Setup.exe') -Destination $setup -Force
# The launcher is framework-only; it needs no external DLL/config.
@($setup,$manifest) | ForEach-Object { (Get-FileHash -LiteralPath $_).Hash.ToLowerInvariant()+'  '+[IO.Path]::GetFileName($_) } | Set-Content -LiteralPath (Join-Path $ReleaseOutput 'SHA256SUMS.txt') -Encoding ascii
@{version=$v;fingerprint=(Get-ReleaseFingerprint);sha256=(Get-FileHash -LiteralPath $setup).Hash.ToLowerInvariant()} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $ReleaseWork 'installer-state.json')
Write-Output "Installer: $setup"
Write-Output 'Silent install: XDM-Setup.exe /quiet /norestart /log setup.log'
Write-Output 'Both modes download the pinned media versions for the operating-system architecture.'
