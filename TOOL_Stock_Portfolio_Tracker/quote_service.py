import time
import requests
import json
from typing import Dict, List, Any, Optional

try:
    import yfinance as yf
    HAS_YFINANCE = True
except ImportError:
    HAS_YFINANCE = False

from database import get_last_trading_day_quote

# 證交所即時查詢端點
TWSE_MIS_URL = "https://mis.twse.com.tw/stock/api/getStockInfo.jsp"

class QuoteService:
    def __init__(self):
        self.session = requests.Session()
        self.session.headers.update({
            "User-Agent": "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36",
            "Accept": "application/json, text/javascript, */*; q=0.01",
            "Accept-Language": "zh-TW,zh;q=0.9,en-US;q=0.8,en;q=0.7",
            "Referer": "https://mis.twse.com.tw/stock/fibest.jsp",
            "Connection": "keep-alive"
        })
        self._last_twse_request_time = 0.0
        self._min_interval = 4.0 # 防止高頻被擋，至少間隔 4 秒

    def fetch_realtime_quotes(self, positions: List[Dict[str, Any]]) -> Dict[str, Dict[str, Any]]:
        """
        批次取得持股即時報價
        回傳字典 key 為 symbol (大寫)，value 為行情資訊
        """
        results = {}
        if not positions:
            return results

        tw_symbols = [] # 台股清單
        us_symbols = [] # 美股清單

        for pos in positions:
            sym = pos["symbol"].strip().upper()
            mkt = pos.get("market", "TW").strip().upper()
            if mkt in ["TW", "TWO"]:
                tw_symbols.append((sym, mkt))
            else:
                us_symbols.append(sym)

        # 1. 抓取台股即時報價 (TWSE MIS 批次查詢)
        if tw_symbols:
            tw_quotes = self._fetch_twse_batch(tw_symbols)
            results.update(tw_quotes)

        # 2. 抓取美股即時報價 (需 yfinance)
        if us_symbols and HAS_YFINANCE:
            us_quotes = self._fetch_us_batch(us_symbols)
            results.update(us_quotes)

        return results

    def _fetch_twse_batch(self, symbols: List[tuple]) -> Dict[str, Dict[str, Any]]:
        """
        批次抓取台股 (上市/上櫃) 即時盤中行情
        一次請求查多檔，防爬蟲最安全！
        若非交易日或未開盤 (無成交價 z)，則自動保留最後一個交易日的交易狀況與漲跌。
        """
        quotes = {}
        # 防高頻調用
        now = time.time()
        elapsed = now - self._last_twse_request_time
        if elapsed < self._min_interval:
            time.sleep(self._min_interval - elapsed)

        # 構造 ex_ch 參數，例如 tse_2330.tw|otc_6547.tw
        ch_list = []
        for sym, mkt in symbols:
            prefix = "otc" if mkt == "TWO" else "tse"
            ch_list.append(f"{prefix}_{sym}.tw")
        
        ex_ch = "|".join(ch_list)
        params = {
            "ex_ch": ex_ch,
            "json": "1",
            "delay": "0",
            "_": str(int(time.time() * 1000))
        }

        try:
            resp = self.session.get(TWSE_MIS_URL, params=params, timeout=6)
            self._last_twse_request_time = time.time()

            if resp.status_code == 200:
                data = resp.json()
                msg_array = data.get("msgArray", [])
                
                for item in msg_array:
                    c = item.get("c") # 股票代碼
                    n = item.get("n", "") # 股票簡稱
                    y = float(item.get("y", 0.0)) # 昨收價 (或前一交易日收盤價)
                    
                    # 成交價 z (盤中真實撮合成交價)
                    z_str = item.get("z", "-")
                    has_realtime_trade = (z_str != "-" and z_str != "")

                    if has_realtime_trade:
                        # === 盤中正在交易中 ===
                        current_price = float(z_str)
                        yesterday_close = y
                        open_p = float(item.get("o", 0.0)) if item.get("o", "-") != "-" else y
                        high_p = float(item.get("h", 0.0)) if item.get("h", "-") != "-" else y
                        low_p = float(item.get("l", 0.0)) if item.get("l", "-") != "-" else y
                        vol = int(item.get("v", 0)) if item.get("v", "-") != "-" else 0
                        t_time = item.get("t", "")

                        change = current_price - yesterday_close if yesterday_close > 0 else 0.0
                        change_pct = (change / yesterday_close * 100) if yesterday_close > 0 else 0.0
                    else:
                        # === 非交易日、休市或開盤前 (尚未有當日成交撮合) ===
                        # 依據規範保留「最後一個交易日」的交易狀況 (收盤價、漲跌金額與幅度)
                        last_q = get_last_trading_day_quote(c)
                        if last_q:
                            current_price = y if y > 0 else last_q["current_price"]
                            yesterday_close = last_q["yesterday_close"]
                            change = last_q["change"]
                            change_pct = last_q["change_pct"]
                            open_p = last_q["open"]
                            high_p = last_q["high"]
                            low_p = last_q["low"]
                            vol = last_q["volume"]
                            t_time = f"非交易日/盤前 (保留 {last_q['date']} 交易)"
                        else:
                            # 資料庫尚無歷史日K，嘗試以昨收為現價
                            current_price = y
                            yesterday_close = y
                            open_p = y
                            high_p = y
                            low_p = y
                            vol = 0
                            change = 0.0
                            change_pct = 0.0
                            t_time = "休市/無歷史資料"

                    quotes[c.upper()] = {
                        "symbol": c.upper(),
                        "name": n,
                        "current_price": current_price,
                        "yesterday_close": yesterday_close,
                        "open": open_p,
                        "high": high_p,
                        "low": low_p,
                        "volume": vol,
                        "change": round(change, 2),
                        "change_pct": round(change_pct, 2),
                        "time": t_time,
                        "status": "OK"
                    }
        except Exception as e:
            print(f"[QuoteService] TWSE MIS 查詢異常: {e}")
            if HAS_YFINANCE:
                print("[QuoteService] 切換至 yfinance 備援...")
                quotes.update(self._fetch_tw_yfinance_fallback(symbols))

        return quotes

    def _fetch_tw_yfinance_fallback(self, symbols: List[tuple]) -> Dict[str, Dict[str, Any]]:
        """yfinance 備援查詢台股"""
        if not HAS_YFINANCE:
            return {}
        quotes = {}
        for sym, mkt in symbols:
            yf_ticker = f"{sym}.TWO" if mkt == "TWO" else f"{sym}.TW"
            try:
                t = yf.Ticker(yf_ticker)
                fast = t.fast_info
                curr = getattr(fast, "last_price", None) or getattr(fast, "regular_market_previous_close", 0.0)
                prev = getattr(fast, "previous_close", 0.0) or getattr(fast, "regular_market_previous_close", curr)
                if curr and curr > 0:
                    change = curr - prev
                    change_pct = (change / prev * 100) if prev > 0 else 0.0
                    quotes[sym.upper()] = {
                        "symbol": sym.upper(),
                        "name": sym,
                        "current_price": round(float(curr), 2),
                        "yesterday_close": round(float(prev), 2),
                        "open": round(float(getattr(fast, "open", curr)), 2),
                        "high": round(float(getattr(fast, "day_high", curr)), 2),
                        "low": round(float(getattr(fast, "day_low", curr)), 2),
                        "volume": int(getattr(fast, "last_volume", 0) or 0),
                        "change": round(change, 2),
                        "change_pct": round(change_pct, 2),
                        "time": "yfinance",
                        "status": "OK"
                    }
            except Exception as ex:
                print(f"[QuoteService] yfinance 查詢 {yf_ticker} 失敗: {ex}")
        return quotes

    def _fetch_us_batch(self, symbols: List[str]) -> Dict[str, Dict[str, Any]]:
        """抓取美股報價"""
        if not HAS_YFINANCE:
            return {}
        quotes = {}
        for sym in symbols:
            try:
                t = yf.Ticker(sym)
                fast = t.fast_info
                curr = getattr(fast, "last_price", None) or getattr(fast, "regular_market_previous_close", 0.0)
                prev = getattr(fast, "previous_close", 0.0)
                if curr and curr > 0:
                    change = curr - prev if prev else 0.0
                    change_pct = (change / prev * 100) if prev else 0.0
                    quotes[sym.upper()] = {
                        "symbol": sym.upper(),
                        "name": sym,
                        "current_price": round(float(curr), 2),
                        "yesterday_close": round(float(prev), 2),
                        "open": round(float(getattr(fast, "open", curr)), 2),
                        "high": round(float(getattr(fast, "day_high", curr)), 2),
                        "low": round(float(getattr(fast, "day_low", curr)), 2),
                        "volume": int(getattr(fast, "last_volume", 0) or 0),
                        "change": round(change, 2),
                        "change_pct": round(change_pct, 2),
                        "time": "US Market",
                        "status": "OK"
                    }
            except Exception as e:
                print(f"[QuoteService] 美股 {sym} 查詢失敗: {e}")
        return quotes
