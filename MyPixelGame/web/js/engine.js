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
function drawBg() {
  const area = getCurArea ? getCurArea() : { bg: '#0c0a1a', terrain: 'sect' };
  const terr = area.terrain || 'sect';

  if (terr === 'sect') {
    // 🏛️ 青雲宗大殿：白玉青石地磚、金紋柱子與宗門大旗
    ctx.fillStyle = '#0f172a';
    ctx.fillRect(0, 0, 480, 270);

    // 地磚格線
    ctx.strokeStyle = 'rgba(51, 65, 85, 0.4)';
    ctx.lineWidth = 1;
    for (let x = 0; x < 480; x += 32) {
      for (let y = 0; y < 270; y += 32) {
        ctx.strokeRect(x, y, 32, 32);
      }
    }

    // 兩側朱紅大柱
    ctx.fillStyle = '#881337';
    ctx.fillRect(20, 0, 16, 270);
    ctx.fillRect(444, 0, 16, 270);
    ctx.fillStyle = '#fbbf24';
    ctx.fillRect(24, 0, 8, 270);
    ctx.fillRect(448, 0, 8, 270);

    // 中央宗門聖徽
    ctx.strokeStyle = 'rgba(245, 158, 11, 0.25)';
    ctx.lineWidth = 3;
    ctx.beginPath();
    ctx.arc(240, 135, 70, 0, Math.PI * 2);
    ctx.stroke();

  } else if (terr === 'mountain') {
    // 🌲 外門靈山：青翠草地、山石台階與古松
    ctx.fillStyle = '#064e3b';
    ctx.fillRect(0, 0, 480, 270);

    // 草皮斑駁紋理
    ctx.fillStyle = '#047857';
    for (let x = 0; x < 480; x += 40) {
      for (let y = 0; y < 270; y += 40) {
        if ((x + y) % 80 === 0) ctx.fillRect(x, y, 20, 20);
      }
    }

    // 石路小徑
    ctx.fillStyle = '#334155';
    ctx.fillRect(200, 0, 80, 270);
    ctx.fillStyle = '#475569';
    for (let y = 10; y < 270; y += 24) {
      ctx.fillRect(210, y, 60, 12);
    }

    // 古松裝飾 (四周樹叢)
    ctx.fillStyle = '#022c22';
    ctx.beginPath(); ctx.arc(40, 40, 35, 0, Math.PI * 2); ctx.fill();
    ctx.beginPath(); ctx.arc(440, 50, 40, 0, Math.PI * 2); ctx.fill();
    ctx.beginPath(); ctx.arc(30, 230, 35, 0, Math.PI * 2); ctx.fill();
    ctx.beginPath(); ctx.arc(450, 220, 38, 0, Math.PI * 2); ctx.fill();

  } else if (terr === 'cave') {
    // 🌌 玄陰洞府：幽暗岩石與螢光水晶脈
    ctx.fillStyle = '#090d16';
    ctx.fillRect(0, 0, 480, 270);

    // 洞穴基岩石紋
    ctx.strokeStyle = 'rgba(30, 41, 59, 0.6)';
    ctx.lineWidth = 2;
    for (let i = 0; i < 480; i += 60) {
      ctx.beginPath();
      ctx.moveTo(i, 0); ctx.lineTo(i + 30, 270);
      ctx.stroke();
    }

    // 螢光水晶簇 (發光亮藍點)
    const crystals = [
      { x: 50, y: 40, c: '#38bdf8' }, { x: 420, y: 60, c: '#818cf8' },
      { x: 80, y: 220, c: '#38bdf8' }, { x: 400, y: 210, c: '#c084fc' }
    ];
    crystals.forEach(cr => {
      ctx.fillStyle = cr.c;
      ctx.beginPath(); ctx.arc(cr.x, cr.y, 6, 0, Math.PI * 2); ctx.fill();
      ctx.fillStyle = 'rgba(56, 189, 248, 0.2)';
      ctx.beginPath(); ctx.arc(cr.x, cr.y, 16, 0, Math.PI * 2); ctx.fill();
    });

  } else if (terr === 'ruins') {
    // 🌋 古修遺跡萬魔窟：焦黑熾熱大地與赤紅熔岩裂隙
    ctx.fillStyle = '#18040a';
    ctx.fillRect(0, 0, 480, 270);

    // 熔岩裂痕
    ctx.strokeStyle = 'rgba(239, 68, 68, 0.4)';
    ctx.lineWidth = 3;
    ctx.beginPath();
    ctx.moveTo(0, 80); ctx.lineTo(180, 140); ctx.lineTo(320, 100); ctx.lineTo(480, 190);
    ctx.stroke();

    ctx.strokeStyle = 'rgba(245, 158, 11, 0.3)';
    ctx.lineWidth = 1;
    ctx.beginPath();
    ctx.moveTo(100, 0); ctx.lineTo(220, 270);
    ctx.stroke();

    // 斷壁殘垣柱基
    ctx.fillStyle = '#290814';
    ctx.fillRect(60, 30, 30, 30);
    ctx.fillRect(390, 30, 30, 30);
    ctx.fillRect(60, 200, 30, 30);
    ctx.fillRect(390, 200, 30, 30);
  }
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
    // 玩家顯示尺寸：72×96（放大高清）
    drawHDFrame(hdPlayerImg, fr.col, fr.row, PLAYER_SHEET_COLS, PLAYER_SHEET_ROWS,
      P.x - 36, P.y - 56 + hov, 72, 96, flipX);
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
