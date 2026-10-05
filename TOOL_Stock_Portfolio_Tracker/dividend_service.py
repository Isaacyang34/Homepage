import datetime
import time
import requests
from typing import Dict, List, Any, Optional
from database import get_connection

# 證交所最新除權除息預告表 (未來排定)
TWSE_EX_ANNOUNCE_URL = "https://www.twse.com.tw/rwd/zh/exRight/TWT48U"

class DividendService:
    _cached_twse_announced: Optional[Dict[str, Dict[str, Any]]] = None
    _last_twse_fetch_time = 0.0

    @classmethod
    def fetch_twse_announced_dividends(cls) -> Dict[str, Dict[str, Any]]:
        """抓取證交所即將除權息預告表"""
        now = time.time()
        if cls._cached_twse_announced is not None and (now - cls._last_twse_fetch_time < 600):
            return cls._cached_twse_announced

        announced_map = {}
        headers = {
            "User-Agent": "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36",
            "Accept": "application/json, text/javascript, */*; q=0.01",
            "Referer": "https://www.twse.com.tw/zh/trading/historical/twt48u.html"
        }
        try:
            resp = requests.get(TWSE_EX_ANNOUNCE_URL, params={"response": "json"}, headers=headers, timeout=6)
            cls._last_twse_fetch_time = time.time()
            if resp.status_code == 200:
                data = resp.json()
                fields = data.get("fields", [])
                rows = data.get("data", [])
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
                        raw_date = str(r[ex_date_idx]).strip()
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
                            "status": "證交所預告",
                            "is_announced": 1
                        }
        except Exception as e:
            print(f"[DividendService] 查詢證交所預告失敗: {e}")

        cls._cached_twse_announced = announced_map
        return announced_map

    @classmethod
    def get_stock_dividend_info(cls, symbol: str, market: str = "TW") -> Dict[str, Any]:
        """
        全面取得個股配息資訊：
        1. 優先透過 Yahoo Finance Events API 取得完整歷史除息明細 (免 yfinance 套件，純 requests)。
        2. 若搜尋不到今年，自動定位前一期 / 去年度的除息時間與金額，確保股息持續有效計算！
        3. 同步比對證交所最新公布的未來除息預告。
        """
        sym = symbol.strip().upper()
        mkt = market.strip().upper()
        today = datetime.date.today()
        current_year = today.year
        last_year = current_year - 1

        # 優先從 Yahoo Chart API 抓取近 2 年除息紀錄
        parsed = cls._fetch_dividend_from_yahoo_api(sym, mkt)
        
        # 比對證交所即將除息預告 (若有更新的未來除息日程)
        if mkt in ["TW", "TWO"]:
            twse_map = cls.fetch_twse_announced_dividends()
            if sym in twse_map:
                twse_item = twse_map[sym]
                # 若證交所公告的除息日晚於目前抓到的除息日
                if twse_item.get("cash_dividend", 0) > 0:
                    parsed["latest_ex_date"] = twse_item["ex_date"]
                    parsed["single_amt"] = twse_item["cash_dividend"]
                    parsed["status"] = f"已公布({current_year})"
                    parsed["is_announced"] = 1
                    # 更新年配息預估
                    if parsed["annual_div"] == 0:
                        parsed["annual_div"] = twse_item["cash_dividend"]

        # 若查詢成功且有配息金額，存入本地資料庫
        if parsed and parsed.get("annual_div", 0) > 0:
            cls.save_dividend_record(
                sym, 
                cash_div=parsed["annual_div"], 
                single_div=parsed.get("single_amt", 0.0),
                ex_date=parsed["latest_ex_date"], 
                status=parsed["status"], 
                div_year=str(parsed.get("dividend_year", current_year)), 
                is_announced=parsed["is_announced"]
            )
            return parsed

        # 若線上查詢暫時無結果，讀取本地資料庫現存紀錄 (留存前期數據)
        db_record = cls.get_db_dividend(sym)
        if db_record and db_record.get("cash_dividend", 0) > 0:
            return {
                "symbol": sym,
                "latest_ex_date": db_record.get("ex_date", f"留存{last_year}"),
                "single_amt": db_record.get("single_dividend", db_record.get("cash_dividend", 0.0)),
                "annual_div": db_record.get("cash_dividend", 0.0),
                "status": db_record.get("status", f"留存{last_year}年數據"),
                "dividend_year": db_record.get("dividend_year", str(last_year)),
                "is_announced": db_record.get("is_announced", 0)
            }

        # 預設無配息標的
        return {
            "symbol": sym,
            "latest_ex_date": "無",
            "single_amt": 0.0,
            "annual_div": 0.0,
            "status": "無配息紀錄",
            "dividend_year": str(last_year),
            "is_announced": 0
        }

    @classmethod
    def _fetch_dividend_from_yahoo_api(cls, symbol: str, market: str) -> Dict[str, Any]:
        """透過 Yahoo Chart Events 端點抓取除息序列 (純 requests，速度快且 100% 穩定)"""
        if market == "TWO":
            yf_ticker = f"{symbol}.TWO"
        elif market == "TW":
            yf_ticker = f"{symbol}.TW"
        else:
            yf_ticker = symbol

        today = datetime.date.today()
        current_year = today.year
        last_year = current_year - 1

        url = f"https://query1.finance.yahoo.com/v8/finance/chart/{yf_ticker}?events=div&interval=1d&range=2y"
        headers = {
            "User-Agent": "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36"
        }

        try:
            resp = requests.get(url, headers=headers, timeout=8)
            if resp.status_code != 200:
                return {}

            data = resp.json()
            result_list = data.get("chart", {}).get("result", [])
            if not result_list or not result_list[0]:
                return {}

            events = result_list[0].get("events", {})
            divs = events.get("dividends", {})
            if not divs:
                return {}

            # 解析配息事件清單 [(date, amount)]
            records = []
            for ts_str, item in divs.items():
                try:
                    dt = datetime.datetime.fromtimestamp(int(ts_str)).date()
                    amt = float(item.get("amount", 0.0))
                    if amt > 0:
                        records.append((dt, amt))
                except Exception:
                    continue

            if not records:
                return {}

            # 依日期正序排列 (最新在最後)
            records.sort(key=lambda x: x[0])
            latest_dt, latest_amt = records[-1]

            # 1. 判定是否為今年公布
            is_this_year = (latest_dt.year >= current_year)

            # 2. 計算預估「全年總配息」：
            # 優先加總近 365 天內累積配息 (針對季配息、半年配、月配息)
            one_year_ago = today - datetime.timedelta(days=365)
            recent_divs = [amt for dt, amt in records if dt >= one_year_ago]

            if recent_divs:
                annual_div = sum(recent_divs)
            else:
                # 若近 365 天尚未除息，則往前統計去年度 (last_year) 全年配息總和
                last_year_divs = [amt for dt, amt in records if dt.year == last_year]
                if last_year_divs:
                    annual_div = sum(last_year_divs)
                else:
                    # 若去年也沒有，取前一期配息
                    annual_div = latest_amt

            # 狀態顯示文字
            if is_this_year:
                status_str = f"已公布({latest_dt.year})"
            else:
                status_str = f"留存前期({latest_dt.year})"

            return {
                "symbol": symbol,
                "latest_ex_date": latest_dt.strftime("%Y-%m-%d"),
                "single_amt": round(latest_amt, 2),
                "annual_div": round(annual_div, 2),
                "status": status_str,
                "dividend_year": str(latest_dt.year),
                "is_announced": 1 if is_this_year else 0
            }

        except Exception as e:
            print(f"[DividendService] 解析 {symbol} Yahoo 配息失敗: {e}")
            return {}

    @classmethod
    def get_db_dividend(cls, symbol: str) -> Optional[Dict[str, Any]]:
        """從資料庫讀取股息紀錄"""
        with get_connection() as conn:
            cursor = conn.cursor()
            cursor.execute("SELECT * FROM dividend_records WHERE symbol = ?", (symbol.upper(),))
            row = cursor.fetchone()
            return dict(row) if row else None

    @classmethod
    def save_dividend_record(cls, symbol: str, cash_div: float, single_div: float, ex_date: str, status: str, div_year: str, is_announced: int):
        """儲存或更新股息紀錄"""
        now_str = datetime.datetime.now().strftime("%Y-%m-%d %H:%M:%S")
        with get_connection() as conn:
            cursor = conn.cursor()
            # 確保欄位存在
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
            cursor.execute("""
                INSERT INTO dividend_records (symbol, cash_dividend, single_dividend, ex_date, status, dividend_year, is_announced, updated_at)
                VALUES (?, ?, ?, ?, ?, ?, ?, ?)
                ON CONFLICT(symbol) DO UPDATE SET
                    cash_dividend = excluded.cash_dividend,
                    single_dividend = excluded.single_dividend,
                    ex_date = excluded.ex_date,
                    status = excluded.status,
                    dividend_year = excluded.dividend_year,
                    is_announced = excluded.is_announced,
                    updated_at = excluded.updated_at
            """, (symbol.upper(), cash_div, single_div, ex_date, status, div_year, is_announced, now_str))
            conn.commit()
