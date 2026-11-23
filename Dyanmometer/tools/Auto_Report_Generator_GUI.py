# -*- coding: utf-8 -*-
"""
Dynamometer Motor Report & Specification One-Click Auto-Generator GUI
Compatible with Windows XP (Python 2.7 / 3.4+) and Windows 10/11
"""
import sys
import os
import glob
import threading
from datetime import datetime

try:
    # Python 3
    import tkinter as tk
    from tkinter import ttk, filedialog, messagebox
    from tkinter.scrolledtext import ScrolledText
except ImportError:
    # Python 2
    import Tkinter as tk
    import ttk
    import tkFileDialog as filedialog
    import tkMessageBox as messagebox
    from ScrolledText import ScrolledText

# Import engine
curr_dir = os.path.dirname(os.path.abspath(__file__))
if curr_dir not in sys.path:
    sys.path.insert(0, curr_dir)
from Auto_Report_Engine import extract_folder_telemetry, generate_report_and_spec, resolve_report_workspace

class AutoReportGeneratorGUI(tk.Tk):
    def __init__(self):
        super(AutoReportGeneratorGUI, self).__init__()
        self.title("動力計電機測試報告與規格書一鍵生成器 (Dynamometer Report Generator)")
        self.geometry("780x560")
        self.minsize(680, 480)
        
        # Configure fonts
        self.font_title = ("Segoe UI", 12, "bold") if sys.platform.startswith("win") else ("Arial", 12, "bold")
        self.font_normal = ("Segoe UI", 9) if sys.platform.startswith("win") else ("Arial", 9)
        self.font_bold = ("Segoe UI", 9, "bold") if sys.platform.startswith("win") else ("Arial", 9, "bold")
        self.font_mono = ("Consolas", 9) if sys.platform.startswith("win") else ("Courier New", 9)
        
        # Default paths
        self.default_workspace = resolve_report_workspace()
        self.init_ui()

    def init_ui(self):
        # Top banner
        banner_frame = tk.Frame(self, bg="#1E3A8A", height=50)
        banner_frame.pack(fill=tk.X, side=tk.TOP)
        banner_label = tk.Label(
            banner_frame,
            text="動力計測試日誌自動解析與報表生成系統",
            font=self.font_title,
            fg="white",
            bg="#1E3A8A",
            pady=10
        )
        banner_label.pack(side=tk.LEFT, padx=15)

        # Main container
        main_frame = tk.Frame(self, padx=15, pady=10)
        main_frame.pack(fill=tk.BOTH, expand=True)

        # 1. Folder Selection Group
        grp_folder = tk.LabelFrame(main_frame, text=" [1] 選擇電機測試數據目錄 ", font=self.font_bold, padx=10, pady=10)
        grp_folder.pack(fill=tk.X, pady=5)

        lbl_folder = tk.Label(grp_folder, text="測試數據資料夾路徑:", font=self.font_normal)
        lbl_folder.grid(row=0, column=0, sticky=tk.W, pady=2)

        self.var_folder = tk.StringVar()
        default_target = os.path.join(self.default_workspace, "SIMW132M-15-08")
        if os.path.exists(default_target):
            self.var_folder.set(default_target)
        else:
            self.var_folder.set(self.default_workspace)

        self.ent_folder = tk.Entry(grp_folder, textvariable=self.var_folder, font=self.font_normal, width=58)
        self.ent_folder.grid(row=0, column=1, padx=8, sticky=tk.EW)

        btn_browse = tk.Button(grp_folder, text="瀏覽...", width=10, command=self.on_browse_folder, font=self.font_normal)
        btn_browse.grid(row=0, column=2, padx=2)

        # Output dir
        lbl_out = tk.Label(grp_folder, text="報表輸出目標目錄:", font=self.font_normal)
        lbl_out.grid(row=1, column=0, sticky=tk.W, pady=5)

        self.var_out = tk.StringVar(value=self.default_workspace)
        self.ent_out = tk.Entry(grp_folder, textvariable=self.var_out, font=self.font_normal, width=58)
        self.ent_out.grid(row=1, column=1, padx=8, sticky=tk.EW)

        btn_browse_out = tk.Button(grp_folder, text="瀏覽...", width=10, command=self.on_browse_output, font=self.font_normal)
        btn_browse_out.grid(row=1, column=2, padx=2)

        grp_folder.grid_columnconfigure(1, weight=1)

        # 2. Options Group
        grp_opt = tk.LabelFrame(main_frame, text=" [2] 生成選項與基準設定 ", font=self.font_bold, padx=10, pady=8)
        grp_opt.pack(fill=tk.X, pady=5)

        tk.Label(grp_opt, text="報告產生日期 (YYYYMMDD):", font=self.font_normal).grid(row=0, column=0, sticky=tk.W)
        self.var_date = tk.StringVar(value=datetime.now().strftime("%Y%m%d"))
        tk.Entry(grp_opt, textvariable=self.var_date, width=14, font=self.font_normal).grid(row=0, column=1, sticky=tk.W, padx=5)

        self.var_compat = tk.BooleanVar(value=True)
        chk_compat = tk.Checkbutton(grp_opt, text="自動新增相容 Excel 2010 之 [自動審查_修復版] 分頁", variable=self.var_compat, font=self.font_normal)
        chk_compat.grid(row=0, column=2, padx=20, sticky=tk.W)

        # 3. Action Button
        btn_frame = tk.Frame(main_frame, pady=5)
        btn_frame.pack(fill=tk.X)

        self.btn_run = tk.Button(
            btn_frame,
            text="[>>] 一鍵解析數據並生成規格書與測試報告",
            font=self.font_bold,
            bg="#0D9488",
            fg="white",
            activebackground="#0F766E",
            activeforeground="white",
            height=2,
            command=self.on_run_generate
        )
        self.btn_run.pack(fill=tk.X)

        # 4. Progress Log Display
        grp_log = tk.LabelFrame(main_frame, text=" [3] 執行進度與日誌輸出 ", font=self.font_bold, padx=10, pady=8)
        grp_log.pack(fill=tk.BOTH, expand=True, pady=5)

        self.txt_log = ScrolledText(grp_log, font=self.font_mono, bg="#1E1E1E", fg="#D4D4D4", height=10)
        self.txt_log.pack(fill=tk.BOTH, expand=True)

        self.log_msg("系統就緒。請指定欲解析之電機測試資料夾，然後點擊 [一鍵解析數據並生成規格書與測試報告]。")

    def log_msg(self, msg):
        self.txt_log.insert(tk.END, msg + "\n")
        self.txt_log.see(tk.END)
        self.update_idletasks()

    def on_browse_folder(self):
        folder = filedialog.askdirectory(initialdir=self.default_workspace, title="選擇電機測試數據目錄")
        if folder:
            self.var_folder.set(os.path.normpath(folder))

    def on_browse_output(self):
        folder = filedialog.askdirectory(initialdir=self.default_workspace, title="選擇報表輸出目標目錄")
        if folder:
            self.var_out.set(os.path.normpath(folder))

    def on_run_generate(self):
        folder = self.var_folder.get().strip()
        out_dir = self.var_out.get().strip()
        date_str = self.var_date.get().strip()

        if not os.path.exists(folder):
            messagebox.showerror("路徑錯誤", "指定的測試數據資料夾不存在！\n" + folder)
            return

        self.btn_run.config(state=tk.DISABLED, text="[..] 正在解析數據並生成報告中，請稍候...")

        def worker():
            try:
                self.log_msg("\n" + "="*70)
                self.log_msg(f"開始解析目錄: {folder}")
                self.log_msg(f"日期標籤: {date_str} | 輸出目錄: {out_dir}")
                
                # Step 1: Telemetry Extraction
                telemetry = extract_folder_telemetry(folder)
                self.log_msg(f"  [√] 成功識別電機型號: {telemetry['motor_model']}")
                self.log_msg(f"  [√] 額定電壓={telemetry['rated_voltage']}V, 額定轉速={telemetry['rated_speed']}rpm, 額定功率={telemetry['rated_power']}kW")
                if telemetry['equiv']:
                    self.log_msg(f"  [√] 提取等效電路: R1={telemetry['equiv'].get('R1')}Ω, X1={telemetry['equiv'].get('X1')}Ω, Xm={telemetry['equiv'].get('Xm')}Ω")
                if telemetry['s1']:
                    self.log_msg(f"  [√] 提取 S1 額定: 轉速={telemetry['s1'].get('rpm')}rpm, 轉矩={telemetry['s1'].get('torque')}Nm, 功率={telemetry['s1'].get('p_elec')}kW")
                if telemetry['s2']:
                    self.log_msg(f"  [√] 提取 S2 過載: 轉速={telemetry['s2'].get('rpm')}rpm, 轉矩={telemetry['s2'].get('torque')}Nm, 功率={telemetry['s2'].get('p_elec')}kW")
                if telemetry['s6']:
                    self.log_msg(f"  [√] 提取 S6 週期: 轉速={telemetry['s6'].get('rpm')}rpm, 轉矩={telemetry['s6'].get('torque')}Nm, 最高溫={telemetry['s6'].get('t_coil')}°C")

                # Step 2: Generation
                spec_path, rep_path = generate_report_and_spec(telemetry, out_dir, date_str=date_str)
                self.log_msg("\n" + "*"*70)
                self.log_msg(f"[SUCCESS] 規格書已生成: {os.path.basename(spec_path)}")
                self.log_msg(f"[SUCCESS] 測試報告已生成: {os.path.basename(rep_path)}")
                self.log_msg("*"*70)
                messagebox.showinfo("生成成功", f"電機報表已順利生成！\n\n規格書:\n{spec_path}\n\n測試報告:\n{rep_path}")
            except Exception as e:
                self.log_msg(f"[ERROR] 生成失敗: {str(e)}")
                messagebox.showerror("生成失敗", f"發生異常錯誤:\n{str(e)}")
            finally:
                self.btn_run.config(state=tk.NORMAL, text="[>>] 一鍵解析數據並生成規格書與測試報告")

        t = threading.Thread(target=worker)
        t.daemon = True
        t.start()

if __name__ == '__main__':
    app = AutoReportGeneratorGUI()
    app.mainloop()
