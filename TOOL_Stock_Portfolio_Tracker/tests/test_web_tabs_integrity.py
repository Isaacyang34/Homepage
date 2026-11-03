# -*- coding: utf-8 -*-
import os
import re
import json

base_dir = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
web_dir = os.path.join(base_dir, "web")
html_path = os.path.join(web_dir, "index.html")
js_path = os.path.join(web_dir, "app.js")

with open(html_path, "r", encoding="utf-8") as f:
    html = f.read()

with open(js_path, "r", encoding="utf-8") as f:
    js = f.read()

checks = []

# 檢查 1: HTML 中的標籤與容器 ID
checks.append(("HTML #tab-btn-fundamental", 'id="tab-btn-fundamental"' in html))
checks.append(("HTML #fundamental-tab-content", 'id="fundamental-tab-content"' in html))
checks.append(("HTML #sub-tab-btn-postmarket", 'id="sub-tab-btn-postmarket"' in html))
checks.append(("HTML #sub-content-postmarket", 'id="sub-content-postmarket"' in html))
checks.append(("HTML #fundamental-stock-selector", 'id="fundamental-stock-selector"' in html))
checks.append(("HTML #peRiverChart", 'id="peRiverChart"' in html))
checks.append(("HTML #revYoyChart", 'id="revYoyChart"' in html))
checks.append(("HTML #marginsChart", 'id="marginsChart"' in html))

# 檢查 2: JS 中的監聽與切換邏輯
checks.append(("JS tabBtnFundamental binding", 'tabBtnFundamental.addEventListener("click"' in js))
checks.append(("JS subBtnPostmarket binding", 'subBtnPostmarket.addEventListener("click"' in js))
checks.append(("JS switchMainTab('fundamental')", 'activeTab === "fundamental"' in js))
checks.append(("JS switchSubTab('postmarket')", 'subTab === "postmarket"' in js))
checks.append(("JS renderFundamentalAnalysis call", 'renderFundamentalAnalysis(sym)' in js))
checks.append(("JS renderPostMarketRankings call", 'renderPostMarketRankings()' in js))
checks.append(("JS window.switchMainTab export", 'window.switchMainTab = switchMainTab;' in js))
checks.append(("JS window.switchSubTab export", 'window.switchSubTab = switchSubTab;' in js))

# 檢查 3: STATIC_POST_MARKET_RANKINGS 長度是否為 30
m = re.search(r'window\.STATIC_POST_MARKET_RANKINGS\s*=\s*(\[.*?\]);', js)
if m:
    ranks_data = json.loads(m.group(1))
    checks.append(("JS STATIC_POST_MARKET_RANKINGS count >= 30", len(ranks_data) >= 30))
else:
    checks.append(("JS STATIC_POST_MARKET_RANKINGS found", False))

all_pass = True
print("=== Web Interface and Event Binding Integrity Report ===")
for name, passed in checks:
    status = "PASS" if passed else "FAIL"
    print(f"[{status}] {name}")
    if not passed:
        all_pass = False

print(f"\nOverall Result: {'ALL PASS' if all_pass else 'SOME TESTS FAILED!'}")
if not all_pass:
    exit(1)
