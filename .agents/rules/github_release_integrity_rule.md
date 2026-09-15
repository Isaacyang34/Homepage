# GitHub 雙分支同動與線上發布完整性規範 (GitHub Dual-Branch & Online Release Integrity Rule)

## 核心宗旨
本規範旨在杜絕「推送了 GitHub 卻在網頁上看不到」、「線上更新偵測不到最新版」、「PAT 憑證被版面儲存清空」以及「Secret Scanning 阻擋推版」等致命問題。凡涉及 GitHub 程式碼提交、版本發布或線上更新，本規範擁有全案最高強制力。

---

## 1. 雙分支強制同動鐵律 (Dual-Branch Push Policy)
- **背景現狀**：
  GitHub 遠端儲存庫（`Isaacyang34/Homepage`）之預設展示分支為 `master`，而 GitHub Pages 靜態託管分支為 `gh-pages`。
- **鐵律要求**：
  1. 任何時候向 GitHub 遠端進行推送，**嚴格禁止只推單一分支**。
  2. 必須一律同時向 `gh-pages` 與 `master` 兩大分支推送：
     ```powershell
     git push origin gh-pages
     git push origin gh-pages:master --force
     ```
  3. 全案通用推送腳本（如 `push_to_github.bat` 與 `Dyanmometer/package_release.ps1`）必須強制包含雙分支推送指令，確保網頁端預設檢視（`master`）與 Pages（`gh-pages`）完全 100% 鏡像同步。

---

## 2. 發布三端原子同動 (Three-Point Release Alignment)
每當打包發布新版本或提供線上更新功能時，必須在同一個動作內同步更新以下三端指標，嚴禁任何一端遺漏或版本不一：

1. **本機源碼與編譯 Binary**：
   - `Dynamometer_WebServer.cs` 之 `APP_VERSION`（例如 `"2.10.43"`）。
   - 編譯產出之 `Dynamometer_HMI_Pro.exe` 必須內嵌此版本號。
2. **雲端版本指標 (Firebase RTDB)**：
   - `/update/version.json` 之 `version` 欄位必須同步更新。
3. **GitHub Official Release**：
   - 建立對應之 Git Tag（如 `v2.10.43`）與 GitHub Release。
   - 上傳最新打包之 `Dynamometer_HMI_Pro.exe` 與便攜包作為 Release Asset。
4. **線上更新測試遞增原則**：
   - 線上更新檢測引擎邏輯為 `V_cloud > V_local`。
   - 若使用者要求實測線上更新流程，發布到雲端的版本號必須嚴格大於本機目前運行的版本號，防止因「版本已是最新」而導致無法觸發更新。

---

## 3. 機密防護與 PAT 憑證安全 (Zero-Leak PAT & INI Integrity)
- **零洩漏防護 (Zero Secret Scanning Alerts)**：
  - Git 追蹤的任何檔案（包括 `dynamometer_layout.ini`）中的 `Token=` 欄位必須永久保持空白。
  - 程式碼內部使用 XOR 混淆（`GetEmbeddedToken()`）作為內部備援機制，嚴禁明文寫入 `ghp_` 等 Token 字串，徹底杜絕觸發 GitHub Push Protection。
- **設定檔非 UI 區段保護 (INI Preservation)**：
  - WinForms 於關閉或重置版面時觸發 `SaveLayoutConfig()`。
  - 儲存時**必須先行快取並完整回寫所有非 UI 區段**（如 `[GitHub]`、`[ReportManager]`、`[GoogleDrive]`），嚴禁因重構或重寫 INI 而抹除使用者配置的 PAT 與外部服務設定。

---

## 4. 發布執行檔 Git 追蹤與 .gitignore 白名單鐵律 (Binary Tracking & Whitelist Integrity)
- **核心痛點**：
  若 `.gitignore` 設定了 `*.exe` 全域忽略，未設置例外白名單，且打包腳本執行 `git add` 時漏加 `-f`，會導致編譯出的最新版主程式未被加入 Git 暫存區，遠端分支無此檔案，客戶端線上更新即遭遇「HTTP 404 Not Found」。
- **強制規範**：
  1. **根目錄 `.gitignore` 永久白名單**：
     必須永久保留發布目錄執行檔之白名單例外：
     ```gitignore
     *.exe
     !Dyanmometer/Release/**/*.exe
     !Release/**/*.exe
     ```
  2. **打包腳本強制暫存與預檢**：
     `package_release.ps1` 執行暫存必須帶有 `-f` 參數：
     ```powershell
     & $gitExe -C $repoRoot add -f "Dyanmometer/Release/"
     ```
     並且在 `commit` 之前必須執行 `git ls-files --stage` 驗證目標版號之 `Dynamometer_HMI_Pro.exe` 確實已被 Git 暫存追蹤；若未追蹤直接報警中斷，嚴禁盲目發布。

---

## 5. 線上發布 200 OK 實測探測閉鎖鐵律 (Post-Release 200-OK Probe Lockout)
- **核心痛點**：
  若在發布腳本中單向將下載 URL 寫入 Firebase RTDB，卻未在實際伺服器驗證該 URL 是否真正具備有效二進位檔案，將導致客戶端下載失敗。
- **強制規範**：
  1. **發布閉環探測**：
     凡更新 Firebase RTDB `/update/version.json` 之 `download_url`，**腳本必須立即對該 Raw 下載網址發起 HTTP HEAD 實測探測**（最多重試 5 次以容許 CDN 同步延遲）。
  2. **200 OK 驗證閉鎖**：
     當且僅當遠端伺服器回傳 **`HTTP 200 OK` 且 Content-Length > 500KB** 時，才視為發布成功！若探測非 200 或異常，嚴禁向使用者宣告發布成功，必須立即發出致命告警並排查 Git 推送與 CDN 狀態！

