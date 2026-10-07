import os
import sys
import math
import random
import time
import threading
import tkinter as tk
from typing import List, Optional

try:
    from PIL import Image, ImageTk, ImageEnhance
    HAS_PIL = True
except ImportError:
    HAS_PIL = False

def get_resource_path(relative_path: str) -> str:
    """取得靜態資源路徑，相容開發環境與 PyInstaller 打包 (_MEIPASS)"""
    if hasattr(sys, '_MEIPASS'):
        base_path = sys._MEIPASS
    else:
        base_path = os.path.dirname(os.path.abspath(__file__))
    return os.path.join(base_path, relative_path)

class FallingCoin:
    """從天而降的 3D 翻轉金幣粒子"""
    def __init__(self, canvas_w: int, canvas_h: int, start_y_random: bool = True):
        self.canvas_w = canvas_w
        self.canvas_h = canvas_h
        self.reset(start_y_random)

    def reset(self, start_y_random: bool = False):
        self.x = random.uniform(15, self.canvas_w - 15)
        if start_y_random:
            self.y = random.uniform(-120, self.canvas_h - 20)
        else:
            self.y = random.uniform(-90, -15)
        
        # 尺寸與深淺 (模擬立體景深)
        self.size_tier = random.choice([1, 2, 2, 3])
        if self.size_tier == 1:       # 遠景小金幣
            self.radius = random.uniform(7, 10)
            self.vy = random.uniform(3.0, 5.0)
            self.symbol = ""
        elif self.size_tier == 2:     # 中景金幣
            self.radius = random.uniform(11, 14)
            self.vy = random.uniform(5.0, 7.5)
            self.symbol = "$"
        else:                         # 近景大金幣
            self.radius = random.uniform(15, 19)
            self.vy = random.uniform(7.5, 10.0)
            self.symbol = "$" if random.random() < 0.65 else "NT$"

        self.vx = random.uniform(-0.7, 0.7)
        self.angle = random.uniform(0, 360)
        self.v_rot = random.uniform(8, 16) * (1 if random.random() > 0.5 else -1)
        self.wobble = random.uniform(0, math.pi * 2)

    def update(self):
        self.y += self.vy
        self.wobble += 0.06
        self.x += self.vx + math.sin(self.wobble) * 0.5
        self.angle = (self.angle + self.v_rot) % 360

        # 超出底部時重置至頂部
        if self.y - self.radius > self.canvas_h:
            self.reset(start_y_random=False)

    def draw(self, canvas: tk.Canvas):
        # 3D 翻轉寬度比例 (cos 週期伸縮)
        rad = math.radians(self.angle)
        scale_x = abs(math.cos(rad))
        w = max(2.5, self.radius * scale_x)
        h = self.radius

        x0, y0 = self.x - w, self.y - h
        x1, y1 = self.x + w, self.y + h

        if scale_x < 0.22:
            # 翻轉側面：立體金幣暗金色厚邊
            canvas.create_oval(x0, y0, x1, y1, fill="#d97706", outline="#78350f", width=1)
        else:
            # 正面與斜角面：深金底陰影 + 明亮金黃高光
            canvas.create_oval(x0 + 1.2, y0 + 1.2, x1 + 1.2, y1 + 1.2, fill="#92400e", outline="", width=0)
            canvas.create_oval(x0, y0, x1, y1, fill="#f59e0b", outline="#fef08a", width=1.4)
            
            # 內圈浮雕裝飾線
            if w > 6 and h > 6:
                in_w = max(1.5, (w - 2.8))
                in_h = h - 2.8
                canvas.create_oval(self.x - in_w, self.y - in_h, self.x + in_w, self.y + in_h, outline="#fbbf24", width=1)

            # 金融文字 (正面展開時顯現)
            if w > 8.5 and self.symbol:
                font_sz = max(6, int(h * 0.72))
                canvas.create_text(self.x, self.y, text=self.symbol, fill="#78350f", font=("Arial", font_sz, "bold"))


class SplashScreen:
    """專屬啟動動畫視窗 (ICON滿版背景 + 錢幣從天而降動畫)"""
    def __init__(self):
        self.width = 540
        self.height = 380

        self.root = tk.Tk()
        self.root.title("Stock Portfolio Tracker")
        self.root.overrideredirect(True) # 無邊框
        self.root.attributes("-topmost", True) # 最上層

        # 螢幕居中
        sw = self.root.winfo_screenwidth()
        sh = self.root.winfo_screenheight()
        x = max(0, (sw - self.width) // 2)
        y = max(0, (sh - self.height) // 2)
        self.root.geometry(f"{self.width}x{self.height}+{x}+{y}")
        self.root.configure(bg="#12131a")

        # 畫布
        self.canvas = tk.Canvas(self.root, width=self.width, height=self.height, bg="#12131a", highlightthickness=0, bd=0)
        self.canvas.pack(fill=tk.BOTH, expand=True)

        self.bg_photo = None
        self.load_background()

        # 生成 38 枚不同大小與下落速度的金幣
        self.coins: List[FallingCoin] = [FallingCoin(self.width, self.height, start_y_random=True) for _ in range(38)]

        self.progress_val = 0.0
        self.target_progress = 0.15
        self.status_text = "✦ 系統正在啟動，載入模組..."
        self.is_running = True
        self.start_time = time.time()
        self.min_duration = 2.4 # 保持至少 2.4 秒完整體驗金幣雨
        self.preload_done = False

        # 在背景非同步預熱重度模組與資料庫
        threading.Thread(target=self._background_preload, daemon=True).start()

        # 開始動畫
        self.animate()

    def load_background(self):
        """讀取 app_icon.png 作為背景圖並加上深色沉浸遮罩"""
        icon_path = get_resource_path("app_icon.png")
        if HAS_PIL and os.path.exists(icon_path):
            try:
                img = Image.open(icon_path).convert("RGBA")
                
                # 等比縮放滿版
                target_ratio = self.width / self.height
                img_ratio = img.width / img.height
                if img_ratio > target_ratio:
                    new_h = self.height
                    new_w = int(new_h * img_ratio)
                else:
                    new_w = self.width
                    new_h = int(new_w / img_ratio)

                resized = img.resize((new_w, new_h), Image.Resampling.LANCZOS)
                
                # 居中裁切為 540x380
                left = (new_w - self.width) // 2
                top = (new_h - self.height) // 2
                cropped = resized.crop((left, top, left + self.width, top + self.height))

                # 微調亮度與對比度 (適度壓暗讓金幣與文字璀璨可見)
                enhancer = ImageEnhance.Brightness(cropped)
                darkened = enhancer.enhance(0.42)

                # 覆蓋一層深藍/黑半透明遮罩
                overlay = Image.new("RGBA", (self.width, self.height), (10, 12, 22, 125))
                blended = Image.alpha_composite(darkened, overlay)

                self.bg_photo = ImageTk.PhotoImage(blended)
                self.canvas.create_image(0, 0, image=self.bg_photo, anchor="nw", tags="bg")
                return
            except Exception as e:
                print(f"[Splash] 載入 ICON 背景圖失敗: {e}")

        # 若載入異常，使用深邃夜空黑底
        self.canvas.create_rectangle(0, 0, self.width, self.height, fill="#12131a", width=0, tags="bg")

    def animate(self):
        """流暢動畫影格更新 (金幣雨物理下墜 + 進度平滑過渡)"""
        if not self.is_running:
            return

        self.canvas.delete("coin")
        self.canvas.delete("ui")

        # 1. 繪製所有從天而降金幣粒子
        for coin in self.coins:
            coin.update()
            coin.draw(self.canvas)

        # 2. 視窗邊框與裝飾
        self.canvas.create_rectangle(0, 0, self.width, self.height, outline="#3b82f6", width=1.5, tags="ui")

        # 標題與 LOGO 橫幅
        self.canvas.create_text(
            self.width // 2, 70,
            text="STOCK PORTFOLIO TRACKER",
            fill="#ffffff", font=("Segoe UI", 16, "bold"), tags="ui"
        )
        self.canvas.create_text(
            self.width // 2, 96,
            text="台美股庫存損益、歷史走勢與股息追蹤系統",
            fill="#cbd5e1", font=("Microsoft JhengHei UI", 10), tags="ui"
        )

        # 3. 底部動態載入提示與平滑進度條
        self.progress_val += (self.target_progress - self.progress_val) * 0.14
        p_pct = min(1.0, max(0.0, self.progress_val))

        # 狀態文字
        self.canvas.create_text(
            self.width // 2, 288,
            text=self.status_text,
            fill="#38bdf8", font=("Microsoft JhengHei UI", 9, "bold"), tags="ui"
        )

        # 進度條槽
        bar_w = 380
        bar_h = 7
        bx0 = (self.width - bar_w) // 2
        by0 = 310
        bx1 = bx0 + bar_w
        by1 = by0 + bar_h

        self.canvas.create_rectangle(bx0, by0, bx1, by1, fill="#1e2235", outline="#2e344e", width=1, tags="ui")
        
        # 進度金色發光條
        cur_bar_w = bar_w * p_pct
        if cur_bar_w > 2:
            self.canvas.create_rectangle(bx0 + 1, by0 + 1, bx0 + cur_bar_w - 1, by1 - 1, fill="#f59e0b", outline="", width=0, tags="ui")
            self.canvas.create_rectangle(bx0 + max(0, cur_bar_w - 6), by0 + 1, bx0 + cur_bar_w - 1, by1 - 1, fill="#fef08a", outline="", width=0, tags="ui")

        # 底部離線標籤
        self.canvas.create_text(
            self.width // 2, 348,
            text="本地端純離線儲存  |  安全端到端加密同步",
            fill="#64748b", font=("Microsoft JhengHei UI", 8), tags="ui"
        )

        # 檢查是否準備完成
        elapsed = time.time() - self.start_time
        if self.preload_done and elapsed >= self.min_duration and p_pct >= 0.96:
            self.is_running = False
            self.root.destroy()
            return

        # 33ms (~30 FPS)
        self.root.after(33, self.animate)

    def _background_preload(self):
        """背景預熱載入模組與資料庫"""
        try:
            time.sleep(0.3)
            self.set_status("✦ 正在讀取本地庫存資料庫與歷史行情...", 0.40)
            from database import init_db
            init_db()

            time.sleep(0.3)
            self.set_status("✦ 初始化即時報價引擎與自訂外觀風格...", 0.75)
            import quote_service
            import dividend_service
            import history_service
            import main_gui

            self.set_status("✦ 載入完成，即刻啟動！", 1.0)
            self.preload_done = True
        except Exception as e:
            print(f"[Splash] 背景預載異常: {e}")
            self.set_status(f"載入就緒...", 1.0)
            self.preload_done = True

    def set_status(self, text: str, progress: float):
        self.status_text = text
        self.target_progress = progress


def show_splash_and_start_app():
    """以金幣雨啟動動畫開場，完成後流暢進入主程式"""
    # 1. 播放金幣雨動畫與背景模組預熱
    splash = SplashScreen()
    splash.root.mainloop()

    # 2. Splash 視窗乾淨銷毀後，啟動主視窗
    from main_gui import PortfolioApp
    app = PortfolioApp()
    app.lift()
    app.focus_force()
    app.mainloop()
