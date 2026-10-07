/**
 * 零知識端到端加密投資庫存儀表板 (Web Crypto API 原生純前端)
 * 具備設備智慧自動分析、動態形態自我調適 (手機卡片流 / 桌面專業表格)
 */

const DEFAULT_FIREBASE_URL = "https://my-stock-tracker-2a94e-default-rtdb.asia-southeast1.firebasedatabase.app";
const PALETTE = ["#3a86ff", "#00f090", "#ffbe0b", "#ff3366", "#8338ec", "#06d6a0", "#118ab2", "#fb5607"];

let state = {
  user: "",
  password: "",
  rtdbUrl: DEFAULT_FIREBASE_URL,
  decryptedData: null,
  privacyMode: false,
  rawPositions: [],
  deviceType: "desktop", // "mobile" | "tablet" | "desktop"
  currentViewMode: "table", // 預設以表格視圖為主
  sortField: "symbol",
  sortOrder: "asc"
};

// --- 設備智慧分析引擎 ---

function detectDeviceProfile() {
  const width = window.innerWidth;
  const isTouch = ('ontouchstart' in window) || (navigator.maxTouchPoints > 0);
  const ua = navigator.userAgent.toLowerCase();

  let type = "desktop";
  let label = "💻 桌面專業表格模式";

  if (/iphone|android(?!.*tablet)|ipod/i.test(ua) || width <= 768) {
    type = "mobile";
    label = "📱 手機卡片流模式";
  } else if (/ipad|tablet|playbook|silk/i.test(ua) || (isTouch && width <= 1024)) {
    type = "tablet";
    label = " 觸控平板模式";
  }

  return { type, label };
}

// --- Web Crypto 原生解密模組 ---

function hexToBytes(hex) {
  const bytes = new Uint8Array(hex.length / 2);
  for (let i = 0; i < bytes.length; i++) {
    bytes[i] = parseInt(hex.substr(i * 2, 2), 16);
  }
  return bytes;
}

function base64ToBytes(base64) {
  const binString = atob(base64);
  const bytes = new Uint8Array(binString.length);
  for (let i = 0; i < binString.length; i++) {
    bytes[i] = binString.charCodeAt(i);
  }
  return bytes;
}

async function decryptAesGcm(encryptedPayload, password, secretKey = "") {
  // 1. 防回滾重放檢驗 (Anti-Rollback Sequence Counter Check)
  if (encryptedPayload.sync_seq !== undefined) {
    const userMatch = (encryptedPayload.aad_str && encryptedPayload.aad_str.match(/USER:([^|]+)/));
    const userKey = userMatch ? userMatch[1].toLowerCase() : (state.user || "default");
    const lastSeqKey = `last_sync_seq_${userKey}`;
    const lastSeq = parseInt(localStorage.getItem(lastSeqKey) || "0", 10);

    if (encryptedPayload.sync_seq < lastSeq) {
      throw new Error(`[防回滾安全警示] 雲端序號 (${encryptedPayload.sync_seq}) 低於本地已知序號 (${lastSeq})！疑似遭遇重放攻擊或雲端資料回滾。`);
    }
    localStorage.setItem(lastSeqKey, String(encryptedPayload.sync_seq));
  }

  const salt = hexToBytes(encryptedPayload.salt_hex);
  const nonce = hexToBytes(encryptedPayload.nonce_hex);
  const ciphertext = base64ToBytes(encryptedPayload.ciphertext_b64);

  const enc = new TextEncoder();

  // 2. 雙因子金鑰材料結合 (Password + 128-bit Secret Key)
  let combinedSecret = password.trim();
  if (secretKey && secretKey.trim()) {
    combinedSecret = `${password.trim()}:${secretKey.trim()}`;
  }
  const passBytes = enc.encode(combinedSecret);

  // 3. 導入原始金鑰材料
  const baseKey = await crypto.subtle.importKey(
    "raw",
    passBytes,
    { name: "PBKDF2" },
    false,
    ["deriveKey"]
  );

  // 4. PBKDF2 疊代次數 (OWASP 2023 最新規範 600,000 次)
  let iterations = 600000;
  if (encryptedPayload.kdf && encryptedPayload.kdf.includes("100K")) {
    iterations = 100000; // 向下相容舊版封包
  }

  const aesKey = await crypto.subtle.deriveKey(
    {
      name: "PBKDF2",
      salt: salt,
      iterations: iterations,
      hash: "SHA-256"
    },
    baseKey,
    { name: "AES-GCM", length: 256 },
    false,
    ["decrypt"]
  );

  // 5. AAD 關聯認證數據綁定 (防跨帳號搬移與標頭竄改)
  let aadBytes = new Uint8Array(0);
  if (encryptedPayload.aad_str) {
    aadBytes = enc.encode(encryptedPayload.aad_str);
  }

  // 6. 原生 AES-GCM 解密 (含 128-bit Auth Tag 與 AAD 校驗)
  const decryptedBuf = await crypto.subtle.decrypt(
    {
      name: "AES-GCM",
      iv: nonce,
      additionalData: aadBytes
    },
    aesKey,
    ciphertext
  );

  // 7. 解碼 4-byte 大端長度前綴並分離 32 KB 固定塊隨機填充
  let realBytes;
  if (decryptedBuf.byteLength >= 4) {
    const view = new DataView(decryptedBuf);
    const declaredLen = view.getUint32(0, false); // 大端 uint32
    if (declaredLen > 0 && declaredLen <= decryptedBuf.byteLength - 4) {
      realBytes = new Uint8Array(decryptedBuf, 4, declaredLen);
    } else {
      realBytes = new Uint8Array(decryptedBuf);
    }
  } else {
    realBytes = new Uint8Array(decryptedBuf);
  }

  const dec = new TextDecoder();
  const plainJson = dec.decode(realBytes);
  return JSON.parse(plainJson);
}

// --- DOM 事件與控制器 ---

document.addEventListener("DOMContentLoaded", () => {
  const loginModal = document.getElementById("login-modal");
  const appContainer = document.getElementById("app-container");
  const loginForm = document.getElementById("login-form");
  const inputUser = document.getElementById("input-user");
  const inputPassword = document.getElementById("input-password");
  const inputSecretKey = document.getElementById("input-secret-key");
  const inputRtdb = document.getElementById("input-rtdb");
  const loginStatus = document.getElementById("login-status");
  const toggleAdvanced = document.getElementById("toggle-advanced");
  const advancedGroup = document.getElementById("advanced-group");

  const btnLogout = document.getElementById("btn-logout");
  const btnRefresh = document.getElementById("btn-refresh");
  const btnTogglePrivacy = document.getElementById("btn-toggle-privacy");
  const privacyText = document.getElementById("privacy-text");
  const filterInput = document.getElementById("filter-input");

  const btnViewCards = document.getElementById("btn-view-cards");
  const btnViewTable = document.getElementById("btn-view-table");

  // 手機底部浮動按鈕
  const mBtnViewToggle = document.getElementById("m-btn-view-toggle");
  const mViewLabel = document.getElementById("m-view-label");
  const mBtnPrivacy = document.getElementById("m-btn-privacy");
  const mBtnRefresh = document.getElementById("m-btn-refresh");
  const mBtnLogout = document.getElementById("m-btn-logout");

  // 1. 執行設備自動分析與形態預設
  const profile = detectDeviceProfile();
  state.deviceType = profile.type;
  
  // 檢查是否有手動喜好，若無則依設備自動切換：手機/平板預設卡片流，桌機預設專業表格
  const savedPrefView = localStorage.getItem("portfolio_pref_view");
  state.currentViewMode = savedPrefView || "table";

  document.getElementById("device-badge").textContent = profile.label;

  // 載入歷史帳號與設備 Secret Key
  const cachedUser = localStorage.getItem("last_portfolio_user");
  if (cachedUser) inputUser.value = cachedUser;

  const savedKeyTag = document.getElementById("saved-key-tag");
  const btnClearKey = document.getElementById("btn-clear-key");

  const cachedSecretKey = localStorage.getItem("last_portfolio_secret_key");
  if (cachedSecretKey && inputSecretKey) {
    inputSecretKey.value = cachedSecretKey;
    if (savedKeyTag) savedKeyTag.style.display = "inline";
    if (btnClearKey) btnClearKey.style.display = "inline";
  }

  if (btnClearKey) {
    btnClearKey.addEventListener("click", () => {
      localStorage.removeItem("last_portfolio_secret_key");
      if (inputSecretKey) inputSecretKey.value = "";
      if (savedKeyTag) savedKeyTag.style.display = "none";
      btnClearKey.style.display = "none";
    });
  }

  if (inputSecretKey) {
    inputSecretKey.addEventListener("input", () => {
      const val = inputSecretKey.value.trim();
      if (val) {
        if (savedKeyTag) savedKeyTag.style.display = "inline";
        if (btnClearKey) btnClearKey.style.display = "inline";
      } else {
        if (savedKeyTag) savedKeyTag.style.display = "none";
        if (btnClearKey) btnClearKey.style.display = "none";
      }
    });
  }

  const cachedUrl = localStorage.getItem("last_rtdb_url");
  if (cachedUrl) inputRtdb.value = cachedUrl;

  toggleAdvanced.addEventListener("click", () => {
    advancedGroup.classList.toggle("hidden");
  });

  // 視圖切換事件
  btnViewCards.addEventListener("click", () => switchViewMode("cards"));
  btnViewTable.addEventListener("click", () => switchViewMode("table"));

  if (mBtnViewToggle) {
    mBtnViewToggle.addEventListener("click", () => {
      const nextMode = state.currentViewMode === "cards" ? "table" : "cards";
      switchViewMode(nextMode);
    });
  }

  // 視窗尺寸改變時自我調適
  window.addEventListener("resize", () => {
    const newProfile = detectDeviceProfile();
    if (newProfile.type !== state.deviceType) {
      state.deviceType = newProfile.type;
      document.getElementById("device-badge").textContent = newProfile.label;
      if (!localStorage.getItem("portfolio_pref_view")) {
        state.currentViewMode = (state.deviceType === "desktop" ? "table" : "cards");
        applyViewModeUI();
      }
    }
  });

  // 登入解密提交
  loginForm.addEventListener("submit", async (e) => {
    e.preventDefault();
    const user = inputUser.value.trim().toLowerCase();
    const pass = inputPassword.value;
    const secretKey = inputSecretKey ? inputSecretKey.value.trim() : "";
    const customUrl = inputRtdb.value.trim();

    if (!user || !pass) {
      showStatus("請輸入使用者代號與專屬密碼！", "err");
      return;
    }

    state.user = user;
    state.password = pass;
    state.secretKey = secretKey;
    state.rtdbUrl = customUrl ? customUrl.replace(/\/$/, "") : DEFAULT_FIREBASE_URL;

    localStorage.setItem("last_portfolio_user", user);
    if (secretKey) {
      localStorage.setItem("last_portfolio_secret_key", secretKey);
      if (savedKeyTag) savedKeyTag.style.display = "inline";
      if (btnClearKey) btnClearKey.style.display = "inline";
    }
    if (customUrl) localStorage.setItem("last_rtdb_url", customUrl);

    showStatus("正在連線雲端資料庫...", "ok");

    try {
      const endpoint = `${state.rtdbUrl}/portfolios/${user}.json`;
      const resp = await fetch(endpoint);
      if (!resp.ok) throw new Error(`HTTP ${resp.status} - 無法存取雲端資料`);

      const encPayload = await resp.json();
      if (!encPayload || !encPayload.ciphertext_b64) {
        throw new Error(`找不到使用者 [${user}] 的資料！請先於電腦本機點擊「一鍵同步」。`);
      }

      showStatus("已取得資料，正在本機解密中...", "ok");

      const decrypted = await decryptAesGcm(encPayload, pass, secretKey);
      state.decryptedData = decrypted;
      state.rawPositions = decrypted.positions || [];

      loginModal.classList.add("hidden");
      appContainer.classList.remove("hidden");

      applyViewModeUI();
      renderDashboard(decrypted, encPayload.updated_at);
      showStatus("", "");
    } catch (err) {
      console.error(err);
      showStatus(`解密失敗: ${err.message || "密碼錯誤或金鑰不符"}`, "err");
    }
  });

  // 安全登出銷毀記憶體
  const performLogout = () => {
    state.password = "";
    state.decryptedData = null;
    state.rawPositions = [];
    inputPassword.value = "";
    appContainer.classList.add("hidden");
    loginModal.classList.remove("hidden");
    showStatus("已安全銷毀本機記憶體並登出", "ok");
  };

  btnLogout.addEventListener("click", performLogout);
  if (mBtnLogout) mBtnLogout.addEventListener("click", performLogout);

  // 重新整理
  const performRefresh = () => {
    if (state.user && state.password) {
      loginForm.dispatchEvent(new Event("submit"));
    }
  };
  btnRefresh.addEventListener("click", performRefresh);
  if (mBtnRefresh) mBtnRefresh.addEventListener("click", performRefresh);

  // 隱私脫敏切換
  const togglePrivacy = () => {
    state.privacyMode = !state.privacyMode;
    privacyText.textContent = state.privacyMode ? "顯示金額" : "隱藏金額";
    if (state.decryptedData) {
      renderDashboard(state.decryptedData);
    }
  };
  btnTogglePrivacy.addEventListener("click", togglePrivacy);
  if (mBtnPrivacy) mBtnPrivacy.addEventListener("click", togglePrivacy);

  // 搜尋過濾
  filterInput.addEventListener("input", (e) => {
    const q = e.target.value.trim().toLowerCase();
    renderContentByMode(q);
  });

  // 表頭點擊排序監聽
  const sortHeaders = document.querySelectorAll("#portfolio-table th.th-sortable");
  sortHeaders.forEach(th => {
    th.addEventListener("click", () => {
      const field = th.getAttribute("data-sort");
      if (!field) return;
      if (state.sortField === field) {
        state.sortOrder = state.sortOrder === "asc" ? "desc" : "asc";
      } else {
        state.sortField = field;
        state.sortOrder = ["symbol", "name"].includes(field) ? "asc" : "desc";
      }
      updateSortHeaderUI();
      renderContentByMode(filterInput.value.trim().toLowerCase());
    });
  });

  function updateSortHeaderUI() {
    const headers = document.querySelectorAll("#portfolio-table th.th-sortable");
    headers.forEach(th => {
      const field = th.getAttribute("data-sort");
      th.classList.remove("sorted-asc", "sorted-desc");
      if (field === state.sortField) {
        th.classList.add(state.sortOrder === "asc" ? "sorted-asc" : "sorted-desc");
      }
    });
  }

  function showStatus(msg, type) {
    loginStatus.textContent = msg;
    loginStatus.className = `status-msg status-${type}`;
  }

  function switchViewMode(mode) {
    state.currentViewMode = mode;
    localStorage.setItem("portfolio_pref_view", mode);
    applyViewModeUI();
    if (state.decryptedData) {
      renderContentByMode(filterInput.value.trim().toLowerCase());
    }
  }

  function applyViewModeUI() {
    const isCards = state.currentViewMode === "cards";
    btnViewCards.classList.toggle("active", isCards);
    btnViewTable.classList.toggle("active", !isCards);

    document.getElementById("mobile-cards-view").classList.toggle("hidden", !isCards);
    document.getElementById("desktop-table-view").classList.toggle("hidden", isCards);

    if (mViewLabel) {
      mViewLabel.textContent = isCards ? "表格" : "卡片";
    }

    const indicator = document.getElementById("active-view-indicator");
    if (indicator) {
      indicator.textContent = isCards ? "卡片模式 ‧ 點擊上方按鈕可切換專業表格" : "完整數據表格 ‧ 點擊表頭可快速排序";
    }
  }
});

// --- 數據標準化與格式化輔助函式 ---

function normalizePosition(pos, divs = {}) {
  const shares = parseFloat(pos.shares) || 0;
  const costPrice = parseFloat(pos.cost_price) || 0;
  const curPrice = parseFloat(pos.current_price !== undefined ? pos.current_price : pos.cost_price) || 0;

  const totalCost = parseFloat(pos.total_cost !== undefined ? pos.total_cost : (shares * costPrice)) || 0;
  const marketVal = parseFloat(pos.market_val !== undefined ? pos.market_val : (shares * curPrice)) || 0;

  const unrealizedPnl = parseFloat(pos.unrealized_pnl !== undefined ? pos.unrealized_pnl : (marketVal - totalCost)) || 0;
  const roiPct = parseFloat(pos.roi_pct !== undefined ? pos.roi_pct : (totalCost > 0 ? (unrealizedPnl / totalCost) * 100 : 0)) || 0;

  // 當日損益
  const yClose = parseFloat(pos.yesterday_close !== undefined ? pos.yesterday_close : curPrice) || curPrice;
  const dayPnl = parseFloat(pos.day_pnl !== undefined ? pos.day_pnl : ((curPrice - yClose) * shares)) || 0;
  const dayPct = parseFloat(pos.day_pct !== undefined ? pos.day_pct : (yClose > 0 ? ((curPrice - yClose) / yClose) * 100 : 0)) || 0;

  // 當週損益
  const wClose = parseFloat(pos.week_close !== undefined ? pos.week_close : yClose) || yClose;
  const weekPnl = parseFloat(pos.week_pnl !== undefined ? pos.week_pnl : ((curPrice - wClose) * shares)) || 0;
  const weekPct = parseFloat(pos.week_pct !== undefined ? pos.week_pct : (wClose > 0 ? ((curPrice - wClose) / wClose) * 100 : 0)) || 0;

  // 股息資訊
  const divInfo = divs[pos.symbol] || {};
  const cashDiv = parseFloat(pos.cash_dividend !== undefined ? pos.cash_dividend : (divInfo.annual_div || divInfo.cash_dividend || 0)) || 0;
  const singleDiv = parseFloat(pos.single_dividend !== undefined ? pos.single_dividend : (divInfo.single_amt || divInfo.single_dividend || cashDiv)) || 0;
  const frequency = pos.frequency || divInfo.frequency || "年配";
  const totalDiv = parseFloat(pos.total_dividend !== undefined ? pos.total_dividend : (shares * cashDiv)) || 0;
  const yieldOnCost = parseFloat(pos.yield_on_cost !== undefined ? pos.yield_on_cost : (costPrice > 0 ? (cashDiv / costPrice) * 100 : 0)) || 0;
  const yieldOnPrice = parseFloat(pos.yield_on_price !== undefined ? pos.yield_on_price : (curPrice > 0 ? (cashDiv / curPrice) * 100 : 0)) || 0;
  const histDiv = parseFloat(pos.hist_div_received !== undefined ? pos.hist_div_received : (divInfo.hist_div_received || 0)) || 0;

  return {
    ...pos,
    shares,
    cost_price: costPrice,
    current_price: curPrice,
    total_cost: totalCost,
    market_val: marketVal,
    unrealized_pnl: unrealizedPnl,
    roi_pct: roiPct,
    yesterday_close: yClose,
    day_pnl: dayPnl,
    day_pct: dayPct,
    week_close: wClose,
    week_pnl: weekPnl,
    week_pct: weekPct,
    cash_dividend: cashDiv,
    single_dividend: singleDiv,
    frequency,
    total_dividend: totalDiv,
    yield_on_cost: yieldOnCost,
    yield_on_price: yieldOnPrice,
    hist_div_received: histDiv
  };
}

function fmtNum(n) {
  if (isNaN(n) || n === null || n === undefined) return "--";
  return Number(n).toLocaleString("en-US");
}

function fmtSign(n, prefix = "$ ") {
  if (isNaN(n) || n === null || n === undefined) return "--";
  const num = Math.round(n);
  const sign = num > 0 ? "+" : (num < 0 ? "-" : "");
  return `${sign}${prefix}${fmtNum(Math.abs(num))}`;
}

function fmtPctSign(n) {
  if (isNaN(n) || n === null || n === undefined) return "--";
  const num = Number(n);
  const sign = num > 0 ? "+" : (num < 0 ? "-" : "");
  return `${sign}${Math.abs(num).toFixed(2)}%`;
}

// --- 渲染畫面核心 ---

function renderDashboard(data, cloudUpdateTime) {
  document.getElementById("user-badge").textContent = `👤 使用者: ${state.user}`;
  const syncTime = cloudUpdateTime || data.exported_at || "剛剛";
  document.getElementById("sync-time-lbl").textContent = `同步時間: ${syncTime}`;

  const divs = data.dividends || {};
  const rawPositions = data.positions || [];
  const normalized = rawPositions.map(p => normalizePosition(p, divs));
  state.normalizedPositions = normalized;

  // 計算全庫存總合指標
  let totalCost = 0;
  let totalMarketVal = 0;
  let totalUnrealizedPnl = 0;
  let totalDayPnl = 0;
  let totalWeekPnl = 0;
  let totalEstDiv = 0;
  let totalHistDiv = 0;

  normalized.forEach(p => {
    totalCost += p.total_cost;
    totalMarketVal += p.market_val;
    totalUnrealizedPnl += p.unrealized_pnl;
    totalDayPnl += p.day_pnl;
    totalWeekPnl += p.week_pnl;
    totalEstDiv += p.total_dividend;
    totalHistDiv += p.hist_div_received;
  });

  const totalRoiPct = totalCost > 0 ? (totalUnrealizedPnl / totalCost) * 100 : 0;
  const totalDayPct = (totalMarketVal - totalDayPnl > 0) ? (totalDayPnl / (totalMarketVal - totalDayPnl)) * 100 : 0;
  const totalWeekPct = (totalMarketVal - totalWeekPnl > 0) ? (totalWeekPnl / (totalMarketVal - totalWeekPnl)) * 100 : 0;
  const portfolioYield = totalCost > 0 ? (totalEstDiv / totalCost) * 100 : 0;

  const isPrivate = state.privacyMode;

  // 1. 總市值 / 投資成本
  document.getElementById("val-total-market").textContent = isPrivate ? "$ ********" : `$ ${fmtNum(Math.round(totalMarketVal))}`;
  document.getElementById("val-total-cost").textContent = isPrivate ? "原始成本: $ ********" : `原始成本: $ ${fmtNum(Math.round(totalCost))}`;

  // 2. 總未實現損益 (報酬率)
  const pnlEl = document.getElementById("val-total-pnl");
  pnlEl.className = `metric-val ${totalUnrealizedPnl >= 0 ? "text-red" : "text-green"}`;
  pnlEl.textContent = isPrivate ? `${fmtPctSign(totalRoiPct)}` : `${fmtSign(totalUnrealizedPnl)} (${fmtPctSign(totalRoiPct)})`;

  // 3. 當日總損益 (今日波動)
  const dayEl = document.getElementById("val-day-pnl");
  dayEl.className = `metric-val ${totalDayPnl >= 0 ? "text-red" : "text-green"}`;
  dayEl.textContent = isPrivate ? `${fmtPctSign(totalDayPct)}` : `${fmtSign(totalDayPnl)} (${fmtPctSign(totalDayPct)})`;

  // 4. 當週總損益 (本週波動)
  const weekEl = document.getElementById("val-week-pnl");
  weekEl.className = `metric-val ${totalWeekPnl >= 0 ? "text-red" : "text-green"}`;
  weekEl.textContent = isPrivate ? `${fmtPctSign(totalWeekPct)}` : `${fmtSign(totalWeekPnl)} (${fmtPctSign(totalWeekPct)})`;

  // 5. 預估全年總股息 (年化殖利率)
  document.getElementById("val-total-div").textContent = isPrivate ? `${portfolioYield.toFixed(2)}%` : `$ ${fmtNum(Math.round(totalEstDiv))} (${portfolioYield.toFixed(2)}%)`;

  // 6. 歷年累計已領股息
  document.getElementById("val-hist-div").textContent = isPrivate ? "$ ********" : `$ ${fmtNum(Math.round(totalHistDiv))}`;

  document.getElementById("stock-count-badge").textContent = `在庫標的: ${normalized.length} 檔`;

  // 渲染資產配置條
  renderAllocation(normalized, totalMarketVal);

  // 渲染當前視圖模式
  renderContentByMode(document.getElementById("filter-input").value.trim().toLowerCase());
}

function renderContentByMode(keyword) {
  if (state.currentViewMode === "cards") {
    renderCardsView(keyword);
  } else {
    renderTableView(keyword);
  }
}

function renderAllocation(positions, totalMarketVal) {
  const barEl = document.getElementById("allocation-bar");
  const legendEl = document.getElementById("allocation-legend");
  barEl.innerHTML = "";
  legendEl.innerHTML = "";

  if (totalMarketVal <= 0 || positions.length === 0) return;

  const sorted = [...positions].sort((a, b) => b.market_val - a.market_val);

  sorted.forEach((pos, idx) => {
    const pct = ((pos.market_val / totalMarketVal) * 100).toFixed(1);
    if (parseFloat(pct) <= 0) return;

    const color = PALETTE[idx % PALETTE.length];

    const seg = document.createElement("div");
    seg.className = "alloc-segment";
    seg.style.width = `${pct}%`;
    seg.style.backgroundColor = color;
    seg.title = `${pos.symbol} ${pos.name}: ${pct}%`;
    barEl.appendChild(seg);

    const item = document.createElement("div");
    item.className = "legend-item";
    item.innerHTML = `
      <span class="legend-dot" style="background-color: ${color}"></span>
      <span>${pos.symbol} ${pos.name} (<b>${pct}%</b>)</span>
    `;
    legendEl.appendChild(item);
  });
}

// --- 渲染：專業完整數據表格 (Table View) ---

function renderTableView(keyword) {
  const tbody = document.getElementById("portfolio-tbody");
  const tfoot = document.getElementById("portfolio-tfoot");
  tbody.innerHTML = "";
  if (tfoot) tfoot.innerHTML = "";

  const positions = state.normalizedPositions || [];
  const isPrivate = state.privacyMode;

  let filtered = positions.filter(pos => {
    if (!keyword) return true;
    const s = (pos.symbol || "").toLowerCase();
    const n = (pos.name || "").toLowerCase();
    return s.includes(keyword) || n.includes(keyword);
  });

  // 排序
  if (state.sortField) {
    const f = state.sortField;
    const isAsc = state.sortOrder === "asc";
    filtered.sort((a, b) => {
      let va = a[f];
      let vb = b[f];
      if (typeof va === "string") {
        return isAsc ? va.localeCompare(vb) : vb.localeCompare(va);
      }
      va = parseFloat(va) || 0;
      vb = parseFloat(vb) || 0;
      return isAsc ? va - vb : vb - va;
    });
  }

  if (filtered.length === 0) {
    tbody.innerHTML = `<tr><td colspan="11" style="text-align: center; color: #8e95a5; padding: 30px;">無符合標的</td></tr>`;
    return;
  }

  // 累計合計列數據
  let totalShares = 0;
  let totalCost = 0;
  let totalMarketVal = 0;
  let totalUnrealizedPnl = 0;
  let totalDayPnl = 0;
  let totalWeekPnl = 0;
  let totalEstDiv = 0;
  let totalHistDiv = 0;

  filtered.forEach(p => {
    totalShares += p.shares;
    totalCost += p.total_cost;
    totalMarketVal += p.market_val;
    totalUnrealizedPnl += p.unrealized_pnl;
    totalDayPnl += p.day_pnl;
    totalWeekPnl += p.week_pnl;
    totalEstDiv += p.total_dividend;
    totalHistDiv += p.hist_div_received;

    const mkt = p.market || "TW";
    const tagMktClass = mkt === "TW" ? "tag-tw" : (mkt === "TWO" ? "tag-two" : "tag-us");
    const etfTag = p.is_etf ? `<span class="badge-tag tag-etf">ETF</span>` : "";

    const tr = document.createElement("tr");
    tr.innerHTML = `
      <td class="td-sticky">
        <div class="sym-cell-box">
          <div class="sym-row-top">
            <span class="sym-code">${p.symbol}</span>
            <span class="badge-tag ${tagMktClass}">${mkt}</span>
            ${etfTag}
          </div>
          <span class="sym-name">${p.name || p.symbol}</span>
        </div>
      </td>
      <td>${isPrivate ? "***" : fmtNum(p.shares)}</td>
      <td>${isPrivate ? "***" : p.cost_price.toFixed(2)}</td>
      <td>
        <b>${p.current_price > 0 ? p.current_price.toFixed(2) : "--"}</b>
        <span class="cell-sub ${p.day_pct >= 0 ? "text-red" : "text-green"}">${fmtPctSign(p.day_pct)}</span>
      </td>
      <td>
        <b class="${p.unrealized_pnl >= 0 ? "text-red" : "text-green"}">${isPrivate ? "***" : fmtSign(p.unrealized_pnl)}</b>
        <span class="cell-sub ${p.roi_pct >= 0 ? "text-red" : "text-green"}">${fmtPctSign(p.roi_pct)}</span>
      </td>
      <td>
        <b class="${p.day_pnl >= 0 ? "text-red" : "text-green"}">${isPrivate ? "***" : fmtSign(p.day_pnl)}</b>
        <span class="cell-sub ${p.day_pct >= 0 ? "text-red" : "text-green"}">${fmtPctSign(p.day_pct)}</span>
      </td>
      <td>
        <b class="${p.week_pnl >= 0 ? "text-red" : "text-green"}">${isPrivate ? "***" : fmtSign(p.week_pnl)}</b>
        <span class="cell-sub ${p.week_pct >= 0 ? "text-red" : "text-green"}">${fmtPctSign(p.week_pct)}</span>
      </td>
      <td>
        <span>${p.cash_dividend > 0 ? `$ ${p.cash_dividend.toFixed(2)}` : "--"}</span>
        <span class="cell-sub text-dim">${p.frequency}</span>
      </td>
      <td>
        <span class="text-gold font-bold">${p.yield_on_cost > 0 ? `${p.yield_on_cost.toFixed(2)}%` : "--"}</span>
      </td>
      <td>
        <span class="text-gold font-bold">${isPrivate ? "***" : (p.total_dividend > 0 ? `$ ${fmtNum(Math.round(p.total_dividend))}` : "--")}</span>
      </td>
      <td>
        <span class="text-cyan font-bold">${isPrivate ? "***" : (p.hist_div_received > 0 ? `$ ${fmtNum(Math.round(p.hist_div_received))}` : "$ 0")}</span>
      </td>
    `;
    tbody.appendChild(tr);
  });

  // 渲染底部合計列 (Footer Summary Row)
  if (tfoot) {
    const totalRoiPct = totalCost > 0 ? (totalUnrealizedPnl / totalCost) * 100 : 0;
    const totalDayPct = (totalMarketVal - totalDayPnl > 0) ? (totalDayPnl / (totalMarketVal - totalDayPnl)) * 100 : 0;
    const totalWeekPct = (totalMarketVal - totalWeekPnl > 0) ? (totalWeekPnl / (totalMarketVal - totalWeekPnl)) * 100 : 0;
    const portfolioYield = totalCost > 0 ? (totalEstDiv / totalCost) * 100 : 0;

    const tfootRow = document.createElement("tr");
    tfootRow.innerHTML = `
      <td class="td-sticky">
        <div class="sym-cell-box">
          <span class="sym-code">總計</span>
          <span class="sym-name">${filtered.length} 檔標的</span>
        </div>
      </td>
      <td>${isPrivate ? "***" : fmtNum(totalShares)}</td>
      <td>-</td>
      <td>-</td>
      <td>
        <b class="${totalUnrealizedPnl >= 0 ? "text-red" : "text-green"}">${isPrivate ? "***" : fmtSign(totalUnrealizedPnl)}</b>
        <span class="cell-sub ${totalRoiPct >= 0 ? "text-red" : "text-green"}">${fmtPctSign(totalRoiPct)}</span>
      </td>
      <td>
        <b class="${totalDayPnl >= 0 ? "text-red" : "text-green"}">${isPrivate ? "***" : fmtSign(totalDayPnl)}</b>
        <span class="cell-sub ${totalDayPct >= 0 ? "text-red" : "text-green"}">${fmtPctSign(totalDayPct)}</span>
      </td>
      <td>
        <b class="${totalWeekPnl >= 0 ? "text-red" : "text-green"}">${isPrivate ? "***" : fmtSign(totalWeekPnl)}</b>
        <span class="cell-sub ${totalWeekPct >= 0 ? "text-red" : "text-green"}">${fmtPctSign(totalWeekPct)}</span>
      </td>
      <td>-</td>
      <td>
        <span class="text-gold font-bold">${portfolioYield > 0 ? `${portfolioYield.toFixed(2)}%` : "--"}</span>
      </td>
      <td>
        <span class="text-gold font-bold">${isPrivate ? "***" : `$ ${fmtNum(Math.round(totalEstDiv))}`}</span>
      </td>
      <td>
        <span class="text-cyan font-bold">${isPrivate ? "***" : `$ ${fmtNum(Math.round(totalHistDiv))}`}</span>
      </td>
    `;
    tfoot.appendChild(tfootRow);
  }
}

// --- 渲染：手機自適應卡片流 (Cards View) ---

function renderCardsView(keyword) {
  const container = document.getElementById("mobile-cards-view");
  container.innerHTML = "";

  const positions = state.normalizedPositions || [];
  const isPrivate = state.privacyMode;

  const filtered = positions.filter(pos => {
    if (!keyword) return true;
    const s = (pos.symbol || "").toLowerCase();
    const n = (pos.name || "").toLowerCase();
    return s.includes(keyword) || n.includes(keyword);
  });

  if (filtered.length === 0) {
    container.innerHTML = `<div style="grid-column: 1/-1; text-align: center; color: #8e95a5; padding: 30px;">無符合標的</div>`;
    return;
  }

  filtered.forEach(p => {
    const pnlSign = p.unrealized_pnl >= 0 ? "+" : "";
    const pillClass = p.unrealized_pnl >= 0 ? "pill-red" : "pill-green";
    const pnlColorClass = p.unrealized_pnl >= 0 ? "text-red" : "text-green";

    const mkt = p.market || "TW";
    const tagMktClass = mkt === "TW" ? "tag-tw" : (mkt === "TWO" ? "tag-two" : "tag-us");
    const etfTag = p.is_etf ? `<span class="badge-tag tag-etf">ETF</span>` : "";

    const card = document.createElement("div");
    card.className = "stock-card";
    card.innerHTML = `
      <div class="sc-header">
        <div class="sc-title-box">
          <span class="sc-symbol">${p.symbol}</span>
          <span class="sc-name">${p.name}</span>
        </div>
        <div class="sc-tags">
          <span class="badge-tag ${tagMktClass}">${mkt}</span>
          ${etfTag}
        </div>
      </div>

      <div class="sc-hero">
        <div class="sc-price-box">
          <span class="sc-price-label">參考現價 (今日)</span>
          <span class="sc-price-val">${p.current_price > 0 ? p.current_price.toFixed(2) : "--"}</span>
          <span class="cell-sub ${p.day_pct >= 0 ? "text-red" : "text-green"}">${fmtPctSign(p.day_pct)}</span>
        </div>
        <div class="sc-pnl-box">
          <span class="sc-pnl-pill ${pillClass}">${pnlSign}${p.roi_pct.toFixed(2)}%</span>
          <span class="sc-pnl-amt ${pnlColorClass}">${isPrivate ? "***" : fmtSign(p.unrealized_pnl)}</span>
        </div>
      </div>

      <div class="sc-details-grid">
        <div class="sc-detail-cell">
          <span class="sc-cell-lbl">持有股數</span>
          <span class="sc-cell-val">${isPrivate ? "***" : fmtNum(p.shares)}</span>
        </div>
        <div class="sc-detail-cell">
          <span class="sc-cell-lbl">成本均價</span>
          <span class="sc-cell-val">${isPrivate ? "***" : p.cost_price.toFixed(2)}</span>
        </div>
        <div class="sc-detail-cell">
          <span class="sc-cell-lbl">當日損益</span>
          <span class="sc-cell-val ${p.day_pnl >= 0 ? "text-red" : "text-green"}">${isPrivate ? "***" : fmtSign(p.day_pnl)}</span>
        </div>
        <div class="sc-detail-cell">
          <span class="sc-cell-lbl">當週損益</span>
          <span class="sc-cell-val ${p.week_pnl >= 0 ? "text-red" : "text-green"}">${isPrivate ? "***" : fmtSign(p.week_pnl)}</span>
        </div>
        <div class="sc-detail-cell">
          <span class="sc-cell-lbl">預估年股息 (殖利率)</span>
          <span class="sc-cell-val text-gold">${isPrivate ? "***" : (p.total_dividend > 0 ? `$ ${fmtNum(Math.round(p.total_dividend))}` : "--")} (${p.yield_on_cost.toFixed(1)}%)</span>
        </div>
        <div class="sc-detail-cell">
          <span class="sc-cell-lbl">歷年已領股息</span>
          <span class="sc-cell-val text-cyan">${isPrivate ? "***" : (p.hist_div_received > 0 ? `$ ${fmtNum(Math.round(p.hist_div_received))}` : "$ 0")}</span>
        </div>
      </div>
    `;
    container.appendChild(card);
  });
}
