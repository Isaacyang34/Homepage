using System;
using System.Drawing;
using System.Windows.Forms;

namespace DynamometerHMI
{
    /// <summary>
    /// 統一加載追隨狀態跟蹤器 (UnifiedLoadTracker)
    /// 封裝全系統兩大基石追隨引擎所需的狀態、試探斜率、給定參數與收斂狀態
    /// </summary>
    public class UnifiedLoadTracker
    {
        public int ProbeStep = 0;             // 0: 未啟動, 1..3: 0.1%階梯靈敏度試探, >=4: 動態大步長加速逼近
        public double ProbeBaseTorque = 0.0;  // 試探前底載 Nm
        public double ProbeTorque1 = 0.0;     // 0.1% 實測 Nm
        public double ProbeTorque2 = 0.0;     // 0.2% 實測 Nm
        public double ProbeTorque3 = 0.0;     // 0.3% 實測 Nm
        public double NmPerPointOnePct = 0.5; // 負載響應斜率 (Nm / 0.1%)
        public double AdaptedTorquePct = 0.0; // 當前 CS18 給定負載百分比 (%)
        public double CurrentSpeedCmd = 0.0;  // 當前 SY52 給定轉速命令 (rpm)
        public int SustainedStableSec = 0;    // 連續雙達標維持秒數
        public bool IsConverged = false;      // 是否已雙達標收斂
        public int ElapsedSec = 0;            // 調節已耗時秒數

        public void Reset(double initSpeed, double initTorquePct = 0.0)
        {
            ProbeStep = 0;
            ProbeBaseTorque = 0.0;
            ProbeTorque1 = 0.0;
            ProbeTorque2 = 0.0;
            ProbeTorque3 = 0.0;
            NmPerPointOnePct = 0.5;
            AdaptedTorquePct = Math.Max(0.0, initTorquePct);
            CurrentSpeedCmd = Math.Max(0.0, initSpeed);
            SustainedStableSec = 0;
            IsConverged = false;
            ElapsedSec = 0;
        }
    }

    public partial class MainForm : Form
    {
        // 全域統一追隨跟蹤器實例 (各模式互相獨立不干擾，但算法 100% 統一)
        public UnifiedLoadTracker s1LoadTracker = new UnifiedLoadTracker();
        public UnifiedLoadTracker s2LoadTracker = new UnifiedLoadTracker();
        public UnifiedLoadTracker s6LoadTracker = new UnifiedLoadTracker();
        public UnifiedLoadTracker tnLoadTracker = new UnifiedLoadTracker();
        public UnifiedLoadTracker effMapLoadTracker = new UnifiedLoadTracker();

        /// <summary>
        /// 【全系統統一核心引擎 1：SY52 + CS18 雙閉環自適應定錨加速追隨】
        /// 核心流程：
        /// 1. 待測端空載提速至 65% 以上，加載端同步熱備妥激磁 (Sy50=4)。
        /// 2. 步驟 A：前 3 秒以 0.1% 階梯試探計算該馬達當前轉速下的真實負載斜率 (Nm / 0.1%)。
        /// 3. 步驟 B：依目標力矩差距與斜率，動態預估並開出大步長 (最高 5.0%) 極速衝刺逼近。
        /// 4. 步驟 C：待測端同步監控轉速掉速幅度，動態補償 SY52 補轉差。
        /// 5. 步驟 D：轉矩與轉速雙達標持續 >= 2 秒宣告收斂，步進縮小至 0.08%~0.15% 穩態微調。
        /// 通用於：S1 連續、S2 錨點校驗、S6 加載定錨、T-N 曲線、效率地圖
        /// </summary>
        public bool ExecuteUnifiedDualTrackingStep(
            UnifiedLoadTracker tracker,
            int spdDrive, int trqDrive,
            double targetSpd, double targetTrq,
            double actSpd, double actTrq,
            out string statusDesc,
            string tag = "")
        {
            tracker.ElapsedSec++;

            int spdCom = GetHmiKebComIdx(spdDrive);
            int spdBaud = GetHmiKebBaudIdx(spdDrive);
            int spdNode = (spdDrive == 1) ? (int)numHmiKebNode1.Value : (int)numHmiKebNode2.Value;

            int trqCom = GetHmiKebComIdx(trqDrive);
            int trqBaud = GetHmiKebBaudIdx(trqDrive);
            int trqNode = (trqDrive == 1) ? (int)numHmiKebNode1.Value : (int)numHmiKebNode2.Value;

            string spdDriveName = (spdDrive == 1) ? "A載台" : "B載台";
            string trqDriveName = (trqDrive == 1) ? "A載台" : "B載台";

            if (tracker.CurrentSpeedCmd <= 0) tracker.CurrentSpeedCmd = targetSpd;

            // 1. 加載端激磁熱備妥檢查 (待測端轉速達到 65% 時啟動激磁 Sy50=4，但此時 CS18 仍為 0)
            int curTrqSy50 = (trqDrive == 1) ? lastSy50Cmd1 : lastSy50Cmd2;
            if (actSpd >= targetSpd * 0.65 && curTrqSy50 != 4)
            {
                SetHmiKebCommand(trqCom, trqBaud, trqNode, 4, string.Format("{0}加載端達速激磁熱備妥 (Sy50=4)", trqDriveName));
                if (trqDrive == 1) lastSy50Cmd1 = 4; else lastSy50Cmd2 = 4;
            }

            // 2. 轉速尚未達 65%：保持空載極速提速，加載端 cs.18 強制保持 0
            if (actSpd < targetSpd * 0.65)
            {
                KebWriteParamWithDll(trqCom, trqBaud, trqNode, 0x0F12, 0);
                statusDesc = string.Format("【空載極速提速】實測 {0:F0}/{1:F0} rpm (加載端保持 0%)", actSpd, targetSpd);
                return false;
            }

            // 3. 核心加載自適應試探與動態大步進加速
            double trqErr = targetTrq - actTrq;

            // ── 步驟 A：前 3 秒以 0.1% 階梯試探計算負載靈敏度斜率 (Nm / 0.1%) ──
            if (tracker.ProbeStep < 3)
            {
                tracker.ProbeStep++;
                tracker.AdaptedTorquePct = tracker.ProbeStep * 0.1; // 0.1%, 0.2%, 0.3%
                KebWriteParamWithDll(trqCom, trqBaud, trqNode, 0x0F12, (int)Math.Round(tracker.AdaptedTorquePct * 10.0));

                if (tracker.ProbeStep == 1) { tracker.ProbeTorque1 = actTrq; tracker.ProbeBaseTorque = actTrq; }
                else if (tracker.ProbeStep == 2) { tracker.ProbeTorque2 = actTrq; }
                else if (tracker.ProbeStep == 3)
                {
                    tracker.ProbeTorque3 = actTrq;
                    double deltaTrq = tracker.ProbeTorque3 - tracker.ProbeBaseTorque;
                    tracker.NmPerPointOnePct = (deltaTrq > 0.05) ? (deltaTrq / 3.0) : 0.5;
                    WriteHmiLog("UNIFIED_TRACK", string.Format("【{0} 靈敏度試探完成】基底={1:F2}Nm, 0.3%時={2:F2}Nm, 估算每 0.1% 增加 {3:F3} Nm (負載斜率)",
                        tag, tracker.ProbeBaseTorque, tracker.ProbeTorque3, tracker.NmPerPointOnePct));
                }

                statusDesc = string.Format("【靈敏度探測】0.{0}%: 實測 {1:F1} Nm (負載斜率學習中)", tracker.ProbeStep, actTrq);
            }
            else
            {
                // ── 步驟 B：根據目標力矩差距與斜率，動態預估並開出大步長 (最高 5.0%) ──
                double safeSlope = Math.Max(0.05, tracker.NmPerPointOnePct);
                double estRemainingPct = (trqErr / safeSlope) * 0.1;

                if (trqErr > 0.4) // 尚未達標，需要加載
                {
                    double stepPct = 0.08;
                    if (trqErr > 50.0 || estRemainingPct > 15.0) stepPct = 5.0;      // 超遠距離狂衝 5.0%
                    else if (trqErr > 25.0 || estRemainingPct > 8.0) stepPct = 2.5;  // 遠距離衝刺 2.5%
                    else if (trqErr > 10.0 || estRemainingPct > 3.0) stepPct = 1.0;  // 中距離過渡 1.0%
                    else if (trqErr > 4.0 || estRemainingPct > 1.0) stepPct = 0.4;   // 近距離緩衝 0.4%
                    else if (trqErr > 1.5) stepPct = 0.15;                           // 微調 0.15%
                    else stepPct = 0.08;                                             // 精修 0.08%

                    tracker.AdaptedTorquePct = Math.Min(100.0, tracker.AdaptedTorquePct + stepPct);
                    KebWriteParamWithDll(trqCom, trqBaud, trqNode, 0x0F12, (int)Math.Round(tracker.AdaptedTorquePct * 10.0));
                }
                else if (trqErr < -0.6) // 超出目標，平穩階梯卸載
                {
                    double stepDec = 0.08;
                    if (trqErr < -20.0) stepDec = 2.5;
                    else if (trqErr < -8.0) stepDec = 1.0;
                    else if (trqErr < -3.0) stepDec = 0.4;
                    else if (trqErr < -1.2) stepDec = 0.15;
                    else stepDec = 0.08;

                    tracker.AdaptedTorquePct = Math.Max(0.0, tracker.AdaptedTorquePct - stepDec);
                    KebWriteParamWithDll(trqCom, trqBaud, trqNode, 0x0F12, (int)Math.Round(tracker.AdaptedTorquePct * 10.0));
                }

                statusDesc = string.Format("【自適應加載】給定 {0:F1}% (實測 {1:F1}/{2:F1} Nm | 斜率 {3:F2}Nm/0.1%)",
                    tracker.AdaptedTorquePct, actTrq, targetTrq, tracker.NmPerPointOnePct);
            }

            // ── 步驟 C：待測端 SY52 同步補轉差 ──
            double spdSlip = targetSpd - actSpd;
            if (spdSlip > 2.0)
            {
                tracker.CurrentSpeedCmd += (spdSlip > 10.0 ? 3.0 : 1.0);
                tracker.CurrentSpeedCmd = Math.Min(targetSpd * 1.35, tracker.CurrentSpeedCmd);
                KebWriteParam32(spdCom, spdBaud, spdNode, 0x0034, (int)Math.Round(tracker.CurrentSpeedCmd), tag + " 補轉差 (SY52)");
            }
            else if (spdSlip < -3.0)
            {
                tracker.CurrentSpeedCmd -= 1.0;
                KebWriteParam32(spdCom, spdBaud, spdNode, 0x0034, (int)Math.Round(tracker.CurrentSpeedCmd), tag + " 微調速度 (SY52)");
            }

            // 同步主畫面數字輸入框顯示
            if (spdDrive == 2 && numHmiKebSpeed2 != null) numHmiKebSpeed2.Value = (decimal)Math.Max(0, Math.Min(6000, tracker.CurrentSpeedCmd));
            else if (spdDrive == 1 && numHmiKebSpeed1 != null) numHmiKebSpeed1.Value = (decimal)Math.Max(0, Math.Min(6000, tracker.CurrentSpeedCmd));
            if (trqDrive == 1 && numHmiKebTorque1 != null) numHmiKebTorque1.Value = (decimal)tracker.AdaptedTorquePct;
            else if (trqDrive == 2 && numHmiKebTorque2 != null) numHmiKebTorque2.Value = (decimal)tracker.AdaptedTorquePct;

            // ── 步驟 D：雙達標判定 ──
            bool isTrqReached = Math.Abs(trqErr) <= Math.Max(1.0, targetTrq * 0.08);
            bool isSpdReached = Math.Abs(spdSlip) <= Math.Max(6.0, targetSpd * 0.015);

            if (isTrqReached && isSpdReached)
            {
                tracker.SustainedStableSec++;
                if (tracker.SustainedStableSec >= 2)
                {
                    tracker.IsConverged = true;
                }
            }
            else
            {
                tracker.SustainedStableSec = 0;
                tracker.IsConverged = false;
            }

            return tracker.IsConverged;
        }

        /// <summary>
        /// 【全系統統一核心引擎 2：純 SY52 速度自適應追隨】
        /// 核心流程：加載端 cs.18 強制歸零，待測端單獨微調 SY52 維持目標轉速
        /// 通用於：空載測試、S6 空載定錨、S6 空載冷卻期、T-N 空載提速期
        /// </summary>
        public bool ExecuteUnifiedSpeedTrackingStep(
            UnifiedLoadTracker tracker,
            int spdDrive, int trqDrive,
            double targetSpd,
            double actSpd,
            out string statusDesc,
            string tag = "")
        {
            tracker.ElapsedSec++;

            int spdCom = GetHmiKebComIdx(spdDrive);
            int spdBaud = GetHmiKebBaudIdx(spdDrive);
            int spdNode = (spdDrive == 1) ? (int)numHmiKebNode1.Value : (int)numHmiKebNode2.Value;

            int trqCom = GetHmiKebComIdx(trqDrive);
            int trqBaud = GetHmiKebBaudIdx(trqDrive);
            int trqNode = (trqDrive == 1) ? (int)numHmiKebNode1.Value : (int)numHmiKebNode2.Value;

            // 加載端強制歸零
            tracker.AdaptedTorquePct = 0.0;
            KebWriteParamWithDll(trqCom, trqBaud, trqNode, 0x0F12, 0);

            if (tracker.CurrentSpeedCmd <= 0) tracker.CurrentSpeedCmd = targetSpd;

            double spdDiff = targetSpd - actSpd;
            if (Math.Abs(spdDiff) > 3.0)
            {
                tracker.CurrentSpeedCmd += (spdDiff > 0 ? (spdDiff > 20.0 ? 5.0 : 2.0) : -2.0);
                tracker.CurrentSpeedCmd = Math.Max(0.0, Math.Min(6000.0, tracker.CurrentSpeedCmd));
                KebWriteParam32(spdCom, spdBaud, spdNode, 0x0034, (int)Math.Round(tracker.CurrentSpeedCmd), tag + " 速度微調 (SY52)");
            }

            if (spdDrive == 2 && numHmiKebSpeed2 != null) numHmiKebSpeed2.Value = (decimal)Math.Max(0, Math.Min(6000, tracker.CurrentSpeedCmd));
            else if (spdDrive == 1 && numHmiKebSpeed1 != null) numHmiKebSpeed1.Value = (decimal)Math.Max(0, Math.Min(6000, tracker.CurrentSpeedCmd));
            if (trqDrive == 1 && numHmiKebTorque1 != null) numHmiKebTorque1.Value = 0;
            else if (trqDrive == 2 && numHmiKebTorque2 != null) numHmiKebTorque2.Value = 0;

            bool isSpdReached = Math.Abs(spdDiff) <= Math.Max(6.0, targetSpd * 0.015);
            if (isSpdReached)
            {
                tracker.SustainedStableSec++;
                if (tracker.SustainedStableSec >= 2)
                {
                    tracker.IsConverged = true;
                }
            }
            else
            {
                tracker.SustainedStableSec = 0;
                tracker.IsConverged = false;
            }

            statusDesc = string.Format("【空載定速追隨】實測 {0:F0}/{1:F0} rpm (命令 {2:F0})", actSpd, targetSpd, tracker.CurrentSpeedCmd);
            return tracker.IsConverged;
        }
    }
}
