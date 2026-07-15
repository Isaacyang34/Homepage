/**
 * app.js — 主控制器
 */

const App = (() => {

  /* ========== 狀態 ========== */
  let state = {
    currentPage: 'home',
    searchResults: [],
    currentFilter: 'total',
    compareList: [],
    trendMode: 'total',
    adults: 2,
    children: 0,
    nights: 5,
    lastSearch: null,
  };

  /* ========== 頁面路由 ========== */
  const navigate = (page) => {
    document.querySelectorAll('.page-section').forEach(el => el.classList.remove('active'));
    document.querySelectorAll('.nav-btn[data-page]').forEach(el => el.classList.remove('active'));

    const section = document.getElementById(`page-${page}`);
    if (section) section.classList.add('active');

    const navBtn = document.querySelector(`.nav-btn[data-page="${page}"]`);
    if (navBtn) navBtn.classList.add('active');

    state.currentPage = page;

    if (page === 'tracker') {
      Tracker.renderTrackedSection('tracker-list');
    }
    if (page === 'history') {
      renderHistory();
    }
    if (page === 'results' && state.searchResults.length) {
      renderStats();
      ChartModule.renderTrendChart('trend-chart', state.searchResults, state.trendMode);
    }
  };

  /* ========== 城市自動補全 ========== */
  const initAutocomplete = (inputId, dropdownId) => {
    const input = document.getElementById(inputId);
    const dropdown = document.getElementById(dropdownId);
    if (!input || !dropdown) return;

    const close = () => {
      dropdown.style.display = 'none';
      dropdown.innerHTML = '';
    };

    input.addEventListener('input', () => {
      const q = input.value.trim();
      if (q.length < 1) { close(); return; }

      const cities = DataEngine.searchCities(q);
      if (!cities.length) { close(); return; }

      dropdown.innerHTML = cities.map(c => `
        <div class="autocomplete-item" data-code="${c.code}" data-name="${c.name}">
          <span class="ac-emoji">${c.emoji}</span>
          <div>
            <div class="ac-name">${c.name} <span class="ac-code">${c.code}</span></div>
            <div class="ac-country">${c.country}</div>
          </div>
        </div>
      `).join('');
      dropdown.style.display = 'block';

      dropdown.querySelectorAll('.autocomplete-item').forEach(item => {
        item.addEventListener('click', () => {
          input.value = `${item.dataset.name} (${item.dataset.code})`;
          input.dataset.code = item.dataset.code;
          close();
        });
      });
    });

    document.addEventListener('click', (e) => {
      if (!input.contains(e.target) && !dropdown.contains(e.target)) close();
    });
  };

  /* ========== 人數控制器 ========== */
  const initQtyControls = () => {
    const setup = (decreaseId, increaseId, displayId, stateKey, min = 0, max = 9) => {
      const dec = document.getElementById(decreaseId);
      const inc = document.getElementById(increaseId);
      const display = document.getElementById(displayId);
      if (!dec || !inc || !display) return;

      display.textContent = state[stateKey];

      dec.addEventListener('click', () => {
        if (state[stateKey] > min) {
          state[stateKey]--;
          display.textContent = state[stateKey];
        }
      });
      inc.addEventListener('click', () => {
        if (state[stateKey] < max) {
          state[stateKey]++;
          display.textContent = state[stateKey];
        }
      });
    };

    setup('adults-dec', 'adults-inc', 'adults-display', 'adults', 1, 9);
    setup('children-dec', 'children-inc', 'children-display', 'children', 0, 6);
    setup('nights-dec', 'nights-inc', 'nights-display', 'nights', 1, 30);
  };

  /* ========== 日期預設值 ========== */
  const initDateDefaults = () => {
    const today = new Date();
    const depart = new Date(today);
    depart.setDate(today.getDate() + 14);
    const ret = new Date(depart);
    ret.setDate(depart.getDate() + state.nights);

    const fmt = (d) => d.toISOString().split('T')[0];
    const departInput = document.getElementById('depart-date');
    const returnInput = document.getElementById('return-date');

    if (departInput) departInput.value = fmt(depart);
    if (returnInput) returnInput.value = fmt(ret);

    // 聯動：出發日期改變時更新回程預設
    if (departInput) {
      departInput.addEventListener('change', () => {
        const d = new Date(departInput.value);
        d.setDate(d.getDate() + state.nights);
        if (returnInput) returnInput.value = fmt(d);
      });
    }
  };

  /* ========== 搜尋處理 ========== */
  const handleSearch = async () => {
    const fromInput = document.getElementById('from-city');
    const toInput   = document.getElementById('to-city');
    const depart    = document.getElementById('depart-date')?.value;
    const ret       = document.getElementById('return-date')?.value;

    const fromCode = fromInput?.dataset.code || extractCode(fromInput?.value || '') || 'TPE';
    const toCode   = toInput?.dataset.code   || extractCode(toInput?.value   || '') || 'NRT';

    if (!fromCode || !toCode) {
      Tracker.showToast('請選擇出發地與目的地', 'warning');
      return;
    }
    if (!depart || !ret) {
      Tracker.showToast('請選擇出發與回程日期', 'warning');
      return;
    }
    if (fromCode === toCode) {
      Tracker.showToast('出發地與目的地不能相同', 'warning');
      return;
    }

    const btn = document.getElementById('search-btn');
    if (btn) {
      btn.classList.add('loading');
      btn.querySelector('.btn-text').textContent = '搜尋中…';
      btn.disabled = true;
    }

    // 模擬網路延遲
    await delay(1200);

    // 計算旅行天數
    const d1 = new Date(depart), d2 = new Date(ret);
    const calcNights = Math.max(1, Math.round((d2 - d1) / (1000 * 60 * 60 * 24)));

    const params = {
      from: fromCode,
      to:   toCode,
      departDate: depart,
      returnDate: ret,
      adults: state.adults,
      children: state.children,
      nights: calcNights,
    };

    const results = DataEngine.generateResults(params);
    state.searchResults = results;
    state.lastSearch = params;

    // 儲存搜尋記錄
    const fromCity = DataEngine.CITIES.find(c => c.code === fromCode);
    const toCity   = DataEngine.CITIES.find(c => c.code === toCode);
    Storage.addSearchHistory({
      from: fromCode,
      to:   toCode,
      fromName: fromCity?.name || fromCode,
      toName:   toCity?.name   || toCode,
      dates: `${depart} ~ ${ret}`,
      adults: state.adults,
      children: state.children,
      nights: calcNights,
    });

    if (btn) {
      btn.classList.remove('loading');
      btn.querySelector('.btn-text').textContent = '🔍 搜尋最優方案';
      btn.disabled = false;
    }

    navigate('results');
    renderResults(results);
    renderStats();
    setTimeout(() => {
      ChartModule.renderTrendChart('trend-chart', results, state.trendMode);
    }, 100);

    Tracker.showToast(`找到 ${results.length} 個方案！`, 'success');
  };

  /* ========== 渲染結果卡片 ========== */
  const renderResults = (results) => {
    const container = document.getElementById('results-container');
    if (!container) return;

    // 顯示搜尋摘要
    const summaryEl = document.getElementById('search-summary');
    if (summaryEl && state.lastSearch) {
      const { fromName, toName, departDate, returnDate, adults, children, nights } = state.searchResults[0]
        ? {
            fromName: state.searchResults[0].fromCity.name,
            toName:   state.searchResults[0].toCity.name,
            departDate: state.searchResults[0].departDate,
            returnDate: state.searchResults[0].returnDate,
            adults: state.searchResults[0].adults,
            children: state.searchResults[0].children,
            nights: state.searchResults[0].nights,
          }
        : {};

      summaryEl.innerHTML = `
        <span class="badge badge-purple">✈️ ${state.searchResults[0]?.fromCity?.name} → ${state.searchResults[0]?.toCity?.name}</span>
        <span class="badge badge-cyan">📅 ${state.lastSearch.departDate} ~ ${state.lastSearch.returnDate}</span>
        <span class="badge badge-purple">🌙 ${state.lastSearch.nights} 晚</span>
        <span class="badge badge-cyan">👤 大人 ${state.lastSearch.adults}${state.lastSearch.children > 0 ? ` · 兒童 ${state.lastSearch.children}` : ''}</span>
      `;
    }

    container.innerHTML = '';

    const filtered = filterAndSort(results, state.currentFilter);

    filtered.forEach((r, idx) => {
      const card = createResultCard(r, idx);
      container.appendChild(card);

      // 迷你圖
      setTimeout(() => {
        ChartModule.renderMiniChart(`mini-chart-${r.id}`, r.priceHistory);
      }, idx * 60);
    });
  };

  const createResultCard = (r, idx) => {
    const el = document.createElement('div');
    el.className = `result-card${r.isBestDeal ? ' best-deal' : ''}`;
    el.id = `card-${r.id}`;
    el.style.animationDelay = `${idx * 0.08}s`;

    const isTracked = Storage.isTracked(r.id);
    const inCompare = state.compareList.some(c => c.id === r.id);
    const priceChangeHTML = r.priceChange !== 0
      ? `<div class="price-change ${r.priceChange > 0 ? 'up' : 'down'}">
           ${r.priceChange > 0 ? '▲' : '▼'} NT$${Math.abs(r.priceChange).toLocaleString()} 較昨日
         </div>`
      : '';

    el.innerHTML = `
      <div class="card-top">
        <div class="airline-info">
          <div class="airline-logo">${r.airline.logo}</div>
          <div>
            <div class="airline-name">${r.airline.name}</div>
            <div class="airline-source">${r.platform.emoji} ${r.platform.name} · ⭐ ${r.airline.rating}</div>
          </div>
        </div>
        <div class="price-display">
          <div class="price-total">NT$${r.totalPrice.toLocaleString()}</div>
          <div class="price-per-person">每人 NT$${r.perPersonPrice.toLocaleString()}</div>
          ${priceChangeHTML}
        </div>
      </div>

      <div class="flight-timeline">
        <div class="flight-time">
          <div class="time">${r.outbound.dep}</div>
          <div class="airport">${r.fromCity.code}</div>
          <div class="date">${r.departDate || ''}</div>
        </div>
        <div class="flight-middle">
          <div class="flight-duration">${r.outbound.duration}</div>
          <div class="flight-line"></div>
          <div class="flight-stops">${r.outbound.stopText}</div>
        </div>
        <div class="flight-time">
          <div class="time">${r.outbound.arr}${r.outbound.nextDay ? '<sup style="font-size:10px;color:#f59e0b">+1</sup>' : ''}</div>
          <div class="airport">${r.toCity.code}</div>
          <div class="date">${r.returnDate || ''}</div>
        </div>
      </div>

      <div class="hotel-row">
        <div class="hotel-info">
          <span style="font-size:20px">${r.hotel.emoji}</span>
          <div>
            <div class="hotel-name">${r.hotel.name}</div>
            <div class="hotel-meta">
              ${'★'.repeat(r.hotel.stars)}${'☆'.repeat(5 - r.hotel.stars)} ·
              ${r.nights} 晚 · NT$${r.nightlyPrice.toLocaleString()}/晚
            </div>
          </div>
        </div>
        <div class="hotel-rating">⭐ ${r.hotel.rating}</div>
      </div>

      <div style="display:flex;gap:16px;margin-bottom:14px;align-items:center;">
        <div style="flex:1;font-size:12px;color:var(--text-muted)">
          機票 NT$${r.flightTotal.toLocaleString()} + 住宿 NT$${r.hotelTotal.toLocaleString()}
        </div>
        <div style="position:relative;width:100px;height:36px">
          <canvas id="mini-chart-${r.id}"></canvas>
        </div>
      </div>

      <div class="card-actions">
        <button class="btn-track${isTracked ? ' tracking' : ''}"
          id="track-btn-${r.id}"
          onclick="App.toggleTrack('${r.id}')">
          ${isTracked ? '🔔 追蹤中' : '🔔 追蹤此方案'}
        </button>
        <button class="btn-compare${inCompare ? ' in-compare' : ''}"
          id="compare-btn-${r.id}"
          onclick="App.toggleCompare('${r.id}')">
          ${inCompare ? '✓ 已加入' : '⚖️ 比較'}
        </button>
        <button class="btn-book" onclick="App.openBooking('${r.id}')">
          立即訂購 →
        </button>
      </div>
    `;
    return el;
  };

  /* ========== 排序 & 篩選 ========== */
  const filterAndSort = (results, filter) => {
    const list = [...results];
    if (filter === 'total')   list.sort((a, b) => a.totalPrice - b.totalPrice);
    if (filter === 'flight')  list.sort((a, b) => a.flightTotal - b.flightTotal);
    if (filter === 'hotel')   list.sort((a, b) => a.hotelTotal - b.hotelTotal);
    if (filter === 'rating')  list.sort((a, b) => b.airline.rating - a.airline.rating);
    if (filter === 'stops')   list.sort((a, b) => a.outbound.stops - b.outbound.stops);
    return list;
  };

  const setFilter = (filter) => {
    state.currentFilter = filter;
    document.querySelectorAll('.filter-chip').forEach(c => c.classList.remove('active'));
    document.getElementById(`filter-${filter}`)?.classList.add('active');
    if (state.searchResults.length) renderResults(state.searchResults);
  };

  /* ========== 統計卡片 ========== */
  const renderStats = () => {
    const trend = DataEngine.generateTrendStats(state.searchResults);
    if (!trend) return;

    const set = (id, html) => {
      const el = document.getElementById(id);
      if (el) el.innerHTML = html;
    };

    set('stat-today',   `NT$${trend.todayPrice.toLocaleString()}`);
    set('stat-min',     `NT$${trend.minPrice.toLocaleString()}`);
    set('stat-max',     `NT$${trend.maxPrice.toLocaleString()}`);
    set('stat-avg',     `NT$${trend.avgPrice.toLocaleString()}`);

    set('stat-today-sub',
      trend.isLowestToday
        ? `<span style="color:var(--accent-green)">🎯 今日最低！</span>`
        : `<span style="color:var(--text-muted)">7天最低 ${trend.minDay?.label || ''}</span>`
    );

    set('stat-trend-sub',
      trend.trend === 'up'
        ? `<span style="color:var(--accent-red)">▲ 近期上漲趨勢</span>`
        : `<span style="color:var(--accent-green)">▼ 近期下跌趨勢</span>`
    );

    set('stat-save-sub',
      `<span style="color:var(--accent-amber)">最多可省 NT$${trend.savings.toLocaleString()}</span>`
    );
  };

  /* ========== 追蹤方案 ========== */
  const toggleTrack = (planId) => {
    const plan = state.searchResults.find(r => r.id === planId);
    if (!plan) return;

    const btn = document.getElementById(`track-btn-${planId}`);

    if (Storage.isTracked(planId)) {
      Storage.removeTracked(planId);
      if (btn) { btn.textContent = '🔔 追蹤此方案'; btn.classList.remove('tracking'); }
      Tracker.showToast('已取消追蹤', 'info');
    } else {
      Storage.addTracked(plan);
      // 儲存今日價格
      const today = new Date().toISOString().split('T')[0];
      Storage.addPricePoint(planId, plan.totalPrice, today);

      if (btn) { btn.textContent = '🔔 追蹤中'; btn.classList.add('tracking'); }
      Tracker.showToast('已加入追蹤！價格下跌時將通知您 🎉', 'success');

      // 請求通知權限
      Tracker.requestPermission().then(granted => {
        if (!granted) {
          Tracker.showToast('請允許瀏覽器通知以接收低價提醒', 'warning', 6000);
        }
      });
    }
  };

  const removeTracked = (planId) => {
    Storage.removeTracked(planId);
    Tracker.showToast('已移除追蹤', 'info');
    Tracker.renderTrackedSection('tracker-list');
  };

  /* ========== 比較功能 ========== */
  const toggleCompare = (planId) => {
    const plan = state.searchResults.find(r => r.id === planId);
    if (!plan) return;
    const btn = document.getElementById(`compare-btn-${planId}`);

    const idx = state.compareList.findIndex(c => c.id === planId);
    if (idx !== -1) {
      state.compareList.splice(idx, 1);
      if (btn) { btn.textContent = '⚖️ 比較'; btn.classList.remove('in-compare'); }
    } else {
      if (state.compareList.length >= 3) {
        Tracker.showToast('最多比較 3 個方案', 'warning');
        return;
      }
      state.compareList.push(plan);
      if (btn) { btn.textContent = '✓ 已加入'; btn.classList.add('in-compare'); }
    }

    updateCompareBar();
  };

  const updateCompareBar = () => {
    const bar = document.getElementById('compare-bar');
    if (!bar) return;

    if (state.compareList.length === 0) {
      bar.classList.remove('visible');
      return;
    }
    bar.classList.add('visible');

    const items = document.getElementById('compare-bar-items');
    if (items) {
      items.innerHTML = state.compareList.map(c => `
        <div class="compare-item">
          <span>${c.airline.logo}</span>
          <span>${c.airline.name} NT$${c.totalPrice.toLocaleString()}</span>
          <button class="compare-remove" onclick="App.toggleCompare('${c.id}')">×</button>
        </div>
      `).join('');
    }

    const countEl = document.getElementById('compare-count');
    if (countEl) countEl.textContent = state.compareList.length;
  };

  const openCompareModal = () => {
    if (state.compareList.length < 2) {
      Tracker.showToast('請至少選擇 2 個方案進行比較', 'warning');
      return;
    }

    const modal = document.getElementById('compare-modal');
    if (!modal) return;

    const cols = state.compareList.length;
    const gridCols = `180px ${'1fr '.repeat(cols)}`.trim();

    const rows = [
      { label: '✈️ 航空公司',  fn: r => r.airline.name },
      { label: '🌐 平台',      fn: r => r.platform.name },
      { label: '💰 總價',      fn: r => `<strong style="color:var(--accent-purple-light)">NT$${r.totalPrice.toLocaleString()}</strong>` },
      { label: '🎫 機票',      fn: r => `NT$${r.flightTotal.toLocaleString()}` },
      { label: '🏨 住宿',      fn: r => `NT$${r.hotelTotal.toLocaleString()}` },
      { label: '🕐 出發時間',  fn: r => r.outbound.dep },
      { label: '🕔 抵達時間',  fn: r => `${r.outbound.arr}${r.outbound.nextDay ? '+1' : ''}` },
      { label: '⏱️ 飛行時間',  fn: r => r.outbound.duration },
      { label: '🛑 停靠',      fn: r => r.outbound.stopText },
      { label: '⭐ 評分',      fn: r => `${r.airline.rating} / 5` },
      { label: '🏩 住宿名稱',  fn: r => r.hotel.name },
      { label: '🌙 住宿天數',  fn: r => `${r.nights} 晚` },
    ];

    const headerCells = state.compareList.map(r =>
      `<div class="compare-col-header">${r.airline.logo} ${r.airline.name}</div>`
    ).join('');

    const rowsHTML = rows.map(row => `
      <div class="compare-row" style="grid-template-columns:${gridCols}">
        <div class="compare-cell compare-cell-label">${row.label}</div>
        ${state.compareList.map(r => `<div class="compare-cell">${row.fn(r)}</div>`).join('')}
      </div>
    `).join('');

    modal.querySelector('.modal-body').innerHTML = `
      <div class="compare-table" style="grid-template-columns:${gridCols}">
        <div></div>${headerCells}
      </div>
      ${rowsHTML}
      <div style="margin-top:24px">
        <div class="section-title" style="margin-bottom:16px">📊 綜合評比雷達圖</div>
        <div style="height:280px"><canvas id="compare-radar"></canvas></div>
      </div>
    `;

    modal.classList.add('open');
    setTimeout(() => {
      ChartModule.renderCompareChart('compare-radar', state.compareList);
    }, 100);
  };

  const closeCompareModal = () => {
    document.getElementById('compare-modal')?.classList.remove('open');
  };

  /* ========== 搜尋記錄 ========== */
  const renderHistory = () => {
    const container = document.getElementById('history-container');
    if (!container) return;

    const history = Storage.getSearchHistory();
    if (!history.length) {
      container.innerHTML = `
        <div class="empty-state">
          <div class="empty-state-icon">🕐</div>
          <h3>尚無搜尋記錄</h3>
          <p>進行第一次搜尋後，記錄將出現在此</p>
        </div>`;
      return;
    }

    container.innerHTML = `<div class="history-grid">${
      history.map(h => `
        <div class="history-card" onclick="App.replaySearch(${JSON.stringify(h).replace(/"/g, '&quot;')})">
          <div class="history-route">${h.fromName} → ${h.toName}</div>
          <div class="history-meta">
            <span>📅 ${h.dates}</span>
            <span>🌙 ${h.nights} 晚</span>
            <span>👤 ${h.adults} 大人${h.children > 0 ? ` ${h.children} 兒童` : ''}</span>
          </div>
          <div style="font-size:11px;color:var(--text-muted);margin-top:8px">
            ${new Date(h.timestamp).toLocaleString('zh-TW')}
          </div>
        </div>
      `).join('')
    }</div>`;
  };

  const replaySearch = (h) => {
    // 回到首頁並填入數值
    navigate('home');
    state.adults   = h.adults   || 2;
    state.children = h.children || 0;
    state.nights   = h.nights   || 5;

    setTimeout(() => {
      const fromInput = document.getElementById('from-city');
      const toInput   = document.getElementById('to-city');
      if (fromInput) { fromInput.value = `${h.fromName} (${h.from})`; fromInput.dataset.code = h.from; }
      if (toInput)   { toInput.value   = `${h.toName} (${h.to})`;     toInput.dataset.code   = h.to;   }

      document.getElementById('adults-display').textContent = state.adults;
      document.getElementById('children-display').textContent = state.children;
      document.getElementById('nights-display').textContent = state.nights;

      if (h.dates) {
        const [d, r] = h.dates.split(' ~ ');
        const departInput = document.getElementById('depart-date');
        const returnInput = document.getElementById('return-date');
        if (departInput) departInput.value = d;
        if (returnInput) returnInput.value = r;
      }
    }, 100);
  };

  /* ========== 設定目標價格 ========== */
  const setTargetPriceDialog = (planId) => {
    const settings = Storage.getSettings();
    const currentTarget = settings.targetPrices[planId] || '';
    const input = prompt(`請輸入目標價格（TWD）：\n達到此金額時，系統將發送通知。`, currentTarget);
    if (input === null) return;
    const price = parseInt(input);
    if (isNaN(price) || price <= 0) {
      Tracker.showToast('請輸入有效金額', 'warning');
      return;
    }
    Storage.setTargetPrice(planId, price);
    Tracker.showToast(`已設定目標價格 NT$${price.toLocaleString()}`, 'success');
    Tracker.renderTrackedSection('tracker-list');
  };

  /* ========== 趨勢圖模式切換 ========== */
  const setTrendMode = (mode) => {
    state.trendMode = mode;
    document.querySelectorAll('.chart-toggle-btn').forEach(b => b.classList.remove('active'));
    document.getElementById(`trend-${mode}`)?.classList.add('active');
    if (state.searchResults.length) {
      ChartModule.renderTrendChart('trend-chart', state.searchResults, mode);
    }
  };

  /* ========== 訂購（示意） ========== */
  const openBooking = (planId) => {
    const plan = state.searchResults.find(r => r.id === planId);
    if (!plan) return;
    Tracker.showToast(`正在前往 ${plan.platform.name} 訂購頁面…`, 'info');
    setTimeout(() => {
      Tracker.showToast(`（示意）在真實版本中，這裡將開啟 ${plan.platform.name} 的訂購連結`, 'info', 6000);
    }, 1500);
  };

  /* ========== 通知設定 ========== */
  const toggleNotifications = async (checked) => {
    if (checked) {
      const granted = await Tracker.requestPermission();
      if (!granted) {
        document.getElementById('notif-toggle').checked = false;
        Tracker.showToast('瀏覽器通知被封鎖，請至瀏覽器設定中允許', 'warning', 6000);
        return;
      }
      Tracker.showToast('通知已開啟！低價時將推播提醒 🔔', 'success');
    }
    const settings = Storage.getSettings();
    settings.notificationsEnabled = checked;
    Storage.saveSettings(settings);
  };

  /* ========== 工具函式 ========== */
  const extractCode = (str) => {
    const match = str.match(/\(([A-Z]{3})\)/);
    return match ? match[1] : null;
  };

  const delay = (ms) => new Promise(resolve => setTimeout(resolve, ms));

  /* ========== 初始化 ========== */
  const init = () => {
    initAutocomplete('from-city', 'from-dropdown');
    initAutocomplete('to-city', 'to-dropdown');
    initQtyControls();
    initDateDefaults();

    // 設定預設出發地
    const fromInput = document.getElementById('from-city');
    if (fromInput) { fromInput.value = '台北 (TPE)'; fromInput.dataset.code = 'TPE'; }

    // 頁面導覽按鈕
    document.querySelectorAll('.nav-btn[data-page]').forEach(btn => {
      btn.addEventListener('click', () => navigate(btn.dataset.page));
    });

    // 搜尋按鈕
    document.getElementById('search-btn')?.addEventListener('click', handleSearch);
    document.getElementById('hero-search-btn')?.addEventListener('click', handleSearch);

    // 篩選
    document.querySelectorAll('.filter-chip').forEach(chip => {
      chip.addEventListener('click', () => setFilter(chip.dataset.filter));
    });

    // 趨勢圖切換
    document.getElementById('trend-total')?.addEventListener('click',  () => setTrendMode('total'));
    document.getElementById('trend-flight')?.addEventListener('click', () => setTrendMode('flight'));
    document.getElementById('trend-hotel')?.addEventListener('click',  () => setTrendMode('hotel'));

    // 比較 Modal
    document.getElementById('compare-now-btn')?.addEventListener('click', openCompareModal);
    document.getElementById('modal-close-btn')?.addEventListener('click', closeCompareModal);
    document.getElementById('compare-modal')?.addEventListener('click', (e) => {
      if (e.target.id === 'compare-modal') closeCompareModal();
    });

    // 通知切換
    document.getElementById('notif-toggle')?.addEventListener('change', (e) => {
      toggleNotifications(e.target.checked);
    });

    // 更新通知 UI
    const settings = Storage.getSettings();
    const toggle = document.getElementById('notif-toggle');
    if (toggle) toggle.checked = settings.notificationsEnabled && Tracker.isPermissionGranted();

    // 初始頁面
    navigate('home');

    // 每日追蹤刷新
    Tracker.initDailyRefresh();
  };

  return {
    init,
    navigate,
    handleSearch,
    toggleTrack,
    removeTracked,
    toggleCompare,
    openCompareModal,
    closeCompareModal,
    setFilter,
    setTrendMode,
    setTargetPriceDialog,
    openBooking,
    replaySearch,
    toggleNotifications,
    updateCompareBar,
  };
})();

// DOM Ready
document.addEventListener('DOMContentLoaded', App.init);
