@echo off
chcp 65001 >nul
set PYTHONIOENCODING=utf-8
cd /d "%~dp0"
title HatchMerge server
where py >nul 2>nul
if %errorlevel%==0 (
    py -3 hatchmerge_server.py
) else (
    python hatchmerge_server.py
)
echo.
echo Server stopped. If you see an error above, install Python 3 from python.org
pause
