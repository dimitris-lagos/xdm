[CmdletBinding()]
param([string]$ConfigPath='', [switch]$DryRun, [switch]$Draft)
. (Join-Path $PSScriptRoot 'release-common.ps1')
$notes=Join-Path $XdmRoot $ReleaseConfig.github.notes
if(-not (Test-Path -LiteralPath $notes)){throw 'Missing release notes'}
$tag='v'+$ReleaseConfig.version
if(-not $DryRun) {
    $dirty=@(& git -C $RepoRoot status --porcelain)
    if($LASTEXITCODE -ne 0 -or $dirty.Count){throw 'Commit the reviewed release inputs before publishing.'}
    $remoteUrl=& git -C $RepoRoot remote get-url $ReleaseConfig.github.remote
    if($LASTEXITCODE -ne 0 -or $remoteUrl -notmatch [regex]::Escape($ReleaseConfig.github.repository)){throw 'Configured remote does not match release repository'}
    Run-Checked 'gh' @('auth','status')
    $existing=& git -C $RepoRoot tag --list $tag
    if($existing){throw "Tag already exists: $tag"}
}
& (Join-Path $PSScriptRoot 'build-exe.ps1') -ConfigPath $ConfigPath
& (Join-Path $PSScriptRoot 'build-installer.ps1') -ConfigPath $ConfigPath
if($DryRun){Write-Output "Dry run complete: $tag built and verified; no GitHub changes.";return}
Run-Checked 'git' @('-C',$RepoRoot,'push',$ReleaseConfig.github.remote,'HEAD')
Run-Checked 'git' @('-C',$RepoRoot,'tag','-a',$tag,'-m',"XDM $($ReleaseConfig.version)")
Run-Checked 'git' @('-C',$RepoRoot,'push',$ReleaseConfig.github.remote,$tag)
$args=@('release','create',$tag,(Join-Path $ReleaseOutput 'XDM-Setup.exe'),(Join-Path $ReleaseOutput 'SHA256SUMS.txt'),(Join-Path $ReleaseOutput 'payload-manifest.json'),'--verify-tag','--repo',$ReleaseConfig.github.repository,'--title',"XDM $($ReleaseConfig.version) Beta",'--prerelease','--notes-file',$notes)
if($Draft){$args+='--draft'}
Run-Checked 'gh' $args
