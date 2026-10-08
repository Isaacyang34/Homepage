# -*- coding: utf-8 -*-
"""
範例外掛：自訂均線與技術分析指標擴充
放置於 plugins/ 目錄下，開機時將由 plugin_manager 自動載入。
"""

def register():
    """外掛註冊入口點"""
    print("[Plugin: ExampleIndicator] 自訂技術指標外掛已自動註冊成功！")

def calculate_custom_bias(close_price: float, ma20_price: float) -> float:
    """計算自訂乖離率 (BIAS)"""
    if ma20_price <= 0:
        return 0.0
    return round(((close_price - ma20_price) / ma20_price) * 100, 2)
