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

echo publish self-contained
if exist publish_install rmdir /s /q publish_install
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -o publish_install
if errorlevel 1 goto fail

set "ISCC="
if exist "%LocalAppData%\Programs\Inno Setup 6\ISCC.exe" set "ISCC=%LocalAppData%\Programs\Inno Setup 6\ISCC.exe"
if exist "%ProgramFiles(x86)%\Inno Setup 6\ISCC.exe" set "ISCC=%ProgramFiles(x86)%\Inno Setup 6\ISCC.exe"
if exist "%ProgramFiles%\Inno Setup 6\ISCC.exe" set "ISCC=%ProgramFiles%\Inno Setup 6\ISCC.exe"

if defined ISCC (
  echo compile Setup.exe
  if not exist installer mkdir installer
  "%ISCC%" setup.iss
  if errorlevel 1 goto fail
  echo SETUP=%~dp0installer\MDviewerSetup.exe
  pause
  exit /b 0
)

echo Inno Setup 6 not found
echo Install Inno Setup then run this bat again
echo Or run install_user.bat
echo Or use publish_install\MDviewer.exe
pause
exit /b 0

:fail
echo installer build failed
pause
exit /b 1
