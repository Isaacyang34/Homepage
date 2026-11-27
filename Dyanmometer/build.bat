@echo off
title Dynamometer HMI Build
cd /d "%~dp0"
powershell -ExecutionPolicy Bypass -File package_release.ps1 -NoPush
pause
