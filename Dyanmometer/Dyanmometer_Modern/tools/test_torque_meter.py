"""
===============================================================================
USB / RS-232 扭力計通訊獨立測試工具 (Test Tool: USB Torque Meter)
支援:
  1. 自動掃描系統所有可用 COM 埠 (Virtual COM Port / FTDI / USB-to-Serial)
  2. 4503A 扭力計標準指令 (MEAS:TORQ?) 交互測試
  3. 連續主動讀取模式 (Streaming / Polling)
  4. 滑動平均濾波 (Moving Average) 與自訂 Scale 換算
===============================================================================
"""

import sys
import time
import argparse

try:
    import serial
    import serial.tools.list_ports
except ImportError:
    serial = None


def list_available_ports():
    print("\n=======================================================")
    print(" 🔍 正在掃描系統可用串列埠 (COM Ports)...")
    print("=======================================================")
    if serial is None:
        print("❌ 未安裝 pyserial 套件。請執行: pip install pyserial")
        return []
    
    ports = list(serial.tools.list_ports.comports())
    if not ports:
        print("⚠️ 未偵測到任何 COM 埠。請確認 USB 扭力計已連接電腦並安裝驅動。")
        return []

    for idx, p in enumerate(ports):
        print(f"  [{idx + 1}] {p.device} - {p.description} (VID:PID = {p.hwid})")
    return ports


def test_torque_meter(port_name: str, baudrate: int = 9600, query_cmd: str = "MEAS:TORQ?", 
                      scale: float = 1.0, count: int = 20, interval: float = 0.2):
    if serial is None:
        print("❌ 請先安裝 pyserial: pip install pyserial")
        return

    print(f"\n🚀 正在連線扭力計: {port_name} @ {baudrate} bps (8-N-1)...")
    try:
        ser = serial.Serial(
            port=port_name,
            baudrate=baudrate,
            bytesize=serial.EIGHTBITS,
            parity=serial.PARITY_NONE,
            stopbits=serial.STOPBITS_ONE,
            timeout=1.0
        )
    except Exception as e:
        print(f"❌ 無法開啟串列埠 {port_name}: {e}")
        return

    print(f"✅ 連線成功！即將發送指令或讀取數據 (測試 {count} 次，間隔 {interval} 秒)...")
    print("-" * 65)
    print(f"{'取樣次數':^8} | {'原始回應/字串':^20} | {'解析轉矩 (Nm)':^14} | {'縮放後轉矩 (Nm)':^14}")
    print("-" * 65)

    buffer_vals = []
    window_size = 5

    try:
        for i in range(1, count + 1):
            if query_cmd:
                # 4503A 標準詢問指令
                cmd_bytes = (query_cmd + "\r\n").encode("ascii")
                ser.write(cmd_bytes)
                time.sleep(0.05)

            line = ser.readline().decode("ascii", errors="ignore").strip()
            if not line:
                print(f"[{i:03d}] ⚠️ 等待逾時，無回傳資料")
                time.sleep(interval)
                continue

            # 嘗試從回應中提取浮點數數值
            parsed_torque = None
            try:
                # 去除前綴非數字部分 (例如 "+0125.4" 或 "TORQ:+125.4")
                cleaned = "".join(c for c in line if c in "0123456789+-.eE")
                if cleaned:
                    parsed_torque = float(cleaned)
            except ValueError:
                parsed_torque = None

            if parsed_torque is not None:
                scaled_torque = parsed_torque * scale
                buffer_vals.append(scaled_torque)
                if len(buffer_vals) > window_size:
                    buffer_vals.pop(0)
                avg_torque = sum(buffer_vals) / len(buffer_vals)

                print(f"[{i:03d}]    | {line:<20} | {parsed_torque:>12.2f}   | {scaled_torque:>10.2f} (均:{avg_torque:>6.2f})")
            else:
                print(f"[{i:03d}]    | {line:<20} | {'[無法解析]':^14} | {'--':^14}")

            time.sleep(interval)

    except KeyboardInterrupt:
        print("\n⏹️ 使用者手動中斷測試。")
    finally:
        ser.close()
        print("\n🔒 串列埠已關閉。測試結束。")


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description="USB / RS-232 扭力計獨立通訊測試工具")
    parser.add_argument("--port", "-p", type=str, default="", help="串列埠名稱 (如 COM3, COM4)")
    parser.add_argument("--baud", "-b", type=int, default=9600, help="鮑率 Baud Rate (預設: 9600)")
    parser.add_argument("--cmd", "-c", type=str, default="MEAS:TORQ?", help="詢問指令 (預設: MEAS:TORQ?，留空則純監聽)")
    parser.add_argument("--scale", "-s", type=float, default=1.0, help="手動轉矩縮放比例 TorqueManualScale (預設: 1.0)")
    parser.add_argument("--count", "-n", type=int, default=30, help="測試取樣次數 (預設: 30)")
    parser.add_argument("--interval", "-i", type=float, default=0.2, help="取樣間隔秒數 (預設: 0.2)")

    args = parser.parse_args()

    if not args.port:
        ports = list_available_ports()
        if ports:
            print("\n👉 請在命令列指定連接埠，例如: python test_torque_meter.py -p COM3")
            if len(ports) == 1:
                auto_p = ports[0].device
                print(f"💡 偵測到單一可用埠 {auto_p}，是否直接開始測試？[Y/n]: ", end="")
                ans = input().strip().lower()
                if ans in ["", "y", "yes"]:
                    test_torque_meter(auto_p, args.baud, args.cmd, args.scale, args.count, args.interval)
        sys.exit(0)

    test_torque_meter(args.port, args.baud, args.cmd, args.scale, args.count, args.interval)
