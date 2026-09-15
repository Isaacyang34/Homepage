# 全專案通用自動化測試腳本生成規範 (Universal Test Script Generation Rule)

當使用者在任何專案下達「生成自動測試腳本」或相關自動化測試指令時，必須嚴格遵守以下規範產出程式碼與配置。

---

## 1. 核心四大架構原則

### 原則一：嚴禁使用螢幕絕對座標 (Window Anchoring & Rel-Pos)
* **禁止**：使用螢幕絕對座標 (如 `click(1920, 1080)`)。
* **規範**：
  1. **首選 UIA / 控制項 ID**：優先透過 `AutomationId`、`Name`、`ClassName` 抓取元件。
  2. **視窗錨定**：一律以 `Process.Id (PID)` 或 `HWND` 錨定目標視窗，嚴禁依賴容易受語系/編碼影響的靜態視窗標題字串。
  3. **次選視窗相對座標**：若無控制項 ID，以目標視窗左上角為 `(0, 0)` 計算偏移量 `(x_offset, y_offset)`。

### 原則二：標準四階段生命週期 (Standard 4-Phase Lifecycle)
每個測試腳本必須嚴格劃分四階段：
1. **Setup (環境準備)**：啟動軟體/網頁，取得 PID/HWND，確認視窗存在並置頂。
2. **Action (動作執行)**：依序執行操作（點擊、文字輸入、發送指令），按鍵之間加入 `50~100ms` 緩衝時間防止漏步。
3. **Assert & Log Harvest (斷言與日誌回收)**：
   * **UI 狀態檢查**：驗證介面元件狀態與數值。
   * **日誌 Log-First 檢查**：讀取軟體即時 `.log`，提取 HEX 電文、Ack/Quittung 與 Exception 狀態。
4. **Teardown (狀態恢復/關閉)**：截圖存證、安全中斷連線或關閉進程，確保環境乾淨。

### 原則三：日誌回收與證據留存 (Log-Driven Evidence)
* 執行中若發生 `FAIL` 或 `ERROR`：
  1. 自動觸發當前視窗截圖（存至 `reports/screenshots/`）。
  2. 精準截取最近產生的錯誤日誌或通訊封包 HEX 作為證據。

### 原則四：標準回傳資料契約 (Standard Return Contract)
所有產出的情境腳本函式 `run_scenario()`，必須固定回傳符合此規格的字典結構：
```python
return {
    "status": "PASS",               # "PASS" | "FAIL" | "ERROR"
    "scenario": "情境名稱",
    "details": "執行細節與說明",
    "log_excerpt": "[HEX] 02 06 01 A0 ... Ack OK", # 提取的日誌證據
    "screenshot": "reports/screenshots/pass_xxx.png"
}
```

---

## 2. 實戰踩坑防護鐵律 (Anti-Bug & Resiliency Rules)

### 鐵律 2.1：雙軌原生執行器支援 (PowerShell + Python)
* 在 Windows 桌面軟體測試場景，必須優先提供 **Windows 原生 0 相依性的 PowerShell 腳本 (`.ps1`)**，避免目標機台未安裝 Python 或僅有 Windows Store 佔位符導致執行失敗。

### 鐵律 2.2：字串與剪貼簿強型別轉型 (Type Safety & Out-String)
* 在 PowerShell 讀取外部輸出或剪貼簿時，**嚴禁直接假設為字串**：
  * ❌ 錯誤：`$res = Get-Clipboard; $res.Trim()` (為空時會是 `[Object[]]` 拋出 MethodInvocationException)
  * ✔️ 正確：`[string]$res = (Get-Clipboard | Out-String).Trim()`

### 鐵律 2.3：純 ASCII 與編碼防呆
* 腳本內與命令列通訊若使用 PowerShell，視窗啟動一律使用 `Process.Id` 啟用，避免中文字串在 ANSI/UTF-8 解析時產生 `MissingEndParenthesisInMethodCall` 語法錯誤。

---

## 3. 模式二：Macro 錄製與 Pipeline 編排原則
* **原子化原則**：單一 Macro 腳本只記錄單一功能模組（如：僅做連線、僅做加壓）。
* **容錯間隔**：關鍵視窗彈出或通訊請求處預留 `0.5s ~ 1.0s` 緩衝時間。
* **Pipeline 配置**：使用標準 JSON 宣告執行順序、重試次數與循環圈數。
