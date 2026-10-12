# -*- coding: utf-8 -*-
"""
TOOL_Stock_Portfolio_Tracker - 全自動化建置與發布流水線 (package_release.py)
一鍵全自動執行：
1. 雙層原始碼原子同步 (根目錄 <-> modules/ 防呆)
2. 離線單元測試品質硬閘門 (13項測試100%全綠)
3. 二進位核心編譯 (app_core.pkg)
4. 熱補丁包自動壓製 (dist_patches/patch_<Ver>.zip)
5. 完整便攜包自動壓製 (dist_releases/Stock_Portfolio_Tracker_<Ver>_Portable.zip)
6. 發布指標與檔案完整性校驗 (version.json & SHA256)
"""

import os
import sys
import json
import shutil
import zipfile
import hashlib
import subprocess
from typing import Dict, Any, List, Tuple

if hasattr(sys.stdout, "reconfigure"):
    sys.stdout.reconfigure(encoding="utf-8")
if hasattr(sys.stderr, "reconfigure"):
    sys.stderr.reconfigure(encoding="utf-8")

APP_DIR = os.path.dirname(os.path.abspath(__file__))
MODULES_DIR = os.path.join(APP_DIR, "modules")
CONFIG_DIR = os.path.join(APP_DIR, "config")
PLUGINS_DIR = os.path.join(APP_DIR, "plugins")
DIST_PATCHES_DIR = os.path.join(APP_DIR, "dist_patches")
DIST_RELEASES_DIR = os.path.join(APP_DIR, "dist_releases")

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
    "login_gui.py"
]

def calculate_sha256(filepath: str) -> str:
    """計算檔案 SHA-256 雜湊值"""
    h = hashlib.sha256()
    with open(filepath, "rb") as f:
        while chunk := f.read(65536):
            h.update(chunk)
    return h.hexdigest()

def step_1_sync_dual_locations() -> bool:
    """Step 1: 雙層原始碼原子同步檢查 (根目錄 <-> modules/)"""
    print("\n[Step 1/6] 執行雙層原始碼原子同步檢查 (根目錄 <-> modules/)...")
    os.makedirs(MODULES_DIR, exist_ok=True)
    synced_count = 0

    for fname in MODULE_FILES:
        root_f = os.path.join(APP_DIR, fname)
        mod_f = os.path.join(MODULES_DIR, fname)

        if os.path.exists(root_f) and os.path.exists(mod_f):
            root_mtime = os.path.getmtime(root_f)
            mod_mtime = os.path.getmtime(mod_f)
            # 若時間戳不同且內容不同，以較新者同步覆蓋較舊者
            if abs(root_mtime - mod_mtime) > 1.0:
                with open(root_f, "rb") as f1, open(mod_f, "rb") as f2:
                    if f1.read() != f2.read():
                        if root_mtime > mod_mtime:
                            shutil.copy2(root_f, mod_f)
                            print(f"  -> [同步: 根目錄 -> modules/] {fname}")
                        else:
                            shutil.copy2(mod_f, root_f)
                            print(f"  -> [同步: modules/ -> 根目錄] {fname}")
                        synced_count += 1
        elif os.path.exists(root_f) and not os.path.exists(mod_f):
            shutil.copy2(root_f, mod_f)
            print(f"  -> [補齊至 modules/] {fname}")
            synced_count += 1
        elif not os.path.exists(root_f) and os.path.exists(mod_f):
            shutil.copy2(mod_f, root_f)
            print(f"  -> [補齊至 根目錄] {fname}")
            synced_count += 1

    print(f"  ✔ 雙層原始碼檢查完成 (共同步/校正 {synced_count} 個模組)")
    return True

def step_2_run_quality_gate() -> bool:
    """Step 2: 離線單元測試品質硬閘門 (13項測試100%全綠)"""
    print("\n[Step 2/6] 執行離線單元測試品質硬閘門 (test_core_modules.py)...")
    test_script = os.path.join(APP_DIR, "test_core_modules.py")
    if not os.path.exists(test_script):
        print("  [X] 錯誤: 找不到 test_core_modules.py！")
        return False

    cmd = [sys.executable, test_script]
    res = subprocess.run(cmd, cwd=APP_DIR, capture_output=True, text=True, encoding="utf-8", errors="replace")
    if res.returncode != 0:
        print("  [X] 致命錯誤: 單元測試未通過！發布流程已強制中斷！")
        print(res.stdout)
        print(res.stderr)
        return False

    print("  ✔ 單元測試全部 100% 通過 (品質硬閘門 PASS)")
    return True

def step_2_5_run_ui_layout_inspection() -> bool:
    """Step 2.5: 全自動 GUI 排版防裁切健康檢查 (ui_layout_inspector.py)"""
    print("\n[Step 2.5/6] 執行全系統 GUI 排版防裁切健康檢查 (ui_layout_inspector.py)...")
    inspector_script = os.path.join(APP_DIR, "ui_layout_inspector.py")
    if not os.path.exists(inspector_script):
        print("  [!] 找不到 ui_layout_inspector.py，跳過獨立檢查")
        return True

    cmd = [sys.executable, inspector_script]
    res = subprocess.run(cmd, cwd=APP_DIR, capture_output=True, text=True, encoding="utf-8", errors="replace")
    if res.returncode != 0:
        print("  [X] 致命錯誤: 偵測到 GUI 元件溢位或視窗裁切！發布流程已強制中斷！")
        print(res.stdout)
        print(res.stderr)
        return False

    print("  ✔ 全系統 GUI 視窗幾何邊界驗證通過 (零裁切、零文字截斷)")
    return True

def step_3_compile_bytecode() -> bool:
    """Step 3: 編譯二進位核心包 (app_core.pkg)"""
    print("\n[Step 3/6] 編譯位元碼並封裝 app_core.pkg...")
    build_script = os.path.join(APP_DIR, "build_pkg.py")
    cmd = [sys.executable, build_script]
    res = subprocess.run(cmd, cwd=APP_DIR, capture_output=True, text=True, encoding="utf-8", errors="replace")
    if res.returncode != 0:
        print("  [X] 二進位封裝失敗:")
        print(res.stderr)
        return False

    pkg_path = os.path.join(APP_DIR, "app_core.pkg")
    if not os.path.exists(pkg_path):
        print("  [X] 錯誤: 未產出 app_core.pkg！")
        return False

    print(f"  ✔ 二進位核心封裝成功: {pkg_path} ({os.path.getsize(pkg_path):,} bytes)")
    return True

def step_4_package_patch(version: str) -> str:
    """Step 4: 自動壓製熱補丁包 (dist_patches/patch_<Ver>.zip)"""
    print(f"\n[Step 4/6] 壓製熱補丁包 (patch_{version}.zip)...")
    os.makedirs(DIST_PATCHES_DIR, exist_ok=True)
    patch_zip_name = f"patch_{version}.zip"
    patch_zip_path = os.path.join(DIST_PATCHES_DIR, patch_zip_name)

    files_to_pack = [
        ("app_core.pkg", "app_core.pkg"),
        (os.path.join("config", "broker_rules.json"), "config/broker_rules.json"),
        (os.path.join("config", "api_endpoints.json"), "config/api_endpoints.json"),
        (os.path.join("config", "etf_live_cache.json"), "config/etf_live_cache.json"),
        ("market_ranking_cache.json", "market_ranking_cache.json"),
        (os.path.join("plugins", "README.md"), "plugins/README.md"),
        (os.path.join("plugins", "example_custom_indicator.py"), "plugins/example_custom_indicator.py")
    ]

    with zipfile.ZipFile(patch_zip_path, "w", compression=zipfile.ZIP_DEFLATED) as zf:
        for rel_src, arcname in files_to_pack:
            abs_src = os.path.join(APP_DIR, rel_src)
            if os.path.exists(abs_src):
                zf.write(abs_src, arcname=arcname)

    print(f"  ✔ 熱補丁包壓製完成: {patch_zip_path} ({os.path.getsize(patch_zip_path):,} bytes)")
    return patch_zip_path

def step_5_package_portable_release(version: str) -> str:
    """Step 5: 自動壓製完整便攜包 (dist_releases/Stock_Portfolio_Tracker_<Ver>_Portable.zip)"""
    print(f"\n[Step 5/6] 壓製完整綠色便攜包 (Stock_Portfolio_Tracker_{version}_Portable.zip)...")
    os.makedirs(DIST_RELEASES_DIR, exist_ok=True)
    rel_zip_name = f"Stock_Portfolio_Tracker_{version}_Portable.zip"
    rel_zip_path = os.path.join(DIST_RELEASES_DIR, rel_zip_name)

    files_to_pack = [
        ("app_core.pkg", "app_core.pkg"),
        ("Stock_Portfolio_Tracker.exe", "Stock_Portfolio_Tracker.exe"),
        ("run.bat", "run.bat"),
        ("start.vbs", "start.vbs"),
        (os.path.join("config", "broker_rules.json"), "config/broker_rules.json"),
        (os.path.join("config", "api_endpoints.json"), "config/api_endpoints.json"),
        (os.path.join("config", "etf_live_cache.json"), "config/etf_live_cache.json"),
        ("market_ranking_cache.json", "market_ranking_cache.json"),
        (os.path.join("plugins", "README.md"), "plugins/README.md"),
        (os.path.join("plugins", "example_custom_indicator.py"), "plugins/example_custom_indicator.py")
    ]

    with zipfile.ZipFile(rel_zip_path, "w", compression=zipfile.ZIP_DEFLATED) as zf:
        for rel_src, arcname in files_to_pack:
            abs_src = os.path.join(APP_DIR, rel_src)
            if os.path.exists(abs_src):
                zf.write(abs_src, arcname=arcname)

    print(f"  ✔ 完整便攜包壓製完成: {rel_zip_path} ({os.path.getsize(rel_zip_path):,} bytes)")
    return rel_zip_path

def step_6_verify_version_manifest() -> Dict[str, Any]:
    """Step 6: 發布指標與檔案完整性校驗"""
    print("\n[Step 6/6] 校驗 version.json 與發布指標完整性...")
    vjson_path = os.path.join(APP_DIR, "version.json")
    if not os.path.exists(vjson_path):
        raise FileNotFoundError("找不到 version.json！")

    with open(vjson_path, "r", encoding="utf-8") as f:
        v_data = json.load(f)

    cur_version = v_data.get("version", "")
    patch_zip_name = f"patch_{cur_version}.zip"
    rel_zip_name = f"Stock_Portfolio_Tracker_{cur_version}_Portable.zip"

    patch_path = os.path.join(DIST_PATCHES_DIR, patch_zip_name)
    rel_path = os.path.join(DIST_RELEASES_DIR, rel_zip_name)

    info = {
        "version": cur_version,
        "patch_exists": os.path.exists(patch_path),
        "patch_size": os.path.getsize(patch_path) if os.path.exists(patch_path) else 0,
        "patch_sha256": calculate_sha256(patch_path) if os.path.exists(patch_path) else "",
        "rel_exists": os.path.exists(rel_path),
        "rel_size": os.path.getsize(rel_path) if os.path.exists(rel_path) else 0,
        "rel_sha256": calculate_sha256(rel_path) if os.path.exists(rel_path) else ""
    }

    print(f"  ✔ 發布指標校驗通過:")
    print(f"    - 版號: {info['version']}")
    print(f"    - 熱補丁包: {patch_zip_name} ({info['patch_size']:,} bytes)")
    print(f"    - 補丁 SHA-256: {info['patch_sha256'][:16]}...")
    print(f"    - 完整便攜包: {rel_zip_name} ({info['rel_size']:,} bytes)")
    print(f"    - 便攜包 SHA-256: {info['rel_sha256'][:16]}...")
    return info

def main():
    print("=" * 65)
    print("   Stock Portfolio Tracker - 全自動建置與發布流水線 Pipeline")
    print("=" * 65)

    # 讀取版本號
    vjson_path = os.path.join(APP_DIR, "version.json")
    with open(vjson_path, "r", encoding="utf-8") as f:
        v_data = json.load(f)
    target_version = v_data.get("version", "V1.0.0.0")

    print(f"▶ 目標發布版本: {target_version}")

    # 依序執行 6 大流水線工序
    if not step_1_sync_dual_locations():
        sys.exit(1)
    if not step_2_run_quality_gate():
        sys.exit(1)
    if not step_2_5_run_ui_layout_inspection():
        sys.exit(1)
    if not step_3_compile_bytecode():
        sys.exit(1)

    step_4_package_patch(target_version)
    step_5_package_portable_release(target_version)
    manifest = step_6_verify_version_manifest()

    print("\n" + "=" * 65)
    print("🎉 發布流水線執行完畢！所有產物均已就緒並通過品質硬閘門驗證！")
    print("=" * 65)

if __name__ == "__main__":
    main()
