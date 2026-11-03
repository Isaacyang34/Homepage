# Python GUI 無主控台 (Windowed / `--noconsole`) 執行期空串流防護與編譯閉鎖規範 (Windowed Null-Stdio Safety & Build Gate Rule)

## 1. 核心物理限制與致命盲區 (The Windowed Stdio Void)
* **無主控台空串流現象 (Null-Stdio In Windowed Apps)**：
  * 在 Windows 作業系統下，當 Python 應用以 GUI 模式執行（例如使用 `pythonw.exe` 執行，或由 PyInstaller 以 `--noconsole` / `--windowed` 參數封裝成 `.exe` 時），作業系統**物理上不附加任何主控台視窗 (No Console Window Attached)**。
  * 此時 Python 執行期核心會將標準輸入/輸出/錯誤串流物件直接賦值為 `None`：
    `sys.stdout = None`、`sys.stderr = None`、`sys.stdin = None`。
* **致命崩潰陷阱 (The False-Sense-Of-Security Crash)**：
  * **常規單元測試無法捕捉**：一般開發者或 CI 測試皆在具有終端（Console Python）的環境下執行 `python -m unittest`，此時 `sys.stdout` 是合法串流，任何對其存取的語法皆正常運行。
  * **雙擊即暴斃**：若原始碼中出現任何未經防護的裸存取（如直接調用 `sys.stdout.reconfigure(encoding="utf-8")`、`sys.stdout.write(...)` 或 `sys.stdout.flush()`），當使用者雙擊 `.exe` 啟動的**第一毫秒**，Python 直譯器在匯入模組時便會立即拋出致命未捕捉例外：
    ```text
    AttributeError: 'NoneType' object has no attribute 'reconfigure'
    ```
    造成主視窗完全無法呈現、啟動畫面閃退、或跳出 unhandled exception 錯誤彈窗！

---

## 2. 三重自動化常態測試規範 (Mandatory 3-Layer Test Suite)
所有 Python GUI 桌面應用專案（包含 `TOOL_Stock_Portfolio_Tracker` 等）必須在 `tests/` 目錄下強制建立並常態維護 `test_windowed_stdio_safety.py`，且包含以下三重測試：

### 防線 1：AST 抽象語法樹靜態掃描 (`test_01_ast_static_scan_no_bare_stdio_calls`)
* 利用 Python 原生 `ast` 模組遞迴解析專案內所有 `.py` 檔案。
* 檢索所有存取 `sys.stdout`、`sys.stderr`、`sys.stdin` 之方法調用（特別是 `reconfigure`、`write`、`flush`、`writelines`）。
* **嚴格斷言**：該調用必須身處 `if sys.stdout is not None:` 或 `try...except:` 條件塊之內。
* **零容忍回報**：若發現任何一行未受防護之裸調用，測試立即 FAIL，並輸出精確違規檔案與行號。
* **檢查器自驗證**：測試套件本身必須包含 `test_00_ast_visitor_self_verification`，以虛擬惡意程式碼驗證檢查器的 100% 靈敏度。

### 防線 2：Null-Stdio 執行期動態匯入模擬 (`test_02_runtime_null_stdio_simulation`)
* 在單元測試進程中，主動將 `sys.stdout = None`、`sys.stderr = None`、`sys.stdin = None`。
* 強制遍歷專案所有核心業務模組、GUI 模組與啟動模組執行 `importlib.reload()` 或 `import_module()`。
* 斷言在標準串流為 `NoneType` 條件下，**0 個模組拋出 `AttributeError`**。

### 防線 3：DEVNULL 獨立行程隔離啟動 (`test_03_subprocess_isolated_null_stdio_startup`)
* 以獨立作業系統進程，將 `stdin`、`stdout`、`stderr` 全數重新導向至 `subprocess.DEVNULL`，並宣告 `sys.stdout = None` 執行啟動匯入。
* 斷言子程序 Exit Code 必須為 0。

---

## 3. 編譯封裝硬閉鎖門禁鐵律 (Build Pipeline Gate Lockout - 物理防禦)
* **未通過測試嚴禁打包**：
  * 凡執行 `build_pkg.py`、`package_release.py`、PyInstaller 打包或產生任何二進位封裝檔（如 `app_core.pkg`、`.exe`），**第 [0] 步必須強制呼叫 `test_windowed_stdio_safety.py` 進行門禁檢驗**！
* **物理中斷退出**：
  * 若安全檢測發現任何裸調用，打包腳本必須立即印出致命告警並直接退出（Exit Code != 0），**嚴格拒絕生成二進位封裝包與發布檔**！
  * 杜絕任何人為疏忽或 AI 疏漏將帶有白屏隱患的執行檔發布給使用者。

---

## 4. 程式碼標準雙保險寫法規範 (Coding Standards)
專案內所有 Python 檔案涉及主控台編碼或輸出配置時，嚴禁直接裸調用，必須一律採用雙保險條件防護：

```python
# 正確寫法：雙保險檢查 (兼顧 NoneType 與 hasattr)
if sys.stdout is not None and hasattr(sys.stdout, "reconfigure"):
    sys.stdout.reconfigure(encoding="utf-8")
if sys.stderr is not None and hasattr(sys.stderr, "reconfigure"):
    sys.stderr.reconfigure(encoding="utf-8")

# 嚴格禁止的錯誤裸調用 (在 --noconsole 下直接崩潰):
# sys.stdout.reconfigure(encoding="utf-8")  <-- 致命！
# hasattr(sys.stdout, "reconfigure")        <-- 若未判斷 NoneType 亦具隱患
```
