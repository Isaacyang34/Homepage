import re
import requests
from typing import Dict, Any

TWSE_MIS_DETECT_URL = "https://mis.twse.com.tw/stock/api/getStockInfo.jsp"
YAHOO_CHART_BASE = "https://query1.finance.yahoo.com/v8/finance/chart/{symbol}"

# 常見熱門標的名稱反向字典 (供使用者輸入中文名稱時快速定位)
NAME_TO_SYMBOL_MAP = {
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
    "元大台灣50": "0050",
    "台灣50": "0050",
    "元大高股息": "0056",
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
    "元大台灣50反1": "00632R",
    "富邦上証正2": "00633L",
    "富邦上証反1": "00634R",
    "元大S&P原油反1": "00673R",
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
    支援多元輸入：
      - 純代碼：'00878', '2330', '00637L', 'AAPL'
      - 代碼+名稱：'00878 國泰永續高股息', '2330 台積電'
      - 純名稱：'台積電', '國泰永續高股息'
      - 帶後綴代碼：'2330.TW', '00878.TWO'
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

    # 4. 判斷美股 (純英文字母)
    if symbol.isalpha():
        # 向 Yahoo Finance 驗證該美股代碼是否確實存在
        valid_us = False
        name_us = symbol
        try:
            headers_yf = {"User-Agent": "Mozilla/5.0 (Windows NT 10.0; Win64; x64)"}
            r = requests.get(YAHOO_CHART_BASE.format(symbol=symbol) + "?interval=1d&range=1d", headers=headers_yf, timeout=3)
            if r.status_code == 200:
                chart = r.json().get("chart", {}).get("result")
                if chart and len(chart) > 0:
                    valid_us = True
                    meta = chart[0].get("meta", {})
                    name_us = meta.get("shortName") or meta.get("longName") or symbol
        except Exception:
            pass

        if valid_us:
            return {
                "symbol": symbol,
                "name": name_us,
                "market": "US",
                "is_etf": 0,
                "valid": True,
                "display_info": f"{symbol} {name_us} (美股 US)"
            }
        else:
            return {
                "symbol": symbol,
                "name": symbol,
                "market": "US",
                "is_etf": 0,
                "valid": False,
                "display_info": f"⚠ 查無此美股標的代碼 [{symbol}]"
            }

    # 5. 台股判斷 (向 TWSE MIS 真實驗證是否存在此股票或 ETF)
    # 台股 ETF 規則初步預判
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
        resp = requests.get(url, headers=headers, timeout=4)
        if resp.status_code == 200:
            data = resp.json()
            msg_arr = data.get("msgArray", [])
            for item in msg_arr:
                item_c = item.get("c", "").strip().upper()
                item_n = item.get("n", "").strip()
                # 必須有代碼與真實中文名稱才算查有此標的
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

    # 若 TWSE MIS 查無，再向 Yahoo Finance 備援確認台股 (.TW / .TWO)
    if not found_in_market:
        for suffix, m_type in [(".TW", "TW"), (".TWO", "TWO")]:
            try:
                yf_ticker = f"{symbol}{suffix}"
                headers_yf = {"User-Agent": "Mozilla/5.0 (Windows NT 10.0; Win64; x64)"}
                r = requests.get(YAHOO_CHART_BASE.format(symbol=yf_ticker) + "?interval=1d&range=1d", headers=headers_yf, timeout=3)
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
        return {
            "symbol": symbol,
            "name": name,
            "market": market,
            "is_etf": is_etf,
            "valid": True,
            "display_info": f"{symbol} {name} ({mkt_text}{etf_text})"
        }
    else:
        # 查無此標的，誠實標記無效，絕不虛假宣稱已辨識！
        return {
            "symbol": symbol,
            "name": "",
            "market": "TW",
            "is_etf": is_etf,
            "valid": False,
            "display_info": f"⚠ 查無此股票或 ETF 代碼 [{symbol}] (請核對是否輸入正確)"
        }
