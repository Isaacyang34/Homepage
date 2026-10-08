import tkinter as tk
from tkinter import ttk
from typing import List, Dict, Any, Optional

class KLineChartCanvas(tk.Frame):
    """
    純 Tkinter Canvas 打造的高效互動式專業 K 線圖元件
    包含: K線蠟燭圖、MA5/MA20均線、成交量柱狀圖、十字游標互動
    """
    def __init__(self, parent, records: List[Dict[str, Any]], symbol: str, name: str, **kwargs):
        super().__init__(parent, bg="#181820", **kwargs)
        self.raw_records = records
        self.symbol = symbol
        self.name = name

        self.candle_width = 8
        self.candle_gap = 4
        self.padding_left = 15
        self.padding_right = 75
        self.padding_top = 35
        self.padding_bottom = 25
        self.vol_height_ratio = 0.28 # 成交量佔 28% 高度

        # 配色方案
        self.c_bg = "#181820"
        self.c_grid = "#282835"
        self.c_text = "#9090a0"
        self.c_up = "#ff4d4f"     # 紅漲
        self.c_down = "#52c41a"   # 綠跌
        self.c_flat = "#cccccc"
        self.c_ma5 = "#fadb14"    # 黃色 MA5
        self.c_ma20 = "#b37feb"   # 紫色 MA20
        self.c_crosshair = "#597ef7"

        # 頂部即時懸停行情資訊列
        self.header_frame = tk.Frame(self, bg="#20202a", height=30)
        self.header_frame.pack(fill=tk.X, side=tk.TOP)
        self.info_lbl = tk.Label(self.header_frame, text="", bg="#20202a", fg="#ffffff", font=("Consolas", 10))
        self.info_lbl.pack(anchor="w", padx=10, pady=4)

        # 畫布
        self.canvas = tk.Canvas(self, bg=self.c_bg, highlightthickness=0)
        self.canvas.pack(fill=tk.BOTH, expand=True)

        self.canvas.bind("<Configure>", self._on_resize)
        self.canvas.bind("<Motion>", self._on_mouse_move)
        self.canvas.bind("<Leave>", self._on_mouse_leave)

        self.visible_records: List[Dict[str, Any]] = []
        self.calc_indicators()
        self.set_display_days(60)

    def calc_indicators(self):
        """計算 MA5 與 MA20 均線"""
        closes = [r["close"] for r in self.raw_records]
        for i, r in enumerate(self.raw_records):
            # MA5
            if i >= 4:
                r["ma5"] = sum(closes[i-4:i+1]) / 5.0
            else:
                r["ma5"] = None
            # MA20
            if i >= 19:
                r["ma20"] = sum(closes[i-19:i+1]) / 20.0
            else:
                r["ma20"] = None

    def set_display_days(self, days: int):
        """設定可視天數 (例如 30, 60, 120)"""
        if days <= 0 or days >= len(self.raw_records):
            self.visible_records = list(self.raw_records)
        else:
            self.visible_records = list(self.raw_records[-days:])
        self.redraw()

    def _on_resize(self, event):
        self.redraw()

    def redraw(self):
        """重新繪製完整 K 線與成交量"""
        self.canvas.delete("all")
        if not self.visible_records:
            w = self.canvas.winfo_width() or 800
            h = self.canvas.winfo_height() or 500
            self.canvas.create_text(w // 2, h // 2, text="暫無歷史 K 線資料，請點擊【盤後更新】", fill="#a0a0b0", font=("Microsoft JhengHei UI", 12))
            return

        w = self.canvas.winfo_width()
        h = self.canvas.winfo_height()
        if w < 100 or h < 100:
            return

        # 區域分割
        chart_w = w - self.padding_left - self.padding_right
        total_chart_h = h - self.padding_top - self.padding_bottom
        vol_h = total_chart_h * self.vol_height_ratio
        k_h = total_chart_h - vol_h - 20 # 留 20px 空隙

        k_top = self.padding_top
        k_bottom = k_top + k_h
        vol_top = k_bottom + 20
        vol_bottom = vol_top + vol_h

        n = len(self.visible_records)
        # 動態計算每根 K 棒的寬度
        slot_w = chart_w / n
        self.candle_width = max(3, slot_w * 0.7)
        self.candle_gap = slot_w * 0.3

        # 計算價格極值
        highs = [r["high"] for r in self.visible_records]
        lows = [r["low"] for r in self.visible_records]
        max_p = max(highs)
        min_p = min(lows)
        p_range = max_p - min_p if max_p != min_p else 1.0
        # 留 5% 上下留白
        margin_p = p_range * 0.05
        max_p += margin_p
        min_p = max(0, min_p - margin_p)
        p_range = max_p - min_p

        # 計算成交量極值
        vols = [r["volume"] for r in self.visible_records]
        max_v = max(vols) if vols else 1

        def p_to_y(price):
            return k_bottom - ((price - min_p) / p_range) * k_h

        def v_to_y(volume):
            return vol_bottom - (volume / max_v) * vol_h

        # 1. 繪製水平價格參考網格
        grid_steps = 5
        for i in range(grid_steps + 1):
            val = min_p + (p_range / grid_steps) * i
            y = p_to_y(val)
            self.canvas.create_line(self.padding_left, y, w - self.padding_right, y, fill=self.c_grid, dash=(2, 4))
            self.canvas.create_text(w - self.padding_right + 8, y, text=f"{val:,.1f}", fill=self.c_text, anchor="w", font=("Consolas", 8))

        # 成交量頂部網格線
        self.canvas.create_line(self.padding_left, vol_top, w - self.padding_right, vol_top, fill=self.c_grid, dash=(2, 4))
        self.canvas.create_text(w - self.padding_right + 8, vol_top, text=f"{int(max_v/1000):,}張", fill=self.c_text, anchor="w", font=("Consolas", 8))

        # 2. 記錄每根 K 棒的座標位置 (供滑鼠十字線定位)
        self.bar_coords = []
        ma5_points = []
        ma20_points = []

        highest_idx = 0
        lowest_idx = 0

        for i, r in enumerate(self.visible_records):
            center_x = self.padding_left + i * slot_w + slot_w / 2
            
            o = r["open"]
            h_p = r["high"]
            l_p = r["low"]
            c = r["close"]
            v = r["volume"]

            if h_p > self.visible_records[highest_idx]["high"]:
                highest_idx = i
            if l_p < self.visible_records[lowest_idx]["low"]:
                lowest_idx = i

            y_o = p_to_y(o)
            y_c = p_to_y(c)
            y_h = p_to_y(h_p)
            y_l = p_to_y(l_p)
            y_v = v_to_y(v)

            is_up = c >= o
            color = self.c_up if is_up else self.c_down

            # 繪製上下影線
            self.canvas.create_line(center_x, y_h, center_x, y_l, fill=color, width=1)

            # 繪製 K 棒實體
            half_w = self.candle_width / 2
            top_y = min(y_o, y_c)
            bot_y = max(y_o, y_c)
            if bot_y - top_y < 1:
                bot_y = top_y + 1 # 最少 1px
            
            self.canvas.create_rectangle(center_x - half_w, top_y, center_x + half_w, bot_y, fill=color, outline=color)

            # 繪製成交量柱狀圖
            self.canvas.create_rectangle(center_x - half_w, y_v, center_x + half_w, vol_bottom, fill=color, outline=color)

            # 均線座標點
            if r.get("ma5") is not None:
                ma5_points.append((center_x, p_to_y(r["ma5"])))
            if r.get("ma20") is not None:
                ma20_points.append((center_x, p_to_y(r["ma20"])))

            self.bar_coords.append({
                "x": center_x,
                "record": r,
                "index": i,
                "y_c": y_c
            })

            # 日期軸刻度 (每隔若干根顯示一次)
            date_interval = max(5, n // 8)
            if i % date_interval == 0 or i == n - 1:
                dt_str = r["date"][5:] # MM-DD
                self.canvas.create_text(center_x, h - self.padding_bottom + 8, text=dt_str, fill=self.c_text, font=("Consolas", 8))

        # 3. 繪製 MA5 (黃) 與 MA20 (紫) 折線
        if len(ma5_points) > 1:
            flat_ma5 = [coord for pt in ma5_points for coord in pt]
            self.canvas.create_line(*flat_ma5, fill=self.c_ma5, width=1.5, smooth=True)

        if len(ma20_points) > 1:
            flat_ma20 = [coord for pt in ma20_points for coord in pt]
            self.canvas.create_line(*flat_ma20, fill=self.c_ma20, width=1.5, smooth=True)

        # 4. 最高點與最低點標示
        if self.bar_coords:
            h_pt = self.bar_coords[highest_idx]
            l_pt = self.bar_coords[lowest_idx]
            self.canvas.create_text(h_pt["x"], p_to_y(h_pt["record"]["high"]) - 10, 
                                    text=f"▲ {h_pt['record']['high']:.2f}", fill="#ff7875", font=("Consolas", 8, "bold"))
            self.canvas.create_text(l_pt["x"], p_to_y(l_pt["record"]["low"]) + 10, 
                                    text=f"▼ {l_pt['record']['low']:.2f}", fill="#95de64", font=("Consolas", 8, "bold"))

        # 預設頂部顯示最後一日的數據
        if self.bar_coords:
            self._update_header_info(self.bar_coords[-1]["record"])

    def _update_header_info(self, r: Dict[str, Any]):
        """更新頂部資訊標籤"""
        c = r["close"]
        o = r["open"]
        h = r["high"]
        l = r["low"]
        v = r["volume"]
        vol_str = f"{v // 1000:,} 張" if v >= 1000 else f"{v:,} 股"
        
        chg = c - o
        chg_pct = (chg / o * 100) if o > 0 else 0.0
        chg_sign = "+" if chg > 0 else ""
        chg_color = self.c_up if chg > 0 else (self.c_down if chg < 0 else self.c_flat)

        ma5_str = f"MA5:{r['ma5']:.2f}" if r.get("ma5") else "MA5:--"
        ma20_str = f"MA20:{r['ma20']:.2f}" if r.get("ma20") else "MA20:--"

        info_text = (
            f"[{self.symbol} {self.name}]  日期: {r['date']}  "
            f"開盤: {o:.2f}  最高: {h:.2f}  最低: {l:.2f}  收盤: {c:.2f} ({chg_sign}{chg_pct:.2f}%)  "
            f"成交量: {vol_str}  |  {ma5_str}  {ma20_str}"
        )
        self.info_lbl.configure(text=info_text, foreground="#ffffff")

    def _on_mouse_move(self, event):
        """滑鼠移動時更新十字游標與懸浮數據"""
        if not hasattr(self, "bar_coords") or not self.bar_coords:
            return

        w = self.canvas.winfo_width()
        h = self.canvas.winfo_height()

        # 尋找離滑鼠 x 最近的 K 棒
        mx = event.x
        my = event.y

        closest_bar = min(self.bar_coords, key=lambda b: abs(b["x"] - mx))
        bx = closest_bar["x"]

        # 刪除舊十字線
        self.canvas.delete("crosshair")

        # 繪製垂直十字線
        self.canvas.create_line(bx, self.padding_top, bx, h - self.padding_bottom, 
                                fill=self.c_crosshair, dash=(3, 3), tags="crosshair")

        # 繪製水平十字線
        if self.padding_top <= my <= h - self.padding_bottom:
            self.canvas.create_line(self.padding_left, my, w - self.padding_right, my, 
                                    fill=self.c_crosshair, dash=(3, 3), tags="crosshair")

        # 更新頂部詳細數據
        self._update_header_info(closest_bar["record"])

    def _on_mouse_leave(self, event):
        """滑鼠離開時清空十字游標"""
        self.canvas.delete("crosshair")
        if hasattr(self, "bar_coords") and self.bar_coords:
            self._update_header_info(self.bar_coords[-1]["record"])
