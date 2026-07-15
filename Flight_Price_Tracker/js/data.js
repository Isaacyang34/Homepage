/**
 * data.js — 模擬數據引擎（擴充版）
 * 修正：擴充城市庫、修正種子 hash、修正 fallback 邏輯
 */

const DataEngine = (() => {

  /* ========== 城市資料庫（完整版，涵蓋 DEST_CATEGORIES 所有城市）========== */
  const CITIES = [
    // 台灣出發地
    { code:'TPE', name:'桃園國際機場', country:'台灣', emoji:'🛫', region:'Asia' },
    { code:'TSA', name:'台北松山機場', country:'台灣', emoji:'🛫', region:'Asia' },
    { code:'RMQ', name:'台中清泉崗機場', country:'台灣', emoji:'🛫', region:'Asia' },
    { code:'TNN', name:'台南機場', country:'台灣', emoji:'🛫', region:'Asia' },
    { code:'KHH', name:'高雄國際機場', country:'台灣', emoji:'🛫', region:'Asia' },
    { code:'HUN', name:'花蓮機場', country:'台灣', emoji:'🛩️', region:'Asia' },
    { code:'TTT', name:'台東豐年機場', country:'台灣', emoji:'🛩️', region:'Asia' },
    { code:'KNH', name:'金門尚義機場', country:'台灣', emoji:'🛩️', region:'Asia' },
    { code:'MZG', name:'澎湖馬公機場', country:'台灣', emoji:'🛩️', region:'Asia' },
    // 日本
    { code:'NRT', name:'東京 成田', country:'日本', emoji:'⛩️', region:'Asia' },
    { code:'HND', name:'東京 羽田', country:'日本', emoji:'🗼', region:'Asia' },
    { code:'KIX', name:'大阪 關西', country:'日本', emoji:'🏯', region:'Asia' },
    { code:'CTS', name:'札幌 千歲', country:'日本', emoji:'❄️', region:'Asia' },
    { code:'FUK', name:'福岡', country:'日本', emoji:'🍜', region:'Asia' },
    { code:'OKA', name:'沖繩 那霸', country:'日本', emoji:'🏖️', region:'Asia' },
    { code:'NGO', name:'名古屋 中部', country:'日本', emoji:'🏯', region:'Asia' },
    { code:'KMJ', name:'熊本', country:'日本', emoji:'🐻', region:'Asia' },
    { code:'SDJ', name:'仙台', country:'日本', emoji:'🌸', region:'Asia' },
    { code:'KOJ', name:'鹿兒島', country:'日本', emoji:'🌋', region:'Asia' },
    // 韓國
    { code:'ICN', name:'首爾 仁川', country:'韓國', emoji:'🎎', region:'Asia' },
    { code:'GMP', name:'首爾 金浦', country:'韓國', emoji:'🏙️', region:'Asia' },
    { code:'PUS', name:'釜山 金海', country:'韓國', emoji:'🌊', region:'Asia' },
    { code:'CJU', name:'濟州島', country:'韓國', emoji:'🌿', region:'Asia' },
    { code:'TAE', name:'大邱', country:'韓國', emoji:'🎵', region:'Asia' },
    // 東南亞
    { code:'BKK', name:'曼谷 素萬那普', country:'泰國', emoji:'🌺', region:'Asia' },
    { code:'DMK', name:'曼谷 廊曼', country:'泰國', emoji:'🛕', region:'Asia' },
    { code:'HKT', name:'普吉島', country:'泰國', emoji:'🏝️', region:'Asia' },
    { code:'CNX', name:'清邁', country:'泰國', emoji:'🐘', region:'Asia' },
    { code:'SIN', name:'新加坡 樟宜', country:'新加坡', emoji:'🦁', region:'Asia' },
    { code:'KUL', name:'吉隆坡', country:'馬來西亞', emoji:'🏙️', region:'Asia' },
    { code:'MNL', name:'馬尼拉', country:'菲律賓', emoji:'🌴', region:'Asia' },
    { code:'CEB', name:'宿霧', country:'菲律賓', emoji:'🐠', region:'Asia' },
    { code:'DPS', name:'峇里島', country:'印尼', emoji:'🌴', region:'Asia' },
    { code:'SGN', name:'胡志明市', country:'越南', emoji:'🛵', region:'Asia' },
    { code:'HAN', name:'河內', country:'越南', emoji:'🏮', region:'Asia' },
    { code:'DAD', name:'峴港', country:'越南', emoji:'🌉', region:'Asia' },
    { code:'PNH', name:'金邊', country:'柬埔寨', emoji:'🛕', region:'Asia' },
    { code:'REP', name:'暹粒', country:'柬埔寨', emoji:'🏛️', region:'Asia' },
    { code:'RGN', name:'仰光', country:'緬甸', emoji:'🏯', region:'Asia' },
    // 中港澳
    { code:'HKG', name:'香港', country:'香港', emoji:'🐉', region:'Asia' },
    { code:'MFM', name:'澳門', country:'澳門', emoji:'🎰', region:'Asia' },
    { code:'PVG', name:'上海 浦東', country:'中國', emoji:'🌆', region:'Asia' },
    { code:'SHA', name:'上海 虹橋', country:'中國', emoji:'🏙️', region:'Asia' },
    { code:'PEK', name:'北京 首都', country:'中國', emoji:'🏛️', region:'Asia' },
    { code:'PKX', name:'北京 大興', country:'中國', emoji:'🚀', region:'Asia' },
    { code:'CAN', name:'廣州', country:'中國', emoji:'🌸', region:'Asia' },
    { code:'SZX', name:'深圳', country:'中國', emoji:'🏗️', region:'Asia' },
    { code:'XIY', name:'西安', country:'中國', emoji:'🏺', region:'Asia' },
    { code:'CTU', name:'成都', country:'中國', emoji:'🐼', region:'Asia' },
    // 其他亞洲
    { code:'DEL', name:'新德里', country:'印度', emoji:'🕌', region:'Asia' },
    { code:'BOM', name:'孟買', country:'印度', emoji:'🎬', region:'Asia' },
    { code:'CMB', name:'可倫坡', country:'斯里蘭卡', emoji:'🌴', region:'Asia' },
    { code:'KTM', name:'加德滿都', country:'尼泊爾', emoji:'🏔️', region:'Asia' },
    { code:'ULN', name:'烏蘭巴托', country:'蒙古', emoji:'🐎', region:'Asia' },
    { code:'NQZ', name:'阿斯塔納', country:'哈薩克', emoji:'🏙️', region:'Asia' },
    { code:'TAS', name:'塔什干', country:'烏茲別克', emoji:'🕌', region:'Asia' },
    // 大洋洲
    { code:'SYD', name:'雪梨', country:'澳洲', emoji:'🦘', region:'Oceania' },
    { code:'MEL', name:'墨爾本', country:'澳洲', emoji:'🏏', region:'Oceania' },
    { code:'BNE', name:'布里斯本', country:'澳洲', emoji:'🌞', region:'Oceania' },
    { code:'PER', name:'伯斯', country:'澳洲', emoji:'🦙', region:'Oceania' },
    { code:'AKL', name:'奧克蘭', country:'紐西蘭', emoji:'🥝', region:'Oceania' },
    { code:'CHC', name:'基督城', country:'紐西蘭', emoji:'🐑', region:'Oceania' },
    { code:'NAN', name:'楠迪', country:'斐濟', emoji:'🏝️', region:'Oceania' },
    { code:'HNL', name:'夏威夷 檀香山', country:'美國', emoji:'🌺', region:'Americas' },
    // 歐洲
    { code:'LHR', name:'倫敦 希斯羅', country:'英國', emoji:'🎩', region:'Europe' },
    { code:'LGW', name:'倫敦 蓋威克', country:'英國', emoji:'🏰', region:'Europe' },
    { code:'CDG', name:'巴黎 戴高樂', country:'法國', emoji:'🗼', region:'Europe' },
    { code:'FRA', name:'法蘭克福', country:'德國', emoji:'🍺', region:'Europe' },
    { code:'MUC', name:'慕尼黑', country:'德國', emoji:'🥨', region:'Europe' },
    { code:'FCO', name:'羅馬', country:'義大利', emoji:'🏛️', region:'Europe' },
    { code:'MXP', name:'米蘭 馬爾彭薩', country:'義大利', emoji:'🎭', region:'Europe' },
    { code:'BCN', name:'巴塞隆納', country:'西班牙', emoji:'⚽', region:'Europe' },
    { code:'MAD', name:'馬德里', country:'西班牙', emoji:'💃', region:'Europe' },
    { code:'AMS', name:'阿姆斯特丹', country:'荷蘭', emoji:'🌷', region:'Europe' },
    { code:'ZRH', name:'蘇黎世', country:'瑞士', emoji:'⛷️', region:'Europe' },
    { code:'VIE', name:'維也納', country:'奧地利', emoji:'🎵', region:'Europe' },
    { code:'PRG', name:'布拉格', country:'捷克', emoji:'🏰', region:'Europe' },
    { code:'ATH', name:'雅典', country:'希臘', emoji:'🏺', region:'Europe' },
    { code:'IST', name:'伊斯坦堡', country:'土耳其', emoji:'🕌', region:'Europe' },
    { code:'DXB', name:'杜拜', country:'阿聯酋', emoji:'🌟', region:'Europe' },
    // 美洲
    { code:'LAX', name:'洛杉磯', country:'美國', emoji:'🎬', region:'Americas' },
    { code:'JFK', name:'紐約 甘迺迪', country:'美國', emoji:'🗽', region:'Americas' },
    { code:'EWR', name:'紐約 紐瓦克', country:'美國', emoji:'🌆', region:'Americas' },
    { code:'SFO', name:'舊金山', country:'美國', emoji:'🌉', region:'Americas' },
    { code:'SEA', name:'西雅圖', country:'美國', emoji:'☁️', region:'Americas' },
    { code:'LAS', name:'拉斯維加斯', country:'美國', emoji:'🎰', region:'Americas' },
    { code:'YVR', name:'溫哥華', country:'加拿大', emoji:'🍁', region:'Americas' },
    { code:'YYZ', name:'多倫多', country:'加拿大', emoji:'🏒', region:'Americas' },
    { code:'YUL', name:'蒙特婁', country:'加拿大', emoji:'❄️', region:'Americas' },
    { code:'GRU', name:'聖保羅', country:'巴西', emoji:'🌴', region:'Americas' },
    { code:'EZE', name:'布宜諾斯艾利斯', country:'阿根廷', emoji:'💃', region:'Americas' },
    { code:'MEX', name:'墨西哥城', country:'墨西哥', emoji:'🌮', region:'Americas' },
    { code:'CUN', name:'坎昆', country:'墨西哥', emoji:'🏖️', region:'Americas' },
  ];

  /* ========== 航空公司 ========== */
  const AIRLINES = [
    { code:'CI',  name:'中華航空', emoji:'🟥', logo:'✈️',  rating:4.1 },
    { code:'BR',  name:'長榮航空', emoji:'🟩', logo:'🛫', rating:4.3 },
    { code:'JX',  name:'星宇航空', emoji:'⭐', logo:'🌟', rating:4.5 },
    { code:'IT',  name:'台灣虎航', emoji:'🐯', logo:'🐯', rating:3.7 },
    { code:'TW',  name:'德威航空', emoji:'🇰🇷', logo:'🛩️', rating:3.8 },
    { code:'JL',  name:'日本航空', emoji:'🇯🇵', logo:'⛩️', rating:4.4 },
    { code:'NH',  name:'全日空',   emoji:'🌸', logo:'🌸', rating:4.6 },
    { code:'SQ',  name:'新加坡航空', emoji:'🌴', logo:'🦚', rating:4.7 },
    { code:'CX',  name:'國泰航空', emoji:'🇭🇰', logo:'🐉', rating:4.4 },
    { code:'TG',  name:'泰國國際航空', emoji:'🇹🇭', logo:'🌺', rating:4.0 },
    { code:'KE',  name:'大韓航空', emoji:'🇰🇷', logo:'🎎', rating:4.2 },
    { code:'OZ',  name:'韓亞航空', emoji:'🇰🇷', logo:'🌙', rating:4.1 },
    { code:'MH',  name:'馬來西亞航空', emoji:'🇲🇾', logo:'🦅', rating:3.9 },
    { code:'QF',  name:'澳洲航空', emoji:'🇦🇺', logo:'🦘', rating:4.3 },
    { code:'BA',  name:'英國航空', emoji:'🇬🇧', logo:'🎩', rating:4.0 },
    { code:'AF',  name:'法國航空', emoji:'🇫🇷', logo:'🗼', rating:4.1 },
    { code:'LH',  name:'漢莎航空', emoji:'🇩🇪', logo:'🦅', rating:4.2 },
    { code:'EK',  name:'阿聯酋航空', emoji:'🇦🇪', logo:'🌟', rating:4.8 },
    { code:'AY',  name:'芬蘭航空', emoji:'🇫🇮', logo:'❄️', rating:4.2 },
    { code:'UA',  name:'聯合航空', emoji:'🇺🇸', logo:'✈️', rating:3.9 },
    { code:'AA',  name:'美國航空', emoji:'🇺🇸', logo:'🦅', rating:3.8 },
    { code:'DL',  name:'達美航空', emoji:'🇺🇸', logo:'🔺', rating:4.0 },
  ];

  /* ========== 旅遊平台 ========== */
  const PLATFORMS = [
    { name:'Skyscanner',   emoji:'🔵', color:'#0770e3' },
    { name:'Google Flights', emoji:'🔴', color:'#4285f4' },
    { name:'Expedia',      emoji:'🟡', color:'#ffc300' },
    { name:'Trip.com',     emoji:'🟣', color:'#1d9bf0' },
    { name:'Kayak',        emoji:'🟠', color:'#ff690f' },
    { name:'Booking.com',  emoji:'🔷', color:'#003580' },
    { name:'易遊網',        emoji:'🇹🇼', color:'#e63946' },
    { name:'雄獅旅遊',      emoji:'🦁', color:'#f4a261' },
    { name:'Agoda',        emoji:'💚', color:'#00b14f' },
  ];

  /* ========== 飯店 ========== */
  const HOTELS = {
    default: [
      { name:'市區精品酒店',   stars:4, rating:8.6, emoji:'🏨' },
      { name:'豪華溫泉旅館',   stars:5, rating:9.2, emoji:'♨️' },
      { name:'商旅飯店',       stars:3, rating:7.8, emoji:'🏩' },
      { name:'設計風格旅店',   stars:4, rating:8.9, emoji:'🎨' },
      { name:'海景度假村',     stars:5, rating:9.4, emoji:'🌊' },
      { name:'市中心商務酒店', stars:4, rating:8.3, emoji:'🏙️' },
    ],
  };

  /* ========== 基礎票價（TWD 含稅 / 人 單程）========== */
  const BASE_PRICES = {
    // 日本
    TPE_NRT:9000, TPE_HND:9200, TPE_KIX:8000, TPE_CTS:9800, TPE_FUK:7500,
    TPE_OKA:6800, TPE_NGO:8200, TPE_KMJ:9000, TPE_SDJ:9500, TPE_KOJ:9200,
    // 韓國
    TPE_ICN:6200, TPE_GMP:6400, TPE_PUS:7200, TPE_CJU:7800, TPE_TAE:7500,
    // 東南亞
    TPE_BKK:10000, TPE_DMK:9500, TPE_HKT:11200, TPE_CNX:10800,
    TPE_SIN:11500, TPE_KUL:10800, TPE_MNL:7200, TPE_CEB:8800,
    TPE_DPS:13500, TPE_SGN:10200, TPE_HAN:9800, TPE_DAD:10200,
    TPE_PNH:11200, TPE_REP:12000, TPE_RGN:13800,
    // 中港澳
    TPE_HKG:5800, TPE_MFM:6000, TPE_PVG:7000, TPE_SHA:6800,
    TPE_PEK:7500, TPE_PKX:7600, TPE_CAN:6500, TPE_SZX:6300,
    TPE_XIY:8000, TPE_CTU:7800,
    // 其他亞洲
    TPE_DEL:15500, TPE_BOM:16500, TPE_CMB:14500, TPE_KTM:16500,
    TPE_ULN:12500, TPE_NQZ:18500, TPE_TAS:17500,
    // 大洋洲
    TPE_SYD:22500, TPE_MEL:23500, TPE_BNE:21500, TPE_PER:24500,
    TPE_AKL:25500, TPE_CHC:26500, TPE_NAN:28500, TPE_HNL:20500,
    // 歐洲
    TPE_LHR:38500, TPE_LGW:37000, TPE_CDG:36500, TPE_FRA:35500,
    TPE_MUC:36500, TPE_FCO:37500, TPE_MXP:36500, TPE_BCN:38000,
    TPE_MAD:38500, TPE_AMS:36500, TPE_ZRH:40500, TPE_VIE:37500,
    TPE_PRG:35500, TPE_ATH:38500, TPE_IST:28500, TPE_DXB:22500,
    // 美洲
    TPE_LAX:32500, TPE_JFK:40500, TPE_EWR:39500, TPE_SFO:33500,
    TPE_SEA:33500, TPE_LAS:35500, TPE_YVR:35500, TPE_YYZ:42500,
    TPE_YUL:43500, TPE_GRU:56000, TPE_EZE:58500, TPE_MEX:46000,
    TPE_CUN:48500,
  };

  // 動態計算不在表中的路線基礎票價（依地區）
  const getBasePrice = (fromCode, toCode) => {
    const key  = `${fromCode}_${toCode}`;
    const rkey = `${toCode}_${fromCode}`;
    if (BASE_PRICES[key])  return BASE_PRICES[key];
    if (BASE_PRICES[rkey]) return BASE_PRICES[rkey];

    // 找目的地的地區，給個合理預設
    const dest = CITIES.find(c => c.code === toCode);
    const regionDefaults = {
      Asia: 12000, Europe: 37000, Americas: 38000, Oceania: 24000
    };
    return regionDefaults[dest?.region] || 15000;
  };

  /* ========== 改良版隨機種子（全碼 hash，解決同首字母問題）========== */
  const strHash = (str) => {
    let h = 5381;
    for (let i = 0; i < str.length; i++) {
      h = ((h << 5) + h) + str.charCodeAt(i);
      h = h & h; // 轉 32-bit integer
    }
    return Math.abs(h);
  };

  const seededRandom = (seed) => {
    const x = Math.sin(seed + 1.23456789) * 10000;
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

      const variation  = (seededRandom(planSeed + i * 7) - 0.5) * 0.28;
      const weekday    = d.getDay();
      const weekEffect = (weekday === 0 || weekday === 6) ? 0.07 : -0.03;
      const trendEffect = (days - i) * 0.004;

      const price = Math.round(basePrice * (1 + variation + weekEffect + trendEffect));
      history.push({ date: dateStr, price, label: formatDateLabel(d) });
    }
    return history;
  };

  const formatDateLabel = (d) => {
    const days = ['日','一','二','三','四','五','六'];
    return `${d.getMonth()+1}/${d.getDate()}(${days[d.getDay()]})`;
  };

  /* ========== 飛行時間估算（依城市地區）========== */
  const estimateFlightHours = (from, to) => {
    const dest = CITIES.find(c => c.code === to);
    const region = dest?.region || 'Asia';
    const regionHours = { Asia: 4.5, Europe: 14, Americas: 14, Oceania: 10 };

    // 精確距離表
    const DURATIONS = {
      TPE_NRT:3.5, TPE_HND:3.5, TPE_KIX:3.0, TPE_CTS:4.0, TPE_FUK:2.5,
      TPE_OKA:1.8, TPE_NGO:3.2, TPE_ICN:2.5, TPE_GMP:2.5, TPE_PUS:2.8,
      TPE_CJU:3.0, TPE_BKK:4.5, TPE_DMK:4.5, TPE_HKT:5.0, TPE_CNX:5.0,
      TPE_SIN:5.0, TPE_KUL:5.5, TPE_MNL:2.5, TPE_CEB:3.0, TPE_DPS:5.5,
      TPE_SGN:4.0, TPE_HAN:3.8, TPE_DAD:3.5, TPE_HKG:1.8, TPE_MFM:2.0,
      TPE_PVG:2.2, TPE_SHA:2.2, TPE_PEK:2.8, TPE_CAN:2.0, TPE_CTU:4.0,
      TPE_SYD:10.0, TPE_MEL:11.0, TPE_BNE:9.5, TPE_HNL:9.0, TPE_AKL:12.0,
      TPE_LHR:14.0, TPE_CDG:14.0, TPE_FRA:14.5, TPE_BCN:15.0, TPE_IST:12.0,
      TPE_DXB:10.5, TPE_LAX:13.0, TPE_JFK:16.5, TPE_SFO:13.0, TPE_YVR:13.5,
    };

    const key  = `${from}_${to}`;
    const rkey = `${to}_${from}`;
    return DURATIONS[key] || DURATIONS[rkey] || regionHours[region];
  };

  /* ========== 產生航班時間 ========== */
  const generateFlightTime = (fromCode, toCode, seed) => {
    const departures = [
      '06:00','06:30','07:00','07:15','07:45','08:00','08:30','09:00',
      '09:30','10:00','11:00','12:30','13:00','14:00','15:00','15:30',
      '16:00','16:45','17:30','18:00','18:30','19:00','20:00','21:00','22:00',
    ];

    const hours = estimateFlightHours(fromCode, toCode);
    const depStr = departures[Math.floor(seededRandom(seed) * departures.length)];
    const [dh, dm] = depStr.split(':').map(Number);
    const totalMin = dh * 60 + dm + Math.round(hours * 60);
    const ah = Math.floor(totalMin / 60) % 24;
    const am = totalMin % 60;
    const arrStr = `${String(ah).padStart(2,'0')}:${String(am).padStart(2,'0')}`;
    const nextDay = totalMin >= 24 * 60;

    const stopSeed = seededRandom(seed + 3);
    let stops, stopText;
    if (hours < 2.5)       { stops = 0; stopText = '直飛'; }
    else if (hours < 6)    { stops = stopSeed < 0.6 ? 0 : 1; stopText = stops === 0 ? '直飛' : '1 停'; }
    else if (hours < 11)   { stops = stopSeed < 0.4 ? 1 : 2; stopText = `${stops} 停`; }
    else                   { stops = stopSeed < 0.3 ? 1 : 2; stopText = `${stops} 停`; }

    const h = Math.floor(hours), m = Math.round((hours % 1) * 60);
    const durationText = m > 0 ? `${h}h ${m}m` : `${h}h`;

    return { dep: depStr, arr: arrStr, nextDay, duration: durationText, stops, stopText };
  };

  /* ========== 主要：產生搜尋結果 ========== */
  const generateResults = (params) => {
    const { from, to, departDate, returnDate, adults, children, nights } = params;

    // 查城市，找不到就用動態建立（避免 fallback 到固定城市）
    const fromCity = CITIES.find(c => c.code === from)
      || { code: from, name: from, country: '台灣', emoji: '🛫', region: 'Asia' };
    const toCity   = CITIES.find(c => c.code === to)
      || { code: to, name: to, country: '未知', emoji: '🌍', region: 'Asia' };

    const baseFlight = getBasePrice(fromCity.code, toCity.code);
    const totalPax   = adults + (children || 0);
    const isLongHaul = baseFlight > 20000;

    const results = [];
    const platformCount = 6;

    for (let i = 0; i < platformCount; i++) {
      // ★ 修正：使用完整城市代碼 hash，避免同首字母問題
      const baseHash = strHash(`${fromCity.code}${toCity.code}`);
      const seed = (baseHash + i * 7919) % 999983;

      const airline  = AIRLINES[i % AIRLINES.length]; // 輪流分配航空公司
      const platform = PLATFORMS[i % PLATFORMS.length];
      const hotel    = pick(HOTELS.default, seed + 1);

      // 機票：各平台有不同折扣（±25%）
      const flightVariation = 0.78 + seededRandom(seed + 5) * 0.44;
      const flightPerPax    = Math.round(baseFlight * flightVariation);
      const flightTotal     = flightPerPax * totalPax * 2; // 來回

      // 住宿
      const nightlyBase = isLongHaul
        ? randomBetween(3500, 9000, seed + 9)
        : randomBetween(1500, 5500, seed + 9);
      const hotelTotal  = nightlyBase * (nights || 3) * totalPax;

      const total        = flightTotal + hotelTotal;
      const priceHistory = generatePriceHistory(total, seed + 100);

      const yesterday   = priceHistory[priceHistory.length - 2]?.price || total;
      const priceChange = total - yesterday;

      const outbound = generateFlightTime(fromCity.code, toCity.code, seed + 200);
      const inbound  = generateFlightTime(toCity.code,   fromCity.code, seed + 300);

      results.push({
        id: `plan_${fromCity.code}_${toCity.code}_${i}`,
        rank: i,
        platform, airline,
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

    results.sort((a, b) => a.totalPrice - b.totalPrice);
    results[0].isBestDeal = true;

    return results;
  };

  /* ========== 趨勢統計 ========== */
  const generateTrendStats = (results) => {
    if (!results.length) return null;
    const hist   = results[0].priceHistory;
    const prices = hist.map(h => h.price);
    const minPrice  = Math.min(...prices);
    const maxPrice  = Math.max(...prices);
    const avgPrice  = Math.round(prices.reduce((a,b) => a+b, 0) / prices.length);
    const todayPrice = prices[prices.length - 1];
    const minDay    = hist[prices.indexOf(minPrice)];
    const isLowestToday = todayPrice === minPrice;

    return {
      minPrice, maxPrice, avgPrice, todayPrice,
      minDay, isLowestToday,
      savings: maxPrice - minPrice,
      trend: prices[prices.length-1] > prices[prices.length-4] ? 'up' : 'down',
    };
  };

  /* ========== 城市搜尋 ========== */
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
    const newPrice  = Math.round(plan.totalPrice * (1 + variation));
    const today     = new Date().toISOString().split('T')[0];
    Storage.addPricePoint(plan.id, newPrice, today);

    const history = Storage.getPriceHistory(plan.id);
    const prices  = history.map(h => h.price);
    const minPrice  = Math.min(...prices);
    const isLowest  = newPrice <= minPrice;

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
