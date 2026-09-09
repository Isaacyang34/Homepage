using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;

namespace DynamometerHMI
{
    // ============================================================
    // Dynamometer_ViewerMode.cs
    // 主程式「VIEWER 遠端檢視模式」引擎
    // 零實體硬體佔用、純接收 Master 主控端或雲端數據推播
    // 唯讀防護、即時驅動桌面版完整儀表、曲線與溫度陣列
    // ============================================================
    public partial class MainForm
    {
        // ── 靜態全域旗標與變數 ──────────────────────────────────────
        public static bool isViewerMode = false;
        public static string viewerTargetUrl = "http://localhost:8080";

        // ── VIEWER 執行個體變數 ────────────────────────────────────
        public bool isViewerClientRunning = false;
        private Thread viewerClientThread = null;
        private DateTime viewerLastSyncSuccessTime = DateTime.MinValue;
        private int viewerSyncErrorCount = 0;

        // VIEWER 頂部導航專用控制項
        private FlowLayoutPanel flpViewerTopBar = null;
        private Label lblViewerBadge = null;
        private TextBox txtViewerUrlInput = null;
        private Button btnViewerConnect = null;
        private Button btnViewerCloudToggle = null;
        private Button btnViewerOpenBrowser = null;
        private Label lblViewerSyncStatus = null;

        // 預設 Firebase 雲端資料庫網址
        private const string FIREBASE_DEFAULT_URL = "https://dynamometer-live-default-rtdb.asia-southeast1.firebasedatabase.app/live.json";

        // ── VIEWER 模式初始化 ──────────────────────────────────────
        public void InitializeViewerMode()
        {
            try
            {
                // 1. 設定視窗標題與主標題
                this.Text = "Dynamometer HMI Pro [👀 遠端檢視端 - VIEWER Mode (純唯讀)]";
                if (lblAppTitle != null)
                {
                    lblAppTitle.Text = "⚡ DYNAMOMETER [VIEWER 遠端監看]";
                    lblAppTitle.ForeColor = Color.FromArgb(56, 189, 248);
                }

                // 2. 鎖定實體控制按鈕 (防止遠端誤觸下發硬體指令造成工安問題)
                LockControlsForViewerMode();

                // 3. 在頂部工具列建置專屬 VIEWER 連線控制列
                BuildViewerTopBar();

                // 4. 啟動非同步遠端遙測接收同步 Worker
                StartViewerClientSync(viewerTargetUrl);

                WriteHmiLog("VIEWER_INIT", "【VIEWER 模式啟動】已切換為純唯讀檢視架構，零硬體佔用，開始連線目標: " + viewerTargetUrl);
            }
            catch (Exception ex)
            {
                WriteHmiLog("VIEWER_ERROR", "VIEWER 初始化異常: " + ex.Message);
            }
        }

        // ── 唯讀鎖定所有實體控制與參數修改元件 ──────────────────────────────
        private void LockControlsForViewerMode()
        {
            var ttLock = new ToolTip();

            // 1. 鎖定頂部主控端特定功能按鈕
            Action<Control, string> safeLock = (c, name) =>
            {
                if (c == null || c.IsDisposed) return;
                c.Enabled = false;
                ttLock.SetToolTip(c, "【👀 唯讀檢視模式】" + name + " 已鎖定，防止修改。");
            };

            safeLock(btnMasterConnectAll, "設備連線");
            safeLock(btnClosedLoopModal, "閉迴路設定");
            safeLock(btnCalibrationSettings, "儀表校正");
            safeLock(btnDeviceSettings, "設備通訊設定");
            safeLock(btnRecordRawTop, "記錄");

            // 2. 遞迴深層遍歷全視窗所有控制項，對所有輸入、調節與操作元件進行唯讀鎖定
            DeepLockControlTreeForViewer(this, ttLock);

            // 3. 在 tabControl 的分頁切換時，自動重新檢查並維持鎖定狀態
            if (tabControl != null)
            {
                tabControl.SelectedIndexChanged += (s, e) => {
                    DeepLockControlTreeForViewer(tabControl.SelectedTab, ttLock);
                };
            }
        }

        /// <summary>
        /// 遞迴遍歷控制項樹，將所有參數設定元件 (NumericUpDown, ComboBox, CheckBox, RadioButton, TrackBar, Button) 徹底鎖定
        /// </summary>
        private void DeepLockControlTreeForViewer(Control parent, ToolTip ttLock)
        {
            if (parent == null) return;

            foreach (Control c in parent.Controls)
            {
                // 排除 VIEWER 頂部導航列中的專屬控制項
                if (c == flpViewerTopBar || c.Parent == flpViewerTopBar || (c.Parent != null && c.Parent.Parent == flpViewerTopBar))
                {
                    continue;
                }

                // A. 數值輸入框、下拉清單、核取方塊、單選按鈕、滑動條：100% 停用禁止點選修改
                if (c is NumericUpDown || c is ComboBox || c is CheckBox || c is RadioButton || c is TrackBar)
                {
                    c.Enabled = false;
                    ttLock.SetToolTip(c, "【👀 唯讀檢視模式】參數已鎖定，禁止修改。");
                }
                // B. 文字輸入框：設為唯讀並變更背景色提示
                else if (c is TextBox)
                {
                    TextBox txt = (TextBox)c;
                    if (txt != txtViewerUrlInput)
                    {
                        txt.ReadOnly = true;
                        ttLock.SetToolTip(txt, "【👀 唯讀檢視模式】純唯讀，禁止編輯。");
                    }
                }
                // C. 按鈕：除 VIEWER 專屬導航按鈕外，所有操作/下發/設定按鈕全面禁用
                else if (c is Button)
                {
                    Button btn = (Button)c;
                    if (btn != btnViewerOpenBrowser && btn != btnViewerCloudToggle && btn != btnViewerConnect)
                    {
                        btn.Enabled = false;
                        ttLock.SetToolTip(btn, "【👀 唯讀檢視模式】操作功能已鎖定。");
                    }
                }
                // D. 表格 DataGridView：設為唯讀，允許滾動查看，禁止修改儲存格
                else if (c is DataGridView)
                {
                    DataGridView dgv = (DataGridView)c;
                    dgv.ReadOnly = true;
                    dgv.AllowUserToAddRows = false;
                    dgv.AllowUserToDeleteRows = false;
                }

                // 遞迴深層子容器 (GroupBox, Panel, SplitContainer 等)
                if (c.HasChildren)
                {
                    DeepLockControlTreeForViewer(c, ttLock);
                }
            }
        }

        // ── 建置頂部 VIEWER 專屬導航控制列 ────────────────────────
        private void BuildViewerTopBar()
        {
            if (pnlTop == null) return;

            // 移除原本的主控端專用右側面板
            for (int i = pnlTop.Controls.Count - 1; i >= 0; i--)
            {
                if (pnlTop.Controls[i] is FlowLayoutPanel)
                {
                    pnlTop.Controls.RemoveAt(i);
                    break;
                }
            }

            flpViewerTopBar = new FlowLayoutPanel()
            {
                Dock = DockStyle.Right,
                FlowDirection = FlowDirection.RightToLeft,
                WrapContents = false,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                BackColor = Color.Transparent,
                Margin = new Padding(0),
                Padding = new Padding(2)
            };

            // 1. 開啟瀏覽器網頁儀表按鈕
            btnViewerOpenBrowser = new Button()
            {
                Text = "🌐 開啟網頁儀表",
                Size = new Size(115, 34),
                Margin = new Padding(3, 1, 3, 1),
                BackColor = Color.FromArgb(79, 70, 229),
                ForeColor = Color.White,
                Font = new Font("微軟正黑體", 9f, FontStyle.Bold),
                Cursor = Cursors.Hand,
                FlatStyle = FlatStyle.Flat
            };
            btnViewerOpenBrowser.FlatAppearance.BorderSize = 0;
            btnViewerOpenBrowser.Click += (s, e) => {
                try {
                    string u = viewerTargetUrl;
                    if (u.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                    {
                        // 若為 Firebase，開啟本地 WebMonitor.html
                        string localPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "WebMonitor.html");
                        if (File.Exists(localPath)) System.Diagnostics.Process.Start(localPath);
                        else System.Diagnostics.Process.Start(u);
                    }
                    else
                    {
                        System.Diagnostics.Process.Start(u);
                    }
                } catch { }
            };

            // 2. 切換連線模式按鈕 (本機 LAN / Firebase 雲端)
            btnViewerCloudToggle = new Button()
            {
                Text = "☁️ 雲端模式",
                Size = new Size(95, 34),
                Margin = new Padding(3, 1, 3, 1),
                BackColor = Color.FromArgb(37, 99, 235),
                ForeColor = Color.White,
                Font = new Font("微軟正黑體", 9f, FontStyle.Bold),
                Cursor = Cursors.Hand,
                FlatStyle = FlatStyle.Flat
            };
            btnViewerCloudToggle.FlatAppearance.BorderSize = 0;
            btnViewerCloudToggle.Click += (s, e) => {
                if (txtViewerUrlInput.Text.Contains("firebasedatabase.app"))
                {
                    txtViewerUrlInput.Text = "http://localhost:8080";
                    btnViewerCloudToggle.Text = "☁️ 雲端模式";
                    btnViewerCloudToggle.BackColor = Color.FromArgb(37, 99, 235);
                }
                else
                {
                    txtViewerUrlInput.Text = FIREBASE_DEFAULT_URL;
                    btnViewerCloudToggle.Text = "🏠 區網模式";
                    btnViewerCloudToggle.BackColor = Color.FromArgb(16, 185, 129);
                }
                StartViewerClientSync(txtViewerUrlInput.Text);
            };

            // 3. 連線 / 重新整理按鈕
            btnViewerConnect = new Button()
            {
                Text = "🔄 連線",
                Size = new Size(65, 34),
                Margin = new Padding(3, 1, 3, 1),
                BackColor = Color.FromArgb(16, 185, 129),
                ForeColor = Color.White,
                Font = new Font("微軟正黑體", 9.5f, FontStyle.Bold),
                Cursor = Cursors.Hand,
                FlatStyle = FlatStyle.Flat
            };
            btnViewerConnect.FlatAppearance.BorderSize = 0;
            btnViewerConnect.Click += (s, e) => {
                StartViewerClientSync(txtViewerUrlInput.Text);
            };

            // 4. 連線目標 URL 輸入框
            txtViewerUrlInput = new TextBox()
            {
                Text = viewerTargetUrl,
                Size = new Size(220, 28),
                Margin = new Padding(3, 4, 3, 1),
                Font = new Font("Consolas", 10f),
                BackColor = Color.FromArgb(15, 23, 42),
                ForeColor = Color.FromArgb(226, 232, 240)
            };
            txtViewerUrlInput.KeyDown += (s, e) => {
                if (e.KeyCode == Keys.Enter)
                {
                    e.SuppressKeyPress = true;
                    StartViewerClientSync(txtViewerUrlInput.Text);
                }
            };

            // 5. 連線狀態指示燈標籤
            lblViewerSyncStatus = new Label()
            {
                Text = "連線中...",
                AutoSize = false,
                Size = new Size(100, 34),
                Margin = new Padding(3, 1, 3, 1),
                BackColor = Color.FromArgb(30, 41, 59),
                ForeColor = Color.FromArgb(234, 179, 8),
                Font = new Font("微軟正黑體", 9f, FontStyle.Bold),
                TextAlign = ContentAlignment.MiddleCenter
            };

            // 6. VIEWER 模式徽章標籤
            lblViewerBadge = new Label()
            {
                Text = "👀 VIEWER 唯讀",
                AutoSize = false,
                Size = new Size(110, 34),
                Margin = new Padding(3, 1, 3, 1),
                BackColor = Color.FromArgb(139, 92, 246),
                ForeColor = Color.White,
                Font = new Font("微軟正黑體", 9.5f, FontStyle.Bold),
                TextAlign = ContentAlignment.MiddleCenter
            };

            // 依 FlowDirection = RightToLeft 排列
            flpViewerTopBar.Controls.Add(btnViewerOpenBrowser);
            flpViewerTopBar.Controls.Add(btnViewerCloudToggle);
            flpViewerTopBar.Controls.Add(btnViewerConnect);
            flpViewerTopBar.Controls.Add(txtViewerUrlInput);
            flpViewerTopBar.Controls.Add(lblViewerSyncStatus);
            flpViewerTopBar.Controls.Add(lblViewerBadge);

            pnlTop.Controls.Add(flpViewerTopBar);
        }

        // ── 啟動 VIEWER 遠端遙測接收 Worker ───────────────────────
        public void StartViewerClientSync(string targetUrl)
        {
            if (string.IsNullOrEmpty(targetUrl)) return;
            viewerTargetUrl = targetUrl.Trim();
            if (txtViewerUrlInput != null && txtViewerUrlInput.Text != viewerTargetUrl)
                txtViewerUrlInput.Text = viewerTargetUrl;

            StopViewerClientSync();

            isViewerClientRunning = true;
            viewerSyncErrorCount = 0;
            viewerClientThread = new Thread(ViewerClientSyncLoop)
            {
                IsBackground = true,
                Name = "ViewerClientSyncThread"
            };
            viewerClientThread.Start();
        }

        // ── 停止 VIEWER 遙測 Worker ──────────────────────────────
        public void StopViewerClientSync()
        {
            isViewerClientRunning = false;
            if (viewerClientThread != null)
            {
                try { viewerClientThread.Abort(); } catch { }
                viewerClientThread = null;
            }
        }

        // ── VIEWER 背景遙測輪詢迴圈 ──────────────────────────────
        private void ViewerClientSyncLoop()
        {
            // 強制啟用 TLS 1.2 (3072) / TLS 1.1 (768)，相容 Google Firebase HTTPS 連線
            try
            {
                ServicePointManager.SecurityProtocol = (SecurityProtocolType)3072 | (SecurityProtocolType)768 | SecurityProtocolType.Tls;
                ServicePointManager.ServerCertificateValidationCallback = (sender, cert, chain, sslPolicyErrors) => true;
            }
            catch { }

            bool wasOnline = false;
            DateTime lastErrLogTime = DateTime.MinValue;

            while (isViewerClientRunning)
            {
                DateTime reqStart = DateTime.Now;
                try
                {
                    string fetchUrl = viewerTargetUrl;
                    if (!fetchUrl.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                    {
                        fetchUrl = fetchUrl.TrimEnd('/') + "/api/status";
                    }

                    HttpWebRequest req = (HttpWebRequest)WebRequest.Create(fetchUrl);
                    req.Method = "GET";
                    req.Timeout = 2500;
                    req.ReadWriteTimeout = 2500;
                    req.Headers.Add("Cache-Control", "no-cache");

                    using (HttpWebResponse resp = (HttpWebResponse)req.GetResponse())
                    using (StreamReader reader = new StreamReader(resp.GetResponseStream(), Encoding.UTF8))
                    {
                        string json = reader.ReadToEnd();
                        if (!string.IsNullOrEmpty(json) && json.Trim().Length > 10)
                        {
                            ApplyViewerTelemetryJson(json);
                            viewerLastSyncSuccessTime = DateTime.Now;
                            viewerSyncErrorCount = 0;

                            int pingMs = (int)(DateTime.Now - reqStart).TotalMilliseconds;
                            UpdateViewerStatusUI(true, pingMs, null);

                            if (!wasOnline)
                            {
                                wasOnline = true;
                                WriteHmiLog("VIEWER", "[OK] 遠端遙測連線同步成功 (" + fetchUrl + " RTT=" + pingMs + "ms)");
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    viewerSyncErrorCount++;
                    UpdateViewerStatusUI(false, 0, ex.Message);

                    if (wasOnline || (DateTime.Now - lastErrLogTime).TotalSeconds >= 15)
                    {
                        wasOnline = false;
                        lastErrLogTime = DateTime.Now;
                        WriteHmiLog("VIEWER", "[ERR] 遠端同步中斷: " + ex.Message + " (目標: " + viewerTargetUrl + ")");
                    }
                }

                Thread.Sleep(500); // 500ms 刷新率 (2Hz 高頻即時更新)
            }
        }

        // ── 更新 VIEWER 頂部狀態 UI ──────────────────────────────
        private void UpdateViewerStatusUI(bool isOnline, int pingMs, string errMsg)
        {
            if (this.InvokeRequired)
            {
                try { this.BeginInvoke(new Action(() => UpdateViewerStatusUI(isOnline, pingMs, errMsg))); } catch { }
                return;
            }

            if (lblViewerSyncStatus != null)
            {
                if (isOnline)
                {
                    lblViewerSyncStatus.Text = string.Format("🟢 {0}ms", pingMs);
                    lblViewerSyncStatus.ForeColor = Color.FromArgb(52, 211, 153);
                    lblViewerSyncStatus.BackColor = Color.FromArgb(15, 23, 42);
                }
                else
                {
                    if (viewerSyncErrorCount > 3)
                    {
                        lblViewerSyncStatus.Text = "🔴 離線中";
                        lblViewerSyncStatus.ForeColor = Color.FromArgb(248, 113, 113);
                        lblViewerSyncStatus.BackColor = Color.FromArgb(69, 10, 10);
                    }
                    else
                    {
                        lblViewerSyncStatus.Text = "🟡 連線中...";
                        lblViewerSyncStatus.ForeColor = Color.FromArgb(250, 204, 21);
                        lblViewerSyncStatus.BackColor = Color.FromArgb(66, 32, 6);
                    }
                }
            }
        }

        // ── 遙測 JSON 解析並更新至主畫面各控制項與趨勢圖 ─────────
        private void ApplyViewerTelemetryJson(string json)
        {
            try
            {
                // 解析各項數值
                double spd = ExtractJsonDouble(json, "speed", 0.0);
                double trq = ExtractJsonDouble(json, "torque", 0.0);
                double mPwr = ExtractJsonDouble(json, "mech_power", 0.0);
                double ePwr = ExtractJsonDouble(json, "elec_power", 0.0);
                double eff = ExtractJsonDouble(json, "efficiency", 0.0);
                double ktVal = ExtractJsonDouble(json, "kt", 0.0);
                double curSig = ExtractJsonDouble(json, "current_sigma", 0.0);
                double voltSig = ExtractJsonDouble(json, "voltage_sigma", 0.0);
                double pfVal = ExtractJsonDouble(json, "pf", 0.0);
                double kebCurA = ExtractJsonDouble(json, "keb_current_a", 0.0);
                double kebCurB = ExtractJsonDouble(json, "keb_current_b", 0.0);
                double tempMax = ExtractJsonDouble(json, "temp_max", 0.0);

                string modeStr = ExtractJsonString(json, "mode", "IDLE");
                string statusStr = ExtractJsonString(json, "status_text", "監看中");
                string phaseStr = ExtractJsonString(json, "phase_text", "");

                int dutyEl = (int)ExtractJsonDouble(json, "duty_elapsed_sec", 0);
                int dutyTot = (int)ExtractJsonDouble(json, "duty_total_sec", 0);
                int tnStep = (int)ExtractJsonDouble(json, "tn_step", 0);

                double[] temps = ExtractJsonDoubleArray(json, "temp_ch");

                // 更新至 MainForm 實體遙測變數
                actSpeed = spd;
                actTorque = trq;
                actMechPower = mPwr;
                actElecPower = ePwr;
                actEfficiency = eff;
                actKt = ktVal;
                actCurrentSigma = curSig;
                actVoltageSigma = voltSig;
                actPf = pfVal;
                kebCurrent1 = kebCurA;
                kebCurrent2 = kebCurB;
                actTemp = tempMax;

                webRemoteMode = modeStr;
                webRemoteStatusText = statusStr;
                webRemotePhaseText = phaseStr;
                dutyElapsedSec = dutyEl;
                dutyTotalSec = dutyTot;
                tnCurrentStep = tnStep;

                if (temps != null && temps.Length > 0 && gbdChTemps != null)
                {
                    Array.Copy(temps, gbdChTemps, Math.Min(temps.Length, gbdChTemps.Length));
                }

                // 餵入走勢圖表
                DateTime now = DateTime.Now;
                if (trqSpdChart != null && !trqSpdChart.IsDisposed)
                {
                    trqSpdChart.AddSample(now, Math.Abs(trq), Math.Abs(trq), Math.Abs(spd), Math.Abs(spd), false);
                }
                if (motorTempChart != null && !motorTempChart.IsDisposed && tempMax > 0.0)
                {
                    motorTempChart.AddSample(now, tempMax);
                }
                if (gbdTrendChart != null && !gbdTrendChart.IsDisposed && gbdChTemps != null)
                {
                    gbdTrendChart.AddSample(now, gbdChTemps);
                }
            }
            catch { }
        }

        #region 輕量零相依 JSON 欄位提取工具函數

        private static double ExtractJsonDouble(string json, string key, double defaultVal = 0.0)
        {
            try
            {
                Match m = Regex.Match(json, "\"" + Regex.Escape(key) + "\"\\s*:\\s*(-?[0-9]+(\\.[0-9]+)?)", RegexOptions.IgnoreCase);
                if (m.Success)
                {
                    double d;
                    if (double.TryParse(m.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out d))
                        return d;
                }
            }
            catch { }
            return defaultVal;
        }

        private static string ExtractJsonString(string json, string key, string defaultVal = "")
        {
            try
            {
                Match m = Regex.Match(json, "\"" + Regex.Escape(key) + "\"\\s*:\\s*\"([^\"]*)\"", RegexOptions.IgnoreCase);
                if (m.Success)
                {
                    return m.Groups[1].Value;
                }
            }
            catch { }
            return defaultVal;
        }

        private static double[] ExtractJsonDoubleArray(string json, string key)
        {
            try
            {
                Match m = Regex.Match(json, "\"" + Regex.Escape(key) + "\"\\s*:\\s*\\[([^\\]]*)\\]", RegexOptions.IgnoreCase);
                if (m.Success)
                {
                    string[] parts = m.Groups[1].Value.Split(',');
                    double[] arr = new double[parts.Length];
                    for (int i = 0; i < parts.Length; i++)
                    {
                        double d;
                        if (double.TryParse(parts[i].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out d))
                            arr[i] = d;
                    }
                    return arr;
                }
            }
            catch { }
            return new double[0];
        }

        #endregion
    }
}
