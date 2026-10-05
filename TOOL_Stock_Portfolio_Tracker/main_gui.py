import tkinter as tk
from tkinter import ttk, messagebox
import threading
import time
from datetime import datetime
from typing import Dict, List, Any, Optional

from database import (
    init_db, get_all_positions, add_position, update_position, 
    delete_position, get_history_kline, get_setting, set_setting
)
from quote_service import QuoteService
from history_service import HistoryService
from dividend_service import DividendService
from pnl_calculator import calculate_position_pnl
from kline_chart import KLineChartCanvas

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

class PortfolioApp(tk.Tk):
    def __init__(self):
        super().__init__()
        self.title("本地端台美股庫存即時損益、歷史與股息追蹤系統 (Stock Portfolio Tracker)")
        self.geometry("1380x780")
        self.minsize(1100, 620)

        # 初始化資料庫
        init_db()

        self.quote_service = QuoteService()
        self.positions: List[Dict[str, Any]] = []
        self.latest_quotes: Dict[str, Dict[str, Any]] = {}
        self.dividend_cache: Dict[str, Dict[str, Any]] = {}

        self.auto_refresh_active = True
        self.refresh_interval = int(get_setting("refresh_interval", "10"))
        self.color_mode = get_setting("color_mode", "tw") # 'tw': 紅漲綠跌, 'us': 綠漲紅跌

        self._is_fetching = False
        self._sort_reverse = False
        self._sort_col = "unrealized_pnl"

        # 設定風格與配色
        self.setup_styles()
        self.build_ui()

        # 初始載入庫存與股息並啟動背景輪詢
        self.reload_positions()
        self.trigger_refresh()
        self.trigger_dividend_update(silent=True)
        self.start_auto_refresh_timer()

    def setup_styles(self):
        """配置深色質感風格與表格樣式"""
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

        # Treeview 表格樣式
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

        # 按鈕樣式
        self.style.configure("Action.TButton", font=("Microsoft JhengHei UI", 10, "bold"), padding=6)

    def build_ui(self):
        """建構主介面排版"""
        # 1. 頂部儀表板卡片 (總資產、總損益、總報酬率、今日損益、預估總年股息)
        top_frame = ttk.Frame(self, padding=(12, 10, 12, 5))
        top_frame.pack(fill=tk.X)

        self.cards = {}
        card_defs = [
            ("total_cost", "總投入成本 (NT$)", "$ 0", "#ffffff"),
            ("market_val", "庫存總市值 (NT$)", "$ 0", "#ffffff"),
            ("total_pnl", "總未實現損益 (NT$)", "$ 0 (0.00%)", "#ffffff"),
            ("day_pnl", "今日預估損益 (NT$)", "$ 0", "#ffffff"),
            ("total_div", "預估年總股息 (殖利率)", "$ 0 (0.00%)", "#ffd166"),
            ("stock_count", "在庫標的數", "0 檔", "#3a86ff")
        ]

        for i, (key, title, default_val, text_col) in enumerate(card_defs):
            card = ttk.Frame(top_frame, style="Card.TFrame", padding=(12, 8))
            card.pack(side=tk.LEFT, fill=tk.BOTH, expand=True, padx=3)
            lbl_title = ttk.Label(card, text=title, style="CardTitle.TLabel")
            lbl_title.pack(anchor="w")
            lbl_val = ttk.Label(card, text=default_val, style="CardVal.TLabel", foreground=text_col)
            lbl_val.pack(anchor="w", pady=(3, 0))
            self.cards[key] = lbl_val

        # 2. 中間庫存清單表格 (Treeview)
        mid_frame = ttk.Frame(self, padding=(12, 5, 12, 5))
        mid_frame.pack(fill=tk.BOTH, expand=True)

        columns = [
            ("symbol", "代碼", 75, "center"),
            ("name", "名稱", 105, "center"),
            ("market", "市場", 55, "center"),
            ("shares", "持股數", 75, "e"),
            ("cost_price", "成本均價", 85, "e"),
            ("current_price", "現價", 85, "e"),
            ("change_pct", "今日漲跌", 90, "e"),
            ("total_cost", "總成本", 95, "e"),
            ("market_val", "預估市值", 95, "e"),
            ("unrealized_pnl", "未實現損益", 105, "e"),
            ("roi_pct", "報酬率%", 80, "e"),
            ("cash_dividend", "每股股息", 75, "e"),
            ("total_dividend", "預估總股息", 85, "e"),
            ("yield_on_cost", "成本殖利率%", 90, "e"),
            ("div_status", "股息狀態/除息日", 125, "center"),
            ("note", "備註", 100, "w")
        ]

        self.tree = ttk.Treeview(mid_frame, columns=[c[0] for c in columns], show="headings", selectmode="browse")
        vsb = ttk.Scrollbar(mid_frame, orient="vertical", command=self.tree.yview)
        hsb = ttk.Scrollbar(mid_frame, orient="horizontal", command=self.tree.xview)
        self.tree.configure(yscrollcommand=vsb.set, xscrollcommand=hsb.set)

        for col_id, col_name, width, align in columns:
            self.tree.heading(col_id, text=col_name, command=lambda c=col_id: self.sort_tree(c))
            self.tree.column(col_id, width=width, anchor=align)

        self.tree.grid(row=0, column=0, sticky="nsew")
        vsb.grid(row=0, column=1, sticky="ns")
        hsb.grid(row=1, column=0, sticky="ew")

        mid_frame.grid_rowconfigure(0, weight=1)
        mid_frame.grid_columnconfigure(0, weight=1)

        # 雙擊列開啟編輯/明細
        self.tree.bind("<Double-1>", lambda e: self.on_edit_position())

        # 定義顏色 Tag (紅漲綠跌或綠漲紅跌)
        self.update_color_tags()

        # 3. 底部功能按鈕與工具列
        bottom_frame = ttk.Frame(self, padding=(12, 8, 12, 12))
        bottom_frame.pack(fill=tk.X)

        btn_add = ttk.Button(bottom_frame, text="[+] 新增持股", command=self.on_add_position, style="Action.TButton")
        btn_add.pack(side=tk.LEFT, padx=3)

        btn_edit = ttk.Button(bottom_frame, text="[筆] 修改持股", command=self.on_edit_position, style="Action.TButton")
        btn_edit.pack(side=tk.LEFT, padx=3)

        btn_del = ttk.Button(bottom_frame, text="[-] 刪除持股", command=self.on_delete_position, style="Action.TButton")
        btn_del.pack(side=tk.LEFT, padx=3)

        ttk.Separator(bottom_frame, orient="vertical").pack(side=tk.LEFT, fill=tk.Y, padx=6)

        btn_refresh = ttk.Button(bottom_frame, text="[更新] 立即刷新行情", command=self.trigger_refresh, style="Action.TButton")
        btn_refresh.pack(side=tk.LEFT, padx=3)

        btn_div = ttk.Button(bottom_frame, text="[股息] 更新除權息公告", command=lambda: self.trigger_dividend_update(silent=False), style="Action.TButton")
        btn_div.pack(side=tk.LEFT, padx=3)

        btn_post_market = ttk.Button(bottom_frame, text="[盤後] 更新歷史資料庫", command=self.on_update_history_all, style="Action.TButton")
        btn_post_market.pack(side=tk.LEFT, padx=3)

        btn_view_kline = ttk.Button(bottom_frame, text="[歷史] 查看個股歷史K線", command=self.on_view_kline, style="Action.TButton")
        btn_view_kline.pack(side=tk.LEFT, padx=3)

        # 右側控制：自動刷新頻率與狀態
        self.status_lbl = ttk.Label(bottom_frame, text="系統就緒", foreground=self.muted_text)
        self.status_lbl.pack(side=tk.RIGHT, padx=6)

        self.auto_refresh_var = tk.BooleanVar(value=True)
        chk_auto = ttk.Checkbutton(bottom_frame, text="自動刷新", variable=self.auto_refresh_var, command=self.on_toggle_auto_refresh)
        chk_auto.pack(side=tk.RIGHT, padx=4)

        self.interval_var = tk.StringVar(value=str(self.refresh_interval))
        combo_interval = ttk.Combobox(bottom_frame, textvariable=self.interval_var, values=["5", "10", "15", "30", "60"], width=3, state="readonly")
        combo_interval.pack(side=tk.RIGHT, padx=2)
        combo_interval.bind("<<ComboboxSelected>>", self.on_interval_changed)
        ttk.Label(bottom_frame, text="秒數:").pack(side=tk.RIGHT)

    def update_color_tags(self):
        """根據市場設定紅綠標籤樣式"""
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
        """從資料庫重新載入持股"""
        self.positions = get_all_positions()
        self.cards["stock_count"].configure(text=f"{len(self.positions)} 檔")

    def trigger_dividend_update(self, silent: bool = False):
        """背景抓取持股股息資料 (優先公布日期，否則留存去年)"""
        if not self.positions:
            return
        if not silent:
            self.status_lbl.configure(text="正在查詢證交所除權息公告...")

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
        """重新計算並填入表格與頂部卡片"""
        selected_iid = self.tree.focus()
        self.tree.delete(*self.tree.get_children())

        total_cost_sum = 0.0
        market_val_sum = 0.0
        total_pnl_sum = 0.0
        day_pnl_sum = 0.0
        total_div_sum = 0.0

        pnl_records = []

        for pos in self.positions:
            sym = pos["symbol"].upper()
            quote = self.latest_quotes.get(sym, {
                "current_price": pos["cost_price"],
                "yesterday_close": pos["cost_price"],
                "change": 0.0,
                "change_pct": 0.0
            })
            
            # 取得股息資料 (已公布或留存去年)
            div_info = self.dividend_cache.get(sym) or DividendService.get_db_dividend(sym) or {}

            pnl = calculate_position_pnl(pos, quote, div_info)
            pnl["id"] = pos["id"]
            pnl["market"] = pos.get("market", "TW")
            pnl["note"] = pos.get("note", "")
            pnl_records.append(pnl)

            total_cost_sum += pnl["total_cost"]
            market_val_sum += pnl["market_val"]
            total_pnl_sum += pnl["unrealized_pnl"]
            day_pnl_sum += pnl["day_pnl"]
            total_div_sum += pnl["total_dividend"]

        # 排序
        pnl_records.sort(key=lambda x: x.get(self._sort_col, 0), reverse=self._sort_reverse)

        for pnl in pnl_records:
            # 判斷標籤顏色 (漲/跌)
            change = pnl["change"]
            tag = "up" if change > 0 else ("down" if change < 0 else "flat")
            
            pnl_val = pnl["unrealized_pnl"]
            pnl_sign = "+" if pnl_val > 0 else ""
            chg_sign = "+" if change > 0 else ""

            # 股息狀態字串呈現
            if pnl["is_announced"] == 1:
                div_display = f"{pnl['ex_date']} [已公布]"
            elif pnl["cash_dividend"] > 0:
                div_display = f"{pnl['div_status']}"
            else:
                div_display = "尚未公布"

            # 格式化持股數 (整數或小數)
            shares_val = pnl["shares"]
            shares_str = f"{shares_val:,.0f}" if shares_val.is_integer() else f"{shares_val:,.2f}"

            row_values = (
                pnl["symbol"],
                pnl["name"],
                pnl["market"],
                shares_str,
                f"{pnl['cost_price']:,.2f}",
                f"{pnl['current_price']:,.2f}",
                f"{chg_sign}{pnl['change']:,.2f} ({chg_sign}{pnl['change_pct']:.2f}%)",
                f"{pnl['total_cost']:,}",
                f"{pnl['market_val']:,}",
                f"{pnl_sign}{pnl['unrealized_pnl']:,}",
                f"{pnl_sign}{pnl['roi_pct']:.2f}%",
                f"{pnl['cash_dividend']:.2f}",
                f"{pnl['total_dividend']:,}",
                f"{pnl['yield_on_cost']:.2f}%",
                div_display,
                pnl["note"]
            )
            item_id = str(pnl["id"])
            self.tree.insert("", tk.END, iid=item_id, values=row_values, tags=(tag,))

        if selected_iid and self.tree.exists(selected_iid):
            self.tree.selection_set(selected_iid)
            self.tree.focus(selected_iid)

        # 更新頂部卡片
        self.cards["total_cost"].configure(text=f"$ {total_cost_sum:,.0f}")
        self.cards["market_val"].configure(text=f"$ {market_val_sum:,.0f}")
        
        roi_total = (total_pnl_sum / total_cost_sum * 100) if total_cost_sum > 0 else 0.0
        pnl_color = self.up_color if total_pnl_sum > 0 else (self.down_color if total_pnl_sum < 0 else "#ffffff")
        pnl_sign = "+" if total_pnl_sum > 0 else ""
        self.cards["total_pnl"].configure(text=f"$ {pnl_sign}{total_pnl_sum:,.0f} ({pnl_sign}{roi_total:.2f}%)", foreground=pnl_color)

        day_color = self.up_color if day_pnl_sum > 0 else (self.down_color if day_pnl_sum < 0 else "#ffffff")
        day_sign = "+" if day_pnl_sum > 0 else ""
        self.cards["day_pnl"].configure(text=f"$ {day_sign}{day_pnl_sum:,.0f}", foreground=day_color)

        # 更新股息卡片
        avg_yield = (total_div_sum / total_cost_sum * 100) if total_cost_sum > 0 else 0.0
        self.cards["total_div"].configure(text=f"$ {total_div_sum:,.0f} ({avg_yield:.2f}%)")

    def trigger_refresh(self):
        """觸發背景非同步刷新即時行情"""
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
        """自動刷新循環計時器"""
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
        """點擊標題排序"""
        if self._sort_col == col:
            self._sort_reverse = not self._sort_reverse
        else:
            self._sort_col = col
            self._sort_reverse = True
        self.refresh_ui_table()

    # --- 持股管理對話框 ---

    def on_add_position(self):
        PositionEditDialog(self, title="新增持股庫存", on_saved=self._on_position_saved)

    def on_edit_position(self):
        selected = self.tree.selection()
        if not selected:
            messagebox.showinfo("提示", "請先點選欲修改的持股！", parent=self)
            return
        pos_id = int(selected[0])
        pos = next((p for p in self.positions if p["id"] == pos_id), None)
        if pos:
            PositionEditDialog(self, title="修改持股庫存", pos=pos, on_saved=self._on_position_saved)

    def on_delete_position(self):
        selected = self.tree.selection()
        if not selected:
            messagebox.showinfo("提示", "請先點選欲刪除的持股！", parent=self)
            return
        pos_id = int(selected[0])
        pos = next((p for p in self.positions if p["id"] == pos_id), None)
        if not pos:
            return
        confirm = messagebox.askyesno("確認刪除", f"確定要從本地庫存刪除 {pos['symbol']} ({pos['name']}) 嗎？", parent=self)
        if confirm:
            delete_position(pos_id)
            self.reload_positions()
            self.refresh_ui_table()
            messagebox.showinfo("成功", "已成功刪除持股！", parent=self)

    def _on_position_saved(self):
        self.reload_positions()
        self.trigger_refresh()
        self.trigger_dividend_update(silent=True)

    # --- 盤後更新與歷史查詢 ---

    def on_update_history_all(self):
        if not self.positions:
            messagebox.showinfo("提示", "目前沒有持股可更新！", parent=self)
            return
        ProgressDialog(self, positions=self.positions)

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
    """新增/編輯持股對話框 (支援小數點成本均價與股息自訂)"""
    def __init__(self, parent, title: str, pos: Optional[Dict[str, Any]] = None, on_saved=None):
        super().__init__(parent)
        self.title(title)
        self.geometry("450x550")
        self.resizable(False, False)
        self.transient(parent)
        self.grab_set()

        self.pos = pos
        self.on_saved = on_saved
        self.configure(bg="#22222a")

        self.build_form()

    def build_form(self):
        # 讀取現有股息紀錄
        existing_div = {}
        if self.pos:
            existing_div = DividendService.get_db_dividend(self.pos["symbol"]) or {}

        fields = [
            ("股票代碼 (例 2330 / 0050 / AAPL):", "symbol", self.pos["symbol"] if self.pos else ""),
            ("股票名稱 (選填，可自動抓取):", "name", self.pos["name"] if self.pos else ""),
            ("持有股數 (例 1000 或 0.5 零股):", "shares", str(self.pos["shares"]) if self.pos else "1000"),
            ("買入成本均價 (支援小數點，例 580.5 或 2400.25):", "cost_price", str(self.pos["cost_price"]) if self.pos else ""),
            ("券商手續費折扣 (例 0.6 代表 6 折):", "fee_discount", str(self.pos["fee_discount"]) if self.pos else "0.6"),
            ("每股現金股利 (選填，留空自動抓取或留存去年):", "cash_div", str(existing_div.get("cash_dividend", "")) if existing_div.get("cash_dividend") else ""),
            ("除息日期 YYYY-MM-DD (選填，有公布則填入):", "ex_date", str(existing_div.get("ex_date", "")) if existing_div.get("ex_date") else ""),
            ("備註 (選填):", "note", self.pos["note"] if self.pos else "")
        ]

        self.entries = {}
        for label_text, key, def_val in fields:
            lbl = tk.Label(self, text=label_text, bg="#22222a", fg="#ffffff", font=("Microsoft JhengHei UI", 9))
            lbl.pack(anchor="w", padx=15, pady=(4, 1))
            ent = tk.Entry(self, bg="#2d2d38", fg="#ffffff", insertbackground="#ffffff", font=("Microsoft JhengHei UI", 10), relief="flat")
            ent.insert(0, def_val)
            ent.pack(fill=tk.X, padx=15, ipady=2)
            self.entries[key] = ent

        # 市場選擇
        mkt_frame = tk.Frame(self, bg="#22222a")
        mkt_frame.pack(fill=tk.X, padx=15, pady=6)
        tk.Label(mkt_frame, text="市場別:", bg="#22222a", fg="#ffffff").pack(side=tk.LEFT)
        self.market_var = tk.StringVar(value=self.pos["market"] if self.pos else "TW")
        r1 = tk.Radiobutton(mkt_frame, text="台股上市", variable=self.market_var, value="TW", bg="#22222a", fg="#ffffff", selectcolor="#2d2d38")
        r2 = tk.Radiobutton(mkt_frame, text="台股上櫃", variable=self.market_var, value="TWO", bg="#22222a", fg="#ffffff", selectcolor="#2d2d38")
        r3 = tk.Radiobutton(mkt_frame, text="美股", variable=self.market_var, value="US", bg="#22222a", fg="#ffffff", selectcolor="#2d2d38")
        r1.pack(side=tk.LEFT, padx=3)
        r2.pack(side=tk.LEFT, padx=3)
        r3.pack(side=tk.LEFT, padx=3)

        # 是否為 ETF (影響證交稅 0.1% vs 0.3%)
        self.etf_var = tk.IntVar(value=self.pos["is_etf"] if self.pos else 0)
        chk_etf = tk.Checkbutton(self, text="此標的為 ETF (享有 0.1% 優惠證券交易稅)", variable=self.etf_var, bg="#22222a", fg="#ffffff", selectcolor="#2d2d38")
        chk_etf.pack(anchor="w", padx=15, pady=2)

        # 儲存按鈕
        btn_box = tk.Frame(self, bg="#22222a")
        btn_box.pack(fill=tk.X, padx=15, pady=12)
        btn_save = tk.Button(btn_box, text="儲存送出", bg="#3a86ff", fg="#ffffff", font=("Microsoft JhengHei UI", 10, "bold"), relief="flat", command=self.save)
        btn_save.pack(side=tk.RIGHT, ipadx=10, ipady=3)
        btn_cancel = tk.Button(btn_box, text="取消", bg="#3a3a46", fg="#ffffff", relief="flat", command=self.destroy)
        btn_cancel.pack(side=tk.RIGHT, padx=8, ipadx=10, ipady=3)

    def save(self):
        sym = self.entries["symbol"].get().strip().upper()
        name = self.entries["name"].get().strip()
        market = self.market_var.get().strip().upper()
        shares_str = self.entries["shares"].get().strip()
        cost_str = self.entries["cost_price"].get().strip()
        disc_str = self.entries["fee_discount"].get().strip()
        cash_div_str = self.entries["cash_div"].get().strip()
        ex_date_str = self.entries["ex_date"].get().strip()
        note = self.entries["note"].get().strip()
        is_etf = self.etf_var.get()

        if not sym:
            messagebox.showerror("錯誤", "請輸入股票代碼！", parent=self)
            return

        # 寬容小數點與逗號清理
        shares = clean_number(shares_str, default=-1)
        cost_price = clean_number(cost_str, default=-1)
        fee_discount = clean_number(disc_str, default=0.6)

        if shares <= 0 or cost_price < 0:
            messagebox.showerror("錯誤", "持股數必須大於 0，成本均價必須為非負小數 (例如 580.5)！", parent=self)
            return

        # 儲存持股資料
        if self.pos:
            update_position(self.pos["id"], sym, name, market, shares, cost_price, fee_discount, is_etf, note)
        else:
            add_position(sym, name, market, shares, cost_price, fee_discount, is_etf, note)

        # 若使用者有輸入自訂股息資訊
        if cash_div_str:
            cash_div = clean_number(cash_div_str, 0.0)
            is_announced = 1 if (ex_date_str and ex_date_str != "未公布") else 0
            status_text = "已公布日期" if is_announced else "自訂/留存數據"
            DividendService.save_dividend_record(sym, cash_div, ex_date_str, status_text, str(datetime.now().year), is_announced)

        if self.on_saved:
            self.on_saved()
        self.destroy()


class ProgressDialog(tk.Toplevel):
    """盤後歷史增量更新進度視窗"""
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
    """查看個股歷史 K 線專業走勢圖 (蠟燭圖 + MA均線 + 成交量 + 十字游標 + 數據表)"""
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

        # 讀取最多 250 天歷史資料 (反轉回正序)
        self.records = get_history_kline(symbol, limit=250)

        # 頂部控制面板
        top_bar = tk.Frame(self, bg="#20202a", height=42)
        top_bar.pack(fill=tk.X, side=tk.TOP, padx=0, pady=0)

        title_txt = f"{symbol} {name} ({market}) - 歷史走勢圖"
        tk.Label(top_bar, text=title_txt, bg="#20202a", fg="#ffffff", font=("Microsoft JhengHei UI", 11, "bold")).pack(side=tk.LEFT, padx=12, pady=6)

        # 檢視模式切換：[K線圖] vs [明細表]
        self.view_mode = tk.StringVar(value="chart")
        btn_view_table = tk.Button(top_bar, text="數據表格", bg="#323242", fg="#ffffff", relief="flat", font=("Microsoft JhengHei UI", 9), command=self.show_table_view)
        btn_view_table.pack(side=tk.RIGHT, padx=8, pady=6, ipadx=8)

        btn_view_chart = tk.Button(top_bar, text="K線圖表", bg="#3a86ff", fg="#ffffff", relief="flat", font=("Microsoft JhengHei UI", 9, "bold"), command=self.show_chart_view)
        btn_view_chart.pack(side=tk.RIGHT, padx=4, pady=6, ipadx=8)
        self.btn_chart = btn_view_chart
        self.btn_table = btn_view_table

        # 週期快速切換
        tk.Label(top_bar, text="| 範圍:", bg="#20202a", fg="#888899").pack(side=tk.RIGHT, padx=4)
        for days, label in [(250, "全部"), (120, "120日"), (60, "60日"), (30, "30日")]:
            btn_d = tk.Button(top_bar, text=label, bg="#282835", fg="#d0d0d0", relief="flat", font=("Microsoft JhengHei UI", 8), command=lambda d=days: self.change_days(d))
            btn_d.pack(side=tk.RIGHT, padx=2, pady=6, ipadx=4)

        # 主顯示容器
        self.main_container = tk.Frame(self, bg="#181820")
        self.main_container.pack(fill=tk.BOTH, expand=True)

        # 1. K 線圖元件
        self.chart_widget = KLineChartCanvas(self.main_container, records=self.records, symbol=symbol, name=name)
        self.chart_widget.pack(fill=tk.BOTH, expand=True)

        # 2. 表格元件 (備用容器)
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


if __name__ == "__main__":
    app = PortfolioApp()
    app.mainloop()
