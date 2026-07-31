/**
 * 《無盡洪荒：五行聖境》- 遊戲核心邏輯 (Game Engine v1.2)
 */

// 8-Bit 音效合成器 (Web Audio API)
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
      // 靜默處理 Web Audio 錯誤，不阻斷遊戲邏輯
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

// 境界定義 (Realm Hierarchy)
const REALMS = [
  "練氣初期", "練氣中期", "練氣後期",
  "築基初期", "築基中期", "築基後期",
  "金丹初期", "金丹中期", "金丹後期",
  "元嬰初期", "元嬰中期", "元嬰後期",
  "化神初期", "化神中期", "化神後期",
  "返虛期", "合體期", "大乘期", "洪荒聖人"
];

// 五行克制關係：金克木、木克土、土克水、水克火、火克金
const ELEMENT_COUNTER = {
  gold: 'wood',
  wood: 'earth',
  earth: 'water',
  water: 'fire',
  fire: 'gold'
};

const ELEMENT_NAMES = {
  gold: '金系', wood: '木系', water: '水系', fire: '火系', earth: '土系', chaos: '五行混沌'
};

// 副本怪物庫設定 (Dungeons with Monster Pools)
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
    baseExp: 40, baseCoin: 30, matDrop: 'all' // 掉落全種類
  }
];

// 混沌秘境浮動等級階階
const CHAOS_TIERS = [
  { name: "1~15級 (凡階)", minLvl: 1, maxLvl: 15, scale: 1.0 },
  { name: "15~30級 (靈階)", minLvl: 15, maxLvl: 30, scale: 1.8 },
  { name: "30~50級 (地階)", minLvl: 30, maxLvl: 50, scale: 3.0 },
  { name: "50~70級 (天階)", minLvl: 50, maxLvl: 70, scale: 5.5 },
  { name: "70~100級 (聖階)", minLvl: 70, maxLvl: 100, scale: 9.0 }
];

// 品階設定
const QUALITIES = [
  { level: 1, name: "凡品", color: "#888888", multiplier: 1.0 },
  { level: 2, name: "良品", color: "#2ecc71", multiplier: 1.4 },
  { level: 3, name: "上品", color: "#3498db", multiplier: 1.9 },
  { level: 4, name: "極品", color: "#9b59b6", multiplier: 2.6 },
  { level: 5, name: "仙品", color: "#f1c40f", multiplier: 3.5 },
  { level: 6, name: "神品", color: "#e74c3c", multiplier: 5.0 }
];

// 預設玩家狀態
let player = {
  name: "洪荒修真者",
  element: "gold",
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
  
  materials: {
    goldMat: 10,
    woodMat: 10,
    waterMat: 10,
    fireMat: 10,
    earthMat: 10
  },
  
  inventory: [],
  equipped: { weapon: null, armor: null, accessory: null },
  usedCodes: []
};

// 全域狀態
let currentDungeonIdx = 5; // 預設進五行混沌秘境
let currentChaosTier = 0; // 預設 1~15級
let autoBattleInterval = null;
let isAutoBattling = false;
let currentMonster = null;
let currentForgeMode = 'single'; // single, sheng, ke

document.addEventListener('DOMContentLoaded', () => {
  loadGame();
  setupEventListeners();
  setupDragAndDrop();
  updateUI();
  selectDungeon(5);
});

function setupEventListeners() {
  // 分頁切換
  document.querySelectorAll('.tab-btn').forEach(btn => {
    btn.addEventListener('click', () => {
      document.querySelectorAll('.tab-btn').forEach(b => b.classList.remove('active'));
      document.querySelectorAll('.tab-content').forEach(c => c.classList.remove('active'));
      
      btn.classList.add('active');
      const tabId = btn.getAttribute('data-tab');
      document.getElementById(`tab-${tabId}`).classList.add('active');
      audioSynth.playTone(300, 'square', 0.05);
    });
  });

  // CRT切換
  document.getElementById('toggle-crt').addEventListener('click', () => {
    const crt = document.querySelector('.crt-overlay');
    crt.style.display = crt.style.display === 'none' ? 'block' : 'none';
  });

  // 音效切換
  document.getElementById('toggle-sound').addEventListener('click', (e) => {
    audioSynth.enabled = !audioSynth.enabled;
    e.target.textContent = audioSynth.enabled ? '🔊 音效:開' : '🔇 音效:關';
  });

  // 禮包碼按鈕
  const giftModal = document.getElementById('gift-modal');
  document.getElementById('btn-giftcode').addEventListener('click', () => giftModal.classList.add('show'));
  document.getElementById('close-gift-modal').addEventListener('click', () => giftModal.classList.remove('show'));
  giftModal.addEventListener('click', (e) => { if (e.target === giftModal) giftModal.classList.remove('show'); });
  document.getElementById('btn-claim-code').addEventListener('click', claimGiftCode);

  // 存檔與重置
  document.getElementById('btn-save').addEventListener('click', () => {
    saveGame();
    audioSynth.sfxReward();
    addLog('【系統】存檔成功！進度已儲存。', 'log-system');
  });
  document.getElementById('btn-reset').addEventListener('click', () => {
    if (confirm('確定要重置遊戲進度嗎？所有修為與裝備將清空！')) {
      localStorage.removeItem('wujin_honghuang_save');
      location.reload();
    }
  });

  // 創角選擇
  document.querySelectorAll('.class-card').forEach(card => {
    card.addEventListener('click', () => {
      document.querySelectorAll('.class-card').forEach(c => c.classList.remove('selected'));
      card.classList.add('selected');
    });
  });
  document.getElementById('btn-confirm-class').addEventListener('click', confirmCharacterClass);

  // 戰鬥控制
  document.getElementById('btn-manual-attack').addEventListener('click', executeBattleRound);
  document.getElementById('btn-toggle-auto').addEventListener('click', toggleAutoBattle);

  // 鍛造與一鍵熔練
  document.getElementById('btn-forge').addEventListener('click', forgeEquipment);
  document.getElementById('btn-salvage-all').addEventListener('click', salvageCommonItems);
}

// 選擇副本區域
function selectDungeon(idx) {
  if (DUNGEONS[idx].reqLevel > player.level) {
    addLog(`【警告】境界未達要求，無法進入 ${DUNGEONS[idx].name}！`, 'log-monster');
    return;
  }
  currentDungeonIdx = idx;
  document.querySelectorAll('.dungeon-card').forEach((card, i) => {
    card.classList.toggle('active', i === idx);
  });
  
  // 切換混沌秘境等級選單顯示
  const chaosSelector = document.getElementById('chaos-level-selector');
  if (idx === 5) chaosSelector.style.display = 'flex';
  else chaosSelector.style.display = 'none';

  spawnMonster();
  addLog(`【地圖】進入 ${DUNGEONS[idx].name}，遭遇怪物 ${currentMonster.name}！`, 'log-system');
  updateUI();
}

// 切換混沌秘境等級階段
function setChaosTier(tierIdx) {
  currentChaosTier = tierIdx;
  document.querySelectorAll('.chaos-lvl-btn').forEach((btn, i) => {
    btn.classList.toggle('active', i === tierIdx);
  });
  spawnMonster();
  addLog(`【秘境切換】將五行混沌秘境調整至【${CHAOS_TIERS[tierIdx].name}】！`, 'log-system');
  updateUI();
}

// 切換鍛造模式
function setForgeMode(mode) {
  currentForgeMode = mode;
  document.querySelectorAll('.forge-mode-btn').forEach(btn => {
    btn.classList.remove('active');
  });
  event.target.classList.add('active');

  const rateBox = document.getElementById('forge-rate-box');
  if (mode === 'single') {
    rateBox.className = 'rate-indicator rate-success';
    rateBox.textContent = '100% 成功率 | 單純屬性穩定鍛造';
  } else if (mode === 'sheng') {
    rateBox.className = 'rate-indicator rate-boost';
    rateBox.textContent = '120% 成功率 | 相生增強，極品加成！';
  } else if (mode === 'ke') {
    rateBox.className = 'rate-indicator rate-risk';
    rateBox.textContent = '65% 成功率 | 風險衰減，可【暴擊神品】！';
  }
}

// 生成怪物
function spawnMonster() {
  const dung = DUNGEONS[currentDungeonIdx];
  // 隨機從怪物庫中挑選一個怪物
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

// 回合戰鬥邏輯
function executeBattleRound() {
  if (!currentMonster || currentMonster.hp <= 0) {
    spawnMonster();
  }

  audioSynth.sfxAttack();

  // 計算克制
  let playerDamageMult = 1.0;
  if (ELEMENT_COUNTER[player.element] === currentMonster.element) {
    playerDamageMult = 1.3;
  } else if (ELEMENT_COUNTER[currentMonster.element] === player.element) {
    playerDamageMult = 0.8;
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

  // 怪物反擊
  setTimeout(() => {
    let monsterDmgMult = (ELEMENT_COUNTER[currentMonster.element] === player.element) ? 1.3 : 1.0;
    let monsterDmg = Math.max(3, Math.floor((currentMonster.atk - Math.floor(player.def * 0.5)) * monsterDmgMult));
    
    player.hp = Math.max(0, player.hp - monsterDmg);
    audioSynth.sfxHit();
    addLog(`【受擊】${currentMonster.name} 對你造成 ${monsterDmg} 點傷害！`, 'log-monster');

    if (player.hp <= 0) {
      addLog(`【重傷】你體力不支打坐冥想，恢復全滿狀態！`, 'log-monster');
      player.hp = player.maxHp;
      if (isAutoBattling) toggleAutoBattle();
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
    }
  }
}

// 擊敗怪物處理
function onMonsterDefeated() {
  const dung = DUNGEONS[currentDungeonIdx];
  audioSynth.sfxReward();

  let expGain = dung.baseExp;
  let coinGain = dung.baseCoin;

  if (currentDungeonIdx === 5) {
    const scale = CHAOS_TIERS[currentChaosTier].scale;
    expGain = Math.floor(dung.baseExp * scale);
    coinGain = Math.floor(dung.baseCoin * scale);
  }

  player.exp += expGain;
  player.coins += coinGain;
  
  // 材料掉落
  let droppedMatName = "";
  if (dung.matDrop === 'all') {
    // 混沌秘境隨機掉落全五行材料
    const allMats = ['goldMat', 'woodMat', 'waterMat', 'fireMat', 'earthMat'];
    const selectedMat = allMats[Math.floor(Math.random() * allMats.length)];
    player.materials[selectedMat] += 1;
    droppedMatName = { goldMat:'金精石', woodMat:'神木芯', waterMat:'玄冰髓', fireMat:'朱雀羽', earthMat:'息壤土' }[selectedMat];
  } else {
    player.materials[dung.matDrop] += 1;
    droppedMatName = { goldMat:'金精石', woodMat:'神木芯', waterMat:'玄冰髓', fireMat:'朱雀羽', earthMat:'息壤土' }[dung.matDrop];
  }

  addLog(`【大捷】你擊敗了 ${currentMonster.name}！獲得 修為+${expGain}，靈石+${coinGain}，【${droppedMatName}】+1！`, 'log-drop');

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

  player.maxHp += 40;
  player.hp = player.maxHp;
  player.atk += 8;
  player.def += 4;

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

// 彈性五行鍛造裝備 (單一 / 相生 / 相剋)
function forgeEquipment() {
  const m = player.materials;
  const cost = 3;

  if (currentForgeMode === 'single') {
    // 只需要單一材料 (預設金精石)
    if (m.goldMat < cost) {
      addLog(`【鍛造失敗】金精石不足！需要至少 ${cost} 個金精石。`, 'log-monster');
      return;
    }
    m.goldMat -= cost;
  } else if (currentForgeMode === 'sheng') {
    // 金 + 水 相生
    if (m.goldMat < cost || m.waterMat < cost) {
      addLog(`【鍛造失敗】相生鍛造需要 金精石與玄冰髓 各 ${cost} 個！`, 'log-monster');
      return;
    }
    m.goldMat -= cost;
    m.waterMat -= cost;
  } else if (currentForgeMode === 'ke') {
    // 金 + 木 相剋
    if (m.goldMat < cost || m.woodMat < cost) {
      addLog(`【鍛造失敗】相剋鍛造需要 金精石與神木芯 各 ${cost} 個！`, 'log-monster');
      return;
    }
    m.goldMat -= cost;
    m.woodMat -= cost;
  }

  // 鐵鎚動畫
  const anvil = document.querySelector('.forge-anvil');
  anvil.classList.add('hammer-anim');
  setTimeout(() => anvil.classList.remove('hammer-anim'), 400);
  audioSynth.sfxCraft();

  // 計算成功率與炸爐風險
  let successRate = 1.0; // 100%
  if (currentForgeMode === 'sheng') successRate = 1.2;
  if (currentForgeMode === 'ke') successRate = 0.65;

  if (Math.random() > successRate) {
    addLog(`【炸爐】屬性強烈衝突導致爆爐！材料損毀！`, 'log-monster');
    updateUI();
    return;
  }

  // 計算品質
  let qIdx = 0; // 凡品
  const rand = Math.random() * 100;

  if (currentForgeMode === 'ke') {
    // 相剋機率暴擊【神品】
    if (rand < 25) qIdx = 5;       // 神品暴擊率高達 25%!
    else if (rand < 45) qIdx = 4;  // 仙品
    else if (rand < 70) qIdx = 3;  // 極品
    else qIdx = 2;
  } else if (currentForgeMode === 'sheng') {
    // 相生加成
    if (rand < 3) qIdx = 5;       // 神品 3%
    else if (rand < 15) qIdx = 4;  // 仙品 12%
    else if (rand < 40) qIdx = 3;  // 極品 25%
    else if (rand < 75) qIdx = 2;  // 上品 35%
    else qIdx = 1;
  } else {
    // 單屬性
    if (rand < 1) qIdx = 5;
    else if (rand < 5) qIdx = 4;
    else if (rand < 15) qIdx = 3;
    else if (rand < 35) qIdx = 2;
    else if (rand < 65) qIdx = 1;
  }

  const qualityObj = QUALITIES[qIdx];
  const types = ['weapon', 'armor', 'accessory'];
  const type = types[Math.floor(Math.random() * types.length)];
  const elem = player.element;

  let namePrefix = { gold: '金煞', wood: '蒼木', water: '玄冰', fire: '赤炎', earth: '厚土' }[elem];
  let typeName = { weapon: '聖劍', armor: '寶鎧', accessory: '佩玉' }[type];
  let icon = { weapon: '🗡️', armor: '🛡️', accessory: '📿' }[type];

  let baseVal = Math.floor((15 + player.level * 3) * qualityObj.multiplier);
  let atk = type === 'weapon' ? baseVal : Math.floor(baseVal * 0.3);
  let def = type === 'armor' ? baseVal : Math.floor(baseVal * 0.3);

  const newEquip = {
    id: Date.now() + Math.random(),
    name: `${namePrefix}·${qualityObj.name}${typeName}`,
    type: type,
    element: elem,
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
    addLog(`【鍛造成功】恭喜打造出【${qualityObj.name}】級別裝備：${newEquip.name}！`, 'log-drop');
  }

  updateUI();
}

// 設定 HTML5 Drag and Drop 熔練
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
      const itemId = parseFloat(itemIdStr);
      salvageSingleItem(itemId);
    }
  });
}

// 單件裝備熔練（拖曳至熔練爐）
function salvageSingleItem(itemId) {
  const idx = player.inventory.findIndex(i => i.id === itemId);
  if (idx === -1) return;

  const item = player.inventory[idx];
  player.inventory.splice(idx, 1);

  // 計算所得靈石與材料
  const coinsGain = item.quality * 100;
  const matKeys = ['goldMat', 'woodMat', 'waterMat', 'fireMat', 'earthMat'];
  const matGainKey = matKeys[Math.floor(Math.random() * matKeys.length)];
  player.materials[matGainKey] += item.quality;
  player.coins += coinsGain;

  audioSynth.sfxReward();
  addLog(`【三昧熔練】成功將裝備【${item.name}】投入真火熔練！獲得 靈石+${coinsGain}，五行神材+${item.quality}！`, 'log-crit');
  updateUI();
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

function recalculatePlayerStats() {
  let extraAtk = 0;
  let extraDef = 0;

  Object.values(player.equipped).forEach(eq => {
    if (eq) {
      extraAtk += eq.atk || 0;
      extraDef += eq.def || 0;
    }
  });

  player.atk = (35 + (player.level - 1) * 8) + extraAtk;
  player.def = (10 + (player.level - 1) * 4) + extraDef;
}

function salvageCommonItems() {
  let count = 0;
  player.inventory = player.inventory.filter(item => {
    if (item.quality <= 2) {
      count++;
      player.coins += item.quality * 50;
      return false;
    }
    return true;
  });

  if (count > 0) {
    audioSynth.sfxReward();
    addLog(`【熔練完成】共拆解 ${count} 件普通裝備，獲得靈石換算獎勵！`, 'log-drop');
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

function confirmCharacterClass() {
  const selectedCard = document.querySelector('.class-card.selected');
  if (!selectedCard) return;

  const elem = selectedCard.getAttribute('data-class');
  player.element = elem;
  const inputName = document.getElementById('player-name-input').value.trim();
  if (inputName) player.name = inputName;

  document.getElementById('class-select-modal').classList.remove('show');
  addLog(`【踏入修途】尊者 ${player.name} 選擇了【${ELEMENT_NAMES[elem]}】進入洪荒大地上！`, 'log-crit');
  updateUI();
  saveGame();
}

function updateUI() {
  document.getElementById('ui-player-name').textContent = player.name;
  document.getElementById('ui-realm').textContent = getRealmName(player.level);
  document.getElementById('ui-level').textContent = player.level;
  document.getElementById('ui-coins').textContent = player.coins;

  const elemTag = document.getElementById('ui-element-tag');
  elemTag.textContent = ELEMENT_NAMES[player.element];
  elemTag.className = `element-tag element-${player.element}`;

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

    // 拖曳事件
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
      recalculatePlayerStats();
    } catch (e) {
      console.error("Save file load error", e);
    }
  } else {
    document.getElementById('class-select-modal').classList.add('show');
  }
}
