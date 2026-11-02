# -*- coding: utf-8 -*-
"""
Stock Portfolio Tracker - 自組 ETF 淨值走勢與各股多期績效分析介面 (portfolio_etf_gui.py)
嚴格遵守:
1. Windows XP 向下相容 (.NET 4.0 / Python 3.8-3.10 Tkinter 原生)
2. 防裁切佈局 (Grid / Pack with fill, AutoScroll)
3. 零 Emoji 規範 (禁用任何 U+1F000+ 多位元組 Emoji)
4. 零偽造真實數據驅動
"""

import tkinter as tk
from tkinter import ttk, messagebox
from typing import List, Dict, Any, Optional

from kline_chart import KLineChartCanvas
from portfolio_etf_service import calculate_portfolio_synthetic_etf, calculate_stocks_performance_matrix

class PortfolioETFFrame(tk.Frame):
    """
    自組 ETF 淨值 K 線與各股每日/週/月/年績效分析整合面板 (可作為獨立分頁嵌入)
    """
    def __init__(self, parent, get_positions_callback, **kwargs):
        super().__init__(parent, bg="#181820", **kwargs)
        self.get_positions_callback = get_positions_callback

        self.etf_data: Dict[str, Any] = {}
        self.perf_data: Dict[str, Any] = {}
        self.current_period_view = "day" # "day", "week", "month", "year"

        self._build_ui()
        self.refresh_data()

    def _build_ui(self):
        # 1. 頂部看板列 (Summary Header Card)
        self.header_card = tk.Frame(self, bg="#20202d", bd=1, relief=tk.SOLID, padx=12, pady=8)
        self.header_card.pack(fill=tk.X, side=tk.TOP, padx=10, pady=(8, 4))

        # 頂部左側：自組 ETF 標題與指標
        left_header = tk.Frame(self.header_card, bg="#20202d")
        left_header.pack(side=tk.LEFT, fill=tk.BOTH, expand=True)

        self.title_lbl = tk.Label(
            left_header,
            text="[自組投資組合 ETF] PORTFOLIO COMPOSITE INDEX",
            bg="#20202d",
            fg="#ffffff",
            font=("Microsoft JhengHei UI", 12, "bold")
        )
        self.title_lbl.pack(anchor="w")

        self.metrics_lbl = tk.Label(
            left_header,
            text="載入中...",
            bg="#20202d",
            fg="#9090a0",
            font=("Consolas", 10)
        )
        self.metrics_lbl.pack(anchor="w", pady=(4, 0))

        # 頂部右側：控制按鈕 (天數切換與重整)
        right_header = tk.Frame(self.header_card, bg="#20202d")
        right_header.pack(side=tk.RIGHT, fill=tk.Y)

        self.btn_refresh = tk.Button(
            right_header,
            text="[重新計算]",
            bg="#2a2a3c",
            fg="#69c0ff",
            activebackground="#333348",
            activeforeground="#91d5ff",
            relief=tk.FLAT,
            font=("Microsoft JhengHei UI", 9, "bold"),
            command=self.refresh_data,
            cursor="hand2",
            padx=8,
            pady=3
        )
        self.btn_refresh.pack(side=tk.RIGHT, padx=4)

        for days, label in [(120, "120日"), (60, "60日"), (30, "30日"), (0, "全部")]:
            btn = tk.Button(
                right_header,
                text=f"[{label}]",
                bg="#232332",
                fg="#d0d0e0",
                activebackground="#303046",
                relief=tk.FLAT,
                font=("Microsoft JhengHei UI", 9),
                command=lambda d=days: self._set_chart_days(d),
                cursor="hand2",
                padx=6,
                pady=3
            )
            btn.pack(side=tk.RIGHT, padx=2)

        # 2. 中間上半部：自組 ETF K 線圖容器
        self.kline_container = tk.Frame(self, bg="#181820", height=320)
        self.kline_container.pack(fill=tk.BOTH, expand=True, padx=10, pady=4)
        self.kline_canvas: Optional[KLineChartCanvas] = None

        # 3. 下半部：各股多週期績效分析區域
        self.perf_section = tk.Frame(self, bg="#181820", height=280)
        self.perf_section.pack(fill=tk.BOTH, expand=True, padx=10, pady=(4, 8))

        # 下半部左側：績效長條圖 (Performance Bar Chart)
        self.chart_panel = tk.Frame(self.perf_section, bg="#20202d", bd=1, relief=tk.SOLID)
        self.chart_panel.pack(side=tk.LEFT, fill=tk.BOTH, expand=True, padx=(0, 4))

        # 長條圖頂部控制列
        bar_header = tk.Frame(self.chart_panel, bg="#20202d", padx=8, pady=6)
        bar_header.pack(fill=tk.X, side=tk.TOP)

        tk.Label(
            bar_header,
            text="[各股績效排行榜]",
            bg="#20202d",
            fg="#ffffff",
            font=("Microsoft JhengHei UI", 10, "bold")
        ).pack(side=tk.LEFT)

        self.period_var = tk.StringVar(value="day")
        for val, txt in [("day", "當日"), ("week", "當週(5d)"), ("month", "當月(20d)"), ("year", "全期/年化")]:
            rb = tk.Radiobutton(
                bar_header,
                text=txt,
                value=val,
                variable=self.period_var,
                bg="#20202d",
                fg="#a0a0b8",
                selectcolor="#2e2e42",
                activebackground="#20202d",
                activeforeground="#ffffff",
                font=("Microsoft JhengHei UI", 9),
                command=self._on_period_changed
            )
            rb.pack(side=tk.LEFT, padx=4)

        # 長條圖 Canvas
        self.bar_canvas = tk.Canvas(self.chart_panel, bg="#1a1a24", highlightthickness=0)
        self.bar_canvas.pack(fill=tk.BOTH, expand=True, padx=4, pady=(0, 4))
        self.bar_canvas.bind("<Configure>", lambda e: self._draw_bar_chart())

        # 下半部右側：績效詳細表格 (Treeview)
        self.table_panel = tk.Frame(self.perf_section, bg="#20202d", bd=1, relief=tk.SOLID)
        self.table_panel.pack(side=tk.RIGHT, fill=tk.BOTH, expand=True, padx=(4, 0))

        tbl_header = tk.Frame(self.table_panel, bg="#20202d", padx=8, pady=6)
        tbl_header.pack(fill=tk.X, side=tk.TOP)

        tk.Label(
            tbl_header,
            text="[各股多週期詳細數據與權重]",
            bg="#20202d",
            fg="#ffffff",
            font=("Microsoft JhengHei UI", 10, "bold")
        ).pack(side=tk.LEFT)

        # Treeview 表格
        cols = ("symbol", "name", "weight", "day", "week", "month", "year", "unrealized")
        self.tree = ttk.Treeview(self.table_panel, columns=cols, show="headings", selectmode="browse")
        
        self.tree.heading("symbol", text="代號", command=lambda: self._sort_table("symbol", False))
        self.tree.heading("name", text="名稱", command=lambda: self._sort_table("name", False))
        self.tree.heading("weight", text="權重%", command=lambda: self._sort_table("weight", True))
        self.tree.heading("day", text="日%", command=lambda: self._sort_table("day", True))
        self.tree.heading("week", text="週%", command=lambda: self._sort_table("week", True))
        self.tree.heading("month", text="月%", command=lambda: self._sort_table("month", True))
        self.tree.heading("year", text="全期/年%", command=lambda: self._sort_table("year", True))
        self.tree.heading("unrealized", text="未實現損益", command=lambda: self._sort_table("unrealized", True))

        self.tree.column("symbol", width=60, anchor="center")
        self.tree.column("name", width=95, anchor="w")
        self.tree.column("weight", width=65, anchor="e")
        self.tree.column("day", width=65, anchor="e")
        self.tree.column("week", width=65, anchor="e")
        self.tree.column("month", width=65, anchor="e")
        self.tree.column("year", width=75, anchor="e")
        self.tree.column("unrealized", width=85, anchor="e")

        tree_scroll = ttk.Scrollbar(self.table_panel, orient=tk.VERTICAL, command=self.tree.yview)
        self.tree.configure(yscrollcommand=tree_scroll.set)
        
        self.tree.pack(side=tk.LEFT, fill=tk.BOTH, expand=True, padx=(4, 0), pady=(0, 4))
        tree_scroll.pack(side=tk.RIGHT, fill=tk.Y, pady=(0, 4))

        self.tree_sort_reverse = {}

    def refresh_data(self):
        """重新從目前持股計算自組 ETF K 線與各股績效"""
        try:
            positions = self.get_positions_callback()
        except Exception as e:
            positions = []

        if not positions:
            self.metrics_lbl.config(text="目前無有效持股部位資料")
            return

        # 1. 計算自組 ETF
        self.etf_data = calculate_portfolio_synthetic_etf(positions)
        # 2. 計算各股多期績效
        self.perf_data = calculate_stocks_performance_matrix(positions)

        # 更新頂部指標卡
        c_nav = self.etf_data.get("current_nav", 100.0)
        tot_ret = self.etf_data.get("total_return_pct", 0.0)
        d_chg = self.etf_data.get("nav_change_pct", 0.0)
        tot_mv = self.perf_data.get("total_market_value", 0.0)
        unreal_pnl = self.perf_data.get("total_unrealized_pnl", 0.0)
        unreal_pct = self.perf_data.get("total_unrealized_pct", 0.0)
        b_date = self.etf_data.get("base_date", "")
        l_date = self.etf_data.get("latest_date", "")

        sign_ret = "+" if tot_ret > 0 else ""
        sign_day = "+" if d_chg > 0 else ""
        sign_pnl = "+" if unreal_pnl > 0 else ""

        metrics_text = (
            f"基準淨值: 100.00 ({b_date}) -> 最新淨值: {c_nav:.2f} ({sign_ret}{tot_ret:.2f}%) | "
            f"當日漲跌: {sign_day}{d_chg:.2f}% | "
            f"組合總市值: NT$ {tot_mv:,.0f} | "
            f"未實現損益: NT$ {sign_pnl}{unreal_pnl:,.0f} ({sign_pnl}{unreal_pct:.2f}%)"
        )
        self.metrics_lbl.config(text=metrics_text)

        # 更新 K 線圖
        if self.kline_canvas:
            self.kline_canvas.destroy()

        records = self.etf_data.get("records", [])
        self.kline_canvas = KLineChartCanvas(
            self.kline_container,
            records=records,
            symbol="PORTFOLIO",
            name="自組投資組合 ETF 淨值指數"
        )
        self.kline_canvas.pack(fill=tk.BOTH, expand=True)

        # 更新右側表格
        self._populate_table()
        # 更新左側長條圖
        self._draw_bar_chart()

    def _set_chart_days(self, days: int):
        if self.kline_canvas:
            self.kline_canvas.set_display_days(days)

    def _on_period_changed(self):
        self.current_period_view = self.period_var.get()
        self._draw_bar_chart()

    def _populate_table(self):
        for item in self.tree.get_children():
            self.tree.delete(item)

        items = self.perf_data.get("items", [])
        for it in items:
            sign_d = "+" if it["day_pct"] > 0 else ""
            sign_w = "+" if it["week_pct"] > 0 else ""
            sign_m = "+" if it["month_pct"] > 0 else ""
            sign_y = "+" if it["year_pct"] > 0 else ""
            sign_u = "+" if it["unrealized_pnl"] > 0 else ""

            row_vals = (
                it["symbol"],
                it["name"],
                f"{it['weight_pct']:.2f}%",
                f"{sign_d}{it['day_pct']:.2f}%",
                f"{sign_w}{it['week_pct']:.2f}%",
                f"{sign_m}{it['month_pct']:.2f}%",
                f"{sign_y}{it['year_pct']:.2f}%",
                f"{sign_u}{it['unrealized_pnl']:,.0f}"
            )
            item_id = self.tree.insert("", tk.END, values=row_vals)

    def _sort_table(self, col: str, is_numeric: bool):
        items = self.perf_data.get("items", [])
        if not items:
            return

        reverse = self.tree_sort_reverse.get(col, False)
        key_map = {
            "symbol": lambda x: x["symbol"],
            "name": lambda x: x["name"],
            "weight": lambda x: x["weight_pct"],
            "day": lambda x: x["day_pct"],
            "week": lambda x: x["week_pct"],
            "month": lambda x: x["month_pct"],
            "year": lambda x: x["year_pct"],
            "unrealized": lambda x: x["unrealized_pnl"]
        }
        getter = key_map.get(col, lambda x: 0)
        items.sort(key=getter, reverse=reverse)
        self.tree_sort_reverse[col] = not reverse
        self._populate_table()

    def _draw_bar_chart(self):
        """自繪各股橫向柱狀圖 (純 Tkinter Canvas，紅漲綠跌)"""
        self.bar_canvas.delete("all")
        items = list(self.perf_data.get("items", []))
        if not items:
            return

        w = self.bar_canvas.winfo_width()
        h = self.bar_canvas.winfo_height()
        if w < 50 or h < 50:
            return

        period_key_map = {
            "day": ("day_pct", "當日績效"),
            "week": ("week_pct", "當週績效 (5日)"),
            "month": ("month_pct", "當月績效 (20日)"),
            "year": ("year_pct", "全期/年化績效")
        }
        val_key, title = period_key_map.get(self.current_period_view, ("day_pct", "當日績效"))

        # 依照該週期數值由高至低排序
        items.sort(key=lambda x: x[val_key], reverse=True)

        # 找出最大絕對值以決定比例尺
        max_abs = max(max(abs(x[val_key]) for x in items), 5.0)

        padding_top = 25
        padding_bottom = 20
        label_w = 110 # 左側股票名稱寬度
        val_w = 65   # 數值標籤寬度
        chart_w = w - label_w - val_w - 20
        zero_x = label_w + chart_w / 2.0 # 0 軸位於正中央

        # 標題與軸線
        self.bar_canvas.create_text(
            w / 2, 12,
            text=f"-- {title} 排行 (紅漲 / 綠跌) --",
            fill="#a0a0b8",
            font=("Microsoft JhengHei UI", 9)
        )
        self.bar_canvas.create_line(zero_x, padding_top, zero_x, h - padding_bottom, fill="#404055", width=1, dash=(2, 2))

        n = len(items)
        available_h = h - padding_top - padding_bottom
        row_h = available_h / max(n, 1)
        bar_h = max(min(row_h * 0.65, 16), 6)

        c_up = "#ff4d4f"
        c_down = "#52c41a"
        c_flat = "#aaaaaa"

        for i, it in enumerate(items):
            y_center = padding_top + (i + 0.5) * row_h
            val = it[val_key]
            sym = it["symbol"]
            name = it["name"]

            # 股票名稱
            self.bar_canvas.create_text(
                label_w - 6, y_center,
                text=f"{sym} {name[:5]}",
                anchor="e",
                fill="#d0d0e0",
                font=("Consolas", 9)
            )

            # 計算柱體寬度
            bar_len = (abs(val) / max_abs) * (chart_w / 2.0)
            if val > 0:
                x1 = zero_x
                x2 = zero_x + bar_len
                fill_c = c_up
                val_anchor = "w"
                val_x = x2 + 6
                val_text = f"+{val:.2f}%"
            elif val < 0:
                x1 = zero_x - bar_len
                x2 = zero_x
                fill_c = c_down
                val_anchor = "e"
                val_x = x1 - 6
                val_text = f"{val:.2f}%"
            else:
                x1 = zero_x - 1
                x2 = zero_x + 1
                fill_c = c_flat
                val_anchor = "w"
                val_x = x2 + 6
                val_text = "0.00%"

            # 繪製柱體
            self.bar_canvas.create_rectangle(
                x1, y_center - bar_h / 2.0,
                x2, y_center + bar_h / 2.0,
                fill=fill_c,
                outline=""
            )

            # 標註百分比
            self.bar_canvas.create_text(
                val_x, y_center,
                text=val_text,
                anchor=val_anchor,
                fill=fill_c,
                font=("Consolas", 8, "bold")
            )

class PortfolioETFStudioDialog(tk.Toplevel):
    """
    自組 ETF 淨值走勢與各股多期績效獨立分析視窗 (可自選全螢幕或視窗化)
    """
    def __init__(self, parent, get_positions_callback):
        super().__init__(parent)
        self.title("自組投資組合 ETF 淨值走勢與多週期績效分析")
        self.geometry("1100x720")
        self.minsize(950, 600)
        self.configure(bg="#181820")

        self.panel = PortfolioETFFrame(self, get_positions_callback)
        self.panel.pack(fill=tk.BOTH, expand=True)

