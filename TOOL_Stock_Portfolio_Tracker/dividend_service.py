import datetime
import time
import requests
from typing import Dict, List, Any, Optional
from database import get_connection, get_trade_lots

# 證交所最新除權除息預告表
TWSE_EX_ANNOUNCE_URL = "https://www.twse.com.tw/rwd/zh/exRight/TWT48U"

class DividendService:
    _cached_twse_announced: Optional[Dict[str, Dict[str, Any]]] = None
    _last_twse_fetch_time = 0.0
    _raw_div_events_cache: Dict[str, List[tuple]] = {} # symbol -> [(date, amount)]

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
        1. 優先透過 Yahoo Finance Events API 取得完整歷史除息明細。
        2. 若搜尋不到今年，自動定位前一期 / 去年度的除息時間與金額。
        3. 自動分析配息頻率 (月/季/半年/年配) 與預估實際發放月份。
        4. 比對本地交易批次 (trade_lots)，依取得時間精算累計已領歷史股息！
        """
        sym = symbol.strip().upper()
        mkt = market.strip().upper()
        today = datetime.date.today()
        current_year = today.year
        last_year = current_year - 1

        # 1. 抓取 Yahoo 近 2 年除息序列
        parsed = cls._fetch_dividend_from_yahoo_api(sym, mkt)
        
        # 2. 比對證交所即將除息預告 (若有最新未來除息排程)
        if mkt in ["TW", "TWO"]:
            twse_map = cls.fetch_twse_announced_dividends()
            if sym in twse_map:
                twse_item = twse_map[sym]
                if twse_item.get("cash_dividend", 0) > 0:
                    parsed["latest_ex_date"] = twse_item["ex_date"]
                    parsed["single_amt"] = twse_item["cash_dividend"]
                    parsed["status"] = f"已公布({current_year})"
                    parsed["is_announced"] = 1
                    if parsed.get("annual_div", 0) == 0:
                        parsed["annual_div"] = twse_item["cash_dividend"]

        # 3. 儲存至資料庫
        if parsed and parsed.get("annual_div", 0) > 0:
            cls.save_dividend_record(
                sym, 
                cash_div=parsed["annual_div"], 
                single_div=parsed.get("single_amt", 0.0),
                ex_date=parsed["latest_ex_date"], 
                payment_month=parsed.get("payment_month", ""),
                frequency=parsed.get("frequency", "年配"),
                status=parsed["status"], 
                div_year=str(parsed.get("dividend_year", current_year)), 
                is_announced=parsed["is_announced"]
            )
        else:
            # 嘗試讀取本地歷史紀錄
            db_record = cls.get_db_dividend(sym)
            if db_record and db_record.get("cash_dividend", 0) > 0:
                parsed = {
                    "symbol": sym,
                    "latest_ex_date": db_record.get("ex_date", f"留存{last_year}"),
                    "single_amt": db_record.get("single_dividend", db_record.get("cash_dividend", 0.0)),
                    "annual_div": db_record.get("cash_dividend", 0.0),
                    "payment_month": db_record.get("payment_month", ""),
                    "frequency": db_record.get("frequency", "年配"),
                    "status": db_record.get("status", f"留存{last_year}年數據"),
                    "dividend_year": db_record.get("dividend_year", str(last_year)),
                    "is_announced": db_record.get("is_announced", 0)
                }
            else:
                parsed = {
                    "symbol": sym,
                    "latest_ex_date": "無",
                    "single_amt": 0.0,
                    "annual_div": 0.0,
                    "payment_month": "--",
                    "frequency": "--",
                    "status": "無配息紀錄",
                    "dividend_year": str(last_year),
                    "is_announced": 0
                }

        # 4. 根據「股票取得時間批次 (trade_lots)」精算歷史已領股息
        lots = get_trade_lots(sym)
        hist_div_total, lot_calc_count = cls.calc_historical_received(sym, lots)
        parsed["hist_div_received"] = hist_div_total
        parsed["lot_count"] = len(lots)

        return parsed

    @classmethod
    def _fetch_dividend_from_yahoo_api(cls, symbol: str, market: str) -> Dict[str, Any]:
        """透過 Yahoo Chart Events 端點抓取除息序列並分析週期頻率與發放月份"""
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

            records.sort(key=lambda x: x[0])
            cls._raw_div_events_cache[symbol] = records
            latest_dt, latest_amt = records[-1]

            is_this_year = (latest_dt.year >= current_year)

            # 計算近 365 天配息次數與加總
            one_year_ago = today - datetime.timedelta(days=365)
            recent_records = [(dt, amt) for dt, amt in records if dt >= one_year_ago]
            recent_count = len(recent_records)

            if recent_records:
                annual_div = sum(amt for dt, amt in recent_records)
            else:
                last_year_records = [(dt, amt) for dt, amt in records if dt.year == last_year]
                recent_count = len(last_year_records)
                if last_year_records:
                    annual_div = sum(amt for dt, amt in last_year_records)
                else:
                    annual_div = latest_amt

            # 分析配息頻率 (月配 / 季配 / 半年配 / 年配)
            if recent_count >= 10:
                freq = "月配"
            elif recent_count >= 3:
                freq = "季配"
            elif recent_count == 2:
                freq = "半年配"
            else:
                freq = "年配"

            # 預估實際發放月份 (通常為除息日次月)
            p_month = (latest_dt.month % 12) + 1
            payment_month = f"{p_month}月發放"

            # 狀態顯示
            if is_this_year:
                status_str = f"已公布({latest_dt.year})"
            else:
                status_str = f"留存前期({latest_dt.year})"

            return {
                "symbol": symbol,
                "latest_ex_date": latest_dt.strftime("%Y-%m-%d"),
                "single_amt": round(latest_amt, 2),
                "annual_div": round(annual_div, 2),
                "frequency": freq,
                "payment_month": payment_month,
                "status": status_str,
                "dividend_year": str(latest_dt.year),
                "is_announced": 1 if is_this_year else 0
            }

        except Exception as e:
            print(f"[DividendService] 解析 {symbol} 配息失敗: {e}")
            return {}

    @classmethod
    def calc_historical_received(cls, symbol: str, trade_lots: List[Dict[str, Any]]) -> tuple:
        """
        依據買入取得時間批次，計算歷年已領取歷史現金股利總額
        若無任何取得時間明細，回傳 (0.0, 0)
        """
        if not trade_lots:
            return 0.0, 0

        events = cls._raw_div_events_cache.get(symbol)
        if not events:
            # 嘗試重新取得
            cls._fetch_dividend_from_yahoo_api(symbol, "TW")
            events = cls._raw_div_events_cache.get(symbol, [])

        if not events:
            return 0.0, len(trade_lots)

        today = datetime.date.today()
        total_received = 0.0

        for ex_dt, div_amt in events:
            # 只能計算已經除息 (歷史) 的事件
            if ex_dt > today:
                continue

            ex_date_str = ex_dt.strftime("%Y-%m-%d")
            # 統計在該次除息日前已經買入取得的總股數
            eligible_shares = sum(
                l["shares"] for l in trade_lots 
                if l["acquire_date"] <= ex_date_str
            )
            if eligible_shares > 0:
                total_received += eligible_shares * div_amt

        return round(total_received), len(trade_lots)

    @classmethod
    def get_db_dividend(cls, symbol: str) -> Optional[Dict[str, Any]]:
        with get_connection() as conn:
            cursor = conn.cursor()
            cursor.execute("SELECT * FROM dividend_records WHERE symbol = ?", (symbol.upper(),))
            row = cursor.fetchone()
            return dict(row) if row else None

    @classmethod
    def save_dividend_record(cls, symbol: str, cash_div: float, single_div: float, ex_date: str, 
                             payment_month: str, frequency: str, status: str, div_year: str, is_announced: int):
        now_str = datetime.datetime.now().strftime("%Y-%m-%d %H:%M:%S")
        with get_connection() as conn:
            cursor = conn.cursor()
            cursor.execute("""
                INSERT INTO dividend_records (symbol, cash_dividend, single_dividend, ex_date, payment_month, frequency, status, dividend_year, is_announced, updated_at)
                VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
                ON CONFLICT(symbol) DO UPDATE SET
                    cash_dividend = excluded.cash_dividend,
                    single_dividend = excluded.single_dividend,
                    ex_date = excluded.ex_date,
                    payment_month = excluded.payment_month,
                    frequency = excluded.frequency,
                    status = excluded.status,
                    dividend_year = excluded.dividend_year,
                    is_announced = excluded.is_announced,
                    updated_at = excluded.updated_at
            """, (symbol.upper(), cash_div, single_div, ex_date, payment_month, frequency, status, div_year, is_announced, now_str))
            conn.commit()
