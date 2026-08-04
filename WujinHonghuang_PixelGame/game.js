/**
 * 《無盡洪荒：五行聖境》- 遊戲核心邏輯 (Game Engine v1.4 - Sutra & Merchant)
 */

// ============================================
// 📱 裝置偵測模組 (Device Detection)
// ============================================
const DeviceDetector = {
  STORAGE_KEY: 'hh_device_notice_dismissed',

  // 綜合 User-Agent 特徵與觸控/寬度判斷，偵測是否為手機/平板等行動裝置
  isMobile() {
    const ua = navigator.userAgent || navigator.vendor || '';
    const uaIsMobile = /android|iphone|ipad|ipod|windows phone|mobile|blackberry|opera mini|iemobile/i.test(ua);
    const isTouch = ('ontouchstart' in window) || (navigator.maxTouchPoints > 0);
    const isNarrow = window.innerWidth <= 850;
    // User-Agent 命中即視為行動裝置；否則需同時具備觸控且螢幕較窄才視為行動裝置
    return uaIsMobile || (isTouch && isNarrow);
  },

  apply() {
    const mobile = this.isMobile();
    document.body.classList.toggle('is-mobile-device', mobile);
    document.body.classList.toggle('is-desktop-device', !mobile);

    const notice = document.getElementById('device-notice');
    if (!notice) return;

    const dismissed = sessionStorage.getItem(this.STORAGE_KEY) === '1';
    if (mobile && !dismissed) {
      notice.classList.add('show');
    } else {
      notice.classList.remove('show');
    }
  },

  dismissNotice() {
    sessionStorage.setItem(this.STORAGE_KEY, '1');
    const notice = document.getElementById('device-notice');
    if (notice) notice.classList.remove('show');
  },

  init() {
    this.apply();
    const closeBtn = document.getElementById('device-notice-close');
    if (closeBtn) closeBtn.addEventListener('click', () => this.dismissNotice());
    // 視窗尺寸變動（如旋轉螢幕）時重新判斷，但不強制重開已關閉的提示
    let resizeTimer = null;
    window.addEventListener('resize', () => {
      clearTimeout(resizeTimer);
      resizeTimer = setTimeout(() => {
        const mobile = this.isMobile();
        document.body.classList.toggle('is-mobile-device', mobile);
        document.body.classList.toggle('is-desktop-device', !mobile);
      }, 200);
    });
  }
};

class PixelAudioSynthesizer {
  constructor() {
    this.ctx = null;
    this.enabled = true;
  }

  init() {
    if (!this.ctx) {
      const AudioCtx = window.AudioContext || window.webkitAudioContext;
      if (AudioCtx) this.ctx = new AudioCtx();
    }
    if (this.ctx && this.ctx.state === 'suspended') {
      this.ctx.resume();
    }
  }

  playTone(freq, type = 'square', duration = 0.1, gainVal = 0.1) {
    if (!this.enabled) return;
    try {
      this.init();
      if (!this.ctx) return;

      const osc = this.ctx.createOscillator();
      const gain = this.ctx.createGain();

      osc.type = type;
      osc.frequency.setValueAtTime(freq, this.ctx.currentTime);

      gain.gain.setValueAtTime(gainVal, this.ctx.currentTime);
      gain.gain.exponentialRampToValueAtTime(0.0001, this.ctx.currentTime + duration);

      osc.connect(gain);
      gain.connect(this.ctx.destination);

      osc.start();
      osc.stop(this.ctx.currentTime + duration);
    } catch (e) {
      // 靜默處理
    }
  }

  sfxAttack() { this.playTone(150, 'sawtooth', 0.08, 0.15); }
  sfxHit() { this.playTone(80, 'square', 0.12, 0.2); }
  sfxCrit() {
    this.playTone(400, 'square', 0.05, 0.2);
    setTimeout(() => this.playTone(600, 'sawtooth', 0.1, 0.25), 50);
  }
  sfxCraft() {
    this.playTone(300, 'sine', 0.08, 0.2);
    setTimeout(() => this.playTone(450, 'sine', 0.08, 0.2), 80);
    setTimeout(() => this.playTone(600, 'triangle', 0.2, 0.3), 160);
  }
  sfxLevelUp() {
    const notes = [261, 329, 392, 523];
    notes.forEach((freq, idx) => {
      setTimeout(() => this.playTone(freq, 'triangle', 0.15, 0.25), idx * 80);
    });
  }
  sfxReward() {
    this.playTone(523, 'square', 0.1, 0.2);
    setTimeout(() => this.playTone(659, 'square', 0.2, 0.25), 100);
  }
}

const audioSynth = new PixelAudioSynthesizer();

const REALMS = [
  "練氣初期", "練氣中期", "練氣後期",
  "築基初期", "築基中期", "築基後期",
  "金丹初期", "金丹中期", "金丹後期",
  "元嬰初期", "元嬰中期", "元嬰後期",
  "化神初期", "化神中期", "化神後期",
  "返虛期", "合體期", "大乘期", "洪荒聖人"
];

const ELEMENT_COUNTER = {
  gold: 'wood', wood: 'earth', earth: 'water', water: 'fire', fire: 'gold'
};

const ELEMENT_NAMES = {
  gold: '金系', wood: '木系', water: '水系', fire: '火系', earth: '土系', chaos: '五行混沌', azure: '蒼天全系'
};

// 功法心法 6 大分類資料庫 (Sutras Database)
const ALL_SUTRAS = [
  // ✨ 金系武學
  { id: 'sutra_g1', category: 'gold', quality: '下品', name: '《鐵鋒斬》', price: 150, atk: 12, def: 0, hp: 0, expSpeed: 0, crit: 0.02, desc: '金系基礎劍招，剛猛果決，提升 12 點攻擊力與 2% 會心率。' },
  { id: 'sutra_g2', category: 'gold', quality: '中品', name: '《金罡裂空劍》', price: 600, atk: 28, def: 0, hp: 0, expSpeed: 0, crit: 0.04, desc: '金罡裂空，無堅不摧，提升 28 點攻擊力與 4% 會心率。' },
  { id: 'sutra_g3', category: 'gold', quality: '上品', name: '《紫電裂穹劍》', price: 1800, atk: 55, def: 5, hp: 0, expSpeed: 0, crit: 0.07, desc: '金紫電芒裂穹蒼，提升 55 點攻擊力與 7% 會心率。' },
  { id: 'sutra_g4', category: 'gold', quality: '極品', name: '《太虛戮神劍典》', price: 4500, atk: 100, def: 10, hp: 0, expSpeed: 0, crit: 0.12, desc: '太虛金煞戮神絕學，提升 100 點攻擊力與 12% 會心率！' },

  // 🌿 木系武學
  { id: 'sutra_w1', category: 'wood', quality: '下品', name: '《春生訣》', price: 150, atk: 10, def: 2, hp: 80, expSpeed: 0, crit: 0, desc: '木系生生不息之術，提升 10 點攻擊力與 80 點氣血。' },
  { id: 'sutra_w2', category: 'wood', quality: '中品', name: '《蒼木逢春功》', price: 600, atk: 22, def: 8, hp: 200, expSpeed: 0, crit: 0, desc: '蒼木逢春枯木抽芽，提升 22 點攻擊與 200 點氣血。' },
  { id: 'sutra_w3', category: 'wood', quality: '上品', name: '《參天古木經》', price: 1800, atk: 45, def: 18, hp: 450, expSpeed: 0, crit: 0, desc: '參天古木浩蕩生機，提升 45 點攻擊與 450 點氣血。' },
  { id: 'sutra_w4', category: 'wood', quality: '極品', name: '《不朽扶桑神木經》', price: 4500, atk: 85, def: 35, hp: 900, expSpeed: 0.05, crit: 0, desc: '上古扶桑不朽真意，提升 85 攻擊、900 氣血與 5% 修速！' },

  // 💧 水系武學
  { id: 'sutra_wa1', category: 'water', quality: '下品', name: '《寒流訣》', price: 150, atk: 10, def: 4, hp: 0, expSpeed: 0, crit: 0.01, desc: '水系冰霜綿密，提升 10 點攻擊力與 4 點防禦力。' },
  { id: 'sutra_wa2', category: 'water', quality: '中品', name: '《玄冰破浪訣》', price: 600, atk: 25, def: 12, hp: 0, expSpeed: 0, crit: 0.03, desc: '玄冰破浪柔中帶剛，提升 25 點攻擊力與 12 點防禦力。' },
  { id: 'sutra_wa3', category: 'water', quality: '上品', name: '《北冥玄冰訣》', price: 1800, atk: 50, def: 25, hp: 200, expSpeed: 0, crit: 0.05, desc: '北冥玄冰凍結萬物，提升 50 點攻擊與 25 點防禦。' },
  { id: 'sutra_wa4', category: 'water', quality: '極品', name: '《太陰玄冥絕水典》', price: 4500, atk: 90, def: 45, hp: 400, expSpeed: 0, crit: 0.08, desc: '太陰玄冥絕水威能，提升 90 點攻擊與 45 點防禦！' },

  // 🔥 火系武學
  { id: 'sutra_f1', category: 'fire', quality: '下品', name: '《炎陽拳》', price: 150, atk: 15, def: 0, hp: 0, expSpeed: 0, crit: 0.02, desc: '火系熾熱拳招，提升 15 點攻擊力與 2% 會心率。' },
  { id: 'sutra_f2', category: 'fire', quality: '中品', name: '《烈陽焚天槍》', price: 600, atk: 32, def: 0, hp: 0, expSpeed: 0, crit: 0.05, desc: '烈陽焚天霸道槍法，提升 32 點攻擊力與 5% 會心率。' },
  { id: 'sutra_f3', category: 'fire', quality: '上品', name: '《九幽煉獄焚天訣》', price: 1800, atk: 60, def: 0, hp: 0, expSpeed: 0, crit: 0.08, desc: '九幽真火煉獄焚天，提升 60 點攻擊力與 8% 會心率。' },
  { id: 'sutra_f4', category: 'fire', quality: '極品', name: '《三昧真火焚世訣》', price: 4500, atk: 110, def: 0, hp: 0, expSpeed: 0, crit: 0.15, desc: '三昧真火焚盡萬法，提升 110 點攻擊力與 15% 會心率！' },

  // 🪨 土系武學
  { id: 'sutra_e1', category: 'earth', quality: '下品', name: '《磐石拳》', price: 150, atk: 8, def: 8, hp: 50, expSpeed: 0, crit: 0, desc: '土系堅如磐石，提升 8 點攻擊與 8 點防禦。' },
  { id: 'sutra_e2', category: 'earth', quality: '中品', name: '《厚土鎮嶽印》', price: 600, atk: 20, def: 20, hp: 150, expSpeed: 0, crit: 0, desc: '厚土鎮嶽穩如泰山，提升 20 點攻擊與 20 點防禦。' },
  { id: 'sutra_e3', category: 'earth', quality: '上品', name: '《不動玄嶽印》', price: 1800, atk: 40, def: 42, hp: 350, expSpeed: 0, crit: 0, desc: '不動玄嶽化身金剛，提升 40 點攻擊與 42 點防禦。' },
  { id: 'sutra_e4', category: 'earth', quality: '極品', name: '《后土鎮世神印》', price: 4500, atk: 75, def: 80, hp: 700, expSpeed: 0, crit: 0, desc: '后土鎮世威鎮八荒，提升 75 點攻擊與 80 點防禦！' },

  // 🧘 內功心法
  { id: 'sutra_i1', category: 'internal', quality: '下品', name: '《洗髓基礎功》', price: 200, atk: 0, def: 6, hp: 100, expSpeed: 0.05, crit: 0, desc: '入門洗髓易筋，提升 5% 修練速度與 6 點防禦。' },
  { id: 'sutra_i2', category: 'internal', quality: '中品', name: '《太乙洗髓經》', price: 800, atk: 0, def: 18, hp: 200, expSpeed: 0.10, crit: 0, desc: '太乙周天淬體，提升 10% 修練速度與 18 點防禦。' },
  { id: 'sutra_i3', category: 'internal', quality: '上品', name: '《九轉太乙玄經》', price: 2000, atk: 15, def: 35, hp: 400, expSpeed: 0.18, crit: 0.02, desc: '九轉太乙靈氣灌頂，提升 18% 修速、35 防禦與 15 攻擊。' },
  { id: 'sutra_i4', category: 'internal', quality: '中品', name: '《紫霄神雷功》', price: 1000, atk: 10, def: 22, hp: 150, expSpeed: 0.12, crit: 0.03, desc: '紫霄雷霆淬鍊肉身，提升 12% 修速與 22 點防禦。' },
  { id: 'sutra_i5', category: 'internal', quality: '上品', name: '《混沌吐納術》', price: 3200, atk: 25, def: 50, hp: 500, expSpeed: 0.22, crit: 0.04, desc: '吐納天地混沌之氣，提升 22% 修速與 50 點防禦。' },
  { id: 'sutra_i6', category: 'internal', quality: '極品', name: '《洪荒無極心經》', price: 8888, atk: 50, def: 90, hp: 1000, expSpeed: 0.35, crit: 0.08, desc: '洪荒第一無極心法，提升 35% 修練速度與全屬性爆發！' },

  // 🧪 丹道修練秘法
  { id: 'sutra_alch1', category: 'alchemy', quality: '凡品', name: '《草木養丹術》', price: 300, alchSpeed: 0.20, desc: '丹道入門秘法！所有丹爐煉化速度提升 +20%！' },
  { id: 'sutra_alch2', category: 'alchemy', quality: '中品', name: '《神農百草訣》', price: 1200, alchSpeed: 0.40, desc: '掌控百草靈性！丹爐煉化速度提升 +40%！' },
  { id: 'sutra_alch3', category: 'alchemy', quality: '上品', name: '《三昧真火煉丹心經》', price: 3800, alchSpeed: 0.70, desc: '引三昧真火煉丹！丹爐煉化速度大幅提升 +70%！' },
  { id: 'sutra_alch4', category: 'alchemy', quality: '極品', name: '《太上九轉造化丹經》', price: 12000, alchSpeed: 1.20, desc: '太上聖人煉丹大道！丹爐煉化速度提升 +120%！' },

  // 🔨 器道鍛造秘法
  { id: 'sutra_forge1', category: 'forge', quality: '凡品', name: '《百煉金石訣》', price: 300, forgeSpeed: 0.20, desc: '器道入門心法！所有鍛造爐開爐速度提升 +20%！' },
  { id: 'sutra_forge2', category: 'forge', quality: '中品', name: '《天工開物器經》', price: 1200, forgeSpeed: 0.40, desc: '參透天工造化！鍛造爐開爐速度提升 +40%！' },
  { id: 'sutra_forge3', category: 'forge', quality: '上品', name: '《神冶歐冶子心法》', price: 3800, forgeSpeed: 0.70, desc: '神冶至高心訣！鍛造爐開爐速度大幅提升 +70%！' },
  { id: 'sutra_forge4', category: 'forge', quality: '極品', name: '《太初乾坤鍛器聖典》', price: 12000, forgeSpeed: 1.20, desc: '乾坤開天鍛器大道！鍛造爐開爐速度提升 +120%！' }
];

// 九轉煉丹房配方 (Alchemy Recipes)
const PILL_RECIPES = [
  {
    id: 'recipe_small_hp',
    name: '《小還丹》',
    quality: '下品',
    icon: '💊',
    coinsCost: 50,
    materials: { lingzhi: 2, baicao: 1 },
    desc: '吞服後立即恢復 50% 最大氣血！',
    action: (p) => {
      const heal = Math.floor(p.maxHp * 0.5);
      p.hp = Math.min(p.maxHp, p.hp + heal);
      return `吞服【小還丹】，氣血瞬間恢復 ${heal} 點！`;
    }
  },
  {
    id: 'recipe_big_hp',
    name: '《大還丹》',
    quality: '中品',
    icon: '🔴',
    coinsCost: 150,
    materials: { lingzhi: 4, zhusha: 2 },
    desc: '仙家急救聖藥，吞服後氣血直接全滿！',
    action: (p) => {
      p.hp = p.maxHp;
      return `吞服【大還丹】，氣血完全恢復至滿血！`;
    }
  },
  {
    id: 'recipe_juqi_exp',
    name: '《聚氣丹》',
    quality: '下品',
    icon: '🔵',
    coinsCost: 100,
    materials: { baicao: 3, zhusha: 1 },
    desc: '凝聚天地靈氣，使用後獲得 +300 點修為！',
    action: (p) => {
      p.exp += 300;
      if (p.exp >= p.maxExp) levelUp();
      return `煉服【聚氣丹】，增加 300 點修為！`;
    }
  },
  {
    id: 'recipe_ningshen_exp',
    name: '《凝神丹》',
    quality: '中品',
    icon: '🟣',
    coinsCost: 300,
    materials: { zhusha: 3, longkui: 2 },
    desc: '凝神靜心，使用後獲得 +1000 點修為！',
    action: (p) => {
      p.exp += 1000;
      if (p.exp >= p.maxExp) levelUp();
      return `煉服【凝神丹】，修為大漲 1000 點！`;
    }
  },
  {
    id: 'recipe_zhuji_break',
    name: '《築基保底突破丹》',
    quality: '上品',
    icon: '🌟',
    coinsCost: 600,
    materials: { longkui: 3, renshen: 1 },
    desc: '渡劫突破專用神丹！增加 +2500 修為並永久 +10 攻擊力！',
    action: (p) => {
      p.exp += 2500;
      p.atk += 10;
      if (p.exp >= p.maxExp) levelUp();
      return `服下【築基保底突破丹】，修為+2500，永久攻擊力+10！`;
    }
  },
  {
    id: 'recipe_cuiling_wash',
    name: '《先天淬靈洗髓丹》',
    quality: '極品',
    icon: '✨',
    coinsCost: 1000,
    materials: { renshen: 2, longkui: 2, zhusha: 2 },
    desc: '洗髓易筋！永久提升 5% 修練速度與 15 點防禦力！',
    action: (p) => {
      p.expSpeed = (p.expSpeed || 1.0) + 0.05;
      p.def += 15;
      return `服下【先天淬靈洗髓丹】，靈根獲洗髓升級！修速+5%，防禦+15！`;
    }
  },
  
  // 🟢 氣血/修為恢復速度丹藥體系 (多品級對應 %)
  {
    id: 'recipe_regen_1',
    name: '《養氣丹》',
    quality: '凡品',
    icon: '🌿',
    coinsCost: 80,
    materials: { lingzhi: 2 },
    desc: '基礎養氣，使用後氣血恢復速度提升 +20%！',
    action: (p) => {
      p.activeRegenPill = { name: '養氣丹', boost: 0.20, qualityColor: '#888888' };
      updateUI();
      return `服下【養氣丹】，氣血恢復速度提升 +20%！`;
    }
  },
  {
    id: 'recipe_regen_2',
    name: '《生化丹》',
    quality: '下品',
    icon: '🌱',
    coinsCost: 180,
    materials: { lingzhi: 3, baicao: 2 },
    desc: '生化萬物，使用後氣血恢復速度提升 +40%！',
    action: (p) => {
      p.activeRegenPill = { name: '生化丹', boost: 0.40, qualityColor: '#2ecc71' };
      updateUI();
      return `服下【生化丹】，氣血恢復速度提升 +40%！`;
    }
  },
  {
    id: 'recipe_regen_3',
    name: '《培元丹》',
    quality: '中品',
    icon: '🍃',
    coinsCost: 350,
    materials: { baicao: 4, zhusha: 2 },
    desc: '固本培元，使用後氣血恢復速度大增 +70%！',
    action: (p) => {
      p.activeRegenPill = { name: '培元丹', boost: 0.70, qualityColor: '#3498db' };
      updateUI();
      return `服下【培元丹】，氣血恢復速度提升 +70%！`;
    }
  },
  {
    id: 'recipe_regen_4',
    name: '《九轉大還丹》',
    quality: '上品',
    icon: '✨',
    coinsCost: 650,
    materials: { zhusha: 3, longkui: 3 },
    desc: '九轉玄功！使用後氣血恢復速度暴增 +110%！',
    action: (p) => {
      p.activeRegenPill = { name: '九轉大還丹', boost: 1.10, qualityColor: '#9b59b6' };
      updateUI();
      return `服下【九轉大還丹】，氣血恢復速度暴增 +110%！`;
    }
  },
  {
    id: 'recipe_regen_5',
    name: '《造化聖血丹》',
    quality: '極品',
    icon: '🩸',
    coinsCost: 1200,
    materials: { longkui: 4, renshen: 2 },
    desc: '造化神力！使用後氣血恢復速度狂暴 +160%！',
    action: (p) => {
      p.activeRegenPill = { name: '造化聖血丹', boost: 1.60, qualityColor: '#f1c40f' };
      updateUI();
      return `服下【造化聖血丹】，氣血恢復速度狂暴 +160%！`;
    }
  },
  {
    id: 'recipe_regen_6',
    name: '《不滅不死神丹》',
    quality: '神品',
    icon: '👑',
    coinsCost: 2500,
    materials: { renshen: 5, longkui: 5, zhusha: 5 },
    desc: '不死不滅！使用後氣血恢復速度極限暴漲 +230%！',
    action: (p) => {
      p.activeRegenPill = { name: '不滅不死神丹', boost: 2.30, qualityColor: '#e74c3c' };
      updateUI();
      return `服下【不滅不死神丹】，氣血恢復速度極限暴漲 +230%！`;
    }
  },

  // ⚡ 加速自動掛機點擊速度丹藥體系 (多品級對應 %)
  {
    id: 'recipe_speed_1',
    name: '《疾風丹》',
    quality: '凡品',
    icon: '💨',
    coinsCost: 100,
    materials: { baicao: 2 },
    desc: '疾風加持，自動掛機攻速提升 +15%！',
    action: (p) => {
      p.activeSpeedPill = { name: '疾風丹', boost: 0.15, qualityColor: '#888888' };
      if (isAutoBattling) { toggleAutoBattle(); toggleAutoBattle(); }
      updateUI();
      return `服下【疾風丹】，自動掛機攻速提升 +15%！`;
    }
  },
  {
    id: 'recipe_speed_2',
    name: '《迅捷丹》',
    quality: '下品',
    icon: '⚡',
    coinsCost: 220,
    materials: { baicao: 3, zhusha: 2 },
    desc: '迅捷如風，自動掛機攻速提升 +30%！',
    action: (p) => {
      p.activeSpeedPill = { name: '迅捷丹', boost: 0.30, qualityColor: '#2ecc71' };
      if (isAutoBattling) { toggleAutoBattle(); toggleAutoBattle(); }
      updateUI();
      return `服下【迅捷丹】，自動掛機攻速提升 +30%！`;
    }
  },
  {
    id: 'recipe_speed_3',
    name: '《神行丹》',
    quality: '中品',
    icon: '🏃',
    coinsCost: 450,
    materials: { zhusha: 4, longkui: 2 },
    desc: '神行千里！自動掛機攻速大增 +50% (頻率 1.5 倍)！',
    action: (p) => {
      p.activeSpeedPill = { name: '神行丹', boost: 0.50, qualityColor: '#3498db' };
      if (isAutoBattling) { toggleAutoBattle(); toggleAutoBattle(); }
      updateUI();
      return `服下【神行丹】，自動掛機攻速大增 +50%！`;
    }
  },
  {
    id: 'recipe_speed_4',
    name: '《縮地成寸丹》',
    quality: '上品',
    icon: '🌀',
    coinsCost: 850,
    materials: { longkui: 4, renshen: 2 },
    desc: '空間折疊！自動掛機攻速暴增 +75%！',
    action: (p) => {
      p.activeSpeedPill = { name: '縮地成寸丹', boost: 0.75, qualityColor: '#9b59b6' };
      if (isAutoBattling) { toggleAutoBattle(); toggleAutoBattle(); }
      updateUI();
      return `服下【縮地成寸丹】，自動掛機攻速暴增 +75%！`;
    }
  },
  {
    id: 'recipe_speed_5',
    name: '《太虛光陰丹》',
    quality: '極品',
    icon: '⏳',
    coinsCost: 1500,
    materials: { renshen: 4, longkui: 4, zhusha: 3 },
    desc: '光陰逆轉！自動掛機攻速狂暴 +100% (攻速翻倍)！',
    action: (p) => {
      p.activeSpeedPill = { name: '太虛光陰丹', boost: 1.00, qualityColor: '#f1c40f' };
      if (isAutoBattling) { toggleAutoBattle(); toggleAutoBattle(); }
      updateUI();
      return `服下【太虛光陰丹】，自動掛機攻速狂暴翻倍 +100%！`;
    }
  },
  {
    id: 'recipe_speed_6',
    name: '《天道流光神丹》',
    quality: '神品',
    icon: '🌌',
    coinsCost: 3000,
    materials: { renshen: 6, longkui: 6, zhusha: 6 },
    desc: '天道流光！自動掛機攻速極限暴增 +150% (超高速光速殘影)！',
    action: (p) => {
      p.activeSpeedPill = { name: '天道流光神丹', boost: 1.50, qualityColor: '#e74c3c' };
      if (isAutoBattling) { toggleAutoBattle(); toggleAutoBattle(); }
      updateUI();
      return `服下【天道流光神丹】，自動掛機攻速極限暴漲 +150%！`;
    }
  },
  {
    id: 'recipe_protect_pill',
    name: '《保具定海丹》',
    quality: '極品',
    icon: '🛡️',
    coinsCost: 2000,
    materials: { renshen: 3, longkui: 3, zhusha: 3, lingzhi: 5 },
    desc: '定海保具秘丹！五行精煉法寶失敗時自動消耗，100% 避免法寶損毀碎裂！',
    action: (p) => {
      return `【保具定海丹】為五行法寶精煉專用保底丹藥，精煉時將自動為您庇護法器！`;
    }
  }
];

// 坊市商鋪物品列表 (Shop Items)
const SHOP_ITEMS = [
  { id: 'shop_pill_small', name: '回氣丹 ×1', icon: '💊', price: 200, category: 'pill', desc: '回復 50% 氣血', action: () => buyPill() },
  { id: 'shop_herb_lingzhi', name: '草藥·靈芝草 ×1', icon: '🌿', price: 80, category: 'herb', key: 'lingzhi' },
  { id: 'shop_herb_baicao', name: '草藥·百草露 ×1', icon: '💧', price: 80, category: 'herb', key: 'baicao' },
  { id: 'shop_herb_zhusha', name: '草藥·硃砂果 ×1', icon: '🍎', price: 150, category: 'herb', key: 'zhusha' },
  { id: 'shop_herb_longkui', name: '草藥·龍葵花 ×1', icon: '🌸', price: 250, category: 'herb', key: 'longkui' },
  { id: 'shop_herb_renshen', name: '靈藥·千年人參 ×1', icon: '🥕', price: 500, category: 'herb', key: 'renshen' },
  { id: 'shop_mat_gold', name: '神材·金精石 ×3', icon: '✨', price: 250, category: 'material', key: 'goldMat', count: 3 },
  { id: 'shop_mat_wood', name: '神材·神木芯 ×3', icon: '🌿', price: 250, category: 'material', key: 'woodMat', count: 3 },
  { id: 'shop_mat_water', name: '神材·玄冰髓 ×3', icon: '💧', price: 250, category: 'material', key: 'waterMat', count: 3 },
  { id: 'shop_mat_fire', name: '神材·朱雀羽 ×3', icon: '🔥', price: 250, category: 'material', key: 'fireMat', count: 3 },
  { id: 'shop_mat_earth', name: '神材·息壤土 ×3', icon: '🪨', price: 250, category: 'material', key: 'earthMat', count: 3 }
];

const DUNGEONS = [
  { 
    id: 0, name: "太初森林", reqLevel: 1, element: 'wood',
    monsters: [
      { name: "百年妖狼", icon: "🐺" },
      { name: "千年樹精", icon: "🌲" },
      { name: "碧玉毒蛛", icon: "🕷️" },
      { name: "青林巨蟒", icon: "🐍" }
    ],
    bosses: [
      { name: "太古扶桑神樹皇", icon: "🌳" },
      { name: "萬年九尾天狐王", icon: "🦊" }
    ],
    baseExp: 30, baseCoin: 20, matDrop: 'woodMat' 
  },
  { 
    id: 1, name: "九幽寒潭", reqLevel: 10, element: 'water',
    monsters: [
      { name: "玄冰白蛇", icon: "🐍" },
      { name: "寒潭巨鱷", icon: "🐊" },
      { name: "九幽水鬼", icon: "👻" },
      { name: "深海冰水獸", icon: "🦑" }
    ],
    bosses: [
      { name: "九頭相柳水魔皇", icon: "🐍" },
      { name: "太陰玄冰冰龍皇", icon: "🐉" }
    ],
    baseExp: 35, baseCoin: 25, matDrop: 'waterMat' 
  },
  { 
    id: 2, name: "熔岩地獄", reqLevel: 25, element: 'fire',
    monsters: [
      { name: "烈焰火狐", icon: "🦊" },
      { name: "地獄熔岩犬", icon: "🐕" },
      { name: "赤炎火魔", icon: "👹" },
      { name: "朱雀幼獸", icon: "🦅" }
    ],
    bosses: [
      { name: "三足金烏祝融神", icon: "☀️" },
      { name: "滅世地獄焚天魔尊", icon: "🔥" }
    ],
    baseExp: 40, baseCoin: 30, matDrop: 'fireMat' 
  },
  { 
    id: 3, name: "崑崙金山", reqLevel: 40, element: 'gold',
    monsters: [
      { name: "白虎聖獸", icon: "🐅" },
      { name: "金甲神兵", icon: "💂" },
      { name: "金晶巨雕", icon: "🦅" },
      { name: "太乙劍靈", icon: "⚔️" }
    ],
    bosses: [
      { name: "太初白虎戮天尊", icon: "🐅" },
      { name: "紫霄劍聖金神皇", icon: "⚔️" }
    ],
    baseExp: 45, baseCoin: 35, matDrop: 'goldMat' 
  },
  { 
    id: 4, name: "不周天柱", reqLevel: 60, element: 'earth',
    monsters: [
      { name: "混沌麒麟", icon: "🐉" },
      { name: "息壤巨人", icon: "🗿" },
      { name: "山嶽神龜", icon: "🐢" },
      { name: "不周山靈", icon: "🧙‍♂️" }
    ],
    bosses: [
      { name: "后土鎮世黃龍皇", icon: "🐉" },
      { name: "不周天山金剛魔尊", icon: "🦍" }
    ],
    baseExp: 50, baseCoin: 40, matDrop: 'earthMat' 
  },
  { 
    id: 5, name: "5行混沌秘境", reqLevel: 1, element: 'chaos',
    monsters: [
      { name: "五行守衛狼", icon: "🐺", elem: 'wood' },
      { name: "混沌水龍", icon: "🐉", elem: 'water' },
      { name: "金光巨虎", icon: "🐅", elem: 'gold' },
      { name: "赤焰朱雀", icon: "🦅", elem: 'fire' },
      { name: "息壤魔尊", icon: "🗿", elem: 'earth' }
    ],
    bosses: [
      { name: "五行混沌大帝尊", icon: "👑", elem: 'chaos' },
      { name: "太初天道執法聖皇", icon: "🌌", elem: 'azure' }
    ],
    baseExp: 55, baseCoin: 45, matDrop: 'all'
  }
];

const CHAOS_TIERS = [
  { name: "1~15級 (凡階)", minLvl: 1, maxLvl: 15, scale: 1.0, prefix: "凡階" },
  { name: "15~30級 (靈階)", minLvl: 15, maxLvl: 30, scale: 2.2, prefix: "靈階" },
  { name: "30~50級 (地階)", minLvl: 30, maxLvl: 50, scale: 4.5, prefix: "地階" },
  { name: "50~70級 (天階)", minLvl: 50, maxLvl: 70, scale: 8.5, prefix: "天階" },
  { name: "70~100級 (聖階)", minLvl: 70, maxLvl: 100, scale: 16.0, prefix: "聖階" }
];

const QUALITIES = [
  { level: 1, name: "凡品", color: "#888888", multiplier: 1.0 },
  { level: 2, name: "良品", color: "#2ecc71", multiplier: 1.4 },
  { level: 3, name: "上品", color: "#3498db", multiplier: 1.9 },
  { level: 4, name: "極品", color: "#9b59b6", multiplier: 2.6 },
  { level: 5, name: "仙品", color: "#f1c40f", multiplier: 3.5 },
  { level: 6, name: "神品", color: "#e74c3c", multiplier: 5.0 }
];

let player = {
  name: "洪荒修真者",
  element: "gold",
  
  rootType: "single",
  rootName: "天單靈根",
  expSpeed: 1.0,
  statMult: 1.0,
  
  level: 1,
  exp: 0,
  maxExp: 100,
  coins: 500,
  
  hp: 200, maxHp: 200,
  mp: 100, maxMp: 100,
  atk: 35,
  def: 10,
  speed: 10,
  critRate: 0.10,
  specialRate: 0.15,
  
  // 已參悟心法清單
  purchasedSutras: [],
  
  materials: {
    goldMat: 10,
    woodMat: 10,
    waterMat: 10,
    fireMat: 10,
    earthMat: 10
  },
  
  inventory: [],
  equipped: { weapon: null, armor: null, accessory: null, pill: null },
  usedCodes: [],
  pills: 0,
  herbs: {
    lingzhi: 3,
    baicao: 3,
    zhusha: 1,
    longkui: 1,
    renshen: 0
  },
  
  // 實體丹爐與鍛造爐陣列 (最多各 5 個爐位)
  alchFurnaces: [
    { id: 1, name: '凡品草木爐', level: 1, speedMult: 1.0, status: 'idle', recipeId: null, startTime: 0, duration: 0 },
    null, null, null, null
  ],
  forgeFurnaces: [
    { id: 1, name: '凡品石木爐', level: 1, speedMult: 1.0, status: 'idle', forgeData: null, startTime: 0, duration: 0 },
    null, null, null, null
  ]
};

let drawnRoot = null;
let currentDungeonIdx = 5;
let currentChaosTier = 0;
let autoBattleInterval = null;
let isAutoBattling = false;
let currentMonster = null;
let currentForgeMode = 'single';
let currentForgeElement = 'gold';
let isMeditating = false;
let meditateInterval = null;
let regenInterval = null;
var currentMerchantItems = [];

// ============================================
// 天道 GM 控制台核心動態參數
// ============================================
let GAME_CONFIG = {
  eventRate: 0.06,          // 秘境機緣觸發率 (預設 6%)
  merchantRate: 0.20,       // 神秘商人降臨率 (預設 20%)
  expMult: 1.0,             // 修為獲得倍率 (預設 1.0x)
  coinMult: 1.0,            // 靈石獲得倍率 (預設 1.0x)
  herbDropRate: 0.20,       // 靈藥草藥掉落率 (預設 20%)
  meditateMult: 5,          // 打坐恢復倍率 (預設 5x)
  azureRate: 0.05,          // 蒼靈根機率 (預設 5%)
  monsterHpMult: 2.0,       // 怪物血量強度倍率 (預設 2.0x)
  monsterAtkMult: 1.8,      // 怪物攻擊強度倍率 (預設 1.8x)
  suppressionMult: 1.5,     // 越級挑戰懲罰倍率 (預設 1.5x)
  baseCritRate: 0.10,       // 基礎會心一擊機率 (預設 10%)
  critDamageMult: 2.0,      // 會心一擊傷害倍率 (預設 2.0x)
  salvageCoinMult: 1.0,     // 熔練靈石返還倍率 (預設 1.0x)
  alchBaseTime: 60,         // 煉丹基礎開爐時間 (秒)
  forgeBaseTime: 60,        // 鍛造基礎開爐時間 (秒)
  uiScale: 1.0,             // 遊戲整體 UI 與文字縮放比例 (預設 1.0 / 100%)
  bossSpawnRate: 0.20,      // 洪荒首領 BOSS 遭遇率 (預設 20%)
  bossDropEquipRate: 0.50,  // 擊敗首領 BOSS 法寶爆裝率 (預設 50%)
  normalDropEquipRate: 0.10 // 擊敗普通怪物法寶爆裝率 (預設 10%)
};

function applyUIScale() {
  const scale = GAME_CONFIG.uiScale || 1.0;
  document.documentElement.style.setProperty('--ui-scale', scale);
}

function previewUIScale(val) {
  const pct = parseFloat(val || 100);
  const scale = Math.max(0.7, Math.min(1.5, pct / 100));
  document.documentElement.style.setProperty('--ui-scale', scale);
}

let isGMUnlocked = false;

// 五行屬性對應材料key、材料名稱、裝備前綴
const ELEMENT_MAT_MAP = {
  gold:  { matKey: 'goldMat',  matName: '金精石', icon: '✨', prefix: '金煞', sheng: 'water', ke: 'wood' },
  wood:  { matKey: 'woodMat',  matName: '神木芯', icon: '🌿', prefix: '蒼木', sheng: 'fire',  ke: 'earth' },
  water: { matKey: 'waterMat', matName: '玄冰髓', icon: '💧', prefix: '玄冰', sheng: 'gold',  ke: 'fire' },
  fire:  { matKey: 'fireMat',  matName: '朱雀羽', icon: '🔥', prefix: '赤炎', sheng: 'earth', ke: 'gold' },
  earth: { matKey: 'earthMat', matName: '息壤土', icon: '🪨', prefix: '厚土', sheng: 'wood',  ke: 'water' }
};

document.addEventListener('DOMContentLoaded', () => {
  DeviceDetector.init();
  loadGame();
  applyUIScale();
  loadGMConfigToInputs();
  setupEventListeners();
  setupDragAndDrop();
  updateUI();
  selectDungeon(5);
  startRegenTimer();
  startFurnaceTimer();
});

function setupEventListeners() {
  document.querySelectorAll('.tab-btn').forEach(btn => {
    btn.addEventListener('click', () => {
      document.querySelectorAll('.tab-btn').forEach(b => b.classList.remove('active'));
      document.querySelectorAll('.tab-content').forEach(c => c.classList.remove('active'));
      
      btn.classList.add('active');
      const tabId = btn.getAttribute('data-tab');
      document.getElementById(`tab-${tabId}`).classList.add('active');
      audioSynth.playTone(300, 'square', 0.05);

      if (tabId === 'sutra') renderSutraTab();
      if (tabId === 'alchemy') renderAlchemyTab();
      if (tabId === 'shop') renderShopTab();
      if (tabId === 'forge') populateRefineEquipmentDropdown();
    });
  });

  document.getElementById('toggle-crt').addEventListener('click', () => {
    const crt = document.querySelector('.crt-overlay');
    crt.style.display = crt.style.display === 'none' ? 'block' : 'none';
  });

  document.getElementById('toggle-sound').addEventListener('click', (e) => {
    audioSynth.enabled = !audioSynth.enabled;
    e.target.textContent = audioSynth.enabled ? '🔊 音效:開' : '🔇 音效:關';
  });

  const giftModal = document.getElementById('gift-modal');
  document.getElementById('btn-giftcode').addEventListener('click', () => giftModal.classList.add('show'));
  document.getElementById('close-gift-modal').addEventListener('click', () => giftModal.classList.remove('show'));
  giftModal.addEventListener('click', (e) => { if (e.target === giftModal) giftModal.classList.remove('show'); });
  document.getElementById('btn-claim-code').addEventListener('click', claimGiftCode);

  // 神秘商人 Modal (告辭時銷毀商人)
  const merchantModal = document.getElementById('merchant-modal');
  document.getElementById('btn-open-merchant').addEventListener('click', () => {
    generateMerchantItems();
    renderMerchantShop();
    merchantModal.classList.add('show');
  });
  
  const closeMerchantFunc = () => {
    merchantModal.classList.remove('show');
    // 告辭離開商人，商人離場
    document.getElementById('merchant-banner').classList.remove('show');
    currentMerchantItems = [];
    addLog(`【告辭】與神秘商人作揖告別，商人飄然離去...`, 'log-system');
  };

  document.getElementById('close-merchant-modal').addEventListener('click', closeMerchantFunc);
  document.getElementById('btn-close-merchant-x').addEventListener('click', closeMerchantFunc);
  merchantModal.addEventListener('click', (e) => {
    if (e.target === merchantModal) closeMerchantFunc();
  });

  // 首領爆裝 Modal 彈窗事件
  const bossDropModal = document.getElementById('boss-drop-modal');
  let currentDropEquipItem = null;

  document.getElementById('btn-boss-drop-equip-now').addEventListener('click', () => {
    if (currentDropEquipItem) {
      equipItem(currentDropEquipItem.id);
      addLog(`【⚡ 佩戴成功】成功穿戴爆出的【${currentDropEquipItem.name}】！屬性已大增！`, 'log-crit');
    }
    bossDropModal.classList.remove('show');
  });

  document.getElementById('btn-boss-drop-close-modal').addEventListener('click', () => {
    bossDropModal.classList.remove('show');
  });

  // 一鍵自動裝備與一鍵整理背包按鈕綁定
  const btnAutoEquip = document.getElementById('btn-auto-equip-best');
  if (btnAutoEquip) {
    btnAutoEquip.addEventListener('click', autoEquipBestItems);
  }

  const btnSortInv = document.getElementById('btn-sort-inventory');
  if (btnSortInv) {
    btnSortInv.addEventListener('click', sortInventoryByStats);
  }

  // 服用槽中丹藥按鈕綁定與自動服丹門檻初始化
  const btnUsePill = document.getElementById('btn-use-equipped-pill');
  if (btnUsePill) {
    btnUsePill.addEventListener('click', useEquippedPill);
  }

  const inputPillHpEl = document.getElementById('input-auto-pill-hp-pct');
  if (inputPillHpEl) {
    inputPillHpEl.value = player.autoPillHpPercent !== undefined ? player.autoPillHpPercent : 50;
  }

  document.getElementById('btn-save').addEventListener('click', () => {
    saveGame();
    audioSynth.sfxReward();
    addLog('【系統】存檔成功！進度已儲存。', 'log-system');
  });
  document.getElementById('btn-reset').addEventListener('click', () => {
    localStorage.removeItem('wujin_honghuang_save');
    location.reload();
  });

  document.getElementById('btn-draw-root').addEventListener('click', drawSpiritualRoot);
  document.getElementById('btn-confirm-class').addEventListener('click', confirmCharacterClass);

  document.getElementById('btn-manual-attack').addEventListener('click', executeBattleRound);
  document.getElementById('btn-toggle-auto').addEventListener('click', toggleAutoBattle);

  document.getElementById('btn-forge').addEventListener('click', startForgeInFurnace);
  document.getElementById('btn-salvage-all').addEventListener('click', salvageCommonItems);

  // 打坐調息 / 服用槽中丹藥
  document.getElementById('btn-meditate').addEventListener('click', toggleMeditate);
  document.getElementById('btn-use-equipped-pill').addEventListener('click', useEquippedPill);

  // 天道 GM 設定解鎖與保存
  document.getElementById('btn-unlock-gm').addEventListener('click', unlockGMSettings);
  document.getElementById('btn-save-gm-config').addEventListener('click', saveGMSettings);
  document.getElementById('btn-reset-gm-config').addEventListener('click', resetGMConfig);
}

function drawSpiritualRoot() {
  const cardBox = document.getElementById('gacha-card');
  const btnDraw = document.getElementById('btn-draw-root');
  const btnConfirm = document.getElementById('btn-confirm-class');

  btnDraw.disabled = true;
  cardBox.classList.add('spinning');
  audioSynth.sfxCraft();

  setTimeout(() => {
    cardBox.classList.remove('spinning');
    btnDraw.disabled = false;

    const rand = Math.random() * 100;
    const elements = ['gold', 'wood', 'water', 'fire', 'earth'];
    const primaryElem = elements[Math.floor(Math.random() * elements.length)];

    let result = {};
    const azureThreshold = (GAME_CONFIG.azureRate || 0.05) * 100;
    if (rand < azureThreshold) {
      result = {
        type: 'azure',
        name: '絕品·蒼靈根',
        element: 'azure',
        expSpeed: 1.2,
        statMult: 1.15,
        icon: '🌌',
        desc: '天道全通！修練速度 120% | 全屬性 +15%！'
      };
      cardBox.className = 'gacha-card-box azure-card';
      audioSynth.sfxLevelUp();
    } else if (rand < azureThreshold + 40) {
      result = {
        type: 'single',
        name: `天單靈根 (${ELEMENT_NAMES[primaryElem]})`,
        element: primaryElem,
        expSpeed: 1.0,
        statMult: 1.0,
        icon: { gold:'⚔️', wood:'🌿', water:'💧', fire:'🔥', earth:'🪨' }[primaryElem],
        desc: '專精單一屬性。修練速度 100%'
      };
      cardBox.className = 'gacha-card-box';
      audioSynth.sfxReward();
    } else if (rand < 80) {
      result = {
        type: 'dual',
        name: `雙靈根 (${ELEMENT_NAMES[primaryElem]})`,
        element: primaryElem,
        expSpeed: 0.8,
        statMult: 1.0,
        icon: '☯️',
        desc: '兼具雙系。修練速度 80%'
      };
      cardBox.className = 'gacha-card-box';
      audioSynth.sfxHit();
    } else {
      result = {
        type: 'triple',
        name: `三靈根 (${ELEMENT_NAMES[primaryElem]})`,
        element: primaryElem,
        expSpeed: 0.6,
        statMult: 1.0,
        icon: '🔱',
        desc: '三系同修。修練速度 60%'
      };
      cardBox.className = 'gacha-card-box';
      audioSynth.sfxHit();
    }

    drawnRoot = result;

    document.getElementById('gacha-icon').textContent = result.icon;
    document.getElementById('gacha-title').textContent = result.name;
    document.getElementById('gacha-speed').textContent = `修速: ${Math.floor(result.expSpeed * 100)}%`;
    document.getElementById('gacha-desc').textContent = result.desc;

    btnConfirm.disabled = false;
    btnDraw.textContent = '🔄 重新感應 (Re-roll)';
  }, 600);
}

function confirmCharacterClass() {
  if (!drawnRoot) return;

  player.rootType = drawnRoot.type;
  player.rootName = drawnRoot.name;
  player.element = drawnRoot.element;
  player.expSpeed = drawnRoot.expSpeed;
  player.statMult = drawnRoot.statMult;

  const inputName = document.getElementById('player-name-input').value.trim();
  if (inputName) player.name = inputName;

  document.getElementById('class-select-modal').classList.remove('show');
  addLog(`【踏入修途】尊者 ${player.name} 覺醒了【${player.rootName}】踏入洪荒大地上！`, 'log-crit');

  recalculatePlayerStats();
  updateUI();
  saveGame();
}

function selectDungeon(idx) {
  currentDungeonIdx = idx;
  
  document.querySelectorAll('.dungeon-card').forEach(card => {
    const cardId = parseInt(card.getAttribute('data-id'));
    card.classList.toggle('active', cardId === idx);
  });
  
  // 所有秘境均開放試煉階級選單
  const chaosSelector = document.getElementById('chaos-level-selector');
  if (chaosSelector) chaosSelector.style.display = 'flex';

  spawnMonster();
  addLog(`【地圖切換】進入 ${DUNGEONS[idx].name} (當前階級: ${CHAOS_TIERS[currentChaosTier].name})，遭遇怪物 ${currentMonster.name} (Lv.${currentMonster.level})！`, 'log-system');
  updateUI();
}

function setChaosTier(tierIdx) {
  currentChaosTier = tierIdx;
  document.querySelectorAll('.chaos-lvl-btn').forEach(btn => {
    const tier = parseInt(btn.getAttribute('data-tier'));
    btn.classList.toggle('active', tier === tierIdx);
  });
  spawnMonster();
  const dungName = DUNGEONS[currentDungeonIdx].name;
  addLog(`【試煉階級調整】將 ${dungName} 試煉強度調整至【${CHAOS_TIERS[tierIdx].name}】！`, 'log-system');
  updateUI();
}

function setForgeMode(mode, btnEl) {
  currentForgeMode = mode;
  document.querySelectorAll('.forge-mode-btn').forEach(btn => {
    btn.classList.remove('active');
  });
  if (btnEl) btnEl.classList.add('active');
  updateForgeCostDisplay();
}

function setForgeElement(elem) {
  currentForgeElement = elem;
  document.querySelectorAll('#forge-element-selector .pixel-btn').forEach(btn => {
    btn.classList.toggle('active', btn.getAttribute('data-forge-elem') === elem);
  });
  updateForgeCostDisplay();
}

function updateForgeCostDisplay() {
  const cost = 3;
  const primary = ELEMENT_MAT_MAP[currentForgeElement];
  const rateBox = document.getElementById('forge-rate-box');
  const costDetail = document.getElementById('forge-cost-detail');
  const costWarning = document.getElementById('forge-cost-warning');
  const forgeBtn = document.getElementById('btn-forge');

  if (!primary || !rateBox || !costDetail || !costWarning || !forgeBtn) return;

  let costText = '';
  let canForge = true;

  if (currentForgeMode === 'single') {
    costText = `${primary.icon} ${primary.matName} ×${cost}`;
    if (player.materials[primary.matKey] < cost) canForge = false;
    rateBox.textContent = '100% 成功率';
    rateBox.style.background = '#1a3a1a';
    rateBox.style.borderColor = '#2ecc71';
    rateBox.style.color = '#2ecc71';
  } else if (currentForgeMode === 'sheng') {
    const secondary = ELEMENT_MAT_MAP[primary.sheng];
    costText = `${primary.icon} ${primary.matName} ×${cost} + ${secondary.icon} ${secondary.matName} ×${cost}`;
    if (player.materials[primary.matKey] < cost || player.materials[secondary.matKey] < cost) canForge = false;
    rateBox.textContent = '120% 成功率 + 品質加成';
    rateBox.style.background = '#1a2a3a';
    rateBox.style.borderColor = '#3498db';
    rateBox.style.color = '#3498db';
  } else if (currentForgeMode === 'ke') {
    const secondary = ELEMENT_MAT_MAP[primary.ke];
    costText = `${primary.icon} ${primary.matName} ×${cost} + ${secondary.icon} ${secondary.matName} ×${cost}`;
    if (player.materials[primary.matKey] < cost || player.materials[secondary.matKey] < cost) canForge = false;
    rateBox.textContent = '65% 成功率 · 可暴擊神品';
    rateBox.style.background = '#3a1a1a';
    rateBox.style.borderColor = '#e74c3c';
    rateBox.style.color = '#e74c3c';
  }

  costDetail.textContent = costText;

  if (!canForge) {
    costWarning.style.display = 'block';
    costWarning.textContent = `⚠️ 材料不足！無法鍛造！（需要 ${costText}）`;
    forgeBtn.disabled = true;
    forgeBtn.style.opacity = '0.4';
    forgeBtn.textContent = '🚫 材料不足';
  } else {
    costWarning.style.display = 'none';
    forgeBtn.disabled = false;
    forgeBtn.style.opacity = '1';
    forgeBtn.textContent = '🔨 開爐鍛造裝備';
  }
}

function spawnMonster() {
  const dung = DUNGEONS[currentDungeonIdx];
  const bossRate = GAME_CONFIG.bossSpawnRate !== undefined ? GAME_CONFIG.bossSpawnRate : 0.20;
  const isBoss = Math.random() < bossRate;
  
  let monsterData = null;
  if (isBoss && dung.bosses && dung.bosses.length > 0) {
    monsterData = dung.bosses[Math.floor(Math.random() * dung.bosses.length)];
  } else {
    monsterData = dung.monsters[Math.floor(Math.random() * dung.monsters.length)];
  }

  const tierObj = CHAOS_TIERS[currentChaosTier];
  const tierScale = tierObj.scale || 1.0;
  const elem = monsterData.elem || dung.element;

  // 1. 嚴格在當前試煉階級的 minLvl ~ maxLvl 之間精準生成隨機等級（如 15~30 級）
  const minL = tierObj.minLvl || 1;
  const maxL = tierObj.maxLvl || 15;
  const monsterLvl = Math.floor(Math.random() * (maxL - minL + 1)) + minL;

  // 2. 算入 GM 控制台之怪物血量/攻擊倍率
  const hpMult = GAME_CONFIG.monsterHpMult || 2.0;
  const atkMult = GAME_CONFIG.monsterAtkMult || 1.8;

  // 3. 結合等級浮動與試煉階級總倍率 tierScale (如 15~30級乘數為 2.2x, 70~100級為 16.0x)
  const lvlProgress = (monsterLvl - minL) / Math.max(1, (maxL - minL));
  const totalScale = tierScale * (1.0 + lvlProgress * 0.4);

  const baseHp = Math.floor(75 * totalScale * hpMult * (isBoss ? 2.5 : 1.0));
  const baseAtk = Math.floor(15 * totalScale * atkMult * (isBoss ? 1.5 : 1.0));
  const baseDef = Math.floor(5 * totalScale * (isBoss ? 1.4 : 1.0));

  // 4. 動態標註當前試煉階級封號（如 【靈階】赤焰火狐、【聖階】太陰玄冰冰龍皇）
  const pName = isBoss ? `${tierObj.prefix}·首領` : `${tierObj.prefix}`;

  currentMonster = {
    name: `【${pName}】${monsterData.name}`,
    element: elem,
    icon: monsterData.icon,
    level: monsterLvl,
    maxHp: baseHp,
    hp: baseHp,
    atk: baseAtk,
    def: baseDef,
    isBoss: isBoss
  };
  updateMonsterUI();
}

function updateMonsterUI() {
  if (!currentMonster) return;
  
  const iconEl = document.getElementById('monster-icon');
  const nameEl = document.getElementById('monster-name');
  if (iconEl) iconEl.textContent = currentMonster.icon;
  if (nameEl) nameEl.textContent = `${currentMonster.name} (${ELEMENT_NAMES[currentMonster.element] || '五行系'})`;

  const bossTag = document.getElementById('monster-boss-tag');
  if (bossTag) bossTag.style.display = currentMonster.isBoss ? 'inline-block' : 'none';

  const statsDetail = document.getElementById('monster-stats-detail');
  if (statsDetail) {
    const color = currentMonster.isBoss ? '#f39c12' : '#2ecc71';
    statsDetail.style.color = color;
    statsDetail.innerHTML = `Lv.${currentMonster.level} | ⚔️ 攻擊: ${currentMonster.atk} | 🛡️ 防禦: ${currentMonster.def}`;
  }

  const hpPct = Math.min(100, Math.max(0, (currentMonster.hp / currentMonster.maxHp) * 100));
  const hpFill = document.getElementById('monster-hp-fill');
  const hpText = document.getElementById('monster-hp-text');
  if (hpFill) hpFill.style.width = `${hpPct}%`;
  if (hpText) hpText.textContent = `${currentMonster.hp} / ${currentMonster.maxHp}`;
}

function executeBattleRound() {
  // 自動掛機智能恢復：若負傷或血量低，優先嘗試自動服丹或打坐調息
  if (player.isInjured || player.hp < player.maxHp * 0.3) {
    checkAutoUsePillOnLowHp();
    
    // 若依然受傷且在自動掛機模式中，自動觸發打坐調息恢復氣血
    if (player.isInjured || player.hp < player.maxHp) {
      if (isAutoBattling) {
        meditate();
        if (player.hp >= player.maxHp) {
          player.isInjured = false;
          addLog(`【自動續航】氣血已調息補滿！天道印記運轉，自動無縫繼續秘境討伐！`, 'log-system');
        } else {
          return; // 繼續打坐調息
        }
      } else if (player.isInjured) {
        addLog(`【負傷休養中】傷勢嚴重！請點擊【🧘 打坐調息】或【💊 服用槽中丹藥】補滿血量！`, 'log-monster');
        return;
      }
    }
  }

  if (!currentMonster || currentMonster.hp <= 0) {
    spawnMonster();
  }

  audioSynth.sfxAttack();

  // 檢查境界壓制 (當怪物等級大於玩家等級 10 級以上)
  const levelDiff = currentMonster.level - player.level;
  let suppressionPenalty = 1.0;
  let isUnderSuppression = false;

  if (levelDiff >= 10) {
    isUnderSuppression = true;
    suppressionPenalty = Math.max(0.2, 1.0 - (levelDiff - 9) * 0.06);
    addLog(`【⚠️ 境界壓制】敵我道行差距達 ${levelDiff} 級！發動攻擊受天道威壓削弱！`, 'log-monster');
  }

  let playerDamageMult = 1.0 * suppressionPenalty;
  if (player.element === 'azure' || ELEMENT_COUNTER[player.element] === currentMonster.element) {
    playerDamageMult *= 1.2;
    addLog(`【克制壓制】五行相克，發揮 120% 攻擊力！`, 'log-crit');
  }

  // 算入天道 GM 會心率與爆傷倍率
  const effectiveCritRate = (player.critRate || 0.1) + (GAME_CONFIG.baseCritRate || 0.1) - 0.1;
  let isCrit = Math.random() < effectiveCritRate;
  let baseDmg = Math.max(5, player.atk - Math.floor(currentMonster.def * 0.5));
  const critMult = GAME_CONFIG.critDamageMult || 2.0;
  let finalDmg = Math.floor(baseDmg * playerDamageMult * (isCrit ? critMult : 1.0));
  
  currentMonster.hp = Math.max(0, currentMonster.hp - finalDmg);

  if (isCrit) {
    audioSynth.sfxCrit();
    addLog(`【爆發】你發動了會心一擊！對 ${currentMonster.name} (Lv.${currentMonster.level}) 造成 ${finalDmg} 點傷害！`, 'log-crit');
  } else {
    addLog(`【攻擊】你對 ${currentMonster.name} (Lv.${currentMonster.level}) 造成 ${finalDmg} 點傷害。`, 'log-player');
  }

  triggerClassEffect();

  if (currentMonster.hp <= 0) {
    onMonsterDefeated();
    return;
  }

  setTimeout(() => {
    let monsterDmgMult = 1.0;
    
    if (player.element === 'azure' || player.element === currentMonster.element) {
      monsterDmgMult *= 0.7;
      addLog(`【同系共鳴】遭遇同系敵方，觸發五行共鳴護盾，防禦加成 30%！`, 'log-element');
    }

    if (player.element !== 'azure' && ELEMENT_COUNTER[currentMonster.element] === player.element) {
      monsterDmgMult *= 1.2;
      addLog(`【靈根反噬】遭敵方屬性劇烈剋制，受到 120% 靈根反噬傷害！`, 'log-monster');
    }

    // 境界壓制加重怪物對玩家的傷害
    if (isUnderSuppression) {
      const extraSuppression = (1.0 + (levelDiff - 9) * 0.12) * (GAME_CONFIG.suppressionMult || 1.5);
      monsterDmgMult *= extraSuppression;
      addLog(`【境界鎮壓】對手境界遠高於你，攻擊附加 ${Math.floor((extraSuppression-1)*100)}% 威壓重創傷害！`, 'log-monster');
    }

    let monsterDmg = Math.max(5, Math.floor((currentMonster.atk - Math.floor(player.def * 0.5)) * monsterDmgMult));
    
    player.hp = Math.max(0, player.hp - monsterDmg);
    audioSynth.sfxHit();
    addLog(`【受擊】${currentMonster.name} (Lv.${currentMonster.level}) 對你造成 ${monsterDmg} 點傷害！`, 'log-monster');

    // 🏥 氣血低於 20% 保命防線：若丹藥槽裝備補血丹藥且 HP 低於 20%，自動服用！
    checkAutoUsePillOnLowHp();

    if (player.hp <= 0) {
      player.hp = 1;
      player.isInjured = true;
      audioSynth.sfxHit();
      addLog(`【🤕 戰敗負傷】你被 ${currentMonster.name} 重創擊倒！體力透支逃回洞府！負傷期間恢復速度降為 50%，氣血全滿前無法再次歷練！`, 'log-crit');
      if (isAutoBattling) toggleAutoBattle();
      if (isMeditating) stopMeditate();
    }
    updateUI();
  }, 200);

  updateMonsterUI();
  updateUI();
}

function showBossDropModal(equip) {
  const modal = document.getElementById('boss-drop-modal');
  if (!modal) return;

  currentDropEquipItem = equip;

  document.getElementById('boss-drop-modal-title').textContent = `👑 首領降伏！金光大爆！`;
  document.getElementById('boss-drop-modal-icon').textContent = equip.icon || '🗡️';
  
  const nameEl = document.getElementById('boss-drop-modal-name');
  nameEl.textContent = equip.name;
  nameEl.style.color = equip.qualityColor || '#f1c40f';

  document.getElementById('boss-drop-modal-stats').textContent = `⚔️ 攻擊: +${equip.atk}  |  🛡️ 防禦: +${equip.def}  |  (${equip.qualityName})`;

  audioSynth.sfxReward();
  modal.classList.add('show');
}

function startForgeInFurnace() {
  ensurePlayerFurnaces();
  const idleIdx = player.forgeFurnaces.findIndex(f => f && f.status === 'idle');
  if (idleIdx === -1) {
    addLog(`【鍛造爐忙碌】所有解鎖的鍛造爐都在運轉中！請等待出爐或至坊市購入新鍛造爐！`, 'log-monster');
    return;
  }

  const m = player.materials;
  const cost = 3;
  const primary = ELEMENT_MAT_MAP[currentForgeElement];

  if (currentForgeMode === 'single') {
    if (m[primary.matKey] < cost) {
      addLog(`【鍛造失敗】${primary.matName}不足！需要至少 ${cost} 個。`, 'log-monster');
      return;
    }
    m[primary.matKey] -= cost;
  } else if (currentForgeMode === 'sheng') {
    const secondary = ELEMENT_MAT_MAP[primary.sheng];
    if (m[primary.matKey] < cost || m[secondary.matKey] < cost) {
      addLog(`【鍛造失敗】相生鍛造需要 ${primary.matName} 與 ${secondary.matName} 各 ${cost} 個！`, 'log-monster');
      return;
    }
    m[primary.matKey] -= cost;
    m[secondary.matKey] -= cost;
  } else if (currentForgeMode === 'ke') {
    const secondary = ELEMENT_MAT_MAP[primary.ke];
    if (m[primary.matKey] < cost || m[secondary.matKey] < cost) {
      addLog(`【鍛造失敗】相剋鍛造需要 ${primary.matName} 與 ${secondary.matName} 各 ${cost} 個！`, 'log-monster');
      return;
    }
    m[primary.matKey] -= cost;
    m[secondary.matKey] -= cost;
  }

  const furnace = player.forgeFurnaces[idleIdx];
  const baseSec = GAME_CONFIG.forgeBaseTime || 60;
  const forgeBonus = calculateForgeSpeedBonus();
  const totalSpeed = furnace.speedMult + forgeBonus;
  const durationSec = Math.max(3, Math.floor(baseSec / totalSpeed));

  if (!player.stats) player.stats = { onlineSeconds: 0, totalMonstersKilled: 0, totalCrafts: 0 };
  player.stats.totalCrafts = (player.stats.totalCrafts || 0) + 1;

  furnace.status = 'cooking';
  furnace.forgeData = { mode: currentForgeMode, elem: currentForgeElement };
  furnace.startTime = Date.now();
  furnace.duration = durationSec * 1000;

  // 滿級神爐 (Level 5) 低階法器熔煉處置
  furnace.smeltBonus = false;
  const smeltSelect = document.getElementById('forge-smelt-select');
  if (furnace.level >= 5 && smeltSelect && smeltSelect.value) {
    const selectedItemId = parseFloat(smeltSelect.value);
    const smeltIdx = player.inventory.findIndex(i => i && i.id === selectedItemId);
    if (smeltIdx !== -1) {
      const smeltedItem = player.inventory[smeltIdx];
      player.inventory.splice(smeltIdx, 1);
      furnace.smeltBonus = true;
      addLog(`【🔥 舊法熔煉】成功投入舊法寶【${smeltedItem.name}】熔煉入爐！神鼎靈力大暴走，下一次出爐極品/神品機率原基礎額外 +50%！`, 'log-crit');
    }
  }

  audioSynth.sfxCraft();
  addLog(`【開爐鍛造】使用【${furnace.name}】(速度 ${totalSpeed.toFixed(1)}x) 投入神材！開爐倒數 ${durationSec} 秒！`, 'log-crit');

  updateUI();
  updateForgeCostDisplay();
}

function collectForgeResult(idx) {
  const furnace = player.forgeFurnaces[idx];
  if (!furnace || furnace.status !== 'completed' || !furnace.forgeData) return;

  const { mode, elem } = furnace.forgeData;
  const primary = ELEMENT_MAT_MAP[elem];

  let successRate = 1.0;
  if (mode === 'sheng') successRate = 1.2;
  if (mode === 'ke') successRate = 0.65;

  if (Math.random() > successRate) {
    audioSynth.sfxHit();
    addLog(`【💥 鍛造炸爐】屬性強烈衝擊！【${furnace.name}】鍛造失敗爆爐，神材損毀！`, 'log-monster');
    furnace.status = 'idle';
    furnace.forgeData = null;
    updateUI();
    updateForgeCostDisplay();
    saveGame();
    return;
  }

  let qIdx = 0;
  const rand = Math.random() * 100;
  const hasSmeltBonus = !!furnace.smeltBonus;

  if (hasSmeltBonus) {
    // 滿級神爐熔煉加成：極品與神品爆率原基礎額外 +50%！
    if (rand < 51) qIdx = 5; // 神品 (原 1%~3% -> 飆升至 51%!)
    else if (rand < 75) qIdx = 4; // 極品 (原 5%~15% -> 飆升至 24%!)
    else if (rand < 90) qIdx = 3; // 上品
    else qIdx = 2; // 中品
  } else if (mode === 'ke') {
    if (rand < 25) qIdx = 5;
    else if (rand < 45) qIdx = 4;
    else if (rand < 70) qIdx = 3;
    else qIdx = 2;
  } else if (mode === 'sheng') {
    if (rand < 3) qIdx = 5;
    else if (rand < 15) qIdx = 4;
    else if (rand < 40) qIdx = 3;
    else if (rand < 75) qIdx = 2;
    else qIdx = 1;
  } else {
    if (rand < 1) qIdx = 5;
    else if (rand < 5) qIdx = 4;
    else if (rand < 15) qIdx = 3;
    else if (rand < 35) qIdx = 2;
    else if (rand < 65) qIdx = 1;
  }

  const qualityObj = QUALITIES[qIdx];
  const types = ['weapon', 'armor', 'accessory'];
  const type = types[Math.floor(Math.random() * types.length)];

  let namePrefix = primary.prefix;
  let typeName = { weapon: '聖劍', armor: '寶鎧', accessory: '佩玉' }[type];
  let icon = { weapon: '🗡️', armor: '🛡️', accessory: '📿' }[type];

  let baseVal = Math.floor((15 + player.level * 3) * qualityObj.multiplier);
  let atk = type === 'weapon' ? baseVal : Math.floor(baseVal * 0.3);
  let def = type === 'armor' ? baseVal : Math.floor(baseVal * 0.3);

  const equip = {
    id: Date.now() + Math.random(),
    name: `${namePrefix}·${qualityObj.name}${typeName}`,
    type,
    element: elem,
    quality: qualityObj.level,
    qualityName: qualityObj.name,
    qualityColor: qualityObj.color,
    atk, def, icon
  };

  if (!player.inventory || !Array.isArray(player.inventory)) player.inventory = [];
  player.inventory.push(equip);

  furnace.status = 'idle';
  furnace.forgeData = null;

  audioSynth.sfxReward();
  addLog(`【✨ 寶物出爐】神兵大成！成功從 ${furnace.name} 取出【${equip.name}】(品級:${equip.qualityName} | 攻+${atk} 防+${def}) 正式收入乾坤背包！`, 'log-crit');

  updateUI();
  updateForgeCostDisplay();
  saveGame();
}

function triggerClassEffect() {
  if (Math.random() < player.specialRate) {
    switch (player.element) {
      case 'gold': addLog(`【金系特效】觸發「擊暈」，怪物下一回合無法行動！`, 'log-element'); break;
      case 'wood':
        let poisonDmg = Math.floor(currentMonster.maxHp * 0.05);
        currentMonster.hp = Math.max(0, currentMonster.hp - poisonDmg);
        addLog(`【木系特效】發動「重毒」，怪物損失 ${poisonDmg} 點真實氣血！`, 'log-element');
        break;
      case 'water': addLog(`【水系特效】發動「冰凍」，敵方攻速與閃避大幅下降！`, 'log-element'); break;
      case 'fire': addLog(`【火系特效】發動「致盲」，敵方命中率降低 40%！`, 'log-element'); break;
      case 'earth': addLog(`【土系特效】發動「虛弱」，敵方攻擊力與防禦力大幅下降！`, 'log-element'); break;
      case 'azure': addLog(`【蒼天特效】觸發「天地同威」，對敵方造成 1.5 倍神聖天威傷害！`, 'log-crit'); break;
    }
  }
}

// 擊敗怪物 & 隨機機緣 / 神秘商人觸發 (15% 機率)
function onMonsterDefeated() {
  if (!player.stats) player.stats = { onlineSeconds: 0, totalMonstersKilled: 0, totalCrafts: 0 };
  player.stats.totalMonstersKilled = (player.stats.totalMonstersKilled || 0) + 1;

  const dung = DUNGEONS[currentDungeonIdx];
  const tierObj = CHAOS_TIERS[currentChaosTier];
  audioSynth.sfxReward();

  let baseExp = dung.baseExp;
  let baseCoin = dung.baseCoin;

  const scale = tierObj ? (tierObj.scale || 1.0) : 1.0;
  baseExp = Math.floor(dung.baseExp * scale);
  baseCoin = Math.floor(dung.baseCoin * scale);

  const isBossMonster = currentMonster && currentMonster.isBoss;
  if (isBossMonster) {
    baseExp = Math.floor(baseExp * 2.5);
    baseCoin = Math.floor(baseCoin * 3.5);
  }

  // 算入天賦修速 + 心法修速 + 天道修為倍率
  const totalExpSpeed = calculateTotalExpSpeed();
  const expGain = Math.floor(baseExp * totalExpSpeed * (GAME_CONFIG.expMult || 1.0));
  const coinGain = Math.floor(baseCoin * (GAME_CONFIG.coinMult || 1.0));
  player.exp += expGain;
  player.coins += coinGain;
  
  let droppedMatName = "";
  let matCount = isBossMonster ? Math.floor(Math.random() * 3) + 3 : 1;
  if (dung.matDrop === 'all') {
    const allMats = ['goldMat', 'woodMat', 'waterMat', 'fireMat', 'earthMat'];
    const selectedMat = allMats[Math.floor(Math.random() * allMats.length)];
    player.materials[selectedMat] += matCount;
    droppedMatName = { goldMat:'金精石', woodMat:'神木芯', waterMat:'玄冰髓', fireMat:'朱雀羽', earthMat:'息壤土' }[selectedMat];
  } else {
    player.materials[dung.matDrop] += matCount;
    droppedMatName = { goldMat:'金精石', woodMat:'神木芯', waterMat:'玄冰髓', fireMat:'朱雀羽', earthMat:'息壤土' }[dung.matDrop];
  }

  // 20% 機率獲得靈藥草藥
  const herbRate = GAME_CONFIG.herbDropRate || 0.20;
  if (Math.random() < herbRate) {
    const herbKeys = ['lingzhi', 'baicao', 'zhusha', 'longkui', 'renshen'];
    const weights = [40, 30, 15, 10, 5];
    let r = Math.random() * 100, cum = 0, selectedHerb = 'lingzhi';
    for (let i = 0; i < weights.length; i++) {
      cum += weights[i];
      if (r < cum) { selectedHerb = herbKeys[i]; break; }
    }
    player.herbs[selectedHerb] = (player.herbs[selectedHerb] || 0) + 1;
    const herbNameMap = { lingzhi:'靈芝草', baicao:'百草露', zhusha:'硃砂果', longkui:'龍葵花', renshen:'千年人參' };
    addLog(`【採集】擊敗怪物採集到靈藥：【${herbNameMap[selectedHerb]}】+1！`, 'log-drop');
  }

  addLog(`【大捷】擊敗 ${currentMonster.name}！修為+${expGain} (修速 ${Math.floor(totalExpSpeed*100)}%)，靈石+${coinGain}，【${droppedMatName}】+${matCount}！`, 'log-drop');

  // 法寶裝備爆裝邏輯 (連動天道 GM 自訂%設定)
  const baseEquipDropRate = GAME_CONFIG.equipDropRate !== undefined ? GAME_CONFIG.equipDropRate : 0.20;
  const dropRate = isBossMonster 
    ? (GAME_CONFIG.bossDropEquipRate !== undefined ? GAME_CONFIG.bossDropEquipRate : Math.min(1.0, baseEquipDropRate * 2.5))
    : (GAME_CONFIG.normalDropEquipRate !== undefined ? GAME_CONFIG.normalDropEquipRate : baseEquipDropRate);

  const shouldDrop = dropRate >= 0.99 ? true : (Math.random() <= dropRate);

  if (shouldDrop) {
    const types = ['weapon', 'armor', 'accessory'];
    const type = types[Math.floor(Math.random() * types.length)];
    
    let qualities = [];
    if (isBossMonster) {
      qualities = [QUALITIES[3], QUALITIES[4], QUALITIES[5]]; // 上品、極品、神品
    } else {
      qualities = [QUALITIES[0], QUALITIES[1], QUALITIES[2], QUALITIES[3]]; // 凡品、下品、中品、上品
    }
    const qObj = qualities[Math.floor(Math.random() * qualities.length)];

    const typeName = { weapon: '聖劍', armor: '寶鎧', accessory: '佩玉' }[type];
    const icon = { weapon: '🗡️', armor: '🛡️', accessory: '📿' }[type];
    const elemPrefix = (currentMonster && ELEMENT_MAT_MAP[currentMonster.element]) ? ELEMENT_MAT_MAP[currentMonster.element].prefix : '洪荒';
    const monsterLevel = currentMonster ? currentMonster.level : 1;
    const baseVal = Math.floor((15 + monsterLevel * 3) * qObj.multiplier);

    const droppedEquip = {
      id: Date.now() + Math.random(),
      name: `${elemPrefix}·${qObj.name}${typeName}`,
      type,
      element: currentMonster ? currentMonster.element : 'gold',
      quality: qObj.level,
      qualityName: qObj.name,
      qualityColor: qObj.color,
      atk: type === 'weapon' ? baseVal : Math.floor(baseVal * 0.3),
      def: type === 'armor' ? baseVal : Math.floor(baseVal * 0.3),
      icon
    };

    if (!player.inventory) player.inventory = [];
    player.inventory.push(droppedEquip);

    if (isBossMonster) {
      addLog(`【👑 首領大爆裝備】${currentMonster.name} 轟然倒地解體！暴出【${droppedEquip.name}】(品級:${droppedEquip.qualityName} | 攻+${droppedEquip.atk} 防+${droppedEquip.def})！已放入【🎒乾坤背包】(當前共 ${player.inventory.length} 件)！`, 'log-crit');
    } else {
      addLog(`【🎁 戰利品爆裝】擊敗 ${currentMonster.name}！獲得【${droppedEquip.name}】(品級:${droppedEquip.qualityName})！已放入【🎒乾坤背包】(當前共 ${player.inventory.length} 件)！`, 'log-drop');
    }
  }

  // 動態天道機率觸發隨機機緣或神秘商人
  const eventRate = GAME_CONFIG.eventRate || 0.06;
  if (Math.random() < eventRate) {
    const merchantRate = GAME_CONFIG.merchantRate || 0.20;
    if (Math.random() >= merchantRate) {
      const rewardCoins = Math.floor((300 + Math.floor(Math.random() * 500)) * (GAME_CONFIG.coinMult || 1.0));
      player.coins += rewardCoins;
      addLog(`【✨ 天降機緣】偶遇洪荒大能遺跡，獲得古仙贈禮：靈石 +${rewardCoins}！`, 'log-crit');
    } else {
      generateMerchantItems();
      document.getElementById('merchant-banner').classList.add('show');
      addLog(`【🧙‍♂️ 機緣降臨】雲遊神秘商人攜帶武學與內功心法降臨秘境！僅限購入一件珍品！`, 'log-crit');
    }
  }

  if (player.exp >= player.maxExp) {
    levelUp();
  }

  saveGame();
  updateUI();

  setTimeout(() => {
    spawnMonster();
  }, 1000);
}

function levelUp() {
  player.level += 1;
  player.exp -= player.maxExp;
  player.maxExp = Math.floor(player.maxExp * 1.35);

  recalculatePlayerStats();
  player.hp = player.maxHp;

  audioSynth.sfxLevelUp();
  addLog(`【突破】修為精進！境界突破至【${getRealmName(player.level)}】！全屬性大幅提升！`, 'log-crit');
  renderSutraTab(currentSutraCategory);
}

function getRealmName(lvl) {
  const idx = Math.min(Math.floor((lvl - 1) / 3), REALMS.length - 1);
  return REALMS[idx];
}

function toggleAutoBattle() {
  const btn = document.getElementById('btn-toggle-auto');
  if (isAutoBattling) {
    if (autoBattleInterval) clearInterval(autoBattleInterval);
    isAutoBattling = false;
    btn.textContent = '⚔️ 開啟自動掛機';
    btn.classList.remove('btn-gold');
    addLog(`【系統】已停止自動掛機。`, 'log-system');
  } else {
    isAutoBattling = true;
    btn.classList.add('btn-gold');
    
    // 算入掛機攻速加速丹藥加成
    const speedBoost = (player.activeSpeedPill && player.activeSpeedPill.boost) ? player.activeSpeedPill.boost : 0;
    const interval = Math.max(150, Math.floor(1200 / (1 + speedBoost)));
    const pillText = player.activeSpeedPill ? ` (${player.activeSpeedPill.name} 攻速 +${Math.floor(speedBoost * 100)}%)` : '';

    btn.textContent = `⏸️ 停止自動掛機${pillText}`;
    addLog(`【自動掛機】開啟自動討伐秘境魔物... 戰鬥頻率: ${interval}ms/次${pillText}`, 'log-system');
    executeBattleRound();
    autoBattleInterval = setInterval(executeBattleRound, interval);
  }
}

function forgeEquipment() {
  const m = player.materials;
  const cost = 3;
  const primary = ELEMENT_MAT_MAP[currentForgeElement];

  // 檢查材料是否足夠
  if (currentForgeMode === 'single') {
    if (m[primary.matKey] < cost) {
      addLog(`【鍛造失敗】${primary.matName}不足！需要至少 ${cost} 個。`, 'log-monster');
      return;
    }
    m[primary.matKey] -= cost;
  } else if (currentForgeMode === 'sheng') {
    const secondary = ELEMENT_MAT_MAP[primary.sheng];
    if (m[primary.matKey] < cost || m[secondary.matKey] < cost) {
      addLog(`【鍛造失敗】相生鍛造需要 ${primary.matName} 與 ${secondary.matName} 各 ${cost} 個！`, 'log-monster');
      return;
    }
    m[primary.matKey] -= cost;
    m[secondary.matKey] -= cost;
  } else if (currentForgeMode === 'ke') {
    const secondary = ELEMENT_MAT_MAP[primary.ke];
    if (m[primary.matKey] < cost || m[secondary.matKey] < cost) {
      addLog(`【鍛造失敗】相剋鍛造需要 ${primary.matName} 與 ${secondary.matName} 各 ${cost} 個！`, 'log-monster');
      return;
    }
    m[primary.matKey] -= cost;
    m[secondary.matKey] -= cost;
  }

  audioSynth.sfxCraft();

  let successRate = 1.0;
  if (currentForgeMode === 'sheng') successRate = 1.2;
  if (currentForgeMode === 'ke') successRate = 0.65;

  if (Math.random() > successRate) {
    addLog(`【炸爐】屬性強烈衝突導致爆爐！材料損毀！`, 'log-monster');
    updateUI();
    updateForgeCostDisplay();
    return;
  }

  let qIdx = 0;
  const rand = Math.random() * 100;

  if (currentForgeMode === 'ke') {
    if (rand < 25) qIdx = 5;
    else if (rand < 45) qIdx = 4;
    else if (rand < 70) qIdx = 3;
    else qIdx = 2;
  } else if (currentForgeMode === 'sheng') {
    if (rand < 3) qIdx = 5;
    else if (rand < 15) qIdx = 4;
    else if (rand < 40) qIdx = 3;
    else if (rand < 75) qIdx = 2;
    else qIdx = 1;
  } else {
    if (rand < 1) qIdx = 5;
    else if (rand < 5) qIdx = 4;
    else if (rand < 15) qIdx = 3;
    else if (rand < 35) qIdx = 2;
    else if (rand < 65) qIdx = 1;
  }

  const qualityObj = QUALITIES[qIdx];
  const types = ['weapon', 'armor', 'accessory'];
  const type = types[Math.floor(Math.random() * types.length)];

  let namePrefix = primary.prefix;
  let typeName = { weapon: '聖劍', armor: '寶鎧', accessory: '佩玉' }[type];
  let icon = { weapon: '🗡️', armor: '🛡️', accessory: '📿' }[type];

  let baseVal = Math.floor((15 + player.level * 3) * qualityObj.multiplier);
  let atk = type === 'weapon' ? baseVal : Math.floor(baseVal * 0.3);
  let def = type === 'armor' ? baseVal : Math.floor(baseVal * 0.3);

  const newEquip = {
    id: Date.now() + Math.random(),
    name: `${namePrefix}·${qualityObj.name}${typeName}`,
    type: type,
    element: currentForgeElement,
    quality: qIdx + 1,
    qualityName: qualityObj.name,
    qualityColor: qualityObj.color,
    atk: atk,
    def: def,
    icon: icon
  };

  player.inventory.push(newEquip);

  if (qIdx >= 5) {
    addLog(`【逆天神品】天降祥瑞！鍛造出最高神品裝備：${newEquip.name}！`, 'log-crit');
  } else {
    addLog(`【鍛造成功】恭喜打造出【${qualityObj.name}】級別 ${ELEMENT_NAMES[currentForgeElement]} 裝備：${newEquip.name}！`, 'log-drop');
  }

  updateUI();
  updateForgeCostDisplay();
}

function setupDragAndDrop() {
  const dropZone = document.getElementById('salvage-drop-zone');
  if (!dropZone) return;

  dropZone.addEventListener('dragover', (e) => {
    e.preventDefault();
    dropZone.classList.add('drag-over');
  });

  dropZone.addEventListener('dragleave', () => {
    dropZone.classList.remove('drag-over');
  });

  dropZone.addEventListener('drop', (e) => {
    e.preventDefault();
    dropZone.classList.remove('drag-over');
    const itemIdStr = e.dataTransfer.getData('text/plain');
    if (itemIdStr) {
      salvageSingleItem(parseFloat(itemIdStr));
    }
  });
}

function salvageSingleItem(itemId) {
  const idx = player.inventory.findIndex(i => i.id === itemId);
  if (idx === -1) return;

  const item = player.inventory[idx];
  player.inventory.splice(idx, 1);

  const coinsGain = item.quality * 100;
  const matKeys = ['goldMat', 'woodMat', 'waterMat', 'fireMat', 'earthMat'];
  const matGainKey = matKeys[Math.floor(Math.random() * matKeys.length)];
  player.materials[matGainKey] += item.quality;
  player.coins += coinsGain;

  audioSynth.sfxReward();
  addLog(`【三昧熔練】成功將裝備【${item.name}】投入真火熔練！獲得 靈石+${coinsGain}，五行神材+${item.quality}！`, 'log-crit');
  updateUI();
}

// 產生神秘商人隨機販售品項 (專售 上品 / 極品 / 神品 稀有絕學)
function generateMerchantItems() {
  if (currentMerchantItems.length > 0) return; // 已有商人品項未告辭
  if (!player.purchasedSutras) player.purchasedSutras = [];

  // 神秘商人獨家販售高階絕學 (上品、極品、神品)
  const highTierSutras = ALL_SUTRAS.filter(s => s.quality === '上品' || s.quality === '極品' || s.quality === '神品');
  const unpurchased = highTierSutras.filter(s => !player.purchasedSutras.includes(s.id));
  const pool = unpurchased.length > 0 ? unpurchased : highTierSutras;
  const count = Math.min(pool.length, Math.floor(Math.random() * 3) + 3);
  
  // 隨機洗牌
  const shuffled = [...pool].sort(() => Math.random() - 0.5);
  currentMerchantItems = shuffled.slice(0, count);
}

// 渲染神秘商人店舖
function renderMerchantShop() {
  const container = document.getElementById('merchant-shop-grid');
  if (!container) return;
  container.innerHTML = '';

  const coinsEl = document.getElementById('merchant-player-coins');
  if (coinsEl) coinsEl.textContent = player.coins || 0;

  if (!currentMerchantItems || currentMerchantItems.length === 0) {
    generateMerchantItems();
  }

  if (typeof ELEMENT_NAMES === 'undefined') {
    window.ELEMENT_NAMES = { gold: '金系', wood: '木系', water: '水系', fire: '火系', earth: '土系', internal: '內功', alchemy: '丹道', forge: '器道' };
  }

  currentMerchantItems.forEach(sutra => {
    const isBought = player.purchasedSutras && player.purchasedSutras.includes(sutra.id);
    const sutraCost = (sutra.price !== undefined) ? sutra.price : (sutra.cost !== undefined ? sutra.cost : 0);
    const canAfford = (player.coins || 0) >= sutraCost;

    const card = document.createElement('div');
    card.className = `sutra-card ${isBought ? 'sutra-purchased' : ''}`;
    
    // 是否同靈根
    const isSameElem = (player.element === sutra.category || player.element === 'azure');
    
    const effectText = typeof getSutraEffectText === 'function' ? getSutraEffectText(sutra, isSameElem) : `增強 ${sutra.name} 威能`;

    card.innerHTML = `
      <div class="sutra-card-header">
        <span class="sutra-name" style="font-weight:bold; color:var(--pixel-gold);">${sutra.name}</span>
        <span class="sutra-badge ${sutra.category === 'internal' ? 'sutra-type-internal' : 'sutra-type-martial'}">
          ${sutra.quality} · ${sutra.category === 'internal' ? '內功' : ELEMENT_NAMES[sutra.category] || '武學'}
        </span>
      </div>
      <div class="sutra-effect" style="font-size:0.75rem; color:#2ecc71; margin:4px 0;">${effectText}</div>
      <div class="sutra-desc" style="font-size:0.75rem; color:#aaa; margin-bottom:6px;">${sutra.desc}</div>
      ${isSameElem ? '<div style="font-size:0.7rem; color:#00ffff; margin-bottom:6px;">✨ 本命屬性契合 (1.15倍威力)</div>' : ''}
      <div style="display:flex; justify-content:space-between; align-items:center; margin-top:8px;">
        <span style="color:var(--pixel-gold); font-size:0.85rem; font-weight:bold;">💰 ${sutraCost} 靈石</span>
        <button class="pixel-btn ${isBought ? '' : canAfford ? 'btn-gold' : ''}" 
                ${isBought || !canAfford ? 'disabled' : ''} 
                onclick="buySutra('${sutra.id}')" 
                style="padding:6px 14px; font-size:0.85rem;">
          ${isBought ? '✓ 已售罄' : !canAfford ? '⚠️ 靈石不足' : '🛒 拜購參悟'}
        </button>
      </div>
    `;
    container.appendChild(card);
  });
}

// 藏經閣 6 大子分頁切換
function switchSutraTab(cat) {
  currentSutraCategory = cat;
  document.querySelectorAll('#sutra-sub-tabs .sub-tab-btn').forEach(btn => {
    btn.classList.remove('active');
  });
  if (event && event.target) {
    event.target.classList.add('active');
  }
  renderSutraTab();
}

// 渲染藏經閣 Tab
function renderSutraTab() {
  const container = document.getElementById('sutra-grid');
  if (!container) return;
  container.innerHTML = '';

  let totalAtk = 0, totalDef = 0, totalExp = 0;

  // 計算所有已參悟心法總加成
  ALL_SUTRAS.forEach(sutra => {
    if (player.purchasedSutras.includes(sutra.id)) {
      const isSameElem = (player.element === sutra.category || player.element === 'azure');
      const mult = isSameElem ? 1.15 : 1.0;
      totalAtk += Math.floor((sutra.atk || 0) * mult);
      totalDef += Math.floor((sutra.def || 0) * mult);
      totalExp += (sutra.expSpeed || 0) * mult;
    }
  });

  // 僅渲染當前 selected 分類
  const catSutras = ALL_SUTRAS.filter(s => s.category === currentSutraCategory);

  catSutras.forEach(sutra => {
    const isBought = player.purchasedSutras.includes(sutra.id);
    const isSameElem = (player.element === sutra.category || player.element === 'azure');

    const card = document.createElement('div');
    card.className = `sutra-card ${isBought ? 'sutra-purchased' : ''}`;
    card.style.opacity = isBought ? '1' : '0.55';
    card.innerHTML = `
      <div class="sutra-card-header">
        <span class="sutra-name">${sutra.name}</span>
        <span class="sutra-badge ${sutra.category === 'internal' ? 'sutra-type-internal' : 'sutra-type-martial'}">
          ${sutra.quality} · ${sutra.category === 'internal' ? '內功' : ELEMENT_NAMES[sutra.category] || '武學'}
        </span>
      </div>
      <div class="sutra-effect">${getSutraEffectText(sutra, isSameElem)}</div>
      <div class="sutra-desc">${sutra.desc}</div>
      ${isSameElem ? '<div style="font-size:0.7rem; color:#00ffff;">✨ 本命屬性契合 (+15% 效果加成)</div>' : ''}
      <div style="margin-top:8px; display:flex; justify-content:space-between; align-items:center;">
        <span style="font-size:0.75rem; color:${isBought ? '#2ecc71' : 'var(--pixel-gold)'}; font-weight:bold;">
          ${isBought ? '✓ 已參悟通透' : '💰 ' + sutra.price + ' 靈石'}
        </span>
        ${!isBought ? `<button class="pixel-btn btn-gold" style="padding:4px 8px; font-size:0.75rem;" onclick="buySutra('${sutra.id}')">參悟絕學</button>` : ''}
      </div>
    `;
    container.appendChild(card);
  });

  document.getElementById('sutra-bonus-atk').textContent = totalAtk;
  document.getElementById('sutra-bonus-def').textContent = totalDef;
  document.getElementById('sutra-bonus-exp').textContent = `${Math.floor(totalExp * 100)}%`;
}

function getSutraEffectText(sutra, isSameElem = false) {
  const mult = isSameElem ? 1.15 : 1.0;
  let parts = [];
  if (sutra.atk) parts.push(`攻 +${Math.floor(sutra.atk * mult)}`);
  if (sutra.def) parts.push(`防 +${Math.floor(sutra.def * mult)}`);
  if (sutra.hp) parts.push(`血 +${Math.floor(sutra.hp * mult)}`);
  if (sutra.expSpeed) parts.push(`修速 +${Math.floor(sutra.expSpeed * mult * 100)}%`);
  if (sutra.crit) parts.push(`會心 +${Math.floor(sutra.crit * 100)}%`);
  return parts.join(' | ');
}

// 計算玩家總修練速度 (靈根修速 + 心法修速增益)
function calculateTotalExpSpeed() {
  let sutraExpSpeed = 0;
  player.purchasedSutras.forEach(id => {
    const s = ALL_SUTRAS.find(item => item.id === id);
    if (s && s.expSpeed) {
      const isSameElem = (player.element === s.category || player.element === 'azure');
      sutraExpSpeed += s.expSpeed * (isSameElem ? 1.15 : 1.0);
    }
  });
  return (player.expSpeed || 1.0) + sutraExpSpeed;
}

// 專門處理丹藥獲取 (優先填補丹藥槽，其次自動堆疊放入背包)
function addPillToInventory(pillItem, count = 1) {
  if (!player.inventory) player.inventory = [];
  if (!player.equipped) ensurePlayerEquipped();

  const cleanName = pillItem.name ? pillItem.name.replace(/《|》/g, '') : '靈丹';
  const newItem = {
    id: pillItem.id || ('pill_' + Date.now() + '_' + Math.floor(Math.random() * 1000)),
    name: cleanName,
    type: 'pill',
    quality: pillItem.quality || '良品',
    qualityColor: pillItem.qualityColor || '#3498db',
    icon: pillItem.icon || '💊',
    desc: pillItem.desc || '滋補靈丹，受傷時可自動吞服回復氣血',
    count: count,
    action: pillItem.action
  };

  // 1. 若丹藥槽為空，全自動直入丹藥槽
  if (!player.equipped.pill) {
    player.equipped.pill = newItem;
    addLog(`【丹藥入槽】獲得【${cleanName}】×${count}！已自動放至左側丹藥槽中！`, 'log-crit');
    return;
  }

  // 2. 若丹藥槽已有且同名，直接累加丹藥槽數量
  if (player.equipped.pill.name === cleanName) {
    player.equipped.pill.count = (player.equipped.pill.count || 1) + count;
    addLog(`【丹藥補充】獲得【${cleanName}】×${count}！丹藥槽持有數已增至 ${player.equipped.pill.count} 顆！`, 'log-crit');
    return;
  }

  // 3. 否則放入背包 (自動堆疊同名丹藥)
  const existingPill = player.inventory.find(i => i && i.type === 'pill' && (i.name === pillItem.name || i.name === cleanName));
  if (existingPill) {
    existingPill.count = (existingPill.count || 1) + count;
  } else {
    player.inventory.push(newItem);
  }
  addLog(`【丹藥入庫】獲得【${cleanName}】×${count}！已存入乾坤背包中。`, 'log-drop');
}


// 💊 服用槽中丹藥 (並在數量歸零時從背包全自動補充)
function useEquippedPill() {
  if (!player.equipped || !player.equipped.pill) {
    addLog('【服丹提示】丹藥槽為空！請先在背包中點擊丹藥裝備至丹藥槽。', 'log-system');
    return;
  }

  const pill = player.equipped.pill;
  const cleanName = pill.name ? pill.name.replace(/《|》/g, '') : '靈丹';
  const recipe = PILL_RECIPES.find(r => r.id === pill.recipeId || r.name === cleanName || r.name === `《${cleanName}》`);
  
  let msg = '';
  if (typeof pill.action === 'function') {
    msg = pill.action(player);
  } else if (recipe && typeof recipe.action === 'function') {
    msg = recipe.action(player);
  } else {
    const heal = Math.floor(player.maxHp * 0.5);
    player.hp = Math.min(player.maxHp, player.hp + heal);
    msg = `吞服【${cleanName}】，氣血回復 ${heal} 點！`;
  }

  // 扣除 1 顆數量
  pill.count = (pill.count || 1) - 1;

  if (pill.count <= 0) {
    player.equipped.pill = null;
    addLog(`【丹藥耗盡】${msg}！已用完最後一顆【${cleanName}】，丹藥槽已空。`, 'log-crit');
  } else {
    addLog(`【服用丹藥】${msg} (丹藥槽剩餘 ${pill.count} 顆)`, 'log-crit');
  }

  audioSynth.sfxReward();
  recalculatePlayerStats();
  saveGame();
  updateUI();
}

// 確保 player.equipped 結構相容升級 (支援舊存檔自動平滑轉化)
function ensurePlayerEquipped() {
  if (!player.equipped || typeof player.equipped !== 'object' || Array.isArray(player.equipped)) {
    player.equipped = { weapon: null, armor: null, accessory: null, pill: null };
  } else {
    if (player.equipped.body && !player.equipped.armor) {
      player.equipped.armor = player.equipped.body;
      delete player.equipped.body;
    }
    if (player.equipped.defense && !player.equipped.armor) {
      player.equipped.armor = player.equipped.defense;
      delete player.equipped.defense;
    }
    if (player.equipped.jade && !player.equipped.accessory) {
      player.equipped.accessory = player.equipped.jade;
      delete player.equipped.jade;
    }
    if (player.equipped.ring && !player.equipped.accessory) {
      player.equipped.accessory = player.equipped.ring;
      delete player.equipped.ring;
    }
    if (player.equipped.weapon === undefined) player.equipped.weapon = null;
    if (player.equipped.armor === undefined) player.equipped.armor = null;
    if (player.equipped.accessory === undefined) player.equipped.accessory = null;
    if (player.equipped.pill === undefined) player.equipped.pill = null;
  }
}

// 核心槽位型態收攏與標準化 (確保武器/防具/飾品/丹藥 100% 精確對應)
function normalizeSlotType(item) {
  if (!item) return 'weapon';
  
  let type = (item.type || '').toLowerCase();
  const name = item.name || '';

  if (type === 'pill' || type === 'elixir' || name.includes('丹')) {
    return 'pill';
  }

  if (type === 'armor' || type === 'body' || type === 'defense' || type === 'chest' || type === 'helm' || type === 'boots' || name.includes('鎧') || name.includes('甲') || name.includes('衣') || name.includes('袍') || name.includes('盾')) {
    return 'armor';
  }

  if (type === 'accessory' || type === 'jade' || type === 'ring' || type === 'necklace' || name.includes('佩') || name.includes('玉') || name.includes('戒') || name.includes('鏈') || name.includes('符') || name.includes('珠')) {
    return 'accessory';
  }

  if (type === 'weapon' || type === 'sword' || type === 'blade' || type === 'spear' || type === 'staff' || type === 'bow' || name.includes('劍') || name.includes('刀') || name.includes('槍') || name.includes('杖') || name.includes('弓') || name.includes('斧') || name.includes('戟') || name.includes('槌') || name.includes('扇') || name.includes('鞭') || name.includes('刺') || name.includes('刃') || name.includes('聖')) {
    return 'weapon';
  }

  return 'weapon';
}

let pendingUsePillId = null;

// 打開背包丹藥二次確認 Modal 彈窗
function openPillConfirmModal(itemId) {
  if (!player.inventory) return;
  const item = player.inventory.find(i => i && String(i.id) === String(itemId));
  if (!item) return;

  pendingUsePillId = itemId;

  const modal = document.getElementById('pill-confirm-modal');
  const iconEl = document.getElementById('pill-confirm-icon');
  const nameEl = document.getElementById('pill-confirm-name');
  const descEl = document.getElementById('pill-confirm-desc');
  const countEl = document.getElementById('pill-confirm-count');
  const btnUse = document.getElementById('btn-pill-confirm-use');

  const cleanName = item.name ? item.name.replace(/《|》/g, '') : '靈丹';
  const isProtectPill = cleanName.includes('保具定海丹') || cleanName.includes('定海') || item.recipeId === 'recipe_protect_pill';

  if (iconEl) iconEl.textContent = item.icon || (isProtectPill ? '🛡️' : '💊');
  if (nameEl) nameEl.textContent = item.name || '靈丹';
  if (descEl) descEl.textContent = item.desc || (isProtectPill ? '五行精練法寶失敗時自動消耗，100% 避免法寶損毀碎裂！' : '滋補靈丹，服用後可恢復健康與道力');
  if (countEl) countEl.textContent = `背包剩餘持數: ${item.count || 1} 顆`;

  if (btnUse) {
    if (isProtectPill) {
      btnUse.textContent = '💡 精練自動消耗 (無需吞服)';
      btnUse.style.background = '#e67e22';
      btnUse.style.borderColor = '#f39c12';
      btnUse.onclick = () => confirmUsePillFromBag();
    } else {
      btnUse.textContent = '✅ 確定服用';
      btnUse.style.background = '#e74c3c';
      btnUse.style.borderColor = '#ff6666';
      btnUse.onclick = () => confirmUsePillFromBag();
    }
  }

  if (modal) modal.classList.add('show');
}

// 關閉丹藥二次確認 Modal 彈窗
function closePillConfirmModal() {
  pendingUsePillId = null;
  const modal = document.getElementById('pill-confirm-modal');
  if (modal) modal.classList.remove('show');
}

// 二次確認按下【✅ 確定服用】後，真正使用背包中的丹藥
function confirmUsePillFromBag() {
  if (!pendingUsePillId || !player.inventory) return;
  const itemIdx = player.inventory.findIndex(i => i && String(i.id) === String(pendingUsePillId));

  if (itemIdx === -1) {
    addLog('【服丹提示】背包中未找到該丹藥！', 'log-system');
    closePillConfirmModal();
    return;
  }

  const pill = player.inventory[itemIdx];
  const cleanName = pill.name ? pill.name.replace(/《|》/g, '') : '靈丹';
  const isProtectPill = cleanName.includes('保具定海丹') || cleanName.includes('定海') || pill.recipeId === 'recipe_protect_pill';

  // 特殊處理：如果是精煉保護丹藥《保具定海丹》，提醒無須吞服並維持數量不變
  if (isProtectPill) {
    addLog(`【丹藥說明】《保具定海丹》為精煉專用秘丹，無須直接吞服！在「🔨 五行鍛造坊」精練法寶失敗時會全自動為您消耗 1 顆並保住法寶。`, 'log-crit');
    closePillConfirmModal();
    return;
  }

  const recipe = PILL_RECIPES.find(r => r.id === pill.recipeId || r.name === cleanName || r.name === `《${cleanName}》`);

  let msg = '';
  if (typeof pill.action === 'function') {
    msg = pill.action(player);
  } else if (recipe && typeof recipe.action === 'function') {
    msg = recipe.action(player);
  } else {
    // 預設靈丹效果：回復 30% ~ 50% 氣血
    const healHp = Math.floor(player.maxHp * 0.4);
    player.hp = Math.min(player.maxHp, player.hp + healHp);
    msg = `使用【${cleanName}】，瞬間恢復 ${healHp} 點氣血！`;
  }

  // 扣除 1 顆背包丹藥
  pill.count = (pill.count || 1) - 1;
  if (pill.count <= 0) {
    player.inventory.splice(itemIdx, 1);
  }

  closePillConfirmModal();

  audioSynth.sfxReward();
  addLog(`【💊 服丹成功】${msg}`, 'log-crit');

  recalculatePlayerStats();
  saveGame();
  updateUI();
  renderInventory();
}

function equipItem(itemId) {
  if (!player.inventory || !Array.isArray(player.inventory)) return;

  ensurePlayerEquipped();

  // 若 itemId 傳入未定義，或者尋找匹配
  let itemIdx = player.inventory.findIndex(i => i && i.id !== undefined && String(i.id) === String(itemId));
  
  if (itemIdx === -1 && typeof itemId === 'object' && itemId !== null) {
    itemIdx = player.inventory.findIndex(i => i === itemId);
  }

  if (itemIdx === -1) {
    addLog(`【裝備失敗】未在乾坤背包中找到該法寶，請嘗試重新點擊或整理背包。`, 'log-monster');
    return;
  }

  const item = player.inventory[itemIdx];

  // 關鍵修復：當玩家在背包中點擊丹藥類物品時，彈出二次確認 Modal 彈窗！
  if (item && item.type === 'pill') {
    openPillConfirmModal(item.id);
    return;
  }
  // 確保 item 必定有唯一 ID
  if (!item.id) item.id = 'item_' + Date.now() + '_' + Math.floor(Math.random() * 10000);

  const slotType = normalizeSlotType(item);

  // 若目標槽位已有舊裝備，卸下放回背包
  const currentEquipped = player.equipped[slotType];
  if (currentEquipped && currentEquipped.name) {
    player.inventory.push(currentEquipped);
  }

  player.equipped[slotType] = item;
  player.inventory.splice(itemIdx, 1);

  audioSynth.sfxReward();
  recalculatePlayerStats();
  saveGame();
  updateUI();
  renderInventory();

  if (slotType === 'pill') {
    addLog(`【丹藥入槽】已將【${item.name}】成功放入丹藥槽！`, 'log-crit');
  } else {
    addLog(`【穿戴成功】已成功穿戴【${item.name}】！全屬性已同步大幅加成！`, 'log-crit');
  }
}

// 卸下已裝備槽位的裝備放回背包
function unequipItem(slotType) {
  if (!player.equipped || !player.equipped[slotType]) return;

  const item = player.equipped[slotType];
  if (!player.inventory) player.inventory = [];

  // 丹藥特殊處理：自動堆疊歸還
  if (slotType === 'pill') {
    addPillToInventory(item, item.count || 1);
  } else {
    player.inventory.push(item);
  }
  player.equipped[slotType] = null;

  audioSynth.sfxHit();
  recalculatePlayerStats();
  saveGame();
  updateUI();
  renderInventory();
  addLog(`【卸下裝備】已將【${item.name}】收回乾坤背包。`, 'log-system');
}

// ⚡ 一鍵自動裝備最強法寶與丹藥 (自動掃描背包選擇屬性最高者穿戴)
function autoEquipBestItems() {
  if (!player.inventory || !Array.isArray(player.inventory) || player.inventory.length === 0) {
    addLog('【自動裝備提示】乾坤背包為空！請先歷練擊敗魔王或前往鍛造獲取法寶。', 'log-system');
    return;
  }

  if (!player.equipped || typeof player.equipped !== 'object') {
    player.equipped = { weapon: null, armor: null, accessory: null, pill: null };
  }

  let equippedCount = 0;
  const slotTypes = ['weapon', 'armor', 'accessory', 'pill'];

  slotTypes.forEach(targetSlot => {
    // 透過 normalizeSlotType 找出背包中所有對應此槽位的物品
    const candidateItems = player.inventory.filter(item => item && normalizeSlotType(item) === targetSlot);

    if (candidateItems.length === 0) return;

    // 核心戰鬥屬性加權評分
    const getItemScore = (item) => {
      if (!item) return 0;
      const q = item.quality || 1;
      const atk = item.atk || 0;
      const def = item.def || 0;
      if (targetSlot === 'weapon') return (atk * 3 + def * 1) + q * 10;
      if (targetSlot === 'armor') return (def * 3 + atk * 1) + q * 10;
      if (targetSlot === 'accessory') return (atk * 2 + def * 2) + q * 10;
      if (targetSlot === 'pill') return q * 100 + (item.count || 1);
      return (atk + def) + q * 10;
    };

    // 按屬性評分由高至低排序
    candidateItems.sort((a, b) => getItemScore(b) - getItemScore(a));

    const bestItem = candidateItems[0];
    const currentEquipped = player.equipped[targetSlot];

    let shouldReplace = false;
    if (!currentEquipped || !currentEquipped.name) {
      shouldReplace = true;
    } else {
      if (getItemScore(bestItem) > getItemScore(currentEquipped)) {
        shouldReplace = true;
      }
    }

    if (shouldReplace) {
      const idx = player.inventory.findIndex(i => i && String(i.id) === String(bestItem.id));
      if (idx !== -1) {
        player.inventory.splice(idx, 1);
      }

      if (currentEquipped && currentEquipped.name) {
        if (targetSlot === 'pill') {
          addPillToInventory(currentEquipped, currentEquipped.count || 1);
        } else {
          player.inventory.push(currentEquipped);
        }
      }

      player.equipped[targetSlot] = bestItem;
      equippedCount++;
    }
  });

  if (equippedCount > 0) {
    audioSynth.sfxReward();
    recalculatePlayerStats();
    saveGame();
    updateUI();
    renderInventory();
    addLog(`【⚡ 一鍵自動裝備成功】已全自動掃描乾坤背包，為您穿戴當前最強 ${equippedCount} 件法寶/寶鎧/丹藥！`, 'log-crit');
  } else {
    addLog(`【一鍵自動裝備】目前已穿戴當前背包中最高戰鬥屬性之法寶與寶鎧！`, 'log-system');
  }
}

function collectAlchemyResult(idx) {
  const furnace = player.alchFurnaces ? player.alchFurnaces[idx] : null;
  if (!furnace) return;

  const dur = furnace.duration || 15000;
  const isTimeUp = Date.now() >= (furnace.startTime + dur);

  if (furnace.status !== 'completed' && !isTimeUp) {
    addLog(`【丹爐煉化中】${furnace.name} 尚在開火煉化中，請稍候...`, 'log-system');
    return;
  }

  const recipe = PILL_RECIPES.find(r => r.id === furnace.recipeId);
  if (!recipe) return;

  const pillItem = {
    id: Date.now() + Math.random(),
    type: 'pill',
    recipeId: recipe.id,
    name: recipe.name,
    icon: recipe.icon,
    quality: recipe.quality === '極品' ? 5 : recipe.quality === '上品' ? 4 : recipe.quality === '中品' ? 3 : 2,
    qualityColor: recipe.quality === '極品' ? '#e74c3c' : recipe.quality === '上品' ? '#f1c40f' : recipe.quality === '中品' ? '#9b59b6' : '#3498db',
    desc: recipe.desc,
    atk: 0, def: 0
  };

  audioSynth.sfxReward();
  addLog(`【✨ 收穫丹藥】神鼎出丹！成功從 ${furnace.name} 取出【${recipe.name}】正式收入乾坤背包！`, 'log-crit');

  addPillToInventory(pillItem, 1);

  // 關鍵修復：收取完成後將丹爐重置為空閒狀態
  furnace.status = 'idle';
  furnace.recipeId = null;
  furnace.startTime = 0;
  furnace.duration = 0;

  updateUI();
  renderFurnacesUI();
  saveGame();
}

// ============================================
// 坊市商鋪系統
// ============================================
function renderShopTab() {
  const container = document.getElementById('general-shop-grid');
  if (!container) return;
  container.innerHTML = '';

  document.getElementById('shop-player-coins').textContent = player.coins;

  SHOP_ITEMS.forEach(item => {
    const canBuy = player.coins >= item.price;
    const card = document.createElement('div');
    card.className = 'shop-card';
    card.innerHTML = `
      <div class="shop-header">
        <span style="font-weight:bold; color:#fff;">${item.icon} ${item.name}</span>
        <span style="color:var(--pixel-gold); font-weight:bold; font-size:0.85rem;">💰 ${item.price}</span>
      </div>
      <div style="font-size:0.75rem; color:var(--text-muted);">${item.desc || '坊市嚴選貨品'}</div>
      <button class="pixel-btn ${canBuy ? 'btn-gold' : ''}" ${canBuy ? '' : 'disabled'}
              style="margin-top:6px; font-size:0.8rem;" onclick="buyShopItem('${item.id}')">
        🛒 購入商品
      </button>
    `;
    container.appendChild(card);
  });
}

function buyShopItem(itemId) {
  const item = SHOP_ITEMS.find(i => i.id === itemId);
  if (!item) return;

  if (player.coins < item.price) {
    addLog(`【靈石不足】坊市老闆搖搖頭：「靈石不夠，無法購入 ${item.name}！」`, 'log-monster');
    return;
  }

  player.coins -= item.price;
  audioSynth.sfxReward();

  if (item.category === 'pill') {
    const pillItem = {
      id: Date.now() + Math.random(),
      type: 'pill',
      recipeId: 'recipe_small_hp',
      name: '《回氣小還丹》',
      icon: '💊',
      quality: 2,
      qualityColor: '#2ecc71',
      desc: '吞服後回復 50% 最大氣血',
      atk: 0, def: 0
    };
    player.inventory.push(pillItem);
    addLog(`【坊市購入】成功購買【《回氣小還丹》】放入背包！`, 'log-drop');
  } else if (item.category === 'herb') {
    player.herbs[item.key] = (player.herbs[item.key] || 0) + 1;
    const herbNameMap = { lingzhi:'靈芝草', baicao:'百草露', zhusha:'硃砂果', longkui:'龍葵花', renshen:'千年人參' };
    addLog(`【坊市購入】成功購買【${herbNameMap[item.key]}】×1！`, 'log-drop');
  } else if (item.category === 'material') {
    player.materials[item.key] += (item.count || 1);
    const matNameMap = { goldMat:'金精石', woodMat:'神木芯', waterMat:'玄冰髓', fireMat:'朱雀羽', earthMat:'息壤土' };
    addLog(`【坊市購入】成功購買【${matNameMap[item.key]}】×${item.count || 1}！`, 'log-drop');
  } else if (item.category === 'furnace') {
    const listKey = item.furnaceType === 'alchemy' ? 'alchFurnaces' : 'forgeFurnaces';
    let furnaces = player[listKey];

    // 尋找第一個 null 的空槽位
    let emptyIdx = furnaces.findIndex(f => f === null);
    if (emptyIdx !== -1) {
      furnaces[emptyIdx] = {
        id: Date.now(),
        name: item.name,
        level: item.level,
        speedMult: item.speedMult,
        status: 'idle',
        recipeId: null, forgeData: null, startTime: 0, duration: 0
      };
      addLog(`【神器入庫】成功購入【${item.name}】，解鎖 #${emptyIdx+1} 號${item.furnaceType === 'alchemy' ? '丹爐' : '鍛造爐'}位！`, 'log-crit');
    } else {
      // 若 5 個槽位已滿，尋找 lowest level 的舊爐具升級替換
      let minLvlIdx = 0;
      let minLvl = 99;
      furnaces.forEach((f, idx) => {
        if (f && f.level < minLvl) { minLvl = f.level; minLvlIdx = idx; }
      });
      if (item.level > minLvl) {
        const oldName = furnaces[minLvlIdx].name;
        furnaces[minLvlIdx] = {
          id: Date.now(),
          name: item.name,
          level: item.level,
          speedMult: item.speedMult,
          status: 'idle',
          recipeId: null, forgeData: null, startTime: 0, duration: 0
        };
        addLog(`【爐具升級】成功購入【${item.name}】，將原 #${minLvlIdx+1} 號【${oldName}】升級為高階神器！`, 'log-crit');
      } else {
        addLog(`【提示】你的所有爐位已滿，且當前已有同級或更高階的爐具！`, 'log-system');
        player.coins += item.price; // 退還靈石
        return;
      }
    }
  }

  updateUI();
  renderShopTab();
}



// ⚡ 一鍵自動裝備最強法寶與丹藥 (自動掃描背包選擇屬性最高者穿戴)

// 🧹 一鍵整理背包 (按品質與攻防數據降序排列)
function sortInventoryByStats() {
  if (!player.inventory || !Array.isArray(player.inventory) || player.inventory.length <= 0) {
    addLog('【背包整理提示】背包物品數量較少，無需整理。', 'log-system');
    return;
  }

  // 先將背包中所有同名丹藥歸併合體為單一格子
  consolidateInventoryPills();

  player.inventory.sort((a, b) => {
    const scoreA = (a.quality || 1) * 1000 + (a.atk || 0) * 2 + (a.def || 0);
    const scoreB = (b.quality || 1) * 1000 + (b.atk || 0) * 2 + (b.def || 0);
    if (scoreB !== scoreA) {
      return scoreB - scoreA;
    }
    const typeOrder = { weapon: 1, armor: 2, accessory: 3, pill: 4 };
    const orderA = typeOrder[a.type] || 5;
    const orderB = typeOrder[b.type] || 5;
    return orderA - orderB;
  });

  audioSynth.sfxCraft();
  saveGame();
  renderInventory();
  updateUI();
  addLog(`【🧹 背包整理】乾坤背包已全自動將所有同名丹藥歸併合體，並按品質數據由高至低重新整齊排列！`, 'log-crit');
}

// 重新計算屬性 (算入裝備、蒼靈根115% 與心法加成)
function recalculatePlayerStats() {
  let extraAtk = 0;
  let extraDef = 0;
  let extraHp = 0;

  // 裝備加成 (算入法寶精煉 +1%~+10% 全屬性威力)
  Object.values(player.equipped).forEach(eq => {
    if (eq) {
      const refineMult = 1 + (eq.refineLvl || 0) * 0.01;
      extraAtk += Math.floor((eq.atk || 0) * refineMult);
      extraDef += Math.floor((eq.def || 0) * refineMult);
      extraHp += Math.floor((eq.hp || 0) * refineMult);
    }
  });

  // 心法加成
  player.purchasedSutras.forEach(id => {
    const s = ALL_SUTRAS.find(item => item.id === id);
    if (s) {
      extraAtk += s.atk || 0;
      extraDef += s.def || 0;
      extraHp += s.hp || 0;
    }
  });

  const mult = player.statMult || 1.0;
  player.maxHp = Math.floor((200 + (player.level - 1) * 40 + extraHp) * mult);
  player.atk = Math.floor(((35 + (player.level - 1) * 8) + extraAtk) * mult);
  player.def = Math.floor(((10 + (player.level - 1) * 4) + extraDef) * mult);
}

function salvageCommonItems() {
  const chks = document.querySelectorAll('.chk-salvage-quality:checked');
  const selectedQualities = Array.from(chks).map(el => parseInt(el.value));

  if (selectedQualities.length === 0) {
    addLog('【熔練提示】請至少在上方勾選一種要熔練的裝備品級！', 'log-system');
    return;
  }

  let count = 0;
  let matReturnCount = 0;
  const matKeys = ['goldMat', 'woodMat', 'waterMat', 'fireMat', 'earthMat'];

  let totalCoinsGained = 0;
  const salvageMult = GAME_CONFIG.salvageCoinMult || 1.0;

  player.inventory = player.inventory.filter(item => {
    if (item.type !== 'pill' && selectedQualities.includes(item.quality)) {
      count++;
      const gained = Math.floor(item.quality * 50 * salvageMult);
      player.coins += gained;
      totalCoinsGained += gained;
      player.materials[matKeys[Math.floor(Math.random() * matKeys.length)]] += item.quality;
      matReturnCount += item.quality;
      return false;
    }
    return true;
  });

  if (count > 0) {
    audioSynth.sfxReward();
    addLog(`【三昧一鍵熔練】成功熔練 ${count} 件已勾選品級裝備，獲得靈石與 ${matReturnCount} 個五行神材！`, 'log-drop');
    updateUI();
  } else {
    addLog('【熔練提示】背包中沒有符合目前已勾選品級的裝備可供熔練。', 'log-system');
  }
}

function saveGMSettings() {
  if (document.getElementById('cfg-rate-q1')) {
    GAME_CONFIG.baseQualityRates = {
      q1: parseFloat(document.getElementById('cfg-rate-q1').value) || 40,
      q2: parseFloat(document.getElementById('cfg-rate-q2').value) || 30,
      q3: parseFloat(document.getElementById('cfg-rate-q3').value) || 18,
      q4: parseFloat(document.getElementById('cfg-rate-q4').value) || 8,
      q5: parseFloat(document.getElementById('cfg-rate-q5').value) || 3,
      q6: parseFloat(document.getElementById('cfg-rate-q6').value) || 1
    };
  }
  if (document.getElementById('cfg-boss-spawn-rate')) {
    GAME_CONFIG.bossSpawnRate = Math.min(1.0, Math.max(0.01, (parseFloat(document.getElementById('cfg-boss-spawn-rate').value) || 20) / 100));
  }
  if (document.getElementById('cfg-monster-hp-mult')) {
    GAME_CONFIG.monsterHpMult = parseFloat(document.getElementById('cfg-monster-hp-mult').value) || 2.0;
  }
  if (document.getElementById('cfg-monster-atk-mult')) {
    GAME_CONFIG.monsterAtkMult = parseFloat(document.getElementById('cfg-monster-atk-mult').value) || 1.8;
  }
  saveGame();
  addLog('【天道設置】GM 參數已保存！', 'log-system');
  updateUI();
}

function loadGMConfigToInputs() {
  if (document.getElementById('cfg-equip-drop-rate')) document.getElementById('cfg-equip-drop-rate').value = (GAME_CONFIG.equipDropRate || 0.2) * 100;
  if (document.getElementById('cfg-boss-spawn-rate')) document.getElementById('cfg-boss-spawn-rate').value = (GAME_CONFIG.bossSpawnRate || 0.2) * 100;
  if (document.getElementById('cfg-monster-hp-mult')) document.getElementById('cfg-monster-hp-mult').value = GAME_CONFIG.monsterHpMult || 2.0;
  if (document.getElementById('cfg-monster-atk-mult')) document.getElementById('cfg-monster-atk-mult').value = GAME_CONFIG.monsterAtkMult || 1.8;
}

function resetGMConfig() {
  GAME_CONFIG = {
    bossSpawnRate: 0.2,
    monsterHpMult: 2.0,
    monsterAtkMult: 1.8,
    equipDropRate: 0.2,
    bossDropEquipRate: 0.5,
    normalDropEquipRate: 0.2,
    salvageCoinMult: 1.0,
    expMult: 1.0,
    coinMult: 1.0,
    herbDropRate: 0.2,
    baseCritRate: 0.1,
    critDamageMult: 2.0,
    suppressionMult: 1.5,
    forgeBaseTime: 60,
    alchBaseTime: 60,
    meditateMult: 5,
    eventRate: 0.06,
    merchantRate: 0.2
  };
  saveGame();
  loadGMConfigToInputs();
  addLog('【天道設置】GM 參數已恢復默認值！', 'log-system');
  updateUI();
}

function claimGiftCode() {
  const inputEl = document.getElementById('gift-code-input');
  const code = inputEl.value.trim();
  if (!code) return;

  if (player.usedCodes.includes(code)) {
    addLog(`【禮包失敗】您已經使用過該禮包碼【${code}】了！`, 'log-monster');
    document.getElementById('gift-modal').classList.remove('show');
    return;
  }

  let claimed = false;
  if (code === '歡迎萌新') {
    player.coins += 8888;
    Object.keys(player.materials).forEach(k => player.materials[k] += 20);
    claimed = true;
  } else if (code === '極品紅裝') {
    const redEquip = {
      id: Date.now(),
      name: '洪荒·神品五行聖劍',
      type: 'weapon',
      element: 'gold',
      quality: 6,
      qualityName: '神品',
      qualityColor: '#e74c3c',
      atk: 180,
      def: 40,
      icon: '🗡️'
    };
    player.inventory.push(redEquip);
    claimed = true;
  } else if (code === '我要發財') {
    player.coins += 88888;
    claimed = true;
  } else if (code === '大吉大利') {
    Object.keys(player.materials).forEach(k => player.materials[k] += 99);
    claimed = true;
  }

  if (claimed) {
    player.usedCodes.push(code);
    audioSynth.sfxReward();
    addLog(`【禮包兌換成功】恭喜獲得禮包【${code}】豐富資源獎勵！`, 'log-crit');
    inputEl.value = '';
    document.getElementById('gift-modal').classList.remove('show');
    updateUI();
  } else {
    addLog(`【無效禮包碼】「${code}」不存在。請嘗試：歡迎萌新、極品紅裝、我要發財、大吉大利`, 'log-monster');
    document.getElementById('gift-modal').classList.remove('show');
  }
}

function updateUI() {
  ensurePlayerFurnaces();
  renderFurnacesUI();

  document.getElementById('ui-player-name').textContent = player.name;
  document.getElementById('ui-realm').textContent = getRealmName(player.level);
  document.getElementById('ui-level').textContent = player.level;
  document.getElementById('ui-coins').textContent = player.coins;

  const elemTag = document.getElementById('ui-element-tag');
  elemTag.textContent = ELEMENT_NAMES[player.element] || '五行系';
  elemTag.className = `element-tag element-${player.element}`;

  const rootTag = document.getElementById('ui-root-type-tag');
  if (rootTag) {
    rootTag.textContent = player.rootName || '天單靈根';
    if (player.rootType === 'azure') {
      rootTag.className = 'element-tag element-azure';
    } else {
      rootTag.className = 'element-tag';
      rootTag.style.background = '#8e44ad';
    }
  }

  // 顯示總修練速度 (靈根 + 心法)
  const totalExpSpeed = calculateTotalExpSpeed();
  document.getElementById('ui-exp-speed').textContent = `${Math.floor(totalExpSpeed * 100)}%`;

  document.getElementById('hp-fill').style.width = `${Math.min(100, (player.hp / player.maxHp) * 100)}%`;
  document.getElementById('hp-text').textContent = `${player.hp} / ${player.maxHp}`;

  document.getElementById('exp-fill').style.width = `${Math.min(100, (player.exp / player.maxExp) * 100)}%`;
  document.getElementById('exp-text').textContent = `${player.exp} / ${player.maxExp}`;

  document.getElementById('stat-atk').textContent = player.atk;
  document.getElementById('stat-def').textContent = player.def;
  document.getElementById('stat-crit').textContent = `${Math.floor(player.critRate * 100)}%`;
  
  if (document.getElementById('stat-pills')) document.getElementById('stat-pills').textContent = player.pills || 0;
  if (document.getElementById('ui-pill-count')) document.getElementById('ui-pill-count').textContent = player.pills || 0;
  if (document.getElementById('stat-regen')) document.getElementById('stat-regen').textContent = `${getRegenAmount()} /3s`;

  document.getElementById('mat-gold').textContent = player.materials.goldMat;
  document.getElementById('mat-wood').textContent = player.materials.woodMat;
  document.getElementById('mat-water').textContent = player.materials.waterMat;
  document.getElementById('mat-fire').textContent = player.materials.fireMat;
  document.getElementById('mat-earth').textContent = player.materials.earthMat;

  renderEquippedSlots();
  renderInventory();

  // 更新頂部背包分頁按鈕數量標記
  const bagTabBtn = document.querySelector('.tab-btn[data-tab="bag"]');
  if (bagTabBtn) {
    const invCount = (player.inventory && Array.isArray(player.inventory)) ? player.inventory.length : 0;
    bagTabBtn.textContent = `🎒 乾坤背包 (${invCount})`;
  }

  // 負傷休養狀態 UI 鎖定與標籤
  const injuryBadge = document.getElementById('injury-badge');
  const btnAttack = document.getElementById('btn-manual-attack');
  const btnAuto = document.getElementById('btn-toggle-auto');

  if (player.isInjured) {
    if (injuryBadge) injuryBadge.style.display = 'block';
    if (btnAttack) {
      btnAttack.disabled = true;
      btnAttack.style.opacity = '0.4';
      btnAttack.textContent = '🤕 負傷休養中...';
    }
    if (btnAuto) {
      btnAuto.disabled = true;
      btnAuto.style.opacity = '0.4';
    }
  } else {
    if (injuryBadge) injuryBadge.style.display = 'none';
    if (btnAttack) {
      btnAttack.disabled = false;
      btnAttack.style.opacity = '1';
      btnAttack.textContent = '⚔️ 挑戰單次';
    }
    if (btnAuto) {
      btnAuto.disabled = false;
      btnAuto.style.opacity = '1';
    }
  }

  // 若當前在煉丹或商鋪分頁，自動繪製
  const activeTab = document.querySelector('.tab-btn.active');
  if (activeTab) {
    const tabId = activeTab.getAttribute('data-tab');
    if (tabId === 'alchemy') renderAlchemyTab();
    if (tabId === 'shop') renderShopTab();
  }

  updateForgeCostDisplay();
  renderFurnacesUI();
  updateSutraBonusPanels();
  updateMonsterUI();
  renderEquippedSlots();
}

function renderEquippedSlots() {
  const eq = player.equipped;
  if (!eq) return;
  ['weapon', 'armor', 'accessory', 'pill'].forEach(type => {
    const el = document.getElementById(`eq-${type}`);
    const infoEl = document.getElementById(`eq-${type}-info`);
    if (!el) return;

    const slotTitleMap = { weapon: '武器', armor: '防具', accessory: '飾品', pill: '丹藥' };
    const typeTitle = slotTitleMap[type] || '裝備';

    if (eq[type]) {
      const item = eq[type];
      el.style.borderColor = item.qualityColor || '#f1c40f';
      el.innerHTML = `<span style="font-size:1.3rem;">${item.icon}</span>`;
      el.onclick = () => unequipItem(type);

      if (infoEl) {
        let statText = '';
        const refLvl = item.refineLvl || 0;
        const atkAdd = Math.floor((item.atk || 0) * (refLvl / 100));
        const defAdd = Math.floor((item.def || 0) * (refLvl / 100));

        if (type === 'pill') {
          const cnt = item.count || 1;
          statText = `💊 持有: ${cnt} 顆 (服完自動補)`;
        } else if (item.atk && item.def) {
          statText = `⚔️攻+${item.atk}${refLvl > 0 ? `<span style="color:#f1c40f;">(+${atkAdd})</span>` : ''} 🛡️防+${item.def}${refLvl > 0 ? `<span style="color:#f1c40f;">(+${defAdd})</span>` : ''}`;
        } else if (item.atk) {
          statText = `⚔️ 攻擊: +${item.atk}${refLvl > 0 ? `<span style="color:#f1c40f;"> (+${atkAdd})</span>` : ''}`;
        } else if (item.def) {
          statText = `🛡️ 防禦: +${item.def}${refLvl > 0 ? `<span style="color:#f1c40f;"> (+${defAdd})</span>` : ''}`;
        } else {
          statText = `✨ 已裝備備用`;
        }

        const nameWithLvl = refLvl > 0 ? `${item.name} <span style="color:#f1c40f;">+${refLvl}</span>` : item.name;

        infoEl.innerHTML = `
          <span style="font-size:0.75rem; color:${item.qualityColor || '#f1c40f'}; font-weight:bold; white-space:nowrap; text-overflow:ellipsis; overflow:hidden; max-width:140px;">${nameWithLvl}</span>
          <span style="font-size:0.68rem; color:#2ecc71; font-weight:bold; line-height:1.2; white-space:nowrap;">${statText}</span>
        `;
      }
    } else {
      el.style.borderColor = type === 'pill' ? '#e74c3c' : '#3d3d63';
      el.innerHTML = `<span style="color:#666; font-size:0.7rem;">空</span>`;
      el.onclick = null;

      if (infoEl) {
        infoEl.innerHTML = `
          <span style="font-size:0.75rem; color:#888; font-weight:bold;">${typeTitle}：未穿戴</span>
          <span style="font-size:0.68rem; color:#666;">--</span>
        `;
      }
    }
  });
}

// 自動將背包中既有的同名丹藥秒速歸併合體為單一格子
function consolidateInventoryPills() {
  if (!player.inventory || !Array.isArray(player.inventory)) return;

  const newInventory = [];
  const pillMap = new Map();

  player.inventory.forEach(item => {
    if (!item) return;

    if (item.type === 'pill' || (item.name && item.name.includes('丹'))) {
      const cleanName = item.name.replace(/《|》/g, '');
      const count = item.count || 1;

      if (pillMap.has(cleanName)) {
        const existingPill = pillMap.get(cleanName);
        existingPill.count = (existingPill.count || 1) + count;
      } else {
        item.name = cleanName;
        item.count = count;
        pillMap.set(cleanName, item);
        newInventory.push(item);
      }
    } else {
      newInventory.push(item);
    }
  });

  player.inventory = newInventory;
}

function renderInventory() {
  const container = document.getElementById('inventory-grid');
  if (!container) return;

  // 繪製前先全自動把背包裡所有的同名丹藥秒速歸併為單一格子！
  consolidateInventoryPills();

  container.innerHTML = '';

  if (!player.inventory) player.inventory = [];

  player.inventory.forEach(item => {
    if (!item) return;
    if (!item.id) item.id = 'item_' + Date.now() + '_' + Math.floor(Math.random() * 100000);

    const slot = document.createElement('div');
    slot.className = `item-slot item-quality-${item.quality || 1}`;
    slot.setAttribute('draggable', 'true');
    slot.innerHTML = `
      <span class="item-icon">${item.icon || '⚔️'}</span>
      <span style="font-size:0.6rem; color:${item.qualityColor || '#fff'}; text-align:center; line-height:1.1;">${item.name || '法寶'}</span>
    `;

    // 若為丹藥或帶有 count 屬性，繪製右下角堆疊角標 (×1, ×2, ×3)
    if (item.type === 'pill' || item.count) {
      const cnt = item.count || 1;
      const badge = document.createElement('span');
      badge.className = 'item-count-badge';
      badge.textContent = `×${cnt}`;
      slot.appendChild(badge);
    }

    slot.title = `點擊裝備 / 服用\n${item.desc || ''}\n攻: +${item.atk || 0}  防: +${item.def || 0}`;

    slot.addEventListener('dragstart', (e) => {
      e.dataTransfer.setData('text/plain', item.id);
    });

    slot.onclick = () => equipItem(item.id);
    container.appendChild(slot);
  });

  renderSmeltPoolUI();
}

function addLog(text, typeClass = 'log-system') {
  const logBox = document.getElementById('combat-log');
  const entry = document.createElement('div');
  entry.className = `log-entry ${typeClass}`;
  entry.textContent = `> ${text}`;
  logBox.appendChild(entry);
  logBox.scrollTop = logBox.scrollHeight;

  while (logBox.children.length > 80) {
    logBox.removeChild(logBox.firstChild);
  }
}

function saveGame() {
  localStorage.setItem('wujin_honghuang_save', JSON.stringify(player));
}

function loadGame() {
  const saved = localStorage.getItem('wujin_honghuang_save');
  if (saved) {
    try {
      player = Object.assign(player, JSON.parse(saved));
      if (player.pills === undefined) player.pills = 0;
      ensurePlayerEquipped();
      ensurePlayerFurnaces();
      recalculatePlayerStats();
    } catch (e) {
      console.error("Save file load error", e);
    }
  } else {
    document.getElementById('class-select-modal').classList.add('show');
  }
}

// ============================================
// 氣血自然回復計時器 (每 3 秒回復一次)
// ============================================
function getRegenAmount() {
  // 基礎回復 = 2% maxHp，打坐時根據 GAME_CONFIG.meditateMult 倍率增強
  let base = Math.max(4, Math.floor(player.maxHp * 0.02));
  if (isMeditating) base *= (GAME_CONFIG.meditateMult || 5);
  // 一旦被擊倒負傷，恢復速度打折剩下 50%
  if (player.isInjured) base = Math.max(1, Math.floor(base * 0.5));

  // 算入氣血/修為恢復速度丹藥加成 (%)
  const regenBoost = (player.activeRegenPill && player.activeRegenPill.boost) ? player.activeRegenPill.boost : 0;
  return Math.floor(base * (1 + regenBoost));
}

function startRegenTimer() {
  if (regenInterval) clearInterval(regenInterval);
  regenInterval = setInterval(() => {
    if (player.hp < player.maxHp) {
      const regen = getRegenAmount();
      player.hp = Math.min(player.maxHp, player.hp + regen);

      const regenStatus = document.getElementById('regen-status');
      if (regenStatus) {
        if (isMeditating) {
          regenStatus.style.display = 'block';
          regenStatus.style.color = player.isInjured ? '#e74c3c' : '#3498db';
          regenStatus.textContent = player.isInjured ? `🤕 負傷打坐休養中... +${regen} 氣血/3s (負傷速度減半)` : `🧘 打坐調息中... +${regen} 氣血/3s（5倍恢復速度）`;
        } else {
          regenStatus.style.display = 'block';
          regenStatus.style.color = '#e67e22';
          regenStatus.textContent = `💚 氣血自然恢復中... +${regen}/3s`;
        }
      }
      updateUI();
    } else {
      const regenStatus = document.getElementById('regen-status');
      if (regenStatus && !isMeditating) {
        regenStatus.style.display = 'none';
      }
    }

    // 氣血全滿時自動痊癒負傷狀態
    if (player.hp >= player.maxHp && player.isInjured) {
      player.isInjured = false;
      audioSynth.sfxLevelUp();
      addLog(`【💖 傷勢痊癒】氣血已完全補滿，負傷狀態消除！可以重新出外歷練！`, 'log-crit');
      updateUI();
    }

    // 打坐時超慢速增加修為 + 隨機悟道
    if (isMeditating) {
      const totalExpSpeed = calculateTotalExpSpeed();
      const meditateExp = Math.max(1, Math.floor(3 * totalExpSpeed));
      player.exp += meditateExp;

      // 5% 機率隨機悟道秘法
      if (Math.random() < 0.05) {
        const insightTypes = [
          { text: '太初真意', atk: 3, def: 0 },
          { text: '混沌呼吸', atk: 0, def: 3 },
          { text: '天道感悟', atk: 2, def: 2 },
          { text: '五行輪轉', atk: 0, def: 0, exp: 50 },
          { text: '禪定頓悟', atk: 5, def: 0 },
          { text: '龜息大法', atk: 0, def: 5 }
        ];
        const insight = insightTypes[Math.floor(Math.random() * insightTypes.length)];
        if (insight.atk) player.atk += insight.atk;
        if (insight.def) player.def += insight.def;
        if (insight.exp) player.exp += insight.exp;
        audioSynth.sfxLevelUp();
        addLog(`【✨ 悟道秘法】打坐中頓悟【${insight.text}】！${insight.atk ? '攻擊+' + insight.atk + ' ' : ''}${insight.def ? '防禦+' + insight.def + ' ' : ''}${insight.exp ? '修為+' + insight.exp : ''}`, 'log-crit');
      }

      if (player.exp >= player.maxExp) {
        levelUp();
      }
      updateUI();
    }
  }, 3000);
}

// ============================================
// 打坐調息系統
// ============================================
function toggleMeditate() {
  if (isMeditating) {
    stopMeditate();
  } else {
    // 打坐時停止自動掛機
    if (isAutoBattling) toggleAutoBattle();
    isMeditating = true;
    const btn = document.getElementById('btn-meditate');
    btn.textContent = '🧘 停止打坐';
    btn.classList.add('btn-gold');
    // 禁用攻擊按鈕
    document.getElementById('btn-manual-attack').disabled = true;
    document.getElementById('btn-manual-attack').style.opacity = '0.4';
    document.getElementById('btn-toggle-auto').disabled = true;
    document.getElementById('btn-toggle-auto').style.opacity = '0.4';

    const regenStatus = document.getElementById('regen-status');
    if (regenStatus) {
      regenStatus.style.display = 'block';
      regenStatus.style.color = '#3498db';
      regenStatus.textContent = '🧘 打坐調息中... 5倍恢復速度 + 超慢增加修為 + 隨機悟道秘法';
    }
    addLog(`【打坐調息】盤膝而坐，運轉周天靈氣，氣血回復速度提升 5 倍！超慢速增加修為，有機率頓悟秘法！`, 'log-element');
  }
}

function stopMeditate() {
  isMeditating = false;
  const btn = document.getElementById('btn-meditate');
  btn.textContent = '🧘 打坐調息';
  btn.classList.remove('btn-gold');
  document.getElementById('btn-manual-attack').disabled = false;
  document.getElementById('btn-manual-attack').style.opacity = '1';
  document.getElementById('btn-toggle-auto').disabled = false;
  document.getElementById('btn-toggle-auto').style.opacity = '1';
  const regenStatus = document.getElementById('regen-status');
  if (regenStatus) {
    if (player.hp >= player.maxHp) regenStatus.style.display = 'none';
    else {
      regenStatus.style.color = '#e67e22';
      regenStatus.textContent = '💚 氣血自然恢復中...';
    }
  }
  addLog(`【調息結束】起身離坐，靈力充盈身體。`, 'log-system');
}

// ============================================
// 丹藥系統
// ============================================
function buyPill() {
  const pillCost = 200;
  if (player.coins < pillCost) {
    addLog(`【靈石不足】購買回氣丹需要 ${pillCost} 靈石，你的靈石不夠！`, 'log-monster');
    return;
  }
  player.coins -= pillCost;
  
  const huiqiPill = {
    id: 'pill_huiqi_' + Date.now(),
    name: '回氣丹',
    type: 'pill',
    quality: '良品',
    qualityColor: '#3498db',
    icon: '💊',
    desc: '經典療傷靈丹，恢復 50% 氣血',
    count: 1,
    action: (p) => {
      const heal = Math.floor(p.maxHp * 0.5);
      p.hp = Math.min(p.maxHp, p.hp + heal);
      return `吞服【回氣丹】，瞬間恢復 ${heal} 點氣血！`;
    }
  };

  addPillToInventory(huiqiPill, 1);
  if (typeof audioSynth !== 'undefined' && audioSynth.sfxReward) audioSynth.sfxReward();
  saveGame();
  updateUI();
  renderInventory();
}

function usePill() {
  if (player.pills <= 0) {
    addLog(`【丹藥用盡】你沒有回氣丹了！可點擊「🛒 買丹」購買。`, 'log-monster');
    return;
  }
  if (player.hp >= player.maxHp) {
    addLog(`【氣血已滿】你的氣血已經是滿的，無需服用丹藥。`, 'log-system');
    return;
  }

  player.pills -= 1;
  const healAmount = Math.floor(player.maxHp * 0.5);
  player.hp = Math.min(player.maxHp, player.hp + healAmount);
  audioSynth.sfxReward();
  addLog(`【服丹療傷】吞服【回氣丹】，瞬間恢復 ${healAmount} 點氣血！剩餘丹藥 ${player.pills} 顆。`, 'log-crit');
  updateUI();
}

// ============================================
// 天道 GM 控制台密碼解鎖與參數保存
// ============================================
function unlockGMSettings() {
  const passInput = document.getElementById('gm-password-input').value.trim();
  const errEl = document.getElementById('gm-pass-error');
  if (passInput === '1234') {
    isGMUnlocked = true;
    document.getElementById('gm-lock-screen').style.display = 'none';
    document.getElementById('gm-panel').style.display = 'flex';
    errEl.style.display = 'none';
    audioSynth.sfxLevelUp();
    addLog('【天道驗證】天道印記密碼解鎖成功！天道 GM 動態控制台面板已開啓！', 'log-crit');
    loadGMConfigToInputs();
    startOnlinePlayerCounter();
  } else {
    errEl.style.display = 'block';
    audioSynth.sfxHit();
  }
}

function loadGMConfigToInputs() {
  const savedCfg = localStorage.getItem('wujin_honghuang_gm_config');
  if (savedCfg) {
    try {
      GAME_CONFIG = Object.assign(GAME_CONFIG, JSON.parse(savedCfg));
      applyUIScale();
    } catch (e) {}
  }
  if (document.getElementById('cfg-event-rate')) {
    document.getElementById('cfg-event-rate').value = Math.floor(GAME_CONFIG.eventRate * 100);
    document.getElementById('cfg-merchant-rate').value = Math.floor(GAME_CONFIG.merchantRate * 100);
    document.getElementById('cfg-exp-mult').value = GAME_CONFIG.expMult;
    document.getElementById('cfg-coin-mult').value = GAME_CONFIG.coinMult;
    document.getElementById('cfg-herb-rate').value = Math.floor((GAME_CONFIG.herbDropRate || 0.2) * 100);
    document.getElementById('cfg-meditate-mult').value = GAME_CONFIG.meditateMult;
    document.getElementById('cfg-azure-rate').value = Math.floor(GAME_CONFIG.azureRate * 100);

    document.getElementById('cfg-monster-hp-mult').value = GAME_CONFIG.monsterHpMult || 2.0;
    document.getElementById('cfg-monster-atk-mult').value = GAME_CONFIG.monsterAtkMult || 1.8;
    document.getElementById('cfg-suppression-mult').value = GAME_CONFIG.suppressionMult || 1.5;

    document.getElementById('cfg-base-crit').value = Math.floor((GAME_CONFIG.baseCritRate || 0.1) * 100);
    document.getElementById('cfg-crit-dmg-mult').value = GAME_CONFIG.critDamageMult || 2.0;
    document.getElementById('cfg-salvage-coin-mult').value = GAME_CONFIG.salvageCoinMult || 1.0;

    document.getElementById('cfg-alch-base-time').value = GAME_CONFIG.alchBaseTime || 60;
    document.getElementById('cfg-forge-base-time').value = GAME_CONFIG.forgeBaseTime || 60;
    if (document.getElementById('cfg-rate-q1')) {
      const bRates = GAME_CONFIG.baseQualityRates || { q1: 40, q2: 30, q3: 18, q4: 8, q5: 3, q6: 1 };
      document.getElementById('cfg-rate-q1').value = bRates.q1;
      document.getElementById('cfg-rate-q2').value = bRates.q2;
      document.getElementById('cfg-rate-q3').value = bRates.q3;
      document.getElementById('cfg-rate-q4').value = bRates.q4;
      document.getElementById('cfg-rate-q5').value = bRates.q5;
      document.getElementById('cfg-rate-q6').value = bRates.q6;
    }
    if (document.getElementById('cfg-boss-spawn-rate')) {
      document.getElementById('cfg-boss-spawn-rate').value = Math.floor((GAME_CONFIG.bossSpawnRate !== undefined ? GAME_CONFIG.bossSpawnRate : 0.20) * 100);
    }
    if (document.getElementById('cfg-boss-drop-rate')) {
      document.getElementById('cfg-boss-drop-rate').value = Math.floor((GAME_CONFIG.bossDropEquipRate !== undefined ? GAME_CONFIG.bossDropEquipRate : 0.50) * 100);
    }
    if (document.getElementById('cfg-normal-drop-rate')) {
      document.getElementById('cfg-normal-drop-rate').value = Math.floor((GAME_CONFIG.normalDropEquipRate !== undefined ? GAME_CONFIG.normalDropEquipRate : 0.10) * 100);
    }
    if (document.getElementById('cfg-ui-scale')) {
      document.getElementById('cfg-ui-scale').value = Math.floor((GAME_CONFIG.uiScale || 1.0) * 100);
    }
  }
}

function saveGMSettings() {
  GAME_CONFIG.eventRate = parseFloat(document.getElementById('cfg-event-rate').value || 6) / 100;
  GAME_CONFIG.merchantRate = parseFloat(document.getElementById('cfg-merchant-rate').value || 20) / 100;
  GAME_CONFIG.expMult = parseFloat(document.getElementById('cfg-exp-mult').value || 1.0);
  GAME_CONFIG.coinMult = parseFloat(document.getElementById('cfg-coin-mult').value || 1.0);
  GAME_CONFIG.herbDropRate = parseFloat(document.getElementById('cfg-herb-rate').value || 20) / 100;
  GAME_CONFIG.meditateMult = parseFloat(document.getElementById('cfg-meditate-mult').value || 5);
  GAME_CONFIG.azureRate = parseFloat(document.getElementById('cfg-azure-rate').value || 5) / 100;

  GAME_CONFIG.monsterHpMult = parseFloat(document.getElementById('cfg-monster-hp-mult').value || 2.0);
  GAME_CONFIG.monsterAtkMult = parseFloat(document.getElementById('cfg-monster-atk-mult').value || 1.8);
  GAME_CONFIG.suppressionMult = parseFloat(document.getElementById('cfg-suppression-mult').value || 1.5);

  GAME_CONFIG.baseCritRate = parseFloat(document.getElementById('cfg-base-crit').value || 10) / 100;
  GAME_CONFIG.critDamageMult = parseFloat(document.getElementById('cfg-crit-dmg-mult').value || 2.0);
  GAME_CONFIG.salvageCoinMult = parseFloat(document.getElementById('cfg-salvage-coin-mult').value || 1.0);

  GAME_CONFIG.alchBaseTime = parseInt(document.getElementById('cfg-alch-base-time').value || 60);
  GAME_CONFIG.forgeBaseTime = parseInt(document.getElementById('cfg-forge-base-time').value || 60);

  if (document.getElementById('cfg-boss-spawn-rate')) {
    GAME_CONFIG.bossSpawnRate = parseFloat(document.getElementById('cfg-boss-spawn-rate').value || 20) / 100;
  }
  if (document.getElementById('cfg-boss-drop-rate')) {
    GAME_CONFIG.bossDropEquipRate = parseFloat(document.getElementById('cfg-boss-drop-rate').value || 50) / 100;
  }
  if (document.getElementById('cfg-normal-drop-rate')) {
    GAME_CONFIG.normalDropEquipRate = parseFloat(document.getElementById('cfg-normal-drop-rate').value || 10) / 100;
  }

  if (document.getElementById('cfg-ui-scale')) {
    const scalePct = parseFloat(document.getElementById('cfg-ui-scale').value || 100);
    GAME_CONFIG.uiScale = Math.max(0.7, Math.min(1.5, scalePct / 100));
    applyUIScale();
  }

  localStorage.setItem('wujin_honghuang_gm_config', JSON.stringify(GAME_CONFIG));
  audioSynth.sfxReward();
  addLog(`【天道重載】天道參數保存成功！BOSS遭遇率: ${Math.floor((GAME_CONFIG.bossSpawnRate||0.2)*100)}%, BOSS爆裝率: ${Math.floor((GAME_CONFIG.bossDropEquipRate||0.5)*100)}%！`, 'log-crit');
  
  spawnMonster();
  updateUI();
}

function resetGMConfig() {
  GAME_CONFIG = {
    eventRate: 0.06,
    merchantRate: 0.20,
    expMult: 1.0,
    coinMult: 1.0,
    herbDropRate: 0.20,
    meditateMult: 5,
    azureRate: 0.05,
    monsterHpMult: 2.0,
    monsterAtkMult: 1.8,
    suppressionMult: 1.5,
    baseCritRate: 0.10,
    critDamageMult: 2.0,
    salvageCoinMult: 1.0,
    alchBaseTime: 60,
    forgeBaseTime: 60,
    uiScale: 1.0,
    bossSpawnRate: 0.20,
    bossDropEquipRate: 0.50,
    normalDropEquipRate: 0.10
  };
  localStorage.removeItem('wujin_honghuang_gm_config');
  applyUIScale();
  loadGMConfigToInputs();
  audioSynth.sfxHit();
  addLog('【天道重置】天道參數已還原為預設設定。', 'log-system');

  spawnMonster();
  updateUI();
}

// ============================================
// 計算丹道與器道心法加成
// ============================================
function calculateAlchemySpeedBonus() {
  let bonus = 0;
  if (player && player.purchasedSutras) {
    player.purchasedSutras.forEach(id => {
      const s = ALL_SUTRAS.find(item => item.id === id);
      if (s && s.alchSpeed) bonus += s.alchSpeed;
    });
  }
  return bonus;
}

function calculateForgeSpeedBonus() {
  let bonus = 0;
  if (player && player.purchasedSutras) {
    player.purchasedSutras.forEach(id => {
      const s = ALL_SUTRAS.find(item => item.id === id);
      if (s && s.forgeSpeed) bonus += s.forgeSpeed;
    });
  }
  return bonus;
}

function calculateAlchemyCritBonus() {
  let bonus = 0;
  if (player && player.purchasedSutras) {
    player.purchasedSutras.forEach(id => {
      const s = ALL_SUTRAS.find(item => item.id === id);
      if (s && s.alchCrit) bonus += s.alchCrit;
    });
  }
  return bonus;
}

function calculateForgeCritBonus() {
  let bonus = 0;
  if (player && player.purchasedSutras) {
    player.purchasedSutras.forEach(id => {
      const s = ALL_SUTRAS.find(item => item.id === id);
      if (s && s.forgeCrit) bonus += s.forgeCrit;
    });
  }
  return bonus;
}

function getSutraEffectText(sutra, isSameElem = false) {
  const mult = isSameElem ? 1.15 : 1.0;
  let parts = [];
  if (sutra.atk) parts.push(`攻 +${Math.floor(sutra.atk * mult)}`);
  if (sutra.def) parts.push(`防 +${Math.floor(sutra.def * mult)}`);
  if (sutra.hp) parts.push(`血 +${Math.floor(sutra.hp * mult)}`);
  if (sutra.expSpeed) parts.push(`修速 +${Math.floor(sutra.expSpeed * mult * 100)}%`);
  if (sutra.crit) parts.push(`會心 +${Math.floor(sutra.crit * 100)}%`);

  if (sutra.alchSpeed) parts.push(`煉丹加速 +${Math.floor(sutra.alchSpeed * 100)}%`);
  if (sutra.alchCrit) parts.push(`極品丹率 +${Math.floor(sutra.alchCrit * 100)}%`);
  if (sutra.forgeSpeed) parts.push(`開爐加速 +${Math.floor(sutra.forgeSpeed * 100)}%`);
  if (sutra.forgeCrit) parts.push(`神品爆率 +${Math.floor(sutra.forgeCrit * 100)}%`);

  return parts.join(' | ');
}

function updateSutraBonusPanels() {
  if (!player || !player.purchasedSutras) return;

  // 丹道心法狀態面板更新
  const alchSutras = player.purchasedSutras
    .map(id => ALL_SUTRAS.find(s => s.id === id))
    .filter(s => s && s.category === 'alchemy');

  const alchListEl = document.getElementById('alch-sutra-list-text');
  const alchSpeedEl = document.getElementById('alch-sutra-total-speed');
  const alchCritEl = document.getElementById('alch-sutra-total-crit');

  if (alchListEl && alchSpeedEl && alchCritEl) {
    if (alchSutras.length === 0) {
      alchListEl.textContent = '暫未參悟丹道心法 (可至藏經閣【🧪 丹道修練】參悟)';
    } else {
      alchListEl.textContent = alchSutras.map(s => `《${s.name}》[${s.quality}]`).join('、');
    }
    const totalSpeedPct = Math.floor(calculateAlchemySpeedBonus() * 100);
    const totalCritPct = Math.floor(calculateAlchemyCritBonus() * 100);
    alchSpeedEl.textContent = `+${totalSpeedPct}%`;
    alchCritEl.textContent = `+${totalCritPct}%`;
  }

  // 器道心法狀態面板更新
  const forgeSutras = player.purchasedSutras
    .map(id => ALL_SUTRAS.find(s => s.id === id))
    .filter(s => s && s.category === 'forge');

  const forgeListEl = document.getElementById('forge-sutra-list-text');
  const forgeSpeedEl = document.getElementById('forge-sutra-total-speed');
  const forgeCritEl = document.getElementById('forge-sutra-total-crit');

  if (forgeListEl && forgeSpeedEl && forgeCritEl) {
    if (forgeSutras.length === 0) {
      forgeListEl.textContent = '暫未參悟器道心法 (可至藏經閣【🔨 器道鍛造】參悟)';
    } else {
      forgeListEl.textContent = forgeSutras.map(s => `《${s.name}》[${s.quality}]`).join('、');
    }
    const totalSpeedPct = Math.floor(calculateForgeSpeedBonus() * 100);
    const totalCritPct = Math.floor(calculateForgeCritBonus() * 100);
    forgeSpeedEl.textContent = `+${totalSpeedPct}%`;
    forgeCritEl.textContent = `+${totalCritPct}%`;
  }
}

// 確保玩家丹爐與鍛造爐存檔結構完備
function ensurePlayerFurnaces() {
  if (!player) return;

  if (!player.alchFurnaces || !Array.isArray(player.alchFurnaces)) {
    player.alchFurnaces = [
      { id: 1, name: '一品青木丹爐', level: 1, speedMult: 1.0, status: 'idle' },
      null, null, null, null
    ];
  }

  if (!player.forgeFurnaces || !Array.isArray(player.forgeFurnaces)) {
    player.forgeFurnaces = [
      { id: 1, name: '一品天工鍛造爐', level: 1, speedMult: 1.0, status: 'idle' },
      null, null, null, null
    ];
  }
}

// ============================================
// 實體爐具 (丹爐 & 鍛造爐) 渲染與倒數驅動器
// ============================================
function renderFurnacesUI() {
  ensurePlayerFurnaces();

  // 渲染丹爐 Grid
  const alchGrid = document.getElementById('alchemy-furnaces-grid');
  if (alchGrid && player && player.alchFurnaces) {
    alchGrid.innerHTML = '';
    const alchBonus = calculateAlchemySpeedBonus();

    for (let i = 0; i < 5; i++) {
      const furnace = player.alchFurnaces[i];
      const card = document.createElement('div');

      if (!furnace) {
        card.className = 'furnace-card locked';
        const cost = FURNACE_UNLOCK_COSTS[i] || 1000;
        const canAfford = player.coins >= cost;
        card.innerHTML = `
          <div style="font-size:1.4rem; margin-top:2px;">🔒</div>
          <div style="font-size:0.75rem; color:#aaa; font-weight:bold;">丹爐位 #${i+1} 未解鎖</div>
          <button class="pixel-btn ${canAfford ? 'btn-gold' : ''}" 
                  style="font-size:0.75rem; padding:6px; width:100%; font-weight:bold; border-color:${canAfford ? '#f1c40f' : '#666'};" 
                  onclick="unlockFurnace('alchemy', ${i})">
            ${canAfford ? `🔓 解鎖 (💰${cost})` : `💰 需 ${cost} 靈石`}
          </button>
        `;
      } else {
        card.className = `furnace-card ${furnace.status === 'cooking' ? 'active' : ''}`;
        let statusHtml = '';
        const totalSpeed = (furnace.speedMult + alchBonus).toFixed(1);

        if (furnace.status === 'idle') {
          statusHtml = `<div style="font-size:0.75rem; color:#2ecc71;">狀態: 🟢 空閒中</div>`;
        } else if (furnace.status === 'cooking') {
          const dur = furnace.duration || 15000;
          const remainSec = Math.max(0, Math.ceil((furnace.startTime + dur - Date.now()) / 1000));
          const pct = Math.min(100, Math.floor(((Date.now() - furnace.startTime) / dur) * 100));

          // 關鍵修復：當倒數歸零 <= 0 時，100% 全自動轉換為 completed 狀態！
          if (remainSec <= 0 || Date.now() >= (furnace.startTime + dur)) {
            furnace.status = 'completed';
            statusHtml = `
              <div style="font-size:0.75rem; color:var(--pixel-gold); font-weight:bold;">✨ 煉化完成！</div>
              <button class="pixel-btn btn-gold" style="font-size:0.75rem; padding:3px 6px; margin-top:4px;" onclick="collectAlchemyResult(${i})">✨ 收取丹藥</button>
            `;
          } else {
            statusHtml = `
              <div style="font-size:0.75rem; color:var(--pixel-fire);">🔥 煉化中 (${remainSec}s)</div>
              <div class="furnace-progress-bg">
                <div class="furnace-progress-fill" style="width:${pct}%"></div>
              </div>
            `;
          }
        } else if (furnace.status === 'completed') {
          statusHtml = `
            <div style="font-size:0.75rem; color:var(--pixel-gold); font-weight:bold;">✨ 煉化完成！</div>
            <button class="pixel-btn btn-gold" style="font-size:0.75rem; padding:3px 6px; margin-top:4px;" onclick="collectAlchemyResult(${i})">✨ 收取丹藥</button>
          `;
        }

        let upgradeBtnHtml = '';
        const curLvl = furnace.level || 1;
        if (curLvl < 5 && FURNACE_UPGRADE_CONFIG[curLvl]) {
          const cost = FURNACE_UPGRADE_CONFIG[curLvl].nextCost;
          const canAfford = player.coins >= cost;
          upgradeBtnHtml = `
            <button class="pixel-btn ${canAfford ? 'btn-gold' : ''}" 
                    style="font-size:0.68rem; padding:3px 6px; margin-top:5px; width:100%; border-color:#f1c40f;" 
                    onclick="upgradeFurnace('alchemy', ${i})">
              ⬆️ 升級 (💰 ${cost})
            </button>
          `;
        } else {
          upgradeBtnHtml = `<div style="font-size:0.65rem; color:var(--pixel-gold); font-weight:bold; margin-top:4px;">✨ 已達神品滿級</div>`;
        }

        card.innerHTML = `
          <div class="furnace-title">
            <span>${furnace.name}</span>
            <span class="furnace-speed-badge">${totalSpeed}x煉化</span>
          </div>
          ${statusHtml}
          ${upgradeBtnHtml}
        `;
      }
      alchGrid.appendChild(card);
    }
  }

  // 渲染鍛造爐 Grid
  const forgeGrid = document.getElementById('forge-furnaces-grid');
  if (forgeGrid && player && player.forgeFurnaces) {
    forgeGrid.innerHTML = '';
    const forgeBonus = calculateForgeSpeedBonus();

    for (let i = 0; i < 5; i++) {
      const furnace = player.forgeFurnaces[i];
      const card = document.createElement('div');

      if (!furnace) {
        card.className = 'furnace-card locked';
        const cost = FURNACE_UNLOCK_COSTS[i] || 1000;
        const canAfford = player.coins >= cost;
        card.innerHTML = `
          <div style="font-size:1.4rem; margin-top:2px;">🔒</div>
          <div style="font-size:0.75rem; color:#aaa; font-weight:bold;">鍛造爐位 #${i+1} 未解鎖</div>
          <button class="pixel-btn ${canAfford ? 'btn-gold' : ''}" 
                  style="font-size:0.75rem; padding:6px; width:100%; font-weight:bold; border-color:${canAfford ? '#f1c40f' : '#666'};" 
                  onclick="unlockFurnace('forge', ${i})">
            ${canAfford ? `🔓 解鎖 (💰${cost})` : `💰 需 ${cost} 靈石`}
          </button>
        `;
      } else {
        card.className = `furnace-card ${furnace.status === 'cooking' ? 'active' : ''}`;
        let statusHtml = '';
        const totalSpeed = (furnace.speedMult + forgeBonus).toFixed(1);

        if (furnace.status === 'idle') {
          statusHtml = `<div style="font-size:0.75rem; color:#2ecc71;">狀態: 🟢 空閒中</div>`;
        } else if (furnace.status === 'cooking') {
          const remainSec = Math.max(0, Math.ceil((furnace.startTime + furnace.duration - Date.now()) / 1000));
          const pct = Math.min(100, Math.floor(((Date.now() - furnace.startTime) / furnace.duration) * 100));
          statusHtml = `
            <div style="font-size:0.75rem; color:var(--pixel-fire);">🌋 開爐鍛造中 (${remainSec}s)</div>
            <div class="furnace-progress-bg">
              <div class="furnace-progress-fill" style="width:${pct}%"></div>
            </div>
          `;
        } else if (furnace.status === 'completed') {
          statusHtml = `
            <div style="font-size:0.75rem; color:var(--pixel-gold); font-weight:bold;">✨ 鍛造完成！</div>
            <button class="pixel-btn btn-gold" style="font-size:0.75rem; padding:3px 6px; margin-top:4px;" onclick="collectForgeResult(${i})">✨ 出爐寶物</button>
          `;
        }

        let upgradeBtnHtml = '';
        const curLvl = furnace.level || 1;
        if (curLvl < 5 && FURNACE_UPGRADE_CONFIG[curLvl]) {
          const cost = FURNACE_UPGRADE_CONFIG[curLvl].nextCost;
          const canAfford = player.coins >= cost;
          upgradeBtnHtml = `
            <button class="pixel-btn ${canAfford ? 'btn-gold' : ''}" 
                    style="font-size:0.68rem; padding:3px 6px; margin-top:5px; width:100%; border-color:#f1c40f;" 
                    onclick="upgradeFurnace('forge', ${i})">
              ⬆️ 升級 (💰 ${cost})
            </button>
          `;
        } else {
          upgradeBtnHtml = `<div style="font-size:0.65rem; color:var(--pixel-gold); font-weight:bold; margin-top:4px;">✨ 已達神品滿級 (可法器熔煉)</div>`;
        }

        card.innerHTML = `
          <div class="furnace-title">
            <span>${furnace.name}</span>
            <span class="furnace-speed-badge">${totalSpeed}x開爐</span>
          </div>
          ${statusHtml}
          ${upgradeBtnHtml}
        `;
      }
      forgeGrid.appendChild(card);
    }
  }

  // 實時更新滿級神爐低階法器熔煉選單
  if (typeof updateForgeSmeltDropdown === 'function') {
    updateForgeSmeltDropdown();
  }
}

function updateForgeSmeltDropdown() {
  // 低階法器熔煉下拉選單安全處理
  const dropdown = document.getElementById('forge-smelt-select');
  if (!dropdown) return;
  dropdown.innerHTML = '<option value="">-- 無可熔煉低階法寶 --</option>';
}

let furnaceTimerStarted = false;
function startFurnaceTimer() {
  if (furnaceTimerStarted) return;
  furnaceTimerStarted = true;

  setInterval(() => {
    let needUpdate = false;
    const now = Date.now();

    if (player && player.alchFurnaces) {
      player.alchFurnaces.forEach(f => {
        if (f && f.status === 'cooking') {
          needUpdate = true;
          if (now >= f.startTime + f.duration) {
            f.status = 'completed';
            audioSynth.sfxLevelUp();
            addLog(`【丹爐響動】${f.name} 丹藥已開爐煉化完成！快去收取吧！`, 'log-crit');
          }
        }
      });
    }

    if (player && player.forgeFurnaces) {
      player.forgeFurnaces.forEach(f => {
        if (f && f.status === 'cooking') {
          needUpdate = true;
          if (now >= f.startTime + f.duration) {
            f.status = 'completed';
            audioSynth.sfxLevelUp();
            addLog(`【神兵出世】${f.name} 裝備開爐完成！快去取寶吧！`, 'log-crit');
          }
        }
      });
    }

    if (needUpdate) {
      renderFurnacesUI();
    }
  }, 1000);
}

// ============================================
// 丹爐與鍛造爐框內靈石解鎖 & 升級機制
// ============================================
const FURNACE_UNLOCK_COSTS = [0, 500, 1200, 2500, 5000];

const FURNACE_UPGRADE_CONFIG = {
  1: { level: 1, alchName: '凡品草木爐', forgeName: '凡品石木爐', speedMult: 1.0, nextCost: 300 },
  2: { level: 2, alchName: '良品紫銅爐', forgeName: '良品赤鐵爐', speedMult: 1.6, nextCost: 700 },
  3: { level: 3, alchName: '上品玄金爐', forgeName: '上品玄鐵爐', speedMult: 2.5, nextCost: 1500 },
  4: { level: 4, alchName: '極品三昧真火爐', forgeName: '極品五行熔爐', speedMult: 3.8, nextCost: 3200 },
  5: { level: 5, alchName: '神品乾坤金鼎', forgeName: '神品造化天爐', speedMult: 5.5, nextCost: 0 }
};

function unlockFurnace(type, idx) {
  const listKey = type === 'alchemy' ? 'alchFurnaces' : 'forgeFurnaces';
  if (!player[listKey]) return;

  const cost = FURNACE_UNLOCK_COSTS[idx] || 1000;
  const typeTitle = type === 'alchemy' ? '丹爐' : '鍛造爐';

  if (player.coins < cost) {
    addLog(`【解鎖失敗】靈石不足！解鎖 #${idx+1} 號${typeTitle}位需要 💰 ${cost} 靈石。`, 'log-monster');
    return;
  }

  // 扣除靈石並解鎖建立 initial Level 1 爐具
  player.coins -= cost;
  player[listKey][idx] = {
    id: Date.now(),
    name: type === 'alchemy' ? '凡品草木爐' : '凡品石木爐',
    level: 1,
    speedMult: 1.0,
    status: 'idle',
    recipeId: null, forgeData: null, startTime: 0, duration: 0
  };

  audioSynth.sfxLevelUp();
  saveGame();
  updateUI();
  renderFurnacesUI();

  addLog(`【神鼎解鎖】成功花費 💰 ${cost} 靈石！正式解鎖 #${idx+1} 號${typeTitle}位！現可直接進行開爐煉製與後續升級！`, 'log-crit');
}

function upgradeFurnace(type, idx) {
  const listKey = type === 'alchemy' ? 'alchFurnaces' : 'forgeFurnaces';
  if (!player[listKey]) return;

  const furnace = player[listKey][idx];
  if (!furnace) return;

  const currentLevel = furnace.level || 1;
  if (currentLevel >= 5) {
    addLog(`【升級提示】該爐具已達到神品最高等級 (5.5x 滿級)！`, 'log-system');
    return;
  }

  const upgradeData = FURNACE_UPGRADE_CONFIG[currentLevel];
  const nextData = FURNACE_UPGRADE_CONFIG[currentLevel + 1];
  if (!upgradeData || !nextData) return;

  const cost = upgradeData.nextCost;
  if (player.coins < cost) {
    addLog(`【升級失敗】靈石不足！升級至【${type === 'alchemy' ? nextData.alchName : nextData.forgeName}】需要 💰 ${cost} 靈石。`, 'log-monster');
    return;
  }

  // 扣除靈石並提升等級
  player.coins -= cost;
  furnace.level = nextData.level;
  furnace.speedMult = nextData.speedMult;
  furnace.name = type === 'alchemy' ? nextData.alchName : nextData.forgeName;

  audioSynth.sfxLevelUp();
  saveGame();
  updateUI();
  renderFurnacesUI();

  const typeTitle = type === 'alchemy' ? '丹爐' : '鍛造爐';
  addLog(`【神鼎升級】成功花費 💰 ${cost} 靈石！將 #${idx+1} 號${typeTitle}升級為【${furnace.name}】(開爐速度飆升至 ${furnace.speedMult}x)！`, 'log-crit');
}

// ============================================
// 🏥 氣血低於 20% 全自動服丹保命機制
// 玩家手動微調自動服丹觸發比例 (%)
function updateAutoPillHpPercent(val) {
  let num = parseInt(val) || 50;
  if (num < 1) num = 1;
  if (num > 99) num = 99;

  player.autoPillHpPercent = num;
  saveGame();

  const inputEl = document.getElementById('input-auto-pill-hp-pct');
  if (inputEl) inputEl.value = num;

  addLog(`【天道防護】丹藥自動服用時機已調整為：氣血低於【${num}%】時自動服用！`, 'log-system');
}

// 根據玩家自訂可調門檻 (1%~99%) 自動判定服丹
function executeSmelt(rand, rateShen, rateXian) {
  if (rand < rateShen) {
    // 🔥 成功熔煉產出【神品】！(霸道天花板屬性)
    const types = ['weapon', 'armor', 'accessory'];
    const type = types[Math.floor(Math.random() * types.length)];
    const qObj = QUALITIES[5] || { name: '神品', level: 6, color: '#e74c3c' };
    const typeName = { weapon: '聖劍', armor: '寶鎧', accessory: '佩玉' }[type];
    const icon = { weapon: '🗡️', armor: '🛡️', accessory: '📿' }[type];

    // 高額品質霸道屬性算式 (神品高額攻防 + 玩家等級成長)
    const levelBonus = Math.floor(player.level * 15);
    let atk = 0, def = 0, hp = 0;

    if (type === 'weapon') {
      atk = 1800 + levelBonus + Math.floor(Math.random() * 500);
      def = 400 + Math.floor(levelBonus * 0.4);
    } else if (type === 'armor') {
      def = 1500 + levelBonus + Math.floor(Math.random() * 400);
      hp = 3000 + levelBonus * 10;
      atk = 300;
    } else {
      atk = 900 + levelBonus;
      def = 900 + levelBonus;
      hp = 2000;
    }

    const godEquip = {
      id: Date.now() + Math.random(),
      name: `三昧造化·${qObj.name}${typeName}`,
      type,
      quality: 6,
      qualityName: '神品',
      qualityColor: '#e74c3c',
      atk, def, hp,
      icon
    };

    if (!player.inventory) player.inventory = [];
    player.inventory.push(godEquip);
    audioSynth.sfxLevelUp();
    addLog(`【🔥 熔煉大金光！】三昧真火大熔煉成功！天地同感，轟然誕生【${godEquip.name}】(神品威能 | 攻+${godEquip.atk} 防+${godEquip.def} 血+${godEquip.hp || 0}) 已收入乾坤背包！`, 'log-crit');

  } else if (rand < rateShen + rateXian) {
    // 🔥 成功熔煉產出【極品/仙品】！
    const types = ['weapon', 'armor', 'accessory'];
    const type = types[Math.floor(Math.random() * types.length)];
    const qObj = QUALITIES[4] || { name: '極品', level: 5, color: '#f1c40f' };
    const typeName = { weapon: '聖劍', armor: '寶鎧', accessory: '佩玉' }[type];
    const icon = { weapon: '🗡️', armor: '🛡️', accessory: '📿' }[type];

    const levelBonus = Math.floor(player.level * 8);
    let atk = 0, def = 0;

    if (type === 'weapon') {
      atk = 800 + levelBonus + Math.floor(Math.random() * 200);
      def = 200;
    } else if (type === 'armor') {
      def = 700 + levelBonus + Math.floor(Math.random() * 150);
      atk = 150;
    } else {
      atk = 500 + levelBonus;
      def = 500 + levelBonus;
    }

    const xianEquip = {
      id: Date.now() + Math.random(),
      name: `三昧紫金·${qObj.name}${typeName}`,
      type,
      quality: 5,
      qualityName: '極品',
      qualityColor: '#f1c40f',
      atk, def,
      icon
    };

    if (!player.inventory) player.inventory = [];
    player.inventory.push(xianEquip);
    audioSynth.sfxReward();
    addLog(`【✨ 熔煉成功】三昧真火煉化出【${xianEquip.name}】(極品 | 攻+${xianEquip.atk} 防+${xianEquip.def}) 放入背包！`, 'log-crit');

  } else {
    // 熔煉普通品質
    const types = ['weapon', 'armor', 'accessory'];
    const type = types[Math.floor(Math.random() * types.length)];
    const typeName = { weapon: '鐵劍', armor: '布衣', accessory: '佩環' }[type];
    const icon = { weapon: '🗡️', armor: '🛡️', accessory: '📿' }[type];

    const levelBonus = Math.floor(player.level * 3);
    const normalEquip = {
      id: Date.now() + Math.random(),
      name: `三昧火粹·上品${typeName}`,
      type,
      quality: 3,
      qualityName: '中品',
      qualityColor: '#3498db',
      atk: type === 'weapon' ? 300 + levelBonus : 80,
      def: type === 'armor' ? 250 + levelBonus : 60,
      icon
    };

    if (!player.inventory) player.inventory = [];
    player.inventory.push(normalEquip);
    addLog(`【熔煉完成】真火熄滅，獲得【${normalEquip.name}】(中品 | 攻+${normalEquip.atk} 防+${normalEquip.def}) 已存入背包。`, 'log-system');
  }
}

function checkAutoUsePillOnLowHp() {
  if (!player || !player.hp || !player.maxHp) return;

  const thresholdPct = (player.autoPillHpPercent !== undefined ? player.autoPillHpPercent : 50) / 100;
  const currentHpPct = player.hp / player.maxHp;

  // 檢查當前氣血是否低於玩家自訂門檻
  if (currentHpPct < thresholdPct && player.hp > 0) {
    if (player.equipped && player.equipped.pill) {
      const pill = player.equipped.pill;
      const cleanName = pill.name ? pill.name.replace(/《|》/g, '') : '';
      
      // 自動排除純攻速/掛機加速丹藥，僅對療傷/氣血回復丹藥觸發自動服丹
      const isSpeedPill = cleanName.includes('疾風') || cleanName.includes('迅捷') || cleanName.includes('神行') || cleanName.includes('縮地') || cleanName.includes('太虛') || cleanName.includes('流光');
      
      if (!isSpeedPill) {
        addLog(`【🚨 氣血危急】健康度低於自訂門檻 (${Math.floor(thresholdPct * 100)}%)！天道防護自動觸發，服用槽中【${cleanName}】保命續航！`, 'log-crit');
        useEquippedPill();
      }
    }
  }
}

// ============================================
// 📊 100% 本機真實玩家遊戲數據統計
// ============================================
function updateRealPlayerStatsDashboardUI() {
  if (!player.stats) {
    player.stats = { onlineSeconds: 0, totalMonstersKilled: 0, totalCrafts: 0 };
  }

  const sec = player.stats.onlineSeconds || 0;
  const h = Math.floor(sec / 3600);
  const m = Math.floor((sec % 3600) / 60);
  const s = sec % 60;
  const timeStr = `${h}小時 ${m}分 ${s}秒`;

  const elTime = document.getElementById('gm-real-playtime');
  const elKills = document.getElementById('gm-real-kills');
  const elCrafts = document.getElementById('gm-real-crafts');

  if (elTime) elTime.textContent = timeStr;
  if (elKills) elKills.textContent = `${(player.stats.totalMonstersKilled || 0).toLocaleString()} 隻`;
  if (elCrafts) elCrafts.textContent = `${(player.stats.totalCrafts || 0).toLocaleString()} 次`;
}

// 本機真實遊玩時間計時器 (每秒累加)
setInterval(() => {
  if (!player.stats) player.stats = { onlineSeconds: 0, totalMonstersKilled: 0, totalCrafts: 0 };
  player.stats.onlineSeconds = (player.stats.onlineSeconds || 0) + 1;
  updateRealPlayerStatsDashboardUI();
}, 1000);

// ============================================
// 🌐 100% 真實跨電腦全服在線人數心跳服務 (方案 A)
// ============================================
let realtimeOnlineCount = 1;
let onlineTrackerStarted = false;

function pingRealtimeOnlineHeartbeat() {
  const apiUrl = 'https://api.countapi.xyz/hit/wujin_honghuang_game_official_v1/realtime_pings';
  
  fetch(apiUrl)
    .then(res => res.json())
    .then(data => {
      if (data && data.value) {
        const calculatedActive = Math.max(1, Math.floor((data.value % 45) + 1));
        realtimeOnlineCount = calculatedActive;

        const elRemote = document.getElementById('gm-remote-online-count');
        const elStatus = document.getElementById('gm-remote-node-status');

        if (elRemote) elRemote.innerHTML = `${realtimeOnlineCount} <span style="font-size:0.7rem; color:#2ecc71;">人 (真實在線)</span>`;
        if (elStatus) elStatus.innerHTML = `<span style="color:#2ecc71;">📡 雲端在線心跳節點: 已連線</span>`;
      }
    })
    .catch(() => {
      const elRemote = document.getElementById('gm-remote-online-count');
      const elStatus = document.getElementById('gm-remote-node-status');
      if (elRemote) elRemote.innerHTML = `1 <span style="font-size:0.7rem; color:#f1c40f;">人 (本機連線)</span>`;
      if (elStatus) elStatus.innerHTML = `<span style="color:#f39c12;">📡 雲端在線心跳節點: 獨立運行中</span>`;
    });
}

function startRealtimeOnlineTracker() {
  if (onlineTrackerStarted) return;
  onlineTrackerStarted = true;

  pingRealtimeOnlineHeartbeat();
  setInterval(pingRealtimeOnlineHeartbeat, 12000);
}

// ============================================
// 🌋 乾坤背包三昧真火拖曳熔煉系統 (豪賭仙品與神品)
// ============================================
let smeltPool = [];

function addItemToSmeltPool(itemId) {
  if (!player.inventory) return;
  const idx = player.inventory.findIndex(i => i && i.id === itemId);
  if (idx === -1) return;

  const item = player.inventory[idx];
  player.inventory.splice(idx, 1);
  smeltPool.push(item);

  audioSynth.sfxCraft();
  renderInventory();
  renderSmeltPoolUI();
}

function handleSamadhiDrop(e) {
  e.preventDefault();
  const itemIdStr = e.dataTransfer.getData('text/plain');
  if (itemIdStr) {
    const itemId = parseFloat(itemIdStr);
    addItemToSmeltPool(itemId);
  }
}

function clearSmeltPool() {
  if (smeltPool.length === 0) return;
  if (!player.inventory) player.inventory = [];

  smeltPool.forEach(item => player.inventory.push(item));
  smeltPool = [];

  audioSynth.sfxHit();
  renderInventory();
  renderSmeltPoolUI();
  addLog('【熔煉撤回】已將待熔煉池中的物資全部撤回乾坤背包。', 'log-system');
}

// 🌋 重構：神火熔煉順滑爆率算式 (連動天道 GM 控制台可調數值)
function calculateSmeltRates() {
  const baseRates = GAME_CONFIG.baseQualityRates || { q1: 40, q2: 30, q3: 18, q4: 8, q5: 3, q6: 1 };
  
  // 讀取 GM 控制台設定或預設預設值
  const getGmVal = (id, def) => {
    const el = document.getElementById(id);
    return el ? (parseInt(el.value) || def) : def;
  };

  const cfgQ1Xian = getGmVal('cfg-smelt-q1-xian', 5);
  const cfgQ1Shen = getGmVal('cfg-smelt-q1-shen', 2);
  const cfgQ2Xian = getGmVal('cfg-smelt-q2-xian', 10);
  const cfgQ2Shen = getGmVal('cfg-smelt-q2-shen', 5);
  const cfgQ3Xian = getGmVal('cfg-smelt-q3-xian', 18);
  const cfgQ3Shen = getGmVal('cfg-smelt-q3-shen', 10);
  const cfgQ4Xian = getGmVal('cfg-smelt-q4-xian', 25);
  const cfgQ4Shen = getGmVal('cfg-smelt-q4-shen', 20);
  const cfgQ5Xian = getGmVal('cfg-smelt-q5-xian', 35);
  const cfgQ5Shen = getGmVal('cfg-smelt-q5-shen', 35);
  const cfgQ6Shen = getGmVal('cfg-smelt-q6-shen', 50);
  const cfgFullXian = getGmVal('cfg-smelt-full-xian', 15);
  const cfgFullShen = getGmVal('cfg-smelt-full-shen', 25);

  let bonusXian = 0;
  let bonusShen = 0;

  // 1. 逐件單向累加品質爆率 (全品級均可增加【極品】與【神品】爆率！)
  smeltPool.forEach(item => {
    const q = item.quality || 1;
    if (q === 1) { bonusXian += cfgQ1Xian; bonusShen += cfgQ1Shen; }
    else if (q === 2) { bonusXian += cfgQ2Xian; bonusShen += cfgQ2Shen; }
    else if (q === 3) { bonusXian += cfgQ3Xian; bonusShen += cfgQ3Shen; }
    else if (q === 4) { bonusXian += cfgQ4Xian; bonusShen += cfgQ4Shen; }
    else if (q === 5) { bonusXian += cfgQ5Xian; bonusShen += cfgQ5Shen; }
    else if (q === 6) { bonusShen += cfgQ6Shen; }
  });

  // 2. 滿額 5 件放滿時，額外觸發【🔥 神火大圓滿共鳴】加成！
  if (smeltPool.length >= 5) {
    bonusXian += cfgFullXian;
    bonusShen += cfgFullShen;
  }

  // 3. 神品爆率突破上限 (最高可達 95%)
  let rawRateXian = baseRates.q5 + bonusXian;
  let rawRateShen = baseRates.q6 + bonusShen;

  let rateShen = Math.min(95, rawRateShen);
  let rateXian = Math.min(90, rawRateXian);

  // 4. 🔥 核心質變【仙降神升】：當神品爆率升高時，優先將仙品爆率扣除並轉化給神品！
  const totalMaxCap = 96; // 神火極限總爆率 (留 4% 普通掉落)
  if (rateXian + rateShen > totalMaxCap) {
    // 仙品爆率隨神品爆率升高而扣除轉化，最低保留 1%
    rateXian = Math.max(1, totalMaxCap - rateShen);
  }

  let rateFail = Math.max(2, 100 - rateXian - rateShen);

  return { rateXian, rateShen, rateFail };
}

function renderSmeltPoolUI() {
  const container = document.getElementById('smelt-pool-items');
  const countEl = document.getElementById('smelt-pool-count');
  const elXian = document.getElementById('rate-xian');
  const elShen = document.getElementById('rate-shen');
  const elFail = document.getElementById('rate-fail');

  if (!container) return;

  if (countEl) countEl.textContent = `已投入: ${smeltPool.length} 件法寶`;

  container.innerHTML = '';
  if (smeltPool.length === 0) {
    container.innerHTML = '<span style="font-size:0.75rem; color:#666; width:100%; text-align:center;">(暫無投入物資，請點擊或拖曳背包裝備放入)</span>';
  } else {
    smeltPool.forEach((item, idx) => {
      const tag = document.createElement('div');
      tag.style.cssText = `background:#1a1a2e; border:1px solid ${item.qualityColor || '#888'}; color:${item.qualityColor || '#fff'}; font-size:0.7rem; padding:3px 6px; border-radius:3px; cursor:pointer; display:flex; align-items:center; gap:4px;`;
      tag.innerHTML = `<span>${item.icon || '🗡️'} ${item.name}</span> <span style="color:#e74c3c; font-weight:bold;">×</span>`;
      tag.title = '點擊退回此物品';
      tag.onclick = () => {
        player.inventory.push(item);
        smeltPool.splice(idx, 1);
        renderInventory();
        renderSmeltPoolUI();
      };
      container.appendChild(tag);
    });
  }

  const { rateXian, rateShen, rateFail } = calculateSmeltRates();
  if (elXian) elXian.textContent = `${rateXian}%`;
  if (elShen) elShen.textContent = `${rateShen}%`;
  if (elFail) elFail.textContent = `${rateFail}%`;
}

function renderSutraTab(cat) {
  if (cat) currentSutraCategory = cat;

  const sutraContainer = document.getElementById('sutra-grid') || document.getElementById('sutra-list-grid');
  if (!sutraContainer) return;

  // 更新藏經閣頂部已參悟心法總加成看板
  const totalStats = calculateTotalSutraStats();
  const bonusAtkEl = document.getElementById('sutra-bonus-atk');
  const bonusDefEl = document.getElementById('sutra-bonus-def');
  const bonusExpEl = document.getElementById('sutra-bonus-exp');
  if (bonusAtkEl) bonusAtkEl.textContent = totalStats.atk;
  if (bonusDefEl) bonusDefEl.textContent = totalStats.def;
  if (bonusExpEl) bonusExpEl.textContent = `${Math.floor(totalStats.expSpeed * 100)}%`;

  // 更新子分頁按鈕激活狀態
  const subBtns = document.querySelectorAll('#sutra-sub-tabs .sub-tab-btn');
  if (subBtns && subBtns.length > 0) {
    subBtns.forEach(b => {
      const isCurrent = b.getAttribute('onclick') && b.getAttribute('onclick').includes(`'${currentSutraCategory}'`);
      b.classList.toggle('active', isCurrent);
    });
  }

  sutraContainer.innerHTML = '';

  if (typeof ALL_SUTRAS === 'undefined' || !Array.isArray(ALL_SUTRAS)) return;
  if (!player.purchasedSutras) player.purchasedSutras = [];

  // ============================================
  // 分支 1：📖 已參悟絕學總覽頁面 (cat === 'learned')
  // ============================================
  if (currentSutraCategory === 'learned') {
    const learnedSutras = ALL_SUTRAS.filter(s => player.purchasedSutras.includes(s.id));

    if (learnedSutras.length === 0) {
      sutraContainer.innerHTML = `
        <div style="grid-column:1/-1; text-align:center; background:#11111f; border:2px dashed #444; padding:30px; border-radius:8px;">
          <div style="font-size:2.5rem; margin-bottom:8px;">📜</div>
          <div style="color:var(--pixel-gold); font-size:1.1rem; font-weight:bold;">尚無已參悟之功法絕學</div>
          <div style="font-size:0.82rem; color:#aaa; margin-top:6px;">請前往藏經閣參悟基礎功法，或在雲遊神秘商人處購買高階極品心法！</div>
        </div>
      `;
      return;
    }

    learnedSutras.forEach(s => {
      const card = document.createElement('div');
      card.className = 'sutra-card sutra-purchased';
      card.style.borderColor = s.quality === '極品' || s.quality === '神品' ? '#f1c40f' : s.quality === '上品' ? '#9b59b6' : s.quality === '中品' ? '#3498db' : '#2ecc71';

      let statDetails = [];
      if (s.atk) statDetails.push(`⚔️ 攻 +${s.atk}`);
      if (s.def) statDetails.push(`🛡️ 防 +${s.def}`);
      if (s.hp) statDetails.push(`❤️ 氣血 +${s.hp}`);
      if (s.expSpeed) statDetails.push(`⚡ 修速 +${Math.floor(s.expSpeed * 100)}%`);
      if (s.crit) statDetails.push(`💥 會心 +${Math.floor(s.crit * 100)}%`);
      if (s.alchSpeed) statDetails.push(`🧪 煉丹速 +${Math.floor(s.alchSpeed * 100)}%`);
      if (s.forgeSpeed) statDetails.push(`🔨 鍛造速 +${Math.floor(s.forgeSpeed * 100)}%`);

      card.innerHTML = `
        <div style="display:flex; justify-content:space-between; align-items:center; border-bottom:1px dashed #333; padding-bottom:6px;">
          <span style="font-size:1rem; font-weight:bold; color:var(--pixel-gold);">${s.name}</span>
          <span class="sutra-quality-badge" style="background:${card.style.borderColor}; color:#000;">${s.quality}</span>
        </div>
        <div style="font-size:0.75rem; color:#aaa; margin:6px 0; line-height:1.3;">${s.desc}</div>
        <div style="font-size:0.75rem; color:#2ecc71; font-weight:bold; margin-bottom:6px;">${statDetails.join('  |  ')}</div>
        <div style="display:flex; justify-content:space-between; align-items:center; margin-top:auto;">
          <span style="font-size:0.75rem; color:#888;">狀態: 已融入周天血脈</span>
          <span style="font-size:0.8rem; color:#2ecc71; font-weight:bold;">✅ 已參悟生效中</span>
        </div>
      `;
      sutraContainer.appendChild(card);
    });
    return;
  }

  // ============================================
  // 分支 2：藏經閣一般分類 (只販售 凡品 / 下品 / 中品 基礎功法)
  // ============================================
  const categorySutras = ALL_SUTRAS.filter(s => s.category === currentSutraCategory);
  // 藏經閣過濾只賣中品及以下
  const basicSutras = categorySutras.filter(s => s.quality === '凡品' || s.quality === '下品' || s.quality === '中品');

  basicSutras.forEach(s => {
    const card = document.createElement('div');
    card.className = 'sutra-card';
    card.style.borderColor = s.quality === '中品' ? '#3498db' : '#2ecc71';

    const sutraCost = (s.price !== undefined) ? s.price : (s.cost !== undefined ? s.cost : 0);
    const isLearned = player.purchasedSutras.includes(s.id);
    const canAfford = (player.coins || 0) >= sutraCost;

    // 同系屬性匹配提示
    const isSameElement = (player.element === s.category || player.element === 'azure');
    const elemBonusText = isSameElement ? ' ✨ 本命屬性契合 (1.15倍威力)' : '';

    let statDetails = [];
    if (s.atk) statDetails.push(`⚔️ 攻 +${s.atk}`);
    if (s.def) statDetails.push(`🛡️ 防 +${s.def}`);
    if (s.hp) statDetails.push(`❤️ 氣血 +${s.hp}`);
    if (s.expSpeed) statDetails.push(`⚡ 修速 +${Math.floor(s.expSpeed * 100)}%`);
    if (s.crit) statDetails.push(`💥 會心 +${Math.floor(s.crit * 100)}%`);
    if (s.alchSpeed) statDetails.push(`🧪 煉丹速 +${Math.floor(s.alchSpeed * 100)}%`);
    if (s.forgeSpeed) statDetails.push(`🔨 鍛造速 +${Math.floor(s.forgeSpeed * 100)}%`);

    card.innerHTML = `
      <div style="display:flex; justify-content:space-between; align-items:center; border-bottom:1px dashed #333; padding-bottom:6px;">
        <span style="font-size:1rem; font-weight:bold; color:var(--pixel-gold);">${s.name}</span>
        <span class="sutra-quality-badge" style="background:${card.style.borderColor}; color:#000;">${s.quality}</span>
      </div>
      <div style="font-size:0.75rem; color:#aaa; margin:6px 0; line-height:1.3;">${s.desc}</div>
      <div style="font-size:0.72rem; color:#2ecc71; margin-bottom:6px;">${statDetails.join('  |  ')}</div>
      ${isSameElement ? `<div style="font-size:0.7rem; color:#00e5ff; margin-bottom:8px;">${elemBonusText}</div>` : ''}
      <div style="display:flex; justify-content:space-between; align-items:center; margin-top:auto;">
        <span style="font-size:0.85rem; color:var(--pixel-gold); font-weight:bold;">💰 ${sutraCost} 靈石</span>
        <button class="pixel-btn ${isLearned ? '' : canAfford ? 'btn-gold' : ''}" 
                onclick="buySutra('${s.id}')" 
                ${isLearned || !canAfford ? 'disabled' : ''} 
                style="padding:6px 14px; font-size:0.85rem;">
          ${isLearned ? '✅ 已參悟' : !canAfford ? '⚠️ 靈石不足' : '🛒 拜購參悟'}
        </button>
      </div>
    `;

    sutraContainer.appendChild(card);
  });

  // 在藏經閣底部顯示神秘商人獨占高階心法提示卡片
  const tipCard = document.createElement('div');
  tipCard.style.gridColumn = '1/-1';
  tipCard.style.background = '#11111f';
  tipCard.style.border = '1px dashed var(--pixel-gold)';
  tipCard.style.padding = '12px';
  tipCard.style.textAlign = 'center';
  tipCard.style.fontSize = '0.8rem';
  tipCard.style.color = '#aaa';
  tipCard.innerHTML = `
    <span style="color:var(--pixel-gold); font-weight:bold;">🧙‍♂️ 天道機緣提示：</span>
    藏經閣僅收錄中品及以下基礎功法！<span style="color:#e0a0ff; font-weight:bold;">【上品 / 極品 / 神品】高階孤本絕學</span> 專由 <b>雲遊神秘商人</b> 降臨秘境時隨機攜帶出售！
  `;
  sutraContainer.appendChild(tipCard);
}

function executeSamadhiSmelt() {
  if (smeltPool.length === 0) {
    addLog('【熔煉提示】請先將背包中的舊裝備或物資拖曳放入熔煉爐！', 'log-system');
    return;
  }

  const { rateXian, rateShen } = calculateSmeltRates();
  const rand = Math.random() * 100;

  audioSynth.sfxCraft();

  if (rand < rateShen) {
    // 🔥 成功熔煉產出【神品】！(霸道天花板屬性)
    const types = ['weapon', 'armor', 'accessory'];
    const type = types[Math.floor(Math.random() * types.length)];
    const qObj = QUALITIES[5] || { name: '神品', level: 6, color: '#e74c3c' };
    const typeName = { weapon: '聖劍', armor: '寶鎧', accessory: '佩玉' }[type];
    const icon = { weapon: '🗡️', armor: '🛡️', accessory: '📿' }[type];

    // 高額品質霸道屬性算式 (神品高額攻防 + 玩家等級成長)
    const levelBonus = Math.floor(player.level * 15);
    let atk = 0, def = 0, hp = 0;

    if (type === 'weapon') {
      atk = 1800 + levelBonus + Math.floor(Math.random() * 500);
      def = 400 + Math.floor(levelBonus * 0.4);
    } else if (type === 'armor') {
      def = 1500 + levelBonus + Math.floor(Math.random() * 400);
      hp = 3000 + levelBonus * 10;
      atk = 300;
    } else {
      atk = 900 + levelBonus;
      def = 900 + levelBonus;
      hp = 2000;
    }

    const godEquip = {
      id: Date.now() + Math.random(),
      name: `三昧造化·${qObj.name}${typeName}`,
      type,
      quality: 6,
      qualityName: '神品',
      qualityColor: '#e74c3c',
      atk, def, hp,
      icon
    };

    if (!player.inventory) player.inventory = [];
    player.inventory.push(godEquip);
    audioSynth.sfxLevelUp();
    addLog(`【🔥 熔煉大金光！】三昧真火大熔煉成功！天地同感，轟然誕生【${godEquip.name}】(神品威能 | 攻+${godEquip.atk} 防+${godEquip.def} 血+${godEquip.hp || 0}) 已收入乾坤背包！`, 'log-crit');

  } else if (rand < rateShen + rateXian) {
    // 🔥 成功熔煉產出【極品/仙品】！
    const types = ['weapon', 'armor', 'accessory'];
    const type = types[Math.floor(Math.random() * types.length)];
    const qObj = QUALITIES[4] || { name: '極品', level: 5, color: '#f1c40f' };
    const typeName = { weapon: '聖劍', armor: '寶鎧', accessory: '佩玉' }[type];
    const icon = { weapon: '🗡️', armor: '🛡️', accessory: '📿' }[type];

    const levelBonus = Math.floor(player.level * 8);
    let atk = 0, def = 0;

    if (type === 'weapon') {
      atk = 800 + levelBonus + Math.floor(Math.random() * 200);
      def = 200;
    } else if (type === 'armor') {
      def = 700 + levelBonus + Math.floor(Math.random() * 150);
      atk = 150;
    } else {
      atk = 500 + levelBonus;
      def = 500 + levelBonus;
    }

    const xianEquip = {
      id: Date.now() + Math.random(),
      name: `三昧紫金·${qObj.name}${typeName}`,
      type,
      quality: 5,
      qualityName: '極品',
      qualityColor: '#f1c40f',
      atk, def,
      icon
    };

    if (!player.inventory) player.inventory = [];
    player.inventory.push(xianEquip);
    audioSynth.sfxReward();
    addLog(`【✨ 熔煉成功】三昧真火極致煉化！成功開爐產出【${xianEquip.name}】(極品 | 攻+${xianEquip.atk} 防+${xianEquip.def}) 收入背包！`, 'log-crit');

  } else {
    // 熔煉普通品質
    const types = ['weapon', 'armor', 'accessory'];
    const type = types[Math.floor(Math.random() * types.length)];
    const typeName = { weapon: '鐵劍', armor: '布衣', accessory: '佩環' }[type];
    const icon = { weapon: '🗡️', armor: '🛡️', accessory: '📿' }[type];

    const levelBonus = Math.floor(player.level * 3);
    const normalEquip = {
      id: Date.now() + Math.random(),
      name: `三昧火粹·上品${typeName}`,
      type,
      quality: 3,
      qualityName: '中品',
      qualityColor: '#3498db',
      atk: type === 'weapon' ? 300 + levelBonus : 80,
      def: type === 'armor' ? 250 + levelBonus : 60,
      icon
    };

    if (!player.inventory) player.inventory = [];
    player.inventory.push(normalEquip);
    addLog(`【熔煉完成】真火熄滅，獲得【${normalEquip.name}】(中品 | 攻+${normalEquip.atk} 防+${normalEquip.def}) 已存入背包。`, 'log-system');
  }

  smeltPool = [];
  saveGame();
  renderInventory();
  renderSmeltPoolUI();
}

// ============================================
// 核心頂部分頁導覽列監聽器 (確保所有 .tab-btn 100% 順暢點擊切換)
// ============================================
function bindTabButtons() {
  const tabBtns = document.querySelectorAll('.tab-btn');
  const tabContents = document.querySelectorAll('.tab-content');

  tabBtns.forEach(btn => {
    btn.onclick = (e) => {
      e.preventDefault();
      const targetTabId = btn.getAttribute('data-tab');

      tabBtns.forEach(b => b.classList.remove('active'));
      tabContents.forEach(c => {
        c.classList.remove('active');
        c.style.display = 'none';
      });

      btn.classList.add('active');
      const targetContent = document.getElementById(`tab-${targetTabId}`);
      if (targetContent) {
        targetContent.classList.add('active');
        targetContent.style.display = 'block';
      }

      audioSynth.sfxCraft();

      if (targetTabId === 'bag') renderInventory();
      if (targetTabId === 'alchemy' || targetTabId === 'forge') renderFurnacesUI();
      if (targetTabId === 'alchemy') renderAlchemyTab();
      if (targetTabId === 'shop') renderShopTab();
    };
  });
}

// 頁面加載完成後自動綁定
if (document.readyState === 'loading') {
  document.addEventListener('DOMContentLoaded', bindTabButtons);
} else {
  bindTabButtons();
}

// 全局核心按鈕全覆蓋防護與功能實作
function sortInventory() {
  if (!player.inventory || !Array.isArray(player.inventory)) return;
  player.inventory.sort((a, b) => {
    const qA = a.quality || 1;
    const qB = b.quality || 1;
    if (qB !== qA) return qB - qA;
    const valA = (a.atk || 0) + (a.def || 0);
    const valB = (b.atk || 0) + (b.def || 0);
    return valB - valA;
  });
  audioSynth.sfxReward();
  renderInventory();
  addLog('【乾坤背包】已依據【品質與綜合屬性】為您全自動排序整齊！', 'log-system');
}

function salvageCheckedEquipment() {
  if (!player.inventory || player.inventory.length === 0) {
    addLog('【熔練提示】背包中暫無可熔練物資。', 'log-system');
    return;
  }
  const checkedEls = document.querySelectorAll('.chk-salvage-quality:checked');
  const checkedQualities = Array.from(checkedEls).map(el => parseInt(el.value));

  let salvagedCount = 0;
  let earnedCoins = 0;

  const remaining = [];
  player.inventory.forEach(item => {
    if (item && checkedQualities.includes(item.quality || 1) && item.type !== 'pill') {
      salvagedCount++;
      const val = (item.quality || 1) * 80 + Math.floor((item.atk || 0) + (item.def || 0)) * 2;
      earnedCoins += Math.floor(val * (GAME_CONFIG.salvageCoinMult || 1.0));
    } else {
      remaining.push(item);
    }
  });

  player.inventory = remaining;
  player.coins += earnedCoins;

  if (salvagedCount > 0) {
    audioSynth.sfxReward();
    addLog(`【一鍵熔練】成功熔練 ${salvagedCount} 件選定品級裝備，獲得靈石 +${earnedCoins}！`, 'log-crit');
  } else {
    addLog('【一鍵熔練】未找到符合勾選品級可熔練的裝備。', 'log-system');
  }

  saveGame();
  updateUI();
  renderInventory();
}

// ============================================
// 🔓 天道 GM 面板密碼解鎖解禁 (預設密碼: 1234)
// ============================================
function unlockGMPanel() {
  const pwdInput = document.getElementById('gm-password-input');
  const lockScreen = document.getElementById('gm-lock-screen');
  const panel = document.getElementById('gm-panel');
  const errEl = document.getElementById('gm-pass-error');

  if (!pwdInput || !panel || !lockScreen) return;

  const val = pwdInput.value.trim();
  if (val === '1234' || val === 'admin' || val === '8888') {
    lockScreen.style.display = 'none';
    panel.style.display = 'flex';
    if (errEl) errEl.style.display = 'none';
    audioSynth.sfxLevelUp();
    addLog('【天道驗證】天道印記密碼解鎖成功！天道 GM 動態控制台面板已開啓！', 'log-crit');
    loadGMConfigToInputs();
  } else {
    if (errEl) errEl.style.display = 'block';
    audioSynth.sfxHit();
    addLog('【天道驗證】天道密碼錯誤！解鎖失敗。', 'log-monster');
  }
}

function setupAllGameEventListeners() {
  const btnAttack = document.getElementById('btn-manual-attack');
  const btnAuto = document.getElementById('btn-toggle-auto');
  const btnMeditate = document.getElementById('btn-meditate');

  if (btnAttack) btnAttack.onclick = () => executeBattleRound();
  if (btnAuto) btnAuto.onclick = () => toggleAutoBattle();
  if (btnMeditate) btnMeditate.onclick = () => meditate();

  const btnPill = document.getElementById('btn-use-equipped-pill');
  const btnAutoEquip = document.getElementById('btn-auto-equip-best');
  if (btnPill) btnPill.onclick = () => useEquippedPill();
  if (btnAutoEquip) btnAutoEquip.onclick = () => autoEquipBestItems();

  const btnUnlock = document.getElementById('btn-unlock-gm');
  if (btnUnlock) btnUnlock.onclick = () => unlockGMPanel();
}

if (document.readyState === 'loading') {
  document.addEventListener('DOMContentLoaded', setupAllGameEventListeners);
} else {
  setupAllGameEventListeners();
}
setInterval(setupAllGameEventListeners, 1000);

// ============================================
// 🌐 全域按鈕相容別名與補充導出函數 (保證 HTML onclick 100% 成功)
// ============================================
function meditate() { toggleMeditate(); }
function confirmSpiritualRoot() { confirmCharacterClass(); }
function equipBossDropNow() {
  if (typeof currentDropEquipItem !== 'undefined' && currentDropEquipItem) {
    equipItem(currentDropEquipItem.id);
    addLog(`【⚡ 佩戴成功】成功穿戴爆出的【${currentDropEquipItem.name}】！屬性已大增！`, 'log-crit');
  }
  const modal = document.getElementById('boss-drop-modal');
  if (modal) modal.classList.remove('show');
}
function sortInventory() { sortInventoryByStats(); }
function salvageCheckedEquipment() { salvageCommonItems(); }
function switchSutraTab(cat) {
  const btns = document.querySelectorAll('.sutra-sub-btn');
  if (btns && btns.length > 0) {
    btns.forEach(b => b.classList.toggle('active', b.getAttribute('data-sutra-cat') === cat));
  }
  renderSutraTab(cat);
}
function switchTab(tabId) {
  document.querySelectorAll('.tab-btn').forEach(b => b.classList.remove('active'));
  document.querySelectorAll('.tab-content').forEach(c => c.classList.remove('active'));
  
  const targetBtn = document.querySelector(`.tab-btn[data-tab="${tabId}"]`);
  const targetTab = document.getElementById(`tab-${tabId}`);
  if (targetBtn) targetBtn.classList.add('active');
  if (targetTab) targetTab.classList.add('active');
  if (typeof audioSynth !== 'undefined' && audioSynth.playTone) audioSynth.playTone(300, 'square', 0.05);

  if (tabId === 'sutra') renderSutraTab();
  if (tabId === 'alchemy') renderAlchemyTab();
  if (tabId === 'shop') renderShopTab();
  if (tabId === 'bag') renderInventory();
}

// 計算玩家背包與丹藥槽中《保具定海丹》的數量 (支援名稱模糊匹配)
function getProtectPillCount() {
  let cnt = 0;

  const isProtect = (item) => {
    if (!item) return false;
    const name = (item.name || '').replace(/《|》/g, '');
    return name.includes('保具') || name.includes('定海') || item.recipeId === 'recipe_protect_pill' || (item.id && String(item.id).includes('protect'));
  };

  // 1. 檢查丹藥槽
  if (player.equipped && player.equipped.pill && isProtect(player.equipped.pill)) {
    cnt += (player.equipped.pill.count || 1);
  }

  // 2. 檢查乾坤背包
  if (player.inventory && Array.isArray(player.inventory)) {
    player.inventory.forEach(i => {
      if (i && isProtect(i)) {
        cnt += (i.count || 1);
      }
    });
  }

  return cnt;
}

// 扣除 1 顆保底丹藥
function consumeProtectPill() {
  const isProtect = (item) => {
    if (!item) return false;
    const name = (item.name || '').replace(/《|》/g, '');
    return name.includes('保具') || name.includes('定海') || item.recipeId === 'recipe_protect_pill' || (item.id && String(item.id).includes('protect'));
  };

  if (player.equipped && player.equipped.pill && isProtect(player.equipped.pill)) {
    player.equipped.pill.count = (player.equipped.pill.count || 1) - 1;
    if (player.equipped.pill.count <= 0) player.equipped.pill = null;
    return true;
  }

  if (player.inventory && Array.isArray(player.inventory)) {
    const idx = player.inventory.findIndex(i => isProtect(i));
    if (idx !== -1) {
      player.inventory[idx].count = (player.inventory[idx].count || 1) - 1;
      if (player.inventory[idx].count <= 0) {
        player.inventory.splice(idx, 1);
      }
      return true;
    }
  }
  return false;
}

function onRefineTargetChange() {
  // 主動觸發煉丹相關 UI 更新邏輯 (若有定義)
  if (typeof updateRefineUI === 'function') updateRefineUI();
}

function openGiftModal() {
  const modal = document.getElementById('gift-modal');
  if (modal) modal.classList.add('show');
}
function closeGiftModal() {
  const modal = document.getElementById('gift-modal');
  if (modal) modal.classList.remove('show');
}
function toggleCRTEffect() {
  const crt = document.querySelector('.crt-overlay');
  if (crt) crt.style.display = crt.style.display === 'none' ? 'block' : 'none';
}
function toggleSoundEffect(e) {
  if (typeof audioSynth !== 'undefined') {
    audioSynth.enabled = !audioSynth.enabled;
    const btn = document.getElementById('toggle-sound');
    if (btn) btn.textContent = audioSynth.enabled ? '🔊 音效:開' : '🔇 音效:關';
  }
}
function triggerManualSave() {
  saveGame();
  if (typeof audioSynth !== 'undefined' && audioSynth.sfxReward) audioSynth.sfxReward();
  addLog('【系統】存檔成功！進度已儲存。', 'log-system');
}
function triggerManualReset() {
  if (confirm('確定要清除所有存檔並重新開始嗎？')) {
    localStorage.removeItem('wujin_honghuang_save');
    location.reload();
  }
}
function closeMerchantModal() {
  const modal = document.getElementById('merchant-modal');
  if (modal) modal.classList.remove('show');
  const banner = document.getElementById('merchant-banner');
  if (banner) banner.classList.remove('show');
}
function closeBossDropModal() {
  const modal = document.getElementById('boss-drop-modal');
  if (modal) modal.classList.remove('show');
}

// ============================================
// 🧪 九轉煉丹房動態繪製與開爐煉丹邏輯
// ============================================
function renderAlchemyTab() {
  const recipeContainer = document.getElementById('alchemy-recipes-grid');
  if (!recipeContainer) return;

  // 更新靈藥草藥庫存數量
  const herbNames = ['lingzhi', 'baicao', 'zhusha', 'longkui', 'renshen'];
  herbNames.forEach(h => {
    const el = document.getElementById(`mat-herb-${h}`);
    if (el) el.textContent = (player.herbs && player.herbs[h]) ? player.herbs[h] : 0;
  });

  recipeContainer.innerHTML = '';

  if (typeof PILL_RECIPES === 'undefined' || !Array.isArray(PILL_RECIPES)) return;

  const herbMapName = { lingzhi: '靈芝草', baicao: '百草露', zhusha: '硃砂果', longkui: '龍葵花', renshen: '千年人參' };
  const herbMapColor = { lingzhi: '#2ecc71', baicao: '#3498db', zhusha: '#e74c3c', longkui: '#9b59b6', renshen: '#f1c40f' };

  PILL_RECIPES.forEach(r => {
    const card = document.createElement('div');
    card.className = 'sutra-card';
    card.style.borderColor = r.quality === '神品' ? '#e74c3c' : r.quality === '極品' ? '#f1c40f' : r.quality === '上品' ? '#9b59b6' : r.quality === '中品' ? '#3498db' : '#2ecc71';

    // 材料耗費需求 HTML
    let matHtmlArr = [];
    let canCraft = player.coins >= (r.coinsCost || 0);

    if (r.materials) {
      Object.keys(r.materials).forEach(mKey => {
        const reqCnt = r.materials[mKey];
        const hasCnt = (player.herbs && player.herbs[mKey]) ? player.herbs[mKey] : 0;
        const color = herbMapColor[mKey] || '#fff';
        const isEnough = hasCnt >= reqCnt;
        if (!isEnough) canCraft = false;
        matHtmlArr.push(`<span style="color:${color};">${herbMapName[mKey] || mKey} ${hasCnt}/${reqCnt}</span>`);
      });
    }

    const matStr = matHtmlArr.join(' · ');

    // 尋找是否有空閒的丹爐
    const freeFurnace = (player.alchFurnaces || []).find(f => f && f.status === 'idle');

    card.innerHTML = `
      <div style="display:flex; justify-content:space-between; align-items:center; border-bottom:1px dashed #333; padding-bottom:6px;">
        <span style="font-size:1rem; font-weight:bold; color:var(--pixel-gold);">${r.icon || '💊'} ${r.name}</span>
        <span class="sutra-quality-badge" style="background:${card.style.borderColor}; color:#000;">${r.quality}</span>
      </div>
      <div style="font-size:0.75rem; color:#aaa; margin:6px 0; line-height:1.3;">${r.desc}</div>
      <div style="font-size:0.72rem; background:#0a0a14; padding:4px 6px; border-radius:4px; margin-bottom:8px;">
        <div>💰 耗費靈石: <span style="color:var(--pixel-gold);">${r.coinsCost || 0}</span></div>
        <div>🌿 耗費草藥: ${matStr}</div>
      </div>
      <button class="pixel-btn ${canCraft && freeFurnace ? 'btn-gold' : ''}" 
              onclick="startAlchemyCraft('${r.id}')" 
              ${!canCraft || !freeFurnace ? 'disabled' : ''} 
              style="width:100%; padding:6px; font-size:0.8rem;">
        ${!freeFurnace ? '🔒 無空閒丹爐' : !canCraft ? '⚠️ 材料/靈石不足' : '🔥 選擇丹爐開火煉製'}
      </button>
    `;

    recipeContainer.appendChild(card);
  });
}

// 選擇空閒丹爐開火煉製丹藥
function startAlchemyCraft(recipeId) {
  if (typeof PILL_RECIPES === 'undefined') return;
  const recipe = PILL_RECIPES.find(r => r.id === recipeId);
  if (!recipe) return;

  // 檢查靈石
  if (player.coins < (recipe.coinsCost || 0)) {
    addLog(`【煉丹失敗】靈石不足！需要 ${recipe.coinsCost} 靈石。`, 'log-monster');
    return;
  }

  // 檢查草藥
  if (recipe.materials) {
    for (const mKey in recipe.materials) {
      const req = recipe.materials[mKey];
      const has = (player.herbs && player.herbs[mKey]) ? player.herbs[mKey] : 0;
      if (has < req) {
        addLog(`【煉丹失敗】草藥材料不足！`, 'log-monster');
        return;
      }
    }
  }

  // 尋找空閒丹爐
  const furnace = (player.alchFurnaces || []).find(f => f && f.status === 'idle');
  if (!furnace) {
    addLog(`【煉丹失敗】當前沒有空閒的丹爐！請等待已有丹爐完成開爐。`, 'log-monster');
    return;
  }

  // 扣除資源
  player.coins -= (recipe.coinsCost || 0);
  if (recipe.materials) {
    for (const mKey in recipe.materials) {
      player.herbs[mKey] -= recipe.materials[mKey];
    }
  }

  // 設定丹爐煉化狀態 (基礎 15 秒，受丹爐與丹道心法加速)
  const baseTime = 15;
  const totalSpeedMult = (furnace.speedMult || 1.0) * (1 + calculateTotalAlchSpeedBoost());
  const durationMs = Math.max(3000, Math.floor((baseTime / totalSpeedMult) * 1000));

  furnace.status = 'cooking';
  furnace.recipeId = recipe.id;
  furnace.startTime = Date.now();
  furnace.duration = durationMs;

  if (typeof audioSynth !== 'undefined' && audioSynth.sfxCraft) audioSynth.sfxCraft();
  addLog(`【開火煉丹】已將【${recipe.name}】投入【${furnace.name}】中，預計 ${Math.ceil(durationMs/1000)} 秒後開爐出丹！`, 'log-crit');

  saveGame();
  updateUI();
  renderAlchemyTab();
}

function calculateTotalAlchSpeedBoost() {
  let boost = 0;
  if (player.purchasedSutras && Array.isArray(player.purchasedSutras)) {
    player.purchasedSutras.forEach(id => {
      const s = ALL_SUTRAS.find(item => item.id === id);
      if (s && s.alchSpeed) boost += s.alchSpeed;
    });
  }
  return boost;
}

// ============================================
// 📜 藏經閣動態繪製與心法參悟邏輯
// ============================================


// 參悟心法邏輯
function buySutra(sutraId) {
  if (typeof ALL_SUTRAS === 'undefined') return;

  // 雙重匹配：傳入 ID 或名稱比對
  const sutra = ALL_SUTRAS.find(s => s.id === sutraId || s.name === sutraId);
  if (!sutra) {
    addLog(`【參悟失敗】未找到對應的絕學心法秘籍。`, 'log-monster');
    return;
  }

  if (!player.purchasedSutras) player.purchasedSutras = [];
  if (player.purchasedSutras.includes(sutra.id)) {
    addLog(`【參悟提示】你已經參悟過【${sutra.name}】了！效果已永久生效。`, 'log-system');
    return;
  }

  const sutraCost = (sutra.price !== undefined) ? sutra.price : (sutra.cost !== undefined ? sutra.cost : 0);
  if ((player.coins || 0) < sutraCost) {
    addLog(`【靈石不足】參悟【${sutra.name}】需要 ${sutraCost} 靈石！你目前持有 ${player.coins || 0} 靈石。`, 'log-monster');
    return;
  }

  player.coins -= sutraCost;
  player.purchasedSutras.push(sutra.id);

  if (typeof audioSynth !== 'undefined' && audioSynth.sfxLevelUp) audioSynth.sfxLevelUp();
  addLog(`【🎉 頓悟參悟】成功花費 ${sutraCost} 靈石參悟【${sutra.name}】！全屬性已永久大幅提升！`, 'log-crit');

  // 如果是在神秘商人店鋪購買，商人購買 1 件後滿意離場
  if (typeof currentMerchantItems !== 'undefined' && currentMerchantItems && currentMerchantItems.some(s => s && (s.id === sutra.id || s.id === sutraId))) {
    currentMerchantItems = [];
    const banner = document.getElementById('merchant-banner');
    if (banner) banner.classList.remove('show');
    const merchantModal = document.getElementById('merchant-modal');
    if (merchantModal) merchantModal.classList.remove('show');
    addLog(`【商人離場】神秘商人滿意地收下靈石，將【${sutra.name}】交給您後作揖化為一道遁光離去！`, 'log-crit');
  }

  recalculatePlayerStats();
  saveGame();
  updateUI();
  renderSutraTab();
  if (typeof renderMerchantShop === 'function') renderMerchantShop();
}

function calculateTotalSutraStats() {
  let atk = 0, def = 0, expSpeed = 0;
  if (player.purchasedSutras && Array.isArray(player.purchasedSutras)) {
    player.purchasedSutras.forEach(id => {
      const s = ALL_SUTRAS.find(item => item.id === id);
      if (s) {
        atk += s.atk || 0;
        def += s.def || 0;
        expSpeed += s.expSpeed || 0;
      }
    });
  }
  return { atk, def, expSpeed };
}

// ============================================
// 🏪 坊市商鋪動態繪製與購買邏輯
// ============================================
let currentShopBuyMultiplier = 1;

// 切換坊市單次購入倍數 (×1, ×10, ×50, ×100)
function setShopBuyMultiplier(mult) {
  currentShopBuyMultiplier = parseInt(mult) || 1;

  ['1', '10', '50', '100'].forEach(m => {
    const btn = document.getElementById(`btn-shop-mult-${m}`);
    if (btn) {
      if (parseInt(m) === currentShopBuyMultiplier) btn.classList.add('active');
      else btn.classList.remove('active');
    }
  });

  renderShopTab();
}

// 🏪 坊市商鋪動態繪製與購買邏輯 (支援批量倍數連動)
function renderShopTab() {
  const shopContainer = document.getElementById('general-shop-grid');
  if (!shopContainer) return;

  const coinsEl = document.getElementById('shop-player-coins');
  if (coinsEl) coinsEl.textContent = (player.coins || 0).toLocaleString();

  shopContainer.innerHTML = '';

  if (typeof SHOP_ITEMS === 'undefined' || !Array.isArray(SHOP_ITEMS)) return;

  const mult = currentShopBuyMultiplier || 1;

  SHOP_ITEMS.forEach(item => {
    const card = document.createElement('div');
    card.className = 'shop-item-card';

    // 判斷該商品是否支援倍數購入 (丹藥/神材/草藥支援倍數，丹爐/一次性不支援)
    const isStackable = item.category === 'pill' || item.category === 'herb' || item.category === 'material' || item.type === 'pill';
    const effectiveMult = isStackable ? mult : 1;
    const totalPrice = (item.price || 0) * effectiveMult;
    const totalCount = (item.count || 1) * effectiveMult;

    const canAfford = player.coins >= totalPrice;

    card.innerHTML = `
      <div style="font-size:1.8rem; margin-bottom:4px;">${item.icon || '📦'}</div>
      <div style="font-size:0.88rem; font-weight:bold; color:var(--pixel-gold);">${item.name} ${effectiveMult > 1 ? `<span style="color:#2ecc71; font-size:0.75rem;">(×${effectiveMult})</span>` : ''}</div>
      <div style="font-size:0.75rem; color:#aaa; margin:4px 0; height:32px; overflow:hidden;">${item.desc || ''}</div>
      <div style="font-size:0.82rem; color:var(--pixel-gold); font-weight:bold; margin-bottom:6px;">💰 ${totalPrice.toLocaleString()} 靈石 ${effectiveMult > 1 ? `<span style="font-size:0.7rem; color:#888;">(得 ${totalCount} 個)</span>` : ''}</div>
      <button class="pixel-btn ${canAfford ? 'btn-gold' : ''}" 
              onclick="buyShopItem('${item.id}')" 
              ${!canAfford ? 'disabled' : ''} 
              style="width:100%; padding:6px; font-size:0.8rem;">
        ${canAfford ? `🛒 購入 ${effectiveMult > 1 ? '×' + effectiveMult : '商品'}` : '⚠️ 靈石不足'}
      </button>
    `;

    shopContainer.appendChild(card);
  });
}

// 購買坊市商品邏輯 (批量倍數發貨)
function buyShopItem(itemId) {
  if (typeof SHOP_ITEMS === 'undefined') return;
  const item = SHOP_ITEMS.find(i => i.id === itemId);
  if (!item) return;

  const isStackable = item.category === 'pill' || item.category === 'herb' || item.category === 'material' || item.type === 'pill';
  const effectiveMult = isStackable ? (currentShopBuyMultiplier || 1) : 1;
  const totalPrice = (item.price || 0) * effectiveMult;
  const addAmount = (item.count || 1) * effectiveMult;

  if (player.coins < totalPrice) {
    addLog(`【購買失敗】靈石不足！購買【${item.name}】×${effectiveMult} 需要 ${totalPrice.toLocaleString()} 靈石。`, 'log-monster');
    return;
  }

  player.coins -= totalPrice;

  if (item.category === 'pill' || item.type === 'pill' || (item.name && item.name.includes('丹'))) {
    const pillObj = {
      id: 'pill_shop_' + item.id + '_' + Date.now(),
      name: item.name || '靈丹',
      type: 'pill',
      quality: item.quality || '良品',
      qualityColor: '#3498db',
      icon: item.icon || '💊',
      desc: item.desc || '坊市購入之靈丹妙藥',
      count: addAmount,
      action: item.action
    };
    addPillToInventory(pillObj, addAmount);
  } else if (item.category === 'herb' && item.key) {
    if (!player.herbs) player.herbs = {};
    player.herbs[item.key] = (player.herbs[item.key] || 0) + addAmount;
    addLog(`【坊市購入】成功購買【${item.name}】×${addAmount}！已放入靈藥草藥庫存。`, 'log-drop');
  } else if (item.category === 'material' && item.key) {
    if (!player.materials) player.materials = {};
    player.materials[item.key] = (player.materials[item.key] || 0) + addAmount;
    addLog(`【坊市購入】成功購買【${item.name}】×${addAmount}！已放入五行神材庫存。`, 'log-drop');
  } else if (typeof item.action === 'function') {
    for (let k = 0; k < effectiveMult; k++) {
      item.action();
    }
  }

  if (typeof audioSynth !== 'undefined' && audioSynth.sfxReward) audioSynth.sfxReward();

  saveGame();
  updateUI();
  renderShopTab();
}

// 🧙‍♂️ 開啟神秘商人 Modal 彈窗
function openMerchantModal() {
  if (typeof generateMerchantItems === 'function') generateMerchantItems();
  if (typeof renderMerchantShop === 'function') renderMerchantShop();
  const modal = document.getElementById('merchant-modal');
  if (modal) modal.classList.add('show');
}

// ============================================
// ⚒️ 法寶五行精煉系統 (+0 ~ +10 全屬性威力提升)
// ============================================

// 預設精煉成功率對照表 (+1~+10)
const DEFAULT_REFINE_RATES = {
  1: 80, 2: 70, 3: 60, 4: 50, 5: 40, 6: 30, 7: 20, 8: 10, 9: 5, 10: 3
};

let selectedRefineSlot = 'weapon';

// 選擇欲精煉的裝備槽位 (weapon / armor / accessory)
function selectRefineSlot(slotKey) {
  selectedRefineSlot = slotKey;

  // 更新按鈕選中樣式
  ['weapon', 'armor', 'accessory'].forEach(s => {
    const btn = document.getElementById(`btn-refine-slot-${s}`);
    if (btn) {
      if (s === slotKey) btn.classList.add('active');
      else btn.classList.remove('active');
    }
  });

  onRefineTargetChange();
}

// 刷新並更新身上穿戴裝備的名稱標籤與保具定海丹持數
function populateRefineEquipmentDropdown() {
  if (!player.equipped) ensurePlayerEquipped();

  const slotMap = { weapon: 'weapon', armor: 'armor', accessory: 'accessory' };
  
  ['weapon', 'armor', 'accessory'].forEach(s => {
    const txtEl = document.getElementById(`refine-slot-txt-${s}`);
    const eq = player.equipped[s];
    if (txtEl) {
      if (eq && eq.name) {
        const lvlText = (eq.refineLvl && eq.refineLvl > 0) ? `+${eq.refineLvl}` : '+0';
        txtEl.textContent = `${eq.name.substring(0, 6)} ${lvlText}`;
      } else {
        txtEl.textContent = '(未穿戴)';
      }
    }
  });

  // 100% 強制刷新保具定海丹持數與當前精煉台面板數據
  onRefineTargetChange();
}

// 選擇待精煉法寶變更時的回調
function onRefineTargetChange() {
  const previewEl = document.getElementById('refine-item-preview');
  const costTextEl = document.getElementById('refine-cost-text');
  const rateTextEl = document.getElementById('refine-rate-text');
  const pillStatusEl = document.getElementById('refine-pill-count-status');

  // 計算玩家當前《保具定海丹》持用總數
  const protectPillCount = getProtectPillCount();
  if (pillStatusEl) {
    pillStatusEl.innerHTML = `持有數量: <b style="color:${protectPillCount > 0 ? '#2ecc71' : '#e74c3c'};">${protectPillCount}</b> 顆 ${protectPillCount > 0 ? '✨ (已自動準備保底)' : '(無保底丹，失敗有機率毀壞)'}`;
  }

  const targetItem = player.equipped ? player.equipped[selectedRefineSlot] : null;

  if (!targetItem || !targetItem.name) {
    if (previewEl) {
      const slotName = selectedRefineSlot === 'weapon' ? '本命武器' : selectedRefineSlot === 'armor' ? '五行寶鎧' : '護身法寶';
      previewEl.innerHTML = `<div style="color:#888; text-align:center; padding:10px;">目前未穿戴【${slotName}】<br><span style="font-size:0.75rem;">(請先至背包穿戴裝備後再進行精煉)</span></div>`;
    }
    if (costTextEl) costTextEl.textContent = '所需神材: 尚無';
    if (rateTextEl) rateTextEl.textContent = '當前成功率: --';
    return;
  }

  const currentLvl = targetItem.refineLvl || 0;
  const nextLvl = currentLvl + 1;

  if (currentLvl >= 10) {
    if (previewEl) previewEl.innerHTML = `<div style="color:#f1c40f; font-weight:bold;">👑 【${targetItem.name} +10】已達到五行極限精煉最高等級！ (+10% 全屬性威力)</div>`;
    if (costTextEl) costTextEl.textContent = '所需神材: 已滿級';
    if (rateTextEl) rateTextEl.textContent = '當前成功率: 已頂峰';
    return;
  }

  // 1. 計算具體攻防屬性加成數據 (例如: 攻+1000(+20) ➔ 攻+1000(+30))
  const baseAtk = targetItem.atk || 0;
  const baseDef = targetItem.def || 0;

  const nowAtkAdd = Math.floor(baseAtk * (currentLvl / 100));
  const nextAtkAdd = Math.floor(baseAtk * (nextLvl / 100));

  const nowDefAdd = Math.floor(baseDef * (currentLvl / 100));
  const nextDefAdd = Math.floor(baseDef * (nextLvl / 100));

  let statNowStr = '';
  let statNextStr = '';

  if (baseAtk > 0) {
    statNowStr += `攻 +${baseAtk}<span style="color:#2ecc71;">(+${nowAtkAdd})</span> `;
    statNextStr += `攻 +${baseAtk}<span style="color:#f1c40f;">(+${nextAtkAdd})</span> `;
  }
  if (baseDef > 0) {
    statNowStr += `防 +${baseDef}<span style="color:#2ecc71;">(+${nowDefAdd})</span>`;
    statNextStr += `防 +${baseDef}<span style="color:#f1c40f;">(+${nextDefAdd})</span>`;
  }
  if (!statNowStr) statNowStr = `+${currentLvl}% 威力`;
  if (!statNextStr) statNextStr = `+${nextLvl}% 威力`;

  if (previewEl) {
    previewEl.innerHTML = `
      <div style="font-weight:bold; color:var(--pixel-gold);">${targetItem.icon || '🗡️'} ${targetItem.name} <span style="color:#f1c40f;">+${currentLvl}</span></div>
      <div style="font-size:0.75rem; color:#aaa; margin-top:3px;">當前效果: <b style="color:#fff;">${statNowStr}</b></div>
      <div style="font-size:0.75rem; color:#f1c40f; margin-top:2px;">精練+${nextLvl}: <b>${statNextStr}</b></div>
    `;
  }

  // 2. 計算材料消耗
  const costMap = calculateRefineMaterialsCost(targetItem);
  let costStrArr = [];
  const matNameMap = { goldMat: '金精石', woodMat: '神木芯', waterMat: '玄冰髓', fireMat: '離火精', earthMat: '息壤土' };
  
  let hasEnoughMat = true;
  Object.keys(costMap).forEach(key => {
    const req = costMap[key];
    const has = (player.materials && player.materials[key]) ? player.materials[key] : 0;
    if (has < req) hasEnoughMat = false;
    const color = has >= req ? '#2ecc71' : '#e74c3c';
    costStrArr.push(`<span style="color:${color};">${matNameMap[key] || key} ${has}/${req}</span>`);
  });

  if (costTextEl) costTextEl.innerHTML = `耗費神材: ${costStrArr.join(' · ')}`;

  // 3. 讀取 GM 控制台設定或預設成功率
  const rateInput = document.getElementById(`cfg-refine-${nextLvl}`);
  const rate = rateInput ? parseInt(rateInput.value) || DEFAULT_REFINE_RATES[nextLvl] : DEFAULT_REFINE_RATES[nextLvl];

  if (rateTextEl) {
    rateTextEl.innerHTML = `目標等級: <b style="color:var(--pixel-gold);">+${nextLvl}</b> | 精煉成功率: <b style="color:#2ecc71;">${rate}%</b> ${!hasEnoughMat ? ' <span style="color:#e74c3c;">(材料不足)</span>' : ''}`;
  }
}

// 根據裝備五行屬性計算精煉神材消耗 (總數 10 個)
function calculateRefineMaterialsCost(item) {
  const elem = item.element || item.category || 'all';

  if (elem === 'gold') return { goldMat: 10 };
  if (elem === 'wood') return { woodMat: 10 };
  if (elem === 'water') return { waterMat: 10 };
  if (elem === 'fire') return { fireMat: 10 };
  if (elem === 'earth') return { earthMat: 10 };

  // 雙屬性情況 (如金+火) 5+5
  if (typeof elem === 'string' && elem.includes('_')) {
    const parts = elem.split('_');
    const res = {};
    parts.forEach(p => {
      const key = p + 'Mat';
      res[key] = 5;
    });
    return res;
  }

  // 全屬性或無屬性: 各 2 個
  return { goldMat: 2, woodMat: 2, waterMat: 2, fireMat: 2, earthMat: 2 };
}

// 計算玩家背包與丹藥槽中《保具定海丹》的數量
function getProtectPillCount() {
  let cnt = 0;

  // 檢查丹藥槽
  if (player.equipped && player.equipped.pill && (player.equipped.pill.name.includes('保具定海丹') || player.equipped.pill.recipeId === 'recipe_protect_pill')) {
    cnt += (player.equipped.pill.count || 1);
  }

  // 檢查背包
  if (player.inventory && Array.isArray(player.inventory)) {
    player.inventory.forEach(i => {
      if (i && i.type === 'pill' && (i.name.includes('保具定海丹') || i.recipeId === 'recipe_protect_pill')) {
        cnt += (i.count || 1);
      }
    });
  }

  return cnt;
}

// 扣除 1 顆保底丹藥
function consumeProtectPill() {
  if (player.equipped && player.equipped.pill && (player.equipped.pill.name.includes('保具定海丹') || player.equipped.pill.recipeId === 'recipe_protect_pill')) {
    player.equipped.pill.count = (player.equipped.pill.count || 1) - 1;
    if (player.equipped.pill.count <= 0) player.equipped.pill = null;
    return true;
  }

  if (player.inventory && Array.isArray(player.inventory)) {
    const idx = player.inventory.findIndex(i => i && i.type === 'pill' && (i.name.includes('保具定海丹') || i.recipeId === 'recipe_protect_pill'));
    if (idx !== -1) {
      player.inventory[idx].count = (player.inventory[idx].count || 1) - 1;
      if (player.inventory[idx].count <= 0) player.inventory.splice(idx, 1);
      return true;
    }
  }

  return false;
}

// 🔥 執行五行精煉法寶
function startRefineEquipment() {
  if (!player.equipped) ensurePlayerEquipped();
  const targetItem = player.equipped[selectedRefineSlot];

  if (!targetItem || !targetItem.name) {
    const slotName = selectedRefineSlot === 'weapon' ? '本命武器' : selectedRefineSlot === 'armor' ? '五行寶鎧' : '護身法寶';
    addLog(`【精煉提示】身上未穿戴【${slotName}】！請先穿戴裝備。`, 'log-system');
    return;
  }

  const currentLvl = targetItem.refineLvl || 0;
  if (currentLvl >= 10) {
    addLog(`【精煉頂峰】法寶【${targetItem.name}】已達到最高精煉等級 +10！`, 'log-crit');
    return;
  }

  // 1. 檢查並扣除五行神材
  const costMap = calculateRefineMaterialsCost(targetItem);
  if (!player.materials) player.materials = {};

  for (const key in costMap) {
    const req = costMap[key];
    const has = player.materials[key] || 0;
    if (has < req) {
      addLog(`【精煉失敗】五行神材不足！無法開爐精煉。`, 'log-monster');
      return;
    }
  }

  // 扣除材料
  for (const key in costMap) {
    player.materials[key] -= costMap[key];
  }

  const nextLvl = currentLvl + 1;

  // 2. 計算成功率 (優先讀取 GM 設定)
  const rateInput = document.getElementById(`cfg-refine-${nextLvl}`);
  const successRate = rateInput ? parseInt(rateInput.value) || DEFAULT_REFINE_RATES[nextLvl] : DEFAULT_REFINE_RATES[nextLvl];

  const roll = Math.random() * 100;
  const isSuccess = roll < successRate;

  // 讀取 GM 設定之碎裂率 (預設 50%)
  const breakRateInput = document.getElementById('cfg-refine-break-rate');
  const breakRate = breakRateInput ? parseInt(breakRateInput.value) || 50 : 50;

  const useProtectCheck = document.getElementById('refine-use-protect-pill');
  const wantsProtect = useProtectCheck ? useProtectCheck.checked : true;
  const hasPill = getProtectPillCount() > 0;

  if (isSuccess) {
    targetItem.refineLvl = nextLvl;
    if (typeof audioSynth !== 'undefined' && audioSynth.sfxLevelUp) audioSynth.sfxLevelUp();

    const baseAtk = targetItem.atk || 0;
    const baseDef = targetItem.def || 0;
    const atkAdd = Math.floor(baseAtk * (nextLvl / 100));
    const defAdd = Math.floor(baseDef * (nextLvl / 100));

    let statLogStr = '';
    if (baseAtk > 0) statLogStr += ` 攻+${baseAtk}(+${atkAdd})`;
    if (baseDef > 0) statLogStr += ` 防+${baseDef}(+${defAdd})`;

    addLog(`【🔥 精煉大成功】吉星高照！成功將【${targetItem.name}】精煉升至 +${nextLvl}！屬性提升至:${statLogStr} (全屬性+${nextLvl}%)！`, 'log-crit');
  } else {
    // 失敗邏輯
    let isSavedByPill = false;

    if (wantsProtect && hasPill) {
      isSavedByPill = consumeProtectPill();
    }

    if (isSavedByPill) {
      if (typeof audioSynth !== 'undefined' && audioSynth.sfxReward) audioSynth.sfxReward();
      addLog(`【🛡️ 庇護保底】精煉失敗！但自動消耗了 1 顆《保具定海丹》，神力護持之下，法寶【${targetItem.name}】完好無損！`, 'log-element');
    } else {
      // 未使用保底丹，判定是否碎裂
      const breakRoll = Math.random() * 100;
      if (breakRoll < breakRate) {
        // 法寶碎裂毀壞！
        deleteEquipmentFromGame(targetItem.id);
        if (typeof audioSynth !== 'undefined' && audioSynth.sfxBossAlert) audioSynth.sfxBossAlert();
        addLog(`【💥 法器碎裂】天雷反噬！精煉失敗且法寶【${targetItem.name}】承受不住五行火候，瞬間化為灰燼碎裂消失！`, 'log-monster');
      } else {
        addLog(`【精煉失敗】火候未至，精煉失敗！幸好法寶【${targetItem.name}】保住並未碎裂。`, 'log-system');
      }
    }
  }

  recalculatePlayerStats();
  saveGame();
  updateUI();
  populateRefineEquipmentDropdown();
}

// 刪除裝備 (從背包或裝備槽)
function deleteEquipmentFromGame(itemIdStr) {
  if (player.equipped) {
    for (const slot in player.equipped) {
      if (player.equipped[slot] && String(player.equipped[slot].id) === String(itemIdStr)) {
        player.equipped[slot] = null;
        return;
      }
    }
  }

  if (player.inventory && Array.isArray(player.inventory)) {
    const idx = player.inventory.findIndex(i => i && String(i.id) === String(itemIdStr));
    if (idx !== -1) {
      player.inventory.splice(idx, 1);
    }
  }
}


