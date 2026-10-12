# -*- coding: utf-8 -*-
"""
Stock Portfolio Tracker - 本地 SQLite 資料庫存取層 (database.py)
支援功能：
1. 多使用者獨立帳號管理 (users 資料表、密碼 SHA-256+Salt 雜湊防護、免密碼無縫登入)
2. 庫存持股 (portfolio) 與買入批次 (trade_lots) 之 user_id 物理隔離
3. 歷史日K (history_kline) 與除權息 (dividend_records) 全局共享，節省流量與空間
4. 現有資料 100% 自動無痛遷移至預設帳號 (user_id=1)
"""

import sqlite3
import os
import sys
import json
import hashlib
import secrets
from datetime import datetime
from typing import List, Dict, Any, Optional, Tuple

if getattr(sys, 'frozen', False):
    BASE_DIR = os.path.dirname(os.path.abspath(sys.executable))
else:
    BASE_DIR = os.path.dirname(os.path.abspath(__file__))
    if BASE_DIR.lower().endswith(".pkg") or BASE_DIR.lower().endswith(".zip") or os.path.basename(BASE_DIR) == "modules":
        BASE_DIR = os.path.dirname(BASE_DIR)

DB_FILE = os.path.join(BASE_DIR, "portfolio.db")

# 當前運行期登入使用者狀態 (全域單例)
_CURRENT_USER = {
    "id": 1,
    "username": "default",
    "display_name": "主要帳戶"
}

def get_current_user() -> Dict[str, Any]:
    """取得當前已登入之使用者資訊"""
    global _CURRENT_USER
    return dict(_CURRENT_USER)

def set_current_user(user_info: Dict[str, Any]):
    """設定當前已登入之使用者資訊"""
    global _CURRENT_USER
    _CURRENT_USER = {
        "id": int(user_info.get("id", 1)),
        "username": str(user_info.get("username", "default")),
        "display_name": str(user_info.get("display_name", user_info.get("username", "主要帳戶")))
    }

def hash_password(password: str, salt: Optional[str] = None) -> Tuple[str, str]:
    """密碼安全雜湊 (SHA-256 + 16 bytes 隨機 Salt)；空密碼直接回傳空字串"""
    if not password:
        return "", ""
    if not salt:
        salt = secrets.token_hex(16)
    hashed = hashlib.sha256((salt + password).encode("utf-8")).hexdigest()
    return hashed, salt

def verify_password(password: str, salt: str, password_hash: str) -> bool:
    """驗證密碼是否正確"""
    if not password_hash:
        return True  # 未設定密碼之帳戶，直接放行
    if not password:
        return False
    calc_hash, _ = hash_password(password, salt)
    return secrets.compare_digest(calc_hash, password_hash)

def get_connection():
    conn = sqlite3.connect(DB_FILE)
    conn.row_factory = sqlite3.Row
    return conn

def init_db():
    """初始化資料庫、建立資料表與自動無痛遷移"""
    with get_connection() as conn:
        cursor = conn.cursor()
        
        # 0. 使用者帳號表 (支援多人多帳戶隔離與密碼驗證)
        cursor.execute("""
            CREATE TABLE IF NOT EXISTS users (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                username TEXT UNIQUE NOT NULL,
                display_name TEXT DEFAULT '',
                password_hash TEXT DEFAULT '',
                salt TEXT DEFAULT '',
                created_at TEXT DEFAULT CURRENT_TIMESTAMP
            )
        """)

        # 確保至少存在第 1 位預設使用者
        cursor.execute("SELECT COUNT(*) as cnt FROM users")
        if cursor.fetchone()["cnt"] == 0:
            cursor.execute("""
                INSERT INTO users (id, username, display_name, password_hash, salt)
                VALUES (1, 'default', '主要帳戶', '', '')
            """)

        # 1. 庫存持股總匯表
        cursor.execute("""
            CREATE TABLE IF NOT EXISTS portfolio (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                user_id INTEGER DEFAULT 1,
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

        # 2. 多筆買入取得批次明細表
        cursor.execute("""
            CREATE TABLE IF NOT EXISTS trade_lots (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                user_id INTEGER DEFAULT 1,
                symbol TEXT NOT NULL,
                acquire_date TEXT NOT NULL,     -- 取得時間 YYYY-MM-DD
                shares REAL NOT NULL,           -- 該批次買入股數
                price REAL NOT NULL,            -- 該批次買入單價
                fee REAL DEFAULT 0.0,           -- 買入手續費
                note TEXT DEFAULT '',
                created_at TEXT DEFAULT CURRENT_TIMESTAMP
            )
        """)

        # 自動無痛遷移：若現有 portfolio / trade_lots 缺少 user_id 欄位，自動加入
        cursor.execute("PRAGMA table_info(portfolio)")
        p_cols = [r["name"] for r in cursor.fetchall()]
        if "user_id" not in p_cols:
            cursor.execute("ALTER TABLE portfolio ADD COLUMN user_id INTEGER DEFAULT 1")

        cursor.execute("PRAGMA table_info(trade_lots)")
        t_cols = [r["name"] for r in cursor.fetchall()]
        if "user_id" not in t_cols:
            cursor.execute("ALTER TABLE trade_lots ADD COLUMN user_id INTEGER DEFAULT 1")

        # 3. 歷史 K 線日資料表 (全域共用)
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

        # 4. 股息資訊表 (全域共用)
        cursor.execute("""
            CREATE TABLE IF NOT EXISTS dividend_records (
                symbol TEXT PRIMARY KEY,
                cash_dividend REAL DEFAULT 0.0,
                single_dividend REAL DEFAULT 0.0,
                ex_date TEXT DEFAULT '',
                payment_month TEXT DEFAULT '',
                frequency TEXT DEFAULT '',
                status TEXT DEFAULT '',
                dividend_year TEXT DEFAULT '',
                is_announced INTEGER DEFAULT 0,
                raw_events TEXT DEFAULT '',
                updated_at TEXT DEFAULT CURRENT_TIMESTAMP
            )
        """)

        for col_def in [
            ("single_dividend", "REAL DEFAULT 0.0"),
            ("payment_month", "TEXT DEFAULT ''"),
            ("frequency", "TEXT DEFAULT ''"),
            ("raw_events", "TEXT DEFAULT ''")
        ]:
            try:
                cursor.execute(f"ALTER TABLE dividend_records ADD COLUMN {col_def[0]} {col_def[1]}")
            except sqlite3.OperationalError:
                pass

        # 5. 系統設定
        cursor.execute("""
            CREATE TABLE IF NOT EXISTS app_settings (
                key TEXT PRIMARY KEY,
                value TEXT
            )
        """)

        cursor.execute("INSERT OR IGNORE INTO app_settings (key, value) VALUES ('refresh_interval', '10')")
        cursor.execute("INSERT OR IGNORE INTO app_settings (key, value) VALUES ('color_mode', 'tw')")
        conn.commit()

        # 自動恢復上次登入使用者為運行期當前使用者單例
        try:
            cursor.execute("SELECT value FROM app_settings WHERE key = 'last_user_id'")
            row = cursor.fetchone()
            if row and row["value"]:
                last_uid = int(row["value"])
                cursor.execute("SELECT id, username, display_name FROM users WHERE id = ?", (last_uid,))
                u_row = cursor.fetchone()
                if u_row:
                    set_current_user(dict(u_row))
        except Exception:
            pass


# =========================================================
# 多使用者帳號管理 API (User Management)
# =========================================================

def get_all_users() -> List[Dict[str, Any]]:
    """取得所有使用者列表"""
    with get_connection() as conn:
        cursor = conn.cursor()
        cursor.execute("SELECT id, username, display_name, (password_hash != '') as has_password, created_at FROM users ORDER BY id ASC")
        return [dict(r) for r in cursor.fetchall()]

def get_user_by_id(user_id: int) -> Optional[Dict[str, Any]]:
    """依 ID 取得使用者資訊"""
    with get_connection() as conn:
        cursor = conn.cursor()
        cursor.execute("SELECT id, username, display_name, password_hash, salt, created_at FROM users WHERE id = ?", (user_id,))
        row = cursor.fetchone()
        return dict(row) if row else None

def get_user_by_username(username: str) -> Optional[Dict[str, Any]]:
    """依帳號名稱取得使用者資訊"""
    with get_connection() as conn:
        cursor = conn.cursor()
        cursor.execute("SELECT id, username, display_name, password_hash, salt, created_at FROM users WHERE LOWER(username) = LOWER(?)", (username.strip(),))
        row = cursor.fetchone()
        return dict(row) if row else None

def create_user(username: str, display_name: str = "", password: str = "") -> Tuple[bool, str, Optional[int]]:
    """建立新使用者帳號"""
    clean_username = username.strip()
    if not clean_username:
        return False, "使用者帳號不得為空！", None
    
    clean_display = display_name.strip() or clean_username
    pw_hash, salt = hash_password(password) if password else ("", "")

    try:
        with get_connection() as conn:
            cursor = conn.cursor()
            cursor.execute("""
                INSERT INTO users (username, display_name, password_hash, salt)
                VALUES (?, ?, ?, ?)
            """, (clean_username, clean_display, pw_hash, salt))
            conn.commit()
            new_id = cursor.lastrowid
            return True, "使用者建立成功！", new_id
    except sqlite3.IntegrityError:
        return False, f"帳號「{clean_username}」已存在，請使用其他名稱！", None
    except Exception as e:
        return False, f"建立失敗: {e}", None

def verify_user_credentials(identifier: Any, password: str = "") -> Tuple[bool, Optional[Dict[str, Any]], str]:
    """驗證特定使用者之登入密碼，支援傳入 user_id (int) 或 username (str)，回傳 (成功與否, 使用者物件, 提示訊息)"""
    if isinstance(identifier, int) or (isinstance(identifier, str) and identifier.isdigit()):
        user = get_user_by_id(int(identifier))
    else:
        user = get_user_by_username(str(identifier))
    if not user:
        return False, None, "找不到該使用者帳號！"
    pw_hash = user.get("password_hash", "")
    salt = user.get("salt", "")
    if not pw_hash:
        return True, user, "驗證成功 (免密碼帳戶)"
    if not password:
        return False, None, "此帳戶已設置安全密碼，請輸入密碼！"
    if verify_password(password, salt, pw_hash):
        return True, user, "密碼驗證成功！"
    return False, None, "密碼錯誤，請重新確認！"

def update_user_password(user_id: int, new_password: str) -> Tuple[bool, str]:
    """更新使用者密碼 (傳入空字串則解除密碼保護)"""
    pw_hash, salt = hash_password(new_password) if new_password else ("", "")
    try:
        with get_connection() as conn:
            cursor = conn.cursor()
            cursor.execute("UPDATE users SET password_hash = ?, salt = ? WHERE id = ?", (pw_hash, salt, user_id))
            conn.commit()
            return True, "密碼更新成功！"
    except Exception as e:
        return False, f"更新密碼失敗: {e}"

def update_user_display_name(user_id: int, display_name: str) -> bool:
    """更新使用者顯示暱稱"""
    clean_name = display_name.strip()
    if not clean_name:
        return False
    try:
        with get_connection() as conn:
            cursor = conn.cursor()
            cursor.execute("UPDATE users SET display_name = ? WHERE id = ?", (clean_name, user_id))
            conn.commit()
            if _CURRENT_USER.get("id") == user_id:
                _CURRENT_USER["display_name"] = clean_name
            return True
    except Exception:
        return False

def update_username(user_id: int, new_username: str) -> Tuple[bool, str]:
    """更新使用者登入帳號名稱 (包含防呆與唯一性檢查)"""
    clean_u = new_username.strip()
    if not clean_u:
        return False, "帳號名稱不可為空！"
        
    try:
        with get_connection() as conn:
            cursor = conn.cursor()
            # 檢查是否被其他帳號佔用
            cursor.execute("SELECT id FROM users WHERE username = ? AND id != ?", (clean_u, user_id))
            if cursor.fetchone():
                return False, f"帳號名稱「{clean_u}」已被其他使用者使用，請使用其他名稱！"
                
            cursor.execute("UPDATE users SET username = ? WHERE id = ?", (clean_u, user_id))
            conn.commit()
            
            if _CURRENT_USER.get("id") == user_id:
                _CURRENT_USER["username"] = clean_u
            return True, "帳號名稱更新成功！"
    except Exception as e:
        return False, f"更新帳號名稱失敗: {e}"

def delete_user(user_id: int) -> Tuple[bool, str]:
    """刪除使用者帳號及其專屬持股與買入批次 (保護 id=1 預設帳號與唯一帳號不可刪除)"""
    if user_id == 1:
        return False, "主要帳戶 (ID=1) 為系統核心帳戶，不可刪除！"
    
    users = get_all_users()
    if len(users) <= 1:
        return False, "系統中至少必須保留一個使用者帳戶！"

    try:
        with get_connection() as conn:
            cursor = conn.cursor()
            # 級聯刪除該使用者的持股與批次明細
            cursor.execute("DELETE FROM trade_lots WHERE user_id = ?", (user_id,))
            cursor.execute("DELETE FROM portfolio WHERE user_id = ?", (user_id,))
            cursor.execute("DELETE FROM users WHERE id = ?", (user_id,))
            conn.commit()

            # 若刪除的是當前登入者，自動切換回主要帳戶
            if _CURRENT_USER.get("id") == user_id:
                default_u = get_user_by_id(1) or {"id": 1, "username": "default", "display_name": "主要帳戶"}
                set_current_user(default_u)
            return True, "使用者帳號及其所有持股已成功刪除！"
    except Exception as e:
        return False, f"刪除失敗: {e}"


# =========================================================
# 庫存總表操作 (支援 user_id 帳號物理隔離)
# =========================================================

def get_all_positions(user_id: Optional[int] = None) -> List[Dict[str, Any]]:
    """取得特定使用者之所有庫存持股 (未指定則採用當前登入者)"""
    uid = user_id if user_id is not None else get_current_user()["id"]
    with get_connection() as conn:
        cursor = conn.cursor()
        cursor.execute("SELECT * FROM portfolio WHERE user_id = ? ORDER BY symbol ASC", (uid,))
        return [dict(row) for row in cursor.fetchall()]

def get_position_by_symbol(symbol: str, user_id: Optional[int] = None) -> Optional[Dict[str, Any]]:
    """依代碼查詢特定使用者之單一庫存"""
    uid = user_id if user_id is not None else get_current_user()["id"]
    with get_connection() as conn:
        cursor = conn.cursor()
        cursor.execute("SELECT * FROM portfolio WHERE user_id = ? AND symbol = ?", (uid, symbol.strip().upper()))
        row = cursor.fetchone()
        return dict(row) if row else None

def add_position(symbol: str, name: str, market: str, shares: float, cost_price: float, 
                 fee_discount: float = 0.6, is_etf: int = 0, note: str = "",
                 user_id: Optional[int] = None) -> int:
    """新增庫存紀錄至特定使用者"""
    uid = user_id if user_id is not None else get_current_user()["id"]
    symbol = symbol.strip().upper()
    with get_connection() as conn:
        cursor = conn.cursor()
        cursor.execute("""
            INSERT INTO portfolio (user_id, symbol, name, market, shares, cost_price, fee_discount, is_etf, note)
            VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?)
        """, (uid, symbol, name.strip(), market.strip(), shares, cost_price, fee_discount, is_etf, note.strip()))
        conn.commit()
        return cursor.lastrowid

def update_position(position_id: int, symbol: str, name: str, market: str, shares: float, 
                    cost_price: float, fee_discount: float, is_etf: int, note: str,
                    user_id: Optional[int] = None):
    """更新庫存紀錄"""
    uid = user_id if user_id is not None else get_current_user()["id"]
    with get_connection() as conn:
        cursor = conn.cursor()
        cursor.execute("""
            UPDATE portfolio
            SET symbol = ?, name = ?, market = ?, shares = ?, cost_price = ?, 
                fee_discount = ?, is_etf = ?, note = ?
            WHERE id = ? AND user_id = ?
        """, (symbol.strip().upper(), name.strip(), market.strip(), shares, cost_price, fee_discount, is_etf, note.strip(), position_id, uid))
        conn.commit()

def delete_position(position_id: int, user_id: Optional[int] = None):
    """刪除庫存紀錄及其關聯批次"""
    uid = user_id if user_id is not None else get_current_user()["id"]
    with get_connection() as conn:
        cursor = conn.cursor()
        cursor.execute("SELECT symbol FROM portfolio WHERE id = ? AND user_id = ?", (position_id, uid))
        row = cursor.fetchone()
        if row:
            sym = row["symbol"]
            cursor.execute("DELETE FROM trade_lots WHERE symbol = ? AND user_id = ?", (sym, uid))
        cursor.execute("DELETE FROM portfolio WHERE id = ? AND user_id = ?", (position_id, uid))
        conn.commit()


# =========================================================
# 取得時間與多筆買入批次操作 (支援 user_id 帳號物理隔離)
# =========================================================

def get_trade_lots(symbol: str, user_id: Optional[int] = None) -> List[Dict[str, Any]]:
    """取得指定標的之所有買入批次明細"""
    uid = user_id if user_id is not None else get_current_user()["id"]
    with get_connection() as conn:
        cursor = conn.cursor()
        cursor.execute("""
            SELECT * FROM trade_lots 
            WHERE symbol = ? AND user_id = ?
            ORDER BY acquire_date ASC, id ASC
        """, (symbol.strip().upper(), uid))
        return [dict(r) for r in cursor.fetchall()]

def add_trade_lot(symbol: str, acquire_date: str, shares: float, price: float, fee: float = 0.0, note: str = "",
                  user_id: Optional[int] = None) -> int:
    """新增單筆買入批次"""
    uid = user_id if user_id is not None else get_current_user()["id"]
    sym = symbol.strip().upper()
    with get_connection() as conn:
        cursor = conn.cursor()
        cursor.execute("""
            INSERT INTO trade_lots (user_id, symbol, acquire_date, shares, price, fee, note)
            VALUES (?, ?, ?, ?, ?, ?, ?)
        """, (uid, sym, acquire_date.strip(), shares, price, fee, note.strip()))
        conn.commit()
        last_id = cursor.lastrowid
        
    sync_portfolio_from_lots(sym, user_id=uid)
    return last_id

def update_trade_lot(lot_id: int, symbol: str, acquire_date: str, shares: float, price: float, fee: float = 0.0, note: str = "",
                     user_id: Optional[int] = None):
    """修改特定買入批次"""
    uid = user_id if user_id is not None else get_current_user()["id"]
    sym = symbol.strip().upper()
    with get_connection() as conn:
        cursor = conn.cursor()
        cursor.execute("""
            UPDATE trade_lots 
            SET acquire_date = ?, shares = ?, price = ?, fee = ?, note = ?
            WHERE id = ? AND symbol = ? AND user_id = ?
        """, (acquire_date.strip(), shares, price, fee, note.strip(), lot_id, sym, uid))
        conn.commit()
    sync_portfolio_from_lots(sym, user_id=uid)

def delete_trade_lot(lot_id: int, symbol: str, user_id: Optional[int] = None):
    """刪除特定批次"""
    uid = user_id if user_id is not None else get_current_user()["id"]
    sym = symbol.strip().upper()
    with get_connection() as conn:
        cursor = conn.cursor()
        cursor.execute("DELETE FROM trade_lots WHERE id = ? AND user_id = ?", (lot_id, uid))
        conn.commit()
    sync_portfolio_from_lots(sym, user_id=uid)

def sync_portfolio_from_lots(symbol: str, user_id: Optional[int] = None):
    """若該標的有多筆批次，自動計算總股數與加權成本更新至 portfolio 表"""
    uid = user_id if user_id is not None else get_current_user()["id"]
    sym = symbol.strip().upper()
    lots = get_trade_lots(sym, user_id=uid)
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
            WHERE symbol = ? AND user_id = ?
        """, (total_shares, avg_cost, sym, uid))
        conn.commit()


# =========================================================
# 日、週、月歷史價格基準 (全市場共享)
# =========================================================

def get_price_benchmarks(symbol: str, current_price: float) -> Dict[str, float]:
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


# =========================================================
# 系統設定操作與介面欄位持久化
# =========================================================

DEFAULT_VISIBLE_COLUMNS = [
    "symbol", "name", "market", "shares", "cost_price", "current_price",
    "change_pct", "total_cost", "market_val", "nav", "prem_disc", "unrealized_pnl", "roi_pct",
    "day_pnl", "week_pnl", "month_pnl",
    "frequency", "cash_dividend", "total_dividend", "yield_on_cost",
    "ex_date", "payment_month", "hist_div_received", "note"
]

def get_visible_columns() -> List[str]:
    raw = get_setting("visible_columns", "")
    if not raw:
        return list(DEFAULT_VISIBLE_COLUMNS)
    try:
        cols = json.loads(raw)
        if "nav" not in cols:
            if "market_val" in cols:
                mv_idx = cols.index("market_val")
                cols.insert(mv_idx + 1, "nav")
                cols.insert(mv_idx + 2, "prem_disc")
            else:
                cols.extend(["nav", "prem_disc"])
            set_visible_columns(cols)
        return cols
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

def get_update_timestamp(key: str) -> str:
    return get_setting(f"last_{key}_update_time", "尚未更新")

def set_update_timestamp(key: str, ts_str: Optional[str] = None):
    if ts_str is None:
        ts_str = datetime.now().strftime("%Y-%m-%d %H:%M:%S")
    set_setting(f"last_{key}_update_time", ts_str)

DEFAULT_THEME_SETTINGS = {
    "font_family": "Microsoft JhengHei UI",
    "font_size": 10,
    "row_height": 32,
    "bg_color": "#1e1e24",
    "card_bg": "#2b2b36",
    "table_bg": "#252530",
    "text_color": "#ffffff",
    "hist_div_color": "#38bdf8",
    "theme_preset": "default"
}

def get_theme_settings() -> Dict[str, Any]:
    raw = get_setting("theme_settings", "")
    if not raw:
        return dict(DEFAULT_THEME_SETTINGS)
    try:
        data = json.loads(raw)
        res = dict(DEFAULT_THEME_SETTINGS)
        res.update(data)
        return res
    except Exception:
        return dict(DEFAULT_THEME_SETTINGS)

def set_theme_settings(settings: Dict[str, Any]):
    set_setting("theme_settings", json.dumps(settings))
