import sqlite3
import os
import sys
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
        
        # 1. 庫存持股表
        cursor.execute("""
            CREATE TABLE IF NOT EXISTS portfolio (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                symbol TEXT NOT NULL,
                name TEXT DEFAULT '',
                market TEXT DEFAULT 'TW',       -- TW (上市), TWO (上櫃), US (美股)
                shares INTEGER NOT NULL,        -- 持有股數
                cost_price REAL NOT NULL,       -- 買入成本均價
                fee_discount REAL DEFAULT 0.6,  -- 券商手續費折讓 (例 0.6 代表 6 折)
                is_etf INTEGER DEFAULT 0,       -- 0: 一般股票(證交稅0.3%), 1: ETF(證交稅0.1%)
                note TEXT DEFAULT '',
                created_at TEXT DEFAULT CURRENT_TIMESTAMP
            )
        """)

        # 2. 歷史 K 線日資料表 (避免重複下載)
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

        # 3. 股息資訊表 (記錄公布日期與留存去年數據)
        cursor.execute("""
            CREATE TABLE IF NOT EXISTS dividend_records (
                symbol TEXT PRIMARY KEY,
                cash_dividend REAL DEFAULT 0.0,
                single_dividend REAL DEFAULT 0.0,
                ex_date TEXT DEFAULT '',
                status TEXT DEFAULT '',
                dividend_year TEXT DEFAULT '',
                is_announced INTEGER DEFAULT 0,
                updated_at TEXT DEFAULT CURRENT_TIMESTAMP
            )
        """)

        # 自動升級既有資料表欄位
        try:
            cursor.execute("ALTER TABLE dividend_records ADD COLUMN single_dividend REAL DEFAULT 0.0")
        except sqlite3.OperationalError:
            pass

        # 4. 系統設定 (例如自動刷新頻率)
        cursor.execute("""
            CREATE TABLE IF NOT EXISTS app_settings (
                key TEXT PRIMARY KEY,
                value TEXT
            )
        """)

        # 預設設定
        cursor.execute("INSERT OR IGNORE INTO app_settings (key, value) VALUES ('refresh_interval', '10')")
        cursor.execute("INSERT OR IGNORE INTO app_settings (key, value) VALUES ('color_mode', 'tw')") # tw: 紅漲綠跌, us: 綠漲紅跌
        conn.commit()

# --- 庫存操作函式 ---

def get_all_positions() -> List[Dict[str, Any]]:
    """取得所有庫存持股"""
    with get_connection() as conn:
        cursor = conn.cursor()
        cursor.execute("SELECT * FROM portfolio ORDER BY symbol ASC")
        return [dict(row) for row in cursor.fetchall()]

def add_position(symbol: str, name: str, market: str, shares: int, cost_price: float, 
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

def update_position(position_id: int, symbol: str, name: str, market: str, shares: int, 
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
    """刪除庫存紀錄"""
    with get_connection() as conn:
        cursor = conn.cursor()
        cursor.execute("DELETE FROM portfolio WHERE id = ?", (position_id,))
        conn.commit()

# --- 歷史資料操作函式 ---

def get_latest_history_date(symbol: str) -> Optional[str]:
    """取得特定標的在資料庫中最晚的歷史 K 線日期 (YYYY-MM-DD)"""
    with get_connection() as conn:
        cursor = conn.cursor()
        cursor.execute("SELECT MAX(date) as max_date FROM history_kline WHERE symbol = ?", (symbol.upper(),))
        row = cursor.fetchone()
        return row["max_date"] if row and row["max_date"] else None

def save_kline_batch(symbol: str, records: List[Dict[str, Any]]) -> int:
    """批次儲存 K 線資料 (UPSERT)，回傳新增/更新筆數"""
    if not records:
        return 0
    symbol = symbol.upper()
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
        """, [(symbol, r['date'], r['open'], r['high'], r['low'], r['close'], r['volume'], now_str) for r in records])
        conn.commit()
        return len(records)

def get_history_kline(symbol: str, limit: int = 60) -> List[Dict[str, Any]]:
    """讀取本地歷史 K 線"""
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
        # 反轉為時間正序
        return [dict(r) for r in reversed(rows)]

# --- 設定操作 ---

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
