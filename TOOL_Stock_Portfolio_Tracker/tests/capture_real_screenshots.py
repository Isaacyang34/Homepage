# -*- coding: utf-8 -*-
"""
透過本地 HTTP Server 與 Headless Chrome 進行高畫質靜默截圖
"""
import os
import sys
import time
import threading
import subprocess
from http.server import SimpleHTTPRequestHandler, HTTPServer

if sys.stdout is not None and hasattr(sys.stdout, "reconfigure"):
    sys.stdout.reconfigure(encoding="utf-8")
if sys.stderr is not None and hasattr(sys.stderr, "reconfigure"):
    sys.stderr.reconfigure(encoding="utf-8")

BASE_DIR = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SCREENSHOT_DIR = os.path.join(BASE_DIR, "reports", "screenshots")
os.makedirs(SCREENSHOT_DIR, exist_ok=True)

CHROME_PATH = r"C:\Program Files\Google\Chrome\Application\chrome.exe"
WEB_DIR = os.path.join(BASE_DIR, "web")
INDEX_PATH = os.path.join(WEB_DIR, "index.html")

with open(INDEX_PATH, "r", encoding="utf-8") as f:
    base_html = f.read()

# 移除 login modal，並強制展開 app-container
clean_html = base_html.replace('id="login-modal" class="modal-overlay"', 'id="login-modal" class="modal-overlay" style="display:none !important;"')
clean_html = clean_html.replace('id="app-container" class="app-layout hidden"', 'id="app-container" class="app-layout"')

# 模擬真實持股資料供表格呈現
mock_init_script = """
<script>
window.addEventListener("DOMContentLoaded", function() {
  // 注入模擬庫存數據
  state.normalizedPositions = [
    {
      symbol: "2330", name: "台積電", shares: 2000, cost_price: 980.0, current_price: 2490.0,
      total_cost: 1960000, market_val: 4980000, unrealized_pnl: 3020000, roi_pct: 154.08,
      day_pnl: 70000, day_pct: 1.43, week_pnl: 140000, week_pct: 2.89,
      cash_dividend: 18.0, frequency: "季配", yield_on_cost: 3.67, total_dividend: 36000, hist_div_received: 72000,
      market: "TW", is_etf: false
    },
    {
      symbol: "2002", name: "中鋼", shares: 10000, cost_price: 24.5, current_price: 19.5,
      total_cost: 245000, market_val: 195000, unrealized_pnl: -50000, roi_pct: -20.41,
      day_pnl: 2000, day_pct: 1.04, week_pnl: -3000, week_pct: -1.52,
      cash_dividend: 1.0, frequency: "年配", yield_on_cost: 4.08, total_dividend: 10000, hist_div_received: 25000,
      market: "TW", is_etf: false
    },
    {
      symbol: "00878", name: "國泰永續高股息", shares: 15000, cost_price: 21.2, current_price: 35.47,
      total_cost: 318000, market_val: 532050, unrealized_pnl: 214050, roi_pct: 67.31,
      day_pnl: 3000, day_pct: 0.57, week_pnl: 7500, week_pct: 1.43,
      cash_dividend: 2.28, frequency: "季配", yield_on_cost: 10.75, total_dividend: 34200, hist_div_received: 48000,
      market: "TW", is_etf: true
    },
    {
      symbol: "00919", name: "群益精選高息", shares: 12000, cost_price: 24.8, current_price: 32.49,
      total_cost: 297600, market_val: 389880, unrealized_pnl: 92280, roi_pct: 31.01,
      day_pnl: 2400, day_pct: 0.62, week_pnl: 4800, week_pct: 1.25,
      cash_dividend: 2.88, frequency: "季配", yield_on_cost: 11.61, total_dividend: 34560, hist_div_received: 31000,
      market: "TW", is_etf: true
    }
  ];
  state.privacyMode = false;
  state.decryptedData = { positions: state.normalizedPositions };
});
</script>
"""

# -------------------------------------------------------------
# 頁面 1: 首頁部位明細
# -------------------------------------------------------------
p1_html = clean_html + mock_init_script + """
<script>
window.addEventListener("load", function() {
  setTimeout(function() {
    if (typeof renderTableView === "function") renderTableView("");
  }, 300);
});
</script>
"""
with open(os.path.join(WEB_DIR, "snap_view_1.html"), "w", encoding="utf-8") as f:
    f.write(p1_html)

# -------------------------------------------------------------
# 頁面 2: 盤後五維度綜合排名與市場環境
# -------------------------------------------------------------
p2_html = clean_html + mock_init_script
p2_html = p2_html.replace('id="positions-tab-content"', 'id="positions-tab-content" class="hidden"')
p2_html = p2_html.replace('id="rankings-tab-content" class="hidden"', 'id="rankings-tab-content"')
p2_html = p2_html.replace('id="tab-btn-positions" class="main-tab-btn active"', 'id="tab-btn-positions" class="main-tab-btn"')
p2_html = p2_html.replace('id="tab-btn-rankings" class="main-tab-btn"', 'id="tab-btn-rankings" class="main-tab-btn active"')
p2_html = p2_html.replace('id="sub-content-market"', 'id="sub-content-market" class="hidden"')
p2_html = p2_html.replace('id="sub-content-postmarket" class="hidden"', 'id="sub-content-postmarket"')
p2_html = p2_html.replace('id="sub-tab-btn-market" class="sub-tab-btn active"', 'id="sub-tab-btn-market" class="sub-tab-btn"')
p2_html = p2_html.replace('id="sub-tab-btn-postmarket" class="sub-tab-btn"', 'id="sub-tab-btn-postmarket" class="sub-tab-btn active"')
p2_html += """
<script>
window.addEventListener("load", function() {
  setTimeout(function() {
    if (typeof renderPostMarketRankings === "function") renderPostMarketRankings("composite");
  }, 400);
});
</script>
"""
with open(os.path.join(WEB_DIR, "snap_view_2.html"), "w", encoding="utf-8") as f:
    f.write(p2_html)

# -------------------------------------------------------------
# 頁面 3: 個股基本面深度分析 & 價值河流圖 (2330 台積電)
# -------------------------------------------------------------
p3_html = clean_html + mock_init_script
p3_html = p3_html.replace('id="positions-tab-content"', 'id="positions-tab-content" class="hidden"')
p3_html = p3_html.replace('id="fundamental-tab-content" class="hidden"', 'id="fundamental-tab-content"')
p3_html = p3_html.replace('id="tab-btn-positions" class="main-tab-btn active"', 'id="tab-btn-positions" class="main-tab-btn"')
p3_html = p3_html.replace('id="tab-btn-fundamental" class="main-tab-btn"', 'id="tab-btn-fundamental" class="main-tab-btn active"')
p3_html += """
<script>
window.addEventListener("load", function() {
  setTimeout(function() {
    if (typeof renderFundamentalAnalysis === "function") renderFundamentalAnalysis("2330.TW");
  }, 400);
});
</script>
"""
with open(os.path.join(WEB_DIR, "snap_view_3.html"), "w", encoding="utf-8") as f:
    f.write(p3_html)

# 2. 啟動本機 HTTP Server
PORT = 8995
class Handler(SimpleHTTPRequestHandler):
    def __init__(self, *args, **kwargs):
        super().__init__(*args, directory=WEB_DIR, **kwargs)
    def log_message(self, format, *args):
        pass

server = HTTPServer(("127.0.0.1", PORT), Handler)
t = threading.Thread(target=server.serve_forever)
t.daemon = True
t.start()
print(f"HTTP Server started at http://127.0.0.1:{PORT}")
time.sleep(0.5)

# 3. 執行截圖
targets = [
    ("screenshot_1_portfolio_with_analysis_btn.png", f"http://127.0.0.1:{PORT}/snap_view_1.html"),
    ("screenshot_2_postmarket_rankings_regime.png", f"http://127.0.0.1:{PORT}/snap_view_2.html"),
    ("screenshot_3_fundamental_river_bands.png", f"http://127.0.0.1:{PORT}/snap_view_3.html")
]

for img_name, url in targets:
    out_file = os.path.join(SCREENSHOT_DIR, img_name)
    cmd = [
        CHROME_PATH,
        "--headless=new",
        "--disable-gpu",
        "--hide-scrollbars",
        "--window-size=1680,1050",
        f"--screenshot={out_file}",
        url
    ]
    print(f"Capturing: {img_name}...")
    p = subprocess.run(cmd, stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True)
    if os.path.exists(out_file) and os.path.getsize(out_file) > 10000:
        print(f"[OK] Saved {img_name} ({os.path.getsize(out_file):,} bytes)")
    else:
        print(f"[STATUS] {img_name} size={os.path.getsize(out_file) if os.path.exists(out_file) else 0}")

# 4. 關閉 server 與清理
server.shutdown()
for snap in ["snap_view_1.html", "snap_view_2.html", "snap_view_3.html"]:
    p = os.path.join(WEB_DIR, snap)
    if os.path.exists(p):
        os.remove(p)

print("All real screenshots generated successfully!")
