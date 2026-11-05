@echo off
chcp 65001 >nul
echo ==================================================================
echo   橫河 YOKOGAWA WT333E 獨立測試工具建置編譯 (XP/Win7/10/11 相容)
echo ==================================================================

set CSC_PATH="C:\Windows\Microsoft.NET\Framework\v4.0.30319\csc.exe"

if not exist %CSC_PATH% (
    echo [錯誤] 找不到 .NET Framework 4.0 編譯器: %CSC_PATH%
    pause
    exit /b 1
)

echo.
echo 正在編譯 WT333E_Tester_GUI.exe...
%CSC_PATH% /nologo /target:winexe /platform:anycpu /out:WT333E_Tester_GUI.exe /r:System.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:System.Data.dll WT333E_Tester_GUI.cs
if %ERRORLEVEL% NEQ 0 (
    echo [ERROR] GUI Build Failed!
    pause
    exit /b 1
)

echo.
echo ========================================================
echo [成功] WT333E Tester GUI 編譯完成！
echo ========================================================
pause
