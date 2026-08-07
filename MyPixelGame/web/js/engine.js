// ═══════════════════════════════════════════════════════════
//  遊戲核心引擎、生成式 5層地牢與三類武器戰鬥系統 (Engine & Combat)
// ═══════════════════════════════════════════════════════════

let canvas, ctx;
let lastT = 0, T = 0;
let SQS = [], PARTS = [], FLOATS = [], LGHTS = [];
let doorCooldown = 0;
let activeDungeonRooms = null; // 當前生成式地牢地圖樹
let currentRoomId = 'room_0';   // 當前所在房間 ID

// 🌀 房間過場黑幕轉場系統 (Fade Transition System)
let isRoomTransitioning = false;
let roomTransitionAlpha = 0;
let roomTransitionPhase = 'IDLE'; // 'FADE_OUT' | 'FADE_IN' | 'IDLE'
let onPeakAction = null;

// ════ 牆壁碰撞常數 ════
// WALL_N/S/W/E, DOOR_GAP/CX/CY 已定義在 config.js 並掛載至 window
// 此處直接引用全域變數，不重複宣告

// 根據當前房間門洞動態計算可行走邊界
function getPlayerBounds() {
  const area = (typeof SECT_ROOMS !== 'undefined' && SECT_ROOMS[curAreaId])
    ? SECT_ROOMS[curAreaId]
    : (activeDungeonRooms && activeDungeonRooms[currentRoomId])
      ? activeDungeonRooms[currentRoomId]
      : null;
  const doors = area ? (area.doors || []) : [];

  return {
    minY: (doors.includes('N') && Math.abs(P.x - DOOR_CX) <= DOOR_GAP) ? 0   : WALL_N,
    maxY: (doors.includes('S') && Math.abs(P.x - DOOR_CX) <= DOOR_GAP) ? 720 : WALL_S,
    minX: (doors.includes('W') && Math.abs(P.y - DOOR_CY) <= DOOR_GAP) ? 0   : WALL_W,
    maxX: (doors.includes('E') && Math.abs(P.y - DOOR_CY) <= DOOR_GAP) ? 1280 : WALL_E,
  };
}


function triggerRoomTransition(onPeakCallback) {
  if (isRoomTransitioning) return;
  isRoomTransitioning = true;
  roomTransitionPhase = 'FADE_OUT';
  roomTransitionAlpha = 0;
  onPeakAction = onPeakCallback;
}

function updRoomTransition() {
  if (!isRoomTransitioning) return;
  if (roomTransitionPhase === 'FADE_OUT') {
    roomTransitionAlpha += 0.1;
    if (roomTransitionAlpha >= 1.0) {
      roomTransitionAlpha = 1.0;
      if (typeof onPeakAction === 'function') {
        try { onPeakAction(); } catch(e) {}
      }
      roomTransitionPhase = 'FADE_IN';
    }
  } else if (roomTransitionPhase === 'FADE_IN') {
    roomTransitionAlpha -= 0.1;
    if (roomTransitionAlpha <= 0) {
      roomTransitionAlpha = 0;
      roomTransitionPhase = 'IDLE';
      isRoomTransitioning = false;
    }
  }
}

// 動態滿板縮放計算 (原生 1280x720 720p HD 高畫質畫布)
function autoScaleViewport() {
  const viewport = document.getElementById('game-viewport');
  const gc = document.getElementById('gc');
  if (!viewport || !gc) return;
  const vw = window.innerWidth;
  const vh = window.innerHeight;
  const scaleX = vw / 1280;
  const scaleY = vh / 720;
  const scale = Math.min(scaleX, scaleY);
  const displayW = Math.round(1280 * scale);
  const displayH = Math.round(720 * scale);
  gc.style.width = displayW + 'px';
  gc.style.height = displayH + 'px';
}

window.addEventListener('resize', autoScaleViewport);

function initEngine() {
  canvas = document.getElementById('gc');
  if (canvas) {
    canvas.width = 1280;
    canvas.height = 720;
    ctx = canvas.getContext('2d');
  }
  window.ctx = ctx;
  autoScaleViewport();
}

// ───── 特效物理 ─────
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

function quickPill(type = 'pill_hp') {
  useItem(type);
}

// ⚔️ 武器種類（劍、槍、鏢）與五行相生揮砍/發射邏輯
function triggerAttack(chargeLevel) {
  if (P.state === 'DIE' || isRoomTransitioning) return;
  if (P.state === 'MEDITATE') P.state = 'IDLE';
  const fx = P.facing.x || (P.facing.y === 0 ? 1 : 0), fy = P.facing.y;
  P.state = 'ATTACK';
  setTimeout(() => { if (P.state === 'ATTACK') P.state = 'IDLE'; }, 220);

  const wType = (P.weapon && P.weapon.type) ? P.weapon.type : 'SWORD';
  const wElem = (P.weapon && P.weapon.elem) ? P.weapon.elem : 'GOLD';

  // 提升當前裝備功法熟練度
  if (P.equippedSutraIds) {
    P.equippedSutraIds.forEach(sId => {
      if (typeof trainSutraMastery === 'function') trainSutraMastery(sId, 12);
    });
  }

  // 1. 🗡️ 劍 (Sword) - 近戰 1 格 (~55px Hitbox)
  if (wType === 'SWORD') {
    const slashX = P.x + fx * 38;
    const slashY = P.y + fy * 38;
    SQS.push({
      x: slashX, y: slashY, dx: 0, dy: 0, speed: 0, life: 10,
      type: 'sword', elem: wElem, dmgMult: chargeLevel > 0 ? 2.2 : 1.0, sz: chargeLevel > 0 ? 95 : 65
    });
    spark(slashX, slashY, chargeLevel > 0 ? '#00ffff' : '#00cfff', chargeLevel > 0 ? 14 : 8, 3.5, 12, 3);
  }
  // 2. 🔱 槍 (Spear) - 直線貫穿 3 格 (~150px Hitbox)
  else if (wType === 'SPEAR') {
    const spearX = P.x + fx * 85;
    const spearY = P.y + fy * 85;
    SQS.push({
      x: spearX, y: spearY, dx: 0, dy: 0, speed: 0, life: 12,
      type: 'spear', elem: wElem, dmgMult: chargeLevel > 0 ? 2.8 : 1.4, sz: chargeLevel > 0 ? 180 : 130
    });
    spark(spearX, spearY, '#f59e0b', 12, 4.5, 14, 3);
    notify('🔱 長槍出洞！直線貫穿 3 格！');
  }
  // 3. 🎯 鏢 (Dart) - 遠程暗器 10 格 (~500px 飛行發射)
  else if (wType === 'DART') {
    const startX = P.x + fx * 25;
    const startY = P.y + fy * 25;
    SQS.push({
      x: startX, y: startY, dx: fx, dy: fy, speed: 12, life: 42,
      type: 'dart', elem: wElem, dmgMult: chargeLevel > 0 ? 2.0 : 1.1, sz: 32
    });
    spark(startX, startY, '#38bdf8', 6, 2.5, 10, 2);
    notify('🎯 甩射寒冰飛鏢！破空射擊 10 格！');
  }
}

function toggleMed() {
  P.state = P.state === 'MEDITATE' ? 'IDLE' : 'MEDITATE';
  notify(P.state === 'MEDITATE' ? '🧘 進入打坐吐納…（再按 K 離開）' : '踏出打坐');
}

function doDash() {
  if (P.state === 'DASH' || P.state === 'DIE' || isRoomTransitioning) return;
  if (P.state === 'MEDITATE') P.state = 'IDLE';
  P.state = 'DASH'; P.invincible = true;
  const ox = P.x, oy = P.y;
  // 衝刺也遵循牆壁內緣限制（統一使用 WALL_* 常數）
  const bounds = getPlayerBounds();
  P.x = Math.max(bounds.minX, Math.min(bounds.maxX, P.x + P.facing.x * 75));
  P.y = Math.max(bounds.minY, Math.min(bounds.maxY, P.y + P.facing.y * 75));
  for (let i = 0; i < 6; i++) PARTS.push({ x: ox + (P.x - ox) * i / 6, y: oy + (P.y - oy) * i / 6, vx: 0, vy: 0, col: '#4a8ad8', life: 18, ml: 18, sz: 3 });
  setTimeout(() => { P.invincible = false; if (P.state === 'DASH') P.state = 'IDLE'; }, 240);
}

// 🏰 5 層生成式地牢拓撲生成器 (最多 10 個房間，邊角為 BOSS 房)
function generateFloorDungeon(worldIdx, floor) {
  const world = (typeof DUNGEON_WORLDS !== 'undefined') ? (DUNGEON_WORLDS[worldIdx] || DUNGEON_WORLDS[0]) : { name: '秘境', mobs: [], boss: {} };
  const roomCount = Math.floor(Math.random() * 3) + 7;
  const rooms = {};

  for (let i = 0; i < roomCount; i++) {
    const id = `room_${i}`;
    const isEntrance = (i === 0);
    const isBoss = (i === roomCount - 1);

    let doors = ['N', 'S', 'E', 'W'];
    if (isBoss) doors = ['S'];
    else if (isEntrance) doors = ['S', 'N', 'E'];

    const multHp = 1 + (floor - 1) * 0.35;
    const multAtk = 1 + (floor - 1) * 0.3;
    const multRw = 1 + (floor - 1) * 0.45;

    let enemies = [];
    if (isBoss && world.boss) {
      const b = world.boss;
      enemies = [{
        ...b,
        hp: Math.ceil((b.hp || 300) * multHp), maxHp: Math.ceil((b.hp || 300) * multHp),
        atk: Math.ceil((b.atk || 20) * multAtk),
        reward: { stones: Math.ceil((b.reward?.stones || 200) * multRw), exp: Math.ceil((b.reward?.exp || 300) * multRw) }
      }];
    } else if (!isEntrance && world.mobs && world.mobs.length > 0) {
      const mobCount = Math.floor(Math.random() * 3) + 2;
      for (let m = 0; m < mobCount; m++) {
        const mobTemplate = world.mobs[m % world.mobs.length];
        enemies.push({
          id: m,
          x: 200 + Math.random() * 880,
          y: 150 + Math.random() * 420,
          ...mobTemplate,
          hp: Math.ceil((mobTemplate.hp || 40) * multHp), maxHp: Math.ceil((mobTemplate.hp || 40) * multHp),
          atk: Math.ceil((mobTemplate.atk || 8) * multAtk),
          reward: { stones: Math.ceil((mobTemplate.reward?.stones || 10) * multRw), exp: Math.ceil((mobTemplate.reward?.exp || 15) * multRw) }
        });
      }
    }

    rooms[id] = {
      id,
      name: isBoss ? `👑 ${world.name} · 第 ${floor} 階【首領戰】` : isEntrance ? `🚪 ${world.name} · 第 ${floor} 階【入口】` : `⚔️ ${world.name} · 第 ${floor} 階 (房間 ${i})`,
      terrain: world.terrain || 'mountain',
      doors,
      enemies,
      isBossRoom: isBoss,
      isEntranceRoom: isEntrance,
      cleared: false
    };
  }

  activeDungeonRooms = rooms;
  currentRoomId = 'room_0';
  return rooms['room_0'];
}

// 門樓與地圖傳送觸發 (帶平滑黑幕淡出過場動畫)
function checkDoorTriggers() {
  if (doorCooldown > 0 || isRoomTransitioning) { if (doorCooldown > 0) doorCooldown--; return; }

  // 1. 宗門內部固定房間切換
  if (typeof SECT_ROOMS !== 'undefined' && SECT_ROOMS[curAreaId]) {
    let nextSect = null;
    let targetX = 640, targetY = 480;

    if (curAreaId === 'sect_main') {
      if (P.x >= 1238 && Math.abs(P.y - 360) <= 80) { nextSect = 'sect_alchemy'; targetX = 100; targetY = 360; }
      else if (P.x <= 42 && Math.abs(P.y - 360) <= 80) { nextSect = 'sect_forge'; targetX = 1180; targetY = 360; }
      else if (P.y >= 678 && Math.abs(P.x - 640) <= 90) { nextSect = 'sect_gate'; targetX = 640; targetY = 90; }
      else if (P.y <= 42 && Math.abs(P.x - 640) <= 90) { nextSect = 'sect_market'; targetX = 640; targetY = 630; }
    } else if (curAreaId === 'sect_market' && P.y >= 678) {
      nextSect = 'sect_main'; targetX = 640; targetY = 90;
    } else if (curAreaId === 'sect_alchemy' && P.x <= 42) {
      nextSect = 'sect_main'; targetX = 1180; targetY = 360;
    } else if (curAreaId === 'sect_forge' && P.x >= 1238) {
      nextSect = 'sect_main'; targetX = 100; targetY = 360;
    } else if (curAreaId === 'sect_gate') {
      if (P.y <= 42 && Math.abs(P.x - 640) <= 90) { nextSect = 'sect_main'; targetX = 640; targetY = 630; }
      else if (P.x <= 42) { nextSect = 'sect_meditate'; targetX = 1180; targetY = 360; }
      else if (P.x >= 1238) { nextSect = 'sect_spring'; targetX = 100; targetY = 360; }
      else if (P.y >= 678 && Math.abs(P.x - 640) <= 90) {
        // 踏出山門 -> 平滑黑幕過場進入 5 層生成式地牢
        triggerRoomTransition(() => {
          const firstRoom = generateFloorDungeon(P.worldIdx, P.currentFloor);
          curAreaId = firstRoom.id;
          P.x = 640; P.y = 100;
          doorCooldown = 50;
          const bh = document.getElementById('boss-hud'); if (bh) bh.style.display = 'none';
          notify(`⛩️ 踏出山門！進入【${DUNGEON_WORLDS[P.worldIdx].name} · 第 ${P.currentFloor} 階】！`);
        });
        return;
      }
    } else if ((curAreaId === 'sect_meditate' && P.x >= 1238) || (curAreaId === 'sect_spring' && P.x <= 42)) {
      nextSect = 'sect_gate'; targetX = 640; targetY = 360;
    }

    if (nextSect) {
      triggerRoomTransition(() => {
        curAreaId = nextSect;
        P.x = targetX; P.y = targetY;
        doorCooldown = 35;
        const bh = document.getElementById('boss-hud'); if (bh) bh.style.display = 'none';
        notify(`🚪 進入【${SECT_ROOMS[nextSect].name}】`);
      });
    }
    return;
  }

  // 2. 生成式秘境地牢房間切換
  if (activeDungeonRooms && activeDungeonRooms[currentRoomId]) {
    const curRoom = activeDungeonRooms[currentRoomId];
    const doors = curRoom.doors || ['N', 'S', 'E', 'W'];

    let enteredDir = null;
    if (doors.includes('N') && P.y <= 42 && Math.abs(P.x - 640) <= 90) enteredDir = 'N';
    else if (doors.includes('S') && P.y >= 678 && Math.abs(P.x - 640) <= 90) enteredDir = 'S';
    else if (doors.includes('W') && P.x <= 42 && Math.abs(P.y - 360) <= 80) enteredDir = 'W';
    else if (doors.includes('E') && P.x >= 1238 && Math.abs(P.y - 360) <= 80) enteredDir = 'E';

    if (enteredDir) {
      triggerRoomTransition(() => {
        doorCooldown = 45;
        if (curRoom.isEntranceRoom && enteredDir === 'S') {
          curAreaId = 'sect_gate';
          P.x = 640; P.y = 630;
          notify('⛩️ 返回【青雲宗 · 宗門山門】');
          return;
        }

        const roomKeys = Object.keys(activeDungeonRooms);
        const nextKey = roomKeys[Math.floor(Math.random() * roomKeys.length)];
        currentRoomId = nextKey;
        const nextRoom = activeDungeonRooms[currentRoomId];
        curAreaId = nextRoom.id;

        if (enteredDir === 'N') { P.x = 640; P.y = 630; }
        else if (enteredDir === 'S') { P.x = 640; P.y = 90; }
        else if (enteredDir === 'W') { P.x = 1180; P.y = 360; }
        else if (enteredDir === 'E') { P.x = 100; P.y = 360; }

        if (typeof resetEnemies === 'function') resetEnemies(curAreaId);
        if (typeof updateHUD === 'function') updateHUD();
        notify(`🚪 進入【${nextRoom.name}】`);
      });
    }
  }
}

// 招式與粒子物理更新
function updAttacks() {
  for (let i = SQS.length - 1; i >= 0; i--) {
    const s = SQS[i];
    s.x += (s.dx || 0) * (s.speed || 0);
    s.y += (s.dy || 0) * (s.speed || 0);
    s.life--;

    const enemies = (typeof getEnemies === 'function') ? getEnemies() : [];
    enemies.forEach(e => {
      if (!e || !e.alive) return;
      const hitDist = Math.hypot(s.x - e.x, s.y - e.y);
      const hitRadius = (s.sz || 40) + (e.isBoss ? 35 : 24);

      if (hitDist <= hitRadius) {
        const dmg = Math.max(1, Math.ceil(P.atk * (s.dmgMult || 1.0) - (e.def || 0)));
        e.hp -= dmg;
        e.hflash = 8;
        floatTxt(e.x, e.y - 14, '-' + dmg, '#ffea00');
        spark(e.x, e.y, '#00ffff', 8, 4);

        if (e.hp <= 0) {
          e.alive = false;
          P.kills++;
          const rw = e.reward || { stones: 15, exp: 20 };
          P.stones += rw.stones;
          P.exp += rw.exp;

          if (e.isBoss && typeof FIVE_ELEMENT_SUTRAS !== 'undefined') {
            const unlearned = FIVE_ELEMENT_SUTRAS.filter(st => !P.learnedSutras.some(ls => ls.id === st.id));
            if (unlearned.length > 0) {
              const dropped = unlearned[Math.floor(Math.random() * unlearned.length)];
              P.learnedSutras.push({ id: dropped.id, level: 1, masteryExp: 0, maxMastery: 100 });
              notify(`👑 震撼擊敗首領【${e.name}】！領悟絕世功法【${dropped.name}】！`);
            } else {
              notify(`👑 震撼擊敗首領【${e.name}】！獲得鉅額靈石 +${rw.stones}！`);
            }

            if (P.currentFloor < 5) {
              P.currentFloor++;
              setTimeout(() => {
                triggerRoomTransition(() => {
                  notify(`🌟 成功突破第 ${P.currentFloor - 1} 階！自動進入【第 ${P.currentFloor} 階關卡】！`);
                  generateFloorDungeon(P.worldIdx, P.currentFloor);
                  P.x = 640; P.y = 480;
                });
              }, 1800);
            } else {
              if (typeof DUNGEON_WORLDS !== 'undefined' && P.worldIdx < DUNGEON_WORLDS.length - 1) {
                P.worldIdx++;
                P.currentFloor = 1;
                setTimeout(() => {
                  triggerRoomTransition(() => {
                    notify(`🎉 震撼通關 5 階關卡！成功解鎖全新秘境【${DUNGEON_WORLDS[P.worldIdx].name}】！`);
                    curAreaId = 'sect_gate';
                    P.x = 640; P.y = 480;
                  });
                }, 2200);
              }
            }
          } else {
            notify(`⚔ 擊殺【${e.name}】！靈石 +${rw.stones}、經驗 +${rw.exp}`);
          }
          if (typeof updateHUD === 'function') updateHUD();
        }
        if (s.speed > 0) s.life = 0;
      }
    });

    if (s.life <= 0) SQS.splice(i, 1);
  }

  for (let i = PARTS.length - 1; i >= 0; i--) {
    const p = PARTS[i]; p.x += (p.vx || 0); p.y += (p.vy || 0); p.life--;
    if (p.life <= 0) PARTS.splice(i, 1);
  }
  for (let i = FLOATS.length - 1; i >= 0; i--) {
    const f = FLOATS[i]; f.y += f.vy; f.life--;
    if (f.life <= 0) FLOATS.splice(i, 1);
  }
}

// 招式與特效繪製
function drawAttacks() {
  if (!ctx) return;
  SQS.forEach(s => {
    ctx.save();
    ctx.fillStyle = s.col || '#00cfff';
    ctx.shadowColor = s.col || '#00cfff';
    ctx.shadowBlur = 12;
    ctx.beginPath();
    ctx.arc(s.x, s.y, s.sz ? s.sz / 2 : 16, 0, Math.PI * 2);
    ctx.fill();
    ctx.restore();
  });
  PARTS.forEach(p => {
    ctx.fillStyle = p.col || '#00cfff';
    ctx.fillRect(p.x, p.y, p.sz || 2, p.sz || 2);
  });
  FLOATS.forEach(f => {
    ctx.fillStyle = f.col || '#ffea00';
    ctx.font = 'bold 13px sans-serif';
    ctx.textAlign = 'center';
    ctx.fillText(f.txt, f.x, f.y);
  });
}

// 🧑 NPC 繪製與近距離互動提示
function drawNPCs() {
  if (!ctx) return;
  const areaObj = (typeof SECT_ROOMS !== 'undefined') ? SECT_ROOMS[curAreaId] : null;
  if (!areaObj || !areaObj.npcs) return;

  areaObj.npcs.forEach(npc => {
    const nx = npc.x || 640, ny = npc.y || 350;
    ctx.save();

    // 1. 橢圓陰影與腳下護體金光
    ctx.fillStyle = 'rgba(0, 0, 0, 0.4)';
    ctx.beginPath(); ctx.ellipse(nx, ny + 10, 32, 12, 0, 0, Math.PI * 2); ctx.fill();

    const glow = ctx.createRadialGradient(nx, ny, 4, nx, ny, 42);
    glow.addColorStop(0, 'rgba(255, 215, 0, 0.35)');
    glow.addColorStop(1, 'rgba(0, 0, 0, 0)');
    ctx.fillStyle = glow;
    ctx.beginPath(); ctx.arc(nx, ny, 42, 0, Math.PI * 2); ctx.fill();

    // 2. 繪製 NPC 圖標與亮麗造型
    ctx.font = '36px sans-serif';
    ctx.textAlign = 'center';
    ctx.textBaseline = 'middle';
    ctx.fillText(npc.icon || '🧑', nx, ny - 15);

    // 3. NPC 稱號名字
    ctx.font = 'bold 13px sans-serif';
    ctx.fillStyle = '#ffd700';
    ctx.shadowColor = '#000000'; ctx.shadowBlur = 6;
    ctx.fillText(npc.name, nx, ny - 48);

    // 4. 靠近時顯示互動按鈕提示 `▶ 按 [F] 鍵對話`
    const dist = Math.hypot(P.x - nx, P.y - ny);
    if (dist <= 75) {
      ctx.fillStyle = '#00ffff';
      ctx.font = 'bold 12px sans-serif';
      ctx.fillText('▶ 按 [F] 鍵對話', nx, ny - 66);

      // 按下 F 鍵觸發NPC對話
      if (keys['f']) {
        keys['f'] = false;
        if (typeof startDlg === 'function') startDlg(npc);
      }
    }

    ctx.restore();
  });
}

// 👾 怪物與首領繪製
function drawEnemies() {
  if (!ctx) return;
  const list = (typeof getEnemies === 'function') ? getEnemies() : [];
  if (!list || list.length === 0) return;

  list.forEach(e => {
    if (!e || !e.alive) return;
    ctx.save();
    const ex = e.x, ey = e.y;

    // 1. 怪物腳下陰影
    ctx.fillStyle = 'rgba(0, 0, 0, 0.45)';
    ctx.beginPath();
    ctx.ellipse(ex, ey + 12, e.isBoss ? 55 : 28, e.isBoss ? 20 : 10, 0, 0, Math.PI * 2);
    ctx.fill();

    // 2. 受傷受擊閃爍
    if (e.hflash > 0) ctx.globalAlpha = 0.65;

    // 3. 嘗試精靈圖繪製，若無則降級向量極光怪物
    let sprDrawn = false;
    const sprMat = (typeof ES !== 'undefined' && ES[e.sprite]) ? ES[e.sprite] : null;
    if (sprMat && typeof drawSprite === 'function') {
      drawSprite(sprMat, ex - 40, ey - 45, e.isBoss ? 6.5 : 4.5, e.facingLeft, null, ctx);
      sprDrawn = true;
    }

    if (!sprDrawn) {
      ctx.fillStyle = e.color || '#e84040';
      ctx.beginPath(); ctx.arc(ex, ey - 15, e.isBoss ? 36 : 20, 0, Math.PI * 2); ctx.fill();
    }

    // 4. 怪物頭頂血條與名稱
    const hpPct = Math.max(0, Math.min(1, e.hp / e.maxHp));
    const barW = e.isBoss ? 90 : 46;
    const barY = ey - (e.isBoss ? 75 : 45);

    // 血條底色與紅條
    ctx.fillStyle = 'rgba(0,0,0,0.7)';
    ctx.fillRect(ex - barW / 2, barY, barW, 6);
    ctx.fillStyle = e.isBoss ? '#ff0055' : '#ef4444';
    ctx.fillRect(ex - barW / 2, barY, barW * hpPct, 6);

    // 怪物名稱與稱號
    ctx.font = 'bold 11px sans-serif';
    ctx.fillStyle = e.isBoss ? '#ff2200' : '#ffffff';
    ctx.textAlign = 'center';
    ctx.shadowColor = '#000'; ctx.shadowBlur = 4;
    ctx.fillText(e.name, ex, barY - 6);

    ctx.restore();
  });
}

function render() {
  if (!ctx) return;
  ctx.save();

  const shake = typeof screenShake !== 'undefined' ? screenShake : (window.screenShake || 0);
  if (shake > 0) {
    if (typeof screenShake !== 'undefined') screenShake--;
    if (typeof window.screenShake !== 'undefined') window.screenShake--;
    ctx.translate((Math.random() - .5) * shake, (Math.random() - .5) * shake);
  }

  // 1. 獨立背景渲染器
  const areaId = typeof curAreaId !== 'undefined' ? curAreaId : (window.curAreaId || 'sect_main');
  let curAreaObj = (typeof SECT_ROOMS !== 'undefined') ? SECT_ROOMS[areaId] : null;
  if (!curAreaObj && typeof activeDungeonRooms !== 'undefined' && activeDungeonRooms) curAreaObj = activeDungeonRooms[typeof currentRoomId !== 'undefined' ? currentRoomId : 'room_0'];
  if (!curAreaObj) curAreaObj = { terrain: 'sect', doors: ['N','S','E','W'] };

  if (typeof BackgroundRenderer !== 'undefined' && BackgroundRenderer.draw) {
    try { BackgroundRenderer.draw(ctx, curAreaObj); } catch(e) {}
  }

  // 2. 招式與特效
  try { drawAttacks(); } catch(e) {}

  // 3. NPC 繪製 (宗主、藥師兄、藏劍長老、傳功長老、靈泉守衛、守山長老)
  try { drawNPCs(); } catch(e) {}

  // 4. 敵人與 Boss 繪製
  try { drawEnemies(); } catch(e) {}

  // 5. 獨立玩家渲染器 (100% 絕對保證執行，畫出角色)
  const pState = typeof P !== 'undefined' ? P : window.P;
  if (typeof PlayerRenderer !== 'undefined' && PlayerRenderer.draw) {
    try { PlayerRenderer.draw(ctx, pState, T); } catch(e) { console.error('PlayerRenderer.draw error:', e); }
  }

  // 6. 羅盤小地圖
  try { drawMM(); } catch(e) {}

  // 7. 🌀 房間切換平滑黑幕過場 (Fade Transition)
  if (roomTransitionAlpha > 0) {
    ctx.fillStyle = `rgba(0, 0, 0, ${roomTransitionAlpha.toFixed(2)})`;
    ctx.fillRect(0, 0, 1280, 720);
    if (roomTransitionAlpha > 0.35) {
      ctx.fillStyle = `rgba(0, 207, 255, ${Math.min(1, roomTransitionAlpha * 1.2).toFixed(2)})`;
      ctx.font = 'bold 18px sans-serif';
      ctx.textAlign = 'center';
      ctx.shadowColor = '#00cfff';
      ctx.shadowBlur = 8;
      ctx.fillText('🌀 踏入傳送陣，切換房間中…', 640, 360);
    }
  }

  ctx.restore();
}

// 🗺 右上角羅盤小地圖（完整功能版）
// 畫布 90x90，對應遊戲場景 1280x720
// 縮放比例：sx = 90/1280 ≈ 0.0703，sy = 90/720 = 0.125
function drawMM() {
  const mc = document.getElementById('mm-canvas');
  if (!mc) return;
  const mctx = mc.getContext('2d');
  const MW = 90, MH = 90;
  const SX = MW / 1280, SY = MH / 720;

  // ─ 0. 清底 ─
  mctx.clearRect(0, 0, MW, MH);
  mctx.fillStyle = '#020510';
  mctx.fillRect(0, 0, MW, MH);

  // 取得當前房間資料
  const isSect = typeof SECT_ROOMS !== 'undefined' && SECT_ROOMS[curAreaId];
  const isDungeon = !isSect && typeof activeDungeonRooms !== 'undefined' && activeDungeonRooms;
  const area = isSect
    ? SECT_ROOMS[curAreaId]
    : isDungeon
      ? activeDungeonRooms[currentRoomId]
      : null;
  const doors  = area ? (area.doors || []) : [];
  const isBossRoom = area && area.isBossRoom;

  // ─ 1. 房間地板底色 ─
  const floorCol = isBossRoom
    ? 'rgba(180,0,0,0.35)'
    : isSect
      ? 'rgba(10,25,55,0.85)'
      : 'rgba(5,18,38,0.85)';
  mctx.fillStyle = floorCol;
  // 地板留牆壁內緣（64px對應到縮放後約4.5px）
  const wallPx = Math.round(64 * SX);
  mctx.fillRect(wallPx, wallPx, MW - wallPx * 2, MH - wallPx * 2);

  // ─ 2. 牆壁外框 ─
  mctx.strokeStyle = isBossRoom ? '#ef4444' : '#2a3860';
  mctx.lineWidth = 1.5;
  mctx.strokeRect(wallPx, wallPx, MW - wallPx * 2, MH - wallPx * 2);

  // ─ 3. 門洞（以缺口表示，在對應牆壁位置畫亮色小方塊） ─
  const doorCol = isBossRoom ? '#ff6060' : '#f59e0b';
  mctx.fillStyle = doorCol;
  const gapHalf = Math.round(DOOR_GAP * SX); // 門洞半寬縮放後
  const gapCX   = Math.round(DOOR_CX * SX);
  const gapCY   = Math.round(DOOR_CY * SY);

  if (doors.includes('N')) {
    mctx.fillRect(gapCX - gapHalf, 0, gapHalf * 2, wallPx + 1);
  }
  if (doors.includes('S')) {
    mctx.fillRect(gapCX - gapHalf, MH - wallPx - 1, gapHalf * 2, wallPx + 1);
  }
  if (doors.includes('W')) {
    const gapTop = Math.round((DOOR_CY - DOOR_GAP) * SY);
    const gapH   = Math.round(DOOR_GAP * 2 * SY);
    mctx.fillRect(0, gapTop, wallPx + 1, gapH);
  }
  if (doors.includes('E')) {
    const gapTop = Math.round((DOOR_CY - DOOR_GAP) * SY);
    const gapH   = Math.round(DOOR_GAP * 2 * SY);
    mctx.fillRect(MW - wallPx - 1, gapTop, wallPx + 1, gapH);
  }

  // ─ 4. 宗門模式：顯示靜態場景物件圖示 ─
  if (isSect && typeof STATIC_COLLIDERS !== 'undefined') {
    const rects = STATIC_COLLIDERS[curAreaId] || [];
    mctx.fillStyle = 'rgba(168,85,247,0.55)';
    for (const r of rects) {
      mctx.fillRect(
        Math.round(r.x * SX), Math.round(r.y * SY),
        Math.max(3, Math.round(r.w * SX)), Math.max(3, Math.round(r.h * SY))
      );
    }
    // 靈泉池（圓形）
    if (curAreaId === 'sect_spring') {
      mctx.fillStyle = 'rgba(14,165,233,0.45)';
      mctx.beginPath();
      mctx.arc(Math.round(640 * SX), Math.round(360 * SY), Math.round(170 * SX), 0, Math.PI * 2);
      mctx.fill();
    }
  }

  // ─ 5. 地牢模式：顯示生成的其他房間節點 ─
  if (isDungeon && activeDungeonRooms) {
    const roomKeys = Object.keys(activeDungeonRooms);
    const total = roomKeys.length;
    roomKeys.forEach((rk, idx) => {
      const rm = activeDungeonRooms[rk];
      const isMe = (rk === currentRoomId);
      // 以格子方式排列在小地圖上
      const gx = 8 + (idx % 5) * 14;
      const gy = 6 + Math.floor(idx / 5) * 14;
      mctx.fillStyle = isMe
        ? '#ffd700'
        : rm.isBossRoom
          ? '#ef4444'
          : rm.cleared
            ? '#2a4a20'
            : '#1e3a5a';
      mctx.fillRect(gx, gy, 10, 10);
      if (rm.isBossRoom) {
        mctx.fillStyle = '#fff';
        mctx.font = '6px sans-serif';
        mctx.textAlign = 'center';
        mctx.fillText('B', gx + 5, gy + 8);
      }
    });
  }

  // ─ 6. NPC 藍點（宗門模式） ─
  if (isSect && area && area.npcs) {
    mctx.fillStyle = '#38bdf8';
    for (const npc of area.npcs) {
      mctx.beginPath();
      mctx.arc(
        Math.round((npc.x || 640) * SX),
        Math.round((npc.y || 360) * SY),
        2.5, 0, Math.PI * 2
      );
      mctx.fill();
    }
  }

  // ─ 7. 怪物紅點（地牢模式） ─
  if (typeof getEnemies === 'function') {
    const enemies = getEnemies();
    mctx.fillStyle = '#ef4444';
    for (const e of enemies) {
      if (!e || !e.alive) continue;
      mctx.beginPath();
      mctx.arc(
        Math.round(e.x * SX), Math.round(e.y * SY),
        e.isBoss ? 3.5 : 2, 0, Math.PI * 2
      );
      mctx.fill();
    }
  }

  // ─ 8. 玩家金色三角羅盤標記 ─
  const px = Math.round(P.x * SX);
  const py = Math.round(P.y * SY);
  // 外圈發光
  mctx.shadowColor = '#ffd700';
  mctx.shadowBlur = 4;
  mctx.fillStyle = '#ffd700';
  mctx.beginPath();
  // 方向三角形（朝向 P.facing）
  const fx = P.facing.x, fy = P.facing.y;
  const sz = 3.5;
  const angle = Math.atan2(fy, fx);
  mctx.save();
  mctx.translate(px, py);
  mctx.rotate(angle);
  mctx.beginPath();
  mctx.moveTo(sz * 1.5, 0);
  mctx.lineTo(-sz, sz);
  mctx.lineTo(-sz, -sz);
  mctx.closePath();
  mctx.fill();
  mctx.restore();
  mctx.shadowBlur = 0;

  // ─ 9. 更新地圖文字標籤（截短房間名稱）─
  const lbl = document.getElementById('mm-lbl');
  if (lbl) {
    const name = area ? (area.name || '未知區域') : '未知區域';
    // 只取最後的「·」後面的部分，或截短顯示
    const short = name.includes('·') ? name.split('·').pop().trim() : name;
    lbl.textContent = short.length > 10 ? short.slice(0, 10) + '…' : short;
  }
}


function gameLoop(ts) {
  try {
    const dt = Math.min((ts - lastT) / 1000, .05); lastT = ts; T += dt;

    if (!isRoomTransitioning) {
      // ─── 玩家移動：使用動態邊界（根據房間門洞決定哪個方向可穿越）───
      const bounds = getPlayerBounds();

      let moved = false;
      if (keys['w'] || keys['arrowup']) {
        P.y = Math.max(bounds.minY, P.y - P.speed);
        P.facing.y = -1; P.facing.x = 0; moved = true;
      }
      if (keys['s'] || keys['arrowdown']) {
        P.y = Math.min(bounds.maxY, P.y + P.speed);
        P.facing.y = 1; P.facing.x = 0; moved = true;
      }
      if (keys['a'] || keys['arrowleft']) {
        P.x = Math.max(bounds.minX, P.x - P.speed);
        P.facing.x = -1; P.facing.y = 0; moved = true;
      }
      if (keys['d'] || keys['arrowright']) {
        P.x = Math.min(bounds.maxX, P.x + P.speed);
        P.facing.x = 1; P.facing.y = 0; moved = true;
      }

      if (moved && P.state === 'IDLE') P.state = 'WALK';
      if (!moved && P.state === 'WALK') P.state = 'IDLE';

      // 充能計時器
      if (P.charging) P.chargeTime += dt;

      // 🛡️ 碰撞解析：玩家 vs 場景物件 / NPC / 怪物（移動後立即修正位置）
      try {
        if (typeof resolveAllPlayerCollisions === 'function') resolveAllPlayerCollisions();
      } catch(e) {}
    }

    updRoomTransition();

    try { if (typeof updEnemies === 'function') updEnemies(); else if (window.updEnemies) window.updEnemies(); } catch(e) {}
    try { if (typeof updAttacks === 'function') updAttacks(); else if (window.updAttacks) window.updAttacks(); } catch(e) {}
    try { if (typeof checkDoorTriggers === 'function') checkDoorTriggers(); else if (window.checkDoorTriggers) window.checkDoorTriggers(); } catch(e) {}

    // 每幀同步 HUD（確保 HP/Qi/Exp 條即時更新）
    if (typeof updateHUD === 'function') { try { updateHUD(); } catch(e) {} }

    render();
  } catch (err) {
    console.error('Safe Game Loop caught error:', err);
  }
  requestAnimationFrame(gameLoop);
}

// 顯式掛載至 window 全域物件
window.render = render;
window.gameLoop = gameLoop;
window.triggerAttack = triggerAttack;
window.quickPill = quickPill;
window.doDash = doDash;
window.toggleMed = toggleMed;
window.triggerRoomTransition = triggerRoomTransition;
window.updRoomTransition = updRoomTransition;
window.drawNPCs = drawNPCs;
window.drawEnemies = drawEnemies;

// 鍵盤監聽 (支援 ESC 鍵關閉所有 Modal 彈窗)
const keys = {};
window.addEventListener('keydown', e => {
  const k = e.key === 'ArrowUp' ? 'w' : e.key === 'ArrowDown' ? 's' : e.key === 'ArrowLeft' ? 'a' : e.key === 'ArrowRight' ? 'd' : e.key.toLowerCase();
  keys[k] = true;
  if (e.code === 'Space') keys['j'] = true;
  if (e.key === 'Shift') keys['l'] = true;

  // ESC 鍵關閉所有彈窗
  if (e.key === 'Escape' || e.code === 'Escape') {
    if (typeof closeAllModals === 'function') closeAllModals();
    else if (window.closeAllModals) window.closeAllModals();
  }

  if (e.key === '1') { P.weapon = { name: '精鋼長劍', type: 'SWORD', elem: 'GOLD', atk: 15 }; notify('🗡️ 裝備快捷1：切換【精鋼長劍】 (近戰 1 格)'); updateHUD(); }
  if (e.key === '2') { P.weapon = { name: '赤焰長槍', type: 'SPEAR', elem: 'FIRE', atk: 40 }; notify('🔱 裝備快捷2：切換【赤焰長槍】 (貫穿 3 格)'); updateHUD(); }
  if (e.key === '3') { P.weapon = { name: '寒冰飛鏢', type: 'DART', elem: 'WATER', atk: 35 }; notify('🎯 裝備快捷3：切換【寒冰飛鏢】 (遠程 10 格)'); updateHUD(); }
  if (e.key === '4') quickPill('pill_hp');
  if (e.key === '5') quickPill('pill_qi');
  if (e.key === '6') quickPill('pill_break');
  if (e.key === '7' || e.key === 'i' || e.key === 'b') openInv();
  if (e.key === '8' || e.key === 'c' || e.key === 'u') openSutraUI();
  if (e.key === '9' || e.key === 'm') openMap();
  if (e.key === 'g' || e.key === 'G') openForge();
  if (e.key === 'v' || e.key === 'V') openAlchemy();
  if (e.key === '?') openHelp();

  // 🗣️ F 鍵專用：當對話框開啟時，按下 F 鍵推進對話
  if (e.key === 'f' || e.key === 'F') {
    const dlgBox = document.getElementById('dlg-box');
    if (dlgBox && dlgBox.style.display === 'block') {
      if (typeof advanceDlg === 'function') advanceDlg();
    }
  }

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

function bootEngine() {
  initEngine();
  if (typeof updateHUD === 'function') updateHUD();
  requestAnimationFrame(gameLoop);
}

if (document.readyState === 'loading') {
  window.addEventListener('DOMContentLoaded', bootEngine);
} else {
  bootEngine();
}
