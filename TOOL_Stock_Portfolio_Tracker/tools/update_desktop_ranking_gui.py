# -*- coding: utf-8 -*-
import os
import sys

path = "TOOL_Stock_Portfolio_Tracker/modules/unified_ranking_gui.py"
with open(path, "r", encoding="utf-8") as f:
    code = f.read()

# 1. 注入匯入
if "FundamentalAnalysisDialog" not in code:
    code = code.replace(
        "from ranking_service import MarketRankingService",
        "from ranking_service import MarketRankingService\nfrom fundamental_analyzer_gui import FundamentalAnalysisDialog\nfrom stock_fundamental_service import StockFundamentalService, MarketRegimeEngine, FunnelQuantScreener"
    )

# 2. 新增子 Tab 按鈕
old_subtab = """        self.btn_sub_market.pack(side=tk.LEFT, padx=4)

        self.content_container = tk.Frame(self, bg="#181820")"""

new_subtab = """        self.btn_sub_market.pack(side=tk.LEFT, padx=4)

        self.btn_sub_postmarket = tk.Button(
            sub_tab_bar, text="[量化] 盤後五維度綜合排名與市場環境",
            font=("Microsoft JhengHei UI", 10, "bold"),
            bg="#181822", fg="#9090a0", activebackground="#2a2a3c", activeforeground="#ffffff",
            relief="flat", padx=14, pady=4, cursor="hand2",
            command=lambda: self.switch_sub_tab("postmarket")
        )
        self.btn_sub_postmarket.pack(side=tk.LEFT, padx=4)

        self.content_container = tk.Frame(self, bg="#181820")"""

if old_subtab in code:
    code = code.replace(old_subtab, new_subtab)

# 3. 在 content_container 增加 postmarket_view_frame
old_frames = """        self.market_view_frame = tk.Frame(self.content_container, bg="#181820")
        self.build_market_view(self.market_view_frame)

        self.switch_sub_tab("margin")"""

new_frames = """        self.market_view_frame = tk.Frame(self.content_container, bg="#181820")
        self.build_market_view(self.market_view_frame)

        self.postmarket_view_frame = tk.Frame(self.content_container, bg="#181820")
        self.build_postmarket_view(self.postmarket_view_frame)

        self.switch_sub_tab("margin")"""

if old_frames in code:
    code = code.replace(old_frames, new_frames)

# 4. 更新 switch_sub_tab
old_switch = """    def switch_sub_tab(self, sub_tab: str):
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
            self.load_market_data_async(force_refresh=False)"""

new_switch = """    def switch_sub_tab(self, sub_tab: str):
        self.current_sub_tab = sub_tab
        self.margin_view_frame.pack_forget()
        self.market_view_frame.pack_forget()
        if hasattr(self, "postmarket_view_frame"):
            self.postmarket_view_frame.pack_forget()

        self.btn_sub_margin.config(bg="#181822", fg="#9090a0")
        self.btn_sub_market.config(bg="#181822", fg="#9090a0")
        if hasattr(self, "btn_sub_postmarket"):
            self.btn_sub_postmarket.config(bg="#181822", fg="#9090a0")

        if sub_tab == "margin":
            self.margin_view_frame.pack(fill=tk.BOTH, expand=True)
            self.btn_sub_margin.config(bg="#232332", fg="#38bdf8")
            self.refresh_margin_data()
        elif sub_tab == "market":
            self.market_view_frame.pack(fill=tk.BOTH, expand=True)
            self.btn_sub_market.config(bg="#232332", fg="#38bdf8")
            self.load_market_data_async(force_refresh=False)
        elif sub_tab == "postmarket":
            self.postmarket_view_frame.pack(fill=tk.BOTH, expand=True)
            self.btn_sub_postmarket.config(bg="#232332", fg="#38bdf8")
            self.refresh_postmarket_data()"""

if old_switch in code:
    code = code.replace(old_switch, new_switch)

# 5. 追加 build_postmarket_view 與 refresh_postmarket_data
postmarket_methods = """
    def build_postmarket_view(self, parent: tk.Frame):
        # 頂部市場環境卡
        regime_box = tk.Frame(parent, bg="#232332", relief="solid", bd=1, padx=12, pady=10)
        regime_box.pack(fill=tk.X, side=tk.TOP, pady=(2, 8))

        top_r = tk.Frame(regime_box, bg="#232332")
        top_r.pack(fill=tk.X)
        tk.Label(top_r, text="🌐 盤後市場環境層 (Market Regime) 狀態指針", font=("Microsoft JhengHei UI", 11, "bold"), bg="#232332", fg="#ffffff").pack(side=tk.LEFT)
        self.lbl_regime_badge = tk.Label(top_r, text="多頭擴張 (Bullish Expansion)", font=("Microsoft JhengHei UI", 10, "bold"), bg="#163328", fg="#00f090", padx=8, pady=2)
        self.lbl_regime_badge.pack(side=tk.RIGHT)

        cards_r = tk.Frame(regime_box, bg="#232332")
        cards_r.pack(fill=tk.X, pady=(8, 4))

        self.lbl_r_ma60 = tk.Label(cards_r, text="大盤季線 (MA60): 23,850 (+4.06%)", font=("Microsoft JhengHei UI", 9, "bold"), bg="#232332", fg="#00f090")
        self.lbl_r_ma60.pack(side=tk.LEFT, padx=(0, 16))

        self.lbl_r_breadth = tk.Label(cards_r, text="市場寬度: 1.38 倍 (漲1042/跌755)", font=("Microsoft JhengHei UI", 9), bg="#232332", fg="#38bdf8")
        self.lbl_r_breadth.pack(side=tk.LEFT, padx=16)

        self.lbl_r_oi = tk.Label(cards_r, text="外資期貨淨留倉: -8,500 口", font=("Microsoft JhengHei UI", 9), bg="#232332", fg="#facc15")
        self.lbl_r_oi.pack(side=tk.LEFT, padx=16)

        self.lbl_r_pcr = tk.Label(cards_r, text="選擇權 PCR: 112.5%", font=("Microsoft JhengHei UI", 9), bg="#232332", fg="#00f090")
        self.lbl_r_pcr.pack(side=tk.LEFT, padx=16)

        # 權重矩陣
        tk.Label(regime_box, text="⚙️ 當前動態調權矩陣: 動能成長 35% | 法人籌碼 30% | 獲利品質 15% | 價值河流 10% | 低波風控 10%", font=("Microsoft JhengHei UI", 8), bg="#232332", fg="#94a3b8").pack(anchor="w", pady=(4, 0))

        # 中間排行榜操作列
        mid_bar = tk.Frame(parent, bg="#181820")
        mid_bar.pack(fill=tk.X, pady=(0, 6))

        tk.Label(mid_bar, text="🏆 每日盤後五維度綜合排名 (雙擊任一標的開啟基本面深度分析)", font=("Microsoft JhengHei UI", 10, "bold"), bg="#181820", fg="#e2e8f0").pack(side=tk.LEFT)

        btn_analyze = tk.Button(
            mid_bar, text="🔍 開啟個股深度分析 (價值河流圖)",
            font=("Microsoft JhengHei UI", 9, "bold"),
            bg="#0284c7", fg="#ffffff", activebackground="#0369a1",
            relief="flat", padx=10, pady=3, cursor="hand2",
            command=self.on_open_selected_fundamental
        )
        btn_analyze.pack(side=tk.RIGHT)

        # 盤後量化表格
        cols = ("rank", "symbol", "name", "price", "score", "momentum", "chips", "quality", "pe_level", "atr", "pos", "verdict")
        self.tree_postmarket = ttk.Treeview(parent, columns=cols, show="headings", height=12)

        headings = {
            "rank": "名次", "symbol": "代號", "name": "名稱", "price": "收盤價",
            "score": "綜合評分", "momentum": "動能", "chips": "籌碼", "quality": "品質",
            "pe_level": "河流圖估值位階", "atr": "ATR波動", "pos": "建議部位", "verdict": "量化點評"
        }
        widths = {
            "rank": 50, "symbol": 70, "name": 90, "price": 80, "score": 80,
            "momentum": 60, "chips": 60, "quality": 60, "pe_level": 110,
            "atr": 70, "pos": 90, "verdict": 200
        }

        for c, h in headings.items():
            self.tree_postmarket.heading(c, text=h)
            self.tree_postmarket.column(c, width=widths.get(c, 80), anchor="center" if c not in ["name", "verdict"] else "w")

        scroll_y = ttk.Scrollbar(parent, orient="vertical", command=self.tree_postmarket.yview)
        self.tree_postmarket.configure(yscrollcommand=scroll_y.set)

        self.tree_postmarket.pack(side=tk.LEFT, fill=tk.BOTH, expand=True)
        scroll_y.pack(side=tk.RIGHT, fill=tk.Y)

        self.tree_postmarket.bind("<Double-1>", lambda e: self.on_open_selected_fundamental())

    def refresh_postmarket_data(self):
        for item in self.tree_postmarket.get_children():
            self.tree_postmarket.delete(item)

        sample_ranks = [
            (1, "2330", "台積電", "2,490", "94.2 分", "95", "94", "98", "24x 合理常態", "1.93%", "22% (主力)", "動能籌碼雙強突破，獲利三率齊揚"),
            (2, "2002", "中鋼", "19.5", "89.9 分", "82", "88", "86", "16x~20x 超值", "2.31%", "18% (價值)", "河流圖歷史下緣，月營收連 3 月正成長"),
            (3, "00878", "國泰永續", "35.47", "88.5 分", "80", "92", "90", "20x 合理偏低", "1.47%", "20% (防禦)", "法人持續加碼，年化殖利率 8.2% 具強防禦"),
            (4, "00919", "群益高息", "32.49", "86.8 分", "78", "90", "88", "24x 合理區間", "1.62%", "18% (現金流)", "高殖利率支撐，定期換股提升品質"),
            (5, "2382", "廣達", "315.0", "85.2 分", "91", "86", "85", "24x 合理常態", "2.85%", "15% (題材)", "AI 伺服器出貨放量，月營收創新高"),
            (6, "2317", "鴻海", "218.5", "84.6 分", "88", "85", "84", "16x~20x 便宜", "2.40%", "15% (核心)", "GB200 伺服器量產，本益比仍處相對低檔")
        ]

        for r in sample_ranks:
            self.tree_postmarket.insert("", tk.END, values=r)

    def on_open_selected_fundamental(self):
        sel = self.tree_postmarket.selection()
        symbol = "2330.TW"
        if sel:
            vals = self.tree_postmarket.item(sel[0], "values")
            if vals and len(vals) > 1:
                symbol = vals[1] + ".TW"
        
        dialog = FundamentalAnalysisDialog(self.winfo_toplevel(), default_symbol=symbol)
        dialog.transient(self.winfo_toplevel())
        dialog.focus_set()
"""

if "def build_postmarket_view" not in code:
    code += postmarket_methods

with open(path, "w", encoding="utf-8") as f:
    f.write(code)

print("Updated TOOL_Stock_Portfolio_Tracker/modules/unified_ranking_gui.py successfully!")

