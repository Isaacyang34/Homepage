/**
 * tracker.js — 價格追蹤 & 瀏覽器通知系統
 */

const Tracker = (() => {

  let notifPermission = 'default';

  /* ========== 請求通知權限 ========== */
  const requestPermission = async () => {
    if (!('Notification' in window)) return false;
    if (Notification.permission === 'granted') {
      notifPermission = 'granted';
      return true;
    }
    if (Notification.permission === 'denied') {
      notifPermission = 'denied';
      return false;
    }
    const result = await Notification.requestPermission();
    notifPermission = result;
    return result === 'granted';
  };

  const isNotificationSupported = () => 'Notification' in window;
  const isPermissionGranted = () => Notification.permission === 'granted';

  /* ========== 發送瀏覽器通知 ========== */
  const sendBrowserNotification = (title, body, icon = '') => {
    if (!isPermissionGranted()) return;
    try {
      const n = new Notification(title, {
        body,
        icon: icon || 'assets/favicon.svg',
        badge: 'assets/favicon.svg',
        tag: 'flight-price-alert',
        requireInteraction: true,
      });
      n.onclick = () => { window.focus(); n.close(); };
      setTimeout(() => n.close(), 10000);
    } catch (e) {
      console.warn('Notification error:', e);
    }
  };

  /* ========== Toast 通知（頁面內） ========== */
  const showToast = (message, type = 'info', duration = 4000) => {
    const container = document.getElementById('toast-container');
    if (!container) return;

    const icons = { success: '✅', info: '💡', warning: '⚠️', alert: '🎯' };
    const toast = document.createElement('div');
    toast.className = `toast ${type}`;
    toast.innerHTML = `<span>${icons[type] || '💡'}</span><span>${message}</span>`;

    container.appendChild(toast);
    setTimeout(() => {
      toast.style.animation = 'toastOut 0.3s ease forwards';
      setTimeout(() => toast.remove(), 300);
    }, duration);
  };

  /* ========== 檢查追蹤方案（頁面載入時執行） ========== */
  const checkTrackedPlans = () => {
    const tracked = Storage.getTracked();
    if (!tracked.length) return;

    const settings = Storage.getSettings();
    const results = [];
    const today = new Date().toISOString().split('T')[0];

    tracked.forEach(plan => {
      const { newPrice, isLowest, history } = DataEngine.refreshTrackedPrice(plan);
      const targetPrice = settings.targetPrices[plan.id];
      const hitTarget = targetPrice && newPrice <= targetPrice;
      const history7 = history.slice(-7);

      if (isLowest || hitTarget) {
        results.push({ plan, newPrice, isLowest, hitTarget, history7 });
      }
    });

    if (results.length > 0) {
      results.forEach(({ plan, newPrice, isLowest, hitTarget }) => {
        const title = hitTarget
          ? `🎯 已達目標價格！${plan.fromCity?.name || ''} → ${plan.toCity?.name || ''}`
          : `📉 7天最低價！${plan.fromCity?.name || ''} → ${plan.toCity?.name || ''}`;

        const body = `NT$${newPrice.toLocaleString()} ${hitTarget ? '（已低於您設定的目標）' : '（7天內最低）'}`;

        sendBrowserNotification(title, body);
        showToast(`${title} — ${body}`, 'alert', 8000);
      });
    }

    return results;
  };

  /* ========== 模擬「每日刷新」邏輯 ========== */
  const initDailyRefresh = () => {
    const lastCheck = localStorage.getItem('fpt_last_check');
    const today = new Date().toDateString();

    if (lastCheck !== today) {
      localStorage.setItem('fpt_last_check', today);
      // 延遲 2 秒後檢查，避免頁面剛載入就發通知
      setTimeout(() => {
        const alerts = checkTrackedPlans();
        if (alerts?.length) {
          console.log(`[Tracker] 發現 ${alerts.length} 筆低價提醒`);
        }
      }, 2000);
    }
  };

  /* ========== 格式化追蹤卡片 ========== */
  const renderTrackedSection = (containerId) => {
    const container = document.getElementById(containerId);
    if (!container) return;

    const tracked = Storage.getTracked();
    if (!tracked.length) {
      container.innerHTML = `
        <div class="empty-state">
          <div class="empty-state-icon">🔔</div>
          <h3>尚未追蹤任何方案</h3>
          <p>在搜尋結果中點擊「追蹤此方案」，即可在此查看價格提醒</p>
        </div>`;
      return;
    }

    container.innerHTML = tracked.map(plan => {
      const history = Storage.getPriceHistory(plan.id);
      const currentPrice = history.length
        ? history[history.length - 1].price
        : plan.totalPrice;
      const prices = history.map(h => h.price);
      const minPrice = prices.length ? Math.min(...prices) : currentPrice;
      const isLowest = currentPrice === minPrice;
      const settings = Storage.getSettings();
      const targetPrice = settings.targetPrices[plan.id];

      return `
        <div class="alert-card" id="alert-${plan.id}">
          <div class="alert-content">
            <div class="alert-icon-wrap">${plan.airline?.emoji || '✈️'}</div>
            <div>
              <div class="alert-title">
                ${plan.fromCity?.name || '—'} → ${plan.toCity?.name || '—'}
                ${isLowest ? '<span class="badge badge-green" style="margin-left:8px">🎯 7天最低</span>' : ''}
              </div>
              <div class="alert-desc">
                ${plan.airline?.name || ''} ·
                現價 <strong>NT$${currentPrice.toLocaleString()}</strong> ·
                7天最低 NT$${minPrice.toLocaleString()}
                ${targetPrice ? ` · 目標 NT$${targetPrice.toLocaleString()}` : ''}
              </div>
            </div>
          </div>
          <div style="display:flex;gap:10px;flex-shrink:0">
            <button class="btn-track tracking" onclick="App.setTargetPriceDialog('${plan.id}')">
              💰 設定目標價
            </button>
            <button class="btn-compare" onclick="App.removeTracked('${plan.id}')">
              🗑️ 移除
            </button>
          </div>
        </div>`;
    }).join('');
  };

  return {
    requestPermission,
    isNotificationSupported,
    isPermissionGranted,
    sendBrowserNotification,
    showToast,
    checkTrackedPlans,
    initDailyRefresh,
    renderTrackedSection,
  };
})();
