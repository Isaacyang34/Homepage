# -*- coding: utf-8 -*-
import sys
import os

# 確保主控台 UTF-8 編碼避免 Windows 亂碼
if hasattr(sys.stdout, "reconfigure"):
    sys.stdout.reconfigure(encoding="utf-8")
if hasattr(sys.stderr, "reconfigure"):
    sys.stderr.reconfigure(encoding="utf-8")

# 1. 程式最早期啟動日誌紀錄與未捕捉例外攔截器 (保證任何崩潰 100% 寫入 update_debug.log)
app_dir = os.path.dirname(os.path.abspath(sys.executable if getattr(sys, 'frozen', False) else __file__))

def _log_boot_early(msg: str):
    try:
        from datetime import datetime
        ts = datetime.now().strftime("%Y-%m-%d %H:%M:%S")
        log_f = os.path.join(app_dir, "update_debug.log")
        with open(log_f, "a", encoding="utf-8") as f:
            f.write(f"[{ts}] {msg}\n")
    except Exception:
        pass

def _global_exception_handler(exc_type, exc_value, exc_traceback):
    if issubclass(exc_type, KeyboardInterrupt):
        sys.__excepthook__(exc_type, exc_value, exc_traceback)
        return
    import traceback
    tb_str = "".join(traceback.format_exception(exc_type, exc_value, exc_traceback))
    _log_boot_early(f"[UNHANDLED_EXCEPTION] Crash detected:\n{tb_str}")
    sys.__excepthook__(exc_type, exc_value, exc_traceback)

sys.excepthook = _global_exception_handler

_mei_early = getattr(sys, '_MEIPASS', 'None')
_log_boot_early(f"[EARLY_BOOT] Python interpreter started. PID={os.getpid()}, exe={sys.executable}, _MEIPASS={_mei_early}, _MEIPASS2={os.environ.get('_MEIPASS2')}")

# 2. 動態配置二進位封裝包路徑：優先載入非明碼核心包 app_core.pkg
pkg_path = os.path.join(app_dir, "app_core.pkg")
if os.path.exists(pkg_path):
    sys.path.insert(0, pkg_path)

modules_dir = os.path.join(app_dir, "modules")
if os.path.exists(modules_dir) and modules_dir not in sys.path:
    sys.path.insert(1, modules_dir)
if app_dir not in sys.path:
    sys.path.insert(2, app_dir)

# 3. 開機自我健康檢查與自癒機制 (Health & Integrity Check)
try:
    from integrity_checker import check_and_heal_system_integrity
    if not check_and_heal_system_integrity():
        _log_boot_early(f"[INTEGRITY] check_and_heal_system_integrity returned False, exiting.")
        sys.exit(1)
except Exception as e:
    _log_boot_early(f"[INTEGRITY_ERROR] Failed during integrity check: {e}")
    print(f"[Warning] 完整性檢查模組載入失敗: {e}")

# 4. 記錄核心模組就緒日誌
try:
    from updater import log_update_debug, APP_VERSION
    log_update_debug(f"[BOOT] Stock_Portfolio_Tracker booted cleanly! PID={os.getpid()}, Version={APP_VERSION}, _MEIPASS={_mei_early}")
except Exception as e:
    _log_boot_early(f"[BOOT_ERROR] Failed to load updater/APP_VERSION: {e}")

# 4. 動態掃描並載入 plugins/ 目錄之自訂外掛擴充腳本
try:
    from plugin_manager import load_plugins
    load_plugins()
except Exception as e:
    pass

def main():
    try:
        from splash_screen import show_splash_and_start_app
        show_splash_and_start_app()
    except Exception as e:
        # 防呆降級：若啟動畫面異常直接開啟主視窗
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
