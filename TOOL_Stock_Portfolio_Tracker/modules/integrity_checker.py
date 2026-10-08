# -*- coding: utf-8 -*-
"""
Stock Portfolio Tracker - 開機完整性與自癒檢查模組
負責在軟體啟動時驗證所有關鍵模組、靜態資源與設定檔是否存在且健全。
若發現異常，主動從本機 versions/ 備份目錄自動嘗試還原修復。
"""
import os
import sys
import shutil
import json
import tkinter as tk
from tkinter import messagebox
from typing import List, Tuple, Optional

def get_app_dir() -> str:
    """取得應用程式根目錄"""
    if getattr(sys, 'frozen', False):
        return os.path.dirname(os.path.abspath(sys.executable))
    return os.path.dirname(os.path.abspath(__file__))

# 必要的核心模組與資源清單 (相對路徑)
CRITICAL_COMPONENTS = [
    os.path.join("modules", "main_gui.py"),
    os.path.join("modules", "updater.py"),
    os.path.join("modules", "database.py"),
    os.path.join("modules", "quote_service.py"),
    os.path.join("modules", "pnl_calculator.py"),
    os.path.join("modules", "dividend_service.py"),
    os.path.join("modules", "history_service.py"),
    os.path.join("modules", "splash_screen.py"),
    os.path.join("version.json")
]

CRITICAL_ASSETS = [
    os.path.join("assets", "app_icon.ico"),
    os.path.join("assets", "app_icon.png")
]

def find_latest_local_backup(app_dir: str) -> Optional[Tuple[str, str]]:
    """
    在 versions/ 目錄尋找最新的本機版本備份
    回傳 (版本名稱, 備份路徑) 或 None
    """
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
    # 依資料夾名稱排序 (最新版通常排在最後)
    candidates.sort(key=lambda x: x[0], reverse=True)
    return candidates[0]

def check_and_heal_system_integrity() -> bool:
    """
    執行完整性檢查與自癒程序。
    若系統健全或修復成功回傳 True，否則回傳 False。
    """
    app_dir = get_app_dir()
    missing_files: List[str] = []

    # 1. 檢查核心模組
    for rel_path in CRITICAL_COMPONENTS:
        target = os.path.join(app_dir, rel_path)
        # 容錯：若在 modules 找不到，檢查根目錄是否有降級備用檔
        if not os.path.exists(target):
            alt_name = os.path.basename(rel_path)
            alt_target = os.path.join(app_dir, alt_name)
            if os.path.exists(alt_target):
                # 自動自癒：從根目錄同步修復到 modules/
                try:
                    os.makedirs(os.path.dirname(target), exist_ok=True)
                    shutil.copy2(alt_target, target)
                    continue
                except Exception:
                    pass
            missing_files.append(rel_path)
        elif os.path.getsize(target) == 0:
            missing_files.append(f"{rel_path} (檔案損壞空白)")

    # 2. 檢查資產檔案 (若 assets/ 缺失，檢查根目錄)
    for rel_path in CRITICAL_ASSETS:
        target = os.path.join(app_dir, rel_path)
        if not os.path.exists(target):
            alt_name = os.path.basename(rel_path)
            alt_target = os.path.join(app_dir, alt_name)
            if os.path.exists(alt_target):
                try:
                    os.makedirs(os.path.dirname(target), exist_ok=True)
                    shutil.copy2(alt_target, target)
                    continue
                except Exception:
                    pass
            # 圖標若完全沒有不阻礙啟動，但記錄警示
            pass

    # 3. 若所有核心檔案皆存在，檢查通過
    if not missing_files:
        return True

    # 4. 發現缺失，嘗試自癒
    root = tk.Tk()
    root.withdraw()
    root.attributes("-topmost", True)

    backup_info = find_latest_local_backup(app_dir)
    missing_str = "\n".join([f" • {f}" for f in missing_files])

    if backup_info:
        ver_name, backup_path = backup_info
        ans = messagebox.askyesno(
            "系統開機完整性警告與自癒機制",
            f"系統偵測到下列必要組件缺失或損壞：\n\n{missing_str}\n\n"
            f"檢測到本機歷史備份資料夾：【{ver_name}】\n\n"
            f"是否立即啟動「自動修復程序」，從本機備份還原缺失檔案？",
            parent=root
        )
        if ans:
            repaired_count = 0
            for rel_path in missing_files:
                pure_rel = rel_path.split(" ")[0]
                src = os.path.join(backup_path, pure_rel)
                if not os.path.exists(src):
                    src = os.path.join(backup_path, os.path.basename(pure_rel))
                dst = os.path.join(app_dir, pure_rel)
                if os.path.exists(src):
                    try:
                        os.makedirs(os.path.dirname(dst), exist_ok=True)
                        shutil.copy2(src, dst)
                        repaired_count += 1
                    except Exception:
                        pass
            
            if repaired_count > 0:
                messagebox.showinfo(
                    "自癒修復完成",
                    f"成功從備份【{ver_name}】還原了 {repaired_count} 個必要組件！\n軟體即將正常啟動。",
                    parent=root
                )
                root.destroy()
                return True
            else:
                messagebox.showerror(
                    "修復失敗",
                    f"備份中亦缺少該檔案，請至 GitHub 下載完整修復包 (Full Package)。",
                    parent=root
                )
                root.destroy()
                return False
        else:
            root.destroy()
            return False
    else:
        messagebox.showerror(
            "必要組件缺失",
            f"系統偵測到下列必要組件缺失：\n\n{missing_str}\n\n"
            f"本機無可用歷史備份。\n請前往 GitHub 下載完整便攜安裝包 (Full Package) 覆蓋修復。",
            parent=root
        )
        root.destroy()
        return False
