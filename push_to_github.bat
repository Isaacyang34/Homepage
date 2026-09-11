@echo off
setlocal
cd /d "%~dp0"
if not exist ".git" cd /d "%~dp0\.."
echo ========================================================
echo   Pushing to GitHub (gh-pages & master)...
echo ========================================================
set "PATH=%LOCALAPPDATA%\Programs\Git\cmd;C:\Program Files\Git\cmd;%PATH%"
git push origin gh-pages
git push origin gh-pages:master --force
echo.
echo ========================================================
if %ERRORLEVEL% EQU 0 (
    echo [OK] Successfully pushed to GitHub (gh-pages & master)!
) else (
    echo [!] Push failed, please check GitHub authorization.
)
echo ========================================================
pause