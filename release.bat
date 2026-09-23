@echo off
setlocal EnableExtensions
cd /d "%~dp0"

where dotnet >nul 2>&1
if errorlevel 1 (
  echo dotnet not found
  exit /b 1
)

echo restore
dotnet restore
if errorlevel 1 exit /b 1

set "RELEASE_DIR=publish"
echo close running viewer
powershell -NoProfile -Command "Get-Process -Name MDviewer -ErrorAction SilentlyContinue | Stop-Process -Force"
powershell -NoProfile -Command "Start-Sleep -Milliseconds 800"
if exist publish\MDviewer.exe (
  powershell -NoProfile -Command "try { $file = [System.IO.File]::Open('%~dp0publish\MDviewer.exe', 'Open', 'ReadWrite', 'None'); $file.Dispose(); exit 0 } catch { exit 1 }"
  if errorlevel 1 (
    echo publish\MDviewer.exe is still in use. Close MDviewer and retry.
    exit /b 1
  )
)
if exist "%RELEASE_DIR%" (
  rmdir /s /q "%RELEASE_DIR%"
)
if exist "%RELEASE_DIR%" (
  echo Cannot clear publish
  exit /b 1
)
echo publish release single file
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:DebugType=none -p:DebugSymbols=false -o "%RELEASE_DIR%"
if errorlevel 1 exit /b 1

echo.
echo RELEASE EXE=%~dp0%RELEASE_DIR%\MDviewer.exe
dir "%RELEASE_DIR%\MDviewer.exe"
exit /b 0
