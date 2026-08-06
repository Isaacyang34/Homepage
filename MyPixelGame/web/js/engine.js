// ═══════════════════════════════════════════════════════════
//  遊戲核心引擎、渲染器與動態全螢幕滿板自適應 (Engine & Full-Viewport Scaler)
// ═══════════════════════════════════════════════════════════

let canvas, ctx;
let lastT = 0, T = 0;
let SQS = [], PARTS = [], FLOATS = [], LGHTS = [];

// 動態滿板縮放計算 (Auto-Scale to fill screen nicely)
function autoScaleViewport() {
  const wrap = document.getElementById('wrap');
  if (!wrap) return;
  const vw = window.innerWidth;
  const vh = window.innerHeight;
  // 計算滿板最佳比例 (保留 480x270 像素點陣解析度比 16:9)
  const scaleX = vw / 480;
  const scaleY = vh / 270;
  const fitScale = Math.min(scaleX, scaleY) * 0.95; // 95% 全螢幕滿板
  wrap.style.transform = `scale(${Math.max(1.0, fitScale)})`;
}

window.addEventListener('resize', autoScaleViewport);

function initEngine() {
  canvas = document.getElementById('gc');
  if (canvas) ctx = canvas.getContext('2d');
  window.ctx = ctx;
  autoScaleViewport();
}

// ───── 招式與特效物理 ─────
function spark(x, y, col = '#00cfff', num = 6, spd = 2, maxLife = 20, sz = 2) {
  for (let i = 0; i < num; i++) {
    const a = Math.random() * Math.PI * 2;
    const s = Math.random() * spd + .5;
    PARTS.push({ x, y, vx: Math.cos(a) * s, vy: Math.sin(a) * s, col, life: maxLife, ml: maxLife, sz });
  }
}

function floatTxt(x, y, txt, col = '#fff') {
  FLOATS.push({ x, y, txt, col, life: 40, vy: -0.7 });
}

function spiralQi(x, y) {
  for (let i = 0; i < 2; i++) {
    const a = Math.random() * Math.PI * 2;
    const r = Math.random() * 26 + 10;
    PARTS.push({ x: x + Math.cos(a) * r, y: y + Math.sin(a) * r, tx: x, ty: y, qi: true, col: i % 2 ? '#00cfff' : '#b86fff', life: 25, ml: 25, sz: 2 });
  }
}

function setStance(st) {
  if (!P.unlockedStances.includes(st)) {
    const msg = st === 'TALISMAN' ? '🔒 【五行雷符】未解鎖！需要晉升【練氣二重】或鍛造【紫電神木法杖】' : '🔒 【寒冰法訣】未解鎖！需要晉升【練氣三重】或鍛造【玄冰法珠】';
    notify(msg);
    return;
  }
  P.stance = st;
  document.querySelectorAll('#hotbar .hslot').forEach(s => s.classList.remove('active-slot'));
  const m = { SWORD: ['st-sword', '🗡️ 近身短劍 (劍氣與萬劍歸宗)'], TALISMAN: ['st-talisman', '⚡ 五行雷符 (神雷與九天雷陣)'], FROST: ['st-frost', '❄️ 寒冰法訣 (冰錐與冰封萬里)'] };
  if (m[st]) {
    const el = document.getElementById(m[st][0]);
    if (el) el.classList.add('active-slot');
    notify(m[st][1]);
  }
}

function quickPill(type = 'pill_hp') {
  useItem(type);
}

function triggerAttack(chargeLevel) {
  if (P.state === 'DIE') return;
  if (P.state === 'MEDITATE') P.state = 'IDLE';
  const fx = P.facing.x || (P.facing.y === 0 ? 1 : 0), fy = P.facing.y;
  P.state = 'ATTACK';
  setTimeout(() => { if (P.state === 'ATTACK') P.state = 'IDLE'; }, 280);

  if (P.stance === 'SWORD') {
    if (chargeLevel === 0) {
      SQS.push({ x: P.x + fx * 14, y: P.y + fy * 14, dx: fx, dy: fy, life: 65, type: 'sword', dmgMult: 1 });
      spark(P.x + fx * 14, P.y + fy * 14, '#00cfff', 5, 3, 18);
    } else if (chargeLevel === 1) {
      SQS.push({ x: P.x + fx * 16, y: P.y + fy * 16, dx: fx, dy: fy, life: 80, type: 'mega_sword', dmgMult: 2.5, sz: 16 });
      spark(P.x + fx * 16, P.y + fy * 16, '#00ffff', 15, 4, 30, 4);
      notify('⚔ 強化斬！巨型劍芒！');
    } else {
      screenShake = 12;
      notify('⚡⚡ 萬劍歸宗 ⚡⚡');
      const enemies = getEnemies().filter(e => e.alive);
      for (let i = 0; i < 8; i++) {
        setTimeout(() => {
          const a = i * Math.PI * 2 / 8;
          const sx = P.x + Math.cos(a) * 40, sy = P.y + Math.sin(a) * 40;
          const target = enemies[i % enemies.length] || { x: P.x + fx * 100, y: P.y + fy * 100 };
          const tdx = target.x - sx, tdy = target.y - sy, tl = Math.hypot(tdx, tdy) || 1;
          SQS.push({ x: sx, y: sy, dx: tdx / tl, dy: tdy / tl, life: 90, type: 'homing_sword', dmgMult: 3.5, sz: 12 });
          spark(sx, sy, '#00e5ff', 10, 3.5, 25, 3);
        }, i * 70);
      }
    }
  }
}

function toggleMed() {
  P.state = P.state === 'MEDITATE' ? 'IDLE' : 'MEDITATE';
  notify(P.state === 'MEDITATE' ? '🧘 進入打坐吐納…（再按 K 離開）' : '踏出打坐');
}

function doDash() {
  if (P.state === 'DASH' || P.state === 'DIE') return;
  if (P.state === 'MEDITATE') P.state = 'IDLE';
  P.state = 'DASH'; P.invincible = true;
  const ox = P.x, oy = P.y;
  P.x = Math.max(10, Math.min(470, P.x + P.facing.x * 32));
  P.y = Math.max(10, Math.min(260, P.y + P.facing.y * 32));
  for (let i = 0; i < 6; i++) PARTS.push({ x: ox + (P.x - ox) * i / 6, y: oy + (P.y - oy) * i / 6, vx: 0, vy: 0, col: '#4a8ad8', life: 18, ml: 18, sz: 3 });
  setTimeout(() => { P.invincible = false; if (P.state === 'DASH') P.state = 'IDLE'; }, 240);
}

// 按鍵監聽
const keys = {};
window.addEventListener('keydown', e => {
  const k = e.key === 'ArrowUp' ? 'w' : e.key === 'ArrowDown' ? 's' : e.key === 'ArrowLeft' ? 'a' : e.key === 'ArrowRight' ? 'd' : e.key.toLowerCase();
  keys[k] = true;
  if (e.code === 'Space') keys['j'] = true;
  if (e.key === 'Shift') keys['l'] = true;

  if (e.key === '1') setStance('SWORD');
  if (e.key === '2') setStance('TALISMAN');
  if (e.key === '3') setStance('FROST');
  if (e.key === '4') quickPill('pill_hp');
  if (e.key === '5') quickPill('pill_qi');
  if (e.key === '6') quickPill('pill_break');
  if (e.key === '7' || e.key === 'i') openInv();
  if (e.key === '8' || e.key === 'c') openChar();
  if (e.key === '9' || e.key === 'm') openMap();
  if (e.key === '?') openHelp();

  if ((k === 'j' || e.code === 'Space') && !P.charging && P.state !== 'DIE') {
    P.charging = true; P.chargeTime = 0;
  }
}, { capture: true });

window.addEventListener('keyup', e => {
  const k = e.key === 'ArrowUp' ? 'w' : e.key === 'ArrowDown' ? 's' : e.key === 'ArrowLeft' ? 'a' : e.key === 'ArrowRight' ? 'd' : e.key.toLowerCase();
  keys[k] = false;
  if (e.code === 'Space') keys['j'] = false;
  if (e.key === 'Shift') keys['l'] = false;

  if ((k === 'j' || e.code === 'Space') && P.charging) {
    P.charging = false;
    const cl = P.chargeTime > 1.8 ? 2 : P.chargeTime > .8 ? 1 : 0;
    triggerAttack(cl);
    P.chargeTime = 0;
  }
});

// 渲染與主迴圈
function drawBg() {
  const area = getCurArea();
  ctx.fillStyle = area.bg || '#0c0a1a';
  ctx.fillRect(0, 0, 480, 270);
  ctx.strokeStyle = 'rgba(42,56,96,.15)'; ctx.lineWidth = 1;
  for (let x = 0; x < 480; x += 24) { ctx.beginPath(); ctx.moveTo(x, 0); ctx.lineTo(x, 270); ctx.stroke(); }
  for (let y = 0; y < 270; y += 24) { ctx.beginPath(); ctx.moveTo(0, y); ctx.lineTo(480, y); ctx.stroke(); }
}

function drawPlayer() {
  if (P.hurtFlash > 0 && P.hurtFlash % 2 === 0) return;
  const hov = P.state === 'MOVE' ? Math.sin(T * 12) * 1.5 : 0;
  if (hdPlayerLoaded) {
    // 依狀態決定 Spritesheet 幀
    let fKey = 'IDLE';
    if (P.state === 'MOVE') {
      const wi = Math.floor(T * 8) % 3;
      fKey = ['WALK1', 'WALK2', 'WALK3'][wi];
    } else if (P.state === 'ATTACK') {
      const ai = Math.floor(T * 12) % 2;
      fKey = ['ATK1', 'ATK2'][ai];
    } else if (P.state === 'MEDITATE') {
      fKey = 'IDLE1';
    }
    const fr = PLAYER_FRAMES[fKey] || PLAYER_FRAMES.IDLE;
    const flipX = P.facing.x < 0;
    // 玩家顯示尺寸：48×64（保持比例）
    drawHDFrame(hdPlayerImg, fr.col, fr.row, PLAYER_SHEET_COLS, PLAYER_SHEET_ROWS,
      P.x - 24, P.y - 36 + hov, 48, 64, flipX);
  } else {
    drawSprite(IDLE0, P.x - 14, P.y - 20 + hov, 2, P.facing.x < 0);
  }
}

function drawEnemies() {
  const list = getEnemies ? getEnemies() : [];
  list.forEach(e => {
    if (!e.alive) return;
    const flash = e.hflash > 0 && e.hflash % 2 === 0;
    if (flash) return;
    if (e.isBoss) {
      // 決定 BOSS 動作
      let animKey = 'IDLE';
      if (e.atkCd > 0.8) animKey = 'ATTACK';
      else if (e._p2Rage && e.specialCd > 0) animKey = 'ROAR';
      const drawn = drawBossHD(e, animKey, T);
      if (!drawn) {
        // Fallback 像素陣列
        const sc = e.hflash > 0 ? '#ff4444' : null;
        drawSprite(ES[e.sprite] || ES.wolf, e.x - 8, e.y - 10, 3, e.facingLeft, sc);
      }
    } else {
      // 一般敵人：像素陣列
      const sc = e.hflash > 0 ? '#ff4444' : null;
      drawSprite(ES[e.sprite] || ES.wolf, e.x - 6, e.y - 8, 2, e.facingLeft, sc);
    }
  });
}

function drawMM() {
  const mc = document.getElementById('mm-canvas');
  if (!mc) return;
  const mctx = mc.getContext('2d');
  mctx.clearRect(0, 0, 96, 96);
  mctx.fillStyle = '#040814'; mctx.fillRect(0, 0, 96, 96);
  mctx.fillStyle = 'var(--gold)'; mctx.beginPath(); mctx.arc(P.x / 5, P.y / 3, 3, 0, Math.PI * 2); mctx.fill();
}

function render() {
  if (!ctx) return;
  ctx.save();
  if (screenShake > 0) {
    screenShake--;
    ctx.translate((Math.random() - .5) * screenShake, (Math.random() - .5) * screenShake);
  }
  drawBg();
  drawEnemies();
  drawPlayer();
  drawMM();
  ctx.restore();
}

function gameLoop(ts) {
  try {
    const dt = Math.min((ts - lastT) / 1000, .05); lastT = ts; T += dt;
    // 更新與渲染
    if (keys['w']) P.y = Math.max(12, P.y - P.speed);
    if (keys['s']) P.y = Math.min(258, P.y + P.speed);
    if (keys['a']) { P.x = Math.max(12, P.x - P.speed); P.facing.x = -1; }
    if (keys['d']) { P.x = Math.min(468, P.x + P.speed); P.facing.x = 1; }

    updEnemies();
    render();
  } catch (err) {
    console.error('Safe Game Loop caught error:', err);
  }
  requestAnimationFrame(gameLoop);
}

// 啟動遊戲
window.addEventListener('DOMContentLoaded', () => {
  initEngine();
  updateHUD();
  requestAnimationFrame(gameLoop);
});
