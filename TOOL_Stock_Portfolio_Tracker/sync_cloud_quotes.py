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

# Top indicator stocks and ETFs to keep fresh with exact market prefix (tse / otc)
TARGET_CONFIG = [
    ('tse', '2330'), ('tse', '2317'), ('tse', '2454'), ('tse', '2382'),
    ('tse', '2881'), ('tse', '2882'), ('tse', '2886'), ('tse', '2891'),
    ('tse', '2883'), ('tse', '2812'),
    ('tse', '0050'), ('tse', '0056'), ('tse', '006208'), ('tse', '00878'),
    ('tse', '00919'), ('tse', '00929'), ('tse', '00713'), ('tse', '00918'),
    ('tse', '00915'), ('tse', '00940'), ('tse', '00934'), ('otc', '00936'),
    ('otc', '00679B'), ('otc', '00687B'), ('otc', '00937B'), ('otc', '00933B'),
    ('tse', '00905')
]

def fetch_twse_quotes(target_config):
    """從證交所 MIS 抓取最新盤中成交價與昨收價"""
    quotes = {}
    now_str = datetime.datetime.now().strftime("%Y-%m-%d %H:%M:%S")

    ex_list = [f"{mkt}_{sym}.{'tw' if mkt == 'tse' else 'two'}" for mkt, sym in target_config]

    # 分批查詢 (每批 20 檔，避免超時)
    batch_size = 20
    for i in range(0, len(ex_list), batch_size):
        chunk = ex_list[i:i+batch_size]
        ex_ch = "|".join(chunk)
        url = f"https://mis.twse.com.tw/stock/api/getStockInfo.jsp?ex_ch={ex_ch}&json=1&delay=0"
        try:
            req = urllib.request.Request(url, headers={'User-Agent': 'Mozilla/5.0'})
            res = urllib.request.urlopen(req, timeout=8)
            data = json.loads(res.read().decode('utf-8'))
            msg_arr = data.get("msgArray", [])
            for item in msg_arr:
                c = item.get("c", "").strip().upper()
                if not c:
                    continue
                z_str = item.get("z", "-") # 即時成交價
                trade_dict = item.get("trade", {}) or {}
                trade_z = trade_dict.get("z", "-")
                y_str = item.get("y", "-") # 昨收價
                b_str = item.get("b", "")  # 買一價
                a_str = item.get("a", "")  # 賣一價
                name = item.get("n", c)
                
                curr_price = 0.0
                if z_str != "-":
                    try: curr_price = float(z_str)
                    except ValueError: pass
                if curr_price <= 0 and trade_z != "-":
                    try: curr_price = float(trade_z)
                    except ValueError: pass
                if curr_price <= 0 and b_str:
                    try: curr_price = float(b_str.split("_")[0])
                    except ValueError: pass
                if curr_price <= 0 and a_str:
                    try: curr_price = float(a_str.split("_")[0])
                    except ValueError: pass
                if curr_price <= 0 and y_str != "-":
                    try: curr_price = float(y_str)
                    except ValueError: pass
                
                y_close = 0.0
                if y_str != "-":
                    try: y_close = float(y_str)
                    except ValueError: pass

                # 解析時間戳
                t_str = item.get("t", "")
                d_str = item.get("d", "")
                item_time = now_str
                if d_str and t_str and len(d_str) == 8:
                    item_time = f"{d_str[:4]}-{d_str[4:6]}-{d_str[6:]} {t_str}"

                if curr_price > 0:
                    quotes[c] = {
                        "price": curr_price,
                        "yesterday_close": y_close if y_close > 0 else curr_price,
                        "name": name,
                        "time": item_time
                    }
        except Exception as e:
            print(f"Error fetching batch: {e}")
            
    return quotes

def main():
    print(f"Starting cloud market quotes sync at {datetime.datetime.now()}...")
    quotes = fetch_twse_quotes(TARGET_CONFIG)
    print(f"Fetched {len(quotes)} quotes.")
    if not quotes:
        print("No quotes fetched, exiting.")
        return

    # 寫入 Firebase RTDB
    endpoint = f"{RTDB_URL}/stats/quotes.json"
    req = urllib.request.Request(
        endpoint,
        data=json.dumps(quotes).encode('utf-8'),
        headers={'Content-Type': 'application/json'},
        method='PATCH' # 使用 PATCH 只更新對應標的，不抹平其他標的
    )
    try:
        res = urllib.request.urlopen(req, timeout=10)
        print(f"Successfully pushed to Firebase RTDB /stats/quotes.json (Status: {res.status})")
    except Exception as e:
        print(f"Failed to push to Firebase RTDB: {e}")

if __name__ == "__main__":
    main()
