# -*- coding: utf-8 -*-
"""
Stock Portfolio Tracker - 二進位核心封裝建置腳本 (build_pkg.py)
負責將所有 Python 業務模組編譯為位元碼 (.pyc)，並封裝成單一非明碼之二進位包 (app_core.pkg)
"""
import os
import sys
import shutil
import zipfile
import compileall

if sys.stdout is not None and hasattr(sys.stdout, "reconfigure"):
    sys.stdout.reconfigure(encoding="utf-8")
if sys.stderr is not None and hasattr(sys.stderr, "reconfigure"):
    sys.stderr.reconfigure(encoding="utf-8")

MODULE_FILES = [
    "main_gui.py",
    "updater.py",
    "database.py",
    "quote_service.py",
    "history_service.py",
    "dividend_service.py",
    "pnl_calculator.py",
    "ranking_service.py",
    "market_ranking_gui.py",
    "dividend_analyzer_gui.py",
    "kline_chart.py",
    "splash_screen.py",
    "stock_detector.py",
    "crypto_sync.py",
    "integrity_checker.py",
    "plugin_manager.py",
    "login_gui.py",
    "error_reporter.py",
    "portfolio_etf_service.py",
    "portfolio_etf_gui.py",
    "margin_risk_service.py",
    "unified_ranking_gui.py",
    "stock_fundamental_service.py",
    "fundamental_analyzer_gui.py"
]

def check_windowed_stdio_safety(app_dir: str) -> bool:
    """編譯前安全守門員：執行無主控台 (Windowed/Null-Stdio) 語法檢驗，嚴禁任何未防護裸調用混入封裝包"""
    print("[0/3] 執行無主控台 (Windowed/Null-Stdio) 語法安全防護檢驗...")
    import unittest
    tests_dir = os.path.join(app_dir, "tests")
    if not os.path.exists(tests_dir):
        return True
    suite = unittest.defaultTestLoader.discover(tests_dir, pattern="test_windowed_stdio_safety.py")
    runner = unittest.TextTestRunner(verbosity=1)
    result = runner.run(suite)
    if not result.wasSuccessful():
        print("\n[!] 致命錯誤: 模組中存在未防護之 stdio 裸調用，嚴格拒絕編譯與打包！")
        return False
    print("[0/3] 無主控台語法安全檢測 PASS (0 裸調用，0 NoneType 異常)\n")
    return True

def build_app_core_pkg(output_pkg_path: str = "app_core.pkg") -> bool:
    app_dir = os.path.dirname(os.path.abspath(__file__))
    if not check_windowed_stdio_safety(app_dir):
        return False

    stage_dir = os.path.join(app_dir, "temp_pkg_stage")
    
    if os.path.exists(stage_dir):
        shutil.rmtree(stage_dir, ignore_errors=True)
    os.makedirs(stage_dir, exist_ok=True)
    
    print("[1/3] 收集原始碼檔案至臨時編譯區...")
    for mod_name in MODULE_FILES:
        src = os.path.join(app_dir, "modules", mod_name)
        if not os.path.exists(src):
            src = os.path.join(app_dir, mod_name)
        if os.path.exists(src):
            shutil.copy2(src, os.path.join(stage_dir, mod_name))
        else:
            print(f"[!] 警告: 找不到模組 {mod_name}")
            
    print("[2/3] 執行二進位位元碼 (Bytecode) 編譯...")
    compileall.compile_dir(stage_dir, force=True, legacy=True, quiet=1)
    
    print("[3/3] 打包產出非明碼二進位核心包 app_core.pkg...")
    pkg_full_path = os.path.join(app_dir, output_pkg_path)
    if os.path.exists(pkg_full_path):
        os.remove(pkg_full_path)
        
    with zipfile.ZipFile(pkg_full_path, "w", compression=zipfile.ZIP_DEFLATED) as zf:
        for fname in os.listdir(stage_dir):
            if fname.endswith(".pyc"):
                full_p = os.path.join(stage_dir, fname)
                zf.write(full_p, arcname=fname)
                
    # 清理臨時編譯目錄
    shutil.rmtree(stage_dir, ignore_errors=True)
    
    pkg_size = os.path.getsize(pkg_full_path)
    print(f"✔ 建置完成！已產出二進位核心包: {pkg_full_path} (大小: {pkg_size:,} bytes)")
    return True

if __name__ == "__main__":
    build_app_core_pkg()
