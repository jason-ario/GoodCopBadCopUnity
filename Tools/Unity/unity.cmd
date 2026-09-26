@echo off
rem Shim so the CLI works from cmd.exe and without changing PowerShell's execution policy.
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0unity.ps1" %*
exit /b %ERRORLEVEL%
