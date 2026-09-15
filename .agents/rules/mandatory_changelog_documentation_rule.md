# Mandatory Real-Time Changelog Documentation Rule (全案最高強制規範)

## 檔案存放唯一路徑與發布防污染原則
1. **單一真實來源 (Single Source of Truth)**：
   * 全案變更紀錄**唯一存放於專案源碼根目錄：`Dyanmometer/CHANGELOG.md`**。
   * 嚴禁分散在子目錄或任意建立複本。
2. **發布目錄防污染鐵律 (Clean Release Policy)**：
   * `Release/`（如 `Release/Dynamometer_HMI_V2.5.0_Portable/`）為便攜測試與使用者直接執行之專屬目錄。
   * **`Release/` 目錄下嚴禁放置 `CHANGELOG.md` 或任何非執行必備的開發文件**！
   * 僅允許存在原生可執行檔 `Dynamometer_HMI_Pro.exe` 以及原廠驅動庫 `DLL/`，保持發布目錄 100% 純淨。

## 核心鐵律
1. **Changelog-Atomic 鐵律 (同動更新)**：
   * 凡是有任何程式碼、功能、參數或錯誤修復之改動，**必須在同一個對話 Turn 內「原子化同步更新 `Dyanmometer/CHANGELOG.md`」**。
   * 嚴格禁止將記錄延遲到事後、下一次對話，或等待使用者開口提醒。

2. **未記 Log 視同未完成**：
   * 若未在 `Dyanmometer/CHANGELOG.md` 中詳細記錄修改內容、實測日誌依據、根本原因 (Root Cause) 與修復細節，該次修改在流程上視為「未完成 (Incomplete)」。
   * **未完成狀態下，嚴禁執行打包發布或向使用者回報結論！**

3. **記錄三要素 (強制要求)**：
   每一次的 CHANGELOG 更新，必須遵循標準 Markdown 結構，清楚陳述：
   * **現象與佐證**：使用者回報的問題現象，以及從 `Auto_Raw_Telemetry_*.csv` 或 `.log` 提取的關鍵佐證行（Verbatim Excerpts）。
   * **致命根因 (Root Cause)**：明確指出是在哪個檔案、哪個函式，因為何種邏輯錯誤、算式錯誤、常數縮放錯誤或硬體暫存器地址筆誤所導致。
   * **精確修復方案**：具體修改了哪些程式碼、調整了何種機制，並標註對應的內部版本號與發布路徑。
