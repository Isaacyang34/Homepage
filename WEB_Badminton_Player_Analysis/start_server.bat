@echo off
title Badminton Vision AI - Web & API Server
cd /d "%~dp0\python_backend"
echo ===================================================
echo [Badminton Vision AI] Starting FastAPI Server...
echo Host: http://localhost:8000
echo Dashboard: http://localhost:8000/
echo ===================================================
python api_server.py
pause
