# Isaacyang34/Homepage Workspace Instructions

## 1. 通用日誌驅動分析規範 (Universal Log-Driven Analysis Workflow - 全案子最高強制規範)
* **Log-First 原則**：當使用者回報問題或測試結果時，**第一優先動作必須主動定位並同時讀取 `Auto_Raw_Telemetry_*.csv` 與最新 `.log` 紀錄檔**，嚴禁在未分析真實遙測 CSV 前憑空猜測。
* **標準 5 步鐵律 (5-Step Pipeline - 嚴禁跳步)**：
  1. **Locate (定位)**：自動掃描最近 30 分鐘內產生的 **`Auto_Raw_Telemetry_*.csv` (AUTOLOG)**、`.log` 與截圖。
  2. **Extract (提取)**：提取 CSV 各相電氣量 ($U, I, P_1..P_3, P_{\Sigma}, \text{PF}$)、HEX 原始電文、狀態碼 (Ack/Quittung) 與例外堆疊。
  3. **Cross-Reference (交叉驗證)**：將實測封包與 CSV 數值交叉比對原廠手冊與驅動源碼。
  4. **Transparent Report (佐證回報)**：回報時必須先引用 AUTOLOG CSV 實測數據行（Verbatim Excerpt）作為佐證依據。
  5. **Auto-Purge & Teardown (用畢即清 - 強制工具執行)**：**在輸出任何分析結論前，必須先主動調用指令將所有用畢之 `logs/*`（包含所有臨時 `.csv`、`.log`）與暫存截圖（`.jpg`/`.png`）徹底刪除清空**！保持發布與測試目錄永遠純淨，絕不讓使用者開口提醒！
* **單一整合日誌鐵律 (Single Unified Log Principle - 嚴禁多檔拆分)**：
  * **嚴格禁止**同一測試程式同時產出多個日誌檔（例如禁止拆分為 `AUTO_*.csv` 與 `*.log`）。
  * 所有的**狀態事件 (EVENT)**、**通訊握手 (HEX)** 與**各相遙測數值 (TELEMETRY)**，必須一律整合寫入**單一 `.csv` 整合日誌檔**中！
* **零垃圾日誌原則**：禁止每毫秒無意義刷屏，只留狀態變更、HEX 電文與異常報錯。

## 2. Dynamometer Project Iteration Rules
* **Source**: `Dyanmometer/Dyanmometer_Modern/Dynamometer_HMI_WinForms.cs` & `tools/Dynamometer_Device_Tester_GUI.cs`
* **Build**: Always compile with `csc.exe` linking both files into `Dynamometer_HMI_Pro.exe`.
* **Release**: Execute `package_release.ps1 -Version <TargetVersion>` to package the active target release (e.g. `Release/Dynamometer_HMI_V2.5.0_Portable/`). Do NOT overwrite historical archive releases.
* **Testing Target**: User tests via `Release/Dynamometer_HMI_V2.5.0_Portable/Dynamometer_HMI_Pro.exe` directly (Native winexe, no .vbs needed).

## 3. 通用自動化測試腳本生成規範 (Universal Test Script Generation - 全案子適用)
* 當收到**「生成自動測試腳本」**或自動化測試指令時，必須強制遵守 **AI 邏輯控制腳本建立原則**：
  1. **禁止絕對螢幕座標**：採用 UIA 控制項或視窗相對座標 `(x_offset, y_offset)`。
  2. **標準 4 階段生命週期**：`Setup` -> `Action` -> `Assert & Log Harvest` -> `Teardown`。
  3. **日誌回收與證據留存**：提取 HEX 通訊電文、Ack/Quittung、Exception 與失敗自動截圖。
  4. **標準回傳契約**：統一回傳包含 `status`、`scenario`、`details`、`log_excerpt`、`screenshot` 之字典結構。
* **詳細規範**：參考 `.agents/rules/universal_test_script_generation_rule.md`。

## 4. 通用 UI 設計與防裁切排版規範 (UI Layout Robustness - 全案子適用)
* **嚴禁靜態 Y 座標**：全面廢除手動計算之 `Point(X, Y)`，強制採用三段式 `Dock (Top / Bottom / Fill)` 容器。
* **雙保險安全機制**：所有容器面板一律預設啟用 `AutoScroll = true`，防止 DPI 縮放破版。
* **彈性按鈕網格**：按鈕群一律使用 `TableLayoutPanel` / `Flexbox Grid` 百分比分割，確保核心操作按鈕 100% 可見。
* **詳細規範**：參考 `.agents/rules/ui_layout_robustness_rule.md`。

## 5. 變更歷程即時記錄鐵律 (Mandatory Real-Time Changelog Documentation - 全案最高強制規範)
* **唯一存放路徑 (Single Source of Truth)**：**全案變更紀錄唯一存放於專案源碼根目錄 `Dyanmometer/CHANGELOG.md`**，嚴禁分散存於其他路徑。
* **發布目錄防污染鐵律 (Clean Release Policy)**：**`Release/` 目錄僅供原生便攜執行（只允許存在 `.exe` 與 `DLL/` 驅動函式庫），嚴禁放置 `CHANGELOG.md` 或任何開發文檔！**
* **Changelog-Atomic 鐵律 (同動更新)**：**凡是有任何程式碼、功能、參數或錯誤修復之改動，必須在同一個 Turn 內「原子化同步更新 `Dyanmometer/CHANGELOG.md`」，嚴禁拖延、事後補記或讓使用者提醒！**
* **未記 Log 視同未完成**：若未在 `Dyanmometer/CHANGELOG.md` 中詳細記錄修改內容、實測日誌依據、根本原因 (Root Cause) 與修復細節，該次修改視同未完成，**嚴禁執行打包發布或向使用者回報結論**！
* **記錄三要素 (嚴格遵循)**：每次記錄必須精確包含：
  1. **現象與佐證**：使用者回報問題現象與提取之實測數據/日誌行。
  2. **致命根因 (Root Cause)**：明確點出哪個檔案、哪個函式、因何邏輯/算式/暫存器地址寫錯。
  3. **精確修復方案**：具體改動了哪些運算邏輯、對應修正之版本號與發布路徑。
* **詳細規範**：參考 `.agents/rules/mandatory_changelog_documentation_rule.md`。

## 6. Windows XP 向下相容最高鐵律 (Windows XP Legacy Compatibility - 全案最高強制規範)
* **唯一工作電腦環境**：現場機台與測試電腦作業系統**一律為 Windows XP (x86 32-bit)**，所有開發、測試、編譯與通訊**必須 100% 永久相容 Windows XP**！
* **.NET 4.0 執行期約束**：僅支援 .NET Framework 4.0，**嚴禁**引用 .NET 4.5+ 專屬類別（如 `HttpClient` 等高版本庫），嚴格使用 `/platform:x86` 編譯。
* **Schannel / TLS 加密協定物理限制 (核心重點)**：
  * Windows XP 系統原生 `Schannel.dll` **僅支援 SSL 3.0 / TLS 1.0，完全無原生 TLS 1.2 / TLS 1.3 支援**！
  * **嚴禁**假設 Windows 原生 `HttpWebRequest` 能直連現代雲端（如 Firebase 等強制要求 TLS 1.2 的服務），否則必觸發 `The underlying connection was closed: An unexpected error occurred on a send`。
  * 對外雲端通訊須透過 XP 相容方案（如本機獨立 OpenSSL 代理 stunnel、本地中繼、或區域網路轉發）。
* **UI 與字體相容**：避免使用高版本 Windows 專用字體與特殊 Emoji 符號，全自繪控制項強制啟用 `DoubleBuffered = true` 避免在 XP GDI 下畫面閃爍。
* **詳細規範**：參考 `.agents/rules/windows_xp_compatibility_rule.md`。

## 7. GitHub 雙分支同動與線上發布完整性鐵律 (GitHub Dual-Branch & Online Release Integrity - 全案最高強制規範)
* **雙分支強制同動 (Mandatory Dual-Branch Push - 永不漏推 master)**：
  * GitHub 遠端儲存庫預設展示分支為 `master`，而 GitHub Pages 部署分支為 `gh-pages`。
  * **凡是推送到 GitHub，必須同時推送到 `gh-pages` 與 `master` 兩大分支**（例如執行 `git push origin gh-pages` 與 `git push origin gh-pages:master --force`），絕對嚴禁只推單一分支，確保網頁端預設檢視永遠為最新進度！
* **發布三端原子同動 (Three-Point Release Alignment - 嚴禁版本斷層)**：
  * 每次執行版本發布 (`package_release.ps1`) 或線上更新發布時，必須同時且一致完成三端同動：
    1. **源碼與編譯檔**：`Dynamometer_WebServer.cs` 中之 `APP_VERSION`、編譯出之 `Dynamometer_HMI_Pro.exe`。
    2. **雲端版本指標**：Firebase RTDB 之 `/update/version.json` 中的 `version` 欄位。
    3. **GitHub 官方發布**：GitHub Releases 之 Tag（如 `v2.10.43`）與其附加的發布執行檔。
  * **線上更新驗證遞增原則**：若使用者需要實測線上更新功能，雲端版本必須嚴格大於本機版本 ($V_{cloud} > V_{local}$)，禁止因版本相同而導致按鈕反灰或跳過更新。
* **機密防護與 PAT 永久保存 (Zero-Leak PAT & INI Integrity)**：
  * **嚴禁明文 Token 上傳**：Git 追蹤之 `dynamometer_layout.ini` 中的 `Token=` 必須永久為空，程式碼內部一律採用 XOR 編碼混淆（`GetEmbeddedToken()`），嚴格杜絕觸發 GitHub Secret Scanning Push Protection 阻擋提交。
  * **設定檔防覆寫保護**：WinForms 在執行 `SaveLayoutConfig()` 儲存視窗版面時，必須完整保留並回寫所有非 UI 區段（如 `[GitHub]`、`[ReportManager]`、`[GoogleDrive]`），嚴禁截斷或清空使用者的 PAT 與外部配置。
