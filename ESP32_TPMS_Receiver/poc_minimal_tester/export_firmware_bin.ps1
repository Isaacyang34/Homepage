# Export Firmware Binary for OTA Wireless Update
$ErrorActionPreference = "Stop"
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Definition
$InoPath = Join-Path $ScriptDir "poc_minimal_tester.ino"
$OutBin = Join-Path $ScriptDir "firmware.bin"
$ArduinoCli = "C:\Users\peter\bin\arduino-cli.exe"

if (-not (Test-Path $ArduinoCli)) {
    $ArduinoCli = "arduino-cli"
}

Write-Host "==================================================" -ForegroundColor Cyan
Write-Host "   Export ESP8266 Firmware Binary for OTA Update  " -ForegroundColor Cyan
Write-Host "==================================================" -ForegroundColor Cyan

Write-Host "Compiling firmware with --export-binaries..." -ForegroundColor Yellow
& $ArduinoCli compile --fqbn esp8266:esp8266:nodemcuv2 --export-binaries "$InoPath"

if ($LASTEXITCODE -ne 0) {
    Write-Host "[Error] Compilation failed!" -ForegroundColor Red
    exit $LASTEXITCODE
}

# Locate built binary
$buildDir = Join-Path $ScriptDir "build\esp8266.esp8266.nodemcuv2"
$sourceBin = Join-Path $buildDir "poc_minimal_tester.ino.bin"

if (Test-Path $sourceBin) {
    Copy-Item -Path $sourceBin -Destination $OutBin -Force
    $sizeKb = [math]::Round((Get-Item $OutBin).Length / 1024, 1)
    Write-Host "`n[SUCCESS] Firmware binary ready for OTA!" -ForegroundColor Green
    Write-Host "File: $OutBin ($sizeKb KB)" -ForegroundColor Green
    Write-Host "`nOTA Wireless Update Instructions:" -ForegroundColor Cyan
    Write-Host "1. Connect phone or PC to WiFi AP: TPMS_PoC_Tester"
    Write-Host "2. Open Web Browser to: http://192.168.4.1 -> Click '線上更新' Tab"
    Write-Host "3. Choose firmware.bin and click '開始無線升級'!"
} else {
    Write-Host "[Notice] Please check build directory for .bin output" -ForegroundColor Yellow
}
