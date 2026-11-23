# AI 自我端到端實測驗收鐵律 (Mandatory AI Self-Verification & Evidence Acceptance Rule)

## 1. 核心精神與最高禁令
* **核心宗旨**：使用者是系統的**最終驗收者與需求決策者**，絕非免費用戶測試員！
* **最高禁令（嚴禁推諉測試）**：
  * **嚴格禁止**在僅完成程式碼編寫、尚未親自執行端到端實測前，便向使用者回報「請你幫我測測看」、「你可以試試看」、「請測試看看是否正常」！
  * 凡是要交付給使用者的任何功能、改動或 Bug 修復，**AI 必須自己扮演第一位最嚴苛的測試員，親自跑過完整生命週期測試驗收通過後，始得回報**。

---

## 2. 標準 3 步自我驗收閉環 (3-Step Self-Acceptance Pipeline)

任何功能交付前，必須強制在背景依序執行以下 3 步：

### 步驟 1：真實探測 (Live End-to-End Probe)
* 透過本機指令或腳本（PowerShell / Python / Node / curl / adb），對目標裝置、伺服器、API 端點或 DOM 發起真實請求。
* 絕不以靜態推論當作已驗收，必須拿到實際的 HTTP Status Code、Response Body、或行程 Process 狀態。

### 步驟 2：嚴格斷言與殘留掃描 (Strict Assertion & Residue Scan)
* **正向斷言 (Positive Assertion)**：
  * 目標新增的功能、文字、數值、按鈕或卡片，必須經由程式斷言（`assert in`）確實存在且回傳格式正確。
* **負向斷言 (Negative / Residue Assertion)**：
  * 若任務要求「刪除某個按鈕」、「移除某段文字」或「清理某個檔案」，必須主動執行負向斷言，驗證目標字串在產出的 HTML / 源碼 / 目錄中**出現次數嚴格為 0**！

### 步驟 3：佐證回報 (Hard Evidence Presentation)
* 向使用者回報結論時，必須包含自我驗收的**終端輸出日誌截錄（Verbatim Test Excerpt）**作為客觀證據。
* 清楚交代驗收通過的項目、實測數值與時間戳記，杜絕任何空泛的口頭保證。
