@echo off
rem Double-click to verify the Unity CLI: info, compile check, EditMode tests.
rem Output is saved to Logs\cli\setup-check.txt. Close the Unity Editor first.
cd /d "%~dp0..\.."
if not exist Logs\cli mkdir Logs\cli
set OUT=Logs\cli\setup-check.txt
set PS=powershell -NoProfile -ExecutionPolicy Bypass -File Tools\Unity\unity.ps1

echo Started %DATE% %TIME% > %OUT%
echo === info === >> %OUT%
%PS% info >> %OUT% 2>&1

echo === compile === >> %OUT%
echo Running compile check (this can take several minutes)...
%PS% compile >> %OUT% 2>&1
echo compile exit code: %ERRORLEVEL% >> %OUT%

echo === test EditMode === >> %OUT%
echo Running EditMode tests...
%PS% test -Platform EditMode >> %OUT% 2>&1
echo test exit code: %ERRORLEVEL% >> %OUT%

echo Finished %DATE% %TIME% >> %OUT%
echo DONE >> %OUT%
type %OUT%
timeout /t 30
