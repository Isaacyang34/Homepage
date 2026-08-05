// ═══════════════════════════════════════════════════════════
//  UI 彈窗與選單互動模組 (UI & Modals)
// ═══════════════════════════════════════════════════════════

function openInv() { renderInv(); $('inv-overlay').classList.add('show'); }
function closeInv() { $('inv-overlay').classList.remove('show'); }

function openChar() {
  const r = REALMS[P.realmIdx];
  const charCanvas = $('char-canvas');
  if (charCanvas) {
    const sc = charCanvas.getContext('2d');
    sc.imageSmoothingEnabled = false;
    sc.clearRect(0, 0, 96, 120);
    sc.fillStyle = '#06091a'; sc.fillRect(0, 0, 96, 120);

    if (hdPlayerLoaded) {
      const fw = hdPlayerImg.width / 4;
      const fh = hdPlayerImg.height / 2;
      sc.drawImage(hdPlayerImg, 0, 0, fw, fh, 12, 10, 72, 96);
    } else {
      drawSpriteSmall(IDLE0, sc, 12, 15, 4);
    }
  }

  const st = $('char-stats');
  if (st && r) {
    st.innerHTML = '';
    const curArea = WORLD_AREAS.find(a => a.id === curAreaId);
    const rows = [
      ['境界', r.name],
      ['氣血', `${Math.ceil(P.hp)} / ${P.maxHp}`],
      ['靈力', `${Math.ceil(P.qi)} / ${P.maxQi}`],
      ['悟道', `${Math.ceil(P.exp)} / ${r.expNext}`],
      ['攻擊力', P.atk], ['防禦力', P.def],
      ['靈石', P.stones], ['擊殺', P.kills],
      ['當前地圖', curArea ? (curArea.name.split('·')[1]?.trim() || curArea.name) : '未知'],
    ];
    rows.forEach(([l, v]) => {
      const d = document.createElement('div'); d.className = 'cs-row';
      d.innerHTML = `<span class="cs-lbl">${l}</span><span class="cs-val">${v}</span>`;
      st.appendChild(d);
    });
  }
  $('char-overlay').classList.add('show');
}
function closeChar() { $('char-overlay').classList.remove('show'); }

// ─── 說明 Modal ───
function openHelp() { $('help-overlay').classList.add('show'); if ($('btn-help')) $('btn-help').classList.add('spin'); }
function closeHelp() { $('help-overlay').classList.remove('show'); if ($('btn-help')) $('btn-help').classList.remove('spin'); }

// ─── 設定 Modal ───
function openCfg() { $('cfg-overlay').classList.add('show'); if ($('btn-cfg')) $('btn-cfg').classList.add('spin'); }
function closeCfg() { $('cfg-overlay').classList.remove('show'); if ($('btn-cfg')) $('btn-cfg').classList.remove('spin'); }

// ─── 九轉煉丹房 ───
const ALCHEMY_RECIPES = [
  { id: 'alch_juqi', name: '《聚氣丹》', icon: '🔵', cost: 80, desc: '提升 +300 悟道經驗', action: () => { P.exp += 300; notify('🧪 煉製成功！使用【聚氣丹】，修為大漲 300！'); updateHUD(); } },
  { id: 'alch_ningshen', name: '《凝神丹》', icon: '🟣', cost: 200, desc: '提升 +1000 悟道經驗', action: () => { P.exp += 1000; notify('🧪 煉製成功！使用【凝神丹】，修為暴漲 1000！'); updateHUD(); } },
  { id: 'alch_zhuji', name: '《築基保底丹》', icon: '🔥', cost: 450, desc: '破境突破成功率 +15%', action: () => { P.breakBonus = (P.breakBonus || 0) + .15; notify('🧪 煉製成功！服用【築基保底丹】，突破率+15%！'); } },
  { id: 'alch_cuiling', name: '《先天淬靈洗髓丹》', icon: '✨', cost: 800, desc: '永久防禦 +15，悟道靈感大增', action: () => { P.def += 15; notify('🧪 煉製成功！【洗髓丹】發揮藥力，永久防禦力+15！'); updateHUD(); } },
];

function openAlchemy() {
  const g = $('alchemy-grid');
  if (!g) return;
  g.innerHTML = '';
  ALCHEMY_RECIPES.forEach(r => {
    const card = document.createElement('div');
    card.style.cssText = 'background:#06091a;border:1px solid var(--border);border-radius:5px;padding:8px;display:flex;align-items:center;justify-content:space-between;';
    card.innerHTML = `<div><div style="font-size:12px;color:var(--gold);font-weight:700;">${r.icon} ${r.name}</div><div style="font-size:9px;color:var(--dim);margin-top:2px;">${r.desc}</div><div style="font-size:9px;color:var(--jade);margin-top:2px;">所需靈石: 💎${r.cost}</div></div><button class="cbtn" style="padding:4px 10px;" onclick="startAlchemy('${r.id}')">開爐</button>`;
    g.appendChild(card);
  });
  $('alchemy-overlay').classList.add('show');
}
function closeAlchemy() { $('alchemy-overlay').classList.remove('show'); }
function startAlchemy(id) {
  const r = ALCHEMY_RECIPES.find(x => x.id === id);
  if (!r) return;
  if (P.stones < r.cost) { notify('❌ 靈石不足，無法開爐煉丹！'); return; }
  P.stones -= r.cost; updateHUD();
  closeAlchemy();
  notify(`🔥 已將【${r.name}】投入丹爐中，開始煉製…`);
  setTimeout(() => { r.action(); }, 1500);
}

// ─── 神兵煉器坊 ───
const FORGE_RECIPES = [
  { id: 'forge_sword', name: '🗡️ 精鋼長劍', cost: 150, atk: 15, desc: '基礎攻擊力 +15 (近身型態)', action: () => { P.atk += 15; notify('🔨 鍛造成功！裝備【精鋼長劍】，攻擊力+15！'); updateHUD(); } },
  { id: 'forge_robe', name: '🥋 青雲道袍', cost: 200, hp: 80, def: 8, desc: '最大氣血 +80，防禦力 +8', action: () => { P.maxHp += 80; P.hp += 80; P.def += 8; notify('🔨 鍛造成功！穿上【青雲道袍】，氣血+80，防禦+8！'); updateHUD(); } },
  { id: 'forge_talisman', name: '⚡ 紫電神木法杖', cost: 350, atk: 25, unlock: 'TALISMAN', desc: '解鎖【五行雷符】功法！攻擊力 +25', action: () => {
    P.atk += 25;
    if (!P.unlockedStances.includes('TALISMAN')) {
      P.unlockedStances.push('TALISMAN');
      if ($('lbl-talisman')) $('lbl-talisman').textContent = '雷符';
      notify('⚡ 鍛造神兵！成功解鎖功法【五行雷符】！');
    } else notify('🔨 鍛造成功！【紫電神木法杖】攻擊力+25！');
    updateHUD();
  }},
  { id: 'forge_frost', name: '❄️ 玄冰法珠', cost: 350, atk: 25, unlock: 'FROST', desc: '解鎖【寒冰法訣】功法！攻擊力 +25', action: () => {
    P.atk += 25;
    if (!P.unlockedStances.includes('FROST')) {
      P.unlockedStances.push('FROST');
      if ($('lbl-frost')) $('lbl-frost').textContent = '冰法';
      notify('❄️ 鍛造神兵！成功解鎖功法【寒冰法訣】！');
    } else notify('🔨 鍛造成功！【玄冰法珠】攻擊力+25！');
    updateHUD();
  }},
];

function openForge() {
  const g = $('forge-grid');
  if (!g) return;
  g.innerHTML = '';
  FORGE_RECIPES.forEach(r => {
    const card = document.createElement('div');
    card.style.cssText = 'background:#06091a;border:1px solid var(--border);border-radius:5px;padding:8px;display:flex;align-items:center;justify-content:space-between;';
    card.innerHTML = `<div><div style="font-size:12px;color:var(--gold);font-weight:700;">${r.name}</div><div style="font-size:9px;color:var(--dim);margin-top:2px;">${r.desc}</div><div style="font-size:9px;color:var(--jade);margin-top:2px;">所需靈石: 💎${r.cost}</div></div><button class="cbtn" style="padding:4px 10px;" onclick="startForging('${r.id}')">鍛造</button>`;
    g.appendChild(card);
  });
  $('forge-overlay').classList.add('show');
}
function closeForge() { $('forge-overlay').classList.remove('show'); }
function startForging(id) {
  const r = FORGE_RECIPES.find(x => x.id === id);
  if (!r) return;
  if (P.stones < r.cost) { notify('❌ 靈石不足，無法鍛造神兵！'); return; }
  P.stones -= r.cost; updateHUD();
  closeForge();
  notify(`🔨 鐵鎚揮舞，開始打磨【${r.name}】…`);
  setTimeout(() => { r.action(); }, 1500);
}

// ─── 世界地圖選單 ───
let selectedAreaId = null;
function openMap() {
  drawWorldMap();
  $('map-overlay').classList.add('show');
}
function closeMap() { $('map-overlay').classList.remove('show'); }

function drawWorldMap() {
  const wc = $('wmap-canvas');
  if (!wc) return;
  const wctx = wc.getContext('2d');
  wctx.imageSmoothingEnabled = false;
  wctx.fillStyle = '#04060e'; wctx.fillRect(0, 0, 600, 340);
  wctx.strokeStyle = 'rgba(42,56,96,.2)'; wctx.lineWidth = 1;
  for (let x = 0; x < 600; x += 30) { wctx.beginPath(); wctx.moveTo(x, 0); wctx.lineTo(x, 340); wctx.stroke(); }
  for (let y = 0; y < 340; y += 30) { wctx.beginPath(); wctx.moveTo(0, y); wctx.lineTo(600, y); wctx.stroke(); }

  const PATHS = [[0, 1], [0, 2], [0, 3], [1, 2]];
  PATHS.forEach(([a, b]) => {
    const A = WORLD_AREAS[a], B = WORLD_AREAS[b];
    const grad = wctx.createLinearGradient(A.x, A.y, B.x, B.y);
    grad.addColorStop(0, 'rgba(212,168,67,.4)'); grad.addColorStop(1, 'rgba(82,200,160,.4)');
    wctx.strokeStyle = grad; wctx.lineWidth = 2; wctx.setLineDash([4, 4]);
    wctx.beginPath(); wctx.moveTo(A.x, A.y); wctx.lineTo(B.x, B.y); wctx.stroke();
    wctx.setLineDash([]);
  });

  WORLD_AREAS.forEach((area) => {
    const isCur = area.id === curAreaId;
    const isSel = area.id === selectedAreaId;
    wctx.fillStyle = isCur ? 'rgba(0,207,255,.25)' : isSel ? 'rgba(212,168,67,.25)' : 'rgba(20,30,50,.7)';
    wctx.strokeStyle = isCur ? 'var(--blue)' : isSel ? 'var(--gold)' : '#3a4870';
    wctx.lineWidth = isCur || isSel ? 2 : 1;
    wctx.beginPath(); wctx.arc(area.x, area.y, 22, 0, Math.PI * 2); wctx.fill(); wctx.stroke();
    wctx.fillStyle = isCur ? '#00cfff' : isSel ? '#ffd700' : '#a0b0d0';
    wctx.font = 'bold 11px sans-serif'; wctx.textAlign = 'center'; wctx.textBaseline = 'middle';
    wctx.fillText(area.mapLabel, area.x, area.y);
  });
}

// ─── 對話框對話 ───
let dlgNpc = null, dlgIdx = 0, dlgActive = false;
function startDlg(npc) {
  dlgNpc = npc; dlgIdx = 0; dlgActive = true;
  if (npc.onStart) { npc.onStart(); npc.onStart = null; }
  $('dlg-port').textContent = npc.icon || '🧑';
  $('dlg-name').textContent = npc.name;
  typeDlg(npc.dlg[0]);
  $('dlg-box').style.display = 'block';
}
function typeDlg(txt) { $('dlg-txt').textContent = txt; }
function advanceDlg() {
  if (!dlgNpc) return; dlgIdx++;
  if (dlgIdx >= dlgNpc.dlg.length) {
    dlgActive = false;
    const oldNpc = dlgNpc; dlgNpc = null;
    $('dlg-box').style.display = 'none';
    if (oldNpc.onEnd) oldNpc.onEnd();
  } else typeDlg(dlgNpc.dlg[dlgIdx]);
}
