/**
 * 零知識端到端加密投資庫存儀表板 (Web Crypto API 原生純前端)
 * 具備瀏覽器直連即時行情 (CNYES + TradingView 雙引擎 + Firebase 備援)、
 * 即時動態損益重算與自組 ETF 盤中淨值/K線同步運算
 */

const DEFAULT_FIREBASE_URL = "https://my-stock-tracker-2a94e-default-rtdb.asia-southeast1.firebasedatabase.app";
const WEB_VERSION = "V1.1.3.1";
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
      renderMarginRiskAnalytics();
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
  currentType: "yield",
  filter: "all",
  rawMarketData: null
};

function renderMarginRiskAnalytics() {
  const data = state.decryptedData;
  if (!data) return;

  const positions = state.normalizedPositions || [];
  const isPrivate = state.privacyMode;

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
    const callPrice = shares > 0 ? Number(((marginLoan * 1.30) / shares).toFixed(2)) : 0;
    const callDiff = ratio < 130 ? Math.max(0, Math.round((marginLoan * 1.30) - marketVal)) : 0;

    let riskLevel = "SAFE";
    let riskLabel = "安全";
    if (ratio < 130) {
      riskLevel = "CALL";
      riskLabel = "追繳";
      callCount++;
    } else if (ratio < 160) {
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
      overallRatioEl.className = `metric-val ${overallRatio >= 160 ? "text-green" : (overallRatio >= 130 ? "text-gold" : "text-red")}`;
    }
    if (overallStatusEl) {
      if (overallRatio >= 160) {
        overallStatusEl.textContent = "整戶安全 (擔保維持率充裕)";
        overallStatusEl.style.color = "#52c41a";
      } else if (overallRatio >= 130) {
        overallStatusEl.textContent = "整戶警戒 (留意標的跌幅)";
        overallStatusEl.style.color = "#ffd166";
      } else {
        overallStatusEl.textContent = "追繳斷頭警報！(需儘速補繳)";
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
      <td><b>${it.current_price > 0 ? it.current_price.toFixed(2) : "--"}</b></td>
      <td>${isPrivate ? "***" : loanDisplay}</td>
      <td>${isPrivate ? "***" : `$ ${fmtNum(it.market_val)}`}</td>
      <td><b class="${it.is_margin ? (it.maintenance_ratio >= 160 ? "text-green" : (it.maintenance_ratio >= 130 ? "text-gold" : "text-red")) : ""}">${isPrivate ? "***" : ratioDisplay}</b></td>
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
    const resp = await fetch("../market_ranking_cache.json?t=" + Date.now(), { cache: "no-store" });
    if (resp.ok) {
      marketRankState.rawMarketData = await resp.json();
      renderMarketRankings();
    }
  } catch (e) {}
}

function renderMarketRankings() {
  const tbody = document.getElementById("market-ranking-tbody");
  if (!tbody) return;
  tbody.innerHTML = "";

  const cache = marketRankState.rawMarketData;
  if (!cache || !cache.stocks) {
    tbody.innerHTML = `<tr><td colspan="9" style="text-align:center; color:#94a3b8; padding:30px;">正在載入全市場指標快取或資料快取建立中...</td></tr>`;
    return;
  }

  const mType = marketRankState.currentType || "yield";
  const filter = marketRankState.filter || "all";

  let list = [...cache.stocks];
  if (filter === "stock") list = list.filter(s => !s.is_etf);
  else if (filter === "etf") list = list.filter(s => s.is_etf);

  if (mType === "yield") {
    list.sort((a, b) => (b.dividend_yield || 0) - (a.dividend_yield || 0));
  } else if (mType === "pe") {
    list = list.filter(s => (s.pe_ratio || 0) >= 4 && (s.pe_ratio || 0) <= 30);
    list.sort((a, b) => (a.pe_ratio || 999) - (b.pe_ratio || 999));
  } else if (mType === "rev") {
    list.sort((a, b) => (b.revenue_yoy || -999) - (a.revenue_yoy || -999));
  } else if (mType === "roe") {
    list.sort((a, b) => (b.roe || -999) - (a.roe || -999));
  }

  const topItems = list.slice(0, 100);
  const mySyms = new Set((state.normalizedPositions || []).map(p => (p.symbol || "").toUpperCase()));

  topItems.forEach((it, idx) => {
    const tr = document.createElement("tr");
    const isIn = mySyms.has(it.symbol.toUpperCase());
    const inTag = isIn ? `<span class="badge-tag tag-tw">已在庫</span>` : `<span class="cell-sub text-dim">未持有</span>`;

    const yd = (it.dividend_yield !== undefined && it.dividend_yield > 0) ? `${it.dividend_yield.toFixed(2)}%` : "--";
    const pe = (it.pe_ratio !== undefined && it.pe_ratio > 0) ? it.pe_ratio.toFixed(1) : "--";
    const yoy = (it.revenue_yoy !== undefined) ? `${it.revenue_yoy > 0 ? "+" : ""}${it.revenue_yoy.toFixed(1)}%` : "--";
    const roe = (it.roe !== undefined) ? `${it.roe.toFixed(1)}%` : "--";

    tr.innerHTML = `
      <td>${idx + 1}</td>
      <td><b>${it.symbol}</b></td>
      <td>${it.name || it.symbol}</td>
      <td>${it.price ? it.price.toFixed(2) : "--"}</td>
      <td class="text-gold font-bold">${yd}</td>
      <td>${pe}</td>
      <td class="${(it.revenue_yoy || 0) >= 0 ? "text-red" : "text-green"}">${yoy}</td>
      <td>${roe}</td>
      <td>${inTag}</td>
    `;
    tbody.appendChild(tr);
  });
}
