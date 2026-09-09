# Dynamometer_HMI_WinForms.cs — 備份與還原指南

## 備份資訊

| 項目 | 值 |
|:---|:---|
| **備份版本** | v2.6.5 (V2.5 beta) |
| **備份時間** | 2026-09-04 15:40 |
| **備份檔大小** | 758,115 bytes (13,110 行) |
| **備份路徑** | `Dyanmometer_Modern/Dynamometer_HMI_WinForms_MONOLITH_BACKUP_v2.6.5.cs` |

---

## 🔄 快速還原指令 (PowerShell，從 Dyanmometer 根目錄執行)

```powershell
Copy-Item "Dyanmometer_Modern\Dynamometer_HMI_WinForms_MONOLITH_BACKUP_v2.6.5.cs" "Dyanmometer_Modern\Dynamometer_HMI_WinForms.cs" -Force; Write-Host "✅ 還原完成！"
```

### 還原後同時清理 partial 檔案
```powershell
Remove-Item "Dyanmometer_Modern\Dynamometer_KebComm.cs" -ErrorAction SilentlyContinue
Remove-Item "Dyanmometer_Modern\Dynamometer_Telemetry.cs" -ErrorAction SilentlyContinue
Remove-Item "Dyanmometer_Modern\Dynamometer_TestTN.cs" -ErrorAction SilentlyContinue
Remove-Item "Dyanmometer_Modern\Dynamometer_TestDuty.cs" -ErrorAction SilentlyContinue
Remove-Item "Dyanmometer_Modern\Dynamometer_TestEffMap.cs" -ErrorAction SilentlyContinue
Remove-Item "Dyanmometer_Modern\Dynamometer_UIControls.cs" -ErrorAction SilentlyContinue
Write-Host "✅ 已回到 Monolith 架構"
```

### 驗證還原成功
```powershell
(Get-Item "Dyanmometer_Modern\Dynamometer_HMI_WinForms.cs").Length
# 應顯示：758115
```

---

## 📦 拆分後重新編譯

方法不變，package_release.ps1 已自動納入所有 partial 檔案：
```powershell
powershell -ExecutionPolicy Bypass -File "package_release.ps1" -Version "2.5.0"
```

## 📁 拆分後檔案結構

```
Dyanmometer_Modern/
├── Dynamometer_HMI_WinForms.cs                         ← 主體：欄位+Form初始化+主Timer
├── Dynamometer_HMI_WinForms_MONOLITH_BACKUP_v2.6.5.cs  ← ✅ 備份（勿刪除）
├── Dynamometer_KebComm.cs                               ← partial: KEB 通訊層
├── Dynamometer_Telemetry.cs                             ← partial: 遙測/CSV/日誌
├── Dynamometer_TestTN.cs                                ← partial: T-N 曲線測試
├── Dynamometer_TestDuty.cs                              ← partial: DUTY 工作制測試
├── Dynamometer_TestEffMap.cs                            ← partial: 效率地圖測試
├── Dynamometer_UIControls.cs                            ← 獨立 class: 所有 UserControl
└── tools/Dynamometer_Device_Tester_GUI.cs
```
