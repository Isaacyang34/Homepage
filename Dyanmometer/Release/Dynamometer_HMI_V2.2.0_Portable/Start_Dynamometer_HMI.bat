@echo off
chcp 65001 >nul
title Dynamometer HMI Pro V2.2.0
echo ===============================================================
echo   ⚡ Dynamometer HMI Pro (V2.2.0 Portable Release)
echo   正在啟動現代化動力計上位機監控系統...
echo ===============================================================

start "" "%~dp0index.html"

echo.
echo ✅ 已在預設瀏覽器中開啟 HMI 監控面板！
echo.
echo 若需要執行獨立硬體通訊測試工具，請前往 tools\ 資料夾：
echo   - tools\Test_USB_TorqueMeter.exe       (USB 扭力計測試)
echo   - tools\Test_PowerMeter_Ethernet.exe   (網路功率分析儀測試)
echo   - tools\Test_Graphtec_GBD_Ethernet.exe (GBD 網路溫度記錄器測試)
echo ===============================================================
pause
