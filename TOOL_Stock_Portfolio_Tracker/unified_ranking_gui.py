# -*- coding: utf-8 -*-
"""
Stock Portfolio Tracker - 綜合指標與融資風控排行分頁視圖 (unified_ranking_gui.py)
"""
import os
import sys
import tkinter as tk
from tkinter import ttk, messagebox
import threading
from typing import Dict, List, Any, Optional, Callable

from margin_risk_service import calculate_portfolio_margin_summary, fetch_twse_market_margin_data
from ranking_service import MarketRankingService

class UnifiedRankingFrame(tk.Frame):
    def __init__(self, parent, get_positions_callback: Callable[[], List[Dict[str, Any]]], 
                 get_quotes_callback: Callable[[], Dict[str, Any]],
                 on_add_position_callback: Optional[Callable[[Dict[str, Any]], None]] = None):
        super().__init__(parent, bg="#181820")
        self.get_positions_callback = get_positions_callback
        self.get_quotes_callback = get_quotes_callback
        self.on_add_position_callback = on_add_position_callback

        self.current_sub_tab = "margin" # "margin" | "market"
        self.market_current_tab = "margin_market" # 預設切至全市場融資排行！
        self.market_filter_type = "全部 (個股+ETF)"
        self.market_search_kw = ""
        self.raw_market_items: List[Dict[str, Any]] = []

        self.margin_sort_col = "ratio"
        self.margin_sort_desc = False

        self.build_ui()

    def build_ui(self):
        sub_tab_bar = tk.Frame(self, bg="#13131a", height=38)
        sub_tab_bar.pack(fill=tk.X, side=tk.TOP, padx=10, pady=(6, 4))

        self.btn_sub_margin = tk.Button(
            sub_tab_bar, text="[風控] 個人庫存融資維持率排行",
            font=("Microsoft JhengHei UI", 10, "bold"),
            bg="#232332", fg="#38bdf8", activebackground="#2a2a3c", activeforeground="#38bdf8",
            relief="flat", padx=14, pady=4, cursor="hand2",
            command=lambda: self.switch_sub_tab("margin")
        )
        self.btn_sub_margin.pack(side=tk.LEFT, padx=(0, 6))

        self.btn_sub_market = tk.Button(
            sub_tab_bar, text="[市場] 全市場融資維持率與指標排行",
            font=("Microsoft JhengHei UI", 10, "bold"),
            bg="#181822", fg="#9090a0", activebackground="#2a2a3c", activeforeground="#ffffff",
            relief="flat", padx=14, pady=4, cursor="hand2",
            command=lambda: self.switch_sub_tab("market")
        )
        self.btn_sub_market.pack(side=tk.LEFT, padx=4)

        self.content_container = tk.Frame(self, bg="#181820")
        self.content_container.pack(fill=tk.BOTH, expand=True, padx=10, pady=(0, 6))

        self.margin_view_frame = tk.Frame(self.content_container, bg="#181820")
        self.build_margin_view(self.margin_view_frame)

        self.market_view_frame = tk.Frame(self.content_container, bg="#181820")
        self.build_market_view(self.market_view_frame)

        self.switch_sub_tab("margin")

    def switch_sub_tab(self, sub_tab: str):
        self.current_sub_tab = sub_tab
        if sub_tab == "margin":
            self.market_view_frame.pack_forget()
            self.margin_view_frame.pack(fill=tk.BOTH, expand=True)
            self.btn_sub_margin.config(bg="#232332", fg="#38bdf8")
            self.btn_sub_market.config(bg="#181822", fg="#9090a0")
            self.refresh_margin_data()
        else:
            self.margin_view_frame.pack_forget()
            self.market_view_frame.pack(fill=tk.BOTH, expand=True)
            self.btn_sub_margin.config(bg="#181822", fg="#9090a0")
            self.btn_sub_market.config(bg="#232332", fg="#38bdf8")
            self.load_market_data_async(force_refresh=False)

    def build_margin_view(self, parent: tk.Frame):
        cards_frame = tk.Frame(parent, bg="#181820")
        cards_frame.pack(fill=tk.X, side=tk.TOP, pady=(2, 6))

        self.card_ratio = tk.Frame(cards_frame, bg="#232332", relief="solid", bd=1, padx=12, pady=8)
        self.card_ratio.pack(side=tk.LEFT, fill=tk.BOTH, expand=True, padx=(0, 4))
        tk.Label(self.card_ratio, text="整戶擔保維持率 (券商風控)", font=("Microsoft JhengHei UI", 9), bg="#232332", fg="#94a3b8").pack(anchor="w")
        self.lbl_overall_ratio = tk.Label(self.card_ratio, text="--.-%", font=("Inter", 15, "bold"), bg="#232332", fg="#38bdf8")
        self.lbl_overall_ratio.pack(anchor="w", pady=(2, 0))
        self.lbl_overall_status = tk.Label(self.card_ratio, text="計算中...", font=("Microsoft JhengHei UI", 9), bg="#232332", fg="#52c41a")
        self.lbl_overall_status.pack(anchor="w")

        self.card_loan = tk.Frame(cards_frame, bg="#232332", relief="solid", bd=1, padx=12, pady=8)
        self.card_loan.pack(side=tk.LEFT, fill=tk.BOTH, expand=True, padx=4)
        tk.Label(self.card_loan, text="融資借款總本金 (NT$)", font=("Microsoft JhengHei UI", 9), bg="#232332", fg="#94a3b8").pack(anchor="w")
        self.lbl_total_loan = tk.Label(self.card_loan, text="$ 0", font=("Inter", 15, "bold"), bg="#232332", fg="#f59e0b")
        self.lbl_total_loan.pack(anchor="w", pady=(2, 0))
        self.lbl_loan_sub = tk.Label(self.card_loan, text="自備保證金: $ 0", font=("Microsoft JhengHei UI", 9), bg="#232332", fg="#9ca3af")
        self.lbl_loan_sub.pack(anchor="w")

        self.card_mkt = tk.Frame(cards_frame, bg="#232332", relief="solid", bd=1, padx=12, pady=8)
        self.card_mkt.pack(side=tk.LEFT, fill=tk.BOTH, expand=True, padx=4)
        tk.Label(self.card_mkt, text="融資擔保品目前市值", font=("Microsoft JhengHei UI", 9), bg="#232332", fg="#94a3b8").pack(anchor="w")
        self.lbl_margin_mkt = tk.Label(self.card_mkt, text="$ 0", font=("Inter", 15, "bold"), bg="#232332", fg="#10b981")
        self.lbl_margin_mkt.pack(anchor="w", pady=(2, 0))
        self.lbl_mkt_sub = tk.Label(self.card_mkt, text="融資部位現值", font=("Microsoft JhengHei UI", 9), bg="#232332", fg="#9ca3af")
        self.lbl_mkt_sub.pack(anchor="w")

        self.card_call = tk.Frame(cards_frame, bg="#232332", relief="solid", bd=1, padx=12, pady=8)
        self.card_call.pack(side=tk.LEFT, fill=tk.BOTH, expand=True, padx=(4, 0))
        tk.Label(self.card_call, text="追繳斷頭警報 (< 130%)", font=("Microsoft JhengHei UI", 9), bg="#232332", fg="#94a3b8").pack(anchor="w")
        self.lbl_call_count = tk.Label(self.card_call, text="0 檔 (安全)", font=("Inter", 15, "bold"), bg="#232332", fg="#52c41a")
        self.lbl_call_count.pack(anchor="w", pady=(2, 0))
        self.lbl_call_sub = tk.Label(self.card_call, text="需補繳差額: $ 0", font=("Microsoft JhengHei UI", 9), bg="#232332", fg="#9ca3af")
        self.lbl_call_sub.pack(anchor="w")

        table_box = tk.Frame(parent, bg="#1e1e28", relief="solid", bd=1)
        table_box.pack(fill=tk.BOTH, expand=True, pady=(4, 6))

        cols = [
            ("symbol", "標的代碼", 80),
            ("name", "標的名稱", 110),
            ("trade_type", "交易別", 70),
            ("shares", "持有股數", 90),
            ("cost_price", "成本均價", 90),
            ("current_price", "參考現價", 90),
            ("margin_loan", "融資借款額", 100),
            ("market_val", "目前市值", 100),
            ("maintenance_ratio", "融資維持率", 105),
            ("call_price", "130%斷頭價", 100),
            ("call_diff", "需補繳差額", 95),
            ("risk_label", "風控等級", 85)
        ]

        self.margin_tree = ttk.Treeview(table_box, columns=[c[0] for c in cols], show="headings")
        margin_vsb = ttk.Scrollbar(table_box, orient="vertical", command=self.margin_tree.yview)
        margin_hsb = ttk.Scrollbar(table_box, orient="horizontal", command=self.margin_tree.xview)
        self.margin_tree.configure(yscrollcommand=margin_vsb.set, xscrollcommand=margin_hsb.set)

        for cid, cname, w in cols:
            self.margin_tree.heading(cid, text=cname)
            anchor = "center" if cid in ["symbol", "trade_type", "risk_label"] else ("w" if cid == "name" else "e")
            self.margin_tree.column(cid, width=w, anchor=anchor)

        self.margin_tree.pack(side=tk.LEFT, fill=tk.BOTH, expand=True)
        margin_vsb.pack(side=tk.RIGHT, fill=tk.Y)

        self.margin_tree.tag_configure("call", foreground="#ff4d4f", background="#2a1818")
        self.margin_tree.tag_configure("warning", foreground="#ffd166", background="#282418")
        self.margin_tree.tag_configure("safe", foreground="#52c41a")
        self.margin_tree.tag_configure("cash", foreground="#9ca3af")

        bottom_tip = tk.Frame(parent, bg="#13131a", padx=10, pady=4)
        bottom_tip.pack(fill=tk.X, side=tk.BOTTOM)
        tip_text = "※ 說明：上市融資成數 60%、上櫃 50% ‧ 維持率 >= 160% 為安全區 ‧ 130%~160% 為警戒區 ‧ 低於 130% 券商發布追繳通知 ‧ 點擊表頭可排序"
        tk.Label(bottom_tip, text=tip_text, font=("Microsoft JhengHei UI", 9), bg="#13131a", fg="#64748b").pack(side=tk.LEFT)

    def refresh_margin_data(self):
        positions = self.get_positions_callback() or []
        quotes = self.get_quotes_callback() or {}
        summary = calculate_portfolio_margin_summary(positions, quotes)

        if summary["has_margin"]:
            ratio_val = summary["overall_maintenance_ratio"]
            self.lbl_overall_ratio.config(text=f"{ratio_val:.2f}%")
            if summary["overall_risk_level"] == "SAFE":
                self.lbl_overall_ratio.config(fg="#52c41a")
                self.lbl_overall_status.config(text="整戶安全 (維持率充裕)", fg="#52c41a")
            elif summary["overall_risk_level"] == "WARNING":
                self.lbl_overall_ratio.config(fg="#ffd166")
                self.lbl_overall_status.config(text="整戶警戒 (留意跌幅)", fg="#ffd166")
            else:
                self.lbl_overall_ratio.config(fg="#ff4d4f")
                self.lbl_overall_status.config(text="追繳警報 (需補繳擔保品)", fg="#ff4d4f")
        else:
            self.lbl_overall_ratio.config(text="--.-%", fg="#38bdf8")
            self.lbl_overall_status.config(text="全現股部位 (無融資借款)", fg="#9ca3af")

        tot_loan = summary["total_margin_loan"]
        tot_eq = summary["total_margin_equity"]
        self.lbl_total_loan.config(text=f"$ {tot_loan:,.0f}")
        self.lbl_loan_sub.config(text=f"自備保證金: $ {tot_eq:,.0f}")

        tot_mkt = summary["total_margin_market_val"]
        self.lbl_margin_mkt.config(text=f"$ {tot_mkt:,.0f}")

        call_cnt = summary["call_count"]
        call_diff = summary["total_call_diff"]
        if call_cnt > 0:
            self.lbl_call_count.config(text=f"{call_cnt} 檔 (追繳斷頭)", fg="#ff4d4f")
            self.lbl_call_sub.config(text=f"需補繳差額: $ {call_diff:,.0f}", fg="#ff4d4f")
        elif summary["warning_count"] > 0:
            self.lbl_call_count.config(text=f"{summary['warning_count']} 檔警戒", fg="#ffd166")
            self.lbl_call_sub.config(text="維持率介於 130%~160%", fg="#ffd166")
        else:
            self.lbl_call_count.config(text="0 檔 (無追繳)", fg="#52c41a")
            self.lbl_call_sub.config(text="需補繳差額: $ 0", fg="#9ca3af")

        for item in self.margin_tree.get_children():
            self.margin_tree.delete(item)

        items = summary["items"]
        for it in items:
            ratio_str = f"{it['maintenance_ratio']:.2f}%" if it["maintenance_ratio"] is not None else "--"
            call_p_str = f"{it['call_price']:.2f}" if it["call_price"] is not None else "--"
            diff_str = f"$ {it['margin_call_diff']:,.0f}" if it["margin_call_diff"] > 0 else "-"
            loan_str = f"$ {it['margin_loan']:,.0f}" if it["is_margin"] else "-"

            tag = "cash"
            if it["is_margin"]:
                if it["risk_level"] == "CALL":
                    tag = "call"
                elif it["risk_level"] == "WARNING":
                    tag = "warning"
                else:
                    tag = "safe"

            type_label = "融資" if it["is_margin"] else "現股"
            risk_label_full = f"[{it['risk_label']}]"

            self.margin_tree.insert("", "end", values=(
                it["symbol"],
                it["name"],
                type_label,
                f"{it['shares']:,.0f}",
                f"{it['cost_price']:.2f}",
                f"{it['current_price']:.2f}" if it["current_price"] > 0 else "--",
                loan_str,
                f"$ {it['market_val']:,.0f}",
                ratio_str,
                call_p_str,
                diff_str,
                risk_label_full
            ), tags=(tag,))

    # =========================================================================
    # 全市場融資維持率與財務指標排行榜視圖
    # =========================================================================

    def build_market_view(self, parent: tk.Frame):
        # 1. 頂部看板：展示「全市場整體融資維持率」、「全市場融資餘額」
        summary_bar = tk.Frame(parent, bg="#13131a", height=32, padx=12, pady=4)
        summary_bar.pack(fill=tk.X, side=tk.TOP, pady=(0, 4))

        self.lbl_mkt_ratio_banner = tk.Label(
            summary_bar, text="【大盤融資指標】加載中...",
            font=("Microsoft JhengHei UI", 9, "bold"),
            bg="#13131a", fg="#38bdf8"
        )
        self.lbl_mkt_ratio_banner.pack(side=tk.LEFT)

        # 2. 控制按鈕列
        top_ctrl = tk.Frame(parent, bg="#1e1e28", height=46)
        top_ctrl.pack(fill=tk.X, side=tk.TOP, pady=(2, 6))

        self.mkt_tab_btns: Dict[str, tk.Button] = {}
        tab_defs = [
            ("margin_market", "[全市場融資排行]", "上市全市場融資餘額、使用率與估計斷頭價排行"),
            ("yield", "[殖利率排行]", "全市場高配息率股票與 ETF 排行"),
            ("pe", "[低本益比排行]", "本益比 4~30 倍合理價值低估績優股"),
            ("rev", "[營收年增 (YoY) 排行]", "最新月份營收爆發成長排行"),
            ("roe", "[高 ROE 排行]", "股東權益報酬率優質排行")
        ]

        for tid, tlbl, _ in tab_defs:
            btn = tk.Button(
                top_ctrl, text=tlbl,
                font=("Microsoft JhengHei UI", 9, "bold"),
                bg="#2a2a3a", fg="#d1d5db", activebackground="#2563eb", activeforeground="#ffffff",
                relief="flat", padx=12, pady=6, cursor="hand2",
                command=lambda t=tid: self.switch_market_tab(t)
            )
            btn.pack(side=tk.LEFT, padx=3, pady=4)
            self.mkt_tab_btns[tid] = btn

        self.mkt_filter_combo = ttk.Combobox(
            top_ctrl, values=["全部 (個股+ETF)", "僅股票 (排除ETF)", "僅ETF (高股息/債券)"],
            state="readonly", width=18, font=("Microsoft JhengHei UI", 9)
        )
        self.mkt_filter_combo.set("全部 (個股+ETF)")
        self.mkt_filter_combo.pack(side=tk.LEFT, padx=8, pady=4)
        self.mkt_filter_combo.bind("<<ComboboxSelected>>", lambda e: self.apply_market_filter_and_render())

        btn_refresh = tk.Button(
            top_ctrl, text="[⟳ 重新整理行情]", font=("Microsoft JhengHei UI", 9),
            bg="#2563eb", fg="#ffffff", relief="flat", padx=10, pady=4, cursor="hand2",
            command=lambda: self.load_market_data_async(force_refresh=True)
        )
        btn_refresh.pack(side=tk.RIGHT, padx=6)

        btn_add = tk.Button(
            top_ctrl, text="[+ 加入存股庫存]", font=("Microsoft JhengHei UI", 9, "bold"),
            bg="#059669", fg="#ffffff", relief="flat", padx=10, pady=4, cursor="hand2",
            command=self.on_add_selected_market_to_portfolio
        )
        btn_add.pack(side=tk.RIGHT, padx=4)

        self.lbl_market_status = tk.Label(top_ctrl, text="資料載入中...", font=("Microsoft JhengHei UI", 9), bg="#1e1e28", fg="#9ca3af")
        self.lbl_market_status.pack(side=tk.RIGHT, padx=8)

        mkt_table_box = tk.Frame(parent, bg="#1e1e28", relief="solid", bd=1)
        mkt_table_box.pack(fill=tk.BOTH, expand=True, pady=(2, 6))

        # 預設通用欄位
        self.mkt_cols = [
            ("rank", "名次", 50),
            ("symbol", "代號", 75),
            ("name", "名稱", 110),
            ("price", "現價", 80),
            ("col1", "指標 1", 100),
            ("col2", "指標 2", 90),
            ("col3", "指標 3", 90),
            ("col4", "指標 4", 85),
            ("in_portfolio", "在庫狀態", 85)
        ]

        self.market_tree = ttk.Treeview(mkt_table_box, columns=[c[0] for c in self.mkt_cols], show="headings")
        mkt_vsb = ttk.Scrollbar(mkt_table_box, orient="vertical", command=self.market_tree.yview)
        self.market_tree.configure(yscrollcommand=mkt_vsb.set)

        self.market_tree.pack(side=tk.LEFT, fill=tk.BOTH, expand=True)
        mkt_vsb.pack(side=tk.RIGHT, fill=tk.Y)

        self.market_tree.tag_configure("in_port", foreground="#38bdf8", background="#182230")
        self.market_tree.tag_configure("normal", foreground="#e2e8f0")
        self.market_tree.tag_configure("high_util", foreground="#ffd166")

    def switch_market_tab(self, tab_id: str):
        self.market_current_tab = tab_id
        for tid, btn in self.mkt_tab_btns.items():
            if tid == tab_id:
                btn.config(bg="#2563eb", fg="#ffffff")
            else:
                btn.config(bg="#2a2a3a", fg="#d1d5db")
        self.apply_market_filter_and_render()

    def load_market_data_async(self, force_refresh: bool = False):
        self.lbl_market_status.config(text="正在載入全市場數據...")
        def worker():
            try:
                # 1. 抓取/快取全市場大盤融資維護率數據
                margin_res = fetch_twse_market_margin_data(force_refresh=force_refresh)
                # 2. 抓取/快取 4 大財務指標數據
                data = MarketRankingService.get_market_data(force_refresh=force_refresh)
                cached_time = data.get("cached_time_str", "剛剛") if data else "剛剛"
                self.after(0, lambda: self._on_market_data_ready(margin_res, cached_time))
            except Exception as e:
                self.after(0, lambda: self.lbl_market_status.config(text=f"載入失敗: {e}"))

        threading.Thread(target=worker, daemon=True).start()

    def _on_market_data_ready(self, margin_res: Dict[str, Any], cached_time: str):
        # 更新大盤融資維持率看板
        ms = margin_res.get("market_summary", {})
        if ms and ms.get("market_maintenance_ratio", 0) > 0:
            m_ratio = ms["market_maintenance_ratio"]
            loan_str = ms.get("total_margin_loan_str", "--")
            r_label = ms.get("risk_label", "安全")
            t_date = ms.get("date", "")
            banner_text = f"【台股大盤融資維持率】{m_ratio:.2f}%  |  全市場融資餘額：{loan_str}  |  風控狀態：{r_label}  (證交所數據: {t_date})"
            color = "#52c41a" if m_ratio >= 165 else ("#ffd166" if m_ratio >= 140 else "#ff4d4f")
            self.lbl_mkt_ratio_banner.config(text=banner_text, fg=color)
        else:
            self.lbl_mkt_ratio_banner.config(text="【台股大盤融資維持率】數據載入完成", fg="#38bdf8")

        self.lbl_market_status.config(text=f"資料時間: {cached_time}")
        self.apply_market_filter_and_render()

    def apply_market_filter_and_render(self):
        ftype = self.mkt_filter_combo.get()
        tab = self.market_current_tab

        # 設定表頭標題
        if tab == "margin_market":
            headers = [("rank", "名次", 50), ("symbol", "代號", 75), ("name", "名稱", 110), ("price", "現價", 80),
                       ("col1", "融資餘額(張)", 105), ("col2", "使用率(%)", 95), ("col3", "今日資增減", 90), ("col4", "130%追繳價", 95), ("in_portfolio", "在庫狀態", 85)]
        elif tab == "yield":
            headers = [("rank", "名次", 50), ("symbol", "代號", 75), ("name", "名稱", 110), ("price", "現價", 80),
                       ("col1", "現金殖利率", 100), ("col2", "本益比", 85), ("col3", "股價淨值比", 90), ("col4", "ROE", 85), ("in_portfolio", "在庫狀態", 85)]
        elif tab == "pe":
            headers = [("rank", "名次", 50), ("symbol", "代號", 75), ("name", "名稱", 110), ("price", "現價", 80),
                       ("col1", "本益比(低估)", 100), ("col2", "殖利率", 85), ("col3", "股價淨值比", 90), ("col4", "ROE", 85), ("in_portfolio", "在庫狀態", 85)]
        elif tab == "rev":
            headers = [("rank", "名次", 50), ("symbol", "代號", 75), ("name", "名稱", 110), ("price", "現價", 80),
                       ("col1", "營收年增率", 100), ("col2", "累計年增率", 95), ("col3", "單月營收(億)", 95), ("col4", "產業別", 85), ("in_portfolio", "在庫狀態", 85)]
        else: # roe
            headers = [("rank", "名次", 50), ("symbol", "代號", 75), ("name", "名稱", 110), ("price", "現價", 80),
                       ("col1", "ROE(%)", 100), ("col2", "本益比", 85), ("col3", "殖利率", 85), ("col4", "股價淨值比", 85), ("in_portfolio", "在庫狀態", 85)]

        for cid, cname, w in headers:
            self.market_tree.heading(cid, text=cname)
            anchor = "center" if cid in ["rank", "symbol", "in_portfolio"] else ("w" if cid == "name" else "e")
            self.market_tree.column(cid, width=w, anchor=anchor)

        # 取得排序數據
        filtered = []
        if tab == "margin_market":
            margin_data = fetch_twse_market_margin_data(force_refresh=False)
            all_m_items = list(margin_data.get("items", []))
            # 依融資餘額由大到小排序
            all_m_items.sort(key=lambda x: x.get("margin_balance_lots", 0), reverse=True)
            for it in all_m_items:
                is_etf = it.get("is_etf", False)
                if ftype == "僅股票 (排除ETF)" and is_etf:
                    continue
                if ftype == "僅ETF (高股息/債券)" and not is_etf:
                    continue
                filtered.append(it)
        else:
            if tab == "yield":
                fcode = "etf" if "僅ETF" in ftype else ("stock" if "僅股票" in ftype else "all")
                items = MarketRankingService.get_dividend_ranking(filter_type=fcode, limit=100)
            elif tab == "pe":
                items = MarketRankingService.get_pe_ranking(limit=100)
            elif tab == "rev":
                items = MarketRankingService.get_revenue_growth_ranking(limit=100)
            else: # roe
                items = MarketRankingService.get_roe_ranking(limit=100)
            filtered = items[:100]

        curr_positions = self.get_positions_callback() or []
        existing_syms = set(p.get("symbol", "").upper() for p in curr_positions)

        for item in self.market_tree.get_children():
            self.market_tree.delete(item)

        for idx, it in enumerate(filtered, start=1):
            sym = it.get("symbol", "")
            name = it.get("name", sym)
            price = it.get("price", 0.0)
            is_in = sym.upper() in existing_syms
            in_label = "[已在庫]" if is_in else "未持有"
            tag = "in_port" if is_in else "normal"

            if tab == "margin_market":
                bal_lots = f"{it.get('margin_balance_lots', 0):,}"
                util = f"{it.get('margin_utilization', 0):.2f}%"
                net_lots = f"{it.get('margin_net_lots', 0):+,}"
                call_p = f"{it.get('call_price', 0):.2f}" if it.get("call_price", 0) > 0 else "--"
                vals = (idx, sym, name, f"{price:.2f}" if price > 0 else "--", bal_lots, util, net_lots, call_p, in_label)
                if it.get("margin_utilization", 0) >= 50.0:
                    tag = "high_util"
            elif tab == "yield":
                yd = f"{it.get('yield_pct', 0):.2f}%" if it.get("yield_pct") else "--"
                pe = f"{it.get('pe', 0):.2f}" if it.get("pe") else "--"
                pb = f"{it.get('pb', 0):.2f}" if it.get("pb") else "--"
                roe = f"{it.get('roe', 0):.2f}%" if it.get("roe") else "--"
                vals = (idx, sym, name, f"{price:.2f}" if price > 0 else "--", yd, pe, pb, roe, in_label)
            elif tab == "pe":
                pe = f"{it.get('pe', 0):.2f}" if it.get("pe") else "--"
                yd = f"{it.get('yield_pct', 0):.2f}%" if it.get("yield_pct") else "--"
                pb = f"{it.get('pb', 0):.2f}" if it.get("pb") else "--"
                roe = f"{it.get('roe', 0):.2f}%" if it.get("roe") else "--"
                vals = (idx, sym, name, f"{price:.2f}" if price > 0 else "--", pe, yd, pb, roe, in_label)
            elif tab == "rev":
                yoy = f"{it.get('yoy', 0):+.2f}%" if it.get("yoy") is not None else "--"
                cum = f"{it.get('cum_yoy', 0):+.2f}%" if it.get("cum_yoy") is not None else "--"
                cur_e = f"{it.get('rev_cur_e', 0):.2f}" if it.get("rev_cur_e") else "--"
                ind = it.get("industry", "--")
                vals = (idx, sym, name, f"{price:.2f}" if price > 0 else "--", yoy, cum, cur_e, ind, in_label)
            else: # roe
                roe = f"{it.get('roe', 0):.2f}%" if it.get("roe") else "--"
                pe = f"{it.get('pe', 0):.2f}" if it.get("pe") else "--"
                yd = f"{it.get('yield_pct', 0):.2f}%" if it.get("yield_pct") else "--"
                pb = f"{it.get('pb', 0):.2f}" if it.get("pb") else "--"
                vals = (idx, sym, name, f"{price:.2f}" if price > 0 else "--", roe, pe, yd, pb, in_label)

            self.market_tree.insert("", "end", values=vals, tags=(tag,))

    def on_add_selected_market_to_portfolio(self):
        sel = self.market_tree.selection()
        if not sel:
            messagebox.showinfo("提示", "請先在列表中點選欲加入庫存的標的！", parent=self)
            return
        vals = self.market_tree.item(sel[0], "values")
        if vals and len(vals) >= 4:
            sym = vals[1]
            name = vals[2]
            try:
                price = float(vals[3])
            except Exception:
                price = 0.0

            if self.on_add_position_callback:
                self.on_add_position_callback({
                    "symbol": sym,
                    "name": name,
                    "cost_price": price,
                    "shares": 1000
                })
            else:
                messagebox.showinfo("加入標的", f"標的 [{sym} {name}] 參考現價: {price}", parent=self)
