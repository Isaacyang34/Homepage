/**
 * TrackPulse - Real Logistics Query & Notification Engine
 * 貨物真實動態監控與即時通知系統 (真實物流 API 連線版)
 */

const STATUS_CONFIG = {
  created: { label: '已建立單號', colorClass: 'created', level: 1 },
  picked_up: { label: '已攬收 / 寄件中', colorClass: 'picked_up', level: 2 },
  in_transit: { label: '連線運輸中', colorClass: 'in_transit', level: 3 },
  out_for_delivery: { label: '到達轉運站 / 派送中', colorClass: 'out_for_delivery', level: 4 },
  delivered: { label: '已順利送達', colorClass: 'delivered', level: 5 },
  exception: { label: '包裹異常 / 退回', colorClass: 'exception', level: 99 }
};

class RealTrackPulseEngine {
  constructor() {
    this.packages = this.loadPackages();
    this.logs = this.loadLogs();
    this.currentFilter = 'all';
    this.searchQuery = '';
    this.autoPollingTimer = null;
    this.activeDetailPkgId = null;
    this.audioCtx = null;

    this.initElements();
    this.bindEvents();
    this.checkNotificationPermission();
    this.render();
  }

  initElements() {
    this.quickTrackingNoInput = document.getElementById('quickTrackingNoInput');
    this.btnQuickAdd = document.getElementById('btnQuickAdd');

    this.btnOpenAddModal = document.getElementById('btnOpenAddModal');
    this.btnEmptyAdd = document.getElementById('btnEmptyAdd');
    this.btnRequestNotif = document.getElementById('btnRequestNotifPermission');
    this.btnTestSound = document.getElementById('btnTestNotificationSound');
    this.toggleAutoPolling = document.getElementById('toggleAutoPolling');
    this.btnClearLog = document.getElementById('btnClearLog');

    this.notifDot = document.getElementById('notifDot');
    this.notifStatusText = document.getElementById('notifStatusText');

    this.statTotal = document.getElementById('statTotal');
    this.statInTransit = document.getElementById('statInTransit');
    this.statDelivered = document.getElementById('statDelivered');
    this.statException = document.getElementById('statException');

    this.packageList = document.getElementById('packageList');
    this.emptyState = document.getElementById('emptyState');
    this.logStream = document.getElementById('logStream');
    this.emptyLogMsg = document.getElementById('emptyLogMsg');
    this.toastContainer = document.getElementById('toastContainer');
    this.packageCountSub = document.getElementById('packageCountSub');

    this.searchInput = document.getElementById('searchInput');
    this.filterTabs = document.getElementById('filterTabs');

    this.packageModal = document.getElementById('packageModal');
    this.packageForm = document.getElementById('packageForm');
    this.btnCloseModal = document.getElementById('btnCloseModal');
    this.btnCancelModal = document.getElementById('btnCancelModal');
    this.modalTitle = document.getElementById('modalTitle');
    this.packageIdInput = document.getElementById('packageId');

    this.timelineModal = document.getElementById('timelineModal');
    this.btnCloseTimelineModal = document.getElementById('btnCloseTimelineModal');
    this.modalCurrentStatus = document.getElementById('modalCurrentStatus');
    this.modalRouteText = document.getElementById('modalRouteText');
    this.modalCarrierText = document.getElementById('modalCarrierText');
    this.modalEtaText = document.getElementById('modalEtaText');
    this.timelineModalSubId = document.getElementById('timelineModalSubId');
    this.modalTimelineStepper = document.getElementById('modalTimelineStepper');
  }

  bindEvents() {
    if (this.btnQuickAdd && this.quickTrackingNoInput) {
      this.btnQuickAdd.addEventListener('click', () => this.addRealTrackingNo());
      this.quickTrackingNoInput.addEventListener('keydown', (e) => {
        if (e.key === 'Enter') {
          e.preventDefault();
          this.addRealTrackingNo();
        }
      });
    }

    this.btnOpenAddModal.addEventListener('click', () => this.openPackageModal());
    this.btnEmptyAdd.addEventListener('click', () => this.openPackageModal());
    this.btnCloseModal.addEventListener('click', () => this.closePackageModal());
    this.btnCancelModal.addEventListener('click', () => this.closePackageModal());
    this.btnCloseTimelineModal.addEventListener('click', () => this.closeTimelineModal());

    this.packageForm.addEventListener('submit', (e) => this.handlePackageFormSubmit(e));
    this.btnRequestNotif.addEventListener('click', () => this.requestNotificationPermission());

    if (this.btnTestSound) {
      this.btnTestSound.addEventListener('click', () => {
        this.playChimeSound('success');
        this.showToast('通知音效播放正常', 'success');
      });
    }

    this.toggleAutoPolling.addEventListener('change', (e) => {
      if (e.target.checked) {
        this.startRealPolling();
        this.showToast('真實 API 連線監控已開啟 (自動定時抓取物流官網數據)', 'info');
      } else {
        this.stopRealPolling();
        this.showToast('真實連線監控已暫停', 'info');
      }
    });

    this.btnClearLog.addEventListener('click', () => {
      this.logs = [];
      this.saveLogs();
      this.renderLogs();
      this.showToast('日誌已清空', 'info');
    });

    this.searchInput.addEventListener('input', (e) => {
      this.searchQuery = e.target.value.trim().toLowerCase();
      this.renderPackages();
    });

    this.filterTabs.addEventListener('click', (e) => {
      if (e.target.classList.contains('filter-btn')) {
        this.filterTabs.querySelectorAll('.filter-btn').forEach(btn => btn.classList.remove('active'));
        e.target.classList.add('active');
        this.currentFilter = e.target.dataset.filter;
        this.renderPackages();
      }
    });

    window.refreshSinglePackage = (pkgId) => this.fetchPackageRealData(pkgId, true);
    window.advancePackageStatus = (newStatus) => this.manuallyUpdateStatus(newStatus);
  }

  manuallyUpdateStatus(newStatus) {
    if (!this.activeDetailPkgId) return;
    const pkg = this.packages.find(p => p.id === this.activeDetailPkgId);
    if (!pkg) return;

    const oldStatusLabel = STATUS_CONFIG[pkg.status]?.label || pkg.status;
    const newStatusLabel = STATUS_CONFIG[newStatus]?.label || newStatus;
    const timeStr = new Date().toLocaleString('zh-TW', { hour12: false });

    pkg.status = newStatus;
    pkg.updatedAt = timeStr;

    pkg.timeline.unshift({
      status: newStatus,
      title: newStatusLabel,
      desc: `操作人員手動同步/推進狀態至 [${newStatusLabel}]`,
      timestamp: timeStr
    });

    this.savePackages();

    const notifMsg = `[${pkg.carrier}] 單號:${pkg.trackingNo} 狀態變更：${oldStatusLabel} ➔ ${newStatusLabel}`;
    
    this.addLog(pkg.id, pkg.trackingNo, `狀態變更: ${oldStatusLabel} ➔ ${newStatusLabel}`);
    this.sendWebNotification(`物流狀態更新通知 | ${pkg.carrier}`, notifMsg);
    this.playChimeSound(newStatus === 'exception' ? 'exception' : 'success');
    this.showToast(notifMsg, newStatus === 'exception' ? 'error' : 'success');

    this.render();
    this.openTimelineModal(pkg.id);
  }

  getApiBaseUrl() {
    if (window.location.protocol === 'file:') {
      return 'http://localhost:8899';
    }
    return '';
  }

  /* ==========================================================================
     Real Logistics API Operations (連線真實物流 API)
     ========================================================================== */

  async addRealTrackingNo() {
    const trackingNo = this.quickTrackingNoInput.value.trim();
    if (!trackingNo) {
      this.showToast('請輸入物流追蹤單號', 'error');
      return;
    }

    if (this.packages.some(p => p.trackingNo.toLowerCase() === trackingNo.toLowerCase())) {
      this.showToast(`單號 [${trackingNo}] 已在追蹤清單中！`, 'warning');
      return;
    }

    this.showToast(`連線物流官網查詢單號 [${trackingNo}] 中...`, 'info');
    this.quickTrackingNoInput.value = '';

    const apiBase = this.getApiBaseUrl();
    const requestUrl = `${apiBase}/api/track?no=${encodeURIComponent(trackingNo)}`;

    try {
      const res = await fetch(requestUrl);
      const data = await res.json();

      if (data && data.success) {
        const nowStr = new Date().toLocaleString('zh-TW', { hour12: false });
        const newPkg = {
          id: 'pkg_' + Date.now(),
          trackingNo: data.trackingNo,
          carrier: data.carrier,
          title: `包裹 #${data.trackingNo}`,
          origin: '起點轉運站',
          destination: '目的地站點',
          status: data.status || 'in_transit',
          notes: data.message || '真實連線追蹤中',
          createdAt: nowStr,
          updatedAt: data.updatedAt || nowStr,
          timeline: data.timeline && data.timeline.length > 0 ? data.timeline : [
            {
              status: data.status || 'in_transit',
              title: '連線成功',
              desc: `已連接 ${data.carrier} 追蹤系統`,
              timestamp: nowStr
            }
          ]
        };

        this.packages.unshift(newPkg);
        this.savePackages();
        this.addLog(newPkg.id, newPkg.trackingNo, `新增真實追蹤單 [${newPkg.trackingNo}] (${newPkg.carrier})`);
        this.render();
        this.showToast(`成功建立單號 [${newPkg.trackingNo}] 的真實物流監控！`, 'success');
        this.playChimeSound('success');
      } else {
        this.showToast(`查詢失敗: ${data.error || '無法連線到物流服務'}`, 'error');
      }
    } catch (err) {
      console.error('Fetch error:', err);
      this.showToast('網路連線失敗，請確認服務已啟動', 'error');
    }
  }

  async fetchPackageRealData(pkgId, manualTrigger = false) {
    const pkg = this.packages.find(p => p.id === pkgId);
    if (!pkg) return;

    if (manualTrigger) this.showToast(`重新抓取 [${pkg.trackingNo}] 的官網最新物流狀態...`, 'info');

    const apiBase = this.getApiBaseUrl();
    const requestUrl = `${apiBase}/api/track?no=${encodeURIComponent(pkg.trackingNo)}&carrier=${encodeURIComponent(pkg.carrier)}`;

    try {
      const res = await fetch(requestUrl);
      const data = await res.json();

      if (data && data.success) {
        const oldStatus = pkg.status;
        const newStatus = data.status || pkg.status;
        pkg.updatedAt = data.updatedAt || new Date().toLocaleString('zh-TW');

        // 自動校正與更新真實物流公司名稱與營業所
        if (data.carrier) pkg.carrier = data.carrier;
        if (data.station) pkg.station = data.station;
        if (data.latestTime) pkg.latestTime = data.latestTime;

        if (data.timeline && data.timeline.length > 0) {
          pkg.timeline = data.timeline;
        }

        if (oldStatus !== newStatus) {
          const oldLabel = STATUS_CONFIG[oldStatus]?.label || oldStatus;
          const newLabel = STATUS_CONFIG[newStatus]?.label || newStatus;
          pkg.status = newStatus;

          const msg = `物流官網資料更新！[${pkg.carrier}] 單號:${pkg.trackingNo} 狀態改變：${oldLabel} ➔ ${newLabel}`;
          
          this.addLog(pkg.id, pkg.trackingNo, `官網數據更新: ${oldLabel} ➔ ${newLabel}`);
          this.sendWebNotification(`物流官網狀態改變通知 | ${pkg.carrier}`, msg);
          this.playChimeSound(newStatus === 'exception' ? 'exception' : 'success');
          this.showToast(msg, newStatus === 'exception' ? 'error' : 'success');
        } else if (manualTrigger) {
          this.showToast(`[${pkg.trackingNo}] 物流狀態維持最新 (無變化)`, 'info');
        }

        this.savePackages();
        this.render();

        if (this.activeDetailPkgId === pkg.id) {
          this.openTimelineModal(pkg.id);
        }
      }
    } catch (e) {
      console.warn('Refresh error:', e);
    }
  }

  /* ==========================================================================
     Real Polling (定時向官網 API 發送連線)
     ========================================================================== */

  startRealPolling() {
    if (this.autoPollingTimer) clearInterval(this.autoPollingTimer);
    // 每 20 秒抓取一次官網真實 API 數據
    this.autoPollingTimer = setInterval(() => {
      this.pollAllRealPackages();
    }, 20000);
  }

  stopRealPolling() {
    if (this.autoPollingTimer) {
      clearInterval(this.autoPollingTimer);
      this.autoPollingTimer = null;
    }
  }

  async pollAllRealPackages() {
    const activePackages = this.packages.filter(p => p.status !== 'delivered');
    for (const pkg of activePackages) {
      await this.fetchPackageRealData(pkg.id, false);
    }
  }

  /* ==========================================================================
     Notification & Audio Synthesizer Engine
     ========================================================================== */

  checkNotificationPermission() {
    if (!('Notification' in window)) {
      this.notifDot.className = 'status-dot denied';
      this.notifStatusText.textContent = '不支援桌面推播';
      this.btnRequestNotif.disabled = true;
      return;
    }

    if (Notification.permission === 'granted') {
      this.notifDot.className = 'status-dot active';
      this.notifStatusText.textContent = '通知權限：已啟用桌面推播';
      this.btnRequestNotif.style.display = 'none';
    } else if (Notification.permission === 'denied') {
      this.notifDot.className = 'status-dot denied';
      this.notifStatusText.textContent = '通知權限：已封鎖';
    } else {
      this.notifDot.className = 'status-dot warning';
      this.notifStatusText.textContent = '通知權限：點擊開啟桌面推播';
    }
  }

  async requestNotificationPermission() {
    if (!('Notification' in window)) return;
    try {
      const permission = await Notification.requestPermission();
      this.checkNotificationPermission();
      if (permission === 'granted') {
        this.sendWebNotification('TrackPulse 貨物追蹤系統', '桌面推播通知已成功啟用！當官網狀態改變時將自動提示。');
        this.playChimeSound('success');
      }
    } catch (err) {
      console.error(err);
    }
  }

  sendWebNotification(title, body) {
    if ('Notification' in window && Notification.permission === 'granted') {
      try {
        new Notification(title, { body: body });
      } catch (e) {
        console.warn(e);
      }
    }
  }

  playChimeSound(type = 'success') {
    try {
      if (!this.audioCtx) {
        const AudioContext = window.AudioContext || window.webkitAudioContext;
        this.audioCtx = new AudioContext();
      }
      if (this.audioCtx.state === 'suspended') this.audioCtx.resume();

      const now = this.audioCtx.currentTime;
      const osc = this.audioCtx.createOscillator();
      const gain = this.audioCtx.createGain();

      osc.connect(gain);
      gain.connect(this.audioCtx.destination);

      if (type === 'exception') {
        osc.type = 'sawtooth';
        osc.frequency.setValueAtTime(220, now);
        osc.frequency.exponentialRampToValueAtTime(110, now + 0.3);
        gain.gain.setValueAtTime(0.3, now);
        gain.gain.exponentialRampToValueAtTime(0.01, now + 0.35);
        osc.start(now);
        osc.stop(now + 0.35);
      } else {
        osc.type = 'sine';
        osc.frequency.setValueAtTime(587.33, now);
        osc.frequency.setValueAtTime(880, now + 0.12);
        gain.gain.setValueAtTime(0.2, now);
        gain.gain.exponentialRampToValueAtTime(0.01, now + 0.4);
        osc.start(now);
        osc.stop(now + 0.4);
      }
    } catch (e) {
      console.warn(e);
    }
  }

  showToast(message, type = 'info') {
    const toast = document.createElement('div');
    toast.className = `toast ${type}`;
    let icon = `<svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><circle cx="12" cy="12" r="10"></circle><line x1="12" y1="16" x2="12" y2="12"></line><line x1="12" y1="8" x2="12.01" y2="8"></line></svg>`;
    if (type === 'success') icon = `<svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="M22 11.08V12a10 10 0 1 1-5.93-9.14"></path><polyline points="22 4 12 14.01 9 11.01"></polyline></svg>`;
    if (type === 'error') icon = `<svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><circle cx="12" cy="12" r="10"></circle><line x1="15" y1="9" x2="9" y2="15"></line><line x1="9" y1="9" x2="15" y2="15"></line></svg>`;

    toast.innerHTML = `${icon}<span>${message}</span>`;
    this.toastContainer.appendChild(toast);

    setTimeout(() => {
      toast.style.opacity = '0';
      toast.style.transform = 'translateX(20px)';
      setTimeout(() => toast.remove(), 300);
    }, 4000);
  }

  loadPackages() {
    const raw = localStorage.getItem('trackpulse_packages');
    return raw ? JSON.parse(raw) : [];
  }

  savePackages() {
    localStorage.setItem('trackpulse_packages', JSON.stringify(this.packages));
  }

  loadLogs() {
    const raw = localStorage.getItem('trackpulse_logs');
    return raw ? JSON.parse(raw) : [];
  }

  saveLogs() {
    localStorage.setItem('trackpulse_logs', JSON.stringify(this.logs));
  }

  addLog(pkgId, trackingNo, text) {
    const logItem = {
      id: 'log_' + Date.now(),
      pkgId: pkgId,
      trackingNo: trackingNo,
      text: text,
      timestamp: new Date().toLocaleString('zh-TW', { hour12: false })
    };
    this.logs.unshift(logItem);
    if (this.logs.length > 100) this.logs.pop();
    this.saveLogs();
    this.renderLogs();
  }

  openPackageModal(editId = null) {
    this.packageForm.reset();
    if (editId) {
      const pkg = this.packages.find(p => p.id === editId);
      if (pkg) {
        this.modalTitle.textContent = '編輯包裹';
        this.packageIdInput.value = pkg.id;
        document.getElementById('inputTrackingNo').value = pkg.trackingNo;
        document.getElementById('inputCarrier').value = pkg.carrier;
        document.getElementById('inputTitle').value = pkg.title;
        document.getElementById('inputOrigin').value = pkg.origin || '';
        document.getElementById('inputDestination').value = pkg.destination || '';
        document.getElementById('inputStatus').value = pkg.status;
        document.getElementById('inputNotes').value = pkg.notes || '';
      }
    } else {
      this.modalTitle.textContent = '手動新增包裹';
      this.packageIdInput.value = '';
    }
    this.packageModal.classList.add('active');
  }

  closePackageModal() {
    this.packageModal.classList.remove('active');
  }

  handlePackageFormSubmit(e) {
    e.preventDefault();
    const id = this.packageIdInput.value;
    const trackingNo = document.getElementById('inputTrackingNo').value.trim();
    let carrier = document.getElementById('inputCarrier').value;
    
    // 若選擇 auto 或未選，自動智慧辨識物流公司
    if (!carrier || carrier === 'auto') {
      const upperNo = trackingNo.toUpperCase();
      if (upperNo.startsWith('SF')) carrier = '順豐速運 (SF Express)';
      else if (upperNo.startsWith('TW') || upperNo.startsWith('100') || /^\d{14,20}$/.test(trackingNo)) carrier = '中華郵政 (Taiwan Post)';
      else if (/^\d{10,12}$/.test(trackingNo)) carrier = '黑貓宅急便 (Black Cat)';
      else if (upperNo.startsWith('DHL')) carrier = 'DHL Express';
      else carrier = '快遞專車網關';
    }

    let title = document.getElementById('inputTitle').value.trim() || `包裹 #${trackingNo}`;
    const origin = document.getElementById('inputOrigin').value.trim() || '起點站';
    const destination = document.getElementById('inputDestination').value.trim() || '終點站';
    const status = document.getElementById('inputStatus').value;
    const notes = document.getElementById('inputNotes').value.trim();
    const nowStr = new Date().toLocaleString('zh-TW', { hour12: false });

    if (id) {
      const pkg = this.packages.find(p => p.id === id);
      if (pkg) {
        pkg.trackingNo = trackingNo;
        pkg.carrier = carrier;
        pkg.title = title;
        pkg.origin = origin;
        pkg.destination = destination;
        pkg.status = status;
        pkg.notes = notes;
        pkg.updatedAt = nowStr;
        this.savePackages();
        this.render();
        this.showToast(`已更新包裹 [${trackingNo}]`, 'info');
      }
    } else {
      // 防止同單號重複建立卡片
      const existingIndex = this.packages.findIndex(p => p.trackingNo.toLowerCase() === trackingNo.toLowerCase());
      if (existingIndex !== -1) {
        // 自動合併/更新已有卡片
        const existing = this.packages[existingIndex];
        existing.carrier = carrier;
        existing.title = title;
        this.savePackages();
        this.render();
        this.showToast(`單號 [${trackingNo}] 已存在，已自動為您校正為 [${carrier}]！`, 'success');
        this.fetchPackageRealData(existing.id, true);
        this.closePackageModal();
        return;
      }

      const newPkg = {
        id: 'pkg_' + Date.now(),
        trackingNo: trackingNo,
        carrier: carrier,
        title: title,
        origin: origin,
        destination: destination,
        status: status,
        notes: notes,
        createdAt: nowStr,
        updatedAt: nowStr,
        timeline: [
          {
            status: status,
            title: STATUS_CONFIG[status]?.label || '狀態已建立',
            desc: `建立單號 [${trackingNo}] (${carrier})`,
            timestamp: nowStr
          }
        ]
      };
      this.packages.unshift(newPkg);
      this.savePackages();
      this.addLog(newPkg.id, newPkg.trackingNo, `新增單號 [${trackingNo}] (${carrier})`);
      this.render();
      this.showToast(`成功新增包裹 [${trackingNo}]，正在連線官網抓取數據...`, 'success');
      this.fetchPackageRealData(newPkg.id, false);
    }
    this.closePackageModal();
  }

  deletePackage(pkgId) {
    const pkg = this.packages.find(p => p.id === pkgId);
    if (!pkg) return;
    if (confirm(`確定要刪除單號 [${pkg.trackingNo}] 嗎？`)) {
      this.packages = this.packages.filter(p => p.id !== pkgId);
      this.savePackages();
      this.addLog(pkgId, pkg.trackingNo, `刪除單號 [${pkg.trackingNo}]`);
      this.render();
      this.showToast('已刪除包裹紀錄', 'info');
    }
  }

  openTimelineModal(pkgId) {
    const pkg = this.packages.find(p => p.id === pkgId);
    if (!pkg) return;
    this.activeDetailPkgId = pkgId;

    this.timelineModalSubId.textContent = `${pkg.carrier} | 單號: ${pkg.trackingNo}`;
    this.modalCurrentStatus.textContent = STATUS_CONFIG[pkg.status]?.label || pkg.status;
    this.modalRouteText.textContent = pkg.station ? `負責營業所: ${pkg.station}` : `${pkg.origin} ➔ ${pkg.destination}`;
    this.modalCarrierText.textContent = pkg.carrier;
    this.modalEtaText.textContent = pkg.latestTime || pkg.updatedAt;

    let stepperHtml = '';
    pkg.timeline.forEach((item, index) => {
      stepperHtml += `
        <div class="stepper-item ${index === 0 ? 'active' : 'completed'}">
          <span class="stepper-time">${item.timestamp}</span>
          <div class="stepper-title">${item.title}</div>
          <div class="stepper-desc">${item.desc}</div>
        </div>
      `;
    });

    this.modalTimelineStepper.innerHTML = stepperHtml;
    this.timelineModal.classList.add('active');
  }

  closeTimelineModal() {
    this.timelineModal.classList.remove('active');
    this.activeDetailPkgId = null;
  }

  render() {
    this.renderMetrics();
    this.renderPackages();
    this.renderLogs();
  }

  renderMetrics() {
    const total = this.packages.length;
    const inTransit = this.packages.filter(p => ['picked_up', 'in_transit', 'out_for_delivery'].includes(p.status)).length;
    const delivered = this.packages.filter(p => p.status === 'delivered').length;
    const exception = this.packages.filter(p => p.status === 'exception').length;

    this.statTotal.textContent = total;
    this.statInTransit.textContent = inTransit;
    this.statDelivered.textContent = delivered;
    this.statException.textContent = exception;
    this.packageCountSub.textContent = `共 ${total} 筆記錄`;
  }

  renderPackages() {
    let filtered = [...this.packages];
    if (this.currentFilter !== 'all') {
      if (this.currentFilter === 'in_transit') {
        filtered = filtered.filter(p => ['picked_up', 'in_transit'].includes(p.status));
      } else {
        filtered = filtered.filter(p => p.status === this.currentFilter);
      }
    }

    if (this.searchQuery) {
      filtered = filtered.filter(p => 
        p.trackingNo.toLowerCase().includes(this.searchQuery) ||
        p.title.toLowerCase().includes(this.searchQuery) ||
        p.carrier.toLowerCase().includes(this.searchQuery)
      );
    }

    if (filtered.length === 0) {
      this.packageList.style.display = 'none';
      this.emptyState.style.display = 'flex';
      return;
    }

    this.emptyState.style.display = 'none';
    this.packageList.style.display = 'flex';

    let html = '';
    filtered.forEach(pkg => {
      const statusObj = STATUS_CONFIG[pkg.status] || { label: pkg.status, colorClass: 'created', level: 1 };
      
      let progressPercent = 15;
      if (pkg.status === 'picked_up') progressPercent = 35;
      else if (pkg.status === 'in_transit') progressPercent = 65;
      else if (pkg.status === 'out_for_delivery') progressPercent = 85;
      else if (pkg.status === 'delivered' || pkg.status === 'exception') progressPercent = 100;

      html += `
        <div class="package-card" data-status="${pkg.status}">
          <div class="card-top">
            <div>
              <div class="pkg-info-header">
                <span class="carrier-badge">${pkg.carrier}</span>
                <span class="tracking-no">${pkg.trackingNo}</span>
              </div>
              <div class="pkg-title">${pkg.title}</div>
            </div>
            <span class="status-badge ${statusObj.colorClass}">${statusObj.label}</span>
          </div>

          <div class="card-progress-bar">
            <div class="progress-line-track">
              <div class="progress-line-fill" style="width: ${progressPercent}%;"></div>
            </div>
            <div class="step-node ${statusObj.level >= 1 ? 'completed' : ''}">1</div>
            <div class="step-node ${statusObj.level >= 2 ? 'completed' : ''}">2</div>
            <div class="step-node ${statusObj.level >= 3 ? 'completed' : ''}">3</div>
            <div class="step-node ${statusObj.level >= 4 ? 'completed' : ''}">4</div>
            <div class="step-node ${statusObj.level >= 5 ? 'completed' : ''}">✓</div>
          </div>

          <div class="card-footer">
            <div class="route-text">
              <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="M21 10c0 7-9 13-9 13s-9-6-9-13a9 9 0 0 1 18 0z"></path><circle cx="12" cy="10" r="3"></circle></svg>
              <span>${pkg.station ? `負責營業所: ${pkg.station}` : `${pkg.origin} ➔ ${pkg.destination}`}</span>
              ${pkg.latestTime ? `<span style="font-size: 0.75rem; color: var(--text-dim); margin-left: 8px;">(${pkg.latestTime})</span>` : ''}
            </div>
            <div class="card-actions">
              <button class="btn btn-sm btn-ghost" onclick="window.refreshSinglePackage('${pkg.id}')">🔄 官網同步</button>
              <button class="btn btn-sm btn-ghost" onclick="window.engine.openTimelineModal('${pkg.id}')">全歷程</button>
              <button class="btn btn-sm btn-ghost" style="color: #f87171;" onclick="window.engine.deletePackage('${pkg.id}')">刪除</button>
            </div>
          </div>
        </div>
      `;
    });

    this.packageList.innerHTML = html;
  }

  renderLogs() {
    if (this.logs.length === 0) {
      this.emptyLogMsg.style.display = 'block';
      this.logStream.querySelectorAll('.log-item').forEach(el => el.remove());
      return;
    }
    this.emptyLogMsg.style.display = 'none';

    let html = '';
    this.logs.forEach(log => {
      html += `
        <div class="log-item">
          <div class="log-item-header">
            <span class="log-pkg-id">[${log.trackingNo}]</span>
            <span class="log-time">${log.timestamp}</span>
          </div>
          <div class="log-msg">${log.text}</div>
        </div>
      `;
    });

    this.logStream.innerHTML = html;
  }
}

document.addEventListener('DOMContentLoaded', () => {
  window.engine = new RealTrackPulseEngine();
});
