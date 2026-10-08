# -*- coding: utf-8 -*-
"""
Stock Portfolio Tracker - 即時行情服務模組 (quote_service.py)
純 Python 原生輕量架構：
1. 台股盤中：TWSE MIS 官方即時批次查詢 (低頻安全查詢，支援上市與上櫃)
2. 美股即時與台股回退：Yahoo Finance v8 原生 REST API (純 JSON 解析，徹底移除 yfinance/pandas/numpy)
3. 支援外部 config/api_endpoints.json 動態設定
"""
import time
import requests
import json
import os
from typing import Dict, List, Any, Optional

from database import get_last_trading_day_quote

# 讀取外部端點設定
def get_api_endpoints() -> Dict[str, str]:
    app_dir = os.path.dirname(os.path.abspath(__file__))
    cfg_path = os.path.join(app_dir, "config", "api_endpoints.json")
    if not os.path.exists(cfg_path):
        root_dir = os.path.dirname(os.path.abspath(__file__))
        cfg_path = os.path.join(root_dir, "..", "config", "api_endpoints.json")
    
    if os.path.exists(cfg_path):
        try:
            with open(cfg_path, "r", encoding="utf-8") as f:
                return json.load(f)
        except Exception:
            pass
    return {
        "twse_mis_url": "https://mis.twse.com.tw/stock/api/getStockInfo.jsp",
        "yahoo_chart_url": "https://query1.finance.yahoo.com/v8/finance/chart/{symbol}"
    }

_ENDPOINTS = get_api_endpoints()
TWSE_MIS_URL = _ENDPOINTS.get("twse_mis_url", "https://mis.twse.com.tw/stock/api/getStockInfo.jsp")
YAHOO_CHART_BASE = _ENDPOINTS.get("yahoo_chart_url", "https://query1.finance.yahoo.com/v8/finance/chart/{symbol}")

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

        # 2. 抓取美股即時報價 (原生 Yahoo Finance v8 REST，純 Python 零相依)
        if us_symbols:
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
        now = time.time()
        elapsed = now - self._last_twse_request_time
        if elapsed < self._min_interval:
            time.sleep(self._min_interval - elapsed)

        ch_list = []
        for sym, mkt in symbols:
            prefix = "otc" if mkt == "TWO" else "tse"
            ch_list.append(f"{prefix}_{sym}.tw")
        
        ex_ch = "|".join(ch_list)
        url = f"{TWSE_MIS_URL}?ex_ch={ex_ch}&json=1&delay=0&_={int(now*1000)}"

        try:
            resp = self.session.get(url, timeout=6.0)
            self._last_twse_request_time = time.time()
            if resp.status_code == 200:
                data = resp.json()
                msg_array = data.get("msgArray", [])
                for item in msg_array:
                    sym = item.get("c", "").strip().upper()
                    if not sym:
                        continue
                    
                    z_val = item.get("z", "-")
                    y_val = item.get("y", "-")
                    o_val = item.get("o", "-")
                    h_val = item.get("h", "-")
                    l_val = item.get("l", "-")
                    v_val = item.get("v", "0")
                    name = item.get("n", sym)
                    time_str = item.get("t", "")

                    prev_close = float(y_val) if (y_val and y_val != "-") else 0.0

                    # 依序提取盤中真實成交價 (TWSE MIS 完整撮合支援)
                    curr_price = 0.0

                    # 1. 本盤撮合成交價 (z)
                    if z_val and z_val != "-":
                        try:
                            curr_price = float(z_val)
                        except ValueError:
                            pass

                    # 2. 若本盤無撮合，取盤中最後一筆撮合成交價 (trade.z)
                    if curr_price <= 0.0:
                        trade_obj = item.get("trade")
                        if isinstance(trade_obj, dict):
                            tz_val = trade_obj.get("z", "-")
                            if tz_val and tz_val != "-":
                                try:
                                    curr_price = float(tz_val)
                                except ValueError:
                                    pass

                    # 3. 若無 trade.z，取盤中五檔最佳買價第一檔 (b[0]) 或賣價第一檔 (a[0])
                    if curr_price <= 0.0:
                        b_first = item.get("b", "").split("_")[0]
                        if b_first and b_first != "-":
                            try:
                                curr_price = float(b_first)
                            except ValueError:
                                pass
                    if curr_price <= 0.0:
                        a_first = item.get("a", "").split("_")[0]
                        if a_first and a_first != "-":
                            try:
                                curr_price = float(a_first)
                            except ValueError:
                                pass

                    # 4. 若盤中皆無即時成交價 (未開盤或休市)，才嘗試取歷史收盤價，或昨收價
                    if curr_price <= 0.0:
                        hist_last = get_last_trading_day_quote(sym)
                        if hist_last and hist_last.get("close", 0) > 0:
                            curr_price = float(hist_last["close"])
                            if prev_close <= 0.0:
                                prev_close = float(hist_last.get("yesterday_close") or curr_price)
                        else:
                            curr_price = prev_close if prev_close > 0.0 else 0.0

                    open_price = float(o_val) if (o_val and o_val != "-") else curr_price
                    high_price = float(h_val) if (h_val and h_val != "-") else curr_price
                    low_price = float(l_val) if (l_val and l_val != "-") else curr_price
                    volume = int(v_val) if (v_val and v_val.isdigit()) else 0

                    change = curr_price - prev_close if prev_close > 0 else 0.0
                    change_pct = (change / prev_close * 100) if prev_close > 0 else 0.0

                    quotes[sym] = {
                        "symbol": sym,
                        "name": name,
                        "current_price": round(curr_price, 2),
                        "yesterday_close": round(prev_close, 2),
                        "open": round(open_price, 2),
                        "high": round(high_price, 2),
                        "low": round(low_price, 2),
                        "volume": volume,
                        "change": round(change, 2),
                        "change_pct": round(change_pct, 2),
                        "time": time_str,
                        "status": "OK" if curr_price > 0 else "OFFLINE"
                    }
        except Exception as e:
            print(f"[QuoteService] TWSE MIS 查詢失敗: {e}")

        # 若證交所連線異常，回退到 Yahoo Finance v8 原生查詢
        missing = [sym for sym, _ in symbols if sym not in quotes or quotes[sym].get("status") != "OK"]
        if missing:
            print(f"[QuoteService] 證交所離線，透過 Yahoo 原生 REST 回退查詢: {missing}")
            quotes.update(self._fetch_tw_yahoo_fallback(missing))

        return quotes

    def _fetch_tw_yahoo_fallback(self, symbols: List[str]) -> Dict[str, Dict[str, Any]]:
        """Yahoo Finance v8 原生 JSON 回退查詢台股 (純 Python 零相依)"""
        quotes = {}
        for sym in symbols:
            for suffix in [".TW", ".TWO"]:
                yf_ticker = f"{sym}{suffix}"
                url = YAHOO_CHART_BASE.format(symbol=yf_ticker) + "?interval=1d&range=1d"
                try:
                    r = self.session.get(url, timeout=4.0)
                    if r.status_code == 200:
                        res = r.json().get("chart", {}).get("result")
                        if res and len(res) > 0:
                            meta = res[0].get("meta", {})
                            curr = meta.get("regularMarketPrice") or meta.get("chartPreviousClose", 0.0)
                            prev = meta.get("chartPreviousClose", curr)
                            if curr and curr > 0:
                                change = curr - prev if prev else 0.0
                                change_pct = (change / prev * 100) if prev else 0.0
                                quotes[sym.upper()] = {
                                    "symbol": sym.upper(),
                                    "name": sym,
                                    "current_price": round(float(curr), 2),
                                    "yesterday_close": round(float(prev), 2),
                                    "open": round(float(meta.get("regularMarketDayHigh", curr)), 2),
                                    "high": round(float(meta.get("regularMarketDayHigh", curr)), 2),
                                    "low": round(float(meta.get("regularMarketDayLow", curr)), 2),
                                    "volume": int(meta.get("regularMarketVolume", 0) or 0),
                                    "change": round(change, 2),
                                    "change_pct": round(change_pct, 2),
                                    "time": "Yahoo",
                                    "status": "OK"
                                }
                                break
                except Exception:
                    pass
        return quotes

    def _fetch_us_batch(self, symbols: List[str]) -> Dict[str, Dict[str, Any]]:
        """抓取美股即時報價 (純 Python 呼叫 Yahoo v8 REST，零套件相依)"""
        quotes = {}
        for sym in symbols:
            url = YAHOO_CHART_BASE.format(symbol=sym) + "?interval=1d&range=1d"
            try:
                r = self.session.get(url, timeout=5.0)
                if r.status_code == 200:
                    res = r.json().get("chart", {}).get("result")
                    if res and len(res) > 0:
                        meta = res[0].get("meta", {})
                        curr = meta.get("regularMarketPrice") or meta.get("chartPreviousClose", 0.0)
                        prev = meta.get("chartPreviousClose", curr)
                        if curr and curr > 0:
                            change = curr - prev if prev else 0.0
                            change_pct = (change / prev * 100) if prev else 0.0
                            quotes[sym.upper()] = {
                                "symbol": sym.upper(),
                                "name": sym,
                                "current_price": round(float(curr), 2),
                                "yesterday_close": round(float(prev), 2),
                                "open": round(float(meta.get("regularMarketDayHigh", curr)), 2),
                                "high": round(float(meta.get("regularMarketDayHigh", curr)), 2),
                                "low": round(float(meta.get("regularMarketDayLow", curr)), 2),
                                "volume": int(meta.get("regularMarketVolume", 0) or 0),
                                "change": round(change, 2),
                                "change_pct": round(change_pct, 2),
                                "time": "US Market",
                                "status": "OK"
                            }
            except Exception as e:
                print(f"[QuoteService] 美股 {sym} 查詢失敗: {e}")
        return quotes
