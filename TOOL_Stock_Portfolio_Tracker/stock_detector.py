import re
import requests
from typing import Dict, Any

TWSE_MIS_DETECT_URL = "https://mis.twse.com.tw/stock/api/getStockInfo.jsp"

# 常見熱門標的名稱反查字典 (當使用者僅輸入中文名稱時直接秒查)
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
    "大華優利高填息30": "00918",
    "凱基優選高股息30": "00915",
    "國泰20年美債": "00687B",
    "元大美債20年": "00679B",
    "元大投資級公司債": "00720B",
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
      - 純代碼：'00878', '2330', 'AAPL'
      - 代碼+名稱：'00878 國泰永續高股息', '2330 台積電'
      - 純名稱：'台積電', '國泰永續高股息'
      - 帶後綴代碼：'2330.TW', '00878.TWO'
    """
    raw = input_text.strip()
    if not raw:
        return {"symbol": "", "name": "", "market": "TW", "is_etf": 0, "display_info": ""}

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
        # 若無法 regex 拆出，保留前半段或全部
        symbol = cleaned.split()[0].upper()

    # 4. 判斷美股 (純英文字母)
    if symbol.isalpha():
        return {
            "symbol": symbol,
            "name": symbol,
            "market": "US",
            "is_etf": 0,
            "display_info": f"{symbol} | 美股 (US)"
        }

    # 5. 判斷台股 ETF 規則 (代碼以 00, 01, 02 開頭，或含 B/債)
    is_etf = 1 if (symbol.startswith("00") or symbol.startswith("01") or symbol.startswith("02") or "B" in symbol) else 0

    # 6. 向 TWSE MIS 查詢確認上市 (tse) 或上櫃 (otc) 與真實公司名稱
    url = f"{TWSE_MIS_DETECT_URL}?ex_ch=tse_{symbol}.tw|otc_{symbol}.tw&json=1&delay=0"
    headers = {
        "User-Agent": "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36",
        "Referer": "https://mis.twse.com.tw/stock/fibest.jsp"
    }

    name = symbol
    market = "TW"

    try:
        resp = requests.get(url, headers=headers, timeout=4)
        if resp.status_code == 200:
            data = resp.json()
            msg_arr = data.get("msgArray", [])
            for item in msg_arr:
                item_n = item.get("n", "").strip()
                if item_n:
                    name = item_n
                    symbol = item.get("c", symbol).strip().upper()
                    ex = item.get("ex", "").strip().lower()
                    market = "TWO" if ex == "otc" else "TW"
                    if "ETF" in name or "反1" in name or "正2" in name or "債" in name or "高息" in name or "息" in name:
                        is_etf = 1
                    break
    except Exception:
        pass

    mkt_text = "台股上市 (TW)" if market == "TW" else ("台股上櫃 (TWO)" if market == "TWO" else "美股 (US)")
    etf_text = " - ETF 優惠稅率 0.1%" if is_etf else ""
    display_info = f"{symbol} {name} ({mkt_text}{etf_text})"

    return {
        "symbol": symbol,
        "name": name,
        "market": market,
        "is_etf": is_etf,
        "display_info": display_info
    }

