@echo off
chcp 65001 >nul
title 羽球影像分析系統 (Badminton Vision AI)
color 0A
echo =======================================================================
echo          羽球影像生物力學與姿態分析系統 (Badminton Vision AI)
echo =======================================================================
echo.
echo [1/2] 正在啟動本機安全網頁伺服器 (Local Web Server)...
start powershell -WindowStyle Hidden -ExecutionPolicy Bypass -File "%~dp0static_server.ps1"

timeout /t 1 >nul
echo [2/2] 正在為您開啟瀏覽器 (http://127.0.0.1:8080/index.html)...
start http://127.0.0.1:8080/index.html

echo.
echo =======================================================================
echo  系統已成功啟動！
echo  請在瀏覽器中觀看真實羽球比賽影片與 AI 即時人體姿態骨架分析。
echo  若要停止服務，直接關閉此視窗即可。
echo =======================================================================
echo.