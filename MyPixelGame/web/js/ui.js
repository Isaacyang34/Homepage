// ═══════════════════════════════════════════════════════════
//  UI 彈窗與選單互動模組 (UI & Modals)
//  【地圖未探索隱藏、1~8號道具欄、右側訊息紀錄面板】
// ═══════════════════════════════════════════════════════════

// ────────────────────────────────────────────────────────
// 🗒 右側訊息紀錄面板 (Game Log Panel) 動態注入
//    位於右下角，緊貼 #hud-sys 系統按鈕上方
// ────────────────────────────────────────────────────────
(function injectGameLog() {
  // 若已存在則跳過
  if (document.getElementById('game-log-panel')) return;

  // ── 注入 CSS ──
  const style = document.createElement('style');
  style.textContent = `
    #game-log-panel {
      position: fixed;
      right: 14px;
      bottom: 170px;
      width: 240px;
      max-height: 220px;
      background: rgba(4,6,16,0.88);
      border: 1px solid rgba(180,150,70,0.25);
      border-left: 3px solid rgba(180,150,70,0.5);
      border-radius: 6px 0 0 6px;
      overflow: hidden;
      display: flex;
      flex-direction: column;
      z-index: 21;
      pointer-events: auto;
      backdrop-filter: blur(5px);
    }
    #game-log-hdr {
      padding: 5px 10px;
      font-size: 9px;
      color: rgba(200,168,75,0.7);
      letter-spacing: 1.5px;
      font-weight: 700;
      border-bottom: 1px solid rgba(180,150,70,0.15);
      background: rgba(0,0,0,0.3);
      display: flex;
      align-items: center;
      justify-content: space-between;
      flex-shrink: 0;
    }
    #game-log-hdr button {
      background: none;
      border: none;
      color: rgba(107,122,144,0.7);
      font-size: 10px;
      cursor: pointer;
      padding: 0 2px;
      line-height: 1;
    }
    #game-log-hdr button:hover { color: rgba(200,168,75,0.9); }
    #game-log {
      overflow-y: auto;
      flex: 1;
      padding: 4px 0;
      scrollbar-width: thin;
      scrollbar-color: rgba(180,150,70,0.3) transparent;
    }
    #game-log::-webkit-scrollbar { width: 3px; }
    #game-log::-webkit-scrollbar-thumb { background: rgba(180,150,70,0.3); border-radius: 2px; }
    .log-entry {
      display: flex;
      gap: 6px;
      padding: 2px 10px;
      font-size: 10px;
      line-height: 1.5;
      border-bottom: 1px solid rgba(60,70,100,0.2);
      animation: logIn 0.2s ease;
    }
    .log-entry:last-child { border-bottom: none; }
    @keyframes logIn { from { opacity: 0; transform: translateX(8px); } to { opacity: 1; } }
    .log-ts {
      color: rgba(107,122,144,0.6);
      font-size: 8px;
      flex-shrink: 0;
      padding-top: 1px;
      font-variant-numeric: tabular-nums;
    }
    .log-msg {
      color: rgba(216,223,232,0.9);
      word-break: break-all;
    }
    /* 讓 #notif 縮小至頂部，僅作緊急大字警示 */
    #notif {
      top: 12% !important;
      font-size: 14px !important;
      text-shadow: 0 0 16px rgba(200,168,75,0.9), 0 0 4px #000 !important;
    }
  `;
  document.head.appendChild(style);

  // ── 注入 DOM ──
  const panel = document.createElement('div');
  panel.id = 'game-log-panel';
  panel.innerHTML = `
    <div id="game-log-hdr">
      <span>📜 訊息紀錄</span>
      <button onclick="document.getElementById('game-log').innerHTML='';logCnt=0;" title="清除紀錄">✕</button>
    </div>
    <div id="game-log"></div>
  `;

  // 插入至 #hud 或 body
  const hud = document.getElementById('hud');
  if (hud) hud.appendChild(panel);
  else document.body.appendChild(panel);
})();

function openInv() { renderInv(); $('inv-overlay').classList.add('show'); }
function closeInv() { $('inv-overlay').classList.remove('show'); }

// ⌨️ 一鍵關閉所有 Modal 彈窗 (按下 ESC 鍵觸發)
function closeAllModals() {
  closeInv();
  closeChar();
  closeHelp();
  closeCfg();
  closeAlchemy();
  closeForge();
  closeMap();
  if (typeof closeMarketUI === 'function') closeMarketUI();

  // 關閉對話框
  const dlgBox = document.getElementById('dlg-box');
  if (dlgBox) dlgBox.style.display = 'none';
  dlgActive = false;
}
window.closeAllModals = closeAllModals;

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
    const syn = getSynergyBonus();
    const rows = [
      ['境界', r.name],
      ['氣血', `${Math.ceil(P.hp)} / ${P.maxHp}`],
      ['靈力', `${Math.ceil(P.qi)} / ${P.maxQi}`],
      ['悟道', `${Math.ceil(P.exp)} / ${r.expNext}`],
      ['攻擊力', `${P.atk} (基礎:${r.atk} + 裝備:${P.weapon ? P.weapon.atk : 10} + 相生:${syn.atkBonus})`],
      ['防禦力', `${P.def} (基礎:${r.def} + 相生:${syn.defBonus})`],
      ['當前裝備武器', `${P.weapon ? P.weapon.name : '精鋼短劍'} [${WEAPON_TYPES[P.weapon.type].name}]`],
      ['五行相生狀態', syn.activeSynergyName],
      ['靈石', P.stones], ['擊殺數', P.kills],
    ];
    rows.forEach(([l, v]) => {
      const d = document.createElement('div'); d.className = 'cs-row';
      d.innerHTML = `<span class="cs-lbl">${l}</span><span class="cs-val" style="font-size:11px;">${v}</span>`;
      st.appendChild(d);
    });
  }
  $('char-overlay').classList.add('show');
}
function closeChar() { $('char-overlay').classList.remove('show'); }

// ─── 說明與設定 Modal ───
function openHelp() { $('help-overlay').classList.add('show'); if ($('btn-help')) $('btn-help').classList.add('spin'); }
function closeHelp() { $('help-overlay').classList.remove('show'); if ($('btn-help')) $('btn-help').classList.remove('spin'); }
function openCfg() { $('cfg-overlay').classList.add('show'); if ($('btn-cfg')) $('btn-cfg').classList.add('spin'); }
function closeCfg() { $('cfg-overlay').classList.remove('show'); if ($('btn-cfg')) $('btn-cfg').classList.remove('spin'); }

// 🛍️ 仙緣集市材料與靈草買賣系統
const MARKET_ITEMS = [
  { id: 'mat_lingzhi', name: '千年靈芝', icon: '🍄', buyCost: 60, sellReward: 30, desc: '頂級煉丹靈草，增加 +150 悟道經驗' },
  { id: 'mat_chiyan', name: '赤炎花', icon: '🌺', buyCost: 50, sellReward: 25, desc: '火屬靈草，提升突破成功率' },
  { id: 'mat_xuantie', name: '玄鐵精石', icon: '🪨', buyCost: 80, sellReward: 40, desc: '堅硬煉器礦石，可用於鍛造神兵' },
  { id: 'mat_zitong', name: '紫銅金沙', icon: '✨', buyCost: 100, sellReward: 50, desc: '稀有五行金屬，提升武器附加威力' },
];

function openMarketUI() {
  let mOverlay = document.getElementById('market-overlay');
  if (!mOverlay) {
    mOverlay = document.createElement('div');
    mOverlay.id = 'market-overlay';
    mOverlay.className = 'overlay';
    document.body.appendChild(mOverlay);
  }

  mOverlay.innerHTML = `
    <div class="modal" style="width:480px;">
      <div class="modal-hdr"><div class="modal-title">🏪 仙緣集市 · 材料買賣舖</div><button class="close-x" onclick="closeMarketUI()">✕</button></div>
      <div style="padding:14px 18px;">
        <div style="font-size:12px;color:var(--gold);margin-bottom:10px;">💎 當前靈石: <span id="mkt-stones">${P.stones}</span></div>
        <div id="market-grid" style="display:grid;grid-template-columns:1fr 1fr;gap:8px;max-height:280px;overflow-y:auto;"></div>
      </div>
    </div>
  `;

  const g = document.getElementById('market-grid');
  if (g) {
    MARKET_ITEMS.forEach(item => {
      const card = document.createElement('div');
      card.style.cssText = 'background:#06091a;border:1px solid var(--border);border-radius:5px;padding:8px;display:flex;flex-direction:column;justify-content:space-between;gap:4px;';
      card.innerHTML = `
        <div style="font-size:12px;color:var(--gold);font-weight:700;">${item.icon} ${item.name}</div>
        <div style="font-size:9px;color:var(--dim);">${item.desc}</div>
        <div style="font-size:9px;color:var(--jade);margin-top:2px;">買價: 💎${item.buyCost} | 賣價: 💎${item.sellReward}</div>
        <div style="display:flex;gap:4px;margin-top:4px;">
          <button class="cbtn" style="flex:1;padding:3px;font-size:10px;" onclick="buyMarketItem('${item.id}')">購買</button>
          <button class="cbtn" style="flex:1;padding:3px;font-size:10px;background:#334155;" onclick="sellMarketItem('${item.id}')">出售</button>
        </div>
      `;
      g.appendChild(card);
    });
  }

  mOverlay.classList.add('show');
}

function closeMarketUI() {
  const mOverlay = document.getElementById('market-overlay');
  if (mOverlay) mOverlay.classList.remove('show');
}

function buyMarketItem(id) {
  const item = MARKET_ITEMS.find(x => x.id === id);
  if (!item) return;
  if (P.stones < item.buyCost) { notify('❌ 靈石不足，無法購買！'); return; }
  P.stones -= item.buyCost;
  P.exp += 150;
  if (typeof updateHUD === 'function') updateHUD();
  const stEl = document.getElementById('mkt-stones'); if (stEl) stEl.textContent = P.stones;
  notify(`🛍️ 成功購買【${item.name}】！修為增長 +150！`);
}

function sellMarketItem(id) {
  const item = MARKET_ITEMS.find(x => x.id === id);
  if (!item) return;
  P.stones += item.sellReward;
  if (typeof updateHUD === 'function') updateHUD();
  const stEl = document.getElementById('mkt-stones'); if (stEl) stEl.textContent = P.stones;
  notify(`💰 成功出售【${item.name}】！獲得靈石 +${item.sellReward}！`);
}

window.openMarketUI = openMarketUI;
window.closeMarketUI = closeMarketUI;
window.buyMarketItem = buyMarketItem;
window.sellMarketItem = sellMarketItem;

// 📜 功法修煉與五行相生 UI 彈窗
function openSutraUI() {
  const syn = getSynergyBonus();
  notify(`📜 功法修煉：【${syn.activeSynergyName}】！${syn.activeSynergyDesc}`);
  openChar();
}

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

// 🔨 神兵煉器坊 (五行神兵與三類武器)
function openForge() {
  const g = $('forge-grid');
  if (!g) return;
  g.innerHTML = '';
  FORGE_RECIPES_V2.forEach(r => {
    const wInfo = WEAPON_TYPES[r.type];
    const card = document.createElement('div');
    card.style.cssText = 'background:#06091a;border:1px solid var(--border);border-radius:5px;padding:8px;display:flex;align-items:center;justify-content:space-between;';
    card.innerHTML = `<div><div style="font-size:12px;color:var(--gold);font-weight:700;">${r.name} [${wInfo.name}]</div><div style="font-size:9px;color:var(--dim);margin-top:2px;">${r.desc}</div><div style="font-size:9px;color:var(--jade);margin-top:2px;">所需靈石: 💎${r.cost}</div></div><button class="cbtn" style="padding:4px 10px;" onclick="startForging('${r.id}')">打造裝備</button>`;
    g.appendChild(card);
  });
  $('forge-overlay').classList.add('show');
}
function closeForge() { $('forge-overlay').classList.remove('show'); }
function startForging(id) {
  const r = FORGE_RECIPES_V2.find(x => x.id === id);
  if (!r) return;
  if (P.stones < r.cost) { notify('❌ 靈石不足，無法鍛造神兵！'); return; }
  P.stones -= r.cost;
  P.weapon = { name: r.name, type: r.type, elem: r.elem, atk: r.atk, desc: r.desc };
  updateHUD();
  closeForge();
  notify(`🔨 打造完成！成功裝備【${r.name}】！攻擊範圍擴展至 ${WEAPON_TYPES[r.type].name}！`);
}

// ─── 世界地圖選單 (未探索區域隱藏迷霧機制) ───
let selectedAreaId = null;

function initMapEvents() {
  const wc = $('wmap-canvas');
  if (!wc || wc._bound) return;
  wc._bound = true;
  wc.addEventListener('click', (e) => {
    const rect = wc.getBoundingClientRect();
    const scaleX = wc.width / rect.width;
    const scaleY = wc.height / rect.height;
    const cx = (e.clientX - rect.left) * scaleX;
    const cy = (e.clientY - rect.top) * scaleY;

    // 只有已解鎖/已探索地區能被點擊傳送
    const unlockedWorlds = DUNGEON_WORLDS.filter((a, idx) => idx <= P.worldIdx);
    const clickedArea = unlockedWorlds.find((a, idx) => Math.hypot(cx - (120 + idx * 110), cy - 170) <= 40);
    if (!clickedArea) return;

    P.worldIdx = DUNGEON_WORLDS.indexOf(clickedArea);
    P.currentFloor = 1;
    curAreaId = 'sect_gate';
    P.x = 640; P.y = 480;
    closeMap();
    updateHUD();
    notify('🌀 御劍傳送！已選擇秘境目標【' + clickedArea.name + '】！');
  });
}

function openMap() {
  initMapEvents();
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

  // 僅繪製已探索/解鎖的秘境地圖節點
  DUNGEON_WORLDS.forEach((area, idx) => {
    const ax = 120 + idx * 110, ay = 170;
    const isUnlocked = idx <= P.worldIdx;

    if (isUnlocked) {
      const isCur = idx === P.worldIdx;
      wctx.fillStyle = isCur ? 'rgba(0,207,255,.25)' : 'rgba(20,30,50,.7)';
      wctx.strokeStyle = isCur ? 'var(--blue)' : '#3a4870';
      wctx.lineWidth = isCur ? 2 : 1;
      wctx.beginPath(); wctx.arc(ax, ay, 22, 0, Math.PI * 2); wctx.fill(); wctx.stroke();
      wctx.fillStyle = isCur ? '#00cfff' : '#a0b0d0';
      wctx.font = 'bold 11px sans-serif'; wctx.textAlign = 'center'; wctx.textBaseline = 'middle';
      wctx.fillText(area.name.split('·')[1]?.trim() || area.name, ax, ay);
    }
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

// 🎨 動態 UI 排版重組 (徹底刪除煉丹煉器進度文字與右下角對應按鈕)
function applyUIRelayout() {
  try {
    // 1. 徹底刪除右上角煉丹與煉器進度文字面板
    const roStatus = document.getElementById('ro-status-panel');
    if (roStatus) roStatus.remove();

    // 2. 道具快捷欄只留 1 ~ 8 格 (移除第 9 格)
    const hotbarItems = document.getElementById('hotbar-items');
    if (hotbarItems) {
      const slot9 = hotbarItems.querySelector('[data-slot="9"]');
      if (slot9) slot9.remove();
    }

    const hotbarContainer = document.getElementById('hud-hotbar');
    if (hotbarContainer) {
      hotbarContainer.style.cssText = 'position:fixed; left:50%; transform:translateX(-50%); bottom:18px; top:auto; z-index:100; pointer-events:auto; margin:0;';
    }

    // 3. 左側氣血/靈力/經驗條靠左下緣對齊 (Bottom-Left Aligned)
    const hudBars = document.querySelector('.hud-bars') || document.getElementById('hud-bars');
    if (hudBars) {
      hudBars.style.cssText = 'position:fixed; left:18px; bottom:18px; top:auto; display:flex; flex-direction:column; align-items:flex-start; gap:5px; z-index:100; pointer-events:auto; margin:0;';
    }

    // 4. 右上角羅盤小地圖與靈石
    const minimap = document.getElementById('hud-minimap');
    if (minimap) {
      minimap.style.cssText = 'position:fixed; right:18px; top:18px; z-index:99; margin:0;';
    }

    const hudStones = document.getElementById('hud-stones');
    if (hudStones) {
      hudStones.style.cssText = 'position:fixed; right:18px; top:118px; background:rgba(6,9,26,0.85); border:1px solid rgba(212,168,67,0.4); border-radius:12px; padding:4px 10px; font-size:12px; color:#ffd700; z-index:99; margin:0;';
    }

    // 5. 徹底刪除右下角的煉丹 (🧪) 與 煉器 (🔨) 按鈕，只保留剩餘按鈕 (🎒 儲物袋, 📋 人物, 🗺 地圖, ❓ 說明)
    const btnBox = document.querySelector('.hud-btns') || document.getElementById('hud-btns');
    if (btnBox) {
      const btns = btnBox.querySelectorAll('button, .hud-btn, .sys-btn');
      btns.forEach(b => {
        const txt = b.textContent || b.title || '';
        const onclickAttr = b.getAttribute('onclick') || '';
        if (txt.includes('🧪') || txt.includes('🔨') || onclickAttr.includes('openAlchemy') || onclickAttr.includes('openForge')) {
          b.remove();
        } else {
          b.style.cssText = 'width:36px; height:36px; min-width:36px; min-height:36px; border-radius:50%; display:flex; align-items:center; justify-content:center; padding:0; font-size:15px; background:#0c1424; border:1.5px solid var(--border); box-shadow:0 3px 8px rgba(0,0,0,0.6); cursor:pointer; margin:0;';
        }
      });
      btnBox.style.cssText = 'position:fixed; right:18px; bottom:18px; top:auto; display:flex; flex-direction:column-reverse; gap:6px; align-items:center; z-index:101; pointer-events:auto; margin:0; padding:0;';
    }

    // 6. 清理原父層系統容器干擾
    const hudSys = document.getElementById('hud-sys');
    if (hudSys) {
      hudSys.style.cssText = 'position:static; background:none; border:none; padding:0; margin:0;';
    }
  } catch(e) {}
}

if (document.readyState === 'loading') {
  window.addEventListener('DOMContentLoaded', applyUIRelayout);
} else {
  applyUIRelayout();
}
