# 軟體版本更新與發布管理最高規範 (Software Versioning & Release Integrity Rule)

## 1. 核心原則 (Core Philosophy)
全案嚴格遵循國際 **語意化版本 2.0.0 (Semantic Versioning 2.0.0 - SemVer)** 規範，版本號格式統一為：
$$\text{v}\mathbf{MAJOR}.\mathbf{MINOR}.\mathbf{PATCH}$$
廢除內部雙軌版號（嚴禁同時出現 `V2.245 (beta)` 與 `v2.10.167` 混淆認知），全專案唯一以 `vX.Y.Z` 為正式版號。

---

## 2. 版本號晉級三大鐵律 (Version Bump Rules)

| 版號層級 | 格式 | 觸發條件 | 範例與承諾 |
| :--- | :--- | :--- | :--- |
| **MAJOR (主版號)** | `vX.0.0` | **不相容的架構變更 (Breaking Change)**<br>• 通信協議封包格式重構，舊韌體/舊設備無法直接對接。<br>• 系統設定檔格式重寫，舊版 layout.ini 無法相容。<br>• 核心作業系統環境或底層依賴重大斷層切換。 | `v2.0.0` $\rightarrow$ `v3.0.0`<br>使用者升級須注意相容性。 |
| **MINOR (次版號)** | `v2.X.0` | **向下相容的重大功能模組新增 (New Feature Milestone)**<br>• 新增獨立測試功能項目（如 DUTY 全自動排程系統、S6 週期負載、等效電路自動推算）。<br>• 新增獨立安全保護機制（如軸承熱源逆轉、失步卡死、雙階過電流保護）。<br>• 新增重要互動介面（如獨立安全保護設定分頁）。<br>• *晉級時 PATCH 強制歸零*。 | `v2.5.0` $\rightarrow$ `v2.11.0` $\rightarrow$ `v2.12.0`<br>100% 向下相容，安心升級使用新功能。 |
| **PATCH (修訂號)** | `v2.11.X` | **向下相容的 Bug 修復與細節微調 (Bug Fix & Hotfix)**<br>• UI 元件排版修復、文字被邊界截斷修正。<br>• 演算法公式細微常數修正、正則解析防呆。<br>• 異常日誌排查後的防禦性程式碼補強。<br>• *每次修補遞增 1*。 | `v2.11.0` $\rightarrow$ `v2.11.1` $\rightarrow$ `v2.11.2`<br>純修復，無功能異動。 |

### 嚴格禁止的錯誤行為 (Anti-Patterns)
1. **嚴禁將每次內部微調都當作新發布或隨意飆號**：
   嚴格禁止將每一次在對話中的除錯微調都無限制累加版號（如飆到 `.167` 甚至 `.245`），內部除錯應在 PATCH 層級微調。
2. **純外觀與文字潤飾不更動版本號**：
   若僅更換圖標 (Icon)、修飾註解或單純的文檔勘誤，嚴禁推動版號。
3. **嚴禁跳過位元**：
   嚴禁從 `v2.11.0` 逕行跳至 `v2.12`，必須嚴格維持 3 段式結構。

---

## 3. 發布目錄與過渡產物自動清理鐵律 (Release Retention Policy)
1. **單一固定測試入口 (Permanent Test Entrance)**：
   * 每次打包必須同步鏡像維護：
     👉 `Dyanmometer/Release/Dynamometer_HMI_Latest_Portable/Dynamometer_HMI_Pro.exe`
   * 使用者在本地端驗證時，**永久以此目錄為首要入口**，徹底免除每次到不同版號子資料夾中搜尋的困擾。
2. **過渡建置用畢即清 (Zero Interim Artifact Bloat)**：
   * `Dyanmometer/Release/` 目錄嚴禁堆積過往除錯的廢棄資料夾與 `.zip`。
   * 打包腳本 `package_release.ps1` 必須內建自動輪替清理（Retention Policy）：
     - **白名單保護**：僅保留歷史重大里程碑（`V2.2.0`, `V2.3.0`, `V2.4.0`, `V2.5.0`）與當前目標版本（`V2.11.0`）。
     - **自動清理**：非白名單之過渡快照自動掃除，確保 `Release/` 永遠乾淨整潔（檔案數 $\le 15$，總體積 $\le 50$ MB）。
3. **歷史版本歸屬於 Git Tag**：
   * 所有歷史版本由 Git Commit 與 GitHub Release 負責，本地磁碟嚴禁作為備份倉庫無限制堆疊二進位檔案。

---

## 4. 三端原子同動鐵律 (Three-Point Release Alignment)
凡版號發生變更，必須在同一個 Turn 內完成四處一致性同動：
1. **C# 源碼**：`Dynamometer_WebServer.cs` 之 `public const string APP_VERSION = "2.X.Y";`
2. **變更紀錄**：`Dyanmometer/CHANGELOG.md` 最新記錄頂部之版本號與日期。
3. **打包腳本**：`Dyanmometer/package_release.ps1` 之 `param([string]$Version = "2.X.Y", ...)`。
4. **雲端版本指標**（若獲授權發布）：Firebase RTDB 之 `/update/version.json` 中的 `version` 欄位。
