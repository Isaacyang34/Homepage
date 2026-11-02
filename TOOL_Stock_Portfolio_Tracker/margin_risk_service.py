# -*- coding: utf-8 -*-
"""
Stock Portfolio Tracker - 融資維持率與個人庫存風控計算引擎 (margin_risk_service.py)
"""
import time
import json
import urllib.request
from typing import Dict, List, Any, Optional

def calculate_single_margin_risk(pos: Dict[str, Any], curr_price: float) -> Dict[str, Any]:
    shares = float(pos.get("shares", 0.0))
    cost_price = float(pos.get("cost_price", 0.0))
    trade_type = str(pos.get("trade_type", "CASH")).upper()
    market = str(pos.get("market", "TW")).upper()
    
    default_rate = 0.5 if market == "TWO" else 0.6
    margin_rate = float(pos.get("margin_rate") or default_rate)
    if margin_rate <= 0 or margin_rate >= 1.0:
        margin_rate = default_rate

    is_margin = (trade_type == "MARGIN")
    total_cost = shares * cost_price
    market_val = shares * curr_price if curr_price > 0 else total_cost

    if not is_margin or shares <= 0 or cost_price <= 0:
        return {
            "symbol": pos.get("symbol", ""),
            "name": pos.get("name", pos.get("symbol", "")),
            "trade_type": "CASH",
            "is_margin": False,
            "shares": shares,
            "cost_price": cost_price,
            "current_price": curr_price,
            "market_val": round(market_val),
            "margin_rate": margin_rate,
            "margin_loan": 0.0,
            "margin_equity": round(total_cost),
            "maintenance_ratio": None,
            "call_price": None,
            "margin_call_diff": 0.0,
            "risk_level": "CASH",
            "risk_label": "現股"
        }

    margin_loan = round(total_cost * margin_rate)
    margin_equity = total_cost - margin_loan

    maintenance_ratio = (market_val / margin_loan * 100.0) if margin_loan > 0 else 0.0
    maintenance_ratio = round(maintenance_ratio, 2)

    call_price = round((margin_loan * 1.30) / shares, 2) if shares > 0 else 0.0

    margin_call_diff = 0.0
    if maintenance_ratio < 130.0 and margin_loan > 0:
        margin_call_diff = round((margin_loan * 1.30) - market_val)

    if maintenance_ratio >= 160.0:
        risk_level = "SAFE"
        risk_label = "安全"
    elif maintenance_ratio >= 130.0:
        risk_level = "WARNING"
        risk_label = "警戒"
    else:
        risk_level = "CALL"
        risk_label = "追繳"

    return {
        "symbol": pos.get("symbol", ""),
        "name": pos.get("name", pos.get("symbol", "")),
        "trade_type": "MARGIN",
        "is_margin": True,
        "shares": shares,
        "cost_price": cost_price,
        "current_price": curr_price,
        "market_val": round(market_val),
        "margin_rate": margin_rate,
        "margin_loan": margin_loan,
        "margin_equity": round(margin_equity),
        "maintenance_ratio": maintenance_ratio,
        "call_price": call_price,
        "margin_call_diff": max(0.0, margin_call_diff),
        "risk_level": risk_level,
        "risk_label": risk_label
    }

def calculate_portfolio_margin_summary(positions: List[Dict[str, Any]], quotes: Dict[str, Any]) -> Dict[str, Any]:
    items = []
    tot_margin_market_val = 0.0
    tot_margin_loan = 0.0
    tot_margin_equity = 0.0
    call_count = 0
    warning_count = 0
    tot_call_diff = 0.0
    has_any_margin = False

    for pos in positions:
        sym = pos.get("symbol", "").upper()
        q = quotes.get(sym) or {}
        curr_p = float(q.get("current_price") or pos.get("current_price") or pos.get("cost_price", 0.0))
        risk_info = calculate_single_margin_risk(pos, curr_p)
        items.append(risk_info)

        if risk_info["is_margin"]:
            has_any_margin = True
            tot_margin_market_val += risk_info["market_val"]
            tot_margin_loan += risk_info["margin_loan"]
            tot_margin_equity += risk_info["margin_equity"]
            tot_call_diff += risk_info["margin_call_diff"]

            if risk_info["risk_level"] == "CALL":
                call_count += 1
            elif risk_info["risk_level"] == "WARNING":
                warning_count += 1

    if not has_any_margin or tot_margin_loan <= 0:
        overall_ratio = 0.0
        overall_level = "NO_MARGIN"
        overall_label = "全現股 (無融資風險)"
    else:
        overall_ratio = round((tot_margin_market_val / tot_margin_loan) * 100.0, 2)
        if overall_ratio >= 160.0:
            overall_level = "SAFE"
            overall_label = "整戶安全"
        elif overall_ratio >= 130.0:
            overall_level = "WARNING"
            overall_label = "整戶警戒"
        else:
            overall_level = "CALL"
            overall_label = "追繳斷頭"

    def sort_key(x):
        if not x["is_margin"]:
            return (3, 999999)
        ratio = x["maintenance_ratio"] or 999999
        if x["risk_level"] == "CALL":
            return (0, ratio)
        elif x["risk_level"] == "WARNING":
            return (1, ratio)
        return (2, ratio)

    sorted_items = sorted(items, key=sort_key)

    return {
        "has_margin": has_any_margin,
        "total_margin_market_val": round(tot_margin_market_val),
        "total_margin_loan": round(tot_margin_loan),
        "total_margin_equity": round(tot_margin_equity),
        "overall_maintenance_ratio": overall_ratio,
        "overall_risk_level": overall_level,
        "overall_risk_label": overall_label,
        "call_count": call_count,
        "warning_count": warning_count,
        "total_call_diff": round(tot_call_diff),
        "items": sorted_items
    }

# =========================================================================
# 全市場融資維持率與信用交易籌碼大盤/個股排行引擎 (TWSE Market Margin Engine)
# =========================================================================

_MARKET_MARGIN_CACHE: Optional[Dict[str, Any]] = None
_MARKET_MARGIN_CACHE_TIME: float = 0.0

def fetch_twse_market_margin_data(force_refresh: bool = False) -> Dict[str, Any]:
    """
    從台灣證交所 (TWSE) 抓取官方最新信用交易總表 (MI_MARGN) 與全市場各股融資餘額、使用率與估算維持率。
    - 支援快取 15 分鐘，避免重複請求
    - 回傳：大盤整體融資維持率估計、總融資餘額、融券餘額，以及全市場個股融資排行
    """
    global _MARKET_MARGIN_CACHE, _MARKET_MARGIN_CACHE_TIME
    now = time.time()
    if not force_refresh and _MARKET_MARGIN_CACHE and (now - _MARKET_MARGIN_CACHE_TIME < 900):
        return _MARKET_MARGIN_CACHE

    # 嘗試讀取本地現有股票快取以取得即時/最新價格
    from ranking_service import MarketRankingService
    cache = MarketRankingService.load_cache(allow_stale=True) or {}
    stocks_map = {s["symbol"]: s for s in cache.get("stocks", [])}

    url = "https://www.twse.com.tw/rwd/zh/marginTrading/MI_MARGN?response=json&selectType=ALL"
    headers = {"User-Agent": "Mozilla/5.0 (Windows NT 10.0; Win64; x64)"}

    market_summary = {
        "date": "",
        "total_margin_loan_str": "",
        "total_margin_loan_val": 0.0,
        "total_margin_mkt_val": 0.0,
        "market_maintenance_ratio": 0.0,
        "risk_level": "SAFE",
        "risk_label": "大盤安全"
    }
    items = []

    try:
        req = urllib.request.Request(url, headers=headers)
        with urllib.request.urlopen(req, timeout=10) as resp:
            data = json.loads(resp.read().decode("utf-8"))

        trade_date = data.get("date", "")
        market_summary["date"] = trade_date

        tables = data.get("tables", [])
        if len(tables) >= 1:
            # Table 0: 全市場彙總 (融資金額、融資張數、融券張數)
            t0 = tables[0]
            t0_data = t0.get("data", [])
            for r in t0_data:
                label = r[0] if r else ""
                if "融資金額" in label or "融資(元)" in label:
                    # r[5] 為今日融資餘額 (千元)
                    try:
                        loan_thousand = float(r[5].replace(",", ""))
                        market_summary["total_margin_loan_val"] = loan_thousand * 1000.0
                        market_summary["total_margin_loan_str"] = f"{loan_thousand / 100000.0:.2f} 億元"
                    except Exception:
                        pass

        if len(tables) >= 2:
            # Table 1: 全市場各股明細 (1300+ 檔)
            t1 = tables[1]
            rows = t1.get("data", [])
            tot_margin_mkt_val = 0.0

            for r in rows:
                if len(r) < 8:
                    continue
                sym = r[0].strip()
                name = r[1].strip()
                try:
                    # r[6]: 今日融資餘額 (張)
                    # r[7]: 融資限額 (張)
                    # r[2]: 融資買進 (張)
                    # r[3]: 融資賣出 (張)
                    bal_lots = int(r[6].replace(",", ""))
                    lim_lots = int(r[7].replace(",", ""))
                    buy_lots = int(r[2].replace(",", "")) if len(r) > 2 and r[2] else 0
                    sell_lots = int(r[3].replace(",", "")) if len(r) > 3 and r[3] else 0
                    
                    st = stocks_map.get(sym) or {}
                    price = float(st.get("price") or 0.0)

                    utilization = (bal_lots / lim_lots * 100.0) if lim_lots > 0 else 0.0
                    mkt_val = (price * bal_lots * 1000.0) if price > 0 else 0.0

                    if mkt_val > 0:
                        tot_margin_mkt_val += mkt_val

                    # 預估借款本金 (以預設 60% 成數估算)
                    est_loan = (mkt_val * 0.6) if mkt_val > 0 else 0.0
                    # 預估維持率 (若無現價則為 None)
                    est_ratio = 166.67
                    if price > 0 and est_loan > 0:
                        est_ratio = (mkt_val / est_loan * 100.0)

                    # 130% 追繳警示門檻價
                    call_price = round(price * 0.78, 2) if price > 0 else 0.0

                    items.append({
                        "symbol": sym,
                        "name": name,
                        "price": price,
                        "margin_balance_lots": bal_lots,
                        "margin_limit_lots": lim_lots,
                        "margin_utilization": round(utilization, 2),
                        "margin_net_lots": buy_lots - sell_lots,
                        "margin_mkt_val_million": round(mkt_val / 1000000.0, 2),
                        "call_price": call_price,
                        "is_etf": sym.startswith("00")
                    })
                except Exception:
                    continue

            market_summary["total_margin_mkt_val"] = tot_margin_mkt_val
            if market_summary["total_margin_loan_val"] > 0 and tot_margin_mkt_val > 0:
                mkt_ratio = (tot_margin_mkt_val / market_summary["total_margin_loan_val"]) * 100.0
                market_summary["market_maintenance_ratio"] = round(mkt_ratio, 2)
                if mkt_ratio >= 165.0:
                    market_summary["risk_level"] = "SAFE"
                    market_summary["risk_label"] = "籌碼健康 (>= 165%)"
                elif mkt_ratio >= 140.0:
                    market_summary["risk_level"] = "WARNING"
                    market_summary["risk_label"] = "去槓桿警戒區 (140%~165%)"
                else:
                    market_summary["risk_level"] = "CALL"
                    market_summary["risk_label"] = "高風險追繳區 (< 140%)"

    except Exception as e:
        print(f"[margin_risk_service] 抓取證交所信用交易數據失敗: {e}")

    result = {
        "stat": "OK" if items else "FAIL",
        "date": market_summary.get("date", ""),
        "market_summary": market_summary,
        "items": items,
        "cached_at": now
    }
    _MARKET_MARGIN_CACHE = result
    _MARKET_MARGIN_CACHE_TIME = now
    return result
