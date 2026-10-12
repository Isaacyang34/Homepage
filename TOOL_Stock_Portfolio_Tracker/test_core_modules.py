# -*- coding: utf-8 -*-
"""
TOOL_Stock_Portfolio_Tracker - 核心模組離線單元測試套件
測試對象：
1. pnl_calculator.py: 台美股手續費低消、ETF 優惠稅率、零股規則、日/週/月損益
2. crypto_sync.py: 32KB Padding 防長度側信道、PBKDF2 雙因子金鑰衍生、AES-256-GCM + AAD 認證解密
"""

import unittest
import json
import os
import sys

# 確保在目前目錄能直接引用
current_dir = os.path.dirname(os.path.abspath(__file__))
if current_dir not in sys.path:
    sys.path.insert(0, current_dir)

from pnl_calculator import calculate_position_pnl
from crypto_sync import (
    pad_to_fixed_block,
    unpad_fixed_block,
    encrypt_portfolio_payload,
    decrypt_portfolio_payload,
    build_aad_string,
    FIXED_BLOCK_SIZE
)

class TestPnLCalculator(unittest.TestCase):
    """測試金融損益計算引擎之數值精確度"""

    def test_tw_stock_standard(self):
        """測試台股一般股票：1000股、0.3%證交稅、手續費低消20元"""
        pos = {
            "symbol": "2330",
            "shares": 1000,
            "cost_price": 500.0,
            "fee_discount": 0.6,
            "is_etf": 0,
            "market": "TW"
        }
        quote = {
            "current_price": 520.0,
            "yesterday_close": 510.0
        }
        res = calculate_position_pnl(pos, quote)

        # 買入成本：500 * 1000 = 500,000
        # 買入手續費：500000 * 0.001425 * 0.6 = 427.5 -> floor = 427 (>= 20)
        # 總成本 = 500,427
        self.assertEqual(res["total_cost"], 500427)

        # 市值：520 * 1000 = 520,000
        self.assertEqual(res["market_val"], 520000)

        # 賣出手續費：520000 * 0.001425 * 0.6 = 444.6 -> floor = 444
        # 證交稅：520000 * 0.003 = 1560
        self.assertEqual(res["sell_fee"], 444)
        self.assertEqual(res["tax"], 1560)

        # 淨賣出金額：520000 - 444 - 1560 = 517,996
        net_sell = res["market_val"] - res["sell_fee"] - res["tax"]
        self.assertEqual(net_sell, 517996)

        # 未實現損益：517996 - 500427 = 17,569
        self.assertEqual(res["unrealized_pnl"], 17569)

        # 日損益：(520 - 510) * 1000 = 10,000
        self.assertEqual(res["day_pnl"], 10000)

    def test_tw_etf_tax_discount(self):
        """測試台股 ETF 優惠證交稅 (0.1% 而非 0.3%)"""
        pos = {
            "symbol": "0050",
            "shares": 1000,
            "cost_price": 100.0,
            "fee_discount": 0.6,
            "is_etf": 1,
            "market": "TW"
        }
        quote = {
            "current_price": 100.0,
            "yesterday_close": 100.0
        }
        res = calculate_position_pnl(pos, quote)

        # 賣出市值 100,000
        # 賣出稅率應為 0.1% (100 元)，而非 0.3% (300 元)
        tax = res["tax"]
        self.assertEqual(tax, 100)
        net_sell = res["market_val"] - res["sell_fee"] - res["tax"]
        self.assertEqual(net_sell, 100000 - res["sell_fee"] - 100)

    def test_tw_odd_lot_fee(self):
        """測試零股交易 (小於 1000 股，無 20 元低消限制，採 ceil 向上取整)"""
        pos = {
            "symbol": "2330",
            "shares": 10,  # 零股 10 股
            "cost_price": 600.0,
            "fee_discount": 0.6,
            "is_etf": 0,
            "market": "TW"
        }
        quote = {
            "current_price": 600.0,
            "yesterday_close": 600.0
        }
        res = calculate_position_pnl(pos, quote)

        # 成本金額 6,000
        # 原始手續費 6000 * 0.001425 * 0.6 = 5.13 -> math.ceil = 6 元
        self.assertEqual(res["buy_fee"], 6)
        self.assertEqual(res["total_cost"], 6006)

    def test_us_stock_rules(self):
        """測試美股規則：無台股手續費低消與證交稅，純淨損益"""
        pos = {
            "symbol": "AAPL",
            "shares": 5,
            "cost_price": 150.0,
            "market": "US"
        }
        quote = {
            "current_price": 180.0,
            "yesterday_close": 175.0
        }
        res = calculate_position_pnl(pos, quote)
        self.assertEqual(res["total_cost"], 750.0)
        self.assertEqual(res["market_val"], 900.0)
        self.assertEqual(res["unrealized_pnl"], 150.0)
        self.assertAlmostEqual(res["roi_pct"], 20.0, places=2)


class TestCryptoSync(unittest.TestCase):
    """測試 E2EE 零知識加密同步之密碼學安全機制"""

    def test_fixed_block_padding(self):
        """測試 32KB 固定塊填充與反填充，確保抹平長度特徵"""
        sample_data = b"Hello Antigravity E2EE Test Data"
        padded = pad_to_fixed_block(sample_data, block_size=FIXED_BLOCK_SIZE)

        # 驗證總長度固定為 32768 bytes
        self.assertEqual(len(padded), FIXED_BLOCK_SIZE)

        # 驗證能 100% 精確還原
        restored = unpad_fixed_block(padded)
        self.assertEqual(restored, sample_data)

    def test_aad_string_format(self):
        """測試 AAD 認證字串之綁定格式"""
        aad = build_aad_string("peter", "1.0", "2026-10-12 12:00:00", 101)
        self.assertEqual(aad, "USER:peter|VER:1.0|TS:2026-10-12 12:00:00|SEQ:101")

    def test_encrypt_decrypt_roundtrip(self):
        """測試完整 E2EE 加解密閉環 (PBKDF2 600,000次 + AES-256-GCM)"""
        raw_payload = {
            "portfolio": [
                {"symbol": "2330", "shares": 1000, "cost": 500.0},
                {"symbol": "0050", "shares": 2000, "cost": 120.0}
            ],
            "total_value": 740000
        }
        password = "UserSuperStrongPassword123!"
        secret_key = "A8F2-99CD-31E0-74BA"
        user_id = "peter_investor"
        seq = 105

        # 1. 執行加密
        enc_result = encrypt_portfolio_payload(
            data=raw_payload.copy(),
            password=password,
            secret_key=secret_key,
            user_id=user_id,
            seq=seq
        )

        self.assertEqual(enc_result["version"], "1.0")
        self.assertEqual(enc_result["algorithm"], "AES-256-GCM")
        self.assertIn("600K", enc_result["kdf"])
        self.assertTrue(len(enc_result["ciphertext_b64"]) > 1000)

        # 2. 執行正確解密
        dec_payload = decrypt_portfolio_payload(
            encrypted_dict=enc_result,
            password=password,
            secret_key=secret_key,
            user_id=user_id
        )

        # 驗證持股資料完全吻合
        self.assertEqual(dec_payload["total_value"], 740000)
        self.assertEqual(len(dec_payload["portfolio"]), 2)
        self.assertEqual(dec_payload["_meta"]["sync_seq"], 105)

    def test_tamper_proofing(self):
        """測試防竄改：錯誤密碼或竄改 AAD 必須拋出例外拒絕解密"""
        raw_payload = {"test": 123}
        enc_result = encrypt_portfolio_payload(
            data=raw_payload,
            password="CorrectPassword",
            secret_key="A8F2-99CD-31E0-74BA",
            user_id="alice",
            seq=1
        )

        # 嘗試以錯誤密碼解密 -> 必須失敗
        with self.assertRaises(Exception):
            decrypt_portfolio_payload(
                encrypted_dict=enc_result,
                password="WrongPassword!",
                secret_key="A8F2-99CD-31E0-74BA",
                user_id="alice"
            )

        # 嘗試以偽造的 AAD 標頭解密 -> 必須被 AES-GCM 拒絕 (InvalidTag)
        tampered_enc = enc_result.copy()
        tampered_enc["aad_str"] = "USER:attacker|VER:1.0|TS:fake|SEQ:999"
        with self.assertRaises(Exception):
            decrypt_portfolio_payload(
                encrypted_dict=tampered_enc,
                password="CorrectPassword",
                secret_key="A8F2-99CD-31E0-74BA",
                user_id="alice"
            )


from unittest.mock import MagicMock, patch
from quote_service import QuoteService

class TestQuoteServiceMock(unittest.TestCase):
    """測試行情服務之批次解析、備援 Fallback 與 ETF 折溢價邏輯 (Mock 離線)"""

    def setUp(self):
        self.service = QuoteService()
        self.service._min_interval = 0.0 # 測試環境免等待

    @patch("requests.Session.get")
    def test_twse_batch_normal(self, mock_get):
        """測試 TWSE MIS 正常撮合成交價與開高低收解析"""
        mock_resp = MagicMock()
        mock_resp.status_code = 200
        mock_resp.json.return_value = {
            "msgArray": [
                {
                    "c": "2330",
                    "n": "台積電",
                    "z": "580.0",
                    "y": "570.0",
                    "o": "575.0",
                    "h": "585.0",
                    "l": "572.0",
                    "v": "25000",
                    "t": "13:30:00"
                }
            ]
        }
        mock_get.return_value = mock_resp

        quotes = self.service._fetch_twse_batch([("2330", "TW")])
        self.assertIn("2330", quotes)
        q = quotes["2330"]
        self.assertEqual(q["current_price"], 580.0)
        self.assertEqual(q["yesterday_close"], 570.0)
        self.assertEqual(q["open"], 575.0)
        self.assertEqual(q["high"], 585.0)
        self.assertEqual(q["low"], 572.0)
        self.assertEqual(q["volume"], 25000)
        self.assertEqual(q["change"], 10.0)
        self.assertAlmostEqual(q["change_pct"], 1.75, places=2)
        self.assertEqual(q["status"], "OK")

    @patch("requests.Session.get")
    def test_twse_pre_market_orderbook_fallback(self, mock_get):
        """測試盤前或未成交時 (z='-')，自動取最佳買價第一檔作為估計現價"""
        mock_resp = MagicMock()
        mock_resp.status_code = 200
        mock_resp.json.return_value = {
            "msgArray": [
                {
                    "c": "2330",
                    "n": "台積電",
                    "z": "-",
                    "y": "570.0",
                    "o": "-",
                    "h": "-",
                    "l": "-",
                    "b": "578.0_15_577.0_20",
                    "a": "579.0_10",
                    "v": "0"
                }
            ]
        }
        mock_get.return_value = mock_resp

        quotes = self.service._fetch_twse_batch([("2330", "TW")])
        q = quotes["2330"]
        # 現價應取自 b[0] = 578.0
        self.assertEqual(q["current_price"], 578.0)
        self.assertEqual(q["change"], 8.0)

    @patch("requests.Session.get")
    def test_twse_offline_yahoo_fallback(self, mock_get):
        """測試證交所離線時，自動觸發 Yahoo Finance v8 原生 REST 回退"""
        # 第一次請求 (TWSE) 失敗 500
        twse_resp = MagicMock()
        twse_resp.status_code = 500

        # 第二次請求 (Yahoo .TW) 成功 200
        yahoo_resp = MagicMock()
        yahoo_resp.status_code = 200
        yahoo_resp.json.return_value = {
            "chart": {
                "result": [
                    {
                        "meta": {
                            "regularMarketPrice": 585.0,
                            "chartPreviousClose": 575.0,
                            "regularMarketOpen": 576.0,
                            "regularMarketDayHigh": 588.0,
                            "regularMarketDayLow": 574.0,
                            "regularMarketVolume": 18000
                        }
                    }
                ]
            }
        }
        mock_get.side_effect = [twse_resp, yahoo_resp]

        quotes = self.service._fetch_twse_batch([("2330", "TW")])
        self.assertIn("2330", quotes)
        q = quotes["2330"]
        self.assertEqual(q["current_price"], 585.0)
        self.assertEqual(q["open"], 576.0) # 驗證修復後的 open 取值正確
        self.assertEqual(q["high"], 588.0)
        self.assertEqual(q["time"], "Yahoo")

    @patch("requests.Session.get")
    def test_us_market_batch(self, mock_get):
        """測試美股 Yahoo Finance 原生 v8 報價查詢"""
        mock_resp = MagicMock()
        mock_resp.status_code = 200
        mock_resp.json.return_value = {
            "chart": {
                "result": [
                    {
                        "meta": {
                            "regularMarketPrice": 185.25,
                            "chartPreviousClose": 180.0,
                            "regularMarketOpen": 182.0,
                            "regularMarketDayHigh": 186.0,
                            "regularMarketDayLow": 181.5,
                            "regularMarketVolume": 45000000
                        }
                    }
                ]
            }
        }
        mock_get.return_value = mock_resp

        quotes = self.service._fetch_us_batch(["AAPL"])
        self.assertIn("AAPL", quotes)
        q = quotes["AAPL"]
        self.assertEqual(q["current_price"], 185.25)
        self.assertEqual(q["open"], 182.0)
        self.assertEqual(q["change"], 5.25)
        self.assertEqual(q["time"], "US Market")

    @patch("requests.Session.get")
    def test_etf_nav_and_premium_discount(self, mock_get):
        """測試 ETF 即時淨值獲取與動態折溢價計算"""
        # Mock all_etf.txt
        etf_resp = MagicMock()
        etf_resp.status_code = 200
        etf_raw_json = json.dumps({
            "a1": [
                {
                    "msgArray": [
                        {
                            "a": "0050",
                            "e": "151.5", # 市價
                            "f": "150.0", # 淨值
                            "g": "1.00",  # 折溢價
                            "h": "149.8"  # 前日淨值
                        }
                    ]
                }
            ]
        })
        etf_resp.content = etf_raw_json.encode("cp950")
        mock_get.return_value = etf_resp

        etf_map = self.service.fetch_etf_nav_data()
        self.assertIn("0050", etf_map)
        self.assertEqual(etf_map["0050"]["nav"], 150.0)
        self.assertEqual(etf_map["0050"]["market_price"], 151.5)


class TestMultiUserSupport(unittest.TestCase):
    """測試多使用者帳號管理、密碼安全防護 (SHA-256+Salt) 與持股庫存物理隔離"""

    def setUp(self):
        import database
        import uuid
        self.db = database
        self.orig_db_file = self.db.DB_FILE
        self.test_db_file = os.path.join(current_dir, f"test_mu_{uuid.uuid4().hex[:8]}.db")
        self.db.DB_FILE = self.test_db_file
        self.db.init_db()

    def tearDown(self):
        self.db.DB_FILE = self.orig_db_file
        if os.path.exists(self.test_db_file):
            try:
                os.remove(self.test_db_file)
            except Exception:
                pass

    def test_default_user_creation(self):
        """驗證資料庫初始化自動建立預設主要帳號 (user_id=1)"""
        users = self.db.get_all_users()
        self.assertGreaterEqual(len(users), 1)
        u1 = users[0]
        self.assertEqual(u1["id"], 1)
        self.assertEqual(u1["username"], "default")
        self.assertFalse(u1["has_password"])

    def test_create_user_and_credential_verification(self):
        """驗證建立具密碼保護與免密碼帳戶，及 SHA-256+Salt 密碼驗證邏輯"""
        # 1. 建立有密碼帳號
        ok, msg, uid_alice = self.db.create_user("alice", "愛麗絲", "Secret123!")
        self.assertTrue(ok)
        self.assertIsNotNone(uid_alice)

        # 密碼驗證成功
        ok_verify, u_alice, _ = self.db.verify_user_credentials("alice", "Secret123!")
        self.assertTrue(ok_verify)
        self.assertIsNotNone(u_alice)
        self.assertEqual(u_alice["username"], "alice")

        # 錯誤密碼驗證失敗
        ok_wrong, _, err_msg = self.db.verify_user_credentials("alice", "WrongPassword")
        self.assertFalse(ok_wrong)
        self.assertIn("密碼錯誤", err_msg)

        # 2. 建立免密碼帳號
        ok_bob, _, uid_bob = self.db.create_user("bob", "小鮑伯", "")
        self.assertTrue(ok_bob)
        ok_bob_verify, u_bob, _ = self.db.verify_user_credentials("bob", "")
        self.assertTrue(ok_bob_verify)
        self.assertEqual(u_bob["display_name"], "小鮑伯")

    def test_multi_user_portfolio_isolation(self):
        """驗證不同使用者間之持股庫存與買入批次物理隔離"""
        ok, _, uid_user1 = self.db.create_user("trader_a", "交易員A", "")
        ok, _, uid_user2 = self.db.create_user("trader_b", "交易員B", "")

        # 交易員 A 新增台積電 1000 股
        self.db.set_current_user({"id": uid_user1, "username": "trader_a", "display_name": "交易員A"})
        self.db.add_position("2330", "台積電", "TW", 1000, 500.0, "A的長線部位")

        # 交易員 B 新增 0050 2000 股
        self.db.set_current_user({"id": uid_user2, "username": "trader_b", "display_name": "交易員B"})
        self.db.add_position("0050", "元大台灣50", "TW", 2000, 150.0, "B的定期定額")

        # 檢查交易員 A 的持股清單：僅有 2330，絕對沒有 0050
        pos_a = self.db.get_all_positions(user_id=uid_user1)
        symbols_a = [p["symbol"] for p in pos_a]
        self.assertIn("2330", symbols_a)
        self.assertNotIn("0050", symbols_a)

        # 檢查交易員 B 的持股清單：僅有 0050，絕對沒有 2330
        pos_b = self.db.get_all_positions(user_id=uid_user2)
        symbols_b = [p["symbol"] for p in pos_b]
        self.assertIn("0050", symbols_b)
        self.assertNotIn("2330", symbols_b)

    def test_user_deletion_cascade_and_guards(self):
        """驗證刪除使用者保護機制與級聯刪除"""
        # 1. 嘗試刪除 user_id=1 必須被阻擋
        ok, msg = self.db.delete_user(1)
        self.assertFalse(ok)
        self.assertIn("不可刪除", msg)

        # 2. 建立新帳號並添加持股，刪除該帳號時專屬庫存自動清除
        ok, _, test_uid = self.db.create_user("temp_user", "臨時測試", "")
        self.db.set_current_user({"id": test_uid, "username": "temp_user", "display_name": "臨時測試"})
        self.db.add_position("2454", "聯發科", "TW", 500, 1000.0)
        self.assertEqual(len(self.db.get_all_positions(test_uid)), 1)

        # 執行刪除
        del_ok, _ = self.db.delete_user(test_uid)
        self.assertTrue(del_ok)
        self.assertEqual(len(self.db.get_all_positions(test_uid)), 0)

    def test_update_username_success_and_unique_guard(self):
        """驗證更新帳號名稱 (Username) 之成功路徑與唯一性防呆"""
        ok1, _, uid1 = self.db.create_user("user_old", "舊名稱", "")
        ok2, _, uid2 = self.db.create_user("user_other", "其他用戶", "")
        self.assertTrue(ok1 and ok2)

        # 1. 成功修改帳號名稱
        ok_up, msg_up = self.db.update_username(uid1, "user_new")
        self.assertTrue(ok_up)
        u1 = self.db.get_user_by_id(uid1)
        self.assertEqual(u1["username"], "user_new")

        # 2. 嘗試修改為空帳號名稱應被阻擋
        ok_empty, _ = self.db.update_username(uid1, "   ")
        self.assertFalse(ok_empty)

        # 3. 嘗試修改為已存在之帳號名稱應被阻擋
        ok_dup, msg_dup = self.db.update_username(uid1, "user_other")
        self.assertFalse(ok_dup)
        self.assertIn("已被其他使用者使用", msg_dup)


class TestUILayoutIntegrity(unittest.TestCase):
    """驗證 GUI 視窗排版防裁切健康檢查 (自動防文字截斷硬閘門)"""

    def test_all_dialogs_anti_clipping(self):
        """驗證所有核心對話框皆具備充足幾何尺寸，杜絕任何文字或按鈕被邊界裁切"""
        from ui_layout_inspector import run_full_ui_layout_inspection
        self.assertTrue(run_full_ui_layout_inspection(), "發現 GUI 對話框排版裁切瑕疵！")


if __name__ == "__main__":
    unittest.main(verbosity=2)

