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
  currentViewMode: "cards" // "cards" | "table"
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
  state.currentViewMode = savedPrefView || (state.deviceType === "desktop" ? "table" : "cards");

  document.getElementById("device-badge").textContent = profile.label;

  // 載入歷史帳號與設備 Secret Key
  const cachedUser = localStorage.getItem("last_portfolio_user");
  if (cachedUser) inputUser.value = cachedUser;

  const cachedSecretKey = localStorage.getItem("last_portfolio_secret_key");
  if (cachedSecretKey && inputSecretKey) inputSecretKey.value = cachedSecretKey;

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
    if (secretKey) localStorage.setItem("last_portfolio_secret_key", secretKey);
    if (customUrl) localStorage.setItem("last_rtdb_url", customUrl);

    showStatus("正在連線雲端並拉取加密封包...", "ok");

    try {
      const endpoint = `${state.rtdbUrl}/portfolios/${user}.json`;
      const resp = await fetch(endpoint);
      if (!resp.ok) throw new Error(`HTTP ${resp.status} - 無法存取雲端資料`);

      const encPayload = await resp.json();
      if (!encPayload || !encPayload.ciphertext_b64) {
        throw new Error(`找不到使用者 [${user}] 的密文！請先於電腦本機軟體執行同步。`);
      }

      showStatus("已取得密文，正在進行本地 PBKDF2 (600,000次) + AES-GCM 原生解密...", "ok");

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
      indicator.textContent = isCards ? "模式：直覺式卡片流 (推薦手機/觸控)" : "模式：專業完整數據表格 (推薦桌機/寬螢幕)";
    }
  }
});

// --- 渲染畫面 ---

function renderDashboard(data, cloudUpdateTime) {
  document.getElementById("user-badge").textContent = `👤 使用者: ${state.user}`;
  const syncTime = cloudUpdateTime || data.exported_at || "剛剛";
  document.getElementById("sync-time-lbl").textContent = `同步時間: ${syncTime}`;

  const positions = data.positions || [];
  const divs = data.dividends || {};

  let totalCost = 0;
  let totalMarketVal = 0;
  let totalEstDiv = 0;

  positions.forEach(pos => {
    const shares = parseFloat(pos.shares) || 0;
    const costPrice = parseFloat(pos.cost_price) || 0;
    const curPrice = parseFloat(pos.current_price || pos.cost_price) || 0;

    const cost = shares * costPrice;
    const mVal = shares * curPrice;
    totalCost += cost;
    totalMarketVal += mVal;

    const divInfo = divs[pos.symbol];
    if (divInfo && divInfo.cash_dividend) {
      totalEstDiv += (parseFloat(divInfo.cash_dividend) || 0) * shares;
    }
  });

  const totalPnl = totalMarketVal - totalCost;
  const totalRoi = totalCost > 0 ? (totalPnl / totalCost) * 100 : 0;
  const totalYield = totalMarketVal > 0 ? (totalEstDiv / totalMarketVal) * 100 : 0;

  // 頂部指標卡片渲染 (支援脫敏模式)
  const isPrivate = state.privacyMode;
  document.getElementById("val-total-cost").textContent = isPrivate ? "$ ********" : `$ ${fmtNum(Math.round(totalCost))}`;
  document.getElementById("val-total-market").textContent = isPrivate ? "$ ********" : `$ ${fmtNum(Math.round(totalMarketVal))}`;

  const pnlEl = document.getElementById("val-total-pnl");
  const pnlSign = totalPnl >= 0 ? "+" : "";
  const pnlColorClass = totalPnl >= 0 ? "text-red" : "text-green"; // 台灣紅漲綠跌
  pnlEl.className = `metric-val ${pnlColorClass}`;
  pnlEl.textContent = isPrivate ? `${pnlSign}${totalRoi.toFixed(2)}%` : `$ ${pnlSign}${fmtNum(Math.round(totalPnl))} (${pnlSign}${totalRoi.toFixed(2)}%)`;

  document.getElementById("val-total-div").textContent = isPrivate ? `${totalYield.toFixed(2)}%` : `$ ${fmtNum(Math.round(totalEstDiv))} (${totalYield.toFixed(2)}%)`;
  document.getElementById("stock-count-badge").textContent = `在庫標的: ${positions.length} 檔`;

  // 渲染資產配置條
  renderAllocation(positions, totalMarketVal);

  // 渲染當前視圖模式
  renderContentByMode("");
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

  const sorted = [...positions].sort((a, b) => {
    const ma = (a.shares || 0) * (a.current_price || a.cost_price || 0);
    const mb = (b.shares || 0) * (b.current_price || b.cost_price || 0);
    return mb - ma;
  });

  sorted.forEach((pos, idx) => {
    const mVal = (pos.shares || 0) * (pos.current_price || pos.cost_price || 0);
    const pct = ((mVal / totalMarketVal) * 100).toFixed(1);
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

// --- 渲染：手機專屬卡片流 (Cards View) ---

function renderCardsView(keyword) {
  const container = document.getElementById("mobile-cards-view");
  container.innerHTML = "";

  const positions = state.rawPositions || [];
  const divs = (state.decryptedData && state.decryptedData.dividends) || {};
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

  filtered.forEach(pos => {
    const shares = parseFloat(pos.shares) || 0;
    const cost = parseFloat(pos.cost_price) || 0;
    const price = parseFloat(pos.current_price || pos.cost_price) || 0;

    const pnl = (price - cost) * shares;
    const roi = cost > 0 ? ((price - cost) / cost) * 100 : 0;
    const pnlSign = pnl >= 0 ? "+" : "";
    const pillClass = pnl >= 0 ? "pill-red" : "pill-green";
    const pnlColorClass = pnl >= 0 ? "text-red" : "text-green";

    const divInfo = divs[pos.symbol] || {};
    const singleDiv = divInfo.single_dividend || divInfo.cash_dividend || 0;
    const estAnnualDiv = (divInfo.cash_dividend || 0) * shares;

    const mkt = pos.market || "TW";
    const tagMktClass = mkt === "TW" ? "tag-tw" : (mkt === "TWO" ? "tag-two" : "tag-us");
    const etfTag = pos.is_etf ? `<span class="badge-tag tag-etf">ETF 0.1%</span>` : "";

    const card = document.createElement("div");
    card.className = "stock-card";
    card.innerHTML = `
      <div class="sc-header">
        <div class="sc-title-box">
          <span class="sc-symbol">${pos.symbol}</span>
          <span class="sc-name">${pos.name}</span>
        </div>
        <div class="sc-tags">
          <span class="badge-tag ${tagMktClass}">${mkt}</span>
          ${etfTag}
        </div>
      </div>

      <div class="sc-hero">
        <div class="sc-price-box">
          <span class="sc-price-label">參考現價</span>
          <span class="sc-price-val">${price > 0 ? price.toFixed(2) : "--"}</span>
        </div>
        <div class="sc-pnl-box">
          <span class="sc-pnl-pill ${pillClass}">${pnlSign}${roi.toFixed(2)}%</span>
          <span class="sc-pnl-amt ${pnlColorClass}">${isPrivate ? "***" : `${pnlSign}${fmtNum(Math.round(pnl))}`}</span>
        </div>
      </div>

      <div class="sc-details-grid">
        <div class="sc-detail-cell">
          <span class="sc-cell-lbl">持有股數</span>
          <span class="sc-cell-val">${isPrivate ? "***" : fmtNum(shares)}</span>
        </div>
        <div class="sc-detail-cell">
          <span class="sc-cell-lbl">成本均價</span>
          <span class="sc-cell-val">${isPrivate ? "***" : cost.toFixed(2)}</span>
        </div>
        <div class="sc-detail-cell">
          <span class="sc-cell-lbl">預估年股息</span>
          <span class="sc-cell-val text-gold">${isPrivate ? "***" : (estAnnualDiv > 0 ? `$ ${fmtNum(Math.round(estAnnualDiv))}` : "--")}</span>
        </div>
      </div>
    `;
    container.appendChild(card);
  });
}

// --- 渲染：桌面專業表格 (Table View) ---

function renderTableView(keyword) {
  const tbody = document.getElementById("portfolio-tbody");
  tbody.innerHTML = "";

  const positions = state.rawPositions || [];
  const divs = (state.decryptedData && state.decryptedData.dividends) || {};
  const lots = (state.decryptedData && state.decryptedData.trade_lots) || {};
  const isPrivate = state.privacyMode;

  const filtered = positions.filter(pos => {
    if (!keyword) return true;
    const s = (pos.symbol || "").toLowerCase();
    const n = (pos.name || "").toLowerCase();
    return s.includes(keyword) || n.includes(keyword);
  });

  filtered.forEach(pos => {
    const shares = parseFloat(pos.shares) || 0;
    const cost = parseFloat(pos.cost_price) || 0;
    const price = parseFloat(pos.current_price || pos.cost_price) || 0;

    const pnl = (price - cost) * shares;
    const roi = cost > 0 ? ((price - cost) / cost) * 100 : 0;
    const pnlSign = pnl >= 0 ? "+" : "";
    const pnlClass = pnl >= 0 ? "text-red" : "text-green";

    const divInfo = divs[pos.symbol] || {};
    const singleDiv = divInfo.single_dividend || divInfo.cash_dividend || 0;
    const estAnnualDiv = (divInfo.cash_dividend || 0) * shares;

    const mkt = pos.market || "TW";
    const tagMktClass = mkt === "TW" ? "tag-tw" : (mkt === "TWO" ? "tag-two" : "tag-us");
    const etfTag = pos.is_etf ? `<span class="badge-tag tag-etf">ETF</span>` : "";

    const lotCount = (lots[pos.symbol] || []).length;
    const lotText = lotCount > 0 ? `${lotCount} 筆取得明細` : "單一成本";

    const tr = document.createElement("tr");
    tr.innerHTML = `
      <td><b>${pos.symbol}</b></td>
      <td>${pos.name}</td>
      <td><span class="badge-tag ${tagMktClass}">${mkt}</span>${etfTag}</td>
      <td>${isPrivate ? "***" : fmtNum(shares)}</td>
      <td>${isPrivate ? "***" : cost.toFixed(2)}</td>
      <td>${price > 0 ? price.toFixed(2) : "--"}</td>
      <td class="${pnlClass}"><b>${isPrivate ? "***" : `${pnlSign}${fmtNum(Math.round(pnl))}`}</b></td>
      <td class="${pnlClass}">${pnlSign}${roi.toFixed(2)}%</td>
      <td>${divInfo.frequency || "年配"}</td>
      <td>${singleDiv > 0 ? singleDiv.toFixed(2) : "--"}</td>
      <td class="text-gold">${isPrivate ? "***" : (estAnnualDiv > 0 ? `$ ${fmtNum(Math.round(estAnnualDiv))}` : "--")}</td>
      <td><span style="color: #8e95a5; font-size: 0.8rem;">${lotText}</span></td>
    `;
    tbody.appendChild(tr);
  });
}

function fmtNum(n) {
  return Number(n).toLocaleString("en-US");
}
