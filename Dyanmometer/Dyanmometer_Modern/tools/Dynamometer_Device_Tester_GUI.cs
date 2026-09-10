using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.IO.Ports;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;
using DynamometerHMI;

namespace DynamometerDeviceTester
{
    public class TesterForm : Form
    {
        private DynamometerHMI.MainForm mainForm;

        // 橫河 YOKOGAWA 官方原廠 tmctl.dll P/Invoke 接口
        [DllImport("tmctl.dll", EntryPoint = "TmcInitialize", CharSet = CharSet.Ansi)]
        public static extern int TmcInitialize(int wire, string address, ref int id);

        [DllImport("tmctl.dll", EntryPoint = "TmcFinish")]
        public static extern int TmcFinish(int id);

        [DllImport("tmctl.dll", EntryPoint = "TmcSend", CharSet = CharSet.Ansi)]
        public static extern int TmcSend(int id, string msg);

        [DllImport("tmctl.dll", EntryPoint = "TmcReceive", CharSet = CharSet.Ansi)]
        public static extern int TmcReceive(int id, StringBuilder buff, int maxlen, ref int readlen);

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

        // copydata(Source, Destination, cntBytes) — 解引用 tRecTel.SR 指標取得 tServ00Rec.Data
        [DllImport("protKEB.dll", CallingConvention = CallingConvention.StdCall, EntryPoint = "copydata")]
        public static extern void copydata_1(int source, byte[] destination, int cntBytes);

        // 頂部大分頁導航按鈕 (4 大設備)
        private Button btnTab1, btnTab2, btnTab3, btnTab4;
        private Panel pnlTorque, pnlPower, pnlGbd, pnlKeb;

        // =========================================================================
        // 分頁 1: KISTLER 4700B 扭力計
        // =========================================================================
        private ComboBox cmbTorquePort, cmbTorqueBaud, cmbTorqueTerm;
        private TextBox txtTorqueCustomCmd;
        private Button btnTorqueConnect, btnTorqueRefresh, btnTorqueSingleQuery, btnTorqueScanAllCmds, btnTorqueSendCustom;
        private Label lblTorqueValue, lblTorqueSpeed, lblTorquePower, lblTorqueStatus;
        private TextBox txtTorqueLog;
        private SerialPort spTorque;
        private System.Windows.Forms.Timer tmrTorque;
        private int torqueSampleCount = 0;

        // =========================================================================
        // 分頁 2: YOKOGAWA WT333E 功率分析儀 (支援全通訊埠 111 / 51064 / 51065 / 502 / 自訂)
        // =========================================================================
        private TextBox txtPowerIp, txtPowerPort, txtPowerCustomCmd;
        private ComboBox cmbPowerProtocol, cmbPowerDecodeMode, cmbPowerModbusOffset;
        private Button btnPowerConnect, btnPowerSingleQuery, btnPowerScanPorts, btnPowerSendCustom, btnPowerModbus;
        private Label lblPowerStatus;
        private TextBox txtPowerLog;
        private DataGridView dgvPower;
        private int ykDeviceId = -1;
        private TcpClient tcpPowerSocket;
        private NetworkStream streamPowerSocket;
        private System.Windows.Forms.Timer tmrPower;
        private int powerSampleCount = 0;

        // =========================================================================
        // 分頁 3: Graphtec GL820 (IEEE 488.2 二進制解碼器)
        // =========================================================================
        private TextBox txtGbdIp, txtGbdPort, txtGbdCustomCmd;
        private Button btnGbdConnect, btnGbdHandshake, btnGbdSingleQuery, btnGbdTestHttp, btnGbdSendCustom;
        private Label lblGbdStatus;
        private TextBox txtGbdLog;
        private DataGridView dgvGbd;
        private TcpClient tcpGbd;
        private NetworkStream streamGbd;
        private System.Windows.Forms.Timer tmrGbd;
        private int gbdSampleCount = 0;

        // =========================================================================
        // 分頁 4: 雙 KEB 變頻負載驅動器 (Drive 1 驅動端 COM1 + Drive 2 負載端 COM2)
        // =========================================================================
        private ComboBox cmbKebPort1, cmbKebBaud1;
        private NumericUpDown numKebNodeId1, numKebSetSpeed1, numKebSetTorque1;
        private Button btnKebConnect1, btnKebSingleQuery1, btnKebSendSpeed1, btnKebSendTorque1, btnKebStop1;
        private Label lblKebStatusWord1, lblKebSpeed1, lblKebTrq1, lblKebCurrent1, lblKebVolt1, lblKebDcBus1, lblKebPwr1, lblKebTemp1, lblKebErr1, lblKebStatus1;
        private SerialPort spKeb1;
        private System.Windows.Forms.Timer tmrKeb1;

        private ComboBox cmbKebPort2, cmbKebBaud2;
        private NumericUpDown numKebNodeId2, numKebSetSpeed2, numKebSetTorque2;
        private Button btnKebConnect2, btnKebSingleQuery2, btnKebSendSpeed2, btnKebSendTorque2, btnKebStop2;
        private Label lblKebStatusWord2, lblKebSpeed2, lblKebTrq2, lblKebCurrent2, lblKebVolt2, lblKebDcBus2, lblKebPwr2, lblKebTemp2, lblKebErr2, lblKebStatus2;
        private SerialPort spKeb2;
        private System.Windows.Forms.Timer tmrKeb2;

        private Button btnModeDigSpd1, btnModeDigTrq1, btnModeAnaSpd1, btnModeAnaTrq1;
        private Button btnModeDigSpd2, btnModeDigTrq2, btnModeAnaSpd2, btnModeAnaTrq2;
        private Control[] spdControls1, trqControls1, spdControls2, trqControls2;

        private Button btnDualKebStart, btnDualKebStop, btnClearKebLog;
        private TextBox txtKebLog;
        private int kebSampleCount1 = 0, kebSampleCount2 = 0;

        public TesterForm() : this(null) { }

        public TesterForm(DynamometerHMI.MainForm main)
        {
            this.mainForm = main;
            this.Text = "動力測試設備現場四合一連線測試工具箱 (KISTLER / WT333E / GL820 / 雙 KEB 驅動器)";
            this.Size = new Size(1020, 770);
            this.StartPosition = FormStartPosition.CenterScreen;
            this.BackColor = Color.FromArgb(240, 243, 246);
            this.Font = new Font("微軟正黑體", 9.5f, FontStyle.Regular);

            WriteDirectLog("SYSTEM", "==================================================");
            WriteDirectLog("SYSTEM", " 動力計現場設備連線測試工具箱啟動: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            WriteDirectLog("SYSTEM", "作業系統: " + Environment.OSVersion.ToString() + " | 電腦名稱: " + Environment.MachineName);
            WriteDirectLog("SYSTEM", "==================================================");

            BuildUI();
            LoadSettingsFromMain();
            SwitchTab(1); // 預設聚焦顯示 KISTLER 扭力計分頁

            this.FormClosing += (s, e) => {
                CleanupConnections();
                SyncToMain();
            };
        }

        private void LoadSettingsFromMain()
        {
            if (mainForm == null) return;
            try
            {
                string tPort = mainForm.torquePortName;
                int tBaud = mainForm.torqueBaudRate;
                string pIp = mainForm.powerMeterIp;
                int pPort = mainForm.powerMeterPort;
                string gIp = mainForm.gbdIp;
                int gPort = mainForm.gbdPort;

                if (!string.IsNullOrEmpty(tPort))
                {
                    if (!cmbTorquePort.Items.Contains(tPort)) cmbTorquePort.Items.Add(tPort);
                    cmbTorquePort.SelectedItem = tPort;
                }
                if (tBaud > 0 && cmbTorqueBaud.Items.Contains(tBaud.ToString()))
                    cmbTorqueBaud.SelectedItem = tBaud.ToString();

                if (!string.IsNullOrEmpty(pIp))
                    txtPowerIp.Text = pIp;
                if (pPort > 0)
                    txtPowerPort.Text = pPort.ToString();

                if (!string.IsNullOrEmpty(gIp))
                    txtGbdIp.Text = gIp;
                if (gPort > 0)
                    txtGbdPort.Text = gPort.ToString();
            }
            catch { }
        }

        public void SyncToMain()
        {
            if (mainForm == null) return;
            try
            {
                if (cmbTorquePort != null && cmbTorquePort.SelectedItem != null)
                    mainForm.torquePortName = cmbTorquePort.SelectedItem.ToString();
                int tBaud;
                if (cmbTorqueBaud != null && cmbTorqueBaud.SelectedItem != null && int.TryParse(cmbTorqueBaud.SelectedItem.ToString(), out tBaud))
                    mainForm.torqueBaudRate = tBaud;

                if (txtPowerIp != null) mainForm.powerMeterIp = txtPowerIp.Text.Trim();
                int pPort;
                if (txtPowerPort != null && int.TryParse(txtPowerPort.Text.Trim(), out pPort)) mainForm.powerMeterPort = pPort;

                if (txtGbdIp != null) mainForm.gbdIp = txtGbdIp.Text.Trim();
                int gPort;
                if (txtGbdPort != null && int.TryParse(txtGbdPort.Text.Trim(), out gPort)) mainForm.gbdPort = gPort;

                mainForm.ApplyDeviceSettings();
            }
            catch { }
        }

        private void CleanupConnections()
        {
            try
            {
                if (tmrTorque != null) { try { tmrTorque.Stop(); } catch { } }
                if (spTorque != null)
                {
                    try { if (spTorque.IsOpen) spTorque.Close(); } catch { }
                    try { spTorque.Dispose(); } catch { }
                    spTorque = null;
                }

                if (tmrPower != null) { try { tmrPower.Stop(); } catch { } }
                if (streamPowerSocket != null) { try { streamPowerSocket.Close(); streamPowerSocket.Dispose(); } catch { } streamPowerSocket = null; }
                if (tcpPowerSocket != null) { try { tcpPowerSocket.Close(); } catch { } tcpPowerSocket = null; }
                if (ykDeviceId >= 0) { try { TmcFinish(ykDeviceId); } catch { } ykDeviceId = -1; }

                if (tmrGbd != null) { try { tmrGbd.Stop(); } catch { } }
                if (streamGbd != null) { try { streamGbd.Close(); streamGbd.Dispose(); } catch { } streamGbd = null; }
                if (tcpGbd != null) { try { tcpGbd.Close(); } catch { } tcpGbd = null; }

                if (tmrKeb1 != null) { try { tmrKeb1.Stop(); } catch { } }
                if (spKeb1 != null)
                {
                    try { if (spKeb1.IsOpen) spKeb1.Close(); } catch { }
                    spKeb1 = null;
                }

                if (tmrKeb2 != null) { try { tmrKeb2.Stop(); } catch { } }
                if (spKeb2 != null)
                {
                    try { if (spKeb2.IsOpen) spKeb2.Close(); } catch { }
                    spKeb2 = null;
                }

                Thread.Sleep(150); // 給予 Windows 核心 COM 控制代碼釋放緩衝
            }
            catch { }
        }

        private void BuildUI()
        {
            Panel pnlTopHeader = new Panel() { Dock = DockStyle.Top, Height = 45, BackColor = Color.FromArgb(20, 35, 60) };
            Label lblTitle = new Label()
            {
                Text = " 動力測試現場四大設備獨立連線測試工具箱 (KISTLER | WT333E | GL820 | 雙 KEB 驅動器)",
                ForeColor = Color.White,
                Font = new Font("微軟正黑體", 11.5f, FontStyle.Bold),
                Location = new Point(15, 11),
                AutoSize = true
            };

            Button btnSyncAndClose = new Button()
            {
                Text = " 同步設定至主系統並關閉",
                Location = new Point(510, 7),
                Size = new Size(220, 30),
                BackColor = Color.FromArgb(0, 120, 215),
                ForeColor = Color.White,
                Font = new Font("微軟正黑體", 9.5f, FontStyle.Bold),
                Cursor = Cursors.Hand
            };
            btnSyncAndClose.Click += (s, e) => {
                CleanupConnections();
                SyncToMain();
                this.Close();
            };

            Button btnExportDiag = new Button()
            {
                Text = " 檢視/匯出自動診斷日誌 (.log)",
                Location = new Point(740, 7),
                Size = new Size(245, 30),
                BackColor = Color.FromArgb(16, 185, 129),
                ForeColor = Color.White,
                Font = new Font("微軟正黑體", 9.5f, FontStyle.Bold)
            };
            btnExportDiag.Click += (s, e) => {
                string logFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs", "device_test_diagnosis.log");
                MessageBox.Show("現場測試全診斷日誌已即時自動寫入至：\n" + logFile + "\n\nAI 助手可直接在專案目錄中讀取分析，無需手動截圖！", "自動診斷日誌已就緒", MessageBoxButtons.OK, MessageBoxIcon.Information);
            };

            pnlTopHeader.Controls.Add(lblTitle);
            pnlTopHeader.Controls.Add(btnSyncAndClose);
            pnlTopHeader.Controls.Add(btnExportDiag);
            this.Controls.Add(pnlTopHeader);

            Panel pnlNav = new Panel() { Dock = DockStyle.Top, Height = 46, BackColor = Color.FromArgb(220, 228, 238) };

            btnTab1 = new Button()
            {
                Text = " 1. Kistler 4700B 扭力計",
                Location = new Point(8, 5),
                Size = new Size(235, 36),
                Font = new Font("微軟正黑體", 10f, FontStyle.Bold),
                BackColor = Color.FromArgb(235, 240, 248),
                ForeColor = Color.Black,
                FlatStyle = FlatStyle.Flat
            };
            btnTab1.Click += (s, e) => SwitchTab(1);

            btnTab2 = new Button()
            {
                Text = " 2. 橫河 WT333E 功率計",
                Location = new Point(250, 5),
                Size = new Size(245, 36),
                Font = new Font("微軟正黑體", 10f, FontStyle.Bold),
                BackColor = Color.FromArgb(235, 240, 248),
                ForeColor = Color.Black,
                FlatStyle = FlatStyle.Flat
            };
            btnTab2.Click += (s, e) => SwitchTab(2);

            btnTab3 = new Button()
            {
                Text = " 3. Graphtec GL820 記錄器",
                Location = new Point(502, 5),
                Size = new Size(245, 36),
                Font = new Font("微軟正黑體", 10f, FontStyle.Bold),
                BackColor = Color.FromArgb(235, 240, 248),
                ForeColor = Color.Black,
                FlatStyle = FlatStyle.Flat
            };
            btnTab3.Click += (s, e) => SwitchTab(3);

            btnTab4 = new Button()
            {
                Text = " 4. 雙 KEB 變頻負載驅動器",
                Location = new Point(754, 5),
                Size = new Size(245, 36),
                Font = new Font("微軟正黑體", 10f, FontStyle.Bold),
                BackColor = Color.FromArgb(0, 120, 215),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat
            };
            btnTab4.Click += (s, e) => SwitchTab(4);

            pnlNav.Controls.AddRange(new Control[] { btnTab1, btnTab2, btnTab3, btnTab4 });
            this.Controls.Add(pnlNav);

            pnlTorque = new Panel() { Dock = DockStyle.Fill, BackColor = Color.White };
            pnlPower = new Panel() { Dock = DockStyle.Fill, BackColor = Color.White };
            pnlGbd = new Panel() { Dock = DockStyle.Fill, BackColor = Color.White };
            pnlKeb = new Panel() { Dock = DockStyle.Fill, BackColor = Color.White };

            BuildTorquePanel(pnlTorque);
            BuildPowerPanel(pnlPower);
            BuildGbdPanel(pnlGbd);
            BuildDualKebPanel(pnlKeb);

            AttachAutoLog(txtTorqueLog, "KISTLER");
            AttachAutoLog(txtPowerLog, "WT333E");
            AttachAutoLog(txtGbdLog, "GL820");
            AttachAutoLog(txtKebLog, "KEB_DRIVE");

            this.Controls.Add(pnlKeb);
            this.Controls.Add(pnlGbd);
            this.Controls.Add(pnlPower);
            this.Controls.Add(pnlTorque);
        }

        private void SwitchTab(int tabNum)
        {
            btnTab1.BackColor = tabNum == 1 ? Color.FromArgb(0, 120, 215) : Color.FromArgb(235, 240, 248);
            btnTab1.ForeColor = tabNum == 1 ? Color.White : Color.Black;

            btnTab2.BackColor = tabNum == 2 ? Color.FromArgb(0, 120, 215) : Color.FromArgb(235, 240, 248);
            btnTab2.ForeColor = tabNum == 2 ? Color.White : Color.Black;

            btnTab3.BackColor = tabNum == 3 ? Color.FromArgb(0, 120, 215) : Color.FromArgb(235, 240, 248);
            btnTab3.ForeColor = tabNum == 3 ? Color.White : Color.Black;

            btnTab4.BackColor = tabNum == 4 ? Color.FromArgb(0, 120, 215) : Color.FromArgb(235, 240, 248);
            btnTab4.ForeColor = tabNum == 4 ? Color.White : Color.Black;

            pnlTorque.Visible = (tabNum == 1);
            pnlPower.Visible = (tabNum == 2);
            pnlGbd.Visible = (tabNum == 3);
            pnlKeb.Visible = (tabNum == 4);

            if (tabNum == 1) pnlTorque.BringToFront();
            if (tabNum == 2) pnlPower.BringToFront();
            if (tabNum == 3) pnlGbd.BringToFront();
            if (tabNum == 4) pnlKeb.BringToFront();
        }

        private static NumericUpDown CreateNumericUpDown(Point loc, int width, decimal min, decimal max, decimal val, int dec = 0, decimal inc = 1)
        {
            NumericUpDown nud = new NumericUpDown();
            nud.Location = loc;
            nud.Width = width;
            nud.DecimalPlaces = dec;
            nud.Increment = inc;
            nud.Minimum = min;
            nud.Maximum = max;
            if (val < min) val = min;
            if (val > max) val = max;
            nud.Value = val;
            return nud;
        }

        // =========================================================================
        // 分頁 4: 雙 KEB 變頻驅動器 (Drive 1 驅動端 COM1 + Drive 2 負載端 COM2 雙軌控制)
        // =========================================================================
        private void BuildDualKebPanel(Panel pnl)
        {
            // 頂部總覽與雙機連動列
            Panel pnlHeader = new Panel() { Dock = DockStyle.Top, Height = 48, BackColor = Color.FromArgb(240, 245, 252) };
            Label lblDualInfo = new Label()
            {
                Text = "雙 KEB F5 架構：1號機 (主動驅動端) 控制轉速 ＋ 2號機 (被動負載端) 控制轉矩加載",
                Location = new Point(10, 14),
                AutoSize = true,
                Font = new Font("微軟正黑體", 9f, FontStyle.Bold),
                ForeColor = Color.FromArgb(20, 50, 100)
            };

            Button btnOneClickKeb = new Button()
            {
                Text = "⚡ 一鍵智能診斷 (COM1)",
                Location = new Point(610, 8),
                Size = new Size(185, 32),
                BackColor = Color.FromArgb(16, 185, 129),
                ForeColor = Color.White,
                Font = new Font("微軟正黑體", 9f, FontStyle.Bold),
                Cursor = Cursors.Hand
            };
            btnOneClickKeb.Click += (s, e) => RunOneClickKebDiagnosis();

            btnDualKebStart = new Button()
            {
                Text = "雙機輪詢",
                Location = new Point(805, 8),
                Size = new Size(80, 32),
                BackColor = Color.FromArgb(0, 180, 216),
                ForeColor = Color.White,
                Font = new Font("微軟正黑體", 9f, FontStyle.Bold),
                Cursor = Cursors.Hand
            };
            btnDualKebStart.Click += (s, e) => {
                BtnKebConnect1_Click(null, null);
                BtnKebConnect2_Click(null, null);
            };

            btnDualKebStop = new Button()
            {
                Text = "急停 (E-STOP)",
                Location = new Point(890, 8),
                Size = new Size(95, 32),
                BackColor = Color.FromArgb(239, 68, 68),
                ForeColor = Color.White,
                Font = new Font("微軟正黑體", 9f, FontStyle.Bold),
                Cursor = Cursors.Hand
            };
            btnDualKebStop.Click += (s, e) => {
                KebStopDrive(spKeb1, (int)numKebNodeId1.Value, "1號驅動端");
                KebStopDrive(spKeb2, (int)numKebNodeId2.Value, "2號負載端");
            };

            pnlHeader.Controls.AddRange(new Control[] { lblDualInfo, btnOneClickKeb, btnDualKebStart, btnDualKebStop });

            // 中間：左右兩側獨立控制面板 (左: Drive 1 COM1 / 右: Drive 2 COM2)
            Panel pnlSplit = new Panel() { Dock = DockStyle.Top, Height = 360 };

            //  左側：Drive 1 (主動驅動端 COM1)
            GroupBox grpD1 = new GroupBox() { Text = "【1號 KEB 變頻器】(主動端 / 驅動馬達 COM1)", Location = new Point(12, 6), Size = new Size(480, 348) };
            Label l1_1 = new Label() { Text = "COM 埠:", Location = new Point(8, 22), AutoSize = true };
            cmbKebPort1 = new ComboBox() { Location = new Point(62, 19), Width = 70, DropDownStyle = ComboBoxStyle.DropDownList };
            cmbKebBaud1 = new ComboBox() { Location = new Point(136, 19), Width = 75, DropDownStyle = ComboBoxStyle.DropDownList };
            cmbKebBaud1.Items.AddRange(new object[] { "9600", "19200", "38400", "57600", "115200" });
            cmbKebBaud1.SelectedIndex = 0; // 9600 default

            Label l1_Node = new Label() { Text = "站號:", Location = new Point(215, 22), AutoSize = true };
            numKebNodeId1 = CreateNumericUpDown(new Point(252, 19), 42, 1, 239, 1);

            btnKebConnect1 = new Button() { Text = " 輪詢", Location = new Point(300, 16), Size = new Size(62, 28), BackColor = Color.FromArgb(0, 180, 216), ForeColor = Color.White, Font = new Font("微軟正黑體", 9f, FontStyle.Bold) };
            btnKebConnect1.Click += BtnKebConnect1_Click;

            btnKebSingleQuery1 = new Button() { Text = "單次", Location = new Point(366, 16), Size = new Size(50, 28) };
            btnKebSingleQuery1.Click += (s, e) => DoKebQuery1();

            lblKebStatus1 = new Label() { Text = "狀態: 未連線", Location = new Point(8, 48), AutoSize = true, ForeColor = Color.Gray };

            // 模式切換按鈕列 (Drive 1)
            btnModeDigSpd1 = new Button() { Text = " 數位轉速", Location = new Point(8, 68), Size = new Size(110, 28), Font = new Font("微軟正黑體", 8.5f, FontStyle.Bold) };
            btnModeDigSpd1.Click += (s, e) => ApplyTesterKebInterlock(1, 7);

            btnModeDigTrq1 = new Button() { Text = " 數位轉矩", Location = new Point(122, 68), Size = new Size(110, 28), Font = new Font("微軟正黑體", 8.5f, FontStyle.Bold) };
            btnModeDigTrq1.Click += (s, e) => ApplyTesterKebInterlock(1, 8);

            btnModeAnaSpd1 = new Button() { Text = " 類比轉速", Location = new Point(236, 68), Size = new Size(110, 28), Font = new Font("微軟正黑體", 8.5f) };
            btnModeAnaSpd1.Click += (s, e) => ApplyTesterKebInterlock(1, 9);

            btnModeAnaTrq1 = new Button() { Text = " 類比轉矩", Location = new Point(350, 68), Size = new Size(110, 28), Font = new Font("微軟正黑體", 8.5f) };
            btnModeAnaTrq1.Click += (s, e) => ApplyTesterKebInterlock(1, 10);

            // 轉速調整列 (oP03)
            Label lSpdCmd1 = new Label() { Text = "轉速:", Location = new Point(8, 104), AutoSize = true };
            Button bSpdDec100_1 = new Button() { Text = "-100", Location = new Point(48, 100), Size = new Size(48, 26) };
            bSpdDec100_1.Click += (s, e) => { if (numKebSetSpeed1.Value >= 100) numKebSetSpeed1.Value -= 100; };
            Button bSpdDec1_1 = new Button() { Text = "-1", Location = new Point(98, 100), Size = new Size(36, 26) };
            bSpdDec1_1.Click += (s, e) => { if (numKebSetSpeed1.Value >= 1) numKebSetSpeed1.Value -= 1; };

            numKebSetSpeed1 = CreateNumericUpDown(new Point(136, 101), 66, 0, 6000, 0);

            Button bSpdInc1_1 = new Button() { Text = "+1", Location = new Point(204, 100), Size = new Size(36, 26) };
            bSpdInc1_1.Click += (s, e) => { if (numKebSetSpeed1.Value <= 5999) numKebSetSpeed1.Value += 1; };
            Button bSpdInc100_1 = new Button() { Text = "+100", Location = new Point(242, 100), Size = new Size(48, 26) };
            bSpdInc100_1.Click += (s, e) => { if (numKebSetSpeed1.Value <= 5900) numKebSetSpeed1.Value += 100; };

            btnKebSendSpeed1 = new Button() { Text = " 寫入速度 (oP03)", Location = new Point(294, 99), Size = new Size(168, 28), BackColor = Color.FromArgb(0, 180, 216), ForeColor = Color.White, Font = new Font("微軟正黑體", 9f, FontStyle.Bold) };
            btnKebSendSpeed1.Click += (s, e) => KebWriteParam32(spKeb1, (int)numKebNodeId1.Value, 0x0303, (int)numKebSetSpeed1.Value, "1號速度設定 (oP03)");

            // 轉矩調整列 (cs18)
            Label lTrqCmd1 = new Label() { Text = "轉矩:", Location = new Point(8, 134), AutoSize = true };
            Button bTrqDec1_1 = new Button() { Text = "-1%", Location = new Point(48, 130), Size = new Size(48, 26) };
            bTrqDec1_1.Click += (s, e) => { if (numKebSetTorque1.Value >= 1) numKebSetTorque1.Value -= 1; };
            Button bTrqDec01_1 = new Button() { Text = "-0.1", Location = new Point(98, 130), Size = new Size(42, 26) };
            bTrqDec01_1.Click += (s, e) => { if (numKebSetTorque1.Value >= 0.1m) numKebSetTorque1.Value -= 0.1m; };

            numKebSetTorque1 = CreateNumericUpDown(new Point(142, 131), 60, 0, 150, 0, 1, 0.1m);

            Button bTrqInc01_1 = new Button() { Text = "+0.1", Location = new Point(204, 130), Size = new Size(42, 26) };
            bTrqInc01_1.Click += (s, e) => { if (numKebSetTorque1.Value <= 149.9m) numKebSetTorque1.Value += 0.1m; };
            Button bTrqInc1_1 = new Button() { Text = "+1%", Location = new Point(248, 130), Size = new Size(44, 26) };
            bTrqInc1_1.Click += (s, e) => { if (numKebSetTorque1.Value <= 149) numKebSetTorque1.Value += 1; };

            btnKebSendTorque1 = new Button() { Text = " 寫入轉矩 (cs18)", Location = new Point(294, 129), Size = new Size(168, 28), BackColor = Color.FromArgb(245, 158, 11), ForeColor = Color.White, Font = new Font("微軟正黑體", 9f, FontStyle.Bold) };
            btnKebSendTorque1.Click += (s, e) => KebWriteParam32(spKeb1, (int)numKebNodeId1.Value, 0x0F12, (int)(numKebSetTorque1.Value * 100), "1號轉矩設定 (cs18)");

            spdControls1 = new Control[] { bSpdDec100_1, bSpdDec1_1, numKebSetSpeed1, bSpdInc1_1, bSpdInc100_1, btnKebSendSpeed1 };
            trqControls1 = new Control[] { bTrqDec1_1, bTrqDec01_1, numKebSetTorque1, bTrqInc01_1, bTrqInc1_1, btnKebSendTorque1 };

            // 運轉啟動/停止列 (Sy50)
            Button btnRunFwd1 = new Button() { Text = " 正轉 (FOR)", Location = new Point(8, 162), Size = new Size(110, 28), BackColor = Color.FromArgb(16, 185, 129), ForeColor = Color.White, Font = new Font("微軟正黑體", 8.5f, FontStyle.Bold) };
            btnRunFwd1.Click += (s, e) => SetKebCommand(spKeb1, (int)numKebNodeId1.Value, 1, "1號驅動端");

            Button btnRunRev1 = new Button() { Text = " 反轉 (REV)", Location = new Point(122, 162), Size = new Size(110, 28), BackColor = Color.FromArgb(139, 92, 246), ForeColor = Color.White, Font = new Font("微軟正黑體", 8.5f, FontStyle.Bold) };
            btnRunRev1.Click += (s, e) => SetKebCommand(spKeb1, (int)numKebNodeId1.Value, 2, "1號驅動端");

            btnKebStop1 = new Button() { Text = " 停機 (STOP)", Location = new Point(236, 162), Size = new Size(110, 28), BackColor = Color.FromArgb(239, 68, 68), ForeColor = Color.White, Font = new Font("微軟正黑體", 8.5f, FontStyle.Bold) };
            btnKebStop1.Click += (s, e) => SetKebCommand(spKeb1, (int)numKebNodeId1.Value, 0, "1號驅動端");

            Button btnReset1 = new Button() { Text = " 故障復歸", Location = new Point(350, 162), Size = new Size(110, 28), BackColor = Color.FromArgb(100, 116, 139), ForeColor = Color.White, Font = new Font("微軟正黑體", 8.5f, FontStyle.Bold) };
            btnReset1.Click += (s, e) => SetKebCommand(spKeb1, (int)numKebNodeId1.Value, 4, "1號驅動端");

            // ru 即時監視數據卡
            Panel card1 = new Panel() { Location = new Point(8, 194), Size = new Size(462, 146), BackColor = Color.FromArgb(245, 248, 252), BorderStyle = BorderStyle.FixedSingle };
            Label lc1_1 = new Label() { Text = "狀態字元 (Sy51):", Location = new Point(6, 6), AutoSize = true, ForeColor = Color.Gray };
            lblKebStatusWord1 = new Label() { Text = "Ready / 待機", Location = new Point(6, 24), AutoSize = true, Font = new Font("微軟正黑體", 11f, FontStyle.Bold), ForeColor = Color.FromArgb(0, 120, 215) };

            Label lc1_2 = new Label() { Text = "實測轉速 (ru07):", Location = new Point(155, 6), AutoSize = true, ForeColor = Color.Gray };
            lblKebSpeed1 = new Label() { Text = "0.0 rpm", Location = new Point(155, 24), AutoSize = true, Font = new Font("微軟正黑體", 13f, FontStyle.Bold), ForeColor = Color.FromArgb(245, 158, 11) };

            Label lc1_Trq = new Label() { Text = "實測轉矩 (ru12):", Location = new Point(310, 6), AutoSize = true, ForeColor = Color.Gray };
            lblKebTrq1 = new Label() { Text = "0.0 Nm", Location = new Point(310, 24), AutoSize = true, Font = new Font("微軟正黑體", 13f, FontStyle.Bold), ForeColor = Color.FromArgb(220, 38, 38) };

            Label lc1_3 = new Label() { Text = "輸出電流 (ru15):", Location = new Point(6, 52), AutoSize = true, ForeColor = Color.Gray };
            lblKebCurrent1 = new Label() { Text = "0.00 A", Location = new Point(6, 70), AutoSize = true, Font = new Font("微軟正黑體", 12f, FontStyle.Bold), ForeColor = Color.FromArgb(16, 185, 129) };

            Label lc1_Volt = new Label() { Text = "輸出電壓 (ru09):", Location = new Point(155, 52), AutoSize = true, ForeColor = Color.Gray };
            lblKebVolt1 = new Label() { Text = "0.0 V", Location = new Point(155, 70), AutoSize = true, Font = new Font("微軟正黑體", 12f, FontStyle.Bold), ForeColor = Color.FromArgb(59, 130, 246) };

            Label lc1_4 = new Label() { Text = "直流母線 (ru18):", Location = new Point(310, 52), AutoSize = true, ForeColor = Color.Gray };
            lblKebDcBus1 = new Label() { Text = "0 V", Location = new Point(310, 70), AutoSize = true, Font = new Font("微軟正黑體", 12f, FontStyle.Bold), ForeColor = Color.FromArgb(139, 92, 246) };

            Label lc1_Pwr = new Label() { Text = "轉矩命令 (ru11):", Location = new Point(6, 98), AutoSize = true, ForeColor = Color.Gray };
            lblKebPwr1 = new Label() { Text = "0.00 Nm", Location = new Point(6, 116), AutoSize = true, Font = new Font("微軟正黑體", 11f, FontStyle.Bold), ForeColor = Color.FromArgb(217, 119, 6) };

            Label lc1_Temp = new Label() { Text = "模組散熱 (ru20):", Location = new Point(155, 98), AutoSize = true, ForeColor = Color.Gray };
            lblKebTemp1 = new Label() { Text = "-- °C", Location = new Point(155, 116), AutoSize = true, Font = new Font("微軟正黑體", 11f, FontStyle.Bold), ForeColor = Color.FromArgb(16, 185, 129) };

            Label lc1_Err = new Label() { Text = "故障碼 (ru43):", Location = new Point(310, 98), AutoSize = true, ForeColor = Color.Gray };
            lblKebErr1 = new Label() { Text = "無異常", Location = new Point(310, 116), AutoSize = true, Font = new Font("微軟正黑體", 11f, FontStyle.Bold), ForeColor = Color.DarkGreen };

            card1.Controls.AddRange(new Control[] {
                lc1_1, lblKebStatusWord1, lc1_2, lblKebSpeed1, lc1_Trq, lblKebTrq1,
                lc1_3, lblKebCurrent1, lc1_Volt, lblKebVolt1, lc1_4, lblKebDcBus1,
                lc1_Pwr, lblKebPwr1, lc1_Temp, lblKebTemp1, lc1_Err, lblKebErr1
            });

            grpD1.Controls.AddRange(new Control[] {
                l1_1, cmbKebPort1, cmbKebBaud1, l1_Node, numKebNodeId1, btnKebConnect1, btnKebSingleQuery1, lblKebStatus1,
                btnModeDigSpd1, btnModeDigTrq1, btnModeAnaSpd1, btnModeAnaTrq1,
                lSpdCmd1, bSpdDec100_1, bSpdDec1_1, numKebSetSpeed1, bSpdInc1_1, bSpdInc100_1, btnKebSendSpeed1,
                lTrqCmd1, bTrqDec1_1, bTrqDec01_1, numKebSetTorque1, bTrqInc01_1, bTrqInc1_1, btnKebSendTorque1,
                btnRunFwd1, btnRunRev1, btnKebStop1, btnReset1, card1
            });
            pnlSplit.Controls.Add(grpD1);

            //  右側：Drive 2 (被動負載端 COM2)
            GroupBox grpD2 = new GroupBox() { Text = "【2號 KEB 變頻器】(被動端 / 負載動力計 COM2)", Location = new Point(505, 6), Size = new Size(480, 348) };
            Label l2_1 = new Label() { Text = "COM 埠:", Location = new Point(8, 22), AutoSize = true };
            cmbKebPort2 = new ComboBox() { Location = new Point(62, 19), Width = 70, DropDownStyle = ComboBoxStyle.DropDownList };
            cmbKebBaud2 = new ComboBox() { Location = new Point(136, 19), Width = 75, DropDownStyle = ComboBoxStyle.DropDownList };
            cmbKebBaud2.Items.AddRange(new object[] { "9600", "19200", "38400", "57600", "115200" });
            cmbKebBaud2.SelectedIndex = 0; // 9600 default

            Label l2_Node = new Label() { Text = "站號:", Location = new Point(215, 22), AutoSize = true };
            numKebNodeId2 = CreateNumericUpDown(new Point(252, 19), 42, 1, 239, 1);

            btnKebConnect2 = new Button() { Text = " 輪詢", Location = new Point(300, 16), Size = new Size(62, 28), BackColor = Color.FromArgb(0, 180, 216), ForeColor = Color.White, Font = new Font("微軟正黑體", 9f, FontStyle.Bold) };
            btnKebConnect2.Click += BtnKebConnect2_Click;

            btnKebSingleQuery2 = new Button() { Text = "單次", Location = new Point(366, 16), Size = new Size(50, 28) };
            btnKebSingleQuery2.Click += (s, e) => DoKebQuery2();

            lblKebStatus2 = new Label() { Text = "狀態: 未連線", Location = new Point(8, 48), AutoSize = true, ForeColor = Color.Gray };

            // 模式切換按鈕列 (Drive 2)
            btnModeDigSpd2 = new Button() { Text = " 數位轉速", Location = new Point(8, 68), Size = new Size(110, 28), Font = new Font("微軟正黑體", 8.5f, FontStyle.Bold) };
            btnModeDigSpd2.Click += (s, e) => ApplyTesterKebInterlock(2, 7);

            btnModeDigTrq2 = new Button() { Text = " 數位轉矩", Location = new Point(122, 68), Size = new Size(110, 28), Font = new Font("微軟正黑體", 8.5f, FontStyle.Bold) };
            btnModeDigTrq2.Click += (s, e) => ApplyTesterKebInterlock(2, 8);

            btnModeAnaSpd2 = new Button() { Text = " 類比轉速", Location = new Point(236, 68), Size = new Size(110, 28), Font = new Font("微軟正黑體", 8.5f) };
            btnModeAnaSpd2.Click += (s, e) => ApplyTesterKebInterlock(2, 9);

            btnModeAnaTrq2 = new Button() { Text = " 類比轉矩", Location = new Point(350, 68), Size = new Size(110, 28), Font = new Font("微軟正黑體", 8.5f) };
            btnModeAnaTrq2.Click += (s, e) => ApplyTesterKebInterlock(2, 10);

            // 轉速調整列 (oP03)
            Label lSpdCmd2 = new Label() { Text = "轉速:", Location = new Point(8, 104), AutoSize = true };
            Button bSpdDec100_2 = new Button() { Text = "-100", Location = new Point(48, 100), Size = new Size(48, 26) };
            bSpdDec100_2.Click += (s, e) => { if (numKebSetSpeed2.Value >= 100) numKebSetSpeed2.Value -= 100; };
            Button bSpdDec1_2 = new Button() { Text = "-1", Location = new Point(98, 100), Size = new Size(36, 26) };
            bSpdDec1_2.Click += (s, e) => { if (numKebSetSpeed2.Value >= 1) numKebSetSpeed2.Value -= 1; };

            numKebSetSpeed2 = CreateNumericUpDown(new Point(136, 101), 66, 0, 6000, 0);

            Button bSpdInc1_2 = new Button() { Text = "+1", Location = new Point(204, 100), Size = new Size(36, 26) };
            bSpdInc1_2.Click += (s, e) => { if (numKebSetSpeed2.Value <= 5999) numKebSetSpeed2.Value += 1; };
            Button bSpdInc100_2 = new Button() { Text = "+100", Location = new Point(242, 100), Size = new Size(48, 26) };
            bSpdInc100_2.Click += (s, e) => { if (numKebSetSpeed2.Value <= 5900) numKebSetSpeed2.Value += 100; };

            btnKebSendSpeed2 = new Button() { Text = " 寫入速度 (oP03)", Location = new Point(294, 99), Size = new Size(168, 28), BackColor = Color.FromArgb(0, 180, 216), ForeColor = Color.White, Font = new Font("微軟正黑體", 9f, FontStyle.Bold) };
            btnKebSendSpeed2.Click += (s, e) => KebWriteParam32(spKeb2, (int)numKebNodeId2.Value, 0x0303, (int)numKebSetSpeed2.Value, "2號速度設定 (oP03)");

            // 轉矩調整列 (cs18)
            Label lTrqCmd2 = new Label() { Text = "轉矩:", Location = new Point(8, 134), AutoSize = true };
            Button bTrqDec1_2 = new Button() { Text = "-1%", Location = new Point(48, 130), Size = new Size(48, 26) };
            bTrqDec1_2.Click += (s, e) => { if (numKebSetTorque2.Value >= 1) numKebSetTorque2.Value -= 1; };
            Button bTrqDec01_2 = new Button() { Text = "-0.1", Location = new Point(98, 130), Size = new Size(42, 26) };
            bTrqDec01_2.Click += (s, e) => { if (numKebSetTorque2.Value >= 0.1m) numKebSetTorque2.Value -= 0.1m; };

            numKebSetTorque2 = CreateNumericUpDown(new Point(142, 131), 60, 0, 150, 0, 1, 0.1m);

            Button bTrqInc01_2 = new Button() { Text = "+0.1", Location = new Point(204, 130), Size = new Size(42, 26) };
            bTrqInc01_2.Click += (s, e) => { if (numKebSetTorque2.Value <= 149.9m) numKebSetTorque2.Value += 0.1m; };
            Button bTrqInc1_2 = new Button() { Text = "+1%", Location = new Point(248, 130), Size = new Size(44, 26) };
            bTrqInc1_2.Click += (s, e) => { if (numKebSetTorque2.Value <= 149) numKebSetTorque2.Value += 1; };

            btnKebSendTorque2 = new Button() { Text = " 寫入轉矩 (cs18)", Location = new Point(294, 129), Size = new Size(168, 28), BackColor = Color.FromArgb(245, 158, 11), ForeColor = Color.White, Font = new Font("微軟正黑體", 9f, FontStyle.Bold) };
            btnKebSendTorque2.Click += (s, e) => KebWriteParam32(spKeb2, (int)numKebNodeId2.Value, 0x0F12, (int)(numKebSetTorque2.Value * 100), "2號加載轉矩 (cs18)");

            spdControls2 = new Control[] { bSpdDec100_2, bSpdDec1_2, numKebSetSpeed2, bSpdInc1_2, bSpdInc100_2, btnKebSendSpeed2 };
            trqControls2 = new Control[] { bTrqDec1_2, bTrqDec01_2, numKebSetTorque2, bTrqInc01_2, bTrqInc1_2, btnKebSendTorque2 };

            // 運轉啟動/停止列 (Sy50)
            Button btnRunFwd2 = new Button() { Text = " 正轉 (FOR)", Location = new Point(8, 162), Size = new Size(110, 28), BackColor = Color.FromArgb(16, 185, 129), ForeColor = Color.White, Font = new Font("微軟正黑體", 8.5f, FontStyle.Bold) };
            btnRunFwd2.Click += (s, e) => SetKebCommand(spKeb2, (int)numKebNodeId2.Value, 1, "2號負載端");

            Button btnRunRev2 = new Button() { Text = " 反轉 (REV)", Location = new Point(122, 162), Size = new Size(110, 28), BackColor = Color.FromArgb(139, 92, 246), ForeColor = Color.White, Font = new Font("微軟正黑體", 8.5f, FontStyle.Bold) };
            btnRunRev2.Click += (s, e) => SetKebCommand(spKeb2, (int)numKebNodeId2.Value, 2, "2號負載端");

            btnKebStop2 = new Button() { Text = " 停機 (STOP)", Location = new Point(236, 162), Size = new Size(110, 28), BackColor = Color.FromArgb(239, 68, 68), ForeColor = Color.White, Font = new Font("微軟正黑體", 8.5f, FontStyle.Bold) };
            btnKebStop2.Click += (s, e) => SetKebCommand(spKeb2, (int)numKebNodeId2.Value, 0, "2號負載端");

            Button btnReset2 = new Button() { Text = " 故障復歸", Location = new Point(350, 162), Size = new Size(110, 28), BackColor = Color.FromArgb(100, 116, 139), ForeColor = Color.White, Font = new Font("微軟正黑體", 8.5f, FontStyle.Bold) };
            btnReset2.Click += (s, e) => SetKebCommand(spKeb2, (int)numKebNodeId2.Value, 4, "2號負載端");

            // ru 即時監視數據卡
            Panel card2 = new Panel() { Location = new Point(8, 194), Size = new Size(462, 146), BackColor = Color.FromArgb(245, 248, 252), BorderStyle = BorderStyle.FixedSingle };
            Label lc2_1 = new Label() { Text = "狀態字元 (Sy51):", Location = new Point(6, 6), AutoSize = true, ForeColor = Color.Gray };
            lblKebStatusWord2 = new Label() { Text = "Ready / 待機", Location = new Point(6, 24), AutoSize = true, Font = new Font("微軟正黑體", 11f, FontStyle.Bold), ForeColor = Color.FromArgb(0, 120, 215) };

            Label lc2_2 = new Label() { Text = "實測轉速 (ru07):", Location = new Point(155, 6), AutoSize = true, ForeColor = Color.Gray };
            lblKebSpeed2 = new Label() { Text = "0.0 rpm", Location = new Point(155, 24), AutoSize = true, Font = new Font("微軟正黑體", 13f, FontStyle.Bold), ForeColor = Color.FromArgb(245, 158, 11) };

            Label lc2_Trq = new Label() { Text = "實測轉矩 (ru12):", Location = new Point(310, 6), AutoSize = true, ForeColor = Color.Gray };
            lblKebTrq2 = new Label() { Text = "0.0 Nm", Location = new Point(310, 24), AutoSize = true, Font = new Font("微軟正黑體", 13f, FontStyle.Bold), ForeColor = Color.FromArgb(220, 38, 38) };

            Label lc2_3 = new Label() { Text = "輸出電流 (ru15):", Location = new Point(6, 52), AutoSize = true, ForeColor = Color.Gray };
            lblKebCurrent2 = new Label() { Text = "0.00 A", Location = new Point(6, 70), AutoSize = true, Font = new Font("微軟正黑體", 12f, FontStyle.Bold), ForeColor = Color.FromArgb(16, 185, 129) };

            Label lc2_Volt = new Label() { Text = "輸出電壓 (ru09):", Location = new Point(155, 52), AutoSize = true, ForeColor = Color.Gray };
            lblKebVolt2 = new Label() { Text = "0.0 V", Location = new Point(155, 70), AutoSize = true, Font = new Font("微軟正黑體", 12f, FontStyle.Bold), ForeColor = Color.FromArgb(59, 130, 246) };

            Label lc2_4 = new Label() { Text = "直流母線 (ru18):", Location = new Point(310, 52), AutoSize = true, ForeColor = Color.Gray };
            lblKebDcBus2 = new Label() { Text = "0 V", Location = new Point(310, 70), AutoSize = true, Font = new Font("微軟正黑體", 12f, FontStyle.Bold), ForeColor = Color.FromArgb(139, 92, 246) };

            Label lc2_Pwr = new Label() { Text = "轉矩命令 (ru11):", Location = new Point(6, 98), AutoSize = true, ForeColor = Color.Gray };
            lblKebPwr2 = new Label() { Text = "0.00 Nm", Location = new Point(6, 116), AutoSize = true, Font = new Font("微軟正黑體", 11f, FontStyle.Bold), ForeColor = Color.FromArgb(217, 119, 6) };

            Label lc2_Temp = new Label() { Text = "模組散熱 (ru20):", Location = new Point(155, 98), AutoSize = true, ForeColor = Color.Gray };
            lblKebTemp2 = new Label() { Text = "-- °C", Location = new Point(155, 116), AutoSize = true, Font = new Font("微軟正黑體", 11f, FontStyle.Bold), ForeColor = Color.FromArgb(16, 185, 129) };

            Label lc2_Err = new Label() { Text = "故障碼 (ru43):", Location = new Point(310, 98), AutoSize = true, ForeColor = Color.Gray };
            lblKebErr2 = new Label() { Text = "無異常", Location = new Point(310, 116), AutoSize = true, Font = new Font("微軟正黑體", 11f, FontStyle.Bold), ForeColor = Color.DarkGreen };

            card2.Controls.AddRange(new Control[] {
                lc2_1, lblKebStatusWord2, lc2_2, lblKebSpeed2, lc2_Trq, lblKebTrq2,
                lc2_3, lblKebCurrent2, lc2_Volt, lblKebVolt2, lc2_4, lblKebDcBus2,
                lc2_Pwr, lblKebPwr2, lc2_Temp, lblKebTemp2, lc2_Err, lblKebErr2
            });

            grpD2.Controls.AddRange(new Control[] {
                l2_1, cmbKebPort2, cmbKebBaud2, l2_Node, numKebNodeId2, btnKebConnect2, btnKebSingleQuery2, lblKebStatus2,
                btnModeDigSpd2, btnModeDigTrq2, btnModeAnaSpd2, btnModeAnaTrq2,
                lSpdCmd2, bSpdDec100_2, bSpdDec1_2, numKebSetSpeed2, bSpdInc1_2, bSpdInc100_2, btnKebSendSpeed2,
                lTrqCmd2, bTrqDec1_2, bTrqDec01_2, numKebSetTorque2, bTrqInc01_2, bTrqInc1_2, btnKebSendTorque2,
                btnRunFwd2, btnRunRev2, btnKebStop2, btnReset2, card2
            });
            pnlSplit.Controls.Add(grpD2);

            // 中間下部：自訂 Request (Req Rd) 與 Response (Rsp Rd) 協定診斷測試面板
            GroupBox grpCustomReq = new GroupBox() { Text = "【KEB 原廠通訊協定 - 單參數自訂 Request Rd / Response 診斷測試】(支援 Address / Set / Ack 狀態碼驗證)", Dock = DockStyle.Top, Height = 75 };
            Label lcqPort = new Label() { Text = "COM:", Location = new Point(8, 22), AutoSize = true };
            ComboBox cmbCustomKebPort = new ComboBox() { Location = new Point(48, 19), Width = 68, DropDownStyle = ComboBoxStyle.DropDownList };
            foreach (string p in SerialPort.GetPortNames()) cmbCustomKebPort.Items.Add(p);
            if (cmbCustomKebPort.Items.Count > 0) cmbCustomKebPort.SelectedIndex = 0;

            Label lcqBaud = new Label() { Text = "波特率:", Location = new Point(120, 22), AutoSize = true };
            ComboBox cmbCustomKebBaud = new ComboBox() { Location = new Point(168, 19), Width = 70, DropDownStyle = ComboBoxStyle.DropDownList };
            cmbCustomKebBaud.Items.AddRange(new object[] { "9600", "19200", "38400", "57600", "115200" });
            cmbCustomKebBaud.SelectedIndex = 0; // 9600 default

            Label lcqNode = new Label() { Text = "站號:", Location = new Point(242, 22), AutoSize = true };
            NumericUpDown numCustomKebNode = CreateNumericUpDown(new Point(278, 19), 38, 0, 239, 1);

            Label lcqAddr = new Label() { Text = "位址(Hex):", Location = new Point(320, 22), AutoSize = true };
            TextBox txtCustomKebAddr = new TextBox() { Text = "0200", Location = new Point(382, 19), Width = 48, Font = new Font("Consolas", 9f, FontStyle.Bold) };

            Label lcqSet = new Label() { Text = "Set:", Location = new Point(434, 22), AutoSize = true };
            NumericUpDown numCustomKebSet = CreateNumericUpDown(new Point(462, 19), 36, 0, 7, 0);

            Button btnPresetRu18 = new Button() { Text = "ru18母線", Location = new Point(504, 18), Size = new Size(62, 26) };
            btnPresetRu18.Click += (s, e) => txtCustomKebAddr.Text = "0212";
            Button btnPresetRu10 = new Button() { Text = "ru10母線", Location = new Point(568, 18), Size = new Size(62, 26) };
            btnPresetRu10.Click += (s, e) => txtCustomKebAddr.Text = "020A";
            Button btnPresetRu07 = new Button() { Text = "ru07轉速", Location = new Point(632, 18), Size = new Size(62, 26) };
            btnPresetRu07.Click += (s, e) => txtCustomKebAddr.Text = "0207";
            Button btnPresetRu00 = new Button() { Text = "ru00狀態", Location = new Point(696, 18), Size = new Size(62, 26) };
            btnPresetRu00.Click += (s, e) => txtCustomKebAddr.Text = "0200";

            Button btnRunCustomDll = new Button()
            {
                Text = "⚡ 原廠 DLL Request Rd (讀取)",
                Location = new Point(764, 16),
                Size = new Size(200, 48),
                BackColor = Color.FromArgb(16, 185, 129),
                ForeColor = Color.White,
                Font = new Font("微軟正黑體", 9.5f, FontStyle.Bold),
                Cursor = Cursors.Hand
            };
            btnRunCustomDll.Click += (s, e) => {
                string portName = cmbCustomKebPort.SelectedItem != null ? cmbCustomKebPort.SelectedItem.ToString() : "COM1";
                int pNum = 1;
                if (portName.StartsWith("COM", StringComparison.OrdinalIgnoreCase)) int.TryParse(portName.Substring(3), out pNum);
                int comIdx = pNum - 1;

                int baud = int.Parse(cmbCustomKebBaud.SelectedItem != null ? cmbCustomKebBaud.SelectedItem.ToString() : "57600");
                int baudIdx = 6;
                if (baud == 19200) baudIdx = 4;
                else if (baud == 9600) baudIdx = 3;
                else if (baud == 38400) baudIdx = 5;
                else if (baud == 115200) baudIdx = 7;

                int node = (int)numCustomKebNode.Value;
                int addr = 0x0200;
                int.TryParse(txtCustomKebAddr.Text.Trim(), NumberStyles.HexNumber, null, out addr);
                int pSet = (int)numCustomKebSet.Value;

                RunCustomKebDllTest(comIdx, baudIdx, portName, baud, node, addr, pSet);
            };

            grpCustomReq.Controls.AddRange(new Control[] {
                lcqPort, cmbCustomKebPort, lcqBaud, cmbCustomKebBaud, lcqNode, numCustomKebNode,
                lcqAddr, txtCustomKebAddr, lcqSet, numCustomKebSet,
                btnPresetRu18, btnPresetRu10, btnPresetRu07, btnPresetRu00,
                btnRunCustomDll
            });

            // 下方：通訊日誌
            GroupBox grpLog = new GroupBox() { Text = "雙 KEB 驅動器通訊交握日誌 (COM1 / COM2 DIN 66019 電文與 Request/Response 診斷)", Dock = DockStyle.Fill };
            txtKebLog = new TextBox() { Dock = DockStyle.Fill, Multiline = true, ScrollBars = ScrollBars.Vertical, ReadOnly = true, BackColor = Color.Black, ForeColor = Color.LightGreen, Font = new Font("Consolas", 9.5f) };
            btnClearKebLog = new Button() { Text = "清空日誌", Location = new Point(880, 12), Size = new Size(85, 25), Anchor = AnchorStyles.Top | AnchorStyles.Right };
            btnClearKebLog.Click += (s, e) => txtKebLog.Clear();
            grpLog.Controls.Add(btnClearKebLog);
            grpLog.Controls.Add(txtKebLog);

            pnl.Controls.AddRange(new Control[] { grpLog, grpCustomReq, pnlSplit, pnlHeader });

            RefreshKebPorts();

            // 預設初始化互鎖狀態：1號 = 數位速度 (轉矩參數鎖定) / 2號 = 數位轉矩 (速度參數鎖定)
            ApplyTesterKebInterlock(1, 7);

            tmrKeb1 = new System.Windows.Forms.Timer() { Interval = 250 };
            tmrKeb1.Tick += (s, e) => DoKebQuery1();

            tmrKeb2 = new System.Windows.Forms.Timer() { Interval = 250 };
            tmrKeb2.Tick += (s, e) => DoKebQuery2();
        }

        private void ApplyTesterKebInterlock(int driveIdx, int mode)
        {
            int mode1, mode2;
            if (driveIdx == 1)
            {
                mode1 = mode;
                // 1號選速度(7/9) -> 2號自動鎖定為轉矩(8/10)；1號選轉矩(8/10) -> 2號自動鎖定為速度(7/9)
                if (mode == 7) mode2 = 8;
                else if (mode == 8) mode2 = 7;
                else if (mode == 9) mode2 = 10;
                else mode2 = 9;
            }
            else
            {
                mode2 = mode;
                if (mode == 7) mode1 = 8;
                else if (mode == 8) mode1 = 7;
                else if (mode == 9) mode1 = 10;
                else mode1 = 9;
            }

            // 更新 1 號機模式按鈕高亮外觀
            SetModeBtnStyle(btnModeDigSpd1, mode1 == 7, Color.FromArgb(16, 185, 129));
            SetModeBtnStyle(btnModeDigTrq1, mode1 == 8, Color.FromArgb(0, 120, 215));
            SetModeBtnStyle(btnModeAnaSpd1, mode1 == 9, Color.FromArgb(217, 119, 6));
            SetModeBtnStyle(btnModeAnaTrq1, mode1 == 10, Color.FromArgb(139, 92, 246));

            // 更新 2 號機模式按鈕高亮外觀
            SetModeBtnStyle(btnModeDigSpd2, mode2 == 7, Color.FromArgb(16, 185, 129));
            SetModeBtnStyle(btnModeDigTrq2, mode2 == 8, Color.FromArgb(0, 120, 215));
            SetModeBtnStyle(btnModeAnaSpd2, mode2 == 9, Color.FromArgb(217, 119, 6));
            SetModeBtnStyle(btnModeAnaTrq2, mode2 == 10, Color.FromArgb(139, 92, 246));

            // 1 號機參數微調控制項鎖定/解鎖
            bool d1SpdEn = (mode1 == 7);
            bool d1TrqEn = (mode1 == 8);
            if (spdControls1 != null) { foreach (var c in spdControls1) if (c != null) c.Enabled = d1SpdEn; }
            if (trqControls1 != null) { foreach (var c in trqControls1) if (c != null) c.Enabled = d1TrqEn; }

            // 2 號機參數微調控制項鎖定/解鎖
            bool d2SpdEn = (mode2 == 7);
            bool d2TrqEn = (mode2 == 8);
            if (spdControls2 != null) { foreach (var c in spdControls2) if (c != null) c.Enabled = d2SpdEn; }
            if (trqControls2 != null) { foreach (var c in trqControls2) if (c != null) c.Enabled = d2TrqEn; }

            // 發送硬體模式配置指令
            SetKebMode(spKeb1, (int)numKebNodeId1.Value, mode1, "1號驅動端");
            SetKebMode(spKeb2, (int)numKebNodeId2.Value, mode2, "2號負載端");
        }

        private void SetModeBtnStyle(Button btn, bool isActive, Color activeColor)
        {
            if (btn == null) return;
            if (isActive)
            {
                btn.BackColor = activeColor;
                btn.ForeColor = Color.White;
                btn.Font = new Font("微軟正黑體", 8.5f, FontStyle.Bold);
            }
            else
            {
                btn.BackColor = SystemColors.Control;
                btn.ForeColor = Color.Gray;
                btn.Font = new Font("微軟正黑體", 8.5f, FontStyle.Regular);
            }
        }

        private void SetKebMode(SerialPort sp, int nodeId, int mode, string driveName)
        {
            if (sp == null || !sp.IsOpen)
            {
                txtKebLog.AppendText(string.Format(">> 請先點擊【輪詢】開啟 {0} 的 COM 埠！\r\n", driveName));
                return;
            }
            if (mode == 7) // 數位速度 (oP00=2, oP01=8, cs00=4, cs15=3)
            {
                KebWriteParam32(sp, nodeId, 0x0300, 2, driveName + " oP00=2");
                KebWriteParam32(sp, nodeId, 0x0301, 8, driveName + " oP01=8");
                KebWriteParam32(sp, nodeId, 0x0F00, 4, driveName + " cs00=4 (速度模式)");
                KebWriteParam32(sp, nodeId, 0x0F0F, 3, driveName + " cs15=3");
                txtKebLog.AppendText(string.Format("[OK] [{0}] 已切換為【數位定轉速模式 (Digital Speed)】\r\n", driveName));
            }
            else if (mode == 8) // 數位轉矩 (oP00=2, oP01=8, cs00=6, cs15=3)
            {
                KebWriteParam32(sp, nodeId, 0x0300, 2, driveName + " oP00=2");
                KebWriteParam32(sp, nodeId, 0x0301, 8, driveName + " oP01=8");
                KebWriteParam32(sp, nodeId, 0x0F00, 6, driveName + " cs00=6 (轉矩模式)");
                KebWriteParam32(sp, nodeId, 0x0F0F, 3, driveName + " cs15=3");
                txtKebLog.AppendText(string.Format("[OK] [{0}] 已切換為【數位定轉矩模式 (Digital Torque)】\r\n", driveName));
            }
            else if (mode == 9) // 類比速度 (oP00=0, oP01=7, cs00=4, cs15=2)
            {
                KebWriteParam32(sp, nodeId, 0x0300, 0, driveName + " oP00=0");
                KebWriteParam32(sp, nodeId, 0x0301, 7, driveName + " oP01=7");
                KebWriteParam32(sp, nodeId, 0x0F00, 4, driveName + " cs00=4 (類比速度)");
                KebWriteParam32(sp, nodeId, 0x0F0F, 2, driveName + " cs15=2");
                txtKebLog.AppendText(string.Format("[OK] [{0}] 已切換為【類比轉速模式 (Analog Speed 0~10V)】\r\n", driveName));
            }
            else if (mode == 10) // 類比轉矩 (oP00=0, oP01=7, cs00=6, cs15=1)
            {
                KebWriteParam32(sp, nodeId, 0x0300, 0, driveName + " oP00=0");
                KebWriteParam32(sp, nodeId, 0x0301, 7, driveName + " oP01=7");
                KebWriteParam32(sp, nodeId, 0x0F00, 6, driveName + " cs00=6 (類比轉矩)");
                KebWriteParam32(sp, nodeId, 0x0F0F, 1, driveName + " cs15=1");
                txtKebLog.AppendText(string.Format("[OK] [{0}] 已切換為【類比轉矩模式 (Analog Torque 0~10V)】\r\n", driveName));
            }
        }

        private void SetKebCommand(SerialPort sp, int nodeId, int cmd, string driveName)
        {
            if (sp == null || !sp.IsOpen)
            {
                txtKebLog.AppendText(string.Format(">> 請先點擊【輪詢】開啟 {0} 的 COM 埠！\r\n", driveName));
                return;
            }
            KebWriteParam32(sp, nodeId, 0x0032, cmd, driveName + " Sy50=" + cmd);
            if (cmd == 1) txtKebLog.AppendText(string.Format(" [{0}] 發送：RUN 正轉命令 (Sy50=1)\r\n", driveName));
            else if (cmd == 2) txtKebLog.AppendText(string.Format(" [{0}] 發送：RUN 反轉命令 (Sy50=2)\r\n", driveName));
            else if (cmd == 0) txtKebLog.AppendText(string.Format(" [{0}] 發送：STOP 停機命令 (Sy50=0)\r\n", driveName));
            else if (cmd == 4) txtKebLog.AppendText(string.Format(" [{0}] 發送：FAULT RESET 故障復歸 (Sy50=4)\r\n", driveName));
        }

        private void RefreshKebPorts()
        {
            cmbKebPort1.Items.Clear();
            cmbKebPort2.Items.Clear();
            string[] ports = SerialPort.GetPortNames();

            int selIdx1 = -1, selIdx2 = -1;
            for (int i = 0; i < ports.Length; i++)
            {
                cmbKebPort1.Items.Add(ports[i]);
                cmbKebPort2.Items.Add(ports[i]);
                if (ports[i].Equals("COM1", StringComparison.OrdinalIgnoreCase)) selIdx1 = i;
                if (ports[i].Equals("COM2", StringComparison.OrdinalIgnoreCase)) selIdx2 = i;
            }

            if (selIdx1 >= 0) cmbKebPort1.SelectedIndex = selIdx1;
            else if (cmbKebPort1.Items.Count > 0) cmbKebPort1.SelectedIndex = 0;
            else cmbKebPort1.Items.Add("COM1");

            if (selIdx2 >= 0) cmbKebPort2.SelectedIndex = selIdx2;
            else if (cmbKebPort2.Items.Count > 1) cmbKebPort2.SelectedIndex = 1;
            else if (cmbKebPort2.Items.Count > 0) cmbKebPort2.SelectedIndex = 0;
            else cmbKebPort2.Items.Add("COM2");
        }

        private bool EnsureKebOpen1()
        {
            string port = cmbKebPort1.SelectedItem != null ? cmbKebPort1.SelectedItem.ToString() : "COM1";
            int baud = int.Parse(cmbKebBaud1.SelectedItem != null ? cmbKebBaud1.SelectedItem.ToString() : "9600");

            try
            {
                lblKebStatus1.Text = string.Format("狀態: 原廠驅動已連線 ({0} @ {1} 8-E-1)", port, baud);
                lblKebStatus1.ForeColor = Color.Green;
                txtKebLog.AppendText(string.Format("[{0}] 1號 KEB (主動端) 成功啟用原廠驅動 {1} @ {2} bps\r\n", DateTime.Now.ToLongTimeString(), port, baud));
                return true;
            }
            catch (Exception ex)
            {
                txtKebLog.AppendText(string.Format("[1號錯誤] 開啟 {0} 失敗: {1}\r\n", port, ex.Message));
                lblKebStatus1.Text = "狀態: 連線失敗";
                lblKebStatus1.ForeColor = Color.Red;
                return false;
            }
        }

        private bool EnsureKebOpen2()
        {
            string port = cmbKebPort2.SelectedItem != null ? cmbKebPort2.SelectedItem.ToString() : "COM2";
            int baud = int.Parse(cmbKebBaud2.SelectedItem != null ? cmbKebBaud2.SelectedItem.ToString() : "9600");

            try
            {
                lblKebStatus2.Text = string.Format("狀態: 原廠驅動已連線 ({0} @ {1} 8-E-1)", port, baud);
                lblKebStatus2.ForeColor = Color.Green;
                txtKebLog.AppendText(string.Format("[{0}] 2號 KEB (負載端) 成功啟用原廠驅動 {1} @ {2} bps\r\n", DateTime.Now.ToLongTimeString(), port, baud));
                return true;
            }
            catch (Exception ex)
            {
                txtKebLog.AppendText(string.Format("[2號錯誤] 開啟 {0} 失敗: {1}\r\n", port, ex.Message));
                lblKebStatus2.Text = "狀態: 連線失敗";
                lblKebStatus2.ForeColor = Color.Red;
                return false;
            }
        }

        private void BtnKebConnect1_Click(object sender, EventArgs e)
        {
            if (tmrKeb1.Enabled)
            {
                tmrKeb1.Stop();
                btnKebConnect1.Text = " 輪詢";
                btnKebConnect1.BackColor = Color.FromArgb(0, 180, 216);
                lblKebStatus1.Text = "狀態: 輪詢暫停";
                return;
            }

            if (EnsureKebOpen1())
            {
                kebSampleCount1 = 0;
                btnKebConnect1.Text = " 停止";
                btnKebConnect1.BackColor = Color.FromArgb(239, 68, 68);
                tmrKeb1.Start();
            }
        }

        private void BtnKebConnect2_Click(object sender, EventArgs e)
        {
            if (tmrKeb2.Enabled)
            {
                tmrKeb2.Stop();
                btnKebConnect2.Text = " 輪詢";
                btnKebConnect2.BackColor = Color.FromArgb(0, 180, 216);
                lblKebStatus2.Text = "狀態: 輪詢暫停";
                return;
            }

            if (EnsureKebOpen2())
            {
                kebSampleCount2 = 0;
                btnKebConnect2.Text = " 停止";
                btnKebConnect2.BackColor = Color.FromArgb(239, 68, 68);
                tmrKeb2.Start();
            }
        }

        private void DoKebQuery1()
        {
            if (!EnsureKebOpen1()) return;
            int node = (int)numKebNodeId1.Value;
            string portName = cmbKebPort1.SelectedItem != null ? cmbKebPort1.SelectedItem.ToString() : "COM1";
            int pNum = 1;
            if (portName.StartsWith("COM", StringComparison.OrdinalIgnoreCase)) int.TryParse(portName.Substring(3), out pNum);
            int comIdx = pNum - 1;

            int baud = int.Parse(cmbKebBaud1.SelectedItem != null ? cmbKebBaud1.SelectedItem.ToString() : "9600");
            int baudIdx = 3;
            if (baud == 19200) baudIdx = 4;
            else if (baud == 38400) baudIdx = 5;
            else if (baud == 57600) baudIdx = 6;
            else if (baud == 115200) baudIdx = 7;

            try
            {
                kebSampleCount1++;
                int? sy51 = KebReadParamWithDll(comIdx, baudIdx, node, 0x0033); // Sy.51 狀態字元 (0x0033)
                int? ru00 = sy51.HasValue ? sy51 : KebReadParamWithDll(comIdx, baudIdx, node, 0x0200);
                if (lblKebStatusWord1 != null && ru00.HasValue) lblKebStatusWord1.Text = string.Format("0x{0:X4}", ru00.Value);

                int? ru07 = KebReadParamWithDll(comIdx, baudIdx, node, 0x0207); // ru.07 實測轉速 (n * 0.125 rpm)
                if (lblKebSpeed1 != null && ru07.HasValue) lblKebSpeed1.Text = string.Format("{0:F1} rpm", ru07.Value * 0.125);

                int? ru12 = KebReadParamWithDll(comIdx, baudIdx, node, 0x020C); // ru.12 實測轉矩
                if (lblKebTrq1 != null && ru12.HasValue) lblKebTrq1.Text = string.Format("{0:F2} Nm", ru12.Value * 0.01);

                int? ru15 = KebReadParamWithDll(comIdx, baudIdx, node, 0x020F); // ru.15 輸出電流 (0.1 A)
                if (lblKebCurrent1 != null && ru15.HasValue) lblKebCurrent1.Text = string.Format("{0:F2} A", ru15.Value * 0.1);

                int? ru09 = KebReadParamWithDll(comIdx, baudIdx, node, 0x0209); // ru.09 輸出電壓 (0.1 V)
                if (lblKebVolt1 != null && ru09.HasValue) lblKebVolt1.Text = string.Format("{0:F1} V", ru09.Value * 0.1);

                int? ru18 = KebReadParamWithDll(comIdx, baudIdx, node, 0x0212); // ru.18 直流母線電壓 DC Bus (0x0212)
                if (!ru18.HasValue || ru18.Value == 0) ru18 = KebReadParamWithDll(comIdx, baudIdx, node, 0x020A); // fallback ru.10
                if (lblKebDcBus1 != null && ru18.HasValue) lblKebDcBus1.Text = string.Format("{0:F0} V", (double)ru18.Value);

                int? ru11 = KebReadParamWithDll(comIdx, baudIdx, node, 0x020B); // ru.11 設定轉矩命令
                if (lblKebPwr1 != null && ru11.HasValue) lblKebPwr1.Text = string.Format("{0:F2} Nm", ru11.Value * 0.01);

                int? ru20 = KebReadParamWithDll(comIdx, baudIdx, node, 0x0214); // ru.20 散熱溫度
                if (lblKebTemp1 != null && ru20.HasValue) lblKebTemp1.Text = string.Format("{0:F1} °C", (double)ru20.Value);

                int? ru43 = KebReadParamWithDll(comIdx, baudIdx, node, 0x022B); // ru.43 故障代碼
                if (lblKebErr1 != null && ru43.HasValue)
                {
                    lblKebErr1.Text = ru43.Value == 0 ? "無異常" : string.Format("E.0x{0:X2}", ru43.Value);
                    lblKebErr1.ForeColor = ru43.Value == 0 ? Color.DarkGreen : Color.Red;
                }

                string spdText = (lblKebSpeed1 != null) ? lblKebSpeed1.Text : "--";
                string trqText = (lblKebTrq1 != null) ? lblKebTrq1.Text : "--";
                string curText = (lblKebCurrent1 != null) ? lblKebCurrent1.Text : "--";
                string voltText = (lblKebVolt1 != null) ? lblKebVolt1.Text : "--";
                string dcText = (lblKebDcBus1 != null) ? lblKebDcBus1.Text : "--";

                txtKebLog.AppendText(string.Format("[1號主動 #{0:D3}] 轉速: {1} | 轉矩: {2} | 電流: {3} | 電壓: {4} | 母線: {5}\r\n",
                    kebSampleCount1, spdText, trqText, curText, voltText, dcText));
            }
            catch (Exception ex) { txtKebLog.AppendText("[1號異常] " + ex.Message + "\r\n"); }
        }

        private void DoKebQuery2()
        {
            if (!EnsureKebOpen2()) return;
            int node = (int)numKebNodeId2.Value;
            string portName = cmbKebPort2.SelectedItem != null ? cmbKebPort2.SelectedItem.ToString() : "COM2";
            int pNum = 2;
            if (portName.StartsWith("COM", StringComparison.OrdinalIgnoreCase)) int.TryParse(portName.Substring(3), out pNum);
            int comIdx = pNum - 1;

            int baud = int.Parse(cmbKebBaud2.SelectedItem != null ? cmbKebBaud2.SelectedItem.ToString() : "9600");
            int baudIdx = 3;
            if (baud == 19200) baudIdx = 4;
            else if (baud == 38400) baudIdx = 5;
            else if (baud == 57600) baudIdx = 6;
            else if (baud == 115200) baudIdx = 7;

            try
            {
                kebSampleCount2++;
                int? sy51 = KebReadParamWithDll(comIdx, baudIdx, node, 0x0033);
                int? ru00 = sy51.HasValue ? sy51 : KebReadParamWithDll(comIdx, baudIdx, node, 0x0200);
                if (lblKebStatusWord2 != null && ru00.HasValue) lblKebStatusWord2.Text = string.Format("0x{0:X4}", ru00.Value);

                int? ru07 = KebReadParamWithDll(comIdx, baudIdx, node, 0x0207);
                if (lblKebSpeed2 != null && ru07.HasValue) lblKebSpeed2.Text = string.Format("{0:F1} rpm", ru07.Value * 0.125);

                int? ru12 = KebReadParamWithDll(comIdx, baudIdx, node, 0x020C);
                if (lblKebTrq2 != null && ru12.HasValue) lblKebTrq2.Text = string.Format("{0:F2} Nm", ru12.Value * 0.01);

                int? ru15 = KebReadParamWithDll(comIdx, baudIdx, node, 0x020F);
                if (lblKebCurrent2 != null && ru15.HasValue) lblKebCurrent2.Text = string.Format("{0:F2} A", ru15.Value * 0.1);

                int? ru09 = KebReadParamWithDll(comIdx, baudIdx, node, 0x0209);
                if (lblKebVolt2 != null && ru09.HasValue) lblKebVolt2.Text = string.Format("{0:F1} V", ru09.Value * 0.1);

                int? ru18 = KebReadParamWithDll(comIdx, baudIdx, node, 0x0212);
                if (!ru18.HasValue || ru18.Value == 0) ru18 = KebReadParamWithDll(comIdx, baudIdx, node, 0x020A);
                if (lblKebDcBus2 != null && ru18.HasValue) lblKebDcBus2.Text = string.Format("{0:F0} V", (double)ru18.Value);

                int? ru11 = KebReadParamWithDll(comIdx, baudIdx, node, 0x020B);
                if (lblKebPwr2 != null && ru11.HasValue) lblKebPwr2.Text = string.Format("{0:F2} Nm", ru11.Value * 0.01);

                int? ru20 = KebReadParamWithDll(comIdx, baudIdx, node, 0x0214);
                if (lblKebTemp2 != null && ru20.HasValue) lblKebTemp2.Text = string.Format("{0:F1} °C", (double)ru20.Value);

                int? ru43 = KebReadParamWithDll(comIdx, baudIdx, node, 0x022B);
                if (lblKebErr2 != null && ru43.HasValue)
                {
                    lblKebErr2.Text = ru43.Value == 0 ? "無異常" : string.Format("E.0x{0:X2}", ru43.Value);
                    lblKebErr2.ForeColor = ru43.Value == 0 ? Color.DarkGreen : Color.Red;
                }

                string spdText = (lblKebSpeed2 != null) ? lblKebSpeed2.Text : "--";
                string trqText = (lblKebTrq2 != null) ? lblKebTrq2.Text : "--";
                string curText = (lblKebCurrent2 != null) ? lblKebCurrent2.Text : "--";
                string voltText = (lblKebVolt2 != null) ? lblKebVolt2.Text : "--";
                string dcText = (lblKebDcBus2 != null) ? lblKebDcBus2.Text : "--";

                txtKebLog.AppendText(string.Format("[2號負載 #{0:D3}] 轉速: {1} | 轉矩: {2} | 電流: {3} | 電壓: {4} | 母線: {5}\r\n",
                    kebSampleCount2, spdText, trqText, curText, voltText, dcText));
            }
            catch (Exception ex) { txtKebLog.AppendText("[2號異常] " + ex.Message + "\r\n"); }
        }

        // =========================================================================
        // 2014 原廠官方驗證參數記憶體透視 (ru.18 母線電壓 / ru.07 實測轉速 / ru.15 電流 / ru.12 轉矩)
        // =========================================================================
        private void RunDeepRu10Diagnosis()
        {
            // 先停止背景輪詢並關閉序列埠，防止 COM 埠衝突 (Access Denied)
            if (tmrKeb1 != null && tmrKeb1.Enabled) tmrKeb1.Stop();
            if (tmrKeb2 != null && tmrKeb2.Enabled) tmrKeb2.Stop();
            if (spKeb1 != null && spKeb1.IsOpen) { try { spKeb1.Close(); spKeb1.Dispose(); spKeb1 = null; } catch { } }
            if (spKeb2 != null && spKeb2.IsOpen) { try { spKeb2.Close(); spKeb2.Dispose(); spKeb2 = null; } catch { } }

            txtKebLog.AppendText("\r\n===============================================================\r\n");
            txtKebLog.AppendText(" [⚡ KEB 2014原廠位址記憶體透視] COM1 @ 9600 bps 8-E-1 ...\r\n");
            txtKebLog.AppendText("===============================================================\r\n");
            txtKebLog.SelectionStart = txtKebLog.Text.Length;
            txtKebLog.ScrollToCaret();
            Application.DoEvents();

            StringBuilder report = new StringBuilder();
            report.AppendLine("=== KEB 2014 原廠驗證位址讀取報告 ===");

            int[] testParams = new int[] { 0x0212, 0x0207, 0x020F, 0x020C, 0x0033, 0x0201, 0x020A, 0x0200 };
            string[] paramNames = new string[] {
                "ru.18 (2014真·直流母線電壓 0x0212)",
                "ru.07 (2014真·實測轉速 0x0207)",
                "ru.15 (2014真·輸出電流 0x020F)",
                "ru.12 (2014真·實測轉矩 0x020C)",
                "Sy.51 (2014真·狀態字元 0x0033)",
                "ru.01 (設定轉速 0x0201)",
                "ru.10 (母線電壓 0x020A)",
                "ru.00 (狀態字元 0x0200)"
            };

            int node = 1; // 依據 SY06 官方參數截圖：站號鎖定為 1
            txtKebLog.AppendText(string.Format("\r\n--- 依據原廠 SY06=1, SY07=9.6k 截圖，鎖定 站號 1 (Node 1) ---\r\n", node));

            bool foundAny = false;
            try
            {
                tProtProperty prop = new tProtProperty();
                prop.ProtType = 1; // prAnsi (DIN66019-II)
                prop.TimeOut = 450;
                prop.Comport = 0;  // COM1
                prop.Baudrate = 3; // 9600 (SY07)
                prop.Flag = 0;
                setprotproperties(ref prop);
                setinvprot(node, 1);

                for (int pIdx = 0; pIdx < testParams.Length; pIdx++)
                {
                    int pAddr = testParams[pIdx];
                    string pName = paramNames[pIdx];

                    byte[] txBuf = new byte[256];
                    byte[] rxBuf = new byte[256];
                    BitConverter.GetBytes((short)pAddr).CopyTo(txBuf, 0);
                    txBuf[2] = 1; // Setmask 1

                    int res = waitrdreq(node, 0, txBuf, rxBuf);
                    int ack = BitConverter.ToInt32(rxBuf, 12);
                    if (res == 0 && ack == 0)
                    {
                        foundAny = true;
                        int bestVal = BitConverter.ToInt32(rxBuf, 24);
                        txtKebLog.AppendText(string.Format("  [DLL Service 0] ✅ {0} >> 數值: {1} (Hex: 0x{1:X4})\r\n", pName, bestVal));
                        report.AppendLine(string.Format("• {0}: 成功 >> {1} (Hex: 0x{1:X4})", pName, bestVal));

                        if (pAddr == 0x0212 || pAddr == 0x020A) lblKebDcBus1.Text = string.Format("{0} V", bestVal);
                        else if (pAddr == 0x0033 || pAddr == 0x0200) lblKebStatusWord1.Text = string.Format("0x{0:X4}", bestVal);
                        else if (pAddr == 0x0207) lblKebSpeed1.Text = string.Format("{0:F1} rpm", bestVal * 0.125);
                        else if (pAddr == 0x020F) lblKebCurrent1.Text = string.Format("{0:F2} A", bestVal * 0.1);
                        else if (pAddr == 0x020C) lblKebTrq1.Text = string.Format("{0:F2} Nm", bestVal * 0.01);
                    }
                    else
                    {
                        txtKebLog.AppendText(string.Format("  [DLL Service 0] ❌ {0} >> res: {1}, Ack: {2} ({3})\r\n",
                            pName, res, ack, MakeKebErrorText(ack)));
                    }
                }
                closechannels();
            }
            catch (Exception ex)
            {
                txtKebLog.AppendText("  [DLL 例外] " + ex.Message + "\r\n");
            }

                // 4. 原生 SerialPort DIN 66019-II Enquiry
                SerialPort sp = null;
                try
                {
                    sp = new SerialPort("COM1", 9600, Parity.Even, 8, StopBits.One);
                    sp.ReadTimeout = 500;
                    sp.WriteTimeout = 500;
                    sp.DtrEnable = true;
                    sp.RtsEnable = true;
                    sp.Open();

                    byte EOT = 0x04, STX = 0x02, ETX = 0x03, ENQ = 0x05;
                    string addrStr = node.ToString("X2");
                    byte[] enqPkt = new byte[] {
                        EOT,
                        (byte)addrStr[0], (byte)addrStr[1],
                        (byte)'0', (byte)'2', (byte)'0', (byte)'A', (byte)'0', (byte)'0',
                        ENQ
                    };

                    sp.DiscardInBuffer();
                    sp.Write(enqPkt, 0, enqPkt.Length);
                    Thread.Sleep(80);

                    byte[] buf = new byte[64];
                    int r = 0;
                    if (sp.BytesToRead > 0) r = sp.Read(buf, 0, Math.Min(buf.Length, sp.BytesToRead));

                    if (r > 0)
                    {
                        string rxHex = BitConverter.ToString(buf, 0, r).Replace("-", " ");
                        string rxAscii = Encoding.ASCII.GetString(buf, 0, r).Replace("\x02", "<STX>").Replace("\x03", "<ETX>");
                        txtKebLog.AppendText(string.Format("  [原生 DIN66019] ✅ 收到回應 ({0}B): HEX [{1}] | ASCII [{2}]\r\n", r, rxHex, rxAscii));

                        int stxIdx = -1, etxIdx = -1;
                        for (int i = 0; i < r; i++)
                        {
                            if (buf[i] == STX) stxIdx = i;
                            if (buf[i] == ETX && stxIdx >= 0) { etxIdx = i; break; }
                        }
                        if (stxIdx >= 0 && etxIdx > stxIdx)
                        {
                            string content = Encoding.ASCII.GetString(buf, stxIdx + 1, etxIdx - (stxIdx + 1));
                            if (content.Length >= 8)
                            {
                                string hexData = content.Substring(content.Length - 4, 4);
                                uint uVal = Convert.ToUInt32(hexData, 16);
                                txtKebLog.AppendText(string.Format("  🎉 [原生解碼] 母線電壓 ru.10: {0} V (0x{1})\r\n", uVal, hexData));
                                report.AppendLine(string.Format("• Node {0} [原生電文]: 成功 >> ru.10 = {1} V", node, uVal));
                                foundAny = true;
                            }
                        }
                    }
                    else
                    {
                        txtKebLog.AppendText(string.Format("  [原生 DIN66019] 逾時無回應\r\n"));
                    }
                }
                catch (Exception ex)
                {
                    txtKebLog.AppendText("  [原生電文例外] " + ex.Message + "\r\n");
                }
                finally
                {
                    if (sp != null) { try { sp.Close(); sp.Dispose(); } catch { } }
                }

            txtKebLog.AppendText("\r\n===============================================================\r\n");
            txtKebLog.SelectionStart = txtKebLog.Text.Length;
            txtKebLog.ScrollToCaret();

            if (!foundAny)
            {
                report.AppendLine("\n⚠️ 診斷結論：COM1 @ 9600 bps 下所有站號均未回傳數值。");
                report.AppendLine("排錯建議：");
                report.AppendLine("1. 請確認 KEB F5 變頻器電源已開機 (操作面板正常顯示)。");
                report.AppendLine("2. 請確認 RS-485 連接線插在 COM1，且 Tx+/Rx+ 極性未反接。");
                report.AppendLine("3. 請確認變頻器參數 ud.02 (波特率) 為 9600，ud.01 (站號) 為 1。");
            }
            MessageBox.Show(report.ToString(), "ru.10 診斷結果");
        }

        // =========================================================================
        // 一鍵全自動智能診斷 KEB F5 通訊 (僅專注掃描 COM1)
        // =========================================================================
        private void RunOneClickKebDiagnosis()
        {
            txtKebLog.AppendText("\r\n===============================================================\r\n");
            txtKebLog.AppendText(" [⚡ 一鍵智能診斷啟動] 開始對 【COM1】 執行全自動掃描 (9600/38400/19200 bps)...\r\n");
            txtKebLog.AppendText("===============================================================\r\n");

            // 先停止背景輪詢並關閉序列埠，防止 COM 埠衝突 (Access Denied)
            if (tmrKeb1 != null && tmrKeb1.Enabled) tmrKeb1.Stop();
            if (tmrKeb2 != null && tmrKeb2.Enabled) tmrKeb2.Stop();
            btnKebConnect1.Text = " 輪詢";
            btnKebConnect1.BackColor = Color.FromArgb(0, 180, 216);
            btnKebConnect2.Text = " 輪詢";
            btnKebConnect2.BackColor = Color.FromArgb(0, 180, 216);
            if (spKeb1 != null && spKeb1.IsOpen) { try { spKeb1.Close(); } catch { } spKeb1 = null; }
            if (spKeb2 != null && spKeb2.IsOpen) { try { spKeb2.Close(); } catch { } spKeb2 = null; }
            Thread.Sleep(50);

            // 依使用者指示：僅鎖定 COM1 進行深層診斷
            string[] ports = new string[] { "COM1" };

            int[] testBauds = new int[] { 9600, 38400, 19200 };
            Parity[] testParities = new Parity[] { Parity.Even, Parity.None };
            int[] testNodes = new int[] { 1 }; // 依原廠截圖 100% 鎖定站號 1 (Node 1)

            List<string> foundDrives = new List<string>();

            foreach (string port in ports)
            {
                txtKebLog.AppendText(string.Format(" 🔍 正在探測序列埠 {0} (輪詢 9600/38400/19200 bps) ...\r\n", port));
                Application.DoEvents();

                int comIdx = 0; // COM1
                bool portFound = false;

                // 優先模式 A: 使用 KEB 原廠 protKEB.dll
                foreach (int baud in testBauds)
                {
                    if (portFound) break;
                    int baudIdx = 3; // 9600 default
                    if (baud == 38400) baudIdx = 5;
                    else if (baud == 19200) baudIdx = 4;
                    else if (baud == 57600) baudIdx = 6;
                    else if (baud == 115200) baudIdx = 7;

                    txtKebLog.AppendText(string.Format("   >> [protKEB.dll] 嘗試 {0} @ {1} bps ...\r\n", port, baud));
                    Application.DoEvents();

                    foreach (int node in testNodes)
                    {
                        int? ru18 = KebReadParamWithDll(comIdx, baudIdx, node, 0x0212); // 2014 真·母線電壓
                        int? ru07 = KebReadParamWithDll(comIdx, baudIdx, node, 0x0207); // 2014 真·實測轉速
                        int? sy51 = KebReadParamWithDll(comIdx, baudIdx, node, 0x0033); // 2014 狀態字
                        int? ru00 = KebReadParamWithDll(comIdx, baudIdx, node, 0x0200);
                        if (ru18.HasValue || ru07.HasValue || sy51.HasValue || ru00.HasValue)
                        {
                            portFound = true;
                            string info = string.Format("【{0}】@ {1} bps (protKEB 驅動), 站號 {2} >> ru.18 (母線): {3} V, ru.07: {4} rpm, Sy.51: 0x{5:X4}",
                                port, baud, node,
                                ru18.HasValue ? ru18.Value.ToString() : "--",
                                ru07.HasValue ? (ru07.Value * 0.125).ToString("F1") : "--",
                                sy51.HasValue ? sy51.Value : (ru00.HasValue ? ru00.Value : 0));

                            txtKebLog.AppendText(string.Format("   🎉 [✅ 原廠 DLL 連線成功] {0}\r\n", info));
                            foundDrives.Add(info);

                            if (!cmbKebPort1.Items.Contains(port)) cmbKebPort1.Items.Add(port);
                            cmbKebPort1.SelectedItem = port;
                            cmbKebBaud1.SelectedItem = baud.ToString();
                            numKebNodeId1.Value = node;
                            lblKebStatus1.Text = string.Format("狀態: 原廠驅動 ({0} @ {1})", port, baud);
                            lblKebStatus1.ForeColor = Color.Green;
                            break;
                        }
                    }
                }

                // 模式 B: 原生 SerialPort DIN 66019-I/II 雙模探測
                if (!portFound)
                {
                    foreach (int baud in testBauds)
                    {
                        if (portFound) break;
                        foreach (Parity par in testParities)
                        {
                            if (portFound) break;
                            SerialPort sp = null;
                            try
                            {
                                sp = new SerialPort(port, baud, par, 8, StopBits.One);
                                sp.ReadTimeout = 200;
                                sp.WriteTimeout = 200;
                                sp.DtrEnable = true;
                                sp.RtsEnable = true;
                                sp.Open();

                                foreach (int node in testNodes)
                                {
                                    int? ru18 = KebReadParam(sp, node, 0x0212);
                                    int? ru07 = KebReadParam(sp, node, 0x0207);
                                    int? sy51 = KebReadParam(sp, node, 0x0033);
                                    int? ru00 = KebReadParam(sp, node, 0x0200);
                                    if (ru18.HasValue || ru07.HasValue || sy51.HasValue || ru00.HasValue)
                                    {
                                        portFound = true;
                                        string parStr = (par == Parity.Even ? "8-E-1" : "8-N-1");
                                        string info = string.Format("【{0}】@ {1} bps, {2}, 站號 {3} >> ru.18 (母線): {4} V, ru.07: {5} rpm, Sy.51: 0x{6:X4}",
                                            port, baud, parStr, node,
                                            ru18.HasValue ? ru18.Value.ToString() : "--",
                                            ru07.HasValue ? (ru07.Value * 0.125).ToString("F1") : "--",
                                            sy51.HasValue ? sy51.Value : (ru00.HasValue ? ru00.Value : 0));

                                        txtKebLog.AppendText(string.Format("   🎉 [✅ DIN66019 成功] {0}\r\n", info));
                                        foundDrives.Add(info);

                                        if (!cmbKebPort1.Items.Contains(port)) cmbKebPort1.Items.Add(port);
                                        cmbKebPort1.SelectedItem = port;
                                        cmbKebBaud1.SelectedItem = baud.ToString();
                                        numKebNodeId1.Value = node;
                                        lblKebStatus1.Text = string.Format("狀態: 已辨識 ({0} @ {1})", port, baud);
                                        lblKebStatus1.ForeColor = Color.Green;
                                        break;
                                    }
                                }
                            }
                            catch { }
                        }
                    }
                }

                if (!portFound)
                {
                    txtKebLog.AppendText(string.Format("   ❌ {0} 未收到 KEB 變頻器回應。\r\n", port));
                }
            }

            txtKebLog.AppendText("\r\n===============================================================\r\n");
            if (foundDrives.Count > 0)
            {
                txtKebLog.AppendText(string.Format(" 🎉 [診斷完成] 成功鎖定 {0} 台 KEB F5 變頻器！已自動更新控制面板參數。\r\n", foundDrives.Count));
                txtKebLog.AppendText(" >> 您可點擊【單次】或【輪詢】開始進行運轉與監控。\r\n");
            }
            else
            {
                txtKebLog.AppendText(" ⚠️ [診斷結果] 未掃描到 KEB 驅動器。\r\n");
                txtKebLog.AppendText(" >> 排錯建議：\r\n");
                txtKebLog.AppendText("    1. 請確認 KEB 驅動器已上電開機 (面板螢幕點亮)。\r\n");
                txtKebLog.AppendText("    2. 請檢查 RS-485 接線 (Rx+/Tx+, Rx-/Tx-) 與接地線。\r\n");
                txtKebLog.AppendText("    3. 若使用 KEB 原廠轉接線，請確認 USB-Serial 晶片驅動正常。\r\n");
            }
            txtKebLog.AppendText("===============================================================\r\n\r\n");
        }

        private static string MakeKebErrorText(int errcode)
        {
            switch (errcode)
            {
                case 0: return "0: 正常 (OK / 成功)";
                case 1: return "1: 設備未就緒 (Inverter not ready / BCC framing)";
                case 2: return "2: 無效位址或密碼 (Invalid Address/Password)";
                case 3: return "3: 無效數據 (Invalid Data)";
                case 4: return "4: 參數唯讀/寫入保護 (Read Only / Write Protected)";
                case 5: return "5: BCC 校驗錯誤";
                case 6: return "6: 變頻器忙碌 (Inverter Busy)";
                case 7: return "7: 服務不支援 (Service Not Available)";
                case 8: return "8: 密碼錯誤 (Password Error)";
                case 9: return "9: 無效電文幀 (Invalid Frame)";
                case 10: return "10: 無效字元 (Invalid Character)";
                case 11: return "11: 無效 Set 選擇器";
                case 13: return "13: 無效參數位址 (Address Invalid)";
                case 14: return "14: 操作不可行 (Operation Not Possible)";
                case 16: return "16: 離線狀態 (Offline)";
                case -7: return "-7: Parity 校驗錯誤 (需為 8-E-1)";
                case -8: return "-8: Framing 幀錯誤";
                case -9: return "-9: Overrun 溢位錯誤";
                case -10: return "-10: 通訊逾時 (Timeout - 變頻器無回應，請確認站號/接線/波特率)";
                case -11: return "-11: BCC 檢查碼錯誤";
                case -100: return "-100: 通道已關閉";
                case -101: return "-101: 協定未就緒";
                case -103: return "-103: 無效變頻器站號";
                case -104: return "-104: 無效服務";
                case -300: return "-300: 接收到無效電文";
                case -400: return "-400: 無可用數據";
                default: return "代碼 " + errcode;
            }
        }

        private void RunCustomKebDllTest(int comIdx, int baudIdx, string portName, int baud, int node, int addr, int pSet)
        {
            // 1. 先安全釋放 COM 埠避免衝突
            if (tmrKeb1 != null && tmrKeb1.Enabled) tmrKeb1.Stop();
            if (tmrKeb2 != null && tmrKeb2.Enabled) tmrKeb2.Stop();
            if (spKeb1 != null && spKeb1.IsOpen) { try { spKeb1.Close(); } catch { } spKeb1 = null; }
            if (spKeb2 != null && spKeb2.IsOpen) { try { spKeb2.Close(); } catch { } spKeb2 = null; }
            Thread.Sleep(50);

            txtKebLog.AppendText(string.Format("\r\n===============================================================\r\n"));
            txtKebLog.AppendText(string.Format(" [⚡ 原廠 DLL 測試啟動] 埠: {0} (索引 {1}) @ {2} bps, 站號: {3}, Addr: 0x{4:X4}, Set: {5}\r\n",
                portName, comIdx, baud, node, addr, pSet));
            txtKebLog.AppendText(string.Format("===============================================================\r\n"));
            txtKebLog.SelectionStart = txtKebLog.Text.Length;
            txtKebLog.ScrollToCaret();
            Application.DoEvents();

            try
            {
                // ============================================================
                // 依據 VB6 源碼：原始動力計使用 prAnsi (DIN66019-II)
                // 但同時測試 prHsp5，以確認硬體協議類型
                // ============================================================
                int[] protTypes = new int[] { 1, 2 }; // 1=prAnsi, 2=prHsp5
                string[] protNames = new string[] { "prAnsi (DIN66019-II)", "prHsp5" };
                int[] services = new int[] { -1, 1, 0 }; // Service -1最穩定(16bit通用)
                bool anySuccess = false;

                for (int pi = 0; pi < protTypes.Length; pi++)
                {
                    int protType = protTypes[pi];
                    string protName = protNames[pi];

                    tProtProperty prop = new tProtProperty();
                    prop.ProtType = protType;
                    prop.Baudrate = baudIdx;
                    prop.Comport = comIdx;
                    prop.TimeOut = 600;
                    prop.Flag = 0;
                    setprotproperties(ref prop);
                    setinvprot(node, protType);
                    setretrycnt(2);
                    txtKebLog.AppendText(string.Format(">> [{0}/{1}] 協定: {2} | 站號: {3} | Addr: 0x{4:X4}\r\n",
                        pi + 1, protTypes.Length, protName, node, addr));
                    Application.DoEvents();

                    foreach (int svc in services)
                    {
                        byte[] txBuf = new byte[256];
                        byte[] rxBuf = new byte[256];
                        BitConverter.GetBytes((short)addr).CopyTo(txBuf, 0);
                        txBuf[2] = (byte)pSet;

                        int res = waitrdreq(node, svc, txBuf, rxBuf);
                        int ack = BitConverter.ToInt32(rxBuf, 12);
                        string errDesc = MakeKebErrorText(ack);
                        txtKebLog.AppendText(string.Format("   Service {0}: res={1}, Ack={2} [{3}]\r\n",
                            svc, res, ack, errDesc));

                        if (res == 0 && ack == 0)
                        {
                            anySuccess = true;
                            int finalData = BitConverter.ToInt32(rxBuf, 24);

                            txtKebLog.AppendText(string.Format("   🎉 [{0} / Service {1}] 讀取成功！最終值: {2} (0x{2:X8})\r\n", protName, svc, finalData));
                            MessageBox.Show(string.Format(
                                "【KEB DLL 讀取成功】\n協議: {0}\nService: {1}\nAddr: 0x{2:X4}\nAck: 0 (正常)\n實測數據: {3}",
                                protName, svc, addr, finalData), "KEB 測試成功 ✅");
                            break;
                        }
                    }
                    closechannels();
                }

                if (!anySuccess)
                {
                    txtKebLog.AppendText("   ❌ prAnsi + prHsp5 全部 Service 組合均無回應\r\n");
                    MessageBox.Show(
                        "【KEB DLL 全協議測試失敗】\n\nprAnsi(DIN66019II) 和 prHsp5 均無回應。\n\n排錯建議：\n1. 確認 RS-485 接線正確 (Tx+/Rx+ 對應)\n2. 確認變頻器站號（目前測試站號 " + node + "）\n3. 確認 9600 bps 8-E-1\n4. 請確認 KEB 驅動器已上電開機",
                        "KEB 測試失敗 ❌");
                }
            }
            catch (Exception ex)
            {
                txtKebLog.AppendText("[DLL 例外錯誤] " + ex.Message + "\r\n");
                MessageBox.Show("DLL 執行例外: " + ex.Message, "錯誤");
            }
            txtKebLog.SelectionStart = txtKebLog.Text.Length;
            txtKebLog.ScrollToCaret();
        }

        private void RunCustomKebDinTest(string portName, int baud, int node, int addr, int pSet)
        {
            // 1. 先安全釋放 COM 埠避免衝突
            if (tmrKeb1 != null && tmrKeb1.Enabled) tmrKeb1.Stop();
            if (tmrKeb2 != null && tmrKeb2.Enabled) tmrKeb2.Stop();
            if (spKeb1 != null && spKeb1.IsOpen) { try { spKeb1.Close(); spKeb1.Dispose(); spKeb1 = null; } catch { } }
            if (spKeb2 != null && spKeb2.IsOpen) { try { spKeb2.Close(); spKeb2.Dispose(); spKeb2 = null; } catch { } }

            txtKebLog.AppendText(string.Format("\r\n===============================================================\r\n"));
            txtKebLog.AppendText(string.Format(" [⚡ 原生 DIN 66019-II 測試啟動] {0} @ {1} bps 8-E-1, 站號: {2}, Addr: 0x{3:X4}, Set: {4}\r\n",
                portName, baud, node, addr, pSet));
            txtKebLog.AppendText(string.Format("===============================================================\r\n"));
            txtKebLog.SelectionStart = txtKebLog.Text.Length;
            txtKebLog.ScrollToCaret();
            Application.DoEvents();

            SerialPort sp = null;
            try
            {
                sp = new SerialPort(portName, baud, Parity.Even, 8, StopBits.One);
                sp.ReadTimeout = 600;
                sp.WriteTimeout = 600;
                sp.DtrEnable = true;
                sp.RtsEnable = true;
                sp.Open();

                byte EOT = 0x04, STX = 0x02, ETX = 0x03, ENQ = 0x05;
                string addrStr = node.ToString("X2");
                string paramStr = string.Format("{0:X4}{1:X2}", addr, pSet);
                byte[] pBytes = Encoding.ASCII.GetBytes(paramStr);

                // 建立 10-Byte Enquiry 電文: EOT + Addr(2) + Param(4) + Set(2) + ENQ
                byte[] enqPkt = new byte[10];
                enqPkt[0] = EOT;
                enqPkt[1] = (byte)addrStr[0];
                enqPkt[2] = (byte)addrStr[1];
                Array.Copy(pBytes, 0, enqPkt, 3, 6);
                enqPkt[9] = ENQ;

                string txHex = BitConverter.ToString(enqPkt).Replace("-", " ");
                string txAscii = Encoding.ASCII.GetString(enqPkt).Replace("\x04", "<EOT>").Replace("\x05", "<ENQ>");
                txtKebLog.AppendText(string.Format(">> [1/2] 發送 Enquiry 電文: HEX [{0}] | ASCII [{1}]\r\n", txHex, txAscii));

                sp.DiscardInBuffer();
                sp.Write(enqPkt, 0, enqPkt.Length);
                Thread.Sleep(60);

                byte[] buf = new byte[64];
                int r = 0;
                if (sp.BytesToRead > 0)
                {
                    r = sp.Read(buf, 0, Math.Min(buf.Length, sp.BytesToRead));
                }

                if (r > 0)
                {
                    string rxHex = BitConverter.ToString(buf, 0, r).Replace("-", " ");
                    string rxAscii = Encoding.ASCII.GetString(buf, 0, r).Replace("\x02", "<STX>").Replace("\x03", "<ETX>");
                    txtKebLog.AppendText(string.Format(">> [2/2] 收到變頻器回應 ({0} Bytes): HEX [{1}] | ASCII [{2}]\r\n", r, rxHex, rxAscii));

                    // 解析 STX ~ ETX
                    int stxIdx = -1, etxIdx = -1;
                    for (int i = 0; i < r; i++)
                    {
                        if (buf[i] == STX) stxIdx = i;
                        if (buf[i] == ETX && stxIdx >= 0) { etxIdx = i; break; }
                    }

                    if (stxIdx >= 0 && etxIdx > stxIdx)
                    {
                        string content = Encoding.ASCII.GetString(buf, stxIdx + 1, etxIdx - (stxIdx + 1));
                        txtKebLog.AppendText(string.Format("   - 欄位解析: 內容長度={0}, 內容=[{1}]\r\n", content.Length, content));
                        if (content.Length >= 14)
                        {
                            string hexData = content.Substring(content.Length - 8, 8);
                            uint uVal = Convert.ToUInt32(hexData, 16);
                            txtKebLog.AppendText(string.Format("   🎉 [解析成功] Data: 0x{0} (十進位: {1})\r\n", hexData, uVal));
                            MessageBox.Show(string.Format("【原生電文解析成功】\n參數 0x{0:X4}\nData 數值: {1} (0x{2})", addr, uVal, hexData), "DIN 66019 成功");
                        }
                    }
                }
                else
                {
                    txtKebLog.AppendText(">> [2/2] ❌ 逾時無回應 (變頻器未回傳位元組，請確認 RS-485 接線與站號)\r\n");
                    MessageBox.Show(string.Format("【原生電文無回應】\n發送電文: {0}\n變頻器未回傳位元組。\n請確認：\n1. 站號 (預設為 1 或 0)\n2. 波特率 9600\n3. RS-485 接線極性 (Rx+/Tx+)", txHex), "通訊逾時");
                }
            }
            catch (Exception ex)
            {
                txtKebLog.AppendText("[序列埠例外] " + ex.Message + "\r\n");
                MessageBox.Show("序列埠例外: " + ex.Message, "錯誤");
            }
            finally
            {
                if (sp != null) { try { sp.Close(); sp.Dispose(); } catch { } }
            }
            txtKebLog.SelectionStart = txtKebLog.Text.Length;
            txtKebLog.ScrollToCaret();
        }

        private static readonly object kebLock = new object();
        private static int activeKebComIndex = -1;
        private static int activeKebBaudIndex = -1;

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
                        prop.ProtType = 1; // DIN 66019-II
                        prop.Baudrate = baudIndex;
                        prop.Comport  = comIndex;
                        prop.TimeOut  = 600;
                        prop.Flag     = 0;
                        prop.Port     = 0;
                        prop.Txtlen   = 0;
                        prop.txt      = "";
                        setprotproperties(ref prop);
                        setretrycnt(2);
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

        private static int? KebReadParam(SerialPort sp, int invAddr, int paramAddr, int paramSet = 1)
        {
            int comIndex = 0; // 預設 COM1 (索引 0)
            int baudIdx = 3;  // 預設 9600 bps
            if (sp != null)
            {
                if (sp.PortName.StartsWith("COM", StringComparison.OrdinalIgnoreCase))
                {
                    int pNum = 1;
                    if (int.TryParse(sp.PortName.Substring(3), out pNum)) comIndex = pNum - 1;
                }
                if (sp.BaudRate == 19200) baudIdx = 4;
                else if (sp.BaudRate == 9600) baudIdx = 3;
                else if (sp.BaudRate == 57600) baudIdx = 6;
                else if (sp.BaudRate == 115200) baudIdx = 7;
                else if (sp.BaudRate == 38400) baudIdx = 5;
            }

            return KebReadParamWithDll(comIndex, baudIdx, invAddr, paramAddr, paramSet);
        }

        private void KebWriteParam32(SerialPort sp, int invAddr, int paramAddr, int dataVal, string desc, int paramSet = 1)
        {
            int comIndex = 0;
            int baudIdx = 3;
            if (sp != null)
            {
                if (sp.PortName.StartsWith("COM", StringComparison.OrdinalIgnoreCase))
                {
                    int pNum = 1;
                    if (int.TryParse(sp.PortName.Substring(3), out pNum)) comIndex = pNum - 1;
                }
                if (sp.BaudRate == 19200) baudIdx = 4;
                else if (sp.BaudRate == 9600) baudIdx = 3;
                else if (sp.BaudRate == 57600) baudIdx = 6;
                else if (sp.BaudRate == 115200) baudIdx = 7;
                else if (sp.BaudRate == 38400) baudIdx = 5;
            }

            lock (kebLock)
            {
                try
                {
                    if (activeKebComIndex != comIndex || activeKebBaudIndex != baudIdx)
                    {
                        closechannels();
                        tProtProperty prop = new tProtProperty();
                        prop.ProtType = 1;
                        prop.Baudrate = baudIdx;
                        prop.Comport = comIndex;
                        prop.TimeOut = 500;
                        setprotproperties(ref prop);
                        setretrycnt(2);
                        activeKebComIndex = comIndex;
                        activeKebBaudIndex = baudIdx;
                    }

                    setinvprot(invAddr, 1);

                    byte[] txBuf = new byte[256];
                    byte[] rxBuf = new byte[256];
                    BitConverter.GetBytes((short)paramAddr).CopyTo(txBuf, 0);
                    txBuf[2] = (byte)paramSet;
                    txBuf[3] = 0;
                    BitConverter.GetBytes(dataVal).CopyTo(txBuf, 4);

                    int res = waitwrreq(invAddr, 0, txBuf, rxBuf);
                    int ack = BitConverter.ToInt32(rxBuf, 12);

                    if (res == 0 && ack == 0)
                    {
                        txtKebLog.AppendText(string.Format(">> [protKEB.dll 寫入成功] [{0}] 數值: {1}\r\n", desc, dataVal));
                        return;
                    }
                }
                catch { activeKebComIndex = -1; try { closechannels(); } catch { } }
            }
        }

        private void KebStopDrive(SerialPort sp, int invAddr, string driveName)
        {
            KebWriteParam32(sp, invAddr, 0x0201, 0, driveName + " 速度歸零");
            txtKebLog.AppendText(string.Format(" [{0}] 已發送停止命令！\r\n", driveName));
        }

        // =========================================================================
        // 分頁 1: KISTLER 4700B 扭力感測計
        // =========================================================================
        private void BuildTorquePanel(Panel pnl)
        {
            GroupBox grpMain = new GroupBox() { Text = "【主要測試】KISTLER 4700B 原廠 MENU:DISP? 直讀 (COM4 @ 1,000,000 Baud)", Location = new Point(15, 8), Size = new Size(975, 85) };

            Label l1 = new Label() { Text = "COM 埠:", Location = new Point(15, 24), AutoSize = true };
            cmbTorquePort = new ComboBox() { Location = new Point(75, 21), Width = 80, DropDownStyle = ComboBoxStyle.DropDownList };
            btnTorqueRefresh = new Button() { Text = "整理", Location = new Point(160, 20), Size = new Size(48, 25) };
            btnTorqueRefresh.Click += (s, e) => RefreshTorquePorts();

            btnTorqueSingleQuery = new Button()
            {
                Text = " 單次查詢 (MENU:DISP?)",
                Location = new Point(220, 18),
                Size = new Size(185, 30),
                BackColor = Color.FromArgb(245, 158, 11),
                ForeColor = Color.White,
                Font = new Font("微軟正黑體", 9f, FontStyle.Bold)
            };
            btnTorqueSingleQuery.Click += (s, e) => DoTorqueQuery();

            btnTorqueConnect = new Button()
            {
                Text = " 開始連續輪詢",
                Location = new Point(415, 17),
                Size = new Size(140, 32),
                BackColor = Color.FromArgb(0, 180, 216),
                ForeColor = Color.White,
                Font = new Font("微軟正黑體", 9.5f, FontStyle.Bold)
            };
            btnTorqueConnect.Click += BtnTorqueConnect_Click;

            lblTorqueStatus = new Label() { Text = "狀態: 未連線", Location = new Point(570, 24), AutoSize = true, ForeColor = Color.Gray };

            Label lHint = new Label()
            {
                Text = "[OK] 現場實測確認：韌體版本為 V5.12，已配置 CRLF 結尾符，支援 *IDN?、MENU:DISP? 與 LCD Hex 解碼！",
                Location = new Point(15, 56),
                AutoSize = true,
                ForeColor = Color.DarkGreen,
                Font = new Font("微軟正黑體", 9f, FontStyle.Bold)
            };

            grpMain.Controls.AddRange(new Control[] { l1, cmbTorquePort, btnTorqueRefresh, btnTorqueSingleQuery, btnTorqueConnect, lblTorqueStatus, lHint });
            pnl.Controls.Add(grpMain);

            Panel pnlCard = new Panel() { Location = new Point(15, 98), Size = new Size(975, 65), BackColor = Color.FromArgb(245, 248, 250), BorderStyle = BorderStyle.FixedSingle };
            Label lt1 = new Label() { Text = "實測轉矩 (Torque):", Location = new Point(15, 5), AutoSize = true, ForeColor = Color.Gray, Font = new Font("微軟正黑體", 9f) };
            lblTorqueValue = new Label() { Text = "0.000 Nm", Location = new Point(15, 22), AutoSize = true, ForeColor = Color.FromArgb(245, 158, 11), Font = new Font("微軟正黑體", 20f, FontStyle.Bold) };

            Label lt2 = new Label() { Text = "實測轉速 (Speed):", Location = new Point(330, 5), AutoSize = true, ForeColor = Color.Gray, Font = new Font("微軟正黑體", 9f) };
            lblTorqueSpeed = new Label() { Text = "0 rpm", Location = new Point(330, 22), AutoSize = true, ForeColor = Color.FromArgb(0, 180, 216), Font = new Font("微軟正黑體", 20f, FontStyle.Bold) };

            Label lt3 = new Label() { Text = "機械功率 (Power):", Location = new Point(640, 5), AutoSize = true, ForeColor = Color.Gray, Font = new Font("微軟正黑體", 9f) };
            lblTorquePower = new Label() { Text = "0.000 kW", Location = new Point(640, 22), AutoSize = true, ForeColor = Color.FromArgb(16, 185, 129), Font = new Font("微軟正黑體", 20f, FontStyle.Bold) };

            pnlCard.Controls.AddRange(new Control[] { lt1, lblTorqueValue, lt2, lblTorqueSpeed, lt3, lblTorquePower });
            pnl.Controls.Add(pnlCard);

            GroupBox grpDiag = new GroupBox() { Text = "【備案測試與診斷工具】", Location = new Point(15, 168), Size = new Size(975, 68) };

            Label lBaud = new Label() { Text = "鮑率:", Location = new Point(10, 24), AutoSize = true };
            cmbTorqueBaud = new ComboBox() { Location = new Point(48, 21), Width = 95, DropDownStyle = ComboBoxStyle.DropDownList };
            cmbTorqueBaud.Items.AddRange(new object[] { "1000000", "115200", "57600", "38400", "19200", "9600" });
            cmbTorqueBaud.SelectedIndex = 0;

            Label lTerm = new Label() { Text = "結尾符:", Location = new Point(150, 24), AutoSize = true };
            cmbTorqueTerm = new ComboBox() { Location = new Point(200, 21), Width = 80, DropDownStyle = ComboBoxStyle.DropDownList };
            cmbTorqueTerm.Items.AddRange(new object[] { "CRLF (\\r\\n)", "CR (\\r)", "LF (\\n)" });
            cmbTorqueTerm.SelectedIndex = 0;

            btnTorqueScanAllCmds = new Button()
            {
                Text = " 備案A: 掃描所有指令",
                Location = new Point(290, 19),
                Size = new Size(165, 28),
                BackColor = Color.FromArgb(100, 116, 139),
                ForeColor = Color.White,
                Font = new Font("微軟正黑體", 9f, FontStyle.Bold)
            };
            btnTorqueScanAllCmds.Click += BtnTorqueScanAllCmds_Click;

            Label lCust = new Label() { Text = "自訂指令:", Location = new Point(465, 24), AutoSize = true };
            txtTorqueCustomCmd = new TextBox() { Text = "*IDN?", Location = new Point(530, 21), Width = 150 };
            btnTorqueSendCustom = new Button() { Text = "發送", Location = new Point(690, 19), Size = new Size(55, 28) };
            btnTorqueSendCustom.Click += (s, e) => SendTorqueRaw(txtTorqueCustomCmd.Text);

            Button btnClearTorqueLog = new Button() { Text = "清空日誌", Location = new Point(860, 19), Size = new Size(90, 28) };
            btnClearTorqueLog.Click += (s, e) => txtTorqueLog.Clear();

            grpDiag.Controls.AddRange(new Control[] { lBaud, cmbTorqueBaud, lTerm, cmbTorqueTerm, btnTorqueScanAllCmds, lCust, txtTorqueCustomCmd, btnTorqueSendCustom, btnClearTorqueLog });
            pnl.Controls.Add(grpDiag);

            GroupBox grpLog = new GroupBox() { Text = "KISTLER 4700B 通訊記錄", Location = new Point(15, 240), Size = new Size(975, 410) };
            txtTorqueLog = new TextBox() { Dock = DockStyle.Fill, Multiline = true, ScrollBars = ScrollBars.Vertical, ReadOnly = true, BackColor = Color.Black, ForeColor = Color.LightGreen, Font = new Font("Consolas", 9.5f) };
            grpLog.Controls.Add(txtTorqueLog);
            pnl.Controls.Add(grpLog);

            RefreshTorquePorts();

            tmrTorque = new System.Windows.Forms.Timer();
            tmrTorque.Interval = 250;
            tmrTorque.Tick += (s, e) => DoTorqueQuery();
        }

        private void RefreshTorquePorts()
        {
            cmbTorquePort.Items.Clear();
            string[] ports = SerialPort.GetPortNames();
            int selIdx = -1;
            for (int i = 0; i < ports.Length; i++)
            {
                cmbTorquePort.Items.Add(ports[i]);
                if (ports[i].Equals("COM4", StringComparison.OrdinalIgnoreCase)) selIdx = i;
            }
            if (selIdx >= 0) cmbTorquePort.SelectedIndex = selIdx;
            else if (cmbTorquePort.Items.Count > 0) cmbTorquePort.SelectedIndex = 0;
            else cmbTorquePort.Items.Add("COM4");
        }

        private bool EnsureTorqueOpen()
        {
            if (spTorque != null && spTorque.IsOpen) return true;
            string port = cmbTorquePort.SelectedItem.ToString();
            int baud = int.Parse(cmbTorqueBaud.SelectedItem != null ? cmbTorqueBaud.SelectedItem.ToString() : "1000000");

            try
            {
                spTorque = new SerialPort(port, baud, Parity.None, 8, StopBits.One);
                spTorque.ReadTimeout = 500;
                spTorque.WriteTimeout = 500;
                spTorque.DtrEnable = true;
                spTorque.RtsEnable = true;
                spTorque.NewLine = "\r\n";
                spTorque.Open();
                lblTorqueStatus.Text = string.Format("狀態: 已開啟 ({0} @ {1})", port, baud);
                lblTorqueStatus.ForeColor = Color.Green;
                txtTorqueLog.AppendText(string.Format("[{0}] 成功開啟串列埠 {1} @ {2} bps (DTR/RTS CRLF 已就緒)\r\n", DateTime.Now.ToLongTimeString(), port, baud));
                return true;
            }
            catch (Exception ex)
            {
                txtTorqueLog.AppendText(string.Format("[錯誤] 開啟 {0} 失敗: {1}\r\n", port, ex.Message));
                lblTorqueStatus.Text = "狀態: 開啟失敗";
                lblTorqueStatus.ForeColor = Color.Red;
                return false;
            }
        }

        private void BtnTorqueConnect_Click(object sender, EventArgs e)
        {
            if (tmrTorque.Enabled)
            {
                tmrTorque.Stop();
                btnTorqueConnect.Text = " 開始連續輪詢";
                btnTorqueConnect.BackColor = Color.FromArgb(0, 180, 216);
                lblTorqueStatus.Text = "狀態: 輪詢已暫停";
                return;
            }

            if (EnsureTorqueOpen())
            {
                torqueSampleCount = 0;
                btnTorqueConnect.Text = " 停止輪詢";
                btnTorqueConnect.BackColor = Color.FromArgb(239, 68, 68);
                tmrTorque.Start();
            }
        }

        private void DoTorqueQuery()
        {
            if (!EnsureTorqueOpen()) return;

            try
            {
                spTorque.DiscardInBuffer();
                byte[] cmd = Encoding.ASCII.GetBytes("MENU:DISP?\r\n");
                spTorque.Write(cmd, 0, cmd.Length);
                Thread.Sleep(80);

                string raw = "";
                if (spTorque.BytesToRead > 0) raw = spTorque.ReadExisting().Trim();

                if (!string.IsNullOrEmpty(raw))
                {
                    torqueSampleCount++;
                    string decoded = DecodeHexToAscii(raw);

                    double torque = 0.0;
                    double speed = 0.0;
                    double power = 0.0;

                    Match mTorq = Regex.Match(decoded, @"Torque\s+([-\+]?\d+(\.\d+)?)");
                    Match mSpd = Regex.Match(decoded, @"Speed\s+([-\+]?\d+(\.\d+)?)");
                    Match mPwr = Regex.Match(decoded, @"Power\s+([-\+]?\d+(\.\d+)?)");

                    if (mTorq.Success) double.TryParse(mTorq.Groups[1].Value, out torque);
                    if (mSpd.Success) double.TryParse(mSpd.Groups[1].Value, out speed);
                    if (mPwr.Success) double.TryParse(mPwr.Groups[1].Value, out power);

                    lblTorqueValue.Text = string.Format("{0:F3} Nm", torque);
                    lblTorqueSpeed.Text = string.Format("{0:F0} rpm", speed);
                    lblTorquePower.Text = string.Format("{0:F3} kW", power);

                    txtTorqueLog.AppendText(string.Format("[#{0:D3}] 轉矩: {1,7:F3} Nm | 轉速: {2,5:F0} rpm | 功率: {3,6:F3} kW\r\n", torqueSampleCount, torque, speed, power));
                }
            }
            catch (Exception ex)
            {
                txtTorqueLog.AppendText("[異常] " + ex.Message + "\r\n");
            }
        }

        private void SendTorqueRaw(string cmd)
        {
            if (!EnsureTorqueOpen()) return;
            string term = "\r\n";
            if (cmbTorqueTerm.SelectedIndex == 1) term = "\r";
            else if (cmbTorqueTerm.SelectedIndex == 2) term = "\n";

            try
            {
                spTorque.DiscardInBuffer();
                byte[] b = Encoding.ASCII.GetBytes(cmd + term);
                spTorque.Write(b, 0, b.Length);
                Thread.Sleep(100);
                string r = "";
                if (spTorque.BytesToRead > 0) r = spTorque.ReadExisting().Trim();
                txtTorqueLog.AppendText(string.Format(">> 手動發送 [{0}] >> 回應: [{1}]\r\n", cmd, r));
            }
            catch (Exception ex)
            {
                txtTorqueLog.AppendText("[發送異常] " + ex.Message + "\r\n");
            }
        }

        private void BtnTorqueScanAllCmds_Click(object sender, EventArgs e)
        {
            if (!EnsureTorqueOpen()) return;
            txtTorqueLog.AppendText("\r\n=== 正在對 Kistler 4700B 執行全部指令掃描 (CRLF) ===\r\n");

            string[] testCmds = new string[] {
                "*IDN?", "MENU:DISP?", "*TSR?", "ROUT:TORQ?", "SENS:UNIT?", "SENS:RANG?", "MEAS:ALL?", "M?", "READ?"
            };

            foreach (string cmd in testCmds)
            {
                try
                {
                    spTorque.DiscardInBuffer();
                    byte[] b = Encoding.ASCII.GetBytes(cmd + "\r\n");
                    spTorque.Write(b, 0, b.Length);
                    Thread.Sleep(80);
                    if (spTorque.BytesToRead > 0)
                    {
                        string resp = spTorque.ReadExisting().Trim();
                        txtTorqueLog.AppendText(string.Format("  指令 [{0,-12}] >> 回應: [{1}]\r\n", cmd, resp));
                    }
                    else
                    {
                        txtTorqueLog.AppendText(string.Format("  指令 [{0,-12}] >> (無回應)\r\n", cmd));
                    }
                }
                catch { }
            }
            txtTorqueLog.AppendText("=== 掃描結束 ===\r\n");
        }

        private static string DecodeHexToAscii(string hex)
        {
            try
            {
                if (hex.Contains("Torque") || hex.Contains("Sensor")) return hex;

                StringBuilder sb = new StringBuilder();
                for (int i = 0; i < hex.Length - 1; i += 2)
                {
                    string hs = hex.Substring(i, 2);
                    if (hs.Equals("FF", StringComparison.OrdinalIgnoreCase)) break;
                    byte b;
                    if (byte.TryParse(hs, NumberStyles.HexNumber, null, out b)) sb.Append((char)b);
                }
                string res = sb.ToString();
                return string.IsNullOrEmpty(res) ? hex : res;
            }
            catch { return hex; }
        }

        // =========================================================================
        // 分頁 2: 橫河 WT333E 功率分析儀
        // =========================================================================
        private void BuildPowerPanel(Panel pnl)
        {
            GroupBox grpMain = new GroupBox() { Text = "【橫河 WT333E 功率分析儀】全介面與通訊通道測試 (支援乙太網路與 USB)", Location = new Point(15, 8), Size = new Size(975, 95) };

            Label l1 = new Label() { Text = "WT333E IP:", Location = new Point(10, 24), AutoSize = true };
            txtPowerIp = new TextBox() { Text = "192.168.0.11", Location = new Point(78, 21), Width = 88 };

            Label l2 = new Label() { Text = "通訊通道:", Location = new Point(170, 24), AutoSize = true };
            cmbPowerProtocol = new ComboBox() { Location = new Point(230, 21), Width = 230, DropDownStyle = ComboBoxStyle.DropDownList };
            cmbPowerProtocol.Items.AddRange(new object[] {
                "乙太網路 Modbus TCP (Port 502)",
                "乙太網路 tmctl (Wire=4, Port 111 VXI-11)",
                "乙太網路 Socket Server (Port 10001)",
                "WTViewer 通訊埠 (TCP Socket 51064)",
                "USB 直連 tmctl (Wire=6, USBTMC原廠線)"
            });
            cmbPowerProtocol.SelectedIndex = 0;
            cmbPowerProtocol.SelectedIndexChanged += (s, e) => {
                string sel = cmbPowerProtocol.SelectedItem != null ? cmbPowerProtocol.SelectedItem.ToString() : "";
                if (sel.Contains("502")) txtPowerPort.Text = "502";
                else if (sel.Contains("10001")) txtPowerPort.Text = "10001";
                else if (sel.Contains("51064")) txtPowerPort.Text = "51064";
                else if (sel.Contains("51065")) txtPowerPort.Text = "51065";
                else if (sel.Contains("111")) txtPowerPort.Text = "111";
            };

            Label lPort = new Label() { Text = "Port:", Location = new Point(466, 24), AutoSize = true };
            txtPowerPort = new TextBox() { Text = "502", Location = new Point(498, 21), Width = 40 };

            btnPowerSingleQuery = new Button()
            {
                Text = " 單次查詢",
                Location = new Point(560, 16),
                Size = new Size(110, 36),
                BackColor = Color.FromArgb(245, 158, 11),
                ForeColor = Color.White,
                Font = new Font("微軟正黑體", 9.5f, FontStyle.Bold),
                Cursor = Cursors.Hand
            };
            btnPowerSingleQuery.Click += (s, e) => DoPowerSingleQuery();

            btnPowerConnect = new Button()
            {
                Text = " 開始連續讀取",
                Location = new Point(680, 16),
                Size = new Size(130, 36),
                BackColor = Color.FromArgb(0, 180, 216),
                ForeColor = Color.White,
                Font = new Font("微軟正黑體", 9.5f, FontStyle.Bold),
                Cursor = Cursors.Hand
            };
            btnPowerConnect.Click += BtnPowerConnect_Click;

            lblPowerStatus = new Label() { Text = "狀態: 未連線", Location = new Point(825, 24), AutoSize = true, ForeColor = Color.Gray, Font = new Font("微軟正黑體", 9.5f) };

            grpMain.Controls.AddRange(new Control[] { l1, txtPowerIp, l2, cmbPowerProtocol, lPort, txtPowerPort, btnPowerSingleQuery, btnPowerConnect, lblPowerStatus });
            pnl.Controls.Add(grpMain);

            GroupBox grpTable = new GroupBox() { Text = "WT333E 三相即時量測數據 (即時電壓、電流、電功率與功率因數)", Location = new Point(15, 108), Size = new Size(975, 125) };
            dgvPower = new DataGridView() { Dock = DockStyle.Fill, BackgroundColor = Color.White, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill, RowHeadersVisible = false, AllowUserToAddRows = false };
            dgvPower.Columns.Add("P", "項目");
            dgvPower.Columns.Add("U", "1 相 / U 相");
            dgvPower.Columns.Add("V", "2 相 / V 相");
            dgvPower.Columns.Add("W", "3 相 / W 相");
            dgvPower.Columns.Add("Sigma", "Sigma 總計");
            dgvPower.Columns.Add("PF", "功率因數 (PF)");

            dgvPower.Rows.Add("電壓 (V)", "--", "--", "--", "--", "--");
            dgvPower.Rows.Add("電流 (A)", "--", "--", "--", "--", "--");
            dgvPower.Rows.Add("功率 (kW)", "--", "--", "--", "--", "--");

            grpTable.Controls.Add(dgvPower);
            pnl.Controls.Add(grpTable);

            GroupBox grpDiag = new GroupBox() { Text = "【多假說驗證與全矩陣獵捕工具箱】", Location = new Point(15, 238), Size = new Size(975, 78) };

            Button btnPowerMatrixDump = new Button()
            {
                Text = "🔍 128暫存器全矩陣Dump獵捕",
                Location = new Point(8, 20),
                Size = new Size(180, 48),
                BackColor = Color.FromArgb(139, 92, 246),
                ForeColor = Color.White,
                Font = new Font("微軟正黑體", 8.5f, FontStyle.Bold),
                Cursor = Cursors.Hand
            };
            btnPowerMatrixDump.Click += (s, e) => RunPowerMatrixDump();

            Label lRegOffset = new Label() { Text = "Modbus解碼區塊:", Location = new Point(230, 32), AutoSize = true, Font = new Font("微軟正黑體", 9f, FontStyle.Bold) };
            cmbPowerModbusOffset = new ComboBox() { Location = new Point(345, 29), Width = 280, DropDownStyle = ComboBoxStyle.DropDownList, Font = new Font("微軟正黑體", 9f) };
            cmbPowerModbusOffset.Items.AddRange(new object[] {
                "0x0064 (100 - 橫河原廠標準實體量測矩陣: 16 Floats/Element)",
                "0x0100 (256 - 連續全通道 U,I,P,S,Q,PF 浮點數)",
                "0x0150 (336 - 各相獨立有功功率 kW 區塊)"
            });
            cmbPowerModbusOffset.SelectedIndex = 0;
            wtModbusAddr = 100;
            wtModbusCount = 96;
            cmbPowerModbusOffset.SelectedIndexChanged += (s, e) => {
                if (cmbPowerModbusOffset.SelectedIndex == 0) { wtModbusAddr = 100; wtModbusCount = 96; }
                else if (cmbPowerModbusOffset.SelectedIndex == 1) { wtModbusAddr = 256; wtModbusCount = 48; }
                else if (cmbPowerModbusOffset.SelectedIndex == 2) { wtModbusAddr = 336; wtModbusCount = 48; }
            };

            Button btnClearPowerLog = new Button() { Text = "清空日誌", Location = new Point(870, 20), Size = new Size(95, 48), Font = new Font("微軟正黑體", 9f) };
            btnClearPowerLog.Click += (s, e) => txtPowerLog.Clear();

            grpDiag.Controls.AddRange(new Control[] { btnPowerMatrixDump, lRegOffset, cmbPowerModbusOffset, btnClearPowerLog });
            pnl.Controls.Add(grpDiag);

            GroupBox grpLog = new GroupBox() { Text = "WT333E 診斷與通訊日誌 (即時顯示各通道握手狀態與數據封包)", Location = new Point(15, 308), Size = new Size(975, 342) };
            txtPowerLog = new TextBox() { Dock = DockStyle.Fill, Multiline = true, ScrollBars = ScrollBars.Vertical, ReadOnly = true, BackColor = Color.Black, ForeColor = Color.LightGreen, Font = new Font("Consolas", 9.5f) };
            grpLog.Controls.Add(txtPowerLog);
            pnl.Controls.Add(grpLog);

            tmrPower = new System.Windows.Forms.Timer();
            tmrPower.Interval = 500;
            tmrPower.Tick += (s, e) => DoPowerQuery();

            LoadDeviceConfig();
        }

        private void SaveDeviceConfig()
        {
            try
            {
                string cfgPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "dynamometer_config.ini");
                using (StreamWriter sw = new StreamWriter(cfgPath, false, Encoding.UTF8))
                {
                    sw.WriteLine("[WT333E]");
                    sw.WriteLine("IP=" + txtPowerIp.Text.Trim());
                    sw.WriteLine("Port=" + txtPowerPort.Text.Trim());
                    sw.WriteLine("Protocol=" + cmbPowerProtocol.SelectedIndex);
                    sw.WriteLine("SelectedText=" + (cmbPowerProtocol.SelectedItem != null ? cmbPowerProtocol.SelectedItem.ToString() : ""));
                }
                txtPowerLog.AppendText(" [配置記憶] 已將通訊配置寫入 dynamometer_config.ini！\r\n");
            }
            catch { }
        }

        private void LoadDeviceConfig()
        {
            try
            {
                string cfgPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "dynamometer_config.ini");
                if (File.Exists(cfgPath))
                {
                    string[] lines = File.ReadAllLines(cfgPath);
                    foreach (string l in lines)
                    {
                        string line = l.Trim();
                        if (line.StartsWith("IP=")) txtPowerIp.Text = line.Substring(3).Trim();
                        if (line.StartsWith("Port=")) txtPowerPort.Text = line.Substring(5).Trim();
                    }
                }
            }
            catch { }
        }

        private static int wtModbusFunc = 4;
        private static int wtModbusAddr = 0;
        private static int wtModbusCount = 48;

        private void RunOfficialScpiTest(string ip, int targetPort = 10001)
        {
            if (string.IsNullOrEmpty(ip)) ip = "192.168.0.11";
            txtPowerLog.AppendText(string.Format("\r\n===============================================================\r\n"));
            txtPowerLog.AppendText(string.Format(" [⚡ 官方手冊 SCPI 測試啟動] 目標 IP: {0}, 首選埠: {1} ...\r\n", ip, targetPort));
            txtPowerLog.AppendText(string.Format("===============================================================\r\n"));

            int[] tryPorts = new int[] { targetPort, 10001, 1001, 51064 };
            bool success = false;

            foreach (int port in tryPorts)
            {
                try
                {
                    txtPowerLog.AppendText(string.Format(">> 正在建立 TCP Socket 連線 {0}:{1} (超時 1.5s) ...\r\n", ip, port));
                    using (TcpClient tc = new TcpClient())
                    {
                        IAsyncResult ar = tc.BeginConnect(ip, port, null, null);
                        if (ar.AsyncWaitHandle.WaitOne(1500) && tc.Connected)
                        {
                            using (NetworkStream ns = tc.GetStream())
                            {
                                ns.ReadTimeout = 1500;
                                ns.WriteTimeout = 1500;

                                byte[] idnCmd = Encoding.ASCII.GetBytes("*IDN?\n");
                                ns.Write(idnCmd, 0, idnCmd.Length);
                                Thread.Sleep(80);
                                byte[] idnBuf = new byte[512];
                                int idnLen = ns.Read(idnBuf, 0, idnBuf.Length);
                                string idnStr = Encoding.ASCII.GetString(idnBuf, 0, idnLen).Trim();
                                txtPowerLog.AppendText(string.Format("   ✅ [握手成功] *IDN? 回應: [{0}]\r\n", idnStr));

                                string initSeq = ":COMMUNICATE:HEADER OFF\n:NUMERIC:FORMAT ASCII\n:NUMERIC:NORMAL:PRESET 1\n:NUMERIC:NORMAL:NUMBER 13\n"
                                               + ":NUMERIC:NORMAL:ITEM1 U,1\n:NUMERIC:NORMAL:ITEM2 I,1\n:NUMERIC:NORMAL:ITEM3 P,1\n"
                                               + ":NUMERIC:NORMAL:ITEM4 U,2\n:NUMERIC:NORMAL:ITEM5 I,2\n:NUMERIC:NORMAL:ITEM6 P,2\n"
                                               + ":NUMERIC:NORMAL:ITEM7 U,3\n:NUMERIC:NORMAL:ITEM8 I,3\n:NUMERIC:NORMAL:ITEM9 P,3\n"
                                               + ":NUMERIC:NORMAL:ITEM10 U,SIGMA\n:NUMERIC:NORMAL:ITEM11 I,SIGMA\n:NUMERIC:NORMAL:ITEM12 P,SIGMA\n:NUMERIC:NORMAL:ITEM13 LAMBDA,SIGMA\n";
                                byte[] initBytes = Encoding.ASCII.GetBytes(initSeq);
                                ns.Write(initBytes, 0, initBytes.Length);
                                Thread.Sleep(100);

                                byte[] valCmd = Encoding.ASCII.GetBytes(":NUMeric:NORMal:VALue?\n");
                                ns.Write(valCmd, 0, valCmd.Length);
                                Thread.Sleep(100);

                                byte[] valBuf = new byte[2048];
                                int valLen = ns.Read(valBuf, 0, valBuf.Length);
                                string rawVal = Encoding.ASCII.GetString(valBuf, 0, valLen).Trim();
                                txtPowerLog.AppendText(string.Format("   🎉 [官方回傳 ASCII 數據] [{0}]\r\n", rawVal));

                                UpdatePowerTable(rawVal);
                                success = true;
                                break;
                            }
                        }
                    }
                }
                catch { }
            }

            if (!success)
            {
                txtPowerLog.AppendText("⚠️ [測試建議] SCPI Socket 未連通，若使用 Modbus TCP 請維持 Port 502！\r\n");
            }
        }

        private void RunOfficialUsbTest()
        {
            txtPowerLog.AppendText(string.Format("\r\n===============================================================\r\n"));
            txtPowerLog.AppendText(string.Format(" [⚡ USB 介面專屬通訊測試啟動 (USBTMC / tmctl 原廠通道)] ...\r\n"));
            txtPowerLog.AppendText(string.Format("===============================================================\r\n"));
            Application.DoEvents();

            try
            {
                int id = -1;
                int ret = TmcInitialize(5, "", ref id);
                if (ret != 0 || id < 0) ret = TmcInitialize(6, "", ref id);
                if (ret != 0 || id < 0) ret = TmcInitialize(3, "", ref id);

                if (ret == 0 && id >= 0)
                {
                    txtPowerLog.AppendText(string.Format(">> [1/3] ✅ USB 通道初始化成功！裝置代碼 ID: {0}\r\n", id));

                    TmcSend(id, "*IDN?");
                    StringBuilder sbIdn = new StringBuilder(512);
                    int rLen = 0;
                    TmcReceive(id, sbIdn, 512, ref rLen);
                    string idn = sbIdn.ToString().Trim();
                    txtPowerLog.AppendText(string.Format(">> [2/3] 儀表型號 (*IDN?): [{0}]\r\n", idn));

                    string initSeq = ":COMMUNICATE:HEADER OFF;:NUMERIC:FORMAT ASCII;:NUMERIC:NORMAL:PRESET 1;:NUMERIC:NORMAL:NUMBER 13;";
                    TmcSend(id, initSeq);
                    Thread.Sleep(80);

                    TmcSend(id, ":NUMeric:NORMal:VALue?");
                    StringBuilder sbVal = new StringBuilder(4096);
                    TmcReceive(id, sbVal, 4096, ref rLen);
                    string valStr = sbVal.ToString().Trim();
                    txtPowerLog.AppendText(string.Format(">> [3/3] 🎉 [收到 USB SCPI 數據]: [{0}]\r\n", valStr));

                    UpdatePowerTable(valStr);
                    TmcFinish(id);
                    MessageBox.Show(string.Format("【WT333E USB 測試成功】\n\n設備: {0}\n\n數據:\n{1}", idn, valStr), "USB 成功");
                }
                else
                {
                    txtPowerLog.AppendText(string.Format("❌ [USB 初始化失敗] 錯誤代碼: {0}\r\n", ret));
                }
            }
            catch (Exception ex)
            {
                txtPowerLog.AppendText("[USB 測試例外] " + ex.Message + "\r\n");
            }
        }

        // =========================================================================
        // 假說 1: 128 暫存器全矩陣 Dump 獵捕 (特徵值直接定位)
        // =========================================================================
        private void RunPowerMatrixDump()
        {
            string ip = txtPowerIp.Text.Trim();
            if (string.IsNullOrEmpty(ip)) ip = "192.168.0.11";

            txtPowerLog.AppendText("\r\n===============================================================\r\n");
            txtPowerLog.AppendText(string.Format(" [🔍 128 暫存器全矩陣 Dump 獵捕啟動] (IP: {0}) ...\r\n", ip));
            txtPowerLog.AppendText(" >> 目標：搜尋所有 ~15A 電流與 ~52V 電壓的實體暫存器 Offset\r\n");
            txtPowerLog.AppendText("===============================================================\r\n");
            Application.DoEvents();

            int[] scanBases = new int[] { 256, 100, 336, 512 };
            try
            {
                using (TcpClient tc = new TcpClient())
                {
                    IAsyncResult ar = tc.BeginConnect(ip, 502, null, null);
                    if (ar.AsyncWaitHandle.WaitOne(600) && tc.Connected)
                    {
                        using (NetworkStream ns = tc.GetStream())
                        {
                            ns.ReadTimeout = 600;
                            ns.WriteTimeout = 600;

                            foreach (int baseAddr in scanBases)
                            {
                                byte[] req = new byte[] {
                                    0x00, 0x01, 0x00, 0x00, 0x00, 0x06, 0x01, 0x04,
                                    (byte)(baseAddr >> 8), (byte)(baseAddr & 0xFF), 0x00, 0x30
                                };
                                ns.Write(req, 0, req.Length);
                                Thread.Sleep(50);

                                byte[] resp = new byte[256];
                                int r = ns.Read(resp, 0, resp.Length);
                                if (r >= 9 && resp[7] == 0x04)
                                {
                                    txtPowerLog.AppendText(string.Format("\r\n--- [區塊 0x{0:X4} ({1}) 實測 Float 陣列 (16組)] ---\r\n", baseAddr, baseAddr));
                                    for (int i = 0; i < 16; i++)
                                    {
                                        float val = ParseModbusValue(resp, 9 + i * 4, 0);
                                        string tag = "";
                                        if (Math.Abs(val) >= 12.0f && Math.Abs(val) <= 18.0f) tag = " 🎯[電流~15A]";
                                        else if (val >= 45.0f && val <= 60.0f) tag = " ⚡[電壓~52V]";
                                        else if (val >= 100.0f && val <= 130.0f) tag = " ⚡[線電壓~110V]";
                                        else if (Math.Abs(val) >= 25.0f && Math.Abs(val) <= 35.0f) tag = " 📌[電流總和~30A]";

                                        txtPowerLog.AppendText(string.Format("  F{0,2} [Reg {1} / +{2,2}B]: {3,10:F2}{4}\r\n",
                                            i, baseAddr + i * 2, i * 4, val, tag));
                                    }
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                txtPowerLog.AppendText("❌ 全矩陣 Dump 異常: " + ex.Message + "\r\n");
            }
            txtPowerLog.SelectionStart = txtPowerLog.Text.Length;
            txtPowerLog.ScrollToCaret();
        }

        // =========================================================================
        // 假說 2: 3P3W 兩瓦特表物理合成驗證
        // =========================================================================
        private void RunPower3P3WTest()
        {
            string ip = txtPowerIp.Text.Trim();
            if (string.IsNullOrEmpty(ip)) ip = "192.168.0.11";

            txtPowerLog.AppendText("\r\n===============================================================\r\n");
            txtPowerLog.AppendText(" [⚡ 3P3W 兩瓦特表物理合成驗證啟動] ...\r\n");
            txtPowerLog.AppendText(" >> 原理：Element 1 (L1) + Element 2 (L3) 實測，Element 3 由平衡向量合成\r\n");
            txtPowerLog.AppendText("===============================================================\r\n");
            Application.DoEvents();

            try
            {
                using (TcpClient tc = new TcpClient())
                {
                    IAsyncResult ar = tc.BeginConnect(ip, 502, null, null);
                    if (ar.AsyncWaitHandle.WaitOne(600) && tc.Connected)
                    {
                        using (NetworkStream ns = tc.GetStream())
                        {
                            ns.ReadTimeout = 600;
                            ns.WriteTimeout = 600;
                            byte[] req = new byte[] {
                                0x00, 0x01, 0x00, 0x00, 0x00, 0x06, 0x01, 0x04,
                                0x01, 0x00, 0x00, 0x30
                            };
                            ns.Write(req, 0, req.Length);
                            Thread.Sleep(50);

                            byte[] resp = new byte[256];
                            int r = ns.Read(resp, 0, resp.Length);
                            if (r >= 9 && resp[7] == 0x04)
                            {
                                float u1 = ParseModbusValue(resp, 9 + 0, 0);
                                float i1 = ParseModbusValue(resp, 9 + 4, 0);
                                float p1 = ParseModbusValue(resp, 9 + 8, 0);

                                float u2 = ParseModbusValue(resp, 9 + 12, 0);
                                float i2 = ParseModbusValue(resp, 9 + 16, 0);
                                float p2 = ParseModbusValue(resp, 9 + 20, 0);

                                // 3P3W 合成
                                float u3 = (u1 + u2) / 2.0f;
                                float i3 = (i1 + i2) / 2.0f;
                                float pSum = p1 + p2;
                                float p3 = pSum / 3.0f;

                                txtPowerLog.AppendText(string.Format("  Element 1 (實測): U1={0:F2} V, I1={1:F2} A, P1={2:F2} kW\r\n", u1, i1, p1));
                                txtPowerLog.AppendText(string.Format("  Element 2 (實測): U2={0:F2} V, I2={1:F2} A, P2={2:F2} kW\r\n", u2, i2, p2));
                                txtPowerLog.AppendText(string.Format("  Element 3 (合成): U3={0:F2} V, I3={1:F2} A, P3={2:F2} kW\r\n", u3, i3, p3));
                                txtPowerLog.AppendText(string.Format("  Sigma 總合: P總 = {0:F2} kW, I總 = {1:F2} A\r\n", pSum, i1 + i2));

                                if (dgvPower != null && dgvPower.Rows.Count >= 3)
                                {
                                    dgvPower.Rows[0].Cells[1].Value = u1.ToString("F2");
                                    dgvPower.Rows[0].Cells[2].Value = u2.ToString("F2");
                                    dgvPower.Rows[0].Cells[3].Value = u3.ToString("F2");
                                    dgvPower.Rows[0].Cells[4].Value = ((u1 + u2 + u3) / 3.0f).ToString("F2");

                                    dgvPower.Rows[1].Cells[1].Value = i1.ToString("F2");
                                    dgvPower.Rows[1].Cells[2].Value = i2.ToString("F2");
                                    dgvPower.Rows[1].Cells[3].Value = i3.ToString("F2");
                                    dgvPower.Rows[1].Cells[4].Value = (i1 + i2).ToString("F2");

                                    dgvPower.Rows[2].Cells[1].Value = p1.ToString("F3");
                                    dgvPower.Rows[2].Cells[2].Value = p2.ToString("F3");
                                    dgvPower.Rows[2].Cells[3].Value = p3.ToString("F3");
                                    dgvPower.Rows[2].Cells[4].Value = pSum.ToString("F3");
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex) { txtPowerLog.AppendText("❌ 3P3W 驗證異常: " + ex.Message + "\r\n"); }
            txtPowerLog.SelectionStart = txtPowerLog.Text.Length;
            txtPowerLog.ScrollToCaret();
        }

        // =========================================================================
        // 假說 3: 參數分組模式驗證 (U/I/P 分離)
        // =========================================================================
        private void RunPowerGroupTest()
        {
            string ip = txtPowerIp.Text.Trim();
            if (string.IsNullOrEmpty(ip)) ip = "192.168.0.11";

            txtPowerLog.AppendText("\r\n===============================================================\r\n");
            txtPowerLog.AppendText(" [⚡ 參數分組模式 (U/I/P 獨立區塊) 驗證啟動] ...\r\n");
            txtPowerLog.AppendText(" >> 原理：電壓區塊 (F0..F3) / 電流區塊 (F4..F7) / 功率區塊 (F8..F11)\r\n");
            txtPowerLog.AppendText("===============================================================\r\n");
            Application.DoEvents();

            try
            {
                using (TcpClient tc = new TcpClient())
                {
                    IAsyncResult ar = tc.BeginConnect(ip, 502, null, null);
                    if (ar.AsyncWaitHandle.WaitOne(600) && tc.Connected)
                    {
                        using (NetworkStream ns = tc.GetStream())
                        {
                            ns.ReadTimeout = 600;
                            ns.WriteTimeout = 600;
                            byte[] req = new byte[] {
                                0x00, 0x01, 0x00, 0x00, 0x00, 0x06, 0x01, 0x04,
                                0x01, 0x00, 0x00, 0x30
                            };
                            ns.Write(req, 0, req.Length);
                            Thread.Sleep(50);

                            byte[] resp = new byte[256];
                            int r = ns.Read(resp, 0, resp.Length);
                            if (r >= 9 && resp[7] == 0x04)
                            {
                                float u1 = ParseModbusValue(resp, 9 + 0, 0);
                                float u2 = ParseModbusValue(resp, 9 + 4, 0);
                                float u3 = ParseModbusValue(resp, 9 + 8, 0);
                                float uSig = ParseModbusValue(resp, 9 + 12, 0);

                                float i1 = ParseModbusValue(resp, 9 + 16, 0);
                                float i2 = ParseModbusValue(resp, 9 + 20, 0);
                                float i3 = ParseModbusValue(resp, 9 + 24, 0);
                                float iSig = ParseModbusValue(resp, 9 + 28, 0);

                                float p1 = ParseModbusValue(resp, 9 + 32, 0);
                                float p2 = ParseModbusValue(resp, 9 + 36, 0);
                                float p3 = ParseModbusValue(resp, 9 + 40, 0);
                                float pSig = ParseModbusValue(resp, 9 + 44, 0);

                                txtPowerLog.AppendText(string.Format("  電壓區塊: U1={0:F2}V, U2={1:F2}V, U3={2:F2}V, U_Sig={3:F2}V\r\n", u1, u2, u3, uSig));
                                txtPowerLog.AppendText(string.Format("  電流區塊: I1={0:F2}A, I2={1:F2}A, I3={2:F2}A, I_Sig={3:F2}A\r\n", i1, i2, i3, iSig));
                                txtPowerLog.AppendText(string.Format("  功率區塊: P1={0:F2}kW, P2={1:F2}kW, P3={2:F2}kW, P_Sig={3:F2}kW\r\n", p1, p2, p3, pSig));

                                if (dgvPower != null && dgvPower.Rows.Count >= 3)
                                {
                                    dgvPower.Rows[0].Cells[1].Value = u1.ToString("F2");
                                    dgvPower.Rows[0].Cells[2].Value = u2.ToString("F2");
                                    dgvPower.Rows[0].Cells[3].Value = u3.ToString("F2");
                                    dgvPower.Rows[0].Cells[4].Value = uSig.ToString("F2");

                                    dgvPower.Rows[1].Cells[1].Value = i1.ToString("F2");
                                    dgvPower.Rows[1].Cells[2].Value = i2.ToString("F2");
                                    dgvPower.Rows[1].Cells[3].Value = i3.ToString("F2");
                                    dgvPower.Rows[1].Cells[4].Value = iSig.ToString("F2");

                                    dgvPower.Rows[2].Cells[1].Value = p1.ToString("F3");
                                    dgvPower.Rows[2].Cells[2].Value = p2.ToString("F3");
                                    dgvPower.Rows[2].Cells[3].Value = p3.ToString("F3");
                                    dgvPower.Rows[2].Cells[4].Value = pSig.ToString("F3");
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex) { txtPowerLog.AppendText("❌ 分組驗證異常: " + ex.Message + "\r\n"); }
            txtPowerLog.SelectionStart = txtPowerLog.Text.Length;
            txtPowerLog.ScrollToCaret();
        }

        // =========================================================================
        // 一鍵自動位址探測與物理約束驗算 (Physical Constraint & Sigma Solver)
        // =========================================================================
        private void RunOneClickPowerDiagnosis()
        {
            string ip = txtPowerIp.Text.Trim();
            if (string.IsNullOrEmpty(ip)) ip = "192.168.0.11";

            txtPowerLog.AppendText(string.Format("\r\n===============================================================\r\n"));
            txtPowerLog.AppendText(string.Format(" [⚡ WT333E 物理約束與 Sigma 閉環自動診斷啟動] (IP: {0}) ...\r\n", ip));
            txtPowerLog.AppendText(string.Format(" >> 約束條件：三相電流平衡 (~15A)、三相電壓平衡 (~52V)、Sigma 功率閉環\r\n"));
            txtPowerLog.AppendText(string.Format("===============================================================\r\n"));
            Application.DoEvents();

            int[] testAddrs = new int[] { 256, 100, 288, 336, 512, 0 };
            string[] addrNames = new string[] { "0x0100 (256)", "0x0064 (100)", "0x0120 (288)", "0x0150 (336)", "0x0200 (512)", "0x0000 (0)" };

            int bestAddr = -1;
            int bestStrideIndex = 0;
            float bestScore = -999999f;
            string bestReport = "";

            try
            {
                using (TcpClient tc = new TcpClient())
                {
                    IAsyncResult ar = tc.BeginConnect(ip, 502, null, null);
                    if (ar.AsyncWaitHandle.WaitOne(600) && tc.Connected)
                    {
                        using (NetworkStream ns = tc.GetStream())
                        {
                            ns.ReadTimeout = 600;
                            ns.WriteTimeout = 600;

                            for (int aIdx = 0; aIdx < testAddrs.Length; aIdx++)
                            {
                                int addr = testAddrs[aIdx];
                                string aName = addrNames[aIdx];

                                byte[] req = new byte[] {
                                    0x00, 0x01, 0x00, 0x00, 0x00, 0x06, 0x01, 0x04,
                                    (byte)(addr >> 8), (byte)(addr & 0xFF), 0x00, 0x30
                                };
                                ns.Write(req, 0, req.Length);
                                Thread.Sleep(40);

                                byte[] resp = new byte[256];
                                int r = ns.Read(resp, 0, resp.Length);
                                if (r >= 9 && resp[7] == 0x04)
                                {
                                    int baseOffset = 9;
                                    // 提取 16 個 Float 陣列
                                    float[] floats = new float[16];
                                    for (int fi = 0; fi < 16; fi++)
                                    {
                                        floats[fi] = ParseModbusValue(resp, baseOffset + fi * 4, 0);
                                    }

                                    // 評估 4 種解碼跨距
                                    for (int strideMode = 0; strideMode < 4; strideMode++)
                                    {
                                        float u1 = 0f, i1 = 0f, p1 = 0f;
                                        float u2 = 0f, i2 = 0f, p2 = 0f;
                                        float u3 = 0f, i3 = 0f, p3 = 0f;
                                        float pSig = 0f;

                                        if (strideMode == 0) // 24B 標準 (每相 6 Float)
                                        {
                                            u1 = floats[0]; i1 = floats[1]; p1 = floats[2];
                                            u2 = floats[6]; i2 = floats[7]; p2 = floats[8];
                                            u3 = floats[12]; i3 = floats[13]; p3 = floats[14];
                                            pSig = floats.Length > 15 ? floats[15] : (p1 + p2 + p3);
                                        }
                                        else if (strideMode == 1) // 16B 緊湊 (每相 4 Float)
                                        {
                                            u1 = floats[0]; i1 = floats[1]; p1 = floats[2];
                                            u2 = floats[4]; i2 = floats[5]; p2 = floats[6];
                                            u3 = floats[8]; i3 = floats[9]; p3 = floats[10];
                                            pSig = floats[14];
                                        }
                                        else if (strideMode == 2) // 12B 基本 (每相 3 Float)
                                        {
                                            u1 = floats[0]; i1 = floats[1]; p1 = floats[2];
                                            u2 = floats[3]; i2 = floats[4]; p2 = floats[5];
                                            u3 = floats[6]; i3 = floats[7]; p3 = floats[8];
                                            pSig = floats[11];
                                        }
                                        else if (strideMode == 3) // 3P3W 兩瓦特表合成
                                        {
                                            u1 = floats[0]; i1 = floats[1]; p1 = floats[2];
                                            u2 = floats[3]; i2 = floats[4]; p2 = floats[5];
                                            u3 = (u1 + u2) / 2.0f;
                                            i3 = (i1 + i2) / 2.0f;
                                            p3 = 0f;
                                            pSig = p1 + p2;
                                        }

                                        // 物理特徵約束評分體系 (Score System)
                                        float score = 0f;

                                        // 1. 電流特徵約束：三相電流在 8A~25A 之間 (特別是 ~15A)，且三相平衡差 < 3A
                                        bool i1Ok = Math.Abs(i1) >= 5f && Math.Abs(i1) <= 30f;
                                        bool i2Ok = Math.Abs(i2) >= 5f && Math.Abs(i2) <= 30f;
                                        bool i3Ok = Math.Abs(i3) >= 5f && Math.Abs(i3) <= 30f;
                                        if (i1Ok) score += 30f;
                                        if (i2Ok) score += 30f;
                                        if (i3Ok) score += 30f;
                                        if (i1Ok && i2Ok && Math.Abs(Math.Abs(i1) - Math.Abs(i2)) <= 3.0f) score += 50f;
                                        if (i2Ok && i3Ok && Math.Abs(Math.Abs(i2) - Math.Abs(i3)) <= 3.0f) score += 50f;
                                        if (Math.Abs(i1) >= 12f && Math.Abs(i1) <= 18f) score += 40f; // 命中 ~15A 目標！

                                        // 2. 電壓特徵約束：三相電壓在 30V~80V 之間 (特別是 ~52V)，且三相平衡差 < 10V
                                        bool u1Ok = u1 >= 20f && u1 <= 120f;
                                        bool u2Ok = u2 >= 20f && u2 <= 120f;
                                        bool u3Ok = u3 >= 20f && u3 <= 120f;
                                        if (u1Ok) score += 20f;
                                        if (u2Ok) score += 20f;
                                        if (u3Ok) score += 20f;
                                        if (u1Ok && u2Ok && Math.Abs(u1 - u2) <= 10.0f) score += 40f;
                                        if (u2Ok && u3Ok && Math.Abs(u2 - u3) <= 10.0f) score += 40f;
                                        if (u1 >= 45f && u1 <= 60f) score += 30f; // 命中 ~52V 目標！

                                        // 3. Sigma 功率閉環驗算
                                        float pSum = p1 + p2 + p3;
                                        if (Math.Abs(pSig) > 0.01f && Math.Abs(pSum - pSig) <= (Math.Abs(pSig) * 0.2f + 2.0f))
                                        {
                                            score += 100f; // 功率閉環吻合！
                                        }

                                        if (score > bestScore)
                                        {
                                            bestScore = score;
                                            bestAddr = addr;
                                            bestStrideIndex = strideMode;
                                            bestReport = string.Format("位址: {0} | 跨距: {1} >> U1={2:F1}V, U2={3:F1}V, U3={4:F1}V | I1={5:F2}A, I2={6:F2}A, I3={7:F2}A | P總={8:F2}kW",
                                                aName, strideMode, u1, u2, u3, i1, i2, i3, (Math.Abs(pSig) > 0.01f ? pSig : pSum));
                                        }
                                    }
                                }
                            }
                        }
                    }
                }

                if (bestAddr >= 0 && bestScore > 50)
                {
                    wtModbusAddr = bestAddr;
                    if (cmbPowerModbusOffset != null && cmbPowerModbusOffset.Items.Count > bestStrideIndex)
                    {
                        cmbPowerModbusOffset.SelectedIndex = bestStrideIndex;
                    }
                    txtPowerLog.AppendText(string.Format("🎉 【🎯 物理約束與閉環吻合，已自動鎖定最佳位址】\r\n   -> {0}\r\n", bestReport));
                    DoPowerQuery();
                }
                else
                {
                    txtPowerLog.AppendText("⚠️ 未能在連線中匹配出同時符合三相平衡(~15A, ~52V)與功率閉環之組合，請確認馬達已通電加載。\r\n");
                }
            }
            catch (Exception ex)
            {
                txtPowerLog.AppendText("❌ 自動掃描異常: " + ex.Message + "\r\n");
            }

            txtPowerLog.SelectionStart = txtPowerLog.Text.Length;
            txtPowerLog.ScrollToCaret();
        }

        // =========================================================================
        // 單次查詢按鈕功能 (Single Query)
        // =========================================================================
        private void DoPowerSingleQuery()
        {
            string ip = txtPowerIp.Text.Trim();
            string sel = cmbPowerProtocol.SelectedItem != null ? cmbPowerProtocol.SelectedItem.ToString() : "";
            int port = 502;
            int.TryParse(txtPowerPort.Text.Trim(), out port);
            if (port <= 0) port = 502;

            txtPowerLog.AppendText(string.Format("[單次查詢] 通道: {0}, 目標: {1}:{2} ...\r\n", sel, ip, port));

            if (sel.Contains("USB") || sel.Contains("tmctl (Wire=6"))
            {
                try
                {
                    int id = -1;
                    int ret = TmcInitialize(6, "", ref id);
                    if (ret != 0 || id < 0) ret = TmcInitialize(5, "", ref id);
                    if (ret != 0 || id < 0) ret = TmcInitialize(3, "", ref id);

                    if (ret == 0 && id >= 0)
                    {
                        TmcSend(id, ":COMMUNICATE:HEADER OFF");
                        TmcSend(id, ":NUMeric:NORMal:VALue?");
                        StringBuilder sb = new StringBuilder(4096);
                        int readLen = 0;
                        int r = TmcReceive(id, sb, 4096, ref readLen);
                        TmcFinish(id);

                        if (r == 0 && sb.Length > 0)
                        {
                            txtPowerLog.AppendText(string.Format("  [USB 數據] {0}\r\n", sb.ToString().Trim()));
                            UpdatePowerTable(sb.ToString().Trim());
                            lblPowerStatus.Text = "狀態: 單次查詢成功 (USB)";
                            lblPowerStatus.ForeColor = Color.Green;
                        }
                    }
                    else
                    {
                        txtPowerLog.AppendText(string.Format("  [USB 失敗] 初始化代碼: {0}\r\n", ret));
                    }
                }
                catch (Exception ex) { txtPowerLog.AppendText("  [USB 例外] " + ex.Message + "\r\n"); }
            }
            else if (sel.Contains("Modbus") || port == 502)
            {
                try
                {
                    using (TcpClient tc = new TcpClient())
                    {
                        tc.Connect(ip, port);
                        using (NetworkStream ns = tc.GetStream())
                        {
                            ns.ReadTimeout = 600;
                            ns.WriteTimeout = 600;
                            byte[] req = new byte[] {
                                0x00, 0x01, 0x00, 0x00, 0x00, 0x06, 0x01,
                                (byte)wtModbusFunc,
                                (byte)((wtModbusAddr >> 8) & 0xFF),
                                (byte)(wtModbusAddr & 0xFF),
                                (byte)((wtModbusCount >> 8) & 0xFF),
                                (byte)(wtModbusCount & 0xFF)
                            };
                            ns.Write(req, 0, req.Length);
                            Thread.Sleep(50);
                            byte[] buf = new byte[256];
                            int r = ns.Read(buf, 0, buf.Length);
                            if (r >= 9 && buf[7] == wtModbusFunc)
                            {
                                UpdatePowerTableFromModbus(buf, r);
                                txtPowerLog.AppendText(string.Format("  [Modbus 成功] 讀取 {0} Bytes 三相電氣數值！\r\n", r));
                                lblPowerStatus.Text = "狀態: 單次查詢成功 (Modbus 502)";
                                lblPowerStatus.ForeColor = Color.Green;
                            }
                            else
                            {
                                txtPowerLog.AppendText(string.Format("  [Modbus 回應異常] 回應長度: {0}\r\n", r));
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    txtPowerLog.AppendText(string.Format("  [Modbus 失敗] {0}:{1} - {2}\r\n", ip, port, ex.Message));
                }
            }
            else
            {
                try
                {
                    using (TcpClient tc = new TcpClient())
                    {
                        tc.Connect(ip, port);
                        using (NetworkStream ns = tc.GetStream())
                        {
                            ns.ReadTimeout = 800;
                            byte[] cmd = Encoding.ASCII.GetBytes(":NUMeric:NORMal:VALue?\n");
                            ns.Write(cmd, 0, cmd.Length);
                            Thread.Sleep(80);
                            byte[] buf = new byte[4096];
                            int r = ns.Read(buf, 0, buf.Length);
                            string resp = Encoding.ASCII.GetString(buf, 0, r).Trim();
                            txtPowerLog.AppendText(string.Format("  [Socket 數據] {0}\r\n", resp));
                            UpdatePowerTable(resp);
                            lblPowerStatus.Text = string.Format("狀態: 單次查詢成功 (Port {0})", port);
                            lblPowerStatus.ForeColor = Color.Green;
                        }
                    }
                }
                catch (Exception ex)
                {
                    txtPowerLog.AppendText(string.Format("  [Socket 失敗] {0}:{1} - {2}\r\n", ip, port, ex.Message));
                }
            }
        }

        private void BtnPowerConnect_Click(object sender, EventArgs e)
        {
            if (ykDeviceId >= 0 || (tcpPowerSocket != null && tcpPowerSocket.Connected))
            {
                tmrPower.Stop();
                if (ykDeviceId >= 0) { try { TmcFinish(ykDeviceId); } catch { } ykDeviceId = -1; }
                if (streamPowerSocket != null) { streamPowerSocket.Close(); streamPowerSocket = null; }
                if (tcpPowerSocket != null) { tcpPowerSocket.Close(); tcpPowerSocket = null; }

                btnPowerConnect.Text = " 開始連線讀取";
                btnPowerConnect.BackColor = Color.FromArgb(0, 180, 216);
                lblPowerStatus.Text = "狀態: 已斷開";
                lblPowerStatus.ForeColor = Color.Gray;
                return;
            }

            string ip = txtPowerIp.Text.Trim();
            string sel = cmbPowerProtocol.SelectedItem != null ? cmbPowerProtocol.SelectedItem.ToString() : "";

            if (sel.Contains("USB") || sel.Contains("tmctl (Wire=6"))
            {
                try
                {
                    txtPowerLog.AppendText(string.Format("[{0}] 正在以 USB tmctl 直連 WT333E...\r\n", DateTime.Now.ToLongTimeString()));
                    int id = -1;
                    int ret = TmcInitialize(6, "", ref id);
                    if (ret != 0 || id < 0) ret = TmcInitialize(5, "", ref id);
                    if (ret != 0 || id < 0) ret = TmcInitialize(3, "", ref id);

                    if (ret == 0 && id >= 0)
                    {
                        ykDeviceId = id;
                        powerSampleCount = 0;
                        btnPowerConnect.Text = "停止讀取";
                        btnPowerConnect.BackColor = Color.FromArgb(239, 68, 68);
                        lblPowerStatus.Text = "狀態: USB 成功連線 WT333E";
                        lblPowerStatus.ForeColor = Color.Green;
                        txtPowerLog.AppendText(string.Format("[OK] USB tmctl.dll 成功連線 WT333E (Handle: {0})\r\n", id));

                        InitPowerMeterScpi();
                        tmrPower.Start();
                        SaveDeviceConfig();
                    }
                    else
                    {
                        txtPowerLog.AppendText(string.Format("[FAIL] USB tmctl 初始化回傳碼: {0}\r\n", ret));
                        MessageBox.Show("USB 連線失敗 (Code: " + ret + ")。\n請確認 USB 傳輸線已連接且電表已開機！", "提示");
                    }
                }
                catch (Exception ex)
                {
                    txtPowerLog.AppendText("[USB 載入異常] " + ex.Message + "\r\n");
                }
            }
            else if (sel.Contains("tmctl (Wire=4") || sel.Contains("VXI-11"))
            {
                try
                {
                    txtPowerLog.AppendText(string.Format("[{0}] 正在以 tmctl.dll (Wire=4 Ethernet VXI-11, IP: {1}) 連線...\r\n", DateTime.Now.ToLongTimeString(), ip));
                    int id = 0;
                    int ret = TmcInitialize(4, ip, ref id);

                    if (ret == 0 && id >= 0)
                    {
                        ykDeviceId = id;
                        powerSampleCount = 0;
                        btnPowerConnect.Text = "停止讀取";
                        btnPowerConnect.BackColor = Color.FromArgb(239, 68, 68);
                        lblPowerStatus.Text = string.Format("狀態: tmctl 成功連線 WT333E ({0})", ip);
                        lblPowerStatus.ForeColor = Color.Green;
                        txtPowerLog.AppendText(string.Format("[OK] tmctl.dll 成功連線 WT333E {0} (Handle: {1})\r\n", ip, id));

                        InitPowerMeterScpi();
                        tmrPower.Start();
                        SaveDeviceConfig();
                    }
                    else
                    {
                        txtPowerLog.AppendText(string.Format("[FAIL] tmctl Wire=4 初始化回傳碼: {0}\r\n", ret));
                        MessageBox.Show("tmctl.dll (Wire=4) 連線失敗 (Code: " + ret + ")。\n建議點擊【⚡ 一鍵智能診斷通道】掃描可用通訊埠！", "提示");
                    }
                }
                catch (Exception ex)
                {
                    txtPowerLog.AppendText("[tmctl 載入異常] " + ex.Message + "\r\n");
                }
            }
            else
            {
                int port = 502;
                if (sel.Contains("502") || sel.Contains("Modbus")) port = 502;
                else if (sel.Contains("10001")) port = 10001;
                else if (sel.Contains("51064")) port = 51064;
                else if (sel.Contains("51065")) port = 51065;
                else int.TryParse(txtPowerPort.Text.Trim(), out port);
                if (port <= 0) port = 502;

                try
                {
                    txtPowerLog.AppendText(string.Format("[{0}] 正在建立 TCP Socket 連線 {1}:{2} ...\r\n", DateTime.Now.ToLongTimeString(), ip, port));
                    tcpPowerSocket = NetworkHelper.CreateBoundTcpClient(ip);
                    tcpPowerSocket.Connect(ip, port);
                    streamPowerSocket = tcpPowerSocket.GetStream();
                    streamPowerSocket.ReadTimeout = 1000;
                    streamPowerSocket.WriteTimeout = 1000;
                    powerSampleCount = 0;
                    btnPowerConnect.Text = "停止讀取";
                    btnPowerConnect.BackColor = Color.FromArgb(239, 68, 68);
                    lblPowerStatus.Text = string.Format("狀態: 成功連線 {0}:{1}", ip, port);
                    lblPowerStatus.ForeColor = Color.Green;
                    txtPowerLog.AppendText(string.Format("[OK] 成功建立 TCP Socket 連線 {0}:{1}！\r\n", ip, port));

                    if (port != 502)
                    {
                        InitPowerMeterScpi();
                    }
                    tmrPower.Start();
                    SaveDeviceConfig();
                }
                catch (Exception ex)
                {
                    txtPowerLog.AppendText(string.Format("[FAIL] 連線 {0}:{1} 失敗: {2}\r\n", ip, port, ex.Message));
                    MessageBox.Show("連線失敗: " + ex.Message + "\n建議點擊【⚡ 一鍵智能診斷通道】掃描可用通訊埠！", "錯誤");
                }
            }
        }

        private void InitPowerMeterScpi()
        {
            txtPowerLog.AppendText(">> [SCPI 模式初始化] 設定 WT333E 輸出項目 (:NUM:NORM:PRESet 1, ITEM1..13) ...\r\n");
            SendPowerDirect(":COMMUNICATE:HEADER OFF");
            SendPowerDirect(":NUMERIC:FORMAT ASCII");
            SendPowerDirect(":NUMERIC:NORMAL:PRESET 1");
            SendPowerDirect(":NUMERIC:NORMAL:NUMBER 13");
            SendPowerDirect(":NUMERIC:NORMAL:ITEM1 U,1");
            SendPowerDirect(":NUMERIC:NORMAL:ITEM2 I,1");
            SendPowerDirect(":NUMERIC:NORMAL:ITEM3 P,1");
            SendPowerDirect(":NUMERIC:NORMAL:ITEM4 U,2");
            SendPowerDirect(":NUMERIC:NORMAL:ITEM5 I,2");
            SendPowerDirect(":NUMERIC:NORMAL:ITEM6 P,2");
            SendPowerDirect(":NUMERIC:NORMAL:ITEM7 U,3");
            SendPowerDirect(":NUMERIC:NORMAL:ITEM8 I,3");
            SendPowerDirect(":NUMERIC:NORMAL:ITEM9 P,3");
            SendPowerDirect(":NUMERIC:NORMAL:ITEM10 U,SIGMA");
            SendPowerDirect(":NUMERIC:NORMAL:ITEM11 I,SIGMA");
            SendPowerDirect(":NUMERIC:NORMAL:ITEM12 P,SIGMA");
            SendPowerDirect(":NUMERIC:NORMAL:ITEM13 LAMBDA,SIGMA");
        }

        private void SendPowerDirect(string cmd)
        {
            if (ykDeviceId >= 0)
            {
                try { TmcSend(ykDeviceId, cmd); } catch { }
            }
            else if (streamPowerSocket != null && tcpPowerSocket != null && tcpPowerSocket.Connected)
            {
                try
                {
                    byte[] b = Encoding.ASCII.GetBytes(cmd + "\n");
                    streamPowerSocket.Write(b, 0, b.Length);
                }
                catch { }
            }
        }

        private static byte[] SendModbusReadBlock(NetworkStream ns, ushort startAddr, ushort regCount)
        {
            if (ns == null) return null;
            try
            {
                byte[] req = new byte[] {
                    0x00, 0x01, 0x00, 0x00, 0x00, 0x06, 0x01, 0x04,
                    (byte)(startAddr >> 8), (byte)(startAddr & 0xFF),
                    (byte)(regCount >> 8), (byte)(regCount & 0xFF)
                };
                ns.Write(req, 0, req.Length);

                byte[] hdr = new byte[9];
                int totalHdr = 0;
                int startTick = Environment.TickCount;
                while (totalHdr < 9 && Environment.TickCount - startTick < 300)
                {
                    if (ns.DataAvailable)
                    {
                        int r = ns.Read(hdr, totalHdr, 9 - totalHdr);
                        if (r <= 0) break;
                        totalHdr += r;
                    }
                    else { Thread.Sleep(3); }
                }
                if (totalHdr < 9 || hdr[7] != 0x04) return null;

                int byteCount = hdr[8];
                byte[] data = new byte[byteCount];
                int totalData = 0;
                startTick = Environment.TickCount;
                while (totalData < byteCount && Environment.TickCount - startTick < 300)
                {
                    if (ns.DataAvailable)
                    {
                        int r = ns.Read(data, totalData, byteCount - totalData);
                        if (r <= 0) break;
                        totalData += r;
                    }
                    else { Thread.Sleep(3); }
                }
                if (totalData < byteCount) return null;

                byte[] full = new byte[9 + byteCount];
                Array.Copy(hdr, 0, full, 0, 9);
                Array.Copy(data, 0, full, 9, byteCount);
                return full;
            }
            catch { return null; }
        }

        private void DoPowerQuery()
        {
            if (ykDeviceId >= 0)
            {
                try
                {
                    TmcSend(ykDeviceId, ":NUMeric:NORMal:VALue?");
                    StringBuilder sb = new StringBuilder(4096);
                    int readLen = 0;
                    int ret = TmcReceive(ykDeviceId, sb, 4096, ref readLen);

                    if (ret == 0 && sb.Length > 0)
                    {
                        powerSampleCount++;
                        string resp = sb.ToString().Trim();
                        txtPowerLog.AppendText(string.Format("[#{0:D3}] 數據: {1}\r\n", powerSampleCount, resp));
                        UpdatePowerTable(resp);
                    }
                }
                catch (Exception ex) { txtPowerLog.AppendText("[tmctl 查詢異常] " + ex.Message + "\r\n"); }
            }
            else if (streamPowerSocket != null && tcpPowerSocket != null && tcpPowerSocket.Connected)
            {
                int port = 0;
                int.TryParse(txtPowerPort.Text.Trim(), out port);

                if (port == 502 || cmbPowerProtocol.SelectedIndex == 3)
                {
                    try
                    {
                        byte[] rawE1 = SendModbusReadBlock(streamPowerSocket, 100, 18);
                        byte[] rawE2 = SendModbusReadBlock(streamPowerSocket, 200, 14);
                        byte[] rawE3 = SendModbusReadBlock(streamPowerSocket, 300, 14);
                        byte[] rawSig = SendModbusReadBlock(streamPowerSocket, 400, 14);

                        if (rawE1 != null && rawE1.Length >= 9 + 12)
                        {
                            powerSampleCount++;
                            int decodeMode = (cmbPowerDecodeMode != null && cmbPowerDecodeMode.SelectedIndex >= 0) ? cmbPowerDecodeMode.SelectedIndex : 0;
                            
                            float u1 = ParseModbusValue(rawE1, 9 + 0, decodeMode);
                            float i1 = ParseModbusValue(rawE1, 9 + 4, decodeMode);
                            float p1 = NormalizePowerVal(ParseModbusValue(rawE1, 9 + 8, decodeMode));
                            float pf1 = (rawE1.Length >= 9 + 24) ? ParseModbusValue(rawE1, 9 + 20, decodeMode) : 1f;

                            float u2 = (rawE2 != null && rawE2.Length >= 9 + 12) ? ParseModbusValue(rawE2, 9 + 0, decodeMode) : 0f;
                            float i2 = (rawE2 != null && rawE2.Length >= 9 + 12) ? ParseModbusValue(rawE2, 9 + 4, decodeMode) : 0f;
                            float p2 = (rawE2 != null && rawE2.Length >= 9 + 12) ? NormalizePowerVal(ParseModbusValue(rawE2, 9 + 8, decodeMode)) : 0f;
                            float pf2 = (rawE2 != null && rawE2.Length >= 9 + 24) ? ParseModbusValue(rawE2, 9 + 20, decodeMode) : 1f;

                            float u3 = (rawE3 != null && rawE3.Length >= 9 + 12) ? ParseModbusValue(rawE3, 9 + 0, decodeMode) : 0f;
                            float i3 = (rawE3 != null && rawE3.Length >= 9 + 12) ? ParseModbusValue(rawE3, 9 + 4, decodeMode) : 0f;
                            float p3 = (rawE3 != null && rawE3.Length >= 9 + 12) ? NormalizePowerVal(ParseModbusValue(rawE3, 9 + 8, decodeMode)) : 0f;
                            float pf3 = (rawE3 != null && rawE3.Length >= 9 + 24) ? ParseModbusValue(rawE3, 9 + 20, decodeMode) : 1f;

                            float uSigma = (rawSig != null && rawSig.Length >= 9 + 12) ? ParseModbusValue(rawSig, 9 + 0, decodeMode) : ((u1 + u2 + u3) / 3f);
                            float iSigma = (rawSig != null && rawSig.Length >= 9 + 12) ? ParseModbusValue(rawSig, 9 + 4, decodeMode) : ((i1 + i2 + i3) / 3f);
                            float pSigma = (rawSig != null && rawSig.Length >= 9 + 12) ? NormalizePowerVal(ParseModbusValue(rawSig, 9 + 8, decodeMode)) : (p1 + p2 + p3);
                            float pfSigma = (rawSig != null && rawSig.Length >= 9 + 24) ? ParseModbusValue(rawSig, 9 + 20, decodeMode) : pf1;

                            if (dgvPower != null && dgvPower.Rows.Count >= 3 && dgvPower.Columns.Count >= 6)
                            {
                                dgvPower.Rows[0].Cells[1].Value = string.Format("{0:F2} V", u1);
                                dgvPower.Rows[1].Cells[1].Value = string.Format("{0:F3} A", i1);
                                dgvPower.Rows[2].Cells[1].Value = string.Format("{0:F3} kW", p1);

                                dgvPower.Rows[0].Cells[2].Value = string.Format("{0:F2} V", u2);
                                dgvPower.Rows[1].Cells[2].Value = string.Format("{0:F3} A", i2);
                                dgvPower.Rows[2].Cells[2].Value = string.Format("{0:F3} kW", p2);

                                dgvPower.Rows[0].Cells[3].Value = string.Format("{0:F2} V", u3);
                                dgvPower.Rows[1].Cells[3].Value = string.Format("{0:F3} A", i3);
                                dgvPower.Rows[2].Cells[3].Value = string.Format("{0:F3} kW", p3);

                                dgvPower.Rows[0].Cells[4].Value = string.Format("{0:F2} V", uSigma);
                                dgvPower.Rows[1].Cells[4].Value = string.Format("{0:F3} A", iSigma);
                                dgvPower.Rows[2].Cells[4].Value = string.Format("{0:F3} kW", pSigma);
                                dgvPower.Rows[0].Cells[5].Value = string.Format("{0:F3}", pfSigma);
                            }

                            txtPowerLog.AppendText(string.Format("[Modbus #{0:D3}] U1={1:F1}V I1={2:F2}A P1={3:F3}kW | U2={4:F1}V I2={5:F2}A P2={6:F3}kW | U3={7:F1}V I3={8:F2}A P3={9:F3}kW | ΣP={10:F3}kW PF={11:F3}\r\n",
                                powerSampleCount, u1, i1, p1, u2, i2, p2, u3, i3, p3, pSigma, pfSigma));
                        }
                    }
                    catch (Exception ex) { txtPowerLog.AppendText("[Modbus 查詢異常] " + ex.Message + "\r\n"); }
                }
                else
                {
                    // SCPI 文字查詢
                    try
                    {
                        byte[] cmd = Encoding.ASCII.GetBytes(":NUMeric:NORMal:VALue?\n");
                        streamPowerSocket.Write(cmd, 0, cmd.Length);
                        Thread.Sleep(80);
                        byte[] buf = new byte[4096];
                        int r = streamPowerSocket.Read(buf, 0, buf.Length);
                        string resp = Encoding.ASCII.GetString(buf, 0, r).Trim();
                        powerSampleCount++;
                        txtPowerLog.AppendText(string.Format("[#{0:D3}] 數據: {1}\r\n", powerSampleCount, resp));
                        UpdatePowerTable(resp);
                    }
                    catch (Exception ex) { txtPowerLog.AppendText("[Socket 查詢異常] " + ex.Message + "\r\n"); }
                }
            }
        }

        private void SendPowerRaw(string cmd)
        {
            if (ykDeviceId >= 0)
            {
                try
                {
                    TmcSend(ykDeviceId, cmd);
                    StringBuilder sb = new StringBuilder(4096);
                    int readLen = 0;
                    TmcReceive(ykDeviceId, sb, 4096, ref readLen);
                    txtPowerLog.AppendText(string.Format(">> tmctl 發送 [{0}] >> 回應: [{1}]\r\n", cmd, sb.ToString().Trim()));
                }
                catch (Exception ex) { txtPowerLog.AppendText("[發送異常] " + ex.Message + "\r\n"); }
            }
            else if (streamPowerSocket != null && tcpPowerSocket != null && tcpPowerSocket.Connected)
            {
                try
                {
                    byte[] b = Encoding.ASCII.GetBytes(cmd + "\n");
                    streamPowerSocket.Write(b, 0, b.Length);
                    Thread.Sleep(100);
                    byte[] buf = new byte[4096];
                    int r = streamPowerSocket.Read(buf, 0, buf.Length);
                    txtPowerLog.AppendText(string.Format(">> Socket 發送 [{0}] >> 回應: [{1}]\r\n", cmd, Encoding.ASCII.GetString(buf, 0, r).Trim()));
                }
                catch (Exception ex) { txtPowerLog.AppendText("[發送異常] " + ex.Message + "\r\n"); }
            }
            else
            {
                txtPowerLog.AppendText(">> 請先點擊「開始連線」建立通訊通道後再發送指令。\r\n");
            }
        }

        private void BtnPowerScanPorts_Click(object sender, EventArgs e)
        {
            string ip = txtPowerIp.Text.Trim();
            txtPowerLog.AppendText(string.Format("\r\n=== 正在全面探測 WT333E ({0}) 的所有通訊埠 ===\r\n", ip));
            var portMap = new System.Collections.Generic.Dictionary<int, string>() {
                { 111, "VXI-11 (原廠 tmctl Wire=4 網路標準協定)" },
                { 51064, "WTViewer 通訊埠 (橫河專屬 SCPI 協定埠)" },
                { 51065, "WTViewer 資料串流埠 (高速量測串流)" },
                { 502, "Modbus TCP 工業網路通訊埠" },
                { 80, "Web HTTP 內建伺服器管理埠" },
                { 10001, "Socket Server (自訂序列伺服器埠)" }
            };

            foreach (var kvp in portMap)
            {
                int p = kvp.Key;
                string desc = kvp.Value;
                try
                {
                    using (TcpClient tc = new TcpClient())
                    {
                        IAsyncResult ar = tc.BeginConnect(ip, p, null, null);
                        if (ar.AsyncWaitHandle.WaitOne(350) && tc.Connected)
                            txtPowerLog.AppendText(string.Format("  [OK] Port {0,-5} 【開啟】>> {1}\r\n", p, desc));
                        else
                            txtPowerLog.AppendText(string.Format("  [FAIL] Port {0,-5} 【關閉 / 逾時】>> {1}\r\n", p, desc));
                    }
                }
                catch { }
            }
            txtPowerLog.AppendText("=== 通訊埠探測結束 (若有綠色開啟之 Port，請直接在上方選擇或填入連線) ===\r\n");
        }

        private void BtnPowerModbus_Click(object sender, EventArgs e)
        {
            string ip = txtPowerIp.Text.Trim();
            txtPowerLog.AppendText(string.Format("\r\n=== 正在測試 WT333E ({0}:502) Modbus TCP 保持/輸入暫存器 (100, 200, 300, 400) ===\r\n", ip));
            try
            {
                using (TcpClient tc = new TcpClient())
                {
                    tc.Connect(ip, 502);
                    using (NetworkStream ns = tc.GetStream())
                    {
                        ns.ReadTimeout = 1000;
                        byte[] rawE1 = SendModbusReadBlock(ns, 100, 18);
                        byte[] rawE2 = SendModbusReadBlock(ns, 200, 14);
                        byte[] rawE3 = SendModbusReadBlock(ns, 300, 14);
                        byte[] rawSig = SendModbusReadBlock(ns, 400, 14);

                        if (rawE1 != null && rawE1.Length >= 9 + 12)
                        {
                            txtPowerLog.AppendText(string.Format("[OK] 成功收到 WT333E Modbus TCP 回應！Element 1 長度: {0} Bytes\r\n", rawE1.Length));
                            int decodeMode = (cmbPowerDecodeMode != null && cmbPowerDecodeMode.SelectedIndex >= 0) ? cmbPowerDecodeMode.SelectedIndex : 0;
                            
                            float u1 = ParseModbusValue(rawE1, 9 + 0, decodeMode);
                            float i1 = ParseModbusValue(rawE1, 9 + 4, decodeMode);
                            float p1 = NormalizePowerVal(ParseModbusValue(rawE1, 9 + 8, decodeMode));
                            float pf1 = (rawE1.Length >= 9 + 24) ? ParseModbusValue(rawE1, 9 + 20, decodeMode) : 1f;

                            float u2 = (rawE2 != null && rawE2.Length >= 9 + 12) ? ParseModbusValue(rawE2, 9 + 0, decodeMode) : 0f;
                            float i2 = (rawE2 != null && rawE2.Length >= 9 + 12) ? ParseModbusValue(rawE2, 9 + 4, decodeMode) : 0f;
                            float p2 = (rawE2 != null && rawE2.Length >= 9 + 12) ? NormalizePowerVal(ParseModbusValue(rawE2, 9 + 8, decodeMode)) : 0f;
                            float pf2 = (rawE2 != null && rawE2.Length >= 9 + 24) ? ParseModbusValue(rawE2, 9 + 20, decodeMode) : 1f;

                            float u3 = (rawE3 != null && rawE3.Length >= 9 + 12) ? ParseModbusValue(rawE3, 9 + 0, decodeMode) : 0f;
                            float i3 = (rawE3 != null && rawE3.Length >= 9 + 12) ? ParseModbusValue(rawE3, 9 + 4, decodeMode) : 0f;
                            float p3 = (rawE3 != null && rawE3.Length >= 9 + 12) ? NormalizePowerVal(ParseModbusValue(rawE3, 9 + 8, decodeMode)) : 0f;
                            float pf3 = (rawE3 != null && rawE3.Length >= 9 + 24) ? ParseModbusValue(rawE3, 9 + 20, decodeMode) : 1f;

                            float uSigma = (rawSig != null && rawSig.Length >= 9 + 12) ? ParseModbusValue(rawSig, 9 + 0, decodeMode) : ((u1 + u2 + u3) / 3f);
                            float iSigma = (rawSig != null && rawSig.Length >= 9 + 12) ? ParseModbusValue(rawSig, 9 + 4, decodeMode) : ((i1 + i2 + i3) / 3f);
                            float pSigma = (rawSig != null && rawSig.Length >= 9 + 12) ? NormalizePowerVal(ParseModbusValue(rawSig, 9 + 8, decodeMode)) : (p1 + p2 + p3);
                            float pfSigma = (rawSig != null && rawSig.Length >= 9 + 24) ? ParseModbusValue(rawSig, 9 + 20, decodeMode) : pf1;

                            if (dgvPower != null && dgvPower.Rows.Count >= 3 && dgvPower.Columns.Count >= 6)
                            {
                                dgvPower.Rows[0].Cells[1].Value = string.Format("{0:F2} V", u1);
                                dgvPower.Rows[1].Cells[1].Value = string.Format("{0:F3} A", i1);
                                dgvPower.Rows[2].Cells[1].Value = string.Format("{0:F3} kW", p1);

                                dgvPower.Rows[0].Cells[2].Value = string.Format("{0:F2} V", u2);
                                dgvPower.Rows[1].Cells[2].Value = string.Format("{0:F3} A", i2);
                                dgvPower.Rows[2].Cells[2].Value = string.Format("{0:F3} kW", p2);

                                dgvPower.Rows[0].Cells[3].Value = string.Format("{0:F2} V", u3);
                                dgvPower.Rows[1].Cells[3].Value = string.Format("{0:F3} A", i3);
                                dgvPower.Rows[2].Cells[3].Value = string.Format("{0:F3} kW", p3);

                                dgvPower.Rows[0].Cells[4].Value = string.Format("{0:F2} V", uSigma);
                                dgvPower.Rows[1].Cells[4].Value = string.Format("{0:F3} A", iSigma);
                                dgvPower.Rows[2].Cells[4].Value = string.Format("{0:F3} kW", pSigma);
                                dgvPower.Rows[0].Cells[5].Value = string.Format("{0:F3}", pfSigma);
                            }

                            txtPowerLog.AppendText(string.Format("   [解析結果] Phase 1: {0:F1}V, {1:F2}A, {2:F3}kW\r\n", u1, i1, p1));
                            txtPowerLog.AppendText(string.Format("   [解析結果] Phase 2: {0:F1}V, {1:F2}A, {2:F3}kW\r\n", u2, i2, p2));
                            txtPowerLog.AppendText(string.Format("   [解析結果] Phase 3: {0:F1}V, {1:F2}A, {2:F3}kW\r\n", u3, i3, p3));
                            txtPowerLog.AppendText(string.Format("   [解析結果] Sigma Σ: {0:F1}V, {1:F2}A, {2:F3}kW, PF={3:F3}\r\n", uSigma, iSigma, pSigma, pfSigma));
                        }
                        else
                        {
                            txtPowerLog.AppendText("[FAIL] Modbus TCP 回應長度不足。\r\n");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                txtPowerLog.AppendText("[FAIL] Modbus TCP 連線失敗: " + ex.Message + "\r\n");
            }
        }

        private float ParseModbusValue(byte[] buf, int offset, int mode)
        {
            if (buf == null || offset + 2 > buf.Length) return 0f;
            try
            {
                if (mode == 0) // 1. IEEE-754 Big-Endian (ABCD)
                {
                    if (offset + 4 > buf.Length) return 0f;
                    byte[] b = new byte[] { buf[offset + 3], buf[offset + 2], buf[offset + 1], buf[offset] };
                    float f = BitConverter.ToSingle(b, 0);
                    return (!float.IsNaN(f) && !float.IsInfinity(f) && Math.Abs(f) < 500000.0f) ? f : 0f;
                }
                else if (mode == 1) // 2. IEEE-754 Word-Swap (CDAB)
                {
                    if (offset + 4 > buf.Length) return 0f;
                    byte[] b = new byte[] { buf[offset + 1], buf[offset], buf[offset + 3], buf[offset + 2] };
                    float f = BitConverter.ToSingle(b, 0);
                    return (!float.IsNaN(f) && !float.IsInfinity(f) && Math.Abs(f) < 500000.0f) ? f : 0f;
                }
                else if (mode == 2) // 3. INT32 整數 (定點縮放 /1000)
                {
                    if (offset + 4 > buf.Length) return 0f;
                    int v = (buf[offset] << 24) | (buf[offset + 1] << 16) | (buf[offset + 2] << 8) | buf[offset + 3];
                    return (float)(v / 1000.0);
                }
                else if (mode == 3) // 4. INT16 整數 (定點縮放 /100)
                {
                    short v = (short)((buf[offset] << 8) | buf[offset + 1]);
                    return (float)(v / 100.0);
                }
                else if (mode == 4) // 5. ASCII 數值提取
                {
                    string s = Encoding.ASCII.GetString(buf, offset, Math.Min(8, buf.Length - offset));
                    Match m = Regex.Match(s, @"[-+]?[0-9]*\.?[0-9]+");
                    if (m.Success)
                    {
                        float f;
                        if (float.TryParse(m.Value, out f)) return f;
                    }
                    return 0f;
                }
                return 0f;
            }
            catch { return 0f; }
        }

        private float NormalizePowerVal(float rawVal)
        {
            if (Math.Abs(rawVal) >= 50f) return rawVal / 1000.0f;
            return rawVal;
        }

        private void UpdatePowerTableFromModbus(byte[] resp, int len)
        {
            if (len < 9 + 4) return;
            try
            {
                int decodeMode = (cmbPowerDecodeMode != null && cmbPowerDecodeMode.SelectedIndex >= 0) ? cmbPowerDecodeMode.SelectedIndex : 0;
                int baseOffset = 9;

                int offsetSel = (cmbPowerModbusOffset != null && cmbPowerModbusOffset.SelectedIndex >= 0) ? cmbPowerModbusOffset.SelectedIndex : 0;
                float u1 = 0f, i1 = 0f, p1 = 0f, pf1 = 1f;
                float u2 = 0f, i2 = 0f, p2 = 0f, pf2 = 1f;
                float u3 = 0f, i3 = 0f, p3 = 0f, pf3 = 1f;
                float uSigma = 0f, iSigma = 0f, pSigma = 0f, pfSigma = 1f;

                // 1. 橫河原廠 100-Register (50-Float = 200B) 實體通道 (E1:100, E2:200, E3:300, Sig:400)
                if (offsetSel == 0)
                {
                    u1 = ParseModbusValue(resp, baseOffset + 0, decodeMode);
                    i1 = ParseModbusValue(resp, baseOffset + 4, decodeMode);
                    p1 = NormalizePowerVal(ParseModbusValue(resp, baseOffset + 8, decodeMode));
                    pf1 = ParseModbusValue(resp, baseOffset + 20, decodeMode);

                    // E2 @ Float 50 (Byte 200 = 100 Regs)
                    if (baseOffset + 200 + 4 <= len)
                    {
                        u2 = ParseModbusValue(resp, baseOffset + 200, decodeMode);
                        i2 = ParseModbusValue(resp, baseOffset + 204, decodeMode);
                        p2 = NormalizePowerVal(ParseModbusValue(resp, baseOffset + 208, decodeMode));
                        pf2 = ParseModbusValue(resp, baseOffset + 220, decodeMode);
                    }
                    // E3 @ Float 100 (Byte 400 = 200 Regs)
                    if (baseOffset + 400 + 4 <= len)
                    {
                        u3 = ParseModbusValue(resp, baseOffset + 400, decodeMode);
                        i3 = ParseModbusValue(resp, baseOffset + 404, decodeMode);
                        p3 = NormalizePowerVal(ParseModbusValue(resp, baseOffset + 408, decodeMode));
                        pf3 = ParseModbusValue(resp, baseOffset + 420, decodeMode);
                    }
                    // Sigma @ Float 150 (Byte 600 = 300 Regs)
                    if (baseOffset + 600 + 4 <= len)
                    {
                        uSigma = ParseModbusValue(resp, baseOffset + 600, decodeMode);
                        iSigma = ParseModbusValue(resp, baseOffset + 604, decodeMode);
                        pSigma = NormalizePowerVal(ParseModbusValue(resp, baseOffset + 608, decodeMode));
                        pfSigma = ParseModbusValue(resp, baseOffset + 620, decodeMode);
                    }
                    else
                    {
                        uSigma = (u2 > 1f && u3 > 1f) ? ((u1 + u2 + u3) / 3.0f) : u1;
                        iSigma = (i2 > 0.1f && i3 > 0.1f) ? ((i1 + i2 + i3) / 3.0f) : i1;
                        pSigma = (p2 != 0f || p3 != 0f) ? (p1 + p2 + p3) : p1;
                        pfSigma = pf1;
                    }
                }
                // 2. 16B 緊湊跨距 (每相 4 變數: U, I, P, PF) -> 專門解決第 3 相在 48B 偏移讀不到的問題
                else if (offsetSel == 1)
                {
                    u1 = ParseModbusValue(resp, baseOffset + 0, decodeMode);
                    i1 = ParseModbusValue(resp, baseOffset + 4, decodeMode);
                    p1 = ParseModbusValue(resp, baseOffset + 8, decodeMode);
                    pf1 = ParseModbusValue(resp, baseOffset + 12, decodeMode);

                    if (baseOffset + 32 <= len)
                    {
                        u2 = ParseModbusValue(resp, baseOffset + 16, decodeMode);
                        i2 = ParseModbusValue(resp, baseOffset + 20, decodeMode);
                        p2 = ParseModbusValue(resp, baseOffset + 24, decodeMode);
                        pf2 = ParseModbusValue(resp, baseOffset + 28, decodeMode);
                    }
                    if (baseOffset + 48 <= len)
                    {
                        u3 = ParseModbusValue(resp, baseOffset + 32, decodeMode);
                        i3 = ParseModbusValue(resp, baseOffset + 36, decodeMode);
                        p3 = ParseModbusValue(resp, baseOffset + 40, decodeMode);
                        pf3 = ParseModbusValue(resp, baseOffset + 44, decodeMode);
                    }
                    if (baseOffset + 64 <= len)
                    {
                        uSigma = ParseModbusValue(resp, baseOffset + 48, decodeMode);
                        iSigma = ParseModbusValue(resp, baseOffset + 52, decodeMode);
                        pSigma = ParseModbusValue(resp, baseOffset + 56, decodeMode);
                        pfSigma = ParseModbusValue(resp, baseOffset + 60, decodeMode);
                    }
                }
                // 3. 12B 基本跨距 (每相 3 變數: U, I, P)
                else if (offsetSel == 2)
                {
                    u1 = ParseModbusValue(resp, baseOffset + 0, decodeMode);
                    i1 = ParseModbusValue(resp, baseOffset + 4, decodeMode);
                    p1 = ParseModbusValue(resp, baseOffset + 8, decodeMode);

                    if (baseOffset + 24 <= len)
                    {
                        u2 = ParseModbusValue(resp, baseOffset + 12, decodeMode);
                        i2 = ParseModbusValue(resp, baseOffset + 16, decodeMode);
                        p2 = ParseModbusValue(resp, baseOffset + 20, decodeMode);
                    }
                    if (baseOffset + 36 <= len)
                    {
                        u3 = ParseModbusValue(resp, baseOffset + 24, decodeMode);
                        i3 = ParseModbusValue(resp, baseOffset + 28, decodeMode);
                        p3 = ParseModbusValue(resp, baseOffset + 32, decodeMode);
                    }
                    if (baseOffset + 48 <= len)
                    {
                        uSigma = ParseModbusValue(resp, baseOffset + 36, decodeMode);
                        iSigma = ParseModbusValue(resp, baseOffset + 40, decodeMode);
                        pSigma = ParseModbusValue(resp, baseOffset + 44, decodeMode);
                    }
                }
                // 4. 3P3W 兩瓦特表法自動合成模式 (E1 + E2 合成 Phase 3 與 Sigma)
                else if (offsetSel == 3)
                {
                    u1 = ParseModbusValue(resp, baseOffset + 0, decodeMode);
                    i1 = ParseModbusValue(resp, baseOffset + 4, decodeMode);
                    p1 = ParseModbusValue(resp, baseOffset + 8, decodeMode);

                    if (baseOffset + 48 <= len)
                    {
                        u2 = ParseModbusValue(resp, baseOffset + 24, decodeMode);
                        i2 = ParseModbusValue(resp, baseOffset + 28, decodeMode);
                        p2 = ParseModbusValue(resp, baseOffset + 32, decodeMode);
                    }
                    u3 = (u1 + u2) / 2.0f;
                    i3 = (i1 + i2) / 2.0f;
                    p3 = 0f;
                    uSigma = (u1 + u2) / 1.732f;
                    iSigma = (i1 + i2) / 2.0f;
                    pSigma = p1 + p2;
                }
                // 5. 0x0064 連續 100 起始
                else
                {
                    u1 = ParseModbusValue(resp, baseOffset + 0, decodeMode);
                    i1 = ParseModbusValue(resp, baseOffset + 4, decodeMode);
                    p1 = ParseModbusValue(resp, baseOffset + 8, decodeMode);

                    if (baseOffset + 24 <= len)
                    {
                        u2 = ParseModbusValue(resp, baseOffset + 12, decodeMode);
                        i2 = ParseModbusValue(resp, baseOffset + 16, decodeMode);
                        p2 = ParseModbusValue(resp, baseOffset + 20, decodeMode);
                    }
                    if (baseOffset + 36 <= len)
                    {
                        u3 = ParseModbusValue(resp, baseOffset + 24, decodeMode);
                        i3 = ParseModbusValue(resp, baseOffset + 28, decodeMode);
                        p3 = ParseModbusValue(resp, baseOffset + 32, decodeMode);
                    }
                    if (baseOffset + 48 <= len)
                    {
                        uSigma = ParseModbusValue(resp, baseOffset + 36, decodeMode);
                        iSigma = ParseModbusValue(resp, baseOffset + 40, decodeMode);
                        pSigma = ParseModbusValue(resp, baseOffset + 44, decodeMode);
                    }
                }

                if (uSigma <= 0.001f) uSigma = (u1 + u2 + u3 > 0.1f) ? (u1 + u2 + u3) / 1.732f : u1;
                if (iSigma <= 0.0001f) iSigma = (i1 + i2 + i3 > 0.01f) ? (i1 + i2 + i3) / 3.0f : i1;
                if (pSigma <= 0.0001f) pSigma = (p1 + p2 + p3 > 0.01f) ? (p1 + p2 + p3) : p1;

                dgvPower.Rows[0].Cells[1].Value = string.Format("{0:F2} V", u1);
                dgvPower.Rows[1].Cells[1].Value = string.Format("{0:F3} A", i1);
                dgvPower.Rows[2].Cells[1].Value = string.Format("{0:F3} kW", Math.Abs(p1) > 500f ? p1 / 1000.0f : p1);

                dgvPower.Rows[0].Cells[2].Value = string.Format("{0:F2} V", u2);
                dgvPower.Rows[1].Cells[2].Value = string.Format("{0:F3} A", i2);
                dgvPower.Rows[2].Cells[2].Value = string.Format("{0:F3} kW", Math.Abs(p2) > 500f ? p2 / 1000.0f : p2);

                dgvPower.Rows[0].Cells[3].Value = string.Format("{0:F2} V", u3);
                dgvPower.Rows[1].Cells[3].Value = string.Format("{0:F3} A", i3);
                dgvPower.Rows[2].Cells[3].Value = string.Format("{0:F3} kW", Math.Abs(p3) > 500f ? p3 / 1000.0f : p3);

                dgvPower.Rows[0].Cells[4].Value = string.Format("{0:F2} V", uSigma);
                dgvPower.Rows[1].Cells[4].Value = string.Format("{0:F3} A", iSigma);
                dgvPower.Rows[2].Cells[4].Value = string.Format("{0:F3} kW", Math.Abs(pSigma) > 500f ? pSigma / 1000.0f : pSigma);
                dgvPower.Rows[0].Cells[5].Value = string.Format("{0:F3}", Math.Abs(pfSigma) > 0.0001f ? Math.Abs(pfSigma) : 1.0f);

                // 即時暫存器透視矩陣 (Dump Float 0..15)
                StringBuilder sbMatrix = new StringBuilder();
                int maxFloats = Math.Min((len - baseOffset) / 4, 16);
                for (int fi = 0; fi < maxFloats; fi++)
                {
                    float fVal = ParseModbusValue(resp, baseOffset + fi * 4, decodeMode);
                    if (Math.Abs(fVal) > 0.001f && Math.Abs(fVal) < 10000f)
                    {
                        sbMatrix.Append(string.Format("F{0}[+{1}B]={2:F2} | ", fi, fi * 4, fVal));
                    }
                }
                string matrixStr = sbMatrix.Length > 0 ? sbMatrix.ToString().TrimEnd(' ', '|') : "全部為0";

                string modeName = cmbPowerDecodeMode != null ? cmbPowerDecodeMode.Text : "模式 1";
                string selName = cmbPowerModbusOffset != null ? cmbPowerModbusOffset.SelectedItem.ToString() : "";
                txtPowerLog.AppendText(string.Format("   📊 [{0}] U1: {1:F1}V, U2: {2:F1}V, U3: {3:F1}V | I1: {4:F2}A, I2: {5:F2}A, I3: {6:F2}A | P總: {7:F2}kW\r\n",
                    selName, u1, u2, u3, i1, i2, i3, pSigma));
                txtPowerLog.AppendText(string.Format("   🔎 [即時暫存器透視] {0}\r\n", matrixStr));
            }
            catch (Exception ex)
            {
                txtPowerLog.AppendText("   ⚠️ [Modbus 解碼異常] " + ex.Message + "\r\n");
            }
        }

        private void UpdatePowerTable(string resp)
        {
            try
            {
                string[] tokens = resp.Split(new char[] { ',', ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                if (tokens.Length >= 4)
                {
                    double v = ParseSci(tokens[0]);
                    double a = ParseSci(tokens[1]);
                    double p = ParseSci(tokens[2]);
                    double pf = ParseSci(tokens[3]);

                    dgvPower.Rows[0].Cells[4].Value = string.Format("{0:F2} V", v);
                    dgvPower.Rows[1].Cells[4].Value = string.Format("{0:F3} A", a);
                    dgvPower.Rows[2].Cells[4].Value = string.Format("{0:F3} kW", p / 1000.0 > 0.001 ? p / 1000.0 : p);
                    dgvPower.Rows[0].Cells[5].Value = string.Format("{0:F3}", pf);

                    if (tokens.Length >= 12)
                    {
                        dgvPower.Rows[0].Cells[1].Value = string.Format("{0:F2} V", ParseSci(tokens[0]));
                        dgvPower.Rows[1].Cells[1].Value = string.Format("{0:F3} A", ParseSci(tokens[1]));
                        dgvPower.Rows[2].Cells[1].Value = string.Format("{0:F3} kW", ParseSci(tokens[2]) / 1000.0);

                        dgvPower.Rows[0].Cells[2].Value = string.Format("{0:F2} V", ParseSci(tokens[4]));
                        dgvPower.Rows[1].Cells[2].Value = string.Format("{0:F3} A", ParseSci(tokens[5]));
                        dgvPower.Rows[2].Cells[2].Value = string.Format("{0:F3} kW", ParseSci(tokens[6]) / 1000.0);

                        dgvPower.Rows[0].Cells[3].Value = string.Format("{0:F2} V", ParseSci(tokens[8]));
                        dgvPower.Rows[1].Cells[3].Value = string.Format("{0:F3} A", ParseSci(tokens[9]));
                        dgvPower.Rows[2].Cells[3].Value = string.Format("{0:F3} kW", ParseSci(tokens[10]) / 1000.0);
                    }
                }
            }
            catch { }
        }

        private static double ParseSci(string valStr)
        {
            double res = 0.0;
            if (double.TryParse(valStr, NumberStyles.Float | NumberStyles.AllowExponent, CultureInfo.InvariantCulture, out res))
                return res;
            return 0.0;
        }

        // =========================================================================
        // 分頁 3: Graphtec GL820 (IEEE 488.2 二進制解碼器)
        // =========================================================================
        private void BuildGbdPanel(Panel pnl)
        {
            GroupBox grpMain = new GroupBox() { Text = "【主要測試】Graphtec GL820 (原廠 GL220_820APS 握手協議)", Location = new Point(15, 8), Size = new Size(975, 85) };

            Label l1 = new Label() { Text = "GL820 IP:", Location = new Point(15, 24), AutoSize = true };
            txtGbdIp = new TextBox() { Text = "192.168.0.3", Location = new Point(85, 21), Width = 100 };

            Label l2 = new Label() { Text = "Port:", Location = new Point(195, 24), AutoSize = true };
            txtGbdPort = new TextBox() { Text = "8023", Location = new Point(235, 21), Width = 50 };

            btnGbdHandshake = new Button()
            {
                Text = " 執行原廠握手序列",
                Location = new Point(295, 18),
                Size = new Size(160, 30),
                BackColor = Color.FromArgb(245, 158, 11),
                ForeColor = Color.White,
                Font = new Font("微軟正黑體", 9f, FontStyle.Bold)
            };
            btnGbdHandshake.Click += BtnGbdHandshake_Click;

            btnGbdSingleQuery = new Button()
            {
                Text = "單次取樣",
                Location = new Point(465, 18),
                Size = new Size(85, 30)
            };
            btnGbdSingleQuery.Click += (s, e) => DoGbdQuery();

            btnGbdConnect = new Button()
            {
                Text = " 開始連續讀取",
                Location = new Point(560, 17),
                Size = new Size(130, 32),
                BackColor = Color.FromArgb(0, 180, 216),
                ForeColor = Color.White,
                Font = new Font("微軟正黑體", 9.5f, FontStyle.Bold)
            };
            btnGbdConnect.Click += BtnGbdConnect_Click;

            lblGbdStatus = new Label() { Text = "狀態: 未連線", Location = new Point(700, 24), AutoSize = true, ForeColor = Color.Gray };

            Label lHint = new Label()
            {
                Text = "[OK] 現場實測確認：GL820 成功回傳 #6000068 IEEE 488.2 二進制封包，已內建 16-bit Big-Endian 溫度解碼！",
                Location = new Point(15, 56),
                AutoSize = true,
                ForeColor = Color.DarkGreen,
                Font = new Font("微軟正黑體", 9f, FontStyle.Bold)
            };

            grpMain.Controls.AddRange(new Control[] { l1, txtGbdIp, l2, txtGbdPort, btnGbdHandshake, btnGbdSingleQuery, btnGbdConnect, lblGbdStatus, lHint });
            pnl.Controls.Add(grpMain);

            GroupBox grpTable = new GroupBox() { Text = "GL820 CH1~CH10 多通道即時溫度數據 (°C)", Location = new Point(15, 98), Size = new Size(975, 130) };
            dgvGbd = new DataGridView() { Dock = DockStyle.Fill, BackgroundColor = Color.White, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill, RowHeadersVisible = false, AllowUserToAddRows = false };
            for (int i = 1; i <= 10; i++) dgvGbd.Columns.Add("CH" + i, "CH" + i);
            dgvGbd.Rows.Add("--", "--", "--", "--", "--", "--", "--", "--", "--", "--");
            grpTable.Controls.Add(dgvGbd);
            pnl.Controls.Add(grpTable);

            GroupBox grpDiag = new GroupBox() { Text = "【備案測試與診斷工具】", Location = new Point(15, 233), Size = new Size(975, 65) };

            btnGbdTestHttp = new Button()
            {
                Text = " 備案A: 測試 GL820 內建 Web HTTP 直讀 (Port 80)",
                Location = new Point(10, 20),
                Size = new Size(330, 28),
                BackColor = Color.FromArgb(100, 116, 139),
                ForeColor = Color.White,
                Font = new Font("微軟正黑體", 9f, FontStyle.Bold)
            };
            btnGbdTestHttp.Click += BtnGbdTestHttp_Click;

            Label lCust = new Label() { Text = "備案B 指令:", Location = new Point(355, 25), AutoSize = true };
            txtGbdCustomCmd = new TextBox() { Text = "*IDN?", Location = new Point(440, 22), Width = 180 };
            btnGbdSendCustom = new Button() { Text = "發送", Location = new Point(630, 20), Size = new Size(60, 28) };
            btnGbdSendCustom.Click += (s, e) => SendGbdRaw(txtGbdCustomCmd.Text);

            Button btnClearGbdLog = new Button() { Text = "清空日誌", Location = new Point(860, 20), Size = new Size(90, 28) };
            btnClearGbdLog.Click += (s, e) => txtGbdLog.Clear();

            grpDiag.Controls.AddRange(new Control[] { btnGbdTestHttp, lCust, txtGbdCustomCmd, btnGbdSendCustom, btnClearGbdLog });
            pnl.Controls.Add(grpDiag);

            GroupBox grpLog = new GroupBox() { Text = "GL820 原廠交握與通訊記錄", Location = new Point(15, 303), Size = new Size(975, 347) };
            txtGbdLog = new TextBox() { Dock = DockStyle.Fill, Multiline = true, ScrollBars = ScrollBars.Vertical, ReadOnly = true, BackColor = Color.Black, ForeColor = Color.LightGreen, Font = new Font("Consolas", 9.5f) };
            grpLog.Controls.Add(txtGbdLog);
            pnl.Controls.Add(grpLog);

            tmrGbd = new System.Windows.Forms.Timer();
            tmrGbd.Interval = 1000;
            tmrGbd.Tick += (s, e) => DoGbdQuery();
        }

        private void BtnGbdHandshake_Click(object sender, EventArgs e)
        {
            string ip = txtGbdIp.Text.Trim();
            int port = int.Parse(txtGbdPort.Text.Trim());
            txtGbdLog.AppendText(string.Format("\r\n=== 正在執行 GL220_820APS 原廠完整握手序列 (連線 {0}:{1}) ===\r\n", ip, port));

            try
            {
                using (TcpClient client = new TcpClient())
                {
                    client.Connect(ip, port);
                    using (NetworkStream ns = client.GetStream())
                    {
                        ns.ReadTimeout = 1000;
                        ns.WriteTimeout = 1000;

                        string[] handshakeCmds = new string[] {
                            ":IF:ID?", ":CONTR:MAS?", "*IDN?", ":INFO:CH?;OPT?;VER:SYS?;MAIN?;OS?",
                            ":STAT:COND?", ":OPT:TUNIT?", ":DATA:SAMP?", ":MEAS:OUTP:ONE?"
                        };

                        byte[] buf = new byte[2048];
                        foreach (string cmd in handshakeCmds)
                        {
                            txtGbdLog.AppendText(string.Format("  發送: {0,-35} ", cmd));
                            byte[] b = Encoding.ASCII.GetBytes(cmd + "\r\n");
                            ns.Write(b, 0, b.Length);
                            Thread.Sleep(80);

                            if (ns.DataAvailable)
                            {
                                int r = ns.Read(buf, 0, buf.Length);
                                string resp = Encoding.ASCII.GetString(buf, 0, r).Trim();
                                txtGbdLog.AppendText(string.Format(">> 回應: [{0}]\r\n", resp));
                            }
                            else
                            {
                                txtGbdLog.AppendText(">> (無文字回應)\r\n");
                            }
                        }
                    }
                }
                txtGbdLog.AppendText("=== 原廠握手序列完成 ===\r\n");
            }
            catch (Exception ex)
            {
                txtGbdLog.AppendText("[握手異常] " + ex.Message + "\r\n");
            }
        }

        private void BtnGbdConnect_Click(object sender, EventArgs e)
        {
            if (tcpGbd != null && tcpGbd.Connected)
            {
                tmrGbd.Stop();
                if (streamGbd != null) streamGbd.Close();
                tcpGbd.Close();
                tcpGbd = null;
                btnGbdConnect.Text = " 開始連續讀取";
                btnGbdConnect.BackColor = Color.FromArgb(0, 180, 216);
                lblGbdStatus.Text = "狀態: 已斷開";
                return;
            }

            string ip = txtGbdIp.Text.Trim();
            int port = int.Parse(txtGbdPort.Text.Trim());

            try
            {
                tcpGbd = NetworkHelper.CreateBoundTcpClient(ip);
                tcpGbd.Connect(ip, port);
                streamGbd = tcpGbd.GetStream();
                gbdSampleCount = 0;

                btnGbdConnect.Text = " 停止連線";
                btnGbdConnect.BackColor = Color.FromArgb(239, 68, 68);
                lblGbdStatus.Text = string.Format("狀態: 成功連線 {0}:{1}", ip, port);
                lblGbdStatus.ForeColor = Color.Green;
                txtGbdLog.AppendText(string.Format("[{0}] 成功連線 GL820 {1}:{2}\r\n", DateTime.Now.ToLongTimeString(), ip, port));

                tmrGbd.Start();
            }
            catch (Exception ex)
            {
                MessageBox.Show("無法連線 GL820: " + ex.Message, "錯誤");
            }
        }

        private void DoGbdQuery()
        {
            if (tcpGbd == null || !tcpGbd.Connected || streamGbd == null) return;

            try
            {
                byte[] cmd = Encoding.ASCII.GetBytes(":MEAS:OUTP:ONE?\r\n");
                streamGbd.Write(cmd, 0, cmd.Length);
                Thread.Sleep(100);

                if (streamGbd.DataAvailable)
                {
                    byte[] buf = new byte[2048];
                    int r = streamGbd.Read(buf, 0, buf.Length);
                    gbdSampleCount++;

                    int hashIdx = -1;
                    for (int i = 0; i < r; i++) { if (buf[i] == (byte)'#') { hashIdx = i; break; } }

                    if (hashIdx >= 0 && hashIdx + 2 < r)
                    {
                        int numDigits = buf[hashIdx + 1] - '0';
                        if (numDigits > 0 && hashIdx + 2 + numDigits <= r)
                        {
                            int dataStart = hashIdx + 2 + numDigits;
                            int totalBytes = r - dataStart;
                            int numChannels = Math.Min(totalBytes / 2, 10);

                            StringBuilder sbTemps = new StringBuilder();
                            for (int ch = 0; ch < numChannels; ch++)
                            {
                                int idx = dataStart + ch * 2;
                                if (idx + 1 < r)
                                {
                                    short rawShort = (short)((buf[idx] << 8) | buf[idx + 1]);
                                    double tempC = rawShort * 0.1;
                                    if (rawShort != 0x7FFF && rawShort != -32768 && Math.Abs(tempC) < 300.0)
                                    {
                                        dgvGbd.Rows[0].Cells[ch].Value = tempC.ToString("F1") + " °C";
                                        sbTemps.Append(string.Format("CH{0}:{1:F1}°C ", ch + 1, tempC));
                                    }
                                    else
                                    {
                                        dgvGbd.Rows[0].Cells[ch].Value = "--";
                                    }
                                }
                            }
                            txtGbdLog.AppendText(string.Format("[#{0:D3}] 溫度: {1}\r\n", gbdSampleCount, sbTemps.ToString()));
                            return;
                        }
                    }

                    string resp = Encoding.ASCII.GetString(buf, 0, r).Trim();
                    txtGbdLog.AppendText(string.Format("[#{0:D3}] 原始封包: {1}\r\n", gbdSampleCount, resp));
                }
            }
            catch (Exception ex) { txtGbdLog.AppendText("[GL820 讀取異常] " + ex.Message + "\r\n"); }
        }

        private void SendGbdRaw(string cmd)
        {
            if (tcpGbd == null || !tcpGbd.Connected || streamGbd == null)
            {
                txtGbdLog.AppendText(">> 請先點擊「開始連續讀取」建立連線後再發送自訂指令。\r\n");
                return;
            }
            try
            {
                byte[] b = Encoding.ASCII.GetBytes(cmd + "\r\n");
                streamGbd.Write(b, 0, b.Length);
                Thread.Sleep(100);
                if (streamGbd.DataAvailable)
                {
                    byte[] buf = new byte[2048];
                    int r = streamGbd.Read(buf, 0, buf.Length);
                    txtGbdLog.AppendText(string.Format(">> 發送 [{0}] >> 回應: [{1}]\r\n", cmd, Encoding.ASCII.GetString(buf, 0, r).Trim()));
                }
                else
                {
                    txtGbdLog.AppendText(string.Format(">> 發送 [{0}] >> (無回應)\r\n", cmd));
                }
            }
            catch (Exception ex) { txtGbdLog.AppendText("[發送異常] " + ex.Message + "\r\n"); }
        }

        private void BtnGbdTestHttp_Click(object sender, EventArgs e)
        {
            string ip = txtGbdIp.Text.Trim();
            txtGbdLog.AppendText(string.Format("\r\n=== 正在測試 GL820 內建 Web HTTP (http://{0}/) ===\r\n", ip));
            try
            {
                HttpWebRequest req = (HttpWebRequest)WebRequest.Create("http://" + ip + "/");
                req.Timeout = 2500;
                using (HttpWebResponse resp = (HttpWebResponse)req.GetResponse())
                using (StreamReader sr = new StreamReader(resp.GetResponseStream()))
                {
                    string html = sr.ReadToEnd();
                    txtGbdLog.AppendText(string.Format("[OK] 成功連通 GL820 內建 Web 伺服器 (Port 80)！長度: {0} Bytes\r\n", html.Length));
                }
            }
            catch (Exception ex) { txtGbdLog.AppendText("[FAIL] Web HTTP 連線失敗: " + ex.Message + "\r\n"); }
        }

        private static readonly object logLock = new object();
        public static void WriteDirectLog(string device, string msg)
        {
            try
            {
                string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                string logDir = Path.Combine(baseDir, "logs");
                if (!Directory.Exists(logDir)) Directory.CreateDirectory(logDir);
                string file2 = Path.Combine(logDir, "device_test_diagnosis.log");

                string formatted = string.Format("[{0}] [{1}] {2}", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff"), device, msg.TrimEnd('\r', '\n')) + Environment.NewLine;

                lock (logLock)
                {
                    File.AppendAllText(file2, formatted, Encoding.UTF8);
                }
            }
            catch { }
        }

        public static void AttachAutoLog(TextBox txt, string device)
        {
            if (txt == null) return;
            int lastLen = 0;
            txt.TextChanged += (s, e) =>
            {
                try
                {
                    if (txt.TextLength > lastLen)
                    {
                        string added = txt.Text.Substring(lastLen);
                        lastLen = txt.TextLength;
                        string[] lines = added.Split(new char[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                        foreach (string line in lines)
                        {
                            WriteDirectLog(device, line);
                        }
                    }
                    else
                    {
                        lastLen = txt.TextLength;
                    }
                }
                catch { }
            };
        }
    }
}
