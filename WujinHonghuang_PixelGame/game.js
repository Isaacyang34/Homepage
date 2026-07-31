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

// 功法心法資料庫 (Sutras Database)
const ALL_SUTRAS = [
  // 武學心法 (五行屬性攻擊加成)
  { id: 'sutra_1', type: 'martial', name: '《金罡裂空劍》', price: 800, atk: 25, def: 0, hp: 0, expSpeed: 0, crit: 0.03, desc: '金系上古劍訣，參悟後永久提升 25 點攻擊力與 3% 會心率。' },
  { id: 'sutra_2', type: 'martial', name: '《蒼木逢春功》', price: 1200, atk: 30, def: 0, hp: 150, expSpeed: 0, crit: 0, desc: '木系逢春絕學，參悟後提升 30 點攻擊力與 150 點最大氣血。' },
  { id: 'sutra_3', type: 'martial', name: '《玄冰破浪訣》', price: 1800, atk: 35, def: 10, hp: 0, expSpeed: 0, crit: 0, desc: '水系破浪秘訣，參悟後提升 35 點攻擊力與 10 點防禦力。' },
  { id: 'sutra_4', type: 'martial', name: '《烈陽焚天槍》', price: 2500, atk: 45, def: 0, hp: 0, expSpeed: 0, crit: 0.05, desc: '火系焚天槍法，參悟後提升 45 點攻擊力與 5% 會心率。' },
  { id: 'sutra_5', type: 'martial', name: '《厚土鎮嶽印》', price: 3500, atk: 40, def: 25, hp: 0, expSpeed: 0, crit: 0, desc: '土系鎮嶽大印，參悟後提升 40 點攻擊力與 25 點防禦力。' },

  // 內功心法 (修為獲得速度 & 防禦氣血加成)
  { id: 'sutra_6', type: 'internal', name: '《太乙洗髓經》', price: 1000, atk: 0, def: 15, hp: 100, expSpeed: 0.10, crit: 0, desc: '太乙洗髓易筋，參悟後永久提升 10% 修練速度與 15 點防禦。' },
  { id: 'sutra_7', type: 'internal', name: '《紫霄神雷功》', price: 2200, atk: 0, def: 30, hp: 200, expSpeed: 0.15, crit: 0, desc: '紫霄雷霆淬體，參悟後提升 15% 修練速度與 30 點防禦。' },
  { id: 'sutra_8', type: 'internal', name: '《混沌吐納術》', price: 4500, atk: 0, def: 50, hp: 300, expSpeed: 0.20, crit: 0, desc: '混沌呼吸法，參悟後提升 20% 修練速度與 50 點防禦。' },
  { id: 'sutra_9', type: 'internal', name: '《洪荒無極心經》', price: 8888, atk: 20, def: 80, hp: 500, expSpeed: 0.30, crit: 0.05, desc: '洪荒第一無極心經，參悟後提升 30% 修練速度與全屬性爆發！' }
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
    baseExp: 25, baseCoin: 15, matDrop: 'woodMat' 
  },
  { 
    id: 1, name: "九幽寒潭", reqLevel: 10, element: 'water',
    monsters: [
      { name: "玄冰白蛇", icon: "🐍" },
      { name: "寒潭巨鱷", icon: "🐊" },
      { name: "九幽水鬼", icon: "👻" },
      { name: "深海冰水獸", icon: "🦑" }
    ],
    baseExp: 60, baseCoin: 45, matDrop: 'waterMat' 
  },
  { 
    id: 2, name: "熔岩地獄", reqLevel: 25, element: 'fire',
    monsters: [
      { name: "烈焰火狐", icon: "🦊" },
      { name: "地獄熔岩犬", icon: "🐕" },
      { name: "赤炎火魔", icon: "👹" },
      { name: "朱雀幼獸", icon: "🦅" }
    ],
    baseExp: 150, baseCoin: 110, matDrop: 'fireMat' 
  },
  { 
    id: 3, name: "崑崙金山", reqLevel: 40, element: 'gold',
    monsters: [
      { name: "白虎聖獸", icon: "🐅" },
      { name: "金甲神兵", icon: "💂" },
      { name: "金晶巨雕", icon: "🦅" },
      { name: "太乙劍靈", icon: "⚔️" }
    ],
    baseExp: 350, baseCoin: 280, matDrop: 'goldMat' 
  },
  { 
    id: 4, name: "不周天柱", reqLevel: 60, element: 'earth',
    monsters: [
      { name: "混沌麒麟", icon: "🐉" },
      { name: "息壤巨人", icon: "🗿" },
      { name: "山嶽神龜", icon: "🐢" },
      { name: "不周山靈", icon: "🧙‍♂️" }
    ],
    baseExp: 800, baseCoin: 700, matDrop: 'earthMat' 
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
    baseExp: 40, baseCoin: 30, matDrop: 'all'
  }
];

const CHAOS_TIERS = [
  { name: "1~15級 (凡階)", scale: 1.0 },
  { name: "15~30級 (靈階)", scale: 1.8 },
  { name: "30~50級 (地階)", scale: 3.0 },
  { name: "50~70級 (天階)", scale: 5.5 },
  { name: "70~100級 (聖階)", scale: 9.0 }
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
  equipped: { weapon: null, armor: null, accessory: null },
  usedCodes: [],
  pills: 0
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
  setupEventListeners();
  setupDragAndDrop();
  updateUI();
  selectDungeon(5);
  startRegenTimer();
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

  // 神秘商人 Modal
  const merchantModal = document.getElementById('merchant-modal');
  document.getElementById('btn-open-merchant').addEventListener('click', () => {
    renderMerchantShop();
    merchantModal.classList.add('show');
  });
  document.getElementById('close-merchant-modal').addEventListener('click', () => {
    merchantModal.classList.remove('show');
  });
  merchantModal.addEventListener('click', (e) => {
    if (e.target === merchantModal) merchantModal.classList.remove('show');
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

  document.getElementById('btn-forge').addEventListener('click', forgeEquipment);
  document.getElementById('btn-salvage-all').addEventListener('click', salvageCommonItems);

  // 打坐調息 / 丹藥
  document.getElementById('btn-meditate').addEventListener('click', toggleMeditate);
  document.getElementById('btn-use-pill').addEventListener('click', usePill);
  document.getElementById('btn-buy-pill').addEventListener('click', buyPill);
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
    if (rand < 5) {
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
    } else if (rand < 45) {
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
  if (DUNGEONS[idx].reqLevel > player.level) {
    addLog(`【警告】境界未達要求，無法進入 ${DUNGEONS[idx].name}！`, 'log-monster');
    return;
  }
  currentDungeonIdx = idx;
  
  document.querySelectorAll('.dungeon-card').forEach(card => {
    const cardId = parseInt(card.getAttribute('data-id'));
    card.classList.toggle('active', cardId === idx);
  });
  
  const chaosSelector = document.getElementById('chaos-level-selector');
  if (idx === 5) chaosSelector.style.display = 'flex';
  else chaosSelector.style.display = 'none';

  spawnMonster();
  addLog(`【地圖】進入 ${DUNGEONS[idx].name}，遭遇怪物 ${currentMonster.name}！`, 'log-system');
  updateUI();
}

function setChaosTier(tierIdx) {
  currentChaosTier = tierIdx;
  document.querySelectorAll('.chaos-lvl-btn').forEach(btn => {
    const tier = parseInt(btn.getAttribute('data-tier'));
    btn.classList.toggle('active', tier === tierIdx);
  });
  spawnMonster();
  addLog(`【秘境切換】將五行混沌秘境調整至【${CHAOS_TIERS[tierIdx].name}】！`, 'log-system');
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
  const monsterData = dung.monsters[Math.floor(Math.random() * dung.monsters.length)];
  
  let scale = 1 + (player.level - 1) * 0.12;
  let elem = monsterData.elem || dung.element;

  if (currentDungeonIdx === 5) {
    scale = CHAOS_TIERS[currentChaosTier].scale;
  }

  currentMonster = {
    name: monsterData.name,
    element: elem,
    icon: monsterData.icon,
    maxHp: Math.floor(120 * scale),
    hp: Math.floor(120 * scale),
    atk: Math.floor(20 * scale),
    def: Math.floor(8 * scale)
  };
  updateMonsterUI();
}

function executeBattleRound() {
  if (!currentMonster || currentMonster.hp <= 0) {
    spawnMonster();
  }

  audioSynth.sfxAttack();

  let playerDamageMult = 1.0;
  if (player.element === 'azure' || ELEMENT_COUNTER[player.element] === currentMonster.element) {
    playerDamageMult = 1.2;
    addLog(`【克制壓制】五行相克，發揮 120% 攻擊力！`, 'log-crit');
  }

  let isCrit = Math.random() < player.critRate;
  let baseDmg = Math.max(5, player.atk - Math.floor(currentMonster.def * 0.5));
  let finalDmg = Math.floor(baseDmg * playerDamageMult * (isCrit ? 2.0 : 1.0));
  
  currentMonster.hp = Math.max(0, currentMonster.hp - finalDmg);

  if (isCrit) {
    audioSynth.sfxCrit();
    addLog(`【爆發】你發動了會心一擊！對 ${currentMonster.name} 造成 ${finalDmg} 點傷害！`, 'log-crit');
  } else {
    addLog(`【攻擊】你對 ${currentMonster.name} 造成 ${finalDmg} 點傷害。`, 'log-player');
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

    let monsterDmg = Math.max(3, Math.floor((currentMonster.atk - Math.floor(player.def * 0.5)) * monsterDmgMult));
    
    player.hp = Math.max(0, player.hp - monsterDmg);
    audioSynth.sfxHit();
    addLog(`【受擊】${currentMonster.name} 對你造成 ${monsterDmg} 點傷害！`, 'log-monster');

    if (player.hp <= 0) {
      player.hp = Math.floor(player.maxHp * 0.1);
      addLog(`【重傷】你體力不支被迫撤退！氣血僅恢復 10%，建議打坐調息或服用丹藥恢復！`, 'log-monster');
      if (isAutoBattling) toggleAutoBattle();
      if (isMeditating) stopMeditate();
    }
    updateUI();
  }, 200);

  updateMonsterUI();
  updateUI();
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
  audioSynth.sfxReward();

  let baseExp = dung.baseExp;
  let baseCoin = dung.baseCoin;

  if (currentDungeonIdx === 5) {
    const scale = CHAOS_TIERS[currentChaosTier].scale;
    baseExp = Math.floor(dung.baseExp * scale);
    baseCoin = Math.floor(dung.baseCoin * scale);
  }

  // 算入天賦修速 + 心法修速
  const totalExpSpeed = calculateTotalExpSpeed();
  const expGain = Math.floor(baseExp * totalExpSpeed);
  player.exp += expGain;
  player.coins += baseCoin;
  
  let droppedMatName = "";
  if (dung.matDrop === 'all') {
    const allMats = ['goldMat', 'woodMat', 'waterMat', 'fireMat', 'earthMat'];
    const selectedMat = allMats[Math.floor(Math.random() * allMats.length)];
    player.materials[selectedMat] += 1;
    droppedMatName = { goldMat:'金精石', woodMat:'神木芯', waterMat:'玄冰髓', fireMat:'朱雀羽', earthMat:'息壤土' }[selectedMat];
  } else {
    player.materials[dung.matDrop] += 1;
    droppedMatName = { goldMat:'金精石', woodMat:'神木芯', waterMat:'玄冰髓', fireMat:'朱雀羽', earthMat:'息壤土' }[dung.matDrop];
  }

  addLog(`【大捷】擊敗 ${currentMonster.name}！修為+${expGain} (修速 ${Math.floor(totalExpSpeed*100)}%)，靈石+${baseCoin}，【${droppedMatName}】+1！`, 'log-drop');

  // 15% 機率觸發隨機機緣或神秘商人
  if (Math.random() < 0.15) {
    if (Math.random() < 0.5) {
      // 50% 隨機天降機緣
      const rewardCoins = 300 + Math.floor(Math.random() * 500);
      player.coins += rewardCoins;
      addLog(`【✨ 天降機緣】偶遇洪荒大能遺跡，獲得古仙贈禮：靈石 +${rewardCoins}！`, 'log-crit');
    } else {
      // 50% 神秘商人降臨
      document.getElementById('merchant-banner').classList.add('show');
      addLog(`【🧙‍♂️ 機緣降臨】雲遊神秘商人攜帶武學與內功心法降臨秘境！`, 'log-crit');
    }
  }

  if (player.exp >= player.maxExp) {
    levelUp();
  }

  spawnMonster();
  updateUI();
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

  ALL_SUTRAS.forEach(sutra => {
    const isBought = player.purchasedSutras.includes(sutra.id);
    const card = document.createElement('div');
    card.className = `sutra-card ${isBought ? 'sutra-purchased' : ''}`;
    card.innerHTML = `
      <div class="sutra-card-header">
        <span class="sutra-name">${sutra.name}</span>
        <span class="sutra-badge ${sutra.type === 'martial' ? 'sutra-type-martial' : 'sutra-type-internal'}">${sutra.type === 'martial' ? '五行武學' : '內功心法'}</span>
      </div>
      <div class="sutra-effect">${getSutraEffectText(sutra)}</div>
      <div class="sutra-desc">${sutra.desc}</div>
      <div style="display:flex; justify-content:space-between; align-items:center; margin-top:8px;">
        <span style="color:var(--pixel-gold); font-size:0.85rem; font-weight:bold;">💰 ${sutra.price} 靈石</span>
        <button class="pixel-btn ${isBought ? '' : 'btn-gold'}" ${isBought ? 'disabled' : ''} onclick="buySutra('${sutra.id}')">
          ${isBought ? '已參悟' : '購買參悟'}
        </button>
      </div>
    `;
    container.appendChild(card);
  });
}

// 渲染功法 Tab 頁籤
function renderSutraTab() {
  const container = document.getElementById('sutra-grid');
  if (!container) return;
  container.innerHTML = '';

  let totalAtk = 0, totalDef = 0, totalExp = 0;

  ALL_SUTRAS.forEach(sutra => {
    const isBought = player.purchasedSutras.includes(sutra.id);
    if (isBought) {
      totalAtk += sutra.atk || 0;
      totalDef += sutra.def || 0;
      totalExp += sutra.expSpeed || 0;
    }

    const card = document.createElement('div');
    card.className = `sutra-card ${isBought ? 'sutra-purchased' : ''}`;
    card.style.opacity = isBought ? '1' : '0.4';
    card.innerHTML = `
      <div class="sutra-card-header">
        <span class="sutra-name">${sutra.name}</span>
        <span class="sutra-badge ${sutra.type === 'martial' ? 'sutra-type-martial' : 'sutra-type-internal'}">${sutra.type === 'martial' ? '五行武學' : '內功心法'}</span>
      </div>
      <div class="sutra-effect">${getSutraEffectText(sutra)}</div>
      <div class="sutra-desc">${sutra.desc}</div>
      <div style="margin-top:6px; font-size:0.75rem; color:${isBought ? '#2ecc71' : '#888'}; font-weight:bold;">
        ${isBought ? '✓ 已參悟境界' : '🔒 未獲得心法'}
      </div>
    `;
    container.appendChild(card);
  });

  document.getElementById('sutra-bonus-atk').textContent = totalAtk;
  document.getElementById('sutra-bonus-def').textContent = totalDef;
  document.getElementById('sutra-bonus-exp').textContent = `${Math.floor(totalExp * 100)}%`;
}

function getSutraEffectText(sutra) {
  let parts = [];
  if (sutra.atk) parts.push(`攻 +${sutra.atk}`);
  if (sutra.def) parts.push(`防 +${sutra.def}`);
  if (sutra.hp) parts.push(`血 +${sutra.hp}`);
  if (sutra.expSpeed) parts.push(`修速 +${Math.floor(sutra.expSpeed * 100)}%`);
  if (sutra.crit) parts.push(`會心 +${Math.floor(sutra.crit * 100)}%`);
  return parts.join(' | ');
}

// 計算玩家總修練速度 (靈根修速 + 心法修速增益)
function calculateTotalExpSpeed() {
  let sutraExpSpeed = 0;
  player.purchasedSutras.forEach(id => {
    const s = ALL_SUTRAS.find(item => item.id === id);
    if (s && s.expSpeed) sutraExpSpeed += s.expSpeed;
  });
  return (player.expSpeed || 1.0) + sutraExpSpeed;
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
  addLog(`【裝備】已穿戴 ${item.name}！`, 'log-system');
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
  let count = 0;
  let matReturnCount = 0;
  const matKeys = ['goldMat', 'woodMat', 'waterMat', 'fireMat', 'earthMat'];

  player.inventory = player.inventory.filter(item => {
    if (item.quality <= 2) {
      count++;
      player.coins += item.quality * 50;
      player.materials[matKeys[Math.floor(Math.random() * matKeys.length)]] += 1;
      matReturnCount += 1;
      return false;
    }
    return true;
  });

  if (count > 0) {
    audioSynth.sfxReward();
    addLog(`【一鍵熔練】共拆解 ${count} 件普通裝備，獲得靈石與 ${matReturnCount} 個五行神材返還！`, 'log-drop');
    updateUI();
  } else {
    addLog('【熔練提示】背包中沒有凡品或良品裝備可供拆解。', 'log-system');
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

  document.getElementById('mat-gold').textContent = player.materials.goldMat;
  document.getElementById('mat-wood').textContent = player.materials.woodMat;
  document.getElementById('mat-water').textContent = player.materials.waterMat;
  document.getElementById('mat-fire').textContent = player.materials.fireMat;
  document.getElementById('mat-earth').textContent = player.materials.earthMat;

  renderEquippedSlots();
  renderInventory();
}

function updateMonsterUI() {
  if (!currentMonster) return;
  document.getElementById('monster-name').textContent = `${currentMonster.name} (${ELEMENT_NAMES[currentMonster.element]})`;
  document.getElementById('monster-icon').textContent = currentMonster.icon;
  document.getElementById('monster-hp-fill').style.width = `${Math.min(100, (currentMonster.hp / currentMonster.maxHp) * 100)}%`;
  document.getElementById('monster-hp-text').textContent = `${currentMonster.hp} / ${currentMonster.maxHp}`;
}

function renderEquippedSlots() {
  const eq = player.equipped;
  ['weapon', 'armor', 'accessory'].forEach(type => {
    const el = document.getElementById(`eq-${type}`);
    if (eq[type]) {
      el.style.borderColor = eq[type].qualityColor;
      el.innerHTML = `
        <span style="font-size:1.4rem;">${eq[type].icon}</span>
        <span style="font-size:0.65rem; color:${eq[type].qualityColor}; font-weight:bold;">${eq[type].name}</span>
      `;
      el.onclick = () => unequipItem(type);
    } else {
      el.style.borderColor = '#3d3d63';
      let title = type === 'weapon' ? '空武器' : type === 'armor' ? '空防具' : '空飾品';
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
  // 基礎回復 = 2% maxHp，打坐時 5 倍速
  let base = Math.max(4, Math.floor(player.maxHp * 0.02));
  if (isMeditating) base *= 5;
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
          regenStatus.style.color = '#3498db';
          regenStatus.textContent = `🧘 打坐調息中... +${regen} 氣血/3s（5倍恢復速度）`;
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
