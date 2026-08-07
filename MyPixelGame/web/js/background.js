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

    // 2. 疊加 64x64 方格網格
    this.drawTopDownGridOverlay(ctx, terr);

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
    const glowCol = (area && area.terrain === 'ruins') ? '#ef4444' : (area && area.terrain === 'cave') ? '#38bdf8' : '#f59e0b';

    ctx.save();
    if (doors.includes('N')) {
      ctx.fillStyle = '#0f172a'; ctx.fillRect(570, 0, 140, 28);
      ctx.strokeStyle = glowCol; ctx.lineWidth = 3; ctx.strokeRect(575, 2, 130, 24);
      ctx.fillStyle = glowCol; ctx.font = 'bold 12px sans-serif'; ctx.textAlign = 'center';
      ctx.fillText('🚪 北門 (N)', 640, 18);
    }
    if (doors.includes('S')) {
      ctx.fillStyle = '#0f172a'; ctx.fillRect(570, 692, 140, 28);
      ctx.strokeStyle = glowCol; ctx.lineWidth = 3; ctx.strokeRect(575, 694, 130, 24);
      ctx.fillStyle = glowCol; ctx.font = 'bold 12px sans-serif'; ctx.textAlign = 'center';
      ctx.fillText('🚪 南門 (S)', 640, 710);
    }
    if (doors.includes('W')) {
      ctx.fillStyle = '#0f172a'; ctx.fillRect(0, 300, 28, 120);
      ctx.strokeStyle = glowCol; ctx.lineWidth = 3; ctx.strokeRect(2, 305, 24, 110);
      ctx.fillStyle = glowCol; ctx.font = 'bold 12px sans-serif'; ctx.textAlign = 'center';
      ctx.fillText('西門', 14, 365);
    }
    if (doors.includes('E')) {
      ctx.fillStyle = '#0f172a'; ctx.fillRect(1252, 300, 28, 120);
      ctx.strokeStyle = glowCol; ctx.lineWidth = 3; ctx.strokeRect(1254, 305, 24, 110);
      ctx.fillStyle = glowCol; ctx.font = 'bold 12px sans-serif'; ctx.textAlign = 'center';
      ctx.fillText('東門', 1266, 365);
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
