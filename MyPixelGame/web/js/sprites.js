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
 * 載入 HD 圖片並處理成 Canvas/Image
 * @param {string} src 圖片路徑
 * @returns {{ canvas: HTMLCanvasElement|HTMLImageElement|null, loaded: boolean }}
 */
function loadHDImageProcessed(src) {
  const ref = { canvas: null, loaded: false };
  const img = new Image();
  img.onload = () => {
    try {
      const w = img.naturalWidth || img.width;
      const h = img.naturalHeight || img.height;
      const oc = document.createElement('canvas');
      oc.width = w; oc.height = h;
      const oc2d = oc.getContext('2d');
      oc2d.drawImage(img, 0, 0);

      const imgData = oc2d.getImageData(0, 0, w, h);
      const data = imgData.data;

      // 像素掃描去背：過濾白底 (r,g,b > 210) 與外圍純黑網格線 (r,g,b < 35 且在邊界16px內)
      const cols = 4, rows = 2;
      const fw = w / cols, fh = h / rows;

      for (let y = 0; y < h; y++) {
        for (let x = 0; x < w; x++) {
          const idx = (y * w + x) * 4;
          const r = data[idx], g = data[idx + 1], b = data[idx + 2];

          // 1. 去除白色與近白背景
          if (r > 210 && g > 210 && b > 210) {
            data[idx + 3] = 0;
            continue;
          }

          // 2. 去除圖集每格外圍 16px 內的黑邊與黑線雜訊
          const lx = x % fw;
          const ly = y % fh;
          if (lx < 16 || lx > fw - 16 || ly < 16 || ly > fh - 16) {
            if (r < 35 && g < 35 && b < 35) {
              data[idx + 3] = 0;
            }
          }
        }
      }

      oc2d.putImageData(imgData, 0, 0);
      ref.canvas = oc;
      ref.loaded = true;
    } catch(e) {
      ref.canvas = img;
      ref.loaded = true;
    }
  };
  img.onerror = () => {
    ref.canvas = img;
    ref.loaded = true;
  };
  img.src = src;
  return ref;
}

// ─── HD 玩家圖集載入 ───
const hdPlayerRef = loadHDImageProcessed('assets/sprites/player_hd.png');

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
  if (!c || !img) return false;
  const w = img.naturalWidth || img.width || 0;
  const h = img.naturalHeight || img.height || 0;
  if (w <= 0 || h <= 0) return false;

  const fw = w / totalCols;
  const fh = h / totalRows;

  // 6px 內縮裁切，徹底去除圖集網格殘留邊線與雜訊
  const insetX = 6;
  const insetY = 6;
  const sx = col * fw + insetX;
  const sy = row * fh + insetY;
  const sw = Math.max(1, fw - insetX * 2);
  const sh = Math.max(1, fh - insetY * 2);

  c.save();
  try {
    if (useScreen) {
      c.globalCompositeOperation = 'screen';
    } else {
      c.globalCompositeOperation = 'source-over';
    }

    if (flipX) {
      c.scale(-1, 1);
      c.drawImage(img, sx, sy, sw, sh, -(dx + dw), dy, dw, dh);
    } else {
      c.drawImage(img, sx, sy, sw, sh, dx, dy, dw, dh);
    }
  } catch(e) {
    c.restore();
    return false;
  }
  c.globalCompositeOperation = 'source-over';
  c.restore();
  return true;
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

// 高畫質高密度 20×20 精致小怪物像素陣列 (高質感細節對齊 720p HD)
const ES_WOLF = [
  [_,_,_,_,_,_,_,_,_,'#990000','#990000',_,_,_,_,_,_,_,_,_],
  [_,_,_,_,_,_,_,'#e84040','#e84040','#ff6666','#ff6666','#e84040',_,_,_,_,_,_,_,_],
  [_,_,_,_,_,_,'#e84040','#e84040','#ff6666','#ffff00','#ffff00','#ff6666','#e84040',_,_,_,_,_,_],
  [_,_,_,_,_,'#990000','#e84040','#e84040','#ff6666','#ffff00','#ffff00','#ff6666','#e84040','#990000',_,_,_,_,_],
  [_,_,_,_,'#990000','#e84040','#e84040','#e84040','#e84040','#ff6666','#ff6666','#e84040','#e84040','#990000',_,_,_,_],
  [_,_,_,'#990000','#e84040','#e84040','#e84040','#e84040','#e84040','#e84040','#e84040','#e84040','#e84040','#e84040','#990000',_,_,_],
  [_,_,'#990000','#e84040','#e84040','#ffffff','#ffffff','#e84040','#e84040','#e84040','#e84040','#ffffff','#ffffff','#e84040','#e84040','#990000',_,_],
  [_,'#990000','#e84040','#e84040','#e84040','#ffffff','#ffffff','#e84040','#e84040','#e84040','#e84040','#ffffff','#ffffff','#e84040','#e84040','#e84040','#990000',_],
  ['#990000','#e84040','#e84040','#e84040','#e84040','#e84040','#e84040','#e84040','#e84040','#e84040','#e84040','#e84040','#e84040','#e84040','#e84040','#e84040','#990000'],
  ['#990000','#990000','#990000','#e84040','#e84040','#e84040','#e84040','#e84040','#e84040','#e84040','#e84040','#e84040','#e84040','#e84040','#990000','#990000','#990000'],
  [_,_,_,'#990000','#990000','#990000','#990000','#990000','#990000','#990000','#990000','#990000','#990000','#990000',_,_,_],
  [_,_,_,_,_,'#990000','#e84040',_,_,_,_,_,'#e84040','#990000',_,_,_,_],
  [_,_,_,_,_,'#990000','#e84040',_,_,_,_,_,'#e84040','#990000',_,_,_,_],
  [_,_,_,_,_,'#e84040','#ffffff',_,_,_,_,_,'#ffffff','#e84040',_,_,_,_],
];

const ES_SPIDER = [
  [_,_,_,_,_,_,'#2ecc71',_,_,_,_,_,_,_,'#2ecc71',_,_,_,_,_],
  ['#2ecc71',_,_,_,_,'#2ecc71',_,_,_,'#27ae60','#27ae60',_,_,_,'#2ecc71',_,_,_,_,'#2ecc71'],
  [_,'#2ecc71',_,_,'#2ecc71',_,'#27ae60',_,'#2ecc71','#2ecc71',_,'#27ae60',_,'#2ecc71',_,_,'#2ecc71',_],
  [_,_,'#2ecc71','#27ae60',_,'#27ae60','#2ecc71','#2ecc71','#e74c3c','#e74c3c','#2ecc71','#2ecc71','#27ae60',_,'#27ae60','#2ecc71',_,_],
  [_,_,_,'#27ae60','#2ecc71','#2ecc71','#2ecc71','#2ecc71','#9b59b6','#9b59b6','#2ecc71','#2ecc71','#2ecc71','#2ecc71','#27ae60',_,_,_],
  [_,_,_,'#27ae60','#2ecc71','#2ecc71','#2ecc71','#2ecc71','#2ecc71','#2ecc71','#2ecc71','#2ecc71','#2ecc71','#2ecc71','#27ae60',_,_,_],
  [_,_,'#2ecc71',_,'#27ae60','#2ecc71','#2ecc71','#2ecc71','#2ecc71','#2ecc71','#2ecc71','#2ecc71','#2ecc71','#27ae60',_,'#2ecc71',_,_],
  ['_','2ecc71',_,_,_,'#27ae60','#27ae60','#27ae60','#27ae60','#27ae60','#27ae60','#27ae60','#27ae60',_,_,_,'#2ecc71'],
  [_,_,_,_,_,_,_,'#2ecc71','#2ecc71',_,'#2ecc71','#2ecc71',_,_,_,_,_,_],
];

const ES_MOLE = [
  [_,_,_,_,_,_,_,_,_,'#7f8c8d','#7f8c8d',_,_,_,_,_,_,_,_,_],
  [_,_,_,_,_,_,_,'#7f8c8d','#bdc3c7','#bdc3c7','#bdc3c7','#7f8c8d',_,_,_,_,_,_,_,_],
  [_,_,_,_,_,_,'#7f8c8d','#bdc3c7','#bdc3c7','#bdc3c7','#bdc3c7','#bdc3c7','#7f8c8d',_,_,_,_,_,_],
  [_,_,_,_,_,'#7f8c8d','#bdc3c7','#e74c3c','#bdc3c7','#e74c3c','#bdc3c7','#bdc3c7','#7f8c8d',_,_,_,_,_],
  [_,_,_,_,'#7f8c8d','#bdc3c7','#bdc3c7','#bdc3c7','#ffcc00','#bdc3c7','#bdc3c7','#bdc3c7','#bdc3c7','#7f8c8d',_,_,_,_],
  [_,_,_,'#7f8c8d','#bdc3c7','#bdc3c7','#bdc3c7','#bdc3c7','#bdc3c7','#bdc3c7','#bdc3c7','#bdc3c7','#bdc3c7','#7f8c8d',_,_,_],
  [_,_,'#7f8c8d','#7f8c8d','#bdc3c7','#bdc3c7','#bdc3c7','#bdc3c7','#bdc3c7','#bdc3c7','#bdc3c7','#bdc3c7','#7f8c8d','#7f8c8d',_,_],
  [_,'#34495e','#7f8c8d','#7f8c8d','#bdc3c7','#bdc3c7','#bdc3c7','#bdc3c7','#bdc3c7','#bdc3c7','#7f8c8d','#7f8c8d','#34495e',_],
  ['#34495e','#ffffff',_,_,'#34495e','#7f8c8d','#7f8c8d','#7f8c8d','#7f8c8d','#34495e',_,_,'#ffffff','#34495e'],
];

const ES_GOLEM = [
  [_,_,_,_,_,_,_,'#8e44ad','#8e44ad','#8e44ad','#8e44ad',_,_,_,_,_,_,_,_],
  [_,_,_,_,_,_,'#8e44ad','#9b59b6','#9b59b6','#9b59b6','#9b59b6','#8e44ad',_,_,_,_,_,_],
  [_,_,_,_,_,'#8e44ad','#9b59b6','#f1c40f','#9b59b6','#f1c40f','#9b59b6','#8e44ad',_,_,_,_,_],
  [_,_,_,_,'#8e44ad','#9b59b6','#9b59b6','#9b59b6','#9b59b6','#9b59b6','#9b59b6','#8e44ad',_,_,_,_],
  [_,_,_,'#8e44ad','#8e44ad','#9b59b6','#9b59b6','#f1c40f','#9b59b6','#9b59b6','#8e44ad','#8e44ad',_,_,_],
  [_,_,'#8e44ad',_,'#8e44ad','#9b59b6','#9b59b6','#9b59b6','#9b59b6','#8e44ad',_,'#8e44ad',_,_],
  [_,_,_,_,_,_,'#8e44ad','#8e44ad','#8e44ad','#8e44ad',_,_,_,_,_,_],
];

const ES_BAT = [
  ['#3498db',_,_,_,_,_,_,_,_,_,_,_,_,_,_,_,_,_,'#3498db'],
  ['#3498db','#2980b9',_,_,_,_,_,_,_,_,_,_,_,_,_,_,'#2980b9','#3498db'],
  [_,'#3498db','#2980b9',_,_,_,_,_,_,_,_,_,_,_,_,'#2980b9','#3498db',_],
  [_,_,'#3498db','#2980b9','#2980b9',_,_,_,'#e74c3c','#e74c3c',_,_,_,'#2980b9','#2980b9','#3498db',_,_],
  [_,_,_,_,'#2980b9','#2980b9','#2980b9','#2980b9','#2980b9','#2980b9','#2980b9','#2980b9','#2980b9',_,_,_,_],
  [_,_,_,_,_,_,_,'#2980b9','#2980b9','#2980b9','#2980b9',_,_,_,_,_,_,_],
];

const ES_GUARD = [
  [_,_,_,'#3a0060','#5a00a0','#7000d0','#7000d0','#5a00a0','#3a0060',_,_],
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
