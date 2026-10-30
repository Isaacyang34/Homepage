#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
GitHub Actions / Cloud Standalone Quotes Sync
Fetches Taiwan stock/ETF quotes and pushes to Firebase RTDB /stats/quotes.json
Allows mobile web app to display live prices and calculate PnL even when user's PC is OFF.
"""

import sys
import json
import datetime
import urllib.request
import urllib.error

RTDB_URL = "https://my-stock-tracker-2a94e-default-rtdb.asia-southeast1.firebasedatabase.app"

# Top indicator stocks and ETFs to keep fresh
TARGET_SYMBOLS = [
    '2330', '2317', '2454', '2382', '2881', '2882', '2886', '2891', '2883', '2812',
    '0050', '0056', '006208', '00878', '00919', '00929', '00713', '00918', '00915',
    '00940', '00934', '00936', '00679B', '00687B', '00937B', '00933B', '00905'
]

def fetch_twse_quotes(symbols):
    """從證交所 MIS 抓取最新盤中成交價與昨收價"""
    quotes = {}
    now_str = datetime.datetime.now().strftime("%Y-%m-%d %H:%M:%S")
    
    # 建立 ex_ch 清單
    ex_list = []
    for s in symbols:
        # 大多數股票與 ETF 為 tse
        ex_list.append(f"tse_{s}.tw")
        ex_list.append(f"otc_{s}.two")

    # 分批查詢 (每批 20 檔)
    batch_size = 30
    for i in range(0, len(ex_list), batch_size):
        chunk = ex_list[i:i+batch_size]
        ex_ch = "|".join(chunk)
        url = f"https://mis.twse.com.tw/stock/api/getStockInfo.jsp?ex_ch={ex_ch}&json=1&delay=0"
        try:
            req = urllib.request.Request(url, headers={'User-Agent': 'Mozilla/5.0'})
            res = urllib.request.urlopen(req, timeout=6)
            data = json.loads(res.read().decode('utf-8'))
            msg_arr = data.get("msgArray", [])
            for item in msg_arr:
                c = item.get("c", "").strip().upper()
                if not c:
                    continue
                z_str = item.get("z", "-") # 成交價
                y_str = item.get("y", "-") # 昨收價
                b_str = item.get("b", "")  # 買一價
                name = item.get("n", c)
                
                curr_price = 0.0
                if z_str != "-":
                    try: curr_price = float(z_str)
                    except ValueError: pass
                if curr_price <= 0 and b_str:
                    try: curr_price = float(b_str.split("_")[0])
                    except ValueError: pass
                if curr_price <= 0 and y_str != "-":
                    try: curr_price = float(y_str)
                    except ValueError: pass
                
                y_close = 0.0
                if y_str != "-":
                    try: y_close = float(y_str)
                    except ValueError: pass

                if curr_price > 0:
                    quotes[c] = {
                        "price": curr_price,
                        "yesterday_close": y_close if y_close > 0 else curr_price,
                        "name": name,
                        "time": now_str
                    }
        except Exception as e:
            print(f"Error fetching batch: {e}")
            
    return quotes

def main():
    print(f"Starting cloud market quotes sync at {datetime.datetime.now()}...")
    quotes = fetch_twse_quotes(TARGET_SYMBOLS)
    print(f"Fetched {len(quotes)} quotes.")
    if not quotes:
        print("No quotes fetched, exiting.")
        return

    # 寫入 Firebase RTDB
    endpoint = f"{RTDB_URL}/stats/quotes.json"
    req = urllib.request.Request(
        endpoint,
        data=json.dumps(quotes).encode('utf-8'),
        method='PATCH' # 使用 PATCH 只更新對應標的，不抹平其他標的
    )
    try:
        res = urllib.request.urlopen(req, timeout=8)
        print(f"Successfully pushed to Firebase RTDB /stats/quotes.json (Status: {res.status})")
    except Exception as e:
        print(f"Failed to push to Firebase RTDB: {e}")
        sys.exit(1)

if __name__ == "__main__":
    main()
