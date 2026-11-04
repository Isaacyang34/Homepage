# -*- coding: utf-8 -*-
"""
TOOL_Stock_Portfolio_Tracker - 雲端多設備差異比對與智慧雙向合併服務 (Sync Merge Service)
功能：
1. compare_portfolios(local_data, cloud_data):
   - 精確比對本機與雲端持股，分析出「雲端獨有」、「本機獨有」與「內容差異 (股數/成本/批次)」。
2. smart_merge_into_local(local_data, cloud_data, user_id):
   - 智慧雙向合併：將雲端獨有標的與買入批次補入本機 SQLite，保留本機獨有標的，
   - 雙方都有之標的進行批次聯集合併重算，達成「兩端兼得、0 筆遺失」。
3. restore_cloud_to_local(cloud_data, user_id):
   - 以雲端持股 100% 覆蓋還原本機資料庫。
"""

import os
import sys
from typing import Dict, Any, List, Tuple, Optional

current_dir = os.path.dirname(os.path.abspath(__file__))
if current_dir not in sys.path:
    sys.path.insert(0, current_dir)

import database

def compare_portfolios(local_data: Dict[str, Any], cloud_data: Dict[str, Any]) -> Dict[str, Any]:
    """
    精確比對本機與雲端資料庫差異
    返回：
    {
        "has_diff": bool,
        "cloud_only": List[Dict],
        "local_only": List[Dict],
        "differences": List[Dict]
    }
    """
    local_positions = {p.get("symbol", "").upper(): p for p in local_data.get("positions", []) if p.get("symbol")}
    cloud_positions = {p.get("symbol", "").upper(): p for p in cloud_data.get("positions", []) if p.get("symbol")}

    local_lots = local_data.get("trade_lots", {})
    cloud_lots = cloud_data.get("trade_lots", {})

    all_symbols = set(local_positions.keys()).union(set(cloud_positions.keys()))

    cloud_only = []
    local_only = []
    differences = []

    for sym in sorted(all_symbols):
        in_local = sym in local_positions
        in_cloud = sym in cloud_positions

        if in_cloud and not in_local:
            cp = cloud_positions[sym]
            cloud_only.append({
                "symbol": sym,
                "name": cp.get("name", sym),
                "market": cp.get("market", "TW"),
                "shares": float(cp.get("shares", 0)),
                "cost_price": float(cp.get("cost_price", 0.0)),
                "trade_type": cp.get("trade_type", "CASH"),
                "margin_rate": float(cp.get("margin_rate", 0.6)),
                "note": cp.get("note", ""),
                "lots": cloud_lots.get(sym, [])
            })
        elif in_local and not in_cloud:
            lp = local_positions[sym]
            local_only.append({
                "symbol": sym,
                "name": lp.get("name", sym),
                "market": lp.get("market", "TW"),
                "shares": float(lp.get("shares", 0)),
                "cost_price": float(lp.get("cost_price", 0.0)),
                "trade_type": lp.get("trade_type", "CASH"),
                "margin_rate": float(lp.get("margin_rate", 0.6)),
                "note": lp.get("note", ""),
                "lots": local_lots.get(sym, [])
            })
        else:
            # 兩邊皆有，比對持股數、成本均價與買入批次數量
            lp = local_positions[sym]
            cp = cloud_positions[sym]
            l_shares = float(lp.get("shares", 0))
            c_shares = float(cp.get("shares", 0))
            l_cost = float(lp.get("cost_price", 0.0))
            c_cost = float(cp.get("cost_price", 0.0))
            l_lot_count = len(local_lots.get(sym, []))
            c_lot_count = len(cloud_lots.get(sym, []))

            # 允許 0.01 誤差
            shares_diff = abs(l_shares - c_shares) > 0.01
            cost_diff = abs(l_cost - c_cost) > 0.01
            lots_diff = l_lot_count != c_lot_count

            if shares_diff or cost_diff or lots_diff:
                reasons = []
                if shares_diff:
                    reasons.append(f"股數 (本機 {l_shares:,.0f} vs 雲端 {c_shares:,.0f})")
                if cost_diff:
                    reasons.append(f"成本均價 (本機 ${l_cost:,.2f} vs 雲端 ${c_cost:,.2f})")
                if lots_diff and not (shares_diff or cost_diff):
                    reasons.append(f"批次數 (本機 {l_lot_count} 筆 vs 雲端 {c_lot_count} 筆)")

                differences.append({
                    "symbol": sym,
                    "name": lp.get("name", cp.get("name", sym)),
                    "market": lp.get("market", "TW"),
                    "local_shares": l_shares,
                    "cloud_shares": c_shares,
                    "local_cost": l_cost,
                    "cloud_cost": c_cost,
                    "reason": "、".join(reasons),
                    "local_lots": local_lots.get(sym, []),
                    "cloud_lots": cloud_lots.get(sym, []),
                    "cloud_pos": cp,
                    "local_pos": lp
                })

    has_diff = bool(cloud_only or local_only or differences)
    return {
        "has_diff": has_diff,
        "cloud_only": cloud_only,
        "local_only": local_only,
        "differences": differences
    }


def smart_merge_into_local(local_data: Dict[str, Any], cloud_data: Dict[str, Any], user_id: int = 1) -> bool:
    """
    智慧雙向合併至本地 SQLite：
    1. 將雲端獨有的標的與批次新增至本機。
    2. 本地獨有的標的保留。
    3. 兩邊皆有的標的：合併 trade lots 買入批次（依日期/股數/價格去重），並自動聯立重算股數與成本均價！
    """
    diff_report = compare_portfolios(local_data, cloud_data)

    try:
        # 1. 處理雲端獨有標的
        for c_item in diff_report.get("cloud_only", []):
            sym = c_item["symbol"]
            pos_id = database.add_position(
                symbol=sym,
                name=c_item.get("name", sym),
                market=c_item.get("market", "TW"),
                shares=c_item.get("shares", 0),
                cost_price=c_item.get("cost_price", 0.0),
                note=c_item.get("note", ""),
                trade_type=c_item.get("trade_type", "CASH"),
                margin_rate=c_item.get("margin_rate", 0.6),
                user_id=user_id
            )
            # 補齊批次
            for lot in c_item.get("lots", []):
                database.add_trade_lot(
                    symbol=sym,
                    acquire_date=lot.get("acquire_date", "2026-01-01"),
                    shares=float(lot.get("shares", 0)),
                    price=float(lot.get("price", 0.0)),
                    fee=float(lot.get("fee", 0.0)),
                    note=lot.get("note", ""),
                    user_id=user_id
                )

        # 2. 處理兩端差異標的 (合併 Trade Lots)
        for d_item in diff_report.get("differences", []):
            sym = d_item["symbol"]
            pos = database.get_position_by_symbol(sym, user_id=user_id)
            if not pos:
                continue

            existing_lots = database.get_trade_lots(sym, user_id=user_id)
            # 建立現有批次特徵集 (日期, 股數, 價格)
            seen_lots = set()
            for l in existing_lots:
                key = (l.get("acquire_date", ""), round(float(l.get("shares", 0)), 2), round(float(l.get("price", 0)), 2))
                seen_lots.add(key)

            # 將雲端獨有之批次合入本地
            has_new_lot = False
            for cl in d_item.get("cloud_lots", []):
                key = (cl.get("acquire_date", ""), round(float(cl.get("shares", 0)), 2), round(float(cl.get("price", 0)), 2))
                if key not in seen_lots:
                    seen_lots.add(key)
                    database.add_trade_lot(
                        symbol=sym,
                        acquire_date=cl.get("acquire_date", "2026-01-01"),
                        shares=float(cl.get("shares", 0)),
                        price=float(cl.get("price", 0.0)),
                        fee=float(cl.get("fee", 0.0)),
                        note=cl.get("note", ""),
                        user_id=user_id
                    )
                    has_new_lot = True

            # 依合併後的所有批次重新計算持股與成本均價
            database.sync_portfolio_from_lots(sym, user_id=user_id)

        return True
    except Exception as e:
        print(f"[sync_merge_service] 智慧合併失敗: {e}")
        return False


def restore_cloud_to_local(cloud_data: Dict[str, Any], user_id: int = 1) -> bool:
    """
    以雲端持股 100% 覆蓋還原本機資料庫
    """
    try:
        # 1. 清空本機此使用者目前的所有持股與批次
        existing_pos = database.get_all_positions(user_id=user_id)
        for p in existing_pos:
            database.delete_position(p["id"], user_id=user_id)

        # 2. 依雲端資料重新建立
        cloud_positions = cloud_data.get("positions", [])
        cloud_lots = cloud_data.get("trade_lots", {})

        for cp in cloud_positions:
            sym = cp.get("symbol", "").upper()
            if not sym:
                continue
            pos_id = database.add_position(
                symbol=sym,
                name=cp.get("name", sym),
                market=cp.get("market", "TW"),
                shares=float(cp.get("shares", 0)),
                cost_price=float(cp.get("cost_price", 0.0)),
                note=cp.get("note", ""),
                trade_type=cp.get("trade_type", "CASH"),
                margin_rate=float(cp.get("margin_rate", 0.6)),
                user_id=user_id
            )
            # 建立批次
            for lot in cloud_lots.get(sym, []):
                database.add_trade_lot(
                    symbol=sym,
                    acquire_date=lot.get("acquire_date", "2026-01-01"),
                    shares=float(lot.get("shares", 0)),
                    price=float(lot.get("price", 0.0)),
                    fee=float(lot.get("fee", 0.0)),
                    note=lot.get("note", ""),
                    user_id=user_id
                )

        return True
    except Exception as e:
        print(f"[sync_merge_service] 覆蓋還原失敗: {e}")
        return False
