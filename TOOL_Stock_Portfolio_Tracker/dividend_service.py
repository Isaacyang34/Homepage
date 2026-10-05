import datetime
import time
import requests
from typing import Dict, List, Any, Optional
from database import get_connection

try:
    import yfinance as yf
    HAS_YFINANCE = True
except ImportError:
    HAS_YFINANCE = False

TWSE_EX_ANNOUNCE_URL = "https://www.twse.com.tw/rwd/zh/exRight/TWT48U" # 除權除息預告表 (最新已公布)

class DividendService:
    _cached_twse_announced: Optional[Dict[str, Dict[str, Any]]] = None
    _last_fetch_time = 0.0

    @classmethod
    def fetch_twse_announced_dividends(cls) -> Dict[str, Dict[str, Any]]:
        """
        抓取證交所官方最新除權息預告表 (只要有公布日期的股票)
        回傳字典: key 為 symbol, value 為包含 ex_date, cash_div 等
        快取 10 分鐘，防高頻請求
        """
        now = time.time()
        if cls._cached_twse_announced is not None and (now - cls._last_fetch_time < 600):
            return cls._cached_twse_announced

        announced_map = {}
        headers = {
            "User-Agent": "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36",
            "Accept": "application/json, text/javascript, */*; q=0.01",
            "Referer": "https://www.twse.com.tw/zh/trading/historical/twt48u.html"
        }
        try:
            resp = requests.get(TWSE_EX_ANNOUNCE_URL, params={"response": "json"}, headers=headers, timeout=8)
            cls._last_fetch_time = time.time()
            if resp.status_code == 200:
                data = resp.json()
                # data["data"]: 每列為 [除息交易日(民國), 股票代號, 股票名稱, 除權息前收盤價, 開盤競價基準, ..., 每股現金股利, ...]
                fields = data.get("fields", [])
                rows = data.get("data", [])
                
                # 尋找欄位索引
                code_idx = 1
                ex_date_idx = 0
                cash_idx = -1
                for idx, f in enumerate(fields):
                    if "現金股利" in f:
                        cash_idx = idx
                        break

                for r in rows:
                    if len(r) > max(code_idx, ex_date_idx):
                        sym = str(r[code_idx]).strip().upper()
                        raw_date = str(r[ex_date_idx]).strip() # 例 113年10月15日 或 113/10/15
                        # 解析日期
                        clean_date = raw_date.replace("年", "/").replace("月", "/").replace("日", "")
                        parts = clean_date.split("/")
                        if len(parts) >= 3:
                            ad_year = int(parts[0]) + 1911
                            norm_date = f"{ad_year:04d}-{int(parts[1]):02d}-{int(parts[2]):02d}"
                        else:
                            norm_date = raw_date

                        cash_val = 0.0
                        if cash_idx != -1 and len(r) > cash_idx:
                            try:
                                cash_val = float(str(r[cash_idx]).replace(",", "").strip())
                            except Exception:
                                cash_val = 0.0

                        announced_map[sym] = {
                            "symbol": sym,
                            "cash_dividend": cash_val,
                            "ex_date": norm_date,
                            "status": "已公布日期",
                            "is_announced": 1
                        }
        except Exception as e:
            print(f"[DividendService] 抓取除權息預告表失敗: {e}")

        cls._cached_twse_announced = announced_map
        return announced_map

    @classmethod
    def get_stock_dividend_info(cls, symbol: str, market: str = "TW") -> Dict[str, Any]:
        """
        取得個股股息資訊：
        1. 若有公布最新除息日期 -> 使用最新已公布日期與股息 (status: '已公布日期')
        2. 若未公布最新日期 -> 留存並讀取本地歷史/去年數據 (status: '留存去年數據')
        """
        sym = symbol.strip().upper()
        current_year = datetime.date.today().year
        last_year = current_year - 1

        # 優先查詢資料庫現有紀錄
        db_record = cls.get_db_dividend(sym)

        # 1. 比對 TWSE 官方最新公布之除息預告表
        if market in ["TW", "TWO"]:
            twse_announced = cls.fetch_twse_announced_dividends()
            if sym in twse_announced:
                info = twse_announced[sym]
                cls.save_dividend_record(sym, info["cash_dividend"], info["ex_date"], "已公布日期", str(current_year), 1)
                return info

        # 2. 如果資料庫已存在「已公布日期」且除息日在未來或今年
        if db_record and db_record["is_announced"] == 1 and db_record["cash_dividend"] > 0:
            return db_record

        # 3. 嘗試使用 yfinance 查詢歷史配息
        if HAS_YFINANCE:
            yf_info = cls._fetch_yfinance_dividend(sym, market)
            if yf_info:
                cls.save_dividend_record(
                    sym, yf_info["cash_dividend"], yf_info["ex_date"], 
                    yf_info["status"], yf_info["dividend_year"], yf_info["is_announced"]
                )
                return yf_info

        # 4. 若皆無新公布，但 DB 有既有紀錄 -> 留存去年/既有數據
        if db_record and db_record["cash_dividend"] > 0:
            return db_record

        # 5. 預設空資訊 (留存去年標記)
        default_info = {
            "symbol": sym,
            "cash_dividend": 0.0,
            "ex_date": "未公布",
            "status": f"留存{last_year}年數據",
            "dividend_year": str(last_year),
            "is_announced": 0
        }
        return default_info

    @classmethod
    def _fetch_yfinance_dividend(cls, symbol: str, market: str) -> Optional[Dict[str, Any]]:
        """透過 yfinance 取得最新配息紀錄，判定是最新公布還是去年度配息"""
        if market == "TW":
            yf_sym = f"{symbol}.TW"
        elif market == "TWO":
            yf_sym = f"{symbol}.TWO"
        else:
            yf_sym = symbol

        today = datetime.date.today()
        current_year = today.year
        last_year = current_year - 1

        try:
            t = yf.Ticker(yf_sym)
            div_series = t.dividends
            if div_series is None or div_series.empty:
                return None

            # 抓取最近兩年的配息
            # div_series index 為 Timestamp, value 為 float
            recent = div_series.tail(10)
            
            # 檢查是否有今天之後（未來）已公布的除息日
            future_divs = recent[recent.index.date >= today]
            if not future_divs.empty:
                last_dt = future_divs.index[-1].date()
                cash_val = float(future_divs.iloc[-1])
                return {
                    "symbol": symbol,
                    "cash_dividend": round(cash_val, 2),
                    "ex_date": last_dt.strftime("%Y-%m-%d"),
                    "status": "已公布日期",
                    "dividend_year": str(last_dt.year),
                    "is_announced": 1
                }

            # 檢查當年度的配息總和
            cur_year_divs = recent[recent.index.year == current_year]
            if not cur_year_divs.empty:
                last_dt = cur_year_divs.index[-1].date()
                sum_cash = float(cur_year_divs.sum())
                return {
                    "symbol": symbol,
                    "cash_dividend": round(sum_cash, 2),
                    "ex_date": last_dt.strftime("%Y-%m-%d"),
                    "status": f"已公布({current_year})",
                    "dividend_year": str(current_year),
                    "is_announced": 1
                }

            # 若今年尚無公布，則統計去年度 (last_year) 總配息留存
            last_year_divs = recent[recent.index.year == last_year]
            if not last_year_divs.empty:
                last_dt = last_year_divs.index[-1].date()
                sum_cash = float(last_year_divs.sum())
                return {
                    "symbol": symbol,
                    "cash_dividend": round(sum_cash, 2),
                    "ex_date": f"留存{last_year}",
                    "status": f"留存{last_year}年數據",
                    "dividend_year": str(last_year),
                    "is_announced": 0
                }

        except Exception as e:
            print(f"[DividendService] yfinance 配息查詢失敗: {e}")
        return None

    @classmethod
    def get_db_dividend(cls, symbol: str) -> Optional[Dict[str, Any]]:
        """從資料庫讀取股息紀錄"""
        with get_connection() as conn:
            cursor = conn.cursor()
            cursor.execute("SELECT * FROM dividend_records WHERE symbol = ?", (symbol.upper(),))
            row = cursor.fetchone()
            return dict(row) if row else None

    @classmethod
    def save_dividend_record(cls, symbol: str, cash_div: float, ex_date: str, status: str, div_year: str, is_announced: int):
        """儲存或更新股息紀錄"""
        now_str = datetime.datetime.now().strftime("%Y-%m-%d %H:%M:%S")
        with get_connection() as conn:
            cursor = conn.cursor()
            cursor.execute("""
                INSERT INTO dividend_records (symbol, cash_dividend, ex_date, status, dividend_year, is_announced, updated_at)
                VALUES (?, ?, ?, ?, ?, ?, ?)
                ON CONFLICT(symbol) DO UPDATE SET
                    cash_dividend = excluded.cash_dividend,
                    ex_date = excluded.ex_date,
                    status = excluded.status,
                    dividend_year = excluded.dividend_year,
                    is_announced = excluded.is_announced,
                    updated_at = excluded.updated_at
            """, (symbol.upper(), cash_div, ex_date, status, div_year, is_announced, now_str))
            conn.commit()
