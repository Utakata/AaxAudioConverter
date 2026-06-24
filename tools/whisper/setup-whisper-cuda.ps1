<#
.SYNOPSIS
  Set up a CUDA (GPU) build of whisper.cpp + a model for the AAX Audio Converter
  local transcription engine.

.DESCRIPTION
  The app's local transcription engine (see AaxAudioConverterLib/Whisper.cs) simply runs
  "whisper-cli.exe" with no GPU flag, so whisper.cpp uses the GPU automatically WHEN the
  binary is a CUDA / cuBLAS build. This script provisions such a build so an NVIDIA GPU
  (e.g. a GeForce GTX 1650, 4 GB) is used instead of the CPU:

    1. Downloads the latest whisper.cpp Windows CUDA (cuBLAS) release from GitHub and
       extracts whisper-cli.exe + the bundled runtime DLLs (ggml*.dll, cudart/cublas).
       The CUDA runtime is bundled, so no separate CUDA Toolkit install is required.
    2. Downloads a ggml model suited to the available VRAM from Hugging Face.

  Afterwards, point the app at -Dest:
    Settings -> Transcription -> Engine = Local -> Whisper folder = <Dest>

  Everything is resolved relative to the repository (via $PSScriptRoot), so it works on any
  drive; -Dest may point at e.g. D:\ to keep C:\ clean.

.EXAMPLE
  # Default: bin under <repo>\tools\whisper\bin, large-v3-turbo q5_0 model (good for 4 GB)
  powershell -ExecutionPolicy Bypass -File tools\whisper\setup-whisper-cuda.ps1

.EXAMPLE
  # Lighter model, custom destination on D:\
  powershell -ExecutionPolicy Bypass -File tools\whisper\setup-whisper-cuda.ps1 `
    -Dest D:\aax-whisper -Model medium
#>
[CmdletBinding()]
param(
  [string] $Dest,
  [ValidateSet('medium', 'large-v3', 'large-v3-q5_0', 'large-v3-turbo', 'large-v3-turbo-q5_0')]
  [string] $Model = 'large-v3-turbo-q5_0',
  [switch] $Force
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12

# --- paths -------------------------------------------------------------------
if (-not $Dest) { $Dest = Join-Path $PSScriptRoot 'bin' }

$WhisperReleasesApi = 'https://api.github.com/repos/ggml-org/whisper.cpp/releases/latest'
$ModelBaseUrl       = 'https://huggingface.co/ggerganov/whisper.cpp/resolve/main'

# --- helpers -----------------------------------------------------------------
function Write-Step ([string] $Message) { Write-Host "`n==> $Message" -ForegroundColor Cyan }
function Write-Info ([string] $Message) { Write-Host "    $Message" -ForegroundColor DarkGray }
function Write-Warn ([string] $Message) { Write-Host "    $Message" -ForegroundColor Yellow }

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

# --- preflight ---------------------------------------------------------------
if ($PSVersionTable.PSVersion.Major -lt 5) { throw 'PowerShell 5+ is required.' }
if ($env:OS -ne 'Windows_NT') { throw 'This script provisions Windows binaries and must run on Windows.' }

Write-Step 'whisper.cpp CUDA (GPU) setup for AAX Audio Converter'
Write-Info "Dest  : $Dest"
Write-Info "Model : ggml-$Model.bin"
New-Dir $Dest

# --- 1. whisper.cpp CUDA binary ---------------------------------------------
Write-Step 'Locating the latest whisper.cpp Windows CUDA (cuBLAS) release'
$whisperCli = Join-Path $Dest 'whisper-cli.exe'
if ((Test-Path -LiteralPath $whisperCli) -and -not $Force) {
  Write-Info "exists, skip: $whisperCli"
} else {
  $headers = @{ 'User-Agent' = 'aaxconv-setup'; 'Accept' = 'application/vnd.github+json' }
  try {
    $release = Invoke-RestMethod -Uri $WhisperReleasesApi -Headers $headers -UseBasicParsing
  } catch {
    throw "Could not query the whisper.cpp releases API ($WhisperReleasesApi): $($_.Exception.Message)"
  }

  # Prefer a cuBLAS / CUDA x64 asset.
  $asset = $release.assets |
    Where-Object { $_.name -match '(?i)(cublas|cuda)' -and $_.name -match '(?i)x64' -and $_.name -match '(?i)\.zip$' } |
    Select-Object -First 1

  if (-not $asset) {
    Write-Warn 'No prebuilt CUDA (cuBLAS) Windows asset was found in the latest whisper.cpp release.'
    Write-Warn 'Download one manually from https://github.com/ggml-org/whisper.cpp/releases'
    Write-Warn "(look for a *cublas*-x64*.zip), extract whisper-cli.exe + its DLLs into: $Dest"
    Write-Warn 'Then re-run this script to fetch the model, or download the model manually too.'
    throw 'CUDA whisper.cpp binary not available automatically.'
  }

  Write-Info "asset: $($asset.name)  (release $($release.tag_name))"
  $zip = Join-Path $Dest $asset.name
  Get-File $asset.browser_download_url $zip

  $extractDir = Join-Path $Dest '_extract'
  if (Test-Path -LiteralPath $extractDir) { Remove-Item -LiteralPath $extractDir -Recurse -Force }
  Expand-Archive -LiteralPath $zip -DestinationPath $extractDir -Force

  # Flatten: copy whisper-cli.exe and every DLL (ggml*, cudart*, cublas*, etc.) into $Dest.
  $cli = Get-ChildItem -LiteralPath $extractDir -Recurse -Filter 'whisper-cli.exe' | Select-Object -First 1
  if (-not $cli) {
    # older builds shipped the exe as main.exe
    $cli = Get-ChildItem -LiteralPath $extractDir -Recurse -Filter 'main.exe' | Select-Object -First 1
  }
  if (-not $cli) { throw "Neither whisper-cli.exe nor main.exe found inside $($asset.name)." }

  $binDir = Split-Path -Parent $cli.FullName
  Get-ChildItem -LiteralPath $binDir -File |
    Where-Object { $_.Extension -in @('.exe', '.dll') } |
    ForEach-Object { Copy-Item -LiteralPath $_.FullName -Destination (Join-Path $Dest $_.Name) -Force }

  Remove-Item -LiteralPath $extractDir -Recurse -Force
  if (-not (Test-Path -LiteralPath $whisperCli)) {
    # the build may have used main.exe; the app also accepts that legacy name
    $legacy = Join-Path $Dest 'main.exe'
    if (-not (Test-Path -LiteralPath $legacy)) { throw "whisper-cli.exe was not placed in $Dest." }
    Write-Info 'placed legacy main.exe (the app accepts it as a fallback).'
  }
  Write-Info "whisper.cpp CUDA binary ready in $Dest"
}

# --- 2. model ----------------------------------------------------------------
Write-Step "Downloading model ggml-$Model.bin"
$modelFile = Join-Path $Dest "ggml-$Model.bin"
Get-File "$ModelBaseUrl/ggml-$Model.bin" $modelFile

# --- done --------------------------------------------------------------------
Write-Step 'Done'
Write-Host ''
Write-Host "  Whisper folder : $Dest" -ForegroundColor Green
Write-Host "  Binary         : whisper-cli.exe (+ CUDA runtime DLLs)" -ForegroundColor Green
Write-Host "  Model          : ggml-$Model.bin" -ForegroundColor Green
Write-Host ''
Write-Host '  Next steps:' -ForegroundColor Green
Write-Host '   1. In the app: Settings -> Transcription -> Engine = Local'
Write-Host "   2. Set the Whisper folder to: $Dest"
Write-Host '   3. Convert a book. Confirm GPU use in Task Manager (GPU -> CUDA),'
Write-Host '      or run whisper-cli.exe once and look for the CUDA backend / your GPU name.'
Write-Host ''
