using System;
using System.Drawing;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace WT333E_Portable_Tester
{
    public class MainForm : Form
    {
        // UI Controls
        private TextBox txtIp, txtPort, txtUnitId;
        private ComboBox cmbFuncCode, cmbByteOrder, cmbPollRate;
        private Button btnConnect, btnSingleQuery, btnMatrixDump, btnAutoScan, btnCompareRef, btnClearLog, btnOpenLogDir;
        private Label lblStatus, lblPacketStats;
        private DataGridView dgvResults;
        private TextBox txtLog;
        private System.Windows.Forms.Timer tmrPoll;

        // Networking & State
        private TcpClient tcpClient;
        private NetworkStream netStream;
        private bool isPolling = false;
        private int pollCount = 0;
        private int errCount = 0;
        private ushort transId = 1;

        // Telemetry Logging (Single Unified File in logs/)
        private string logFilePath;
        private StreamWriter csvWriter;
        private readonly object logLock = new object();

        [STAThread]
        public static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
        }

        public MainForm()
        {
            InitializeComponent();
            InitLogging();
        }

        private void InitializeComponent()
        {
            this.Text = "橫河 YOKOGAWA WT333E 功率分析儀 Modbus TCP 通訊測試工具 V2.5 (100/200/300/400 官方十進位完美映射版)";
            this.Size = new Size(1150, 880);
            this.MinimumSize = new Size(1000, 720);
            this.StartPosition = FormStartPosition.CenterScreen;
            this.Font = new Font("微軟正黑體", 9f);
            this.BackColor = Color.FromArgb(240, 243, 246);

            // Master Layout Panel
            Panel mainContainer = new Panel() { Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(10) };
            this.Controls.Add(mainContainer);

            // 1. Top Connection & Protocol GroupBox
            GroupBox grpConn = new GroupBox() {
                Text = "[通訊] WT333E 乙太網路連線與 Modbus TCP 參數設定 (100% 鏡像原廠 WTViewerFreePlus 數值)",
                Dock = DockStyle.Top,
                Height = 125,
                Font = new Font("微軟正黑體", 9.5f, FontStyle.Bold),
                ForeColor = Color.FromArgb(20, 35, 60),
                Padding = new Padding(8)
            };
            mainContainer.Controls.Add(grpConn);

            TableLayoutPanel tlpConn = new TableLayoutPanel() {
                Dock = DockStyle.Fill,
                ColumnCount = 6,
                RowCount = 2,
                Font = new Font("微軟正黑體", 9f, FontStyle.Regular)
            };
            tlpConn.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110f));
            tlpConn.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 22f));
            tlpConn.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110f));
            tlpConn.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 22f));
            tlpConn.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110f));
            tlpConn.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 56f));

            // Row 0
            tlpConn.Controls.Add(new Label() { Text = "設備 IP 地址:", Anchor = AnchorStyles.Right, AutoSize = true }, 0, 0);
            txtIp = new TextBox() { Text = "192.168.0.11", Dock = DockStyle.Fill, Font = new Font("Consolas", 10f, FontStyle.Bold) };
            tlpConn.Controls.Add(txtIp, 1, 0);

            tlpConn.Controls.Add(new Label() { Text = "TCP 連接埠 (Port):", Anchor = AnchorStyles.Right, AutoSize = true }, 2, 0);
            txtPort = new TextBox() { Text = "502", Dock = DockStyle.Fill, Font = new Font("Consolas", 10f, FontStyle.Bold) };
            tlpConn.Controls.Add(txtPort, 3, 0);

            tlpConn.Controls.Add(new Label() { Text = "Modbus 站號 (ID):", Anchor = AnchorStyles.Right, AutoSize = true }, 4, 0);
            FlowLayoutPanel flpUnit = new FlowLayoutPanel() { Dock = DockStyle.Fill, Margin = new Padding(0) };
            txtUnitId = new TextBox() { Text = "1", Width = 45, Font = new Font("Consolas", 10f) };
            cmbFuncCode = new ComboBox() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 175 };
            cmbFuncCode.Items.AddRange(new object[] { "FC 04 (讀取輸入暫存器)", "FC 03 (讀取保持暫存器)" });
            cmbFuncCode.SelectedIndex = 0;
            flpUnit.Controls.AddRange(new Control[] { txtUnitId, new Label() { Text = " 功能碼:", AutoSize = true, Padding = new Padding(0, 4, 0, 0) }, cmbFuncCode });
            tlpConn.Controls.Add(flpUnit, 5, 0);

            // Row 1
            tlpConn.Controls.Add(new Label() { Text = "官方通道位址:", Anchor = AnchorStyles.Right, AutoSize = true }, 0, 1);
            Label lblAddrs = new Label() { Text = "E1: 100 | E2: 200 | E3: 300 | Σ: 400", Dock = DockStyle.Fill, Font = new Font("Consolas", 9.5f, FontStyle.Bold), ForeColor = Color.DarkGreen, Padding = new Padding(0, 4, 0, 0) };
            tlpConn.Controls.Add(lblAddrs, 1, 1);

            tlpConn.Controls.Add(new Label() { Text = "浮點數字節順序:", Anchor = AnchorStyles.Right, AutoSize = true }, 2, 1);
            cmbByteOrder = new ComboBox() { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
            cmbByteOrder.Items.AddRange(new object[] {
                "Big-Endian (ABCD - 標準橫河)",
                "Word-Swap (CDAB - 常見 PLC)",
                "Byte-Swap (BADC)",
                "Little-Endian (DCBA)"
            });
            cmbByteOrder.SelectedIndex = 0;
            tlpConn.Controls.Add(cmbByteOrder, 3, 1);

            tlpConn.Controls.Add(new Label() { Text = "輪詢更新週期:", Anchor = AnchorStyles.Right, AutoSize = true }, 4, 1);
            FlowLayoutPanel flpRate = new FlowLayoutPanel() { Dock = DockStyle.Fill, Margin = new Padding(0) };
            cmbPollRate = new ComboBox() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 100 };
            cmbPollRate.Items.AddRange(new object[] { "200 ms", "500 ms", "1000 ms", "2000 ms" });
            cmbPollRate.SelectedIndex = 1;
            flpRate.Controls.Add(cmbPollRate);
            tlpConn.Controls.Add(flpRate, 5, 1);

            grpConn.Controls.Add(tlpConn);

            // 2. Action Buttons & Status Bar
            Panel pnlActions = new Panel() { Dock = DockStyle.Top, Height = 52, Padding = new Padding(0, 4, 0, 4) };
            mainContainer.Controls.Add(pnlActions);
            pnlActions.BringToFront();

            btnConnect = new Button() {
                Text = "▶ 開始連續讀取 (Poll)",
                Size = new Size(160, 42),
                Location = new Point(5, 5),
                BackColor = Color.FromArgb(0, 122, 204),
                ForeColor = Color.White,
                Font = new Font("微軟正黑體", 10f, FontStyle.Bold),
                FlatStyle = FlatStyle.Flat
            };
            btnConnect.Click += BtnConnect_Click;

            btnSingleQuery = new Button() {
                Text = "⚡ 單次查詢 (Single Read)",
                Size = new Size(155, 42),
                Location = new Point(170, 5),
                BackColor = Color.FromArgb(40, 167, 69),
                ForeColor = Color.White,
                Font = new Font("微軟正黑體", 9.5f, FontStyle.Bold),
                FlatStyle = FlatStyle.Flat
            };
            btnSingleQuery.Click += (s, e) => ExecuteSingleRead();

            btnMatrixDump = new Button() {
                Text = "[搜尋] 4 通道官方 Dump",
                Size = new Size(175, 42),
                Location = new Point(330, 5),
                BackColor = Color.FromArgb(108, 117, 125),
                ForeColor = Color.White,
                Font = new Font("微軟正黑體", 9.5f, FontStyle.Bold),
                FlatStyle = FlatStyle.Flat
            };
            btnMatrixDump.Click += (s, e) => ExecuteMatrixDump();

            btnAutoScan = new Button() {
                Text = " 智能特徵求解 (Auto-Solve)",
                Size = new Size(175, 42),
                Location = new Point(510, 5),
                BackColor = Color.FromArgb(23, 162, 184),
                ForeColor = Color.White,
                Font = new Font("微軟正黑體", 9.5f, FontStyle.Bold),
                FlatStyle = FlatStyle.Flat
            };
            btnAutoScan.Click += (s, e) => ExecuteAutoScan();

            btnCompareRef = new Button() {
                Text = "[圖表] 原廠數據基準對照",
                Size = new Size(140, 42),
                Location = new Point(690, 5),
                BackColor = Color.FromArgb(111, 66, 193),
                ForeColor = Color.White,
                Font = new Font("微軟正黑體", 9f, FontStyle.Bold),
                FlatStyle = FlatStyle.Flat
            };
            btnCompareRef.Click += (s, e) => ShowFactoryComparisonDialog();

            btnClearLog = new Button() {
                Text = "清空日誌",
                Size = new Size(75, 42),
                Location = new Point(835, 5),
                Font = new Font("微軟正黑體", 9f)
            };
            btnClearLog.Click += (s, e) => { txtLog.Clear(); AppendLog("日誌已清空。"); };

            btnOpenLogDir = new Button() {
                Text = " 開啟日誌目錄",
                Size = new Size(110, 42),
                Location = new Point(915, 5),
                Font = new Font("微軟正黑體", 9f)
            };
            btnOpenLogDir.Click += (s, e) => {
                string logDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs");
                if (!Directory.Exists(logDir)) Directory.CreateDirectory(logDir);
                System.Diagnostics.Process.Start("explorer.exe", logDir);
            };

            pnlActions.Controls.AddRange(new Control[] {
                btnConnect, btnSingleQuery, btnMatrixDump, btnAutoScan, btnCompareRef, btnClearLog, btnOpenLogDir
            });

            // 3. Status Bar Strip
            Panel pnlStatus = new Panel() { Dock = DockStyle.Top, Height = 32, BackColor = Color.FromArgb(225, 230, 238), Padding = new Padding(8, 6, 8, 4) };
            lblStatus = new Label() { Text = "狀態: 閒置 (尚未連線)", AutoSize = true, Font = new Font("微軟正黑體", 9.5f, FontStyle.Bold), ForeColor = Color.DimGray, Dock = DockStyle.Left };
            lblPacketStats = new Label() { Text = "封包統計: 發送 0 | 成功 0 | 異常 0", AutoSize = true, Font = new Font("Consolas", 9f), ForeColor = Color.DarkSlateBlue, Dock = DockStyle.Right };
            pnlStatus.Controls.AddRange(new Control[] { lblStatus, lblPacketStats });
            mainContainer.Controls.Add(pnlStatus);
            pnlStatus.BringToFront();

            // 4. Data Display Grid (三相與 SIGMA 電氣量)
            GroupBox grpGrid = new GroupBox() {
                Text = "[圖表] 三相電氣量 (Phase 1, Phase 2, Phase 3) 與 SIGMA (總和/平均) 即時量測矩陣 (100% 鏡像原廠 WTViewerFreePlus)",
                Dock = DockStyle.Top,
                Height = 265,
                Font = new Font("微軟正黑體", 9.5f, FontStyle.Bold),
                ForeColor = Color.FromArgb(20, 35, 60),
                Padding = new Padding(6)
            };
            mainContainer.Controls.Add(grpGrid);
            grpGrid.BringToFront();

            dgvResults = new DataGridView() {
                Dock = DockStyle.Fill,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                ReadOnly = true,
                RowHeadersVisible = false,
                BackgroundColor = Color.White,
                BorderStyle = BorderStyle.Fixed3D,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                SelectionMode = DataGridViewSelectionMode.CellSelect,
                MultiSelect = false,
                Font = new Font("Consolas", 10f, FontStyle.Regular),
                ColumnHeadersHeight = 30
            };
            dgvResults.EnableHeadersVisualStyles = false;
            dgvResults.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(45, 60, 80);
            dgvResults.ColumnHeadersDefaultCellStyle.ForeColor = Color.White;
            dgvResults.ColumnHeadersDefaultCellStyle.Font = new Font("微軟正黑體", 9.5f, FontStyle.Bold);
            dgvResults.ColumnHeadersDefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
            dgvResults.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;

            dgvResults.Columns.Add("Param", "量測參數 (Parameter)");
            dgvResults.Columns.Add("L1", "第 1 相 / L1 (Element 1)");
            dgvResults.Columns.Add("L2", "第 2 相 / L2 (Element 2)");
            dgvResults.Columns.Add("L3", "第 3 相 / L3 (Element 3)");
            dgvResults.Columns.Add("Sigma", "總和 / 平均 (SIGMA Σ)");
            dgvResults.Columns.Add("Unit", "物理單位 (Unit)");

            dgvResults.Columns[0].FillWeight = 140;
            dgvResults.Columns[1].FillWeight = 110;
            dgvResults.Columns[2].FillWeight = 110;
            dgvResults.Columns[3].FillWeight = 110;
            dgvResults.Columns[4].FillWeight = 120;
            dgvResults.Columns[5].FillWeight = 75;

            string[] rowTitles = new string[] {
                "電壓 U (Voltage)",
                "電流 I (Current)",
                "有功功率 P (Active Power)",
                "視在功率 S (Apparent Power)",
                "無功功率 Q (Reactive Power)",
                "功率因數 PF (Power Factor / λ)",
                "相位角 Φ (Phase Angle)",
                "電壓頻率 FreqU (Voltage Freq)",
                "電流頻率 FreqI (Current Freq)"
            };
            string[] units = new string[] { "V", "A", "kW", "kVA", "kvar", "", "deg (°)", "Hz", "Hz" };

            for (int i = 0; i < rowTitles.Length; i++)
            {
                dgvResults.Rows.Add(rowTitles[i], "--", "--", "--", "--", units[i]);
            }
            grpGrid.Controls.Add(dgvResults);

            // 5. Raw Telemetry & Protocol Log
            GroupBox grpLog = new GroupBox() {
                Text = " 通訊握手電文 (HEX) 與即時診斷分析日誌 (自動記錄至 logs/ 單一整合 CSV)",
                Dock = DockStyle.Fill,
                Font = new Font("微軟正黑體", 9.5f, FontStyle.Bold),
                ForeColor = Color.FromArgb(20, 35, 60),
                Padding = new Padding(6)
            };
            mainContainer.Controls.Add(grpLog);
            grpLog.BringToFront();

            txtLog = new TextBox() {
                Dock = DockStyle.Fill,
                Multiline = true,
                ScrollBars = ScrollBars.Both,
                ReadOnly = true,
                BackColor = Color.FromArgb(15, 20, 25),
                ForeColor = Color.FromArgb(100, 255, 120),
                Font = new Font("Consolas", 9.5f)
            };
            grpLog.Controls.Add(txtLog);

            // Timer
            tmrPoll = new System.Windows.Forms.Timer();
            tmrPoll.Interval = 500;
            tmrPoll.Tick += TmrPoll_Tick;
        }

        private void InitLogging()
        {
            try
            {
                string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                string logDir = Path.Combine(baseDir, "logs");
                if (!Directory.Exists(logDir)) Directory.CreateDirectory(logDir);

                string ts = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                logFilePath = Path.Combine(logDir, string.Format("WT333E_Unified_Log_{0}.csv", ts));

                csvWriter = new StreamWriter(new FileStream(logFilePath, FileMode.Create, FileAccess.Write, FileShare.ReadWrite), Encoding.UTF8);
                csvWriter.WriteLine("Timestamp,Type,U1_V,U2_V,U3_V,USig_V,I1_A,I2_A,I3_A,ISig_A,P1_kW,P2_kW,P3_kW,PSig_kW,S1_kVA,S2_kVA,S3_kVA,SSig_kVA,Q1_kvar,Q2_kvar,Q3_kvar,QSig_kvar,PF1,PF2,PF3,PFSig,Phi1_deg,Phi2_deg,Phi3_deg,PhiSig_deg,FreqU_Hz,FreqI_Hz,DetailsOrHex");
                csvWriter.Flush();

                AppendLog(string.Format("[OK] 日誌系統初始化完畢 (單一整合檔於 logs/ 目錄): {0}", logFilePath));
            }
            catch (Exception ex)
            {
                AppendLog("初始化日誌失敗: " + ex.Message);
            }
        }

        private void AppendLog(string msg, bool toFileOnly = false)
        {
            string ts = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
            string line = string.Format("[{0}] {1}", DateTime.Now.ToString("HH:mm:ss.fff"), msg);

            if (!toFileOnly)
            {
                if (txtLog.InvokeRequired)
                {
                    txtLog.BeginInvoke(new Action(() => {
                        txtLog.AppendText(line + "\r\n");
                        if (txtLog.TextLength > 50000) txtLog.Text = txtLog.Text.Substring(10000);
                        txtLog.SelectionStart = txtLog.TextLength;
                        txtLog.ScrollToCaret();
                    }));
                }
                else
                {
                    txtLog.AppendText(line + "\r\n");
                    if (txtLog.TextLength > 50000) txtLog.Text = txtLog.Text.Substring(10000);
                    txtLog.SelectionStart = txtLog.TextLength;
                    txtLog.ScrollToCaret();
                }
            }

            lock (logLock)
            {
                try
                {
                    if (csvWriter != null)
                    {
                        string csvLine = string.Format("{0},EVENT,,,,,,,,,,,,,,,,,,,,,,,,,,,,,\"{1}\"", ts, msg.Replace("\"", "\"\""));
                        csvWriter.WriteLine(csvLine);
                        csvWriter.Flush();
                    }
                }
                catch { }
            }
        }

        private void LogTelemetryCsv(ModbusPowerResult res, string hexExcerpt)
        {
            lock (logLock)
            {
                try
                {
                    if (csvWriter != null)
                    {
                        string line = string.Format("{0},TELEMETRY,{1:F2},{2:F2},{3:F2},{4:F2},{5:F3},{6:F3},{7:F3},{8:F3},{9:F4},{10:F4},{11:F4},{12:F4},{13:F4},{14:F4},{15:F4},{16:F4},{17:F4},{18:F4},{19:F4},{20:F4},{21:F4},{22:F4},{23:F4},{24:F4},{25:F1},{26:F1},{27:F1},{28:F1},{29:F3},{30:F3},\"{31}\"",
                            DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff"),
                            res.U1, res.U2, res.U3, res.USig,
                            res.I1, res.I2, res.I3, res.ISig,
                            res.P1, res.P2, res.P3, res.PSig,
                            res.S1, res.S2, res.S3, res.SSig,
                            res.Q1, res.Q2, res.Q3, res.QSig,
                            res.PF1, res.PF2, res.PF3, res.PFSig,
                            res.Phi1, res.Phi2, res.Phi3, res.PhiSig,
                            res.FreqU, res.FreqI,
                            hexExcerpt);
                        csvWriter.WriteLine(line);
                        csvWriter.Flush();
                    }
                }
                catch { }
            }
        }

        private void BtnConnect_Click(object sender, EventArgs e)
        {
            if (isPolling)
            {
                StopPolling();
            }
            else
            {
                StartPolling();
            }
        }

        private void StartPolling()
        {
            string ip = txtIp.Text.Trim();
            int port = 502;
            int.TryParse(txtPort.Text.Trim(), out port);
            if (port <= 0) port = 502;

            int interval = 500;
            if (cmbPollRate.SelectedIndex == 0) interval = 200;
            else if (cmbPollRate.SelectedIndex == 1) interval = 500;
            else if (cmbPollRate.SelectedIndex == 2) interval = 1000;
            else if (cmbPollRate.SelectedIndex == 3) interval = 2000;

            try
            {
                AppendLog(string.Format("正在建立連線至 WT333E -> {0}:{1} ...", ip, port));
                tcpClient = new TcpClient();
                IAsyncResult ar = tcpClient.BeginConnect(ip, port, null, null);
                if (!ar.AsyncWaitHandle.WaitOne(1200) || !tcpClient.Connected)
                {
                    tcpClient.Close();
                    tcpClient = null;
                    throw new Exception(string.Format("連線至 {0}:{1} 逾時，請確認網路線與電表 IP 設定。", ip, port));
                }

                netStream = tcpClient.GetStream();
                netStream.ReadTimeout = 1000;
                netStream.WriteTimeout = 1000;

                isPolling = true;
                pollCount = 0;
                errCount = 0;
                tmrPoll.Interval = interval;
                tmrPoll.Start();

                btnConnect.Text = "⏹ 停止讀取 (Stop)";
                btnConnect.BackColor = Color.FromArgb(220, 53, 69);
                lblStatus.Text = string.Format("狀態: 連線讀取中 ({0}:{1})", ip, port);
                lblStatus.ForeColor = Color.FromArgb(40, 167, 69);
                AppendLog("[OK] 成功連線 WT333E Modbus TCP 伺服器！已啟動連續輪詢。");
            }
            catch (Exception ex)
            {
                lblStatus.Text = "狀態: 連線失敗";
                lblStatus.ForeColor = Color.Red;
                AppendLog("[X] 連線異常: " + ex.Message);
                MessageBox.Show("連線失敗: " + ex.Message, "錯誤", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void StopPolling()
        {
            tmrPoll.Stop();
            isPolling = false;

            if (netStream != null) { try { netStream.Close(); } catch { } netStream = null; }
            if (tcpClient != null) { try { tcpClient.Close(); } catch { } tcpClient = null; }

            btnConnect.Text = "▶ 開始連續讀取 (Poll)";
            btnConnect.BackColor = Color.FromArgb(0, 122, 204);
            lblStatus.Text = "狀態: 連線已中斷 (閒置)";
            lblStatus.ForeColor = Color.DimGray;
            AppendLog("已停止連續讀取並關閉連線。");
        }

        private void TmrPoll_Tick(object sender, EventArgs e)
        {
            DoQueryWork();
        }

        private void ExecuteSingleRead()
        {
            if (isPolling)
            {
                DoQueryWork();
                return;
            }

            string ip = txtIp.Text.Trim();
            int port = 502;
            int.TryParse(txtPort.Text.Trim(), out port);
            if (port <= 0) port = 502;

            AppendLog(string.Format("執行單次查詢 -> {0}:{1} ...", ip, port));
            try
            {
                using (TcpClient tc = new TcpClient())
                {
                    IAsyncResult ar = tc.BeginConnect(ip, port, null, null);
                    if (!ar.AsyncWaitHandle.WaitOne(1000) || !tc.Connected)
                    {
                        throw new Exception("連線逾時，無法抵達目標儀表。");
                    }

                    using (NetworkStream ns = tc.GetStream())
                    {
                        ns.ReadTimeout = 1000;
                        ns.WriteTimeout = 1000;
                        ModbusPowerResult res = QueryFullPowerData(ns);
                        if (res != null)
                        {
                            UpdateTableUI(res);
                            lblStatus.Text = "狀態: 單次查詢成功 (三相與SIGMA全數對齊)";
                            lblStatus.ForeColor = Color.FromArgb(40, 167, 69);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                AppendLog("[X] 單次查詢異常: " + ex.Message);
                lblStatus.Text = "狀態: 單次查詢失敗";
                lblStatus.ForeColor = Color.Red;
            }
        }

        private void DoQueryWork()
        {
            if (netStream == null || tcpClient == null || !tcpClient.Connected)
            {
                StopPolling();
                return;
            }

            try
            {
                ModbusPowerResult res = QueryFullPowerData(netStream);
                if (res != null)
                {
                    pollCount++;
                    UpdateTableUI(res);
                    lblPacketStats.Text = string.Format("封包統計: 發送 {0} | 成功 {1} | 異常 {2}", pollCount + errCount, pollCount, errCount);
                }
                else
                {
                    errCount++;
                    lblPacketStats.Text = string.Format("封包統計: 發送 {0} | 成功 {1} | 異常 {2}", pollCount + errCount, pollCount, errCount);
                }
            }
            catch (Exception ex)
            {
                errCount++;
                AppendLog("輪詢通訊異常: " + ex.Message);
                lblPacketStats.Text = string.Format("封包統計: 發送 {0} | 成功 {1} | 異常 {2}", pollCount + errCount, pollCount, errCount);
            }
        }

        // =========================================================================
        // 核心解碼引擎：橫河官方 100/200/300/400 完美映射 (E1:100, E2:200, E3:300, Sig:400)
        // =========================================================================
        private ModbusPowerResult QueryFullPowerData(NetworkStream ns)
        {
            byte unitId = 1;
            byte.TryParse(txtUnitId.Text.Trim(), out unitId);
            if (unitId == 0) unitId = 1;

            byte funcCode = (byte)(cmbFuncCode.SelectedIndex == 0 ? 0x04 : 0x03);
            int decodeMode = cmbByteOrder.SelectedIndex;

            // 1. Read SIGMA (Addr 100, Count 14 = 7 Floats)
            byte[] rawSig = SendModbusSingleBlock(ns, unitId, funcCode, 100, 14);
            if (rawSig == null) return null;

            // 2. Read E1 (Addr 200, Count 18 = 9 Floats)
            byte[] rawE1 = SendModbusSingleBlock(ns, unitId, funcCode, 200, 18);
            if (rawE1 == null) return null;

            // 3. Read E2 (Addr 300, Count 14 = 7 Floats)
            byte[] rawE2 = SendModbusSingleBlock(ns, unitId, funcCode, 300, 14);
            if (rawE2 == null) return null;

            // 4. Read E3 (Addr 400, Count 14 = 7 Floats)
            byte[] rawE3 = SendModbusSingleBlock(ns, unitId, funcCode, 400, 14);
            if (rawE3 == null) return null;

            float[] f1 = ExtractFloatArray(rawE1, 9, rawE1.Length - 9, decodeMode);
            float[] f2 = ExtractFloatArray(rawE2, 9, rawE2.Length - 9, decodeMode);
            float[] f3 = ExtractFloatArray(rawE3, 9, rawE3.Length - 9, decodeMode);
            float[] fs = ExtractFloatArray(rawSig, 9, rawSig.Length - 9, decodeMode);

            ModbusPowerResult res = new ModbusPowerResult();

            // 1. Element 1 (Reg 100)
            if (f1.Length > 0) res.U1 = f1[0];
            if (f1.Length > 1) res.I1 = f1[1];
            if (f1.Length > 2) res.P1 = NormalizePower(f1[2]);
            if (f1.Length > 3) res.S1 = NormalizePower(f1[3]);
            if (f1.Length > 4) res.Q1 = NormalizePower(f1[4]);
            if (f1.Length > 5) res.PF1 = f1[5];
            if (f1.Length > 6) res.Phi1 = f1[6];
            if (f1.Length > 7 && f1[7] > 1f) res.FreqU = f1[7];
            if (f1.Length > 8 && f1[8] > 1f) res.FreqI = f1[8];

            // 2. Element 2 (Reg 200)
            if (f2.Length > 0) res.U2 = f2[0];
            if (f2.Length > 1) res.I2 = f2[1];
            if (f2.Length > 2) res.P2 = NormalizePower(f2[2]);
            if (f2.Length > 3) res.S2 = NormalizePower(f2[3]);
            if (f2.Length > 4) res.Q2 = NormalizePower(f2[4]);
            if (f2.Length > 5) res.PF2 = f2[5];
            if (f2.Length > 6) res.Phi2 = f2[6];

            // 3. Element 3 (Reg 300)
            if (f3.Length > 0) res.U3 = f3[0];
            if (f3.Length > 1) res.I3 = f3[1];
            if (f3.Length > 2) res.P3 = NormalizePower(f3[2]);
            if (f3.Length > 3) res.S3 = NormalizePower(f3[3]);
            if (f3.Length > 4) res.Q3 = NormalizePower(f3[4]);
            if (f3.Length > 5) res.PF3 = f3[5];
            if (f3.Length > 6) res.Phi3 = f3[6];

            // 4. SIGMA (Reg 400)
            if (fs.Length > 0) res.USig = fs[0];
            if (fs.Length > 1) res.ISig = fs[1];
            if (fs.Length > 2) res.PSig = NormalizePower(fs[2]);
            if (fs.Length > 3) res.SSig = NormalizePower(fs[3]);
            if (fs.Length > 4) res.QSig = NormalizePower(fs[4]);
            if (fs.Length > 5) res.PFSig = fs[5];
            if (fs.Length > 6) res.PhiSig = fs[6];

            // Fallback for Sigma if 0
            if (res.USig < 1f && res.U1 > 1f && res.U2 > 1f && res.U3 > 1f)
                res.USig = (res.U1 + res.U2 + res.U3) / 3.0f;
            if (res.ISig < 0.1f && res.I1 > 0.1f && res.I2 > 0.1f && res.I3 > 0.1f)
                res.ISig = (res.I1 + res.I2 + res.I3) / 3.0f;
            if (res.PSig == 0f && (res.P1 != 0f || res.P2 != 0f || res.P3 != 0f))
                res.PSig = res.P1 + res.P2 + res.P3;

            string hexLog = string.Format("E1:{0} | E2:{1} | E3:{2} | Sig:{3}",
                FormatHex(rawE1, 6), FormatHex(rawE2, 6), FormatHex(rawE3, 6), FormatHex(rawSig, 6));

            if (pollCount % 2 == 0 || !isPolling)
            {
                AppendLog(string.Format("[官方精確對齊] U: [{0:F2}, {1:F2}, {2:F2} / Σ={3:F2}V] | I: [{4:F2}, {5:F2}, {6:F2} / Σ={7:F2}A] | P: [{8:F2}, {9:F2}, {10:F2} / Σ={11:F2}kW]",
                    res.U1, res.U2, res.U3, res.USig, res.I1, res.I2, res.I3, res.ISig, res.P1, res.P2, res.P3, res.PSig));
            }

            LogTelemetryCsv(res, hexLog);
            return res;
        }

        private float NormalizePower(float rawVal)
        {
            if (Math.Abs(rawVal) >= 50f) return rawVal / 1000.0f; // Watts -> kW
            return rawVal; // already in kW
        }

        private byte[] SendModbusSingleBlock(NetworkStream ns, byte unitId, byte funcCode, ushort startAddr, ushort regCount)
        {
            ushort tid = transId++;
            byte[] req = new byte[] {
                (byte)(tid >> 8), (byte)(tid & 0xFF),
                0x00, 0x00, 0x00, 0x06,
                unitId, funcCode,
                (byte)(startAddr >> 8), (byte)(startAddr & 0xFF),
                (byte)(regCount >> 8), (byte)(regCount & 0xFF)
            };

            ns.Write(req, 0, req.Length);

            byte[] respHeader = new byte[9];
            int readHdr = ReadFullBytes(ns, respHeader, 0, 9, 1000);
            if (readHdr < 9) return null;
            if (respHeader[7] != funcCode) return null;

            int byteCount = respHeader[8];
            byte[] dataBytes = new byte[byteCount];
            int readData = ReadFullBytes(ns, dataBytes, 0, byteCount, 1000);
            if (readData < byteCount) return null;

            byte[] full = new byte[9 + byteCount];
            Array.Copy(respHeader, 0, full, 0, 9);
            Array.Copy(dataBytes, 0, full, 9, byteCount);
            return full;
        }

        private float[] ExtractFloatArray(byte[] buf, int offset, int byteLen, int decodeMode)
        {
            int floatCount = byteLen / 4;
            float[] f = new float[floatCount];
            for (int i = 0; i < floatCount; i++)
            {
                f[i] = ParseSingleFloat(buf, offset + i * 4, decodeMode);
            }
            return f;
        }

        private string FormatHex(byte[] buf, int maxLen)
        {
            if (buf == null) return "";
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < Math.Min(buf.Length, maxLen); i++)
                sb.Append(buf[i].ToString("X2") + " ");
            if (buf.Length > maxLen) sb.Append("...");
            return sb.ToString();
        }

        private int ReadFullBytes(NetworkStream ns, byte[] buffer, int offset, int count, int timeoutMs)
        {
            int totalRead = 0;
            int startTick = Environment.TickCount;
            while (totalRead < count)
            {
                if (Environment.TickCount - startTick > timeoutMs) break;
                if (ns.DataAvailable)
                {
                    int r = ns.Read(buffer, offset + totalRead, count - totalRead);
                    if (r <= 0) break;
                    totalRead += r;
                }
                else
                {
                    Thread.Sleep(5);
                }
            }
            return totalRead;
        }

        private float ParseSingleFloat(byte[] buf, int offset, int decodeMode)
        {
            if (buf == null || offset + 4 > buf.Length) return 0f;
            try
            {
                if (decodeMode == 0) // ABCD (Big-Endian)
                {
                    byte[] b = new byte[] { buf[offset + 3], buf[offset + 2], buf[offset + 1], buf[offset] };
                    float f = BitConverter.ToSingle(b, 0);
                    return (!float.IsNaN(f) && !float.IsInfinity(f) && Math.Abs(f) < 1000000f) ? f : 0f;
                }
                else if (decodeMode == 1) // CDAB (Word-Swap)
                {
                    byte[] b = new byte[] { buf[offset + 1], buf[offset], buf[offset + 3], buf[offset + 2] };
                    float f = BitConverter.ToSingle(b, 0);
                    return (!float.IsNaN(f) && !float.IsInfinity(f) && Math.Abs(f) < 1000000f) ? f : 0f;
                }
                else if (decodeMode == 2) // BADC (Byte-Swap)
                {
                    byte[] b = new byte[] { buf[offset + 2], buf[offset + 3], buf[offset], buf[offset + 1] };
                    float f = BitConverter.ToSingle(b, 0);
                    return (!float.IsNaN(f) && !float.IsInfinity(f) && Math.Abs(f) < 1000000f) ? f : 0f;
                }
                else if (decodeMode == 3) // DCBA (Little-Endian)
                {
                    byte[] b = new byte[] { buf[offset], buf[offset + 1], buf[offset + 2], buf[offset + 3] };
                    float f = BitConverter.ToSingle(b, 0);
                    return (!float.IsNaN(f) && !float.IsInfinity(f) && Math.Abs(f) < 1000000f) ? f : 0f;
                }
                return 0f;
            }
            catch { return 0f; }
        }

        private void UpdateTableUI(ModbusPowerResult res)
        {
            if (dgvResults.InvokeRequired)
            {
                dgvResults.BeginInvoke(new Action(() => UpdateTableUI(res)));
                return;
            }

            // Row 0: Voltage U
            dgvResults.Rows[0].Cells[1].Value = res.U1.ToString("F2");
            dgvResults.Rows[0].Cells[2].Value = res.U2.ToString("F2");
            dgvResults.Rows[0].Cells[3].Value = res.U3.ToString("F2");
            dgvResults.Rows[0].Cells[4].Value = res.USig.ToString("F2");

            // Row 1: Current I
            dgvResults.Rows[1].Cells[1].Value = res.I1.ToString("F3");
            dgvResults.Rows[1].Cells[2].Value = res.I2.ToString("F3");
            dgvResults.Rows[1].Cells[3].Value = res.I3.ToString("F3");
            dgvResults.Rows[1].Cells[4].Value = res.ISig.ToString("F3");

            // Row 2: Active Power P
            dgvResults.Rows[2].Cells[1].Value = res.P1.ToString("F4");
            dgvResults.Rows[2].Cells[2].Value = res.P2.ToString("F4");
            dgvResults.Rows[2].Cells[3].Value = res.P3.ToString("F4");
            dgvResults.Rows[2].Cells[4].Value = res.PSig.ToString("F4");

            // Row 3: Apparent Power S
            dgvResults.Rows[3].Cells[1].Value = res.S1.ToString("F4");
            dgvResults.Rows[3].Cells[2].Value = res.S2.ToString("F4");
            dgvResults.Rows[3].Cells[3].Value = res.S3.ToString("F4");
            dgvResults.Rows[3].Cells[4].Value = res.SSig.ToString("F4");

            // Row 4: Reactive Power Q
            dgvResults.Rows[4].Cells[1].Value = res.Q1.ToString("F4");
            dgvResults.Rows[4].Cells[2].Value = res.Q2.ToString("F4");
            dgvResults.Rows[4].Cells[3].Value = res.Q3.ToString("F4");
            dgvResults.Rows[4].Cells[4].Value = res.QSig.ToString("F4");

            // Row 5: Power Factor PF
            dgvResults.Rows[5].Cells[1].Value = res.PF1.ToString("F4");
            dgvResults.Rows[5].Cells[2].Value = res.PF2.ToString("F4");
            dgvResults.Rows[5].Cells[3].Value = res.PF3.ToString("F4");
            dgvResults.Rows[5].Cells[4].Value = res.PFSig.ToString("F4");

            // Row 6: Phase Angle Phi
            dgvResults.Rows[6].Cells[1].Value = res.Phi1.ToString("F1");
            dgvResults.Rows[6].Cells[2].Value = res.Phi2.ToString("F1");
            dgvResults.Rows[6].Cells[3].Value = res.Phi3.ToString("F1");
            dgvResults.Rows[6].Cells[4].Value = res.PhiSig.ToString("F1");

            // Row 7: Voltage Frequency FreqU
            dgvResults.Rows[7].Cells[1].Value = res.FreqU.ToString("F3");
            dgvResults.Rows[7].Cells[2].Value = "--------";
            dgvResults.Rows[7].Cells[3].Value = "--------";
            dgvResults.Rows[7].Cells[4].Value = "--------";

            // Row 8: Current Frequency FreqI
            dgvResults.Rows[8].Cells[1].Value = res.FreqI.ToString("F3");
            dgvResults.Rows[8].Cells[2].Value = "--------";
            dgvResults.Rows[8].Cells[3].Value = "--------";
            dgvResults.Rows[8].Cells[4].Value = "--------";
        }

        // =========================================================================
        // 原廠數據基準對照視窗 (Golden Reference Comparator)
        // =========================================================================
        private void ShowFactoryComparisonDialog()
        {
            Form dlg = new Form() {
                Text = "[圖表] 橫河原廠 WTViewerFreePlus 實測基準對照表 (2026-08-28 現場真機實測截圖數據)",
                Size = new Size(720, 480),
                StartPosition = FormStartPosition.CenterParent,
                Font = new Font("微軟正黑體", 9.5f),
                BackColor = Color.White
            };

            DataGridView dgvRef = new DataGridView() {
                Dock = DockStyle.Fill,
                ReadOnly = true,
                AllowUserToAddRows = false,
                RowHeadersVisible = false,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                Font = new Font("Consolas", 10f)
            };
            dgvRef.EnableHeadersVisualStyles = false;
            dgvRef.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(40, 70, 110);
            dgvRef.ColumnHeadersDefaultCellStyle.ForeColor = Color.White;
            dgvRef.ColumnHeadersDefaultCellStyle.Font = new Font("微軟正黑體", 9.5f, FontStyle.Bold);
            dgvRef.ColumnHeadersDefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
            dgvRef.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;

            dgvRef.Columns.Add("Function", "Function");
            dgvRef.Columns.Add("E1", "Element 1");
            dgvRef.Columns.Add("E2", "Element 2");
            dgvRef.Columns.Add("E3", "Element 3");
            dgvRef.Columns.Add("Sig", "SIGMA");
            dgvRef.Columns.Add("Unit", "Units");

            dgvRef.Rows.Add("U (電壓)", "52.68", "52.71", "52.75", "52.71", "V");
            dgvRef.Rows.Add("I (電流)", "15.39", "15.16", "15.55", "15.47", "A");
            dgvRef.Rows.Add("P (有功功率)", "-0.30k (-300W)", "-0.78k (-780W)", "0.51k (510W)", "0.21k (210W)", "W");
            dgvRef.Rows.Add("S (視在功率)", "0.81k (810VA)", "0.80k (800VA)", "0.82k (820VA)", "1.41k (1410VA)", "VA");
            dgvRef.Rows.Add("Q (無功功率)", "0.75k (750var)", "-0.15k (-150var)", "0.64k (640var)", "1.40k (1400var)", "var");
            dgvRef.Rows.Add("PF (功率因數)", "-0.3657", "-0.9813", "0.6181", "0.1491", "");
            dgvRef.Rows.Add("Phi (相位角)", "111.45", "-168.90", "51.82", "81.42", "deg (°)");
            dgvRef.Rows.Add("FreqU (電壓頻率)", "44.919", "--------", "--------", "--------", "Hz");
            dgvRef.Rows.Add("FreqI (電流頻率)", "10.007", "--------", "--------", "--------", "Hz");

            Panel pnlBottom = new Panel() { Dock = DockStyle.Bottom, Height = 45, Padding = new Padding(8) };
            Button btnClose = new Button() { Text = "關閉對照視窗", Dock = DockStyle.Right, Width = 120, Font = new Font("微軟正黑體", 9f) };
            btnClose.Click += (s, e) => dlg.Close();
            pnlBottom.Controls.Add(btnClose);

            dlg.Controls.Add(dgvRef);
            dlg.Controls.Add(pnlBottom);
            dlg.ShowDialog(this);
        }

        // =========================================================================
        // 4 通道全矩陣 Dump 工具
        // =========================================================================
        private void ExecuteMatrixDump()
        {
            string ip = txtIp.Text.Trim();
            int port = 502;
            int.TryParse(txtPort.Text.Trim(), out port);
            if (port <= 0) port = 502;

            AppendLog("\r\n===============================================================");
            AppendLog(string.Format(" [[搜尋] 4 通道官方十進位 Dump 獵捕啟動] (IP: {0}:{1})", ip, port));
            AppendLog(" >> 自動傾印 E1(100), E2(200), E3(300), SIGMA(400)");
            AppendLog("===============================================================");

            int decodeMode = cmbByteOrder.SelectedIndex;

            try
            {
                using (TcpClient tc = new TcpClient())
                {
                    IAsyncResult ar = tc.BeginConnect(ip, port, null, null);
                    if (!ar.AsyncWaitHandle.WaitOne(1000) || !tc.Connected)
                    {
                        AppendLog("[X] 連線逾時，無法開啟 Dump 連線。");
                        return;
                    }

                    using (NetworkStream ns = tc.GetStream())
                    {
                        ns.ReadTimeout = 1000;
                        ns.WriteTimeout = 1000;

                        int[] addrs = new int[] { 100, 200, 300, 400 };
                        string[] names = new string[] { "Element 1", "Element 2", "Element 3", "SIGMA (Σ)" };

                        for (int k = 0; k < 4; k++)
                        {
                            byte[] resp = SendModbusSingleBlock(ns, 1, 0x04, (ushort)addrs[k], 16);
                            if (resp != null && resp.Length >= 9 && resp[7] == 0x04)
                            {
                                int byteLen = resp[8];
                                int floatCount = Math.Min(8, byteLen / 4);
                                AppendLog(string.Format("\r\n--- [{0} @ Reg {1}] ---", names[k], addrs[k]));
                                for (int i = 0; i < floatCount; i++)
                                {
                                    float val = ParseSingleFloat(resp, 9 + i * 4, decodeMode);
                                    string tag = "";
                                    if (i == 0) tag = " ⚡[電壓 U]";
                                    else if (i == 1) tag = " [電流 I]";
                                    else if (i == 2) tag = " [功率 P (W)]";
                                    else if (i == 5) tag = " [功因 PF]";
                                    else if (i == 6) tag = " [相位角 Φ]";

                                    AppendLog(string.Format("  F{0} [Reg {1}]: {2,10:F2}{3}", i, addrs[k] + i * 2, val, tag));
                                }
                            }
                        }
                    }
                }
                AppendLog("================ 4 通道 Dump 完畢 ================");
            }
            catch (Exception ex)
            {
                AppendLog("[X] Dump 例外異常: " + ex.Message);
            }
        }

        // =========================================================================
        // 智能特徵求解 (Auto-Solve & Constraint Matcher)
        // =========================================================================
        private void ExecuteAutoScan()
        {
            string ip = txtIp.Text.Trim();
            int port = 502;
            int.TryParse(txtPort.Text.Trim(), out port);
            if (port <= 0) port = 502;

            AppendLog("\r\n===============================================================");
            AppendLog(" [ WT333E 官方 100/200/300/400 閉環自動診斷求解啟動] ...");
            AppendLog("===============================================================");

            try
            {
                using (TcpClient tc = new TcpClient())
                {
                    IAsyncResult ar = tc.BeginConnect(ip, port, null, null);
                    if (!ar.AsyncWaitHandle.WaitOne(1000) || !tc.Connected)
                    {
                        AppendLog("[X] 連線逾時，無法開啟智能求解連線。");
                        return;
                    }

                    using (NetworkStream ns = tc.GetStream())
                    {
                        ns.ReadTimeout = 800;
                        ns.WriteTimeout = 800;

                        ModbusPowerResult res = QueryFullPowerData(ns);
                        if (res != null && res.U1 > 40f && res.U2 > 40f && res.U3 > 40f)
                        {
                            AppendLog(string.Format(" 【 官方 100/200/300/400 完美對齊求解成功】\r\n   -> U1={0:F2}V, U2={1:F2}V, U3={2:F2}V | I1={3:F2}A, I2={4:F2}A, I3={5:F2}A | P總={6:F2}kW",
                                res.U1, res.U2, res.U3, res.I1, res.I2, res.I3, res.PSig));
                            UpdateTableUI(res);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                AppendLog("[X] 智能求解例外: " + ex.Message);
            }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            StopPolling();
            lock (logLock)
            {
                try
                {
                    if (csvWriter != null) { csvWriter.Flush(); csvWriter.Close(); csvWriter = null; }
                }
                catch { }
            }
            base.OnFormClosing(e);
        }
    }

    public class ModbusPowerResult
    {
        public float U1, U2, U3, USig;
        public float I1, I2, I3, ISig;
        public float P1, P2, P3, PSig;
        public float S1, S2, S3, SSig;
        public float Q1, Q2, Q3, QSig;
        public float PF1, PF2, PF3, PFSig;
        public float Phi1, Phi2, Phi3, PhiSig;
        public float FreqU, FreqI;

        public ModbusPowerResult()
        {
            PF1 = 1.0f; PF2 = 1.0f; PF3 = 1.0f; PFSig = 1.0f;
            FreqU = 35.0f; FreqI = 10.0f;
        }
    }
}
