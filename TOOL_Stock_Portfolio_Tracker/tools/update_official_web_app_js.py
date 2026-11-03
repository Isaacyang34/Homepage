# -*- coding: utf-8 -*-
import json
import re

app_js_path = "TOOL_Stock_Portfolio_Tracker/web/app.js"
with open(app_js_path, "r", encoding="utf-8") as f:
    js = f.read()

# 準備基礎數據
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
            "current_ratio": "215%"
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
            "current_ratio": "135%"
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
            "current_ratio": "380%"
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
        "name": "群益精選高息",
        "category": "高股息 ETF / 季配",
        "current_price": 32.49,
        "eps_ttm": 1.38,
        "current_pe": 23.6,
        "pe_status": "合理常態區間",
        "pe_color": "#facc15",
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
            "current_ratio": "420%"
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

# 1. 注入數據到檔案開頭
data_injection = f"""
window.STATIC_FUNDAMENTAL_DATA = {json.dumps(fundamental_data, ensure_ascii=False)};
window.STATIC_POST_MARKET_RANKINGS = {json.dumps(post_market_rankings, ensure_ascii=False)};
"""
if "window.STATIC_FUNDAMENTAL_DATA" not in js:
    js = data_injection + "\n" + js

# 2. 更新 switchMainTab 支援 "fundamental"
old_switch = """  const tabBtnPos = document.getElementById("tab-btn-positions");
  const tabBtnEtf = document.getElementById("tab-btn-etf");
  const tabBtnRank = document.getElementById("tab-btn-rankings");
  const posContent = document.getElementById("positions-tab-content");
  const etfContent = document.getElementById("etf-analytics-tab-content");
  const rankContent = document.getElementById("rankings-tab-content");

  function switchMainTab(activeTab) {
    [tabBtnPos, tabBtnEtf, tabBtnRank].forEach(b => b && b.classList.remove("active"));
    [posContent, etfContent, rankContent].forEach(c => c && c.classList.add("hidden"));

    if (activeTab === "positions") {
      if (tabBtnPos) tabBtnPos.classList.add("active");
      if (posContent) posContent.classList.remove("hidden");
    } else if (activeTab === "etf") {
      if (tabBtnEtf) tabBtnEtf.classList.add("active");
      if (etfContent) etfContent.classList.remove("hidden");
      renderEtfAnalytics();
    } else if (activeTab === "rankings") {
      if (tabBtnRank) tabBtnRank.classList.add("active");
      if (rankContent) rankContent.classList.remove("hidden");
      // 預設進入融資與指標排行 (同 unified_ranking_gui.py)
      const subBtnMargin = document.getElementById("sub-tab-btn-margin");
      const subBtnMarket = document.getElementById("sub-tab-btn-market");
      const subContentMargin = document.getElementById("sub-content-margin");
      const subContentMarket = document.getElementById("sub-content-market");
      if (subBtnMarket && subBtnMarket.classList.contains("active")) {
        if (subContentMarket) subContentMarket.classList.remove("hidden");
        if (subContentMargin) subContentMargin.classList.add("hidden");
        renderMarketRankings();
      } else {
        renderMarginRiskAnalytics();
      }
      loadMarketRankingCacheSilently();
    }
  }

  if (tabBtnPos) tabBtnPos.addEventListener("click", () => switchMainTab("positions"));
  if (tabBtnEtf) tabBtnEtf.addEventListener("click", () => switchMainTab("etf"));
  if (tabBtnRank) tabBtnRank.addEventListener("click", () => switchMainTab("rankings"));"""

new_switch = """  const tabBtnPos = document.getElementById("tab-btn-positions");
  const tabBtnEtf = document.getElementById("tab-btn-etf");
  const tabBtnRank = document.getElementById("tab-btn-rankings");
  const tabBtnFund = document.getElementById("tab-btn-fundamental");
  const posContent = document.getElementById("positions-tab-content");
  const etfContent = document.getElementById("etf-analytics-tab-content");
  const rankContent = document.getElementById("rankings-tab-content");
  const fundContent = document.getElementById("fundamental-tab-content");

  function switchMainTab(activeTab) {
    [tabBtnPos, tabBtnEtf, tabBtnRank, tabBtnFund].forEach(b => b && b.classList.remove("active"));
    [posContent, etfContent, rankContent, fundContent].forEach(c => c && c.classList.add("hidden"));

    if (activeTab === "positions") {
      if (tabBtnPos) tabBtnPos.classList.add("active");
      if (posContent) posContent.classList.remove("hidden");
    } else if (activeTab === "etf") {
      if (tabBtnEtf) tabBtnEtf.classList.add("active");
      if (etfContent) etfContent.classList.remove("hidden");
      renderEtfAnalytics();
    } else if (activeTab === "rankings") {
      if (tabBtnRank) tabBtnRank.classList.add("active");
      if (rankContent) rankContent.classList.remove("hidden");
      const subBtnMarket = document.getElementById("sub-tab-btn-market");
      const subBtnPostMarket = document.getElementById("sub-tab-btn-postmarket");
      if (subBtnPostMarket && subBtnPostMarket.classList.contains("active")) {
        switchSubTab("postmarket");
      } else if (subBtnMarket && subBtnMarket.classList.contains("active")) {
        switchSubTab("market");
      } else {
        switchSubTab("margin");
      }
      loadMarketRankingCacheSilently();
    } else if (activeTab === "fundamental") {
      if (tabBtnFund) tabBtnFund.classList.add("active");
      if (fundContent) fundContent.classList.remove("hidden");
      const sel = document.getElementById("fundamental-stock-selector");
      const sym = sel ? sel.value : "2330.TW";
      renderFundamentalAnalysis(sym);
    }
  }

  window.switchMainTab = switchMainTab;

  if (tabBtnPos) tabBtnPos.addEventListener("click", () => switchMainTab("positions"));
  if (tabBtnEtf) tabBtnEtf.addEventListener("click", () => switchMainTab("etf"));
  if (tabBtnRank) tabBtnRank.addEventListener("click", () => switchMainTab("rankings"));
  if (tabBtnFund) tabBtnFund.addEventListener("click", () => switchMainTab("fundamental"));"""

if old_switch in js:
    js = js.replace(old_switch, new_switch)

# 3. 更新 sub-tab 切換支援 postmarket
old_subtab = """  function switchSubTab(subMode) {
    [subBtnMargin, subBtnMarket].forEach(b => b && b.classList.remove("active"));
    [subContentMargin, subContentMarket].forEach(c => c && c.classList.add("hidden"));

    if (subMode === "margin") {
      if (subBtnMargin) subBtnMargin.classList.add("active");
      if (subContentMargin) subContentMargin.classList.remove("hidden");
      renderMarginRiskAnalytics();
    } else {
      if (subBtnMarket) subBtnMarket.classList.add("active");
      if (subContentMarket) subContentMarket.classList.remove("hidden");
      renderMarketRankings();
    }
  }

  if (subBtnMargin) subBtnMargin.addEventListener("click", () => switchSubTab("margin"));
  if (subBtnMarket) subBtnMarket.addEventListener("click", () => switchSubTab("market"));"""

new_subtab = """  const subBtnPostMarket = document.getElementById("sub-tab-btn-postmarket");
  const subContentPostMarket = document.getElementById("sub-content-postmarket");

  function switchSubTab(subMode) {
    [subBtnMargin, subBtnMarket, subBtnPostMarket].forEach(b => b && b.classList.remove("active"));
    [subContentMargin, subContentMarket, subContentPostMarket].forEach(c => c && c.classList.add("hidden"));

    if (subMode === "margin") {
      if (subBtnMargin) subBtnMargin.classList.add("active");
      if (subContentMargin) subContentMargin.classList.remove("hidden");
      renderMarginRiskAnalytics();
    } else if (subMode === "market") {
      if (subBtnMarket) subBtnMarket.classList.add("active");
      if (subContentMarket) subContentMarket.classList.remove("hidden");
      renderMarketRankings();
    } else if (subMode === "postmarket") {
      if (subBtnPostMarket) subBtnPostMarket.classList.add("active");
      if (subContentPostMarket) subContentPostMarket.classList.remove("hidden");
      renderPostMarketRankings();
    }
  }

  window.switchSubTab = switchSubTab;

  if (subBtnMargin) subBtnMargin.addEventListener("click", () => switchSubTab("margin"));
  if (subBtnMarket) subBtnMarket.addEventListener("click", () => switchSubTab("market"));
  if (subBtnPostMarket) subBtnPostMarket.addEventListener("click", () => switchSubTab("postmarket"));"""

if old_subtab in js:
    js = js.replace(old_subtab, new_subtab)

# 4. 在 renderTableView 加入點擊與分析按鈕
old_tr = """    tr.innerHTML = `
      <td class="td-sticky">
        <div class="sym-cell-box">
          <div class="sym-row-top">
            <span class="sym-code">${p.symbol}</span>
            <span class="badge-tag ${tagMktClass}">${mkt}</span>
            ${etfTag}
          </div>
          <span class="sym-name">${p.name || p.symbol}</span>
        </div>
      </td>"""

new_tr = """    tr.innerHTML = `
      <td class="td-sticky" style="cursor: pointer;" onclick="openStockFundamentalAnalysis('${p.symbol}')" title="點擊直接開啟個股深度分析與價值河流圖">
        <div class="sym-cell-box">
          <div class="sym-row-top">
            <span class="sym-code" style="color: #38bdf8; font-weight: 700;">${p.symbol} 🔍</span>
            <span class="badge-tag ${tagMktClass}">${mkt}</span>
            ${etfTag}
          </div>
          <span class="sym-name">${p.name || p.symbol}</span>
        </div>
      </td>"""

if old_tr in js:
    js = js.replace(old_tr, new_tr)

# 加入最後一欄分析按鈕
old_tr_tail = """      <td>
        <span class="text-cyan font-bold">${isPrivate ? "***" : (p.hist_div_received > 0 ? `$ ${fmtNum(Math.round(p.hist_div_received))}` : "$ 0")}</span>
      </td>
    `;"""

new_tr_tail = """      <td>
        <span class="text-cyan font-bold">${isPrivate ? "***" : (p.hist_div_received > 0 ? `$ ${fmtNum(Math.round(p.hist_div_received))}` : "$ 0")}</span>
      </td>
      <td style="text-align: center;">
        <button class="sub-tab-btn" style="padding: 4px 8px; font-size: 0.75rem; color: #38bdf8; border: 1px solid rgba(56, 189, 248, 0.4); background: rgba(56, 189, 248, 0.1); cursor: pointer;" onclick="openStockFundamentalAnalysis('${p.symbol}')">
          🔍 深度分析
        </button>
      </td>
    `;"""

if old_tr_tail in js:
    js = js.replace(old_tr_tail, new_tr_tail)

# 底部合計列也要加一個空白 td
old_tfoot_tail = """      <td>
        <span class="text-cyan font-bold">${isPrivate ? "***" : (totalHistDiv > 0 ? `$ ${fmtNum(Math.round(totalHistDiv))}` : "$ 0")}</span>
      </td>
    `;"""

new_tfoot_tail = """      <td>
        <span class="text-cyan font-bold">${isPrivate ? "***" : (totalHistDiv > 0 ? `$ ${fmtNum(Math.round(totalHistDiv))}` : "$ 0")}</span>
      </td>
      <td>-</td>
    `;"""

if old_tfoot_tail in js:
    js = js.replace(old_tfoot_tail, new_tfoot_tail)

# 5. 追加核心繪圖與跳轉函式
core_functions = """

// ==========================================
// 🌊 個股基本面深度分析 & 價值河流圖核心
// ==========================================
let peRiverChart = null;
let revYoyChart = null;
let marginsChart = null;

function openStockFundamentalAnalysis(symbol) {
  if (!symbol) return;
  const cleanSym = symbol.includes(".") ? symbol : (symbol + ".TW");
  
  if (typeof switchMainTab === "function") {
    switchMainTab("fundamental");
  }

  const sel = document.getElementById("fundamental-stock-selector");
  if (sel) {
    let exists = false;
    for (let i = 0; i < sel.options.length; i++) {
      if (sel.options[i].value === cleanSym || sel.options[i].value.startsWith(cleanSym.split(".")[0])) {
        sel.selectedIndex = i;
        exists = true;
        break;
      }
    }
    if (!exists) {
      const opt = document.createElement("option");
      opt.value = cleanSym;
      opt.innerText = cleanSym + " (自選深度分析)";
      sel.appendChild(opt);
      sel.value = cleanSym;
    }
  }

  renderFundamentalAnalysis(cleanSym);
  window.scrollTo({ top: 0, behavior: "smooth" });
}
window.openStockFundamentalAnalysis = openStockFundamentalAnalysis;

function renderFundamentalAnalysis(symbol) {
  if (!window.STATIC_FUNDAMENTAL_DATA) return;
  const cleanKey = symbol.includes(".") ? symbol : (symbol + ".TW");
  const data = window.STATIC_FUNDAMENTAL_DATA[cleanKey] || 
               window.STATIC_FUNDAMENTAL_DATA[cleanKey.split(".")[0] + ".TW"] ||
               window.STATIC_FUNDAMENTAL_DATA["2330.TW"];
  if (!data) return;

  const priceEl = document.getElementById("fd-price");
  const peEl = document.getElementById("fd-pe");
  const epsEl = document.getElementById("fd-eps");
  const badgeEl = document.getElementById("fd-status-badge");

  if (priceEl) priceEl.innerText = (data.current_price || 0).toLocaleString() + " 元";
  if (peEl) peEl.innerText = data.current_pe + " 倍";
  if (epsEl) epsEl.innerText = data.eps_ttm + " 元";
  if (badgeEl) {
    badgeEl.innerText = data.pe_status;
    badgeEl.style.color = data.pe_color || "#38bdf8";
  }

  if (data.dupont) {
    const nmEl = document.getElementById("dp-net-margin");
    const atEl = document.getElementById("dp-asset-turnover");
    const emEl = document.getElementById("dp-equity-multiplier");
    const roeEl = document.getElementById("dp-roe");
    const vdEl = document.getElementById("dp-verdict");

    if (nmEl) nmEl.innerText = data.dupont.net_margin + "%";
    if (atEl) atEl.innerText = data.dupont.asset_turnover + " 次";
    if (emEl) emEl.innerText = data.dupont.equity_multiplier + " 倍";
    if (roeEl) roeEl.innerText = data.dupont.roe + "%";
    if (vdEl) vdEl.innerText = data.dupont.verdict;
  }

  if (data.safety) {
    const fcfEl = document.getElementById("sec-fcf");
    const ocfEl = document.getElementById("sec-ocf");
    const debtEl = document.getElementById("sec-debt");
    const curEl = document.getElementById("sec-current");

    if (fcfEl) fcfEl.innerText = data.safety.fcf;
    if (ocfEl) ocfEl.innerText = data.safety.operating_cf;
    if (debtEl) debtEl.innerText = data.safety.debt_ratio;
    if (curEl) curEl.innerText = data.safety.current_ratio;
  }

  renderPeRiverChart(data.river_bands);
  renderRevYoyChart(data.revenue_yoy);
  renderMarginsChart(data.margins);
}
window.renderFundamentalAnalysis = renderFundamentalAnalysis;

function renderPeRiverChart(riverData) {
  const ctx = document.getElementById("peRiverChart");
  if (!ctx || !riverData) return;
  if (peRiverChart) peRiverChart.destroy();

  peRiverChart = new Chart(ctx, {
    type: "line",
    data: {
      labels: riverData.quarters,
      datasets: [
        {
          label: "實際收盤價",
          data: riverData.prices,
          borderColor: "#ffffff",
          borderWidth: 3,
          pointBackgroundColor: "#ffffff",
          pointRadius: 5,
          tension: 0.2,
          order: 1
        },
        {
          label: "32x PE (昂貴超買)",
          data: riverData.band_32x,
          borderColor: "rgba(244, 63, 94, 0.7)",
          backgroundColor: "rgba(244, 63, 94, 0.12)",
          fill: "+1",
          borderWidth: 1.5,
          pointRadius: 0,
          tension: 0.2
        },
        {
          label: "28x PE (偏高警戒)",
          data: riverData.band_28x,
          borderColor: "rgba(251, 146, 60, 0.7)",
          backgroundColor: "rgba(251, 146, 60, 0.12)",
          fill: "+1",
          borderWidth: 1.5,
          pointRadius: 0,
          tension: 0.2
        },
        {
          label: "24x PE (合理常態)",
          data: riverData.band_24x,
          borderColor: "rgba(250, 204, 21, 0.7)",
          backgroundColor: "rgba(250, 204, 21, 0.12)",
          fill: "+1",
          borderWidth: 1.5,
          pointRadius: 0,
          tension: 0.2
        },
        {
          label: "20x PE (偏低便宜)",
          data: riverData.band_20x,
          borderColor: "rgba(56, 189, 248, 0.7)",
          backgroundColor: "rgba(56, 189, 248, 0.12)",
          fill: "+1",
          borderWidth: 1.5,
          pointRadius: 0,
          tension: 0.2
        },
        {
          label: "16x PE (超值低估)",
          data: riverData.band_16x,
          borderColor: "rgba(0, 240, 144, 0.7)",
          backgroundColor: "rgba(0, 240, 144, 0.15)",
          fill: "origin",
          borderWidth: 1.5,
          pointRadius: 0,
          tension: 0.2
        }
      ]
    },
    options: {
      responsive: true,
      maintainAspectRatio: false,
      plugins: {
        legend: {
          position: "top",
          labels: { color: "#94a3b8", font: { size: 10 }, boxWidth: 12 }
        }
      },
      scales: {
        x: { grid: { color: "rgba(255,255,255,0.05)" }, ticks: { color: "#94a3b8" } },
        y: { grid: { color: "rgba(255,255,255,0.05)" }, ticks: { color: "#94a3b8" } }
      }
    }
  });
}

function renderRevYoyChart(revData) {
  const ctx = document.getElementById("revYoyChart");
  if (!ctx || !revData) return;
  if (revYoyChart) revYoyChart.destroy();

  const labels = revData.map(d => d.month);
  const revs = revData.map(d => d.rev);
  const yoys = revData.map(d => d.yoy);

  revYoyChart = new Chart(ctx, {
    data: {
      labels: labels,
      datasets: [
        {
          type: "bar",
          label: "單月營收 (億元)",
          data: revs,
          backgroundColor: "rgba(56, 189, 248, 0.4)",
          borderColor: "#38bdf8",
          borderWidth: 1,
          yAxisID: "y"
        },
        {
          type: "line",
          label: "營收年增率 YoY (%)",
          data: yoys,
          borderColor: "#00f090",
          backgroundColor: "rgba(0, 240, 144, 0.1)",
          borderWidth: 2.5,
          pointBackgroundColor: "#00f090",
          pointRadius: 4,
          yAxisID: "y1"
        }
      ]
    },
    options: {
      responsive: true,
      maintainAspectRatio: false,
      plugins: {
        legend: {
          position: "top",
          labels: { color: "#94a3b8", font: { size: 10 }, boxWidth: 12 }
        }
      },
      scales: {
        x: { grid: { color: "rgba(255,255,255,0.05)" }, ticks: { color: "#94a3b8" } },
        y: {
          type: "linear",
          position: "left",
          grid: { color: "rgba(255,255,255,0.05)" },
          ticks: { color: "#94a3b8" }
        },
        y1: {
          type: "linear",
          position: "right",
          grid: { drawOnChartArea: false },
          ticks: { color: "#00f090", callback: v => v + "%" }
        }
      }
    }
  });
}

function renderMarginsChart(marginData) {
  const ctx = document.getElementById("marginsChart");
  if (!ctx || !marginData) return;
  if (marginsChart) marginsChart.destroy();

  const labels = marginData.map(d => d.quarter);
  const gross = marginData.map(d => d.gross);
  const op = marginData.map(d => d.op);
  const net = marginData.map(d => d.net);

  marginsChart = new Chart(ctx, {
    type: "line",
    data: {
      labels: labels,
      datasets: [
        {
          label: "營業毛利率 (%)",
          data: gross,
          borderColor: "#facc15",
          backgroundColor: "rgba(250, 204, 21, 0.1)",
          borderWidth: 2,
          pointRadius: 4,
          tension: 0.2
        },
        {
          label: "營業利益率 (%)",
          data: op,
          borderColor: "#38bdf8",
          backgroundColor: "rgba(56, 189, 248, 0.1)",
          borderWidth: 2,
          pointRadius: 4,
          tension: 0.2
        },
        {
          label: "稅後淨利率 (%)",
          data: net,
          borderColor: "#00f090",
          backgroundColor: "rgba(0, 240, 144, 0.1)",
          borderWidth: 2,
          pointRadius: 4,
          tension: 0.2
        }
      ]
    },
    options: {
      responsive: true,
      maintainAspectRatio: false,
      plugins: {
        legend: {
          position: "top",
          labels: { color: "#94a3b8", font: { size: 10 }, boxWidth: 12 }
        }
      },
      scales: {
        x: { grid: { color: "rgba(255,255,255,0.05)" }, ticks: { color: "#94a3b8" } },
        y: {
          grid: { color: "rgba(255,255,255,0.05)" },
          ticks: { color: "#94a3b8", callback: v => v + "%" }
        }
      }
    }
  });
}

// ==========================================
// 🏆 盤後五維度綜合量化排名表
// ==========================================
function filterPostMarketRankings(criteria) {
  renderPostMarketRankings(criteria);
}
window.filterPostMarketRankings = filterPostMarketRankings;

function renderPostMarketRankings(criteria = "composite") {
  const tbody = document.getElementById("postmarket-rank-tbody");
  if (!tbody) return;

  const rawList = window.STATIC_POST_MARKET_RANKINGS || [];
  let list = [...rawList];

  if (criteria === "momentum") {
    list.sort((a, b) => b.momentum - a.momentum);
  } else if (criteria === "quality") {
    list.sort((a, b) => b.quality - a.quality);
  } else if (criteria === "value") {
    list.sort((a, b) => b.chips - a.chips);
  } else {
    list.sort((a, b) => b.composite_score - a.composite_score);
  }

  tbody.innerHTML = "";
  list.forEach((item, idx) => {
    const tr = document.createElement("tr");
    tr.innerHTML = `
      <td style="text-align: center; font-weight: 700; color: ${idx < 3 ? '#00f090' : '#94a3b8'};">
        ${idx + 1}
      </td>
      <td style="cursor: pointer;" onclick="openStockFundamentalAnalysis('${item.symbol}')" title="點擊直接開啟深度分析">
        <div class="sym-cell-box">
          <span class="sym-code" style="color: #38bdf8; font-weight: 700;">${item.symbol} 🔍</span>
          <span class="sym-name" style="color: #fff; font-weight: 600;">${item.name}</span>
        </div>
      </td>
      <td style="font-weight: 600;">$ ${(item.price || 0).toLocaleString()}</td>
      <td style="font-weight: 700; color: #00f090; font-size: 1.05rem;">${item.composite_score} 分</td>
      <td><span class="badge" style="background: rgba(56, 189, 248, 0.15); color: #38bdf8;">${item.momentum}</span></td>
      <td><span class="badge" style="background: rgba(0, 240, 144, 0.15); color: #00f090;">${item.chips}</span></td>
      <td><span class="badge" style="background: rgba(250, 204, 21, 0.15); color: #facc15;">${item.quality}</span></td>
      <td><span class="badge" style="background: rgba(255,255,255,0.06); color: ${item.pe_level_color || '#fff'}; font-weight: 600;">${item.pe_level}</span></td>
      <td style="color: #94a3b8;">${item.atr_20}</td>
      <td style="color: #38bdf8; font-weight: 500;">${item.suggested_pos}</td>
      <td style="font-size: 0.8rem; color: #cbd5e1; max-width: 220px;">${item.verdict}</td>
      <td>
        <button class="sub-tab-btn" style="padding: 4px 8px; font-size: 0.75rem; color: #38bdf8; border: 1px solid rgba(56, 189, 248, 0.4); background: rgba(56, 189, 248, 0.1); cursor: pointer;" onclick="openStockFundamentalAnalysis('${item.symbol}')">
          🔍 深度分析
        </button>
      </td>
    `;
    tbody.appendChild(tr);
  });
}
window.renderPostMarketRankings = renderPostMarketRankings;
"""

if "function openStockFundamentalAnalysis" not in js:
    js += core_functions

with open(app_js_path, "w", encoding="utf-8") as f:
    f.write(js)

print("Updated TOOL_Stock_Portfolio_Tracker/web/app.js successfully!")

