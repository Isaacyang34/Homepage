
// 靜態 GitHub Pages 適配器
const originalFetch = window.fetch;
window.fetch = async function(url, options) {
  if (window.IS_STATIC_CLOUD) {
    if (url.startsWith('/api/portfolio')) {
      return {
        ok: true,
        json: async () => window.STATIC_PORTFOLIO_DATA
      };
    }
    if (url.startsWith('/api/analysis/')) {
      const sym = url.split('/').pop();
      const cached = window.STATIC_ANALYSIS_DATA[sym] || window.STATIC_ANALYSIS_DATA[Object.keys(window.STATIC_ANALYSIS_DATA)[0]];
      return {
        ok: true,
        json: async () => cached
      };
    }
  }
  return originalFetch(url, options);
};

let allocationChart = null;
let radarChart = null;
let quantRadarChart = null;
let backtestTrendChart = null;
let currentPositions = [];

document.addEventListener("DOMContentLoaded", () => {
  loadPortfolio();
  renderRankingsTable();
  initBacktestChart();
});

let peRiverChart = null;
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
}

async function loadPortfolio() {
  try {
    const res = await fetch("/api/portfolio");
    if (!res.ok) throw new Error("載入投資組合失敗");
    const data = await res.json();
    currentPositions = data.positions || [];
    renderSummary(data.summary);
    renderTable(data.positions);
    renderAllocationChart(data.positions, data.summary.cash_balance);
    populateQuantSelector(data.positions);

    // 預設診斷第一檔股票
    if (currentPositions.length > 0) {
      analyzeStock(currentPositions[0].symbol, currentPositions[0].name);
      loadQuantWarRoom(currentPositions[0].symbol);
    }
  } catch (err) {
    console.error(err);
  }
}

function renderSummary(summary) {
  document.getElementById("kpi-total-equity").innerText = `NT$ ${formatNumber(summary.total_equity)}`;
  document.getElementById("kpi-cash").innerText = `NT$ ${formatNumber(summary.cash_balance)}`;
  document.getElementById("kpi-market-value").innerText = `NT$ ${formatNumber(summary.total_market_value)}`;
  document.getElementById("kpi-total-cost").innerText = `NT$ ${formatNumber(summary.total_cost)}`;
  
  const pnlEl = document.getElementById("kpi-unrealized-pnl");
  const roiEl = document.getElementById("kpi-roi-badge");
  const isUp = summary.total_unrealized_pnl >= 0;
  
  pnlEl.innerText = `${isUp ? "+" : ""}NT$ ${formatNumber(summary.total_unrealized_pnl)}`;
  pnlEl.className = `kpi-value ${isUp ? "color-up" : "color-down"}`;

  roiEl.innerHTML = `報酬率: <strong class="${isUp ? "color-up" : "color-down"}">${isUp ? "+" : ""}${summary.total_roi}%</strong>`;
  document.getElementById("kpi-count").innerText = `${summary.position_count} 檔`;
}

function renderTable(positions) {
  const tbody = document.getElementById("positions-tbody");
  tbody.innerHTML = "";

  if (!positions || positions.length === 0) {
    tbody.innerHTML = `<tr><td colspan="8" style="text-align: center; color: var(--text-muted); padding: 2rem;">目前尚無持股，請點擊右上角「新增持股」！</td></tr>`;
    return;
  }

  positions.forEach(pos => {
    const isUp = pos.unrealized_pnl >= 0;
    const tr = document.createElement("tr");
    tr.innerHTML = `
      <td style="cursor: pointer;" onclick="openStockAnalysis('${pos.symbol}')" title="點擊直接開啟個股深度分析與價值河流圖">
        <div class="stock-tag" style="display: inline-flex; align-items: center; gap: 4px;">
          ${pos.symbol} <span style="font-size: 0.7rem; color: #38bdf8;">🔍</span>
        </div>
        <div class="stock-name" style="color: #f8fafc; font-weight: 600;">${pos.name}</div>
      </td>
      <td>${formatNumber(pos.shares)}</td>
      <td>NT$ ${formatNumber(pos.cost_price)}</td>
      <td style="font-weight: 600;">NT$ ${formatNumber(pos.current_price)}</td>
      <td>NT$ ${formatNumber(pos.market_value)}</td>
      <td class="${isUp ? "color-up" : "color-down"}" style="font-weight: 600;">
        ${isUp ? "+" : ""}NT$ ${formatNumber(pos.unrealized_pnl)}
        <div style="font-size: 0.75rem;">(${isUp ? "+" : ""}${pos.pnl_percentage}%)</div>
      </td>
      <td style="color: var(--accent-cyan); font-weight: 500;">${pos.weight_percentage}%</td>
      <td>
        <button class="btn-sm-action" onclick="analyzeStock('${pos.symbol}', '${pos.name}')">AI 診斷</button>
        <button class="btn-sm-delete" onclick="deletePosition(${pos.id})">刪除</button>
      </td>
    `;
    tbody.appendChild(tr);
  });
}

function populateQuantSelector(positions) {
  const sel = document.getElementById("quant-stock-selector");
  sel.innerHTML = "";
  positions.forEach(p => {
    const opt = document.createElement("option");
    opt.value = p.symbol;
    opt.innerText = `${p.symbol} ${p.name}`;
    sel.appendChild(opt);
  });
}

function onQuantStockChange(symbol) {
  loadQuantWarRoom(symbol);
}

async function loadQuantWarRoom(symbol) {
  try {
    const res = await fetch(`/api/analysis/${symbol}`);
    if (!res.ok) throw new Error("獲取量化戰情失敗");
    const data = await res.json();
    
    // 更新辯論面板
    const bull = data.debate.bull_agent;
    document.getElementById("quant-bull-confidence").innerText = `信心度: ${bull.confidence}%`;
    document.getElementById("quant-bull-arguments").innerHTML = bull.arguments.map(arg => `<li>${arg}</li>`).join("");

    const bear = data.debate.bear_agent;
    document.getElementById("quant-bear-confidence").innerText = `信心度: ${bear.confidence}%`;
    document.getElementById("quant-bear-arguments").innerHTML = bear.arguments.map(arg => `<li>${arg}</li>`).join("");

    const arb = data.debate.arbitrator;
    document.getElementById("quant-verdict-action").innerText = arb.verdict;
    document.getElementById("quant-verdict-rationale").innerText = arb.rationale;
    document.getElementById("quant-verdict-target").innerText = `NT$ ${arb.target_price}`;
    document.getElementById("quant-verdict-stop").innerText = `NT$ ${arb.stop_loss}`;
    document.getElementById("quant-verdict-size").innerText = arb.position_sizing;

    // 繪製雷達圖
    renderQuantRadarChart(data.factors.radar_factors);
  } catch (err) {
    console.error(err);
  }
}

function renderQuantRadarChart(factorMap) {
  const ctx = document.getElementById("quantRadarChart").getContext("2d");
  const labels = Object.keys(factorMap);
  const scores = Object.values(factorMap);

  if (quantRadarChart) {
    quantRadarChart.destroy();
  }

  quantRadarChart = new Chart(ctx, {
    type: "radar",
    data: {
      labels: labels,
      datasets: [{
        label: "因子強度 (0-100)",
        data: scores,
        backgroundColor: "rgba(129, 140, 248, 0.25)",
        borderColor: "#818cf8",
        pointBackgroundColor: "#818cf8",
        borderWidth: 2
      }]
    },
    options: {
      responsive: true,
      maintainAspectRatio: false,
      scales: {
        r: {
          min: 0,
          max: 100,
          ticks: { display: false, stepSize: 20 },
          grid: { color: "rgba(255, 255, 255, 0.08)" },
          angleLines: { color: "rgba(255, 255, 255, 0.08)" },
          pointLabels: { color: "#cbd5e1", font: { size: 10 } }
        }
      },
      plugins: {
        legend: { display: false }
      }
    }
  });
}

function initBacktestChart() {
  const ctx = document.getElementById("backtestTrendChart").getContext("2d");
  const labels = ["1月", "2月", "3月", "4月", "5月", "6月", "7月", "8月", "9月", "10月"];
  const strategyData = [100, 104, 109, 107, 115, 122, 119, 128, 131, 138];
  const benchmarkData = [100, 102, 105, 103, 106, 110, 108, 114, 115, 118];

  if (backtestTrendChart) {
    backtestTrendChart.destroy();
  }

  backtestTrendChart = new Chart(ctx, {
    type: "line",
    data: {
      labels: labels,
      datasets: [
        {
          label: "量化多空複合策略",
          data: strategyData,
          borderColor: "#38bdf8",
          backgroundColor: "rgba(56, 189, 248, 0.1)",
          fill: true,
          tension: 0.3
        },
        {
          label: "台灣加權基準 (TWSE)",
          data: benchmarkData,
          borderColor: "#64748b",
          borderDash: [5, 5],
          fill: false,
          tension: 0.3
        }
      ]
    },
    options: {
      responsive: true,
      maintainAspectRatio: false,
      scales: {
        x: { grid: { color: "rgba(255, 255, 255, 0.05)" }, ticks: { color: "#94a3b8" } },
        y: { grid: { color: "rgba(255, 255, 255, 0.05)" }, ticks: { color: "#94a3b8" } }
      },
      plugins: {
        legend: { labels: { color: "#94a3b8", boxWidth: 12 } }
      }
    }
  });
}

function renderAllocationChart(positions, cash) {
  const ctx = document.getElementById("allocationChart").getContext("2d");
  const labels = positions.map(p => `${p.name} (${p.symbol})`);
  const data = positions.map(p => p.market_value);

  if (cash > 0) {
    labels.push("現金儲備");
    data.push(cash);
  }

  const bgColors = [
    "#38bdf8", "#818cf8", "#f43f5e", "#10b981", "#fbbf24", "#a855f7", "#ec4899", "#64748b"
  ];

  if (allocationChart) {
    allocationChart.destroy();
  }

  allocationChart = new Chart(ctx, {
    type: "doughnut",
    data: {
      labels: labels,
      datasets: [{
        data: data,
        backgroundColor: bgColors.slice(0, data.length),
        borderWidth: 0
      }]
    },
    options: {
      responsive: true,
      maintainAspectRatio: false,
      plugins: {
        legend: {
          position: "right",
          labels: { color: "#94a3b8", boxWidth: 12, font: { size: 11 } }
        }
      },
      cutout: "65%"
    }
  });
}

async function analyzeStock(symbol, name) {
  const panel = document.getElementById("analysis-panel");
  panel.style.display = "block";
  document.getElementById("target-stock-title").innerText = `${symbol} ${name}`;

  try {
    const res = await fetch(`/api/analysis/${symbol}`);
    if (!res.ok) throw new Error("獲取 AI 診斷失敗");
    const data = await res.json();
    
    document.getElementById("target-stock-score").innerText = `綜合因子評分: ${data.factors.overall_score} (${data.factors.rating})`;
    renderRadarChart(data.factors.radar_factors);
  } catch (err) {
    console.error(err);
  }
}

function renderRadarChart(factorMap) {
  const ctx = document.getElementById("radarChart").getContext("2d");
  const labels = Object.keys(factorMap);
  const scores = Object.values(factorMap);

  if (radarChart) {
    radarChart.destroy();
  }

  radarChart = new Chart(ctx, {
    type: "radar",
    data: {
      labels: labels,
      datasets: [{
        label: "因子強度",
        data: scores,
        backgroundColor: "rgba(56, 189, 248, 0.2)",
        borderColor: "#38bdf8",
        pointBackgroundColor: "#38bdf8",
        borderWidth: 2
      }]
    },
    options: {
      responsive: true,
      maintainAspectRatio: false,
      scales: {
        r: {
          min: 0,
          max: 100,
          ticks: { display: false, stepSize: 20 },
          grid: { color: "rgba(255, 255, 255, 0.08)" },
          angleLines: { color: "rgba(255, 255, 255, 0.08)" },
          pointLabels: { color: "#cbd5e1", font: { size: 10 } }
        }
      },
      plugins: { legend: { display: false } }
    }
  });
}

function openAddModal() {
  document.getElementById("addModal").style.display = "flex";
}

function closeAddModal() {
  document.getElementById("addModal").style.display = "none";
  document.getElementById("addPositionForm").reset();
}

async function submitPosition(e) {
  e.preventDefault();
  const payload = {
    symbol: document.getElementById("input-symbol").value.trim(),
    name: document.getElementById("input-name").value.trim() || null,
    shares: parseInt(document.getElementById("input-shares").value),
    cost_price: parseFloat(document.getElementById("input-cost").value),
    notes: document.getElementById("input-notes").value.trim() || null
  };

  try {
    const res = await fetch("/api/portfolio", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(payload)
    });
    if (!res.ok) throw new Error("新增失敗");
    closeAddModal();
    loadPortfolio();
  renderRankingsTable();
  } catch (err) {
    alert("新增失敗: " + err.message);
  }
}

async function deletePosition(id) {
  if (!confirm("確定要刪除這筆持股嗎？")) return;
  try {
    const res = await fetch(`/api/portfolio/${id}`, { method: "DELETE" });
    if (!res.ok) throw new Error("刪除失敗");
    loadPortfolio();
  renderRankingsTable();
  } catch (err) {
    alert("刪除失敗: " + err.message);
  }
}

function exportAndSyncToGitHub() {
  alert("正在為您打包雙功能雲端戰情室... 請稍候，或執行專案目錄下的「同步推播至GitHub.bat」！");
}

function formatNumber(num) {
  if (num === null || num === undefined) return "0";
  return Number(num).toLocaleString("en-US", { minimumFractionDigits: 1, maximumFractionDigits: 1 });
}


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
