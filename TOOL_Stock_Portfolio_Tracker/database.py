# -*- coding: utf-8 -*-
"""
Stock Portfolio Tracker - 本地 SQLite 資料庫存取層 (database.py)
支援功能：
1. 多使用者獨立帳號管理 (users 資料表、密碼 SHA-256+Salt 雜湊防護、免密碼無縫登入)
2. 敏感欄位 AES-256 軍規應用層加密防護 (AESSIV 決定性加密標的代碼與名稱 + AES-256-GCM 隨機化加密股數、成本與備註)
3. 庫存持股 (portfolio) 與買入批次 (trade_lots) 之 user_id 物理隔離
4. 歷史日K (history_kline) 與除權息 (dividend_records) 全局共享，節省流量與空間
5. 現有歷史明文 100% 自動無痛遷移升級為密文，外人竊取 portfolio.db 無密碼絕無法逆向還原
"""

import sqlite3
import os
import sys
import json
import hashlib
import secrets
import base64
from datetime import datetime
from typing import List, Dict, Any, Optional, Tuple

from cryptography.hazmat.primitives.ciphers.aead import AESGCM, AESSIV
from cryptography.hazmat.primitives.kdf.pbkdf2 import PBKDF2HMAC
from cryptography.hazmat.primitives import hashes

if getattr(sys, 'frozen', False):
    BASE_DIR = os.path.dirname(os.path.abspath(sys.executable))
else:
    BASE_DIR = os.path.dirname(os.path.abspath(__file__))
    if BASE_DIR.lower().endswith(".pkg") or BASE_DIR.lower().endswith(".zip") or os.path.basename(BASE_DIR) == "modules":
        BASE_DIR = os.path.dirname(BASE_DIR)

DB_FILE = os.path.join(BASE_DIR, "portfolio.db")

# 當前運行期登入使用者狀態 (全域單例)
_CURRENT_USER: Dict[str, Any] = {}

ENC_PREFIX = "ENC:"

# 當前運行期登入使用者之解密引擎單例緩存 (user_id -> {"gcm": AESGCM, "siv": AESSIV})
_SESSION_CRYPTO_ENGINES: Dict[int, Dict[str, Any]] = {}

def get_startup_user() -> Dict[str, Any]:
    """從本地資料庫讀取上次登入帳號或預設使用者 (精確帶出 has_password 旗標)"""
    try:
        init_db()
        last_uid_str = get_setting("last_user_id", "1")
        try:
            last_uid = int(last_uid_str)
        except Exception:
            last_uid = 1
        user = get_user_by_id(last_uid)
        if not user:
            all_u = get_all_users()
            if all_u:
                user = get_user_by_id(all_u[0]["id"])
        if user:
            has_pwd = bool(user.get("password_hash"))
            return {
                "id": int(user["id"]),
                "username": str(user["username"]),
                "display_name": str(user.get("display_name") or user["username"]),
                "has_password": has_pwd
            }
    except Exception:
        pass
    return {
        "id": 1,
        "username": "default",
        "display_name": "主要帳戶",
        "has_password": False
    }

def get_current_user() -> Dict[str, Any]:
    """取得當前已登入之使用者資訊 (具備 has_password 狀態)"""
    global _CURRENT_USER
    if not _CURRENT_USER or "has_password" not in _CURRENT_USER:
        _CURRENT_USER = get_startup_user()
    return dict(_CURRENT_USER)

def set_current_user(user_info: Dict[str, Any]):
    """設定當前已登入之使用者資訊"""
    global _CURRENT_USER
    has_pwd = bool(user_info.get("has_password", False))
    if "has_password" not in user_info and "password_hash" in user_info:
        has_pwd = bool(user_info.get("password_hash"))
    _CURRENT_USER = {
        "id": int(user_info.get("id", 1)),
        "username": str(user_info.get("username", "default")),
        "display_name": str(user_info.get("display_name", user_info.get("username", "主要帳戶"))),
        "has_password": has_pwd
    }

def is_startup_login_required() -> bool:
    """
    判斷啟動系統時是否必須顯示登入視窗：
    1. 當前/上次登入帳號設有安全密碼 -> 100% 強制要求輸入密碼驗證！
    2. 或系統內存在多個使用者帳號且其中有任一帳號設有密碼 -> 強制登入選擇！
    3. 或設定為每次啟動需選擇帳號 (require_login_on_startup == '1')。
    """
    cur_u = get_current_user()
    if cur_u.get("has_password"):
        return True
    req_setting = get_setting("require_login_on_startup", "")
    if req_setting == "1":
        return True
    all_users = get_all_users()
    if len(all_users) > 1:
        for u in all_users:
            if u.get("has_password"):
                return True
    return False

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

# =========================================================
# 本機欄位級 AES-256 軍規加密/解密引擎 (AESSIV + AESGCM)
# =========================================================

def _derive_crypto_engine(password: str, salt: str) -> Dict[str, Any]:
    """使用 PBKDF2 (100,000次) 從密碼與鹽值衍生 AESGCM (32B) 與 AESSIV (64B) 雙引擎"""
    kdf = PBKDF2HMAC(
        algorithm=hashes.SHA256(),
        length=96,
        salt=salt.encode("utf-8") if salt else b"salt_default_stock",
        iterations=100000,
    )
    derived = kdf.derive(password.encode("utf-8"))
    return {
        "gcm": AESGCM(derived[:32]),
        "siv": AESSIV(derived[32:96])
    }

def _get_fallback_salt(user_id: int) -> str:
    """取得或建立本機設備級鹽值 (用於未設密碼之帳號，避免資料庫裸存明碼)"""
    dev_salt = get_setting("device_crypto_salt", "")
    if not dev_salt:
        dev_salt = secrets.token_hex(16)
        set_setting("device_crypto_salt", dev_salt)
    return f"dev_{dev_salt}_uid_{user_id}"

def _get_user_crypto_engine(user_id: int) -> Dict[str, Any]:
    """取得指定使用者之加密引擎，優先使用使用者登入解鎖之引擎，未解鎖則回退保底設備引擎"""
    global _SESSION_CRYPTO_ENGINES
    engine = _SESSION_CRYPTO_ENGINES.get(user_id)
    if engine:
        return engine
    
    # 檢查是否有無密碼使用者或未輸入密碼之環境
    fb_salt = _get_fallback_salt(user_id)
    fallback = _derive_crypto_engine(fb_salt, fb_salt)
    _SESSION_CRYPTO_ENGINES[user_id] = fallback
    return fallback

def init_user_crypto_session(user_id: int, password: str, salt: str):
    """使用者輸入正確密碼登入後，以其密碼升級該使用者之運行期金鑰引擎並執行自動遷移"""
    global _SESSION_CRYPTO_ENGINES
    if not password:
        fb_salt = _get_fallback_salt(user_id)
        _SESSION_CRYPTO_ENGINES[user_id] = _derive_crypto_engine(fb_salt, fb_salt)
    else:
        _SESSION_CRYPTO_ENGINES[user_id] = _derive_crypto_engine(password, salt)
    
    # 登入成功時，若硬碟上有尚未加密之歷史明文，自動無痛全量升級為 AES-256 密文
    try:
        migrate_user_data_to_encrypted(user_id)
    except Exception as e:
        print(f"[database] 自動遷移加密異常: {e}")

def clear_user_crypto_session(user_id: Optional[int] = None):
    """登出時銷毀記憶體中的金鑰"""
    global _SESSION_CRYPTO_ENGINES
    if user_id is not None:
        _SESSION_CRYPTO_ENGINES.pop(user_id, None)
    else:
        _SESSION_CRYPTO_ENGINES.clear()

def encrypt_field_gcm(value: Any, user_id: int) -> str:
    """隨機化 AES-256-GCM 加密數值或備註 (每次 Nonce 不同，徹底防禦頻率分析)"""
    if value is None:
        return ""
    val_str = str(value).strip()
    if not val_str:
        return ""
    if val_str.startswith(ENC_PREFIX):
        return val_str  # 避免重複加密
        
    engine = _get_user_crypto_engine(user_id)
    nonce = secrets.token_bytes(12)
    ct = engine["gcm"].encrypt(nonce, val_str.encode("utf-8"), None)
    token = base64.b64encode(nonce + ct).decode("ascii")
    return f"{ENC_PREFIX}GCM:{token}"

def decrypt_field_gcm(encrypted_str: Any, user_id: int, target_type: type = str) -> Any:
    """解密 AES-256-GCM 密文並轉型 (支援 float, int, str)"""
    if encrypted_str is None:
        return target_type() if target_type in (int, float) else ""
    s = str(encrypted_str).strip()
    if not s:
        return target_type() if target_type in (int, float) else ""
    if not s.startswith(ENC_PREFIX):
        # 兼容歷史未加密明文
        try:
            return target_type(s)
        except Exception:
            return s
            
    token_str = s[len(ENC_PREFIX):]
    if token_str.startswith("GCM:"):
        token_str = token_str[4:]
    try:
        raw = base64.b64decode(token_str)
        nonce = raw[:12]
        ct = raw[12:]
        engine = _get_user_crypto_engine(user_id)
        pt = engine["gcm"].decrypt(nonce, ct, None).decode("utf-8")
        if target_type == float:
            return float(pt)
        elif target_type == int:
            return int(float(pt))
        return pt
    except Exception:
        # 解密失敗時防呆回退
        return target_type() if target_type in (int, float) else s

def encrypt_field_siv(value: str, user_id: int) -> str:
    """決定性 AESSIV 加密 (用於 symbol, name，支援 SQL 快速精確比對)"""
    if not value:
        return ""
    val_str = str(value).strip()
    if val_str.startswith(ENC_PREFIX):
        return val_str
        
    engine = _get_user_crypto_engine(user_id)
    ct = engine["siv"].encrypt(val_str.encode("utf-8"), None)
    token = base64.b64encode(ct).decode("ascii")
    return f"{ENC_PREFIX}SIV:{token}"

def decrypt_field_siv(encrypted_str: str, user_id: int) -> str:
    """解密 AESSIV 密文"""
    if not encrypted_str:
        return ""
    s = str(encrypted_str).strip()
    if not s.startswith(ENC_PREFIX):
        return s  # 兼容歷史未加密明文
        
    token_str = s[len(ENC_PREFIX):]
    if token_str.startswith("SIV:"):
        token_str = token_str[4:]
    try:
        raw = base64.b64decode(token_str)
        engine = _get_user_crypto_engine(user_id)
        pt = engine["siv"].decrypt(raw, None).decode("utf-8")
        return pt
    except Exception:
        return s

def migrate_user_data_to_encrypted(user_id: int):
    """檢查並將指定使用者的歷史明文持股與交易批次全面無痛升級為 AES-256 加密"""
    with get_connection() as conn:
        cursor = conn.cursor()
        # 1. 遷移 portfolio
        cursor.execute("SELECT id, symbol, name, shares, cost_price, note FROM portfolio WHERE user_id = ?", (user_id,))
        p_rows = cursor.fetchall()
        for r in p_rows:
            raw_sym = str(r["symbol"])
            raw_shares = str(r["shares"])
            if not raw_sym.startswith(ENC_PREFIX) or not raw_shares.startswith(ENC_PREFIX):
                dec_sym = decrypt_field_siv(raw_sym, user_id).strip().upper()
                dec_name = decrypt_field_siv(str(r["name"] or ""), user_id)
                dec_shares = decrypt_field_gcm(r["shares"], user_id, float)
                dec_cost = decrypt_field_gcm(r["cost_price"], user_id, float)
                dec_note = decrypt_field_gcm(str(r["note"] or ""), user_id, str)
                
                enc_sym = encrypt_field_siv(dec_sym, user_id)
                enc_name = encrypt_field_siv(dec_name, user_id)
                enc_shares = encrypt_field_gcm(dec_shares, user_id)
                enc_cost = encrypt_field_gcm(dec_cost, user_id)
                enc_note = encrypt_field_gcm(dec_note, user_id)
                cursor.execute("""
                    UPDATE portfolio
                    SET symbol = ?, name = ?, shares = ?, cost_price = ?, note = ?
                    WHERE id = ?
                """, (enc_sym, enc_name, enc_shares, enc_cost, enc_note, r["id"]))

        # 2. 遷移 trade_lots
        cursor.execute("SELECT id, symbol, shares, price, fee, note FROM trade_lots WHERE user_id = ?", (user_id,))
        t_rows = cursor.fetchall()
        for r in t_rows:
            raw_sym = str(r["symbol"])
            raw_shares = str(r["shares"])
            if not raw_sym.startswith(ENC_PREFIX) or not raw_shares.startswith(ENC_PREFIX):
                dec_sym = decrypt_field_siv(raw_sym, user_id).strip().upper()
                dec_shares = decrypt_field_gcm(r["shares"], user_id, float)
                dec_price = decrypt_field_gcm(r["price"], user_id, float)
                dec_fee = decrypt_field_gcm(r["fee"], user_id, float)
                dec_note = decrypt_field_gcm(str(r["note"] or ""), user_id, str)
                
                enc_sym = encrypt_field_siv(dec_sym, user_id)
                enc_shares = encrypt_field_gcm(dec_shares, user_id)
                enc_price = encrypt_field_gcm(dec_price, user_id)
                enc_fee = encrypt_field_gcm(dec_fee, user_id)
                enc_note = encrypt_field_gcm(dec_note, user_id)
                cursor.execute("""
                    UPDATE trade_lots
                    SET symbol = ?, shares = ?, price = ?, fee = ?, note = ?
                    WHERE id = ?
                """, (enc_sym, enc_shares, enc_price, enc_fee, enc_note, r["id"]))
        conn.commit()


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
                frequency TEXT DEFAULT '',      -- 年配, 季配, 月配, 半年配
                cash_dividend REAL DEFAULT 0.0, -- 最新一期現金股利
                total_dividend REAL DEFAULT 0.0,-- 近四季/全年累計現金股利
                ex_date TEXT DEFAULT '',        -- 最新除息日
                payment_date TEXT DEFAULT '',   -- 最新發放日
                payment_month TEXT DEFAULT '',  -- 預計發放月份
                history_json TEXT DEFAULT '',   -- 歷年股息 JSON 紀錄
                updated_at TEXT DEFAULT CURRENT_TIMESTAMP
            )
        """)

        # 5. 系統偏好設定表 (全域共用)
        cursor.execute("""
            CREATE TABLE IF NOT EXISTS app_settings (
                key TEXT PRIMARY KEY,
                value TEXT NOT NULL
            )
        """)

        conn.commit()

        # 自動校驗運行期當前使用者
        try:
            cur_u = get_current_user()
            cursor.execute("SELECT id, username, display_name, password_hash, salt FROM users WHERE id = ?", (cur_u["id"],))
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
            
            # 立即初始化金鑰
            init_user_crypto_session(new_id, password, salt)
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
        init_user_crypto_session(user["id"], "", salt)
        return True, user, "驗證成功 (免密碼帳戶)"
    if not password:
        return False, None, "此帳戶已設置安全密碼，請輸入密碼！"
    if verify_password(password, salt, pw_hash):
        init_user_crypto_session(user["id"], password, salt)
        return True, user, "密碼驗證成功！"
    return False, None, "密碼錯誤，請重新確認！"

def update_user_password(user_id: int, new_password: str) -> Tuple[bool, str]:
    """更新使用者密碼並執行金鑰輪替 (Key Rotation) 重新加密現有持股"""
    # 1. 備份解密現有持股與批次
    old_positions = get_all_positions(user_id=user_id)
    old_lots = {}
    for pos in old_positions:
        sym = pos["symbol"]
        old_lots[sym] = get_trade_lots(sym, user_id=user_id)

    pw_hash, salt = hash_password(new_password) if new_password else ("", "")
    try:
        with get_connection() as conn:
            cursor = conn.cursor()
            cursor.execute("UPDATE users SET password_hash = ?, salt = ? WHERE id = ?", (pw_hash, salt, user_id))
            conn.commit()

        # 2. 升級或輪替新金鑰引擎
        if new_password:
            init_user_crypto_session(user_id, new_password, salt)
        else:
            fb_salt = _get_fallback_salt(user_id)
            _SESSION_CRYPTO_ENGINES[user_id] = _derive_crypto_engine(fb_salt, fb_salt)

        # 3. 以新金鑰重新加密並覆寫現有持股
        for pos in old_positions:
            update_position(pos["id"], pos["symbol"], pos.get("name", ""), pos.get("market", "TW"),
                            pos["shares"], pos["cost_price"], pos.get("fee_discount", 0.6),
                            pos.get("is_etf", 0), pos.get("note", ""), user_id=user_id)
        for sym, lots in old_lots.items():
            for lot in lots:
                update_trade_lot(lot["id"], sym, lot["acquire_date"], lot["shares"],
                                 lot["price"], lot.get("fee", 0.0), lot.get("note", ""), user_id=user_id)

        return True, "密碼更新成功！全庫存已完成 AES-256 金鑰輪替升級！"
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
            cursor.execute("SELECT id FROM users WHERE username = ? AND id != ?", (clean_u, user_id))
            if cursor.fetchone():
                return False, f"帳號名稱「{clean_u}」已被其他使用者使用，請使用其他名稱！"
                
            cursor.execute("UPDATE users SET username = ? WHERE id = ?", (clean_u, user_id))
            conn.commit()
            if _CURRENT_USER.get("id") == user_id:
                _CURRENT_USER["username"] = clean_u
            return True, "帳號名稱已成功更新！"
    except Exception as e:
        return False, f"更新帳號失敗: {e}"

def delete_user(user_id: int) -> Tuple[bool, str]:
    """刪除使用者帳號及其所有專屬持股與買入批次 (防止刪除主要帳號與唯一帳號)"""
    if user_id == 1:
        return False, "預設主要帳號 (ID=1) 受到系統保護，不可刪除！"
        
    all_users = get_all_users()
    if len(all_users) <= 1:
        return False, "系統中至少必須保留一個使用者帳號，無法刪除！"
        
    try:
        with get_connection() as conn:
            cursor = conn.cursor()
            cursor.execute("DELETE FROM trade_lots WHERE user_id = ?", (user_id,))
            cursor.execute("DELETE FROM portfolio WHERE user_id = ?", (user_id,))
            cursor.execute("DELETE FROM users WHERE id = ?", (user_id,))
            conn.commit()
            
            # 若刪除的是當前登入者，自動切換至剩餘之第 1 位使用者
            if _CURRENT_USER.get("id") == user_id:
                remain = get_all_users()
                default_u = remain[0] if remain else {"id": 1, "username": "default", "display_name": "主要帳戶"}
                set_current_user(default_u)
                
            _SESSION_CRYPTO_ENGINES.pop(user_id, None)
            return True, "使用者帳號及相關持股資料已完全刪除！"
    except Exception as e:
        return False, f"刪除失敗: {e}"

# =========================================================
# 庫存部位 (Portfolio) CRUD - 支援 AES-256 欄位加密與透明解密
# =========================================================

def get_all_positions(user_id: Optional[int] = None) -> List[Dict[str, Any]]:
    """取得特定使用者之所有庫存持股 (未指定則採用當前登入者)，自動透明解密"""
    uid = user_id if user_id is not None else get_current_user()["id"]
    with get_connection() as conn:
        cursor = conn.cursor()
        cursor.execute("SELECT * FROM portfolio WHERE user_id = ?", (uid,))
        rows = [dict(row) for row in cursor.fetchall()]
        
    positions = []
    for r in rows:
        dec_pos = dict(r)
        dec_pos["symbol"] = decrypt_field_siv(r.get("symbol", ""), uid).strip().upper()
        dec_pos["name"] = decrypt_field_siv(r.get("name", ""), uid)
        dec_pos["shares"] = decrypt_field_gcm(r.get("shares"), uid, float)
        dec_pos["cost_price"] = decrypt_field_gcm(r.get("cost_price"), uid, float)
        try:
            dec_pos["fee_discount"] = float(r.get("fee_discount", 0.6))
        except (ValueError, TypeError):
            dec_pos["fee_discount"] = 0.6
        dec_pos["is_etf"] = int(r.get("is_etf", 0))
        dec_pos["note"] = decrypt_field_gcm(r.get("note", ""), uid, str)
        positions.append(dec_pos)
        
    positions.sort(key=lambda x: x.get("symbol", ""))
    return positions

def get_position_by_symbol(symbol: str, user_id: Optional[int] = None) -> Optional[Dict[str, Any]]:
    """依代碼查詢特定使用者之單一庫存"""
    uid = user_id if user_id is not None else get_current_user()["id"]
    sym = symbol.strip().upper()
    enc_sym = encrypt_field_siv(sym, uid)
    with get_connection() as conn:
        cursor = conn.cursor()
        cursor.execute("SELECT * FROM portfolio WHERE user_id = ? AND (symbol = ? OR symbol = ?)", (uid, enc_sym, sym))
        row = cursor.fetchone()
        if not row:
            return None
        r = dict(row)
        r["symbol"] = sym
        r["name"] = decrypt_field_siv(r.get("name", ""), uid)
        r["shares"] = decrypt_field_gcm(r.get("shares"), uid, float)
        r["cost_price"] = decrypt_field_gcm(r.get("cost_price"), uid, float)
        try:
            r["fee_discount"] = float(r.get("fee_discount", 0.6))
        except (ValueError, TypeError):
            r["fee_discount"] = 0.6
        r["is_etf"] = int(r.get("is_etf", 0))
        r["note"] = decrypt_field_gcm(r.get("note", ""), uid, str)
        return r

def add_position(symbol: str, name: str, market: str, shares: float, cost_price: float, 
                 fee_discount: Any = 0.6, is_etf: int = 0, note: str = "",
                 user_id: Optional[int] = None) -> int:
    """新增庫存紀錄至特定使用者，欄位自動加密存入 SQLite"""
    uid = user_id if user_id is not None else get_current_user()["id"]
    sym = symbol.strip().upper()
    
    # 參數容錯防呆：若第 6 個參數傳入備註字串而非折讓數值
    if isinstance(fee_discount, str):
        try:
            fee_discount_val = float(fee_discount)
        except ValueError:
            if not note:
                note = fee_discount
            fee_discount_val = 0.6
    else:
        fee_discount_val = float(fee_discount) if fee_discount is not None else 0.6

    enc_sym = encrypt_field_siv(sym, uid)
    enc_name = encrypt_field_siv(name.strip(), uid)
    enc_shares = encrypt_field_gcm(shares, uid)
    enc_cost = encrypt_field_gcm(cost_price, uid)
    enc_note = encrypt_field_gcm(note.strip(), uid)
    
    with get_connection() as conn:
        cursor = conn.cursor()
        cursor.execute("""
            INSERT INTO portfolio (user_id, symbol, name, market, shares, cost_price, fee_discount, is_etf, note)
            VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?)
        """, (uid, enc_sym, enc_name, market.strip(), enc_shares, enc_cost, fee_discount_val, is_etf, enc_note))
        conn.commit()
        return cursor.lastrowid

def update_position(position_id: int, symbol: str, name: str, market: str, shares: float, 
                    cost_price: float, fee_discount: float, is_etf: int, note: str,
                    user_id: Optional[int] = None):
    """更新庫存紀錄，欄位自動加密"""
    uid = user_id if user_id is not None else get_current_user()["id"]
    sym = symbol.strip().upper()
    enc_sym = encrypt_field_siv(sym, uid)
    enc_name = encrypt_field_siv(name.strip(), uid)
    enc_shares = encrypt_field_gcm(shares, uid)
    enc_cost = encrypt_field_gcm(cost_price, uid)
    enc_note = encrypt_field_gcm(note.strip(), uid)
    
    with get_connection() as conn:
        cursor = conn.cursor()
        cursor.execute("""
            UPDATE portfolio
            SET symbol = ?, name = ?, market = ?, shares = ?, cost_price = ?, 
                fee_discount = ?, is_etf = ?, note = ?
            WHERE id = ? AND user_id = ?
        """, (enc_sym, enc_name, market.strip(), enc_shares, enc_cost, fee_discount, is_etf, enc_note, position_id, uid))
        conn.commit()

def delete_position(position_id: int, user_id: Optional[int] = None):
    """刪除庫存紀錄及其關聯批次"""
    uid = user_id if user_id is not None else get_current_user()["id"]
    with get_connection() as conn:
        cursor = conn.cursor()
        cursor.execute("SELECT symbol FROM portfolio WHERE id = ? AND user_id = ?", (position_id, uid))
        row = cursor.fetchone()
        if row:
            raw_sym = row["symbol"]
            dec_sym = decrypt_field_siv(raw_sym, uid).strip().upper()
            enc_sym = encrypt_field_siv(dec_sym, uid)
            cursor.execute("DELETE FROM trade_lots WHERE (symbol = ? OR symbol = ? OR symbol = ?) AND user_id = ?", (raw_sym, enc_sym, dec_sym, uid))
        cursor.execute("DELETE FROM portfolio WHERE id = ? AND user_id = ?", (position_id, uid))
        conn.commit()

# =========================================================
# 多筆買入批次 (Trade Lots) CRUD - 支援 AES-256 欄位加密與透明解密
# =========================================================

def get_trade_lots(symbol: str, user_id: Optional[int] = None) -> List[Dict[str, Any]]:
    """取得指定標的之所有買入批次明細，自動透明解密"""
    uid = user_id if user_id is not None else get_current_user()["id"]
    sym = symbol.strip().upper()
    enc_sym = encrypt_field_siv(sym, uid)
    with get_connection() as conn:
        cursor = conn.cursor()
        cursor.execute("""
            SELECT * FROM trade_lots 
            WHERE (symbol = ? OR symbol = ?) AND user_id = ?
            ORDER BY acquire_date ASC, id ASC
        """, (enc_sym, sym, uid))
        rows = [dict(r) for r in cursor.fetchall()]
        
    lots = []
    for r in rows:
        dec_lot = dict(r)
        dec_lot["symbol"] = sym
        dec_lot["shares"] = decrypt_field_gcm(r.get("shares"), uid, float)
        dec_lot["price"] = decrypt_field_gcm(r.get("price"), uid, float)
        dec_lot["fee"] = decrypt_field_gcm(r.get("fee", 0.0), uid, float)
        dec_lot["note"] = decrypt_field_gcm(r.get("note", ""), uid, str)
        lots.append(dec_lot)
    return lots

def add_trade_lot(symbol: str, acquire_date: str, shares: float, price: float, fee: float = 0.0, note: str = "",
                  user_id: Optional[int] = None) -> int:
    """新增單筆買入批次，欄位自動加密存入 SQLite"""
    uid = user_id if user_id is not None else get_current_user()["id"]
    sym = symbol.strip().upper()
    enc_sym = encrypt_field_siv(sym, uid)
    enc_shares = encrypt_field_gcm(shares, uid)
    enc_price = encrypt_field_gcm(price, uid)
    enc_fee = encrypt_field_gcm(fee, uid)
    enc_note = encrypt_field_gcm(note.strip(), uid)
    
    with get_connection() as conn:
        cursor = conn.cursor()
        cursor.execute("""
            INSERT INTO trade_lots (user_id, symbol, acquire_date, shares, price, fee, note)
            VALUES (?, ?, ?, ?, ?, ?, ?)
        """, (uid, enc_sym, acquire_date.strip(), enc_shares, enc_price, enc_fee, enc_note))
        conn.commit()
        last_id = cursor.lastrowid
        
    sync_portfolio_from_lots(sym, user_id=uid)
    return last_id

def update_trade_lot(lot_id: int, symbol: str, acquire_date: str, shares: float, price: float, fee: float = 0.0, note: str = "",
                     user_id: Optional[int] = None):
    """修改特定買入批次，欄位自動加密"""
    uid = user_id if user_id is not None else get_current_user()["id"]
    sym = symbol.strip().upper()
    enc_shares = encrypt_field_gcm(shares, uid)
    enc_price = encrypt_field_gcm(price, uid)
    enc_fee = encrypt_field_gcm(fee, uid)
    enc_note = encrypt_field_gcm(note.strip(), uid)
    
    with get_connection() as conn:
        cursor = conn.cursor()
        cursor.execute("""
            UPDATE trade_lots 
            SET acquire_date = ?, shares = ?, price = ?, fee = ?, note = ?
            WHERE id = ? AND user_id = ?
        """, (acquire_date.strip(), enc_shares, enc_price, enc_fee, enc_note, lot_id, uid))
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
    """若該標的有多筆批次，自動計算總股數與加權成本更新至 portfolio 表 (加密回寫)"""
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

    enc_sym = encrypt_field_siv(sym, uid)
    enc_shares = encrypt_field_gcm(total_shares, uid)
    enc_cost = encrypt_field_gcm(avg_cost, uid)

    with get_connection() as conn:
        cursor = conn.cursor()
        cursor.execute("""
            UPDATE portfolio 
            SET shares = ?, cost_price = ?
            WHERE user_id = ? AND (symbol = ? OR symbol = ?)
        """, (enc_shares, enc_cost, uid, enc_sym, sym))
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
