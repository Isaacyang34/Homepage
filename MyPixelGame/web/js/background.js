// ═══════════════════════════════════════════════════════════
//  背景地質與場景獨立渲染器 (Background Renderer Module)
//  【架構隔離】背景邏輯與人物/UI完全解耦，修改人物絕不動到本檔案
// ═══════════════════════════════════════════════════════════

const BackgroundRenderer = {
  draw(ctx, area) {
    if (!ctx) return;
    const terr = (area && area.terrain) ? area.terrain : 'sect';

    if (terr === 'sect') {
      // 🏛️ 青雲宗大殿：深藍青石地板 + 金紋聖徽與四角細花紋
      ctx.fillStyle = '#080e1f';
      ctx.fillRect(0, 0, 480, 270);

      // 地磚正方格（細膩低調）
      ctx.strokeStyle = 'rgba(80, 100, 160, 0.18)';
      ctx.lineWidth = 1;
      for (let x = 0; x < 480; x += 40) {
        ctx.beginPath(); ctx.moveTo(x, 0); ctx.lineTo(x, 270); ctx.stroke();
      }
      for (let y = 0; y < 270; y += 40) {
        ctx.beginPath(); ctx.moveTo(0, y); ctx.lineTo(480, y); ctx.stroke();
      }

      // 中央圓形宗門聖徽
      ctx.strokeStyle = 'rgba(212, 168, 67, 0.12)';
      ctx.lineWidth = 2;
      ctx.beginPath(); ctx.arc(240, 135, 100, 0, Math.PI * 2); ctx.stroke();
      ctx.beginPath(); ctx.arc(240, 135, 60, 0, Math.PI * 2); ctx.stroke();

      // 四角淡金邊框標記
      const corners = [[0,0], [480,0], [0,270], [480,270]];
      ctx.strokeStyle = 'rgba(212, 168, 67, 0.15)';
      ctx.lineWidth = 1;
      corners.forEach(([cx, cy]) => {
        const sx = cx === 0 ? 1 : -1, sy = cy === 0 ? 1 : -1;
        ctx.beginPath(); ctx.moveTo(cx, cy + sy*30); ctx.lineTo(cx, cy); ctx.lineTo(cx + sx*30, cy); ctx.stroke();
      });

    } else if (terr === 'mountain') {
      // 🌲 外門靈山：水墨遠山 + 深綠草地
      ctx.fillStyle = '#061a0e';
      ctx.fillRect(0, 0, 480, 270);

      // 遠山層次
      ctx.fillStyle = 'rgba(10, 40, 20, 0.6)';
      ctx.beginPath(); ctx.moveTo(0, 120); ctx.lineTo(80, 80); ctx.lineTo(160, 100); ctx.lineTo(240, 60); ctx.lineTo(320, 90); ctx.lineTo(400, 70); ctx.lineTo(480, 100); ctx.lineTo(480, 270); ctx.lineTo(0, 270); ctx.fill();

      // 草地地面
      ctx.fillStyle = '#0a2e15';
      ctx.fillRect(0, 150, 480, 120);

      // 草地細紋
      ctx.strokeStyle = 'rgba(30, 80, 40, 0.4)';
      ctx.lineWidth = 1;
      for (let x = 0; x < 480; x += 20) {
        ctx.beginPath(); ctx.moveTo(x, 150); ctx.lineTo(x + 5, 270); ctx.stroke();
      }

    } else if (terr === 'cave') {
      // 🌌 玄陰洞府：幽暗石窟 + 螢光水晶
      ctx.fillStyle = '#05080f';
      ctx.fillRect(0, 0, 480, 270);

      // 石窟頂部輪廓
      ctx.fillStyle = '#0a0f1a';
      ctx.beginPath(); ctx.moveTo(0, 0); ctx.lineTo(60, 40); ctx.lineTo(120, 15); ctx.lineTo(200, 50); ctx.lineTo(280, 20); ctx.lineTo(360, 45); ctx.lineTo(480, 25); ctx.lineTo(480, 0); ctx.fill();

      // 石地板紋理線
      ctx.strokeStyle = 'rgba(30, 41, 59, 0.5)';
      ctx.lineWidth = 1;
      for (let y = 160; y < 270; y += 18) {
        ctx.beginPath(); ctx.moveTo(0, y); ctx.lineTo(480, y + 5); ctx.stroke();
      }

      // 螢光水晶簇
      [
        { x: 25, y: 70, c: '#38bdf8', r: 5 },
        { x: 455, y: 55, c: '#818cf8', r: 4 },
        { x: 18, y: 190, c: '#22d3ee', r: 4 },
        { x: 462, y: 200, c: '#c084fc', r: 5 }
      ].forEach(cr => {
        ctx.fillStyle = cr.c + '33';
        ctx.beginPath(); ctx.arc(cr.x, cr.y, cr.r * 3, 0, Math.PI * 2); ctx.fill();
        ctx.fillStyle = cr.c;
        ctx.beginPath(); ctx.arc(cr.x, cr.y, cr.r, 0, Math.PI * 2); ctx.fill();
      });

    } else if (terr === 'ruins') {
      // 🌋 古修遺跡：熔岩地隙與火焰天空
      ctx.fillStyle = '#0e0205';
      ctx.fillRect(0, 0, 480, 270);

      ctx.fillStyle = '#1a0408';
      ctx.fillRect(0, 160, 480, 110);

      ctx.strokeStyle = 'rgba(239, 68, 68, 0.35)';
      ctx.lineWidth = 2;
      [[0,190,200,170,480,195],[100,160,250,180,400,160]].forEach(pts => {
        ctx.beginPath(); ctx.moveTo(pts[0], pts[1]); ctx.lineTo(pts[2], pts[3]); ctx.lineTo(pts[4], pts[5]); ctx.stroke();
      });

      const skyGrad = ctx.createLinearGradient(0, 0, 0, 160);
      skyGrad.addColorStop(0, '#1a0408');
      skyGrad.addColorStop(1, '#2d0610');
      ctx.fillStyle = skyGrad;
      ctx.fillRect(0, 0, 480, 160);

      ctx.fillStyle = '#1e0508';
      [[10, 60, 14, 100], [456, 55, 14, 105], [10, 180, 14, 90], [456, 175, 14, 95]].forEach(([x, y, w, h]) => {
        ctx.fillRect(x, y, w, h);
      });
    }
  }
};
