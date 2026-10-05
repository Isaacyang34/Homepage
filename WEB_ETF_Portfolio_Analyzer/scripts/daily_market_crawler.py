#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
Taiwan ETF & Stock Automated Daily Market Data Crawler
Designed for GitHub Actions CI/CD to auto-update etf_live_cache.json on daily close.
"""

import os
import sys
import json
import time
import datetime
import urllib.request
import urllib.error

# Ensure UTF-8 output on all operating systems
if sys.stdout.encoding != 'utf-8':
    try:
        sys.stdout.reconfigure(encoding='utf-8')
    except Exception:
        pass

# Master Directory of Target ETFs & Indicator Stocks
ETF_CATALOG = {
    # 1. High Dividend ETFs (高股息型)
    '0056': {'name': '元大高股息', 'cat': 'stock', 'beta': 0.82, 'mdd': -0.21, 'freq': 'quarterly', 'freq_desc': '季配息', 'p_months': [1, 4, 7, 10]},
    '00878': {'name': '國泰永續高股息', 'cat': 'stock', 'beta': 0.65, 'mdd': -0.15, 'freq': 'quarterly', 'freq_desc': '季配息', 'p_months': [2, 5, 8, 11]},
    '00919': {'name': '群益台灣精選高息', 'cat': 'stock', 'beta': 0.88, 'mdd': -0.19, 'freq': 'quarterly', 'freq_desc': '季配息', 'p_months': [3, 6, 9, 12]},
    '00929': {'name': '復華台灣科技優息', 'cat': 'stock', 'beta': 0.88, 'mdd': -0.16, 'freq': 'monthly', 'freq_desc': '月配息', 'p_months': [1,2,3,4,5,6,7,8,9,10,11,12]},
    '00713': {'name': '元大台灣高息低波', 'cat': 'stock', 'beta': 0.58, 'mdd': -0.14, 'freq': 'quarterly', 'freq_desc': '季配息', 'p_months': [3, 6, 9, 12]},
    '00918': {'name': '大華優利高填息30', 'cat': 'stock', 'beta': 0.85, 'mdd': -0.18, 'freq': 'quarterly', 'freq_desc': '季配息', 'p_months': [3, 6, 9, 12]},
    '00915': {'name': '凱基優選高股息30', 'cat': 'stock', 'beta': 0.72, 'mdd': -0.15, 'freq': 'quarterly', 'freq_desc': '季配息', 'p_months': [3, 6, 9, 12]},
    '00934': {'name': '中信成長高股息', 'cat': 'stock', 'beta': 0.82, 'mdd': -0.17, 'freq': 'monthly', 'freq_desc': '月配息', 'p_months': [1,2,3,4,5,6,7,8,9,10,11,12]},
    '00936': {'name': '台新永續高息中小', 'cat': 'stock', 'beta': 0.92, 'mdd': -0.22, 'freq': 'monthly', 'freq_desc': '月配息', 'p_months': [1,2,3,4,5,6,7,8,9,10,11,12]},
    '00940': {'name': '元大台灣價值高息', 'cat': 'stock', 'beta': 0.72, 'mdd': -0.18, 'freq': 'monthly', 'freq_desc': '月配息', 'p_months': [1,2,3,4,5,6,7,8,9,10,11,12]},
    '00944': {'name': '野村趨勢動能高息', 'cat': 'stock', 'beta': 0.78, 'mdd': -0.16, 'freq': 'monthly', 'freq_desc': '月配息', 'p_months': [1,2,3,4,5,6,7,8,9,10,11,12]},
    '00946': {'name': '群益科技高息成長', 'cat': 'stock', 'beta': 0.90, 'mdd': -0.19, 'freq': 'monthly', 'freq_desc': '月配息', 'p_months': [1,2,3,4,5,6,7,8,9,10,11,12]},
    '00900': {'name': '富邦特選高股息30', 'cat': 'stock', 'beta': 0.95, 'mdd': -0.24, 'freq': 'monthly', 'freq_desc': '月配息', 'p_months': [1,2,3,4,5,6,7,8,9,10,11,12]},
    '00730': {'name': '富邦臺灣優質高息', 'cat': 'stock', 'beta': 0.68, 'mdd': -0.16, 'freq': 'monthly', 'freq_desc': '月配息', 'p_months': [1,2,3,4,5,6,7,8,9,10,11,12]},
    '00731': {'name': '復華富時高息低波', 'cat': 'stock', 'beta': 0.62, 'mdd': -0.13, 'freq': 'quarterly', 'freq_desc': '季配息', 'p_months': [2, 5, 8, 11]},
    '00888': {'name': '永豐台灣ESG', 'cat': 'stock', 'beta': 0.85, 'mdd': -0.20, 'freq': 'quarterly', 'freq_desc': '季配息', 'p_months': [1, 4, 7, 10]},
    '00907': {'name': '永豐優息存股', 'cat': 'stock', 'beta': 0.60, 'mdd': -0.14, 'freq': 'quarterly', 'freq_desc': '季配息', 'p_months': [2, 5, 8, 11]},
    '00930': {'name': '永豐ESG低碳高息', 'cat': 'stock', 'beta': 0.78, 'mdd': -0.17, 'freq': 'quarterly', 'freq_desc': '季配息', 'p_months': [1, 3, 5, 7, 9, 11]},
    '00932': {'name': '兆豐永續高息等權', 'cat': 'stock', 'beta': 0.82, 'mdd': -0.19, 'freq': 'quarterly', 'freq_desc': '季配息', 'p_months': [2, 5, 8, 11]},

    # 2. Market Cap & Thematic Equity ETFs (市值型、科技、半導體)
    '0050': {'name': '元大台灣50', 'cat': 'stock', 'beta': 1.00, 'mdd': -0.27, 'freq': 'semi-annual', 'freq_desc': '半年配', 'p_months': [1, 7]},
    '006208': {'name': '富邦台50', 'cat': 'stock', 'beta': 1.00, 'mdd': -0.27, 'freq': 'semi-annual', 'freq_desc': '半年配', 'p_months': [7, 11]},
    '00692': {'name': '富邦公司治理', 'cat': 'stock', 'beta': 0.96, 'mdd': -0.25, 'freq': 'semi-annual', 'freq_desc': '半年配', 'p_months': [7, 11]},
    '00850': {'name': '元大臺灣ESG永續', 'cat': 'stock', 'beta': 0.95, 'mdd': -0.25, 'freq': 'quarterly', 'freq_desc': '季配息', 'p_months': [2, 5, 8, 11]},
    '00905': {'name': 'FT臺灣Smart', 'cat': 'stock', 'beta': 0.98, 'mdd': -0.26, 'freq': 'quarterly', 'freq_desc': '季配息', 'p_months': [1, 4, 7, 10]},
    '00922': {'name': '國泰台灣領袖50', 'cat': 'stock', 'beta': 1.02, 'mdd': -0.28, 'freq': 'semi-annual', 'freq_desc': '半年配', 'p_months': [3, 10]},
    '00923': {'name': '群益台ESG低碳50', 'cat': 'stock', 'beta': 1.05, 'mdd': -0.28, 'freq': 'semi-annual', 'freq_desc': '半年配', 'p_months': [2, 8]},
    '00935': {'name': '野村臺灣新科技50', 'cat': 'stock', 'beta': 1.15, 'mdd': -0.30, 'freq': 'semi-annual', 'freq_desc': '半年配', 'p_months': [3, 9]},
    '00891': {'name': '中信關鍵半導體', 'cat': 'stock', 'beta': 1.25, 'mdd': -0.35, 'freq': 'quarterly', 'freq_desc': '季配息', 'p_months': [1, 4, 7, 10]},
    '00892': {'name': '富邦台灣半導體', 'cat': 'stock', 'beta': 1.22, 'mdd': -0.34, 'freq': 'semi-annual', 'freq_desc': '半年配', 'p_months': [1, 7]},
    '00881': {'name': '國泰台灣科技龍頭', 'cat': 'stock', 'beta': 1.10, 'mdd': -0.31, 'freq': 'annual', 'freq_desc': '年配息', 'p_months': [1]},
    '00927': {'name': '群益半導體收益', 'cat': 'stock', 'beta': 1.18, 'mdd': -0.32, 'freq': 'quarterly', 'freq_desc': '季配息', 'p_months': [1, 4, 7, 10]},
    '00947': {'name': '台新臺灣IC設計', 'cat': 'stock', 'beta': 1.28, 'mdd': -0.36, 'freq': 'quarterly', 'freq_desc': '季配息', 'p_months': [1, 4, 7, 10]},

    # 3. Active ETFs (主動型 ETF)
    '00406A': {'name': '主動中信台灣收益', 'cat': 'active', 'beta': 0.95, 'mdd': -0.16, 'freq': 'quarterly', 'freq_desc': '季配息', 'p_months': [3, 6, 9, 12]},

    # 4. Overseas & Thematic Global Equity ETFs (海外權益型)
    '00757': {'name': '統一FANG+', 'cat': 'stock', 'beta': 1.35, 'mdd': -0.38, 'freq': 'none', 'freq_desc': '不配息 (收益累積)', 'p_months': []},
    '00662': {'name': '富邦NASDAQ', 'cat': 'stock', 'beta': 1.18, 'mdd': -0.32, 'freq': 'quarterly', 'freq_desc': '季配息', 'p_months': [3, 6, 9, 12]},
    '00646': {'name': '元大S&P500', 'cat': 'stock', 'beta': 0.95, 'mdd': -0.24, 'freq': 'quarterly', 'freq_desc': '季配息', 'p_months': [3, 6, 9, 12]},
    '00830': {'name': '國泰費城半導體', 'cat': 'stock', 'beta': 1.45, 'mdd': -0.42, 'freq': 'annual', 'freq_desc': '年配息', 'p_months': [1]},
    '00924': {'name': '復華S&P500成長', 'cat': 'stock', 'beta': 1.08, 'mdd': -0.28, 'freq': 'quarterly', 'freq_desc': '季配息', 'p_months': [3, 6, 9, 12]},
    '00960': {'name': '野村全球航運龍頭', 'cat': 'stock', 'beta': 1.25, 'mdd': -0.36, 'freq': 'quarterly', 'freq_desc': '季配息', 'p_months': [3, 6, 9, 12]},

    # 5. Bond ETFs (債券型 - 100% 免二代健保)
    '00679B': {'name': '元大美債20年', 'cat': 'bond', 'beta': 0.38, 'mdd': -0.18, 'freq': 'quarterly', 'freq_desc': '季配息', 'p_months': [2, 5, 8, 11]},
    '00687B': {'name': '國泰20年美債', 'cat': 'bond', 'beta': 0.38, 'mdd': -0.18, 'freq': 'quarterly', 'freq_desc': '季配息', 'p_months': [1, 4, 7, 10]},
    '00720B': {'name': '元大投資級公司債', 'cat': 'bond', 'beta': 0.32, 'mdd': -0.14, 'freq': 'quarterly', 'freq_desc': '季配息', 'p_months': [1, 4, 7, 10]},
    '00725B': {'name': '國泰投資級公司債', 'cat': 'bond', 'beta': 0.32, 'mdd': -0.14, 'freq': 'quarterly', 'freq_desc': '季配息', 'p_months': [1, 4, 7, 10]},
    '00740B': {'name': '富邦全球投等債', 'cat': 'bond', 'beta': 0.30, 'mdd': -0.13, 'freq': 'monthly', 'freq_desc': '月配息', 'p_months': [1,2,3,4,5,6,7,8,9,10,11,12]},
    '00751B': {'name': '元大AAA至A公司債', 'cat': 'bond', 'beta': 0.31, 'mdd': -0.13, 'freq': 'quarterly', 'freq_desc': '季配息', 'p_months': [3, 6, 9, 12]},
    '00761B': {'name': '國泰A級公司債', 'cat': 'bond', 'beta': 0.30, 'mdd': -0.13, 'freq': 'quarterly', 'freq_desc': '季配息', 'p_months': [3, 6, 9, 12]},
    '00772B': {'name': '中信高評級公司債', 'cat': 'bond', 'beta': 0.30, 'mdd': -0.12, 'freq': 'monthly', 'freq_desc': '月配息', 'p_months': [1,2,3,4,5,6,7,8,9,10,11,12]},
    '00773B': {'name': '中信優先金融債', 'cat': 'bond', 'beta': 0.28, 'mdd': -0.12, 'freq': 'monthly', 'freq_desc': '月配息', 'p_months': [1,2,3,4,5,6,7,8,9,10,11,12]},
    '00795B': {'name': '中信美國公債20年', 'cat': 'bond', 'beta': 0.38, 'mdd': -0.18, 'freq': 'quarterly', 'freq_desc': '季配息', 'p_months': [2, 5, 8, 11]},
    '00937B': {'name': '群益ESG投等債20+', 'cat': 'bond', 'beta': 0.32, 'mdd': -0.12, 'freq': 'monthly', 'freq_desc': '月配息', 'p_months': [1,2,3,4,5,6,7,8,9,10,11,12]},
    '00933B': {'name': '國泰10Y+金融債', 'cat': 'bond', 'beta': 0.30, 'mdd': -0.12, 'freq': 'monthly', 'freq_desc': '月配息', 'p_months': [1,2,3,4,5,6,7,8,9,10,11,12]},
    '00948B': {'name': '中信優息投資級債', 'cat': 'bond', 'beta': 0.29, 'mdd': -0.11, 'freq': 'monthly', 'freq_desc': '月配息', 'p_months': [1,2,3,4,5,6,7,8,9,10,11,12]},
    '00953B': {'name': '群益優選非投等債', 'cat': 'bond', 'beta': 0.35, 'mdd': -0.13, 'freq': 'monthly', 'freq_desc': '月配息', 'p_months': [1,2,3,4,5,6,7,8,9,10,11,12]},
    '00945B': {'name': '凱基美國非投等債', 'cat': 'bond', 'beta': 0.36, 'mdd': -0.13, 'freq': 'monthly', 'freq_desc': '月配息', 'p_months': [1,2,3,4,5,6,7,8,9,10,11,12]},
    '00959B': {'name': '大華投等美債15Y+', 'cat': 'bond', 'beta': 0.33, 'mdd': -0.14, 'freq': 'monthly', 'freq_desc': '月配息', 'p_months': [1,2,3,4,5,6,7,8,9,10,11,12]},
    '00777B': {'name': '凱基AAA至A公司債', 'cat': 'bond', 'beta': 0.30, 'mdd': -0.12, 'freq': 'monthly', 'freq_desc': '月配息', 'p_months': [1,2,3,4,5,6,7,8,9,10,11,12]},
    '00780B': {'name': '國泰A級金融債', 'cat': 'bond', 'beta': 0.29, 'mdd': -0.12, 'freq': 'quarterly', 'freq_desc': '季配息', 'p_months': [1, 4, 7, 10]},
    '00782B': {'name': '國泰A級公用債', 'cat': 'bond', 'beta': 0.28, 'mdd': -0.12, 'freq': 'quarterly', 'freq_desc': '季配息', 'p_months': [1, 4, 7, 10]},
    '00842B': {'name': '台新美元銀行債', 'cat': 'bond', 'beta': 0.32, 'mdd': -0.13, 'freq': 'quarterly', 'freq_desc': '季配息', 'p_months': [1, 4, 7, 10]},
    '00844B': {'name': '新光15年IG金融債', 'cat': 'bond', 'beta': 0.31, 'mdd': -0.13, 'freq': 'quarterly', 'freq_desc': '季配息', 'p_months': [1, 4, 7, 10]},
    '00712':  {'name': '復華富時不動產', 'cat': 'stock', 'beta': 0.85, 'mdd': -0.28, 'freq': 'quarterly', 'freq_desc': '季配息', 'p_months': [1, 4, 7, 10]},

    # 6. Blue-Chip & Indicator Individual Stocks (指標個股 - 100% 54C 課徵二代健保)
    '2330': {'name': '台積電', 'cat': 'single_stock', 'beta': 1.15, 'mdd': -0.32, 'freq': 'quarterly', 'freq_desc': '季配息', 'p_months': [3, 6, 9, 12]},
    '2317': {'name': '鴻海', 'cat': 'single_stock', 'beta': 1.05, 'mdd': -0.28, 'freq': 'annual', 'freq_desc': '年配息', 'p_months': [7]},
    '2454': {'name': '聯發科', 'cat': 'single_stock', 'beta': 1.25, 'mdd': -0.38, 'freq': 'semi-annual', 'freq_desc': '半年配', 'p_months': [1, 7]},
    '2382': {'name': '廣達', 'cat': 'single_stock', 'beta': 1.20, 'mdd': -0.35, 'freq': 'annual', 'freq_desc': '年配息', 'p_months': [5]},
    '2308': {'name': '台達電', 'cat': 'single_stock', 'beta': 0.95, 'mdd': -0.26, 'freq': 'annual', 'freq_desc': '年配息', 'p_months': [7]},
    '2303': {'name': '聯電', 'cat': 'single_stock', 'beta': 0.98, 'mdd': -0.30, 'freq': 'annual', 'freq_desc': '年配息', 'p_months': [7]},
    '2357': {'name': '華碩', 'cat': 'single_stock', 'beta': 1.05, 'mdd': -0.33, 'freq': 'annual', 'freq_desc': '年配息', 'p_months': [7]},
    '3231': {'name': '緯創', 'cat': 'single_stock', 'beta': 1.30, 'mdd': -0.40, 'freq': 'annual', 'freq_desc': '年配息', 'p_months': [7]},
    '2881': {'name': '富邦金', 'cat': 'single_stock', 'beta': 0.85, 'mdd': -0.22, 'freq': 'annual', 'freq_desc': '年配息', 'p_months': [7]},
    '2882': {'name': '國泰金', 'cat': 'single_stock', 'beta': 0.88, 'mdd': -0.24, 'freq': 'annual', 'freq_desc': '年配息', 'p_months': [7]},
    '2891': {'name': '中信金', 'cat': 'single_stock', 'beta': 0.78, 'mdd': -0.19, 'freq': 'annual', 'freq_desc': '年配息', 'p_months': [7]},
    '2886': {'name': '兆豐金', 'cat': 'single_stock', 'beta': 0.72, 'mdd': -0.18, 'freq': 'annual', 'freq_desc': '年配息', 'p_months': [8]},
    '2884': {'name': '玉山金', 'cat': 'single_stock', 'beta': 0.75, 'mdd': -0.20, 'freq': 'annual', 'freq_desc': '年配息', 'p_months': [8]},
    '2885': {'name': '元大金', 'cat': 'single_stock', 'beta': 0.82, 'mdd': -0.21, 'freq': 'annual', 'freq_desc': '年配息', 'p_months': [7]},
    '2892': {'name': '第一金', 'cat': 'single_stock', 'beta': 0.70, 'mdd': -0.17, 'freq': 'annual', 'freq_desc': '年配息', 'p_months': [8]},
    '2880': {'name': '華南金', 'cat': 'single_stock', 'beta': 0.68, 'mdd': -0.16, 'freq': 'annual', 'freq_desc': '年配息', 'p_months': [8]},
    '5880': {'name': '合庫金', 'cat': 'single_stock', 'beta': 0.65, 'mdd': -0.16, 'freq': 'annual', 'freq_desc': '年配息', 'p_months': [8]},
    '2890': {'name': '永豐金', 'cat': 'single_stock', 'beta': 0.76, 'mdd': -0.19, 'freq': 'annual', 'freq_desc': '年配息', 'p_months': [8]},
    '2887': {'name': '台新金', 'cat': 'single_stock', 'beta': 0.77, 'mdd': -0.20, 'freq': 'annual', 'freq_desc': '年配息', 'p_months': [8]},
    '2603': {'name': '長榮', 'cat': 'single_stock', 'beta': 1.35, 'mdd': -0.45, 'freq': 'annual', 'freq_desc': '年配息', 'p_months': [6]},
    '2609': {'name': '陽明', 'cat': 'single_stock', 'beta': 1.40, 'mdd': -0.48, 'freq': 'annual', 'freq_desc': '年配息', 'p_months': [7]},
    '2412': {'name': '中華電', 'cat': 'single_stock', 'beta': 0.45, 'mdd': -0.12, 'freq': 'annual', 'freq_desc': '年配息', 'p_months': [7]},
    '3045': {'name': '台灣大', 'cat': 'single_stock', 'beta': 0.50, 'mdd': -0.13, 'freq': 'annual', 'freq_desc': '年配息', 'p_months': [7]},
    '1216': {'name': '統一', 'cat': 'single_stock', 'beta': 0.60, 'mdd': -0.15, 'freq': 'annual', 'freq_desc': '年配息', 'p_months': [7]},
    '2002': {'name': '中鋼', 'cat': 'single_stock', 'beta': 0.75, 'mdd': -0.25, 'freq': 'annual', 'freq_desc': '年配息', 'p_months': [7]},
    '1301': {'name': '台塑', 'cat': 'single_stock', 'beta': 0.72, 'mdd': -0.28, 'freq': 'annual', 'freq_desc': '年配息', 'p_months': [7]}
}

def query_yahoo(ticker_symbol):
    """Query Yahoo Finance API for current market price and dividend distribution events."""
    url = f"https://query1.finance.yahoo.com/v8/finance/chart/{ticker_symbol}?interval=1d&events=div&range=2y"
    req = urllib.request.Request(url, headers={'User-Agent': 'Mozilla/5.0 (Windows NT 10.0; Win64; x64)'})
    try:
        with urllib.request.urlopen(req, timeout=6) as resp:
            data = json.loads(resp.read().decode('utf-8'))
            chart = data.get('chart', {}).get('result', [])
            if not chart:
                return None
            res = chart[0]
            price = res.get('meta', {}).get('regularMarketPrice')
            events = res.get('events', {}).get('dividends', {})
            return {'price': price, 'events': events}
    except Exception:
        return None

def query_finmind(code):
    """Fallback query to FinMind Open Data API."""
    now = datetime.datetime.now()
    start_p = (now - datetime.timedelta(days=45)).strftime('%Y-%m-%d')
    start_d = (now - datetime.timedelta(days=365)).strftime('%Y-%m-%d')

    price = None
    try:
        url_p = f"https://api.finmindtrade.com/api/v4/data?dataset=TaiwanStockPrice&data_id={code}&start_date={start_p}"
        req = urllib.request.Request(url_p, headers={'User-Agent': 'Mozilla/5.0'})
        with urllib.request.urlopen(req, timeout=5) as resp:
            p_data = json.loads(resp.read().decode('utf-8')).get('data', [])
            if p_data:
                price = float(p_data[-1].get('close') or 0)
    except Exception:
        pass

    div_sum = 0.0
    div_count = 0
    p_months = []
    try:
        url_d = f"https://api.finmindtrade.com/api/v4/data?dataset=TaiwanStockDividend&data_id={code}&start_date={start_d}"
        req = urllib.request.Request(url_d, headers={'User-Agent': 'Mozilla/5.0'})
        with urllib.request.urlopen(req, timeout=5) as resp:
            d_data = json.loads(resp.read().decode('utf-8')).get('data', [])
            for item in d_data:
                val = float(item.get('CashEarningsDistribution') or 0) + float(item.get('CashStatutorySurplus') or 0)
                if val > 0:
                    div_sum += val
                    div_count += 1
                    p_date = item.get('CashDividendPaymentDate') or item.get('CashExDividendTradingDate') or item.get('date', '')
                    if p_date and '-' in p_date:
                        try:
                            m = int(p_date.split('-')[1])
                            if m not in p_months:
                                p_months.append(m)
                        except Exception:
                            pass
    except Exception:
        pass

    return {'price': price, 'div_sum': div_sum, 'div_count': div_count, 'p_months': p_months}

def run_crawler():
    print(f"=== Starting Daily Market Data Sync [{datetime.datetime.now().strftime('%Y-%m-%d %H:%M:%S')}] ===")
    
    script_dir = os.path.dirname(os.path.abspath(__file__))
    project_root = os.path.dirname(script_dir)
    cache_path = os.path.join(project_root, 'etf_live_cache.json')

    # Load existing cache for fallback persistence
    cache = {}
    if os.path.exists(cache_path):
        try:
            with open(cache_path, 'r', encoding='utf-8') as f:
                cache = json.load(f)
            print(f"Found existing cache with {len(cache)} entries.")
        except Exception as e:
            print(f"Notice: Failed to read existing cache: {e}")

    success_count = 0
    total_count = len(ETF_CATALOG)

    for code, meta in ETF_CATALOG.items():
        is_tpex = code.endswith('B')  # Bond ETFs are on TPEx
        symbol = f"{code}.TWO" if is_tpex else f"{code}.TW"
        
        # 1. Attempt Yahoo Finance
        y_res = query_yahoo(symbol)
        price = None
        annual_div = 0.0
        div_count = 0
        p_months = list(meta['p_months'])

        if y_res and y_res.get('price'):
            price = float(y_res['price'])
            events = y_res.get('events', {})
            now_ts = time.time()
            one_year_ago = now_ts - 365 * 86400
            
            recent_divs = []
            extracted_months = []
            for k, item in events.items():
                t_date = float(item.get('date', 0))
                if t_date >= one_year_ago:
                    amt = float(item.get('amount') or 0)
                    if amt > 0:
                        recent_divs.append(amt)
                        m = datetime.datetime.fromtimestamp(t_date).month
                        if m not in extracted_months:
                            extracted_months.append(m)

            if recent_divs:
                annual_div = sum(recent_divs)
                div_count = len(recent_divs)
                if extracted_months:
                    p_months = sorted(extracted_months)
            elif events:
                # If newly listed or only 1 payout on record
                annual_div = float(list(events.values())[-1].get('amount') or 0)
                div_count = 1

        # 2. Fallback to FinMind if Yahoo price missing
        if price is None:
            fm_res = query_finmind(code)
            if fm_res and fm_res.get('price'):
                price = fm_res['price']
                if fm_res.get('div_sum') and fm_res['div_sum'] > 0:
                    annual_div = fm_res['div_sum']
                    div_count = fm_res['div_count']
                    if fm_res.get('p_months'):
                        p_months = fm_res['p_months']

        # 3. Final Fallback to existing cached values
        if price is None:
            if code in cache:
                price = cache[code].get('price', 15.0)
                annual_div = cache[code].get('annual_dividend', 0.0)
                div_count = cache[code].get('dividend_count', 4)
            else:
                price = 15.0

        # Yield calculation
        if annual_div > 0 and price > 0:
            yield_rate = annual_div / price
        else:
            # Baseline conservative yield estimation for non-dividend or new listings
            default_yield = 0.045 if meta['cat'] == 'bond' else (0.009 if meta['cat'] == 'single_stock' else 0.055)
            yield_rate = default_yield
            annual_div = price * yield_rate
            div_count = 1 if meta['freq'] == 'annual' else (12 if meta['freq'] == 'monthly' else 4)

        yield_pct = round(yield_rate * 100, 2)
        display_name = meta['name'] if meta['cat'] != 'single_stock' else f"{meta['name']} (個股)"

        cache[code] = {
            'code': code,
            'symbol': symbol,
            'name': display_name,
            'price': round(price, 2),
            'annual_dividend': round(annual_div, 3),
            'dividend_count': div_count,
            'yield_rate': round(yield_rate, 4),
            'yield_pct': yield_pct,
            'frequency': meta['freq'],
            'frequency_desc': meta['freq_desc'],
            'payout_months': p_months,
            'category': meta['cat'],
            'beta': meta['beta'],
            'mdd_1y': meta['mdd']
        }
        success_count += 1
        print(f"[{success_count:02d}/{total_count}] OK {code} ({display_name}): Price={price:.2f}, Div={annual_div:.2f}, Yield={yield_pct}%, Freq={meta['freq_desc']}")
        time.sleep(0.08)

    # Save to disk
    with open(cache_path, 'w', encoding='utf-8') as f:
        json.dump(cache, f, ensure_ascii=False, indent=2)

    print(f"\n=== Successfully synced {len(cache)} market instruments to {cache_path} ===")

if __name__ == '__main__':
    run_crawler()
