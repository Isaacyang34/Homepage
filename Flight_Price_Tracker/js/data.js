/**
 * data.js — 模擬數據引擎
 * 基於目的地 + 時間動態產生真實感數據
 * 可替換成真實 API 呼叫
 */

const DataEngine = (() => {

  /* ========== 城市資料庫 ========== */
  const CITIES = [
    { code: 'TPE', name: '台北', country: '台灣', emoji: '🇹🇼', region: 'Asia' },
    { code: 'NRT', name: '東京 成田', country: '日本', emoji: '🇯🇵', region: 'Asia' },
    { code: 'HND', name: '東京 羽田', country: '日本', emoji: '🇯🇵', region: 'Asia' },
    { code: 'KIX', name: '大阪 關西', country: '日本', emoji: '🇯🇵', region: 'Asia' },
    { code: 'CTS', name: '札幌 千歲', country: '日本', emoji: '🇯🇵', region: 'Asia' },
    { code: 'FUK', name: '福岡', country: '日本', emoji: '🇯🇵', region: 'Asia' },
    { code: 'OKA', name: '沖繩 那霸', country: '日本', emoji: '🇯🇵', region: 'Asia' },
    { code: 'ICN', name: '首爾 仁川', country: '韓國', emoji: '🇰🇷', region: 'Asia' },
    { code: 'BKK', name: '曼谷', country: '泰國', emoji: '🇹🇭', region: 'Asia' },
    { code: 'SIN', name: '新加坡', country: '新加坡', emoji: '🇸🇬', region: 'Asia' },
    { code: 'HKG', name: '香港', country: '香港', emoji: '🇭🇰', region: 'Asia' },
    { code: 'MNL', name: '馬尼拉', country: '菲律賓', emoji: '🇵🇭', region: 'Asia' },
    { code: 'KUL', name: '吉隆坡', country: '馬來西亞', emoji: '🇲🇾', region: 'Asia' },
    { code: 'DPS', name: '峇里島', country: '印尼', emoji: '🇮🇩', region: 'Asia' },
    { code: 'PNH', name: '金邊', country: '柬埔寨', emoji: '🇰🇭', region: 'Asia' },
    { code: 'SGN', name: '胡志明', country: '越南', emoji: '🇻🇳', region: 'Asia' },
    { code: 'HAN', name: '河內', country: '越南', emoji: '🇻🇳', region: 'Asia' },
    { code: 'PVG', name: '上海 浦東', country: '中國', emoji: '🇨🇳', region: 'Asia' },
    { code: 'PEK', name: '北京', country: '中國', emoji: '🇨🇳', region: 'Asia' },
    { code: 'SYD', name: '雪梨', country: '澳洲', emoji: '🇦🇺', region: 'Oceania' },
    { code: 'MEL', name: '墨爾本', country: '澳洲', emoji: '🇦🇺', region: 'Oceania' },
    { code: 'LHR', name: '倫敦 希斯羅', country: '英國', emoji: '🇬🇧', region: 'Europe' },
    { code: 'CDG', name: '巴黎 戴高樂', country: '法國', emoji: '🇫🇷', region: 'Europe' },
    { code: 'FRA', name: '法蘭克福', country: '德國', emoji: '🇩🇪', region: 'Europe' },
    { code: 'FCO', name: '羅馬 菲烏米奇諾', country: '義大利', emoji: '🇮🇹', region: 'Europe' },
    { code: 'LAX', name: '洛杉磯', country: '美國', emoji: '🇺🇸', region: 'Americas' },
    { code: 'JFK', name: '紐約 甘迺迪', country: '美國', emoji: '🇺🇸', region: 'Americas' },
    { code: 'SFO', name: '舊金山', country: '美國', emoji: '🇺🇸', region: 'Americas' },
    { code: 'HNL', name: '夏威夷 檀香山', country: '美國', emoji: '🇺🇸', region: 'Americas' },
    { code: 'YVR', name: '溫哥華', country: '加拿大', emoji: '🇨🇦', region: 'Americas' },
  ];

  /* ========== 航空公司 ========== */
  const AIRLINES = [
    { code: 'CI', name: '中華航空', emoji: '🟥', logo: '✈️', rating: 4.1 },
    { code: 'BR', name: '長榮航空', emoji: '🟩', logo: '🛫', rating: 4.3 },
    { code: 'JX', name: '星宇航空', emoji: '⭐', logo: '🌟', rating: 4.5 },
    { code: 'IT', name: '台灣虎航', emoji: '🐯', logo: '🐯', rating: 3.7 },
    { code: 'TW', name: '德威航空', emoji: '🇰🇷', logo: '🛩️', rating: 3.8 },
    { code: 'JL', name: '日本航空', emoji: '🇯🇵', logo: '⛩️', rating: 4.4 },
    { code: 'NH', name: '全日空', emoji: '🌸', logo: '🌸', rating: 4.6 },
    { code: 'SQ', name: '新加坡航空', emoji: '🌴', logo: '🦚', rating: 4.7 },
    { code: 'CX', name: '國泰航空', emoji: '🇭🇰', logo: '🐉', rating: 4.4 },
    { code: 'TG', name: '泰國國際航空', emoji: '🇹🇭', logo: '🌺', rating: 4.0 },
    { code: 'KE', name: '大韓航空', emoji: '🇰🇷', logo: '🎎', rating: 4.2 },
    { code: 'OZ', name: '韓亞航空', emoji: '🇰🇷', logo: '🌙', rating: 4.1 },
    { code: 'MH', name: '馬來西亞航空', emoji: '🇲🇾', logo: '🦅', rating: 3.9 },
    { code: 'QF', name: '澳洲航空', emoji: '🇦🇺', logo: '🦘', rating: 4.3 },
    { code: 'BA', name: '英國航空', emoji: '🇬🇧', logo: '🎩', rating: 4.0 },
    { code: 'AF', name: '法國航空', emoji: '🇫🇷', logo: '🗼', rating: 4.1 },
    { code: 'LH', name: '漢莎航空', emoji: '🇩🇪', logo: '🦅', rating: 4.2 },
    { code: 'EK', name: '阿聯酋航空', emoji: '🇦🇪', logo: '🌟', rating: 4.8 },
  ];

  /* ========== 旅遊平台 ========== */
  const PLATFORMS = [
    { name: 'Skyscanner',  emoji: '🔵', color: '#0770e3' },
    { name: 'Google Flights', emoji: '🔴', color: '#4285f4' },
    { name: 'Expedia',    emoji: '🟡', color: '#ffc300' },
    { name: 'Trip.com',   emoji: '🟣', color: '#1d9bf0' },
    { name: 'Kayak',      emoji: '🟠', color: '#ff690f' },
    { name: 'Booking.com', emoji: '🔷', color: '#003580' },
    { name: '易遊網',      emoji: '🇹🇼', color: '#e63946' },
    { name: '雄獅旅遊',    emoji: '🦁', color: '#f4a261' },
    { name: 'Agoda',      emoji: '💚', color: '#00b14f' },
  ];

  /* ========== 飯店資料庫 ========== */
  const HOTELS = {
    default: [
      { name: '市區精品酒店', stars: 4, rating: 8.6, emoji: '🏨' },
      { name: '豪華溫泉旅館', stars: 5, rating: 9.2, emoji: '♨️' },
      { name: '商旅飯店', stars: 3, rating: 7.8, emoji: '🏩' },
      { name: '設計風格旅店', stars: 4, rating: 8.9, emoji: '🎨' },
      { name: '海景度假村', stars: 5, rating: 9.4, emoji: '🌊' },
      { name: '市中心商務酒店', stars: 4, rating: 8.3, emoji: '🏙️' },
    ],
  };

  /* ========== 基礎票價（TWD 含稅 / 人 單程） ========== */
  const BASE_PRICES = {
    TPE_NRT: 8500, TPE_HND: 9000, TPE_KIX: 7800, TPE_CTS: 9500,
    TPE_FUK: 7200, TPE_OKA: 6500, TPE_ICN: 6000, TPE_BKK: 9800,
    TPE_SIN: 11000, TPE_HKG: 5500, TPE_MNL: 7000, TPE_KUL: 10500,
    TPE_DPS: 13000, TPE_PNH: 11000, TPE_SGN: 10000, TPE_HAN: 9500,
    TPE_PVG: 6800, TPE_PEK: 7200, TPE_SYD: 22000, TPE_MEL: 23000,
    TPE_LHR: 38000, TPE_CDG: 36000, TPE_FRA: 35000, TPE_FCO: 37000,
    TPE_LAX: 32000, TPE_JFK: 40000, TPE_SFO: 33000, TPE_HNL: 20000,
    TPE_YVR: 35000,
  };

  const getBasePrice = (fromCode, toCode) => {
    return BASE_PRICES[`${fromCode}_${toCode}`]
      || BASE_PRICES[`${toCode}_${fromCode}`]
      || 15000; // fallback
  };

  /* ========== 隨機工具 ========== */
  const seededRandom = (seed) => {
    const x = Math.sin(seed + 1) * 10000;
    return x - Math.floor(x);
  };

  const randomBetween = (min, max, seed) =>
    Math.round(min + seededRandom(seed) * (max - min));

  const pick = (arr, seed) => arr[Math.floor(seededRandom(seed) * arr.length)];

  /* ========== 產生 7 天歷史價格 ========== */
  const generatePriceHistory = (basePrice, planSeed, days = 8) => {
    const history = [];
    const today = new Date();

    for (let i = days - 1; i >= 0; i--) {
      const d = new Date(today);
      d.setDate(d.getDate() - i);
      const dateStr = d.toISOString().split('T')[0];

      // 每天波動 ±15%
      const variation = (seededRandom(planSeed + i * 7) - 0.5) * 0.3;
      // 加入週期性（週末漲、週間跌）
      const weekday = d.getDay();
      const weekEffect = (weekday === 0 || weekday === 6) ? 0.08 : -0.04;
      // 趨勢：越近越高
      const trendEffect = (days - i) * 0.003;

      const price = Math.round(basePrice * (1 + variation + weekEffect + trendEffect));
      history.push({ date: dateStr, price, label: formatDateLabel(d) });
    }
    return history;
  };

  const formatDateLabel = (d) => {
    const days = ['日', '一', '二', '三', '四', '五', '六'];
    return `${d.getMonth() + 1}/${d.getDate()}(${days[d.getDay()]})`;
  };

  /* ========== 產生航班時間 ========== */
  const generateFlightTime = (fromCode, toCode, seed) => {
    const departures = ['06:30', '07:15', '08:00', '09:30', '11:00',
                        '13:30', '15:00', '16:45', '18:30', '20:00', '22:10'];
    // 飛行時間（小時）
    const DURATIONS = {
      TPE_NRT: 3.5, TPE_HND: 3.5, TPE_KIX: 2.8, TPE_ICN: 2.5,
      TPE_BKK: 4.5, TPE_SIN: 4.8, TPE_HKG: 1.8, TPE_MNL: 2.3,
      TPE_SYD: 10, TPE_LHR: 14, TPE_LAX: 13, TPE_JFK: 16,
      TPE_DPS: 5.5, TPE_PVG: 2, TPE_PEK: 2.5,
    };
    const key = `${fromCode}_${toCode}`;
    const revKey = `${toCode}_${fromCode}`;
    const hours = DURATIONS[key] || DURATIONS[revKey] || 5;

    const depStr = departures[Math.floor(seededRandom(seed) * departures.length)];
    const [dh, dm] = depStr.split(':').map(Number);
    const totalMin = dh * 60 + dm + Math.round(hours * 60);
    const ah = Math.floor(totalMin / 60) % 24;
    const am = totalMin % 60;
    const arrStr = `${String(ah).padStart(2,'0')}:${String(am).padStart(2,'0')}`;
    const nextDay = totalMin >= 24 * 60;

    const stopSeed = seededRandom(seed + 3);
    let stops, stopText;
    if (hours < 3) { stops = 0; stopText = '直飛'; }
    else if (hours < 7) { stops = stopSeed < 0.55 ? 0 : 1; stopText = stops === 0 ? '直飛' : '1 停'; }
    else { stops = stopSeed < 0.3 ? 1 : 2; stopText = `${stops} 停`; }

    const durationText = `${Math.floor(hours)}h${Math.round((hours % 1) * 60)}m`;

    return { dep: depStr, arr: arrStr, nextDay, duration: durationText, stops, stopText };
  };

  /* ========== 主要：產生搜尋結果 ========== */
  const generateResults = (params) => {
    const { from, to, departDate, returnDate, adults, children, nights } = params;

    const fromCity = CITIES.find(c => c.code === from || c.name.includes(from)) || CITIES[0];
    const toCity   = CITIES.find(c => c.code === to   || c.name.includes(to))   || CITIES[1];

    const baseFlight = getBasePrice(fromCity.code, toCity.code);
    const totalPax = adults + (children || 0);
    const isLongHaul = baseFlight > 20000;

    const results = [];
    const platformCount = 6;

    for (let i = 0; i < platformCount; i++) {
      const seed = (fromCity.code.charCodeAt(0) + toCity.code.charCodeAt(0)) * (i + 1) + 42;
      const airline = pick(AIRLINES, seed);
      const platform = PLATFORMS[i % PLATFORMS.length];
      const hotel = pick(HOTELS.default, seed + 1);

      // 機票價格
      const flightVariation = 0.85 + seededRandom(seed + 5) * 0.35;
      const flightPerPax = Math.round(baseFlight * flightVariation);
      const flightTotal = flightPerPax * totalPax * 2; // 來回

      // 住宿價格（每晚 / 人）
      const nightlyBase = isLongHaul
        ? randomBetween(3000, 8000, seed + 9)
        : randomBetween(1500, 5000, seed + 9);
      const hotelTotal = nightlyBase * (nights || 3) * totalPax;

      const total = flightTotal + hotelTotal;
      const priceHistory = generatePriceHistory(total, seed);

      // 昨天 vs 今天
      const yesterday = priceHistory[priceHistory.length - 2]?.price || total;
      const priceChange = total - yesterday;

      // 去程 & 回程航班
      const outbound = generateFlightTime(fromCity.code, toCity.code, seed + 100);
      const inbound  = generateFlightTime(toCity.code, fromCity.code, seed + 200);

      results.push({
        id: `plan_${fromCity.code}_${toCity.code}_${i}`,
        rank: i,
        platform,
        airline,
        fromCity, toCity,
        outbound, inbound,
        hotel,
        nightlyPrice: nightlyBase,
        flightTotal, hotelTotal,
        totalPrice: total,
        perPersonPrice: Math.round(total / totalPax),
        priceChange,
        priceHistory,
        adults, children: children || 0, nights: nights || 3,
        departDate, returnDate,
        isBestDeal: false,
        savedToday: randomBetween(5, 30, seed + 77),
      });
    }

    // 標記最低價
    results.sort((a, b) => a.totalPrice - b.totalPrice);
    results[0].isBestDeal = true;

    return results;
  };

  /* ========== 產生趨勢摘要統計 ========== */
  const generateTrendStats = (results) => {
    if (!results.length) return null;

    const allHistories = results.map(r => r.priceHistory);
    const bestResult = results[0];
    const hist = bestResult.priceHistory;

    const prices = hist.map(h => h.price);
    const minPrice = Math.min(...prices);
    const maxPrice = Math.max(...prices);
    const avgPrice = Math.round(prices.reduce((a, b) => a + b, 0) / prices.length);
    const todayPrice = prices[prices.length - 1];
    const minDay = hist[prices.indexOf(minPrice)];
    const isLowestToday = todayPrice === minPrice;

    return {
      minPrice, maxPrice, avgPrice, todayPrice,
      minDay, isLowestToday,
      priceRange: maxPrice - minPrice,
      savings: maxPrice - minPrice,
      trend: prices[prices.length - 1] > prices[prices.length - 4] ? 'up' : 'down',
    };
  };

  /* ========== 城市搜尋自動補全 ========== */
  const searchCities = (query) => {
    if (!query || query.length < 1) return [];
    const q = query.toLowerCase();
    return CITIES.filter(c =>
      c.name.toLowerCase().includes(q) ||
      c.code.toLowerCase().includes(q) ||
      c.country.toLowerCase().includes(q)
    ).slice(0, 8);
  };

  /* ========== 追蹤方案每日價格更新 ========== */
  const refreshTrackedPrice = (plan) => {
    const variation = (Math.random() - 0.5) * 0.12;
    const newPrice = Math.round(plan.totalPrice * (1 + variation));
    const today = new Date().toISOString().split('T')[0];
    Storage.addPricePoint(plan.id, newPrice, today);

    const history = Storage.getPriceHistory(plan.id);
    const prices = history.map(h => h.price);
    const minPrice = Math.min(...prices);
    const isLowest = newPrice <= minPrice;

    return { newPrice, isLowest, history };
  };

  return {
    CITIES, AIRLINES, PLATFORMS,
    generateResults,
    generateTrendStats,
    generatePriceHistory,
    searchCities,
    refreshTrackedPrice,
    formatDateLabel,
  };
})();
