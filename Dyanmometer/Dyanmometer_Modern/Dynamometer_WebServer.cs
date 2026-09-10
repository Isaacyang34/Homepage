using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;
using System.Net.Sockets;
using Org.BouncyCastle.Crypto.Tls;
using Org.BouncyCastle.Security;

namespace DynamometerHMI
{
    // ============================================================
    // Dynamometer_WebServer.cs
    // 遠端即時監看 Web Server (HttpListener + Server-Sent Events)
    // 純唯讀監看，對核心測試控制邏輯零干擾。
    // ============================================================
    public partial class MainForm
    {
        // ── 欄位宣告 ──────────────────────────────────────────────
        private DynWebServer _webServer = null;
        private int webServerPort = 8080;
        private Button btnWebServerToggle = null;
        private Label lblWebServerUrl = null;

        // ── 雲端上傳發送器 (Firebase / Cloud Realtime Database) ───────────
        public string cloudUploadUrl = "https://dynamometer-live-default-rtdb.asia-southeast1.firebasedatabase.app/live.json";
        public string cloudLogUploadUrl = "https://dynamometer-live-default-rtdb.asia-southeast1.firebasedatabase.app/logs/latest.json";
        public string cloudLogHistoryBaseUrl = "https://dynamometer-live-default-rtdb.asia-southeast1.firebasedatabase.app/logs/history";
        public int cloudLogMaxHistoryCount = 30; // 預設保留最新 30 筆
        public int cloudLogMaxDays = 7;          // 預設保留 7 天
        public int localLogMaxHistoryCount = 30; // 預設本地保留最新 30 筆 (CSV 測試日誌、報表與歷史記錄)
        public bool isCloudUploadEnabled = true;
        private Thread cloudUploadThread = null;
        private bool isCloudUploadRunning = false;

        // ── 軟體線上熱更新設定 (Online Auto-Update & In-Place Hot Swap) ──────
        public const string APP_VERSION = "2.6.4";
        public string cloudUpdateManifestUrl = "https://dynamometer-live-default-rtdb.asia-southeast1.firebasedatabase.app/update/version.json";
        public Button btnOnlineUpdate = null;
        private bool? lastCloudUploadSuccess = null;
        private DateTime lastCloudLogTime = DateTime.MinValue;
        private long cloudUploadCount = 0;
        private int lastCloudRttMs = 0;
        private string lastCloudTimeStr = "--:--:--";
        private string lastCloudErrorMsg = "";
        private Button btnCloudUploadToggle = null;
        private Label lblCloudSyncStatus = null;
        private Label lblWifiStatus = null;

        // ── Wi-Fi 網卡自動偵測與綁定快取 (B對策) ───────────────────────────
        public static string detectedWifiIp = "";
        public static string detectedWifiNicName = "";
        public static string detectedWifiGateway = "";

        // 各測試狀態機填入的即時狀態文字 (主執行緒寫入，WebServer 執行緒唯讀)
        public string webRemoteStatusText = "待機中";
        public string webRemotePhaseText  = "";
        public string webRemoteMode       = "IDLE";  // IDLE / TN / DUTY-S1 / DUTY-S2 / DUTY-S6 / EFF

        // ── UI 狀態安全更新 (支援跨執行緒與主執行緒) ──────────────────
        private void UpdateWebServerUI(string url, bool isRunning, string errorMsg = null)
        {
            if (this.InvokeRequired)
            {
                this.BeginInvoke(new Action(() => UpdateWebServerUI(url, isRunning, errorMsg)));
                return;
            }

            if (lblWebServerUrl != null)
            {
                if (!string.IsNullOrEmpty(errorMsg))
                {
                    lblWebServerUrl.Text = "🌐 啟動異常 :" + webServerPort;
                    lblWebServerUrl.ForeColor = System.Drawing.Color.FromArgb(248, 113, 113);
                }
                else if (isRunning)
                {
                    lblWebServerUrl.Text = "🌐 " + url;
                    lblWebServerUrl.ForeColor = System.Drawing.Color.FromArgb(52, 211, 153);
                    lblWebServerUrl.BackColor = System.Drawing.Color.FromArgb(30, 41, 59);
                }
                else
                {
                    lblWebServerUrl.Text = "🌐 " + url + " (已停止)";
                    lblWebServerUrl.ForeColor = System.Drawing.Color.FromArgb(148, 163, 184);
                    lblWebServerUrl.BackColor = System.Drawing.Color.FromArgb(30, 41, 59);
                }
            }

            if (btnWebServerToggle != null)
            {
                if (isRunning)
                {
                    btnWebServerToggle.Text = "⏹ 停止遠端";
                    btnWebServerToggle.BackColor = System.Drawing.Color.FromArgb(22, 163, 74);
                }
                else
                {
                    btnWebServerToggle.Text = "🌐 啟動遠端";
                    btnWebServerToggle.BackColor = System.Drawing.Color.FromArgb(37, 99, 235);
                }
            }
        }

        // ── 啟動 ──────────────────────────────────────────────────
        private void StartWebServer()
        {
            if (_webServer != null && _webServer.IsRunning) return;
            try
            {
                _webServer = new DynWebServer(webServerPort, this);
                _webServer.Start();
                webServerPort = _webServer.Port;
                string localIp = GetLocalIpAddress();
                string url = "http://" + localIp + ":" + webServerPort;
                UpdateWebServerUI(url, true);
                WriteHmiLog("WEB_SERVER", "【遠端監看已啟動】網址: " + url);
            }
            catch (Exception ex)
            {
                string localIp = GetLocalIpAddress();
                string url = "http://" + localIp + ":" + webServerPort;
                UpdateWebServerUI(url, false, ex.Message);
                WriteHmiLog("WEB_SERVER", "【遠端監看啟動失敗】" + ex.Message + " (請確認 TCP 埠 " + webServerPort + ")");
            }

            // 同步啟動雲端上傳背景 Worker
            StartCloudUploader();
        }

        // ── 停止 ──────────────────────────────────────────────────
        private void StopWebServer()
        {
            StopCloudUploader();
            if (_webServer != null) { _webServer.Stop(); _webServer = null; }
            string localIp = GetLocalIpAddress();
            string url = "http://" + localIp + ":" + webServerPort;
            UpdateWebServerUI(url, false);
            WriteHmiLog("WEB_SERVER", "【遠端監看停止】Web Server 已關閉。");
        }

        // ── 雲端上傳背景 Worker 實作 (B對策: 鎖定 Wi-Fi 網卡拋送 Firebase) ──
        public void StartCloudUploader()
        {
            if (isCloudUploadRunning) return;
            isCloudUploadRunning = true;

            // 啟動時立即自動掃描所有網路介面，並完整記錄至 HMI 整合日誌
            ThreadPool.QueueUserWorkItem(_ => {
                DetectWifiNetworkInterface(msg => WriteHmiLog("NET_INIT", msg));
                UpdateCloudSyncUI(false, null);
            });

            cloudUploadThread = new Thread(CloudUploadLoop)
            {
                IsBackground = true,
                Name = "CloudTelemetryUploaderThread"
            };
            cloudUploadThread.Start();
        }

        public void StopCloudUploader()
        {
            isCloudUploadRunning = false;
            if (cloudUploadThread != null)
            {
                try { cloudUploadThread.Abort(); } catch { }
                cloudUploadThread = null;
            }
        }

        private void CloudUploadLoop()
        {
            WriteHmiLog("CLOUD", "【Firebase 雲端推播啟動 (XP TLS 1.2 穿透引擎)】目標端點: " + cloudUploadUrl);

            int loopCount = 0;
            while (isCloudUploadRunning)
            {
                loopCount++;
                // 每 15 輪 (約 12 秒) 若無 Wi-Fi IP，主動重新探測
                if (string.IsNullOrEmpty(detectedWifiIp) && (loopCount % 15 == 1))
                {
                    DetectWifiNetworkInterface();
                }

                // 每 2250 輪 (約 30 分鐘) 自動執行一次雲端歷史日誌生命週期清理
                if (loopCount % 2250 == 100)
                {
                    PurgeCloudLogsAsync(false);
                }

                // 開機第 20 輪 (約 15 秒) 或每 4500 輪 (約 1 小時) 背景靜默檢查線上更新
                if (loopCount == 20 || (loopCount % 4500 == 0))
                {
                    CheckAndPerformOnlineUpdateAsync(false);
                }

                if (isCloudUploadEnabled && !string.IsNullOrEmpty(cloudUploadUrl))
                {
                    try
                    {
                        string json = GetTelemetryJson();
                        DateTime t0 = DateTime.Now;

                        // ★ 核心穿透調用：自動判斷 HTTPS (BouncyCastle TLS 1.2) 或 HTTP 中繼
                        string respStatus = UploadTelemetryPayload(cloudUploadUrl, json, detectedWifiIp, 4000);
                        lastCloudRttMs = (int)(DateTime.Now - t0).TotalMilliseconds;
                        cloudUploadCount++;
                        lastCloudTimeStr = DateTime.Now.ToString("HH:mm:ss");
                        lastCloudErrorMsg = "";

                        UpdateCloudSyncUI(true, null);
                        if (lastCloudUploadSuccess != true)
                        {
                            lastCloudUploadSuccess = true;
                            WriteHmiLog("CLOUD", "[OK] Firebase 雲端推播連線成功 (" + respStatus + " | RTT=" + lastCloudRttMs + "ms | 網卡: " + (detectedWifiIp ?? "預設") + ") -> 資料已即時推播至雲端");
                        }
                    }
                    catch (Exception ex)
                    {
                        lastCloudErrorMsg = ex.Message;
                        UpdateCloudSyncUI(false, ex.Message);
                        bool isNewErr = (lastCloudUploadSuccess != false);
                        bool throttleElapsed = (DateTime.Now - lastCloudLogTime).TotalSeconds >= 15;
                        if (isNewErr || throttleElapsed)
                        {
                            lastCloudUploadSuccess = false;
                            lastCloudLogTime = DateTime.Now;
                            WriteHmiLog("CLOUD", "[ERR] 雲端推播失敗: " + ex.Message + " (綁定網卡: " + (detectedWifiIp ?? "未綁定") + ")");
                        }
                    }
                }
                Thread.Sleep(800); // 800ms 雲端同步間隔
            }
        }

        private void UpdateCloudSyncUI(bool isSuccess, string errorMsg)
        {
            if (this.InvokeRequired)
            {
                try { this.BeginInvoke(new Action(() => UpdateCloudSyncUI(isSuccess, errorMsg))); } catch { }
                return;
            }

            if (lblWifiStatus != null)
            {
                if (!string.IsNullOrEmpty(detectedWifiIp))
                {
                    lblWifiStatus.Text = "📶 " + detectedWifiIp;
                    lblWifiStatus.ForeColor = System.Drawing.Color.FromArgb(56, 189, 248);
                    lblWifiStatus.BackColor = System.Drawing.Color.FromArgb(15, 23, 42);
                }
                else
                {
                    lblWifiStatus.Text = "📶 尋找 Wi-Fi...";
                    lblWifiStatus.ForeColor = System.Drawing.Color.FromArgb(251, 146, 60);
                    lblWifiStatus.BackColor = System.Drawing.Color.FromArgb(30, 41, 59);
                }
            }

            if (lblCloudSyncStatus != null)
            {
                if (!isCloudUploadEnabled)
                {
                    lblCloudSyncStatus.Text = "☁️ Firebase: 暫停";
                    lblCloudSyncStatus.ForeColor = System.Drawing.Color.FromArgb(148, 163, 184);
                    lblCloudSyncStatus.BackColor = System.Drawing.Color.FromArgb(30, 41, 59);
                }
                else if (isSuccess)
                {
                    lblCloudSyncStatus.Text = string.Format("🟢 Firebase #{0} ({1}ms)", cloudUploadCount, lastCloudRttMs);
                    lblCloudSyncStatus.ForeColor = System.Drawing.Color.FromArgb(52, 211, 153);
                    lblCloudSyncStatus.BackColor = System.Drawing.Color.FromArgb(15, 23, 42);
                }
                else
                {
                    lastCloudErrorMsg = errorMsg ?? "連線失敗";
                    lblCloudSyncStatus.Text = "🔴 雲端離線 (點此診斷)";
                    lblCloudSyncStatus.ForeColor = System.Drawing.Color.FromArgb(248, 113, 113);
                    lblCloudSyncStatus.BackColor = System.Drawing.Color.FromArgb(69, 10, 10);
                }
            }
        }

        // ── JSON 字串跳脫防護 (確保 .NET 4.0 手工組裝 JSON 格式 100% 合法) ──────
        private static string EscapeJsonString(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                switch (c)
                {
                    case '\\': sb.Append("\\\\"); break;
                    case '\"': sb.Append("\\\""); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    case '\b': sb.Append("\\b"); break;
                    case '\f': sb.Append("\\f"); break;
                    default:
                        if (c < 32) sb.AppendFormat("\\u{0:x4}", (int)c);
                        else sb.Append(c);
                        break;
                }
            }
            return sb.ToString();
        }

        // ── 雲端日誌打包上傳器 (本地與雲端共存：免隨身碟，AI 助理可直接線上下載分析) ──
        public void UploadLatestLogToCloudAsync(bool isManualClick = false)
        {
            ThreadPool.QueueUserWorkItem(state =>
            {
                try
                {
                    string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                    string logDir = Path.Combine(baseDir, "logs");
                    if (!Directory.Exists(logDir))
                    {
                        WriteHmiLog("CLOUD_LOG", "【日誌上傳警告】logs 目錄不存在，尚無日誌檔案可上傳。");
                        if (isManualClick && this.IsHandleCreated && !this.IsDisposed)
                        {
                            this.BeginInvoke(new Action(() => {
                                MessageBox.Show("目前 logs 目錄無任何日誌可上傳。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                            }));
                        }
                        return;
                    }

                    var dirInfo = new DirectoryInfo(logDir);
                    var csvFiles = dirInfo.GetFiles("*.csv");
                    Array.Sort(csvFiles, (a, b) => b.LastWriteTime.CompareTo(a.LastWriteTime));

                    var logFiles = dirInfo.GetFiles("*.log");
                    Array.Sort(logFiles, (a, b) => b.LastWriteTime.CompareTo(a.LastWriteTime));

                    string csvName = "";
                    string csvContent = "";
                    int csvRows = 0;

                    if (csvFiles.Length > 0)
                    {
                        csvName = csvFiles[0].Name;
                        try
                        {
                            using (var fs = new FileStream(csvFiles[0].FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                            using (var sr = new StreamReader(fs, Encoding.UTF8))
                            {
                                List<string> lines = new List<string>();
                                string headerLine = null;
                                string line;
                                while ((line = sr.ReadLine()) != null)
                                {
                                    if (headerLine == null) headerLine = line;
                                    lines.Add(line);
                                }
                                csvRows = lines.Count;
                                if (lines.Count > 3000)
                                {
                                    var sub = new List<string>();
                                    if (!string.IsNullOrEmpty(headerLine)) sub.Add(headerLine);
                                    int startIdx = lines.Count - 3000;
                                    for (int i = startIdx; i < lines.Count; i++)
                                    {
                                        sub.Add(lines[i]);
                                    }
                                    csvContent = string.Join("\n", sub.ToArray());
                                }
                                else
                                {
                                    csvContent = string.Join("\n", lines.ToArray());
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            csvContent = "讀取 CSV 失敗: " + ex.Message;
                        }
                    }

                    string logName = "";
                    string logContent = "";
                    if (logFiles.Length > 0)
                    {
                        logName = logFiles[0].Name;
                        try
                        {
                            using (var fs = new FileStream(logFiles[0].FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                            using (var sr = new StreamReader(fs, Encoding.UTF8))
                            {
                                List<string> lines = new List<string>();
                                string line;
                                while ((line = sr.ReadLine()) != null) lines.Add(line);
                                if (lines.Count > 2000)
                                {
                                    var sub = new List<string>();
                                    int startIdx = lines.Count - 2000;
                                    for (int i = startIdx; i < lines.Count; i++)
                                    {
                                        sub.Add(lines[i]);
                                    }
                                    logContent = string.Join("\n", sub.ToArray());
                                }
                                else
                                {
                                    logContent = string.Join("\n", lines.ToArray());
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            logContent = "讀取 LOG 失敗: " + ex.Message;
                        }

                        // ★ 核心黑盒子穿透保障：主動搜尋 logs/ 目錄下之最新 CRASH_REPORT 或 Crash_Last_Exception
                        // 確保即使程式閃退重開後 hmi_telemetry.log 被刷新，崩潰診斷報告依然 100% 同步推送至雲端
                        try
                        {
                            FileInfo latestCrash = null;
                            foreach (var lf in logFiles)
                            {
                                if (lf.Name.StartsWith("CRASH_REPORT_", StringComparison.OrdinalIgnoreCase) ||
                                    lf.Name.Equals("Crash_Last_Exception.log", StringComparison.OrdinalIgnoreCase) ||
                                    lf.Name.Equals("system_error.log", StringComparison.OrdinalIgnoreCase))
                                {
                                    latestCrash = lf;
                                    break;
                                }
                            }
                            if (latestCrash != null)
                            {
                                string crashText = File.ReadAllText(latestCrash.FullName, Encoding.UTF8);
                                if (!string.IsNullOrEmpty(crashText))
                                {
                                    logContent = string.Format("=== 🚨 現場主機黑盒子崩潰診斷報告 ({0}) ===\n{1}\n\n=== 即時運行日誌 ({2}) ===\n{3}",
                                        latestCrash.Name, crashText, logName, logContent);
                                    if (string.IsNullOrEmpty(lastCriticalLog))
                                    {
                                        lastCriticalLog = string.Format("【檢出崩潰報告 {0}】", latestCrash.Name);
                                    }
                                }
                            }
                        }
                        catch { }
                    }

                    if (string.IsNullOrEmpty(csvContent) && string.IsNullOrEmpty(logContent))
                    {
                        WriteHmiLog("CLOUD_LOG", "【日誌上傳警告】logs 目錄下無有效檔案內容。");
                        return;
                    }

                    string url = cloudLogUploadUrl;
                    string timeStr = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

                    StringBuilder json = new StringBuilder();
                    json.Append("{");
                    json.AppendFormat("\"upload_time\":\"{0}\",", EscapeJsonString(timeStr));
                    json.AppendFormat("\"test_mode\":\"{0}\",", EscapeJsonString(webRemoteMode));
                    json.AppendFormat("\"status_text\":\"{0}\",", EscapeJsonString(webRemoteStatusText));
                    json.AppendFormat("\"csv_filename\":\"{0}\",", EscapeJsonString(csvName));
                    json.AppendFormat("\"csv_rows_count\":{0},", csvRows);
                    json.AppendFormat("\"csv_content\":\"{0}\",", EscapeJsonString(csvContent));
                    json.AppendFormat("\"log_filename\":\"{0}\",", EscapeJsonString(logName));
                    json.AppendFormat("\"log_content\":\"{0}\",", EscapeJsonString(logContent));
                    json.AppendFormat("\"last_error\":\"{0}\",", EscapeJsonString(lastCriticalLog));
                    json.Append("\"uploader\":\"HMI_Pro_WinXP\"");
                    json.Append("}");

                    WriteHmiLog("CLOUD_LOG", string.Format("【日誌雲端上傳中】正在將 {0} ({1} 行) 與 {2} 透過 Wi-Fi 網卡推播至 Firebase...", csvName, csvRows, logName));

                    string resp = UploadTelemetryPayload(url, json.ToString(), detectedWifiIp, 15000);
                    WriteHmiLog("CLOUD_LOG", string.Format("【✅ 日誌雲端上傳成功】端點: /logs/latest.json | CSV: {0} ({1}行) | AI 助理可直接線上下載分析！", csvName, csvRows));

                    // 同步寫入一份帶有時間戳記之歷史紀錄節點 (/logs/history/{timestamp}.json)
                    try
                    {
                        string historyKey = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                        string historyUrl = string.Format("{0}/{1}.json", cloudLogHistoryBaseUrl.TrimEnd('/'), historyKey);
                        UploadTelemetryPayload(historyUrl, json.ToString(), detectedWifiIp, 10000);
                    }
                    catch (Exception exH)
                    {
                        WriteHmiLog("CLOUD_LOG", "歷史節點同步略過: " + exH.Message);
                    }

                    // 觸發雲端歷史日誌清理檢查 (依保留天數與筆數限制)
                    PurgeCloudLogsAsync(false);
                    // 觸發本地歷史日誌清理檢查 (保留最新 30 筆)
                    PurgeLocalLogs(false);

                    if (isManualClick && this.IsHandleCreated && !this.IsDisposed)
                    {
                        this.BeginInvoke(new Action(() => {
                            MessageBox.Show(string.Format("【✅ 日誌雲端上傳成功】\n\n- 檔案: {0} (共 {1} 行)\n- 時間: {2}\n\n已成功同步至 Firebase 雲端！\nAI 助理現在可直接線上下載分析，不需使用隨身碟。", csvName, csvRows, timeStr),
                                "日誌雲端上傳成功", MessageBoxButtons.OK, MessageBoxIcon.Information);
                        }));
                    }
                }
                catch (Exception ex)
                {
                    WriteHmiLog("CLOUD_LOG_ERR", "【❌ 日誌雲端上傳失敗】" + ex.Message);
                    if (isManualClick && this.IsHandleCreated && !this.IsDisposed)
                    {
                        this.BeginInvoke(new Action(() => {
                            MessageBox.Show("日誌雲端上傳失敗: " + ex.Message + "\n請確認 Wi-Fi 網路連線是否正常。", "上傳失敗", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        }));
                    }
                }
            });
        }

        // ── 雲端日誌生命週期清理器 (依設定之「保留天數」與「最大歷史筆數」自動修剪) ──────
        public void PurgeCloudLogsAsync(bool isManualClick = false)
        {
            ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    string historyListUrl = cloudLogHistoryBaseUrl.TrimEnd('/') + ".json?shallow=true";
                    int code;
                    string resp = SendHttpRequest("GET", historyListUrl, null, detectedWifiIp, 10000, out code);
                    if (code != 200 || string.IsNullOrEmpty(resp) || resp == "null")
                    {
                        if (isManualClick && this.IsHandleCreated && !this.IsDisposed)
                        {
                            this.BeginInvoke(new Action(() => {
                                MessageBox.Show("雲端歷史日誌目前為空或無須清理。", "清理提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                            }));
                        }
                        return;
                    }

                    // 解析 Firebase shallow 回傳之鍵值 (例如: {"20260909_080000":true,"20260909_083000":true})
                    List<string> keys = new List<string>();
                    string[] parts = resp.Trim('{', '}', ' ', '\r', '\n').Split(',');
                    for (int i = 0; i < parts.Length; i++)
                    {
                        string p = parts[i].Trim();
                        int colIdx = p.IndexOf(':');
                        if (colIdx > 0)
                        {
                            string key = p.Substring(0, colIdx).Trim('\"', ' ');
                            if (!string.IsNullOrEmpty(key)) keys.Add(key);
                        }
                    }

                    if (keys.Count == 0) return;
                    keys.Sort(); // 按時間升序排列 (最早的在前面)

                    List<string> toDelete = new List<string>();
                    DateTime cutoffDate = DateTime.Now.AddDays(-Math.Max(1, cloudLogMaxDays));

                    // 1. 檢查過期天數 (yyyyMMdd_HHmmss)
                    for (int i = 0; i < keys.Count; i++)
                    {
                        string k = keys[i];
                        try
                        {
                            if (k.Length >= 8)
                            {
                                int y = int.Parse(k.Substring(0, 4));
                                int m = int.Parse(k.Substring(4, 2));
                                int d = int.Parse(k.Substring(6, 2));
                                DateTime dt = new DateTime(y, m, d);
                                if (dt < cutoffDate.Date)
                                {
                                    if (!toDelete.Contains(k)) toDelete.Add(k);
                                }
                            }
                        }
                        catch { }
                    }

                    // 2. 檢查總筆數限制 (保留最新 N 筆，超過的由最早的刪除)
                    int remainingCount = keys.Count - toDelete.Count;
                    int maxAllowed = Math.Max(5, cloudLogMaxHistoryCount);
                    if (remainingCount > maxAllowed)
                    {
                        int excess = remainingCount - maxAllowed;
                        for (int i = 0; i < keys.Count && excess > 0; i++)
                        {
                            string k = keys[i];
                            if (!toDelete.Contains(k))
                            {
                                toDelete.Add(k);
                                excess--;
                            }
                        }
                    }

                    if (toDelete.Count == 0)
                    {
                        if (isManualClick && this.IsHandleCreated && !this.IsDisposed)
                        {
                            this.BeginInvoke(new Action(() => {
                                MessageBox.Show(string.Format("雲端歷史日誌健康良好！\n目前共 {0} 筆 (限制 {1} 筆 / {2} 天內)，無過期日誌需清理。", keys.Count, maxAllowed, cloudLogMaxDays),
                                    "雲端日誌健康", MessageBoxButtons.OK, MessageBoxIcon.Information);
                            }));
                        }
                        return;
                    }

                    // 執行刪除作業
                    int deletedCount = 0;
                    for (int i = 0; i < toDelete.Count; i++)
                    {
                        string delKey = toDelete[i];
                        string delUrl = string.Format("{0}/{1}.json", cloudLogHistoryBaseUrl.TrimEnd('/'), delKey);
                        int delCode;
                        SendHttpRequest("DELETE", delUrl, null, detectedWifiIp, 8000, out delCode);
                        if (delCode == 200 || delCode == 204) deletedCount++;
                    }

                    WriteHmiLog("CLOUD_PURGE", string.Format("【🧹 雲端日誌自動清理完成】已清除 {0} 筆過期歷史日誌 (規則: 超過 {1} 天或超過 {2} 筆上限)。", deletedCount, cloudLogMaxDays, maxAllowed));

                    if (isManualClick && this.IsHandleCreated && !this.IsDisposed)
                    {
                        this.BeginInvoke(new Action(() => {
                            MessageBox.Show(string.Format("【🧹 雲端日誌清理完成】\n\n已成功刪除 {0} 筆過期歷史日誌！\n保留規則：最新 {1} 筆，且在 {2} 天內。", deletedCount, maxAllowed, cloudLogMaxDays),
                                "清理完成", MessageBoxButtons.OK, MessageBoxIcon.Information);
                        }));
                    }
                }
                catch (Exception ex)
                {
                    WriteHmiLog("CLOUD_PURGE_ERR", "雲端日誌清理失敗: " + ex.Message);
                    if (isManualClick && this.IsHandleCreated && !this.IsDisposed)
                    {
                        this.BeginInvoke(new Action(() => {
                            MessageBox.Show("雲端日誌清理失敗: " + ex.Message, "清理失敗", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        }));
                    }
                }
            });
        }

        // ── 雲端最新日誌清空器 (清空 /logs/latest.json) ──────
        public void ClearCloudLatestLogAsync(bool isManualClick = false)
        {
            ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    int code;
                    SendHttpRequest("DELETE", cloudLogUploadUrl, null, detectedWifiIp, 10000, out code);
                    if (code == 200 || code == 204)
                    {
                        WriteHmiLog("CLOUD_LOG", "【🧹 雲端日誌已清空】端點 /logs/latest.json 資料已成功抹除。");
                        if (isManualClick && this.IsHandleCreated && !this.IsDisposed)
                        {
                            this.BeginInvoke(new Action(() => {
                                MessageBox.Show("雲端最新日誌 (/logs/latest.json) 已成功清空！", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                            }));
                        }
                    }
                    else
                    {
                        throw new Exception("HTTP 代碼 " + code);
                    }
                }
                catch (Exception ex)
                {
                    WriteHmiLog("CLOUD_LOG_ERR", "清空雲端最新日誌失敗: " + ex.Message);
                    if (isManualClick && this.IsHandleCreated && !this.IsDisposed)
                    {
                        this.BeginInvoke(new Action(() => {
                            MessageBox.Show("清空雲端最新日誌失敗: " + ex.Message, "錯誤", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        }));
                    }
                }
            });
        }

        // ── 本地日誌生命週期清理器 (依設定之「最大歷史筆數」自動修剪本地 logs/ 目錄) ──────
        public void PurgeLocalLogs(bool isManualClick = false)
        {
            ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                    string logDir = Path.Combine(baseDir, "logs");
                    if (!Directory.Exists(logDir))
                    {
                        if (isManualClick && this.IsHandleCreated && !this.IsDisposed)
                        {
                            this.BeginInvoke(new Action(() => {
                                MessageBox.Show("本地 logs 目錄不存在，尚無日誌檔案需清理。", "清理提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                            }));
                        }
                        return;
                    }

                    var dirInfo = new DirectoryInfo(logDir);
                    int maxAllowed = Math.Max(5, localLogMaxHistoryCount);
                    int deletedCsvCount = 0;
                    int deletedLogCount = 0;

                    // 1. 清理測試資料 CSV 檔案 (*.csv)，保留最新 maxAllowed 筆
                    var csvFiles = dirInfo.GetFiles("*.csv");
                    Array.Sort(csvFiles, (a, b) => b.LastWriteTime.CompareTo(a.LastWriteTime)); // 由新到舊排序

                    if (csvFiles.Length > maxAllowed)
                    {
                        for (int i = maxAllowed; i < csvFiles.Length; i++)
                        {
                            FileInfo fi = csvFiles[i];
                            try
                            {
                                // 若為當前正在錄製之檔案，跳過不刪除
                                if (isManualRecording && !string.IsNullOrEmpty(manualRecordFilePath) &&
                                    string.Equals(fi.FullName, manualRecordFilePath, StringComparison.OrdinalIgnoreCase))
                                {
                                    continue;
                                }
                                fi.Delete();
                                deletedCsvCount++;

                                // 同步清理對應之同名 .gbd 二進位檔案 (若存在)
                                string gbdPath = Path.Combine(fi.DirectoryName, Path.GetFileNameWithoutExtension(fi.Name) + ".gbd");
                                if (File.Exists(gbdPath))
                                {
                                    try { File.Delete(gbdPath); } catch { }
                                }
                            }
                            catch { }
                        }
                    }

                    // 2. 清理歷史崩潰日誌 (CRASH_REPORT_*.log)，保留最新 maxAllowed 筆
                    var crashFiles = dirInfo.GetFiles("CRASH_REPORT_*.log");
                    Array.Sort(crashFiles, (a, b) => b.LastWriteTime.CompareTo(a.LastWriteTime));
                    if (crashFiles.Length > maxAllowed)
                    {
                        for (int i = maxAllowed; i < crashFiles.Length; i++)
                        {
                            FileInfo fi = crashFiles[i];
                            try
                            {
                                fi.Delete();
                                deletedLogCount++;
                            }
                            catch { }
                        }
                    }

                    // 3. 清理歷史歸檔系統日誌 (hmi_telemetry_*.log)，保留最新 maxAllowed 筆
                    var archivedLogs = dirInfo.GetFiles("hmi_telemetry_*.log");
                    Array.Sort(archivedLogs, (a, b) => b.LastWriteTime.CompareTo(a.LastWriteTime));
                    if (archivedLogs.Length > maxAllowed)
                    {
                        for (int i = maxAllowed; i < archivedLogs.Length; i++)
                        {
                            FileInfo fi = archivedLogs[i];
                            try
                            {
                                fi.Delete();
                                deletedLogCount++;
                            }
                            catch { }
                        }
                    }

                    // 4. 若使用者設定了自訂 RAW DATA 儲存目錄，同步清理該目錄之 *.csv
                    if (!string.IsNullOrEmpty(rawDataSaveDirectory) && Directory.Exists(rawDataSaveDirectory) &&
                        !string.Equals(rawDataSaveDirectory, logDir, StringComparison.OrdinalIgnoreCase))
                    {
                        try
                        {
                            var customDirInfo = new DirectoryInfo(rawDataSaveDirectory);
                            var customCsvFiles = customDirInfo.GetFiles("*.csv");
                            Array.Sort(customCsvFiles, (a, b) => b.LastWriteTime.CompareTo(a.LastWriteTime));
                            if (customCsvFiles.Length > maxAllowed)
                            {
                                for (int i = maxAllowed; i < customCsvFiles.Length; i++)
                                {
                                    FileInfo fi = customCsvFiles[i];
                                    try
                                    {
                                        if (isManualRecording && !string.IsNullOrEmpty(manualRecordFilePath) &&
                                            string.Equals(fi.FullName, manualRecordFilePath, StringComparison.OrdinalIgnoreCase))
                                        {
                                            continue;
                                        }
                                        fi.Delete();
                                        deletedCsvCount++;

                                        string gbdPath = Path.Combine(fi.DirectoryName, Path.GetFileNameWithoutExtension(fi.Name) + ".gbd");
                                        if (File.Exists(gbdPath))
                                        {
                                            try { File.Delete(gbdPath); } catch { }
                                        }
                                    }
                                    catch { }
                                }
                            }
                        }
                        catch { }
                    }

                    int totalDeleted = deletedCsvCount + deletedLogCount;
                    if (totalDeleted > 0)
                    {
                        WriteHmiLog("LOCAL_PURGE", string.Format("【🧹 本地日誌自動清理完成】已刪除 {0} 個過期歷史檔案 (CSV: {1}, LOG: {2}，保留最新 {3} 筆)。",
                            totalDeleted, deletedCsvCount, deletedLogCount, maxAllowed));
                    }

                    if (isManualClick && this.IsHandleCreated && !this.IsDisposed)
                    {
                        this.BeginInvoke(new Action(() => {
                            if (totalDeleted > 0)
                            {
                                MessageBox.Show(string.Format("【🧹 本地日誌清理完成】\n\n已成功清除 {0} 個過期舊日誌檔案！\n- 測試 CSV / GBD: {1} 筆\n- 歷史診斷 LOG: {2} 筆\n\n目前本地已修剪並保留最新 {3} 筆。",
                                    totalDeleted, deletedCsvCount, deletedLogCount, maxAllowed),
                                    "清理完成", MessageBoxButtons.OK, MessageBoxIcon.Information);
                            }
                            else
                            {
                                MessageBox.Show(string.Format("本地日誌健康良好！\n目前測試 CSV 共有 {0} 筆 (限制 {1} 筆)，無多餘舊日誌需清理。",
                                    csvFiles.Length, maxAllowed),
                                    "清理提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                            }
                        }));
                    }
                }
                catch (Exception ex)
                {
                    WriteHmiLog("LOCAL_PURGE_ERR", "本地日誌清理失敗: " + ex.Message);
                    if (isManualClick && this.IsHandleCreated && !this.IsDisposed)
                    {
                        this.BeginInvoke(new Action(() => {
                            MessageBox.Show("本地日誌清理失敗: " + ex.Message, "清理失敗", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        }));
                    }
                }
            });
        }

        // ── 頂部工具列右側容器 (B對策: 緊急停機 + Wi-Fi 網卡狀態 + Firebase 推播控制，靠右放) ──────
        private FlowLayoutPanel BuildTopRightPanel(Button btnEstop)
        {
            FlowLayoutPanel flp = new FlowLayoutPanel()
            {
                Dock = DockStyle.Right,
                FlowDirection = FlowDirection.RightToLeft,
                WrapContents = false,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                BackColor = System.Drawing.Color.Transparent,
                Margin = new Padding(0),
                Padding = new Padding(0)
            };

            // 1. 緊急停機按鈕 (最右側)
            btnEstop.Dock = DockStyle.None;
            btnEstop.Size = new System.Drawing.Size(145, 34);
            btnEstop.Margin = new Padding(4, 1, 0, 1);

            // 2. Wi-Fi 網卡狀態標籤 (顯示鎖定之 Wi-Fi IPv4，點擊開啟診斷)
            lblWifiStatus = new Label()
            {
                Text = string.IsNullOrEmpty(detectedWifiIp) ? "📶 尋找 Wi-Fi..." : ("📶 " + detectedWifiIp),
                AutoSize = false,
                Size = new System.Drawing.Size(145, 34),
                Margin = new Padding(3, 1, 3, 1),
                BackColor = System.Drawing.Color.FromArgb(15, 23, 42),
                ForeColor = System.Drawing.Color.FromArgb(56, 189, 248),
                Font = new System.Drawing.Font("Consolas", 9.5f, System.Drawing.FontStyle.Bold),
                TextAlign = System.Drawing.ContentAlignment.MiddleCenter,
                Cursor = Cursors.Hand
            };
            var ttWifi = new ToolTip();
            ttWifi.SetToolTip(lblWifiStatus, "📶 目前綁定之 Wi-Fi 網卡 IPv4 地址\n點擊可開啟【Firebase 雲端推播與網卡架構診斷工具】");
            lblWifiStatus.Click += (s, e) => ShowCloudDiagnosticsDialog();

            // 3. 雲端同步狀態膠囊 (點擊可彈出診斷)
            lblCloudSyncStatus = new Label()
            {
                Text = "☁️ Firebase: 待機",
                AutoSize = false,
                Size = new System.Drawing.Size(165, 34),
                Margin = new Padding(3, 1, 3, 1),
                BackColor = System.Drawing.Color.FromArgb(30, 41, 59),
                ForeColor = System.Drawing.Color.FromArgb(148, 163, 184),
                Font = new System.Drawing.Font("Consolas", 9.5f, System.Drawing.FontStyle.Bold),
                TextAlign = System.Drawing.ContentAlignment.MiddleCenter,
                Cursor = Cursors.Hand
            };
            var ttCloud = new ToolTip();
            ttCloud.SetToolTip(lblCloudSyncStatus, "☁️ Firebase 遙測推播狀態\n點擊可開啟【Firebase 雲端推播與網卡架構診斷工具】");
            lblCloudSyncStatus.Click += (s, e) => ShowCloudDiagnosticsDialog();

            // 4. 雲端同步開關按鈕
            btnCloudUploadToggle = new Button()
            {
                Text = "☁️ 雲端推播",
                Size = new System.Drawing.Size(88, 34),
                Margin = new Padding(3, 1, 3, 1),
                BackColor = System.Drawing.Color.FromArgb(16, 185, 129),
                ForeColor = System.Drawing.Color.White,
                Font = new System.Drawing.Font("微軟正黑體", 9f, System.Drawing.FontStyle.Bold),
                Cursor = Cursors.Hand,
                FlatStyle = FlatStyle.Flat
            };
            btnCloudUploadToggle.FlatAppearance.BorderSize = 0;
            btnCloudUploadToggle.Click += (s, e) => {
                isCloudUploadEnabled = !isCloudUploadEnabled;
                btnCloudUploadToggle.Text = isCloudUploadEnabled ? "☁️ 雲端推播" : "⏸ 暫停推播";
                btnCloudUploadToggle.BackColor = isCloudUploadEnabled ? System.Drawing.Color.FromArgb(16, 185, 129) : System.Drawing.Color.FromArgb(100, 116, 139);
                UpdateCloudSyncUI(isCloudUploadEnabled, null);
                WriteHmiLog("CLOUD", isCloudUploadEnabled ? "【使用者操作】手動恢復 Firebase 雲端遙測推播" : "【使用者操作】手動暫停 Firebase 雲端遙測推播");
                if (isCloudUploadEnabled && !isCloudUploadRunning) StartCloudUploader();
            };

            // 5. 雲端診斷按鈕 (Test Cloud)
            Button btnCloudDiagnose = new Button()
            {
                Text = "🧪 雲端診斷",
                Size = new System.Drawing.Size(88, 34),
                Margin = new Padding(3, 1, 3, 1),
                BackColor = System.Drawing.Color.FromArgb(59, 130, 246),
                ForeColor = System.Drawing.Color.White,
                Font = new System.Drawing.Font("微軟正黑體", 9f, System.Drawing.FontStyle.Bold),
                Cursor = Cursors.Hand,
                FlatStyle = FlatStyle.Flat
            };
            btnCloudDiagnose.FlatAppearance.BorderSize = 0;
            var ttDiag = new ToolTip();
            ttDiag.SetToolTip(btnCloudDiagnose, "🧪 雲端連線診斷與測試工具\n立即探測 Firebase 端點並顯示往返延遲 (RTT)、連線日誌與傳輸狀態");
            btnCloudDiagnose.Click += (s, e) => ShowCloudDiagnosticsDialog();

            // 6. 監看網頁快捷按鈕 (一鍵打開 WebMonitor.html)
            Button btnOpenWebMonitor = new Button()
            {
                Text = "📊 監看網頁",
                Size = new System.Drawing.Size(88, 34),
                Margin = new Padding(3, 1, 3, 1),
                BackColor = System.Drawing.Color.FromArgb(14, 116, 144),
                ForeColor = System.Drawing.Color.White,
                Font = new System.Drawing.Font("微軟正黑體", 9f, System.Drawing.FontStyle.Bold),
                Cursor = Cursors.Hand,
                FlatStyle = FlatStyle.Flat
            };
            btnOpenWebMonitor.FlatAppearance.BorderSize = 0;
            var ttWeb = new ToolTip();
            ttWeb.SetToolTip(btnOpenWebMonitor, "📊 一鍵開啟本地/遠端監看網頁 (WebMonitor.html)\n直接在瀏覽器即時查看指針儀表與雲端曲線");
            btnOpenWebMonitor.Click += (s, e) => {
                try {
                    string htmlPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "WebMonitor.html");
                    if (File.Exists(htmlPath)) {
                        System.Diagnostics.Process.Start(htmlPath);
                    }
                } catch (Exception ex) {
                    MessageBox.Show("無法開啟監看網頁: " + ex.Message, "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            };

            // 4.5 雲端日誌上傳按鈕 (Upload Latest Log to Cloud for AI Analysis)
            Button btnUploadLogToCloud = new Button()
            {
                Text = "☁️ 上傳日誌",
                Size = new System.Drawing.Size(88, 34),
                Margin = new Padding(3, 1, 3, 1),
                BackColor = System.Drawing.Color.FromArgb(139, 92, 246),
                ForeColor = System.Drawing.Color.White,
                Font = new System.Drawing.Font("微軟正黑體", 9f, System.Drawing.FontStyle.Bold),
                Cursor = Cursors.Hand,
                FlatStyle = FlatStyle.Flat
            };
            btnUploadLogToCloud.FlatAppearance.BorderSize = 0;
            var ttUpload = new ToolTip();
            ttUpload.SetToolTip(btnUploadLogToCloud, "☁️ 一鍵將最新產生的整合 CSV 與 LOG 推播至雲端端點\n免隨身碟，AI 助理可直接線上下載分析！");
            btnUploadLogToCloud.Click += (s, e) => UploadLatestLogToCloudAsync(true);

            // 4.6 線上更新按鈕 (Check and Perform Online Hot-Swap Update)
            btnOnlineUpdate = new Button()
            {
                Text = "🔄 線上更新",
                Size = new System.Drawing.Size(88, 34),
                Margin = new Padding(3, 1, 3, 1),
                BackColor = System.Drawing.Color.FromArgb(14, 165, 233), // Sky Blue
                ForeColor = System.Drawing.Color.White,
                Font = new System.Drawing.Font("微軟正黑體", 9f, System.Drawing.FontStyle.Bold),
                Cursor = Cursors.Hand,
                FlatStyle = FlatStyle.Flat
            };
            btnOnlineUpdate.FlatAppearance.BorderSize = 0;
            var ttUpdate = new ToolTip();
            ttUpdate.SetToolTip(btnOnlineUpdate, "🔄 一鍵檢查並執行線上熱更新\n免隨身碟，透過 Wi-Fi 自動下載最新程式並重啟更新！");
            btnOnlineUpdate.Click += (s, e) => CheckAndPerformOnlineUpdateAsync(true);

            // FlowDirection = RightToLeft，依序加入即為由右至左排列：
            flp.Controls.Add(btnEstop);
            flp.Controls.Add(lblWifiStatus);
            flp.Controls.Add(lblCloudSyncStatus);
            flp.Controls.Add(btnCloudUploadToggle);
            flp.Controls.Add(btnUploadLogToCloud);
            flp.Controls.Add(btnOnlineUpdate);
            flp.Controls.Add(btnCloudDiagnose);
            flp.Controls.Add(btnOpenWebMonitor);

            return flp;
        }

        public void ShowCloudDiagnosticsDialog()
        {
            try
            {
                Form diagForm = new Form()
                {
                    Text = "☁️ Firebase 雲端推播與 Wi-Fi 網卡架構診斷中心 (B對策)",
                    Size = new System.Drawing.Size(680, 520),
                    StartPosition = FormStartPosition.CenterParent,
                    FormBorderStyle = FormBorderStyle.FixedDialog,
                    MaximizeBox = false,
                    MinimizeBox = false,
                    BackColor = System.Drawing.Color.FromArgb(15, 23, 42),
                    ForeColor = System.Drawing.Color.White,
                    Font = new System.Drawing.Font("微軟正黑體", 10f)
                };

                Label lblTitle = new Label()
                {
                    Text = "☁️ Firebase 雲端遙測推播與 Wi-Fi 網卡診斷中心",
                    Location = new System.Drawing.Point(20, 15),
                    Size = new System.Drawing.Size(620, 28),
                    Font = new System.Drawing.Font("微軟正黑體", 13f, System.Drawing.FontStyle.Bold),
                    ForeColor = System.Drawing.Color.FromArgb(56, 189, 248)
                };
                diagForm.Controls.Add(lblTitle);

                Label lblUrlTitle = new Label()
                {
                    Text = "推播目標端點 (可點選預設或手動修改):",
                    Location = new System.Drawing.Point(20, 48),
                    Size = new System.Drawing.Size(260, 20),
                    ForeColor = System.Drawing.Color.FromArgb(148, 163, 184)
                };
                diagForm.Controls.Add(lblUrlTitle);

                TextBox txtUrl = new TextBox()
                {
                    Text = cloudUploadUrl,
                    Location = new System.Drawing.Point(20, 70),
                    Size = new System.Drawing.Size(430, 26),
                    BackColor = System.Drawing.Color.FromArgb(30, 41, 59),
                    ForeColor = System.Drawing.Color.FromArgb(226, 232, 240),
                    Font = new System.Drawing.Font("Consolas", 9.5f)
                };
                diagForm.Controls.Add(txtUrl);

                Button btnPresetFirebase = new Button()
                {
                    Text = "Firebase 原生",
                    Location = new System.Drawing.Point(458, 68),
                    Size = new System.Drawing.Size(100, 28),
                    BackColor = System.Drawing.Color.FromArgb(37, 99, 235),
                    ForeColor = System.Drawing.Color.White,
                    Font = new System.Drawing.Font("微軟正黑體", 8.5f, System.Drawing.FontStyle.Bold),
                    FlatStyle = FlatStyle.Flat,
                    Cursor = Cursors.Hand
                };
                btnPresetFirebase.FlatAppearance.BorderSize = 0;
                btnPresetFirebase.Click += (s, e) => {
                    txtUrl.Text = "https://dynamometer-live-default-rtdb.asia-southeast1.firebasedatabase.app/live.json";
                    cloudUploadUrl = txtUrl.Text;
                    WriteHmiLog("CLOUD", "【端點切換】已切換為 Firebase 原生 HTTPS: " + cloudUploadUrl);
                };
                diagForm.Controls.Add(btnPresetFirebase);

                Button btnSaveUrl = new Button()
                {
                    Text = "💾 套用",
                    Location = new System.Drawing.Point(565, 68),
                    Size = new System.Drawing.Size(75, 28),
                    BackColor = System.Drawing.Color.FromArgb(16, 185, 129),
                    ForeColor = System.Drawing.Color.White,
                    Font = new System.Drawing.Font("微軟正黑體", 8.5f, System.Drawing.FontStyle.Bold),
                    FlatStyle = FlatStyle.Flat,
                    Cursor = Cursors.Hand
                };
                btnSaveUrl.FlatAppearance.BorderSize = 0;
                btnSaveUrl.Click += (s, e) => {
                    if (!string.IsNullOrEmpty(txtUrl.Text.Trim()))
                    {
                        cloudUploadUrl = txtUrl.Text.Trim();
                        WriteHmiLog("CLOUD", "【端點套用】自訂雲端端點已套用: " + cloudUploadUrl);
                        MessageBox.Show("已套用推播端點:\n" + cloudUploadUrl, "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    }
                };
                diagForm.Controls.Add(btnSaveUrl);

                Label lblStats = new Label()
                {
                    Text = string.Format("推播: {0} | 網卡: {1} | 累計: {2} 筆 | 延遲: {3} ms",
                        isCloudUploadEnabled ? "已啟用" : "已暫停",
                        string.IsNullOrEmpty(detectedWifiIp) ? "未綁定" : detectedWifiIp,
                        cloudUploadCount,
                        lastCloudRttMs),
                    Location = new System.Drawing.Point(20, 102),
                    Size = new System.Drawing.Size(620, 22),
                    Font = new System.Drawing.Font("Consolas", 9.5f, System.Drawing.FontStyle.Bold),
                    ForeColor = (lastCloudUploadSuccess == true) ? System.Drawing.Color.FromArgb(52, 211, 153) : System.Drawing.Color.FromArgb(248, 113, 113)
                };
                diagForm.Controls.Add(lblStats);

                TextBox txtDiagLog = new TextBox()
                {
                    Location = new System.Drawing.Point(20, 128),
                    Size = new System.Drawing.Size(620, 250),
                    Multiline = true,
                    ReadOnly = true,
                    ScrollBars = ScrollBars.Vertical,
                    BackColor = System.Drawing.Color.FromArgb(2, 6, 23),
                    ForeColor = System.Drawing.Color.FromArgb(226, 232, 240),
                    Font = new System.Drawing.Font("Consolas", 9.5f)
                };
                txtDiagLog.AppendText(string.Format("[{0}] 診斷器就緒。當前綁定 Wi-Fi IP: {1}\r\n", DateTime.Now.ToString("HH:mm:ss"), string.IsNullOrEmpty(detectedWifiIp) ? "未綁定 (點擊下方按鈕診斷)" : detectedWifiIp));
                if (!string.IsNullOrEmpty(lastCloudErrorMsg))
                {
                    txtDiagLog.AppendText(string.Format("[{0}] 最近推播錯誤: {1}\r\n", lastCloudTimeStr, lastCloudErrorMsg));
                }
                diagForm.Controls.Add(txtDiagLog);

                // 操作按鈕列
                Button btnTestNow = new Button()
                {
                    Text = "🚀 雲端探測 (PUT)",
                    Location = new System.Drawing.Point(20, 395),
                    Size = new System.Drawing.Size(140, 42),
                    BackColor = System.Drawing.Color.FromArgb(37, 99, 235),
                    ForeColor = System.Drawing.Color.White,
                    Font = new System.Drawing.Font("微軟正黑體", 9.5f, System.Drawing.FontStyle.Bold),
                    FlatStyle = FlatStyle.Flat,
                    Cursor = Cursors.Hand
                };
                btnTestNow.FlatAppearance.BorderSize = 0;
                btnTestNow.Click += (s, e) => {
                    btnTestNow.Enabled = false;
                    btnTestNow.Text = "⏳ 探測中...";
                    ThreadPool.QueueUserWorkItem(_ => {
                        DateTime t0 = DateTime.Now;
                        try
                        {
                            string testJson = GetTelemetryJson();
                            // ★ 核心穿透調用：自動走 BouncyCastle TLS 1.2 穿透 Windows XP Schannel
                            string respStatus = UploadTelemetryPayload(cloudUploadUrl, testJson, detectedWifiIp, 5000);
                            int rtt = (int)(DateTime.Now - t0).TotalMilliseconds;

                            string okMsg = string.Format("[{0}] [OK] 探測成功！回應: {1}，耗時: {2} ms (Wi-Fi 網卡: {3})",
                                DateTime.Now.ToString("HH:mm:ss"), respStatus, rtt, detectedWifiIp ?? "系統預設");
                            WriteHmiLog("CLOUD_PROBE", okMsg);

                            if (diagForm != null && !diagForm.IsDisposed && diagForm.IsHandleCreated)
                            {
                                diagForm.BeginInvoke(new Action(() => {
                                    if (diagForm.IsDisposed) return;
                                    txtDiagLog.AppendText(okMsg + "\r\n");
                                    txtDiagLog.AppendText("      資料已成功寫入 Firebase，遠端網頁可立即接收！\r\n");
                                    lblStats.Text = string.Format("推播: 正常 | 延遲: {0} ms | 最新探測: {1}", rtt, DateTime.Now.ToString("HH:mm:ss"));
                                    lblStats.ForeColor = System.Drawing.Color.FromArgb(52, 211, 153);
                                    btnTestNow.Enabled = true;
                                    btnTestNow.Text = "🚀 雲端探測 (PUT)";
                                }));
                            }
                        }
                        catch (Exception ex)
                        {
                            string errMsg = string.Format("[{0}] [ERR] 探測失敗: {1} (Wi-Fi 網卡: {2})",
                                DateTime.Now.ToString("HH:mm:ss"), ex.Message, detectedWifiIp ?? "未鎖定");
                            WriteHmiLog("CLOUD_PROBE", errMsg);

                            if (diagForm != null && !diagForm.IsDisposed && diagForm.IsHandleCreated)
                            {
                                diagForm.BeginInvoke(new Action(() => {
                                    if (diagForm.IsDisposed) return;
                                    txtDiagLog.AppendText(errMsg + "\r\n");
                                    lblStats.Text = "推播: 異常 (探測失敗)";
                                    lblStats.ForeColor = System.Drawing.Color.FromArgb(248, 113, 113);
                                    btnTestNow.Enabled = true;
                                    btnTestNow.Text = "🚀 雲端探測 (PUT)";
                                }));
                            }
                        }
                    });
                };
                diagForm.Controls.Add(btnTestNow);

                Button btnNetDiag = new Button()
                {
                    Text = "🔍 網卡架構診斷",
                    Location = new System.Drawing.Point(170, 395),
                    Size = new System.Drawing.Size(145, 42),
                    BackColor = System.Drawing.Color.FromArgb(16, 185, 129),
                    ForeColor = System.Drawing.Color.White,
                    Font = new System.Drawing.Font("微軟正黑體", 9.5f, System.Drawing.FontStyle.Bold),
                    FlatStyle = FlatStyle.Flat,
                    Cursor = Cursors.Hand
                };
                btnNetDiag.FlatAppearance.BorderSize = 0;
                btnNetDiag.Click += (s, e) => {
                    Action<string> logBoth = (msg) => {
                        txtDiagLog.AppendText(msg + "\r\n");
                        WriteHmiLog("NET_DIAG", msg);
                    };

                    logBoth("\r\n========================================");
                    logBoth(string.Format("[{0}] 雙網卡架構分層診斷結果 (B對策 Wi-Fi 網卡檢驗)：", DateTime.Now.ToString("HH:mm:ss")));
                    try
                    {
                        DetectWifiNetworkInterface(logBoth);
                        logBoth(string.Format("【目前鎖定之連外 Wi-Fi 網卡】: {0}", string.IsNullOrEmpty(detectedWifiIp) ? "未偵測到有效 Wi-Fi" : (detectedWifiNicName + " | IP: " + detectedWifiIp)));
                        
                        logBoth("【外網 DNS 解析測試】:");
                        try
                        {
                            var ips = Dns.GetHostAddresses("google.com");
                            logBoth(string.Format("  ✓ google.com 解析成功 -> {0}", ips[0]));
                        }
                        catch (Exception dex)
                        {
                            logBoth(string.Format("  ✗ google.com 解析失敗: {0}", dex.Message));
                        }

                        try
                        {
                            Uri u = new Uri(cloudUploadUrl);
                            var fbIps = Dns.GetHostAddresses(u.Host);
                            logBoth(string.Format("  ✓ Firebase 主機 [{0}] 解析成功 -> {1}", u.Host, fbIps[0]));
                        }
                        catch (Exception fex)
                        {
                            logBoth(string.Format("  ✗ Firebase 主機解析失敗: {0}", fex.Message));
                        }
                    }
                    catch (Exception ex)
                    {
                        logBoth("診斷例外: " + ex.Message);
                    }
                    logBoth("========================================");
                };
                diagForm.Controls.Add(btnNetDiag);

                Button btnOpenHtml = new Button()
                {
                    Text = "📊 監看網頁",
                    Location = new System.Drawing.Point(325, 395),
                    Size = new System.Drawing.Size(140, 42),
                    BackColor = System.Drawing.Color.FromArgb(14, 116, 144),
                    ForeColor = System.Drawing.Color.White,
                    Font = new System.Drawing.Font("微軟正黑體", 9.5f, System.Drawing.FontStyle.Bold),
                    FlatStyle = FlatStyle.Flat,
                    Cursor = Cursors.Hand
                };
                btnOpenHtml.FlatAppearance.BorderSize = 0;
                btnOpenHtml.Click += (s, e) => {
                    try {
                        string htmlPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "WebMonitor.html");
                        if (File.Exists(htmlPath)) System.Diagnostics.Process.Start(htmlPath);
                    } catch (Exception ex) {
                        MessageBox.Show("無法開啟監看網頁: " + ex.Message);
                    }
                };
                diagForm.Controls.Add(btnOpenHtml);

                Button btnClose = new Button()
                {
                    Text = "關閉",
                    Location = new System.Drawing.Point(475, 395),
                    Size = new System.Drawing.Size(140, 42),
                    BackColor = System.Drawing.Color.FromArgb(51, 65, 85),
                    ForeColor = System.Drawing.Color.White,
                    Font = new System.Drawing.Font("微軟正黑體", 10f),
                    FlatStyle = FlatStyle.Flat,
                    Cursor = Cursors.Hand
                };
                btnClose.FlatAppearance.BorderSize = 0;
                btnClose.Click += (s, e) => { try { diagForm.Close(); } catch { } };
                diagForm.Controls.Add(btnClose);

                if (this.IsHandleCreated && !this.IsDisposed)
                    diagForm.ShowDialog(this);
                else
                    diagForm.ShowDialog();
            }
            catch (Exception ex)
            {
                WriteHmiLog("DIAG_ERR", "開啟雲端診斷對話框異常: " + ex.Message);
                MessageBox.Show("開啟診斷視窗失敗: " + ex.Message, "診斷異常", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        // ── 鎖定 Wi-Fi 網卡 (B對策：遍歷全網卡，識別並鎖定連外 Wi-Fi 網卡，徹底排除 192.168.0.x 儀器專用網卡) ──
        public static string DetectWifiNetworkInterface(Action<string> logAction = null)
        {
            try
            {
                var nics = System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces();
                string bestIp = "";
                string bestNicName = "";
                string bestGateway = "";
                int bestPriority = -1;

                if (logAction != null)
                {
                    logAction(string.Format("【系統網卡拓撲分析 (共 {0} 個介面)】", nics.Length));
                }

                foreach (var nic in nics)
                {
                    if (nic.NetworkInterfaceType == System.Net.NetworkInformation.NetworkInterfaceType.Loopback) continue;

                    var ipProps = nic.GetIPProperties();
                    string ipv4 = "無 IPv4";
                    foreach (var u in ipProps.UnicastAddresses)
                    {
                        if (u.Address.AddressFamily == AddressFamily.InterNetwork)
                        {
                            string s = u.Address.ToString();
                            if (!s.StartsWith("127.") && !s.StartsWith("169.254."))
                            {
                                ipv4 = s;
                                break;
                            }
                        }
                    }

                    string gwStr = "無 (隔離)";
                    bool hasValidGateway = false;
                    foreach (var g in ipProps.GatewayAddresses)
                    {
                        if (g.Address != null && g.Address.AddressFamily == AddressFamily.InterNetwork && !g.Address.ToString().StartsWith("0."))
                        {
                            hasValidGateway = true;
                            gwStr = g.Address.ToString();
                            break;
                        }
                    }

                    bool isWireless = (nic.NetworkInterfaceType.ToString().IndexOf("Wireless", StringComparison.OrdinalIgnoreCase) >= 0)
                                      || nic.Description.IndexOf("Wi-Fi", StringComparison.OrdinalIgnoreCase) >= 0
                                      || nic.Description.IndexOf("Wireless", StringComparison.OrdinalIgnoreCase) >= 0
                                      || nic.Description.IndexOf("802.11", StringComparison.OrdinalIgnoreCase) >= 0
                                      || nic.Name.IndexOf("Wi-Fi", StringComparison.OrdinalIgnoreCase) >= 0
                                      || nic.Name.IndexOf("無線", StringComparison.OrdinalIgnoreCase) >= 0;

                    bool isInstrumentLan = ipv4.StartsWith("192.168.0.");

                    string tag = isInstrumentLan ? "【儀器專用 LAN (WT333E/GL820)】" : (isWireless ? "【Wi-Fi 無線網卡】" : "【乙太網路/外網】");

                    if (logAction != null)
                    {
                        logAction(string.Format("  * {0} [{1}] {2} | IP: {3} | 閘道: {4} | 狀態: {5}",
                            tag, nic.Name, nic.Description, ipv4, gwStr, nic.OperationalStatus));
                    }

                    if (nic.OperationalStatus != System.Net.NetworkInformation.OperationalStatus.Up) continue;
                    if (ipv4 == "無 IPv4") continue;

                    // 儀器專用 LAN 192.168.0.x 嚴禁作為外網 Wi-Fi 使用
                    if (isInstrumentLan && !isWireless) continue;

                    int priority = 0;
                    if (isWireless && hasValidGateway) priority = 4;
                    else if (hasValidGateway) priority = 3;
                    else if (isWireless) priority = 2;
                    else if (!isInstrumentLan) priority = 1;

                    if (priority > bestPriority)
                    {
                        bestPriority = priority;
                        bestIp = ipv4;
                        bestNicName = nic.Name + " (" + nic.Description + ")";
                        bestGateway = gwStr;
                    }
                }

                detectedWifiIp = bestIp;
                detectedWifiNicName = bestNicName;
                detectedWifiGateway = bestGateway;

                if (logAction != null)
                {
                    if (!string.IsNullOrEmpty(bestIp))
                    {
                        logAction(string.Format("  ==> [鎖定 Wi-Fi 連外網卡] 網卡: {0} | 出口 IP: {1} | 閘道: {2}", bestNicName, bestIp, bestGateway));
                    }
                    else
                    {
                        logAction("  ==> [警告] 未偵測到連外 Wi-Fi 網卡或具備有效閘道之網卡！");
                    }
                }

                return bestIp;
            }
            catch (Exception ex)
            {
                if (logAction != null) logAction("偵測網卡例外: " + ex.Message);
                return "";
            }
        }

        private Button BuildWebServerButton()
        {
            if (btnWebServerToggle == null)
            {
                btnWebServerToggle = new Button()
                {
                    Text = "🌐 遠端監看",
                    Size = new System.Drawing.Size(105, 34),
                    BackColor = System.Drawing.Color.FromArgb(37, 99, 235),
                    ForeColor = System.Drawing.Color.White,
                    Font = new System.Drawing.Font("微軟正黑體", 9.5f, System.Drawing.FontStyle.Bold),
                    Cursor = Cursors.Hand,
                    FlatStyle = FlatStyle.Flat
                };
                btnWebServerToggle.FlatAppearance.BorderSize = 0;
                btnWebServerToggle.Click += (s, e) =>
                {
                    if (_webServer != null && _webServer.IsRunning) StopWebServer();
                    else StartWebServer();
                };
            }
            return btnWebServerToggle;
        }

        // ── 取得本機 IP ───────────────────────────────────────────
        // ── 取得本機 IP (優先選取具備有效預設閘道的 Wi-Fi / 外網網卡，避開無閘道的儀器專用 LAN) ──
        private static string GetLocalIpAddress()
        {
            // 優先策略 1: 遍歷所有網路介面，找出具備有效預設閘道 (Gateway != 0.0.0.0) 的 IPv4 網卡
            // (在現場雙網卡拓撲下，儀器專用 LAN 192.168.0.x 無閘道，而連線廠內路由器的 Wi-Fi 網卡具備有效 Gateway)
            try
            {
                var nics = System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces();

                // 第一輪優先：具備有效閘道且為無線網卡 (Wireless80211 或名稱含 WiFi/WLAN)
                foreach (var nic in nics)
                {
                    if (nic.OperationalStatus != System.Net.NetworkInformation.OperationalStatus.Up) continue;
                    if (nic.NetworkInterfaceType == System.Net.NetworkInformation.NetworkInterfaceType.Loopback) continue;

                    bool isWireless = (nic.NetworkInterfaceType.ToString().IndexOf("Wireless", StringComparison.OrdinalIgnoreCase) >= 0)
                                      || nic.Description.IndexOf("Wi-Fi", StringComparison.OrdinalIgnoreCase) >= 0
                                      || nic.Description.IndexOf("Wireless", StringComparison.OrdinalIgnoreCase) >= 0
                                      || nic.Name.IndexOf("Wi-Fi", StringComparison.OrdinalIgnoreCase) >= 0
                                      || nic.Name.IndexOf("無線", StringComparison.OrdinalIgnoreCase) >= 0;

                    var ipProps = nic.GetIPProperties();
                    bool hasGateway = false;
                    foreach (var gw in ipProps.GatewayAddresses)
                    {
                        if (gw.Address != null && gw.Address.AddressFamily == AddressFamily.InterNetwork && !gw.Address.ToString().StartsWith("0."))
                        {
                            hasGateway = true;
                            break;
                        }
                    }

                    if (isWireless && hasGateway)
                    {
                        foreach (var uni in ipProps.UnicastAddresses)
                        {
                            if (uni.Address.AddressFamily == AddressFamily.InterNetwork)
                            {
                                string s = uni.Address.ToString();
                                if (!s.StartsWith("127.") && !s.StartsWith("169.254.")) return s;
                            }
                        }
                    }
                }

                // 第二輪優先：任何具備有效閘道 (Default Gateway) 的網卡 (通常為連線外網或內部路由器的網卡)
                foreach (var nic in nics)
                {
                    if (nic.OperationalStatus != System.Net.NetworkInformation.OperationalStatus.Up) continue;
                    if (nic.NetworkInterfaceType == System.Net.NetworkInformation.NetworkInterfaceType.Loopback) continue;

                    var ipProps = nic.GetIPProperties();
                    bool hasGateway = false;
                    foreach (var gw in ipProps.GatewayAddresses)
                    {
                        if (gw.Address != null && gw.Address.AddressFamily == AddressFamily.InterNetwork && !gw.Address.ToString().StartsWith("0."))
                        {
                            hasGateway = true;
                            break;
                        }
                    }

                    if (hasGateway)
                    {
                        foreach (var uni in ipProps.UnicastAddresses)
                        {
                            if (uni.Address.AddressFamily == AddressFamily.InterNetwork)
                            {
                                string s = uni.Address.ToString();
                                if (!s.StartsWith("127.") && !s.StartsWith("169.254.")) return s;
                            }
                        }
                    }
                }
            }
            catch { }

            // 策略 2: Socket UDP 探測 (由作業系統路由表自動決定通往 8.8.8.8 的出口網卡 IP)
            try
            {
                using (var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, 0))
                {
                    socket.Connect("8.8.8.8", 65530);
                    var ep = socket.LocalEndPoint as IPEndPoint;
                    if (ep != null && !ep.Address.ToString().StartsWith("127.")) return ep.Address.ToString();
                }
            }
            catch { }

            // 策略 3: 備援：任一非 127/169.254 的 IPv4 地址 (避開 192.168.0.x 儀器網段)
            try
            {
                var nics = System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces();
                string nonKebIp = null;
                string fallbackIp = null;
                foreach (var nic in nics)
                {
                    if (nic.OperationalStatus != System.Net.NetworkInformation.OperationalStatus.Up) continue;
                    if (nic.NetworkInterfaceType == System.Net.NetworkInformation.NetworkInterfaceType.Loopback) continue;

                    foreach (var uni in nic.GetIPProperties().UnicastAddresses)
                    {
                        if (uni.Address.AddressFamily == AddressFamily.InterNetwork)
                            {
                            string s = uni.Address.ToString();
                            if (!s.StartsWith("127.") && !s.StartsWith("169.254."))
                            {
                                if (fallbackIp == null) fallbackIp = s;
                                if (!s.StartsWith("192.168.0.")) { nonKebIp = s; break; }
                            }
                        }
                    }
                    if (nonKebIp != null) return nonKebIp;
                }
                if (fallbackIp != null) return fallbackIp;
            }
            catch { }

            try
            {
                var addrs = Dns.GetHostAddresses(Dns.GetHostName());
                foreach (var a in addrs)
                {
                    if (a.AddressFamily == AddressFamily.InterNetwork && !a.ToString().StartsWith("127."))
                        return a.ToString();
                }
            }
            catch { }

            return "127.0.0.1";
        }

        // ── 遙測 JSON 序列化 (手動拼接，零外部依賴，相容 .NET 4.0) ──
        internal string GetTelemetryJson()
        {
            // 讀取溫度通道快照（防止跨執行緒陣列競爭）
            double[] temps = new double[20];
            try
            {
                if (gbdChTemps != null)
                    Array.Copy(gbdChTemps, temps, Math.Min(gbdChTemps.Length, 20));
            }
            catch { }

            double tempMax = 0.0;
            for (int i = 0; i < temps.Length; i++)
                if (temps[i] > tempMax && temps[i] < 999.0) tempMax = temps[i];

            string statusText = webRemoteStatusText ?? "待機中";
            string phaseText  = webRemotePhaseText  ?? "";
            string modeText   = webRemoteMode       ?? "IDLE";

            // 溫度陣列 JSON
            var sbTemps = new StringBuilder("[");
            for (int i = 0; i < 20; i++)
            {
                if (i > 0) sbTemps.Append(",");
                sbTemps.Append(temps[i].ToString("F1", System.Globalization.CultureInfo.InvariantCulture));
            }
            sbTemps.Append("]");

            bool isGbdOnline = (tcpGbd   != null && tcpGbd.Connected);
            bool isPmOnline  = (tcpPower != null && tcpPower.Connected);
            int  tnTotal     = (tnResults != null) ? tnResults.Count : 0;

            var sb = new StringBuilder();
            sb.Append("{");
            AppJ(sb, "ts",                DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss"), true);
            AppJ(sb, "mode",              EscapeJson(modeText), true);
            AppJ(sb, "status_text",       EscapeJson(statusText), true);
            AppJ(sb, "phase_text",        EscapeJson(phaseText), true);
            AppN(sb, "speed",             actSpeed.ToString("F1", System.Globalization.CultureInfo.InvariantCulture));
            AppN(sb, "torque",            actTorque.ToString("F2", System.Globalization.CultureInfo.InvariantCulture));
            AppN(sb, "mech_power",        actMechPower.ToString("F1", System.Globalization.CultureInfo.InvariantCulture));
            AppN(sb, "elec_power",        actElecPower.ToString("F1", System.Globalization.CultureInfo.InvariantCulture));
            AppN(sb, "efficiency",        actEfficiency.ToString("F1", System.Globalization.CultureInfo.InvariantCulture));
            AppN(sb, "kt",                actKt.ToString("F3", System.Globalization.CultureInfo.InvariantCulture));
            AppN(sb, "current_sigma",     actCurrentSigma.ToString("F2", System.Globalization.CultureInfo.InvariantCulture));
            AppN(sb, "voltage_sigma",     actVoltageSigma.ToString("F1", System.Globalization.CultureInfo.InvariantCulture));
            AppN(sb, "pf",                actPf.ToString("F3", System.Globalization.CultureInfo.InvariantCulture));
            AppN(sb, "keb_current_a",     kebCurrent1.ToString("F2", System.Globalization.CultureInfo.InvariantCulture));
            AppN(sb, "keb_current_b",     kebCurrent2.ToString("F2", System.Globalization.CultureInfo.InvariantCulture));
            AppN(sb, "keb_frequency_a",   kebFrequency1.ToString("F2", System.Globalization.CultureInfo.InvariantCulture));
            AppN(sb, "keb_frequency_b",   kebFrequency2.ToString("F2", System.Globalization.CultureInfo.InvariantCulture));
            AppN(sb, "act_frequency",     actFrequency.ToString("F2", System.Globalization.CultureInfo.InvariantCulture));
            AppN(sb, "temp_max",          tempMax.ToString("F1", System.Globalization.CultureInfo.InvariantCulture));
            sb.Append("\"temp_ch\":" + sbTemps + ",");
            AppB(sb, "gbd_online",        isGbdOnline);
            AppB(sb, "pm_online",         isPmOnline);
            AppB(sb, "is_sim",            false);
            AppN(sb, "duty_elapsed_sec",  dutyElapsedSec.ToString());
            AppN(sb, "duty_total_sec",    dutyTotalSec.ToString());
            AppN(sb, "tn_step",           tnCurrentStep.ToString());
            AppN(sb, "tn_total",          tnTotal.ToString());
            long epochMs = (long)(DateTime.UtcNow - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalMilliseconds;
            AppN(sb, "epoch_ms",          epochMs.ToString());
            AppN(sb, "seq",               cloudUploadCount.ToString());
            AppB(sb, "is_crashed",        isSystemCrashed);
            AppJ(sb, "crash_reason",      EscapeJson(systemCrashReason ?? ""), true);
            AppJ(sb, "last_alert_cat",    EscapeJson(lastCriticalCategory ?? ""), true);
            AppJ(sb, "last_alert_msg",    EscapeJson(lastCriticalLog ?? ""), true);
            AppJ(sb, "last_alert_time",   EscapeJson(lastCriticalTime ?? ""), false);
            sb.Append("}");
            return sb.ToString();
        }

        // JSON helpers
        private static void AppJ(StringBuilder sb, string k, string v, bool comma = false)
        { sb.Append("\"" + k + "\":\"" + v + "\"" + (comma ? "," : "")); }
        private static void AppN(StringBuilder sb, string k, string v)
        { sb.Append("\"" + k + "\":" + v + ","); }
        private static void AppB(StringBuilder sb, string k, bool v)
        { sb.Append("\"" + k + "\":" + (v ? "true" : "false") + ","); }

        private static string EscapeJson(string s)
        {
            if (s == null) return "";
            return s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", "").Replace("\n", " ");
        }

        // ============================================================
        // 內部 Web Server 類別 (基於 TcpListener，純原生 Socket，免管理者權限)
        // ============================================================
        private class DynWebServer
        {
            private int               _port;
            private readonly MainForm _form;
            private TcpListener       _tcpListener;
            private Thread            _listenerThread;
            private Thread            _pushThread;
            private volatile bool     _running;

            private readonly object          _sseLock    = new object();
            private readonly List<TcpClient> _sseClients = new List<TcpClient>();

            public bool IsRunning { get { return _running; } }
            public int  Port      { get { return _port; } }

            public DynWebServer(int port, MainForm form)
            {
                _port = port;
                _form = form;
            }

            public void Start()
            {
                int[] candidatePorts = new int[] { _port, 8081, 8088, 8888, 8089 };
                bool started = false;
                Exception lastEx = null;

                foreach (int p in candidatePorts)
                {
                    try
                    {
                        _tcpListener = new TcpListener(IPAddress.Any, p);
                        _tcpListener.Server.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                        _tcpListener.Start();
                        _port = p;
                        started = true;
                        break;
                    }
                    catch (Exception ex)
                    {
                        lastEx = ex;
                        try { if (_tcpListener != null) _tcpListener.Stop(); } catch { }
                    }
                }

                if (!started && lastEx != null)
                {
                    throw lastEx;
                }

                _running = true;

                _listenerThread = new Thread(ListenLoop) { IsBackground = true, Name = "DynWebListener" };
                _listenerThread.Start();

                _pushThread = new Thread(PushLoop) { IsBackground = true, Name = "DynWebPush" };
                _pushThread.Start();
            }

            public void Stop()
            {
                _running = false;
                try { if (_tcpListener != null) _tcpListener.Stop(); } catch { }
                lock (_sseLock)
                {
                    foreach (var c in _sseClients) { try { c.Close(); } catch { } }
                    _sseClients.Clear();
                }
            }

            // 主監聽迴圈
            private void ListenLoop()
            {
                while (_running)
                {
                    try
                    {
                        TcpClient client = _tcpListener.AcceptTcpClient();
                        ThreadPool.QueueUserWorkItem(HandleClient, client);
                    }
                    catch
                    {
                        if (!_running) break;
                    }
                }
            }

            private void HandleClient(object state)
            {
                TcpClient client = (TcpClient)state;
                try
                {
                    client.ReceiveTimeout = 4000;
                    client.SendTimeout = 4000;
                    NetworkStream stream = client.GetStream();

                    StringBuilder sb = new StringBuilder();
                    int b;
                    string firstLine = null;

                    while ((b = stream.ReadByte()) != -1)
                    {
                        char c = (char)b;
                        sb.Append(c);
                        if (c == '\n')
                        {
                            string line = sb.ToString().TrimEnd('\r', '\n');
                            if (firstLine == null) firstLine = line;
                            if (line.Length == 0) break; // 標頭結束
                            sb.Length = 0;
                        }
                        if (sb.Length > 4096) break;
                    }

                    if (string.IsNullOrEmpty(firstLine))
                    {
                        try { client.Close(); } catch { }
                        return;
                    }

                    string[] parts = firstLine.Split(' ');
                    if (parts.Length < 2)
                    {
                        try { client.Close(); } catch { }
                        return;
                    }

                    string method = parts[0].ToUpper();
                    string path = parts[1].Split('?')[0].ToLower();

                    if (method == "OPTIONS")
                    {
                        byte[] corsBytes = Encoding.UTF8.GetBytes(
                            "HTTP/1.1 200 OK\r\n" +
                            "Access-Control-Allow-Origin: *\r\n" +
                            "Access-Control-Allow-Methods: GET, OPTIONS\r\n" +
                            "Access-Control-Allow-Headers: *\r\n" +
                            "Content-Length: 0\r\n" +
                            "Connection: close\r\n\r\n");
                        stream.Write(corsBytes, 0, corsBytes.Length);
                        stream.Flush();
                        client.Close();
                        return;
                    }

                    if (path == "" || path == "/" || path == "/index.html")
                    {
                        ServeHtml(client, stream);
                    }
                    else if (path == "/api/status")
                    {
                        ServeJson(client, stream);
                    }
                    else if (path == "/sse")
                    {
                        HandleSse(client, stream);
                    }
                    else
                    {
                        byte[] nf = Encoding.UTF8.GetBytes("HTTP/1.1 404 Not Found\r\nContent-Length: 0\r\nConnection: close\r\n\r\n");
                        stream.Write(nf, 0, nf.Length);
                        stream.Flush();
                        client.Close();
                    }
                }
                catch
                {
                    try { client.Close(); } catch { }
                }
            }

            private void ServeHtml(TcpClient client, NetworkStream stream)
            {
                try
                {
                    string html = null;
                    string localPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "WebMonitor.html");
                    if (File.Exists(localPath))
                    {
                        html = File.ReadAllText(localPath, Encoding.UTF8);
                    }
                    if (string.IsNullOrEmpty(html))
                    {
                        html = GetEmbeddedHtml();
                    }
                    byte[] body = Encoding.UTF8.GetBytes(html);
                    string header = "HTTP/1.1 200 OK\r\n" +
                                    "Content-Type: text/html; charset=utf-8\r\n" +
                                    "Content-Length: " + body.Length + "\r\n" +
                                    "Cache-Control: no-cache\r\n" +
                                    "Connection: close\r\n\r\n";
                    byte[] hBytes = Encoding.UTF8.GetBytes(header);
                    stream.Write(hBytes, 0, hBytes.Length);
                    stream.Write(body, 0, body.Length);
                    stream.Flush();
                }
                catch { }
                finally
                {
                    try { client.Close(); } catch { }
                }
            }

            private void ServeJson(TcpClient client, NetworkStream stream)
            {
                try
                {
                    byte[] body = Encoding.UTF8.GetBytes(_form.GetTelemetryJson());
                    string header = "HTTP/1.1 200 OK\r\n" +
                                    "Content-Type: application/json; charset=utf-8\r\n" +
                                    "Content-Length: " + body.Length + "\r\n" +
                                    "Access-Control-Allow-Origin: *\r\n" +
                                    "Cache-Control: no-cache\r\n" +
                                    "Connection: close\r\n\r\n";
                    byte[] hBytes = Encoding.UTF8.GetBytes(header);
                    stream.Write(hBytes, 0, hBytes.Length);
                    stream.Write(body, 0, body.Length);
                    stream.Flush();
                }
                catch { }
                finally
                {
                    try { client.Close(); } catch { }
                }
            }

            private void HandleSse(TcpClient client, NetworkStream stream)
            {
                try
                {
                    client.ReceiveTimeout = 0;
                    client.SendTimeout = 2000;

                    string header = "HTTP/1.1 200 OK\r\n" +
                                    "Content-Type: text/event-stream\r\n" +
                                    "Cache-Control: no-cache\r\n" +
                                    "Connection: keep-alive\r\n" +
                                    "Access-Control-Allow-Origin: *\r\n" +
                                    "X-Accel-Buffering: no\r\n\r\n" +
                                    ": ping\n\n";
                    byte[] hBytes = Encoding.UTF8.GetBytes(header);
                    stream.Write(hBytes, 0, hBytes.Length);
                    stream.Flush();

                    lock (_sseLock)
                    {
                        _sseClients.Add(client);
                    }
                }
                catch
                {
                    try { client.Close(); } catch { }
                }
            }

            // SSE 推播迴圈：每 500ms 推最新 JSON 給所有連線中的瀏覽器
            private void PushLoop()
            {
                while (_running)
                {
                    Thread.Sleep(500);
                    string json = null;
                    try { json = _form.GetTelemetryJson(); } catch { continue; }

                    byte[] buf = Encoding.UTF8.GetBytes("data: " + json + "\n\n");
                    List<TcpClient> dead = null;
                    lock (_sseLock)
                    {
                        foreach (var client in _sseClients)
                        {
                            try
                            {
                                NetworkStream ns = client.GetStream();
                                ns.Write(buf, 0, buf.Length);
                                ns.Flush();
                            }
                            catch
                            {
                                if (dead == null) dead = new List<TcpClient>();
                                dead.Add(client);
                            }
                        }
                        if (dead != null)
                        {
                            foreach (var d in dead)
                            {
                                try { d.Close(); } catch { }
                                _sseClients.Remove(d);
                            }
                        }
                    }
                }
            }

            // ── 嵌入式 HTML5 儀表板 (暗色工業風) ────────────────────
            private static string GetEmbeddedHtml()
            {
                return "<!DOCTYPE html>\n" +
"<html lang=\"zh-TW\">\n" +
"<head>\n" +
"<meta charset=\"UTF-8\">\n" +
"<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">\n" +
"<title>Dynamometer HMI \u2014 \u9060\u7aef\u5373\u6642\u76e3\u770b</title>\n" +
"<style>\n" +
"@import url('https://fonts.googleapis.com/css2?family=Inter:wght@400;600;700&family=JetBrains+Mono:wght@400;700&display=swap');\n" +
"*{box-sizing:border-box;margin:0;padding:0;}\n" +
"body{background:#0a0e1a;color:#e2e8f0;font-family:'Inter',sans-serif;min-height:100vh;}\n" +
"header{background:linear-gradient(90deg,#0f172a,#1e293b);border-bottom:2px solid #334155;\n" +
"  padding:12px 20px;display:flex;align-items:center;justify-content:space-between;flex-wrap:wrap;gap:8px;}\n" +
"header h1{font-size:1.1rem;font-weight:700;letter-spacing:.5px;\n" +
"  background:linear-gradient(90deg,#38bdf8,#818cf8);\n" +
"  -webkit-background-clip:text;-webkit-text-fill-color:transparent;background-clip:text;}\n" +
".header-right{display:flex;align-items:center;gap:12px;font-size:.8rem;color:#94a3b8;}\n" +
"#conn-dot{width:10px;height:10px;border-radius:50%;background:#ef4444;display:inline-block;margin-right:4px;\n" +
"  box-shadow:0 0 8px #ef4444;transition:background .4s,box-shadow .4s;}\n" +
"#conn-dot.live{background:#22c55e;box-shadow:0 0 10px #22c55e;}\n" +
"#mode-banner{background:linear-gradient(90deg,#1e293b,#0f172a);border-bottom:1px solid #1e3a5f;\n" +
"  padding:10px 20px;display:flex;align-items:center;justify-content:space-between;flex-wrap:wrap;gap:6px;}\n" +
"#mode-tag{font-size:.85rem;font-weight:700;padding:4px 14px;border-radius:20px;\n" +
"  background:#1e3a5f;color:#38bdf8;letter-spacing:.5px;border:1px solid #38bdf8;}\n" +
"#status-text{font-size:.9rem;color:#cbd5e1;flex:1;padding-left:16px;}\n" +
"#phase-text{font-size:.8rem;color:#94a3b8;text-align:right;}\n" +
"#ts{font-size:.72rem;color:#475569;}\n" +
".dashboard{padding:16px;display:grid;\n" +
"  grid-template-columns:repeat(auto-fit,minmax(180px,1fr));gap:12px;}\n" +
".card{background:linear-gradient(135deg,#1e293b,#0f172a);\n" +
"  border:1px solid #334155;border-radius:12px;\n" +
"  padding:14px 16px;position:relative;overflow:hidden;\n" +
"  transition:border-color .3s,box-shadow .3s;}\n" +
".card:hover{border-color:#38bdf8;box-shadow:0 0 18px rgba(56,189,248,.15);}\n" +
".card::before{content:'';position:absolute;top:0;left:0;right:0;height:3px;\n" +
"  background:var(--accent,#38bdf8);border-radius:3px 3px 0 0;}\n" +
".card-label{font-size:.72rem;font-weight:600;color:#64748b;letter-spacing:.8px;text-transform:uppercase;margin-bottom:6px;}\n" +
".card-value{font-family:'JetBrains Mono',monospace;font-size:2rem;font-weight:700;\n" +
"  color:var(--accent,#38bdf8);line-height:1.1;}\n" +
".card-unit{font-size:.75rem;color:#475569;margin-top:3px;}\n" +
".c-speed{--accent:#38bdf8;} .c-torque{--accent:#f59e0b;} .c-mpower{--accent:#a78bfa;}\n" +
".c-epower{--accent:#fb923c;} .c-eff{--accent:#22c55e;} .c-current{--accent:#e879f9;}\n" +
".c-temp{--accent:#f87171;} .c-pf{--accent:#67e8f9;} .c-keb{--accent:#fbbf24;}\n" +
".progress-section{padding:0 16px 16px;}\n" +
".progress-label{font-size:.75rem;color:#64748b;margin-bottom:6px;display:flex;justify-content:space-between;}\n" +
".progress-track{background:#1e293b;border-radius:99px;height:10px;overflow:hidden;border:1px solid #334155;}\n" +
".progress-fill{height:100%;border-radius:99px;\n" +
"  background:linear-gradient(90deg,#38bdf8,#818cf8);\n" +
"  transition:width .8s ease;min-width:0;}\n" +
".temp-section{padding:0 16px 16px;}\n" +
".temp-title{font-size:.75rem;font-weight:600;color:#64748b;letter-spacing:.5px;text-transform:uppercase;margin-bottom:8px;}\n" +
".temp-grid{display:grid;grid-template-columns:repeat(auto-fill,minmax(70px,1fr));gap:6px;}\n" +
".temp-cell{background:#1e293b;border:1px solid #334155;border-radius:8px;\n" +
"  padding:6px 8px;text-align:center;transition:border-color .3s;}\n" +
".temp-cell.active{border-color:#f87171;}\n" +
".temp-cell .ch-name{font-size:.6rem;color:#64748b;}\n" +
".temp-cell .ch-val{font-family:'JetBrains Mono',monospace;font-size:.85rem;font-weight:700;color:#f87171;}\n" +
".temp-cell.off .ch-val{color:#374151;}\n" +
".status-row{padding:10px 20px;display:flex;align-items:center;gap:16px;flex-wrap:wrap;\n" +
"  background:#0f172a;border-top:1px solid #1e293b;font-size:.75rem;color:#475569;}\n" +
".badge{display:inline-flex;align-items:center;gap:4px;padding:3px 10px;border-radius:20px;font-size:.7rem;font-weight:600;}\n" +
".badge.on{background:#064e3b;color:#34d399;border:1px solid #34d399;}\n" +
".badge.off{background:#1c1917;color:#6b7280;border:1px solid #374151;}\n" +
".badge.sim{background:#3b1f08;color:#fb923c;border:1px solid #fb923c;}\n" +
"@media(max-width:480px){.card-value{font-size:1.5rem;}\n" +
"  .dashboard{grid-template-columns:repeat(2,1fr);gap:8px;padding:10px;}}\n" +
"</style>\n" +
"</head>\n" +
"<body>\n" +
"<header>\n" +
"  <h1>\u26a1 Dynamometer HMI \u2014 \u9060\u7aef\u5373\u6642\u76e3\u770b</h1>\n" +
"  <div class=\"header-right\">\n" +
"    <span id=\"conn-dot\"></span><span id=\"conn-label\">\u9023\u7dda\u4e2d...</span>\n" +
"    <span id=\"ts\">--</span>\n" +
"  </div>\n" +
"</header>\n" +
"<div id=\"mode-banner\">\n" +
"  <span id=\"mode-tag\">IDLE</span>\n" +
"  <span id=\"status-text\">\u7b49\u5f85\u8cc7\u6599...</span>\n" +
"  <span id=\"phase-text\"></span>\n" +
"</div>\n" +
"<div class=\"dashboard\">\n" +
"  <div class=\"card c-speed\">\n" +
"    <div class=\"card-label\">\u8f49\u901f Speed</div>\n" +
"    <div class=\"card-value\" id=\"v-speed\">---</div>\n" +
"    <div class=\"card-unit\">rpm</div></div>\n" +
"  <div class=\"card c-torque\">\n" +
"    <div class=\"card-label\">\u8f49\u77e9 Torque</div>\n" +
"    <div class=\"card-value\" id=\"v-torque\">---</div>\n" +
"    <div class=\"card-unit\">Nm</div></div>\n" +
"  <div class=\"card c-mpower\">\n" +
"    <div class=\"card-label\">\u6a5f\u68b0\u529f\u7387 Mech</div>\n" +
"    <div class=\"card-value\" id=\"v-mpower\">---</div>\n" +
"    <div class=\"card-unit\">W</div></div>\n" +
"  <div class=\"card c-epower\">\n" +
"    <div class=\"card-label\">\u96fb\u529f\u7387 Elec</div>\n" +
"    <div class=\"card-value\" id=\"v-epower\">---</div>\n" +
"    <div class=\"card-unit\">W</div></div>\n" +
"  <div class=\"card c-eff\">\n" +
"    <div class=\"card-label\">\u6548\u7387 Efficiency</div>\n" +
"    <div class=\"card-value\" id=\"v-eff\">---</div>\n" +
"    <div class=\"card-unit\">%</div></div>\n" +
"  <div class=\"card c-current\">\n" +
"    <div class=\"card-label\">\u96fb\u6d41 \u03a3 Current</div>\n" +
"    <div class=\"card-value\" id=\"v-cur\">---</div>\n" +
"    <div class=\"card-unit\">A (WT333E)</div></div>\n" +
"  <div class=\"card c-keb\">\n" +
"    <div class=\"card-label\">\u9a45\u52d5\u5668\u96fb\u6d41 A / B</div>\n" +
"    <div class=\"card-value\" id=\"v-keb\">---</div>\n" +
"    <div class=\"card-unit\">A (KEB)</div></div>\n" +
"  <div class=\"card c-temp\">\n" +
"    <div class=\"card-label\">\u6700\u9ad8\u6eab\u5ea6 Peak Temp</div>\n" +
"    <div class=\"card-value\" id=\"v-temp\">---</div>\n" +
"    <div class=\"card-unit\">\u2103 (Graphtec)</div></div>\n" +
"  <div class=\"card c-pf\">\n" +
"    <div class=\"card-label\">\u529f\u7387\u56e0\u6578 PF</div>\n" +
"    <div class=\"card-value\" id=\"v-pf\">---</div>\n" +
"    <div class=\"card-unit\">---</div></div>\n" +
"</div>\n" +
"<div class=\"progress-section\" id=\"duty-sec\" style=\"display:none\">\n" +
"  <div class=\"progress-label\"><span>DUTY \u6e2c\u8a66\u9032\u5ea6</span><span id=\"duty-time\">0 / 0 s</span></div>\n" +
"  <div class=\"progress-track\"><div class=\"progress-fill\" id=\"duty-bar\" style=\"width:0%\"></div></div>\n" +
"</div>\n" +
"<div class=\"progress-section\" id=\"tn-sec\" style=\"display:none\">\n" +
"  <div class=\"progress-label\"><span>T-N \u6e2c\u8a66\u6b65\u9032\u9032\u5ea6</span><span id=\"tn-step\">0 / 0</span></div>\n" +
"  <div class=\"progress-track\"><div class=\"progress-fill\" id=\"tn-bar\" style=\"width:0%\"></div></div>\n" +
"</div>\n" +
"<div class=\"temp-section\">\n" +
"  <div class=\"temp-title\">\ud83c\udf21 \u6eab\u5ea6\u901a\u9053 (GL820 / Graphtec)</div>\n" +
"  <div class=\"temp-grid\" id=\"temp-grid\"></div>\n" +
"</div>\n" +
"<div class=\"status-row\">\n" +
"  <span id=\"badge-sim\" class=\"badge off\">SIM \u6a21\u64ec</span>\n" +
"  <span id=\"badge-gbd\" class=\"badge off\">\ud83c\udf21 \u6eab\u5ea6\u8a08</span>\n" +
"  <span id=\"badge-pm\" class=\"badge off\">\u26a1 \u529f\u7387\u8a08</span>\n" +
"  <span style=\"margin-left:auto;color:#334155\">Dynamometer HMI Remote Monitor \u2014 \u50c5\u4f9b\u76e3\u770b\uff0c\u7121\u63a7\u5236\u529f\u80fd</span>\n" +
"</div>\n" +
"<script>\n" +
"(function(){\n" +
"  var grid=document.getElementById('temp-grid');\n" +
"  for(var i=0;i<20;i++){\n" +
"    var c=document.createElement('div');c.className='temp-cell off';c.id='tc'+i;\n" +
"    c.innerHTML='<div class=\"ch-name\">CH'+(i+1)+'<\\/div><div class=\"ch-val\" id=\"tv'+i+'\">&mdash;<\\/div>';\n" +
"    grid.appendChild(c);\n" +
"  }\n" +
"  var dot=document.getElementById('conn-dot'),clbl=document.getElementById('conn-label'),lastTs=0;\n" +
"  function upd(d){\n" +
"    lastTs=Date.now();dot.className='live';clbl.textContent='\u5df2\u9023\u7dda';\n" +
"    document.getElementById('ts').textContent=d.ts;\n" +
"    document.getElementById('mode-tag').textContent=d.mode||'IDLE';\n" +
"    document.getElementById('status-text').textContent=d.status_text||'';\n" +
"    document.getElementById('phase-text').textContent=d.phase_text||'';\n" +
"    document.getElementById('v-speed').textContent=parseFloat(d.speed).toFixed(1);\n" +
"    document.getElementById('v-torque').textContent=parseFloat(d.torque).toFixed(2);\n" +
"    document.getElementById('v-mpower').textContent=parseFloat(d.mech_power).toFixed(0);\n" +
"    document.getElementById('v-epower').textContent=parseFloat(d.elec_power).toFixed(0);\n" +
"    document.getElementById('v-eff').textContent=parseFloat(d.efficiency).toFixed(1);\n" +
"    document.getElementById('v-cur').textContent=parseFloat(d.current_sigma).toFixed(2);\n" +
"    document.getElementById('v-keb').textContent=parseFloat(d.keb_current_a).toFixed(2)+' / '+parseFloat(d.keb_current_b).toFixed(2);\n" +
"    document.getElementById('v-temp').textContent=parseFloat(d.temp_max).toFixed(1);\n" +
"    document.getElementById('v-pf').textContent=parseFloat(d.pf).toFixed(3);\n" +
"    var ds=document.getElementById('duty-sec');\n" +
"    if(d.duty_total_sec>0){ds.style.display='';\n" +
"      document.getElementById('duty-time').textContent=d.duty_elapsed_sec+' / '+d.duty_total_sec+' s';\n" +
"      document.getElementById('duty-bar').style.width=Math.min(100,Math.round(d.duty_elapsed_sec/d.duty_total_sec*100))+'%';\n" +
"    }else ds.style.display='none';\n" +
"    var ts2=document.getElementById('tn-sec');\n" +
"    if(d.tn_total>0){ts2.style.display='';\n" +
"      document.getElementById('tn-step').textContent=d.tn_step+' / '+d.tn_total;\n" +
"      document.getElementById('tn-bar').style.width=Math.min(100,Math.round(d.tn_step/d.tn_total*100))+'%';\n" +
"    }else ts2.style.display='none';\n" +
"    var tc=d.temp_ch||[];\n" +
"    for(var i=0;i<20;i++){\n" +
"      var v=tc[i]?parseFloat(tc[i]):0;\n" +
"      var cel=document.getElementById('tc'+i),tv=document.getElementById('tv'+i);\n" +
"      if(v>0.5&&v<999){cel.className='temp-cell active';tv.textContent=v.toFixed(1)+'\u2103';}\n" +
"      else{cel.className='temp-cell off';tv.textContent='\u2014';}\n" +
"    }\n" +
"    bdg('badge-sim',d.is_sim,  'SIM \u6a21\u64ec \u25cf','SIM \u6a21\u64ec','sim');\n" +
"    bdg('badge-gbd',d.gbd_online,'\ud83c\udf21 \u6eab\u5ea6\u8a08 \u2713','\ud83c\udf21 \u6eab\u5ea6\u8a08 \u2717','on');\n" +
"    bdg('badge-pm', d.pm_online, '\u26a1 \u529f\u7387\u8a08 \u2713','\u26a1 \u529f\u7387\u8a08 \u2717','on');\n" +
"  }\n" +
"  function bdg(id,c,on,off,cls){\n" +
"    var el=document.getElementById(id);\n" +
"    if(c){el.textContent=on;el.className='badge '+cls;}else{el.textContent=off;el.className='badge off';}\n" +
"  }\n" +
"  function go(){\n" +
"    var es=new EventSource('/sse');\n" +
"    es.onmessage=function(e){try{upd(JSON.parse(e.data));}catch(x){}};\n" +
"    es.onerror=function(){dot.className='';clbl.textContent='\u9023\u7dda\u4e2d\u65b7\uff0c\u91cd\u8a66...';es.close();setTimeout(go,3000);};\n" +
"  }\n" +
"  setInterval(function(){if(Date.now()-lastTs>4000&&lastTs>0){dot.className='';clbl.textContent='\u8cc7\u6599\u903e\u6642';}},3000);\n" +
"  go();\n" +
"})();\n" +
"<" + "/script>\n" +
"</body></html>";
            }
        } // end class DynWebServer

        /// <summary>
        /// ★ Windows XP TLS 1.2 穿透引擎：
        /// 當目標端點為 https:// (如 Firebase) 時，使用 BouncyCastle 純 Managed TLS 1.2 協定棧，
        /// <summary>
        /// 底層相容 Windows XP / .NET 4.0 之 TLS 1.2 HTTP/HTTPS 萬用請求引擎。
        /// 徹底繞過 Windows XP Schannel.dll 缺乏 TLS 1.2 密碼套件之物理限制。
        /// 同時支援透過 Wi-Fi 網卡 (localWifiIp) 綁定發送，隔離 192.168.0.x 儀器網卡。
        /// 支援 GET, PUT, POST, DELETE 等標準 REST 操作，回傳 Response Body 或 HTTP 狀態字串。
        /// </summary>
        public static string SendHttpRequest(string method, string url, string jsonPayload, string localWifiIp, int timeoutMs, out int statusCode)
        {
            if (string.IsNullOrEmpty(url)) throw new ArgumentNullException("url");
            Uri uri = new Uri(url);
            string verb = string.IsNullOrEmpty(method) ? "GET" : method.ToUpper();
            statusCode = 0;

            if (uri.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase))
            {
                IPAddress[] remoteIps = Dns.GetHostAddresses(uri.Host);
                if (remoteIps == null || remoteIps.Length == 0)
                {
                    throw new Exception("DNS 解析失敗: 無法取得主機 " + uri.Host + " 之 IP");
                }

                using (TcpClient tcp = new TcpClient())
                {
                    // 若有鎖定 Wi-Fi 網卡，綁定本地出口 IP，強制繞過 192.168.0.x 儀器網卡
                    if (!string.IsNullOrEmpty(localWifiIp))
                    {
                        IPAddress localIp;
                        if (IPAddress.TryParse(localWifiIp, out localIp))
                        {
                            tcp.Client.Bind(new IPEndPoint(localIp, 0));
                        }
                    }

                    int port = uri.Port > 0 ? uri.Port : 443;
                    IAsyncResult asyncConnect = tcp.BeginConnect(remoteIps[0], port, null, null);
                    if (!asyncConnect.AsyncWaitHandle.WaitOne(timeoutMs))
                    {
                        tcp.Close();
                        throw new TimeoutException("TCP 連線超時 (" + timeoutMs + "ms) 主機: " + uri.Host + ":" + port);
                    }
                    tcp.EndConnect(asyncConnect);

                    NetworkStream ns = tcp.GetStream();
                    ns.ReadTimeout = timeoutMs;
                    ns.WriteTimeout = timeoutMs;

                    TlsClientProtocol tls = new TlsClientProtocol(ns, new SecureRandom());
                    tls.Connect(new ManagedTlsClient(uri.Host));

                    Stream tlsStream = tls.Stream;
                    byte[] body = !string.IsNullOrEmpty(jsonPayload) ? Encoding.UTF8.GetBytes(jsonPayload) : new byte[0];

                    StringBuilder reqHeader = new StringBuilder();
                    reqHeader.AppendFormat("{0} {1} HTTP/1.1\r\n", verb, uri.PathAndQuery);
                    reqHeader.AppendFormat("Host: {0}\r\n", uri.Host);
                    if (body.Length > 0 || verb == "PUT" || verb == "POST")
                    {
                        reqHeader.Append("Content-Type: application/json; charset=utf-8\r\n");
                        reqHeader.AppendFormat("Content-Length: {0}\r\n", body.Length);
                    }
                    reqHeader.Append("Connection: close\r\n\r\n");

                    byte[] headerBytes = Encoding.ASCII.GetBytes(reqHeader.ToString());
                    tlsStream.Write(headerBytes, 0, headerBytes.Length);
                    if (body.Length > 0)
                    {
                        tlsStream.Write(body, 0, body.Length);
                    }
                    tlsStream.Flush();

                    using (StreamReader sr = new StreamReader(tlsStream, Encoding.UTF8))
                    {
                        string statusLine = sr.ReadLine();
                        if (string.IsNullOrEmpty(statusLine))
                        {
                            throw new Exception("伺服器無回應 (Empty response)");
                        }

                        // 解析 HTTP 狀態碼
                        string[] statusParts = statusLine.Split(' ');
                        if (statusParts.Length >= 2)
                        {
                            int.TryParse(statusParts[1], out statusCode);
                        }

                        // 讀取 HTTP Header 直到空行
                        int contentLength = -1;
                        bool isChunked = false;
                        string line;
                        while (!string.IsNullOrEmpty(line = sr.ReadLine()))
                        {
                            if (line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase))
                            {
                                int.TryParse(line.Substring(15).Trim(), out contentLength);
                            }
                            else if (line.StartsWith("Transfer-Encoding:", StringComparison.OrdinalIgnoreCase) && line.Contains("chunked"))
                            {
                                isChunked = true;
                            }
                        }

                        // 讀取 Response Body
                        string respBody = sr.ReadToEnd();
                        return respBody;
                    }
                }
            }
            else
            {
                // 純 HTTP (Port 80) 中繼轉發
                HttpWebRequest req = (HttpWebRequest)WebRequest.Create(url);
                req.Method = verb;
                req.Timeout = timeoutMs;
                req.ReadWriteTimeout = timeoutMs;

                IPAddress wIp;
                if (!string.IsNullOrEmpty(localWifiIp) && IPAddress.TryParse(localWifiIp, out wIp))
                {
                    req.ServicePoint.BindIPEndPointDelegate = delegate(ServicePoint sp, IPEndPoint remoteEP, int retryCount)
                    {
                        return new IPEndPoint(wIp, 0);
                    };
                }

                if (!string.IsNullOrEmpty(jsonPayload))
                {
                    byte[] payload = Encoding.UTF8.GetBytes(jsonPayload);
                    req.ContentType = "application/json; charset=utf-8";
                    req.ContentLength = payload.Length;
                    using (Stream reqStream = req.GetRequestStream())
                    {
                        reqStream.Write(payload, 0, payload.Length);
                    }
                }

                try
                {
                    using (HttpWebResponse resp = (HttpWebResponse)req.GetResponse())
                    {
                        statusCode = (int)resp.StatusCode;
                        using (StreamReader sr = new StreamReader(resp.GetResponseStream(), Encoding.UTF8))
                        {
                            return sr.ReadToEnd();
                        }
                    }
                }
                catch (WebException wex)
                {
                    HttpWebResponse errResp = wex.Response as HttpWebResponse;
                    if (errResp != null)
                    {
                        statusCode = (int)errResp.StatusCode;
                        using (StreamReader sr = new StreamReader(errResp.GetResponseStream(), Encoding.UTF8))
                        {
                            return sr.ReadToEnd();
                        }
                    }
                    throw;
                }
            }
        }

        public static string UploadTelemetryPayload(string url, string jsonPayload, string localWifiIp, int timeoutMs)
        {
            int code;
            string resp = SendHttpRequest("PUT", url, jsonPayload, localWifiIp, timeoutMs, out code);
            if (code != 200 && code != 204)
            {
                throw new Exception(string.Format("HTTP 狀態異常: {0} ({1})", code, resp));
            }
            return "HTTP/1.1 " + code;
        }

        // ============================================================
        // ── 線上熱更新核心引擎 (Online Auto-Update & In-Place Hot Swap) ──
        // ============================================================

        public class UpdateManifest
        {
            public string Version = "";
            public string ReleaseDate = "";
            public string Notes = "";
            public string DownloadUrl = "";
            public string Sha256 = "";
        }

        public static UpdateManifest ParseUpdateManifest(string json)
        {
            if (string.IsNullOrEmpty(json)) return null;
            UpdateManifest m = new UpdateManifest();
            m.Version = ExtractJsonString(json, "version");
            m.ReleaseDate = ExtractJsonString(json, "release_date");
            m.Notes = ExtractJsonString(json, "notes");
            m.DownloadUrl = ExtractJsonString(json, "download_url");
            m.Sha256 = ExtractJsonString(json, "sha256");
            return m;
        }

        public static string ExtractJsonString(string json, string key)
        {
            if (string.IsNullOrEmpty(json) || string.IsNullOrEmpty(key)) return "";
            string pattern = "\"" + key + "\"\\s*:\\s*\"((?:\\\\\"|[^\"])*)\"";
            Match match = Regex.Match(json, pattern);
            if (match.Success)
            {
                string val = match.Groups[1].Value;
                return DecodeJsonString(val);
            }
            return "";
        }

        public static string DecodeJsonString(string val)
        {
            if (string.IsNullOrEmpty(val)) return "";
            try
            {
                // 1. 支援 \uXXXX unicode escape sequences
                val = Regex.Replace(val, @"\\u([0-9a-fA-F]{4})", m => ((char)Convert.ToInt32(m.Groups[1].Value, 16)).ToString());
                // 2. 處理標準 JSON 跳脫字元
                val = val.Replace("\\n", "\n")
                         .Replace("\\r", "\r")
                         .Replace("\\t", "\t")
                         .Replace("\\\"", "\"")
                         .Replace("\\\\", "\\")
                         .Replace("\\/", "/");
                return val;
            }
            catch
            {
                try { return Regex.Unescape(val); } catch { return val; }
            }
        }

        private static string ReadLineFromStream(Stream s)
        {
            StringBuilder sb = new StringBuilder();
            while (true)
            {
                int b = s.ReadByte();
                if (b == -1) break;
                if (b == 10) break; // \n
                if (b != 13) sb.Append((char)b);
            }
            return sb.ToString();
        }

        /// <summary>
        /// 下載二進位檔案 (相容 Windows XP BouncyCastle TLS 1.2、Wi-Fi 網卡綁定、301/302 跳轉與分塊傳輸)
        /// </summary>
        public static bool DownloadBinaryPayload(string url, string targetFilePath, string localWifiIp, int timeoutMs, Action<long, long, int> progressCallback, out string errorMsg)
        {
            errorMsg = "";
            string currentUrl = url;
            int redirectCount = 0;

            while (redirectCount < 5)
            {
                try
                {
                    Uri uri = new Uri(currentUrl);
                    bool isHttps = uri.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase);
                    int port = uri.Port > 0 ? uri.Port : (isHttps ? 443 : 80);

                    IPAddress[] remoteIps = Dns.GetHostAddresses(uri.Host);
                    if (remoteIps == null || remoteIps.Length == 0)
                    {
                        throw new Exception("DNS 解析失敗: 無法取得 " + uri.Host + " 之 IP");
                    }

                    using (TcpClient tcp = new TcpClient())
                    {
                        if (!string.IsNullOrEmpty(localWifiIp))
                        {
                            IPAddress localIp;
                            if (IPAddress.TryParse(localWifiIp, out localIp))
                            {
                                tcp.Client.Bind(new IPEndPoint(localIp, 0));
                            }
                        }

                        IAsyncResult asyncConnect = tcp.BeginConnect(remoteIps[0], port, null, null);
                        if (!asyncConnect.AsyncWaitHandle.WaitOne(timeoutMs))
                        {
                            tcp.Close();
                            throw new TimeoutException(string.Format("TCP 連線超時 ({0}ms) 主機: {1}:{2}", timeoutMs, uri.Host, port));
                        }
                        tcp.EndConnect(asyncConnect);

                        NetworkStream ns = tcp.GetStream();
                        ns.ReadTimeout = timeoutMs;
                        ns.WriteTimeout = timeoutMs;

                        Stream stream = ns;
                        TlsClientProtocol tls = null;
                        if (isHttps)
                        {
                            tls = new TlsClientProtocol(ns, new SecureRandom());
                            tls.Connect(new ManagedTlsClient(uri.Host));
                            stream = tls.Stream;
                        }

                        StringBuilder reqHeader = new StringBuilder();
                        reqHeader.AppendFormat("GET {0} HTTP/1.1\r\n", uri.PathAndQuery);
                        reqHeader.AppendFormat("Host: {0}\r\n", uri.Host);
                        reqHeader.Append("User-Agent: Dynamometer-HMI-Updater/2.5.0 (Windows XP; x86)\r\n");
                        reqHeader.Append("Accept: */*\r\n");
                        reqHeader.Append("Accept-Encoding: identity\r\n");
                        reqHeader.Append("Connection: close\r\n\r\n");

                        byte[] headerBytes = Encoding.ASCII.GetBytes(reqHeader.ToString());
                        stream.Write(headerBytes, 0, headerBytes.Length);
                        stream.Flush();

                        // 逐 Byte 讀取直到 \r\n\r\n，杜絕 StreamReader 預讀緩衝吃掉二進位 Body 之問題
                        List<byte> headerBuffer = new List<byte>();
                        while (true)
                        {
                            int b = stream.ReadByte();
                            if (b == -1) break;
                            headerBuffer.Add((byte)b);
                            int len = headerBuffer.Count;
                            if (len >= 4 &&
                                headerBuffer[len - 4] == 13 && headerBuffer[len - 3] == 10 &&
                                headerBuffer[len - 2] == 13 && headerBuffer[len - 1] == 10)
                            {
                                break;
                            }
                            if (len > 65536) throw new Exception("HTTP Header 超過 64KB 上限");
                        }

                        string headerText = Encoding.ASCII.GetString(headerBuffer.ToArray());
                        string[] headerLines = headerText.Split(new string[] { "\r\n" }, StringSplitOptions.None);
                        string statusLine = headerLines.Length > 0 ? headerLines[0] : "";
                        string[] statusParts = statusLine.Split(' ');
                        int statusCode = 0;
                        if (statusParts.Length >= 2) int.TryParse(statusParts[1], out statusCode);

                        long contentLength = -1;
                        string redirectLocation = null;
                        bool isChunked = false;

                        for (int i = 0; i < headerLines.Length; i++)
                        {
                            string h = headerLines[i];
                            if (h.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase))
                            {
                                long.TryParse(h.Substring(15).Trim(), out contentLength);
                            }
                            else if (h.StartsWith("Location:", StringComparison.OrdinalIgnoreCase))
                            {
                                redirectLocation = h.Substring(9).Trim();
                            }
                            else if (h.StartsWith("Transfer-Encoding:", StringComparison.OrdinalIgnoreCase) &&
                                     h.IndexOf("chunked", StringComparison.OrdinalIgnoreCase) >= 0)
                            {
                                isChunked = true;
                            }
                        }

                        // 處理 301/302/303/307/308 跳轉
                        if ((statusCode == 301 || statusCode == 302 || statusCode == 303 || statusCode == 307 || statusCode == 308) &&
                            !string.IsNullOrEmpty(redirectLocation))
                        {
                            Uri nextUri = new Uri(uri, redirectLocation);
                            currentUrl = nextUri.ToString();
                            redirectCount++;
                            continue;
                        }

                        if (statusCode != 200)
                        {
                            throw new Exception(string.Format("伺服器回應異常: {0} ({1})", statusCode, statusLine));
                        }

                        // 寫入暫存檔案
                        string tempFilePath = targetFilePath + ".part";
                        if (File.Exists(tempFilePath)) File.Delete(tempFilePath);

                        using (FileStream fs = new FileStream(tempFilePath, FileMode.Create, FileAccess.Write, FileShare.None, 65536))
                        {
                            byte[] buffer = new byte[32768];
                            long totalRead = 0;
                            DateTime lastReport = DateTime.UtcNow;

                            if (isChunked)
                            {
                                while (true)
                                {
                                    string hexLine = ReadLineFromStream(stream);
                                    if (string.IsNullOrEmpty(hexLine)) break;
                                    int chunkLen = 0;
                                    try { chunkLen = Convert.ToInt32(hexLine.Trim().Split(';')[0], 16); } catch { break; }
                                    if (chunkLen == 0)
                                    {
                                        ReadLineFromStream(stream); // 結尾空行
                                        break;
                                    }

                                    int bytesRemaining = chunkLen;
                                    while (bytesRemaining > 0)
                                    {
                                        int toRead = Math.Min(buffer.Length, bytesRemaining);
                                        int read = stream.Read(buffer, 0, toRead);
                                        if (read <= 0) throw new Exception("Chunked 傳輸異常中斷");
                                        fs.Write(buffer, 0, read);
                                        totalRead += read;
                                        bytesRemaining -= read;
                                    }
                                    stream.ReadByte(); // \r
                                    stream.ReadByte(); // \n

                                    if (progressCallback != null && (DateTime.UtcNow - lastReport).TotalMilliseconds >= 80)
                                    {
                                        progressCallback(totalRead, -1, -1);
                                        lastReport = DateTime.UtcNow;
                                    }
                                }
                            }
                            else
                            {
                                int bytesRead;
                                while ((bytesRead = stream.Read(buffer, 0, buffer.Length)) > 0)
                                {
                                    fs.Write(buffer, 0, bytesRead);
                                    totalRead += bytesRead;

                                    if (progressCallback != null && (DateTime.UtcNow - lastReport).TotalMilliseconds >= 80)
                                    {
                                        int percent = contentLength > 0 ? (int)((totalRead * 100) / contentLength) : -1;
                                        progressCallback(totalRead, contentLength, percent);
                                        lastReport = DateTime.UtcNow;
                                    }
                                }
                            }

                            fs.Flush();
                            if (progressCallback != null)
                            {
                                int percent = contentLength > 0 ? (int)((totalRead * 100) / contentLength) : 100;
                                progressCallback(totalRead, contentLength, percent);
                            }
                        }

                        if (File.Exists(targetFilePath)) File.Delete(targetFilePath);
                        File.Move(tempFilePath, targetFilePath);
                        return true;
                    }
                }
                catch (Exception ex)
                {
                    if (redirectCount >= 4)
                    {
                        errorMsg = "下載失敗 (轉向過多次數或連線異常): " + ex.Message;
                        return false;
                    }
                    errorMsg = ex.Message;
                    return false;
                }
            }

            errorMsg = "超過最大 HTTP 跳轉次數 (5)";
            return false;
        }

        public static void VerifyPeHeader(string filePath)
        {
            FileInfo fi = new FileInfo(filePath);
            if (fi.Length < 50000)
            {
                throw new Exception(string.Format("下載之檔案大小異常 ({0} bytes)，非完整主程式。", fi.Length));
            }
            using (FileStream fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                byte[] mz = new byte[2];
                int read = fs.Read(mz, 0, 2);
                if (read < 2 || mz[0] != 0x4D || mz[1] != 0x5A) // 'M' 'Z'
                {
                    throw new Exception("下載檔案非合法 Windows PE 執行檔 (缺少 MZ 檔頭，可能下載到錯誤網頁或已損壞)。");
                }
            }
        }

        public static void ExecuteHotSwapAndRestart(string newExePath)
        {
            try
            {
                string currentExe = Process.GetCurrentProcess().MainModule.FileName;
                string appDir = Path.GetDirectoryName(currentExe);
                string bakExe = Path.Combine(appDir, Path.GetFileNameWithoutExtension(currentExe) + ".bak");

                if (File.Exists(bakExe))
                {
                    try { File.Delete(bakExe); } catch { }
                }

                try
                {
                    // Windows 核心特性：正在執行的 EXE 可以被 Move/Rename！
                    File.Move(currentExe, bakExe);
                    File.Move(newExePath, currentExe);

                    ProcessStartInfo psi = new ProcessStartInfo(currentExe);
                    psi.WorkingDirectory = appDir;
                    Process.Start(psi);
                    Environment.Exit(0);
                }
                catch (Exception)
                {
                    // 若即時 Rename/Move 發生檔案鎖定，啟動自毀延遲批次檔進行替換
                    LaunchExternalFallbackUpdater(currentExe, newExePath, bakExe);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("線上熱替換更新失敗: " + ex.Message, "更新錯誤", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private static void LaunchExternalFallbackUpdater(string currentExe, string newExe, string bakExe)
        {
            try
            {
                string appDir = Path.GetDirectoryName(currentExe);
                string batPath = Path.Combine(appDir, "_update_swap.cmd");
                StringBuilder sb = new StringBuilder();
                sb.AppendLine("@echo off");
                sb.AppendLine("ping 127.0.0.1 -n 2 > nul");
                sb.AppendLine(string.Format("if exist \"{0}\" del \"{0}\"", bakExe));
                sb.AppendLine(string.Format("if exist \"{0}\" ren \"{0}\" \"{1}\"", currentExe, Path.GetFileName(bakExe)));
                sb.AppendLine(string.Format("if exist \"{0}\" move /y \"{0}\" \"{1}\"", newExe, currentExe));
                sb.AppendLine(string.Format("start \"\" \"{0}\"", currentExe));
                sb.AppendLine("del \"%~f0\"");
                File.WriteAllText(batPath, sb.ToString(), Encoding.Default);

                ProcessStartInfo psi = new ProcessStartInfo();
                psi.FileName = batPath;
                psi.WorkingDirectory = appDir;
                psi.WindowStyle = ProcessWindowStyle.Hidden;
                psi.CreateNoWindow = true;
                Process.Start(psi);
                Environment.Exit(0);
            }
            catch (Exception ex)
            {
                MessageBox.Show("啟動備援更新排程失敗: " + ex.Message, "更新錯誤", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        public void CheckAndPerformOnlineUpdateAsync(bool isManualClick = false)
        {
            ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    int code;
                    string resp = SendHttpRequest("GET", cloudUpdateManifestUrl, null, detectedWifiIp, 10000, out code);
                    if (code != 200 || string.IsNullOrEmpty(resp) || resp == "null")
                    {
                        if (isManualClick && this.IsHandleCreated && !this.IsDisposed)
                        {
                            this.BeginInvoke(new Action(() =>
                            {
                                MessageBox.Show("目前無法取得雲端更新資訊，請確認 Wi-Fi 網卡連線是否正常，或稍後再試。\n(伺服器代碼: " + code + ")", "線上更新檢查", MessageBoxButtons.OK, MessageBoxIcon.Information);
                            }));
                        }
                        return;
                    }

                    UpdateManifest manifest = ParseUpdateManifest(resp);
                    if (manifest == null || string.IsNullOrEmpty(manifest.DownloadUrl))
                    {
                        if (isManualClick && this.IsHandleCreated && !this.IsDisposed)
                        {
                            this.BeginInvoke(new Action(() =>
                            {
                                MessageBox.Show("雲端版本資訊格式異常，請稍後再試。", "線上更新檢查", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                            }));
                        }
                        return;
                    }

                    Version cloudVer = null;
                    Version localVer = null;
                    bool hasCloudVer = Version.TryParse(manifest.Version.TrimStart('v', 'V'), out cloudVer);
                    bool hasLocalVer = Version.TryParse(APP_VERSION.TrimStart('v', 'V'), out localVer);

                    bool isNewer = false;
                    if (hasCloudVer && hasLocalVer)
                    {
                        isNewer = cloudVer > localVer;
                    }
                    else
                    {
                        isNewer = !string.Equals(manifest.Version.Trim(), APP_VERSION.Trim(), StringComparison.OrdinalIgnoreCase);
                    }

                    if (!isManualClick)
                    {
                        // 背景靜默檢查：若有新版本，更新按鈕提示外觀
                        if (isNewer && this.IsHandleCreated && !this.IsDisposed && btnOnlineUpdate != null)
                        {
                            this.BeginInvoke(new Action(() =>
                            {
                                btnOnlineUpdate.Text = "🔄 有新版本!";
                                btnOnlineUpdate.BackColor = System.Drawing.Color.FromArgb(245, 158, 11); // Amber
                            }));
                        }
                        return;
                    }

                    // 手動點擊：開啟更新精靈彈窗
                    if (this.IsHandleCreated && !this.IsDisposed)
                    {
                        this.BeginInvoke(new Action(() =>
                        {
                            ShowUpdateWizardDialog(manifest, isNewer);
                        }));
                    }
                }
                catch (Exception ex)
                {
                    if (isManualClick && this.IsHandleCreated && !this.IsDisposed)
                    {
                        this.BeginInvoke(new Action(() =>
                        {
                            MessageBox.Show("檢查線上更新時發生例外: " + ex.Message, "線上更新", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        }));
                    }
                }
            });
        }

        public void ShowUpdateWizardDialog(UpdateManifest manifest, bool isNewer)
        {
            try
            {
                Form updateForm = new Form()
                {
                    Text = "🔄 馬達動力計 HMI 線上自動更新精靈 (Hot-Swap)",
                    Size = new System.Drawing.Size(640, 520),
                    StartPosition = FormStartPosition.CenterParent,
                    FormBorderStyle = FormBorderStyle.FixedDialog,
                    MaximizeBox = false,
                    MinimizeBox = false,
                    BackColor = System.Drawing.Color.FromArgb(15, 23, 42), // Slate 900
                    ForeColor = System.Drawing.Color.White,
                    Font = new System.Drawing.Font("微軟正黑體", 9.5f)
                };

                // Header Title
                Label lblTitle = new Label()
                {
                    Text = "馬達動力計測試系統 (Dynamometer HMI Pro)",
                    Font = new System.Drawing.Font("微軟正黑體", 12f, System.Drawing.FontStyle.Bold),
                    ForeColor = System.Drawing.Color.FromArgb(56, 189, 248), // Sky 400
                    Location = new System.Drawing.Point(20, 16),
                    AutoSize = true
                };
                updateForm.Controls.Add(lblTitle);

                // Versions Info
                string verInfo = string.Format("本機目前版本: v{0}       雲端最新版本: v{1} {2}",
                    APP_VERSION,
                    manifest.Version,
                    string.IsNullOrEmpty(manifest.ReleaseDate) ? "" : "(" + manifest.ReleaseDate + ")");
                Label lblVer = new Label()
                {
                    Text = verInfo,
                    Font = new System.Drawing.Font("Consolas", 10.5f, System.Drawing.FontStyle.Bold),
                    ForeColor = isNewer ? System.Drawing.Color.FromArgb(52, 211, 153) : System.Drawing.Color.FromArgb(203, 213, 225),
                    Location = new System.Drawing.Point(20, 48),
                    AutoSize = true
                };
                updateForm.Controls.Add(lblVer);

                // Status Notice Banner
                bool isKeb1PhysicallyOpen = (this.isHmiKebOpen1 && this.spKeb1 != null && this.spKeb1.IsOpen);
                bool isKeb2PhysicallyOpen = (this.isHmiKebOpen2 && this.spKeb2 != null && this.spKeb2.IsOpen);
                bool isAnyKebOpen = isKeb1PhysicallyOpen || isKeb2PhysicallyOpen;
                string bannerText;
                if (!isAnyKebOpen)
                {
                    bannerText = isNewer ?
                        "✨ 雲端伺服器已發布新版本！(KEB 變頻器未連線，無法控制馬達，允許隨時安全升級更新)" :
                        "ℹ️ 目前本機版本已是最新狀態。(KEB 變頻器未連線，可隨時重新安裝修復)";
                }
                else
                {
                    bannerText = isNewer ?
                        "✨ 雲端伺服器已發布新版本！KEB 連線中，請確認機台非處於運轉測試狀態即可點擊更新。" :
                        "ℹ️ 目前本機版本已是最新狀態。若軟體異常需要修復，仍可點擊重新安裝。";
                }

                Label lblBanner = new Label()
                {
                    Text = bannerText,
                    Font = new System.Drawing.Font("微軟正黑體", 9f),
                    ForeColor = isNewer ? System.Drawing.Color.FromArgb(253, 224, 71) : System.Drawing.Color.FromArgb(148, 163, 184),
                    Location = new System.Drawing.Point(20, 78),
                    Size = new System.Drawing.Size(590, 36)
                };
                updateForm.Controls.Add(lblBanner);

                // Release Notes Label
                Label lblNotesTitle = new Label()
                {
                    Text = "📝 更新日誌與功能說明 (Release Notes):",
                    Font = new System.Drawing.Font("微軟正黑體", 9f, System.Drawing.FontStyle.Bold),
                    ForeColor = System.Drawing.Color.FromArgb(226, 232, 240),
                    Location = new System.Drawing.Point(20, 120),
                    AutoSize = true
                };
                updateForm.Controls.Add(lblNotesTitle);

                // Release Notes TextBox
                TextBox txtNotes = new TextBox()
                {
                    Multiline = true,
                    ReadOnly = true,
                    ScrollBars = ScrollBars.Vertical,
                    Location = new System.Drawing.Point(20, 144),
                    Size = new System.Drawing.Size(585, 190),
                    BackColor = System.Drawing.Color.FromArgb(30, 41, 59), // Slate 800
                    ForeColor = System.Drawing.Color.FromArgb(241, 245, 249),
                    Font = new System.Drawing.Font("微軟正黑體", 9f),
                    Text = string.IsNullOrEmpty(manifest.Notes) ? "(本次更新無額外日誌說明)" : manifest.Notes.Replace("\n", "\r\n")
                };
                updateForm.Controls.Add(txtNotes);

                // Progress Bar
                ProgressBar pb = new ProgressBar()
                {
                    Location = new System.Drawing.Point(20, 345),
                    Size = new System.Drawing.Size(585, 22),
                    Minimum = 0,
                    Maximum = 100,
                    Value = 0
                };
                updateForm.Controls.Add(pb);

                // Progress Status Label
                Label lblStatus = new Label()
                {
                    Text = "準備就緒，點擊下方按鈕開始線上自動更新...",
                    Font = new System.Drawing.Font("微軟正黑體", 8.5f),
                    ForeColor = System.Drawing.Color.FromArgb(148, 163, 184),
                    Location = new System.Drawing.Point(20, 373),
                    Size = new System.Drawing.Size(585, 20)
                };
                updateForm.Controls.Add(lblStatus);

                // Buttons Panel
                Button btnUpdate = new Button()
                {
                    Text = isNewer ? "🚀 開始線上更新並重啟" : "🔄 強制重新安裝並重啟",
                    Location = new System.Drawing.Point(280, 410),
                    Size = new System.Drawing.Size(200, 38),
                    BackColor = System.Drawing.Color.FromArgb(16, 185, 129), // Emerald
                    ForeColor = System.Drawing.Color.White,
                    Font = new System.Drawing.Font("微軟正黑體", 10f, System.Drawing.FontStyle.Bold),
                    Cursor = Cursors.Hand,
                    FlatStyle = FlatStyle.Flat
                };
                btnUpdate.FlatAppearance.BorderSize = 0;
                updateForm.Controls.Add(btnUpdate);

                Button btnClose = new Button()
                {
                    Text = "稍後更新",
                    Location = new System.Drawing.Point(495, 410),
                    Size = new System.Drawing.Size(110, 38),
                    BackColor = System.Drawing.Color.FromArgb(71, 85, 105),
                    ForeColor = System.Drawing.Color.White,
                    Font = new System.Drawing.Font("微軟正黑體", 9.5f),
                    Cursor = Cursors.Hand,
                    FlatStyle = FlatStyle.Flat
                };
                btnClose.FlatAppearance.BorderSize = 0;
                btnClose.Click += (s, e) => updateForm.Close();
                updateForm.Controls.Add(btnClose);

                btnUpdate.Click += (s, e) =>
                {
                    // 安全防護判斷：
                    // 只有在 KEB 變頻器有連線的情況下，才需要檢查馬達/動力計是否正在運轉測試中
                    bool btnKeb1Open = (this.isHmiKebOpen1 && this.spKeb1 != null && this.spKeb1.IsOpen);
                    bool btnKeb2Open = (this.isHmiKebOpen2 && this.spKeb2 != null && this.spKeb2.IsOpen);
                    bool btnAnyKebConnected = btnKeb1Open || btnKeb2Open;
                    if (btnAnyKebConnected)
                    {
                        // 「測試中」之精確定義：
                        // 1. 自動工作制計時器啟動 (dutyTimer.Enabled)
                        // 2. 無負載溫升測試計時器啟動 (noLoadTimer.Enabled 或 isNoLoadRunning)
                        // 3. T-N 特性曲線測試計時器啟動 (tnTimer.Enabled)
                        // 4. 效率圖譜測試計時器啟動 (effMapTimer.Enabled)
                        // 5. 閉迴路或定速追隨控制啟動 (isClosedLoopTracking 或 isSpeedTracking)
                        // 6. 變頻器下達 RUN 運轉指令 (lastSy50Cmd == 4 正轉 或 12 反轉)
                        // 【致命除錯】：嚴禁檢查 this.isRunning，因為 isRunning 係底層背景採樣輪詢迴圈旗標 (Form_Load 恆為 true)，絕非馬達運轉！
                        bool isMotorRunning = (this.dutyTimer != null && this.dutyTimer.Enabled)
                            || this.isNoLoadRunning
                            || (this.noLoadTimer != null && this.noLoadTimer.Enabled)
                            || (this.tnTimer != null && this.tnTimer.Enabled)
                            || (this.effMapTimer != null && this.effMapTimer.Enabled)
                            || this.isClosedLoopTracking
                            || this.isSpeedTracking
                            || (this.lastSy50Cmd1 == 4 || this.lastSy50Cmd1 == 12)
                            || (this.lastSy50Cmd2 == 4 || this.lastSy50Cmd2 == 12);

                        if (isMotorRunning)
                        {
                            MessageBox.Show("KEB 變頻器連線中且動力計目前正在運轉測試中！\n為避免設備失控或測試中斷，請先停止測試後再執行更新。\n\n(提示：若 KEB 斷開離線未連線狀態下，因無法驅動馬達，允許直接更新)", "安全保護機制", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                            return;
                        }
                    }
                    else
                    {
                        // KEB 未連線，無法控制馬達，安全終止軟體殘留測試計時器
                        try { if (this.dutyTimer != null && this.dutyTimer.Enabled) this.dutyTimer.Stop(); } catch { }
                        try { if (this.noLoadTimer != null && this.noLoadTimer.Enabled) this.noLoadTimer.Stop(); } catch { }
                        try { if (this.tnTimer != null && this.tnTimer.Enabled) this.tnTimer.Stop(); } catch { }
                        try { if (this.effMapTimer != null && this.effMapTimer.Enabled) this.effMapTimer.Stop(); } catch { }
                        this.isNoLoadRunning = false;
                        this.isClosedLoopTracking = false;
                        this.isSpeedTracking = false;
                    }

                    btnUpdate.Enabled = false;
                    btnClose.Enabled = false;
                    lblStatus.Text = "正在透過 Wi-Fi 網卡連線下載最新主程式...";
                    lblStatus.ForeColor = System.Drawing.Color.FromArgb(56, 189, 248);

                    string currentExe = Process.GetCurrentProcess().MainModule.FileName;
                    string appDir = Path.GetDirectoryName(currentExe);
                    string newExePath = Path.Combine(appDir, "Dynamometer_HMI_Pro.new");

                    ThreadPool.QueueUserWorkItem(_ =>
                    {
                        try
                        {
                            string dlErr;
                            bool ok = DownloadBinaryPayload(manifest.DownloadUrl, newExePath, detectedWifiIp, 35000, (bytesRead, totalBytes, percent) =>
                            {
                                if (updateForm.IsHandleCreated && !updateForm.IsDisposed)
                                {
                                    updateForm.BeginInvoke(new Action(() =>
                                    {
                                        if (percent >= 0) pb.Value = Math.Max(0, Math.Min(100, percent));
                                        if (totalBytes > 0)
                                        {
                                            lblStatus.Text = string.Format("下載進度: {0:F1} MB / {1:F1} MB ({2}%)",
                                                bytesRead / 1048576.0, totalBytes / 1048576.0, percent >= 0 ? percent.ToString() : "--");
                                        }
                                        else
                                        {
                                            lblStatus.Text = string.Format("下載中: {0:F1} MB...", bytesRead / 1048576.0);
                                        }
                                    }));
                                }
                            }, out dlErr);

                            if (!ok) throw new Exception(dlErr);

                            if (updateForm.IsHandleCreated && !updateForm.IsDisposed)
                            {
                                updateForm.BeginInvoke(new Action(() =>
                                {
                                    pb.Value = 100;
                                    lblStatus.Text = "下載完成！正在驗證 Windows PE 檔案完整性...";
                                }));
                            }

                            VerifyPeHeader(newExePath);

                            if (updateForm.IsHandleCreated && !updateForm.IsDisposed)
                            {
                                updateForm.BeginInvoke(new Action(() =>
                                {
                                    lblStatus.Text = "驗證成功！正在執行熱替換並重新啟動主程式...";
                                    lblStatus.ForeColor = System.Drawing.Color.FromArgb(52, 211, 153);
                                }));
                            }

                            Thread.Sleep(800);
                            ExecuteHotSwapAndRestart(newExePath);
                        }
                        catch (Exception ex)
                        {
                            if (updateForm.IsHandleCreated && !updateForm.IsDisposed)
                            {
                                updateForm.BeginInvoke(new Action(() =>
                                {
                                    lblStatus.Text = "下載或替換失敗: " + ex.Message;
                                    lblStatus.ForeColor = System.Drawing.Color.FromArgb(239, 68, 68);
                                    btnUpdate.Enabled = true;
                                    btnClose.Enabled = true;
                                    MessageBox.Show("線上更新過程發生錯誤:\n" + ex.Message, "更新失敗", MessageBoxButtons.OK, MessageBoxIcon.Error);
                                }));
                            }
                        }
                    });
                };

                updateForm.ShowDialog(this);
            }
            catch (Exception ex)
            {
                MessageBox.Show("無法開啟線上更新視窗: " + ex.Message, "線上更新", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    } // end partial class MainForm

    /// <summary>
    /// BouncyCastle TLS 1.2 客戶端規格 (強制啟用 TLS 1.2，相容 Google Firebase RTDB)
    /// </summary>
    public class ManagedTlsClient : DefaultTlsClient
    {
        private string host;
        public ManagedTlsClient(string host) { this.host = host; }

        public override TlsAuthentication GetAuthentication()
        {
            return new ServerOnlyTlsAuthentication();
        }

        public override ProtocolVersion ClientVersion
        {
            get { return ProtocolVersion.TLSv12; }
        }

        public override ProtocolVersion MinimumVersion
        {
            get { return ProtocolVersion.TLSv12; }
        }
    }

    /// <summary>
    /// 伺服器憑證認證器 (略過驗證，確保在 XP 舊根憑證下依然能 100% 成功連線)
    /// </summary>
    public class ServerOnlyTlsAuthentication : TlsAuthentication
    {
        public void NotifyServerCertificate(Certificate serverCertificate) { }
        public TlsCredentials GetClientCredentials(CertificateRequest certificateRequest) { return null; }
    }
} // end namespace
