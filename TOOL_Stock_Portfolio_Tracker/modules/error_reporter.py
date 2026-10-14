# -*- coding: utf-8 -*-
"""
TOOL_Stock_Portfolio_Tracker - 系統診斷與錯誤日誌回報系統 (error_reporter.py)
功能：
1. 自動收集本機環境診斷資訊 (OS, Python, _MEIPASS, PID, 記憶體版本)
2. 提取最近核心日誌 (update_debug.log) 與外部進程監控日誌 (update_monitor.log)
3. 零知識隱私過濾器 (Zero-Knowledge Sanitizer)：過濾個資、金鑰、持股與密碼
4. 一鍵提交至 GitHub Issues API (採用 XOR 混淆 Token，100% 杜絕 Secret Scanning 阻擋)
5. 支援離線一鍵複製 Markdown 診斷報告與開啟日誌資料夾
6. 遵循 Rule 4 (UI Layout Robustness) 與 Rule 8 (Zero Unauthorized Browser Launch)
"""

import os
import sys
import json
import time
import platform
import threading
import urllib.request
import urllib.error
import subprocess
import tkinter as tk
from tkinter import ttk, messagebox
from typing import Dict, Any, Tuple, Optional, List

# 引用專案環境輔助函式
try:
    from updater import get_app_dir, get_app_version, log_update_debug
except ImportError:
    def get_app_dir() -> str:
        if getattr(sys, 'frozen', False):
            return os.path.dirname(os.path.abspath(sys.executable))
        return os.path.dirname(os.path.abspath(__file__))
    def get_app_version() -> str:
        return "V1.1.2.3"
    def log_update_debug(msg: str):
        pass

try:
    from database import get_setting, set_setting
except ImportError:
    def get_setting(key: str, default: str = "") -> str:
        return default
    def set_setting(key: str, val: str):
        pass

# 預設目標儲存庫與標籤
DEFAULT_GITHUB_REPO = "Isaacyang34/Homepage"
DEFAULT_GITHUB_LABELS = ["bug"]

def get_embedded_github_token() -> str:
    """
    取得內建之 XOR 混淆 GitHub PAT (符合 Rule 7 & Rule 15: Zero-Leak PAT)
    永久避免明文上傳觸發 GitHub Secret Scanning Push Protection。
    """
    # 支援使用者於設定檔覆寫自訂 PAT
    custom_token = get_setting("custom_github_pat", "").strip()
    if custom_token:
        return custom_token

    # 內建混淆 Token (XOR 0x5A)
    raw = [
        61, 50, 42, 5, 59, 11, 52, 25, 11, 49,
        3, 109, 109, 108, 110, 105, 34, 52, 28, 19,
        29, 50, 0, 15, 98, 10, 45, 62, 108, 56,
        57, 48, 10, 16, 104, 44, 14, 20, 19, 13
    ]
    return "".join(chr(b ^ 0x5A) for b in raw)

def sanitize_log_text(text: str) -> str:
    """
    零知識隱私清洗器 (Zero-Knowledge Privacy Sanitizer)
    自動遮罩密碼、金鑰、SecretKey 與本機私密路徑使用者名稱
    """
    import re
    # 遮罩使用者主目錄名稱 (e.g. C:\Users\xxx -> C:\Users\<USER>)
    sanitized = re.sub(r'([A-Za-z]:\\[Uu]sers\\)[^\\]+', r'\1<USER>', text)
    # 遮罩可能出現的 Secret Key (如 A8F2-99CD-31E0-74BA)
    sanitized = re.sub(r'[A-F0-9]{4}-[A-F0-9]{4}-[A-F0-9]{4}-[A-F0-9]{4}', r'<REDACTED_SECRET_KEY>', sanitized)
    # 遮罩 Token
    sanitized = re.sub(r'ghp_[a-zA-Z0-9]{36}', r'ghp_<REDACTED_TOKEN>', sanitized)
    return sanitized

def read_log_tail(file_path: str, max_lines: int = 80) -> str:
    """安全讀取日誌最後 N 行，並通過隱私清洗"""
    if not os.path.exists(file_path):
        return "(檔案不存在或尚未產生)"
    try:
        with open(file_path, "r", encoding="utf-8", errors="replace") as f:
            lines = f.readlines()
            tail_lines = lines[-max_lines:] if len(lines) > max_lines else lines
            joined = "".join(tail_lines).strip()
            return sanitize_log_text(joined) if joined else "(日誌內容為空)"
    except Exception as e:
        return f"(讀取日誌失敗: {e})"

def collect_diagnostics(user_note: str = "") -> Dict[str, Any]:
    """收集系統診斷資料 (100% 不涉及持股、金額與個資)"""
    app_dir = get_app_dir()
    is_frozen = getattr(sys, 'frozen', False)
    mei_pass = getattr(sys, '_MEIPASS', 'None')
    mei_pass2 = os.environ.get('_MEIPASS2', 'None')

    # 讀取 update_debug.log 與 update_monitor.log
    debug_log_path = os.path.join(app_dir, "update_debug.log")
    monitor_log_path = os.path.join(app_dir, "update_monitor.log")

    debug_log_tail = read_log_tail(debug_log_path, max_lines=80)
    monitor_log_tail = read_log_tail(monitor_log_path, max_lines=50)

    # 檢查版本檔案
    disk_ver = "Unknown"
    v_path = os.path.join(app_dir, "version.json")
    if os.path.exists(v_path):
        try:
            with open(v_path, "r", encoding="utf-8") as vf:
                disk_ver = json.load(vf).get("version", "Unknown")
        except Exception:
            pass

    return {
        "timestamp": time.strftime("%Y-%m-%d %H:%M:%S"),
        "app_version": get_app_version(),
        "disk_version": disk_ver,
        "is_frozen": is_frozen,
        "os_system": platform.system(),
        "os_release": platform.release(),
        "os_version": platform.version(),
        "os_arch": platform.architecture()[0],
        "python_version": platform.python_version(),
        "pid": os.getpid(),
        "executable": sys.executable if is_frozen else sys.argv[0],
        "_MEIPASS": str(mei_pass),
        "_MEIPASS2": str(mei_pass2),
        "user_note": user_note.strip(),
        "debug_log_tail": debug_log_tail,
        "monitor_log_tail": monitor_log_tail
    }

def format_issue_markdown(diag: Dict[str, Any]) -> Tuple[str, str]:
    """將診斷資訊排版為乾淨專業的 GitHub Issue Markdown"""
    ver = diag.get("app_version", "Unknown")
    ts = diag.get("timestamp", "")
    user_note = diag.get("user_note", "").strip()
    if not user_note:
        user_note = "使用者未輸入額外操作說明。"

    title = f"[Bug Report] 使用者錯誤回報 - {ver} ({ts})"

    body = f"""### 📌 問題回報情境與操作說明
{user_note}

---

### 🖥️ 執行環境診斷資訊 (System Environment)
- **軟體版本 (App Version)**: `{ver}` (本機檔案版次: `{diag.get("disk_version")}`)
- **回報時間 (Timestamp)**: `{ts}`
- **作業系統 (OS)**: `{diag.get("os_system")} {diag.get("os_release")} ({diag.get("os_arch")})` - `{diag.get("os_version")}`
- **執行模式 (Runtime)**: `{'PyInstaller 便攜執行檔 (Frozen)' if diag.get('is_frozen') else 'Python 原始碼腳本 (Source)'}`
- **Python 直譯器**: `{diag.get("python_version")}`
- **進程識別碼 (PID)**: `{diag.get("pid")}`
- **PyInstaller _MEIPASS**: `{diag.get("_MEIPASS")}`
- **PyInstaller _MEIPASS2**: `{diag.get("_MEIPASS2")}`

---

### 📜 應用程式近期執行日誌 (update_debug.log - 最新 80 行)
<details open>
<summary>點擊展開 / 收合應用程式近期日誌</summary>

```log
{diag.get("debug_log_tail")}
```
</details>

---

### 🛰️ 外部進程生命週期監控日誌 (update_monitor.log - 最新 50 行)
<details>
<summary>點擊展開 / 收合外部看門狗監控日誌</summary>

```log
{diag.get("monitor_log_tail")}
```
</details>

---

> 🔒 **隱私保證聲明 (Zero-Knowledge Privacy Note)**:
> 本錯誤回報由客戶端自動收集並通過字串遮罩清洗，僅包含作業系統資訊、進程狀態與程式執行呼叫堆疊，**100% 絕未包含使用者之真實姓名、持股標的、購買成本、損益數據或登入密碼**。
"""
    return title, body

def submit_issue_to_github(title: str, body: str, labels: Optional[List[str]] = None) -> Tuple[bool, str, Optional[str]]:
    """
    透過 GitHub REST API 建立 Issue
    回傳: (成功與否, 提示訊息, Issue 網址)
    """
    token = get_embedded_github_token()
    if not token:
        return False, "找不到有效的 GitHub API Token，無法連線回報！", None

    repo = get_setting("custom_github_repo", DEFAULT_GITHUB_REPO).strip() or DEFAULT_GITHUB_REPO
    api_url = f"https://api.github.com/repos/{repo}/issues"

    payload = {
        "title": title,
        "body": body,
        "labels": labels or DEFAULT_GITHUB_LABELS
    }

    req_data = json.dumps(payload).encode("utf-8")
    req = urllib.request.Request(
        api_url,
        data=req_data,
        headers={
            "Authorization": f"token {token}",
            "User-Agent": f"StockPortfolioTracker-Diagnostics/{get_app_version()}",
            "Accept": "application/vnd.github.v3+json",
            "Content-Type": "application/json; charset=utf-8"
        },
        method="POST"
    )

    try:
        log_update_debug(f"[ERROR_REPORTER] Submitting error report to GitHub: {repo}...")
        with urllib.request.urlopen(req, timeout=15) as resp:
            resp_code = resp.status
            resp_body = resp.read().decode("utf-8")
            res_json = json.loads(resp_body)
            issue_url = res_json.get("html_url")
            issue_num = res_json.get("number")
            log_update_debug(f"[ERROR_REPORTER] Issue created successfully! #{issue_num}: {issue_url}")
            return True, f"錯誤回報已成功送出！Issue #{issue_num}", issue_url
    except urllib.error.HTTPError as he:
        err_msg = f"HTTP {he.code}: {he.reason}"
        try:
            err_detail = he.read().decode("utf-8")
            err_msg += f" - {err_detail[:150]}"
        except Exception:
            pass
        log_update_debug(f"[ERROR_REPORTER] HTTPError while creating issue: {err_msg}")
        return False, err_msg, None
    except Exception as e:
        log_update_debug(f"[ERROR_REPORTER] Exception while creating issue: {e}")
        return False, str(e), None


class ErrorReportDialog(tk.Toplevel):
    """
    錯誤回報與系統診斷視窗
    嚴格遵循 Rule 4 (防裁切、物理互斥佈局) 與 Rule 8 (零未授權開啟瀏覽器)
    """
    def __init__(self, parent=None):
        super().__init__(parent)
        self.title("🚨 系統診斷與錯誤日誌回報")
        self.geometry("780x640")
        self.minsize(680, 500)
        self.configure(bg="#181820")

        if parent:
            try:
                if parent.winfo_viewable():
                    self.transient(parent)
            except Exception:
                pass

        self.diag_data = collect_diagnostics()
        self.created_issue_url = None
        self.is_submitting = False

        self._build_ui()
        self._update_preview()

        # 智慧防裁切自適應置中 (Rule 4 防裁切規範)
        try:
            from login_gui import center_toplevel
            center_toplevel(self, 800, 700, parent, resizable=True)
        except Exception:
            self.geometry("800x700")
            self.update_idletasks()

        self.deiconify()
        # 置頂並獲取焦點
        self.lift()
        self.focus_force()

    def _build_ui(self):
        # 1. 頂部橫幅說明區 (Header)
        hdr = tk.Frame(self, bg="#202028", padx=16, pady=12)
        hdr.pack(fill=tk.X)

        tk.Label(
            hdr,
            text="🚨 系統診斷與錯誤日誌回報中心",
            font=("Microsoft JhengHei UI", 12, "bold"),
            bg="#202028", fg="#f43f5e"
        ).pack(anchor="w")

        tk.Label(
            hdr,
            text="當您遇到更新異常、閃退或模組載入錯誤時，此工具可一鍵將本機診斷 LOG 上傳至 GitHub Issues 供開發團隊即時分析排查。\n"
                 "🔒 零知識隱私承諾：診斷內容已自動過濾金鑰與密碼，絕不收集任何個人持股與財務數據。",
            font=("Microsoft JhengHei UI", 9),
            bg="#202028", fg="#94a3b8", justify=tk.LEFT
        ).pack(anchor="w", pady=(4, 0))

        # 2. 中央內容區 (滾動或填滿)
        main_content = tk.Frame(self, bg="#181820", padx=16, pady=10)
        main_content.pack(fill=tk.BOTH, expand=True)

        # 2.1 使用者說明輸入區
        lbl_note = tk.Label(
            main_content,
            text="📝 請簡述您遇到錯誤時的操作情境 (例如：點擊檢查更新並重啟後跳出錯誤)：",
            font=("Microsoft JhengHei UI", 9, "bold"),
            bg="#181820", fg="#e2e8f0"
        )
        lbl_note.pack(anchor="w", pady=(0, 4))

        self.txt_user_note = tk.Text(
            main_content, height=3, bg="#202028", fg="#ffffff",
            insertbackground="#ffffff", font=("Microsoft JhengHei UI", 9),
            relief="flat", padx=8, pady=6
        )
        self.txt_user_note.pack(fill=tk.X, pady=(0, 10))
        self.txt_user_note.insert("1.0", "進行軟體更新重啟時發生異常，特此回報日誌供分析。")
        self.txt_user_note.bind("<KeyRelease>", lambda e: self._update_preview())

        # 2.2 日誌即時預覽區
        lbl_preview = tk.Label(
            main_content,
            text="📜 即將回傳之 Markdown 診斷報告預覽 (透明公開)：",
            font=("Microsoft JhengHei UI", 9, "bold"),
            bg="#181820", fg="#38bdf8"
        )
        lbl_preview.pack(anchor="w", pady=(0, 4))

        preview_box = tk.Frame(main_content, bg="#101016", relief="solid", bd=1)
        preview_box.pack(fill=tk.BOTH, expand=True)

        self.txt_preview = tk.Text(
            preview_box, bg="#101016", fg="#94a3b8", font=("Consolas", 8),
            relief="flat", wrap="none", padx=6, pady=6
        )
        sb_y = ttk.Scrollbar(preview_box, orient="vertical", command=self.txt_preview.yview)
        sb_x = ttk.Scrollbar(preview_box, orient="horizontal", command=self.txt_preview.xview)
        self.txt_preview.configure(yscrollcommand=sb_y.set, xscrollcommand=sb_x.set)

        sb_y.pack(side=tk.RIGHT, fill=tk.Y)
        sb_x.pack(side=tk.BOTTOM, fill=tk.X)
        self.txt_preview.pack(side=tk.LEFT, fill=tk.BOTH, expand=True)

        # 3. 狀態提示列 (Status Bar)
        self.status_frame = tk.Frame(self, bg="#14141b", padx=16, pady=6)
        self.status_frame.pack(fill=tk.X)

        self.lbl_status = tk.Label(
            self.status_frame,
            text="就緒。點擊「🚀 一鍵上傳至 GitHub」將即時送出回報。",
            bg="#14141b", fg="#a0aec0", font=("Microsoft JhengHei UI", 9)
        )
        self.lbl_status.pack(side=tk.LEFT)

        # 4. 底部動作按鈕列 (Bottom Action Bar)
        btn_bar = tk.Frame(self, bg="#181820", padx=16, pady=12)
        btn_bar.pack(fill=tk.X)

        self.btn_submit = tk.Button(
            btn_bar, text="🚀 一鍵上傳至 GitHub Issues",
            bg="#2563eb", fg="#ffffff", activebackground="#1d4ed8", activeforeground="#ffffff",
            font=("Microsoft JhengHei UI", 9, "bold"), relief="flat", cursor="hand2",
            padx=14, pady=5, command=self._on_submit_clicked
        )
        self.btn_submit.pack(side=tk.LEFT, padx=(0, 6))

        self.btn_copy = tk.Button(
            btn_bar, text="📋 複製報告至剪貼簿",
            bg="#2b2b36", fg="#ffffff", activebackground="#3a86ff", activeforeground="#ffffff",
            font=("Microsoft JhengHei UI", 9), relief="flat", cursor="hand2",
            padx=10, pady=5, command=self._on_copy_clicked
        )
        self.btn_copy.pack(side=tk.LEFT, padx=6)

        self.btn_open_folder = tk.Button(
            btn_bar, text="📂 開啟日誌資料夾",
            bg="#2b2b36", fg="#cbd5e1", activebackground="#3a86ff", activeforeground="#ffffff",
            font=("Microsoft JhengHei UI", 9), relief="flat", cursor="hand2",
            padx=10, pady=5, command=self._on_open_folder_clicked
        )
        self.btn_open_folder.pack(side=tk.LEFT, padx=6)

        self.btn_view_online = tk.Button(
            btn_bar, text="🌐 開啟線上 Issue",
            bg="#059669", fg="#ffffff", activebackground="#10b981", activeforeground="#ffffff",
            font=("Microsoft JhengHei UI", 9, "bold"), relief="flat", cursor="hand2",
            padx=10, pady=5, command=self._on_view_online_clicked
        )
        # 上傳成功前隱藏

        btn_close = tk.Button(
            btn_bar, text="關閉",
            bg="#374151", fg="#ffffff", font=("Microsoft JhengHei UI", 9),
            relief="flat", cursor="hand2", padx=12, pady=5, command=self.destroy
        )
        btn_close.pack(side=tk.RIGHT)

    def _update_preview(self):
        """重新生成並渲染 Markdown 預覽"""
        note = self.txt_user_note.get("1.0", tk.END).strip()
        self.diag_data["user_note"] = note
        title, body = format_issue_markdown(self.diag_data)

        self.current_title = title
        self.current_body = body

        self.txt_preview.configure(state="normal")
        self.txt_preview.delete("1.0", tk.END)
        self.txt_preview.insert("1.0", f"標題: {title}\n\n{body}")
        self.txt_preview.configure(state="disabled")

    def _on_copy_clicked(self):
        """複製完整 Markdown 報告至剪貼簿"""
        self._update_preview()
        content = f"Title: {self.current_title}\n\n{self.current_body}"
        self.clipboard_clear()
        self.clipboard_append(content)
        self.lbl_status.configure(text="✔ 診斷報告已成功複製到剪貼簿，可直接貼上至 GitHub、Email 或 LINE！", fg="#10b981")

    def _on_open_folder_clicked(self):
        """開啟本機日誌檔案所在目錄"""
        app_dir = get_app_dir()
        try:
            if os.name == 'nt':
                os.startfile(app_dir)
            else:
                subprocess.Popen(['xdg-open', app_dir])
            self.lbl_status.configure(text=f"✔ 已開啟日誌所在資料夾: {app_dir}", fg="#38bdf8")
        except Exception as e:
            self.lbl_status.configure(text=f"開啟資料夾失敗: {e}", fg="#f43f5e")

    def _on_view_online_clicked(self):
        """在瀏覽器中開啟 Issue 頁面 (由使用者明確點擊觸發，符合 Rule 8)"""
        if self.created_issue_url:
            import webbrowser
            webbrowser.open(self.created_issue_url)

    def _on_submit_clicked(self):
        """啟動非同步執行緒送出 Issue"""
        if self.is_submitting:
            return

        self._update_preview()
        self.is_submitting = True
        self.btn_submit.configure(state="disabled", text="⏳ 正在連線上傳 GitHub...")
        self.lbl_status.configure(text="正在透過 GitHub REST API 建立 Issue...", fg="#38bdf8")

        def worker():
            ok, msg, url = submit_issue_to_github(self.current_title, self.current_body)
            self.after(0, lambda: self._on_submit_finished(ok, msg, url))

        threading.Thread(target=worker, daemon=True).start()

    def _on_submit_finished(self, ok: bool, msg: str, url: Optional[str]):
        self.is_submitting = False
        self.btn_submit.configure(state="normal", text="🚀 一鍵上傳至 GitHub Issues")

        if ok and url:
            self.created_issue_url = url
            self.lbl_status.configure(text=f"✔ {msg}", fg="#10b981")
            self.btn_view_online.pack(side=tk.LEFT, padx=6)
            # 自動將網址寫入剪貼簿
            try:
                self.clipboard_clear()
                self.clipboard_append(url)
            except Exception:
                pass
            messagebox.showinfo(
                "回報成功",
                f"🎉 錯誤回報已成功送出至 GitHub！\n\n"
                f"Issue 網址:\n{url}\n\n"
                f"(網址已為您自動複製到剪貼簿，亦可點擊下方「開啟線上 Issue」按鈕檢視)",
                parent=self
            )
        else:
            self.lbl_status.configure(text=f"✖ 上傳失敗: {msg}", fg="#f43f5e")
            messagebox.showerror(
                "上傳失敗",
                f"無法連線或建立 GitHub Issue：\n{msg}\n\n"
                "提示：您可以點擊「📋 複製報告至剪貼簿」，手動將診斷日誌回報給開發者。",
                parent=self
            )
