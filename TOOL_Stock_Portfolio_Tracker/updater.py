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

APP_VERSION = "V1.0.2"

def get_resource_path(relative_path: str) -> str:
    """取得靜態資源路徑，相容開發環境與 PyInstaller 打包 (_MEIPASS)"""
    if hasattr(sys, '_MEIPASS'):
        base_path = sys._MEIPASS
    else:
        base_path = os.path.dirname(os.path.abspath(__file__))
    return os.path.join(base_path, relative_path)

# 遠端更新指標端點 (GitHub Raw 與 Firebase RTDB 雙保險)
GITHUB_MANIFEST_URL = "https://raw.githubusercontent.com/Isaacyang34/Homepage/gh-pages/TOOL_Stock_Portfolio_Tracker/version.json"
FIREBASE_MANIFEST_URL = "https://my-stock-tracker-2a94e-default-rtdb.asia-southeast1.firebasedatabase.app/update/version.json"

def parse_version_tuple(ver_str: str) -> Tuple[int, ...]:
    """解析版本字串例如 'V1.0' -> (1, 0, 0), 'V1.0.1' -> (1, 0, 1)"""
    try:
        clean = ver_str.strip().lstrip('vV')
        parts = [int(p) for p in clean.split('.') if p.isdigit()]
        while len(parts) < 3:
            parts.append(0)
        return tuple(parts)
    except Exception:
        return (0, 0, 0)

def is_newer_version(cloud_ver: str, local_ver: str) -> bool:
    """判斷雲端版本是否大於本地版本"""
    c_tup = parse_version_tuple(cloud_ver)
    l_tup = parse_version_tuple(local_ver)
    if c_tup != (0, 0, 0) and l_tup != (0, 0, 0):
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
        self.grab_set()

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

    def start_download(self):
        download_url = self.manifest.get("download_url")
        if not download_url:
            messagebox.showerror("錯誤", "更新檔案下載網址為空，無法執行更新！", parent=self)
            return

        self.is_downloading = True
        self.btn_download.configure(state=tk.DISABLED, text="下載升級中...")
        self.btn_cancel.configure(state=tk.DISABLED)

        self.pbar.pack(fill=tk.X, pady=(0, 4))
        self.lbl_download_status.pack(anchor="w")
        self.lbl_download_status.configure(text="正在連線至下載伺服器...")
        self.pbar["value"] = 0

        threading.Thread(target=self._download_worker, args=(download_url,), daemon=True).start()

    def _download_worker(self, url: str):
        target_dir = os.path.dirname(os.path.abspath(sys.argv[0]))
        temp_exe = os.path.join(target_dir, "Stock_Portfolio_Tracker_update.exe")

        try:
            resp = requests.get(url, stream=True, timeout=30)
            if resp.status_code != 200:
                raise RuntimeError(f"伺服器回應代碼: {resp.status_code}")

            total_size = int(resp.headers.get("content-length", 0))
            downloaded = 0

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
            if os.path.getsize(temp_exe) < 5 * 1024 * 1024:
                raise RuntimeError("下載檔案大小異常，可能下載失敗或檔案不完整！")

            self.after(0, lambda: self._on_download_success(target_dir, temp_exe))
        except Exception as e:
            self.after(0, lambda err=str(e): self._on_download_failed(err))

    def _update_pbar(self, val: int, msg: str):
        self.pbar["value"] = val
        self.lbl_download_status.configure(text=msg)

    def _on_download_success(self, target_dir: str, temp_exe: str):
        self.lbl_download_status.configure(text="✔ 下載完成！正在啟動自替換更新批次檔...", fg="#52c41a")
        
        # 產生純 ASCII Windows 更新批次檔
        bat_path = os.path.join(target_dir, "apply_update.bat")
        target_exe_name = "Stock_Portfolio_Tracker.exe"
        temp_exe_name = os.path.basename(temp_exe)

        bat_content = f"""@echo off
REM Stock Portfolio Tracker Auto Update Script
timeout /t 1 /nobreak > nul
:RETRY
copy /y "{temp_exe_name}" "{target_exe_name}" > nul
if errorlevel 1 (
    timeout /t 1 /nobreak > nul
    goto RETRY
)
del /f /q "{temp_exe_name}" > nul
start "" "{target_exe_name}"
del /f /q "%~f0" > nul
exit
"""
        try:
            with open(bat_path, "w", encoding="ascii") as f:
                f.write(bat_content)

            # 啟動批次檔並結束目前主程式
            subprocess.Popen(["cmd.exe", "/c", bat_path], cwd=target_dir, creationflags=subprocess.CREATE_NO_WINDOW if os.name == 'nt' else 0)
            self.destroy()
            if self.parent:
                self.parent.destroy()
            sys.exit(0)
        except Exception as e:
            messagebox.showerror("更新失敗", f"啟動更新替換腳本失敗: {e}", parent=self)
            self.btn_cancel.configure(state=tk.NORMAL)

    def _on_download_failed(self, err: str):
        self.lbl_download_status.configure(text=f"[!] 下載失敗: {err}", fg="#ff4d4f")
        messagebox.showerror("下載失敗", f"下載更新檔時發生錯誤: {err}\n請檢查網路連線或稍後再試。", parent=self)
        self.btn_download.configure(state=tk.NORMAL, text="重試下載")
        self.btn_cancel.configure(state=tk.NORMAL)
