---
name: blackbox-crash-diagnostics
description: Out-of-process blackbox diagnostics for application startup crashes, restart failures, silent exits, and bootloader/loader errors before application logging initializes. Use when application logs are empty or cut off, when a program crashes on boot or restart, or when diagnosing Win32 LoadLibrary/DLL loading issues.
---

# Blackbox Crash Diagnostics (黑盒子旁路崩潰診斷技術)

## Overview

當應用程式在啟動、熱重載或自我重啟時發生崩潰，且應用層日誌（如 Python、C#、Node.js 等）一片空白或提前中斷時，問題 100% 發生在「開機殼層 (Bootloader/Bootstrap)」或「作業系統進程派生期 (OS Process Forking/Creation)」。此時在應用程式內部修改程式碼或盲目猜測延遲無效，必須啟動獨立於目標程式之外的「旁路黑盒子外部監控儀 (Out-of-Process Blackbox Monitor)」，從作業系統底層錄製進程生滅、環境塊傳遞、暫存目錄生命週期與 Win32 系統彈窗。

---

## When to Use

1. 應用程式在點擊「自我重啟 (Self-Restart)」或「線上更新 (Apply Update)」時崩潰或彈出系統 Error。
2. 應用程式啟動即閃退，且本機應用日誌（`update_debug.log`、`app.log`）完全沒有記錄或只記錄到啟動前。
3. 彈出 Windows 系統對話框（如 `Failed to load Python DLL`、`LoadLibrary 126`、`0xC0000005 記憶體衝突`）。
4. PyInstaller `--onefile` 多進程環境目錄（`_MEI*`）遺失、鎖定或無法載入模組。
5. 排查多進程架構中父引導進程 (Parent Bootloader) 與子進程 (Child Process) 的清理延遲與環境污染。

---

## The 4-Stage Blackbox Diagnostic Workflow

```
[Trigger Event]
       │
       ▼
┌────────────────────────────────────────────────────────┐
│ Stage 1: Launch Independent Out-of-Process Monitor     │
│ - Acquire Global Named Mutex (single-instance guard)   │
│ - Inspect initial Process Tree (PID & PPID)            │
│ - Take snapshot of %TEMP% directory state              │
└────────────────────────────────────────────────────────┘
       │
       ▼
┌────────────────────────────────────────────────────────┐
│ Stage 2: Track Process Exit & Cleanup Latency          │
│ - Monitor Child Process exit timestamp (EVENT_EXIT)    │
│ - Monitor Parent Bootloader exit (PARENT_EXIT)         │
│ - Measure exact cleanup delay (ms)                     │
└────────────────────────────────────────────────────────┘
       │
       ▼
┌────────────────────────────────────────────────────────┐
│ Stage 3: Audit New Process Spawn & Collision           │
│ - Capture New Process PID, Parent PID, CommandLine     │
│ - Audit if old parent process was TERMINATED or ALIVE  │
│ - Audit if disk extraction directories were created    │
└────────────────────────────────────────────────────────┘
       │
       ▼
┌────────────────────────────────────────────────────────┐
│ Stage 4: Win32 Dialog Interception & Post-Mortem Report│
│ - Intercept Class #32770 MessageBox via Win32 API      │
│ - Extract Dialog Title & Static Text verbatim          │
│ - Dump physical disk state at the exact millisecond    │
│ - Correlate with runtime C/Native bootloader branches  │
└────────────────────────────────────────────────────────┘
```

---

## Standard Monitor Implementation Template (`update_monitor.ps1`)

監控儀必須採用純 ASCII 腳本（防範 Windows CP950/Big5 編碼錯位），具備以下核心架構：

```powershell
param(
    [string]$TargetDir = "",
    [int]$TargetPid = 0
)

# 1. 跨進程具名 Mutex (防多開)
$createdNew = $false
$mutex = New-Object System.Threading.Mutex($true, "Global\App_UpdateMonitor_Mutex", [ref]$createdNew)
if (-not $createdNew) {
    exit 0
}

# 2. Win32 視窗文字攔截器
Add-Type @"
using System;
using System.Text;
using System.Runtime.InteropServices;

public class Win32Helper {
    public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);
    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);
    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)] public static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);
    [DllImport("user32.dll", SetLastError = true)] public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hWnd);
    public static string GetDialogText(IntPtr hWnd) {
        StringBuilder sb = new StringBuilder(1024);
        GetWindowText(hWnd, sb, 1024);
        return sb.ToString();
    }
}
"@ -ErrorAction SilentlyContinue

# 3. 核心監控迴圈 (心跳、父進程退出延遲、新進程捕獲、彈窗攔截)
# (詳見專案 tools/update_monitor.ps1 完整實作)
```

---

## Defensive Engineering Solutions

當黑盒子日誌證實是開機殼層環境污染或進程鏈問題時，遵循以下定式解決：

1. **脫離當前進程鏈，委託 Windows Desktop Shell 拉起**：
   嚴禁由即將死去的子進程或其派生的背景腳本直接 `CreateProcess`。
   一律透過 Windows Shell COM 介面委託桌面 Explorer 發起全新頂層進程：
   ```powershell
   $shell = New-Object -ComObject Shell.Application
   $shell.ShellExecute($exePath, "", $targetDir, "open", 1)
   ```
2. **無重啟純熱替換 (Zero-Restart Modular Patching)**：
   若更新僅涉及二進位封裝包（如 `app_core.pkg`）或設定檔，主程式 binary 根本未變，嚴禁主動強制重啟，改為提示「下次開啟自動生效」或動態重新載入模組。
3. **看門狗保底防殭屍**：
   監控儀必須內建：
   * 目標 PID 消失且無新進程則 5 秒自滅。
   * 更新頁面關閉時主動調用關閉訊號。
   * 120 秒絕對硬超時上限。
