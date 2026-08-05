# 《無盡洪荒：五行聖境》 - 滾動版本備份腳本 (保留前5次)
# 用法: .\backup.ps1 [-Reason "備份原因"]
param(
    [string]$Reason = "手動備份"
)

$backupRoot = "$PSScriptRoot\backups"
$gameDir = $PSScriptRoot

if (-not (Test-Path $backupRoot)) {
    New-Item -ItemType Directory -Path $backupRoot -Force | Out-Null
}

$existingVersions = @(Get-ChildItem -Path $backupRoot -Directory | 
    Where-Object { $_.Name -match "^v\d+$" } | 
    Sort-Object { [int]($_.Name -replace "v","") })

if ($existingVersions.Count -ge 5) {
    $oldest = $existingVersions[0]
    Remove-Item -Recurse -Force $oldest.FullName
    Write-Host "移除最舊備份: $($oldest.Name)" -ForegroundColor Yellow
    $existingVersions = @($existingVersions | Select-Object -Skip 1)
}

$maxVer = 0
if ($existingVersions.Count -gt 0) {
    $nums = $existingVersions | ForEach-Object { [int]($_.Name -replace "v","") }
    $maxVer = ($nums | Measure-Object -Maximum).Maximum
}
$newVer = "v$($maxVer + 1)"
$newBackupDir = Join-Path $backupRoot $newVer
New-Item -ItemType Directory -Path $newBackupDir -Force | Out-Null

foreach ($file in @("game.js", "index.html", "styles.css")) {
    $src = Join-Path $gameDir $file
    if (Test-Path $src) {
        Copy-Item -Path $src -Destination $newBackupDir -Force
        Write-Host "備份: $file -> $newVer" -ForegroundColor Green
    }
}

$timestamp = Get-Date -Format "yyyy-MM-dd HH:mm:ss"
"備份時間: $timestamp`n版本號: $newVer`n備份原因: $Reason" | 
    Out-File -FilePath (Join-Path $newBackupDir "info.txt") -Encoding UTF8

Write-Host "備份完成！版本: $newVer" -ForegroundColor Cyan
Get-ChildItem -Path $backupRoot -Directory | Sort-Object { [int]($_.Name -replace "v","") } | 
    Select-Object Name, LastWriteTime | Format-Table -AutoSize
