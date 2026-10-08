import os
import sys
import tkinter as tk
from tkinter import ttk, messagebox, colorchooser
import threading
import time
from datetime import datetime
from typing import Dict, List, Any, Optional

def get_resource_path(relative_path: str) -> str:
    """取得靜態資源路徑，相容開發環境與 PyInstaller 打包 (_MEIPASS)"""
    if hasattr(sys, '_MEIPASS'):
        base_path = sys._MEIPASS
    else:
        base_path = os.path.dirname(os.path.abspath(__file__))
    return os.path.join(base_path, relative_path)

from database import (
    init_db, get_all_positions, add_position, update_position, 
    delete_position, get_history_kline, get_setting, set_setting,
    get_trade_lots, add_trade_lot, update_trade_lot, delete_trade_lot, get_price_benchmarks,
    get_visible_columns, set_visible_columns, get_visible_cards, set_visible_cards,
    get_update_timestamp, set_update_timestamp,
    get_theme_settings, set_theme_settings, DEFAULT_THEME_SETTINGS,
    DEFAULT_VISIBLE_COLUMNS, DEFAULT_VISIBLE_CARDS
)
from quote_service import QuoteService
from history_service import HistoryService
from dividend_service import DividendService
from pnl_calculator import calculate_position_pnl
from kline_chart import KLineChartCanvas
from stock_detector import detect_stock_metadata
from crypto_sync import sync_to_cloud, fetch_and_decrypt_from_cloud, DEFAULT_FIREBASE_URL, get_or_create_secret_key
from updater import APP_VERSION, fetch_update_manifest, is_newer_version, UpdateDialog
from market_ranking_gui import MarketRankingDialog

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

def enable_numeric_input_guard(entry: tk.Entry, allow_decimal: bool = True):
    """
    數值欄位專屬防護機制：
    1. 專屬綁定右側九宮格數字鍵盤小數點 (<KP_Decimal>) 與主鍵盤點號 (<period>)，
       不管 Windows 系統語系將小數點識別為點或逗號、也不管中文輸入法狀態，直接強制插入半形 '.' 並中斷預設冒泡。
    2. 透過 Win32 ImmAssociateContext 與 ImmSetOpenStatus 深入視窗頂層 (GA_ROOT) 關閉輸入法 (IME)。
    3. <KeyRelease> 即時字元洗淨器：若輸入法組合送出全形數字 (０-９)、全形句號 (。/．/·) 或逗號 (,)，
       立即自動清洗替換為標準半形數字與小數點，並確保游標位置不遺失。
    """
    def disable_ime(event=None):
        try:
            import ctypes
            user32 = ctypes.windll.user32
            imm32 = ctypes.windll.imm32
            h_entry = entry.winfo_id()
            h_root = user32.GetAncestor(h_entry, 3) if h_entry else 0  # 3 = GA_ROOT
            for h in [h_entry, h_root]:
                if not h:
                    continue
                imc = imm32.ImmGetContext(h)
                if imc:
                    imm32.ImmSetOpenStatus(imc, 0)
                    imm32.ImmReleaseContext(h, imc)
                imm32.ImmAssociateContext(h, 0)
        except Exception:
            pass

    def on_kp_decimal(event):
        """專門處理九宮格數字鍵盤的小數點 (KP_Decimal)"""
        if not allow_decimal:
            return "break"
        cur = entry.get()
        if "." not in cur or entry.selection_present():
            # 若已有選取反白區塊，先刪除選取再插入
            if entry.selection_present():
                try:
                    entry.delete(tk.SEL_FIRST, tk.SEL_LAST)
                except Exception:
                    pass
            entry.insert(tk.INSERT, ".")
        return "break"

    def on_key_press(event):
        char = event.char
        keysym = event.keysym

        # 若按下主鍵盤 period 或數字鍵盤 KP_Decimal
        if keysym in ["KP_Decimal", "period"] or char == ".":
            if not allow_decimal:
                return "break"
            cur = entry.get()
            if "." not in cur or entry.selection_present():
                if entry.selection_present():
                    try:
                        entry.delete(tk.SEL_FIRST, tk.SEL_LAST)
                    except Exception:
                        pass
                entry.insert(tk.INSERT, ".")
            return "break"

        # 攔截全形數字 ０-９ (0xFF10 - 0xFF19) 自動轉半形
        if char and "０" <= char <= "９":
            digit = chr(ord(char) - 0xFEE0)
            entry.insert(tk.INSERT, digit)
            return "break"

        # 攔截全形標點 (。/．/·/・/、)
        if char in ["。", "．", "·", "・", "、"]:
            if allow_decimal:
                cur = entry.get()
                if "." not in cur or entry.selection_present():
                    if entry.selection_present():
                        try:
                            entry.delete(tk.SEL_FIRST, tk.SEL_LAST)
                        except Exception:
                            pass
                    entry.insert(tk.INSERT, ".")
            return "break"

    def on_key_release(event):
        """防止微軟輸入法在 Enter 確認送出文字後混入全形符號或逗點，即時進行文字淨化"""
        cur = entry.get()
        if not cur:
            return

        cleaned = []
        has_dot = False
        changed = False

        for ch in cur:
            if "０" <= ch <= "９":
                cleaned.append(chr(ord(ch) - 0xFEE0))
                changed = True
            elif ch in ["。", "．", "·", "・", "、", "."]:
                if allow_decimal and not has_dot:
                    cleaned.append(".")
                    has_dot = True
                else:
                    changed = True
                if ch != ".":
                    changed = True
            elif ch.isdigit():
                cleaned.append(ch)
            elif ch == "-" and len(cleaned) == 0:
                cleaned.append("-")
            else:
                # 過濾所有其他注音符號或非數字
                changed = True

        if changed:
            new_text = "".join(cleaned)
            idx = entry.index(tk.INSERT)
            entry.delete(0, tk.END)
            entry.insert(0, new_text)
            entry.icursor(min(idx, len(new_text)))

    # 綁定焦點、滑鼠點擊與鍵盤事件
    entry.bind("<FocusIn>", disable_ime, add="+")
    entry.bind("<Button-1>", disable_ime, add="+")
    entry.bind("<KP_Decimal>", on_kp_decimal)
    entry.bind("<KeyPress>", on_key_press)
    entry.bind("<KeyRelease>", on_key_release)
    entry.after(150, disable_ime)

# 欄位完整定義清單 (欄位ID, 顯示名稱, 預設寬度, 對齊方式, 分類)
ALL_COLUMN_SPECS = [
    ("symbol", "代碼", 80, "center", "基本"),
    ("name", "名稱", 120, "center", "基本"),
    ("market", "市場", 60, "center", "基本"),
    ("shares", "持股數", 85, "e", "部位"),
    ("cost_price", "成本均價", 90, "e", "部位"),
    ("current_price", "現價", 90, "e", "行情"),
    ("change_pct", "今日漲跌", 130, "e", "行情"),
    ("total_cost", "總成本", 105, "e", "損益"),
    ("market_val", "預估市值", 110, "e", "損益"),
    ("unrealized_pnl", "未實現損益", 115, "e", "損益"),
    ("roi_pct", "報酬率%", 90, "e", "損益"),
    ("day_pnl", "日損益", 125, "e", "週期損益"),
    ("week_pnl", "週損益", 125, "e", "週期損益"),
    ("month_pnl", "月損益", 125, "e", "週期損益"),
    ("frequency", "分配頻率", 80, "center", "股息"),
    ("cash_dividend", "每股年股息(單期)", 135, "e", "股息"),
    ("total_dividend", "預估年總股息", 110, "e", "股息"),
    ("yield_on_cost", "成本殖利率%", 95, "e", "股息"),
    ("ex_date", "除息日期/期別", 160, "center", "股息"),
    ("payment_month", "預估發放月", 90, "center", "股息"),
    ("hist_div_received", "累計已領股息", 120, "e", "股息歷史"),
    ("note", "備註", 110, "w", "其他")
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
    ("hist_div", "歷年累計已領股息", "#38bdf8", "依買入取得時間精算"),
    ("stock_count", "在庫標的數", "#3a86ff", "持股檔數與批次數")
]

class PortfolioApp(tk.Tk):
    def __init__(self, preloaded_quotes: Optional[Dict[str, Dict[str, Any]]] = None, preloaded_divs: Optional[Dict[str, Dict[str, Any]]] = None):
        super().__init__()
        # 關鍵核心：啟動時先隱藏主視窗 (withdraw)，杜絕 Windows 繪製空白白色畫布與未響應殘影！
        self.withdraw()
        self.title(f"本地端台美股庫存即時損益、歷史與股息追蹤系統 (Stock Portfolio Tracker) {APP_VERSION}")
        self.geometry("1420x820")
        self.minsize(1120, 640)

        # 設定 Windows 應用程式專屬 AppUserModelID (確保工作列與標題列永久綁定專屬 ICON)
        try:
            import ctypes
            myappid = 'isaacyang.stockportfoliotracker.app.v1'
            ctypes.windll.shell32.SetCurrentProcessExplicitAppUserModelID(myappid)
        except Exception:
            pass

        # 設定視窗左上角與工作列專屬金融美金圖示 (Windows 原生 .ico，徹底杜絕 Tkinter 預設藍色羽毛)
        icon_path = get_resource_path("app_icon.ico")
        if os.path.exists(icon_path):
            try:
                self.iconbitmap(icon_path)
            except Exception:
                try:
                    self.iconbitmap(default=icon_path)
                except Exception:
                    pass

        # 初始化資料庫
        init_db()

        self.quote_service = QuoteService()
        self.positions: List[Dict[str, Any]] = []
        self.latest_quotes: Dict[str, Dict[str, Any]] = dict(preloaded_quotes) if preloaded_quotes else {}
        self.dividend_cache: Dict[str, Dict[str, Any]] = dict(preloaded_divs) if preloaded_divs else {}
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
        self.active_update_dialog = None
        self.btn_post_market = None

        # 設定風格與配色
        self.setup_styles()
        self.build_ui()

        # 初始載入庫存與立即繪製表格 (開窗瞬間秒顯持股資訊，0秒空白等待)
        self.reload_positions()

        # 預先自本地 SQLite 補齊所有持股的股息快取 (極速純本地讀取，0 毫秒，絕無網路請求)
        for pos in self.positions:
            sym = pos["symbol"].upper()
            if sym not in self.dividend_cache:
                self.dividend_cache[sym] = DividendService.get_cached_or_db_dividend(sym)

        self.refresh_ui_table()

        # 完成所有 UI 元件與深色主題佈局後，強制更新繪圖緩衝區並秒級亮出主視窗 (0 秒白屏)
        self.update_idletasks()
        self.deiconify()
        self.lift()
        self.focus_force()

        if self.latest_quotes:
            now_str = datetime.now().strftime("%H:%M:%S")
            self.status_lbl.configure(text=f"最後更新時間: {now_str} (啟動預先載入)")
        else:
            self.trigger_refresh()

        self.trigger_dividend_update(silent=True)
        self.start_auto_refresh_timer()

        # 啟動後自動預熱市場排行快取、比對盤後資料、線上更新檢查與每日 15:00 定時同步監控 (延遲啟動確保 UI 流暢)
        self.after(1500, self.prewarm_market_ranking_cache)
        self.after(2500, self.auto_check_post_market_history)
        self.after(4000, self.check_online_update_silently)
        self.after(5500, self.start_daily_1500_scheduler)

        # 綁定視窗關閉事件，支援在退出時自動無聲套用延遲更新
        self.protocol("WM_DELETE_WINDOW", self._on_app_close)

    def on_update_deferred(self, cloud_ver: str):
        """當使用者選擇稍後更新時，在視窗標題與狀態列進行友好提示"""
        self.title(f"本地端台美股庫存即時損益、歷史與股息追蹤系統 (Stock Portfolio Tracker) {APP_VERSION} [🔔 新版 {cloud_ver} 待關閉更新]")
        if hasattr(self, 'status_lbl') and self.status_lbl:
            self.status_lbl.configure(text=f"🔔 新版本 {cloud_ver} 已下載就緒，將於程式關閉時自動置換升級")

    def _on_app_close(self):
        """主視窗關閉事件：檢查是否有待套用的延遲更新"""
        try:
            from updater import is_pending_update, apply_update_now, _PENDING_UPDATE, log_update_debug
            if is_pending_update():
                t_dir = _PENDING_UPDATE.get("target_dir", "")
                t_file = _PENDING_UPDATE.get("temp_file") or _PENDING_UPDATE.get("temp_exe", "")
                ver = _PENDING_UPDATE.get("version", "")
                log_update_debug(f"[CLOSE] Window closing detected pending update {ver}. Triggering silent apply_update (restart=False)...")
                if t_dir and t_file:
                    apply_update_now(t_dir, t_file, restart=False)
        except Exception as e:
            try:
                from updater import log_update_debug
                log_update_debug(f"[CLOSE] Error during pending update check on close: {e}")
            except Exception:
                pass
        self.destroy()
        sys.exit(0)

    def setup_styles(self):
        self.style = ttk.Style(self)
        try:
            self.style.theme_use("clam")
        except Exception:
            pass

        self.theme_settings = get_theme_settings()
        ts = self.theme_settings

        self.bg_color = ts.get("bg_color", "#1e1e24")
        self.card_bg = ts.get("card_bg", "#2b2b36")
        self.table_bg = ts.get("table_bg", "#252530")
        self.text_color = ts.get("text_color", "#ffffff")
        self.muted_text = "#a0a0b0"
        self.accent_blue = "#3a86ff"
        self.hist_div_color = ts.get("hist_div_color", "#38bdf8")
        self.font_family = ts.get("font_family", "Microsoft JhengHei UI")
        self.font_size = int(ts.get("font_size", 10))
        self.row_height = int(ts.get("row_height", 32))
        
        self.configure(bg=self.bg_color)

        self.style.configure(".", background=self.bg_color, foreground=self.text_color, font=(self.font_family, self.font_size))
        self.style.configure("TFrame", background=self.bg_color)
        self.style.configure("Card.TFrame", background=self.card_bg, relief="flat")
        self.style.configure("TLabel", background=self.bg_color, foreground=self.text_color, font=(self.font_family, self.font_size))
        self.style.configure("CardTitle.TLabel", background=self.card_bg, foreground=self.muted_text, font=(self.font_family, max(8, self.font_size - 1)))
        self.style.configure("CardVal.TLabel", background=self.card_bg, foreground=self.text_color, font=(self.font_family, self.font_size + 3, "bold"))

        self.style.configure("Treeview", 
                             background=self.table_bg, 
                             foreground=self.text_color, 
                             fieldbackground=self.table_bg,
                             rowheight=self.row_height,
                             font=(self.font_family, self.font_size))
        self.style.configure("Treeview.Heading", 
                             background="#323242", 
                             foreground="#ffffff", 
                             relief="flat", 
                             font=(self.font_family, self.font_size, "bold"))
        self.style.map("Treeview", background=[("selected", "#3a86ff")], foreground=[("selected", "#ffffff")])
        self.style.map("Treeview.Heading", background=[("active", "#404055")])
        self.style.configure("Action.TButton", font=(self.font_family, self.font_size, "bold"), padding=5)

        # 下拉選單 (Combobox) 深度優化：黑底白字極致高對比，徹底根除淺灰底白字無法閱讀之問題
        combo_bg = "#14141c"
        combo_btn_bg = "#282836"
        self.style.configure("TCombobox", 
                             fieldbackground=combo_bg, 
                             background=combo_btn_bg, 
                             foreground="#ffffff", 
                             darkcolor=combo_bg, 
                             lightcolor=combo_bg,
                             bordercolor="#3f3f50",
                             selectbackground="#2563eb", 
                             selectforeground="#ffffff",
                             arrowcolor="#38bdf8",
                             arrowsize=14,
                             padding=4)
        self.style.map("TCombobox", 
                       fieldbackground=[("readonly", combo_bg), ("focus", combo_bg), ("!disabled", combo_bg)],
                       foreground=[("readonly", "#ffffff"), ("focus", "#ffffff"), ("!disabled", "#ffffff")],
                       selectbackground=[("readonly", "#2563eb"), ("focus", "#2563eb")],
                       selectforeground=[("readonly", "#ffffff"), ("focus", "#ffffff")],
                       background=[("active", "#3b3b4d"), ("!disabled", combo_btn_bg)],
                       arrowcolor=[("readonly", "#38bdf8"), ("active", "#60a5fa")])

        # 下拉清單展開面板 (Popdown Listbox) 黑底白字高對比設定
        self.option_add("*TCombobox*Listbox.background", combo_bg)
        self.option_add("*TCombobox*Listbox.foreground", "#ffffff")
        self.option_add("*TCombobox*Listbox.selectBackground", "#2563eb")
        self.option_add("*TCombobox*Listbox.selectForeground", "#ffffff")
        self.option_add("*TCombobox*Listbox.font", (self.font_family, self.font_size))
        self.option_add("*TCombobox*Listbox.relief", "flat")

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



        btn_data_update = ttk.Button(bottom_frame, text="[⟳ 資料更新]", command=self.on_open_data_update, style="Action.TButton")
        btn_data_update.pack(side=tk.LEFT, padx=2)

        btn_ranking = ttk.Button(bottom_frame, text="[📊 市場排行] 4大指標", command=self.on_open_market_ranking, style="Action.TButton")
        btn_ranking.pack(side=tk.LEFT, padx=2)

        btn_view_kline = ttk.Button(bottom_frame, text="[K線] 個股走勢圖", command=self.on_view_kline, style="Action.TButton")
        btn_view_kline.pack(side=tk.LEFT, padx=2)

        btn_settings = ttk.Button(bottom_frame, text="[⚙ 設定] 介面與外觀", command=self.on_open_settings, style="Action.TButton")
        btn_settings.pack(side=tk.LEFT, padx=2)

        btn_cloud = ttk.Button(bottom_frame, text="[☁ 雲端] 加密同步", command=self.on_open_cloud_sync, style="Action.TButton")
        btn_cloud.pack(side=tk.LEFT, padx=2)

        self.btn_update = tk.Button(
            bottom_frame, text="[⬆ 軟體更新]", bg="#323242", fg="#ffffff",
            activebackground="#3a86ff", activeforeground="#ffffff",
            font=(self.font_family, self.font_size, "bold"), relief="flat", padx=8, pady=3,
            cursor="hand2", command=self.on_check_online_update
        )
        self.btn_update.pack(side=tk.LEFT, padx=3)

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
            if cid == "hist_div":
                text_col = getattr(self, "hist_div_color", "#38bdf8")
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

        # 建立選定持股反白時在代碼左側出現的快捷工具框 [✏][刪] (兩者尺寸完全均等)
        self.row_action_frame = tk.Frame(self.tree, bg="#181820", bd=1, relief="solid")
        self.row_action_frame.grid_columnconfigure(0, weight=1, uniform="row_act_btn")
        self.row_action_frame.grid_columnconfigure(1, weight=1, uniform="row_act_btn")
        self.row_action_frame.grid_rowconfigure(0, weight=1)

        # 修改持股按鈕：明確易辨識的標準鉛筆圖示 (✏)
        self.btn_row_edit = tk.Button(
            self.row_action_frame, text="✏", bg="#2563eb", fg="#ffffff",
            activebackground="#1d4ed8", activeforeground="#ffffff",
            font=("Segoe UI Emoji", 8, "bold"), relief="flat", bd=0, padx=0, pady=0,
            cursor="hand2", command=self.on_edit_position
        )
        self.btn_row_edit.grid(row=0, column=0, sticky="nsew", padx=(1, 1), pady=1)

        # 刪除持股按鈕：中文字 [刪]
        self.btn_row_del = tk.Button(
            self.row_action_frame, text="刪", bg="#dc2626", fg="#ffffff",
            activebackground="#b91c1c", activeforeground="#ffffff",
            font=("Microsoft JhengHei UI", 8, "bold"), relief="flat", bd=0, padx=0, pady=0,
            cursor="hand2", command=self.on_delete_position
        )
        self.btn_row_del.grid(row=0, column=1, sticky="nsew", padx=(1, 1), pady=1)

        # 綁定選取反白與點擊/雙擊事件
        self.tree.bind("<<TreeviewSelect>>", self.on_tree_select)
        self.tree.bind("<Button-1>", self.on_tree_click)
        self.tree.bind("<Double-1>", self.on_tree_double_click)
        self.tree.bind("<Escape>", self.clear_selection)
        self.bind("<Escape>", self.clear_selection)

        # 點擊表格容器 mid_frame 空白處亦取消選取
        self.mid_frame.bind("<Button-1>", lambda e: self.clear_selection())

        # 滾動時動態調整/隱藏浮動按鈕
        def on_y_scroll(*args):
            vsb.set(*args)
            self.on_tree_select()
        def on_x_scroll(*args):
            hsb.set(*args)
            self.on_tree_select()
        self.tree.configure(yscrollcommand=on_y_scroll, xscrollcommand=on_x_scroll)

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
        self.tree.tag_configure("add_row", foreground="#3a86ff", font=("Microsoft JhengHei UI", 10, "bold"))

    def autofit_columns(self):
        """依據標題與儲存格內容文字長度，自動適應欄位寬度（防文字裁切）"""
        try:
            import tkinter.font as tkfont
            cell_font = tkfont.Font(family=getattr(self, "font_family", "Microsoft JhengHei UI"), size=getattr(self, "font_size", 10))
            heading_font = tkfont.Font(family=getattr(self, "font_family", "Microsoft JhengHei UI"), size=getattr(self, "font_size", 10), weight="bold")
            spec_dict = {s[0]: s for s in ALL_COLUMN_SPECS}

            for col_id in self.visible_columns:
                spec = spec_dict.get(col_id)
                title = spec[1] if spec else col_id

                # 表頭標題寬度 + 排序箭頭與左右 padding
                max_w = heading_font.measure(title) + 32

                # 計算該欄所有儲存格的最大文字像素寬度
                for item in self.tree.get_children():
                    val = str(self.tree.set(item, col_id))
                    if val:
                        w = cell_font.measure(val) + 30
                        if w > max_w:
                            max_w = w

                base_w = spec[2] if spec else 60
                final_w = max(max_w, base_w)
                self.tree.column(col_id, width=final_w, minwidth=final_w)
        except Exception as e:
            print(f"[autofit_columns] 自動適應寬度異常: {e}")

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
        set_update_timestamp("dividends")
        self.refresh_ui_table()
        self.status_lbl.configure(text="股息與除息日期已同步")
        if self.active_update_dialog and self.active_update_dialog.winfo_exists():
            self.active_update_dialog.refresh_timestamps()

    def refresh_ui_table(self):
        """重新計算並填入表格與頂部卡片 (包含日/週/月週期損益與取得批次股息)"""
        cur_sel = self.tree.selection()
        selected_iid = cur_sel[0] if cur_sel else None
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

            # 取得股息資訊 (含頻率、發放月、依批次計算已領歷史股息，純本地或記憶體快取，絕不卡住 UI 線程)
            div_info = self.dividend_cache.get(sym) or DividendService.get_cached_or_db_dividend(sym)

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

            # 動態組裝欄位值 (在代碼前預留空白供選取時浮貼動作按鈕)
            val_map = {
                "symbol": f"       {pnl['symbol']}",
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

        # 依需求：[+] 直接顯示在上方目前有的存股最後一檔的代碼下方
        add_row_id = "__ADD_POSITION_ROW__"
        add_vals = []
        for cid in self.visible_columns:
            if cid == "symbol":
                add_vals.append("  [+] 新增持股")
            elif cid == "name":
                add_vals.append("點擊此處快速新增")
            else:
                add_vals.append("")
        self.tree.insert("", tk.END, iid=add_row_id, values=tuple(add_vals), tags=("add_row",))

        if selected_iid and self.tree.exists(selected_iid) and selected_iid != "__ADD_POSITION_ROW__":
            self.tree.selection_set(selected_iid)
            self.tree.focus(selected_iid)
            self.after(50, self.on_tree_select)
        else:
            self.row_action_frame.place_forget()

        # 自動依文字長度適應各欄位寬度，確保所有內容與符號完整顯示
        self.autofit_columns()

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
            self.cards["hist_div"].configure(text=f"$ {hist_div_sum:,.0f}", foreground=getattr(self, "hist_div_color", "#38bdf8"))

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
        set_update_timestamp("quotes")
        self.refresh_ui_table()
        self.status_lbl.configure(text=f"最後更新時間: {update_time}")
        if self.active_update_dialog and self.active_update_dialog.winfo_exists():
            self.active_update_dialog.refresh_timestamps()

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

    def clear_selection(self, event=None):
        """取消 Treeview 目前所有的反白選取狀態，並收合浮動按鈕列"""
        try:
            sel = self.tree.selection()
            if sel:
                self.tree.selection_remove(sel)
            self.tree.focus("")
            self.row_action_frame.place_forget()
        except Exception:
            pass

    def on_tree_click(self, event):
        item = self.tree.identify_row(event.y)
        if item == "__ADD_POSITION_ROW__":
            self.row_action_frame.place_forget()
            self.on_add_position()
            return "break"
        elif not item:
            # 點擊下方黑色無資料空白處，直接取消反白選取
            self.clear_selection()

    def on_tree_double_click(self, event):
        item = self.tree.identify_row(event.y)
        if item == "__ADD_POSITION_ROW__":
            self.row_action_frame.place_forget()
            self.on_add_position()
            return "break"
        elif item:
            self.on_edit_position()
            return "break"

    def on_tree_select(self, event=None):
        """當選定某檔持股後，有反白效果的同時，在代碼的左側出現筆的ICON(✎)，右側為[-]刪除按鈕"""
        sel = self.tree.selection()
        if not sel:
            self.row_action_frame.place_forget()
            return

        item_id = sel[0]
        if item_id == "__ADD_POSITION_ROW__":
            self.row_action_frame.place_forget()
            return

        bbox = self.tree.bbox(item_id, column="symbol")
        if bbox:
            x, y, w, h = bbox
            btn_w = 54
            btn_h = max(20, min(24, h - 4))
            pos_x = x + 2
            pos_y = y + (h - btn_h) // 2
            self.row_action_frame.place(x=pos_x, y=pos_y, width=btn_w, height=btn_h)
            self.row_action_frame.lift()
        else:
            self.row_action_frame.place_forget()

    def on_add_position(self):
        self.row_action_frame.place_forget()
        PositionEditDialog(self, title="新增持股部位", on_saved=self._on_position_saved)

    def on_edit_position(self):
        selected = self.tree.selection()
        if not selected or selected[0] == "__ADD_POSITION_ROW__":
            messagebox.showinfo("提示", "請先點選欲修改的持股！", parent=self)
            return
        pos_id = int(selected[0])
        pos = next((p for p in self.positions if p["id"] == pos_id), None)
        if pos:
            PositionEditDialog(self, title=f"修改持股與取得時間管理 - {pos['symbol']}", pos=pos, on_saved=self._on_position_saved)

    def on_manage_lots(self):
        """開啟買入批次管理 (取得時間明細)"""
        selected = self.tree.selection()
        if not selected or selected[0] == "__ADD_POSITION_ROW__":
            messagebox.showinfo("提示", "請先點選欲管理買入批次的股票！", parent=self)
            return
        pos_id = int(selected[0])
        pos = next((p for p in self.positions if p["id"] == pos_id), None)
        if pos:
            TradeLotManagerDialog(self, symbol=pos["symbol"], name=pos["name"], on_lots_changed=self._on_position_saved)

    def on_delete_position(self):
        selected = self.tree.selection()
        if not selected or selected[0] == "__ADD_POSITION_ROW__":
            messagebox.showinfo("提示", "請先點選欲刪除的持股！", parent=self)
            return
        pos_id = int(selected[0])
        pos = next((p for p in self.positions if p["id"] == pos_id), None)
        if not pos:
            return
        confirm = messagebox.askyesno("確認刪除", f"確定要從本地庫存刪除 {pos['symbol']} ({pos['name']}) 及其所有買入批次嗎？", parent=self)
        if confirm:
            self.row_action_frame.place_forget()
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

    def on_open_data_update(self):
        """開啟資料更新管理視窗 (手動更新行情/股息/盤後與檢視最後更新時間)"""
        DataUpdateDialog(self)

    def start_daily_1500_scheduler(self):
        """啟動每日 15:00 自動定時更新盤後與股息資料監控"""
        def check_daily_task():
            now = datetime.now()
            today_str = now.strftime("%Y-%m-%d")
            last_run = get_setting("last_daily_1500_run_date", "")

            # 若到了 15:00 (且今天尚未執行過，且為週一至週五交易日)
            if now.hour == 15 and now.minute >= 0 and last_run != today_str:
                if now.weekday() < 5:
                    print(f"[Scheduler] 觸發每日 15:00 盤後與股息自動同步任務...")
                    set_setting("last_daily_1500_run_date", today_str)
                    self.status_lbl.configure(text="[定時任務 15:00] 正在自動同步盤後日K與最新除權息公告...")
                    self.trigger_dividend_update(silent=True)
                    self.on_update_history_all(silent=True)

            # 每 30 秒輪詢一次
            self.after(30000, check_daily_task)

        self.after(1000, check_daily_task)

    def on_check_online_update(self):
        """使用者手動點擊檢查軟體更新：彈窗告知版本更新內容並由使用者自主決定"""
        self.status_lbl.configure(text="正在連線檢查軟體最新版本...")
        def worker():
            ok, manifest, source = fetch_update_manifest()
            if not ok or not manifest:
                self.after(0, lambda: messagebox.showinfo("軟體更新", "目前版本伺服器離線或同步中，請稍候再試。", parent=self))
                self.after(0, lambda: self.status_lbl.configure(text="系統就緒"))
                return
            cloud_ver = manifest.get("version", APP_VERSION)
            is_newer = is_newer_version(cloud_ver, APP_VERSION)
            self.after(0, lambda: UpdateDialog(self, manifest, is_newer))
            self.after(0, lambda: self.status_lbl.configure(text="系統就緒"))
        threading.Thread(target=worker, daemon=True).start()

    def check_online_update_silently(self):
        """軟體開啟後自動在背景檢查更新：若有新版本則變色提示"""
        def worker():
            ok, manifest, source = fetch_update_manifest()
            if ok and manifest:
                cloud_ver = manifest.get("version", APP_VERSION)
                if is_newer_version(cloud_ver, APP_VERSION):
                    self.after(0, lambda: self._on_found_newer_version(cloud_ver))
        threading.Thread(target=worker, daemon=True).start()

    def _on_found_newer_version(self, cloud_ver: str):
        """發現新版本時：按鈕變色為鮮明亮橘色高亮警示"""
        self.btn_update.configure(
            text=f"[🔥 發現新版 {cloud_ver}]",
            bg="#f59e0b", fg="#ffffff",
            activebackground="#d97706", activeforeground="#ffffff"
        )
        self.status_lbl.configure(text=f"💡 發現軟體新版本 {cloud_ver}！點擊「[🔥 發現新版]」即可檢視更新內容並決定是否升級。")

    def auto_check_post_market_history(self):
        """軟體開啟後自動比對盤後歷史資料是否要更新"""
        if not self.positions:
            return
        def worker():
            needs_update, reason, symbols = HistoryService.check_needs_update(self.positions)
            if needs_update:
                self.after(0, lambda r=reason: self._start_auto_post_market_update(r))
            else:
                self.after(0, lambda r=reason: self._show_post_market_up_to_date(r))
        threading.Thread(target=worker, daemon=True).start()

    def _show_post_market_up_to_date(self, reason: str):
        self.history_progress_lbl.configure(text=f"✔ 盤後資料已是最新", fg="#52c41a")
        self.after(6000, self._reset_history_progress_ui)

    def _start_auto_post_market_update(self, reason: str):
        self.status_lbl.configure(text=f"[自動比對] {reason}")
        self.on_update_history_all(silent=True)

    def _on_settings_applied(self):
        self.setup_styles()
        self.visible_columns = get_visible_columns()
        self.visible_cards = get_visible_cards()
        self.rebuild_cards()
        self.rebuild_table()
        self.refresh_ui_table()

    # --- 盤後更新與歷史查詢 (背景非同步執行，不佔用視窗) ---

    def on_update_history_all(self, silent: bool = False):
        """背景非同步更新盤後歷史資料 (不佔用視窗、不鎖定畫面、底部狀態列顯示進度)"""
        if self.is_history_updating:
            self.status_lbl.configure(text="💡 盤後歷史資料正在背景同步中，請稍候...")
            return

        if not self.positions:
            self.status_lbl.configure(text="提示: 目前庫存尚無持股部位可供更新。")
            return

        self.is_history_updating = True
        if self.btn_post_market:
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
        msg = f"[盤後更新中 {cur_idx}/{total}] 正在同步 {sym} {name}..."
        self.history_progress_lbl.configure(text=msg, fg="#52c41a")
        if self.active_update_dialog and self.active_update_dialog.winfo_exists():
            self.active_update_dialog.lbl_hist_time.configure(text=f"下載進度: {sym} {name} ({cur_idx}/{total})")

    def _update_history_progress_text(self, msg: str):
        # 顯示更詳細的細項進度，例如月份或狀態
        disp_msg = msg if len(msg) <= 42 else msg[:39] + "..."
        self.history_progress_lbl.configure(text=disp_msg, fg="#87d068")
        if self.active_update_dialog and self.active_update_dialog.winfo_exists():
            self.active_update_dialog.lbl_hist_time.configure(text=f"進度: {disp_msg}")

    def _on_history_update_completed(self, count: int, total_days: int):
        self.is_history_updating = False
        set_update_timestamp("history")
        if self.btn_post_market:
            self.btn_post_market.configure(text="[盤後] 更新歷史資料", state=tk.NORMAL)
        self.history_pbar["value"] = self.history_pbar["maximum"]
        self.history_progress_lbl.configure(
            text=f"✔ 盤後歷史更新完成 (共 {count} 檔，新增 {total_days} 筆日K)", 
            fg="#52c41a"
        )
        self.benchmarks_cache.clear()
        self.trigger_refresh()
        if self.active_update_dialog and self.active_update_dialog.winfo_exists():
            self.active_update_dialog.on_history_sync_done()
        self.after(8000, self._reset_history_progress_ui)

    def _on_history_update_failed(self, err: str):
        self.is_history_updating = False
        if self.btn_post_market:
            self.btn_post_market.configure(text="[盤後] 更新歷史資料", state=tk.NORMAL)
        self.history_pbar.pack_forget()
        self.history_progress_lbl.configure(text=f"[!] 盤後更新異常: {err}", fg="#ff7875")
        if self.active_update_dialog and self.active_update_dialog.winfo_exists():
            self.active_update_dialog.on_history_sync_done()
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

    def prewarm_market_ranking_cache(self):
        """主畫面載入完成後，在背景靜默預先抓取/快取市場 4 大指標排行數據，確保使用者點開時 0 秒秒開"""
        def worker():
            try:
                from ranking_service import MarketRankingService
                MarketRankingService.get_market_data(force_refresh=False)
            except Exception as e:
                print(f"[MarketRanking] 背景預熱失敗: {e}")

        threading.Thread(target=worker, daemon=True).start()

    def on_open_market_ranking(self):
        """開啟台股與 ETF 4 大核心財務指標排行榜視窗"""
        existing_syms = {p["symbol"].strip().upper() for p in self.positions}

        def on_add_cb(symbol, name, is_etf):
            pos_seed = {
                "symbol": symbol,
                "name": name,
                "market": "TW",
                "is_etf": is_etf,
                "shares": 1000,
                "cost_price": 0.0,
                "fee_discount": 0.6,
                "note": "來自市場排行榜篩選"
            }
            PositionEditDialog(
                self,
                title=f"新增持股部位 - {symbol} {name}",
                pos=pos_seed,
                on_saved=self.reload_positions
            )

        MarketRankingDialog(self, on_add_position_callback=on_add_cb, existing_symbols=existing_syms)


class PositionEditDialog(tk.Toplevel):
    """新增/修改持股部位對話框 (整合取得時間批次管理與自動同步股息)"""
    def __init__(self, parent, title: str, pos: Optional[Dict[str, Any]] = None, on_saved=None):
        super().__init__(parent)
        self.title(title)
        self.geometry("820x640")
        self.minsize(720, 560)
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

        self.editing_lot_id = None
        self.build_ui()
        if self.pos:
            self.trigger_detect(self.pos["symbol"])
            self.reload_lots()

    def build_ui(self):
        # ==========================================
        # 1. 上半部：持股部位基本資料設定
        # ==========================================
        base_frame = tk.LabelFrame(self, text="持股基本資料維護", bg="#262633", fg="#ffffff", font=("Microsoft JhengHei UI", 10, "bold"), padx=12, pady=8)
        base_frame.pack(fill=tk.X, padx=14, pady=(10, 6))

        # 第 1 列：股票代碼與辨識標籤
        r1 = tk.Frame(base_frame, bg="#262633")
        r1.pack(fill=tk.X, pady=2)

        tk.Label(r1, text="股票代碼/標的:", bg="#262633", fg="#ffffff", font=("Microsoft JhengHei UI", 9, "bold")).pack(side=tk.LEFT)
        self.ent_symbol = tk.Entry(r1, bg="#1e1e28", fg="#ffffff", insertbackground="#ffffff", font=("Microsoft JhengHei UI", 10, "bold"), width=14, relief="flat")
        if self.pos:
            self.ent_symbol.insert(0, self.pos["symbol"])
        self.ent_symbol.pack(side=tk.LEFT, padx=8, ipady=2)
        self.ent_symbol.bind("<KeyRelease>", self.on_symbol_changed)
        self.ent_symbol.bind("<FocusOut>", self.on_symbol_focus_out)

        self.lbl_detect = tk.Label(r1, text="輸入代碼後將自動辨識名稱、市場與 ETF...", bg="#262633", fg="#a0e0a0", font=("Microsoft JhengHei UI", 9))
        self.lbl_detect.pack(side=tk.LEFT, padx=10)

        # 第 2 列：股數、成本均價、手續費折扣、備註
        r2 = tk.Frame(base_frame, bg="#262633")
        r2.pack(fill=tk.X, pady=4)

        tk.Label(r2, text="持有總股數:", bg="#262633", fg="#ffffff", font=("Microsoft JhengHei UI", 9)).grid(row=0, column=0, sticky="w", pady=2)
        self.ent_shares = tk.Entry(r2, bg="#1e1e28", fg="#ffffff", insertbackground="#ffffff", font=("Microsoft JhengHei UI", 10), width=12, relief="flat")
        self.ent_shares.insert(0, str(self.pos["shares"]) if self.pos else "")
        self.ent_shares.grid(row=0, column=1, padx=(6, 16), pady=2, ipady=2)
        enable_numeric_input_guard(self.ent_shares, allow_decimal=False)

        tk.Label(r2, text="買入成本均價:", bg="#262633", fg="#ffffff", font=("Microsoft JhengHei UI", 9)).grid(row=0, column=2, sticky="w", pady=2)
        self.ent_price = tk.Entry(r2, bg="#1e1e28", fg="#ffffff", insertbackground="#ffffff", font=("Microsoft JhengHei UI", 10), width=12, relief="flat")
        if self.pos:
            self.ent_price.insert(0, str(self.pos["cost_price"]))
        self.ent_price.grid(row=0, column=3, padx=(6, 16), pady=2, ipady=2)
        enable_numeric_input_guard(self.ent_price, allow_decimal=True)

        tk.Label(r2, text="手續費折扣:", bg="#262633", fg="#ffffff", font=("Microsoft JhengHei UI", 9)).grid(row=0, column=4, sticky="w", pady=2)
        self.ent_discount = tk.Entry(r2, bg="#1e1e28", fg="#ffffff", insertbackground="#ffffff", font=("Microsoft JhengHei UI", 10), width=8, relief="flat")
        self.ent_discount.insert(0, str(self.pos["fee_discount"]) if self.pos else "0.6")
        self.ent_discount.grid(row=0, column=5, padx=(6, 16), pady=2, ipady=2)
        enable_numeric_input_guard(self.ent_discount, allow_decimal=True)

        tk.Label(r2, text="備註:", bg="#262633", fg="#ffffff", font=("Microsoft JhengHei UI", 9)).grid(row=0, column=6, sticky="w", pady=2)
        self.ent_note = tk.Entry(r2, bg="#1e1e28", fg="#ffffff", insertbackground="#ffffff", font=("Microsoft JhengHei UI", 10), width=14, relief="flat")
        if self.pos:
            self.ent_note.insert(0, self.pos.get("note", ""))
        self.ent_note.grid(row=0, column=7, padx=(6, 4), pady=2, ipady=2)

        # ==========================================
        # 2. 下半部：整合「取得時間批次管理」
        # ==========================================
        self.lot_frame = tk.LabelFrame(
            self,
            text="取得時間批次管理 (依取得時間精算歷年實領股息)",
            bg="#262633", fg="#ffffff", font=("Microsoft JhengHei UI", 10, "bold"),
            padx=12, pady=8
        )
        self.lot_frame.pack(fill=tk.BOTH, expand=True, padx=14, pady=4)

        # 批次明細清單表格
        table_box = tk.Frame(self.lot_frame, bg="#181820", relief="solid", bd=1)
        table_box.pack(fill=tk.BOTH, expand=True, pady=(2, 6))

        cols = [
            ("id", "序號", 50),
            ("acquire_date", "取得時間", 110),
            ("shares", "買入股數", 100),
            ("price", "買入單價", 100),
            ("fee", "手續費", 80),
            ("note", "備註說明", 160)
        ]
        self.lot_tree = ttk.Treeview(table_box, columns=[c[0] for c in cols], show="headings", height=6)
        lot_vsb = ttk.Scrollbar(table_box, orient="vertical", command=self.lot_tree.yview)
        self.lot_tree.configure(yscrollcommand=lot_vsb.set)

        for cid, cname, w in cols:
            self.lot_tree.heading(cid, text=cname)
            self.lot_tree.column(cid, width=w, anchor="center" if cid in ["id", "acquire_date"] else ("e" if cid in ["shares", "price", "fee"] else "w"))

        self.lot_tree.pack(side=tk.LEFT, fill=tk.BOTH, expand=True)
        lot_vsb.pack(side=tk.RIGHT, fill=tk.Y)
        self.lot_tree.bind("<Double-1>", lambda e: self.on_start_edit_lot())

        # 快速新增/修改批次輸入列
        add_box = tk.Frame(self.lot_frame, bg="#262633")
        add_box.pack(fill=tk.X, pady=4)

        tk.Label(add_box, text="取得日期:", bg="#262633", fg="#ffffff", font=("Microsoft JhengHei UI", 9)).pack(side=tk.LEFT)
        self.ent_lot_date = tk.Entry(add_box, width=11, font=("Microsoft JhengHei UI", 9), bg="#1e1e28", fg="#ffffff", relief="flat")
        self.ent_lot_date.insert(0, datetime.now().strftime("%Y-%m-%d"))
        self.ent_lot_date.pack(side=tk.LEFT, padx=4, ipady=1)

        tk.Label(add_box, text="股數:", bg="#262633", fg="#ffffff", font=("Microsoft JhengHei UI", 9)).pack(side=tk.LEFT, padx=(4, 0))
        self.ent_lot_shares = tk.Entry(add_box, width=9, font=("Microsoft JhengHei UI", 9), bg="#1e1e28", fg="#ffffff", relief="flat")
        self.ent_lot_shares.insert(0, "1000")
        self.ent_lot_shares.pack(side=tk.LEFT, padx=4, ipady=1)
        enable_numeric_input_guard(self.ent_lot_shares, allow_decimal=False)

        tk.Label(add_box, text="單價:", bg="#262633", fg="#ffffff", font=("Microsoft JhengHei UI", 9)).pack(side=tk.LEFT, padx=(4, 0))
        self.ent_lot_price = tk.Entry(add_box, width=9, font=("Microsoft JhengHei UI", 9), bg="#1e1e28", fg="#ffffff", relief="flat")
        if self.pos:
            self.ent_lot_price.insert(0, str(self.pos["cost_price"]))
        self.ent_lot_price.pack(side=tk.LEFT, padx=4, ipady=1)
        enable_numeric_input_guard(self.ent_lot_price, allow_decimal=True)
        self.ent_lot_price.bind("<Return>", lambda e: self.on_add_lot())
        self.ent_lot_price.bind("<KP_Enter>", lambda e: self.on_add_lot())

        tk.Label(add_box, text="備註:", bg="#262633", fg="#ffffff", font=("Microsoft JhengHei UI", 9)).pack(side=tk.LEFT, padx=(4, 0))
        self.ent_lot_note = tk.Entry(add_box, width=12, font=("Microsoft JhengHei UI", 9), bg="#1e1e28", fg="#ffffff", relief="flat")
        self.ent_lot_note.pack(side=tk.LEFT, padx=4, ipady=1)
        self.ent_lot_note.bind("<Return>", lambda e: self.on_add_lot())
        self.ent_lot_note.bind("<KP_Enter>", lambda e: self.on_add_lot())

        self.btn_add_lot = tk.Button(add_box, text="[+] 新增此批次", bg="#3a86ff", fg="#ffffff", font=("Microsoft JhengHei UI", 9, "bold"), relief="flat", command=self.on_add_lot)
        self.btn_add_lot.pack(side=tk.LEFT, padx=6)

        self.btn_cancel_lot_edit = tk.Button(add_box, text="[✕ 放棄修改]", bg="#4a4a5a", fg="#ffffff", font=("Microsoft JhengHei UI", 9), relief="flat", command=self.on_cancel_edit_lot)

        # 批次工具與統計資訊條
        lot_bar = tk.Frame(self.lot_frame, bg="#262633")
        lot_bar.pack(fill=tk.X, pady=(4, 0))

        self.lbl_lot_summary = tk.Label(lot_bar, text="尚未設定取得時間批次", bg="#262633", fg="#ffd166", font=("Microsoft JhengHei UI", 9, "bold"))
        self.lbl_lot_summary.pack(side=tk.LEFT)

        btn_fill_from_lots = tk.Button(lot_bar, text="[↺ 依批次自動填入總股數與均價]", bg="#2b4c7e", fg="#ffffff", font=("Microsoft JhengHei UI", 8), relief="flat", command=self.on_fill_from_lots)
        btn_fill_from_lots.pack(side=tk.RIGHT, padx=4)

        btn_del_lot = tk.Button(lot_bar, text="[-] 刪除選取批次", bg="#ff4d4f", fg="#ffffff", font=("Microsoft JhengHei UI", 8), relief="flat", command=self.on_delete_selected_lot)
        btn_del_lot.pack(side=tk.RIGHT, padx=4)

        btn_edit_lot = tk.Button(lot_bar, text="[✏ 編輯修改選取批次]", bg="#d97706", fg="#ffffff", font=("Microsoft JhengHei UI", 8), relief="flat", command=self.on_start_edit_lot)
        btn_edit_lot.pack(side=tk.RIGHT, padx=4)

        # ==========================================
        # 3. 底部動作列
        # ==========================================
        bot = tk.Frame(self, bg="#22222a", padx=14, pady=10)
        bot.pack(fill=tk.X, side=tk.BOTTOM)

        btn_save = tk.Button(bot, text="儲存並套用全部 (自動同步股息)", bg="#52c41a", fg="#ffffff", font=("Microsoft JhengHei UI", 10, "bold"), relief="flat", command=self.save)
        btn_save.pack(side=tk.RIGHT, ipadx=12, ipady=3)

        btn_cancel = tk.Button(bot, text="取消", bg="#3a3a46", fg="#ffffff", font=("Microsoft JhengHei UI", 9), relief="flat", command=self.destroy)
        btn_cancel.pack(side=tk.RIGHT, padx=8, ipadx=10, ipady=3)

    def on_symbol_changed(self, event):
        val = self.ent_symbol.get().strip().upper()
        if len(val) >= 4 or (val.isalpha() and len(val) >= 2):
            self.trigger_detect(val)
            self.reload_lots()

    def on_symbol_focus_out(self, event):
        val = self.ent_symbol.get().strip().upper()
        if val:
            self.trigger_detect(val)
            self.reload_lots()

    def trigger_detect(self, val: str):
        def worker():
            meta = detect_stock_metadata(val)
            self.detected_meta = meta
            self.after(0, lambda: self.lbl_detect.configure(text=f"✔ 已辨識：{meta['display_info']}", fg="#52c41a"))
        threading.Thread(target=worker, daemon=True).start()

    def reload_lots(self):
        """讀取並重新繪製買入批次明細，且自動將加權平均均價與總股數同步回填至上方欄位"""
        sym = self.ent_symbol.get().strip().upper()
        if not sym:
            return

        self.lot_tree.delete(*self.lot_tree.get_children())
        lots = get_trade_lots(sym)
        total_shares = sum(l["shares"] for l in lots)
        total_val = sum(l["shares"] * l["price"] for l in lots)
        avg_price = (total_val / total_shares) if total_shares > 0 else 0.0

        for idx, l in enumerate(lots, start=1):
            self.lot_tree.insert("", tk.END, iid=str(l["id"]), values=(
                idx,
                l["acquire_date"],
                f"{l['shares']:,.0f}" if l["shares"].is_integer() else f"{l['shares']:,.2f}",
                f"{l['price']:,.2f}",
                f"{l['fee']:,.0f}",
                l["note"]
            ))

        hist_div_total, count = DividendService.calc_historical_received(sym, lots)
        if count > 0:
            summary_text = (
                f"合計總股數: {total_shares:,.0f} 股  |  加權均價: ${avg_price:,.2f}  |  "
                f"歷年累計已領股息: ${hist_div_total:,.0f} (共{count}批次)"
            )
        else:
            summary_text = "尚未登記取得時間批次 (可在上方輸入取得日期與股數加入)"
        self.lbl_lot_summary.configure(text=summary_text)

        # 核心優化：只要下方有登記取得批次，就自動幫使用者回填上方的總股數與加權均價，免除手動重打
        if total_shares > 0:
            self.ent_shares.delete(0, tk.END)
            self.ent_shares.insert(0, str(int(total_shares) if total_shares.is_integer() else total_shares))
            self.ent_price.delete(0, tk.END)
            self.ent_price.insert(0, f"{avg_price:.2f}")

    def on_start_edit_lot(self):
        """點擊編輯或雙擊批次行時，載入該批次進行修改"""
        sel = self.lot_tree.selection()
        if not sel:
            messagebox.showinfo("提示", "請先點選欲修改的買入批次！", parent=self)
            return
        lot_id = int(sel[0])
        sym = self.ent_symbol.get().strip().upper()
        lots = get_trade_lots(sym)
        target = next((l for l in lots if l["id"] == lot_id), None)
        if not target:
            return

        self.editing_lot_id = lot_id
        self.ent_lot_date.delete(0, tk.END)
        self.ent_lot_date.insert(0, target["acquire_date"])

        self.ent_lot_shares.delete(0, tk.END)
        sh_val = target["shares"]
        self.ent_lot_shares.insert(0, str(int(sh_val) if sh_val.is_integer() else sh_val))

        self.ent_lot_price.delete(0, tk.END)
        self.ent_lot_price.insert(0, str(target["price"]))

        self.ent_lot_note.delete(0, tk.END)
        self.ent_lot_note.insert(0, target.get("note", ""))

        self.btn_add_lot.configure(text="[💾 儲存修改批次]", bg="#f59e0b", activebackground="#d97706")
        self.btn_cancel_lot_edit.pack(side=tk.LEFT, padx=4)

    def on_cancel_edit_lot(self):
        """取消批次修改模式，重設輸入欄位"""
        self.editing_lot_id = None
        self.btn_add_lot.configure(text="[+] 新增此批次", bg="#3a86ff", activebackground="#2563eb")
        self.btn_cancel_lot_edit.pack_forget()
        self.ent_lot_date.delete(0, tk.END)
        self.ent_lot_date.insert(0, datetime.now().strftime("%Y-%m-%d"))
        self.ent_lot_shares.delete(0, tk.END)
        self.ent_lot_shares.insert(0, "1000")
        self.ent_lot_price.delete(0, tk.END)
        if self.pos:
            self.ent_lot_price.insert(0, str(self.pos["cost_price"]))
        self.ent_lot_note.delete(0, tk.END)

    def on_add_lot(self):
        """新增或儲存修改買入批次"""
        sym = self.ent_symbol.get().strip().upper()
        if not sym:
            messagebox.showerror("錯誤", "請先輸入上方股票代碼！", parent=self)
            return

        dt_str = self.ent_lot_date.get().strip()
        sh_str = self.ent_lot_shares.get().strip()
        pr_str = self.ent_lot_price.get().strip()
        note = self.ent_lot_note.get().strip()

        if not dt_str:
            messagebox.showerror("錯誤", "請輸入取得時間 (YYYY-MM-DD)！", parent=self)
            return

        shares = clean_number(sh_str, -1)
        price = clean_number(pr_str, -1)
        disc = clean_number(self.ent_discount.get().strip(), 0.6)

        if shares <= 0 or price < 0:
            messagebox.showerror("錯誤", "股數必須大於 0，價格必須大於等於 0！", parent=self)
            return

        fee = max(20.0, round(shares * price * 0.001425 * disc, 0))

        if self.editing_lot_id is not None:
            update_trade_lot(self.editing_lot_id, sym, dt_str, shares, price, fee, note)
            self.on_cancel_edit_lot()
        else:
            add_trade_lot(sym, dt_str, shares, price, fee, note)
            self.ent_lot_note.delete(0, tk.END)

        self.reload_lots()

        # 友善體驗：新增/修改成功後自動將游標焦點切回「取得日期」，並反白文字方便下一筆連續輸入
        try:
            self.ent_lot_date.focus_set()
            self.ent_lot_date.selection_range(0, tk.END)
        except Exception:
            pass

    def on_delete_selected_lot(self):
        sel = self.lot_tree.selection()
        if not sel:
            messagebox.showinfo("提示", "請先點選欲刪除的買入批次！", parent=self)
            return
        lot_id = int(sel[0])
        sym = self.ent_symbol.get().strip().upper()
        delete_trade_lot(lot_id, sym)
        self.reload_lots()

    def on_fill_from_lots(self):
        """依據已登記的買入批次，自動計算總股數與加權均價回填到上方輸入框"""
        sym = self.ent_symbol.get().strip().upper()
        lots = get_trade_lots(sym)
        if not lots:
            messagebox.showinfo("提示", "目前尚無任何買入批次可回填！", parent=self)
            return

        total_shares = sum(l["shares"] for l in lots)
        total_val = sum(l["shares"] * l["price"] for l in lots)
        avg_price = (total_val / total_shares) if total_shares > 0 else 0.0

        self.ent_shares.delete(0, tk.END)
        self.ent_shares.insert(0, str(int(total_shares) if total_shares.is_integer() else total_shares))

        self.ent_price.delete(0, tk.END)
        self.ent_price.insert(0, f"{avg_price:.2f}")
        messagebox.showinfo("成功", f"已依據 {len(lots)} 筆批次明細自動回填：\n總股數: {total_shares:,.0f} 股\n加權均價: ${avg_price:.2f}", parent=self)

    def save(self):
        sym_input = self.ent_symbol.get().strip().upper()
        if not sym_input:
            messagebox.showerror("錯誤", "請輸入股票代碼！", parent=self)
            return

        # 智慧回算與容錯機制：
        # 1. 優先檢查下方是否已建立買入批次 (Trade Lots)
        lots = get_trade_lots(sym_input)
        total_lot_shares = sum(l["shares"] for l in lots)
        total_lot_val = sum(l["shares"] * l["price"] for l in lots)
        avg_lot_price = (total_lot_val / total_lot_shares) if total_lot_shares > 0 else 0.0

        shares_str = self.ent_shares.get().strip()
        cost_str = self.ent_price.get().strip()
        disc_str = self.ent_discount.get().strip()
        note = self.ent_note.get().strip()

        shares = clean_number(shares_str, default=-1)
        cost_price = clean_number(cost_str, default=-1)
        fee_discount = clean_number(disc_str, default=0.6)

        # 若使用者上方未輸入或為空，但下方有批次，自動無縫採用批次累計數據
        if (shares <= 0 or cost_price < 0) and total_lot_shares > 0:
            shares = total_lot_shares
            cost_price = round(avg_lot_price, 2)
            # 同步回填畫面上
            self.ent_shares.delete(0, tk.END)
            self.ent_shares.insert(0, str(int(shares) if shares.is_integer() else shares))
            self.ent_price.delete(0, tk.END)
            self.ent_price.insert(0, f"{cost_price:.2f}")

        if shares <= 0 or cost_price < 0:
            messagebox.showerror("錯誤", "持股數必須大於 0，成本均價必須為有效小數！\n(請於上方填入股數與均價，或於下方登記買入批次明細)", parent=self)
            return

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
        self.editing_lot_id = None

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
        self.tree.bind("<Double-1>", lambda e: self.on_start_edit_lot())

        # 新增/修改批次輸入面板
        input_frame = tk.LabelFrame(self, text="新增/修改買入批次", bg="#282835", fg="#d0d0e0", font=("Microsoft JhengHei UI", 9, "bold"), padx=10, pady=6)
        input_frame.pack(fill=tk.X, padx=12, pady=(0, 6))

        tk.Label(input_frame, text="取得日期 (YYYY-MM-DD):", bg="#282835", fg="#ffffff").grid(row=0, column=0, padx=4, pady=2)
        self.ent_date = tk.Entry(input_frame, width=12, font=("Microsoft JhengHei UI", 9), bg="#1e1e28", fg="#ffffff", insertbackground="#ffffff", relief="flat")
        self.ent_date.insert(0, datetime.now().strftime("%Y-%m-%d"))
        self.ent_date.grid(row=0, column=1, padx=4, pady=2)

        tk.Label(input_frame, text="股數:", bg="#282835", fg="#ffffff").grid(row=0, column=2, padx=4, pady=2)
        self.ent_shares = tk.Entry(input_frame, width=10, font=("Microsoft JhengHei UI", 9), bg="#1e1e28", fg="#ffffff", insertbackground="#ffffff", relief="flat")
        self.ent_shares.insert(0, "1000")
        self.ent_shares.grid(row=0, column=3, padx=4, pady=2)
        enable_numeric_input_guard(self.ent_shares, allow_decimal=False)

        tk.Label(input_frame, text="單價:", bg="#282835", fg="#ffffff").grid(row=0, column=4, padx=4, pady=2)
        self.ent_price = tk.Entry(input_frame, width=10, font=("Microsoft JhengHei UI", 9), bg="#1e1e28", fg="#ffffff", insertbackground="#ffffff", relief="flat")
        self.ent_price.grid(row=0, column=5, padx=4, pady=2)
        enable_numeric_input_guard(self.ent_price, allow_decimal=True)
        self.ent_price.bind("<Return>", lambda e: self.on_add_lot())
        self.ent_price.bind("<KP_Enter>", lambda e: self.on_add_lot())

        tk.Label(input_frame, text="備註:", bg="#282835", fg="#ffffff").grid(row=0, column=6, padx=4, pady=2)
        self.ent_note = tk.Entry(input_frame, width=12, font=("Microsoft JhengHei UI", 9), bg="#1e1e28", fg="#ffffff", insertbackground="#ffffff", relief="flat")
        self.ent_note.grid(row=0, column=7, padx=4, pady=2)
        self.ent_note.bind("<Return>", lambda e: self.on_add_lot())
        self.ent_note.bind("<KP_Enter>", lambda e: self.on_add_lot())

        self.btn_add_lot = tk.Button(input_frame, text="[+] 新增此批次", bg="#3a86ff", fg="#ffffff", font=("Microsoft JhengHei UI", 9, "bold"), relief="flat", command=self.on_add_lot)
        self.btn_add_lot.grid(row=0, column=8, padx=6, pady=2)

        self.btn_cancel_edit = tk.Button(input_frame, text="[✕ 放棄修改]", bg="#4a4a5a", fg="#ffffff", font=("Microsoft JhengHei UI", 9), relief="flat", command=self.on_cancel_edit)

        # 底部統計資訊與關閉
        bot_bar = tk.Frame(self, bg="#20202a", height=42)
        bot_bar.pack(fill=tk.X, side=tk.BOTTOM)

        self.summary_lbl = tk.Label(bot_bar, text="統計計算中...", bg="#20202a", fg="#ffd166", font=("Microsoft JhengHei UI", 10, "bold"))
        self.summary_lbl.pack(side=tk.LEFT, padx=12, pady=8)

        btn_close = tk.Button(bot_bar, text="關閉完成", bg="#3a3a46", fg="#ffffff", relief="flat", font=("Microsoft JhengHei UI", 9), command=self.destroy)
        btn_close.pack(side=tk.RIGHT, padx=4, pady=8, ipadx=8)

        btn_del = tk.Button(bot_bar, text="[-] 刪除選取批次", bg="#ff4d4f", fg="#ffffff", relief="flat", font=("Microsoft JhengHei UI", 9), command=self.on_delete_selected)
        btn_del.pack(side=tk.RIGHT, padx=6, pady=8, ipadx=8)

        btn_edit = tk.Button(bot_bar, text="[✏ 編輯選取批次]", bg="#d97706", fg="#ffffff", relief="flat", font=("Microsoft JhengHei UI", 9), command=self.on_start_edit_lot)
        btn_edit.pack(side=tk.RIGHT, padx=6, pady=8, ipadx=8)

    def reload_lots(self):
        self.tree.delete(*self.tree.get_children())
        lots = get_trade_lots(self.symbol)
        total_shares = sum(l["shares"] for l in lots)
        total_val = sum(l["shares"] * l["price"] for l in lots)
        avg_price = (total_val / total_shares) if total_shares > 0 else 0.0

        for idx, l in enumerate(lots, start=1):
            self.tree.insert("", tk.END, iid=str(l["id"]), values=(
                idx,
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

    def on_start_edit_lot(self):
        """點擊編輯或雙擊批次行時，載入該批次進行修改"""
        sel = self.tree.selection()
        if not sel:
            messagebox.showinfo("提示", "請先點選欲修改的批次！", parent=self)
            return
        lot_id = int(sel[0])
        lots = get_trade_lots(self.symbol)
        target = next((l for l in lots if l["id"] == lot_id), None)
        if not target:
            return

        self.editing_lot_id = lot_id
        self.ent_date.delete(0, tk.END)
        self.ent_date.insert(0, target["acquire_date"])

        self.ent_shares.delete(0, tk.END)
        sh_val = target["shares"]
        self.ent_shares.insert(0, str(int(sh_val) if sh_val.is_integer() else sh_val))

        self.ent_price.delete(0, tk.END)
        self.ent_price.insert(0, str(target["price"]))

        self.ent_note.delete(0, tk.END)
        self.ent_note.insert(0, target.get("note", ""))

        self.btn_add_lot.configure(text="[💾 儲存修改批次]", bg="#f59e0b", activebackground="#d97706")
        self.btn_cancel_edit.grid(row=0, column=9, padx=4, pady=2)

    def on_cancel_edit(self):
        """取消批次修改模式，重設輸入欄位"""
        self.editing_lot_id = None
        self.btn_add_lot.configure(text="[+] 新增此批次", bg="#3a86ff", activebackground="#2563eb")
        self.btn_cancel_edit.grid_remove()
        self.ent_date.delete(0, tk.END)
        self.ent_date.insert(0, datetime.now().strftime("%Y-%m-%d"))
        self.ent_shares.delete(0, tk.END)
        self.ent_shares.insert(0, "1000")
        self.ent_price.delete(0, tk.END)
        self.ent_note.delete(0, tk.END)

    def on_add_lot(self):
        """新增或儲存修改買入批次"""
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

        if self.editing_lot_id is not None:
            update_trade_lot(self.editing_lot_id, self.symbol, dt_str, shares, price, 0.0, note)
            self.on_cancel_edit()
        else:
            add_trade_lot(self.symbol, dt_str, shares, price, 0.0, note)
            self.ent_price.delete(0, tk.END)
            self.ent_note.delete(0, tk.END)

        self.reload_lots()
        if self.on_lots_changed:
            self.on_lots_changed()

        # 友善體驗：新增/修改成功後自動將游標焦點切回「取得日期」，並反白文字方便下一筆連續輸入
        try:
            self.ent_date.focus_set()
            self.ent_date.selection_range(0, tk.END)
        except Exception:
            pass

    def on_delete_selected(self):
        sel = self.tree.selection()
        if not sel:
            messagebox.showinfo("提示", "請先點選欲刪除的批次！", parent=self)
            return
        lot_id = int(sel[0])
        delete_trade_lot(lot_id, self.symbol)
        if self.editing_lot_id == lot_id:
            self.on_cancel_edit()
        self.reload_lots()
        if self.on_lots_changed:
            self.on_lots_changed()


class DataUpdateDialog(tk.Toplevel):
    """資料更新管理彈窗 (手動更新三個項目與顯示最後更新時間)"""
    def __init__(self, parent_app):
        super().__init__(parent_app)
        self.app = parent_app
        self.app.active_update_dialog = self
        self.title("資料更新管理")
        self.geometry("560x420")
        self.resizable(False, False)
        self.configure(bg="#22222a")
        self.transient(parent_app)
        self.grab_set()

        try:
            icon_ico = get_resource_path("app_icon.ico")
            if os.path.exists(icon_ico):
                self.iconbitmap(icon_ico)
        except Exception:
            pass

        self.build_ui()
        self.refresh_timestamps()

    def build_ui(self):
        # 頂部提示說明
        top_frame = tk.Frame(self, bg="#2a2a36", padx=16, pady=12)
        top_frame.pack(fill=tk.X)

        tk.Label(
            top_frame,
            text="[⟳] 資料更新與同步中心",
            bg="#2a2a36", fg="#ffffff", font=("Microsoft JhengHei UI", 12, "bold")
        ).pack(anchor="w")

        tk.Label(
            top_frame,
            text="自動同步時機：軟體啟動時自動比對 ‧ 每日 15:00 定時同步。\n在此可針對各個項目手動即時刷新：",
            bg="#2a2a36", fg="#a0a0b0", font=("Microsoft JhengHei UI", 9), justify=tk.LEFT
        ).pack(anchor="w", pady=(4, 0))

        # 主體內容卡片區
        content_frame = tk.Frame(self, bg="#22222a", padx=16, pady=12)
        content_frame.pack(fill=tk.BOTH, expand=True)

        # 1. 即時盤中行情卡片
        card1 = tk.Frame(content_frame, bg="#1a1a22", padx=12, pady=10, relief="solid", bd=1)
        card1.pack(fill=tk.X, pady=5)

        self.btn_quote = tk.Button(
            card1, text="[更新即時行情]", bg="#3a86ff", fg="#ffffff",
            font=("Microsoft JhengHei UI", 9, "bold"), width=16, relief="flat",
            command=self.on_refresh_quotes
        )
        self.btn_quote.pack(side=tk.LEFT, padx=(0, 12))

        c1_txt = tk.Frame(card1, bg="#1a1a22")
        c1_txt.pack(side=tk.LEFT, fill=tk.BOTH, expand=True)
        self.lbl_quote_time = tk.Label(c1_txt, text="最後更新時間: 讀取中...", bg="#1a1a22", fg="#ffffff", font=("Microsoft JhengHei UI", 9, "bold"))
        self.lbl_quote_time.pack(anchor="w")
        tk.Label(c1_txt, text="連線證交所 MIS 查詢持股最新成交價、昨收價與漲跌幅", bg="#1a1a22", fg="#808090", font=("Microsoft JhengHei UI", 8)).pack(anchor="w")

        # 2. 除權息與股利卡片
        card2 = tk.Frame(content_frame, bg="#1a1a22", padx=12, pady=10, relief="solid", bd=1)
        card2.pack(fill=tk.X, pady=5)

        self.btn_div = tk.Button(
            card2, text="[同步除權息資訊]", bg="#2b4c7e", fg="#ffffff",
            font=("Microsoft JhengHei UI", 9, "bold"), width=16, relief="flat",
            command=self.on_refresh_dividend
        )
        self.btn_div.pack(side=tk.LEFT, padx=(0, 12))

        c2_txt = tk.Frame(card2, bg="#1a1a22")
        c2_txt.pack(side=tk.LEFT, fill=tk.BOTH, expand=True)
        self.lbl_div_time = tk.Label(c2_txt, text="最後更新時間: 讀取中...", bg="#1a1a22", fg="#ffffff", font=("Microsoft JhengHei UI", 9, "bold"))
        self.lbl_div_time.pack(anchor="w")
        tk.Label(c2_txt, text="查詢官方公告除息日、配息金額、預估發放月與配息頻率", bg="#1a1a22", fg="#808090", font=("Microsoft JhengHei UI", 8)).pack(anchor="w")

        # 3. 盤後歷史日 K 線卡片
        card3 = tk.Frame(content_frame, bg="#1a1a22", padx=12, pady=10, relief="solid", bd=1)
        card3.pack(fill=tk.X, pady=5)

        self.btn_history = tk.Button(
            card3, text="[更新盤後歷史資料]", bg="#2b4c7e", fg="#ffffff",
            font=("Microsoft JhengHei UI", 9, "bold"), width=16, relief="flat",
            command=self.on_refresh_history
        )
        self.btn_history.pack(side=tk.LEFT, padx=(0, 12))

        c3_txt = tk.Frame(card3, bg="#1a1a22")
        c3_txt.pack(side=tk.LEFT, fill=tk.BOTH, expand=True)
        self.lbl_hist_time = tk.Label(c3_txt, text="最後更新時間: 讀取中...", bg="#1a1a22", fg="#ffffff", font=("Microsoft JhengHei UI", 9, "bold"))
        self.lbl_hist_time.pack(anchor="w")
        tk.Label(c3_txt, text="增量補齊每檔股票每日收盤日K線 (供週/月損益與K線圖計算)", bg="#1a1a22", fg="#808090", font=("Microsoft JhengHei UI", 8)).pack(anchor="w")

        # 底部動作列
        bot = tk.Frame(self, bg="#22222a", padx=16, pady=10)
        bot.pack(fill=tk.X, side=tk.BOTTOM)

        tk.Button(bot, text="一鍵全部更新", bg="#52c41a", fg="#ffffff", font=("Microsoft JhengHei UI", 9, "bold"), relief="flat", command=self.on_update_all).pack(side=tk.LEFT, ipadx=10, ipady=3)
        tk.Button(bot, text="關閉", bg="#3a3a46", fg="#ffffff", font=("Microsoft JhengHei UI", 9), relief="flat", command=self.destroy).pack(side=tk.RIGHT, ipadx=12, ipady=3)

    def refresh_timestamps(self):
        """刷新三個項目各自的最後更新時間顯示"""
        q_time = get_update_timestamp("quotes")
        d_time = get_update_timestamp("dividends")
        h_time = get_update_timestamp("history")

        self.lbl_quote_time.configure(text=f"最後更新時間:  {q_time}")
        self.lbl_div_time.configure(text=f"最後更新時間:  {d_time}")
        self.lbl_hist_time.configure(text=f"最後更新時間:  {h_time}")

    def on_refresh_quotes(self):
        self.btn_quote.configure(state=tk.DISABLED, text="行情更新中...")
        self.app.trigger_refresh()
        self.after(1500, lambda: self.btn_quote.configure(state=tk.NORMAL, text="[更新即時行情]"))

    def on_refresh_dividend(self):
        self.btn_div.configure(state=tk.DISABLED, text="股息同步中...")
        self.app.trigger_dividend_update(silent=False)
        self.after(2000, lambda: self.btn_div.configure(state=tk.NORMAL, text="[同步除權息資訊]"))

    def on_refresh_history(self):
        if self.app.is_history_updating:
            self.lbl_hist_time.configure(text="提示: 盤後歷史資料正在背景下載中，請稍候...")
            return
        self.btn_history.configure(state=tk.DISABLED, text="盤後下載中...")
        self.lbl_hist_time.configure(text="最後更新時間:  正在連線證交所下載日K線...")
        self.app.on_update_history_all(silent=False)
        # 防呆保險：若 45 秒後仍未完成，自動解除按鈕禁用
        self.after(45000, self._ensure_history_button_released)

    def on_history_sync_done(self):
        """由背景更新完成/失敗後安全回調"""
        if self.winfo_exists():
            self.btn_history.configure(state=tk.NORMAL, text="[更新盤後歷史資料]")
            self.refresh_timestamps()

    def _ensure_history_button_released(self):
        if self.winfo_exists() and not self.app.is_history_updating:
            self.btn_history.configure(state=tk.NORMAL, text="[更新盤後歷史資料]")
            self.refresh_timestamps()

    def on_update_all(self):
        self.on_refresh_quotes()
        self.on_refresh_dividend()
        self.on_refresh_history()


class SettingsDialog(tk.Toplevel):
    """介面自訂設定對話框 (支援高對比分頁切換、主表格欄位排序、頂部卡片勾選、字型大小顏色與背景設定)"""
    def __init__(self, parent, on_applied=None):
        super().__init__(parent)
        self.title("自訂介面顯示、字型色彩與欄位設定")
        self.geometry("860x650")
        self.resizable(False, False)
        self.configure(bg="#181820")
        self.transient(parent)
        self.grab_set()

        self.on_applied = on_applied
        self.spec_dict = {s[0]: s for s in ALL_COLUMN_SPECS}

        # 讀取目前順序的欄位清單
        saved_cols = get_visible_columns()
        self.active_cols = [cid for cid in saved_cols if cid in self.spec_dict]
        self.hidden_cols = [cid for cid, _, _, _, _ in ALL_COLUMN_SPECS if cid not in self.active_cols]
        self.cur_cards = set(get_visible_cards())

        # 讀取外觀與色彩設定
        self.theme_settings = dict(get_theme_settings())
        self.cur_font_family = tk.StringVar(value=self.theme_settings.get("font_family", "Microsoft JhengHei UI"))
        self.cur_font_size = tk.StringVar(value=str(self.theme_settings.get("font_size", 10)))
        self.cur_row_height = tk.StringVar(value=str(self.theme_settings.get("row_height", 32)))

        self.color_vars = {
            "bg_color": self.theme_settings.get("bg_color", "#1e1e24"),
            "card_bg": self.theme_settings.get("card_bg", "#2b2b36"),
            "table_bg": self.theme_settings.get("table_bg", "#252530"),
            "text_color": self.theme_settings.get("text_color", "#ffffff"),
            "hist_div_color": self.theme_settings.get("hist_div_color", "#38bdf8"),
        }

        # 確保對話框內所有下拉選單均為黑底白字高對比
        self.option_add("*TCombobox*Listbox.background", "#14141c")
        self.option_add("*TCombobox*Listbox.foreground", "#ffffff")
        self.option_add("*TCombobox*Listbox.selectBackground", "#2563eb")
        self.option_add("*TCombobox*Listbox.selectForeground", "#ffffff")
        self.option_add("*TCombobox*Listbox.font", ("Microsoft JhengHei UI", 9))

        self.build_ui()
        self.refresh_lists()
        self.update_preview()

    def build_ui(self):
        # ==========================================
        # 頂部高對比客製化導航分頁列 (保證 100% 清晰可見)
        # ==========================================
        tab_bar = tk.Frame(self, bg="#13131a", height=44)
        tab_bar.pack(fill=tk.X, padx=12, pady=(10, 0))

        self.tab_container = tk.Frame(self, bg="#20202a")
        self.tab_container.pack(fill=tk.BOTH, expand=True, padx=12, pady=(0, 10))

        self.tab_frames = {}
        self.tab_buttons = {}

        tabs = [
            ("cols", "主表格顯示欄位與左右排序"),
            ("cards", "頂部儀表板卡片勾選"),
            ("appearance", "字型大小、顏色與背景設定")
        ]

        for key, title in tabs:
            btn = tk.Button(
                tab_bar, text=f"  {title}  ", bg="#262634", fg="#94a3b8",
                activebackground="#3b82f6", activeforeground="#ffffff",
                relief="flat", font=("Microsoft JhengHei UI", 10),
                cursor="hand2", padx=14, pady=6,
                command=lambda k=key: self.switch_tab(k)
            )
            btn.pack(side=tk.LEFT, padx=(0, 4))
            self.tab_buttons[key] = btn

            frame = tk.Frame(self.tab_container, bg="#20202a")
            self.tab_frames[key] = frame

        # ==========================================
        # 分頁 1: 表格欄位順序與增減設定 (左右順序編輯)
        # ==========================================
        tab_cols = self.tab_frames["cols"]
        lbl_hint = tk.Label(
            tab_cols,
            text="💡 列表順序代表表格中【由左至右】的顯示順序。選取欄位後，可透過中間按鈕調整左右位置、增減或置頂置底：",
            bg="#20202a", fg="#a0a0b0", font=("Microsoft JhengHei UI", 9)
        )
        lbl_hint.pack(anchor="w", padx=12, pady=(10, 6))

        work_frame = tk.Frame(tab_cols, bg="#20202a")
        work_frame.pack(fill=tk.BOTH, expand=True, padx=12, pady=4)

        # 1. 左側：已啟用顯示欄位
        left_box = tk.Frame(work_frame, bg="#20202a")
        left_box.pack(side=tk.LEFT, fill=tk.BOTH, expand=True)

        self.lbl_active_title = tk.Label(left_box, text="[目前顯示欄位 - 由左至右]", bg="#20202a", fg="#ffffff", font=("Microsoft JhengHei UI", 9, "bold"))
        self.lbl_active_title.pack(anchor="w", pady=(0, 4))

        active_scroll_frame = tk.Frame(left_box, bg="#14141b", relief="solid", bd=1)
        active_scroll_frame.pack(fill=tk.BOTH, expand=True)

        self.list_active = tk.Listbox(
            active_scroll_frame, bg="#14141b", fg="#ffffff", selectbackground="#2563eb",
            selectforeground="#ffffff", font=("Microsoft JhengHei UI", 9),
            activestyle="none", highlightthickness=0, bd=0, exportselection=False
        )
        active_sb = tk.Scrollbar(active_scroll_frame, orient="vertical", command=self.list_active.yview)
        self.list_active.configure(yscrollcommand=active_sb.set)
        self.list_active.pack(side=tk.LEFT, fill=tk.BOTH, expand=True, padx=4, pady=4)
        active_sb.pack(side=tk.RIGHT, fill=tk.Y)
        self.list_active.bind("<Double-Button-1>", lambda e: self.remove_from_active())

        # 2. 中間：操作按鈕欄
        mid_box = tk.Frame(work_frame, bg="#20202a", padx=10)
        mid_box.pack(side=tk.LEFT, fill=tk.Y, pady=20)

        btn_w = 14
        tk.Button(mid_box, text="◀ 加入顯示", bg="#323242", fg="#ffffff", font=("Microsoft JhengHei UI", 9), width=btn_w, relief="flat", command=self.add_to_active).pack(pady=3)
        tk.Button(mid_box, text="移除隱藏 ▶", bg="#323242", fg="#ffffff", font=("Microsoft JhengHei UI", 9), width=btn_w, relief="flat", command=self.remove_from_active).pack(pady=3)

        tk.Frame(mid_box, bg="#3a3a46", height=1).pack(fill=tk.X, pady=10)

        tk.Button(mid_box, text="▲ 往左 (上移)", bg="#1d4ed8", fg="#ffffff", font=("Microsoft JhengHei UI", 9, "bold"), width=btn_w, relief="flat", command=self.move_up).pack(pady=3)
        tk.Button(mid_box, text="▼ 往右 (下移)", bg="#1d4ed8", fg="#ffffff", font=("Microsoft JhengHei UI", 9, "bold"), width=btn_w, relief="flat", command=self.move_down).pack(pady=3)
        tk.Button(mid_box, text="[置頂] 最左", bg="#323242", fg="#ffffff", font=("Microsoft JhengHei UI", 9), width=btn_w, relief="flat", command=self.move_top).pack(pady=3)
        tk.Button(mid_box, text="[置底] 最右", bg="#323242", fg="#ffffff", font=("Microsoft JhengHei UI", 9), width=btn_w, relief="flat", command=self.move_bottom).pack(pady=3)

        # 3. 右側：未顯示/隱藏欄位庫
        right_box = tk.Frame(work_frame, bg="#20202a")
        right_box.pack(side=tk.RIGHT, fill=tk.BOTH, expand=True)

        self.lbl_hidden_title = tk.Label(right_box, text="[可選隱藏欄位庫]", bg="#20202a", fg="#a0a0b0", font=("Microsoft JhengHei UI", 9, "bold"))
        self.lbl_hidden_title.pack(anchor="w", pady=(0, 4))

        hidden_scroll_frame = tk.Frame(right_box, bg="#14141b", relief="solid", bd=1)
        hidden_scroll_frame.pack(fill=tk.BOTH, expand=True)

        self.list_hidden = tk.Listbox(
            hidden_scroll_frame, bg="#14141b", fg="#d0d0d8", selectbackground="#2563eb",
            selectforeground="#ffffff", font=("Microsoft JhengHei UI", 9),
            activestyle="none", highlightthickness=0, bd=0, exportselection=False
        )
        hidden_sb = tk.Scrollbar(hidden_scroll_frame, orient="vertical", command=self.list_hidden.yview)
        self.list_hidden.configure(yscrollcommand=hidden_sb.set)
        self.list_hidden.pack(side=tk.LEFT, fill=tk.BOTH, expand=True, padx=4, pady=4)
        hidden_sb.pack(side=tk.RIGHT, fill=tk.Y)
        self.list_hidden.bind("<Double-Button-1>", lambda e: self.add_to_active())

        # 底部快捷按鈕列 (全選 / 重設)
        btn_col_box = tk.Frame(tab_cols, bg="#20202a")
        btn_col_box.pack(fill=tk.X, padx=12, pady=8)
        tk.Button(btn_col_box, text="全部加入顯示", bg="#323242", fg="#ffffff", relief="flat", font=("Microsoft JhengHei UI", 8), command=self.select_all_cols).pack(side=tk.LEFT, padx=3)
        tk.Button(btn_col_box, text="恢復預設順序與欄位", bg="#323242", fg="#ffffff", relief="flat", font=("Microsoft JhengHei UI", 8), command=self.reset_default_cols).pack(side=tk.LEFT, padx=3)
        tk.Label(btn_col_box, text="* 支援雙擊項目快速加入或移除", bg="#20202a", fg="#7a7a8c", font=("Microsoft JhengHei UI", 8)).pack(side=tk.RIGHT)

        # ==========================================
        # 分頁 2: 頂部資訊卡片設定
        # ==========================================
        tab_cards = self.tab_frames["cards"]
        lbl_k = tk.Label(tab_cards, text="勾選您希望在視窗最上方儀表板顯示的概覽卡片：", bg="#20202a", fg="#cbd5e1", font=("Microsoft JhengHei UI", 9, "bold"))
        lbl_k.pack(anchor="w", padx=16, pady=(14, 8))

        cards_container = tk.Frame(tab_cards, bg="#20202a")
        cards_container.pack(fill=tk.BOTH, expand=True, padx=16, pady=4)

        self.card_vars = {}
        for cid, title, text_col, desc in ALL_CARD_SPECS:
            var = tk.BooleanVar(value=(cid in self.cur_cards))
            self.card_vars[cid] = var
            desc_tag = f"[{desc}]"
            if cid == "hist_div":
                desc_tag = f"[{desc} ★ 亮藍色字體，防綠色忌諱]"
            chk = tk.Checkbutton(
                cards_container, text=f"{title} - {desc_tag}",
                variable=var, bg="#20202a", fg="#ffffff", selectcolor="#2b2b3a",
                activebackground="#20202a", activeforeground="#ffffff",
                font=("Microsoft JhengHei UI", 9)
            )
            chk.pack(anchor="w", padx=10, pady=5)

        # ==========================================
        # 分頁 3: 字型大小、顏色與背景設定
        # ==========================================
        tab_app = self.tab_frames["appearance"]

        # 頂部提示
        lbl_app_hint = tk.Label(
            tab_app,
            text="客製化主介面字型大小、主題背景色與數字色彩。台股最忌諱綠色數字，已領股息預設為亮藍色：",
            bg="#20202a", fg="#a0a0b0", font=("Microsoft JhengHei UI", 9)
        )
        lbl_app_hint.pack(anchor="w", padx=14, pady=(10, 6))

        app_work = tk.Frame(tab_app, bg="#20202a")
        app_work.pack(fill=tk.BOTH, expand=True, padx=14, pady=4)

        # 左側欄位：字型大小 + 快速主題
        left_app = tk.Frame(app_work, bg="#20202a", width=360)
        left_app.pack(side=tk.LEFT, fill=tk.BOTH, expand=True, padx=(0, 10))

        # 1. 字型設定群組
        font_grp = tk.LabelFrame(left_app, text=" 【表格字型與排版大小】 ", bg="#20202a", fg="#ffffff", font=("Microsoft JhengHei UI", 9, "bold"), padx=10, pady=8)
        font_grp.pack(fill=tk.X, pady=(0, 10))

        # 字型名稱
        f_row1 = tk.Frame(font_grp, bg="#20202a")
        f_row1.pack(fill=tk.X, pady=3)
        tk.Label(f_row1, text="字型家族:", bg="#20202a", fg="#ffffff", font=("Microsoft JhengHei UI", 9, "bold"), width=10, anchor="w").pack(side=tk.LEFT)
        combo_font = ttk.Combobox(f_row1, textvariable=self.cur_font_family, values=["Microsoft JhengHei UI", "微軟正黑體", "Segoe UI", "Consolas", "Arial"], state="readonly", width=20, font=("Microsoft JhengHei UI", 9, "bold"))
        combo_font.pack(side=tk.LEFT, padx=4)
        combo_font.bind("<<ComboboxSelected>>", lambda e: self.update_preview())

        # 字型大小
        f_row2 = tk.Frame(font_grp, bg="#20202a")
        f_row2.pack(fill=tk.X, pady=3)
        tk.Label(f_row2, text="字型大小:", bg="#20202a", fg="#ffffff", font=("Microsoft JhengHei UI", 9, "bold"), width=10, anchor="w").pack(side=tk.LEFT)
        combo_sz = ttk.Combobox(f_row2, textvariable=self.cur_font_size, values=["9", "10", "11", "12", "13", "14", "16"], state="readonly", width=8, font=("Microsoft JhengHei UI", 9, "bold"))
        combo_sz.pack(side=tk.LEFT, padx=4)
        combo_sz.bind("<<ComboboxSelected>>", lambda e: self.update_preview())
        tk.Label(f_row2, text="pt (預設 10pt)", bg="#20202a", fg="#94a3b8", font=("Microsoft JhengHei UI", 8)).pack(side=tk.LEFT, padx=4)

        # 列高
        f_row3 = tk.Frame(font_grp, bg="#20202a")
        f_row3.pack(fill=tk.X, pady=3)
        tk.Label(f_row3, text="表格單列高:", bg="#20202a", fg="#ffffff", font=("Microsoft JhengHei UI", 9, "bold"), width=10, anchor="w").pack(side=tk.LEFT)
        combo_rh = ttk.Combobox(f_row3, textvariable=self.cur_row_height, values=["26", "28", "30", "32", "36", "40"], state="readonly", width=8, font=("Microsoft JhengHei UI", 9, "bold"))
        combo_rh.pack(side=tk.LEFT, padx=4)
        combo_rh.bind("<<ComboboxSelected>>", lambda e: self.update_preview())
        tk.Label(f_row3, text="px (預設 32px)", bg="#20202a", fg="#94a3b8", font=("Microsoft JhengHei UI", 8)).pack(side=tk.LEFT, padx=4)

        # 2. 快速主題方案群組
        theme_grp = tk.LabelFrame(left_app, text=" 【一鍵套用熱門配色主題】 ", bg="#20202a", fg="#ffffff", font=("Microsoft JhengHei UI", 9, "bold"), padx=10, pady=8)
        theme_grp.pack(fill=tk.X)

        theme_presets = [
            ("科技深灰 (預設)", "#1e1e24", "#2b2b36", "#252530", "#ffffff", "#38bdf8"),
            ("曜石極黑 (AMOLED)", "#101014", "#18181f", "#14141a", "#f8fafc", "#00e5ff"),
            ("沉穩海藍 (Navy)", "#0f172a", "#1e293b", "#1e293b", "#f1f5f9", "#38bdf8"),
            ("高對比炭黑 (高清晰)", "#18181b", "#27272a", "#202024", "#ffffff", "#60a5fa")
        ]

        for name, bg_c, card_c, tbl_c, txt_c, hist_c in theme_presets:
            btn_t = tk.Button(
                theme_grp, text=f"✦ {name}", bg="#2b2b3a", fg="#ffffff",
                relief="flat", font=("Microsoft JhengHei UI", 9), anchor="w",
                padx=8, pady=4, cursor="hand2",
                command=lambda b=bg_c, c=card_c, tb=tbl_c, tx=txt_c, h=hist_c: self.apply_preset_theme(b, c, tb, tx, h)
            )
            btn_t.pack(fill=tk.X, pady=2)

        # 右側欄位：細部色彩選色器 + 即時預覽面板
        right_app = tk.Frame(app_work, bg="#20202a")
        right_app.pack(side=tk.RIGHT, fill=tk.BOTH, expand=True)

        color_grp = tk.LabelFrame(right_app, text=" 【背景與文字色彩自訂 (點選色塊更換)】 ", bg="#20202a", fg="#ffffff", font=("Microsoft JhengHei UI", 9, "bold"), padx=10, pady=8)
        color_grp.pack(fill=tk.X, pady=(0, 10))

        self.color_pick_widgets = {}

        color_items = [
            ("bg_color", "視窗主背景顏色", "主程式四周與容器基底"),
            ("card_bg", "概覽卡片背景色", "頂部 9 大指標卡片底色"),
            ("table_bg", "庫存表格背景色", "持股清單背景底色"),
            ("text_color", "主要文字字體顏色", "持股清單與表頭預設文字"),
            ("hist_div_color", "歷年已領股息顏色", "★ 亮藍色呈現，防綠色忌諱"),
        ]

        for key, name, hint in color_items:
            row = tk.Frame(color_grp, bg="#20202a")
            row.pack(fill=tk.X, pady=3)

            tk.Label(row, text=name, bg="#20202a", fg="#ffffff", font=("Microsoft JhengHei UI", 9), width=16, anchor="w").pack(side=tk.LEFT)

            # 色塊預覽按鈕
            cur_hex = self.color_vars[key]
            lbl_preview = tk.Button(row, bg=cur_hex, width=4, height=1, relief="solid", bd=1, cursor="hand2", command=lambda k=key: self.pick_color(k))
            lbl_preview.pack(side=tk.LEFT, padx=4)

            lbl_hex = tk.Label(row, text=cur_hex, bg="#20202a", fg="#a0a0b0", font=("Consolas", 9), width=8, anchor="w")
            lbl_hex.pack(side=tk.LEFT, padx=2)

            btn_pick = tk.Button(row, text="選擇顏色", bg="#323242", fg="#ffffff", relief="flat", font=("Microsoft JhengHei UI", 8), command=lambda k=key: self.pick_color(k))
            btn_pick.pack(side=tk.LEFT, padx=4)

            tk.Label(row, text=hint, bg="#20202a", fg="#7a7a8c", font=("Microsoft JhengHei UI", 8)).pack(side=tk.LEFT, padx=4)

            self.color_pick_widgets[key] = (lbl_preview, lbl_hex)

        # 3. 即時色彩預覽面板
        prev_grp = tk.LabelFrame(right_app, text=" 【即時樣式效果預覽】 ", bg="#20202a", fg="#ffffff", font=("Microsoft JhengHei UI", 9, "bold"), padx=10, pady=8)
        prev_grp.pack(fill=tk.BOTH, expand=True)

        self.preview_container = tk.Frame(prev_grp, bg=self.color_vars["bg_color"], padx=10, pady=10)
        self.preview_container.pack(fill=tk.BOTH, expand=True)

        # 預覽卡片
        self.prev_card_frame = tk.Frame(self.preview_container, bg=self.color_vars["card_bg"], padx=8, pady=6)
        self.prev_card_frame.pack(fill=tk.X, pady=(0, 6))

        self.prev_card_title = tk.Label(self.prev_card_frame, text="歷年累計已領股息", bg=self.color_vars["card_bg"], fg="#a0a0b0", font=("Microsoft JhengHei UI", 8))
        self.prev_card_title.pack(anchor="w")

        self.prev_card_val = tk.Label(self.prev_card_frame, text="$ 168,500", bg=self.color_vars["card_bg"], fg=self.color_vars["hist_div_color"], font=("Microsoft JhengHei UI", 12, "bold"))
        self.prev_card_val.pack(anchor="w", pady=(2, 0))

        # 預覽表格行
        self.prev_table_frame = tk.Frame(self.preview_container, bg=self.color_vars["table_bg"], padx=8, pady=8)
        self.prev_table_frame.pack(fill=tk.X)

        self.prev_row_text = tk.Label(
            self.prev_table_frame,
            text="2330 台積電   1,000股   $580.00   +5.20%   已領股息: $14,000",
            bg=self.color_vars["table_bg"], fg=self.color_vars["text_color"],
            font=(self.cur_font_family.get(), int(self.cur_font_size.get()))
        )
        self.prev_row_text.pack(anchor="w")

        # 預設開啟第 1 個分頁
        self.switch_tab("cols")

        # ==========================================
        # 底部儲存列
        # ==========================================
        bot = tk.Frame(self, bg="#181820")
        bot.pack(fill=tk.X, padx=12, pady=(0, 12))
        tk.Button(bot, text="儲存並立即套用", bg="#2563eb", fg="#ffffff", font=("Microsoft JhengHei UI", 10, "bold"), relief="flat", cursor="hand2", command=self.save_settings).pack(side=tk.RIGHT, ipadx=12, ipady=3)
        tk.Button(bot, text="取消", bg="#3a3a46", fg="#ffffff", relief="flat", font=("Microsoft JhengHei UI", 9), cursor="hand2", command=self.destroy).pack(side=tk.RIGHT, padx=8, ipadx=8, ipady=3)

    def switch_tab(self, target_key: str):
        """切換分頁，具備極度強烈對比的高亮按鈕與頁面顯示"""
        for key, frame in self.tab_frames.items():
            btn = self.tab_buttons[key]
            if key == target_key:
                frame.pack(fill=tk.BOTH, expand=True)
                btn.configure(
                    bg="#2563eb", fg="#ffffff",
                    font=("Microsoft JhengHei UI", 10, "bold"),
                    relief="solid", bd=0
                )
            else:
                frame.pack_forget()
                btn.configure(
                    bg="#262634", fg="#94a3b8",
                    font=("Microsoft JhengHei UI", 10),
                    relief="flat"
                )

    def pick_color(self, key: str):
        """開啟調色盤選擇顏色並更新色塊"""
        cur = self.color_vars[key]
        chosen = colorchooser.askcolor(color=cur, title=f"選擇顏色 - {key}", parent=self)
        if chosen and chosen[1]:
            new_hex = chosen[1].lower()
            self.color_vars[key] = new_hex
            btn_prev, lbl_hex = self.color_pick_widgets[key]
            btn_prev.configure(bg=new_hex)
            lbl_hex.configure(text=new_hex)
            self.update_preview()

    def apply_preset_theme(self, bg_c: str, card_c: str, tbl_c: str, txt_c: str, hist_c: str):
        """一鍵套用主題預設色系"""
        self.color_vars["bg_color"] = bg_c
        self.color_vars["card_bg"] = card_c
        self.color_vars["table_bg"] = tbl_c
        self.color_vars["text_color"] = txt_c
        self.color_vars["hist_div_color"] = hist_c

        for key, val in self.color_vars.items():
            if key in self.color_pick_widgets:
                btn_prev, lbl_hex = self.color_pick_widgets[key]
                btn_prev.configure(bg=val)
                lbl_hex.configure(text=val)

        self.update_preview()

    def update_preview(self):
        """更新即時預覽方塊的配色與字型"""
        try:
            bg_c = self.color_vars["bg_color"]
            card_c = self.color_vars["card_bg"]
            tbl_c = self.color_vars["table_bg"]
            txt_c = self.color_vars["text_color"]
            hist_c = self.color_vars["hist_div_color"]

            f_fam = self.cur_font_family.get()
            f_sz = int(self.cur_font_size.get())

            self.preview_container.configure(bg=bg_c)
            self.prev_card_frame.configure(bg=card_c)
            self.prev_card_title.configure(bg=card_c)
            self.prev_card_val.configure(bg=card_c, fg=hist_c)

            self.prev_table_frame.configure(bg=tbl_c)
            self.prev_row_text.configure(bg=tbl_c, fg=txt_c, font=(f_fam, f_sz))
        except Exception:
            pass

    def refresh_lists(self):
        """刷新左右兩側清單顯示與序號"""
        # 左側
        self.list_active.delete(0, tk.END)
        for i, cid in enumerate(self.active_cols, 1):
            spec = self.spec_dict.get(cid)
            name = spec[1] if spec else cid
            cat = spec[4] if spec else "其他"
            self.list_active.insert(tk.END, f"{i:02d}. {name:<10} [{cat}]")

        # 右側
        self.list_hidden.delete(0, tk.END)
        for cid in self.hidden_cols:
            spec = self.spec_dict.get(cid)
            name = spec[1] if spec else cid
            cat = spec[4] if spec else "其他"
            self.list_hidden.insert(tk.END, f"  {name:<10} [{cat}]")

        self.lbl_active_title.configure(text=f"[目前顯示欄位 - 由左至右] (共 {len(self.active_cols)} 欄)")
        self.lbl_hidden_title.configure(text=f"[可選隱藏欄位庫] (共 {len(self.hidden_cols)} 欄)")

    def move_up(self):
        """將選定欄位往左 (在清單中上移)"""
        sel = self.list_active.curselection()
        if not sel:
            return
        idx = sel[0]
        if idx > 0:
            item = self.active_cols.pop(idx)
            self.active_cols.insert(idx - 1, item)
            self.refresh_lists()
            self.list_active.selection_set(idx - 1)
            self.list_active.activate(idx - 1)
            self.list_active.see(idx - 1)

    def move_down(self):
        """將選定欄位往右 (在清單中下移)"""
        sel = self.list_active.curselection()
        if not sel:
            return
        idx = sel[0]
        if idx < len(self.active_cols) - 1:
            item = self.active_cols.pop(idx)
            self.active_cols.insert(idx + 1, item)
            self.refresh_lists()
            self.list_active.selection_set(idx + 1)
            self.list_active.activate(idx + 1)
            self.list_active.see(idx + 1)

    def move_top(self):
        """將選定欄位移至最左邊 (第 1 欄)"""
        sel = self.list_active.curselection()
        if not sel or sel[0] == 0:
            return
        idx = sel[0]
        item = self.active_cols.pop(idx)
        self.active_cols.insert(0, item)
        self.refresh_lists()
        self.list_active.selection_set(0)
        self.list_active.activate(0)
        self.list_active.see(0)

    def move_bottom(self):
        """將選定欄位移至最右邊 (最後 1 欄)"""
        sel = self.list_active.curselection()
        if not sel or sel[0] == len(self.active_cols) - 1:
            return
        idx = sel[0]
        item = self.active_cols.pop(idx)
        self.active_cols.append(item)
        self.refresh_lists()
        last_idx = len(self.active_cols) - 1
        self.list_active.selection_set(last_idx)
        self.list_active.activate(last_idx)
        self.list_active.see(last_idx)

    def add_to_active(self):
        """將右側隱藏欄位加入至顯示清單"""
        sel = self.list_hidden.curselection()
        if not sel:
            return
        idx = sel[0]
        cid = self.hidden_cols.pop(idx)

        # 插入在左側選取位置下方，若無選取則插入至最後
        active_sel = self.list_active.curselection()
        insert_pos = (active_sel[0] + 1) if active_sel else len(self.active_cols)
        self.active_cols.insert(insert_pos, cid)

        self.refresh_lists()
        self.list_active.selection_set(insert_pos)
        self.list_active.activate(insert_pos)
        self.list_active.see(insert_pos)

        if self.hidden_cols:
            next_idx = min(idx, len(self.hidden_cols) - 1)
            self.list_hidden.selection_set(next_idx)

    def remove_from_active(self):
        """將左側欄位移至隱藏庫"""
        sel = self.list_active.curselection()
        if not sel:
            return
        if len(self.active_cols) <= 1:
            messagebox.showwarning("提示", "主表格至少必須保留一個顯示欄位！", parent=self)
            return
        idx = sel[0]
        cid = self.active_cols.pop(idx)
        self.hidden_cols.append(cid)

        self.refresh_lists()
        if self.active_cols:
            next_idx = min(idx, len(self.active_cols) - 1)
            self.list_active.selection_set(next_idx)
            self.list_active.activate(next_idx)

    def select_all_cols(self):
        for cid in list(self.hidden_cols):
            self.active_cols.append(cid)
        self.hidden_cols.clear()
        self.refresh_lists()

    def reset_default_cols(self):
        self.active_cols = [cid for cid in DEFAULT_VISIBLE_COLUMNS if cid in self.spec_dict]
        self.hidden_cols = [cid for cid, _, _, _, _ in ALL_COLUMN_SPECS if cid not in self.active_cols]
        self.refresh_lists()

    def save_settings(self):
        if not self.active_cols:
            messagebox.showerror("錯誤", "至少必須保留一個表格顯示欄位！", parent=self)
            return

        selected_cards = [cid for cid, v in self.card_vars.items() if v.get()]

        # 儲存欄位與卡片
        set_visible_columns(self.active_cols)
        set_visible_cards(selected_cards)

        # 儲存外觀字型與色彩設定
        theme_payload = {
            "font_family": self.cur_font_family.get(),
            "font_size": int(self.cur_font_size.get()),
            "row_height": int(self.cur_row_height.get()),
            "bg_color": self.color_vars["bg_color"],
            "card_bg": self.color_vars["card_bg"],
            "table_bg": self.color_vars["table_bg"],
            "text_color": self.color_vars["text_color"],
            "hist_div_color": self.color_vars["hist_div_color"],
        }
        set_theme_settings(theme_payload)

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
        saved_pass = get_setting("cloud_password", "")
        self.ent_pass.insert(0, saved_pass)
        self.ent_pass.pack(fill=tk.X, ipady=3)

        self.var_remember = tk.BooleanVar(value=True if saved_pass else True)
        self.chk_remember = tk.Checkbutton(form, text="記住在本機電腦 (下次同步自動帶入帳號密碼)", variable=self.var_remember, bg="#202028", fg="#d0d0dc", selectcolor="#2d2d38", activebackground="#202028", activeforeground="#ffffff", font=("Microsoft JhengHei UI", 8))
        self.chk_remember.pack(anchor="w", pady=(4, 0))

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

        # 儲存連線設定與帳號密碼 (依記住核取方塊決定)
        set_setting("cloud_user_id", user)
        set_setting("cloud_firebase_url", url)
        if self.var_remember.get():
            set_setting("cloud_password", pwd)
        else:
            set_setting("cloud_password", "")

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
