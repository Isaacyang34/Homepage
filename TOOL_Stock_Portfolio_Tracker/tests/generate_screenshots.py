# -*- coding: utf-8 -*-
"""
Headless Chrome 截圖生成腳本
安靜、零彈窗、無頭模式截取系統實際運行之畫面
"""
import os
import sys
import subprocess
import time

if sys.stdout is not None and hasattr(sys.stdout, "reconfigure"):
    sys.stdout.reconfigure(encoding="utf-8")
if sys.stderr is not None and hasattr(sys.stderr, "reconfigure"):
    sys.stderr.reconfigure(encoding="utf-8")

BASE_DIR = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SCREENSHOT_DIR = os.path.join(BASE_DIR, "reports", "screenshots")
os.makedirs(SCREENSHOT_DIR, exist_ok=True)

CHROME_PATH = r"C:\Program Files\Google\Chrome\Application\chrome.exe"
if not os.path.exists(CHROME_PATH):
    CHROME_PATH = r"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe"

WEB_DIR = os.path.join(BASE_DIR, "web")
INDEX_PATH = os.path.join(WEB_DIR, "index.html")

with open(INDEX_PATH, "r", encoding="utf-8") as f:
    base_html = f.read()

# -------------------------------------------------------------
# 頁面 1: 首頁/存股庫存部位明細 (含 🔍 深度分析 操作按鈕與標的連結)
# -------------------------------------------------------------
p1_html = base_html.replace('id="login-modal" class="modal-overlay"', 'id="login-modal" class="modal-overlay hidden" style="display:none;"')
p1_file = os.path.join(WEB_DIR, "temp_view_portfolio.html")
with open(p1_file, "w", encoding="utf-8") as f:
    f.write(p1_html)

# -------------------------------------------------------------
# 頁面 2: 盤後五維度綜合排名與市場環境指針 (sub-content-postmarket)
# -------------------------------------------------------------
p2_html = p1_html
p2_html = p2_html.replace('id="positions-tab-content"', 'id="positions-tab-content" class="hidden"')
p2_html = p2_html.replace('id="rankings-tab-content" class="hidden"', 'id="rankings-tab-content"')
p2_html = p2_html.replace('id="tab-btn-positions" class="main-tab-btn active"', 'id="tab-btn-positions" class="main-tab-btn"')
p2_html = p2_html.replace('id="tab-btn-rankings" class="main-tab-btn"', 'id="tab-btn-rankings" class="main-tab-btn active"')
p2_html = p2_html.replace('id="sub-content-market"', 'id="sub-content-market" class="hidden"')
p2_html = p2_html.replace('id="sub-content-postmarket" class="hidden"', 'id="sub-content-postmarket"')
p2_html = p2_html.replace('id="sub-tab-btn-market" class="sub-tab-btn active"', 'id="sub-tab-btn-market" class="sub-tab-btn"')
p2_html = p2_html.replace('id="sub-tab-btn-postmarket" class="sub-tab-btn"', 'id="sub-tab-btn-postmarket" class="sub-tab-btn active"')
# 自動觸發渲染盤後表
p2_html = p2_html.replace('</body>', '<script>window.addEventListener("load", function(){ setTimeout(function(){ if(typeof renderPostMarketRankings==="function") renderPostMarketRankings("composite"); }, 500); });</script></body>')

p2_file = os.path.join(WEB_DIR, "temp_view_postmarket.html")
with open(p2_file, "w", encoding="utf-8") as f:
    f.write(p2_html)

# -------------------------------------------------------------
# 頁面 3: 個股基本面深度分析 & 價值河流圖 (2330 台積電 P/E River Band, 營收, 三率, 杜邦)
# -------------------------------------------------------------
p3_html = p1_html
p3_html = p3_html.replace('id="positions-tab-content"', 'id="positions-tab-content" class="hidden"')
p3_html = p3_html.replace('id="fundamental-tab-content" class="hidden"', 'id="fundamental-tab-content"')
p3_html = p3_html.replace('id="tab-btn-positions" class="main-tab-btn active"', 'id="tab-btn-positions" class="main-tab-btn"')
p3_html = p3_html.replace('id="tab-btn-fundamental" class="main-tab-btn"', 'id="tab-btn-fundamental" class="main-tab-btn active"')
# 自動觸發渲染 2330 台積電
p3_html = p3_html.replace('</body>', '<script>window.addEventListener("load", function(){ setTimeout(function(){ if(typeof renderFundamentalAnalysis==="function") renderFundamentalAnalysis("2330.TW"); }, 500); });</script></body>')

p3_file = os.path.join(WEB_DIR, "temp_view_fundamental.html")
with open(p3_file, "w", encoding="utf-8") as f:
    f.write(p3_html)

# 執行無頭截圖任務
tasks = [
    ("screenshot_1_portfolio_with_analysis_btn.png", p1_file),
    ("screenshot_2_postmarket_rankings_regime.png", p2_file),
    ("screenshot_3_fundamental_river_bands.png", p3_file)
]

print("=== Starting Headless Chrome Screenshots ===")
for img_name, html_f in tasks:
    out_img = os.path.join(SCREENSHOT_DIR, img_name)
    file_url = f"file:///{os.path.abspath(html_f).replace(os.sep, '/')}"
    cmd = [
        CHROME_PATH,
        "--headless=new",
        "--disable-gpu",
        "--hide-scrollbars",
        "--run-all-compositor-stages-before-draw",
        "--virtual-time-budget=3000",
        "--window-size=1680,1050",
        f"--screenshot={out_img}",
        file_url
    ]
    print(f"Capturing: {img_name}...")
    res = subprocess.run(cmd, stdout=subprocess.PIPE, stderr=subprocess.PIPE)
    if os.path.exists(out_img) and os.path.getsize(out_img) > 10000:
        print(f"[OK] Screenshot captured: {img_name} ({os.path.getsize(out_img):,} bytes)")
    else:
        print(f"[FAIL] Screenshot failed: {img_name}, returncode={res.returncode}")

# 清理暫存 html
for _, f_path in tasks:
    if os.path.exists(f_path):
        os.remove(f_path)

print("=== All Headless Screenshots Complete ===")
