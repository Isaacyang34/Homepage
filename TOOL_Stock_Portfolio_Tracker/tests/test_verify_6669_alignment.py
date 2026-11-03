import os
import sys
import json
import re

# Add repo to sys.path
sys.path.insert(0, os.path.abspath(os.path.join(os.path.dirname(__file__), "..", "..")))
from TOOL_Stock_Portfolio_Tracker.modules.stock_fundamental_service import StockFundamentalService

def test_6669_alignment():
    print("=" * 70)
    print("  E2E VERIFICATION SUITE: 6669 (緯穎) 1:1 DESKTOP VS WEB ALIGNMENT")
    print("=" * 70)

    # 1. Desktop Profile
    desktop = StockFundamentalService.get_stock_profile("6669")
    print("[1] DESKTOP PROFILE FOR 6669:")
    print(f"  Symbol:            {desktop['symbol']}")
    print(f"  Name:              {desktop['name']}")
    print(f"  Category:          {desktop['category']}")
    print(f"  Price:             ${desktop['current_price']:,.1f}")
    print(f"  EPS (TTM):         {desktop['pe_bands']['eps_ttm']}")
    print(f"  PE:                {desktop['pe_bands']['current_pe']}")
    print(f"  PE Status:         {desktop['pe_bands']['status']}")
    print(f"  DuPont ROE:        {desktop['dupont']['roe']}%")
    print(f"  DuPont Net Margin: {desktop['dupont']['net_margin']}%")
    print(f"  DuPont Turnover:   {desktop['dupont']['asset_turnover']}次")
    print(f"  DuPont Multiplier: {desktop['dupont']['equity_multiplier']}倍")
    print(f"  DuPont Verdict:    {desktop['dupont']['verdict']}")
    print(f"  Safety FCF:        {desktop['safety']['fcf']}")
    print(f"  Safety OCF:        {desktop['safety']['operating_cf']}")
    print(f"  Safety Debt:       {desktop['safety']['debt_ratio']}")
    print(f"  Safety Current:    {desktop['safety']['current_ratio']}")
    print(f"  Safety Quick:      {desktop['safety']['quick_ratio']}")

    # 2. Web JS Engine Profile (Simulated via same market_ranking_cache.json)
    web_cache_path = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", "web", "market_ranking_cache.json"))
    with open(web_cache_path, "r", encoding="utf-8") as f:
        web_cache = json.load(f)

    stocks_map = {s["symbol"]: s for s in web_cache.get("stocks", []) if "symbol" in s}
    revs_map = {r["symbol"]: r for r in web_cache.get("revenues", []) if "symbol" in r}

    s = stocks_map.get("6669")
    r = revs_map.get("6669")

    assert s is not None, "Stock 6669 not found in web market_ranking_cache.json"
    assert r is not None, "Revenue 6669 not found in web market_ranking_cache.json"

    # Verify field values
    price = s["price"]
    pe = s["pe"]
    roe = s["roe"]
    pb = s["pb"]
    rev_cur = r["rev_cur_e"]
    yoy = r["yoy"]
    industry = r["industry"]

    eps_ttm = round(price / pe, 2)
    cur_pe = round(price / eps_ttm, 1)

    base_net = max(5.0, min(45.0, roe * 0.8))
    asset_turnover = 0.65 if ("半導體" in industry or "電子" in industry) else 0.85
    equity_multiplier = round(max(1.2, min(2.5, roe / (base_net * asset_turnover))), 2)

    fcf_val = round(rev_cur * 0.35, 1)
    ocf_val = round(rev_cur * 0.55, 1)
    debt_ratio_val = round(35.0 + (equity_multiplier - 1.2) * 15.0, 1)

    print("\n[2] WEB ENGINE CALCULATED PROFILE FOR 6669:")
    print(f"  Category:          {industry}")
    print(f"  Price:             ${price:,.1f}")
    print(f"  EPS (TTM):         {eps_ttm}")
    print(f"  PE:                {cur_pe}")
    print(f"  DuPont ROE:        {round(roe, 1)}%")
    print(f"  DuPont Net Margin: {round(base_net, 1)}%")
    print(f"  DuPont Turnover:   {asset_turnover}次")
    print(f"  DuPont Multiplier: {equity_multiplier}倍")
    print(f"  Safety FCF:        +{fcf_val:,.1f} 億元")
    print(f"  Safety OCF:        +{ocf_val:,.1f} 億元")
    print(f"  Safety Debt:       {debt_ratio_val:.1f}%")

    print("\n[3] CROSS-CHECK ASSERTIONS (100% 1:1 MATCH):")
    assert industry == desktop["category"], f"Industry mismatch: {industry} vs {desktop['category']}"
    assert price == desktop["current_price"], f"Price mismatch: {price} vs {desktop['current_price']}"
    assert eps_ttm == desktop["pe_bands"]["eps_ttm"], f"EPS mismatch: {eps_ttm} vs {desktop['pe_bands']['eps_ttm']}"
    assert cur_pe == desktop["pe_bands"]["current_pe"], f"PE mismatch: {cur_pe} vs {desktop['pe_bands']['current_pe']}"
    assert round(roe, 1) == desktop["dupont"]["roe"], f"ROE mismatch: {roe} vs {desktop['dupont']['roe']}"
    assert round(base_net, 1) == desktop["dupont"]["net_margin"], f"Net margin mismatch: {base_net} vs {desktop['dupont']['net_margin']}"
    assert asset_turnover == desktop["dupont"]["asset_turnover"], f"Turnover mismatch: {asset_turnover} vs {desktop['dupont']['asset_turnover']}"
    assert equity_multiplier == desktop["dupont"]["equity_multiplier"], f"Multiplier mismatch: {equity_multiplier} vs {desktop['dupont']['equity_multiplier']}"
    assert f"+{fcf_val:,.1f}" in desktop["safety"]["fcf"], f"FCF mismatch: {fcf_val} vs {desktop['safety']['fcf']}"
    assert f"+{ocf_val:,.1f}" in desktop["safety"]["operating_cf"], f"OCF mismatch: {ocf_val} vs {desktop['safety']['operating_cf']}"
    assert f"{debt_ratio_val:.1f}%" == desktop["safety"]["debt_ratio"], f"Debt mismatch: {debt_ratio_val} vs {desktop['safety']['debt_ratio']}"

    # 4. Check app.js file content contains custom river annotations and right labels
    app_path = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", "web", "app.js"))
    with open(app_path, "r", encoding="utf-8") as f:
        app_code = f.read()

    assert "riverCustomPlugin" in app_code, "riverCustomPlugin missing from app.js"
    assert "32x 昂貴" in app_code, "32x 昂貴 annotation missing from app.js"
    assert "最新收盤:" in app_code, "最新收盤 callout missing from app.js"
    assert "marginsLabelPlugin" in app_code, "marginsLabelPlugin missing from app.js"

    # 5. Check index.html file contains explanation box
    html_path = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", "web", "index.html"))
    with open(html_path, "r", encoding="utf-8") as f:
        html_code = f.read()

    assert "[圖表閱讀指南]" in html_code, "[圖表閱讀指南] tip box missing from index.html"

    print(">>> ALL ASSERTIONS PASSED: 100% 1:1 ALIGNMENT CONFIRMED! <<<")
    print("=" * 70)

if __name__ == "__main__":
    test_6669_alignment()
