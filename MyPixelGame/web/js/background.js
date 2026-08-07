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

    // 通道靈光與石牆材質色彩
    const portalGlow = isBossRoom ? '#ef4444' : (terr === 'ruins') ? '#ef4444' : (terr === 'cave') ? '#38bdf8' : '#00cfff';
    const wallColor = (terr === 'ruins') ? '#1e141a' : (terr === 'cave') ? '#0d192e' : '#111827';
    const wallBorder = (terr === 'ruins') ? '#451a24' : (terr === 'cave') ? '#1d3354' : '#26344d';

    ctx.save();

    // ── 1. 北通道 (North Corridor: X: 570~710, Y: 0~110) ──
    if (doors.includes('N')) {
      const glow = ctx.createLinearGradient(640, 0, 640, 110);
      glow.addColorStop(0, portalGlow);
      glow.addColorStop(1, 'rgba(0, 0, 0, 0)');
      ctx.fillStyle = glow;
      ctx.fillRect(570, 0, 140, 110);
      ctx.strokeStyle = portalGlow; ctx.lineWidth = 2.5;
      ctx.strokeRect(570, 0, 140, 110);
    } else {
      // 封密石牆：覆蓋無門區域，形態完美融入周圍牆壁
      ctx.fillStyle = wallColor;
      ctx.fillRect(560, 0, 160, 112);
      ctx.strokeStyle = wallBorder; ctx.lineWidth = 3.5;
      ctx.strokeRect(560, 0, 160, 112);
      ctx.strokeStyle = 'rgba(255, 255, 255, 0.08)'; ctx.lineWidth = 1.5;
      ctx.beginPath();
      ctx.moveTo(560, 40); ctx.lineTo(720, 40);
      ctx.moveTo(560, 80); ctx.lineTo(720, 80);
      ctx.stroke();
    }

    // ── 2. 南通道 (South Corridor: X: 570~710, Y: 610~720) ──
    if (doors.includes('S')) {
      const glow = ctx.createLinearGradient(640, 720, 640, 610);
      glow.addColorStop(0, portalGlow);
      glow.addColorStop(1, 'rgba(0, 0, 0, 0)');
      ctx.fillStyle = glow;
      ctx.fillRect(570, 610, 140, 110);
      ctx.strokeStyle = portalGlow; ctx.lineWidth = 2.5;
      ctx.strokeRect(570, 610, 140, 110);
    } else {
      ctx.fillStyle = wallColor;
      ctx.fillRect(560, 608, 160, 112);
      ctx.strokeStyle = wallBorder; ctx.lineWidth = 3.5;
      ctx.strokeRect(560, 608, 160, 112);
      ctx.strokeStyle = 'rgba(255, 255, 255, 0.08)'; ctx.lineWidth = 1.5;
      ctx.beginPath();
      ctx.moveTo(560, 648); ctx.lineTo(720, 648);
      ctx.moveTo(560, 688); ctx.lineTo(720, 688);
      ctx.stroke();
    }

    // ── 3. 西通道 (West Corridor: X: 0~180, Y: 290~430) ──
    if (doors.includes('W')) {
      const glow = ctx.createLinearGradient(0, 360, 180, 360);
      glow.addColorStop(0, portalGlow);
      glow.addColorStop(1, 'rgba(0, 0, 0, 0)');
      ctx.fillStyle = glow;
      ctx.fillRect(0, 290, 180, 140);
      ctx.strokeStyle = portalGlow; ctx.lineWidth = 2.5;
      ctx.strokeRect(0, 290, 180, 140);
    } else {
      ctx.fillStyle = wallColor;
      ctx.fillRect(0, 280, 182, 160);
      ctx.strokeStyle = wallBorder; ctx.lineWidth = 3.5;
      ctx.strokeRect(0, 280, 182, 160);
      ctx.strokeStyle = 'rgba(255, 255, 255, 0.08)'; ctx.lineWidth = 1.5;
      ctx.beginPath();
      ctx.moveTo(60, 280); ctx.lineTo(60, 440);
      ctx.moveTo(120, 280); ctx.lineTo(120, 440);
      ctx.stroke();
    }

    // ── 4. 東通道 (East Corridor: X: 1100~1280, Y: 290~430) ──
    if (doors.includes('E')) {
      const glow = ctx.createLinearGradient(1280, 360, 1100, 360);
      glow.addColorStop(0, portalGlow);
      glow.addColorStop(1, 'rgba(0, 0, 0, 0)');
      ctx.fillStyle = glow;
      ctx.fillRect(1100, 290, 180, 140);
      ctx.strokeStyle = portalGlow; ctx.lineWidth = 2.5;
      ctx.strokeRect(1100, 290, 180, 140);
    } else {
      ctx.fillStyle = wallColor;
      ctx.fillRect(1098, 280, 182, 160);
      ctx.strokeStyle = wallBorder; ctx.lineWidth = 3.5;
      ctx.strokeRect(1098, 280, 182, 160);
      ctx.strokeStyle = 'rgba(255, 255, 255, 0.08)'; ctx.lineWidth = 1.5;
      ctx.beginPath();
      ctx.moveTo(1160, 280); ctx.lineTo(1160, 440);
      ctx.moveTo(1220, 280); ctx.lineTo(1220, 440);
      ctx.stroke();
    }

    ctx.restore();
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
