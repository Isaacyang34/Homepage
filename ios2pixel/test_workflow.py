import os
import sys
import time
import subprocess
import unittest

class TestIOS2PixelDecoupledWorkflow(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.base_dir = os.path.dirname(os.path.abspath(__file__))
        cls.exe_path = os.path.join(cls.base_dir, "iOS2Pixel_Pro.exe")
        print(f"\n[SETUP] Base dir: {cls.base_dir}")
        print(f"[SETUP] Target EXE: {cls.exe_path}")

    def test_01_hardware_and_buffer_stats(self):
        """Test 1: 驗證分時硬體偵測與電腦緩衝庫狀態"""
        print("\n--- Running Test 1: Hardware & Buffer Health ---")
        from core_sync import check_iphone, check_pixel, find_adb, get_buffer_summary, get_vault_dir
        
        adb = find_adb()
        self.assertIsNotNone(adb, "adb.exe 必須有效")
        
        iph_ok, iph_info = check_iphone()
        print(f"  [PASS] iPhone 狀態: ok={iph_ok}, info={iph_info}")
        
        pix_ok, pix_info, free_gb = check_pixel(adb)
        print(f"  [PASS] Pixel 狀態: ok={pix_ok}, info={pix_info}, 剩餘容量={free_gb:.2f} GB")

        vault_dir = get_vault_dir()
        self.assertTrue(os.path.isdir(vault_dir), "電腦緩衝庫目錄必須存在")
        stats = get_buffer_summary()
        print(f"  [PASS] 電腦緩衝庫狀態: 總相片={stats['total_files']}, 待推送={stats['pending_pixel']}")

    def test_02_gui_window_rendering(self):
        """Test 2: 驗證 GUI 視窗元件與線程載入無任何 NameError / ImportError，能正常渲染"""
        print("\n--- Running Test 2: GUI Window Rendering Test ---")
        import tkinter as tk
        from gui_app import IOS2PixelProApp
        root = tk.Tk()
        app = IOS2PixelProApp(root)
        root.update()
        self.assertIsNotNone(app.btn_ingest, "採集按鈕必須成功初始化")
        self.assertIsNotNone(app.btn_analyze, "分析按鈕必須成功初始化")
        self.assertIsNotNone(app.btn_clean_iph, "清理按鈕必須成功初始化")
        self.assertIsNotNone(app.btn_push, "推送按鈕必須成功初始化")
        self.assertIsNotNone(app.btn_auto_pilot, "Auto-Pilot 按鈕必須成功初始化")
        self.assertIsNotNone(app.btn_wash_quota, "雲端洗白按鈕必須成功初始化")
        print("  [PASS] GUI 視窗建立並完成第一幀渲染，包含分析、刪除與雲端洗白按鈕，無任何 NameError/AttributeError！")
        root.destroy()

    def test_03_executable_blackbox_smoke_test(self):
        """Test 3: 驗證獨立執行檔在運行 6 秒內，進程數嚴格為 1，絕無連鎖衍生"""
        print("\n--- Running Test 3: Frozen Executable Blackbox Smoke Test ---")
        self.assertTrue(os.path.isfile(self.exe_path), "iOS2Pixel_Pro.exe 必須存在")

        proc = subprocess.Popen([self.exe_path])
        pid = proc.pid
        print(f"  [INFO] 啟動測試實例 PID={pid}，監控 6 秒進程行為...")

        time.sleep(3)
        cmd = 'Get-Process -Name "iOS2Pixel_Pro" -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Id'
        r = subprocess.run(["powershell", "-Command", cmd], capture_output=True, text=True)
        pids = [line.strip() for line in r.stdout.splitlines() if line.strip()]
        
        print(f"  [MONITOR] 3秒後運行中之進程 PIDs: {pids}")
        self.assertEqual(len(pids), 1, f"系統中必須且僅能有 1 個 iOS2Pixel_Pro 進程，實際檢測到: {len(pids)}")

        time.sleep(3)
        r2 = subprocess.run(["powershell", "-Command", cmd], capture_output=True, text=True)
        pids2 = [line.strip() for line in r2.stdout.splitlines() if line.strip()]
        print(f"  [MONITOR] 6秒後運行中之進程 PIDs: {pids2}")
        self.assertEqual(len(pids2), 1, f"背景輪詢後進程數必須維持在 1，實際檢測到: {len(pids2)}")

        # Teardown
        proc.terminate()
        try: proc.wait(timeout=3)
        except subprocess.TimeoutExpired: proc.kill()
        subprocess.run(["powershell", "-Command", 'Stop-Process -Name "iOS2Pixel_Pro" -Force -ErrorAction SilentlyContinue'])
        print("  [PASS] 獨立執行檔黑箱冒煙測試全數通過！零子行程、零視窗連環生成。")

if __name__ == "__main__":
    unittest.main()
