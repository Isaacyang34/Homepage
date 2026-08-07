// ═══════════════════════════════════════════════════════════
//  玩家狀態與系統數據管理 (State Management)
//  【修仙地牢、五行功法熟練度與武器相生面板運算】
// ═══════════════════════════════════════════════════════════

// 🚌 全域事件總線 (EventBus) - 採用發佈/訂閱模式實現 UI、玩家與 AI 完全解耦
const EventBus = {
  listeners: {},
  on(event, callback) {
    if (!this.listeners[event]) this.listeners[event] = [];
    this.listeners[event].push(callback);
  },
  off(event, callback) {
    if (!this.listeners[event]) return;
    this.listeners[event] = this.listeners[event].filter(cb => cb !== callback);
  },
  emit(event, ...args) {
    if (!this.listeners[event]) return;
    this.listeners[event].forEach(cb => {
      try { cb(...args); } catch(e) { console.error(`[EventBus] Event '${event}' handler error:`, e); }
    });
  }
};
window.EventBus = EventBus;

const P = {
  x: 640, y: 480,
  state: 'IDLE', facing: { x: 1, y: 0 },
  realmIdx: 0,
  hp: 100, maxHp: 100, qi: 0, maxQi: 100, exp: 0,
  atk: 10, def: 2, stones: 0, kills: 0,
  invincible: false, hurtFlash: 0, dashTimer: 0,
  speed: 4.8,
  charging: false,
  chargeTime: 0,

  // 🏰 地牢與關卡狀態
  worldIdx: 0,           // 5 大秘境索引 (0: 靈山, 1: 洞府, 2: 遺跡, 3: 萬仙, 4: 修羅)
  currentFloor: 1,       // 5 階關卡 (Floor 1 ~ 5)
  dungeonFloorData: null, // 當前層生成式房間拓撲圖與連線

  // ⚔️ 裝備武器 (預設近戰劍 1 格)
  weapon: {
    name: '基礎精鋼劍',
    type: 'SWORD',     // 'SWORD' (近戰1格) | 'SPEAR' (貫穿3格) | 'DART' (遠程10格)
    elem: 'GOLD',      // 'GOLD' | 'WOOD' | 'WATER' | 'FIRE' | 'EARTH'
    atk: 15,
    desc: '標準近戰劍，攻擊距離 1 格。'
  },

  // 📜 已學習功法與熟練度
  learnedSutras: [
    { id: 'sutra_body_1', level: 1, masteryExp: 0, maxMastery: 100 }, // 《金剛不壞體》
    { id: 'sutra_spell_gold', level: 1, masteryExp: 0, maxMastery: 100 }, // 《庚金劍氣》
  ],

  // 🧘 當前裝備/運轉的功法 (最多可同時裝備 2 門功法觸發相生)
  equippedSutraIds: ['sutra_body_1', 'sutra_spell_gold'],
};

let curAreaId = 'sect_main';
let screenShake = 0;
let nTO = null;

window.curAreaId = curAreaId;
window.screenShake = screenShake;

const INV = [];

function $(id) { return document.getElementById(id); }

let logCnt = 0;
const MAX_LOG = 50; // 最多保留 50 則訊息

function notify(msg) {
  // 徹底停用畫面中央浮現提示 (依據使用者指示完全移除中央 pop-up 文字)
  const n = $('notif');
  if (n) {
    n.style.display = 'none';
  }

  // 右側滾動訊息紀錄盒
  const logBox = $('game-log');
  if (logBox) {
    const now = new Date();
    const ts = `${String(now.getHours()).padStart(2,'0')}:${String(now.getMinutes()).padStart(2,'0')}:${String(now.getSeconds()).padStart(2,'0')}`;
    const entry = document.createElement('div');
    entry.className = 'log-entry';
    entry.innerHTML = `<span class="log-ts">${ts}</span><span class="log-msg">${msg}</span>`;
    logBox.appendChild(entry);

    // 超過上限時移除最舊一則
    logCnt++;
    if (logCnt > MAX_LOG) {
      const first = logBox.querySelector('.log-entry');
      if (first) logBox.removeChild(first);
      logCnt--;
    }

    // 自動捲到最新
    logBox.scrollTop = logBox.scrollHeight;
  }
}

// 🧮 功法熟練度提升與自動突破 (Mastery System)
function trainSutraMastery(sutraId, expGained = 15) {
  const s = P.learnedSutras.find(x => x.id === sutraId);
  if (!s) return;
  s.masteryExp += expGained;
  if (s.masteryExp >= s.maxMastery && s.level < 5) {
    s.level++;
    s.masteryExp = 0;
    s.maxMastery = Math.floor(s.maxMastery * 1.8);
    const meta = (typeof FIVE_ELEMENT_SUTRAS !== 'undefined') ? FIVE_ELEMENT_SUTRAS.find(x => x.id === sutraId) : null;
    notify(`✨ 功法突破！【${meta ? meta.name : sutraId}】突破至第 ${s.level} 重大成！`);
    updateHUD();
  }
}

// ☯️ 計算五行相生與同屬共鳴加成 (Five-Element Calculation)
function getSynergyBonus() {
  let atkBonus = 0, defBonus = 0, hpBonus = 0, qiBonus = 0;
  let activeSynergyName = '無相生加乘';
  let activeSynergyDesc = '同時裝備相生五行功法（木生火、火生土、土生金、金生水、水生木）可觸發相生暴擊！';

  if (typeof FIVE_ELEMENT_SUTRAS === 'undefined') {
    return { atkBonus: 0, defBonus: 0, hpBonus: 0, qiBonus: 0, activeSynergyName, activeSynergyDesc };
  }

  const sutras = P.equippedSutraIds
    .map(id => FIVE_ELEMENT_SUTRAS.find(s => s.id === id))
    .filter(Boolean);

  // 1. 計算功法基礎屬性加成 (包含熟練度倍率)
  sutras.forEach(s => {
    const userS = P.learnedSutras.find(x => x.id === s.id);
    const mult = userS ? (1 + (userS.level - 1) * 0.3) : 1.0;
    if (s.stats.atk) atkBonus += Math.ceil(s.stats.atk * mult);
    if (s.stats.def) defBonus += Math.ceil(s.stats.def * mult);
    if (s.stats.maxHp) hpBonus += Math.ceil(s.stats.maxHp * mult);
    if (s.stats.maxQi) qiBonus += Math.ceil(s.stats.maxQi * mult);
  });

  // 2. 檢測兩門裝備功法之五行相生關係 (Mutual Generation)
  if (sutras.length >= 2 && typeof FIVE_ELEMENT_SYNERGY !== 'undefined') {
    const e1 = sutras[0].elem, e2 = sutras[1].elem;
    const syn1 = FIVE_ELEMENT_SYNERGY[e1];
    const syn2 = FIVE_ELEMENT_SYNERGY[e2];

    if (syn1 && syn1.generates === e2) {
      activeSynergyName = syn1.name;
      activeSynergyDesc = syn1.desc;
      atkBonus += 25; defBonus += 15;
    } else if (syn2 && syn2.generates === e1) {
      activeSynergyName = syn2.name;
      activeSynergyDesc = syn2.desc;
      atkBonus += 25; defBonus += 15;
    }
  }

  // 3. 檢測武器與功法之同屬共鳴 / 相生搭配 (Weapon Element Synergy)
  if (P.weapon && sutras.length > 0) {
    const wElem = P.weapon.elem;
    const hasSameElem = sutras.some(s => s.elem === wElem);
    if (hasSameElem) {
      atkBonus += Math.ceil(P.weapon.atk * 0.25); // 同屬共鳴面格 +25%
    }
  }

  return { atkBonus, defBonus, hpBonus, qiBonus, activeSynergyName, activeSynergyDesc };
}

// 刷新 HUD 與境界面板
function updateHUD() {
  if (typeof REALMS === 'undefined') return;
  const r = REALMS[P.realmIdx];
  if (!r) return;

  const syn = getSynergyBonus();
  P.maxHp = r.maxHp + syn.hpBonus;
  P.maxQi = r.maxQi + syn.qiBonus;
  P.atk = r.atk + (P.weapon ? P.weapon.atk : 10) + syn.atkBonus;
  P.def = r.def + syn.defBonus;

  const hpPct = Math.max(0, Math.min(100, (P.hp / P.maxHp) * 100));
  const qiPct = Math.max(0, Math.min(100, (P.qi / P.maxQi) * 100));
  const expPct = Math.max(0, Math.min(100, (P.exp / r.expNext) * 100));

  if ($('ui-realm')) $('ui-realm').textContent = r.name;
  if ($('ui-hp')) $('ui-hp').style.width = hpPct + '%';
  if ($('ui-qi')) $('ui-qi').style.width = qiPct + '%';
  if ($('ui-exp')) $('ui-exp').style.width = expPct + '%';
  if ($('hp-v')) $('hp-v').textContent = `${Math.ceil(P.hp)} / ${P.maxHp}`;
  if ($('qi-v')) $('qi-v').textContent = `${Math.ceil(P.qi)} / ${P.maxQi}`;
  if ($('exp-v')) $('exp-v').textContent = `${Math.ceil(P.exp)} / ${r.expNext}`;
  if ($('ui-st')) $('ui-st').textContent = P.stones;

  // 🚌 發射 EventBus 訊號 (發佈/訂閱解耦)
  EventBus.emit('player_health_changed', P.hp, P.maxHp);
  EventBus.emit('player_qi_changed', P.qi, P.maxQi);
  EventBus.emit('hud_updated', P);
}

// 掛載至全域 window 物件
window.P = P;
window.curAreaId = curAreaId;
window.updateHUD = updateHUD;
window.getSynergyBonus = getSynergyBonus;
window.trainSutraMastery = trainSutraMastery;
window.notify = notify;
window.$ = $;
