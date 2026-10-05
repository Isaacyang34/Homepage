import urllib.request
import json
import time
import datetime
import sys

# Ensure UTF-8 output
sys.stdout.reconfigure(encoding='utf-8')

TARGET_CODES = [
    # Core & High Dividend
    '0050', '0056', '00878', '00919', '00929', '00713', '00918', '00915', 
    '00934', '00936', '00940', '00944', '00946', '006208', '00692', '00850',
    '00905', '00922', '00923', '00935', '00891', '00892', '00881', '00927',
    '00947', '00930', '00932', '00900', '00730', '00731', '00888', '00907',
    # Active
    '00406A',
    # Overseas Equity
    '00757', '00662', '00646', '00830', '00924', '00960',
    # Bond ETFs
    '00679B', '00687B', '00720B', '00725B', '00740B', '00751B', '00761B', 
    '00772B', '00773B', '00795B', '00937B', '00933B', '00948B', '00953B',
    '00945B', '00959B', '00777B', '00780B', '00782B', '00842B', '00844B'
]

import os
cache_path = os.path.join(os.path.dirname(os.path.abspath(__file__)), 'etf_live_cache.json')
try:
    with open(cache_path, 'r', encoding='utf-8') as f:
        cache = json.load(f)
except Exception:
    cache = {}

print(f"Loaded existing cache with {len(cache)} entries.")

def query_finmind(dataset, data_id, extra_params=''):
    url = f"https://api.finmindtrade.com/api/v4/data?dataset={dataset}&data_id={data_id}{extra_params}"
    req = urllib.request.Request(url, headers={'User-Agent': 'Mozilla/5.0 (Windows NT 10.0; Win64; x64)'})
    try:
        with urllib.request.urlopen(req, timeout=6) as resp:
            return json.loads(resp.read().decode('utf-8'))
    except Exception as e:
        return None

now = datetime.datetime.now()
start_price = (now - datetime.timedelta(days=45)).strftime('%Y-%m-%d')
start_div = (now - datetime.timedelta(days=365)).strftime('%Y-%m-%d')

updated_count = 0
for code in TARGET_CODES:
    # 1. Info
    info_res = query_finmind('TaiwanStockInfo', code)
    name = code
    industry = 'ETF'
    if info_res and info_res.get('data'):
        name = info_res['data'][0].get('stock_name', code)
        industry = info_res['data'][0].get('industry_category', 'ETF')
    elif code in cache:
        name = cache[code].get('name', code)

    # 2. Price
    price_res = query_finmind('TaiwanStockPrice', code, f"&start_date={start_price}")
    price = 15.0
    if price_res and price_res.get('data'):
        price = float(price_res['data'][-1]['close'])
    elif code in cache:
        price = cache[code].get('price', 15.0)

    # 3. Dividend
    div_res = query_finmind('TaiwanStockDividend', code, f"&start_date={start_div}")
    total_div = 0.0
    div_count = 0
    payout_months = []
    if div_res and div_res.get('data'):
        for item in div_res['data']:
            c = float(item.get('CashEarningsDistribution') or 0) + float(item.get('CashStatutorySurplus') or 0)
            if c > 0:
                total_div += c
                div_count += 1
                p_date = item.get('CashDividendPaymentDate') or item.get('CashExDividendTradingDate') or item.get('date', '')
                if p_date and '-' in p_date:
                    try:
                        m = int(p_date.split('-')[1])
                        if m not in payout_months:
                            payout_months.append(m)
                    except Exception:
                        pass

    # Frequency analysis
    if div_count >= 10:
        freq = 'monthly'
        freq_desc = '月配息'
        if len(payout_months) < 6:
            payout_months = [1,2,3,4,5,6,7,8,9,10,11,12]
    elif div_count >= 3:
        freq = 'quarterly'
        freq_desc = '季配息'
        if not payout_months:
            payout_months = [3,6,9,12]
    elif div_count == 2:
        freq = 'semi-annual'
        freq_desc = '半年配'
        if not payout_months:
            payout_months = [1,7]
    elif div_count == 1:
        freq = 'annual'
        freq_desc = '年配息'
        if not payout_months:
            payout_months = [7]
    else:
        # Fallback to existing or heuristic
        if code in cache:
            freq = cache[code].get('frequency', 'quarterly')
            freq_desc = cache[code].get('frequency_desc', '季配息')
            payout_months = cache[code].get('payout_months', [3,6,9,12])
            total_div = cache[code].get('annual_dividend', 0.0)
            div_count = cache[code].get('dividend_count', 4)
        else:
            if code.endswith('B'):
                freq = 'monthly'
                freq_desc = '月配息'
                payout_months = [1,2,3,4,5,6,7,8,9,10,11,12]
            else:
                freq = 'quarterly'
                freq_desc = '季配息'
                payout_months = [3,6,9,12]

    is_bond = code.endswith('B') or ('債' in name)
    is_active = ('主動' in name) or (code == '00406A')

    if total_div > 0 and price > 0:
        yield_rate = total_div / price
    else:
        yield_rate = 0.055 if is_bond else 0.065
        total_div = price * yield_rate

    payout_months.sort()

    cache[code] = {
        'code': code,
        'symbol': f"{code}.TW",
        'name': name,
        'price': round(price, 2),
        'annual_dividend': round(total_div, 3),
        'dividend_count': div_count if div_count > 0 else (12 if freq == 'monthly' else 4),
        'yield_rate': round(yield_rate, 4),
        'yield_pct': round(yield_rate * 100, 2),
        'frequency': freq,
        'frequency_desc': freq_desc,
        'payout_months': payout_months,
        'category': 'bond' if is_bond else ('active' if is_active else 'stock'),
        'beta': 0.35 if is_bond else (0.95 if is_active else 0.88),
        'mdd_1y': -0.11 if is_bond else -0.16
    }
    updated_count += 1
    print(f"[{updated_count}/{len(TARGET_CODES)}] Cached {code} ({name}): Price={price}, Div={total_div:.2f}, Yield={yield_rate*100:.2f}%, Freq={freq_desc}")
    time.sleep(0.1) # Be gentle to API

with open(cache_path, 'w', encoding='utf-8') as f:
    json.dump(cache, f, ensure_ascii=False, indent=2)

print(f"Successfully wrote {len(cache)} ETFs to {cache_path}")
