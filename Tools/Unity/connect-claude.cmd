@echo off
rem Double-click to connect this Unity project to the Claude desktop app (Unity MCP).
rem Output is saved to Logs\cli\connect-claude.txt. Unity Editor can stay open.
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0connect-claude.ps1"
echo.
echo Done. Now fully quit and reopen the Claude desktop app.
timeout /t 60
