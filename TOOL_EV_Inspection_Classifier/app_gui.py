"""
電動車進料檢驗自動分類與統計工具 (EV Inspection Auto Classifier) V1.2
核心特色：
- 嚴格可靠度門檻：>= 95% 自動採納，< 95% 強制列入待人工判定審核區
- 人工審核判定回饋學習 (Active Learning Feedback Loop)
- 自動累積與持久化學習規則 (learning_rules.json)
- 單檔深層除錯診斷與異常隔離保護
- 100% 本機離線、0 Token、相容所有 Windows 環境
"""

import os
import sys
import io
import json
import threading
import traceback
from datetime import datetime
from pathlib import Path
import tkinter as tk
from tkinter import ttk, filedialog, messagebox
from PIL import Image, ImageTk
import pypdf

try:
    from core_engine import BOMManager, InspectionEngine, LearningEngine, DEFAULT_BOM_MAP
except ImportError:
    sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
    from core_engine import BOMManager, InspectionEngine, LearningEngine, DEFAULT_BOM_MAP


class AppGUI(tk.Tk):
    def __init__(self):
        super().__init__()
        self.title("電動車進料檢驗自動分類與自主學習系統 V1.2 (可靠度 >=95% 門檻 | 人工審核與主動學習)")
        self.geometry("1180x780")
        self.minsize(1020, 650)

        self.style = ttk.Style(self)
        try:
            self.style.theme_use('vista')
        except:
            pass

        # Configure treeview fonts and styles
        self.style.configure("Treeview.Heading", font=("微軟正黑體", 9, "bold"))
        self.style.configure("Treeview", font=("Consolas", 9), rowheight=22)

        self.project_root = Path(r"c:\Users\peter\OneDrive\Desktop\AI_Projects")
        self.app_dir = Path(sys.executable).parent if getattr(sys, 'frozen', False) else Path(__file__).parent
        self.config_path = self.app_dir / "app_config.json"
        self.log_file_path = self.app_dir / "classifier_runtime.log"
        saved_cfg = self._load_saved_config()

        # Persistent target directory resolution
        cand_targets = [
            Path(saved_cfg.get('target_dir', '')) if saved_cfg.get('target_dir') else None,
            Path(r"C:\Users\peter\OneDrive\Desktop\資料整理\2025"),
            self.project_root / "資料搜尋" / "品檢2026",
            self.project_root / "資料搜尋" / "2026電動車進料檢記錄",
            self.project_root / "資料搜尋" / "2026",
            self.project_root / "資料搜尋"
        ]
        self.default_target = next((c for c in cand_targets if c and c.exists()), cand_targets[1])

        # Persistent BOM resolution
        cand_boms = [
            Path(saved_cfg.get('bom_path', '')) if saved_cfg.get('bom_path') else None,
            self.project_root / "資料搜尋" / "EVMD250-30(BOM).xlsx",
            self.project_root / "資料搜尋" / "產品BOM用料需求表 - 十二米電動車.xlsx"
        ]
        self.default_bom = next((c for c in cand_boms if c and c.exists()), cand_boms[1])
        self.default_history = self.project_root / "資料搜尋" / "進貨明細清單_依PDF品名.csv"

        self.saved_mode = saved_cfg.get('mode', 'move')
        self.saved_debug = saved_cfg.get('debug', True)

        self.learning_engine = LearningEngine()
        self.engine = None
        self.records = []
        self.last_excel_path = None

        self._init_ui()
        self._refresh_learning_stats()

    def _load_saved_config(self):
        if self.config_path.exists():
            try:
                with open(self.config_path, 'r', encoding='utf-8') as f:
                    return json.load(f)
            except Exception:
                pass
        return {}

    def _save_current_config(self):
        try:
            data = {
                'target_dir': self.var_target.get().strip(),
                'bom_path': self.var_bom.get().strip(),
                'mode': self.var_mode.get(),
                'debug': self.var_debug.get()
            }
            with open(self.config_path, 'w', encoding='utf-8') as f:
                json.dump(data, f, ensure_ascii=False, indent=2)
        except Exception:
            pass

    def _init_ui(self):
        # ── 1. Top Banner ────────────────────────────────────────────────
        banner_frame = tk.Frame(self, bg='#1F497D', height=56)
        banner_frame.pack(fill=tk.X, side=tk.TOP)
        banner_frame.pack_propagate(False)

        lbl_title = tk.Label(
            banner_frame,
            text="電動車零部件進料檢驗 - 自動分類歸檔與自主學習系統 V1.2",
            font=("微軟正黑體", 13, "bold"),
            fg="#FFFFFF",
            bg="#1F497D"
        )
        lbl_title.pack(side=tk.LEFT, padx=15, pady=8)

        lbl_sub = tk.Label(
            banner_frame,
            text="[100% 地端離線 | 0 Token | 嚴格門檻: 可靠度 >=95% 自動採納 | 疑義單據人工審核判定與回饋學習]",
            font=("微軟正黑體", 9),
            fg="#DCE6F1",
            bg="#1F497D"
        )
        lbl_sub.pack(side=tk.RIGHT, padx=15, pady=12)

        main_container = ttk.Frame(self, padding="8")
        main_container.pack(fill=tk.BOTH, expand=True)

        # ── 2. Configuration & Learning Stats ─────────────────────────────
        cfg_frame = ttk.LabelFrame(main_container, text=" [1] 資料夾路徑、運作模式與學習庫狀態 ", padding="8")
        cfg_frame.pack(fill=tk.X, pady=(0, 6))

        # Target folder
        ttk.Label(cfg_frame, text="待分類資料夾：", width=14, anchor='e').grid(row=0, column=0, padx=4, pady=2, sticky='w')
        self.var_target = tk.StringVar(value=str(self.default_target) if self.default_target.exists() else "")
        self.ent_target = ttk.Entry(cfg_frame, textvariable=self.var_target, font=("Consolas", 9))
        self.ent_target.grid(row=0, column=1, sticky='ew', padx=4, pady=2)
        ttk.Button(cfg_frame, text="瀏覽...", width=8, command=self._browse_target).grid(row=0, column=2, padx=4, pady=2)

        # BOM file
        ttk.Label(cfg_frame, text="BOM 需求表：", width=14, anchor='e').grid(row=1, column=0, padx=4, pady=2, sticky='w')
        self.var_bom = tk.StringVar(value=str(self.default_bom) if self.default_bom.exists() else "")
        self.ent_bom = ttk.Entry(cfg_frame, textvariable=self.var_bom, font=("Consolas", 9))
        self.ent_bom.grid(row=1, column=1, sticky='ew', padx=4, pady=2)
        ttk.Button(cfg_frame, text="瀏覽...", width=8, command=self._browse_bom).grid(row=1, column=2, padx=4, pady=2)

        # Mode line + Debug + Learning Stats
        opt_box = ttk.Frame(cfg_frame)
        opt_box.grid(row=2, column=1, sticky='ew', padx=4, pady=4)

        self.var_mode = tk.StringVar(value=self.saved_mode)
        ttk.Radiobutton(opt_box, text="直接分類移動", variable=self.var_mode, value="move", command=self._save_current_config).pack(side=tk.LEFT, padx=(0, 12))
        ttk.Radiobutton(opt_box, text="複製檔案分類", variable=self.var_mode, value="copy", command=self._save_current_config).pack(side=tk.LEFT, padx=(0, 20))

        self.var_debug = tk.BooleanVar(value=self.saved_debug)
        ttk.Checkbutton(opt_box, text="詳細除錯日誌 (Debug)", variable=self.var_debug, command=self._save_current_config).pack(side=tk.LEFT, padx=(0, 25))

        self.lbl_learning_stats = ttk.Label(opt_box, text="", font=("微軟正黑體", 9, "bold"), foreground="#1F497D")
        self.lbl_learning_stats.pack(side=tk.RIGHT)

        cfg_frame.columnconfigure(1, weight=1)

        # ── 3. Action Buttons & Progress Bar ──────────────────────────────
        btn_bar = ttk.Frame(main_container)
        btn_bar.pack(fill=tk.X, pady=(0, 6))

        self.btn_scan = ttk.Button(btn_bar, text="[->] 1. 掃描與解析 PDF", width=18, command=self._action_scan)
        self.btn_scan.pack(side=tk.LEFT, padx=(0, 5))

        self.btn_adjudicate = ttk.Button(btn_bar, text="[!] 進行人工審核判定與學習", width=24, command=self._action_adjudicate_selected, state=tk.DISABLED)
        self.btn_adjudicate.pack(side=tk.LEFT, padx=(0, 5))

        self.btn_execute = ttk.Button(btn_bar, text="[*] 2. 一鍵執行分類與產出報表", width=25, command=self._action_execute, state=tk.DISABLED)
        self.btn_execute.pack(side=tk.LEFT, padx=(0, 10))

        self.btn_diag = ttk.Button(btn_bar, text="[?] 單檔診斷除錯", width=14, command=self._action_diagnose_single)
        self.btn_diag.pack(side=tk.LEFT, padx=(0, 5))

        self.btn_open_folder = ttk.Button(btn_bar, text="[DIR] 開啟目標目錄", width=15, command=self._action_open_dir)
        self.btn_open_folder.pack(side=tk.LEFT, padx=(0, 5))

        self.btn_open_excel = ttk.Button(btn_bar, text="[XLS] 開啟統計 Excel", width=15, command=self._action_open_excel, state=tk.DISABLED)
        self.btn_open_excel.pack(side=tk.LEFT, padx=(0, 5))

        self.btn_autofit = ttk.Button(btn_bar, text="[<->] 最適欄寬", width=12, command=self._action_auto_fit)
        self.btn_autofit.pack(side=tk.LEFT)

        # Progress bar
        self.var_progress = tk.DoubleVar(value=0.0)
        self.progress_bar = ttk.Progressbar(btn_bar, variable=self.var_progress, maximum=100)
        self.progress_bar.pack(side=tk.RIGHT, fill=tk.X, expand=True, padx=(15, 0))

        # ── 4. Main Notebook (3 Tabs) ────────────────────────────────────
        self.notebook = ttk.Notebook(main_container)
        self.notebook.pack(fill=tk.BOTH, expand=True, pady=(0, 6))

        # Tab 1: All Records
        self.tab_all = ttk.Frame(self.notebook, padding="4")
        self.notebook.add(self.tab_all, text="  全部單據明細清單 (0)  ")
        self.tree_all = self._create_treeview(self.tab_all, is_adjudication_tab=False)

        # Tab 2: Under 95% Pending Adjudication
        self.tab_under95 = ttk.Frame(self.notebook, padding="4")
        self.notebook.add(self.tab_under95, text="  [!] 待人工判定審核區 (<95%) (0)  ")

        # Top instruction bar in Tab 2
        hint_bar = tk.Frame(self.tab_under95, bg='#FFF2CC', relief=tk.SOLID, borderwidth=1, padx=6, pady=4)
        hint_bar.pack(fill=tk.X, pady=(0, 4))
        lbl_hint = tk.Label(
            hint_bar,
            text="[審核提示] 下列單據可靠度低於 95% 或存在疑義。請選取單據後點選上方【[!] 進行人工審核判定與學習】按鈕（或雙擊該列），核定後系統將立即儲存至自主學習庫，日後同類單據 100% 自動採納！",
            font=("微軟正黑體", 9, "bold"),
            fg="#B25900",
            bg="#FFF2CC"
        )
        btn_batch_invalid = tk.Button(
            hint_bar,
            text="[X] 標記所選為無效單據 (刪除不計)",
            fg="#FFFFFF",
            bg="#C62828",
            activebackground="#B71C1C",
            activeforeground="#FFFFFF",
            font=("微軟正黑體", 9, "bold"),
            padx=8,
            pady=2,
            relief=tk.RAISED,
            command=self._action_mark_selected_invalid
        )
        btn_batch_invalid.pack(side=tk.RIGHT, padx=4)
        lbl_hint.pack(side=tk.LEFT)

        self.tree_under95 = self._create_treeview(self.tab_under95, is_adjudication_tab=True)

        # Tab 3: Active Learning Database Manager
        self.tab_learning = ttk.Frame(self.notebook, padding="6")
        self.notebook.add(self.tab_learning, text="  [AI] 自主學習規則庫管理  ")
        self._init_learning_tab(self.tab_learning)

        # ── 5. Log Console ───────────────────────────────────────────────
        log_frame = ttk.LabelFrame(main_container, text=" [3] 執行即時日誌與主動學習追蹤 ", padding="6")
        log_frame.pack(fill=tk.X)

        log_top_bar = ttk.Frame(log_frame)
        log_top_bar.pack(fill=tk.X, pady=(0, 2))
        ttk.Button(log_top_bar, text="清除日誌", width=10, command=self._clear_log).pack(side=tk.RIGHT)
        ttk.Button(log_top_bar, text="匯出除錯日誌", width=12, command=self._export_log).pack(side=tk.RIGHT, padx=4)

        self.txt_log = tk.Text(log_frame, height=4, font=("Consolas", 9), wrap=tk.WORD, bg="#FAFAFA")
        log_scroll = ttk.Scrollbar(log_frame, orient=tk.VERTICAL, command=self.txt_log.yview)
        self.txt_log.configure(yscrollcommand=log_scroll.set)
        self.txt_log.pack(side=tk.LEFT, fill=tk.BOTH, expand=True)
        log_scroll.pack(side=tk.RIGHT, fill=tk.Y)

        self._log("系統就緒。可靠度門檻設定：>= 95.0% 自動採納；< 95.0% 強制列入人工審核。請點擊【1. 掃描與解析 PDF】開始分析。")

    def _create_treeview(self, parent, is_adjudication_tab=False):
        cols = ("idx", "month", "date", "order", "part_no", "part_name", "qty", "usage", "equiv", "conf", "status", "note", "desc", "filename")
        tree_container = ttk.Frame(parent)
        tree_container.pack(fill=tk.BOTH, expand=True)

        tree = ttk.Treeview(tree_container, columns=cols, show="headings", selectmode="browse")

        tree.heading("idx", text="序號")
        tree.heading("month", text="月份")
        tree.heading("date", text="進料日期")
        tree.heading("order", text="工單/批號")
        tree.heading("part_no", text="標準料號")
        tree.heading("part_name", text="品名規格")
        tree.heading("qty", text="數量")
        tree.heading("usage", text="單台用量")
        tree.heading("equiv", text="折合套數")
        tree.heading("conf", text="可靠度")
        tree.heading("status", text="判定狀態")
        tree.heading("note", text="檢驗判定/備註")
        tree.heading("desc", text="診斷與評分細節")
        tree.heading("filename", text="原始檔案名稱")

        tree.column("idx", width=42, anchor="center")
        tree.column("month", width=48, anchor="center")
        tree.column("date", width=80, anchor="center")
        tree.column("order", width=95, anchor="center")
        tree.column("part_no", width=130, anchor="w")
        tree.column("part_name", width=160, anchor="w")
        tree.column("qty", width=60, anchor="e")
        tree.column("usage", width=55, anchor="center")
        tree.column("equiv", width=65, anchor="e")
        tree.column("conf", width=65, anchor="center")
        tree.column("status", width=120, anchor="center")
        tree.column("note", width=120, anchor="w")
        tree.column("desc", width=160, anchor="w")
        tree.column("filename", width=210, anchor="w")

        v_scroll = ttk.Scrollbar(tree_container, orient=tk.VERTICAL, command=tree.yview)
        h_scroll = ttk.Scrollbar(tree_container, orient=tk.HORIZONTAL, command=tree.xview)
        tree.configure(yscrollcommand=v_scroll.set, xscrollcommand=h_scroll.set)

        tree.grid(row=0, column=0, sticky='nsew')
        v_scroll.grid(row=0, column=1, sticky='ns')
        h_scroll.grid(row=1, column=0, sticky='ew')
        tree_container.rowconfigure(0, weight=1)
        tree_container.columnconfigure(0, weight=1)

        tree.bind("<Double-1>", lambda e: self._on_double_click_row(tree, e))
        tree.bind("<<TreeviewSelect>>", lambda e: self._on_tree_select(tree))
        return tree

    def _init_learning_tab(self, parent):
        top_bar = ttk.Frame(parent)
        top_bar.pack(fill=tk.X, pady=(0, 6))

        ttk.Label(top_bar, text="自主學習知識庫說明：系統會自動保存人工審核判定的料號別名、工單數量與覆核歷史，永久累積辨識智慧。", font=("微軟正黑體", 9)).pack(side=tk.LEFT)
        ttk.Button(top_bar, text="[重新整理學習庫]", command=self._refresh_learning_tab_data).pack(side=tk.RIGHT, padx=4)
        ttk.Button(top_bar, text="[開啟 JSON 知識檔]", command=self._open_learning_json).pack(side=tk.RIGHT, padx=4)

        paned = ttk.PanedWindow(parent, orient=tk.HORIZONTAL)
        paned.pack(fill=tk.BOTH, expand=True)

        # Left pane: Alias Mappings
        frame_alias = ttk.LabelFrame(paned, text=" 1. 料號別名映射 (Alias Mappings) ", padding="4")
        paned.add(frame_alias, weight=1)

        self.tree_alias = ttk.Treeview(frame_alias, columns=("raw", "target"), show="headings")
        self.tree_alias.heading("raw", text="原檔特徵 / 誤寫料號")
        self.tree_alias.heading("target", text="轉換為標準 BOM 料號")
        self.tree_alias.column("raw", width=150, anchor="w")
        self.tree_alias.column("target", width=150, anchor="w")
        self.tree_alias.pack(fill=tk.BOTH, expand=True)

        # Middle pane: Work Order Quantities
        frame_orders = ttk.LabelFrame(paned, text=" 2. 工單進貨數量記憶 (Work Order Quantities) ", padding="4")
        paned.add(frame_orders, weight=1)

        self.tree_orders = ttk.Treeview(frame_orders, columns=("order", "qty"), show="headings")
        self.tree_orders.heading("order", text="工單/批號")
        self.tree_orders.heading("qty", text="確認進貨數量 (PC)")
        self.tree_orders.column("order", width=120, anchor="center")
        self.tree_orders.column("qty", width=100, anchor="e")
        self.tree_orders.pack(fill=tk.BOTH, expand=True)

        # Right pane: History
        frame_hist = ttk.LabelFrame(paned, text=" 3. 人工審核判定歷史 (Audit History) ", padding="4")
        paned.add(frame_hist, weight=2)

        self.tree_hist = ttk.Treeview(frame_hist, columns=("fn", "part", "qty", "note"), show="headings")
        self.tree_hist.heading("fn", text="審核檔案")
        self.tree_hist.heading("part", text="核定料號")
        self.tree_hist.heading("qty", text="數量")
        self.tree_hist.heading("note", text="判定備註")
        self.tree_hist.column("fn", width=160, anchor="w")
        self.tree_hist.column("part", width=110, anchor="w")
        self.tree_hist.column("qty", width=50, anchor="e")
        self.tree_hist.column("note", width=110, anchor="w")
        self.tree_hist.pack(fill=tk.BOTH, expand=True)

        self._refresh_learning_tab_data()

    def _refresh_learning_stats(self):
        st = self.learning_engine.stats()
        self.lbl_learning_stats.config(
            text=f"[自主學習庫] 已記憶別名: {st['aliases']} 條 | 工單數量: {st['order_rules']} 條 | 審核歷史: {st['history_count']} 筆"
        )

    def _refresh_learning_tab_data(self):
        self.learning_engine.load()
        self._refresh_learning_stats()

        # Refresh Alias tree
        self.tree_alias.delete(*self.tree_alias.get_children())
        for raw, target in self.learning_engine.rules.get('alias_mappings', {}).items():
            self.tree_alias.insert("", tk.END, values=(raw, target))

        # Refresh Orders tree
        self.tree_orders.delete(*self.tree_orders.get_children())
        for order, qty in self.learning_engine.rules.get('work_order_quantities', {}).items():
            self.tree_orders.insert("", tk.END, values=(order, f"{qty:,} PC"))

        # Refresh History tree
        self.tree_hist.delete(*self.tree_hist.get_children())
        for h in reversed(self.learning_engine.rules.get('adjudication_history', [])):
            self.tree_hist.insert("", tk.END, values=(
                h.get('filename', ''),
                h.get('corrected_part', ''),
                h.get('qty', 0),
                h.get('note', '')
            ))

    def _open_learning_json(self):
        p = self.learning_engine.rules_file
        if p.exists():
            os.startfile(str(p))
        else:
            messagebox.showwarning("提示", f"知識庫檔案尚未建立：\n{p}")

    def _log(self, msg):
        self.txt_log.insert(tk.END, f"{msg}\n")
        self.txt_log.see(tk.END)
        try:
            with open(self.log_file_path, "a", encoding="utf-8") as lf:
                lf.write(f"[{datetime.now().strftime('%Y-%m-%d %H:%M:%S')}] {msg}\n")
        except Exception:
            pass

    def _clear_log(self):
        self.txt_log.delete("1.0", tk.END)

    def _export_log(self):
        f = filedialog.asksaveasfilename(title="儲存除錯日誌", defaultextension=".log", filetypes=[("Log Files", "*.log"), ("Text Files", "*.txt")])
        if f:
            try:
                with open(f, 'w', encoding='utf-8') as out:
                    out.write(self.txt_log.get("1.0", tk.END))
                messagebox.showinfo("成功", f"除錯日誌已成功儲存至：\n{f}")
            except Exception as e:
                messagebox.showerror("錯誤", f"儲存日誌失敗: {e}")

    def _browse_target(self):
        d = filedialog.askdirectory(title="選擇待分類進料檢資料夾", initialdir=str(self.project_root / "資料搜尋"))
        if d:
            self.var_target.set(d)
            self._save_current_config()
            self._log(f"[記憶設定] 待分類資料夾已更新並儲存：\n{d}")

    def _browse_bom(self):
        f = filedialog.askopenfilename(title="選擇 BOM 需求表 Excel", filetypes=[("Excel Files", "*.xlsx;*.xls")], initialdir=str(self.project_root / "資料搜尋"))
        if f:
            self.var_bom.set(f)
            self._save_current_config()
            self._log(f"[記憶設定] BOM 需求表路徑已更新並儲存：\n{f}")

    def _on_tree_select(self, tree):
        sel = tree.selection()
        if sel:
            self.btn_adjudicate.config(state=tk.NORMAL)
        else:
            self.btn_adjudicate.config(state=tk.DISABLED)

    def _auto_fit_columns(self, tree):
        """Automatically calculates and sets optimal column widths based on cell text content and headers."""
        import tkinter.font as tkfont
        hdr_font = tkfont.Font(family="微軟正黑體", size=9, weight="bold")
        cell_font = tkfont.Font(family="Consolas", size=9)
        cjk_font = tkfont.Font(family="微軟正黑體", size=9)

        for col in tree["columns"]:
            heading_txt = tree.heading(col)["text"]
            max_w = hdr_font.measure(heading_txt) + 24

            for item in tree.get_children():
                val = str(tree.set(item, col))
                if val:
                    w = max(cell_font.measure(val), cjk_font.measure(val)) + 22
                    if w > max_w:
                        max_w = w

            if col == "idx": max_w = max(max_w, 48)
            elif col == "month": max_w = max(max_w, 55)
            elif col == "date": max_w = max(max_w, 95)
            elif col == "order": max_w = max(max_w, 115)
            elif col == "part_no": max_w = max(max_w, 145)
            elif col == "part_name": max_w = max(max_w, 160)
            elif col in ["qty", "usage", "equiv"]: max_w = max(max_w, 65)
            elif col == "conf": max_w = max(max_w, 75)
            elif col == "status": max_w = max(max_w, 125)
            elif col == "note": max_w = max(max_w, 130)
            elif col == "desc": max_w = max(max_w, 240)
            elif col == "filename": max_w = max(max_w, 300)

            tree.column(col, width=max_w)

    def _action_auto_fit(self):
        self._auto_fit_columns(self.tree_all)
        self._auto_fit_columns(self.tree_under95)
        self._log("[版面優化] 已將所有表格欄位自動縮放至最適內容寬度！")

    def _action_scan(self):
        target = self.var_target.get().strip()
        if not target or not os.path.exists(target):
            messagebox.showerror("錯誤", "待分類資料夾路徑不存在，請重新選擇！")
            return

        bom_path = self.var_bom.get().strip()
        history_path = str(self.default_history) if self.default_history.exists() else None
        is_debug = self.var_debug.get()

        # Save config immediately on scan
        self._save_current_config()

        self.btn_scan.config(state=tk.DISABLED)
        self.btn_execute.config(state=tk.DISABLED)
        self.btn_adjudicate.config(state=tk.DISABLED)
        self.var_progress.set(0)
        self.tree_all.delete(*self.tree_all.get_children())
        self.tree_under95.delete(*self.tree_under95.get_children())

        self._log(f"\n--- 開始高精度掃描與解析 PDF (可靠度門檻 >= 95.0%) ---")
        self._log(f"目標目錄: {target}")
        self._log(f"BOM 需求表: {bom_path or '(使用內建標準 BOM)'}")

        def worker():
            try:
                self.learning_engine.load()
                bom_mgr = BOMManager(bom_path if os.path.exists(bom_path) else None, learning_engine=self.learning_engine, debug_callback=self._log)
                self.engine = InspectionEngine(bom_mgr, history_path, learning_engine=self.learning_engine, debug_mode=is_debug, log_callback=self._log)

                def update_prog(cur, tot, text):
                    pct = (cur / tot) * 100 if tot else 0
                    self.var_progress.set(pct)
                    if cur % 15 == 0 or cur == tot:
                        self._log(f"[{cur}/{tot}] {text}")

                self.records = self.engine.scan_and_parse_files(target, progress_callback=update_prog)
                self.after(0, self._render_grid)
            except Exception as e:
                self.after(0, lambda: messagebox.showerror("解析失敗", f"發生異常：{e}\n{traceback.format_exc()}"))
                self.after(0, lambda: self._log(f"[異常堆疊] {traceback.format_exc()}"))
            finally:
                self.after(0, lambda: self.btn_scan.config(state=tk.NORMAL))

        threading.Thread(target=worker, daemon=True).start()

    def _render_grid(self):
        total_qty = 0
        under_95_count = 0

        self.tree_all.delete(*self.tree_all.get_children())
        self.tree_under95.delete(*self.tree_under95.get_children())

        for r in self.records:
            equiv = round(r['qty'] / r['unit_usage'], 1) if r['unit_usage'] else r['qty']
            total_qty += r['qty']
            status_text = r.get('status_tag', '自動採納 (>=95%)' if r['is_accepted'] else '待人工判定 (<95%)')
            row_vals = (
                r['idx'],
                f"{r['month']}月",
                r['date'],
                r['order_no'],
                r['part_no'],
                r['part_name'],
                r['qty'],
                r['unit_usage'],
                equiv,
                f"{r['confidence']:.1f}%",
                status_text,
                r['note'],
                r['anomaly_desc'],
                r['filename']
            )
            self.tree_all.insert("", tk.END, values=row_vals)

            if not r['is_accepted']:
                under_95_count += 1
                self.tree_under95.insert("", tk.END, values=row_vals)

        self.notebook.tab(0, text=f"  全部單據明細清單 ({len(self.records)})  ")
        self.notebook.tab(1, text=f"  [!] 待人工判定審核區 (<95%) ({under_95_count})  ")

        # Automatically auto-fit all column widths to content
        self._auto_fit_columns(self.tree_all)
        self._auto_fit_columns(self.tree_under95)

        self.btn_execute.config(state=tk.NORMAL)
        self._refresh_learning_stats()

        self._log(f"\n[掃描解析完成] 共 {len(self.records)} 筆單據，總進貨數量：{total_qty:,} PC。")
        self._log("[版面優化] 已自動將所有欄位調整至最適內容寬度，無需手動拖曳！")
        if under_95_count > 0:
            self._log(f"[重要審核通知] 發現 {under_95_count} 筆單據可靠度 < 95%！已強制隔離列入【待人工判定審核區】。請點擊該分頁進行人工核定與回饋學習。")
            self.notebook.select(self.tab_under95)
        else:
            self._log("[完美符合] 100% 單據辨識可靠度皆 >= 95%，完全符合自動採納標準！")

    def _on_double_click_row(self, tree, event):
        item_id = tree.selection()
        if not item_id: return
        self._open_adjudication_dialog(tree, item_id[0])

    def _action_adjudicate_selected(self):
        cur_tab = self.notebook.index(self.notebook.select())
        active_tree = self.tree_under95 if cur_tab == 1 else self.tree_all
        sel = active_tree.selection()
        if not sel:
            messagebox.showinfo("提示", "請先在清單中選取一筆欲審核判定的單據！")
            return
        self._open_adjudication_dialog(active_tree, sel[0])

    def _action_mark_selected_invalid(self):
        cur_tab = self.notebook.index(self.notebook.select())
        active_tree = self.tree_under95 if cur_tab == 1 else self.tree_all
        sel = active_tree.selection()
        if not sel:
            messagebox.showwarning("提示", "請先在清單中選取要標記為無效的單據列！")
            return

        target_items = []
        for item in sel:
            vals = active_tree.item(item, 'values')
            fn = vals[13] if len(vals) > 13 else None
            if fn:
                target_items.append(fn)

        if not target_items:
            return

        msg = (
            f"確定將選取的 {len(target_items)} 筆單據確認為【無效單據 / 刪除不列入計算】？\n\n"
            + "\n".join(f"● {f}" for f in target_items[:5])
            + (f"\n...等共 {len(target_items)} 筆" if len(target_items) > 5 else "")
            + "\n\n標記後將徹底排除於進貨數量與整車齊套計算之外，歸檔至 [_作廢與無效單據隔離區]。"
        )
        if not messagebox.askyesno("確認排除無效單據", msg):
            return

        for fn in target_items:
            self.learning_engine.mark_invalid(fn)
            for r in self.records:
                if r['filename'] == fn:
                    r['is_valid'] = False
                    r['qty'] = 0
                    r['is_accepted'] = True
                    r['is_anomaly'] = False
                    r['status_tag'] = "人工排除 (無效單據)"
                    r['note'] = "作廢/無效單據 (排除不計入進貨)"
                    r['anomaly_desc'] = "人工確認為無效單據，已排除不列入計算"
                    r['folder_name'] = "_作廢與無效單據隔離區"

        self._log(f"[批量排除] 已將 {len(target_items)} 筆單據標記為無效單據，排除於進貨數量統計之外！")
        self._render_grid()
        messagebox.showinfo("成功排除", f"已成功排除 {len(target_items)} 筆無效單據！\n這些單據已從待人工判定區清除，且不計入進貨數量統計。")

    def _open_adjudication_dialog(self, source_tree, item_id):
        vals = list(source_tree.item(item_id, 'values'))
        row_idx = str(vals[0])
        rec = next((r for r in self.records if str(r['idx']) == row_idx), None)
        if not rec:
            rec = self.records[int(row_idx) - 1]

        dlg = tk.Toplevel(self)
        dlg.title(f"【人工審核判定與主動學習】 - 序號 {rec['idx']}: {rec['filename']}")
        dlg.geometry("1180x760")
        dlg.minsize(980, 600)
        dlg.resizable(True, True)
        dlg.transient(self)
        dlg.grab_set()

        # Split dialog into PanedWindow (Left: PDF Preview, Right: Adjudication Form)
        paned = ttk.PanedWindow(dlg, orient=tk.HORIZONTAL)
        paned.pack(fill=tk.BOTH, expand=True, padx=8, pady=8)

        # ── LEFT PANE: PDF Preview (58% width) ───────────────────────────
        frame_pdf = ttk.LabelFrame(paned, text=" [1] 原始檢驗單據 PDF 畫面檢視 (可縮放/滾動對照) ", padding="6")
        paned.add(frame_pdf, weight=6)

        # PDF Top Toolbar
        pdf_bar = ttk.Frame(frame_pdf)
        pdf_bar.pack(fill=tk.X, pady=(0, 4))

        pdf_path = Path(rec.get('path', ''))
        def open_external():
            if pdf_path.exists():
                os.startfile(str(pdf_path))
            else:
                messagebox.showerror("錯誤", f"找不到原始 PDF 檔案：\n{pdf_path}")

        btn_ext = ttk.Button(pdf_bar, text="[開啟系統外部 PDF 閱讀器]", command=open_external)
        btn_ext.pack(side=tk.LEFT, padx=(0, 6))

        # Canvas with Scrollbars
        canvas_container = ttk.Frame(frame_pdf)
        canvas_container.pack(fill=tk.BOTH, expand=True)

        canvas = tk.Canvas(canvas_container, bg="#2D2D2D")
        v_scroll = ttk.Scrollbar(canvas_container, orient=tk.VERTICAL, command=canvas.yview)
        h_scroll = ttk.Scrollbar(canvas_container, orient=tk.HORIZONTAL, command=canvas.xview)
        canvas.configure(xscrollcommand=h_scroll.set, yscrollcommand=v_scroll.set)

        canvas.grid(row=0, column=0, sticky='nsew')
        v_scroll.grid(row=0, column=1, sticky='ns')
        h_scroll.grid(row=1, column=0, sticky='ew')
        canvas_container.rowconfigure(0, weight=1)
        canvas_container.columnconfigure(0, weight=1)

        # Mouse wheel bindings
        def on_mousewheel(event):
            canvas.yview_scroll(int(-1 * (event.delta / 120)), "units")
        def on_shift_mousewheel(event):
            canvas.xview_scroll(int(-1 * (event.delta / 120)), "units")
        canvas.bind("<MouseWheel>", on_mousewheel)
        canvas.bind("<Shift-MouseWheel>", on_shift_mousewheel)

        # State for PDF Viewer
        pdf_state = {
            'pages': [],
            'cur_page': 0,
            'zoom': 1.0,
            'tk_img': None
        }

        # Load PDF Pages
        if pdf_path.exists():
            try:
                reader = pypdf.PdfReader(str(pdf_path))
                for page in reader.pages:
                    if len(page.images) > 0:
                        img_data = page.images[0].data
                        pil_img = Image.open(io.BytesIO(img_data))
                        pdf_state['pages'].append(pil_img)
                    else:
                        txt = page.extract_text() or "(此頁無圖片與數位文字)"
                        pdf_state['pages'].append(txt)
            except Exception as e:
                pdf_state['pages'].append(f"讀取 PDF 失敗：{e}")
        else:
            pdf_state['pages'].append(f"檔案不存在：{pdf_path}")

        lbl_page_info = ttk.Label(pdf_bar, text="第 1/1 頁", font=("微軟正黑體", 9))

        def render_current_page():
            if not pdf_state['pages']:
                canvas.delete("all")
                canvas.create_text(200, 200, text="無可檢視之頁面", fill="#FFFFFF")
                return
            content = pdf_state['pages'][pdf_state['cur_page']]
            if isinstance(content, Image.Image):
                base_w = int(580 * pdf_state['zoom'])
                w_pct = base_w / float(content.size[0])
                h_size = int(float(content.size[1]) * float(w_pct))
                resized = content.resize((base_w, h_size), Image.Resampling.LANCZOS)
                pdf_state['tk_img'] = ImageTk.PhotoImage(resized)
                canvas.delete("all")
                canvas.create_image(10, 10, anchor='nw', image=pdf_state['tk_img'])
                canvas.config(scrollregion=(0, 0, base_w + 30, h_size + 30))
                lbl_page_info.config(text=f"第 {pdf_state['cur_page']+1} / {len(pdf_state['pages'])} 頁 (縮放: {int(pdf_state['zoom']*100)}%)")
            else:
                canvas.delete("all")
                canvas.create_text(20, 20, anchor='nw', text=str(content), fill="#E0E0E0", font=("Consolas", 10))
                canvas.config(scrollregion=(0, 0, 800, 800))
                lbl_page_info.config(text=f"第 {pdf_state['cur_page']+1} / {len(pdf_state['pages'])} 頁 [純文字]")

        def prev_page():
            if pdf_state['cur_page'] > 0:
                pdf_state['cur_page'] -= 1
                render_current_page()

        def next_page():
            if pdf_state['cur_page'] < len(pdf_state['pages']) - 1:
                pdf_state['cur_page'] += 1
                render_current_page()

        def zoom_in():
            if pdf_state['zoom'] < 3.0:
                pdf_state['zoom'] += 0.2
                render_current_page()

        def zoom_out():
            if pdf_state['zoom'] > 0.4:
                pdf_state['zoom'] -= 0.2
                render_current_page()

        def zoom_fit():
            pdf_state['zoom'] = 1.0
            render_current_page()

        ttk.Button(pdf_bar, text="[< 上一頁]", width=8, command=prev_page).pack(side=tk.LEFT, padx=2)
        lbl_page_info.pack(side=tk.LEFT, padx=4)
        ttk.Button(pdf_bar, text="[下一頁 >]", width=8, command=next_page).pack(side=tk.LEFT, padx=2)

        ttk.Button(pdf_bar, text="[+ 放大]", width=7, command=zoom_in).pack(side=tk.LEFT, padx=2)
        ttk.Button(pdf_bar, text="[- 縮小]", width=7, command=zoom_out).pack(side=tk.LEFT, padx=2)
        ttk.Button(pdf_bar, text="[最適 100%]", width=9, command=zoom_fit).pack(side=tk.LEFT, padx=2)

        dlg.after(100, render_current_page)

        # ── RIGHT PANE: Adjudication Form (42% width) ────────────────────
        right_container = ttk.Frame(paned, padding="4")
        paned.add(right_container, weight=5)

        # 1. Info Header (Decoupled OCR Confidence & Quality Verdict, Anti-Truncation)
        info_frame = tk.Frame(right_container, bg="#EEF4FB", padx=12, pady=10, relief=tk.GROOVE, borderwidth=1)
        info_frame.pack(fill=tk.X, pady=(0, 6))

        # File Title & Quality Badge
        title_box = tk.Frame(info_frame, bg="#EEF4FB")
        title_box.pack(fill=tk.X)
        is_ng_file = 'NG' in rec['filename'].upper() or 'NG' in str(rec.get('note', '')).upper()
        if is_ng_file:
            lbl_badge = tk.Label(title_box, text=" [品檢: NG不合格] ", font=("微軟正黑體", 9, "bold"), fg="#FFFFFF", bg="#D32F2F", padx=4, pady=1)
            lbl_badge.pack(side=tk.RIGHT, padx=(4, 0))
        else:
            lbl_badge = tk.Label(title_box, text=" [品檢: 正常/待定] ", font=("微軟正黑體", 9, "bold"), fg="#FFFFFF", bg="#2E7D32", padx=4, pady=1)
            lbl_badge.pack(side=tk.RIGHT, padx=(4, 0))

        lbl_fn = tk.Label(title_box, text=f"檔案：{rec['filename']}", font=("Consolas", 9, "bold"), fg="#1F497D", bg="#EEF4FB", anchor='w')
        lbl_fn.pack(side=tk.LEFT, fill=tk.X, expand=True)

        # OCR Recognition Confidence (Explicitly states OCR Text Extraction Fidelity)
        score_color = "#2E7D32" if rec['confidence'] >= 95 else "#C62828"
        lbl_cur_conf = tk.Label(
            info_frame,
            text=f"OCR文字辨識可靠度：{rec['confidence']:.1f}% ({'達標 (>=95%)' if rec['is_accepted'] else '未達95%門檻，需人工覆核'})",
            font=("微軟正黑體", 9, "bold"),
            fg=score_color,
            bg="#EEF4FB",
            anchor='w'
        )
        lbl_cur_conf.pack(fill=tk.X, pady=(3, 1))

        # Extracted Telemetry
        lbl_extract = tk.Label(
            info_frame,
            text=f"已提取數據：料號 [{rec['part_no']}] | 工單 [{rec['order_no']}] | 數量 [{rec['qty']} PC] | 日期 [{rec['date']}]",
            font=("微軟正黑體", 9),
            fg="#333333",
            bg="#EEF4FB",
            anchor='w'
        )
        lbl_extract.pack(fill=tk.X, pady=(1, 4))

        # Diagnostic Scoring Breakdown (Multi-line bullet items, 100% full detail without clipping)
        diag_box = tk.Frame(info_frame, bg="#FFFFFF", padx=8, pady=6, relief=tk.SOLID, borderwidth=1)
        diag_box.pack(fill=tk.X, pady=(2, 0))

        tk.Label(diag_box, text="【OCR 辨識評分與扣分依據明細】", font=("微軟正黑體", 8, "bold"), fg="#1565C0", bg="#FFFFFF", anchor='w').pack(fill=tk.X)

        raw_details = rec.get('conf_details', rec.get('anomaly_desc', ''))
        detail_items = [d.strip() for d in raw_details.split('|') if d.strip()]
        if not detail_items:
            detail_items = [raw_details] if raw_details else ["所有辨識特徵完全合規"]

        diag_labels = []
        for item in detail_items:
            # Color code items based on positive or negative
            if any(k in item for k in ['扣', '未', '缺少']):
                item_fg = "#C62828"
                bullet = "[-]"
            elif any(k in item for k in ['+', '精確', '完整', '有效', '採納', '完全命中']):
                item_fg = "#2E7D32"
                bullet = "[+]"
            else:
                item_fg = "#555555"
                bullet = "[*]"
            l = tk.Label(diag_box, text=f"  {bullet} {item}", font=("微軟正黑體", 8), fg=item_fg, bg="#FFFFFF", anchor='w', wraplength=480, justify=tk.LEFT)
            l.pack(fill=tk.X, pady=1)
            diag_labels.append(l)

        # Dynamic auto-wrap adjustment when dialog or right panel resizes
        def on_info_resize(event):
            wrap_w = max(300, event.width - 35)
            lbl_fn.config(wraplength=wrap_w)
            lbl_extract.config(wraplength=wrap_w)
            for lb in diag_labels:
                lb.config(wraplength=wrap_w)
        info_frame.bind("<Configure>", on_info_resize)

        # 2. Adjudication Form
        form_frame = ttk.LabelFrame(right_container, text=" [2] 人工判定審核輸入與校正 ", padding="10")
        form_frame.pack(fill=tk.BOTH, expand=True, pady=4)

        # Standard Part Combo
        ttk.Label(form_frame, text="核定標準料號：").grid(row=0, column=0, sticky='w', pady=4)
        bom_options = sorted(list(set(DEFAULT_BOM_MAP.keys())))
        var_part = tk.StringVar(value=rec['part_no'])
        cbo_part = ttk.Combobox(form_frame, textvariable=var_part, values=bom_options, width=30, font=("Consolas", 10))
        cbo_part.grid(row=0, column=1, sticky='w', pady=4, padx=5)

        # Part Name Label
        ttk.Label(form_frame, text="品名規格：").grid(row=1, column=0, sticky='w', pady=4)
        lbl_pname = ttk.Label(form_frame, text=rec['part_name'], font=("微軟正黑體", 9, "bold"), foreground="#1F497D")
        lbl_pname.grid(row=1, column=1, sticky='w', pady=4, padx=5)

        def on_part_change(event=None):
            p = var_part.get().strip()
            if p in DEFAULT_BOM_MAP:
                lbl_pname.config(text=DEFAULT_BOM_MAP[p]['name'])
            else:
                lbl_pname.config(text=f"{p} (自訂/手動指定料號)")
        cbo_part.bind("<<ComboboxSelected>>", on_part_change)
        cbo_part.bind("<KeyRelease>", on_part_change)

        # Qty Input
        ttk.Label(form_frame, text="核定進貨數量 (PC)：").grid(row=2, column=0, sticky='w', pady=4)
        var_qty = tk.StringVar(value=str(rec['qty']) if rec['qty'] > 0 else "2")
        ent_qty = ttk.Entry(form_frame, textvariable=var_qty, width=15, font=("Consolas", 10))
        ent_qty.grid(row=2, column=1, sticky='w', pady=4, padx=5)

        # Work Order Input
        ttk.Label(form_frame, text="核定工單/批號：").grid(row=3, column=0, sticky='w', pady=4)
        var_order = tk.StringVar(value=rec['order_no'])
        ent_order = ttk.Entry(form_frame, textvariable=var_order, width=20, font=("Consolas", 10))
        ent_order.grid(row=3, column=1, sticky='w', pady=4, padx=5)

        # Inspection Note Combo
        ttk.Label(form_frame, text="檢驗判定/備註：").grid(row=4, column=0, sticky='w', pady=4)
        if is_ng_file:
            init_note = "NG 不合格 / 退回隔離"
        elif rec['note'] and rec['note'] not in ['正常合格', '由學習庫規則自動確認']:
            init_note = rec['note']
        else:
            init_note = "三次元量測合格 (人工核定)"
        var_note = tk.StringVar(value=init_note)
        cbo_note = ttk.Combobox(form_frame, textvariable=var_note, width=30, font=("微軟正黑體", 9))
        cbo_note['values'] = [
            "三次元量測合格 (人工核定)",
            "正常合格 (進料允收)",
            "特採接收 (附檢驗記錄)",
            "尺寸覆核無誤 (樣品量測)",
            "NG 不合格 / 退回隔離"
        ]
        cbo_note.grid(row=4, column=1, sticky='w', pady=4, padx=5)

        # 3. Active Learning Options
        learn_box = ttk.LabelFrame(form_frame, text=" [AI 主動學習知識庫設定] ", padding="6")
        learn_box.grid(row=5, column=0, columnspan=2, sticky='ew', pady=(10, 4))

        var_learn_alias = tk.BooleanVar(value=True)
        chk_alias = ttk.Checkbutton(
            learn_box,
            text="記錄料號別名映射 (日後相同原檔特徵自動對映至標準料號，100% 採納)",
            variable=var_learn_alias
        )
        chk_alias.pack(anchor='w', pady=2)

        var_learn_order = tk.BooleanVar(value=True)
        chk_order = ttk.Checkbutton(
            learn_box,
            text="記錄工單數量規則 (日後相同工單號碼自動套用此進貨數量，100% 採納)",
            variable=var_learn_order
        )
        chk_order.pack(anchor='w', pady=2)

        def save_adjudication():
            try:
                new_q = int(var_qty.get().strip())
                new_part = var_part.get().strip()
                new_order = var_order.get().strip()
                new_note = var_note.get().strip()

                if not new_part:
                    messagebox.showerror("錯誤", "標準料號不能為空！")
                    return

                # Retrieve BOM metadata
                bom_info = DEFAULT_BOM_MAP.get(new_part, {
                    'name': new_part, 'usage': 1, 'folder': f"{new_part}_{new_part}"
                })

                # Update Record in Memory
                rec['part_no'] = new_part
                rec['part_name'] = bom_info['name']
                rec['folder_name'] = bom_info['folder']
                rec['unit_usage'] = bom_info['usage']
                rec['qty'] = new_q
                rec['order_no'] = new_order
                rec['note'] = new_note
                rec['confidence'] = 100.0
                rec['confidence_str'] = "100.0%"
                rec['is_accepted'] = True
                rec['is_anomaly'] = False
                rec['status_tag'] = "人工已核定 (100%)"
                rec['anomaly_desc'] = "人工審核通過 (寫入學習庫)"

                # Persist to Active Learning Engine
                raw_part_prefix = rec['filename'].split('_')[0]
                self.learning_engine.record_adjudication(
                    filename=rec['filename'],
                    raw_part=raw_part_prefix if var_learn_alias.get() else "",
                    corrected_part=new_part,
                    qty=new_q,
                    note=new_note,
                    add_to_alias=var_learn_alias.get(),
                    order_no=new_order if var_learn_order.get() else None
                )

                self._log(f"[人工判定與學習完成] 檔案 {rec['filename']} -> 核定為 [{new_part} / {new_q} PC]。可靠度升至 100.0%，已儲存至學習庫！")
                dlg.destroy()

                # Re-render UI grids
                self._render_grid()
                self._refresh_learning_tab_data()
                messagebox.showinfo("判定成功", f"檔案：\n{rec['filename']}\n\n已成功人工核定！\n可靠度提升至 100.0% (已採納)，相關特徵已存入自主學習庫。")
            except ValueError:
                messagebox.showerror("錯誤", "數量必須為正整數！")

        def mark_as_invalid():
            msg = (
                f"確定將檔案：\n{rec['filename']}\n\n"
                f"確認為【無效單據 / 刪除不列入計算】？\n\n"
                f"● 標記後進貨數量將設為 0 PC。\n"
                f"● 徹底排除於月度統計與整車齊套計算之外。\n"
                f"● 分類歸檔時將自動移入 [_作廢與無效單據隔離區]。\n"
                f"● 該檔案將記錄至排除名單，日後重新掃描自動維持排除。"
            )
            if messagebox.askyesno("確認排除無效資料", msg):
                rec['is_valid'] = False
                rec['qty'] = 0
                rec['is_accepted'] = True
                rec['is_anomaly'] = False
                rec['status_tag'] = "人工排除 (無效單據)"
                rec['note'] = "作廢/無效單據 (排除不計入進貨)"
                rec['anomaly_desc'] = "人工確認為無效單據，已排除不列入計算"
                rec['folder_name'] = "_作廢與無效單據隔離區"
                self.learning_engine.mark_invalid(rec['filename'])
                self._log(f"[單據排除] 檔案 {rec['filename']} 已標記為無效單據，不列入計算！")
                dlg.destroy()
                self._render_grid()
                messagebox.showinfo("已排除", f"檔案 {rec['filename']} 已成功標記為無效資料，不列入計算！")

        btn_box = ttk.Frame(right_container)
        btn_box.pack(pady=10)
        ttk.Button(btn_box, text="[OK] 確認判定並儲存學習", width=22, command=save_adjudication).pack(side=tk.LEFT, padx=6)
        btn_del = tk.Button(
            btn_box,
            text="[X] 標記為無效單據 (刪除不計)",
            fg="#FFFFFF",
            bg="#C62828",
            activebackground="#B71C1C",
            activeforeground="#FFFFFF",
            font=("微軟正黑體", 9, "bold"),
            padx=8,
            pady=3,
            command=mark_as_invalid
        )
        btn_del.pack(side=tk.LEFT, padx=6)
        ttk.Button(btn_box, text="取消", width=10, command=dlg.destroy).pack(side=tk.LEFT)

    def _action_diagnose_single(self):
        """Single-file deep diagnostic window."""
        f = filedialog.askopenfilename(
            title="選擇待診斷之單一 PDF 檔案",
            filetypes=[("PDF Files", "*.pdf")],
            initialdir=self.var_target.get().strip() or str(self.project_root)
        )
        if not f: return

        if not self.engine:
            self.learning_engine.load()
            bom_mgr = BOMManager(self.var_bom.get().strip() or None, learning_engine=self.learning_engine)
            self.engine = InspectionEngine(bom_mgr, str(self.default_history) if self.default_history.exists() else None, learning_engine=self.learning_engine, debug_mode=True)

        diag = self.engine.diagnose_single_file(f)

        dlg = tk.Toplevel(self)
        dlg.title(f"單檔深度除錯診斷 - {diag['filename']}")
        dlg.geometry("640x520")
        dlg.transient(self)

        txt_diag = tk.Text(dlg, font=("Consolas", 10), wrap=tk.WORD, bg="#F7F9FC", padx=10, pady=10)
        scroll = ttk.Scrollbar(dlg, orient=tk.VERTICAL, command=txt_diag.yview)
        txt_diag.configure(yscrollcommand=scroll.set)
        txt_diag.pack(side=tk.LEFT, fill=tk.BOTH, expand=True)
        scroll.pack(side=tk.RIGHT, fill=tk.Y)

        lines = [
            "=================================================================",
            "              【單一 PDF 進料單據深度診斷與可靠度報告】",
            "=================================================================",
            f"● 檔案名稱：{diag['filename']}",
            f"● 檔案大小：{diag['filesize_kb']} KB | 總頁數：{diag['page_count']} 頁",
            f"● 格式健康度：{'正常合法 PDF' if diag['is_valid_pdf'] else '檔案損毀/異常'}",
            "",
            "【1. 檔名正規表示式解析】",
            f"   - 比對狀態：{'成功吻合標準格式' if diag['regex_success'] else '未吻合標準格式'}",
            f"   - 提取料號：{diag['parsed_part'] or '(無)'}",
            f"   - 提取日期：{diag['parsed_date'] or '(無)'}",
            f"   - 提取工單：{diag['parsed_order'] or '(無)'}",
            "",
            "【2. 原廠 BOM 需求表對照與自主學習規則】",
            f"   - 比對狀態：{'已成功對照 BOM' if diag['bom_matched'] else '未匹配 BOM (歸入待檢)'}",
            f"   - 匹配品名：{diag['bom_name']}",
            f"   - 目標目錄：{diag['bom_folder']}",
            f"   - 單台用量：{diag['bom_usage']} PC",
            "",
            "【3. 進料數量與檢驗判定】",
            f"   - 實質進料數：{diag['qty']} PC",
            f"   - 狀態判定：{diag['note']}",
            "",
            "【4. 頁面內嵌文字偵測】",
            f"   - 是否含數位內嵌文字：{'是 (可直接純文字擷取)' if diag['has_text'] else '否 (為全點陣掃描圖，需OCR識別)'}",
        ]
        if diag['sample_text']:
            lines.append(f"   - 數位文字摘錄：\n     {diag['sample_text']}")

        lines.append("")
        lines.append("【5. 辨識可靠度評估結論 (門檻 >= 95.0%)】")
        if not diag['is_anomaly']:
            lines.append("   ✔ [PASS - 達標] 該單據所有特徵均符合標準 (>=95%)，可直接進行 100% 正確分類與統計。")
        else:
            lines.append("   [!] [需人工審核判定 (<95%)]：")
            for r in diag['anomaly_reasons']:
                lines.append(f"       - {r}")
            lines.append("   排查建議：請在主畫面使用【進行人工審核判定與學習】按鈕，核定後即自動存入學習庫。")

        lines.append("=================================================================")
        txt_diag.insert(tk.END, "\n".join(lines))
        self._log(f"[單檔除錯診斷] 檔案 {diag['filename']} 診斷完成。")

    def _action_execute(self):
        if not self.records:
            messagebox.showwarning("提示", "目前無解析數據，請先點擊掃描！")
            return

        unadjudicated = [r for r in self.records if not r['is_accepted']]
        if unadjudicated:
            msg = (
                f"【注意：發現低於 95% 可靠度之單據】\n\n"
                f"目前尚有 {len(unadjudicated)} 筆單據可靠度低於 95% 且尚未進行人工審核判定！\n\n"
                f"建議您點選【否】返回，切換至【待人工判定審核區 (<95%)】完成審核後再執行。\n\n"
                f"若您現在堅持執行，這些未核定之單據將被隔離放置於各零件目錄或待檢清單中。\n\n"
                f"是否確定直接執行？"
            )
            if not messagebox.askyesno("審核安全警示", msg):
                self.notebook.select(self.tab_under95)
                return

        target = self.var_target.get().strip()
        is_copy = (self.var_mode.get() == "copy")

        msg = f"即將對目錄：\n{target}\n\n執行共 {len(self.records)} 份單據的【分類歸檔】與【統計報表生成】。\n\n操作模式：{'複製保留' if is_copy else '直接移動歸檔'}。\n是否確定執行？"
        if not messagebox.askyesno("確認執行", msg):
            return

        self.btn_scan.config(state=tk.DISABLED)
        self.btn_execute.config(state=tk.DISABLED)
        self.btn_adjudicate.config(state=tk.DISABLED)
        self.var_progress.set(0)
        self._log("\n--- 開始執行分類歸檔與報表產出 ---")

        def worker():
            try:
                def update_prog(cur, tot, text):
                    pct = (cur / tot) * 100 if tot else 0
                    self.var_progress.set(pct)
                    if cur % 15 == 0 or cur == tot:
                        self._log(text)

                excel_path, sum_csv, det_csv = self.engine.execute_classification_and_export(
                    target, self.records, is_copy_mode=is_copy, progress_callback=update_prog
                )
                self.last_excel_path = excel_path
                self.after(0, lambda: self._on_execute_success(excel_path))
            except Exception as e:
                err_detail = traceback.format_exc()
                self._log(f"[執行異常] {err_detail}")
                self.after(0, lambda: messagebox.showerror(
                    "執行失敗",
                    f"產出報表時發生異常：{e}\n\n【詳細日誌已同步寫入磁碟】\n{self.log_file_path}\n\n錯誤追蹤摘要：\n{err_detail[:350]}..."
                ))
            finally:
                self.after(0, lambda: self.btn_scan.config(state=tk.NORMAL))
                self.after(0, lambda: self.btn_execute.config(state=tk.NORMAL))

        threading.Thread(target=worker, daemon=True).start()

    def _on_execute_success(self, excel_path):
        self.btn_open_excel.config(state=tk.NORMAL)
        self.var_progress.set(100)
        self._log(f"\n[成功] 全數檔案分類歸檔與報表產出圓滿完成！")
        self._log(f"● 格式化 Excel 樞紐總表：{excel_path.name}")
        self._log(f"● 各零件資料夾獨立逐筆統計表：已於每個子目錄生成 零件進貨數量統計.txt")
        messagebox.showinfo("執行完成", f"分類與報表產出成功！\n\n已產出格式化 Excel 總表：\n{excel_path}\n\n您可直接點擊【開啟統計 Excel】或【開啟目標目錄】查看！")

    def _action_open_dir(self):
        target = self.var_target.get().strip()
        if os.path.exists(target):
            os.startfile(target)
        else:
            messagebox.showerror("錯誤", "目錄不存在！")

    def _action_open_excel(self):
        if self.last_excel_path and os.path.exists(self.last_excel_path):
            os.startfile(str(self.last_excel_path))
        else:
            target = Path(self.var_target.get().strip())
            cand = list(target.glob("*.xlsx"))
            if cand:
                os.startfile(str(cand[0]))
            else:
                messagebox.showerror("錯誤", "尚未生成 Excel 報表！")


if __name__ == "__main__":
    try:
        app = AppGUI()
        app.mainloop()
    except Exception as e:
        err_msg = traceback.format_exc()
        try:
            base_p = Path(sys.executable).parent if getattr(sys, 'frozen', False) else Path(__file__).parent
            crash_file = base_p / "startup_crash.log"
            with open(crash_file, "w", encoding="utf-8") as cf:
                cf.write(err_msg)
        except:
            pass

        try:
            import tkinter as tk
            from tkinter import messagebox
            root = tk.Tk()
            root.withdraw()
            messagebox.showerror(
                "系統啟動異常 (Startup Error)",
                f"電動車進料檢驗工具啟動失敗：\n{e}\n\n詳細錯誤已記錄至 startup_crash.log\n\n{err_msg[:400]}"
            )
            root.destroy()
        except:
            pass
        sys.exit(1)

