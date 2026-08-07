// ═══════════════════════════════════════════════════════════
//  遊戲全局設定與數據定義 (Config & Game Data)
// ═══════════════════════════════════════════════════════════

const CFG = {
  scale: 1.45,
  vignette: true,
  particles: true,
  fps: false,
  bgmVol: 60,
  sfxVol: 100,
  muted: false,
  qiSpd: 1.0,
  rsSec: 8,
};

const REALMS = [
  { name: '練氣期一重', maxHp: 100, maxQi: 100, expNext: 80, atk: 10, def: 2, tribulation: false },
  { name: '練氣期二重', maxHp: 140, maxQi: 150, expNext: 180, atk: 15, def: 4, tribulation: false },
  { name: '練氣期三重', maxHp: 200, maxQi: 220, expNext: 350, atk: 22, def: 7, tribulation: false },
  { name: '練氣期四重', maxHp: 280, maxQi: 300, expNext: 600, atk: 30, def: 11, tribulation: false },
  { name: '練氣期五重', maxHp: 380, maxQi: 400, expNext: 1000, atk: 40, def: 16, tribulation: false },
  { name: '練氣期圓滿', maxHp: 500, maxQi: 500, expNext: 1500, atk: 55, def: 22, tribulation: true },
  { name: '築基期初期', maxHp: 800, maxQi: 800, expNext: 3000, atk: 80, def: 35, tribulation: false },
];

// 🏛️ 宗門內部固定建築房間 (Sect Hub Rooms - 無敵冒險區，無怪物/BOSS)
const SECT_ROOMS = {
  sect_main: {
    id: 'sect_main', name: '青雲宗 · 宗門大殿',
    desc: '修仙聖地大殿，宗主坐鎮。可聽取宗主教誨，獲取渡劫突破提示。',
    doors: ['E', 'S', 'W', 'N'],
    npcs: [{ x: 640, y: 280, name: '青雲宗主', icon: '👑', dlg: ['本座觀汝骨相清奇，日後必成大器。', '若能成功通關 5 階秘境並渡劫築基，本座親贈築基丹！', '去宗門山門（南門）踏出冒險吧！'] }],
    terrain: 'sect', safe: true
  },
  sect_alchemy: {
    id: 'sect_alchemy', name: '青雲宗 · 九轉煉丹房',
    desc: '丹香飄逸之所。可開爐煉製恢復丹藥、屬性丹藥與修為倍率丹。',
    doors: ['W'],
    npcs: [
      { x: 500, y: 350, name: '藥師兄', icon: '🌿', dlg: ['師弟！九轉丹爐已開，快來煉丹！'], onEnd: () => { if (window.openAlchemy) window.openAlchemy(); } },
      { x: 780, y: 350, name: '煉丹長老', icon: '🔥', dlg: ['金丹大道，靈藥相輔。師弟可往仙緣集市購入千年靈芝與赤炎花！'] }
    ],
    terrain: 'sect', safe: true
  },
  sect_forge: {
    id: 'sect_forge', name: '青雲宗 · 神兵煉器坊',
    desc: '地火熾熱之所。可打造劍（近戰1格）、槍（貫穿3格）、鏢（遠程10格）及五行神兵。',
    doors: ['E'],
    npcs: [
      { x: 500, y: 350, name: '藏劍閣長老', icon: '⚔', dlg: ['工欲善其事，必先利其器！快來鍛造五行神兵！'], onEnd: () => { if (window.openForge) window.openForge(); } },
      { x: 780, y: 350, name: '煉器師兄', icon: '🔨', dlg: ['師兄：地火鐵砧已準備就緒，淬火打造玄鐵神兵！'] }
    ],
    terrain: 'sect', safe: true
  },
  sect_market: {
    id: 'sect_market', name: '青雲宗 · 仙緣集市',
    desc: '熱鬧繁華的仙家集市。可自由買賣煉丹靈草、煉器玄鐵與天材地寶。',
    doors: ['S'],
    npcs: [
      { x: 640, y: 350, name: '集市掌櫃', icon: '💰', dlg: ['掌櫃：靈草靈礦，買賣公平！快來選購煉丹煉器材料！'], onEnd: () => { if (window.openMarketUI) window.openMarketUI(); } }
    ],
    terrain: 'sect', safe: true
  },
  sect_meditate: {
    id: 'sect_meditate', name: '青雲宗 · 靜心修煉房',
    desc: '靈氣靜謐之所。按 [K] 鍵打坐吐納，修煉功法熟練度與恢復靈力。',
    doors: ['N'],
    npcs: [{ x: 640, y: 350, name: '傳功長老', icon: '🧘', dlg: ['靜心打坐可大幅提升功法熟練度，心無旁鶩方能大成。'] }],
    terrain: 'sect', safe: true
  },
  sect_spring: {
    id: 'sect_spring', name: '青雲宗 · 靈泉池',
    desc: '靈泉碧波蕩漾。踏入池中可快速滋養身心，全額恢復氣血與靈力。',
    doors: ['N'],
    npcs: [{ x: 640, y: 350, name: '靈泉守衛', icon: '💧', dlg: ['浸泡靈泉可瞬間沐浴靈氣，恢復氣血與靈力。'] }],
    terrain: 'sect', safe: true
  },
  sect_gate: {
    id: 'sect_gate', name: '青雲宗 · 宗門山門',
    desc: '通往外部冒險秘境的關隘。踏出山門將進入生成式 5 層階梯地牢！',
    doors: ['N', 'S'],
    npcs: [{ x: 640, y: 280, name: '守山長老', icon: '⛩️', dlg: ['山門之外妖獸橫行，每座秘境皆有 5 階關卡與首領。', '打敗每層 BOSS 方可進階下一層，擊敗第 5 層 BOSS 解鎖全新秘境！'] }],
    terrain: 'sect', safe: true
  }
};

// 🌌 5 大秘境與怪物資料庫 (Five Regional Dungeon Worlds)
const DUNGEON_WORLDS = [
  {
    id: 'mountain', name: '青雲宗 · 外門靈山',
    terrain: 'mountain', reqRealm: 0,
    mobs: [
      { name: '赤焰妖狼', aiType: 'wolf', sprite: 'wolf', color: '#e84040', hp: 50, atk: 8, def: 2, reward: { stones: 15, exp: 25 } },
      { name: '青脊毒蛛', aiType: 'spider', sprite: 'spider', color: '#2ecc71', hp: 35, atk: 5, def: 1, reward: { stones: 10, exp: 15 } },
      { name: '玄鐵地鼠', aiType: 'mole', sprite: 'mole', color: '#7f8c8d', hp: 70, atk: 10, def: 4, reward: { stones: 20, exp: 35 } },
    ],
    boss: { name: '👑 赤焰狼王 · 首領', aiType: 'boss', sprite: 'boss', isBoss: true, color: '#ff1100', hp: 450, atk: 26, def: 8, reward: { stones: 250, exp: 400 } }
  },
  {
    id: 'cave', name: '玄陰洞府',
    terrain: 'cave', reqRealm: 1,
    mobs: [
      { name: '洞穴岩魔', aiType: 'golem', sprite: 'golem', color: '#8e44ad', hp: 110, atk: 16, def: 6, reward: { stones: 35, exp: 50 } },
      { name: '幽影冥蝠', aiType: 'bat', sprite: 'bat', color: '#3498db', hp: 65, atk: 12, def: 3, reward: { stones: 25, exp: 40 } },
    ],
    boss: { name: '👑 玄陰岩魔皇 · 首領', aiType: 'boss', sprite: 'boss', isBoss: true, color: '#9b59b6', hp: 750, atk: 38, def: 15, reward: { stones: 450, exp: 700 } }
  },
  {
    id: 'ruins', name: '古修遺跡 · 萬魔窟',
    terrain: 'ruins', reqRealm: 2,
    mobs: [
      { name: '遺跡守衛狼', aiType: 'wolf', sprite: 'wolf', color: '#e84040', hp: 180, atk: 24, def: 10, reward: { stones: 55, exp: 80 } },
      { name: '魔化巨岩魔', aiType: 'golem', sprite: 'golem', color: '#8e44ad', hp: 260, atk: 30, def: 16, reward: { stones: 70, exp: 110 } },
    ],
    boss: { name: '👑 赤焰魔狼皇 · 首領', aiType: 'boss', sprite: 'boss', isBoss: true, color: '#ff1100', hp: 1200, atk: 55, def: 22, reward: { stones: 800, exp: 1200 } }
  },
  {
    id: 'grotto', name: '萬仙靈窟',
    terrain: 'cave', reqRealm: 3,
    mobs: [
      { name: '萬仙幽蝠', aiType: 'bat', sprite: 'bat', color: '#00cfff', hp: 220, atk: 32, def: 12, reward: { stones: 90, exp: 140 } },
      { name: '靈石傀儡', aiType: 'golem', sprite: 'golem', color: '#f1c40f', hp: 350, atk: 40, def: 22, reward: { stones: 120, exp: 180 } },
    ],
    boss: { name: '👑 萬仙傀儡王 · 首領', aiType: 'boss', sprite: 'boss', isBoss: true, color: '#f39c12', hp: 1800, atk: 75, def: 30, reward: { stones: 1200, exp: 2000 } }
  },
  {
    id: 'abyss', name: '神魔修羅界',
    terrain: 'ruins', reqRealm: 4,
    mobs: [
      { name: '修羅魔狼', aiType: 'wolf', sprite: 'wolf', color: '#c0392b', hp: 450, atk: 55, def: 25, reward: { stones: 160, exp: 260 } },
      { name: '滅世冥蛛', aiType: 'spider', sprite: 'spider', color: '#8e44ad', hp: 380, atk: 48, def: 20, reward: { stones: 140, exp: 220 } },
    ],
    boss: { name: '👑 滅世修羅帝 · 終極首領', aiType: 'boss', sprite: 'boss', isBoss: true, color: '#e74c3c', hp: 3000, atk: 110, def: 45, reward: { stones: 2500, exp: 4000 } }
  }
];

// 📜 功法數據庫與五行相生關係 (Five-Element Sutras & Synergy Matrix)
const FIVE_ELEMENT_SUTRAS = [
  // 體修功法
  { id: 'sutra_body_1', name: '《金剛不壞體》', cat: 'BODY', elem: 'GOLD', desc: '體修硬功。最大氣血 +150，防禦力 +15', stats: { maxHp: 150, def: 15 } },
  { id: 'sutra_body_2', name: '《神力洗髓功》', cat: 'BODY', elem: 'EARTH', desc: '體修密化。最大氣血 +220，攻擊力 +20', stats: { maxHp: 220, atk: 20 } },
  // 身法功法
  { id: 'sutra_agility_1', name: '《凌波微步》', cat: 'AGILITY', elem: 'WATER', desc: '身法神行。移動速度 +1.5，衝刺冷卻 -30%', stats: { spd: 1.5 } },
  { id: 'sutra_agility_2', name: '《疾風追影訣》', cat: 'AGILITY', elem: 'WOOD', desc: '身法飄逸。移動速度 +2.2，防禦力 +10', stats: { spd: 2.2, def: 10 } },
  // 內功心法
  { id: 'sutra_internal_1', name: '《九轉太極功》', cat: 'INTERNAL', elem: 'EARTH', desc: '內功心法。最大靈力 +300，攻擊力 +25', stats: { maxQi: 300, atk: 25 } },
  { id: 'sutra_internal_2', name: '《紫霄吐納訣》', cat: 'INTERNAL', elem: 'GOLD', desc: '內功上乘。最大靈力 +500，防禦力 +20', stats: { maxQi: 500, def: 20 } },
  // 五行法術
  { id: 'sutra_spell_gold', name: '《庚金劍氣》', cat: 'SPELL', elem: 'GOLD', desc: '金系法術。攻擊附帶金刃割裂與 30% 無視防禦傷害', stats: { atk: 30 } },
  { id: 'sutra_spell_wood', name: '《萬木逢春》', cat: 'SPELL', elem: 'WOOD', desc: '木系法術。攻擊附帶藤蔓纏繞與 15% 氣血吸取', stats: { maxHp: 100, atk: 18 } },
  { id: 'sutra_spell_water', name: '《寒冰刺》', cat: 'SPELL', elem: 'WATER', desc: '水/冰系法術。攻擊附帶寒冰凍結減速敵方 50%', stats: { atk: 22 } },
  { id: 'sutra_spell_fire', name: '《赤焰爆破》', cat: 'SPELL', elem: 'FIRE', desc: '火系法術。攻擊引發赤焰二次爆破傷害', stats: { atk: 35 } },
  { id: 'sutra_spell_earth', name: '《泰山岩鎧》', cat: 'SPELL', elem: 'EARTH', desc: '土系法術。受擊召喚岩鎧抵擋 30% 傷害', stats: { def: 25 } },
];

// 五行相生矩陣：木生火 -> 火生土 -> 土生金 -> 金生水 -> 水生木
const FIVE_ELEMENT_SYNERGY = {
  WOOD:  { generates: 'FIRE',  name: '木火相生', desc: '🔥 觸發「木火相生」！火系二次爆破傷害 +40%，範圍擴大！' },
  FIRE:  { generates: 'EARTH', name: '火土相生', desc: '🗿 觸發「火土相生」！防禦力 +30，受擊反彈烈火爆破傷害！' },
  EARTH: { generates: 'GOLD',  name: '土金相生', desc: '⚔️ 觸發「土金相生」！金系劍氣傷害 +35%，穿透敵方護甲！' },
  GOLD:  { generates: 'WATER', name: '金水相生', desc: '❄️ 觸發「金水相生」！寒冰凍結時間 +1.5秒，靈力消耗 -25%！' },
  WATER: { generates: 'WOOD',  name: '水木相生', desc: '🌿 觸發「水木相生」！吸血效果 +25%，氣血恢復速度提升！' },
};

// ⚔️ 三類武器規格 (SWORD: 1格近戰, SPEAR: 3格貫穿, DART: 10格遠程)
const WEAPON_TYPES = {
  SWORD: { name: '劍 (近戰 1 格)', range: 1, hitRadius: 55, atkSpd: 1.0, icon: '🗡️' },
  SPEAR: { name: '槍 (貫穿 3 格)', range: 3, hitRadius: 150, atkSpd: 0.8, icon: '🔱' },
  DART:  { name: '鏢 (遠程 10 格)', range: 10, hitRadius: 500, atkSpd: 1.2, icon: '🎯' },
};

// 🔨 神兵煉器坊全新打造配方 (三類武器 x 五行屬性)
const FORGE_RECIPES_V2 = [
  { id: 'weapon_sword_gold', name: '🗡️ 庚金長劍', type: 'SWORD', elem: 'GOLD', cost: 150, atk: 25, desc: '近戰 1 格。金屬性劍芒，攻擊力 +25' },
  { id: 'weapon_spear_fire', name: '🔱 赤焰長槍', type: 'SPEAR', elem: 'FIRE', cost: 280, atk: 40, desc: '貫穿 3 格。火屬性長槍，直線爆刺 +40' },
  { id: 'weapon_dart_water', name: '🎯 寒冰飛鏢', type: 'DART',  elem: 'WATER', cost: 350, atk: 35, desc: '遠程 10 格。水/冰屬性飛鏢，減速敵方' },
  { id: 'weapon_spear_wood', name: '🔱 青木靈槍', type: 'SPEAR', elem: 'WOOD', cost: 320, atk: 35, desc: '貫穿 3 格。木屬性長槍，刺擊附帶吸血' },
  { id: 'weapon_sword_earth', name: '🗡️ 玄岩重劍', type: 'SWORD', elem: 'EARTH', cost: 400, atk: 50, desc: '近戰 1 格。土屬性重劍，基礎攻擊力 +50' },
];

window.CFG = CFG;
window.REALMS = REALMS;
window.SECT_ROOMS = SECT_ROOMS;
window.WORLD_AREAS = SECT_ROOMS;
window.DUNGEON_WORLDS = DUNGEON_WORLDS;
window.FIVE_ELEMENT_SUTRAS = FIVE_ELEMENT_SUTRAS;
window.FIVE_ELEMENT_SYNERGY = FIVE_ELEMENT_SYNERGY;
window.WEAPON_TYPES = WEAPON_TYPES;
window.FORGE_RECIPES_V2 = FORGE_RECIPES_V2;

// ════ 全域牆壁與門洞通道常數 (依據範例圖紅線與紅色通道精準對齊) ════
// 畫布 1280x720，紅框內緣為可行走區域，紅色實心為通道感應區
const WALL_N = 110;   // 北牆內緣 (紅線頂部) Y 最小值
const WALL_S = 610;   // 南牆內緣 (紅線底部) Y 最大值
const WALL_W = 180;   // 西牆內緣 (紅線左側) X 最小值
const WALL_E = 1100;  // 東牆內緣 (紅線右側) X 最大值
const DOOR_GAP = 70;  // 通道感應區半寬 (570 ~ 710px / 290 ~ 430px)
const DOOR_CX  = 640; // 北南通道中心 X
const DOOR_CY  = 360; // 東西通道中心 Y

window.WALL_N = WALL_N; window.WALL_S = WALL_S;
window.WALL_W = WALL_W; window.WALL_E = WALL_E;
window.DOOR_GAP = DOOR_GAP; window.DOOR_CX = DOOR_CX; window.DOOR_CY = DOOR_CY;

// 舊版相容性 WORLD_AREAS 匯出 (包含宗門與秘境)
const WORLD_AREAS = Object.values(SECT_ROOMS).concat(DUNGEON_WORLDS);
