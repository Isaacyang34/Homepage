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

const WORLD_AREAS = [
  {
    id: 'sect', name: '青雲宗 · 宗門大殿',
    desc: '修仙聖地，安全區域。可與長老、藥師互動，補充物資與煉丹煉器。',
    x: 300, y: 170, safe: true,
    bg: '#0c0a1a', qiDensity: 2.0, mapLabel: '宗門',
    enemies: [],
    npcs: [
      { x: 240, y: 110, name: '青雲宗主', icon: '👑', dlg: ['本座觀汝骨相清奇，日後必成大器。', '若能成功渡劫築基，本座親贈一枚築基丹！', '去靈山磨礪吧，那裡有赤焰妖狼與毒蛛，頗適合修行。'] },
      { x: 140, y: 185, name: '藥師兄', icon: '🌿', dlg: ['師弟！隨我來，這裡有九轉煉丹房！', '收集靈草與妖丹可煉製高級丹藥，助力修行！'], onEnd: () => { if (window.openAlchemy) window.openAlchemy(); } },
      { x: 360, y: 130, name: '藏劍閣長老', icon: '⚔', dlg: ['汝劍法尚可，但若想參悟高階法術，需神兵相助！', '隨本座開啟【神兵煉器坊】，鍛造雷法杖與冰法珠吧！'], onEnd: () => { if (window.openForge) window.openForge(); } },
    ],
    terrain: 'sect',
  },
  {
    id: 'mountain', name: '青雲宗 · 外門靈山',
    desc: '靈氣充沛（1.5×），有赤焰妖狼與青脊毒蛛出沒，適合初期修煉。',
    x: 155, y: 115, safe: false,
    bg: '#0b1220', qiDensity: 1.5, mapLabel: '靈山',
    enemies: [
      { id: 0, x: 310, y: 80,  hp: 60,  maxHp: 60,  name: '赤焰妖狼', aiType: 'wolf',   sprite: 'wolf',   color: '#e84040', spd: .85, atk: 8,  reward: { stones: 15, exp: 25 } },
      { id: 1, x: 150, y: 195, hp: 40,  maxHp: 40,  name: '青脊毒蛛', aiType: 'spider', sprite: 'spider', color: '#2ecc71', spd: .7,  atk: 5,  reward: { stones: 10, exp: 15 } },
      { id: 2, x: 410, y: 65,  hp: 80,  maxHp: 80,  name: '玄鐵地鼠', aiType: 'mole',   sprite: 'mole',   color: '#7f8c8d', spd: .6,  atk: 10, reward: { stones: 20, exp: 35 } },
    ],
    npcs: [{ x: 80, y: 90, name: '巡山弟子', icon: '🧑', dlg: ['師兄！前方有赤焰妖狼群，危險！', '不過妖狼的妖丹可以換靈石，值得去打。'] }],
    terrain: 'mountain',
  },
  {
    id: 'cave', name: '玄陰洞府',
    desc: '靈氣極濃（3×），有洞穴岩魔與幽影冥蝠，適合中期修煉。',
    x: 440, y: 115, safe: false,
    bg: '#060c18', qiDensity: 3.0, mapLabel: '洞府',
    enemies: [
      { id: 3, x: 220, y: 140, hp: 120, maxHp: 120, name: '洞穴岩魔', aiType: 'golem', sprite: 'golem', color: '#8e44ad', spd: .5,  atk: 18, reward: { stones: 35, exp: 50 } },
      { id: 4, x: 360, y: 90,  hp: 70,  maxHp: 70,  name: '幽影冥蝠', aiType: 'bat',   sprite: 'bat',   color: '#3498db', spd: 1.1, atk: 12, reward: { stones: 25, exp: 40 } },
    ],
    npcs: [],
    terrain: 'cave',
  },
  {
    id: 'ruins', name: '古修遺跡 · 萬魔窟',
    desc: '傳說中的古魔禁地，赤焰魔狼皇在此盤踞！擁有 3 階段狂暴招式，擊敗 100% 掉落【築基丹】！',
    x: 300, y: 270, safe: false,
    bg: '#14040a', qiDensity: 3.5, mapLabel: '遺跡Boss',
    enemies: [
      { id: 5, x: 240, y: 120, hp: 600, maxHp: 600, name: '👑 赤焰魔狼皇 · 首領', aiType: 'boss',   sprite: 'boss',   isBoss: true, color: '#ff1100', spd: 1.0, atk: 32, reward: { stones: 350, exp: 600 } },
      { id: 6, x: 140, y: 180, hp: 80,  maxHp: 80,  name: '護法妖狼',               aiType: 'wolf',   sprite: 'wolf',   color: '#e84040', spd: 1.0, atk: 12, reward: { stones: 20,  exp: 30  } },
    ],
    npcs: [],
    terrain: 'ruins',
    reqRealm: 2,
  },
];
