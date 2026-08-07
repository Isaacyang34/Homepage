// ═══════════════════════════════════════════════════════════
//  玩家獨立渲染器 (Player Renderer Module)
//  【亮色修仙劍客 + 靈氣煥發光環 + 無條件高對比度渲染】
// ═══════════════════════════════════════════════════════════

window.PlayerRenderer = {
  draw(ctx, playerState, T) {
    if (!ctx || !playerState) return;

    ctx.save();

    const x = playerState.x || 640;
    const y = playerState.y || 480;
    const flipX = playerState.facing && playerState.facing.x < 0;
    const hov = playerState.state === 'MOVE' ? Math.sin(T * 14) * 2.5 : 0;

    // 1. 受傷半透明提示
    if (playerState.hurtFlash > 0) {
      ctx.globalAlpha = 0.7;
    }

    // 2. 腳下靈氣光環與橢圓柔和陰影 (高亮金藍護體光環)
    ctx.fillStyle = 'rgba(0, 0, 0, 0.45)';
    ctx.beginPath();
    ctx.ellipse(x, y + 8, 38, 16, 0, 0, Math.PI * 2);
    ctx.fill();

    // 護體靈氣極光 (防止在暗色地板上看不清)
    const auraGlow = ctx.createRadialGradient(x, y - 20, 5, x, y - 20, 55);
    auraGlow.addColorStop(0, 'rgba(0, 229, 255, 0.35)');
    auraGlow.addColorStop(0.7, 'rgba(212, 168, 67, 0.15)');
    auraGlow.addColorStop(1, 'rgba(0, 0, 0, 0)');
    ctx.fillStyle = auraGlow;
    ctx.beginPath(); ctx.arc(x, y - 20, 55, 0, Math.PI * 2); ctx.fill();

    let hdDrawn = false;

    // 3. 嘗試高清 HD 圖集渲染 (如無效自動降級為高對比向量/像素精靈)
    if (window.hdPlayerLoaded && window.hdPlayerImg && typeof drawHDFrame === 'function') {
      let fKey = 'IDLE';
      if (playerState.state === 'MOVE') {
        const wi = Math.floor(T * 8) % 3;
        fKey = ['WALK1', 'WALK2', 'WALK3'][wi];
      } else if (playerState.state === 'ATTACK') {
        const ai = Math.floor(T * 12) % 2;
        fKey = ['ATK1', 'ATK2'][ai];
      } else if (playerState.state === 'MEDITATE') {
        fKey = 'IDLE1';
      }

      const fr = (typeof PLAYER_FRAMES !== 'undefined') ? (PLAYER_FRAMES[fKey] || PLAYER_FRAMES.IDLE) : { col: 0, row: 0 };
      hdDrawn = drawHDFrame(
        window.hdPlayerImg,
        fr.col, fr.row,
        PLAYER_SHEET_COLS, PLAYER_SHEET_ROWS,
        x - 54, y - 95 + hov,
        108, 144,
        flipX,
        ctx
      );
    }

    // 4. 高亮高對比度修仙少俠 (白藍道袍 + 金冠 + 飛劍，對齊 108×144 大圖尺寸)
    if (!hdDrawn) {
      ctx.save();
      ctx.translate(x, y - 45 + hov);
      if (flipX) ctx.scale(-1, 1);

      // 鮮明白藍飄逸道袍 (白色主體 #ffffff，青藍鑲邊 #00cfff)
      ctx.fillStyle = '#ffffff';
      ctx.fillRect(-20, -32, 40, 64);
      ctx.fillStyle = '#00cfff';
      ctx.fillRect(-22, -32, 44, 8); // 肩膀青青襟邊
      ctx.fillStyle = '#ffd700';
      ctx.fillRect(-18, 0, 36, 6);   // 束腰金帶

      // 膚色臉龐與發亮黑髮金冠
      ctx.fillStyle = '#1e1b4b';
      ctx.fillRect(-14, -58, 28, 14); // 發髻黑髮
      ctx.fillStyle = '#ffd700';
      ctx.fillRect(-12, -60, 24, 6);  // 發髻金冠
      ctx.fillStyle = '#ffe0b2';
      ctx.beginPath(); ctx.arc(0, -40, 15, 0, Math.PI * 2); ctx.fill(); // 臉龐

      // 雙眼與眼神
      ctx.fillStyle = '#0f172a';
      ctx.fillRect(4, -43, 4, 5);

      // 身後背負/手持亮藍靈劍 (發光特效)
      ctx.fillStyle = '#00ffff';
      ctx.shadowColor = '#00ffff';
      ctx.shadowBlur = 10;
      ctx.fillRect(18, -25, 7, 50);
      ctx.fillStyle = '#ffffff';
      ctx.fillRect(15, -28, 13, 6);

      ctx.restore();
    }

    // 5. 繪製主角上方高亮稱號 Badge (黃金文字 + 黑色陰影，極易辨識)
    ctx.fillStyle = '#ffd700';
    ctx.font = 'bold 13px sans-serif';
    ctx.textAlign = 'center';
    ctx.shadowColor = '#000000';
    ctx.shadowBlur = 6;
    const rName = (typeof REALMS !== 'undefined' && REALMS[playerState.realmIdx]) ? REALMS[playerState.realmIdx].name : '修仙者';
    ctx.fillText(`⚔️【${rName}】少俠`, x, y - 105 + hov);

    ctx.restore();
  }
};
