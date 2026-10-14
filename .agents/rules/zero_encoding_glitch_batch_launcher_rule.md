# Windows 批次檔 (.bat) 與啟動器 100% 純 ASCII 與零亂碼鐵律 (Zero-Encoding-Glitch Batch & Launcher Rule)

## 1. 核心物理限制與背景 (Root Cause)
* **cmd.exe 預設編碼限制**：繁體中文 Windows 系統的 `cmd.exe`（命令提示字元）預設採用 **CP950 (Big5)** 編碼逐行讀取批次檔。
* **UTF-8 位元組錯位 (Byte Desynchronization)**：現代編輯器寫入之 UTF-8（無 BOM）中文字每個佔 3 位元組，被 `cmd.exe` 以 2 位元組解析時，檔案指針必然發生錯位。若遇到半形括號 `)`、`&` 或特殊位元，`cmd.exe` 會將中文字當成指令執行，或吞掉後續指令首字母（如 `echo` 變成 `cho`）。
* **PowerShell 5.1 歷史缺陷**：Windows 內建之 Windows PowerShell 5.1 在讀取無 BOM 的 `.ps1` 檔案時，同樣預設使用 ANSI (Big5) 讀取，且 `$MyInvocation.MyCommand.Path` 在特定調用環境下會為 `$null` 引發 `Set-Location` 崩潰。

---

## 2. 最高強制執行規範 (Strict Mandate)

### 鐵律一：.bat 批次檔 100% 絕對純 ASCII 英文 (Zero Non-ASCII)
* **嚴禁**在任何 `.bat` 或 `.cmd` 檔案中出現**任何中文字元**、全形標點符號或特殊 Unicode 字元。
* **嚴禁**使用 `::` 作為註解（`::` 在 cmd.exe 內部是無效標籤，帶括號或中文字時會被錯誤當成命令執行）。一律使用純英文 `REM` 或完全不寫註解。
* **標準 .bat 範本結構 (極簡 4 行)**：
  ```cmd
  @echo off
  title <Project Name> Launcher
  cd /d "%~dp0"
  python <entry_script>.py
  pause
  ```

### 鐵律二：主控台中文輸出全權交由 Python 原生 Unicode 處理
* 終端介面、彩色橫幅、操作提示等所有繁體中文輸出，**一律交由 Python 腳本負責**。
* 任何對外提供主控台互動的 Python 啟動檔（如 `server.py` 或 `launcher.py`），第一行必須設置：
  ```python
  import sys
  if sys.platform == 'win32':
      try:
          sys.stdout.reconfigure(encoding='utf-8')
          sys.stderr.reconfigure(encoding='utf-8')
      except Exception:
          pass
  ```
  Python 透過 Windows 原生 Unicode API (`WriteConsoleW`) 直接向主控台輸出，保證 0% 亂碼。

### 鐵律三：自動開啟瀏覽器交由 Python `webbrowser`
* 嚴禁在 `.bat` 中使用 `start http://...` 配合脆弱的 `timeout` 等待伺服器。
* 一律在 Python 伺服器啟動後，透過背景執行緒延遲 0.8 秒調用 `webbrowser.open(url)`，伺服器就緒後平滑自動彈出瀏覽器。
