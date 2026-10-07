import sys
import os

# 確保主控台 UTF-8 編碼避免 Windows 亂碼
if hasattr(sys.stdout, "reconfigure"):
    sys.stdout.reconfigure(encoding="utf-8")
if hasattr(sys.stderr, "reconfigure"):
    sys.stderr.reconfigure(encoding="utf-8")

# 記錄啟動日誌 (證明全新程序啟動成功，便於追蹤更新鏈條)
try:
    from updater import log_update_debug, APP_VERSION
    _mei = getattr(sys, '_MEIPASS', 'None')
    log_update_debug(f"[BOOT] Stock_Portfolio_Tracker booted cleanly! PID={os.getpid()}, Version={APP_VERSION}, _MEIPASS={_mei}")
except Exception as e:
    try:
        with open("boot_error.txt", "w", encoding="utf-8") as f:
            f.write(f"Boot error: {e}\n")
    except Exception:
        pass

def main():
    try:
        from splash_screen import show_splash_and_start_app
        show_splash_and_start_app()
    except Exception as e:
        # 防呆降級：若環境異常直接開啟主視窗
        print(f"[Warning] 啟動動畫異常，直接開啟主程式: {e}")
        try:
            from main_gui import PortfolioApp
            app = PortfolioApp()
            app.mainloop()
        except ImportError as ie:
            print(f"[ERROR] 缺少必要的套件: {ie}")
            print("請執行: pip install -r requirements.txt")
            input("按 Enter 鍵結束...")
            sys.exit(1)

if __name__ == "__main__":
    main()
