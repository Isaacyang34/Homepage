# TOOL_Stock_Portfolio_Tracker 變更歷程與架構防護記錄 (CHANGELOG.md)

## [V1.2.1.0] - 2026-11-04

### 1. 【嚴重故障排除】熱更新重啟後記憶體版本未生效死循環 (Bytecode Stale Lockout Fix)
- **問題現象 (Symptom)**：
  - 使用者在點擊「立即輕量升級至 V1.2.1.0」並完成重啟後，軟體主畫面依然跳出「`發現新版 V1.2.1.0 (需重啟)`」，更新中心顯示：
    - `記憶體運行版本: V1.2.0.0 (需重啟更新介面)`
    - `磁碟已安裝版本: V1.2.1.0 (新版待生效)`
  - 重複重啟多次依然陷入死循環，新功能介面無法載入。
- **根本原因 (Root Cause)**：
  - 桌面版 `Stock_Portfolio_Tracker.exe` 執行時，依賴 `app_core.pkg` (Python 位元組核心封裝包) 載入業務邏輯。
  - 在發布流水線 (`package_release.py`) 執行 `step_3_compile_bytecode()` 時：
    1. `build_pkg.py` 設有無主控台語法安全守門員 (`test_windowed_stdio_safety.py`)，因 `tests/test_query_0050.py` 存在未防護之 `sys.stdout.reconfigure()` 而攔截報錯。
    2. 但 `build_pkg.py` 未宣告 `sys.exit(1)`，直譯器以 Exit Code 0 退出；且 `package_release.py` 在編譯前未清理磁碟上舊的 `app_core.pkg`。
    3. 流水線誤將舊版 `app_core.pkg` (內部 `updater.pyc` 為 `V1.2.0.0`) 壓入 `patch_V1.2.1.0.zip`。
    4. 使用者更新後，磁碟 `version.json` 為 `V1.2.1.0`，但啟動時載入的 `app_core.pkg` 核心模組永遠回傳 `V1.2.0.0`，造成死循環。
- **永久防禦與三重防護閘門 (Permanent 3-Layer Defense Gate)**：
  1. **【語法修復】**：修正 `tests/test_query_0050.py` 之 `sys.stdout` 雙保險防護，AST 靜態掃描 100% 綠燈。
  2. **【物理清舊與非零退出碼】**：
     - `build_pkg.py` 宣告 `sys.exit(0 if success else 1)`，失敗必定中斷流程。
     - `package_release.py` 在執行 `step_3` 前，強制物理刪除既有 `app_core.pkg`，杜絕任何幽靈舊檔混入。
  3. **【位元組版號強一致性閉鎖驗證 (Bytecode Manifest Consistency Lockout)】**：
     - 在 `package_release.py` 新增二進位位元組檢查門禁：直接解開 `app_core.pkg` 並反序列化 `updater.pyc`，透過 `marshal` 讀取 Python 位元組常數，強制斷言 `compiled_version == target_version`！
     - 若內部版號與目標版本不符，**立即拋出致命例外並熔斷停止打包**，物理保證絕無可能產出版號不一致的補丁包。

---

### 2. 【多設備雲端智慧雙向合併與衝突決策】
- **背景自動同步徹底停用**：
  - `is_auto_sync_enabled() -> False`，全面移除未經授權的背景定時上傳與靜默覆蓋。
- **會話異動追蹤與關閉前密碼核實**：
  - 僅在當次會話有增刪改持股時，於關閉前彈出專屬視窗核實雲端主密碼。
- **差異比對與四大決策入口 (`SyncConflictDialog`)**：
  - 精確表格化比對「雲端獨有」、「本機獨有」與「內容差異」。
  - 提供四大明確決策：
    1. 【智慧雙向合併 (兩端兼得)】：聯集合併兩台電腦的新增標的與買入批次重算均價，0 資料遺失。
    2. 【以本機為主 (覆蓋雲端)】：本機覆蓋雲端。
    3. 【以雲端為主 (覆蓋本機)】：雲端還原本機。
    4. 【取消】：不上傳。
- **開機零連線**：啟動 100% 純離線毫秒級秒開。
- **Rule 9 GDI+ 零 Emoji 達標**：全面清除對話框與按鈕中多位元組 Emoji 符號，防止 WinForms / Tkinter 裁切與字型崩潰。
