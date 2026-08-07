// ═══════════════════════════════════════════════════════════
//  玩家獨立渲染器 (Player Renderer Module)
//  【架構隔離】角色渲染、HD動態幀計算與顯示尺寸完全獨立
// ═══════════════════════════════════════════════════════════

const PlayerRenderer = {
  draw(ctx, playerState, T) {
    if (!ctx || !playerState) return;
    if (playerState.hurtFlash > 0 && playerState.hurtFlash % 2 === 0) return;

    const hov = playerState.state === 'MOVE' ? Math.sin(T * 12) * 1.5 : 0;

    if (window.hdPlayerLoaded && window.hdPlayerImg) {
      // 依玩家狀態決定 Spritesheet 幀
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

      const fr = PLAYER_FRAMES[fKey] || PLAYER_FRAMES.IDLE;
      const flipX = playerState.facing.x < 0;

      // 玩家顯示尺寸：72×96 (高解析度清晰大圖)
      drawHDFrame(
        window.hdPlayerImg,
        fr.col, fr.row,
        PLAYER_SHEET_COLS, PLAYER_SHEET_ROWS,
        playerState.x - 36, playerState.y - 56 + hov,
        72, 96,
        flipX,
        ctx
      );
    } else {
      // 備援像素圖形
      drawSprite(IDLE0, playerState.x - 14, playerState.y - 20 + hov, 2, playerState.facing.x < 0, null, ctx);
    }
  }
};
