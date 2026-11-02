/**
 * 零知識端到端加密投資庫存儀表板 (Web Crypto API 原生純前端)
 * 具備設備智慧自動分析、動態形態自我調適 (手機卡片流 / 桌面專業表格)
 */

const DEFAULT_FIREBASE_URL = "https://my-stock-tracker-2a94e-default-rtdb.asia-southeast1.firebasedatabase.app";
const WEB_VERSION = "V1.0";
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
  sortOrder: "asc",
  lastCloudUpdateTime: "",
  lastPortfolioSyncTime: "",
  lastQuotesTime: ""
};

let autoPollInterval = null;

function getNowFormatted() {
  const d = new Date();
  const pad = n => String(n).padStart(2, "0");
  return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())} ${pad(d.getHours())}:${pad(d.getMinutes())}:${pad(d.getSeconds())}`;
}


// --- 客戶端獨立即時報價引擎 (Client-Side Independent Live Quotes Engine) ---

async function fetchLiveQuotesAndUpdateUI() {
  if (!state.decryptedData || !state.rawPositions || state.rawPositions.length === 0) return;

  let quotesMap = {};
  let quotesTime = "";

  // 1. 優先嘗試從 Firebase /stats/quotes.json 讀取雲端報價快取 (最即時、零 CORS 問題)
  try {
    const qResp = await fetch(`${state.rtdbUrl}/stats/quotes.json?t=${Date.now()}`, { cache: "no-store" });
    if (qResp.ok) {
      const qData = await qResp.json();
      if (qData && typeof qData === "object") {
        quotesMap = qData;
        for (const k in qData) {
          if (qData[k] && qData[k].time && (!quotesTime || qData[k].time > quotesTime)) {
            quotesTime = qData[k].time;
          }
        }
      }
    }
  } catch (e) {
    // 靜默降級
  }

  // 2. 備援嘗試：若 Firebase 無報價，從同源 GitHub Pages 讀取 market_ranking_cache 與 etf_live_cache
  if (Object.keys(quotesMap).length === 0) {
    try {
      const [rEtf, rMkt] = await Promise.allSettled([
        fetch("../../WEB_ETF_Portfolio_Analyzer/etf_live_cache.json?t=" + Date.now()).then(r => r.ok ? r.json() : {}),
        fetch("../market_ranking_cache.json?t=" + Date.now()).then(r => r.ok ? r.json() : {})
      ]);
      if (rEtf.status === "fulfilled" && rEtf.value) {
        for (const code in rEtf.value) {
          const it = rEtf.value[code];
          if (it && it.price) quotesMap[code] = { price: it.price, name: it.name, time: "盤後快取" };
        }
      }
      if (rMkt.status === "fulfilled" && rMkt.value && rMkt.value.stocks) {
        for (const s of rMkt.value.stocks) {
          if (s && s.symbol && s.price) quotesMap[s.symbol] = { price: s.price, name: s.name, time: "盤後快取" };
        }
      }
    } catch (e) {}
  }

  // 3. 將最新報價套用至當前解密的部位
  let anyPriceUpdated = false;
  state.rawPositions.forEach(p => {
    const sym = (p.symbol || "").trim().toUpperCase();
    const q = quotesMap[sym];
    if (q && q.price && q.price > 0) {
      if (Math.abs(p.current_price - q.price) > 0.001) {
        p.current_price = q.price;
        anyPriceUpdated = true;
      }
      if (q.yesterday_close && q.yesterday_close > 0) {
        p.yesterday_close = q.yesterday_close;
      }
      if (q.time && (!quotesTime || q.time > quotesTime)) {
        quotesTime = q.time;
      }
    }
  });

  // 最新數據計算時間戳：若抓到了市場報價時間，顯示市場時間；否則以當前即時重新計算之時間為準
  if (!quotesTime || quotesTime === "盤後快取") {
    quotesTime = getNowFormatted();
  }

  // 4. 即時重算全庫存損益與畫面渲染
  state.lastQuotesTime = quotesTime;
  renderDashboard(state.decryptedData, state.lastCloudUpdateTime, quotesTime, state.lastPortfolioSyncTime);

  // 5. 綠色小圓點與邊框閃爍提示 (視覺化反饋)
  const quoteEl = document.getElementById("quote-time-lbl");
  if (quoteEl) {
    quoteEl.style.transition = "all 0.3s ease";
    quoteEl.style.color = "#00f090";
    quoteEl.style.borderColor = "rgba(0, 240, 144, 0.8)";
    quoteEl.style.boxShadow = "0 0 10px rgba(0, 240, 144, 0.3)";
    setTimeout(() => {
      quoteEl.style.color = "";
      quoteEl.style.borderColor = "";
      quoteEl.style.boxShadow = "";
    }, 1500);
  }
}

async function reloadCloudDataSilently() {
  if (!state.user || !state.password) return;
  try {
    const endpoint = `${state.rtdbUrl}/portfolios/${state.user}.json?t=${Date.now()}`;
    const resp = await fetch(endpoint, { cache: "no-store" });
    if (!resp.ok) return;
    const encPayload = await resp.json();
    if (!encPayload || !encPayload.ciphertext_b64) return;
    const decrypted = await decryptAesGcm(encPayload, state.password, state.secretKey);
    state.decryptedData = decrypted;
    state.rawPositions = decrypted.positions || [];
    state.lastPortfolioSyncTime = encPayload.updated_at || decrypted.exported_at || "";
    state.lastCloudUpdateTime = encPayload.updated_at || decrypted.exported_at || "";
    await fetchLiveQuotesAndUpdateUI();
  } catch (err) {
    console.warn("[Cloud] 自動更新失敗:", err);
  }
}

function startCloudAutoPolling() {
  if (autoPollInterval) clearInterval(autoPollInterval);
  autoPollInterval = setInterval(async () => {
    if (!state.user || !state.password || !state.decryptedData) return;
    
    // A. 探測持股部位是否在電腦端有異動
    try {
      const checkUrl = `${state.rtdbUrl}/portfolios/${state.user}/updated_at.json?t=${Date.now()}`;
      const r = await fetch(checkUrl, { cache: "no-store" });
      if (r.ok) {
        const remoteTime = await r.json();
        if (remoteTime && remoteTime !== state.lastCloudUpdateTime) {
          console.log(`[Cloud] 偵測到雲端資料已更新 (${remoteTime})，正在自動重新載入...`);
          await reloadCloudDataSilently();
          return;
        }
      }
    } catch (e) {}

    // B. 電腦即使關機，網頁每 15 秒也自動探測雲端即時行情並重算損益！
    try {
      await fetchLiveQuotesAndUpdateUI();
    } catch (e) {}
  }, 15000); // 每 15 秒自動探測一次
}

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
      const endpoint = `${state.rtdbUrl}/portfolios/${user}.json?t=${Date.now()}`;
      const resp = await fetch(endpoint, { cache: "no-store" });
      if (!resp.ok) throw new Error(`HTTP ${resp.status} - 無法存取雲端資料`);

      const encPayload = await resp.json();
      if (!encPayload || !encPayload.ciphertext_b64) {
        throw new Error(`找不到使用者 [${user}] 的資料！請先於電腦本機點擊「一鍵同步」。`);
      }

      showStatus("已取得資料，正在本機解密中...", "ok");

      const decrypted = await decryptAesGcm(encPayload, pass, secretKey);
      state.decryptedData = decrypted;
      state.rawPositions = decrypted.positions || [];
      state.lastPortfolioSyncTime = encPayload.updated_at || decrypted.exported_at || "";
      state.lastCloudUpdateTime = encPayload.updated_at || decrypted.exported_at || "";
      state.lastQuotesTime = getNowFormatted();

      loginModal.classList.add("hidden");
      appContainer.classList.remove("hidden");

      applyViewModeUI();
      renderDashboard(decrypted, encPayload.updated_at, state.lastQuotesTime, state.lastPortfolioSyncTime);
      showStatus("", "");
      await fetchLiveQuotesAndUpdateUI();
      startCloudAutoPolling();
    } catch (err) {
      console.error(err);
      showStatus(`解密失敗: ${err.message || "密碼錯誤或金鑰不符"}`, "err");
    }
  });

  // 安全登出銷毀記憶體
  const performLogout = () => {
    if (autoPollInterval) {
      clearInterval(autoPollInterval);
      autoPollInterval = null;
    }
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
  const performRefresh = async () => {
    if (state.user && state.password) {
      const btn = document.getElementById("btn-refresh");
      if (btn) btn.style.opacity = "0.5";
      await reloadCloudDataSilently();
      await fetchLiveQuotesAndUpdateUI();
      if (btn) setTimeout(() => { btn.style.opacity = "1"; }, 500);
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

  // 主分頁切換監聽 (存股部位 vs 自組 ETF 績效)
  const tabBtnPos = document.getElementById("tab-btn-positions");
  const tabBtnEtf = document.getElementById("tab-btn-etf");
  const posContent = document.getElementById("positions-tab-content");
  const etfContent = document.getElementById("etf-analytics-tab-content");

  if (tabBtnPos && tabBtnEtf) {
    tabBtnPos.addEventListener("click", () => {
      tabBtnPos.classList.add("active");
      tabBtnEtf.classList.remove("active");
      if (posContent) posContent.classList.remove("hidden");
      if (etfContent) etfContent.classList.add("hidden");
    });

    tabBtnEtf.addEventListener("click", () => {
      tabBtnEtf.classList.add("active");
      tabBtnPos.classList.remove("active");
      if (posContent) posContent.classList.add("hidden");
      if (etfContent) etfContent.classList.remove("hidden");
      renderEtfAnalytics();
    });
  }

  // 自組 ETF K 線範圍按鈕切換
  document.querySelectorAll(".btn-etf-range").forEach(btn => {
    btn.addEventListener("click", (e) => {
      document.querySelectorAll(".btn-etf-range").forEach(b => b.classList.remove("active"));
      btn.classList.add("active");
      etfChartState.displayDays = parseInt(btn.getAttribute("data-days") || "0", 10);
      drawEtfKlineChart();
    });
  });

  // 各股績效多週期切換
  document.querySelectorAll(".btn-period").forEach(btn => {
    btn.addEventListener("click", (e) => {
      document.querySelectorAll(".btn-period").forEach(b => b.classList.remove("active"));
      btn.classList.add("active");
      etfChartState.perfPeriod = btn.getAttribute("data-period") || "day";
      drawPerfBarChart();
    });
  });

  // 各股績效表格表頭排序
  document.querySelectorAll("[data-psort]").forEach(th => {
    th.addEventListener("click", () => {
      const field = th.getAttribute("data-psort");
      if (etfChartState.perfSortField === field) {
        etfChartState.perfSortOrder = etfChartState.perfSortOrder === "asc" ? "desc" : "asc";
      } else {
        etfChartState.perfSortField = field;
        etfChartState.perfSortOrder = "desc";
      }
      renderPerfTable();
    });
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

function renderDashboard(data, cloudUpdateTime, quotesUpdateTime, portfolioSyncTime) {
  document.getElementById("user-badge").textContent = `👤 使用者: ${state.user}`;

  // 1. 存股部位與批次同步時間
  if (portfolioSyncTime) {
    state.lastPortfolioSyncTime = portfolioSyncTime;
  } else if (cloudUpdateTime) {
    state.lastPortfolioSyncTime = cloudUpdateTime;
  } else if (!state.lastPortfolioSyncTime) {
    state.lastPortfolioSyncTime = data.portfolio_sync_time || data.exported_at || "剛剛";
  }
  const portSyncTime = state.lastPortfolioSyncTime;
  const syncEl = document.getElementById("sync-time-lbl");
  if (syncEl) {
    syncEl.innerHTML = `<span class="badge-icon">📦</span> 存股: ${portSyncTime}`;
    syncEl.title = `存股部位與買進批次同步至雲端的時間: ${portSyncTime}`;
  }

  // 2. 目前盤面行情與損益數據更新時間
  if (quotesUpdateTime) {
    state.lastQuotesTime = quotesUpdateTime;
  } else if (!state.lastQuotesTime) {
    state.lastQuotesTime = data.quotes_update_time || state.lastPortfolioSyncTime || getNowFormatted();
  }
  const quotesTime = state.lastQuotesTime;
  const quoteEl = document.getElementById("quote-time-lbl");
  if (quoteEl) {
    quoteEl.innerHTML = `<span class="live-dot"></span> 數據: ${quotesTime}`;
    quoteEl.title = `即時行情報價與損益最新計算時間: ${quotesTime}`;
  }

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

  // 渲染自組 ETF 淨值走勢與各股多週期績效
  renderEtfAnalytics();
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


// ============================================================================
// 自組 ETF (Portfolio Synthetic ETF) 淨值走勢與各股多週期績效分析前端引擎
// ============================================================================

let etfChartState = {
  displayDays: 0, // 0 代表全部
  perfPeriod: "day", // "day", "week", "month", "year"
  perfSortField: "weight_pct",
  perfSortOrder: "desc",
  hoverRecord: null
};

function renderEtfAnalytics() {
  const data = state.decryptedData;
  if (!data) return;

  const etfData = data.synthetic_etf || {};
  const records = etfData.records || [];
  const perfData = etfData.performance || {};
  const items = perfData.items || [];

  const isPrivate = state.privacyMode;

  // 1. 更新頂部指標卡
  const navValEl = document.getElementById("etf-nav-val");
  const returnValEl = document.getElementById("etf-return-val");
  const dayValEl = document.getElementById("etf-day-val");
  const mktValEl = document.getElementById("etf-market-val");
  const baseInfoEl = document.getElementById("etf-base-info");

  const currentNav = etfData.current_nav || 100.0;
  const totalReturn = etfData.total_return_pct || 0.0;
  const dayChange = etfData.nav_change_pct || 0.0;
  const totalMkt = perfData.total_market_value || etfData.total_market_value || 0;
  const baseDate = etfData.base_date || "";

  if (navValEl) navValEl.textContent = isPrivate ? "***.**" : currentNav.toFixed(2);
  if (returnValEl) {
    const s = totalReturn > 0 ? "+" : "";
    returnValEl.textContent = `${s}${totalReturn.toFixed(2)}%`;
    returnValEl.className = `etf-return-val ${totalReturn >= 0 ? "text-red" : "text-green"}`;
  }
  if (dayValEl) {
    const s = dayChange > 0 ? "+" : "";
    dayValEl.textContent = `${s}${dayChange.toFixed(2)}%`;
    dayValEl.className = `etf-day-val ${dayChange >= 0 ? "text-red" : "text-green"}`;
  }
  if (mktValEl) mktValEl.textContent = isPrivate ? "$ ********" : `$ ${fmtNum(Math.round(totalMkt))}`;
  if (baseInfoEl) baseInfoEl.textContent = `基準淨值: 100.00 (${baseDate})`;

  // 2. 繪製自組 ETF K 線圖
  drawEtfKlineChart();

  // 3. 繪製各股多週期長條圖
  drawPerfBarChart();

  // 4. 渲染各股績效詳細數據表格
  renderPerfTable();
}

function drawEtfKlineChart() {
  const canvas = document.getElementById("etf-kline-canvas");
  if (!canvas) return;
  const ctx = canvas.getContext("2d");

  const data = state.decryptedData;
  const etfData = (data && data.synthetic_etf) ? data.synthetic_etf : {};
  let records = etfData.records || [];

  if (records.length === 0) {
    ctx.clearRect(0, 0, canvas.width, canvas.height);
    ctx.fillStyle = "#94a3b8";
    ctx.font = "14px 'Noto Sans TC', sans-serif";
    ctx.textAlign = "center";
    ctx.fillText("暫無歷史 K 線資料，請於電腦端執行【盤後更新】並重新同步", canvas.width / 2, canvas.height / 2);
    return;
  }

  // 天數過濾
  const days = etfChartState.displayDays;
  if (days > 0 && records.length > days) {
    records = records.slice(records.length - days);
  }

  // 依畫布大小縮放
  const w = canvas.width;
  const h = canvas.height;
  ctx.clearRect(0, 0, w, h);

  const paddingLeft = 15;
  const paddingRight = 65;
  const paddingTop = 25;
  const paddingBottom = 25;
  const volHeightRatio = 0.25;

  const chartW = w - paddingLeft - paddingRight;
  const totalChartH = h - paddingTop - paddingBottom;
  const volH = totalChartH * volHeightRatio;
  const kH = totalChartH - volH - 15;

  const kTop = paddingTop;
  const kBottom = kTop + kH;
  const volTop = kBottom + 15;
  const volBottom = volTop + volH;

  // 計算價格最高、最低與成交量最高
  let minP = Infinity;
  let maxP = -Infinity;
  let maxV = 0;

  records.forEach(r => {
    if (r.low < minP) minP = r.low;
    if (r.high > maxP) maxP = r.high;
    if (r.ma5 && r.ma5 < minP) minP = r.ma5;
    if (r.ma5 && r.ma5 > maxP) maxP = r.ma5;
    if (r.ma20 && r.ma20 < minP) minP = r.ma20;
    if (r.ma20 && r.ma20 > maxP) maxP = r.ma20;
    if (r.volume > maxV) maxV = r.volume;
  });

  const pMargin = (maxP - minP) * 0.05 || 1.0;
  minP -= pMargin;
  maxP += pMargin;
  if (maxV <= 0) maxV = 1;

  const pRange = maxP - minP;
  const yFromPrice = p => kBottom - ((p - minP) / pRange) * kH;
  const yFromVol = v => volBottom - (v / maxV) * volH;

  // 繪製格線與右側標尺
  ctx.strokeStyle = "#232332";
  ctx.lineWidth = 1;
  ctx.fillStyle = "#828299";
  ctx.font = "11px Inter, sans-serif";
  ctx.textAlign = "left";

  const gridSteps = 4;
  for (let i = 0; i <= gridSteps; i++) {
    const p = minP + (pRange / gridSteps) * i;
    const y = yFromPrice(p);
    ctx.beginPath();
    ctx.moveTo(paddingLeft, y);
    ctx.lineTo(paddingLeft + chartW, y);
    ctx.stroke();
    ctx.fillText(p.toFixed(2), paddingLeft + chartW + 6, y + 4);
  }

  // 繪製蠟燭線與成交量
  const n = records.length;
  const candleW = Math.max(Math.min((chartW / n) * 0.7, 14), 2);
  const stepX = chartW / n;

  const cUp = "#ff4d4f";
  const cDown = "#52c41a";
  const cFlat = "#cccccc";

  const ma5Points = [];
  const ma20Points = [];

  records.forEach((r, i) => {
    const xCenter = paddingLeft + (i + 0.5) * stepX;
    const yOpen = yFromPrice(r.open);
    const yClose = yFromPrice(r.close);
    const yHigh = yFromPrice(r.high);
    const yLow = yFromPrice(r.low);

    const isUp = r.close > r.open;
    const isDown = r.close < r.open;
    const col = isUp ? cUp : (isDown ? cDown : cFlat);

    // 上下影線
    ctx.strokeStyle = col;
    ctx.lineWidth = 1.2;
    ctx.beginPath();
    ctx.moveTo(xCenter, yHigh);
    ctx.lineTo(xCenter, yLow);
    ctx.stroke();

    // 實體蠟燭
    ctx.fillStyle = col;
    const rectTop = Math.min(yOpen, yClose);
    const rectH = Math.max(Math.abs(yClose - yOpen), 1.5);
    ctx.fillRect(xCenter - candleW / 2, rectTop, candleW, rectH);

    // 成交量柱
    const yV = yFromVol(r.volume);
    ctx.fillStyle = col;
    ctx.fillRect(xCenter - candleW / 2, yV, candleW, volBottom - yV);

    // 紀錄 MA 點
    if (r.ma5) ma5Points.push({ x: xCenter, y: yFromPrice(r.ma5) });
    if (r.ma20) ma20Points.push({ x: xCenter, y: yFromPrice(r.ma20) });
  });

  // 繪製 MA5 (黃)
  if (ma5Points.length > 1) {
    ctx.strokeStyle = "#fadb14";
    ctx.lineWidth = 1.6;
    ctx.beginPath();
    ma5Points.forEach((pt, i) => {
      if (i === 0) ctx.moveTo(pt.x, pt.y);
      else ctx.lineTo(pt.x, pt.y);
    });
    ctx.stroke();
  }

  // 繪製 MA20 (紫)
  if (ma20Points.length > 1) {
    ctx.strokeStyle = "#b37feb";
    ctx.lineWidth = 1.6;
    ctx.beginPath();
    ma20Points.forEach((pt, i) => {
      if (i === 0) ctx.moveTo(pt.x, pt.y);
      else ctx.lineTo(pt.x, pt.y);
    });
    ctx.stroke();
  }

  // 更新預設頂部 MA 數值
  const latestR = records[records.length - 1];
  if (latestR) {
    document.getElementById("etf-kline-ma5").textContent = latestR.ma5 ? latestR.ma5.toFixed(2) : "--";
    document.getElementById("etf-kline-ma20").textContent = latestR.ma20 ? latestR.ma20.toFixed(2) : "--";
    document.getElementById("etf-kline-vol").textContent = fmtNum(Math.round(latestR.volume / 1000)) + " 張";
  }

  // 綁定懸停游標事件
  if (!canvas.getAttribute("data-bound")) {
    canvas.setAttribute("data-bound", "true");
    canvas.addEventListener("mousemove", (e) => {
      const rect = canvas.getBoundingClientRect();
      const scaleX = canvas.width / rect.width;
      const mouseX = (e.clientX - rect.left) * scaleX;

      const idx = Math.floor((mouseX - paddingLeft) / stepX);
      if (idx >= 0 && idx < records.length) {
        const r = records[idx];
        const infoEl = document.getElementById("etf-crosshair-info");
        if (infoEl) {
          const s = r.change_pct > 0 ? "+" : "";
          infoEl.innerHTML = `<b>${r.date}</b> | 開: <b>${r.open.toFixed(2)}</b> 高: <b>${r.high.toFixed(2)}</b> 低: <b>${r.low.toFixed(2)}</b> 收: <b>${r.close.toFixed(2)}</b> (${s}${r.change_pct.toFixed(2)}%)`;
        }
        if (r.ma5) document.getElementById("etf-kline-ma5").textContent = r.ma5.toFixed(2);
        if (r.ma20) document.getElementById("etf-kline-ma20").textContent = r.ma20.toFixed(2);
        if (r.volume) document.getElementById("etf-kline-vol").textContent = fmtNum(Math.round(r.volume / 1000)) + " 張";
      }
    });

    canvas.addEventListener("mouseleave", () => {
      const infoEl = document.getElementById("etf-crosshair-info");
      if (infoEl) infoEl.textContent = "懸停於圖表上查看日期與詳細開高低收";
    });
  }
}

function drawPerfBarChart() {
  const canvas = document.getElementById("perf-bar-canvas");
  if (!canvas) return;
  const ctx = canvas.getContext("2d");

  const data = state.decryptedData;
  const etfData = (data && data.synthetic_etf) ? data.synthetic_etf : {};
  const perfData = etfData.performance || {};
  let items = [...(perfData.items || [])];

  const w = canvas.width;
  const h = canvas.height;
  ctx.clearRect(0, 0, w, h);

  if (items.length === 0) {
    ctx.fillStyle = "#94a3b8";
    ctx.font = "14px 'Noto Sans TC', sans-serif";
    ctx.textAlign = "center";
    ctx.fillText("暫無個股多週期績效數據", w / 2, h / 2);
    return;
  }

  const period = etfChartState.perfPeriod;
  const keyMap = {
    "day": { key: "day_pct", label: "當日績效" },
    "week": { key: "week_pct", label: "當週 (5日)" },
    "month": { key: "month_pct", label: "當月 (20日)" },
    "year": { key: "year_pct", label: "全期 / 年化" }
  };
  const cfg = keyMap[period] || keyMap["day"];

  // 依照該週期數值排序
  items.sort((a, b) => b[cfg.key] - a[cfg.key]);

  // 找出最大絕對值
  const maxAbs = Math.max(...items.map(x => Math.abs(x[cfg.key])), 5.0);

  const paddingTop = 28;
  const paddingBottom = 15;
  const labelW = 110;
  const valW = 60;
  const chartW = w - labelW - valW - 20;
  const zeroX = labelW + chartW / 2;

  // 繪製標題與中心軸
  ctx.fillStyle = "#94a3b8";
  ctx.font = "12px 'Noto Sans TC', sans-serif";
  ctx.textAlign = "center";
  ctx.fillText(`-- ${cfg.label} 排行榜 (紅漲 / 綠跌) --`, w / 2, 16);

  ctx.strokeStyle = "#2b2b3d";
  ctx.lineWidth = 1;
  ctx.setLineDash([3, 3]);
  ctx.beginPath();
  ctx.moveTo(zeroX, paddingTop);
  ctx.lineTo(zeroX, h - paddingBottom);
  ctx.stroke();
  ctx.setLineDash([]);

  const n = items.length;
  const rowH = (h - paddingTop - paddingBottom) / n;
  const barH = Math.max(Math.min(rowH * 0.65, 18), 8);

  const cUp = "#ff4d4f";
  const cDown = "#52c41a";
  const cFlat = "#999999";

  items.forEach((it, i) => {
    const yCenter = paddingTop + (i + 0.5) * rowH;
    const val = it[cfg.key] || 0;
    const sym = it.symbol;
    const name = it.name || sym;

    // 左側股票標籤
    ctx.fillStyle = "#cbd5e1";
    ctx.font = "11px Inter, sans-serif";
    ctx.textAlign = "right";
    ctx.fillText(`${sym} ${name.slice(0, 5)}`, labelW - 6, yCenter + 4);

    // 柱體寬度
    const barLen = (Math.abs(val) / maxAbs) * (chartW / 2);
    let x1, x2, col, valX, valAlign, valText;

    if (val > 0) {
      x1 = zeroX;
      x2 = zeroX + barLen;
      col = cUp;
      valX = x2 + 5;
      valAlign = "left";
      valText = `+${val.toFixed(2)}%`;
    } else if (val < 0) {
      x1 = zeroX - barLen;
      x2 = zeroX;
      col = cDown;
      valX = x1 - 5;
      valAlign = "right";
      valText = `${val.toFixed(2)}%`;
    } else {
      x1 = zeroX - 1;
      x2 = zeroX + 1;
      col = cFlat;
      valX = x2 + 5;
      valAlign = "left";
      valText = `0.00%`;
    }

    // 繪製柱體
    ctx.fillStyle = col;
    ctx.fillRect(x1, yCenter - barH / 2, Math.max(x2 - x1, 1), barH);

    // 繪製數值
    ctx.font = "bold 10px Inter, monospace";
    ctx.textAlign = valAlign;
    ctx.fillText(valText, valX, yCenter + 3.5);
  });
}

function renderPerfTable() {
  const tbody = document.getElementById("perf-tbody");
  if (!tbody) return;
  tbody.innerHTML = "";

  const data = state.decryptedData;
  const etfData = (data && data.synthetic_etf) ? data.synthetic_etf : {};
  const perfData = etfData.performance || {};
  let items = [...(perfData.items || [])];

  if (items.length === 0) {
    tbody.innerHTML = `<tr><td colspan="8" style="text-align:center; color:#94a3b8; padding:20px;">尚無個股多週期數據</td></tr>`;
    return;
  }

  // 排序
  const field = etfChartState.perfSortField;
  const order = etfChartState.perfSortOrder;
  items.sort((a, b) => {
    let vA = a[field];
    let vB = b[field];
    if (typeof vA === "string") return order === "asc" ? vA.localeCompare(vB) : vB.localeCompare(a);
    return order === "asc" ? vA - vB : vB - vA;
  });

  const isPrivate = state.privacyMode;

  items.forEach(it => {
    const tr = document.createElement("tr");

    const signD = it.day_pct > 0 ? "+" : "";
    const signW = it.week_pct > 0 ? "+" : "";
    const signM = it.month_pct > 0 ? "+" : "";
    const signY = it.year_pct > 0 ? "+" : "";
    const signU = it.unrealized_pnl > 0 ? "+" : "";

    const colD = it.day_pct >= 0 ? "text-red" : "text-green";
    const colW = it.week_pct >= 0 ? "text-red" : "text-green";
    const colM = it.month_pct >= 0 ? "text-red" : "text-green";
    const colY = it.year_pct >= 0 ? "text-red" : "text-green";
    const colU = it.unrealized_pnl >= 0 ? "text-red" : "text-green";

    tr.innerHTML = `
      <td><b>${it.symbol}</b></td>
      <td>${it.name}</td>
      <td>${it.weight_pct.toFixed(2)}%</td>
      <td class="${colD}">${signD}${it.day_pct.toFixed(2)}%</td>
      <td class="${colW}">${signW}${it.week_pct.toFixed(2)}%</td>
      <td class="${colM}">${signM}${it.month_pct.toFixed(2)}%</td>
      <td class="${colY}">${signY}${it.year_pct.toFixed(2)}%</td>
      <td class="${colU}">${isPrivate ? "***" : `${signU}${fmtNum(Math.round(it.unrealized_pnl))}`}</td>
    `;
    tbody.appendChild(tr);
  });
}
