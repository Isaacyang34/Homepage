# LED Studio Pro - PowerShell 備份和管理腳本
# 使用方式:
#   1. 以管理員身份打開 PowerShell
#   2. cd C:\Users\Isaac Yang\Documents\homepage
#   3. .\manage-versions.ps1 [命令]
#
# 可用命令:
#   backup      - 備份當前版本
#   restore     - 還原到指定版本
#   list        - 列出所有備份版本
#   compare     - 比較兩個版本
#   clean       - 刪除舊備份（保留最近 5 個）
#   help        - 顯示此幫助信息

param(
    [string]$Command = "help"
)

$SourceFile = "LED螢幕設計界面規劃V1.1.html"
$BackupDir = "backups"
$VDiff = "code --diff"  # 使用 VS Code 比較

function Create-Backup {
    Write-Host "`n🔄 正在備份..." -ForegroundColor Cyan
    
    if (-not (Test-Path $BackupDir)) {
        New-Item -ItemType Directory -Path $BackupDir | Out-Null
    }
    
    if (-not (Test-Path $SourceFile)) {
        Write-Host "✗ 錯誤: 找不到 $SourceFile" -ForegroundColor Red
        return $false
    }
    
    $timestamp = Get-Date -Format "yyyyMMdd_HHmmss"
    $backupName = "LED_V1.1_$timestamp.html"
    $backupPath = Join-Path $BackupDir $backupName
    
    Copy-Item $SourceFile $backupPath
    
    $fileSize = (Get-Item $backupPath).Length / 1KB
    Write-Host "✓ 備份成功！" -ForegroundColor Green
    Write-Host "  位置: $backupPath" -ForegroundColor Gray
    Write-Host "  大小: $([Math]::Round($fileSize, 2)) KB" -ForegroundColor Gray
    
    return $true
}

function List-Backups {
    Write-Host "`n📋 備份版本列表:" -ForegroundColor Cyan
    
    if (-not (Test-Path $BackupDir)) {
        Write-Host "  (無備份)" -ForegroundColor Gray
        return
    }
    
    $backups = Get-ChildItem $BackupDir -Filter "LED_V1.1_*.html" -File | Sort-Object LastWriteTime -Descending
    
    if ($backups.Count -eq 0) {
        Write-Host "  (無備份)" -ForegroundColor Gray
        return
    }
    
    $backups | ForEach-Object {
        $index = [Array]::IndexOf($backups, $_) + 1
        $date = $_.LastWriteTime.ToString("yyyy-MM-dd HH:mm:ss")
        $size = [Math]::Round($_.Length / 1KB, 2)
        Write-Host "  [$index] $($_.Name)" -ForegroundColor White
        Write-Host "       修改時間: $date | 大小: $size KB" -ForegroundColor Gray
    }
    
    Write-Host ""
}

function Restore-Version {
    Write-Host "`n🔄 還原版本:" -ForegroundColor Cyan
    List-Backups
    
    $choice = Read-Host "輸入要還原的版本號（或按 Ctrl+C 取消）"
    
    if (-not $choice) { return }
    
    $backups = Get-ChildItem $BackupDir -Filter "LED_V1.1_*.html" -File | Sort-Object LastWriteTime -Descending
    
    if ([int]$choice -gt $backups.Count -or [int]$choice -lt 1) {
        Write-Host "✗ 無效的版本號" -ForegroundColor Red
        return
    }
    
    $selectedBackup = $backups[[int]$choice - 1]
    
    # 備份當前版本
    Write-Host "  正在備份當前版本..." -ForegroundColor Yellow
    Create-Backup | Out-Null
    
    # 還原選定版本
    Copy-Item $selectedBackup.FullName $SourceFile -Force
    Write-Host "✓ 還原成功！" -ForegroundColor Green
    Write-Host "  已還原: $($selectedBackup.Name)" -ForegroundColor Gray
}

function Compare-Versions {
    Write-Host "`n📊 版本比較:" -ForegroundColor Cyan
    List-Backups
    
    $choice = Read-Host "輸入要比較的版本號"
    
    if (-not $choice) { return }
    
    $backups = Get-ChildItem $BackupDir -Filter "LED_V1.1_*.html" -File | Sort-Object LastWriteTime -Descending
    
    if ([int]$choice -gt $backups.Count -or [int]$choice -lt 1) {
        Write-Host "✗ 無效的版本號" -ForegroundColor Red
        return
    }
    
    $selectedBackup = $backups[[int]$choice - 1]
    
    Write-Host "  正在打開 VS Code 比較工具..." -ForegroundColor Yellow
    Write-Host "  左側: $SourceFile (當前)" -ForegroundColor Gray
    Write-Host "  右側: $($selectedBackup.Name) (備份)" -ForegroundColor Gray
    
    & code --diff $SourceFile $selectedBackup.FullName
}

function Clean-Old-Backups {
    Write-Host "`n🧹 清理舊備份..." -ForegroundColor Cyan
    
    $backups = Get-ChildItem $BackupDir -Filter "LED_V1.1_*.html" -File | Sort-Object LastWriteTime -Descending
    $toDelete = $backups | Select-Object -Skip 5
    
    if ($toDelete.Count -eq 0) {
        Write-Host "✓ 備份數量正常，無需清理" -ForegroundColor Green
        return
    }
    
    Write-Host "  將刪除 $($toDelete.Count) 個舊備份（保留最近 5 個）" -ForegroundColor Yellow
    
    $confirm = Read-Host "確認刪除？(y/n)"
    if ($confirm -ne "y") {
        Write-Host "已取消" -ForegroundColor Yellow
        return
    }
    
    $toDelete | ForEach-Object {
        Remove-Item $_.FullName
        Write-Host "  ✓ 已刪除: $($_.Name)" -ForegroundColor Gray
    }
    
    Write-Host "✓ 清理完成！" -ForegroundColor Green
}

function Show-Help {
    Write-Host @"

╔════════════════════════════════════════════════════════════════════╗
║          LED Studio Pro - 版本管理腳本                            ║
╚════════════════════════════════════════════════════════════════════╝

使用方式:
  .\manage-versions.ps1 [命令]

可用命令:
  backup       - 備份當前版本
  restore      - 還原到指定備份版本
  list         - 列出所有備份版本
  compare      - 使用 VS Code 比較兩個版本
  clean        - 刪除舊備份（保留最近 5 個）
  help         - 顯示此幫助信息

範例:
  .\manage-versions.ps1 backup     # 創建新備份
  .\manage-versions.ps1 list       # 列出備份
  .\manage-versions.ps1 restore    # 還原版本

提示:
  • 每次修改前建議先執行 backup
  • 備份會自動保存到 backups 目錄
  • 可以用 compare 查看版本差異

"@ -ForegroundColor Cyan
}

# 主程式
switch ($Command.ToLower()) {
    "backup" { Create-Backup }
    "list" { List-Backups }
    "restore" { Restore-Version }
    "compare" { Compare-Versions }
    "clean" { Clean-Old-Backups }
    "help" { Show-Help }
    default { 
        Write-Host "✗ 未知的命令: $Command" -ForegroundColor Red
        Show-Help
    }
}
