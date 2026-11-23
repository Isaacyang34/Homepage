# 跨對話上下文專案鎖定與雲端日誌即時提取鐵律 (Context-Project Lock & Instant Cloud-Log Fetch Rule)

## 1. 核心宗旨
本規範為杜絕 AI 受到 IDE 背景開啟之分頁元數據（Active Document）干擾而發生「專案跳槽誤判」，並確保當使用者於機台上位機（Dynamometer HMI）點擊【日誌上傳】時，AI 能 100% 零延遲直連雲端端點提取真實數據，嚴禁任何脫靶猜測與工作區外部盲目搜尋。

---

## 2. 跨對話上下文專案唯一鎖定 (Conversation Context > Editor Tab Metadata)
* **對話主題為最高仲裁基準**：
  * 在多專案工作區（`AI_Projects`）中，AI 判定當前任務專案的最高依據**一律為「整段對話的歷史演進、正在解決的問題與先前的討論上下文」**。
* **嚴禁「編輯器分頁元數據錨定偏誤」 (Zero Tab Metadata Anchor Bias)**：
  * 使用者可能在編輯器中同時開啟其他專案的檔案進行查閱（如 `TOOL_EV_Inspection_Classifier`、`TOOL_Stock_Portfolio_Tracker` 等）。
  * **嚴格禁止**僅憑系統附帶的 `Active Document` 或其他 open documents，就擅自推論「使用者切換了專案」！
* **專案切換必須有明確文字指示 (Explicit Switch Authorization)**：
  * 當且僅當使用者在對話中**以明確文字下達切換指令**（例如「切換到 EV 專案」、「改處理分類器問題」），始得變更專案上下文。
  * 若無明確切換指示，當前對話之專案主體永遠嚴格鎖定於原專案（如 `Dyanmometer` 動力計專案）。

---

## 3. 「日誌上傳」專屬雲端端點零秒直連 (Zero-Second Direct Cloud-Log Fetch)
* **專屬推播端點標準契約**：
  * 馬達動力計 HMI 內建 Wi-Fi 雲端日誌發送器，使用者於介面點擊【日誌上傳】（或【☁️ 上傳日誌至雲端】）時，當次測試之 CSV 實測表與 `hmi_telemetry.log` 會即刻推播至 Firebase RTDB：
    ```text
    https://dynamometer-live-default-rtdb.asia-southeast1.firebasedatabase.app/logs/latest.json
    ```
* **命中關鍵字立即直連 (Instant Fetch Triggers)**：
  * 凡收到包含「已上傳」、「日誌上傳」、「點了上傳」、「看最新日誌」、「分析測試」等指令，**第一動作必須在同一個 Turn 內直接向上述雲端 URL 發起 HTTP GET 請求**！
* **直連解析三要素**：
  1. `csv_content`：提取當次測試之各相電氣量（$U, I, P_1..P_3, P_{\Sigma}, \text{PF}$、頻率等實測矩陣）。
  2. `log_content`：提取 HMI 事件、看門狗保護、KEB 通訊握手與例外堆疊。
  3. `upload_time`：確認上傳時間戳記，佐證數據新鮮度。
* **嚴禁本地盲翻**：
  * **嚴禁**在此時要求使用者提供本地檔案、**嚴禁**在本地磁碟盲目掃描、更**嚴禁**違反第 18 條鐵律跑到桌面或無關資料夾搜尋！

---

## 4. 舊日誌路徑反向污染防禦 (Anti-Log-Path Contamination)
* 嚴禁讀取某個歷史日誌（如某專案舊 log 出現 `C:\Users\peter\OneDrive\Desktop\資料整理`）後，未經思考就將該外部路徑當作當前任務目標。
* 所有診斷分析必須閉鎖於當前專案與對應的專屬雲端通道之內。
