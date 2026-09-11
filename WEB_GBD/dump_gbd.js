// GBD binary header analyzer
const fs = require('fs');
const path = require('path');

const fname = process.argv[2] || '260826-162120_UG.GBD';
const buf = fs.readFileSync(path.join(__dirname, fname));

// Read header as ASCII text
const HEADER_SIZE = 12288;
const hdrBytes = buf.slice(0, HEADER_SIZE);
const hdr = hdrBytes.toString('ascii').replace(/\0/g, ' ');

console.log('=== KEY FIELDS in text header ===');
hdr.split('\n').forEach(line => {
  const l = line.trim();
  if (l.match(/Start|Stop|Count|Sample|Model|Vendor|Header|Date|Time/i)) {
    console.log('  ' + JSON.stringify(l));
  }
});

console.log('\n=== DATE STRINGS in header ===');
const dateRe = /20\d{2}[-\/]\d{2}[-\/]\d{2}/g;
let m;
while ((m = dateRe.exec(hdr)) !== null) {
  const ctx = hdr.slice(Math.max(0, m.index - 30), m.index + 50);
  console.log(`  offset=${m.index}: ${JSON.stringify(ctx)}`);
}

console.log('\n=== HEX DUMP first 512 bytes ===');
for (let i = 0; i < Math.min(512, buf.length); i += 16) {
  const chunk = buf.slice(i, i + 16);
  const hex = Array.from(chunk).map(b => b.toString(16).padStart(2, '0')).join(' ');
  const asc = Array.from(chunk).map(b => (b >= 32 && b < 127) ? String.fromCharCode(b) : '.').join('');
  console.log(`  ${i.toString(16).padStart(4,'0')}: ${hex.padEnd(47)}  ${asc}`);
}

// Scan for binary Unix timestamps
console.log('\n=== SCAN for binary timestamps (near header boundary) ===');
const TS_MIN = new Date('2020-01-01').getTime() / 1000;
const TS_MAX = new Date('2030-01-01').getTime() / 1000;
for (let off = 0; off < HEADER_SIZE; off += 2) {
  if (off + 4 > buf.length) break;
  const be = buf.readUInt32BE(off);
  const le = buf.readUInt32LE(off);
  for (const [val, endian] of [[be,'BE'], [le,'LE']]) {
    if (val >= TS_MIN && val <= TS_MAX) {
      const dt = new Date(val * 1000);
      console.log(`  offset=${off} ${endian}: ${val} -> ${dt.toISOString()}`);
    }
  }
}

console.log('\n=== FULL header text (first 1000 printable chars) ===');
let printable = '';
for (let i = 0; i < Math.min(hdr.length, 3000); i++) {
  const c = hdr[i];
  if (c === '\n') printable += '\n';
  else if (c === '\r') continue;
  else if (c >= ' ' && c < '\x7f') printable += c;
}
console.log(printable.trim());
