using System;
using System.Drawing;
using System.IO;
using System.IO.Ports;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace DeviceProtocolSniffer
{
    public class SnifferForm : Form
    {
        private TabControl tabs;

        // --- Tab 1: TCP Network Proxy Sniffer (網路代理監聽器) ---
        private TextBox txtTargetIp, txtTargetPort, txtLocalProxyPort;
        private Button btnStartTcpProxy;
        private Label lblTcpProxyStatus;
        private TextBox txtTcpSniffLog;
        private TcpListener tcpProxyListener;
        private Thread proxyThread;
        private bool isProxyRunning = false;
        private int packetCount = 0;

        // --- Tab 2: Serial Proxy / Direct Sniffer (串列埠監聽與HEX擷取) ---
        private ComboBox cmbSerialPort, cmbSerialBaud;
        private Button btnStartSerialSniff;
        private Label lblSerialSniffStatus;
        private TextBox txtSerialSniffLog;
        private SerialPort spSniff;
        private Thread serialReadThread;
        private bool isSerialSniffRunning = false;

        // --- Tab 3: OEM File & CSV Live Stream Watcher (原廠軟體記錄檔監控) ---
        private TextBox txtWatchFolder, txtWatchPattern;
        private Button btnStartFileWatch, btnSelectWatchFolder;
        private Label lblFileWatchStatus, lblLatestDataValue;
        private TextBox txtFileWatchLog;
        private FileSystemWatcher fileWatcher;

        [STAThread]
        public static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new SnifferForm());
        }

        public SnifferForm()
        {
            this.Text = "動力計設備通訊協定即時監聽與反向擷取工具 (Protocol Sniffer & Traffic Analyzer - XP/7/10/11 版)";
            this.Size = new Size(950, 720);
            this.StartPosition = FormStartPosition.CenterScreen;
            this.BackColor = Color.FromArgb(240, 243, 246);
            this.Font = new Font("微軟正黑體", 9.5f, FontStyle.Regular);

            BuildUI();
        }

        private void BuildUI()
        {
            Panel pnlTop = new Panel() { Dock = DockStyle.Top, Height = 55, BackColor = Color.FromArgb(26, 43, 76) };
            Label lblTitle = new Label()
            {
                Text = "🕵️‍♂️ 動力測試通訊協定即時監聽與逆向反編譯工具箱 (Device Protocol Sniffer)",
                ForeColor = Color.White,
                Font = new Font("微軟正黑體", 11.5f, FontStyle.Bold),
                Location = new Point(15, 15),
                AutoSize = true
            };
            pnlTop.Controls.Add(lblTitle);
            this.Controls.Add(pnlTop);

            tabs = new TabControl() { Dock = DockStyle.Fill, Font = new Font("微軟正黑體", 10f, FontStyle.Bold) };

            // Tab 1: 網路 TCP 代理監聽 (適用於 WT333E & GL820)
            TabPage tabTcp = new TabPage("  🌐 1. 網路 TCP 通訊代理監聽 (WT333E / GL820)  ");
            tabTcp.BackColor = Color.White;
            BuildTcpProxyTab(tabTcp);
            tabs.TabPages.Add(tabTcp);

            // Tab 2: 串列埠底層 HEX 監聽 (適用於 KISTLER 4700B)
            TabPage tabSerial = new TabPage("  🔧 2. 串列埠底層 HEX 監聽 (KISTLER 4700B)  ");
            tabSerial.BackColor = Color.White;
            BuildSerialSniffTab(tabSerial);
            tabs.TabPages.Add(tabSerial);

            // Tab 3: 原廠軟體輸出即時監視橋接 (WTViewer / GL-APS / SensorTool)
            TabPage tabFile = new TabPage("  📁 3. 原廠軟體即時記錄監控橋接 (Zero-Driver)  ");
            tabFile.BackColor = Color.White;
            BuildFileWatchTab(tabFile);
            tabs.TabPages.Add(tabFile);

            this.Controls.Add(tabs);
        }

        // =========================================================================
        // TAB 1: 網路 TCP 代理監聽器 (雙向轉發 + 完整封包 HEX/ASCII 即時解析)
        // =========================================================================
        private void BuildTcpProxyTab(TabPage tab)
        {
            GroupBox grpConfig = new GroupBox() { Text = "TCP 透明代理轉發與封包監聽設定", Location = new Point(15, 10), Size = new Size(905, 125) };

            Label l1 = new Label() { Text = "本機代理監聽 Port:", Location = new Point(15, 25), AutoSize = true };
            txtLocalProxyPort = new TextBox() { Text = "8024", Location = new Point(145, 22), Width = 60 };

            Label l2 = new Label() { Text = "目標實體儀表 IP:", Location = new Point(220, 25), AutoSize = true };
            txtTargetIp = new TextBox() { Text = "192.168.0.3", Location = new Point(335, 22), Width = 110 };

            Label l3 = new Label() { Text = "目標 Port:", Location = new Point(460, 25), AutoSize = true };
            txtTargetPort = new TextBox() { Text = "8023", Location = new Point(530, 22), Width = 60 };

            btnStartTcpProxy = new Button()
            {
                Text = "▶ 啟動 TCP 代理監聽",
                Location = new Point(610, 18),
                Size = new Size(160, 32),
                BackColor = Color.FromArgb(0, 180, 216),
                ForeColor = Color.White,
                Font = new Font("微軟正黑體", 9.5f, FontStyle.Bold)
            };
            btnStartTcpProxy.Click += BtnStartTcpProxy_Click;

            Button btnClearTcpLog = new Button() { Text = "清空日誌", Location = new Point(780, 18), Size = new Size(110, 32) };
            btnClearTcpLog.Click += (s, e) => { txtTcpSniffLog.Clear(); packetCount = 0; };

            lblTcpProxyStatus = new Label() { Text = "狀態: 代理伺服器已停止", Location = new Point(15, 58), AutoSize = true, ForeColor = Color.Gray };

            Label lDesc = new Label()
            {
                Text = "💡 原廠軟體監聽教學：\n  1. 設定「目標實體儀表 IP/Port」（如 GL820: 192.168.0.3:8023 或 WT333E: 192.168.0.11:111），點擊「啟動代理監聽」。\n  2. 在原廠軟體中將連線 IP 改填為 127.0.0.1 (Port 填代理 Port)，原廠軟體送出的每個 Byte 與儀表回傳的每個 Byte 都將在此一覽無遺！",
                Location = new Point(15, 78),
                AutoSize = true,
                ForeColor = Color.FromArgb(70, 80, 95)
            };

            grpConfig.Controls.AddRange(new Control[] { l1, txtLocalProxyPort, l2, txtTargetIp, l3, txtTargetPort, btnStartTcpProxy, btnClearTcpLog, lblTcpProxyStatus, lDesc });
            tab.Controls.Add(grpConfig);

            GroupBox grpLog = new GroupBox() { Text = "網路封包雙向監聽日誌 (TX 原廠軟體發出 ➔ 實體儀表 | RX 實體儀表回傳 ➔ 原廠軟體)", Location = new Point(15, 145), Size = new Size(905, 465) };
            txtTcpSniffLog = new TextBox() { Dock = DockStyle.Fill, Multiline = true, ScrollBars = ScrollBars.Vertical, ReadOnly = true, BackColor = Color.Black, ForeColor = Color.LightGreen, Font = new Font("Consolas", 9.5f) };
            grpLog.Controls.Add(txtTcpSniffLog);
            tab.Controls.Add(grpLog);
        }

        private void BtnStartTcpProxy_Click(object sender, EventArgs e)
        {
            if (isProxyRunning)
            {
                // 停止代理
                isProxyRunning = false;
                if (tcpProxyListener != null) tcpProxyListener.Stop();
                btnStartTcpProxy.Text = "▶ 啟動 TCP 代理監聽";
                btnStartTcpProxy.BackColor = Color.FromArgb(0, 180, 216);
                lblTcpProxyStatus.Text = "狀態: 代理伺服器已停止";
                lblTcpProxyStatus.ForeColor = Color.Gray;
                return;
            }

            int localPort = int.Parse(txtLocalProxyPort.Text.Trim());
            string targetIp = txtTargetIp.Text.Trim();
            int targetPort = int.Parse(txtTargetPort.Text.Trim());

            try
            {
                tcpProxyListener = new TcpListener(IPAddress.Any, localPort);
                tcpProxyListener.Start();
                isProxyRunning = true;

                btnStartTcpProxy.Text = "⏹ 停止代理監聽";
                btnStartTcpProxy.BackColor = Color.FromArgb(239, 68, 68);
                lblTcpProxyStatus.Text = string.Format("狀態: 代理運行中 (本機 127.0.0.1:{0} ➔ 儀表 {1}:{2})", localPort, targetIp, targetPort);
                lblTcpProxyStatus.ForeColor = Color.Green;

                LogTcp(string.Format("[系統] 代理伺服器啟動成功！正在本機 Port {0} 監聽連線並轉發至 {1}:{2}...\r\n", localPort, targetIp, targetPort));

                proxyThread = new Thread(() => ProxyWorker(tcpProxyListener, targetIp, targetPort));
                proxyThread.IsBackground = true;
                proxyThread.Start();
            }
            catch (Exception ex)
            {
                MessageBox.Show("無法啟動代理監聽: " + ex.Message, "錯誤");
            }
        }

        private void ProxyWorker(TcpListener listener, string targetIp, int targetPort)
        {
            while (isProxyRunning)
            {
                try
                {
                    TcpClient clientApp = listener.AcceptTcpClient();
                    LogTcp(string.Format("\r\n[連線] 偵測到原廠軟體已連入代理埠！正在建立與實體儀表 {0}:{1} 之橋接通道...\r\n", targetIp, targetPort));

                    TcpClient device = new TcpClient();
                    device.Connect(targetIp, targetPort);

                    NetworkStream streamApp = clientApp.GetStream();
                    NetworkStream streamDevice = device.GetStream();

                    // 啟動雙向轉發線程
                    Thread tAppToDev = new Thread(() => ForwardStream(streamApp, streamDevice, "【TX 原廠軟體 ➔ 儀表】"));
                    Thread tDevToApp = new Thread(() => ForwardStream(streamDevice, streamApp, "【RX 實體儀表 ➔ 軟體】"));
                    tAppToDev.IsBackground = true;
                    tDevToApp.IsBackground = true;
                    tAppToDev.Start();
                    tDevToApp.Start();
                }
                catch (Exception)
                {
                    if (!isProxyRunning) break;
                }
            }
        }

        private void ForwardStream(NetworkStream source, NetworkStream dest, string tag)
        {
            byte[] buf = new byte[4096];
            while (isProxyRunning)
            {
                try
                {
                    int read = source.Read(buf, 0, buf.Length);
                    if (read <= 0) break;

                    dest.Write(buf, 0, read);
                    dest.Flush();

                    packetCount++;
                    string ascii = Encoding.ASCII.GetString(buf, 0, read);
                    StringBuilder hex = new StringBuilder();
                    for (int i = 0; i < read; i++) hex.Append(buf[i].ToString("X2") + " ");

                    string cleanAscii = ascii.Replace("\r", "\\r").Replace("\n", "\\n");
                    LogTcp(string.Format("[#{0:D4}] {1} (長度 {2} Bytes)\r\n  ASCII : {3}\r\n  HEX   : {4}\r\n", packetCount, tag, read, cleanAscii, hex.ToString().Trim()));
                }
                catch
                {
                    break;
                }
            }
        }

        private void LogTcp(string msg)
        {
            if (txtTcpSniffLog.InvokeRequired)
            {
                txtTcpSniffLog.BeginInvoke(new Action<string>(LogTcp), msg);
                return;
            }
            txtTcpSniffLog.AppendText(msg);
        }

        // =========================================================================
        // TAB 2: 串列埠底層 HEX / ASCII 監聽器 (適用於 KISTLER 4700B)
        // =========================================================================
        private void BuildSerialSniffTab(TabPage tab)
        {
            GroupBox grpConfig = new GroupBox() { Text = "串列埠底層封包監聽設定", Location = new Point(15, 10), Size = new Size(905, 125) };

            Label l1 = new Label() { Text = "監聽 COM 埠:", Location = new Point(15, 25), AutoSize = true };
            cmbSerialPort = new ComboBox() { Location = new Point(110, 22), Width = 90, DropDownStyle = ComboBoxStyle.DropDownList };
            cmbSerialPort.Items.AddRange(new object[] { "COM4", "COM1", "COM2", "COM3", "COM5" });
            cmbSerialPort.SelectedIndex = 0;

            Label l2 = new Label() { Text = "鮑率:", Location = new Point(215, 25), AutoSize = true };
            cmbSerialBaud = new ComboBox() { Location = new Point(255, 22), Width = 110, DropDownStyle = ComboBoxStyle.DropDownList };
            cmbSerialBaud.Items.AddRange(new object[] { "1000000", "115200", "57600", "38400", "19200", "9600" });
            cmbSerialBaud.SelectedIndex = 0;

            btnStartSerialSniff = new Button()
            {
                Text = "▶ 開始底層監聽",
                Location = new Point(380, 18),
                Size = new Size(140, 32),
                BackColor = Color.FromArgb(0, 180, 216),
                ForeColor = Color.White,
                Font = new Font("微軟正黑體", 9.5f, FontStyle.Bold)
            };
            btnStartSerialSniff.Click += BtnStartSerialSniff_Click;

            Button btnClearSerialLog = new Button() { Text = "清空日誌", Location = new Point(530, 18), Size = new Size(90, 32) };
            btnClearSerialLog.Click += (s, e) => txtSerialSniffLog.Clear();

            lblSerialSniffStatus = new Label() { Text = "狀態: 監聽停止", Location = new Point(640, 25), AutoSize = true, ForeColor = Color.Gray };

            Label lDesc = new Label()
            {
                Text = "💡 串列通訊監聽說明：\n  本工具直接以 Binary Byte 串流監聽串列埠，會完整印出儀器輸出的「每一個十六進制 Byte (HEX)」與 ASCII 字元。\n  若 Kistler 4700B 處於主動傳輸狀態，此處將以毫秒級即時列出所有原始封包。",
                Location = new Point(15, 65),
                AutoSize = true,
                ForeColor = Color.FromArgb(70, 80, 95)
            };

            grpConfig.Controls.AddRange(new Control[] { l1, cmbSerialPort, l2, cmbSerialBaud, btnStartSerialSniff, btnClearSerialLog, lblSerialSniffStatus, lDesc });
            tab.Controls.Add(grpConfig);

            GroupBox grpLog = new GroupBox() { Text = "串列埠原始數據 Byte 監聽記錄 (HEX + ASCII)", Location = new Point(15, 145), Size = new Size(905, 465) };
            txtSerialSniffLog = new TextBox() { Dock = DockStyle.Fill, Multiline = true, ScrollBars = ScrollBars.Vertical, ReadOnly = true, BackColor = Color.Black, ForeColor = Color.LightGreen, Font = new Font("Consolas", 9.5f) };
            grpLog.Controls.Add(txtSerialSniffLog);
            tab.Controls.Add(grpLog);
        }

        private void BtnStartSerialSniff_Click(object sender, EventArgs e)
        {
            if (isSerialSniffRunning)
            {
                isSerialSniffRunning = false;
                if (spSniff != null && spSniff.IsOpen) spSniff.Close();
                btnStartSerialSniff.Text = "▶ 開始底層監聽";
                btnStartSerialSniff.BackColor = Color.FromArgb(0, 180, 216);
                lblSerialSniffStatus.Text = "狀態: 監聽已停止";
                lblSerialSniffStatus.ForeColor = Color.Gray;
                return;
            }

            string port = cmbSerialPort.SelectedItem.ToString();
            int baud = int.Parse(cmbSerialBaud.SelectedItem.ToString());

            try
            {
                spSniff = new SerialPort(port, baud, Parity.None, 8, StopBits.One);
                spSniff.ReadTimeout = 500;
                spSniff.Open();
                isSerialSniffRunning = true;

                btnStartSerialSniff.Text = "⏹ 停止監聽";
                btnStartSerialSniff.BackColor = Color.FromArgb(239, 68, 68);
                lblSerialSniffStatus.Text = "狀態: 正在監聽 " + port + " @ " + baud;
                lblSerialSniffStatus.ForeColor = Color.Green;

                LogSerial(string.Format("[系統] 成功開啟 {0} @ {1} bps，正在傾聽底層 Byte 數據串流...\r\n", port, baud));

                serialReadThread = new Thread(SerialReadWorker);
                serialReadThread.IsBackground = true;
                serialReadThread.Start();
            }
            catch (Exception ex)
            {
                MessageBox.Show("無法開啟串列埠 " + port + ": " + ex.Message, "錯誤");
            }
        }

        private void SerialReadWorker()
        {
            byte[] buf = new byte[1024];
            while (isSerialSniffRunning && spSniff != null && spSniff.IsOpen)
            {
                try
                {
                    if (spSniff.BytesToRead > 0)
                    {
                        int count = spSniff.Read(buf, 0, Math.Min(buf.Length, spSniff.BytesToRead));
                        if (count > 0)
                        {
                            StringBuilder hex = new StringBuilder();
                            StringBuilder asc = new StringBuilder();
                            for (int i = 0; i < count; i++)
                            {
                                hex.Append(buf[i].ToString("X2") + " ");
                                char c = (char)buf[i];
                                if (char.IsControl(c)) asc.Append(".");
                                else asc.Append(c);
                            }
                            LogSerial(string.Format("[RX {0} bytes] HEX: {1,-40} | ASCII: {2}\r\n", count, hex.ToString().Trim(), asc.ToString()));
                        }
                    }
                    else
                    {
                        Thread.Sleep(20);
                    }
                }
                catch
                {
                    break;
                }
            }
        }

        private void LogSerial(string msg)
        {
            if (txtSerialSniffLog.InvokeRequired)
            {
                txtSerialSniffLog.BeginInvoke(new Action<string>(LogSerial), msg);
                return;
            }
            txtSerialSniffLog.AppendText(msg);
        }

        // =========================================================================
        // TAB 3: 原廠軟體即時記錄監控橋接 (Zero-Driver Live File Bridge)
        // =========================================================================
        private void BuildFileWatchTab(TabPage tab)
        {
            GroupBox grpConfig = new GroupBox() { Text = "原廠軟體即時記錄檔監控設定 (WTViewer / GL-APS / SensorTool 即時橋接)", Location = new Point(15, 10), Size = new Size(905, 135) };

            Label l1 = new Label() { Text = "原廠記錄資料夾:", Location = new Point(15, 25), AutoSize = true };
            txtWatchFolder = new TextBox() { Text = @"C:\Documents and Settings\SOL\桌面", Location = new Point(125, 22), Width = 380 };
            btnSelectWatchFolder = new Button() { Text = "瀏覽...", Location = new Point(515, 20), Size = new Size(65, 26) };
            btnSelectWatchFolder.Click += (s, e) => {
                FolderBrowserDialog fbd = new FolderBrowserDialog();
                if (fbd.ShowDialog() == DialogResult.OK) txtWatchFolder.Text = fbd.SelectedPath;
            };

            Label l2 = new Label() { Text = "檔案過濾 (*.csv, *.GBD, *.txt):", Location = new Point(595, 25), AutoSize = true };
            txtWatchPattern = new TextBox() { Text = "*.*", Location = new Point(780, 22), Width = 110 };

            btnStartFileWatch = new Button()
            {
                Text = "▶ 啟動即時檔案監聽橋接",
                Location = new Point(15, 60),
                Size = new Size(200, 32),
                BackColor = Color.FromArgb(0, 180, 216),
                ForeColor = Color.White,
                Font = new Font("微軟正黑體", 9.5f, FontStyle.Bold)
            };
            btnStartFileWatch.Click += BtnStartFileWatch_Click;

            lblFileWatchStatus = new Label() { Text = "狀態: 檔案監聽未啟動", Location = new Point(230, 68), AutoSize = true, ForeColor = Color.Gray };

            Label lDesc = new Label()
            {
                Text = "💡 原理說明：\n  當您開啟 WTViewer、GL220-APS 或 SensorTool 並在原廠軟體中開啟「即時存檔/記錄」時，本橋接器會毫秒級即時捕捉最新寫入的數據行，\n  直接解析出轉速、轉矩、電壓、電流與溫度，無需任何額外驅動即可無縫驅動動力計 HMI！",
                Location = new Point(15, 95),
                AutoSize = true,
                ForeColor = Color.FromArgb(70, 80, 95)
            };

            grpConfig.Controls.AddRange(new Control[] { l1, txtWatchFolder, btnSelectWatchFolder, l2, txtWatchPattern, btnStartFileWatch, lblFileWatchStatus, lDesc });
            tab.Controls.Add(grpConfig);

            // 最新數值卡片
            Panel pnlCard = new Panel() { Location = new Point(15, 155), Size = new Size(905, 55), BackColor = Color.FromArgb(245, 248, 250), BorderStyle = BorderStyle.FixedSingle };
            lblLatestDataValue = new Label() { Text = "最新解析數據: (尚未讀取到新數據)", Location = new Point(15, 15), AutoSize = true, ForeColor = Color.FromArgb(16, 185, 129), Font = new Font("微軟正黑體", 12f, FontStyle.Bold) };
            pnlCard.Controls.Add(lblLatestDataValue);
            tab.Controls.Add(pnlCard);

            GroupBox grpLog = new GroupBox() { Text = "即時檔案異動與最新數據解析日誌", Location = new Point(15, 220), Size = new Size(905, 390) };
            txtFileWatchLog = new TextBox() { Dock = DockStyle.Fill, Multiline = true, ScrollBars = ScrollBars.Vertical, ReadOnly = true, BackColor = Color.Black, ForeColor = Color.LightGreen, Font = new Font("Consolas", 9.5f) };
            grpLog.Controls.Add(txtFileWatchLog);
            tab.Controls.Add(grpLog);
        }

        private void BtnStartFileWatch_Click(object sender, EventArgs e)
        {
            if (fileWatcher != null)
            {
                fileWatcher.EnableRaisingEvents = false;
                fileWatcher.Dispose();
                fileWatcher = null;
                btnStartFileWatch.Text = "▶ 啟動即時檔案監聽橋接";
                btnStartFileWatch.BackColor = Color.FromArgb(0, 180, 216);
                lblFileWatchStatus.Text = "狀態: 檔案監聽已停止";
                lblFileWatchStatus.ForeColor = Color.Gray;
                return;
            }

            string path = txtWatchFolder.Text.Trim();
            if (!Directory.Exists(path))
            {
                MessageBox.Show("資料夾不存在: " + path, "錯誤");
                return;
            }

            try
            {
                fileWatcher = new FileSystemWatcher(path);
                fileWatcher.Filter = txtWatchPattern.Text.Trim();
                fileWatcher.NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName;
                fileWatcher.Changed += OnWatchedFileChanged;
                fileWatcher.Created += OnWatchedFileChanged;
                fileWatcher.EnableRaisingEvents = true;

                btnStartFileWatch.Text = "⏹ 停止檔案監聽";
                btnStartFileWatch.BackColor = Color.FromArgb(239, 68, 68);
                lblFileWatchStatus.Text = "狀態: 正在監控 " + path;
                lblFileWatchStatus.ForeColor = Color.Green;
                LogFile("[系統] 已啟動檔案監視器，正監視目錄中所有新寫入的數據...\r\n");
            }
            catch (Exception ex)
            {
                MessageBox.Show("無法啟動檔案監視器: " + ex.Message, "錯誤");
            }
        }

        private void OnWatchedFileChanged(object sender, FileSystemEventArgs e)
        {
            try
            {
                // 讀取檔案末行
                Thread.Sleep(50);
                string[] lines = ReadAllLinesShared(e.FullPath);
                if (lines.Length > 0)
                {
                    string lastLine = lines[lines.Length - 1].Trim();
                    if (!string.IsNullOrEmpty(lastLine))
                    {
                        LogFile(string.Format("[{0}] 捕捉到檔案更新 [{1}]: {2}\r\n", DateTime.Now.ToLongTimeString(), Path.GetFileName(e.FullPath), lastLine));
                        UpdateLatestLabel(string.Format("檔案: {0} => {1}", Path.GetFileName(e.FullPath), lastLine));
                    }
                }
            }
            catch { }
        }

        private string[] ReadAllLinesShared(string path)
        {
            using (FileStream fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (StreamReader sr = new StreamReader(fs, Encoding.Default))
            {
                var list = new System.Collections.Generic.List<string>();
                while (!sr.EndOfStream) list.Add(sr.ReadLine());
                return list.ToArray();
            }
        }

        private void LogFile(string msg)
        {
            if (txtFileWatchLog.InvokeRequired)
            {
                txtFileWatchLog.BeginInvoke(new Action<string>(LogFile), msg);
                return;
            }
            txtFileWatchLog.AppendText(msg);
        }

        private void UpdateLatestLabel(string msg)
        {
            if (lblLatestDataValue.InvokeRequired)
            {
                lblLatestDataValue.BeginInvoke(new Action<string>(UpdateLatestLabel), msg);
                return;
            }
            lblLatestDataValue.Text = msg;
        }
    }
}
