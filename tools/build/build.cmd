@echo off
rem One-shot build environment setup + build for AAX Audio Converter.
rem Forwards all arguments to setup.ps1, e.g.:
rem   build.cmd -ToolsRoot D:\aax-tools -VsInstallPath D:\aax-tools\VSBuildTools
rem   build.cmd -SkipVsBuildTools -SkipInstaller
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0setup.ps1" %*
exit /b %ERRORLEVEL%
