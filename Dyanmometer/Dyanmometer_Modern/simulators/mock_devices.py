"""
===============================================================================
動力計硬體模擬器 (Dynamometer Mock Hardware Simulator)
提供虛擬之 KEB 驅動器、網路功率分析儀、USB 扭力計與 KTY84 溫度感測數據
===============================================================================
"""

import math
import random
import time
from typing import Dict, Any


class MockDynamometerSystem:
    def __init__(self):
        self.target_speed_rpm = 1500.0
        self.target_torque_nm = 320.0
        self.current_speed_rpm = 0.0
        self.current_torque_nm = 0.0
        self.motor_temperature = 25.0
        self.start_time = time.time()

    def update(self, target_rpm: float, target_torque: float) -> Dict[str, Any]:
        """
        模擬一步物理狀態演進
        """
        self.target_speed_rpm = target_rpm
        self.target_torque_nm = target_torque

        # 模擬轉速平滑升降
        self.current_speed_rpm += (self.target_speed_rpm - self.current_speed_rpm) * 0.15 + random.uniform(-1.5, 1.5)
        self.current_torque_nm += (self.target_torque_nm - self.current_torque_nm) * 0.15 + random.uniform(-2.0, 2.0)

        # 機械功率 (kW)
        p_mech_kw = (max(0, self.current_torque_nm) * max(0, self.current_speed_rpm)) / 9549.3

        # 模擬電氣參數
        eff_factor = 0.92 - (self.current_speed_rpm / 3000.0) * 0.05
        p_elec_kw = p_mech_kw / eff_factor if eff_factor > 0 and p_mech_kw > 0 else 0.5

        # 模擬三相電壓電流
        v_sigma = 299.0 + random.uniform(-0.5, 0.5)
        u_v = v_sigma + random.uniform(-0.3, 0.3)
        v_v = v_sigma + random.uniform(-0.3, 0.3)
        w_v = v_sigma + random.uniform(-0.3, 0.3)

        pf_sigma = 0.72 + random.uniform(-0.02, 0.02)
        i_sigma = (p_elec_kw * 1000.0) / (math.sqrt(3) * v_sigma * pf_sigma) if v_sigma > 0 else 0.0
        u_i = i_sigma + random.uniform(-0.5, 0.5)
        v_i = i_sigma + random.uniform(-0.5, 0.5)
        w_i = i_sigma + random.uniform(-0.5, 0.5)

        # 模擬溫升
        self.motor_temperature += (p_elec_kw - p_mech_kw) * 0.005 + random.uniform(-0.05, 0.05)
        self.motor_temperature = max(20.0, min(140.0, self.motor_temperature))

        kt = max(0, self.current_torque_nm) / i_sigma if i_sigma > 0.1 else 0.0
        eff = (p_mech_kw / p_elec_kw) * 100.0 if p_elec_kw > 0.1 else 0.0

        return {
            "speed_rpm": round(self.current_speed_rpm, 1),
            "torque_nm": round(self.current_torque_nm, 2),
            "power_mech_kw": round(p_mech_kw, 2),
            "power_elec_kw": round(p_elec_kw, 2),
            "efficiency_pct": round(eff, 1),
            "kt_val": round(kt, 2),
            "u_volt": round(u_v, 2),
            "v_volt": round(v_v, 2),
            "w_volt": round(w_v, 2),
            "u_curr": round(u_i, 2),
            "v_curr": round(v_i, 2),
            "w_curr": round(w_i, 2),
            "sigma_volt": round(v_sigma, 2),
            "sigma_curr": round(i_sigma, 2),
            "sigma_power_kw": round(p_elec_kw, 2),
            "sigma_pf": round(pf_sigma, 3),
            "temp_1": round(self.motor_temperature, 1),
            "temp_2": round(self.motor_temperature - 2.0, 1),
            "temp_3": round(self.motor_temperature - 3.5, 1),
            "temp_4": round(self.motor_temperature + 1.2, 1),
            "temp_5": round(self.motor_temperature + 0.5, 1),
        }
