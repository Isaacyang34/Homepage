import os
import sys
import json
import time
import threading
import subprocess
import requests
import tkinter as tk
from tkinter import ttk, messagebox
from typing import Optional, Dict, Any, Tuple

APP_VERSION = "V1.0.2.5"

# 延遲更新狀態管理 (使用者可選擇「稍後於關閉程式時自動置換」)
_PENDING_UPDATE: Dict[str, Any] = {
    "ready": False,
    "target_dir": "",
    "temp_exe": "",
    "version": ""
}

def set_pending_update(target_dir: str, temp_exe: str, version: str):
    _PENDING_UPDATE["ready"] = True
    _PENDING_UPDATE["target_dir"] = target_dir
    _PENDING_UPDATE["temp_exe"] = temp_exe
    _PENDING_UPDATE["version"] = version
    log_update_debug(f"[PENDING] Update deferred. version={version}, temp_exe={temp_exe}")

def is_pending_update() -> bool:
    ready = _PENDING_UPDATE.get("ready", False)
    temp_exe = _PENDING_UPDATE.get("temp_exe", "")
    return ready and os.path.exists(temp_exe)

def get_pending_version() -> str:
    return _PENDING_UPDATE.get("version", "")

def get_app_dir() -> str:
    """取得應用程式根目錄 (相容 PyInstaller 凍結執行與原始碼執行)"""
    if getattr(sys, 'frozen', False):
        return os.path.dirname(os.path.abspath(sys.executable))
    return os.path.dirname(os.path.abspath(__file__))

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

def apply_update_now(target_dir: str, temp_exe: str, restart: bool = True):
    """
    執行更新批次置換作業
    :param target_dir: 應用程式目標目錄
    :param temp_exe: 下載之暫存更新執行檔
    :param restart: True 為立即重啟新版；False 為程式關閉時背景安靜置換，不主動重啟
    """
    bat_path = os.path.join(target_dir, "apply_update.bat")
    target_exe_name = "Stock_Portfolio_Tracker.exe"
    temp_exe_name = os.path.basename(temp_exe)
    current_pid = os.getpid()

    log_update_debug(f"[APPLY] apply_update_now triggered. PID={current_pid}, restart={restart}")

    if restart:
        restart_action = f"""echo [%DATE% %TIME%] [BATCH] Launching updated {target_exe_name} via Windows Shell (explorer.exe)... >> "%LOG_FILE%"
explorer.exe "%~dp0{target_exe_name}"
set LAUNCH_ERR=%errorlevel%
echo [%DATE% %TIME%] [BATCH] Explorer launched with errorlevel: %LAUNCH_ERR% >> "%LOG_FILE%"
"""
    else:
        restart_action = f"""echo [%DATE% %TIME%] [BATCH] Silent update on app exit completed. Not restarting. >> "%LOG_FILE%"
"""

    bat_content = f"""@echo off
REM Stock Portfolio Tracker Auto Update Script
set LOG_FILE=%~dp0update_debug.log

echo ====================================================== >> "%LOG_FILE%"
echo [%DATE% %TIME%] [BATCH] ===== Update Script Started (restart={restart}) ===== >> "%LOG_FILE%"
echo [%DATE% %TIME%] [BATCH] Script path: %~f0 >> "%LOG_FILE%"
echo [%DATE% %TIME%] [BATCH] Working directory: %cd% >> "%LOG_FILE%"
echo [%DATE% %TIME%] [BATCH] Target PID to terminate: {current_pid} >> "%LOG_FILE%"
echo [%DATE% %TIME%] [BATCH] Inherited _MEIPASS: '%_MEIPASS%' >> "%LOG_FILE%"

echo [%DATE% %TIME%] [BATCH] Clearing PyInstaller and Python environment variables... >> "%LOG_FILE%"
set _MEIPASS=
set _MEIPASS2=
set PYTHONHOME=
set PYTHONPATH=
echo [%DATE% %TIME%] [BATCH] _MEIPASS after clear: '%_MEIPASS%' >> "%LOG_FILE%"

cd /d "%~dp0"
echo [%DATE% %TIME%] [BATCH] Changed working dir to: %cd% >> "%LOG_FILE%"

echo [%DATE% %TIME%] [BATCH] Terminating parent PID {current_pid}... >> "%LOG_FILE%"
taskkill /PID {current_pid} /F >> "%LOG_FILE%" 2>&1
timeout /t 2 /nobreak > nul

set RETRY=0
:RETRY_LOOP
set /a RETRY+=1
echo [%DATE% %TIME%] [BATCH] Overwrite attempt %RETRY%: Copying "{temp_exe_name}" to "{target_exe_name}"... >> "%LOG_FILE%"
copy /y "{temp_exe_name}" "{target_exe_name}" >> "%LOG_FILE%" 2>&1
if errorlevel 1 (
    echo [%DATE% %TIME%] [BATCH] Overwrite locked, waiting 1s (retry %RETRY%)... >> "%LOG_FILE%"
    timeout /t 1 /nobreak > nul
    if %RETRY% leq 10 goto RETRY_LOOP
    echo [%DATE% %TIME%] [BATCH] FATAL ERROR: Failed to overwrite {target_exe_name} after 10 retries! >> "%LOG_FILE%"
    exit /b 1
)

echo [%DATE% %TIME%] [BATCH] Overwrite succeeded! Target file info: >> "%LOG_FILE%"
dir "{target_exe_name}" >> "%LOG_FILE%" 2>&1

echo [%DATE% %TIME%] [BATCH] Removing temporary download file... >> "%LOG_FILE%"
del /f /q "{temp_exe_name}" >> "%LOG_FILE%" 2>&1

{restart_action}
echo [%DATE% %TIME%] [BATCH] ===== Update Script Completed ===== >> "%LOG_FILE%"
exit
"""
    try:
        with open(bat_path, "w", encoding="ascii") as f:
            f.write(bat_content)

        log_update_debug(f"[APPLY] Written batch script to {bat_path}")

        creation_flags = 0x00000008 | (subprocess.CREATE_NO_WINDOW if os.name == 'nt' else 0)
        clean_env = os.environ.copy()
        clean_env.pop('_MEIPASS2', None)
        clean_env.pop('_MEIPASS', None)

        log_update_debug(f"[APPLY] Spawning detached cmd.exe /c apply_update.bat (restart={restart})")
        subprocess.Popen(
            ["cmd.exe", "/c", bat_path],
            cwd=target_dir,
            env=clean_env,
            creationflags=creation_flags
        )
    except Exception as e:
        log_update_debug(f"[APPLY] FATAL ERROR spawning updater script: {e}")

def get_resource_path(relative_path: str) -> str:
    """取得靜態資源路徑，相容開發環境與 PyInstaller 打包 (_MEIPASS)"""
    if hasattr(sys, '_MEIPASS'):
        base_path = sys._MEIPASS
    else:
        base_path = get_app_dir()
    return os.path.join(base_path, relative_path)

# 遠端更新指標端點 (GitHub Raw 與 Firebase RTDB 雙保險)
GITHUB_MANIFEST_URL = "https://raw.githubusercontent.com/Isaacyang34/Homepage/gh-pages/TOOL_Stock_Portfolio_Tracker/version.json"
FIREBASE_MANIFEST_URL = "https://my-stock-tracker-2a94e-default-rtdb.asia-southeast1.firebasedatabase.app/update/version.json"

def parse_version_tuple(ver_str: str) -> Tuple[int, ...]:
    """解析版本字串例如 'V1.0.2' -> (1, 0, 2, 0), 'V1.0.2.1' -> (1, 0, 2, 1)"""
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
    c_tup = parse_version_tuple(cloud_ver)
    l_tup = parse_version_tuple(local_ver)
    if c_tup != (0, 0, 0, 0) and l_tup != (0, 0, 0, 0):
        return c_tup > l_tup
    return cloud_ver.strip().lower() != local_ver.strip().lower()

def fetch_update_manifest(timeout: float = 3.5) -> Tuple[bool, Optional[Dict[str, Any]], str]:
    """
    從遠端取得最新版本清單 (多通道備援 + 本機環境自動降級適配)
    回傳 (成功與否, manifest內容, 訊息來源)
    """
    headers = {
        "User-Agent": "Mozilla/5.0 (Windows NT 10.0; Win64; x64) StockPortfolioTracker/1.0",
        "Cache-Control": "no-cache",
        "Pragma": "no-cache"
    }

    # 1. 優先嘗試遠端端點 (GitHub Raw / jsDelivr / Firebase)
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
                    return True, data, name
        except Exception:
            pass

    # 2. 地端/離線/開發測試環境備援 (讀取本地 version.json 供驗證)
    local_candidates = [
        os.path.join(os.path.dirname(os.path.abspath(__file__)), "version.json"),
        os.path.join(os.path.dirname(os.path.abspath(sys.argv[0])), "version.json"),
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

class UpdateDialog(tk.Toplevel):
    """軟體版本更新決策與升級互動視窗"""
    def __init__(self, parent, manifest: Dict[str, Any], is_newer: bool):
        super().__init__(parent)
        self.title("軟體版本更新")
        self.geometry("620x540")
        self.minsize(580, 480)
        self.configure(bg="#20202a")
        self.transient(parent)
        # 不啟用 grab_set()，確保下載過程中主程式維持 100% 可自由操作與點選

        # 設定專屬更新圖示 (徹底消除預設藍色羽毛)
        upd_ico = get_resource_path("update_icon.ico")
        if not os.path.exists(upd_ico):
            upd_ico = get_resource_path("app_icon.ico")
        if os.path.exists(upd_ico):
            try:
                self.iconbitmap(upd_ico)
            except Exception:
                try:
                    self.iconbitmap(default=upd_ico)
                except Exception:
                    pass

        self.parent = parent
        self.manifest = manifest
        self.is_newer = is_newer
        self.is_downloading = False

        self.build_ui()

    def build_ui(self):
        cloud_ver = self.manifest.get("version", "未知")
        rel_date = self.manifest.get("release_date", "近期")
        changelog = self.manifest.get("changelog", "無詳細說明")

        # 1. 頂部狀態橫幅 (TOP)
        top_bar = tk.Frame(self, bg="#282834", padx=18, pady=12)
        top_bar.pack(side=tk.TOP, fill=tk.X)

        if self.is_newer:
            status_title = f"✦ 發現軟體新版本: {cloud_ver}"
            status_color = "#f59e0b"
            status_desc = f"您目前使用的版本為 {APP_VERSION}，官方已釋出新版 {cloud_ver}，請檢視更新內容並決定是否升級："
        else:
            status_title = f"✔ 目前已是最新版本: {APP_VERSION}"
            status_color = "#38bdf8"
            status_desc = "您目前運行的已是最新版本，功能皆為最新狀態，無需更新。"

        tk.Label(top_bar, text=status_title, bg="#282834", fg=status_color, font=("Microsoft JhengHei UI", 13, "bold")).pack(anchor="w")
        tk.Label(top_bar, text=status_desc, bg="#282834", fg="#d0d0d8", font=("Microsoft JhengHei UI", 9)).pack(anchor="w", pady=(4, 0))

        # 2. 底部操作按鈕列 (優先於中間內容區 pack 到 BOTTOM，保證絕對永遠可見、絕不被文字框擠壓)
        bot = tk.Frame(self, bg="#1a1a24", padx=18, pady=12)
        bot.pack(side=tk.BOTTOM, fill=tk.X)
        self.bot_frame = bot

        if self.is_newer:
            self.btn_download = tk.Button(
                bot, text=f"立即下載升級至 {cloud_ver}", bg="#2563eb", fg="#ffffff",
                activebackground="#1d4ed8", activeforeground="#ffffff",
                font=("Microsoft JhengHei UI", 10, "bold"), relief="flat", cursor="hand2",
                command=self.start_download
            )
            self.btn_download.pack(side=tk.RIGHT, ipadx=14, ipady=5)

            self.btn_cancel = tk.Button(
                bot, text="稍後再說 (暫不更新)", bg="#323242", fg="#d0d0d8",
                activebackground="#3e3e50", activeforeground="#ffffff",
                font=("Microsoft JhengHei UI", 9), relief="flat", cursor="hand2",
                command=self.destroy
            )
            self.btn_cancel.pack(side=tk.RIGHT, padx=10, ipadx=10, ipady=5)
        else:
            self.btn_close = tk.Button(
                bot, text="確定關閉", bg="#323242", fg="#ffffff",
                activebackground="#3e3e50", activeforeground="#ffffff",
                font=("Microsoft JhengHei UI", 9), relief="flat", cursor="hand2",
                command=self.destroy
            )
            self.btn_close.pack(side=tk.RIGHT, ipadx=14, ipady=4)

            self.btn_reinstall = tk.Button(
                bot, text=f"重新下載/覆蓋版本 ({cloud_ver})", bg="#2563eb", fg="#ffffff",
                activebackground="#1d4ed8", activeforeground="#ffffff",
                font=("Microsoft JhengHei UI", 9, "bold"), relief="flat", cursor="hand2",
                command=self.start_download
            )
            self.btn_reinstall.pack(side=tk.RIGHT, padx=10, ipadx=10, ipady=4)

        self.btn_view_log = tk.Button(
            bot, text="檢視更新日誌", bg="#262633", fg="#94a3b8",
            activebackground="#333344", activeforeground="#ffffff",
            font=("Microsoft JhengHei UI", 9), relief="flat", cursor="hand2",
            command=self._open_log_file
        )
        self.btn_view_log.pack(side=tk.LEFT, ipadx=8, ipady=4)

        # 3. 下載進度條與狀態文字 (BOTTOM，緊貼按鈕列上方)
        self.progress_frame = tk.Frame(self, bg="#20202a", padx=18)
        self.progress_frame.pack(side=tk.BOTTOM, fill=tk.X, pady=(0, 4))

        self.pbar = ttk.Progressbar(self.progress_frame, orient="horizontal", mode="determinate")
        self.lbl_download_status = tk.Label(self.progress_frame, text="", bg="#20202a", fg="#38bdf8", font=("Microsoft JhengHei UI", 9))

        # 4. 中間資訊與改版公告區 (最後 pack，fill BOTH/EXPAND 自適應中央所有區域)
        info_frame = tk.Frame(self, bg="#20202a", padx=18, pady=8)
        info_frame.pack(side=tk.TOP, fill=tk.BOTH, expand=True)

        meta_box = tk.Frame(info_frame, bg="#16161f", padx=12, pady=8, relief="solid", bd=1)
        meta_box.pack(fill=tk.X, pady=(0, 6))

        tk.Label(meta_box, text=f"本機目前版本:   {APP_VERSION}", bg="#16161f", fg="#94a3b8", font=("Microsoft JhengHei UI", 9)).grid(row=0, column=0, sticky="w", pady=2)
        
        target_color = "#f59e0b" if self.is_newer else "#38bdf8"
        tk.Label(meta_box, text=f"雲端最新版本:   {cloud_ver}  (發布日期: {rel_date})", bg="#16161f", fg=target_color, font=("Microsoft JhengHei UI", 9, "bold")).grid(row=1, column=0, sticky="w", pady=2)

        tk.Label(info_frame, text="版本更新內容與修復詳情:", bg="#20202a", fg="#ffffff", font=("Microsoft JhengHei UI", 9, "bold")).pack(anchor="w", pady=(2, 4))

        txt_frame = tk.Frame(info_frame, bg="#14141c", relief="solid", bd=1)
        txt_frame.pack(fill=tk.BOTH, expand=True)

        self.txt_changelog = tk.Text(txt_frame, bg="#14141c", fg="#f0f2f8", font=("Microsoft JhengHei UI", 9), relief="flat", wrap=tk.WORD, bd=0, height=8)
        sb = tk.Scrollbar(txt_frame, orient="vertical", command=self.txt_changelog.yview)
        self.txt_changelog.configure(yscrollcommand=sb.set)
        self.txt_changelog.pack(side=tk.LEFT, fill=tk.BOTH, expand=True, padx=8, pady=6)
        sb.pack(side=tk.RIGHT, fill=tk.Y)

        self.txt_changelog.insert("1.0", changelog)
        self.txt_changelog.configure(state=tk.DISABLED)

    def _open_log_file(self):
        log_path = os.path.join(get_app_dir(), "update_debug.log")
        if os.path.exists(log_path):
            try:
                os.startfile(log_path)
            except Exception as e:
                messagebox.showerror("開啟失敗", f"無法開啟日誌檔: {e}", parent=self)
        else:
            messagebox.showinfo("提示", "目前尚無更新日誌記錄！", parent=self)

    def start_download(self):
        download_url = self.manifest.get("download_url")
        if not download_url:
            messagebox.showerror("錯誤", "更新檔案下載網址為空，無法執行更新！", parent=self)
            return

        self.is_downloading = True
        log_update_debug(f"[UPDATE] User clicked download for version {self.manifest.get('version', '')}")
        if hasattr(self, 'btn_download'):
            self.btn_download.configure(state=tk.DISABLED, text="下載升級中...")
        if hasattr(self, 'btn_reinstall'):
            self.btn_reinstall.configure(state=tk.DISABLED, text="下載中...")
        if hasattr(self, 'btn_cancel'):
            self.btn_cancel.configure(state=tk.DISABLED)

        self.pbar.pack(fill=tk.X, pady=(0, 4))
        self.lbl_download_status.pack(anchor="w")
        self.lbl_download_status.configure(text="正在連線至下載伺服器...")
        self.pbar["value"] = 0

        threading.Thread(target=self._download_worker, args=(download_url,), daemon=True).start()

    def _download_worker(self, url: str):
        target_dir = get_app_dir()
        temp_exe = os.path.join(target_dir, "Stock_Portfolio_Tracker_update.exe")
        log_update_debug(f"[DOWNLOAD] Starting download from {url}")
        log_update_debug(f"[DOWNLOAD] Target dir: {target_dir}, Temp target: {temp_exe}")

        try:
            resp = requests.get(url, stream=True, timeout=45)
            if resp.status_code != 200:
                raise RuntimeError(f"伺服器回應代碼: {resp.status_code}")

            total_size = int(resp.headers.get("content-length", 0))
            downloaded = 0
            log_update_debug(f"[DOWNLOAD] Server returned 200 OK, total_size={total_size} bytes")

            with open(temp_exe, "wb") as f:
                for chunk in resp.iter_content(chunk_size=65536):
                    if not chunk:
                        continue
                    f.write(chunk)
                    downloaded += len(chunk)
                    if total_size > 0:
                        pct = int((downloaded / total_size) * 100)
                        msg = f"正在下載更新檔: {downloaded / (1024*1024):.1f} MB / {total_size / (1024*1024):.1f} MB ({pct}%)"
                        self.after(0, lambda p=pct, m=msg: self._update_pbar(p, m))
                    else:
                        msg = f"已下載: {downloaded / (1024*1024):.1f} MB"
                        self.after(0, lambda m=msg: self._update_pbar(50, m))

            # 驗證檔案是否有效 (PyInstaller 執行檔通常 > 10MB)
            actual_size = os.path.getsize(temp_exe)
            log_update_debug(f"[DOWNLOAD] Completed. Actual downloaded size={actual_size} bytes")
            if actual_size < 5 * 1024 * 1024:
                raise RuntimeError("下載檔案大小異常 (<5MB)，可能檔案不完整！")

            self.after(0, lambda: self._on_download_success(target_dir, temp_exe))
        except Exception as e:
            log_update_debug(f"[DOWNLOAD] ERROR: {e}")
            self.after(0, lambda err=str(e): self._on_download_failed(err))

    def _update_pbar(self, val: int, msg: str):
        self.pbar["value"] = val
        self.lbl_download_status.configure(text=msg)
        if hasattr(self.parent, "status_lbl") and self.parent.status_lbl:
            self.parent.status_lbl.configure(text=f"⬇ {msg}")

    def _on_download_success(self, target_dir: str, temp_exe: str):
        cloud_ver = self.manifest.get("version", "新版本")
        log_update_debug(f"[DOWNLOAD_SUCCESS] temp_exe={temp_exe}, size={os.path.getsize(temp_exe)} bytes")
        
        self.pbar["value"] = 100
        self.lbl_download_status.configure(
            text=f"✔ 新版本 ({cloud_ver}) 已下載完畢！您可以選擇「馬上更新」或「待關閉主程式後再更新」：",
            fg="#52c41a"
        )
        if hasattr(self.parent, "status_lbl") and self.parent.status_lbl:
            self.parent.status_lbl.configure(text=f"✔ 新版本 ({cloud_ver}) 已下載完畢")

        # 隱藏下載階段的按鈕
        for btn_name in ('btn_download', 'btn_reinstall', 'btn_cancel', 'btn_close'):
            if hasattr(self, btn_name):
                try:
                    getattr(self, btn_name).pack_forget()
                except Exception:
                    pass

        # 建立兩個明確操作按鈕供使用者決定何時升級
        self.btn_apply_now = tk.Button(
            self.bot_frame, text=f"🚀 馬上更新 ({cloud_ver})", bg="#2563eb", fg="#ffffff",
            activebackground="#1d4ed8", activeforeground="#ffffff",
            font=("Microsoft JhengHei UI", 10, "bold"), relief="flat", cursor="hand2",
            command=lambda: self._do_apply(target_dir, temp_exe, restart=True)
        )
        self.btn_apply_now.pack(side=tk.RIGHT, ipadx=12, ipady=5)

        self.btn_apply_later = tk.Button(
            self.bot_frame, text="稍後更新 (待關閉主程式後再更新)", bg="#334155", fg="#e2e8f0",
            activebackground="#475569", activeforeground="#ffffff",
            font=("Microsoft JhengHei UI", 9), relief="flat", cursor="hand2",
            command=lambda: self._do_defer(target_dir, temp_exe, cloud_ver)
        )
        self.btn_apply_later.pack(side=tk.RIGHT, padx=10, ipadx=10, ipady=5)

        # 主動彈出提示對話框，詢問使用者是否要馬上更新
        parent_target = self.parent if (self.parent and self.parent.winfo_exists()) else self
        choice = messagebox.askyesno(
            "軟體更新提醒",
            f"新版本【{cloud_ver}】已下載完畢！\n\n"
            f"是否要馬上更新？\n\n"
            f"• 按【是 (Yes)】：馬上關閉程式並執行更新\n"
            f"• 按【否 (No)】：等到您關閉主程式後再更新",
            parent=parent_target
        )
        if choice:
            self._do_apply(target_dir, temp_exe, restart=True)
        else:
            self._do_defer(target_dir, temp_exe, cloud_ver)

    def _do_apply(self, target_dir: str, temp_exe: str, restart: bool = True):
        log_update_debug(f"[USER_ACTION] User selected immediate apply (restart={restart})")
        apply_update_now(target_dir, temp_exe, restart=restart)
        self.destroy()
        if self.parent:
            self.parent.destroy()
        sys.exit(0)

    def _do_defer(self, target_dir: str, temp_exe: str, cloud_ver: str):
        log_update_debug(f"[USER_ACTION] User deferred update to app exit. cloud_ver={cloud_ver}")
        set_pending_update(target_dir, temp_exe, cloud_ver)
        if hasattr(self.parent, "on_update_deferred"):
            self.parent.on_update_deferred(cloud_ver)
        parent_target = self.parent if (self.parent and self.parent.winfo_exists()) else self
        messagebox.showinfo(
            "已排程更新",
            f"已記錄更新！\n\n軟體將在您關閉主程式後再自動執行置換。\n您現在可以繼續正常使用！",
            parent=parent_target
        )
        self.destroy()

    def _on_download_failed(self, err: str):
        log_update_debug(f"[UPDATE] Download failed: {err}")
        self.lbl_download_status.configure(text=f"[!] 下載失敗: {err}", fg="#ff4d4f")
        messagebox.showerror("下載失敗", f"下載更新檔時發生錯誤: {err}\n詳情請檢視 update_debug.log。", parent=self)
        if hasattr(self, 'btn_download'):
            self.btn_download.configure(state=tk.NORMAL, text="重試下載")
        if hasattr(self, 'btn_reinstall'):
            self.btn_reinstall.configure(state=tk.NORMAL, text=f"重新下載/覆蓋版本 ({self.manifest.get('version', '')})")
        if hasattr(self, 'btn_cancel'):
            self.btn_cancel.configure(state=tk.NORMAL)
