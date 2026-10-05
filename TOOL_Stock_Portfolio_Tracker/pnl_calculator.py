import math
from typing import Dict, Any, Optional

def calculate_position_pnl(pos: Dict[str, Any], quote: Dict[str, Any], div_info: Optional[Dict[str, Any]] = None) -> Dict[str, Any]:
    """
    計算單一庫存的即時損益明細與股息資訊
    pos: 包含 symbol, shares, cost_price, fee_discount, is_etf, market
    quote: 包含 current_price, yesterday_close, change, change_pct
    div_info: 包含 cash_dividend, ex_date, status, is_announced
    """
    shares = float(pos["shares"]) # 支援零股與小數點股數
    cost_price = float(pos["cost_price"])
    fee_discount = float(pos.get("fee_discount", 0.6))
    is_etf = int(pos.get("is_etf", 0))
    market = pos.get("market", "TW").upper()

    curr_price = float(quote.get("current_price", cost_price))
    yesterday_close = float(quote.get("yesterday_close", curr_price))

    if market in ["TW", "TWO"]:
        # --- 台股交易成本規則 ---
        # 1. 買進手續費 (0.1425% * 折扣，低消20元)
        raw_buy_fee = cost_price * shares * 0.001425 * fee_discount
        buy_fee = max(20, math.floor(raw_buy_fee)) if shares >= 1000 else math.ceil(raw_buy_fee)
        total_cost = round(cost_price * shares + buy_fee)

        # 2. 目前市值
        market_val = round(curr_price * shares)

        # 3. 預估賣出手續費
        raw_sell_fee = curr_price * shares * 0.001425 * fee_discount
        sell_fee = max(20, math.floor(raw_sell_fee)) if shares >= 1000 else math.ceil(raw_sell_fee)

        # 4. 證券交易稅 (一般股票 0.3%, ETF 0.1%)
        tax_rate = 0.001 if is_etf else 0.003
        tax = math.floor(curr_price * shares * tax_rate)

        # 5. 預估淨賣出金額
        net_sell_value = market_val - sell_fee - tax

        # 6. 未實現損益與報酬率
        unrealized_pnl = net_sell_value - total_cost
        roi_pct = (unrealized_pnl / total_cost * 100) if total_cost > 0 else 0.0

        # 7. 單日損益 (相較昨日收盤)
        day_pnl = round((curr_price - yesterday_close) * shares)

    else:
        # --- 美股交易規則 (免手續費與證交稅) ---
        total_cost = round(cost_price * shares, 2)
        market_val = round(curr_price * shares, 2)
        unrealized_pnl = round(market_val - total_cost, 2)
        roi_pct = (unrealized_pnl / total_cost * 100) if total_cost > 0 else 0.0
        day_pnl = round((curr_price - yesterday_close) * shares, 2)
        buy_fee = 0
        sell_fee = 0
        tax = 0

    # --- 股息與殖利率精算 ---
    div_info = div_info or {}
    cash_div = float(div_info.get("annual_div") or div_info.get("cash_dividend", 0.0))
    single_div = float(div_info.get("single_amt") or div_info.get("single_dividend", cash_div))
    ex_date = div_info.get("latest_ex_date") or div_info.get("ex_date", "-")
    div_status = div_info.get("status", "無資料")
    is_announced = div_info.get("is_announced", 0)

    # 預估應領全年總股息 (NT$)
    total_dividend = round(shares * cash_div)
    # 成本殖利率 % (存股族最看重的報酬)
    yield_on_cost = (cash_div / cost_price * 100) if cost_price > 0 else 0.0
    # 現價殖利率 %
    yield_on_price = (cash_div / curr_price * 100) if curr_price > 0 else 0.0

    return {
        "symbol": pos["symbol"],
        "name": pos.get("name", "") or quote.get("name", pos["symbol"]),
        "shares": shares,
        "cost_price": cost_price,
        "current_price": curr_price,
        "yesterday_close": yesterday_close,
        "change": round(curr_price - yesterday_close, 2),
        "change_pct": round(((curr_price - yesterday_close) / yesterday_close * 100) if yesterday_close > 0 else 0.0, 2),
        "total_cost": total_cost,
        "market_val": market_val,
        "unrealized_pnl": unrealized_pnl,
        "roi_pct": round(roi_pct, 2),
        "day_pnl": day_pnl,
        "buy_fee": buy_fee,
        "sell_fee": sell_fee,
        "tax": tax,
        "cash_dividend": cash_div,
        "single_dividend": single_div,
        "ex_date": ex_date,
        "div_status": div_status,
        "is_announced": is_announced,
        "total_dividend": total_dividend,
        "yield_on_cost": round(yield_on_cost, 2),
        "yield_on_price": round(yield_on_price, 2)
    }
