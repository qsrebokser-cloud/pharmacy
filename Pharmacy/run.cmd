@echo off
setlocal
if not exist "%~dp0Deliverable\ZharPtitsa.exe" (
  echo Build the application first: powershell -File build.ps1
  pause
  exit /b 1
)
if exist "%~dp0..\.dotnet\dotnet.exe" (
  set "DOTNET_ROOT=%~dp0..\.dotnet"
  "%~dp0..\.dotnet\dotnet.exe" "%~dp0Deliverable\ZharPtitsa.dll"
  exit /b
)
"%~dp0Deliverable\ZharPtitsa.exe"
