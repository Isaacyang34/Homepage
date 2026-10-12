@echo off
title Stock Portfolio Tracker - Release Pipeline
cd /d "%~dp0"
python package_release.py
pause
