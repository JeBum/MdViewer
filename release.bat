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

rem Requirement: never delete publish_next; use a timestamped sibling when it already exists.
set "RELEASE_DIR=publish"
if exist publish\MDviewer.exe (
  powershell -NoProfile -Command "try { $file = [System.IO.File]::Open('%~dp0publish\MDviewer.exe', 'Open', 'ReadWrite', 'None'); $file.Dispose(); exit 0 } catch { exit 1 }"
  if errorlevel 1 (
    set "RELEASE_DIR=publish_next"
    if exist publish_next (
      for /f "tokens=1-4 delims=/:. " %%a in ("%date% %time%") do set "RELEASE_DIR=publish_next_%%a%%b%%c_%%d"
      if exist "%RELEASE_DIR%" set "RELEASE_DIR=publish_next_%RANDOM%"
    )
  )
)
if /i not "%RELEASE_DIR%"=="publish_next" if /i not "%RELEASE_DIR:~0,13%"=="publish_next_" if exist "%RELEASE_DIR%" rmdir /s /q "%RELEASE_DIR%"
if exist "%RELEASE_DIR%" (
  echo Cannot clear %RELEASE_DIR%
  exit /b 1
)
echo publish release single file
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:DebugType=none -p:DebugSymbols=false -o "%RELEASE_DIR%"
if errorlevel 1 exit /b 1

echo.
echo RELEASE EXE=%~dp0%RELEASE_DIR%\MDviewer.exe
dir "%RELEASE_DIR%\MDviewer.exe"
exit /b 0
