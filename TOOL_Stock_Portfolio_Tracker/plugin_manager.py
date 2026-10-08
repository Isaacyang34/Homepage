# -*- coding: utf-8 -*-
"""
Stock Portfolio Tracker - 外掛擴充模組管理器 (plugin_manager.py)
支援在使用者目錄下的 plugins/ 資料夾動態置放自訂 Python 擴充腳本 (*.py)。
主程式啟動時自動掃描、載入並註冊外掛，達到免動主包、隨插即用的模組化擴展能力。
"""
import os
import sys
import importlib.util
from typing import Dict, Any, List

def get_app_dir() -> str:
    if getattr(sys, 'frozen', False):
        return os.path.dirname(os.path.abspath(sys.executable))
    return os.path.dirname(os.path.abspath(__file__))

# 全域外掛註冊表
LOADED_PLUGINS: Dict[str, Any] = {}

def load_plugins() -> List[str]:
    """掃描並載入 plugins/ 目錄下的所有自訂擴充腳本"""
    app_dir = get_app_dir()
    plugins_dir = os.path.join(app_dir, "plugins")
    if not os.path.exists(plugins_dir):
        try:
            os.makedirs(plugins_dir, exist_ok=True)
        except Exception:
            return []

    loaded_names = []
    for fname in os.listdir(plugins_dir):
        if fname.endswith(".py") and not fname.startswith("__"):
            mod_name = fname[:-3]
            fpath = os.path.join(plugins_dir, fname)
            try:
                spec = importlib.util.spec_from_file_location(f"plugin_{mod_name}", fpath)
                if spec and spec.loader:
                    mod = importlib.util.module_from_spec(spec)
                    spec.loader.exec_module(mod)
                    LOADED_PLUGINS[mod_name] = mod
                    loaded_names.append(mod_name)
                    # 若插件具有 register 函數，自動調用
                    if hasattr(mod, "register"):
                        mod.register()
                    print(f"[PluginManager] 成功載入自訂外掛: {mod_name}")
            except Exception as e:
                print(f"[PluginManager] 載入外掛 {fname} 失敗: {e}")

    return loaded_names

def get_plugin(name: str) -> Any:
    """取得指定已載入之外掛實例"""
    return LOADED_PLUGINS.get(name)
