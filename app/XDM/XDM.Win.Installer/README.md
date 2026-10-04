# Windows installer

setup.wxs defines the MSI. scripts/build-installer.ps1 harvests an explicit runtime allowlist for each architecture, builds x86/x64 MSIs with the vendored WiX compiler, then embeds them in XDM.Setup. The launcher selects the operating-system architecture and offers two checked download options and downloads the selected pinned tested media concurrently. Unchecked components show an inline manual-install warning. The fixed-size second page shows download progress followed by Windows Installer progress and an explicit Finish button. Neither yt-dlp nor FFmpeg is included in the MSI/EXE payload.

Build EXEs first with ../scripts/build-exe.ps1. Configuration: ../release-config.json. Outputs: ../obj/release and ../dist. Packaging refuses source changes after verification.

Silent: XDM-Setup.exe /quiet /norestart /log setup.log. Silent mode also downloads the pinned versions automatically, choosing x86/x64 from the OS. Use /skip-yt-dlp and/or /skip-ffmpeg to omit individual downloads. Selected downloads and checksums must succeed before installation starts. Exit codes: 0 success, 3010 success/restart required, 1 bootstrapper/download failure. MSI errors are recorded in the log. Run elevated. No database restore or copy occurs.
