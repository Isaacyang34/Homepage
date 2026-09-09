@echo off
chcp 65001 >nul
setlocal EnableDelayedExpansion

echo ===============================================================
echo   📦 Dynamometer HMI Pro 自動化版本發布打包工具
echo ===============================================================

set /p NEW_VER="請輸入欲發布的版本號 (例如 2.3.0): "
if "%NEW_VER%"=="" (
    echo ❌ 版本號不可為空！
    pause
    exit /b
)

set TARGET_DIR=%~dp0Dynamometer_HMI_V%NEW_VER%_Portable
echo.
echo 📁 正在建立發布目標目錄: !TARGET_DIR!
if exist "!TARGET_DIR!" (
    echo ⚠️ 目錄已存在，正在清理舊檔案...
    rmdir /s /q "!TARGET_DIR!"
)
mkdir "!TARGET_DIR!"

echo 🚚 正在複製核心程式與工具檔...
xcopy /E /I /Y "%~dp0..\Dyanmometer_Modern\*" "!TARGET_DIR!\" >nul

echo ⚙️ 正在產生一鍵啟動腳本...
(
echo @echo off
echo chcp 65001 ^>nul
echo title Dynamometer HMI Pro V%NEW_VER%
echo echo ===============================================================
echo echo   ⚡ Dynamometer HMI Pro ^(V%NEW_VER% Portable Release^)
echo echo   正在啟動現代化動力計上位機監控系統...
echo echo ===============================================================
echo start "" "%%~dp0index.html"
echo echo ✅ 已在預設瀏覽器中開啟 HMI 監控面板！
echo echo.
echo pause
) > "!TARGET_DIR!\Start_Dynamometer_HMI.bat"

echo.
echo ===============================================================
echo 🎉 發布打包完成！
echo 發布目錄: !TARGET_DIR!
echo ===============================================================
pause
