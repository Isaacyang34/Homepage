// ═══════════════════════════════════════════════════════════
//  背景地質與場景獨立渲染器 (Background Renderer Module)
//  【Moonlighter 經典 45度正俯視 + 宗門功能房間與地牢地貌】
// ═══════════════════════════════════════════════════════════

window.BackgroundRenderer = {
  images: {},
  loaded: {},

  init() {
    // 同時載入：1. 四邊有門原圖 (bg_<theme>.png)  2. 四邊無門實心石牆原圖 (bg_<theme>_closed.png)
    const maps = {
      sect: 'assets/backgrounds/bg_sect.png',
      sect_closed: 'assets/backgrounds/bg_sect_closed.png',
      mountain: 'assets/backgrounds/bg_mountain.png',
      mountain_closed: 'assets/backgrounds/bg_mountain_closed.png',
      cave: 'assets/backgrounds/bg_cave.png',
      cave_closed: 'assets/backgrounds/bg_cave_closed.png',
      ruins: 'assets/backgrounds/bg_ruins.png',
      ruins_closed: 'assets/backgrounds/bg_ruins_closed.png'
    };
    for (let key in maps) {
      const img = new Image();
      img.onload = () => { this.loaded[key] = true; };
      img.src = maps[key];
      this.images[key] = img;
    }
  },

  draw(ctx, area) {
    if (!ctx) return;
    const terr = (area && area.terrain) ? area.terrain : 'sect';

    // 1. 繪製 45度正俯視 Top-Down HD 背景圖 (預設畫四門底圖)
    if (this.images[terr] && this.loaded[terr]) {
      ctx.drawImage(this.images[terr], 0, 0, 1280, 720);
    } else {
      this.drawFallbackTopDownFloor(ctx, terr);
    }

    // 2. 若為宗門特定功能房間，繪製專屬造景 (丹爐/鍛造台/靈泉池/蒲團)
    if (area && area.id) {
      this.drawSectRoomScenery(ctx, area.id);
    }

    // 3. 繪製門通道與無門實心封牆覆蓋
    this.drawDoors(ctx, area);
  },

  // 宗門特定房間特化造景
  drawSectRoomScenery(ctx, roomId) {
    ctx.save();
    if (roomId === 'sect_alchemy') {
      // 煉丹房：中央九轉八卦巨型煉丹鼎與紫焰
      ctx.fillStyle = '#1e1b4b';
      ctx.beginPath(); ctx.arc(640, 220, 50, 0, Math.PI * 2); ctx.fill();
      ctx.strokeStyle = '#a855f7'; ctx.lineWidth = 4; ctx.stroke();
      ctx.fillStyle = '#c084fc';
      ctx.beginPath(); ctx.arc(640, 220, 20, 0, Math.PI * 2); ctx.fill();
      ctx.fillStyle = 'rgba(168, 85, 247, 0.35)';
      ctx.beginPath(); ctx.arc(640, 220, 80, 0, Math.PI * 2); ctx.fill();
      ctx.fillStyle = '#d4a843'; ctx.font = 'bold 13px sans-serif'; ctx.textAlign = 'center';
      ctx.fillText('🔥 九轉八卦煉丹鼎', 640, 150);
    } else if (roomId === 'sect_forge') {
      // 煉器房：地火熾熱熔爐與鐵砧
      ctx.fillStyle = '#450a0a';
      ctx.fillRect(575, 160, 130, 110);
      ctx.strokeStyle = '#ef4444'; ctx.lineWidth = 3.5; ctx.strokeRect(575, 160, 130, 110);
      ctx.fillStyle = '#f59e0b';
      ctx.beginPath(); ctx.arc(640, 215, 28, 0, Math.PI * 2); ctx.fill();
      ctx.fillStyle = '#ffd700'; ctx.font = 'bold 13px sans-serif'; ctx.textAlign = 'center';
      ctx.fillText('🔨 地火神兵鐵砧', 640, 140);
    } else if (roomId === 'sect_market') {
      // 仙緣集市：商鋪櫃檯與靈草招牌
      ctx.fillStyle = '#1e293b';
      ctx.fillRect(520, 180, 240, 60);
      ctx.strokeStyle = '#38bdf8'; ctx.lineWidth = 3; ctx.strokeRect(520, 180, 240, 60);
      ctx.fillStyle = '#00cfff'; ctx.font = 'bold 14px sans-serif'; ctx.textAlign = 'center';
      ctx.fillText('🏪 仙緣集市 · 靈草靈礦櫃檯', 640, 160);
    } else if (roomId === 'sect_spring') {
      // 靈泉池：中央碧波水池
      ctx.fillStyle = 'rgba(14, 165, 233, 0.4)';
      ctx.beginPath(); ctx.arc(640, 360, 180, 0, Math.PI * 2); ctx.fill();
      ctx.strokeStyle = '#38bdf8'; ctx.lineWidth = 3; ctx.stroke();
    } else if (roomId === 'sect_meditate') {
      // 修煉房：靜心打坐八卦蒲團
      ctx.fillStyle = '#334155';
      ctx.beginPath(); ctx.arc(640, 360, 60, 0, Math.PI * 2); ctx.fill();
      ctx.strokeStyle = '#f59e0b'; ctx.lineWidth = 2; ctx.stroke();
    }
    ctx.restore();
  },

  drawDoors(ctx, area) {
    if (!ctx) return;
    const doors = (area && area.doors) ? area.doors : ['N', 'S', 'E', 'W'];
    const terr = (area && area.terrain) ? area.terrain : 'sect';
    const isBossRoom = area && area.isBossRoom;

    ctx.save();

    // ── 1. 北通道 (North) ──
    if (doors.includes('N')) {
      this.drawOpenPortalEffect(ctx, 640, 55, 55, isBossRoom ? 'rgba(239,68,68,0.35)' : 'rgba(212,168,67,0.35)');
    } else {
      // 無通道：採用使用者指定之【九宮格單牆旋轉貼圖機制】
      this.drawRotatedClosedWallPatch(ctx, 'N', terr);
    }

    // ── 2. 南通道 (South) ──
    if (doors.includes('S')) {
      this.drawOpenPortalEffect(ctx, 640, 665, 55, isBossRoom ? 'rgba(239,68,68,0.35)' : 'rgba(212,168,67,0.35)');
    } else {
      this.drawRotatedClosedWallPatch(ctx, 'S', terr);
    }

    // ── 3. 西通道 (West) ──
    if (doors.includes('W')) {
      this.drawOpenPortalEffect(ctx, 90, 360, 55, isBossRoom ? 'rgba(239,68,68,0.35)' : 'rgba(212,168,67,0.35)');
    } else {
      this.drawRotatedClosedWallPatch(ctx, 'W', terr);
    }

    // ── 4. 東通道 (East) ──
    if (doors.includes('E')) {
      this.drawOpenPortalEffect(ctx, 1190, 360, 55, isBossRoom ? 'rgba(239,68,68,0.35)' : 'rgba(212,168,67,0.35)');
    } else {
      this.drawRotatedClosedWallPatch(ctx, 'E', terr);
    }

    ctx.restore();
  },

  // 🧩 使用者指定之「九宮格單牆旋轉貼圖機制」
  // 從【上方無門母圖】採樣北牆實心石牆切片 (Y:0~110, X:180~1100)
  // 根據無門方向，將該切片進行 0度、90度、180度、-90度 旋轉貼上，保證四邊封牆100%同質無縫
  drawRotatedClosedWallPatch(ctx, dir, terr) {
    const closedKey = terr + '_closed';
    const closedImg = this.images[closedKey] || this.images['sect_closed'];
    if (!closedImg || !this.loaded[closedKey]) {
      this.drawSealedDoorGate(ctx, dir === 'N' || dir === 'S' ? 570 : (dir === 'W' ? 0 : 1100), dir === 'N' ? 0 : (dir === 'S' ? 610 : 290), dir === 'N' || dir === 'S' ? 140 : 180, dir === 'N' || dir === 'S' ? 110 : 140, dir, terr);
      return;
    }

    ctx.save();
    // 北牆無門切片區域: X:180~1100, Y:0~110 (寬 920, 高 110)
    const sx = 180, sy = 0, sw = 920, sh = 110;

    if (dir === 'N') {
      // 北牆 (直接原位貼上)
      ctx.drawImage(closedImg, sx, sy, sw, sh, 180, 0, 920, 110);
    } else if (dir === 'S') {
      // 南牆 (上下旋轉 180 度)
      ctx.translate(640, 665);
      ctx.rotate(Math.PI);
      ctx.drawImage(closedImg, sx, sy, sw, sh, -460, -55, 920, 110);
    } else if (dir === 'W') {
      // 西牆 (逆時針旋轉 90 度，貼於左側 X:0~180, Y:110~610)
      ctx.translate(90, 360);
      ctx.rotate(-Math.PI / 2);
      ctx.drawImage(closedImg, sx, sy, sw, sh, -250, -90, 500, 180);
    } else if (dir === 'E') {
      // 東牆 (順時針旋轉 90 度，貼於右側 X:1100~1280, Y:110~610)
      ctx.translate(1190, 360);
      ctx.rotate(Math.PI / 2);
      ctx.drawImage(closedImg, sx, sy, sw, sh, -250, -90, 500, 180);
    }
    ctx.restore();
  },

  // 🚪 繪製拱門內側封印石門/重門（精準契合門洞尺寸，無任何貼圖錯位）
  drawSealedDoorGate(ctx, x, y, w, h, dir, terr) {
    ctx.save();
    const gateCol = (terr === 'ruins') ? '#2a1a24' : (terr === 'cave') ? '#0f1e33' : '#141d2e';
    const borderCol = (terr === 'ruins') ? '#542634' : (terr === 'cave') ? '#25446e' : '#334766';

    // 1. 門洞內部暗色底石
    ctx.fillStyle = gateCol;
    ctx.fillRect(x, y, w, h);

    // 2. 石雕雙扇大門縫隙與框線
    ctx.strokeStyle = borderCol;
    ctx.lineWidth = 2;
    ctx.strokeRect(x + 2, y + 2, w - 4, h - 4);

    // 3. 中央對開門縫
    ctx.beginPath();
    if (dir === 'N' || dir === 'S') {
      ctx.moveTo(x + w / 2, y); ctx.lineTo(x + w / 2, y + h);
    } else {
      ctx.moveTo(x, y + h / 2); ctx.lineTo(x + w, y + h / 2);
    }
    ctx.stroke();

    // 4. 門環與古樸符文裝飾
    ctx.fillStyle = 'rgba(200, 168, 75, 0.4)';
    const cx = x + w / 2, cy = y + h / 2;
    ctx.beginPath(); ctx.arc(cx - 10, cy, 4, 0, Math.PI * 2); ctx.fill();
    ctx.beginPath(); ctx.arc(cx + 10, cy, 4, 0, Math.PI * 2); ctx.fill();

    ctx.restore();
  },

  // ✨ 有通道時的仙家靈氣法陣光暈 (極簡高雅，自然融入背景)
  drawOpenPortalEffect(ctx, cx, cy, r, colorStr) {
    ctx.save();
    const rad = ctx.createRadialGradient(cx, cy, 4, cx, cy, r);
    rad.addColorStop(0, colorStr);
    rad.addColorStop(1, 'rgba(0, 0, 0, 0)');
    ctx.fillStyle = rad;
    ctx.beginPath(); ctx.arc(cx, cy, r, 0, Math.PI * 2); ctx.fill();

    ctx.strokeStyle = colorStr; ctx.lineWidth = 1.2;
    ctx.beginPath(); ctx.arc(cx, cy, r * 0.45, 0, Math.PI * 2); ctx.stroke();
    ctx.restore();
  },

  // 🧱 無圖片載入時的後備高品質壁磚紋理
  drawFallbackClosedWall(ctx, x, y, w, h, terr) {
    const wallCol = (terr === 'ruins') ? '#1c1216' : (terr === 'cave') ? '#0c1626' : '#111827';
    const lineCol = (terr === 'ruins') ? '#382028' : (terr === 'cave') ? '#1a2e48' : '#26344d';
    ctx.fillStyle = wallCol; ctx.fillRect(x, y, w, h);
    ctx.strokeStyle = lineCol; ctx.lineWidth = 2;
    ctx.strokeRect(x, y, w, h);
    ctx.strokeStyle = 'rgba(255,255,255,0.06)';
    ctx.beginPath();
    ctx.moveTo(x, y + h * 0.33); ctx.lineTo(x + w, y + h * 0.33);
    ctx.moveTo(x, y + h * 0.66); ctx.lineTo(x + w, y + h * 0.66);
    ctx.stroke();
  },

  drawFallbackTopDownFloor(ctx, terr) {
    const tileSize = 64;
    const cols = 1280 / tileSize;
    const rows = 720 / tileSize;
    let tileA = '#131c2e', tileB = '#19263e';
    if (terr === 'mountain') { tileA = '#0e291c'; tileB = '#153626'; }
    else if (terr === 'cave') { tileA = '#0d182b'; tileB = '#13223d'; }
    else if (terr === 'ruins') { tileA = '#210e14'; tileB = '#2c141c'; }

    for (let r = 0; r < rows; r++) {
      for (let c = 0; c < cols; c++) {
        ctx.fillStyle = (r + c) % 2 === 0 ? tileA : tileB;
        ctx.fillRect(c * tileSize, r * tileSize, tileSize, tileSize);
      }
    }
  },

  drawTopDownGridOverlay(ctx, terr) {
    ctx.save();
    const tileSize = 64;
    const lineCol = terr === 'ruins' ? 'rgba(239, 68, 68, 0.2)' : terr === 'cave' ? 'rgba(56, 189, 248, 0.18)' : 'rgba(212, 168, 67, 0.18)';

    ctx.strokeStyle = lineCol; ctx.lineWidth = 1.2;
    for (let c = 0; c <= 1280; c += tileSize) {
      ctx.beginPath(); ctx.moveTo(c, 0); ctx.lineTo(c, 720); ctx.stroke();
    }
    for (let r = 0; r <= 720; r += tileSize) {
      ctx.beginPath(); ctx.moveTo(0, r); ctx.lineTo(1280, r); ctx.stroke();
    }

    ctx.fillStyle = 'rgba(0, 0, 0, 0.25)';
    ctx.fillRect(0, 0, 1280, 24);
    ctx.fillRect(0, 696, 1280, 24);
    ctx.fillRect(0, 0, 24, 720);
    ctx.fillRect(1256, 0, 24, 720);
    ctx.restore();
  }
};

window.BackgroundRenderer.init();
