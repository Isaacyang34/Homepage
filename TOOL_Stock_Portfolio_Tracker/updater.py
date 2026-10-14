# -*- coding: utf-8 -*-
"""
Stock Portfolio Tracker - 模組化升級管理與多版本容錯系統 (V1.1.0.0)
支援：
1. 輕量秒級補丁更新 (Patch Update - 僅下載修訂模組包，約 100KB)
2. 完整安裝包下載與修復 (Full Package Download)
3. 雲端保留最後三版次自由切換 (Recent 3 Versions Selection)
4. 本地端版本迭代歷史自動備份 (Local Version Snapshot in versions/)
5. 一鍵還原本機歷史版本 (Rollback Mechanism)
6. 延遲更新 (待主程式關閉時自動置換) 與 即時無縫套用
"""
import os
import sys
import json
import time
import shutil
import zipfile
import threading
import subprocess
import requests
import tkinter as tk
from tkinter import ttk, messagebox
from typing import Optional, Dict, Any, Tuple, List

APP_VERSION = "V1.1.2.3"
APP_BOOT_VERSION = APP_VERSION  # 記憶體進程啟動時鎖定之版本

def get_boot_version() -> str:
    """取得當前進程啟動時之記憶體版本號"""
    global APP_BOOT_VERSION
    return APP_BOOT_VERSION

def get_disk_version() -> str:
    """直接讀取本機磁碟上的 version.json，取得實體檔案之真實版次"""
    try:
        app_dir = get_app_dir()
        v_path = os.path.join(app_dir, "version.json")
        if os.path.exists(v_path):
            with open(v_path, "r", encoding="utf-8") as f:
                data = json.load(f)
                v = data.get("version")
                if v:
                    return str(v).strip()
    except Exception:
        pass
    return get_app_version()

def get_app_version() -> str:
    """動態取得當前應用程式版本號，確保熱更新後所有模組取值一致"""
    global APP_VERSION
    return APP_VERSION

def set_app_version(new_version: str):
    """動態更新全域版本號，並同步更新各模組命名空間"""
    global APP_VERSION
    APP_VERSION = new_version
    # 同步更新已載入之 main_gui 與 sys.modules 中的版本變數
    for mod_name in ("main_gui", "modules.main_gui", "updater", "modules.updater"):
        if mod_name in sys.modules:
            try:
                setattr(sys.modules[mod_name], "APP_VERSION", new_version)
            except Exception:
                pass

# 延遲更新狀態管理 (使用者可選擇「稍後於關閉程式時自動置換」)
_PENDING_UPDATE: Dict[str, Any] = {
    "ready": False,
    "target_dir": "",
    "temp_file": "",
    "version": "",
    "is_patch": True
}

def set_pending_update(target_dir: str, temp_file: str, version: str, is_patch: bool = True):
    _PENDING_UPDATE["ready"] = True
    _PENDING_UPDATE["target_dir"] = target_dir
    _PENDING_UPDATE["temp_file"] = temp_file
    _PENDING_UPDATE["version"] = version
    _PENDING_UPDATE["is_patch"] = is_patch
    log_update_debug(f"[PENDING] Update deferred. version={version}, temp_file={temp_file}, is_patch={is_patch}")

def is_pending_update() -> bool:
    ready = _PENDING_UPDATE.get("ready", False)
    temp_file = _PENDING_UPDATE.get("temp_file", "")
    return ready and os.path.exists(temp_file)

def get_pending_version() -> str:
    return _PENDING_UPDATE.get("version", "")

def get_app_dir() -> str:
    """取得應用程式根目錄 (相容 PyInstaller 凍結執行與原始碼執行)"""
    if getattr(sys, 'frozen', False):
        return os.path.dirname(os.path.abspath(sys.executable))
    p = os.path.dirname(os.path.abspath(__file__))
    if p.lower().endswith(".pkg") or p.lower().endswith(".zip") or os.path.basename(p) == "modules":
        p = os.path.dirname(p)
    return p

def log_update_debug(msg: str):
    """將更新與啟動日誌寫入 update_debug.log，供問題診斷與分析"""
    try:
        log_path = os.path.join(get_app_dir(), "update_debug.log")
        ts = time.strftime("%Y-%m-%d %H:%M:%S")
        with open(log_path, "a", encoding="utf-8") as f:
            f.write(f"[{ts}] {msg}\n")
    except Exception:
        pass

# 模組載入時立即記錄 (全域第一時間抓取進程啟動特徵)
try:
    _mei_init = getattr(sys, '_MEIPASS', 'None')
    log_update_debug(f"[INIT] updater module loaded! PID={os.getpid()}, APP_VERSION={APP_VERSION}, _MEIPASS={_mei_init}")
except Exception:
    pass

def create_local_version_snapshot(app_dir: str, version_tag: str) -> bool:
    """
    在 versions/{version_tag}/ 建立本地端版本迭代快照
    完整保存 modules/ 與 version.json，確保隨時可進行本機還原
    """
    try:
        versions_dir = os.path.join(app_dir, "versions")
        os.makedirs(versions_dir, exist_ok=True)
        target_snapshot = os.path.join(versions_dir, version_tag)
        
        # 若已存在先清空舊目錄
        if os.path.exists(target_snapshot):
            shutil.rmtree(target_snapshot, ignore_errors=True)
        os.makedirs(target_snapshot, exist_ok=True)

        # 備份 app_core.pkg
        pkg_src = os.path.join(app_dir, "app_core.pkg")
        if os.path.exists(pkg_src):
            shutil.copy2(pkg_src, target_snapshot)

        # 備份 modules 資料夾
        modules_src = os.path.join(app_dir, "modules")
        if os.path.exists(modules_src):
            shutil.copytree(modules_src, os.path.join(target_snapshot, "modules"))
        
        # 備份 version.json
        ver_file = os.path.join(app_dir, "version.json")
        if os.path.exists(ver_file):
            shutil.copy2(ver_file, target_snapshot)

        log_update_debug(f"[SNAPSHOT] Created local version snapshot for {version_tag} at {target_snapshot}")
        return True
    except Exception as e:
        log_update_debug(f"[SNAPSHOT] Error creating snapshot: {e}")
        return False

def safe_extract_zip(zf: zipfile.ZipFile, target_dir: str, skip_running_exe: bool = True):
    """
    安全解壓縮 ZIP 檔案：
    若 zip 內包含當前正在執行的 EXE 檔案，主動跳過，
    徹底杜絕 [Errno 13] Permission denied 檔案鎖定衝突！
    """
    running_exe_name = os.path.basename(sys.executable).lower()
    for member in zf.infolist():
        # 防範路徑穿越
        filename = member.filename
        if os.path.isabs(filename) or ".." in filename:
            continue
        base_name = os.path.basename(filename).lower()
        if skip_running_exe and base_name == running_exe_name:
            log_update_debug(f"[SAFE_EXTRACT] Skipping running exe to prevent lock: {filename}")
            continue
        zf.extract(member, target_dir)

def apply_patch_hot_reload(target_dir: str, temp_zip: str, parent_app=None, target_version: str = "") -> bool:
    """
    真・記憶體熱更新 (In-Process Hot Reload):
    1. 解壓縮覆蓋 app_core.pkg、config/、plugins/
    2. 清除 zipimport 與 sys.modules 快取
    3. 重新加載核心模組並刷新現有 UI 畫面
    4. 0 重啟、0 閃退、0 程式中斷！
    """
    global APP_VERSION
    log_update_debug(f"[HOT_RELOAD] apply_patch_hot_reload started for {temp_zip}")
    # 1. 建立快照備份
    create_local_version_snapshot(target_dir, APP_VERSION)
    
    # 2. 解壓縮覆蓋 (跳過正處於鎖定狀態之主執行檔，防範 Permission denied)
    try:
        with zipfile.ZipFile(temp_zip, 'r') as zf:
            safe_extract_zip(zf, target_dir, skip_running_exe=True)
        log_update_debug(f"[HOT_RELOAD] Extracted patch zip successfully.")
    except Exception as e:
        log_update_debug(f"[HOT_RELOAD] ERROR extracting zip: {e}")
        messagebox.showerror("更新失敗", f"解壓縮補丁包失敗: {e}")
        return False
        
    # 3. 刪除暫存 zip
    try:
        os.remove(temp_zip)
    except Exception:
        pass
        
    # 4. 清除 zipimport 快取，強制 Python 重新讀取最新的 app_core.pkg
    try:
        import zipimport
        if hasattr(zipimport, "_zip_directory_cache"):
            zipimport._zip_directory_cache.clear()
    except Exception as e:
        log_update_debug(f"[HOT_RELOAD] zipimport cache clear notice: {e}")

    # 5. 清除核心業務模組的記憶體快取
    core_modules_to_reload = [
        "quote_service", "database", "pnl_calculator", 
        "dividend_service", "history_service", "ranking_service",
        "market_ranking_gui", "dividend_analyzer_gui", "kline_chart", "plugin_manager",
        "stock_detector", "crypto_sync"
    ]
    for mod_name in core_modules_to_reload:
        if mod_name in sys.modules:
            del sys.modules[mod_name]

    # 6. 更新全域版本號
    if target_version:
        set_app_version(target_version)

    # 7. 記憶體熱重載與介面即時刷新
    if parent_app:
        try:
            # 重新加載外掛
            try:
                from plugin_manager import load_plugins
                load_plugins()
            except Exception:
                pass
                
            # 重新初始化 quote_service
            from quote_service import QuoteService
            parent_app.quote_service = QuoteService()
            
            # 更新視窗標題列
            parent_app.title(f"本地端台美股庫存即時損益、歷史與股息追蹤系統 (Stock Portfolio Tracker) {get_app_version()}")
            
            # 更新狀態列文字
            if hasattr(parent_app, 'status_lbl') and parent_app.status_lbl:
                parent_app.status_lbl.configure(text=f"✔ 已無縫熱更新至 {get_app_version()}！模組已即時生效")
                
            # 重置更新按鈕外觀回一般狀態 (清除「發現新版」高亮警示)
            if hasattr(parent_app, 'reset_update_button_state'):
                parent_app.reset_update_button_state()
            elif hasattr(parent_app, 'btn_update') and parent_app.btn_update:
                parent_app.btn_update.configure(
                    text="[⬆ 軟體更新]", bg="#323242", fg="#ffffff",
                    activebackground="#3a86ff", activeforeground="#ffffff"
                )

            # 立即觸發數據重算與表格重繪
            parent_app.trigger_refresh()
            parent_app.refresh_ui_table()
            log_update_debug(f"[HOT_RELOAD] Hot reload completed successfully without restarting!")
        except Exception as e:
            log_update_debug(f"[HOT_RELOAD] Error during UI hot reload: {e}")

    # 8. 友好完成提示 (提示介面變更建議重啟以重塑 Widget 樹)
    parent_target = parent_app if (parent_app and parent_app.winfo_exists()) else None
    choice = messagebox.askyesno(
        "熱更新已就緒 - 建議重啟以套用新介面",
        f"✔ 版本【{target_version or APP_VERSION}】後端運算模組已在記憶體中即時加載！\n\n"
        f"提示：若本次升級包含新按鈕、主視窗版面或登入系統等 UI 變更，\n"
        f"建議重新啟動軟體以載入全新介面。\n\n"
        f"是否立即重新啟動軟體？\n\n"
        f"• 按【是 (Yes)】：立即安全重新啟動軟體（建議）\n"
        f"• 按【否 (No)】：稍後手動重啟（維持目前畫面繼續運行）",
        parent=parent_target
    )
    if choice:
        restart_application(target_dir)
        if parent_app:
            parent_app.destroy()
        sys.exit(0)
    return True

def restart_application(target_dir: Optional[str] = None):
    """
    安全平滑重啟當前應用程式 (相容原始碼 python 執行與 PyInstaller 凍結執行)
    徹底廢除脆弱的 apply_restart.bat 與 taskkill /F，改採 PowerShell 獨立守護進程 + Python 優雅退出 (Graceful Exit)
    徹底消滅 PyInstaller _MEIPASS2 繼承與 LoadLibrary 126 找不到模組錯誤！
    """
    if target_dir is None:
        target_dir = get_app_dir()
    current_pid = os.getpid()
    log_update_debug(f"[RESTART] restart_application requested cleanly. PID={current_pid}")

    if getattr(sys, 'frozen', False):
        exe_path = os.path.abspath(sys.executable)
        exe_name = os.path.basename(exe_path)
        # 委託 Windows 桌面 Shell (explorer.exe) 啟動，物理切斷進程繼承鏈，等同使用者在桌面雙擊滑鼠
        ps_launch_code = f"Start-Process 'explorer.exe' -ArgumentList '\"{exe_path}\"'; "
    else:
        py_exe = os.path.abspath(sys.executable)
        exe_name = "python.exe"
        script_file = os.path.join(target_dir, "main_gui.py")
        if not os.path.exists(script_file):
            script_file = os.path.abspath(sys.argv[0])
        ps_launch_code = (
            f"$psi = New-Object System.Diagnostics.ProcessStartInfo; "
            f"$psi.FileName = '{py_exe}'; "
            f"$psi.Arguments = '\"{script_file}\"'; "
            f"$psi.WorkingDirectory = '{target_dir}'; "
            f"$psi.UseShellExecute = $false; "
            f"$psi.EnvironmentVariables.Remove('_MEIPASS'); "
            f"$psi.EnvironmentVariables.Remove('_MEIPASS2'); "
            f"$psi.EnvironmentVariables.Remove('PYTHONHOME'); "
            f"$psi.EnvironmentVariables.Remove('PYTHONPATH'); "
            f"[System.Diagnostics.Process]::Start($psi); "
        )

    log_file = os.path.join(target_dir, "update_debug.log")
    
    ps_cmd = (
        f"$log = '{log_file}'; "
        f"Add-Content -Path $log -Value ('[' + (Get-Date).ToString('yyyy-MM-dd HH:mm:ss') + '] [RESTART_PS] Waiting for PID {current_pid} and parent bootloader to cleanly exit...'); "
        f"Wait-Process -Id {current_pid} -Timeout 5 -ErrorAction SilentlyContinue; "
        f"Start-Sleep -Seconds 2; "
        f"Add-Content -Path $log -Value ('[' + (Get-Date).ToString('yyyy-MM-dd HH:mm:ss') + '] [RESTART_PS] Launching {exe_name} via Windows Shell (explorer.exe) with 0% process chain pollution...'); "
        f"{ps_launch_code}; "
        f"Add-Content -Path $log -Value ('[' + (Get-Date).ToString('yyyy-MM-dd HH:mm:ss') + '] [RESTART_PS] Spawn command finished successfully.'); "
    )

    clean_env = os.environ.copy()
    clean_env.pop('_MEIPASS', None)
    clean_env.pop('_MEIPASS2', None)
    clean_env.pop('PYTHONHOME', None)
    clean_env.pop('PYTHONPATH', None)

    try:
        creation_flags = subprocess.CREATE_NO_WINDOW if os.name == 'nt' else 0
        subprocess.Popen(
            ["powershell.exe", "-NoProfile", "-WindowStyle", "Hidden", "-Command", ps_cmd],
            cwd=target_dir,
            env=clean_env,
            creationflags=creation_flags
        )
        log_update_debug(f"[RESTART] Spawned clean PowerShell launcher for PID={current_pid}")
    except Exception as e:
        log_update_debug(f"[RESTART] Failed to spawn PowerShell launcher: {e}")

    # 優雅退出自身進程，讓 PyInstaller Bootloader 正常釋放 handles 並刪除舊臨時目錄
    try:
        import tkinter
        if tkinter._default_root:
            try:
                tkinter._default_root.destroy()
            except Exception:
                pass
    except Exception:
        pass

    log_update_debug(f"[RESTART] Cleanly exiting process PID={current_pid} via sys.exit(0)")
    sys.exit(0)

def apply_patch_now(target_dir: str, temp_zip: str, restart: bool = True):
    """
    執行輕量補丁包 (Patch ZIP) 置換作業
    僅覆蓋 modules/ 與 version.json，無需置換主 EXE，0% 檔案鎖定
    """
    current_pid = os.getpid()
    log_update_debug(f"[PATCH] apply_patch_now triggered. PID={current_pid}, restart={restart}")

    # 1. 自動備份當前版本至本機 versions/
    create_local_version_snapshot(target_dir, APP_VERSION)

    # 2. 解壓縮覆蓋 modules 與 version.json (安全略過被鎖定之主執行檔)
    try:
        with zipfile.ZipFile(temp_zip, 'r') as zf:
            safe_extract_zip(zf, target_dir, skip_running_exe=True)
        log_update_debug(f"[PATCH] Extracted {temp_zip} into {target_dir} successfully.")
    except Exception as e:
        log_update_debug(f"[PATCH] ERROR extracting zip: {e}")
        return

    # 3. 刪除暫存補丁檔案
    try:
        os.remove(temp_zip)
    except Exception:
        pass

    # 4. 若需重啟，透過平滑重啟函式安全喚醒新程序
    if restart:
        restart_application(target_dir)

def apply_full_package_now(target_dir: str, temp_file: str, restart: bool = True) -> bool:
    """
    執行完整安裝包 (Full Package ZIP 或獨立 EXE) 的安全外部置換
    徹底杜絕 [Errno 13] Permission denied 檔案鎖定衝突！
    """
    current_pid = os.getpid()
    log_update_debug(f"[FULL_PACKAGE] apply_full_package_now started. temp_file={temp_file}, PID={current_pid}")

    create_local_version_snapshot(target_dir, APP_VERSION)

    bat_path = os.path.join(target_dir, "apply_update.bat")
    target_exe_name = "Stock_Portfolio_Tracker.exe" if os.path.exists(os.path.join(target_dir, "Stock_Portfolio_Tracker.exe")) else sys.argv[0]
    temp_name = os.path.basename(temp_file)

    if temp_file.lower().endswith(".zip"):
        extract_tmp_dir = os.path.join(target_dir, "temp_update_extracted")
        if os.path.exists(extract_tmp_dir):
            shutil.rmtree(extract_tmp_dir, ignore_errors=True)
        os.makedirs(extract_tmp_dir, exist_ok=True)

        try:
            with zipfile.ZipFile(temp_file, 'r') as zf:
                zf.extractall(extract_tmp_dir)
            log_update_debug(f"[FULL_PACKAGE] Extracted full zip into temp_update_extracted successfully.")
        except Exception as e:
            log_update_debug(f"[FULL_PACKAGE] Failed to pre-extract full zip: {e}")
            messagebox.showerror("更新失敗", f"解壓縮安裝包失敗: {e}")
            return False

        restart_action = f'start "" "{target_exe_name}"' if restart else ""

        bat_content = f"""@echo off
REM Stock Portfolio Tracker Full Package Overwrite Script
set LOG_FILE=%~dp0update_debug.log
echo [%DATE% %TIME%] [BATCH] Waiting for PID {current_pid} to exit... >> "%LOG_FILE%"
taskkill /PID {current_pid} /F >> "%LOG_FILE%" 2>&1
echo [%DATE% %TIME%] [BATCH] taskkill return code: %ERRORLEVEL% >> "%LOG_FILE%"
ping 127.0.0.1 -n 3 > nul
cd /d "%~dp0"
set _MEIPASS=
set _MEIPASS2=
set PYTHONHOME=
set PYTHONPATH=

echo [%DATE% %TIME%] [BATCH] Overwriting files from temp_update_extracted... >> "%LOG_FILE%"
set RETRY=0
:RETRY_MOVE
set /a RETRY+=1
robocopy "%~dp0temp_update_extracted" "%~dp0" /E /MOVE /IS /IT > nul 2>&1
if errorlevel 8 (
    ping 127.0.0.1 -n 2 > nul
    if %RETRY% leq 5 goto RETRY_MOVE
    echo [%DATE% %TIME%] [BATCH] Fallback to xcopy... >> "%LOG_FILE%"
    xcopy /y /e /q "%~dp0temp_update_extracted\\*" "%~dp0" > nul 2>&1
)

if exist "%~dp0temp_update_extracted" rmdir /s /q "%~dp0temp_update_extracted" > nul 2>&1
if exist "%~dp0{temp_name}" del /f /q "%~dp0{temp_name}" > nul 2>&1

echo [%DATE% %TIME%] [BATCH] Full update completed. Relaunching: {target_exe_name} >> "%LOG_FILE%"
{restart_action}
echo [%DATE% %TIME%] [BATCH] Process spawned with return code: %ERRORLEVEL% >> "%LOG_FILE%"
ping 127.0.0.1 -n 3 > nul
echo [%DATE% %TIME%] [BATCH] Probing running processes: >> "%LOG_FILE%"
tasklist /FI "IMAGENAME eq {target_exe_name}" /NH >> "%LOG_FILE%" 2>&1
exit
"""
    else:
        restart_action = f'start "" "{target_exe_name}"' if restart else ""
        bat_content = f"""@echo off
REM Stock Portfolio Tracker Full EXE Overwrite Script
set LOG_FILE=%~dp0update_debug.log
echo [%DATE% %TIME%] [BATCH] Waiting for PID {current_pid} to exit... >> "%LOG_FILE%"
taskkill /PID {current_pid} /F >> "%LOG_FILE%" 2>&1
echo [%DATE% %TIME%] [BATCH] taskkill return code: %ERRORLEVEL% >> "%LOG_FILE%"
ping 127.0.0.1 -n 3 > nul
cd /d "%~dp0"
set _MEIPASS=
set _MEIPASS2=
set PYTHONHOME=
set PYTHONPATH=

set RETRY=0
:RETRY_EXE
set /a RETRY+=1
move /y "{temp_name}" "{target_exe_name}" >> "%LOG_FILE%" 2>&1
if errorlevel 1 (
    ping 127.0.0.1 -n 2 > nul
    if %RETRY% leq 5 goto RETRY_EXE
    copy /y "{temp_name}" "{target_exe_name}" >> "%LOG_FILE%" 2>&1
    del /f /q "{temp_name}" > nul 2>&1
)

echo [%DATE% %TIME%] [BATCH] Full update completed. Relaunching: {target_exe_name} >> "%LOG_FILE%"
{restart_action}
echo [%DATE% %TIME%] [BATCH] Process spawned with return code: %ERRORLEVEL% >> "%LOG_FILE%"
ping 127.0.0.1 -n 3 > nul
echo [%DATE% %TIME%] [BATCH] Probing running processes: >> "%LOG_FILE%"
tasklist /FI "IMAGENAME eq {target_exe_name}" /NH >> "%LOG_FILE%" 2>&1
exit
"""
    try:
        with open(bat_path, "w", encoding="ascii") as f:
            f.write(bat_content)
        creation_flags = subprocess.CREATE_NO_WINDOW if os.name == 'nt' else 0
        clean_env = os.environ.copy()
        clean_env.pop('_MEIPASS2', None)
        clean_env.pop('_MEIPASS', None)
        subprocess.Popen(["cmd.exe", "/c", bat_path], cwd=target_dir, env=clean_env, creationflags=creation_flags)
        log_update_debug(f"[FULL_PACKAGE] Spawned apply_update.bat successfully.")
        return True
    except Exception as e:
        log_update_debug(f"[FULL_PACKAGE] Failed to spawn update bat: {e}")
        return False

def apply_full_exe_now(target_dir: str, temp_exe: str, restart: bool = True):
    """相容舊版介面轉接至 apply_full_package_now"""
    return apply_full_package_now(target_dir, temp_exe, restart=restart)

def apply_update_now(target_dir: str, temp_file: str, restart: bool = True, is_patch: bool = True):
    """通用套用更新分流：依性質決定採補丁置換或完整安裝包置換"""
    if is_patch:
        apply_patch_now(target_dir, temp_file, restart=restart)
    else:
        apply_full_package_now(target_dir, temp_file, restart=restart)

def rollback_to_version(app_dir: str, target_ver: str) -> bool:
    """
    一鍵還原至本機指定歷史版本 (從 versions/{target_ver}/ 還原)
    """
    try:
        backup_dir = os.path.join(app_dir, "versions", target_ver)
        if not os.path.exists(backup_dir):
            return False
            
        # 1. 還原 app_core.pkg
        backup_pkg = os.path.join(backup_dir, "app_core.pkg")
        dst_pkg = os.path.join(app_dir, "app_core.pkg")
        if os.path.exists(backup_pkg):
            shutil.copy2(backup_pkg, dst_pkg)

        # 2. 還原 modules (若存在)
        backup_modules = os.path.join(backup_dir, "modules")
        dst_modules = os.path.join(app_dir, "modules")
        if os.path.exists(backup_modules):
            shutil.rmtree(dst_modules, ignore_errors=True)
            shutil.copytree(backup_modules, dst_modules)
            
        # 3. 還原 version.json
        backup_ver_file = os.path.join(backup_dir, "version.json")
        dst_ver_file = os.path.join(app_dir, "version.json")
        if os.path.exists(backup_ver_file):
            shutil.copy2(backup_ver_file, dst_ver_file)
            
        log_update_debug(f"[ROLLBACK] Successfully rolled back to {target_ver}")
        return True
    except Exception as e:
        log_update_debug(f"[ROLLBACK] Error during rollback: {e}")
        return False

def get_resource_path(relative_path: str) -> str:
    """取得靜態資源路徑，優先搜尋 assets/ 資料夾與 _MEIPASS"""
    app_dir = get_app_dir()
    search_dirs = []
    if hasattr(sys, '_MEIPASS'):
        search_dirs.append(sys._MEIPASS)
        search_dirs.append(os.path.join(sys._MEIPASS, "assets"))
    search_dirs.append(app_dir)
    search_dirs.append(os.path.join(app_dir, "assets"))

    for d in search_dirs:
        p = os.path.join(d, relative_path)
        if os.path.exists(p):
            return p
    return os.path.join(app_dir, relative_path)

# 遠端更新指標端點 (GitHub Raw 與 Firebase RTDB 雙保險)
GITHUB_MANIFEST_URL = "https://raw.githubusercontent.com/Isaacyang34/Homepage/gh-pages/TOOL_Stock_Portfolio_Tracker/version.json"
FIREBASE_MANIFEST_URL = "https://my-stock-tracker-2a94e-default-rtdb.asia-southeast1.firebasedatabase.app/update/version.json"
FIREBASE_STATS_URL = "https://my-stock-tracker-2a94e-default-rtdb.asia-southeast1.firebasedatabase.app/stats.json"

def report_firebase_telemetry(action: str, version: str = "", is_patch: bool = False):
    """非同步向 Firebase RTDB 統計節點發送原子計數 (零個資、背景靜默執行)"""
    def _worker():
        try:
            target_url = FIREBASE_STATS_URL
            clean_v = (version or APP_VERSION).replace(".", "_")

            payload = {}
            if action == "check":
                payload["updates/check_count"] = {".sv": {"increment": 1}}
                payload["updates/last_check_time"] = {".sv": "timestamp"}
            elif action == "download":
                payload["downloads/total"] = {".sv": {"increment": 1}}
                if is_patch:
                    payload["downloads/patch_count"] = {".sv": {"increment": 1}}
                    if clean_v:
                        payload[f"downloads/by_version/{clean_v}/patch"] = {".sv": {"increment": 1}}
                else:
                    payload["downloads/full_count"] = {".sv": {"increment": 1}}
                    if clean_v:
                        payload[f"downloads/by_version/{clean_v}/full"] = {".sv": {"increment": 1}}
                payload["downloads/last_download_time"] = {".sv": "timestamp"}
            elif action == "applied":
                payload["updates/applied_count"] = {".sv": {"increment": 1}}
                if clean_v:
                    payload[f"updates/by_version/{clean_v}/applied"] = {".sv": {"increment": 1}}
                payload["updates/last_applied_time"] = {".sv": "timestamp"}

            if payload:
                requests.patch(target_url, json=payload, timeout=3.5)
        except Exception:
            pass

    threading.Thread(target=_worker, daemon=True).start()

def parse_version_tuple(ver_str: str) -> Tuple[int, ...]:
    """解析版本字串例如 'V1.1.0.0' -> (1, 1, 0, 0)"""
    try:
        clean = ver_str.strip().lstrip('vV')
        parts = [int(p) for p in clean.split('.') if p.isdigit()]
        while len(parts) < 4:
            parts.append(0)
        return tuple(parts)
    except Exception:
        return (0, 0, 0, 0)

def is_newer_version(cloud_ver: str, local_ver: str) -> bool:
    """判斷雲端版本是否大於本地版本"""
    c_clean = str(cloud_ver or "").strip().lower()
    l_clean = str(local_ver or "").strip().lower()
    if not c_clean or not l_clean or c_clean == l_clean:
        return False

    c_tup = parse_version_tuple(cloud_ver)
    l_tup = parse_version_tuple(local_ver)
    if c_tup != (0, 0, 0, 0) and l_tup != (0, 0, 0, 0):
        return c_tup > l_tup
    return False

def fetch_update_manifest(timeout: float = 3.5) -> Tuple[bool, Optional[Dict[str, Any]], str]:
    """從遠端取得最新版本清單 (多通道備援 + 本機環境自動降級適配)"""
    headers = {
        "User-Agent": "Mozilla/5.0 (Windows NT 10.0; Win64; x64) StockPortfolioTracker/1.1",
        "Cache-Control": "no-cache",
        "Pragma": "no-cache"
    }

    remote_candidates = [
        ("GitHub gh-pages", GITHUB_MANIFEST_URL),
        ("GitHub master", "https://raw.githubusercontent.com/Isaacyang34/Homepage/master/TOOL_Stock_Portfolio_Tracker/version.json"),
        ("jsDelivr CDN", "https://cdn.jsdelivr.net/gh/Isaacyang34/Homepage@gh-pages/TOOL_Stock_Portfolio_Tracker/version.json"),
        ("Firebase RTDB", FIREBASE_MANIFEST_URL)
    ]

    for name, url in remote_candidates:
        try:
            resp = requests.get(url, headers=headers, timeout=timeout)
            if resp.status_code == 200:
                data = resp.json()
                if isinstance(data, dict) and "version" in data:
                    report_firebase_telemetry("check", version=data.get("version", ""))
                    return True, data, name
        except Exception:
            pass

    # 地端備援
    local_candidates = [
        os.path.join(get_app_dir(), "version.json"),
        os.path.join(os.path.dirname(os.path.abspath(__file__)), "version.json")
    ]
    for p in local_candidates:
        if os.path.exists(p):
            try:
                with open(p, "r", encoding="utf-8") as f:
                    data = json.load(f)
                    if isinstance(data, dict) and "version" in data:
                        return True, data, "本地版本資訊 (Local Fallback)"
            except Exception:
                pass

    return False, None, "無法連線至雲端版本伺服器"

def start_update_monitor():
    """當進入更新頁面後啟動獨立監控儀全程監控更新與重啟過程，精確記錄底層狀況"""
    try:
        app_dir = get_app_dir()
        ps_script = os.path.join(app_dir, "tools", "update_monitor.ps1")
        if not os.path.exists(ps_script):
            alt = os.path.join(os.path.dirname(os.path.abspath(__file__)), "tools", "update_monitor.ps1")
            if os.path.exists(alt):
                ps_script = alt
        if os.path.exists(ps_script):
            current_pid = os.getpid()
            creation_flags = subprocess.CREATE_NO_WINDOW if os.name == 'nt' else 0
            subprocess.Popen(
                ["powershell.exe", "-NoProfile", "-WindowStyle", "Hidden", "-ExecutionPolicy", "Bypass",
                 "-File", ps_script, "-TargetDir", app_dir, "-TargetPid", str(current_pid)],
                cwd=app_dir,
                creationflags=creation_flags
            )
            log_update_debug(f"[MONITOR] Launched update_monitor.ps1 for PID={current_pid}")
    except Exception as e:
        log_update_debug(f"[MONITOR] Failed to launch monitor: {e}")

class UpdateDialog(tk.Toplevel):
    """軟體版本更新決策與升級互動視窗 (支援輕量補丁、全包下載與歷史三版次自由切換)"""
    def __init__(self, parent, manifest: Dict[str, Any], is_newer: bool):
        super().__init__(parent)
        start_update_monitor()
        self.title("軟體版本更新與維護中心")
        self.geometry("680x580")
        self.minsize(640, 520)
        self.configure(bg="#20202a")
        self.transient(parent)

        upd_ico = get_resource_path("update_icon.ico")
        if not os.path.exists(upd_ico):
            upd_ico = get_resource_path("app_icon.ico")
        if os.path.exists(upd_ico):
            try:
                self.iconbitmap(upd_ico)
            except Exception:
                pass

        self.parent = parent
        self.manifest = manifest
        self.is_newer = is_newer
        self.is_downloading = False

        # 解析最近版本清單 (預設包含當前 manifest)
        self.recent_versions = manifest.get("recent_versions", [])
        if not self.recent_versions:
            self.recent_versions = [{
                "version": manifest.get("version", APP_VERSION),
                "release_date": manifest.get("release_date", "近期"),
                "patch_url": manifest.get("patch_url", manifest.get("download_url", "")),
                "full_package_url": manifest.get("full_package_url", ""),
                "changelog": manifest.get("changelog", "無更新紀錄")
            }]

        self.selected_version_data = self.recent_versions[0]

        self.protocol("WM_DELETE_WINDOW", self._on_dialog_close)
        self.build_ui()

    def build_ui(self):
        running_ver = getattr(self.parent, 'running_version', None) or get_boot_version()
        disk_ver = get_disk_version()
        cloud_ver = self.manifest.get("version", disk_ver)
        
        need_restart_for_ui = is_newer_version(disk_ver, running_ver)
        is_cloud_newer = is_newer_version(cloud_ver, disk_ver) or is_newer_version(cloud_ver, running_ver)
        self.is_newer = is_cloud_newer

        # 1. 頂部狀態橫幅 (TOP)
        top_bar = tk.Frame(self, bg="#282834", padx=18, pady=12)
        top_bar.pack(side=tk.TOP, fill=tk.X)

        if need_restart_for_ui:
            status_title = f"✦ 新版模組已安裝就緒: {disk_ver} (需重啟生效)"
            status_color = "#f59e0b"
            status_desc = f"磁碟模組已更新至 {disk_ver}，但目前主視窗仍運行於舊版記憶體 ({running_ver})。\n請重新啟動軟體以載入最新介面與全部新功能。"
        elif is_cloud_newer:
            status_title = f"✦ 發現軟體新版本: {cloud_ver}"
            status_color = "#f59e0b"
            status_desc = f"您目前使用的版本為 {running_ver}，官方已釋出新版 {cloud_ver}。可選擇秒級輕量更新或下載完整包："
        else:
            status_title = f"✔ 目前已是最新版本: {running_ver}"
            status_color = "#38bdf8"
            status_desc = "您目前運行的已是最新版次。您可在此檢視更新日誌，或切換至歷史版次下載/還原。"
            # 若已經是最新版本，同步讓母視窗更新按鈕復原為一般深色狀態
            if self.parent:
                if hasattr(self.parent, 'reset_update_button_state'):
                    self.parent.reset_update_button_state()
                elif hasattr(self.parent, 'btn_update') and self.parent.btn_update:
                    self.parent.btn_update.configure(
                        text="[⬆ 軟體更新]", bg="#323242", fg="#ffffff",
                        activebackground="#3a86ff", activeforeground="#ffffff"
                    )

        tk.Label(top_bar, text=status_title, bg="#282834", fg=status_color, font=("Microsoft JhengHei UI", 13, "bold")).pack(anchor="w")
        tk.Label(top_bar, text=status_desc, bg="#282834", fg="#d0d0d8", font=("Microsoft JhengHei UI", 9)).pack(anchor="w", pady=(4, 0))

        # 2. 底部操作按鈕列 (BOTTOM)
        bot = tk.Frame(self, bg="#1a1a24", padx=18, pady=12)
        bot.pack(side=tk.BOTTOM, fill=tk.X)
        self.bot_frame = bot

        # 按鈕群組：輕量更新、完整包下載、還原歷史、關閉
        self.btn_cancel = tk.Button(
            bot, text="稍後再說", bg="#323242", fg="#d0d0d8",
            activebackground="#3e3e50", activeforeground="#ffffff",
            font=("Microsoft JhengHei UI", 9), relief="flat", cursor="hand2",
            command=self.destroy
        )
        self.btn_cancel.pack(side=tk.RIGHT, padx=6, ipadx=10, ipady=5)

        # 若已安裝新模組但需重啟以載入新介面，放置最醒目的重啟按鈕
        if need_restart_for_ui:
            self.btn_restart_now = tk.Button(
                bot, text="🔄 立即重新啟動軟體", bg="#16a34a", fg="#ffffff",
                activebackground="#15803d", activeforeground="#ffffff",
                font=("Microsoft JhengHei UI", 10, "bold"), relief="flat", cursor="hand2",
                command=self._do_restart_now
            )
            self.btn_restart_now.pack(side=tk.RIGHT, padx=6, ipadx=12, ipady=5)

        self.btn_download_full = tk.Button(
            bot, text="📦 下載完整包 (修復/重裝)", bg="#3b4252", fg="#eceff4",
            activebackground="#4c566a", activeforeground="#ffffff",
            font=("Microsoft JhengHei UI", 9), relief="flat", cursor="hand2",
            command=self.start_download_full
        )
        self.btn_download_full.pack(side=tk.RIGHT, padx=6, ipadx=10, ipady=5)

        patch_btn_text = f"⚡ 重新安裝補丁 ({disk_ver})" if need_restart_for_ui else "⚡ 立即輕量更新 (秒速補丁)"
        self.btn_download_patch = tk.Button(
            bot, text=patch_btn_text, bg="#2563eb", fg="#ffffff",
            activebackground="#1d4ed8", activeforeground="#ffffff",
            font=("Microsoft JhengHei UI", 10, "bold"), relief="flat", cursor="hand2",
            command=self.start_download_patch
        )
        self.btn_download_patch.pack(side=tk.RIGHT, padx=6, ipadx=12, ipady=5)

        self.btn_view_log = tk.Button(
            bot, text="檢視更新日誌", bg="#262633", fg="#94a3b8",
            activebackground="#333344", activeforeground="#ffffff",
            font=("Microsoft JhengHei UI", 9), relief="flat", cursor="hand2",
            command=self._open_log_file
        )
        self.btn_view_log.pack(side=tk.LEFT, padx=4, ipadx=6, ipady=4)

        self.btn_rollback = tk.Button(
            bot, text="⏮ 還原本機前版", bg="#2d3748", fg="#cbd5e1",
            activebackground="#4a5568", activeforeground="#ffffff",
            font=("Microsoft JhengHei UI", 9), relief="flat", cursor="hand2",
            command=self._on_rollback_clicked
        )
        self.btn_rollback.pack(side=tk.LEFT, padx=4, ipadx=6, ipady=4)

        # 3. 下載進度條與狀態文字 (BOTTOM，緊貼按鈕列上方)
        self.progress_frame = tk.Frame(self, bg="#20202a", padx=18)
        self.progress_frame.pack(side=tk.BOTTOM, fill=tk.X, pady=(0, 4))

        self.pbar = ttk.Progressbar(self.progress_frame, orient="horizontal", mode="determinate")
        self.lbl_download_status = tk.Label(self.progress_frame, text="", bg="#20202a", fg="#38bdf8", font=("Microsoft JhengHei UI", 9))

        # 4. 中間資訊與改版公告區 (TOP/FILL BOTH)
        info_frame = tk.Frame(self, bg="#20202a", padx=18, pady=8)
        info_frame.pack(side=tk.TOP, fill=tk.BOTH, expand=True)

        meta_box = tk.Frame(info_frame, bg="#16161f", padx=12, pady=10, relief="solid", bd=1)
        meta_box.pack(fill=tk.X, pady=(0, 6))

        if need_restart_for_ui:
            tk.Label(meta_box, text=f"記憶體運行版本:   {running_ver}  (需重啟更新介面)", bg="#16161f", fg="#f59e0b", font=("Microsoft JhengHei UI", 9, "bold")).grid(row=0, column=0, sticky="w", pady=2)
            tk.Label(meta_box, text=f"磁碟已安裝版本:   {disk_ver}  (新版待生效)", bg="#16161f", fg="#38bdf8", font=("Microsoft JhengHei UI", 9, "bold")).grid(row=1, column=0, sticky="w", pady=2)
            ver_row = 2
        else:
            tk.Label(meta_box, text=f"本機目前版本:   {running_ver}", bg="#16161f", fg="#94a3b8", font=("Microsoft JhengHei UI", 9)).grid(row=0, column=0, sticky="w", pady=2)
            ver_row = 1

        # 雲端最後三版次選擇下拉框
        tk.Label(meta_box, text="雲端目標版本:   ", bg="#16161f", fg="#38bdf8", font=("Microsoft JhengHei UI", 9, "bold")).grid(row=ver_row, column=0, sticky="w", pady=4)
        
        version_names = [f"{v.get('version', '')}  ({v.get('release_date', '')})" for v in self.recent_versions]
        self.cmb_version = ttk.Combobox(meta_box, values=version_names, state="readonly", width=35)
        self.cmb_version.current(0)
        self.cmb_version.grid(row=ver_row, column=1, sticky="w", pady=4)
        self.cmb_version.bind("<<ComboboxSelected>>", self._on_version_selected)

        tk.Label(info_frame, text="版本修復與功能說明:", bg="#20202a", fg="#ffffff", font=("Microsoft JhengHei UI", 9, "bold")).pack(anchor="w", pady=(4, 4))

        txt_frame = tk.Frame(info_frame, bg="#14141c", relief="solid", bd=1)
        txt_frame.pack(fill=tk.BOTH, expand=True)

        self.txt_changelog = tk.Text(txt_frame, bg="#14141c", fg="#f0f2f8", font=("Microsoft JhengHei UI", 9), relief="flat", wrap=tk.WORD, bd=0, height=8)
        sb = tk.Scrollbar(txt_frame, orient="vertical", command=self.txt_changelog.yview)
        self.txt_changelog.configure(yscrollcommand=sb.set)
        self.txt_changelog.pack(side=tk.LEFT, fill=tk.BOTH, expand=True, padx=8, pady=6)
        sb.pack(side=tk.RIGHT, fill=tk.Y)

        self._update_display_content()

    def _on_version_selected(self, event=None):
        idx = self.cmb_version.current()
        if 0 <= idx < len(self.recent_versions):
            self.selected_version_data = self.recent_versions[idx]
            self._update_display_content()

    def _update_display_content(self):
        v_data = self.selected_version_data
        current_local = get_app_version()
        ver_str = v_data.get("version", current_local)
        changelog = v_data.get("changelog", "無更新詳細說明")

        self.txt_changelog.configure(state=tk.NORMAL)
        self.txt_changelog.delete("1.0", tk.END)
        self.txt_changelog.insert("1.0", changelog)
        self.txt_changelog.configure(state=tk.DISABLED)

        # 更新按鈕文字
        if is_newer_version(ver_str, current_local):
            self.btn_download_patch.configure(text=f"⚡ 立即輕量升級至 {ver_str}", bg="#2563eb")
        elif ver_str == current_local:
            self.btn_download_patch.configure(text=f"⚡ 重新下載/覆蓋補丁 ({ver_str})", bg="#3b82f6")
        else:
            self.btn_download_patch.configure(text=f"⚡ 降級/切換至歷史版 {ver_str}", bg="#d97706")

    def _open_log_file(self):
        log_path = os.path.join(get_app_dir(), "update_debug.log")
        if os.path.exists(log_path):
            try:
                os.startfile(log_path)
            except Exception as e:
                messagebox.showerror("開啟失敗", f"無法開啟日誌檔: {e}", parent=self)
        else:
            messagebox.showinfo("提示", "目前尚無更新日誌記錄！", parent=self)

    def _on_rollback_clicked(self):
        """處理還原本機歷史版本"""
        app_dir = get_app_dir()
        versions_dir = os.path.join(app_dir, "versions")
        if not os.path.exists(versions_dir):
            messagebox.showinfo("無歷史版本", "本機 versions/ 資料夾目前尚無任何備份記錄！", parent=self)
            return

        backups = [d for d in os.listdir(versions_dir) if os.path.isdir(os.path.join(versions_dir, d))]
        if not backups:
            messagebox.showinfo("無歷史版本", "本機 versions/ 資料夾目前尚無任何備份記錄！", parent=self)
            return

        backups.sort(reverse=True)
        # 彈出確認詢問
        msg = "本機已建立下列歷史版本快照：\n\n" + "\n".join([f" • {b}" for b in backups])
        target_v = backups[0]
        ans = messagebox.askyesno(
            "還原本機歷史版本",
            f"{msg}\n\n是否立即將系統還原至最近的備份版次【{target_v}】？\n\n（還原將覆蓋現有模組代碼，個人資料庫 portfolio.db 絕不受影響）",
            parent=self
        )
        if ans:
            ok = rollback_to_version(app_dir, target_v)
            if ok:
                messagebox.showinfo("還原成功", f"系統已成功還原至【{target_v}】！\n請重新啟動程式以載入該版次。", parent=self)
                self.destroy()
                if self.parent:
                    self.parent.destroy()
                sys.exit(0)
            else:
                messagebox.showerror("還原失敗", "還原過程發生異常，請檢視 update_debug.log。", parent=self)

    def start_download_patch(self):
        v_data = self.selected_version_data
        url = v_data.get("patch_url") or self.manifest.get("patch_url")
        if not url or not url.lower().endswith(".zip"):
            messagebox.showinfo("提示", "此版次僅提供完整安裝包，即將為您進行完整包下載...", parent=self)
            self.start_download_full()
            return
        self._start_download_task(url, is_patch=True)

    def start_download_full(self):
        v_data = self.selected_version_data
        url = v_data.get("full_package_url") or v_data.get("download_url")
        if not url:
            messagebox.showerror("錯誤", "此版本無完整安裝包網址！", parent=self)
            return
        self._start_download_task(url, is_patch=False)

    def _start_download_task(self, url: str, is_patch: bool):
        self.is_downloading = True
        ver_str = self.selected_version_data.get("version", "")
        log_update_debug(f"[UPDATE] Started download (is_patch={is_patch}) for {ver_str} from {url}")

        self.btn_download_patch.configure(state=tk.DISABLED)
        self.btn_download_full.configure(state=tk.DISABLED)
        self.btn_cancel.configure(state=tk.DISABLED)

        self.pbar.pack(fill=tk.X, pady=(0, 4))
        self.lbl_download_status.pack(anchor="w")
        self.lbl_download_status.configure(text="正在連線至下載伺服器...")
        self.pbar["value"] = 0

        threading.Thread(target=self._download_worker, args=(url, is_patch, ver_str), daemon=True).start()

    def _download_worker(self, url: str, is_patch: bool, version: str):
        target_dir = get_app_dir()
        filename = f"Stock_Portfolio_Tracker_{version}_patch.zip" if is_patch else f"Stock_Portfolio_Tracker_{version}_full.zip"
        if not is_patch and url.endswith(".exe"):
            filename = f"Stock_Portfolio_Tracker_{version}_update.exe"
            
        temp_file = os.path.join(target_dir, filename)
        log_update_debug(f"[DOWNLOAD] Target dir: {target_dir}, Temp target: {temp_file}")

        try:
            resp = requests.get(url, stream=True, timeout=45)
            if resp.status_code != 200:
                raise RuntimeError(f"伺服器回應代碼: {resp.status_code}")

            total_size = int(resp.headers.get("content-length", 0))
            downloaded = 0

            with open(temp_file, "wb") as f:
                for chunk in resp.iter_content(chunk_size=65536):
                    if not chunk:
                        continue
                    f.write(chunk)
                    downloaded += len(chunk)
                    if total_size > 0:
                        pct = int((downloaded / total_size) * 100)
                        msg = f"正在下載: {downloaded / (1024*1024):.2f} MB / {total_size / (1024*1024):.2f} MB ({pct}%)"
                        self.after(0, lambda p=pct, m=msg: self._update_pbar(p, m))
                    else:
                        msg = f"已下載: {downloaded / (1024*1024):.2f} MB"
                        self.after(0, lambda m=msg: self._update_pbar(50, m))

            actual_size = os.path.getsize(temp_file)
            log_update_debug(f"[DOWNLOAD] Actual size: {actual_size} bytes")
            if is_patch and actual_size < 5 * 1024:
                raise RuntimeError("下載補丁包大小異常 (<5KB)，可能不完整！")
            elif not is_patch and actual_size < 100 * 1024:
                raise RuntimeError("下載完整包大小異常 (<100KB)，可能不完整！")

            self.after(0, lambda: self._on_download_success(target_dir, temp_file, version, is_patch))
        except Exception as e:
            log_update_debug(f"[DOWNLOAD] ERROR: {e}")
            self.after(0, lambda err=str(e): self._on_download_failed(err))

    def _on_dialog_close(self):
        if self.is_downloading:
            res = messagebox.askyesno(
                "背景下載提醒",
                "更新檔正在下載中。\n\n是否將下載視窗縮小至背景繼續下載？\n\n• 按【是 (Yes)】：視窗縮小至背景，完成時會主動跳出提醒\n• 按【否 (No)】：取消本次下載並關閉視窗",
                parent=self
            )
            if res:
                self.withdraw()
                return
            else:
                self.destroy()
                return
        self.destroy()

    def _do_restart_now(self):
        """立即重新啟動軟體以載入磁碟上的最新版本介面"""
        target_dir = get_app_dir()
        self.destroy()
        if self.parent:
            self.parent.destroy()
        restart_application(target_dir)

    def _update_pbar(self, val: int, msg: str):
        self.pbar["value"] = val
        self.lbl_download_status.configure(text=msg)
        if hasattr(self.parent, "status_lbl") and self.parent.status_lbl:
            self.parent.status_lbl.configure(text=f"⬇ {msg}")

    def _on_download_success(self, target_dir: str, temp_file: str, version: str, is_patch: bool):
        log_update_debug(f"[DOWNLOAD_SUCCESS] temp_file={temp_file}, ver={version}, is_patch={is_patch}")
        report_firebase_telemetry("download", version=version, is_patch=is_patch)
        try:
            self.deiconify()
            self.lift()
            self.focus_force()
        except Exception:
            pass

        self.pbar["value"] = 100
        pkg_type_name = "輕量秒級補丁包" if is_patch else "完整安裝包"
        self.lbl_download_status.configure(
            text=f"✔ 新版本 ({version}) {pkg_type_name} 已下載完畢！請選擇何時套用：",
            fg="#52c41a"
        )
        if hasattr(self.parent, "status_lbl") and self.parent.status_lbl:
            self.parent.status_lbl.configure(text=f"✔ 新版本 ({version}) 已就緒")

        # 隱藏下載按鈕
        self.btn_download_patch.pack_forget()
        self.btn_download_full.pack_forget()
        self.btn_cancel.pack_forget()

        parent_target = self.parent if (self.parent and self.parent.winfo_exists()) else self

        # 若為輕量補丁包：解壓覆蓋後，彈出明確重啟確認對話框，杜絕記憶體殘留與介面脫節
        if is_patch:
            create_local_version_snapshot(target_dir, APP_VERSION)
            try:
                with zipfile.ZipFile(temp_file, 'r') as zf:
                    zf.extractall(target_dir)
                log_update_debug(f"[PATCH] Extracted patch zip successfully.")
                report_firebase_telemetry("applied", version=version, is_patch=True)
            except Exception as e:
                log_update_debug(f"[PATCH] ERROR extracting zip: {e}")
                messagebox.showerror("更新失敗", f"解壓縮補丁包失敗: {e}", parent=self)
                return
            finally:
                try:
                    os.remove(temp_file)
                except Exception:
                    pass

            choice = messagebox.askyesno(
                "更新就緒 - 建議重啟軟體",
                f"✔ 新版本【{version}】補丁已成功下載並安裝至本機！\n\n"
                f"新版本包含主視窗介面佈局與功能更新。\n"
                f"是否立即重新啟動軟體以載入全新介面？\n\n"
                f"• 按【是 (Yes)】：立即安全重新啟動軟體（強烈建議）\n"
                f"• 按【否 (No)】：稍後手動重啟（目前繼續使用舊版介面）",
                parent=parent_target
            )
            if choice:
                self.destroy()
                if self.parent:
                    self.parent.destroy()
                restart_application(target_dir)
            else:
                running_ver = getattr(self.parent, 'running_version', None) or get_boot_version()
                if hasattr(self.parent, "status_lbl") and self.parent.status_lbl:
                    self.parent.status_lbl.configure(text=f"✔ 新版 {version} 已就緒 (重啟後生效)")
                if hasattr(self.parent, "title"):
                    self.parent.title(f"本地端台美股庫存即時損益、歷史與股息追蹤系統 (Stock Portfolio Tracker) {running_ver} [🔔 新版待重啟生效]")
                self.destroy()
            return

        # 僅當為跨核心完整安裝包 (需重啟主程式) 時，才呈現手動確認與延遲選項
        self.btn_apply_now = tk.Button(
            self.bot_frame, text=f"🚀 馬上置換更新 ({version})", bg="#2563eb", fg="#ffffff",
            activebackground="#1d4ed8", activeforeground="#ffffff",
            font=("Microsoft JhengHei UI", 10, "bold"), relief="flat", cursor="hand2",
            command=lambda: self._do_apply(target_dir, temp_file, restart=True, is_patch=False)
        )
        self.btn_apply_now.pack(side=tk.RIGHT, padx=6, ipadx=12, ipady=5)

        self.btn_apply_later = tk.Button(
            self.bot_frame, text="稍後更新 (待關閉主程式後再更新)", bg="#334155", fg="#e2e8f0",
            activebackground="#475569", activeforeground="#ffffff",
            font=("Microsoft JhengHei UI", 9), relief="flat", cursor="hand2",
            command=lambda: self._do_defer(target_dir, temp_file, version, is_patch)
        )
        self.btn_apply_later.pack(side=tk.RIGHT, padx=6, ipadx=10, ipady=5)

        parent_target = self.parent if (self.parent and self.parent.winfo_exists()) else self
        choice = messagebox.askyesno(
            "完整安裝包下載就緒",
            f"版本【{version}】{pkg_type_name} 已下載完畢！\n\n"
            f"是否要馬上重啟更新？\n\n"
            f"• 按【是 (Yes)】：自動備份當前版次至 versions/，並關閉重啟主程式\n"
            f"• 按【否 (No)】：等到您下次關閉主程式後再自動置換",
            parent=parent_target
        )
        if choice:
            self._do_apply(target_dir, temp_file, restart=True, is_patch=False)
        else:
            self._do_defer(target_dir, temp_file, version, is_patch)

    def _do_apply(self, target_dir: str, temp_file: str, restart: bool = True, is_patch: bool = False):
        log_update_debug(f"[USER_ACTION] Immediate apply triggered for {temp_file}, is_patch={is_patch}")
        target_v = self.selected_version_data.get("version", "")
        report_firebase_telemetry("applied", version=target_v, is_patch=is_patch)
        if is_patch:
            target_v = self.selected_version_data.get("version", "")
            ok = apply_patch_hot_reload(target_dir, temp_file, parent_app=self.parent, target_version=target_v)
            if ok:
                self.destroy()
                return

        # 若為完整安裝包 (is_patch=False，包含主 EXE)，執行外部安全預解壓置換與重啟，徹底杜絕檔案鎖定 Permission denied
        apply_full_package_now(target_dir, temp_file, restart=restart)
        self.destroy()
        if self.parent:
            self.parent.destroy()
        sys.exit(0)

    def _do_defer(self, target_dir: str, temp_file: str, version: str, is_patch: bool):
        log_update_debug(f"[USER_ACTION] Deferred update for {version}")
        set_pending_update(target_dir, temp_file, version, is_patch=is_patch)
        if hasattr(self.parent, "on_update_deferred"):
            self.parent.on_update_deferred(version)
        parent_target = self.parent if (self.parent and self.parent.winfo_exists()) else self
        messagebox.showinfo(
            "已排程更新",
            f"已記錄更新！\n\n軟體將在您關閉主程式後自動執行置換。\n您現在可以繼續正常使用！",
            parent=parent_target
        )
        self.destroy()

    def _on_download_failed(self, err: str):
        log_update_debug(f"[UPDATE] Download failed: {err}")
        self.lbl_download_status.configure(text=f"[!] 下載失敗: {err}", fg="#ff4d4f")
        messagebox.showerror("下載失敗", f"下載更新檔時發生錯誤: {err}\n詳情請檢視 update_debug.log。", parent=self)
        self.btn_download_patch.configure(state=tk.NORMAL)
        self.btn_download_full.configure(state=tk.NORMAL)
        self.btn_cancel.configure(state=tk.NORMAL)
