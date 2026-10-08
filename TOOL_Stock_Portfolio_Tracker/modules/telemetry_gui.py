# -*- coding: utf-8 -*-
"""
Stock Portfolio Tracker - 雲端遙測視覺化分析看板視窗 (telemetry_gui.py)
呈現：
1. 核心 KPI 卡片 (總啟動次數、總更新次數、今日活躍次數、GitHub 下載總量)
2. 各版本佔比視覺化進度條
3. 近 7 日每日活躍趨勢列表
4. GitHub Releases 附件下載細節
5. 一鍵在瀏覽器開啟線上 Web 儀表板
"""
import os
import sys
import webbrowser
import threading
import tkinter as tk
from tkinter import ttk, messagebox
from typing import Dict, Any

from telemetry_service import fetch_telemetry_report

class TelemetryDashboardDialog(tk.Toplevel):
    """雲端營運與版本遙測分析看板"""
    def __init__(self, parent=None):
        super().__init__(parent)
        self.title("雲端營運與版本遙測數據中心")
        self.geometry("740x640")
        self.minsize(680, 560)
        self.configure(bg="#1a1a24")
        if parent:
            self.transient(parent)

        self.report_data: Dict[str, Any] = {}
        self.is_loading = False

        self.build_ui()
        self.refresh_data()

    def build_ui(self):
        # 1. 頂部標題列
        top_bar = tk.Frame(self, bg="#242432", padx=20, pady=12)
        top_bar.pack(side=tk.TOP, fill=tk.X)

        tk.Label(
            top_bar, text="📊 Stock Portfolio Tracker - 營運與版本遙測分析",
            bg="#242432", fg="#38bdf8", font=("Microsoft JhengHei UI", 13, "bold")
        ).pack(anchor="w")

        tk.Label(
            top_bar, text="雙軌整合：Firebase Realtime Database 實時活躍計數 + GitHub Releases 下載數據",
            bg="#242432", fg="#94a3b8", font=("Microsoft JhengHei UI", 9)
        ).pack(anchor="w", pady=(2, 0))

        # 2. 底部操作按鈕列
        bot = tk.Frame(self, bg="#14141c", padx=20, pady=12)
        bot.pack(side=tk.BOTTOM, fill=tk.X)

        tk.Button(
            bot, text="確定關閉", bg="#334155", fg="#ffffff",
            activebackground="#475569", activeforeground="#ffffff",
            font=("Microsoft JhengHei UI", 9), relief="flat", cursor="hand2",
            command=self.destroy
        ).pack(side=tk.RIGHT, padx=6, ipadx=12, ipady=4)

        tk.Button(
            bot, text="🌐 開啟 Web 網頁看板", bg="#1e293b", fg="#38bdf8",
            activebackground="#334155", activeforeground="#38bdf8",
            font=("Microsoft JhengHei UI", 9, "bold"), relief="flat", cursor="hand2",
            command=self._open_web_dashboard
        ).pack(side=tk.RIGHT, padx=6, ipadx=10, ipady=4)

        self.btn_refresh = tk.Button(
            bot, text="🔄 立即重新整理", bg="#2563eb", fg="#ffffff",
            activebackground="#1d4ed8", activeforeground="#ffffff",
            font=("Microsoft JhengHei UI", 9, "bold"), relief="flat", cursor="hand2",
            command=self.refresh_data
        )
        self.btn_refresh.pack(side=tk.LEFT, padx=6, ipadx=10, ipady=4)

        self.lbl_status = tk.Label(bot, text="連線中...", bg="#14141c", fg="#64748b", font=("Microsoft JhengHei UI", 9))
        self.lbl_status.pack(side=tk.LEFT, padx=10)

        # 3. 中央滾動內容區
        main_frame = tk.Frame(self, bg="#1a1a24", padx=20, pady=10)
        main_frame.pack(side=tk.TOP, fill=tk.BOTH, expand=True)

        # 3.1 核心 KPI 卡片區
        kpi_row = tk.Frame(main_frame, bg="#1a1a24")
        kpi_row.pack(fill=tk.X, pady=(0, 10))

        self.card_launches = self._create_kpi_card(kpi_row, "🚀 累計啟動次數", "--", "#38bdf8")
        self.card_updates = self._create_kpi_card(kpi_row, "⚡ 累計更新次數", "--", "#10b981")
        self.card_today = self._create_kpi_card(kpi_row, "📅 今日活躍啟動", "--", "#f59e0b")
        self.card_gh_dl = self._create_kpi_card(kpi_row, "📦 GitHub 下載總量", "--", "#a855f7")

        # 3.2 版本分佈長條圖區
        ver_box = tk.LabelFrame(main_frame, text=" 👥 各版本使用者佔比分佈 (Version Distribution) ", bg="#22222e", fg="#e2e8f0", font=("Microsoft JhengHei UI", 9, "bold"), padx=14, pady=8)
        ver_box.pack(fill=tk.X, pady=(0, 10))
        self.ver_container = tk.Frame(ver_box, bg="#22222e")
        self.ver_container.pack(fill=tk.X)

        # 3.3 近期每日活躍趨勢與 GitHub 發布明細 (分頁或雙欄)
        split_row = tk.Frame(main_frame, bg="#1a1a24")
        split_row.pack(fill=tk.BOTH, expand=True)

        # 左欄：每日趨勢
        left_box = tk.LabelFrame(split_row, text=" 📈 近 7 日活躍趨勢 (Daily Trend) ", bg="#22222e", fg="#e2e8f0", font=("Microsoft JhengHei UI", 9, "bold"), padx=10, pady=6)
        left_box.pack(side=tk.LEFT, fill=tk.BOTH, expand=True, padx=(0, 6))

        self.tree_daily = ttk.Treeview(left_box, columns=("date", "launches", "updates"), show="headings", height=6)
        self.tree_daily.heading("date", text="日期")
        self.tree_daily.heading("launches", text="啟動次數")
        self.tree_daily.heading("updates", text="更新次數")
        self.tree_daily.column("date", width=95, anchor="center")
        self.tree_daily.column("launches", width=75, anchor="center")
        self.tree_daily.column("updates", width=75, anchor="center")
        self.tree_daily.pack(fill=tk.BOTH, expand=True)

        # 右欄：GitHub 發布下載量
        right_box = tk.LabelFrame(split_row, text=" 📦 GitHub Releases 發布下載明細 ", bg="#22222e", fg="#e2e8f0", font=("Microsoft JhengHei UI", 9, "bold"), padx=10, pady=6)
        right_box.pack(side=tk.RIGHT, fill=tk.BOTH, expand=True, padx=(6, 0))

        self.tree_gh = ttk.Treeview(right_box, columns=("tag", "asset", "downloads"), show="headings", height=6)
        self.tree_gh.heading("tag", text="版本標籤")
        self.tree_gh.heading("asset", text="檔案名稱")
        self.tree_gh.heading("downloads", text="下載次數")
        self.tree_gh.column("tag", width=80, anchor="center")
        self.tree_gh.column("asset", width=120, anchor="w")
        self.tree_gh.column("downloads", width=70, anchor="center")
        self.tree_gh.pack(fill=tk.BOTH, expand=True)

    def _create_kpi_card(self, parent, title: str, init_val: str, color: str):
        card = tk.Frame(parent, bg="#22222e", relief="solid", bd=1, padx=12, pady=8)
        card.pack(side=tk.LEFT, fill=tk.BOTH, expand=True, padx=4)

        tk.Label(card, text=title, bg="#22222e", fg="#94a3b8", font=("Microsoft JhengHei UI", 8)).pack(anchor="w")
        lbl_val = tk.Label(card, text=init_val, bg="#22222e", fg=color, font=("Microsoft JhengHei UI", 16, "bold"))
        lbl_val.pack(anchor="w", pady=(2, 0))
        return lbl_val

    def refresh_data(self):
        if self.is_loading:
            return
        self.is_loading = True
        self.btn_refresh.configure(state=tk.DISABLED, text="讀取中...")
        self.lbl_status.configure(text="正在向 Firebase 與 GitHub 擷取數據...")

        threading.Thread(target=self._worker_fetch, daemon=True).start()

    def _worker_fetch(self):
        data = fetch_telemetry_report()
        self.after(0, lambda: self._apply_report(data))

    def _apply_report(self, data: Dict[str, Any]):
        self.is_loading = False
        self.btn_refresh.configure(state=tk.NORMAL, text="🔄 立即重新整理")
        self.report_data = data

        # 更新狀態文字
        if data.get("firebase_connected"):
            self.lbl_status.configure(text=f"✔ Firebase 實時連線正常 (最後活躍: {data.get('last_active', '')})", fg="#10b981")
        else:
            err = data.get("firebase_error", "未連線")
            self.lbl_status.configure(text=f"⚠ {err}", fg="#f59e0b")

        # 更新 KPI 卡片
        self.card_launches.configure(text=f"{data.get('total_launches', 0):,}")
        self.card_updates.configure(text=f"{data.get('total_updates', 0):,}")
        self.card_today.configure(text=f"{data.get('today_launches', 0):,}")

        # 計算 GitHub 總下載量
        gh_releases = data.get("github_releases", [])
        total_gh_dl = sum(r.get("total_downloads", 0) for r in gh_releases)
        self.card_gh_dl.configure(text=f"{total_gh_dl:,}")

        # 更新版本分佈進度條
        for w in self.ver_container.winfo_children():
            w.destroy()

        v_dist = data.get("versions_distribution", {})
        total_v = sum(v_dist.values())
        if v_dist and total_v > 0:
            for ver, count in sorted(v_dist.items(), key=lambda x: x[1], reverse=True)[:5]:
                row = tk.Frame(self.ver_container, bg="#22222e")
                row.pack(fill=tk.X, pady=2)

                pct = int((count / total_v) * 100)
                tk.Label(row, text=f"{ver}", width=12, anchor="w", bg="#22222e", fg="#e2e8f0", font=("Microsoft JhengHei UI", 9, "bold")).pack(side=tk.LEFT)
                
                pb = ttk.Progressbar(row, orient="horizontal", mode="determinate", length=240)
                pb["value"] = pct
                pb.pack(side=tk.LEFT, padx=8)

                tk.Label(row, text=f"{pct}% ({count:,} 次)", bg="#22222e", fg="#38bdf8", font=("Microsoft JhengHei UI", 9)).pack(side=tk.LEFT)
        else:
            tk.Label(self.ver_container, text="尚無版本分佈統計數據（連線後開機將自動累加）", bg="#22222e", fg="#64748b", font=("Microsoft JhengHei UI", 9)).pack(anchor="w", pady=4)

        # 更新每日活躍趨勢表格
        for item in self.tree_daily.get_children():
            self.tree_daily.delete(item)
        trends = data.get("daily_trends", [])
        if trends:
            for t in trends:
                self.tree_daily.insert("", tk.END, values=(t.get("date"), f"{t.get('launches')} 次", f"{t.get('updates')} 次"))
        else:
            self.tree_daily.insert("", tk.END, values=("無紀錄", "-", "-"))

        # 更新 GitHub 下載明細表格
        for item in self.tree_gh.get_children():
            self.tree_gh.delete(item)
        if gh_releases:
            for rel in gh_releases:
                tag = rel.get("tag", "")
                for ast in rel.get("assets", []):
                    self.tree_gh.insert("", tk.END, values=(tag, ast.get("name"), f"{ast.get('download_count')} 次"))
        else:
            self.tree_gh.insert("", tk.END, values=("尚無 Release", "-", "-"))

    def _open_web_dashboard(self):
        """開啟獨立網頁儀表板"""
        url = "https://isaacyang34.github.io/Homepage/TOOL_Stock_Portfolio_Tracker/web/analytics.html"
        try:
            webbrowser.open(url)
        except Exception as e:
            messagebox.showerror("開啟失敗", f"無法開啟瀏覽器: {e}", parent=self)
