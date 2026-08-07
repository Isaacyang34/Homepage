// ═══════════════════════════════════════════════════════════
//  遊戲核心引擎、渲染器與動態全螢幕滿板自適應 (Engine & Full-Viewport Scaler)
// ═══════════════════════════════════════════════════════════

let canvas, ctx;
let lastT = 0, T = 0;
let SQS = [], PARTS = [], FLOATS = [], LGHTS = [];

// 動態滿板縮放計算 (Auto-Scale to fill screen nicely)
function autoScaleViewport() {
  const viewport = document.getElementById('game-viewport');
  const gc = document.getElementById('gc');
  if (!viewport || !gc) return;
  const vw = window.innerWidth;
  const vh = window.innerHeight;
  // 保持 480x270 的 16:9 比例，等比放大至填滿螢幕
  const scaleX = vw / 480;
  const scaleY = vh / 270;
  // 取較小的陷位保持比例
  const scale = Math.min(scaleX, scaleY);
  const displayW = Math.round(480 * scale);
  const displayH = Math.round(270 * scale);
  gc.style.width = displayW + 'px';
  gc.style.height = displayH + 'px';
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
function render() {
  if (!ctx) return;
  ctx.save();
  if (screenShake > 0) {
    screenShake--;
    ctx.translate((Math.random() - .5) * screenShake, (Math.random() - .5) * screenShake);
  }
  
  // 1. 獨立背景渲染器 (100% 模組化隔離)
  const curArea = getCurArea ? getCurArea() : null;
  if (window.BackgroundRenderer) {
    BackgroundRenderer.draw(ctx, curArea);
  }

  // 2. 敵人繪製
  drawEnemies();

  // 3. 獨立玩家渲染器 (100% 模組化隔離)
  if (window.PlayerRenderer) {
    PlayerRenderer.draw(ctx, P, T);
  }

  // 4. 右上角羅盤小地圖
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
