"""
===============================================================================
功率分析儀 (Power Meter) 獨立通訊測試工具 (Test Tool: Power Meter)
支援:
  1. 乙太網路連接模式 (Ethernet TCP/IP Socket - SCPI / Yokogawa WT 系列標準)
  2. GPIB 連接模式 (NI-VISA / PyVISA GPIB0::1::INSTR)
  3. 讀取與解析各通道電氣參數 (U, I, P, PowerFactor, Sigma 總值)
===============================================================================
"""

import sys
import time
import socket
import argparse
from typing import List, Dict, Optional


def test_power_meter_ethernet(ip: str, port: int = 10001, count: int = 10, interval: float = 0.5):
    """
    透過乙太網路 TCP/IP Socket 測試功率分析儀
    """
    print(f"\n🌐 正在透過 Ethernet TCP/IP 連接功率分析儀: {ip}:{port} ...")
    try:
        sock = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
        sock.settimeout(3.0)
        sock.connect((ip, port))
        print("✅ 網路連線成功！")
    except Exception as e:
        print(f"❌ 網路連線失敗 ({ip}:{port}): {e}")
        return

    # 初始化配置查詢或詢問型號
    try:
        # 查詢設備識別碼
        sock.sendall(b"*IDN?\n")
        time.sleep(0.1)
        idn = sock.recv(1024).decode("ascii", errors="ignore").strip()
        print(f"📟 儀表型號識別 (*IDN?): {idn if idn else '[無回應，繼續查詢數值]'}")
    except Exception as e:
        print(f"⚠️ 讀取 *IDN? 逾時: {e}")

    print("\n📊 開始讀取電氣參數 (:NUMeric:NORMal:VALue?)...")
    print("-" * 80)
    print(f"{'次數':^6} | {'Sigma 電壓(V)':^14} | {'Sigma 電流(A)':^14} | {'Sigma 功率(kW)':^14} | {'功率因數':^10}")
    print("-" * 80)

    try:
        for i in range(1, count + 1):
            cmd = b":NUMeric:NORMal:VALue?\n"
            sock.sendall(cmd)
            time.sleep(0.08)
            resp = sock.recv(2048).decode("ascii", errors="ignore").strip()

            if not resp:
                print(f"[{i:03d}] ⚠️ 等待回應逾時")
                time.sleep(interval)
                continue

            # Yokogawa / SCPI 回應通常為以逗號分隔的浮點數列表
            tokens = [t.strip() for t in resp.split(",") if t.strip()]
            
            # 解析數值
            sigma_v = 0.0
            sigma_i = 0.0
            sigma_p_kw = 0.0
            sigma_pf = 1.0

            try:
                numeric_vals = [float(t) for t in tokens if t not in ["NAN", "INF", "9.99999E+37", ""]]
                if len(numeric_vals) >= 4:
                    # 若為標準 4 欄位 (U_sigma, I_sigma, P_sigma, PF_sigma) 或完整列表
                    sigma_v = numeric_vals[0]
                    sigma_i = numeric_vals[1]
                    sigma_p_kw = numeric_vals[2] / 1000.0 if numeric_vals[2] > 100 else numeric_vals[2]
                    sigma_pf = numeric_vals[3]
            except Exception:
                pass

            print(f"[{i:03d}]  | {sigma_v:>12.2f} V | {sigma_i:>12.2f} A | {sigma_p_kw:>12.3f} kW | {sigma_pf:>8.3f}")
            time.sleep(interval)

    except KeyboardInterrupt:
        print("\n⏹️ 使用者手動中斷測試。")
    finally:
        sock.close()
        print("\n🔒 網路 Socket 已關閉。測試結束。")


def test_power_meter_gpib(gpib_addr: str = "GPIB0::1::INSTR", count: int = 10, interval: float = 0.5):
    """
    透過 PyVISA / GPIB 測試功率分析儀
    """
    print(f"\n🔌 正在透過 GPIB 連接功率分析儀: {gpib_addr} ...")
    try:
        import pyvisa
    except ImportError:
        print("❌ 未安裝 pyvisa 套件。請執行: pip install pyvisa pyvisa-py")
        return

    try:
        rm = pyvisa.ResourceManager()
        inst = rm.open_resource(gpib_addr)
        inst.timeout = 3000
        print("✅ GPIB 設備開啟成功！")
        
        try:
            idn = inst.query("*IDN?").strip()
            print(f"📟 儀表型號識別 (*IDN?): {idn}")
        except Exception:
            pass

        print("\n📊 開始讀取電氣參數 (:NUMeric:NORMal:VALue?)...")
        for i in range(1, count + 1):
            resp = inst.query(":NUMeric:NORMal:VALue?").strip()
            print(f"[{i:03d}] 回應數據: {resp}")
            time.sleep(interval)

        inst.close()
        print("\n🔒 GPIB 介面已關閉。")
    except Exception as e:
        print(f"❌ GPIB 連線發生錯誤: {e}")


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description="功率分析儀 (Power Meter) 獨立通訊測試工具")
    parser.add_argument("--mode", "-m", choices=["ethernet", "gpib"], default="ethernet", help="通訊模式: ethernet 或 gpib (預設: ethernet)")
    parser.add_argument("--ip", type=str, default="192.168.1.100", help="儀表 IP 位址 (預設: 192.168.1.100)")
    parser.add_argument("--port", "-p", type=int, default=10001, help="TCP 連接埠 (預設: 10001)")
    parser.add_argument("--gpib", type=str, default="GPIB0::1::INSTR", help="GPIB 資源識別碼 (預設: GPIB0::1::INSTR)")
    parser.add_argument("--count", "-n", type=int, default=20, help="測試取樣次數 (預設: 20)")
    parser.add_argument("--interval", "-i", type=float, default=0.5, help="取樣間隔秒數 (預設: 0.5)")

    args = parser.parse_args()

    if args.mode == "ethernet":
        test_power_meter_ethernet(args.ip, args.port, args.count, args.interval)
    else:
        test_power_meter_gpib(args.gpib, args.count, args.interval)
