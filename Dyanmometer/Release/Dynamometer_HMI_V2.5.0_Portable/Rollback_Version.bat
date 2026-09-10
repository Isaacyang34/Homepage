@echo off
chcp 65001 >nul 2>&1
title 馬達動力計 HMI 歷史版本退回工具 (Dynamometer Rollback Utility)
cls
echo ======================================================================
echo   馬達動力計 HMI Pro - 歷史版本自主退回急救工具 (Rollback Utility)
echo ======================================================================
echo.

setlocal enabledelayedexpansion
set "APP_DIR=%~dp0"
set "BACKUP_DIR=%APP_DIR%backups"
set "MAIN_EXE=%APP_DIR%Dynamometer_HMI_Pro.exe"

if not exist "%BACKUP_DIR%" (
    echo [提示] 尚未建立 backups 備份目錄，目前無歷史版本可退回。
    echo.
    pause
    exit /b 1
)

set count=0
echo 可供退回的歷史備份版本 (保留最新 5 版)：
echo ----------------------------------------------------------------------
for /f "delims=" %%F in ('dir /b /o-d "%BACKUP_DIR%\Dynamometer_HMI_Pro_*.exe" 2^>nul') do (
    set /a count+=1
    set "FILE_!count!=%%F"
    set "PATH_!count!=%BACKUP_DIR%\%%F"
    echo   [!count!] %%F
    if !count! geq 5 goto :list_done
)
:list_done

if %count% equ 0 (
    echo [提示] backups 目錄下尚無任何備份檔案。
    echo.
    pause
    exit /b 1
)

echo ----------------------------------------------------------------------
echo.
set /p "CHOICE=請輸入要還原的歷史版本號碼 (1-%count%)，或按 Q 退出: "

if /i "%CHOICE%"=="Q" (
    echo 已取消操作。
    exit /b 0
)

if "%CHOICE%"=="" goto :invalid_choice
if "%CHOICE%" LSS "1" goto :invalid_choice
if "%CHOICE%" GTR "%count%" goto :invalid_choice

set "SELECTED_NAME=!FILE_%CHOICE%!"
set "SELECTED_PATH=!PATH_%CHOICE%!"

echo.
echo 即將還原為: %SELECTED_NAME%
echo 正在關閉殘留之主程式進程 (若有)...
taskkill /F /IM Dynamometer_HMI_Pro.exe >nul 2>&1
ping 127.0.0.1 -n 2 >nul

if exist "%MAIN_EXE%" (
    echo 正在封存當前執行的主程式...
    copy /y "%MAIN_EXE%" "%BACKUP_DIR%\Dynamometer_HMI_Pro_pre_rollback_%date:~0,4%%date:~5,2%%date:~8,2%_%time:~0,2%%time:~3,2%%time:~6,2%.exe" >nul 2>&1
)

echo 正在寫入還原檔案...
copy /y "%SELECTED_PATH%" "%MAIN_EXE%" >nul
if %errorlevel% neq 0 (
    echo [錯誤] 還原失敗！請確認是否有檔案被鎖定或權限不足。
    pause
    exit /b 1
)

echo.
echo ======================================================================
echo   [成功] 已成功還原為: %SELECTED_NAME%
echo   正在為您重新啟動主程式...
echo ======================================================================
start "" "%MAIN_EXE%"
ping 127.0.0.1 -n 2 >nul
exit /b 0

:invalid_choice
echo [輸入無效] 請輸入有效的數字選項。
pause
exit /b 1
