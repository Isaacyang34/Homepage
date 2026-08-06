// ═══════════════════════════════════════════════════════════
//  精靈與像素畫數據定義 (Sprite & Pixel Graphics)
// ═══════════════════════════════════════════════════════════

const _ = null;
const IDLE0 = [
  [_,_,_,_,_,_,_,_,_,_,_,_,_,_],
  [_,_,_,_,_,_,_,_,_,_,_,_,_,_],
  [_,_,_,_,_,'#d4a843','#d4a843',_,_,_,_,_,_,_],
  [_,_,_,_,_,'#f0c060','#f0c060',_,_,_,_,_,_,_],
  [_,_,_,_,'#2c3e50','#f0d0b0','#f0d0b0','#2c3e50',_,_,_,_,_,_],
  [_,_,_,_,'#2c3e50','#f0d0b0','#f0d0b0','#2c3e50',_,_,_,_,_,_],
  [_,_,_,_,_,'#f0d0b0','#f0d0b0',_,_,_,_,_,_,_],
  [_,_,_,_,'#ffffff','#ffffff','#ffffff','#ffffff',_,_,_,_,_,_],
  [_,_,_,'#ffffff','#ffffff','#ffffff','#ffffff','#ffffff',_,_,_,_,_,_],
  [_,_,_,'#ffffff','#d4a843','#d4a843','#ffffff','#ffffff',_,_,_,_,_,_],
  [_,_,_,'#1a252f','#ffffff','#ffffff','#ffffff','#1a252f',_,_,_,_,_,_],
  [_,_,_,'#1a252f','#1a252f','#1a252f','#1a252f','#1a252f',_,_,_,_,_,_],
  [_,_,_,_,'#1a252f',_,_,'#1a252f',_,_,_,_,_,_],
  [_,_,_,_,'#1a252f',_,_,'#1a252f',_,_,_,_,_,_],
];

// ─── HD 圖片載入工具 ───

/**
 * 載入圖片並透過像素處理去除背景色
 * @param {string} src       圖片路徑
 * @param {'white'|'black'} bgType  背景色類型
 * @param {number} threshold 顏色閾值（預設 30）
 * @returns {{ canvas: HTMLCanvasElement|null, loaded: boolean }}
 */
function loadHDImageProcessed(src, bgType = 'white', threshold = 30) {
  const ref = { canvas: null, loaded: false };
  const img = new Image();
  img.crossOrigin = 'anonymous';
  img.onload = () => {
    const oc = document.createElement('canvas');
    oc.width = img.naturalWidth;
    oc.height = img.naturalHeight;
    const oc2d = oc.getContext('2d');
    oc2d.drawImage(img, 0, 0);
    const id = oc2d.getImageData(0, 0, oc.width, oc.height);
    const d = id.data;
    for (let i = 0; i < d.length; i += 4) {
      const r = d[i], g = d[i+1], b = d[i+2];
      if (bgType === 'white') {
        // 去白背景：高亮且接近中性色
        if (r > 220 && g > 220 && b > 220) d[i+3] = 0;
      } else {
        // 去黑背景：三通道皆低
        if (r < threshold && g < threshold && b < threshold) d[i+3] = 0;
      }
    }
    oc2d.putImageData(id, 0, 0);
    ref.canvas = oc;
    ref.loaded = true;
  };
  img.onerror = () => console.warn('[HD] 圖片載入失敗:', src);
  img.src = src;
  return ref;
}

// ─── HD 玩家圖集載入（白背景去背）───
const hdPlayerRef = loadHDImageProcessed('assets/sprites/player_hd.png', 'white', 30);

// 相容舊邏輯的代理屬性
Object.defineProperty(window, 'hdPlayerLoaded', { get: () => hdPlayerRef.loaded });
Object.defineProperty(window, 'hdPlayerImg', { get: () => hdPlayerRef.canvas });

// ─── 玩家 HD Spritesheet 幀座標定義 ───
// 圖片佈局：4欄 × 2列，無格線無文字
// Row 0: IDLE, WALK1, WALK2, WALK3
// Row 1: IDLE-S（持劍）, ATK-UP（蓄力）, ATK1（劍擊爆發）, ATK2（收劍）
const PLAYER_FRAMES = {
  IDLE:   { col: 0, row: 0 },
  WALK1:  { col: 1, row: 0 },
  WALK2:  { col: 2, row: 0 },
  WALK3:  { col: 3, row: 0 },
  IDLE1:  { col: 0, row: 1 },
  ATK_UP: { col: 1, row: 1 },
  ATK1:   { col: 2, row: 1 },
  ATK2:   { col: 3, row: 1 },
};
const PLAYER_SHEET_COLS = 4;
const PLAYER_SHEET_ROWS = 2;


// ─── HD BOSS 圖集載入 ───
const HD_BOSS = {};

function loadHDBoss(key, src, frames) {
  const img = new Image();
  img.onload = () => { HD_BOSS[key].loaded = true; };
  img.src = src;
  HD_BOSS[key] = { img, loaded: false, frames };
}

// 赤焰魔狼皇：4 幀橫排 (IDLE / ROAR / ATTACK / DEATH)
loadHDBoss('wolf', 'assets/sprites/boss_wolf_hd.png', {
  cols: 4, rows: 2,
  anim: {
    IDLE:   [{ col: 0, row: 0 }],
    ROAR:   [{ col: 1, row: 0 }],
    ATTACK: [{ col: 2, row: 0 }],
    DEATH:  [{ col: 3, row: 0 }],
  }
});

// 虛空蒼龍：單幀全圖
loadHDBoss('dragon', 'assets/sprites/boss_dragon_hd.png', {
  cols: 1, rows: 1,
  anim: {
    IDLE:   [{ col: 0, row: 0 }],
    ATTACK: [{ col: 0, row: 0 }],
  }
});

// 邪魔老祖：3 幀橫排 (IDLE / CAST / ENRAGED)
loadHDBoss('sorcerer', 'assets/sprites/boss_sorcerer_hd.png', {
  cols: 3, rows: 2,
  anim: {
    IDLE:    [{ col: 0, row: 0 }],
    CAST:    [{ col: 1, row: 0 }],
    ENRAGED: [{ col: 2, row: 0 }],
  }
});

// aiType → boss key 對照
const BOSS_SPRITE_MAP = {
  boss:     'wolf',
  dragon:   'dragon',
  sorcerer: 'sorcerer',
};

/**
 * 繪製 HD 圖集（精確透明圖檔渲染與座標對齊）
 * @param {HTMLImageElement|HTMLCanvasElement} img   來源圖片或 Canvas
 * @param {number} col             幀欄號 (0-based)
 * @param {number} row             幀列號 (0-based)
 * @param {number} totalCols       圖集總欄數
 * @param {number} totalRows       圖集總列數
 * @param {number} dx              繪製目標 X
 * @param {number} dy              繪製目標 Y
 * @param {number} dw              繪製寬度
 * @param {number} dh              繪製高度
 * @param {boolean} [flipX=false]  水平翻轉
 * @param {CanvasRenderingContext2D} [ctxRef]  目標 ctx
 * @param {boolean} [useScreen=false] 是否使用 Screen 混合（黑底 Boss 用）
 */
function drawHDFrame(img, col, row, totalCols, totalRows, dx, dy, dw, dh, flipX = false, ctxRef = null, useScreen = false) {
  const c = ctxRef || window.ctx;
  if (!c || !img) return;
  const fw = (img.naturalWidth || img.width) / totalCols;
  const fh = (img.naturalHeight || img.height) / totalRows;
  const sx = col * fw, sy = row * fh;

  c.save();
  if (useScreen) {
    c.globalCompositeOperation = 'screen';
  } else {
    c.globalCompositeOperation = 'source-over';
  }

  if (flipX) {
    c.scale(-1, 1);
    c.drawImage(img, sx, sy, fw, fh, -(dx + dw), dy, dw, dh);
  } else {
    c.drawImage(img, sx, sy, fw, fh, dx, dy, dw, dh);
  }
  c.globalCompositeOperation = 'source-over';
  c.restore();
}

/**
 * 繪製 BOSS HD 精靈
 * @param {object} e      敵人物件（含 aiType, hp, maxHp, facingLeft）
 * @param {string} animKey 動作名稱 'IDLE'|'ATTACK'|'ROAR'|'DEATH'...
 * @param {number} frameT  動畫計時器 T（用於多幀輪播）
 */
function drawBossHD(e, animKey, frameT) {
  const bKey = BOSS_SPRITE_MAP[e.aiType] || 'wolf';
  const bData = HD_BOSS[bKey];
  if (!bData || !bData.loaded) return false; // 未載入，回傳 false 讓呼叫端 fallback
  const anim = bData.frames.anim[animKey] || bData.frames.anim['IDLE'];
  const frameIdx = Math.floor(frameT * 6) % anim.length;
  const { col, row } = anim[frameIdx];
  // BOSS 在畫面上顯示尺寸：60×72
  drawHDFrame(bData.img, col, row, bData.frames.cols, bData.frames.rows,
    e.x - 30, e.y - 40, 60, 72, e.facingLeft, null, true);
  return true;
}

function fixSpr(grid) {
  const maxW = Math.max(...grid.map(r => r.length));
  return grid.map(r => {
    const nr = [...r];
    while (nr.length < maxW) nr.push(_);
    return nr;
  });
}

const ES_WOLF = [
  [_,_,_,_,'#990000','#990000',_,_,_],
  [_,_,'#cc0000','#ff2200','#cc0000','#ff2200','#cc0000',_,_],
  [_,'#cc0000','#ff4400','#ffff00','#ff4400','#ffff00','#ff4400','#cc0000',_],
  [_,'#cc0000','#ff4400','#ff4400','#ff4400','#ff4400','#ff4400','#cc0000',_],
  [_,_,'#cc0000','#ff2200','#ff2200','#ff2200','#cc0000',_,_],
  ['#990000','#cc0000','#ff2200','#ff2200','#ff2200','#ff2200','#ff2200','#cc0000','#990000'],
];

const ES_SPIDER = [
  ['#2ecc71',_,_,'#27ae60','#27ae60',_,_,'#2ecc71'],
  [_,'#2ecc71',_,'#2ecc71','#2ecc71',_,'#2ecc71',_],
  [_,_,'#27ae60','#e74c3c','#e74c3c','#27ae60',_,_],
  [_,'#27ae60','#2ecc71','#2ecc71','#2ecc71','#2ecc71','#27ae60',_],
  ['#2ecc71',_,'#27ae60',_,_,'#27ae60',_,'#2ecc71'],
];

const ES_MOLE = [
  [_,_,_,'#7f8c8d','#7f8c8d',_,_,_],
  [_,_,'#7f8c8d','#bdc3c7','#bdc3c7','#7f8c8d',_,_],
  [_,'#7f8c8d','#e74c3c','#bdc3c7','#e74c3c','#bdc3c7','#7f8c8d',_],
  ['#7f8c8d','#bdc3c7','#bdc3c7','#bdc3c7','#bdc3c7','#bdc3c7','#bdc3c7','#7f8c8d'],
];

const ES_GOLEM = [
  [_,_,'#8e44ad','#8e44ad','#8e44ad','#8e44ad',_,_],
  [_,'#8e44ad','#9b59b6','#9b59b6','#9b59b6','#9b59b6','#8e44ad',_],
  ['#8e44ad','#9b59b6','#f1c40f','#9b59b6','#f1c40f','#9b59b6','#8e44ad'],
  ['#8e44ad','#9b59b6','#9b59b6','#9b59b6','#9b59b6','#9b59b6','#8e44ad'],
];

const ES_BAT = [
  ['#3498db',_,_,_,_,_,_,'#3498db'],
  ['#3498db','#2980b9',_,_,'#2980b9','#3498db'],
  [_,_,'#2980b9','#e74c3c','#2980b9',_,_],
  [_,_,_,'#2980b9',_,_,_],
];

const ES_GUARD = [
  [_,_,'#3a0060','#5a00a0','#7000d0','#7000d0','#5a00a0','#3a0060',_,_],
  ['#2a0040','#5a00a0','#9000e0','#b000ff','#b000ff','#9000e0','#5a00a0','#2a0040'],
  ['#3a0060','#9000e0','#ff4488','#9000e0','#9000e0','#ff4488','#9000e0','#3a0060'],
];

const ES_BOSS = [
  [_,_,'#d4a843',_,'#990000','#990000',_,'#990000','#990000',_,'#d4a843',_,_],
  [_,'#d4a843','#ff2200','#cc0000','#ff2200','#ff2200','#cc0000','#ff2200','#ff2200','#d4a843',_,_],
  ['#cc0000','#ff4400','#ffaa00','#ff4400','#990000','#ff4400','#ffaa00','#ff4400','#cc0000',_,_],
  ['#ff2200','#ff4400','#ffff00','#ff4400','#ff2200','#ff4400','#ffff00','#ff4400','#ff2200',_,_],
];

const ES = {
  wolf: fixSpr(ES_WOLF),
  spider: fixSpr(ES_SPIDER),
  mole: fixSpr(ES_MOLE),
  golem: fixSpr(ES_GOLEM),
  bat: fixSpr(ES_BAT),
  guard: fixSpr(ES_GUARD),
  boss: fixSpr(ES_BOSS),
};

function drawSprite(spr, x, y, scale = 2, flipX = false, tint = null, ctxRef = null) {
  const c = ctxRef || window.ctx;
  if (!c || !spr) return;
  spr.forEach((row, ry) => {
    row.forEach((col, rx) => {
      if (!col) return;
      const px = flipX ? x + (row.length - 1 - rx) * scale : x + rx * scale;
      const py = y + ry * scale;
      c.fillStyle = tint || col;
      c.fillRect(Math.round(px), Math.round(py), scale, scale);
    });
  });
}
