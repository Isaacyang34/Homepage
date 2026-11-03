# -*- coding: utf-8 -*-
import os

js_path = "web_tools/stock_portfolio_tracker/js/app.js"
with open(js_path, "r", encoding="utf-8") as f:
    js = f.read()

# 1. 替換 switchView
old_switch_view = """function switchView(viewName) {
  const pView = document.getElementById("view-portfolio");
  const qView = document.getElementById("view-quant");
  const pTab = document.getElementById("tab-portfolio");
  const qTab = document.getElementById("tab-quant");

  if (viewName === "portfolio") {
    pView.style.display = "block";
    qView.style.display = "none";
    pTab.classList.add("active");
    qTab.classList.remove("active");
  } else {
    pView.style.display = "none";
    qView.style.display = "block";
    pTab.classList.remove("active");
    qTab.classList.add("active");
    
    // 如果有選中股票，載入量化戰情
    const selector = document.getElementById("quant-stock-selector");
    if (selector.value) {
      loadQuantWarRoom(selector.value);
    }
  }
}"""

new_switch_view = """let peRiverChart = null;
let revYoyChart = null;
let marginsChart = null;

function switchView(viewName) {
  const views = {
    portfolio: document.getElementById("view-portfolio"),
    quant: document.getElementById("view-quant"),
    statementdog: document.getElementById("view-statementdog"),
    funnel: document.getElementById("view-funnel")
  };
  const tabs = {
    portfolio: document.getElementById("tab-portfolio"),
    quant: document.getElementById("tab-quant"),
    statementdog: document.getElementById("tab-statementdog"),
    funnel: document.getElementById("tab-funnel")
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

  if (viewName === "quant") {
    const selector = document.getElementById("quant-stock-selector");
    if (selector && selector.value) {
      loadQuantWarRoom(selector.value);
    }
  } else if (viewName === "statementdog") {
    const sdSelector = document.getElementById("statementdog-stock-selector");
    const sym = sdSelector ? sdSelector.value : "2330.TW";
    renderStatementDog(sym);
  }
}"""

if "function switchView(viewName)" in js:
    js = js.replace(old_switch_view, new_switch_view)

# 2. 新增 renderStatementDog 實作
statementdog_funcs = """

// ==========================================
// 🐶 財報狗風格個股深度分析 (StatementDog Style)
// ==========================================
function renderStatementDog(symbol) {
  if (!window.STATIC_STATEMENTDOG_DATA) return;
  const data = window.STATIC_STATEMENTDOG_DATA[symbol] || window.STATIC_STATEMENTDOG_DATA["2330.TW"];
  if (!data) return;

  // 1. 更新頂部指標
  const priceEl = document.getElementById("sd-price");
  const peEl = document.getElementById("sd-pe");
  const epsEl = document.getElementById("sd-eps");
  const badgeEl = document.getElementById("sd-status-badge");

  if (priceEl) priceEl.innerText = formatNumber(data.current_price) + " 元";
  if (peEl) peEl.innerText = data.current_pe + " 倍";
  if (epsEl) epsEl.innerText = data.eps_ttm + " 元";
  if (badgeEl) {
    badgeEl.innerText = data.pe_status;
    badgeEl.style.color = data.pe_color || "#38bdf8";
  }

  // 2. 更新杜邦分析數值
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

  // 3. 更新安全性指標
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

  // 4. 繪製本益比河流圖 (P/E River Band)
  renderPeRiverChart(data.river_bands);

  // 5. 繪製月營收與年增率 (YoY)
  renderRevYoyChart(data.revenue_yoy);

  // 6. 繪製獲利三率
  renderMarginsChart(data.margins);
}

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
          label: "實際股價",
          data: riverData.prices,
          borderColor: "#ffffff",
          borderWidth: 3,
          pointBackgroundColor: "#ffffff",
          pointRadius: 5,
          tension: 0.2,
          order: 1
        },
        {
          label: "32x PE (昂貴)",
          data: riverData.band_32x,
          borderColor: "rgba(244, 63, 94, 0.7)",
          backgroundColor: "rgba(244, 63, 94, 0.12)",
          fill: "+1",
          borderWidth: 1.5,
          pointRadius: 0,
          tension: 0.2
        },
        {
          label: "28x PE (偏高)",
          data: riverData.band_28x,
          borderColor: "rgba(251, 146, 60, 0.7)",
          backgroundColor: "rgba(251, 146, 60, 0.12)",
          fill: "+1",
          borderWidth: 1.5,
          pointRadius: 0,
          tension: 0.2
        },
        {
          label: "24x PE (合理)",
          data: riverData.band_24x,
          borderColor: "rgba(250, 204, 21, 0.7)",
          backgroundColor: "rgba(250, 204, 21, 0.12)",
          fill: "+1",
          borderWidth: 1.5,
          pointRadius: 0,
          tension: 0.2
        },
        {
          label: "20x PE (便宜)",
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
"""

if "renderStatementDog" not in js:
    js += statementdog_funcs

with open(js_path, "w", encoding="utf-8") as f:
    f.write(js)

print("Updated app.js successfully!")

