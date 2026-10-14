# 黑盒子旁路崩潰診斷最高鐵律 (Blackbox Out-of-Process Crash Diagnostics Rule)

本規範為全專案所有涉及「啟動崩潰」、「自重啟失敗」、「靜默閃退」與「開機殼層 (Bootloader/Loader) 異常」之最高強制執行標準。
當應用程式層日誌一片空白、尚未初始化即退出時，嚴禁盲目猜測或在程式碼中反覆修改無效的延遲與重試，必須強制調用獨立旁路監控儀進行作業系統級解剖取證。

---

## 1. 核心物理原則：零盲猜與旁路監控 (Zero-Guessing & Out-of-Process Principle)

* **日誌空白之本質**：
  當應用程式日誌（如 `update_debug.log`、`.log`）在中途斷掉或一片空白時，**100% 代表問題發生在「應用程式 Runtime 啟動之前」**（例如 PyInstaller C Bootloader、JVM Bootstrap、CLR Loader 或 Win32 `LoadLibrary` 階段）。
* **禁止在黑盒子階段盲猜**：
  **嚴格禁止**在沒有抓到 OS 層真實進程狀態前，憑空猜測「快一秒慢一秒」、「可能是路徑不對」、「可能是權限問題」。
* **唯一合規手段：旁路黑盒子外部監控 (Out-of-Process Monitor)**：
  必須透過獨立於主程式之外的外部守護進程（如 PowerShell / OS Probe），在不侵入業務程式碼的前提下，從作業系統外部全程錄製進程生滅與視窗事件。

---

## 2. 旁路黑盒子監控儀標準架構 (Blackbox Architecture Contract)

凡建立旁路監控儀（如 `tools/update_monitor.ps1`），必須嚴格具備以下 5 大取證能力：

### 1. 進程樹與父子層級審查 (PROC_AUDIT)
* 透過 WMI / CIM (`Win32_Process`) 向上遞迴反查主程式之父進程 ID（PPID）與進程名稱。
* 監控並區分多進程架構中的「引導殼層 (Parent Bootloader)」與「執行業務之子進程 (Child Process)」。

### 2. 環境與暫存目錄物理清點 (ENV_AUDIT)
* 監控並記錄系統環境變數與暫存目錄（如 `%TEMP%\_MEI*`）的物理存在狀態、檔案數量與建立時間。
* 嚴格以「磁碟檔案系統物理存在與否」作為判定解壓與載入的客觀依據，嚴禁僅憑執行秒數推論。

### 3. 進程生命週期精確毫秒釘死 (EVENT_EXIT & PARENT_EXIT)
* 監聽舊子進程退出的精確時間戳。
* 監聽舊父進程（Bootloader）在子進程退出後的清理時間與完全退出時間，記錄釋放延遲（毫秒）。

### 4. 新進程拉起與碰撞審查 (NEW_PROC_SPAWNED)
* 捕獲新進程啟動時之 PID、父進程 ID、完整 CommandLine。
* 記錄新進程啟動瞬間，系統中舊進程群的存活狀態（`TERMINATED` 或 `STILL_ALIVE_COLLISION`）。

### 5. 系統 Error 對話框主動攔截 (ERROR_DIALOG_DETECTED)
* 透過 Win32 API (`EnumWindows` / `EnumChildWindows`) 主動掃描作業系統頂層視窗與 MessageBox (`#32770`)。
* 攔截彈窗之精確標題、彈窗所屬進程 PID、對話框靜態文字（Static Text），並在彈窗瞬間傾印磁碟目錄與環境狀態。

---

## 3. 防重複多開與看門狗退場鐵律 (Single-Instance & Watchdog Guard)

外部監控程式必須具備高度自治與自毀能力，杜絕產生殭屍進程或多開泛濫：

1. **跨進程具名互斥鎖 (Global Named Mutex)**：
   監控儀第一行代碼必須宣告系統級具名 Mutex（如 `Global\..._UpdateMonitor_Mutex`）。若偵測到系統已有另一實例在運行，本實例必須立即安全退出（`exit 0`），保證系統中永遠只有一個監控實例。
2. **主進程心跳綁定 (Target Process Liveness Guard)**：
   若主程式被使用者手動關閉（點擊右上角 X），且 5 秒內無新進程拉起，判定為正常使用退出，監控儀必須釋放鎖並立即自我終止。
3. **對話框關閉同動 (Window Close Hook)**：
   在使用者關閉觸發頁面（如關閉更新中心）時，主程式必須主動觸發結束訊號，做到視窗關閉、監控即滅。
4. **絕對超時硬上限 (Hard Timeout Watchdog)**：
   監控儀必須內建最大計時器（預設 120 秒）。超時後無條件自我銷毀，絕不在背景常駐。

---

## 4. 分析報告產出契約 (Post-Mortem Evidence Contract)

當問題排查完成，向使用者回報結論時，必須嚴格依序引用：
1. **外部監控日誌毫秒時間戳 (Verbatim Monitor Excerpt)**。
2. **磁碟物理存在狀態 (Physical Disk Existence Proof)**。
3. **Win32 視窗文字與 PID 歸屬 (Dialog Title, Message & Window PID)**。
4. **Runtime / C 語言開機源碼之對應分支 (Underlying Source Branch)**。
嚴格杜絕在未列出上述 4 點前輸出猜測性結論。
