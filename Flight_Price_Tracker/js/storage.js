/**
 * storage.js — localStorage 管理模組
 */

const Storage = (() => {
  const KEYS = {
    SEARCHES: 'fpt_searches',
    TRACKED: 'fpt_tracked',
    PRICE_HISTORY: 'fpt_price_history',
    SETTINGS: 'fpt_settings',
    COMPARE_LIST: 'fpt_compare',
  };

  const get = (key, fallback = null) => {
    try {
      const raw = localStorage.getItem(key);
      return raw ? JSON.parse(raw) : fallback;
    } catch { return fallback; }
  };

  const set = (key, value) => {
    try { localStorage.setItem(key, JSON.stringify(value)); } catch {}
  };

  // 搜尋記錄（最多 10 筆）
  const getSearchHistory = () => get(KEYS.SEARCHES, []);
  const addSearchHistory = (entry) => {
    const list = getSearchHistory().filter(
      s => !(s.from === entry.from && s.to === entry.to && s.dates === entry.dates)
    );
    list.unshift({ ...entry, timestamp: Date.now() });
    set(KEYS.SEARCHES, list.slice(0, 10));
  };
  const clearSearchHistory = () => set(KEYS.SEARCHES, []);

  // 追蹤方案
  const getTracked = () => get(KEYS.TRACKED, []);
  const addTracked = (plan) => {
    const list = getTracked().filter(t => t.id !== plan.id);
    list.unshift({ ...plan, trackedAt: Date.now() });
    set(KEYS.TRACKED, list);
  };
  const removeTracked = (id) => {
    set(KEYS.TRACKED, getTracked().filter(t => t.id !== id));
  };
  const isTracked = (id) => getTracked().some(t => t.id === id);

  // 7 天價格歷史
  const getPriceHistory = (planId) => {
    const all = get(KEYS.PRICE_HISTORY, {});
    return all[planId] || [];
  };
  const addPricePoint = (planId, price, date) => {
    const all = get(KEYS.PRICE_HISTORY, {});
    if (!all[planId]) all[planId] = [];
    // 去重（同日只保留最新）
    all[planId] = all[planId].filter(p => p.date !== date);
    all[planId].push({ date, price, ts: Date.now() });
    // 只保留最近 30 天
    all[planId].sort((a, b) => a.ts - b.ts);
    all[planId] = all[planId].slice(-30);
    set(KEYS.PRICE_HISTORY, all);
  };

  // 設定
  const getSettings = () => get(KEYS.SETTINGS, {
    notificationsEnabled: false,
    targetPrices: {},
    currency: 'TWD',
    language: 'zh-TW',
  });
  const saveSettings = (s) => set(KEYS.SETTINGS, s);
  const setTargetPrice = (planId, price) => {
    const s = getSettings();
    s.targetPrices[planId] = price;
    saveSettings(s);
  };

  return {
    getSearchHistory, addSearchHistory, clearSearchHistory,
    getTracked, addTracked, removeTracked, isTracked,
    getPriceHistory, addPricePoint,
    getSettings, saveSettings, setTargetPrice,
  };
})();
