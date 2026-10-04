$ErrorActionPreference = 'Stop'
$xdmRoot = Split-Path $PSScriptRoot -Parent
$assemblyPath = Join-Path $xdmRoot 'XDM.Wpf.UI\bin\Release\net4.7.2\xdm-app.exe'
[Reflection.Assembly]::LoadFrom((Join-Path (Split-Path $assemblyPath) 'Newtonsoft.Json.dll')) | Out-Null
$assembly = [Reflection.Assembly]::LoadFrom($assemblyPath)
$config = $assembly.GetType('XDM.Core.Config')
$config.GetProperty('AppDir').SetValue($null, 'C:\Program Files (x86)\XDM')
$process = [Activator]::CreateInstance($assembly.GetType('YDLWrapper.YDLProcess'))
$process.Uri = [Uri]'https://www.youtube.com/watch?v=jNQXAC9IVRw'
$process.SingleVideo = $true
$process.Start()
try {
    $catalog = $assembly.GetType('XDM.Core.BrowserMonitoring.YouTubeFormatCatalog')
    $method = $catalog.GetMethod('Parse', [Reflection.BindingFlags]'Static,NonPublic')
    $choices = $method.Invoke($null, @([IO.File]::ReadAllText($process.JsonOutputFile)))
    $video = @($choices | Where-Object { -not $_.AudioOnly })
    $audio = @($choices | Where-Object AudioOnly)
    if ($video.Count -eq 0 -or $audio.Count -eq 0) { throw 'Missing video/audio choices' }
    if (@($video | Where-Object { -not $_.AudioUrl -and $_.Quality -match 'none' }).Count -gt 0) { throw 'Invalid pairing' }
    Write-Output "Real XDM wrapper extraction: $($video.Count) video choices, $($audio.Count) audio choices"
    $choices | Select-Object Extension,Quality,Hls,AudioOnly | Format-Table
} finally {
    Remove-Item -LiteralPath $process.JsonOutputFile -Force
}
