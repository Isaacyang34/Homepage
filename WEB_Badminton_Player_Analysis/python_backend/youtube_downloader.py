"""
YouTube 羽球影片下載與自動切片分析工具 (YouTube Badminton Clip Extractor & Analyzer)
---------------------------------------------------------------------------------
使用 yt-dlp 下載 YouTube 上的羽球比賽/教學影片，並支援指定起迄時間裁剪，
下載後可直接傳送給 player_analyzer.py 執行選手動作與生物力學分析。
"""

import os
import sys
import subprocess
import argparse
from typing import Optional

def check_ytdlp_installed() -> bool:
    try:
        subprocess.run(["yt-dlp", "--version"], stdout=subprocess.PIPE, stderr=subprocess.PIPE, check=True)
        return True
    except (subprocess.CalledProcessError, FileNotFoundError):
        return False

def download_youtube_clip(
    url: str,
    output_path: str = "downloaded_clip.mp4",
    start_time: Optional[str] = None,
    end_time: Optional[str] = None,
    quality: str = "1080"
) -> bool:
    """
    從 YouTube 下載指定解析度與時間區間的羽球影片片段
    :param url: YouTube 影片網址 (例如: https://www.youtube.com/watch?v=xxxx)
    :param output_path: 輸出的本機 MP4 檔案路徑
    :param start_time: 起始時間字串 (例如: "00:01:25")
    :param end_time: 結束時間字串 (例如: "00:01:45")
    :param quality: 最大畫質 (例如: "1080", "720")
    """
    if not check_ytdlp_installed():
        print("[Error] 未找到 yt-dlp 工具。請先執行安裝: pip install yt-dlp")
        return False

    print(f"\n[YouTube Downloader] 正在解析影片: {url}")
    
    # 格式選擇：優先下載 50/60fps 高幀率 1080p/720p 影片
    format_selector = f"bestvideo[height<={quality}][ext=mp4]+bestaudio[ext=m4a]/best[height<={quality}]"

    cmd = [
        "yt-dlp",
        "-f", format_selector,
        "--merge-output-format", "mp4",
        "-o", output_path,
        "--force-overwrites"
    ]

    # 如果有指定時間區間，利用 ffmpeg 進行精確裁剪
    if start_time and end_time:
        cmd.extend([
            "--download-sections", f"*{start_time}-{end_time}",
            "--force-keyframes-at-cuts"
        ])
        print(f"[Clipping] 下載指定回合區間: {start_time} ~ {end_time}")

    cmd.append(url)

    try:
        print("[Downloading] 正在下載影片串流...")
        result = subprocess.run(cmd, check=True)
        print(f"[Success] 影片已成功下載至: {output_path}")
        return True
    except subprocess.CalledProcessError as e:
        print(f"[Error] 下載失敗: {e}")
        return False

def main():
    parser = argparse.ArgumentParser(description="YouTube Badminton Video Downloader for AI Training")
    parser.add_argument("--url", type=str, required=True, help="YouTube video URL")
    parser.add_argument("--start", type=str, default=None, help="Start time (HH:MM:SS), e.g. 00:02:15")
    parser.add_argument("--end", type=str, default=None, help="End time (HH:MM:SS), e.g. 00:02:35")
    parser.add_argument("--output", type=str, default="badminton_yt_clip.mp4", help="Output MP4 path")
    parser.add_argument("--analyze", action="store_true", help="Automatically run player pose analysis after download")
    args = parser.parse_args()

    success = download_youtube_clip(args.url, args.output, args.start, args.end)
    
    if success and args.analyze:
        output_json = f"analysis_result_{os.path.splitext(os.path.basename(args.output))[0]}.json"
        print(f"\n[AI Analyzer] 開始對下載的片段進行羽球動作與骨架分析...")
        os.system(f'python player_analyzer.py --input "{args.output}" --output "{output_json}"')

        # 自動在預設瀏覽器打開視覺化分析儀表板
        index_html = os.path.join(os.path.dirname(os.path.dirname(os.path.abspath(__file__))), "index.html")
        print(f"\n[Dashboard] 分析完成！正在為您在瀏覽器打開視覺化動作分析儀表板: {index_html}")
        try:
            import webbrowser
            webbrowser.open(f"file:///{os.path.abspath(index_html)}")
        except Exception:
            pass

if __name__ == "__main__":
    main()
