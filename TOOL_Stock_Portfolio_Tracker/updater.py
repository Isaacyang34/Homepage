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

APP_VERSION = "V1.0"

# 遠端更新指標端點 (GitHub Raw 與 Firebase RTDB 雙保險)
GITHUB_MANIFEST_URL = "https://raw.githubusercontent.com/Isaacyang34/Homepage/gh-pages/TOOL_Stock_Portfolio_Tracker/version.json"
FIREBASE_MANIFEST_URL = "https://my-stock-tracker-2a94e-default-rtdb.asia-southeast1.firebasedatabase.app/update/version.json"

def parse_version_tuple(ver_str: str) -> Tuple[int, ...]:
    """解析版本字串例如 'V1.0' -> (1, 0)"""
    try:
        clean = ver_str.strip().lstrip('vV')
        parts = [int(p) for p in clean.split('.') if p.isdigit()]
        return tuple(parts) if parts else (0,)
    except Exception:
        return (0,)

def is_newer_version(cloud_ver: str, local_ver: str) -> bool:
    """判斷雲端版本是否大於本地版本"""
    c_tup = parse_version_tuple(cloud_ver)
    l_tup = parse_version_tuple(local_ver)
    if c_tup != (0,) and l_tup != (0,):
        return c_tup > l_tup
    return cloud_ver.strip().lower() != local_ver.strip().lower()

def fetch_update_manifest(timeout: float = 6.0) -> Tuple[bool, Optional[Dict[str, Any]], str]:
    """
    從遠端取得最新版本清單 (雙通道容錯)
    回傳 (成功與否, manifest內容, 訊息)
    """
    headers = {
        "User-Agent": "Mozilla/5.0 (Windows NT 10.0; Win64; x64) StockPortfolioTracker/1.0",
        "Cache-Control": "no-cache",
        "Pragma": "no-cache"
    }

    # 1. 優先嘗試 GitHub Raw
    try:
        resp = requests.get(GITHUB_MANIFEST_URL, headers=headers, timeout=timeout)
        if resp.status_code == 200:
            data = resp.json()
            if isinstance(data, dict) and "version" in data:
                return True, data, "GitHub"
    except Exception as e:
        print(f"[Updater] GitHub Manifest 檢查失敗: {e}")

    # 2. 備援嘗試 Firebase RTDB
    try:
        resp = requests.get(FIREBASE_MANIFEST_URL, headers=headers, timeout=timeout)
        if resp.status_code == 200:
            data = resp.json()
            if isinstance(data, dict) and "version" in data:
                return True, data, "Firebase"
    except Exception as e:
        print(f"[Updater] Firebase Manifest 檢查失敗: {e}")

    return False, None, "無法連線至雲端版本伺服器"

class UpdateDialog(tk.Toplevel):
    """線上軟體更新互動精靈視窗"""
    def __init__(self, parent, manifest: Dict[str, Any], is_newer: bool):
        super().__init__(parent)
        self.title("線上軟體更新")
        self.geometry("560x440")
        self.resizable(False, False)
        self.configure(bg="#22222a")
        self.transient(parent)
        self.grab_set()

        self.parent = parent
        self.manifest = manifest
        self.is_newer = is_newer
        self.is_downloading = False

        self.build_ui()

    def build_ui(self):
        cloud_ver = self.manifest.get("version", "未知")
        rel_date = self.manifest.get("release_date", "近期")
        changelog = self.manifest.get("changelog", "無詳細說明")

        # 頂部狀態橫幅
        top_bar = tk.Frame(self, bg="#2a2a36", padx=16, pady=12)
        top_bar.pack(fill=tk.X)

        if self.is_newer:
            status_title = f"[!] 發現新版本: {cloud_ver}"
            status_color = "#f59e0b"
            status_desc = f"本機版本為 {APP_VERSION}，建議立即升級以獲得最新功能與修復。"
        else:
            status_title = f"[OK] 目前已是最新版本: {APP_VERSION}"
            status_color = "#52c41a"
            status_desc = "您目前運行的已是最新釋出版本，功能皆為最新狀態。"

        tk.Label(top_bar, text=status_title, bg="#2a2a36", fg=status_color, font=("Microsoft JhengHei UI", 13, "bold")).pack(anchor="w")
        tk.Label(top_bar, text=status_desc, bg="#2a2a36", fg="#d0d0d8", font=("Microsoft JhengHei UI", 9)).pack(anchor="w", pady=(3, 0))

        # 中間資訊區
        info_frame = tk.Frame(self, bg="#22222a", padx=18, pady=12)
        info_frame.pack(fill=tk.BOTH, expand=True)

        meta_box = tk.Frame(info_frame, bg="#1a1a22", padx=12, pady=8, relief="solid", bd=1)
        meta_box.pack(fill=tk.X, pady=(0, 8))

        tk.Label(meta_box, text=f"本機目前版本:  {APP_VERSION}", bg="#1a1a22", fg="#ffffff", font=("Microsoft JhengHei UI", 9)).grid(row=0, column=0, sticky="w", pady=2)
        tk.Label(meta_box, text=f"雲端最新版本:  {cloud_ver} (發布日: {rel_date})", bg="#1a1a22", fg="#87d068", font=("Microsoft JhengHei UI", 9, "bold")).grid(row=1, column=0, sticky="w", pady=2)

        tk.Label(info_frame, text="更新內容與改版重點:", bg="#22222a", fg="#a0a0b0", font=("Microsoft JhengHei UI", 9, "bold")).pack(anchor="w", pady=(4, 2))

        txt_frame = tk.Frame(info_frame, bg="#181820", relief="solid", bd=1)
        txt_frame.pack(fill=tk.BOTH, expand=True)

        self.txt_changelog = tk.Text(txt_frame, bg="#181820", fg="#f0f0f0", font=("Microsoft JhengHei UI", 9), relief="flat", wrap=tk.WORD, bd=0)
        sb = tk.Scrollbar(txt_frame, orient="vertical", command=self.txt_changelog.yview)
        self.txt_changelog.configure(yscrollcommand=sb.set)
        self.txt_changelog.pack(side=tk.LEFT, fill=tk.BOTH, expand=True, padx=6, pady=6)
        sb.pack(side=tk.RIGHT, fill=tk.Y)

        self.txt_changelog.insert("1.0", changelog)
        self.txt_changelog.configure(state=tk.DISABLED)

        # 下載進度條與狀態文字 (下載時顯示)
        self.progress_frame = tk.Frame(self, bg="#22222a", padx=18)
        self.progress_frame.pack(fill=tk.X, pady=(0, 4))

        self.pbar = ttk.Progressbar(self.progress_frame, orient="horizontal", mode="determinate")
        self.lbl_download_status = tk.Label(self.progress_frame, text="", bg="#22222a", fg="#3a86ff", font=("Microsoft JhengHei UI", 9))

        # 底部按鈕列
        bot = tk.Frame(self, bg="#22222a", padx=16, pady=10)
        bot.pack(fill=tk.X, side=tk.BOTTOM)

        btn_action_text = "立即下載並升級" if self.is_newer else "重新下載最新版覆蓋"
        self.btn_download = tk.Button(bot, text=btn_action_text, bg="#3a86ff", fg="#ffffff", font=("Microsoft JhengHei UI", 10, "bold"), relief="flat", command=self.start_download)
        self.btn_download.pack(side=tk.RIGHT, ipadx=10, ipady=4)

        self.btn_cancel = tk.Button(bot, text="稍後再說", bg="#3a3a46", fg="#ffffff", font=("Microsoft JhengHei UI", 9), relief="flat", command=self.destroy)
        self.btn_cancel.pack(side=tk.RIGHT, padx=8, ipadx=8, ipady=4)

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
