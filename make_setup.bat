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
taskkill /F /IM MDviewerSetup.exe >nul 2>&1
timeout /t 1 /nobreak >nul

echo [1/3] publish MDviewer
if exist publish_install rmdir /s /q publish_install
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -o publish_install
if errorlevel 1 goto fail
if not exist publish_install\MDviewer.exe goto fail

echo [2/3] copy payload
if not exist setup_proj\payload mkdir setup_proj\payload
copy /y publish_install\MDviewer.exe setup_proj\payload\MDviewer.exe
if errorlevel 1 goto fail

echo [3/3] publish MDviewerSetup.exe
if not exist installer mkdir installer
dotnet publish setup_proj\MDviewerSetup.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -o installer
if errorlevel 1 goto fail

if exist installer\MDviewerSetup.exe (
  echo SETUP=%~dp0installer\MDviewerSetup.exe
  dir installer\MDviewerSetup.exe
  pause
  exit /b 0
)

echo MDviewerSetup.exe was not created
goto fail

:fail
echo setup build failed
pause
exit /b 1
