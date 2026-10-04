[CmdletBinding()]
param([string]$InstallDirectory='C:\Program Files (x86)\XDM')
$ErrorActionPreference='Stop'
$root=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$dist=Join-Path $root 'dist'
$result=Join-Path $dist 'install-result.json'
$log=Join-Path $dist 'setup.log'
try {
    $principal=[Security.Principal.WindowsPrincipal]::new([Security.Principal.WindowsIdentity]::GetCurrent())
    if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) { throw 'Run this script as Administrator.' }
    $installedExe=Join-Path $InstallDirectory 'xdm-app.exe'
    $running=@(Get-Process xdm-app -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $installedExe })
    if ($running.Count) {
        try { Invoke-RestMethod 'http://127.0.0.1:8597/args' -Method Post -Body '["--quit"]' -TimeoutSec 5 | Out-Null } catch { }
        foreach ($process in $running) { if (-not $process.WaitForExit(15000)) { throw 'XDM did not exit; installation was not started.' } }
    }
    $data=Join-Path $env:USERPROFILE '.xdm-app-data'
    $personal=@{}
    foreach ($file in @('downloads.db','settings.dat','~settings.dat')) {
        $path=Join-Path $data $file
        if (Test-Path -LiteralPath $path) { $personal[$path]=(Get-FileHash -LiteralPath $path).Hash }
    }
    $setup=Join-Path $dist 'XDM-Setup.exe'
    $process=Start-Process -FilePath $setup -ArgumentList @('/quiet','/norestart','/no-launch','/log',('"'+$log+'"')) -Wait -PassThru -WindowStyle Hidden
    if ($process.ExitCode -notin @(0,3010)) { throw "Installer failed: $($process.ExitCode). See $log" }
    $manifests=Get-Content (Join-Path $dist 'payload-manifest.json') -Raw | ConvertFrom-Json
    $arch=if([Environment]::Is64BitOperatingSystem){'x64'}else{'x86'}
    $manifest=$manifests.$arch
    foreach ($entry in $manifest) {
        $path=Join-Path $InstallDirectory $entry.path
        if (-not (Test-Path -LiteralPath $path) -or (Get-FileHash -LiteralPath $path).Hash -ne $entry.sha256) { throw "Installed payload mismatch: $($entry.path)" }
    }
    $config=Get-Content (Join-Path $root 'release-config.json') -Raw | ConvertFrom-Json
    $media=$config.media.architectures.$arch
    foreach($entry in @(@{name='ffmpeg.exe';sha256=$media.ffmpeg.binarySha256},@{name='yt-dlp.exe';sha256=$media.ytdlp.sha256})) {
        if((Get-FileHash -LiteralPath (Join-Path $InstallDirectory $entry.name)).Hash.ToLowerInvariant() -ne $entry.sha256){throw "Installed media checksum mismatch: $($entry.name)"}
    }
    & (Join-Path $PSScriptRoot 'cleanup-xdm.ps1') -Apply -IncludeInstallation -InstallDirectory $InstallDirectory | Out-File (Join-Path $dist 'cleanup.log')
    foreach ($path in $personal.Keys) {
        if (-not (Test-Path -LiteralPath $path) -or (Get-FileHash -LiteralPath $path).Hash -ne $personal[$path]) { throw "Personal data changed: $path" }
    }
    # Verify preserved data before XDM can update its settings or download database.
    $launchCommand="`$assembly=[Reflection.Assembly]::LoadFrom('"+$setup.Replace("'","''")+"'); `$assembly.GetType('XDM.Setup.Program').GetMethod('LaunchInstalledApp',[Reflection.BindingFlags]'Static,Public,NonPublic').Invoke(`$null,`$null) | Out-Null"
    $encodedCommand=[Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($launchCommand))
    $launcher=Start-Process -FilePath (Join-Path $env:WINDIR 'System32\WindowsPowerShell\v1.0\powershell.exe') -ArgumentList @('-NoProfile','-STA','-EncodedCommand',$encodedCommand) -WindowStyle Hidden -Wait -PassThru
    if($launcher.ExitCode -ne 0){throw 'Installation was verified, but XDM could not be launched.'}
    @{success=$true;exitCode=$process.ExitCode;version=(Get-Item $installedExe).VersionInfo.FileVersion;verifiedFiles=@($manifest).Count;personalDataUnchanged=$true} | ConvertTo-Json | Set-Content $result
} catch {
    @{success=$false;error=$_.Exception.Message} | ConvertTo-Json | Set-Content $result
    throw
}
