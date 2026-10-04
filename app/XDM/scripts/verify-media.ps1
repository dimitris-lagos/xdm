param([Parameter(Mandatory=$true)][string]$MediaDirectory,[Parameter(Mandatory=$true)][string]$WorkDirectory,[string]$ExpectedYtdlpVersion='2026.08.19')
$ErrorActionPreference='Stop'
$media=[IO.Path]::GetFullPath($MediaDirectory)
$work=[IO.Path]::GetFullPath($WorkDirectory)
New-Item -ItemType Directory -Path $work -Force | Out-Null
$ff=Join-Path $media 'ffmpeg.exe'
function FF([string[]]$Arguments) {
    $log=Join-Path $work 'ffmpeg.log'
    & $ff -hide_banner -loglevel error @Arguments 2> $log
    if($LASTEXITCODE -ne 0){throw "FFmpeg failed ($LASTEXITCODE): $(Get-Content -LiteralPath $log -Raw)"}
}
$v=& $ff -version
if($LASTEXITCODE -ne 0){throw 'FFmpeg version check failed'}
$yt=& (Join-Path $media 'yt-dlp.exe') --version
if($LASTEXITCODE -ne 0 -or $yt -ne $ExpectedYtdlpVersion){throw 'yt-dlp version mismatch'}
$video=Join-Path $work 'video.mp4'
$audio=Join-Path $work 'audio.m4a'
FF @('-f','lavfi','-i','testsrc2=size=128x72:rate=10','-t','1','-an','-c:v','libx264',$video,'-y')
FF @('-f','lavfi','-i','sine=frequency=440:sample_rate=44100','-t','1','-c:a','aac',$audio,'-y')
# These commands intentionally match FFmpegMediaProcessor's argument order.
$merged=Join-Path $work 'merged.mp4'
FF @('-i',$video,'-i',$audio,'-acodec','copy','-vcodec','copy','-map','0','-map','1',$merged,'-y')
$mp3=Join-Path $work 'audio.mp3'
FF @('-i',$audio,'-acodec','libmp3lame',$mp3,'-y')
$seg=Join-Path $work 'segment.ts'
FF @('-i',$merged,'-c','copy','-f','mpegts',$seg,'-y')
$list=Join-Path $work 'segments.txt'
@("file '$($seg.Replace('\','/'))'","file '$($seg.Replace('\','/'))'") | Set-Content -LiteralPath $list -Encoding ascii
$hls=Join-Path $work 'hls.mp4'
FF @('-f','concat','-safe','0','-i',$list,'-auto_convert','1','-acodec','copy','-vcodec','copy',$hls,'-y')
foreach($file in @($merged,$mp3,$hls)) {
    if((Get-Item -LiteralPath $file).Length -le 0){throw "Empty output: $file"}
    FF @('-i',$file,'-f','null','NUL')
}
Write-Output "$($v[0]); yt-dlp $yt; merge, MP3, HLS and decoding passed"
