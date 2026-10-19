# 線上更新日誌純淨繁體中文與零亂碼鐵律 (Zero-Mojibake Release Notes & Manifest Integrity Rule)

## 1. 核心物理限制與致命根因 (Root Cause)
* **現象**：現場工控機與動力計 HMI (`Dynamometer_WebServer.cs`) 點選「線上更新」彈出更新對話框時，更新說明內容呈現為 `[V2.155 beta / v2.10.113] 1. 蝘?????` 雙重轉碼亂碼 (Mojibake)。
* **致命根因 1 (表格行提取器誤過濾)**：
  * `CHANGELOG.md` 自最新版起改用 Markdown 表格記錄（`| Beta 版本 | 發布版號 | 變更日期 | 變更內容與依據 |`）。
  * 舊版打包腳本 `package_release.ps1` 使用 `-notlike "*Beta*"` 試圖過濾表頭，但 PowerShell 字串比對預設大小寫不敏感，導致真實版本行 `V2.175 (beta)` 因含有 `(beta)` 而被全面誤殺！
  * 表格提取落空後，腳本回退到歷史章節 `## [V...]`，掃到第 65 行歷史舊版 `## [V2.155 beta / v2.10.113]`，而該區塊在過去曾被舊工具以錯誤編碼寫入過亂碼。
* **致命根因 2 (PowerShell 編碼與跳脫漏洞)**：
  * Windows PowerShell 預設命令列輸出採用 ANSI/CP950，當將 UTF-8 中文字元直接以 JSON 字串拼裝送入 WebRequest 時，極易發生代碼頁截斷與錯位。

## 2. 繁體中文與零亂碼發布五大鐵律 (Zero-Mojibake 5-Pillars)
1. **表格行正則鎖定 (Table-First Priority)**：
   * 變更日誌提取器必須優先解析 `CHANGELOG.md` 頂部表格第一列。
   * 必須使用精確版本正則匹配：`$pBeta -match '^V\d+' -and $pBeta -notlike "*---*" -and $pVer -notlike "*---*"`，嚴禁使用模糊的 `-notlike "*Beta*"` 誤殺包含 `(beta)` 之真實版次。
2. **純符號項目解析 (Encoding-Agnostic Bullet Parsing)**：
   * 提取「3)精確修復方案」時，嚴禁依賴腳本內部硬編碼之中文字串比對（避免腳本自身存檔編碼不一致）。
   * 強制採用純數字符號正則：`$t -match '^\s*3\)'` 標記方案段落開始，`$t -match '^\s*\d+\.'` 提取序號條目。
3. **Unicode Escape 傳輸防護 (\uXXXX Encoding)**：
   * 在將 Release Notes 寫入 Firebase RTDB `/update/version.json` 之 JSON 前，所有非 ASCII 字元（`char > 127`）強制由腳本轉換為 `\uXXXX` 轉義格式（Unicode Escape Sequences）。
   * 杜絕任何作業系統環境代碼頁、PowerShell 5.1 或 HTTP Client 傳輸時的亂碼風險；WinForms 端反序列化時會 100% 原生無損還原為純淨繁體中文。
4. **硬性零亂碼閉鎖防呆 (Zero-Mojibake Lockout Guard)**：
   * 在寫入 Firebase 前，必須對 `latestNotes` 逐字進行字元碼檢查。
   * 凡檢測到 Unicode 私用區字元（PUA: `0xE000` ~ `0xF8FF`）或未定義字元（`0xFFFD`），強制認定為 Mojibake 損壞，嚴禁上傳！自動回退為純淨繁體中文發布範本：
     ```text
     [v<APP_VERSION>] (<DATE>)
     * 馬達動力計測試系統 V<APP_VERSION> 更新完成
     1. 核心測試與報表功能強化
     2. 系統通訊與操作穩定性提升
     3. 100% 相容 Windows XP 與零亂碼規範
     ```
5. **發布後回讀實測驗證 (Post-Publish Read-Back Probe)**：
   * 每次發布更新 Firebase RTDB 後，必須透過 HTTP GET 立即回讀 `/update/version.json`，並驗證 `notes` 內容與長度，確保現場工控機讀取到的更新資訊永遠是 100% 正確的繁體中文。
