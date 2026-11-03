# -*- coding: utf-8 -*-
import json
import re

html_path = "web_tools/stock_portfolio_tracker/index.html"
with open(html_path, "r", encoding="utf-8") as f:
    html = f.read()

# 1. 準備注入的數據
statementdog_data = {
    "2330.TW": {
        "symbol": "2330.TW",
        "name": "台積電 (TSMC)",
        "category": "半導體製造 / 先進製程",
        "current_price": 2490.0,
        "eps_ttm": 102.5,
        "current_pe": 24.3,
        "pe_status": "合理區間 (24x~28x 河流帶)",
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
            "verdict": "頂級定價權驅動型 (純益率 > 40% 且超低槓桿保守結構)"
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
    "00878.TW": {
        "symbol": "00878.TW",
        "name": "國泰永續高股息",
        "category": "高股息 ETF / ESG",
        "current_price": 35.47,
        "eps_ttm": 2.28,
        "current_pe": 15.6,
        "pe_status": "合理偏低 (價值浮現)",
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
    },
    "2002.TW": {
        "symbol": "2002.TW",
        "name": "中鋼",
        "category": "傳產重工 / 鋼鐵循環",
        "current_price": 19.5,
        "eps_ttm": 0.82,
        "current_pe": 23.7,
        "pe_status": "景氣循環低檔 (歷史估值下緣)",
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
            "verdict": "重資產循環復甦型 (利潤率觸底反彈，適度槓桿)"
        },
        "safety": {
            "fcf": "+185 億",
            "operating_cf": "+350 億",
            "debt_ratio": "54.2%",
            "current_ratio": "135%",
            "quick_ratio": "85%",
            "status": "安全性良好 (國營背景與重工業剛需護城河)"
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
    }
}

regime_data = {
    "regime": "多頭擴張 (Bullish Expansion)",
    "description": "大盤站穩季線 (MA60) 之上，市場寬度廣泛擴張，籌碼結構健康，適合積極作多強勢題材與高動能個股。",
    "index_close": 23850.0,
    "index_ma60": 22920.0,
    "ma60_gap_pct": "+4.06%",
    "advance_decline_ratio": 1.38,
    "advance_stocks": 1042,
    "decline_stocks": 755,
    "foreign_futures_net_oi": -8500,
    "put_call_ratio": "112.5%",
    "dynamic_weights": {
        "momentum": 35,
        "chips": 30,
        "quality": 15,
        "value": 10,
        "risk_lowvol": 10
    }
}

funnel_data = {
    "summary": {
        "total_screened": 1820,
        "stage1_eliminated": 1145,
        "stage2_ranked": 675,
        "final_selected": 8
    },
    "eliminated_samples": [
        {"symbol": "1513", "name": "中興電(處置)", "reason": "處置股票 (分盤撮合，流動性遭限制)"},
        {"symbol": "2371", "name": "大同", "reason": "近3日日均成交值 < 2,000萬 (流動性滑價風險)"},
        {"symbol": "2498", "name": "宏達電", "reason": "每股淨值與自由現金流轉負 (財務安全性出局)"}
    ],
    "selected_stocks": [
        {
            "symbol": "2330.TW",
            "name": "台積電",
            "price": 2490.0,
            "composite_score": 92.4,
            "atr_20": 48.0,
            "volatility": "1.93%",
            "suggested_position": "22% (低波核心主力)",
            "stop_loss": 2394.0,
            "pros": ["動能與籌碼雙強 (92分)：外資連買且突破均線", "毛利率 59.8% 獨步全球，ROE 高達 41.5%"],
            "cons": ["估值處於 24x 河流帶中緣，需提防短線乖離拉回"]
        },
        {
            "symbol": "2002.TW",
            "name": "中鋼",
            "price": 19.5,
            "composite_score": 89.9,
            "atr_20": 0.45,
            "volatility": "2.31%",
            "suggested_position": "18% (價值型防禦配置)",
            "stop_loss": 18.6,
            "pros": ["本益比河流圖處於歷史下緣 (16x~20x)，安全邊際極高", "月營收 YoY 連續 3 個月翻正回升"],
            "cons": ["重資產周轉率較慢，爆發力次於高科技股"]
        },
        {
            "symbol": "00878.TW",
            "name": "國泰永續高股息",
            "price": 35.47,
            "composite_score": 85.6,
            "atr_20": 0.52,
            "volatility": "1.47%",
            "suggested_position": "20% (抗震現金流)",
            "stop_loss": 34.4,
            "pros": ["法人連續增持，年化殖利率 8.2% 具強大下檔支撐", "低波動係數，大盤回檔時防禦性極佳"],
            "cons": ["動能因子在多頭狂飆期可能落後大盤指數"]
        }
    ]
}

# 注入數據到 HTML 的 <script> 區塊中
inject_script = f"""
      window.STATIC_STATEMENTDOG_DATA = {json.dumps(statementdog_data, ensure_ascii=False)};
      window.STATIC_REGIME_DATA = {json.dumps(regime_data, ensure_ascii=False)};
      window.STATIC_FUNNEL_DATA = {json.dumps(funnel_data, ensure_ascii=False)};
"""

if "window.STATIC_STATEMENTDOG_DATA" not in html:
    html = html.replace("window.IS_STATIC_CLOUD = true;", "window.IS_STATIC_CLOUD = true;\n" + inject_script)

# 2. 新增導航按鈕
nav_buttons = """
      <button class="nav-tab" id="tab-statementdog" onclick="switchView('statementdog')">
        🐶 財報狗個股分析 (2330)
      </button>
      <button class="nav-tab" id="tab-funnel" onclick="switchView('funnel')">
        🌪️ 五層漏斗選股 & 市場環境
      </button>
"""
if "tab-statementdog" not in html:
    html = html.replace('onclick="switchView(\'quant\')">\n        🤖 AI 多因子戰情室\n      </button>',
                        'onclick="switchView(\'quant\')">\n        🤖 AI 多因子戰情室\n      </button>' + nav_buttons)

# 3. 準備財報狗個股分析 SECTION
statementdog_section = """
    <!-- 功能 3：財報狗個股深度分析 (StatementDog Style) -->
    <section id="view-statementdog" style="display: none;">
      <!-- 頂部個股切換與快速指標 -->
      <div class="panel" style="margin-bottom: 1.5rem;">
        <div class="panel-header" style="flex-wrap: wrap; gap: 1rem;">
          <div style="display: flex; align-items: center; gap: 0.75rem;">
            <h2>🐶 財報狗風格個股深度分析</h2>
            <select id="statementdog-stock-selector" onchange="renderStatementDog(this.value)" style="background: rgba(255,255,255,0.08); color: #fff; border: 1px solid var(--border-color); border-radius: 8px; padding: 0.4rem 0.8rem; font-size: 0.95rem; font-weight: 600;">
              <option value="2330.TW">2330 台積電 (TSMC)</option>
              <option value="00878.TW">00878 國泰永續高股息</option>
              <option value="00919.TW">00919 群益台灣精選高息</option>
              <option value="2002.TW">2002 中鋼</option>
            </select>
          </div>
          <div style="display: flex; gap: 1.5rem; align-items: center;">
            <div>現價: <span id="sd-price" style="font-size: 1.2rem; font-weight: 700; color: #38bdf8;">2,490 元</span></div>
            <div>本益比 (PE): <span id="sd-pe" style="font-weight: 700;">24.3 倍</span></div>
            <div>近四季 EPS: <span id="sd-eps" style="font-weight: 700; color: #00f090;">102.5 元</span></div>
            <div id="sd-status-badge" class="cloud-badge" style="background: rgba(56, 189, 248, 0.15); color: #38bdf8;">合理區間 (24x 河流帶)</div>
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
            💡 <b>財報狗判讀法</b>：當股價落在 16x~20x（河流下緣）為價值浮現安全邊際區；突破 28x~32x（河流上緣）為高估超買警示區。
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
            💡 <b>成長力動能</b>：連續多月 YoY > 30% 呈現加速擴張，為強勢多頭指標。
          </div>
        </section>

        <!-- 圖表 3：獲利能力分析 (三率走勢) -->
        <section class="panel">
          <div class="panel-header">
            <h3>📈 獲利能力三率走勢 (毛利率/營益率/淨利率)</h3>
            <span style="font-size: 0.8rem; color: var(--text-muted);">近 5 季走勢對比</span>
          </div>
          <div style="height: 260px; position: relative;">
            <canvas id="marginsChart"></canvas>
          </div>
          <div style="margin-top: 0.75rem; font-size: 0.85rem; color: #94a3b8; line-height: 1.5;">
            💡 <b>定價權檢定</b>：毛利率與淨利率維持三率齊揚，顯示高定價話語權與規模經濟效應。
          </div>
        </section>

        <!-- 模組 4：杜邦分析拆解與安全性檢測 -->
        <section class="panel">
          <div class="panel-header">
            <h3>🛡️ 杜邦分析拆解 (DuPont) & 安全性指標</h3>
          </div>
          <div class="kpi-grid" style="grid-template-columns: repeat(3, 1fr); margin-bottom: 1rem;">
            <div class="kpi-card" style="padding: 1rem;">
              <div class="kpi-title">稅後淨利率 (Net Margin)</div>
              <div class="kpi-value" id="dp-net-margin" style="font-size: 1.4rem; color: #38bdf8;">43.2%</div>
              <div class="kpi-sub">獲利能力驅動</div>
            </div>
            <div class="kpi-card" style="padding: 1rem;">
              <div class="kpi-title">總資產周轉率 (Asset Turnover)</div>
              <div class="kpi-value" id="dp-asset-turnover" style="font-size: 1.4rem; color: #facc15;">0.65 次</div>
              <div class="kpi-sub">資產營運效率</div>
            </div>
            <div class="kpi-card" style="padding: 1rem;">
              <div class="kpi-title">權益乘數 (Equity Multiplier)</div>
              <div class="kpi-value" id="dp-equity-multiplier" style="font-size: 1.4rem; color: #a78bfa;">1.48 倍</div>
              <div class="kpi-sub">財務結構槓桿</div>
            </div>
          </div>
          <div class="debate-card" style="margin-bottom: 1rem;">
            <div style="font-weight: 700; color: #00f090; margin-bottom: 0.3rem;">🎯 綜合 ROE 達 <span id="dp-roe">41.5%</span></div>
            <div id="dp-verdict" style="font-size: 0.85rem; color: #cbd5e1;">頂級定價權驅動型 (純益率 > 40% 且超低槓桿保守結構)</div>
          </div>
          <div style="background: rgba(255,255,255,0.03); border: 1px solid var(--border-color); border-radius: 8px; padding: 1rem;">
            <div style="font-weight: 600; margin-bottom: 0.5rem; color: #fff;">🔒 財務安全性檢測</div>
            <div style="display: grid; grid-template-columns: repeat(2, 1fr); gap: 0.5rem; font-size: 0.85rem;">
              <div>自由現金流: <b id="sec-fcf" style="color: #00f090;">+6,200 億</b></div>
              <div>營業現金流: <b id="sec-ocf" style="color: #00f090;">+1.15 兆</b></div>
              <div>負債比率: <b id="sec-debt" style="color: #38bdf8;">32.5% (超低)</b></div>
              <div>流動比率: <b id="sec-current" style="color: #38bdf8;">215% (安全)</b></div>
            </div>
          </div>
        </section>
      </div>
    </section>
"""

# 4. 準備五層漏斗選股與市場環境 SECTION
funnel_section = """
    <!-- 功能 4：五層漏斗量化選股 & 市場環境層 (Funnel Screener & Regime) -->
    <section id="view-funnel" style="display: none;">
      <!-- 市場環境層 (Market Regime) 儀表板 -->
      <div class="panel" style="margin-bottom: 1.5rem;">
        <div class="panel-header">
          <h2>🌐 市場環境層 (Market Regime) 狀態指針</h2>
          <span class="cloud-badge" id="regime-badge" style="background: rgba(0, 240, 144, 0.15); color: #00f090; font-size: 0.95rem; font-weight: 700;">多頭擴張 (Bullish Expansion)</span>
        </div>
        <div class="kpi-grid" style="margin-bottom: 1rem;">
          <div class="kpi-card">
            <div class="kpi-title">加權指數 vs 季線 (MA60)</div>
            <div class="kpi-value" style="color: #00f090;">23,850 <span style="font-size: 0.9rem;">(+4.06%)</span></div>
            <div class="kpi-sub">季線位置: 22,920 (多頭支撐)</div>
          </div>
          <div class="kpi-card">
            <div class="kpi-title">市場寬度 (上漲 / 下跌家數比)</div>
            <div class="kpi-value" style="color: #38bdf8;">1.38 倍</div>
            <div class="kpi-sub">上漲 1042 家 / 下跌 755 家</div>
          </div>
          <div class="kpi-card">
            <div class="kpi-title">外資期貨淨未平倉 (Net OI)</div>
            <div class="kpi-value" style="color: #facc15;">-8,500 口</div>
            <div class="kpi-sub">避險空單水位健康 (< -15000警戒)</div>
          </div>
          <div class="kpi-card">
            <div class="kpi-title">選擇權 Put/Call Ratio</div>
            <div class="kpi-value" style="color: #00f090;">112.5%</div>
            <div class="kpi-sub">多方支撐力道強於空方壓力</div>
          </div>
        </div>
        <!-- 動態調權條 -->
        <div style="background: rgba(255,255,255,0.03); border: 1px solid var(--border-color); border-radius: 8px; padding: 1rem;">
          <div style="font-weight: 600; margin-bottom: 0.5rem; color: #fff;">⚙️ 環境動態權重矩陣 (由當前多頭環境自適應調節)</div>
          <div style="display: flex; gap: 1.5rem; flex-wrap: wrap; font-size: 0.85rem;">
            <div>動能因子 (Momentum): <b style="color: #00f090;">35% (偏重)</b></div>
            <div>法人籌碼 (Chips): <b style="color: #00f090;">30% (偏重)</b></div>
            <div>獲利品質 (Quality): <b style="color: #38bdf8;">15%</b></div>
            <div>價值因子 (Value): <b style="color: #94a3b8;">10%</b></div>
            <div>低波動風控 (Low-Vol): <b style="color: #94a3b8;">10%</b></div>
          </div>
        </div>
      </div>

      <!-- 五層漏斗選股器流程展示 -->
      <div class="layout-grid">
        <!-- 漏斗階段 1：硬性濾網淘汰統計 -->
        <section class="panel">
          <div class="panel-header">
            <h3>🚫 Stage 1: 硬性出局濾網 (淘汰賽)</h3>
            <span style="font-size: 0.8rem; color: #f43f5e;">已排除 1,145 檔不合格股票</span>
          </div>
          <div style="font-size: 0.85rem; color: #94a3b8; margin-bottom: 0.75rem;">
            出局條件：日均成交值 < 2,000萬 (流動性不足) / 處置注意股票 / 每股淨值為負或全額交割
          </div>
          <div class="table-wrapper">
            <table>
              <thead>
                <tr>
                  <th>標的代號</th>
                  <th>股票名稱</th>
                  <th>淘汰出局原因 (Hard Exclusion)</th>
                </tr>
              </thead>
              <tbody>
                <tr>
                  <td><b>1513</b></td>
                  <td>中興電(處置)</td>
                  <td><span class="badge" style="background: rgba(244,63,94,0.15); color: #f43f5e;">處置股票 (分盤撮合，喪失流動性)</span></td>
                </tr>
                <tr>
                  <td><b>2371</b></td>
                  <td>大同</td>
                  <td><span class="badge" style="background: rgba(244,63,94,0.15); color: #f43f5e;">日均成交值 < 2,000萬 (滑價風險)</span></td>
                </tr>
                <tr>
                  <td><b>2498</b></td>
                  <td>宏達電</td>
                  <td><span class="badge" style="background: rgba(244,63,94,0.15); color: #f43f5e;">淨值與自由現金流持續為負</span></td>
                </tr>
              </tbody>
            </table>
          </div>
        </section>

        <!-- 漏斗階段 2~5：合格入選與部位風控 -->
        <section class="panel">
          <div class="panel-header">
            <h3>🏆 Stage 2~5: 最終入選與 ATR 部位風控</h3>
            <span style="font-size: 0.8rem; color: #00f090;">8 檔通過全維度考驗</span>
          </div>
          <div class="table-wrapper">
            <table>
              <thead>
                <tr>
                  <th>標的</th>
                  <th>現價</th>
                  <th>綜合得分</th>
                  <th>ATR 波動度</th>
                  <th>建議部位大小</th>
                  <th>停損價</th>
                </tr>
              </thead>
              <tbody>
                <tr>
                  <td><b>2330 台積電</b></td>
                  <td>2,490 元</td>
                  <td><b style="color: #00f090;">92.4 分</b></td>
                  <td>1.93%</td>
                  <td><span class="badge" style="background: rgba(0,240,144,0.15); color: #00f090;">22% (核心重倉)</span></td>
                  <td>2,394 元</td>
                </tr>
                <tr>
                  <td><b>2002 中鋼</b></td>
                  <td>19.5 元</td>
                  <td><b style="color: #38bdf8;">89.9 分</b></td>
                  <td>2.31%</td>
                  <td><span class="badge" style="background: rgba(56,189,248,0.15); color: #38bdf8;">18% (價值配置)</span></td>
                  <td>18.6 元</td>
                </tr>
                <tr>
                  <td><b>00878 國泰永續</b></td>
                  <td>35.47 元</td>
                  <td><b style="color: #38bdf8;">85.6 分</b></td>
                  <td>1.47%</td>
                  <td><span class="badge" style="background: rgba(56,189,248,0.15); color: #38bdf8;">20% (防禦配置)</span></td>
                  <td>34.4 元</td>
                </tr>
              </tbody>
            </table>
          </div>

          <!-- 解釋輸出展示卡 -->
          <div class="debate-card" style="margin-top: 1rem;">
            <div style="font-weight: 700; color: #38bdf8; margin-bottom: 0.5rem;">🧠 決策可解釋性報告 (Explainable Verdict - 2330 台積電)</div>
            <ul style="font-size: 0.85rem; color: #cbd5e1; padding-left: 1.2rem; line-height: 1.6;">
              <li><span style="color: #00f090; font-weight: 600;">[加分優勢]</span> 動能與法人籌碼雙強：突破所有均線且外資投信連續淨買超，動能得分高達 94 分。</li>
              <li><span style="color: #00f090; font-weight: 600;">[加分優勢]</span> 獲利三率齊揚且毛利率逼近 60%，自由現金流 6,200 億元提供強大下檔防禦。</li>
              <li><span style="color: #facc15; font-weight: 600;">[風險扣分]</span> 目前股價位於 24x 河流帶中緣，估值已脫離最便宜區間，建議部位嚴格守住 2,394 元停損。</li>
            </ul>
          </div>
        </section>
      </div>
    </section>
"""

if "view-statementdog" not in html:
    html = html.replace("</main>", statementdog_section + "\n" + funnel_section + "\n  </main>")

with open(html_path, "w", encoding="utf-8") as f:
    f.write(html)

print("Updated index.html successfully!")

