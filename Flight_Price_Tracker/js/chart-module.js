/**
 * chart-module.js — Chart.js 趨勢圖模組
 */

const ChartModule = (() => {
  let priceChart = null;

  const CHART_COLORS = {
    purple:    { border: '#6366f1', bg: 'rgba(99,102,241,0.15)' },
    cyan:      { border: '#22d3ee', bg: 'rgba(34,211,238,0.12)' },
    pink:      { border: '#ec4899', bg: 'rgba(236,72,153,0.10)' },
    green:     { border: '#10b981', bg: 'rgba(16,185,129,0.10)' },
    amber:     { border: '#f59e0b', bg: 'rgba(245,158,11,0.10)' },
    slate:     { border: '#64748b', bg: 'rgba(100,116,139,0.10)' },
  };
  const COLOR_KEYS = Object.keys(CHART_COLORS);

  /* 共用 Chart.js 全域預設 */
  const applyDefaults = () => {
    Chart.defaults.color = '#94a3b8';
    Chart.defaults.borderColor = 'rgba(255,255,255,0.06)';
    Chart.defaults.font.family = "'Inter', sans-serif";
  };

  /* ========== 主趨勢折線圖 ========== */
  const renderTrendChart = (canvasId, results, mode = 'total') => {
    applyDefaults();
    const ctx = document.getElementById(canvasId);
    if (!ctx) return;

    if (priceChart) { priceChart.destroy(); priceChart = null; }

    // 用最佳方案的日期標籤
    const labels = results[0]?.priceHistory.map(h => h.label) || [];

    const datasets = results.slice(0, 4).map((r, i) => {
      const color = CHART_COLORS[COLOR_KEYS[i]];
      const data = r.priceHistory.map(h => {
        if (mode === 'flight')  return Math.round(h.price * (r.flightTotal / r.totalPrice));
        if (mode === 'hotel')   return Math.round(h.price * (r.hotelTotal  / r.totalPrice));
        return h.price; // total
      });

      return {
        label: `${r.airline.name} (${r.platform.name})`,
        data,
        borderColor: color.border,
        backgroundColor: color.bg,
        fill: true,
        tension: 0.4,
        borderWidth: 2,
        pointRadius: 4,
        pointHoverRadius: 7,
        pointBackgroundColor: color.border,
        pointBorderColor: '#050810',
        pointBorderWidth: 2,
      };
    });

    priceChart = new Chart(ctx, {
      type: 'line',
      data: { labels, datasets },
      options: {
        responsive: true,
        maintainAspectRatio: false,
        interaction: { mode: 'index', intersect: false },
        plugins: {
          legend: {
            position: 'top',
            align: 'start',
            labels: {
              usePointStyle: true,
              pointStyle: 'circle',
              padding: 20,
              font: { size: 12 },
            },
          },
          tooltip: {
            backgroundColor: 'rgba(10,14,26,0.95)',
            borderColor: 'rgba(99,102,241,0.4)',
            borderWidth: 1,
            padding: 14,
            titleColor: '#f1f5f9',
            bodyColor: '#94a3b8',
            callbacks: {
              label: (ctx) => ` ${ctx.dataset.label}: NT$${ctx.parsed.y.toLocaleString()}`,
            },
          },
        },
        scales: {
          x: {
            grid: { color: 'rgba(255,255,255,0.04)' },
            ticks: { font: { size: 11 } },
          },
          y: {
            grid: { color: 'rgba(255,255,255,0.04)' },
            ticks: {
              font: { size: 11 },
              callback: (v) => `NT$${(v / 1000).toFixed(0)}K`,
            },
          },
        },
        animation: {
          duration: 800,
          easing: 'easeInOutQuart',
        },
      },
    });

    // 標記最低價點
    annotateLowest(priceChart, results[0]);

    return priceChart;
  };

  /* 最低價標記（Plugin） */
  const annotateLowest = (chart, result) => {
    const prices = result.priceHistory.map(h => h.price);
    const minIdx = prices.indexOf(Math.min(...prices));
    const isToday = minIdx === prices.length - 1;

    // 加入自訂 plugin 標記
    const annotationPlugin = {
      id: 'lowestAnnotation',
      afterDraw(chart) {
        const dataset = chart.data.datasets[0];
        const meta = chart.getDatasetMeta(0);
        if (!meta.data[minIdx]) return;

        const point = meta.data[minIdx];
        const { ctx } = chart;
        ctx.save();

        // 光暈
        ctx.beginPath();
        ctx.arc(point.x, point.y, 12, 0, Math.PI * 2);
        ctx.fillStyle = 'rgba(16,185,129,0.2)';
        ctx.fill();

        // 圓點
        ctx.beginPath();
        ctx.arc(point.x, point.y, 6, 0, Math.PI * 2);
        ctx.fillStyle = '#10b981';
        ctx.fill();

        // 標籤
        const label = isToday ? '🎯 今日最低！' : '📌 歷史最低';
        ctx.font = 'bold 11px Inter, sans-serif';
        ctx.fillStyle = '#10b981';
        ctx.textAlign = 'center';
        ctx.fillText(label, point.x, point.y - 18);

        ctx.restore();
      },
    };

    // 先移除舊的再加新的
    Chart.unregister({ id: 'lowestAnnotation' });
    Chart.register(annotationPlugin);
    chart.update('none');
  };

  /* ========== 小型迷你圖（卡片內） ========== */
  const renderMiniChart = (canvasId, priceHistory, color = '#6366f1') => {
    const ctx = document.getElementById(canvasId);
    if (!ctx) return;

    const prices = priceHistory.map(h => h.price);
    const isUp = prices[prices.length - 1] > prices[0];
    const lineColor = isUp ? '#ef4444' : '#10b981';

    new Chart(ctx, {
      type: 'line',
      data: {
        labels: priceHistory.map(h => h.label),
        datasets: [{
          data: prices,
          borderColor: lineColor,
          backgroundColor: `${lineColor}18`,
          fill: true,
          tension: 0.4,
          borderWidth: 1.5,
          pointRadius: 0,
        }],
      },
      options: {
        responsive: true,
        maintainAspectRatio: false,
        plugins: { legend: { display: false }, tooltip: { enabled: false } },
        scales: { x: { display: false }, y: { display: false } },
        animation: { duration: 600 },
      },
    });
  };

  /* ========== 比較雷達圖 ========== */
  const renderCompareChart = (canvasId, compareItems) => {
    applyDefaults();
    const ctx = document.getElementById(canvasId);
    if (!ctx) return;

    const labels = ['總價', '機票', '住宿', '停靠次數', '評分'];

    // 歸一化分數 (0~100)
    const normalize = (vals, invert = false) => {
      const min = Math.min(...vals), max = Math.max(...vals);
      const range = max - min || 1;
      return vals.map(v => {
        const score = ((v - min) / range) * 80 + 20;
        return invert ? 100 - score + 20 : score;
      });
    };

    const totals   = compareItems.map(c => c.totalPrice);
    const flights  = compareItems.map(c => c.flightTotal);
    const hotels   = compareItems.map(c => c.hotelTotal);
    const stops    = compareItems.map(c => c.outbound.stops);
    const ratings  = compareItems.map(c => c.airline.rating * 20);

    const normTotals  = normalize(totals,  true);
    const normFlights = normalize(flights, true);
    const normHotels  = normalize(hotels,  true);
    const normStops   = normalize(stops,   true);

    const datasets = compareItems.map((item, i) => {
      const color = CHART_COLORS[COLOR_KEYS[i]];
      return {
        label: `${item.airline.name}`,
        data: [
          normTotals[i],
          normFlights[i],
          normHotels[i],
          normStops[i],
          ratings[i],
        ],
        borderColor: color.border,
        backgroundColor: color.bg,
        borderWidth: 2,
        pointBackgroundColor: color.border,
      };
    });

    new Chart(ctx, {
      type: 'radar',
      data: { labels, datasets },
      options: {
        responsive: true,
        maintainAspectRatio: false,
        scales: {
          r: {
            min: 0, max: 100,
            grid: { color: 'rgba(255,255,255,0.06)' },
            pointLabels: { color: '#94a3b8', font: { size: 12 } },
            ticks: { display: false },
          },
        },
        plugins: {
          legend: {
            position: 'bottom',
            labels: { usePointStyle: true, padding: 20, font: { size: 12 } },
          },
        },
        animation: { duration: 700 },
      },
    });
  };

  const destroyAll = () => {
    if (priceChart) { priceChart.destroy(); priceChart = null; }
  };

  return {
    renderTrendChart,
    renderMiniChart,
    renderCompareChart,
    destroyAll,
  };
})();
