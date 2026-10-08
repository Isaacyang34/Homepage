import math
import os
import json
from typing import Dict, Any, Optional

_BROKER_CONFIG_CACHE = None

def get_broker_config() -> Dict[str, Any]:
    global _BROKER_CONFIG_CACHE
    if _BROKER_CONFIG_CACHE is not None:
        return _BROKER_CONFIG_CACHE
    
    app_dir = os.path.dirname(os.path.abspath(__file__))
    cfg_path = os.path.join(app_dir, "config", "broker_rules.json")
    if not os.path.exists(cfg_path):
        # 往上或當前執行檔目錄檢查
        root_dir = os.path.dirname(os.path.abspath(__file__))
        cfg_path = os.path.join(root_dir, "..", "config", "broker_rules.json")

    if os.path.exists(cfg_path):
        try:
            with open(cfg_path, "r", encoding="utf-8") as f:
                _BROKER_CONFIG_CACHE = json.load(f)
                return _BROKER_CONFIG_CACHE
        except Exception:
            pass
            
    # 預設回退設定
    _BROKER_CONFIG_CACHE = {
        "base_fee_rate": 0.001425,
        "default_min_fee": 20,
        "tax_rates": {"stock_sell": 0.003, "etf_sell": 0.001}
    }
    return _BROKER_CONFIG_CACHE

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
    b_cfg = get_broker_config()
    base_fee_rate = float(b_cfg.get("base_fee_rate", 0.001425))
    default_min_fee = int(b_cfg.get("default_min_fee", 20))
    tax_rates = b_cfg.get("tax_rates", {})

    shares = float(pos["shares"]) # 支援零股與小數點股數
    cost_price = float(pos["cost_price"])
    fee_discount = float(pos.get("fee_discount", 0.6))
    is_etf = int(pos.get("is_etf", 0))
    market = pos.get("market", "TW").upper()

    curr_price = float(quote.get("current_price", cost_price))
    
    # 昨收價優先採用即時行情官方昨日收盤價 (TWSE MIS 官方昨天收盤)，若無才依序降級為歷史K線昨收或現價
    q_yclose = float(quote.get("yesterday_close") or 0.0)
    b_yclose = float((benchmarks or {}).get("yesterday_close") or 0.0)
    y_close = q_yclose if q_yclose > 0 else (b_yclose if b_yclose > 0 else curr_price)
    
    w_close = float((benchmarks or {}).get("week_close") or y_close)
    if w_close <= 0:
        w_close = y_close
    m_close = float((benchmarks or {}).get("month_close") or y_close)
    if m_close <= 0:
        m_close = y_close

    if market in ["TW", "TWO"]:
        # --- 台股交易成本規則 (支援外部 JSON 參數) ---
        raw_buy_fee = cost_price * shares * base_fee_rate * fee_discount
        buy_fee = max(default_min_fee, math.floor(raw_buy_fee)) if shares >= 1000 else math.ceil(raw_buy_fee)
        total_cost = round(cost_price * shares + buy_fee)

        market_val = round(curr_price * shares)

        raw_sell_fee = curr_price * shares * base_fee_rate * fee_discount
        sell_fee = max(default_min_fee, math.floor(raw_sell_fee)) if shares >= 1000 else math.ceil(raw_sell_fee)

        tax_rate = float(tax_rates.get("etf_sell", 0.001)) if is_etf else float(tax_rates.get("stock_sell", 0.003))
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

    # ETF 淨值與折溢價 (支援動態即時重算)
    nav = float(quote.get("nav") or 0.0)
    prem_disc = quote.get("prem_disc")
    is_etf_flag = bool(is_etf or quote.get("is_etf", False))
    if is_etf_flag and nav > 0 and curr_price > 0:
        prem_disc = round((curr_price - nav) / nav * 100, 2)

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
        "nav": nav,
        "prem_disc": prem_disc,
        "is_etf": is_etf_flag,
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
