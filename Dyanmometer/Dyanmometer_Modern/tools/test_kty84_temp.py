"""
===============================================================================
KTY84 溫度感測器換算與採樣測試工具 (Test Tool: KTY84 Temperature)
===============================================================================
"""

import sys
import os
sys.path.append(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
from core.engine_calc import DynamometerCalculator, MovingAverageFilter


def test_temperature_conversion():
    print("\n=======================================================")
    print(" 🌡️ KTY84 溫度感測換算驗證 (0~10V 對應 20~150 °C)")
    print("=======================================================")

    test_voltages = [0.0, 1.0, 2.5, 5.0, 7.5, 9.0, 10.0]
    print(f"{'類比電壓 (V)':^14} | {'換算溫度 (°C)':^16} | {'公式計算過程'}")
    print("-" * 65)

    for v in test_voltages:
        temp = DynamometerCalculator.kty84_voltage_to_temperature(v)
        print(f"{v:>10.2f} V   | {temp:>12.2f} °C    | Temp = {v:.2f} * ((150-20)/10) + 20")

    print("\n✅ 溫度換算邏輯驗證完畢！")


if __name__ == "__main__":
    test_temperature_conversion()
