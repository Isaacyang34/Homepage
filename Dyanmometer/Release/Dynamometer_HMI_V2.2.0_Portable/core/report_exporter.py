"""
===============================================================================
Dynamometer Report Exporter (測試報表匯出模組)
產出與 20170516 原版格式 100% 相容之 CSV 測試報表
===============================================================================
"""

import os
from datetime import datetime
from typing import List
from .engine_calc import MotorTestDataRecord


class ReportExporter:
    @staticmethod
    def export_csv_report(
        file_path: str,
        motor_model: str,
        rated_current_a: float,
        rated_speed_rpm: float,
        rated_torque_nm: float,
        max_current_a: float,
        max_speed_rpm: float,
        max_torque_nm: float,
        records: List[MotorTestDataRecord],
        start_time: datetime = None
    ) -> bool:
        """
        匯出完整標準 CSV 測試報表
        """
        if start_time is None:
            start_time = datetime.now()

        os.makedirs(os.path.dirname(os.path.abspath(file_path)), exist_ok=True)

        header_lines = [
            f"開始時間：{start_time.strftime('%Y/%m/%d %p %I:%M:%S')}\n",
            "\n",
            f"馬達型號：{motor_model}\n",
            f"額定電流：{rated_current_a:.1f}A\n",
            f"額定轉速：{rated_speed_rpm:.0f}rpm\n",
            f"額定轉矩：{rated_torque_nm:.1f}Nm\n",
            f"最大電流：{max_current_a:.1f}A\n",
            f"最大轉速：{max_speed_rpm:.0f}rpm\n",
            f"最大轉矩：{max_torque_nm:.1f}Nm\n",
            "\n",
            "時間,轉速(rpm),轉矩(Nm),功率(KW),效率(%),加載轉矩(%),加載電流(A),扭力常數(Nm/A),"
            "U相電壓(V),V相電壓(V),W相電壓(V),U相電流(A),V相電流(A),W相電流(A),"
            "Sigma電壓(V),Sigma電流(A),Sigma功率(KW),Sigma功因,"
            "馬達溫度1(degC),馬達溫度2(degC),馬達溫度3(degC),馬達溫度4(degC),馬達溫度5(degC)\n"
        ]

        with open(file_path, "w", encoding="utf-8-sig") as f:
            f.writelines(header_lines)
            for rec in records:
                f.write(rec.to_csv_row() + "\n")

        return True
