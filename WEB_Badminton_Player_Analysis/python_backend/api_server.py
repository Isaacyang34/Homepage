"""
羽球影像分析線上後端 API 伺服器 (FastAPI Backend Server)
------------------------------------------------------
提供 RESTful API 與即時分析介面：
1. POST /api/analyze-video: 接收前端上傳影片，呼叫 YOLO-Pose 進行分析並回傳 JSON
2. GET /api/health: 伺服器狀態確認
"""

import os
import shutil
import uuid
import tempfile
from typing import Optional
from pydantic import BaseModel
from fastapi import FastAPI, File, UploadFile, BackgroundTasks
from fastapi.middleware.cors import CORSMiddleware
from fastapi.staticfiles import StaticFiles
from fastapi.responses import JSONResponse, FileResponse
from player_analyzer import BadmintonPoseAnalyzer
from youtube_downloader import download_youtube_clip

# 確保影片儲存資料夾存在
STATIC_DIR = os.path.join(os.path.dirname(os.path.abspath(__file__)), "static_videos")
os.makedirs(STATIC_DIR, exist_ok=True)

# 取得前端根目錄路徑
FRONTEND_DIR = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))

class YouTubeAnalysisRequest(BaseModel):
    url: str
    start_time: Optional[str] = None
    end_time: Optional[str] = None
    quality: Optional[str] = "1080"

app = FastAPI(
    title="Badminton Vision AI API",
    description="羽球選手動作生物力學與姿態分析後端服務",
    version="1.0.0"
)

# 允許跨來源請求 (CORS)
app.add_middleware(
    CORSMiddleware,
    allow_origins=["*"],
    allow_credentials=True,
    allow_methods=["*"],
    allow_headers=["*"],
)

# 掛載靜態影片目錄，讓前端 <video> 標籤可直接串流播放
app.mount("/videos", StaticFiles(directory=STATIC_DIR), name="videos")

# 初始化分析引擎 (使用輕量版 yolov8n-pose 或標準版 yolov8m-pose)
analyzer = BadmintonPoseAnalyzer(model_weight="yolov8n-pose.pt")

@app.get("/api/health")
async def health_check():
    return {
        "status": "healthy",
        "service": "Badminton Player Action Analyzer",
        "engine_ready": analyzer.model is not None
    }

@app.get("/")
async def serve_index():
    index_path = os.path.join(FRONTEND_DIR, "index.html")
    if os.path.exists(index_path):
        return FileResponse(index_path)
    return {"message": "Badminton Vision AI API is Running"}

@app.post("/api/analyze-video")
async def analyze_video_endpoint(file: UploadFile = File(...)):
    """
    接收上傳的羽球影片 (.mp4, .mov)，執行姿態與生物力學分析
    """
    suffix = os.path.splitext(file.filename)[1] or ".mp4"
    unique_filename = f"upload_{uuid.uuid4().hex[:8]}{suffix}"
    saved_path = os.path.join(STATIC_DIR, unique_filename)

    with open(saved_path, "wb") as f:
        shutil.copyfileobj(file.file, f)

    try:
        print(f"[API] Processing uploaded video: {file.filename} -> {saved_path}")
        results = analyzer.analyze_video(saved_path)
        # 附加影片串流 URL
        results["video_url"] = f"http://localhost:8000/videos/{unique_filename}"
        return JSONResponse(content={"success": True, "data": results})
    except Exception as e:
        print(f"[API Error] {e}")
        return JSONResponse(
            status_code=500,
            content={"success": False, "error": str(e)}
        )

@app.post("/api/analyze-youtube")
async def analyze_youtube_endpoint(req: YouTubeAnalysisRequest):
    """
    接收 YouTube 網址，使用 yt-dlp 抓取指定回合片段並執行 AI 動作分析
    """
    unique_filename = f"yt_{uuid.uuid4().hex[:8]}.mp4"
    saved_path = os.path.join(STATIC_DIR, unique_filename)

    try:
        print(f"[API] Downloading YouTube clip: {req.url} ({req.start_time} ~ {req.end_time}) -> {saved_path}")
        success = download_youtube_clip(
            url=req.url,
            output_path=saved_path,
            start_time=req.start_time,
            end_time=req.end_time,
            quality=req.quality or "1080"
        )
        if not success:
            raise RuntimeError("YouTube video download failed. Please check the URL or yt-dlp installation.")

        # 執行 YOLO-Pose 分析
        print(f"[API] Running Pose & Biomechanics Analyzer on downloaded clip...")
        results = analyzer.analyze_video(saved_path)
        # 附加影片串流 URL
        results["video_url"] = f"http://localhost:8000/videos/{unique_filename}"
        return JSONResponse(content={"success": True, "data": results})
    except Exception as e:
        print(f"[API Error] {e}")
        return JSONResponse(
            status_code=500,
            content={"success": False, "error": str(e)}
        )

if __name__ == "__main__":
    import uvicorn
    print("[Server] Starting Badminton Analysis API on http://localhost:8000 ...")
    uvicorn.run("api_server:app", host="0.0.0.0", port=8000, reload=True)
