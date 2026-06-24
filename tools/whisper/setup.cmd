@echo off
rem Convenience wrapper so the whisper.cpp CUDA setup can be run without changing
rem the PowerShell execution policy. Any arguments are forwarded to setup-whisper-cuda.ps1.
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0setup-whisper-cuda.ps1" %*
