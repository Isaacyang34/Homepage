const sharp = require('sharp');
const fs = require('fs');
const path = require('path');

const srcPath = path.join(__dirname, '../MyPixelGame/assets/backgrounds/bg_sect.png');
const tileDir = path.join(__dirname, '../MyPixelGame/assets/backgrounds/tiles');

async function processChatGPTTiles() {
  if (!fs.existsSync(tileDir)) fs.mkdirSync(tileDir, { recursive: true });

  console.log('[PROCESSING CHATGPT MASTER] 去除黑邊並歸一化至 1280x720...');

  // 1. 去除 ChatGPT 圖片四周黑邊，並歸一化至 1280x720 畫布尺寸
  const resizedBuf = await sharp(srcPath)
    .trim()
    .resize(1280, 720, { fit: 'fill' })
    .toBuffer();

  // 覆蓋主底圖為精確 1280x720 HD 版本
  fs.writeFileSync(srcPath, resizedBuf);

  // 2. 裁切 ChatGPT 九宮格瓦片 (地磚矩陣 100% 純淨呈長方形，門拱 0% 侵佔地磚！)：
  // - Tile 1 (左上角): 180x110
  // - Tile 2 (北牆有門): 920x110
  // - Tile 3 (右上角): 180x110
  // - Tile 4 (西牆有門): 180x500
  // - Tile 5 (中央純地磚): 920x500
  // - Tile 6 (東牆實心無門): 180x500
  // - Tile 7 (左下角): 180x110
  // - Tile 8 (南牆實心無門): 920x110
  // - Tile 9 (右下角): 180x110

  const tile1Buf = await sharp(resizedBuf).extract({ left: 0, top: 0, width: 180, height: 110 }).toBuffer();
  const tile2OpenBuf = await sharp(resizedBuf).extract({ left: 180, top: 0, width: 920, height: 110 }).toBuffer();
  const tile3Buf = await sharp(resizedBuf).extract({ left: 1100, top: 0, width: 180, height: 110 }).toBuffer();
  const tile4OpenBuf = await sharp(resizedBuf).extract({ left: 0, top: 110, width: 180, height: 500 }).toBuffer();
  const tile5Buf = await sharp(resizedBuf).extract({ left: 180, top: 110, width: 920, height: 500 }).toBuffer();
  const tile6ClosedBuf = await sharp(resizedBuf).extract({ left: 1100, top: 110, width: 180, height: 500 }).toBuffer();
  const tile7Buf = await sharp(resizedBuf).extract({ left: 0, top: 610, width: 180, height: 110 }).toBuffer();
  const tile8ClosedBuf = await sharp(resizedBuf).extract({ left: 180, top: 610, width: 920, height: 110 }).toBuffer();
  const tile9Buf = await sharp(resizedBuf).extract({ left: 1100, top: 610, width: 180, height: 110 }).toBuffer();

  // 3. 鏡像對稱衍生瓦片 (0% 幾何變形)
  const tile6OpenBuf = await sharp(tile4OpenBuf).flop().toBuffer();        // 東牆有門 = 西牆有門 水平鏡像
  const tile4ClosedBuf = await sharp(tile6ClosedBuf).flop().toBuffer();    // 西牆無門 = 東牆無門 水平鏡像
  const tile8OpenBuf = await sharp(tile2OpenBuf).flip().toBuffer();        // 南牆有門 = 北牆有門 垂直鏡像
  const tile2ClosedBuf = await sharp(tile8ClosedBuf).flip().toBuffer();    // 北牆無門 = 南牆無門 垂直鏡像

  // 4. 寫入所有瓦片檔
  fs.writeFileSync(path.join(tileDir, 'bg_sect1.png'), tile1Buf);
  fs.writeFileSync(path.join(tileDir, 'bg_sect2_open.png'), tile2OpenBuf);
  fs.writeFileSync(path.join(tileDir, 'bg_sect2_closed.png'), tile2ClosedBuf);
  fs.writeFileSync(path.join(tileDir, 'bg_sect3.png'), tile3Buf);

  fs.writeFileSync(path.join(tileDir, 'bg_sect4_open.png'), tile4OpenBuf);
  fs.writeFileSync(path.join(tileDir, 'bg_sect4_closed.png'), tile4ClosedBuf);
  fs.writeFileSync(path.join(tileDir, 'bg_sect5.png'), tile5Buf);
  fs.writeFileSync(path.join(tileDir, 'bg_sect6_open.png'), tile6OpenBuf);
  fs.writeFileSync(path.join(tileDir, 'bg_sect6_closed.png'), tile6ClosedBuf);

  fs.writeFileSync(path.join(tileDir, 'bg_sect7.png'), tile7Buf);
  fs.writeFileSync(path.join(tileDir, 'bg_sect8_open.png'), tile8OpenBuf);
  fs.writeFileSync(path.join(tileDir, 'bg_sect8_closed.png'), tile8ClosedBuf);
  fs.writeFileSync(path.join(tileDir, 'bg_sect9.png'), tile9Buf);

  console.log('[SUCCESS] ChatGPT 母圖 9 瓦片精確裁切與鏡像處理完成！');
}

processChatGPTTiles().catch(err => console.error(err));
