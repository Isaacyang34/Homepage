// ═══════════════════════════════════════════════════════════
//  玩家狀態與系統數據管理 (State Management)
// ═══════════════════════════════════════════════════════════

const P = {
  x: 240, y: 150,
  state: 'IDLE', facing: { x: 1, y: 0 },
  realmIdx: 0,
  hp: 100, maxHp: 100, qi: 0, maxQi: 100, exp: 0,
  atk: 10, def: 2, stones: 0, kills: 0,
  invincible: false, hurtFlash: 0, dashTimer: 0,
  speed: 1.8,
  stance: 'SWORD', // 'SWORD' | 'TALISMAN' | 'FROST'
  unlockedStances: ['SWORD'], // 初始只有近身短劍
  charging: false,
  chargeTime: 0,
};

let curAreaId = 'sect';
let screenShake = 0;
let nTO = null;

const INV = [];

function $(id) { return document.getElementById(id); }

function notify(msg) {
  const n = $('notif');
  if (n) {
    n.textContent = msg; n.style.display = 'block';
    clearTimeout(nTO);
    nTO = setTimeout(() => n.style.display = 'none', 2800);
  }
  // MMORPG 戰鬥與系統日誌同步
  const log = $('mmo-chat-log');
  if (log) {
    const d = document.createElement('div');
    d.style.cssText = 'line-height:1.35;font-size:9px;margin:1px 0;';
    d.textContent = msg;
    log.appendChild(d);
    if (log.children.length > 30) log.removeChild(log.firstChild);
    log.scrollTop = log.scrollHeight;
  }
}

function updateHUD() {
  const r = REALMS[P.realmIdx];
  if (!r) return;
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
  if ($('ui-kl')) $('ui-kl').textContent = P.kills;

  const curArea = WORLD_AREAS.find(a => a.id === curAreaId);
  if ($('ui-mp') && curArea) $('ui-mp').textContent = curArea.name.split('·')[1]?.trim() || curArea.name;

  // 破境按鈕檢查
  const btnBt = $('btn-bt');
  if (btnBt) {
    if (P.exp >= r.expNext && P.qi >= P.maxQi) {
      btnBt.style.display = 'block';
    } else {
      btnBt.style.display = 'none';
    }
  }
}

function addItem(id, name, icon, qty, desc) {
  const ex = INV.find(i => i.id === id);
  if (ex) ex.qty += qty; else INV.push({ id, name, icon, qty, desc });
  renderInv();
  notify(`✦ 獲得 ${icon}${name} ×${qty}`);
}

function useItem(id) {
  const it = INV.find(i => i.id === id);
  if (!it || it.qty <= 0) return;
  if (id === 'pill_hp') { P.hp = Math.min(P.maxHp, P.hp + 60); notify('💊 聚元丹：恢復 60 氣血'); }
  else if (id === 'pill_qi') { P.qi = Math.min(P.maxQi, P.qi + P.maxQi * .3); notify('💫 聚靈丹：恢復 30% 靈力'); }
  else if (id === 'pill_break') { P.breakBonus = (P.breakBonus || 0) + .15; notify('🔥 築基丹：破境成功率+15%'); }
  it.qty--; if (it.qty <= 0) INV.splice(INV.indexOf(it), 1);
  renderInv(); updateHUD();
}

function renderInv() {
  const g = $('inv-grid');
  if (!g) return;
  g.innerHTML = '';
  INV.forEach(it => {
    const s = document.createElement('div'); s.className = 'islot'; s.title = it.desc;
    s.innerHTML = `${it.icon}<div class="inm">${it.name}</div><span class="iqt">×${it.qty}</span>`;
    s.onclick = () => useItem(it.id); g.appendChild(s);
  });
  for (let i = INV.length; i < 10; i++) {
    const s = document.createElement('div'); s.className = 'islot empty'; g.appendChild(s);
  }
}
