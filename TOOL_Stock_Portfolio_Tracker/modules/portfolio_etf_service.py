# -*- coding: utf-8 -*-
"""
Stock Portfolio Tracker - 自組 ETF (Portfolio Synthetic ETF) 運算引擎與多週期績效分析服務 (portfolio_etf_service.py)
功能：
1. 將多檔存股資料整合為單一自組 ETF (Portfolio Index ETF)，生成標準 OHLCV K線與 MA5/MA20 均線。
2. 基準淨值設定 (Base NAV = 100.0)，精確計算每日資產淨值 (NAV) 與累積收益走勢。
3. 計算各股多週期績效矩陣：每日 (Day %)、每週 (Week 5d %)、每月 (Month 20d %)、全區間/年化 (Year %)、權重比重與損益貢獻。
"""

import sqlite3
import os
import sys
from typing import List, Dict, Any, Tuple, Optional

if getattr(sys, 'frozen', False):
    BASE_DIR = os.path.dirname(os.path.abspath(sys.executable))
else:
    BASE_DIR = os.path.dirname(os.path.abspath(__file__))
    if BASE_DIR.lower().endswith(".pkg") or BASE_DIR.lower().endswith(".zip") or os.path.basename(BASE_DIR) == "modules":
        BASE_DIR = os.path.dirname(BASE_DIR)

DB_FILE = os.path.join(BASE_DIR, "portfolio.db")

def get_db_connection() -> sqlite3.Connection:
    conn = sqlite3.connect(DB_FILE)
    conn.row_factory = sqlite3.Row
    return conn

def calculate_portfolio_synthetic_etf(positions: List[Dict[str, Any]], base_nav: float = 100.0) -> Dict[str, Any]:
    """
    將持股投資組合整合成一檔虛擬自組 ETF (Synthetic ETF)
    回傳字典包含：
    - symbol: "PORTFOLIO"
    - name: "自組投資組合 ETF"
    - base_nav: 100.0
    - current_nav: 最新淨值
    - nav_change_pct: 當日淨值漲跌幅 %
    - total_return_pct: 自基期以來累計報酬率 %
    - base_date: 基期起始日期
    - latest_date: 最新日期
    - total_market_value: 最新總市值
    - records: 每日合成 K 線串列 [{date, open, high, low, close, volume, market_val, nav_change_pct}, ...]
    """
    valid_positions = [p for p in positions if float(p.get("shares", 0)) > 0]
    if not valid_positions:
        return {
            "symbol": "PORTFOLIO",
            "name": "自組投資組合 ETF",
            "base_nav": base_nav,
            "current_nav": base_nav,
            "nav_change_pct": 0.0,
            "total_return_pct": 0.0,
            "base_date": "",
            "latest_date": "",
            "total_market_value": 0.0,
            "records": []
        }

    # 1. 取得每檔股票的所有歷史日 K
    stock_klines: Dict[str, Dict[str, Dict[str, float]]] = {}
    with get_db_connection() as conn:
        c = conn.cursor()
        for p in valid_positions:
            sym = p["symbol"]
            c.execute(
                "SELECT date, open, high, low, close, volume FROM history_kline WHERE symbol=? ORDER BY date ASC",
                (sym,)
            )
            rows = c.fetchall()
            stock_klines[sym] = {
                r["date"]: {
                    "open": float(r["open"] or 0),
                    "high": float(r["high"] or 0),
                    "low": float(r["low"] or 0),
                    "close": float(r["close"] or 0),
                    "volume": float(r["volume"] or 0),
                }
                for r in rows
            }

    # 2. 找出所有股票共有且資料齊全的交易日
    # 若某檔股票天數較短（如新上市或剛加入），取多數股票有資料的交集
    all_syms = list(stock_klines.keys())
    if not all_syms:
        return {"symbol": "PORTFOLIO", "records": []}

    common_dates = set(stock_klines[all_syms[0]].keys())
    for s in all_syms[1:]:
        common_dates = common_dates.intersection(set(stock_klines[s].keys()))

    sorted_dates = sorted(list(common_dates))
    if not sorted_dates:
        # 若交集為空，採用並集並向前補值 (Forward Fill)
        all_dates_set = set()
        for s in all_syms:
            all_dates_set.update(stock_klines[s].keys())
        sorted_dates = sorted(list(all_dates_set))
        
        # 進行前向補值
        last_known = {}
        for d in sorted_dates:
            for s in all_syms:
                if d in stock_klines[s]:
                    last_known[s] = stock_klines[s][d]
                elif s in last_known:
                    stock_klines[s][d] = dict(last_known[s])
                else:
                    # 若尚未有數據，找第一個有數據的值
                    first_k = next(iter(stock_klines[s].values())) if stock_klines[s] else {"open": 0, "high": 0, "low": 0, "close": 0, "volume": 0}
                    stock_klines[s][d] = dict(first_k)

    if not sorted_dates:
        return {"symbol": "PORTFOLIO", "records": []}

    # 3. 計算基準日 (第 0 天) 的收盤總市值
    base_date = sorted_dates[0]
    base_market_val = 0.0
    for p in valid_positions:
        sym = p["symbol"]
        shs = float(p.get("shares", 0))
        k = stock_klines[sym].get(base_date, {})
        base_market_val += shs * k.get("close", 0.0)

    if base_market_val <= 0:
        base_market_val = 1.0

    scale_factor = base_nav / base_market_val

    # 4. 逐日合成 OHLCV
    synthetic_records: List[Dict[str, Any]] = []
    prev_close_val = None

    for d in sorted_dates:
        open_val = 0.0
        high_val = 0.0
        low_val = 0.0
        close_val = 0.0
        tot_vol = 0.0

        for p in valid_positions:
            sym = p["symbol"]
            shs = float(p.get("shares", 0))
            k = stock_klines[sym].get(d)
            if not k:
                continue
            open_val += shs * k["open"]
            high_val += shs * k["high"]
            low_val += shs * k["low"]
            close_val += shs * k["close"]
            tot_vol += k["volume"]

        etf_open = round(open_val * scale_factor, 2)
        etf_high = round(high_val * scale_factor, 2)
        etf_low = round(low_val * scale_factor, 2)
        etf_close = round(close_val * scale_factor, 2)

        day_change = 0.0
        day_change_pct = 0.0
        if prev_close_val is not None and prev_close_val > 0:
            day_change = round(etf_close - prev_close_val, 2)
            day_change_pct = round((etf_close - prev_close_val) / prev_close_val * 100.0, 2)

        prev_close_val = etf_close

        synthetic_records.append({
            "date": d,
            "open": etf_open,
            "high": etf_high,
            "low": etf_low,
            "close": etf_close,
            "volume": int(tot_vol),
            "market_val": round(close_val, 0),
            "change": day_change,
            "change_pct": day_change_pct
        })

    # 計算 MA5 與 MA20
    closes = [r["close"] for r in synthetic_records]
    for i, r in enumerate(synthetic_records):
        if i >= 4:
            r["ma5"] = round(sum(closes[i-4:i+1]) / 5.0, 2)
        else:
            r["ma5"] = None
        if i >= 19:
            r["ma20"] = round(sum(closes[i-19:i+1]) / 20.0, 2)
        else:
            r["ma20"] = None

    latest_rec = synthetic_records[-1] if synthetic_records else {}
    current_nav = latest_rec.get("close", base_nav)
    nav_change_pct = latest_rec.get("change_pct", 0.0)
    total_return_pct = round((current_nav - base_nav) / base_nav * 100.0, 2)
    latest_market_val = latest_rec.get("market_val", 0.0)

    return {
        "symbol": "PORTFOLIO",
        "name": "自組投資組合 ETF (Composite NAV Index)",
        "base_nav": base_nav,
        "current_nav": current_nav,
        "nav_change_pct": nav_change_pct,
        "total_return_pct": total_return_pct,
        "base_date": base_date,
        "latest_date": sorted_dates[-1] if sorted_dates else "",
        "total_market_value": latest_market_val,
        "records_count": len(synthetic_records),
        "records": synthetic_records
    }

def calculate_stocks_performance_matrix(positions: List[Dict[str, Any]]) -> Dict[str, Any]:
    """
    計算各持股之多週期績效（每日、每週 5d、每月 20d、年化/全區間）與持股比重分析
    回傳：
    - total_market_value: 總市值
    - total_cost: 總成本
    - total_unrealized_pnl: 總未實現損益
    - total_unrealized_pct: 總未實現報酬率
    - items: 各股績效清單 [
        {
          symbol, name, shares, current_price, market_val, weight_pct,
          day_pct, week_pct, month_pct, year_pct,
          unrealized_pnl, unrealized_pct,
          contribution_pct
        }, ...
      ]
    """
    valid_positions = [p for p in positions if float(p.get("shares", 0)) > 0]
    if not valid_positions:
        return {
            "total_market_value": 0.0,
            "total_cost": 0.0,
            "total_unrealized_pnl": 0.0,
            "total_unrealized_pct": 0.0,
            "items": []
        }

    items = []
    tot_market_val = 0.0
    tot_cost = 0.0

    with get_db_connection() as conn:
        c = conn.cursor()
        for p in valid_positions:
            sym = p["symbol"]
            name = p.get("name", sym)
            shs = float(p.get("shares", 0))
            cost_p = float(p.get("cost_price", 0))
            pos_cost = shs * cost_p

            c.execute(
                "SELECT date, close FROM history_kline WHERE symbol=? ORDER BY date DESC",
                (sym,)
            )
            rows = c.fetchall()
            if not rows:
                items.append({
                    "symbol": sym,
                    "name": name,
                    "shares": shs,
                    "cost_price": cost_p,
                    "current_price": cost_p,
                    "market_val": pos_cost,
                    "weight_pct": 0.0,
                    "day_pct": 0.0,
                    "week_pct": 0.0,
                    "month_pct": 0.0,
                    "year_pct": 0.0,
                    "unrealized_pnl": 0.0,
                    "unrealized_pct": 0.0,
                    "contribution_pct": 0.0
                })
                tot_cost += pos_cost
                tot_market_val += pos_cost
                continue

            curr_p = float(rows[0]["close"] or 0)
            m_val = shs * curr_p
            tot_market_val += m_val
            tot_cost += pos_cost

            # 1 日前
            p_day_pct = 0.0
            if len(rows) > 1 and float(rows[1]["close"] or 0) > 0:
                prev_1 = float(rows[1]["close"])
                p_day_pct = round((curr_p - prev_1) / prev_1 * 100.0, 2)

            # 5 日前 (1 週)
            p_week_pct = 0.0
            idx_5 = min(5, len(rows) - 1)
            if idx_5 > 0 and float(rows[idx_5]["close"] or 0) > 0:
                prev_5 = float(rows[idx_5]["close"])
                p_week_pct = round((curr_p - prev_5) / prev_5 * 100.0, 2)

            # 20 日前 (1 月)
            p_month_pct = 0.0
            idx_20 = min(20, len(rows) - 1)
            if idx_20 > 0 and float(rows[idx_20]["close"] or 0) > 0:
                prev_20 = float(rows[idx_20]["close"])
                p_month_pct = round((curr_p - prev_20) / prev_20 * 100.0, 2)

            # 最長可用天數 (約半年至一年)
            p_year_pct = 0.0
            idx_year = len(rows) - 1
            if idx_year > 0 and float(rows[idx_year]["close"] or 0) > 0:
                prev_year = float(rows[idx_year]["close"])
                p_year_pct = round((curr_p - prev_year) / prev_year * 100.0, 2)

            unreal_pnl = m_val - pos_cost
            unreal_pct = round((unreal_pnl / pos_cost * 100.0), 2) if pos_cost > 0 else 0.0

            items.append({
                "symbol": sym,
                "name": name,
                "shares": shs,
                "cost_price": round(cost_p, 2),
                "current_price": round(curr_p, 2),
                "market_val": round(m_val, 0),
                "weight_pct": 0.0, # 待總市值加總後計算
                "day_pct": p_day_pct,
                "week_pct": p_week_pct,
                "month_pct": p_month_pct,
                "year_pct": p_year_pct,
                "unrealized_pnl": round(unreal_pnl, 0),
                "unrealized_pct": unreal_pct,
                "contribution_pct": 0.0
            })

    # 計算權重與損益貢獻
    for it in items:
        if tot_market_val > 0:
            it["weight_pct"] = round(it["market_val"] / tot_market_val * 100.0, 2)
            # 損益貢獻：該持股的未實現損益 / 總投資成本
            it["contribution_pct"] = round(it["unrealized_pnl"] / tot_cost * 100.0, 2) if tot_cost > 0 else 0.0

    # 依照市值權重由大到小排序
    items.sort(key=lambda x: x["market_val"], reverse=True)

    tot_unreal_pnl = tot_market_val - tot_cost
    tot_unreal_pct = round((tot_unreal_pnl / tot_cost * 100.0), 2) if tot_cost > 0 else 0.0

    return {
        "total_market_value": round(tot_market_val, 0),
        "total_cost": round(tot_cost, 0),
        "total_unrealized_pnl": round(tot_unreal_pnl, 0),
        "total_unrealized_pct": tot_unreal_pct,
        "items": items
    }

