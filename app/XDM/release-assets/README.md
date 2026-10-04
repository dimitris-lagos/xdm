# Release inputs

Pinned media used only for build/runtime verification and the WiX 3.14.1 compiler are stored here so packaging never reads an installed XDM or a personal download database. FFmpeg ZIPs contain only ffmpeg.exe and its license; the original upstream archive URL and both archive/executable hashes are pinned in ../release-config.json. NuGet packages form the offline build feed. Review dependency licenses inside the archives/packages before redistribution.

Media copies in this folder are test inputs only. The installer excludes them and downloads the matching OS architecture from the pinned upstream URLs.
