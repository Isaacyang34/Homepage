import math
from typing import Dict, Any, Optional

def calculate_position_pnl(pos: Dict[str, Any], 
                           quote: Dict[str, Any], 
                           benchmarks: Optional[Dict[str, float]] = None,
                           div_info: Optional[Dict[str, Any]] = None) -> Dict[str, Any]:
    """
    計算單一庫存的即時損益明細 (支援日/週/月分開統計) 與股息分配資訊
    pos: 包含 symbol, shares, cost_price, fee_discount, is_etf, market
    quote: 包含 current_price, yesterday_close, change, change_pct
    benchmarks: 包含 yesterday_close, week_close, month_close (從本地歷史K線提取)
    div_info: 包含 annual_div, single_amt, ex_date, frequency, payment_month, hist_div_received
    """
    shares = float(pos["shares"]) # 支援零股與小數點股數
    cost_price = float(pos["cost_price"])
    fee_discount = float(pos.get("fee_discount", 0.6))
    is_etf = int(pos.get("is_etf", 0))
    market = pos.get("market", "TW").upper()

    curr_price = float(quote.get("current_price", cost_price))
    
    # 歷史基準收盤價 (日/週/月)
    benchmarks = benchmarks or {}
    y_close = float(benchmarks.get("yesterday_close", quote.get("yesterday_close", curr_price)))
    w_close = float(benchmarks.get("week_close", y_close))
    m_close = float(benchmarks.get("month_close", y_close))

    if market in ["TW", "TWO"]:
        # --- 台股交易成本規則 ---
        raw_buy_fee = cost_price * shares * 0.001425 * fee_discount
        buy_fee = max(20, math.floor(raw_buy_fee)) if shares >= 1000 else math.ceil(raw_buy_fee)
        total_cost = round(cost_price * shares + buy_fee)

        market_val = round(curr_price * shares)

        raw_sell_fee = curr_price * shares * 0.001425 * fee_discount
        sell_fee = max(20, math.floor(raw_sell_fee)) if shares >= 1000 else math.ceil(raw_sell_fee)

        tax_rate = 0.001 if is_etf else 0.003
        tax = math.floor(curr_price * shares * tax_rate)

        net_sell_value = market_val - sell_fee - tax

        unrealized_pnl = net_sell_value - total_cost
        roi_pct = (unrealized_pnl / total_cost * 100) if total_cost > 0 else 0.0

    else:
        # --- 美股交易規則 ---
        total_cost = round(cost_price * shares, 2)
        market_val = round(curr_price * shares, 2)
        unrealized_pnl = round(market_val - total_cost, 2)
        roi_pct = (unrealized_pnl / total_cost * 100) if total_cost > 0 else 0.0
        buy_fee = 0
        sell_fee = 0
        tax = 0

    # --- 日、週、月分開統計損益 ---
    day_pnl = round((curr_price - y_close) * shares)
    day_pct = ((curr_price - y_close) / y_close * 100) if y_close > 0 else 0.0

    week_pnl = round((curr_price - w_close) * shares)
    week_pct = ((curr_price - w_close) / w_close * 100) if w_close > 0 else 0.0

    month_pnl = round((curr_price - m_close) * shares)
    month_pct = ((curr_price - m_close) / m_close * 100) if m_close > 0 else 0.0

    # --- 股息與殖利率精算 ---
    div_info = div_info or {}
    cash_div = float(div_info.get("annual_div") or div_info.get("cash_dividend", 0.0))
    single_div = float(div_info.get("single_amt") or div_info.get("single_dividend", cash_div))
    ex_date = div_info.get("latest_ex_date") or div_info.get("ex_date", "-")
    frequency = div_info.get("frequency", "年配")
    payment_month = div_info.get("payment_month", "--")
    div_status = div_info.get("status", "無資料")
    is_announced = div_info.get("is_announced", 0)

    # 預估應領全年總股息 (NT$)
    total_dividend = round(shares * cash_div)
    # 成本殖利率 %
    yield_on_cost = (cash_div / cost_price * 100) if cost_price > 0 else 0.0
    # 現價殖利率 %
    yield_on_price = (cash_div / curr_price * 100) if curr_price > 0 else 0.0

    # 依取得時間批次精算的「歷年已領歷史現金股利」
    hist_div_received = float(div_info.get("hist_div_received", 0.0))
    lot_count = int(div_info.get("lot_count", 0))

    return {
        "symbol": pos["symbol"],
        "name": pos.get("name", "") or quote.get("name", pos["symbol"]),
        "shares": shares,
        "cost_price": cost_price,
        "current_price": curr_price,
        "yesterday_close": y_close,
        "week_close": w_close,
        "month_close": m_close,
        "change": round(curr_price - y_close, 2),
        "change_pct": round(day_pct, 2),
        "total_cost": total_cost,
        "market_val": market_val,
        "unrealized_pnl": unrealized_pnl,
        "roi_pct": round(roi_pct, 2),
        # 週期損益
        "day_pnl": day_pnl,
        "day_pct": round(day_pct, 2),
        "week_pnl": week_pnl,
        "week_pct": round(week_pct, 2),
        "month_pnl": month_pnl,
        "month_pct": round(month_pct, 2),
        # 股息資訊
        "cash_dividend": cash_div,
        "single_dividend": single_div,
        "ex_date": ex_date,
        "frequency": frequency,
        "payment_month": payment_month,
        "div_status": div_status,
        "is_announced": is_announced,
        "total_dividend": total_dividend,
        "yield_on_cost": round(yield_on_cost, 2),
        "yield_on_price": round(yield_on_price, 2),
        "hist_div_received": hist_div_received,
        "lot_count": lot_count,
        "buy_fee": buy_fee,
        "sell_fee": sell_fee,
        "tax": tax
    }
