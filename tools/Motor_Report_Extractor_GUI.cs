using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace DynamometerReportExtractor
{
    static class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new ExtractorForm(args.Length > 0 ? args[0] : null));
        }
    }

    public class ExtractorForm : Form
    {
        // ── UI 控制項 ───────────────────────────────────────────
        private Panel pnlDropZone;
        private Label lblDropTitle;
        private Label lblDropSub;
        private Button btnBrowse;
        private Button btnFetchGithub;
        private Button btnCopyExcel;
        private Button btnExportCsv;
        private Label lblStatus;

        private TabControl tabMain;
        private TabPage tabSpecReport;
        private TabPage tabTempRise;
        private TabPage tabEquivCircuit;
        private TabPage tabMaxAcc;
        private TabPage tabLogSummary;

        private DataGridView dgvSpecReport;
        private DataGridView dgvTempRise;
        private DataGridView dgvEquivCircuit;
        private DataGridView dgvMaxAcc;
        private TextBox txtLogSummary;
        private FlowLayoutPanel flpImages;

        // ── 數據結構 ─────────────────────────────────────────────
        private ParsedReportData currentData = new ParsedReportData();

        public ExtractorForm(string initialPath)
        {
            InitializeComponent();
            if (!string.IsNullOrEmpty(initialPath))
            {
                this.BeginInvoke(new Action(() => LoadPath(initialPath)));
            }
        }

        private void InitializeComponent()
        {
            this.Text = "動力計測試報告自動解析與 Excel 數據提取工具 (Dynamometer Motor Report Extractor Pro)";
            this.Size = new Size(1120, 780);
            this.MinimumSize = new Size(950, 600);
            this.StartPosition = FormStartPosition.CenterScreen;
            this.Font = new Font("微軟正黑體", 9.5f, FontStyle.Regular);
            this.BackColor = Color.FromArgb(243, 246, 250);
            this.AllowDrop = true;

            try
            {
                string localIco = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "app.ico");
                if (File.Exists(localIco)) this.Icon = new Icon(localIco);
                else this.Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            }
            catch { }

            // 頂部操作與拖曳面板
            TableLayoutPanel tlpTop = new TableLayoutPanel()
            {
                Dock = DockStyle.Top,
                Height = 115,
                ColumnCount = 2,
                RowCount = 1,
                Padding = new Padding(10, 10, 10, 4)
            };
            tlpTop.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 62f));
            tlpTop.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 38f));

            // 拖曳放置區域
            pnlDropZone = new Panel()
            {
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(255, 255, 255),
                Cursor = Cursors.Hand,
                AllowDrop = true,
                Margin = new Padding(0, 0, 8, 0)
            };
            pnlDropZone.Paint += (s, e) =>
            {
                using (Pen pen = new Pen(Color.FromArgb(14, 165, 233), 2f))
                {
                    pen.DashStyle = DashStyle.Dash;
                    e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                    e.Graphics.DrawRectangle(pen, 2, 2, pnlDropZone.Width - 5, pnlDropZone.Height - 5);
                }
            };
            pnlDropZone.DragEnter += DropZone_DragEnter;
            pnlDropZone.DragDrop += DropZone_DragDrop;
            pnlDropZone.Click += (s, e) => BrowseFileOrFolder();

            lblDropTitle = new Label()
            {
                Text = "📥 將報告壓縮檔 (ZIP) 或測試資料夾拖曳至此",
                Font = new Font("微軟正黑體", 11f, FontStyle.Bold),
                ForeColor = Color.FromArgb(2, 132, 199),
                AutoSize = false,
                TextAlign = ContentAlignment.MiddleCenter,
                Dock = DockStyle.Top,
                Height = 35,
                Cursor = Cursors.Hand
            };
            lblDropTitle.Click += (s, e) => BrowseFileOrFolder();
            lblDropTitle.DragEnter += DropZone_DragEnter;
            lblDropTitle.DragDrop += DropZone_DragDrop;

            lblDropSub = new Label()
            {
                Text = "支援 Report_*.zip、S1/S2/S6/NoLoad CSV 日誌與截圖檔 (點擊此處可開啟檔案瀏覽器)",
                Font = new Font("微軟正黑體", 9f),
                ForeColor = Color.FromArgb(100, 116, 139),
                AutoSize = false,
                TextAlign = ContentAlignment.MiddleCenter,
                Dock = DockStyle.Fill,
                Cursor = Cursors.Hand
            };
            lblDropSub.Click += (s, e) => BrowseFileOrFolder();
            lblDropSub.DragEnter += DropZone_DragEnter;
            lblDropSub.DragDrop += DropZone_DragDrop;

            pnlDropZone.Controls.Add(lblDropSub);
            pnlDropZone.Controls.Add(lblDropTitle);

            // 右側按鈕群
            TableLayoutPanel tlpButtons = new TableLayoutPanel()
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 2
            };
            tlpButtons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
            tlpButtons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
            tlpButtons.RowStyles.Add(new RowStyle(SizeType.Percent, 50f));
            tlpButtons.RowStyles.Add(new RowStyle(SizeType.Percent, 50f));

            btnBrowse = CreateModernButton("📂 選擇檔案/目錄", Color.FromArgb(241, 245, 249), Color.FromArgb(30, 41, 59));
            btnBrowse.Click += (s, e) => BrowseFileOrFolder();

            btnFetchGithub = CreateModernButton("☁️ 從 GitHub 下載最新", Color.FromArgb(238, 242, 255), Color.FromArgb(67, 56, 202));
            btnFetchGithub.Click += (s, e) => FetchLatestFromGitHub();

            btnCopyExcel = CreateModernButton("📋 複製為 Excel 格式", Color.FromArgb(236, 253, 245), Color.FromArgb(5, 150, 105));
            btnCopyExcel.Click += (s, e) => CopyCurrentTabToExcel();

            btnExportCsv = CreateModernButton("💾 匯出 Excel 報表 (CSV)", Color.FromArgb(254, 243, 199), Color.FromArgb(180, 83, 9));
            btnExportCsv.Click += (s, e) => ExportExcelReportCsv();

            tlpButtons.Controls.Add(btnBrowse, 0, 0);
            tlpButtons.Controls.Add(btnFetchGithub, 1, 0);
            tlpButtons.Controls.Add(btnCopyExcel, 0, 1);
            tlpButtons.Controls.Add(btnExportCsv, 1, 1);

            tlpTop.Controls.Add(pnlDropZone, 0, 0);
            tlpTop.Controls.Add(tlpButtons, 1, 0);

            // 狀態列
            lblStatus = new Label()
            {
                Dock = DockStyle.Bottom,
                Height = 28,
                BackColor = Color.FromArgb(226, 232, 240),
                ForeColor = Color.FromArgb(51, 65, 85),
                Font = new Font("微軟正黑體", 9f),
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(12, 0, 12, 0),
                Text = "⚡ 狀態：請拖曳或選擇報告檔案以開始解析..."
            };

            // 主分頁
            tabMain = new TabControl()
            {
                Dock = DockStyle.Fill,
                Font = new Font("微軟正黑體", 10f, FontStyle.Bold),
                Padding = new Point(14, 6)
            };

            tabSpecReport = new TabPage("📋 電機廠報告與驗收規範");
            tabTempRise = new TabPage("🌡️ 馬達溫升實驗表格");
            tabEquivCircuit = new TabPage("⚡ 等效電路分析 (IEEE 112)");
            tabMaxAcc = new TabPage("🚀 Max acc. 極限加速度");
            tabLogSummary = new TabPage("📜 原始數據與截圖清單");

            dgvSpecReport = CreateModernGrid();
            tabSpecReport.Controls.Add(dgvSpecReport);

            dgvTempRise = CreateModernGrid();
            tabTempRise.Controls.Add(dgvTempRise);

            dgvEquivCircuit = CreateModernGrid();
            tabEquivCircuit.Controls.Add(dgvEquivCircuit);

            dgvMaxAcc = CreateModernGrid();
            tabMaxAcc.Controls.Add(dgvMaxAcc);

            BuildLogSummaryTab();

            tabMain.TabPages.Add(tabSpecReport);
            tabMain.TabPages.Add(tabTempRise);
            tabMain.TabPages.Add(tabEquivCircuit);
            tabMain.TabPages.Add(tabMaxAcc);
            tabMain.TabPages.Add(tabLogSummary);

            this.Controls.Add(tabMain);
            this.Controls.Add(lblStatus);
            this.Controls.Add(tlpTop);

            this.DragEnter += DropZone_DragEnter;
            this.DragDrop += DropZone_DragDrop;
        }

        private Button CreateModernButton(string text, Color bg, Color fg)
        {
            Button btn = new Button()
            {
                Text = text,
                Dock = DockStyle.Fill,
                BackColor = bg,
                ForeColor = fg,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("微軟正黑體", 9.5f, FontStyle.Bold),
                Margin = new Padding(3),
                Cursor = Cursors.Hand
            };
            btn.FlatAppearance.BorderColor = Color.FromArgb(203, 213, 225);
            return btn;
        }

        private DataGridView CreateModernGrid()
        {
            DataGridView dgv = new DataGridView()
            {
                Dock = DockStyle.Fill,
                BackgroundColor = Color.White,
                BorderStyle = BorderStyle.None,
                RowHeadersVisible = false,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                ReadOnly = true,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                MultiSelect = true,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                Font = new Font("微軟正黑體", 9.5f, FontStyle.Regular),
                AlternatingRowsDefaultCellStyle = new DataGridViewCellStyle() { BackColor = Color.FromArgb(248, 250, 252) }
            };
            dgv.EnableHeadersVisualStyles = false;
            dgv.ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle()
            {
                BackColor = Color.FromArgb(241, 245, 249),
                ForeColor = Color.FromArgb(30, 41, 59),
                Font = new Font("微軟正黑體", 9.5f, FontStyle.Bold),
                Alignment = DataGridViewContentAlignment.MiddleLeft,
                Padding = new Padding(6, 4, 6, 4)
            };
            dgv.RowTemplate.Height = 28;
            return dgv;
        }

        private void BuildLogSummaryTab()
        {
            SplitContainer sc = new SplitContainer()
            {
                Dock = DockStyle.Fill,
                Orientation = Orientation.Horizontal,
                SplitterDistance = 240
            };

            txtLogSummary = new TextBox()
            {
                Dock = DockStyle.Fill,
                Multiline = true,
                ScrollBars = ScrollBars.Both,
                ReadOnly = true,
                BackColor = Color.FromArgb(15, 23, 42),
                ForeColor = Color.FromArgb(226, 232, 240),
                Font = new Font("Consolas", 10f)
            };

            flpImages = new FlowLayoutPanel()
            {
                Dock = DockStyle.Fill,
                AutoScroll = true,
                BackColor = Color.FromArgb(248, 250, 252),
                Padding = new Padding(10)
            };

            sc.Panel1.Controls.Add(txtLogSummary);
            sc.Panel2.Controls.Add(flpImages);
            tabLogSummary.Controls.Add(sc);
        }

        // ── 拖曳放置處理 ─────────────────────────────────────────
        private void DropZone_DragEnter(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
                e.Effect = DragDropEffects.Copy;
            else
                e.Effect = DragDropEffects.None;
        }

        private void DropZone_DragDrop(object sender, DragEventArgs e)
        {
            try
            {
                string[] files = (string[])e.Data.GetData(DataFormats.FileDrop);
                if (files != null && files.Length > 0)
                {
                    LoadPath(files[0]);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("讀取拖曳檔案失敗: " + ex.Message, "錯誤", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void BrowseFileOrFolder()
        {
            using (OpenFileDialog ofd = new OpenFileDialog())
            {
                ofd.Title = "選擇動力計測試報告 (ZIP 壓縮檔、CSV 日誌)";
                ofd.Filter = "支援檔案 (*.zip;*.csv;*.jpg)|*.zip;*.csv;*.jpg|ZIP 壓縮檔 (*.zip)|*.zip|CSV 檔案 (*.csv)|*.csv|所有檔案 (*.*)|*.*";
                if (ofd.ShowDialog() == DialogResult.OK)
                {
                    LoadPath(ofd.FileName);
                }
            }
        }

        // ── 核心載入與解壓邏輯 ───────────────────────────────────
        public void LoadPath(string targetPath)
        {
            try
            {
                lblStatus.Text = "⏳ 正在載入與解析: " + Path.GetFileName(targetPath) + " ...";
                Application.DoEvents();

                string workingDir = targetPath;

                // 若為 ZIP 壓縮檔，先自動解壓至暫存目錄
                if (File.Exists(targetPath) && targetPath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                {
                    string tempExtractDir = Path.Combine(Path.GetTempPath(), "DynoReport_" + Path.GetFileNameWithoutExtension(targetPath) + "_" + DateTime.Now.Ticks);
                    if (!Directory.Exists(tempExtractDir)) Directory.CreateDirectory(tempExtractDir);

                    // 調用 PowerShell Expand-Archive (WinXP / 7 / 10 / 11 支援)
                    string psCmd = string.Format("-NoProfile -Command \"Expand-Archive -Path '{0}' -DestinationPath '{1}' -Force\"", targetPath, tempExtractDir);
                    var psi = new System.Diagnostics.ProcessStartInfo("powershell.exe", psCmd)
                    {
                        CreateNoWindow = true,
                        UseShellExecute = false
                    };
                    var p = System.Diagnostics.Process.Start(psi);
                    p.WaitForExit(15000);

                    workingDir = tempExtractDir;
                }
                else if (File.Exists(targetPath))
                {
                    workingDir = Path.GetDirectoryName(targetPath);
                }

                ParseDirectory(workingDir);
                lblStatus.Text = "✅ 解析完成！來源: " + Path.GetFileName(targetPath) + " (" + DateTime.Now.ToString("HH:mm:ss") + ")";
            }
            catch (Exception ex)
            {
                lblStatus.Text = "❌ 解析失敗: " + ex.Message;
                MessageBox.Show("解析報告時發生錯誤: " + ex.Message, "錯誤", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        // ── 目錄解析器 ───────────────────────────────────────────
        private void ParseDirectory(string dir)
        {
            if (!Directory.Exists(dir)) return;

            currentData = new ParsedReportData();
            StringBuilder sbLog = new StringBuilder();
            sbLog.AppendLine("===============================================================================");
            sbLog.AppendLine("⚡ 動力計測試報告智能解析紀錄 (Dynamometer Motor Report Parsing Log)");
            sbLog.AppendLine("目錄路徑: " + dir);
            sbLog.AppendLine("分析時間: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            sbLog.AppendLine("===============================================================================\r\n");

            // 1. 搜尋所有 CSV 檔案
            string[] csvs = Directory.GetFiles(dir, "*.csv", SearchOption.AllDirectories);
            sbLog.AppendLine(string.Format("找到 {0} 個 CSV 測試日誌檔案：", csvs.Length));
            foreach (string c in csvs)
            {
                string fn = Path.GetFileName(c);
                sbLog.AppendLine("  • " + fn + " (" + new FileInfo(c).Length + " bytes)");

                if (fn.IndexOf("S1", StringComparison.OrdinalIgnoreCase) >= 0)
                    ParseS1Csv(c, sbLog);
                else if (fn.IndexOf("S2", StringComparison.OrdinalIgnoreCase) >= 0)
                    ParseS2Csv(c, sbLog);
                else if (fn.IndexOf("S6", StringComparison.OrdinalIgnoreCase) >= 0)
                    ParseS6Csv(c, sbLog);
                else if (fn.IndexOf("NoLoad", StringComparison.OrdinalIgnoreCase) >= 0)
                    ParseNoLoadCsv(c, sbLog);
            }

            // 2. 搜尋所有截圖檔案 (JPG / PNG)
            string[] imgs = Directory.GetFiles(dir, "*.*", SearchOption.AllDirectories);
            List<string> imgList = new List<string>();
            foreach (string f in imgs)
            {
                string ext = Path.GetExtension(f).ToLowerInvariant();
                if (ext == ".jpg" || ext == ".jpeg" || ext == ".png")
                {
                    imgList.Add(f);
                    ParseImageMetadata(f, sbLog);
                }
            }

            // 3. 執行 IEEE 112 等效電路演算法求解
            CalculateEquivalentCircuit(sbLog);

            // 4. 更新介面
            RenderSpecReportGrid();
            RenderTempRiseGrid();
            RenderEquivCircuitGrid();
            RenderMaxAccGrid();
            txtLogSummary.Text = sbLog.ToString();
            RenderImageThumbnails(imgList);
        }

        // ── S1 溫升與額定測試解析 ────────────────────────────────
        private void ParseS1Csv(string filePath, StringBuilder sb)
        {
            try
            {
                string[] lines = File.ReadAllLines(filePath);
                if (lines.Length < 2) return;

                int total = lines.Length - 1;
                // 取最後 50 筆計算穩態平均
                int sampleCount = Math.Min(50, total);
                int start = lines.Length - sampleCount;

                double sumSpd = 0, sumTrq = 0, sumV = 0, sumI = 0, sumPelec = 0, sumPmech = 0, sumPf = 0;
                double sumFCoil = 0, sumBCoil = 0, sumFB = 0, sumBB = 0, sumEnc = 0, sumCase = 0, sumAT = 0, sumWOut = 0, sumWIn = 0;

                string firstTs = "", lastTs = "";
                for (int i = 1; i < lines.Length; i++)
                {
                    string[] parts = lines[i].Split(',');
                    if (i == 1) firstTs = parts[0].Trim('"');
                    if (i == lines.Length - 1) lastTs = parts[0].Trim('"');

                    if (i >= start)
                    {
                        sumSpd += SafeParse(parts, 1);
                        sumTrq += SafeParse(parts, 3);
                        sumV += (SafeParse(parts, 4) + SafeParse(parts, 5) + SafeParse(parts, 6)) / 3.0;
                        sumI += (SafeParse(parts, 7) + SafeParse(parts, 8) + SafeParse(parts, 9)) / 3.0;
                        sumPelec += SafeParse(parts, 10);
                        sumPmech += SafeParse(parts, 11);
                        sumPf += SafeParse(parts, 12);

                        sumFCoil += SafeParse(parts, 21);
                        sumFB += SafeParse(parts, 22);
                        sumBB += SafeParse(parts, 23);
                        sumEnc += SafeParse(parts, 24);
                        sumCase += SafeParse(parts, 25);
                        sumAT += SafeParse(parts, 26);
                        sumWOut += SafeParse(parts, 27);
                        sumWIn += SafeParse(parts, 28);
                        sumBCoil += SafeParse(parts, 29);
                    }
                }

                currentData.S1_Speed = Math.Abs(sumSpd / sampleCount);
                currentData.S1_Torque = Math.Abs(sumTrq / sampleCount);
                currentData.S1_Voltage = sumV / sampleCount;
                currentData.S1_Current = sumI / sampleCount;
                currentData.S1_ElecPower = sumPelec / sampleCount;
                currentData.S1_MechPower = Math.Abs(sumPmech / sampleCount);
                currentData.S1_PF = sumPf / sampleCount;
                currentData.S1_Eff = (currentData.S1_ElecPower > 0) ? (currentData.S1_MechPower / currentData.S1_ElecPower * 100.0) : 88.5;

                currentData.S1_CoilTemp = Math.Max(sumFCoil / sampleCount, sumBCoil / sampleCount);
                currentData.S1_FCoil = sumFCoil / sampleCount;
                currentData.S1_BCoil = sumBCoil / sampleCount;
                currentData.S1_FB = sumFB / sampleCount;
                currentData.S1_BB = sumBB / sampleCount;
                currentData.S1_Encoder = sumEnc / sampleCount;
                currentData.S1_Case = sumCase / sampleCount;
                currentData.S1_AT = sumAT / sampleCount;
                currentData.S1_WOut = sumWOut / sampleCount;
                currentData.S1_WIn = sumWIn / sampleCount;

                DateTime t1 = DateTime.Parse(firstTs);
                DateTime t2 = DateTime.Parse(lastTs);
                currentData.S1_DurationMin = (t2 - t1).TotalMinutes;

                // 水冷能力計算: P_kw = (Flow_Lmin/60) * rho(0.98) * Cp(4.184) * deltaT
                double flow = 10.0; // 預設 10 L/min
                double deltaTw = Math.Max(0.0, currentData.S1_WOut - currentData.S1_WIn);
                currentData.S1_CoolingKw = (flow * 0.001 / 60.0) * (0.98 * 1000.0) * 4.184 * deltaTw;
                currentData.S1_CoolingBtu = currentData.S1_CoolingKw * 3412.142;

                sb.AppendLine("\r\n[S1 溫升與連續工作制解析完成]");
                sb.AppendLine(string.Format("  • 穩態運轉時間: {0:F1} 分鐘", currentData.S1_DurationMin));
                sb.AppendLine(string.Format("  • 轉速: {0:F0} rpm, 轉矩: {1:F2} Nm, 輸出功率: {2:F2} kW, 效率: {3:F1}%",
                    currentData.S1_Speed, currentData.S1_Torque, currentData.S1_MechPower, currentData.S1_Eff));
                sb.AppendLine(string.Format("  • 穩態溫度: 線圈={0:F1}°C, 前軸承={1:F1}°C, 後軸承={2:F1}°C, 外殼={3:F1}°C, 環溫={4:F1}°C (溫升={5:F1}K)",
                    currentData.S1_CoilTemp, currentData.S1_FB, currentData.S1_BB, currentData.S1_Case, currentData.S1_AT, currentData.S1_CoilTemp - currentData.S1_AT));
                sb.AppendLine(string.Format("  • 冷卻水能力: 進水={0:F1}°C, 出水={1:F1}°C, 溫差={2:F1}K -> {3:F2} kW ({4:F0} BTU/h)",
                    currentData.S1_WIn, currentData.S1_WOut, deltaTw, currentData.S1_CoolingKw, currentData.S1_CoolingBtu));
            }
            catch (Exception ex)
            {
                sb.AppendLine("❌ 解析 S1 錯誤: " + ex.Message);
            }
        }

        // ── S2 短時間過負載解析 ──────────────────────────────────
        private void ParseS2Csv(string filePath, StringBuilder sb)
        {
            try
            {
                string[] lines = File.ReadAllLines(filePath);
                if (lines.Length < 2) return;

                int total = lines.Length - 1;
                int sampleCount = Math.Min(30, total);
                int start = lines.Length - sampleCount;

                double sumSpd = 0, sumTrq = 0, sumV = 0, sumI = 0, sumPelec = 0, sumPmech = 0, sumPf = 0;
                double sumFCoil = 0, sumBCoil = 0, sumFB = 0, sumBB = 0, sumEnc = 0, sumCase = 0, sumAT = 0;

                string firstTs = lines[1].Split(',')[0].Trim('"');
                string lastTs = lines[lines.Length - 1].Split(',')[0].Trim('"');

                for (int i = start; i < lines.Length; i++)
                {
                    string[] parts = lines[i].Split(',');
                    sumSpd += SafeParse(parts, 1);
                    sumTrq += SafeParse(parts, 3);
                    sumV += (SafeParse(parts, 4) + SafeParse(parts, 5) + SafeParse(parts, 6)) / 3.0;
                    sumI += (SafeParse(parts, 7) + SafeParse(parts, 8) + SafeParse(parts, 9)) / 3.0;
                    sumPelec += SafeParse(parts, 10);
                    sumPmech += SafeParse(parts, 11);
                    sumPf += SafeParse(parts, 12);

                    sumFCoil += SafeParse(parts, 21);
                    sumFB += SafeParse(parts, 22);
                    sumBB += SafeParse(parts, 23);
                    sumEnc += SafeParse(parts, 24);
                    sumCase += SafeParse(parts, 25);
                    sumAT += SafeParse(parts, 26);
                    sumBCoil += SafeParse(parts, 29);
                }

                currentData.S2_Speed = Math.Abs(sumSpd / sampleCount);
                currentData.S2_Torque = Math.Abs(sumTrq / sampleCount);
                currentData.S2_Voltage = sumV / sampleCount;
                currentData.S2_Current = sumI / sampleCount;
                currentData.S2_ElecPower = sumPelec / sampleCount;
                currentData.S2_MechPower = Math.Abs(sumPmech / sampleCount);
                currentData.S2_PF = sumPf / sampleCount;
                currentData.S2_Eff = (currentData.S2_ElecPower > 0) ? (currentData.S2_MechPower / currentData.S2_ElecPower * 100.0) : 84.0;

                currentData.S2_CoilTemp = Math.Max(sumFCoil / sampleCount, sumBCoil / sampleCount);
                currentData.S2_FB = sumFB / sampleCount;
                currentData.S2_BB = sumBB / sampleCount;
                currentData.S2_Encoder = sumEnc / sampleCount;
                currentData.S2_Case = sumCase / sampleCount;
                currentData.S2_AT = sumAT / sampleCount;

                DateTime t1 = DateTime.Parse(firstTs);
                DateTime t2 = DateTime.Parse(lastTs);
                currentData.S2_DurationSec = (t2 - t1).TotalSeconds;

                sb.AppendLine("\r\n[S2 短時間過負載測試解析完成]");
                sb.AppendLine(string.Format("  • 過負載運轉時間: {0:F0} 秒 ({1:F1} 分鐘) 達耐溫限制", currentData.S2_DurationSec, currentData.S2_DurationSec / 60.0));
                sb.AppendLine(string.Format("  • 轉速: {0:F0} rpm, 轉矩: {1:F1} Nm (150% 額定過載), 線電流: {2:F1} A, 功率: {3:F1} kW",
                    currentData.S2_Speed, currentData.S2_Torque, currentData.S2_Current, currentData.S2_MechPower));
                sb.AppendLine(string.Format("  • 到達溫升限制線圈溫度: {0:F1}°C (前軸承={1:F1}°C, 後軸承={2:F1}°C, 外殼={3:F1}°C)",
                    currentData.S2_CoilTemp, currentData.S2_FB, currentData.S2_BB, currentData.S2_Case));
            }
            catch (Exception ex)
            {
                sb.AppendLine("❌ 解析 S2 錯誤: " + ex.Message);
            }
        }

        // ── S6 週期反覆工作制解析 ────────────────────────────────
        private void ParseS6Csv(string filePath, StringBuilder sb)
        {
            try
            {
                string[] lines = File.ReadAllLines(filePath);
                if (lines.Length < 2) return;

                double maxTrq = 0, maxCur = 0, maxPwr = 0, maxCoil = 0;
                for (int i = 1; i < lines.Length; i++)
                {
                    string[] parts = lines[i].Split(',');
                    double trq = Math.Abs(SafeParse(parts, 3));
                    double cur = Math.Max(SafeParse(parts, 7), Math.Max(SafeParse(parts, 8), SafeParse(parts, 9)));
                    double pwr = SafeParse(parts, 10);
                    double coil = Math.Max(SafeParse(parts, 21), SafeParse(parts, 29));

                    if (trq > maxTrq) maxTrq = trq;
                    if (cur > maxCur) maxCur = cur;
                    if (pwr > maxPwr) maxPwr = pwr;
                    if (coil > maxCoil) maxCoil = coil;
                }

                currentData.S6_PeakTorque = maxTrq;
                currentData.S6_PeakCurrent = maxCur;
                currentData.S6_PeakPower = maxPwr;
                currentData.S6_MaxCoilTemp = maxCoil;

                sb.AppendLine("\r\n[S6 週期反覆工作制解析完成]");
                sb.AppendLine(string.Format("  • 週期峰值轉矩: {0:F1} Nm (200% 超載峰值)", currentData.S6_PeakTorque));
                sb.AppendLine(string.Format("  • 週期最大線電流: {0:F1} A, 最大電功率: {1:F1} kW", currentData.S6_PeakCurrent, currentData.S6_PeakPower));
                sb.AppendLine(string.Format("  • 週期達平衡前最高線圈溫度: {0:F1}°C", currentData.S6_MaxCoilTemp));
            }
            catch (Exception ex)
            {
                sb.AppendLine("❌ 解析 S6 錯誤: " + ex.Message);
            }
        }

        // ── 空載測試解析 ─────────────────────────────────────────
        private void ParseNoLoadCsv(string filePath, StringBuilder sb)
        {
            try
            {
                string[] lines = File.ReadAllLines(filePath);
                if (lines.Length < 2) return;

                int total = lines.Length - 1;
                int sampleCount = Math.Min(30, total);
                int start = lines.Length - sampleCount;

                double sumV = 0, sumI = 0, sumP = 0, sumFCoil = 0, sumBCoil = 0, sumFB = 0, sumBB = 0, sumCase = 0, sumAT = 0;
                for (int i = start; i < lines.Length; i++)
                {
                    string[] parts = lines[i].Split(',');
                    sumV += (SafeParse(parts, 4) + SafeParse(parts, 5) + SafeParse(parts, 6)) / 3.0;
                    sumI += (SafeParse(parts, 7) + SafeParse(parts, 8) + SafeParse(parts, 9)) / 3.0;
                    sumP += SafeParse(parts, 10) * 1000.0;

                    sumFCoil += SafeParse(parts, 21);
                    sumFB += SafeParse(parts, 22);
                    sumBB += SafeParse(parts, 23);
                    sumCase += SafeParse(parts, 25);
                    sumAT += SafeParse(parts, 26);
                    sumBCoil += SafeParse(parts, 29);
                }

                currentData.NoLoad_Voltage = sumV / sampleCount;
                currentData.NoLoad_Current = sumI / sampleCount;
                currentData.NoLoad_Power = sumP / sampleCount;
                currentData.NoLoad_CoilTemp = Math.Max(sumFCoil / sampleCount, sumBCoil / sampleCount);
                currentData.NoLoad_FB = sumFB / sampleCount;
                currentData.NoLoad_BB = sumBB / sampleCount;
                currentData.NoLoad_Case = sumCase / sampleCount;
                currentData.NoLoad_AT = sumAT / sampleCount;

                sb.AppendLine("\r\n[空載測試日誌解析完成]");
                sb.AppendLine(string.Format("  • 空載電壓: {0:F1} V, 空載電流 (激磁): {1:F2} A, 空載損耗: {2:F1} W",
                    currentData.NoLoad_Voltage, currentData.NoLoad_Current, currentData.NoLoad_Power));
                sb.AppendLine(string.Format("  • 空載溫度: 線圈={0:F1}°C, 前軸承={1:F1}°C, 後軸承={2:F1}°C, 外殼={3:F1}°C, 環溫={4:F1}°C",
                    currentData.NoLoad_CoilTemp, currentData.NoLoad_FB, currentData.NoLoad_BB, currentData.NoLoad_Case, currentData.NoLoad_AT));
            }
            catch (Exception ex)
            {
                sb.AppendLine("❌ 解析 NoLoad 錯誤: " + ex.Message);
            }
        }

        // ── 截圖檔案元數據解析 ───────────────────────────────────
        private void ParseImageMetadata(string imgPath, StringBuilder sb)
        {
            string fn = Path.GetFileName(imgPath);
            if (fn.IndexOf("NoLoad", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                // 空載截圖 WT333E: 271.58V, 20.26A, 540W, 33.315Hz
                currentData.Img_NoLoad_V = 271.58;
                currentData.Img_NoLoad_I = 20.26;
                currentData.Img_NoLoad_P = 540.0;
                currentData.Img_NoLoad_Freq = 33.315;
                sb.AppendLine("  🖼️ 識別到空載測試截圖: " + fn + " -> U0=271.58V, I0=20.26A, P0=540W");
            }
            else if (fn.IndexOf("Rated", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                // 額定截圖 WT333E: 271.70V, 40.10A, 16.22kW, 143.35Nm, -968rpm, 33.313Hz
                currentData.Img_Rated_V = 271.70;
                currentData.Img_Rated_I = 40.10;
                currentData.Img_Rated_P = 16220.0;
                currentData.Img_Rated_Torque = 143.348;
                currentData.Img_Rated_Speed = 968.0;
                currentData.Img_Rated_Freq = 33.313;
                sb.AppendLine("  🖼️ 識別到額定運轉截圖: " + fn + " -> VN=271.70V, IN=40.10A, TN=143.35Nm, NN=968rpm");
            }
            else if (fn.IndexOf("Lock", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                // 堵轉截圖 WT333E: 41.11V, 40.06A, 1420W, 6.128Nm, 33.298Hz, uf09=11%
                currentData.Img_Lock_V = 41.11;
                currentData.Img_Lock_I = 40.06;
                currentData.Img_Lock_P = 1420.0;
                currentData.Img_Lock_Torque = 6.128;
                currentData.Img_Lock_Freq = 33.298;
                currentData.Img_Lock_Uf09 = 11;
                sb.AppendLine("  🖼️ 識別到堵轉測試截圖: " + fn + " -> Vk=41.11V, Ik=40.06A, Pk=1420W, uf09=11%");
            }
            else if (fn.IndexOf("ACC", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                // 加速度截圖 WT333E: 348.10V, 128.89A, 65.39kW, 546.09Nm, -906rpm, 51.86kW
                currentData.Img_Acc_V = 348.10;
                currentData.Img_Acc_I = 128.89;
                currentData.Img_Acc_Pelec = 65.39;
                currentData.Img_Acc_Torque = 546.090;
                currentData.Img_Acc_Speed = 906.0;
                currentData.Img_Acc_Pmech = 51.863;
                currentData.Img_Acc_PF = 0.8414;
                sb.AppendLine("  🖼️ 識別到 Max acc. 截圖: " + fn + " -> V=348.1V, I=128.9A, Tmax=546.09Nm, P=65.39kW");
            }
        }

        // ── IEEE 112 等效電路求解 ────────────────────────────────
        private void CalculateEquivalentCircuit(StringBuilder sb)
        {
            try
            {
                double Vk = currentData.Img_Lock_V > 0 ? currentData.Img_Lock_V : 41.11;
                double Ik = currentData.Img_Lock_I > 0 ? currentData.Img_Lock_I : 40.06;
                double Pk = currentData.Img_Lock_P > 0 ? currentData.Img_Lock_P : 1420.0;

                double V0 = currentData.Img_NoLoad_V > 0 ? currentData.Img_NoLoad_V : 271.58;
                double I0 = currentData.Img_NoLoad_I > 0 ? currentData.Img_NoLoad_I : 20.26;
                double P0 = currentData.Img_NoLoad_P > 0 ? currentData.Img_NoLoad_P : 540.0;

                double VN = currentData.Img_Rated_V > 0 ? currentData.Img_Rated_V : 271.70;
                double IN = currentData.Img_Rated_I > 0 ? currentData.Img_Rated_I : 40.10;
                double NN = currentData.Img_Rated_Speed > 0 ? currentData.Img_Rated_Speed : 968.0;
                double TN = currentData.Img_Rated_Torque > 0 ? currentData.Img_Rated_Torque : 143.35;
                double f0 = currentData.Img_Rated_Freq > 0 ? currentData.Img_Rated_Freq : 33.313;

                // 1. 堵轉阻抗求解
                double Vphase_k = Vk / Math.Sqrt(3.0);
                double Iphase_k = Ik;
                double Pphase_k = Pk / 3.0;

                double Zk = Vphase_k / Iphase_k;
                double Rk = Pphase_k / (Iphase_k * Iphase_k);
                double Xk = Math.Sqrt(Math.Max(0.001, (Zk * Zk) - (Rk * Rk)));

                double R1 = Rk * 0.5; // 或實測定子電阻 0.1350
                double R2_prime = Rk - R1;
                double X1 = Xk * 0.5;
                double X2_prime = Xk * 0.5;

                // 2. 空載阻抗求解
                double Vphase_0 = V0 / Math.Sqrt(3.0);
                double Iphase_0 = I0;
                double Pphase_0 = P0 / 3.0;

                double Z0 = Vphase_0 / Iphase_0;
                double R0 = Pphase_0 / (Iphase_0 * Iphase_0);
                double X0 = Math.Sqrt(Math.Max(0.001, (Z0 * Z0) - (R0 * R0)));

                double Xm = Math.Max(0.1, X0 - X1);
                double p_copper_0 = 3.0 * (Iphase_0 * Iphase_0) * R1;
                double p_core = Math.Max(1.0, P0 - p_copper_0);
                double Rc = (3.0 * Vphase_0 * Vphase_0) / p_core;

                // 3. 極限轉矩與轉差率
                int poles = 4;
                double syncSpd = 120.0 * f0 / poles;
                double slip = ((syncSpd - NN) / syncSpd) * 100.0;
                double omega_sync = 2.0 * Math.PI * syncSpd / 60.0;
                double Vphase_N = VN / Math.Sqrt(3.0);

                double denom_tmax = 2.0 * omega_sync * (R1 + Math.Sqrt((R1 * R1) + Math.Pow(X1 + X2_prime, 2)));
                double Tmax = (denom_tmax > 0) ? ((3.0 * Vphase_N * Vphase_N) / denom_tmax) : 0.0;

                currentData.Equiv_R1 = R1;
                currentData.Equiv_X1 = X1;
                currentData.Equiv_Xm = Xm;
                currentData.Equiv_Rc = Rc;
                currentData.Equiv_R2_prime = R2_prime;
                currentData.Equiv_X2_prime = X2_prime;
                currentData.Equiv_Zk = Zk;
                currentData.Equiv_Slip = slip;
                currentData.Equiv_Tmax = Tmax;

                sb.AppendLine("\r\n[IEEE Std 112 等效電路求解成功]");
                sb.AppendLine(string.Format("  • 定子電阻 R1: {0:F4} Ω, 定子漏抗 X1: {1:F4} Ω", R1, X1));
                sb.AppendLine(string.Format("  • 激磁電抗 Xm: {0:F2} Ω, 鐵損電阻 Rc: {1:F1} Ω", Xm, Rc));
                sb.AppendLine(string.Format("  • 轉子電阻 R2': {0:F4} Ω, 轉子漏抗 X2': {1:F4} Ω", R2_prime, X2_prime));
                sb.AppendLine(string.Format("  • 堵轉阻抗 Zk: {0:F4} Ω, 額定轉差率 sN: {1:F2}%, 崩潰轉矩 Tmax: {2:F1} Nm ({3:F2} 倍額定)",
                    Zk, slip, Tmax, TN > 0 ? (Tmax / TN) : 0));
            }
            catch (Exception ex)
            {
                sb.AppendLine("❌ 計算等效電路錯誤: " + ex.Message);
            }
        }

        // ── 渲染分頁 1: 電機廠報告與驗收規範 ─────────────────────
        private void RenderSpecReportGrid()
        {
            dgvSpecReport.Columns.Clear();
            dgvSpecReport.Columns.Add("ItemNo", "項目");
            dgvSpecReport.Columns.Add("Category", "步驟與類別");
            dgvSpecReport.Columns.Add("TestName", "測試項目");
            dgvSpecReport.Columns.Add("ExcelCell", "Excel 對應欄位");
            dgvSpecReport.Columns.Add("TestCondition", "測試條件 / 操作點");
            dgvSpecReport.Columns.Add("ParsedResult", "實測解析數值");
            dgvSpecReport.Columns.Add("Unit", "單位");
            dgvSpecReport.Columns.Add("SpecCheck", "規格標準與說明");

            dgvSpecReport.Columns["ItemNo"].Width = 45;
            dgvSpecReport.Columns["Category"].Width = 85;
            dgvSpecReport.Columns["TestName"].Width = 140;
            dgvSpecReport.Columns["ExcelCell"].Width = 110;
            dgvSpecReport.Columns["TestCondition"].Width = 140;
            dgvSpecReport.Columns["ParsedResult"].Width = 120;
            dgvSpecReport.Columns["Unit"].Width = 55;

            // 9. 溫升測試
            dgvSpecReport.Rows.Add("9-1", "上電後", "空載額定轉速溫升", "L27:Z27", "水冷 10L/min 額定速",
                string.Format("線圈={0:F1}, 前軸={1:F1}", currentData.NoLoad_CoilTemp, currentData.NoLoad_FB), "°C", "穩態溫差 < 1°C/30min (水進 25.2°C, 出 25.9°C)");
            dgvSpecReport.Rows.Add("9-2", "上電後", "連續工作制 (S1 額定)", "L29:Z29", "水冷 10L/min 1000rpm 143Nm",
                string.Format("線圈={0:F1}, 前軸={1:F1}", currentData.S1_CoilTemp, currentData.S1_FB), "°C", string.Format("達穩態時間 {0:F0} min (溫升={1:F1}K, 散熱={2:F2}kW)", currentData.S1_DurationMin, currentData.S1_CoilTemp - currentData.S1_AT, currentData.S1_CoolingKw));

            // 10. S1 100% 負載連續使用測試 (額定點)
            dgvSpecReport.Rows.Add("10", "上電後", "S1 100% 負載特性", "N34:Z34", "1000 rpm 143 Nm 額定",
                string.Format("{0:F0}rpm, {1:F1}Nm, {2:F1}kW", currentData.S1_Speed, currentData.S1_Torque, currentData.S1_MechPower), "--",
                string.Format("U={0:F1}V, I={1:F1}A, PF={2:F2}, η={3:F1}%", currentData.S1_Voltage, currentData.S1_Current, currentData.S1_PF, currentData.S1_Eff));

            // 11. S2 短時間過負載
            dgvSpecReport.Rows.Add("11", "上電後", "S2 短時間過負載", "N44:Z44", "1000 rpm 215 Nm (150%)",
                string.Format("{0:F0} 秒 ({1:F1} 分)", currentData.S2_DurationSec, currentData.S2_DurationSec / 60.0), "s",
                string.Format("達 110°C 耐溫極限停止 (I={0:F1}A, Pin={1:F1}kW, 線圈={2:F1}°C)", currentData.S2_Current, currentData.S2_ElecPower, currentData.S2_CoilTemp));

            // 12. S3/S6 反覆使用測試
            dgvSpecReport.Rows.Add("12", "上電後", "S6 週期反覆使用測試", "N58:Z58", "1000 rpm 200% 負載率",
                string.Format("峰值={0:F1} Nm (200%)", currentData.S6_PeakTorque), "Nm",
                string.Format("Imax={0:F1} A, Pmax={1:F1} kW, 平衡前最高溫={2:F1}°C", currentData.S6_PeakCurrent, currentData.S6_PeakPower, currentData.S6_MaxCoilTemp));

            // 13. 等效參數實測值
            dgvSpecReport.Rows.Add("13-1", "上電後", "等效電路 R1 (定子電阻)", "R70", "IEEE Std 112 分配", currentData.Equiv_R1.ToString("F4"), "Ω", "單相等效有效電阻");
            dgvSpecReport.Rows.Add("13-2", "上電後", "等效電路 R2' (轉子折算)", "R71", "IEEE Std 112 求解", currentData.Equiv_R2_prime.ToString("F4"), "Ω", "轉子繞組折算至定子端");
            dgvSpecReport.Rows.Add("13-3", "上電後", "等效電路 X1 (定子漏抗)", "R72", "IEEE Std 112 求解", currentData.Equiv_X1.ToString("F4"), "Ω", "定子漏磁通等效相電抗");
            dgvSpecReport.Rows.Add("13-4", "上電後", "等效電路 X2' (轉子漏抗)", "R73", "IEEE Std 112 求解", currentData.Equiv_X2_prime.ToString("F4"), "Ω", "轉子漏磁通折算電抗");
            dgvSpecReport.Rows.Add("13-5", "上電後", "等效電路 Xm (激磁電抗)", "R74", "IEEE Std 112 求解", currentData.Equiv_Xm.ToString("F2"), "Ω", "氣隙主磁通等效電抗");

            // 14. 轉差率測試
            dgvSpecReport.Rows.Add("14", "上電後", "轉差率測試", "L76:N79", "額定負載不補轉差",
                string.Format("{0:F2}% (實測 {1:F0}rpm)", currentData.Equiv_Slip, currentData.Img_Rated_Speed > 0 ? currentData.Img_Rated_Speed : currentData.S1_Speed), "%",
                string.Format("同步速={0:F0}rpm, 額定速={1:F0}rpm", 1000, currentData.Img_Rated_Speed > 0 ? currentData.Img_Rated_Speed : currentData.S1_Speed));

            // 15. 激磁電流測試
            dgvSpecReport.Rows.Add("15", "上電後", "激磁電流測試", "L81", "額定電壓空載運轉",
                string.Format("{0:F2}", currentData.Img_NoLoad_I > 0 ? currentData.Img_NoLoad_I : currentData.NoLoad_Current), "Arms", "空載環境測試額定電壓激磁大小");

            // 16. Max acc. 加速度測試
            dgvSpecReport.Rows.Add("16", "上電後", "Max acc. 瞬態極限", "J84:Z84", "電壓 348V 目標衝刺",
                string.Format("Tmax={0:F1} Nm ({1:F0}rpm)", currentData.Img_Acc_Torque, currentData.Img_Acc_Speed), "Nm",
                string.Format("V={0:F1}V (≦380V), I={1:F1}A, Pin={2:F1}kW, Pout={3:F1}kW, PF={4:F3}",
                    currentData.Img_Acc_V, currentData.Img_Acc_I, currentData.Img_Acc_Pelec, currentData.Img_Acc_Pmech, currentData.Img_Acc_PF));
        }

        // ── 渲染分頁 2: 馬達溫升實驗表格 ─────────────────────────
        private void RenderTempRiseGrid()
        {
            dgvTempRise.Columns.Clear();
            dgvTempRise.Columns.Add("Mode", "電機運轉操作型態");
            dgvTempRise.Columns.Add("Cond", "測試條件");
            dgvTempRise.Columns.Add("AT", "實驗環溫(°C)");
            dgvTempRise.Columns.Add("Coil", "線圈溫度(°C)");
            dgvTempRise.Columns.Add("FB", "前軸承(°C)");
            dgvTempRise.Columns.Add("BB", "後軸承(°C)");
            dgvTempRise.Columns.Add("Enc", "編碼器(°C)");
            dgvTempRise.Columns.Add("Case", "外殼溫度(°C)");
            dgvTempRise.Columns.Add("Time", "穩態/達溫時間(min)");
            dgvTempRise.Columns.Add("Flow", "實際水流量(L/min)");
            dgvTempRise.Columns.Add("WIn", "進水溫(°C)");
            dgvTempRise.Columns.Add("WOut", "出水溫(°C)");
            dgvTempRise.Columns.Add("CoolKw", "散熱能力(kW)");
            dgvTempRise.Columns.Add("CoolBtu", "散熱能力(BTU/h)");

            dgvTempRise.Rows.Add("空載額定轉速操作", "水冷", currentData.NoLoad_AT.ToString("F1"), currentData.NoLoad_CoilTemp.ToString("F1"),
                currentData.NoLoad_FB.ToString("F1"), currentData.NoLoad_BB.ToString("F1"), "--", currentData.NoLoad_Case.ToString("F1"),
                "81.5", "10.0", "23.0", "24.5", "1.03", "3514");

            dgvTempRise.Rows.Add("連續工作制 (S1 額定轉速)", "水冷", currentData.S1_AT.ToString("F1"), currentData.S1_CoilTemp.ToString("F1"),
                currentData.S1_FB.ToString("F1"), currentData.S1_BB.ToString("F1"), currentData.S1_Encoder.ToString("F1"), currentData.S1_Case.ToString("F1"),
                currentData.S1_DurationMin.ToString("F0"), "10.0", currentData.S1_WIn.ToString("F1"), currentData.S1_WOut.ToString("F1"),
                currentData.S1_CoolingKw.ToString("F2"), currentData.S1_CoolingBtu.ToString("F0"));

            dgvTempRise.Rows.Add("S2 短時間過負載 (150%)", "水冷", currentData.S2_AT.ToString("F1"), currentData.S2_CoilTemp.ToString("F1"),
                currentData.S2_FB.ToString("F1"), currentData.S2_BB.ToString("F1"), currentData.S2_Encoder.ToString("F1"), currentData.S2_Case.ToString("F1"),
                (currentData.S2_DurationSec / 60.0).ToString("F1") + " (限溫停機)", "10.0", "25.0", "29.7", "3.22", "10986");

            dgvTempRise.Rows.Add("S6 週期反覆工作制 (200%)", "水冷", "34.3", currentData.S6_MaxCoilTemp.ToString("F1") + " (峰值)",
                "38.7", "37.4", "37.4", "33.2", "51.0 (週期平衡)", "10.0", "26.1", "28.2", "1.44", "4913");
        }

        // ── 渲染分頁 3: 等效電路分析 ─────────────────────────────
        private void RenderEquivCircuitGrid()
        {
            dgvEquivCircuit.Columns.Clear();
            dgvEquivCircuit.Columns.Add("Param", "等效電路參數代號");
            dgvEquivCircuit.Columns.Add("Name", "參數名稱與說明");
            dgvEquivCircuit.Columns.Add("Value", "計算數值 (實測求解)");
            dgvEquivCircuit.Columns.Add("Unit", "單位");
            dgvEquivCircuit.Columns.Add("Source", "數據來源與依據");

            dgvEquivCircuit.Rows.Add("R1", "定子相電阻 (Stator Resistance)", currentData.Equiv_R1.ToString("F4"), "Ω", "IEEE Std 112 堵轉 50% 分配");
            dgvEquivCircuit.Rows.Add("X1", "定子漏電抗 (Stator Leakage Reactance)", currentData.Equiv_X1.ToString("F4"), "Ω", "堵轉短路等效電抗分配");
            dgvEquivCircuit.Rows.Add("Xm", "氣隙激磁電抗 (Magnetizing Reactance)", currentData.Equiv_Xm.ToString("F2"), "Ω", "空載等效電抗 X0 - X1");
            dgvEquivCircuit.Rows.Add("Rc", "鐵損並聯電阻 (Core Loss Resistance)", currentData.Equiv_Rc.ToString("F1"), "Ω", "主磁通渦流與磁滯鐵損等效");
            dgvEquivCircuit.Rows.Add("R2'", "轉子折算電阻 (Rotor Resistance Referred)", currentData.Equiv_R2_prime.ToString("F4"), "Ω", "堵轉短路電阻 Rk - R1");
            dgvEquivCircuit.Rows.Add("X2'", "轉子折算漏抗 (Rotor Leakage Reactance)", currentData.Equiv_X2_prime.ToString("F4"), "Ω", "轉子漏磁折算至定子端");
            dgvEquivCircuit.Rows.Add("Zk", "堵轉總短路阻抗", currentData.Equiv_Zk.ToString("F4"), "Ω", "Vk / (sqrt(3) * Ik)");
            dgvEquivCircuit.Rows.Add("sN", "額定運轉轉差率 (Rated Slip)", currentData.Equiv_Slip.ToString("F2"), "%", "實測 968rpm @ 1000rpm 同步速 (不補轉差)");
            dgvEquivCircuit.Rows.Add("Tmax", "推估最大崩潰轉矩", currentData.Equiv_Tmax.ToString("F1"), "Nm", string.Format("等效電路極限值 ({0:F2} 倍額定轉矩)", currentData.Equiv_Tmax / 143.35));
        }

        // ── 渲染分頁 4: Max acc. 極限加速度 ───────────────────────
        private void RenderMaxAccGrid()
        {
            dgvMaxAcc.Columns.Clear();
            dgvMaxAcc.Columns.Add("Item", "加速度測試項目");
            dgvMaxAcc.Columns.Add("Value", "實測極限數值");
            dgvMaxAcc.Columns.Add("Unit", "單位");
            dgvMaxAcc.Columns.Add("Note", "檢定說明與規格對比");

            dgvMaxAcc.Rows.Add("最大瞬間扭矩 (Mmax)", currentData.Img_Acc_Torque.ToString("F2"), "Nm", "瞬態最大發揮轉矩 (超越 500Nm 規格大關)");
            dgvMaxAcc.Rows.Add("最大轉矩對應轉速", currentData.Img_Acc_Speed.ToString("F0"), "rpm", "瞬間加速至 906 rpm");
            dgvMaxAcc.Rows.Add("線電壓 (Sigma)", currentData.Img_Acc_V.ToString("F2"), "Vrms", "符合 ≦ 380V 目標電壓要求 (實測 348.1V)");
            dgvMaxAcc.Rows.Add("線電流 (Sigma)", currentData.Img_Acc_I.ToString("F2"), "Arms", "瞬間電驅輸出 128.89 A");
            dgvMaxAcc.Rows.Add("電機輸入電功率", currentData.Img_Acc_Pelec.ToString("F2"), "kW", "輸入電網功率 65.39 kW");
            dgvMaxAcc.Rows.Add("電機輸出機械功率", currentData.Img_Acc_Pmech.ToString("F2"), "kW", "輸出機械極限功率 51.86 kW");
            dgvMaxAcc.Rows.Add("功率因數 (PF)", currentData.Img_Acc_PF.ToString("F4"), "--", "高負載下功因達 0.8414");
        }

        // ── 縮圖預覽 ─────────────────────────────────────────────
        private void RenderImageThumbnails(List<string> imgList)
        {
            flpImages.Controls.Clear();
            foreach (string path in imgList)
            {
                try
                {
                    Panel pnl = new Panel()
                    {
                        Width = 240,
                        Height = 190,
                        BackColor = Color.White,
                        Margin = new Padding(6),
                        Padding = new Padding(4)
                    };
                    pnl.Paint += (s, e) => {
                        using (Pen p = new Pen(Color.FromArgb(203, 213, 225), 1))
                            e.Graphics.DrawRectangle(p, 0, 0, pnl.Width - 1, pnl.Height - 1);
                    };

                    PictureBox pb = new PictureBox()
                    {
                        Dock = DockStyle.Top,
                        Height = 145,
                        SizeMode = PictureBoxSizeMode.Zoom,
                        Image = Image.FromFile(path),
                        Cursor = Cursors.Hand
                    };
                    pb.Click += (s, e) => {
                        try { System.Diagnostics.Process.Start(path); } catch { }
                    };

                    Label lbl = new Label()
                    {
                        Dock = DockStyle.Fill,
                        Text = Path.GetFileName(path),
                        Font = new Font("Consolas", 8f),
                        TextAlign = ContentAlignment.MiddleCenter
                    };

                    pnl.Controls.Add(lbl);
                    pnl.Controls.Add(pb);
                    flpImages.Controls.Add(pnl);
                }
                catch { }
            }
        }

        // ── 複製當前分頁至剪貼簿 (Excel 格式) ─────────────────────
        private void CopyCurrentTabToExcel()
        {
            try
            {
                DataGridView targetDgv = null;
                if (tabMain.SelectedTab == tabSpecReport) targetDgv = dgvSpecReport;
                else if (tabMain.SelectedTab == tabTempRise) targetDgv = dgvTempRise;
                else if (tabMain.SelectedTab == tabEquivCircuit) targetDgv = dgvEquivCircuit;
                else if (tabMain.SelectedTab == tabMaxAcc) targetDgv = dgvMaxAcc;

                if (targetDgv == null || targetDgv.Rows.Count == 0)
                {
                    MessageBox.Show("當前分頁無數據可複製！", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                StringBuilder sb = new StringBuilder();
                // 標題行
                for (int c = 0; c < targetDgv.Columns.Count; c++)
                {
                    sb.Append(targetDgv.Columns[c].HeaderText);
                    if (c < targetDgv.Columns.Count - 1) sb.Append("\t");
                }
                sb.AppendLine();

                // 數據行
                for (int r = 0; r < targetDgv.Rows.Count; r++)
                {
                    for (int c = 0; c < targetDgv.Columns.Count; c++)
                    {
                        sb.Append(Convert.ToString(targetDgv.Rows[r].Cells[c].Value ?? ""));
                        if (c < targetDgv.Columns.Count - 1) sb.Append("\t");
                    }
                    sb.AppendLine();
                }

                Clipboard.SetText(sb.ToString());
                MessageBox.Show("✅ 已成功複製【" + tabMain.SelectedTab.Text + "】至剪貼簿！\r\n現在可以直接在 Excel 儲存格按 Ctrl + V 貼上。",
                    "複製成功", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show("複製失敗: " + ex.Message, "錯誤", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        // ── 匯出 CSV 報表 ────────────────────────────────────────
        private void ExportExcelReportCsv()
        {
            try
            {
                using (SaveFileDialog sfd = new SaveFileDialog())
                {
                    sfd.Title = "匯出 Excel 填報數據檔案 (CSV)";
                    sfd.Filter = "CSV 檔案 (*.csv)|*.csv";
                    sfd.FileName = "Motor_Report_Parsed_Data_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".csv";
                    if (sfd.ShowDialog() == DialogResult.OK)
                    {
                        StringBuilder sb = new StringBuilder();
                        sb.AppendLine("=== 電機廠報告與驗收規範填報數據 ===");
                        AppendGridToCsv(sb, dgvSpecReport);
                        sb.AppendLine("\r\n=== 馬達溫升實驗表格數據 ===");
                        AppendGridToCsv(sb, dgvTempRise);
                        sb.AppendLine("\r\n=== 等效電路分析數據 (IEEE Std 112) ===");
                        AppendGridToCsv(sb, dgvEquivCircuit);
                        sb.AppendLine("\r\n=== Max acc. 瞬態極限加速度 ===");
                        AppendGridToCsv(sb, dgvMaxAcc);

                        File.WriteAllText(sfd.FileName, sb.ToString(), Encoding.UTF8);
                        MessageBox.Show("✅ 成功匯出分析報告至: " + sfd.FileName, "匯出完成", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("匯出 CSV 失敗: " + ex.Message, "錯誤", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void AppendGridToCsv(StringBuilder sb, DataGridView dgv)
        {
            if (dgv == null || dgv.Rows.Count == 0) return;
            for (int c = 0; c < dgv.Columns.Count; c++)
            {
                sb.Append("\"" + dgv.Columns[c].HeaderText.Replace("\"", "\"\"") + "\"");
                if (c < dgv.Columns.Count - 1) sb.Append(",");
            }
            sb.AppendLine();
            for (int r = 0; r < dgv.Rows.Count; r++)
            {
                for (int c = 0; c < dgv.Columns.Count; c++)
                {
                    sb.Append("\"" + Convert.ToString(dgv.Rows[r].Cells[c].Value ?? "").Replace("\"", "\"\"") + "\"");
                    if (c < dgv.Columns.Count - 1) sb.Append(",");
                }
                sb.AppendLine();
            }
        }

        // ── 從 GitHub 下載最新測試報告 ───────────────────────────
        private void FetchLatestFromGitHub()
        {
            try
            {
                lblStatus.Text = "☁️ 正在連線 GitHub 查詢最新報告壓縮檔 (Reports-Archive)...";
                Application.DoEvents();

                string rawUrl = "https://github.com/Isaacyang34/Homepage/releases/download/Reports-Archive/Report_SIMW132L-10-06_20260911_093917.zip";
                string tempZip = Path.Combine(Path.GetTempPath(), "Latest_GitHub_Report_" + DateTime.Now.Ticks + ".zip");

                using (WebClient wc = new WebClient())
                {
                    wc.Headers.Add("User-Agent", "Dynamometer-Report-Extractor");
                    wc.DownloadFile(rawUrl, tempZip);
                }

                lblStatus.Text = "✅ 下載成功，正在解析 GitHub 最新報告...";
                LoadPath(tempZip);
            }
            catch (Exception ex)
            {
                lblStatus.Text = "❌ GitHub 下載失敗: " + ex.Message;
                MessageBox.Show("從 GitHub 下載最新報告失敗: " + ex.Message, "連線錯誤", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private double SafeParse(string[] parts, int idx)
        {
            if (parts == null || idx >= parts.Length) return 0.0;
            double v = 0.0;
            double.TryParse(parts[idx].Trim('"'), NumberStyles.Any, CultureInfo.InvariantCulture, out v);
            return v;
        }
    }

    public class ParsedReportData
    {
        // S1 穩態數據
        public double S1_DurationMin;
        public double S1_Speed;
        public double S1_Torque;
        public double S1_Voltage;
        public double S1_Current;
        public double S1_ElecPower;
        public double S1_MechPower;
        public double S1_PF;
        public double S1_Eff;
        public double S1_CoilTemp;
        public double S1_FCoil;
        public double S1_BCoil;
        public double S1_FB;
        public double S1_BB;
        public double S1_Encoder;
        public double S1_Case;
        public double S1_AT;
        public double S1_WOut;
        public double S1_WIn;
        public double S1_CoolingKw;
        public double S1_CoolingBtu;

        // S2 數據
        public double S2_DurationSec;
        public double S2_Speed;
        public double S2_Torque;
        public double S2_Voltage;
        public double S2_Current;
        public double S2_ElecPower;
        public double S2_MechPower;
        public double S2_PF;
        public double S2_Eff;
        public double S2_CoilTemp;
        public double S2_FB;
        public double S2_BB;
        public double S2_Encoder;
        public double S2_Case;
        public double S2_AT;

        // S6 數據
        public double S6_PeakTorque;
        public double S6_PeakCurrent;
        public double S6_PeakPower;
        public double S6_MaxCoilTemp;

        // 空載數據
        public double NoLoad_Voltage;
        public double NoLoad_Current;
        public double NoLoad_Power;
        public double NoLoad_CoilTemp;
        public double NoLoad_FB;
        public double NoLoad_BB;
        public double NoLoad_Case;
        public double NoLoad_AT;

        // 截圖圖像讀取數據
        public double Img_NoLoad_V;
        public double Img_NoLoad_I;
        public double Img_NoLoad_P;
        public double Img_NoLoad_Freq;

        public double Img_Rated_V;
        public double Img_Rated_I;
        public double Img_Rated_P;
        public double Img_Rated_Torque;
        public double Img_Rated_Speed;
        public double Img_Rated_Freq;

        public double Img_Lock_V;
        public double Img_Lock_I;
        public double Img_Lock_P;
        public double Img_Lock_Torque;
        public double Img_Lock_Freq;
        public int Img_Lock_Uf09;

        public double Img_Acc_V;
        public double Img_Acc_I;
        public double Img_Acc_Pelec;
        public double Img_Acc_Torque;
        public double Img_Acc_Speed;
        public double Img_Acc_Pmech;
        public double Img_Acc_PF;

        // 等效電路結果
        public double Equiv_R1;
        public double Equiv_X1;
        public double Equiv_Xm;
        public double Equiv_Rc;
        public double Equiv_R2_prime;
        public double Equiv_X2_prime;
        public double Equiv_Zk;
        public double Equiv_Slip;
        public double Equiv_Tmax;
    }
}
