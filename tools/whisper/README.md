# Local GPU transcription with whisper.cpp (CUDA)

Run the **Local** transcription engine on your NVIDIA GPU (e.g. a **GeForce GTX 1650, 4 GB**)
instead of the CPU or Google Colab. **No app changes are needed** — the app
(`AaxAudioConverterLib/Whisper.cs`) just runs `whisper-cli.exe` with no GPU flag, and whisper.cpp
uses the GPU automatically **when the binary is a CUDA (cuBLAS) build**. This folder provisions
such a build plus a model.

## Prerequisites

- A recent **NVIDIA driver** (a GTX 1650 with a 2024+ driver is fine — it supports CUDA 12).
- **No CUDA Toolkit install required**: the whisper.cpp cuBLAS release zip bundles the CUDA
  runtime DLLs (`cudart*`, `cublas*`).

## Quick start

```powershell
# Default: bin under tools\whisper\bin, large-v3-turbo q5_0 model (good for 4 GB)
powershell -ExecutionPolicy Bypass -File tools\whisper\setup-whisper-cuda.ps1

# Or double-click / run:
tools\whisper\setup.cmd
```

Then in the app:

1. **Settings → Transcription → Engine = *Local***.
2. Set the **Whisper folder** to the script's `-Dest` (default `tools\whisper\bin`).
3. Pick the language (Japanese transcribes with `-l ja`), and convert a book.

## Options

| Parameter | Default | Notes |
|---|---|---|
| `-Dest`  | `tools\whisper\bin` | Where the binary + model go. Can be on any drive, e.g. `D:\aax-whisper`. |
| `-Model` | `large-v3-turbo-q5_0` | One of `medium`, `large-v3`, `large-v3-q5_0`, `large-v3-turbo`, `large-v3-turbo-q5_0`. |
| `-Force` | off | Re-download / overwrite even if files already exist. |

```powershell
# Lighter model, custom destination on D:\
powershell -ExecutionPolicy Bypass -File tools\whisper\setup-whisper-cuda.ps1 -Dest D:\aax-whisper -Model medium
```

## Which model for 4 GB VRAM?

- **Recommended:** `large-v3-turbo-q5_0` — fast, good English/Japanese quality, ~1.5–2 GB VRAM.
- **Lighter:** `medium` — smaller/faster, slightly lower accuracy.
- **Best quality (tighter):** `large-v3-q5_0`.
- The full `large-v3` (f16, ~3 GB) also fits but is tighter and slower on a Tensor-Core-less
  GTX 16xx; the `q5_0` quantized variants are the sweet spot here.

The app picks `ggml-base.bin` if present, otherwise the **first** `ggml-*.bin` in the folder
(`Whisper.ModelPath`). Keep just one model in the folder to avoid ambiguity, or name your
preferred one so it sorts first.

## Confirming the GPU is actually used

- **Task Manager → Performance → GPU**: the **CUDA** (or *Compute*) graph spikes during
  transcription; dedicated GPU memory rises.
- **Run it once manually** to see the backend log:
  ```
  tools\whisper\bin\whisper-cli.exe -m tools\whisper\bin\ggml-large-v3-turbo-q5_0.bin -f some.wav
  ```
  Look for lines mentioning the **CUDA backend** and your device (`NVIDIA GeForce GTX 1650`).
- whisper.cpp uses the GPU by default; `-ng` / `--no-gpu` would force CPU (the app does not pass it).

## Notes

- This is the same **Local** engine as before, just backed by a GPU build — switching back to a
  CPU-only `whisper-cli.exe` in the folder still works.
- The boilerplate filter / Markdown output behave identically to the CPU path; only the
  speech-to-text runs on the GPU.
