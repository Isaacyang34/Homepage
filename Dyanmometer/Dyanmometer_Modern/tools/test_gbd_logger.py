"""
===============================================================================
GBD 網路溫度記錄器 (Graphtec GL 系列) 獨立通訊測試工具 (Test Tool: GBD Logger)
支援:
  1. 透過乙太網路 TCP/IP Socket (Port 8023 / 8024) 連線 Graphtec GL240/GL840 溫度記錄器
  2. 即時輪詢各通道熱電偶/測溫體溫度 (:MEAS:ALL?)
  3. 支援多點通道名稱 (如 CH1 定子U, CH2 定子V, CH3 定子W, CH4 前軸承, CH5 後軸承)
===============================================================================
"""

import sys
import time
import socket
import argparse


def test_gbd_logger_ethernet(ip: str = "192.168.1.101", port: int = 8023, count: int = 20, interval: float = 1.0):
    print(f"\n📡 正在透過 Ethernet TCP/IP 連接 GBD 溫度記錄器: {ip}:{port} ...")
    try:
        sock = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
        sock.settimeout(3.0)
        sock.connect((ip, port))
        print("✅ 網路連線成功！")
    except Exception as e:
        print(f"❌ 網路連線失敗 ({ip}:{port}): {e}")
        return

    try:
        sock.sendall(b"*IDN?\r\n")
        time.sleep(0.1)
        idn = sock.recv(1024).decode("ascii", errors="ignore").strip()
        print(f"📟 記錄器型號識別 (*IDN?): {idn if idn else '[已連線，繼續讀取數據]'}")
    except Exception as e:
        print(f"⚠️ 讀取 *IDN? 逾時: {e}")

    print("\n🌡️ 開始即時讀取多通道溫度 (:MEAS:ALL?)...")
    print("-" * 80)
    print(f"{'次數':^6} | {'CH1 (定子U)':^12} | {'CH2 (定子V)':^12} | {'CH3 (定子W)':^12} | {'CH4 (前軸承)':^12} | {'CH5 (後軸承)':^12}")
    print("-" * 80)

    try:
        for i in range(1, count + 1):
            cmd = b":MEAS:ALL?\r\n"
            sock.sendall(cmd)
            time.sleep(0.08)
            resp = sock.recv(2048).decode("ascii", errors="ignore").strip()

            if not resp:
                print(f"[{i:03d}] ⚠️ 等待回應逾時")
                time.sleep(interval)
                continue

            # 解析各通道溫度數據
            tokens = [t.strip() for t in resp.split(",") if t.strip()]
            ch_temps = []
            for t in tokens:
                try:
                    ch_temps.append(float(t))
                except ValueError:
                    pass

            c1 = f"{ch_temps[0]:.1f}°C" if len(ch_temps) > 0 else "--"
            c2 = f"{ch_temps[1]:.1f}°C" if len(ch_temps) > 1 else "--"
            c3 = f"{ch_temps[2]:.1f}°C" if len(ch_temps) > 2 else "--"
            c4 = f"{ch_temps[3]:.1f}°C" if len(ch_temps) > 3 else "--"
            c5 = f"{ch_temps[4]:.1f}°C" if len(ch_temps) > 4 else "--"

            print(f"[{i:03d}]  | {c1:^12} | {c2:^12} | {c3:^12} | {c4:^12} | {c5:^12}")
            time.sleep(interval)

    except KeyboardInterrupt:
        print("\n⏹️ 使用者手動中斷測試。")
    finally:
        sock.close()
        print("\n🔒 Socket 連線已關閉。")


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description="GBD 網路溫度記錄器 (Graphtec GL) 獨立通訊測試工具")
    parser.add_argument("--ip", type=str, default="192.168.1.101", help="記錄器 IP 位址 (預設: 192.168.1.101)")
    parser.add_argument("--port", "-p", type=int, default=8023, help="TCP 連接埠 (預設: 8023)")
    parser.add_argument("--count", "-n", type=int, default=20, help="測試取樣次數 (預設: 20)")
    parser.add_argument("--interval", "-i", type=float, default=1.0, help="取樣間隔秒數 (預設: 1.0)")

    args = parser.parse_args()
    test_gbd_logger_ethernet(args.ip, args.port, args.count, args.interval)
