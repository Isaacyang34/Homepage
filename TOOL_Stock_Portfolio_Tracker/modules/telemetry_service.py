# -*- coding: utf-8 -*-
"""
Stock Portfolio Tracker - 雲端遙測統計與營運數據服務 (telemetry_service.py)
雙軌支援：
1. Firebase Realtime Database 輕量匿名計數器 (總啟動次數、總更新次數、版本分佈、每日活躍 DAU)
2. GitHub Releases 官方下載量統計 API (各發布檔案之 download_count)
完全匿名、零個資、背景非同步發送，超時自動跳過，絕不影響主程式運作。
"""
import os
import sys
import json
import time
import threading
import requests
from typing import Dict, Any, Optional, Tuple, List

DEFAULT_FIREBASE_URL = "https://my-stock-tracker-2a94e-default-rtdb.asia-southeast1.firebasedatabase.app"
GITHUB_RELEASES_URL = "https://api.github.com/repos/Isaacyang34/Homepage/releases"

def _safe_bg_run(target_func, *args):
    """在背景 daemon 執行緒中安全執行任務"""
    try:
        t = threading.Thread(target=target_func, args=args, daemon=True)
        t.start()
    except Exception:
        pass

def record_app_launch(version: str):
    """紀錄一次軟體啟動 (背景非同步)"""
    _safe_bg_run(_do_record_launch, version)

def record_update_applied(version: str):
    """紀錄一次更新成功套用 (背景非同步)"""
    _safe_bg_run(_do_record_update, version)

def _get_today_str() -> str:
    return time.strftime("%Y-%m-%d")

def _clean_ver_key(ver: str) -> str:
    return ver.strip().replace(".", "_")

def _do_record_launch(version: str):
    """發送開機遙測數據至 Firebase"""
    try:
        now_str = time.strftime("%Y-%m-%d %H:%M:%S")
        today = _get_today_str()
        ver_key = _clean_ver_key(version)
        url_summary = f"{DEFAULT_FIREBASE_URL}/telemetry/summary.json"
        url_daily = f"{DEFAULT_FIREBASE_URL}/telemetry/daily/{today}.json"

        # 1. 讀取現有 summary
        resp = requests.get(url_summary, timeout=2.0)
        if resp.status_code == 200 and resp.json():
            summary = resp.json()
        else:
            summary = {}

        curr_launches = int(summary.get("total_launches", 0)) + 1
        curr_ver_count = int(summary.get("versions", {}).get(ver_key, 0)) + 1

        patch_summary = {
            "total_launches": curr_launches,
            f"versions/{ver_key}": curr_ver_count,
            "last_active": now_str
        }
        requests.patch(url_summary, json=patch_summary, timeout=2.0)

        # 2. 累加當日活躍
        resp_d = requests.get(url_daily, timeout=2.0)
        daily_launches = 1
        if resp_d.status_code == 200 and resp_d.json():
            daily_launches = int(resp_d.json().get("launches", 0)) + 1
        requests.patch(url_daily, json={"launches": daily_launches}, timeout=2.0)
    except Exception:
        # 遙測通訊失敗靜默處理，絕不影響使用者體驗
        pass

def _do_record_update(version: str):
    """發送更新套用遙測數據至 Firebase"""
    try:
        today = _get_today_str()
        url_summary = f"{DEFAULT_FIREBASE_URL}/telemetry/summary.json"
        url_daily = f"{DEFAULT_FIREBASE_URL}/telemetry/daily/{today}.json"

        # 1. 累加總更新數
        resp = requests.get(url_summary, timeout=2.0)
        curr_updates = 1
        if resp.status_code == 200 and resp.json():
            curr_updates = int(resp.json().get("total_updates", 0)) + 1
        requests.patch(url_summary, json={"total_updates": curr_updates}, timeout=2.0)

        # 2. 累加當日更新數
        resp_d = requests.get(url_daily, timeout=2.0)
        daily_updates = 1
        if resp_d.status_code == 200 and resp_d.json():
            daily_updates = int(resp_d.json().get("updates", 0)) + 1
        requests.patch(url_daily, json={"updates": daily_updates}, timeout=2.0)
    except Exception:
        pass

def fetch_telemetry_report() -> Dict[str, Any]:
    """
    自 Firebase 與 GitHub 讀取完整營運與遙測報表
    回傳統合字典供 GUI 與網頁呈現
    """
    report: Dict[str, Any] = {
        "firebase_connected": False,
        "firebase_error": "",
        "total_launches": 0,
        "total_updates": 0,
        "today_launches": 0,
        "today_updates": 0,
        "last_active": "無資料",
        "versions_distribution": {},
        "daily_trends": [],
        "github_releases": []
    }

    # 1. 讀取 Firebase RTDB 遙測
    try:
        url = f"{DEFAULT_FIREBASE_URL}/telemetry.json"
        resp = requests.get(url, timeout=3.0)
        if resp.status_code == 200:
            data = resp.json() or {}
            report["firebase_connected"] = True
            summary = data.get("summary", {})
            report["total_launches"] = int(summary.get("total_launches", 0))
            report["total_updates"] = int(summary.get("total_updates", 0))
            report["last_active"] = summary.get("last_active", "尚未紀錄")

            # 版本佔比
            ver_dict = summary.get("versions", {})
            clean_versions = {}
            for k, v in ver_dict.items():
                clean_name = k.replace("_", ".")
                clean_versions[clean_name] = int(v)
            report["versions_distribution"] = clean_versions

            # 當日與近期趨勢
            daily_dict = data.get("daily", {})
            today_str = _get_today_str()
            if today_str in daily_dict:
                report["today_launches"] = int(daily_dict[today_str].get("launches", 0))
                report["today_updates"] = int(daily_dict[today_str].get("updates", 0))

            # 最近 7 日趨勢
            sorted_days = sorted(daily_dict.keys(), reverse=True)[:7]
            for day in sorted_days:
                item = daily_dict[day]
                report["daily_trends"].append({
                    "date": day,
                    "launches": int(item.get("launches", 0)),
                    "updates": int(item.get("updates", 0))
                })
        elif resp.status_code == 401:
            report["firebase_error"] = "Firebase 存取受限 (需將 firebase_rules.json 發布至控制台)"
        else:
            report["firebase_error"] = f"Firebase 回應代碼 {resp.status_code}"
    except Exception as e:
        report["firebase_error"] = str(e)

    # 2. 讀取 GitHub Releases 下載數據
    try:
        headers = {"User-Agent": "StockPortfolioTracker-Telemetry/1.1"}
        resp_gh = requests.get(GITHUB_RELEASES_URL, headers=headers, timeout=3.0)
        if resp_gh.status_code == 200:
            rel_list = resp_gh.json()
            if isinstance(rel_list, list):
                for rel in rel_list[:5]:
                    rel_tag = rel.get("tag_name", "")
                    assets_info = []
                    total_downloads = 0
                    for ast in rel.get("assets", []):
                        cnt = int(ast.get("download_count", 0))
                        total_downloads += cnt
                        assets_info.append({
                            "name": ast.get("name", ""),
                            "download_count": cnt,
                            "size": ast.get("size", 0)
                        })
                    report["github_releases"].append({
                        "tag": rel_tag,
                        "name": rel.get("name", rel_tag),
                        "total_downloads": total_downloads,
                        "published_at": rel.get("published_at", "")[:10],
                        "assets": assets_info
                    })
    except Exception:
        pass

    return report
