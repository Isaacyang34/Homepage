# Automated Release and Push to GitHub (gh-pages & master)
$ErrorActionPreference = "Stop"
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Definition
$WorkspaceRoot = Split-Path -Parent (Split-Path -Parent $ScriptDir)

$GitExe = "C:\Users\peter\AppData\Local\Programs\Git\cmd\git.exe"
if (-not (Test-Path $GitExe)) {
    $GitExe = "git"
}

Write-Host "==================================================" -ForegroundColor Cyan
Write-Host "   TPMS Firmware Release & Dual-Branch GitHub Push" -ForegroundColor Cyan
Write-Host "==================================================" -ForegroundColor Cyan

# 1. Compile and export latest firmware.bin
Write-Host "`n[Step 1] Compiling firmware binary..." -ForegroundColor Yellow
& (Join-Path $ScriptDir "export_firmware_bin.ps1")
if ($LASTEXITCODE -ne 0) {
    Write-Host "[Error] Compilation failed!" -ForegroundColor Red
    exit 1
}

# 2. Stage files in Git
Write-Host "`n[Step 2] Staging files in Git..." -ForegroundColor Yellow
Push-Location $WorkspaceRoot
try {
    & $GitExe add -f "ESP32_TPMS_Receiver/poc_minimal_tester/firmware.bin"
    & $GitExe add -f "ESP32_TPMS_Receiver/poc_minimal_tester/version.json"
    & $GitExe add -f "ESP32_TPMS_Receiver/poc_minimal_tester/web_page.h"
    & $GitExe add -f "ESP32_TPMS_Receiver/poc_minimal_tester/poc_minimal_tester.ino"
    & $GitExe add -f "ESP32_TPMS_Receiver/poc_minimal_tester/export_firmware_bin.ps1"
    & $GitExe add -f "ESP32_TPMS_Receiver/poc_minimal_tester/push_to_github.ps1"
    & $GitExe add ".gitignore"

    # Verify firmware.bin is tracked
    $tracked = & $GitExe ls-files --stage "ESP32_TPMS_Receiver/poc_minimal_tester/firmware.bin"
    if (-not $tracked) {
        Write-Host "[Error] firmware.bin is NOT tracked by Git!" -ForegroundColor Red
        exit 1
    }
    Write-Host "[OK] firmware.bin confirmed tracked in Git index." -ForegroundColor Green

    # 3. Commit
    Write-Host "`n[Step 3] Committing changes..." -ForegroundColor Yellow
    & $GitExe commit -m "feat(tpms): release v2.8.1 with squelch gate and multi-offset decoder"
} finally {
    Pop-Location
}

# 4. Push to both gh-pages and master (Rule 7: Dual-Branch push)
Write-Host "`n[Step 4] Pushing to GitHub (Dual-Branch: gh-pages + master)..." -ForegroundColor Yellow
Push-Location $WorkspaceRoot
try {
    & $GitExe push origin gh-pages
    if ($LASTEXITCODE -ne 0) {
        Write-Host "[Warning] Push to gh-pages failed. Please check network/auth." -ForegroundColor Yellow
    }

    & $GitExe push origin gh-pages:master --force
    if ($LASTEXITCODE -ne 0) {
        Write-Host "[Warning] Push to master failed." -ForegroundColor Yellow
    }
} finally {
    Pop-Location
}

Write-Host "`n==================================================" -ForegroundColor Cyan
Write-Host "   Release Process Complete!" -ForegroundColor Green
Write-Host "==================================================" -ForegroundColor Cyan
