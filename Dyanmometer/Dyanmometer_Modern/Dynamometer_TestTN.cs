using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;

namespace DynamometerHMI
{
    public partial class MainForm : Form
    {
        // =========================================================================
        // 分頁 2: 多段 T-N 曲線自動測試 (採用 TableLayoutPanel 100% 杜絕遮擋)
        // =========================================================================
        // =========================================================================
        private void BuildTnTab(TabPage tab)
        {
            tab.Controls.Clear();

            // 外層上下分割：上方為參數設定區，下方為圖表與數據表 (可自由上下拖曳)
            splitTnMain = new SplitContainer()
            {
                Dock = DockStyle.Fill,
                Orientation = Orientation.Horizontal,
                SplitterWidth = 8,
                BackColor = Color.FromArgb(203, 213, 225)
            };
            splitTnMain.Panel1.AutoScroll = true;
            splitTnMain.Panel1.BackColor = Color.FromArgb(248, 250, 252);
            splitTnMain.Panel2.AutoScroll = true;
            splitTnMain.Panel2.BackColor = Color.White;
            SafeSetupSplitContainer(splitTnMain, "TnMain", 210, 100, 100);

            // 頂部參數設定區
            Panel pnlTop = new Panel() { Dock = DockStyle.Fill, Padding = new Padding(6), BackColor = Color.FromArgb(248, 250, 252) };
            GroupBox grp = new GroupBox()
            {
                Text = "多段 T-N 曲線自動測試 (含手動定錨與梯度自適應繼承)",
                Dock = DockStyle.Fill,
                Font = new Font("微軟正黑體", 14f, FontStyle.Bold),
                BackColor = Color.FromArgb(248, 250, 252)
            };

            // 第零列：測試模式、測試配置與測試標記 (Y = 28)
            Label lTnMode = new Label() { Text = "測試模式:", Location = new Point(12, 32), AutoSize = true, Font = new Font("微軟正黑體", 13f, FontStyle.Bold) };
            cmbTnMode = new ComboBox() { Location = new Point(105, 28), Width = 230, DropDownStyle = ComboBoxStyle.DropDownList, Font = new Font("微軟正黑體", 12f) };
            cmbTnMode.Items.AddRange(new object[] { "等間距梯度掃描 (原模式)", "多點自訂轉速扭力測試" });
            cmbTnMode.SelectedIndex = 0; // 預設等間距
            cmbTnMode.SelectedIndexChanged += (s, e) => {
                UpdateTnModeVisibility(cmbTnMode.SelectedIndex);
            };

            Label lRole = new Label() { Text = "測試配置:", Location = new Point(350, 32), AutoSize = true, Font = new Font("微軟正黑體", 13f, FontStyle.Bold) };
            cmbTnRole = new ComboBox() { Location = new Point(435, 28), Width = 235, DropDownStyle = ComboBoxStyle.DropDownList, Font = new Font("微軟正黑體", 12f) };
            cmbTnRole.Items.AddRange(new object[] { "A載台待測(速度) / B載台加載", "B載台待測(速度) / A載台加載" });
            cmbTnRole.SelectedIndex = 1; // 預設：B載台待測(速度) / A載台加載
            cmbTnRole.SelectedIndexChanged += (s, e) => {
                if (!isSyncingTnControls && cmbTnRoleMini != null && cmbTnRoleMini.SelectedIndex != cmbTnRole.SelectedIndex)
                {
                    isSyncingTnControls = true;
                    cmbTnRoleMini.SelectedIndex = cmbTnRole.SelectedIndex;
                    isSyncingTnControls = false;
                }
            };

            Label lTnTag = new Label()
            {
                Text = "測試標記:",
                Location = new Point(685, 32),
                AutoSize = true,
                Font = new Font("微軟正黑體", 12f, FontStyle.Bold)
            };
            txtTnTag = new TextBox()
            {
                Text = (txtTnMiniTag != null && !string.IsNullOrEmpty(txtTnMiniTag.Text)) ? txtTnMiniTag.Text : "TN",
                Location = new Point(770, 28),
                Size = new Size(110, 30),
                Font = new Font("微軟正黑體", 12f)
            };
            txtTnTag.TextChanged += (s, e) => {
                if (!isSyncingTnControls && txtTnMiniTag != null && txtTnMiniTag.Text != txtTnTag.Text)
                {
                    isSyncingTnControls = true;
                    txtTnMiniTag.Text = txtTnTag.Text;
                    isSyncingTnControls = false;
                }
            };

            // =========================================================================
            // 面板 A：等間距梯度掃描模式專屬面板 (pnlTnStepRamp)
            // =========================================================================
            pnlTnStepRamp = new Panel()
            {
                Location = new Point(6, 68),
                Size = new Size(1400, 82),
                BackColor = Color.FromArgb(248, 250, 252)
            };

            Label l1 = new Label() { Text = "起始(rpm):", Location = new Point(6, 6), AutoSize = true, Font = new Font("微軟正黑體", 12f, FontStyle.Bold) };
            numTnStartRpm = CreateNumericUpDown(new Point(95, 2), 85, 0, 4000, 50); // 預設 50 rpm
            numTnStartRpm.Font = new Font("微軟正黑體", 12f);
            numTnStartRpm.ValueChanged += (s, e) => {
                if (!isSyncingTnControls && numTnMiniStart != null && numTnMiniStart.Value != numTnStartRpm.Value)
                {
                    isSyncingTnControls = true;
                    numTnMiniStart.Value = numTnStartRpm.Value;
                    isSyncingTnControls = false;
                }
            };

            Label l2 = new Label() { Text = "步階(rpm):", Location = new Point(195, 6), AutoSize = true, Font = new Font("微軟正黑體", 12f, FontStyle.Bold) };
            numTnStepRpm = CreateNumericUpDown(new Point(285, 2), 85, 1, 1000, 50); // 預設 50 rpm
            numTnStepRpm.Font = new Font("微軟正黑體", 12f);
            numTnStepRpm.ValueChanged += (s, e) => {
                if (!isSyncingTnControls && numTnMiniStep != null && numTnMiniStep.Value != numTnStepRpm.Value)
                {
                    isSyncingTnControls = true;
                    numTnMiniStep.Value = numTnStepRpm.Value;
                    isSyncingTnControls = false;
                }
            };

            Label l3 = new Label() { Text = "結束(rpm):", Location = new Point(385, 6), AutoSize = true, Font = new Font("微軟正黑體", 12f, FontStyle.Bold) };
            numTnEndRpm = CreateNumericUpDown(new Point(475, 2), 90, 0, 4000, 300); // 預設 300 rpm
            numTnEndRpm.Font = new Font("微軟正黑體", 12f);
            numTnEndRpm.ValueChanged += (s, e) => {
                if (!isSyncingTnControls && numTnMiniEnd != null && numTnMiniEnd.Value != numTnEndRpm.Value)
                {
                    isSyncingTnControls = true;
                    numTnMiniEnd.Value = numTnEndRpm.Value;
                    isSyncingTnControls = false;
                }
            };

            Label l4 = new Label() { Text = "目標轉矩(Nm):", Location = new Point(580, 6), AutoSize = true, Font = new Font("微軟正黑體", 12f, FontStyle.Bold) };
            numTnTorque = CreateNumericUpDown(new Point(700, 2), 85, 0, 500, 15, 1); // 預設 15.0 Nm
            numTnTorque.Font = new Font("微軟正黑體", 12f);
            numTnTorque.ValueChanged += (s, e) => {
                if (!isSyncingTnControls && numTnMiniTrq != null && numTnMiniTrq.Value != numTnTorque.Value)
                {
                    isSyncingTnControls = true;
                    numTnMiniTrq.Value = numTnTorque.Value;
                    isSyncingTnControls = false;
                }
            };

            Label l5 = new Label() { Text = "穩定時間(s):", Location = new Point(800, 6), AutoSize = true, Font = new Font("微軟正黑體", 12f, FontStyle.Bold) };
            numTnDwell = CreateNumericUpDown(new Point(905, 2), 80, 1, 3600, 16);
            numTnDwell.Font = new Font("微軟正黑體", 12f);
            numTnDwell.ValueChanged += (s, e) => {
                if (!isSyncingTnControls && numTnMiniDwell != null && numTnMiniDwell.Value != numTnDwell.Value)
                {
                    isSyncingTnControls = true;
                    numTnMiniDwell.Value = numTnDwell.Value;
                    isSyncingTnControls = false;
                }
                if (numTnOverCurrentDelay != null)
                {
                    decimal halfDwell = Math.Max(1m, Math.Round(numTnDwell.Value / 2m));
                    if (numTnOverCurrentDelay.Value != halfDwell)
                        numTnOverCurrentDelay.Value = halfDwell;
                }
            };

            btnTnAnchor = new Button()
            {
                Text = "📍 鎖定當前負載為定錨基準點",
                Location = new Point(6, 42),
                Size = new Size(245, 34),
                BackColor = Color.FromArgb(139, 92, 246),
                ForeColor = Color.White,
                Font = new Font("微軟正黑體", 11f, FontStyle.Bold),
                Cursor = Cursors.Hand
            };
            btnTnAnchor.Click += (s, e) => {
                int trqDriveId = (cmbTnRole != null && cmbTnRole.SelectedIndex == 1) ? 1 : 2;
                double curPct = (trqDriveId == 1 && numHmiKebTorque1 != null) ? (double)numHmiKebTorque1.Value : (numHmiKebTorque2 != null ? (double)numHmiKebTorque2.Value : 0.0);
                tnAdaptedTorquePct = Math.Max(0.0, curPct);
                tnHasAnchor = true;
                UpdateTnAnchorStatusText();
                if (txtHmiKebLog != null) txtHmiKebLog.AppendText(string.Format("[TN定錨] 已記憶負載基準點：{0:F1}% ({1:F2} Nm)\r\n", tnAdaptedTorquePct, actTorque));
            };
            lblTnAnchorStatus = new Label()
            {
                Text = "定錨基準: 未設定 (未定錨，從 0% 起步加載)",
                Location = new Point(260, 48),
                AutoSize = true,
                ForeColor = Color.FromArgb(100, 116, 139),
                Font = new Font("微軟正黑體", 11f, FontStyle.Bold)
            };

            Label lblTnOcTitle = new Label() { Text = "⚡過電流(Kt換算):", Location = new Point(660, 48), AutoSize = true, Font = new Font("微軟正黑體", 11.5f, FontStyle.Bold), ForeColor = Color.FromArgb(194, 65, 12) };
            Label lblTnKt = new Label() { Text = "Kt:", Location = new Point(815, 48), AutoSize = true, Font = new Font("微軟正黑體", 11f, FontStyle.Bold) };
            numTnKt = CreateNumericUpDown(new Point(845, 44), 65, 0.01m, 100m, tnMotorKt, 2, 0.1m);
            numTnKt.Font = new Font("微軟正黑體", 11f);
            numTnKt.ValueChanged += (s, e) => { tnMotorKt = numTnKt.Value; };

            Label lblTnOcPct = new Label() { Text = "門檻:", Location = new Point(920, 48), AutoSize = true, Font = new Font("微軟正黑體", 11f, FontStyle.Bold) };
            numTnOverCurrentPct = CreateNumericUpDown(new Point(965, 44), 55, 5, 100, tnOverCurrentPercent, 0, 5);
            numTnOverCurrentPct.Font = new Font("微軟正黑體", 11f);
            numTnOverCurrentPct.ValueChanged += (s, e) => { tnOverCurrentPercent = numTnOverCurrentPct.Value; };

            Label lblTnOcDelay = new Label() { Text = "持續:", Location = new Point(1030, 48), AutoSize = true, Font = new Font("微軟正黑體", 11f, FontStyle.Bold) };
            numTnOverCurrentDelay = CreateNumericUpDown(new Point(1075, 44), 55, 1, 60, tnOverCurrentDelaySec, 0, 1);
            numTnOverCurrentDelay.Font = new Font("微軟正黑體", 11f);
            numTnOverCurrentDelay.ValueChanged += (s, e) => { tnOverCurrentDelaySec = numTnOverCurrentDelay.Value; };
            Label lblTnOcDelayUnit = new Label() { Text = "s", Location = new Point(1135, 48), AutoSize = true, Font = new Font("微軟正黑體", 11f, FontStyle.Bold) };

            pnlTnStepRamp.Controls.AddRange(new Control[] {
                l1, numTnStartRpm, l2, numTnStepRpm, l3, numTnEndRpm, l4, numTnTorque, l5, numTnDwell,
                btnTnAnchor, lblTnAnchorStatus,
                lblTnOcTitle, lblTnKt, numTnKt, lblTnOcPct, numTnOverCurrentPct, lblTnOcDelay, numTnOverCurrentDelay, lblTnOcDelayUnit
            });

            // =========================================================================
            // 面板 B：多點自訂轉速扭力測試專屬面板 (pnlTnMultiPoint)
            // =========================================================================
            pnlTnMultiPoint = new Panel()
            {
                Location = new Point(6, 68),
                Size = new Size(1400, 185),
                BackColor = Color.FromArgb(248, 250, 252),
                Visible = false
            };

            Label lblTnMultiHint = new Label()
            {
                Text = "📌 多點自訂模式：達標穩定 5 秒 ➔ 擷取 30 秒 (每秒 1 筆) ➔ 換項前先降轉矩至 25% 再變速",
                Location = new Point(6, 6),
                AutoSize = true,
                Font = new Font("微軟正黑體", 11.5f, FontStyle.Bold),
                ForeColor = Color.FromArgb(30, 64, 175)
            };

            btnTnAddPoint = new Button()
            {
                Text = "➕ 新增測試點",
                Location = new Point(780, 2),
                Size = new Size(110, 30),
                Font = new Font("微軟正黑體", 10.5f, FontStyle.Bold),
                BackColor = Color.FromArgb(37, 99, 235),
                ForeColor = Color.White,
                Cursor = Cursors.Hand
            };
            btnTnRemovePoint = new Button()
            {
                Text = "➖ 刪除選取點",
                Location = new Point(898, 2),
                Size = new Size(110, 30),
                Font = new Font("微軟正黑體", 10.5f, FontStyle.Bold),
                BackColor = Color.FromArgb(220, 38, 38),
                ForeColor = Color.White,
                Cursor = Cursors.Hand
            };
            btnTnResetPoints = new Button()
            {
                Text = "🔄 重置預設點",
                Location = new Point(1016, 2),
                Size = new Size(110, 30),
                Font = new Font("微軟正黑體", 10.5f, FontStyle.Bold),
                BackColor = Color.FromArgb(100, 116, 139),
                ForeColor = Color.White,
                Cursor = Cursors.Hand
            };

            dgvTnMultiPoints = new DataGridView()
            {
                Location = new Point(6, 36),
                Size = new Size(1120, 142),
                Font = new Font("微軟正黑體", 10f),
                BackgroundColor = Color.White,
                AllowUserToAddRows = false,
                RowHeadersVisible = false,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None
            };
            dgvTnMultiPoints.Columns.Add("Idx", "點位");
            dgvTnMultiPoints.Columns["Idx"].Width = 45;
            dgvTnMultiPoints.Columns["Idx"].ReadOnly = true;

            dgvTnMultiPoints.Columns.Add("TgtSpd", "目標轉速 (rpm)");
            dgvTnMultiPoints.Columns.Add("TgtTrq", "目標轉矩 (Nm)");

            dgvTnMultiPoints.Columns.Add("Status", "執行狀態");
            dgvTnMultiPoints.Columns["Status"].Width = 140;
            dgvTnMultiPoints.Columns["Status"].ReadOnly = true;

            dgvTnMultiPoints.Columns.Add("AvgSpd", "30s均轉速(rpm)");
            dgvTnMultiPoints.Columns["AvgSpd"].ReadOnly = true;

            dgvTnMultiPoints.Columns.Add("AvgTrq", "30s均轉矩(Nm)");
            dgvTnMultiPoints.Columns["AvgTrq"].ReadOnly = true;

            dgvTnMultiPoints.Columns.Add("AvgPwr", "30s均功率(kW)");
            dgvTnMultiPoints.Columns["AvgPwr"].ReadOnly = true;

            dgvTnMultiPoints.Columns.Add("AvgEff", "30s均效率(%)");
            dgvTnMultiPoints.Columns["AvgEff"].ReadOnly = true;
            dgvTnMultiPoints.ColumnWidthChanged += (s, e) => SaveLayoutConfig();

            // 預設兩組測試點 (1500 rpm / 10.0 Nm 與 3000 rpm / 15.0 Nm)
            dgvTnMultiPoints.Rows.Add("1", "1500", "10.0", "待命", "-", "-", "-", "-");
            dgvTnMultiPoints.Rows.Add("2", "3000", "15.0", "待命", "-", "-", "-", "-");

            btnTnAddPoint.Click += (s, e) => {
                int next = dgvTnMultiPoints.Rows.Count + 1;
                dgvTnMultiPoints.Rows.Add(next.ToString(), "1500", "10.0", "待命", "-", "-", "-", "-");
            };
            btnTnRemovePoint.Click += (s, e) => {
                if (dgvTnMultiPoints.Rows.Count <= 1)
                {
                    MessageBox.Show("至少需保留一個測試點位！", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                int selIdx = dgvTnMultiPoints.CurrentRow != null ? dgvTnMultiPoints.CurrentRow.Index : dgvTnMultiPoints.Rows.Count - 1;
                if (selIdx >= 0 && selIdx < dgvTnMultiPoints.Rows.Count)
                {
                    dgvTnMultiPoints.Rows.RemoveAt(selIdx);
                    for (int i = 0; i < dgvTnMultiPoints.Rows.Count; i++)
                    {
                        dgvTnMultiPoints.Rows[i].Cells[0].Value = (i + 1).ToString();
                    }
                }
            };
            btnTnResetPoints.Click += (s, e) => {
                dgvTnMultiPoints.Rows.Clear();
                dgvTnMultiPoints.Rows.Add("1", "1500", "10.0", "待命", "-", "-", "-", "-");
                dgvTnMultiPoints.Rows.Add("2", "3000", "15.0", "待命", "-", "-", "-", "-");
            };

            pnlTnMultiPoint.Controls.AddRange(new Control[] {
                lblTnMultiHint, btnTnAddPoint, btnTnRemovePoint, btnTnResetPoints, dgvTnMultiPoints
            });

            // =========================================================================
            // 面板 C：啟動、停止、匯出與進度狀態 (pnlTnActions，隨模式自適應垂直定位)
            // =========================================================================
            // 底部操作按鈕群 (單一彈性面板，位置依模式動態下移)
            pnlTnActions = new Panel()
            {
                Location = new Point(6, 155),
                Size = new Size(1400, 45),
                BackColor = Color.FromArgb(248, 250, 252)
            };

            btnStartTn = new Button()
            {
                Text = "開始 T-N 測試",
                Location = new Point(6, 3),
                Size = new Size(150, 38),
                BackColor = Color.FromArgb(0, 180, 216),
                ForeColor = Color.White,
                Font = new Font("微軟正黑體", 13f, FontStyle.Bold),
                Cursor = Cursors.Hand
            };
            btnStopTn = new Button()
            {
                Text = "停止",
                Location = new Point(165, 3),
                Size = new Size(90, 38),
                Enabled = false,
                Font = new Font("微軟正黑體", 13f, FontStyle.Bold)
            };
            btnExportTn = new Button()
            {
                Text = "匯出報表",
                Location = new Point(265, 3),
                Size = new Size(120, 38),
                BackColor = Color.FromArgb(16, 185, 129),
                ForeColor = Color.White,
                Font = new Font("微軟正黑體", 12f, FontStyle.Bold),
                Cursor = Cursors.Hand
            };

            lblTnStatus = new Label() { Text = "狀態: 待命準備中", Location = new Point(400, 12), AutoSize = true, Font = new Font("微軟正黑體", 13f, FontStyle.Bold) };
            lblTnCountdown = new Label() { Text = "倒數: -- s", Location = new Point(780, 12), AutoSize = true, ForeColor = Color.DarkOrange, Font = new Font("微軟正黑體", 13f, FontStyle.Bold) };
            prgTn = new ProgressBar() { Location = new Point(920, 12), Size = new Size(220, 24) };

            btnStartTn.Click += BtnStartTn_Click;
            btnStopTn.Click += (s, e) => { StopTnTest(); };
            btnExportTn.Click += BtnExportTn_Click;

            pnlTnActions.Controls.AddRange(new Control[] {
                btnStartTn, btnStopTn, btnExportTn, lblTnStatus, lblTnCountdown, prgTn
            });

            grp.Controls.AddRange(new Control[] {
                lTnMode, cmbTnMode, lRole, cmbTnRole, lTnTag, txtTnTag,
                pnlTnStepRamp, pnlTnMultiPoint, pnlTnActions
            });
            pnlTop.Controls.Add(grp);
            splitTnMain.Panel1.Controls.Add(pnlTop);

            // 下方區域左右分割：左邊繪製 T-N 曲線圖，右邊顯示數據表格 (可自由左右拖曳調整)
            splitTnBottom = new SplitContainer()
            {
                Dock = DockStyle.Fill,
                Orientation = Orientation.Vertical,
                SplitterWidth = 8,
                BackColor = Color.FromArgb(203, 213, 225)
            };
            SafeSetupSplitContainer(splitTnBottom, "TnBottom", 650, 150, 150);

            // 左側：T-N 曲線圖
            tnChart = new TnCurveChart() { Dock = DockStyle.Fill };
            splitTnBottom.Panel1.Controls.Add(tnChart);

            // 右側：上方即時動態溫度曲線 + 下方數據表格 (垂直分割，可自由拖曳調整高低)
            splitTnRight = new SplitContainer()
            {
                Dock = DockStyle.Fill,
                Orientation = Orientation.Horizontal,
                SplitterWidth = 8,
                BackColor = Color.FromArgb(203, 213, 225)
            };
            SafeSetupSplitContainer(splitTnRight, "TnRight", 280, 120, 120);

            // 右上方：專屬即時溫度動態曲線與通道選擇 (同 S1 介面規格)
            grpTnTemp = new GroupBox()
            {
                Text = "🌡️ 專屬溫度監控與即時動態曲線",
                Dock = DockStyle.Fill,
                Font = new Font("微軟正黑體", 11.5f, FontStyle.Bold),
                BackColor = Color.FromArgb(248, 250, 252)
            };

            Panel pnlTnTempHeader = new Panel()
            {
                Dock = DockStyle.Top,
                Height = 40,
                BackColor = Color.FromArgb(241, 245, 249),
                Padding = new Padding(4)
            };

            btnTnSelectChannels = new Button()
            {
                Text = "⚙ 選擇監測通道...",
                Location = new Point(6, 4),
                Size = new Size(150, 30),
                BackColor = Color.FromArgb(241, 245, 249),
                Font = new Font("微軟正黑體", 10.5f, FontStyle.Bold),
                Cursor = Cursors.Hand
            };
            btnTnSelectChannels.Click += (s, e) => { ShowTnChannelSelectDialog(); };

            lblTnSelectedChHint = new Label()
            {
                Text = "(已選 CH1~4，共 4 通道)",
                Location = new Point(165, 10),
                AutoSize = true,
                ForeColor = Color.FromArgb(70, 80, 95),
                Font = new Font("微軟正黑體", 10.5f)
            };

            lblTnTempRealtimeVal = new Label()
            {
                Text = "實測最高: --.- ℃",
                Location = new Point(360, 9),
                AutoSize = true,
                Font = new Font("微軟正黑體", 11.5f, FontStyle.Bold),
                ForeColor = Color.FromArgb(30, 64, 175)
            };

            // 時間軸快捷控制列 (固定靠右對齊)
            Panel pnlTnTimeSpan = new Panel()
            {
                Dock = DockStyle.Right,
                Width = 244,
                BackColor = Color.Transparent
            };
            Label lblTnTimeTitle = new Label()
            {
                Text = "⏱️ 時間軸:",
                Location = new Point(0, 8),
                AutoSize = true,
                Font = new Font("微軟正黑體", 10f, FontStyle.Bold),
                ForeColor = Color.FromArgb(51, 65, 85)
            };
            Button btnTnTimeMinus = new Button()
            {
                Text = "➖",
                Location = new Point(68, 5),
                Size = new Size(26, 28),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.White,
                Font = new Font("微軟正黑體", 8.5f),
                Cursor = Cursors.Hand
            };
            btnTnTimeMinus.FlatAppearance.BorderColor = Color.FromArgb(203, 213, 225);

            cmbTnTimeSpan = new ComboBox()
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Location = new Point(96, 7),
                Size = new Size(114, 26),
                Font = new Font("微軟正黑體", 9.5f, FontStyle.Bold),
                BackColor = Color.White
            };
            cmbTnTimeSpan.Items.AddRange(GbdTemperatureTrendControl.TimeSpanNames);
            cmbTnTimeSpan.SelectedIndex = (sharedTestTempTrend != null) ? sharedTestTempTrend.CurrentTimeSpanIndex : 3;

            Button btnTnTimePlus = new Button()
            {
                Text = "➕",
                Location = new Point(212, 5),
                Size = new Size(26, 28),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.White,
                Font = new Font("微軟正黑體", 8.5f),
                Cursor = Cursors.Hand
            };
            btnTnTimePlus.FlatAppearance.BorderColor = Color.FromArgb(203, 213, 225);

            cmbTnTimeSpan.SelectedIndexChanged += (s, e) => {
                if (sharedTestTempTrend != null && cmbTnTimeSpan.SelectedIndex >= 0)
                {
                    sharedTestTempTrend.CurrentTimeSpanIndex = cmbTnTimeSpan.SelectedIndex;
                }
            };
            btnTnTimeMinus.Click += (s, e) => {
                if (cmbTnTimeSpan.SelectedIndex > 0) cmbTnTimeSpan.SelectedIndex--;
            };
            btnTnTimePlus.Click += (s, e) => {
                if (cmbTnTimeSpan.SelectedIndex < cmbTnTimeSpan.Items.Count - 1) cmbTnTimeSpan.SelectedIndex++;
            };

            pnlTnTimeSpan.Controls.AddRange(new Control[] { lblTnTimeTitle, btnTnTimeMinus, cmbTnTimeSpan, btnTnTimePlus });
            pnlTnTempHeader.Controls.Add(pnlTnTimeSpan);
            pnlTnTempHeader.Controls.AddRange(new Control[] { btnTnSelectChannels, lblTnSelectedChHint, lblTnTempRealtimeVal });

            // 專屬動態溫度曲線：與 Duty / 空載 共用 sharedTestTempTrend
            if (sharedTestTempTrend == null)
            {
                sharedTestTempTrend = new GbdTemperatureTrendControl(this) { Dock = DockStyle.Fill };
            }
            if (tnMonitoredChannels != null) sharedTestTempTrend.SetChannelVisibility(tnMonitoredChannels);

            grpTnTemp.Controls.Add(sharedTestTempTrend); // Fill
            sharedTestTempTrend.SendToBack();
            grpTnTemp.Controls.Add(pnlTnTempHeader);     // Top

            splitTnRight.Panel1.Controls.Add(grpTnTemp);

            // 右下方：數據表格
            dgvTnPoints = new DataGridView()
            {
                Dock = DockStyle.Fill,
                BackgroundColor = Color.White,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None,
                RowHeadersVisible = false,
                AllowUserToAddRows = false,
                Font = new Font("微軟正黑體", 10.5f)
            };
            dgvTnPoints.Columns.Add("Idx", "點位");
            dgvTnPoints.Columns.Add("TgtRpm", "目標(rpm)");
            dgvTnPoints.Columns.Add("Speed", "實測(rpm)");
            dgvTnPoints.Columns.Add("Torque", "轉矩(Nm)");
            dgvTnPoints.Columns.Add("Pwr", "功率(kW)");
            dgvTnPoints.Columns.Add("Eff", "效率(%)");
            dgvTnPoints.Columns.Add("Status", "狀態");
            dgvTnPoints.ColumnWidthChanged += (s, e) => SaveLayoutConfig();
            splitTnRight.Panel2.Controls.Add(dgvTnPoints);

            splitTnBottom.Panel2.Controls.Add(splitTnRight);

            splitTnMain.Panel2.Controls.Add(splitTnBottom);
            tab.Controls.Add(splitTnMain);

            tnTimer = new System.Windows.Forms.Timer();
            tnTimer.Interval = 1000;
            tnTimer.Tick += TnTimer_Tick;
        }

        private void UpdateTnModeVisibility(int mode)
        {
            bool isMulti = (mode == 1);
            if (pnlTnStepRamp != null) pnlTnStepRamp.Visible = !isMulti;
            if (pnlTnMultiPoint != null) pnlTnMultiPoint.Visible = isMulti;
            if (pnlTnActions != null)
            {
                pnlTnActions.Location = isMulti ? new Point(6, 255) : new Point(6, 155);
            }
            if (splitTnMain != null && splitTnMain.Height > 0)
            {
                string key = isMulti ? "TnMainMulti" : "TnMain";
                int defaultTarget = isMulti ? 325 : 210;
                int target = defaultTarget;
                int saved;
                if (layoutSplitters.TryGetValue(key, out saved) && saved > 0)
                {
                    target = saved;
                }
                ApplySplitterDistanceSafe(splitTnMain, key, target, 80, 80);
                UpdateTabHudStatus();
            }
        }

        private void UpdateTnAnchorStatusText()
        {
            string statusStr = tnHasAnchor
                ? string.Format("✅ 已定錨基準：給定 {0:F1}% (實測: {1:F2} Nm)", tnAdaptedTorquePct, actTorque)
                : "定錨基準: 未設定 (未定錨，從 0% 起步加載)";
            Color col = tnHasAnchor ? Color.DarkGreen : Color.FromArgb(100, 116, 139);
            if (lblTnAnchorStatus != null)
            {
                lblTnAnchorStatus.Text = statusStr;
                lblTnAnchorStatus.ForeColor = col;
            }
            if (lblTnMiniAnchorStatus != null)
            {
                lblTnMiniAnchorStatus.Text = statusStr;
                lblTnMiniAnchorStatus.ForeColor = col;
            }
        }

        private void SyncAllTnControls(bool fromMiniToMain)
        {
            if (isSyncingTnControls) return;
            try
            {
                isSyncingTnControls = true;
                try { this.ValidateChildren(); } catch { }

                if (fromMiniToMain)
                {
                    if (cmbTnMode != null && cmbTnModeMini != null && cmbTnModeMini.SelectedIndex >= 0)
                        cmbTnMode.SelectedIndex = cmbTnModeMini.SelectedIndex;
                    if (cmbTnRole != null && cmbTnRoleMini != null && cmbTnRoleMini.SelectedIndex >= 0)
                        cmbTnRole.SelectedIndex = cmbTnRoleMini.SelectedIndex;
                    if (numTnStartRpm != null && numTnMiniStart != null)
                        numTnStartRpm.Value = numTnMiniStart.Value;
                    if (numTnStepRpm != null && numTnMiniStep != null)
                        numTnStepRpm.Value = numTnMiniStep.Value;
                    if (numTnEndRpm != null && numTnMiniEnd != null)
                        numTnEndRpm.Value = numTnMiniEnd.Value;
                    if (numTnTorque != null && numTnMiniTrq != null)
                        numTnTorque.Value = numTnMiniTrq.Value;
                    if (numTnDwell != null && numTnMiniDwell != null)
                        numTnDwell.Value = numTnMiniDwell.Value;
                    if (txtTnTag != null && txtTnMiniTag != null && txtTnTag.Text != txtTnMiniTag.Text)
                        txtTnTag.Text = txtTnMiniTag.Text;
                }
                else
                {
                    if (cmbTnModeMini != null && cmbTnMode != null && cmbTnMode.SelectedIndex >= 0)
                        cmbTnModeMini.SelectedIndex = cmbTnMode.SelectedIndex;
                    if (cmbTnRoleMini != null && cmbTnRole != null && cmbTnRole.SelectedIndex >= 0)
                        cmbTnRoleMini.SelectedIndex = cmbTnRole.SelectedIndex;
                    if (numTnMiniStart != null && numTnStartRpm != null)
                        numTnMiniStart.Value = numTnStartRpm.Value;
                    if (numTnMiniStep != null && numTnStepRpm != null)
                        numTnMiniStep.Value = numTnStepRpm.Value;
                    if (numTnMiniEnd != null && numTnEndRpm != null)
                        numTnMiniEnd.Value = numTnEndRpm.Value;
                    if (numTnMiniTrq != null && numTnTorque != null)
                        numTnMiniTrq.Value = numTnTorque.Value;
                    if (numTnMiniDwell != null && numTnDwell != null)
                        numTnMiniDwell.Value = numTnDwell.Value;
                    if (txtTnMiniTag != null && txtTnTag != null && txtTnMiniTag.Text != txtTnTag.Text)
                        txtTnMiniTag.Text = txtTnTag.Text;
                }
            }
            finally
            {
                isSyncingTnControls = false;
            }
        }

        private void ShowTnChannelSelectDialog()
        {
            Form dlg = new Form()
            {
                Text = "T-N 測試溫度監測通道選取 (右上即時曲線監控)",
                Width = 450,
                Height = 500,
                StartPosition = FormStartPosition.CenterParent,
                FormBorderStyle = FormBorderStyle.FixedDialog,
                MaximizeBox = false,
                MinimizeBox = false,
                ShowInTaskbar = false,
                Font = new Font("微軟正黑體", 9f)
            };

            Label lHint = new Label()
            {
                Text = "請勾選需在 T-N 測試右上即時溫度曲線監控之通道：\n(若設備已連線，可點擊「⚡ 依實測選取」自動偵測真正有訊號的通道)",
                Dock = DockStyle.Top,
                Height = 44,
                Padding = new Padding(6),
                Font = new Font("微軟正黑體", 8.5f, FontStyle.Bold),
                ForeColor = Color.FromArgb(15, 23, 42)
            };

            CheckedListBox clb = new CheckedListBox()
            {
                Dock = DockStyle.Fill,
                CheckOnClick = true,
                Font = new Font("Consolas", 9.5f)
            };

            for (int i = 0; i < 20; i++)
            {
                string cName = (gl820ChannelNames != null && i < gl820ChannelNames.Length && !string.IsNullOrEmpty(gl820ChannelNames[i])) ? gl820ChannelNames[i] : ("CH" + (i + 1));
                bool isChecked = (tnMonitoredChannels != null && i < tnMonitoredChannels.Length) ? tnMonitoredChannels[i] : (i < 4);
                double curVal = (gbdChTemps != null && i < gbdChTemps.Length) ? gbdChTemps[i] : 0.0;
                string valHint = (curVal > -40.0 && curVal < 400.0 && Math.Abs(curVal) > 0.01) ? string.Format(" [{0:F1}℃]", curVal) : " [--.-]";
                clb.Items.Add(string.Format("CH{0,2}: {1,-10}{2}", i + 1, cName, valHint), isChecked);
            }

            FlowLayoutPanel pnlBtns = new FlowLayoutPanel() { Dock = DockStyle.Bottom, Height = 45, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(6) };
            Button btnCancel = new Button() { Text = "取消", DialogResult = DialogResult.Cancel, Width = 70, Height = 28 };
            Button btnOk = new Button() { Text = "確定", DialogResult = DialogResult.OK, Width = 70, Height = 28, BackColor = Color.FromArgb(16, 185, 129), ForeColor = Color.White };
            Button btnAutoDetect = new Button()
            {
                Text = "⚡ 依實測選取",
                Width = 115,
                Height = 28,
                BackColor = Color.FromArgb(59, 130, 246),
                ForeColor = Color.White,
                Font = new Font("微軟正黑體", 9f, FontStyle.Bold),
                Cursor = Cursors.Hand
            };
            btnAutoDetect.Click += (s, e) => {
                bool[] active = DetectActiveGbdChannels();
                if (active.Any(b => b))
                {
                    for (int i = 0; i < 20; i++) clb.SetItemChecked(i, active[i]);
                }
                else
                {
                    MessageBox.Show("目前尚未連線至 GL820 或未收到有效溫度數據，已先勾選預設 CH1~4。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    for (int i = 0; i < 20; i++) clb.SetItemChecked(i, i < 4);
                }
            };
            Button btnDefault = new Button() { Text = "前4點(CH1~4)", Width = 105, Height = 28 };
            btnDefault.Click += (s, e) => {
                for (int i = 0; i < 20; i++) clb.SetItemChecked(i, i < 4);
            };

            pnlBtns.Controls.AddRange(new Control[] { btnCancel, btnOk, btnAutoDetect, btnDefault });
            dlg.Controls.Add(clb);
            dlg.Controls.Add(lHint);
            dlg.Controls.Add(pnlBtns);

            if (dlg.ShowDialog(this) == DialogResult.OK)
            {
                int cnt = 0;
                List<string> selNames = new List<string>();
                for (int i = 0; i < 20; i++)
                {
                    tnMonitoredChannels[i] = clb.GetItemChecked(i);
                    if (tnMonitoredChannels[i])
                    {
                        cnt++;
                        selNames.Add(string.Format("CH{0}", i + 1));
                    }
                }
                if (cnt == 0)
                {
                    tnMonitoredChannels[0] = true;
                    cnt = 1;
                    selNames.Add("CH1");
                }
                if (lblTnSelectedChHint != null) lblTnSelectedChHint.Text = string.Format("(已選 {0} 通道: {1})", cnt, string.Join(",", selNames.ToArray()));
                if (tnTempTrend != null) tnTempTrend.SetChannelVisibility(tnMonitoredChannels);
            }
        }

        /// <summary>
        /// ★【待測端轉速平滑閉迴路追隨 (同動 S1/S2/S6 補轉差機制)】
        /// 實測轉速因加載轉差偏離目標轉速時，自動即時微調 SY.52 補償轉差，確保標準額定轉速運轉
        /// </summary>
        private void ApplyTnSpeedTracking(int spdCom, int spdBaud, int spdNode, int spdDrive, double targetSpd, double actAbsSpd)
        {
            if (actAbsSpd >= targetSpd * 0.5 && targetSpd > 50.0)
            {
                double spdDeadband = (trackingSpeedDeadband > 0) ? (double)trackingSpeedDeadband : 3.0;
                double spdDiff = targetSpd - actAbsSpd;
                if (Math.Abs(spdDiff) > spdDeadband)
                {
                    double maxSpdStep = (trackingSpeedMaxDelta > 0) ? (double)trackingSpeedMaxDelta : 2.0;
                    double step = Math.Sign(spdDiff) * Math.Min(Math.Max(1.0, Math.Abs(spdDiff) * 0.5), maxSpdStep * 2.0);
                    double newSpdCmd = Math.Max(0.0, Math.Min(6000.0, tnCurrentSpeedCmd + step));
                    if (newSpdCmd != tnCurrentSpeedCmd)
                    {
                        tnCurrentSpeedCmd = newSpdCmd;
                        KebWriteParam32(spdCom, spdBaud, spdNode, 0x0034, (int)Math.Round(tnCurrentSpeedCmd), "TN 速度閉迴路補轉差 (SY52)");
                        if (spdDrive == 2 && numHmiKebSpeed2 != null) numHmiKebSpeed2.Value = (decimal)tnCurrentSpeedCmd;
                        else if (spdDrive == 1 && numHmiKebSpeed1 != null) numHmiKebSpeed1.Value = (decimal)tnCurrentSpeedCmd;
                    }
                }
            }
        }

        // =========================================================================
        // T-N 測試邏輯 (含角色自選、手動定錨與梯度自適應繼承)
        // =========================================================================
        private void BtnStartTn_Click(object sender, EventArgs e)
        {
            tnResults.Clear();
            dgvTnPoints.Rows.Clear();
            if (tnChart != null) tnChart.ClearPoints();
            if (tnTempTrend != null) tnTempTrend.ClearData();

            bool isFromMini = (sender == btnTnMiniStart);
            SyncAllTnControls(fromMiniToMain: isFromMini);

            string tnTag = (txtTnTag != null && !string.IsNullOrEmpty(txtTnTag.Text.Trim())) ? txtTnTag.Text.Trim() : "TN";
            // ★ 自動開啟 RAW DATA 記錄 (檔名後綴: _{tnTag})
            StartAutoRawRecordingWithTag(tnTag);

            decimal startRpmVal = numTnStartRpm != null ? numTnStartRpm.Value : 50m;
            decimal stepRpmVal = numTnStepRpm != null ? numTnStepRpm.Value : 50m;
            decimal endRpmVal = numTnEndRpm != null ? numTnEndRpm.Value : 300m;
            decimal targetTrqVal = numTnTorque != null ? numTnTorque.Value : 15m;
            decimal dwellVal = numTnDwell != null ? numTnDwell.Value : 10m;
            int roleIdx = (cmbTnRole != null && cmbTnRole.SelectedIndex >= 0) ? cmbTnRole.SelectedIndex : 1;

            tnCurrentStep = (int)startRpmVal;
            tnStepSpeedReached = false; // 初始步進轉速尚未鎖定
            tnDwellRemaining = (int)dwellVal;
            tnOverCurrentStartTime = DateTime.MinValue;

            int totalSteps = Math.Max(1, ((int)endRpmVal - (int)startRpmVal) / Math.Max(1, (int)stepRpmVal) + 1);
            prgTn.Minimum = 0;
            prgTn.Maximum = Math.Max(1, totalSteps);
            prgTn.Value = 0;

            btnStartTn.Enabled = false;
            btnStopTn.Enabled = true;
            if (btnTnMiniStart != null) btnTnMiniStart.Enabled = false;
            if (btnTnMiniStop != null) btnTnMiniStop.Enabled = true;
            if (prgTnMini != null) { prgTnMini.Minimum = 0; prgTnMini.Maximum = Math.Max(1, totalSteps); prgTnMini.Value = 0; }

            // 角色分配：預設 B載台待測(速度) / A載台加載(轉矩)
            int spdDrive = (roleIdx == 1) ? 2 : 1; // 0: A待測(速度)/B加載, 1: B待測(速度)/A加載
            int trqDrive = (spdDrive == 1) ? 2 : 1;

            int spdCom = GetHmiKebComIdx(spdDrive), spdBaud = GetHmiKebBaudIdx(spdDrive), spdNode = (spdDrive == 1) ? (int)numHmiKebNode1.Value : (int)numHmiKebNode2.Value;
            int trqCom = GetHmiKebComIdx(trqDrive), trqBaud = GetHmiKebBaudIdx(trqDrive), trqNode = (trqDrive == 1) ? (int)numHmiKebNode1.Value : (int)numHmiKebNode2.Value;

            string spdDriveName = (spdDrive == 1) ? "A載台" : "B載台";
            string trqDriveName = (trqDrive == 1) ? "A載台" : "B載台";

            if (cmbTnMode != null && cmbTnMode.SelectedIndex == 1)
            {
                // =========================================================================
                // 模式 1: 多點自訂轉速扭力測試 (多組分別測試、穩定5秒、擷取30秒每秒1筆、換項降載25%)
                // =========================================================================
                tnCustomPoints.Clear();
                for (int i = 0; i < dgvTnMultiPoints.Rows.Count; i++)
                {
                    var row = dgvTnMultiPoints.Rows[i];
                    double spd = 0, trq = 0;
                    if (row.Cells[1].Value != null && double.TryParse(row.Cells[1].Value.ToString(), out spd) &&
                        row.Cells[2].Value != null && double.TryParse(row.Cells[2].Value.ToString(), out trq))
                    {
                        if (spd > 0 && trq >= 0)
                        {
                            tnCustomPoints.Add(new TnCustomPoint(i + 1, spd, trq));
                            row.Cells[3].Value = "等待中";
                            row.Cells[4].Value = "-";
                            row.Cells[5].Value = "-";
                            row.Cells[6].Value = "-";
                            row.Cells[7].Value = "-";
                        }
                    }
                }

                if (tnCustomPoints.Count == 0)
                {
                    MessageBox.Show("請至少設定一組有效的轉速 (>0 rpm) 與扭力 (>=0 Nm) 測試點！", "參數錯誤", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    btnStartTn.Enabled = true;
                    btnStopTn.Enabled = false;
                    if (btnTnMiniStart != null) btnTnMiniStart.Enabled = true;
                    if (btnTnMiniStop != null) btnTnMiniStop.Enabled = false;
                    return;
                }

                StartAutoRawRecordingWithTag(tnTag + "_Multi");

                tnMultiCurrentIndex = 0;
                tnMultiSubPhase = 0; // 0: 待測提速, 1: 平穩加載, 2: 穩定5s, 3: 擷取30s, 4: 換項降載25%
                tnMultiStabilizeCounter = 5;
                tnMultiSampleCounter = 30;
                tnMultiTransitionWaitSec = 0;
                tnConvergeTimeoutSec = 0;
                tnTrqSustainedSec = 0;
                tnAdaptedTorquePct = 0.0;
                tnOverCurrentStartTime = DateTime.MinValue;

                prgTn.Minimum = 0;
                prgTn.Maximum = tnCustomPoints.Count;
                prgTn.Value = 0;

                btnStartTn.Enabled = false;
                btnStopTn.Enabled = true;
                if (btnTnMiniStart != null) btnTnMiniStart.Enabled = false;
                if (btnTnMiniStop != null) btnTnMiniStop.Enabled = true;
                if (prgTnMini != null) { prgTnMini.Minimum = 0; prgTnMini.Maximum = tnCustomPoints.Count; prgTnMini.Value = 0; }

                var p0 = tnCustomPoints[0];
                // 加載端啟動轉矩歸零停機 (Sy50=0, cs18=0)
                KebWriteParamWithDll(trqCom, trqBaud, trqNode, 0x0F12, 0);
                SetHmiKebCommand(trqCom, trqBaud, trqNode, 0, string.Format("{0}TN加載端空載停機待命 (Sy50=0, cs18=0)", trqDriveName));

                // 待測端空載起轉
                tnCurrentSpeedCmd = p0.TargetSpeed;
                KebWriteParam32(spdCom, spdBaud, spdNode, 0x0034, (int)p0.TargetSpeed, "T-N多點第1點初轉速 (Sy52)");
                KebWriteParamWithDll(spdCom, spdBaud, spdNode, 0x0F12, 1000);
                SetHmiKebCommand(spdCom, spdBaud, spdNode, 4, string.Format("{0}TN待測端正轉 (Sy50=4)", spdDriveName));

                if (spdDrive == 2)
                {
                    if (numHmiKebSpeed2 != null) numHmiKebSpeed2.Value = Math.Max(0, Math.Min(6000, (decimal)p0.TargetSpeed));
                    if (numHmiKebTorque2 != null) numHmiKebTorque2.Value = 0;
                    if (numHmiKebSpeed1 != null) numHmiKebSpeed1.Value = 0;
                    if (numHmiKebTorque1 != null) numHmiKebTorque1.Value = 0;
                }
                else
                {
                    if (numHmiKebSpeed1 != null) numHmiKebSpeed1.Value = Math.Max(0, Math.Min(6000, (decimal)p0.TargetSpeed));
                    if (numHmiKebTorque1 != null) numHmiKebTorque1.Value = 0;
                    if (numHmiKebSpeed2 != null) numHmiKebSpeed2.Value = 0;
                    if (numHmiKebTorque2 != null) numHmiKebTorque2.Value = 0;
                }

                dgvTnMultiPoints.Rows[0].Cells[3].Value = "⏳ 提速中";
                lblTnStatus.Text = string.Format("多點測試啟動：第 1/{0} 點 (目標 {1:F0} rpm / {2:F1} Nm)",
                    tnCustomPoints.Count, p0.TargetSpeed, p0.TargetTorque);
                lblTnCountdown.Text = "提速空載中";
                if (lblTnMiniStatus != null) lblTnMiniStatus.Text = lblTnStatus.Text;
                if (lblTnMiniCountdown != null) lblTnMiniCountdown.Text = "提速空載中";

                webRemoteMode = "T-N 多點測試";
                webRemoteStatusText = lblTnStatus.Text;
                webRemotePhaseText = "提速空載中";

                WriteHmiLog("TN_CONFIG", string.Format("【T-N 多點測試啟動】共 {0} 組自訂點位，第 1 點: {1:F0} rpm / {2:F1} Nm (加載端初始歸零)",
                    tnCustomPoints.Count, p0.TargetSpeed, p0.TargetTorque));

                if (!isRunning) BtnStart_Click(null, null);
                tnTimer.Start();
                return;
            }

            // ★【操作鐵律】：只要沒有定錨，加載百分比強制嚴格從 0.0% 起步加載！
            if (!tnHasAnchor)
            {
                tnAdaptedTorquePct = 0.0;
            }

            // 同步更新主畫面數值輸入框 (待測端顯示初轉速，加載端強制顯示 0，未選者歸零)
            if (spdDrive == 2) // B載台待測 (速度), A載台加載 (轉矩)
            {
                if (numHmiKebSpeed2 != null) numHmiKebSpeed2.Value = Math.Max(0, Math.Min(6000, tnCurrentStep));
                if (numHmiKebTorque2 != null) numHmiKebTorque2.Value = 0;
                if (numHmiKebSpeed1 != null) numHmiKebSpeed1.Value = 0;
                if (numHmiKebTorque1 != null) numHmiKebTorque1.Value = 0; // 加載端強制歸零
            }
            else // A載台待測 (速度), B載台加載 (轉矩)
            {
                if (numHmiKebSpeed1 != null) numHmiKebSpeed1.Value = Math.Max(0, Math.Min(6000, tnCurrentStep));
                if (numHmiKebTorque1 != null) numHmiKebTorque1.Value = 0;
                if (numHmiKebSpeed2 != null) numHmiKebSpeed2.Value = 0;
                if (numHmiKebTorque2 != null) numHmiKebTorque2.Value = 0; // 加載端強制歸零
            }

            // ★【嚴禁竄改變頻器硬體架構暫存器】：廢除 SetHmiKebMode，完全保留現場 V/F (cs00=0) 或既有參數！

            // 1. 加載端啟動瞬間轉矩強制歸零 (cs.18 = 0) 並維持 STOP (Sy.50 = 0)，嚴禁空載提速階段硬轉對沖！
            KebWriteParamWithDll(trqCom, trqBaud, trqNode, 0x0F12, 0); // cs.18 = 0
            SetHmiKebCommand(trqCom, trqBaud, trqNode, 0, string.Format("{0}TN加載端空載停機待命 (Sy50=0, cs18=0)", trqDriveName));

            // 2. 待測端空載起轉至起始轉速 (1:1 寫入 Sy.52)
            tnCurrentSpeedCmd = (double)tnCurrentStep;
            KebWriteParam32(spdCom, spdBaud, spdNode, 0x0034, tnCurrentStep, "T-N 初轉速 (Sy52)");
            KebWriteParamWithDll(spdCom, spdBaud, spdNode, 0x0F12, 1000); // 待測端放行 100% 轉矩
            SetHmiKebCommand(spdCom, spdBaud, spdNode, 4, string.Format("{0}TN待測端正轉 (Sy50=4)", spdDriveName));

            tnPhase = 0; // 0: 等待轉速到位與轉矩收斂達標, 1: 穩定持載 10 秒倒數
            tnConvergeTimeoutSec = 0;
            tnStepSpeedReached = false;
            tnTrqSustainedSec = 0;
            tnDwellRemaining = (int)numTnDwell.Value;

            lblTnStatus.Text = string.Format("正在啟動：{0}待測 {1} rpm | {2}加載 (初始 0.0%)",
                spdDriveName, tnCurrentStep, trqDriveName);
            lblTnCountdown.Text = "提速空載中";
            if (lblTnMiniStatus != null) lblTnMiniStatus.Text = lblTnStatus.Text;
            if (lblTnMiniCountdown != null) lblTnMiniCountdown.Text = "提速空載中";
            // ── 遠端監看同步 ──
            webRemoteMode = "T-N 曲線測試";
            webRemoteStatusText = lblTnStatus.Text;
            webRemotePhaseText  = "提速空載中";

            WriteHmiLog("TN_CONFIG", string.Format("【T-N 啟動測試】測試配置: {0}待測 (速度) / {1}加載 (轉矩) | 起始轉速: {2} rpm, 步進: {3} rpm, 結束: {4} rpm, 目標轉矩: {5:F1} Nm (加載端轉矩啟動歸零 0.0%)",
                spdDriveName, trqDriveName, tnCurrentStep, numTnStepRpm.Value, numTnEndRpm.Value, targetTrqVal));

            if (!isRunning) BtnStart_Click(null, null);

            tnTimer.Start();
        }

        // =========================================================================
        // 模式 1: 多點自訂轉速扭力測試執行迴圈 (每秒 Tick)
        // =========================================================================
        private void RunTnMultiPointTick()
        {
            if (tnCustomPoints == null || tnCustomPoints.Count == 0 || tnMultiCurrentIndex >= tnCustomPoints.Count)
            {
                return;
            }

            int roleIdx = (cmbTnRoleMini != null && cmbTnRoleMini.SelectedIndex >= 0)
                ? cmbTnRoleMini.SelectedIndex
                : ((cmbTnRole != null && cmbTnRole.SelectedIndex >= 0) ? cmbTnRole.SelectedIndex : 1);
            int spdDrive = (roleIdx == 1) ? 2 : 1;
            int trqDrive = (spdDrive == 1) ? 2 : 1;
            int spdCom = GetHmiKebComIdx(spdDrive), spdBaud = GetHmiKebBaudIdx(spdDrive), spdNode = (spdDrive == 1) ? (int)numHmiKebNode1.Value : (int)numHmiKebNode2.Value;
            int trqCom = GetHmiKebComIdx(trqDrive), trqBaud = GetHmiKebBaudIdx(trqDrive), trqNode = (trqDrive == 1) ? (int)numHmiKebNode1.Value : (int)numHmiKebNode2.Value;

            string spdDriveName = (spdDrive == 1) ? "A載台" : "B載台";
            string trqDriveName = (trqDrive == 1) ? "A載台" : "B載台";

            var curPt = tnCustomPoints[tnMultiCurrentIndex];
            double targetSpd = curPt.TargetSpeed;
            double targetTrq = curPt.TargetTorque;

            // ★ 過電流安全保護 (Kt)
            double curKt = (double)tnMotorKt;
            if (curKt > 0.001 && targetTrq > 0.1)
            {
                double expCurrent = targetTrq / curKt;
                double limitPct = (double)tnOverCurrentPercent;
                double maxAllowCurrent = expCurrent * (1.0 + (limitPct / 100.0));
                double curSig = actCurrentSigma;
                double curDrv = (spdDrive == 1) ? kebCurrent1 : kebCurrent2;
                bool isSigOver = (curSig > maxAllowCurrent);
                bool isDrvOver = (curDrv > maxAllowCurrent);
                if (isSigOver || isDrvOver)
                {
                    DateTime now = DateTime.Now;
                    if (tnOverCurrentStartTime == DateTime.MinValue) tnOverCurrentStartTime = now;
                    double overSec = (now - tnOverCurrentStartTime).TotalSeconds;
                    double reqDelay = (double)tnOverCurrentDelaySec;
                    if (overSec >= reqDelay)
                    {
                        StopTnTest();
                        string tripMsg = string.Format("【🚨 T-N 多點過電流保護跳脫】\n目標轉矩：{0:F1} Nm, 超限上限：{1:F2} A, 持續 {2:F0} 秒 >= 防抖時間 {3:F0} 秒，已安全停機！", targetTrq, maxAllowCurrent, overSec, reqDelay);
                        WriteHmiLog("TN_OVERCURRENT_TRIP", tripMsg);
                        lblTnStatus.Text = "🚨 過電流跳脫停機！";
                        lblTnCountdown.Text = "過電流跳脫";
                        MessageBox.Show(tripMsg, "T-N 過電流保護跳脫", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        return;
                    }
                }
                else
                {
                    tnOverCurrentStartTime = DateTime.MinValue;
                }
            }

            double actAbsSpd = Math.Abs(actSpeed);
            double actAbsTrq = Math.Abs(actTorque);
            double spdErr = Math.Abs(actAbsSpd - targetSpd);
            double trqErr = targetTrq - actAbsTrq;

            // ★【穩定判定合理化】：必須等待測端閉迴路補轉差將轉速補償回額定轉速帶內 (誤差 <= Max(6 rpm, 1.5% 目標轉速))，且轉矩達標
            bool isSpdValid = (targetSpd <= 0) || (spdErr <= Math.Max(6.0, targetSpd * 0.015));
            bool isTrqValid = (targetTrq <= 0) || (tnAdaptedTorquePct >= 0.5 && Math.Abs(trqErr) <= Math.Max(1.0, targetTrq * 0.08));

            // =========================================================================
            // 子階段 0: 待測端提速/變速 (維持 25% 或空載，等待速度到達後才進入加載)
            // =========================================================================
            if (tnMultiSubPhase == 0)
            {
                tnConvergeTimeoutSec++;

                // ★【待測端轉速平滑閉迴路追隨 (同動 S1/S2/S6 補轉差機制)】
                ApplyTnSpeedTracking(spdCom, spdBaud, spdNode, spdDrive, targetSpd, actAbsSpd);

                // 速度到達判定：等待實際速度到達目標轉速帶 (誤差 <= Max(12.0 rpm, 2% 目標轉速))
                bool reached = (targetSpd <= 0) || (spdErr <= Math.Max(12.0, targetSpd * 0.02));
                if (!reached)
                {
                    // 若是換項變速過渡，加載端維持在 25% 負載；若是第 1 點起步則保持 0 轉矩
                    int curTrqHold = (int)Math.Round(tnAdaptedTorquePct * 10);
                    KebWriteParamWithDll(trqCom, trqBaud, trqNode, 0x0F12, curTrqHold);

                    lblTnStatus.Text = string.Format("⌛ [第 {0}/{1} 點] 變速調整中：實測 {2:F0} rpm / 目標 {3:F0} rpm (加載端保持 {4:F1}%)",
                        tnMultiCurrentIndex + 1, tnCustomPoints.Count, actAbsSpd, targetSpd, tnAdaptedTorquePct);
                    lblTnCountdown.Text = string.Format("變速中 ({0}s)", tnConvergeTimeoutSec);
                    if (lblTnMiniStatus != null) lblTnMiniStatus.Text = lblTnStatus.Text;
                    if (lblTnMiniCountdown != null) lblTnMiniCountdown.Text = lblTnCountdown.Text;
                    if (dgvTnMultiPoints.Rows.Count > tnMultiCurrentIndex)
                        dgvTnMultiPoints.Rows[tnMultiCurrentIndex].Cells[3].Value = "⏳ 變速調整中";
                    return;
                }

                // 轉速已確實到達！啟動加載端激磁，準備平滑遞增加載
                tnMultiSubPhase = 1;
                tnConvergeTimeoutSec = 0;
                tnTrqSustainedSec = 0;
                tnLoadTracker.Reset(tnCurrentSpeedCmd, tnAdaptedTorquePct);
                int curTrqSy50 = (trqDrive == 1) ? lastSy50Cmd1 : lastSy50Cmd2;
                if (curTrqSy50 != 4)
                {
                    SetHmiKebCommand(trqCom, trqBaud, trqNode, 4, string.Format("{0}TN加載端轉速達標激磁啟動 (Sy50=4)", trqDriveName));
                }
                WriteHmiLog("TN_MULTI", string.Format("【第 {0} 點轉速達標】實測 {1:F0} rpm (目標 {2:F0} rpm) 到位，進入平穩加載逼近 (當前負載基準: {3:F1}%)",
                    tnMultiCurrentIndex + 1, actAbsSpd, targetSpd, tnAdaptedTorquePct));
            }
            // =========================================================================
            // 子階段 1: 加載逼近目標轉矩 (全系統統一核心引擎 SY52 + CS18 雙閉環自適應加速)
            // =========================================================================
            else if (tnMultiSubPhase == 1)
            {
                tnConvergeTimeoutSec++;

                string statusDesc;
                bool isConverged = ExecuteUnifiedDualTrackingStep(
                    tnLoadTracker, spdDrive, trqDrive,
                    targetSpd, targetTrq, actAbsSpd, actAbsTrq,
                    out statusDesc, "TN多點");

                tnAdaptedTorquePct = tnLoadTracker.AdaptedTorquePct;
                tnCurrentSpeedCmd = tnLoadTracker.CurrentSpeedCmd;

                lblTnStatus.Text = string.Format("⌛ [第 {0}/{1} 點] {2}",
                    tnMultiCurrentIndex + 1, tnCustomPoints.Count, statusDesc);
                lblTnCountdown.Text = string.Format("加載中 ({0}s)", tnConvergeTimeoutSec);
                if (lblTnMiniStatus != null) lblTnMiniStatus.Text = lblTnStatus.Text;
                if (lblTnMiniCountdown != null) lblTnMiniCountdown.Text = lblTnCountdown.Text;
                if (dgvTnMultiPoints.Rows.Count > tnMultiCurrentIndex)
                    dgvTnMultiPoints.Rows[tnMultiCurrentIndex].Cells[3].Value = string.Format("加載中 ({0:F1}Nm)", actAbsTrq);

                if (isConverged)
                {
                    // 雙達標連續 2 秒 (轉矩到位且轉速已完全補償回來)，進入【子階段 2：穩定 5 秒等待】
                    tnMultiSubPhase = 2;
                    tnMultiStabilizeCounter = 5;
                    tnConvergeTimeoutSec = 0;
                    tnTrqSustainedSec = 0;
                    lblTnStatus.Text = string.Format("✅ [第 {0}/{1} 點] 轉速補償與轉矩雙雙達標 ({2:F0}rpm / {3:F1}Nm)，開始穩定 5 秒倒數！",
                        tnMultiCurrentIndex + 1, tnCustomPoints.Count, actAbsSpd, actAbsTrq);
                    lblTnCountdown.Text = "穩定等待: 5 s";
                    if (lblTnMiniStatus != null) lblTnMiniStatus.Text = lblTnStatus.Text;
                    if (lblTnMiniCountdown != null) lblTnMiniCountdown.Text = lblTnCountdown.Text;
                    if (dgvTnMultiPoints.Rows.Count > tnMultiCurrentIndex)
                        dgvTnMultiPoints.Rows[tnMultiCurrentIndex].Cells[3].Value = "穩定等待 (5s)";
                }
                else if (tnConvergeTimeoutSec >= 50)
                {
                    StopTnTest();
                    MessageBox.Show(string.Format("【T-N 多點加載警報】第 {0} 點加載逼近超時 50 秒未達標，已安全停機！", tnMultiCurrentIndex + 1), "加載未達標", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
            }
            // =========================================================================
            // 子階段 2: 穩定 5 秒等待 (倒數 5 秒，若轉速或轉矩偏離則暫停倒數持續微調補償)
            // =========================================================================
            else if (tnMultiSubPhase == 2)
            {
                // ★【待測端轉速平滑閉迴路追隨 (同動 S1/S2/S6 補轉差機制)】
                ApplyTnSpeedTracking(spdCom, spdBaud, spdNode, spdDrive, targetSpd, actAbsSpd);

                if (Math.Abs(trqErr) > 0.4)
                {
                    double step = (Math.Abs(trqErr) > 1.2) ? 0.3 : 0.1;
                    if (trqErr > 0) tnAdaptedTorquePct += step;
                    else tnAdaptedTorquePct -= step;
                    tnAdaptedTorquePct = Math.Max(0.0, Math.Min(100.0, tnAdaptedTorquePct));
                    int trqRaw = (int)Math.Round(tnAdaptedTorquePct * 10);
                    KebWriteParamWithDll(trqCom, trqBaud, trqNode, 0x0F12, trqRaw);
                    if (trqDrive == 1 && numHmiKebTorque1 != null) numHmiKebTorque1.Value = (decimal)tnAdaptedTorquePct;
                    else if (trqDrive == 2 && numHmiKebTorque2 != null) numHmiKebTorque2.Value = (decimal)tnAdaptedTorquePct;
                }

                // 轉速與轉矩必須維持在合格帶內才扣減秒數
                if (isSpdValid && isTrqValid)
                {
                    tnMultiStabilizeCounter--;
                    lblTnCountdown.Text = string.Format("穩定等待: {0} s", tnMultiStabilizeCounter);
                    lblTnStatus.Text = string.Format("⚖️ [第 {0}/{1} 點] 穩定確認中：轉速 {2:F0} rpm | 轉矩 {3:F2} Nm",
                        tnMultiCurrentIndex + 1, tnCustomPoints.Count, actAbsSpd, actAbsTrq);
                }
                else
                {
                    lblTnCountdown.Text = string.Format("調節等待: {0} s (暫停)", tnMultiStabilizeCounter);
                    lblTnStatus.Text = string.Format("⌛ [第 {0}/{1} 點] 偏離補償微調中：轉速 {2:F0}/{3:F0} rpm | 轉矩 {4:F2}/{5:F1} Nm",
                        tnMultiCurrentIndex + 1, tnCustomPoints.Count, actAbsSpd, targetSpd, actAbsTrq, targetTrq);
                }

                if (lblTnMiniStatus != null) lblTnMiniStatus.Text = lblTnStatus.Text;
                if (lblTnMiniCountdown != null) lblTnMiniCountdown.Text = lblTnCountdown.Text;
                if (dgvTnMultiPoints.Rows.Count > tnMultiCurrentIndex)
                    dgvTnMultiPoints.Rows[tnMultiCurrentIndex].Cells[3].Value = string.Format("穩定等待 ({0}s)", tnMultiStabilizeCounter);

                if (tnMultiStabilizeCounter <= 0)
                {
                    // 5 秒穩定結束，進入【子階段 3：擷取 30 秒穩定資料每秒 1 筆】
                    tnMultiSubPhase = 3;
                    tnMultiSampleCounter = 30;
                    curPt.Samples.Clear();

                    // ★【TN測試紀錄檔優化】在確定穩定後抓取30筆數據時給資料一個斷行
                    lock (manualRecordLock)
                    {
                        if (manualRecordWriter != null && isManualRecording)
                        {
                            try { manualRecordWriter.WriteLine(); manualRecordWriter.Flush(); } catch { }
                        }
                    }

                    lblTnStatus.Text = string.Format("📊 [第 {0}/{1} 點] 開始擷取 30 秒穩定資料 (每秒 1 筆)！", tnMultiCurrentIndex + 1, tnCustomPoints.Count);
                    lblTnCountdown.Text = "擷取: 30 s";
                    if (lblTnMiniStatus != null) lblTnMiniStatus.Text = lblTnStatus.Text;
                    if (lblTnMiniCountdown != null) lblTnMiniCountdown.Text = lblTnCountdown.Text;
                    if (dgvTnMultiPoints.Rows.Count > tnMultiCurrentIndex)
                        dgvTnMultiPoints.Rows[tnMultiCurrentIndex].Cells[3].Value = "📊 擷取中 (30s)";
                }
            }
            // =========================================================================
            // 子階段 3: 擷取 30 秒穩定資料 (每秒 1 筆)
            // =========================================================================
            else if (tnMultiSubPhase == 3)
            {
                // 依 Modify.txt 規範順序: 轉速 頻率 轉矩 U1 U2 U3 I1 I2 I3 輸入功率 輸出功率 功因 效率 Kt V_Sigma I_Sigma 溫度
                curPt.Samples.Add(new double[] {
                    actAbsSpd, actFrequency, actAbsTrq,
                    wtU1, wtU2, wtU3,
                    wtI1, wtI2, wtI3,
                    actElecPower, actMechPower, actPf, actEfficiency,
                    actKt, actVoltageSigma, actCurrentSigma, actTemp
                });

                // 依指示：採樣時即刻進行頻率比對診斷記錄
                CheckAndLogFrequencyComparison("TN_SAMPLE");

                // ★【待測端轉速平滑閉迴路追隨 (同動 S1/S2/S6 補轉差機制)】
                ApplyTnSpeedTracking(spdCom, spdBaud, spdNode, spdDrive, targetSpd, actAbsSpd);

                if (Math.Abs(trqErr) > 0.4)
                {
                    double step = (Math.Abs(trqErr) > 1.2) ? 0.3 : 0.1;
                    if (trqErr > 0) tnAdaptedTorquePct += step;
                    else tnAdaptedTorquePct -= step;
                    tnAdaptedTorquePct = Math.Max(0.0, Math.Min(100.0, tnAdaptedTorquePct));
                    KebWriteParamWithDll(trqCom, trqBaud, trqNode, 0x0F12, (int)Math.Round(tnAdaptedTorquePct * 10));
                    if (trqDrive == 1 && numHmiKebTorque1 != null) numHmiKebTorque1.Value = (decimal)tnAdaptedTorquePct;
                    else if (trqDrive == 2 && numHmiKebTorque2 != null) numHmiKebTorque2.Value = (decimal)tnAdaptedTorquePct;
                }

                tnMultiSampleCounter--;
                lblTnCountdown.Text = string.Format("擷取中: {0} s", tnMultiSampleCounter);
                lblTnStatus.Text = string.Format("📊 [第 {0}/{1} 點] 資料擷取中 ({2}/30筆)：轉速 {3:F0} rpm | 轉矩 {4:F2} Nm",
                    tnMultiCurrentIndex + 1, tnCustomPoints.Count, curPt.Samples.Count, actAbsSpd, actAbsTrq);
                if (lblTnMiniStatus != null) lblTnMiniStatus.Text = lblTnStatus.Text;
                if (lblTnMiniCountdown != null) lblTnMiniCountdown.Text = lblTnCountdown.Text;
                if (dgvTnMultiPoints.Rows.Count > tnMultiCurrentIndex)
                    dgvTnMultiPoints.Rows[tnMultiCurrentIndex].Cells[3].Value = string.Format("📊 擷取中 ({0}/30s)", curPt.Samples.Count);

                if (tnMultiSampleCounter <= 0)
                {
                    // ★【TN測試紀錄檔優化】30筆數據結束後也多一個斷行方便識別測試區間
                    lock (manualRecordLock)
                    {
                        if (manualRecordWriter != null && isManualRecording)
                        {
                            try { manualRecordWriter.WriteLine(); manualRecordWriter.Flush(); } catch { }
                        }
                    }

                    // 30 筆採樣完成！計算 30 秒平均值
                    double avgSpd = curPt.Samples.Average(s => s[0]);
                    double avgFreq = curPt.Samples.Average(s => s[1]);
                    double avgTrq = curPt.Samples.Average(s => s[2]);
                    double avgElecPwr = curPt.Samples.Average(s => s[9]);
                    double avgMechPwr = curPt.Samples.Average(s => s[10]);
                    double avgPf = curPt.Samples.Average(s => s[11]);
                    double avgEff = curPt.Samples.Average(s => s[12]);

                    curPt.AvgSpeed = avgSpd;
                    curPt.AvgTorque = avgTrq;
                    curPt.AvgMechPower = avgMechPwr;
                    curPt.AvgEfficiency = avgEff;
                    curPt.Status = "✅ 完成";

                    if (dgvTnMultiPoints.Rows.Count > tnMultiCurrentIndex)
                    {
                        var r = dgvTnMultiPoints.Rows[tnMultiCurrentIndex];
                        r.Cells[3].Value = "✅ 完成 (30s均值)";
                        r.Cells[4].Value = avgSpd.ToString("F1");
                        r.Cells[5].Value = avgTrq.ToString("F2");
                        r.Cells[6].Value = avgMechPwr.ToString("F2");
                        r.Cells[7].Value = avgEff.ToString("F1");
                    }

                    int rowIdx = dgvTnPoints.Rows.Add();
                    dgvTnPoints.Rows[rowIdx].Cells[0].Value = (tnMultiCurrentIndex + 1).ToString();
                    dgvTnPoints.Rows[rowIdx].Cells[1].Value = targetSpd.ToString("F0");
                    dgvTnPoints.Rows[rowIdx].Cells[2].Value = avgSpd.ToString("F1");
                    dgvTnPoints.Rows[rowIdx].Cells[3].Value = avgTrq.ToString("F2");
                    dgvTnPoints.Rows[rowIdx].Cells[4].Value = avgMechPwr.ToString("F2");
                    dgvTnPoints.Rows[rowIdx].Cells[5].Value = avgEff.ToString("F1");
                    dgvTnPoints.Rows[rowIdx].Cells[6].Value = "✅ 完成 (30s均值)";

                    if (tnChart != null) tnChart.AddPoint(avgSpd, avgTrq, avgMechPwr);

                    tnResults.Add(new string[] {
                        targetSpd.ToString("F0"), avgSpd.ToString("F1"), avgTrq.ToString("F2"),
                        avgMechPwr.ToString("F2"), avgEff.ToString("F1"), actKt.ToString("F2"),
                        "PASS"
                    });

                    prgTn.Value = Math.Min(prgTn.Maximum, tnMultiCurrentIndex + 1);
                    if (prgTnMini != null) prgTnMini.Value = prgTn.Value;

                    tnMultiLastTestedTorque = targetTrq;
                    tnMultiLastTestedAdaptedPct = tnAdaptedTorquePct;

                    if (tnMultiCurrentIndex >= tnCustomPoints.Count - 1)
                    {
                        // 全部自訂點測試完成！
                        tnTimer.Stop();
                        if (isManualRecording) StopManualRecording(showPrompt: false);

                        StartGradualAutoStop(spdDrive, trqDrive, "T-N多點測試全部完成", () => {
                            btnStartTn.Enabled = true;
                            btnStopTn.Enabled = false;
                            if (btnTnMiniStart != null) btnTnMiniStart.Enabled = true;
                            if (btnTnMiniStop != null) btnTnMiniStop.Enabled = false;
                            lblTnStatus.Text = "[成功] T-N 多點自訂測試全部完成！";
                            lblTnCountdown.Text = "倒數: 完成";
                            if (lblTnMiniStatus != null) lblTnMiniStatus.Text = lblTnStatus.Text;
                            if (lblTnMiniCountdown != null) lblTnMiniCountdown.Text = lblTnCountdown.Text;
                            MessageBox.Show("T-N 多點自訂測試已順利完成！\n全部項目均已完成 5 秒穩定 + 30 秒數據擷取與平均值計算，請點擊「匯出報表」儲存數據。", "測試完成", MessageBoxButtons.OK, MessageBoxIcon.Information);
                        });
                        return;
                    }

                    // 檢查下一個測試項目是否需要變速
                    var nextPt = tnCustomPoints[tnMultiCurrentIndex + 1];
                    bool isSpeedChange = Math.Abs(nextPt.TargetSpeed - curPt.TargetSpeed) > 5.0;

                    if (!isSpeedChange)
                    {
                        // =========================================================================
                        // 規範 2: 若下一個測試項目沒有轉速改變，則直接修正(遞增遞減)扭力至目標！
                        // 嚴禁降載或變速，直接切入子階段 1 平穩逼近新目標轉矩！
                        // =========================================================================
                        tnMultiCurrentIndex++;
                        tnMultiSubPhase = 1; // 直接進入加載逼近新目標轉矩
                        tnConvergeTimeoutSec = 0;
                        tnTrqSustainedSec = 0;
                        tnLoadTracker.Reset(tnCurrentSpeedCmd, tnAdaptedTorquePct);

                        WriteHmiLog("TN_MULTI", string.Format("【同轉速換項】第 {0} 點轉速相同 ({1:F0} rpm)，直接平穩調升/調降轉矩至 {2:F1} Nm (當前負載基準: {3:F1}%)",
                            tnMultiCurrentIndex + 1, nextPt.TargetSpeed, nextPt.TargetTorque, tnAdaptedTorquePct));

                        lblTnStatus.Text = string.Format("➡️ 同速調扭：第 {0}/{1} 點 (目標 {2:F0} rpm / {3:F1} Nm)，直接平穩過渡...",
                            tnMultiCurrentIndex + 1, tnCustomPoints.Count, nextPt.TargetSpeed, nextPt.TargetTorque);
                        lblTnCountdown.Text = "調扭過渡中";
                        if (lblTnMiniStatus != null) lblTnMiniStatus.Text = lblTnStatus.Text;
                        if (lblTnMiniCountdown != null) lblTnMiniCountdown.Text = lblTnCountdown.Text;
                        if (dgvTnMultiPoints.Rows.Count > tnMultiCurrentIndex)
                            dgvTnMultiPoints.Rows[tnMultiCurrentIndex].Cells[3].Value = "⏳ 調扭加載中";
                    }
                    else
                    {
                        // =========================================================================
                        // 規範 3: 若下個測試目標是需要變速，則遞減(分三次減)降載到下個目標的25%之後再開始變速！
                        // 等速度到達後再開始遞增加載。
                        // =========================================================================
                        tnMultiSubPhase = 4;
                        tnMultiTransitionWaitSec = 0;
                        tnRampDownStep = 1; // 啟動第 1 階梯降載
                        tnRampDownStartPct = tnAdaptedTorquePct;

                        // 計算目標 25% 輸出百分比 (以原測試扭力百分比之 25% 為安全基準)
                        tnRampDownTargetPct = Math.Max(0.0, tnRampDownStartPct * 0.25);

                        // 立即輸出第 1 步降載：start - (start - target) * (1/3)
                        double step1Pct = tnRampDownStartPct - (tnRampDownStartPct - tnRampDownTargetPct) * (1.0 / 3.0);
                        tnAdaptedTorquePct = Math.Max(0.0, step1Pct);
                        int trqRaw1 = (int)Math.Round(tnAdaptedTorquePct * 10);
                        KebWriteParamWithDll(trqCom, trqBaud, trqNode, 0x0F12, trqRaw1);

                        if (trqDrive == 1 && numHmiKebTorque1 != null) numHmiKebTorque1.Value = (decimal)tnAdaptedTorquePct;
                        else if (trqDrive == 2 && numHmiKebTorque2 != null) numHmiKebTorque2.Value = (decimal)tnAdaptedTorquePct;

                        double trqStep1Nm = curPt.TargetTorque * (tnAdaptedTorquePct / Math.Max(0.1, tnRampDownStartPct));
                        lblTnStatus.Text = string.Format("⬇️ 換項降載 (1/3 步)：轉矩調降至 {0:F1}% ({1:F1} Nm)，平穩邁向 25%...", tnAdaptedTorquePct, trqStep1Nm);
                        lblTnCountdown.Text = "降載階梯 1/3";
                        if (lblTnMiniStatus != null) lblTnMiniStatus.Text = lblTnStatus.Text;
                        if (lblTnMiniCountdown != null) lblTnMiniCountdown.Text = lblTnCountdown.Text;

                        WriteHmiLog("TN_MULTI", string.Format("【換項階梯降載 1/3】第 {0} 點完成，啟動 3 步平穩降載：第 1 步降至 {1:F1}%",
                            tnMultiCurrentIndex + 1, tnAdaptedTorquePct));
                    }
                }
            }
            // =========================================================================
            // 子階段 4: 換項 3 步階梯平穩降載至 25% ➔ 變速 ➔ 切換至子階段 0 等待新速度到達
            // =========================================================================
            else if (tnMultiSubPhase == 4)
            {
                tnMultiTransitionWaitSec++;

                if (tnRampDownStep == 1)
                {
                    // 執行第 2 步降載：start - (start - target) * (2/3)
                    tnRampDownStep = 2;
                    double step2Pct = tnRampDownStartPct - (tnRampDownStartPct - tnRampDownTargetPct) * (2.0 / 3.0);
                    tnAdaptedTorquePct = Math.Max(0.0, step2Pct);
                    int trqRaw2 = (int)Math.Round(tnAdaptedTorquePct * 10);
                    KebWriteParamWithDll(trqCom, trqBaud, trqNode, 0x0F12, trqRaw2);

                    if (trqDrive == 1 && numHmiKebTorque1 != null) numHmiKebTorque1.Value = (decimal)tnAdaptedTorquePct;
                    else if (trqDrive == 2 && numHmiKebTorque2 != null) numHmiKebTorque2.Value = (decimal)tnAdaptedTorquePct;

                    lblTnStatus.Text = string.Format("⬇️ 換項降載 (2/3 步)：轉矩調降至 {0:F1}%，平穩邁向 25%...", tnAdaptedTorquePct);
                    lblTnCountdown.Text = "降載階梯 2/3";
                    if (lblTnMiniStatus != null) lblTnMiniStatus.Text = lblTnStatus.Text;
                    if (lblTnMiniCountdown != null) lblTnMiniCountdown.Text = lblTnCountdown.Text;
                    WriteHmiLog("TN_MULTI", string.Format("【換項階梯降載 2/3】第 2 步降至 {0:F1}%", tnAdaptedTorquePct));
                    return;
                }
                else if (tnRampDownStep == 2)
                {
                    // 執行第 3 步降載：精確到達 target 25%
                    tnRampDownStep = 3;
                    tnAdaptedTorquePct = Math.Max(0.0, tnRampDownTargetPct);
                    int trqRaw3 = (int)Math.Round(tnAdaptedTorquePct * 10);
                    KebWriteParamWithDll(trqCom, trqBaud, trqNode, 0x0F12, trqRaw3);

                    if (trqDrive == 1 && numHmiKebTorque1 != null) numHmiKebTorque1.Value = (decimal)tnAdaptedTorquePct;
                    else if (trqDrive == 2 && numHmiKebTorque2 != null) numHmiKebTorque2.Value = (decimal)tnAdaptedTorquePct;

                    lblTnStatus.Text = string.Format("⬇️ 換項降載 (3/3 步)：轉矩已到達 25% ({0:F1}%)，確認負載穩定後開始變速...", tnAdaptedTorquePct);
                    lblTnCountdown.Text = "降載階梯 3/3";
                    if (lblTnMiniStatus != null) lblTnMiniStatus.Text = lblTnStatus.Text;
                    if (lblTnMiniCountdown != null) lblTnMiniCountdown.Text = lblTnCountdown.Text;
                    WriteHmiLog("TN_MULTI", string.Format("【換項階梯降載 3/3】第 3 步已到達 25% ({0:F1}%)", tnAdaptedTorquePct));
                    return;
                }
                else if (tnRampDownStep == 3)
                {
                    // 3 步降載全部完成！確認轉矩已降至安全帶，正式切換待測端新轉速！
                    if (tnMultiCurrentIndex + 1 >= tnCustomPoints.Count)
                    {
                        // 邊界防禦：已無下一點，安全結束測試
                        tnTimer.Stop();
                        if (isManualRecording) StopManualRecording(showPrompt: false);
                        StartGradualAutoStop(spdDrive, trqDrive, "T-N多點測試全部完成", null);
                        return;
                    }

                    tnMultiCurrentIndex++;
                    var nextPt = tnCustomPoints[tnMultiCurrentIndex];
                    double nextSpd = nextPt.TargetSpeed;
                    double nextTrq = nextPt.TargetTorque;

                    WriteHmiLog("TN_MULTI", string.Format("【3步降載完成 ➔ 啟動變速】轉矩已平穩降至 25% ({0:F1}%, 實測 {1:F1} Nm)，正式切換第 {2} 點目標轉速: {3:F0} rpm (目標轉矩: {4:F1} Nm)",
                        tnAdaptedTorquePct, actAbsTrq, tnMultiCurrentIndex + 1, nextSpd, nextTrq));

                    // 加載端保持在 25% 負載 (不歸零，避免急速甩載)，待測端下達新目標轉速
                    tnCurrentSpeedCmd = nextSpd;
                    KebWriteParam32(spdCom, spdBaud, spdNode, 0x0034, (int)nextSpd, "TN多點換項變速 (SY52)");

                    if (spdDrive == 2 && numHmiKebSpeed2 != null) numHmiKebSpeed2.Value = Math.Max(0, Math.Min(6000, (decimal)nextSpd));
                    else if (spdDrive == 1 && numHmiKebSpeed1 != null) numHmiKebSpeed1.Value = Math.Max(0, Math.Min(6000, (decimal)nextSpd));

                    // 切換至子階段 0：等待待測端新轉速到達目標！速度到達後才會自 25% 遞增加載！
                    tnMultiSubPhase = 0;
                    tnConvergeTimeoutSec = 0;
                    tnTrqSustainedSec = 0;
                    tnRampDownStep = 0;

                    lblTnStatus.Text = string.Format("切換第 {0}/{1} 點：目標轉速 {2:F0} rpm / 目標轉矩 {3:F1} Nm，等待速度到位後加載...",
                        tnMultiCurrentIndex + 1, tnCustomPoints.Count, nextSpd, nextTrq);
                    lblTnCountdown.Text = "變速調整中";
                    if (lblTnMiniStatus != null) lblTnMiniStatus.Text = lblTnStatus.Text;
                    if (lblTnMiniCountdown != null) lblTnMiniCountdown.Text = lblTnCountdown.Text;
                    if (dgvTnMultiPoints.Rows.Count > tnMultiCurrentIndex)
                        dgvTnMultiPoints.Rows[tnMultiCurrentIndex].Cells[3].Value = "⏳ 變速調整中";
                }
            }


            // ── 遠端監看同步 ──
            if (lblTnStatus != null) { webRemoteStatusText = lblTnStatus.Text; }
            if (lblTnCountdown != null) { webRemotePhaseText = lblTnCountdown.Text; }
        }

        private void TnTimer_Tick(object sender, EventArgs e)
        {
            if (cmbTnMode != null && cmbTnMode.SelectedIndex == 1)
            {
                RunTnMultiPointTick();
                return;
            }

            int roleIdx = (cmbTnRoleMini != null && cmbTnRoleMini.SelectedIndex >= 0)
                ? cmbTnRoleMini.SelectedIndex
                : ((cmbTnRole != null && cmbTnRole.SelectedIndex >= 0) ? cmbTnRole.SelectedIndex : 1);
            int spdDrive = (roleIdx == 1) ? 2 : 1;
            int trqDrive = (spdDrive == 1) ? 2 : 1;
            int spdCom = GetHmiKebComIdx(spdDrive), spdBaud = GetHmiKebBaudIdx(spdDrive), spdNode = (spdDrive == 1) ? (int)numHmiKebNode1.Value : (int)numHmiKebNode2.Value;
            int trqCom = GetHmiKebComIdx(trqDrive), trqBaud = GetHmiKebBaudIdx(trqDrive), trqNode = (trqDrive == 1) ? (int)numHmiKebNode1.Value : (int)numHmiKebNode2.Value;

            string spdDriveName = (spdDrive == 1) ? "A載台" : "B載台";
            string trqDriveName = (trqDrive == 1) ? "A載台" : "B載台";

            double targetSpd = tnCurrentStep;
            // 雙保險讀取目標轉矩 (優先讀取 Mini 與 Main 中使用者最新設定之非零數值)
            double targetTrq = (numTnMiniTrq != null && numTnMiniTrq.Value > 0)
                ? (double)numTnMiniTrq.Value
                : ((numTnTorque != null && numTnTorque.Value > 0) ? (double)numTnTorque.Value : 15.0);

            // ★【T-N 過電流保護邏輯 (Kt 換算理論電流)】
            double curKt = (double)tnMotorKt;
            if (curKt > 0.001 && targetTrq > 0.1)
            {
                double expCurrent = targetTrq / curKt; // 理論預期電流 (A)
                double limitPct = (double)tnOverCurrentPercent;
                double maxAllowCurrent = expCurrent * (1.0 + (limitPct / 100.0)); // 門檻上限

                double curSig = actCurrentSigma;
                double curDrv = (spdDrive == 1) ? kebCurrent1 : kebCurrent2;

                bool isSigOver = (curSig > maxAllowCurrent);
                bool isDrvOver = (curDrv > maxAllowCurrent);

                if (isSigOver || isDrvOver)
                {
                    DateTime now = DateTime.Now;
                    if (tnOverCurrentStartTime == DateTime.MinValue)
                    {
                        tnOverCurrentStartTime = now;
                    }
                    double overSec = (now - tnOverCurrentStartTime).TotalSeconds;
                    double reqDelay = (double)tnOverCurrentDelaySec;

                    string tripDetail = isSigOver
                        ? string.Format("PowerMeter Σ: {0:F2}A > 門檻 {1:F2}A", curSig, maxAllowCurrent)
                        : string.Format("驅動器: {0:F2}A > 門檻 {1:F2}A", curDrv, maxAllowCurrent);

                    if (overSec >= reqDelay)
                    {
                        StopTnTest();
                        string tripMsg = string.Format(
                            "【🚨 T-N 過電流保護跳脫】\n\n" +
                            "目標轉矩：{0:F1} Nm (Kt: {1:F2} Nm/A, 理論電流: {2:F2} A)\n" +
                            "超限門檻：+{3:F0}% (允許上限: {4:F2} A)\n" +
                            "觸發詳情：{5}\n" +
                            "持續超限：{6:F0} 秒 >= 設定防抖時間 {7:F0} 秒\n\n" +
                            "系統已立即強制卸載並安全停機保護待測設備！",
                            targetTrq, curKt, expCurrent, limitPct, maxAllowCurrent, tripDetail, overSec, reqDelay);
                        WriteHmiLog("TN_OVERCURRENT_TRIP", tripMsg);
                        lblTnStatus.Text = "🚨 過電流跳脫停機！";
                        lblTnCountdown.Text = "過電流跳脫";
                        if (lblTnMiniStatus != null) lblTnMiniStatus.Text = lblTnStatus.Text;
                        if (lblTnMiniCountdown != null) lblTnMiniCountdown.Text = lblTnCountdown.Text;
                        MessageBox.Show(tripMsg, "T-N 過電流保護跳脫", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        return;
                    }
                }
                else
                {
                    tnOverCurrentStartTime = DateTime.MinValue;
                }
            }
            double actAbsSpd = Math.Abs(actSpeed);
            double actAbsTrq = Math.Abs(actTorque);

            // ★【雙向絕對值精確比對】：徹底解決實體扭力計正轉/反轉符號造成的死鎖問題！
            double spdErr = Math.Abs(actAbsSpd - targetSpd);
            double trqErr = targetTrq - actAbsTrq;

            // ★【穩定判定合理化】：必須等待測端閉迴路補轉差將轉速補償回額定轉速帶內 (誤差 <= Max(6 rpm, 1.5% 目標轉速))，且轉矩達標
            bool isSpdValid = (targetSpd <= 0) || (spdErr <= Math.Max(6.0, targetSpd * 0.015));
            bool isTrqValid = (targetTrq <= 0) || (tnAdaptedTorquePct >= 1.0 && Math.Abs(trqErr) <= Math.Max(1.0, targetTrq * 0.08));

            // =========================================================================
            // 【階段 0：提速起跑 ➔ 轉速鎖定 ➔ 平穩加載逼近】
            // =========================================================================
            if (tnPhase == 0)
            {
                tnConvergeTimeoutSec++;
                tnDwellRemaining = (int)numTnDwell.Value; // 鎖定滿額倒數，禁止提前跳過

                // 【子階段 0A：等待待測端轉速首次到位】
                // 轉速未到位前，加載端維持 0 轉矩空載 (cs.18 = 0)，讓待測端順暢提速！
                if (!tnStepSpeedReached)
                {
                    bool reached = (targetSpd <= 0) || (actAbsSpd >= targetSpd * 0.82) || (spdErr <= Math.Max(20.0, targetSpd * 0.12));
                    if (!reached)
                    {
                        KebWriteParamWithDll(trqCom, trqBaud, trqNode, 0x0F12, 0); // 加載端保持 0 轉矩
                        lblTnStatus.Text = string.Format("⌛ 轉速提速中：實測 {0:F0} rpm / 目標 {1:F0} rpm (加載端保持 0 轉矩)", actAbsSpd, targetSpd);
                        lblTnCountdown.Text = "提速空載中";
                        if (lblTnMiniStatus != null) lblTnMiniStatus.Text = lblTnStatus.Text;
                        if (lblTnMiniCountdown != null) lblTnMiniCountdown.Text = lblTnCountdown.Text;
                        return;
                    }

                    // ★【單向鎖定狀態】：轉速首次到位！立即激磁加載端 (Sy50=4)，並鎖定進入加載階段！
                    // 鎖定後絕不再因帶載轉差自然降速而誤把轉矩甩回 0，徹底消滅 0<->26Nm 震盪失控！
                    tnStepSpeedReached = true;
                    tnTrqSustainedSec = 0;
                    tnLoadTracker.Reset(targetSpd, tnAdaptedTorquePct);
                    int curTrqSy50 = (trqDrive == 1) ? lastSy50Cmd1 : lastSy50Cmd2;
                    if (curTrqSy50 != 4)
                    {
                        SetHmiKebCommand(trqCom, trqBaud, trqNode, 4, string.Format("{0}TN加載端轉速達標激磁啟動 (Sy50=4)", trqDriveName));
                        WriteHmiLog("TN_STAGE", string.Format("【T-N 轉速已鎖定】待測端轉速已達標 ({0:F1} rpm / 目標 {1:F0} rpm)，加載端 {2} 啟動激磁 (Sy50=4)，正式鎖定進入轉矩平穩加載！", actAbsSpd, targetSpd, trqDriveName));
                    }
                }

                // 【子階段 0B：轉速已鎖定，以全系統統一核心引擎 SY52 + CS18 雙閉環自適應加速逼近】
                string statusDesc;
                bool isConverged = ExecuteUnifiedDualTrackingStep(
                    tnLoadTracker, spdDrive, trqDrive,
                    targetSpd, targetTrq, actAbsSpd, actAbsTrq,
                    out statusDesc, "TN梯度");

                tnAdaptedTorquePct = tnLoadTracker.AdaptedTorquePct;
                tnCurrentSpeedCmd = tnLoadTracker.CurrentSpeedCmd;

                lblTnStatus.Text = string.Format("⌛ [TN梯度] {0}", statusDesc);
                lblTnCountdown.Text = string.Format("加載中 ({0}s)", tnConvergeTimeoutSec);
                if (lblTnMiniStatus != null) lblTnMiniStatus.Text = lblTnStatus.Text;
                if (lblTnMiniCountdown != null) lblTnMiniCountdown.Text = lblTnCountdown.Text;

                if (!isConverged)
                {
                    // ★ 超時防呆安全保護：加載逼近超過 50 秒仍未達標時，暫停測試並警告，絕不放任過載！
                    if (tnConvergeTimeoutSec >= 50)
                    {
                        StopTnTest();
                        lblTnStatus.Text = "⚠️ 加載未達標，測試已暫停！";
                        lblTnCountdown.Text = "加載未達標中斷";
                        if (lblTnMiniStatus != null) lblTnMiniStatus.Text = lblTnStatus.Text;
                        if (lblTnMiniCountdown != null) lblTnMiniCountdown.Text = lblTnCountdown.Text;

                        string timeoutMsg = string.Format(
                            "【T-N 測試加載未達標警報】\n\n" +
                            "目標轉速：{0:F0} rpm (實測: {1:F0} rpm)\n" +
                            "目標轉矩：{2:F2} Nm (實測: {3:F2} Nm)\n\n" +
                            "系統已持續加載逼近 50 秒，但實測扭矩仍無法收斂達標！\n" +
                            "加載端已強制卸載歸零保護。\n\n" +
                            "請確認以下硬體狀態：\n" +
                            "1. 加載端變頻器安全端子 (ST) 是否已閉合導通？\n" +
                            "2. 加載端制動電阻 / 回饋單元是否已正確連線？\n" +
                            "3. 目標轉矩是否超出加載變頻器或馬達規格上限？",
                            targetSpd, actAbsSpd, targetTrq, actAbsTrq);
                        MessageBox.Show(timeoutMsg, "加載未達標中斷", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        return;
                    }
                }
                else
                {
                    // 連續 2 秒轉矩與轉速穩定達標！平穩進入【階段 1：穩定持載倒數】
                    tnPhase = 1;
                    tnConvergeTimeoutSec = 0;
                    tnTrqSustainedSec = 0;
                    lblTnStatus.Text = string.Format("✅ 轉矩與轉速已穩定達標 ({0:F1} Nm / {1:F0} rpm)，開始持載倒數！", actAbsTrq, actAbsSpd);
                    lblTnCountdown.Text = string.Format("倒數: {0} s", tnDwellRemaining);
                    if (lblTnMiniStatus != null) lblTnMiniStatus.Text = lblTnStatus.Text;
                    if (lblTnMiniCountdown != null) lblTnMiniCountdown.Text = lblTnCountdown.Text;
                }
            }
            // =========================================================================
            // 【階段 1：10 秒穩定持載期與判定】(★ 鐵律：實測轉速與轉矩雙雙達標後才能開始/進行倒數)
            // =========================================================================
            else if (tnPhase == 1)
            {
                // ★【待測端轉速平滑閉迴路追隨 (同動 S1/S2/S6 補轉差機制)】
                ApplyTnSpeedTracking(spdCom, spdBaud, spdNode, spdDrive, targetSpd, actAbsSpd);

                // 在持載期間進行自適應動態閉迴路維持 (超調時快速收斂，徹底避免 16.68 Nm 超調被判 FAIL)
                if (Math.Abs(trqErr) > 0.4)
                {
                    double step = (Math.Abs(trqErr) > 1.2) ? 0.3 : 0.1;
                    if (trqErr > 0) tnAdaptedTorquePct += step;
                    else tnAdaptedTorquePct -= step;
                    tnAdaptedTorquePct = Math.Max(0.0, Math.Min(100.0, tnAdaptedTorquePct));
                    int trqRaw = (int)Math.Round(tnAdaptedTorquePct * 10);
                    KebWriteParamWithDll(trqCom, trqBaud, trqNode, 0x0F12, trqRaw);
                    if (trqDrive == 1 && numHmiKebTorque1 != null) numHmiKebTorque1.Value = (decimal)tnAdaptedTorquePct;
                    else if (trqDrive == 2 && numHmiKebTorque2 != null) numHmiKebTorque2.Value = (decimal)tnAdaptedTorquePct;
                }

                // ★【達標才倒數鐵律】：只有在轉速與轉矩雙雙達標合格帶內，才扣減倒數！
                bool isTnTargetReached = (spdErr <= Math.Max(6.0, targetSpd * 0.015)) && (Math.Abs(trqErr) <= Math.Max(1.0, targetTrq * 0.08));
                if (isTnTargetReached)
                {
                    tnDwellRemaining--;
                    lblTnCountdown.Text = string.Format("持載倒數: {0} s (已達標)", tnDwellRemaining);
                    lblTnStatus.Text = string.Format("✅ 達標持載中：轉速 {0:F0} rpm | 轉矩 {1:F2} Nm (目標 {2:F1} Nm)", actAbsSpd, actAbsTrq, targetTrq);
                }
                else
                {
                    lblTnCountdown.Text = string.Format("調節等待: {0} s (暫停)", tnDwellRemaining);
                    lblTnStatus.Text = string.Format("⌛ 偏離微調中：轉速 {0:F0}/{1:F0} rpm | 轉矩 {2:F2}/{3:F1} Nm (未達標暫停倒數)", actAbsSpd, targetSpd, actAbsTrq, targetTrq);
                    tnConvergeTimeoutSec++;
                    if (tnConvergeTimeoutSec >= 60)
                    {
                        StopTnTest();
                        lblTnStatus.Text = "⚠️ 持載微調超時，測試已暫停！";
                        lblTnCountdown.Text = "持載微調超時";
                        if (lblTnMiniStatus != null) lblTnMiniStatus.Text = lblTnStatus.Text;
                        if (lblTnMiniCountdown != null) lblTnMiniCountdown.Text = lblTnCountdown.Text;
                        MessageBox.Show("【T-N 測試警報】持載期調節超過 60 秒仍無法穩定達標，已安全停機卸載！", "持載調節超時", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        return;
                    }
                }
                if (lblTnMiniStatus != null) lblTnMiniStatus.Text = lblTnStatus.Text;
                if (lblTnMiniCountdown != null) lblTnMiniCountdown.Text = lblTnCountdown.Text;

                if (tnDwellRemaining <= 0)
                {
                    // 10 秒穩定結束，採樣點位！
                    int rowIdx = dgvTnPoints.Rows.Add();
                    dgvTnPoints.Rows[rowIdx].Cells[0].Value = dgvTnPoints.Rows.Count.ToString();
                    dgvTnPoints.Rows[rowIdx].Cells[1].Value = tnCurrentStep.ToString();
                    dgvTnPoints.Rows[rowIdx].Cells[2].Value = actAbsSpd.ToString("F1");
                    dgvTnPoints.Rows[rowIdx].Cells[3].Value = actAbsTrq.ToString("F2");
                    dgvTnPoints.Rows[rowIdx].Cells[4].Value = actMechPower.ToString("F2");
                    dgvTnPoints.Rows[rowIdx].Cells[5].Value = actEfficiency.ToString("F1");

                    bool isPass = (spdErr <= Math.Max(25.0, targetSpd * 0.08)) && (Math.Abs(trqErr) <= Math.Max(1.0, targetTrq * 0.08));
                    dgvTnPoints.Rows[rowIdx].Cells[6].Value = isPass ? "✅ 合格 (Pass)" : "⚠️ 偏差 (Fail)";

                    if (tnChart != null) tnChart.AddPoint(actAbsSpd, actAbsTrq, actMechPower);

                    tnResults.Add(new string[] {
                        tnCurrentStep.ToString(), actAbsSpd.ToString("F1"), actAbsTrq.ToString("F2"),
                        actMechPower.ToString("F2"), actEfficiency.ToString("F1"), actKt.ToString("F2"),
                        isPass ? "PASS" : "FAIL"
                    });

                    if (prgTn.Value < prgTn.Maximum) prgTn.Value++;
                    if (prgTnMini != null && prgTnMini.Value < prgTnMini.Maximum) prgTnMini.Value = prgTn.Value;

                    tnCurrentStep += (int)numTnStepRpm.Value;

                    // 檢查是否完成全梯度測試
                    if (tnCurrentStep > (int)numTnEndRpm.Value)
                    {
                        tnTimer.Stop();
                        if (isManualRecording)
                        {
                            StopManualRecording(showPrompt: false);
                        }
                        StartGradualAutoStop(spdDrive, trqDrive, "T-N測試全梯度完成", () => {
                            btnStartTn.Enabled = true;
                            btnStopTn.Enabled = false;
                            if (btnTnMiniStart != null) btnTnMiniStart.Enabled = true;
                            if (btnTnMiniStop != null) btnTnMiniStop.Enabled = false;
                            lblTnStatus.Text = "[成功] T-N 曲線測試全部完成！";
                            lblTnCountdown.Text = "倒數: 完成";
                            if (lblTnMiniStatus != null) lblTnMiniStatus.Text = lblTnStatus.Text;
                            if (lblTnMiniCountdown != null) lblTnMiniCountdown.Text = lblTnCountdown.Text;
                            MessageBox.Show("T-N 曲線自動測試已順利完成！\n已先降負載再降速平滑停機，請點擊「匯出 T-N 報表」儲存數據。", "測試完成", MessageBoxButtons.OK, MessageBoxIcon.Information);
                        });
                        return;
                    }

                    // 步進到下一階梯：先卸載為 0 -> 升速 -> 回到 Phase 0 重新判定轉速到位後再漸進加載！
                    KebWriteParamWithDll(trqCom, trqBaud, trqNode, 0x0F12, 0); // 升速前強制卸載為 0
                    if (!tnHasAnchor) tnAdaptedTorquePct = 0.0; // ★【無定錨鐵律】：每個新階梯重新從 0.0% 開始加！
                    tnStepSpeedReached = false; // ★【復歸鎖定】：下一個新階梯需要重新判定空載提速到位！
                    tnTrqSustainedSec = 0;
                    tnCurrentSpeedCmd = (double)tnCurrentStep;
                    KebWriteParam32(spdCom, spdBaud, spdNode, 0x0034, tnCurrentStep, "T-N 階梯升速");

                    // 同步更新主畫面數值輸入框
                    if (spdDrive == 2)
                    {
                        if (numHmiKebSpeed2 != null) numHmiKebSpeed2.Value = Math.Max(0, Math.Min(6000, tnCurrentStep));
                        if (numHmiKebTorque1 != null) numHmiKebTorque1.Value = 0;
                    }
                    else
                    {
                        if (numHmiKebSpeed1 != null) numHmiKebSpeed1.Value = Math.Max(0, Math.Min(6000, tnCurrentStep));
                        if (numHmiKebTorque2 != null) numHmiKebTorque2.Value = 0;
                    }

                    tnPhase = 0;
                    tnConvergeTimeoutSec = 0;
                    tnDwellRemaining = (int)numTnDwell.Value;
                    lblTnStatus.Text = string.Format("切換下一階梯：目標轉速 {0} rpm，等待轉速到位...", tnCurrentStep);
                    lblTnCountdown.Text = "提速空載中";
                    if (lblTnMiniStatus != null) lblTnMiniStatus.Text = lblTnStatus.Text;
                    if (lblTnMiniCountdown != null) lblTnMiniCountdown.Text = lblTnCountdown.Text;
                }
            }
            // ── 遠端監看：每秒同步最新狀態標籤 ──
            if (lblTnStatus != null) { webRemoteStatusText = lblTnStatus.Text; }
            if (lblTnCountdown != null) { webRemotePhaseText = lblTnCountdown.Text; }
        }

        private void StopTnTest()
        {
            if (tnTimer != null) tnTimer.Stop();
            if (isManualRecording)
            {
                StopManualRecording(showPrompt: false);
            }
            tnCurrentSpeedCmd = 0.0;
            tnStepSpeedReached = false;
            tnTrqSustainedSec = 0;
            tnPhase = 0;
            tnAdaptedTorquePct = 0.0;
            tnMultiSubPhase = 0;
            tnMultiCurrentIndex = 0;
            tnMultiStabilizeCounter = 5;
            tnMultiSampleCounter = 30;

            int roleIdx = (cmbTnRoleMini != null && cmbTnRoleMini.SelectedIndex >= 0)
                ? cmbTnRoleMini.SelectedIndex
                : ((cmbTnRole != null && cmbTnRole.SelectedIndex >= 0) ? cmbTnRole.SelectedIndex : 1);
            int spdDrive = (roleIdx == 1) ? 2 : 1;
            int trqDrive = (spdDrive == 1) ? 2 : 1;

            StartGradualAutoStop(spdDrive, trqDrive, "T-N測試停止", () => {
                if (btnStartTn != null) btnStartTn.Enabled = true;
                if (btnStopTn != null) btnStopTn.Enabled = false;
                if (btnTnMiniStart != null) btnTnMiniStart.Enabled = true;
                if (btnTnMiniStop != null) btnTnMiniStop.Enabled = false;
                if (lblTnStatus != null) lblTnStatus.Text = "狀態: 已手動停止";
                if (lblTnMiniStatus != null) lblTnMiniStatus.Text = "狀態: 已停止";
                if (lblTnCountdown != null) lblTnCountdown.Text = "倒數: -- s";
                if (lblTnMiniCountdown != null) lblTnMiniCountdown.Text = "倒數: -- s";
                // ── 遠端監看重置 ──
                webRemoteMode = "IDLE"; webRemoteStatusText = "T-N 測試已停止"; webRemotePhaseText = "";
            });
        }

        private void BtnExportTn_Click(object sender, EventArgs e)
        {
            try
            {
                string logDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs");
                if (!Directory.Exists(logDir)) Directory.CreateDirectory(logDir);

                if (cmbTnMode != null && cmbTnMode.SelectedIndex == 1)
                {
                    // 匯出多點自訂測試彙總與每秒明細報表
                    string multiPath = Path.Combine(logDir, string.Format("Report_TN_MultiPoints_{0}.csv", DateTime.Now.ToString("yyyyMMdd_HHmmss")));
                    using (StreamWriter sw = new StreamWriter(multiPath, false, Encoding.UTF8))
                    {
                        sw.WriteLine("=== T-N 多點自訂測試彙總報表 ===");
                        sw.WriteLine("Point,TargetSpeed_rpm,TargetTorque_Nm,AvgSpeed_rpm,AvgFrequency_Hz,AvgTorque_Nm,AvgElecPower_kW,AvgMechPower_kW,AvgPF,AvgEfficiency_pct,Status");
                        for (int i = 0; i < tnCustomPoints.Count; i++)
                        {
                            var pt = tnCustomPoints[i];
                            double ptFreq = (pt.Samples != null && pt.Samples.Count > 0) ? pt.Samples.Average(s => s[1]) : 0.0;
                            double ptElecPwr = (pt.Samples != null && pt.Samples.Count > 0) ? pt.Samples.Average(s => s[9]) : 0.0;
                            double ptPf = (pt.Samples != null && pt.Samples.Count > 0) ? pt.Samples.Average(s => s[11]) : 0.0;
                            sw.WriteLine(string.Format("{0},{1:F0},{2:F2},{3:F1},{4:F2},{5:F2},{6:F2},{7:F2},{8:F3},{9:F1},{10}",
                                pt.Index, pt.TargetSpeed, pt.TargetTorque, pt.AvgSpeed, ptFreq, pt.AvgTorque, ptElecPwr, pt.AvgMechPower, ptPf, pt.AvgEfficiency, pt.Status));
                        }
                        sw.WriteLine();
                        sw.WriteLine("=== 各測試點 30 秒每秒穩定資料明細 (1Hz) ===");
                        sw.WriteLine("Point,SampleSec,Speed_rpm,Frequency_Hz,Torque_Nm,Voltage_U1_V,Voltage_U2_V,Voltage_U3_V,Current_I1_A,Current_I2_A,Current_I3_A,ElecPower_kW,MechPower_kW,PF,Efficiency_pct,Kt_NmA,VoltSigma_V,CurrSigma_A,MotorTemp_C");
                        for (int i = 0; i < tnCustomPoints.Count; i++)
                        {
                            var pt = tnCustomPoints[i];
                            sw.WriteLine(); // ★ 依 Modify.txt: 在確定穩定後抓取30筆數據時給資料一個斷行
                            for (int s = 0; s < pt.Samples.Count; s++)
                            {
                                var row = pt.Samples[s];
                                sw.WriteLine(string.Format("{0},{1},{2:F1},{3:F2},{4:F2},{5:F2},{6:F2},{7:F2},{8:F3},{9:F3},{10:F3},{11:F2},{12:F2},{13:F3},{14:F1},{15:F2},{16:F1},{17:F2},{18:F1}",
                                    pt.Index, s + 1,
                                    row[0], row[1], row[2],
                                    row[3], row[4], row[5],
                                    row[6], row[7], row[8],
                                    row[9], row[10], row[11], row[12],
                                    row[13], row[14], row[15], row[16]));
                            }
                            sw.WriteLine(); // ★ 依 Modify.txt: 結束後也多一個斷行方便識別測試區間
                        }
                    }
                    PurgeLocalLogs(false);
                    MessageBox.Show("T-N 多點自訂測試完整報表 (含30秒秒級明細) 已成功匯出至:\n" + multiPath, "匯出成功", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                string path = Path.Combine(logDir, string.Format("Report_TN_Curve_{0}.csv", DateTime.Now.ToString("yyyyMMdd_HHmmss")));
                using (StreamWriter sw = new StreamWriter(path, false, Encoding.UTF8))
                {
                    sw.WriteLine("Step,TargetRpm,ActualSpeed_rpm,ActualTorque_Nm,MechPower_kW,Efficiency_pct,Kt_NmA,Status");
                    for (int i = 0; i < tnResults.Count; i++)
                    {
                        sw.WriteLine(string.Format("{0},{1}", i + 1, string.Join(",", tnResults[i])));
                    }
                }
                PurgeLocalLogs(false);
                MessageBox.Show("T-N 測試報表已成功匯出至:\n" + path, "匯出成功", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show("匯出失敗: " + ex.Message, "錯誤");
            }
        }
    }
}
