# -*- coding: utf-8 -*-
import json
import re

html_path = "web_tools/stock_portfolio_tracker/index.html"
with open(html_path, "r", encoding="utf-8") as f:
    html = f.read()

# 1. 徹底清除所有財報狗 / statementdog 相關殘留
html = re.sub(r'window\.STATIC_STATEMENTDOG_DATA\s*=\s*\{.*?\};\s*', '', html, flags=re.DOTALL)
html = re.sub(r'<!-- 功能 3：財報狗.*?<!-- 功能 4：五層漏斗.*?</section>\s*</section>', '', html, flags=re.DOTALL)

# 準備乾淨的基本面與盤後量化數據
fundamental_data = {
    "2330.TW": {
        "symbol": "2330.TW",
        "name": "台積電 (TSMC)",
        "category": "半導體製造 / 先進製程",
        "current_price": 2490.0,
        "eps_ttm": 102.5,
        "current_pe": 24.3,
        "pe_status": "合理常態 (24x 河流帶中緣)",
        "pe_color": "#38bdf8",
        "market_cap": "64.5 兆",
        "revenue_yoy": [
            {"month": "2025/11", "rev": 276.0, "yoy": 34.0},
            {"month": "2025/12", "rev": 285.5, "yoy": 36.2},
            {"month": "2026/01", "rev": 298.2, "yoy": 38.5},
            {"month": "2026/02", "rev": 290.1, "yoy": 35.8},
            {"month": "2026/03", "rev": 310.4, "yoy": 41.2},
            {"month": "2026/04", "rev": 322.0, "yoy": 42.8}
        ],
        "margins": [
            {"quarter": "24Q2", "gross": 53.2, "op": 42.5, "net": 36.8},
            {"quarter": "24Q3", "gross": 57.8, "op": 47.5, "net": 40.5},
            {"quarter": "24Q4", "gross": 59.0, "op": 49.0, "net": 42.0},
            {"quarter": "25Q1", "gross": 58.5, "op": 48.2, "net": 41.5},
            {"quarter": "25Q2", "gross": 59.8, "op": 50.1, "net": 43.2}
        ],
        "dupont": {
            "roe": 41.5,
            "net_margin": 43.2,
            "asset_turnover": 0.65,
            "equity_multiplier": 1.48,
            "verdict": "頂級定價權護城河 (淨利率 > 40% 且超低槓桿保守結構)"
        },
        "safety": {
            "fcf": "+6,200 億",
            "operating_cf": "+1.15 兆",
            "debt_ratio": "32.5%",
            "current_ratio": "215%",
            "quick_ratio": "182%",
            "status": "頂級安全 (自由現金流充沛，無結構性財務風險)"
        },
        "river_bands": {
            "quarters": ["24Q2", "24Q3", "24Q4", "25Q1", "25Q2"],
            "band_16x": [1152, 1288, 1408, 1520, 1640],
            "band_20x": [1440, 1610, 1760, 1900, 2050],
            "band_24x": [1728, 1932, 2112, 2280, 2460],
            "band_28x": [2016, 2254, 2464, 2660, 2870],
            "band_32x": [2304, 2576, 2816, 3040, 3280],
            "prices": [1680, 1950, 2180, 2350, 2490]
        }
    },
    "2002.TW": {
        "symbol": "2002.TW",
        "name": "中鋼",
        "category": "傳產重工 / 鋼鐵循環",
        "current_price": 19.5,
        "eps_ttm": 0.82,
        "current_pe": 23.7,
        "pe_status": "景氣循環低檔 (歷史河流估值下緣)",
        "pe_color": "#00f090",
        "market_cap": "3,080 億",
        "revenue_yoy": [
            {"month": "2025/11", "rev": 31.2, "yoy": -4.2},
            {"month": "2025/12", "rev": 32.5, "yoy": 1.5},
            {"month": "2026/01", "rev": 33.8, "yoy": 5.2},
            {"month": "2026/02", "rev": 31.0, "yoy": 3.8},
            {"month": "2026/03", "rev": 35.4, "yoy": 8.5},
            {"month": "2026/04", "rev": 36.8, "yoy": 10.2}
        ],
        "margins": [
            {"quarter": "24Q2", "gross": 8.5, "op": 2.2, "net": 1.8},
            {"quarter": "24Q3", "gross": 9.2, "op": 3.0, "net": 2.4},
            {"quarter": "24Q4", "gross": 10.5, "op": 4.1, "net": 3.2},
            {"quarter": "25Q1", "gross": 11.2, "op": 4.8, "net": 3.9},
            {"quarter": "25Q2", "gross": 12.8, "op": 6.2, "net": 4.8}
        ],
        "dupont": {
            "roe": 18.2,
            "net_margin": 4.8,
            "asset_turnover": 0.95,
            "equity_multiplier": 2.15,
            "verdict": "重資產循環復甦型 (利潤率觸底回升，適度槓桿)"
        },
        "safety": {
            "fcf": "+185 億",
            "operating_cf": "+350 億",
            "debt_ratio": "54.2%",
            "current_ratio": "135%",
            "quick_ratio": "85%",
            "status": "安全性良好 (重工業剛需護城河)"
        },
        "river_bands": {
            "quarters": ["24Q2", "24Q3", "24Q4", "25Q1", "25Q2"],
            "band_16x": [15.0, 15.8, 16.5, 17.2, 18.0],
            "band_20x": [17.5, 18.5, 19.5, 20.5, 21.5],
            "band_24x": [20.0, 21.2, 22.5, 23.8, 25.0],
            "band_28x": [22.5, 24.0, 25.5, 27.0, 28.5],
            "band_32x": [25.0, 26.8, 28.5, 30.2, 32.0],
            "prices": [22.5, 21.0, 20.2, 19.8, 19.5]
        }
    },
    "00878.TW": {
        "symbol": "00878.TW",
        "name": "國泰永續高股息",
        "category": "高股息 ETF / ESG",
        "current_price": 35.47,
        "eps_ttm": 2.28,
        "current_pe": 15.6,
        "pe_status": "合理偏低 (價值浮現區)",
        "pe_color": "#00f090",
        "market_cap": "3,400 億",
        "revenue_yoy": [
            {"month": "2025/11", "rev": 22.0, "yoy": 12.0},
            {"month": "2025/12", "rev": 24.5, "yoy": 14.5},
            {"month": "2026/01", "rev": 26.0, "yoy": 15.8},
            {"month": "2026/02", "rev": 25.1, "yoy": 13.2},
            {"month": "2026/03", "rev": 28.4, "yoy": 16.5},
            {"month": "2026/04", "rev": 30.0, "yoy": 18.0}
        ],
        "margins": [
            {"quarter": "24Q2", "gross": 35.0, "op": 25.0, "net": 22.0},
            {"quarter": "24Q3", "gross": 36.5, "op": 26.5, "net": 23.5},
            {"quarter": "24Q4", "gross": 38.0, "op": 28.0, "net": 25.0},
            {"quarter": "25Q1", "gross": 37.5, "op": 27.2, "net": 24.5},
            {"quarter": "25Q2", "gross": 39.0, "op": 29.0, "net": 26.2}
        ],
        "dupont": {
            "roe": 26.2,
            "net_margin": 26.2,
            "asset_turnover": 0.82,
            "equity_multiplier": 1.22,
            "verdict": "穩健收益防禦型 (高股息率與低波動權重保護)"
        },
        "safety": {
            "fcf": "+150 億",
            "operating_cf": "+220 億",
            "debt_ratio": "15.0%",
            "current_ratio": "380%",
            "quick_ratio": "350%",
            "status": "超高安全 (ETF 籃子分散風險)"
        },
        "river_bands": {
            "quarters": ["24Q2", "24Q3", "24Q4", "25Q1", "25Q2"],
            "band_16x": [24.0, 26.5, 28.8, 31.0, 33.2],
            "band_20x": [27.0, 29.5, 32.0, 34.5, 37.0],
            "band_24x": [30.0, 32.8, 35.5, 38.0, 41.0],
            "band_28x": [33.0, 36.0, 39.0, 42.0, 45.0],
            "band_32x": [36.0, 39.5, 42.8, 46.0, 49.5],
            "prices": [28.5, 31.2, 33.8, 34.9, 35.47]
        }
    },
    "00919.TW": {
        "symbol": "00919.TW",
        "name": "群益台灣精選高息",
        "category": "高股息 ETF / 季配",
        "current_price": 32.49,
        "eps_ttm": 1.38,
        "current_pe": 23.6,
        "pe_status": "合理常態區間",
        "pe_color": "#facc15",
        "market_cap": "2,850 億",
        "revenue_yoy": [
            {"month": "2025/11", "rev": 18.0, "yoy": 11.2},
            {"month": "2025/12", "rev": 19.5, "yoy": 13.0},
            {"month": "2026/01", "rev": 21.0, "yoy": 14.5},
            {"month": "2026/02", "rev": 20.2, "yoy": 12.8},
            {"month": "2026/03", "rev": 22.8, "yoy": 15.0},
            {"month": "2026/04", "rev": 24.1, "yoy": 16.2}
        ],
        "margins": [
            {"quarter": "24Q2", "gross": 38.0, "op": 28.0, "net": 25.0},
            {"quarter": "24Q3", "gross": 39.5, "op": 29.5, "net": 26.5},
            {"quarter": "24Q4", "gross": 41.0, "op": 31.0, "net": 28.0},
            {"quarter": "25Q1", "gross": 40.5, "op": 30.2, "net": 27.5},
            {"quarter": "25Q2", "gross": 42.0, "op": 32.0, "net": 30.9}
        ],
        "dupont": {
            "roe": 30.9,
            "net_margin": 30.9,
            "asset_turnover": 0.88,
            "equity_multiplier": 1.14,
            "verdict": "高現金回饋型 (成分股篩選高現金殖利率標的)"
        },
        "safety": {
            "fcf": "+110 億",
            "operating_cf": "+180 億",
            "debt_ratio": "12.0%",
            "current_ratio": "420%",
            "quick_ratio": "390%",
            "status": "超高安全 (高流動性與定期換股機制)"
        },
        "river_bands": {
            "quarters": ["24Q2", "24Q3", "24Q4", "25Q1", "25Q2"],
            "band_16x": [20.0, 22.0, 24.0, 26.0, 28.0],
            "band_20x": [23.0, 25.5, 27.8, 30.0, 32.5],
            "band_24x": [26.0, 28.8, 31.2, 33.8, 36.5],
            "band_28x": [29.0, 32.0, 34.8, 37.5, 40.5],
            "band_32x": [32.0, 35.2, 38.2, 41.5, 44.5],
            "prices": [25.0, 27.5, 29.8, 31.5, 32.49]
        }
    }
}

post_market_rankings = [
    {
        "rank": 1,
        "symbol": "2330.TW",
        "name": "台積電",
        "price": 2490.0,
        "composite_score": 94.2,
        "momentum": 95,
        "chips": 94,
        "quality": 98,
        "pe_level": "24x 合理常態",
        "pe_level_color": "#38bdf8",
        "atr_20": "1.93%",
        "suggested_pos": "22% (主力)",
        "verdict": "動能籌碼雙強突破，獲利三率齊揚"
    },
    {
        "rank": 2,
        "symbol": "2002.TW",
        "name": "中鋼",
        "price": 19.5,
        "composite_score": 89.9,
        "momentum": 82,
        "chips": 88,
        "quality": 86,
        "pe_level": "16x~20x 超值低估",
        "pe_level_color": "#00f090",
        "atr_20": "2.31%",
        "suggested_pos": "18% (價值)",
        "verdict": "河流圖歷史下緣，月營收連 3 月正成長"
    },
    {
        "rank": 3,
        "symbol": "00878.TW",
        "name": "國泰永續高股息",
        "price": 35.47,
        "composite_score": 88.5,
        "momentum": 80,
        "chips": 92,
        "quality": 90,
        "pe_level": "20x 合理偏低",
        "pe_level_color": "#00f090",
        "atr_20": "1.47%",
        "suggested_pos": "20% (防禦)",
        "verdict": "法人持續加碼，年化殖利率 8.2% 具強防禦"
    },
    {
        "rank": 4,
        "symbol": "00919.TW",
        "name": "群益精選高息",
        "price": 32.49,
        "composite_score": 86.8,
        "momentum": 78,
        "chips": 90,
        "quality": 88,
        "pe_level": "24x 合理區間",
        "pe_level_color": "#facc15",
        "atr_20": "1.62%",
        "suggested_pos": "18% (現金流)",
        "verdict": "高殖利率支撐，定期換股提升品質"
    },
    {
        "rank": 5,
        "symbol": "2382.TW",
        "name": "廣達",
        "price": 315.0,
        "composite_score": 85.2,
        "momentum": 91,
        "chips": 86,
        "quality": 85,
        "pe_level": "24x 合理常態",
        "pe_level_color": "#38bdf8",
        "atr_20": "2.85%",
        "suggested_pos": "15% (題材)",
        "verdict": "AI 伺服器出貨放量，月營收創新高"
    },
    {
        "rank": 6,
        "symbol": "2317.TW",
        "name": "鴻海",
        "price": 218.5,
        "composite_score": 84.6,
        "momentum": 88,
        "chips": 85,
        "quality": 84,
        "pe_level": "16x~20x 便宜",
        "pe_level_color": "#00f090",
        "atr_20": "2.40%",
        "suggested_pos": "15% (核心)",
        "verdict": "GB200 伺服器量產，本益比仍處相對低檔"
    }
]

# 注入純淨數據
inject_script = f"""
      window.STATIC_FUNDAMENTAL_DATA = {json.dumps(fundamental_data, ensure_ascii=False)};
      window.STATIC_POST_MARKET_RANKINGS = {json.dumps(post_market_rankings, ensure_ascii=False)};
"""
html = html.replace("window.IS_STATIC_CLOUD = true;", "window.IS_STATIC_CLOUD = true;\n" + inject_script)

# 更新導航標籤
nav_tabs_html = """
    <div class="nav-tabs-wrapper">
      <button class="nav-tab active" id="tab-portfolio" onclick="switchView('portfolio')">
        📦 存股庫存部位
      </button>
      <button class="nav-tab" id="tab-rankings" onclick="switchView('rankings')">
        🏆 盤後量化分析排名
      </button>
      <button class="nav-tab" id="tab-fundamental" onclick="switchView('fundamental')">
        🌊 個股基本面深度分析
      </button>
      <button class="nav-tab" id="tab-quant" onclick="switchView('quant')">
        🤖 AI 多因子戰情室
      </button>
    </div>
"""
html = re.sub(r'<div class="nav-tabs-wrapper">.*?</div>\s*</header>', nav_tabs_html + "\n  </header>", html, flags=re.DOTALL)

# 新增「🏆 盤後量化分析排名」與「🌊 個股基本面深度分析」VIEW
views_html = """
    <!-- 功能：🏆 盤後量化分析排名 (Post-Market Quantitative Rankings) -->
    <section id="view-rankings" style="display: none;">
      <!-- 市場環境指標 -->
      <div class="panel" style="margin-bottom: 1.5rem;">
        <div class="panel-header">
          <h2>🌐 盤後市場環境 (Market Regime) 與調權指針</h2>
          <span class="cloud-badge" style="background: rgba(0, 240, 144, 0.15); color: #00f090; font-weight: 700;">多頭擴張 (Bullish Expansion)</span>
        </div>
        <div class="kpi-grid" style="margin-bottom: 1rem;">
          <div class="kpi-card">
            <div class="kpi-title">大盤指數 vs 季線 (MA60)</div>
            <div class="kpi-value" style="color: #00f090;">23,850 <span style="font-size: 0.9rem;">(+4.06%)</span></div>
            <div class="kpi-sub">季線 22,920 具強多方支撐</div>
          </div>
          <div class="kpi-card">
            <div class="kpi-title">市場寬度 (上漲 / 下跌家數)</div>
            <div class="kpi-value" style="color: #38bdf8;">1.38 倍</div>
            <div class="kpi-sub">上漲 1042 家 / 下跌 755 家</div>
          </div>
          <div class="kpi-card">
            <div class="kpi-title">外資期貨淨留倉 (Net OI)</div>
            <div class="kpi-value" style="color: #facc15;">-8,500 口</div>
            <div class="kpi-sub">未平倉避險空單處於健康水位</div>
          </div>
          <div class="kpi-card">
            <div class="kpi-title">選擇權 Put/Call Ratio</div>
            <div class="kpi-value" style="color: #00f090;">112.5%</div>
            <div class="kpi-sub">多方支撐力道佔優</div>
          </div>
        </div>
        <div style="background: rgba(255,255,255,0.03); border: 1px solid var(--border-color); border-radius: 8px; padding: 0.8rem 1rem; font-size: 0.85rem; display: flex; gap: 1.5rem; flex-wrap: wrap;">
          <div>⚙️ <b>當前動態因子權重</b>：</div>
          <div>動能成長: <b style="color: #00f090;">35%</b></div>
          <div>法人籌碼: <b style="color: #00f090;">30%</b></div>
          <div>獲利品質: <b style="color: #38bdf8;">15%</b></div>
          <div>價值河流: <b style="color: #94a3b8;">10%</b></div>
          <div>低波風控: <b style="color: #94a3b8;">10%</b></div>
        </div>
      </div>

      <!-- 盤後排名表格 -->
      <section class="panel">
        <div class="panel-header" style="flex-wrap: wrap; gap: 1rem;">
          <div>
            <h2>🏆 每日盤後綜合量化排名表</h2>
            <span style="font-size: 0.8rem; color: var(--text-muted);">結合五大維度百分位評分、硬性漏斗過濾與波動度風控</span>
          </div>
          <div style="display: flex; gap: 0.5rem;">
            <button class="btn-sm-action" style="background: rgba(56, 189, 248, 0.2); color: #38bdf8; border: 1px solid #38bdf8;" onclick="filterRankings('composite')">全部綜合榜</button>
            <button class="btn-sm-action" onclick="filterRankings('momentum')">強勢動能榜</button>
            <button class="btn-sm-action" onclick="filterRankings('quality')">獲利三率榜</button>
            <button class="btn-sm-action" onclick="filterRankings('value')">河流低估榜</button>
          </div>
        </div>
        <div class="table-wrapper">
          <table id="rankings-table">
            <thead>
              <tr>
                <th style="width: 60px;">排名</th>
                <th>標的代號 / 名稱</th>
                <th>收盤價</th>
                <th>綜合評分</th>
                <th>動能</th>
                <th>籌碼</th>
                <th>品質</th>
                <th>價值河流位階</th>
                <th>ATR波動</th>
                <th>建議部位</th>
                <th>量化點評</th>
                <th>操作</th>
              </tr>
            </thead>
            <tbody id="rankings-tbody">
              <!-- 動態生成 -->
            </tbody>
          </table>
        </div>
      </section>
    </section>

    <!-- 功能：🌊 個股基本面深度分析 (Fundamental & River Valuation) -->
    <section id="view-fundamental" style="display: none;">
      <!-- 頂部切換個股與即時數據 -->
      <div class="panel" style="margin-bottom: 1.5rem;">
        <div class="panel-header" style="flex-wrap: wrap; gap: 1rem;">
          <div style="display: flex; align-items: center; gap: 0.75rem;">
            <h2>🌊 個股基本面與價值河流圖分析</h2>
            <select id="fundamental-stock-selector" onchange="renderFundamentalAnalysis(this.value)" style="background: rgba(255,255,255,0.08); color: #fff; border: 1px solid var(--border-color); border-radius: 8px; padding: 0.4rem 0.8rem; font-size: 0.95rem; font-weight: 600;">
              <option value="2330.TW">2330 台積電 (TSMC)</option>
              <option value="2002.TW">2002 中鋼</option>
              <option value="00878.TW">00878 國泰永續高股息</option>
              <option value="00919.TW">00919 群益精選高息</option>
            </select>
          </div>
          <div style="display: flex; gap: 1.5rem; align-items: center; flex-wrap: wrap;">
            <div>參考現價: <span id="fd-price" style="font-size: 1.25rem; font-weight: 700; color: #38bdf8;">2,490 元</span></div>
            <div>近四季 EPS: <span id="fd-eps" style="font-weight: 700; color: #00f090;">102.5 元</span></div>
            <div>目前本益比: <span id="fd-pe" style="font-weight: 700;">24.3 倍</span></div>
            <div id="fd-status-badge" class="cloud-badge" style="background: rgba(56, 189, 248, 0.15); color: #38bdf8;">合理常態 (24x 河流帶)</div>
          </div>
        </div>
      </div>

      <!-- 四大核心圖表網格 -->
      <div class="layout-grid">
        <!-- 圖表 1：本益比河流圖 (P/E River Band) -->
        <section class="panel">
          <div class="panel-header">
            <h3>🌊 本益比河流圖 (P/E River Band)</h3>
            <span style="font-size: 0.8rem; color: var(--text-muted);">16x / 20x / 24x / 28x / 32x 五階估值帶</span>
          </div>
          <div style="height: 260px; position: relative;">
            <canvas id="peRiverChart"></canvas>
          </div>
          <div style="margin-top: 0.75rem; font-size: 0.85rem; color: #94a3b8; line-height: 1.5;">
            💡 <b>河流圖位階判讀</b>：股價落在 16x~20x（河流下緣）具高度安全邊際；突破 28x~32x（河流上緣）為高估超買警示區。
          </div>
        </section>

        <!-- 圖表 2：月營收與年增率 (YoY) -->
        <section class="panel">
          <div class="panel-header">
            <h3>📊 月營收與年增率 (YoY) 走勢</h3>
            <span style="font-size: 0.8rem; color: var(--text-muted);">柱狀：營收(億元) / 折線：YoY (%)</span>
          </div>
          <div style="height: 260px; position: relative;">
            <canvas id="revYoyChart"></canvas>
          </div>
          <div style="margin-top: 0.75rem; font-size: 0.85rem; color: #94a3b8; line-height: 1.5;">
            💡 <b>成長動能檢定</b>：單月營收維持高檔且年增率 (YoY) 維持雙位數正成長，代表營收動能充沛。
          </div>
        </section>

        <!-- 圖表 3：獲利能力分析 (三率走勢) -->
        <section class="panel">
          <div class="panel-header">
            <h3>📈 獲利能力三率走勢 (毛利率/營益率/淨利率)</h3>
            <span style="font-size: 0.8rem; color: var(--text-muted);">近 5 季對比</span>
          </div>
          <div style="height: 260px; position: relative;">
            <canvas id="marginsChart"></canvas>
          </div>
          <div style="margin-top: 0.75rem; font-size: 0.85rem; color: #94a3b8; line-height: 1.5;">
            💡 <b>定價權分析</b>：營業毛利率與淨利率維持擴張趨勢，反映具備核心技術定價權與成本控制能力。
          </div>
        </section>

        <!-- 模組 4：杜邦分析拆解與安全性檢測 -->
        <section class="panel">
          <div class="panel-header">
            <h3>🛡️ 杜邦分析拆解 (DuPont) & 安全性檢測</h3>
          </div>
          <div class="kpi-grid" style="grid-template-columns: repeat(3, 1fr); margin-bottom: 1rem;">
            <div class="kpi-card" style="padding: 1rem;">
              <div class="kpi-title">稅後淨利率 (Net Margin)</div>
              <div class="kpi-value" id="dp-net-margin" style="font-size: 1.4rem; color: #38bdf8;">43.2%</div>
              <div class="kpi-sub">獲利利潤率</div>
            </div>
            <div class="kpi-card" style="padding: 1rem;">
              <div class="kpi-title">總資產周轉率 (Turnover)</div>
              <div class="kpi-value" id="dp-asset-turnover" style="font-size: 1.4rem; color: #facc15;">0.65 次</div>
              <div class="kpi-sub">資產營運效率</div>
            </div>
            <div class="kpi-card" style="padding: 1rem;">
              <div class="kpi-title">權益乘數 (Multiplier)</div>
              <div class="kpi-value" id="dp-equity-multiplier" style="font-size: 1.4rem; color: #a78bfa;">1.48 倍</div>
              <div class="kpi-sub">財務結構槓桿</div>
            </div>
          </div>
          <div class="debate-card" style="margin-bottom: 1rem;">
            <div style="font-weight: 700; color: #00f090; margin-bottom: 0.3rem;">🎯 綜合 ROE 達 <span id="dp-roe">41.5%</span></div>
            <div id="dp-verdict" style="font-size: 0.85rem; color: #cbd5e1;">頂級定價權護城河 (淨利率 > 40% 且超低槓桿保守結構)</div>
          </div>
          <div style="background: rgba(255,255,255,0.03); border: 1px solid var(--border-color); border-radius: 8px; padding: 1rem;">
            <div style="font-weight: 600; margin-bottom: 0.5rem; color: #fff;">🔒 財務體質安全性指標</div>
            <div style="display: grid; grid-template-columns: repeat(2, 1fr); gap: 0.5rem; font-size: 0.85rem;">
              <div>自由現金流: <b id="sec-fcf" style="color: #00f090;">+6,200 億</b></div>
              <div>營業現金流: <b id="sec-ocf" style="color: #00f090;">+1.15 兆</b></div>
              <div>負債比率: <b id="sec-debt" style="color: #38bdf8;">32.5% (穩健)</b></div>
              <div>流動比率: <b id="sec-current" style="color: #38bdf8;">215% (安全)</b></div>
            </div>
          </div>
        </section>
      </div>
    </section>
"""

# 把新 views 注入到 main 結尾
if "view-rankings" not in html:
    html = html.replace("</main>", views_html + "\n  </main>")

with open(html_path, "w", encoding="utf-8") as f:
    f.write(html)

print("Updated index.html cleanly!")

