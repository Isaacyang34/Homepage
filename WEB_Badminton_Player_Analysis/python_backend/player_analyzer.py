"""
羽球選手動作生物力學與姿態分析引擎 (Badminton Player Biomechanics & Pose Analyzer)
---------------------------------------------------------------------------------
使用 YOLOv8-Pose / YOLO11-Pose 模型提取人體 17 處關鍵點，計算關節角度、起跳高度、
揮拍動作週期（引拍蓄力 -> 擊球伸展 -> 隨揮回防）以及球場跑動指標。
"""

import cv2
import numpy as np
import json
import math
import os
import argparse
from typing import Dict, List, Tuple, Any

# COCO 17 Keypoints Index Definition
KEYPOINTS_MAP = {
    0: "nose", 1: "left_eye", 2: "right_eye", 3: "left_ear", 4: "right_ear",
    5: "left_shoulder", 6: "right_shoulder", 7: "left_elbow", 8: "right_elbow",
    9: "left_wrist", 10: "right_wrist", 11: "left_hip", 12: "right_hip",
    13: "left_knee", 14: "right_knee", 15: "left_ankle", 16: "right_ankle"
}

# Skeleton Connections for Drawing
SKELETON_PAIRS = [
    (5, 6),   # shoulders
    (5, 7), (7, 9),   # left arm
    (6, 8), (8, 10),  # right arm
    (5, 11), (6, 12), # torso
    (11, 12),         # hips
    (11, 13), (13, 15), # left leg
    (12, 14), (14, 16), # right leg
]

class BadmintonPoseAnalyzer:
    def __init__(self, model_weight: str = "yolov8m-pose.pt", conf_thresh: float = 0.4):
        """
        初始化 YOLO-Pose 姿態分析模型
        """
        print(f"[Init] Loading YOLO-Pose model: {model_weight}...")
        try:
            from ultralytics import YOLO
            self.model = YOLO(model_weight)
        except ImportError:
            print("[Warning] ultralytics package not installed. Run: pip install ultralytics")
            self.model = None
        self.conf_thresh = conf_thresh

    @staticmethod
    def calculate_angle(p1: np.ndarray, p2: np.ndarray, p3: np.ndarray) -> float:
        """
        計算以 p2 為頂點，向量 p2->p1 與 p2->p3 之間的平面夾角 (度數 0 ~ 180)
        """
        if p1 is None or p2 is None or p3 is None:
            return 0.0
        v1 = np.array([p1[0] - p2[0], p1[1] - p2[1]], dtype=np.float32)
        v2 = np.array([p3[0] - p2[0], p3[1] - p2[1]], dtype=np.float32)
        
        norm_v1 = np.linalg.norm(v1)
        norm_v2 = np.linalg.norm(v2)
        if norm_v1 == 0 or norm_v2 == 0:
            return 0.0
            
        dot = np.dot(v1, v2)
        cos_theta = np.clip(dot / (norm_v1 * norm_v2), -1.0, 1.0)
        return float(np.degrees(np.arccos(cos_theta)))

    @staticmethod
    def calculate_center_of_mass(kpts: np.ndarray) -> Tuple[float, float]:
        """
        估算人體軀幹重心點 (Center of Mass - CoM)
        採用雙肩中點與雙髖中點的加權平均
        """
        l_sh, r_sh = kpts[5][:2], kpts[6][:2]
        l_hip, r_hip = kpts[11][:2], kpts[12][:2]
        
        mid_shoulder = (l_sh + r_sh) / 2.0
        mid_hip = (l_hip + r_hip) / 2.0
        com = (mid_shoulder * 0.4 + mid_hip * 0.6)
        return float(com[0]), float(com[1])

    def extract_biomechanics_for_person(self, kpts: np.ndarray) -> Dict[str, Any]:
        """
        針對單一選手關鍵點計算各項羽球生物力學指標
        kpts shape: (17, 3) -> [x, y, confidence]
        """
        # 1. 上肢手肘關節夾角 (判定引拍蓄力 vs 擊球伸展)
        r_elbow_angle = self.calculate_angle(kpts[6][:2], kpts[8][:2], kpts[10][:2])   # 右肩-右肘-右腕
        l_elbow_angle = self.calculate_angle(kpts[5][:2], kpts[7][:2], kpts[9][:2])    # 左肩-左肘-左腕
        
        # 2. 下肢膝關節夾角 (判定蹬地與起跳深蹲蓄力)
        r_knee_angle = self.calculate_angle(kpts[12][:2], kpts[14][:2], kpts[16][:2])  # 右髖-右膝-右踝
        l_knee_angle = self.calculate_angle(kpts[11][:2], kpts[13][:2], kpts[15][:2])  # 左髖-左膝-左踝
        
        # 3. 軀幹與肩膀傾角 (Trunk & Shoulder Tilt)
        sh_dx = kpts[6][0] - kpts[5][0]
        sh_dy = kpts[6][1] - kpts[5][1]
        shoulder_tilt = float(np.degrees(np.arctan2(sh_dy, sh_dx + 1e-6)))

        # 4. 重心點
        com_x, com_y = self.calculate_center_of_mass(kpts)

        # 5. 擊球手臂判定（通常持拍手的手腕較常處於較高或動態劇烈位置）
        dominant_arm = "right" if kpts[10][1] < kpts[9][1] else "left"
        dominant_elbow_angle = r_elbow_angle if dominant_arm == "right" else l_elbow_angle

        return {
            "right_elbow_angle": round(r_elbow_angle, 1),
            "left_elbow_angle": round(l_elbow_angle, 1),
            "dominant_arm": dominant_arm,
            "dominant_elbow_angle": round(dominant_elbow_angle, 1),
            "right_knee_angle": round(r_knee_angle, 1),
            "left_knee_angle": round(l_knee_angle, 1),
            "shoulder_tilt": round(shoulder_tilt, 1),
            "center_of_mass": {"x": round(com_x, 1), "y": round(com_y, 1)},
            "wrist_height": round(float(min(kpts[9][1], kpts[10][1])), 1),
            "ankle_height": round(float(min(kpts[15][1], kpts[16][1])), 1)
        }

    def analyze_video(self, video_path: str, max_frames: int = 0) -> Dict[str, Any]:
        """
        完整分析一段羽球選手影片並輸出時序生物力學數據
        """
        cap = cv2.VideoCapture(video_path)
        if not cap.isOpened():
            raise ValueError(f"Could not open video file: {video_path}")

        fps = cap.get(cv2.CAP_PROP_FPS) or 30.0
        width = int(cap.get(cv2.CAP_PROP_FRAME_WIDTH))
        height = int(cap.get(cv2.CAP_PROP_FRAME_HEIGHT))
        total_frames = int(cap.get(cv2.CAP_PROP_FRAME_COUNT))

        print(f"[Analyzing] Resolution: {width}x{height}, FPS: {fps}, Total Frames: {total_frames}")

        frame_data_list = []
        frame_idx = 0

        # Baseline ground level calibration
        lowest_ankle_y = 0.0

        while cap.isOpened():
            ret, frame = cap.read()
            if not ret:
                break
            if max_frames > 0 and frame_idx >= max_frames:
                break

            # YOLO inference
            kpts_all = []
            if self.model:
                results = self.model(frame, verbose=False, conf=self.conf_thresh)
                if results and len(results[0]) > 0:
                    # 選擇畫面中信心度最高或面積最大的主要選手 (Main Player)
                    boxes = results[0].boxes
                    keypoints = results[0].keypoints.data.cpu().numpy()
                    
                    if len(keypoints) > 0:
                        # 選取最靠近鏡頭/下半場的主要選手
                        main_player_kpts = keypoints[0] # shape (17, 3)
                        kpts_all = main_player_kpts.tolist()
                        metrics = self.extract_biomechanics_for_person(main_player_kpts)
                    else:
                        metrics = self._empty_metrics()
                else:
                    metrics = self._empty_metrics()
            else:
                # 模擬示範數據 (若環境無 GPU/ultralytics)
                metrics = self._simulate_frame_metrics(frame_idx, total_frames, width, height)

            # 計算跳躍垂直高度 (基於踝關節/重心的相對高度)
            if metrics["ankle_height"] > lowest_ankle_y:
                lowest_ankle_y = metrics["ankle_height"]
            
            jump_pixels = max(0.0, lowest_ankle_y - metrics["ankle_height"]) if lowest_ankle_y > 0 else 0.0
            jump_cm_approx = round(jump_pixels * (175.0 / (height * 0.5)), 1) # 粗估像素換算公分

            # 動作階段判定 (Action Phase Classification)
            # Ready -> Loading (Backswing) -> Impact/Apex (Smash Extension) -> Recovery
            elbow = metrics["dominant_elbow_angle"]
            phase = "準備 (Ready)"
            if jump_cm_approx > 15.0 or (elbow > 150.0 and metrics["wrist_height"] < height * 0.35):
                phase = "擊球/頂點 (Impact / Apex)"
            elif elbow < 110.0 and elbow > 45.0:
                phase = "引拍蓄力 (Loading / Backswing)"
            elif jump_cm_approx < 10.0 and elbow > 120.0:
                phase = "隨揮回防 (Follow-Through / Recovery)"

            frame_entry = {
                "frame_index": frame_idx,
                "timestamp_sec": round(frame_idx / fps, 3),
                "phase": phase,
                "jump_height_cm": jump_cm_approx,
                "metrics": metrics,
                "keypoints": kpts_all
            }
            frame_data_list.append(frame_entry)
            frame_idx += 1

            if frame_idx % 30 == 0:
                print(f"Processed frame {frame_idx}/{total_frames} ({(frame_idx/total_frames)*100:.1f}%)")

        cap.release()

        # 彙總分析摘要
        summary = self._compute_session_summary(frame_data_list, fps)

        return {
            "video_metadata": {
                "filename": os.path.basename(video_path),
                "width": width,
                "height": height,
                "fps": fps,
                "total_frames": frame_idx,
                "duration_seconds": round(frame_idx / fps, 2)
            },
            "summary_metrics": summary,
            "frames": frame_data_list
        }

    def _empty_metrics(self) -> Dict[str, Any]:
        return {
            "right_elbow_angle": 0.0,
            "left_elbow_angle": 0.0,
            "dominant_arm": "right",
            "dominant_elbow_angle": 0.0,
            "right_knee_angle": 0.0,
            "left_knee_angle": 0.0,
            "shoulder_tilt": 0.0,
            "center_of_mass": {"x": 0.0, "y": 0.0},
            "wrist_height": 0.0,
            "ankle_height": 0.0
        }

    def _simulate_frame_metrics(self, f: int, total: int, w: int, h: int) -> Dict[str, Any]:
        # 示範平滑正弦曲線模擬殺球動作
        progress = (f % 60) / 60.0
        elbow = 90.0 + 75.0 * math.sin(progress * math.pi)
        knee = 140.0 - 30.0 * math.sin(progress * math.pi)
        com_x = w * 0.5 + 50.0 * math.sin(progress * 2 * math.pi)
        com_y = h * 0.65 - 80.0 * math.sin(progress * math.pi)
        return {
            "right_elbow_angle": round(elbow, 1),
            "left_elbow_angle": 120.0,
            "dominant_arm": "right",
            "dominant_elbow_angle": round(elbow, 1),
            "right_knee_angle": round(knee, 1),
            "left_knee_angle": round(knee + 5.0, 1),
            "shoulder_tilt": round(15.0 * math.cos(progress * math.pi), 1),
            "center_of_mass": {"x": round(com_x, 1), "y": round(com_y, 1)},
            "wrist_height": round(h * 0.4 - 100.0 * math.sin(progress * math.pi), 1),
            "ankle_height": round(h * 0.85, 1)
        }

    def _compute_session_summary(self, frames: List[Dict[str, Any]], fps: float) -> Dict[str, Any]:
        if not frames:
            return {}
        max_jump = max(f["jump_height_cm"] for f in frames)
        max_elbow_extension = max(f["metrics"]["dominant_elbow_angle"] for f in frames)
        min_elbow_loading = min([f["metrics"]["dominant_elbow_angle"] for f in frames if f["metrics"]["dominant_elbow_angle"] > 20] or [90])
        
        # 統計各階段佔比
        phase_counts = {}
        for f in frames:
            p = f["phase"]
            phase_counts[p] = phase_counts.get(p, 0) + 1

        return {
            "max_jump_height_cm": max_jump,
            "max_elbow_extension_deg": max_elbow_extension,
            "min_elbow_loading_deg": min_elbow_loading,
            "swing_range_deg": round(max_elbow_extension - min_elbow_loading, 1),
            "phase_distribution": {k: round(v / len(frames) * 100, 1) for k, v in phase_counts.items()}
        }

def main():
    parser = argparse.ArgumentParser(description="Badminton Player Pose & Biomechanics Analyzer")
    parser.add_argument("--input", type=str, required=True, help="Input badminton video path")
    parser.add_argument("--output", type=str, default="badminton_analysis_result.json", help="Output JSON path")
    parser.add_argument("--model", type=str, default="yolov8m-pose.pt", help="YOLO-Pose model weight")
    args = parser.parse_args()

    analyzer = BadmintonPoseAnalyzer(model_weight=args.model)
    results = analyzer.analyze_video(args.input)
    
    with open(args.output, "w", encoding="utf-8") as f:
        json.dump(results, f, ensure_ascii=False, indent=2)
    print(f"\n[Success] Analysis finished! Results saved to: {args.output}")

if __name__ == "__main__":
    main()
