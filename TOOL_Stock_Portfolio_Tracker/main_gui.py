import tkinter as tk
from tkinter import ttk, messagebox
import threading
import time
from datetime import datetime
from typing import Dict, List, Any, Optional

from database import (
    init_db, get_all_positions, add_position, update_position, 
    delete_position, get_history_kline, get_setting, set_setting,
    get_trade_lots, add_trade_lot, delete_trade_lot, get_price_benchmarks,
    get_visible_columns, set_visible_columns, get_visible_cards, set_visible_cards,
    DEFAULT_VISIBLE_COLUMNS, DEFAULT_VISIBLE_CARDS
)
from quote_service import QuoteService
from history_service import HistoryService
from dividend_service import DividendService
from pnl_calculator import calculate_position_pnl
from kline_chart import KLineChartCanvas
from stock_detector import detect_stock_metadata
from crypto_sync import sync_to_cloud, fetch_and_decrypt_from_cloud, DEFAULT_FIREBASE_URL, get_or_create_secret_key

def clean_number(val_str: str, default: float = 0.0) -> float:
    """寬容解析數值：自動相容千分位逗號、全形小數點(．/。)、全形逗點與貨幣符號"""
    if not val_str:
        return default
    s = str(val_str).strip()
    s = s.replace("，", "").replace(",", "")
    s = s.replace("．", ".").replace("。", ".")
    s = s.replace("$", "").replace("NT", "").replace("nt", "").strip()
    try:
        return float(s)
    except ValueError:
        return default

# 欄位完整定義清單 (欄位ID, 顯示名稱, 預設寬度, 對齊方式, 分類)
ALL_COLUMN_SPECS = [
    ("symbol", "代碼", 75, "center", "基本"),
    ("name", "名稱", 105, "center", "基本"),
    ("market", "市場", 55, "center", "基本"),
    ("shares", "持股數", 75, "e", "部位"),
    ("cost_price", "成本均價", 85, "e", "部位"),
    ("current_price", "現價", 85, "e", "行情"),
    ("change_pct", "今日漲跌", 90, "e", "行情"),
    ("total_cost", "總成本", 95, "e", "損益"),
    ("market_val", "預估市值", 95, "e", "損益"),
    ("unrealized_pnl", "未實現損益", 105, "e", "損益"),
    ("roi_pct", "報酬率%", 80, "e", "損益"),
    ("day_pnl", "日損益", 95, "e", "週期損益"),
    ("week_pnl", "週損益", 95, "e", "週期損益"),
    ("month_pnl", "月損益", 95, "e", "週期損益"),
    ("frequency", "分配頻率", 75, "center", "股息"),
    ("cash_dividend", "每股年股息(單期)", 125, "e", "股息"),
    ("total_dividend", "預估年總股息", 100, "e", "股息"),
    ("yield_on_cost", "成本殖利率%", 90, "e", "股息"),
    ("ex_date", "除息日期/期別", 155, "center", "股息"),
    ("payment_month", "預估發放月", 85, "center", "股息"),
    ("hist_div_received", "累計已領股息", 105, "e", "股息歷史"),
    ("note", "備註", 100, "w", "其他")
]

# 卡片完整定義清單 (卡片ID, 標題, 預設顏色, 說明)
ALL_CARD_SPECS = [
    ("total_cost", "總投入成本 (NT$)", "#ffffff", "全庫存買入總成本"),
    ("market_val", "庫存總市值 (NT$)", "#ffffff", "現價預估總市值"),
    ("total_pnl", "總未實現損益 (NT$)", "#ffffff", "累積總損益與淨報酬率"),
    ("day_pnl", "今日損益 (NT$)", "#ffffff", "相比昨日收盤"),
    ("week_pnl", "本週損益 (NT$)", "#ffffff", "相比 5 日前收盤"),
    ("month_pnl", "本月損益 (NT$)", "#ffffff", "相比 20 日前收盤"),
    ("total_div", "預估年總股息 (殖利率)", "#ffd166", "全庫存預估全年被動現金流"),
    ("hist_div", "歷年累計已領股息", "#a0e0a0", "依買入取得時間精算"),
    ("stock_count", "在庫標的數", "#3a86ff", "持股檔數與批次數")
]

class PortfolioApp(tk.Tk):
    def __init__(self):
        super().__init__()
        self.title("本地端台美股庫存即時損益、歷史與股息追蹤系統 (Stock Portfolio Tracker)")
        self.geometry("1420x820")
        self.minsize(1120, 640)

        # 初始化資料庫
        init_db()

        self.quote_service = QuoteService()
        self.positions: List[Dict[str, Any]] = []
        self.latest_quotes: Dict[str, Dict[str, Any]] = {}
        self.dividend_cache: Dict[str, Dict[str, Any]] = {}
        self.benchmarks_cache: Dict[str, Dict[str, float]] = {}

        self.auto_refresh_active = True
        self.refresh_interval = int(get_setting("refresh_interval", "10"))
        self.color_mode = get_setting("color_mode", "tw")

        self.visible_columns = get_visible_columns()
        self.visible_cards = get_visible_cards()

        self._is_fetching = False
        self._sort_reverse = False
        self._sort_col = "unrealized_pnl"
        self.is_history_updating = False

        # 設定風格與配色
        self.setup_styles()
        self.build_ui()

        # 初始載入庫存與股息並啟動背景輪詢
        self.reload_positions()
        self.trigger_refresh()
        self.trigger_dividend_update(silent=True)
        self.start_auto_refresh_timer()

    def setup_styles(self):
        self.style = ttk.Style(self)
        try:
            self.style.theme_use("clam")
        except Exception:
            pass

        self.bg_color = "#1e1e24"
        self.card_bg = "#2b2b36"
        self.text_color = "#ffffff"
        self.muted_text = "#a0a0b0"
        self.accent_blue = "#3a86ff"
        
        self.configure(bg=self.bg_color)

        self.style.configure(".", background=self.bg_color, foreground=self.text_color, font=("Microsoft JhengHei UI", 10))
        self.style.configure("TFrame", background=self.bg_color)
        self.style.configure("Card.TFrame", background=self.card_bg, relief="flat")
        self.style.configure("TLabel", background=self.bg_color, foreground=self.text_color, font=("Microsoft JhengHei UI", 10))
        self.style.configure("CardTitle.TLabel", background=self.card_bg, foreground=self.muted_text, font=("Microsoft JhengHei UI", 9))
        self.style.configure("CardVal.TLabel", background=self.card_bg, foreground="#ffffff", font=("Microsoft JhengHei UI", 13, "bold"))

        self.style.configure("Treeview", 
                             background="#252530", 
                             foreground="#f0f0f0", 
                             fieldbackground="#252530",
                             rowheight=32,
                             font=("Microsoft JhengHei UI", 10))
        self.style.configure("Treeview.Heading", 
                             background="#323242", 
                             foreground="#ffffff", 
                             relief="flat", 
                             font=("Microsoft JhengHei UI", 10, "bold"))
        self.style.map("Treeview", background=[("selected", "#3a86ff")], foreground=[("selected", "#ffffff")])
        self.style.map("Treeview.Heading", background=[("active", "#404055")])
        self.style.configure("Action.TButton", font=("Microsoft JhengHei UI", 10, "bold"), padding=5)

    def build_ui(self):
        """建構主介面排版 (依據自訂設定動態生成卡片與表格)"""
        # 1. 頂部儀表板卡片
        self.top_cards_container = ttk.Frame(self, padding=(12, 10, 12, 5))
        self.top_cards_container.pack(fill=tk.X)
        self.rebuild_cards()

        # 2. 中間庫存清單表格
        self.mid_frame = ttk.Frame(self, padding=(12, 5, 12, 5))
        self.mid_frame.pack(fill=tk.BOTH, expand=True)
        self.rebuild_table()

        # 3. 底部功能按鈕與工具列
        bottom_frame = ttk.Frame(self, padding=(12, 8, 12, 12))
        bottom_frame.pack(fill=tk.X)

        btn_add = ttk.Button(bottom_frame, text="[+] 新增持股", command=self.on_add_position, style="Action.TButton")
        btn_add.pack(side=tk.LEFT, padx=2)

        btn_edit = ttk.Button(bottom_frame, text="[筆] 修改持股", command=self.on_edit_position, style="Action.TButton")
        btn_edit.pack(side=tk.LEFT, padx=2)

        btn_lots = ttk.Button(bottom_frame, text="[批次] 取得時間管理", command=self.on_manage_lots, style="Action.TButton")
        btn_lots.pack(side=tk.LEFT, padx=2)

        btn_del = ttk.Button(bottom_frame, text="[-] 刪除", command=self.on_delete_position, style="Action.TButton")
        btn_del.pack(side=tk.LEFT, padx=2)

        ttk.Separator(bottom_frame, orient="vertical").pack(side=tk.LEFT, fill=tk.Y, padx=5)

        btn_refresh = ttk.Button(bottom_frame, text="[更新] 刷新行情", command=self.trigger_refresh, style="Action.TButton")
        btn_refresh.pack(side=tk.LEFT, padx=2)

        btn_div = ttk.Button(bottom_frame, text="[股息] 同步除權息", command=lambda: self.trigger_dividend_update(silent=False), style="Action.TButton")
        btn_div.pack(side=tk.LEFT, padx=2)

        self.btn_post_market = ttk.Button(bottom_frame, text="[盤後] 更新歷史資料", command=self.on_update_history_all, style="Action.TButton")
        self.btn_post_market.pack(side=tk.LEFT, padx=2)

        btn_view_kline = ttk.Button(bottom_frame, text="[K線] 個股走勢圖", command=self.on_view_kline, style="Action.TButton")
        btn_view_kline.pack(side=tk.LEFT, padx=2)

        btn_settings = ttk.Button(bottom_frame, text="[⚙ 設定] 介面欄位", command=self.on_open_settings, style="Action.TButton")
        btn_settings.pack(side=tk.LEFT, padx=2)

        btn_cloud = ttk.Button(bottom_frame, text="[☁ 雲端] 加密同步", command=self.on_open_cloud_sync, style="Action.TButton")
        btn_cloud.pack(side=tk.LEFT, padx=2)

        # 右側控制：自動刷新頻率
        self.auto_refresh_var = tk.BooleanVar(value=True)
        chk_auto = ttk.Checkbutton(bottom_frame, text="自動刷新", variable=self.auto_refresh_var, command=self.on_toggle_auto_refresh)
        chk_auto.pack(side=tk.RIGHT, padx=4)

        self.interval_var = tk.StringVar(value=str(self.refresh_interval))
        combo_interval = ttk.Combobox(bottom_frame, textvariable=self.interval_var, values=["5", "10", "15", "30", "60"], width=3, state="readonly")
        combo_interval.pack(side=tk.RIGHT, padx=2)
        combo_interval.bind("<<ComboboxSelected>>", self.on_interval_changed)
        ttk.Label(bottom_frame, text="秒數:").pack(side=tk.RIGHT)

        # 4. 最底部全域狀態列 (背景更新進度與狀態提示，完全不佔用主視窗)
        self.status_bar = tk.Frame(self, bg="#13131a", height=26)
        self.status_bar.pack(fill=tk.X, side=tk.BOTTOM)

        self.status_lbl = tk.Label(self.status_bar, text="系統就緒", bg="#13131a", fg=self.muted_text, font=("Microsoft JhengHei UI", 9))
        self.status_lbl.pack(side=tk.LEFT, padx=12, pady=3)

        # 盤後更新進度提示標籤 (右側醒目顯示)
        self.history_progress_lbl = tk.Label(self.status_bar, text="", bg="#13131a", fg="#52c41a", font=("Microsoft JhengHei UI", 9))
        self.history_progress_lbl.pack(side=tk.RIGHT, padx=12, pady=3)

        # 盤後更新進度條 (平時隱藏，更新時顯示)
        self.history_pbar = ttk.Progressbar(self.status_bar, orient="horizontal", length=160, mode="determinate")

    def rebuild_cards(self):
        """依據 visible_cards 動態建構頂部卡片"""
        for child in self.top_cards_container.winfo_children():
            child.destroy()

        self.cards = {}
        spec_dict = {s[0]: s for s in ALL_CARD_SPECS}

        for card_id in self.visible_cards:
            if card_id not in spec_dict:
                continue
            cid, title, text_col, desc = spec_dict[card_id]
            card = ttk.Frame(self.top_cards_container, style="Card.TFrame", padding=(10, 7))
            card.pack(side=tk.LEFT, fill=tk.BOTH, expand=True, padx=3)
            lbl_title = ttk.Label(card, text=title, style="CardTitle.TLabel")
            lbl_title.pack(anchor="w")
            lbl_val = ttk.Label(card, text="$ 0", style="CardVal.TLabel", foreground=text_col)
            lbl_val.pack(anchor="w", pady=(2, 0))
            self.cards[cid] = lbl_val

    def rebuild_table(self):
        """依據 visible_columns 動態建構 Treeview 欄位"""
        for child in self.mid_frame.winfo_children():
            child.destroy()

        spec_dict = {s[0]: s for s in ALL_COLUMN_SPECS}
        active_specs = [spec_dict[cid] for cid in self.visible_columns if cid in spec_dict]

        self.tree = ttk.Treeview(self.mid_frame, columns=[s[0] for s in active_specs], show="headings", selectmode="browse")
        vsb = ttk.Scrollbar(self.mid_frame, orient="vertical", command=self.tree.yview)
        hsb = ttk.Scrollbar(self.mid_frame, orient="horizontal", command=self.tree.xview)
        self.tree.configure(yscrollcommand=vsb.set, xscrollcommand=hsb.set)

        for col_id, col_name, width, align, category in active_specs:
            self.tree.heading(col_id, text=col_name, command=lambda c=col_id: self.sort_tree(c))
            self.tree.column(col_id, width=width, anchor=align)

        self.tree.grid(row=0, column=0, sticky="nsew")
        vsb.grid(row=0, column=1, sticky="ns")
        hsb.grid(row=1, column=0, sticky="ew")

        self.mid_frame.grid_rowconfigure(0, weight=1)
        self.mid_frame.grid_columnconfigure(0, weight=1)

        self.tree.bind("<Double-1>", lambda e: self.on_edit_position())
        self.update_color_tags()

    def update_color_tags(self):
        if self.color_mode == "tw":
            self.up_color = "#ff4d4f"
            self.down_color = "#52c41a"
        else:
            self.up_color = "#52c41a"
            self.down_color = "#ff4d4f"

        self.tree.tag_configure("up", foreground=self.up_color)
        self.tree.tag_configure("down", foreground=self.down_color)
        self.tree.tag_configure("flat", foreground="#ffffff")

    def reload_positions(self):
        self.positions = get_all_positions()
        if "stock_count" in self.cards:
            self.cards["stock_count"].configure(text=f"{len(self.positions)} 檔")

    def trigger_dividend_update(self, silent: bool = False):
        if not self.positions:
            return
        if not silent:
            self.status_lbl.configure(text="正在查詢證交所與除權息公告...")

        def worker():
            updated_any = False
            for pos in self.positions:
                sym = pos["symbol"]
                mkt = pos.get("market", "TW")
                try:
                    info = DividendService.get_stock_dividend_info(sym, mkt)
                    self.dividend_cache[sym] = info
                    updated_any = True
                except Exception as e:
                    print(f"[App] 獲取 {sym} 股息失敗: {e}")
            if updated_any:
                self.after(0, self._on_dividend_updated)

        threading.Thread(target=worker, daemon=True).start()

    def _on_dividend_updated(self):
        self.refresh_ui_table()
        self.status_lbl.configure(text="股息與除息日期已同步")

    def refresh_ui_table(self):
        """重新計算並填入表格與頂部卡片 (包含日/週/月週期損益與取得批次股息)"""
        selected_iid = self.tree.focus()
        self.tree.delete(*self.tree.get_children())

        total_cost_sum = 0.0
        market_val_sum = 0.0
        total_pnl_sum = 0.0
        day_pnl_sum = 0.0
        week_pnl_sum = 0.0
        month_pnl_sum = 0.0
        total_div_sum = 0.0
        hist_div_sum = 0.0

        pnl_records = []

        for pos in self.positions:
            sym = pos["symbol"].upper()
            quote = self.latest_quotes.get(sym, {
                "current_price": pos["cost_price"],
                "yesterday_close": pos["cost_price"],
                "change": 0.0,
                "change_pct": 0.0
            })
            
            # 取得日/週/月歷史基準價
            if sym not in self.benchmarks_cache:
                self.benchmarks_cache[sym] = get_price_benchmarks(sym, quote["current_price"])
            benchmarks = self.benchmarks_cache[sym]

            # 取得股息資訊 (含頻率、發放月、依批次計算已領歷史股息)
            div_info = self.dividend_cache.get(sym) or DividendService.get_stock_dividend_info(sym, pos.get("market", "TW"))

            pnl = calculate_position_pnl(pos, quote, benchmarks, div_info)
            pnl["id"] = pos["id"]
            pnl["market"] = pos.get("market", "TW")
            pnl["note"] = pos.get("note", "")
            pnl_records.append(pnl)

            total_cost_sum += pnl["total_cost"]
            market_val_sum += pnl["market_val"]
            total_pnl_sum += pnl["unrealized_pnl"]
            day_pnl_sum += pnl["day_pnl"]
            week_pnl_sum += pnl["week_pnl"]
            month_pnl_sum += pnl["month_pnl"]
            total_div_sum += pnl["total_dividend"]
            hist_div_sum += pnl.get("hist_div_received", 0.0)

        # 排序
        pnl_records.sort(key=lambda x: x.get(self._sort_col, 0), reverse=self._sort_reverse)

        for pnl in pnl_records:
            change = pnl["change"]
            tag = "up" if change > 0 else ("down" if change < 0 else "flat")
            
            pnl_val = pnl["unrealized_pnl"]
            pnl_sign = "+" if pnl_val > 0 else ""
            chg_sign = "+" if change > 0 else ""
            day_sign = "+" if pnl["day_pnl"] > 0 else ""
            week_sign = "+" if pnl["week_pnl"] > 0 else ""
            month_sign = "+" if pnl["month_pnl"] > 0 else ""

            ann_div = pnl["cash_dividend"]
            single_d = pnl.get("single_dividend", ann_div)
            if ann_div > 0 and abs(single_d - ann_div) > 0.001:
                div_val_str = f"{ann_div:.2f} (期{single_d:.2f})"
            elif ann_div > 0:
                div_val_str = f"{ann_div:.2f}"
            else:
                div_val_str = "--"

            ex_d = pnl.get("ex_date", "")
            status_txt = pnl.get("div_status", "")
            if ex_d and ex_d != "無":
                div_display = f"{ex_d} [{status_txt}]"
            elif status_txt:
                div_display = f"[{status_txt}]"
            else:
                div_display = "暫無資料"

            shares_val = pnl["shares"]
            shares_str = f"{shares_val:,.0f}" if shares_val.is_integer() else f"{shares_val:,.2f}"

            # 累計已領歷史股息文字
            hist_val = pnl.get("hist_div_received", 0.0)
            lot_cnt = pnl.get("lot_count", 0)
            if lot_cnt > 0:
                hist_div_str = f"${hist_val:,.0f} ({lot_cnt}筆)"
            else:
                hist_div_str = "-- (未設批次)"

            # 動態組裝欄位值
            val_map = {
                "symbol": pnl["symbol"],
                "name": pnl["name"],
                "market": pnl["market"],
                "shares": shares_str,
                "cost_price": f"{pnl['cost_price']:,.2f}",
                "current_price": f"{pnl['current_price']:,.2f}",
                "change_pct": f"{chg_sign}{pnl['change']:,.2f} ({chg_sign}{pnl['change_pct']:.2f}%)",
                "total_cost": f"{pnl['total_cost']:,}",
                "market_val": f"{pnl['market_val']:,}",
                "unrealized_pnl": f"{pnl_sign}{pnl['unrealized_pnl']:,}",
                "roi_pct": f"{pnl_sign}{pnl['roi_pct']:.2f}%",
                "day_pnl": f"{day_sign}{pnl['day_pnl']:,} ({day_sign}{pnl['day_pct']:.2f}%)",
                "week_pnl": f"{week_sign}{pnl['week_pnl']:,} ({week_sign}{pnl['week_pct']:.2f}%)",
                "month_pnl": f"{month_sign}{pnl['month_pnl']:,} ({month_sign}{pnl['month_pct']:.2f}%)",
                "frequency": pnl.get("frequency", "--"),
                "cash_dividend": div_val_str,
                "total_dividend": f"{pnl['total_dividend']:,}",
                "yield_on_cost": f"{pnl['yield_on_cost']:.2f}%",
                "ex_date": div_display,
                "payment_month": pnl.get("payment_month", "--"),
                "hist_div_received": hist_div_str,
                "note": pnl["note"]
            }

            row_values = tuple(val_map.get(cid, "") for cid in self.visible_columns)
            item_id = str(pnl["id"])
            self.tree.insert("", tk.END, iid=item_id, values=row_values, tags=(tag,))

        if selected_iid and self.tree.exists(selected_iid):
            self.tree.selection_set(selected_iid)
            self.tree.focus(selected_iid)

        # 更新可見的頂部卡片
        if "total_cost" in self.cards:
            self.cards["total_cost"].configure(text=f"$ {total_cost_sum:,.0f}")
        if "market_val" in self.cards:
            self.cards["market_val"].configure(text=f"$ {market_val_sum:,.0f}")
        
        roi_total = (total_pnl_sum / total_cost_sum * 100) if total_cost_sum > 0 else 0.0
        pnl_color = self.up_color if total_pnl_sum > 0 else (self.down_color if total_pnl_sum < 0 else "#ffffff")
        pnl_sign = "+" if total_pnl_sum > 0 else ""
        if "total_pnl" in self.cards:
            self.cards["total_pnl"].configure(text=f"$ {pnl_sign}{total_pnl_sum:,.0f} ({pnl_sign}{roi_total:.2f}%)", foreground=pnl_color)

        if "day_pnl" in self.cards:
            day_color = self.up_color if day_pnl_sum > 0 else (self.down_color if day_pnl_sum < 0 else "#ffffff")
            self.cards["day_pnl"].configure(text=f"$ {'+' if day_pnl_sum > 0 else ''}{day_pnl_sum:,.0f}", foreground=day_color)

        if "week_pnl" in self.cards:
            week_color = self.up_color if week_pnl_sum > 0 else (self.down_color if week_pnl_sum < 0 else "#ffffff")
            self.cards["week_pnl"].configure(text=f"$ {'+' if week_pnl_sum > 0 else ''}{week_pnl_sum:,.0f}", foreground=week_color)

        if "month_pnl" in self.cards:
            month_color = self.up_color if month_pnl_sum > 0 else (self.down_color if month_pnl_sum < 0 else "#ffffff")
            self.cards["month_pnl"].configure(text=f"$ {'+' if month_pnl_sum > 0 else ''}{month_pnl_sum:,.0f}", foreground=month_color)

        if "total_div" in self.cards:
            avg_yield = (total_div_sum / total_cost_sum * 100) if total_cost_sum > 0 else 0.0
            self.cards["total_div"].configure(text=f"$ {total_div_sum:,.0f} ({avg_yield:.2f}%)")

        if "hist_div" in self.cards:
            self.cards["hist_div"].configure(text=f"$ {hist_div_sum:,.0f}")

        if "stock_count" in self.cards:
            self.cards["stock_count"].configure(text=f"{len(self.positions)} 檔")

    def trigger_refresh(self):
        if self._is_fetching:
            return
        if not self.positions:
            self.status_lbl.configure(text="尚未新增任何持股")
            return

        self._is_fetching = True
        self.status_lbl.configure(text="正在獲取即時報價...")

        def worker():
            try:
                quotes = self.quote_service.fetch_realtime_quotes(self.positions)
                self.latest_quotes.update(quotes)
                now_str = datetime.now().strftime("%H:%M:%S")
                self.after(0, lambda: self._on_fetch_success(now_str))
            except Exception as e:
                self.after(0, lambda: self._on_fetch_failed(str(e)))

        threading.Thread(target=worker, daemon=True).start()

    def _on_fetch_success(self, update_time: str):
        self._is_fetching = False
        self.refresh_ui_table()
        self.status_lbl.configure(text=f"最後更新時間: {update_time}")

    def _on_fetch_failed(self, err_msg: str):
        self._is_fetching = False
        self.status_lbl.configure(text=f"報價抓取失敗: {err_msg}")

    def start_auto_refresh_timer(self):
        def timer_tick():
            if self.auto_refresh_var.get() and not self._is_fetching:
                self.trigger_refresh()
            interval_ms = max(4000, self.refresh_interval * 1000)
            self.after(interval_ms, timer_tick)

        self.after(self.refresh_interval * 1000, timer_tick)

    def on_toggle_auto_refresh(self):
        if self.auto_refresh_var.get():
            self.status_lbl.configure(text="已開啟自動刷新")
            self.trigger_refresh()
        else:
            self.status_lbl.configure(text="已暫停自動刷新")

    def on_interval_changed(self, event):
        val = int(self.interval_var.get())
        self.refresh_interval = val
        set_setting("refresh_interval", str(val))

    def sort_tree(self, col: str):
        if self._sort_col == col:
            self._sort_reverse = not self._sort_reverse
        else:
            self._sort_col = col
            self._sort_reverse = True
        self.refresh_ui_table()

    # --- 持股與批次對話框 ---

    def on_add_position(self):
        PositionEditDialog(self, title="新增持股部位", on_saved=self._on_position_saved)

    def on_edit_position(self):
        selected = self.tree.selection()
        if not selected:
            messagebox.showinfo("提示", "請先點選欲修改的持股！", parent=self)
            return
        pos_id = int(selected[0])
        pos = next((p for p in self.positions if p["id"] == pos_id), None)
        if pos:
            PositionEditDialog(self, title="修改持股部位", pos=pos, on_saved=self._on_position_saved)

    def on_manage_lots(self):
        """開啟買入批次管理 (取得時間明細)"""
        selected = self.tree.selection()
        if not selected:
            messagebox.showinfo("提示", "請先點選欲管理買入批次的股票！", parent=self)
            return
        pos_id = int(selected[0])
        pos = next((p for p in self.positions if p["id"] == pos_id), None)
        if pos:
            TradeLotManagerDialog(self, symbol=pos["symbol"], name=pos["name"], on_lots_changed=self._on_position_saved)

    def on_delete_position(self):
        selected = self.tree.selection()
        if not selected:
            messagebox.showinfo("提示", "請先點選欲刪除的持股！", parent=self)
            return
        pos_id = int(selected[0])
        pos = next((p for p in self.positions if p["id"] == pos_id), None)
        if not pos:
            return
        confirm = messagebox.askyesno("確認刪除", f"確定要從本地庫存刪除 {pos['symbol']} ({pos['name']}) 及其所有買入批次嗎？", parent=self)
        if confirm:
            delete_position(pos_id)
            self.reload_positions()
            self.refresh_ui_table()
            messagebox.showinfo("成功", "已成功刪除持股！", parent=self)

    def _on_position_saved(self):
        self.reload_positions()
        self.benchmarks_cache.clear()
        self.trigger_refresh()
        self.trigger_dividend_update(silent=True)

    def on_open_settings(self):
        SettingsDialog(self, on_applied=self._on_settings_applied)

    def on_open_cloud_sync(self):
        CloudSyncDialog(self, on_restored=self._on_position_saved)

    def _on_settings_applied(self):
        self.visible_columns = get_visible_columns()
        self.visible_cards = get_visible_cards()
        self.rebuild_cards()
        self.rebuild_table()
        self.refresh_ui_table()

    # --- 盤後更新與歷史查詢 (背景非同步執行，不佔用視窗) ---

    def on_update_history_all(self):
        """背景非同步更新盤後歷史資料 (不佔用視窗、不鎖定畫面、底部狀態列顯示進度)"""
        if self.is_history_updating:
            messagebox.showinfo("提示", "盤後歷史資料已在背景更新中，請稍候...", parent=self)
            return

        if not self.positions:
            messagebox.showinfo("提示", "目前沒有持股可更新！", parent=self)
            return

        self.is_history_updating = True
        self.btn_post_market.configure(text="[盤後] 更新中...", state=tk.DISABLED)
        self.history_pbar.pack(side=tk.RIGHT, padx=8, pady=3)
        self.history_pbar["value"] = 0
        self.history_pbar["maximum"] = len(self.positions)
        self.history_progress_lbl.configure(text=f"[盤後更新中 0/{len(self.positions)}] 準備開始增量下載...", fg="#52c41a")

        threading.Thread(target=self._run_background_history_update, daemon=True).start()

    def _run_background_history_update(self):
        total = len(self.positions)
        updated_stocks = 0
        total_days = 0

        def on_step_progress(msg: str):
            self.after(0, lambda m=msg: self._update_history_progress_text(m))

        try:
            for idx, pos in enumerate(self.positions, 1):
                sym = pos["symbol"]
                name = pos.get("name", sym)
                mkt = pos.get("market", "TW")
                self.after(0, lambda i=idx, s=sym, n=name: self._update_history_step(i, total, s, n))
                
                days = HistoryService.update_stock_history(sym, mkt, on_progress=on_step_progress)
                if days > 0:
                    total_days += days
                updated_stocks += 1

            self.after(0, lambda: self._on_history_update_completed(updated_stocks, total_days))
        except Exception as e:
            self.after(0, lambda err=str(e): self._on_history_update_failed(err))

    def _update_history_step(self, cur_idx: int, total: int, sym: str, name: str):
        self.history_pbar["value"] = cur_idx - 1
        self.history_progress_lbl.configure(
            text=f"[盤後更新中 {cur_idx}/{total}] 正在同步 {sym} {name}...", 
            fg="#52c41a"
        )

    def _update_history_progress_text(self, msg: str):
        # 顯示更詳細的細項進度，例如月份或狀態
        if len(msg) > 42:
            msg = msg[:39] + "..."
        self.history_progress_lbl.configure(text=msg, fg="#87d068")

    def _on_history_update_completed(self, count: int, total_days: int):
        self.is_history_updating = False
        self.btn_post_market.configure(text="[盤後] 更新歷史資料", state=tk.NORMAL)
        self.history_pbar["value"] = self.history_pbar["maximum"]
        self.history_progress_lbl.configure(
            text=f"✔ 盤後歷史更新完成 (共 {count} 檔，新增 {total_days} 筆日K)", 
            fg="#52c41a"
        )
        self.after(8000, self._reset_history_progress_ui)

    def _on_history_update_failed(self, err: str):
        self.is_history_updating = False
        self.btn_post_market.configure(text="[盤後] 更新歷史資料", state=tk.NORMAL)
        self.history_pbar.pack_forget()
        self.history_progress_lbl.configure(text=f"[!] 盤後更新異常: {err}", fg="#ff7875")
        self.after(8000, self._reset_history_progress_ui)

    def _reset_history_progress_ui(self):
        if not self.is_history_updating:
            self.history_pbar.pack_forget()
            self.history_progress_lbl.configure(text="", fg="#52c41a")

    def on_view_kline(self):
        selected = self.tree.selection()
        if not selected:
            messagebox.showinfo("提示", "請先在表格中點選一檔股票！", parent=self)
            return
        pos_id = int(selected[0])
        pos = next((p for p in self.positions if p["id"] == pos_id), None)
        if pos:
            HistoryViewerDialog(self, symbol=pos["symbol"], name=pos["name"], market=pos.get("market", "TW"))


class PositionEditDialog(tk.Toplevel):
    """新增/修改持股部位對話框 (極簡輸入：自動判斷市場別、名稱、ETF與除權息資訊)"""
    def __init__(self, parent, title: str, pos: Optional[Dict[str, Any]] = None, on_saved=None):
        super().__init__(parent)
        self.title(title)
        self.geometry("450x410")
        self.resizable(False, False)
        self.transient(parent)
        self.grab_set()

        self.pos = pos
        self.on_saved = on_saved
        self.configure(bg="#22222a")
        self.detected_meta = {
            "symbol": pos["symbol"] if pos else "",
            "name": pos["name"] if pos else "",
            "market": pos["market"] if pos else "TW",
            "is_etf": pos["is_etf"] if pos else 0
        }

        self.build_form()

    def build_form(self):
        # 1. 股票代碼/標的 (合併為單一輸入框)
        lbl_sym = tk.Label(self, text="股票代碼 / 標的 (例 00878 / 2330 / AAPL):", bg="#22222a", fg="#ffffff", font=("Microsoft JhengHei UI", 9, "bold"))
        lbl_sym.pack(anchor="w", padx=18, pady=(12, 2))
        
        self.ent_symbol = tk.Entry(self, bg="#2d2d38", fg="#ffffff", insertbackground="#ffffff", font=("Microsoft JhengHei UI", 11), relief="flat")
        if self.pos:
            self.ent_symbol.insert(0, self.pos["symbol"])
        self.ent_symbol.pack(fill=tk.X, padx=18, ipady=3)
        self.ent_symbol.bind("<KeyRelease>", self.on_symbol_changed)
        self.ent_symbol.bind("<FocusOut>", self.on_symbol_focus_out)

        # 自動辨識狀態提示標籤
        self.lbl_detect = tk.Label(self, text="輸入代碼後將自動辨識名稱、市場別與 ETF...", bg="#22222a", fg="#a0e0a0", font=("Microsoft JhengHei UI", 8))
        self.lbl_detect.pack(anchor="w", padx=18, pady=(2, 6))

        # 2. 持有股數
        lbl_sh = tk.Label(self, text="持有股數 (例 1000 或 0.5 零股):", bg="#22222a", fg="#ffffff", font=("Microsoft JhengHei UI", 9))
        lbl_sh.pack(anchor="w", padx=18, pady=(4, 2))
        self.ent_shares = tk.Entry(self, bg="#2d2d38", fg="#ffffff", insertbackground="#ffffff", font=("Microsoft JhengHei UI", 10), relief="flat")
        self.ent_shares.insert(0, str(self.pos["shares"]) if self.pos else "1000")
        self.ent_shares.pack(fill=tk.X, padx=18, ipady=3)

        # 3. 買入成本均價
        lbl_pr = tk.Label(self, text="買入成本均價 (支援小數點，例 35.5 或 2400.25):", bg="#22222a", fg="#ffffff", font=("Microsoft JhengHei UI", 9))
        lbl_pr.pack(anchor="w", padx=18, pady=(4, 2))
        self.ent_price = tk.Entry(self, bg="#2d2d38", fg="#ffffff", insertbackground="#ffffff", font=("Microsoft JhengHei UI", 10), relief="flat")
        if self.pos:
            self.ent_price.insert(0, str(self.pos["cost_price"]))
        self.ent_price.pack(fill=tk.X, padx=18, ipady=3)

        # 4. 手續費折讓
        lbl_dc = tk.Label(self, text="券商手續費折扣 (預設 0.6 代表 6 折):", bg="#22222a", fg="#ffffff", font=("Microsoft JhengHei UI", 9))
        lbl_dc.pack(anchor="w", padx=18, pady=(4, 2))
        self.ent_discount = tk.Entry(self, bg="#2d2d38", fg="#ffffff", insertbackground="#ffffff", font=("Microsoft JhengHei UI", 10), relief="flat")
        self.ent_discount.insert(0, str(self.pos["fee_discount"]) if self.pos else "0.6")
        self.ent_discount.pack(fill=tk.X, padx=18, ipady=3)

        # 5. 備註 (選填)
        lbl_nt = tk.Label(self, text="備註說明 (選填):", bg="#22222a", fg="#ffffff", font=("Microsoft JhengHei UI", 9))
        lbl_nt.pack(anchor="w", padx=18, pady=(4, 2))
        self.ent_note = tk.Entry(self, bg="#2d2d38", fg="#ffffff", insertbackground="#ffffff", font=("Microsoft JhengHei UI", 10), relief="flat")
        if self.pos:
            self.ent_note.insert(0, self.pos.get("note", ""))
        self.ent_note.pack(fill=tk.X, padx=18, ipady=3)

        # 底部按鈕
        btn_box = tk.Frame(self, bg="#22222a")
        btn_box.pack(fill=tk.X, padx=18, pady=16)
        btn_save = tk.Button(btn_box, text="儲存送出 (自動同步股息)", bg="#3a86ff", fg="#ffffff", font=("Microsoft JhengHei UI", 10, "bold"), relief="flat", command=self.save)
        btn_save.pack(side=tk.RIGHT, ipadx=12, ipady=3)
        btn_cancel = tk.Button(btn_box, text="取消", bg="#3a3a46", fg="#ffffff", relief="flat", command=self.destroy)
        btn_cancel.pack(side=tk.RIGHT, padx=8, ipadx=10, ipady=3)

        if self.pos:
            self.trigger_detect(self.pos["symbol"])

    def on_symbol_changed(self, event):
        val = self.ent_symbol.get().strip().upper()
        if len(val) >= 4 or (val.isalpha() and len(val) >= 2):
            self.trigger_detect(val)

    def on_symbol_focus_out(self, event):
        val = self.ent_symbol.get().strip().upper()
        if val:
            self.trigger_detect(val)

    def trigger_detect(self, val: str):
        def worker():
            meta = detect_stock_metadata(val)
            self.detected_meta = meta
            self.after(0, lambda: self.lbl_detect.configure(text=f"✔ 已辨識：{meta['display_info']}", foreground="#52c41a"))
        threading.Thread(target=worker, daemon=True).start()

    def save(self):
        sym_input = self.ent_symbol.get().strip().upper()
        shares_str = self.ent_shares.get().strip()
        cost_str = self.ent_price.get().strip()
        disc_str = self.ent_discount.get().strip()
        note = self.ent_note.get().strip()

        if not sym_input:
            messagebox.showerror("錯誤", "請輸入股票代碼！", parent=self)
            return

        shares = clean_number(shares_str, default=-1)
        cost_price = clean_number(cost_str, default=-1)
        fee_discount = clean_number(disc_str, default=0.6)

        if shares <= 0 or cost_price < 0:
            messagebox.showerror("錯誤", "持股數必須大於 0，成本均價必須為有效小數！", parent=self)
            return

        # 自動判斷補齊
        meta = detect_stock_metadata(sym_input)
        symbol = meta["symbol"]
        name = meta["name"]
        market = meta["market"]
        is_etf = meta["is_etf"]

        if self.pos:
            update_position(self.pos["id"], symbol, name, market, shares, cost_price, fee_discount, is_etf, note)
        else:
            add_position(symbol, name, market, shares, cost_price, fee_discount, is_etf, note)

        if self.on_saved:
            self.on_saved()
        self.destroy()


class TradeLotManagerDialog(tk.Toplevel):
    """買入取得時間批次管理視窗 (支援各筆取得時間、股數、價格與歷年已領股息精算)"""
    def __init__(self, parent, symbol: str, name: str, on_lots_changed=None):
        super().__init__(parent)
        self.title(f"買入批次取得時間明細管理 - {symbol} {name}")
        self.geometry("780x520")
        self.minsize(650, 420)
        self.configure(bg="#22222a")
        self.transient(parent)
        self.grab_set()

        self.symbol = symbol
        self.name = name
        self.on_lots_changed = on_lots_changed

        self.build_ui()
        self.reload_lots()

    def build_ui(self):
        # 頂部標題
        top = tk.Frame(self, bg="#20202a", height=38)
        top.pack(fill=tk.X)
        tk.Label(top, text=f"標的: {self.symbol} {self.name} - 買入批次明細 (依取得時間計算歷史已領股息)", 
                 bg="#20202a", fg="#ffffff", font=("Microsoft JhengHei UI", 11, "bold")).pack(side=tk.LEFT, padx=12, pady=6)

        # 中間表格
        mid = tk.Frame(self, bg="#22222a")
        mid.pack(fill=tk.BOTH, expand=True, padx=12, pady=8)

        cols = [("id", "序號", 50), ("acquire_date", "取得時間", 110), ("shares", "買入股數", 100), 
                ("price", "買入單價", 100), ("fee", "手續費", 80), ("note", "備註說明", 160)]
        self.tree = ttk.Treeview(mid, columns=[c[0] for c in cols], show="headings")
        vsb = ttk.Scrollbar(mid, orient="vertical", command=self.tree.yview)
        self.tree.configure(yscrollcommand=vsb.set)

        for cid, cname, w in cols:
            self.tree.heading(cid, text=cname)
            self.tree.column(cid, width=w, anchor="center" if cid in ["id", "acquire_date"] else ("e" if cid in ["shares", "price", "fee"] else "w"))

        self.tree.pack(side=tk.LEFT, fill=tk.BOTH, expand=True)
        vsb.pack(side=tk.RIGHT, fill=tk.Y)

        # 新增批次輸入面板
        input_frame = tk.LabelFrame(self, text="新增買入批次", bg="#282835", fg="#d0d0e0", font=("Microsoft JhengHei UI", 9, "bold"), padx=10, pady=6)
        input_frame.pack(fill=tk.X, padx=12, pady=(0, 6))

        tk.Label(input_frame, text="取得日期 (YYYY-MM-DD):", bg="#282835", fg="#ffffff").grid(row=0, column=0, padx=4, pady=2)
        self.ent_date = tk.Entry(input_frame, width=12, font=("Microsoft JhengHei UI", 9))
        self.ent_date.insert(0, datetime.now().strftime("%Y-%m-%d"))
        self.ent_date.grid(row=0, column=1, padx=4, pady=2)

        tk.Label(input_frame, text="股數:", bg="#282835", fg="#ffffff").grid(row=0, column=2, padx=4, pady=2)
        self.ent_shares = tk.Entry(input_frame, width=10, font=("Microsoft JhengHei UI", 9))
        self.ent_shares.insert(0, "1000")
        self.ent_shares.grid(row=0, column=3, padx=4, pady=2)

        tk.Label(input_frame, text="單價:", bg="#282835", fg="#ffffff").grid(row=0, column=4, padx=4, pady=2)
        self.ent_price = tk.Entry(input_frame, width=10, font=("Microsoft JhengHei UI", 9))
        self.ent_price.grid(row=0, column=5, padx=4, pady=2)

        tk.Label(input_frame, text="備註:", bg="#282835", fg="#ffffff").grid(row=0, column=6, padx=4, pady=2)
        self.ent_note = tk.Entry(input_frame, width=12, font=("Microsoft JhengHei UI", 9))
        self.ent_note.grid(row=0, column=7, padx=4, pady=2)

        btn_add_lot = tk.Button(input_frame, text="[+] 新增此批次", bg="#3a86ff", fg="#ffffff", font=("Microsoft JhengHei UI", 9, "bold"), relief="flat", command=self.on_add_lot)
        btn_add_lot.grid(row=0, column=8, padx=8, pady=2)

        # 底部統計資訊與關閉
        bot_bar = tk.Frame(self, bg="#20202a", height=42)
        bot_bar.pack(fill=tk.X, side=tk.BOTTOM)

        self.summary_lbl = tk.Label(bot_bar, text="統計計算中...", bg="#20202a", fg="#ffd166", font=("Microsoft JhengHei UI", 10, "bold"))
        self.summary_lbl.pack(side=tk.LEFT, padx=12, pady=8)

        btn_del = tk.Button(bot_bar, text="[-] 刪除選取批次", bg="#ff4d4f", fg="#ffffff", relief="flat", font=("Microsoft JhengHei UI", 9), command=self.on_delete_selected)
        btn_del.pack(side=tk.RIGHT, padx=12, pady=8, ipadx=8)

        btn_close = tk.Button(bot_bar, text="關閉完成", bg="#3a3a46", fg="#ffffff", relief="flat", font=("Microsoft JhengHei UI", 9), command=self.destroy)
        btn_close.pack(side=tk.RIGHT, padx=4, pady=8, ipadx=8)

    def reload_lots(self):
        self.tree.delete(*self.tree.get_children())
        lots = get_trade_lots(self.symbol)
        total_shares = sum(l["shares"] for l in lots)
        total_val = sum(l["shares"] * l["price"] for l in lots)
        avg_price = (total_val / total_shares) if total_shares > 0 else 0.0

        for l in lots:
            self.tree.insert("", tk.END, iid=str(l["id"]), values=(
                l["id"],
                l["acquire_date"],
                f"{l['shares']:,.0f}" if l["shares"].is_integer() else f"{l['shares']:,.2f}",
                f"{l['price']:,.2f}",
                f"{l['fee']:,.0f}",
                l["note"]
            ))

        # 精算歷史已領股息
        hist_div_total, count = DividendService.calc_historical_received(self.symbol, lots)

        summary_text = (
            f"合計總股數: {total_shares:,.0f} 股  |  加權平均成本: ${avg_price:,.2f}  |  "
            f"歷年累計已領現金股利: ${hist_div_total:,.0f} (共{count}批次取得時間)"
        )
        self.summary_lbl.configure(text=summary_text)

    def on_add_lot(self):
        dt_str = self.ent_date.get().strip()
        sh_str = self.ent_shares.get().strip()
        pr_str = self.ent_price.get().strip()
        note = self.ent_note.get().strip()

        if not dt_str:
            messagebox.showerror("錯誤", "請輸入取得時間 (YYYY-MM-DD)！", parent=self)
            return

        shares = clean_number(sh_str, -1)
        price = clean_number(pr_str, -1)
        if shares <= 0 or price < 0:
            messagebox.showerror("錯誤", "買入股數與單價必須為有效大於 0 之數字！", parent=self)
            return

        add_trade_lot(self.symbol, dt_str, shares, price, 0.0, note)
        self.ent_price.delete(0, tk.END)
        self.reload_lots()
        if self.on_lots_changed:
            self.on_lots_changed()

    def on_delete_selected(self):
        sel = self.tree.selection()
        if not sel:
            messagebox.showinfo("提示", "請先點選欲刪除的批次！", parent=self)
            return
        lot_id = int(sel[0])
        delete_trade_lot(lot_id, self.symbol)
        self.reload_lots()
        if self.on_lots_changed:
            self.on_lots_changed()


class SettingsDialog(tk.Toplevel):
    """介面自訂設定對話框 (支援勾選主表格顯示欄位與頂部卡片)"""
    def __init__(self, parent, on_applied=None):
        super().__init__(parent)
        self.title("自訂介面顯示項目設定")
        self.geometry("680x560")
        self.resizable(False, False)
        self.configure(bg="#22222a")
        self.transient(parent)
        self.grab_set()

        self.on_applied = on_applied
        self.cur_cols = set(get_visible_columns())
        self.cur_cards = set(get_visible_cards())

        self.build_ui()

    def build_ui(self):
        nb = ttk.Notebook(self)
        nb.pack(fill=tk.BOTH, expand=True, padx=12, pady=10)

        # 分頁 1: 表格欄位設定
        tab_cols = tk.Frame(nb, bg="#22222a")
        nb.add(tab_cols, text="  主表格顯示欄位勾選  ")

        lbl_c = tk.Label(tab_cols, text="勾選您希望在持股行情表格中顯示的欄位項目 (可隨時調整)：", bg="#22222a", fg="#a0a0b0", font=("Microsoft JhengHei UI", 9))
        lbl_c.pack(anchor="w", padx=12, pady=8)

        cols_container = tk.Frame(tab_cols, bg="#22222a")
        cols_container.pack(fill=tk.BOTH, expand=True, padx=12, pady=4)

        self.col_vars = {}
        row = 0
        col = 0
        for cid, cname, w, align, category in ALL_COLUMN_SPECS:
            var = tk.BooleanVar(value=(cid in self.cur_cols))
            self.col_vars[cid] = var
            chk = tk.Checkbutton(cols_container, text=f"{cname} ({category})", variable=var, bg="#22222a", fg="#ffffff", selectcolor="#2d2d38", font=("Microsoft JhengHei UI", 9))
            chk.grid(row=row, column=col, sticky="w", padx=8, pady=4)
            col += 1
            if col >= 3:
                col = 0
                row += 1

        # 全選/重設按鈕
        btn_col_box = tk.Frame(tab_cols, bg="#22222a")
        btn_col_box.pack(fill=tk.X, padx=12, pady=6)
        tk.Button(btn_col_box, text="全部勾選", bg="#323242", fg="#ffffff", relief="flat", font=("Microsoft JhengHei UI", 8), command=self.select_all_cols).pack(side=tk.LEFT, padx=3)
        tk.Button(btn_col_box, text="恢復預設", bg="#323242", fg="#ffffff", relief="flat", font=("Microsoft JhengHei UI", 8), command=self.reset_default_cols).pack(side=tk.LEFT, padx=3)

        # 分頁 2: 頂部資訊卡片設定
        tab_cards = tk.Frame(nb, bg="#22222a")
        nb.add(tab_cards, text="  頂部儀表板卡片勾選  ")

        lbl_k = tk.Label(tab_cards, text="勾選您希望在視窗最上方儀表板顯示的概覽卡片：", bg="#22222a", fg="#a0a0b0", font=("Microsoft JhengHei UI", 9))
        lbl_k.pack(anchor="w", padx=12, pady=8)

        cards_container = tk.Frame(tab_cards, bg="#22222a")
        cards_container.pack(fill=tk.BOTH, expand=True, padx=12, pady=4)

        self.card_vars = {}
        for i, (cid, title, text_col, desc) in enumerate(ALL_CARD_SPECS):
            var = tk.BooleanVar(value=(cid in self.cur_cards))
            self.card_vars[cid] = var
            chk = tk.Checkbutton(cards_container, text=f"{title} - [{desc}]", variable=var, bg="#22222a", fg="#ffffff", selectcolor="#2d2d38", font=("Microsoft JhengHei UI", 9))
            chk.pack(anchor="w", padx=15, pady=4)

        # 底部儲存列
        bot = tk.Frame(self, bg="#22222a")
        bot.pack(fill=tk.X, padx=12, pady=(0, 12))
        tk.Button(bot, text="儲存並立即套用", bg="#3a86ff", fg="#ffffff", font=("Microsoft JhengHei UI", 10, "bold"), relief="flat", command=self.save_settings).pack(side=tk.RIGHT, ipadx=10, ipady=3)
        tk.Button(bot, text="取消", bg="#3a3a46", fg="#ffffff", relief="flat", font=("Microsoft JhengHei UI", 9), command=self.destroy).pack(side=tk.RIGHT, padx=8, ipadx=8, ipady=3)

    def select_all_cols(self):
        for v in self.col_vars.values():
            v.set(True)

    def reset_default_cols(self):
        def_set = set(DEFAULT_VISIBLE_COLUMNS)
        for cid, v in self.col_vars.items():
            v.set(cid in def_set)

    def save_settings(self):
        selected_cols = [cid for cid, v in self.col_vars.items() if v.get()]
        if not selected_cols:
            messagebox.showerror("錯誤", "至少必須勾選一個表格顯示欄位！", parent=self)
            return

        selected_cards = [cid for cid, v in self.card_vars.items() if v.get()]

        set_visible_columns(selected_cols)
        set_visible_cards(selected_cards)

        if self.on_applied:
            self.on_applied()
        self.destroy()


class ProgressDialog(tk.Toplevel):
    def __init__(self, parent, positions: List[Dict[str, Any]]):
        super().__init__(parent)
        self.title("盤後歷史資料庫增量下載")
        self.geometry("520x360")
        self.configure(bg="#22222a")
        self.transient(parent)
        self.grab_set()

        self.positions = positions

        tk.Label(self, text="正在更新歷史日 K 線資料庫 (純本地儲存)...", bg="#22222a", fg="#ffffff", font=("Microsoft JhengHei UI", 11, "bold")).pack(anchor="w", padx=15, pady=(15, 5))

        self.text_log = tk.Text(self, bg="#1a1a20", fg="#a0e0a0", font=("Consolas", 9), relief="flat")
        self.text_log.pack(fill=tk.BOTH, expand=True, padx=15, pady=8)

        self.btn_close = tk.Button(self, text="背景執行中...", state=tk.DISABLED, bg="#3a3a46", fg="#ffffff", relief="flat", command=self.destroy)
        self.btn_close.pack(side=tk.RIGHT, padx=15, pady=(0, 15), ipadx=10, ipady=3)

        threading.Thread(target=self.run_update, daemon=True).start()

    def log(self, msg: str):
        self.text_log.insert(tk.END, msg + "\n")
        self.text_log.see(tk.END)

    def run_update(self):
        HistoryService.update_all_positions_history(self.positions, on_progress=lambda m: self.after(0, lambda: self.log(m)))
        self.after(0, self.finish)

    def finish(self):
        self.log("\n======================================")
        self.log("[OK] 盤後歷史資料庫更新完成！")
        self.btn_close.configure(text="關閉視窗", state=tk.NORMAL, bg="#3a86ff")


class HistoryViewerDialog(tk.Toplevel):
    def __init__(self, parent, symbol: str, name: str, market: str):
        super().__init__(parent)
        self.title(f"{symbol} {name} - 專業歷史 K 線圖表與數據")
        self.geometry("1020x680")
        self.minsize(850, 550)
        self.configure(bg="#181820")
        self.transient(parent)

        self.symbol = symbol
        self.name = name
        self.market = market

        self.records = get_history_kline(symbol, limit=250)

        top_bar = tk.Frame(self, bg="#20202a", height=42)
        top_bar.pack(fill=tk.X, side=tk.TOP, padx=0, pady=0)

        title_txt = f"{symbol} {name} ({market}) - 歷史走勢圖"
        tk.Label(top_bar, text=title_txt, bg="#20202a", fg="#ffffff", font=("Microsoft JhengHei UI", 11, "bold")).pack(side=tk.LEFT, padx=12, pady=6)

        self.view_mode = tk.StringVar(value="chart")
        btn_view_table = tk.Button(top_bar, text="數據表格", bg="#323242", fg="#ffffff", relief="flat", font=("Microsoft JhengHei UI", 9), command=self.show_table_view)
        btn_view_table.pack(side=tk.RIGHT, padx=8, pady=6, ipadx=8)

        btn_view_chart = tk.Button(top_bar, text="K線圖表", bg="#3a86ff", fg="#ffffff", relief="flat", font=("Microsoft JhengHei UI", 9, "bold"), command=self.show_chart_view)
        btn_view_chart.pack(side=tk.RIGHT, padx=4, pady=6, ipadx=8)
        self.btn_chart = btn_view_chart
        self.btn_table = btn_view_table

        tk.Label(top_bar, text="| 範圍:", bg="#20202a", fg="#888899").pack(side=tk.RIGHT, padx=4)
        for days, label in [(250, "全部"), (120, "120日"), (60, "60日"), (30, "30日")]:
            btn_d = tk.Button(top_bar, text=label, bg="#282835", fg="#d0d0d0", relief="flat", font=("Microsoft JhengHei UI", 8), command=lambda d=days: self.change_days(d))
            btn_d.pack(side=tk.RIGHT, padx=2, pady=6, ipadx=4)

        self.main_container = tk.Frame(self, bg="#181820")
        self.main_container.pack(fill=tk.BOTH, expand=True)

        self.chart_widget = KLineChartCanvas(self.main_container, records=self.records, symbol=symbol, name=name)
        self.chart_widget.pack(fill=tk.BOTH, expand=True)
        self.table_widget = None

    def change_days(self, days: int):
        if self.chart_widget:
            self.chart_widget.set_display_days(days)

    def show_chart_view(self):
        if self.table_widget:
            self.table_widget.pack_forget()
        self.chart_widget.pack(fill=tk.BOTH, expand=True)
        self.btn_chart.configure(bg="#3a86ff", font=("Microsoft JhengHei UI", 9, "bold"))
        self.btn_table.configure(bg="#323242", font=("Microsoft JhengHei UI", 9))
        self.chart_widget.redraw()

    def show_table_view(self):
        self.chart_widget.pack_forget()
        if not self.table_widget:
            self.create_table_widget()
        self.table_widget.pack(fill=tk.BOTH, expand=True)
        self.btn_table.configure(bg="#3a86ff", font=("Microsoft JhengHei UI", 9, "bold"))
        self.btn_chart.configure(bg="#323242", font=("Microsoft JhengHei UI", 9))

    def create_table_widget(self):
        self.table_widget = tk.Frame(self.main_container, bg="#181820")
        cols = [("date", "日期", 95), ("open", "開盤價", 85), ("high", "最高價", 85), ("low", "最低價", 85), ("close", "收盤價", 85), ("volume", "成交量", 110)]
        tree = ttk.Treeview(self.table_widget, columns=[c[0] for c in cols], show="headings")
        vsb = ttk.Scrollbar(self.table_widget, orient="vertical", command=tree.yview)
        tree.configure(yscrollcommand=vsb.set)

        for col_id, col_name, w in cols:
            tree.heading(col_id, text=col_name)
            tree.column(col_id, width=w, anchor="e" if col_id != "date" else "center")

        tree.pack(side=tk.LEFT, fill=tk.BOTH, expand=True, padx=8, pady=8)
        vsb.pack(side=tk.RIGHT, fill=tk.Y, pady=8)

        if not self.records:
            tree.insert("", tk.END, values=("無資料", "請點擊", "主畫面", "【盤後更新】", "下載歷史", ""))
        else:
            for r in reversed(self.records):
                tree.insert("", tk.END, values=(
                    r["date"],
                    f"{r['open']:.2f}",
                    f"{r['high']:.2f}",
                    f"{r['low']:.2f}",
                    f"{r['close']:.2f}",
                    f"{r['volume']:,}"
                ))


class CloudSyncDialog(tk.Toplevel):
    """零知識端到端加密雲端同步對話框 (支援 OWASP 600K + 128-bit SecretKey 雙因子)"""
    def __init__(self, parent, on_restored=None):
        super().__init__(parent)
        self.title("零知識加密雲端同步 (OWASP 600K + 雙因子金鑰)")
        self.geometry("560x660")
        self.resizable(False, False)
        self.configure(bg="#202028")
        self.transient(parent)
        self.grab_set()

        self.on_restored = on_restored
        self.secret_key = get_or_create_secret_key()
        self.web_url = "https://tinyurl.com/25g5elkq"
        self.build_ui()

    def build_ui(self):
        # 標題
        head = tk.Frame(self, bg="#202028", padx=20, pady=12)
        head.pack(fill=tk.X)
        tk.Label(head, text="E2EE 零知識雲端加密同步 (高防護版)", bg="#202028", fg="#ffffff", font=("Microsoft JhengHei UI", 12, "bold")).pack(anchor="w")
        tk.Label(head, text="PBKDF2 600,000次 ‧ AES-256-GCM AAD 認證 ‧ 32KB 固定防護 ‧ 雙因子金鑰", bg="#202028", fg="#8e95a5", font=("Microsoft JhengHei UI", 8)).pack(anchor="w", pady=(2, 0))

        # 表單
        form = tk.Frame(self, bg="#202028", padx=20)
        form.pack(fill=tk.BOTH, expand=True)

        tk.Label(form, text="使用者專屬代號 (User ID):", bg="#202028", fg="#ffffff", font=("Microsoft JhengHei UI", 9)).pack(anchor="w", pady=(4, 2))
        self.ent_user = tk.Entry(form, bg="#2d2d38", fg="#ffffff", insertbackground="#ffffff", font=("Microsoft JhengHei UI", 10), relief="flat")
        saved_user = get_setting("cloud_user_id", "")
        self.ent_user.insert(0, saved_user)
        self.ent_user.pack(fill=tk.X, ipady=3)

        tk.Label(form, text="專屬加密主密碼 (Master Password):", bg="#202028", fg="#ffffff", font=("Microsoft JhengHei UI", 9)).pack(anchor="w", pady=(8, 2))
        self.ent_pass = tk.Entry(form, bg="#2d2d38", fg="#ffffff", insertbackground="#ffffff", font=("Microsoft JhengHei UI", 10), relief="flat", show="*")
        self.ent_pass.pack(fill=tk.X, ipady=3)

        # 雙因子 128-bit 設備金鑰 (Secret Key)
        sk_frame = tk.Frame(form, bg="#202028")
        sk_frame.pack(fill=tk.X, pady=(10, 2))
        tk.Label(sk_frame, text="雙因子設備金鑰 (Secret Key):", bg="#202028", fg="#ffd166", font=("Microsoft JhengHei UI", 9, "bold")).pack(side=tk.LEFT)
        
        btn_sk_help = tk.Button(sk_frame, text="❓ 這是什麼？如何使用？", bg="#202028", fg="#00f090", activebackground="#202028", activeforeground="#ffffff", font=("Microsoft JhengHei UI", 8, "underline"), relief="flat", bd=0, cursor="hand2", command=self.show_secret_key_help)
        btn_sk_help.pack(side=tk.LEFT, padx=8)

        btn_copy_sk = tk.Button(sk_frame, text="📋 複製金鑰", bg="#3a3a46", fg="#ffffff", font=("Microsoft JhengHei UI", 8), relief="flat", command=self.copy_secret_key)
        btn_copy_sk.pack(side=tk.RIGHT)

        self.ent_sk = tk.Entry(form, bg="#1a1a22", fg="#00f090", font=("Consolas", 10, "bold"), relief="flat")
        self.ent_sk.insert(0, self.secret_key)
        self.ent_sk.configure(state="readonly")
        self.ent_sk.pack(fill=tk.X, ipady=3)
        tk.Label(form, text="💡 手機/平板登入網頁時需填入此金鑰。就像 1Password，無此金鑰即使猜中密碼也無法解密", bg="#202028", fg="#8e95a5", font=("Microsoft JhengHei UI", 8)).pack(anchor="w", pady=(2, 0))

        # Firebase RTDB 設定區塊
        fb_frame = tk.Frame(form, bg="#202028")
        fb_frame.pack(fill=tk.X, pady=(10, 2))
        tk.Label(fb_frame, text="Firebase RTDB 網址 (可留空使用預設):", bg="#202028", fg="#8e95a5", font=("Microsoft JhengHei UI", 8)).pack(side=tk.LEFT)
        btn_fb_help = tk.Button(fb_frame, text="📖 自行申請與設定教學", bg="#202028", fg="#3a86ff", activebackground="#202028", activeforeground="#ffffff", font=("Microsoft JhengHei UI", 8, "underline"), relief="flat", bd=0, cursor="hand2", command=self.show_firebase_help)
        btn_fb_help.pack(side=tk.RIGHT)

        self.ent_url = tk.Entry(form, bg="#2d2d38", fg="#a0a0b0", insertbackground="#ffffff", font=("Microsoft JhengHei UI", 9), relief="flat")
        saved_url = get_setting("cloud_firebase_url", DEFAULT_FIREBASE_URL)
        self.ent_url.insert(0, saved_url)
        self.ent_url.pack(fill=tk.X, ipady=3)

        # 手機端網頁指引卡片
        web_card = tk.Frame(form, bg="#181822", padx=10, pady=8, highlightthickness=1, highlightbackground="#2d2d3d")
        web_card.pack(fill=tk.X, pady=(12, 0))
        tk.Label(web_card, text="📱 跨平台手機/平板看盤網頁短網址：", bg="#181822", fg="#8e95a5", font=("Microsoft JhengHei UI", 8)).pack(anchor="w")
        
        web_sub = tk.Frame(web_card, bg="#181822")
        web_sub.pack(fill=tk.X, pady=(2, 0))
        tk.Label(web_sub, text=self.web_url, bg="#181822", fg="#00f090", font=("Consolas", 9, "bold")).pack(side=tk.LEFT)
        btn_copy_url = tk.Button(web_sub, text="📋 複製短網址", bg="#2d2d38", fg="#ffffff", font=("Microsoft JhengHei UI", 8), relief="flat", command=self.copy_web_url)
        btn_copy_url.pack(side=tk.RIGHT)

        self.lbl_status = tk.Label(form, text="", bg="#202028", fg="#52c41a", font=("Microsoft JhengHei UI", 9), wraplength=510, justify="left")
        self.lbl_status.pack(anchor="w", pady=(8, 0))

        # 按鈕區
        btns = tk.Frame(self, bg="#202028", padx=20, pady=12)
        btns.pack(fill=tk.X)

        self.btn_sync = tk.Button(btns, text="[🚀 一鍵加密並同步到雲端]", bg="#3a86ff", fg="#ffffff", font=("Microsoft JhengHei UI", 10, "bold"), relief="flat", command=self.do_upload)
        self.btn_sync.pack(side=tk.LEFT, fill=tk.X, expand=True, ipady=6, padx=(0, 8))

        btn_cancel = tk.Button(btns, text="關閉", bg="#3a3a46", fg="#ffffff", font=("Microsoft JhengHei UI", 9), relief="flat", command=self.destroy)
        btn_cancel.pack(side=tk.RIGHT, ipadx=12, ipady=6)

    def copy_secret_key(self):
        self.clipboard_clear()
        self.clipboard_append(self.secret_key)
        messagebox.showinfo("已複製", "雙因子金鑰 (Secret Key) 已複製到剪貼簿！\n\n可傳給自己的手機 LINE 或備忘錄，在手機網頁登入時填入一次即可。", parent=self)

    def copy_web_url(self):
        self.clipboard_clear()
        self.clipboard_append(self.web_url)
        messagebox.showinfo("已複製", f"看盤網頁短網址已複製到剪貼簿：\n{self.web_url}\n\n傳到手機即可在瀏覽器開啟並加入主畫面！", parent=self)

    def show_secret_key_help(self):
        """展示雙因子設備金鑰詳細原理與具體操作指引"""
        help_win = tk.Toplevel(self)
        help_win.title("❓ 什麼是雙因子設備金鑰 (Secret Key)？")
        help_win.geometry("540x480")
        help_win.resizable(False, False)
        help_win.configure(bg="#1e1e26")
        help_win.transient(self)
        help_win.grab_set()

        content = tk.Frame(help_win, bg="#1e1e26", padx=20, pady=16)
        content.pack(fill=tk.BOTH, expand=True)

        tk.Label(content, text="🔐 雙因子設備金鑰 (Secret Key) 使用指引", bg="#1e1e26", fg="#ffd166", font=("Microsoft JhengHei UI", 12, "bold")).pack(anchor="w")

        info_text = (
            "【為什麼需要這串金鑰？】\n"
            "一般使用者設定的密碼如果較短或包含常見單字，駭客拿到加密資料後能用電腦字典高速暴力猜測。\n"
            "本系統借鏡國際密碼庫 1Password 的最高安全架構：在您的電腦本地隨機產生 128 位元亂數「設備金鑰」。\n"
            "它與您的主密碼結合在一起加密，即使駭客猜中您的密碼，沒有這串金鑰也絕對解不開資料！\n\n"
            "【為什麼要點擊『複製金鑰』？具體該怎麼做？】\n"
            "1. 點擊「複製金鑰」，透過 LINE、通訊軟體傳給自己，或存入手機備忘錄。\n"
            "2. 在本機電腦點擊「一鍵加密並同步到雲端」。\n"
            "3. 用手機瀏覽器打開看盤網頁 (https://tinyurl.com/25g5elkq)。\n"
            "4. 在網頁登入時填寫：\n"
            "   • 使用者代號 (User ID)\n"
            "   • 專屬加密主密碼 (Master Password)\n"
            "   • 設備雙因子金鑰 (Secret Key 貼在此處)\n"
            "5. 手機瀏覽器會自動記住這組金鑰，日後打開網頁就不用重複輸入了！"
        )
        msg_lbl = tk.Label(content, text=info_text, bg="#1e1e26", fg="#d0d0dc", font=("Microsoft JhengHei UI", 9), justify="left", wraplength=490, lineheight=1.4 if hasattr(tk.Label, 'lineheight') else 1)
        msg_lbl.pack(anchor="w", pady=(10, 16))

        btn_box = tk.Frame(content, bg="#1e1e26")
        btn_box.pack(fill=tk.X, side=tk.BOTTOM)
        tk.Button(btn_box, text="我了解了", bg="#3a86ff", fg="#ffffff", font=("Microsoft JhengHei UI", 9, "bold"), relief="flat", command=help_win.destroy).pack(fill=tk.X, ipady=5)

    def show_firebase_help(self):
        """展示自行申請與設定 Firebase RTDB 的詳細指引"""
        help_win = tk.Toplevel(self)
        help_win.title("📖 Firebase Realtime Database 申請與安全設定教學")
        help_win.geometry("560x520")
        help_win.resizable(False, False)
        help_win.configure(bg="#1e1e26")
        help_win.transient(self)
        help_win.grab_set()

        content = tk.Frame(help_win, bg="#1e1e26", padx=20, pady=16)
        content.pack(fill=tk.BOTH, expand=True)

        tk.Label(content, text="☁️ Firebase 免費資料庫申請步驟 (個人終身免費)", bg="#1e1e26", fg="#3a86ff", font=("Microsoft JhengHei UI", 12, "bold")).pack(anchor="w")

        guide_text = (
            "Firebase 是 Google 提供的雲端服務，Spark 免費方案提供 1GB 空間與 10GB/月 流量。\n"
            "儲存加密股票庫存終身完全免費，請依以下步驟建立：\n\n"
            "【步驟 1：建立專案】\n"
            "1. 前往 https://console.firebase.google.com/ 登入 Google 帳號。\n"
            "2. 點擊「新增專案」，輸入自訂名稱，關閉 Google Analytics 後建立。\n\n"
            "【步驟 2：啟用 Realtime Database】\n"
            "1. 左側選單點擊「建構 (Build)」➔「Realtime Database」➔「建立資料庫」。\n"
            "2. 地區建議選「asia-southeast1 (新加坡)」，模式先選「鎖定模式」。\n\n"
            "【步驟 3：套用零知識安全規則 (防列舉與防覆寫)】\n"
            "1. 切換至上方「規則 (Rules)」分頁。\n"
            "2. 點擊下方按鈕複製安全規則 JSON，貼上並覆蓋原有內容，點擊「發布 (Publish)」。\n\n"
            "【步驟 4：取得網址】\n"
            "切換回「資料 (Data)」分頁，複製頂部的 https://... 網址，貼回本視窗之欄位。"
        )
        msg_lbl = tk.Label(content, text=guide_text, bg="#1e1e26", fg="#d0d0dc", font=("Microsoft JhengHei UI", 9), justify="left", wraplength=510)
        msg_lbl.pack(anchor="w", pady=(8, 12))

        btn_box = tk.Frame(content, bg="#1e1e26")
        btn_box.pack(fill=tk.X, side=tk.BOTTOM)

        def copy_rules():
            rules_json = (
                "{\n"
                "  \"rules\": {\n"
                "    \".read\": false,\n"
                "    \".write\": false,\n"
                "    \"portfolios\": {\n"
                "      \".read\": false,\n"
                "      \".write\": false,\n"
                "      \"$user_id\": {\n"
                "        \".read\": true,\n"
                "        \".write\": \"!data.exists() || (newData.child('write_token').val() === data.child('write_token').val())\",\n"
                "        \".validate\": \"newData.hasChildren(['ciphertext_b64', 'salt_hex', 'nonce_hex', 'write_token', 'sync_seq'])\"\n"
                "      }\n"
                "    }\n"
                "  }\n"
                "}"
            )
            self.clipboard_clear()
            self.clipboard_append(rules_json)
            messagebox.showinfo("已複製", "Firebase 安全規則 (JSON) 已成功複製到剪貼簿！\n請直接至 Firebase 控制台的 Rules 分頁貼上並發布即可。", parent=help_win)

        def open_guide_doc():
            doc_path = os.path.join(os.path.dirname(__file__), "docs", "FIREBASE_SETUP_GUIDE.md")
            if os.path.exists(doc_path):
                os.startfile(doc_path)
            else:
                messagebox.showinfo("檔案位置", f"完整手冊位於：{doc_path}", parent=help_win)

        tk.Button(btn_box, text="📋 一鍵複製 Firebase 安全規則 (JSON)", bg="#00f090", fg="#101014", font=("Microsoft JhengHei UI", 9, "bold"), relief="flat", command=copy_rules).pack(fill=tk.X, ipady=4, pady=(0, 4))
        
        sub_btns = tk.Frame(btn_box, bg="#1e1e26")
        sub_btns.pack(fill=tk.X)
        tk.Button(sub_btns, text="📄 打開詳細說明文件 (FIREBASE_SETUP_GUIDE.md)", bg="#2d2d38", fg="#ffffff", font=("Microsoft JhengHei UI", 8), relief="flat", command=open_guide_doc).pack(side=tk.LEFT, fill=tk.X, expand=True, ipady=3, padx=(0, 4))
        tk.Button(sub_btns, text="關閉", bg="#3a3a46", fg="#ffffff", font=("Microsoft JhengHei UI", 8), relief="flat", command=help_win.destroy).pack(side=tk.RIGHT, ipadx=10, ipady=3)

    def do_upload(self):
        user = self.ent_user.get().strip()
        pwd = self.ent_pass.get()
        url = self.ent_url.get().strip()

        if not user or not pwd:
            messagebox.showerror("錯誤", "請輸入使用者代號與專屬密碼！", parent=self)
            return

        self.btn_sync.configure(state=tk.DISABLED, text="正在本機加密與上傳...")
        self.lbl_status.configure(text="正在執行 PBKDF2 (600,000次) + AAD + 32KB Padding...", fg="#52c41a")

        def worker():
            ok, msg = sync_to_cloud(user, pwd, url)
            self.after(0, lambda: self._on_upload_done(ok, msg))

        threading.Thread(target=worker, daemon=True).start()

    def _on_upload_done(self, ok: bool, msg: str):
        self.btn_sync.configure(state=tk.NORMAL, text="[🚀 一鍵加密並同步到雲端]")
        if ok:
            self.lbl_status.configure(text=f"✔ {msg}", fg="#52c41a")
            messagebox.showinfo("成功", f"{msg}\n\n您現在可使用手機或平板開啟雲端網頁：\n{self.web_url}\n\n輸入使用者代號、主密碼與 Secret Key 即可安全解密！", parent=self)
        else:
            self.lbl_status.configure(text=f"[!] {msg}", fg="#ff4d4f")
            messagebox.showerror("失敗", msg, parent=self)



if __name__ == "__main__":
    app = PortfolioApp()
    app.mainloop()
