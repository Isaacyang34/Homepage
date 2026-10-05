import datetime
import time
import requests
from typing import List, Dict, Any, Callable, Optional
from database import get_latest_history_date, save_kline_batch, get_connection

try:
    import yfinance as yf
    HAS_YFINANCE = True
except ImportError:
    HAS_YFINANCE = False

TWSE_STOCK_DAY_URL = "https://www.twse.com.tw/rwd/zh/afterTrading/STOCK_DAY"
TPEX_STOCK_DAY_URL = "https://www.tpex.org.tw/web/stock/aftertrading/daily_trading_info/st43_result.php"

class HistoryService:
    _session = None
    _last_twse_request_time = 0.0

    @classmethod
    def _get_session(cls):
        if cls._session is None:
            cls._session = requests.Session()
            cls._session.headers.update({
                "User-Agent": "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36",
                "Accept": "application/json, text/javascript, */*; q=0.01",
                "Referer": "https://www.twse.com.tw/zh/trading/historical/stock-day.html"
            })
        return cls._session

    @classmethod
    def update_stock_history(cls, symbol: str, market: str, 
                             default_days: int = 180,
                             on_progress: Optional[Callable[[str], None]] = None) -> int:
        """
        增量更新個股歷史日 K 線
        優先使用 yfinance (若有安裝)，或自動使用 TWSE 官方 STOCK_DAY 端點
        """
        sym = symbol.strip().upper()
        mkt = market.strip().upper()
        latest_date_str = get_latest_history_date(sym)
        today = datetime.date.today()

        if on_progress:
            on_progress(f"正在檢查 {sym} 歷史數據...")

        if latest_date_str:
            latest_dt = datetime.datetime.strptime(latest_date_str, "%Y-%m-%d").date()
            if latest_dt >= today:
                if on_progress:
                    on_progress(f"[OK] {sym} 本地資料庫已是最新 ({latest_date_str})，無需更新。")
                return 0

        # 若有安裝 yfinance 優先使用
        if HAS_YFINANCE:
            return cls._update_via_yfinance(sym, mkt, latest_date_str, default_days, on_progress)
        else:
            # 原生官方 TWSE 端點增量更新
            return cls._update_via_twse_official(sym, mkt, latest_date_str, default_days, on_progress)

    @classmethod
    def _update_via_twse_official(cls, symbol: str, market: str, latest_date_str: Optional[str],
                                  default_days: int, on_progress: Optional[Callable[[str], None]] = None) -> int:
        """透過 TWSE 官方 STOCK_DAY 端點下載 (無需額外第三方套件)"""
        today = datetime.date.today()
        session = cls._get_session()

        # 計算需要下載的月份清單
        if latest_date_str:
            start_dt = datetime.datetime.strptime(latest_date_str, "%Y-%m-%d").date()
        else:
            start_dt = today - datetime.timedelta(days=min(default_days, 180)) # 首次預設抓半年

        # 產生月份清單 YYYYMM01
        cur = datetime.date(start_dt.year, start_dt.month, 1)
        months_to_fetch = []
        while cur <= today:
            months_to_fetch.append(cur.strftime("%Y%m01"))
            # 下個月
            year = cur.year + (1 if cur.month == 12 else 0)
            month = 1 if cur.month == 12 else cur.month + 1
            cur = datetime.date(year, month, 1)

        total_saved = 0
        for m_str in months_to_fetch:
            # 節流防止被證交所防護阻擋 (間隔 3.5 秒)
            now = time.time()
            elapsed = now - cls._last_twse_request_time
            if elapsed < 3.5:
                time.sleep(3.5 - elapsed)

            if on_progress:
                on_progress(f"正在從證交所下載 {symbol} {m_str[:4]}年{m_str[4:6]}月 歷史盤後資料...")

            params = {
                "date": m_str,
                "stockNo": symbol,
                "response": "json"
            }
            try:
                resp = session.get(TWSE_STOCK_DAY_URL, params=params, timeout=10)
                cls._last_twse_request_time = time.time()
                if resp.status_code != 200:
                    continue

                data = resp.json()
                if data.get("stat") != "OK":
                    continue

                raw_data = data.get("data", [])
                records = []
                for row in raw_data:
                    # 格式: [日期(民國), 成交股數, 成交金額, 開盤價, 最高價, 最低價, 收盤價, 漲跌價差, 成交筆數]
                    roc_date = row[0].strip() # 113/10/01
                    parts = roc_date.split("/")
                    if len(parts) == 3:
                        ad_year = int(parts[0]) + 1911
                        iso_date = f"{ad_year:04d}-{int(parts[1]):02d}-{int(parts[2]):02d}"
                    else:
                        continue

                    def parse_price(val_str):
                        try:
                            return float(val_str.replace(",", "").replace("--", "0.0").strip())
                        except Exception:
                            return 0.0

                    o_p = parse_price(row[3])
                    h_p = parse_price(row[4])
                    l_p = parse_price(row[5])
                    c_p = parse_price(row[6])
                    vol = int(row[1].replace(",", "").strip()) if row[1] else 0

                    if c_p > 0:
                        records.append({
                            "date": iso_date,
                            "open": o_p,
                            "high": h_p,
                            "low": l_p,
                            "close": c_p,
                            "volume": vol
                        })

                saved = save_kline_batch(symbol, records)
                total_saved += saved
            except Exception as e:
                if on_progress:
                    on_progress(f"下載 {m_str} 異常: {e}")

        if on_progress:
            on_progress(f"[OK] {symbol} 歷史資料更新完成，共更新 {total_saved} 個交易日！")
        return total_saved

    @classmethod
    def _update_via_yfinance(cls, symbol: str, market: str, latest_date_str: Optional[str],
                             default_days: int, on_progress: Optional[Callable[[str], None]] = None) -> int:
        """yfinance 下載引擎"""
        today = datetime.date.today()
        if market == "TW":
            yf_symbol = f"{symbol}.TW"
        elif market == "TWO":
            yf_symbol = f"{symbol}.TWO"
        else:
            yf_symbol = symbol

        if latest_date_str:
            latest_dt = datetime.datetime.strptime(latest_date_str, "%Y-%m-%d").date()
            start_date = (latest_dt + datetime.timedelta(days=1)).strftime("%Y-%m-%d")
        else:
            start_date = (today - datetime.timedelta(days=default_days)).strftime("%Y-%m-%d")

        end_date = (today + datetime.timedelta(days=1)).strftime("%Y-%m-%d")

        if on_progress:
            on_progress(f"正在透過 yfinance 下載 {symbol} [{start_date} ~ {today}]...")

        try:
            ticker = yf.Ticker(yf_symbol)
            df = ticker.history(start=start_date, end=end_date, auto_adjust=False)
            if df.empty:
                if on_progress:
                    on_progress(f"{symbol} 無新增之歷史交易紀錄。")
                return 0

            records = []
            for dt_index, row in df.iterrows():
                date_str = dt_index.strftime("%Y-%m-%d")
                records.append({
                    "date": date_str,
                    "open": round(float(row.get("Open", 0.0)), 2),
                    "high": round(float(row.get("High", 0.0)), 2),
                    "low": round(float(row.get("Low", 0.0)), 2),
                    "close": round(float(row.get("Close", 0.0)), 2),
                    "volume": int(row.get("Volume", 0))
                })

            saved_count = save_kline_batch(symbol, records)
            if on_progress:
                on_progress(f"[OK] {symbol} 成功更新 {saved_count} 筆歷史資料 (至 {records[-1]['date']})")
            return saved_count
        except Exception as e:
            if on_progress:
                on_progress(f"[ERR] yfinance 下載失敗: {e}")
            return 0

    @classmethod
    def update_all_positions_history(cls, positions: List[Dict[str, Any]], 
                                     on_progress: Optional[Callable[[str], None]] = None) -> Dict[str, int]:
        """批次盤後更新所有持股之歷史資料"""
        results = {}
        total = len(positions)
        for i, pos in enumerate(positions, 1):
            sym = pos["symbol"]
            mkt = pos.get("market", "TW")
            if on_progress:
                on_progress(f"[{i}/{total}] 處理中: {sym} ({mkt})")
            count = cls.update_stock_history(sym, mkt, on_progress=on_progress)
            results[sym] = count
        return results
