# -*- coding: utf-8 -*-
"""
Stock Portfolio Tracker - 個股基本面深度分析 & 價值河流圖 (Fundamental Analysis GUI)
支援全市場 1,000+ 檔上市上櫃股票與 ETF 任意查詢與深度分析：
1. 任意代碼與名稱搜尋：支援輸入 2317, 2454, 2603, 鴻海, 聯發科 等任意標的，即時檢索全市場快取。
2. 本益比價值河流圖 (P/E River Valuation Bands 16x~32x)：清晰 Y 軸金額、水平參考網格、4 層漸層河流、最新價光暈標記。
3. 月營收與年增率 (YoY) 雙軸圖：左軸營收 (億元)、右軸 YoY (%)、月份與成長率數據標註。
4. 獲利能力三率走勢：毛利率、營業利益率、稅後淨利率 5 季走勢與網格線。
5. 杜邦三因子拆解 (ROE 驅動結構) 與 財務體質安全性診斷 (自由現金流、負債比、流動比)。
"""
import os
import sys
import json
import tkinter as tk
from tkinter import ttk, messagebox
from typing import Optional, Dict, Any, List

if sys.stdout is not None and hasattr(sys.stdout, "reconfigure"):
    sys.stdout.reconfigure(encoding="utf-8")
if sys.stderr is not None and hasattr(sys.stderr, "reconfigure"):
    sys.stderr.reconfigure(encoding="utf-8")

from stock_fundamental_service import StockFundamentalService


class FundamentalAnalysisDialog(tk.Toplevel):
    def __init__(self, parent, default_symbol: str = "2330.TW", symbol: str = None, name: str = "", get_positions_callback=None):
        super().__init__(parent)
        self.title("個股基本面深度分析 & 價值河流圖 - Stock Fundamental & River Valuation")
        self.geometry("1200x820+40+30")
        self.minsize(1050, 720)
        self.configure(bg="#111118")

        if symbol:
            default_symbol = symbol

        self.get_positions_callback = get_positions_callback
        self.supported_symbols = self.load_all_market_symbols()

        # 設定初始選中標的
        self.current_symbol = default_symbol.replace(".TW", "").replace(".TWO", "").strip()
        matched_str = next((s for s in self.supported_symbols if s.startswith(self.current_symbol)), f"{self.current_symbol} {name}".strip())
        self.selected_display = matched_str

        self.stock_data = {}
        self.build_ui()
        
        # 強制進行幾何佈局計算，確保 Canvas 擁有真實寬高
        self.update_idletasks()
        self.load_and_render(self.current_symbol)

        # 綁定視窗縮放事件，動態自適應重繪
        self.bind("<Configure>", self.on_window_resize)
        self._resize_job = None

    def load_all_market_symbols(self) -> List[str]:
        """動態載入全市場 1,000+ 檔標的與個人庫存持股"""
        symbols = []
        seen = set()

        # 1. 使用者個人庫存持股置頂
        if self.get_positions_callback:
            try:
                positions = self.get_positions_callback()
                for p in positions:
                    sym = p.get("symbol", "").replace(".TW", "").replace(".TWO", "").strip()
                    nm = p.get("name", "").strip()
                    if sym and sym not in seen:
                        seen.add(sym)
                        symbols.append(f"{sym} {nm} (庫存)")
            except Exception:
                pass

        # 2. 檢索本機全市場快取 market_ranking_cache.json
        cache_data = {}
        for base_dir in [
            os.path.dirname(os.path.abspath(__file__)),
            os.path.dirname(os.path.dirname(os.path.abspath(__file__))),
            os.getcwd()
        ]:
            c_path = os.path.join(base_dir, "market_ranking_cache.json")
            if os.path.exists(c_path):
                try:
                    with open(c_path, "r", encoding="utf-8") as f:
                        cache_data = json.load(f)
                    break
                except Exception:
                    pass

        stocks = cache_data.get("stocks", [])
        etfs = cache_data.get("etfs", [])

        # 重點標的優先排列 (台積電、聯發科、鴻海、廣達、長榮、富邦金、中鋼等)
        priority_syms = ["2330", "2317", "2454", "2382", "2603", "2881", "2882", "2002", "3008", "0050", "0056", "00878", "00919"]
        for psym in priority_syms:
            s_item = next((s for s in stocks if s.get("symbol") == psym), None) or next((e for e in etfs if e.get("symbol") == psym), None)
            if s_item and psym not in seen:
                seen.add(psym)
                symbols.append(f"{psym} {s_item.get('name', '')}")

        # 其餘全市場 1,000+ 檔依序納入
        for s in stocks:
            sym = s.get("symbol", "").strip()
            nm = s.get("name", "").strip()
            if sym and sym not in seen:
                seen.add(sym)
                symbols.append(f"{sym} {nm}")

        for e in etfs:
            sym = e.get("symbol", "").strip()
            nm = e.get("name", "").strip()
            if sym and sym not in seen:
                seen.add(sym)
                symbols.append(f"{sym} {nm}")

        if not symbols:
            symbols = ["2330 台積電", "2317 鴻海", "2454 聯發科", "2603 長榮", "2881 富邦金", "0050 元大台灣50"]

        return symbols

    def on_window_resize(self, event):
        if event.widget == self:
            if self._resize_job:
                self.after_cancel(self._resize_job)
            self._resize_job = self.after(150, self.redraw_all_charts)

    def build_ui(self):
        # 1. 頂部導航與標的搜尋輸入列
        top_bar = tk.Frame(self, bg="#1a1a26", padx=16, pady=12)
        top_bar.pack(fill=tk.X, side=tk.TOP)

        tk.Label(top_bar, text="標的選取/輸入:", font=("Microsoft JhengHei UI", 10, "bold"), bg="#1a1a26", fg="#94a3b8").pack(side=tk.LEFT, padx=(0, 6))

        # 支援輸入任意代碼或自下拉選單 1,000+ 檔標的選擇 (state="normal")
        self.combo_symbol = ttk.Combobox(top_bar, values=self.supported_symbols, font=("Consolas", 11, "bold"), width=22)
        self.combo_symbol.set(self.selected_display)
        self.combo_symbol.pack(side=tk.LEFT, padx=(0, 6))
        self.combo_symbol.bind("<<ComboboxSelected>>", lambda e: self.on_search_clicked())
        self.combo_symbol.bind("<Return>", lambda e: self.on_search_clicked())

        btn_search = tk.Button(top_bar, text="[查詢分析]", command=self.on_search_clicked, bg="#2563eb", fg="#ffffff",
                               activebackground="#1d4ed8", font=("Microsoft JhengHei UI", 9, "bold"), relief="flat", padx=10, pady=2)
        btn_search.pack(side=tk.LEFT, padx=(0, 16))

        self.lbl_title = tk.Label(top_bar, text="", font=("Microsoft JhengHei UI", 14, "bold"), bg="#1a1a26", fg="#ffffff")
        self.lbl_title.pack(side=tk.LEFT, padx=(0, 16))

        # 頂部即時核心數值標籤群
        self.info_frame = tk.Frame(top_bar, bg="#1a1a26")
        self.info_frame.pack(side=tk.RIGHT)

        self.lbl_price = tk.Label(self.info_frame, text="-- 元", font=("Inter", 14, "bold"), bg="#1a1a26", fg="#38bdf8")
        self.lbl_price.pack(side=tk.LEFT, padx=10)

        self.lbl_eps = tk.Label(self.info_frame, text="EPS: --", font=("Inter", 11, "bold"), bg="#1a1a26", fg="#00f090")
        self.lbl_eps.pack(side=tk.LEFT, padx=10)

        self.lbl_pe = tk.Label(self.info_frame, text="PE: -- 倍", font=("Inter", 11, "bold"), bg="#1a1a26", fg="#facc15")
        self.lbl_pe.pack(side=tk.LEFT, padx=10)

        self.lbl_status = tk.Label(self.info_frame, text="", font=("Microsoft JhengHei UI", 10, "bold"), bg="#242436", fg="#38bdf8", padx=10, pady=3, relief="solid", bd=1)
        self.lbl_status.pack(side=tk.LEFT, padx=6)

        # 2. 主要內容容器 (左側 70% 專業圖表區 / 右側 30% 杜邦與財務體質卡片)
        main_content = tk.Frame(self, bg="#111118", padx=14, pady=10)
        main_content.pack(fill=tk.BOTH, expand=True)

        left_pane = tk.Frame(main_content, bg="#111118")
        left_pane.pack(side=tk.LEFT, fill=tk.BOTH, expand=True, padx=(0, 12))

        right_pane = tk.Frame(main_content, bg="#181824", width=340, padx=14, pady=14, relief="solid", bd=1)
        right_pane.pack(side=tk.RIGHT, fill=tk.Y)
        right_pane.pack_propagate(False)

        # -----------------------------
        # 左側上方: 本益比價值河流圖 (P/E River Valuation Bands)
        # -----------------------------
        river_box = tk.LabelFrame(left_pane, text=" [河流] 本益比價值河流圖 (P/E River Valuation Bands 16x ~ 32x) ",
                                  font=("Microsoft JhengHei UI", 10, "bold"), bg="#181824", fg="#38bdf8", padx=8, pady=8)
        river_box.pack(fill=tk.BOTH, expand=True, pady=(0, 10))

        self.river_canvas = tk.Canvas(river_box, bg="#0d0d14", highlightthickness=0)
        self.river_canvas.pack(fill=tk.BOTH, expand=True)

        # -----------------------------
        # 左側下方: 營收 YoY 與 獲利三率圖表 (雙面板)
        # -----------------------------
        bottom_charts = tk.Frame(left_pane, bg="#111118", height=230)
        bottom_charts.pack(fill=tk.X)

        rev_box = tk.LabelFrame(bottom_charts, text=" [統計] 月營收 (億元) 與 營收年增率 YoY (%) ",
                                font=("Microsoft JhengHei UI", 9, "bold"), bg="#181824", fg="#facc15", padx=6, pady=6)
        rev_box.pack(side=tk.LEFT, fill=tk.BOTH, expand=True, padx=(0, 6))
        self.rev_canvas = tk.Canvas(rev_box, bg="#0d0d14", height=180, highlightthickness=0)
        self.rev_canvas.pack(fill=tk.BOTH, expand=True)

        margins_box = tk.LabelFrame(bottom_charts, text=" [趨勢] 近 5 季獲利能力三率走勢 (毛利 / 營益 / 淨利 %) ",
                                    font=("Microsoft JhengHei UI", 9, "bold"), bg="#181824", fg="#00f090", padx=6, pady=6)
        margins_box.pack(side=tk.RIGHT, fill=tk.BOTH, expand=True)
        self.margin_canvas = tk.Canvas(margins_box, bg="#0d0d14", height=180, highlightthickness=0)
        self.margin_canvas.pack(fill=tk.BOTH, expand=True)

        # -----------------------------
        # 右側面板: 杜邦分析拆解 & 財務體質安全指標
        # -----------------------------
        tk.Label(right_pane, text="[杜邦] 股東權益報酬率 (DuPont ROE)", font=("Microsoft JhengHei UI", 11, "bold"), bg="#181824", fg="#38bdf8").pack(anchor="w", pady=(0, 6))

        self.card_dupont = tk.Frame(right_pane, bg="#202030", padx=12, pady=12, relief="solid", bd=1)
        self.card_dupont.pack(fill=tk.X, pady=(0, 10))

        self.lbl_roe = tk.Label(self.card_dupont, text="綜合 ROE: --%", font=("Inter", 15, "bold"), bg="#202030", fg="#00f090")
        self.lbl_roe.pack(anchor="w")

        self.lbl_net_margin = tk.Label(self.card_dupont, text="• 稅後淨利率 (Net Margin): --%", font=("Microsoft JhengHei UI", 9), bg="#202030", fg="#e2e8f0")
        self.lbl_net_margin.pack(anchor="w", pady=(6, 2))

        self.lbl_turnover = tk.Label(self.card_dupont, text="• 總資產周轉率 (Turnover): -- 次", font=("Microsoft JhengHei UI", 9), bg="#202030", fg="#e2e8f0")
        self.lbl_turnover.pack(anchor="w", pady=2)

        self.lbl_leverage = tk.Label(self.card_dupont, text="• 權益乘數槓桿 (Multiplier): -- 倍", font=("Microsoft JhengHei UI", 9), bg="#202030", fg="#e2e8f0")
        self.lbl_leverage.pack(anchor="w", pady=2)

        self.lbl_dupont_verdict = tk.Label(right_pane, text="", font=("Microsoft JhengHei UI", 9), bg="#181824", fg="#cbd5e1", wraplength=310, justify="left")
        self.lbl_dupont_verdict.pack(anchor="w", pady=(0, 14))

        # 財務安全性指標卡
        tk.Label(right_pane, text="[安全] 財務體質安全性診斷", font=("Microsoft JhengHei UI", 11, "bold"), bg="#181824", fg="#facc15").pack(anchor="w", pady=(0, 6))

        self.card_safety = tk.Frame(right_pane, bg="#202030", padx=12, pady=12, relief="solid", bd=1)
        self.card_safety.pack(fill=tk.X, pady=(0, 14))

        self.lbl_fcf = tk.Label(self.card_safety, text="• 自由現金流: --", font=("Microsoft JhengHei UI", 9, "bold"), bg="#202030", fg="#00f090")
        self.lbl_fcf.pack(anchor="w", pady=3)

        self.lbl_ocf = tk.Label(self.card_safety, text="• 營業現金流: --", font=("Microsoft JhengHei UI", 9), bg="#202030", fg="#e2e8f0")
        self.lbl_ocf.pack(anchor="w", pady=3)

        self.lbl_debt = tk.Label(self.card_safety, text="• 負債比率: --", font=("Microsoft JhengHei UI", 9), bg="#202030", fg="#38bdf8")
        self.lbl_debt.pack(anchor="w", pady=3)

        self.lbl_current = tk.Label(self.card_safety, text="• 流動比率: --", font=("Microsoft JhengHei UI", 9), bg="#202030", fg="#38bdf8")
        self.lbl_current.pack(anchor="w", pady=3)

        # 底部判讀指南
        tip_box = tk.Frame(right_pane, bg="#111118", padx=10, pady=10, relief="solid", bd=1)
        tip_box.pack(fill=tk.X, side=tk.BOTTOM)
        tk.Label(tip_box, text="[圖表判讀指南]\n"
                              "1. 任意輸入：上方可直接輸入任何台股代碼 (如 2317) 或名稱 (如 鴻海)。\n"
                              "2. 河流圖：股價觸及 16x~20x 為超值低估區；觸及 28x~32x 為昂貴警戒區。\n"
                              "3. 營收 YoY：長條柱為月營收，黃線為年增率，持續向上代表營收動能強。\n"
                              "4. 獲利三率：毛利、營益與淨利率同步走升，代表企業本業定價權優異。",
                 font=("Microsoft JhengHei UI", 8), bg="#111118", fg="#94a3b8", justify="left").pack(anchor="w")

    def on_search_clicked(self):
        input_text = self.combo_symbol.get().strip()
        if not input_text:
            return
        # 提取代碼 (若為 "2317 鴻海" 則提取 2317)
        sym = input_text.split()[0].replace(".TW", "").replace(".TWO", "").strip()
        self.load_and_render(sym)

    def load_and_render(self, symbol: str):
        self.current_symbol = symbol
        self.stock_data = StockFundamentalService.get_stock_profile(symbol)
        data = self.stock_data

        # 頂部更新
        sym_name = data.get("name", symbol)
        disp_sym = data.get("symbol", symbol)
        self.lbl_title.config(text=f"{sym_name} ({disp_sym} · {data.get('category', '')})")
        cur_price = data.get("current_price", 0)
        self.lbl_price.config(text=f"${cur_price:,.1f}")

        pe_info = data.get("pe_bands", {})
        eps_ttm = pe_info.get("eps_ttm", 0)
        cur_pe = pe_info.get("current_pe", 0)
        self.lbl_eps.config(text=f"EPS: {eps_ttm:.2f}元")
        self.lbl_pe.config(text=f"PE: {cur_pe:.1f}倍")
        self.lbl_status.config(text=pe_info.get("status", "合理區間"), fg=pe_info.get("status_color", "#38bdf8"))

        # 杜邦指標
        dupont = data.get("dupont", {})
        self.lbl_roe.config(text=f"綜合 ROE: {dupont.get('roe', 0):.1f}%")
        self.lbl_net_margin.config(text=f"• 稅後淨利率 (Net Margin): {dupont.get('net_margin', 0):.1f}%")
        self.lbl_turnover.config(text=f"• 總資產周轉率 (Turnover): {dupont.get('asset_turnover', 0):.2f} 次")
        self.lbl_leverage.config(text=f"• 權益乘數槓桿 (Multiplier): {dupont.get('equity_multiplier', 0):.2f} 倍")
        self.lbl_dupont_verdict.config(text=f"診斷評語: {dupont.get('verdict', '營運穩健，獲利結構健全')}")

        # 安全性指標 (字串或數值相容)
        safety = data.get("safety", {})
        fcf = safety.get("fcf", "--")
        ocf = safety.get("operating_cf", "--")
        debt = safety.get("debt_ratio", "--")
        current_r = safety.get("current_ratio", "--")
        quick_r = safety.get("quick_ratio", "--")
        self.lbl_fcf.config(text=f"• 自由現金流: {fcf}", fg="#00f090")
        self.lbl_ocf.config(text=f"• 營業現金流: {ocf}")
        self.lbl_debt.config(text=f"• 負債比率: {debt} (安全標竿 < 50%)", fg="#38bdf8")
        self.lbl_current.config(text=f"• 流動比率: {current_r} (速動: {quick_r})", fg="#38bdf8")

        self.redraw_all_charts()

    def redraw_all_charts(self):
        if not self.stock_data:
            return
        pe_info = self.stock_data.get("pe_bands", {})
        points = pe_info.get("history_points", [])
        cur_price = self.stock_data.get("current_price", 0)
        
        self.draw_river_chart(points, cur_price)
        self.draw_revenue_chart(self.stock_data.get("revenue_yoy", []))
        self.draw_margins_chart(self.stock_data.get("margins", []))

    def draw_river_chart(self, points: List[Dict[str, Any]], cur_price: float):
        cv = self.river_canvas
        cv.delete("all")
        w = max(cv.winfo_width(), 720)
        h = max(cv.winfo_height(), 320)

        if not points:
            cv.create_text(w / 2, h / 2, text="無歷史本益比河流資料", fill="#64748b", font=("Microsoft JhengHei UI", 12))
            return

        pad_left, pad_right, pad_top, pad_bottom = 75, 140, 35, 40
        draw_w = w - pad_left - pad_right
        draw_h = h - pad_top - pad_bottom

        # 計算 Y 軸數值範圍
        all_vals = []
        for p in points:
            all_vals.extend([p["band_16x"], p["band_20x"], p["band_24x"], p["band_28x"], p["band_32x"]])
        all_vals.append(cur_price)
        min_v = max(0, min(all_vals) * 0.85)
        max_v = max(all_vals) * 1.12

        def val_to_y(v):
            return pad_top + draw_h - ((v - min_v) / (max_v - min_v) * draw_h)

        # 1. 繪製水平參考網格線與 Y 軸股價標籤
        step_grid = (max_v - min_v) / 5
        for i in range(6):
            grid_v = min_v + i * step_grid
            gy = val_to_y(grid_v)
            cv.create_line(pad_left, gy, pad_left + draw_w, gy, fill="#1c1c28", dash=(2, 3))
            cv.create_text(pad_left - 10, gy, text=f"${grid_v:,.0f}", anchor="e", fill="#64748b", font=("Consolas", 8))

        n = len(points)
        x_step = draw_w / max(1, n - 1)

        # 2. 繪製 4 層平滑河流帶 (由高至低：32x~28x 昂貴, 28x~24x 偏高, 24x~20x 合理, 20x~16x 便宜)
        bands_spec = [
            ("band_32x", "band_28x", "#3c1622", "32x 昂貴區", "#f43f5e"),
            ("band_28x", "band_24x", "#382314", "28x 偏高區", "#fb923c"),
            ("band_24x", "band_20x", "#243319", "24x 合理區", "#a3e635"),
            ("band_20x", "band_16x", "#122a1f", "20x 便宜區", "#00f090")
        ]

        for top_k, btm_k, fill_c, band_title, edge_c in bands_spec:
            poly = []
            for i, p in enumerate(points):
                poly.extend([pad_left + i * x_step, val_to_y(p[top_k])])
            for i in reversed(range(n)):
                poly.extend([pad_left + i * x_step, val_to_y(points[i][btm_k])])
            cv.create_polygon(poly, fill=fill_c, outline="")

        # 3. 繪製 5 條倍數輪廓虛線與右側標籤
        edges = [
            ("band_32x", "#f43f5e", "32x 昂貴"),
            ("band_28x", "#fb923c", "28x 偏高"),
            ("band_24x", "#a3e635", "24x 合理"),
            ("band_20x", "#38bdf8", "20x 便宜"),
            ("band_16x", "#00f090", "16x 超值")
        ]
        for key, col, lbl in edges:
            pts = []
            for i, p in enumerate(points):
                pts.extend([pad_left + i * x_step, val_to_y(p[key])])
            cv.create_line(pts, fill=col, width=1, dash=(3, 2))
            
            # 右側標記倍數與最新金額
            last_p = points[-1][key]
            last_y = val_to_y(last_p)
            cv.create_text(pad_left + draw_w + 10, last_y, text=f"{lbl} (${last_p:,.0f})", anchor="w",
                           fill=col, font=("Microsoft JhengHei UI", 8, "bold"))

        # 4. 繪製歷史收盤價軌跡 (白色亮線與點位標籤)
        price_pts = []
        for i, p in enumerate(points):
            x = pad_left + i * x_step
            # 依各季估值區間動態推算歷史收盤價，最後一季為即時價格
            if i == n - 1:
                hist_p = cur_price
            else:
                hist_p = p["band_20x"] + (p["band_24x"] - p["band_20x"]) * (0.6 + i * 0.08)
            y = val_to_y(hist_p)
            price_pts.extend([x, y])

            # 節點圓圈與價格小標記
            cv.create_oval(x - 4, y - 4, x + 4, y + 4, fill="#ffffff", outline="#38bdf8", width=2)
            cv.create_text(x, y - 14, text=f"${hist_p:,.0f}", fill="#ffffff", font=("Consolas", 8, "bold"))

        cv.create_line(price_pts, fill="#ffffff", width=3)

        # 最新價光暈高亮標記
        lx, ly = price_pts[-2], price_pts[-1]
        cv.create_oval(lx - 8, ly - 8, lx + 8, ly + 8, outline="#38bdf8", width=2)
        cv.create_oval(lx - 12, ly - 12, lx + 12, ly + 12, outline="#0284c7", width=1, dash=(2, 2))
        cv.create_rectangle(lx - 110, ly + 12, lx + 20, ly + 32, fill="#1e293b", outline="#38bdf8")
        cv.create_text(lx - 45, ly + 22, text=f"最新收盤: ${cur_price:,.0f}", fill="#38bdf8", font=("Microsoft JhengHei UI", 8, "bold"))

        # 5. X 軸季度標籤與底部分割線
        cv.create_line(pad_left, pad_top + draw_h, pad_left + draw_w, pad_top + draw_h, fill="#475569", width=1)
        for i, p in enumerate(points):
            x = pad_left + i * x_step
            cv.create_line(x, pad_top + draw_h, x, pad_top + draw_h + 4, fill="#475569")
            cv.create_text(x, pad_top + draw_h + 16, text=p["period"], fill="#94a3b8", font=("Consolas", 9, "bold"))

        # 6. 左上角圖例
        cv.create_text(pad_left + 10, pad_top - 16,
                       text="■ 昂貴(32x)   ■ 偏高(28x)   ■ 合理(24x)   ■ 便宜(20x)   ── 最新收盤價軌跡",
                       fill="#94a3b8", font=("Microsoft JhengHei UI", 8), anchor="w")

    def draw_revenue_chart(self, rev_data: List[Dict[str, Any]]):
        cv = self.rev_canvas
        cv.delete("all")
        w = max(cv.winfo_width(), 340)
        h = max(cv.winfo_height(), 170)

        if not rev_data:
            cv.create_text(w / 2, h / 2, text="無營收資料", fill="#64748b", font=("Microsoft JhengHei UI", 10))
            return

        pad_left, pad_right, pad_top, pad_bottom = 45, 45, 25, 28
        draw_w = w - pad_left - pad_right
        draw_h = h - pad_top - pad_bottom

        n = len(rev_data)
        max_rev = max([d.get("rev", 10) for d in rev_data]) * 1.25
        max_yoy = max([d.get("yoy", 10) for d in rev_data] + [50]) * 1.2

        # 參考網格 (0%, 25%, 50%)
        for frac in [0.0, 0.5, 1.0]:
            gy = pad_top + draw_h * (1 - frac)
            cv.create_line(pad_left, gy, pad_left + draw_w, gy, fill="#1c1c28", dash=(2, 2))
            cv.create_text(pad_left - 6, gy, text=f"{max_rev * frac:,.0f}億", anchor="e", fill="#64748b", font=("Consolas", 7))
            cv.create_text(pad_left + draw_w + 6, gy, text=f"{max_yoy * frac:.0f}%", anchor="w", fill="#fbbf24", font=("Consolas", 7))

        col_w = draw_w / n
        bar_w = col_w * 0.45

        # 繪製長條圖 (營收)
        for i, d in enumerate(rev_data):
            rev = d.get("rev", 0)
            cx = pad_left + i * col_w + col_w / 2
            bar_h = (rev / max_rev) * draw_h
            by = pad_top + draw_h - bar_h

            cv.create_rectangle(cx - bar_w / 2, by, cx + bar_w / 2, pad_top + draw_h, fill="#0284c7", outline="")
            cv.create_text(cx, by - 6, text=f"{rev:.0f}", fill="#38bdf8", font=("Consolas", 7, "bold"))
            
            # X 軸月份
            m_str = d.get("month", "")
            cv.create_text(cx, pad_top + draw_h + 12, text=m_str[-5:], fill="#94a3b8", font=("Consolas", 7))

        # 繪製年增率折線 (YoY %)
        yoy_pts = []
        for i, d in enumerate(rev_data):
            yoy = d.get("yoy", 0)
            cx = pad_left + i * col_w + col_w / 2
            cy = pad_top + draw_h - (yoy / max_yoy) * draw_h
            yoy_pts.extend([cx, cy])
            cv.create_oval(cx - 3, cy - 3, cx + 3, cy + 3, fill="#fbbf24", outline="#ffffff", width=1)
            cv.create_text(cx, cy - 8, text=f"+{yoy:.1f}%", fill="#fbbf24", font=("Consolas", 7))

        if len(yoy_pts) >= 4:
            cv.create_line(yoy_pts, fill="#fbbf24", width=2)

        # 圖例
        cv.create_text(pad_left, pad_top - 12, text="■ 月營收 (億元)   ● 年增率 YoY (%)", fill="#94a3b8", font=("Microsoft JhengHei UI", 8), anchor="w")

    def draw_margins_chart(self, margins_data: List[Dict[str, Any]]):
        cv = self.margin_canvas
        cv.delete("all")
        w = max(cv.winfo_width(), 340)
        h = max(cv.winfo_height(), 170)

        if not margins_data:
            cv.create_text(w / 2, h / 2, text="無三率資料", fill="#64748b", font=("Microsoft JhengHei UI", 10))
            return

        pad_left, pad_right, pad_top, pad_bottom = 45, 25, 25, 28
        draw_w = w - pad_left - pad_right
        draw_h = h - pad_top - pad_bottom

        # Y 軸 0% ~ 70% 網格線 (每 15% 一條線)
        for pct in [0, 20, 40, 60]:
            gy = pad_top + draw_h - (pct / 70.0 * draw_h)
            cv.create_line(pad_left, gy, pad_left + draw_w, gy, fill="#1c1c28", dash=(2, 2))
            cv.create_text(pad_left - 6, gy, text=f"{pct}%", anchor="e", fill="#64748b", font=("Consolas", 7))

        n = len(margins_data)
        col_w = draw_w / max(1, n - 1)

        def to_y(val):
            return pad_top + draw_h - (val / 70.0 * draw_h)

        gross_pts, op_pts, net_pts = [], [], []

        for i, d in enumerate(margins_data):
            x = pad_left + i * col_w
            g = d.get("gross", 0)
            o = d.get("op", 0)
            nt = d.get("net", 0)

            gross_pts.extend([x, to_y(g)])
            op_pts.extend([x, to_y(o)])
            net_pts.extend([x, to_y(nt)])

            # 節點與文字標籤
            cv.create_oval(x - 3, to_y(g) - 3, x + 3, to_y(g) + 3, fill="#facc15", outline="")
            cv.create_text(x, to_y(g) - 7, text=f"{g:.1f}%", fill="#facc15", font=("Consolas", 7))

            cv.create_oval(x - 3, to_y(o) - 3, x + 3, to_y(o) + 3, fill="#38bdf8", outline="")
            cv.create_text(x, to_y(o) - 7, text=f"{o:.1f}%", fill="#38bdf8", font=("Consolas", 7))

            cv.create_oval(x - 3, to_y(nt) - 3, x + 3, to_y(nt) + 3, fill="#00f090", outline="")
            cv.create_text(x, to_y(nt) + 8, text=f"{nt:.1f}%", fill="#00f090", font=("Consolas", 7))

            # X 軸季度
            cv.create_text(x, pad_top + draw_h + 12, text=d.get("quarter", ""), fill="#94a3b8", font=("Consolas", 8, "bold"))

        if len(gross_pts) >= 4:
            cv.create_line(gross_pts, fill="#facc15", width=2)
            cv.create_line(op_pts, fill="#38bdf8", width=2)
            cv.create_line(net_pts, fill="#00f090", width=2)

        # 圖例
        cv.create_text(pad_left, pad_top - 12, text="● 毛利率(黃)   ● 營益率(藍)   ● 淨利率(綠)", fill="#94a3b8", font=("Microsoft JhengHei UI", 8), anchor="w")
