"""
===============================================================================
Dynamometer Core Calculation & Safety Engine (動力計核心計算與安全控制庫)
新增功能:
  1. 閉迴路轉矩安全防護與突變抑制器 (Torque Closed-Loop Safety & Rate Limiter)
  2. 自動化多段 T-N 曲線測試序列 (Multi-Step T-N Test Sequencer)
  3. 2D 效率地圖網格掃描與插值計算 (Efficiency Map Grid Scanner)
  4. IEC 60034-1 標準 S1/S2/S6 工作制測試序列 (Duty Cycle Test Sequencer)
===============================================================================
"""

import math
import time
from collections import deque
from typing import List, Dict, Optional, Tuple, Callable


class TorqueClosedLoopSafetyGuard:
    """
    轉矩閉迴路安全防護與變化率限幅器 (Anti-Runaway & Rate-of-Change Limiter)
    防止感測器訊號遺失、瞬間突波(Spike)或機械雜訊導致閉迴路控制爆衝失控。
    """
    def __init__(self, 
                 max_allowed_delta_pct: float = 10.0,
                 max_step_torque_change_nm: float = 2.0,
                 signal_timeout_sec: float = 0.5):
        self.max_allowed_delta_pct = max_allowed_delta_pct
        self.max_step_torque_change_nm = max_step_torque_change_nm
        self.signal_timeout_sec = signal_timeout_sec
        
        self.baseline_torque_nm: Optional[float] = None
        self.last_valid_torque_nm: float = 0.0
        self.last_control_output_nm: float = 0.0
        self.last_update_time = time.time()
        
        self.is_alarm: bool = False
        self.alarm_reason: str = ""
        self.warning_active: bool = False
        self.warning_message: str = ""

    def snapshot_baseline(self, current_torque_nm: float, current_output_nm: float):
        self.baseline_torque_nm = current_torque_nm
        self.last_valid_torque_nm = current_torque_nm
        self.last_control_output_nm = current_output_nm
        self.is_alarm = False
        self.warning_active = False
        self.alarm_reason = ""
        self.warning_message = ""

    def evaluate_feedback(self, raw_torque_nm: Optional[float]) -> Tuple[float, bool, str]:
        now = time.time()
        if raw_torque_nm is None or math.isnan(raw_torque_nm):
            if now - self.last_update_time > self.signal_timeout_sec:
                self.is_alarm = True
                self.alarm_reason = "⚠️ 扭力感測訊號中斷 (Signal Lost)！已自動凍結加載量。"
                return self.last_valid_torque_nm, True, self.alarm_reason
            return self.last_valid_torque_nm, False, "訊號短暫遺失，維持上一值"

        self.last_update_time = now

        if self.baseline_torque_nm is not None and self.baseline_torque_nm > 0.5:
            delta = abs(raw_torque_nm - self.baseline_torque_nm)
            delta_pct = (delta / self.baseline_torque_nm) * 100.0

            if delta_pct > self.max_allowed_delta_pct:
                self.warning_active = True
                self.warning_message = (f"⚠️ 扭力偏差過大: 當前 {raw_torque_nm:.2f} Nm "
                                        f"(基準 {self.baseline_torque_nm:.2f} Nm, 偏移 {delta_pct:.1f}% > {self.max_allowed_delta_pct}%)")
            else:
                self.warning_active = False
                self.warning_message = ""

        if self.last_valid_torque_nm > 1.0:
            if abs(raw_torque_nm - self.last_valid_torque_nm) > max(10.0, self.last_valid_torque_nm * 0.5):
                filtered_val = self.last_valid_torque_nm + math.copysign(self.max_step_torque_change_nm * 2, raw_torque_nm - self.last_valid_torque_nm)
                return filtered_val, True, "⚠️ 偵測到瞬間突波訊號 (Spike Detected)，已進行平滑防護"

        self.last_valid_torque_nm = raw_torque_nm
        return raw_torque_nm, self.warning_active, self.warning_message

    def limit_control_output(self, target_output_nm: float) -> float:
        if self.is_alarm:
            return self.last_control_output_nm

        diff = target_output_nm - self.last_control_output_nm
        if abs(diff) > self.max_step_torque_change_nm:
            clamped_output = self.last_control_output_nm + math.copysign(self.max_step_torque_change_nm, diff)
        else:
            clamped_output = target_output_nm

        self.last_control_output_nm = clamped_output
        return clamped_output


class MovingAverageFilter:
    def __init__(self, window_size: int = 10, default_init_val: Optional[float] = None):
        self.window_size = max(1, window_size)
        self.buffer = deque(maxlen=self.window_size)
        self.is_locked = False
        self.locked_value: float = 0.0

        if default_init_val is not None:
            for _ in range(self.window_size):
                self.buffer.append(default_init_val)

    def add_sample(self, val: float) -> float:
        if self.is_locked:
            return self.locked_value
        self.buffer.append(val)
        return sum(self.buffer) / len(self.buffer)

    def get_average(self) -> float:
        if self.is_locked:
            return self.locked_value
        if not self.buffer:
            return 0.0
        return sum(self.buffer) / len(self.buffer)

    def lock(self, custom_val: Optional[float] = None):
        self.is_locked = True
        self.locked_value = custom_val if custom_val is not None else self.get_average()

    def unlock(self):
        self.is_locked = False


class DynamometerCalculator:
    @staticmethod
    def calc_mechanical_power_kw(torque_nm: float, speed_rpm: float) -> float:
        if speed_rpm <= 0 or torque_nm <= 0:
            return 0.0
        return (torque_nm * speed_rpm) / 9549.3

    @staticmethod
    def calc_efficiency_pct(p_mech_kw: float, p_elec_kw: float) -> float:
        if p_elec_kw <= 0.001 or p_mech_kw <= 0:
            return 0.0
        eff = (p_mech_kw / p_elec_kw) * 100.0
        return min(100.0, max(0.0, eff))

    @staticmethod
    def calc_torque_constant_kt(torque_nm: float, sigma_current_a: float) -> float:
        if sigma_current_a <= 0.01 or torque_nm <= 0:
            return 0.0
        return torque_nm / sigma_current_a

    @staticmethod
    def kty84_voltage_to_temperature(adc_volt: float, v_min: float = 0.0, v_max: float = 10.0, 
                                     t_min: float = 20.0, t_max: float = 150.0) -> float:
        v_clamped = max(v_min, min(v_max, adc_volt))
        scale = (t_max - t_min) / (v_max - v_min)
        return (v_clamped - v_min) * scale + t_min


class DutyCyclePlanner:
    """
    IEC 60034-1 標準馬達工作制測試規劃器 (S1, S2, S6)
    """

    @staticmethod
    def plan_s1_test(target_rpm: float, load_torque_nm: float, duration_min: float = 30.0, 
                     thermal_equilibrium_check: bool = True) -> Dict:
        """
        S1 連續運轉工作制 (Continuous Duty)
        長時間恆定負載運轉，直至達到熱平衡
        """
        return {
            "mode": "S1",
            "name": "S1 連續運轉工作制 (Continuous Duty)",
            "target_rpm": target_rpm,
            "load_torque_nm": load_torque_nm,
            "duration_sec": duration_min * 60.0,
            "thermal_check": thermal_equilibrium_check,
            "max_allowed_temp_degc": 130.0
        }

    @staticmethod
    def plan_s2_test(target_rpm: float, overload_torque_nm: float, duration_min: float = 10.0) -> Dict:
        """
        S2 短時運轉工作制 (Short-Time Duty)
        恆定負載（通常為過載如 120%~150%）運轉指定時間（如 10min, 30min, 60min），隨後停機冷卻
        """
        return {
            "mode": "S2",
            "name": "S2 短時運轉工作制 (Short-Time Duty)",
            "target_rpm": target_rpm,
            "load_torque_nm": overload_torque_nm,
            "duration_sec": duration_min * 60.0,
            "max_allowed_temp_degc": 140.0
        }

    @staticmethod
    def plan_s6_test(target_rpm: float, load_torque_nm: float, cycle_time_sec: float = 600.0, 
                     ed_pct: float = 40.0, cycles: int = 5) -> Dict:
        """
        S6 連續週期運轉工作制 (Continuous Periodic Duty with Intermittent Load)
        由週期性之「負載運轉期 (t_load)」與「空載運轉期 (t_idle)」組成，無停機休止
        ED% = (t_load / (t_load + t_idle)) * 100%
        """
        t_load = cycle_time_sec * (ed_pct / 100.0)
        t_idle = cycle_time_sec - t_load

        return {
            "mode": "S6",
            "name": f"S6 連續週期運轉 (S6-{ed_pct:.0f}%, {cycles} 循環)",
            "target_rpm": target_rpm,
            "load_torque_nm": load_torque_nm,
            "idle_torque_nm": 0.0,
            "cycle_time_sec": cycle_time_sec,
            "ed_pct": ed_pct,
            "t_load_sec": t_load,
            "t_idle_sec": t_idle,
            "total_cycles": cycles,
            "total_duration_sec": cycle_time_sec * cycles
        }
