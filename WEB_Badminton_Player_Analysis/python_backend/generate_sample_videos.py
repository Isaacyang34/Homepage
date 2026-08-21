"""
生成高清真實羽球比賽預覽影片 (Generate HD Sample Badminton Match Videos)
----------------------------------------------------------------------
建立符合真實比例、球員跑動、揮拍動作與羽球飛行的高畫質 MP4 影片，
供系統預設載入與即時重合測試使用。
"""

import cv2
import numpy as np
import os
import math

def create_badminton_match_video(
    output_path: str,
    player_type: str = "smash", # "smash" (安賽龍) or "trickshot" (戴資穎)
    width: int = 1280,
    height: int = 720,
    fps: int = 30,
    duration_sec: int = 4
):
    os.makedirs(os.path.dirname(os.path.abspath(output_path)), exist_ok=True)
    total_frames = fps * duration_sec
    fourcc = cv2.VideoWriter_fourcc(*'mp4v')
    out = cv2.VideoWriter(output_path, fourcc, fps, (width, height))

    print(f"[Generating Video] {output_path} ({width}x{height}, {fps}fps, {total_frames} frames)...")

    base_x = width // 2
    ground_y = int(height * 0.82)

    for f in range(total_frames):
        # 1. 建立真實羽球館深色環境
        frame = np.zeros((height, width, 3), dtype=np.uint8)
        
        # 背景漸層 (Dark Stadium Arena)
        for y in range(height):
            ratio = y / height
            b = int(18 + ratio * 15)
            g = int(22 + ratio * 18)
            r = int(12 + ratio * 12)
            frame[y, :] = [b, g, r]

        # 2. 繪製 3D 透視標準羽球場地 (Green Court Mat)
        court_poly = np.array([
            [int(width * 0.22), int(height * 0.42)],
            [int(width * 0.78), int(height * 0.42)],
            [int(width * 0.92), int(height * 0.95)],
            [int(width * 0.08), int(height * 0.95)]
        ], np.int32)
        cv2.fillPoly(frame, [court_poly], (35, 75, 25)) # 綠色地墊
        cv2.polylines(frame, [court_poly], True, (255, 255, 255), 3) # 白邊線

        # 繪製球網 (Net)
        net_y = int(height * 0.52)
        cv2.line(frame, (int(width * 0.19), net_y), (int(width * 0.81), net_y), (60, 60, 220), 4) # 紅色網帶

        # 繪製球場發球線與中線
        cv2.line(frame, (int(width * 0.16), int(height * 0.62)), (int(width * 0.84), int(height * 0.62)), (200, 200, 200), 2)
        cv2.line(frame, (width // 2, int(height * 0.62)), (width // 2, int(height * 0.95)), (200, 200, 200), 2)

        # 3. 運動員動作姿態時序軌跡
        p = (f % total_frames) / float(total_frames)

        if player_type == "smash":
            # 安賽龍跳殺動作 (Jump Smash)
            jump_h = math.sin(p * math.pi) * 95.0 if (0.45 <= p <= 0.75) else 0.0
            px = base_x + int(math.sin(p * math.pi * 2) * 20.0)
            py = ground_y - int(jump_h)
            arm_deg = 80.0 + (p / 0.6) * 95.0 if p <= 0.6 else 175.0 - (p - 0.6) * 80.0
            jersey_color = (220, 120, 40) # 丹麥紅橘球衣
            player_name = "Viktor AXELSEN (390 km/h Smash)"
        else:
            # 戴資穎網前滑拍假動作 (Tai Tzu Ying Trickshot)
            jump_h = math.sin(p * math.pi) * 25.0 if (0.4 <= p <= 0.65) else 0.0
            px = base_x + int(math.cos(p * math.pi) * 70.0)
            py = ground_y - 20 - int(jump_h)
            arm_deg = 110.0 + math.sin(p * math.pi * 2) * 55.0
            jersey_color = (40, 160, 220) # 中華隊藍球衣
            player_name = "TAI Tzu Ying (Deception Trickshot)"

        # 繪製地面陰影
        shadow_w = max(20, int(60 - jump_h * 0.4))
        shadow_h = max(8, int(18 - jump_h * 0.15))
        cv2.ellipse(frame, (px, ground_y + 10), (shadow_w, shadow_h), 0, 0, 360, (15, 30, 15), -1)

        # 4. 繪製運動員身體輪廓 (Athlete Body Silhouette)
        # 雙腿 (Legs & Shoes)
        knee_bend = math.sin(p * math.pi) * 30.0
        cv2.line(frame, (px - 16, py - 90), (px - 22, py - 40 + int(knee_bend)), (180, 195, 225), 14) # 左大腿
        cv2.line(frame, (px - 22, py - 40 + int(knee_bend)), (px - 20, py), (180, 195, 225), 12) # 左小腿
        cv2.circle(frame, (px - 20, py), 8, (255, 255, 255), -1) # 左鞋

        cv2.line(frame, (px + 16, py - 90), (px + 22, py - 40 + int(knee_bend)), (180, 195, 225), 14) # 右大腿
        cv2.line(frame, (px + 22, py - 40 + int(knee_bend)), (px + 20, py), (180, 195, 225), 12) # 右小腿
        cv2.circle(frame, (px + 20, py), 8, (255, 255, 255), -1) # 右鞋

        # 軀幹球衣 (Torso Jersey)
        torso_pts = np.array([
            [px - 32, py - 170],
            [px + 32, py - 170],
            [px + 22, py - 90],
            [px - 22, py - 90]
        ], np.int32)
        cv2.fillPoly(frame, [torso_pts], jersey_color)
        cv2.polylines(frame, [torso_pts], True, (255, 255, 255), 2)

        # 頭部與五官
        cv2.circle(frame, (px, py - 205), 22, (180, 195, 225), -1)
        cv2.circle(frame, (px, py - 212), 22, (40, 40, 40), 5) # 頭髮

        # 手臂與球拍
        rad = math.radians(arm_deg)
        r_elbow_x = px + int(math.cos(rad - 1.2) * 55)
        r_elbow_y = (py - 165) + int(math.sin(rad - 1.2) * 55)
        r_wrist_x = r_elbow_x + int(math.cos(rad) * 50)
        r_wrist_y = r_elbow_y + int(math.sin(rad) * 50)

        # 揮拍手臂
        cv2.line(frame, (px + 28, py - 165), (r_elbow_x, r_elbow_y), (180, 195, 225), 12)
        cv2.line(frame, (r_elbow_x, r_elbow_y), (r_wrist_x, r_wrist_y), (180, 195, 225), 10)

        # 球拍 (Badminton Racket)
        racket_angle = math.atan2(r_wrist_y - r_elbow_y, r_wrist_x - r_elbow_x)
        shaft_end_x = r_wrist_x + int(math.cos(racket_angle) * 60)
        shaft_end_y = r_wrist_y + int(math.sin(racket_angle) * 60)
        head_cx = shaft_end_x + int(math.cos(racket_angle) * 35)
        head_cy = shaft_end_y + int(math.sin(racket_angle) * 35)

        cv2.line(frame, (r_wrist_x, r_wrist_y), (shaft_end_x, shaft_end_y), (220, 220, 220), 3) # 拍桿
        cv2.ellipse(frame, (head_cx, head_cy), (32, 22), int(math.degrees(racket_angle)), 0, 360, (40, 40, 240), 3) # 拍框

        # 擊球羽球與飛行殘影
        if 0.5 <= p <= 0.8:
            shuttle_x = head_cx + int((p - 0.5) * 500.0)
            shuttle_y = head_cy + int((p - 0.5) * 300.0)
            cv2.circle(frame, (shuttle_x, shuttle_y), 6, (255, 255, 255), -1)
            cv2.line(frame, (shuttle_x, shuttle_y), (shuttle_x - 40, shuttle_y - 25), (200, 200, 200), 2)

        # 轉播資訊標籤 (Broadcast Overlay HUD)
        cv2.rectangle(frame, (40, 40), (460, 95), (10, 15, 25), -1)
        cv2.rectangle(frame, (40, 40), (460, 95), (0, 245, 155), 2)
        cv2.putText(frame, "BWF WORLD TOUR LIVE", (55, 62), cv2.FONT_HERSHEY_SIMPLEX, 0.55, (0, 245, 155), 2)
        cv2.putText(frame, player_name, (55, 85), cv2.FONT_HERSHEY_SIMPLEX, 0.65, (255, 255, 255), 2)

        out.write(frame)

    out.release()
    print(f"[Done] Created video: {output_path}")

if __name__ == "__main__":
    base_dir = os.path.dirname(os.path.abspath(__file__))
    static_vids = os.path.join(base_dir, "static_videos")
    
    # 產生戴資穎與安賽龍兩段高清示範影片
    create_badminton_match_video(
        output_path=os.path.join(static_vids, "tai_tzu_ying_trickshot.mp4"),
        player_type="trickshot"
    )
    create_badminton_match_video(
        output_path=os.path.join(static_vids, "axelsen_jump_smash.mp4"),
        player_type="smash"
    )
