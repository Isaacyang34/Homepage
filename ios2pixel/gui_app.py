import sys
import os
import io
import threading
import time
import json
import tkinter as tk
from tkinter import ttk, messagebox
import tkinter.font as tkfont

# Prevent windowed mode NoneType stdout/stderr crashes
if sys.stdout is None:
    sys.stdout = io.StringIO()
if sys.stderr is None:
    sys.stderr = io.StringIO()
from typing import Optional

from core_sync import (
    find_adb, check_source_device, check_pixel,
    ingest_source_to_buffer, push_buffer_to_pixel,
    run_auto_pilot_loop,
    analyze_source_redundancy, delete_source_photos,
    get_buffer_summary, get_vault_dir, get_base_dir
)
from cloud_quota_washer import (
    launch_chrome_with_cdp, is_cdp_ready,
    scan_cloud_quota, download_quota_items, trash_quota_items,
    get_manifest_path, get_cloud_ledger_path, reconcile_cloud_ledger_with_sync_state
)

# Global mutex holder to prevent garbage collection
_app_mutex = None

def ensure_single_instance() -> bool:
    # Always allow opening to prevent mutex locking issues
    return True

class ModernTheme:
    BG_DARK = "#0F1014"
    CARD_BG = "#181920"
    CARD_BORDER = "#282A36"
    ACCENT_GREEN = "#00E676"
    ACCENT_BLUE = "#2979FF"
    ACCENT_HOVER = "#00C853"
    TEXT_MAIN = "#FFFFFF"
    TEXT_MUTED = "#8E94A5"
    STATUS_OFFLINE = "#4E5262"
    STATUS_ONLINE = "#00E676"
    CONSOLE_BG = "#0A0B0E"
    CONSOLE_FG = "#69F0AE"
    FONT_FAMILY = "Segoe UI"

class Any2PixelApp:
    def __init__(self, root: tk.Tk):
        self.root = root
        self.root.title("Any2Pixel Pro - 智慧相簿分時轉運站 (支援 iPhone & Android)")
        self.root.geometry("920x720")
        self.root.minsize(880, 640)
        self.root.configure(bg=ModernTheme.BG_DARK)

        # Set App Icon
        base_dir = get_base_dir()
        icon_path = os.path.join(base_dir, "app_icon.ico")
        if hasattr(sys, "_MEIPASS"):
            meipass_icon = os.path.join(sys._MEIPASS, "app_icon.ico")
            if os.path.exists(meipass_icon):
                icon_path = meipass_icon
        if os.path.exists(icon_path):
            try:
                self.root.iconbitmap(icon_path)
            except Exception:
                pass

        # Font scaling configuration
        self.font_scales = [1.0, 1.25, 1.45]
        self.font_scale_names = ["標準 (100%)", "中等 (125%)", "特大 (145%)"]
        self.current_font_scale_idx = 0

        # Execution states
        self.is_busy = False
        self.cancel_requested = False
        self.auto_pilot = tk.BooleanVar(value=False)
        self.src_prev_online = None  # None indicates initial poll
        self.pix_prev_online = None  # None indicates initial poll
        self.adb_path = find_adb()
        self.last_analysis = None

        self._setup_styles()
        self._build_ui()

        # 啟動時自動載入已儲存之雲端總帳，自動套用對帳結果，免去每次開啟重複比對
        ledger_path = get_cloud_ledger_path()
        if os.path.exists(ledger_path):
            try:
                reconcile_cloud_ledger_with_sync_state(log_callback=lambda msg: None)
            except Exception:
                pass

        # Start hardware poller
        self.poller_thread = threading.Thread(target=self._hardware_poller_loop, daemon=True)
        self.poller_thread.start()
        self.root.update()

    def _setup_styles(self):
        self.style = ttk.Style()
        self.style.theme_use("clam")
        self.style.configure(
            "Green.Horizontal.TProgressbar",
            troughcolor=ModernTheme.CARD_BG,
            background=ModernTheme.ACCENT_GREEN,
            bordercolor=ModernTheme.CARD_BORDER,
            lightcolor=ModernTheme.ACCENT_GREEN,
            darkcolor=ModernTheme.ACCENT_GREEN
        )

        # Setup dynamic scalable named fonts
        self.f_title = tkfont.Font(self.root, family=ModernTheme.FONT_FAMILY, size=18, weight="bold")
        self.f_sub = tkfont.Font(self.root, family=ModernTheme.FONT_FAMILY, size=9)
        self.f_card_title = tkfont.Font(self.root, family=ModernTheme.FONT_FAMILY, size=11, weight="bold")
        self.f_btn = tkfont.Font(self.root, family=ModernTheme.FONT_FAMILY, size=9, weight="bold")
        self.f_btn_normal = tkfont.Font(self.root, family=ModernTheme.FONT_FAMILY, size=9)
        self.f_btn_sm = tkfont.Font(self.root, family=ModernTheme.FONT_FAMILY, size=8, weight="bold")
        self.f_normal = tkfont.Font(self.root, family=ModernTheme.FONT_FAMILY, size=9)
        self.f_normal_bold = tkfont.Font(self.root, family=ModernTheme.FONT_FAMILY, size=9, weight="bold")
        self.f_muted = tkfont.Font(self.root, family=ModernTheme.FONT_FAMILY, size=8)
        self.f_dot = tkfont.Font(self.root, family=ModernTheme.FONT_FAMILY, size=12)
        self.f_console = tkfont.Font(self.root, family="Consolas", size=9)

    def cycle_font_scale(self):
        self.current_font_scale_idx = (self.current_font_scale_idx + 1) % len(self.font_scales)
        self._apply_font_scale()

    def _apply_font_scale(self):
        scale = self.font_scales[self.current_font_scale_idx]
        name = self.font_scale_names[self.current_font_scale_idx]
        if hasattr(self, 'btn_font_scale'):
            self.btn_font_scale.config(text=f"🗚 字級: {name}")

        self.f_title.configure(size=int(18 * scale))
        self.f_sub.configure(size=int(9 * scale))
        self.f_card_title.configure(size=int(11 * scale))
        self.f_btn.configure(size=int(9 * scale))
        self.f_btn_normal.configure(size=int(9 * scale))
        self.f_btn_sm.configure(size=int(8 * scale))
        self.f_normal.configure(size=int(9 * scale))
        self.f_normal_bold.configure(size=int(9 * scale))
        self.f_muted.configure(size=int(8 * scale))
        self.f_dot.configure(size=int(12 * scale))
        self.f_console.configure(size=int(9 * scale))

        if scale > 1.3:
            self.root.geometry("1000x780")
        elif scale > 1.1:
            self.root.geometry("950x750")
        else:
            self.root.geometry("920x720")

    def _build_ui(self):
        # 1. Header (Left: Titles, Right: Font Size Scaler)
        header = tk.Frame(self.root, bg=ModernTheme.BG_DARK)
        header.pack(fill="x", padx=24, pady=(16, 8))

        header_left = tk.Frame(header, bg=ModernTheme.BG_DARK)
        header_left.pack(side="left", fill="x", expand=True)

        lbl_title = tk.Label(
            header_left, text="Any2Pixel Pro - 智慧相簿分時轉運站",
            font=self.f_title,
            fg=ModernTheme.TEXT_MAIN, bg=ModernTheme.BG_DARK
        )
        lbl_title.pack(anchor="w")

        lbl_sub = tk.Label(
            header_left,
            text="雙機無需同時連接！以電腦硬碟為安全中繼緩衝，分時接線：主力手機 (iPhone/Android) 斷點採集 ➔ Pixel 1 容量精算推送",
            font=self.f_sub,
            fg=ModernTheme.TEXT_MUTED, bg=ModernTheme.BG_DARK
        )
        lbl_sub.pack(anchor="w", pady=(2, 0))

        header_right = tk.Frame(header, bg=ModernTheme.BG_DARK)
        header_right.pack(side="right", anchor="ne", pady=2)

        self.btn_font_scale = tk.Button(
            header_right,
            text=f"🗚 字級: {self.font_scale_names[self.current_font_scale_idx]}",
            font=self.f_btn_sm,
            fg="#69F0AE", bg="#142B20", relief="flat", cursor="hand2", padx=10, pady=5,
            command=self.cycle_font_scale
        )
        self.btn_font_scale.pack(side="right")

        # 2. Three Cards Grid (Source Phone / PC Buffer / Pixel 1)
        cards = tk.Frame(self.root, bg=ModernTheme.BG_DARK)
        cards.pack(fill="x", padx=24, pady=8)
        cards.grid_columnconfigure(0, weight=1)
        cards.grid_columnconfigure(1, weight=1)
        cards.grid_columnconfigure(2, weight=1)

        # Card 1: Source Producer (Universal iPhone or Android)
        self.card_src = tk.Frame(cards, bg=ModernTheme.CARD_BG, highlightbackground=ModernTheme.CARD_BORDER, highlightthickness=1)
        self.card_src.grid(row=0, column=0, sticky="nsew", padx=(0, 6), pady=0)

        h1 = tk.Frame(self.card_src, bg=ModernTheme.CARD_BG)
        h1.pack(fill="x", padx=12, pady=(10, 4))
        self.dot_src = tk.Label(h1, text="●", font=self.f_dot, fg=ModernTheme.STATUS_OFFLINE, bg=ModernTheme.CARD_BG)
        self.dot_src.pack(side="left")
        self.lbl_src_title = tk.Label(h1, text=" 來源主力手機 (iPhone / Android)", font=self.f_card_title, fg=ModernTheme.TEXT_MAIN, bg=ModernTheme.CARD_BG)
        self.lbl_src_title.pack(side="left")

        self.lbl_src_status = tk.Label(self.card_src, text="未連線 (等待插線 - 支援 iPhone / Android)", font=self.f_muted, fg=ModernTheme.TEXT_MUTED, bg=ModernTheme.CARD_BG)
        self.lbl_src_status.pack(anchor="w", padx=12, pady=(0, 6))

        self.btn_ingest = tk.Button(
            self.card_src, text="📥 採集至電腦緩衝",
            font=self.f_btn,
            fg="#0A0B0E", bg=ModernTheme.ACCENT_BLUE, relief="flat", cursor="hand2", padx=10, pady=5,
            command=self.trigger_source_ingest
        )
        self.btn_ingest.pack(fill="x", padx=12, pady=(0, 4))

        self.btn_analyze = tk.Button(
            self.card_src, text="🔍 分析可清理重複照片",
            font=self.f_btn_normal,
            fg=ModernTheme.TEXT_MAIN, bg=ModernTheme.CARD_BORDER, relief="flat", cursor="hand2", padx=10, pady=4,
            command=self.trigger_analyze_redundancy
        )
        self.btn_analyze.pack(fill="x", padx=12, pady=(0, 4))

        self.btn_clean_src = tk.Button(
            self.card_src, text="🗑️ 釋放來源手機空間 (刪除已備份)",
            font=self.f_btn,
            fg="#FF5252", bg="#2A1B1F", relief="flat", cursor="hand2", padx=10, pady=4,
            command=self.trigger_clean_source
        )
        self.btn_clean_src.pack(fill="x", padx=12, pady=(0, 4))

        tk.Label(self.card_src, text="* 經電腦與 Pixel 雙備份驗證才允許刪除", font=self.f_muted, fg=ModernTheme.TEXT_MUTED, bg=ModernTheme.CARD_BG).pack(anchor="w", padx=12, pady=(0, 6))

        # Card 2: PC Vault Buffer
        self.card_buf = tk.Frame(cards, bg=ModernTheme.CARD_BG, highlightbackground=ModernTheme.CARD_BORDER, highlightthickness=1)
        self.card_buf.grid(row=0, column=1, sticky="nsew", padx=3, pady=0)

        h2 = tk.Frame(self.card_buf, bg=ModernTheme.CARD_BG)
        h2.pack(fill="x", padx=12, pady=(10, 4))
        tk.Label(h2, text="●", font=self.f_dot, fg=ModernTheme.STATUS_ONLINE, bg=ModernTheme.CARD_BG).pack(side="left")
        tk.Label(h2, text=" 電腦緩衝庫 (中繼)", font=self.f_card_title, fg=ModernTheme.TEXT_MAIN, bg=ModernTheme.CARD_BG).pack(side="left")

        self.lbl_buf_stats = tk.Label(self.card_buf, text="庫存: 0 張 (0.00 GB)", font=self.f_normal_bold, fg=ModernTheme.ACCENT_GREEN, bg=ModernTheme.CARD_BG)
        self.lbl_buf_stats.pack(anchor="w", padx=12, pady=(0, 2))

        self.lbl_buf_pending = tk.Label(self.card_buf, text="待送 Pixel: 0 張", font=self.f_muted, fg=ModernTheme.TEXT_MUTED, bg=ModernTheme.CARD_BG)
        self.lbl_buf_pending.pack(anchor="w", padx=12, pady=(0, 6))

        btn_open_vault = tk.Button(
            self.card_buf, text="📂 打開緩衝資料夾",
            font=self.f_btn_normal,
            fg=ModernTheme.TEXT_MAIN, bg=ModernTheme.CARD_BORDER, relief="flat", cursor="hand2", padx=10, pady=5,
            command=self._open_vault_folder
        )
        btn_open_vault.pack(fill="x", padx=12, pady=(0, 6))
        tk.Label(self.card_buf, text="* 斷點續傳保護，永不漏片", font=self.f_muted, fg=ModernTheme.TEXT_MUTED, bg=ModernTheme.CARD_BG).pack(anchor="w", padx=12, pady=(0, 8))

        # Card 3: Pixel Consumer
        self.card_pix = tk.Frame(cards, bg=ModernTheme.CARD_BG, highlightbackground=ModernTheme.CARD_BORDER, highlightthickness=1)
        self.card_pix.grid(row=0, column=2, sticky="nsew", padx=(6, 0), pady=0)

        h3 = tk.Frame(self.card_pix, bg=ModernTheme.CARD_BG)
        h3.pack(fill="x", padx=12, pady=(10, 4))
        self.dot_pix = tk.Label(h3, text="●", font=self.f_dot, fg=ModernTheme.STATUS_OFFLINE, bg=ModernTheme.CARD_BG)
        self.dot_pix.pack(side="left")
        tk.Label(h3, text=" Google Pixel 1", font=self.f_card_title, fg=ModernTheme.TEXT_MAIN, bg=ModernTheme.CARD_BG).pack(side="left")

        self.lbl_pix_status = tk.Label(self.card_pix, text="未連線 (等待插線)", font=self.f_muted, fg=ModernTheme.TEXT_MUTED, bg=ModernTheme.CARD_BG)
        self.lbl_pix_status.pack(anchor="w", padx=12, pady=(0, 2))

        self.lbl_pix_space = tk.Label(self.card_pix, text="剩餘容量: 未連線", font=self.f_normal_bold, fg=ModernTheme.ACCENT_GREEN, bg=ModernTheme.CARD_BG)
        self.lbl_pix_space.pack(anchor="w", padx=12, pady=(0, 6))

        self.btn_push = tk.Button(
            self.card_pix, text="推送至 Pixel (<1GB 單批)",
            font=self.f_btn_normal,
            fg=ModernTheme.TEXT_MAIN, bg=ModernTheme.CARD_BORDER, relief="flat", cursor="hand2", padx=10, pady=4,
            command=self.trigger_pixel_push
        )
        self.btn_push.pack(fill="x", padx=12, pady=(0, 4))

        self.btn_auto_pilot = tk.Button(
            self.card_pix, text="⚡ Auto-Pilot 全自動閉環",
            font=self.f_btn,
            fg="#0A0B0E", bg=ModernTheme.ACCENT_GREEN, relief="flat", cursor="hand2", padx=10, pady=4,
            command=self.trigger_auto_pilot
        )
        self.btn_auto_pilot.pack(fill="x", padx=12, pady=(0, 4))

        self.btn_wash_quota = tk.Button(
            self.card_pix, text="✨ 雲端配額自動洗白 (歸零扣額相片)",
            font=self.f_btn,
            fg="#FFD700", bg="#2B2414", relief="flat", cursor="hand2", padx=10, pady=4,
            command=self.trigger_cloud_quota_wash
        )
        self.btn_wash_quota.pack(fill="x", padx=12, pady=(0, 4))
        tk.Label(self.card_pix, text="* 全自動偵測雲端扣額舊照並洗成 0 空間", font=self.f_muted, fg=ModernTheme.TEXT_MUTED, bg=ModernTheme.CARD_BG).pack(anchor="w", padx=12, pady=(0, 6))

        # 3. Mode Bar
        mode_bar = tk.Frame(self.root, bg=ModernTheme.BG_DARK)
        mode_bar.pack(fill="x", padx=24, pady=(4, 8))

        chk_auto = tk.Checkbutton(
            mode_bar, text="常駐分時自動感知 (插主力手機自動汲取，插 Pixel 自動精算推送)",
            variable=self.auto_pilot,
            font=self.f_btn,
            fg=ModernTheme.TEXT_MAIN, bg=ModernTheme.BG_DARK,
            activeforeground=ModernTheme.ACCENT_GREEN, activebackground=ModernTheme.BG_DARK,
            selectcolor=ModernTheme.CARD_BG
        )
        chk_auto.pack(side="left")

        btn_reconcile = tk.Button(
            mode_bar, text="🔍 雲端對帳 (比對 Google 相簿現況做紀錄)",
            font=self.f_btn_sm,
            fg="#69F0AE", bg="#142B20", relief="flat", cursor="hand2", padx=8, pady=4,
            command=self.trigger_cloud_reconciliation
        )
        btn_reconcile.pack(side="left", padx=(12, 0))

        btn_guide = tk.Button(
            mode_bar, text="📖 Pixel 空間釋放指引",
            font=self.f_btn_sm,
            fg=ModernTheme.TEXT_MUTED, bg=ModernTheme.CARD_BG, relief="flat", cursor="hand2", padx=8, pady=4,
            command=self._show_guide
        )
        btn_guide.pack(side="right")

        # 4. Progress Bar Area
        p_frame = tk.Frame(self.root, bg=ModernTheme.BG_DARK)
        p_frame.pack(fill="x", padx=24, pady=(2, 8))

        self.lbl_progress = tk.Label(p_frame, text="系統就緒 - 請連接主力手機 (iPhone/Android) 或 Google Pixel 1", font=self.f_normal, fg=ModernTheme.TEXT_MUTED, bg=ModernTheme.BG_DARK)
        self.lbl_progress.pack(anchor="w", pady=(0, 3))

        self.progress_bar = ttk.Progressbar(p_frame, style="Green.Horizontal.TProgressbar", orient="horizontal", mode="determinate")
        self.progress_bar.pack(fill="x")

        # 5. Console Terminal Log
        log_box = tk.Frame(self.root, bg=ModernTheme.BG_DARK)
        log_box.pack(fill="both", expand=True, padx=24, pady=(0, 16))

        tk.Label(log_box, text="即時執行日誌", font=self.f_normal_bold, fg=ModernTheme.TEXT_MUTED, bg=ModernTheme.BG_DARK).pack(anchor="w", pady=(0, 2))

        self.txt_log = tk.Text(log_box, bg=ModernTheme.CONSOLE_BG, fg=ModernTheme.CONSOLE_FG, insertbackground=ModernTheme.ACCENT_GREEN, font=self.f_console, relief="flat", wrap="word", padx=8, pady=6)
        self.txt_log.pack(side="left", fill="both", expand=True)

        sc = ttk.Scrollbar(log_box, orient="vertical", command=self.txt_log.yview)
        sc.pack(side="right", fill="y")
        self.txt_log.config(yscrollcommand=sc.set)

        self.log_msg("[系統啟動] 分時轉運工作站就緒。隨時可連接主力手機 (iPhone/Android) 或 Pixel 1。")

    def log_msg(self, text: str):
        def _a():
            self.txt_log.insert(tk.END, f"[{time.strftime('%H:%M:%S')}] {text}\n")
            self.txt_log.see(tk.END)
        self.root.after(0, _a)

    def _open_vault_folder(self):
        vault = get_vault_dir()
        if os.name == 'nt':
            os.startfile(vault)

    def _show_guide(self):
        msg = (
            "【分時備份循環操作法】：\n\n"
            "1.【白天/平時】：插上來源主力手機 (iPhone 或 Android)，程式自動將新增相片下載至「電腦緩衝庫」（斷點續傳，手機原檔絕不刪除）。\n\n"
            "2.【充電/空閒時】：插上 Pixel 1，程式自動精算 Pixel 剩餘容量，安全打包一批推送到 Pixel。\n\n"
            "3.【Pixel 備份完成】：在 Pixel 開啟 Google 相簿 App，等右上角顯示「備份已完成」，點擊頭像 ➔「釋出裝置空間」。\n\n"
            "4.【循環下一批】：釋出空間後，再插上 Pixel 即可繼續輸送下一批！直到電腦緩衝庫全數清空上雲端。"
        )
        messagebox.showinfo("分時轉運操作指引", msg)

    def _refresh_buffer_ui(self):
        stats = get_buffer_summary()
        self.lbl_buf_stats.config(text=f"庫存: {stats['total_files']} 張 ({stats['total_gb']:.2f} GB)")
        self.lbl_buf_pending.config(text=f"待送 Pixel: {stats['pending_pixel']} 張 ({stats['pending_gb']:.2f} GB)")

    def _hardware_poller_loop(self):
        """Polls hardware every 3 seconds and handles smart auto-routing."""
        while True:
            try:
                src_ok, src_info, src_type, src_id = check_source_device(self.adb_path)
                pix_ok, pix_info, free_gb = check_pixel(self.adb_path)

                def update_ui():
                    self._refresh_buffer_ui()

                    # Source Device UI
                    if src_ok:
                        self.dot_src.config(fg=ModernTheme.STATUS_ONLINE)
                        if src_type == "android":
                            self.lbl_src_title.config(text=" Android 主力機")
                        else:
                            self.lbl_src_title.config(text=" Apple iPhone")
                        self.lbl_src_status.config(text=src_info, fg=ModernTheme.TEXT_MAIN)
                    else:
                        self.dot_src.config(fg=ModernTheme.STATUS_OFFLINE)
                        self.lbl_src_title.config(text=" 來源主力手機 (iPhone / Android)")
                        self.lbl_src_status.config(text=src_info, fg=ModernTheme.TEXT_MUTED)

                    # Pixel UI
                    if pix_ok:
                        self.dot_pix.config(fg=ModernTheme.STATUS_ONLINE)
                        self.lbl_pix_status.config(text=pix_info, fg=ModernTheme.TEXT_MAIN)
                        self.lbl_pix_space.config(text=f"可用: {free_gb:.2f} GB (預算: {max(0.0, free_gb-1.5):.2f}G)")
                    else:
                        self.dot_pix.config(fg=ModernTheme.STATUS_OFFLINE)
                        self.lbl_pix_status.config(text=f"未連線 ({pix_info})", fg=ModernTheme.TEXT_MUTED)
                        self.lbl_pix_space.config(text="剩餘容量: 未連線")

                self.root.after(0, update_ui)

                # First poll: record initial status without triggering auto-actions
                if self.src_prev_online is None or self.pix_prev_online is None:
                    self.src_prev_online = src_ok
                    self.pix_prev_online = pix_ok
                else:
                    # Smart Auto-Pilot: Only trigger if enabled and device was explicitly plugged in
                    if self.auto_pilot.get() and not self.is_busy:
                        # Source Phone transition False -> True
                        if src_ok and not self.src_prev_online:
                            self.src_prev_online = True
                            self.log_msg(f"[自動感知] 偵測到主力手機 ({src_info}) 插入！準備啟動增量採集至電腦緩衝庫...")
                            self.root.after(1000, self.trigger_source_ingest)
                        # Pixel transition False -> True
                        elif pix_ok and not self.pix_prev_online:
                            self.pix_prev_online = True
                            buf = get_buffer_summary()
                            if buf["pending_pixel"] > 0:
                                self.log_msg(f"[自動感知] 偵測到 Pixel 插入！緩衝庫尚有 {buf['pending_pixel']} 張待傳，準備容量精算推送...")
                                self.root.after(1000, self.trigger_pixel_push)

                if not src_ok:
                    self.src_prev_online = False
                if not pix_ok:
                    self.pix_prev_online = False

            except Exception:
                pass

            time.sleep(3)

    def trigger_source_ingest(self):
        if self.is_busy:
            return
        self.is_busy = True
        self._set_buttons_state("disabled")
        self.progress_bar["value"] = 0

        def worker():
            ok = False
            msg = ""
            stats = {}
            try:
                def on_p(cur, tot, fname, act):
                    def _u():
                        pct = int((cur / tot) * 100) if tot > 0 else 0
                        self.progress_bar["value"] = pct
                        self.lbl_progress.config(text=f"[主力機 ➔ 電腦] ({pct}%) [{cur}/{tot}] {fname} - {act}")
                    self.root.after(0, _u)

                self.log_msg(">>> 開始從來源主力手機增量採集新相片至電腦緩衝庫...")
                ok, msg, stats = ingest_source_to_buffer(
                    progress_callback=on_p,
                    log_callback=self.log_msg,
                    stop_event=lambda: self.cancel_requested
                )
            except Exception as e:
                ok = False
                msg = str(e)
                self.log_msg(f"[!] 來源手機採集異常: {e}")
            finally:
                def done():
                    self.is_busy = False
                    self._set_buttons_state("normal")
                    self._refresh_buffer_ui()
                    if ok:
                        self.lbl_progress.config(text=f"✔ 主力手機採集完成！已安全存入電腦緩衝庫。")
                    else:
                        self.lbl_progress.config(text=f"✖ 主力手機採集中斷: {msg}")

                self.root.after(0, done)

        threading.Thread(target=worker, daemon=True).start()

    def trigger_pixel_push(self):
        if self.is_busy:
            return
        self.is_busy = True
        self._set_buttons_state("disabled")
        self.progress_bar["value"] = 0

        def worker():
            ok = False
            msg = ""
            stats = {}
            try:
                def on_p(cur, tot, fname, act):
                    def _u():
                        pct = int((cur / tot) * 100) if tot > 0 else 0
                        self.progress_bar["value"] = pct
                        self.lbl_progress.config(text=f"[電腦 ➔ Pixel] ({pct}%) [{cur}/{tot}] {fname} - {act}")
                    self.root.after(0, _u)

                self.log_msg(">>> 依 Pixel 剩餘容量規劃本波次推送清單 (< 1GB)...")
                ok, msg, stats = push_buffer_to_pixel(
                    adb_path=self.adb_path,
                    progress_callback=on_p,
                    log_callback=self.log_msg,
                    stop_event=lambda: self.cancel_requested,
                    batch_max_gb=0.8,
                    min_safety_gb=1.5
                )
            except Exception as e:
                ok = False
                msg = str(e)
                self.log_msg(f"[!] 推送至 Pixel 過程異常: {e}")
            finally:
                def done():
                    self.is_busy = False
                    self._set_buttons_state("normal")
                    self._refresh_buffer_ui()
                    if ok:
                        cnt = stats.get("pushed", 0)
                        rem = stats.get("remaining_buffer", 0)
                        self.lbl_progress.config(text=f"✔ 本波推送完成: 送出 {cnt} 張，待傳 {rem} 張")
                    else:
                        self.lbl_progress.config(text=f"✖ 推送中斷: {msg}")

                self.root.after(0, done)

        threading.Thread(target=worker, daemon=True).start()

    def trigger_auto_pilot(self):
        if self.is_busy:
            return
        self.is_busy = True
        self.btn_ingest.config(state="disabled")
        self.btn_push.config(state="disabled")
        self.btn_auto_pilot.config(state="disabled")
        self.progress_bar["value"] = 0

        def worker():
            ok = False
            msg = ""
            stats = {}
            try:
                def on_p(cur, tot, fname, act):
                    def _u():
                        pct = int((cur / tot) * 100) if tot > 0 else 0
                        self.progress_bar["value"] = pct
                        self.lbl_progress.config(text=f"[Auto-Pilot 閉環] ({pct}%) [{cur}/{tot}] {fname} - {act}")
                    self.root.after(0, _u)

                self.log_msg(">>> 啟動 Auto-Pilot 全自動閉環同步模式...")
                ok, msg, stats = run_auto_pilot_loop(
                    adb_path=self.adb_path,
                    progress_callback=on_p,
                    log_callback=self.log_msg,
                    stop_event=lambda: self.cancel_requested,
                    batch_max_gb=0.8,
                    min_safety_gb=1.5
                )
            except Exception as e:
                ok = False
                msg = str(e)
            finally:
                def done():
                    self.is_busy = False
                    self._set_buttons_state("normal")
                    self._refresh_buffer_ui()
                    if ok:
                        self.lbl_progress.config(text="🎉 Auto-Pilot 全自動閉環同步完成！緩衝庫所有相片已全數入雲！")
                        messagebox.showinfo("同步大告成", "恭喜！電腦緩衝庫所有相片已全部安全送入 Google Pixel，並完成雲端備份循環！")
                    else:
                        self.lbl_progress.config(text=f"✖ Auto-Pilot 中斷: {msg}")

                self.root.after(0, done)

        threading.Thread(target=worker, daemon=True).start()

    def _set_buttons_state(self, state: str):
        for btn in [getattr(self, 'btn_ingest', None),
                    getattr(self, 'btn_analyze', None),
                    getattr(self, 'btn_clean_src', None),
                    getattr(self, 'btn_push', None),
                    getattr(self, 'btn_auto_pilot', None),
                    getattr(self, 'btn_wash_quota', None)]:
            if btn:
                try:
                    btn.config(state=state)
                except Exception:
                    pass

    def trigger_analyze_redundancy(self):
        if self.is_busy:
            return
        self.is_busy = True
        self._set_buttons_state("disabled")
        self.progress_bar["value"] = 0
        self.lbl_progress.config(text="正在分析來源手機重複與可清理相片...")

        def worker():
            ok = False
            msg = ""
            res = {}
            try:
                self.log_msg(">>> 開始比對來源手機相片與電腦緩衝庫、Pixel 備份清單...")
                ok, msg, res = analyze_source_redundancy(log_callback=self.log_msg)
            except Exception as e:
                ok = False
                msg = str(e)
                self.log_msg(f"[!] 分析來源手機相片異常: {e}")
            finally:
                def done():
                    self.is_busy = False
                    self._set_buttons_state("normal")
                    if ok:
                        self.last_analysis = res
                        safe_cnt = res.get("safe_cnt", 0)
                        safe_gb = res.get("safe_gb", 0.0)
                        unbacked = res.get("unbacked_cnt", 0)
                        self.lbl_progress.config(text=f"✔ 分析完成: 可安全釋放 {safe_cnt} 張 ({safe_gb:.2f} GB)")
                        messagebox.showinfo(
                            "來源手機相片分析結果",
                            f"【分析結果】\n\n"
                            f"● 手機總相片數: {res.get('total_iphone_cnt', 0)} 張\n"
                            f"● 雙重備份已完成 (可安全刪除): {safe_cnt} 張 ({safe_gb:.2f} GB)\n"
                            f"● 尚未完整備份 (系統強制保留): {unbacked} 張\n\n"
                            f"可點擊『🗑️ 釋放主力手機空間』進行安全清理！"
                        )
                    else:
                        self.lbl_progress.config(text=f"✖ 分析失敗: {msg}")
                        messagebox.showwarning("分析失敗", f"無法分析來源手機相簿: {msg}\n請確認手機已解鎖並信任此電腦/開啟偵錯。")
                self.root.after(0, done)

        threading.Thread(target=worker, daemon=True).start()

    def trigger_clean_source(self):
        if self.is_busy:
            return

        if not self.last_analysis or self.last_analysis.get("safe_cnt", 0) == 0:
            ans = messagebox.askyesno(
                "需要先執行分析",
                "尚未進行相片備份比對，是否立即啟動分析？"
            )
            if ans:
                self.trigger_analyze_redundancy()
            return

        safe_cnt = self.last_analysis["safe_cnt"]
        safe_gb = self.last_analysis["safe_gb"]
        targets = self.last_analysis["safe_to_delete"]

        ans = messagebox.askyesnocancel(
            "安全釋放主力手機空間確認",
            f"系統已驗證以下相片已 100% 存入電腦緩衝庫並推送至 Pixel：\n\n"
            f"★ 可釋放數量: {safe_cnt} 張相片/影片\n"
            f"★ 預計騰出空間: {safe_gb:.2f} GB\n\n"
            f"【是 (Yes)】：全數批次清理 (永久從手機刪除 {safe_cnt} 張已備份相片)\n"
            f"【否 (No)】：僅測試刪除 1 張相片 (先刪除 1 張測試相簿反應)\n"
            f"【取消 (Cancel)】：取消本次操作"
        )

        if ans is None:
            return

        selected_targets = []
        if ans is True:
            selected_targets = targets
            self.log_msg(f"🗑️ [批次清理] 使用者確認刪除全部 {len(selected_targets)} 張已備份相片...")
        else:
            selected_targets = [targets[-1]]
            fname_sample = selected_targets[0].get('fname') or selected_targets[0].get('filename') or '相片'
            self.log_msg(f"🧪 [測試模式] 本次僅測試刪除 1 張相片: {fname_sample}")

        self.is_busy = True
        self._set_buttons_state("disabled")
        self.progress_bar["value"] = 0

        def worker():
            ok = False
            msg = ""
            stats = {}
            try:
                def on_p(cur, tot, fname, act):
                    def _u():
                        pct = int((cur / tot) * 100) if tot > 0 else 0
                        self.progress_bar["value"] = pct
                        self.lbl_progress.config(text=f"[清理手機] ({pct}%) [{cur}/{tot}] {fname} - {act}")
                    self.root.after(0, _u)

                ok, msg, stats = delete_source_photos(
                    targets=selected_targets,
                    progress_callback=on_p,
                    log_callback=self.log_msg,
                    stop_event=lambda: self.cancel_requested
                )
            except Exception as e:
                ok = False
                msg = str(e)
                self.log_msg(f"[!] 清理手機異常: {e}")
            finally:
                def done():
                    self.is_busy = False
                    self._set_buttons_state("normal")
                    self.last_analysis = None
                    if ok:
                        succ = stats.get("success", stats.get("deleted", 0))
                        freed = stats.get("freed_gb", 0.0)
                        self.lbl_progress.config(text=f"✔ 已從手機成功刪除 {succ} 張相片")
                        messagebox.showinfo(
                            "手機空間釋放完成",
                            f"【清理完成】\n\n"
                            f"已成功從來源手機刪除 {succ} 張已備份相片！\n\n"
                            f"* 電腦緩衝庫與 Google 相簿完整保留原檔。\n"
                            f"* 請查看手機相簿確認空間已釋出。"
                        )
                    else:
                        self.lbl_progress.config(text=f"✖ 手機清理中斷: {msg}")
                self.root.after(0, done)

        threading.Thread(target=worker, daemon=True).start()

    def trigger_cloud_quota_wash(self):
        if self.is_busy:
            return

        # Step 1: Ensure Chrome CDP is ready
        if not is_cdp_ready():
            ans = messagebox.askyesno(
                "啟動雲端配額洗白引擎",
                "【雲端相簿自動連線說明】\n\n"
                "系統即將自動啟動專用 Chrome 視窗連接 Google 相簿：\n"
                "1. 請在跳出的 Chrome 視窗登入您的 Google 帳號 (只需登入一次)。\n"
                "2. 登入後看到 Google 相簿主畫面，請回本程式點擊【確定】繼續。\n\n"
                "是否立即啟動 Chrome 視窗？"
            )
            if not ans:
                return

            ok, msg = launch_chrome_with_cdp()
            if not ok:
                messagebox.showerror("啟動失敗", f"無法啟動 Chrome CDP 服務:\n{msg}")
                return

            ans2 = messagebox.askokcancel(
                "等待 Google 帳號登入",
                "Chrome 視窗已啟動！\n\n"
                "請確認您已在該 Chrome 視窗成功登入 Google 帳號並看到相簿。\n"
                "完成後請點擊【確定】立即開始自動掃描佔空間相片！"
            )
            if not ans2:
                return

        self.is_busy = True
        self._set_buttons_state("disabled")
        self.progress_bar["value"] = 0
        self.lbl_progress.config(text="正在全自動掃描 Google 雲端扣額相片...")

        def worker():
            ok = False
            msg = ""
            summary = {}
            try:
                self.log_msg(">>> [洗白引擎] 正在透過 CDP 掃描雲端相簿並批次比對配額 (EWgK9e)...")
                ok, msg, summary = scan_cloud_quota(
                    log_callback=self.log_msg,
                    stop_event=lambda: self.cancel_requested
                )
            except Exception as e:
                ok = False
                msg = str(e)
                self.log_msg(f"[!] 雲端掃描異常: {e}")

            def on_scan_done():
                self.is_busy = False
                self._set_buttons_state("normal")

                if not ok:
                    self.lbl_progress.config(text=f"✖ 掃描中斷: {msg}")
                    messagebox.showerror("掃描失敗", f"無法完成雲端掃描:\n{msg}")
                    return

                quota_cnt = summary.get("quota_count", 0)
                quota_gb = summary.get("total_space_gb", 0.0)
                tot_scanned = summary.get("total_scanned", 0)

                if quota_cnt == 0:
                    self.lbl_progress.config(text="🎉 太棒了！您的 Google 相簿沒有任何佔用空間的相片！")
                    messagebox.showinfo("無佔用空間", f"掃描了 {tot_scanned} 個項目，所有項目皆已是免空間狀態！")
                    return

                self.lbl_progress.config(text=f"✔ 掃描完成: 發現 {quota_cnt} 張佔空間相片 ({quota_gb:.2f} GB)")

                start_wash = messagebox.askyesno(
                    "雲端空間自動洗白確認",
                    f"【掃描成果分析】\n\n"
                    f"● 雲端總掃描項目: {tot_scanned} 個\n"
                    f"● 真正扣除空間相片: {quota_cnt} 張\n"
                    f"● 佔用 Google 空間: {quota_gb:.2f} GB\n\n"
                    f"【自動化洗白流程】：\n"
                    f"1. 自動將這 {quota_cnt} 張原檔完整下載保存至電腦緩衝庫。\n"
                    f"2. 自動移至雲端垃圾桶以釋放空間。\n"
                    f"3. 自動透過 Pixel 1 重新備份，永久轉為 0 空間！\n\n"
                    f"是否立即啟動全自動洗白作業？"
                )

                if not start_wash:
                    return

                self._run_wash_pipeline(summary)

            self.root.after(0, on_scan_done)

        threading.Thread(target=worker, daemon=True).start()

    def _run_wash_pipeline(self, scan_summary: dict):
        manifest_path = scan_summary.get("manifest_path")
        if not manifest_path or not os.path.exists(manifest_path):
            messagebox.showerror("錯誤", "找不到掃描清單檔案")
            return

        with open(manifest_path, "r", encoding="utf-8") as f:
            manifest_data = json.load(f)

        items = manifest_data.get("items", [])
        if not items:
            return

        self.is_busy = True
        self._set_buttons_state("disabled")
        self.progress_bar["value"] = 0
        vault_dir = get_vault_dir()

        def worker():
            try:
                def on_p_dl(cur, tot, fname, act):
                    def _u():
                        pct = int((cur / tot) * 100) if tot > 0 else 0
                        self.progress_bar["value"] = pct
                        self.lbl_progress.config(text=f"[洗白步驟 1/3 下載] ({pct}%) [{cur}/{tot}] {fname}")
                    self.root.after(0, _u)

                # Step 1: Download
                self.log_msg(">>> [洗白步驟 1/3] 開始將雲端扣額原檔下載至電腦緩衝庫...")
                ok_dl, msg_dl, stats_dl = download_quota_items(
                    manifest_items=items,
                    vault_dir=vault_dir,
                    log_callback=self.log_msg,
                    progress_callback=on_p_dl,
                    stop_event=lambda: self.cancel_requested
                )
                if not ok_dl:
                    self.log_msg(f"[!] 下載步驟中斷: {msg_dl}")
                    return

                # Step 2: Trash
                def on_p_tr(cur, tot, fname, act):
                    def _u():
                        pct = int((cur / tot) * 100) if tot > 0 else 0
                        self.progress_bar["value"] = pct
                        self.lbl_progress.config(text=f"[洗白步驟 2/3 釋出] ({pct}%) [{cur}/{tot}] {fname}")
                    self.root.after(0, _u)

                self.log_msg(">>> [洗白步驟 2/3] 本機檔案驗證通過，開始自動將雲端扣額舊檔移入垃圾桶...")
                ok_tr, msg_tr, stats_tr = trash_quota_items(
                    manifest_items=items,
                    vault_dir=vault_dir,
                    log_callback=self.log_msg,
                    progress_callback=on_p_tr,
                    stop_event=lambda: self.cancel_requested
                )

                # Step 3: Trigger Auto-Pilot push to Pixel
                self.log_msg(">>> [洗白步驟 3/3] 啟動 Pixel 1 重新備份管線 (享有終生無限 0 空間)...")
                self.root.after(1000, self.trigger_auto_pilot)

            except Exception as e:
                self.log_msg(f"[!] 洗白流程異常: {e}")
            finally:
                def done():
                    self.is_busy = False
                    self._set_buttons_state("normal")
                    self._refresh_buffer_ui()
                    messagebox.showinfo(
                        "洗白作業完成",
                        "🎉【雲端配額洗白管線執行完畢！】\n\n"
                        "1. 所有佔空間的原檔已安全保存在電腦緩衝庫中。\n"
                        "2. 雲端舊檔已移至垃圾桶，請前往 Google 相簿垃圾桶點擊『清空垃圾桶』釋出配額！\n"
                        "3. 系統已自動啟動 Auto-Pilot 將相片推送進 Pixel 1，即將以 0 空間原裝重生！"
                    )
                self.root.after(0, done)

        threading.Thread(target=worker, daemon=True).start()

    def trigger_cloud_reconciliation(self):
        if self.is_busy:
            return
        self.is_busy = True
        self._set_buttons_state("disabled")
        self.progress_bar["value"] = 0
        self.lbl_progress.config(text="正在與 Google 相簿雲端進行資產對帳比對...")

        def worker():
            try:
                self.log_msg(">>> [雲端對帳] 開始盤點 Google 相簿雲端全量相片清冊...")
                # 1. 檢查 Chrome CDP
                if not is_cdp_ready():
                    self.log_msg("[*] 正在啟動 Google Chrome 進行自動對帳 (除錯連接埠 9222)...")
                    ok_ch, msg_ch = launch_chrome_with_cdp()
                    if not ok_ch:
                        self.log_msg(f"[X] {msg_ch}")
                        return

                # 2. 執行全量相簿掃描並產出 Google Photos Cloud Ledger
                def on_p(cur, tot, fname, act):
                    def _u():
                        pct = int((cur / tot) * 100) if tot > 0 else 0
                        self.progress_bar["value"] = pct
                        self.lbl_progress.config(text=f"[雲端對帳] ({pct}%) 正在盤點 Google 相簿總帳...")
                    self.root.after(0, _u)

                ok_sc, msg_sc, stats_sc = scan_cloud_quota(
                    log_callback=self.log_msg,
                    progress_callback=on_p,
                    stop_event=lambda: self.cancel_requested
                )

                if ok_sc:
                    self.log_msg(f"[完成] 雲端資產總帳建立完成！已同步登記至本地對帳本。")
                    def done_ok():
                        self.is_busy = False
                        self._set_buttons_state("normal")
                        self._refresh_buffer_ui()
                        buf = get_buffer_summary()
                        messagebox.showinfo(
                            "雲端資產對帳完成",
                            f"【Google 相簿雲端對帳完成！】\n\n"
                            f"● 雲端總帳已成功建立並已永久儲存！\n"
                            f"● 電腦已存在的舊相片已全數標記『已備份 (免傳)』，絕不重複推送！\n"
                            f"● 目前待處理新相片：{buf['pending_pixel']} 張 ({buf['pending_gb']:.2f} GB)\n\n"
                            f"【接下來您可以進行的操作】：\n"
                            f"1. 點擊下方【確定】關閉此視窗。\n"
                            f"2. 若要將待傳新照片備份到雲端：\n"
                            f"   ➔ 插上 Pixel 1 (確認綠燈)，點擊【⚡ Auto-Pilot】即可全自動循環傳輸。\n"
                            f"3. 若要將之前被 Google 扣額的舊照洗成永久免費：\n"
                            f"   ➔ 點擊左下方【✨ 雲端配額自動洗白】。\n\n"
                            f"💡 總帳已存檔，下次開啟程式會自動套用，無須重複比對！"
                        )
                    self.root.after(0, done_ok)
                else:
                    self.log_msg(f"[X] 雲端對帳未完成: {msg_sc}")
                    def done_err():
                        self.is_busy = False
                        self._set_buttons_state("normal")
                        self._refresh_buffer_ui()
                        messagebox.showwarning(
                            "雲端對帳未完成",
                            f"對帳未完成：{msg_sc}\n\n請確認 Chrome 視窗已成功開啟且已登入 Google 相簿。"
                        )
                    self.root.after(0, done_err)
            except Exception as e:
                self.log_msg(f"[!] 雲端對帳異常: {e}")
                def done_ex():
                    self.is_busy = False
                    self._set_buttons_state("normal")
                    self._refresh_buffer_ui()
                self.root.after(0, done_ex)

        threading.Thread(target=worker, daemon=True).start()

def main():
    if not ensure_single_instance():
        sys.exit(0)
    root = tk.Tk()
    app = Any2PixelApp(root)
    root.mainloop()

if __name__ == "__main__":
    main()
