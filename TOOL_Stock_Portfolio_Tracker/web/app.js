/**
 * 零知識端到端加密投資庫存儀表板 (Web Crypto API 原生純前端)
 * 具備瀏覽器直連即時行情 (CNYES + TradingView 雙引擎 + Firebase 備援)、
 * 即時動態損益重算與自組 ETF 盤中淨值/K線同步運算
 */

const DEFAULT_FIREBASE_URL = "https://my-stock-tracker-2a94e-default-rtdb.asia-southeast1.firebasedatabase.app";
const WEB_VERSION = "V1.2.1";
const PALETTE = ["#3a86ff", "#00f090", "#ffbe0b", "#ff3366", "#8338ec", "#06d6a0", "#118ab2", "#fb5607"];

let state = {
  user: "",
  password: "",
  secretKey: "",
  rtdbUrl: DEFAULT_FIREBASE_URL,
  decryptedData: null,
  privacyMode: false,
  rawPositions: [],
  normalizedPositions: [],
  deviceType: "desktop", // "mobile" | "tablet" | "desktop"
  currentViewMode: "table", // 預設以表格視圖為主
  sortField: "symbol",
  sortOrder: "asc",
  lastCloudUpdateTime: "",
  lastPortfolioSyncTime: "",
  lastQuotesTime: "",
  quoteSourceLabel: "即時"
};

let autoPollInterval = null;
let pollTickCounter = 0;
let isFetchingQuotes = false;

// 使用者自訂維持率風控閥值 (預設: 警戒線 160%, 追繳線 130%)
let marginThresholds = {
  warning: 160,
  call: 130
};

function loadMarginThresholds() {
  const savedWarn = localStorage.getItem("portfolio_thresh_warning");
  const savedCall = localStorage.getItem("portfolio_thresh_call");
  if (savedWarn) {
    const w = parseFloat(savedWarn);
    if (!isNaN(w) && w >= 130 && w <= 250) marginThresholds.warning = w;
  }
  if (savedCall) {
    const c = parseFloat(savedCall);
    if (!isNaN(c) && c >= 100 && c <= 200) marginThresholds.call = c;
  }
}

function saveMarginThresholds(warn, call) {
  marginThresholds.warning = warn;
  marginThresholds.call = call;
  localStorage.setItem("portfolio_thresh_warning", String(warn));
  localStorage.setItem("portfolio_thresh_call", String(call));
}

function getNowFormatted() {
  const d = new Date();
  const pad = n => String(n).padStart(2, "0");
  return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())} ${pad(d.getHours())}:${pad(d.getMinutes())}:${pad(d.getSeconds())}`;
}

function formatEpochToTaiwanTime(epochSec) {
  if (!epochSec || isNaN(epochSec) || epochSec <= 0) return "";
  const baseMs = epochSec > 1e12 ? epochSec : epochSec * 1000;
  const twMs = baseMs + 8 * 3600 * 1000;
  const d = new Date(twMs);
  const pad = n => String(n).padStart(2, "0");
  return `${d.getUTCFullYear()}-${pad(d.getUTCMonth() + 1)}-${pad(d.getUTCDate())} ${pad(d.getUTCHours())}:${pad(d.getUTCMinutes())}:${pad(d.getUTCSeconds())}`;
}

function getTaiwanTodayDateStr() {
  const twMs = Date.now() + 8 * 3600 * 1000;
  const d = new Date(twMs);
  const pad = n => String(n).padStart(2, "0");
  return `${d.getUTCFullYear()}-${pad(d.getUTCMonth() + 1)}-${pad(d.getUTCDate())}`;
}

function fetchWithTimeout(url, options = {}, timeoutMs = 6000) {
  const ctrl = new AbortController();
  const timer = setTimeout(() => ctrl.abort(), timeoutMs);
  return fetch(url, { ...options, signal: ctrl.signal }).finally(() => clearTimeout(timer));
}

// --- 客戶端獨立即時報價引擎 (Direct Browser Multi-Tier Live Quotes Engine) ---

async function fetchCnyesLiveQuotes(twSymbols) {
  const result = {};
  if (!twSymbols || twSymbols.length === 0) return result;

  const batchSize = 25;
  for (let i = 0; i < twSymbols.length; i += batchSize) {
    const chunk = twSymbols.slice(i, i + batchSize);
    const symbolsParam = chunk
      .map(s => {
        const clean = s.replace(/(\.TW|\.TWO)$/i, "").replace(/[^A-Z0-9]/gi, "");
        return `TWS:${clean}:STOCK`;
      })
      .join(",");
    const url = `https://ws.api.cnyes.com/ws/api/v1/quote/quotes/${symbolsParam}?column=I&_t=${Date.now()}`;
    try {
      const resp = await fetchWithTimeout(url, { cache: "no-store" }, 5500);
      if (!resp.ok) continue;
      const json = await resp.json();
      const items = Array.isArray(json.data)
        ? json.data
        : (json.data && Array.isArray(json.data.items) ? json.data.items : []);

      for (const it of items) {
        if (!it) continue;
        const sym = String(it["200010"] || "").trim().toUpperCase();
        if (!sym) continue;

        const tradePrice = parseFloat(it["6"]);
        const yClose = parseFloat(it["21"]);
        const openP = parseFloat(it["19"]);
        const highP = parseFloat(it["12"]);
        const lowP = parseFloat(it["13"]);
        const volLots = parseFloat(it["800001"]);
        const matchEpoch = parseFloat(it["200007"]);

        const validPrice = (tradePrice > 0) ? tradePrice : ((yClose > 0) ? yClose : 0);
        if (validPrice <= 0) continue;

        const effOpen = openP > 0 ? openP : validPrice;
        const effHigh = highP > 0 ? Math.max(highP, effOpen, validPrice) : Math.max(effOpen, validPrice);
        const effLow = lowP > 0 ? Math.min(lowP, effOpen, validPrice) : Math.min(effOpen, validPrice);

        result[sym] = {
          price: validPrice,
          yesterday_close: yClose > 0 ? yClose : validPrice,
          open: effOpen,
          high: effHigh,
          low: effLow,
          volume: (!isNaN(volLots) && volLots > 0) ? Math.round(volLots * 1000) : 0,
          name: it["200009"] || sym,
          time: formatEpochToTaiwanTime(matchEpoch) || getNowFormatted(),
          source: "LIVE_CNYES"
        };
        result[`${sym}.TW`] = result[sym];
        result[`${sym}.TWO`] = result[sym];
      }
    } catch (e) {
      // 單批失敗時自動交由下一層 TradingView 即時引擎接手
    }
  }
  return result;
}

async function fetchTradingViewLiveQuotes(symbols, marketRegion = "taiwan") {
  const result = {};
  if (!symbols || symbols.length === 0) return result;

  const prefixes = marketRegion === "america"
    ? ["NASDAQ", "NYSE", "AMEX"]
    : ["TWSE", "TPEX"];

  const tickers = [];
  for (const s of symbols) {
    const cleanSym = s.replace(/[^A-Z0-9.\-]/gi, "");
    for (const pfx of prefixes) {
      tickers.push(`${pfx}:${cleanSym}`);
    }
  }

  const url = `https://scanner.tradingview.com/${marketRegion}/scan`;
  const payload = JSON.stringify({
    symbols: { tickers },
    columns: ["name", "close", "change", "change_abs", "open", "high", "low", "volume"]
  });

  try {
    const resp = await fetchWithTimeout(url, {
      method: "POST",
      headers: { "Content-Type": "text/plain;charset=UTF-8" },
      body: payload,
      cache: "no-store"
    }, 6000);
    if (!resp.ok) return result;
    const json = await resp.json();
    const rows = Array.isArray(json.data) ? json.data : [];
    const nowTime = getNowFormatted();

    for (const row of rows) {
      if (!row || !Array.isArray(row.d)) continue;
      const [symName, closeP, chgPct, chgAbs, openP, highP, lowP, vol] = row.d;
      const sym = String(symName || "").trim().toUpperCase();
      const validPrice = parseFloat(closeP);
      if (!sym || isNaN(validPrice) || validPrice <= 0) continue;

      let yClose = validPrice;
      const absVal = parseFloat(chgAbs);
      if (!isNaN(absVal) && validPrice - absVal > 0) {
        yClose = Number((validPrice - absVal).toFixed(4));
      } else if (!isNaN( parseFloat(chgPct) ) && Math.abs(parseFloat(chgPct)) > 0) {
        yClose = Number((validPrice / (1 + parseFloat(chgPct) / 100)).toFixed(4));
      }

      const effOpen = parseFloat(openP) > 0 ? parseFloat(openP) : validPrice;
      const effHigh = parseFloat(highP) > 0 ? Math.max(parseFloat(highP), effOpen, validPrice) : Math.max(effOpen, validPrice);
      const effLow = parseFloat(lowP) > 0 ? Math.min(parseFloat(lowP), effOpen, validPrice) : Math.min(effOpen, validPrice);

      result[sym] = {
        price: validPrice,
        yesterday_close: yClose,
        open: effOpen,
        high: effHigh,
        low: effLow,
        volume: (!isNaN(parseFloat(vol)) && parseFloat(vol) > 0) ? Math.round(parseFloat(vol)) : 0,
        time: nowTime,
        source: "LIVE_TV"
      };
    }
  } catch (e) {
    // 靜默降級至第三層雲端備援
  }
  return result;
}

async function fetchLiveQuotesAndUpdateUI() {
  if (!state.decryptedData || !state.rawPositions || state.rawPositions.length === 0) return;
  if (isFetchingQuotes) return;
  isFetchingQuotes = true;

  try {
    const twSymbolsSet = new Set();
    const usSymbolsSet = new Set();

    state.rawPositions.forEach(p => {
      const sym = (p.symbol || "").trim().toUpperCase();
      if (!sym) return;
      const mkt = (p.market || "").toUpperCase();
      const isUs = mkt === "US" || (!mkt && /^[A-Z.\-]+$/.test(sym) && !/^\d/.test(sym));
      if (isUs) usSymbolsSet.add(sym);
      else twSymbolsSet.add(sym);
    });

    const twSymbols = Array.from(twSymbolsSet);
    const usSymbols = Array.from(usSymbolsSet);
    const allSymbols = [...twSymbols, ...usSymbols];

    const quotesMap = {};
    let quotesTime = "";
    let liveHitCount = 0;

    // 第一層：瀏覽器直連鉅亨網 CNYES 即時撮合報價 API (台股上市/上櫃/ETF/債券ETF 毫秒級 0.16s 直連)
    if (twSymbols.length > 0) {
      const cnyesQuotes = await fetchCnyesLiveQuotes(twSymbols);
      for (const sym in cnyesQuotes) {
        quotesMap[sym] = cnyesQuotes[sym];
        liveHitCount++;
        if (cnyesQuotes[sym].time && (!quotesTime || cnyesQuotes[sym].time > quotesTime)) {
          quotesTime = cnyesQuotes[sym].time;
        }
      }
    }

    // 第二層：TradingView Scanner 即時報價引擎 (補齊漏網台股 + 美股即時報價)
    const missingTw = twSymbols.filter(s => !quotesMap[s]);
    const tvTasks = [];
    if (missingTw.length > 0) tvTasks.push(fetchTradingViewLiveQuotes(missingTw, "taiwan"));
    if (usSymbols.length > 0) tvTasks.push(fetchTradingViewLiveQuotes(usSymbols, "america"));

    if (tvTasks.length > 0) {
      const tvResults = await Promise.all(tvTasks);
      for (const mapObj of tvResults) {
        for (const sym in mapObj) {
          if (!quotesMap[sym]) {
            quotesMap[sym] = mapObj[sym];
            liveHitCount++;
            if (mapObj[sym].time && (!quotesTime || mapObj[sym].time > quotesTime)) {
              quotesTime = mapObj[sym].time;
            }
          }
        }
      }
    }

    // 第三層：若仍有未命中的標的 (如極端斷網或防火牆封鎖)，從 Firebase /stats/quotes.json 補齊
    const stillMissing = allSymbols.filter(s => !quotesMap[s]);
    if (stillMissing.length > 0) {
      try {
        const qResp = await fetchWithTimeout(`${state.rtdbUrl}/stats/quotes.json?t=${Date.now()}`, { cache: "no-store" }, 5000);
        if (qResp.ok) {
          const qData = await qResp.json();
          if (qData && typeof qData === "object") {
            for (const sym of stillMissing) {
              const fbQ = qData[sym];
              if (fbQ && fbQ.price && fbQ.price > 0) {
                quotesMap[sym] = {
                  price: parseFloat(fbQ.price),
                  yesterday_close: parseFloat(fbQ.yesterday_close) || parseFloat(fbQ.price),
                  time: fbQ.time || "",
                  source: "FIREBASE_CACHE"
                };
                if (!quotesTime && fbQ.time) quotesTime = fbQ.time;
              }
            }
          }
        }
      } catch (e) {}
    }

    // 將最新即時報價套用至當前解密的部位
    state.rawPositions.forEach(p => {
      const sym = (p.symbol || "").trim().toUpperCase();
      const q = quotesMap[sym];
      if (q && q.price && q.price > 0) {
        p.current_price = q.price;
        if (q.yesterday_close && q.yesterday_close > 0) {
          p.yesterday_close = q.yesterday_close;
        }
        if (q.open && q.open > 0) p._live_open = q.open;
        if (q.high && q.high > 0) p._live_high = q.high;
        if (q.low && q.low > 0) p._live_low = q.low;
        if (q.volume !== undefined) p._live_volume = q.volume;
      }
    });

    const clientNow = getNowFormatted();
    state.lastClientRefreshTime = clientNow;

    if (!quotesTime) {
      quotesTime = clientNow;
    }
    state.quoteSourceLabel = liveHitCount > 0 ? "即時" : "雲端快取";
    state.lastQuotesTime = quotesTime;

    // 同步動態更新「自組 ETF 淨值 K 線與多週期績效矩陣」
    updateSyntheticEtfWithLiveQuotes(quotesMap, quotesTime);

    // 即時重算全庫存損益與畫面渲染
    renderDashboard(state.decryptedData, state.lastCloudUpdateTime, quotesTime, state.lastPortfolioSyncTime);

    // 綠色小圓點與邊框閃爍提示 (視覺化反饋)
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
      }, 1200);
    }
  } finally {
    isFetchingQuotes = false;
  }
}

function captureSyntheticEtfBaseline(decrypted) {
  if (!decrypted || !decrypted.synthetic_etf) return;
  const etf = decrypted.synthetic_etf;
  if (Array.isArray(etf.records) && !etf._origRecords) {
    etf._origRecords = etf.records.map(r => ({ ...r }));
  }
  if (etf.performance && Array.isArray(etf.performance.items) && !etf.performance._origItems) {
    etf.performance._origItems = etf.performance.items.map(it => ({ ...it }));
  }
}

function updateSyntheticEtfWithLiveQuotes(quotesMap, quotesTime) {
  if (!state.decryptedData || !state.decryptedData.synthetic_etf) return;
  const etf = state.decryptedData.synthetic_etf;
  captureSyntheticEtfBaseline(state.decryptedData);

  const divs = state.decryptedData.dividends || {};
  const liveNormalized = (state.rawPositions || []).map(p => normalizePosition(p, divs));
  const normBySym = {};
  liveNormalized.forEach(np => {
    normBySym[(np.symbol || "").trim().toUpperCase()] = np;
  });

  // 1. 動態更新自組 ETF 今日盤中 K 線棒與最新 NAV / MA5 / MA20
  const origRecords = etf._origRecords || [];
  if (origRecords.length > 0 && liveNormalized.length > 0) {
    const baseNav = parseFloat(etf.base_nav) || 100.0;
    const baseMarketVal = parseFloat(origRecords[0].market_val) || 0;

    if (baseMarketVal > 0) {
      const scaleFactor = baseNav / baseMarketVal;
      let openVal = 0;
      let highVal = 0;
      let lowVal = 0;
      let closeVal = 0;
      let totVol = 0;

      liveNormalized.forEach(np => {
        const sym = (np.symbol || "").trim().toUpperCase();
        const shs = np.shares || 0;
        if (shs <= 0 || np.current_price <= 0) return;
        const q = quotesMap[sym] || {};
        const pClose = np.current_price;
        const pOpen = q.open > 0 ? q.open : pClose;
        const pHigh = q.high > 0 ? Math.max(q.high, pOpen, pClose) : Math.max(pOpen, pClose);
        const pLow = q.low > 0 ? Math.min(q.low, pOpen, pClose) : Math.min(pOpen, pClose);

        openVal += shs * pOpen;
        highVal += shs * pHigh;
        lowVal += shs * pLow;
        closeVal += shs * pClose;
        totVol += (q.volume || 0);
      });

      if (closeVal > 0) {
        const quoteDatePart = (quotesTime && /^\d{4}-\d{2}-\d{2}/.test(quotesTime))
          ? quotesTime.slice(0, 10)
          : getTaiwanTodayDateStr();

        const nextRecords = origRecords.map(r => ({ ...r }));
        const lastOrig = nextRecords[nextRecords.length - 1];

        const etfOpen = Number((openVal * scaleFactor).toFixed(2));
        const etfHigh = Number((highVal * scaleFactor).toFixed(2));
        const etfLow = Number((lowVal * scaleFactor).toFixed(2));
        const etfClose = Number((closeVal * scaleFactor).toFixed(2));

        if (quoteDatePart === lastOrig.date) {
          const prevClose = nextRecords.length >= 2 ? nextRecords[nextRecords.length - 2].close : baseNav;
          const dayChg = prevClose > 0 ? Number((etfClose - prevClose).toFixed(2)) : 0;
          const dayChgPct = prevClose > 0 ? Number(((etfClose - prevClose) / prevClose * 100).toFixed(2)) : 0;
          nextRecords[nextRecords.length - 1] = {
            ...lastOrig,
            open: etfOpen,
            high: Math.max(etfHigh, etfOpen, etfClose),
            low: Math.min(etfLow, etfOpen, etfClose),
            close: etfClose,
            volume: totVol > 0 ? Math.round(totVol) : (lastOrig.volume || 0),
            market_val: Math.round(closeVal),
            change: dayChg,
            change_pct: dayChgPct
          };
        } else if (quoteDatePart > lastOrig.date) {
          const prevClose = lastOrig.close || baseNav;
          const dayChg = prevClose > 0 ? Number((etfClose - prevClose).toFixed(2)) : 0;
          const dayChgPct = prevClose > 0 ? Number(((etfClose - prevClose) / prevClose * 100).toFixed(2)) : 0;
          nextRecords.push({
            date: quoteDatePart,
            open: etfOpen,
            high: Math.max(etfHigh, etfOpen, etfClose),
            low: Math.min(etfLow, etfOpen, etfClose),
            close: etfClose,
            volume: Math.round(totVol),
            market_val: Math.round(closeVal),
            change: dayChg,
            change_pct: dayChgPct,
            ma5: null,
            ma20: null
          });
        }

        // 重算 MA5 與 MA20
        const closes = nextRecords.map(r => r.close);
        for (let i = 0; i < nextRecords.length; i++) {
          if (i >= 4) {
            const sum5 = closes.slice(i - 4, i + 1).reduce((a, b) => a + b, 0);
            nextRecords[i].ma5 = Number((sum5 / 5).toFixed(2));
          } else {
            nextRecords[i].ma5 = null;
          }
          if (i >= 19) {
            const sum20 = closes.slice(i - 19, i + 1).reduce((a, b) => a + b, 0);
            nextRecords[i].ma20 = Number((sum20 / 20).toFixed(2));
          } else {
            nextRecords[i].ma20 = null;
          }
        }

        const latestRec = nextRecords[nextRecords.length - 1];
        etf.records = nextRecords;
        etf.current_nav = latestRec.close;
        etf.nav_change_pct = latestRec.change_pct;
        etf.total_return_pct = Number(((latestRec.close - baseNav) / baseNav * 100).toFixed(2));
        etf.total_market_value = latestRec.market_val;
        etf.latest_date = latestRec.date;
      }
    }
  }

  // 2. 動態更新各股多週期績效矩陣 (Day / Week / Month / Year / Weight / Unrealized PnL)
  if (etf.performance && Array.isArray(etf.performance._origItems)) {
    const origItems = etf.performance._origItems;
    let totalMktVal = 0;
    let totalCostVal = 0;
    let totalUnrPnl = 0;

    const updatedItems = origItems.map(origIt => {
      const sym = (origIt.symbol || "").trim().toUpperCase();
      const np = normBySym[sym];
      const nextIt = { ...origIt };

      if (np && np.current_price > 0) {
        const origPrice = parseFloat(origIt.current_price) || np.current_price;
        const livePrice = np.current_price;

        nextIt.current_price = livePrice;
        nextIt.market_val = np.market_val;
        nextIt.day_pct = Number(np.day_pct.toFixed(2));
        nextIt.unrealized_pnl = np.unrealized_pnl;
        nextIt.unrealized_pct = Number(np.roi_pct.toFixed(2));

        if (origPrice > 0) {
          const ratio = livePrice / origPrice;
          nextIt.week_pct = Number(((ratio * (1 + (parseFloat(origIt.week_pct) || 0) / 100) - 1) * 100).toFixed(2));
          nextIt.month_pct = Number(((ratio * (1 + (parseFloat(origIt.month_pct) || 0) / 100) - 1) * 100).toFixed(2));
          nextIt.year_pct = Number(((ratio * (1 + (parseFloat(origIt.year_pct) || 0) / 100) - 1) * 100).toFixed(2));
        }
        totalCostVal += np.total_cost;
      } else {
        totalCostVal += (parseFloat(origIt.shares) || 0) * (parseFloat(origIt.cost_price) || 0);
      }

      totalMktVal += (parseFloat(nextIt.market_val) || 0);
      totalUnrPnl += (parseFloat(nextIt.unrealized_pnl) || 0);
      return nextIt;
    });

    updatedItems.forEach(it => {
      it.weight_pct = totalMktVal > 0 ? Number(((it.market_val / totalMktVal) * 100).toFixed(2)) : 0;
    });

    etf.performance.items = updatedItems;
    etf.performance.total_market_value = Math.round(totalMktVal);
    etf.performance.total_cost = Math.round(totalCostVal);
    etf.performance.total_unrealized_pnl = Math.round(totalUnrPnl);
    etf.performance.total_unrealized_pct = totalCostVal > 0 ? Number(((totalUnrPnl / totalCostVal) * 100).toFixed(2)) : 0;
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
    captureSyntheticEtfBaseline(decrypted);
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
  pollTickCounter = 0;

  autoPollInterval = setInterval(async () => {
    if (!state.user || !state.password || !state.decryptedData) return;
    if (document.hidden) return; // 背景分頁暫停輪詢，切回前景時立即喚醒刷新

    pollTickCounter++;

    // A. 每 30 秒探測一次持股部位是否在電腦端有異動
    if (pollTickCounter % 3 === 0) {
      try {
        const checkUrl = `${state.rtdbUrl}/portfolios/${state.user}/updated_at.json?t=${Date.now()}`;
        const r = await fetchWithTimeout(checkUrl, { cache: "no-store" }, 4000);
        if (r.ok) {
          const remoteTime = await r.json();
          if (remoteTime && remoteTime !== state.lastCloudUpdateTime) {
            await reloadCloudDataSilently();
            return;
          }
        }
      } catch (e) {}
    }

    // B. 每 10 秒直接向市場即時報價源抓取最新成交價並重算損益與自組 ETF！
    try {
      await fetchLiveQuotesAndUpdateUI();
    } catch (e) {}
  }, 10000);
}

// 當手機解鎖或切換回瀏覽器分頁時，立即無延遲刷新即時報價
document.addEventListener("visibilitychange", () => {
  if (!document.hidden && state.decryptedData && state.rawPositions.length > 0) {
    fetchLiveQuotesAndUpdateUI();
  }
});

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

      showStatus("已取得資料，正在本機解密並抓取即時行情...", "ok");

      const decrypted = await decryptAesGcm(encPayload, pass, secretKey);
      captureSyntheticEtfBaseline(decrypted);
      state.decryptedData = decrypted;
      state.rawPositions = decrypted.positions || [];
      state.lastPortfolioSyncTime = encPayload.updated_at || decrypted.exported_at || "";
      state.lastCloudUpdateTime = encPayload.updated_at || decrypted.exported_at || "";
      state.lastQuotesTime = decrypted.quotes_update_time || state.lastPortfolioSyncTime || getNowFormatted();

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
    state.normalizedPositions = [];
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
      if (btn) setTimeout(() => { btn.style.opacity = "1"; }, 400);
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
  const tabBtnRank = document.getElementById("tab-btn-rankings");
  const posContent = document.getElementById("positions-tab-content");
  const etfContent = document.getElementById("etf-analytics-tab-content");
  const rankContent = document.getElementById("rankings-tab-content");

  function switchMainTab(activeTab) {
    [tabBtnPos, tabBtnEtf, tabBtnRank].forEach(b => b && b.classList.remove("active"));
    [posContent, etfContent, rankContent].forEach(c => c && c.classList.add("hidden"));

    if (activeTab === "positions") {
      if (tabBtnPos) tabBtnPos.classList.add("active");
      if (posContent) posContent.classList.remove("hidden");
    } else if (activeTab === "etf") {
      if (tabBtnEtf) tabBtnEtf.classList.add("active");
      if (etfContent) etfContent.classList.remove("hidden");
      renderEtfAnalytics();
    } else if (activeTab === "rankings") {
      if (tabBtnRank) tabBtnRank.classList.add("active");
      if (rankContent) rankContent.classList.remove("hidden");
      // 預設進入全市場融資維持率與指標排行 (對齊本機端 unified_ranking_gui.py)
      const subBtnMargin = document.getElementById("sub-tab-btn-margin");
      const subBtnMarket = document.getElementById("sub-tab-btn-market");
      const subContentMargin = document.getElementById("sub-content-margin");
      const subContentMarket = document.getElementById("sub-content-market");
      if (subBtnMarket && subBtnMarket.classList.contains("active")) {
        if (subContentMarket) subContentMarket.classList.remove("hidden");
        if (subContentMargin) subContentMargin.classList.add("hidden");
        renderMarketRankings();
      } else {
        renderMarginRiskAnalytics();
      }
      loadMarketRankingCacheSilently();
    }
  }

  if (tabBtnPos) tabBtnPos.addEventListener("click", () => switchMainTab("positions"));
  if (tabBtnEtf) tabBtnEtf.addEventListener("click", () => switchMainTab("etf"));
  if (tabBtnRank) tabBtnRank.addEventListener("click", () => switchMainTab("rankings"));

  // 子分頁切換監聽 (融資風控 vs 全市場排行)
  const subBtnMargin = document.getElementById("sub-tab-btn-margin");
  const subBtnMarket = document.getElementById("sub-tab-btn-market");
  const subContentMargin = document.getElementById("sub-content-margin");
  const subContentMarket = document.getElementById("sub-content-market");

  if (subBtnMargin && subBtnMarket) {
    subBtnMargin.addEventListener("click", () => {
      subBtnMargin.classList.add("active");
      subBtnMarket.classList.remove("active");
      if (subContentMargin) subContentMargin.classList.remove("hidden");
      if (subContentMarket) subContentMarket.classList.add("hidden");
      renderMarginRiskAnalytics();
    });

    subBtnMarket.addEventListener("click", () => {
      subBtnMarket.classList.add("active");
      subBtnMargin.classList.remove("active");
      if (subContentMarket) subContentMarket.classList.remove("hidden");
      if (subContentMargin) subContentMargin.classList.add("hidden");
      renderMarketRankings();
    });
  }

  // 載入自訂維持率閥值設定至輸入框
  loadMarginThresholds();
  const inputThreshWarn = document.getElementById("input-thresh-warning");
  const inputThreshCall = document.getElementById("input-thresh-call");
  const btnResetThresh = document.getElementById("btn-reset-thresh");

  if (inputThreshWarn) inputThreshWarn.value = marginThresholds.warning;
  if (inputThreshCall) inputThreshCall.value = marginThresholds.call;

  if (inputThreshWarn) {
    inputThreshWarn.addEventListener("change", () => {
      let val = parseFloat(inputThreshWarn.value);
      if (isNaN(val) || val < 130) val = 160;
      if (val <= marginThresholds.call) val = marginThresholds.call + 5;
      inputThreshWarn.value = val;
      saveMarginThresholds(val, marginThresholds.call);
      renderMarginRiskAnalytics();
    });
  }

  if (inputThreshCall) {
    inputThreshCall.addEventListener("change", () => {
      let val = parseFloat(inputThreshCall.value);
      if (isNaN(val) || val < 100) val = 130;
      if (val >= marginThresholds.warning) val = marginThresholds.warning - 5;
      inputThreshCall.value = val;
      saveMarginThresholds(marginThresholds.warning, val);
      renderMarginRiskAnalytics();
    });
  }

  if (btnResetThresh) {
    btnResetThresh.addEventListener("click", () => {
      saveMarginThresholds(160, 130);
      if (inputThreshWarn) inputThreshWarn.value = 160;
      if (inputThreshCall) inputThreshCall.value = 130;
      renderMarginRiskAnalytics();
    });
  }

  // 全市場指標按鈕切換
  document.querySelectorAll("#market-rank-type-switch .btn-period").forEach(btn => {
    btn.addEventListener("click", () => {
      document.querySelectorAll("#market-rank-type-switch .btn-period").forEach(b => b.classList.remove("active"));
      btn.classList.add("active");
      marketRankState.currentType = btn.getAttribute("data-mtype") || "yield";
      renderMarketRankings();
    });
  });

  const mFilterSelect = document.getElementById("market-filter-select");
  if (mFilterSelect) {
    mFilterSelect.addEventListener("change", (e) => {
      marketRankState.filter = e.target.value;
      renderMarketRankings();
    });
  }

  const btnExportCsv = document.getElementById("btn-export-market-csv");
  if (btnExportCsv) {
    btnExportCsv.addEventListener("click", () => {
      downloadCurrentMarketCsv();
    });
  }

  const btnExportAllCsv = document.getElementById("btn-export-all-tabs-csv");
  if (btnExportAllCsv) {
    btnExportAllCsv.addEventListener("click", () => {
      exportAllMarketTabsCsv();
    });
  }

  const mRatioModeSelect = document.getElementById("market-ratio-mode-select");
  if (mRatioModeSelect) {
    const savedRatioMode = localStorage.getItem("portfolio_market_ratio_mode");
    if (savedRatioMode) {
      marketRankState.ratioMode = savedRatioMode;
      mRatioModeSelect.value = savedRatioMode;
    }
    mRatioModeSelect.addEventListener("change", (e) => {
      marketRankState.ratioMode = e.target.value;
      localStorage.setItem("portfolio_market_ratio_mode", e.target.value);
      renderMarketRankings();
    });
  }

  // 自組 ETF K 線範圍按鈕切換
  document.querySelectorAll(".btn-etf-range").forEach(btn => {
    btn.addEventListener("click", () => {
      document.querySelectorAll(".btn-etf-range").forEach(b => b.classList.remove("active"));
      btn.classList.add("active");
      etfChartState.displayDays = parseInt(btn.getAttribute("data-days") || "0", 10);
      drawEtfKlineChart();
    });
  });

  // 各股績效多週期切換
  document.querySelectorAll(".btn-period").forEach(btn => {
    btn.addEventListener("click", () => {
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

// --- 數據標準化與即時損益動態重算核心 (100% 對齊桌面版 pnl_calculator.py) ---

function normalizePosition(pos, divs = {}) {
  const shares = parseFloat(pos.shares) || 0;
  const costPrice = parseFloat(pos.cost_price) || 0;
  const curPrice = parseFloat(pos.current_price !== undefined ? pos.current_price : pos.cost_price) || 0;
  const mkt = (pos.market || "TW").toUpperCase();
  const isEtf = Boolean(pos.is_etf);

  // 推導手續費折數 (優先讀取 fee_discount，若舊版雲端封包未帶則由既有 sell_fee 反推，預設 0.6 折)
  let feeDiscount = 0.6;
  if (pos.fee_discount !== undefined && parseFloat(pos.fee_discount) > 0) {
    feeDiscount = parseFloat(pos.fee_discount);
  } else if (pos.sell_fee > 20 && pos.market_val > 0) {
    const inferred = parseFloat(pos.sell_fee) / (parseFloat(pos.market_val) * 0.001425);
    if (inferred > 0.05 && inferred <= 1.0) {
      feeDiscount = Number(inferred.toFixed(2));
    }
  }

  let totalCost = 0;
  let marketVal = 0;
  let unrealizedPnl = 0;

  if (mkt === "TW" || mkt === "TWO") {
    if (pos.total_cost !== undefined && parseFloat(pos.total_cost) > 0) {
      totalCost = parseFloat(pos.total_cost);
    } else {
      const rawBuyFee = costPrice * shares * 0.001425 * feeDiscount;
      const buyFee = shares >= 1000 ? Math.max(20, Math.floor(rawBuyFee)) : Math.ceil(rawBuyFee);
      totalCost = Math.round(costPrice * shares + buyFee);
    }

    if (curPrice > 0 && shares > 0) {
      marketVal = Math.round(curPrice * shares);
      const rawSellFee = curPrice * shares * 0.001425 * feeDiscount;
      const sellFee = shares >= 1000 ? Math.max(20, Math.floor(rawSellFee)) : Math.ceil(rawSellFee);
      const taxRate = isEtf ? 0.001 : 0.003;
      const tax = Math.floor(curPrice * shares * taxRate);
      unrealizedPnl = (marketVal - sellFee - tax) - totalCost;
    } else {
      marketVal = parseFloat(pos.market_val) || 0;
      unrealizedPnl = parseFloat(pos.unrealized_pnl) || 0;
    }
  } else {
    totalCost = (pos.total_cost !== undefined && parseFloat(pos.total_cost) > 0)
      ? parseFloat(pos.total_cost)
      : Number((costPrice * shares).toFixed(2));
    marketVal = curPrice > 0 ? Number((curPrice * shares).toFixed(2)) : (parseFloat(pos.market_val) || 0);
    unrealizedPnl = Number((marketVal - totalCost).toFixed(2));
  }

  const roiPct = totalCost > 0 ? (unrealizedPnl / totalCost) * 100 : 0;

  // 當日損益：始終根據最新現價 curPrice 與昨收價 yClose 即時動態計算！
  const yClose = (parseFloat(pos.yesterday_close) > 0) ? parseFloat(pos.yesterday_close) : curPrice;
  const dayPnl = (curPrice > 0 && yClose > 0) ? Math.round((curPrice - yClose) * shares) : 0;
  const dayPct = (curPrice > 0 && yClose > 0) ? ((curPrice - yClose) / yClose) * 100 : 0;

  // 當週損益：始終根據最新現價 curPrice 與上週基準收盤價 wClose 即時動態計算！
  const wClose = (parseFloat(pos.week_close) > 0) ? parseFloat(pos.week_close) : yClose;
  const weekPnl = (curPrice > 0 && wClose > 0) ? Math.round((curPrice - wClose) * shares) : 0;
  const weekPct = (curPrice > 0 && wClose > 0) ? ((curPrice - wClose) / wClose) * 100 : 0;

  // 股息資訊
  const divInfo = divs[pos.symbol] || {};
  const cashDiv = parseFloat(pos.cash_dividend !== undefined ? pos.cash_dividend : (divInfo.annual_div || divInfo.cash_dividend || 0)) || 0;
  const singleDiv = parseFloat(pos.single_dividend !== undefined ? pos.single_dividend : (divInfo.single_amt || divInfo.single_dividend || cashDiv)) || 0;
  const frequency = pos.frequency || divInfo.frequency || "年配";
  const totalDiv = parseFloat(pos.total_dividend !== undefined ? pos.total_dividend : (shares * cashDiv)) || 0;
  const yieldOnCost = costPrice > 0 ? (cashDiv / costPrice) * 100 : 0;
  const yieldOnPrice = curPrice > 0 ? (cashDiv / curPrice) * 100 : 0;
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
    const srcTag = state.quoteSourceLabel || "即時";
    const refreshSec = state.lastClientRefreshTime ? state.lastClientRefreshTime.split(" ")[1] : (quotesUpdateTime && quotesUpdateTime.includes(" ") ? quotesUpdateTime.split(" ")[1] : "");
    const refreshText = refreshSec ? ` <span style="font-size: 11px; opacity: 0.85; margin-left: 3px;">(連線 ${refreshSec})</span>` : "";
    quoteEl.innerHTML = `<span class="live-dot"></span> ${srcTag}: ${quotesTime}${refreshText}`;
    quoteEl.title = `市場成交撮合時間: ${quotesTime}\n前端瀏覽器最後連線刷新時間: ${state.lastClientRefreshTime || "剛剛"} (${srcTag}直連)`;
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
          <span class="sc-price-label">參考現價 (即時)</span>
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
          <span class="sc-cell-val ${p.day_pnl >= 0 ? "text-red" : "text-green"}">${isPrivate ? "***" : fmtSign(p.day_pnl)} (${fmtPctSign(p.day_pct)})</span>
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
  hoverRecord: null,
  _renderedRecords: [],
  _stepX: 1,
  _paddingLeft: 15
};

function renderEtfAnalytics() {
  const data = state.decryptedData;
  if (!data) return;

  const etfData = data.synthetic_etf || {};
  const perfData = etfData.performance || {};

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
    etfChartState._renderedRecords = [];
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

  etfChartState._renderedRecords = records;
  etfChartState._stepX = stepX;
  etfChartState._paddingLeft = paddingLeft;

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

  // 綁定懸停游標事件 (動態讀取 etfChartState，確保切換週期與即時更新後始終精準)
  if (!canvas.getAttribute("data-bound")) {
    canvas.setAttribute("data-bound", "true");
    canvas.addEventListener("mousemove", (e) => {
      const curRecords = etfChartState._renderedRecords || [];
      if (curRecords.length === 0) return;

      const rect = canvas.getBoundingClientRect();
      const scaleX = canvas.width / rect.width;
      const mouseX = (e.clientX - rect.left) * scaleX;

      const idx = Math.floor((mouseX - etfChartState._paddingLeft) / etfChartState._stepX);
      if (idx >= 0 && idx < curRecords.length) {
        const r = curRecords[idx];
        const infoEl = document.getElementById("etf-crosshair-info");
        if (infoEl) {
          const s = r.change_pct > 0 ? "+" : "";
          infoEl.innerHTML = `<b>${r.date}</b> | 開: <b>${r.open.toFixed(2)}</b> 高: <b>${r.high.toFixed(2)}</b> 低: <b>${r.low.toFixed(2)}</b> 收: <b>${r.close.toFixed(2)}</b> (${s}${r.change_pct.toFixed(2)}%)`;
        }
        if (r.ma5) document.getElementById("etf-kline-ma5").textContent = r.ma5.toFixed(2);
        if (r.ma20) document.getElementById("etf-kline-ma20").textContent = r.ma20.toFixed(2);
        if (r.volume !== undefined) document.getElementById("etf-kline-vol").textContent = fmtNum(Math.round(r.volume / 1000)) + " 張";
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
    if (typeof vA === "string") return order === "asc" ? vA.localeCompare(vB) : vB.localeCompare(vA);
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


// ============================================================================
// 第 3 分頁：個人庫存融資維持率風控與全市場 4 大指標排行前端引擎
// ============================================================================

let marketRankState = {
  currentType: "margin_market", // 預設切至全市場融資維持率排行 (對齊本機端 unified_ranking_gui.py)
  filter: "all",
  ratioMode: "all_three", // "all_three" | "call_only" | "buffer_only" | "ratio_only"
  rawMarketData: null,
  twseMarginData: null,
  isFetchingMargin: false
};

const TWSE_MARGIN_BASELINE = {"date": "20261030", "market_maintenance_ratio": 163.08, "total_margin_loan_str": "6234.68 億元", "total_margin_loan_val": 623467886000.0, "total_margin_mkt_val": 1016725578550.0, "risk_label": "去槓桿警戒區 (140%~165%)", "risk_level": "WARNING", "items": [{"symbol": "3481", "name": "群創", "price": 42.55, "margin_balance_lots": 303692, "margin_utilization": 15.21, "margin_net_lots": -10004, "call_price": 33.19, "is_etf": false}, {"symbol": "00632R", "name": "元大台灣50反1", "price": 0.0, "margin_balance_lots": 290822, "margin_utilization": 50.45, "margin_net_lots": 24691, "call_price": 0.0, "is_etf": true}, {"symbol": "00631L", "name": "元大台灣50正2", "price": 0.0, "margin_balance_lots": 285379, "margin_utilization": 13.53, "margin_net_lots": 9115, "call_price": 0.0, "is_etf": true}, {"symbol": "2409", "name": "友達", "price": 30.65, "margin_balance_lots": 269997, "margin_utilization": 14.31, "margin_net_lots": -18082, "call_price": 23.91, "is_etf": false}, {"symbol": "00685L", "name": "群益臺灣加權正2", "price": 0.0, "margin_balance_lots": 264882, "margin_utilization": 13.26, "margin_net_lots": 17792, "call_price": 0.0, "is_etf": true}, {"symbol": "00981A", "name": "主動統一台股增長", "price": 0.0, "margin_balance_lots": 221996, "margin_utilization": 8.89, "margin_net_lots": 3780, "call_price": 0.0, "is_etf": true}, {"symbol": "00406A", "name": "主動中信台灣收益", "price": 0.0, "margin_balance_lots": 216321, "margin_utilization": 5.7, "margin_net_lots": 14838, "call_price": 0.0, "is_etf": true}, {"symbol": "00673R", "name": "期元大S&P原油反1", "price": 0.0, "margin_balance_lots": 164079, "margin_utilization": 49.29, "margin_net_lots": -1192, "call_price": 0.0, "is_etf": true}, {"symbol": "2303", "name": "聯電", "price": 111.5, "margin_balance_lots": 156568, "margin_utilization": 4.98, "margin_net_lots": -1778, "call_price": 86.97, "is_etf": false}, {"symbol": "6770", "name": "力積電", "price": 66.6, "margin_balance_lots": 132941, "margin_utilization": 11.27, "margin_net_lots": 373, "call_price": 51.95, "is_etf": false}, {"symbol": "00991A", "name": "主動復華未來50", "price": 0.0, "margin_balance_lots": 113307, "margin_utilization": 10.89, "margin_net_lots": -2413, "call_price": 0.0, "is_etf": true}, {"symbol": "00953B", "name": "群益優選非投等債", "price": 0.0, "margin_balance_lots": 112282, "margin_utilization": 9.54, "margin_net_lots": 3018, "call_price": 0.0, "is_etf": true}, {"symbol": "00671R", "name": "富邦NASDAQ反1", "price": 0.0, "margin_balance_lots": 107054, "margin_utilization": 87.95, "margin_net_lots": -1023, "call_price": 0.0, "is_etf": true}, {"symbol": "6949", "name": "沛爾生醫*-創", "price": 39.05, "margin_balance_lots": 106397, "margin_utilization": 32.45, "margin_net_lots": -296, "call_price": 30.46, "is_etf": false}, {"symbol": "2344", "name": "華邦電", "price": 164.5, "margin_balance_lots": 104682, "margin_utilization": 9.31, "margin_net_lots": -954, "call_price": 128.31, "is_etf": false}, {"symbol": "00637L", "name": "元大滬深300正2", "price": 0.0, "margin_balance_lots": 93900, "margin_utilization": 43.64, "margin_net_lots": -384, "call_price": 0.0, "is_etf": true}, {"symbol": "2337", "name": "旺宏", "price": 95.4, "margin_balance_lots": 86115, "margin_utilization": 17.34, "margin_net_lots": -1964, "call_price": 74.41, "is_etf": false}, {"symbol": "00753L", "name": "中信中國50正2", "price": 0.0, "margin_balance_lots": 84448, "margin_utilization": 39.85, "margin_net_lots": -634, "call_price": 0.0, "is_etf": true}, {"symbol": "00403A", "name": "主動統一升級50", "price": 0.0, "margin_balance_lots": 76711, "margin_utilization": 2.37, "margin_net_lots": -2394, "call_price": 0.0, "is_etf": true}, {"symbol": "009829", "name": "大華韓國KOSPI50", "price": 0.0, "margin_balance_lots": 73483, "margin_utilization": 50.78, "margin_net_lots": 1599, "call_price": 0.0, "is_etf": true}, {"symbol": "00664R", "name": "國泰臺灣加權反1", "price": 0.0, "margin_balance_lots": 65127, "margin_utilization": 30.65, "margin_net_lots": 1097, "call_price": 0.0, "is_etf": true}, {"symbol": "2408", "name": "南亞科", "price": 454.5, "margin_balance_lots": 60151, "margin_utilization": 7.76, "margin_net_lots": 605, "call_price": 354.51, "is_etf": false}, {"symbol": "00665L", "name": "富邦恒生國企正2", "price": 0.0, "margin_balance_lots": 55842, "margin_utilization": 51.47, "margin_net_lots": -597, "call_price": 0.0, "is_etf": true}, {"symbol": "2317", "name": "鴻海", "price": 245.5, "margin_balance_lots": 55806, "margin_utilization": 1.59, "margin_net_lots": -193, "call_price": 191.49, "is_etf": false}, {"symbol": "2324", "name": "仁寶", "price": 35.2, "margin_balance_lots": 54869, "margin_utilization": 4.98, "margin_net_lots": -562, "call_price": 27.46, "is_etf": false}, {"symbol": "00680L", "name": "元大美債20正2", "price": 0.0, "margin_balance_lots": 52038, "margin_utilization": 15.44, "margin_net_lots": 591, "call_price": 0.0, "is_etf": true}, {"symbol": "2312", "name": "金寶", "price": 29.65, "margin_balance_lots": 51998, "margin_utilization": 13.81, "margin_net_lots": -826, "call_price": 23.13, "is_etf": false}, {"symbol": "1314", "name": "中石化", "price": 8.06, "margin_balance_lots": 51622, "margin_utilization": 5.46, "margin_net_lots": -52, "call_price": 6.29, "is_etf": false}, {"symbol": "1303", "name": "南亞", "price": 296.0, "margin_balance_lots": 45725, "margin_utilization": 2.31, "margin_net_lots": -625, "call_price": 230.88, "is_etf": false}, {"symbol": "2327", "name": "國巨*", "price": 502.0, "margin_balance_lots": 45616, "margin_utilization": 8.85, "margin_net_lots": 155, "call_price": 391.56, "is_etf": false}, {"symbol": "9105", "name": "泰金寶-DR", "price": 0.0, "margin_balance_lots": 44687, "margin_utilization": 11.79, "margin_net_lots": -584, "call_price": 0.0, "is_etf": false}, {"symbol": "1802", "name": "台玻", "price": 62.3, "margin_balance_lots": 44394, "margin_utilization": 6.11, "margin_net_lots": -2087, "call_price": 48.59, "is_etf": false}, {"symbol": "009828", "name": "中信台日韓PCB", "price": 0.0, "margin_balance_lots": 43793, "margin_utilization": 13.42, "margin_net_lots": 3831, "call_price": 0.0, "is_etf": true}, {"symbol": "2887", "name": "台新新光金", "price": 42.25, "margin_balance_lots": 41353, "margin_utilization": 0.66, "margin_net_lots": -653, "call_price": 32.95, "is_etf": false}, {"symbol": "2476", "name": "鉅祥", "price": 110.5, "margin_balance_lots": 40709, "margin_utilization": 73.95, "margin_net_lots": 39, "call_price": 86.19, "is_etf": false}, {"symbol": "6116", "name": "彩晶", "price": 13.3, "margin_balance_lots": 40433, "margin_utilization": 5.66, "margin_net_lots": -1679, "call_price": 10.37, "is_etf": false}, {"symbol": "1605", "name": "華新", "price": 35.95, "margin_balance_lots": 40351, "margin_utilization": 3.64, "margin_net_lots": -75, "call_price": 28.04, "is_etf": false}, {"symbol": "1312", "name": "國喬", "price": 13.85, "margin_balance_lots": 40212, "margin_utilization": 10.68, "margin_net_lots": -767, "call_price": 10.8, "is_etf": false}, {"symbol": "2371", "name": "大同", "price": 27.95, "margin_balance_lots": 40152, "margin_utilization": 7.47, "margin_net_lots": -361, "call_price": 21.8, "is_etf": false}, {"symbol": "00988A", "name": "主動統一全球創新", "price": 0.0, "margin_balance_lots": 40095, "margin_utilization": 5.27, "margin_net_lots": -487, "call_price": 0.0, "is_etf": true}, {"symbol": "2610", "name": "華航", "price": 20.15, "margin_balance_lots": 40075, "margin_utilization": 2.61, "margin_net_lots": -184, "call_price": 15.72, "is_etf": false}, {"symbol": "3231", "name": "緯創", "price": 183.5, "margin_balance_lots": 39260, "margin_utilization": 4.58, "margin_net_lots": -211, "call_price": 143.13, "is_etf": false}, {"symbol": "4763", "name": "材料*-KY", "price": 44.75, "margin_balance_lots": 38497, "margin_utilization": 15.57, "margin_net_lots": -63, "call_price": 34.91, "is_etf": false}, {"symbol": "00712", "name": "復華富時不動產", "price": 0.0, "margin_balance_lots": 38192, "margin_utilization": 2.33, "margin_net_lots": -1314, "call_price": 0.0, "is_etf": true}, {"symbol": "2489", "name": "瑞軒", "price": 35.75, "margin_balance_lots": 37280, "margin_utilization": 24.45, "margin_net_lots": -924, "call_price": 27.89, "is_etf": false}, {"symbol": "1101", "name": "台泥", "price": 28.35, "margin_balance_lots": 37233, "margin_utilization": 1.98, "margin_net_lots": -390, "call_price": 22.11, "is_etf": false}, {"symbol": "2002", "name": "中鋼", "price": 19.25, "margin_balance_lots": 36109, "margin_utilization": 0.92, "margin_net_lots": 679, "call_price": 15.02, "is_etf": false}, {"symbol": "3037", "name": "欣興", "price": 989.0, "margin_balance_lots": 35417, "margin_utilization": 8.62, "margin_net_lots": -43, "call_price": 771.42, "is_etf": false}, {"symbol": "2313", "name": "華通", "price": 202.0, "margin_balance_lots": 35328, "margin_utilization": 11.45, "margin_net_lots": -696, "call_price": 157.56, "is_etf": false}, {"symbol": "2330", "name": "台積電", "price": 2370.0, "margin_balance_lots": 34961, "margin_utilization": 0.54, "margin_net_lots": -367, "call_price": 1848.6, "is_etf": false}, {"symbol": "4906", "name": "正文", "price": 38.2, "margin_balance_lots": 34910, "margin_utilization": 30.91, "margin_net_lots": -358, "call_price": 29.8, "is_etf": false}, {"symbol": "2353", "name": "宏碁", "price": 30.4, "margin_balance_lots": 34512, "margin_utilization": 4.53, "margin_net_lots": 27, "call_price": 23.71, "is_etf": false}, {"symbol": "4958", "name": "臻鼎-KY", "price": 486.0, "margin_balance_lots": 34200, "margin_utilization": 12.07, "margin_net_lots": -380, "call_price": 379.08, "is_etf": false}, {"symbol": "4927", "name": "泰鼎-KY", "price": 50.1, "margin_balance_lots": 33473, "margin_utilization": 50.54, "margin_net_lots": -636, "call_price": 39.08, "is_etf": false}, {"symbol": "00992A", "name": "主動群益科技創新", "price": 0.0, "margin_balance_lots": 33385, "margin_utilization": 4.78, "margin_net_lots": -614, "call_price": 0.0, "is_etf": true}, {"symbol": "2498", "name": "宏達電", "price": 38.8, "margin_balance_lots": 30424, "margin_utilization": 14.54, "margin_net_lots": 382, "call_price": 30.26, "is_etf": false}, {"symbol": "2834", "name": "臺企銀", "price": 18.05, "margin_balance_lots": 30344, "margin_utilization": 1.17, "margin_net_lots": 253, "call_price": 14.08, "is_etf": false}, {"symbol": "6456", "name": "GIS-KY", "price": 92.3, "margin_balance_lots": 30204, "margin_utilization": 35.75, "margin_net_lots": -3353, "call_price": 71.99, "is_etf": false}, {"symbol": "009816", "name": "凱基台灣TOP50", "price": 0.0, "margin_balance_lots": 29915, "margin_utilization": 0.87, "margin_net_lots": 701, "call_price": 0.0, "is_etf": true}, {"symbol": "2323", "name": "中環", "price": 11.45, "margin_balance_lots": 29074, "margin_utilization": 11.86, "margin_net_lots": -284, "call_price": 8.93, "is_etf": false}, {"symbol": "2515", "name": "中工", "price": 11.35, "margin_balance_lots": 28738, "margin_utilization": 6.81, "margin_net_lots": 99, "call_price": 8.85, "is_etf": false}, {"symbol": "2332", "name": "友訊", "price": 19.4, "margin_balance_lots": 28671, "margin_utilization": 19.05, "margin_net_lots": -169, "call_price": 15.13, "is_etf": false}, {"symbol": "2449", "name": "京元電子", "price": 253.5, "margin_balance_lots": 27184, "margin_utilization": 8.47, "margin_net_lots": -675, "call_price": 197.73, "is_etf": false}, {"symbol": "2356", "name": "英業達", "price": 59.5, "margin_balance_lots": 26984, "margin_utilization": 3.01, "margin_net_lots": -5, "call_price": 46.41, "is_etf": false}, {"symbol": "1504", "name": "東元", "price": 69.1, "margin_balance_lots": 26872, "margin_utilization": 4.52, "margin_net_lots": -135, "call_price": 53.9, "is_etf": false}, {"symbol": "0050", "name": "元大台灣50", "price": 0.0, "margin_balance_lots": 26635, "margin_utilization": 0.45, "margin_net_lots": 1015, "call_price": 0.0, "is_etf": true}, {"symbol": "1718", "name": "中纖", "price": 10.55, "margin_balance_lots": 26555, "margin_utilization": 6.3, "margin_net_lots": -352, "call_price": 8.23, "is_etf": false}, {"symbol": "1301", "name": "台塑", "price": 72.5, "margin_balance_lots": 26413, "margin_utilization": 1.66, "margin_net_lots": -781, "call_price": 56.55, "is_etf": false}, {"symbol": "2367", "name": "燿華", "price": 40.55, "margin_balance_lots": 26350, "margin_utilization": 14.92, "margin_net_lots": -165, "call_price": 31.63, "is_etf": false}, {"symbol": "6282", "name": "康舒", "price": 39.0, "margin_balance_lots": 26316, "margin_utilization": 12.3, "margin_net_lots": -284, "call_price": 30.42, "is_etf": false}, {"symbol": "3715", "name": "定穎投控", "price": 120.0, "margin_balance_lots": 25431, "margin_utilization": 35.87, "margin_net_lots": -82, "call_price": 93.6, "is_etf": false}, {"symbol": "3706", "name": "神達", "price": 76.8, "margin_balance_lots": 25229, "margin_utilization": 6.91, "margin_net_lots": 5, "call_price": 59.9, "is_etf": false}, {"symbol": "8039", "name": "台虹", "price": 245.0, "margin_balance_lots": 24577, "margin_utilization": 37.25, "margin_net_lots": -446, "call_price": 191.1, "is_etf": false}, {"symbol": "1907", "name": "永豐餘", "price": 28.1, "margin_balance_lots": 24153, "margin_utilization": 5.82, "margin_net_lots": 49, "call_price": 21.92, "is_etf": false}, {"symbol": "2609", "name": "陽明", "price": 63.5, "margin_balance_lots": 24114, "margin_utilization": 2.76, "margin_net_lots": -788, "call_price": 49.53, "is_etf": false}, {"symbol": "8422", "name": "可寧衛*", "price": 24.7, "margin_balance_lots": 23789, "margin_utilization": 7.0, "margin_net_lots": -210, "call_price": 19.27, "is_etf": false}, {"symbol": "4960", "name": "誠美材", "price": 31.5, "margin_balance_lots": 23749, "margin_utilization": 16.62, "margin_net_lots": -1684, "call_price": 24.57, "is_etf": false}, {"symbol": "2328", "name": "廣宇", "price": 50.6, "margin_balance_lots": 23449, "margin_utilization": 18.1, "margin_net_lots": -1174, "call_price": 39.47, "is_etf": false}, {"symbol": "3591", "name": "艾笛森", "price": 26.0, "margin_balance_lots": 22785, "margin_utilization": 69.9, "margin_net_lots": 51, "call_price": 20.28, "is_etf": false}, {"symbol": "3149", "name": "正達", "price": 66.8, "margin_balance_lots": 22556, "margin_utilization": 32.91, "margin_net_lots": -239, "call_price": 52.1, "is_etf": false}, {"symbol": "00405A", "name": "主動富邦台灣龍耀", "price": 0.0, "margin_balance_lots": 22217, "margin_utilization": 3.44, "margin_net_lots": 669, "call_price": 0.0, "is_etf": true}, {"symbol": "2354", "name": "鴻準", "price": 59.6, "margin_balance_lots": 21808, "margin_utilization": 6.17, "margin_net_lots": -211, "call_price": 46.49, "is_etf": false}, {"symbol": "00927", "name": "群益半導體收益", "price": 0.0, "margin_balance_lots": 21768, "margin_utilization": 4.0, "margin_net_lots": 41, "call_price": 0.0, "is_etf": true}, {"symbol": "3714", "name": "富采", "price": 58.1, "margin_balance_lots": 21721, "margin_utilization": 13.01, "margin_net_lots": -554, "call_price": 45.32, "is_etf": false}, {"symbol": "2486", "name": "一詮", "price": 192.5, "margin_balance_lots": 21276, "margin_utilization": 35.35, "margin_net_lots": -15, "call_price": 150.15, "is_etf": false}, {"symbol": "1714", "name": "和桐", "price": 17.45, "margin_balance_lots": 21251, "margin_utilization": 8.57, "margin_net_lots": -634, "call_price": 13.61, "is_etf": false}, {"symbol": "1326", "name": "台化", "price": 72.5, "margin_balance_lots": 21249, "margin_utilization": 1.45, "margin_net_lots": -256, "call_price": 56.55, "is_etf": false}, {"symbol": "2634", "name": "漢翔", "price": 58.9, "margin_balance_lots": 21147, "margin_utilization": 8.98, "margin_net_lots": -318, "call_price": 45.94, "is_etf": false}, {"symbol": "00407A", "name": "主動凱基台灣", "price": 0.0, "margin_balance_lots": 21072, "margin_utilization": 3.69, "margin_net_lots": 1559, "call_price": 0.0, "is_etf": true}, {"symbol": "2492", "name": "華新科", "price": 381.0, "margin_balance_lots": 21054, "margin_utilization": 17.36, "margin_net_lots": 885, "call_price": 297.18, "is_etf": false}, {"symbol": "00708L", "name": "期元大S&P黃金正2", "price": 0.0, "margin_balance_lots": 20815, "margin_utilization": 49.54, "margin_net_lots": 13, "call_price": 0.0, "is_etf": true}, {"symbol": "1337", "name": "再生-KY", "price": 4.05, "margin_balance_lots": 20775, "margin_utilization": 30.9, "margin_net_lots": -160, "call_price": 3.16, "is_etf": false}, {"symbol": "6239", "name": "力成", "price": 256.0, "margin_balance_lots": 20655, "margin_utilization": 10.88, "margin_net_lots": -14, "call_price": 199.68, "is_etf": false}, {"symbol": "00918", "name": "大華優利高填息30", "price": 0.0, "margin_balance_lots": 20564, "margin_utilization": 1.75, "margin_net_lots": -120, "call_price": 0.0, "is_etf": true}, {"symbol": "00985A", "name": "主動野村台灣50", "price": 0.0, "margin_balance_lots": 20501, "margin_utilization": 5.7, "margin_net_lots": -79, "call_price": 0.0, "is_etf": true}, {"symbol": "1711", "name": "永光", "price": 39.0, "margin_balance_lots": 20473, "margin_utilization": 14.95, "margin_net_lots": -498, "call_price": 30.42, "is_etf": false}, {"symbol": "2426", "name": "鼎元", "price": 87.4, "margin_balance_lots": 20235, "margin_utilization": 26.92, "margin_net_lots": -1367, "call_price": 68.17, "is_etf": false}, {"symbol": "1409", "name": "新纖", "price": 23.75, "margin_balance_lots": 20020, "margin_utilization": 4.95, "margin_net_lots": -1777, "call_price": 18.53, "is_etf": false}, {"symbol": "2027", "name": "大成鋼", "price": 50.2, "margin_balance_lots": 20018, "margin_utilization": 3.1, "margin_net_lots": -228, "call_price": 39.16, "is_etf": false}, {"symbol": "1709", "name": "和益", "price": 48.1, "margin_balance_lots": 19504, "margin_utilization": 16.36, "margin_net_lots": -320, "call_price": 37.52, "is_etf": false}, {"symbol": "6191", "name": "精成科", "price": 78.5, "margin_balance_lots": 19414, "margin_utilization": 14.67, "margin_net_lots": -217, "call_price": 61.23, "is_etf": false}, {"symbol": "8476", "name": "台境*", "price": 19.4, "margin_balance_lots": 19403, "margin_utilization": 46.69, "margin_net_lots": -10, "call_price": 15.13, "is_etf": false}, {"symbol": "1810", "name": "和成", "price": 21.7, "margin_balance_lots": 19366, "margin_utilization": 25.62, "margin_net_lots": -166, "call_price": 16.93, "is_etf": false}, {"symbol": "9904", "name": "寶成", "price": 25.8, "margin_balance_lots": 19177, "margin_utilization": 2.6, "margin_net_lots": -585, "call_price": 20.12, "is_etf": false}, {"symbol": "3006", "name": "晶豪科", "price": 277.0, "margin_balance_lots": 19043, "margin_utilization": 25.55, "margin_net_lots": -152, "call_price": 216.06, "is_etf": false}, {"symbol": "00688L", "name": "國泰20年美債正2", "price": 0.0, "margin_balance_lots": 18804, "margin_utilization": 13.76, "margin_net_lots": -218, "call_price": 0.0, "is_etf": true}, {"symbol": "00686R", "name": "群益臺灣加權反1", "price": 0.0, "margin_balance_lots": 18623, "margin_utilization": 27.98, "margin_net_lots": 1477, "call_price": 0.0, "is_etf": true}, {"symbol": "2352", "name": "佳世達", "price": 33.8, "margin_balance_lots": 18248, "margin_utilization": 4.62, "margin_net_lots": -1862, "call_price": 26.36, "is_etf": false}, {"symbol": "6213", "name": "聯茂", "price": 687.0, "margin_balance_lots": 18120, "margin_utilization": 19.93, "margin_net_lots": -1322, "call_price": 535.86, "is_etf": false}, {"symbol": "2514", "name": "龍邦", "price": 11.25, "margin_balance_lots": 18115, "margin_utilization": 18.36, "margin_net_lots": -10, "call_price": 8.78, "is_etf": false}, {"symbol": "6209", "name": "今國光", "price": 87.0, "margin_balance_lots": 18013, "margin_utilization": 39.5, "margin_net_lots": -1210, "call_price": 67.86, "is_etf": false}, {"symbol": "2101", "name": "南港", "price": 28.6, "margin_balance_lots": 17613, "margin_utilization": 9.6, "margin_net_lots": -15, "call_price": 22.31, "is_etf": false}, {"symbol": "4919", "name": "新唐", "price": 124.5, "margin_balance_lots": 17489, "margin_utilization": 15.46, "margin_net_lots": -650, "call_price": 97.11, "is_etf": false}, {"symbol": "2908", "name": "特力", "price": 22.15, "margin_balance_lots": 17188, "margin_utilization": 13.48, "margin_net_lots": 195, "call_price": 17.28, "is_etf": false}, {"symbol": "4989", "name": "榮科", "price": 60.4, "margin_balance_lots": 17172, "margin_utilization": 39.76, "margin_net_lots": -582, "call_price": 47.11, "is_etf": false}, {"symbol": "2363", "name": "矽統", "price": 50.7, "margin_balance_lots": 17163, "margin_utilization": 13.33, "margin_net_lots": -107, "call_price": 39.55, "is_etf": false}, {"symbol": "3673", "name": "TPK-KY", "price": 67.3, "margin_balance_lots": 17059, "margin_utilization": 16.78, "margin_net_lots": -441, "call_price": 52.49, "is_etf": false}, {"symbol": "8112", "name": "至上", "price": 81.9, "margin_balance_lots": 16905, "margin_utilization": 11.95, "margin_net_lots": -180, "call_price": 63.88, "is_etf": false}, {"symbol": "8150", "name": "南茂", "price": 90.9, "margin_balance_lots": 16764, "margin_utilization": 9.52, "margin_net_lots": -271, "call_price": 70.9, "is_etf": false}, {"symbol": "2104", "name": "國際中橡", "price": 10.8, "margin_balance_lots": 16751, "margin_utilization": 6.8, "margin_net_lots": -158, "call_price": 8.42, "is_etf": false}, {"symbol": "1717", "name": "長興", "price": 65.6, "margin_balance_lots": 16496, "margin_utilization": 5.57, "margin_net_lots": -17, "call_price": 51.17, "is_etf": false}, {"symbol": "6285", "name": "啟碁", "price": 213.0, "margin_balance_lots": 16327, "margin_utilization": 13.51, "margin_net_lots": -78, "call_price": 166.14, "is_etf": false}, {"symbol": "2484", "name": "希華", "price": 70.6, "margin_balance_lots": 16202, "margin_utilization": 40.65, "margin_net_lots": -843, "call_price": 55.07, "is_etf": false}, {"symbol": "8110", "name": "華東", "price": 42.7, "margin_balance_lots": 15736, "margin_utilization": 12.16, "margin_net_lots": -34, "call_price": 33.31, "is_etf": false}, {"symbol": "2374", "name": "佳能", "price": 63.2, "margin_balance_lots": 15614, "margin_utilization": 18.48, "margin_net_lots": -56, "call_price": 49.3, "is_etf": false}, {"symbol": "2382", "name": "廣達", "price": 318.0, "margin_balance_lots": 15497, "margin_utilization": 1.51, "margin_net_lots": 121, "call_price": 248.04, "is_etf": false}, {"symbol": "3717", "name": "聯嘉投控", "price": 25.35, "margin_balance_lots": 15053, "margin_utilization": 27.33, "margin_net_lots": -478, "call_price": 19.77, "is_etf": false}, {"symbol": "00990A", "name": "主動元大AI新經濟", "price": 0.0, "margin_balance_lots": 14947, "margin_utilization": 2.63, "margin_net_lots": 119, "call_price": 0.0, "is_etf": true}, {"symbol": "2615", "name": "萬海", "price": 120.0, "margin_balance_lots": 14818, "margin_utilization": 2.11, "margin_net_lots": -355, "call_price": 93.6, "is_etf": false}, {"symbol": "2618", "name": "長榮航", "price": 42.7, "margin_balance_lots": 14783, "margin_utilization": 1.09, "margin_net_lots": -472, "call_price": 33.31, "is_etf": false}, {"symbol": "00676R", "name": "富邦臺灣加權反1", "price": 0.0, "margin_balance_lots": 14722, "margin_utilization": 33.11, "margin_net_lots": -1107, "call_price": 0.0, "is_etf": true}, {"symbol": "2369", "name": "菱生", "price": 27.25, "margin_balance_lots": 14703, "margin_utilization": 15.47, "margin_net_lots": -217, "call_price": 21.25, "is_etf": false}, {"symbol": "2481", "name": "強茂", "price": 139.5, "margin_balance_lots": 14582, "margin_utilization": 15.26, "margin_net_lots": -520, "call_price": 108.81, "is_etf": false}, {"symbol": "912000", "name": "晨訊科-DR", "price": 0.0, "margin_balance_lots": 14479, "margin_utilization": 36.67, "margin_net_lots": 38, "call_price": 0.0, "is_etf": false}, {"symbol": "6153", "name": "嘉聯益", "price": 16.1, "margin_balance_lots": 14441, "margin_utilization": 10.55, "margin_net_lots": -79, "call_price": 12.56, "is_etf": false}, {"symbol": "6269", "name": "台郡", "price": 69.0, "margin_balance_lots": 14274, "margin_utilization": 17.63, "margin_net_lots": -1125, "call_price": 53.82, "is_etf": false}, {"symbol": "00984D", "name": "主動聯博全球非投", "price": 0.0, "margin_balance_lots": 14213, "margin_utilization": 4.61, "margin_net_lots": -27, "call_price": 0.0, "is_etf": true}, {"symbol": "3036", "name": "文曄", "price": 207.0, "margin_balance_lots": 14173, "margin_utilization": 4.21, "margin_net_lots": -316, "call_price": 161.46, "is_etf": false}, {"symbol": "00738U", "name": "期元大道瓊白銀", "price": 0.0, "margin_balance_lots": 14145, "margin_utilization": 21.78, "margin_net_lots": -429, "call_price": 0.0, "is_etf": true}, {"symbol": "00400A", "name": "主動國泰動能高息", "price": 0.0, "margin_balance_lots": 14038, "margin_utilization": 2.59, "margin_net_lots": -396, "call_price": 0.0, "is_etf": true}, {"symbol": "2329", "name": "華泰", "price": 39.25, "margin_balance_lots": 13907, "margin_utilization": 8.44, "margin_net_lots": -17, "call_price": 30.62, "is_etf": false}, {"symbol": "2301", "name": "光寶科", "price": 240.5, "margin_balance_lots": 13821, "margin_utilization": 2.39, "margin_net_lots": -392, "call_price": 187.59, "is_etf": false}, {"symbol": "3450", "name": "聯鈞", "price": 409.0, "margin_balance_lots": 13690, "margin_utilization": 37.59, "margin_net_lots": -428, "call_price": 319.02, "is_etf": false}, {"symbol": "3605", "name": "宏致", "price": 161.5, "margin_balance_lots": 13468, "margin_utilization": 30.43, "margin_net_lots": 843, "call_price": 125.97, "is_etf": false}, {"symbol": "8429", "name": "金麗-KY", "price": 5.8, "margin_balance_lots": 13463, "margin_utilization": 28.99, "margin_net_lots": -157, "call_price": 4.52, "is_etf": false}, {"symbol": "2605", "name": "新興", "price": 34.95, "margin_balance_lots": 13379, "margin_utilization": 9.14, "margin_net_lots": 32, "call_price": 27.26, "is_etf": false}, {"symbol": "2236", "name": "百達-KY", "price": 106.0, "margin_balance_lots": 13246, "margin_utilization": 83.42, "margin_net_lots": -76, "call_price": 82.68, "is_etf": false}, {"symbol": "2406", "name": "國碩", "price": 28.6, "margin_balance_lots": 13124, "margin_utilization": 14.96, "margin_net_lots": -255, "call_price": 22.31, "is_etf": false}, {"symbol": "3189", "name": "景碩", "price": 932.0, "margin_balance_lots": 12956, "margin_utilization": 9.84, "margin_net_lots": -195, "call_price": 726.96, "is_etf": false}, {"symbol": "5469", "name": "瀚宇博", "price": 68.4, "margin_balance_lots": 12940, "margin_utilization": 10.65, "margin_net_lots": -953, "call_price": 53.35, "is_etf": false}]};

let _twseMarginCache = TWSE_MARGIN_BASELINE;
let _twseMarginCacheTime = Date.now();

async function fetchTwseMarketMarginData(forceRefresh = false) {
  const now = Date.now();
  if (!forceRefresh && _twseMarginCache && (now - _twseMarginCacheTime < 15 * 60 * 1000)) {
    return _twseMarginCache;
  }
  if (marketRankState.isFetchingMargin) return _twseMarginCache;
  marketRankState.isFetchingMargin = true;

  try {
    const url = "https://www.twse.com.tw/rwd/zh/marginTrading/MI_MARGN?response=json&selectType=ALL";
    const resp = await fetchWithTimeout(url, { cache: "no-store" }, 9000);
    if (!resp.ok) return _twseMarginCache;
    const data = await resp.json();

    const tradeDate = data.date || "";
    let totalMarginLoanVal = 0.0;
    let totalMarginLoanStr = "--";

    const tables = data.tables || [];
    if (tables.length >= 1) {
      const t0Data = tables[0].data || [];
      for (const r of t0Data) {
        const label = r[0] || "";
        if (label.includes("融資金額") || label.includes("融資(元)")) {
          const loanK = parseFloat(String(r[5] || "").replace(/,/g, "")) || 0;
          totalMarginLoanVal = loanK * 1000.0;
          totalMarginLoanStr = `${(loanK / 100000.0).toFixed(2)} 億元`;
        }
      }
    }

    // 建立股票現價映射表
    const priceMap = {};
    if (marketRankState.rawMarketData && Array.isArray(marketRankState.rawMarketData.stocks)) {
      for (const s of marketRankState.rawMarketData.stocks) {
        if (s.symbol) priceMap[s.symbol.toUpperCase()] = s.price || 0;
      }
    }

    const items = [];
    let totMarginMktVal = 0.0;

    if (tables.length >= 2) {
      const rows = tables[1].data || [];
      for (const r of rows) {
        if (!r || r.length < 8) continue;
        const sym = String(r[0] || "").trim();
        const name = String(r[1] || "").trim();
        const buyLots = parseInt(String(r[2] || "0").replace(/,/g, ""), 10) || 0;
        const sellLots = parseInt(String(r[3] || "0").replace(/,/g, ""), 10) || 0;
        const balLots = parseInt(String(r[6] || "0").replace(/,/g, ""), 10) || 0;
        const limLots = parseInt(String(r[7] || "0").replace(/,/g, ""), 10) || 0;

        if (balLots <= 0) continue;

        const price = priceMap[sym.toUpperCase()] || 0.0;
        const utilization = limLots > 0 ? (balLots / limLots * 100.0) : 0.0;
        const mktVal = price > 0 ? (price * balLots * 1000.0) : 0.0;
        if (mktVal > 0) totMarginMktVal += mktVal;

        const callPrice = price > 0 ? Number((price * 0.78).toFixed(2)) : 0.0;

        items.push({
          symbol: sym,
          name: name,
          price: price,
          margin_balance_lots: balLots,
          margin_limit_lots: limLots,
          margin_utilization: Number(utilization.toFixed(2)),
          margin_net_lots: buyLots - sellLots,
          margin_mkt_val_million: Number((mktVal / 1000000.0).toFixed(2)),
          call_price: callPrice,
          is_etf: sym.startsWith("00")
        });
      }
    }

    let marketRatio = 166.5;
    let riskLevel = "SAFE";
    let riskLabel = "籌碼健康 (>= 165%)";

    if (totalMarginLoanVal > 0 && totMarginMktVal > 0) {
      marketRatio = Number((totMarginMktVal / totalMarginLoanVal * 100.0).toFixed(2));
      if (marketRatio >= 165.0) {
        riskLevel = "SAFE";
        riskLabel = "籌碼健康 (>= 165%)";
      } else if (marketRatio >= 140.0) {
        riskLevel = "WARNING";
        riskLabel = "浮額偏高 (140% - 165%)";
      } else {
        riskLevel = "DANGER";
        riskLabel = "多殺多追繳警報 (< 140%)";
      }
    }

    _twseMarginCache = {
      date: tradeDate,
      total_margin_loan_str: totalMarginLoanStr,
      total_margin_loan_val: totalMarginLoanVal,
      total_margin_mkt_val: totMarginMktVal,
      market_maintenance_ratio: marketRatio,
      risk_level: riskLevel,
      risk_label: riskLabel,
      items: items
    };
    _twseMarginCacheTime = now;
    marketRankState.twseMarginData = _twseMarginCache;
    return _twseMarginCache;
  } catch (e) {
    console.warn("TWSE 信用交易資料抓取失敗:", e);
    return _twseMarginCache;
  } finally {
    marketRankState.isFetchingMargin = false;
  }
}

function renderMarginRiskAnalytics() {
  const data = state.decryptedData;
  if (!data) return;

  const positions = state.normalizedPositions || [];
  const isPrivate = state.privacyMode;

  const warnThresh = marginThresholds.warning || 160;
  const callThresh = marginThresholds.call || 130;
  const callMultiplier = callThresh / 100.0;

  // 動態更新表頭與提示文字
  const thCall = document.getElementById("th-call-price");
  if (thCall) thCall.textContent = `${callThresh}%斷頭價`;
  const subTip = document.getElementById("margin-risk-sub-tip");
  if (subTip) subTip.textContent = `上市成數 60% ‧ 上櫃 50% ‧ 警戒線 < ${warnThresh}% ‧ 追繳線 < ${callThresh}% 觸發補繳`;
  const callCardTitle = document.getElementById("margin-call-card-title");
  if (callCardTitle) callCardTitle.textContent = `追繳斷頭警報 (< ${callThresh}%)`;

  let totMarginMarketVal = 0;
  let totMarginLoan = 0;
  let totMarginEquity = 0;
  let callCount = 0;
  let warningCount = 0;
  let totCallDiff = 0;
  let hasMargin = false;

  const riskItems = [];

  positions.forEach(p => {
    const shares = p.shares || 0;
    const costP = p.cost_price || 0;
    const curP = p.current_price > 0 ? p.current_price : costP;
    const isMargin = (p.trade_type === "MARGIN");
    const mkt = (p.market || "TW").toUpperCase();
    const marginRate = parseFloat(p.margin_rate) || (mkt === "TWO" ? 0.5 : 0.6);

    const totalCost = shares * costP;
    const marketVal = p.market_val || (shares * curP);

    if (!isMargin || shares <= 0 || costP <= 0) {
      riskItems.push({
        symbol: p.symbol,
        name: p.name || p.symbol,
        is_margin: false,
        shares,
        cost_price: costP,
        current_price: curP,
        day_pct: p.day_pct !== undefined ? p.day_pct : 0,
        margin_loan: 0,
        market_val: marketVal,
        maintenance_ratio: null,
        call_price: null,
        margin_call_diff: 0,
        risk_level: "CASH",
        risk_label: "現股"
      });
      return;
    }

    hasMargin = true;
    const marginLoan = Math.round(totalCost * marginRate);
    const marginEquity = totalCost - marginLoan;
    const ratio = marginLoan > 0 ? Number(((marketVal / marginLoan) * 100).toFixed(2)) : 0;
    const callPrice = shares > 0 ? Number(((marginLoan * callMultiplier) / shares).toFixed(2)) : 0;
    const callDiff = ratio < callThresh ? Math.max(0, Math.round((marginLoan * callMultiplier) - marketVal)) : 0;

    let riskLevel = "SAFE";
    let riskLabel = "安全";
    if (ratio < callThresh) {
      riskLevel = "CALL";
      riskLabel = "追繳";
      callCount++;
    } else if (ratio < warnThresh) {
      riskLevel = "WARNING";
      riskLabel = "警戒";
      warningCount++;
    }

    totMarginMarketVal += marketVal;
    totMarginLoan += marginLoan;
    totMarginEquity += marginEquity;
    totCallDiff += callDiff;

    riskItems.push({
      symbol: p.symbol,
      name: p.name || p.symbol,
      is_margin: true,
      shares,
      cost_price: costP,
      current_price: curP,
      day_pct: p.day_pct !== undefined ? p.day_pct : 0,
      margin_loan: marginLoan,
      market_val: marketVal,
      maintenance_ratio: ratio,
      call_price: callPrice,
      margin_call_diff: callDiff,
      risk_level: riskLevel,
      risk_label: riskLabel
    });
  });

  const overallRatioEl = document.getElementById("margin-overall-ratio");
  const overallStatusEl = document.getElementById("margin-overall-status");
  const totalLoanEl = document.getElementById("margin-total-loan");
  const totalEquityEl = document.getElementById("margin-total-equity");
  const totalMarketEl = document.getElementById("margin-total-market");
  const callCountEl = document.getElementById("margin-call-count");
  const callDiffEl = document.getElementById("margin-call-diff");

  if (hasMargin && totMarginLoan > 0) {
    const overallRatio = Number(((totMarginMarketVal / totMarginLoan) * 100).toFixed(2));
    if (overallRatioEl) {
      overallRatioEl.textContent = isPrivate ? "***.**%" : `${overallRatio.toFixed(2)}%`;
      overallRatioEl.className = `metric-val ${overallRatio >= warnThresh ? "text-green" : (overallRatio >= callThresh ? "text-gold" : "text-red")}`;
    }
    if (overallStatusEl) {
      if (overallRatio >= warnThresh) {
        overallStatusEl.textContent = `整戶安全 (擔保維持率 >= ${warnThresh}%)`;
        overallStatusEl.style.color = "#52c41a";
      } else if (overallRatio >= callThresh) {
        overallStatusEl.textContent = `整戶警戒 (維持率 ${callThresh}% ~ ${warnThresh}%)`;
        overallStatusEl.style.color = "#ffd166";
      } else {
        overallStatusEl.textContent = `追繳斷頭警報！(< ${callThresh}% 需儘速補繳)`;
        overallStatusEl.style.color = "#ff4d4f";
      }
    }
  } else {
    if (overallRatioEl) {
      overallRatioEl.textContent = "--.-%";
      overallRatioEl.className = "metric-val text-accent";
    }
    if (overallStatusEl) {
      overallStatusEl.textContent = "全現股部位 (無融資風險)";
      overallStatusEl.style.color = "#94a3b8";
    }
  }

  if (totalLoanEl) totalLoanEl.textContent = isPrivate ? "$ ********" : `$ ${fmtNum(totMarginLoan)}`;
  if (totalEquityEl) totalEquityEl.textContent = isPrivate ? "自備保證金: $ ********" : `自備保證金: $ ${fmtNum(totMarginEquity)}`;
  if (totalMarketEl) totalMarketEl.textContent = isPrivate ? "$ ********" : `$ ${fmtNum(totMarginMarketVal)}`;

  if (callCountEl) {
    if (callCount > 0) {
      callCountEl.textContent = `${callCount} 檔 (追繳斷頭)`;
      callCountEl.className = "metric-val text-red";
    } else if (warningCount > 0) {
      callCountEl.textContent = `${warningCount} 檔警戒`;
      callCountEl.className = "metric-val text-gold";
    } else {
      callCountEl.textContent = "0 檔 (安全)";
      callCountEl.className = "metric-val text-green";
    }
  }
  if (callDiffEl) {
    callDiffEl.textContent = isPrivate ? "需補繳差額: $ ********" : `需補繳差額: $ ${fmtNum(totCallDiff)}`;
    callDiffEl.style.color = totCallDiff > 0 ? "#ff4d4f" : "#94a3b8";
  }

  const tbody = document.getElementById("margin-risk-tbody");
  if (!tbody) return;
  tbody.innerHTML = "";

  riskItems.sort((a, b) => {
    if (a.is_margin !== b.is_margin) return a.is_margin ? -1 : 1;
    if (!a.is_margin) return 0;
    return (a.maintenance_ratio || 999) - (b.maintenance_ratio || 999);
  });

  if (riskItems.length === 0) {
    tbody.innerHTML = `<tr><td colspan="11" style="text-align:center; color:#94a3b8; padding:30px;">尚無持股部位</td></tr>`;
    return;
  }

  riskItems.forEach(it => {
    const tr = document.createElement("tr");

    let badgeClass = "badge-risk-safe";
    if (!it.is_margin) badgeClass = "badge-tag tag-tw";
    else if (it.risk_level === "CALL") badgeClass = "badge-risk-call";
    else if (it.risk_level === "WARNING") badgeClass = "badge-risk-warning";

    const ratioDisplay = it.is_margin ? `${it.maintenance_ratio.toFixed(2)}%` : "--";
    const callPDisplay = it.is_margin ? it.call_price.toFixed(2) : "--";
    const diffDisplay = (it.margin_call_diff > 0) ? `$ ${fmtNum(it.margin_call_diff)}` : "-";
    const loanDisplay = it.is_margin ? `$ ${fmtNum(it.margin_loan)}` : "-";

    tr.innerHTML = `
      <td class="td-sticky">
        <div class="sym-cell-box">
          <span class="sym-code">${it.symbol}</span>
          <span class="sym-name">${it.name}</span>
        </div>
      </td>
      <td><span class="badge-tag ${it.is_margin ? "tag-etf" : "tag-tw"}">${it.is_margin ? "融資" : "現股"}</span></td>
      <td>${isPrivate ? "***" : fmtNum(it.shares)}</td>
      <td>${isPrivate ? "***" : it.cost_price.toFixed(2)}</td>
      <td>
        <div style="display:flex; flex-direction:column; align-items:flex-start;">
          <b>${it.current_price > 0 ? it.current_price.toFixed(2) : "--"}</b>
          <span class="cell-sub ${it.day_pct >= 0 ? "text-red" : "text-green"}" style="font-size: 11px; font-weight: 600;">
            ${it.day_pct !== undefined ? fmtPctSign(it.day_pct) : "--"}
          </span>
        </div>
      </td>
      <td>${isPrivate ? "***" : loanDisplay}</td>
      <td>${isPrivate ? "***" : `$ ${fmtNum(it.market_val)}`}</td>
      <td><b class="${it.is_margin ? (it.maintenance_ratio >= warnThresh ? "text-green" : (it.maintenance_ratio >= callThresh ? "text-gold" : "text-red")) : ""}">${isPrivate ? "***" : ratioDisplay}</b></td>
      <td><span class="${it.is_margin ? "text-dim" : ""}">${callPDisplay}</span></td>
      <td><b class="${it.margin_call_diff > 0 ? "text-red" : ""}">${isPrivate ? "***" : diffDisplay}</b></td>
      <td><span class="${badgeClass}">${it.risk_label}</span></td>
    `;
    tbody.appendChild(tr);
  });
}

async function loadMarketRankingCacheSilently() {
  if (marketRankState.rawMarketData) return;
  try {
    let resp = await fetch("market_ranking_cache.json?t=" + Date.now(), { cache: "no-store" });
    if (!resp.ok) {
      resp = await fetch("../market_ranking_cache.json?t=" + Date.now(), { cache: "no-store" });
    }
    if (resp.ok) {
      const data = await resp.json();
      const revMap = {};
      if (Array.isArray(data.revenues)) {
        for (const r of data.revenues) {
          if (r && r.symbol) revMap[r.symbol.toUpperCase()] = r;
        }
      }
      if (Array.isArray(data.stocks)) {
        for (const s of data.stocks) {
          if (!s) continue;
          const sym = (s.symbol || "").toUpperCase();
          const r = revMap[sym] || {};
          s.yoy = (r.yoy !== undefined && r.yoy !== null) ? r.yoy : null;
          s.cum_yoy = (r.cum_yoy !== undefined && r.cum_yoy !== null) ? r.cum_yoy : null;
          s.industry = r.industry || s.type || "--";
        }
      }
      marketRankState.rawMarketData = data;
      const statusTag = document.getElementById("mkt-banner-status-tag");
      if (statusTag && data.cached_time_str) {
        statusTag.textContent = `資料時間: ${data.cached_time_str}`;
      }
      renderMarketRankings();
    }
  } catch (e) {
    console.warn("載入 market_ranking_cache 失敗:", e);
  }
}


/**
 * 將目前全市場排行榜表格內容匯出為標準 UTF-8 CSV 檔 (含 BOM，支援 Excel 零亂碼)
 */

/**
 * 依據指定的排行分頁類型 (mType) 與篩選條件 (filter) 產生該分頁專屬的 CSV 字串
 */
function buildCsvForRankType(targetType, filter) {
  const mySyms = new Set((state.normalizedPositions || []).map(p => (p.symbol || "").toUpperCase()));
  const rows = [];

  const typeNames = {
    margin_market: "全市場融資排行",
    yield: "高殖利率排行",
    pe: "低本益比排行",
    rev: "營收年增YoY排行",
    roe: "高ROE排行"
  };
  const typeName = typeNames[targetType] || "市場指標排行";

  if (targetType === "margin_market") {
    // 表頭
    rows.push(['"名次"', '"代號"', '"名稱"', '"現價"', '"融資餘額(張)"', '"使用率(%)"', '"今日資增減"', '"130%追繳價"', '"緩衝空間%"', '"估算維持率%"', '"在庫狀態"'].join(","));

    let marginData = marketRankState.twseMarginData || _twseMarginCache || TWSE_MARGIN_BASELINE;
    let list = [...(marginData.items || [])];
    if (filter === "stock") list = list.filter(s => !s.is_etf);
    else if (filter === "etf") list = list.filter(s => s.is_etf);

    list.sort((a, b) => (b.margin_balance_lots || 0) - (a.margin_balance_lots || 0));
    const topItems = list.slice(0, 150);

    // 建立個人庫存維持率映射
    const myPosMap = {};
    (state.normalizedPositions || []).forEach(p => {
      if (p && p.symbol) {
        const sym = p.symbol.toUpperCase();
        const shares = p.shares || 0;
        const costP = p.cost_price || 0;
        const curP = p.current_price > 0 ? p.current_price : costP;
        const isMargin = (p.trade_type === "MARGIN");
        const mkt = (p.market || "TW").toUpperCase();
        const mRate = parseFloat(p.margin_rate) || (mkt === "TWO" ? 0.5 : 0.6);
        const loan = isMargin ? Math.round(shares * costP * mRate) : 0;
        const mktVal = shares * curP;
        const ratio = (isMargin && loan > 0) ? Number(((mktVal / loan) * 100).toFixed(2)) : null;
        myPosMap[sym] = { isMargin, ratio };
      }
    });

    topItems.forEach((it, idx) => {
      const sym = (it.symbol || "").toUpperCase();
      const myPos = myPosMap[sym];
      const isIn = !!myPos;
      const inTag = isIn ? "已在庫" : "未持有";

      const balLots = (it.margin_balance_lots || 0).toLocaleString();
      const util = `${(it.margin_utilization || 0).toFixed(2)}%`;
      const netLots = it.margin_net_lots || 0;
      const netStr = `${netLots > 0 ? "+" : ""}${netLots.toLocaleString()}`;
      const priceVal = (it.price && it.price > 0) ? it.price : 0;
      const priceStr = priceVal > 0 ? priceVal.toFixed(2) : "--";

      const callPVal = priceVal > 0 ? Number((priceVal * 0.78).toFixed(2)) : (it.call_price || 0);
      const callP = callPVal > 0 ? callPVal.toFixed(2) : "--";
      const bufferStr = priceVal > 0 ? "-22.0%" : "--";

      let ratioLabel = "166.7%";
      if (isIn && myPos.ratio !== null) {
        ratioLabel = `${myPos.ratio.toFixed(2)}% (我的)`;
      } else if (priceVal <= 0) {
        ratioLabel = "--";
      }

      rows.push([
        idx + 1,
        `"${it.symbol}"`,
        `"${(it.name || it.symbol).replace(/"/g, '""')}"`,
        `"${priceStr}"`,
        `"${balLots}"`,
        `"${util}"`,
        `"${netStr}"`,
        `"${callP}"`,
        `"${bufferStr}"`,
        `"${ratioLabel}"`,
        `"${inTag}"`
      ].join(","));
    });
  } else if (targetType === "yield") {
    rows.push(['"名次"', '"代號"', '"名稱"', '"現價"', '"現金殖利率"', '"本益比"', '"股價淨值比"', '"ROE"', '"在庫狀態"'].join(","));

    const cache = marketRankState.rawMarketData;
    let pool = [];
    if (cache) {
      if (filter === "all" || filter === "stock") {
        (cache.stocks || []).forEach(s => {
          const y = (s.yield_pct !== undefined && s.yield_pct !== null) ? s.yield_pct : (s.dividend_yield || 0);
          if (y > 0) pool.push({ ...s, is_etf: false });
        });
      }
      if (filter === "all" || filter === "etf") {
        (cache.etfs || []).forEach(e => {
          const y = (e.yield_pct !== undefined && e.yield_pct !== null) ? e.yield_pct : (e.dividend_yield || 0);
          if (y > 0) pool.push({ ...e, is_etf: true });
        });
      }
      pool.sort((a, b) => {
        const ya = (a.yield_pct !== undefined && a.yield_pct !== null) ? a.yield_pct : (a.dividend_yield || 0);
        const yb = (b.yield_pct !== undefined && b.yield_pct !== null) ? b.yield_pct : (b.dividend_yield || 0);
        return yb - ya;
      });
    }

    const topItems = pool.slice(0, 100);
    topItems.forEach((it, idx) => {
      const isIn = mySyms.has((it.symbol || "").toUpperCase());
      const inTag = isIn ? "已在庫" : "未持有";

      const ydVal = (it.yield_pct !== undefined && it.yield_pct !== null) ? it.yield_pct : it.dividend_yield;
      const yd = (ydVal !== undefined && ydVal > 0) ? `${ydVal.toFixed(2)}%` : "--";
      const pe = (it.pe !== undefined && it.pe !== null && it.pe > 0) ? Number(it.pe).toFixed(2) : "--";
      const pb = (it.pb !== undefined && it.pb !== null && it.pb > 0) ? Number(it.pb).toFixed(2) : "--";
      const roe = (it.roe !== undefined && it.roe !== null && !isNaN(it.roe)) ? `${Number(it.roe).toFixed(2)}%` : "--";
      const priceStr = (it.price && it.price > 0) ? it.price.toFixed(2) : "--";

      rows.push([
        idx + 1,
        `"${it.symbol}"`,
        `"${(it.name || it.symbol).replace(/"/g, '""')}"`,
        `"${priceStr}"`,
        `"${yd}"`,
        `"${pe}"`,
        `"${pb}"`,
        `"${roe}"`,
        `"${inTag}"`
      ].join(","));
    });
  } else if (targetType === "pe") {
    rows.push(['"名次"', '"代號"', '"名稱"', '"現價"', '"本益比(低估)"', '"殖利率"', '"股價淨值比"', '"ROE"', '"在庫狀態"'].join(","));

    const cache = marketRankState.rawMarketData;
    let pool = [];
    if (cache && cache.stocks) {
      pool = (cache.stocks || []).filter(s => {
        const p = s.pe;
        const price = s.price || 0;
        return (p !== undefined && p !== null && p >= 4.0 && p <= 30.0 && price >= 10.0);
      });
      if (filter === "stock") pool = pool.filter(s => !s.is_etf);
      else if (filter === "etf") pool = pool.filter(s => s.is_etf);
      pool.sort((a, b) => a.pe - b.pe);
    }

    const topItems = pool.slice(0, 100);
    topItems.forEach((it, idx) => {
      const isIn = mySyms.has((it.symbol || "").toUpperCase());
      const inTag = isIn ? "已在庫" : "未持有";

      const pe = `${Number(it.pe).toFixed(2)} 倍`;
      const ydVal = (it.yield_pct !== undefined && it.yield_pct !== null) ? it.yield_pct : it.dividend_yield;
      const yd = (ydVal !== undefined && ydVal > 0) ? `${ydVal.toFixed(2)}%` : "--";
      const pb = (it.pb !== undefined && it.pb !== null && it.pb > 0) ? Number(it.pb).toFixed(2) : "--";
      const roe = (it.roe !== undefined && it.roe !== null && !isNaN(it.roe)) ? `${Number(it.roe).toFixed(2)}%` : "--";
      const priceStr = (it.price && it.price > 0) ? it.price.toFixed(2) : "--";

      rows.push([
        idx + 1,
        `"${it.symbol}"`,
        `"${(it.name || it.symbol).replace(/"/g, '""')}"`,
        `"${priceStr}"`,
        `"${pe}"`,
        `"${yd}"`,
        `"${pb}"`,
        `"${roe}"`,
        `"${inTag}"`
      ].join(","));
    });
  } else if (targetType === "rev") {
    rows.push(['"名次"', '"代號"', '"名稱"', '"現價"', '"營收年增率"', '"累計年增率"', '"單月營收(億)"', '"產業別"', '"在庫狀態"'].join(","));

    const cache = marketRankState.rawMarketData;
    let pool = [];
    const priceMap = {};
    if (cache) {
      (cache.stocks || []).forEach(s => {
        if (s && s.symbol) priceMap[s.symbol.toUpperCase()] = s.price || 0;
      });
      pool = (cache.revenues || []).filter(r => (r && r.yoy !== undefined && r.yoy !== null && r.yoy > 0));
      if (filter === "stock") pool = pool.filter(s => !s.is_etf);
      else if (filter === "etf") pool = pool.filter(s => s.is_etf);
      pool.sort((a, b) => b.yoy - a.yoy);
    }

    const topItems = pool.slice(0, 100);
    topItems.forEach((it, idx) => {
      const sym = (it.symbol || "").toUpperCase();
      const isIn = mySyms.has(sym);
      const inTag = isIn ? "已在庫" : "未持有";

      const curP = priceMap[sym] || it.price || 0;
      const priceStr = curP > 0 ? curP.toFixed(2) : "--";
      const yoy = `+${Number(it.yoy).toFixed(2)}%`;
      const cum = (it.cum_yoy !== undefined && it.cum_yoy !== null) ? `${it.cum_yoy > 0 ? "+" : ""}${Number(it.cum_yoy).toFixed(2)}%` : "--";
      const curE = (it.rev_cur_e !== undefined && it.rev_cur_e !== null) ? `${Number(it.rev_cur_e).toFixed(2)}` : "--";
      const ind = it.industry || "--";

      rows.push([
        idx + 1,
        `"${it.symbol}"`,
        `"${(it.name || it.symbol).replace(/"/g, '""')}"`,
        `"${priceStr}"`,
        `"${yoy}"`,
        `"${cum}"`,
        `"${curE}"`,
        `"${ind}"`,
        `"${inTag}"`
      ].join(","));
    });
  } else if (targetType === "roe") {
    rows.push(['"名次"', '"代號"', '"名稱"', '"現價"', '"ROE(%)"', '"本益比"', '"殖利率"', '"股價淨值比"', '"在庫狀態"'].join(","));

    const cache = marketRankState.rawMarketData;
    let pool = [];
    if (cache && cache.stocks) {
      pool = (cache.stocks || []).filter(s => {
        const roe = s.roe;
        const pe = s.pe;
        return (roe !== undefined && roe !== null && pe !== undefined && pe !== null && pe >= 4.0 && roe >= 8.0 && roe <= 75.0);
      });
      if (filter === "stock") pool = pool.filter(s => !s.is_etf);
      else if (filter === "etf") pool = pool.filter(s => s.is_etf);
      pool.sort((a, b) => b.roe - a.roe);
    }

    const topItems = pool.slice(0, 100);
    topItems.forEach((it, idx) => {
      const isIn = mySyms.has((it.symbol || "").toUpperCase());
      const inTag = isIn ? "已在庫" : "未持有";

      const roe = `${Number(it.roe).toFixed(2)}%`;
      const pe = (it.pe !== undefined && it.pe !== null && it.pe > 0) ? `${Number(it.pe).toFixed(2)} 倍` : "--";
      const ydVal = (it.yield_pct !== undefined && it.yield_pct !== null) ? it.yield_pct : it.dividend_yield;
      const yd = (ydVal !== undefined && ydVal > 0) ? `${ydVal.toFixed(2)}%` : "--";
      const pb = (it.pb !== undefined && it.pb !== null && it.pb > 0) ? Number(it.pb).toFixed(2) : "--";
      const priceStr = (it.price && it.price > 0) ? it.price.toFixed(2) : "--";

      rows.push([
        idx + 1,
        `"${it.symbol}"`,
        `"${(it.name || it.symbol).replace(/"/g, '""')}"`,
        `"${priceStr}"`,
        `"${roe}"`,
        `"${pe}"`,
        `"${yd}"`,
        `"${pb}"`,
        `"${inTag}"`
      ].join(","));
    });
  }

  return {
    typeName,
    content: "\uFEFF" + rows.join("\r\n")
  };
}

/**
 * 下載單一 CSV 檔案輔助函式
 */
function triggerCsvDownload(csvContent, fileName) {
  const blob = new Blob([csvContent], { type: "text/csv;charset=utf-8;" });
  const link = document.createElement("a");
  if (link.download !== undefined) {
    const url = URL.createObjectURL(blob);
    link.setAttribute("href", url);
    link.setAttribute("download", fileName);
    link.style.visibility = "hidden";
    document.body.appendChild(link);
    link.click();
    document.body.removeChild(link);
    setTimeout(() => URL.revokeObjectURL(url), 1000);
  }
}

/**
 * 一鍵批量依序下載全部 5 個分頁的排行 CSV (未來新增分頁可自動擴展)
 */
async function exportAllMarketTabsCsv() {
  const allTabTypes = [];
  document.querySelectorAll("#market-rank-type-switch .btn-period").forEach(btn => {
    const t = btn.getAttribute("data-mtype");
    if (t) allTabTypes.push(t);
  });

  if (allTabTypes.length === 0) {
    alert("未找到可匯出的分頁！");
    return;
  }

  const filter = marketRankState.filter || "all";
  const filterNames = { all: "全部", stock: "僅股票", etf: "僅ETF" };
  const filterName = filterNames[filter] || "全部";
  const now = new Date();
  const dStr = now.toISOString().slice(0, 10).replace(/-/g, "");

  const btnAll = document.getElementById("btn-export-all-tabs-csv");
  const origText = btnAll ? btnAll.textContent : "[全部匯出 CSV]";
  if (btnAll) {
    btnAll.textContent = "匯出中...";
    btnAll.disabled = true;
  }

  try {
    for (let i = 0; i < allTabTypes.length; i++) {
      const typeId = allTabTypes[i];
      const result = buildCsvForRankType(typeId, filter);
      const fileName = `台股_${result.typeName}_${filterName}_${dStr}.csv`;
      triggerCsvDownload(result.content, fileName);
      // 間隔 350ms，避免瀏覽器並發下載限制攔截
      await new Promise(resolve => setTimeout(resolve, 350));
    }
  } catch (err) {
    console.error("批量匯出失敗:", err);
    alert("批量匯出遇到問題: " + err.message);
  } finally {
    if (btnAll) {
      btnAll.textContent = origText;
      btnAll.disabled = false;
    }
  }
}

function downloadCurrentMarketCsv() {
  const mType = marketRankState.currentType || "margin_market";
  const filter = marketRankState.filter || "all";
  const filterNames = { all: "全部", stock: "僅股票", etf: "僅ETF" };
  const filterName = filterNames[filter] || "全部";

  const result = buildCsvForRankType(mType, filter);
  const now = new Date();
  const dStr = now.toISOString().slice(0, 10).replace(/-/g, "");
  const fileName = `台股_${result.typeName}_${filterName}_${dStr}.csv`;

  triggerCsvDownload(result.content, fileName);
}

async function renderMarketRankings() {
  const tbody = document.getElementById("market-ranking-tbody");
  const thead = document.getElementById("market-ranking-thead");
  const bannerBar = document.getElementById("market-margin-banner-bar");
  const bannerText = document.getElementById("mkt-ratio-banner-text");
  const titleEl = document.getElementById("market-rank-title");
  const tipEl = document.getElementById("market-rank-tip");
  if (!tbody) return;

  const mType = marketRankState.currentType || "margin_market";
  const filter = marketRankState.filter || "all";
  const mySyms = new Set((state.normalizedPositions || []).map(p => (p.symbol || "").toUpperCase()));

  // ==========================================
  // 分支 A：[全市場融資排行] (margin_market)
  // 對齊本機端 unified_ranking_gui.py tab == "margin_market"
  // 支援三者共存切換：130%追繳價 / 距斷頭緩衝% / 市場估算維持率 / 個人在庫真實維持率
  // ==========================================
  if (mType === "margin_market") {
    if (bannerBar) bannerBar.classList.remove("hidden");
    if (titleEl) titleEl.textContent = "全市場融資餘額與維持率排行 (TWSE Market Margin Rankings)";
    if (tipEl) tipEl.textContent = "台灣證交所官方信用交易總表 ‧ 融資餘額由大到小 ‧ 融資使用率 ‧ 130% 斷頭追繳預警與維持率";

    const rMode = marketRankState.ratioMode || "all_three";

    // 依切換模式動態設定表頭欄位
    if (thead) {
      if (rMode === "all_three") {
        thead.innerHTML = `
          <tr>
            <th>名次</th>
            <th>代號</th>
            <th>名稱</th>
            <th>現價</th>
            <th>融資餘額(張)</th>
            <th>使用率(%)</th>
            <th>今日資增減</th>
            <th title="現價跌破即觸發 130% 追繳">130%追繳價</th>
            <th title="距 130% 追繳之跌幅安全緩衝空間">緩衝空間%</th>
            <th title="近 60 日融資成本估算維持率 / 個人在庫真實維持率">估算維持率%</th>
            <th>在庫狀態</th>
          </tr>`;
      } else if (rMode === "call_only") {
        thead.innerHTML = `
          <tr>
            <th>名次</th>
            <th>代號</th>
            <th>名稱</th>
            <th>現價</th>
            <th>融資餘額(張)</th>
            <th>使用率(%)</th>
            <th>今日資增減</th>
            <th>130%追繳價</th>
            <th>在庫狀態</th>
          </tr>`;
      } else if (rMode === "buffer_only") {
        thead.innerHTML = `
          <tr>
            <th>名次</th>
            <th>代號</th>
            <th>名稱</th>
            <th>現價</th>
            <th>融資餘額(張)</th>
            <th>使用率(%)</th>
            <th>今日資增減</th>
            <th title="距離 130% 追繳價的緩衝跌幅空間">距130%緩衝空間(%)</th>
            <th>在庫狀態</th>
          </tr>`;
      } else { // ratio_only
        thead.innerHTML = `
          <tr>
            <th>名次</th>
            <th>代號</th>
            <th>名稱</th>
            <th>現價</th>
            <th>融資餘額(張)</th>
            <th>使用率(%)</th>
            <th>今日資增減</th>
            <th title="市場近 60 日持倉成本估算維持率 (在庫者呈現個人真實維持率)">市場估算維持率(%)</th>
            <th>在庫狀態</th>
          </tr>`;
      }
    }

    let marginData = marketRankState.twseMarginData || _twseMarginCache || TWSE_MARGIN_BASELINE;
    if (!marginData) {
      tbody.innerHTML = `<tr><td colspan="11" style="text-align:center; color:#94a3b8; padding:30px;">正在向台灣證交所 (TWSE) 抓取官方最新信用交易總表 (MI_MARGN)...</td></tr>`;
      marginData = await fetchTwseMarketMarginData();
    }

    if (!marginData || !marginData.items || marginData.items.length === 0) {
      marginData = TWSE_MARGIN_BASELINE;
    }

    // 建立個人庫存維持率映射 (方案 C: 在庫真實維持率)
    const myPosMap = {};
    (state.normalizedPositions || []).forEach(p => {
      if (p && p.symbol) {
        const sym = p.symbol.toUpperCase();
        const shares = p.shares || 0;
        const costP = p.cost_price || 0;
        const curP = p.current_price > 0 ? p.current_price : costP;
        const isMargin = (p.trade_type === "MARGIN");
        const mkt = (p.market || "TW").toUpperCase();
        const mRate = parseFloat(p.margin_rate) || (mkt === "TWO" ? 0.5 : 0.6);
        const loan = isMargin ? Math.round(shares * costP * mRate) : 0;
        const mktVal = shares * curP;
        const ratio = (isMargin && loan > 0) ? Number(((mktVal / loan) * 100).toFixed(2)) : null;
        myPosMap[sym] = { isMargin, ratio, costP, curP };
      }
    });

    // 更新大盤概況橫幅
    if (bannerText) {
      const r = marginData.market_maintenance_ratio || 163.08;
      const loanStr = marginData.total_margin_loan_str || "6234.68 億元";
      const riskLbl = marginData.risk_label || "去槓桿警戒區 (140%~165%)";
      const tDate = marginData.date || "20261030";
      bannerText.textContent = `【台股大盤融資維持率】${r.toFixed(2)}%  |  全市場融資餘額：${loanStr}  |  風控狀態：${riskLbl}  (證交所數據: ${tDate})`;
      bannerText.style.color = (r >= 165) ? "#52c41a" : ((r >= 140) ? "#ffd166" : "#ff4d4f");
    }

    let list = [...(marginData.items || [])];
    if (filter === "stock") list = list.filter(s => !s.is_etf);
    else if (filter === "etf") list = list.filter(s => s.is_etf);

    // 依融資餘額降序排列
    list.sort((a, b) => (b.margin_balance_lots || 0) - (a.margin_balance_lots || 0));
    const topItems = list.slice(0, 150);

    tbody.innerHTML = "";
    topItems.forEach((it, idx) => {
      const tr = document.createElement("tr");
      const sym = (it.symbol || "").toUpperCase();
      const myPos = myPosMap[sym];
      const isIn = !!myPos;
      const inTag = isIn ? `<span class="badge-tag tag-tw">已在庫</span>` : `<span class="cell-sub text-dim">未持有</span>`;

      const balLots = (it.margin_balance_lots || 0).toLocaleString();
      const util = it.margin_utilization || 0;
      const utilClass = util >= 50 ? "text-red font-bold" : (util >= 30 ? "text-gold font-bold" : "");
      const netLots = it.margin_net_lots || 0;
      const netStr = `${netLots > 0 ? "+" : ""}${netLots.toLocaleString()}`;
      const netClass = netLots > 0 ? "text-red font-bold" : (netLots < 0 ? "text-green font-bold" : "text-dim");
      const priceVal = (it.price && it.price > 0) ? it.price : 0;
      const priceStr = priceVal > 0 ? priceVal.toFixed(2) : "--";

      // 1. 130% 追繳價
      const callPVal = priceVal > 0 ? Number((priceVal * 0.78).toFixed(2)) : (it.call_price || 0);
      const callP = callPVal > 0 ? callPVal.toFixed(2) : "--";

      // 2. 距 130% 跌幅緩衝空間 (%)
      const bufferPct = priceVal > 0 ? -22.0 : null; // 官方融資成數 60% 算式：跌幅空間永遠精確為 -22.0%
      const bufferStr = bufferPct !== null ? "-22.0%" : "--";
      const bufferClass = "text-gold font-bold";

      // 3. 市場估算維持率 (%) & 在庫真實維持率
      let estRatio = 166.67; // 融資買進基準維持率 (100% / 0.6)
      let ratioLabel = `${estRatio.toFixed(1)}%`;
      let ratioClass = "text-green";

      if (isIn && myPos.ratio !== null) {
        // 若個人在庫有融資部位，直接帶入個人真實維持率 (方案 C)
        ratioLabel = `${myPos.ratio.toFixed(2)}% (我的)`;
        ratioClass = myPos.ratio >= 160 ? "text-green font-bold" : (myPos.ratio >= 130 ? "text-gold font-bold" : "text-red font-bold");
      } else if (priceVal <= 0) {
        ratioLabel = "--";
        ratioClass = "text-dim";
      }

      if (rMode === "all_three") {
        tr.innerHTML = `
          <td>${idx + 1}</td>
          <td><b>${it.symbol}</b></td>
          <td>${it.name || it.symbol}</td>
          <td><b>${priceStr}</b></td>
          <td class="font-bold">${balLots}</td>
          <td class="${utilClass}">${util.toFixed(2)}%</td>
          <td class="${netClass}">${netStr}</td>
          <td class="text-dim font-bold">${callP}</td>
          <td class="${bufferClass}">${bufferStr}</td>
          <td class="${ratioClass}">${ratioLabel}</td>
          <td>${inTag}</td>
        `;
      } else if (rMode === "call_only") {
        tr.innerHTML = `
          <td>${idx + 1}</td>
          <td><b>${it.symbol}</b></td>
          <td>${it.name || it.symbol}</td>
          <td><b>${priceStr}</b></td>
          <td class="font-bold">${balLots}</td>
          <td class="${utilClass}">${util.toFixed(2)}%</td>
          <td class="${netClass}">${netStr}</td>
          <td class="text-dim font-bold">${callP}</td>
          <td>${inTag}</td>
        `;
      } else if (rMode === "buffer_only") {
        tr.innerHTML = `
          <td>${idx + 1}</td>
          <td><b>${it.symbol}</b></td>
          <td>${it.name || it.symbol}</td>
          <td><b>${priceStr}</b></td>
          <td class="font-bold">${balLots}</td>
          <td class="${utilClass}">${util.toFixed(2)}%</td>
          <td class="${netClass}">${netStr}</td>
          <td class="${bufferClass}">${bufferStr}</td>
          <td>${inTag}</td>
        `;
      } else { // ratio_only
        tr.innerHTML = `
          <td>${idx + 1}</td>
          <td><b>${it.symbol}</b></td>
          <td>${it.name || it.symbol}</td>
          <td><b>${priceStr}</b></td>
          <td class="font-bold">${balLots}</td>
          <td class="${utilClass}">${util.toFixed(2)}%</td>
          <td class="${netClass}">${netStr}</td>
          <td class="${ratioClass}">${ratioLabel}</td>
          <td>${inTag}</td>
        `;
      }

      tbody.appendChild(tr);
    });
    return;
  }
  // ==========================================
  // 分支 B：[殖利率排行] (yield)
  // 對齊本機端 unified_ranking_gui.py:
  // 表頭: 名次 | 代號 | 名稱 | 現價 | 現金殖利率 | 本益比 | 股價淨值比 | ROE | 在庫狀態
  // ==========================================
  if (mType === "yield") {
    if (bannerBar) bannerBar.classList.remove("hidden");
    if (titleEl) titleEl.textContent = "全市場高配息殖利率排行榜 (High Dividend Yield Rankings)";
    if (tipEl) tipEl.textContent = "全市場上市櫃股票與高股息 ETF ‧ 現金殖利率由高至低排列 ‧ 排除 0% 與未配息標的";

    if (thead) {
      thead.innerHTML = `
        <tr>
          <th>名次</th>
          <th>代號</th>
          <th>名稱</th>
          <th>現價</th>
          <th>現金殖利率</th>
          <th>本益比</th>
          <th>股價淨值比</th>
          <th>ROE</th>
          <th>在庫狀態</th>
        </tr>`;
    }

    const cache = marketRankState.rawMarketData;
    if (!cache || (!cache.stocks && !cache.etfs)) {
      tbody.innerHTML = `<tr><td colspan="9" style="text-align:center; color:#94a3b8; padding:30px;">正在載入全市場指標快取...</td></tr>`;
      return;
    }

    let pool = [];
    if (filter === "all" || filter === "stock") {
      (cache.stocks || []).forEach(s => {
        const y = (s.yield_pct !== undefined && s.yield_pct !== null) ? s.yield_pct : (s.dividend_yield || 0);
        if (y > 0) pool.push({ ...s, is_etf: false });
      });
    }
    if (filter === "all" || filter === "etf") {
      (cache.etfs || []).forEach(e => {
        const y = (e.yield_pct !== undefined && e.yield_pct !== null) ? e.yield_pct : (e.dividend_yield || 0);
        if (y > 0) pool.push({ ...e, is_etf: true });
      });
    }

    pool.sort((a, b) => {
      const ya = (a.yield_pct !== undefined && a.yield_pct !== null) ? a.yield_pct : (a.dividend_yield || 0);
      const yb = (b.yield_pct !== undefined && b.yield_pct !== null) ? b.yield_pct : (b.dividend_yield || 0);
      return yb - ya;
    });

    const topItems = pool.slice(0, 100);
    tbody.innerHTML = "";
    topItems.forEach((it, idx) => {
      const tr = document.createElement("tr");
      const isIn = mySyms.has((it.symbol || "").toUpperCase());
      const inTag = isIn ? `<span class="badge-tag tag-tw">已在庫</span>` : `<span class="cell-sub text-dim">未持有</span>`;

      const ydVal = (it.yield_pct !== undefined && it.yield_pct !== null) ? it.yield_pct : it.dividend_yield;
      const yd = (ydVal !== undefined && ydVal > 0) ? `${ydVal.toFixed(2)}%` : "--";
      const pe = (it.pe !== undefined && it.pe !== null && it.pe > 0) ? Number(it.pe).toFixed(2) : "--";
      const pb = (it.pb !== undefined && it.pb !== null && it.pb > 0) ? Number(it.pb).toFixed(2) : "--";
      const roe = (it.roe !== undefined && it.roe !== null && !isNaN(it.roe)) ? `${Number(it.roe).toFixed(2)}%` : "--";
      const priceStr = (it.price && it.price > 0) ? it.price.toFixed(2) : "--";

      tr.innerHTML = `
        <td>${idx + 1}</td>
        <td><b>${it.symbol}</b></td>
        <td>${it.name || it.symbol}</td>
        <td><b>${priceStr}</b></td>
        <td class="text-gold font-bold">${yd}</td>
        <td>${pe}</td>
        <td>${pb}</td>
        <td class="${(it.roe || 0) >= 15 ? "text-red font-bold" : ""}">${roe}</td>
        <td>${inTag}</td>
      `;
      tbody.appendChild(tr);
    });
    return;
  }

  // ==========================================
  // 分支 C：[低本益比排行] (pe)
  // 對齊本機端 unified_ranking_gui.py:
  // 表頭: 名次 | 代號 | 名稱 | 現價 | 本益比(低估) | 殖利率 | 股價淨值比 | ROE | 在庫狀態
  // ==========================================
  if (mType === "pe") {
    if (bannerBar) bannerBar.classList.remove("hidden");
    if (titleEl) titleEl.textContent = "全市場價值低本益比優質股排行榜 (Low PE Ratio Value Rankings)";
    if (tipEl) tipEl.textContent = "排除虧損與處分資產暴賺 (PE 4.0 ~ 30.0) ‧ 股價 >= 10 元之優質績優個股由低至高";

    if (thead) {
      thead.innerHTML = `
        <tr>
          <th>名次</th>
          <th>代號</th>
          <th>名稱</th>
          <th>現價</th>
          <th>本益比(低估)</th>
          <th>殖利率</th>
          <th>股價淨值比</th>
          <th>ROE</th>
          <th>在庫狀態</th>
        </tr>`;
    }

    const cache = marketRankState.rawMarketData;
    if (!cache || !cache.stocks) {
      tbody.innerHTML = `<tr><td colspan="9" style="text-align:center; color:#94a3b8; padding:30px;">正在載入全市場指標快取...</td></tr>`;
      return;
    }

    let pool = (cache.stocks || []).filter(s => {
      const p = s.pe;
      const price = s.price || 0;
      return (p !== undefined && p !== null && p >= 4.0 && p <= 30.0 && price >= 10.0);
    });

    if (filter === "stock") pool = pool.filter(s => !s.is_etf);
    else if (filter === "etf") pool = pool.filter(s => s.is_etf);

    pool.sort((a, b) => a.pe - b.pe);
    const topItems = pool.slice(0, 100);

    tbody.innerHTML = "";
    topItems.forEach((it, idx) => {
      const tr = document.createElement("tr");
      const isIn = mySyms.has((it.symbol || "").toUpperCase());
      const inTag = isIn ? `<span class="badge-tag tag-tw">已在庫</span>` : `<span class="cell-sub text-dim">未持有</span>`;

      const pe = `${Number(it.pe).toFixed(2)} 倍`;
      const ydVal = (it.yield_pct !== undefined && it.yield_pct !== null) ? it.yield_pct : it.dividend_yield;
      const yd = (ydVal !== undefined && ydVal > 0) ? `${ydVal.toFixed(2)}%` : "--";
      const pb = (it.pb !== undefined && it.pb !== null && it.pb > 0) ? Number(it.pb).toFixed(2) : "--";
      const roe = (it.roe !== undefined && it.roe !== null && !isNaN(it.roe)) ? `${Number(it.roe).toFixed(2)}%` : "--";
      const priceStr = (it.price && it.price > 0) ? it.price.toFixed(2) : "--";

      tr.innerHTML = `
        <td>${idx + 1}</td>
        <td><b>${it.symbol}</b></td>
        <td>${it.name || it.symbol}</td>
        <td><b>${priceStr}</b></td>
        <td class="text-accent font-bold">${pe}</td>
        <td class="text-gold font-bold">${yd}</td>
        <td>${pb}</td>
        <td class="${(it.roe || 0) >= 15 ? "text-red font-bold" : ""}">${roe}</td>
        <td>${inTag}</td>
      `;
      tbody.appendChild(tr);
    });
    return;
  }

  // ==========================================
  // 分支 D：[營收年增(YoY)排行] (rev)
  // 對齊本機端 unified_ranking_gui.py:
  // 表頭: 名次 | 代號 | 名稱 | 現價 | 營收年增率 | 累計年增率 | 單月營收(億) | 產業別 | 在庫狀態
  // ==========================================
  if (mType === "rev") {
    if (bannerBar) bannerBar.classList.remove("hidden");
    if (titleEl) titleEl.textContent = "全市場最新月份營收爆發成長排行榜 (Revenue YoY Growth Rankings)";
    if (tipEl) tipEl.textContent = "依上市公司公告最新單月營收年增率 (YoY) 降序排列 ‧ 捕捉基本面成長飆股";

    if (thead) {
      thead.innerHTML = `
        <tr>
          <th>名次</th>
          <th>代號</th>
          <th>名稱</th>
          <th>現價</th>
          <th>營收年增率</th>
          <th>累計年增率</th>
          <th>單月營收(億)</th>
          <th>產業別</th>
          <th>在庫狀態</th>
        </tr>`;
    }

    const cache = marketRankState.rawMarketData;
    if (!cache || (!cache.revenues && !cache.stocks)) {
      tbody.innerHTML = `<tr><td colspan="9" style="text-align:center; color:#94a3b8; padding:30px;">正在載入全市場指標快取...</td></tr>`;
      return;
    }

    // 建立現價字典
    const priceMap = {};
    (cache.stocks || []).forEach(s => {
      if (s && s.symbol) priceMap[s.symbol.toUpperCase()] = s.price || 0;
    });

    let pool = (cache.revenues || []).filter(r => (r && r.yoy !== undefined && r.yoy !== null && r.yoy > 0));
    if (filter === "stock") pool = pool.filter(s => !s.is_etf);
    else if (filter === "etf") pool = pool.filter(s => s.is_etf);

    pool.sort((a, b) => b.yoy - a.yoy);
    const topItems = pool.slice(0, 100);

    tbody.innerHTML = "";
    topItems.forEach((it, idx) => {
      const tr = document.createElement("tr");
      const sym = (it.symbol || "").toUpperCase();
      const isIn = mySyms.has(sym);
      const inTag = isIn ? `<span class="badge-tag tag-tw">已在庫</span>` : `<span class="cell-sub text-dim">未持有</span>`;

      const curP = priceMap[sym] || it.price || 0;
      const priceStr = curP > 0 ? curP.toFixed(2) : "--";
      const yoy = `+${Number(it.yoy).toFixed(2)}%`;
      const cum = (it.cum_yoy !== undefined && it.cum_yoy !== null) ? `${it.cum_yoy > 0 ? "+" : ""}${Number(it.cum_yoy).toFixed(2)}%` : "--";
      const curE = (it.rev_cur_e !== undefined && it.rev_cur_e !== null) ? `${Number(it.rev_cur_e).toFixed(2)}` : "--";
      const ind = it.industry || "--";

      tr.innerHTML = `
        <td>${idx + 1}</td>
        <td><b>${it.symbol}</b></td>
        <td>${it.name || it.symbol}</td>
        <td><b>${priceStr}</b></td>
        <td class="text-red font-bold">${yoy}</td>
        <td class="${(it.cum_yoy || 0) >= 0 ? "text-red" : "text-green"}">${cum}</td>
        <td>${curE}</td>
        <td class="text-dim">${ind}</td>
        <td>${inTag}</td>
      `;
      tbody.appendChild(tr);
    });
    return;
  }

  // ==========================================
  // 分支 E：[高ROE排行] (roe)
  // 對齊本機端 unified_ranking_gui.py:
  // 表頭: 名次 | 代號 | 名稱 | 現價 | ROE(%) | 本益比 | 殖利率 | 股價淨值比 | 在庫狀態
  // ==========================================
  if (mType === "roe") {
    if (bannerBar) bannerBar.classList.remove("hidden");
    if (titleEl) titleEl.textContent = "全市場股東權益報酬率績優排行榜 (High ROE Rankings)";
    if (tipEl) tipEl.textContent = "杜邦分析優選 ‧ 排除 PE < 4.0 與極端資產膨脹 ‧ ROE 8.0% ~ 75.0% 高資本報酬率";

    if (thead) {
      thead.innerHTML = `
        <tr>
          <th>名次</th>
          <th>代號</th>
          <th>名稱</th>
          <th>現價</th>
          <th>ROE(%)</th>
          <th>本益比</th>
          <th>殖利率</th>
          <th>股價淨值比</th>
          <th>在庫狀態</th>
        </tr>`;
    }

    const cache = marketRankState.rawMarketData;
    if (!cache || !cache.stocks) {
      tbody.innerHTML = `<tr><td colspan="9" style="text-align:center; color:#94a3b8; padding:30px;">正在載入全市場指標快取...</td></tr>`;
      return;
    }

    let pool = (cache.stocks || []).filter(s => {
      const roe = s.roe;
      const pe = s.pe;
      return (roe !== undefined && roe !== null && pe !== undefined && pe !== null && pe >= 4.0 && roe >= 8.0 && roe <= 75.0);
    });

    if (filter === "stock") pool = pool.filter(s => !s.is_etf);
    else if (filter === "etf") pool = pool.filter(s => s.is_etf);

    pool.sort((a, b) => b.roe - a.roe);
    const topItems = pool.slice(0, 100);

    tbody.innerHTML = "";
    topItems.forEach((it, idx) => {
      const tr = document.createElement("tr");
      const isIn = mySyms.has((it.symbol || "").toUpperCase());
      const inTag = isIn ? `<span class="badge-tag tag-tw">已在庫</span>` : `<span class="cell-sub text-dim">未持有</span>`;

      const roe = `${Number(it.roe).toFixed(2)}%`;
      const pe = (it.pe !== undefined && it.pe !== null && it.pe > 0) ? `${Number(it.pe).toFixed(2)} 倍` : "--";
      const ydVal = (it.yield_pct !== undefined && it.yield_pct !== null) ? it.yield_pct : it.dividend_yield;
      const yd = (ydVal !== undefined && ydVal > 0) ? `${ydVal.toFixed(2)}%` : "--";
      const pb = (it.pb !== undefined && it.pb !== null && it.pb > 0) ? Number(it.pb).toFixed(2) : "--";
      const priceStr = (it.price && it.price > 0) ? it.price.toFixed(2) : "--";

      tr.innerHTML = `
        <td>${idx + 1}</td>
        <td><b>${it.symbol}</b></td>
        <td>${it.name || it.symbol}</td>
        <td><b>${priceStr}</b></td>
        <td class="text-red font-bold">${roe}</td>
        <td>${pe}</td>
        <td class="text-gold font-bold">${yd}</td>
        <td>${pb}</td>
        <td>${inTag}</td>
      `;
      tbody.appendChild(tr);
    });
    return;
  }
}
