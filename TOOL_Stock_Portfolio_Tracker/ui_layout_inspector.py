# -*- coding: utf-8 -*-
"""
Stock Portfolio Tracker - 自動化 UI 排版防裁切健康檢查器 (ui_layout_inspector.py)
專門於測試與發布流水線中自動掃描所有視窗幾何邊界，
嚴格防範視窗尺寸不足、DPI 縮放擠壓或元件被邊界裁切 (Clipping & Overflow)！
"""

import sys
import os
import tkinter as tk
from typing import List, Tuple, Dict, Any

# 確保主控台 UTF-8 編碼避免 Windows 亂碼
if hasattr(sys.stdout, "reconfigure"):
    sys.stdout.reconfigure(encoding="utf-8")
if hasattr(sys.stderr, "reconfigure"):
    sys.stderr.reconfigure(encoding="utf-8")

app_dir = os.path.dirname(os.path.abspath(__file__))
if app_dir not in sys.path:
    sys.path.insert(0, app_dir)

import database
from login_gui import LoginDialog, UserManageDialog, EditUserDialog

def inspect_dialog_clipping(dialog_name: str, win: tk.Toplevel) -> Tuple[bool, List[str]]:
    """
    深度掃描 Toplevel 視窗及其內部所有元件：
    1. 驗證視窗目前高度與寬度是否大於等於內部元件自然所需之幾何尺寸 (reqheight/reqwidth)
    2. 深度遍歷所有葉節點 (Button, Entry, Label 等)，驗證其底部邊界是否超越視窗可視區域
    """
    issues = []
    win.update_idletasks()
    
    actual_w = win.winfo_width()
    actual_h = win.winfo_height()
    req_w = win.winfo_reqwidth()
    req_h = win.winfo_reqheight()
    
    # 1. 視窗整體幾何充足性檢驗
    if actual_h < req_h:
        issues.append(
            f"[{dialog_name}] 視窗高度不足！實際分配高度={actual_h}px < 元件所需高度={req_h}px (裁切短少 {req_h - actual_h}px)"
        )
    if actual_w < req_w:
        issues.append(
            f"[{dialog_name}] 視窗寬度不足！實際分配寬度={actual_w}px < 元件所需寬度={req_w}px (裁切短少 {req_w - actual_w}px)"
        )
        
    # 2. 深度遍歷所有子元件之物理邊界
    win_root_y = win.winfo_rooty()
    win_root_x = win.winfo_rootx()
    
    def check_widget_bounds(w: tk.Widget):
        if not w.winfo_viewable():
            return
            
        w_y = w.winfo_rooty()
        w_h = w.winfo_height()
        w_bottom = (w_y - win_root_y) + w_h
        
        # 若元件底端超出視窗邊界超過 2px
        if w_bottom > (actual_h + 2):
            w_class = w.winfo_class()
            w_text = ""
            try:
                w_text = w.cget("text")
            except Exception:
                pass
            desc = f"{w_class}('{w_text}')" if w_text else w_class
            issues.append(
                f"[{dialog_name}] 元件 {desc} 溢出裁切！底端坐標={w_bottom}px > 視窗高度={actual_h}px (截斷 {w_bottom - actual_h}px)"
            )
            
        for child in w.winfo_children():
            check_widget_bounds(child)
            
    for child in win.winfo_children():
        check_widget_bounds(child)
        
    return len(issues) == 0, issues


def run_full_ui_layout_inspection() -> bool:
    """執行全系統所有對話框視窗之自動防裁切掃描"""
    database.init_db()
    
    root = tk.Tk()
    root.withdraw()
    
    all_passed = True
    total_dialogs = 0
    all_issues = []
    
    print("=================================================================")
    print(" [UI-INSPECTOR] 開始執行全系統 GUI 排版防裁切健康檢查")
    print("=================================================================")
    
    # 1. 檢驗 LoginDialog
    total_dialogs += 1
    dlg_login = LoginDialog(root, allow_cancel=True)
    ok, errs = inspect_dialog_clipping("LoginDialog (使用者登入對話框)", dlg_login)
    if not ok:
        all_passed = False
        all_issues.extend(errs)
    dlg_login._on_close()
    root.update()
    
    # 2. 檢驗 UserManageDialog (本次截圖問題核心)
    total_dialogs += 1
    dlg_manage = UserManageDialog(root)
    ok, errs = inspect_dialog_clipping("UserManageDialog (帳號管理中心)", dlg_manage)
    if not ok:
        all_passed = False
        all_issues.extend(errs)
    dlg_manage._on_close()
    root.update()
    
    # 3. 檢驗 EditUserDialog
    total_dialogs += 1
    sample_user = {"id": 1, "username": "test", "display_name": "測試人員", "has_password": False}
    dlg_edit = EditUserDialog(root, sample_user)
    ok, errs = inspect_dialog_clipping("EditUserDialog (編輯使用者帳號)", dlg_edit)
    if not ok:
        all_passed = False
        all_issues.extend(errs)
    dlg_edit._on_close()
    root.update()
    
    root.destroy()
    
    if all_passed:
        print(f" [PASS] 成功檢查 {total_dialogs} 個核心 GUI 對話框：全數 100% 通過無任何裁切或溢位！")
        print("=================================================================\n")
        return True
    else:
        print(f" [FAIL] 警告！發現 {len(all_issues)} 處排版裁切與溢位瑕疵：")
        for err in all_issues:
            print(f"  - {err}")
        print("=================================================================\n")
        return False


if __name__ == "__main__":
    success = run_full_ui_layout_inspection()
    sys.exit(0 if success else 1)
