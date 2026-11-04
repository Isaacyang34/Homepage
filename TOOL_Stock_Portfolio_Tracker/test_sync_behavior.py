# -*- coding: utf-8 -*-
"""
TOOL_Stock_Portfolio_Tracker - 存股同步與異動提醒、差異比對與智慧合併行為驗證測試套件
測試目標：
1. 啟動無聯網：軟體開機時絕對不比對雲端資料，維持 100% 離線。
2. 背景定時同步徹底停用：is_auto_sync_enabled() 恆為 False。
3. 盤中報價刷新、股息更新平時運作時，絕對不主動觸發雲端上傳。
4. 關閉前確認：僅在當前會話有存股部位異動時，於關閉前彈出密碼核實與同步確認。
5. 差異比對與智慧合併：
   - 雲端獨有、本機獨有、內容差異精確分析。
   - 智慧雙向合併 (0 筆遺失、兩端融合)。
   - 差異決策視窗 (SyncConflictDialog)。
   - 手動上傳與關閉前同步觸發差異決策。
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
from crypto_sync import (
    is_auto_sync_enabled,
    trigger_auto_sync_to_cloud,
    gather_full_portfolio_data
)
from sync_merge_service import (
    compare_portfolios,
    smart_merge_into_local,
    restore_cloud_to_local
)

class TestSyncWorkflowAndPermissions(unittest.TestCase):
    def setUp(self):
        self.orig_db = database.DB_FILE
        self.test_db = os.path.join(current_dir, "test_sync_behavior.db")
        database.DB_FILE = self.test_db
        if os.path.exists(self.test_db):
            try:
                os.remove(self.test_db)
            except Exception:
                pass
        database.init_db()

    def tearDown(self):
        if os.path.exists(self.test_db):
            try:
                os.remove(self.test_db)
            except Exception:
                pass
        database.DB_FILE = self.orig_db

    def test_01_auto_sync_permanently_disabled(self):
        """驗證：背景定時自動同步功能已徹底關閉，不接受任何背景自動上傳"""
        self.assertFalse(is_auto_sync_enabled(), "背景定時同步必須永久為 False")
        trigger_auto_sync_to_cloud(force=True)

    @patch("crypto_sync.sync_to_cloud")
    def test_02_no_sync_on_quotes_or_dividend_refresh(self, mock_sync):
        """驗證：盤中刷新即時報價或更新股息公告時，絕對不上傳雲端"""
        import tkinter as tk
        from main_gui import PortfolioApp

        root = tk.Tk()
        root.withdraw()
        try:
            app = PortfolioApp.__new__(PortfolioApp)
            app.status_lbl = MagicMock()
            app.cards = {}
            app.positions = []
            app.tree = MagicMock()
            app.tree.selection.return_value = []
            app.tree.get_children.return_value = []
            app._sort_col = "symbol"
            app._sort_reverse = False
            app.visible_columns = []
            app.row_action_frame = MagicMock()
            app.portfolio_etf_frame = None
            app.current_main_tab = "portfolio"
            app.active_update_dialog = None

            app._on_fetch_success("11:30:00")
            app._on_dividend_updated()

            mock_sync.assert_not_called()
        finally:
            root.destroy()

    @patch("crypto_sync.sync_to_cloud")
    def test_03_no_sync_on_app_close_without_changes(self, mock_sync):
        """驗證：無持股異動時關閉桌面程式，0 彈窗、0 偷傳"""
        import tkinter as tk
        from main_gui import PortfolioApp

        root = tk.Tk()
        root.withdraw()
        try:
            app = PortfolioApp.__new__(PortfolioApp)
            app.tk = MagicMock()
            app.user_id = 1
            app._portfolio_changed_in_session = False
            app.destroy = MagicMock()
            with self.assertRaises(SystemExit):
                app._on_app_close()
            mock_sync.assert_not_called()
        finally:
            root.destroy()

    def test_04_position_saved_marks_session_dirty_without_sync(self):
        """驗證：存股部位儲存時，標記會話異動，但平時絕對不發起同步"""
        import tkinter as tk
        from main_gui import PortfolioApp

        root = tk.Tk()
        root.withdraw()
        try:
            app = PortfolioApp.__new__(PortfolioApp)
            app.tk = MagicMock()
            app._portfolio_changed_in_session = False
            app.reload_positions = MagicMock()
            app.benchmarks_cache = {}
            app.trigger_refresh = MagicMock()
            app.trigger_dividend_update = MagicMock()

            app._on_position_saved(prompt_sync=False)

            self.assertTrue(app._portfolio_changed_in_session, "存股異動後必須標記會話為 dirty")
        finally:
            root.destroy()

    @patch("tkinter.messagebox.askyesno")
    @patch("crypto_sync.sync_to_cloud")
    def test_05_app_close_with_changes_user_declines(self, mock_sync, mock_ask):
        """驗證：有存股異動時關閉，若使用者選擇「否」，絕不上傳"""
        import tkinter as tk
        from main_gui import PortfolioApp

        database.set_user_setting(1, "cloud_user_id", "my_cloud_user")
        mock_ask.return_value = False

        root = tk.Tk()
        root.withdraw()
        try:
            app = PortfolioApp.__new__(PortfolioApp)
            app.tk = MagicMock()
            app.user_id = 1
            app._portfolio_changed_in_session = True
            app.destroy = MagicMock()

            with self.assertRaises(SystemExit):
                app._on_app_close()

            mock_ask.assert_called_once()
            self.assertIn("存股異動", mock_ask.call_args[0][0])
            mock_sync.assert_not_called()
        finally:
            root.destroy()

    def test_06_verify_cloud_password_logic(self):
        """驗證：密碼核實函式能正常執行並回傳布林與說明字串"""
        from crypto_sync import verify_cloud_password
        ok, msg = verify_cloud_password("nonexistent_test_acc_999", "pass123")
        self.assertIsInstance(ok, bool)
        self.assertIsInstance(msg, str)

    @patch("crypto_sync.fetch_and_decrypt_from_cloud")
    @patch("crypto_sync.sync_to_cloud")
    def test_07_manual_cloud_sync_dialog_upload(self, mock_sync, mock_fetch):
        """驗證：使用者在雲端同步對話框手動點擊「立即加密備份到雲端」正常執行"""
        import tkinter as tk
        from main_gui import CloudSyncDialog

        mock_fetch.return_value = (True, None, "雲端尚無存檔")
        mock_sync.return_value = (True, "OK")

        root = tk.Tk()
        root.withdraw()
        try:
            dialog = CloudSyncDialog.__new__(CloudSyncDialog)
            dialog.tk = MagicMock()
            dialog.curr_user = {"id": 1, "username": "default"}
            dialog.user_id = 1
            dialog.username = "my_account"
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

            sync_done = threading.Event()
            orig_sync = mock_sync.side_effect
            def _sync_wrapper(*args, **kwargs):
                try:
                    return (True, "OK")
                finally:
                    sync_done.set()
            mock_sync.side_effect = _sync_wrapper

            with patch("tkinter.messagebox.showinfo"):
                dialog.do_upload()
                sync_done.wait(timeout=3.0)

            mock_sync.assert_called_once_with("my_account", "my_password", "https://test.firebaseio.com", local_user_id=1)
        finally:
            root.destroy()

    def test_08_cloud_payload_data_integrity_and_independent_calc(self):
        """驗證：上傳 payload 包含基礎存股部位 (代號、股數、成本均價)，網頁端可100%獨立精算損益"""
        database.add_position("0050", "元大台灣50", "TW", 2000, 100.0, fee_discount=0.6, is_etf=1)
        payload = gather_full_portfolio_data()

        self.assertIn("positions", payload)
        self.assertIn("trade_lots", payload)

        found = next((p for p in payload["positions"] if p["symbol"] == "0050"), None)
        self.assertIsNotNone(found)
        self.assertEqual(found["shares"], 2000)
        self.assertEqual(found["cost_price"], 100.0)

    def test_09_compare_portfolios_diff_detection(self):
        """驗證：差異比對引擎精確偵測「雲端獨有」、「本機獨有」與「內容差異」"""
        local_data = {
            "positions": [
                {"symbol": "2330", "name": "台積電", "shares": 1000, "cost_price": 600.0},
                {"symbol": "0050", "name": "元大台灣50", "shares": 2000, "cost_price": 120.0}
            ],
            "trade_lots": {
                "2330": [{"acquire_date": "2026-01-01", "shares": 1000, "price": 600.0}]
            }
        }
        cloud_data = {
            "positions": [
                {"symbol": "2330", "name": "台積電", "shares": 2000, "cost_price": 650.0},
                {"symbol": "2454", "name": "聯發科", "shares": 500, "cost_price": 1000.0}
            ],
            "trade_lots": {
                "2330": [
                    {"acquire_date": "2026-01-01", "shares": 1000, "price": 600.0},
                    {"acquire_date": "2026-02-01", "shares": 1000, "price": 700.0}
                ],
                "2454": [{"acquire_date": "2026-01-15", "shares": 500, "price": 1000.0}]
            }
        }

        diff = compare_portfolios(local_data, cloud_data)
        self.assertTrue(diff["has_diff"])
        self.assertEqual(len(diff["cloud_only"]), 1)
        self.assertEqual(diff["cloud_only"][0]["symbol"], "2454")
        self.assertEqual(len(diff["local_only"]), 1)
        self.assertEqual(diff["local_only"][0]["symbol"], "0050")
        self.assertEqual(len(diff["differences"]), 1)
        self.assertEqual(diff["differences"][0]["symbol"], "2330")

    def test_10_smart_merge_logic(self):
        """驗證：智慧雙向合併能將雲端獨有與買入批次正確補入本機，達成零資料遺失"""
        database.add_position("0050", "元大台灣50", "TW", 1000, 100.0, user_id=1)
        database.add_trade_lot("0050", "2026-01-01", 1000, 100.0, 0.0, "", user_id=1)

        local_data = {
            "positions": [{"symbol": "0050", "name": "元大台灣50", "shares": 1000, "cost_price": 100.0}],
            "trade_lots": {"0050": [{"acquire_date": "2026-01-01", "shares": 1000, "price": 100.0}]}
        }
        cloud_data = {
            "positions": [
                {"symbol": "0050", "name": "元大台灣50", "shares": 2000, "cost_price": 105.0},
                {"symbol": "2330", "name": "台積電", "shares": 500, "cost_price": 900.0}
            ],
            "trade_lots": {
                "0050": [
                    {"acquire_date": "2026-01-01", "shares": 1000, "price": 100.0},
                    {"acquire_date": "2026-02-01", "shares": 1000, "price": 110.0}
                ],
                "2330": [{"acquire_date": "2026-01-10", "shares": 500, "price": 900.0}]
            }
        }

        ok = smart_merge_into_local(local_data, cloud_data, user_id=1)
        self.assertTrue(ok)

        pos_2330 = database.get_position_by_symbol("2330", user_id=1)
        self.assertIsNotNone(pos_2330)
        self.assertEqual(pos_2330["shares"], 500)

        lots_0050 = database.get_trade_lots("0050", user_id=1)
        self.assertEqual(len(lots_0050), 2)
        pos_0050 = database.get_position_by_symbol("0050", user_id=1)
        self.assertEqual(pos_0050["shares"], 2000)
        self.assertEqual(pos_0050["cost_price"], 105.0)

    def test_11_sync_conflict_dialog_choices(self):
        """驗證：SyncConflictDialog 介面選擇按鈕能正確更新 choice 屬性"""
        import tkinter as tk
        from main_gui import SyncConflictDialog

        diff_report = {
            "has_diff": True,
            "cloud_only": [{"symbol": "2330", "name": "台積電", "shares": 1000, "cost_price": 600.0, "lots": []}],
            "local_only": [],
            "differences": []
        }

        root = tk.Tk()
        root.withdraw()
        try:
            dlg = SyncConflictDialog(root, diff_report, {}, {})
            self.assertEqual(dlg.choice, "CANCEL")
            dlg._on_smart_merge()
            self.assertEqual(dlg.choice, "SMART_MERGE")

            dlg2 = SyncConflictDialog(root, diff_report, {}, {})
            dlg2._on_local_overwrite()
            self.assertEqual(dlg2.choice, "LOCAL_OVERWRITE")
        finally:
            root.destroy()

if __name__ == "__main__":
    unittest.main()
