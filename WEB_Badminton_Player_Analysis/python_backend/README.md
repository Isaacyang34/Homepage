# 羽球選手動作分析 Python 後端引擎 (Badminton Pose Analytics Engine)

本後端模組採用 **YOLO-Pose（17 處人體關鍵點）** 實現羽球選手動作的生物力學運算。

## 📦 安裝依賴環境

請先確保已安裝 Python 3.8+，並執行以下指令安裝依賴庫：

```bash
cd python_backend
pip install -r requirements.txt
```

---

## 🚀 使用方式

### 方式一：單機離線分析影片 (CLI)
輸入羽球影片，自動輸出每格骨架座標、關節角度與動作階段分析 JSON：

```bash
python player_analyzer.py --input your_badminton_video.mp4 --output analysis_result.json
```

### 方式二：啟動線上 API 伺服器 (FastAPI)
提供前端 Web 儀表板直接上傳影片並即時回傳分析結果：

```bash
python api_server.py
```
* 伺服器將在 `http://localhost:8000` 啟動。
* API 文件瀏覽：`http://localhost:8000/docs`。

---

## 📊 分析指標輸出說明
輸出的 JSON 包含：
1. `dominant_elbow_angle`：持拍手手臂夾角（蓄力 80°~100° -> 擊球伸展 160°~180°）
2. `left/right_knee_angle`：膝關節深蹲與起跳角度
3. `jump_height_cm`：起跳滯空高度估算
4. `center_of_mass`：人體重心座標 $(x, y)$
5. `phase`：目前動作階段（準備 / 引拍蓄力 / 擊球頂點 / 隨揮回防）
