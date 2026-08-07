// ═══════════════════════════════════════════════════════════
//  背景地質與場景獨立渲染器 (Background Renderer Module)
//  【Moonlighter 經典 45度正俯視 + 宗門功能房間與地牢地貌】
// ═══════════════════════════════════════════════════════════

window.BackgroundRenderer = {
  images: {},
  loaded: {},

  init() {
    const maps = {
      sect: 'assets/backgrounds/bg_sect.png',
      mountain: 'assets/backgrounds/bg_mountain.png',
      cave: 'assets/backgrounds/bg_cave.png',
      ruins: 'assets/backgrounds/bg_ruins.png'
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

    // 1. 繪製 45度正俯視 Top-Down HD 背景圖
    if (this.images[terr] && this.loaded[terr]) {
      ctx.drawImage(this.images[terr], 0, 0, 1280, 720);
    } else {
      this.drawFallbackTopDownFloor(ctx, terr);
    }

    // 2. 直接以背景原圖地板作為邊界，不額外疊加人工方格網格線
    // (已依據使用者要求完全取消畫面中的人工方格網格)

    // 3. 若為宗門特定功能房間，繪製專屬造景 (丹爐/鍛造台/靈泉池/蒲團)
    if (area && area.id) {
      this.drawSectRoomScenery(ctx, area.id);
    }

    // 4. 繪製 Moonlighter 經典 N / S / W / E 石砌拱門
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
    const hasImg = this.images[terr] && this.loaded[terr];

    ctx.save();

    // ── 1. 北通道 (North: 560~720, Y: 0~112) ──
    if (doors.includes('N')) {
      // 有通道：柔和仙家靈氣法陣光暈（完全取消生硬藍色外框與粗暴漸層）
      this.drawOpenPortalEffect(ctx, 640, 55, 55, isBossRoom ? 'rgba(239,68,68,0.35)' : 'rgba(212,168,67,0.35)');
    } else {
      // 無通道：直接採樣背景原圖的實體壁磚紋理進行無縫封密覆蓋
      if (hasImg) {
        // 從背景原圖的左側牆面 (320~480, 0~112) 採樣高畫質壁磚紋理，遮擋北部門洞
        ctx.drawImage(this.images[terr], 320, 0, 160, 112, 560, 0, 160, 112);
        ctx.fillStyle = 'rgba(0, 0, 0, 0.12)'; ctx.fillRect(560, 0, 160, 112);
      } else {
        this.drawFallbackClosedWall(ctx, 560, 0, 160, 112, terr);
      }
    }

    // ── 2. 南通道 (South: 560~720, Y: 608~720) ──
    if (doors.includes('S')) {
      this.drawOpenPortalEffect(ctx, 640, 665, 55, isBossRoom ? 'rgba(239,68,68,0.35)' : 'rgba(212,168,67,0.35)');
    } else {
      if (hasImg) {
        ctx.drawImage(this.images[terr], 320, 608, 160, 112, 560, 608, 160, 112);
        ctx.fillStyle = 'rgba(0, 0, 0, 0.12)'; ctx.fillRect(560, 608, 160, 112);
      } else {
        this.drawFallbackClosedWall(ctx, 560, 608, 160, 112, terr);
      }
    }

    // ── 3. 西通道 (West: 0~182, Y: 280~440) ──
    if (doors.includes('W')) {
      this.drawOpenPortalEffect(ctx, 90, 360, 55, isBossRoom ? 'rgba(239,68,68,0.35)' : 'rgba(212,168,67,0.35)');
    } else {
      if (hasImg) {
        ctx.drawImage(this.images[terr], 0, 112, 182, 160, 0, 280, 182, 160);
        ctx.fillStyle = 'rgba(0, 0, 0, 0.12)'; ctx.fillRect(0, 280, 182, 160);
      } else {
        this.drawFallbackClosedWall(ctx, 0, 280, 182, 160, terr);
      }
    }

    // ── 4. 東通道 (East: 1098~1280, Y: 280~440) ──
    if (doors.includes('E')) {
      this.drawOpenPortalEffect(ctx, 1190, 360, 55, isBossRoom ? 'rgba(239,68,68,0.35)' : 'rgba(212,168,67,0.35)');
    } else {
      if (hasImg) {
        ctx.drawImage(this.images[terr], 1098, 112, 182, 160, 1098, 280, 182, 160);
        ctx.fillStyle = 'rgba(0, 0, 0, 0.12)'; ctx.fillRect(1098, 280, 182, 160);
      } else {
        this.drawFallbackClosedWall(ctx, 1098, 280, 182, 160, terr);
      }
    }

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
