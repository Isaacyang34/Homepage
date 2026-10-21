$ErrorActionPreference = "Stop"

$srcDist = "c:\Users\peter\OneDrive\Desktop\AI_Projects\ios2pixel\dist\Any2Pixel"
$targetDir = "c:\Users\peter\OneDrive\Desktop\AI_Projects\Release\Any2Pixel_V1.0.0_Portable"
$platformTools = "c:\Users\peter\OneDrive\Desktop\AI_Projects\ios2pixel\platform-tools"
$iconPath = "c:\Users\peter\OneDrive\Desktop\AI_Projects\ios2pixel\app_icon.ico"
$readmePath = "c:\Users\peter\OneDrive\Desktop\AI_Projects\ios2pixel\README.md"

if (Test-Path $targetDir) {
    Remove-Item $targetDir -Recurse -Force
}
New-Item -ItemType Directory -Path $targetDir -Force | Out-Null

Write-Host "Copying binaries from dist..."
Copy-Item (Join-Path $srcDist "Any2Pixel.exe") $targetDir -Force
Copy-Item (Join-Path $srcDist "_internal") $targetDir -Recurse -Force

Write-Host "Copying platform-tools..."
Copy-Item $platformTools $targetDir -Recurse -Force

Write-Host "Copying icon and readme..."
Copy-Item $iconPath $targetDir -Force
Copy-Item $readmePath $targetDir -Force

$batLines = @(
    "@echo off",
    "REM Any2Pixel Pro V1.0.0 Portable Launcher",
    "cd /d ""%~dp0""",
    "start """" ""Any2Pixel.exe"""
)
[System.IO.File]::WriteAllLines((Join-Path $targetDir "Run_Any2Pixel.bat"), $batLines, [System.Text.Encoding]::ASCII)

Write-Host "Creating zip archive..."
$zipPath = "c:\Users\peter\OneDrive\Desktop\AI_Projects\Release\Any2Pixel_V1.0.0_Portable.zip"
if (Test-Path $zipPath) {
    Remove-Item $zipPath -Force
}
Compress-Archive -Path "$targetDir\*" -DestinationPath $zipPath -Force

Write-Host "Syncing to ios2pixel/Release..."
$subRelease = "c:\Users\peter\OneDrive\Desktop\AI_Projects\ios2pixel\Release\Any2Pixel_V1.0.0_Portable"
$subZip = "c:\Users\peter\OneDrive\Desktop\AI_Projects\ios2pixel\Release\Any2Pixel_V1.0.0_Portable.zip"
if (Test-Path $subRelease) {
    Remove-Item $subRelease -Recurse -Force
}
New-Item -ItemType Directory -Path "c:\Users\peter\OneDrive\Desktop\AI_Projects\ios2pixel\Release" -Force | Out-Null
Copy-Item $targetDir "c:\Users\peter\OneDrive\Desktop\AI_Projects\ios2pixel\Release\" -Recurse -Force
Copy-Item $zipPath $subZip -Force

Write-Host "Package completed successfully."
Get-Item $zipPath | Select-Object Name, Length, LastWriteTime
