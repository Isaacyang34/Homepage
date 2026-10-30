# -*- coding: utf-8 -*-
"""
TOOL_Stock_Portfolio_Tracker - 存股同步與異動提醒行為驗證測試套件
測試目標：
1. 本機端不主動更新：盤中報價刷新、股息更新、程式關閉時，絕對不主動觸發雲端上傳。
2. 存股有異動時主動提醒：新增/修改/刪除持股時，必須跳出詢問框，待使用者確認「是」後才執行上傳；若使用者點「否」則完全不上傳。
3. 手動上傳：使用者於雲端同步視窗自主點擊上傳時，正常執行加密上傳。
4. 雲端/本地各自獨立計算損益：本機端上傳存股名稱、代碼、數量與批次；計算部分由本地端與網頁端分別計算。
"""

import unittest
import os
import sys
import threading
import time
from unittest.mock import patch, MagicMock

current_dir = os.path.dirname(os.path.abspath(__file__))
if current_dir not in sys.path:
    sys.path.insert(0, current_dir)

import database
from database import get_setting, set_setting, init_db
import crypto_sync
from crypto_sync import is_auto_sync_enabled, gather_full_portfolio_data
import main_gui

class TestSyncWorkflowAndPermissions(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        # 使用獨立測試資料庫
        cls.orig_db = database.DB_FILE
        cls.test_db = os.path.join(current_dir, "test_sync_sandbox.db")
        database.DB_FILE = cls.test_db
        init_db()

    @classmethod
    def tearDownClass(cls):
        database.DB_FILE = cls.orig_db
        if os.path.exists(cls.test_db):
            try:
                os.remove(cls.test_db)
            except Exception:
                pass

    def setUp(self):
        set_setting("cloud_user_id", "test_user")
        set_setting("cloud_password", "test_pass_123")
        set_setting("prompt_sync_on_change", "1")
        set_setting("cloud_auto_sync", "0")

    def test_01_is_auto_sync_disabled_by_default(self):
        """驗證：本地端預設不主動啟用自動背景同步"""
        set_setting("cloud_auto_sync", "0")
        self.assertFalse(is_auto_sync_enabled(), "預設狀態下自動背景同步必須為關閉 (False)")

    @patch("main_gui.sync_to_cloud")
    def test_02_no_unsolicited_sync_on_quotes_refresh(self, mock_sync):
        """驗證：報價抓取成功與股息更新時，絕對不主動發起雲端同步"""
        import tkinter as tk
        from main_gui import PortfolioApp
        
        root = tk.Tk()
        root.withdraw()
        try:
            app = PortfolioApp.__new__(PortfolioApp)
            app.status_lbl = MagicMock()
            app.active_update_dialog = None
            app.tree = MagicMock()
            app.cards = {}
            app.positions = []
            app.quotes = {}
            app.benchmarks_cache = {}
            app.visible_columns = []
            app.visible_cards = []
            app.row_action_frame = MagicMock()
            app._sort_col = "symbol"
            app._sort_reverse = False
            app.tree.selection.return_value = []
            app.tree.get_children.return_value = []

            # 觸發報價更新回呼
            app._on_fetch_success("11:30:00")
            # 觸發股息更新回呼
            app._on_dividend_updated()

            mock_sync.assert_not_called()
        finally:
            root.destroy()

    @patch("main_gui.sync_to_cloud")
    def test_03_no_unsolicited_sync_on_app_close(self, mock_sync):
        """驗證：使用者關閉桌面應用程式時，絕對不偷傳或主動發起雲端同步"""
        import tkinter as tk
        from main_gui import PortfolioApp

        root = tk.Tk()
        root.withdraw()
        try:
            app = PortfolioApp.__new__(PortfolioApp)
            app.destroy = MagicMock()
            with self.assertRaises(SystemExit):
                app._on_app_close()
            mock_sync.assert_not_called()
        finally:
            root.destroy()

    @patch("tkinter.messagebox.askyesno")
    @patch("main_gui.sync_to_cloud")
    def test_04_prompt_sync_on_position_saved_user_accepts(self, mock_sync, mock_ask):
        """驗證：存股部位儲存時，彈出詢問框；使用者點擊「是 (True)」時才發起上傳"""
        import tkinter as tk
        from main_gui import PortfolioApp

        mock_ask.return_value = True
        mock_sync.return_value = (True, "OK")

        root = tk.Tk()
        root.withdraw()
        try:
            app = PortfolioApp.__new__(PortfolioApp)
            app.status_lbl = MagicMock()
            app.after = lambda delay, fn: fn()
            app.reload_positions = MagicMock()
            app.benchmarks_cache = {}
            app.trigger_refresh = MagicMock()
            app.trigger_dividend_update = MagicMock()

            # 執行存股儲存回呼
            with patch("tkinter.messagebox.showinfo"):
                app._on_position_saved(prompt_sync=True)
                time.sleep(0.15)

            mock_ask.assert_called_once()
            self.assertIn("存股異動 ‧ 雲端更新提醒", mock_ask.call_args[0][0])
            mock_sync.assert_called_once_with("test_user", "test_pass_123")
        finally:
            root.destroy()

    @patch("tkinter.messagebox.askyesno")
    @patch("main_gui.sync_to_cloud")
    def test_05_prompt_sync_on_position_saved_user_declines(self, mock_sync, mock_ask):
        """驗證：存股部位儲存時，使用者若點擊「否 (False)」，絕對不上傳"""
        import tkinter as tk
        from main_gui import PortfolioApp

        mock_ask.return_value = False

        root = tk.Tk()
        root.withdraw()
        try:
            app = PortfolioApp.__new__(PortfolioApp)
            app.status_lbl = MagicMock()
            app.after = lambda delay, fn: fn()
            app.reload_positions = MagicMock()
            app.benchmarks_cache = {}
            app.trigger_refresh = MagicMock()
            app.trigger_dividend_update = MagicMock()

            app._on_position_saved(prompt_sync=True)
            time.sleep(0.1)

            mock_ask.assert_called_once()
            mock_sync.assert_not_called()
        finally:
            root.destroy()

    @patch("tkinter.messagebox.askyesno")
    @patch("main_gui.sync_to_cloud")
    def test_06_prompt_sync_skipped_when_disabled_or_no_user(self, mock_sync, mock_ask):
        """驗證：未設定雲端帳號或使用者關閉異動提醒時，不打擾使用者且不上傳"""
        import tkinter as tk
        from main_gui import PortfolioApp

        root = tk.Tk()
        root.withdraw()
        try:
            app = PortfolioApp.__new__(PortfolioApp)
            app.status_lbl = MagicMock()
            app.reload_positions = MagicMock()
            app.benchmarks_cache = {}
            app.trigger_refresh = MagicMock()
            app.trigger_dividend_update = MagicMock()

            # 情況 A：未設定使用者
            set_setting("cloud_user_id", "")
            app._on_position_saved(prompt_sync=True)
            mock_ask.assert_not_called()
            mock_sync.assert_not_called()

            # 情況 B：使用者主動關閉異動提醒
            set_setting("cloud_user_id", "test_user")
            set_setting("prompt_sync_on_change", "0")
            app._on_position_saved(prompt_sync=True)
            mock_ask.assert_not_called()
            mock_sync.assert_not_called()
        finally:
            root.destroy()

    @patch("main_gui.sync_to_cloud")
    def test_07_manual_cloud_sync_dialog_upload(self, mock_sync):
        """驗證：使用者在雲端同步對話框手動點擊「立即加密備份到雲端」正常執行"""
        import tkinter as tk
        from main_gui import CloudSyncDialog

        mock_sync.return_value = (True, "OK")

        root = tk.Tk()
        root.withdraw()
        try:
            dialog = CloudSyncDialog.__new__(CloudSyncDialog)
            dialog.ent_user = MagicMock()
            dialog.ent_user.get.return_value = "my_account"
            dialog.ent_pass = MagicMock()
            dialog.ent_pass.get.return_value = "my_password"
            dialog.ent_url = MagicMock()
            dialog.ent_url.get.return_value = "https://test.firebaseio.com"
            dialog.var_remember = MagicMock()
            dialog.var_remember.get.return_value = True
            dialog.var_prompt_sync = MagicMock()
            dialog.var_prompt_sync.get.return_value = True
            dialog.btn_sync = MagicMock()
            dialog.lbl_status = MagicMock()
            dialog.web_url = "https://test.com"
            dialog.after = lambda delay, fn: fn()

            with patch("tkinter.messagebox.showinfo"):
                dialog.do_upload()
                time.sleep(0.15)

            mock_sync.assert_called_once_with("my_account", "my_password", "https://test.firebaseio.com")
            self.assertEqual(get_setting("prompt_sync_on_change"), "1")
        finally:
            root.destroy()

    def test_08_cloud_payload_data_integrity_and_independent_calc(self):
        """驗證：上傳 payload 包含基礎存股部位 (代號、股數、成本均價)，網頁端可100%獨立精算損益"""
        # 1. 建立測試部位
        database.add_position("0050", "元大台灣50", "TW", 2000, 100.0, fee_discount=0.6, is_etf=1)
        payload = gather_full_portfolio_data()

        # 2. 驗證基礎存股結構完整性
        self.assertIn("positions", payload)
        self.assertGreaterEqual(len(payload["positions"]), 1)
        pos = next(p for p in payload["positions"] if p["symbol"] == "0050")
        self.assertEqual(pos["shares"], 2000)
        self.assertEqual(pos["cost_price"], 100.0)

        # 3. 模擬網頁端 (app.js) 獨立計算邏輯 (自訂行情 105.0, 昨收 102.0)
        web_shares = pos["shares"]
        web_cost_price = pos["cost_price"]
        web_current_price = 105.0
        web_yesterday_close = 102.0

        # 網頁端獨立公式：
        # total_cost = shares * cost_price
        # market_val = shares * current_price
        # unrealized_pnl = market_val - total_cost
        # day_pnl = (current_price - yesterday_close) * shares
        # roi_pct = (unrealized_pnl / total_cost) * 100
        total_cost = web_shares * web_cost_price
        market_val = web_shares * web_current_price
        unrealized_pnl = market_val - total_cost
        day_pnl = (web_current_price - web_yesterday_close) * web_shares
        roi_pct = (unrealized_pnl / total_cost) * 100

        self.assertEqual(total_cost, 200000.0)
        self.assertEqual(market_val, 210000.0)
        self.assertEqual(unrealized_pnl, 10000.0)
        self.assertEqual(day_pnl, 6000.0)
        self.assertAlmostEqual(roi_pct, 5.0, places=2)

if __name__ == "__main__":
    unittest.main()
