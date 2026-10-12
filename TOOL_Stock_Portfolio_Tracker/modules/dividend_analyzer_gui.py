# -*- coding: utf-8 -*-
"""
Stock Portfolio Tracker - 股息收益分析與動態現金流落袋月曆 (dividend_analyzer_gui.py)
參考 WEB_ETF_Portfolio_Analyzer 設計規範與視覺美學：
1. 頂部四大精選 KPI 儀表板卡片 (總市值現值、預估月息/年息、加權殖利率、現金流平穩度/健保診斷)
2. 1~12 月現金流落袋矩陣 (淡旺季月曆柱狀圖 + 變異度 CV 診斷徽章)
3. 月息貢獻分佈圓環圖 (Donut Chart + 息收佔比 Legend)
4. 二代健保單次 2 萬門檻深度診斷 (單次配息金額、安全免扣張數上限、2.11% 扣繳試算、海外債券免稅)
5. 持股配息詳細清單 (支援 ETF/個股/高頻/健保標的分類切換、即時搜尋與點選查看除息歷程)
"""

import os
import sys
import json
import math
import datetime
import tkinter as tk
from tkinter import ttk, messagebox
import threading
from typing import Dict, List, Any, Optional

from database import get_connection, get_all_positions, get_trade_lots
from dividend_service import DividendService

def get_app_dir() -> str:
    if getattr(sys, 'frozen', False):
        return os.path.dirname(os.path.abspath(sys.executable))
    return os.path.dirname(os.path.abspath(__file__))

def load_etf_cache() -> Dict[str, Any]:
    """載入全市場 ETF 中繼快取資料 (含評價月份、頻率、類別、Beta)"""
    app_dir = get_app_dir()
    candidates = [
        os.path.join(app_dir, "config", "etf_live_cache.json"),
        os.path.join(app_dir, "..", "config", "etf_live_cache.json"),
        os.path.join(app_dir, "..", "WEB_ETF_Portfolio_Analyzer", "etf_live_cache.json")
    ]
    for c in candidates:
        if os.path.exists(c):
            try:
                with open(c, "r", encoding="utf-8") as f:
                    return json.load(f)
            except Exception:
                pass
    return {}

class DividendAnalyzerDialog(tk.Toplevel):
    """
    股息股利專用分析頁面 (復刻 WEB_ETF_Portfolio_Analyzer 現金流與健保視覺系統)
    """
    def __init__(self, parent, latest_quotes: Optional[Dict[str, Dict[str, Any]]] = None):
        super().__init__(parent)
        self.parent = parent
        self.latest_quotes = latest_quotes or {}

        self.title("💰 股息收益分析與動態現金流月曆 (Dividend & Cashflow Studio)")
        self.geometry("1240x840")
        self.minsize(1060, 680)
        self.resizable(True, True)
        self.lift()
        self.focus_force()

        # 套用視窗圖標
        try:
            ico_path = os.path.join(get_app_dir(), "app_icon.ico")
            if os.path.exists(ico_path):
                self.iconbitmap(ico_path)
        except Exception:
            pass

        # 色彩調色盤 (Sleek Dark Mode & Vibrant Data Accents)
        self.bg_color = "#12121a"
        self.panel_bg = "#191924"
        self.card_bg = "#20202e"
        self.border_color = "#2e2e42"
        self.text_color = "#ffffff"
        self.muted_text = "#94a3b8"
        self.accent_cyan = "#38bdf8"
        self.accent_emerald = "#34d399"
        self.accent_purple = "#c084fc"
        self.accent_orange = "#fb923c"
        self.accent_rose = "#f43f5e"
        self.accent_blue = "#3b82f6"

        self.configure(bg=self.bg_color)

        self.etf_cache = load_etf_cache()
        self.active_tab = "matrix"  # 'matrix' (現金流月曆), 'donut' (月息圓環圖), 'nhi' (二代健保診斷)
        self.filter_mode = "all"    # 'all', 'etf', 'stock', 'high_freq', 'nhi_alert'
        self.search_var = tk.StringVar(value="")

        self.computed_items: List[Dict[str, Any]] = []
        self.monthly_buckets: List[float] = [0.0] * 12
        self.summary_stats: Dict[str, Any] = {}

        self.build_ui()
        self.refresh_data()

    def build_ui(self):
        # 1. 頂部 Header 列
        header_frame = tk.Frame(self, bg=self.panel_bg, padx=18, pady=12, highlightthickness=1, highlightbackground=self.border_color)
        header_frame.pack(fill=tk.X, side=tk.TOP)

        title_box = tk.Frame(header_frame, bg=self.panel_bg)
        title_box.pack(side=tk.LEFT)

        lbl_title = tk.Label(
            title_box, 
            text="💰 股息收益分析與動態現金流月曆", 
            font=("Microsoft JhengHei UI", 14, "bold"), 
            bg=self.panel_bg, 
            fg=self.text_color
        )
        lbl_title.pack(anchor="w")

        lbl_sub = tk.Label(
            title_box, 
            text="全市場實收除息推算 · 1~12 月淡旺季落袋矩陣 · 二代健保單次 2 萬門檻安全診斷", 
            font=("Microsoft JhengHei UI", 9), 
            bg=self.panel_bg, 
            fg=self.muted_text
        )
        lbl_sub.pack(anchor="w", pady=(2, 0))

        # 右側控制按鈕
        btn_box = tk.Frame(header_frame, bg=self.panel_bg)
        btn_box.pack(side=tk.RIGHT)

        btn_sync = tk.Button(
            btn_box,
            text="↻ 重新整理股息",
            font=("Microsoft JhengHei UI", 9, "bold"),
            bg="#2563eb",
            fg="#ffffff",
            activebackground="#1d4ed8",
            activeforeground="#ffffff",
            relief="flat",
            padx=12,
            pady=5,
            cursor="hand2",
            command=self.on_sync_dividends
        )
        btn_sync.pack(side=tk.LEFT, padx=4)

        btn_export = tk.Button(
            btn_box,
            text="📤 匯出股息報表",
            font=("Microsoft JhengHei UI", 9),
            bg="#2d3748",
            fg="#e2e8f0",
            activebackground="#3b4252",
            activeforeground="#ffffff",
            relief="flat",
            padx=10,
            pady=5,
            cursor="hand2",
            command=self.on_export_report
        )
        btn_export.pack(side=tk.LEFT, padx=4)

        btn_close = tk.Button(
            btn_box,
            text="✕ 關閉",
            font=("Microsoft JhengHei UI", 9),
            bg="#1f2937",
            fg="#9ca3af",
            activebackground="#374151",
            activeforeground="#ffffff",
            relief="flat",
            padx=10,
            pady=5,
            cursor="hand2",
            command=self.destroy
        )
        btn_close.pack(side=tk.LEFT, padx=4)

        # 2. 頂部四大 KPI 儀表板卡片 (復刻 WEB_ETF_Portfolio_Analyzer)
        kpi_container = tk.Frame(self, bg=self.bg_color, padx=16, pady=10)
        kpi_container.pack(fill=tk.X, side=tk.TOP)

        self.kpi_cards = {}
        kpi_defs = [
            ("capital", "預估庫存總現值 (市值)", "NT$ --", "共 -- 檔持股 · -- 張配置", self.accent_cyan),
            ("monthly", "預估每月平均息收 (實證推算)", "NT$ --", "年息約 NT$ -- (實收年化)", self.accent_emerald),
            ("yield", "加權年化殖利率 (平均配息率)", "-- %", "依持股現值加權 · 歷史已領 NT$ --", self.accent_purple),
            ("stability", "現金流平穩度 / 健保狀態", "--", "正在評估淡旺季與補充保費...", self.accent_orange)
        ]

        for i, (k_id, k_title, k_init_val, k_init_sub, k_color) in enumerate(kpi_defs):
            card = tk.Frame(kpi_container, bg=self.card_bg, padx=14, pady=10, relief="solid", bd=1, highlightthickness=1, highlightbackground=self.border_color)
            card.pack(side=tk.LEFT, fill=tk.BOTH, expand=True, padx=4)

            lbl_t = tk.Label(card, text=k_title, font=("Microsoft JhengHei UI", 8, "bold"), bg=self.card_bg, fg=self.muted_text)
            lbl_t.pack(anchor="w")

            lbl_v = tk.Label(card, text=k_init_val, font=("Microsoft JhengHei UI", 16, "bold"), bg=self.card_bg, fg=k_color)
            lbl_v.pack(anchor="w", pady=(2, 2))

            lbl_s = tk.Label(card, text=k_init_sub, font=("Microsoft JhengHei UI", 8), bg=self.card_bg, fg=self.muted_text)
            lbl_s.pack(anchor="w")

            self.kpi_cards[k_id] = (lbl_v, lbl_s, k_color)

        # 3. 中間區域：標籤切換與圖表工作區 (Cashflow Matrix / Donut / NHI)
        mid_container = tk.Frame(self, bg=self.bg_color, padx=16, pady=4)
        mid_container.pack(fill=tk.X, side=tk.TOP)

        # 切換標籤列
        tab_bar = tk.Frame(mid_container, bg=self.bg_color)
        tab_bar.pack(fill=tk.X, side=tk.TOP, pady=(0, 6))

        self.subtab_buttons = {}
        subtabs = [
            ("matrix", "📅 1~12 月現金流落袋矩陣 (淡旺季月曆)"),
            ("donut", "🍩 月息貢獻佔比圓環圖 (Donut Chart)"),
            ("nhi", "🛡️ 二代健保單次 2 萬門檻深度診斷 (免稅/扣費試算)")
        ]
        for tid, ttext in subtabs:
            b = tk.Button(
                tab_bar,
                text=ttext,
                font=("Microsoft JhengHei UI", 9, "bold"),
                bg="#232332" if tid != self.active_tab else "#2563eb",
                fg="#ffffff",
                activebackground="#1d4ed8",
                activeforeground="#ffffff",
                relief="flat",
                padx=14,
                pady=6,
                cursor="hand2",
                command=lambda x=tid: self.switch_view_tab(x)
            )
            b.pack(side=tk.LEFT, padx=3)
            self.subtab_buttons[tid] = b

        # 視圖面板容器 (固定高度 230px，提供圖表與診斷呈現)
        self.visual_panel = tk.Frame(mid_container, bg=self.card_bg, height=230, highlightthickness=1, highlightbackground=self.border_color)
        self.visual_panel.pack(fill=tk.X, side=tk.TOP)
        self.visual_panel.pack_propagate(False)

        # 4. 下方區域：持股配息詳細清單工具列與表格
        list_container = tk.Frame(self, bg=self.bg_color, padx=16, pady=8)
        list_container.pack(fill=tk.BOTH, expand=True, side=tk.TOP)

        list_tool = tk.Frame(list_container, bg=self.bg_color)
        list_tool.pack(fill=tk.X, side=tk.TOP, pady=(0, 6))

        lbl_list_title = tk.Label(list_tool, text="標的配息明細與權重分佈", font=("Microsoft JhengHei UI", 10, "bold"), bg=self.bg_color, fg="#ffffff")
        lbl_list_title.pack(side=tk.LEFT, padx=(0, 10))

        # 篩選按鈕群組
        self.filter_buttons = {}
        filters = [
            ("all", "全部標的"),
            ("etf", "僅看 ETF"),
            ("stock", "僅看個股"),
            ("high_freq", "月配/季配高頻"),
            ("nhi_alert", "⚠️ 二代健保破 2 萬標的")
        ]
        for fid, ftext in filters:
            fb = tk.Button(
                list_tool,
                text=ftext,
                font=("Microsoft JhengHei UI", 8),
                bg="#232332" if fid != self.filter_mode else "#3b82f6",
                fg="#d1d5db" if fid != self.filter_mode else "#ffffff",
                relief="flat",
                padx=9,
                pady=3,
                cursor="hand2",
                command=lambda x=fid: self.set_filter_mode(x)
            )
            fb.pack(side=tk.LEFT, padx=2)
            self.filter_buttons[fid] = fb

        # 搜尋框
        search_box = tk.Frame(list_tool, bg=self.bg_color)
        search_box.pack(side=tk.RIGHT)

        tk.Label(search_box, text="🔍 搜尋代號/名稱:", font=("Microsoft JhengHei UI", 8), bg=self.bg_color, fg=self.muted_text).pack(side=tk.LEFT, padx=2)
        search_entry = tk.Entry(search_box, textvariable=self.search_var, font=("Microsoft JhengHei UI", 8), bg="#1c1c28", fg="#ffffff", insertbackground="#ffffff", relief="flat", width=14)
        search_entry.pack(side=tk.LEFT, padx=2, ipady=2)
        self.search_var.trace("w", lambda *args: self.render_table())

        # 表格 (Treeview)
        table_frame = tk.Frame(list_container, bg=self.panel_bg)
        table_frame.pack(fill=tk.BOTH, expand=True, side=tk.TOP)

        self.columns = [
            ("symbol", "代號", 65, "center"),
            ("name", "名稱", 95, "w"),
            ("category", "類別", 70, "center"),
            ("price", "最新市價", 75, "e"),
            ("shares_lots", "持有規模", 100, "center"),
            ("market_val", "庫存現值", 95, "e"),
            ("val_weight", "現值佔比", 70, "e"),
            ("single_amt", "單期每股股息", 90, "e"),
            ("annual_div", "全年每股股息", 90, "e"),
            ("yearly_est", "預估年息總額", 95, "e"),
            ("monthly_est", "預估月息", 85, "e"),
            ("div_contrib", "息收貢獻", 70, "e"),
            ("yield_rate", "年化殖利率", 80, "e"),
            ("frequency", "配息頻率", 75, "center"),
            ("payout_months", "發放月份", 95, "center"),
            ("nhi_single", "單次配息額", 85, "e"),
            ("nhi_status", "二代健保狀態", 105, "center"),
            ("safe_lots", "免扣繳上限", 85, "center"),
            ("hist_received", "累計已領股息", 95, "e")
        ]

        style = ttk.Style(self)
        style.theme_use("clam")
        style.configure(
            "DividendTree.Treeview",
            background="#161622",
            foreground="#f0f2f8",
            fieldbackground="#161622",
            rowheight=26,
            font=("Microsoft JhengHei UI", 9)
        )
        style.configure(
            "DividendTree.Treeview.Heading",
            background="#202030",
            foreground="#93c5fd",
            font=("Microsoft JhengHei UI", 9, "bold"),
            relief="flat"
        )
        style.map("DividendTree.Treeview.Heading", background=[('active', '#2d3748')])
        style.map("DividendTree.Treeview", background=[('selected', '#2563eb')], foreground=[('selected', '#ffffff')])

        self.tree = ttk.Treeview(
            table_frame,
            columns=[col[0] for col in self.columns],
            show="headings",
            style="DividendTree.Treeview",
            selectmode="browse"
        )

        for col_id, title, w, align in self.columns:
            self.tree.heading(col_id, text=title, command=lambda c=col_id: self.sort_table_column(c))
            self.tree.column(col_id, width=w, minwidth=50, anchor=align)

        # 滾動條
        v_sb = ttk.Scrollbar(table_frame, orient="vertical", command=self.tree.yview)
        h_sb = ttk.Scrollbar(table_frame, orient="horizontal", command=self.tree.xview)
        self.tree.configure(yscrollcommand=v_sb.set, xscrollcommand=h_sb.set)

        self.tree.pack(side=tk.LEFT, fill=tk.BOTH, expand=True)
        v_sb.pack(side=tk.RIGHT, fill=tk.Y)
        h_sb.pack(side=tk.BOTTOM, fill=tk.X)

        self.tree.bind("<Double-1>", self.on_item_double_click)

        # 標記色彩 Tag
        self.tree.tag_configure("nhi_alert", foreground="#f87171", background="#2a1618")
        self.tree.tag_configure("nhi_safe", foreground="#4ade80")
        self.tree.tag_configure("nhi_exempt", foreground="#38bdf8")
        self.tree.tag_configure("high_yield", font=("Microsoft JhengHei UI", 9, "bold"))

        # 底部狀態列
        self.status_bar = tk.Label(self, text="就緒", bg=self.panel_bg, fg=self.muted_text, font=("Microsoft JhengHei UI", 8), anchor="w", padx=14, pady=3)
        self.status_bar.pack(side=tk.BOTTOM, fill=tk.X)

    def refresh_data(self):
        """讀取本地資料庫持股與股息快取，執行綜合演算"""
        positions = get_all_positions()
        if not positions:
            self.status_bar.configure(text="目前資料庫無任何持股紀錄")
            return

        self.computed_items = []
        self.monthly_buckets = [0.0] * 12
        total_market_val = 0.0
        total_annual_div = 0.0
        total_shares_count = 0
        total_lots_count = 0.0
        nhi_alert_count = 0
        nhi_exempt_count = 0

        # 1. 逐檔演算
        for p in positions:
            sym = p['symbol']
            name = p['name']
            shares = float(p['shares'])
            is_etf = int(p.get('is_etf', 0))
            
            # 取得即時價
            quote = self.latest_quotes.get(sym, {})
            price = float(quote.get("current_price", quote.get("price", p.get("cost_price", 0.0))))
            market_val = shares * price

            # 取得股息資料 (0ms 極速讀取 SQLite)
            div_info = DividendService.get_cached_or_db_dividend(sym)
            annual_div = float(div_info.get("annual_div", 0.0))
            single_div = float(div_info.get("single_amt", 0.0))
            freq = div_info.get("frequency", "")
            raw_events = div_info.get("raw_events", [])
            hist_received = float(div_info.get("hist_div_received", 0.0))

            # 判定標的分類與發放月份 (對齊 WEB_ETF_Portfolio_Analyzer)
            etf_meta = self.etf_cache.get(sym, {})
            category = etf_meta.get("category", "")
            if not category:
                if is_etf:
                    category = "bond" if ("債" in name or "B" in sym) else "stock"
                else:
                    category = "single_stock"

            is_bond = (category == "bond")
            cat_display = "債券型" if is_bond else ("ETF" if is_etf else "一般個股")

            # 判定發放月份
            payout_months = etf_meta.get("payout_months", [])
            if not payout_months:
                if raw_events:
                    months = set()
                    for ev in raw_events:
                        try:
                            m = int(ev[0].split('-')[1])
                            months.add(m)
                        except Exception:
                            pass
                    payout_months = sorted(list(months))

            if not payout_months:
                if "月" in freq:
                    payout_months = list(range(1, 13))
                elif "季" in freq:
                    payout_months = [3, 6, 9, 12]
                elif "半年" in freq:
                    payout_months = [7, 11]
                else:
                    payout_months = [8]

            p_count = len(payout_months) if payout_months else 1
            per_payout = annual_div / p_count if p_count > 0 else annual_div
            if single_div > 0 and abs(single_div * p_count - annual_div) < 0.5:
                per_payout = single_div

            # 總金額與月息
            yearly_div_val = shares * annual_div
            monthly_div_val = yearly_div_val / 12.0
            yield_rate = (annual_div / price * 100.0) if price > 0 else 0.0

            # 記入 1~12 月現金流落袋桶
            single_payout_total = shares * per_payout
            for m in payout_months:
                if 1 <= m <= 12:
                    self.monthly_buckets[m - 1] += single_payout_total

            # 二代健保門檻診斷 (單次 2 萬門檻，2.11% 補充保費)
            # 債券型 ETF 海外利息所得 100% 免扣二代健保
            if is_bond:
                nhi_status_text = "🛡️ 100% 免扣繳"
                nhi_tag = "nhi_exempt"
                safe_max_lots_str = "無限 (免徵)"
                nhi_exempt_count += 1
            else:
                if single_payout_total >= 20000.0:
                    nhi_fee = round(single_payout_total * 0.0211)
                    nhi_status_text = f"⚠️ 破2萬 (扣NT${nhi_fee:,})"
                    nhi_tag = "nhi_alert"
                    nhi_alert_count += 1
                else:
                    nhi_status_text = "🛡️ 安全免扣"
                    nhi_tag = "nhi_safe"

                # 免扣上限張數
                if per_payout > 0:
                    safe_shares = 19999.0 / per_payout
                    safe_lots = safe_shares / 1000.0
                    curr_lots = shares / 1000.0
                    safe_max_lots_str = f"上限{safe_lots:.1f}張 (餘{max(0.0, safe_lots - curr_lots):.1f}張)"
                else:
                    safe_max_lots_str = "無配息"

            int_lots = int(shares // 1000)
            odd_shares = int(shares % 1000)
            shares_lots_str = f"{int_lots}張 + {odd_shares}股" if odd_shares > 0 else f"{int_lots}張"

            total_market_val += market_val
            total_annual_div += yearly_div_val
            total_shares_count += int(shares)
            total_lots_count += shares / 1000.0

            self.computed_items.append({
                "symbol": sym,
                "name": name,
                "category": cat_display,
                "is_bond": is_bond,
                "is_etf": is_etf,
                "price": price,
                "shares": shares,
                "shares_lots_str": shares_lots_str,
                "market_val": market_val,
                "val_weight": 0.0,  # 後續統一算
                "single_amt": per_payout,
                "annual_div": annual_div,
                "yearly_est": yearly_div_val,
                "monthly_est": monthly_div_val,
                "div_contrib": 0.0, # 後續統一算
                "yield_rate": yield_rate,
                "frequency": freq or ("月配" if len(payout_months)==12 else "季配" if len(payout_months)==4 else "年配"),
                "payout_months": payout_months,
                "payout_months_str": ", ".join(f"{m}月" for m in payout_months),
                "nhi_single": single_payout_total,
                "nhi_status": nhi_status_text,
                "nhi_tag": nhi_tag,
                "safe_lots_str": safe_max_lots_str,
                "hist_received": hist_received,
                "raw_events": raw_events
            })

        # 2. 計算加權佔比
        for it in self.computed_items:
            it["val_weight"] = (it["market_val"] / total_market_val * 100.0) if total_market_val > 0 else 0.0
            it["div_contrib"] = (it["yearly_est"] / total_annual_div * 100.0) if total_annual_div > 0 else 0.0

        # 加權平均殖利率
        weighted_yield = (total_annual_div / total_market_val * 100.0) if total_market_val > 0 else 0.0
        avg_monthly_div = total_annual_div / 12.0
        hist_total_div = sum(it["hist_received"] for it in self.computed_items)

        # 現金流變異度 CV 診斷
        avg_bucket = sum(self.monthly_buckets) / 12.0
        variance = sum((b - avg_bucket) ** 2 for b in self.monthly_buckets) / 12.0
        std_dev = math.sqrt(variance)
        cv = (std_dev / avg_bucket * 100.0) if avg_bucket > 0 else 0.0

        if cv < 15.0:
            stability_str = f"極度均衡 (CV {cv:.1f}%)"
            stability_desc = "各月份息收起伏小於15%，完美月月領現金流！"
            stab_color = self.accent_emerald
        elif cv < 35.0:
            stability_str = f"次均衡 (CV {cv:.1f}%)"
            stability_desc = "受季配息發放影響有些微起伏，整體平穩。"
            stab_color = self.accent_cyan
        else:
            stability_str = f"淡旺季顯著 (CV {cv:.1f}%)"
            stability_desc = "配息高度集中於除息旺季 (7~9月)，起伏較大。"
            stab_color = self.accent_orange

        self.summary_stats = {
            "total_market_val": total_market_val,
            "total_annual_div": total_annual_div,
            "avg_monthly_div": avg_monthly_div,
            "weighted_yield": weighted_yield,
            "total_lots": total_lots_count,
            "total_shares": total_shares_count,
            "hist_total_div": hist_total_div,
            "cv": cv,
            "stability_str": stability_str,
            "stability_desc": stability_desc,
            "stab_color": stab_color,
            "nhi_alert_count": nhi_alert_count,
            "nhi_exempt_count": nhi_exempt_count,
            "stock_count": len(positions)
        }

        # 3. 更新頂部四大 KPI 卡片
        self.kpi_cards["capital"][0].configure(text=f"NT$ {round(total_market_val):,}")
        self.kpi_cards["capital"][1].configure(text=f"共 {len(positions)} 檔持股 · {total_lots_count:.1f} 張 ({total_shares_count:,} 股)")

        self.kpi_cards["monthly"][0].configure(text=f"NT$ {round(avg_monthly_div):,}")
        self.kpi_cards["monthly"][1].configure(text=f"年息約 NT$ {round(total_annual_div):,} (實收年化)")

        self.kpi_cards["yield"][0].configure(text=f"{weighted_yield:.2f} %")
        self.kpi_cards["yield"][1].configure(text=f"依現值加權 · 歷史已領 NT$ {round(hist_total_div):,}")

        self.kpi_cards["stability"][0].configure(text=stability_str, fg=stab_color)
        nhi_subtext = f"{stability_desc} (⚠️ {nhi_alert_count} 檔單次破2萬)" if nhi_alert_count > 0 else f"{stability_desc} (🛡️ 全數安全未達健保門檻)"
        self.kpi_cards["stability"][1].configure(text=nhi_subtext)

        # 4. 繪製中央視覺圖表與渲染表格
        self.render_visual_tab()
        self.render_table()
        self.status_bar.configure(text=f"✔ 資料已最新！加權殖利率 {weighted_yield:.2f}% · 預估年息 NT$ {round(total_annual_div):,} · 1~12 月現金流模型已完成推算")

    def switch_view_tab(self, tab_id: str):
        self.active_tab = tab_id
        for tid, b in self.subtab_buttons.items():
            if tid == tab_id:
                b.configure(bg="#2563eb", fg="#ffffff")
            else:
                b.configure(bg="#232332", fg="#ffffff")
        self.render_visual_tab()

    def set_filter_mode(self, mode_id: str):
        self.filter_mode = mode_id
        for fid, b in self.filter_buttons.items():
            if fid == mode_id:
                b.configure(bg="#3b82f6", fg="#ffffff")
            else:
                b.configure(bg="#232332", fg="#d1d5db")
        self.render_table()

    def render_visual_tab(self):
        """依據 active_tab 渲染中央視覺面板"""
        for widget in self.visual_panel.winfo_children():
            widget.destroy()

        if self.active_tab == "matrix":
            self.draw_monthly_matrix_canvas()
        elif self.active_tab == "donut":
            self.draw_donut_chart_canvas()
        elif self.active_tab == "nhi":
            self.draw_nhi_diagnostic_panel()

    def draw_monthly_matrix_canvas(self):
        """繪製 1~12 月現金流落袋柱狀圖 (復刻 WEB_ETF_Portfolio_Analyzer)"""
        top_bar = tk.Frame(self.visual_panel, bg=self.card_bg, padx=12, pady=4)
        top_bar.pack(fill=tk.X, side=tk.TOP)

        tk.Label(
            top_bar, 
            text="📅 1~12 月現金流落袋矩陣 (淡旺季月曆)", 
            font=("Microsoft JhengHei UI", 10, "bold"), 
            bg=self.card_bg, 
            fg="#ffffff"
        ).pack(side=tk.LEFT)

        badge_txt = f"● {self.summary_stats.get('stability_str', '')} — {self.summary_stats.get('stability_desc', '')}"
        badge_lbl = tk.Label(
            top_bar, 
            text=badge_txt, 
            font=("Microsoft JhengHei UI", 8, "bold"), 
            bg="#162e24" if self.summary_stats.get("cv", 99) < 25 else "#2e2416", 
            fg=self.summary_stats.get("stab_color", self.accent_emerald),
            padx=8, pady=2
        )
        badge_lbl.pack(side=tk.RIGHT)

        canvas = tk.Canvas(self.visual_panel, bg=self.card_bg, highlightthickness=0, height=185)
        canvas.pack(fill=tk.BOTH, expand=True, padx=8, pady=(0, 6))

        self.update_idletasks()
        w = canvas.winfo_width()
        if w < 100:
            w = 1180
        h = 185

        max_val = max(self.monthly_buckets) if any(self.monthly_buckets) else 1.0
        if max_val <= 0:
            max_val = 1.0

        bar_width = (w - 140) / 12.0
        margin_left = 60
        chart_bottom = h - 35
        chart_top = 25
        chart_height = chart_bottom - chart_top

        # 背景格線與刻度 (Max & Half)
        canvas.create_line(margin_left, chart_bottom, w - 40, chart_bottom, fill="#2e2e42", width=1)
        canvas.create_line(margin_left, chart_top, w - 40, chart_top, fill="#202030", width=1, dash=(3, 3))
        canvas.create_line(margin_left, chart_top + chart_height / 2, w - 40, chart_top + chart_height / 2, fill="#202030", width=1, dash=(3, 3))

        canvas.create_text(margin_left - 8, chart_top, text=f"${round(max_val):,}", fill="#64748b", font=("Microsoft JhengHei UI", 7), anchor="e")
        canvas.create_text(margin_left - 8, chart_top + chart_height / 2, text=f"${round(max_val/2):,}", fill="#64748b", font=("Microsoft JhengHei UI", 7), anchor="e")
        canvas.create_text(margin_left - 8, chart_bottom, text="$0", fill="#64748b", font=("Microsoft JhengHei UI", 7), anchor="e")

        curr_month = datetime.date.today().month

        # 繪製 12 根柱狀
        for i, val in enumerate(self.monthly_buckets):
            x0 = margin_left + i * bar_width + 8
            x1 = margin_left + (i + 1) * bar_width - 8
            val_h = (val / max_val) * chart_height
            y0 = chart_bottom - max(4.0, val_h)
            y1 = chart_bottom

            is_cur = (i + 1 == curr_month)
            # 色彩漸層：高於平均值者更鮮明
            avg_val = sum(self.monthly_buckets) / 12.0
            if val >= avg_val * 1.3:
                bar_color = "#34d399"  # 旺季 Emerald
            elif val > 0:
                bar_color = "#38bdf8"  # 平季 Cyan
            else:
                bar_color = "#2a2a3e"  # 0 元暗灰

            if is_cur:
                bar_color = "#fb923c"  # 本月高亮 Amber

            # 柱身
            canvas.create_rectangle(x0, y0, x1, y1, fill=bar_color, outline="", width=0)

            # 金額文字
            if val > 0:
                amt_str = f"${round(val):,}" if val < 100000 else f"${val/10000:.1f}萬"
                canvas.create_text((x0 + x1) / 2, y0 - 8, text=amt_str, fill="#e2e8f0", font=("Microsoft JhengHei UI", 8, "bold"))

            # 月份標籤
            m_txt = f"{i+1}月" + ("\n(本月)" if is_cur else "")
            canvas.create_text((x0 + x1) / 2, chart_bottom + 14, text=m_txt, fill="#fb923c" if is_cur else "#94a3b8", font=("Microsoft JhengHei UI", 8, "bold" if is_cur else "normal"))

    def draw_donut_chart_canvas(self):
        """繪製各標的月息貢獻圓環圖 (Donut Chart)"""
        top_bar = tk.Frame(self.visual_panel, bg=self.card_bg, padx=12, pady=4)
        top_bar.pack(fill=tk.X, side=tk.TOP)

        tk.Label(
            top_bar, 
            text="🍩 月息貢獻佔比圓環圖 (Donut Income Distribution)", 
            font=("Microsoft JhengHei UI", 10, "bold"), 
            bg=self.card_bg, 
            fg="#ffffff"
        ).pack(side=tk.LEFT)

        container = tk.Frame(self.visual_panel, bg=self.card_bg)
        container.pack(fill=tk.BOTH, expand=True, padx=12, pady=4)

        canvas = tk.Canvas(container, bg=self.card_bg, highlightthickness=0, width=280, height=185)
        canvas.pack(side=tk.LEFT, padx=16)

        legend_frame = tk.Frame(container, bg=self.card_bg)
        legend_frame.pack(side=tk.LEFT, fill=tk.BOTH, expand=True, padx=8)

        total_div = self.summary_stats.get("total_annual_div", 0.0)
        avg_monthly = self.summary_stats.get("avg_monthly_div", 0.0)

        # 排序取前 8 大息收標的
        sorted_items = sorted(self.computed_items, key=lambda x: x["yearly_est"], reverse=True)
        colors = ["#38bdf8", "#34d399", "#c084fc", "#fb923c", "#f43f5e", "#fbbf24", "#60a5fa", "#a78bfa", "#64748b"]

        cx, cy, r_out, r_in = 135, 92, 80, 52
        start_angle = 90.0

        if total_div <= 0:
            canvas.create_oval(cx - r_out, cy - r_out, cx + r_out, cy + r_out, outline=self.border_color, width=2)
            canvas.create_text(cx, cy, text="無配息數據", fill=self.muted_text, font=("Microsoft JhengHei UI", 9))
            return

        for idx, it in enumerate(sorted_items[:8]):
            pct = it["yearly_est"] / total_div
            extent = pct * 360.0
            c = colors[idx % len(colors)]

            # 繪製扇形外圈
            canvas.create_arc(cx - r_out, cy - r_out, cx + r_out, cy + r_out, start=start_angle, extent=-extent, fill=c, outline=self.card_bg, width=2)
            start_angle -= extent

        # 挖空中心形成 Donut
        canvas.create_oval(cx - r_in, cy - r_in, cx + r_in, cy + r_in, fill=self.card_bg, outline=self.card_bg)
        canvas.create_text(cx, cy - 8, text="預估每月總息", fill=self.muted_text, font=("Microsoft JhengHei UI", 8))
        canvas.create_text(cx, cy + 10, text=f"${round(avg_monthly):,}", fill=self.accent_emerald, font=("Microsoft JhengHei UI", 11, "bold"))

        # 右側 Legend
        legend_cols = tk.Frame(legend_frame, bg=self.card_bg)
        legend_cols.pack(fill=tk.BOTH, expand=True)

        for idx, it in enumerate(sorted_items[:8]):
            c = colors[idx % len(colors)]
            row_f = tk.Frame(legend_cols, bg=self.card_bg)
            row_f.pack(fill=tk.X, pady=1)

            # 色塊點
            canvas_dot = tk.Canvas(row_f, bg=self.card_bg, width=12, height=12, highlightthickness=0)
            canvas_dot.pack(side=tk.LEFT, padx=(0, 6))
            canvas_dot.create_oval(2, 2, 10, 10, fill=c, outline="")

            # 代號與名稱
            tk.Label(row_f, text=f"{it['symbol']} {it['name']}", font=("Microsoft JhengHei UI", 8, "bold"), bg=self.card_bg, fg="#ffffff", width=15, anchor="w").pack(side=tk.LEFT)
            # 月息與貢獻比
            tk.Label(row_f, text=f"月收 NT$ {round(it['monthly_est']):,}", font=("Microsoft JhengHei UI", 8), bg=self.card_bg, fg=self.accent_emerald, width=14, anchor="e").pack(side=tk.LEFT)
            tk.Label(row_f, text=f"({it['div_contrib']:.1f}%)", font=("Microsoft JhengHei UI", 8, "bold"), bg=self.card_bg, fg=c, width=8, anchor="e").pack(side=tk.LEFT)

    def draw_nhi_diagnostic_panel(self):
        """二代健保單次 2 萬門檻深度診斷區 (完全復刻 WEB_ETF_Portfolio_Analyzer)"""
        top_bar = tk.Frame(self.visual_panel, bg=self.card_bg, padx=14, pady=8)
        top_bar.pack(fill=tk.X, side=tk.TOP)

        tk.Label(
            top_bar, 
            text="🛡️ 二代健保單次 2 萬門檻安全診斷 (全民健保補充保費 2.11% 規避指南)", 
            font=("Microsoft JhengHei UI", 10, "bold"), 
            bg=self.card_bg, 
            fg="#ffffff"
        ).pack(side=tk.LEFT)

        alert_n = self.summary_stats.get("nhi_alert_count", 0)
        exempt_n = self.summary_stats.get("nhi_exempt_count", 0)

        badge_txt = f"⚠️ {alert_n} 檔超過 2 萬課稅門檻" if alert_n > 0 else "🛡️ 目前所有標的全數安全未達扣繳門檻"
        badge_lbl = tk.Label(
            top_bar, 
            text=badge_txt, 
            font=("Microsoft JhengHei UI", 8, "bold"), 
            bg="#2e1618" if alert_n > 0 else "#162e24", 
            fg=self.accent_rose if alert_n > 0 else self.accent_emerald,
            padx=8, pady=2
        )
        badge_lbl.pack(side=tk.RIGHT)

        # 條列三大法規說明與持股拆解卡片
        cards_f = tk.Frame(self.visual_panel, bg=self.card_bg, padx=14, pady=4)
        cards_f.pack(fill=tk.BOTH, expand=True)

        left_info = tk.Frame(cards_f, bg="#181824", padx=12, pady=10, relief="solid", bd=1, highlightthickness=1, highlightbackground=self.border_color)
        left_info.pack(side=tk.LEFT, fill=tk.BOTH, expand=True, padx=(0, 6))

        tk.Label(left_info, text="💡 二代健保補充保費法規精華:", font=("Microsoft JhengHei UI", 9, "bold"), bg="#181824", fg=self.accent_cyan).pack(anchor="w")
        notes = [
            "• 扣繳門檻：單檔、單次現金股利發放總額達 NT$ 20,000 元以上者，由券商/股務自動預扣 2.11% 補充保費。",
            "• 月配息 / 季配息優勢：高頻配息標的將全年股利拆成 12 次或 4 次發放，大幅拉高免扣張數上限 (例如 00929 上限破百張)！",
            "• 債券型 ETF 100% 免徵：投資海外債券 (如美債 ETF) 之配息屬境外海外所得，依法全免扣二代健保，亦免計入綜所稅！"
        ]
        for nt in notes:
            tk.Label(left_info, text=nt, font=("Microsoft JhengHei UI", 8), bg="#181824", fg="#cbd5e1", wraplength=480, justify="left").pack(anchor="w", pady=(2, 0))

        right_list = tk.Frame(cards_f, bg="#181824", padx=12, pady=10, relief="solid", bd=1, highlightthickness=1, highlightbackground=self.border_color)
        right_list.pack(side=tk.LEFT, fill=tk.BOTH, expand=True, padx=(6, 0))

        tk.Label(right_list, text="📋 現有持股安全門檻診斷摘要:", font=("Microsoft JhengHei UI", 9, "bold"), bg="#181824", fg=self.accent_orange).pack(anchor="w")
        
        # 列出最接近或已超過 2 萬的標的
        sorted_by_nhi = sorted(self.computed_items, key=lambda x: x["nhi_single"], reverse=True)
        for it in sorted_by_nhi[:4]:
            rf = tk.Frame(right_list, bg="#181824")
            rf.pack(fill=tk.X, pady=2)

            t_col = self.accent_rose if it["nhi_tag"] == "nhi_alert" else (self.accent_cyan if it["nhi_tag"] == "nhi_exempt" else self.accent_emerald)
            tk.Label(rf, text=f"• {it['symbol']} {it['name']}:", font=("Microsoft JhengHei UI", 8, "bold"), bg="#181824", fg="#ffffff", width=14, anchor="w").pack(side=tk.LEFT)
            tk.Label(rf, text=f"單次 NT$ {round(it['nhi_single']):,}", font=("Microsoft JhengHei UI", 8), bg="#181824", fg=t_col, width=12, anchor="e").pack(side=tk.LEFT)
            tk.Label(rf, text=f"{it['nhi_status']} ({it['safe_lots_str']})", font=("Microsoft JhengHei UI", 8), bg="#181824", fg=t_col, anchor="w").pack(side=tk.LEFT, padx=6)

    def render_table(self):
        """渲染持股表格"""
        for item in self.tree.get_children():
            self.tree.delete(item)

        q = self.search_var.get().strip().upper()
        mode = self.filter_mode

        for it in self.computed_items:
            # 搜尋過濾
            if q and (q not in it["symbol"].upper() and q not in it["name"].upper()):
                continue

            # 標籤過濾
            if mode == "etf" and not it["is_etf"]:
                continue
            if mode == "stock" and it["is_etf"]:
                continue
            if mode == "high_freq" and ("月" not in it["frequency"] and "季" not in it["frequency"]):
                continue
            if mode == "nhi_alert" and it["nhi_tag"] != "nhi_alert":
                continue

            row_values = (
                it["symbol"],
                it["name"],
                it["category"],
                f"${it['price']:.2f}" if it['price'] > 0 else "--",
                it["shares_lots_str"],
                f"${round(it['market_val']):,}",
                f"{it['val_weight']:.1f}%",
                f"${it['single_amt']:.2f}",
                f"${it['annual_div']:.2f}",
                f"${round(it['yearly_est']):,}",
                f"${round(it['monthly_est']):,}",
                f"{it['div_contrib']:.1f}%",
                f"{it['yield_rate']:.2f}%",
                it["frequency"],
                it["payout_months_str"],
                f"${round(it['nhi_single']):,}",
                it["nhi_status"],
                it["safe_lots_str"],
                f"${round(it['hist_received']):,}"
            )

            tag = it["nhi_tag"]
            self.tree.insert("", tk.END, values=row_values, tags=(tag,))

    def sort_table_column(self, col_id: str):
        """表格欄位升降冪排序"""
        l = [(self.tree.set(k, col_id), k) for k in self.tree.get_children('')]
        
        # 判斷是否為數值型
        def clean_val(v):
            try:
                return float(str(v).replace('$', '').replace('%', '').replace(',', '').replace('NT', '').strip())
            except Exception:
                return str(v)

        l.sort(key=lambda t: clean_val(t[0]), reverse=getattr(self, f"_sort_rev_{col_id}", False))
        setattr(self, f"_sort_rev_{col_id}", not getattr(self, f"_sort_rev_{col_id}", False))

        for index, (val, k) in enumerate(l):
            self.tree.move(k, '', index)

    def on_item_double_click(self, event):
        """雙擊標的：彈出該標的近 2 年完整除息歷史明細清單視窗"""
        selected = self.tree.selection()
        if not selected:
            return
        vals = self.tree.item(selected[0], "values")
        if not vals:
            return
        sym = vals[0]
        name = vals[1]

        # 尋找原始 item
        target_item = next((x for x in self.computed_items if x["symbol"] == sym), None)
        if not target_item:
            return

        self.open_history_detail_window(target_item)

    def open_history_detail_window(self, item: Dict[str, Any]):
        """開啟個股歷年除息明細彈窗"""
        w = tk.Toplevel(self)
        w.title(f"【{item['symbol']} {item['name']}】除息歷程與發放明細")
        w.geometry("640x480")
        w.minsize(520, 360)
        w.configure(bg=self.bg_color)
        w.transient(self)

        top_f = tk.Frame(w, bg=self.panel_bg, padx=16, pady=12)
        top_f.pack(fill=tk.X)

        tk.Label(top_f, text=f"{item['symbol']} {item['name']} 歷史除息數據", font=("Microsoft JhengHei UI", 12, "bold"), bg=self.panel_bg, fg="#ffffff").pack(anchor="w")
        tk.Label(
            top_f, 
            text=f"配息頻率: {item['frequency']} · 全年每股股息: NT${item['annual_div']:.2f} · 年化殖利率: {item['yield_rate']:.2f}%\n累計已領股息: NT${round(item['hist_received']):,} · 持有: {item['shares_lots_str']}", 
            font=("Microsoft JhengHei UI", 9), 
            bg=self.panel_bg, 
            fg=self.muted_text
        ).pack(anchor="w", pady=(4, 0))

        content_f = tk.Frame(w, bg=self.bg_color, padx=16, pady=10)
        content_f.pack(fill=tk.BOTH, expand=True)

        txt = tk.Text(content_f, bg="#1a1a26", fg="#f0f2f8", font=("Consolas", 10), relief="flat", padx=10, pady=10)
        sb = tk.Scrollbar(content_f, command=txt.yview)
        txt.configure(yscrollcommand=sb.set)
        txt.pack(side=tk.LEFT, fill=tk.BOTH, expand=True)
        sb.pack(side=tk.RIGHT, fill=tk.Y)

        raw_events = item.get("raw_events", [])
        if raw_events:
            txt.insert(tk.END, "═══════════════════════════════════════════════════════════\n")
            txt.insert(tk.END, f"  除息交易日 (Ex-Date)        每股配息金額 (NT$)     進帳估計\n")
            txt.insert(tk.END, "═══════════════════════════════════════════════════════════\n")
            for ev in reversed(raw_events):
                try:
                    dt = str(ev[0])
                    amt = float(ev[1])
                    est_in = round(item["shares"] * amt)
                    txt.insert(tk.END, f"   {dt:<22} NT$ {amt:>8.4f}       NT$ {est_in:>10,}\n")
                except Exception:
                    pass
            txt.insert(tk.END, "═══════════════════════════════════════════════════════════\n")
        else:
            txt.insert(tk.END, "本標的目前暫無 Yahoo/證交所歷史除息明細，已採用公告全年股息推估。\n可點擊主畫面【↻ 重新整理股息】連網重新查詢最新歷史。")

        txt.configure(state=tk.DISABLED)

    def on_sync_dividends(self):
        """連網重新同步除息預告與除息歷史"""
        self.status_bar.configure(text="正在連線證交所與 Yahoo Finance 同步最新除息資訊...")
        def worker():
            positions = get_all_positions()
            for p in positions:
                try:
                    DividendService.get_stock_dividend_info(p['symbol'], p.get('market', 'TW'))
                except Exception:
                    pass
            self.after(0, self.refresh_data)
        threading.Thread(target=worker, daemon=True).start()

    def on_export_report(self):
        """匯出股息報告為 CSV 檔案"""
        try:
            now_str = datetime.datetime.now().strftime("%Y%m%d_%H%M%S")
            export_path = os.path.join(get_app_dir(), f"dividend_report_{now_str}.csv")
            with open(export_path, "w", encoding="utf-8-sig") as f:
                f.write("代號,名稱,類別,最新市價,持有股數,持有張數,庫存市值,市值佔比%,單期每股股息,全年每股股息,預估年息,預估月息,息收貢獻%,年化殖利率%,配息頻率,發放月份,單次配息金額,二代健保狀態,安全免扣上限,累計已領股息\n")
                for it in self.computed_items:
                    f.write(f'"{it["symbol"]}","{it["name"]}","{it["category"]}",{it["price"]},{it["shares"]},"{it["shares_lots_str"]}",{it["market_val"]},{it["val_weight"]:.2f},{it["single_amt"]},{it["annual_div"]},{it["yearly_est"]},{it["monthly_est"]},{it["div_contrib"]:.2f},{it["yield_rate"]:.2f},"{it["frequency"]}","{it["payout_months_str"]}",{it["nhi_single"]},"{it["nhi_status"]}","{it["safe_lots_str"]}",{it["hist_received"]}\n')
            messagebox.showinfo("匯出成功", f"✔ 股息分析報表已成功匯出至：\n{export_path}", parent=self)
        except Exception as e:
            messagebox.showerror("匯出失敗", f"匯出失敗: {e}", parent=self)
