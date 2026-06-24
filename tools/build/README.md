# One-shot build environment

`setup.ps1` provisions the **entire** toolchain to build **AAX Audio Converter** from a fresh
clone and (optionally) produce the Windows installer — in a single command, on **any drive**.

It exists because a clean checkout cannot be built as-is: the app project copies `ffmpeg.exe`
and `ffmpeg64.exe` to its output, but those binaries are not in the repo, and the solution
needs MSBuild + the .NET Framework 4.8 targeting pack, NuGet restore, and Inno Setup for the
installer. This script handles all of it.

> **Windows only.** The app targets .NET Framework 4.8 (WinForms) and cannot be built on Linux/macOS.

## Quick start

From an **elevated** (Administrator) prompt — needed the first time so the script can install
VS Build Tools and Inno Setup:

```bat
tools\build\build.cmd
```

or directly:

```powershell
powershell -ExecutionPolicy Bypass -File tools\build\setup.ps1
```

This installs everything, restores packages, builds **Release**, and compiles the installer.
Results:

- App: `src\AaxAudioConverter\bin\Release\AaxAudioConverter.exe`
- Installer: `src\InnoSetup\Setup\AaxAudioConverter-<version>-Setup.exe`

## Put the whole toolchain on another drive (e.g. D:) — keep C: clean

```powershell
tools\build\build.cmd -ToolsRoot D:\aax-tools -VsInstallPath D:\aax-tools\VSBuildTools
```

- `-ToolsRoot` — where portable tools go (nuget.exe, ffmpeg, Inno Setup, vswhere). Default:
  `tools\build\.tools` inside the repo.
- `-VsInstallPath` — where **VS Build Tools 2022** is installed. Default: `<ToolsRoot>\VSBuildTools`.

All other paths are resolved relative to the repository, so the repo itself can live on any drive.

## Already have Visual Studio? Run admin-free

If VS 2022 / Build Tools with the .NET desktop build tools + 4.8 targeting pack is already
installed, skip the heavy installs (no admin needed):

```powershell
tools\build\setup.ps1 -SkipVsBuildTools -SkipInstaller
```

`vswhere` auto-detects the existing instance; the script only fetches nuget/ffmpeg, restores,
and builds.

## Parameters

| Parameter | Default | Purpose |
|---|---|---|
| `-ToolsRoot` | `tools\build\.tools` | Portable tools location (any drive). |
| `-VsInstallPath` | `<ToolsRoot>\VSBuildTools` | VS Build Tools install dir (any drive). |
| `-Configuration` | `Release` | Build configuration. |
| `-Platform` | `Any CPU` | Build platform. |
| `-FfmpegUrl` | gyan.dev win64 zip | Where to fetch ffmpeg; override to pin a version. |
| `-SkipVsBuildTools` | off | Use an already-installed MSBuild (fails if none found). |
| `-SkipInstaller` | off | Build the app only; no Inno Setup install/compile. |
| `-NoBuild` | off | Provision tools only, don't build. |
| `-Force` | off | Re-download/replace tools even if present. |

## Notes

- **Idempotent**: re-running reuses already-downloaded tools and skips completed steps. Use
  `-Force` to refresh.
- **ffmpeg**: the project references both `ffmpeg.exe` and `ffmpeg64.exe`. The script places the
  downloaded 64-bit static build under **both** names (fine for an x64 build/run). To use your
  own builds, drop your `ffmpeg.exe` / `ffmpeg64.exe` into `src\AaxAudioConverter\` before running,
  or pass `-FfmpegUrl`.
- **Admin / UAC**: installing VS Build Tools and Inno Setup needs elevation. If you start the
  script non-elevated, it launches those two installers via UAC prompts; the rest runs unelevated.
- Downloaded tools and the placed ffmpeg binaries are git-ignored (see the repo `.gitignore`),
  so nothing large is committed.
