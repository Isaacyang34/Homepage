// ═══════════════════════════════════════════════════════════
//  敵人與 Boss 行為 AI (Enemy & Boss State Machine)
//  【Moonlighter 戰鬥AI：主動尋路、巡邏漫步與格層方格移動限制】
// ═══════════════════════════════════════════════════════════

const spawnedE = {};
const EPROJ = [];

function getCurArea() {
  if (typeof SECT_ROOMS !== 'undefined' && SECT_ROOMS[curAreaId]) {
    return SECT_ROOMS[curAreaId];
  }
  if (typeof activeDungeonRooms !== 'undefined' && activeDungeonRooms && activeDungeonRooms[currentRoomId]) {
    return activeDungeonRooms[currentRoomId];
  }
  if (typeof DUNGEON_WORLDS !== 'undefined' && DUNGEON_WORLDS[0]) {
    return DUNGEON_WORLDS[0];
  }
  return { id: 'sect_main', terrain: 'sect', doors: ['N','S','E','W'], enemies: [] };
}

function resetEnemies(areaId) {
  delete spawnedE[areaId];
}

function getEnemies() {
  if (!spawnedE[curAreaId]) {
    const area = getCurArea();
    const enemyList = (area && area.enemies) ? area.enemies : [];
    spawnedE[curAreaId] = enemyList.map(e => ({
      ...e,
      ox: e.x || 640, oy: e.y || 360,
      aiSt: 'PATROL',
      patrolTimer: Math.random() * 60,
      patrolDx: (Math.random() - 0.5) * 1.5,
      patrolDy: (Math.random() - 0.5) * 1.5,
      atkCd: 0, specialCd: 0, frozenTimer: 0, hflash: 0,
      facingLeft: false, alive: true,
    }));
    spawnedE[curAreaId].forEach(e => initEnemyAI(e));
  }
  return spawnedE[curAreaId] || [];
}

function initEnemyAI(e) {
  if (!e) return;
  e.facingLeft = false;
  switch (e.aiType) {
    case 'boss':   e.aggroR = 600; e.atkRange = 50; e.spd = 2.8; break;
    case 'wolf':   e.aggroR = 480; e.atkRange = 35; e.spd = 3.2; break;
    case 'spider': e.aggroR = 450; e.atkRange = 32; e.spd = 2.8; break;
    case 'mole':   e.aggroR = 420; e.atkRange = 30; e.spd = 2.5; break;
    case 'golem':  e.aggroR = 400; e.atkRange = 40; e.spd = 2.2; break;
    case 'bat':    e.aggroR = 500; e.atkRange = 30; e.spd = 3.6; break;
    default:       e.aggroR = 450; e.atkRange = 30; e.spd = 2.6; break;
  }
}

function updEnemies() {
  const dt = 0.016;
  const list = getEnemies();
  list.forEach(e => {
    if (!e || !e.alive) return;
    if (e.frozenTimer > 0) { e.frozenTimer -= dt; return; }
    if (e.hflash > 0) e.hflash--;

    const dx = P.x - e.x, dy = P.y - e.y;
    const dist = Math.hypot(dx, dy) || 1;
    const ndx = dx / dist, ndy = dy / dist;
    e.facingLeft = dx < 0;

    if (e.atkCd > 0) e.atkCd -= dt;

    if (e.isBoss) {
      const pct = e.hp / e.maxHp;
      const bossHud = document.getElementById('boss-hud');
      if (bossHud) {
        bossHud.style.display = 'block';
        if (document.getElementById('boss-name')) document.getElementById('boss-name').textContent = `👑 ${e.name} (${pct > .5 ? 'Phase 1' : pct > .2 ? 'Phase 2 (狂暴!)' : 'Phase 3 (毀滅狂暴!)'})`;
        if (document.getElementById('boss-hp-bar')) document.getElementById('boss-hp-bar').style.width = (pct * 100) + '%';
        if (document.getElementById('boss-hp-val')) document.getElementById('boss-hp-val').textContent = `${Math.ceil(e.hp)} / ${e.maxHp}`;
      }
      if (pct <= .5 && !e._p2Rage) { e._p2Rage = true; e.spd *= 1.45; }
    }

    // 1. 發現玩家 -> 追逐攻擊
    if (dist < e.aggroR) {
      e.x += ndx * e.spd;
      e.y += ndy * e.spd;

      // 觸及玩家攻擊判定
      if (dist <= e.atkRange && !P.invincible && e.atkCd <= 0) {
        const dmg = Math.max(1, e.atk - P.def);
        P.hp = Math.max(0, P.hp - dmg);
        P.hurtFlash = 12; P.invincible = true;
        e.atkCd = 1.2;
        setTimeout(() => { P.invincible = false; }, 400);
        updateHUD();
      }
    } else {
      // 2. 巡邏漫步
      e.patrolTimer--;
      if (e.patrolTimer <= 0) {
        e.patrolTimer = 60 + Math.random() * 90;
        e.patrolDx = (Math.random() - 0.5) * 1.5;
        e.patrolDy = (Math.random() - 0.5) * 1.5;
      }
      e.x += e.patrolDx;
      e.y += e.patrolDy;
    }

    // 嚴格限制怪物移動範圍只能在中間方格池 (MIN_X: 64, MAX_X: 1216, MIN_Y: 64, MAX_Y: 656)
    e.x = Math.max(64, Math.min(1216, e.x));
    e.y = Math.max(64, Math.min(656, e.y));
  });

  // 🛡️ 碰撞解析：怪物 vs 場景物件 + 怪物互相不重疊
  try {
    if (typeof resolveAllEnemyCollisions === 'function') resolveAllEnemyCollisions();
  } catch(e) {}
}

// 綁定全域 window 物件
window.updEnemies = updEnemies;
window.getCurArea = getCurArea;
window.getEnemies = getEnemies;
window.resetEnemies = resetEnemies;
