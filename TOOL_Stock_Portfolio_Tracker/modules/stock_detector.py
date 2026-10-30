# -*- coding: utf-8 -*-
"""
stock_detector.py - 高效能智慧股票與 ETF 代碼/名稱辨識模組
具備：
1. 本地超高速記憶體快取 (0ms 瞬時響應)
2. 常見熱門上市櫃股票與 ETF 權威資料庫 (零網路延遲)
3. 台灣證交所 (TWSE) 與櫃買中心 (TPEX) MIS 即時官方校驗
4. 嚴格標的真實性檢驗，杜絕幽靈不存在代碼 (如 0076, 00673)
"""

import re
import requests
from typing import Dict, Any, Optional

TWSE_MIS_DETECT_URL = "https://mis.twse.com.tw/stock/api/getStockInfo.jsp"
YAHOO_CHART_BASE = "https://query1.finance.yahoo.com/v8/finance/chart/{symbol}"

# 本地執行期高頻記憶體快取 (代碼 -> 辨識結果，0ms 瞬時回傳)
_DETECT_CACHE: Dict[str, Dict[str, Any]] = {}

# 預加載熱門標的權威字典 (上市櫃常備股與主力 ETF，免連線直接秒辨)
PRELOADED_STOCKS: Dict[str, Dict[str, Any]] = {
    # 主流 ETF (正向、反向、槓桿、債券、高股息)
    "0050": {"name": "元大台灣50", "market": "TW", "is_etf": 1},
    "0051": {"name": "元大中型100", "market": "TW", "is_etf": 1},
    "0052": {"name": "富邦科技", "market": "TW", "is_etf": 1},
    "0056": {"name": "元大高股息", "market": "TW", "is_etf": 1},
    "006208": {"name": "富邦台50", "market": "TW", "is_etf": 1},
    "00631L": {"name": "元大台灣50正2", "market": "TW", "is_etf": 1},
    "00632R": {"name": "元大台灣50反1", "market": "TW", "is_etf": 1},
    "00633L": {"name": "富邦上証正2", "market": "TW", "is_etf": 1},
    "00634R": {"name": "富邦上証反1", "market": "TW", "is_etf": 1},
    "00637L": {"name": "元大滬深300正2", "market": "TW", "is_etf": 1},
    "00638R": {"name": "元大滬深300反1", "market": "TW", "is_etf": 1},
    "00679B": {"name": "元大美債20年", "market": "TWO", "is_etf": 1},
    "00687B": {"name": "國泰20年美債", "market": "TWO", "is_etf": 1},
    "00713": {"name": "元大台灣高息低波", "market": "TW", "is_etf": 1},
    "00720B": {"name": "元大投資級公司債", "market": "TWO", "is_etf": 1},
    "00751B": {"name": "元大AAA至A公司債", "market": "TWO", "is_etf": 1},
    "00878": {"name": "國泰永續高股息", "market": "TW", "is_etf": 1},
    "00905": {"name": "FT臺灣Smart", "market": "TW", "is_etf": 1},
    "00915": {"name": "凱基優選高股息30", "market": "TW", "is_etf": 1},
    "00918": {"name": "大華優利高 paw30", "market": "TW", "is_etf": 1},
    "00919": {"name": "群益台灣精選高息", "market": "TW", "is_etf": 1},
    "00929": {"name": "復華台灣科技優息", "market": "TW", "is_etf": 1},
    "00939": {"name": "統一台灣高息動能", "market": "TW", "is_etf": 1},
    "00940": {"name": "元大台灣價值高息", "market": "TW", "is_etf": 1},
    # 核心權值股與高流通股
    "2330": {"name": "台積電", "market": "TW", "is_etf": 0},
    "2317": {"name": "鴻海", "market": "TW", "is_etf": 0},
    "2454": {"name": "聯發科", "market": "TW", "is_etf": 0},
    "2382": {"name": "廣達", "market": "TW", "is_etf": 0},
    "2308": {"name": "台達電", "market": "TW", "is_etf": 0},
    "2303": {"name": "聯電", "market": "TW", "is_etf": 0},
    "2881": {"name": "富邦金", "market": "TW", "is_etf": 0},
    "2882": {"name": "國泰金", "market": "TW", "is_etf": 0},
    "2891": {"name": "中信金", "market": "TW", "is_etf": 0},
    "2886": {"name": "兆豐金", "market": "TW", "is_etf": 0},
    "2884": {"name": "玉山金", "market": "TW", "is_etf": 0},
    "2892": {"name": "第一金", "market": "TW", "is_etf": 0},
    "2880": {"name": "華南金", "market": "TW", "is_etf": 0},
    "2885": {"name": "元大金", "market": "TW", "is_etf": 0},
    "2883": {"name": "開發金", "market": "TW", "is_etf": 0},
    "2887": {"name": "台新金", "market": "TW", "is_etf": 0},
    "2890": {"name": "永豐金", "market": "TW", "is_etf": 0},
    "2812": {"name": "台中銀", "market": "TW", "is_etf": 0},
    "5880": {"name": "合庫金", "market": "TW", "is_etf": 0},
    "3008": {"name": "大立光", "market": "TW", "is_etf": 0},
    "3711": {"name": "日月光投控", "market": "TW", "is_etf": 0},
    "3231": {"name": "緯創", "market": "TW", "is_etf": 0},
    "6669": {"name": "緯穎", "market": "TW", "is_etf": 0},
    "2603": {"name": "長榮", "market": "TW", "is_etf": 0},
    "2609": {"name": "陽明", "market": "TW", "is_etf": 0},
    "2615": {"name": "萬海", "market": "TW", "is_etf": 0},
    # 美股代表標的
    "AAPL": {"name": "Apple Inc.", "market": "US", "is_etf": 0},
    "MSFT": {"name": "Microsoft Corporation", "market": "US", "is_etf": 0},
    "NVDA": {"name": "NVIDIA Corporation", "market": "US", "is_etf": 0},
    "TSLA": {"name": "Tesla, Inc.", "market": "US", "is_etf": 0},
    "GOOGL": {"name": "Alphabet Inc.", "market": "US", "is_etf": 0},
    "AMZN": {"name": "Amazon.com, Inc.", "market": "US", "is_etf": 0},
    "META": {"name": "Meta Platforms, Inc.", "market": "US", "is_etf": 0},
    "BRK.B": {"name": "Berkshire Hathaway Inc.", "market": "US", "is_etf": 0},
    "SPY": {"name": "SPDR S&P 500 ETF Trust", "market": "US", "is_etf": 1},
    "QQQ": {"name": "Invesco QQQ Trust", "market": "US", "is_etf": 1},
    "VOO": {"name": "Vanguard S&P 500 ETF", "market": "US", "is_etf": 1},
    "VTI": {"name": "Vanguard Total Stock Market ETF", "market": "US", "is_etf": 1}
}

# 常見熱門標的名稱反向字典 (供使用者輸入中文名稱時快速定位代碼)
NAME_TO_SYMBOL_MAP: Dict[str, str] = {
    "台積電": "2330",
    "聯發科": "2454",
    "鴻海": "2317",
    "廣達": "2382",
    "台達電": "2308",
    "聯電": "2303",
    "富邦金": "2881",
    "國泰金": "2882",
    "中信金": "2891",
    "兆豐金": "2886",
    "玉山金": "2884",
    "第一金": "2892",
    "台中銀": "2812",
    "合庫金": "5880",
    "元大台灣50": "0050",
    "台灣50": "0050",
    "元大高股息": "0056",
    "富邦台50": "006208",
    "國泰永續高股息": "00878",
    "群益台灣精選高息": "00919",
    "復華台灣科技優息": "00929",
    "元大台灣價值高息": "00940",
    "統一台灣高息動能": "00939",
    "大華優利高 paw30": "00918",
    "凱基優選高股息30": "00915",
    "國泰20年美債": "00687B",
    "元大美債20年": "00679B",
    "元大投資級公司債": "00720B",
    "元大滬深300正2": "00637L",
    "元大滬深300反1": "00638R",
    "元大台灣50反1": "00632R",
    "元大台灣50正2": "00631L",
    "富邦上証正2": "00633L",
    "富邦上証反1": "00634R",
    "蘋果": "AAPL",
    "微軟": "MSFT",
    "輝達": "NVDA",
    "特斯拉": "TSLA",
    "谷歌": "GOOGL",
    "亞馬遜": "AMZN",
    "波克夏": "BRK.B"
}

def detect_stock_metadata(input_text: str) -> Dict[str, Any]:
    """
    智慧自動辨識股票代碼、名稱、市場別與是否為 ETF
    具備本地 0ms 記憶體快取與即時連線雙重防護
    """
    raw = input_text.strip()
    if not raw:
        return {"symbol": "", "name": "", "market": "TW", "is_etf": 0, "valid": False, "display_info": ""}

    # 1. 檢查是否能直接從中文名稱對照表比對
    clean_kw = raw.replace(" ", "").replace("　", "")
    for name_key, sym_val in NAME_TO_SYMBOL_MAP.items():
        if name_key in clean_kw:
            raw = sym_val
            break

    # 2. 去除 .TW / .TWO 等常見尾碼
    cleaned = re.sub(r'\.(TW|TWO|US)$', '', raw, flags=re.IGNORECASE).strip().upper()

    # 3. 提取股票代碼 (台股 4-6 碼數字+可選英文字母，或美股 1-5 碼英文)
    sym_match = re.search(r'\b([0-9]{4,6}[A-Z]?|[A-Z]{1,5})\b', cleaned)
    if sym_match:
        symbol = sym_match.group(1).upper()
    else:
        symbol = cleaned.split()[0].upper()

    # 若代碼長度不足，直接視為未完成輸入
    if len(symbol) < 2:
        return {"symbol": symbol, "name": symbol, "market": "TW", "is_etf": 0, "valid": False, "display_info": "輸入中..."}

    # 4. 優先檢查本地記憶體快取 (0ms 秒開)
    if symbol in _DETECT_CACHE:
        return dict(_DETECT_CACHE[symbol])

    # 5. 檢查預加載熱門標的清單 (0ms 命中)
    if symbol in PRELOADED_STOCKS:
        info = PRELOADED_STOCKS[symbol]
        mkt = info["market"]
        is_etf = info["is_etf"]
        name = info["name"]
        if mkt == "US":
            mkt_text = "美股 (US)"
            etf_text = " - ETF" if is_etf else ""
        else:
            mkt_text = "台股上市 (TW)" if mkt == "TW" else "台股上櫃 (TWO)"
            etf_text = " - ETF 優惠稅率 0.1%" if is_etf else ""
        res = {
            "symbol": symbol,
            "name": name,
            "market": mkt,
            "is_etf": is_etf,
            "valid": True,
            "display_info": f"{symbol} {name} ({mkt_text}{etf_text})"
        }
        _DETECT_CACHE[symbol] = res
        return dict(res)

    # 6. 判斷美股 (純英文字母)
    if symbol.isalpha():
        valid_us = False
        name_us = symbol
        try:
            headers_yf = {"User-Agent": "Mozilla/5.0 (Windows NT 10.0; Win64; x64)"}
            r = requests.get(YAHOO_CHART_BASE.format(symbol=symbol) + "?interval=1d&range=1d", headers=headers_yf, timeout=2.0)
            if r.status_code == 200:
                chart = r.json().get("chart", {}).get("result")
                if chart and len(chart) > 0:
                    valid_us = True
                    meta = chart[0].get("meta", {})
                    name_us = meta.get("shortName") or meta.get("longName") or symbol
        except Exception:
            pass

        if valid_us:
            res = {
                "symbol": symbol,
                "name": name_us,
                "market": "US",
                "is_etf": 0,
                "valid": True,
                "display_info": f"{symbol} {name_us} (美股 US)"
            }
        else:
            res = {
                "symbol": symbol,
                "name": symbol,
                "market": "US",
                "is_etf": 0,
                "valid": False,
                "display_info": f"⚠ 查無此美股標的代碼 [{symbol}]"
            }
        _DETECT_CACHE[symbol] = res
        return dict(res)

    # 7. 台股即時官方校驗 (向 TWSE MIS 查詢是否存在此股票或 ETF)
    is_etf = 1 if (symbol.startswith("00") or symbol.startswith("01") or symbol.startswith("02") or "B" in symbol) else 0

    url = f"{TWSE_MIS_DETECT_URL}?ex_ch=tse_{symbol}.tw|otc_{symbol}.tw&json=1&delay=0"
    headers = {
        "User-Agent": "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36",
        "Referer": "https://mis.twse.com.tw/stock/fibest.jsp"
    }

    found_in_market = False
    name = ""
    market = "TW"

    try:
        resp = requests.get(url, headers=headers, timeout=2.5)
        if resp.status_code == 200:
            data = resp.json()
            msg_arr = data.get("msgArray", [])
            for item in msg_arr:
                item_c = item.get("c", "").strip().upper()
                item_n = item.get("n", "").strip()
                if item_c == symbol and item_n:
                    found_in_market = True
                    name = item_n
                    symbol = item_c
                    ex = item.get("ex", "").strip().lower()
                    market = "TWO" if ex == "otc" else "TW"
                    if "ETF" in name or "反1" in name or "正2" in name or "債" in name or "高息" in name or "息" in name:
                        is_etf = 1
                    break
    except Exception:
        pass

    # 若 TWSE MIS 未查到，進行 Yahoo Finance 備援確認 (縮減超時至 1.2s，避免輸入打字卡死)
    if not found_in_market:
        for suffix, m_type in [(".TW", "TW"), (".TWO", "TWO")]:
            try:
                yf_ticker = f"{symbol}{suffix}"
                headers_yf = {"User-Agent": "Mozilla/5.0 (Windows NT 10.0; Win64; x64)"}
                r = requests.get(YAHOO_CHART_BASE.format(symbol=yf_ticker) + "?interval=1d&range=1d", headers=headers_yf, timeout=1.2)
                if r.status_code == 200:
                    chart = r.json().get("chart", {}).get("result")
                    if chart and len(chart) > 0:
                        meta = chart[0].get("meta", {})
                        if meta.get("regularMarketPrice") or meta.get("chartPreviousClose"):
                            found_in_market = True
                            market = m_type
                            name = meta.get("shortName") or meta.get("longName") or symbol
                            if meta.get("instrumentType") == "ETF":
                                is_etf = 1
                            break
            except Exception:
                pass

    if found_in_market and name:
        mkt_text = "台股上市 (TW)" if market == "TW" else "台股上櫃 (TWO)"
        etf_text = " - ETF 優惠稅率 0.1%" if is_etf else ""
        res = {
            "symbol": symbol,
            "name": name,
            "market": market,
            "is_etf": is_etf,
            "valid": True,
            "display_info": f"{symbol} {name} ({mkt_text}{etf_text})"
        }
    else:
        # 查無此標的，誠實標記無效
        res = {
            "symbol": symbol,
            "name": "",
            "market": "TW",
            "is_etf": is_etf,
            "valid": False,
            "display_info": f"⚠ 查無此股票或 ETF 代碼 [{symbol}] (請核對是否輸入正確)"
        }

    # 寫入執行期記憶體快取
    _DETECT_CACHE[symbol] = res
    return dict(res)
