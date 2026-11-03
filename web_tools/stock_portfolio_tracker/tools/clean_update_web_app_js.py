# -*- coding: utf-8 -*-
import os

js_path = "web_tools/stock_portfolio_tracker/js/app.js"
with open(js_path, "r", encoding="utf-8") as f:
    js = f.read()

# 1. 替換 switchView
new_switch_view = """let peRiverChart = null;
let revYoyChart = null;
let marginsChart = null;

function switchView(viewName) {
  const views = {
    portfolio: document.getElementById("view-portfolio"),
    rankings: document.getElementById("view-rankings"),
    fundamental: document.getElementById("view-fundamental"),
    quant: document.getElementById("view-quant")
  };
  const tabs = {
    portfolio: document.getElementById("tab-portfolio"),
    rankings: document.getElementById("tab-rankings"),
    fundamental: document.getElementById("tab-fundamental"),
    quant: document.getElementById("tab-quant")
  };

  for (let key in views) {
    if (views[key]) views[key].style.display = (key === viewName) ? "block" : "none";
  }
  for (let key in tabs) {
    if (tabs[key]) {
      if (key === viewName) tabs[key].classList.add("active");
      else tabs[key].classList.remove("active");
    }
  }

  if (viewName === "rankings") {
    renderRankingsTable("composite");
  } else if (viewName === "fundamental") {
    const sel = document.getElementById("fundamental-stock-selector");
    const sym = sel ? sel.value : "2330.TW";
    renderFundamentalAnalysis(sym);
  } else if (viewName === "quant") {
    const selector = document.getElementById("quant-stock-selector");
    if (selector && selector.value) {
      loadQuantWarRoom(selector.value);
    }
  }
}"""

# 替換舊 switchView
import re
js = re.sub(r'let peRiverChart[\s\S]*?function switchView\(viewName\)[\s\S]*?^\}', new_switch_view, js, flags=re.MULTILINE)

# 2. 修改持股列表渲染，加入點擊各股分析與「🔍 深度分析」按鈕
old_tr_inner = """      <td>
        <button class="btn-sm-action" onclick="analyzeStock('${pos.symbol}', '${pos.name}')">AI 診斷</button>
        <button class="btn-sm-action" style="background: rgba(244, 63, 94, 0.2); color: #f43f5e; margin-left: 0.3rem;" onclick="deletePosition(${pos.id})">刪除</button>
      </td>"""

new_tr_inner = """      <td>
        <button class="btn-sm-action" style="background: rgba(56, 189, 248, 0.15); color: #38bdf8; border: 1px solid rgba(56,189,248,0.4);" onclick="openStockAnalysis('${pos.symbol}')">🔍 深度分析</button>
        <button class="btn-sm-action" onclick="analyzeStock('${pos.symbol}', '${pos.name}')">AI 診斷</button>
        <button class="btn-sm-action" style="background: rgba(244, 63, 94, 0.2); color: #f43f5e; margin-left: 0.3rem;" onclick="deletePosition(${pos.id})">刪除</button>
      </td>"""

if old_tr_inner in js:
    js = js.replace(old_tr_inner, new_tr_inner)

# 點擊 stock-tag 或 stock-name 也能直接導入分析
old_stock_col = """      <td>
        <div class="stock-tag">${pos.symbol}</div>
        <div class="stock-name">${pos.name}</div>
      </td>"""

new_stock_col = """      <td style="cursor: pointer;" onclick="openStockAnalysis('${pos.symbol}')" title="點擊直接開啟個股深度分析與價值河流圖">
        <div class="stock-tag" style="display: inline-flex; align-items: center; gap: 4px;">
          ${pos.symbol} <span style="font-size: 0.7rem; color: #38bdf8;">🔍</span>
        </div>
        <div class="stock-name" style="color: #f8fafc; font-weight: 600;">${pos.name}</div>
      </td>"""

if old_stock_col in js:
    js = js.replace(old_stock_col, new_stock_col)

# 3. 實作 openStockAnalysis, filterRankings, renderRankingsTable, renderFundamentalAnalysis
extra_funcs = """

// ==========================================
// 🔍 個股分析跳轉與盤後排名核心
// ==========================================
function openStockAnalysis(symbol) {
  if (!symbol) return;
  const cleanSym = symbol.includes(".TW") ? symbol : (symbol + ".TW");
  
  // 切換至基本面視圖
  switchView("fundamental");

  // 更新下拉選單
  const sel = document.getElementById("fundamental-stock-selector");
  if (sel) {
    let exists = false;
    for (let i = 0; i < sel.options.length; i++) {
      if (sel.options[i].value === cleanSym || sel.options[i].value.startsWith(cleanSym.replace(".TW", ""))) {
        sel.selectedIndex = i;
        exists = true;
        break;
      }
    }
    if (!exists) {
      const opt = document.createElement("option");
      opt.value = cleanSym;
      opt.innerText = cleanSym + " (自選深入分析)";
      sel.appendChild(opt);
      sel.value = cleanSym;
    }
  }

  // 執行基本面渲染
  renderFundamentalAnalysis(cleanSym);

  // 畫面平滑捲動至頂部
  window.scrollTo({ top: 0, behavior: "smooth" });
}

function filterRankings(criteria) {
  renderRankingsTable(criteria);
}

function renderRankingsTable(criteria = "composite") {
  const tbody = document.getElementById("rankings-tbody");
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
      <td style="cursor: pointer;" onclick="openStockAnalysis('${item.symbol}')" title="點擊直接導入深度分析">
        <div class="stock-tag" style="display: inline-flex; align-items: center; gap: 4px;">
          ${item.symbol} <span style="font-size: 0.7rem; color: #38bdf8;">🔍</span>
        </div>
        <div class="stock-name" style="font-weight: 600; color: #fff;">${item.name}</div>
      </td>
      <td style="font-weight: 600;">NT$ ${formatNumber(item.price)}</td>
      <td style="font-weight: 700; color: #00f090; font-size: 1.05rem;">${item.composite_score} 分</td>
      <td><span class="badge" style="background: rgba(56, 189, 248, 0.15); color: #38bdf8;">${item.momentum}</span></td>
      <td><span class="badge" style="background: rgba(0, 240, 144, 0.15); color: #00f090;">${item.chips}</span></td>
      <td><span class="badge" style="background: rgba(250, 204, 21, 0.15); color: #facc15;">${item.quality}</span></td>
      <td><span class="badge" style="background: rgba(255,255,255,0.06); color: ${item.pe_level_color || '#fff'}; font-weight: 600;">${item.pe_level}</span></td>
      <td style="color: #94a3b8;">${item.atr_20}</td>
      <td style="color: #38bdf8; font-weight: 500;">${item.suggested_pos}</td>
      <td style="font-size: 0.8rem; color: #cbd5e1; max-width: 220px;">${item.verdict}</td>
      <td>
        <button class="btn-sm-action" style="background: rgba(56, 189, 248, 0.2); color: #38bdf8; border: 1px solid rgba(56, 189, 248, 0.4);" onclick="openStockAnalysis('${item.symbol}')">
          🔍 深度分析
        </button>
      </td>
    `;
    tbody.appendChild(tr);
  });
}

// ==========================================
// 🌊 個股基本面深度分析 (Fundamental & River Bands)
// ==========================================
function renderFundamentalAnalysis(symbol) {
  if (!window.STATIC_FUNDAMENTAL_DATA) return;
  const data = window.STATIC_FUNDAMENTAL_DATA[symbol] || 
               window.STATIC_FUNDAMENTAL_DATA[symbol.replace(".TW", "") + ".TW"] ||
               window.STATIC_FUNDAMENTAL_DATA["2330.TW"];
  if (!data) return;

  const priceEl = document.getElementById("fd-price");
  const peEl = document.getElementById("fd-pe");
  const epsEl = document.getElementById("fd-eps");
  const badgeEl = document.getElementById("fd-status-badge");

  if (priceEl) priceEl.innerText = formatNumber(data.current_price) + " 元";
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
"""

# 清除舊的 statementdog 函數，加入新的乾淨函數
js = re.sub(r'// ==========================================\s*// 🐶 財報狗[\s\S]*?renderMarginsChart\(data\.margins\);\s*\}', '', js, flags=re.MULTILINE)
js += extra_funcs

# 在 DOMContentLoaded 裡面加入 renderRankingsTable 初始化
js = js.replace('loadPortfolio();', 'loadPortfolio();\n  renderRankingsTable();')

with open(js_path, "w", encoding="utf-8") as f:
    f.write(js)

print("Updated app.js cleanly!")

