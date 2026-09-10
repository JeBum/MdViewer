@echo off
setlocal EnableExtensions
set "DEST=%LocalAppData%\Programs\MDviewer"
if not exist "%DEST%" mkdir "%DEST%"
copy /y "%~dp0MDviewer.exe" "%DEST%\MDviewer.exe" >nul
if exist "%~dp0*.dll" copy /y "%~dp0*.dll" "%DEST%\" >nul
set "SC=%AppData%\Microsoft\Windows\Start Menu\Programs\MDviewer.lnk"
powershell -NoProfile -Command "$d=$env:LOCALAPPDATA+'\Programs\MDviewer'; $p=$env:APPDATA+'\Microsoft\Windows\Start Menu\Programs\MDviewer.lnk'; $s=(New-Object -ComObject WScript.Shell).CreateShortcut($p); $s.TargetPath=$d+'\MDviewer.exe'; $s.WorkingDirectory=$d; $s.Save()"
start "" "%DEST%\MDviewer.exe"
exit /b 0
