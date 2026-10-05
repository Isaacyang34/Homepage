@echo off
title ETF Portfolio Studio
cd /d "%~dp0"
python server.py
if errorlevel 1 (
    echo.
    echo [Error] Failed to start Python server. Please ensure Python is installed and added to PATH.
    pause
)
