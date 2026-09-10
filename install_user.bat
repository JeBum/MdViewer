@echo off
setlocal EnableExtensions
cd /d "%~dp0"

set "SRC=%~dp0publish_install"
if not exist "%SRC%\MDviewer.exe" set "SRC=%~dp0publish"
if not exist "%SRC%\MDviewer.exe" (
  echo MDviewer.exe not found
  echo Run make_installer.bat or build.bat first
  pause
  exit /b 1
)

set "DEST=%LocalAppData%\Programs\MDviewer"
echo DEST=%DEST%
if not exist "%DEST%" mkdir "%DEST%"
copy /y "%SRC%\MDviewer.exe" "%DEST%\MDviewer.exe"
if exist "%SRC%\*.dll" copy /y "%SRC%\*.dll" "%DEST%\"

set "SC=%AppData%\Microsoft\Windows\Start Menu\Programs\MDviewer.lnk"
powershell -NoProfile -Command "$s=(New-Object -ComObject WScript.Shell).CreateShortcut($env:SC); $s.TargetPath=$env:DEST+'\MDviewer.exe'; $s.WorkingDirectory=$env:DEST; $s.Save()"

echo installed
echo EXE=%DEST%\MDviewer.exe
start "" "%DEST%\MDviewer.exe"
pause
exit /b 0
