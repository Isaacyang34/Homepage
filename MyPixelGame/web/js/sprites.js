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

// HD 角色圖集載入
let hdPlayerImg = new Image();
let hdPlayerLoaded = false;
hdPlayerImg.onload = () => { hdPlayerLoaded = true; };
hdPlayerImg.src = 'assets/sprites/player_hd_transparent.png';

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
