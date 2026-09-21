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

if exist publish rmdir /s /q publish
echo publish release single file
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:DebugType=none -p:DebugSymbols=false -o publish
if errorlevel 1 exit /b 1

echo.
echo RELEASE EXE=%~dp0publish\MDviewer.exe
dir publish\MDviewer.exe
exit /b 0