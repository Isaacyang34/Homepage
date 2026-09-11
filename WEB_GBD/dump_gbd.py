import sys, struct, re

fname = '260826-162120_UG.GBD'
with open(fname, 'rb') as f:
    raw = f.read()

# --- Text header ---
header_bytes = raw[:12288]
hdr = header_bytes.replace(b'\x00', b' ').decode('ascii', errors='replace')

print("=== TEXT HEADER (key fields) ===")
for line in hdr.split('\n'):
    line = line.strip()
    if any(k in line for k in ['Start', 'Stop', 'Count', 'Sample', 'Model', 'Vendor', 'Header', 'Date', 'Time']):
        print(repr(line))

print("\n=== SEARCH FOR DATE STRINGS in entire header (2026, 2007 etc) ===")
# Find any date-like patterns
for m in re.finditer(r'(20\d{6}|2[0-9]{3}-[01]\d-[0-3]\d)', hdr):
    start = max(0, m.start()-20)
    end   = min(len(hdr), m.end()+20)
    print(f"  offset={m.start()}: ...{repr(hdr[start:end])}...")

print("\n=== HEX DUMP of bytes 0-256 ===")
for i in range(0, min(256, len(raw)), 16):
    chunk = raw[i:i+16]
    hex_str = ' '.join(f'{b:02x}' for b in chunk)
    asc_str = ''.join(chr(b) if 32 <= b < 127 else '.' for b in chunk)
    print(f"  {i:04x}: {hex_str:<47}  {asc_str}")

print("\n=== SCAN for possible binary timestamp near offset 12288 boundary ===")
# Check if there are any 4-byte sequences that look like timestamps near data start
HEADER_SIZE = 12288
for off in range(HEADER_SIZE - 32, HEADER_SIZE + 64, 2):
    if off + 4 <= len(raw):
        val = struct.unpack_from('>I', raw, off)[0]
        val_le = struct.unpack_from('<I', raw, off)[0]
        # Check if it looks like a unix timestamp (year 2020-2030 range = ~1577836800 to ~1893456000)
        if 1577836800 <= val <= 1893456000 or 1577836800 <= val_le <= 1893456000:
            print(f"  Possible timestamp at offset {off}: BE={val}, LE={val_le}")
            import datetime
            if 1577836800 <= val <= 1893456000:
                print(f"    BE -> {datetime.datetime.utcfromtimestamp(val)}")
            if 1577836800 <= val_le <= 1893456000:
                print(f"    LE -> {datetime.datetime.utcfromtimestamp(val_le)}")

print("\n=== Done ===")
