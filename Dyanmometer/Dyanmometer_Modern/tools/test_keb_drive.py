"""
===============================================================================
KEB 驅動器 (DIN 66019 協定) 獨立通訊測試工具 (Test Tool: KEB Drive)
支援:
  1. DIN 66019 / ANSI x3.28 協定封包生成與 BCC 校驗 (原生 Python 實作，免 32-bit DLL)
  2. 讀取 ru01 (設定轉速)、ru07 (實際轉速)、ru15 (視在電流)、ru18 (DC Bus 電壓)、Sy51 (狀態字元)
  3. 寫入加載轉速 / 轉矩命令
===============================================================================
"""

import sys
import time
import argparse
from typing import Optional, Tuple

try:
    import serial
except ImportError:
    serial = None


class KebDin66019Protocol:
    """
    KEB DIN 66019 / ANSI x3.28 協定編解碼器
    """
    EOT = 0x04
    STX = 0x02
    ETX = 0x03
    ACK = 0x06
    NAK = 0x15

    @staticmethod
    def calc_bcc(data_bytes: bytes) -> int:
        """計算 BCC 校驗碼 (從 STX 之後到 ETX 含 ETX 的所有 Byte 進行 XOR 運算)"""
        bcc = 0
        for b in data_bytes:
            bcc ^= b
        return bcc

    @classmethod
    def build_read_telegram(cls, inv_addr: int, param_addr: int, param_set: int = 0) -> bytes:
        """
        建立讀取參數請求電文 (Read Telegram)
        格式: <EOT><Addr_H><Addr_L><STX><Param_4Hex><Set_2Hex><ETX><BCC>
        """
        addr_str = f"{inv_addr:02X}"
        payload = f"{param_addr:04X}{param_set:02X}".encode("ascii") + bytes([cls.ETX])
        bcc = cls.calc_bcc(payload)

        packet = bytearray()
        packet.append(cls.EOT)
        packet.extend(addr_str.encode("ascii"))
        packet.append(cls.STX)
        packet.extend(payload)
        packet.append(bcc)
        return bytes(packet)

    @classmethod
    def build_write_telegram(cls, inv_addr: int, param_addr: int, param_set: int, data_val: int) -> bytes:
        """
        建立寫入參數請求電文 (Write Telegram)
        """
        addr_str = f"{inv_addr:02X}"
        # 32-bit 整數轉換為 8 位 16 進位字串
        data_str = f"{data_val & 0xFFFFFFFF:08X}"
        payload = f"{param_addr:04X}{param_set:02X}{data_str}".encode("ascii") + bytes([cls.ETX])
        bcc = cls.calc_bcc(payload)

        packet = bytearray()
        packet.append(cls.EOT)
        packet.extend(addr_str.encode("ascii"))
        packet.append(cls.STX)
        packet.extend(payload)
        packet.append(bcc)
        return bytes(packet)

    @classmethod
    def parse_read_response(cls, resp_bytes: bytes) -> Optional[int]:
        """
        解析讀取回應電文: <STX><Param_4Hex><Set_2Hex><Data_8Hex><ETX><BCC>
        """
        if len(resp_bytes) < 16:
            return None
        try:
            stx_idx = resp_bytes.find(bytes([cls.STX]))
            etx_idx = resp_bytes.find(bytes([cls.ETX]))
            if stx_idx != -1 and etx_idx > stx_idx:
                content = resp_bytes[stx_idx + 1:etx_idx].decode("ascii")
                # 格式: 4碼地址 + 2碼Set + 8碼數值
                if len(content) >= 14:
                    hex_data = content[6:14]
                    val = int(hex_data, 16)
                    # 處理有號整數 (32-bit signed)
                    if val >= 0x80000000:
                        val -= 0x100000000
                    return val
        except Exception:
            return None
        return None


# 常用 KEB 參數地址表
KEB_PARAMS = {
    "ru01": (0x0101, "設定轉速 Set Speed Display", 0.125, "rpm"),
    "ru07": (0x0107, "實際轉速 Actual Speed Display", 0.125, "rpm"),
    "ru15": (0x010F, "視在電流 Apparent Current", 0.01, "A"),
    "ru18": (0x0112, "DC BUS 電壓 Actual DC Bus Voltage", 1.0, "V"),
    "Sy51": (0x0033, "狀態字元 Status Word Low", 1.0, "Hex"),
}


def test_keb_drive(port: str, baudrate: int = 9600, inv_addr: int = 1, count: int = 10, interval: float = 0.5):
    if serial is None:
        print("❌ 請先安裝 pyserial: pip install pyserial")
        return

    print(f"\n🚀 正在連接 KEB 驅動器: {port} @ {baudrate} bps (8-E-1 / 8-N-1)...")
    try:
        ser = serial.Serial(
            port=port,
            baudrate=baudrate,
            bytesize=serial.EIGHTBITS,
            parity=serial.PARITY_EVEN,  # DIN 66019 標準常使用 Even Parity
            stopbits=serial.STOPBITS_ONE,
            timeout=0.5
        )
    except Exception as e:
        print(f"❌ 無法開啟串列埠 {port}: {e}")
        return

    print("✅ 串列埠連線成功！即將輪詢讀取 KEB 核心運行參數...")
    print("-" * 85)
    print(f"{'次數':^6} | {'ru01 (設定rpm)':^15} | {'ru07 (實際rpm)':^15} | {'ru15 (電流 A)':^15} | {'ru18 (DC V)':^12} | {'Sy51 狀態':^10}")
    print("-" * 85)

    try:
        for i in range(1, count + 1):
            results = {}
            for name, (addr, desc, scale, unit) in KEB_PARAMS.items():
                req = KebDin66019Protocol.build_read_telegram(inv_addr=inv_addr, param_addr=addr, param_set=0)
                ser.write(req)
                time.sleep(0.04)
                raw_resp = ser.read(32)
                raw_val = KebDin66019Protocol.parse_read_response(raw_resp)
                if raw_val is not None:
                    scaled = raw_val * scale
                    results[name] = scaled
                else:
                    results[name] = None

            ru01_str = f"{results.get('ru01', 0):>10.1f} rpm" if results.get('ru01') is not None else "--"
            ru07_str = f"{results.get('ru07', 0):>10.1f} rpm" if results.get('ru07') is not None else "--"
            ru15_str = f"{results.get('ru15', 0):>10.2f} A" if results.get('ru15') is not None else "--"
            ru18_str = f"{results.get('ru18', 0):>8.0f} V" if results.get('ru18') is not None else "--"
            sy51_str = f"0x{int(results.get('Sy51', 0)):04X}" if results.get('Sy51') is not None else "--"

            print(f"[{i:03d}]  | {ru01_str:^15} | {ru07_str:^15} | {ru15_str:^15} | {ru18_str:^12} | {sy51_str:^10}")
            time.sleep(interval)

    except KeyboardInterrupt:
        print("\n⏹️ 使用者手動中斷測試。")
    finally:
        ser.close()
        print("\n🔒 串列埠已關閉。")


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description="KEB 驅動器 (DIN 66019) 獨立通訊測試工具")
    parser.add_argument("--port", "-p", type=str, default="COM1", help="串列埠名稱 (如 COM1, COM2)")
    parser.add_argument("--baud", "-b", type=int, default=9600, help="鮑率 Baud Rate (預設: 9600)")
    parser.add_argument("--addr", "-a", type=int, default=1, help="KEB 變頻器站號 Inverter Address (預設: 1)")
    parser.add_argument("--count", "-n", type=int, default=20, help="測試取樣次數 (預設: 20)")
    parser.add_argument("--interval", "-i", type=float, default=0.5, help="取樣間隔秒數 (預設: 0.5)")

    args = parser.parse_args()
    test_keb_drive(args.port, args.baud, args.addr, args.count, args.interval)
