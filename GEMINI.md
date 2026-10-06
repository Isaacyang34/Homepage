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

## 4. 通用 UI 設計與防裁切排版規範 (UI Layout Robustness - 全案子最高強制規範)
* **根容器物理互斥鐵律 (Root TableLayoutPanel 唯一架構)**：
  * **嚴格禁止**直接在 `Form.Controls` 上掛載多個 `Dock` 控制項（WinForms 原生 Dock 在 Z-Order 上會產生浮層遮擋，硬生生蓋住中央畫布與內容）！
  * 凡具有「側邊欄 + 中央內容 + 底部狀態列」的多區域介面，**第一行代碼必須強制宣告 `Root TableLayoutPanel` (或 `SplitContainer`) 作為頂層根網格**，將視窗切分為互斥儲存格，從物理結構上讓遮擋發生的機率為 0%！
* **內部容器百分比網格 (全面廢除 Point(X,Y))**：
  * **嚴禁**任何手動累加靜態座標（如 `y += 105; Location = new Point(10, y)`）。
  * 所有 GroupBox 與 Panel 內部一律強制使用 `TableLayoutPanel`（一個蘿蔔一個坑）或垂直 `FlowLayoutPanel`，元件空間物理互斥，嚴禁重疊。
* **文字與表格防裁切保證**：
  * CheckBox 與 Label 若文字較長，必須給予獨立單列 (Row) 或充足寬度（>250px），嚴禁多欄擠壓導致文字截斷（如「標籤顯示」被吃掉）。
  * DataGridView 必須為欄位明確設置 `FillWeight` 與 `MinimumWidth`，防止欄位標題受擠壓縮水。
* **雙保險滾動機制**：所有容器面板一律預設啟用 `AutoScroll = true`，防止 DPI 縮放破版。
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
* **發布執行檔 Git 追蹤與 .gitignore 永久白名單 (Binary Tracking & Whitelist Integrity - 杜絕 404)**：
  * **根目錄白名單保證**：根目錄 `.gitignore` 必須永久設置 `!Dyanmometer/Release/**/*.exe` 與 `!Release/**/*.exe` 白名單，嚴禁被 `*.exe` 全域忽略規則吞沒。
  * **暫存強制驗證防呆**：`package_release.ps1` 暫存指令一律使用 `git add -f "Dyanmometer/Release/"`；且在 `commit` 前必須主動執行 `git ls-files --stage` 驗證目標版號之 `Dynamometer_HMI_Pro.exe` 確實已被 Git 暫存追蹤，若未追蹤直接拋出致命錯誤中斷，嚴禁盲目推送空目錄或幽靈發布！
* **線上發布 200 OK 實測探測閉鎖 (Post-Release 200-OK Probe Lockout - 嚴禁無效 URL)**：
  * **實測探測鐵律**：凡更新 Firebase RTDB `/update/version.json` 之 `download_url`，**必須在更新前/後立即對該 Raw 下載網址發起 HTTP HEAD 實測探測**！
  * **200 OK 驗證閉鎖**：當且僅當遠端伺服器回傳 **`HTTP 200 OK` 且 Content-Length > 500KB** 時，才視為線上發布真正成功；若探測為 404 或異常，嚴禁向使用者回報發布成功，必須立即發出致命告警並排查 Git 推送與 CDN 狀態！

## 8. 嚴禁未經指示擅自開啟瀏覽器鐵律 (Zero Unauthorized Browser Launch - 全案最高強制規範)
* **嚴禁主動調用瀏覽器工具**：**嚴格禁止**在未獲得使用者明確文字指令（例如明確要求「請打開瀏覽器測試」或「用瀏覽器查看」）的情況下，擅自調用 `browser_subagent` 或透過任何指令在本地彈出瀏覽器視窗！
* **靜默與離線驗證原則**：所有 HTML、JavaScript、CSS 或前端應用之邏輯修復、資料解析與演算法測試，**一律限於背景透過本機腳本（PowerShell / Node.js 等靜態分析或單元驗證）無聲完成**，絕對禁止跳出任何瀏覽器視窗奪取作業系統焦點或干擾使用者工作！
* **回報即止原則**：前端程式碼與樣式修改完成後，僅需清楚條列修改內容、邏輯佐證與本地檔案路徑，**由使用者完全自主決定何時開啟檢視**，嚴禁代為做主開啟！

## 9. WinForms .NET 4.0 GDI+ UI 控制項 Emoji 完全禁用鐵律 (Anti-Emoji UI Rendering - 全案最高強制規範)
* **核心物理限制**：Windows XP + .NET Framework 4.0 + GDI+ 的 WinForms 渲染引擎，**對 Unicode Emoji（U+1F300 以上之多位元組符號，包含 🚀 📸 🛑 🤖 🌐 ☁️ 📶 💾 🔄 🔍 等）的渲染行為不可預測**：
  * 在 Button、Label、CheckBox 等標準 WinForms 控制項內，Emoji 字元可能導致整行文字**完全不渲染（文字消失，只顯示背景色）**，即使不報任何錯誤！
  * 這是 GDI+ 字型 fallback 機制在 XP 上的已知缺陷，無法透過字型替換或 DoubleBuffer 修復。
* **禁用範圍（嚴格執行，無例外）**：
  * **嚴禁**在任何 `Button.Text`、`Label.Text`、`CheckBox.Text`、`GroupBox.Text`、`TabPage.Text` 中使用 **Emoji 或 Unicode 特殊裝飾符號（U+1F000 以上）**。
  * **嚴禁**使用任何多碼點組合 Emoji（如 🕵️‍♂️ 含 ZWJ 組合），這類符號在 .NET 4.0 字串處理中亦可能引發長度計算錯誤。
* **允許使用的替代符號（明確白名單）**：下列 ASCII/BMP 範圍符號在 WinForms GDI+ 下渲染正常，可作為 Emoji 的替代：

  | 用途 | 禁用 Emoji | 允許替代 |
  |---|---|---|
  | 警示/急停 | 🛑 | ` ■ 急停` 或文字 |
  | 啟動/計算 | 🚀 | `>> 計算` 或文字 |
  | 上傳/雲端 | ☁️ | `[雲] 上傳` 或文字 |
  | 讀取/連線 | 🔄 | `[->] 連線` 或文字 |
  | 擷取/拍照 | 📸 | `[*] 擷取` 或文字 |
  | 自動化/機器人 | 🤖 | `[AI] 自適應` 或文字 |
  | 警告/提示 | ⚠️ | `[!] 警告` 或文字 |
  | 狀態圓點 | 🔴🟡🟢 | 使用 GDI+ `OnPaint` 自繪圓形，或用 `[X]` `[-]` `[O]` 文字替代 |

* **AI 自我強制審查鐵律 (Self-Check Before Write — 全案最嚴執行)**：
  * **【接觸前掃描】凡開始讀取或編輯任何 WinForms `.cs` 檔案，第一個動作必須是用 `grep_search` 對整個檔案搜尋 Emoji 字元（搜尋關鍵字為 `Text = "` 配合 Emoji 範圍），列出全部違規行，一次性批量清除後，才允許進行業務邏輯修改**。
  * **【輸出前審查】每次生成或修改任何 WinForms 控制項 `Text` 屬性時，在輸出前必須自我逐行掃描輸出內容是否包含 U+1F000 以上字元**。
  * **發現 Emoji 必須立即替換，嚴禁以「在 Windows 10 上可以渲染」為由保留**，因現場環境永遠是 Windows XP。
  * **【擴散清除】已存在的 Emoji 如在相關檔案上做任何修改時，必須順手清除整個檔案中所有 Emoji，不得只改當次目標函式或目標行**。
## 10. 零偽造與零竄改實測物理量鐵律 (Zero-Fallback & Zero-Tampering Telemetry Integrity Rule - 全案最高強制規範)
* **嚴禁預設值與展示用假數據**：嚴禁在任何 EEPROM 初始化、`setup()` 流程、網頁端、上位機或正則解析中，預先寫死或硬編碼任何展示用假數據（如寫死 28°C、0.0 psi、25°C 兜底值、或假目標 ID）。
* **實測物理量誠實空白原則**：未收到空中真實封包或硬體感測器真實回傳前，狀態一律標記為 `valid = false`，畫面上誠實呈現 `--.- psi / -- °C (等待訊號)`，嚴禁自行腦補或替換歷史值/額定值欺騙使用者！
* **未經授權嚴禁擅自燒錄**：ESP32 等硬體燒錄必須由使用者給予明確文字指示授權，嚴禁自作主張於背景發起燒錄。

## 11. TPMS 射頻協定實測基準與 Issue #3496 永久綁定規範 (TPMS Protocol Integrity Rule - 全案最高強制規範)
* **唯一參照來源 (Single Source of Truth for TPMS)**：
  * 本專案 TPMS 射頻逆向工程唯一權威參考文件永久存放於：
    `ESP32_TPMS_Receiver/docs/RTL_433_ISSUE_3496_CMT2220LY_REFERENCE.md`
  * 原始依據為 GitHub 官方 Issue: `https://github.com/merbanan/rtl_433/issues/3496`。
* **演算法核心三鐵律 (嚴禁自行臆測公式)**：
  1. **相鄰異或解擾 (Adjacent XOR Whitening)**：射頻 Payload 第一動作必須執行 `rawData[i] = payload[i] ^ payload[i+1]` 解擾，嚴禁用原始位元直接硬轉數值。
  2. **非線性平方律壓力公式**：壓力一律使用 $\text{Pressure (Bar)} = (\text{rawData}[4])^2 \times 0.00005$；桌上未充氣狀態 $\text{rawData}[4] = 0 \rightarrow 0.00\text{ Bar} (0.0\text{ psi})$，嚴禁使用線性乘法猜測。
  3. **溫度零點偏移 110**：溫度公式嚴格為 $\text{Temp } (^\circ\text{C}) = \text{rawData}[5] - 110$；嚴禁臆測其他 offset。
* **架構分工鐵律 (免重複燒錄原則)**：
  * **ESP32 韌體**：專職做高頻無線電採樣前端，負責輸出客觀微秒時序 `[TIMING_RAW]` 與原始電文 `[RAW_BITS_HEX]`，**嚴禁每改一次解碼邏輯就重複編譯燒錄 ESP32**！
  * **上位機 GUI (`TPMS_Serial_Monitor_GUI.cs`)**：專職負責軟體定義解碼 (Software-Defined Decoder)，提供多策略即時重算，任何新公式或參數調整一律在上位機秒級驗證完成！

## 12. 單檔原子化整合同動修改鐵律 (Single-Pass Atomic Edit Rule - 全案最高強制規範)
* **嚴禁零碎拖沓分段修訂**：凡涉及同一檔案之多處變更（包含成員變數宣告、UI 版面建立、事件監聽、資料解析與輔助函式），必須事前通盤規劃掌握行號與結構，**強制使用一次性批量編輯工具一次原子化修改完成**！
* **杜絕「查一段改幾行」的惡習**：嚴格禁止「讀取一次、改 7 行、再讀一次、改 66 行、再讀一次、改 2 行」的碎步拖延操作，確保每一次代碼修改都是完整、清晰、可追溯且一次通過編譯驗證！

## 13. Windows 批次檔 (.bat) 與啟動器 100% 純 ASCII 與零亂碼鐵律 (Zero-Encoding-Glitch Batch & Launcher Rule - 全案最高強制規範)
* **核心物理限制 (cmd.exe & PS5.1 缺陷)**：Windows `cmd.exe` 預設以 ANSI/CP950 (Big5) 讀取批次檔，遇 UTF-8 中文必發生位元組錯位，引發指令崩潰與 `'cho' 不是內部或外部命令`；PowerShell 5.1 無 BOM 亦會誤判為 Big5 導致亂碼。
* **.bat 批次檔 100% 絕對純 ASCII (Zero Non-ASCII)**：
  * **嚴禁**在任何 `.bat` 或 `.cmd` 中寫入任何中文字元、全形標點或特殊符號。
  * **嚴禁**使用 `::` 作為註解（易被 cmd 誤判執行）；一律使用純英文 `REM` 或不寫註解。
  * **標準啟動範本**：一律採用純英文 4 行結構：
    ```cmd
    @echo off
    title <Project Name>
    cd /d "%~dp0"
    python <entry_script>.py
    pause
    ```
* **主控台中文與瀏覽器開啟全權由 Python 接管**：
  * 視窗中文橫幅與狀態輸出，一律由 Python 啟動檔宣告 `sys.stdout.reconfigure(encoding='utf-8')`，透過 Windows Unicode API 輸出，保證 0% 亂碼。
  * 瀏覽器自動開啟一律由 Python 在伺服器就緒後調用 `webbrowser.open(url)`，杜絕在 `.bat` 中使用脆弱的 `timeout` 與 `start`。
* **詳細規範**：參考 `.agents/rules/zero_encoding_glitch_batch_launcher_rule.md`。

## 14. GitHub 發布授權與地端優先驗證鐵律 (Local-First Verification & Explicit Release Authorization - 全案最高強制規範)
* **未經指示嚴禁擅自發布 (Zero Unauthorized Push)**：
  * **嚴格禁止**在使用者未明確提出「發布到 GitHub」、「推送到 GitHub」等文字指示前，自作主張執行 `git push`、建立 GitHub Release 或觸發任何線上發布流程！
  * 任何程式改動、功能新增或 Bug 修復，**預設一律只在本地端完成開發、編譯與除錯**，嚴禁擅自推送到遠端倉庫浪費 Token 與網路頻寬。
* **地端驗證優先原則 (Local-First Testing & Verification)**：
  * 很多功能（包含 GUI 排版、即時報價、通訊連線、演算法與圖表渲染）**必須由使用者在本地端親自實測運行確認成功**，無法單憑模型推論保證 100% 完美。
  * 開發完成後僅需：
    1. 在本地完成編譯、打包或腳本測試。
    2. 提供清晰的本地檔案路徑與啟動方式。
    3. 提醒使用者在地端執行驗證。
* **發布閉環標準流程 (Strict Release Gate)**：
  * **本地修改與編譯** $\rightarrow$ **地端測試通過** $\rightarrow$ **等待使用者驗證反饋** $\rightarrow$ **【當且僅當使用者明確要求發布】** $\rightarrow$ **始得執行雙分支同動推送 (`gh-pages` + `master`)**。
* **詳細規範**：參考 `.agents/rules/local_first_verification_rule.md`。

## 15. 密碼學安全實踐與威脅建模鐵律 (Cryptographic Security & Threat Modeling Rule - 全案最高強制規範)
* **嚴禁虛假「零破口」宣稱 (Zero-Fallback Threat Modeling)**：凡涉及端到端加密 (E2EE)、雲端同步與金鑰管理，必須落實客觀威脅建模，清楚定義攻擊面；嚴禁在未實現 1Password Secret Key 熵值時妄自宣稱「完全等同 1Password」。
* **公開可讀雲端防暴破標準 (KDF $\ge$ 600,000 & Dual-Entropy Key)**：
  * 若密文存於公開/無速率限制之雲端，攻擊者必可進行無上限離線暴力窮舉。
  * `PBKDF2-HMAC-SHA256` 疊代次數強制遵守 OWASP 2023 最新標準 **$\ge 600,000$ 次**（或採用 Argon2id）。
  * 必須引進類似 1Password 之 **128-bit 設備 Secret Key** 雙因子金鑰衍生（$\text{MasterPassword} + \text{SecretKey}$），徹底封死單純依賴字典檔離線破解的可能性。
* **AES-GCM AAD 綁定與防回滾 (AAD & Anti-Rollback Integrity)**：
  * 嚴禁無 AAD 的 GCM 加密，必須將 `userId`、`version`、`timestamp` 綁定為 AAD 納入 Auth Tag 計算，杜絕跨帳號密文搬移。
  * 密文內部強制包含遞增單調計數器 (Monotonic Counter)；解密端發現版本逆行立即中斷並警示回滾攻擊。
* **長度側信道防禦 (Padding to Fixed Block)**：加密前明文強制進行 PKCS#7 Padding 至固定塊大小（如 32 KB / 64 KB），抹平持股筆數與交易規模特徵。
* **雲端防列舉與寫入閉鎖 (Cloud Access Lockout)**：Firebase RTDB 必須強制封鎖根目錄遍歷列舉（`.read: false` on root），寫入操作必須強制驗證獨立 `write_token`，嚴禁任何未授權使用者覆寫或惡意清空資料。
* **詳細規範**：參考 `.agents/rules/cryptographic_security_and_threat_modeling_rule.md`。

## 16. 應用程式圖標 (Icon) 生成無外框與滿版鐵律 (Zero-Framed Icon Integrity Rule - 全案最高強制規範)
* **嚴禁任何手機卡片/圓角外框**：凡為桌面應用、Web Favicon 或系統生成任何 Icon，**嚴格禁止生成任何帶有圓角矩形 (Squircle)、App 浮層卡片外框、手機圖示外襯、或周圍多餘留白/邊界的圖像**！
* **100% 滿版或透明去背原則 (Full-Bleed or Transparent)**：
  * 圖標圖形與背景紋理**必須 100% 滿版無縫延伸至圖片的四個邊界 (Full bleed, Edge-to-edge)**，或直接採用透明去背 (Transparent Alpha)。
  * 主視覺符號（如幣別符號、走勢圖、Logo）居中飽滿呈現，絕不允許出現「在一個背景上方浮著一個圓角卡片」的二重外框醜態！
* **生成 Prompt 強制關鍵字約束**：每次生成 Icon 時，Prompt 必須明確加入：`FULL BLEED, EDGE TO EDGE. ABSOLUTELY NO rounded square frame, NO squircle border, NO app icon container frame, NO outer border or surrounding margin.`
