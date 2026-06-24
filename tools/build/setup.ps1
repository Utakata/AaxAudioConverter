<#
.SYNOPSIS
  One-shot, drive-portable build environment setup for AAX Audio Converter.

.DESCRIPTION
  Provisions the full toolchain needed to build the .NET Framework 4.8 WinForms
  solution from a fresh clone and (optionally) produces the Inno Setup installer:

    1. MSBuild + .NET Framework 4.8 targeting pack (VS Build Tools 2022, auto-installed)
    2. nuget.exe (portable) + package restore
    3. ffmpeg.exe / ffmpeg64.exe placed next to the main project (required by the csproj)
    4. Inno Setup 6 + installer compilation

  Everything is resolved relative to the repository (via $PSScriptRoot), so it works on
  any drive. Portable tools land under -ToolsRoot and VS Build Tools under -VsInstallPath,
  both of which may point at e.g. D:\ to keep C:\ clean.

.EXAMPLE
  # Default: tools under <repo>\tools\build\.tools, build Release + installer
  powershell -ExecutionPolicy Bypass -File tools\build\setup.ps1

.EXAMPLE
  # Put the whole toolchain on D:\
  powershell -ExecutionPolicy Bypass -File tools\build\setup.ps1 `
    -ToolsRoot D:\aax-tools -VsInstallPath D:\aax-tools\VSBuildTools

.EXAMPLE
  # VS already installed, just restore + build the app (no admin, no installer)
  powershell -ExecutionPolicy Bypass -File tools\build\setup.ps1 -SkipVsBuildTools -SkipInstaller
#>
[CmdletBinding()]
param(
  [string] $ToolsRoot,
  [string] $VsInstallPath,
  [string] $Configuration = 'Release',
  [string] $Platform = 'Any CPU',
  [string] $FfmpegUrl = 'https://github.com/GyanD/codexffmpeg/releases/download/7.1/ffmpeg-7.1-essentials_build.zip',
  [switch] $SkipVsBuildTools,
  [switch] $SkipInstaller,
  [switch] $NoBuild,
  [switch] $Force
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12

# --- paths -------------------------------------------------------------------
$RepoRoot   = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$SrcDir     = Join-Path $RepoRoot 'src'
$Solution   = Join-Path $SrcDir 'AAX Audio Converter.sln'
$AppProjDir = Join-Path $SrcDir 'AaxAudioConverter'
$IssFile    = Join-Path $SrcDir 'InnoSetup\AaxAudioConverter setup.iss'

if (-not $ToolsRoot)     { $ToolsRoot     = Join-Path $PSScriptRoot '.tools' }
if (-not $VsInstallPath) { $VsInstallPath = Join-Path $ToolsRoot 'VSBuildTools' }

# --- helpers -----------------------------------------------------------------
function Write-Step ([string] $Message) { Write-Host "`n==> $Message" -ForegroundColor Cyan }
function Write-Info ([string] $Message) { Write-Host "    $Message" -ForegroundColor DarkGray }

function New-Dir ([string] $Path) {
  if (-not (Test-Path -LiteralPath $Path)) { New-Item -ItemType Directory -Path $Path -Force | Out-Null }
}

function Get-File ([string] $Url, [string] $OutFile) {
  if ((Test-Path -LiteralPath $OutFile) -and -not $Force) {
    Write-Info "exists, skip download: $OutFile"
    return
  }
  Write-Info "download $Url"
  $tmp = "$OutFile.partial"
  $oldPref = $ProgressPreference
  $ProgressPreference = 'SilentlyContinue'   # faster Invoke-WebRequest
  try {
    Invoke-WebRequest -Uri $Url -OutFile $tmp -UseBasicParsing
    Move-Item -LiteralPath $tmp -Destination $OutFile -Force
  } finally {
    $ProgressPreference = $oldPref
    if (Test-Path -LiteralPath $tmp) { Remove-Item -LiteralPath $tmp -Force -ErrorAction SilentlyContinue }
  }
}

function Test-Admin {
  $id = [Security.Principal.WindowsIdentity]::GetCurrent()
  (New-Object Security.Principal.WindowsPrincipal($id)).IsInRole(
    [Security.Principal.WindowsBuiltInRole]::Administrator)
}

# --- pre-flight --------------------------------------------------------------
if ($PSVersionTable.PSVersion.Major -lt 5) { throw 'PowerShell 5.0 or newer is required.' }
if (-not ($IsWindowsPlatform = ($env:OS -eq 'Windows_NT'))) {
  throw 'This build targets .NET Framework 4.8 (WinForms) and can only be built on Windows.'
}
if (-not (Test-Path -LiteralPath $Solution)) { throw "Solution not found: $Solution" }

Write-Step 'AAX Audio Converter — build environment setup'
Write-Info "RepoRoot      : $RepoRoot"
Write-Info "ToolsRoot     : $ToolsRoot"
Write-Info "VsInstallPath : $VsInstallPath"
Write-Info "Configuration : $Configuration | Platform: $Platform"
New-Dir $ToolsRoot

# --- 1. MSBuild (VS Build Tools 2022) ---------------------------------------
function Find-MSBuild {
  # Prefer a real VS / Build Tools instance that carries the v4.8 targeting pack.
  $vswhere = Join-Path $ToolsRoot 'vswhere.exe'
  Get-File 'https://github.com/microsoft/vswhere/releases/latest/download/vswhere.exe' $vswhere

  $candidates = @()
  $candidates += & $vswhere -products * -latest -prerelease `
    -requires Microsoft.Component.MSBuild `
    -find 'MSBuild\**\Bin\MSBuild.exe' 2>$null

  # Explicit install path (in case vswhere has not indexed it yet)
  $candidates += Join-Path $VsInstallPath 'MSBuild\Current\Bin\MSBuild.exe'

  foreach ($c in $candidates) {
    if ($c -and (Test-Path -LiteralPath $c)) { return $c }
  }
  return $null
}

Write-Step 'Locating MSBuild'
$msbuild = Find-MSBuild
if ($msbuild) {
  Write-Info "found: $msbuild"
} elseif ($SkipVsBuildTools) {
  throw 'MSBuild not found and -SkipVsBuildTools was specified. Install Visual Studio / Build Tools 2022 with the .NET desktop build tools and the .NET Framework 4.8 targeting pack, then re-run.'
} else {
  Write-Step 'Installing Visual Studio Build Tools 2022 (this requires administrator rights)'
  $bootstrapper = Join-Path $ToolsRoot 'vs_BuildTools.exe'
  Get-File 'https://aka.ms/vs/17/release/vs_BuildTools.exe' $bootstrapper

  $vsArgLine = "--installPath `"$VsInstallPath`"" +
    ' --add Microsoft.VisualStudio.Workload.ManagedDesktopBuildTools' +
    ' --add Microsoft.Net.Component.4.8.SDK' +
    ' --add Microsoft.Net.Component.4.8.TargetingPack' +
    ' --includeRecommended --quiet --norestart --wait --nocache'

  if (Test-Admin) {
    Write-Info "$bootstrapper $vsArgLine"
    $p = Start-Process -FilePath $bootstrapper -ArgumentList $vsArgLine -Wait -PassThru
  } else {
    Write-Info 'Re-launching the VS Build Tools installer elevated (UAC prompt)...'
    $p = Start-Process -FilePath $bootstrapper -ArgumentList $vsArgLine -Verb RunAs -Wait -PassThru
  }
  # 0 = ok, 3010 = ok but reboot recommended
  if ($p.ExitCode -ne 0 -and $p.ExitCode -ne 3010) {
    throw "VS Build Tools installer exited with code $($p.ExitCode)."
  }
  $msbuild = Find-MSBuild
  if (-not $msbuild) { throw "MSBuild still not found after installing VS Build Tools to $VsInstallPath." }
  Write-Info "found: $msbuild"
}

# --- 2. nuget.exe ------------------------------------------------------------
Write-Step 'Fetching nuget.exe'
$nuget = Join-Path $ToolsRoot 'nuget.exe'
Get-File 'https://dist.nuget.org/win-x86-commandline/latest/nuget.exe' $nuget

# --- 3. ffmpeg.exe / ffmpeg64.exe -------------------------------------------
Write-Step 'Provisioning ffmpeg (required by the build)'
$ffmpegExe   = Join-Path $AppProjDir 'ffmpeg.exe'
$ffmpeg64Exe = Join-Path $AppProjDir 'ffmpeg64.exe'
if ((Test-Path -LiteralPath $ffmpegExe) -and (Test-Path -LiteralPath $ffmpeg64Exe) -and -not $Force) {
  Write-Info 'ffmpeg.exe and ffmpeg64.exe already present, skip.'
} else {
  $zip = Join-Path $ToolsRoot 'ffmpeg.zip'
  Get-File $FfmpegUrl $zip
  $extractDir = Join-Path $ToolsRoot 'ffmpeg'
  if (Test-Path -LiteralPath $extractDir) { Remove-Item -LiteralPath $extractDir -Recurse -Force }
  Expand-Archive -LiteralPath $zip -DestinationPath $extractDir -Force
  $src = Get-ChildItem -LiteralPath $extractDir -Recurse -Filter 'ffmpeg.exe' | Select-Object -First 1
  if (-not $src) { throw "ffmpeg.exe not found inside $FfmpegUrl" }
  # The csproj expects both names; the 64-bit static build serves both for an x64 build/run.
  Copy-Item -LiteralPath $src.FullName -Destination $ffmpegExe   -Force
  Copy-Item -LiteralPath $src.FullName -Destination $ffmpeg64Exe -Force
  Write-Info "placed ffmpeg.exe and ffmpeg64.exe in $AppProjDir"
}

# --- 4. Inno Setup 6 ---------------------------------------------------------
$iscc = $null
if (-not $SkipInstaller) {
  Write-Step 'Provisioning Inno Setup 6'
  $innoDir = Join-Path $ToolsRoot 'InnoSetup'
  $iscc = Join-Path $innoDir 'ISCC.exe'
  if ((Test-Path -LiteralPath $iscc) -and -not $Force) {
    Write-Info "exists, skip: $iscc"
  } else {
    $innoSetupExe = Join-Path $ToolsRoot 'innosetup.exe'
    Get-File 'https://files.jrsoftware.org/is/6/innosetup-6.3.3.exe' $innoSetupExe
    $innoArgLine = "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /DIR=`"$innoDir`""
    Write-Info 'Installing Inno Setup (UAC prompt if not elevated)...'
    if (Test-Admin) {
      $p = Start-Process -FilePath $innoSetupExe -ArgumentList $innoArgLine -Wait -PassThru
    } else {
      $p = Start-Process -FilePath $innoSetupExe -ArgumentList $innoArgLine -Verb RunAs -Wait -PassThru
    }
    if ($p.ExitCode -ne 0) { throw "Inno Setup installer exited with code $($p.ExitCode)." }
    if (-not (Test-Path -LiteralPath $iscc)) { throw "ISCC.exe not found in $innoDir after install." }
  }
  Write-Info "ISCC: $iscc"
}

# --- 5. NuGet restore --------------------------------------------------------
Write-Step 'Restoring NuGet packages'
& $nuget restore $Solution -NonInteractive
if ($LASTEXITCODE -ne 0) { throw "nuget restore failed ($LASTEXITCODE)." }

# --- 6. Build ----------------------------------------------------------------
if ($NoBuild) {
  Write-Step 'Skipping build (-NoBuild)'
} else {
  Write-Step "Building solution ($Configuration | $Platform)"
  & $msbuild $Solution /t:Build "/p:Configuration=$Configuration" "/p:Platform=$Platform" /m /nologo /v:m
  if ($LASTEXITCODE -ne 0) { throw "msbuild failed ($LASTEXITCODE)." }

  $appExe = Join-Path $AppProjDir "bin\$Configuration\AaxAudioConverter.exe"
  if (-not (Test-Path -LiteralPath $appExe)) { throw "Build reported success but $appExe is missing." }
  Write-Info "built: $appExe"

  # --- 7. Installer ----------------------------------------------------------
  if (-not $SkipInstaller) {
    if ($Configuration -ne 'Release') {
      Write-Info "Installer skipped: setup.iss expects a Release build (current: $Configuration)."
    } else {
      Write-Step 'Compiling the Inno Setup installer'
      & $iscc $IssFile
      if ($LASTEXITCODE -ne 0) { throw "ISCC failed ($LASTEXITCODE)." }
    }
  }
}

# --- summary -----------------------------------------------------------------
Write-Step 'Done'
$appExe = Join-Path $AppProjDir "bin\$Configuration\AaxAudioConverter.exe"
if (Test-Path -LiteralPath $appExe) { Write-Host "  App      : $appExe" -ForegroundColor Green }
$setupDir = Join-Path $SrcDir 'InnoSetup\Setup'
if (Test-Path -LiteralPath $setupDir) {
  Get-ChildItem -LiteralPath $setupDir -Filter '*Setup.exe' -ErrorAction SilentlyContinue |
    ForEach-Object { Write-Host "  Installer: $($_.FullName)" -ForegroundColor Green }
}
