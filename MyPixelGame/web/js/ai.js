// ═══════════════════════════════════════════════════════════
//  敵人與 Boss 行為 AI (Enemy & Boss State Machine)
// ═══════════════════════════════════════════════════════════

const spawnedE = {};
const EPROJ = [];

function getCurArea() {
  return WORLD_AREAS.find(a => a.id === curAreaId) || WORLD_AREAS[0];
}

function getEnemies() {
  if (!spawnedE[curAreaId]) {
    const area = getCurArea();
    spawnedE[curAreaId] = area.enemies.map(e => ({
      ...e,
      ox: e.x, oy: e.y,
      aiSt: 'PATROL',
      aiTimer: 0,
      patrolAngle: Math.random() * Math.PI * 2,
      patrolSpeed: 0.8 + Math.random() * 0.4,
      atkCd: 0, specialCd: 0, frozenTimer: 0, hflash: 0,
      facingLeft: false, alive: true,
    }));
    // 綁定 AI 屬性
    spawnedE[curAreaId].forEach(e => initEnemyAI(e));
  }
  return spawnedE[curAreaId];
}

function initEnemyAI(e) {
  e.facingLeft = false;
  switch (e.aiType) {
    case 'boss':   e.aggroR = 220; e.atkRange = 28; e.canCharge = true; e.canShoot = true; e.canSlam = true; break;
    case 'wolf':   e.aggroR = 120; e.atkRange = 16; e.chargeSpd = 4.5; e.canCharge = true; break;
    case 'spider': e.aggroR = 150; e.atkRange = 80; e.keepDist = 70; e.canShoot = true; break;
    case 'mole':   e.aggroR = 100; e.atkRange = 22; e.canBurrow = true; break;
    case 'golem':  e.aggroR = 110; e.atkRange = 24; e.canSlam = true; break;
    case 'bat':    e.aggroR = 160; e.atkRange = 18; e.spd = 1.2; break;
    default:       e.aggroR = 100; e.atkRange = 18; break;
  }
}

function updEnemies() {
  const dt = 0.016;
  const list = getEnemies();
  list.forEach(e => {
    if (!e.alive) return;
    if (e.frozenTimer > 0) { e.frozenTimer -= dt; return; }
    if (e.hflash > 0) e.hflash--;

    const dx = P.x - e.x, dy = P.y - e.y;
    const dist = Math.hypot(dx, dy) || 1;
    const ndx = dx / dist, ndy = dy / dist;
    e.facingLeft = dx < 0;

    if (e.atkCd > 0) e.atkCd -= dt;
    if (e.specialCd > 0) e.specialCd -= dt;

    if (e.isBoss) {
      const pct = e.hp / e.maxHp;
      const bossHud = $('boss-hud');
      if (bossHud) {
        bossHud.style.display = 'block';
        if ($('boss-name')) $('boss-name').textContent = `👑 ${e.name} (${pct > .5 ? 'Phase 1' : pct > .2 ? 'Phase 2 (狂暴!)' : 'Phase 3 (毀滅狂暴!)'})`;
        if ($('boss-hp-bar')) $('boss-hp-bar').style.width = (pct * 100) + '%';
        if ($('boss-hp-val')) $('boss-hp-val').textContent = `${Math.ceil(e.hp)} / ${e.maxHp}`;
      }
      if (pct <= .5 && !e._p2Rage) { e._p2Rage = true; e.spd *= 1.45; }
    }

    if (dist < e.aggroR && e.atkCd <= 0) {
      e.x += ndx * e.spd;
      e.y += ndy * e.spd;
      if (dist <= e.atkRange && !P.invincible) {
        const dmg = Math.max(1, e.atk - P.def);
        P.hp = Math.max(0, P.hp - dmg);
        P.hurtFlash = 12; P.invincible = true;
        e.atkCd = 1.2;
        setTimeout(() => { P.invincible = false; }, 400);
        updateHUD();
      }
    }
  });
}
