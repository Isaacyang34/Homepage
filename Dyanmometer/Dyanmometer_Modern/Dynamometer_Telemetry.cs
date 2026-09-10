using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace DynamometerHMI
{
    public partial class MainForm : Form
    {
        // =========================================================================
        // 分頁 6: 系統完整運轉與通訊日誌 (System Log Viewer)
        // =========================================================================
        private void BuildLogTab(TabPage tab)
        {
            TableLayoutPanel tableLog = new TableLayoutPanel()
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 2,
                BackColor = Color.FromArgb(240, 243, 246),
                Margin = new Padding(0),
                Padding = new Padding(4)
            };
            tableLog.RowStyles.Add(new RowStyle(SizeType.Absolute, 45f));
            tableLog.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

            Panel pnlLogTools = new Panel() { Dock = DockStyle.Fill, BackColor = Color.FromArgb(245, 248, 252), BorderStyle = BorderStyle.FixedSingle, Margin = new Padding(0) };

            Label lInfo = new Label()
            {
                Text = "動力計即時通訊電文、閉迴路追隨與硬體交握紀錄 (支援即時檢視與完整文字/CSV匯出)",
                Location = new Point(10, 12),
                AutoSize = true,
                Font = new Font("微軟正黑體", 9.5f, FontStyle.Bold),
                ForeColor = Color.FromArgb(20, 50, 100)
            };

            // 使用 FlowLayoutPanel 排列按鈕，確保任何視窗寬度都能正常顯示 (啟用 AutoScroll 防裁切)
            FlowLayoutPanel flowLogTools = new FlowLayoutPanel()
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                Padding = new Padding(6, 5, 6, 5),
                AutoSize = false,
                AutoScroll = true
            };

            Label lInfo2 = new Label()
            {
                Text = "動力計即時通訊電文、閉迴路追隨與硬體交握紀錄",
                AutoSize = true,
                Font = new Font("微軟正黑體", 9.5f, FontStyle.Bold),
                ForeColor = Color.FromArgb(20, 50, 100),
                Margin = new Padding(0, 6, 20, 0)
            };

            Button btnOpenLogDir2 = new Button()
            {
                Text = "開啟日誌目錄",
                Size = new Size(120, 28),
                BackColor = Color.FromArgb(55, 65, 81),
                ForeColor = Color.White,
                Font = new Font("微軟正黑體", 9f, FontStyle.Bold),
                Margin = new Padding(0, 0, 4, 0)
            };
            btnOpenLogDir2.Click += (s, e) => OpenLogsFolder();

            Button btnExport2 = new Button()
            {
                Text = "匯出 LOG (TXT/CSV)",
                Size = new Size(165, 28),
                BackColor = Color.FromArgb(0, 120, 215),
                ForeColor = Color.White,
                Font = new Font("微軟正黑體", 9f, FontStyle.Bold),
                Margin = new Padding(0, 0, 4, 0)
            };
            btnExport2.Click += BtnExportLogs_Click;

            Button btnClear2 = new Button()
            {
                Text = "清空日誌",
                Size = new Size(90, 28),
                BackColor = Color.FromArgb(239, 68, 68),
                ForeColor = Color.White,
                Font = new Font("微軟正黑體", 9f, FontStyle.Bold),
                Margin = new Padding(0, 0, 4, 0)
            };
            btnClear2.Click += (s, e) => {
                lock (hmiLogLock)
                {
                    memoryLogs.Clear();
                    memoryLogs.TrimExcess();
                }
                if (txtFullLog != null) txtFullLog.Clear();
                if (lblMiniLogText != null) lblMiniLogText.Text = "日誌已清空";
                GC.Collect();
            };

            Button btnUploadLogToCloud2 = new Button()
            {
                Text = "☁️ 上傳日誌至雲端",
                Size = new Size(130, 28),
                BackColor = Color.FromArgb(16, 185, 129),
                ForeColor = Color.White,
                Font = new Font("微軟正黑體", 9f, FontStyle.Bold),
                Margin = new Padding(0, 0, 4, 0)
            };
            btnUploadLogToCloud2.Click += (s, e) => UploadLatestLogToCloudAsync(true);

            Label lblCloudDays = new Label()
            {
                Text = "☁️ 雲端保留:",
                AutoSize = true,
                Font = new Font("微軟正黑體", 9f, FontStyle.Bold),
                ForeColor = Color.FromArgb(14, 165, 233),
                Margin = new Padding(6, 6, 2, 0)
            };
            NumericUpDown numCloudDays = new NumericUpDown()
            {
                Minimum = 1,
                Maximum = 90,
                Value = Math.Max(1, Math.Min(90, cloudLogMaxDays)),
                Width = 50,
                Font = new Font("Consolas", 9.5f, FontStyle.Bold),
                BackColor = Color.FromArgb(241, 245, 249),
                ForeColor = Color.FromArgb(14, 165, 233)
            };
            numCloudDays.ValueChanged += (s, e) => {
                cloudLogMaxDays = (int)numCloudDays.Value;
                SaveLayoutConfig();
            };
            Label lblCloudDaysUnit = new Label()
            {
                Text = "天 /",
                AutoSize = true,
                Font = new Font("微軟正黑體", 9f),
                Margin = new Padding(2, 6, 2, 0)
            };

            NumericUpDown numCloudCount = new NumericUpDown()
            {
                Minimum = 5,
                Maximum = 500,
                Increment = 5,
                Value = Math.Max(5, Math.Min(500, cloudLogMaxHistoryCount)),
                Width = 58,
                Font = new Font("Consolas", 9.5f, FontStyle.Bold),
                BackColor = Color.FromArgb(241, 245, 249),
                ForeColor = Color.FromArgb(14, 165, 233)
            };
            numCloudCount.ValueChanged += (s, e) => {
                cloudLogMaxHistoryCount = (int)numCloudCount.Value;
                SaveLayoutConfig();
            };
            Label lblCloudCountUnit = new Label()
            {
                Text = "筆",
                AutoSize = true,
                Font = new Font("微軟正黑體", 9f),
                Margin = new Padding(2, 6, 4, 0)
            };

            Button btnPurgeCloud = new Button()
            {
                Text = "🧹 清理雲端",
                Size = new Size(88, 28),
                BackColor = Color.FromArgb(100, 116, 139),
                ForeColor = Color.White,
                Font = new Font("微軟正黑體", 8.5f, FontStyle.Bold),
                Margin = new Padding(0, 0, 4, 0)
            };
            btnPurgeCloud.Click += (s, e) => PurgeCloudLogsAsync(true);

            Label lblLocalRetention = new Label()
            {
                Text = "💾 本地保留:",
                AutoSize = true,
                Font = new Font("微軟正黑體", 9f, FontStyle.Bold),
                ForeColor = Color.FromArgb(16, 149, 193),
                Margin = new Padding(6, 6, 2, 0)
            };
            NumericUpDown numLocalCount = new NumericUpDown()
            {
                Minimum = 5,
                Maximum = 500,
                Increment = 5,
                Value = Math.Max(5, Math.Min(500, localLogMaxHistoryCount)),
                Width = 58,
                Font = new Font("Consolas", 9.5f, FontStyle.Bold),
                BackColor = Color.FromArgb(241, 245, 249),
                ForeColor = Color.FromArgb(16, 149, 193)
            };
            numLocalCount.ValueChanged += (s, e) => {
                localLogMaxHistoryCount = (int)numLocalCount.Value;
                SaveLayoutConfig();
            };
            Label lblLocalUnit = new Label()
            {
                Text = "筆",
                AutoSize = true,
                Font = new Font("微軟正黑體", 9f),
                Margin = new Padding(2, 6, 4, 0)
            };
            Button btnPurgeLocal = new Button()
            {
                Text = "🧹 清理本地",
                Size = new Size(88, 28),
                BackColor = Color.FromArgb(71, 85, 105),
                ForeColor = Color.White,
                Font = new Font("微軟正黑體", 8.5f, FontStyle.Bold),
                Margin = new Padding(0, 0, 4, 0)
            };
            btnPurgeLocal.Click += (s, e) => PurgeLocalLogs(true);

            Label lblKebFreq = new Label()
            {
                Text = "⚡ KEB更新:",
                AutoSize = true,
                Font = new Font("微軟正黑體", 9f, FontStyle.Bold),
                ForeColor = Color.FromArgb(79, 70, 229),
                Margin = new Padding(6, 6, 2, 0)
            };
            NumericUpDown numKebFreqLog = new NumericUpDown()
            {
                Minimum = 100,
                Maximum = 60000,
                Increment = 100,
                Value = kebPollingIntervalMs,
                Width = 75,
                Font = new Font("Consolas", 9.5f, FontStyle.Bold),
                BackColor = Color.FromArgb(241, 245, 249),
                ForeColor = Color.FromArgb(79, 70, 229)
            };
            numKebFreqLog.ValueChanged += (s, e) => {
                kebPollingIntervalMs = (int)numKebFreqLog.Value;
                if (numKebPollingInterval != null && numKebPollingInterval.Value != numKebFreqLog.Value)
                    numKebPollingInterval.Value = numKebFreqLog.Value;
                SaveLayoutConfig();
                WriteHmiLog("CONFIG", "已更新 KEB 右側參數輪詢週期為: " + kebPollingIntervalMs + " ms");
            };
            Label lblKebFreqMs = new Label()
            {
                Text = "ms",
                AutoSize = true,
                Font = new Font("微軟正黑體", 9f),
                Margin = new Padding(2, 6, 4, 0)
            };

            Label lblBrakeThreshLog = new Label()
            {
                Text = "🛑 停機門檻:",
                AutoSize = true,
                Font = new Font("微軟正黑體", 9f, FontStyle.Bold),
                ForeColor = Color.FromArgb(185, 28, 28),
                Margin = new Padding(6, 6, 2, 0)
            };
            numBrakeThreshLog = new NumericUpDown()
            {
                Minimum = 10,
                Maximum = 5000,
                Increment = 50,
                Value = Math.Max(10, Math.Min(5000, autoStopBrakeThresholdRpm)),
                Width = 70,
                Font = new Font("Consolas", 9.5f, FontStyle.Bold),
                BackColor = Color.FromArgb(241, 245, 249),
                ForeColor = Color.FromArgb(185, 28, 28)
            };
            numBrakeThreshLog.ValueChanged += (s, e) => {
                autoStopBrakeThresholdRpm = numBrakeThreshLog.Value;
                SaveLayoutConfig();
                WriteHmiLog("SAFETY_CONFIG", string.Format("已更新自動停機加載煞車卸載門檻為: {0:F0} rpm", autoStopBrakeThresholdRpm));
            };
            Label lblBrakeThreshRpm = new Label()
            {
                Text = "rpm",
                AutoSize = true,
                Font = new Font("微軟正黑體", 9f),
                Margin = new Padding(2, 6, 4, 0)
            };

            flowLogTools.Controls.AddRange(new Control[] {
                lInfo2, btnOpenLogDir2, btnExport2, btnClear2, btnUploadLogToCloud2,
                lblCloudDays, numCloudDays, lblCloudDaysUnit, numCloudCount, lblCloudCountUnit, btnPurgeCloud,
                lblLocalRetention, numLocalCount, lblLocalUnit, btnPurgeLocal,
                lblKebFreq, numKebFreqLog, lblKebFreqMs, lblBrakeThreshLog, numBrakeThreshLog, lblBrakeThreshRpm
            });
            pnlLogTools.Controls.Add(flowLogTools);
            tableLog.Controls.Add(pnlLogTools, 0, 0);
            TabControl tabLogSub = new TabControl() { Dock = DockStyle.Fill, Margin = new Padding(0, 4, 0, 0) };

            // 子分頁 1: 全系統運作與事件日誌
            TabPage tabSubSys = new TabPage("全系統運作與事件日誌 (System Logs)") { BackColor = Color.FromArgb(240, 243, 246) };
            txtFullLog = new TextBox()
            {
                Dock = DockStyle.Fill,
                Multiline = true,
                ScrollBars = ScrollBars.Vertical,
                ReadOnly = true,
                BackColor = Color.Black,
                ForeColor = Color.LightGreen,
                Font = new Font("Consolas", 9.5f),
                Margin = new Padding(0)
            };
            txtHmiKebLog = txtFullLog;
            tabSubSys.Controls.Add(txtFullLog);

            // 子分頁 2: KEB 驅動器讀回參數獨立日誌 (專屬獨立展示)
            TabPage tabSubKeb = new TabPage("KEB 驅動器讀回參數獨立日誌 (KEB Hardware Config)") { BackColor = Color.FromArgb(240, 243, 246) };
            txtKebConfigLog = new TextBox()
            {
                Dock = DockStyle.Fill,
                Multiline = true,
                ScrollBars = ScrollBars.Vertical,
                ReadOnly = true,
                BackColor = Color.FromArgb(15, 23, 42),
                ForeColor = Color.FromArgb(56, 189, 248),
                Font = new Font("Consolas", 10f),
                Margin = new Padding(0)
            };
            tabSubKeb.Controls.Add(txtKebConfigLog);

            tabLogSub.TabPages.Add(tabSubSys);
            tabLogSub.TabPages.Add(tabSubKeb);

            tableLog.Controls.Add(tabLogSub, 0, 1);
            tab.Controls.Add(tableLog);
        }

        private void BtnExportLogs_Click(object sender, EventArgs e)
        {
            try
            {
                SaveFileDialog sfd = new SaveFileDialog()
                {
                    Title = "匯出動力計系統運轉日誌",
                    Filter = "文字日誌檔 (*.log;*.txt)|*.log;*.txt|CSV 試算表 (*.csv)|*.csv|所有檔案 (*.*)|*.*",
                    FileName = string.Format("Dyno_System_Log_{0}.log", DateTime.Now.ToString("yyyyMMdd_HHmmss"))
                };

                if (sfd.ShowDialog() == DialogResult.OK)
                {
                    List<string> logsToExport;
                    lock (hmiLogLock)
                    {
                        logsToExport = new List<string>(memoryLogs);
                    }
                    if (logsToExport.Count == 0 && txtFullLog != null && !string.IsNullOrEmpty(txtFullLog.Text))
                    {
                        File.WriteAllText(sfd.FileName, txtFullLog.Text, Encoding.UTF8);
                    }
                    else
                    {
                        File.WriteAllLines(sfd.FileName, logsToExport.ToArray(), Encoding.UTF8);
                    }
                    MessageBox.Show("日誌已成功匯出至:\n" + sfd.FileName, "匯出成功", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("匯出日誌失敗: " + ex.Message, "錯誤", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private static readonly object hmiLogLock = new object();
        private static StreamWriter hmiLogWriter = null;
        private static string currentLogFilePath = null;

        // ── 雲端即時連線健康與崩潰狀態追蹤 (供 WebMonitor 識別斷線、崩潰與跳脫) ──
        public static volatile bool isSystemCrashed = false;
        public static volatile string systemCrashReason = "";
        public static volatile string lastCriticalCategory = "";
        public static volatile string lastCriticalLog = "";
        public static volatile string lastCriticalTime = "";

        public static void WriteHmiLog(string category, string message)
        {
            try
            {
                // 雲端告警即時追蹤
                if (category == "SAFETY_TRIP" || category == "EMERGENCY_STOP" || category == "DUTY_ABORT" ||
                    category == "TN_ABORT" || category == "EFFMAP_ABORT" || category == "CRASH" ||
                    category == "WARN_TEMP" || category == "BRAKE_STOP_ERR")
                {
                    lastCriticalCategory = category;
                    lastCriticalLog = message;
                    lastCriticalTime = DateTime.Now.ToString("HH:mm:ss");
                }
                else if (category == "DUTY_CONFIG" || category == "TN_START" || category == "EFFMAP_START")
                {
                    lastCriticalCategory = "";
                    lastCriticalLog = "";
                    lastCriticalTime = "";
                }
                // 隱蔽例外與報錯計數追蹤
                if (category.EndsWith("_ERR") || category == "EXCEPTION" || category == "CRASH" || category.Contains("ERR"))
                {
                    healthHandledErrorsCount++;
                }

                string timeFull = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
                string line = string.Format("[{0}] [{1}] {2}", timeFull, category, message);

                string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                string logDir = Path.Combine(baseDir, "logs");
                if (!Directory.Exists(logDir)) Directory.CreateDirectory(logDir);
                string file2 = Path.Combine(logDir, "hmi_telemetry.log");

                lock (hmiLogLock)
                {
                    memoryLogs.Add(line);
                    // 長時間運行記憶體防護：批次修剪取代逐筆 RemoveAt(0)
                    if (memoryLogs.Count > 5500)
                    {
                        memoryLogs.RemoveRange(0, 500);
                    }

                    if (instance == null || instance.enableSystemEventLog)
                    {
                        try
                        {
                            if (hmiLogWriter == null || currentLogFilePath != file2)
                            {
                                if (hmiLogWriter != null) { try { hmiLogWriter.Dispose(); } catch { } }
                                hmiLogWriter = new StreamWriter(new FileStream(file2, FileMode.Append, FileAccess.Write, FileShare.ReadWrite), Encoding.UTF8) { AutoFlush = true };
                                currentLogFilePath = file2;
                            }
                            hmiLogWriter.WriteLine(line);
                            // 歷史日誌單檔過大自動輪替 (超過 10MB 歸檔並開啟新檔，防止 WinXP 硬碟爆滿)
                            if (hmiLogWriter.BaseStream != null && hmiLogWriter.BaseStream.Length > 10485760L)
                            {
                                try
                                {
                                    hmiLogWriter.Flush();
                                    hmiLogWriter.Dispose();
                                    hmiLogWriter = null;
                                    string rotatedPath = Path.Combine(logDir, string.Format("hmi_telemetry_{0}.log", DateTime.Now.ToString("yyyyMMdd_HHmmss")));
                                    if (File.Exists(file2)) File.Move(file2, rotatedPath);
                                }
                                catch { }
                                finally { currentLogFilePath = null; }
                            }
                        }
                        catch
                        {
                            try { File.AppendAllText(file2, line + Environment.NewLine, Encoding.UTF8); } catch { }
                        }
                    }
                }

                if (instance != null && !instance.IsDisposed && instance.IsHandleCreated)
                {
                    instance.BeginInvoke(new Action(() =>
                    {
                        try
                        {
                            if (instance.lblMiniLogText != null && !instance.lblMiniLogText.IsDisposed)
                            {
                                instance.lblMiniLogText.Text = string.Format("[{0}] [{1}] {2}", DateTime.Now.ToString("HH:mm:ss"), category, message);
                            }
                            // ★ 核心修復：高頻數值 TELEMETRY (每 750ms) 僅寫入檔案與狀態列，嚴禁灌爆 Win32 TextBox
                            // 避免 Windows XP 64KB Edit Control USER 堆疊耗盡或字串重置引發致命崩潰
                            if (category != "TELEMETRY" && instance.txtFullLog != null && !instance.txtFullLog.IsDisposed)
                            {
                                if (instance.txtFullLog.TextLength > 20000)
                                {
                                    instance.txtFullLog.Text = instance.txtFullLog.Text.Substring(10000);
                                }
                                instance.txtFullLog.AppendText(line + "\r\n");
                            }
                        }
                        catch { }
                    }));
                }
            }
            catch { }
        }

        // =========================================================================
        // 全域崩潰黑盒子日誌核心模組 (Crash & Error Diagnostic Blackbox System)
        // =========================================================================
        public static string WriteCrashReport(object exObj, string sourceThreadName, bool isTerminating)
        {
            try
            {
                string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                string logDir = Path.Combine(baseDir, "logs");
                if (!Directory.Exists(logDir)) Directory.CreateDirectory(logDir);

                string timeStamp = DateTime.Now.ToString("yyyyMMdd_HHmmss_fff");
                string crashFileName = string.Format("CRASH_REPORT_{0}.log", timeStamp);
                string crashFilePath = Path.Combine(logDir, crashFileName);
                string lastCrashFilePath = Path.Combine(logDir, "Crash_Last_Exception.log");
                string sysErrorLogPath = Path.Combine(logDir, "system_error.log");

                Exception ex = exObj as Exception;
                isSystemCrashed = true;
                systemCrashReason = (ex != null ? (ex.GetType().Name + ": " + ex.Message) : (exObj != null ? exObj.ToString() : "未知崩潰"));
                lastCriticalCategory = "CRASH";
                lastCriticalLog = string.Format("【🚨 系統崩潰】{0}: {1}", sourceThreadName ?? "未知執行緒", systemCrashReason);
                lastCriticalTime = DateTime.Now.ToString("HH:mm:ss");

                StringBuilder sb = new StringBuilder();
                sb.AppendLine("================================================================================");
                sb.AppendLine("          馬達動力計系統 (Dynamometer HMI) 程式崩潰與嚴重異常診斷報告          ");
                sb.AppendLine("================================================================================");
                sb.AppendLine(string.Format("發生時間: {0}", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff")));
                sb.AppendLine(string.Format("異常來源: {0}", sourceThreadName ?? "未知執行緒"));
                sb.AppendLine(string.Format("處理程序狀態: {0}", isTerminating ? "【🛑 致命崩潰 - CLR 即將強制終止程式】" : "【⚠️ 嚴重例外 - 程式嘗試存活】"));
                sb.AppendLine(string.Format("作業系統: {0} ({1})", Environment.OSVersion.VersionString, Environment.OSVersion.Platform));
                sb.AppendLine(string.Format(".NET CLR 版本: {0}", Environment.Version));
                sb.AppendLine(string.Format("架構環境: {0}", IntPtr.Size == 4 ? "32-bit (x86)" : "64-bit (x64)"));
                sb.AppendLine(string.Format("GC 記憶體佔用: {0:F2} MB", GC.GetTotalMemory(false) / 1048576.0));

                try
                {
                    Thread curT = Thread.CurrentThread;
                    sb.AppendLine(string.Format("當前執行緒: ManagedThreadId={0}, Name='{1}', IsBackground={2}, IsThreadPool={3}",
                        curT.ManagedThreadId, curT.Name, curT.IsBackground, curT.IsThreadPoolThread));
                }
                catch { }

                sb.AppendLine("--------------------------------------------------------------------------------");
                sb.AppendLine("【例外詳細階層 (Exception Details)】");
                if (ex != null)
                {
                    int depth = 1;
                    Exception curr = ex;
                    while (curr != null)
                    {
                        sb.AppendLine(string.Format("[層級 #{0}] 類型: {1}", depth, curr.GetType().FullName));
                        sb.AppendLine(string.Format("訊息: {0}", curr.Message));
                        sb.AppendLine(string.Format("來源 (Source): {0}", curr.Source));
                        if (curr.TargetSite != null)
                        {
                            sb.AppendLine(string.Format("方法 (TargetSite): {0}", curr.TargetSite.ToString()));
                        }
                        sb.AppendLine("呼叫堆疊 (StackTrace):");
                        sb.AppendLine(curr.StackTrace ?? "(無呼叫堆疊)");
                        sb.AppendLine();
                        curr = curr.InnerException;
                        depth++;
                    }
                }
                else
                {
                    sb.AppendLine(string.Format("非託管物件例外 (Non-Exception Object): {0}", exObj != null ? exObj.ToString() : "null"));
                }

                sb.AppendLine("--------------------------------------------------------------------------------");
                sb.AppendLine("【崩潰前最後 60 筆操作/遙測日誌軌跡 (Breadcrumbs)】");
                lock (hmiLogLock)
                {
                    int startIdx = Math.Max(0, memoryLogs.Count - 60);
                    for (int i = startIdx; i < memoryLogs.Count; i++)
                    {
                        sb.AppendLine(memoryLogs[i]);
                    }
                }
                sb.AppendLine("================================================================================");

                string fullReport = sb.ToString();

                // 立即以同步強制寫入硬碟，確保即使 CLR 強制終止也不會遺失數據
                lock (hmiLogLock)
                {
                    try { File.WriteAllText(crashFilePath, fullReport, Encoding.UTF8); } catch { }
                    try { File.WriteAllText(lastCrashFilePath, fullReport, Encoding.UTF8); } catch { }
                    try { File.AppendAllText(sysErrorLogPath, fullReport + Environment.NewLine, Encoding.UTF8); } catch { }
                }

                // 嘗試在行程終止前向雲端發送最後崩潰告警 (Emergency Crash Broadcast)
                try
                {
                    if (instance != null)
                    {
                        string crashJson = instance.GetTelemetryJson();
                        string wifiIp = MainForm.detectedWifiIp;
                        string uploadUrl = instance.cloudUploadUrl;
                        if (!string.IsNullOrEmpty(uploadUrl))
                        {
                            MainForm.UploadTelemetryPayload(uploadUrl, crashJson, wifiIp, 2500);
                        }
                    }
                }
                catch { }

                return crashFilePath;
            }
            catch
            {
                return null;
            }
        }

        public static void ShowCrashDialog(string crashFilePath, object exObj, string sourceThreadName, bool isTerminating)
        {
            try
            {
                string exSummary = (exObj != null) ? exObj.ToString() : "未知錯誤";
                string title = isTerminating ? "🚨 系統致命崩潰報告 (Fatal Crash)" : "⚠️ 系統異常警告 (Runtime Error)";
                string prompt = string.Format(
                    "馬達動力計主程式遇到未預期的例外狀況！\r\n\r\n" +
                    "【異常來源】：{0}\r\n" +
                    "【程式狀態】：{1}\r\n" +
                    "【崩潰日誌已儲存至】：\r\n{2}\r\n\r\n" +
                    "【錯誤摘記】：\r\n{3}\r\n\r\n" +
                    "點擊【是 (Yes)】立即開啟日誌檔案\r\n" +
                    "點擊【否 (No)】複製錯誤訊息至剪貼簿",
                    sourceThreadName,
                    isTerminating ? "程式即將關閉" : "系統嘗試繼續運作",
                    crashFilePath ?? "無法寫入硬碟",
                    exSummary.Length > 300 ? exSummary.Substring(0, 300) + "..." : exSummary
                );

                DialogResult dr = MessageBox.Show(prompt, title, MessageBoxButtons.YesNoCancel, MessageBoxIcon.Error);
                if (dr == DialogResult.Yes)
                {
                    if (File.Exists(crashFilePath))
                    {
                        System.Diagnostics.Process.Start("notepad.exe", crashFilePath);
                    }
                    else
                    {
                        string dir = Path.GetDirectoryName(crashFilePath);
                        if (Directory.Exists(dir)) System.Diagnostics.Process.Start("explorer.exe", dir);
                    }
                }
                else if (dr == DialogResult.No)
                {
                    try { Clipboard.SetText(exSummary); } catch { }
                }
            }
            catch { }
        }

        public static void LogException(string context, Exception ex)
        {
            if (ex == null) return;
            string msg = string.Format("【例外捕捉】[{0}] {1}: {2}", context, ex.GetType().Name, ex.Message);
            WriteHmiLog("EXCEPTION", msg);

            try
            {
                string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                string logDir = Path.Combine(baseDir, "logs");
                if (!Directory.Exists(logDir)) Directory.CreateDirectory(logDir);
                string sysErrorLogPath = Path.Combine(logDir, "system_error.log");

                string errText = string.Format("[{0}] [{1}] {2}\r\nStackTrace:\r\n{3}\r\n----------------------------------------\r\n",
                    DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff"), context, ex.ToString(), ex.StackTrace);
                File.AppendAllText(sysErrorLogPath, errText, Encoding.UTF8);
            }
            catch { }
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

        private static float ParseModbusFloat(byte[] buf, int offset)
        {
            if (buf == null || offset + 4 > buf.Length) return 0f;
            try
            {
                // 橫河 WT333E 確認字節順序: Big-Endian (ABCD)
                // 來源：WT333E_Modbus_Register_Analysis_Report.md V2.5 現場校驗
                //       對齊 WT333E_Tester_GUI.cs ParseSingleFloat(decodeMode=0)
                byte[] b = new byte[] { buf[offset + 3], buf[offset + 2], buf[offset + 1], buf[offset] };
                float f = BitConverter.ToSingle(b, 0);
                // 排除 NaN / Inf / 超出物理量程 (1e6 為保守上限)
                return (!float.IsNaN(f) && !float.IsInfinity(f) && Math.Abs(f) < 1000000.0f) ? f : 0f;
            }
            catch { return 0f; }
        }

        private static float SanitizeFloat(float val, float min, float max)
        {
            if (float.IsNaN(val) || float.IsInfinity(val)) return 0f;
            if (val < min || val > max) return 0f;
            return val;
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
                        if (line.StartsWith("IP=") && txtPowerMeterIp != null) txtPowerMeterIp.Text = line.Substring(3).Trim();
                        else if (line.StartsWith("SCALE_U1=")) double.TryParse(line.Substring(9).Trim(), out scaleU1);
                        else if (line.StartsWith("SCALE_U2=")) double.TryParse(line.Substring(9).Trim(), out scaleU2);
                        else if (line.StartsWith("SCALE_U3=")) double.TryParse(line.Substring(9).Trim(), out scaleU3);
                        else if (line.StartsWith("SCALE_I1=")) double.TryParse(line.Substring(9).Trim(), out scaleI1);
                        else if (line.StartsWith("SCALE_I2=")) double.TryParse(line.Substring(9).Trim(), out scaleI2);
                        else if (line.StartsWith("SCALE_I3=")) double.TryParse(line.Substring(9).Trim(), out scaleI3);
                        else if (line.StartsWith("SCALE_TORQUE=")) double.TryParse(line.Substring(13).Trim(), out scaleTorque);
                    }
                }
            }
            catch { }
        }

        public void SaveCalibrationConfig()
        {
            try
            {
                string cfgPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "dynamometer_config.ini");
                List<string> lines = File.Exists(cfgPath) ? File.ReadAllLines(cfgPath).ToList() : new List<string>();

                SetIniKey(lines, "SCALE_U1", scaleU1.ToString("F3"));
                SetIniKey(lines, "SCALE_U2", scaleU2.ToString("F3"));
                SetIniKey(lines, "SCALE_U3", scaleU3.ToString("F3"));
                SetIniKey(lines, "SCALE_I1", scaleI1.ToString("F3"));
                SetIniKey(lines, "SCALE_I2", scaleI2.ToString("F3"));
                SetIniKey(lines, "SCALE_I3", scaleI3.ToString("F3"));
                SetIniKey(lines, "SCALE_TORQUE", scaleTorque.ToString("F3"));

                File.WriteAllLines(cfgPath, lines.ToArray(), Encoding.UTF8);
                WriteHmiLog("CALIB", string.Format("已儲存量測校正比例: U=({0:F2},{1:F2},{2:F2}), I=({3:F2},{4:F2},{5:F2}), 扭力={6:F2}",
                    scaleU1, scaleU2, scaleU3, scaleI1, scaleI2, scaleI3, scaleTorque));
            }
            catch (Exception ex)
            {
                WriteHmiLog("CALIB_ERR", "儲存校正設定失敗: " + ex.Message);
            }
        }

        private static void SetIniKey(List<string> lines, string key, string value)
        {
            int idx = lines.FindIndex(l => l.Trim().StartsWith(key + "=", StringComparison.OrdinalIgnoreCase));
            if (idx >= 0) lines[idx] = string.Format("{0}={1}", key, value);
            else lines.Add(string.Format("{0}={1}", key, value));
        }

        private string BuildRawCsvHeader()
        {
            StringBuilder sb = new StringBuilder();
            // 依 Modify.txt 規範順序:
            // 時間 轉速 頻率(新增源自於KEB[ru.03]) 轉矩 電壓1 電壓2 電壓3 電流1 電流2 電流3 輸入功率 輸出功率 功因 效率 [其他沒列到的放後面再接上溫度]
            sb.Append("Timestamp,Speed_rpm,Frequency_Hz,Torque_Nm");
            sb.Append(",Voltage_U1_V,Voltage_U2_V,Voltage_U3_V");
            sb.Append(",Current_I1_A,Current_I2_A,Current_I3_A");
            sb.Append(",ElecPower_kW,MechPower_kW,PF,Efficiency_pct");
            sb.Append(",Kt_NmA,Power_P1_kW,Power_P2_kW,Power_P3_kW");
            sb.Append(",Voltage_Sigma_V,Current_Sigma_A,MotorTemp_C");

            for (int i = 0; i < 20; i++)
            {
                if (gl820ChannelMask != null && i < gl820ChannelMask.Length && gl820ChannelMask[i])
                {
                    string cName = (gl820ChannelNames != null && i < gl820ChannelNames.Length && !string.IsNullOrEmpty(gl820ChannelNames[i])) ? gl820ChannelNames[i].Trim() : ("CH" + (i + 1));
                    if (cName.Equals("CH" + (i + 1), StringComparison.OrdinalIgnoreCase))
                    {
                        sb.Append(",GL820_CH" + (i + 1) + "_C");
                    }
                    else
                    {
                        sb.Append(",GL820_CH" + (i + 1) + "_" + cName.Replace(",", "_").Replace(" ", "") + "_C");
                    }
                }
            }

            if (recordKebRuParams)
            {
                sb.Append(",KebA_ru00_State,KebA_ru01_Rpm,KebA_ru26_TorqNm,KebA_ru07_CurrA,KebA_ru09_VoltV,KebA_ru10_DcBusV,KebA_ru20_TempC,KebA_ru43_Fault");
                sb.Append(",KebB_ru00_State,KebB_ru01_Rpm,KebB_ru26_TorqNm,KebB_ru07_CurrA,KebB_ru09_VoltV,KebB_ru10_DcBusV,KebB_ru20_TempC,KebB_ru43_Fault");
            }

            return sb.ToString();
        }

        private string BuildRawCsvRow(DateTime timestamp)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendFormat("\"{0}\",{1:F1},{2:F2},{3:F2}",
                timestamp.ToString("yyyy-MM-dd HH:mm:ss.fff"),
                actSpeed, actFrequency, actTorque);

            sb.AppendFormat(",{0:F2},{1:F2},{2:F2}",
                wtU1, wtU2, wtU3);

            sb.AppendFormat(",{0:F3},{1:F3},{2:F3}",
                wtI1, wtI2, wtI3);

            sb.AppendFormat(",{0:F2},{1:F2},{2:F3},{3:F1}",
                actElecPower, actMechPower, actPf, actEfficiency);

            sb.AppendFormat(",{0:F2},{1:F3},{2:F3},{3:F3},{4:F1},{5:F2},{6:F1}",
                actKt, wtP1, wtP2, wtP3,
                actVoltageSigma, actCurrentSigma, actTemp);

            for (int i = 0; i < 20; i++)
            {
                if (gl820ChannelMask != null && i < gl820ChannelMask.Length && gl820ChannelMask[i])
                {
                    double t = (i < gbdChTemps.Length) ? gbdChTemps[i] : 0.0;
                    sb.AppendFormat(",{0:F1}", t);
                }
            }

            if (recordKebRuParams)
            {
                sb.AppendFormat(",\"{0}\",\"{1}\"", (lastRawKebA ?? "").Replace("\"", "\"\""), (lastRawKebB ?? "").Replace("\"", "\"\""));
            }

            return sb.ToString();
        }

        private void WriteAutoRawTelemetryCsv()
        {
            if (!enableAutoRawCsv) return;
            try
            {
                string logDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs");
                if (!Directory.Exists(logDir)) Directory.CreateDirectory(logDir);
                string autoCsv = Path.Combine(logDir, string.Format("Auto_Raw_Telemetry_{0}.csv", DateTime.Now.ToString("yyyyMMdd")));

                bool exists = File.Exists(autoCsv);
                using (StreamWriter sw = new StreamWriter(autoCsv, true, Encoding.UTF8))
                {
                    if (!exists)
                    {
                        sw.WriteLine(BuildRawCsvHeader());
                    }
                    string row = autoSampleAccumulator.HasSamples
                        ? autoSampleAccumulator.BuildAveragedCsvRow(DateTime.Now, gl820ChannelMask, recordKebRuParams)
                        : BuildRawCsvRow(DateTime.Now);
                    if (!string.IsNullOrEmpty(row))
                    {
                        sw.WriteLine(row);
                    }
                }
            }
            catch { }
        }

        // =========================================================================
        // GL820 原廠標準 12KB (12,288 bytes) 二進位 .GBD 標頭產生器
        // =========================================================================
        private byte[] BuildGbdHeader(int recordCount, DateTime startTime, DateTime stopTime, int sampleSec = 1)
        {
            byte[] headerBuffer = new byte[12288];
            StringBuilder sb = new StringBuilder();
            sb.Append("$Common\r\n");
            sb.Append("  ID        = 29071020\r\n");
            sb.Append("  Volume    = 1, 1\r\n");
            sb.Append("  HeaderSiz = 12288\r\n");
            sb.Append("  Vendor    = \"GRAPHTEC Corporation\"\r\n");
            sb.Append("  Model     = \"GL820\"\r\n");
            sb.Append("  Suffix    = \"      \"\r\n");
            sb.Append("  User      = \"Guest\"\r\n");
            sb.Append("  Stat      = \"Guest\"\r\n");
            sb.Append("  UserSel   = 1\r\n");
            sb.Append("  CH        = 20CH\r\n");
            sb.Append("  Option    = None\r\n");
            sb.Append("  Format    = \"Ver1.00\"\r\n");
            sb.Append("  Hardware  = \"Ver1.00\"\r\n");
            sb.Append("  Firmware  = \"Ver1.08  \"\r\n");
            sb.Append("  OS        = \"Ver4.00\", \"Ver3.10\"\r\n");
            sb.Append("  Software  = \"       \"\r\n");
            sb.Append("$$Data\r\n");
            sb.Append("  Format    = BinaryData\r\n");
            sb.Append("  Type      = BigEndian, Short, Setup\r\n");
            sb.Append("  Order     = CH1  , CH2  , CH3  , CH4  , CH5  , CH6  , CH7  , CH8  , CH9  , CH10 , CH11 , CH12 , CH13 , CH14 , CH15 , Alarm1 , Alarm2 , AlarmOut\r\n");
            sb.Append(string.Format("  Sample    = {0}s\r\n", sampleSec <= 0 ? 1 : sampleSec));
            sb.Append("  ExtSamp   = Off\r\n");
            sb.Append("  ExtSampFilt = Off\r\n");
            sb.Append("  TempUnit  = C\r\n");
            sb.Append("  LogicCH   = 4\r\n");
            sb.Append(string.Format("  Counts    =      {0,6}\r\n", recordCount));
            sb.Append("  Trigger   =          0\r\n");
            sb.Append("  Stat      = Off\r\n");
            sb.Append("$$Time\r\n");
            sb.Append(string.Format("  Start     = {0}\r\n", startTime.ToString("yyyy-MM-dd,HH:mm:ss")));
            sb.Append(string.Format("  Stop      = {0}\r\n", stopTime.ToString("yyyy-MM-dd,HH:mm:ss")));
            sb.Append(string.Format("  Trigger   = {0}\r\n", startTime.ToString("yyyy-MM-dd,HH:mm:ss")));
            sb.Append("$Amp\r\n");
            for (int i = 1; i <= 15; i++)
            {
                sb.Append(string.Format("  CH{0,-8} = M    , TEMP,    50V, 5     ,   TC_T,      +0\r\n", i));
            }
            sb.Append("$Annotation\r\n");
            for (int i = 0; i < 15; i++)
            {
                string cName = (gl820ChannelNames != null && i < gl820ChannelNames.Length && !string.IsNullOrEmpty(gl820ChannelNames[i]))
                    ? gl820ChannelNames[i].Trim().Replace("\"", "")
                    : string.Format("CH{0}", i + 1);
                // 嚴格限制英數字元，過濾非 ASCII / 中文全形，保證 GBD 標頭純淨
                cName = System.Text.RegularExpressions.Regex.Replace(cName, @"[^a-zA-Z0-9_\-\.\s]", "").Trim();
                if (string.IsNullOrEmpty(cName)) cName = string.Format("CH{0}", i + 1);
                sb.Append(string.Format("  CH{0,-6} = \"{1}\"\r\n", i + 1, cName));
            }
            sb.Append("$EndHeader\r\n");

            byte[] asciiBytes = Encoding.ASCII.GetBytes(sb.ToString());
            int copyLen = Math.Min(asciiBytes.Length, 12288);
            Array.Copy(asciiBytes, headerBuffer, copyLen);
            return headerBuffer;
        }

        private void WriteManualRawTelemetryRow()
        {
            lock (manualRecordLock)
            {
                try
                {
                    if (manualRecordWriter == null) return;
                    string row = manualSampleAccumulator.HasSamples
                        ? manualSampleAccumulator.BuildAveragedCsvRow(DateTime.Now, gl820ChannelMask, recordKebRuParams)
                        : BuildRawCsvRow(DateTime.Now);
                    if (string.IsNullOrEmpty(row)) return;

                    manualRecordCount++;
                    manualRecordWriter.WriteLine(row);
                    manualRecordWriter.Flush();

                    // 同步寫入原廠二進位 .GBD 數據筆 (每筆 18 個 Big-Endian signed short = 36 bytes)
                    if (manualGbdWriter != null)
                    {
                        for (int ch = 0; ch < 15; ch++)
                        {
                            double tVal = (gbdChTemps != null && ch < gbdChTemps.Length) ? gbdChTemps[ch] : 0.0;
                            short rawVal;
                            if (tVal <= -999.0 || double.IsNaN(tVal) || double.IsInfinity(tVal) || (gl820ChannelMask != null && ch < gl820ChannelMask.Length && !gl820ChannelMask[ch]))
                            {
                                rawVal = 32765; // 0x7FFD: 斷線或未啟用
                            }
                            else
                            {
                                rawVal = (short)Math.Round(tVal * 10.0);
                            }
                            manualGbdWriter.Write((byte)((rawVal >> 8) & 0xFF));
                            manualGbdWriter.Write((byte)(rawVal & 0xFF));
                        }
                        // 3 個 Alarm 欄位 (Alarm1, Alarm2, AlarmOut)
                        for (int a = 0; a < 3; a++)
                        {
                            manualGbdWriter.Write((byte)0);
                            manualGbdWriter.Write((byte)0);
                        }
                        manualGbdWriter.Flush();

                        // 每 5 筆資料 (或於首筆) 同步回填更新 12KB 標頭內的 Counts 與 Stop 時間，防止非預期斷電/當機/拔隨身碟時遺留 0x00 空白標頭
                        if (manualRecordCount == 1 || manualRecordCount % 5 == 0)
                        {
                            try
                            {
                                if (manualGbdStream != null && manualGbdStream.CanSeek)
                                {
                                    long curPos = manualGbdStream.Position;
                                    int sampSec = Math.Max(1, rawDataIntervalMs / 1000);
                                    byte[] liveHeader = BuildGbdHeader(manualRecordCount, manualGbdStartTime, DateTime.Now, sampSec);
                                    manualGbdStream.Seek(0, SeekOrigin.Begin);
                                    manualGbdStream.Write(liveHeader, 0, 12288);
                                    manualGbdStream.Seek(curPos, SeekOrigin.Begin);
                                    manualGbdStream.Flush();
                                }
                            }
                            catch { }
                        }
                    }

                    if (btnRecordRaw != null && !btnRecordRaw.IsDisposed)
                    {
                        btnRecordRaw.BeginInvoke(new Action(() => {
                            int elSec = manualRecordStartTime != DateTime.MinValue ? (int)(DateTime.Now - manualRecordStartTime).TotalSeconds : 0;
                            btnRecordRaw.Text = string.Format(" 停止錄製 ({0}s/{1}筆)", elSec, manualRecordCount);
                        }));
                    }
                    if (btnRecordRawTop != null && !btnRecordRawTop.IsDisposed)
                    {
                        btnRecordRawTop.BeginInvoke(new Action(() => {
                            int elSec = manualRecordStartTime != DateTime.MinValue ? (int)(DateTime.Now - manualRecordStartTime).TotalSeconds : 0;
                            btnRecordRawTop.Text = string.Format(" 停止錄製 ({0}s/{1}筆)", elSec, manualRecordCount);
                        }));
                    }
                }
                catch { }
            }
        }

        public void StartManualRecordingWithParams(string motorName, string folderPath, string fileName, bool[] chMask, bool saveKebRu, int intervalMs = 1000)
        {
            lock (manualRecordLock)
            {
                try
                {
                    if (isManualRecording) return;
                    motorModelName = string.IsNullOrEmpty(motorName) ? "SVM100S" : motorName.Trim();
                    rawDataSaveDirectory = folderPath;
                    if (chMask != null && chMask.Length == 20) gl820ChannelMask = chMask;
                    recordKebRuParams = saveKebRu;
                    if (intervalMs > 0) rawDataIntervalMs = intervalMs;
                    SaveLayoutConfig();

                    if (!Directory.Exists(folderPath)) Directory.CreateDirectory(folderPath);

                    manualRecordFilePath = Path.Combine(folderPath, fileName);
                    manualRecordWriter = new StreamWriter(manualRecordFilePath, false, Encoding.UTF8);
                    manualRecordWriter.WriteLine(BuildRawCsvHeader());
                    manualRecordWriter.Flush();

                    manualRecordStartTime = DateTime.Now;
                    manualGbdStartTime = manualRecordStartTime;

                    // 同步建立同名二進位 .GBD 檔案 (立即寫入標準 12KB 原廠 ASCII 標頭，嚴禁全 0x00 空白標頭)
                    try
                    {
                        manualRecordGbdPath = Path.Combine(folderPath, Path.GetFileNameWithoutExtension(fileName) + ".gbd");
                        manualGbdStream = new FileStream(manualRecordGbdPath, FileMode.Create, FileAccess.ReadWrite, FileShare.ReadWrite);
                        int sampSec = Math.Max(1, rawDataIntervalMs / 1000);
                        byte[] initialHeader = BuildGbdHeader(0, manualGbdStartTime, manualGbdStartTime, sampSec);
                        manualGbdStream.Write(initialHeader, 0, 12288);
                        manualGbdStream.Flush();
                        manualGbdWriter = new BinaryWriter(manualGbdStream);
                    }
                    catch (Exception exGbd)
                    {
                        WriteHmiLog("GBD_ERR", "建立 GBD 檔案串流失敗: " + exGbd.Message);
                        manualGbdWriter = null;
                        manualGbdStream = null;
                    }

                    manualRecordCount = 0;
                    manualSampleAccumulator.Clear();
                    lastManualRecordWriteTime = DateTime.Now;
                    isManualRecording = true;

                    if (!isWorkerRunning)
                    {
                        StartBackgroundWorker();
                    }

                    if (btnRecordRaw != null)
                    {
                        btnRecordRaw.Text = string.Format(" 停止錄製 (0s: {0})", motorModelName);
                        btnRecordRaw.BackColor = Color.FromArgb(239, 68, 68);
                    }
                    if (btnRecordRawTop != null)
                    {
                        btnRecordRawTop.Image = CreateFloppyIconImage(36, 28, Color.White, true);
                        btnRecordRawTop.BackColor = Color.FromArgb(239, 68, 68);
                    }
                    string modeStr = isAutoTriggeredRecording ? string.Format("自動測試 [{0}]", autoRecordTestTag) : "手動錄製";
                    WriteHmiLog("RECORDER", string.Format(" [開始{0} RAW DATA] 馬達: {1} | 週期: {2}ms | CSV: {3} | GBD: {4} (門檻: 錄製未滿 1 分鐘將於停止時自動刪除)",
                        modeStr, motorModelName, rawDataIntervalMs, Path.GetFileName(manualRecordFilePath), Path.GetFileName(manualRecordGbdPath)));
                }
                catch (Exception ex)
                {
                    MessageBox.Show("啟動 RAW DATA 錄製失敗: " + ex.Message, "錯誤", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        public void StopManualRecording(bool showPrompt = true)
        {
            lock (manualRecordLock)
            {
                if (!isManualRecording) return;
                isManualRecording = false;

                DateTime stopTime = DateTime.Now;
                double durationSec = (manualRecordStartTime != DateTime.MinValue)
                    ? (stopTime - manualRecordStartTime).TotalSeconds
                    : (manualGbdStartTime != DateTime.MinValue ? (stopTime - manualGbdStartTime).TotalSeconds : 0.0);
                bool wasAuto = isAutoTriggeredRecording;
                string tag = autoRecordTestTag;
                string targetCsvPath = manualRecordFilePath;
                string targetGbdPath = manualRecordGbdPath;
                int recordedCount = manualRecordCount;

                // 1. 安全關閉 CSV 寫入器
                try
                {
                    if (manualRecordWriter != null)
                    {
                        manualRecordWriter.Flush();
                        manualRecordWriter.Close();
                        manualRecordWriter.Dispose();
                        manualRecordWriter = null;
                    }
                }
                catch { }

                // 2. 判斷錄製時長是否滿足「最低 1 分鐘 (>= 60 秒)」門檻
                // 若時間太短 (< 60 秒)，直接將生成的紀錄檔刪除，避免磁碟累積零碎檔案
                bool isTooShort = durationSec < 60.0;

                if (isTooShort)
                {
                    // 關閉二進位 GBD 檔案串流（直接關閉準備刪除）
                    if (manualGbdWriter != null)
                    {
                        try { manualGbdWriter.Close(); } catch { }
                        manualGbdWriter = null;
                    }
                    if (manualGbdStream != null)
                    {
                        try { manualGbdStream.Close(); manualGbdStream.Dispose(); } catch { }
                        manualGbdStream = null;
                    }

                    // 刪除 CSV 檔案
                    string deletedCsvName = "";
                    try
                    {
                        if (!string.IsNullOrEmpty(targetCsvPath) && File.Exists(targetCsvPath))
                        {
                            deletedCsvName = Path.GetFileName(targetCsvPath);
                            File.Delete(targetCsvPath);
                        }
                    }
                    catch (Exception exDelCsv)
                    {
                        WriteHmiLog("REC_PURGE_ERR", "刪除未滿1分鐘 CSV 紀錄檔失敗: " + exDelCsv.Message);
                    }

                    // 刪除 GBD 檔案
                    string deletedGbdName = "";
                    try
                    {
                        if (!string.IsNullOrEmpty(targetGbdPath) && File.Exists(targetGbdPath))
                        {
                            deletedGbdName = Path.GetFileName(targetGbdPath);
                            File.Delete(targetGbdPath);
                        }
                    }
                    catch (Exception exDelGbd)
                    {
                        WriteHmiLog("REC_PURGE_ERR", "刪除未滿1分鐘 GBD 紀錄檔失敗: " + exDelGbd.Message);
                    }

                    // 復歸 UI 按鈕狀態
                    if (btnRecordRaw != null)
                    {
                        btnRecordRaw.Text = " 錄製 RAW DATA";
                        btnRecordRaw.BackColor = Color.FromArgb(220, 38, 38);
                    }
                    if (btnRecordRawTop != null)
                    {
                        btnRecordRawTop.Image = CreateFloppyIconImage(36, 28, Color.White, false);
                        btnRecordRawTop.BackColor = Color.FromArgb(220, 38, 38);
                    }

                    // 紀錄日誌
                    if (wasAuto)
                    {
                        WriteHmiLog("AUTO_RAW", string.Format("【自動測試紀錄清理】測試標籤 [{0}] 錄製時間僅 {1:F1} 秒 (未滿 1 分鐘門檻，共 {2} 筆)，已直接刪除紀錄檔 [{3}]，避免產生零碎檔案。",
                            tag, durationSec, recordedCount, deletedCsvName));
                    }
                    else
                    {
                        WriteHmiLog("RECORDER", string.Format("【手動錄製自動清除】錄製歷時僅 {0:F1} 秒 (未滿 1 分鐘門檻，共 {1} 筆)，已直接刪除紀錄檔 [{2}]，保持目錄純淨。",
                            durationSec, recordedCount, deletedCsvName));
                    }

                    // 提示使用者 (僅在手動點擊停止且 showPrompt=true 時跳提示，自動測試中止不彈窗干擾操作)
                    if (showPrompt)
                    {
                        string msg = string.Format("本次錄製時間過短 (實際僅 {0:F1} 秒，未達最低有效門檻 1 分鐘 / 60 秒)。\n\n為避免產生過多零碎無效檔案，系統已直接刪除本次產生的紀錄檔：\n• CSV: {1}\n• GBD: {2}",
                            durationSec, deletedCsvName, deletedGbdName);
                        MessageBox.Show(msg, "錄製時間未滿 1 分鐘 (已自動刪除)", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    }

                    isAutoTriggeredRecording = false;
                    autoRecordTestTag = "";
                    manualRecordFilePath = "";
                    manualRecordGbdPath = "";
                    manualRecordStartTime = DateTime.MinValue;
                    manualGbdStartTime = DateTime.MinValue;
                    manualRecordCount = 0;
                    return;
                }

                // ── 時長滿 1 分鐘 (>= 60 秒)：正常封裝與保留 ──
                if (manualGbdStream != null && manualGbdWriter != null)
                {
                    try
                    {
                        int sampSec = Math.Max(1, rawDataIntervalMs / 1000);
                        byte[] gbdHeader = BuildGbdHeader(manualRecordCount, manualGbdStartTime, stopTime, sampSec);
                        manualGbdStream.Seek(0, SeekOrigin.Begin);
                        manualGbdStream.Write(gbdHeader, 0, 12288);
                        manualGbdStream.Flush();
                        manualGbdWriter.Close();
                        manualGbdStream.Close();
                    }
                    catch (Exception exGbd)
                    {
                        WriteHmiLog("GBD_ERR", "封裝回填 GBD 標頭失敗: " + exGbd.Message);
                    }
                    manualGbdWriter = null;
                    manualGbdStream = null;
                }

                if (btnRecordRaw != null)
                {
                    btnRecordRaw.Text = " 錄製 RAW DATA";
                    btnRecordRaw.BackColor = Color.FromArgb(220, 38, 38);
                }
                if (btnRecordRawTop != null)
                {
                    btnRecordRawTop.Image = CreateFloppyIconImage(36, 28, Color.White, false);
                    btnRecordRawTop.BackColor = Color.FromArgb(220, 38, 38);
                }

                string triggerType = wasAuto ? string.Format("自動測試 [{0}]", tag) : "手動錄製";
                WriteHmiLog("RECORDER", string.Format(" [RAW DATA 錄製完成] 模式: {0} | 馬達: {1} | 歷時: {2:F1} 秒 ({3:F1} 分鐘) | 共錄製 {4} 筆 RAW DATA 數據至 CSV 與 GBD！",
                    triggerType, motorModelName, durationSec, durationSec / 60.0, manualRecordCount));

                // 本地日誌生命週期維護：錄製結束後自動觸發本地日誌修剪 (保留最新 30 筆)
                PurgeLocalLogs(false);

                if (showPrompt)
                {
                    string msg = string.Format("[成功] 手動 RAW DATA 錄製完成！\n\n 馬達名稱：{0}\n 錄製時間：{1:F1} 秒 ({2:F1} 分鐘)\n 錄製筆數：{3} 筆\n CSV 路徑：\n{4}\n GBD 原廠檔：\n{5}\n\n是否立即在檔案總管中查看？",
                        motorModelName, durationSec, durationSec / 60.0, manualRecordCount, manualRecordFilePath, manualRecordGbdPath);

                    DialogResult res = MessageBox.Show(msg, "錄製完成", MessageBoxButtons.YesNo, MessageBoxIcon.Information);
                    if (res == DialogResult.Yes)
                    {
                        try
                        {
                            string dir = Path.GetDirectoryName(manualRecordFilePath);
                            if (Directory.Exists(dir))
                            {
                                System.Diagnostics.Process.Start("explorer.exe", string.Format("/select,\"{0}\"", manualRecordFilePath));
                            }
                        }
                        catch { }
                    }
                }

                isAutoTriggeredRecording = false;
                autoRecordTestTag = "";
                manualRecordStartTime = DateTime.MinValue;
                manualGbdStartTime = DateTime.MinValue;
            }
        }

        public void StartAutoRawRecordingWithTag(string testTag)
        {
            lock (manualRecordLock)
            {
                if (isManualRecording)
                {
                    StopManualRecording(showPrompt: false);
                }

                isAutoTriggeredRecording = true;
                autoRecordTestTag = string.IsNullOrEmpty(testTag) ? "TEST" : testTag.Trim().Replace(" ", "_");

                string mName = !string.IsNullOrEmpty(motorModelName) ? motorModelName : "SVM100S";
                string fDir = !string.IsNullOrEmpty(rawDataSaveDirectory) && Directory.Exists(rawDataSaveDirectory)
                    ? rawDataSaveDirectory
                    : Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs");

                string tag = autoRecordTestTag;
                string fName = string.Format("{0}_{1}_{2}.csv", mName, DateTime.Now.ToString("yyyyMMdd_HHmmss"), tag);

                StartManualRecordingWithParams(mName, fDir, fName, gl820ChannelMask, recordKebRuParams, rawDataIntervalMs);
                WriteHmiLog("AUTO_RAW", string.Format("【自動開啟 RAW DATA 記錄】測試標籤: [{0}] | 檔名: {1}", tag, fName));
            }
        }

        private void ToggleManualRawRecording()
        {
            if (!isManualRecording)
            {
                isAutoTriggeredRecording = false;
                autoRecordTestTag = "";
                string mName = !string.IsNullOrEmpty(motorModelName) ? motorModelName : "SVM100S";
                string fDir = !string.IsNullOrEmpty(rawDataSaveDirectory) && Directory.Exists(rawDataSaveDirectory)
                    ? rawDataSaveDirectory
                    : Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs");
                string fName = string.Format("{0}_{1}.csv", mName, DateTime.Now.ToString("yyyyMMdd_HHmmss"));
                StartManualRecordingWithParams(mName, fDir, fName, gl820ChannelMask, recordKebRuParams, rawDataIntervalMs);
            }
            else
            {
                StopManualRecording(showPrompt: true);
            }
        }

        private void SaveRawSnapshot()
        {
            try
            {
                string logDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs");
                if (!Directory.Exists(logDir)) Directory.CreateDirectory(logDir);
                string snapFile = Path.Combine(logDir, string.Format("RawData_Snapshots_{0}.csv", DateTime.Now.ToString("yyyyMMdd")));

                bool fileExists = File.Exists(snapFile);
                using (StreamWriter sw = new StreamWriter(snapFile, true, Encoding.UTF8))
                {
                    if (!fileExists)
                    {
                        sw.WriteLine(BuildRawCsvHeader());
                    }
                    sw.WriteLine(BuildRawCsvRow(DateTime.Now));
                }
                WriteHmiLog("SNAPSHOT", " [手動快照成功] 已將當前瞬間 RAW DATA 寫入快照檔: " + Path.GetFileName(snapFile));
                MessageBox.Show(" 當前瞬間 RAW DATA 快照已儲存至：\n" + snapFile, "快照成功", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show("儲存快照失敗: " + ex.Message, "錯誤", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void OpenLogsFolder()
        {
            try
            {
                string logDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs");
                if (!Directory.Exists(logDir)) Directory.CreateDirectory(logDir);
                System.Diagnostics.Process.Start("explorer.exe", logDir);
            }
            catch (Exception ex)
            {
                MessageBox.Show("開啟日誌目錄失敗: " + ex.Message, "錯誤");
            }
        }
    }
}
