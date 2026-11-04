# 全專案二進位發布實體版號一致性與建置熔斷最高鐵律 (Universal Binary Integrity, Anti-Stale Artifact & Build Gate Rule)

## 1. 核心致命盲區：幽靈產物與版本脫鉤 (The Ghost Artifact Dilemma)
* **現象與危害**：
  * 在任何編譯或封裝型專案（包含 C# WinForms、Python PyInstaller、二進位位元組核心包 `app_core.pkg`、C++ 或韌體打包）：
  * 若建置工具在編譯中途發生錯誤，但未正確回傳非零退出碼（Exit Code 0），或發布腳本未嚴格檢查編譯狀態；
  * 同時若編譯目錄殘留著**上一版的歷史二進位檔案**；
  * 發布腳本將誤把「上一版的舊產物」當作新版打進補丁包或便攜包！
  * **結果**：外部宣告版號（`version.json`）已更新為新版，但執行檔內部常數永遠運行於舊版，導致使用者即使重啟一百次，主程式依然陷入「記憶體運行舊版，磁碟待生效」的死循環中！

---

## 2. 全案通用四重物理硬閉鎖防線 (Mandatory 4-Layer Build & Release Gate)
**適用範圍：本工作區所有現有與未來新增之專案（包含 `TOOL_*`、`Dyanmometer`、`ESP32_*`、`ios2pixel` 等無例外）！**

### 防線 1：前置物理清空鐵律 (Pre-Build Purge Gate)
* 任何發布流水線（PowerShell `package_release.ps1`、Python `package_release.py`、批次檔 `release.bat`）：
* 在發起編譯指令（`csc.exe`、`compileall`、`pyinstaller`、`build_pkg.py`）的**第一行代碼**：
  **必須強制物理刪除當前目錄下所有即將生成的二進位目標產物（`.exe`、`.pkg`、`.zip`、`.bin`）**！
* 嚴禁在未清理舊二進位檔案的情況下啟動編譯，從物理根源徹底杜絕「編譯失敗卻打包到上一版殘留舊檔」的可能性！

### 防線 2：非零退出碼嚴格熔斷鐵律 (Non-Zero Exit Code Circuit Breaker)
* 所有負責語法檢驗、單元測試、排版防裁切、位元碼編譯之子腳本：
  * 只要檢驗未全數通過或編譯異常，**必須強制拋出非零退出碼（`sys.exit(1)` 或 PowerShell `exit 1` / `throw`）**，嚴禁以 Exit Code 0 靜默退出！
* 發布主腳本必須嚴格攔截子程序的 `returncode` / `$LASTEXITCODE`：
  * **只要 `!= 0`，發布管線立即熔斷中斷**，嚴禁掠過報錯盲目繼續後續壓包或推送流程！

### 防線 3：實體二進位內部版號強一致性反序列化驗證 (Post-Build Binary Reflection Gate)
* 在將二進位檔壓製成壓縮包（`.zip`）或複製至 Release 之前，**發布腳本必須主動「穿透/逆向/反射/反序列化」實體二進位檔案內部的真實版號**：
  1. **C# / Win32 PE 執行檔**：
     - 調用 `[System.Diagnostics.FileVersionInfo]::GetVersionInfo($exePath).FileVersion` 或 `ProductVersion`。
  2. **Python 二進位核心包 (`app_core.pkg` / `.pyc`)**：
     - 調用 `zipfile` 讀取核心模組（如 `updater.pyc`），使用 `marshal.loads()` 反序列化讀取位元碼二進位常數 `APP_VERSION`。
  3. **韌體 / 嵌入式二進位檔**：
     - 讀取 Image Header 中的版本欄位。
* **物理斷言閉鎖**：
  - 實體二進位內部反向解析之版本，**必須與外部目標發布版號 (`target_version` / `version.json`) 100% 絕對完全一致**！
  - 若版本不符或無法解析，發布腳本**直接拋出致命錯誤並熔斷中斷**，視為「幽靈版本 (Ghost Artifact)」拒絕發布！

### 防線 4：發布端 200 OK 實測探測閉鎖 (Post-Release Probe Lockout)
* 嚴格遵循 Rule 7，發布完成後對遠端 Raw 下載網址發起 HTTP HEAD 實測探測，驗證 `HTTP 200 OK` 且大小符合。

---

## 3. 標準實作範本 (Reference Implementations)

### Python 發布腳本實作 (`package_release.py`)：
```python
# 1. 前置清空
pkg_path = os.path.join(APP_DIR, "app_core.pkg")
if os.path.exists(pkg_path):
    os.remove(pkg_path)

# 2. 執行建置並檢查退出碼
res = subprocess.run([sys.executable, "build_pkg.py"], capture_output=True, text=True)
if res.returncode != 0 or not os.path.exists(pkg_path):
    raise RuntimeError(f"二進位核心編譯失敗！Exit code: {res.returncode}")

# 3. 反序列化驗證位元組內部版號
with zipfile.ZipFile(pkg_path, "r") as zf:
    code_obj = marshal.loads(zf.read("updater.pyc")[16:])
    assert target_version in code_obj.co_consts, f"版號脫鉤！目標 {target_version} 未寫入二進位常數！"
```

### PowerShell 發布腳本實作 (`package_release.ps1`)：
```powershell
# 1. 前置清空
$targetExe = "$releaseDir\Dynamometer_HMI_Pro.exe"
if (Test-Path $targetExe) { Remove-Item $targetExe -Force }

# 2. 執行編譯
& $cscPath /out:$targetExe ...
if ($LASTEXITCODE -ne 0 -or -not (Test-Path $targetExe)) {
    throw "編譯失敗！Exit code: $LASTEXITCODE"
}

# 3. 反射驗證實體 PE 內部版號
$realVersion = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($targetExe).FileVersion
if ($realVersion -ne $targetVersion) {
    throw "實體二進位版號 ($realVersion) 與發布版號 ($targetVersion) 不一致！熔斷發布！"
}
```
