/**
 * 北部環島 3天2夜 雙極點公路之旅 - Interactive Application Logic
 */

(function () {
  'use strict';

  // State Management
  const state = {
    currentBranch: 'sunny', // 'sunny' or 'rainy'
    selectedDay: 'all',     // 'all', '1', '2', '3'
    selectedStopId: null,
    activeTab: 'timeline',  // 'timeline', 'weather', 'checklist', 'food'
    completedStops: JSON.parse(localStorage.getItem('trip_completed_stops') || '[]'),
    checklistData: JSON.parse(localStorage.getItem('trip_checklist_items') || '{}')
  };

  // Map & Layers Reference
  let map = null;
  let markersLayerGroup = null;
  let polylinesLayerGroup = null;
  let currentTileLayer = null;
  const markerMap = {}; // stopId -> Leaflet marker

  // Colors
  const COLORS = {
    sunny: '#10b981',
    rainy: '#06b6d4',
    day2: '#a855f7',
    day3: '#f59e0b',
    activeGlow: '#38bdf8'
  };

  // Type Badges & Icons
  const TYPE_CONFIG = {
    start: { label: '出發點', icon: '🚩', pillClass: 'start' },
    end: { label: '終點', icon: '🏁', pillClass: 'end' },
    gas: { label: '加油站', icon: '⛽', pillClass: 'gas' },
    food: { label: '美食餐廳', icon: '🍜', pillClass: 'food' },
    scenic: { label: '觀景點', icon: '🏞️', pillClass: 'scenic' },
    hotel: { label: '住宿溫泉', icon: '♨️', pillClass: 'hotel' },
    shopping: { label: '必買伴手禮', icon: '🛍️', pillClass: 'shopping' }
  };

  // 1. Initialize Map
  function initMap() {
    // Center at northern Taiwan
    map = L.map('map', {
      zoomControl: false,
      attributionControl: true
    }).setView([24.75, 121.35], 9);

    // Zoom control at bottom right
    L.control.zoom({ position: 'bottomright' }).addTo(map);

    // Default tile layer: OpenStreetMap (Detailed Taiwan roads, Traditional Chinese, zero watermark)
    currentTileLayer = L.tileLayer('https://tile.openstreetmap.org/{z}/{x}/{y}.png', {
      attribution: '&copy; <a href="https://www.openstreetmap.org/copyright">OpenStreetMap</a> contributors',
      maxZoom: 19
    }).addTo(map);

    markersLayerGroup = L.layerGroup().addTo(map);
    polylinesLayerGroup = L.layerGroup().addTo(map);

    renderAll();
  }

  // 2. Get active stops based on state
  function getActiveStops() {
    if (!window.TRIP_DATA || !window.TRIP_DATA.stops) return [];

    let stops = [];
    const d1Key = state.currentBranch === 'sunny' ? 'day1_sunny' : 'day1_rainy';

    if (state.selectedDay === 'all' || state.selectedDay === '1') {
      stops = stops.concat(window.TRIP_DATA.stops[d1Key] || []);
    }
    if (state.selectedDay === 'all' || state.selectedDay === '2') {
      stops = stops.concat(window.TRIP_DATA.stops['day2'] || []);
    }
    if (state.selectedDay === 'all' || state.selectedDay === '3') {
      stops = stops.concat(window.TRIP_DATA.stops['day3'] || []);
    }

    return stops;
  }

  // 3. Render Route Polylines
  function renderPolylines() {
    polylinesLayerGroup.clearLayers();
    if (!window.TRIP_DATA || !window.TRIP_DATA.routes) return;

    const routes = window.TRIP_DATA.routes;
    const allBounds = [];

    // Day 1
    const d1Key = state.currentBranch === 'sunny' ? 'day1_sunny' : 'day1_rainy';
    if ((state.selectedDay === 'all' || state.selectedDay === '1') && routes[d1Key]) {
      const d1Color = state.currentBranch === 'sunny' ? COLORS.sunny : COLORS.rainy;
      const poly = L.polyline(routes[d1Key].coordinates, {
        color: d1Color,
        weight: 5,
        opacity: 0.85,
        lineCap: 'round',
        lineJoin: 'round'
      }).addTo(polylinesLayerGroup);

      poly.bindTooltip(`DAY 1 (${state.currentBranch === 'sunny' ? '晴天線・北橫' : '雨天線・大溪碧潭'}): 約 ${routes[d1Key].distance_km} km`, {
        sticky: true
      });
      allBounds.push(poly.getBounds());
    }

    // Day 2
    if ((state.selectedDay === 'all' || state.selectedDay === '2') && routes['day2']) {
      const poly = L.polyline(routes['day2'].coordinates, {
        color: COLORS.day2,
        weight: 5,
        opacity: 0.85,
        lineCap: 'round',
        lineJoin: 'round'
      }).addTo(polylinesLayerGroup);

      poly.bindTooltip(`DAY 2 (雙極點海岸線): 約 ${routes['day2'].distance_km} km`, {
        sticky: true
      });
      allBounds.push(poly.getBounds());
    }

    // Day 3
    if ((state.selectedDay === 'all' || state.selectedDay === '3') && routes['day3']) {
      const poly = L.polyline(routes['day3'].coordinates, {
        color: COLORS.day3,
        weight: 5,
        opacity: 0.85,
        lineCap: 'round',
        lineJoin: 'round'
      }).addTo(polylinesLayerGroup);

      poly.bindTooltip(`DAY 3 (北投返程): 約 ${routes['day3'].distance_km} km`, {
        sticky: true
      });
      allBounds.push(poly.getBounds());
    }

    // Fit map bounds if valid
    if (allBounds.length > 0) {
      let merged = allBounds[0];
      for (let i = 1; i < allBounds.length; i++) {
        merged = merged.extend(allBounds[i]);
      }
      map.fitBounds(merged, { padding: [50, 50] });
    }
  }

  // 4. Render Markers
  function renderMarkers() {
    markersLayerGroup.clearLayers();
    Object.keys(markerMap).forEach(k => delete markerMap[k]);

    const stops = getActiveStops();

    stops.forEach((stop, index) => {
      let pinClass = 'pin-d2';
      if (stop.day === 1) {
        pinClass = state.currentBranch === 'sunny' ? 'pin-d1-sun' : 'pin-d1-rain';
      } else if (stop.day === 3) {
        pinClass = 'pin-d3';
      }

      // Determine category symbol
      const typeInfo = TYPE_CONFIG[stop.type] || { icon: '📍' };

      // Create Custom HTML Pin Icon
      const customIcon = L.divIcon({
        className: 'custom-pin-wrapper',
        html: `
          <div class="custom-pin-marker ${pinClass}" id="pin-${stop.id}" style="width: 32px; height: 32px;">
            <span>${index + 1}</span>
          </div>
        `,
        iconSize: [32, 32],
        iconAnchor: [16, 16],
        popupAnchor: [0, -18]
      });

      const marker = L.marker([stop.lat, stop.lng], { icon: customIcon }).addTo(markersLayerGroup);
      markerMap[stop.id] = marker;

      // Popup content
      const googleNavUrl = `https://www.google.com/maps/dir/?api=1&destination=${stop.lat},${stop.lng}`;
      const popupHtml = `
        <div class="map-popup-inner">
          <div class="map-popup-header">
            <span class="type-pill ${typeInfo.pillClass}">${typeInfo.icon} ${typeInfo.label}</span>
            <span class="stop-time-badge">⏰ ${stop.time}</span>
          </div>
          <div class="map-popup-title">${index + 1}. ${stop.name}</div>
          <div style="font-size: 0.72rem; color: #94a3b8; margin-bottom: 4px;">📍 ${stop.address}</div>
          <div class="map-popup-note">${stop.note}</div>
          <div class="map-popup-actions">
            <a href="${googleNavUrl}" target="_blank" rel="noopener" class="btn-nav">
              🚗 開啟 Google 導航
            </a>
          </div>
        </div>
      `;
      marker.bindPopup(popupHtml);

      // Marker click
      marker.on('click', () => {
        selectStop(stop.id, false);
      });
    });
  }

  // 5. Render Timeline in Sidebar
  function renderTimeline() {
    const listEl = document.getElementById('timeline-list');
    if (!listEl) return;

    const stops = getActiveStops();
    listEl.innerHTML = '';

    let currentDayHeader = null;

    stops.forEach((stop, index) => {
      // Day separator
      if (stop.day !== currentDayHeader) {
        currentDayHeader = stop.day;
        const dayTitleEl = document.createElement('div');
        dayTitleEl.className = 'timeline-section-title';

        let dayBadgeClass = `badge-day d${stop.day}`;
        let dayNameText = '';
        if (stop.day === 1) {
          if (state.currentBranch === 'rainy') dayBadgeClass += ' rainy';
          dayNameText = state.currentBranch === 'sunny'
            ? 'DAY 1: 晴天線・北橫公路 (內灣/宇老/明池/羅東/礁溪)'
            : 'DAY 1: 雨天線・平路巡航 (卓蘭/大溪/碧潭/北宜/礁溪)';
        } else if (stop.day === 2) {
          dayNameText = 'DAY 2: 雙極點海岸線 (極東三貂角 ⇄ 基隆廟口 ⇄ 極北富貴角 ⇄ 北投)';
        } else {
          dayNameText = 'DAY 3: 返程伴手禮之旅 (北投 ⇄ 平鎮 ⇄ 新竹海瑞摃丸 ⇄ 台中)';
        }

        dayTitleEl.innerHTML = `
          <h3>
            <span class="${dayBadgeClass}">DAY ${stop.day}</span>
            <span style="font-size: 0.85rem; font-weight: 600;">${dayNameText}</span>
          </h3>
        `;
        listEl.appendChild(dayTitleEl);
      }

      // Card Element
      const typeInfo = TYPE_CONFIG[stop.type] || { label: '景點', icon: '📍', pillClass: 'scenic' };
      const isCompleted = state.completedStops.includes(stop.id);
      const isSelected = state.selectedStopId === stop.id;

      let cardDayClass = `day-${stop.day}`;
      if (stop.day === 1 && state.currentBranch === 'rainy') cardDayClass += ' rainy-mode';

      const card = document.createElement('div');
      card.className = `timeline-card ${cardDayClass} ${isSelected ? 'active' : ''} ${isCompleted ? 'completed' : ''}`;
      card.id = `card-${stop.id}`;

      const googleNavUrl = `https://www.google.com/maps/dir/?api=1&destination=${stop.lat},${stop.lng}`;

      card.innerHTML = `
        <div class="timeline-node">
          ${index + 1}
        </div>
        <div class="timeline-content">
          <div class="timeline-top-row">
            <span class="stop-time-badge">⏰ ${stop.time}</span>
            <span class="type-pill ${typeInfo.pillClass}">${typeInfo.icon} ${typeInfo.label}</span>
          </div>
          <div class="stop-name">${stop.name}</div>
          <div class="stop-address">📍 ${stop.address}</div>
          <div class="stop-note">${stop.note}</div>
          <div class="stop-meta-footer">
            <div class="meta-stats">
              <span>🚗 ${stop.dist}</span>
              <span>⏳ 停留 ${stop.stay}</span>
            </div>
            <div class="card-actions">
              <label class="check-complete" onclick="event.stopPropagation()">
                <input type="checkbox" data-stop-id="${stop.id}" ${isCompleted ? 'checked' : ''}>
                已達
              </label>
              <a href="${googleNavUrl}" target="_blank" rel="noopener" class="btn-nav" onclick="event.stopPropagation()">
                導航
              </a>
            </div>
          </div>
        </div>
      `;

      card.addEventListener('click', () => {
        selectStop(stop.id, true);
      });

      // Checkbox listener
      const checkbox = card.querySelector('input[type="checkbox"]');
      checkbox.addEventListener('change', (e) => {
        e.stopPropagation();
        toggleStopCompletion(stop.id, e.target.checked);
      });

      listEl.appendChild(card);
    });
  }

  // 6. Select Stop (Sync Map & Timeline)
  function selectStop(stopId, panToMap = true) {
    state.selectedStopId = stopId;

    // Update active class on timeline cards
    document.querySelectorAll('.timeline-card').forEach(c => c.classList.remove('active'));
    const activeCard = document.getElementById(`card-${stopId}`);
    if (activeCard) {
      activeCard.classList.add('active');
      activeCard.scrollIntoView({ behavior: 'smooth', block: 'nearest' });
    }

    // Update markers
    document.querySelectorAll('.custom-pin-marker').forEach(p => p.classList.remove('pin-active'));
    const activePin = document.getElementById(`pin-${stopId}`);
    if (activePin) activePin.classList.add('pin-active');

    // Pan map & open popup
    const marker = markerMap[stopId];
    if (marker && panToMap) {
      map.setView(marker.getLatLng(), 13, { animate: true });
      marker.openPopup();
    }
  }

  // 7. Toggle Stop Completion
  function toggleStopCompletion(stopId, isChecked) {
    if (isChecked) {
      if (!state.completedStops.includes(stopId)) state.completedStops.push(stopId);
    } else {
      state.completedStops = state.completedStops.filter(id => id !== stopId);
    }
    localStorage.setItem('trip_completed_stops', JSON.stringify(state.completedStops));

    const card = document.getElementById(`card-${stopId}`);
    if (card) {
      if (isChecked) card.classList.add('completed');
      else card.classList.remove('completed');
    }
    updateSummaryStats();
  }

  // 8. Update Trip Stats Summary
  function updateSummaryStats() {
    const totalStops = getActiveStops().length;
    const completedCount = state.completedStops.filter(id => {
      return getActiveStops().some(s => s.id === id);
    }).length;

    // Approximate total distance
    let totalDist = 0;
    if (window.TRIP_DATA && window.TRIP_DATA.routes) {
      const d1Key = state.currentBranch === 'sunny' ? 'day1_sunny' : 'day1_rainy';
      const r = window.TRIP_DATA.routes;
      if (state.selectedDay === 'all') {
        totalDist = (r[d1Key]?.distance_km || 0) + (r['day2']?.distance_km || 0) + (r['day3']?.distance_km || 0);
      } else if (state.selectedDay === '1') {
        totalDist = r[d1Key]?.distance_km || 0;
      } else if (state.selectedDay === '2') {
        totalDist = r['day2']?.distance_km || 0;
      } else if (state.selectedDay === '3') {
        totalDist = r['day3']?.distance_km || 0;
      }
    }

    const distEl = document.getElementById('stat-total-dist');
    const stopsEl = document.getElementById('stat-total-stops');
    const completeEl = document.getElementById('stat-completed');

    if (distEl) distEl.textContent = `${Math.round(totalDist)} km`;
    if (stopsEl) stopsEl.textContent = `${totalStops} 站`;
    if (completeEl) completeEl.textContent = `${completedCount}/${totalStops}`;
  }

  // 9. Weather Forecast Fetching (Open-Meteo)
  const WEATHER_LOCATIONS = [
    { name: '新竹尖石/宇老', note: '北橫最高點 1450m 溫差大', lat: 24.6676, lng: 121.2819 },
    { name: '苗栗卓蘭', note: '永安喜餅旗艦店', lat: 24.3206, lng: 120.8237 },
    { name: '宜蘭礁溪', note: 'Day 1 住宿溫泉鄉', lat: 24.8260, lng: 121.7731 },
    { name: '新北三貂角', note: '台灣極東點 海風風速', lat: 25.0080, lng: 122.0039 },
    { name: '基隆廟口', note: '仁三路奠濟宮', lat: 25.1283, lng: 121.7431 },
    { name: '新北富貴角', note: '台灣極北點', lat: 25.2983, lng: 121.5378 },
    { name: '台北北投', note: 'Day 2 馥悅溫泉酒店', lat: 25.1375, lng: 121.5102 }
  ];

  async function loadWeatherForecast() {
    const container = document.getElementById('weather-cards-container');
    if (!container) return;

    container.innerHTML = `<div style="text-align: center; padding: 1.5rem; color: #94a3b8;">正在取得當地即時氣象資訊...</div>`;

    try {
      const cardsHtml = await Promise.all(WEATHER_LOCATIONS.map(async loc => {
        try {
          const url = `https://api.open-meteo.com/v1/forecast?latitude=${loc.lat}&longitude=${loc.lng}&current_weather=true&hourly=precipitation_probability`;
          const res = await fetch(url);
          const data = await res.json();
          const cur = data.current_weather || {};
          const temp = Math.round(cur.temperature || 24);
          const wind = Math.round(cur.windspeed || 10);
          const wcode = cur.weathercode || 0;

          // Simple weather code description
          let desc = '晴朗舒適';
          let icon = '☀️';
          if (wcode >= 1 && wcode <= 3) { desc = '多雲晴天'; icon = '⛅'; }
          else if (wcode >= 51 && wcode <= 67) { desc = '局部陣雨'; icon = '🌧️'; }
          else if (wcode >= 80 && wcode <= 82) { desc = '雷陣雨'; icon = '⛈️'; }
          else if (wcode >= 71) { desc = '涼冷雲霧'; icon = '🌫️'; }

          return `
            <div class="weather-city-card">
              <div class="city-info">
                <h4>${icon} ${loc.name}</h4>
                <p>${loc.note}</p>
                <div style="font-size: 0.72rem; color: #64748b; margin-top: 4px;">風速: ${wind} km/h</div>
              </div>
              <div class="weather-data-col">
                <div class="weather-temp">${temp}°C</div>
                <div class="weather-condition">${desc}</div>
              </div>
            </div>
          `;
        } catch (e) {
          return `
            <div class="weather-city-card">
              <div class="city-info"><h4>${loc.name}</h4><p>${loc.note}</p></div>
              <div class="weather-data-col"><div class="weather-condition">氣象暫未連線</div></div>
            </div>
          `;
        }
      }));

      container.innerHTML = cardsHtml.join('');
    } catch (err) {
      container.innerHTML = `<div style="color: #ef4444; padding: 1rem;">取得氣象資訊時發生錯誤，請稍後再試。</div>`;
    }
  }

  // 10. Checklist Manager
  function initChecklist() {
    const checkboxes = document.querySelectorAll('#checklist-panel input[type="checkbox"]');
    checkboxes.forEach(cb => {
      const key = cb.dataset.key;
      if (key && state.checklistData[key]) {
        cb.checked = true;
        cb.closest('.check-item')?.classList.add('done');
      }

      cb.addEventListener('change', (e) => {
        const item = e.target.closest('.check-item');
        if (e.target.checked) {
          state.checklistData[key] = true;
          item?.classList.add('done');
        } else {
          delete state.checklistData[key];
          item?.classList.remove('done');
        }
        localStorage.setItem('trip_checklist_items', JSON.stringify(state.checklistData));
      });
    });
  }

  // 11. Modal & Export Formatting
  function generateShareText() {
    const d1Key = state.currentBranch === 'sunny' ? 'day1_sunny' : 'day1_rainy';
    const stopsD1 = window.TRIP_DATA.stops[d1Key] || [];
    const stopsD2 = window.TRIP_DATA.stops['day2'] || [];
    const stopsD3 = window.TRIP_DATA.stops['day3'] || [];

    let text = `🏍️【台灣北部環島 3天2夜 雙極點追風計畫】\n`;
    text += `模式：${state.currentBranch === 'sunny' ? '☀️ 好天氣路線 (內灣・北橫公路・明池)' : '🌧️ 下雨天路線 (平路・大溪・碧潭・北宜)'}\n\n`;

    text += `📅 DAY 1 (${state.currentBranch === 'sunny' ? '北橫線' : '大溪平路線'})\n`;
    stopsD1.forEach((s, i) => {
      text += `${s.time} [${s.name}] - ${s.note}\n`;
    });

    text += `\n📅 DAY 2 (雙極點海岸巡禮)\n`;
    stopsD2.forEach((s, i) => {
      text += `${s.time} [${s.name}] - ${s.note}\n`;
    });

    text += `\n📅 DAY 3 (北投伴手禮返程)\n`;
    stopsD3.forEach((s, i) => {
      text += `${s.time} [${s.name}] - ${s.note}\n`;
    });

    text += `\n★ 重要備忘：\n`;
    text += `1. 尖石加滿油（北橫前必加）\n`;
    text += `2. 羅東猿燒日式料理 17:30 預約\n`;
    text += `3. 住宿：礁溪民宿 (1837) / 北投馥悅溫泉酒店 (2839)\n`;
    text += `4. 雙極點：極東三貂角燈塔 + 極北富貴角燈塔！\n`;

    return text;
  }

  // 12. Show Toast Message
  function showToast(msg) {
    let toast = document.getElementById('toast-msg');
    if (!toast) {
      toast = document.createElement('div');
      toast.id = 'toast-msg';
      toast.className = 'toast-msg';
      document.body.appendChild(toast);
    }
    toast.textContent = msg;
    toast.classList.add('show');
    setTimeout(() => {
      toast.classList.remove('show');
    }, 2500);
  }

  // 13. Master Render Function
  function renderAll() {
    renderPolylines();
    renderMarkers();
    renderTimeline();
    updateSummaryStats();
  }

  // 14. Event Listeners Setup
  function setupEventListeners() {
    // Weather Switcher (Sunny / Rainy)
    const btnSunny = document.getElementById('btn-sunny');
    const btnRainy = document.getElementById('btn-rainy');

    if (btnSunny && btnRainy) {
      btnSunny.addEventListener('click', () => {
        if (state.currentBranch === 'sunny') return;
        state.currentBranch = 'sunny';
        btnSunny.classList.add('active');
        btnRainy.classList.remove('active');
        document.getElementById('branch-mode-desc')?.textContent && (document.getElementById('branch-mode-desc').textContent = '北橫・明池・宇老高山線');
        renderAll();
        showToast('已切換為：☀️ 好天氣路線 (走尖石/宇老/北橫/明池)');
      });

      btnRainy.addEventListener('click', () => {
        if (state.currentBranch === 'rainy') return;
        state.currentBranch = 'rainy';
        btnRainy.classList.add('active');
        btnSunny.classList.remove('active');
        document.getElementById('branch-mode-desc')?.textContent && (document.getElementById('branch-mode-desc').textContent = '平路・大溪・碧潭・北宜線');
        renderAll();
        showToast('已切換為：🌧️ 下雨天路線 (避開北橫高山，走大溪/碧潭/北宜公路)');
      });
    }

    // Day Filter Buttons
    document.querySelectorAll('.pill-btn[data-day]').forEach(btn => {
      btn.addEventListener('click', () => {
        document.querySelectorAll('.pill-btn[data-day]').forEach(b => b.classList.remove('active'));
        btn.classList.add('active');
        state.selectedDay = btn.dataset.day;
        renderAll();
      });
    });

    // Sidebar Tabs
    document.querySelectorAll('.tab-btn[data-tab]').forEach(tab => {
      tab.addEventListener('click', () => {
        document.querySelectorAll('.tab-btn[data-tab]').forEach(t => t.classList.remove('active'));
        tab.classList.add('active');

        const targetTab = tab.dataset.tab;
        state.activeTab = targetTab;

        document.querySelectorAll('.tab-panel').forEach(p => p.style.display = 'none');
        const activePanel = document.getElementById(`panel-${targetTab}`);
        if (activePanel) activePanel.style.display = 'block';

        if (targetTab === 'weather') {
          loadWeatherForecast();
        }
      });
    });

    // Map Action Buttons (Reset & Fullscreen)
    document.getElementById('btn-map-fit')?.addEventListener('click', () => {
      renderPolylines();
      showToast('已重新適應全行程視角');
    });

    // Tile Layer Toggle (OpenStreetMap / Esri World Topo Map)
    let currentTileMode = 0; // 0: OSM, 1: Esri Topo, 2: Esri Street
    const tileProviders = [
      {
        name: 'OpenStreetMap 標準公路圖 (詳細繁體中文路名)',
        url: 'https://tile.openstreetmap.org/{z}/{x}/{y}.png',
        attr: '&copy; <a href="https://www.openstreetmap.org/copyright">OpenStreetMap</a> contributors'
      },
      {
        name: 'Esri World Topo 高山地形圖 (山脈陰影與等高地形)',
        url: 'https://server.arcgisonline.com/ArcGIS/rest/services/World_Topo_Map/MapServer/tile/{z}/{y}/{x}',
        attr: 'Tiles &copy; Esri &mdash; Esri, DeLorme, NAVTEQ, TomTom, Intermap, iPC, USGS, FAO, NPS, NRCAN, GeoBase, Kadaster NL, Ordnance Survey, Esri Japan, METI, Esri China (Hong Kong), and the GIS User Community'
      },
      {
        name: 'Esri World Street 街道圖 (清爽公路路網)',
        url: 'https://server.arcgisonline.com/ArcGIS/rest/services/World_Street_Map/MapServer/tile/{z}/{y}/{x}',
        attr: 'Tiles &copy; Esri &mdash; Source: Esri, DeLorme, NAVTEQ, USGS, Intermap, iPC, NRCAN, Esri Japan, METI, Esri China (Hong Kong), Esri (Thailand), TomTom, 2012'
      }
    ];

    document.getElementById('btn-toggle-tiles')?.addEventListener('click', () => {
      currentTileMode = (currentTileMode + 1) % tileProviders.length;
      if (currentTileLayer) map.removeLayer(currentTileLayer);

      const p = tileProviders[currentTileMode];
      currentTileLayer = L.tileLayer(p.url, {
        attribution: p.attr,
        maxZoom: 19
      }).addTo(map);
      showToast(`已切換地圖風格：${p.name}`);
    });

    // Share & Export Modal
    const shareModal = document.getElementById('share-modal');
    document.getElementById('btn-share')?.addEventListener('click', () => {
      const textarea = document.getElementById('share-text-content');
      if (textarea) textarea.value = generateShareText();
      shareModal?.classList.add('show');
    });

    document.getElementById('btn-close-modal')?.addEventListener('click', () => {
      shareModal?.classList.remove('show');
    });

    document.getElementById('btn-copy-share')?.addEventListener('click', () => {
      const textarea = document.getElementById('share-text-content');
      if (textarea) {
        textarea.select();
        navigator.clipboard.writeText(textarea.value).then(() => {
          showToast('已複製行程至剪貼簿！可直接貼到 LINE 群組');
        });
      }
    });

    // Theme Toggle (Dark / Light UI)
    const btnTheme = document.getElementById('btn-toggle-theme');
    btnTheme?.addEventListener('click', () => {
      const current = document.documentElement.getAttribute('data-theme') || 'dark';
      const next = current === 'dark' ? 'light' : 'dark';
      document.documentElement.setAttribute('data-theme', next);
      localStorage.setItem('trip_app_theme', next);
      btnTheme.textContent = next === 'dark' ? '🌙' : '☀️';
    });

    // Print Button
    document.getElementById('btn-print')?.addEventListener('click', () => {
      window.print();
    });

    // Initialize Checklist
    initChecklist();
  }

  // DOM Loaded Init
  document.addEventListener('DOMContentLoaded', () => {
    // Restore saved theme
    const savedTheme = localStorage.getItem('trip_app_theme') || 'dark';
    document.documentElement.setAttribute('data-theme', savedTheme);
    const btnTheme = document.getElementById('btn-toggle-theme');
    if (btnTheme) btnTheme.textContent = savedTheme === 'dark' ? '🌙' : '☀️';

    initMap();
    setupEventListeners();
  });

})();
