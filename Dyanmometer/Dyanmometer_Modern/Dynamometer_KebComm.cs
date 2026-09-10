using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.IO.Ports;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace DynamometerHMI
{
    // KEB 連線硬體參數備份快照資料結構 (供斷線或關閉程式前回寫還原)
    public class KebBackupParams
    {
        public bool HasBackup;
        public int? Ru00;
        public int? Ud02;
        public int? Op00;
        public int? Op01;
        public int? Cs00;
        public int? Cs15;
        public int? Cs18;
        public int? Cs19;
        public int? Op03;
        public int? Sy50;
        public int? Sy52;
        public DateTime BackupTime;

        public KebBackupParams()
        {
            HasBackup = false;
            BackupTime = DateTime.MinValue;
        }
    }

    public partial class MainForm : Form
    {
        // KEB 官方原廠 protKEB.dll P/Invoke 接口與資料結構 (完全 100% 鏡像對齊 Module_KEB_1.bas 記憶體佈局)
        [StructLayout(LayoutKind.Sequential, Pack = 1, CharSet = CharSet.Ansi)]
        public struct tProtProperty
        {
            public int ProtType;      // 0..3 (4B): 1=prAnsi, 2=prHsp5, 4=prIP
            public int TimeOut;       // 4..7 (4B): ms (預設 500)
            public int Baudrate;      // 8..11 (4B): 0=1200, 1=2400, 2=4800, 3=9600, 4=19200, 5=38400, 6=57600, 7=115200
            public int Comport;       // 12..15 (4B): 0=COM1, 1=COM2, 2=COM3, 3=COM4
            public int Flag;          // 16..19 (4B): 01 = Sendslow
            public int Port;          // 20..23 (4B): IP Port
            public byte Txtlen;       // 24 (1B)
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 100)]
            public string txt;        // 25..124 (100B)
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public struct tRecTel
        {
            public int InvId;
            public int Inverter;
            public int Service;
            public int Ack;
            public byte Read;
            public byte Req;
            public short Fill;
            public int SR;  // VB6 Long = 4-byte pointer value (NOT IntPtr, which is 8B in x64 host)
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public struct tServ00Rec
        {
            public short Adr;
            public byte Paraset;
            public byte Fill;
            public int Data;
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public struct tServM1Rec
        {
            public short Adr;
            public short Data;
        }

        [DllImport("protKEB.dll", CallingConvention = CallingConvention.StdCall)]
        public static extern void closechannels();

        [DllImport("protKEB.dll", CallingConvention = CallingConvention.StdCall)]
        public static extern int setprotproperties(ref tProtProperty prop);

        [DllImport("protKEB.dll", CallingConvention = CallingConvention.StdCall)]
        public static extern int setinvprot(int inv, int prot);

        [DllImport("protKEB.dll", CallingConvention = CallingConvention.StdCall)]
        public static extern void setretrycnt(int retries);

        [DllImport("protKEB.dll", CallingConvention = CallingConvention.StdCall, EntryPoint = "waitrdreq")]
        public static extern int waitrdreq(int inv, int service, byte[] servRec, byte[] recTel);

        [DllImport("protKEB.dll", CallingConvention = CallingConvention.StdCall, EntryPoint = "waitwrreq")]
        public static extern int waitwrreq(int inv, int service, byte[] servRec, byte[] recTel);

        // =========================================================================
        //  硬體 ST 安全端子與 ru.00 狀態解碼與即時變更追蹤
        // =========================================================================
        public string DecodeKebRu00(int val)
        {
            switch (val)
            {
                case 0: return "0: nOP (No Operation / ST未導通斷開)";
                case 1: return "1: bOP (Before Operation / 準備中)";
                case 2: return "2: bon (Brake On / 煞車制動閉合)";
                case 3: return "3: bbL (Base Block / 基極阻斷)";
                case 4: return "4: FStP (Fast Stop / 快速停機)";
                case 5: return "5: dEL (Delay / 延時中)";
                case 8: return "8: HCL (Hardware Current Limit / 硬體電流限制)";
                case 9: return "9: SCL (Software Current Limit / 軟體電流限制)";
                case 10: return "10: C.o.L. (Constant Current Limit / 定速電流限制)";
                case 11: return "11: d.o.L. (Decel Current Limit / 減速電流限制)";
                case 12: return "12: A.o.L. (Accel Current Limit / 加速電流限制)";
                case 13: return "13: PA.o.L. (Power Accel Limit / 功率加速限制)";
                case 64: return "64: FAcc (Forward Acceleration / 正轉加速中)";
                case 65: return "65: FdEc (Forward Deceleration / 正轉減速中)";
                case 66: return "66: Fcon (Forward Constant Speed / 正轉定速運轉中)";
                case 67: return "67: rAcc (Reverse Acceleration / 反轉加速中)";
                case 68: return "68: rdEc (Reverse Deceleration / 反轉減速中)";
                case 69: return "69: rcon (Reverse Constant Speed / 反轉定速運轉中)";
                case 70: return "70: LS (Low Speed / 待命準備中，調變關閉)";
                case 71: return "71: POS (Positioning / 定位運轉中)";
                case 72: return "72: Att (Attention / 待命注意)";
                case 73: return "73: dF (Direct Frequency / 直接頻率給定)";
                case 74: return "74: Hld (Hold / 保持運轉中)";
                case 75: return "75: SLS (Safely Limited Speed / 安全限速)";
                case 79: return "79: STO (Safe Torque Off / 安全轉矩切斷)";
                default:
                    return string.Format("{0}: 變頻器狀態機 (0x{0:X2})", val);
            }
        }

        public string DecodeKebFaultCode(int code)
        {
            if (code == 0) return "0: 正常 (無異常)";
            switch (code)
            {
                case 1: return "E.UP (0x01): Under Voltage / 直流母線欠壓";
                case 2: return "E.OU (0x02): Over Voltage / 直流母線過壓";
                case 3: return "E.UPh (0x03): Under Voltage Phase / 輸入欠相";
                case 4: return "E.OC (0x04): Over Current / 即時過電流跳脫";
                case 5: return "E.dOH (0x05): Drive Overheat / 變頻器內部過熱";
                case 6: return "E.OH (0x06): Heatsink Overheat / 散熱模組過熱";
                case 7: return "E.OL (0x07): Motor Overload / 馬達過載保護 (I²t)";
                case 8: return "E.OL2 (0x08): Inverter Overload / 變頻器過載保護";
                case 9: return "E.EF (0x09): External Fault / 外部故障端子跳脫";
                case 10: return "E.Pu (0x0A): Power Unit / 功率級單元故障";
                case 11: return "E.dri (0x0B): Driver Stage / 閘極驅動級異常";
                case 12: return "E.EEP (0x0C): EEPROM Error / 內部記憶體讀寫錯誤";
                case 13: return "E.PRG (0x0D): Programming Error / 參數設定衝突";
                case 14: return "E.buS (0x0E): Fieldbus Error / 通訊匯流排超時";
                case 15: return "E.InI (0x0F): Init Error / 變頻器初始化錯誤";
                case 16: return "E.br (0x10): Braking Resistor / 煞車電阻/制動單元異常";
                case 17: return "E.ndL (0x11): No Speed / 速度回授遺失";
                case 18: return "E.EnC (0x12): Encoder Error / 編碼器信號異常";
                case 19: return "E.Hyb (0x13): Hybrid Error";
                case 20: return "E.LS (0x14): Limit Switch / 極限開關動作";
                case 25: return "E.OH2 (0x19): Motor PTC / 馬達熱敏電阻過溫";
                case 32: return "E.OL (0x20): Overload (Bit32)";
                case 64: return "E.OL2 (0x40): Inverter Overload (Bit64)";
                case 128: return "E.EF (0x80): External Fault (Bit128)";
                default:
                    return string.Format("E.0x{0:X2} (變頻器故障碼 {0})", code);
            }
        }

        private string GetKebModeName(int mode)
        {
            switch (mode)
            {
                case 7: return "Mode 7: 數位定轉速 (半自動, oP01=7)";
                case 8: return "Mode 8: 數位定轉矩 (半自動, oP01=7)";
                case 9: return "Mode 9: 數位定轉速 (全自動, oP01=8)";
                case 10: return "Mode 10: 數位定轉矩 (全自動, oP01=8)";
                default: return string.Format("Mode {0}", mode);
            }
        }

        private string GetKebModeShortName(int mode)
        {
            switch (mode)
            {
                case 7: return "Mode7(半自轉速)";
                case 8: return "Mode8(半自轉矩)";
                case 9: return "Mode9(全自轉速)";
                case 10: return "Mode10(全自轉矩)";
                default: return string.Format("M{0}", mode);
            }
        }

        private void CheckHardwareStStatus(int driveId, int ru00Val)
        {
            string driveName = (driveId == 1) ? "A載台" : "B載台";
            int prevRu00 = (driveId == 1) ? lastRu00_1 : lastRu00_2;

            // 狀態變更即時追蹤 (例如: 實體 ST 接通，狀態由 nOP -> LS)
            if (ru00Val != prevRu00)
            {
                if (driveId == 1) lastRu00_1 = ru00Val; else lastRu00_2 = ru00Val;
                if (prevRu00 != -1) // 非開機首次採樣
                {
                    WriteHmiLog("KEB_STATE", string.Format("【{0} 狀態變更】ru.00 狀態碼更新為: 【{1}】", driveName, DecodeKebRu00(ru00Val)));
                }
            }

            // ru.00 == 0 代表變頻器處於 0: no operation (nOP / Control Release 斷開)
            // ★【核心韌體解鎖】：KEB F5 僅在 nOP 狀態下允許寫入 cs.00！若檢測到 cs.00 殘留為 4，立即趁 nOP 自動寫入 cs.00 = 0！
            if (ru00Val == 0)
            {
                int cachedCs = (driveId == 1) ? cachedCs00_1 : cachedCs00_2;
                if (cachedCs == 4)
                {
                    int com = GetHmiKebComIdx(driveId);
                    int baud = GetHmiKebBaudIdx(driveId);
                    int node = (int)((driveId == 1) ? numHmiKebNode1.Value : numHmiKebNode2.Value);
                    bool ok = KebWriteParamWithDll(com, baud, node, 0x0F00, 0); // cs.00 = 0 (V/F)
                    if (ok)
                    {
                        if (driveId == 1) cachedCs00_1 = 0; else cachedCs00_2 = 0;
                        WriteHmiLog("KEB_STATE", string.Format("【V/F 自動復歸成功】檢測到 {0} 處於 nOP，已成功將 cs.00 寫回 0 (V/F 開迴路模式)！", driveName));
                    }
                }
            }

            bool wasRunning = (driveId == 1) ? (lastSy50Cmd1 == 4 || lastSy50Cmd1 == 12) : (lastSy50Cmd2 == 4 || lastSy50Cmd2 == 12);
            bool isAnyLocked = isClosedLoopTracking || isSpeedTracking || isLocked || hasBaseline || hasSpeedBaseline;
            if (ru00Val == 0 && (wasRunning || isAnyLocked))
            {
                if (isAnyLocked)
                {
                    UnlockAllTrackingAndReset();
                    WriteHmiLog("SAFETY_TRIP", string.Format("【🚨 硬體ST切斷警示】檢測到 {0} 硬體 ST 端子已斷開 (ru.00=0 / nOP)，已自動解除 LOCK 鎖定並安全復歸！", driveName));
                }
                else
                {
                    WriteHmiLog("SAFETY", string.Format("【⚠️ 硬體ST端子斷開】檢測到 {0} 處於 nOP (Control Release 斷開)。", driveName));
                }
            }


        }

        private int GetHmiKebComIdx(int drive)
        {
            ComboBox cmb = (drive == 1) ? cmbHmiKebPort1 : cmbHmiKebPort2;
            string port = (cmb != null && cmb.SelectedItem != null) ? cmb.SelectedItem.ToString() : (drive == 1 ? "COM1" : "COM2");
            int pNum = drive;
            if (port.StartsWith("COM", StringComparison.OrdinalIgnoreCase)) int.TryParse(port.Substring(3), out pNum);
            return Math.Max(0, pNum - 1);
        }

        private int GetHmiKebBaudIdx(int drive)
        {
            ComboBox cmb = (drive == 1) ? cmbHmiKebBaud1 : cmbHmiKebBaud2;
            int baud = 9600;
            if (cmb != null && cmb.SelectedItem != null) int.TryParse(cmb.SelectedItem.ToString(), out baud);
            if (baud == 9600) return 3;
            if (baud == 19200) return 4;
            if (baud == 38400) return 5;
            if (baud == 57600) return 6;
            if (baud == 115200) return 7;
            return 3; // 預設 9600
        }


        private void RefreshHmiKebPorts()
        {
            cmbHmiKebPort1.Items.Clear();
            cmbHmiKebPort2.Items.Clear();
            string[] ports = SerialPort.GetPortNames();

            int selIdx1 = -1, selIdx2 = -1;
            for (int i = 0; i < ports.Length; i++)
            {
                cmbHmiKebPort1.Items.Add(ports[i]);
                cmbHmiKebPort2.Items.Add(ports[i]);
                if (ports[i].Equals("COM1", StringComparison.OrdinalIgnoreCase)) selIdx1 = i;
                if (ports[i].Equals("COM2", StringComparison.OrdinalIgnoreCase)) selIdx2 = i;
            }

            if (selIdx1 >= 0) cmbHmiKebPort1.SelectedIndex = selIdx1;
            else if (cmbHmiKebPort1.Items.Count > 0) cmbHmiKebPort1.SelectedIndex = 0;
            else cmbHmiKebPort1.Items.Add("COM1");

            if (selIdx2 >= 0) cmbHmiKebPort2.SelectedIndex = selIdx2;
            else if (cmbHmiKebPort2.Items.Count > 1) cmbHmiKebPort2.SelectedIndex = 1;
            else if (cmbHmiKebPort2.Items.Count > 0) cmbHmiKebPort2.SelectedIndex = 0;
            else cmbHmiKebPort2.Items.Add("COM2");
        }

        public void UnlockAllTrackingAndReset()
        {
            hasBaseline = false;
            isLocked = false;
            isClosedLoopTracking = false;
            baselineTorque = 0.0;
            activeTrackingTorquePct = 0.0m;

            hasSpeedBaseline = false;
            isSpeedTracking = false;
            baselineSpeed = 0.0;
            activeTrackingSpeedRpm = 0.0m;

            if (this.IsHandleCreated && !this.IsDisposed)
            {
                this.BeginInvoke((MethodInvoker)delegate {
                    if (btnLockTorque != null && !btnLockTorque.IsDisposed)
                    {
                        btnLockTorque.Image = CreateLockIconImage(14, 14, Color.FromArgb(100, 116, 139), false);
                        btnLockTorque.Text = " UNLOCK";
                        btnLockTorque.ForeColor = Color.FromArgb(100, 116, 139);
                        btnLockTorque.BackColor = Color.FromArgb(241, 245, 249);
                        btnLockTorque.FlatAppearance.BorderColor = Color.FromArgb(203, 213, 225);
                    }
                    if (btnLockSpeed != null && !btnLockSpeed.IsDisposed)
                    {
                        btnLockSpeed.Image = CreateLockIconImage(14, 14, Color.FromArgb(100, 116, 139), false);
                        btnLockSpeed.Text = " UNLOCK";
                        btnLockSpeed.ForeColor = Color.FromArgb(100, 116, 139);
                        btnLockSpeed.BackColor = Color.FromArgb(241, 245, 249);
                        btnLockSpeed.FlatAppearance.BorderColor = Color.FromArgb(203, 213, 225);
                    }
                    if (numCardDeadband != null && numCardDeadband.Parent != null) numCardDeadband.Parent.Visible = false;
                    if (numCardTargetTorque != null && numCardTargetTorque.Parent != null) numCardTargetTorque.Parent.Visible = true;
                    if (numCardSpeedDeadband != null && numCardSpeedDeadband.Parent != null) numCardSpeedDeadband.Parent.Visible = false;
                    if (numCardTargetSpeed != null && numCardTargetSpeed.Parent != null) numCardTargetSpeed.Parent.Visible = true;

                    UpdateHmiKebModeButtonsVisual();
                });
            }
        }

        private bool CheckConfirmDisconnectWithLock(string driveName)
        {
            if (isClosedLoopTracking || isSpeedTracking)
            {
                string lockDesc = "";
                if (isClosedLoopTracking && isSpeedTracking) lockDesc = "【轉矩 LOCK】與【轉速 LOCK】";
                else if (isClosedLoopTracking) lockDesc = "【轉矩 LOCK】平滑追隨";
                else lockDesc = "【轉速 LOCK】平滑追隨";

                DialogResult dr = MessageBox.Show(
                    string.Format(
                        "【⚠️ 斷線優先安全警告】\n\n" +
                        "系統檢測到目前正處於 {0} 狀態！\n\n" +
                        "斷開 {1} 通訊前，必須先安全解除 LOCK 追隨控制，以防止驅動器通訊中斷引發非預期運轉。\n\n" +
                        "請問是否確定要【解除 LOCK 並立即斷開連線】？",
                        lockDesc, driveName),
                    "斷線優先安全防護確認",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning,
                    MessageBoxDefaultButton.Button2
                );

                if (dr != DialogResult.Yes)
                {
                    return false;
                }

                // 使用者確認解除 LOCK 並斷線：
                UnlockAllTrackingAndReset();
                WriteHmiLog("SAFETY", string.Format("【斷線優先】已確認解除 {0} 控制，即將斷開 {1} 連線。", lockDesc, driveName));
            }

            return CheckConfirmDisconnectFullAuto(driveName);
        }

        private bool CheckConfirmDisconnectFullAuto(string driveName)
        {
            bool isFullAuto = (currentKebMode1 == 9 || currentKebMode1 == 10 || currentKebMode2 == 9 || currentKebMode2 == 10);
            if (isFullAuto)
            {
                DialogResult dr = MessageBox.Show(
                    string.Format("【⚠️ 全自動模式斷開 COM PORT 警告】\n\n系統目前處於【全自動控制】模式！\n若在此狀態下斷開 {0} 的 COM PORT 連線，將觸發連鎖安全停機保護，【強制雙載台全部停機並斷電】！\n\n請問是否確定要斷開 COM PORT 連線？", driveName),
                    "全自動安全防護確認",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning,
                    MessageBoxDefaultButton.Button2
                );
                if (dr != DialogResult.Yes)
                {
                    return false;
                }

                // 使用者確認斷開：強制執行雙機停機與轉矩卸載
                TriggerGlobalEmergencyStop();
                WriteHmiLog("SAFETY", string.Format("【全自動連鎖停機】使用者確認斷開 {0} 連線，雙載台已強制安全停機並斷電！", driveName));
            }
            return true;
        }

        private void ApplyHmiKebInterlock(int driveIdx, int mode)
        {
            string dName = (driveIdx == 1) ? "A載台(COM1)" : "B載台(COM2)";
            string modeDesc = GetKebModeName(mode);
            WriteHmiLog("USER_ACTION", string.Format("【使用者點擊模式按鈕】載台: {0} ➔ 要求切換為: 【{1}】", dName, modeDesc));

            // 邏輯 1：全自動控制模式 (模式 9: 全自動轉速, 模式 10: 全自動轉矩) 必須雙機 100% 同時連線！
            if (mode == 9 || mode == 10)
            {
                if (!isHmiKebOpen1 || !isHmiKebOpen2)
                {
                    MessageBox.Show(
                        "【🚨 全自動控制安全拒絕】\n\n" +
                        "全自動控制（oP.01=8）由上位機軟體全權掌控 ST 運轉與停機！\n" +
                        "為防止單機全自動運轉導致失控超速（飛車），系統強制要求【雙載台 (COM1 與 COM2) 必須 100% 同時連線】！\n\n" +
                        "目前檢測到尚有一側未連線，已強制阻斷切換！請先將雙機全部連線後再試。",
                        "全自動安全互鎖警告",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning
                    );
                    WriteHmiLog("SAFETY_LOCK", string.Format("【全自動安全拒絕】{0}載台嘗試在單機狀態切換全自動模式，已被系統強制阻斷！", driveIdx == 1 ? "A" : "B"));
                    UpdateHmiKebModeButtonsVisual();
                    return;
                }
            }

            // 單機連線防呆提醒：若另一載台未連線，提醒操作員確認負載安全
            if (mode == 8 || mode == 10)
            {
                bool otherOnline = (driveIdx == 1) ? isHmiKebOpen2 : isHmiKebOpen1;
                if (!otherOnline)
                {
                    DialogResult dr = MessageBox.Show(
                        "【⚠️ 單機調試提醒】\n\n對測端載台尚未連線！\n在單機狀態下輸出轉矩，請確認外部負載已安全就緒，以防無對抗下超速。\n\n是否確認進入定轉矩模式進行單機調試？",
                        "動力計單機調試提示",
                        MessageBoxButtons.YesNo,
                        MessageBoxIcon.Question
                    );
                    if (dr != DialogResult.Yes)
                    {
                        UpdateHmiKebModeButtonsVisual();
                        return;
                    }
                }
            }

            if (driveIdx == 1)
            {
                currentKebMode1 = mode;
                // 模式切換：若斷線則直接拒絕，不同步阻塞 UI
                if (!isHmiKebOpen1)
                {
                    MessageBox.Show("A台 (COM1) 未連線！請先點擊 [Open] 建立連線後再切換模式。", "A台未連線", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    currentKebMode1 = 0; UpdateHmiKebModeButtonsVisual(); return;
                }
                SetHmiKebMode(GetHmiKebComIdx(1), GetHmiKebBaudIdx(1), (int)numHmiKebNode1.Value, mode, "1號驅動端");

                // 半自動互鎖 (7/8)
                if (mode == 7 && currentKebMode2 == 7 && isHmiKebOpen2)
                {
                    currentKebMode2 = 8;
                    SetHmiKebMode(GetHmiKebComIdx(2), GetHmiKebBaudIdx(2), (int)numHmiKebNode2.Value, 8, "2號負載端 (雙機互鎖自動轉矩)");
                    WriteHmiLog("INTERLOCK", "【雙機安全互鎖】1號機切換為轉速控制，2號機已自動互鎖切換為轉矩加載控制！");
                }
                else if (mode == 8 && currentKebMode2 == 8 && isHmiKebOpen2)
                {
                    currentKebMode2 = 7;
                    SetHmiKebMode(GetHmiKebComIdx(2), GetHmiKebBaudIdx(2), (int)numHmiKebNode2.Value, 7, "2號負載端 (雙機互鎖自動轉速)");
                    WriteHmiLog("INTERLOCK", "【雙機安全互鎖】1號機切換為轉矩控制，2號機已自動互鎖切換為轉速驅動控制！");
                }
                // 全自動互鎖 (9/10)
                else if (mode == 9 && currentKebMode2 == 9 && isHmiKebOpen2)
                {
                    currentKebMode2 = 10;
                    SetHmiKebMode(GetHmiKebComIdx(2), GetHmiKebBaudIdx(2), (int)numHmiKebNode2.Value, 10, "2號負載端 (全自動互鎖轉矩)");
                    WriteHmiLog("INTERLOCK", "【全自動安全互鎖】1號機切換為全自動轉速，2號機已自動互鎖切換為全自動轉矩！");
                }
                else if (mode == 10 && currentKebMode2 == 10 && isHmiKebOpen2)
                {
                    currentKebMode2 = 9;
                    SetHmiKebMode(GetHmiKebComIdx(2), GetHmiKebBaudIdx(2), (int)numHmiKebNode2.Value, 9, "2號負載端 (全自動互鎖轉速)");
                    WriteHmiLog("INTERLOCK", "【全自動安全互鎖】1號機切換為全自動轉矩，2號機已自動互鎖切換為全自動轉速！");
                }
            }
            else
            {
                currentKebMode2 = mode;
                // 模式切換：若斷線則直接拒絕，不同步阻塞 UI
                if (!isHmiKebOpen2)
                {
                    MessageBox.Show("B台 (COM2) 未連線！請先點擊 [Open] 建立連線後再切換模式。", "B台未連線", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    currentKebMode2 = 0; UpdateHmiKebModeButtonsVisual(); return;
                }
                SetHmiKebMode(GetHmiKebComIdx(2), GetHmiKebBaudIdx(2), (int)numHmiKebNode2.Value, mode, "2號負載端");

                // 半自動互鎖 (7/8)
                if (mode == 7 && currentKebMode1 == 7 && isHmiKebOpen1)
                {
                    currentKebMode1 = 8;
                    SetHmiKebMode(GetHmiKebComIdx(1), GetHmiKebBaudIdx(1), (int)numHmiKebNode1.Value, 8, "1號驅動端 (雙機互鎖自動轉矩)");
                    WriteHmiLog("INTERLOCK", "【雙機安全互鎖】2號機切換為轉速控制，1號機已自動互鎖切換為轉矩加載控制！");
                }
                else if (mode == 8 && currentKebMode1 == 8 && isHmiKebOpen1)
                {
                    currentKebMode1 = 7;
                    SetHmiKebMode(GetHmiKebComIdx(1), GetHmiKebBaudIdx(1), (int)numHmiKebNode1.Value, 7, "1號驅動端 (雙機互鎖自動轉速)");
                    WriteHmiLog("INTERLOCK", "【雙機安全互鎖】2號機切換為轉矩控制，1號機已自動互鎖切換為轉速驅動控制！");
                }
                // 全自動互鎖 (9/10)
                else if (mode == 9 && currentKebMode1 == 9 && isHmiKebOpen1)
                {
                    currentKebMode1 = 10;
                    SetHmiKebMode(GetHmiKebComIdx(1), GetHmiKebBaudIdx(1), (int)numHmiKebNode1.Value, 10, "1號驅動端 (全自動互鎖轉矩)");
                    WriteHmiLog("INTERLOCK", "【全自動安全互鎖】2號機切換為全自動轉速，1號機已自動互鎖切換為全自動轉矩！");
                }
                else if (mode == 10 && currentKebMode1 == 10 && isHmiKebOpen1)
                {
                    currentKebMode1 = 9;
                    SetHmiKebMode(GetHmiKebComIdx(1), GetHmiKebBaudIdx(1), (int)numHmiKebNode1.Value, 9, "1號驅動端 (全自動互鎖轉速)");
                    WriteHmiLog("INTERLOCK", "【全自動安全互鎖】2號機切換為全自動轉矩，1號機已自動互鎖切換為全自動轉速！");
                }
            }
            UpdateHmiKebModeButtonsVisual();
        }

        private void UpdateHmiKebModeButtonsVisual()
        {
            // 更新 1 號機按鈕高亮
            SetHmiModeBtnStyle(bHmiM1_1, currentKebMode1 == 7, Color.FromArgb(16, 185, 129));
            SetHmiModeBtnStyle(bHmiM1_2, currentKebMode1 == 8, Color.FromArgb(0, 120, 215));
            SetHmiModeBtnStyle(bHmiM1_3, currentKebMode1 == 9, Color.FromArgb(217, 119, 6));
            SetHmiModeBtnStyle(bHmiM1_4, currentKebMode1 == 10, Color.FromArgb(139, 92, 246));

            // 更新 2 號機按鈕高亮
            SetHmiModeBtnStyle(bHmiM2_1, currentKebMode2 == 7, Color.FromArgb(16, 185, 129));
            SetHmiModeBtnStyle(bHmiM2_2, currentKebMode2 == 8, Color.FromArgb(0, 120, 215));
            SetHmiModeBtnStyle(bHmiM2_3, currentKebMode2 == 9, Color.FromArgb(217, 119, 6));
            SetHmiModeBtnStyle(bHmiM2_4, currentKebMode2 == 10, Color.FromArgb(139, 92, 246));
        // 模式互鎖控制：模式 7 與 9 為轉速控制；模式 8 與 10 為轉矩加載控制
            bool d1_spdEnable = (currentKebMode1 == 7 || currentKebMode1 == 9);
            bool d1_trqEnable = (currentKebMode1 == 8 || currentKebMode1 == 10);
            bool d2_spdEnable = (currentKebMode2 == 7 || currentKebMode2 == 9);
            bool d2_trqEnable = (currentKebMode2 == 8 || currentKebMode2 == 10);

            // 🚨 全面安全互鎖：當 LOCK 鎖定時，強制禁用對應的手動控制項，只能由「🎯目標值」來改變！
            if (isSpeedTracking)
            {
                // 轉速 LOCK 鎖定中：禁止手動更改速度，防止階躍落差衝擊
                d1_spdEnable = false;
                d2_spdEnable = false;
            }
            if (isClosedLoopTracking)
            {
                // 轉矩 LOCK 鎖定中：禁止手動更改轉矩，防止力矩突波暴衝
                d1_trqEnable = false;
                d2_trqEnable = false;
            }

            if (hmiSpdControls1 != null) { foreach (var c in hmiSpdControls1) if (c != null) c.Enabled = d1_spdEnable; }
            if (hmiTrqControls1 != null) { foreach (var c in hmiTrqControls1) if (c != null) c.Enabled = d1_trqEnable; }
            if (hmiSpdControls2 != null) { foreach (var c in hmiSpdControls2) if (c != null) c.Enabled = d2_spdEnable; }
            if (hmiTrqControls2 != null) { foreach (var c in hmiTrqControls2) if (c != null) c.Enabled = d2_trqEnable; }

            // 智能加載按鈕動態切換：保持 4 鍵網格永遠可見，絕不隱藏元件導致 TableLayoutPanel 破版！
            if (pnlRun1 != null && btnRF1 != null && btnRR1 != null && btnStop1 != null && btnReset1 != null)
            {
                if (d1_trqEnable)
                {
                    btnRF1.Text = "⚡ 啟用加載";
                    btnRF1.BackColor = Color.FromArgb(245, 158, 11);
                    btnRR1.Text = "⚡ 反向加載";
                    btnRR1.BackColor = Color.FromArgb(217, 119, 6);
                    btnStop1.Text = "🛑 卸載停機";
                }
                else
                {
                    btnRF1.Text = "正轉 (RUN)";
                    btnRF1.BackColor = Color.FromArgb(16, 185, 129);
                    btnRR1.Text = "反轉 (RUN)";
                    btnRR1.BackColor = Color.FromArgb(139, 92, 246);
                    btnStop1.Text = "🛑 停機";
                }
                btnRR1.Visible = true;
                pnlRun1.ColumnStyles[0].Width = 28f;
                pnlRun1.ColumnStyles[1].Width = 28f;
                pnlRun1.ColumnStyles[2].Width = 22f;
                if (pnlRun1.ColumnStyles.Count > 3) pnlRun1.ColumnStyles[3].Width = 22f;
            }

            if (pnlRun2 != null && btnRF2 != null && btnRR2 != null && btnStop2 != null && btnReset2 != null)
            {
                if (d2_trqEnable)
                {
                    btnRF2.Text = "⚡ 啟用加載";
                    btnRF2.BackColor = Color.FromArgb(245, 158, 11);
                    btnRR2.Text = "⚡ 反向加載";
                    btnRR2.BackColor = Color.FromArgb(217, 119, 6);
                    btnStop2.Text = "🛑 卸載停機";
                }
                else
                {
                    btnRF2.Text = "正轉 (RUN)";
                    btnRF2.BackColor = Color.FromArgb(16, 185, 129);
                    btnRR2.Text = "反轉 (RUN)";
                    btnRR2.BackColor = Color.FromArgb(139, 92, 246);
                    btnStop2.Text = "🛑 停機";
                }
                btnRR2.Visible = true;
                pnlRun2.ColumnStyles[0].Width = 28f;
                pnlRun2.ColumnStyles[1].Width = 28f;
                pnlRun2.ColumnStyles[2].Width = 22f;
                if (pnlRun2.ColumnStyles.Count > 3) pnlRun2.ColumnStyles[3].Width = 22f;
            }
        }

        private void SetHmiModeBtnStyle(Button btn, bool isActive, Color activeColor)
        {
            if (btn == null) return;
            btn.Enabled = true;
            if (isActive)
            {
                btn.BackColor = activeColor;
                btn.ForeColor = Color.White;
                btn.Font = new Font("微軟正黑體", 8.5f, FontStyle.Bold);
            }
            else
            {
                btn.BackColor = Color.FromArgb(241, 245, 249);
                btn.ForeColor = Color.FromArgb(51, 65, 85);
                btn.Font = new Font("微軟正黑體", 8.5f, FontStyle.Regular);
            }
        }

        private bool isHmiKebOpen1 = false;
        private bool isHmiKebOpen2 = false;
        private int cachedUd02_1 = -1, cachedUd02_2 = -1;
        private int cachedCs00_1 = -1, cachedCs00_2 = -1;
        private static readonly object kebLock = new object();
        private static int activeKebComIndex = -1;
        private static int activeKebBaudIndex = -1;



        private bool EnsureHmiKebOpen1()
        {
            if (isHmiKebOpen1) return true;
            string port = cmbHmiKebPort1.SelectedItem != null ? cmbHmiKebPort1.SelectedItem.ToString() : "COM1";
            int baud = int.Parse(cmbHmiKebBaud1.SelectedItem != null ? cmbHmiKebBaud1.SelectedItem.ToString() : "9600");
            int comIdx = GetHmiKebComIdx(1);
            int baudIdx = GetHmiKebBaudIdx(1);

            try
            {
                lock (kebLock)
                {
                    closechannels();
                    tProtProperty prop = new tProtProperty();
                    prop.ProtType = 1; // DIN 66019-II (prAnsi)
                    prop.Baudrate = baudIdx;
                    prop.Comport  = comIdx;
                    prop.TimeOut  = 600;
                    prop.Flag     = 0;
                    prop.Port     = 0;
                    prop.Txtlen   = 0;
                    prop.txt      = "";
                    setprotproperties(ref prop);
                    setretrycnt(3);
                    setinvprot((int)numHmiKebNode1.Value, 1);
                    activeKebComIndex = comIdx;
                    activeKebBaudIndex = baudIdx;
                }

                // 實體通訊握手校驗：多重探測 ud.02 (0x0802) / oP.00 (0x0300) / ru.00 (0x0200) / Sy.51 (0x0033) / ru.07 (0x0207)
                int? testVal = KebReadParamWithDll(comIdx, baudIdx, (int)numHmiKebNode1.Value, 0x0802);
                if (!testVal.HasValue) testVal = KebReadParamWithDll(comIdx, baudIdx, (int)numHmiKebNode1.Value, 0x0300);
                if (!testVal.HasValue) testVal = KebReadParamWithDll(comIdx, baudIdx, (int)numHmiKebNode1.Value, 0x0200);
                if (!testVal.HasValue) testVal = KebReadParamWithDll(comIdx, baudIdx, (int)numHmiKebNode1.Value, 0x0033);
                if (!testVal.HasValue) testVal = KebReadParamWithDll(comIdx, baudIdx, (int)numHmiKebNode1.Value, 0x0207);
                if (testVal.HasValue)
                {
                    isHmiKebOpen1 = true;
                    if (btnHmiPortToggle1 != null && !btnHmiPortToggle1.IsDisposed)
                    {
                        btnHmiPortToggle1.Text = "[Close]";
                        btnHmiPortToggle1.BackColor = Color.FromArgb(239, 68, 68);
                    }
                    lblHmiKebStatus1.Text = string.Format("狀態: 已連線 ({0} @ {1})", port, baud);
                    lblHmiKebStatus1.ForeColor = Color.Green;
                    if (lblPillKeb1 != null && !lblPillKeb1.IsDisposed)
                    {
                        lblPillKeb1.Text = " A載台: " + port + " ";
                        lblPillKeb1.ForeColor = Color.FromArgb(74, 222, 128);
                    }
                    if (txtHmiKebLog != null) txtHmiKebLog.AppendText(string.Format("[{0}] A載台 (加載端) 握手成功：{1} @ {2} bps (實體回應 0x{3:X4})\r\n", DateTime.Now.ToLongTimeString(), port, baud, testVal.Value));
                    
                    // 連線成功後第一動作：先讀回變頻器目前真實硬體參數，並同步至介面設定框與模式按鈕
                    ReadAndSyncHmiKebInitialParams(1);
                    return true;
                }
                else
                {
                    // 實體無回傳或連線失敗，絕不虛報連線！
                    CloseHmiKebPort1();
                    lblHmiKebStatus1.Text = string.Format("狀態: 連線失敗 ({0} 無回應)", port);
                    lblHmiKebStatus1.ForeColor = Color.Red;
                    if (lblPillKeb1 != null && !lblPillKeb1.IsDisposed)
                    {
                        lblPillKeb1.Text = " A載台: 斷線 ";
                        lblPillKeb1.ForeColor = Color.FromArgb(248, 113, 113);
                    }
                    if (txtHmiKebLog != null) txtHmiKebLog.AppendText(string.Format("[{0}] [A載台錯誤] {1} 站號 {2} 無回應，請檢查實體接線與變頻器電源！\r\n", DateTime.Now.ToLongTimeString(), port, numHmiKebNode1.Value));
                    return false;
                }
            }
            catch (Exception ex)
            {
                CloseHmiKebPort1();
                if (txtHmiKebLog != null) txtHmiKebLog.AppendText(string.Format("[A載台錯誤] 開啟 {0} 異常: {1}\r\n", port, ex.Message));
                lblHmiKebStatus1.Text = "狀態: 連線異常";
                lblHmiKebStatus1.ForeColor = Color.Red;
                if (lblPillKeb1 != null && !lblPillKeb1.IsDisposed)
                {
                    lblPillKeb1.Text = " A載台: 異常 ";
                    lblPillKeb1.ForeColor = Color.FromArgb(248, 113, 113);
                }
                return false;
            }
        }

        private void CloseHmiKebPort1()
        {
            try
            {
                if (isHmiKebOpen1) RestoreHmiKebInitialParams(1);
            }
            catch { }

            lock (kebLock)
            {
                try { closechannels(); } catch { }
                activeKebComIndex = -1;
            }
            if (spKeb1 != null) { try { spKeb1.Close(); spKeb1.Dispose(); } catch {} spKeb1 = null; }
            isHmiKebOpen1 = false;
            cachedUd02_1 = -1;
            cachedCs00_1 = -1;
            UpdateHmiKebModeParamsDisplay(1);
            if (btnHmiPortToggle1 != null && !btnHmiPortToggle1.IsDisposed)
            {
                btnHmiPortToggle1.Text = "[Open]";
                btnHmiPortToggle1.BackColor = Color.FromArgb(16, 185, 129);
            }
            if (lblHmiKebStatus1 != null && !lblHmiKebStatus1.IsDisposed)
            {
                lblHmiKebStatus1.Text = "狀態: 已釋放埠";
                lblHmiKebStatus1.ForeColor = Color.Gray;
            }
            if (lblPillKeb1 != null && !lblPillKeb1.IsDisposed)
            {
                lblPillKeb1.Text = " A載台: 待連線 ";
                lblPillKeb1.ForeColor = Color.FromArgb(203, 213, 225);
            }
            if (txtHmiKebLog != null) txtHmiKebLog.AppendText(string.Format("[{0}] A載台 COM 埠已釋放 (已解除鎖定，可供外部軟體使用)\r\n", DateTime.Now.ToLongTimeString()));
        }

        private bool EnsureHmiKebOpen2()
        {
            if (isHmiKebOpen2) return true;
            string port = cmbHmiKebPort2.SelectedItem != null ? cmbHmiKebPort2.SelectedItem.ToString() : "COM2";
            int baud = int.Parse(cmbHmiKebBaud2.SelectedItem != null ? cmbHmiKebBaud2.SelectedItem.ToString() : "9600");
            int comIdx = GetHmiKebComIdx(2);
            int baudIdx = GetHmiKebBaudIdx(2);

            try
            {
                lock (kebLock)
                {
                    closechannels();
                    tProtProperty prop = new tProtProperty();
                    prop.ProtType = 1; // DIN 66019-II (prAnsi)
                    prop.Baudrate = baudIdx;
                    prop.Comport  = comIdx;
                    prop.TimeOut  = 600;
                    prop.Flag     = 0;
                    prop.Port     = 0;
                    prop.Txtlen   = 0;
                    prop.txt      = "";
                    setprotproperties(ref prop);
                    setretrycnt(3);
                    setinvprot((int)numHmiKebNode2.Value, 1);
                    activeKebComIndex = comIdx;
                    activeKebBaudIndex = baudIdx;
                }

                // 實體通訊握手校驗：多重探測 ud.02 (0x0802) / oP.00 (0x0300) / ru.00 (0x0200) / Sy.51 (0x0033) / ru.07 (0x0207)
                int? testVal = KebReadParamWithDll(comIdx, baudIdx, (int)numHmiKebNode2.Value, 0x0802);
                if (!testVal.HasValue) testVal = KebReadParamWithDll(comIdx, baudIdx, (int)numHmiKebNode2.Value, 0x0300);
                if (!testVal.HasValue) testVal = KebReadParamWithDll(comIdx, baudIdx, (int)numHmiKebNode2.Value, 0x0200);
                if (!testVal.HasValue) testVal = KebReadParamWithDll(comIdx, baudIdx, (int)numHmiKebNode2.Value, 0x0033);
                if (!testVal.HasValue) testVal = KebReadParamWithDll(comIdx, baudIdx, (int)numHmiKebNode2.Value, 0x0207);
                if (testVal.HasValue)
                {
                    isHmiKebOpen2 = true;
                    if (btnHmiPortToggle2 != null && !btnHmiPortToggle2.IsDisposed)
                    {
                        btnHmiPortToggle2.Text = "[Close]";
                        btnHmiPortToggle2.BackColor = Color.FromArgb(239, 68, 68);
                    }
                    lblHmiKebStatus2.Text = string.Format("狀態: 已連線 ({0} @ {1})", port, baud);
                    lblHmiKebStatus2.ForeColor = Color.Green;
                    if (lblPillKeb2 != null && !lblPillKeb2.IsDisposed)
                    {
                        lblPillKeb2.Text = " B載台: " + port + " ";
                        lblPillKeb2.ForeColor = Color.FromArgb(74, 222, 128);
                    }
                    if (txtHmiKebLog != null) txtHmiKebLog.AppendText(string.Format("[{0}] B載台 (待測端) 握手成功：{1} @ {2} bps (實體回應 0x{3:X4})\r\n", DateTime.Now.ToLongTimeString(), port, baud, testVal.Value));
                    
                    // 連線成功後第一動作：先讀回變頻器目前真實硬體參數，並同步至介面設定框與模式按鈕
                    ReadAndSyncHmiKebInitialParams(2);
                    return true;
                }
                else
                {
                    // 實體無回傳或連線失敗，絕不虛報連線！
                    CloseHmiKebPort2();
                    lblHmiKebStatus2.Text = string.Format("狀態: 連線失敗 ({0} 無回應)", port);
                    lblHmiKebStatus2.ForeColor = Color.Red;
                    if (lblPillKeb2 != null && !lblPillKeb2.IsDisposed)
                    {
                        lblPillKeb2.Text = " B載台: 斷線 ";
                        lblPillKeb2.ForeColor = Color.FromArgb(248, 113, 113);
                    }
                    if (txtHmiKebLog != null) txtHmiKebLog.AppendText(string.Format("[{0}] [B載台錯誤] {1} 站號 {2} 無回應，請檢查實體接線與變頻器電源！\r\n", DateTime.Now.ToLongTimeString(), port, numHmiKebNode2.Value));
                    return false;
                }
            }
            catch (Exception ex)
            {
                CloseHmiKebPort2();
                if (txtHmiKebLog != null) txtHmiKebLog.AppendText(string.Format("[B載台錯誤] 開啟 {0} 異常: {1}\r\n", port, ex.Message));
                lblHmiKebStatus2.Text = "狀態: 連線異常";
                lblHmiKebStatus2.ForeColor = Color.Red;
                if (lblPillKeb2 != null && !lblPillKeb2.IsDisposed)
                {
                    lblPillKeb2.Text = " B載台: 異常 ";
                    lblPillKeb2.ForeColor = Color.FromArgb(248, 113, 113);
                }
                return false;
            }
        }

        private void CloseHmiKebPort2()
        {
            try
            {
                if (isHmiKebOpen2) RestoreHmiKebInitialParams(2);
            }
            catch { }

            lock (kebLock)
            {
                try { closechannels(); } catch { }
                activeKebComIndex = -1;
            }
            if (spKeb2 != null) { try { spKeb2.Close(); spKeb2.Dispose(); } catch {} spKeb2 = null; }
            isHmiKebOpen2 = false;
            cachedUd02_2 = -1;
            cachedCs00_2 = -1;
            UpdateHmiKebModeParamsDisplay(2);
            if (btnHmiPortToggle2 != null && !btnHmiPortToggle2.IsDisposed)
            {
                btnHmiPortToggle2.Text = "[Open]";
                btnHmiPortToggle2.BackColor = Color.FromArgb(16, 185, 129);
            }
            if (lblHmiKebStatus2 != null && !lblHmiKebStatus2.IsDisposed)
            {
                lblHmiKebStatus2.Text = "狀態: 已釋放埠";
                lblHmiKebStatus2.ForeColor = Color.Gray;
            }
            if (lblPillKeb2 != null && !lblPillKeb2.IsDisposed)
            {
                lblPillKeb2.Text = " B載台: 待連線 ";
                lblPillKeb2.ForeColor = Color.FromArgb(203, 213, 225);
            }
            if (txtHmiKebLog != null) txtHmiKebLog.AppendText(string.Format("[{0}] B載台 COM 埠已釋放 (已解除鎖定，可供外部軟體使用)\r\n", DateTime.Now.ToLongTimeString()));
        }

        private void UpdateHmiKebModeParamsDisplay(int driveId, int mode = -1, int speedVal = -1, double trqPercent = -1.0, int lastCmd = -1)
        {
            if (driveId == 1)
            {
                if (mode != -1) currentKebMode1 = mode;
                if (lastCmd != -1) lastSy50Cmd1 = lastCmd;
            }
            else
            {
                if (mode != -1) currentKebMode2 = mode;
                if (lastCmd != -1) lastSy50Cmd2 = lastCmd;
            }

            // 零序列通訊負載架構：直接依據硬體快取與模式即時推論，徹底移除 8 次實體暫存器高頻輪詢！
            bool isOpen = (driveId == 1) ? isHmiKebOpen1 : isHmiKebOpen2;
            string diagDesc = "";
            if (!isOpen)
            {
                diagDesc = "【未連線 / 待開啟 COM 埠】";
            }
            else
            {
                int ud = (driveId == 1) ? cachedUd02_1 : cachedUd02_2;
                int cs = (driveId == 1) ? cachedCs00_1 : cachedCs00_2;
                int curMode = (driveId == 1) ? currentKebMode1 : currentKebMode2;

                if (ud == 0) diagDesc = "【V/F 開迴路 (F5-Basic)】-> 走 Sy.52 (0x0034) 硬體自動換算 rpm->Hz";
                else if (ud == 1) diagDesc = "【SMM 無感測向量 (F5-General)】-> 走 Sy.52 (0x0034) 過程數據給定";
                else if (ud == 2)
                {
                    if (curMode == 8 || curMode == 10 || cs == 6) diagDesc = "【轉矩加載模式 (F5-Multi cs00=6)】-> 走 cs.18 (0x0F12) 轉矩給定";
                    else if (cs == 0) diagDesc = "【V/F 模式 (F5-Multi cs00=0)】-> 走 Sy.52 (0x0034) 硬體自動換算 rpm->Hz";
                    else diagDesc = "【閉迴路向量轉速 (F5-Multi cs00=4)】-> 走 Sy.52 (0x0034) 閉迴路控速";
                }
                else if (ud == 3) diagDesc = "【伺服向量控制 (F5-Servo)】-> 走 Sy.52 (0x0034) 閉迴路伺服控制";
                else if (curMode == 8 || curMode == 10 || cs == 6) diagDesc = "【轉矩加載模式 (cs00=6)】-> 走 cs.18 轉矩給定";
                else if (curMode == 7 || curMode == 9 || cs == 4) diagDesc = "【閉迴路向量轉速 (cs00=4)】-> 走 Sy.52 閉迴路控速";
                else diagDesc = "【連線就緒，請選擇控制模式】";
            }

            string realText = "🔍 系統判斷：" + diagDesc;

            if (this.IsHandleCreated && !this.IsDisposed)
            {
                if (this.InvokeRequired)
                {
                    this.BeginInvoke((MethodInvoker)delegate {
                        if (driveId == 1 && lblKebModeParams1 != null && !lblKebModeParams1.IsDisposed)
                        {
                            if (lblKebModeParams1.Text != realText) lblKebModeParams1.Text = realText;
                        }
                        else if (driveId == 2 && lblKebModeParams2 != null && !lblKebModeParams2.IsDisposed)
                        {
                            if (lblKebModeParams2.Text != realText) lblKebModeParams2.Text = realText;
                        }
                    });
                }
                else
                {
                    if (driveId == 1 && lblKebModeParams1 != null && !lblKebModeParams1.IsDisposed)
                    {
                        if (lblKebModeParams1.Text != realText) lblKebModeParams1.Text = realText;
                    }
                    else if (driveId == 2 && lblKebModeParams2 != null && !lblKebModeParams2.IsDisposed)
                    {
                        if (lblKebModeParams2.Text != realText) lblKebModeParams2.Text = realText;
                    }
                }
            }
        }

        private void ReadAndSyncHmiKebInitialParams(int driveId)
        {
            int comIdx = GetHmiKebComIdx(driveId);
            int baudIdx = GetHmiKebBaudIdx(driveId);
            int node = (int)((driveId == 1) ? numHmiKebNode1.Value : numHmiKebNode2.Value);
            string driveName = (driveId == 1) ? "A載台" : "B載台";

            try
            {
                int? r_ru00 = KebReadParamWithDll(comIdx, baudIdx, node, 0x0200);
                int? r_ru43 = KebReadParamWithDll(comIdx, baudIdx, node, 0x022B); // ru.43 硬體故障碼
                int? r_ud02 = KebReadParamWithDll(comIdx, baudIdx, node, 0x0802);
                int? r_op00 = KebReadParamWithDll(comIdx, baudIdx, node, 0x0300);
                int? r_op01 = KebReadParamWithDll(comIdx, baudIdx, node, 0x0301);
                int? r_cs00 = KebReadParamWithDll(comIdx, baudIdx, node, 0x0F00);
                int? r_cs15 = KebReadParamWithDll(comIdx, baudIdx, node, 0x0F0F);
                int? r_cs18 = KebReadParamWithDll(comIdx, baudIdx, node, 0x0F12);
                int? r_cs19 = KebReadParamWithDll(comIdx, baudIdx, node, 0x0F13);
                int? r_op03 = KebReadParamWithDll(comIdx, baudIdx, node, 0x0303);
                int? r_sy50 = KebReadParamWithDll(comIdx, baudIdx, node, 0x0032);
                int? r_sy52 = KebReadParamWithDll(comIdx, baudIdx, node, 0x0034);

                int? r_dr00 = KebReadParamWithDll(comIdx, baudIdx, node, 0x0400); // dr.00 額定電流 (0.1 A)
                int? r_dr01 = KebReadParamWithDll(comIdx, baudIdx, node, 0x0401); // dr.01 額定轉速 (1.0 rpm)
                int? r_dr02 = KebReadParamWithDll(comIdx, baudIdx, node, 0x0402); // dr.02 額定電壓 (1.0 V)
                int? r_dr03 = KebReadParamWithDll(comIdx, baudIdx, node, 0x0403); // dr.03 額定功率 (0.01 kW)
                int? r_dr04 = KebReadParamWithDll(comIdx, baudIdx, node, 0x0404); // dr.04 功率因數 (0.01)
                int? r_dr05 = KebReadParamWithDll(comIdx, baudIdx, node, 0x0405); // dr.05 額定頻率 (0.1 Hz)

                double drSpeed = r_dr01.HasValue ? (double)r_dr01.Value : 0.0;
                double drFreq = r_dr05.HasValue ? ((double)r_dr05.Value * 0.1) : 0.0;
                int motorPoles = (drSpeed > 0 && drFreq > 0) ? (int)Math.Round(120.0 * drFreq / drSpeed) : 0;

                string udDesc = r_ud02.HasValue ? ((r_ud02.Value == 0) ? "F5-Basic (V/F)" : ((r_ud02.Value == 1) ? "F5-General (SMM)" : ((r_ud02.Value == 2) ? "F5-Multi (開/閉迴路向量)" : ((r_ud02.Value == 3) ? "F5-Servo (伺服)" : "特殊架構")))) : "--";
                string csDesc = r_cs00.HasValue ? ((r_cs00.Value == 0) ? "V/F 開迴路 (cs00=0)" : ((r_cs00.Value == 4) ? "閉迴路向量速度控制 (cs00=4)" : ((r_cs00.Value == 6) ? "轉矩加載控制 (cs00=6)" : "自訂模式"))) : "--";
                double valCs18 = r_cs18.HasValue ? (r_cs18.Value * 0.1) : 0.0;

                // 嚴謹解析 cs.19 (0x0F13): 只有確實讀到硬體暫存器數值時才進行物理換算
                string cs19SnapshotDesc = "未讀取到 (通訊未回傳)";
                string cs19BannerText = "cs.19基準: 讀取失敗 (0x0F13無回傳)";
                bool cs19Valid = false;

                if (r_cs19.HasValue && r_cs19.Value > 0)
                {
                    cs19Valid = true;
                    hasReadCs19 = true;
                    // KEB F5 官方規格: cS.19 解析度 0.01 Nm (例 RAW 2000 = 20.00 Nm, RAW 500 = 5.00 Nm)
                    double realCs19 = (double)r_cs19.Value * 0.01;
                    if (driveId == 1) cs19TorqueRef1 = realCs19; else cs19TorqueRef2 = realCs19;

                    double activeCs19 = (currentKebMode1 == 8 || currentKebMode1 == 10) ? cs19TorqueRef1 : cs19TorqueRef2;
                    if (activeCs19 <= 0) activeCs19 = realCs19;
                    calculatedMinDeadbandTorque = Math.Round(activeCs19 / 1000.0, 3);

                    cs19SnapshotDesc = string.Format("{0:F2} Nm (RAW:{1}, 單步物理極限 x = cs19/1000 = {2:F3} Nm)", realCs19, r_cs19.Value, calculatedMinDeadbandTorque);
                    cs19BannerText = string.Format("cs.19基準: {0:F2} Nm (RAW:{1}, 步進: {2:F3}Nm)", realCs19, r_cs19.Value, calculatedMinDeadbandTorque);

                    WriteHmiLog("KEB_RAW", string.Format("【cs.19 實體讀取成功】[{0}] 暫存器0x0F13 RAW={1} -> 換算 cs.19={2:F2} Nm | 0.1% 單步極限 x = cs19/1000 = {3:F3} Nm",
                        driveName, r_cs19.Value, realCs19, calculatedMinDeadbandTorque));
                }
                else
                {
                    WriteHmiLog("KEB_RAW", string.Format("【cs.19 讀取注意】[{0}] 暫存器 0x0F13 未回傳有效數值 (r_cs19={1})",
                        driveName, (r_cs19.HasValue ? r_cs19.Value.ToString() : "null")));
                }

                double valOp03 = r_op03.HasValue ? (r_op03.Value * 0.125) : 0.0;
                double valSy52 = r_sy52.HasValue ? (double)r_sy52.Value : 0.0;

                // ★【斷線前自動回寫備份機制】：記錄連線初始之硬體參數快照
                KebBackupParams backup = (driveId == 1) ? kebBackup1 : kebBackup2;
                backup.HasBackup = true;
                backup.Ru00 = r_ru00;
                backup.Ud02 = r_ud02;
                backup.Op00 = r_op00;
                backup.Op01 = r_op01;
                backup.Cs00 = r_cs00;
                backup.Cs15 = r_cs15;
                backup.Cs18 = r_cs18;
                backup.Cs19 = r_cs19;
                backup.Op03 = r_op03;
                backup.Sy50 = r_sy50;
                backup.Sy52 = r_sy52;
                backup.BackupTime = DateTime.Now;

                WriteHmiLog("KEB_BACKUP", string.Format("【{0} 初始硬體參數備份成功】oP00={1}, oP01={2}, cs00={3}, cs15={4}, cs18={5}, oP03={6}, Sy52={7} (將於按下斷線或關閉程式時自動安全回寫)",
                    driveName,
                    r_op00.HasValue ? r_op00.Value.ToString() : "--",
                    r_op01.HasValue ? r_op01.Value.ToString() : "--",
                    r_cs00.HasValue ? r_cs00.Value.ToString() : "--",
                    r_cs15.HasValue ? r_cs15.Value.ToString() : "--",
                    r_cs18.HasValue ? r_cs18.Value.ToString() : "--",
                    r_op03.HasValue ? r_op03.Value.ToString() : "--",
                    r_sy52.HasValue ? r_sy52.Value.ToString() : "--"));

                string logSnapshot = string.Format(
                    "======================================================================\r\n" +
                    "[{0}] 【{1}】連線成功，已讀回目前硬體配置參數快照\r\n" +
                    "----------------------------------------------------------------------\r\n" +
                    "- 連線通訊埠: {2} (站號 Node: {3})\r\n" +
                    "- ud.02 (變頻器應用模式) : {4} -> {5}\r\n" +
                    "- oP.00 (目標給定值來源) : {6} (2:oP03給定, 5:Sy52過程數據)\r\n" +
                    "- oP.01 (運轉控制來源)   : {7} (7:半自動, 8:通訊運轉控制)\r\n" +
                    "- cs.00 (控制架構模式)   : {8} -> {9}\r\n" +
                    "- cs.15 (轉矩限制來源)   : {10}\r\n" +
                    "- cs.18 (數位設定轉矩)   : {11:F1} % (RAW: {12})\r\n" +
                    "- cs.19 (轉矩基準/額定)  : {13}\r\n" +
                    "- oP.03 (數位設定轉速)   : {14:F1} rpm (RAW: {15})\r\n" +
                    "- Sy.50 (目前控制字狀態) : {16}\r\n" +
                    "- Sy.52 (過程數據轉速)   : {17:F1} rpm (RAW: {18})\r\n" +
                    "----------------------------------------------------------------------\r\n" +
                    "【dr 馬達銘牌參數反算驗證】\r\n" +
                    "- dr.00 (馬達額定電流)   : {19:F1} A (RAW: {20})\r\n" +
                    "- dr.01 (馬達額定轉速)   : {21:F0} rpm (RAW: {22})\r\n" +
                    "- dr.02 (馬達額定電壓)   : {23} V\r\n" +
                    "- dr.03 (馬達額定功率)   : {24:F2} kW\r\n" +
                    "- dr.04 (馬達功率因數)   : {25:F2}\r\n" +
                    "- dr.05 (馬達額定頻率)   : {26:F1} Hz (RAW: {27})\r\n" +
                    "- dr 參數精確反算極數(P) : {28} 極 (依公式 P = round(120 * f / n))\r\n" +
                    "======================================================================\r\n\r\n",
                    DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                    driveName,
                    (driveId == 1) ? "COM1" : "COM2",
                    node,
                    r_ud02.HasValue ? r_ud02.Value.ToString() : "--", udDesc,
                    r_op00.HasValue ? r_op00.Value.ToString() : "--",
                    r_op01.HasValue ? r_op01.Value.ToString() : "--",
                    r_cs00.HasValue ? r_cs00.Value.ToString() : "--", csDesc,
                    r_cs15.HasValue ? r_cs15.Value.ToString() : "--",
                    valCs18, r_cs18.HasValue ? r_cs18.Value.ToString() : "--",
                    cs19SnapshotDesc,
                    valOp03, r_op03.HasValue ? r_op03.Value.ToString() : "--",
                    r_sy50.HasValue ? r_sy50.Value.ToString() : "--",
                    valSy52, r_sy52.HasValue ? r_sy52.Value.ToString() : "--",
                    r_dr00.HasValue ? ((double)r_dr00.Value * 0.1) : 0.0, r_dr00.HasValue ? r_dr00.Value.ToString() : "--",
                    drSpeed, r_dr01.HasValue ? r_dr01.Value.ToString() : "--",
                    r_dr02.HasValue ? r_dr02.Value.ToString() : "--",
                    r_dr03.HasValue ? ((double)r_dr03.Value * 0.01) : 0.0,
                    r_dr04.HasValue ? ((double)r_dr04.Value * 0.01) : 0.0,
                    drFreq, r_dr05.HasValue ? r_dr05.Value.ToString() : "--",
                    motorPoles
                );

                this.BeginInvoke((MethodInvoker)delegate {
                    if (txtKebConfigLog != null && !txtKebConfigLog.IsDisposed)
                    {
                        txtKebConfigLog.AppendText(logSnapshot);
                    }

                    // 確實更新 A/B 載台的 cs.19 顯示標籤
                    if (driveId == 1 && lblCs19Disp1 != null && !lblCs19Disp1.IsDisposed)
                    {
                        lblCs19Disp1.Text = cs19BannerText;
                        lblCs19Disp1.ForeColor = cs19Valid ? Color.FromArgb(16, 185, 129) : Color.FromArgb(239, 68, 68);
                    }
                    else if (driveId == 2 && lblCs19Disp2 != null && !lblCs19Disp2.IsDisposed)
                    {
                        lblCs19Disp2.Text = cs19BannerText;
                        lblCs19Disp2.ForeColor = cs19Valid ? Color.FromArgb(16, 185, 129) : Color.FromArgb(239, 68, 68);
                    }

                    // 確實更新轉矩卡片死區標籤
                    if (lblDbTrqHint != null && !lblDbTrqHint.IsDisposed)
                    {
                        if (cs19Valid && calculatedMinDeadbandTorque > 0)
                        {
                            lblDbTrqHint.Text = string.Format("⚡死區(≥{0:F3})", calculatedMinDeadbandTorque);
                            if (numCardDeadband != null && !numCardDeadband.IsDisposed && numCardDeadband.Value < (decimal)calculatedMinDeadbandTorque)
                            {
                                numCardDeadband.Value = (decimal)calculatedMinDeadbandTorque;
                                trackingDeadband = (decimal)calculatedMinDeadbandTorque;
                            }
                        }
                        else
                        {
                            lblDbTrqHint.Text = "⚡死區/誤差";
                        }
                    }

                    // ★【鐵律要求：連線上線當下，畫面各數值輸入框強制全部歸零！】
                    if (driveId == 1)
                    {
                        if (numHmiKebSpeed1 != null) numHmiKebSpeed1.Value = 0;
                        if (numHmiKebTorque1 != null) numHmiKebTorque1.Value = 0;
                    }
                    else
                    {
                        if (numHmiKebSpeed2 != null) numHmiKebSpeed2.Value = 0;
                        if (numHmiKebTorque2 != null) numHmiKebTorque2.Value = 0;
                    }

                    if (r_ud02.HasValue) { if (driveId == 1) cachedUd02_1 = r_ud02.Value; else cachedUd02_2 = r_ud02.Value; }
                    if (r_cs00.HasValue)
                    {
                        if (driveId == 1) cachedCs00_1 = r_cs00.Value; else cachedCs00_2 = r_cs00.Value;
                        bool isFullAuto = (r_op01.HasValue && r_op01.Value == 8);
                        int mode = (r_cs00.Value == 6) ? (isFullAuto ? 10 : 8) : (isFullAuto ? 9 : 7);
                        if (driveId == 1) currentKebMode1 = mode; else currentKebMode2 = mode;
                        UpdateHmiKebModeButtonsVisual();
                    }
                    UpdateHmiKebModeParamsDisplay(driveId);
                });

                // ★【鐵律安全防護 1：連線上線強制發送 STOP (Sy.50=0)，徹底消滅背景偷跑！】
                KebWriteParamWithDll(comIdx, baudIdx, node, 0x0032, 0); // Sy.50 = 0 (強制停機)
                
                // ★【鐵律安全防護 2：若當前變頻器處於 FAULT (ru.43 != 0)，自動發送 FAULT RESET (Sy.50=2) 清除報警！】
                if (r_ru43.HasValue && r_ru43.Value != 0)
                {
                    WriteHmiLog("KEB_WARN", string.Format("【{0} 上線檢測到硬體報警】ru.43=0x{1:X2} ({2})，自動發送 FAULT RESET...", driveName, r_ru43.Value, DecodeKebFaultCode(r_ru43.Value)));
                    KebWriteParamWithDll(comIdx, baudIdx, node, 0x0032, 2); // Sy.50 = 2 (Reset)
                    Thread.Sleep(50);
                    KebWriteParamWithDll(comIdx, baudIdx, node, 0x0032, 0); // 復歸為 0
                }

                // ★【鐵律安全防護 3：若 cs.00 殘留為 4 (向量模式)，強制復歸原生安全 cs.00 = 0 (V/F 模式)！】
                if (r_cs00.HasValue && r_cs00.Value == 4)
                {
                    KebWriteParamWithDll(comIdx, baudIdx, node, 0x0F00, 0); // cs.00 = 0 (V/F)
                    if (driveId == 1) cachedCs00_1 = 0; else cachedCs00_2 = 0;
                }

                // ★【全系統統一 Sy.52 架構】：oP.00=5 (Sy52過程數據給定), oP.01=7 (半自動實體端子開關控制)
                KebWriteParamWithDll(comIdx, baudIdx, node, 0x0300, 5); // oP.00 = 5 (Sy.52)
                KebWriteParamWithDll(comIdx, baudIdx, node, 0x0301, 7); // oP.01 = 7 (半自動端子開關控制)

                // ★【鐵律安全防護 4：速度與加載端轉矩強制歸零，速度驅動端維持 100% 轉矩極限】
                KebWriteParam32(comIdx, baudIdx, node, 0x0034, 0, "連線速度歸零 (Sy.52=0)");
                
                // 若為轉矩加載端 (cs00=6 或 Mode 8/10)，加載轉矩強制歸零 cs.18 = 0
                // 若為速度驅動端 (cs00!=6 或 Mode 7/9)，轉矩極限維持 100.0% (cs.18 = 1000)，絕不鎖死馬達出力！
                if (r_cs00.HasValue && r_cs00.Value == 6)
                {
                    KebWriteParamWithDll(comIdx, baudIdx, node, 0x0F12, 0); // 加載端 cs.18 = 0.0%
                }
                else
                {
                    KebWriteParamWithDll(comIdx, baudIdx, node, 0x0F12, 1000); // 速度驅動端放行 100.0% 轉矩極限
                }

                // 獨立檔案儲存：寫入 logs/KEB_Readback_Parameters.log
                try
                {
                    string logDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs");
                    if (!Directory.Exists(logDir)) Directory.CreateDirectory(logDir);
                    string filePath = Path.Combine(logDir, "KEB_Readback_Parameters.log");
                    File.AppendAllText(filePath, logSnapshot, Encoding.UTF8);
                }
                catch { }

                if (txtHmiKebLog != null)
                {
                    txtHmiKebLog.AppendText(string.Format("[OK] [{0}] 已讀回硬體當前配置: ud02={1}, oP00={2}, oP01={3}, cs00={4}, cs18={5:F1}%, oP03={6:F1} rpm | dr極數={7}極 (dr01={8:F0}rpm, dr05={9:F1}Hz) (所有設定與變頻器已強制歸零0)\r\n",
                        driveName,
                        r_ud02.HasValue ? r_ud02.Value.ToString() : "--",
                        r_op00.HasValue ? r_op00.Value.ToString() : "--",
                        r_op01.HasValue ? r_op01.Value.ToString() : "--",
                        r_cs00.HasValue ? r_cs00.Value.ToString() : "--",
                        valCs18,
                        valOp03,
                        motorPoles,
                        drSpeed,
                        drFreq));
                }
            }
            catch { }
        }

        private void RestoreHmiKebInitialParams(int driveId)
        {
            KebBackupParams backup = (driveId == 1) ? kebBackup1 : kebBackup2;
            bool isOpen = (driveId == 1) ? isHmiKebOpen1 : isHmiKebOpen2;
            if (backup == null || !backup.HasBackup || !isOpen) return;

            int comIdx = GetHmiKebComIdx(driveId);
            int baudIdx = GetHmiKebBaudIdx(driveId);
            int node = (int)((driveId == 1) ? numHmiKebNode1.Value : numHmiKebNode2.Value);
            string driveName = (driveId == 1) ? "A載台" : "B載台";

            try
            {
                // 1. 安全第一：先強制發送 STOP (Sy.50 = 0) 並將 cs.18 轉矩卸載為 0
                KebWriteParamWithDll(comIdx, baudIdx, node, 0x0032, 0); // Sy.50 = 0
                KebWriteParamWithDll(comIdx, baudIdx, node, 0x0F12, 0); // cs.18 = 0
                Thread.Sleep(20);

                // 2. 依序安全回寫連線前讀回之硬體參數
                if (backup.Sy52.HasValue)
                {
                    KebWriteParam32(comIdx, baudIdx, node, 0x0034, backup.Sy52.Value, "還原 Sy.52");
                }
                if (backup.Op03.HasValue)
                {
                    KebWriteParamWithDll(comIdx, baudIdx, node, 0x0303, backup.Op03.Value);
                }
                if (backup.Cs18.HasValue)
                {
                    KebWriteParamWithDll(comIdx, baudIdx, node, 0x0F12, backup.Cs18.Value);
                }
                if (backup.Cs15.HasValue)
                {
                    KebWriteParamWithDll(comIdx, baudIdx, node, 0x0F0F, backup.Cs15.Value);
                }
                if (backup.Cs00.HasValue)
                {
                    KebWriteParamWithDll(comIdx, baudIdx, node, 0x0F00, backup.Cs00.Value);
                }
                if (backup.Op01.HasValue)
                {
                    KebWriteParamWithDll(comIdx, baudIdx, node, 0x0301, backup.Op01.Value);
                }
                if (backup.Op00.HasValue)
                {
                    KebWriteParamWithDll(comIdx, baudIdx, node, 0x0300, backup.Op00.Value);
                }
                if (backup.Sy50.HasValue)
                {
                    int safeSy50 = (backup.Sy50.Value == 4 || backup.Sy50.Value == 12) ? 0 : backup.Sy50.Value;
                    KebWriteParamWithDll(comIdx, baudIdx, node, 0x0032, safeSy50);
                }

                backup.HasBackup = false; // 已成功還原，清除備份標記

                string restoreMsg = string.Format(
                    "[{0}] 【{1} 參數回寫完成】已成功還原連線前讀回之原始硬體參數：oP.00={2}, oP.01={3}, cs.00={4}, cs.15={5}, cs.18={6}, oP.03={7}, Sy.52={8}",
                    DateTime.Now.ToString("HH:mm:ss"),
                    driveName,
                    backup.Op00.HasValue ? backup.Op00.Value.ToString() : "--",
                    backup.Op01.HasValue ? backup.Op01.Value.ToString() : "--",
                    backup.Cs00.HasValue ? backup.Cs00.Value.ToString() : "--",
                    backup.Cs15.HasValue ? backup.Cs15.Value.ToString() : "--",
                    backup.Cs18.HasValue ? backup.Cs18.Value.ToString() : "--",
                    backup.Op03.HasValue ? backup.Op03.Value.ToString() : "--",
                    backup.Sy52.HasValue ? backup.Sy52.Value.ToString() : "--");

                WriteHmiLog("KEB_RESTORE", restoreMsg);
                if (txtHmiKebLog != null && !txtHmiKebLog.IsDisposed)
                {
                    txtHmiKebLog.AppendText(restoreMsg + "\r\n");
                }
            }
            catch (Exception ex)
            {
                WriteHmiLog("KEB_RESTORE_ERR", string.Format("[{0}] 回寫原始參數發生異常: {1}", driveName, ex.Message));
            }
        }

        private bool SafeWriteKebParamWithRetry(int comIdx, int baudIdx, int nodeId, int paramAddr, int val, string desc)
        {
            bool ok = KebWriteParamWithDll(comIdx, baudIdx, nodeId, paramAddr, val);
            if (!ok)
            {
                Thread.Sleep(60);
                ok = KebWriteParamWithDll(comIdx, baudIdx, nodeId, paramAddr, val);
            }
            Thread.Sleep(30);
            return ok;
        }



        private void SetHmiKebMode(int comIdx, int baudIdx, int nodeId, int mode, string driveName)
        {
            int driveId = driveName.Contains("A") || driveName.Contains("1") ? 1 : 2;
            int currentSpd = driveId == 1 && numHmiKebSpeed1 != null ? (int)numHmiKebSpeed1.Value : (driveId == 2 && numHmiKebSpeed2 != null ? (int)numHmiKebSpeed2.Value : 0);
            double currentTrq = driveId == 1 && numHmiKebTorque1 != null ? (double)numHmiKebTorque1.Value : (driveId == 2 && numHmiKebTorque2 != null ? (double)numHmiKebTorque2.Value : 0.0);

            // 100% 嚴格對齊 VB6 Form_Dyno_Main.frm (行 9995~10070) 實測驗證之原廠設定：
            if (mode == 7) // 數位定轉速 (oP00=5 Sy52過程數據, oP01=7 半自動端子控制, cs00=0 V/F模式, cs15=3, cs18=1000)
            {
                if (driveId == 1) cachedCs00_1 = 0; else cachedCs00_2 = 0;
                SafeWriteKebParamWithRetry(comIdx, baudIdx, nodeId, 0x0300, 5, "oP.00=5 (Sy52過程數據給定)");
                SafeWriteKebParamWithRetry(comIdx, baudIdx, nodeId, 0x0301, 7, "oP.01=7 (半自動端子開關控制)");
                SafeWriteKebParamWithRetry(comIdx, baudIdx, nodeId, 0x0F00, 0, "cs.00=0 (V/F速度模式)");
                SafeWriteKebParamWithRetry(comIdx, baudIdx, nodeId, 0x0F0F, 3, "cs.15=3 (轉矩極限來源)");
                SafeWriteKebParamWithRetry(comIdx, baudIdx, nodeId, 0x0F12, 1000, "cs.18=1000 (轉矩極限100.0%)");
                KebWriteParam32(comIdx, baudIdx, nodeId, 0x0034, currentSpd, "Sy.52=速度(1:1 rpm)");
                if (txtHmiKebLog != null) txtHmiKebLog.AppendText(string.Format("[OK] [{0}] 已切換為【數位定轉速 Mode 7 (標準: oP00=5(Sy52), oP01=7(端子控制), cs00=0(V/F), cs15=3, cs18=100%)】\r\n", driveName));
                WriteHmiLog("MODE_CHANGE", string.Format("[{0}] 模式切換完成：【數位定轉速 Mode 7 (半自動)】oP.00=5 (Sy52), oP.01=7 (端子), cs.00=0 (V/F), cs.15=3, cs.18=100%", driveName));
                UpdateHmiKebModeParamsDisplay(driveId, 7, currentSpd, currentTrq, 0);
            }
            else if (mode == 8) // 數位定轉矩 (oP00=5 Sy52過程數據, oP01=7 半自動端子控制, cs00=6 轉矩加載, cs15=3, cs18=設定轉矩)
            {
                if (driveId == 1) cachedCs00_1 = 6; else cachedCs00_2 = 6;
                int trqRaw = (int)Math.Round(currentTrq * 10); // 0.1% 解析度
                SafeWriteKebParamWithRetry(comIdx, baudIdx, nodeId, 0x0300, 5, "oP.00=5 (Sy52過程數據給定)");
                SafeWriteKebParamWithRetry(comIdx, baudIdx, nodeId, 0x0301, 7, "oP.01=7 (半自動端子開關控制)");
                SafeWriteKebParamWithRetry(comIdx, baudIdx, nodeId, 0x0F00, 6, "cs.00=6 (轉矩加載控制)");
                SafeWriteKebParamWithRetry(comIdx, baudIdx, nodeId, 0x0F0F, 3, "cs.15=3 (轉矩極限來源)");
                SafeWriteKebParamWithRetry(comIdx, baudIdx, nodeId, 0x0F12, trqRaw, "cs.18=設定轉矩");
                KebWriteParam32(comIdx, baudIdx, nodeId, 0x0034, 0, "Sy.52=0 (加載端速度歸零)");
                if (txtHmiKebLog != null) txtHmiKebLog.AppendText(string.Format("[OK] [{0}] 已切換為【數位定轉矩 Mode 8 (標準: oP00=5(Sy52), oP01=7(端子控制), cs00=6(加載), cs15=3, cs18加載)】\r\n", driveName));
                WriteHmiLog("MODE_CHANGE", string.Format("[{0}] 模式切換完成：【數位定轉矩 Mode 8 (半自動)】oP.00=5 (Sy52), oP.01=7 (端子), cs.00=6 (轉矩), cs.15=3, cs.18={1:F1}%", driveName, currentTrq));
                UpdateHmiKebModeParamsDisplay(driveId, 8, currentSpd, currentTrq, 0);
            }
            else if (mode == 9) // 數位定轉速 (全自動: oP00=5 Sy52過程數據, oP01=8 通訊運轉控制, cs00=0 V/F模式, cs15=3, cs18=1000)
            {
                if (driveId == 1) cachedCs00_1 = 0; else cachedCs00_2 = 0;
                SafeWriteKebParamWithRetry(comIdx, baudIdx, nodeId, 0x0300, 5, "oP.00=5 (Sy52過程數據給定)");
                SafeWriteKebParamWithRetry(comIdx, baudIdx, nodeId, 0x0301, 8, "oP.01=8 (全自動通訊控制)");
                SafeWriteKebParamWithRetry(comIdx, baudIdx, nodeId, 0x0F00, 0, "cs.00=0 (V/F速度模式)");
                SafeWriteKebParamWithRetry(comIdx, baudIdx, nodeId, 0x0F0F, 3, "cs.15=3 (轉矩極限來源)");
                SafeWriteKebParamWithRetry(comIdx, baudIdx, nodeId, 0x0F12, 1000, "cs.18=1000 (轉矩極限100.0%)");
                KebWriteParam32(comIdx, baudIdx, nodeId, 0x0034, currentSpd, "Sy.52=速度(1:1 rpm)");
                if (txtHmiKebLog != null) txtHmiKebLog.AppendText(string.Format("[OK] [{0}] 已切換為【數位定轉速 Mode 9 (全自動: oP00=5(Sy52), oP01=8(通訊控制), cs00=0(V/F), cs15=3, cs18=100%)】\r\n", driveName));
                WriteHmiLog("MODE_CHANGE", string.Format("[{0}] 模式切換完成：【數位定轉速 Mode 9 (全自動)】oP.00=5 (Sy52), oP.01=8 (通訊), cs.00=0 (V/F), cs.15=3, cs.18=100%", driveName));
                UpdateHmiKebModeParamsDisplay(driveId, 9, currentSpd, currentTrq, 0);
            }
            else if (mode == 10) // 數位定轉矩 (全自動: oP00=5 Sy52過程數據, oP01=8 通訊運轉控制, cs00=6 轉矩加載控制, cs15=3, cs18=設定轉矩)
            {
                if (driveId == 1) cachedCs00_1 = 6; else cachedCs00_2 = 6;
                int trqRaw = (int)Math.Round(currentTrq * 10); // 0.1% 解析度
                SafeWriteKebParamWithRetry(comIdx, baudIdx, nodeId, 0x0300, 5, "oP.00=5 (Sy52過程數據給定)");
                SafeWriteKebParamWithRetry(comIdx, baudIdx, nodeId, 0x0301, 8, "oP.01=8 (全自動通訊控制)");
                SafeWriteKebParamWithRetry(comIdx, baudIdx, nodeId, 0x0F00, 6, "cs.00=6 (轉矩加載控制)");
                SafeWriteKebParamWithRetry(comIdx, baudIdx, nodeId, 0x0F0F, 3, "cs.15=3 (轉矩極限來源)");
                SafeWriteKebParamWithRetry(comIdx, baudIdx, nodeId, 0x0F12, trqRaw, "cs.18=設定轉矩");
                KebWriteParam32(comIdx, baudIdx, nodeId, 0x0034, 0, "Sy.52=0 (加載端速度歸零)");
                if (txtHmiKebLog != null) txtHmiKebLog.AppendText(string.Format("[OK] [{0}] 已切換為【數位定轉矩 Mode 10 (全自動: oP00=5(Sy52), oP01=8(通訊控制), cs00=6(加載), cs15=3, cs18加載)】\r\n", driveName));
                WriteHmiLog("MODE_CHANGE", string.Format("[{0}] 模式切換完成：【數位定轉矩 Mode 10 (全自動)】oP.00=5 (Sy52), oP.01=8 (通訊), cs.00=6 (轉矩), cs.15=3, cs.18={1:F1}%", driveName, currentTrq));
                UpdateHmiKebModeParamsDisplay(driveId, 10, currentSpd, currentTrq, 0);
            }
        }

        private DateTime lastEstopTriggerTime = DateTime.MinValue;
        public void TriggerGlobalEmergencyStop()
        {
            // 防抖保護：3 秒內不重複觸發，避免安全保護反覆連發大量 KEB 寫入電文，飽和通訊匯流排
            if ((DateTime.Now - lastEstopTriggerTime).TotalSeconds < 3.0) return;
            lastEstopTriggerTime = DateTime.Now;

            // 最高安全級別：強制中斷所有運轉，歸零給定值
            // ⚠️ 注意：此處不加外層 lock(kebLock)，因為 KebWriteParamWithDll 已內建 lock。
            // 若加外層鎖，在背景 Worker 持有 kebLock 等待 DLL timeout 期間，
            // 緊急停機和其他 UI 操作三方搶鎖，造成跨執行緒死鎖（UI 死當）！
            for (int i = 0; i < 2; i++) // 降低至 2 次，減少 KEB 匯流排負擔
            {
                if (isHmiKebOpen1)
                {
                    KebWriteParamWithDll(GetHmiKebComIdx(1), GetHmiKebBaudIdx(1), (int)numHmiKebNode1.Value, 0x0032, 0); // Sy.50 = 0 (STOP)
                    KebWriteParamWithDll(GetHmiKebComIdx(1), GetHmiKebBaudIdx(1), (int)numHmiKebNode1.Value, 0x0034, 0); // Sy.52 = 0
                    KebWriteParamWithDll(GetHmiKebComIdx(1), GetHmiKebBaudIdx(1), (int)numHmiKebNode1.Value, 0x0F12, 0); // cs.18 = 0
                }
                if (isHmiKebOpen2)
                {
                    KebWriteParamWithDll(GetHmiKebComIdx(2), GetHmiKebBaudIdx(2), (int)numHmiKebNode2.Value, 0x0032, 0); // Sy.50 = 0 (STOP)
                    KebWriteParamWithDll(GetHmiKebComIdx(2), GetHmiKebBaudIdx(2), (int)numHmiKebNode2.Value, 0x0034, 0); // Sy.52 = 0
                    KebWriteParamWithDll(GetHmiKebComIdx(2), GetHmiKebBaudIdx(2), (int)numHmiKebNode2.Value, 0x0F12, 0); // cs.18 = 0
                }
                Thread.Sleep(15);
            }

            // 徹底重置控制字旗標與自動測試狀態，防止停機後安全矩陣反覆誤判運轉中
            lastSy50Cmd1 = 0;
            lastSy50Cmd2 = 0;
            if (dutyTimer != null && dutyTimer.Enabled) dutyTimer.Stop();
            if (tnTimer != null && tnTimer.Enabled) tnTimer.Stop();
            if (effMapTimer != null && effMapTimer.Enabled) effMapTimer.Stop();
            isNoLoadRunning = false;
            isClosedLoopTracking = false;
            isSpeedTracking = false;
            stallStartTime = DateTime.MinValue;
            tempTripStartTime = DateTime.MinValue;

            if (this.IsHandleCreated && !this.IsDisposed)
            {
                this.BeginInvoke((MethodInvoker)delegate {
                    activeTrackingTorquePct = 0.0m;
                    activeTrackingSpeedRpm = 0.0m;
                    if (numHmiKebSpeed1 != null) numHmiKebSpeed1.Value = 0;
                    if (numHmiKebTorque1 != null) numHmiKebTorque1.Value = 0;
                    if (numHmiKebSpeed2 != null) numHmiKebSpeed2.Value = 0;
                    if (numHmiKebTorque2 != null) numHmiKebTorque2.Value = 0;
                    UpdateHmiKebModeParamsDisplay(1);
                    UpdateHmiKebModeParamsDisplay(2);
                });
            }

            WriteHmiLog("EMERGENCY_STOP", "【🚨 緊急停機 E-STOP 觸發】雙機運轉已強制中斷，所有給定值立即歸零！");
            if (txtHmiKebLog != null)
            {
                txtHmiKebLog.AppendText(string.Format("[{0}] >> [🚨 緊急停機 E-STOP 觸發] 雙載台已強制停機並歸零！\r\n", DateTime.Now.ToLongTimeString()));
            }
            UploadLatestLogToCloudAsync(false);
        }

        public void ExecuteFullAutoGracefulStop(string reason)
        {
            // 全自動雙機平穩連鎖停機序列 (Graceful Stop Sequence)：
            // 步驟 1: 加載端先將轉矩卸載歸零 (cs.18 = 0)，消除負載阻力，防止單側停機造成的反拖衝擊
            if (isHmiKebOpen1 && (currentKebMode1 == 8 || currentKebMode1 == 10))
            {
                KebWriteParamWithDll(GetHmiKebComIdx(1), GetHmiKebBaudIdx(1), (int)numHmiKebNode1.Value, 0x0F12, 0); // cs.18 = 0 (卸載)
            }
            if (isHmiKebOpen2 && (currentKebMode2 == 8 || currentKebMode2 == 10))
            {
                KebWriteParamWithDll(GetHmiKebComIdx(2), GetHmiKebBaudIdx(2), (int)numHmiKebNode2.Value, 0x0F12, 0); // cs.18 = 0 (卸載)
            }

            // 步驟 2: 雙機下達停機電文 (Sy.50 = 0)，驅動端沿著減速斜率平穩降速至 0 rpm 斷電
            if (isHmiKebOpen1)
            {
                KebWriteParamWithDll(GetHmiKebComIdx(1), GetHmiKebBaudIdx(1), (int)numHmiKebNode1.Value, 0x0032, 0); // Sy.50 = 0 (STOP)
                lastSy50Cmd1 = 0;
            }
            if (isHmiKebOpen2)
            {
                KebWriteParamWithDll(GetHmiKebComIdx(2), GetHmiKebBaudIdx(2), (int)numHmiKebNode2.Value, 0x0032, 0); // Sy.50 = 0 (STOP)
                lastSy50Cmd2 = 0;
            }

            if (this.IsHandleCreated && !this.IsDisposed)
            {
                this.BeginInvoke((MethodInvoker)delegate {
                    UpdateHmiKebModeParamsDisplay(1);
                    UpdateHmiKebModeParamsDisplay(2);
                });
            }

            WriteHmiLog("FULL_AUTO_STOP", string.Format("【全自動連鎖平穩停機】原因：{0}。已依序執行「轉矩先卸載歸零 ➔ 轉速平穩減速停電」雙機保護！", reason));
            if (txtHmiKebLog != null)
            {
                txtHmiKebLog.AppendText(string.Format("[{0}] >> [全自動連鎖平穩停機] 雙載台已依序安全卸載並平穩停機！\r\n", DateTime.Now.ToLongTimeString()));
            }
            UploadLatestLogToCloudAsync(false);
        }

        // =========================================================================
        // 全自動平滑停機狀態機 (Gradual Graceful Auto-Stop)
        // 規則：先降負載 1/10 或 1%，再降速 1/10 或 100rpm，兩條件達標後一起下達 Sy.50=0；
        //       若初始小於 1% 或 100rpm 則直接下達 Sy.50=0。
        // =========================================================================
        // =========================================================================
        // 全自動加載煞車加速停機狀態機 (Dynamic Braking Auto-Stop Engine)
        // 規則：先將待測端下達 Sy.50=0 切斷動力，由加載端持續提供負載作為反拖煞車；
        //       即時監測轉速，等到實測轉速下降至 < 500 rpm，再對加載端卸載歸零並下達 Sy.50=0。
        //       若初始轉速已 < 500 rpm 則直接雙台下達 Sy.50=0。
        // =========================================================================
        private System.Windows.Forms.Timer tmrGradualStop = null;
        public bool isGradualStopping = false;
        private int gradualSpdDrive = 1;
        private int gradualTrqDrive = 2;
        private string gradualStopReason = "";
        private Action gradualOnComplete = null;
        private int brakeTickCount = 0;

        public void StartGradualAutoStop(int spdDrive, int trqDrive, string reason, Action onComplete = null)
        {
            try
            {
                if (spdDrive <= 0 || trqDrive <= 0)
                {
                    int dutyRole = (cmbDutyMiniRole != null && cmbDutyMiniRole.SelectedIndex >= 0)
                        ? cmbDutyMiniRole.SelectedIndex
                        : ((cmbDutyRole != null && cmbDutyRole.SelectedIndex >= 0) ? cmbDutyRole.SelectedIndex : 1);
                    spdDrive = (dutyRole == 1) ? 2 : 1;
                    trqDrive = (spdDrive == 1) ? 2 : 1;
                }

                gradualSpdDrive = spdDrive;
                gradualTrqDrive = trqDrive;
                gradualStopReason = reason;
                gradualOnComplete = onComplete;
                brakeTickCount = 0;

                int spdCom = GetHmiKebComIdx(spdDrive), spdBaud = GetHmiKebBaudIdx(spdDrive), spdNode = (spdDrive == 1) ? (int)numHmiKebNode1.Value : (int)numHmiKebNode2.Value;
                int trqCom = GetHmiKebComIdx(trqDrive), trqBaud = GetHmiKebBaudIdx(trqDrive), trqNode = (trqDrive == 1) ? (int)numHmiKebNode1.Value : (int)numHmiKebNode2.Value;

                // 取得當前實測轉速
                double currentSpd = Math.Abs(actSpeed);
                if (currentSpd <= 0)
                {
                    currentSpd = (spdDrive == 1 && numHmiKebSpeed1 != null) ? (double)numHmiKebSpeed1.Value : ((spdDrive == 2 && numHmiKebSpeed2 != null) ? (double)numHmiKebSpeed2.Value : 0.0);
                }

                double thresholdRpm = (double)autoStopBrakeThresholdRpm;
                if (thresholdRpm <= 10.0) thresholdRpm = 500.0;

                WriteHmiLog("BRAKE_STOP", string.Format("【啟動煞車加速停機程序】原因: {0} | 當前轉速: {1:F0} rpm | 卸載門檻: {2:F0} rpm (待測{3} ➔ 先停 / 加載{4} ➔ 煞車)",
                    reason, currentSpd, thresholdRpm, spdDrive == 1 ? "A載台" : "B載台", trqDrive == 1 ? "A載台" : "B載台"));

                // ★【低於門檻轉速直達】：若目前轉速已經 < thresholdRpm，直接雙機下達 Sy.50=0
                if (currentSpd < thresholdRpm)
                {
                    isGradualStopping = false;
                    // 待測端停機
                    KebWriteParamWithDll(spdCom, spdBaud, spdNode, 0x0032, 0); // Sy.50 = 0
                    if (spdDrive == 1) lastSy50Cmd1 = 0; else lastSy50Cmd2 = 0;
                    KebWriteParam32(spdCom, spdBaud, spdNode, 0x0034, 0, "停機轉速歸零");
                    if (spdDrive == 1 && numHmiKebSpeed1 != null) numHmiKebSpeed1.Value = 0;
                    else if (spdDrive == 2 && numHmiKebSpeed2 != null) numHmiKebSpeed2.Value = 0;

                    // 加載端卸載並停機
                    KebWriteParamWithDll(trqCom, trqBaud, trqNode, 0x0F12, 0); // cs.18 = 0
                    KebWriteParamWithDll(trqCom, trqBaud, trqNode, 0x0032, 0); // Sy.50 = 0
                    if (trqDrive == 1) lastSy50Cmd1 = 0; else lastSy50Cmd2 = 0;
                    if (trqDrive == 1 && numHmiKebTorque1 != null) numHmiKebTorque1.Value = 0;
                    else if (trqDrive == 2 && numHmiKebTorque2 != null) numHmiKebTorque2.Value = 0;

                    if (this.IsHandleCreated && !this.IsDisposed)
                    {
                        this.BeginInvoke((MethodInvoker)delegate {
                            UpdateHmiKebModeParamsDisplay(1);
                            UpdateHmiKebModeParamsDisplay(2);
                        });
                    }

                    WriteHmiLog("BRAKE_STOP", string.Format("【煞車停機直達】當前轉速已低於門檻 ({0:F0} < {1:F0} rpm)，雙載台直接下達 Sy.50=0 停機完成！", currentSpd, thresholdRpm));
                    if (onComplete != null) onComplete();
                    return;
                }

                // ★【大於等於門檻轉速：加載煞車狀態機】：
                // 步驟 1: 待測端立即下達 Sy.50 = 0 (切斷動力)，轉速設定歸零
                KebWriteParamWithDll(spdCom, spdBaud, spdNode, 0x0032, 0); // Sy.50 = 0
                if (spdDrive == 1) lastSy50Cmd1 = 0; else lastSy50Cmd2 = 0;
                KebWriteParam32(spdCom, spdBaud, spdNode, 0x0034, 0, "待測端斷電停轉");
                if (spdDrive == 1 && numHmiKebSpeed1 != null) numHmiKebSpeed1.Value = 0;
                else if (spdDrive == 2 && numHmiKebSpeed2 != null) numHmiKebSpeed2.Value = 0;

                // 步驟 2: 加載端暫時維持原載轉矩 (提供反拖煞車作用，大幅加速停機時間)
                isGradualStopping = true;
                WriteHmiLog("BRAKE_STOP", string.Format("【待測端已下達 Sy.50=0 停機】加載端維持運轉提供煞車作用，等待轉速下降至 < {0:F0} rpm...", thresholdRpm));
                if (txtHmiKebLog != null)
                {
                    txtHmiKebLog.AppendText(string.Format("[{0}] >> [煞車停機] 待測端已斷電(Sy.50=0)，加載端提供反拖煞車中，等待降速至 <{1:F0} rpm...\r\n", DateTime.Now.ToLongTimeString(), thresholdRpm));
                }

                // 啟動高頻即時監測定時器 (每 100ms 檢查一次即時轉速)
                if (tmrGradualStop == null)
                {
                    tmrGradualStop = new System.Windows.Forms.Timer() { Interval = 100 };
                    tmrGradualStop.Tick += TmrGradualStop_Tick;
                }
                tmrGradualStop.Stop();
                tmrGradualStop.Start();
            }
            catch (Exception ex)
            {
                WriteHmiLog("BRAKE_STOP_ERR", "啟動煞車停機失敗: " + ex.Message);
                ExecuteFullAutoGracefulStop(reason);
                if (onComplete != null) onComplete();
            }
        }

        private void TmrGradualStop_Tick(object sender, EventArgs e)
        {
            try
            {
                brakeTickCount++;
                double spdNow = Math.Abs(actSpeed);
                int trqCom = GetHmiKebComIdx(gradualTrqDrive), trqBaud = GetHmiKebBaudIdx(gradualTrqDrive), trqNode = (gradualTrqDrive == 1) ? (int)numHmiKebNode1.Value : (int)numHmiKebNode2.Value;

                double thresholdRpm = (double)autoStopBrakeThresholdRpm;
                if (thresholdRpm <= 10.0) thresholdRpm = 500.0;

                // 判定條件：實測轉速已下降至 < thresholdRpm，或者超過 15 秒安全超時防線
                bool isBraked = (spdNow < thresholdRpm);
                bool isTimeout = (brakeTickCount >= 150); // 150 * 100ms = 15 秒

                if (isBraked || isTimeout)
                {
                    tmrGradualStop.Stop();
                    isGradualStopping = false;

                    // 步驟 3: 轉速已小於門檻轉速，對加載端卸載歸零並下達 Sy.50 = 0 命令！
                    KebWriteParamWithDll(trqCom, trqBaud, trqNode, 0x0F12, 0); // cs.18 = 0
                    KebWriteParamWithDll(trqCom, trqBaud, trqNode, 0x0032, 0); // Sy.50 = 0
                    if (gradualTrqDrive == 1) lastSy50Cmd1 = 0; else lastSy50Cmd2 = 0;
                    if (gradualTrqDrive == 1 && numHmiKebTorque1 != null) numHmiKebTorque1.Value = 0;
                    else if (gradualTrqDrive == 2 && numHmiKebTorque2 != null) numHmiKebTorque2.Value = 0;

                    // 確保待測端 Sy.50 也是 0
                    int spdCom = GetHmiKebComIdx(gradualSpdDrive), spdBaud = GetHmiKebBaudIdx(gradualSpdDrive), spdNode = (gradualSpdDrive == 1) ? (int)numHmiKebNode1.Value : (int)numHmiKebNode2.Value;
                    KebWriteParamWithDll(spdCom, spdBaud, spdNode, 0x0032, 0);
                    if (gradualSpdDrive == 1) lastSy50Cmd1 = 0; else lastSy50Cmd2 = 0;

                    if (this.IsHandleCreated && !this.IsDisposed)
                    {
                        this.BeginInvoke((MethodInvoker)delegate {
                            UpdateHmiKebModeParamsDisplay(1);
                            UpdateHmiKebModeParamsDisplay(2);
                        });
                    }

                    string finishReason = isTimeout ? "安全超時保護 (15s)" : string.Format("實測轉速已降至 {0:F0} rpm (<{1:F0} rpm)", spdNow, thresholdRpm);
                    WriteHmiLog("BRAKE_STOP", string.Format("【煞車加速停機成功】{0}，加載端已成功卸載並下達 Sy.50=0，雙機停機圓滿完成！", finishReason));
                    if (txtHmiKebLog != null)
                    {
                        txtHmiKebLog.AppendText(string.Format("[{0}] >> [煞車停機完成] 轉速已降至 {1:F0} rpm (<{2:F0} rpm)，加載端卸載歸零並發送 Sy.50=0！\r\n", DateTime.Now.ToLongTimeString(), spdNow, thresholdRpm));
                    }

                    if (gradualOnComplete != null)
                    {
                        Action cb = gradualOnComplete;
                        gradualOnComplete = null;
                        cb();
                    }
                    UploadLatestLogToCloudAsync(false);
                }
            }
            catch (Exception ex)
            {
                if (tmrGradualStop != null) tmrGradualStop.Stop();
                isGradualStopping = false;
                ExecuteFullAutoGracefulStop("煞車監聽例外: " + ex.Message);
                if (gradualOnComplete != null)
                {
                    Action cb = gradualOnComplete;
                    gradualOnComplete = null;
                    cb();
                }
            }
        }

        private void SetHmiKebCommand(int comIdx, int baudIdx, int nodeId, int cmd, string driveName)
        {
            if (cmd == 4 || cmd == 12)
            {
                // 啟動運轉時刷新扭力計心跳時間，給予保護啟動寬限期
                lastTorquePacketTime = DateTime.Now;
            }

            int driveId = driveName.Contains("A") || driveName.Contains("1") ? 1 : 2;
            int mode = driveId == 1 ? currentKebMode1 : currentKebMode2;
            int currentSpd = driveId == 1 && numHmiKebSpeed1 != null ? (int)numHmiKebSpeed1.Value : (driveId == 2 && numHmiKebSpeed2 != null ? (int)numHmiKebSpeed2.Value : 0);
            double currentTrq = driveId == 1 && numHmiKebTorque1 != null ? (double)numHmiKebTorque1.Value : (driveId == 2 && numHmiKebTorque2 != null ? (double)numHmiKebTorque2.Value : 0.0);

            bool ok = KebWriteParamWithDll(comIdx, baudIdx, nodeId, 0x0032, cmd);
            string cmdName = (cmd == 4) ? "RUN 正轉 (Sy50=4)" : ((cmd == 12) ? "RUN 反轉 (Sy50=12)" : ((cmd == 0) ? "STOP 停機 (Sy50=0)" : ((cmd == 2) ? "FAULT RESET 復歸 (Sy50=2)" : string.Format("CONTROL (Sy50={0})", cmd))));
            
            if (txtHmiKebLog != null) txtHmiKebLog.AppendText(string.Format("[{0}] [{1}] 發送：{2} (回應: {3})\r\n", DateTime.Now.ToLongTimeString(), driveName, cmdName, ok ? "ACK 成功" : "NAK/逾時"));
            WriteHmiLog("USER_OP", string.Format("[{0}] 使用者發送運轉控制：{1} (回應: {2})", driveName, cmdName, ok ? "ACK 成功" : "NAK/逾時"));

            UpdateHmiKebModeParamsDisplay(driveId, mode, currentSpd, currentTrq, cmd);
        }

        private void DoHmiKebQuery1()
        {
            if (!isHmiKebOpen1) return;
            int addr = (int)numHmiKebNode1.Value;
            int comIdx = GetHmiKebComIdx(1);
            int baudIdx = GetHmiKebBaudIdx(1);

            try
            {
                hmiKebCount1++;
                List<Tuple<int, string, Color>> gridUpdates = new List<Tuple<int, string, Color>>();
                var items = kebMonitorList1;

                int successCount = 0;
                // 優先讀取 ru.00 (0x0200 運轉狀態/硬體ST端子)，即時安全守護
                int? ru00_1 = KebReadParamWithDll(comIdx, baudIdx, addr, 0x0200);
                if (ru00_1.HasValue)
                {
                    successCount++;
                    CheckHardwareStStatus(1, ru00_1.Value);
                }

                if (items != null)
                {
                    for (int r = 0; r < items.Count; r++)
                    {
                        var item = items[r];
                        if (item.IsNode)
                        {
                            gridUpdates.Add(Tuple.Create(r, "Node " + addr, Color.FromArgb(15, 23, 42)));
                            continue;
                        }

                        int? val = KebReadParamWithDll(comIdx, baudIdx, addr, item.Address);
                        if (val.HasValue)
                        {
                            successCount++;
                            if (item.Address == 0x0200)
                            {
                                CheckHardwareStStatus(1, val.Value);
                            }
                            if (item.Address == 0x020F)
                            {
                                kebCurrent1 = val.Value * 0.1;
                            }
                            if (item.Address == 0x0203)
                            {
                                kebFrequency1 = (Math.Abs(val.Value) >= 100000) ? (val.Value * 0.0001) : (val.Value * 0.01);
                            }
                            if (item.IsStatus)
                            {
                                lastKebFaultCode1 = val.Value;
                                string text = (val.Value == 0) ? "正常 (無異常)" : DecodeKebFaultCode(val.Value);
                                Color color = (val.Value == 0) ? Color.FromArgb(16, 185, 129) : Color.Red;
                                gridUpdates.Add(Tuple.Create(r, text, color));

                                // 變頻器硬體故障 (ru.43 != 0) 雙機連鎖急停與自動測試異常中斷
                                if (val.Value != 0)
                                {
                                    if (dutyTimer != null && dutyTimer.Enabled)
                                    {
                                        dutyTimer.Stop();
                                        ExecuteFullAutoGracefulStop(string.Format("A載台硬體故障 (ru.43={0}: {1})", val.Value, DecodeKebFaultCode(val.Value)));
                                        WriteHmiLog("DUTY_ABORT", string.Format("【🚨 DUTY 測試異常中斷】檢測到 A載台突發硬體故障 (ru.43={0}: {1})，已強制安全卸載停機！", val.Value, DecodeKebFaultCode(val.Value)));
                                    }
                                    if (tnTimer != null && tnTimer.Enabled)
                                    {
                                        tnTimer.Stop();
                                        ExecuteFullAutoGracefulStop(string.Format("A載台硬體故障 (ru.43={0}: {1})", val.Value, DecodeKebFaultCode(val.Value)));
                                        WriteHmiLog("TN_ABORT", string.Format("【🚨 T-N 測試異常中斷】檢測到 A載台突發硬體故障 (ru.43={0}: {1})，已強制安全停機！", val.Value, DecodeKebFaultCode(val.Value)));
                                    }
                                    if (effMapTimer != null && effMapTimer.Enabled)
                                    {
                                        effMapTimer.Stop();
                                        ExecuteFullAutoGracefulStop(string.Format("A載台硬體故障 (ru.43={0}: {1})", val.Value, DecodeKebFaultCode(val.Value)));
                                        WriteHmiLog("EFFMAP_ABORT", string.Format("【🚨 效率MAP 測試異常中斷】檢測到 A載台突發硬體故障 (ru.43={0}: {1})，已強制安全停機！", val.Value, DecodeKebFaultCode(val.Value)));
                                    }

                                    if (enableProtKebFault && (lastSy50Cmd1 != 0 || lastSy50Cmd2 != 0))
                                    {
                                        TriggerGlobalEmergencyStop();
                                        WriteHmiLog("SAFETY_TRIP", string.Format("【🚨 安全保護跳脫】A載台檢測到內部硬體故障 (ru.43={0}: {1})，對側已連鎖緊急停機！", val.Value, DecodeKebFaultCode(val.Value)));
                                    }
                                }
                            }
                            else if (item.IsHex)
                            {
                                gridUpdates.Add(Tuple.Create(r, string.Format("0x{0:X4}", val.Value), Color.FromArgb(15, 23, 42)));
                            }
                            else
                            {
                                double scale = (item.Address == 0x0034) ? 1.0 : item.Scale;
                                double displayVal = val.Value * scale;
                                string fmt = (scale == 1.0) ? "{0:F0} {1}" : ((scale == 0.1) ? "{0:F1} {1}" : "{0:F2} {1}");
                                gridUpdates.Add(Tuple.Create(r, string.Format(fmt, displayVal, item.Unit).Trim(), Color.FromArgb(15, 23, 42)));
                            }
                        }
                    }
                }

                if (items == null || !items.Exists(it => it.Address == 0x0203))
                {
                    int? ru03_1 = KebReadParamWithDll(comIdx, baudIdx, addr, 0x0203);
                    if (ru03_1.HasValue)
                    {
                        successCount++;
                        kebFrequency1 = (Math.Abs(ru03_1.Value) >= 100000) ? (ru03_1.Value * 0.0001) : (ru03_1.Value * 0.01);
                    }
                }

                // 斷線緩衝狀態機判定
                if (successCount > 0)
                {
                    lastKebSuccessTime1 = DateTime.Now;
                    if (isKebReconnecting1)
                    {
                        isKebReconnecting1 = false;
                        isKebTimeoutTriggered1 = false;
                        WriteHmiLog("RECONNECT", "[OK] A載台通訊瞬斷已自愈恢復，測試保持進行！");
                    }
                }
                else
                {
                    double elapsedSec = (DateTime.Now - lastKebSuccessTime1).TotalSeconds;
                    if (elapsedSec > 1.5 && elapsedSec <= (double)disconnectBufferSeconds)
                    {
                        isKebReconnecting1 = true;
                        double remSec = Math.Max(0, (double)disconnectBufferSeconds - elapsedSec);
                        if (lblPillKeb1 != null && !lblPillKeb1.IsDisposed)
                        {
                            this.BeginInvoke((MethodInvoker)delegate {
                                lblPillKeb1.Text = string.Format(" A載台: [⚠️ 瞬斷重連中 ({0:F0}s)] ", remSec);
                                lblPillKeb1.ForeColor = Color.FromArgb(245, 158, 11);
                            });
                        }
                    }
                    else if (elapsedSec > (double)disconnectBufferSeconds && !isKebTimeoutTriggered1)
                    {
                        isKebTimeoutTriggered1 = true;
                        isHmiKebOpen1 = false;
                        if (lblPillKeb1 != null && !lblPillKeb1.IsDisposed)
                        {
                            this.BeginInvoke((MethodInvoker)delegate {
                                lblPillKeb1.Text = " A載台: [徹底斷線] ";
                                lblPillKeb1.ForeColor = Color.FromArgb(239, 68, 68);
                            });
                        }

                        // 分級保護機制
                        if (currentKebMode1 == 8 || currentKebMode1 == 10)
                        {
                            // 狀況一：加載端斷線逾時 -> 停止待測端 (B載台) 轉速命令並斷電
                            if (isHmiKebOpen2)
                            {
                                KebWriteParamWithDll(GetHmiKebComIdx(2), GetHmiKebBaudIdx(2), (int)numHmiKebNode2.Value, 0x0032, 0); // STOP
                                KebWriteParamWithDll(GetHmiKebComIdx(2), GetHmiKebBaudIdx(2), (int)numHmiKebNode2.Value, 0x0034, 0); // Sy.52 = 0
                                WriteHmiLog("FAILSAFE", "【🚨 分級連鎖停機】加載端 (A載台) 斷線逾時，待測端 (B載台) 已自動安全停機斷電！");
                            }
                        }
                        else
                        {
                            // 狀況二：待測端斷線逾時 -> 加載端 (B載台) 將轉矩降至 0% 後斷電
                            if (isHmiKebOpen2)
                            {
                                KebWriteParamWithDll(GetHmiKebComIdx(2), GetHmiKebBaudIdx(2), (int)numHmiKebNode2.Value, 0x0F12, 0); // cs.18 = 0 (卸載)
                                KebWriteParamWithDll(GetHmiKebComIdx(2), GetHmiKebBaudIdx(2), (int)numHmiKebNode2.Value, 0x0032, 0); // STOP
                                WriteHmiLog("FAILSAFE", "【🚨 分級連鎖停機】待測端 (A載台) 斷線逾時，加載端 (B載台) 轉矩已自動卸載歸零並停電！");
                            }
                        }
                    }
                }

                // 批次安全更新 UI (耗時 < 1ms，零 I/O 阻塞)
                if (this.IsHandleCreated && !this.IsDisposed)
                {
                    this.BeginInvoke((MethodInvoker)delegate {
                        if (dgvKebRu1 != null && gridUpdates.Count > 0)
                        {
                            foreach (var u in gridUpdates)
                            {
                                if (u.Item1 < dgvKebRu1.Rows.Count)
                                {
                                    dgvKebRu1.Rows[u.Item1].Cells[1].Value = u.Item2;
                                    dgvKebRu1.Rows[u.Item1].Cells[1].Style.ForeColor = u.Item3;
                                }
                            }
                        }
                    });
                }
                UpdateHmiKebModeParamsDisplay(1);
            }
            catch { }
        }

        private void DoHmiKebQuery2()
        {
            if (!isHmiKebOpen2) return;
            int addr = (int)numHmiKebNode2.Value;
            int comIdx = GetHmiKebComIdx(2);
            int baudIdx = GetHmiKebBaudIdx(2);

            try
            {
                hmiKebCount2++;
                List<Tuple<int, string, Color>> gridUpdates = new List<Tuple<int, string, Color>>();
                var items = kebMonitorList2;

                int successCount = 0;
                // 優先讀取 ru.00 (0x0200 運轉狀態/硬體ST端子)，即時安全守護
                int? ru00_2 = KebReadParamWithDll(comIdx, baudIdx, addr, 0x0200);
                if (ru00_2.HasValue)
                {
                    successCount++;
                    CheckHardwareStStatus(2, ru00_2.Value);
                }

                if (items != null)
                {
                    for (int r = 0; r < items.Count; r++)
                    {
                        var item = items[r];
                        if (item.IsNode)
                        {
                            gridUpdates.Add(Tuple.Create(r, "Node " + addr, Color.FromArgb(15, 23, 42)));
                            continue;
                        }

                        int? val = KebReadParamWithDll(comIdx, baudIdx, addr, item.Address);
                        if (val.HasValue)
                        {
                            successCount++;
                            if (item.Address == 0x0200)
                            {
                                CheckHardwareStStatus(2, val.Value);
                            }
                            if (item.Address == 0x020F)
                            {
                                kebCurrent2 = val.Value * 0.1;
                            }
                            if (item.Address == 0x0203)
                            {
                                kebFrequency2 = (Math.Abs(val.Value) >= 100000) ? (val.Value * 0.0001) : (val.Value * 0.01);
                            }
                            if (item.IsStatus)
                            {
                                lastKebFaultCode2 = val.Value;
                                string text = (val.Value == 0) ? "正常 (無異常)" : DecodeKebFaultCode(val.Value);
                                Color color = (val.Value == 0) ? Color.FromArgb(16, 185, 129) : Color.Red;
                                gridUpdates.Add(Tuple.Create(r, text, color));

                                // 變頻器硬體故障 (ru.43 != 0) 雙機連鎖急停與自動測試異常中斷
                                if (val.Value != 0)
                                {
                                    if (dutyTimer != null && dutyTimer.Enabled)
                                    {
                                        dutyTimer.Stop();
                                        ExecuteFullAutoGracefulStop(string.Format("B載台硬體故障 (ru.43={0}: {1})", val.Value, DecodeKebFaultCode(val.Value)));
                                        WriteHmiLog("DUTY_ABORT", string.Format("【🚨 DUTY 測試異常中斷】檢測到 B載台突發硬體故障 (ru.43={0}: {1})，已強制安全卸載停機！", val.Value, DecodeKebFaultCode(val.Value)));
                                    }
                                    if (tnTimer != null && tnTimer.Enabled)
                                    {
                                        tnTimer.Stop();
                                        ExecuteFullAutoGracefulStop(string.Format("B載台硬體故障 (ru.43={0}: {1})", val.Value, DecodeKebFaultCode(val.Value)));
                                        WriteHmiLog("TN_ABORT", string.Format("【🚨 T-N 測試異常中斷】檢測到 B載台突發硬體故障 (ru.43={0}: {1})，已強制安全停機！", val.Value, DecodeKebFaultCode(val.Value)));
                                    }
                                    if (effMapTimer != null && effMapTimer.Enabled)
                                    {
                                        effMapTimer.Stop();
                                        ExecuteFullAutoGracefulStop(string.Format("B載台硬體故障 (ru.43={0}: {1})", val.Value, DecodeKebFaultCode(val.Value)));
                                        WriteHmiLog("EFFMAP_ABORT", string.Format("【🚨 效率MAP 測試異常中斷】檢測到 B載台突發硬體故障 (ru.43={0}: {1})，已強制安全停機！", val.Value, DecodeKebFaultCode(val.Value)));
                                    }

                                    if (enableProtKebFault && (lastSy50Cmd1 != 0 || lastSy50Cmd2 != 0))
                                    {
                                        TriggerGlobalEmergencyStop();
                                        WriteHmiLog("SAFETY_TRIP", string.Format("【🚨 安全保護跳脫】B載台檢測到內部硬體故障 (ru.43={0}: {1})，對側已連鎖緊急停機！", val.Value, DecodeKebFaultCode(val.Value)));
                                    }
                                }
                            }
                            else if (item.IsHex)
                            {
                                gridUpdates.Add(Tuple.Create(r, string.Format("0x{0:X4}", val.Value), Color.FromArgb(15, 23, 42)));
                            }
                            else
                            {
                                double scale = (item.Address == 0x0034) ? 1.0 : item.Scale;
                                double displayVal = val.Value * scale;
                                string fmt = (scale == 1.0) ? "{0:F0} {1}" : ((scale == 0.1) ? "{0:F1} {1}" : "{0:F2} {1}");
                                gridUpdates.Add(Tuple.Create(r, string.Format(fmt, displayVal, item.Unit).Trim(), Color.FromArgb(15, 23, 42)));
                            }
                        }
                    }
                }

                if (items == null || !items.Exists(it => it.Address == 0x0203))
                {
                    int? ru03_2 = KebReadParamWithDll(comIdx, baudIdx, addr, 0x0203);
                    if (ru03_2.HasValue)
                    {
                        successCount++;
                        kebFrequency2 = (Math.Abs(ru03_2.Value) >= 100000) ? (ru03_2.Value * 0.0001) : (ru03_2.Value * 0.01);
                    }
                }

                // 斷線緩衝狀態機判定 (B載台)
                if (successCount > 0)
                {
                    lastKebSuccessTime2 = DateTime.Now;
                    if (isKebReconnecting2)
                    {
                        isKebReconnecting2 = false;
                        isKebTimeoutTriggered2 = false;
                        WriteHmiLog("RECONNECT", "[OK] B載台通訊瞬斷已自愈恢復，測試保持進行！");
                    }
                }
                else
                {
                    double elapsedSec = (DateTime.Now - lastKebSuccessTime2).TotalSeconds;
                    if (elapsedSec > 1.5 && elapsedSec <= (double)disconnectBufferSeconds)
                    {
                        isKebReconnecting2 = true;
                        double remSec = Math.Max(0, (double)disconnectBufferSeconds - elapsedSec);
                        if (lblPillKeb2 != null && !lblPillKeb2.IsDisposed)
                        {
                            this.BeginInvoke((MethodInvoker)delegate {
                                lblPillKeb2.Text = string.Format(" B載台: [⚠️ 瞬斷重連中 ({0:F0}s)] ", remSec);
                                lblPillKeb2.ForeColor = Color.FromArgb(245, 158, 11);
                            });
                        }
                    }
                    else if (elapsedSec > (double)disconnectBufferSeconds && !isKebTimeoutTriggered2)
                    {
                        isKebTimeoutTriggered2 = true;
                        isHmiKebOpen2 = false;
                        if (lblPillKeb2 != null && !lblPillKeb2.IsDisposed)
                        {
                            this.BeginInvoke((MethodInvoker)delegate {
                                lblPillKeb2.Text = " B載台: [徹底斷線] ";
                                lblPillKeb2.ForeColor = Color.FromArgb(239, 68, 68);
                            });
                        }

                        // 分級保護機制
                        if (currentKebMode2 == 8 || currentKebMode2 == 10)
                        {
                            // 狀況一：加載端 (B載台) 斷線逾時 -> 停止待測端 (A載台) 轉速命令並斷電
                            if (isHmiKebOpen1)
                            {
                                KebWriteParamWithDll(GetHmiKebComIdx(1), GetHmiKebBaudIdx(1), (int)numHmiKebNode1.Value, 0x0032, 0); // STOP
                                KebWriteParamWithDll(GetHmiKebComIdx(1), GetHmiKebBaudIdx(1), (int)numHmiKebNode1.Value, 0x0034, 0); // Sy.52 = 0
                                WriteHmiLog("FAILSAFE", "【🚨 分級連鎖停機】加載端 (B載台) 斷線逾時，待測端 (A載台) 已自動安全停機斷電！");
                            }
                        }
                        else
                        {
                            // 狀況二：待測端 (B載台) 斷線逾時 -> 加載端 (A載台) 將轉矩降至 0% 後斷電
                            if (isHmiKebOpen1)
                            {
                                KebWriteParamWithDll(GetHmiKebComIdx(1), GetHmiKebBaudIdx(1), (int)numHmiKebNode1.Value, 0x0F12, 0); // cs.18 = 0 (卸載)
                                KebWriteParamWithDll(GetHmiKebComIdx(1), GetHmiKebBaudIdx(1), (int)numHmiKebNode1.Value, 0x0032, 0); // STOP
                                WriteHmiLog("FAILSAFE", "【🚨 分級連鎖停機】待測端 (B載台) 斷線逾時，加載端 (A載台) 轉矩已自動卸載歸零並停電！");
                            }
                        }
                    }
                }

                // 批次安全更新 UI
                if (this.IsHandleCreated && !this.IsDisposed)
                {
                    this.BeginInvoke((MethodInvoker)delegate {
                        if (dgvKebRu2 != null && gridUpdates.Count > 0)
                        {
                            foreach (var u in gridUpdates)
                            {
                                if (u.Item1 < dgvKebRu2.Rows.Count)
                                {
                                    dgvKebRu2.Rows[u.Item1].Cells[1].Value = u.Item2;
                                    dgvKebRu2.Rows[u.Item1].Cells[1].Style.ForeColor = u.Item3;
                                }
                            }
                        }
                    });
                }
                UpdateHmiKebModeParamsDisplay(2);
            }
            catch { }
        }

        private void KebWriteParam32(int comIndex, int baudIndex, int invAddr, int paramAddr, int dataVal, string desc, int paramSet = 1)
        {
            try
            {
                bool ok = KebWriteParamWithDll(comIndex, baudIndex, invAddr, paramAddr, dataVal, paramSet);
                if (txtHmiKebLog != null)
                {
                    txtHmiKebLog.AppendText(string.Format("[{0}] [{1}] 寫入暫存器 0x{2:X4} = {3} (回應: {4})\r\n",
                        DateTime.Now.ToLongTimeString(), desc, paramAddr, dataVal, ok ? "ACK 成功" : "NAK/逾時失敗"));
                }
                WriteHmiLog("KEB_WRITE", string.Format("[{0}] 寫入 0x{1:X4} = {2} -> {3}", desc, paramAddr, dataVal, ok ? "成功" : "失敗"));
            }
            catch (Exception ex)
            {
                if (txtHmiKebLog != null) txtHmiKebLog.AppendText(string.Format(">> [寫入異常] {0}: {1}\r\n", desc, ex.Message));
            }
        }

        private static int? KebReadParamWithDll(int comIndex, int baudIndex, int invAddr, int paramAddr, int paramSet = 1)
        {
            lock (kebLock)
            {
                try
                {
                    if (activeKebComIndex != comIndex || activeKebBaudIndex != baudIndex)
                    {
                        closechannels();
                        tProtProperty prop = new tProtProperty();
                        prop.ProtType = 1; // DIN 66019-II (prAnsi)
                        prop.Baudrate = baudIndex;
                        prop.Comport  = comIndex;
                        prop.TimeOut  = 600;
                        prop.Flag     = 0;
                        prop.Port     = 0;     // 必填：tcp 模式保留欄位
                        prop.Txtlen   = 0;     // 必填：防止 DLL 讀入垃圾記憶體
                        prop.txt      = "";    // 必填：managed 字串歸零
                        setprotproperties(ref prop);
                        setretrycnt(1); // 快速非阻塞輪詢，避免單參數逾時卡死通訊
                        activeKebComIndex = comIndex;
                        activeKebBaudIndex = baudIndex;
                    }

                    setinvprot(invAddr, 1);

                    byte[] txBuf = new byte[256];
                    byte[] rxBuf = new byte[256];
                    BitConverter.GetBytes((short)paramAddr).CopyTo(txBuf, 0);
                    txBuf[2] = (byte)paramSet;

                    int res = waitrdreq(invAddr, 0, txBuf, rxBuf);
                    int ack = BitConverter.ToInt32(rxBuf, 12);

                    if (res == 0 && ack == 0)
                    {
                        return BitConverter.ToInt32(rxBuf, 24);
                    }
                }
                catch { activeKebComIndex = -1; try { closechannels(); } catch { } }
                return null;
            }
        }

        private static bool KebWriteParamWithDll(int comIndex, int baudIndex, int invAddr, int paramAddr, int val, int paramSet = 1)
        {
            lock (kebLock)
            {
                try
                {
                    if (activeKebComIndex != comIndex || activeKebBaudIndex != baudIndex)
                    {
                        closechannels();
                        tProtProperty prop = new tProtProperty();
                        prop.ProtType = 1; // DIN 66019-II (prAnsi)
                        prop.Baudrate = baudIndex;
                        prop.Comport  = comIndex;
                        prop.TimeOut  = 600;
                        prop.Flag     = 0;
                        prop.Port     = 0;     // 必填：tcp 模式保留欄位
                        prop.Txtlen   = 0;     // 必填：防止 DLL 讀入垃圾記憶體
                        prop.txt      = "";    // 必填：managed 字串歸零
                        setprotproperties(ref prop);
                        setretrycnt(3); // 對齊 KEB_XP_Portable_Tester 參考值
                        activeKebComIndex = comIndex;
                        activeKebBaudIndex = baudIndex;
                    }

                    setinvprot(invAddr, 1);

                    byte[] txBuf = new byte[256];
                    byte[] rxBuf = new byte[256];
                    BitConverter.GetBytes((short)paramAddr).CopyTo(txBuf, 0);
                    txBuf[2] = (byte)paramSet;
                    BitConverter.GetBytes(val).CopyTo(txBuf, 4);
                    int res = waitwrreq(invAddr, 0, txBuf, rxBuf);
                    int ack = BitConverter.ToInt32(rxBuf, 12);
                    return (res == 0 && ack == 0);
                }
                catch { activeKebComIndex = -1; try { closechannels(); } catch { } }
                return false;
            }
        }
    }
}
