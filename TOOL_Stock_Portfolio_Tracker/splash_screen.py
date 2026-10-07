import os
import sys
import math
import random
import time
import threading
import tkinter as tk
from typing import List, Tuple, Optional

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


class BreakoutSpark:
    """突破阻力線時爆發的能量微光粒子"""
    def __init__(self, x: float, y: float):
        self.x = x
        self.y = y
        angle = random.uniform(0, math.pi * 2)
        speed = random.uniform(2.5, 7.0)
        self.vx = math.cos(angle) * speed
        self.vy = math.sin(angle) * speed - random.uniform(1.0, 3.0)  # 帶有向上衝力
        self.life = 1.0
        self.decay = random.uniform(0.04, 0.08)
        self.size = random.uniform(2.0, 4.5)
        self.color = random.choice(["#f43f5e", "#fbbf24", "#fef08a", "#38bdf8", "#ffffff"])

    def update(self):
        self.x += self.vx
        self.y += self.vy
        self.vy += 0.15  # 輕微重力
        self.life -= self.decay

    def draw(self, canvas: tk.Canvas):
        if self.life <= 0:
            return
        r = self.size * self.life
        canvas.create_oval(
            self.x - r, self.y - r, self.x + r, self.y + r,
            fill=self.color, outline="", width=0, tags="spark"
        )


class RisingSparks:
    """象徵強勁多頭動能的向上漂浮微粒子"""
    def __init__(self, canvas_w: int, canvas_h: int):
        self.canvas_w = canvas_w
        self.canvas_h = canvas_h
        self.reset(start_random=True)

    def reset(self, start_random: bool = False):
        self.x = random.uniform(30, self.canvas_w - 30)
        self.y = random.uniform(100, self.canvas_h) if start_random else random.uniform(self.canvas_h, self.canvas_h + 30)
        self.vy = random.uniform(1.2, 3.2)
        self.vx = random.uniform(-0.4, 0.4)
        self.radius = random.uniform(1.2, 2.8)
        self.alpha_seed = random.uniform(0, math.pi * 2)
        self.color = random.choice(["#ef4444", "#f59e0b", "#38bdf8", "#60a5fa"])

    def update(self):
        self.y -= self.vy
        self.x += self.vx
        self.alpha_seed += 0.08
        if self.y < 60:
            self.reset(start_random=False)

    def draw(self, canvas: tk.Canvas):
        pulse = 0.5 + 0.5 * math.sin(self.alpha_seed)
        r = self.radius * pulse
        if r > 0.6:
            canvas.create_oval(
                self.x - r, self.y - r, self.x + r, self.y + r,
                fill=self.color, outline="", width=0, tags="rising"
            )


class SplashScreen:
    """專屬啟動動畫視窗 (ICON 滿版背景 + 折線圖向上強勢突破動畫)"""
    def __init__(self):
        self.width = 540
        self.height = 380

        self.root = tk.Tk()
        self.root.title("Stock Portfolio Tracker")
        self.root.overrideredirect(True)      # 無邊框
        self.root.attributes("-topmost", True)  # 最上層

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

        # 生成走勢折線圖之關鍵節點 (45 個高精度座標點)
        # 歷程：底部震盪蓄勢 -> 均線糾結反彈 -> 穿透壓力位 (突破) -> 指數型加速飆升攻頂
        self.trend_points: List[Tuple[float, float]] = []
        self._generate_trendline_data()

        # 突破阻力位水平線 Y 座標 (約在整體中線偏上)
        self.resistance_y = 175.0
        self.breakout_idx = int(len(self.trend_points) * 0.60)
        self.breakout_coord = self.trend_points[self.breakout_idx]
        self.has_triggered_breakout_burst = False

        # 特效微粒子
        self.sparks: List[BreakoutSpark] = []
        self.rising_particles: List[RisingSparks] = [RisingSparks(self.width, self.height) for _ in range(22)]

        # 動畫進度控制
        self.chart_progress = 0.0      # 0.0 ~ 1.0 (折線由左至右畫出)
        self.progress_val = 0.0         # 底部載入條平滑進度
        self.target_progress = 0.15
        self.status_text = "✦ 系統正在啟動，載入行情引擎..."
        self.is_running = True
        self.start_time = time.time()
        self.min_duration = 2.4         # 保持至少 2.4 秒完整呈現突破向上氣勢
        self.preload_done = False
        self.anim_tick = 0

        # 背景非同步預熱模組與資料庫
        threading.Thread(target=self._background_preload, daemon=True).start()

        # 開始動畫迴圈
        self.animate()

    def _generate_trendline_data(self):
        """產生流暢逼真的多頭突破走勢點"""
        total_pts = 46
        x_start, x_end = 46, 494
        y_base = 240.0

        for i in range(total_pts):
            t = i / (total_pts - 1)
            x = x_start + t * (x_end - x_start)
            if t < 0.42:
                # 階段 1: 底部箱型震盪打底
                y = y_base - 14 * math.sin(t * 13) - t * 24
            elif t < 0.60:
                # 階段 2: 放量上攻逼近壓力位
                local_t = (t - 0.42) / 0.18
                y = (y_base - 24) - local_t * 41 + math.sin(local_t * 5) * 3.5
            else:
                # 階段 3: 帶量突破壓力線 (斜率急遽陡升，創新高)
                local_t = (t - 0.60) / 0.40
                y = 175.0 - (local_t ** 1.32) * 72.0 + math.sin(local_t * 3.5) * 2.5
            self.trend_points.append((x, y))

    def load_background(self):
        """讀取 app_icon.png 作為滿版背景圖並加上沉浸式深黑遮罩"""
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
                
                # 居中裁切
                left = (new_w - self.width) // 2
                top = (new_h - self.height) // 2
                cropped = resized.crop((left, top, left + self.width, top + self.height))

                # 微調亮度 (暗化讓前景發光折線圖與文字極致清晰)
                enhancer = ImageEnhance.Brightness(cropped)
                darkened = enhancer.enhance(0.38)

                # 覆蓋一層深色科技半透明網格基底
                overlay = Image.new("RGBA", (self.width, self.height), (12, 14, 24, 145))
                blended = Image.alpha_composite(darkened, overlay)

                self.bg_photo = ImageTk.PhotoImage(blended)
                self.canvas.create_image(0, 0, image=self.bg_photo, anchor="nw", tags="bg")
                return
            except Exception as e:
                print(f"[Splash] 載入 ICON 背景圖失敗: {e}")

        self.canvas.create_rectangle(0, 0, self.width, self.height, fill="#12131a", width=0, tags="bg")

    def animate(self):
        """動畫影格更新：金融坐標網格 + 動態折線由左向右繪製 + 向上突破特效"""
        if not self.is_running:
            return

        self.anim_tick += 1
        self.canvas.delete("chart")
        self.canvas.delete("spark")
        self.canvas.delete("rising")
        self.canvas.delete("ui")

        # 1. 繪製金融背景坐標網格 (Financial Chart Grid)
        self._draw_financial_grid()

        # 2. 向上動能光點粒子
        for rp in self.rising_particles:
            rp.update()
            rp.draw(self.canvas)

        # 3. 折線動態生長與進度計算
        # 配合預熱與時間進度平滑加速推進
        elapsed = time.time() - self.start_time
        time_ratio = min(1.0, elapsed / (self.min_duration * 0.92))
        target_chart_prog = max(time_ratio, self.target_progress)
        self.chart_progress += (target_chart_prog - self.chart_progress) * 0.16

        # 4. 繪製突破折線圖本體
        self._draw_trendline_chart()

        # 5. 更新並繪製突破時的爆發光暈粒子
        for spark in list(self.sparks):
            spark.update()
            spark.draw(self.canvas)
            if spark.life <= 0:
                self.sparks.remove(spark)

        # 6. 外框與標題 LOGO
        self.canvas.create_rectangle(0, 0, self.width, self.height, outline="#3b82f6", width=1.5, tags="ui")

        self.canvas.create_text(
            self.width // 2, 54,
            text="STOCK PORTFOLIO TRACKER",
            fill="#ffffff", font=("Segoe UI", 16, "bold"), tags="ui"
        )
        self.canvas.create_text(
            self.width // 2, 78,
            text="台美股庫存損益、歷史走勢與股息追蹤系統",
            fill="#94a3b8", font=("Microsoft JhengHei UI", 9), tags="ui"
        )

        # 7. 底部動態進度條與狀態文字
        self.progress_val += (self.target_progress - self.progress_val) * 0.14
        p_pct = min(1.0, max(0.0, self.progress_val))

        # 狀態文字提示
        self.canvas.create_text(
            self.width // 2, 292,
            text=self.status_text,
            fill="#38bdf8", font=("Microsoft JhengHei UI", 9, "bold"), tags="ui"
        )

        # 進度條
        bar_w = 400
        bar_h = 6
        bx0 = (self.width - bar_w) // 2
        by0 = 312
        bx1 = bx0 + bar_w
        by1 = by0 + bar_h

        self.canvas.create_rectangle(bx0, by0, bx1, by1, fill="#1a1c29", outline="#2b3147", width=1, tags="ui")
        cur_w = bar_w * p_pct
        if cur_w > 2:
            # 漸層科技發光進度條 (紅金強勢突破意象)
            self.canvas.create_rectangle(bx0 + 1, by0 + 1, bx0 + cur_w - 1, by1 - 1, fill="#ef4444", outline="", width=0, tags="ui")
            self.canvas.create_rectangle(bx0 + max(0, cur_w - 8), by0 + 1, bx0 + cur_w - 1, by1 - 1, fill="#fbbf24", outline="", width=0, tags="ui")

        # 底部標籤
        self.canvas.create_text(
            self.width // 2, 348,
            text="本地端純離線儲存  |  安全端到端加密同步",
            fill="#64748b", font=("Microsoft JhengHei UI", 8), tags="ui"
        )

        # 8. 完成檢驗 (折線已畫至頂峰且模組預載完成)
        if self.preload_done and elapsed >= self.min_duration and self.chart_progress >= 0.98 and p_pct >= 0.96:
            self.is_running = False
            self.root.destroy()
            return

        self.root.after(33, self.animate)

    def _draw_financial_grid(self):
        """繪製淡雅精緻的金融座標水平線與關鍵壓力位"""
        # 水平參考刻度線
        grid_ys = [240, 205, 175, 140, 105]
        for y in grid_ys:
            if y == 175:
                continue  # 175 是專屬壓力線
            self.canvas.create_line(45, y, 495, y, fill="#1c2132", width=1, dash=(3, 4), tags="chart")

        # 關鍵壓力阻力線 (Resistance Level)
        ry = self.resistance_y
        res_color = "#f43f5e" if self.has_triggered_breakout_burst else "#38bdf8"
        self.canvas.create_line(45, ry, 495, ry, fill=res_color, width=1.2, dash=(5, 3), tags="chart")

        # 壓力線標籤文字
        res_text = "🔥 關鍵阻力位突破 (BREAKOUT) ──" if self.has_triggered_breakout_burst else "── 關鍵壓力線 RESISTANCE ──"
        self.canvas.create_text(
            150, ry - 9,
            text=res_text,
            fill=res_color,
            font=("Segoe UI", 8, "bold"), tags="chart"
        )

    def _draw_trendline_chart(self):
        """根據當前進度繪製動態向上飆升折線與突破光標點"""
        total = len(self.trend_points)
        visible_count = max(2, int(total * min(1.0, self.chart_progress)))
        cur_pts = self.trend_points[:visible_count]

        # 1. 折線下方半透明淡色投影多邊形 (科技感量能底)
        poly_coords = [cur_pts[0][0], 255]
        for p in cur_pts:
            poly_coords.extend([p[0], p[1]])
        poly_coords.extend([cur_pts[-1][0], 255])
        
        # 繪製底層微光多邊形
        area_fill = "#2a1520" if visible_count >= self.breakout_idx else "#132338"
        self.canvas.create_polygon(*poly_coords, fill=area_fill, outline="", width=0, tags="chart")

        # 2. 折線繪製 (區分突破前與突破後之震撼視覺效果)
        pre_coords = []
        post_coords = []
        
        for i, (px, py) in enumerate(cur_pts):
            if i <= self.breakout_idx:
                pre_coords.extend([px, py])
            if i >= self.breakout_idx:
                post_coords.extend([px, py])

        # 突破前：科技藍走勢線
        if len(pre_coords) >= 4:
            self.canvas.create_line(*pre_coords, fill="#0284c7", width=3.5, smooth=True, tags="chart")
            self.canvas.create_line(*pre_coords, fill="#38bdf8", width=1.8, smooth=True, tags="chart")

        # 突破後：璀璨烈焰紅 + 金光飆升爆發線
        if len(post_coords) >= 4:
            # 外層熱能光暈
            self.canvas.create_line(*post_coords, fill="#881337", width=6.0, smooth=True, tags="chart")
            # 中層璀璨紅
            self.canvas.create_line(*post_coords, fill="#e11d48", width=3.5, smooth=True, tags="chart")
            # 核心亮金高光線
            self.canvas.create_line(*post_coords, fill="#fbbf24", width=1.6, smooth=True, tags="chart")

        # 3. 檢測是否剛觸發穿透壓力位爆發 (觸發一次性爆發微粒)
        if visible_count >= self.breakout_idx and not self.has_triggered_breakout_burst:
            self.has_triggered_breakout_burst = True
            bx, by = self.breakout_coord
            for _ in range(28):
                self.sparks.append(BreakoutSpark(bx, by))

        # 4. 突破節點的發光標記環
        if visible_count >= self.breakout_idx:
            bx, by = self.breakout_coord
            pulse_r = 5.0 + 2.5 * math.sin(self.anim_tick * 0.25)
            self.canvas.create_oval(bx - pulse_r, by - pulse_r, bx + pulse_r, by + pulse_r, outline="#fbbf24", width=1.5, tags="chart")
            self.canvas.create_oval(bx - 3, by - 3, bx + 3, by + 3, fill="#ffffff", outline="", width=0, tags="chart")

        # 5. 當前筆尖領先探針 (Leading Pulse Dot)
        lead_x, lead_y = cur_pts[-1]
        is_breakout_phase = (visible_count >= self.breakout_idx)
        core_color = "#fef08a" if is_breakout_phase else "#ffffff"
        halo_color = "#f43f5e" if is_breakout_phase else "#38bdf8"

        # 外圈呼吸光暈
        halo_r = 7.0 + 3.0 * math.sin(self.anim_tick * 0.3)
        self.canvas.create_oval(lead_x - halo_r, lead_y - halo_r, lead_x + halo_r, lead_y + halo_r, outline=halo_color, width=1.5, tags="chart")
        
        # 核心高光圓點
        self.canvas.create_oval(lead_x - 3.5, lead_y - 3.5, lead_x + 3.5, lead_y + 3.5, fill=core_color, outline="", width=0, tags="chart")

        # 6. 動態攀升數值浮動標籤 (即時計算突破漲幅)
        if visible_count > 6:
            # 根據垂直高度計算漲幅百分比
            rise_pct = max(0.0, (240.0 - lead_y) / 135.0 * 32.8)
            badge_text = f"+{rise_pct:.1f}% ▲"
            badge_color = "#f43f5e" if is_breakout_phase else "#38bdf8"
            
            # 浮動標籤背景氣泡
            tx = min(470, lead_x + 12)
            ty = max(95, lead_y - 12)
            self.canvas.create_rectangle(tx - 4, ty - 9, tx + 62, ty + 10, fill="#181824", outline=badge_color, width=1, tags="chart")
            self.canvas.create_text(tx + 29, ty, text=badge_text, fill="#ffffff", font=("Segoe UI", 9, "bold"), tags="chart")

    def _background_preload(self):
        """背景預熱載入模組與資料庫"""
        try:
            time.sleep(0.3)
            self.set_status("✦ 蓄勢整理：讀取本地資料庫與歷史行情...", 0.40)
            from database import init_db
            init_db()

            time.sleep(0.35)
            self.set_status("✦ 放量攻頂：初始化即時報價引擎與自訂外觀...", 0.75)
            import quote_service
            import dividend_service
            import history_service
            import main_gui

            time.sleep(0.2)
            self.set_status("✦ 突破創新高！系統就緒，即刻啟動...", 1.0)
            self.preload_done = True
        except Exception as e:
            print(f"[Splash] 背景預載異常: {e}")
            self.set_status("✦ 載入就緒...", 1.0)
            self.preload_done = True

    def set_status(self, text: str, progress: float):
        self.status_text = text
        self.target_progress = progress


def show_splash_and_start_app():
    """以折線圖向上強勢突破動畫開場，完成後流暢進入主程式"""
    # 1. 播放趨勢突破動畫與背景模組預熱
    splash = SplashScreen()
    splash.root.mainloop()

    # 2. Splash 視窗銷毀後，啟動主視窗
    from main_gui import PortfolioApp
    app = PortfolioApp()
    app.lift()
    app.focus_force()
    app.mainloop()


if __name__ == "__main__":
    # 獨立測試模式
    splash = SplashScreen()
    splash.root.mainloop()
