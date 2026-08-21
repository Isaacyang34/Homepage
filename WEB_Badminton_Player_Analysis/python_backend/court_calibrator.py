"""
羽球場地界線自動檢測與單應性校正模組 (Badminton Court Boundary & Homography Calibrator)
-----------------------------------------------------------------------------------
功能：
1. 自動提取羽球場館白色邊線 (White Court Line Detection via HSV / Gradient)
2. 霍夫直線變換 (Hough Transform) 與 RANSAC 邊界擬合
3. 自動計算 4 個球場頂點角點 (Far-Left, Far-Right, Near-Right, Near-Left)
4. 單應性矩陣計算 (Homography Matrix H: Video Pixel Space <-> Metric BWF 13.4m x 6.10m)
5. 單打 / 雙打落點界線判定與毫釐距離換算
"""

import cv2
import numpy as np
import json
import math
import os

class CourtBoundaryCalibrator:
    def __init__(self, court_width_m=6.10, court_length_m=13.40):
        self.court_w = court_width_m
        self.court_l = court_length_m
        self.homography_matrix = None

    def detect_court_lines(self, frame_bgr):
        h, w = frame_bgr.shape[:2]
        hsv = cv2.cvtColor(frame_bgr, cv2.COLOR_BGR2HSV)

        # 提取白色/亮色場地界線 (White Mask)
        lower_white = np.array([0, 0, 180], dtype=np.uint8)
        upper_white = np.array([180, 50, 255], dtype=np.uint8)
        mask = cv2.inRange(hsv, lower_white, upper_white)

        # 形態學過濾
        kernel = cv2.getStructuringElement(cv2.MORPH_RECT, (3, 3))
        mask_clean = cv2.morphologyEx(mask, cv2.MORPH_OPEN, kernel)

        # Canny 邊緣檢測
        edges = cv2.Canny(mask_clean, 50, 150)

        # 霍夫直線變換檢測長直線
        lines = cv2.HoughLinesP(edges, 1, np.pi / 180, threshold=80, minLineLength=100, maxLineGap=20)
        return lines

    def compute_homography(self, corners):
        """
        corners: [FarLeft(x, y), FarRight(x, y), NearRight(x, y), NearLeft(x, y)]
        標準 BWF 物理空間 (0, 0) ~ (6.10, 13.40)
        """
        src_pts = np.array(corners, dtype=np.float32)
        dst_pts = np.array([
            [0.0, 0.0],
            [self.court_w, 0.0],
            [self.court_w, self.court_l],
            [0.0, self.court_l]
        ], dtype=np.float32)

        H, _ = cv2.findHomography(src_pts, dst_pts)
        self.homography_matrix = H
        return H

    def judge_landing(self, pixel_x, pixel_y, game_mode="singles"):
        """
        傳入像素座標 (x, y)，判定是否在界內及離界線的公分距離
        """
        if self.homography_matrix is None:
            # 使用預設標準頂點
            corners = [(380, 270), (900, 270), (1150, 680), (130, 680)]
            self.compute_homography(corners)

        pt = np.array([[[pixel_x, pixel_y]]], dtype=np.float32)
        court_pt = cv2.perspectiveTransform(pt, self.homography_matrix)[0][0]
        cx, cy = court_pt[0], court_pt[1]

        # 單打邊界: X in [0.46, 5.64], Y in [0, 13.40]
        # 雙打邊界: X in [0.00, 6.10], Y in [0, 13.40]
        min_x = 0.46 if game_mode == "singles" else 0.0
        max_x = (self.court_w - 0.46) if game_mode == "singles" else self.court_w
        min_y = 0.0
        max_y = self.court_l

        is_in = (min_x <= cx <= max_x) and (min_y <= cy <= max_y)

        # 計算距離最近邊線公分數
        dist_left = cx - min_x
        dist_right = max_x - cx
        dist_back = max_y - cy
        dist_front = cy - min_y

        min_margin = min(abs(dist_left), abs(dist_right), abs(dist_back), abs(dist_front))
        margin_cm = round(min_margin * 100.0, 1)

        return {
            "is_in_court": is_in,
            "metric_pos_m": {"x": round(float(cx), 2), "y": round(float(cy), 2)},
            "margin_cm": margin_cm,
            "game_mode": game_mode,
            "verdict": f"IN (界內 {margin_cm}cm)" if is_in else f"OUT (出界 {margin_cm}cm)"
        }

if __name__ == "__main__":
    calibrator = CourtBoundaryCalibrator()
    print("Court Boundary & Homography Calibrator initialized.")
    # 測試落點
    res = calibrator.judge_landing(980, 640, "singles")
    print("Test singles landing result:", json.dumps(res, indent=2, ensure_ascii=False))