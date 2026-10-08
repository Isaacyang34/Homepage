# -*- coding: utf-8 -*-
"""
Stock Portfolio Tracker - 開機完整性、自癒與系統修復精靈 (integrity_checker.py)
負責在軟體啟動時驗證所有關鍵模組與設定檔是否存在且健全。
三重防護自癒體系：
1. 第一重：從 EXE 內部 (_MEIPASS) 自動釋放缺失核心檔案 (零感知自癒)
2. 第二重：從本機 versions/ 備份目錄自動還原
3. 第三重：互動式系統修復精靈 (提供一鍵線上自動下載修復、直接前往 GitHub Releases 頁面、複製下載連結)
"""
import os
import sys
import shutil
import json
import threading
import webbrowser
import tkinter as tk
from tkinter import ttk, messagebox
from typing import List, Tuple, Optional

GITHUB_RELEASES_URL = "https://github.com/Isaacyang34/Homepage/releases"
GITHUB_RAW_BASE = "https://raw.githubusercontent.com/Isaacyang34/Homepage/gh-pages/TOOL_Stock_Portfolio_Tracker"

def get_app_dir() -> str:
    """取得應用程式根目錄"""
    if getattr(sys, 'frozen', False):
        return os.path.dirname(os.path.abspath(sys.executable))
    return os.path.dirname(os.path.abspath(__file__))

# 必要的核心模組與資源清單 (相對路徑)
CRITICAL_COMPONENTS = [
    "app_core.pkg",
    "version.json"
]

def find_latest_local_backup(app_dir: str) -> Optional[Tuple[str, str]]:
    """在 versions/ 目錄尋找最新的本機版本備份"""
    versions_dir = os.path.join(app_dir, "versions")
    if not os.path.exists(versions_dir):
        return None
    
    candidates = []
    for item in os.listdir(versions_dir):
        full_p = os.path.join(versions_dir, item)
        if os.path.isdir(full_p):
            candidates.append((item, full_p))
    
    if not candidates:
        return None
    candidates.sort(key=lambda x: x[0], reverse=True)
    return candidates[0]

class SystemRepairDialog(tk.Toplevel):
    """系統修復精靈互動視窗"""
    def __init__(self, missing_files: List[str], app_dir: str):
        super().__init__()
        self.title("Stock Portfolio Tracker - 系統修復與組件下載精靈")
        self.geometry("620x520")
        self.minsize(560, 460)
        self.configure(bg="#1a1a24")
        self.attributes("-topmost", True)
        
        self.missing_files = missing_files
        self.app_dir = app_dir
        self.repair_success = False
        self.is_repairing = False

        self.protocol("WM_DELETE_WINDOW", self._on_close)
        self.build_ui()

    def build_ui(self):
        # 1. 頂部警示橫幅
        top = tk.Frame(self, bg="#2d1b22", padx=20, pady=14)
        top.pack(fill=tk.X)

        tk.Label(
            top, text="⚠️ 系統偵測到必要組件缺失", bg="#2d1b22", fg="#f87171",
            font=("Microsoft JhengHei UI", 13, "bold")
        ).pack(anchor="w")

        tk.Label(
            top, text="軟體需要核心二進位模組包才能正常運作。您可選擇自動線上修復或前往 GitHub 下載。",
            bg="#2d1b22", fg="#cbd5e1", font=("Microsoft JhengHei UI", 9)
        ).pack(anchor="w", pady=(3, 0))

        # 2. 中間內容區
        mid = tk.Frame(self, bg="#1a1a24", padx=20, pady=12)
        mid.pack(fill=tk.BOTH, expand=True)

        tk.Label(
            mid, text="缺失或受損的必要檔案清單：", bg="#1a1a24", fg="#94a3b8",
            font=("Microsoft JhengHei UI", 9, "bold")
        ).pack(anchor="w")

        # 缺失檔案清單框
        list_box = tk.Frame(mid, bg="#14141c", relief="solid", bd=1, padx=10, pady=8)
        list_box.pack(fill=tk.X, pady=(6, 12))

        for f in self.missing_files:
            tk.Label(
                list_box, text=f" •  {f}", bg="#14141c", fg="#ef4444",
                font=("Consolas", 10, "bold")
            ).pack(anchor="w", pady=2)

        # 下載進度條 (預設隱藏)
        self.progress_frame = tk.Frame(mid, bg="#1a1a24")
        self.pbar = ttk.Progressbar(self.progress_frame, orient="horizontal", mode="indeterminate")
        self.lbl_pbar_status = tk.Label(self.progress_frame, text="", bg="#1a1a24", fg="#38bdf8", font=("Microsoft JhengHei UI", 9))

        # 說明區
        tip_frame = tk.Frame(mid, bg="#20202c", padx=12, pady=10, relief="solid", bd=1)
        tip_frame.pack(fill=tk.X, pady=(0, 10))
        
        tk.Label(
            tip_frame, text="💡 官方下載與修復選項說明：",
            bg="#20202c", fg="#38bdf8", font=("Microsoft JhengHei UI", 9, "bold")
        ).pack(anchor="w")
        tk.Label(
            tip_frame,
            text="• 【⚡ 立即線上自動修復】：由程式直接從 GitHub 雲端自動下載缺失檔案並寫入，修復後立即開機。\n"
                 "• 【🌐 前往 GitHub Releases 頁面】：開啟瀏覽器下載官方完整安裝包 (Full Package) 覆蓋修復。\n"
                 "• 【📋 複製下載連結】：複製 GitHub Releases 官方網址至剪貼簿，便於在其他電腦下載。",
            bg="#20202c", fg="#cbd5e1", font=("Microsoft JhengHei UI", 8), justify=tk.LEFT
        ).pack(anchor="w", pady=(4, 0))

        # 3. 底部動作按鈕列
        bot = tk.Frame(self, bg="#14141c", padx=20, pady=12)
        bot.pack(side=tk.BOTTOM, fill=tk.X)

        self.btn_exit = tk.Button(
            bot, text="結束程式", bg="#334155", fg="#ffffff",
            activebackground="#475569", activeforeground="#ffffff",
            font=("Microsoft JhengHei UI", 9), relief="flat", cursor="hand2",
            command=self._on_close
        )
        self.btn_exit.pack(side=tk.RIGHT, padx=4, ipadx=10, ipady=4)

        self.btn_copy_url = tk.Button(
            bot, text="📋 複製下載連結", bg="#1e293b", fg="#94a3b8",
            activebackground="#334155", activeforeground="#ffffff",
            font=("Microsoft JhengHei UI", 9), relief="flat", cursor="hand2",
            command=self._copy_download_url
        )
        self.btn_copy_url.pack(side=tk.RIGHT, padx=4, ipadx=8, ipady=4)

        self.btn_open_web = tk.Button(
            bot, text="🌐 前往 GitHub 下載", bg="#0284c7", fg="#ffffff",
            activebackground="#0369a1", activeforeground="#ffffff",
            font=("Microsoft JhengHei UI", 9, "bold"), relief="flat", cursor="hand2",
            command=self._open_github_releases
        )
        self.btn_open_web.pack(side=tk.RIGHT, padx=4, ipadx=10, ipady=4)

        self.btn_auto_repair = tk.Button(
            bot, text="⚡ 立即線上自動修復", bg="#16a34a", fg="#ffffff",
            activebackground="#15803d", activeforeground="#ffffff",
            font=("Microsoft JhengHei UI", 9, "bold"), relief="flat", cursor="hand2",
            command=self._start_online_repair
        )
        self.btn_auto_repair.pack(side=tk.LEFT, padx=4, ipadx=12, ipady=4)

    def _start_online_repair(self):
        """啟動線上自動下載修復任務"""
        if self.is_repairing:
            return
        self.is_repairing = True
        self.btn_auto_repair.configure(state=tk.DISABLED, text="修復中...")
        self.btn_open_web.configure(state=tk.DISABLED)
        self.btn_copy_url.configure(state=tk.DISABLED)
        self.btn_exit.configure(state=tk.DISABLED)

        self.progress_frame.pack(fill=tk.X, pady=(0, 10))
        self.pbar.pack(fill=tk.X, pady=(0, 4))
        self.pbar.start(10)
        self.lbl_pbar_status.pack(anchor="w")
        self.lbl_pbar_status.configure(text="正在連線至 GitHub 官方伺服器下載缺失檔案...")

        threading.Thread(target=self._worker_repair, daemon=True).start()

    def _worker_repair(self):
        import requests
        success_files = 0
        failed_files = []

        for rel_f in self.missing_files:
            clean_f = rel_f.split(" ")[0]
            url = f"{GITHUB_RAW_BASE}/{clean_f.replace(os.sep, '/')}"
            target_path = os.path.join(self.app_dir, clean_f)
            
            self._update_status_safe(f"正在下載: {clean_f} ...")
            try:
                resp = requests.get(url, timeout=10.0)
                if resp.status_code == 200 and len(resp.content) > 0:
                    os.makedirs(os.path.dirname(target_path), exist_ok=True)
                    with open(target_path, "wb") as f_out:
                        f_out.write(resp.content)
                    success_files += 1
                else:
                    failed_files.append(f"{clean_f} (HTTP {resp.status_code})")
            except Exception as e:
                failed_files.append(f"{clean_f} ({e})")

        self.after(0, lambda: self._on_repair_completed(success_files, failed_files))

    def _update_status_safe(self, text: str):
        self.after(0, lambda: self.lbl_pbar_status.configure(text=text))

    def _on_repair_completed(self, success_count: int, failed_files: List[str]):
        self.pbar.stop()
        self.is_repairing = False
        self.btn_auto_repair.configure(state=tk.NORMAL, text="⚡ 立即線上自動修復")
        self.btn_open_web.configure(state=tk.NORMAL)
        self.btn_copy_url.configure(state=tk.NORMAL)
        self.btn_exit.configure(state=tk.NORMAL)

        if not failed_files and success_count > 0:
            self.repair_success = True
            messagebox.showinfo(
                "自動修復成功",
                f"🎉 成功下載並修復了 {success_count} 個必要組件！\n\n系統即將正常啟動。",
                parent=self
            )
            self.destroy()
        else:
            err_msg = "\n".join([f" • {f}" for f in failed_files])
            messagebox.showerror(
                "部分檔案下載失敗",
                f"線上修復未能成功取得下列檔案：\n\n{err_msg}\n\n請點擊【🌐 前往 GitHub 下載】手動下載完整包覆蓋。",
                parent=self
            )

    def _open_github_releases(self):
        """開啟官方 GitHub Releases 頁面"""
        try:
            webbrowser.open(GITHUB_RELEASES_URL)
        except Exception as e:
            messagebox.showerror("開啟失敗", f"無法啟動瀏覽器: {e}\n請手動造訪:\n{GITHUB_RELEASES_URL}", parent=self)

    def _copy_download_url(self):
        """複製下載連結至剪貼簿"""
        self.clipboard_clear()
        self.clipboard_append(GITHUB_RELEASES_URL)
        messagebox.showinfo("複製成功", f"已複製官方 GitHub Releases 下載網址至剪貼簿！\n\n{GITHUB_RELEASES_URL}", parent=self)

    def _on_close(self):
        self.destroy()

def check_and_heal_system_integrity() -> bool:
    """
    執行完整性檢查與多重自癒程序。
    若系統健全或修復成功回傳 True，否則回傳 False。
    """
    app_dir = get_app_dir()
    mei_dir = getattr(sys, '_MEIPASS', '')
    missing_files: List[str] = []

    # 1. 第一重防護：檢查核心組件，若缺失優先從 _MEIPASS (EXE 內建打包資源) 自動解壓提取
    for rel_path in CRITICAL_COMPONENTS:
        target = os.path.join(app_dir, rel_path)
        if not os.path.exists(target) or os.path.getsize(target) == 0:
            # 嘗試從 _MEIPASS 提取
            if mei_dir and os.path.exists(mei_dir):
                internal_src = os.path.join(mei_dir, rel_path)
                if not os.path.exists(internal_src):
                    internal_src = os.path.join(mei_dir, os.path.basename(rel_path))
                if os.path.exists(internal_src) and os.path.getsize(internal_src) > 0:
                    try:
                        os.makedirs(os.path.dirname(target), exist_ok=True)
                        shutil.copy2(internal_src, target)
                        continue  # 成功從內部自解壓自癒！
                    except Exception:
                        pass
            
            # 若 modules/ 或根目錄有明碼備用檔
            alt_name = os.path.basename(rel_path)
            alt_target = os.path.join(app_dir, alt_name)
            if os.path.exists(alt_target) and os.path.getsize(alt_target) > 0:
                try:
                    os.makedirs(os.path.dirname(target), exist_ok=True)
                    shutil.copy2(alt_target, target)
                    continue
                except Exception:
                    pass

            missing_files.append(rel_path)

    # 若所有核心檔案健全，檢查通過
    if not missing_files:
        return True

    # 2. 第二重防護：嘗試從本機 versions/ 備份目錄自癒
    backup_info = find_latest_local_backup(app_dir)
    if backup_info:
        ver_name, backup_path = backup_info
        repaired_count = 0
        for rel_path in missing_files:
            src = os.path.join(backup_path, rel_path)
            if not os.path.exists(src):
                src = os.path.join(backup_path, os.path.basename(rel_path))
            dst = os.path.join(app_dir, rel_path)
            if os.path.exists(src) and os.path.getsize(src) > 0:
                try:
                    os.makedirs(os.path.dirname(dst), exist_ok=True)
                    shutil.copy2(src, dst)
                    repaired_count += 1
                except Exception:
                    pass
        if repaired_count == len(missing_files):
            return True

    # 3. 第三重防護：彈出高質感系統修復與下載精靈視窗 (SystemRepairDialog)
    root = tk.Tk()
    root.withdraw()

    dlg = SystemRepairDialog(missing_files, app_dir)
    root.wait_window(dlg)
    success = dlg.repair_success
    root.destroy()

    return success
