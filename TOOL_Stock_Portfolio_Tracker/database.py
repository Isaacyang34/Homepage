import sqlite3
import os
import sys
import json
from datetime import datetime
from typing import List, Dict, Any, Optional

if getattr(sys, 'frozen', False):
    BASE_DIR = os.path.dirname(os.path.abspath(sys.executable))
else:
    BASE_DIR = os.path.dirname(os.path.abspath(__file__))

DB_FILE = os.path.join(BASE_DIR, "portfolio.db")

def get_connection():
    conn = sqlite3.connect(DB_FILE)
    conn.row_factory = sqlite3.Row
    return conn

def init_db():
    """初始化資料庫與資料表"""
    with get_connection() as conn:
        cursor = conn.cursor()
        
        # 1. 庫存持股總匯表
        cursor.execute("""
            CREATE TABLE IF NOT EXISTS portfolio (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                symbol TEXT NOT NULL,
                name TEXT DEFAULT '',
                market TEXT DEFAULT 'TW',       -- TW (上市), TWO (上櫃), US (美股)
                shares REAL NOT NULL,           -- 持有總股數 (支援零股與小數)
                cost_price REAL NOT NULL,       -- 加權平均成本均價
                fee_discount REAL DEFAULT 0.6,  -- 券商手續費折讓
                is_etf INTEGER DEFAULT 0,       -- 0: 一般股票(證交稅0.3%), 1: ETF(證交稅0.1%)
                note TEXT DEFAULT '',
                created_at TEXT DEFAULT CURRENT_TIMESTAMP
            )
        """)

        # 2. 多筆買入取得批次明細表 (支援各筆不同取得時間與價格)
        cursor.execute("""
            CREATE TABLE IF NOT EXISTS trade_lots (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                symbol TEXT NOT NULL,
                acquire_date TEXT NOT NULL,     -- 取得時間 YYYY-MM-DD
                shares REAL NOT NULL,           -- 該批次買入股數
                price REAL NOT NULL,            -- 該批次買入單價
                fee REAL DEFAULT 0.0,           -- 買入手續費
                note TEXT DEFAULT '',
                created_at TEXT DEFAULT CURRENT_TIMESTAMP
            )
        """)

        # 3. 歷史 K 線日資料表 (供日/週/月損益計算與走勢圖使用)
        cursor.execute("""
            CREATE TABLE IF NOT EXISTS history_kline (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                symbol TEXT NOT NULL,
                date TEXT NOT NULL,             -- YYYY-MM-DD
                open REAL,
                high REAL,
                low REAL,
                close REAL,
                volume INTEGER,
                updated_at TEXT DEFAULT CURRENT_TIMESTAMP,
                UNIQUE(symbol, date)
            )
        """)

        # 4. 股息資訊表 (記錄公布日期、單期/年股息、發放月份與頻率)
        cursor.execute("""
            CREATE TABLE IF NOT EXISTS dividend_records (
                symbol TEXT PRIMARY KEY,
                cash_dividend REAL DEFAULT 0.0,     -- 全年預估每股股息
                single_dividend REAL DEFAULT 0.0,   -- 最新單期每股股息
                ex_date TEXT DEFAULT '',            -- 最新除息交易日
                payment_month TEXT DEFAULT '',      -- 預估發放月份 (例 9月)
                frequency TEXT DEFAULT '',          -- 配息頻率 (季配/半年配/年配/月配)
                status TEXT DEFAULT '',             -- 已公布/留存前期數據
                dividend_year TEXT DEFAULT '',
                is_announced INTEGER DEFAULT 0,
                updated_at TEXT DEFAULT CURRENT_TIMESTAMP
            )
        """)

        # 自動升級既有 dividend_records 欄位
        for col_def in [
            ("single_dividend", "REAL DEFAULT 0.0"),
            ("payment_month", "TEXT DEFAULT ''"),
            ("frequency", "TEXT DEFAULT ''")
        ]:
            try:
                cursor.execute(f"ALTER TABLE dividend_records ADD COLUMN {col_def[0]} {col_def[1]}")
            except sqlite3.OperationalError:
                pass

        # 5. 系統設定 (刷新頻率、顯示欄位、卡片配置等)
        cursor.execute("""
            CREATE TABLE IF NOT EXISTS app_settings (
                key TEXT PRIMARY KEY,
                value TEXT
            )
        """)

        cursor.execute("INSERT OR IGNORE INTO app_settings (key, value) VALUES ('refresh_interval', '10')")
        cursor.execute("INSERT OR IGNORE INTO app_settings (key, value) VALUES ('color_mode', 'tw')")
        conn.commit()

# --- 庫存總表操作 ---

def get_all_positions() -> List[Dict[str, Any]]:
    """取得所有庫存持股"""
    with get_connection() as conn:
        cursor = conn.cursor()
        cursor.execute("SELECT * FROM portfolio ORDER BY symbol ASC")
        return [dict(row) for row in cursor.fetchall()]

def add_position(symbol: str, name: str, market: str, shares: float, cost_price: float, 
                 fee_discount: float = 0.6, is_etf: int = 0, note: str = "") -> int:
    """新增庫存紀錄"""
    symbol = symbol.strip().upper()
    with get_connection() as conn:
        cursor = conn.cursor()
        cursor.execute("""
            INSERT INTO portfolio (symbol, name, market, shares, cost_price, fee_discount, is_etf, note)
            VALUES (?, ?, ?, ?, ?, ?, ?, ?)
        """, (symbol, name.strip(), market.strip(), shares, cost_price, fee_discount, is_etf, note.strip()))
        conn.commit()
        return cursor.lastrowid

def update_position(position_id: int, symbol: str, name: str, market: str, shares: float, 
                    cost_price: float, fee_discount: float, is_etf: int, note: str):
    """更新庫存紀錄"""
    with get_connection() as conn:
        cursor = conn.cursor()
        cursor.execute("""
            UPDATE portfolio
            SET symbol = ?, name = ?, market = ?, shares = ?, cost_price = ?, 
                fee_discount = ?, is_etf = ?, note = ?
            WHERE id = ?
        """, (symbol.strip().upper(), name.strip(), market.strip(), shares, cost_price, fee_discount, is_etf, note.strip(), position_id))
        conn.commit()

def delete_position(position_id: int):
    """刪除庫存紀錄及其關聯批次"""
    with get_connection() as conn:
        cursor = conn.cursor()
        cursor.execute("SELECT symbol FROM portfolio WHERE id = ?", (position_id,))
        row = cursor.fetchone()
        if row:
            sym = row["symbol"]
            cursor.execute("DELETE FROM trade_lots WHERE symbol = ?", (sym,))
        cursor.execute("DELETE FROM portfolio WHERE id = ?", (position_id,))
        conn.commit()

# --- 取得時間與多筆買入批次明細操作 ---

def get_trade_lots(symbol: str) -> List[Dict[str, Any]]:
    """取得指定標的之所有買入批次明細 (依取得時間由早到晚排序)"""
    with get_connection() as conn:
        cursor = conn.cursor()
        cursor.execute("""
            SELECT * FROM trade_lots 
            WHERE symbol = ? 
            ORDER BY acquire_date ASC, id ASC
        """, (symbol.strip().upper(),))
        return [dict(r) for r in cursor.fetchall()]

def add_trade_lot(symbol: str, acquire_date: str, shares: float, price: float, fee: float = 0.0, note: str = "") -> int:
    """新增單筆買入批次"""
    sym = symbol.strip().upper()
    with get_connection() as conn:
        cursor = conn.cursor()
        cursor.execute("""
            INSERT INTO trade_lots (symbol, acquire_date, shares, price, fee, note)
            VALUES (?, ?, ?, ?, ?, ?)
        """, (sym, acquire_date.strip(), shares, price, fee, note.strip()))
        conn.commit()
        last_id = cursor.lastrowid
        
    # 自動同步匯總回 portfolio
    sync_portfolio_from_lots(sym)
    return last_id

def delete_trade_lot(lot_id: int, symbol: str):
    """刪除特定批次"""
    sym = symbol.strip().upper()
    with get_connection() as conn:
        cursor = conn.cursor()
        cursor.execute("DELETE FROM trade_lots WHERE id = ?", (lot_id,))
        conn.commit()
    sync_portfolio_from_lots(sym)

def sync_portfolio_from_lots(symbol: str):
    """若該標的有多筆批次，自動計算總股數與加權成本更新至 portfolio 表"""
    sym = symbol.strip().upper()
    lots = get_trade_lots(sym)
    if not lots:
        return

    total_shares = sum(l["shares"] for l in lots)
    if total_shares <= 0:
        return

    total_val = sum(l["shares"] * l["price"] for l in lots)
    avg_cost = round(total_val / total_shares, 2)

    with get_connection() as conn:
        cursor = conn.cursor()
        cursor.execute("""
            UPDATE portfolio 
            SET shares = ?, cost_price = ?
            WHERE symbol = ?
        """, (total_shares, avg_cost, sym))
        conn.commit()

# --- 日、週、月歷史價格基準 (供損益週期統計使用) ---

def get_price_benchmarks(symbol: str, current_price: float) -> Dict[str, float]:
    """
    從本地 history_kline 取得:
    - yesterday_close: 昨日收盤
    - week_close: 5 個交易日前 (上週末) 收盤
    - month_close: 20 個交易日前 (上月末) 收盤
    """
    sym = symbol.strip().upper()
    with get_connection() as conn:
        cursor = conn.cursor()
        cursor.execute("""
            SELECT close FROM history_kline 
            WHERE symbol = ? 
            ORDER BY date DESC 
            LIMIT 25
        """, (sym,))
        rows = [r["close"] for r in cursor.fetchall()]

    y_close = rows[0] if len(rows) >= 1 else 0.0
    w_close = rows[4] if len(rows) >= 5 else (rows[-1] if len(rows) > 0 else 0.0)
    m_close = rows[19] if len(rows) >= 20 else (rows[-1] if len(rows) > 0 else 0.0)

    return {
        "yesterday_close": y_close,
        "week_close": w_close,
        "month_close": m_close
    }

# --- 歷史資料庫存取 ---

def get_latest_history_date(symbol: str) -> Optional[str]:
    with get_connection() as conn:
        cursor = conn.cursor()
        cursor.execute("SELECT MAX(date) as max_date FROM history_kline WHERE symbol = ?", (symbol.upper(),))
        row = cursor.fetchone()
        return row["max_date"] if row and row["max_date"] else None

def save_kline_batch(symbol: str, records: List[Dict[str, Any]]) -> int:
    if not records:
        return 0
    sym = symbol.upper()
    now_str = datetime.now().strftime("%Y-%m-%d %H:%M:%S")
    with get_connection() as conn:
        cursor = conn.cursor()
        cursor.executemany("""
            INSERT INTO history_kline (symbol, date, open, high, low, close, volume, updated_at)
            VALUES (?, ?, ?, ?, ?, ?, ?, ?)
            ON CONFLICT(symbol, date) DO UPDATE SET
                open=excluded.open,
                high=excluded.high,
                low=excluded.low,
                close=excluded.close,
                volume=excluded.volume,
                updated_at=excluded.updated_at
        """, [(sym, r['date'], r['open'], r['high'], r['low'], r['close'], r['volume'], now_str) for r in records])
        conn.commit()
        return len(records)

def get_history_kline(symbol: str, limit: int = 60) -> List[Dict[str, Any]]:
    with get_connection() as conn:
        cursor = conn.cursor()
        cursor.execute("""
            SELECT date, open, high, low, close, volume
            FROM history_kline
            WHERE symbol = ?
            ORDER BY date DESC
            LIMIT ?
        """, (symbol.upper(), limit))
        rows = cursor.fetchall()
        return [dict(r) for r in reversed(rows)]

def get_last_trading_day_quote(symbol: str) -> Optional[Dict[str, Any]]:
    """
    取得該股票本地歷史日K中「最後一個交易日」的完整交易狀況
    (用於非交易日或盤前，保留最後一天的收盤價、昨收價、漲跌金額與幅度)
    """
    records = get_history_kline(symbol, limit=2)
    if not records:
        return None
    latest = records[-1]
    if len(records) >= 2:
        prev = records[-2]
        prev_close = float(prev.get("close", 0.0))
    else:
        open_val = float(latest.get("open", 0.0))
        prev_close = open_val if open_val > 0 else float(latest.get("close", 0.0))

    curr_close = float(latest.get("close", 0.0))
    change = curr_close - prev_close if prev_close > 0 else 0.0
    change_pct = (change / prev_close * 100) if prev_close > 0 else 0.0

    return {
        "date": latest.get("date", ""),
        "current_price": round(curr_close, 2),
        "yesterday_close": round(prev_close, 2),
        "open": round(float(latest.get("open", curr_close)), 2),
        "high": round(float(latest.get("high", curr_close)), 2),
        "low": round(float(latest.get("low", curr_close)), 2),
        "volume": int(latest.get("volume", 0)),
        "change": round(change, 2),
        "change_pct": round(change_pct, 2)
    }

# --- 設定操作與介面欄位配置持久化 ---

DEFAULT_VISIBLE_COLUMNS = [
    "symbol", "name", "market", "shares", "cost_price", "current_price",
    "change_pct", "total_cost", "market_val", "unrealized_pnl", "roi_pct",
    "day_pnl", "week_pnl", "month_pnl",
    "frequency", "cash_dividend", "total_dividend", "yield_on_cost",
    "ex_date", "payment_month", "hist_div_received", "note"
]

def get_visible_columns() -> List[str]:
    raw = get_setting("visible_columns", "")
    if not raw:
        return list(DEFAULT_VISIBLE_COLUMNS)
    try:
        return json.loads(raw)
    except Exception:
        return list(DEFAULT_VISIBLE_COLUMNS)

def set_visible_columns(cols: List[str]):
    set_setting("visible_columns", json.dumps(cols))

DEFAULT_VISIBLE_CARDS = ["total_cost", "market_val", "total_pnl", "day_pnl", "week_pnl", "month_pnl", "total_div", "hist_div", "stock_count"]

def get_visible_cards() -> List[str]:
    raw = get_setting("visible_cards", "")
    if not raw:
        return list(DEFAULT_VISIBLE_CARDS)
    try:
        return json.loads(raw)
    except Exception:
        return list(DEFAULT_VISIBLE_CARDS)

def set_visible_cards(cards: List[str]):
    set_setting("visible_cards", json.dumps(cards))

def get_setting(key: str, default: str = "") -> str:
    with get_connection() as conn:
        cursor = conn.cursor()
        cursor.execute("SELECT value FROM app_settings WHERE key = ?", (key,))
        row = cursor.fetchone()
        return row["value"] if row else default

def set_setting(key: str, value: str):
    with get_connection() as conn:
        cursor = conn.cursor()
        cursor.execute("""
            INSERT INTO app_settings (key, value) VALUES (?, ?)
            ON CONFLICT(key) DO UPDATE SET value=excluded.value
        """, (key, value))
        conn.commit()
