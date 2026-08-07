// ═══════════════════════════════════════════════════════════
//  碰撞系統模組 (Collision System)
//  【AABB + Circle 混合碰撞，涵蓋：玩家、怪物、NPC、場景物件】
//  所有物件統一以圓形碰撞體 (Circle Collider) 表示，
//  場景靜態物件轉換為矩形碰撞體 (AABB Rect Collider)
// ═══════════════════════════════════════════════════════════

// ──────────────────────────────────────────────
// 1. 碰撞尺寸常數定義表 (Collision Radii Table)
// ──────────────────────────────────────────────
const COLL_R = {
  PLAYER:  20,   // 玩家圓形碰撞半徑 (px)
  MOB:     24,   // 普通怪物碰撞半徑
  BOSS:    45,   // BOSS 碰撞半徑
  NPC:     28,   // NPC 碰撞半徑（不可穿越）
};

// ──────────────────────────────────────────────
// 2. 各宗門房間靜態場景碰撞體定義 (Static AABB Colliders)
//    格式：{ x, y, w, h, label }
//    原點 (x,y) = 矩形左上角
// ──────────────────────────────────────────────
const STATIC_COLLIDERS = {
  sect_alchemy: [
    // 九轉八卦煉丹鼎：圓形半徑 50 → AABB 保守框
    { x: 590, y: 170, w: 100, h: 100, label: '煉丹鼎' },
  ],
  sect_forge: [
    // 地火熾熱熔爐：矩形主體
    { x: 575, y: 160, w: 130, h: 110, label: '地火鐵砧' },
  ],
  sect_market: [
    // 仙緣集市櫃檯
    { x: 520, y: 180, w: 240, h: 60,  label: '集市櫃檯' },
  ],
  sect_spring: [
    // 靈泉水池（圓形半徑 180，用有效阻擋的外矩形框住邊緣只讓人在外圈）
    // 改以圓形阻擋：由 circleVsCircle 處理，這裡留空 AABB，動態碰撞另設
  ],
  sect_meditate: [
    // 打坐蒲團：圓形半徑 60 → 中心 AABB
    { x: 580, y: 300, w: 120, h: 120, label: '打坐蒲團' },
  ],
  // 地牢房間無靜態場景碰撞體（空曠戰場）
};

// 靈泉池以圓形碰撞處理（不使用 AABB）
const CIRCLE_STATIC_COLLIDERS = {
  sect_spring: [
    { cx: 640, cy: 360, r: 170, label: '靈泉水池' },
  ],
};

// ──────────────────────────────────────────────
// 3. 基本碰撞計算工具函數
// ──────────────────────────────────────────────

/**
 * 圓形 vs AABB 矩形碰撞解析
 * @param {number} cx - 圓心 X
 * @param {number} cy - 圓心 Y
 * @param {number} cr - 圓形半徑
 * @param {object} rect - {x, y, w, h}
 * @returns {{px:number, py:number}|null} 推開向量，null 代表無碰撞
 */
function circleVsAABB(cx, cy, cr, rect) {
  // 找矩形上最近的點
  const nearX = Math.max(rect.x, Math.min(cx, rect.x + rect.w));
  const nearY = Math.max(rect.y, Math.min(cy, rect.y + rect.h));

  const dx = cx - nearX;
  const dy = cy - nearY;
  const distSq = dx * dx + dy * dy;

  if (distSq >= cr * cr) return null; // 無碰撞

  const dist = Math.sqrt(distSq) || 0.001;
  const overlap = cr - dist;
  return { px: (dx / dist) * overlap, py: (dy / dist) * overlap };
}

/**
 * 圓形 vs 圓形碰撞解析
 * @param {number} ax, ay, ar - 圓 A 座標與半徑
 * @param {number} bx, by, br - 圓 B 座標與半徑
 * @returns {{px:number, py:number}|null} A 要被推開的向量
 */
function circleVsCircle(ax, ay, ar, bx, by, br) {
  const dx = ax - bx;
  const dy = ay - by;
  const dist = Math.hypot(dx, dy) || 0.001;
  const minDist = ar + br;

  if (dist >= minDist) return null; // 無碰撞

  const overlap = minDist - dist;
  return { px: (dx / dist) * overlap, py: (dy / dist) * overlap };
}

// ──────────────────────────────────────────────
// 4. 靜態場景碰撞解析 (Static Scenery Collision)
//    輸入目標中心座標與半徑，輸出修正後座標
// ──────────────────────────────────────────────
function resolveStaticCollision(cx, cy, cr, roomId) {
  let nx = cx, ny = cy;

  // 4a. AABB 矩形場景碰撞
  const rects = STATIC_COLLIDERS[roomId] || [];
  for (const rect of rects) {
    const push = circleVsAABB(nx, ny, cr, rect);
    if (push) {
      nx += push.px;
      ny += push.py;
    }
  }

  // 4b. 圓形靜態場景碰撞（靈泉池）
  const circles = CIRCLE_STATIC_COLLIDERS[roomId] || [];
  for (const sc of circles) {
    // 對靜態圓形的碰撞：若玩家圓心在靜態圓內則推到外緣
    const dx = nx - sc.cx;
    const dy = ny - sc.cy;
    const dist = Math.hypot(dx, dy) || 0.001;
    const minDist = sc.r + cr; // 靜態圓邊緣 + 碰撞體半徑
    if (dist < minDist) {
      const overlap = minDist - dist;
      nx += (dx / dist) * overlap;
      ny += (dy / dist) * overlap;
    }
  }

  return { x: nx, y: ny };
}

// ──────────────────────────────────────────────
// 5. NPC 碰撞解析 (Player vs NPC)
//    玩家不能穿越 NPC 身體
// ──────────────────────────────────────────────
function resolveNPCCollision(cx, cy, cr) {
  let nx = cx, ny = cy;
  if (typeof SECT_ROOMS === 'undefined' || !SECT_ROOMS[curAreaId]) return { x: nx, y: ny };
  const npcs = SECT_ROOMS[curAreaId].npcs || [];

  for (const npc of npcs) {
    const push = circleVsCircle(nx, ny, cr, npc.x || 640, npc.y || 360, COLL_R.NPC);
    if (push) {
      nx += push.px;
      ny += push.py;
    }
  }
  return { x: nx, y: ny };
}

// ──────────────────────────────────────────────
// 6. 玩家與怪物碰撞解析 (Player vs Enemies)
//    玩家和怪物互相推開，雙方都受影響
// ──────────────────────────────────────────────
function resolvePlayerEnemyCollision() {
  if (typeof getEnemies !== 'function') return;
  const enemies = getEnemies();

  for (const e of enemies) {
    if (!e || !e.alive) continue;
    const er = e.isBoss ? COLL_R.BOSS : COLL_R.MOB;
    const push = circleVsCircle(P.x, P.y, COLL_R.PLAYER, e.x, e.y, er);
    if (push) {
      // 玩家被推開 100%（玩家讓路，怪物不動）
      P.x += push.px;
      P.y += push.py;
    }
  }
}

// ──────────────────────────────────────────────
// 7. 怪物之間碰撞解析 (Enemy vs Enemy)
//    怪物不能互相堆疊，彼此推開各 50%
// ──────────────────────────────────────────────
function resolveEnemyMutualCollision() {
  if (typeof getEnemies !== 'function') return;
  const enemies = getEnemies();

  for (let i = 0; i < enemies.length; i++) {
    const a = enemies[i];
    if (!a || !a.alive) continue;
    const ar = a.isBoss ? COLL_R.BOSS : COLL_R.MOB;

    for (let j = i + 1; j < enemies.length; j++) {
      const b = enemies[j];
      if (!b || !b.alive) continue;
      const br = b.isBoss ? COLL_R.BOSS : COLL_R.MOB;

      const push = circleVsCircle(a.x, a.y, ar, b.x, b.y, br);
      if (push) {
        // 雙方各推開 50%
        a.x += push.px * 0.5;
        a.y += push.py * 0.5;
        b.x -= push.px * 0.5;
        b.y -= push.py * 0.5;

        // 推開後確保怪物也在地圖邊界內
        a.x = Math.max(64, Math.min(1216, a.x));
        a.y = Math.max(64, Math.min(656, a.y));
        b.x = Math.max(64, Math.min(1216, b.x));
        b.y = Math.max(64, Math.min(656, b.y));
      }
    }
  }
}

// ──────────────────────────────────────────────
// 8. 單顆怪物的靜態場景碰撞解析
// ──────────────────────────────────────────────
function resolveEnemyStaticCollision(e) {
  const er = e.isBoss ? COLL_R.BOSS : COLL_R.MOB;
  const res = resolveStaticCollision(e.x, e.y, er, curAreaId);
  e.x = res.x;
  e.y = res.y;
}

// ──────────────────────────────────────────────
// 9. 完整玩家碰撞解析（一次呼叫解決所有層）
//    依序：場景物件 → NPC → 怪物
// ──────────────────────────────────────────────
function resolveAllPlayerCollisions() {
  // 9a. 靜態場景物件（丹爐、鐵砧、集市櫃檯、靈泉池、打坐蒲團）
  const s1 = resolveStaticCollision(P.x, P.y, COLL_R.PLAYER, curAreaId);
  P.x = s1.x; P.y = s1.y;

  // 9b. NPC 碰撞（只有宗門房間有 NPC）
  const s2 = resolveNPCCollision(P.x, P.y, COLL_R.PLAYER);
  P.x = s2.x; P.y = s2.y;

  // 9c. 玩家與怪物碰撞（地牢房間）
  resolvePlayerEnemyCollision();

  // 9d. 最終邊界 clamp（防止碰撞推出地圖外）
  P.x = Math.max(64, Math.min(1216, P.x));
  P.y = Math.max(64, Math.min(656, P.y));
}

// ──────────────────────────────────────────────
// 10. 全體怪物碰撞解析（場景 + 互相）
// ──────────────────────────────────────────────
function resolveAllEnemyCollisions() {
  if (typeof getEnemies !== 'function') return;
  const enemies = getEnemies();

  // 10a. 每隻怪物對靜態場景碰撞
  for (const e of enemies) {
    if (!e || !e.alive) continue;
    resolveEnemyStaticCollision(e);
  }

  // 10b. 怪物之間互相碰撞（分離堆疊）
  resolveEnemyMutualCollision();
}

// 掛載全域物件供外部呼叫
window.CollisionSystem = {
  COLL_R,
  STATIC_COLLIDERS,
  CIRCLE_STATIC_COLLIDERS,
  circleVsAABB,
  circleVsCircle,
  resolveStaticCollision,
  resolveNPCCollision,
  resolvePlayerEnemyCollision,
  resolveEnemyMutualCollision,
  resolveEnemyStaticCollision,
  resolveAllPlayerCollisions,
  resolveAllEnemyCollisions,
};

// 快捷函數供外部直接呼叫
window.resolveAllPlayerCollisions = resolveAllPlayerCollisions;
window.resolveAllEnemyCollisions  = resolveAllEnemyCollisions;
