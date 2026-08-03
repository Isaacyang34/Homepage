/**
 * 《無盡洪荒：五行聖境》- 遊戲核心邏輯 (Game Engine v1.4 - Sutra & Merchant)
 */

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
  { id: 'shop_mat_earth', name: '神材·息壤土 ×3', icon: '🪨', price: 250, category: 'material', key: 'earthMat', count: 3 },
  
  // 實體爐具專區 (解鎖與升級爐位)
  { id: 'furnace_alch_ling', name: '《靈階紫砂丹爐》', icon: '🍵', price: 1000, category: 'furnace', furnaceType: 'alchemy', level: 2, speedMult: 1.5, desc: '解鎖/升級丹爐！煉化速度提升 1.5 倍 (開爐倒數 -33%)' },
  { id: 'furnace_alch_di', name: '《地階寒鐵丹爐》', icon: '🏺', price: 3500, category: 'furnace', furnaceType: 'alchemy', level: 3, speedMult: 2.2, desc: '地階神爐！煉化速度提升 2.2 倍 (開爐倒數 -55%)' },
  { id: 'furnace_alch_tian', name: '《天階赤炎丹爐》', icon: '🔥', price: 10000, category: 'furnace', furnaceType: 'alchemy', level: 4, speedMult: 3.5, desc: '天階聖爐！煉化速度提升 3.5 倍 (開爐倒數 -70%)' },
  { id: 'furnace_alch_god', name: '《神階九龍造化爐》', icon: '🐉', price: 30000, category: 'furnace', furnaceType: 'alchemy', level: 5, speedMult: 6.0, desc: '造化神爐！煉化速度提升 6.0 倍 (開爐僅需 5 秒)' },

  { id: 'furnace_forge_ling', name: '《靈階青銅鍛造爐》', icon: '🔨', price: 1000, category: 'furnace', furnaceType: 'forge', level: 2, speedMult: 1.5, desc: '解鎖/升級鍛造爐！開爐速度提升 1.5 倍' },
  { id: 'furnace_forge_di', name: '《地階玄鐵鍛造爐》', icon: '🌋', price: 3500, category: 'furnace', furnaceType: 'forge', level: 3, speedMult: 2.2, desc: '地階鍛造爐！開爐速度提升 2.2 倍' },
  { id: 'furnace_forge_tian', name: '《天階三昧真火爐》', icon: '♨️', price: 10000, category: 'furnace', furnaceType: 'forge', level: 4, speedMult: 3.5, desc: '天階真火爐！開爐速度提升 3.5 倍' },
  { id: 'furnace_forge_god', name: '《神階乾坤造化爐》', icon: '🌌', price: 30000, category: 'furnace', furnaceType: 'forge', level: 5, speedMult: 6.0, desc: '乾坤神爐！開爐速度提升 6.0 倍' }
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
let currentMerchantItems = [];

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
  if (player.isInjured) {
    addLog(`【負傷休養中】傷勢嚴重！請打坐調息或服用丹藥將氣血補至 100% 滿血方可再次歷練！`, 'log-monster');
    return;
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

  furnace.status = 'cooking';
  furnace.forgeData = { mode: currentForgeMode, elem: currentForgeElement };
  furnace.startTime = Date.now();
  furnace.duration = durationSec * 1000;

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

  if (mode === 'ke') {
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

  // 法寶裝備爆裝邏輯 (連動天道 GM 設定，100% 爆裝保證與 Modal 彈窗)
  const dropRate = isBossMonster 
    ? (GAME_CONFIG.bossDropEquipRate !== undefined ? GAME_CONFIG.bossDropEquipRate : 0.50)
    : (GAME_CONFIG.normalDropEquipRate !== undefined ? GAME_CONFIG.normalDropEquipRate : 0.10);

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
      addLog(`【👑 首領大爆裝備】${currentMonster.name} 轟然倒地解體！暴出【${droppedEquip.name}】(品級:${droppedEquip.qualityName} | 攻+${droppedEquip.atk} 防+${droppedEquip.def})！已寫入【🎒乾坤背包】(當前共 ${player.inventory.length} 件)！`, 'log-crit');
      showBossDropModal(droppedEquip);
    } else {
      addLog(`【🎁 戰利品爆裝】擊敗 ${currentMonster.name}！獲得【${droppedEquip.name}】(品級:${droppedEquip.qualityName})！已寫入【🎒乾坤背包】(當前共 ${player.inventory.length} 件)！`, 'log-drop');
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
}

function getRealmName(lvl) {
  const idx = Math.min(Math.floor((lvl - 1) / 3), REALMS.length - 1);
  return REALMS[idx];
}

function toggleAutoBattle() {
  const btn = document.getElementById('btn-toggle-auto');
  if (isAutoBattling) {
    clearInterval(autoBattleInterval);
    isAutoBattling = false;
    btn.textContent = '⚔️ 開啟自動掛機';
    btn.classList.remove('btn-gold');
    addLog(`【系統】已停止自動掛機。`, 'log-system');
  } else {
    isAutoBattling = true;
    btn.textContent = '⏸️ 停止自動掛機';
    btn.classList.add('btn-gold');
    addLog(`【系統】開始自動掛機修煉中...`, 'log-system');
    executeBattleRound();
    autoBattleInterval = setInterval(executeBattleRound, 1500);
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

// 產生神秘商人隨機販售品項 (每次3~5件)
function generateMerchantItems() {
  if (currentMerchantItems.length > 0) return; // 已有商人品項未告辭
  const unpurchased = ALL_SUTRAS.filter(s => !player.purchasedSutras.includes(s.id));
  const pool = unpurchased.length > 0 ? unpurchased : ALL_SUTRAS;
  const count = Math.min(pool.length, Math.floor(Math.random() * 3) + 3);
  
  // 隨機洗牌
  const shuffled = [...pool].sort(() => Math.random() - 0.5);
  currentMerchantItems = shuffled.slice(0, count);
}

// 購買並參悟心法 (Buy & Practice Sutra)
function buySutra(sutraId) {
  const sutra = ALL_SUTRAS.find(s => s.id === sutraId);
  if (!sutra) return;

  if (player.purchasedSutras.includes(sutraId)) {
    addLog(`【參悟提示】您已經參悟過《${sutra.name}》了！`, 'log-system');
    return;
  }

  if (player.coins < sutra.price) {
    addLog(`【靈石不足】無法購買《${sutra.name}》！需要靈石 ${sutra.price} 個。`, 'log-monster');
    return;
  }

  player.coins -= sutra.price;
  player.purchasedSutras.push(sutraId);

  audioSynth.sfxLevelUp();
  addLog(`【心法參悟】成功花費 靈石 ${sutra.price} 參悟《${sutra.name}》！實力大增！`, 'log-crit');

  // 如果是在神秘商人店鋪購買，商人購買 1 件後立刻離場
  if (currentMerchantItems && currentMerchantItems.some(s => s.id === sutraId)) {
    currentMerchantItems = [];
    document.getElementById('merchant-banner').classList.remove('show');
    const merchantModal = document.getElementById('merchant-modal');
    if (merchantModal) merchantModal.classList.remove('show');
    addLog(`【商人離場】神秘商人收下靈石，將《${sutra.name}》交給您後作揖化為一道遁光離去！`, 'log-crit');
  }

  recalculatePlayerStats();
  updateUI();
  renderMerchantShop();
  renderSutraTab();
}

// 渲染神秘商人店舖
function renderMerchantShop() {
  const container = document.getElementById('merchant-shop-grid');
  if (!container) return;
  container.innerHTML = '';

  const coinsEl = document.getElementById('merchant-player-coins');
  if (coinsEl) coinsEl.textContent = player.coins;

  if (!currentMerchantItems || currentMerchantItems.length === 0) {
    generateMerchantItems();
  }

  currentMerchantItems.forEach(sutra => {
    const isBought = player.purchasedSutras.includes(sutra.id);
    const card = document.createElement('div');
    card.className = `sutra-card ${isBought ? 'sutra-purchased' : ''}`;
    
    // 是否同靈根
    const isSameElem = (player.element === sutra.category || player.element === 'azure');
    
    card.innerHTML = `
      <div class="sutra-card-header">
        <span class="sutra-name">${sutra.name}</span>
        <span class="sutra-badge ${sutra.category === 'internal' ? 'sutra-type-internal' : 'sutra-type-martial'}">
          ${sutra.quality} · ${sutra.category === 'internal' ? '內功' : ELEMENT_NAMES[sutra.category] || '武學'}
        </span>
      </div>
      <div class="sutra-effect">${getSutraEffectText(sutra, isSameElem)}</div>
      <div class="sutra-desc">${sutra.desc}</div>
      ${isSameElem ? '<div style="font-size:0.7rem; color:#00ffff;">✨ 本命屬性契合 (1.15倍威力)</div>' : ''}
      <div style="display:flex; justify-content:space-between; align-items:center; margin-top:8px;">
        <span style="color:var(--pixel-gold); font-size:0.85rem; font-weight:bold;">💰 ${sutra.price} 靈石</span>
        <button class="pixel-btn ${isBought ? '' : 'btn-gold'}" ${isBought ? 'disabled' : ''} onclick="buySutra('${sutra.id}')">
          ${isBought ? '✓ 已售罄' : '🛒 拜購參悟'}
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

// ============================================
// 九轉煉丹房系統
// ============================================
function renderAlchemyTab() {
  const container = document.getElementById('alchemy-recipes-grid');
  if (!container) return;
  container.innerHTML = '';

  // 更新靈藥草藥 UI 數量
  if (!player.herbs) player.herbs = { lingzhi: 0, baicao: 0, zhusha: 0, longkui: 0, renshen: 0 };
  document.getElementById('mat-herb-lingzhi').textContent = player.herbs.lingzhi || 0;
  document.getElementById('mat-herb-baicao').textContent = player.herbs.baicao || 0;
  document.getElementById('mat-herb-zhusha').textContent = player.herbs.zhusha || 0;
  document.getElementById('mat-herb-longkui').textContent = player.herbs.longkui || 0;
  document.getElementById('mat-herb-renshen').textContent = player.herbs.renshen || 0;

  const herbNames = { lingzhi:'靈芝草', baicao:'百草露', zhusha:'硃砂果', longkui:'龍葵花', renshen:'千年人參' };

  PILL_RECIPES.forEach(recipe => {
    let matTextParts = [];
    let canCraft = player.coins >= recipe.coinsCost;

    Object.entries(recipe.materials).forEach(([hKey, count]) => {
      const have = player.herbs[hKey] || 0;
      const hName = herbNames[hKey] || hKey;
      if (have < count) canCraft = false;
      matTextParts.push(`${hName} ×${count} (${have}/${count})`);
    });

    const card = document.createElement('div');
    card.className = 'recipe-card';
    card.innerHTML = `
      <div class="recipe-header">
        <span style="font-weight:bold; color:#fff;">${recipe.icon} ${recipe.name}</span>
        <span class="sutra-badge sutra-type-martial">${recipe.quality}</span>
      </div>
      <div style="font-size:0.75rem; color:var(--pixel-gold);">${recipe.desc}</div>
      <div style="font-size:0.7rem; color:var(--text-muted);">
        消耗: ${matTextParts.join(' + ')} + 💰 ${recipe.coinsCost} 靈石
      </div>
      <button class="pixel-btn ${canCraft ? 'btn-gold' : ''}" ${canCraft ? '' : 'disabled'} 
              style="margin-top:4px; font-size:0.8rem;" onclick="startAlchemyInFurnace('${recipe.id}')">
        🔥 開爐煉製丹藥
      </button>
    `;
    container.appendChild(card);
  });
}

function craftPill(recipeId) {
  startAlchemyInFurnace(recipeId);
}

function ensurePlayerFurnaces() {
  if (!player.alchFurnaces || !Array.isArray(player.alchFurnaces) || player.alchFurnaces.length < 5) {
    player.alchFurnaces = [
      { id: 1, name: '凡品草木爐', level: 1, speedMult: 1.0, status: 'idle', recipeId: null, startTime: 0, duration: 0 },
      null, null, null, null
    ];
  }
  if (!player.forgeFurnaces || !Array.isArray(player.forgeFurnaces) || player.forgeFurnaces.length < 5) {
    player.forgeFurnaces = [
      { id: 1, name: '凡品石木爐', level: 1, speedMult: 1.0, status: 'idle', forgeData: null, startTime: 0, duration: 0 },
      null, null, null, null
    ];
  }
}

function startAlchemyInFurnace(recipeId) {
  ensurePlayerFurnaces();
  const recipe = PILL_RECIPES.find(r => r.id === recipeId);
  if (!recipe) return;

  const idleIdx = player.alchFurnaces.findIndex(f => f && f.status === 'idle');
  if (idleIdx === -1) {
    addLog(`【丹爐忙碌】所有解鎖的丹爐都在煉化中！請等待煉製完成或至坊市購入新丹爐！`, 'log-monster');
    return;
  }

  if (player.coins < recipe.coinsCost) {
    addLog(`【煉丹失敗】靈石不足！需要 ${recipe.coinsCost} 靈石。`, 'log-monster');
    return;
  }

  const herbNames = { lingzhi:'靈芝草', baicao:'百草露', zhusha:'硃砂果', longkui:'龍葵花', renshen:'千年人參' };
  for (const [hKey, count] of Object.entries(recipe.materials)) {
    if ((player.herbs[hKey] || 0) < count) {
      addLog(`【煉丹失敗】${herbNames[hKey]} 不足！`, 'log-monster');
      return;
    }
  }

  // 扣除資源
  player.coins -= recipe.coinsCost;
  for (const [hKey, count] of Object.entries(recipe.materials)) {
    player.herbs[hKey] -= count;
  }

  const furnace = player.alchFurnaces[idleIdx];
  const baseSec = GAME_CONFIG.alchBaseTime || 60;
  const alchBonus = calculateAlchemySpeedBonus();
  const totalSpeed = furnace.speedMult + alchBonus;
  const durationSec = Math.max(3, Math.floor(baseSec / totalSpeed));

  furnace.status = 'cooking';
  furnace.recipeId = recipe.id;
  furnace.startTime = Date.now();
  furnace.duration = durationSec * 1000;

  audioSynth.sfxCraft();
  addLog(`【丹爐開火】使用【${furnace.name}】(速度 ${totalSpeed.toFixed(1)}x) 開火煉製 ${recipe.name}！倒數 ${durationSec} 秒後可收取！`, 'log-crit');

  updateUI();
}

function collectAlchemyResult(idx) {
  const furnace = player.alchFurnaces[idx];
  if (!furnace || furnace.status !== 'completed') return;

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

  if (!player.inventory || !Array.isArray(player.inventory)) player.inventory = [];
  player.inventory.push(pillItem);

  furnace.status = 'idle';
  furnace.recipeId = null;

  audioSynth.sfxReward();
  addLog(`【✨ 收穫丹藥】神鼎出丹！成功從 ${furnace.name} 取出【${recipe.name}】正式收入乾坤背包！`, 'log-crit');

  updateUI();
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

function equipItem(itemId) {
  const itemIdx = player.inventory.findIndex(i => i.id === itemId);
  if (itemIdx === -1) return;

  const item = player.inventory[itemIdx];
  const slotType = item.type;

  if (player.equipped[slotType]) {
    player.inventory.push(player.equipped[slotType]);
  }

  player.equipped[slotType] = item;
  player.inventory.splice(itemIdx, 1);

  audioSynth.sfxReward();
  recalculatePlayerStats();
  updateUI();

  if (slotType === 'pill') {
    addLog(`【丹藥放入槽位】已將【${item.name}】放入丹藥欄位！點擊下方【💊 服用槽中丹藥】即可服用！`, 'log-crit');
  } else {
    addLog(`【裝備】已穿戴 ${item.name}！`, 'log-system');
  }
}

function unequipItem(slotType) {
  if (!player.equipped[slotType]) return;

  const item = player.equipped[slotType];
  player.inventory.push(item);
  player.equipped[slotType] = null;

  audioSynth.sfxHit();
  recalculatePlayerStats();
  updateUI();
  addLog(`【裝備】已卸下 ${item.name}。`, 'log-system');
}

// 服用裝備在丹藥槽中的丹藥
function useEquippedPill() {
  if (!player.equipped || !player.equipped.pill) {
    addLog('【服丹提示】丹藥槽為空！請先在背包中點擊丹藥裝備至丹藥槽。', 'log-system');
    return;
  }

  const pill = player.equipped.pill;
  const recipe = PILL_RECIPES.find(r => r.id === pill.recipeId);
  
  let msg = '';
  if (recipe && recipe.action) {
    msg = recipe.action(player);
  } else {
    const heal = Math.floor(player.maxHp * 0.5);
    player.hp = Math.min(player.maxHp, player.hp + heal);
    msg = `吞服【${pill.name}】，氣血回復 ${heal} 點！`;
  }

  player.equipped.pill = null; // 消耗丹藥
  audioSynth.sfxReward();
  addLog(`【服丹療傷】${msg}`, 'log-crit');

  // 若服用丹藥使氣血全滿，立即消除負傷狀態
  if (player.hp >= player.maxHp && player.isInjured) {
    player.isInjured = false;
    addLog(`【💊 丹效神速】丹藥靈力完全修復全身經脈！負傷痊癒，可以重新歷練！`, 'log-crit');
  }

  recalculatePlayerStats();
  updateUI();
}

// 重新計算屬性 (算入裝備、蒼靈根115% 與心法加成)
function recalculatePlayerStats() {
  let extraAtk = 0;
  let extraDef = 0;
  let extraHp = 0;

  // 裝備加成
  Object.values(player.equipped).forEach(eq => {
    if (eq) {
      extraAtk += eq.atk || 0;
      extraDef += eq.def || 0;
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
}

function renderEquippedSlots() {
  const eq = player.equipped;
  if (!eq) return;
  ['weapon', 'armor', 'accessory', 'pill'].forEach(type => {
    const el = document.getElementById(`eq-${type}`);
    if (!el) return;
    if (eq[type]) {
      el.style.borderColor = eq[type].qualityColor || '#f1c40f';
      el.innerHTML = `
        <span style="font-size:1.3rem;">${eq[type].icon}</span>
        <span style="font-size:0.6rem; color:${eq[type].qualityColor || '#fff'}; font-weight:bold; line-height:1.1; text-align:center;">${eq[type].name}</span>
      `;
      el.onclick = () => unequipItem(type);
    } else {
      el.style.borderColor = type === 'pill' ? '#e74c3c' : '#3d3d63';
      let title = type === 'weapon' ? '空武器' : type === 'armor' ? '空防具' : type === 'accessory' ? '空飾品' : '空丹藥';
      el.innerHTML = `<span style="color:#666; font-size:0.75rem;">${title}</span>`;
      el.onclick = null;
    }
  });
}

function renderInventory() {
  const container = document.getElementById('inventory-grid');
  container.innerHTML = '';

  player.inventory.forEach(item => {
    const slot = document.createElement('div');
    slot.className = `item-slot item-quality-${item.quality}`;
    slot.setAttribute('draggable', 'true');
    slot.innerHTML = `
      <span class="item-icon">${item.icon}</span>
      <span style="font-size:0.6rem; color:${item.qualityColor}; text-align:center; line-height:1.1;">${item.name}</span>
    `;
    slot.title = `點擊裝備 / 拖曳至下方熔練爐\n攻: +${item.atk}  防: +${item.def}`;

    slot.addEventListener('dragstart', (e) => {
      e.dataTransfer.setData('text/plain', item.id);
    });

    slot.onclick = () => equipItem(item.id);
    container.appendChild(slot);
  });
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
  return base;
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
  player.pills += 1;
  audioSynth.sfxReward();
  addLog(`【購買成功】花費 ${pillCost} 靈石購入【回氣丹】×1！目前持有 ${player.pills} 顆。`, 'log-drop');
  updateUI();
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

// ============================================
// 實體爐具 (丹爐 & 鍛造爐) 渲染與倒數驅動器
// ============================================
function renderFurnacesUI() {
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
        card.innerHTML = `
          <div style="font-size:1.5rem;">🔒</div>
          <div style="font-size:0.75rem; color:var(--text-muted);">丹爐位 #${i+1} 未解鎖</div>
          <div style="font-size:0.65rem; color:var(--pixel-gold);">可至坊市購入解鎖</div>
        `;
      } else {
        card.className = `furnace-card ${furnace.status === 'cooking' ? 'active' : ''}`;
        let statusHtml = '';
        const totalSpeed = (furnace.speedMult + alchBonus).toFixed(1);

        if (furnace.status === 'idle') {
          statusHtml = `<div style="font-size:0.75rem; color:#2ecc71;">狀態: 🟢 空閒中</div>`;
        } else if (furnace.status === 'cooking') {
          const remainSec = Math.max(0, Math.ceil((furnace.startTime + furnace.duration - Date.now()) / 1000));
          const pct = Math.min(100, Math.floor(((Date.now() - furnace.startTime) / furnace.duration) * 100));
          statusHtml = `
            <div style="font-size:0.75rem; color:var(--pixel-fire);">🔥 煉化中 (${remainSec}s)</div>
            <div class="furnace-progress-bg">
              <div class="furnace-progress-fill" style="width:${pct}%"></div>
            </div>
          `;
        } else if (furnace.status === 'completed') {
          statusHtml = `
            <div style="font-size:0.75rem; color:var(--pixel-gold); font-weight:bold;">✨ 煉化完成！</div>
            <button class="pixel-btn btn-gold" style="font-size:0.75rem; padding:3px 6px; margin-top:4px;" onclick="collectAlchemyResult(${i})">✨ 收取丹藥</button>
          `;
        }

        card.innerHTML = `
          <div class="furnace-title">
            <span>${furnace.name}</span>
            <span class="furnace-speed-badge">${totalSpeed}x煉化</span>
          </div>
          ${statusHtml}
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
        card.innerHTML = `
          <div style="font-size:1.5rem;">🔒</div>
          <div style="font-size:0.75rem; color:var(--text-muted);">鍛造爐位 #${i+1} 未解鎖</div>
          <div style="font-size:0.65rem; color:var(--pixel-gold);">可至坊市購入解鎖</div>
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

        card.innerHTML = `
          <div class="furnace-title">
            <span>${furnace.name}</span>
            <span class="furnace-speed-badge">${totalSpeed}x開爐</span>
          </div>
          ${statusHtml}
        `;
      }
      forgeGrid.appendChild(card);
    }
  }
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


