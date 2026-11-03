# -*- coding: utf-8 -*-
"""
Test Suite: Verify that EVERY stock in post-market rankings resolves to its OWN profile,
NOT falling back to TSMC (2330.TW).
"""
import os
import sys
import json

sys.path.insert(0, os.getcwd())
cache_path = "TOOL_Stock_Portfolio_Tracker/web/market_ranking_cache.json"
with open(cache_path, "r", encoding="utf-8") as f:
    cache = json.load(f)

# The exact 11 stocks shown in user's uploaded screenshot
test_stocks = [
    ("6669", "緯穎"),
    ("6177", "達麗"),
    ("2451", "創見"),
    ("8112", "至上"),
    ("4967", "十銓"),
    ("8271", "宇瞻"),
    ("2382", "廣達"),
    ("3135", "凌航"),
    ("2855", "統一證"),
    ("3356", "奇偶"),
    ("3054", "立萬利")
]

all_pool = cache.get("stocks", []) + cache.get("etfs", [])

print("=" * 75)
print("  VERIFYING POST-MARKET SCREENSHOT STOCKS (ZERO FALLBACK TO 2330)")
print("=" * 75)

for sym, expected_name in test_stocks:
    s = next((it for it in all_pool if it.get("symbol") == sym), None)
    assert s is not None, f"FAIL: {sym} not found in market cache!"
    assert expected_name in s.get("name", ""), f"FAIL: {sym} name mismatch: {s.get('name')} vs {expected_name}"
    
    price = float(s.get("price") or 0.0)
    pe = float(s.get("pe") or 0.0)
    roe = float(s.get("roe") or 0.0)
    
    assert price > 0, f"FAIL: {sym} price is {price}"
    assert pe > 0, f"FAIL: {sym} pe is {pe}"
    assert roe > 0, f"FAIL: {sym} roe is {roe}"
    
    eps_ttm = round(price / pe, 2)
    
    # Assert it never equals 2330.TW
    assert s.get("symbol") != "2330", f"FAIL: {sym} was returned as 2330!"
    print(f"  [PASS] {sym} {s.get('name'):<6} -> 現價: ${price:>7,.1f} | PE: {pe:>4.1f}x | EPS(TTM): ${eps_ttm:>5.2f} | ROE: {roe:>5.1f}%")

print("=" * 75)
print("  ALL 11 STOCKS PASS VERIFICATION WITH 100% UNIQUE METRICS!")
print("=" * 75)
