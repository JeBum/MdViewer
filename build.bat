@echo off
setlocal EnableExtensions
cd /d "%~dp0"

where dotnet >nul 2>&1
if errorlevel 1 (
  echo dotnet not found
  pause
  exit /b 1
)

echo close running viewer
taskkill /F /IM MDviewer.exe >nul 2>&1
taskkill /F /IM MDviewer.exe >nul 2>&1
timeout /t 1 /nobreak >nul

echo restore
dotnet restore
if errorlevel 1 goto fail

echo publish single file
if exist publish rmdir /s /q publish
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:DebugType=none -p:DebugSymbols=false -o publish
if errorlevel 1 goto fail

echo.
echo EXE=%~dp0publish\MDviewer.exe
dir publish\MDviewer.exe
pause
exit /b 0

:fail
echo build failed
echo close MDviewer.exe and retry
pause
exit /b 1
