import os
import sys
import tkinter as tk
from tkinter import ttk, messagebox
import threading
from typing import Dict, List, Any, Optional

from ranking_service import MarketRankingService

class MarketRankingDialog(tk.Toplevel):
    """
    台股個股與 ETF 4 大核心財務指標即時排行榜對話框
    1. 殖利率排名 (Dividend Yield)
    2. PE (本益比) 排名 (價值投資低估)
    3. 營收年增率 (YoY) 排名
    4. ROE (股東權益報酬率) 排名
    """
    def __init__(self, parent, on_add_position_callback=None, existing_symbols: Optional[set] = None):
        super().__init__(parent)
        self.parent = parent
        self.on_add_position_callback = on_add_position_callback
        self.existing_symbols = existing_symbols or set()

        self.title("台股與 ETF 市場財務指標排行榜 (4大核心指標)")
        self.geometry("1040x680")
        self.minsize(880, 560)
        self.transient(parent)

        # 嘗試套用更新或主程式專屬 ICON
        try:
            icon_ico = os.path.join(os.path.dirname(os.path.abspath(__file__)), "app_icon.ico")
            if os.path.exists(icon_ico):
                self.iconbitmap(icon_ico)
        except Exception:
            pass

        self.bg_color = "#14141c"
        self.panel_bg = "#1e1e28"
        self.card_bg = "#262636"
        self.text_color = "#ffffff"
        self.muted_text = "#9ca3af"
        self.accent_blue = "#2563eb"
        self.gold_color = "#ffd166"
        self.green_color = "#52c41a"
        self.red_color = "#ff4d4f"

        self.configure(bg=self.bg_color)

        # 當前狀態
        self.current_tab = "yield"  # 'yield', 'pe', 'rev', 'roe'
        self.filter_type_var = tk.StringVar(value="全部 (個股+ETF)")
        self.search_var = tk.StringVar(value="")
        self.limit_var = tk.StringVar(value="50")
        self.status_var = tk.StringVar(value="資料載入中...")

        self.raw_ranked_items: List[Dict[str, Any]] = []

        self.build_ui()
        self.load_data_async(force_refresh=False)

    def build_ui(self):
        # ==========================================
        # 1. 頂部：指標切換標籤列 (4 大指標)
        # ==========================================
        tab_frame = tk.Frame(self, bg=self.panel_bg, height=52)
        tab_frame.pack(fill=tk.X, side=tk.TOP)

        self.tab_buttons: Dict[str, tk.Button] = {}
        tab_defs = [
            ("yield", "[💰 殖利率排名]", "精選全市場高配息率標的 (支援個股與熱門ETF)"),
            ("pe", "[🏷 低本益比排名]", "本益比 4~30 倍合理價值低估績優股 (股價>=10元)"),
            ("rev", "[🚀 營收年增 (YoY) 排名]", "上市公司最新月份營收成長動能爆發排行"),
            ("roe", "[💎 高 ROE 排名]", "股東權益報酬率杜邦推導優選 (高資本獲利力)")
        ]

        for tab_id, tab_label, tooltip in tab_defs:
            btn = tk.Button(
                tab_frame,
                text=tab_label,
                font=("Microsoft JhengHei UI", 10, "bold"),
                bg="#2a2a3a",
                fg="#d1d5db",
                activebackground=self.accent_blue,
                activeforeground="#ffffff",
                relief="flat",
                padx=16,
                pady=10,
                cursor="hand2",
                command=lambda tid=tab_id: self.switch_tab(tid)
            )
            btn.pack(side=tk.LEFT, padx=3, pady=6)
            self.tab_buttons[tab_id] = btn

        # ==========================================
        # 2. 次頂部：篩選與搜尋工具列
        # ==========================================
        tool_frame = tk.Frame(self, bg=self.bg_color, padx=14, pady=8)
        tool_frame.pack(fill=tk.X, side=tk.TOP)

        # 類別篩選
        tk.Label(tool_frame, text="標的範圍:", bg=self.bg_color, fg="#ffffff", font=("Microsoft JhengHei UI", 9, "bold")).pack(side=tk.LEFT, padx=(0, 4))
        self.combo_filter = ttk.Combobox(
            tool_frame,
            textvariable=self.filter_type_var,
            values=["全部 (個股+ETF)", "僅台股個股", "僅熱門 ETF"],
            width=14,
            state="readonly"
        )
        self.combo_filter.pack(side=tk.LEFT, padx=(0, 14))
        self.combo_filter.bind("<<ComboboxSelected>>", lambda e: self.apply_filter_and_render())

        # 筆數上限
        tk.Label(tool_frame, text="顯示前:", bg=self.bg_color, fg="#ffffff", font=("Microsoft JhengHei UI", 9, "bold")).pack(side=tk.LEFT, padx=(0, 4))
        self.combo_limit = ttk.Combobox(
            tool_frame,
            textvariable=self.limit_var,
            values=["30", "50", "80", "100"],
            width=5,
            state="readonly"
        )
        self.combo_limit.pack(side=tk.LEFT, padx=(0, 14))
        self.combo_limit.bind("<<ComboboxSelected>>", lambda e: self.apply_filter_and_render())

        # 即時搜尋框
        tk.Label(tool_frame, text="快速搜尋 (代碼/名稱):", bg=self.bg_color, fg="#ffffff", font=("Microsoft JhengHei UI", 9, "bold")).pack(side=tk.LEFT, padx=(0, 4))
        self.entry_search = tk.Entry(
            tool_frame,
            textvariable=self.search_var,
            bg="#222230",
            fg="#ffffff",
            insertbackground="#ffffff",
            font=("Microsoft JhengHei UI", 9),
            width=16,
            relief="flat"
        )
        self.entry_search.pack(side=tk.LEFT, padx=(0, 14), ipady=3)
        self.entry_search.bind("<KeyRelease>", lambda e: self.apply_filter_and_render())

        # 手動更新按鈕
        self.btn_refresh = tk.Button(
            tool_frame,
            text="[⟳ 同步最新市場排行]",
            font=("Microsoft JhengHei UI", 9, "bold"),
            bg="#2563eb",
            fg="#ffffff",
            activebackground="#1d4ed8",
            activeforeground="#ffffff",
            relief="flat",
            padx=10,
            pady=3,
            cursor="hand2",
            command=self.on_force_refresh
        )
        self.btn_refresh.pack(side=tk.RIGHT)

        # 指標說明橫幅提示
        self.lbl_desc = tk.Label(
            self,
            text="載入中...",
            bg="#181824",
            fg=self.gold_color,
            font=("Microsoft JhengHei UI", 9),
            anchor="w",
            padx=16,
            pady=4
        )
        self.lbl_desc.pack(fill=tk.X, side=tk.TOP)

        # ==========================================
        # 3. 中間：排行榜 Treeview
        # ==========================================
        mid_container = tk.Frame(self, bg=self.bg_color, padx=12, pady=4)
        mid_container.pack(fill=tk.BOTH, expand=True, side=tk.TOP)

        columns = ("rank", "symbol", "name", "type", "key_metric", "price", "sub_info", "in_portfolio")
        self.tree = ttk.Treeview(mid_container, columns=columns, show="headings", selectmode="browse")

        self.tree.heading("rank", text="排名", anchor="center")
        self.tree.heading("symbol", text="代碼", anchor="center")
        self.tree.heading("name", text="名稱", anchor="center")
        self.tree.heading("type", text="商品類別", anchor="center")
        self.tree.heading("key_metric", text="核心指標", anchor="e")
        self.tree.heading("price", text="收盤現價", anchor="e")
        self.tree.heading("sub_info", text="重要指標與財報細節", anchor="w")
        self.tree.heading("in_portfolio", text="庫存狀態", anchor="center")

        self.tree.column("rank", width=55, minwidth=45, anchor="center")
        self.tree.column("symbol", width=80, minwidth=70, anchor="center")
        self.tree.column("name", width=120, minwidth=100, anchor="center")
        self.tree.column("type", width=110, minwidth=90, anchor="center")
        self.tree.column("key_metric", width=110, minwidth=90, anchor="e")
        self.tree.column("price", width=85, minwidth=75, anchor="e")
        self.tree.column("sub_info", width=340, minwidth=220, anchor="w")
        self.tree.column("in_portfolio", width=95, minwidth=85, anchor="center")

        vsb = ttk.Scrollbar(mid_container, orient="vertical", command=self.tree.yview)
        hsb = ttk.Scrollbar(mid_container, orient="horizontal", command=self.tree.xview)
        self.tree.configure(yscrollcommand=vsb.set, xscrollcommand=hsb.set)

        self.tree.grid(row=0, column=0, sticky="nsew")
        vsb.grid(row=0, column=1, sticky="ns")
        hsb.grid(row=1, column=0, sticky="ew")

        mid_container.grid_rowconfigure(0, weight=1)
        mid_container.grid_columnconfigure(0, weight=1)

        # 標籤樣式配置
        self.tree.tag_configure("highlight_yield", foreground=self.gold_color, font=("Microsoft JhengHei UI", 10, "bold"))
        self.tree.tag_configure("highlight_pe", foreground="#38bdf8", font=("Microsoft JhengHei UI", 10, "bold"))
        self.tree.tag_configure("highlight_rev", foreground="#ff4d4f", font=("Microsoft JhengHei UI", 10, "bold"))
        self.tree.tag_configure("highlight_roe", foreground="#a78bfa", font=("Microsoft JhengHei UI", 10, "bold"))
        self.tree.tag_configure("in_hold", foreground="#52c41a")
        self.tree.tag_configure("normal", foreground="#ffffff")

        # 雙擊快捷事件
        self.tree.bind("<Double-1>", self.on_double_click_row)

        # ==========================================
        # 4. 底部：操作列與全域狀態
        # ==========================================
        bottom_frame = tk.Frame(self, bg=self.panel_bg, padx=14, pady=8)
        bottom_frame.pack(fill=tk.X, side=tk.BOTTOM)

        self.lbl_status = tk.Label(bottom_frame, textvariable=self.status_var, bg=self.panel_bg, fg=self.muted_text, font=("Microsoft JhengHei UI", 9))
        self.lbl_status.pack(side=tk.LEFT)

        btn_add = tk.Button(
            bottom_frame,
            text="[➕ 將選中標的加入庫存追蹤]",
            font=("Microsoft JhengHei UI", 10, "bold"),
            bg="#10b981",
            fg="#ffffff",
            activebackground="#059669",
            activeforeground="#ffffff",
            relief="flat",
            padx=14,
            pady=4,
            cursor="hand2",
            command=self.on_add_selected_to_portfolio
        )
        btn_add.pack(side=tk.RIGHT, padx=4)

        btn_close = tk.Button(
            bottom_frame,
            text="關閉",
            font=("Microsoft JhengHei UI", 9),
            bg="#374151",
            fg="#ffffff",
            activebackground="#4b5563",
            activeforeground="#ffffff",
            relief="flat",
            padx=14,
            pady=4,
            cursor="hand2",
            command=self.destroy
        )
        btn_close.pack(side=tk.RIGHT, padx=4)

        self.update_tab_button_styles()

    def update_tab_button_styles(self):
        for tid, btn in self.tab_buttons.items():
            if tid == self.current_tab:
                btn.configure(bg=self.accent_blue, fg="#ffffff")
            else:
                btn.configure(bg="#2a2a3a", fg="#9ca3af")

    def switch_tab(self, tab_id: str):
        if self.current_tab == tab_id:
            return
        self.current_tab = tab_id
        self.update_tab_button_styles()

        # 自動調整標的篩選器（例如營收與 PE/ROE 不適用純 ETF）
        if self.current_tab in ("pe", "rev", "roe"):
            if self.filter_type_var.get() == "僅熱門 ETF":
                self.filter_type_var.set("僅台股個股")
            self.combo_filter.configure(values=["全部 (個股)", "僅台股個股"])
        else:
            self.combo_filter.configure(values=["全部 (個股+ETF)", "僅台股個股", "僅熱門 ETF"])
            if self.filter_type_var.get() not in ("全部 (個股+ETF)", "僅台股個股", "僅熱門 ETF"):
                self.filter_type_var.set("全部 (個股+ETF)")

        self.apply_filter_and_render()

    def load_data_async(self, force_refresh: bool = False):
        # 若已有本地快取，開窗瞬間 0ms 同步秒開渲染
        if not force_refresh:
            cached = MarketRankingService.load_cache()
            if cached and cached.get("stocks") and cached.get("revenues"):
                cached_time_str = cached.get("cached_time_str", "剛剛")
                self.status_var.set(f"數據已就緒 (資料時間: {cached_time_str})")
                self.apply_filter_and_render()
                return

        self.status_var.set("正在獲取證交所全市場最新資料，請稍候...")
        self.btn_refresh.configure(state="disabled")

        def worker():
            try:
                data = MarketRankingService.get_market_data(force_refresh=force_refresh)
                cached_time_str = data.get("cached_time_str", "剛剛")
                self.after(0, lambda: self._on_data_loaded(cached_time_str))
            except Exception as e:
                self.after(0, lambda: self._on_data_error(str(e)))

        threading.Thread(target=worker, daemon=True).start()

    def _on_data_loaded(self, cached_time_str: str):
        self.btn_refresh.configure(state="normal")
        self.status_var.set(f"數據已就緒 (資料時間: {cached_time_str})")
        self.apply_filter_and_render()

    def _on_data_error(self, err_msg: str):
        self.btn_refresh.configure(state="normal")
        self.status_var.set(f"載入失敗: {err_msg}")
        messagebox.showwarning("連線提示", f"抓取市場排行數據失敗: {err_msg}\n將使用本地快取（若存在）。", parent=self)

    def on_force_refresh(self):
        self.load_data_async(force_refresh=True)

    def apply_filter_and_render(self):
        try:
            limit = int(self.limit_var.get())
        except Exception:
            limit = 50

        filter_choice = self.filter_type_var.get()
        if "僅熱門 ETF" in filter_choice:
            filter_code = "etf"
        elif "僅台股個股" in filter_choice:
            filter_code = "stock"
        else:
            filter_code = "all"

        # 依據分頁產生排名原始清單
        if self.current_tab == "yield":
            self.lbl_desc.configure(
                text="【💰 殖利率排名】精選全市場現金股利配息率前列標的。涵蓋上市個股與熱門主流 ETF（可一鍵加入部位追蹤）",
                fg=self.gold_color
            )
            self.tree.heading("key_metric", text="現金殖利率(%)")
            items = MarketRankingService.get_dividend_ranking(filter_type=filter_code, limit=limit)

        elif self.current_tab == "pe":
            self.lbl_desc.configure(
                text="【🏷 低本益比排名】價值投資低估優選（篩選合理本益比 4~30 倍、股價>=10元、排除虧損與一次性膨脹個股）",
                fg="#38bdf8"
            )
            self.tree.heading("key_metric", text="本益比 (PE)")
            items = MarketRankingService.get_pe_ranking(min_pe=4.0, max_pe=30.0, min_price=10.0, limit=limit)

        elif self.current_tab == "rev":
            self.lbl_desc.configure(
                text="【🚀 營收年增率 (YoY) 排名】上市公司最新月份營收爆發排行榜（依去年同月增減幅度由高到低排序）",
                fg="#ff4d4f"
            )
            self.tree.heading("key_metric", text="營收年增率 (YoY)")
            items = MarketRankingService.get_revenue_growth_ranking(limit=limit)

        else: # 'roe'
            self.lbl_desc.configure(
                text="【💎 高 ROE 排名】依金融學杜邦恆等式 ROE=(PB/PE)*100% 精準推導（排除一次性處分土地等虛胖值，專注高資本回報率）",
                fg="#a78bfa"
            )
            self.tree.heading("key_metric", text="股東權益報酬率(ROE)")
            items = MarketRankingService.get_roe_ranking(min_pe=4.0, min_roe=8.0, max_roe=75.0, limit=limit)

        # 關鍵字過濾
        keyword = self.search_var.get().strip().upper()
        if keyword:
            filtered = []
            for it in items:
                sym = it.get("symbol", "").upper()
                nm = it.get("name", "").upper()
                if keyword in sym or keyword in nm:
                    filtered.append(it)
            items = filtered

        self.raw_ranked_items = items
        self.render_tree(items)

    def render_tree(self, items: List[Dict[str, Any]]):
        self.tree.delete(*self.tree.get_children())

        tag_map = {
            "yield": "highlight_yield",
            "pe": "highlight_pe",
            "rev": "highlight_rev",
            "roe": "highlight_roe"
        }
        metric_tag = tag_map.get(self.current_tab, "normal")

        for idx, it in enumerate(items, 1):
            sym = it.get("symbol", "")
            nm = it.get("name", "")
            tp = it.get("type", "個股")
            price_val = it.get("price", 0.0)
            price_str = f"{price_val:.2f}" if price_val > 0 else "-"
            key_metric_str = it.get("highlight_label", "-")
            sub_info_str = it.get("sub_info", "-")

            in_hold = sym in self.existing_symbols
            hold_status = "✓ 已在庫" if in_hold else "- 未持有"

            tags = [metric_tag]
            if in_hold:
                tags.append("in_hold")

            self.tree.insert(
                "",
                "end",
                iid=f"rank_{idx}_{sym}",
                values=(
                    idx,
                    sym,
                    nm,
                    tp,
                    key_metric_str,
                    price_str,
                    sub_info_str,
                    hold_status
                ),
                tags=tuple(tags)
            )

    def get_selected_item(self) -> Optional[Dict[str, Any]]:
        selected = self.tree.focus()
        if not selected:
            return None
        # 從 iid 取得 sym
        parts = selected.split("_")
        if len(parts) >= 3:
            sym = parts[2]
            for it in self.raw_ranked_items:
                if it.get("symbol") == sym:
                    return it
        return None

    def on_double_click_row(self, event):
        item = self.get_selected_item()
        if item:
            self.trigger_add_position(item)

    def on_add_selected_to_portfolio(self):
        item = self.get_selected_item()
        if not item:
            messagebox.showinfo("操作提示", "請先點選列表中要加入的股票或 ETF！", parent=self)
            return
        self.trigger_add_position(item)

    def trigger_add_position(self, item: Dict[str, Any]):
        sym = item.get("symbol", "")
        nm = item.get("name", "")
        is_etf = 1 if item.get("is_etf", False) else 0

        if self.on_add_position_callback:
            self.on_add_position_callback(sym, nm, is_etf)
            # 立即更新該標的在排行榜上的狀態
            self.existing_symbols.add(sym)
            self.apply_filter_and_render()
