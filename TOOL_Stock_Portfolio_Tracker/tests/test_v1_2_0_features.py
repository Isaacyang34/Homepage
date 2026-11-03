# -*- coding: utf-8 -*-
"""
TOOL_Stock_Portfolio_Tracker V1.2.0.0 功能驗證自動化測試腳本
遵守 Universal Test Script Generation Rule (4 階段生命週期)
"""
import os
import sys
import json
import unittest

# 設定模組搜尋路徑
BASE_DIR = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
if BASE_DIR not in sys.path:
    sys.path.insert(0, BASE_DIR)
MODULES_DIR = os.path.join(BASE_DIR, "modules")
if MODULES_DIR not in sys.path:
    sys.path.insert(0, MODULES_DIR)

from stock_fundamental_service import (
    StockFundamentalService,
    MarketRegimeEngine,
    FunnelQuantScreener,
    PostMarketQuantRanker
)

class TestV1200Features(unittest.TestCase):
    """V1.2.0.0 核心演算法與服務功能驗證"""

    def setUp(self):
        """Phase 1: Setup"""
        self.tsmc_price = 2490.0
        self.tsmc_eps = 102.5
        self.test_stocks = [
            {
                "symbol": "2330.TW", "name": "台積電", "turnover_million": 45000.0,
                "is_disposed": False, "book_value": 450.0, "current_price": 2490.0,
                "score_momentum": 95.0, "score_chips": 94.0, "score_quality": 98.0,
                "score_value": 75.0, "score_risk": 88.0, "atr_20": 48.0
            },
            {
                "symbol": "1513.TW", "name": "中興電(處置)", "turnover_million": 1200.0,
                "is_disposed": True, "book_value": 35.0, "current_price": 160.0,
                "score_momentum": 85.0, "score_chips": 60.0, "score_quality": 70.0,
                "score_value": 65.0, "score_risk": 50.0, "atr_20": 8.0
            },
            {
                "symbol": "9999.TW", "name": "冷凍股", "turnover_million": 8.5,
                "is_disposed": False, "book_value": 15.0, "current_price": 25.0,
                "score_momentum": 40.0, "score_chips": 40.0, "score_quality": 50.0,
                "score_value": 70.0, "score_risk": 60.0, "atr_20": 1.2
            }
        ]

    def test_01_pe_river_band_calculation(self):
        """驗證本益比河流圖倍數與位階"""
        result = StockFundamentalService.get_pe_river_band(
            current_price=self.tsmc_price,
            eps_ttm=self.tsmc_eps,
            historical_eps=[72.0, 80.5, 88.0, 95.0, 102.5]
        )
        # 斷言 PE 數值
        expected_pe = round(self.tsmc_price / self.tsmc_eps, 1)
        self.assertEqual(result["current_pe"], expected_pe)
        # 斷言 16x, 20x, 24x, 28x, 32x 估值階梯
        self.assertEqual(result["bands"]["16x"], round(self.tsmc_eps * 16.0, 1))
        self.assertEqual(result["bands"]["24x"], round(self.tsmc_eps * 24.0, 1))
        self.assertEqual(result["bands"]["32x"], round(self.tsmc_eps * 32.0, 1))
        # 斷言歷史點數
        self.assertEqual(len(result["history_points"]), 5)
        print("  [PASS] test_01_pe_river_band_calculation 通過")

    def test_02_dupont_analysis(self):
        """驗證杜邦分析三要素拆解"""
        net_margin = 43.2
        asset_turnover = 0.65
        equity_multiplier = 1.48
        
        result = StockFundamentalService.get_dupont_analysis(net_margin, asset_turnover, equity_multiplier)
        expected_roe = round((net_margin / 100.0) * asset_turnover * equity_multiplier * 100.0, 2)
        self.assertEqual(result["roe"], expected_roe)
        self.assertIn("高定價權", result["primary_drivers"][0])
        print("  [PASS] test_02_dupont_analysis 通過")

    def test_03_market_regime_engine(self):
        """驗證市場環境層判定與自適應調權"""
        # 多頭情境
        bull_regime = MarketRegimeEngine.evaluate_regime(
            index_close=23850.0, index_ma60=22920.0,
            advance_decline_ratio=1.38, foreign_futures_net_oi=-8500
        )
        self.assertIn("多頭擴張", bull_regime["regime"])
        self.assertEqual(bull_regime["dynamic_weights"]["momentum"], 0.35)
        self.assertEqual(bull_regime["dynamic_weights"]["chips"], 0.30)

        # 空頭情境 (跌破季線)
        bear_regime = MarketRegimeEngine.evaluate_regime(
            index_close=21500.0, index_ma60=22920.0,
            advance_decline_ratio=0.65, foreign_futures_net_oi=-32000
        )
        self.assertIn("空頭防禦", bear_regime["regime"])
        self.assertEqual(bear_regime["dynamic_weights"]["value"], 0.35)
        self.assertEqual(bear_regime["dynamic_weights"]["quality"], 0.30)
        print("  [PASS] test_03_market_regime_engine 通過")

    def test_04_funnel_quant_screener(self):
        """驗證五層漏斗篩選與 ATR 部位建議"""
        regime_info = MarketRegimeEngine.evaluate_regime(23850.0, 22920.0, 1.38, -8500)
        funnel_res = FunnelQuantScreener.screen_candidates(self.test_stocks, regime_info)

        # 斷言硬性濾網淘汰
        self.assertEqual(funnel_res["eliminated_count"], 2)
        elim_symbols = [e["symbol"] for e in funnel_res["eliminated_list"]]
        self.assertIn("1513.TW", elim_symbols) # 處置股被淘汰
        self.assertIn("9999.TW", elim_symbols) # 成交值 < 2000萬被淘汰

        # 斷言入選名單
        self.assertEqual(funnel_res["passed_count"], 1)
        top_pick = funnel_res["ranked_candidates"][0]
        self.assertEqual(top_pick["symbol"], "2330.TW")
        self.assertGreater(top_pick["composite_score"], 90.0)
        # 斷言部位配置與停損
        self.assertEqual(top_pick["risk_metrics"]["suggested_position_pct"], 22)
        self.assertEqual(top_pick["risk_metrics"]["stop_loss_price"], 2490.0 - (48.0 * 2.0))
        print("  [PASS] test_04_funnel_quant_screener 通過")

    def test_05_web_html_and_js_integrity(self):
        """驗證正式網頁版元件與 JS 綁定完整性"""
        html_file = os.path.join(BASE_DIR, "web", "index.html")
        app_js_file = os.path.join(BASE_DIR, "web", "app.js")

        with open(html_file, "r", encoding="utf-8") as f:
            html = f.read()
        with open(app_js_file, "r", encoding="utf-8") as f:
            js = f.read()

        # 斷言主 Tab 與子 Tab 存在
        self.assertIn('id="tab-btn-fundamental"', html)
        self.assertIn('id="fundamental-tab-content"', html)
        self.assertIn('id="sub-tab-btn-postmarket"', html)
        self.assertIn('id="sub-content-postmarket"', html)
        self.assertIn('id="peRiverChart"', html)
        self.assertIn('id="revYoyChart"', html)
        self.assertIn('id="marginsChart"', html)

        # 斷言無任何特定第三方詞彙殘留
        self.assertNotIn("財報狗", html)
        self.assertNotIn("statementdog", html.lower())
        self.assertNotIn("財報狗", js)
        self.assertNotIn("statementdog", js.lower())

        # 斷言 JS 核心跳轉與繪圖函式存在
        self.assertIn("openStockFundamentalAnalysis", js)
        self.assertIn("renderFundamentalAnalysis", js)
        self.assertIn("renderPostMarketRankings", js)
        self.assertIn("renderPeRiverChart", js)
        print("  [PASS] test_05_web_html_and_js_integrity 通過")


def run_all_tests():
    suite = unittest.TestLoader().loadTestsFromTestCase(TestV1200Features)
    runner = unittest.TextTestRunner(verbosity=2)
    result = runner.run(suite)
    return {
        "status": "PASS" if result.wasSuccessful() else "FAIL",
        "total_run": result.testsRun,
        "failures": len(result.failures),
        "errors": len(result.errors)
    }

if __name__ == "__main__":
    res = run_all_tests()
    print(f"\n執行結果契約: {json.dumps(res, ensure_ascii=False)}")
    if res["status"] != "PASS":
        sys.exit(1)

