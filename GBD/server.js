'use strict';

const express = require('express');
const cors = require('cors');
const crypto = require('crypto');
const path = require('path');

const app = express();
const PORT = process.env.PORT || 3000;

// ══════════════════════════════════════════════════
// CONSTANTS & SECURITY CONFIG
// ══════════════════════════════════════════════════
const HEADER_SIZE   = 12288;
const BYTES_PER_REC = 36;
const NUM_CH_ALL    = 18;
const NUM_CH_DISP   = 15;

const SALT = '_GBD_SALT_2026';
// SHA-256 salted hash of management password "e799c2fbe2"
const UNLOCK_HASH = 'e604c5ff8762a4bcfd6d49ee98e5a3ef7435a43e27f3f82e6e3365a906f9dfd1';

const MAX_ATTEMPTS = 5;
const LOCKOUT_MS   = 15 * 60 * 1000; // 15 minutes IP lockout

// Security state maps
const ipTracker = new Map(); // ip -> { count, lockUntil }
const activeTokens = new Set(); // valid session tokens

app.use(cors());
app.use(express.json({ limit: '50mb' }));
app.use(express.static(path.join(__dirname, 'public')));

// ══════════════════════════════════════════════════
// SECURITY MIDDLEWARES
// ══════════════════════════════════════════════════
function getClientIp(req) {
  return req.headers['x-forwarded-for']?.split(',')[0] || req.socket.remoteAddress || '127.0.0.1';
}

function checkRateLimit(req, res, next) {
  const ip = getClientIp(req);
  const record = ipTracker.get(ip);
  const now = Date.now();

  if (record && record.lockUntil > now) {
    const remainSec = Math.ceil((record.lockUntil - now) / 1000);
    return res.status(429).json({
      error: `🚨 連續密碼錯誤過多，此 IP 已遭防護機制封鎖！請於 ${remainSec} 秒後再試。`,
      lockout: true,
      remainSec
    });
  }

  if (record && record.lockUntil <= now) {
    ipTracker.delete(ip); // lockout expired
  }
  next();
}

function verifyToken(req, res, next) {
  const auth = req.headers.authorization;
  const token = auth?.replace('Bearer ', '');
  if (!token || !activeTokens.has(token)) {
    return res.status(401).json({ error: '🔒 未經授權存取！請輸入密碼解鎖進階編輯功能。' });
  }
  next();
}

function sha256(str) {
  return crypto.createHash('sha256').update(str + SALT).digest('hex');
}

// ══════════════════════════════════════════════════
// AUTHENTICATION API
// ══════════════════════════════════════════════════
app.post('/api/verify', checkRateLimit, (req, res) => {
  const { password } = req.body;
  const ip = getClientIp(req);
  const now = Date.now();

  if (!password) return res.status(400).json({ error: '請輸入密碼' });

  const inputHash = sha256(password.trim());
  if (inputHash === UNLOCK_HASH) {
    ipTracker.delete(ip);
    const token = crypto.randomBytes(24).toString('hex');
    activeTokens.add(token);
    return res.json({ success: true, token, message: '✓ 密碼驗證成功，後端編輯模組已解鎖！' });
  }

  // Record failed attempt
  let record = ipTracker.get(ip) || { count: 0, lockUntil: 0 };
  record.count += 1;

  if (record.count >= MAX_ATTEMPTS) {
    record.lockUntil = now + LOCKOUT_MS;
    ipTracker.set(ip, record);
    return res.status(429).json({
      error: `🚨 密碼連續錯誤達到 ${MAX_ATTEMPTS} 次！IP 已觸發安全自毀封鎖 (15分鐘)。`,
      lockout: true,
      remainSec: LOCKOUT_MS / 1000
    });
  }

  ipTracker.set(ip, record);
  return res.status(401).json({
    error: `❌ 密碼錯誤！(剩餘 ${MAX_ATTEMPTS - record.count} 次嘗試機會)`,
    attemptsLeft: MAX_ATTEMPTS - record.count
  });
});

// ══════════════════════════════════════════════════
// HELPER UTILS FOR GBD BUFFER PROCESSING
// ══════════════════════════════════════════════════
function parseGBDHeader(hdrStr) {
  const numM  = hdrStr.match(/Counts\s*=\s*(\d+)/);
  const stM   = hdrStr.match(/Start\s*=\s*([\d-]+),([\d:]+)/);
  const enM   = hdrStr.match(/Stop\s*=\s*([\d-]+),([\d:]+)/);
  const samM  = hdrStr.match(/Sample\s*=\s*(\d+)s/);

  return {
    counts: parseInt(numM?.[1] || '0'),
    startT: stM ? new Date(`${stM[1]}T${stM[2]}`) : new Date(),
    stopT:  enM ? new Date(`${enM[1]}T${enM[2]}`) : new Date(),
    sample: samM ? parseInt(samM[1]) : 1
  };
}

function fmtGBDDate(d) {
  const p = n => String(n).padStart(2, '0');
  return `${d.getFullYear()}-${p(d.getMonth()+1)}-${p(d.getDate())},${p(d.getHours())}:${p(d.getMinutes())}:${p(d.getSeconds())}`;
}

function clamp(v, lo, hi) { return v < lo ? lo : v > hi ? hi : v; }

function mathFx(x, type) {
  switch(type) {
    case 'linear': return x;
    case 'quad':   return 1 - Math.pow(1-x, 2);
    case 'cubic':  return 1 - Math.pow(1-x, 3);
    case 'sqrt':   return Math.sqrt(x);
    default:       return x;
  }
}

function recLerp(snap, idxF, ch) {
  const lo = Math.floor(idxF);
  const hi = Math.min(lo + 1, snap.length - 1);
  const frac = idxF - lo;
  return snap[lo][ch] * (1 - frac) + snap[hi][ch] * frac;
}

// ══════════════════════════════════════════════════
// SERVER-SIDE EDITING APIS (100% Secure Algorithm Execution)
// ══════════════════════════════════════════════════

// 1. OFFSET API
app.post('/api/edit/offset', verifyToken, (req, res) => {
  try {
    const { gbdBase64, chs, deg, rng, r0, r1 } = req.body;
    if (!gbdBase64 || !Array.isArray(chs) || isNaN(deg)) {
      return res.status(400).json({ error: '無效的參數規格' });
    }

    const buf = Buffer.from(gbdBase64, 'base64');
    const hdr = buf.toString('ascii', 0, HEADER_SIZE).replace(/\0/g, '');
    const { counts } = parseGBDHeader(hdr);
    const rawDelta = Math.round(deg * 10);

    let startRec = 0;
    let endRec = counts - 1;
    if (rng === 'custom') {
      if (r0 != null) startRec = clamp(r0, 0, counts - 1);
      if (r1 != null) endRec   = clamp(r1, 0, counts - 1);
    }

    for (let i = startRec; i <= endRec; i++) {
      const base = HEADER_SIZE + i * BYTES_PER_REC;
      for (const c of chs) {
        const offset = base + c * 2;
        const curVal = buf.readInt16BE(offset);
        const newVal = clamp(curVal + rawDelta, -32768, 32767);
        buf.writeInt16BE(newVal, offset);
      }
    }

    return res.json({
      success: true,
      gbdBase64: buf.toString('base64'),
      message: `✓ 伺服器端運算完成：${chs.length} 通道 × ${endRec - startRec + 1} 筆 數據偏移 ${deg > 0 ? '+' : ''}${deg}°C`
    });
  } catch(e) {
    return res.status(500).json({ error: '伺服器運算失敗：' + e.message });
  }
});

// 2. CURVE EXTENSION API
app.post('/api/edit/extend', verifyToken, (req, res) => {
  try {
    const { gbdBase64, r0, r1, r2, type, appendTail } = req.body;
    if (!gbdBase64 || r0 == null || r1 == null || r2 == null || r0 < 0 || r1 <= r0 || r2 <= r1) {
      return res.status(400).json({ error: '時間延伸參數不正確 (需 T0 < T1 < T2)' });
    }

    const srcBuf = Buffer.from(gbdBase64, 'base64');
    let hdr = srcBuf.toString('ascii', 0, HEADER_SIZE).replace(/\0/g, '');
    const { counts, startT, sample } = parseGBDHeader(hdr);

    const r0c = clamp(r0, 0, counts - 1);
    const r1c = clamp(r1, 0, counts - 1);
    const steps = r2 - r0;
    const origSpan = r1c - r0c;
    const newCount = r2 + 1;

    // Read source snapshot records
    const srcRecords = [];
    for (let i = r0c; i <= r1c; i++) {
      const rec = new Int16Array(NUM_CH_ALL);
      const base = HEADER_SIZE + i * BYTES_PER_REC;
      for (let c = 0; c < NUM_CH_ALL; c++) rec[c] = srcBuf.readInt16BE(base + c * 2);
      srcRecords.push(rec);
    }
    const vS = srcRecords[0];
    const vE = srcRecords[srcRecords.length - 1];

    // Read tail records if checked
    const tailRecords = [];
    if (appendTail && r1c + 1 < counts) {
      for (let i = r1c + 1; i < counts; i++) {
        const rec = new Int16Array(NUM_CH_ALL);
        const base = HEADER_SIZE + i * BYTES_PER_REC;
        for (let c = 0; c < NUM_CH_ALL; c++) rec[c] = srcBuf.readInt16BE(base + c * 2);
        tailRecords.push(rec);
      }
    }

    // Build all output records
    const allOutputRecs = [];
    // Keep 0 .. r0-1
    for (let i = 0; i < r0; i++) {
      const rec = new Int16Array(NUM_CH_ALL);
      const base = HEADER_SIZE + i * BYTES_PER_REC;
      for (let c = 0; c < NUM_CH_ALL; c++) rec[c] = srcBuf.readInt16BE(base + c * 2);
      allOutputRecs.push(rec);
    }

    // Interpolate r0 .. r2
    for (let s = 0; s <= steps; s++) {
      const x = s / steps;
      const rec = new Int16Array(NUM_CH_ALL);

      for (let c = 0; c < NUM_CH_DISP; c++) {
        let v;
        if (type === 'stretch') {
          const srcPos = x * origSpan;
          v = recLerp(srcRecords, srcPos, c);
        } else if (type === 'cycle') {
          const cycPos  = (x * steps % origSpan);
          const baseVal = recLerp(srcRecords, cycPos, c);
          const delta   = baseVal - vS[c];
          v = vS[c] + delta + (vE[c] - vS[c]) * x;
        } else {
          const fx = mathFx(x, type);
          v = vS[c] + (vE[c] - vS[c]) * fx;
        }
        rec[c] = clamp(Math.round(v), -32768, 32767);
      }
      allOutputRecs.push(rec);
    }

    // Append tail records
    if (tailRecords.length > 0) {
      for (const rec of tailRecords) allOutputRecs.push(rec);
    }

    const finalCount = allOutputRecs.length;
    const finalStopT = new Date(startT.getTime() + (finalCount - 1) * sample * 1000);

    // Build output buffer
    const outBuf = Buffer.alloc(HEADER_SIZE + finalCount * BYTES_PER_REC);

    // Update Header
    hdr = hdr.replace(/Counts\s*=\s*\d+/, `Counts    =      ${String(finalCount).padStart(6)}`);
    hdr = hdr.replace(/Stop\s*=\s*[\d-]+,[\d:]+/, `Stop      = ${fmtGBDDate(finalStopT)}`);
    const encHdr = Buffer.from(hdr, 'ascii');
    encHdr.copy(outBuf, 0, 0, Math.min(encHdr.length, HEADER_SIZE));

    // Write binary records
    for (let i = 0; i < finalCount; i++) {
      const base = HEADER_SIZE + i * BYTES_PER_REC;
      const rec = allOutputRecs[i];
      for (let c = 0; c < NUM_CH_ALL; c++) {
        outBuf.writeInt16BE(rec[c], base + c * 2);
      }
    }

    return res.json({
      success: true,
      gbdBase64: outBuf.toString('base64'),
      message: `✓ 伺服器端延伸完成：新筆數 ${finalCount} 筆`
    });
  } catch(e) {
    return res.status(500).json({ error: '伺服器延伸運算失敗：' + e.message });
  }
});

// 3. DATE TIME EDIT API
app.post('/api/edit/datetime', verifyToken, (req, res) => {
  try {
    const { gbdBase64, newStartStr } = req.body;
    if (!gbdBase64 || !newStartStr) {
      return res.status(400).json({ error: '無效的日期時間規格' });
    }

    const newStart = new Date(newStartStr.replace(' ', 'T'));
    if (isNaN(newStart.getTime())) {
      return res.status(400).json({ error: '日期時間格式錯誤 (格式: YYYY-MM-DD HH:mm:ss)' });
    }

    const buf = Buffer.from(gbdBase64, 'base64');
    let hdr = buf.toString('ascii', 0, HEADER_SIZE).replace(/\0/g, '');
    const { counts, sample } = parseGBDHeader(hdr);

    const newStop = new Date(newStart.getTime() + (counts - 1) * sample * 1000);

    hdr = hdr.replace(/Start\s*=\s*[\d-]+,[\d:]+/, `Start     = ${fmtGBDDate(newStart)}`);
    hdr = hdr.replace(/Stop\s*=\s*[\d-]+,[\d:]+/, `Stop      = ${fmtGBDDate(newStop)}`);

    const encHdr = Buffer.from(hdr, 'ascii');
    encHdr.copy(buf, 0, 0, Math.min(encHdr.length, HEADER_SIZE));

    return res.json({
      success: true,
      gbdBase64: buf.toString('base64'),
      message: '✓ 伺服器端起始時間已更新'
    });
  } catch(e) {
    return res.status(500).json({ error: '伺服器處理失敗：' + e.message });
  }
});

// Start Server
app.listen(PORT, () => {
  console.log(`==================================================`);
  console.log(`🚀 GBD WebServer ＋ 後端 API 資安伺服器已啟動`);
  console.log(`🌐 服務網址: http://localhost:${PORT}`);
  console.log(`🔒 防護等級: 100% Server-Side API 安全防護 (IP 限額 5 次)`);
  console.log(`==================================================`);
});
