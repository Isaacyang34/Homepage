# -*- coding: utf-8 -*-
files = [
    "TOOL_Stock_Portfolio_Tracker/web/index.html",
    "web_tools/stock_portfolio_tracker/index.html"
]

for p in files:
    with open(p, "r", encoding="utf-8") as f:
        c = f.read()

    # 更新版本標籤
    c = c.replace('<span class="version-pill">V1.2.3</span>', '<span class="version-pill">V1.3.0</span>')
    c = c.replace('Web Edition V1.2.3', 'Web Edition V1.3.0')
    c = c.replace('style.css?v=1.2.3.1793606500', 'style.css?v=1.3.0.20261103')

    with open(p, "w", encoding="utf-8") as f:
        f.write(c)

print("Updated index.html version badges to V1.3.0 successfully!")

