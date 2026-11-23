# 工作區環境純淨防護與專案資料物理封裝鐵律 (Workspace Cleanliness & Project Isolation Rule)

> **全案最高強制規範**：本工作區（`AI_Projects`）涵蓋硬體（ESP32）、工控桌面（WinForms）、Python 便攜應用與 Web 儀表板等數十個獨立專案。為徹底杜絕檔案散落、根目錄污染與專案產物錯置，所有 AI 代理人與開發流程必須強制恪守本規範。

---

## 1. 專案產物嚴格自洽封裝 (Strict Project Locality & Self-Containment)
* **專案資料與 Release 永遠放一起**：
  * 每個專案的源代碼、資源檔案、說明文件、自動化測試腳本、建置腳本，以及編譯產出之二進位檔、便攜包或 **`Release/` 目錄，必須 100% 嚴格封裝在該專案自身目錄內部**！
  * **範例**：
    * 動力計 HMI 釋出：`Dyanmometer/Release/`（嚴格屬於 `Dyanmometer/`）
    * iOS2Pixel 便攜釋出：`ios2pixel/Release/` 或 `ios2pixel_pro/Any2Pixel_Pro_V1.1_Portable/`
    * 股票投資儀表板發布：`TOOL_Stock_Portfolio_Tracker/dist/`
* **根目錄 Release 資料夾絕對禁用**：
  * **嚴格禁止在工作區根目錄（`AI_Projects/`）建立或放置 `Release/` 資料夾**！
  * 根目錄絕不是任何專案的打包暫存場或發布跳板，專案資料必須與其發布產物嚴格收斂在專案子目錄內。

---

## 2. 過渡檔案用畢即清鐵律 (Zero-Lingering Artifacts & Auto-Purge)
* **禁止留存任何開發過渡產物**：
  * 開發、偵錯、除錯、視覺辨識（CV/OCR）、時序採樣或自動化測試期間產生的所有過渡檔案，包含：
    1. **臨時截圖與裁切圖**：`*.jpg`、`*.png`、`crop_*.jpg`、`sample_*.jpg`、`multi_*.jpg`、`check_*.jpg`、`temp_*.jpg` 等。
    2. **臨時測試腳本與程式**：`test.html`、`test_*.py`、`temp_*.ps1`、臨時編譯之 `*.exe`、臨時 `.bat`。
    3. **過渡性封裝或二進位檔**：除正式安裝於專案內部依賴外的臨時 `.apk`、臨時 `.zip`、臨時 `.rar`。
* **同 Turn 自動清空（不用等使用者提醒）**：
  * 當單元測試、視覺驗證或打包流程完成後，**必須在同一個 Turn 內主動調用指令將所有過渡檔案與暫存檔案徹底刪除**！
  * 絕不允許任何一張測試截圖或臨時程式留在磁碟上過夜，保持工作區與專案目錄永遠純淨。

---

## 3. 工作區根目錄純淨防線 (Clean Workspace Root Guard)
* **根目錄合法清單白名單**：
  工作區根目錄（`AI_Projects/`）僅允許以下三類檔案與目錄存在：
  1. **全域與版本控制配置**：`GEMINI.md`、`.gitignore`、`.git/`、`.github/`、`.vscode/`、`.agents/`。
  2. **GitHub Pages 門戶資產**：`index.html`、`styles.css`、`WORKSPACE_DIRECTORY_STRUCTURE.md` 及門戶入口直接引用的公開展示單頁（如 `WebMonitor.html`、`release_stats.html` 等）。
  3. **標準分類專案目錄**：`ESP32_*`、`WEB_*`、`GAME_*`、`DOC_*`、`TOOL_*`、`Dyanmometer`、`ios2pixel*` 等具備獨立資料夾之專案。
* **嚴禁隨地落子**：
  * 嚴格禁止直接在根目錄下新建任何孤立的功能程式、除錯檔案、測試程式或臨時資料夾。
  * 任何新功能或測試必須在對應專案目錄（或 `TOOL_Scratch/` 臨時試驗區）內進行。

---

## 4. 自我審查與發布前檢查清單 (Pre-Commit & Pre-Response Checklist)
每次在回報任務完成或進行 Git 提交前，AI 必須自我確認：
- [ ] 根目錄是否有任何未歸類的 `*.jpg`、`*.png`、`*.exe`、`*.html` 或 `*.apk`？
- [ ] 根目錄是否出現了違規的 `Release/` 資料夾？
- [ ] 當次測試過程中的臨時裁切圖、除錯日誌與過渡腳本是否已 100% 調用指令刪除？
- [ ] 所有的專案產物是否均已收斂在各自專案子目錄中？
