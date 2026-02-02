@echo off
REM LED Studio Pro - 自動備份腳本
REM 使用方式: 運行此腳本自動備份當前版本

setlocal enabledelayedexpansion
cd /d "%~dp0"

REM 設定變數
set SOURCE=LED螢幕設計界面規劃V1.1.html
set BACKUP_DIR=backups
set TIMESTAMP=%date:~0,4%-%date:~5,2%-%date:~8,2%_%time:~0,2%-%time:~5,2%-%time:~8,2%
set TIMESTAMP=%TIMESTAMP: =0%

REM 創建備份目錄
if not exist "%BACKUP_DIR%" mkdir "%BACKUP_DIR%"

REM 執行備份
copy "%SOURCE%" "%BACKUP_DIR%\LED_Studio_V1.1_%TIMESTAMP%.html"
if %errorlevel% equ 0 (
    echo.
    echo ✓ 備份成功！
    echo 備份位置: %BACKUP_DIR%\LED_Studio_V1.1_%TIMESTAMP%.html
    echo.
) else (
    echo.
    echo ✗ 備份失敗！請檢查檔案是否存在
    echo.
    exit /b 1
)

REM 列出最近的備份
echo 最近的備份:
dir /b /o-d "%BACKUP_DIR%" | findstr /c:"LED_Studio" | for /f "delims=" %%i in ('more') do (
    echo   - %%i
    if !count! gtr 4 goto :show_old
    set /a count=!count!+1
)
goto :end

:show_old
echo.
echo (更早的備份已隱藏，共 %count% 個備份)

:end
echo.
pause
