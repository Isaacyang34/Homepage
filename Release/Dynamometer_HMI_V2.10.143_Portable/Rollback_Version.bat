@echo off
setlocal enabledelayedexpansion
title Dynamometer Rollback Utility
cls
echo ======================================================================
echo   Dynamometer HMI Pro - Rollback Utility
echo ======================================================================
echo.

set "APP_DIR=%~dp0"
set "BACKUP_DIR=%APP_DIR%backups"
set "MAIN_EXE=%APP_DIR%Dynamometer_HMI_Pro.exe"

if not exist "%BACKUP_DIR%" (
    echo [NOTICE] Backups directory not found: %BACKUP_DIR%
    pause
    exit /b 1
)

set count=0
echo Available backup versions (Latest 5):
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
    echo [NOTICE] No backup files found in backups folder.
    pause
    exit /b 1
)

echo ----------------------------------------------------------------------
echo.
set /p "CHOICE=Select version to restore (1-%count%), or Q to quit: "

if /i "%CHOICE%"=="Q" (
    echo Cancelled.
    exit /b 0
)

if "%CHOICE%"=="" goto :invalid_choice
if "%CHOICE%" LSS "1" goto :invalid_choice
if "%CHOICE%" GTR "%count%" goto :invalid_choice

set "SELECTED_NAME=!FILE_%CHOICE%!"
set "SELECTED_PATH=!PATH_%CHOICE%!"

echo.
echo Restoring to: %SELECTED_NAME%
echo Terminating running HMI instances...
taskkill /F /IM Dynamometer_HMI_Pro.exe >nul 2>&1
ping 127.0.0.1 -n 2 >nul

if exist "%MAIN_EXE%" (
    echo Archiving current executable before rollback...
    copy /y "%MAIN_EXE%" "%BACKUP_DIR%\Dynamometer_HMI_Pro_pre_rollback.exe" >nul 2>&1
)

echo Writing restored executable...
copy /y "%SELECTED_PATH%" "%MAIN_EXE%" >nul
if %errorlevel% neq 0 (
    echo [ERROR] Restore failed! Check file locks or permissions.
    pause
    exit /b 1
)

echo.
echo ======================================================================
echo   [SUCCESS] Successfully restored to: %SELECTED_NAME%
echo   Launching main application...
echo ======================================================================
start "" "%MAIN_EXE%"
ping 127.0.0.1 -n 2 >nul
exit /b 0

:invalid_choice
echo [INVALID] Invalid choice.
pause
exit /b 1
