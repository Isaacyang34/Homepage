# -*- coding: utf-8 -*-
"""
Stock Portfolio Tracker - 多使用者登入與帳號管理模組 (login_gui.py)
提供：
1. LoginDialog: 登入視窗，支援帳號切換、密碼驗證 (SHA-256+Salt) 與免密碼快速進入
2. UserManageDialog: 帳號管理中心，支援新增帳號、修改顯示名稱、變更/清除密碼、刪除帳戶
3. EditUserDialog: 使用者個人資料與密碼修改小視窗
4. center_toplevel: 具備 DPI 自適應與防裁切機制之視窗居中引擎
"""

import tkinter as tk
from tkinter import ttk, messagebox
from typing import Dict, List, Any, Optional, Callable
import database

# 主題配色 (與 main_gui 一致)
THEME_BG = "#1e1e24"
THEME_CARD_BG = "#2b2b36"
THEME_ENTRY_BG = "#18181f"
THEME_TEXT = "#ffffff"
THEME_TEXT_MUTED = "#94a3b8"
THEME_ACCENT = "#38bdf8"
THEME_SUCCESS = "#22c55e"
THEME_DANGER = "#ef4444"
THEME_BORDER = "#3b3f58"
FONT_FAMILY = "Microsoft JhengHei UI"


def center_toplevel(win: tk.Toplevel, base_width: int, base_height: int, parent: Optional[tk.Widget] = None, resizable: bool = True):
    """
    智慧防裁切視窗置中函式 (Auto-fit & Anti-Clipping Geometry Engine):
    1. 執行 update_idletasks()，讀取內容實際自然渲染所需尺寸 (reqwidth, reqheight)。
    2. 自動取 max(base, req + safety_margin)，動態擴展視窗幾何大小，徹底杜絕 Windows DPI 縮放下的文字截斷。
    3. 自動啟用 minsize 與 resizable，保障使用者可自由拉伸或雙擊最大化。
    4. 相對於可見之父視窗幾何中心居中，否則相對於螢幕中心居中。
    """
    win.update_idletasks()
    req_w = win.winfo_reqwidth()
    req_h = win.winfo_reqheight()

    # 自動適應安全下限與動態擴展，確保 100% 容納內部所有元件
    final_w = max(base_width, req_w + 24)
    final_h = max(base_height, req_h + 24)

    win.minsize(base_width, base_height)
    win.resizable(resizable, resizable)

    if parent:
        try:
            if parent.winfo_viewable():
                px = parent.winfo_rootx()
                py = parent.winfo_rooty()
                pw = parent.winfo_width()
                ph = parent.winfo_height()
                x = max(0, px + (pw - final_w) // 2)
                y = max(0, py + (ph - final_h) // 2)
                win.geometry(f"{final_w}x{final_h}+{x}+{y}")
                return
        except Exception:
            pass

    sw = win.winfo_screenwidth()
    sh = win.winfo_screenheight()
    x = max(0, (sw - final_w) // 2)
    y = max(0, (sh - final_h) // 2)
    win.geometry(f"{final_w}x{final_h}+{x}+{y}")


class LoginDialog(tk.Toplevel):
    """
    登入與帳號切換對話框 (模態置頂，不閃爍，防死鎖，防裁切)
    """
    def __init__(self, parent=None, on_success: Optional[Callable[[Dict[str, Any]], None]] = None, allow_cancel: bool = True):
        super().__init__(parent)
        self.parent = parent
        self.on_success = on_success
        self.allow_cancel = allow_cancel
        self.logged_in_user: Optional[Dict[str, Any]] = None
        self.show_password = False
        
        self.title("使用者登入 - 投資組合管理系統")
        self.configure(bg=THEME_BG)
        
        self.protocol("WM_DELETE_WINDOW", self._on_close)
        
        self._build_ui()
        self._load_users()
        
        # 依父視窗幾何與內容需求動態防裁切置中
        center_toplevel(self, 460, 440, parent, resizable=True)
        
        # 聚焦與鍵盤監聽
        self.bind("<Return>", lambda e: self._do_login())
        self.bind("<Escape>", lambda e: self._on_close())
        
        # 視窗層次與模態設定 (避免 topmost 閃爍掉到底層)
        self.deiconify()
        if parent and parent.winfo_viewable():
            try:
                self.transient(parent)
            except Exception:
                pass
        self.lift()
        self.focus_force()
        try:
            self.grab_set()
        except Exception:
            pass

    def _build_ui(self):
        # 頂部裝飾標題區
        header_frame = tk.Frame(self, bg=THEME_BG)
        header_frame.pack(fill="x", padx=25, pady=(20, 10))
        
        title_lbl = tk.Label(
            header_frame, text="🔒 帳號登入與切換",
            font=(FONT_FAMILY, 15, "bold"),
            fg=THEME_ACCENT, bg=THEME_BG
        )
        title_lbl.pack(anchor="w")
        
        sub_lbl = tk.Label(
            header_frame, text="選擇使用者帳戶，獨立管理個人持股庫存與買入批次",
            font=(FONT_FAMILY, 9),
            fg=THEME_TEXT_MUTED, bg=THEME_BG
        )
        sub_lbl.pack(anchor="w", pady=(3, 0))
        
        # 底部動作按鈕區 (先 pack side="bottom"，確保物理空間優先權)
        btn_box = tk.Frame(self, bg=THEME_BG)
        btn_box.pack(side="bottom", fill="x", padx=25, pady=(5, 20))
        
        # 登入進入按鈕
        self.login_btn = tk.Button(
            btn_box, text="🚀 登入並載入庫存",
            font=(FONT_FAMILY, 11, "bold"),
            bg=THEME_ACCENT, fg="#0f172a",
            activebackground="#7dd3fc", activeforeground="#0f172a",
            relief="flat", cursor="hand2",
            command=self._do_login
        )
        self.login_btn.pack(fill="x", ipady=6, pady=(0, 8))
        
        # 次要操作按鈕列
        sub_row = tk.Frame(btn_box, bg=THEME_BG)
        sub_row.pack(fill="x")
        
        if self.allow_cancel:
            self.manage_btn = tk.Button(
                sub_row, text="⚙️ 帳號管理中心",
                font=(FONT_FAMILY, 9),
                bg=THEME_CARD_BG, fg=THEME_TEXT,
                activebackground=THEME_BORDER, activeforeground=THEME_TEXT,
                relief="flat", cursor="hand2",
                command=self._open_user_manager
            )
            self.manage_btn.pack(side="left", fill="x", expand=True, padx=(0, 4), ipady=3)

            self.cancel_btn = tk.Button(
                sub_row, text="取消",
                font=(FONT_FAMILY, 9),
                bg=THEME_CARD_BG, fg=THEME_TEXT_MUTED,
                activebackground=THEME_BORDER, activeforeground=THEME_TEXT,
                relief="flat", cursor="hand2",
                command=self._on_close
            )
            self.cancel_btn.pack(side="right", fill="x", expand=True, padx=(4, 0), ipady=3)
        else:
            self.exit_btn = tk.Button(
                sub_row, text="✕ 結束系統",
                font=(FONT_FAMILY, 9),
                bg=THEME_CARD_BG, fg=THEME_TEXT_MUTED,
                activebackground=THEME_BORDER, activeforeground=THEME_TEXT,
                relief="flat", cursor="hand2",
                command=self._on_close
            )
            self.exit_btn.pack(fill="x", ipady=3)

        # 中央卡片容器 (填補剩餘中間區域)
        card = tk.Frame(self, bg=THEME_CARD_BG, highlightbackground=THEME_BORDER, highlightthickness=1)
        card.pack(fill="both", expand=True, padx=25, pady=10)
        
        # 1. 選擇使用者
        u_box = tk.Frame(card, bg=THEME_CARD_BG)
        u_box.pack(fill="x", padx=20, pady=(18, 10))
        
        u_lbl = tk.Label(
            u_box, text="帳戶選擇",
            font=(FONT_FAMILY, 10, "bold"),
            fg=THEME_TEXT, bg=THEME_CARD_BG
        )
        u_lbl.pack(anchor="w", pady=(0, 5))
        
        self.user_var = tk.StringVar()
        self.user_combo = ttk.Combobox(
            u_box, textvariable=self.user_var,
            state="readonly", font=(FONT_FAMILY, 10)
        )
        self.user_combo.pack(fill="x", ipady=3)
        self.user_combo.bind("<<ComboboxSelected>>", self._on_user_selected)
        
        # 2. 密碼輸入區
        p_box = tk.Frame(card, bg=THEME_CARD_BG)
        p_box.pack(fill="x", padx=20, pady=(5, 10))
        
        p_header = tk.Frame(p_box, bg=THEME_CARD_BG)
        p_header.pack(fill="x", pady=(0, 5))
        
        p_lbl = tk.Label(
            p_header, text="帳戶密碼",
            font=(FONT_FAMILY, 10, "bold"),
            fg=THEME_TEXT, bg=THEME_CARD_BG
        )
        p_lbl.pack(side="left")
        
        self.pwd_hint_lbl = tk.Label(
            p_header, text="（無密碼帳號可直接進入）",
            font=(FONT_FAMILY, 8),
            fg=THEME_TEXT_MUTED, bg=THEME_CARD_BG
        )
        self.pwd_hint_lbl.pack(side="left", padx=5)
        
        entry_row = tk.Frame(p_box, bg=THEME_ENTRY_BG, highlightbackground=THEME_BORDER, highlightthickness=1)
        entry_row.pack(fill="x")
        
        self.pwd_entry = tk.Entry(
            entry_row, show="●",
            font=(FONT_FAMILY, 10),
            bg=THEME_ENTRY_BG, fg=THEME_TEXT,
            insertbackground=THEME_TEXT,
            relief="flat", borderwidth=0
        )
        self.pwd_entry.pack(side="left", fill="x", expand=True, padx=8, ipady=5)
        
        self.eye_btn = tk.Button(
            entry_row, text="👁",
            font=(FONT_FAMILY, 9),
            bg=THEME_ENTRY_BG, fg=THEME_TEXT_MUTED,
            activebackground=THEME_ENTRY_BG, activeforeground=THEME_ACCENT,
            relief="flat", borderwidth=0, cursor="hand2",
            command=self._toggle_pwd_visibility
        )
        self.eye_btn.pack(side="right", padx=5)
        
        # 狀態與提示訊息
        self.msg_lbl = tk.Label(
            card, text="",
            font=(FONT_FAMILY, 9),
            fg=THEME_TEXT_MUTED, bg=THEME_CARD_BG,
            wraplength=380
        )
        self.msg_lbl.pack(fill="x", padx=20, pady=(5, 5))

    def _toggle_pwd_visibility(self):
        self.show_password = not self.show_password
        if self.show_password:
            self.pwd_entry.configure(show="")
            self.eye_btn.configure(fg=THEME_ACCENT)
        else:
            self.pwd_entry.configure(show="●")
            self.eye_btn.configure(fg=THEME_TEXT_MUTED)

    def _load_users(self):
        self.users = database.get_all_users()
        if not self.users:
            database.init_db()
            self.users = database.get_all_users()
            
        display_names = []
        last_uid = database.get_setting("last_user_id", "1")
        sel_idx = 0
        
        for idx, u in enumerate(self.users):
            p_status = "🔑" if u.get("has_password") else "🔓"
            name = f"{p_status} {u.get('display_name', u.get('username'))} (@{u.get('username')})"
            display_names.append(name)
            if str(u.get("id")) == str(last_uid):
                sel_idx = idx
                
        self.user_combo["values"] = display_names
        if display_names:
            self.user_combo.current(sel_idx)
            self._on_user_selected()

    def _on_user_selected(self, event=None):
        idx = self.user_combo.current()
        if 0 <= idx < len(self.users):
            user = self.users[idx]
            has_pwd = bool(user.get("has_password"))
            self.pwd_entry.delete(0, tk.END)
            if has_pwd:
                self.pwd_hint_lbl.configure(text="（此帳號設有安全密碼，請輸入）", fg=THEME_ACCENT)
                self.msg_lbl.configure(text="🔒 請輸入該帳戶之登入密碼", fg=THEME_TEXT_MUTED)
                self.pwd_entry.focus_set()
            else:
                self.pwd_hint_lbl.configure(text="（免密碼帳戶，可直接進入）", fg=THEME_SUCCESS)
                self.msg_lbl.configure(text="💡 此帳號未設密碼，可直接點擊進入", fg=THEME_SUCCESS)
                self.login_btn.focus_set()

    def _safe_destroy(self):
        """徹底釋放 Grab 並銷毀視窗，杜絕介面無響應死鎖"""
        try:
            self.grab_release()
        except Exception:
            pass
        try:
            self.destroy()
        except Exception:
            pass

    def _destroy_safe(self):
        """別名相容防呆"""
        self._safe_destroy()

    def _do_login(self):
        idx = self.user_combo.current()
        if not (0 <= idx < len(self.users)):
            self.msg_lbl.configure(text="⚠️ 請先選擇欲登入之帳戶", fg=THEME_DANGER)
            return
            
        user = self.users[idx]
        username = user["username"]
        password = self.pwd_entry.get().strip()
        
        ok, verified_user, err_msg = database.verify_user_credentials(username, password)
        if not ok or not verified_user:
            self.msg_lbl.configure(text=f"❌ {err_msg}", fg=THEME_DANGER)
            self.pwd_entry.focus_set()
            self.pwd_entry.select_range(0, tk.END)
            return
            
        # 驗證成功，設定運行期使用者單例並記錄上次登入帳戶
        database.set_current_user(verified_user)
        database.set_setting("last_user_id", str(verified_user["id"]))
        self.logged_in_user = verified_user
        
        self.msg_lbl.configure(text=f"✅ 登入成功！正在載入 {verified_user.get('display_name')} 的投資庫存...", fg=THEME_SUCCESS)
        self.update_idletasks()
        
        if self.on_success:
            try:
                self.on_success(verified_user)
            except Exception as e:
                print(f"[Login] 登入回呼失敗: {e}")
                
        self._safe_destroy()

    def _on_close(self):
        if not self.logged_in_user and not self.allow_cancel:
            # 強制登入模式下若使用者直接關閉視窗，提示是否結束應用程式
            if messagebox.askyesno("結束系統", "尚未完成登入驗證，確定要結束應用程式嗎？", parent=self):
                if self.parent:
                    try:
                        self.parent.destroy()
                    except Exception:
                        pass
                self._safe_destroy()
                import sys
                sys.exit(0)
            return
            
        self._safe_destroy()

    def _open_user_manager(self):
        """開啟帳號管理中心，暫時釋放當前 grab 以免巢狀模態焦點死鎖"""
        try:
            self.grab_release()
        except Exception:
            pass
        UserManageDialog(self, on_users_changed=self._load_users, on_close_callback=self._restore_grab)

    def _restore_grab(self):
        """子管理視窗關閉後恢復自身焦點與 grab"""
        try:
            self.lift()
            self.focus_force()
            self.grab_set()
        except Exception:
            pass


class UserManageDialog(tk.Toplevel):
    """
    使用者帳號管理中心對話框
    支援：新增使用者、編輯顯示名稱、變更/清除密碼、刪除使用者
    採用反裁切優先排版 (Bottom-First Robust Packing)
    """
    def __init__(self, parent, on_users_changed: Optional[Callable[[], None]] = None, on_close_callback: Optional[Callable[[], None]] = None):
        super().__init__(parent)
        self.parent = parent
        self.on_users_changed = on_users_changed
        self.on_close_callback = on_close_callback
        
        self.title("帳號管理中心 - 投資組合管理系統")
        self.configure(bg=THEME_BG)
        
        self.protocol("WM_DELETE_WINDOW", self._on_close)
        self.bind("<Escape>", lambda e: self._on_close())
        
        self._build_ui()
        self._refresh_user_list()
        
        # 基準寬高 620x560，並自適應內部所有元件自然需求尺寸，允許自由縮放
        center_toplevel(self, 620, 560, parent, resizable=True)
        
        self.deiconify()
        if parent and parent.winfo_viewable():
            try:
                self.transient(parent)
            except Exception:
                pass
        self.lift()
        self.focus_force()
        try:
            self.grab_set()
        except Exception:
            pass

    def _safe_destroy(self):
        try:
            self.grab_release()
        except Exception:
            pass
        if self.on_close_callback:
            try:
                self.on_close_callback()
            except Exception:
                pass
        try:
            self.destroy()
        except Exception:
            pass

    def _on_close(self):
        self._safe_destroy()

    def _build_ui(self):
        # 1. 頂部標題區 (side="top")
        header_frame = tk.Frame(self, bg=THEME_BG)
        header_frame.pack(side="top", fill="x", padx=20, pady=(15, 8))
        
        tk.Label(
            header_frame, text="⚙️ 使用者帳號管理中心",
            font=(FONT_FAMILY, 14, "bold"),
            fg=THEME_ACCENT, bg=THEME_BG
        ).pack(anchor="w")
        
        tk.Label(
            header_frame, text="支援多成員獨立持有庫存，可自由新增帳號或設置獨立安全密碼",
            font=(FONT_FAMILY, 9),
            fg=THEME_TEXT_MUTED, bg=THEME_BG
        ).pack(anchor="w", pady=(2, 0))
        
        # 2. 下半部：快速新增帳號區 (以 side="bottom" 優先宣告，保障物理空間絕對優先權，永不被中央擠壓裁切！)
        add_frame = tk.LabelFrame(
            self, text=" ➕ 建立新帳號 ",
            font=(FONT_FAMILY, 9, "bold"),
            fg=THEME_ACCENT, bg=THEME_BG,
            highlightbackground=THEME_BORDER, highlightthickness=1
        )
        add_frame.pack(side="bottom", fill="x", padx=20, pady=(6, 16))
        
        form_grid = tk.Frame(add_frame, bg=THEME_BG)
        form_grid.pack(fill="x", padx=15, pady=10)
        
        # Row 1: Username & Display Name
        tk.Label(form_grid, text="帳號 (英文/數字):", font=(FONT_FAMILY, 9), fg=THEME_TEXT, bg=THEME_BG).grid(row=0, column=0, sticky="w", pady=4)
        self.new_user_entry = tk.Entry(form_grid, font=(FONT_FAMILY, 9), bg=THEME_CARD_BG, fg=THEME_TEXT, insertbackground=THEME_TEXT, relief="flat")
        self.new_user_entry.grid(row=0, column=1, sticky="ew", padx=(5, 15), pady=4, ipady=3)
        
        tk.Label(form_grid, text="顯示名稱 (暱稱):", font=(FONT_FAMILY, 9), fg=THEME_TEXT, bg=THEME_BG).grid(row=0, column=2, sticky="w", pady=4)
        self.new_name_entry = tk.Entry(form_grid, font=(FONT_FAMILY, 9), bg=THEME_CARD_BG, fg=THEME_TEXT, insertbackground=THEME_TEXT, relief="flat")
        self.new_name_entry.grid(row=0, column=3, sticky="ew", padx=5, pady=4, ipady=3)
        
        # Row 2: Password (Optional)
        tk.Label(form_grid, text="密碼 (留空則免密碼):", font=(FONT_FAMILY, 9), fg=THEME_TEXT, bg=THEME_BG).grid(row=1, column=0, sticky="w", pady=4)
        self.new_pwd_entry = tk.Entry(form_grid, font=(FONT_FAMILY, 9), show="●", bg=THEME_CARD_BG, fg=THEME_TEXT, insertbackground=THEME_TEXT, relief="flat")
        self.new_pwd_entry.grid(row=1, column=1, sticky="ew", padx=(5, 15), pady=4, ipady=3)
        
        self.add_btn = tk.Button(
            form_grid, text="確認新增帳戶",
            font=(FONT_FAMILY, 9, "bold"),
            bg=THEME_ACCENT, fg="#0f172a",
            relief="flat", cursor="hand2",
            command=self._do_add_user
        )
        self.add_btn.grid(row=1, column=2, columnspan=2, sticky="ew", padx=5, pady=4, ipady=3)
        
        form_grid.columnconfigure(1, weight=1)
        form_grid.columnconfigure(3, weight=1)

        # 3. 中央主體：帳號列表與管理操作 (side="top", fill="both", expand=True，填滿頂部與底部之間的全部空間)
        body_frame = tk.Frame(self, bg=THEME_CARD_BG, highlightbackground=THEME_BORDER, highlightthickness=1)
        body_frame.pack(side="top", fill="both", expand=True, padx=20, pady=4)
        
        # 列表標題
        list_header = tk.Frame(body_frame, bg=THEME_CARD_BG)
        list_header.pack(fill="x", padx=15, pady=(10, 5))
        
        tk.Label(
            list_header, text="目前帳號列表：",
            font=(FONT_FAMILY, 10, "bold"),
            fg=THEME_TEXT, bg=THEME_CARD_BG
        ).pack(side="left")
        
        # Treeview 表格
        tree_frame = tk.Frame(body_frame, bg=THEME_CARD_BG)
        tree_frame.pack(fill="both", expand=True, padx=15, pady=5)
        
        columns = ("id", "username", "display_name", "security", "created_at")
        self.tree = ttk.Treeview(
            tree_frame, columns=columns, show="headings",
            height=5, selectmode="browse"
        )
        
        self.tree.heading("id", text="ID")
        self.tree.heading("username", text="帳號名稱")
        self.tree.heading("display_name", text="顯示暱稱")
        self.tree.heading("security", text="安全防護")
        self.tree.heading("created_at", text="建立時間")
        
        self.tree.column("id", width=35, anchor="center")
        self.tree.column("username", width=100, anchor="w")
        self.tree.column("display_name", width=120, anchor="w")
        self.tree.column("security", width=90, anchor="center")
        self.tree.column("created_at", width=130, anchor="center")
        
        scrollbar = ttk.Scrollbar(tree_frame, orient="vertical", command=self.tree.yview)
        self.tree.configure(yscrollcommand=scrollbar.set)
        
        self.tree.pack(side="left", fill="both", expand=True)
        scrollbar.pack(side="right", fill="y")
        self.tree.bind("<<TreeviewSelect>>", self._on_tree_select)
        
        # 操作按鈕列 (選取帳號後之動作 + 返回關閉)
        actions_bar = tk.Frame(body_frame, bg=THEME_CARD_BG)
        actions_bar.pack(fill="x", padx=15, pady=(5, 12))
        
        self.edit_btn = tk.Button(
            actions_bar, text="✏️ 編輯帳號/暱稱/密碼",
            font=(FONT_FAMILY, 9),
            bg=THEME_BG, fg=THEME_TEXT,
            relief="flat", cursor="hand2",
            state="disabled", command=self._edit_selected_user
        )
        self.edit_btn.pack(side="left", padx=(0, 6), ipady=3)
        
        self.del_btn = tk.Button(
            actions_bar, text="🗑️ 刪除此帳號",
            font=(FONT_FAMILY, 9),
            bg=THEME_BG, fg=THEME_DANGER,
            relief="flat", cursor="hand2",
            state="disabled", command=self._delete_selected_user
        )
        self.del_btn.pack(side="left", padx=6, ipady=3)
        
        close_btn = tk.Button(
            actions_bar, text="✖ 返回關閉",
            font=(FONT_FAMILY, 9),
            bg=THEME_BG, fg=THEME_TEXT_MUTED,
            activebackground=THEME_BORDER, activeforeground=THEME_TEXT,
            relief="flat", cursor="hand2",
            command=self._on_close
        )
        close_btn.pack(side="right", ipady=3)

    def _refresh_user_list(self):
        self.users = database.get_all_users()
        for row in self.tree.get_children():
            self.tree.delete(row)
            
        for u in self.users:
            sec_text = "🔑 密碼防護" if u.get("has_password") else "🔓 免密碼"
            created = str(u.get("created_at", ""))[:19]
            self.tree.insert("", "end", iid=str(u["id"]), values=(
                u["id"],
                u["username"],
                u.get("display_name", u["username"]),
                sec_text,
                created
            ))
            
        self.edit_btn.configure(state="disabled")
        self.del_btn.configure(state="disabled")
        if self.on_users_changed:
            try:
                self.on_users_changed()
            except Exception:
                pass

    def _on_tree_select(self, event=None):
        sel = self.tree.selection()
        if sel:
            self.edit_btn.configure(state="normal")
            if len(self.users) > 1:
                self.del_btn.configure(state="normal")
            else:
                self.del_btn.configure(state="disabled")
        else:
            self.edit_btn.configure(state="disabled")
            self.del_btn.configure(state="disabled")

    def _do_add_user(self):
        username = self.new_user_entry.get().strip()
        display_name = self.new_name_entry.get().strip() or username
        password = self.new_pwd_entry.get().strip()
        
        if not username:
            messagebox.showwarning("欄位未填", "請輸入帳號名稱！", parent=self)
            self.new_user_entry.focus_set()
            return
            
        ok, msg, new_uid = database.create_user(username, display_name, password)
        if not ok:
            messagebox.showerror("新增失敗", msg, parent=self)
            return
            
        messagebox.showinfo("新增成功", f"帳號「{display_name}」(@{username}) 已成功建立！", parent=self)
        self.new_user_entry.delete(0, tk.END)
        self.new_name_entry.delete(0, tk.END)
        self.new_pwd_entry.delete(0, tk.END)
        self._refresh_user_list()

    def _edit_selected_user(self):
        sel = self.tree.selection()
        if not sel:
            return
        user_id = int(sel[0])
        user = next((u for u in self.users if u["id"] == user_id), None)
        if not user:
            return
            
        # 暫時釋放當前 grab 以便子編輯視窗接收事件
        try:
            self.grab_release()
        except Exception:
            pass
        EditUserDialog(self, user, on_saved=self._refresh_user_list, on_close_callback=self._restore_grab)

    def _restore_grab(self):
        try:
            self.lift()
            self.focus_force()
            self.grab_set()
        except Exception:
            pass

    def _delete_selected_user(self):
        sel = self.tree.selection()
        if not sel:
            return
        user_id = int(sel[0])
        user = next((u for u in self.users if u["id"] == user_id), None)
        if not user:
            return
            
        if len(self.users) <= 1:
            messagebox.showwarning("無法刪除", "系統必須保留至少一個主要帳戶！", parent=self)
            return
            
        cur_u = database.get_current_user()
        if cur_u.get("id") == user_id:
            messagebox.showwarning("無法刪除", "無法刪除當前正在使用的帳號，請先切換至其他帳號！", parent=self)
            return
            
        name = user.get("display_name", user["username"])
        if messagebox.askyesno("確認刪除", f"確定要刪除帳號「{name}」(@{user['username']}) 嗎？\n注意：該帳號所屬的專屬庫存與買入批次將一併清除！", parent=self):
            ok, msg = database.delete_user(user_id)
            if ok:
                messagebox.showinfo("已刪除", msg, parent=self)
                self._refresh_user_list()
            else:
                messagebox.showerror("刪除失敗", msg, parent=self)


class EditUserDialog(tk.Toplevel):
    """
    修改使用者資料與密碼對話框 (模態置頂，防死鎖，防裁切)
    """
    def __init__(self, parent, user: Dict[str, Any], on_saved: Optional[Callable[[], None]] = None, on_close_callback: Optional[Callable[[], None]] = None):
        super().__init__(parent)
        self.parent = parent
        self.user = user
        self.on_saved = on_saved
        self.on_close_callback = on_close_callback
        
        self.title(f"編輯帳戶 - {user.get('username')}")
        self.configure(bg=THEME_BG)
        
        self.protocol("WM_DELETE_WINDOW", self._on_close)
        self.bind("<Escape>", lambda e: self._on_close())
        
        self._build_ui()
        center_toplevel(self, 460, 430, parent, resizable=True)
        
        self.deiconify()
        if parent and parent.winfo_viewable():
            try:
                self.transient(parent)
            except Exception:
                pass
        self.lift()
        self.focus_force()
        try:
            self.grab_set()
        except Exception:
            pass

    def _safe_destroy(self):
        try:
            self.grab_release()
        except Exception:
            pass
        if self.on_close_callback:
            try:
                self.on_close_callback()
            except Exception:
                pass
        try:
            self.destroy()
        except Exception:
            pass

    def _on_close(self):
        self._safe_destroy()

    def _build_ui(self):
        tk.Label(
            self, text="⚙️ 編輯帳號資料與安全防護",
            font=(FONT_FAMILY, 12, "bold"),
            fg=THEME_ACCENT, bg=THEME_BG
        ).pack(anchor="w", padx=25, pady=(20, 5))
        
        # 底部按鈕列 (side="bottom"，優先排版)
        btn_box = tk.Frame(self, bg=THEME_BG)
        btn_box.pack(side="bottom", fill="x", padx=25, pady=(5, 18))
        
        save_btn = tk.Button(
            btn_box, text="儲存變更",
            font=(FONT_FAMILY, 10, "bold"),
            bg=THEME_ACCENT, fg="#0f172a",
            relief="flat", cursor="hand2",
            command=self._do_save
        )
        save_btn.pack(side="left", fill="x", expand=True, padx=(0, 5), ipady=4)
        
        cancel_btn = tk.Button(
            btn_box, text="取消",
            font=(FONT_FAMILY, 10),
            bg=THEME_CARD_BG, fg=THEME_TEXT_MUTED,
            relief="flat", cursor="hand2",
            command=self._on_close
        )
        cancel_btn.pack(side="right", fill="x", expand=True, padx=(5, 0), ipady=4)

        # 中央表單卡片
        card = tk.Frame(self, bg=THEME_CARD_BG, highlightbackground=THEME_BORDER, highlightthickness=1)
        card.pack(side="top", fill="both", expand=True, padx=25, pady=8)
        
        # 1. 帳號名稱 (登入代號)
        tk.Label(card, text="帳號名稱 (登入代號 / 英文數字)：", font=(FONT_FAMILY, 9), fg=THEME_TEXT, bg=THEME_CARD_BG).pack(anchor="w", padx=15, pady=(10, 2))
        self.username_entry = tk.Entry(card, font=(FONT_FAMILY, 10), bg=THEME_ENTRY_BG, fg=THEME_TEXT, insertbackground=THEME_TEXT, relief="flat")
        self.username_entry.insert(0, self.user.get("username", ""))
        self.username_entry.pack(fill="x", padx=15, ipady=4)

        # 2. 顯示暱稱
        tk.Label(card, text="顯示暱稱 (個人自訂名稱)：", font=(FONT_FAMILY, 9), fg=THEME_TEXT, bg=THEME_CARD_BG).pack(anchor="w", padx=15, pady=(8, 2))
        self.name_entry = tk.Entry(card, font=(FONT_FAMILY, 10), bg=THEME_ENTRY_BG, fg=THEME_TEXT, insertbackground=THEME_TEXT, relief="flat")
        self.name_entry.insert(0, self.user.get("display_name", ""))
        self.name_entry.pack(fill="x", padx=15, ipady=4)
        
        # 3. 新密碼
        tk.Label(card, text="新密碼 (若不修改請留空，輸入空格清除密碼)：", font=(FONT_FAMILY, 9), fg=THEME_TEXT, bg=THEME_CARD_BG).pack(anchor="w", padx=15, pady=(8, 2))
        self.pwd_entry = tk.Entry(card, font=(FONT_FAMILY, 10), show="●", bg=THEME_ENTRY_BG, fg=THEME_TEXT, insertbackground=THEME_TEXT, relief="flat")
        self.pwd_entry.pack(fill="x", padx=15, ipady=4)
        
        # 4. 密碼確認
        tk.Label(card, text="再次輸入新密碼確認：", font=(FONT_FAMILY, 9), fg=THEME_TEXT, bg=THEME_CARD_BG).pack(anchor="w", padx=15, pady=(8, 2))
        self.confirm_entry = tk.Entry(card, font=(FONT_FAMILY, 10), show="●", bg=THEME_ENTRY_BG, fg=THEME_TEXT, insertbackground=THEME_TEXT, relief="flat")
        self.confirm_entry.pack(fill="x", padx=15, ipady=4)

    def _do_save(self):
        new_username = self.username_entry.get().strip()
        new_name = self.name_entry.get().strip() or new_username
        new_pwd = self.pwd_entry.get()
        confirm_pwd = self.confirm_entry.get()
        
        if not new_username:
            messagebox.showwarning("欄位未填", "帳號名稱不可為空！", parent=self)
            self.username_entry.focus_set()
            return
            
        # 1. 若修改了帳號名稱，執行更新與唯一性校驗
        old_username = self.user.get("username", "")
        if new_username != old_username:
            ok_u, msg_u = database.update_username(self.user["id"], new_username)
            if not ok_u:
                messagebox.showerror("修改帳號名稱失敗", msg_u, parent=self)
                self.username_entry.focus_set()
                return

        # 2. 更新顯示暱稱
        database.update_user_display_name(self.user["id"], new_name)
        
        # 3. 更新密碼 (若有輸入)
        if new_pwd != "":
            if new_pwd != confirm_pwd:
                messagebox.showerror("密碼不一致", "兩次輸入的新密碼不相符，請重新確認！", parent=self)
                return
            database.update_user_password(self.user["id"], new_pwd.strip())
            
        # 4. 若修改的是當前登入者，同步更新全域狀態
        cur_u = database.get_current_user()
        if cur_u.get("id") == self.user["id"]:
            cur_u["username"] = new_username
            cur_u["display_name"] = new_name
            database.set_current_user(cur_u)
            
        messagebox.showinfo("更新成功", f"帳號「{new_name}」(@{new_username}) 設定已成功更新！", parent=self)
        if self.on_saved:
            try:
                self.on_saved()
            except Exception:
                pass
        self._safe_destroy()
