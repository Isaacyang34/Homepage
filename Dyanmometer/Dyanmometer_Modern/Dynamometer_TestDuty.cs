using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace DynamometerHMI
{
    public partial class MainForm : Form
    {
        // =========================================================================
        // 分頁 3: IEC 60034-1 標準工作制測試 (S1 / S2 / S6 週期圖示與 V/F 雙重定錨)
        // =========================================================================
        private void BuildDutyTab(TabPage tab)
        {
            tab.Controls.Clear();
            splitDuty = new SplitContainer()
            {
                Dock = DockStyle.Fill,
                Orientation = Orientation.Horizontal,
                SplitterWidth = 8,
                BackColor = Color.FromArgb(203, 213, 225)
            };
            splitDuty.Panel1.AutoScroll = true;
            splitDuty.Panel1.BackColor = Color.FromArgb(248, 250, 252);
            splitDuty.Panel2.AutoScroll = true;
            splitDuty.Panel2.BackColor = Color.White;
            SafeSetupSplitContainer(splitDuty, "DutyMain", 539, 150, 100);

            splitDutyTop = new SplitContainer()
            {
                Dock = DockStyle.Fill,
                Orientation = Orientation.Vertical,
                SplitterWidth = 8,
                BackColor = Color.FromArgb(203, 213, 225)
            };
            splitDutyTop.Panel1.AutoScroll = true;
            splitDutyTop.Panel1.BackColor = Color.FromArgb(248, 250, 252);
            splitDutyTop.Panel2.AutoScroll = true;
            splitDutyTop.Panel2.BackColor = Color.FromArgb(248, 250, 252);
            SafeSetupSplitContainer(splitDutyTop, "DutyTop", 905, 250, 150);

            // -------------------------------------------------------------
            // 左側：4 分頁獨立配置 (主控排程、S1設定、S2設定、S6設定) + 底部操作與安全防護列
            // -------------------------------------------------------------
            GroupBox grp = new GroupBox()
            {
                Text = "IEC 60034-1 工作制測試 (4 分頁排程與自適應在線微調)",
                Dock = DockStyle.Fill,
                Font = new Font("微軟正黑體", 12f, FontStyle.Bold),
                BackColor = Color.FromArgb(248, 250, 252)
            };

            TableLayoutPanel tlpLeft = new TableLayoutPanel()
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 2,
                BackColor = Color.FromArgb(248, 250, 252)
            };
            tlpLeft.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            tlpLeft.RowStyles.Add(new RowStyle(SizeType.Percent, 100f)); // TabControl
            tlpLeft.RowStyles.Add(new RowStyle(SizeType.AutoSize));      // 底部操作與狀態面板

            // =============================================================
            // 4 分頁核心容器 (TabControl)
            // =============================================================
            tabDutyControl = new TabControl()
            {
                Dock = DockStyle.Fill,
                Font = new Font("微軟正黑體", 11.5f, FontStyle.Bold)
            };

            tabDutyMain = new TabPage("【主控排程 / 運轉總覽】") { BackColor = Color.FromArgb(248, 250, 252), Padding = new Padding(6) };
            tabDutyS1 = new TabPage("【S1 額定連續設定】") { BackColor = Color.FromArgb(248, 250, 252), Padding = new Padding(8), AutoScroll = true };
            tabDutyS2 = new TabPage("【S2 短時過載設定】") { BackColor = Color.FromArgb(248, 250, 252), Padding = new Padding(8), AutoScroll = true };
            tabDutyS6 = new TabPage("【S6 週期負載設定】") { BackColor = Color.FromArgb(248, 250, 252), Padding = new Padding(8), AutoScroll = true };

            tabDutyControl.TabPages.Add(tabDutyMain);
            tabDutyControl.TabPages.Add(tabDutyS1);
            tabDutyControl.TabPages.Add(tabDutyS2);
            tabDutyControl.TabPages.Add(tabDutyS6);

            // -------------------------------------------------------------
            // 分頁 1: 【主控排程 / 運轉總覽】
            // -------------------------------------------------------------
            TableLayoutPanel tlpMain = new TableLayoutPanel()
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 2,
                BackColor = Color.FromArgb(248, 250, 252)
            };
            tlpMain.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            tlpMain.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            tlpMain.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

            TableLayoutPanel tlpMainCfg = new TableLayoutPanel()
            {
                Dock = DockStyle.Fill,
                ColumnCount = 4,
                RowCount = 3,
                AutoSize = true,
                BackColor = Color.FromArgb(248, 250, 252),
                Padding = new Padding(2)
            };
            tlpMainCfg.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90f));
            tlpMainCfg.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
            tlpMainCfg.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90f));
            tlpMainCfg.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
            tlpMainCfg.RowStyles.Add(new RowStyle(SizeType.Absolute, 36f));
            tlpMainCfg.RowStyles.Add(new RowStyle(SizeType.Absolute, 36f));
            tlpMainCfg.RowStyles.Add(new RowStyle(SizeType.Absolute, 36f));

            Label lRole = new Label() { Text = "待測端:", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleRight, AutoSize = true, Font = new Font("微軟正黑體", 11.5f, FontStyle.Bold) };
            cmbDutyRole = new ComboBox() { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList, Font = new Font("微軟正黑體", 11f) };
            cmbDutyRole.Items.AddRange(new object[] { "A載台待測(速度) / B加載", "B載台待測(速度) / A加載" });
            cmbDutyRole.SelectedIndex = 1;
            cmbDutyRole.SelectedIndexChanged += (s, e) => {
                if (!isSyncingDutyControls && cmbDutyMiniRole != null && cmbDutyMiniRole.SelectedIndex != cmbDutyRole.SelectedIndex)
                {
                    isSyncingDutyControls = true;
                    cmbDutyMiniRole.SelectedIndex = cmbDutyRole.SelectedIndex;
                    isSyncingDutyControls = false;
                }
                UpdateDutyPreflightSummary();
            };

            Label lMainMode = new Label() { Text = "測試模式:", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleRight, AutoSize = true, Font = new Font("微軟正黑體", 11.5f, FontStyle.Bold) };
            cmbDutyMainMode = new ComboBox() { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList, Font = new Font("微軟正黑體", 11f, FontStyle.Bold) };
            cmbDutyMainMode.Items.AddRange(new object[] {
                "連續自動排程 (依排程方案依序執行)",
                "單項測試：S1 額定連續",
                "單項測試：S2 短時過載",
                "單項測試：S6 週期負載"
            });
            cmbDutyMainMode.SelectedIndex = 0;

            tlpMainCfg.Controls.Add(lRole, 0, 0);
            tlpMainCfg.Controls.Add(cmbDutyRole, 1, 0);
            tlpMainCfg.Controls.Add(lMainMode, 2, 0);
            tlpMainCfg.Controls.Add(cmbDutyMainMode, 3, 0);

            Label lPlan = new Label() { Text = "排程方案:", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleRight, AutoSize = true, Font = new Font("微軟正黑體", 11.5f, FontStyle.Bold) };
            cmbBatchDutyPlan = new ComboBox() { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList, Font = new Font("微軟正黑體", 10.5f, FontStyle.Bold), Enabled = true };
            cmbBatchDutyPlan.Items.AddRange(new object[] {
                "1. S1 -> S6 -> S2 (三項全測)",
                "2. S1 -> S6 (額定 + 週期)",
                "3. S1 -> S2 (額定 + 短時)",
                "4. S6 -> S2 (週期 + 短時)"
            });
            cmbBatchDutyPlan.SelectedIndex = 0;
            cmbBatchDutyPlan.SelectedIndexChanged += (s, e) => UpdateDutyPreflightSummary();

            Label lCool = new Label() { Text = "冷卻策略:", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleRight, AutoSize = true, Font = new Font("微軟正黑體", 11.5f, FontStyle.Bold) };
            cmbBatchCoolingMode = new ComboBox() { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList, Font = new Font("微軟正黑體", 10f), Enabled = true };
            cmbBatchCoolingMode.Items.AddRange(new object[] {
                "冷卻: 自動辨識 (W-IN/OUT為水冷25℃，否則氣冷AT)",
                "冷卻: 強制水冷電機 (冰水機循環降至 <= 25.0℃)",
                "冷卻: 強制氣冷電機 (降至 <= AT+1.5℃ 且 15分無下降)"
            });
            cmbBatchCoolingMode.SelectedIndex = 0;
            cmbBatchCoolingMode.SelectedIndexChanged += (s, e) => UpdateDutyPreflightSummary();

            tlpMainCfg.Controls.Add(lPlan, 0, 1);
            tlpMainCfg.Controls.Add(cmbBatchDutyPlan, 1, 1);
            tlpMainCfg.Controls.Add(lCool, 2, 1);
            tlpMainCfg.Controls.Add(cmbBatchCoolingMode, 3, 1);

            FlowLayoutPanel flpOc = new FlowLayoutPanel() { Dock = DockStyle.Fill, AutoSize = true, Margin = new Padding(0) };
            Label lblDutyOcTitle = new Label() { Text = "[電]過電流保護:", AutoSize = true, Font = new Font("微軟正黑體", 11f, FontStyle.Bold), ForeColor = Color.FromArgb(194, 65, 12), Margin = new Padding(0, 4, 2, 0) };
            Label lblDutyOcPct = new Label() { Text = "門檻:+", AutoSize = true, Font = new Font("微軟正黑體", 10.5f, FontStyle.Bold), Margin = new Padding(4, 4, 0, 0) };
            numDutyOverCurrentPct = CreateNumericUpDown(Point.Empty, 60, 5, 100, dutyOverCurrentPercent, 0, 5);
            numDutyOverCurrentPct.Font = new Font("微軟正黑體", 10.5f);
            numDutyOverCurrentPct.ValueChanged += (s, e) => { dutyOverCurrentPercent = numDutyOverCurrentPct.Value; UpdateDutyPreflightSummary(); };
            Label lblDutyOcPctUnit = new Label() { Text = "%", AutoSize = true, Font = new Font("微軟正黑體", 10.5f, FontStyle.Bold), Margin = new Padding(0, 4, 8, 0) };

            Label lblDutyOcDelay = new Label() { Text = "持續:", AutoSize = true, Font = new Font("微軟正黑體", 10.5f, FontStyle.Bold), Margin = new Padding(4, 4, 0, 0) };
            numDutyOverCurrentDelay = CreateNumericUpDown(Point.Empty, 55, 1, 60, dutyOverCurrentDelaySec, 0, 1);
            numDutyOverCurrentDelay.Font = new Font("微軟正黑體", 10.5f);
            numDutyOverCurrentDelay.ValueChanged += (s, e) => { dutyOverCurrentDelaySec = numDutyOverCurrentDelay.Value; UpdateDutyPreflightSummary(); };
            Label lblDutyOcDelayUnit = new Label() { Text = "s", AutoSize = true, Font = new Font("微軟正黑體", 10.5f, FontStyle.Bold), Margin = new Padding(0, 4, 8, 0) };

            lblDutyCurrentBaseline = new Label() { Text = "基準: 尚未確立 (採樣中)", AutoSize = true, Font = new Font("微軟正黑體", 10.5f, FontStyle.Bold), ForeColor = Color.FromArgb(71, 85, 105), Margin = new Padding(4, 4, 0, 0) };

            Label lblRevTrqTitle = new Label() { Text = "[反轉矩超時]:", AutoSize = true, Font = new Font("微軟正黑體", 10.5f, FontStyle.Bold), ForeColor = Color.FromArgb(185, 28, 28), Margin = new Padding(10, 4, 2, 0) };
            numDutyRevTrqTimeout = CreateNumericUpDown(Point.Empty, 55, 1, 60, reverseTorqueTimeoutSec, 0, 1);
            numDutyRevTrqTimeout.Font = new Font("微軟正黑體", 10.5f);
            numDutyRevTrqTimeout.ValueChanged += (s, e) => {
                reverseTorqueTimeoutSec = numDutyRevTrqTimeout.Value;
                if (numTnRevTrqTimeout != null && numTnRevTrqTimeout.Value != numDutyRevTrqTimeout.Value)
                    numTnRevTrqTimeout.Value = numDutyRevTrqTimeout.Value;
                SaveLayoutConfig();
                UpdateDutyPreflightSummary();
            };
            Label lblRevTrqUnit = new Label() { Text = "s", AutoSize = true, Font = new Font("微軟正黑體", 10.5f, FontStyle.Bold), Margin = new Padding(0, 4, 8, 0) };

            flpOc.Controls.AddRange(new Control[] { lblDutyOcTitle, lblDutyOcPct, numDutyOverCurrentPct, lblDutyOcPctUnit, lblDutyOcDelay, numDutyOverCurrentDelay, lblDutyOcDelayUnit, lblRevTrqTitle, numDutyRevTrqTimeout, lblRevTrqUnit, lblDutyCurrentBaseline });
            tlpMainCfg.SetColumnSpan(flpOc, 4);
            tlpMainCfg.Controls.Add(flpOc, 0, 2);

            tlpMain.Controls.Add(tlpMainCfg, 0, 0);

            // 中央容器：待命時呈現預檢確認清單，運轉時切換為即時階段儀表
            Panel pnlMainCenter = new Panel() { Dock = DockStyle.Fill, Margin = new Padding(0, 6, 0, 0) };

            txtDutyPreflightSummary = new TextBox()
            {
                Dock = DockStyle.Fill,
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical,
                Font = new Font("Consolas", 11.5f),
                BackColor = Color.FromArgb(248, 250, 252),
                ForeColor = Color.FromArgb(15, 23, 42),
                BorderStyle = BorderStyle.FixedSingle
            };

            pnlDutyLiveMonitor = new Panel()
            {
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(238, 242, 255),
                BorderStyle = BorderStyle.FixedSingle,
                Visible = false,
                Padding = new Padding(12)
            };
            TableLayoutPanel tlpLive = new TableLayoutPanel()
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 5,
                BackColor = Color.Transparent
            };
            tlpLive.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            for (int r = 0; r < 5; r++) tlpLive.RowStyles.Add(new RowStyle(SizeType.Percent, 20f));

            lblDutyLiveStage = new Label() { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, Font = new Font("微軟正黑體", 14f, FontStyle.Bold), ForeColor = Color.FromArgb(30, 64, 175), Text = "【階段】待命中" };
            lblDutyLiveCountdown = new Label() { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, Font = new Font("微軟正黑體", 13f, FontStyle.Bold), ForeColor = Color.FromArgb(15, 23, 42), Text = "運轉時間: 00分00秒" };
            lblDutyLiveTargetActual = new Label() { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, Font = new Font("微軟正黑體", 13f, FontStyle.Bold), ForeColor = Color.FromArgb(3, 105, 161), Text = "轉速: -- rpm | 轉矩: -- Nm" };
            lblDutyLiveSlope = new Label() { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, Font = new Font("微軟正黑體", 12f, FontStyle.Bold), ForeColor = Color.FromArgb(71, 85, 105), Text = "即時運轉指標: 輸出功率 -- kW | 馬達最高溫: --" };
            lblDutyLiveCooling = new Label() { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, Font = new Font("微軟正黑體", 12f, FontStyle.Bold), ForeColor = Color.FromArgb(5, 150, 105), Text = "冷卻狀態: 正常運轉 (無待冷卻)" };

            tlpLive.Controls.Add(lblDutyLiveStage, 0, 0);
            tlpLive.Controls.Add(lblDutyLiveCountdown, 0, 1);
            tlpLive.Controls.Add(lblDutyLiveTargetActual, 0, 2);
            tlpLive.Controls.Add(lblDutyLiveSlope, 0, 3);
            tlpLive.Controls.Add(lblDutyLiveCooling, 0, 4);
            pnlDutyLiveMonitor.Controls.Add(tlpLive);

            pnlMainCenter.Controls.Add(txtDutyPreflightSummary);
            pnlMainCenter.Controls.Add(pnlDutyLiveMonitor);
            tlpMain.Controls.Add(pnlMainCenter, 0, 1);
            tabDutyMain.Controls.Add(tlpMain);

            // -------------------------------------------------------------
            // 分頁 2: 【S1 額定連續設定】
            // -------------------------------------------------------------
            TableLayoutPanel tlpS1 = new TableLayoutPanel()
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 4,
                AutoScroll = true,
                Padding = new Padding(10),
                BackColor = Color.FromArgb(248, 250, 252)
            };
            tlpS1.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            tlpS1.RowStyles.Add(new RowStyle(SizeType.Absolute, 42f));
            tlpS1.RowStyles.Add(new RowStyle(SizeType.Absolute, 38f));
            tlpS1.RowStyles.Add(new RowStyle(SizeType.Absolute, 42f));
            tlpS1.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

            // Row 0: 額定轉速與額定轉矩橫向並排
            FlowLayoutPanel flpS1MainParams = new FlowLayoutPanel() { Dock = DockStyle.Fill, AutoSize = true, Margin = new Padding(0) };
            Label lS1Spd = new Label() { Text = "額定轉速(rpm):", AutoSize = true, Font = new Font("微軟正黑體", 11.5f, FontStyle.Bold), Margin = new Padding(0, 7, 4, 0) };
            numS1Speed = CreateNumericUpDown(Point.Empty, 115, 0, 6000, 1500, 0, 1);
            numS1Speed.Font = new Font("微軟正黑體", 12f, FontStyle.Bold);
            numS1Speed.ValueChanged += (s, e) => OnDutySpeedNudge(numS1Speed, numS1Speed.Value);

            Label lS1Trq = new Label() { Text = "額定轉矩(Nm):", AutoSize = true, Font = new Font("微軟正黑體", 11.5f, FontStyle.Bold), Margin = new Padding(18, 7, 4, 0) };
            numS1Torque = CreateNumericUpDown(Point.Empty, 105, 0, 1000, 15, 1, 0.1m);
            numS1Torque.Font = new Font("微軟正黑體", 12f, FontStyle.Bold);
            numS1Torque.ValueChanged += (s, e) => OnDutyTorqueNudge(numS1Torque, numS1Torque.Value);
            flpS1MainParams.Controls.AddRange(new Control[] { lS1Spd, numS1Speed, lS1Trq, numS1Torque });

            // Row 1: 勾選項並排
            FlowLayoutPanel flpS1Checks = new FlowLayoutPanel() { Dock = DockStyle.Fill, AutoSize = true, Margin = new Padding(0) };
            chkDutySlipComp = new CheckBox() { Text = "自動補償轉差(鎖定額定轉速)", AutoSize = true, Font = new Font("微軟正黑體", 11f, FontStyle.Bold), Checked = true, Margin = new Padding(0, 4, 16, 0) };
            chkDutySlipComp.CheckedChanged += (s, e) => {
                chkDutySlipComp.Text = chkDutySlipComp.Checked ? "自動補償轉差(鎖定額定轉速)" : "不補轉差 (恆定驅動器頻率)";
                UpdateDutyPreflightSummary();
            };
            chkS1ThermalStop = new CheckBox() { Text = "啟用 30分溫差<1.0℃ 熱平衡自動停機", AutoSize = true, Font = new Font("微軟正黑體", 11f, FontStyle.Bold), Checked = true, Margin = new Padding(0, 4, 0, 0) };
            chkS1ThermalStop.CheckedChanged += (s, e) => {
                if (btnS1SelectChannels != null) btnS1SelectChannels.Enabled = chkS1ThermalStop.Checked;
                UpdateDutyPreflightSummary();
            };
            flpS1Checks.Controls.AddRange(new Control[] { chkDutySlipComp, chkS1ThermalStop });

            // Row 2: 監控通道選取
            FlowLayoutPanel flpS1Ch = new FlowLayoutPanel() { Dock = DockStyle.Fill, AutoSize = true, Margin = new Padding(0) };
            Label lS1Ch = new Label() { Text = "溫度監控通道:", AutoSize = true, Font = new Font("微軟正黑體", 11.5f, FontStyle.Bold), Margin = new Padding(0, 7, 4, 0) };
            btnS1SelectChannels = new Button() { Text = " 選擇監控通道...", Size = new Size(150, 32), BackColor = Color.FromArgb(241, 245, 249), Font = new Font("微軟正黑體", 10.5f, FontStyle.Bold) };
            btnS1SelectChannels.Click += (s, e) => { ShowS1ChannelSelectDialog(); };
            lblS1SelectedChHint = new Label() { Text = "(已選 CH1~4，共 4 通道)", AutoSize = true, ForeColor = Color.FromArgb(70, 80, 95), Font = new Font("微軟正黑體", 10.5f, FontStyle.Bold), Margin = new Padding(8, 7, 0, 0) };
            flpS1Ch.Controls.AddRange(new Control[] { lS1Ch, btnS1SelectChannels, lblS1SelectedChHint });

            // Row 3: 說明卡片
            Label lS1Desc = new Label()
            {
                Text = "【S1 額定連續運轉說明】\n" +
                       "• 未啟動時：可直接點擊數字輸入修改目標轉速與轉矩。\n" +
                       "• 運轉進行中：輸入格自動鎖定，改由上下鍵以最小量 (轉速 1 rpm / 轉矩 0.1 Nm) 即時動態微調。\n" +
                       "• 系統自動追蹤轉差並監控 30 分鐘溫差，達到平衡後自動停機或進入下階段冷卻。",
                Dock = DockStyle.Fill,
                Font = new Font("微軟正黑體", 10.5f),
                ForeColor = Color.FromArgb(71, 85, 105),
                BorderStyle = BorderStyle.FixedSingle,
                Padding = new Padding(8),
                BackColor = Color.FromArgb(241, 245, 249)
            };

            tlpS1.Controls.Add(flpS1MainParams, 0, 0);
            tlpS1.Controls.Add(flpS1Checks, 0, 1);
            tlpS1.Controls.Add(flpS1Ch, 0, 2);
            tlpS1.Controls.Add(lS1Desc, 0, 3);
            tabDutyS1.Controls.Add(tlpS1);

            // -------------------------------------------------------------
            // 分頁 3: 【S2 短時過載設定】
            // -------------------------------------------------------------
            TableLayoutPanel tlpS2 = new TableLayoutPanel()
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 4,
                AutoScroll = true,
                Padding = new Padding(10),
                BackColor = Color.FromArgb(248, 250, 252)
            };
            tlpS2.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            tlpS2.RowStyles.Add(new RowStyle(SizeType.Absolute, 42f));
            tlpS2.RowStyles.Add(new RowStyle(SizeType.Absolute, 42f));
            tlpS2.RowStyles.Add(new RowStyle(SizeType.Absolute, 42f));
            tlpS2.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

            // Row 0: 額定轉速 + 極限轉矩 + 時長(分鐘) 橫向排列！
            FlowLayoutPanel flpS2MainParams = new FlowLayoutPanel() { Dock = DockStyle.Fill, AutoSize = true, Margin = new Padding(0) };
            Label lS2Spd = new Label() { Text = "額定轉速(rpm):", AutoSize = true, Font = new Font("微軟正黑體", 11.5f, FontStyle.Bold), Margin = new Padding(0, 7, 4, 0) };
            numS2Speed = CreateNumericUpDown(Point.Empty, 105, 0, 6000, 1500, 0, 1);
            numS2Speed.Font = new Font("微軟正黑體", 12f, FontStyle.Bold);
            numS2Speed.ValueChanged += (s, e) => OnDutySpeedNudge(numS2Speed, numS2Speed.Value);

            Label lS2Trq = new Label() { Text = "極限轉矩(Nm):", AutoSize = true, Font = new Font("微軟正黑體", 11.5f, FontStyle.Bold), Margin = new Padding(14, 7, 4, 0) };
            numS2Torque = CreateNumericUpDown(Point.Empty, 95, 0, 1000, 15, 1, 0.1m);
            numS2Torque.Font = new Font("微軟正黑體", 12f, FontStyle.Bold);
            numS2Torque.ValueChanged += (s, e) => OnDutyTorqueNudge(numS2Torque, numS2Torque.Value);

            Label lS2Dur = new Label() { Text = "時長(分鐘):", AutoSize = true, Font = new Font("微軟正黑體", 11.5f, FontStyle.Bold), Margin = new Padding(14, 7, 4, 0) };
            numS2DurationMin = CreateNumericUpDown(Point.Empty, 80, 1, 300, 30, 0, 1);
            numS2DurationMin.Font = new Font("微軟正黑體", 12f, FontStyle.Bold);
            numS2DurationMin.ValueChanged += (s, e) => UpdateDutyPreflightSummary();

            flpS2MainParams.Controls.AddRange(new Control[] { lS2Spd, numS2Speed, lS2Trq, numS2Torque, lS2Dur, numS2DurationMin });

            // Row 1: 溫度保護與實測
            FlowLayoutPanel flpS2Protect = new FlowLayoutPanel() { Dock = DockStyle.Fill, AutoSize = true, Margin = new Padding(0) };
            chkS2TempStop = new CheckBox() { Text = "超溫停機", AutoSize = true, Font = new Font("微軟正黑體", 11f, FontStyle.Bold), Checked = true, Margin = new Padding(0, 5, 6, 0) };
            Label lS2Ch = new Label() { Text = "通道:", AutoSize = true, Font = new Font("微軟正黑體", 11f, FontStyle.Bold), Margin = new Padding(0, 6, 2, 0) };
            cmbS2TempCh = new ComboBox() { Width = 95, DropDownStyle = ComboBoxStyle.DropDownList, Font = new Font("微軟正黑體", 10.5f, FontStyle.Bold) };
            for (int i = 0; i < 20; i++)
            {
                string cName = (gl820ChannelNames != null && i < gl820ChannelNames.Length && !string.IsNullOrEmpty(gl820ChannelNames[i])) ? gl820ChannelNames[i] : ("CH" + (i + 1));
                cmbS2TempCh.Items.Add(string.Format("CH{0} ({1})", i + 1, cName));
            }
            if (cmbS2TempCh.Items.Count > 0) cmbS2TempCh.SelectedIndex = 0;
            Label lS2Thresh = new Label() { Text = "閥值(℃):", AutoSize = true, Font = new Font("微軟正黑體", 11f, FontStyle.Bold), Margin = new Padding(8, 6, 2, 0) };
            numS2TempThreshold = CreateNumericUpDown(Point.Empty, 75, 20, 150, 80, 1, 0.5m);
            numS2TempThreshold.Font = new Font("微軟正黑體", 11f, FontStyle.Bold);
            numS2TempThreshold.ValueChanged += (s, e) => UpdateDutyPreflightSummary();
            lblS2TempRealtime = new Label() { Text = "實測: --.- ℃", AutoSize = true, Font = new Font("微軟正黑體", 11f, FontStyle.Bold), ForeColor = Color.FromArgb(30, 64, 175), Margin = new Padding(8, 6, 0, 0) };

            chkS2TempStop.CheckedChanged += (s, e) => {
                cmbS2TempCh.Enabled = chkS2TempStop.Checked;
                numS2TempThreshold.Enabled = chkS2TempStop.Checked;
                UpdateDutyPreflightSummary();
            };
            flpS2Protect.Controls.AddRange(new Control[] { chkS2TempStop, lS2Ch, cmbS2TempCh, lS2Thresh, numS2TempThreshold, lblS2TempRealtime });

            // Row 2: S2 定錨參數
            FlowLayoutPanel flpS2Anchor = new FlowLayoutPanel() { Dock = DockStyle.Fill, AutoSize = true, Margin = new Padding(0) };
            Label lS2AnchorTitle = new Label() { Text = "S2 定錨:", AutoSize = true, Font = new Font("微軟正黑體", 11f, FontStyle.Bold), ForeColor = Color.FromArgb(30, 64, 175), Margin = new Padding(0, 6, 4, 0) };
            lblS2AnchorSy52 = new Label() { Text = "SY52:", AutoSize = true, Font = new Font("微軟正黑體", 10.5f, FontStyle.Bold), Margin = new Padding(0, 6, 2, 0) };
            numS2AnchorSy52 = CreateNumericUpDown(Point.Empty, 80, 0, 6000, s2AnchorSy52, 0, 1);
            numS2AnchorSy52.Font = new Font("微軟正黑體", 11f, FontStyle.Bold);
            numS2AnchorSy52.ValueChanged += (s, e) => {
                s2AnchorSy52 = (int)numS2AnchorSy52.Value;
                if (isDutyRunning)
                {
                    int dutyRole = (cmbDutyRole != null && cmbDutyRole.SelectedIndex >= 0) ? cmbDutyRole.SelectedIndex : 1;
                    int spdDrive = (dutyRole == 1) ? 2 : 1;
                    int spdCom = GetHmiKebComIdx(spdDrive), spdBaud = GetHmiKebBaudIdx(spdDrive), spdNode = (spdDrive == 1) ? (int)numHmiKebNode1.Value : (int)numHmiKebNode2.Value;
                    KebWriteParam32(spdCom, spdBaud, spdNode, 0x0034, s2AnchorSy52, "S2動態微調SY52");
                }
                UpdateDutyPreflightSummary();
            };

            lblS2AnchorCs18 = new Label() { Text = "CS18:", AutoSize = true, Font = new Font("微軟正黑體", 10.5f, FontStyle.Bold), Margin = new Padding(6, 6, 2, 0) };
            numS2AnchorCs18 = CreateNumericUpDown(Point.Empty, 75, 0, 1000, s2AnchorCs18, 0, 1);
            numS2AnchorCs18.Font = new Font("微軟正黑體", 11f, FontStyle.Bold);
            numS2AnchorCs18.ValueChanged += (s, e) => {
                s2AnchorCs18 = (int)numS2AnchorCs18.Value;
                if (isDutyRunning) OnDutyCs18Nudge(s2AnchorCs18);
                UpdateDutyPreflightSummary();
            };

            btnS2RecordAnchor = new Button() { Text = " 記定錨", Size = new Size(80, 30), BackColor = Color.FromArgb(16, 185, 129), ForeColor = Color.White, Font = new Font("微軟正黑體", 10f, FontStyle.Bold), Margin = new Padding(6, 2, 0, 0) };
            btnS2RecordAnchor.Click += (s, e) => { CaptureCurrentAnchorToS2(); UpdateDutyPreflightSummary(); };

            btnS2ResetAnchor = new Button() { Text = " 重設", Size = new Size(65, 30), BackColor = Color.FromArgb(239, 68, 68), ForeColor = Color.White, Font = new Font("微軟正黑體", 10f, FontStyle.Bold), Margin = new Padding(6, 2, 0, 0) };
            btnS2ResetAnchor.Click += (s, e) => { ResetS2Anchor(); UpdateDutyPreflightSummary(); };

            flpS2Anchor.Controls.AddRange(new Control[] { lS2AnchorTitle, lblS2AnchorSy52, numS2AnchorSy52, lblS2AnchorCs18, numS2AnchorCs18, btnS2RecordAnchor, btnS2ResetAnchor });

            // Row 3: 說明卡片
            Label lS2Desc = new Label()
            {
                Text = "【S2 短時過載測試說明】\n" +
                       "• 未啟動時：可直接點擊輸入框修改目標轉速、極限轉矩與時長。\n" +
                       "• 運轉進行中：輸入格自動鎖定保護，可使用上下鍵以最小量 (轉速 1 rpm / 轉矩 0.1 Nm / CS18 0.1%) 即時動態微調。\n" +
                       "• 錨點 (SY52/CS18) 若為 0 系統自動啟動自適應定錨，已有數據則直接套用運轉，達到時限或超溫即自動平滑卸載停機。",
                Dock = DockStyle.Fill,
                Font = new Font("微軟正黑體", 10.5f),
                ForeColor = Color.FromArgb(71, 85, 105),
                BorderStyle = BorderStyle.FixedSingle,
                Padding = new Padding(8),
                BackColor = Color.FromArgb(241, 245, 249)
            };

            tlpS2.Controls.Add(flpS2MainParams, 0, 0);
            tlpS2.Controls.Add(flpS2Protect, 0, 1);
            tlpS2.Controls.Add(flpS2Anchor, 0, 2);
            tlpS2.Controls.Add(lS2Desc, 0, 3);
            tabDutyS2.Controls.Add(tlpS2);

            // -------------------------------------------------------------
            // 分頁 4: 【S6 週期負載設定】
            // -------------------------------------------------------------
            TableLayoutPanel tlpS6 = new TableLayoutPanel()
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 5,
                AutoScroll = true,
                Padding = new Padding(10),
                BackColor = Color.FromArgb(248, 250, 252)
            };
            tlpS6.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            tlpS6.RowStyles.Add(new RowStyle(SizeType.Absolute, 42f));
            tlpS6.RowStyles.Add(new RowStyle(SizeType.Absolute, 42f));
            tlpS6.RowStyles.Add(new RowStyle(SizeType.Absolute, 44f));
            tlpS6.RowStyles.Add(new RowStyle(SizeType.Absolute, 28f));
            tlpS6.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

            // Row 0: 運轉轉速 + 加載轉矩 + 最高溫通道 (排在右側同 S2 時長位置！)
            FlowLayoutPanel flpS6MainParams = new FlowLayoutPanel() { Dock = DockStyle.Fill, AutoSize = true, Margin = new Padding(0) };
            Label lS6Spd = new Label() { Text = "運轉轉速(rpm):", AutoSize = true, Font = new Font("微軟正黑體", 11.5f, FontStyle.Bold), Margin = new Padding(0, 7, 4, 0) };
            numS6Speed = CreateNumericUpDown(Point.Empty, 105, 0, 6000, 1500, 0, 1);
            numS6Speed.Font = new Font("微軟正黑體", 12f, FontStyle.Bold);
            numS6Speed.ValueChanged += (s, e) => OnDutySpeedNudge(numS6Speed, numS6Speed.Value);

            Label lS6Trq = new Label() { Text = "加載轉矩(Nm):", AutoSize = true, Font = new Font("微軟正黑體", 11.5f, FontStyle.Bold), Margin = new Padding(14, 7, 4, 0) };
            numS6Torque = CreateNumericUpDown(Point.Empty, 95, 0, 1000, 15, 1, 0.1m);
            numS6Torque.Font = new Font("微軟正黑體", 12f, FontStyle.Bold);
            numS6Torque.ValueChanged += (s, e) => OnDutyTorqueNudge(numS6Torque, numS6Torque.Value);

            Label lS6Ch = new Label() { Text = "最高溫通道:", AutoSize = true, Font = new Font("微軟正黑體", 11.5f, FontStyle.Bold), Margin = new Padding(14, 7, 4, 0) };
            cmbS6TempCh = new ComboBox() { Width = 95, DropDownStyle = ComboBoxStyle.DropDownList, Font = new Font("微軟正黑體", 11f, FontStyle.Bold), Margin = new Padding(0, 5, 0, 0) };
            for (int i = 1; i <= 20; i++) cmbS6TempCh.Items.Add("CH " + i);
            cmbS6TempCh.SelectedIndex = 0;
            cmbS6TempCh.SelectedIndexChanged += (s, e) => {
                if (lblDutyTempTrendTitle != null)
                    lblDutyTempTrendTitle.Text = string.Format("當前監控通道: S6 通道 {0} (每10分鐘最高溫熱平衡分析)", cmbS6TempCh.SelectedIndex + 1);
            };
            lblS6TempChHint = new Label() { Text = "(熱平衡通道)", AutoSize = true, ForeColor = Color.FromArgb(70, 80, 95), Font = new Font("微軟正黑體", 10.5f, FontStyle.Bold), Margin = new Padding(4, 7, 0, 0) };

            flpS6MainParams.Controls.AddRange(new Control[] { lS6Spd, numS6Speed, lS6Trq, numS6Torque, lS6Ch, cmbS6TempCh, lblS6TempChHint });

            // Row 1: 週期與 ED% 設定
            FlowLayoutPanel flpS6Cyc = new FlowLayoutPanel() { Dock = DockStyle.Fill, AutoSize = true, Margin = new Padding(0) };
            lblS6CycleLabel = new Label() { Text = "週期T(分):", AutoSize = true, Font = new Font("微軟正黑體", 11f, FontStyle.Bold), Margin = new Padding(0, 6, 2, 0) };
            numS6CycleMin = CreateNumericUpDown(Point.Empty, 70, 1, 60, 10, 1, 0.5m);
            numS6CycleMin.Font = new Font("微軟正黑體", 11f, FontStyle.Bold);
            numS6CycleMin.ValueChanged += (s, e) => {
                if (!isSyncingDutyControls && numDutyMiniCycleMin != null && numDutyMiniCycleMin.Value != numS6CycleMin.Value)
                {
                    isSyncingDutyControls = true;
                    numDutyMiniCycleMin.Value = numS6CycleMin.Value;
                    isSyncingDutyControls = false;
                }
                UpdateS6CalcInfo();
                UpdateDutyPreflightSummary();
            };

            lblS6EdLabel = new Label() { Text = "ED%:", AutoSize = true, Font = new Font("微軟正黑體", 11f, FontStyle.Bold), Margin = new Padding(8, 6, 2, 0) };
            numS6Ed = CreateNumericUpDown(Point.Empty, 65, 1, 100, 40, 0, 1);
            numS6Ed.Font = new Font("微軟正黑體", 11f, FontStyle.Bold);
            numS6Ed.ValueChanged += (s, e) => {
                if (!isSyncingDutyControls && numDutyMiniEd != null && numDutyMiniEd.Value != numS6Ed.Value)
                {
                    isSyncingDutyControls = true;
                    numDutyMiniEd.Value = numS6Ed.Value;
                    isSyncingDutyControls = false;
                }
                UpdateS6CalcInfo();
                UpdateDutyPreflightSummary();
            };

            lblS6CyclesLabel = new Label() { Text = "循環數:", AutoSize = true, Font = new Font("微軟正黑體", 11f, FontStyle.Bold), Margin = new Padding(8, 6, 2, 0) };
            numS6Cycles = CreateNumericUpDown(Point.Empty, 60, 1, 50, 3, 0, 1);
            numS6Cycles.Font = new Font("微軟正黑體", 11f, FontStyle.Bold);
            numS6Cycles.ValueChanged += (s, e) => {
                if (!isSyncingDutyControls && numDutyMiniCycles != null && numDutyMiniCycles.Value != numS6Cycles.Value)
                {
                    isSyncingDutyControls = true;
                    numDutyMiniCycles.Value = numS6Cycles.Value;
                    isSyncingDutyControls = false;
                }
                UpdateDutyPreflightSummary();
            };

            lblS6CalcInfo = new Label() { Text = "有載T1=4.0m, 空載T2=6.0m", AutoSize = true, ForeColor = Color.FromArgb(3, 105, 161), Font = new Font("微軟正黑體", 11f, FontStyle.Bold), Margin = new Padding(10, 6, 0, 0) };
            flpS6Cyc.Controls.AddRange(new Control[] { lblS6CycleLabel, numS6CycleMin, lblS6EdLabel, numS6Ed, lblS6CyclesLabel, numS6Cycles, lblS6CalcInfo });

            // Row 2: S6 雙定錨
            FlowLayoutPanel flpS6Anchor = new FlowLayoutPanel() { Dock = DockStyle.Fill, AutoSize = true, Margin = new Padding(0) };
            lblS6NoLoadTitle = new Label() { Text = "空載SY52:", AutoSize = true, Font = new Font("微軟正黑體", 10.5f, FontStyle.Bold), Margin = new Padding(0, 6, 2, 0) };
            numS6AnchorNoLoadSpd = CreateNumericUpDown(Point.Empty, 75, 0, 6000, 0, 0, 1);
            numS6AnchorNoLoadSpd.Font = new Font("微軟正黑體", 10.5f, FontStyle.Bold);
            numS6AnchorNoLoadSpd.ValueChanged += (s, e) => {
                s6AnchorNoLoadSpeed = (double)numS6AnchorNoLoadSpd.Value;
                s6HasNoLoadAnchor = (s6AnchorNoLoadSpeed > 0);
                UpdateS6AnchorStatusText();
                UpdateDutyPreflightSummary();
            };
            btnS6AnchorNoLoad = new Button() { Text = " 記空載(V)", Size = new Size(88, 30), BackColor = Color.FromArgb(14, 165, 233), ForeColor = Color.White, Font = new Font("微軟正黑體", 9.5f, FontStyle.Bold), Margin = new Padding(4, 2, 8, 0) };
            btnS6AnchorNoLoad.Click += (s, e) => { CaptureCurrentAnchorToS6NoLoad(); UpdateDutyPreflightSummary(); };

            lblS6LoadedTitle = new Label() { Text = "加載SY52:", AutoSize = true, Font = new Font("微軟正黑體", 10.5f, FontStyle.Bold), Margin = new Padding(0, 6, 2, 0) };
            numS6AnchorLoadedSpd = CreateNumericUpDown(Point.Empty, 75, 0, 6000, 0, 0, 1);
            numS6AnchorLoadedSpd.Font = new Font("微軟正黑體", 10.5f, FontStyle.Bold);
            numS6AnchorLoadedSpd.ValueChanged += (s, e) => {
                s6AnchorLoadedSpeed = (double)numS6AnchorLoadedSpd.Value;
                s6HasLoadedAnchor = (s6AnchorLoadedSpeed > 0 && s6AnchorLoadedTorquePct > 0);
                if (isDutyRunning)
                {
                    int dutyRole = (cmbDutyRole != null && cmbDutyRole.SelectedIndex >= 0) ? cmbDutyRole.SelectedIndex : 1;
                    int spdDrive = (dutyRole == 1) ? 2 : 1;
                    int spdCom = GetHmiKebComIdx(spdDrive), spdBaud = GetHmiKebBaudIdx(spdDrive), spdNode = (spdDrive == 1) ? (int)numHmiKebNode1.Value : (int)numHmiKebNode2.Value;
                    KebWriteParam32(spdCom, spdBaud, spdNode, 0x0034, (int)s6AnchorLoadedSpeed, "S6動態微調加載SY52");
                }
                UpdateS6AnchorStatusText();
                UpdateDutyPreflightSummary();
            };

            lblS6Cs18Title = new Label() { Text = "CS18:", AutoSize = true, Font = new Font("微軟正黑體", 10.5f, FontStyle.Bold), Margin = new Padding(6, 6, 2, 0) };
            numS6AnchorLoadedCs18 = CreateNumericUpDown(Point.Empty, 70, 0, 1000, 0, 0, 1);
            numS6AnchorLoadedCs18.Font = new Font("微軟正黑體", 10.5f, FontStyle.Bold);
            numS6AnchorLoadedCs18.ValueChanged += (s, e) => {
                s6AnchorLoadedTorquePct = (double)numS6AnchorLoadedCs18.Value / 10.0;
                s6HasLoadedAnchor = (s6AnchorLoadedSpeed > 0 && s6AnchorLoadedTorquePct > 0);
                if (isDutyRunning) OnDutyCs18Nudge((int)numS6AnchorLoadedCs18.Value);
                UpdateS6AnchorStatusText();
                UpdateDutyPreflightSummary();
            };

            btnS6AnchorLoaded = new Button() { Text = " 記加載(N)", Size = new Size(88, 30), BackColor = Color.FromArgb(139, 92, 246), ForeColor = Color.White, Font = new Font("微軟正黑體", 9.5f, FontStyle.Bold), Margin = new Padding(4, 2, 4, 0) };
            btnS6AnchorLoaded.Click += (s, e) => { CaptureCurrentAnchorToS6Loaded(); UpdateDutyPreflightSummary(); };

            btnS6ResetAnchor = new Button() { Text = " 重設", Size = new Size(65, 30), BackColor = Color.FromArgb(239, 68, 68), ForeColor = Color.White, Font = new Font("微軟正黑體", 9.5f, FontStyle.Bold), Margin = new Padding(4, 2, 0, 0) };
            btnS6ResetAnchor.Click += (s, e) => { ResetS6Anchors(); UpdateDutyPreflightSummary(); };

            flpS6Anchor.Controls.AddRange(new Control[] { lblS6NoLoadTitle, numS6AnchorNoLoadSpd, btnS6AnchorNoLoad, lblS6LoadedTitle, numS6AnchorLoadedSpd, lblS6Cs18Title, numS6AnchorLoadedCs18, btnS6AnchorLoaded, btnS6ResetAnchor });

            // Row 3: 定錨狀態
            lblS6AnchorStatus = new Label() { Text = "定錨狀態: 空載=未設定 | 加載=未設定", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, ForeColor = Color.DarkOrange, Font = new Font("微軟正黑體", 11f, FontStyle.Bold) };

            // Row 4: 說明卡片
            Label lS6Desc = new Label()
            {
                Text = "【S6 週期負載測試說明】\n" +
                       "• 未啟動時：可直接點擊數字輸入修改轉速、加載轉矩、週期 T、ED% 與循環數。\n" +
                       "• 運轉進行中：輸入格自動鎖定保護，可使用上下鍵以最小量 (轉速 1 rpm / 轉矩 0.1 Nm / CS18 0.1%) 即時動態微調。\n" +
                       "• 具備空載與加載雙重錨點，若未設定則第一循環自動試運轉採集，後續循環直接快速套用。",
                Dock = DockStyle.Fill,
                Font = new Font("微軟正黑體", 10.5f),
                ForeColor = Color.FromArgb(71, 85, 105),
                BorderStyle = BorderStyle.FixedSingle,
                Padding = new Padding(8),
                BackColor = Color.FromArgb(241, 245, 249)
            };

            tlpS6.Controls.Add(flpS6MainParams, 0, 0);
            tlpS6.Controls.Add(flpS6Cyc, 0, 1);
            tlpS6.Controls.Add(flpS6Anchor, 0, 2);
            tlpS6.Controls.Add(lblS6AnchorStatus, 0, 3);
            tlpS6.Controls.Add(lS6Desc, 0, 4);
            tabDutyS6.Controls.Add(tlpS6);

            // -------------------------------------------------------------
            // 底部操作列與狀態行 (永遠置底可見，單一入口防呆)
            // -------------------------------------------------------------
            TableLayoutPanel pnlBottomBar = new TableLayoutPanel()
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 5,
                AutoSize = true,
                BackColor = Color.FromArgb(248, 250, 252),
                Padding = new Padding(4)
            };
            pnlBottomBar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

            FlowLayoutPanel flpBtns = new FlowLayoutPanel() { Dock = DockStyle.Fill, AutoSize = true, Margin = new Padding(0) };
            btnStartDuty = new Button() { Text = "[排程] 開始連續測試", Size = new Size(165, 40), BackColor = Color.FromArgb(0, 180, 216), ForeColor = Color.White, Font = new Font("微軟正黑體", 12.5f, FontStyle.Bold) };
            btnStopDuty = new Button() { Text = "[■] 停止", Size = new Size(90, 40), Enabled = false, Font = new Font("微軟正黑體", 12.5f, FontStyle.Bold), ForeColor = Color.Red };
            btnExportDuty = new Button() { Text = "[儲存] 匯出報表", Size = new Size(120, 40), BackColor = Color.FromArgb(16, 185, 129), ForeColor = Color.White, Font = new Font("微軟正黑體", 12f, FontStyle.Bold) };
            btnDutyExtrapolate = new Button() { Text = "[推估] 極限能力回推", Size = new Size(150, 40), BackColor = Color.FromArgb(79, 70, 229), ForeColor = Color.White, Font = new Font("微軟正黑體", 11.5f, FontStyle.Bold) };
            prgDuty = new ProgressBar() { Size = new Size(160, 26), Margin = new Padding(8, 7, 0, 0) };

            btnStartDuty.Click += BtnStartDuty_Click;
            btnStopDuty.Click += (s, e) => { StopDutyTest(); };
            btnExportDuty.Click += BtnExportDuty_Click;
            btnDutyExtrapolate.Click += (s, e) => { ShowDutyExtrapolationDialog(); };

            flpBtns.Controls.AddRange(new Control[] { btnStartDuty, btnStopDuty, btnExportDuty, btnDutyExtrapolate, prgDuty });
            pnlBottomBar.Controls.Add(flpBtns, 0, 0);

            lblDutyStatus = new Label() { Text = "狀態: 待命準備中", Dock = DockStyle.Fill, AutoSize = true, Font = new Font("微軟正黑體", 12.5f, FontStyle.Bold), ForeColor = Color.FromArgb(15, 23, 42), Margin = new Padding(4, 2, 4, 1) };
            lblDutyPhaseAction = new Label() { Text = "動作: --", Dock = DockStyle.Fill, AutoSize = true, ForeColor = Color.FromArgb(2, 132, 199), Font = new Font("微軟正黑體", 11.5f, FontStyle.Bold), Margin = new Padding(4, 1, 4, 1) };
            lblThermalStatus = new Label() { Text = "熱平衡: 未達平衡", Dock = DockStyle.Fill, AutoSize = true, ForeColor = Color.DarkOrange, Font = new Font("微軟正黑體", 11.5f, FontStyle.Bold), Margin = new Padding(4, 1, 4, 1) };
            lblExtrapolationSummary = new Label() { Text = "極限推估: 待命 (S2/S6 測試完成後自動依溫升回推 105℃ 極限扭矩與 DUTY 佔比)", Dock = DockStyle.Fill, AutoSize = true, ForeColor = Color.FromArgb(67, 56, 202), Font = new Font("微軟正黑體", 11f, FontStyle.Bold), Margin = new Padding(4, 1, 4, 1) };
            lblDutyAbStatus = new Label() { Text = "【AB載台現況】待命準備中", Dock = DockStyle.Fill, AutoSize = true, ForeColor = Color.FromArgb(3, 105, 161), Font = new Font("微軟正黑體", 11f, FontStyle.Bold), Margin = new Padding(4, 1, 4, 1) };

            pnlBottomBar.Controls.Add(lblDutyStatus, 0, 1);
            pnlBottomBar.Controls.Add(lblDutyPhaseAction, 0, 2);
            // pnlBottomBar.Controls.Add(lblThermalStatus, 0, 3); // 移除重複顯示之熱平衡標籤，權威狀態唯一保留於右側 grpDutyTemp 頂部
            pnlBottomBar.Controls.Add(lblExtrapolationSummary, 0, 4);

            tlpLeft.Controls.Add(tabDutyControl, 0, 0);
            tlpLeft.Controls.Add(pnlBottomBar, 0, 1);
            grp.Controls.Add(tlpLeft);
            splitDutyTop.Panel1.Controls.Add(grp);

            // 保持背景相容變數之初始化
            numDutySpeed = CreateNumericUpDown(Point.Empty, 105, 0, 4000, 1000, 0, 1);
            numDutyTorque = CreateNumericUpDown(Point.Empty, 95, 0, 500, 15, 1, 0.1m);
            cmbDutyMode = new ComboBox();
            cmbDutyMode.Items.AddRange(new object[] { "S1 連續工作制 (30分熱平衡判定)", "S2 短時工作制 (錨點測試/超溫停機)", "S6 週期工作制 (Periodic ED%)" });
            cmbDutyMode.SelectedIndex = 2;
            chkBatchDutyMode = new CheckBox() { Checked = true };

            cmbDutyMainMode.SelectedIndexChanged += (s, e) => {
                int sel = cmbDutyMainMode.SelectedIndex;
                if (sel == 0)
                {
                    chkBatchDutyMode.Checked = true;
                    cmbBatchDutyPlan.Enabled = true;
                    cmbBatchCoolingMode.Enabled = true;
                    btnStartDuty.Text = "[排程] 開始連續測試";
                }
                else
                {
                    chkBatchDutyMode.Checked = false;
                    cmbBatchDutyPlan.Enabled = false;
                    cmbBatchCoolingMode.Enabled = false;
                    btnStartDuty.Text = "[>] 開始工作制測試";
                    if (sel == 1) cmbDutyMode.SelectedIndex = 0;
                    else if (sel == 2) cmbDutyMode.SelectedIndex = 1;
                    else if (sel == 3) cmbDutyMode.SelectedIndex = 2;
                }
                UpdateDutyPreflightSummary();
            };

            tabDutyControl.SelectedIndexChanged += (s, e) => {
                if (!isDutyRunning && cmbDutyMainMode.SelectedIndex != 0)
                {
                    if (tabDutyControl.SelectedTab == tabDutyS1) cmbDutyMainMode.SelectedIndex = 1;
                    else if (tabDutyControl.SelectedTab == tabDutyS2) cmbDutyMainMode.SelectedIndex = 2;
                    else if (tabDutyControl.SelectedTab == tabDutyS6) cmbDutyMainMode.SelectedIndex = 3;
                }
                if (tabDutyControl.SelectedTab == tabDutyMain)
                {
                    UpdateDutyPreflightSummary();
                }
            };

            UpdateDutyPreflightSummary();


            // -------------------------------------------------------------
            // 右側：專屬溫度監控與即時動態波形 (S1/S2/S6 模式自適應，含 S6 熱平衡與兩段超溫監控)
            // -------------------------------------------------------------
            grpDutyTemp = new GroupBox()
            {
                Text = " 專屬溫度監控與即時動態波形",
                Dock = DockStyle.Fill,
                Font = new Font("微軟正黑體", 13f, FontStyle.Bold),
                BackColor = Color.FromArgb(248, 250, 252)
            };

            Panel pnlTempHeader = new Panel() { Dock = DockStyle.Top, Height = 76, BackColor = Color.FromArgb(241, 245, 249), Padding = new Padding(4) };
            lblDutyTempTrendTitle = new Label()
            {
                Text = "目前監控: S1 多通道最高溫 (熱平衡判定: 30min溫差<1.0℃)",
                Location = new Point(8, 6),
                AutoSize = true,
                Font = new Font("微軟正黑體", 11.5f, FontStyle.Bold),
                ForeColor = Color.FromArgb(15, 23, 42)
            };
            lblDutyTempRealtimeVal = new Label()
            {
                Text = "實測最高: CH1 --.- ℃",
                Location = new Point(8, 28),
                AutoSize = true,
                Font = new Font("微軟正黑體", 13.5f, FontStyle.Bold),
                ForeColor = Color.FromArgb(30, 64, 175)
            };
            lblS1ThermalStatus = new Label()
            {
                Text = "S1 溫升斜率: 採樣中 | 預估熱平衡: 採樣中...",
                Location = new Point(220, 30),
                AutoSize = true,
                Font = new Font("微軟正黑體", 11.5f, FontStyle.Bold),
                ForeColor = Color.DarkOrange,
                Visible = false
            };
            lblS2ThermalStatus = new Label()
            {
                Text = "S2 溫升斜率: 採樣中 | 設定時長預估終溫: 採樣中...",
                Location = new Point(220, 30),
                AutoSize = true,
                Font = new Font("微軟正黑體", 11.5f, FontStyle.Bold),
                ForeColor = Color.FromArgb(30, 64, 175),
                Visible = false
            };
            lblS6ThermalStatus = new Label()
            {
                Text = "S6 熱平衡: 監測中 (需連續 3 週期，達 30 分鐘峰值溫差 <= 1.0℃ 自動停機)",
                Location = new Point(220, 30),
                AutoSize = true,
                Font = new Font("微軟正黑體", 11.5f, FontStyle.Bold),
                ForeColor = Color.DarkOrange
            };
            lblDutyAllChTempsDisp = new Label()
            {
                Text = "選定通道即時值: 等待擷取...",
                Location = new Point(8, 52),
                AutoSize = true,
                Font = new Font("微軟正黑體", 10f, FontStyle.Regular),
                ForeColor = Color.FromArgb(71, 85, 105)
            };

            // 時間軸快捷控制列 (固定靠右對齊)
            Panel pnlDutyTimeSpan = new Panel()
            {
                Dock = DockStyle.Right,
                Width = 256,
                BackColor = Color.Transparent
            };
            Label lblDutyTimeTitle = new Label()
            {
                Text = "[時間] 範圍:",
                Location = new Point(4, 8),
                AutoSize = true,
                Font = new Font("微軟正黑體", 10.5f, FontStyle.Bold),
                ForeColor = Color.FromArgb(51, 65, 85)
            };
            Button btnDutyTimeMinus = new Button()
            {
                Text = "",
                Location = new Point(4, 34),
                Size = new Size(28, 30),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.White,
                Font = new Font("微軟正黑體", 9f),
                Cursor = Cursors.Hand
            };
            btnDutyTimeMinus.FlatAppearance.BorderColor = Color.FromArgb(203, 213, 225);

            cmbDutyTimeSpan = new ComboBox()
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Location = new Point(36, 36),
                Size = new Size(180, 26),
                Font = new Font("微軟正黑體", 10f, FontStyle.Bold),
                BackColor = Color.White
            };
            cmbDutyTimeSpan.Items.AddRange(GbdTemperatureTrendControl.TimeSpanNames);
            cmbDutyTimeSpan.SelectedIndex = (sharedTestTempTrend != null) ? sharedTestTempTrend.CurrentTimeSpanIndex : 3;

            Button btnDutyTimePlus = new Button()
            {
                Text = "",
                Location = new Point(220, 34),
                Size = new Size(28, 30),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.White,
                Font = new Font("微軟正黑體", 9f),
                Cursor = Cursors.Hand
            };
            btnDutyTimePlus.FlatAppearance.BorderColor = Color.FromArgb(203, 213, 225);

            cmbDutyTimeSpan.SelectedIndexChanged += (s, e) => {
                if (sharedTestTempTrend != null && cmbDutyTimeSpan.SelectedIndex >= 0)
                {
                    sharedTestTempTrend.CurrentTimeSpanIndex = cmbDutyTimeSpan.SelectedIndex;
                }
            };
            btnDutyTimeMinus.Click += (s, e) => {
                if (cmbDutyTimeSpan.SelectedIndex > 0) cmbDutyTimeSpan.SelectedIndex--;
            };
            btnDutyTimePlus.Click += (s, e) => {
                if (cmbDutyTimeSpan.SelectedIndex < cmbDutyTimeSpan.Items.Count - 1) cmbDutyTimeSpan.SelectedIndex++;
            };

            pnlDutyTimeSpan.Controls.AddRange(new Control[] { lblDutyTimeTitle, btnDutyTimeMinus, cmbDutyTimeSpan, btnDutyTimePlus });
            pnlTempHeader.Controls.Add(pnlDutyTimeSpan);
            pnlTempHeader.Controls.AddRange(new Control[] { lblDutyTempTrendTitle, lblDutyTempRealtimeVal, lblS1ThermalStatus, lblS2ThermalStatus, lblS6ThermalStatus, lblDutyAllChTempsDisp });

            // 專屬動態溫度曲線：與 TN / 空載 共用 sharedTestTempTrend，當使用者切換至此分頁時由 AttachSharedTempTrendTo 動態掛載

            // 底部 S6 兩段式超溫防護列
            pnlS6Overtemp = new Panel() { Dock = DockStyle.Bottom, Height = 48, BackColor = Color.FromArgb(254, 242, 242), Padding = new Padding(4) };
            Label lWarn = new Label() { Text = "[!] 警告門檻:", Location = new Point(6, 13), AutoSize = true, Font = new Font("微軟正黑體", 11f, FontStyle.Bold), ForeColor = Color.DarkOrange };
            numS6WarnTemp = CreateNumericUpDown(new Point(105, 9), 65, 30, 200, 90, 0, 1);
            numS6WarnTemp.Font = new Font("微軟正黑體", 11f, FontStyle.Bold);

            Label lTrip = new Label() { Text = "[停機] 停機門檻:", Location = new Point(175, 13), AutoSize = true, Font = new Font("微軟正黑體", 11f, FontStyle.Bold), ForeColor = Color.FromArgb(220, 38, 38) };
            numS6TripTemp = CreateNumericUpDown(new Point(275, 9), 65, 30, 200, 105, 0, 1);
            numS6TripTemp.Font = new Font("微軟正黑體", 11f, FontStyle.Bold);

            Label lAct = new Label() { Text = "處置:", Location = new Point(345, 13), AutoSize = true, Font = new Font("微軟正黑體", 11f, FontStyle.Bold) };
            cmbS6OvertempAction = new ComboBox() { Location = new Point(390, 9), Width = 180, DropDownStyle = ComboBoxStyle.DropDownList, Font = new Font("微軟正黑體", 10f) };
            cmbS6OvertempAction.Items.AddRange(new object[] { "1. 警示並立即停機保護", "2. 自動調降 ED% 散熱 [預留]", "3. 自動調降加載轉矩 [預留]" });
            cmbS6OvertempAction.SelectedIndex = 0;

            pnlS6Overtemp.Controls.AddRange(new Control[] { lWarn, numS6WarnTemp, lTrip, numS6TripTemp, lAct, cmbS6OvertempAction });

            // grpDutyTemp 內容由 AttachSharedTempTrendTo 動態掛載共用趨勢圖
            grpDutyTemp.Controls.Add(pnlTempHeader); // Top
            grpDutyTemp.Controls.Add(pnlS6Overtemp);  // Bottom

            splitDutyTop.Panel2.Controls.Add(grpDutyTemp);
            splitDuty.Panel1.Controls.Add(splitDutyTop);

            dgvDuty = new DataGridView()
            {
                Dock = DockStyle.Fill,
                BackgroundColor = Color.White,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                RowHeadersVisible = false,
                AllowUserToAddRows = false,
                RowTemplate = { Height = 32 },
                ColumnHeadersHeight = 38
            };
            dgvDuty.ColumnHeadersDefaultCellStyle.Font = new Font("微軟正黑體", 11.5f, FontStyle.Bold);
            dgvDuty.DefaultCellStyle.Font = new Font("微軟正黑體", 11f);
            dgvDuty.Columns.Add("StdTime", "標準時間");
            dgvDuty.Columns.Add("Elapsed", "經過時間");
            dgvDuty.Columns.Add("Phase", "階段");
            dgvDuty.Columns.Add("Speed", "轉速 (rpm)");
            dgvDuty.Columns.Add("Torque", "轉矩 (Nm)");
            dgvDuty.Columns.Add("Power", "輸出功率 (kW)");
            dgvDuty.Columns.Add("Volt", "平均電壓 (V)");
            dgvDuty.Columns.Add("Curr", "平均電流 (A)");
            dgvDuty.Columns.Add("Pelec", "輸入功率 (kW)");
            dgvDuty.Columns.Add("Pf", "功因");
            dgvDuty.Columns.Add("Eff", "效率 (%)");
            dgvDuty.Columns.Add("Temp", "馬達溫度 (°C)");
            dgvDuty.Columns.Add("Thermal", "熱平衡判定");

            dgvDuty.Columns[0].MinimumWidth = 95;  dgvDuty.Columns[0].FillWeight = 8;
            dgvDuty.Columns[1].MinimumWidth = 85;  dgvDuty.Columns[1].FillWeight = 7;
            dgvDuty.Columns[2].MinimumWidth = 70;  dgvDuty.Columns[2].FillWeight = 6;
            dgvDuty.Columns[3].MinimumWidth = 80;  dgvDuty.Columns[3].FillWeight = 7;
            dgvDuty.Columns[4].MinimumWidth = 80;  dgvDuty.Columns[4].FillWeight = 7;
            dgvDuty.Columns[5].MinimumWidth = 90;  dgvDuty.Columns[5].FillWeight = 8;
            dgvDuty.Columns[6].MinimumWidth = 85;  dgvDuty.Columns[6].FillWeight = 8;
            dgvDuty.Columns[7].MinimumWidth = 85;  dgvDuty.Columns[7].FillWeight = 8;
            dgvDuty.Columns[8].MinimumWidth = 90;  dgvDuty.Columns[8].FillWeight = 8;
            dgvDuty.Columns[9].MinimumWidth = 65;  dgvDuty.Columns[9].FillWeight = 6;
            dgvDuty.Columns[10].MinimumWidth = 70; dgvDuty.Columns[10].FillWeight = 6;
            dgvDuty.Columns[11].MinimumWidth = 100; dgvDuty.Columns[11].FillWeight = 9;
            dgvDuty.Columns[12].MinimumWidth = 180; dgvDuty.Columns[12].FillWeight = 20;

            ContextMenuStrip cmsDuty = new ContextMenuStrip();
            ToolStripMenuItem mnuAutoFit = new ToolStripMenuItem("自動適應內容寬度");
            mnuAutoFit.Click += (s, e) => {
                dgvDuty.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.AllCells;
            };
            ToolStripMenuItem mnuFillWidth = new ToolStripMenuItem("填滿視窗寬度");
            mnuFillWidth.Click += (s, e) => {
                dgvDuty.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            };
            ToolStripMenuItem mnuExportCsv = new ToolStripMenuItem("匯出表格紀錄 (CSV)");
            mnuExportCsv.Click += (s, e) => {
                try
                {
                    if (dgvDuty.Rows.Count == 0)
                    {
                        MessageBox.Show("目前表格尚無記錄可匯出！", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                        return;
                    }
                    string logDir = GetMotorDedicatedLogDirectory(motorModelName);
                    if (!Directory.Exists(logDir)) Directory.CreateDirectory(logDir);
                    string path = Path.Combine(logDir, string.Format("Duty_Table_History_{0}.csv", DateTime.Now.ToString("yyyyMMdd_HHmmss")));
                    using (StreamWriter sw = new StreamWriter(path, false, Encoding.UTF8))
                    {
                        var headers = new List<string>();
                        for (int c = 0; c < dgvDuty.Columns.Count; c++) headers.Add(dgvDuty.Columns[c].HeaderText);
                        sw.WriteLine(string.Join(",", headers));
                        for (int r = 0; r < dgvDuty.Rows.Count; r++)
                        {
                            var vals = new List<string>();
                            for (int c = 0; c < dgvDuty.Columns.Count; c++)
                            {
                                object o = dgvDuty.Rows[r].Cells[c].Value;
                                string cellVal = (o != null) ? o.ToString().Replace(",", " ") : "";
                                vals.Add(cellVal);
                            }
                            sw.WriteLine(string.Join(",", vals));
                        }
                    }
                    MessageBox.Show("工作制表格紀錄已成功匯出至:\n" + path, "匯出成功", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show("匯出失敗: " + ex.Message, "錯誤", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            };
            AttachDgvMemoryAndContextMenu(dgvDuty, "DgvDuty");
            if (dgvDuty.ContextMenuStrip != null)
            {
                dgvDuty.ContextMenuStrip.Items.Add(new ToolStripSeparator());
                dgvDuty.ContextMenuStrip.Items.Add(mnuExportCsv);
            }

            TableLayoutPanel tableDutyBottom = new TableLayoutPanel()
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 3,
                BackColor = Color.White,
                Margin = new Padding(0),
                Padding = new Padding(0)
            };
            tableDutyBottom.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            tableDutyBottom.RowStyles.Add(new RowStyle(SizeType.Absolute, 34f)); // Row 0: 連續排程工作進度橫幅
            tableDutyBottom.RowStyles.Add(new RowStyle(SizeType.Absolute, 30f)); // Row 1: 即時電氣監控
            tableDutyBottom.RowStyles.Add(new RowStyle(SizeType.Percent, 100f)); // Row 2: 歷史記錄表格

            // ── Row 0: 連續性排程工作進度提示橫幅 ──
            pnlBatchDutyProgress = new Panel()
            {
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(238, 242, 255),
                Margin = new Padding(0),
                Padding = new Padding(6, 4, 6, 4)
            };
            TableLayoutPanel tblBatchProgInner = new TableLayoutPanel()
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 1,
                Margin = new Padding(0),
                Padding = new Padding(0),
                BackColor = Color.Transparent
            };
            tblBatchProgInner.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150f));
            tblBatchProgInner.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            tblBatchProgInner.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

            prgBatchTotal = new ProgressBar() { Dock = DockStyle.Fill, Minimum = 0, Maximum = 5, Value = 0, Margin = new Padding(2, 3, 4, 3) };
            lblBatchProgressDetail = new Label()
            {
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                Text = "【連續排程待命】勾選上方「連續性自動排程」並選擇方案 (全測或任選兩項)，即可啟用全自動無人值守測試。",
                Font = new Font("微軟正黑體", 10.5f, FontStyle.Bold),
                ForeColor = Color.FromArgb(49, 46, 129),
                Margin = new Padding(4, 0, 0, 0)
            };
            tblBatchProgInner.Controls.Add(prgBatchTotal, 0, 0);
            tblBatchProgInner.Controls.Add(lblBatchProgressDetail, 1, 0);
            pnlBatchDutyProgress.Controls.Add(tblBatchProgInner);

            // ── Row 1: 即時電氣監控橫幅 ──
            lblDutyRealtimeElec = new Label()
            {
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                Text = "【即時電氣監控】平均電壓: --.- V | 平均電流: --.-- A | 輸入功率: --.-- kW | 功因: -.--- | 輸出功率: --.-- kW | 效率: --.- %",
                Font = new Font("微軟正黑體", 10.5f, FontStyle.Bold),
                ForeColor = Color.FromArgb(30, 64, 175),
                BackColor = Color.FromArgb(241, 245, 249),
                Padding = new Padding(8, 0, 0, 0),
                Margin = new Padding(0)
            };

            tableDutyBottom.Controls.Add(pnlBatchDutyProgress, 0, 0);
            tableDutyBottom.Controls.Add(lblDutyRealtimeElec, 0, 1);
            tableDutyBottom.Controls.Add(dgvDuty, 0, 2);

            splitDuty.Panel2.Controls.Add(tableDutyBottom);
            tab.Controls.Add(splitDuty);

            dutyTimer = new System.Windows.Forms.Timer();
            dutyTimer.Interval = 1000;
            dutyTimer.Tick += DutyTimer_Tick;

            UpdateDutyModeVisibility(cmbDutyMode.SelectedIndex);
        }

        private void ShowS1ChannelSelectDialog()
        {
            Form dlg = new Form()
            {
                Text = "S1 溫度監控通道選取 (鎖定實測最高溫進行熱平衡判定)",
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
                Text = "請勾選參與監控之溫度通道（系統將動態追蹤實測最高溫進行 30 分鐘溫差 < 1.0℃ 熱平衡判定）：\n(若設備已連線，可點擊「[電] 依實測選取」自動偵測真正有訊號的通道)",
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
                bool isChecked = (s1MonitoredChannels != null && i < s1MonitoredChannels.Length) ? s1MonitoredChannels[i] : (i < 4);
                double curVal = (gbdChTemps != null && i < gbdChTemps.Length) ? gbdChTemps[i] : 0.0;
                string valHint = (curVal > -40.0 && curVal < 400.0 && Math.Abs(curVal) > 0.01) ? string.Format(" [{0:F1}℃]", curVal) : " [--.-]";
                clb.Items.Add(string.Format("CH{0,2}: {1,-10}{2}", i + 1, cName, valHint), isChecked);
            }

            FlowLayoutPanel pnlBtns = new FlowLayoutPanel() { Dock = DockStyle.Bottom, Height = 45, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(6) };
            Button btnCancel = new Button() { Text = "取消", DialogResult = DialogResult.Cancel, Width = 70, Height = 28 };
            Button btnOk = new Button() { Text = "確定", DialogResult = DialogResult.OK, Width = 70, Height = 28, BackColor = Color.FromArgb(16, 185, 129), ForeColor = Color.White };
            Button btnAutoDetect = new Button()
            {
                Text = "[電] 依實測選取",
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
                    s1MonitoredChannels[i] = clb.GetItemChecked(i);
                    if (s1MonitoredChannels[i])
                    {
                        cnt++;
                        selNames.Add(string.Format("CH{0}", i + 1));
                    }
                }
                if (cnt == 0)
                {
                    s1MonitoredChannels[0] = true;
                    cnt = 1;
                    selNames.Add("CH1");
                }
                if (lblS1SelectedChHint != null) lblS1SelectedChHint.Text = string.Format("(已選 {0} 通道: {1})", cnt, string.Join(",", selNames.ToArray()));
                if (dutyTempTrend != null) dutyTempTrend.SetChannelVisibility(s1MonitoredChannels);
            }
        }

        
        // =========================================================================
        // 4 分頁架構專屬排程確認、即時儀表與在線微調 (Online Live Nudge) 方法
        // =========================================================================
        private double batchCoolingCurrentTemp = 0.0;
        private double batchCoolingTargetTemp = 0.0;

        public void UpdateDutyPreflightSummary()
        {
            if (txtDutyPreflightSummary == null) return;
            try
            {
                StringBuilder sb = new StringBuilder();
                int mainMode = (cmbDutyMainMode != null && cmbDutyMainMode.SelectedIndex >= 0) ? cmbDutyMainMode.SelectedIndex : 0;
                string modeStr = (mainMode == 0) ? "連續自動排程 (依排程方案依序執行)" :
                                 ((mainMode == 1) ? "單項測試：S1 額定連續" :
                                 ((mainMode == 2) ? "單項測試：S2 短時過載" : "單項測試：S6 週期負載"));

                int role = (cmbDutyRole != null && cmbDutyRole.SelectedIndex >= 0) ? cmbDutyRole.SelectedIndex : 1;
                string roleStr = (role == 0) ? "A載台待測(速度) / B加載" : "B載台待測(速度) / A加載";

                int plan = (cmbBatchDutyPlan != null && cmbBatchDutyPlan.SelectedIndex >= 0) ? cmbBatchDutyPlan.SelectedIndex : 0;
                string planStr = (plan == 0) ? "1. S1 -> S6 -> S2 (三項全測)" :
                                 ((plan == 1) ? "2. S1 -> S6 (額定 + 週期)" :
                                 ((plan == 2) ? "3. S1 -> S2 (額定 + 短時)" : "4. S6 -> S2 (週期 + 短時)"));

                int cool = (cmbBatchCoolingMode != null && cmbBatchCoolingMode.SelectedIndex >= 0) ? cmbBatchCoolingMode.SelectedIndex : 0;
                string coolStr = (cool == 0) ? "自動辨識 (水冷25℃ / 氣冷AT)" :
                                 ((cool == 1) ? "強制水冷 (冰水機循環 <= 25.0℃)" : "強制氣冷 (<= AT+1.5℃ 且 15分無下降)");

                decimal ocPct = (numDutyOverCurrentPct != null) ? numDutyOverCurrentPct.Value : 20;
                decimal ocDelay = (numDutyOverCurrentDelay != null) ? numDutyOverCurrentDelay.Value : 5;

                sb.AppendLine("【IEC 60034-1 工作制測試排程預檢確認清單】");
                sb.AppendLine("==========================================================================");
                sb.AppendLine(string.Format("● 測試模式：{0}", modeStr));
                if (mainMode == 0)
                {
                    sb.AppendLine(string.Format("● 排程方案：{0}", planStr));
                    sb.AppendLine(string.Format("● 冷卻策略：{0}", coolStr));
                }
                sb.AppendLine(string.Format("● 載台角色：{0}", roleStr));
                sb.AppendLine(string.Format("● 過電流保護：基準 +{0}%，持續 {1} 秒跳脫保護", ocPct, ocDelay));
                decimal revTimeout = (numDutyRevTrqTimeout != null) ? numDutyRevTrqTimeout.Value : reverseTorqueTimeoutSec;
                sb.AppendLine(string.Format("● 反轉矩超時保護：轉矩負數/超速拖曳持續 {0} 秒強制安全停機", revTimeout));
                sb.AppendLine("--------------------------------------------------------------------------");

                decimal s1Spd = (numS1Speed != null) ? numS1Speed.Value : 1500;
                decimal s1Trq = (numS1Torque != null) ? numS1Torque.Value : 15.0m;
                bool s1Therm = (chkS1ThermalStop != null && chkS1ThermalStop.Checked);
                bool s1Slip = (chkDutySlipComp != null && chkDutySlipComp.Checked);
                sb.AppendLine(string.Format("【S1 額定連續】轉速: {0:F0} rpm | 轉矩: {1:F1} Nm | {2} | {3}",
                    s1Spd, s1Trq,
                    s1Therm ? "30分溫差<1.0°C 自動停機" : "手動停機",
                    s1Slip ? "自動補轉差" : "自然轉差"));

                decimal s2Spd = (numS2Speed != null) ? numS2Speed.Value : 1500;
                decimal s2Trq = (numS2Torque != null) ? numS2Torque.Value : 15.0m;
                decimal s2Dur = (numS2DurationMin != null) ? numS2DurationMin.Value : 30;
                bool s2Stop = (chkS2TempStop != null && chkS2TempStop.Checked);
                decimal s2Thresh = (numS2TempThreshold != null) ? numS2TempThreshold.Value : 80;
                int s2Sy = (numS2AnchorSy52 != null) ? (int)numS2AnchorSy52.Value : s2AnchorSy52;
                int s2Cs = (numS2AnchorCs18 != null) ? (int)numS2AnchorCs18.Value : s2AnchorCs18;
                string s2AnchorStr = (s2Sy > 0 && s2Cs > 0) ? string.Format("定錨已設定 (SY52={0}, CS18={1}‰)", s2Sy, s2Cs) : "未定錨 (自動偵測)";
                sb.AppendLine(string.Format("【S2 短時過載】轉速: {0:F0} rpm | 轉矩: {1:F1} Nm | 時長: {2:F0} 分鐘 | {3} | {4}",
                    s2Spd, s2Trq, s2Dur,
                    s2Stop ? string.Format("超溫閥值 {0:F1}°C 停機", s2Thresh) : "不限溫",
                    s2AnchorStr));

                decimal s6Spd = (numS6Speed != null) ? numS6Speed.Value : 1500;
                decimal s6Trq = (numS6Torque != null) ? numS6Torque.Value : 15.0m;
                decimal s6Cyc = (numS6CycleMin != null) ? numS6CycleMin.Value : 10;
                decimal s6EdVal = (numS6Ed != null) ? numS6Ed.Value : 40;
                decimal s6Loops = (numS6Cycles != null) ? numS6Cycles.Value : 3;
                double t1 = (double)s6Cyc * (double)s6EdVal / 100.0;
                double t2 = (double)s6Cyc - t1;
                int s6NoSpd = (numS6AnchorNoLoadSpd != null) ? (int)numS6AnchorNoLoadSpd.Value : (int)s6AnchorNoLoadSpeed;
                int s6LdSpd = (numS6AnchorLoadedSpd != null) ? (int)numS6AnchorLoadedSpd.Value : (int)s6AnchorLoadedSpeed;
                int s6LdCs = (numS6AnchorLoadedCs18 != null) ? (int)numS6AnchorLoadedCs18.Value : (int)Math.Round(s6AnchorLoadedTorquePct * 10);
                string s6AnchorStr = (s6NoSpd > 0 && s6LdSpd > 0) ? string.Format("定錨已設定 (空載={0}, 加載={1}, CS18={2}‰)", s6NoSpd, s6LdSpd, s6LdCs) : "未定錨 (自適應探測)";
                sb.AppendLine(string.Format("【S6 週期負載】轉速: {0:F0} rpm | 轉矩: {1:F1} Nm | 週期: {2:F1}m (T1={3:F1}m, T2={4:F1}m) | ED: {5:F0}% | 循環: {6:F0} 次 | {7}",
                    s6Spd, s6Trq, s6Cyc, t1, t2, s6EdVal, s6Loops, s6AnchorStr));
                sb.AppendLine("==========================================================================");
                sb.AppendLine("【操作引導】各分頁已獨立配置。待命狀態可自由鍵盤輸入；運轉中自動鎖定打字，僅允許上下鍵微調 (轉速 1 rpm / 轉矩 0.1 Nm / 0.1%)。");

                txtDutyPreflightSummary.Text = sb.ToString();
            }
            catch { }
        }

        private void SetDutyControlsReadOnly(bool isRunning)
        {
            if (numS1Speed != null) numS1Speed.ReadOnly = isRunning;
            if (numS1Torque != null) numS1Torque.ReadOnly = isRunning;
            if (numS2Speed != null) numS2Speed.ReadOnly = isRunning;
            if (numS2Torque != null) numS2Torque.ReadOnly = isRunning;
            if (numS2DurationMin != null) numS2DurationMin.ReadOnly = isRunning;
            if (numS2TempThreshold != null) numS2TempThreshold.ReadOnly = isRunning;
            if (numS2AnchorSy52 != null) numS2AnchorSy52.ReadOnly = isRunning;
            if (numS2AnchorCs18 != null) numS2AnchorCs18.ReadOnly = isRunning;
            if (numS6Speed != null) numS6Speed.ReadOnly = isRunning;
            if (numS6Torque != null) numS6Torque.ReadOnly = isRunning;
            if (numS6CycleMin != null) numS6CycleMin.ReadOnly = isRunning;
            if (numS6Ed != null) numS6Ed.ReadOnly = isRunning;
            if (numS6Cycles != null) numS6Cycles.ReadOnly = isRunning;
            if (numS6AnchorNoLoadSpd != null) numS6AnchorNoLoadSpd.ReadOnly = isRunning;
            if (numS6AnchorLoadedSpd != null) numS6AnchorLoadedSpd.ReadOnly = isRunning;
            if (numS6AnchorLoadedCs18 != null) numS6AnchorLoadedCs18.ReadOnly = isRunning;
            if (numS6WarnTemp != null) numS6WarnTemp.ReadOnly = isRunning;
            if (numS6TripTemp != null) numS6TripTemp.ReadOnly = isRunning;
            if (cmbS6OvertempAction != null) cmbS6OvertempAction.Enabled = !isRunning;
            if (numDutySpeed != null) numDutySpeed.ReadOnly = isRunning;
            if (numDutyTorque != null) numDutyTorque.ReadOnly = isRunning;
            if (numDutyOverCurrentPct != null) numDutyOverCurrentPct.ReadOnly = isRunning;
            if (numDutyOverCurrentDelay != null) numDutyOverCurrentDelay.ReadOnly = isRunning;
            if (numDutyRevTrqTimeout != null) numDutyRevTrqTimeout.ReadOnly = isRunning;

            if (numDutyMiniSpd != null) numDutyMiniSpd.ReadOnly = isRunning;
            if (numDutyMiniTrq != null) numDutyMiniTrq.ReadOnly = isRunning;
            if (numDutyMiniCycleMin != null) numDutyMiniCycleMin.ReadOnly = isRunning;
            if (numDutyMiniEd != null) numDutyMiniEd.ReadOnly = isRunning;
            if (numDutyMiniCycles != null) numDutyMiniCycles.ReadOnly = isRunning;
            if (numDutyMiniS2Sy52 != null) numDutyMiniS2Sy52.ReadOnly = isRunning;
            if (numDutyMiniS2Cs18 != null) numDutyMiniS2Cs18.ReadOnly = isRunning;
            if (numDutyMiniS6NoLoadSpd != null) numDutyMiniS6NoLoadSpd.ReadOnly = isRunning;
            if (numDutyMiniS6LoadedSpd != null) numDutyMiniS6LoadedSpd.ReadOnly = isRunning;
            if (numDutyMiniS6LoadedCs18 != null) numDutyMiniS6LoadedCs18.ReadOnly = isRunning;
            if (cmbDutyMiniMode != null) cmbDutyMiniMode.Enabled = !isRunning;
            if (cmbDutyMiniRole != null) cmbDutyMiniRole.Enabled = !isRunning;

            if (cmbDutyMainMode != null) cmbDutyMainMode.Enabled = !isRunning;
            if (cmbDutyRole != null) cmbDutyRole.Enabled = !isRunning;
            if (cmbBatchDutyPlan != null) cmbBatchDutyPlan.Enabled = !isRunning && (cmbDutyMainMode != null && cmbDutyMainMode.SelectedIndex == 0);
            if (cmbBatchCoolingMode != null) cmbBatchCoolingMode.Enabled = !isRunning && (cmbDutyMainMode != null && cmbDutyMainMode.SelectedIndex == 0);
            if (chkDutySlipComp != null) chkDutySlipComp.Enabled = !isRunning;
            if (chkS1ThermalStop != null) chkS1ThermalStop.Enabled = !isRunning;
            if (btnS1SelectChannels != null) btnS1SelectChannels.Enabled = !isRunning && (chkS1ThermalStop == null || chkS1ThermalStop.Checked);
            if (chkS2TempStop != null) chkS2TempStop.Enabled = !isRunning;
            if (cmbS2TempCh != null) cmbS2TempCh.Enabled = !isRunning && (chkS2TempStop == null || chkS2TempStop.Checked);
            if (cmbS6TempCh != null) cmbS6TempCh.Enabled = !isRunning;
            if (btnS2RecordAnchor != null) btnS2RecordAnchor.Enabled = !isRunning;
            if (btnS2ResetAnchor != null) btnS2ResetAnchor.Enabled = !isRunning;
            if (btnS6AnchorNoLoad != null) btnS6AnchorNoLoad.Enabled = !isRunning;
            if (btnS6AnchorLoaded != null) btnS6AnchorLoaded.Enabled = !isRunning;
            if (btnS6ResetAnchor != null) btnS6ResetAnchor.Enabled = !isRunning;
        }

        public void ResetDutyTestToStandby()
        {
            if (this.InvokeRequired)
            {
                this.BeginInvoke((MethodInvoker)ResetDutyTestToStandby);
                return;
            }
            if (dutyTimer != null) dutyTimer.Stop();
            SetDutyControlsReadOnly(false);
            if (pnlDutyLiveMonitor != null) pnlDutyLiveMonitor.Visible = false;
            if (txtDutyPreflightSummary != null)
            {
                txtDutyPreflightSummary.Visible = true;
                txtDutyPreflightSummary.BringToFront();
            }
            if (btnStartDuty != null) btnStartDuty.Enabled = true;
            if (btnStopDuty != null) btnStopDuty.Enabled = false;
            if (btnDutyMiniStart != null) btnDutyMiniStart.Enabled = true;
            if (btnDutyMiniStop != null) btnDutyMiniStop.Enabled = false;
            UpdateDutyPreflightSummary();
            UpdateSafetyInterlock();
        }

        private void OnDutySpeedNudge(NumericUpDown senderNum, decimal newVal)
        {
            if (isDutyRunning && !isSyncingDutyControls)
            {
                isSyncingDutyControls = true;
                try
                {
                    double newSpeed = (double)newVal;
                    if (numDutySpeed != null) numDutySpeed.Value = Math.Min(numDutySpeed.Maximum, Math.Max(numDutySpeed.Minimum, newVal));
                    s6CurrentSpeedCmd = newSpeed;

                    int dutyRole = (cmbDutyRole != null && cmbDutyRole.SelectedIndex >= 0) ? cmbDutyRole.SelectedIndex : 1;
                    int spdDrive = (dutyRole == 1) ? 2 : 1;
                    int spdCom = GetHmiKebComIdx(spdDrive), spdBaud = GetHmiKebBaudIdx(spdDrive), spdNode = (spdDrive == 1) ? (int)numHmiKebNode1.Value : (int)numHmiKebNode2.Value;
                    KebWriteParam32(spdCom, spdBaud, spdNode, 0x0034, (int)Math.Round(newSpeed), string.Format("DUTY在線微調轉速 -> {0:F0} rpm (Sy52)", newSpeed));
                    WriteHmiLog("DUTY_NUDGE", string.Format("【DUTY在線微調轉速】設定轉速即時調整為 {0:F0} rpm (Sy.52)", newSpeed));
                }
                catch (Exception ex)
                {
                    WriteHmiLog("DUTY_NUDGE_ERR", "在線轉速微調異常: " + ex.Message);
                }
                finally
                {
                    isSyncingDutyControls = false;
                }
            }
            UpdateDutyPreflightSummary();
        }

        private void OnDutyTorqueNudge(NumericUpDown senderNum, decimal newVal)
        {
            if (isDutyRunning && !isSyncingDutyControls)
            {
                isSyncingDutyControls = true;
                try
                {
                    double newTorque = (double)newVal;
                    if (numDutyTorque != null) numDutyTorque.Value = Math.Min(numDutyTorque.Maximum, Math.Max(numDutyTorque.Minimum, newVal));
                    // 負載跟蹤引擎將於下一 Tick 自動採用新轉矩數值
                    WriteHmiLog("DUTY_NUDGE", string.Format("【DUTY在線微調轉矩】設定轉矩即時調整為 {0:F1} Nm", newTorque));
                }
                catch (Exception ex)
                {
                    WriteHmiLog("DUTY_NUDGE_ERR", "在線轉矩微調異常: " + ex.Message);
                }
                finally
                {
                    isSyncingDutyControls = false;
                }
            }
            UpdateDutyPreflightSummary();
        }

        private void OnDutyCs18Nudge(int newCs18)
        {
            if (isDutyRunning && !isSyncingDutyControls)
            {
                isSyncingDutyControls = true;
                try
                {
                    int dutyRole = (cmbDutyRole != null && cmbDutyRole.SelectedIndex >= 0) ? cmbDutyRole.SelectedIndex : 1;
                    int trqDrive = (dutyRole == 1) ? 1 : 2;
                    int trqCom = GetHmiKebComIdx(trqDrive), trqBaud = GetHmiKebBaudIdx(trqDrive), trqNode = (trqDrive == 1) ? (int)numHmiKebNode1.Value : (int)numHmiKebNode2.Value;
                    KebWriteParamWithDll(trqCom, trqBaud, trqNode, 0x0F12, newCs18);
                    WriteHmiLog("DUTY_NUDGE", string.Format("【DUTY在線微調轉矩】cs.18 即時調整為 {0} ‰ ({1:F1}%)", newCs18, newCs18 / 10.0));
                }
                catch (Exception ex)
                {
                    WriteHmiLog("DUTY_NUDGE_ERR", "在線 CS18 微調異常: " + ex.Message);
                }
                finally
                {
                    isSyncingDutyControls = false;
                }
            }
            UpdateDutyPreflightSummary();
        }

        private void UpdateDutyLiveDashboard()
        {
            if (pnlDutyLiveMonitor == null || !pnlDutyLiveMonitor.Visible) return;
            try
            {
                string stageStr;
                if (isBatchDutyActive)
                {
                    string sName = (batchDutyStage == 1) ? "S1 額定連續負載運轉" :
                                   ((batchDutyStage == 2) ? "S1 完成後冷卻降溫" :
                                   ((batchDutyStage == 3) ? "S6 週期負載運轉" :
                                   ((batchDutyStage == 4) ? "S6 完成後冷卻降溫" :
                                   ((batchDutyStage == 5) ? "S2 短時過載負載運轉" : "排程結束"))));
                    stageStr = string.Format("【連續排程 階段 {0}】{1}", batchDutyStage, sName);
                }
                else
                {
                    int curMode = (cmbDutyMode != null && cmbDutyMode.SelectedIndex >= 0) ? cmbDutyMode.SelectedIndex : 0;
                    string mName = (curMode == 0) ? "S1 額定連續負載" : ((curMode == 1) ? "S2 短時過載" : "S6 週期負載");
                    stageStr = string.Format("【單項測試】{0} 運轉中", mName);
                }
                lblDutyLiveStage.Text = stageStr;

                int elMin = dutyElapsedSec / 60;
                int elSec = dutyElapsedSec % 60;
                if (dutyTotalSec > 0 && dutyTotalSec > dutyElapsedSec)
                {
                    int remSec = dutyTotalSec - dutyElapsedSec;
                    lblDutyLiveCountdown.Text = string.Format("運轉時間: {0:D2}分{1:D2}秒 | 倒數剩餘: {2:D2}分{3:D2}秒 (總計 {4}分)", elMin, elSec, remSec / 60, remSec % 60, dutyTotalSec / 60);
                }
                else
                {
                    lblDutyLiveCountdown.Text = string.Format("運轉時間: {0:D2}分{1:D2}秒", elMin, elSec);
                }

                double targetSpd = (numDutySpeed != null) ? (double)numDutySpeed.Value : 1500.0;
                double targetTrq = (numDutyTorque != null) ? (double)numDutyTorque.Value : 15.0;
                lblDutyLiveTargetActual.Text = string.Format("轉速: 目標 {0:F0} rpm / 實測 {1:F1} rpm | 轉矩: 目標 {2:F1} Nm / 實測 {3:F1} Nm",
                    targetSpd, Math.Abs(actSpeed), targetTrq, Math.Abs(actTorque));

                double curPmechKw = (Math.Abs(actSpeed) * Math.Abs(actTorque)) / 9549.0;
                string tempHighestStr = (lblDutyTempRealtimeVal != null && !string.IsNullOrEmpty(lblDutyTempRealtimeVal.Text))
                    ? lblDutyTempRealtimeVal.Text
                    : string.Format("實測 {0:F1} ℃", actTemp);
                lblDutyLiveSlope.Text = string.Format("即時運轉指標: 輸出功率 {0:F2} kW | {1}", curPmechKw, tempHighestStr);

                if (isBatchDutyActive && (batchDutyStage == 2 || batchDutyStage == 4))
                {
                    lblDutyLiveCooling.Text = string.Format("冷卻降溫中: 實測 {0:F1} °C -> 目標 <= {1:F1} °C", batchCoolingCurrentTemp, batchCoolingTargetTemp);
                    lblDutyLiveCooling.ForeColor = Color.FromArgb(2, 132, 199);
                }
                else
                {
                    lblDutyLiveCooling.Text = "冷卻狀態: 正常運轉 (無待冷卻)";
                    lblDutyLiveCooling.ForeColor = Color.FromArgb(5, 150, 105);
                }
            }
            catch { }
        }

        private void UpdateDutyModeVisibility(int modeIdx)
        {
            bool isS1 = (modeIdx == 0);
            bool isS2 = (modeIdx == 1);
            bool isS6 = (modeIdx == 2);

            // 右側溫度波形指示與 S1/S2/S6 專屬超溫/熱平衡面板顯隱
            if (lblS1ThermalStatus != null) lblS1ThermalStatus.Visible = isS1;
            if (lblS2ThermalStatus != null) lblS2ThermalStatus.Visible = isS2;
            if (lblS6ThermalStatus != null) lblS6ThermalStatus.Visible = isS6;
            if (pnlS6Overtemp != null) pnlS6Overtemp.Visible = isS6;

            if (lblThermalStatus != null)
            {
                if (isS1) lblThermalStatus.Text = "S1 熱平衡判定: 準備中 (30m溫差 < 1.0℃ 自動停機，含即時溫升斜率與完成時間預估)";
                else if (isS2) lblThermalStatus.Text = "S2 溫升預估: 準備中 (依一階熱模型即時預估到達設定時長時之最終溫度)";
                else if (isS6) lblThermalStatus.Text = "S6 熱平衡判定: 準備中 (連續 3 週期峰值溫差 <= 1.0℃ 自動停機)";
                lblThermalStatus.ForeColor = Color.FromArgb(71, 85, 105);
            }

            if (lblDutyTempTrendTitle != null)
            {
                if (isS1)
                {
                    lblDutyTempTrendTitle.Text = "目前監控: S1 多通道最高溫 (熱平衡判定: 30min溫差<1.0℃，含預估完成時間)";
                }
                else if (isS2)
                {
                    int ch = (cmbS2TempCh != null && cmbS2TempCh.SelectedIndex >= 0) ? cmbS2TempCh.SelectedIndex + 1 : 1;
                    lblDutyTempTrendTitle.Text = string.Format("目前監控: S2 通道 {0} (短時運轉，含溫升斜率與到達時長預估終溫)", ch);
                }
                else if (isS6)
                {
                    int ch = (cmbS6TempCh != null && cmbS6TempCh.SelectedIndex >= 0) ? cmbS6TempCh.SelectedIndex + 1 : 1;
                    lblDutyTempTrendTitle.Text = string.Format("目前監控: S6 通道 {0} (每10分鐘最高溫熱平衡分析)", ch);
                }
            }

            // 同步溫度趨勢圖通道遮罩 (S1 顯示選取的多通道；S2/S6 顯示所選之單通道)
            if (dutyTempTrend != null)
            {
                if (isS1)
                {
                    dutyTempTrend.SetChannelVisibility(s1MonitoredChannels);
                }
                else if (isS2)
                {
                    int ch = (cmbS2TempCh != null && cmbS2TempCh.SelectedIndex >= 0) ? cmbS2TempCh.SelectedIndex : 0;
                    bool[] singleMask = new bool[20];
                    if (ch >= 0 && ch < 20) singleMask[ch] = true;
                    dutyTempTrend.SetChannelVisibility(singleMask);
                }
                else if (isS6)
                {
                    int ch = (cmbS6TempCh != null && cmbS6TempCh.SelectedIndex >= 0) ? cmbS6TempCh.SelectedIndex : 0;
                    bool[] singleMask = new bool[20];
                    if (ch >= 0 && ch < 20) singleMask[ch] = true;
                    dutyTempTrend.SetChannelVisibility(singleMask);
                }
            }

            // 4. 綜合監控右下角 Mini 視窗 S2 / S6 專屬元件顯隱與自適應隱藏列高
            if (lblDutyMiniS2AnchorLabel != null) lblDutyMiniS2AnchorLabel.Visible = isS2;
            if (pnlDutyMiniS2Anchors != null) pnlDutyMiniS2Anchors.Visible = isS2;

            if (lblDutyMiniCycleLabel != null) lblDutyMiniCycleLabel.Visible = isS6;
            if (pnlDutyMiniCycle != null) pnlDutyMiniCycle.Visible = isS6;
            if (lblDutyMiniAnchorLabel != null) lblDutyMiniAnchorLabel.Visible = isS6;
            if (pnlDutyMiniAnchors != null) pnlDutyMiniAnchors.Visible = isS6;
            if (lblDutyMiniDiagLabel != null) lblDutyMiniDiagLabel.Visible = isS6;
            if (pnlS6MiniDiagram != null) pnlS6MiniDiagram.Visible = isS6;

            if (tblDutyMini != null && tblDutyMini.RowStyles.Count >= 6)
            {
                tblDutyMini.RowStyles[2].Height = isS2 ? 30f : 0f; // Row 2: S2 錨點
                tblDutyMini.RowStyles[3].Height = isS6 ? 28f : 0f; // Row 3: S6 週期T/ED%
                tblDutyMini.RowStyles[4].Height = isS6 ? 56f : 0f; // Row 4: S6 雙重定錨
                tblDutyMini.RowStyles[5].Height = isS6 ? 65f : 0f; // Row 5: S6 週期圖解
            }

            if (splitDuty != null && splitDuty.Height > 0)
            {
                string dutyModeKey = isS1 ? "DutyMain_S1" : (isS2 ? "DutyMain_S2" : "DutyMain_S6");
                int defDist = isS1 ? 539 : 550;
                int saved;
                if (layoutSplitters.TryGetValue(dutyModeKey, out saved) && saved > 0)
                {
                    defDist = saved;
                }
                else if (layoutSplitters.TryGetValue("DutyMain", out saved) && saved > 0)
                {
                    defDist = saved;
                }
                ApplySplitterDistanceSafe(splitDuty, dutyModeKey, defDist, 150, 80);
            }
        }

        private void UpdateS6CalcInfo()
        {
            if (lblS6CalcInfo == null) return;
            double cycleMin = (double)(numS6CycleMin != null ? numS6CycleMin.Value : (numDutyMiniCycleMin != null ? numDutyMiniCycleMin.Value : 10));
            double edVal = (double)(numS6Ed != null ? numS6Ed.Value : (numDutyMiniEd != null ? numDutyMiniEd.Value : 40));
            double t1Min = cycleMin * (edVal / 100.0);
            double t2Min = cycleMin - t1Min;
            lblS6CalcInfo.Text = string.Format("換算：有載 T1 = {0:F1}分, 空載 T2 = {1:F1}分", t1Min, t2Min);
        }

        private void UpdateS6AnchorStatusText()
        {
            string statusStr = string.Format("定錨: 空載={0} | 加載={1} / {2}",
                s6HasNoLoadAnchor ? string.Format("{0:F0}rpm", s6AnchorNoLoadSpeed) : "未定錨",
                s6HasLoadedAnchor ? string.Format("{0:F0}rpm", s6AnchorLoadedSpeed) : "未定錨",
                s6HasLoadedAnchor ? string.Format("{0:F1}%", s6AnchorLoadedTorquePct) : "--");
            Color col = (s6HasNoLoadAnchor && s6HasLoadedAnchor) ? Color.DarkGreen : Color.DarkOrange;
            if (lblS6AnchorStatus != null)
            {
                lblS6AnchorStatus.Text = statusStr;
                lblS6AnchorStatus.ForeColor = col;
            }
            if (lblS6MiniAnchorStatus != null)
            {
                lblS6MiniAnchorStatus.Text = statusStr;
                lblS6MiniAnchorStatus.ForeColor = col;
            }
        }

        // =========================================================================
        // S2 / S6 手動記錄當前運轉點為錨點與歸零邏輯
        // =========================================================================
        private void CaptureCurrentAnchorToS2()
        {
            int spdDrive = (cmbDutyRole != null && cmbDutyRole.SelectedIndex == 1) ? 2 : 1;
            int trqDrive = (spdDrive == 1) ? 2 : 1;

            double spd = (Math.Abs(actSpeed) > 10.0) ? Math.Abs(actSpeed) :
                         ((spdDrive == 1 && numHmiKebSpeed1 != null) ? (double)numHmiKebSpeed1.Value :
                          (numHmiKebSpeed2 != null ? (double)numHmiKebSpeed2.Value : (numDutySpeed != null ? (double)numDutySpeed.Value : 1000.0)));

            double trqPct = (trqDrive == 1 && numHmiKebTorque1 != null) ? (double)numHmiKebTorque1.Value :
                            (numHmiKebTorque2 != null ? (double)numHmiKebTorque2.Value : (numDutyTorque != null ? (double)numDutyTorque.Value : 10.0));
            int cs18Val = (int)Math.Round(trqPct * 10.0);

            s2AnchorSy52 = (int)Math.Round(spd);
            s2AnchorCs18 = cs18Val;

            if (numS2AnchorSy52 != null) numS2AnchorSy52.Value = Math.Max(numS2AnchorSy52.Minimum, Math.Min(numS2AnchorSy52.Maximum, s2AnchorSy52));
            if (numS2AnchorCs18 != null) numS2AnchorCs18.Value = Math.Max(numS2AnchorCs18.Minimum, Math.Min(numS2AnchorCs18.Maximum, s2AnchorCs18));
            if (numDutyMiniS2Sy52 != null) numDutyMiniS2Sy52.Value = Math.Max(numDutyMiniS2Sy52.Minimum, Math.Min(numDutyMiniS2Sy52.Maximum, s2AnchorSy52));
            if (numDutyMiniS2Cs18 != null) numDutyMiniS2Cs18.Value = Math.Max(numDutyMiniS2Cs18.Minimum, Math.Min(numDutyMiniS2Cs18.Maximum, s2AnchorCs18));

            WriteHmiLog("S2_ANCHOR", string.Format("【手動記錄 S2 錨點】已抓取當前運轉狀態：SY52={0} rpm, CS18={1} ‰ ({2:F1}%)", s2AnchorSy52, s2AnchorCs18, s2AnchorCs18 / 10.0));
        }

        private void ResetS2Anchor()
        {
            s2AnchorSy52 = 0;
            s2AnchorCs18 = 0;
            if (numS2AnchorSy52 != null) numS2AnchorSy52.Value = 0;
            if (numS2AnchorCs18 != null) numS2AnchorCs18.Value = 0;
            if (numDutyMiniS2Sy52 != null) numDutyMiniS2Sy52.Value = 0;
            if (numDutyMiniS2Cs18 != null) numDutyMiniS2Cs18.Value = 0;
            WriteHmiLog("S2_ANCHOR", "使用者手動歸零 S2 錨點數據 (SY52=0, CS18=0)");
        }

        private void CaptureCurrentAnchorToS6NoLoad()
        {
            int spdDrive = (cmbDutyRole != null && cmbDutyRole.SelectedIndex == 1) ? 2 : 1;
            double spd = (Math.Abs(actSpeed) > 10.0) ? Math.Abs(actSpeed) :
                         ((spdDrive == 1 && numHmiKebSpeed1 != null) ? (double)numHmiKebSpeed1.Value :
                          (numHmiKebSpeed2 != null ? (double)numHmiKebSpeed2.Value : (numDutySpeed != null ? (double)numDutySpeed.Value : 1000.0)));
            s6AnchorNoLoadSpeed = Math.Round(spd);
            s6HasNoLoadAnchor = (s6AnchorNoLoadSpeed > 0);

            if (numS6AnchorNoLoadSpd != null) numS6AnchorNoLoadSpd.Value = (decimal)s6AnchorNoLoadSpeed;
            if (numDutyMiniS6NoLoadSpd != null) numDutyMiniS6NoLoadSpd.Value = (decimal)s6AnchorNoLoadSpeed;

            UpdateS6AnchorStatusText();
            WriteHmiLog("S6_ANCHOR", string.Format("【手動記錄 S6 空載錨點】轉速 SY52={0:F0} rpm", s6AnchorNoLoadSpeed));
        }

        private void CaptureCurrentAnchorToS6Loaded()
        {
            int spdDrive = (cmbDutyRole != null && cmbDutyRole.SelectedIndex == 1) ? 2 : 1;
            int trqDrive = (spdDrive == 1) ? 2 : 1;
            double spd = (Math.Abs(actSpeed) > 10.0) ? Math.Abs(actSpeed) :
                         ((spdDrive == 1 && numHmiKebSpeed1 != null) ? (double)numHmiKebSpeed1.Value :
                          (numHmiKebSpeed2 != null ? (double)numHmiKebSpeed2.Value : (numDutySpeed != null ? (double)numDutySpeed.Value : 1100.0)));
            double trqPct = (trqDrive == 1 && numHmiKebTorque1 != null) ? (double)numHmiKebTorque1.Value :
                            (numHmiKebTorque2 != null ? (double)numHmiKebTorque2.Value : (numDutyTorque != null ? (double)numDutyTorque.Value : 10.0));
            int cs18Val = (int)Math.Round(trqPct * 10.0);

            s6AnchorLoadedSpeed = Math.Round(spd);
            s6AnchorLoadedTorquePct = trqPct;
            s6HasLoadedAnchor = (s6AnchorLoadedSpeed > 0 && s6AnchorLoadedTorquePct > 0);

            if (numS6AnchorLoadedSpd != null) numS6AnchorLoadedSpd.Value = (decimal)s6AnchorLoadedSpeed;
            if (numDutyMiniS6LoadedSpd != null) numDutyMiniS6LoadedSpd.Value = (decimal)s6AnchorLoadedSpeed;
            if (numS6AnchorLoadedCs18 != null) numS6AnchorLoadedCs18.Value = cs18Val;
            if (numDutyMiniS6LoadedCs18 != null) numDutyMiniS6LoadedCs18.Value = cs18Val;

            UpdateS6AnchorStatusText();
            WriteHmiLog("S6_ANCHOR", string.Format("【手動記錄 S6 加載錨點】轉速 SY52={0:F0} rpm, 轉矩 CS18={1} ‰ ({2:F1}%)", s6AnchorLoadedSpeed, cs18Val, trqPct));
        }

        private void ResetS6Anchors()
        {
            s6AnchorNoLoadSpeed = 0;
            s6AnchorLoadedSpeed = 0;
            s6AnchorLoadedTorquePct = 0;
            s6HasNoLoadAnchor = false;
            s6HasLoadedAnchor = false;

            if (numS6AnchorNoLoadSpd != null) numS6AnchorNoLoadSpd.Value = 0;
            if (numDutyMiniS6NoLoadSpd != null) numDutyMiniS6NoLoadSpd.Value = 0;
            if (numS6AnchorLoadedSpd != null) numS6AnchorLoadedSpd.Value = 0;
            if (numDutyMiniS6LoadedSpd != null) numDutyMiniS6LoadedSpd.Value = 0;
            if (numS6AnchorLoadedCs18 != null) numS6AnchorLoadedCs18.Value = 0;
            if (numDutyMiniS6LoadedCs18 != null) numDutyMiniS6LoadedCs18.Value = 0;

            UpdateS6AnchorStatusText();
            WriteHmiLog("S6_ANCHOR", "使用者歸零 S6 定錨數據");
        }

        // =========================================================================
        // 大小頁面全息雙向鏡像同步 (完整覆蓋 S1/S2/S6 模式、參數與錨點)
        // =========================================================================
        private void SyncAllDutyControls(bool fromMiniToMain)
        {
            if (isSyncingDutyControls) return;
            try
            {
                isSyncingDutyControls = true;
                try { this.ValidateChildren(); } catch { }

                if (fromMiniToMain)
                {
                    if (cmbDutyMode != null && cmbDutyMiniMode != null && cmbDutyMiniMode.SelectedIndex >= 0)
                        cmbDutyMode.SelectedIndex = cmbDutyMiniMode.SelectedIndex;
                    if (cmbDutyRole != null && cmbDutyMiniRole != null && cmbDutyMiniRole.SelectedIndex >= 0)
                        cmbDutyRole.SelectedIndex = cmbDutyMiniRole.SelectedIndex;
                    if (numDutySpeed != null && numDutyMiniSpd != null)
                        numDutySpeed.Value = numDutyMiniSpd.Value;
                    if (numDutyTorque != null && numDutyMiniTrq != null)
                        numDutyTorque.Value = numDutyMiniTrq.Value;
                    if (numS6CycleMin != null && numDutyMiniCycleMin != null)
                        numS6CycleMin.Value = numDutyMiniCycleMin.Value;
                    if (numS6Ed != null && numDutyMiniEd != null)
                        numS6Ed.Value = numDutyMiniEd.Value;
                    if (numS6Cycles != null && numDutyMiniCycles != null)
                        numS6Cycles.Value = numDutyMiniCycles.Value;

                    // S2 錨點
                    if (numS2AnchorSy52 != null && numDutyMiniS2Sy52 != null)
                        numS2AnchorSy52.Value = numDutyMiniS2Sy52.Value;
                    if (numS2AnchorCs18 != null && numDutyMiniS2Cs18 != null)
                        numS2AnchorCs18.Value = numDutyMiniS2Cs18.Value;

                    // S6 錨點
                    if (numS6AnchorNoLoadSpd != null && numDutyMiniS6NoLoadSpd != null)
                        numS6AnchorNoLoadSpd.Value = numDutyMiniS6NoLoadSpd.Value;
                    if (numS6AnchorLoadedSpd != null && numDutyMiniS6LoadedSpd != null)
                        numS6AnchorLoadedSpd.Value = numDutyMiniS6LoadedSpd.Value;
                    if (numS6AnchorLoadedCs18 != null && numDutyMiniS6LoadedCs18 != null)
                        numS6AnchorLoadedCs18.Value = numDutyMiniS6LoadedCs18.Value;
                }
                else
                {
                    if (cmbDutyMiniMode != null && cmbDutyMode != null && cmbDutyMode.SelectedIndex >= 0)
                        cmbDutyMiniMode.SelectedIndex = cmbDutyMode.SelectedIndex;
                    if (cmbDutyMiniRole != null && cmbDutyRole != null && cmbDutyRole.SelectedIndex >= 0)
                        cmbDutyMiniRole.SelectedIndex = cmbDutyRole.SelectedIndex;
                    if (numDutyMiniSpd != null && numDutySpeed != null)
                        numDutyMiniSpd.Value = numDutySpeed.Value;
                    if (numDutyMiniTrq != null && numDutyTorque != null)
                        numDutyMiniTrq.Value = numDutyTorque.Value;
                    if (numDutyMiniCycleMin != null && numS6CycleMin != null)
                        numDutyMiniCycleMin.Value = numS6CycleMin.Value;
                    if (numDutyMiniEd != null && numS6Ed != null)
                        numDutyMiniEd.Value = numS6Ed.Value;
                    if (numDutyMiniCycles != null && numS6Cycles != null)
                        numDutyMiniCycles.Value = numS6Cycles.Value;

                    // S2 錨點
                    if (numDutyMiniS2Sy52 != null && numS2AnchorSy52 != null)
                        numDutyMiniS2Sy52.Value = numS2AnchorSy52.Value;
                    if (numDutyMiniS2Cs18 != null && numS2AnchorCs18 != null)
                        numDutyMiniS2Cs18.Value = numS2AnchorCs18.Value;

                    // S6 錨點
                    if (numDutyMiniS6NoLoadSpd != null && numS6AnchorNoLoadSpd != null)
                        numDutyMiniS6NoLoadSpd.Value = numS6AnchorNoLoadSpd.Value;
                    if (numDutyMiniS6LoadedSpd != null && numS6AnchorLoadedSpd != null)
                        numDutyMiniS6LoadedSpd.Value = numS6AnchorLoadedSpd.Value;
                    if (numDutyMiniS6LoadedCs18 != null && numS6AnchorLoadedCs18 != null)
                        numDutyMiniS6LoadedCs18.Value = numS6AnchorLoadedCs18.Value;
                }
                int curMode = (cmbDutyMode != null && cmbDutyMode.SelectedIndex >= 0) ? cmbDutyMode.SelectedIndex : 2;
                UpdateDutyModeVisibility(curMode);
                UpdateS6CalcInfo();
                UpdateS6AnchorStatusText();
                if (pnlS6Diagram != null) pnlS6Diagram.Invalidate();
                if (pnlS6MiniDiagram != null) pnlS6MiniDiagram.Invalidate();
            }
            finally
            {
                isSyncingDutyControls = false;
            }
        }

        // =========================================================================
        // WinXP 相容安全字型與 GDI+ 週期圖解繪製
        // =========================================================================
        private Font GetSafeFont(string preferredFamily, float size, FontStyle style = FontStyle.Regular)
        {
            try
            {
                Font f = new Font(preferredFamily, size, style);
                if (f.Name.IndexOf(preferredFamily, StringComparison.OrdinalIgnoreCase) >= 0 || preferredFamily == "Arial")
                    return f;
                f.Dispose();
            }
            catch { }

            try { return new Font("Arial", size, style); }
            catch { }
            try { return new Font("新細明體", size, style); }
            catch { }
            return new Font(FontFamily.GenericSansSerif, size, style);
        }

        private void PnlS6Diagram_Paint(object sender, PaintEventArgs e)
        {
            Panel targetPnl = (sender as Panel != null) ? (Panel)sender : pnlS6Diagram;
            if (targetPnl == null || targetPnl.Width <= 5 || targetPnl.Height <= 5) return;
            Graphics g = e.Graphics;
            try
            {
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                Rectangle rect = targetPnl.ClientRectangle;
                g.Clear(Color.White);

                double cycleMin = (double)(numS6CycleMin != null ? numS6CycleMin.Value : (numDutyMiniCycleMin != null ? numDutyMiniCycleMin.Value : 10));
                double edVal = (double)(numS6Ed != null ? numS6Ed.Value : (numDutyMiniEd != null ? numDutyMiniEd.Value : 40));
                double t1Min = cycleMin * (edVal / 100.0);
                double t2Min = cycleMin - t1Min;

                bool isCompact = rect.Height < 100;

                int left = isCompact ? 32 : 46;
                int top = isCompact ? 18 : 38;
                int right = rect.Width - (isCompact ? 20 : 30);
                int bottom = rect.Height - (isCompact ? 14 : 26);
                if (right <= left + 20 || bottom <= top + 15) return;

                int totalPlotW = Math.Max(40, right - left);
                int totalPlotH = Math.Max(20, bottom - top);

                // 繪製 Y 軸與箭頭
                using (Pen axisPen = new Pen(Color.Black, isCompact ? 1.4f : 1.6f))
                {
                    g.DrawLine(axisPen, left, bottom, left, top - (isCompact ? 10 : 14));
                    Point[] yArrow = new Point[] {
                        new Point(left - 4, top - (isCompact ? 6 : 8)),
                        new Point(left + 4, top - (isCompact ? 6 : 8)),
                        new Point(left, top - (isCompact ? 14 : 18))
                    };
                    g.FillPolygon(Brushes.Black, yArrow);

                    g.DrawLine(axisPen, left, bottom, right + 10, bottom);
                    Point[] xArrow = new Point[] {
                        new Point(right + 4, bottom - 4),
                        new Point(right + 4, bottom + 4),
                        new Point(right + 14, bottom)
                    };
                    g.FillPolygon(Brushes.Black, xArrow);
                }

                // 軸線標註
                using (Font fAxis = GetSafeFont("微軟正黑體", isCompact ? 7.5f : 8.5f, FontStyle.Bold))
                using (Brush textBr = new SolidBrush(Color.Black))
                {
                    int yMid = top + totalPlotH / 2;
                    g.DrawString("負", fAxis, textBr, left - (isCompact ? 18 : 24), yMid - (isCompact ? 14 : 18));
                    g.DrawString("荷", fAxis, textBr, left - (isCompact ? 18 : 24), yMid + (isCompact ? 0 : 2));
                    g.DrawString("時間", fAxis, textBr, right - (isCompact ? 12 : 18), bottom + (isCompact ? 2 : 5));
                }

                // 週期繪製
                int cycleWidth = totalPlotW / 2;
                double edFrac = Math.Max(0.1, Math.Min(0.9, edVal / 100.0));
                int nWidth = (int)(cycleWidth * edFrac);
                int vWidth = cycleWidth - nWidth;
                int blockH = (int)(totalPlotH * (isCompact ? 0.68 : 0.72));
                int blockTop = bottom - blockH;

                // 繪製方塊：優先使用 HatchBrush，若 XP 拋錯則自動退化至淡藍純色
                Brush blockBr = null;
                try
                {
                    blockBr = new System.Drawing.Drawing2D.HatchBrush(
                        System.Drawing.Drawing2D.HatchStyle.ForwardDiagonal, Color.Black, Color.White);
                }
                catch
                {
                    blockBr = new SolidBrush(Color.FromArgb(200, 220, 245));
                }

                using (blockBr)
                using (Pen borderPen = new Pen(Color.Black, isCompact ? 1.4f : 1.8f))
                using (Pen idlePen = new Pen(Color.Black, isCompact ? 2.0f : 2.5f))
                {
                    Rectangle rectN1 = new Rectangle(left, blockTop, nWidth, blockH);
                    g.FillRectangle(blockBr, rectN1);
                    g.DrawRectangle(borderPen, rectN1);
                    g.DrawLine(idlePen, left + nWidth, bottom, left + cycleWidth, bottom);

                    Rectangle rectN2 = new Rectangle(left + cycleWidth, blockTop, nWidth, blockH);
                    g.FillRectangle(blockBr, rectN2);
                    g.DrawRectangle(borderPen, rectN2);
                    g.DrawLine(idlePen, left + cycleWidth + nWidth, bottom, left + 2 * cycleWidth, bottom);
                }

                // 尺寸標註
                using (Pen dimPen = new Pen(Color.Black, 1.2f))
                using (Font fDim = GetSafeFont("微軟正黑體", isCompact ? 7.5f : 8.5f, FontStyle.Bold))
                using (Font fSmall = GetSafeFont("微軟正黑體", isCompact ? 6.5f : 7.5f))
                using (Brush dimBr = new SolidBrush(Color.Black))
                {
                    int yTopDim = isCompact ? top - 12 : top - 18;
                    int ySubDim = isCompact ? top - 2 : top - 2;

                    g.DrawLine(dimPen, left, yTopDim - 3, left, blockTop);
                    g.DrawLine(dimPen, left + nWidth, ySubDim - 3, left + nWidth, blockTop);
                    g.DrawLine(dimPen, left + cycleWidth, yTopDim - 3, left + cycleWidth, bottom);

                    g.DrawLine(dimPen, left, yTopDim, left + cycleWidth, yTopDim);
                    DrawDimArrow(g, left, yTopDim, isLeft: true);
                    DrawDimArrow(g, left + cycleWidth, yTopDim, isLeft: false);
                    string strCycle = "1 週期";
                    SizeF szCycle = g.MeasureString(strCycle, fDim);
                    g.FillRectangle(Brushes.White, left + (cycleWidth - szCycle.Width) / 2 - 2, yTopDim - szCycle.Height / 2, szCycle.Width + 4, szCycle.Height);
                    g.DrawString(strCycle, fDim, dimBr, left + (cycleWidth - szCycle.Width) / 2, yTopDim - szCycle.Height / 2);

                    g.DrawLine(dimPen, left, ySubDim, left + nWidth, ySubDim);
                    DrawDimArrow(g, left, ySubDim, isLeft: true);
                    DrawDimArrow(g, left + nWidth, ySubDim, isLeft: false);
                    string strN = "N";
                    SizeF szN = g.MeasureString(strN, fDim);
                    g.FillRectangle(Brushes.White, left + (nWidth - szN.Width) / 2 - 2, ySubDim - szN.Height / 2, szN.Width + 4, szN.Height);
                    g.DrawString(strN, fDim, dimBr, left + (nWidth - szN.Width) / 2, ySubDim - szN.Height / 2);

                    g.DrawLine(dimPen, left + nWidth, ySubDim, left + cycleWidth, ySubDim);
                    DrawDimArrow(g, left + nWidth, ySubDim, isLeft: true);
                    DrawDimArrow(g, left + cycleWidth, ySubDim, isLeft: false);
                    string strV = "V";
                    SizeF szV = g.MeasureString(strV, fDim);
                    g.FillRectangle(Brushes.White, left + nWidth + (vWidth - szV.Width) / 2 - 2, ySubDim - szV.Height / 2, szV.Width + 4, szV.Height);
                    g.DrawString(strV, fDim, dimBr, left + nWidth + (vWidth - szV.Width) / 2, ySubDim - szV.Height / 2);

                    if (!isCompact && totalPlotW > 200)
                    {
                        string detailStr = string.Format("N(有載)={0:F1}分 (ED {1:F0}%) | V(空載)={2:F1}分 | 總週期={3:F1}分", t1Min, edVal, t2Min, cycleMin);
                        g.DrawString(detailStr, fSmall, Brushes.DimGray, left, bottom + 7);
                    }
                }

                // 游標指示 (S6 週期測試時顯示紅色游標)
                int dutyMode = (cmbDutyMode != null && cmbDutyMode.SelectedIndex >= 0) ? cmbDutyMode.SelectedIndex : (cmbDutyMiniMode != null ? cmbDutyMiniMode.SelectedIndex : 2);
                if (dutyTimer != null && dutyTimer.Enabled && dutyMode == 2)
                {
                    int totalCycleSec = Math.Max(2, (int)(cycleMin * 60));
                    int curSecInCycle = s6CycleElapsedSec % totalCycleSec;
                    float curRatio = (float)curSecInCycle / totalCycleSec;
                    int cursorX = left + (int)(cycleWidth * curRatio);

                    using (Pen curPen = new Pen(Color.FromArgb(220, 38, 38), 2.2f))
                    {
                        g.DrawLine(curPen, cursorX, top - 8, cursorX, bottom);
                        using (SolidBrush cBr = new SolidBrush(Color.FromArgb(220, 38, 38)))
                        {
                            Point[] pointer = new Point[] {
                                new Point(cursorX - 4, top - 8),
                                new Point(cursorX + 4, top - 8),
                                new Point(cursorX, top)
                            };
                            g.FillPolygon(cBr, pointer);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                // WinXP / 舊版 GDI+ 安全降級，絕不崩潰白屏
                try
                {
                    using (Font errFont = new Font(FontFamily.GenericSansSerif, 8f))
                    {
                        g.DrawString("S6 週期圖解: " + ex.Message, errFont, Brushes.Red, 5, 5);
                    }
                }
                catch { }
            }
        }

        private void DrawDimArrow(Graphics g, int x, int y, bool isLeft)
        {
            Point[] arrow = isLeft
                ? new Point[] { new Point(x, y), new Point(x + 5, y - 3), new Point(x + 5, y + 3) }
                : new Point[] { new Point(x, y), new Point(x - 5, y - 3), new Point(x - 5, y + 3) };
            g.FillPolygon(Brushes.Black, arrow);
        }

        // =========================================================================
        // 工作制測試邏輯 (S1 / S2 / S6 含自適應轉矩閉迴路與 V/F 轉差雙重定錨補償)
        // =========================================================================
        private void StopDutyTest()
        {
            ResetDutyTestToStandby();
            if (isManualRecording)
            {
                StopManualRecording(showPrompt: false);
            }
            if (isBatchDutyActive)
            {
                isBatchDutyActive = false;
                batchDutyStage = 0;
                WriteHmiLog("BATCH_STOP", "使用者手動中止連續性自動排程！");
                if (lblBatchProgressDetail != null)
                {
                    lblBatchProgressDetail.Text = "【排程手動停止】使用者已手動中止連續排程，雙載台已安全卸載。";
                }
            }

            // 幽靈日誌防禦：若已有足夠採樣數據 (超過10筆/50秒) 但中途被手動停止，標記為 ABORTED 存檔，避免混淆正式合格數據
            try
            {
                if (dutyResults != null && dutyResults.Count > 10)
                {
                    int mIdx = (cmbDutyMode != null && cmbDutyMode.SelectedIndex >= 0) ? cmbDutyMode.SelectedIndex : 0;
                    string pfx = (mIdx == 0) ? "S1" : ((mIdx == 1) ? "S2" : "S6");
                    if (mIdx == 1 && dutyElapsedSec >= 30)
                    {
                        RecordAndExtrapolateS2();
                    }
                    else if (mIdx == 2 && (s6CurrentCyclePeakTemp > -100 || s6PeakTempHistory.Count > 0))
                    {
                        double pT = (s6CurrentCyclePeakTemp > -100) ? s6CurrentCyclePeakTemp : (s6PeakTempHistory.Count > 0 ? s6PeakTempHistory[s6PeakTempHistory.Count - 1] : GetCurrentPeakTemp());
                        RecordAndExtrapolateS6(pT);
                    }
                    SaveDutyTestReportCsv(pfx, showDialog: false, statusTag: "ABORTED");
                }
            }
            catch { }
            isS2Calibrating = false;
            isLoadMotorPreEnergized = false;
            dutyMinuteVoltSum = 0.0;
            dutyMinuteCurrSum = 0.0;
            dutyMinutePelecSum = 0.0;
            dutyMinutePfSum = 0.0;
            dutyMinuteSpdSum = 0.0;
            dutyMinuteTrqSum = 0.0;
            dutyMinuteSampleCount = 0;
            if (lblDutyRealtimeElec != null)
                lblDutyRealtimeElec.Text = "【即時電氣監控】已停止測試 | 待命準備中";
            dutyExciteStage = 0;
            dutyExciteWaitSec = 0;
            dutyRampStep = 0;
            dutyRampReachedTime = DateTime.MinValue;
            s6SpeedReached = false;
            s6DutyPhase = 0;
            s6TrialStage = 0;
            s6TrialTimer = 0;
            s6TrialStageElapsedSec = 0;
            s6FormalCycleIndex = 1;
            s6CycleElapsedSec = 0;
            s6AdaptedTorquePct = 0.0;
            s6ProbeStep = 0;
            s6ProbeTorqueBase = 0.0;
            s6ProbeTorque1 = 0.0;
            s6ProbeTorque2 = 0.0;
            s6ProbeTorque3 = 0.0;
            s6NmPerPointOnePct = 0.0;
            s6Stage2StableCounter = 0;

            int dutyRole = (cmbDutyMiniRole != null && cmbDutyMiniRole.SelectedIndex >= 0)
                ? cmbDutyMiniRole.SelectedIndex
                : ((cmbDutyRole != null && cmbDutyRole.SelectedIndex >= 0) ? cmbDutyRole.SelectedIndex : 1);
            int spdDrive = (dutyRole == 1) ? 2 : 1;
            int trqDrive = (spdDrive == 1) ? 2 : 1;

            StartGradualAutoStop(spdDrive, trqDrive, "手動終止工作制測試", () => {
                ResetDutyTestToStandby();
                if (lblDutyStatus != null) lblDutyStatus.Text = "狀態: 已手動停止";
                if (lblDutyPhaseAction != null) lblDutyPhaseAction.Text = "動作: 試驗已中斷，加載與轉速已平穩停機";
                if (lblDutyMiniStatus != null) lblDutyMiniStatus.Text = "狀態: 已停止";
                if (lblDutyMiniPhaseAction != null) lblDutyMiniPhaseAction.Text = "動作: 試驗已中斷";
                // ── 遠端監看重置 ──
                webRemoteMode = "IDLE"; webRemoteStatusText = "DUTY 測試已停止"; webRemotePhaseText = "";
                UploadLatestLogToCloudAsync(false);
            });
        }

        private void BtnStartDuty_Click(object sender, EventArgs e)
        {
            bool isFromMini = (sender == btnDutyMiniStart);
            if (chkBatchDutyMode != null && chkBatchDutyMode.Checked && !isBatchDutyActive)
            {
                isBatchDutyActive = true;
                int plan = GetBatchDutyPlan();
                if (plan == 3) // S6 -> S2
                {
                    batchDutyStage = 3;
                    cmbDutyMode.SelectedIndex = 2; // 強制由 S6 開始
                    if (prgBatchTotal != null) { prgBatchTotal.Maximum = 3; prgBatchTotal.Value = 1; }
                    WriteHmiLog("BATCH_START", "【連續排程啟動】即將執行 S6 -> S2 兩項工作制連續測試！");
                    if (lblBatchProgressDetail != null)
                    {
                        lblBatchProgressDetail.Text = "【連續排程 1/3】S6 週期工作制運轉中 (連續 3 週期熱平衡判定中)...";
                    }
                }
                else
                {
                    batchDutyStage = 1;
                    cmbDutyMode.SelectedIndex = 0; // 強制由 S1 開始
                    int maxSteps = (plan == 0) ? 5 : 3;
                    if (prgBatchTotal != null) { prgBatchTotal.Maximum = maxSteps; prgBatchTotal.Value = 1; }
                    string planDesc = (plan == 0) ? "S1 -> S6 -> S2 (三項全測)" : ((plan == 1) ? "S1 -> S6 (額定 + 週期)" : "S1 -> S2 (額定 + 短時)");
                    WriteHmiLog("BATCH_START", string.Format("【連續排程啟動】即將執行 {0} 連續工作制測試！", planDesc));
                    if (lblBatchProgressDetail != null)
                    {
                        lblBatchProgressDetail.Text = string.Format("【連續排程 1/{0}】S1 連續工作制運轉中 (熱平衡 30m 溫差 < 1.0℃ 判定中)...", maxSteps);
                    }
                }
            }
            StartDutyTestInternal(isFromMini, false);
        }

        private void StartDutyTestInternal(bool fromMini = false, bool isBatchTrigger = false)
        {
            // 安全檢查：全自動控制要求雙機 100% 同時連線
            if (!isHmiKebOpen1 || !isHmiKebOpen2)
            {
                MessageBox.Show(
                    "【[!] 全自動控制安全拒絕】\n\n" +
                    "執行 DUTY 工作制測試由上位機全權掌控 ST 運轉與加載！\n" +
                    "為防止通訊失效或失控飛車，系統強制要求【雙載台 (COM1 與 COM2) 必須 100% 同時連線】！\n\n" +
                    "目前檢測到尚有一側未連線，請先至主畫面點擊 [Open] 連線後再開始測試。",
                    "雙機連線安全互鎖",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning
                );
                return;
            }

            dutyResults.Clear();
            dutyElapsedSec = 0;
            s6DutyPhase = 0; // 0: 自適應試運轉定錨階段, 1: 正式週期循環階段
            s6TrialStage = 0; // 0: 空載提速中
            s6TrialTimer = 0;
            s6TrialStageElapsedSec = 0;
            s6FormalCycleIndex = 1;
            s6CycleElapsedSec = 0;
            s6SpeedReached = false;
            s6ProbeStep = 0;
            s6ProbeTorqueBase = 0.0;
            s6ProbeTorque1 = 0.0;
            s6ProbeTorque2 = 0.0;
            s6ProbeTorque3 = 0.0;
            s6NmPerPointOnePct = 0.0;
            s6Stage2StableCounter = 0;
            s6PeakTempHistory.Clear();
            s6TroughTempHistory.Clear();
            s6CurrentCyclePeakTemp = -999.0;
            s6CurrentCycleTroughTemp = 999.0;
            s6LastCyclePeakTemp = 0.0;
            s6LastCycleTroughTemp = 0.0;
            s6ThermalBalanced = false;
            s6IsVerifyingConfirmationCycle = false;
            s6ConfirmationCycleIndex = 0;
            s6TempHistory.Clear();
            if (lblS6ThermalStatus != null)
            {
                lblS6ThermalStatus.Text = "S6 熱平衡: 採樣中 (30分鐘穩定判定 + 1週期再次確認)";
                lblS6ThermalStatus.ForeColor = Color.DarkOrange;
            }
            if (dutyTempTrend != null) dutyTempTrend.ClearData();

            isDutyBaselineEstablished = false;
            dutyBaselineCurrentSigma = 0.0;
            dutyBaselineCurrentDrive = 0.0;
            dutyOverCurrentStartTime = DateTime.MinValue;
            dutySigmaCurrentSamples.Clear();
            dutyDriveCurrentSamples.Clear();
            if (lblDutyCurrentBaseline != null) lblDutyCurrentBaseline.Text = "基準: 尚未確立 (採樣中)";
            dutyLastMinuteLogged = -1;
            dutyMinuteVoltSum = 0.0;
            dutyMinuteCurrSum = 0.0;
            dutyMinutePelecSum = 0.0;
            dutyMinutePfSum = 0.0;
            dutyMinuteSpdSum = 0.0;
            dutyMinuteTrqSum = 0.0;
            dutyMinuteSampleCount = 0;
            if (dgvDuty != null) dgvDuty.Rows.Clear();
            if (lblDutyRealtimeElec != null)
                lblDutyRealtimeElec.Text = "【即時電氣監控】平均電壓: --.- V | 平均電流: --.-- A | 輸入功率: --.-- kW | 功因: -.--- | 輸出功率: --.-- kW | 效率: --.- %";

            bool isFromMini = fromMini;
            SyncAllDutyControls(fromMiniToMain: isFromMini);

            int dutyModeIdxCheck = (cmbDutyMode != null && cmbDutyMode.SelectedIndex >= 0) ? cmbDutyMode.SelectedIndex : 2;
            if (dutyModeIdxCheck == 0)
            {
                if (numS1Speed != null) numDutySpeed.Value = numS1Speed.Value;
                if (numS1Torque != null) numDutyTorque.Value = numS1Torque.Value;
            }
            else if (dutyModeIdxCheck == 1)
            {
                if (numS2Speed != null) numDutySpeed.Value = numS2Speed.Value;
                if (numS2Torque != null) numDutyTorque.Value = numS2Torque.Value;
            }
            else if (dutyModeIdxCheck == 2)
            {
                if (numS6Speed != null) numDutySpeed.Value = numS6Speed.Value;
                if (numS6Torque != null) numDutyTorque.Value = numS6Torque.Value;
            }
            SetDutyControlsReadOnly(true);
            if (txtDutyPreflightSummary != null) txtDutyPreflightSummary.Visible = false;
            if (pnlDutyLiveMonitor != null) { pnlDutyLiveMonitor.Visible = true; pnlDutyLiveMonitor.BringToFront(); }
            UpdateDutyLiveDashboard();

            int dutyRole = (cmbDutyRole != null && cmbDutyRole.SelectedIndex >= 0) ? cmbDutyRole.SelectedIndex : 1;
            int spdDrive = (dutyRole == 1) ? 2 : 1;
            int trqDrive = (spdDrive == 1) ? 2 : 1;

            int spdCom = GetHmiKebComIdx(spdDrive), spdBaud = GetHmiKebBaudIdx(spdDrive), spdNode = (spdDrive == 1) ? (int)numHmiKebNode1.Value : (int)numHmiKebNode2.Value;
            int trqCom = GetHmiKebComIdx(trqDrive), trqBaud = GetHmiKebBaudIdx(trqDrive), trqNode = (trqDrive == 1) ? (int)numHmiKebNode1.Value : (int)numHmiKebNode2.Value;
            string spdDriveName = (spdDrive == 1) ? "A載台" : "B載台";
            string trqDriveName = (trqDrive == 1) ? "A載台" : "B載台";

            // 【啟動前變頻器狀態與故障防護校驗】：
            // 1. 檢查硬體 ST 安全端子 (ru.00 == 0 代表 Control Release 未閉合 / nOP)
            // 2. 檢查變頻器真實硬體故障碼 (ru.43 != 0 代表硬體報警)
            int? curRuSpd = KebReadParamWithDll(spdCom, spdBaud, spdNode, 0x0200);
            int? curRuTrq = KebReadParamWithDll(trqCom, trqBaud, trqNode, 0x0200);
            int? curFaultSpd = KebReadParamWithDll(spdCom, spdBaud, spdNode, 0x022B);
            int? curFaultTrq = KebReadParamWithDll(trqCom, trqBaud, trqNode, 0x022B);

            if (curRuSpd.HasValue && curRuSpd.Value == 0)
            {
                ResetDutyTestToStandby();
                MessageBox.Show(string.Format("【[!] 硬體ST端子未閉合】\n\n{0} 目前處於 0: nOP (Control Release 斷開)！\n請先閉合機櫃實體 ST 端子開關以放行功率級。", spdDriveName),
                    "硬體安全端子未閉合", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            if (curRuTrq.HasValue && curRuTrq.Value == 0)
            {
                ResetDutyTestToStandby();
                MessageBox.Show(string.Format("【[!] 硬體ST端子未閉合】\n\n{0} 目前處於 0: nOP (Control Release 斷開)！\n請先閉合機櫃實體 ST 端子開關以放行功率級。", trqDriveName),
                    "硬體安全端子未閉合", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            // 若任一側處於真實故障狀態 (ru.43 != 0)，嘗試自動發送 FAULT RESET (Sy.50=2  Sy.50=0)
            if (curFaultSpd.HasValue && curFaultSpd.Value != 0)
            {
                WriteHmiLog("DUTY_WARN", string.Format("【啟動前故障警告】{0} 目前處於故障狀態 (ru.43={1}: {2})，自動發送 FAULT RESET...", spdDriveName, curFaultSpd.Value, DecodeKebFaultCode(curFaultSpd.Value)));
                KebWriteParamWithDll(spdCom, spdBaud, spdNode, 0x0032, 2);
                Thread.Sleep(80);
                KebWriteParamWithDll(spdCom, spdBaud, spdNode, 0x0032, 0);
                Thread.Sleep(80);
                curFaultSpd = KebReadParamWithDll(spdCom, spdBaud, spdNode, 0x022B);
            }
            if (curFaultTrq.HasValue && curFaultTrq.Value != 0)
            {
                WriteHmiLog("DUTY_WARN", string.Format("【啟動前故障警告】{0} 目前處於故障狀態 (ru.43={1}: {2})，自動發送 FAULT RESET...", trqDriveName, curFaultTrq.Value, DecodeKebFaultCode(curFaultTrq.Value)));
                KebWriteParamWithDll(trqCom, trqBaud, trqNode, 0x0032, 2);
                Thread.Sleep(80);
                KebWriteParamWithDll(trqCom, trqBaud, trqNode, 0x0032, 0);
                Thread.Sleep(80);
                curFaultTrq = KebReadParamWithDll(trqCom, trqBaud, trqNode, 0x022B);
            }

            if ((curFaultSpd.HasValue && curFaultSpd.Value != 0) || (curFaultTrq.HasValue && curFaultTrq.Value != 0))
            {
                ResetDutyTestToStandby();
                string faultMsg = (curFaultSpd.HasValue && curFaultSpd.Value != 0)
                    ? string.Format("{0} 故障未除 (ru.43={1}: {2})", spdDriveName, curFaultSpd.Value, DecodeKebFaultCode(curFaultSpd.Value))
                    : string.Format("{0} 故障未除 (ru.43={1}: {2})", trqDriveName, curFaultTrq.Value, DecodeKebFaultCode(curFaultTrq.Value));
                WriteHmiLog("DUTY_ABORT", string.Format("【啟動拒絕】{0}，自動復歸無效，終止啟動！", faultMsg));
                MessageBox.Show(
                    string.Format("【[!] 變頻器故障拒絕啟動】\n\n檢測到 {0}！\n系統已嘗試自動復歸但故障無法排除。\n請至變頻器檢查故障碼並排除硬體異常後再試。", faultMsg),
                    "變頻器故障拒絕啟動",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error
                );
                return;
            }

            // 啟動前刷新扭力計心跳時間，防止啟動瞬間因零速無封包觸發保護
            lastTorquePacketTime = DateTime.Now;

            //  強制自動將雙機切換為全自動控制模式 (待測端 Mode 9 / 加載端 Mode 10)
            SetHmiKebMode(spdCom, spdBaud, spdNode, 9, spdDriveName + " (DUTY全自動速度)");
            if (spdDrive == 1) currentKebMode1 = 9; else currentKebMode2 = 9;

            SetHmiKebMode(trqCom, trqBaud, trqNode, 10, trqDriveName + " (DUTY全自動轉矩)");
            if (trqDrive == 1) currentKebMode1 = 10; else currentKebMode2 = 10;

            UpdateHmiKebModeButtonsVisual();
            WriteHmiLog("DUTY_MODE", string.Format("【DUTY啟動自動切換模式】{0}已自動切換為 Mode 9 (全自動速度)，{1}已自動切換為 Mode 10 (全自動轉矩)！", spdDriveName, trqDriveName));

            int dutyModeIdx = (cmbDutyMode != null && cmbDutyMode.SelectedIndex >= 0) ? cmbDutyMode.SelectedIndex : 2;
            string dutyTag = "S6";
            if (dutyModeIdx == 0) dutyTag = "S1";
            else if (dutyModeIdx == 1) dutyTag = "S2";
            else if (dutyModeIdx == 2) dutyTag = "S6";

            //  自動開啟 RAW DATA 記錄 (檔名後綴: _S1, _S2, _S6)
            StartAutoRawRecordingWithTag(dutyTag);

            if (s1TempHistory != null) s1TempHistory.Clear();
            if (s2TempHistory != null) s2TempHistory.Clear();

            double targetSpd = (numDutySpeed != null && numDutySpeed.Value > 0) ? (double)numDutySpeed.Value : 1000.0;
            double targetTrq = (numDutyTorque != null && numDutyTorque.Value > 0) ? (double)numDutyTorque.Value : 15.0;

            if (dutyModeIdx == 0) // S1 連續工作制 (最長可運轉 20 小時，或依溫度 30 分鐘溫差 < 1.0℃ 自動停機)
            {
                dutyTotalSec = 72000;
                s1LoadTracker.Reset(targetSpd, 0.0);
                s6CurrentSpeedCmd = targetSpd;
                s6AdaptedTorquePct = 0.0;
                s1NoSlipCompleted = false;
                isS1RecordingNoSlip = false;
                s1NoSlipSampleCount = 0;
                if (s1NoSlipBuffer != null) s1NoSlipBuffer.Clear();
            }
            else if (dutyModeIdx == 1) // S2 短時工作制 (含錨點測試機制)
            {
                int curSy52 = (numS2AnchorSy52 != null) ? (int)numS2AnchorSy52.Value : s2AnchorSy52;
                int curCs18 = (numS2AnchorCs18 != null) ? (int)numS2AnchorCs18.Value : s2AnchorCs18;

                if (curSy52 == 0 || curCs18 == 0)
                {
                    // 錨點數值為 0  執行錨點偵測測試 (採用統一自適應加速定錨引擎)
                    isS2Calibrating = true;
                    s2CalibStage = 1; // 統一引擎自適應加載階段 (內含自動空載提速與熱備妥激磁)
                    s2CalibTimer = 10;
                    dutyTotalSec = 300; // 最多給予 5 分鐘進行錨點校驗
                    s2LoadTracker.Reset(targetSpd, 0.0);
                    s6CurrentSpeedCmd = targetSpd;
                    s6AdaptedTorquePct = 0.0;
                }
                else
                {
                    // 錨點數值 > 0  直接套用記錄/輸入之錨點運轉！
                    isS2Calibrating = false;
                    s2AnchorSy52 = curSy52;
                    s2AnchorCs18 = curCs18;
                    int durationMin = (numS2DurationMin != null) ? (int)numS2DurationMin.Value : 30;
                    dutyTotalSec = Math.Max(60, durationMin * 60);
                    s6CurrentSpeedCmd = (double)s2AnchorSy52;
                    s6AdaptedTorquePct = (double)s2AnchorCs18 / 10.0;
                    s2LoadTracker.Reset(s6CurrentSpeedCmd, s6AdaptedTorquePct);
                }
            }
            else // S6 週期負載 (套用既有錨點或自適應試運轉定錨)
            {
                double cycleMin = (double)(numS6CycleMin != null ? numS6CycleMin.Value : 10);
                int totalCycles = (int)(numS6Cycles != null ? numS6Cycles.Value : 4);
                int oneCycleSec = Math.Max(60, (int)(cycleMin * 60));

                double curNoLoadSpd = (numS6AnchorNoLoadSpd != null && numS6AnchorNoLoadSpd.Value > 0) ? (double)numS6AnchorNoLoadSpd.Value : s6AnchorNoLoadSpeed;
                double curLoadedSpd = (numS6AnchorLoadedSpd != null && numS6AnchorLoadedSpd.Value > 0) ? (double)numS6AnchorLoadedSpd.Value : s6AnchorLoadedSpeed;
                double curLoadedTrqPct = (numS6AnchorLoadedCs18 != null && numS6AnchorLoadedCs18.Value > 0) ? ((double)numS6AnchorLoadedCs18.Value / 10.0) : s6AnchorLoadedTorquePct;

                bool hasValidS6Anchors = (curNoLoadSpd > 0 && curLoadedSpd > 0 && curLoadedTrqPct > 0);

                if (hasValidS6Anchors)
                {
                    s6AnchorNoLoadSpeed = curNoLoadSpd;
                    s6AnchorLoadedSpeed = curLoadedSpd;
                    s6AnchorLoadedTorquePct = curLoadedTrqPct;
                    s6HasNoLoadAnchor = true;
                    s6HasLoadedAnchor = true;

                    dutyTotalSec = oneCycleSec * totalCycles; // 直接進行正式循環，不需自適應試運轉
                    s6DutyPhase = 1; // 直接進入正式循環
                    s6CurrentSpeedCmd = s6AnchorLoadedSpeed;
                    s6AdaptedTorquePct = s6AnchorLoadedTorquePct;
                    WriteHmiLog("S6_START", string.Format("【S6套用既有錨點】空載轉速={0:F0} rpm, 加載轉速={1:F0} rpm, 加載轉矩={2:F1}%，直接進入正式週期循環 (共 {3} 週期)！", s6AnchorNoLoadSpeed, s6AnchorLoadedSpeed, s6AnchorLoadedTorquePct, totalCycles));
                }
                else
                {
                    dutyTotalSec = oneCycleSec * (totalCycles + 1); // 包含 1 次自適應試運轉
                    s6DutyPhase = 0; // 自適應試運轉定錨階段
                    s6CurrentSpeedCmd = (curNoLoadSpd > 0) ? curNoLoadSpd : targetSpd;
                    s6AdaptedTorquePct = (curLoadedTrqPct > 0) ? curLoadedTrqPct : 0.0;
                    s6HasNoLoadAnchor = (curNoLoadSpd > 0);
                    s6HasLoadedAnchor = (curLoadedSpd > 0 && curLoadedTrqPct > 0);
                    WriteHmiLog("S6_START", string.Format("【S6錨點檢查】空載SY52={0:F0}, 加載SY52={1:F0}, CS18={2:F1}%。開始執行自適應試運轉定錨流程...", curNoLoadSpd, curLoadedSpd, s6AdaptedTorquePct));
                }
                UpdateS6AnchorStatusText();
            }

            prgDuty.Minimum = 0;
            prgDuty.Maximum = dutyTotalSec;
            prgDuty.Value = 0;
            if (prgDutyMini != null) { prgDutyMini.Minimum = 0; prgDutyMini.Maximum = dutyTotalSec; prgDutyMini.Value = 0; }

            btnStartDuty.Enabled = false;
            btnStopDuty.Enabled = true;
            if (btnDutyMiniStart != null) btnDutyMiniStart.Enabled = false;
            if (btnDutyMiniStop != null) btnDutyMiniStop.Enabled = true;

            // 同步主畫面數值輸入框
            if (spdDrive == 2)
            {
                if (numHmiKebSpeed2 != null) numHmiKebSpeed2.Value = (decimal)Math.Max(0, Math.Min(6000, s6CurrentSpeedCmd));
                if (numHmiKebTorque2 != null) numHmiKebTorque2.Value = (decimal)s6AdaptedTorquePct;
                if (numHmiKebSpeed1 != null) numHmiKebSpeed1.Value = 0;
                if (numHmiKebTorque1 != null) numHmiKebTorque1.Value = 0;
            }
            else
            {
                if (numHmiKebSpeed1 != null) numHmiKebSpeed1.Value = (decimal)Math.Max(0, Math.Min(6000, s6CurrentSpeedCmd));
                if (numHmiKebTorque1 != null) numHmiKebTorque1.Value = (decimal)s6AdaptedTorquePct;
                if (numHmiKebSpeed2 != null) numHmiKebSpeed2.Value = 0;
                if (numHmiKebTorque2 != null) numHmiKebTorque2.Value = 0;
            }

            // 【使用者現場測試黃金規範】：
            // 若設定額定轉速 <= 300 rpm，直接加速不慢慢加速；
            // 若 > 300 rpm，待測端先停在 300 rpm，讓加載端激磁不給轉矩量 (Sy50=4, cs18=0)，等待 3 秒，隨後分 4 段梯度加速至需求轉速！
            bool needSlowExciteDuty = (targetSpd > DUTY_EXCITE_BASE_RPM);
            if (needSlowExciteDuty)
            {
                dutyExciteStage = 1; // 1: 待測端升至 300 rpm, 2: 300 rpm 加載端激磁等待 3 秒, 3: 4 段梯度加速
                dutyExciteWaitSec = 0;
                dutyRampStep = 0;
                dutyRampReachedTime = DateTime.MinValue;
                dutyRampSpeeds[0] = Math.Round(300.0 + (targetSpd - 300.0) * 0.25);
                dutyRampSpeeds[1] = Math.Round(300.0 + (targetSpd - 300.0) * 0.50);
                dutyRampSpeeds[2] = Math.Round(300.0 + (targetSpd - 300.0) * 0.75);
                dutyRampSpeeds[3] = targetSpd;

                // 加載端預備：轉矩強制為 0，尚未到達 300 rpm 前保持停機待命 (Sy50=0)
                KebWriteParamWithDll(trqCom, trqBaud, trqNode, 0x0F12, 0);
                SetHmiKebCommand(trqCom, trqBaud, trqNode, 0, string.Format("{0}待命待測端起轉至 300 rpm (Sy50=0, cs18=0%)", trqDriveName));

                // 待測端起步：目標先給 300 rpm！
                KebWriteParam32(spdCom, spdBaud, spdNode, 0x0034, (int)DUTY_EXCITE_BASE_RPM, "待測端起轉停在 300 rpm (Sy52=300)");
                KebWriteParamWithDll(spdCom, spdBaud, spdNode, 0x0F12, 1000); // 放行 100% 轉矩
                SetHmiKebCommand(spdCom, spdBaud, spdNode, 4, "待測端正轉至 300 rpm (Sy50=4)");

                lblDutyStatus.Text = string.Format("【低速起轉】待測端起轉至 300 rpm 中 (目標 {0:F0} rpm)...", targetSpd);
                lblDutyPhaseAction.Text = "待測端起步至 300 rpm，準備加載端激磁等待 3 秒...";
            }
            else
            {
                // 額定轉速 <= 300 rpm：直接加速，加載端同步激磁
                dutyExciteStage = 0;
                dutyExciteWaitSec = 0;
                dutyRampStep = 0;

                KebWriteParamWithDll(trqCom, trqBaud, trqNode, 0x0F12, 0);
                SetHmiKebCommand(trqCom, trqBaud, trqNode, 4, string.Format("{0}加載端激磁 (Sy50=4, cs18=0%)", trqDriveName));

                KebWriteParam32(spdCom, spdBaud, spdNode, 0x0034, (int)s6CurrentSpeedCmd, "待測端直接提速 (Sy52)");
                KebWriteParamWithDll(spdCom, spdBaud, spdNode, 0x0F12, 1000);
                SetHmiKebCommand(spdCom, spdBaud, spdNode, 4, "待測端正轉 (Sy50=4)");
            }

            // 啟轉後短暫等待並校驗變頻器真實故障碼 ru.43 (0x022B)，若硬體跳脫則中止
            Thread.Sleep(80);
            int? postRunFault = KebReadParamWithDll(spdCom, spdBaud, spdNode, 0x022B);
            if (postRunFault.HasValue && postRunFault.Value != 0)
            {
                SetHmiKebCommand(spdCom, spdBaud, spdNode, 0, "啟轉故障停機");
                SetHmiKebCommand(trqCom, trqBaud, trqNode, 0, "加載端同步停機");
                WriteHmiLog("DUTY_ABORT", string.Format("【[!] 啟轉失敗】{0} 啟轉後突發變頻器硬體故障 (ru.43={1}: {2})，已強制停機並終止測試！",
                    spdDriveName, postRunFault.Value, DecodeKebFaultCode(postRunFault.Value)));
                MessageBox.Show(
                    string.Format("【[!] 變頻器啟轉故障】\n\n{0} 下達運轉指令後檢測到故障報警 (ru.43={1}: {2})！\n測試已自動中止並強制停機。",
                        spdDriveName, postRunFault.Value, DecodeKebFaultCode(postRunFault.Value)),
                    "變頻器啟轉異常",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error
                );
                StopDutyTest();
                return;
            }

            if (!isRunning) BtnStart_Click(null, null);

            string modeDesc = (dutyModeIdx == 0) ? "S1 連續工作制" : ((dutyModeIdx == 1) ? (isS2Calibrating ? "S2 錨點偵測測試" : "S2 短時工作制") : "S6 週期工作制");
            if (dutyModeIdx == 0)
            {
                lblDutyStatus.Text = "【S1 連續工作制】恆定負載運轉中...";
                lblDutyPhaseAction.Text = string.Format("目標轉速 {0:F0} rpm，目標轉矩 {1:F1} Nm (監控 30min溫差<1.0℃ 熱平衡)", targetSpd, targetTrq);
            }
            else if (dutyModeIdx == 1)
            {
                if (isS2Calibrating)
                {
                    lblDutyStatus.Text = "【S2 錨點測試】按照目標轉速與轉矩運轉偵測錨點中...";
                    lblDutyPhaseAction.Text = string.Format("目標轉速 {0:F0} rpm，目標轉矩 {1:F1} Nm (到位穩定10秒後記錄並停機)", targetSpd, targetTrq);
                }
                else
                {
                    int durationMin = (numS2DurationMin != null) ? (int)numS2DurationMin.Value : 30;
                    lblDutyStatus.Text = string.Format("【S2 短時工作制】已套用錨點運轉中 (時長 {0} 分鐘)...", durationMin);
                    lblDutyPhaseAction.Text = string.Format("套用錨點 SY52={0} rpm，加載 CS18={1} ‰ ({2:F1}%)", s2AnchorSy52, s2AnchorCs18, s6AdaptedTorquePct);
                }
            }
            else
            {
                lblDutyStatus.Text = "【自適應試運轉】空載提速中，等待轉速到位...";
                lblDutyPhaseAction.Text = "待測端空載加速至設定轉速中，加載端保持 0 轉矩待命...";
            }
            if (lblDutyMiniStatus != null) lblDutyMiniStatus.Text = lblDutyStatus.Text;
            if (lblDutyMiniPhaseAction != null) lblDutyMiniPhaseAction.Text = lblDutyPhaseAction.Text;

            WriteHmiLog("DUTY_CONFIG", string.Format("【工作制啟動測試】模式: {0} | 測試配置: {1}待測 (速度) / {2}加載 (轉矩) | 目標轉速: {3:F0} rpm, 目標轉矩: {4:F1} Nm, 總秒數: {5}s",
                modeDesc, spdDriveName, trqDriveName, targetSpd, targetTrq, dutyTotalSec));
            // ── 遠端監看同步 ──
            webRemoteMode = "DUTY-" + dutyTag;
            webRemoteStatusText = lblDutyStatus.Text;
            webRemotePhaseText  = lblDutyPhaseAction.Text;

            dutyTimer.Start();
        }

        /// <summary>
        /// 計算 S1 所有勾選監測通道最高溫在最近 windowSec 秒內的溫升斜率 (°C/min)
        /// </summary>
        private double CalculateS1ThermalSlopePerMin(List<KeyValuePair<DateTime, double[]>> history, bool[] monitoredChs, int windowSec)
        {
            if (history == null || history.Count < 2) return 0.0;
            DateTime now = history[history.Count - 1].Key;
            DateTime cutoff = now.AddSeconds(-windowSec);
            var window = history.Where(x => x.Key >= cutoff).OrderBy(x => x.Key).ToList();
            if (window.Count < 2) return 0.0;

            int n = window.Count;
            double sumT = 0, sumY = 0, sumTY = 0, sumT2 = 0;
            for (int i = 0; i < n; i++)
            {
                double t = (window[i].Key - cutoff).TotalSeconds;
                double maxT = double.MinValue;
                double[] arr = window[i].Value;
                if (arr != null)
                {
                    for (int ch = 0; ch < 20 && ch < arr.Length; ch++)
                    {
                        if (monitoredChs != null && ch < monitoredChs.Length && monitoredChs[ch])
                        {
                            if (arr[ch] > maxT) maxT = arr[ch];
                        }
                    }
                }
                if (maxT == double.MinValue) maxT = 0;
                double y = maxT;
                sumT += t;
                sumY += y;
                sumTY += t * y;
                sumT2 += t * t;
            }
            double denom = n * sumT2 - sumT * sumT;
            if (Math.Abs(denom) < 1e-9) return 0.0;
            double slopePerSec = (n * sumTY - sumT * sumY) / denom;
            return slopePerSec * 60.0; // °C/min
        }

        /// <summary>
        /// 計算單通道溫度歷史佇列在最近 windowSec 秒內的溫升斜率 (°C/min)
        /// </summary>
        private double CalculateThermalSlopePerMin(List<KeyValuePair<DateTime, double>> history, int windowSec)
        {
            if (history == null || history.Count < 2) return 0.0;
            DateTime now = history[history.Count - 1].Key;
            DateTime cutoff = now.AddSeconds(-windowSec);
            var window = history.Where(x => x.Key >= cutoff).OrderBy(x => x.Key).ToList();
            if (window.Count < 2) return 0.0;

            int n = window.Count;
            double sumT = 0, sumY = 0, sumTY = 0, sumT2 = 0;
            for (int i = 0; i < n; i++)
            {
                double t = (window[i].Key - cutoff).TotalSeconds;
                double y = window[i].Value;
                sumT += t;
                sumY += y;
                sumTY += t * y;
                sumT2 += t * t;
            }
            double denom = n * sumT2 - sumT * sumT;
            if (Math.Abs(denom) < 1e-9) return 0.0;
            double slopePerSec = (n * sumTY - sumT * sumY) / denom;
            return slopePerSec * 60.0; // °C/min
        }

        private void DutyTimer_Tick(object sender, EventArgs e)
        {
            if (isBatchDutyActive && (batchDutyStage == 2 || batchDutyStage == 4))
            {
                ProcessBatchDutyCooling();
                return;
            }

            int dutyRole = (cmbDutyMiniRole != null && cmbDutyMiniRole.SelectedIndex >= 0)
                ? cmbDutyMiniRole.SelectedIndex
                : ((cmbDutyRole != null && cmbDutyRole.SelectedIndex >= 0) ? cmbDutyRole.SelectedIndex : 1);
            int spdDrive = (dutyRole == 1) ? 2 : 1;
            int trqDrive = (spdDrive == 1) ? 2 : 1;
            int spdCom = GetHmiKebComIdx(spdDrive), spdBaud = GetHmiKebBaudIdx(spdDrive), spdNode = (spdDrive == 1) ? (int)numHmiKebNode1.Value : (int)numHmiKebNode2.Value;
            int trqCom = GetHmiKebComIdx(trqDrive), trqBaud = GetHmiKebBaudIdx(trqDrive), trqNode = (trqDrive == 1) ? (int)numHmiKebNode1.Value : (int)numHmiKebNode2.Value;
            string spdDriveName = (spdDrive == 1) ? "A載台" : "B載台";
            string trqDriveName = (trqDrive == 1) ? "A載台" : "B載台";

            int dutyModeIdx = (cmbDutyMiniMode != null && cmbDutyMiniMode.SelectedIndex >= 0)
                ? cmbDutyMiniMode.SelectedIndex
                : ((cmbDutyMode != null && cmbDutyMode.SelectedIndex >= 0) ? cmbDutyMode.SelectedIndex : 2);

            DateTime now = DateTime.Now;
            double[] chSnapshot = new double[20];
            if (gbdChTemps != null && gbdChTemps.Length >= 20)
                Array.Copy(gbdChTemps, chSnapshot, 20);

            double targetSpd = (numDutySpeed != null && numDutySpeed.Value > 0) ? (double)numDutySpeed.Value : 1000.0;
            double targetTrq = (numDutyTorque != null && numDutyTorque.Value > 0) ? (double)numDutyTorque.Value : 15.0;

            double actAbsSpd = Math.Abs(actSpeed);
            double actAbsTrq = Math.Abs(actTorque);

            // 累加一分鐘滑動均值採樣 (平均電壓、平均電流、輸入電功率、功因、轉速、轉矩)
            dutyMinuteSpdSum += actAbsSpd;
            dutyMinuteTrqSum += actAbsTrq;
            if (actVoltageSigma > 1.0)
            {
                dutyMinuteVoltSum += actVoltageSigma;
                dutyMinuteCurrSum += actCurrentSigma;
                dutyMinutePelecSum += actElecPower;
                dutyMinutePfSum += actPf;
                dutyMinuteSampleCount++;
            }

            // 更新下方即時電氣監控橫幅
            if (lblDutyRealtimeElec != null)
            {
                double curPmech = (actAbsSpd * actAbsTrq) / 9549.0;
                string vTxt = actVoltageSigma > 0.1 ? string.Format("{0:F1} V", actVoltageSigma) : "--.- V";
                string iTxt = actCurrentSigma > 0.05 ? string.Format("{0:F2} A", actCurrentSigma) : "--.-- A";
                string pInTxt = Math.Abs(actElecPower) > 0.01 ? string.Format("{0:F2} kW", actElecPower) : "--.-- kW";
                string pfTxt = Math.Abs(actPf) > 0.001 ? string.Format("{0:F3}", actPf) : "-.---";
                string pOutTxt = string.Format("{0:F2} kW", curPmech);
                string effTxt = (actEfficiency > 0.0 && actEfficiency <= 100.0) ? string.Format("{0:F1} %", actEfficiency) : "--.- %";

                lblDutyRealtimeElec.Text = string.Format("【即時電氣監控】平均電壓: {0} | 平均電流: {1} | 輸入功率: {2} | 功因: {3} | 輸出功率: {4} | 效率: {5}",
                    vTxt, iTxt, pInTxt, pfTxt, pOutTxt, effTxt);
            }
            UpdateDutyLiveDashboard();

            // 【DUTY 變頻器硬體故障保護守護】：檢測任一載台是否處於真實硬體故障 (ru.43 != 0)
            if (lastKebFaultCode1 != 0 || lastKebFaultCode2 != 0)
            {
                int faultDrive = (lastKebFaultCode1 != 0) ? 1 : 2;
                int faultCode = (faultDrive == 1) ? lastKebFaultCode1 : lastKebFaultCode2;
                string faultDriveName = (faultDrive == 1) ? "A載台" : "B載台";
                WriteHmiLog("DUTY_ABORT", string.Format("【[!] DUTY 測試異常中斷】檢測到 {0} 突發硬體故障報警 (ru.43={1}: {2})，已強制安全卸載停機！",
                    faultDriveName, faultCode, DecodeKebFaultCode(faultCode)));
                StopDutyTest();
                return;
            }

            double dutyMonitoredTemp = 0.0;
            int maxChIdx = 0;
            if (dutyModeIdx == 0) // S1: 多選最高
            {
                double maxChTemp = -999.0;
                List<string> selectedChStrs = new List<string>();
                if (s1MonitoredChannels != null)
                {
                    for (int i = 0; i < s1MonitoredChannels.Length && i < chSnapshot.Length; i++)
                    {
                        if (s1MonitoredChannels[i])
                        {
                            if (chSnapshot[i] > maxChTemp)
                            {
                                maxChTemp = chSnapshot[i];
                                maxChIdx = i;
                            }
                            selectedChStrs.Add(string.Format("CH{0}:{1:F1}℃", i + 1, chSnapshot[i]));
                        }
                    }
                }
                dutyMonitoredTemp = (maxChTemp > -100.0) ? maxChTemp : actTemp;
                if (lblDutyTempRealtimeVal != null)
                    lblDutyTempRealtimeVal.Text = string.Format("實測最高: CH{0} {1:F1} ℃", maxChIdx + 1, dutyMonitoredTemp);

                if (lblDutyAllChTempsDisp != null)
                {
                    string allChs = (selectedChStrs.Count > 0) ? string.Join(" | ", selectedChStrs) : "未勾選通道";
                    lblDutyAllChTempsDisp.Text = "各通道: " + allChs;
                }
            }
            else if (dutyModeIdx == 1) // S2: 單選
            {
                int chIdx = (cmbS2TempCh != null && cmbS2TempCh.SelectedIndex >= 0) ? cmbS2TempCh.SelectedIndex : 0;
                maxChIdx = chIdx;
                dutyMonitoredTemp = (chIdx >= 0 && chIdx < chSnapshot.Length) ? chSnapshot[chIdx] : actTemp;
                if (lblDutyTempRealtimeVal != null) lblDutyTempRealtimeVal.Text = string.Format("實測: CH{0} {1:F1} ℃", chIdx + 1, dutyMonitoredTemp);
                if (lblDutyAllChTempsDisp != null) lblDutyAllChTempsDisp.Text = string.Format("監控通道: CH{0}", chIdx + 1);
            }
            else if (dutyModeIdx == 2) // S6: 單選
            {
                int chIdx = (cmbS6TempCh != null && cmbS6TempCh.SelectedIndex >= 0) ? cmbS6TempCh.SelectedIndex : 0;
                maxChIdx = chIdx;
                dutyMonitoredTemp = (chIdx >= 0 && chIdx < chSnapshot.Length) ? chSnapshot[chIdx] : actTemp;
                if (lblDutyTempRealtimeVal != null) lblDutyTempRealtimeVal.Text = string.Format("實測: CH{0} {1:F1} ℃", chIdx + 1, dutyMonitoredTemp);
                if (lblDutyAllChTempsDisp != null) lblDutyAllChTempsDisp.Text = string.Format("監控通道: CH{0}", chIdx + 1);
            }

            // 更新專屬動態溫度波形 (傳入各通道溫度陣列)
            bool isGbdOnline = (tcpGbd != null && tcpGbd.Connected);
            if (dutyTempTrend != null)
            {
                dutyTempTrend.IsConnected = isGbdOnline;
                if (isGbdOnline && chSnapshot != null)
                {
                    dutyTempTrend.AddSample(now, chSnapshot);
                }
            }

            string currentPhaseName = "運轉中";

            // 【使用者現場測試黃金規範狀態機】：
            // 階段 1：待測端起步停在 300 rpm，到達後讓加載端激磁不給轉矩量 (Sy50=4, cs18=0)
            if (dutyExciteStage == 1)
            {
                KebWriteParamWithDll(trqCom, trqBaud, trqNode, 0x0F12, 0); // cs.18 嚴格為 0%
                dutyExciteWaitSec++;

                bool reached300 = (actAbsSpd >= 260.0 && actAbsSpd <= 350.0) || (dutyExciteWaitSec >= 5 && actAbsSpd >= 200.0);
                if (reached300)
                {
                    // 到達 300 rpm！立即讓加載端激磁不給轉矩量 (Sy50=4, cs18=0%)
                    SetHmiKebCommand(trqCom, trqBaud, trqNode, 4, string.Format("{0}300rpm激磁導通 (Sy50=4, cs18=0%)", trqDriveName));
                    isLoadMotorPreEnergized = true;
                    dutyExciteStage = 2;
                    dutyExciteWaitSec = 0;
                    WriteHmiLog("DUTY_EXCITE", string.Format("【300 rpm 激磁啟動】待測端已停在 300 rpm (實測 {0:F0} rpm)，加載端 {1} 激磁導通 (Sy50=4, cs18=0%)，開始等待 3 秒建立穩定磁通...", actAbsSpd, trqDriveName));
                }
                lblDutyStatus.Text = string.Format("【低速起轉】待測端起轉至 300 rpm 中 (實測 {0:F0} rpm)...", actAbsSpd);
                lblDutyPhaseAction.Text = "待測端升速停在 300 rpm 中，加載端待命準備激磁...";
                if (lblDutyMiniStatus != null) lblDutyMiniStatus.Text = lblDutyStatus.Text;
                if (lblDutyMiniPhaseAction != null) lblDutyMiniPhaseAction.Text = lblDutyPhaseAction.Text;
                return;
            }
            // 階段 2：在 300 rpm 下讓加載端激磁不給轉矩量，精確等待 3 秒
            else if (dutyExciteStage == 2)
            {
                KebWriteParamWithDll(trqCom, trqBaud, trqNode, 0x0F12, 0); // cs.18 嚴格保持 0%
                dutyExciteWaitSec++;

                lblDutyStatus.Text = string.Format("【加載端激磁】停在 300 rpm 激磁等待中 ({0}/3s) | 實測 {1:F0} rpm, 轉矩 0%", dutyExciteWaitSec, actAbsSpd);
                lblDutyPhaseAction.Text = string.Format("加載端 {0} 300 rpm 磁場建立等待 3 秒 (轉矩 0.0%)，杜絕轉矩方向改變...", trqDriveName);
                if (lblDutyMiniStatus != null) lblDutyMiniStatus.Text = lblDutyStatus.Text;
                if (lblDutyMiniPhaseAction != null) lblDutyMiniPhaseAction.Text = lblDutyPhaseAction.Text;

                if (dutyExciteWaitSec >= 3)
                {
                    // 3 秒等待完成！啟動 4 段梯度加速
                    dutyExciteStage = 3;
                    dutyRampStep = 0;
                    dutyExciteWaitSec = 0;
                    dutyRampReachedTime = DateTime.MinValue;
                    WriteHmiLog("DUTY_EXCITE", string.Format("【300 rpm 激磁完成】加載端 {0} 磁場已在 300 rpm 穩定建立滿 3 秒，正式啟動 4 段梯度加速至目標 {1:F0} rpm", trqDriveName, targetSpd));
                }
                return;
            }
            // 階段 3：加速採用梯度上升，用設定的額定轉速分成 4 段加速，回饋轉速達到且實測轉矩轉正後即刻推進加速
            else if (dutyExciteStage == 3)
            {
                KebWriteParamWithDll(trqCom, trqBaud, trqNode, 0x0F12, 0); // 加速全程加載端維持 0 轉矩

                int curStepIdx = Math.Min(3, Math.Max(0, dutyRampStep));
                double rampTarget = dutyRampSpeeds[curStepIdx];

                if (dutyExciteWaitSec == 0)
                {
                    // 剛進入該段梯度：下達該段目標轉速
                    KebWriteParam32(spdCom, spdBaud, spdNode, 0x0034, (int)rampTarget, string.Format("DUTY梯度加速第 {0}/4 段 (Sy52={1:F0})", curStepIdx + 1, rampTarget));
                    WriteHmiLog("DUTY_EXCITE", string.Format("【4段梯度加速 {0}/4】下達轉速命令 {1:F0} rpm (加載端保持 cs18=0%)", curStepIdx + 1, rampTarget));
                    dutyRampReachedTime = DateTime.MinValue;
                }
                dutyExciteWaitSec++;

                // 判定實測回饋轉速是否到達該段目標
                bool isAtStepSpeed = (actAbsSpd >= rampTarget * 0.95);

                if (!isAtStepSpeed && dutyRampReachedTime == DateTime.MinValue)
                {
                    lblDutyStatus.Text = string.Format("【4段梯度加速】第 {0}/4 段：升速至 {1:F0} rpm (實測回饋 {2:F0} rpm) | 加載端零力矩跟隨", curStepIdx + 1, rampTarget, actAbsSpd);
                    lblDutyPhaseAction.Text = string.Format("梯度升速第 {0}/4 段推進中，等待回饋轉速到達目標...", curStepIdx + 1);
                }
                else
                {
                    if (dutyRampReachedTime == DateTime.MinValue)
                    {
                        dutyRampReachedTime = DateTime.Now;
                    }
                    double elapsedSec = (DateTime.Now - dutyRampReachedTime).TotalSeconds;

                    // 核心物理判據 (反轉矩與超速拖曳檢測)：
                    // 特徵 1: 實測轉矩小於 0 (actTorque < 0)
                    // 特徵 2: 轉速被加載端反向拖曳超速 (actAbsSpd > rampTarget + 15 rpm)
                    bool isReverseTorque = (actTorque < 0.0) || (actAbsSpd > rampTarget + 15.0);

                    if (isReverseTorque)
                    {
                        // 處於反轉矩異常狀態：釘住當前轉速，等待轉矩變回正數且轉速收斂
                        if (elapsedSec >= (double)reverseTorqueTimeoutSec)
                        {
                            string abortMsg = string.Format("【[!] 反轉矩超時保護停機】第 {0}/4 段加速異常：實測轉矩持續為負 ({1:F2} Nm) 或轉速被拖曳超速 ({2:F0} rpm / 目標 {3:F0} rpm) 超過 {4} 秒！",
                                curStepIdx + 1, actTorque, actAbsSpd, rampTarget, reverseTorqueTimeoutSec);
                            WriteHmiLog("DUTY_ABORT", abortMsg);
                            StopDutyTest();
                            MessageBox.Show(abortMsg, "反轉矩異常保護", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                            return;
                        }

                        lblDutyStatus.Text = string.Format("【4段梯度加速】第 {0}/4 段：反轉矩拖曳中 (轉矩 {1:F2} Nm, 轉速 {2:F0} rpm)，等待轉正 ({3:F1}/{4}s)...",
                            curStepIdx + 1, actTorque, actAbsSpd, elapsedSec, reverseTorqueTimeoutSec);
                        lblDutyPhaseAction.Text = string.Format("檢測到反轉矩 ({0:F2} Nm)，等待加載端磁場平衡轉正 ({1:F1}/{2}s)...",
                            actTorque, elapsedSec, reverseTorqueTimeoutSec);
                    }
                    else
                    {
                        // 轉矩已為正數 (actTorque >= 0) 且轉速未被超速拖曳：立刻推進至下一段加速！
                        WriteHmiLog("DUTY_EXCITE", string.Format("【4段梯度加速 {0}/4 轉正放行】實測轉矩已回正 ({1:F2} Nm, 轉速 {2:F0} rpm)，耗時 {3:F1}s，推進至下一階！",
                            curStepIdx + 1, actTorque, actAbsSpd, elapsedSec));
                        dutyRampStep++;
                        dutyExciteWaitSec = 0;
                        dutyRampReachedTime = DateTime.MinValue;

                        if (dutyRampStep >= 4)
                        {
                            // 4 段全部完成！正式提速至需求轉速，進入正常加載測試
                            dutyExciteStage = 0;
                            KebWriteParam32(spdCom, spdBaud, spdNode, 0x0034, (int)s6CurrentSpeedCmd, "DUTY 4段梯度完成提速至需求轉速 (Sy52)");
                            WriteHmiLog("DUTY_EXCITE", string.Format("【4段梯度加速全部完成】各段皆在實測轉矩轉正後即刻推進到達需求轉速 {0:F0} rpm，加載端零力矩無反轉，正式進入工作制測試！", targetSpd));
                        }
                    }
                }

                if (lblDutyMiniStatus != null) lblDutyMiniStatus.Text = lblDutyStatus.Text;
                if (lblDutyMiniPhaseAction != null) lblDutyMiniPhaseAction.Text = lblDutyPhaseAction.Text;
                return;
            }

            // 【DUTY 基準電流採樣與全域過電流保護】
            double curSig = actCurrentSigma;
            double curDrv = (spdDrive == 1) ? kebCurrent1 : kebCurrent2;

            // 1. 採樣建立基準電流 (S1 穩定運轉 60 秒 / S2/S6 穩定持載或錨點 30 秒)
            if (!isDutyBaselineEstablished)
            {
                bool isMotorRunningLoaded = false;
                int reqSampleSec = 60;

                if (dutyModeIdx == 0) // S1: 轉速到位且轉矩達標
                {
                    isMotorRunningLoaded = (actAbsSpd >= targetSpd * 0.85) && (Math.Abs(actAbsTrq - targetTrq) <= 1.5);
                    reqSampleSec = 60;
                }
                else if (dutyModeIdx == 1) // S2: 錨點穩定階段或持載運轉中
                {
                    isMotorRunningLoaded = (actAbsSpd >= targetSpd * 0.85) && (actAbsTrq >= targetTrq * 0.7);
                    reqSampleSec = 30;
                }
                else if (dutyModeIdx == 2) // S6: 加載階段且轉速轉矩穩定
                {
                    isMotorRunningLoaded = (actAbsSpd >= targetSpd * 0.85) && (actAbsTrq >= targetTrq * 0.7);
                    reqSampleSec = 30;
                }

                if (isMotorRunningLoaded)
                {
                    dutySigmaCurrentSamples.Enqueue(new KeyValuePair<DateTime, double>(now, curSig));
                    dutyDriveCurrentSamples.Enqueue(new KeyValuePair<DateTime, double>(now, curDrv));

                    while (dutySigmaCurrentSamples.Count > 0 && (now - dutySigmaCurrentSamples.Peek().Key).TotalSeconds > reqSampleSec)
                        dutySigmaCurrentSamples.Dequeue();
                    while (dutyDriveCurrentSamples.Count > 0 && (now - dutyDriveCurrentSamples.Peek().Key).TotalSeconds > reqSampleSec)
                        dutyDriveCurrentSamples.Dequeue();

                    double elapsedSampleSec = dutySigmaCurrentSamples.Count > 0 ? (now - dutySigmaCurrentSamples.Peek().Key).TotalSeconds : 0;
                    if (elapsedSampleSec >= (reqSampleSec - 1) && dutySigmaCurrentSamples.Count >= (reqSampleSec * 0.8))
                    {
                        dutyBaselineCurrentSigma = dutySigmaCurrentSamples.Average(x => x.Value);
                        dutyBaselineCurrentDrive = dutyDriveCurrentSamples.Average(x => x.Value);
                        isDutyBaselineEstablished = true;
                        WriteHmiLog("DUTY_BASELINE", string.Format("【DUTY 電流基準確立】穩定採樣 {0} 秒完成：PowerMeter Σ = {1:F2} A, 驅動器 = {2:F2} A",
                            reqSampleSec, dutyBaselineCurrentSigma, dutyBaselineCurrentDrive));
                        double pctMult = 1.0 + ((double)dutyOverCurrentPercent / 100.0);
                        double sigLimit = dutyBaselineCurrentSigma * pctMult;
                        double drvLimit = dutyBaselineCurrentDrive * pctMult;
                        if (lblDutyCurrentBaseline != null)
                            lblDutyCurrentBaseline.Text = string.Format("基準(門檻): 驅={0:F1}A ({1:F1}A) | Σ={2:F1}A ({3:F1}A)", dutyBaselineCurrentDrive, drvLimit, dutyBaselineCurrentSigma, sigLimit);
                    }
                    else
                    {
                        if (lblDutyCurrentBaseline != null)
                            lblDutyCurrentBaseline.Text = string.Format("基準: 採樣中 ({0:F0}/{1}s)...", elapsedSampleSec, reqSampleSec);
                    }
                }
            }

            // 2. 過電流判定保護 (超出基準負載比例連續達設定秒數)
            if (isDutyBaselineEstablished && (dutyBaselineCurrentSigma > 0.05 || dutyBaselineCurrentDrive > 0.05))
            {
                double pctMult = 1.0 + ((double)dutyOverCurrentPercent / 100.0);
                double sigLimit = dutyBaselineCurrentSigma * pctMult;
                double drvLimit = dutyBaselineCurrentDrive * pctMult;

                bool isSigOver = (dutyBaselineCurrentSigma > 0.05) && (curSig > sigLimit);
                bool isDrvOver = (dutyBaselineCurrentDrive > 0.05) && (curDrv > drvLimit);

                if (isSigOver || isDrvOver)
                {
                    if (dutyOverCurrentStartTime == DateTime.MinValue)
                    {
                        dutyOverCurrentStartTime = now;
                    }
                    double overSec = (now - dutyOverCurrentStartTime).TotalSeconds;
                    double reqDelay = (double)dutyOverCurrentDelaySec;

                    string tripDetail = isSigOver
                        ? string.Format("PowerMeter Σ: {0:F2}A > 門檻 {1:F2}A (基準 {2:F2}A +{3}%)", curSig, sigLimit, dutyBaselineCurrentSigma, (int)dutyOverCurrentPercent)
                        : string.Format("驅動器輸出: {0:F2}A > 門檻 {1:F2}A (基準 {2:F2}A +{3}%)", curDrv, drvLimit, dutyBaselineCurrentDrive, (int)dutyOverCurrentPercent);

                    lblDutyPhaseAction.Text = string.Format("[!]【電流過載警示】{0} (超限 {1:F0}/{2:F0}s)", tripDetail, overSec, reqDelay);
                    if (lblDutyMiniPhaseAction != null) lblDutyMiniPhaseAction.Text = lblDutyPhaseAction.Text;

                    if (overSec >= reqDelay)
                    {
                        dutyTimer.Stop();
                        if (isManualRecording) StopManualRecording(showPrompt: false);

                        string tripMsg = string.Format(
                            "【[!] DUTY 過電流跳脫保護】\n\n" +
                            "實測電流超出基準負載 {0}% 連續達 {1:F0} 秒！\n" +
                            "觸發詳情：{2}\n\n" +
                            "系統已立即啟動平滑降載停機程序保護待測設備！",
                            (int)dutyOverCurrentPercent, reqDelay, tripDetail);
                        WriteHmiLog("OVER_CURRENT_TRIP", tripMsg);

                        StartGradualAutoStop(spdDrive, trqDrive, "DUTY 過電流保護跳脫", () => {
                            ResetDutyTestToStandby();
                            lblDutyStatus.Text = "【[!] 過電流保護跳脫】已安全停機！";
                            lblDutyPhaseAction.Text = tripDetail;
                            if (lblDutyMiniStatus != null) lblDutyMiniStatus.Text = lblDutyStatus.Text;
                            if (lblDutyMiniPhaseAction != null) lblDutyMiniPhaseAction.Text = lblDutyPhaseAction.Text;
                            MessageBox.Show(tripMsg, "DUTY 過電流保護跳脫", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        });
                        return;
                    }
                }
                else
                {
                    dutyOverCurrentStartTime = DateTime.MinValue;
                }
            }

            if (dutyModeIdx == 2) // ================= S6 週期負載模式 =================
            {
                double cycleMin = (double)(numS6CycleMin != null ? numS6CycleMin.Value : 10);
                double edVal = (double)(numS6Ed != null ? numS6Ed.Value : 40);
                int totalFormalCycles = (numS6Cycles != null) ? (int)numS6Cycles.Value : 4;
                int totalCycleSec = Math.Max(2, (int)(cycleMin * 60));
                int t1Sec = Math.Max(1, (int)(totalCycleSec * (edVal / 100.0)));
                int t2Sec = totalCycleSec - t1Sec;

                // -------------------------------------------------------------
                // 【階段 0：自適應試運轉·自動設定錨點】(不計入正式週期)
                // -------------------------------------------------------------
                if (s6DutyPhase == 0)
                {
                    dutyElapsedSec++;
                    if (dutyElapsedSec <= prgDuty.Maximum) prgDuty.Value = dutyElapsedSec;
                    if (prgDutyMini != null && dutyElapsedSec <= prgDutyMini.Maximum) prgDutyMini.Value = dutyElapsedSec;
                    if (lblExtrapolationSummary != null)
                    {
                        lblExtrapolationSummary.Text = "【S6 即時動態推估】試運轉定錨中... 進入正式週期後隨即啟動即時動態推估";
                        lblExtrapolationSummary.ForeColor = Color.FromArgb(67, 56, 202);
                    }

                    // 子階段 0：空載提速至目標轉速 (嚴格追隨設定轉速 SY52，加載端 0 轉矩待命)
                    if (s6TrialStage == 0)
                    {
                        currentPhaseName = "試運轉提速";
                        KebWriteParamWithDll(trqCom, trqBaud, trqNode, 0x0F12, 0); // 加載端保持 0 負載
                        s6TrialStageElapsedSec++;

                        // 空載速度命令嚴格追隨設定轉速 (或手KEY之空載SY52)，不隨意偏離
                        double noloadCmd = (numS6AnchorNoLoadSpd != null && numS6AnchorNoLoadSpd.Value > 0) ? (double)numS6AnchorNoLoadSpd.Value : targetSpd;
                        if (s6CurrentSpeedCmd != noloadCmd)
                        {
                            s6CurrentSpeedCmd = noloadCmd;
                            KebWriteParam32(spdCom, spdBaud, spdNode, 0x0034, (int)Math.Round(s6CurrentSpeedCmd), "S6 空載追隨設定轉速 (SY52)");
                            if (spdDrive == 2 && numHmiKebSpeed2 != null) numHmiKebSpeed2.Value = (decimal)s6CurrentSpeedCmd;
                            else if (spdDrive == 1 && numHmiKebSpeed1 != null) numHmiKebSpeed1.Value = (decimal)s6CurrentSpeedCmd;
                        }

                        // 提速判定：待測端轉速必須真正達到目標速度的一定比例，且實測轉速過低時嚴禁因 15 秒超時誤判到位！
                        bool spdReady = (targetSpd > 0 && Math.Abs(actAbsSpd - targetSpd) <= Math.Max(25.0, targetSpd * 0.08)) ||
                                        (targetSpd > 0 && actAbsSpd >= targetSpd * 0.90) ||
                                        (targetSpd > 0 && s6TrialStageElapsedSec >= 15 && actAbsSpd >= targetSpd * 0.5);

                        if (!spdReady)
                        {
                            string spdWaitText = string.Format("【自適應試運轉】空載提速中：實測 {0:F0} / 目標 {1:F0} rpm ({2}s)...", actAbsSpd, targetSpd, s6TrialStageElapsedSec);
                            lblDutyStatus.Text = spdWaitText;
                            lblDutyPhaseAction.Text = "待測端加速至設定轉速中，加載端 0 轉矩待命...";
                            if (lblDutyMiniStatus != null) lblDutyMiniStatus.Text = spdWaitText;
                            if (lblDutyMiniPhaseAction != null) lblDutyMiniPhaseAction.Text = lblDutyPhaseAction.Text;
                            return;
                        }

                        // 轉速到位，進入 10 秒空載穩定倒數
                        s6TrialStage = 1;
                        s6TrialTimer = 10;
                        s6TrialStageElapsedSec = 0;
                        WriteHmiLog("S6_ANCHOR", string.Format("待測端空載轉速已到位 ({0:F1} rpm)，開始 10 秒空載穩定確認！", actAbsSpd));
                    }
                    // 子階段 1：空載 10 秒穩定確認  確定並記錄 [空載轉速錨點]
                    else if (s6TrialStage == 1)
                    {
                        currentPhaseName = "空載定錨倒數";
                        KebWriteParamWithDll(trqCom, trqBaud, trqNode, 0x0F12, 0); // 加載端保持 0
                        s6TrialStageElapsedSec++;

                        // 空載速度命令嚴格追隨設定轉速 (或手KEY之空載SY52)，不再進行人為拉速偏離本意
                        double noloadCmd = (numS6AnchorNoLoadSpd != null && numS6AnchorNoLoadSpd.Value > 0) ? (double)numS6AnchorNoLoadSpd.Value : targetSpd;
                        if (s6CurrentSpeedCmd != noloadCmd)
                        {
                            s6CurrentSpeedCmd = noloadCmd;
                            KebWriteParam32(spdCom, spdBaud, spdNode, 0x0034, (int)Math.Round(s6CurrentSpeedCmd), "S6 空載追隨設定轉速 (SY52)");
                            if (spdDrive == 2 && numHmiKebSpeed2 != null) numHmiKebSpeed2.Value = (decimal)s6CurrentSpeedCmd;
                            else if (spdDrive == 1 && numHmiKebSpeed1 != null) numHmiKebSpeed1.Value = (decimal)s6CurrentSpeedCmd;
                        }

                        // 合理評估達標帶寬 (±25 rpm 或 ±8%) 或運轉 10 秒以上
                        bool isNoLoadSpdReached = (targetSpd <= 0) || (Math.Abs(actAbsSpd - targetSpd) <= Math.Max(25.0, targetSpd * 0.08));
                        bool isStage1Timeout = (s6TrialStageElapsedSec >= 12 && actAbsSpd >= targetSpd * 0.85);

                        if (isNoLoadSpdReached || isStage1Timeout)
                        {
                            s6TrialTimer--;
                            string countdownText = string.Format("【自適應試運轉】空載轉速達標確認中 (倒數 {0}s)...", Math.Max(0, s6TrialTimer));
                            lblDutyStatus.Text = countdownText;
                            lblDutyPhaseAction.Text = string.Format("待測端 {0:F0} rpm 達標穩定中 (SY52={1:F0} rpm)，加載端 0.0 Nm", actAbsSpd, s6CurrentSpeedCmd);
                        }
                        else
                        {
                            string waitText = string.Format("【自適應試運轉】空載轉速調節中 (實測 {0:F0}/{1:F0} rpm, 倒數 {2}s)...", actAbsSpd, targetSpd, s6TrialTimer);
                            lblDutyStatus.Text = waitText;
                            lblDutyPhaseAction.Text = "待測端空載轉速穩定中...";
                        }
                        if (lblDutyMiniStatus != null) lblDutyMiniStatus.Text = lblDutyStatus.Text;
                        if (lblDutyMiniPhaseAction != null) lblDutyMiniPhaseAction.Text = lblDutyPhaseAction.Text;

                        if (s6TrialTimer <= 0 || (s6TrialStageElapsedSec >= 20 && actAbsSpd >= targetSpd * 0.85))
                        {
                            // 記錄【空載轉速錨點】
                            s6AnchorNoLoadSpeed = s6CurrentSpeedCmd;
                            s6HasNoLoadAnchor = true;
                            if (numS6AnchorNoLoadSpd != null) numS6AnchorNoLoadSpd.Value = (decimal)s6AnchorNoLoadSpeed;
                            if (numDutyMiniS6NoLoadSpd != null) numDutyMiniS6NoLoadSpd.Value = (decimal)s6AnchorNoLoadSpeed;
                            UpdateS6AnchorStatusText();
                            s6TrialStage = 2; // 進入加載與補轉差階段
                            s6TrialStageElapsedSec = 0;
                            s6AdaptedTorquePct = 0.0;

                            if (numS6AnchorLoadedSpd != null && numS6AnchorLoadedSpd.Value > 0)
                            {
                                s6CurrentSpeedCmd = (double)numS6AnchorLoadedSpd.Value;
                                KebWriteParam32(spdCom, spdBaud, spdNode, 0x0034, (int)s6CurrentSpeedCmd, "S6 套用手KEY加載SY52");
                            }

                            s6LoadTracker.Reset(s6CurrentSpeedCmd, 0.0);
                            WriteHmiLog("S6_ANCHOR", string.Format("【空載定錨完成】確立 [空載轉速錨點] = {0:F0} rpm！開始執行 3 步 0.1% 靈敏度試探加載！", s6AnchorNoLoadSpeed));
                        }
                    }
                    // 子階段 2：統一自適應加速定錨加載 (前 3 秒 0.1% 試探斜率 -> 依目標差距開出最大 5% 步長狂衝 -> 同動補轉差)
                    else if (s6TrialStage == 2)
                    {
                        currentPhaseName = "加載自適應爬坡";
                        s6TrialStageElapsedSec++;

                        string statusDesc;
                        bool converged = ExecuteUnifiedDualTrackingStep(
                            s6LoadTracker,
                            spdDrive, trqDrive,
                            targetSpd, targetTrq,
                            actAbsSpd, actAbsTrq,
                            out statusDesc,
                            "S6加載定錨"
                        );

                        s6AdaptedTorquePct = s6LoadTracker.AdaptedTorquePct;
                        s6CurrentSpeedCmd = s6LoadTracker.CurrentSpeedCmd;

                        lblDutyStatus.Text = statusDesc;
                        lblDutyPhaseAction.Text = string.Format("S6 定錨加載：命令 {0:F1}% (每0.1%約{1:F2}Nm)，待測端 {2:F0} rpm",
                            s6AdaptedTorquePct, s6LoadTracker.NmPerPointOnePct, s6CurrentSpeedCmd);

                        // 即時同步介面上的加載錨點輸入框顯示
                        if (!isSyncingDutyControls)
                        {
                            isSyncingDutyControls = true;
                            if (numS6AnchorLoadedCs18 != null)
                                numS6AnchorLoadedCs18.Value = (decimal)Math.Max(0, Math.Min(1000, Math.Round(s6AdaptedTorquePct * 10.0)));
                            if (numS6AnchorLoadedSpd != null)
                                numS6AnchorLoadedSpd.Value = (decimal)Math.Max(0, Math.Min(6000, Math.Round(s6CurrentSpeedCmd)));
                            isSyncingDutyControls = false;
                        }

                        if (lblDutyMiniStatus != null) lblDutyMiniStatus.Text = lblDutyStatus.Text;
                        if (lblDutyMiniPhaseAction != null) lblDutyMiniPhaseAction.Text = lblDutyPhaseAction.Text;

                        if (converged)
                        {
                            s6TrialStage = 3;
                            s6TrialTimer = 10;
                            s6TrialStageElapsedSec = 0;
                            WriteHmiLog("S6_ANCHOR", string.Format("加載轉矩 ({0:F1}/{1:F1} Nm) 與轉速 ({2:F0}/{3:F0} rpm) 雙達標收斂，開始 10 秒真達標穩定確認！", actAbsTrq, targetTrq, actAbsSpd, targetSpd));
                        }
                        else if (s6TrialStageElapsedSec >= 90)
                        {
                            // 超時防護：超過 90 秒仍無法達標，停止並警示，嚴禁偽裝定錨成功！
                            WriteHmiLog("S6_ERROR", string.Format("【[!] S6 加載定錨超時報警】已調節 90 秒但轉矩仍無法達到目標 ({0:F1}/{1:F1} Nm)！請檢查負載側供電、制動器或目標值設定！", actAbsTrq, targetTrq));
                            StopDutyTest();
                            MessageBox.Show(
                                string.Format("【[!] S6 加載定錨超時】\n\n系統加載調節已達 90 秒，但轉矩實測 ({0:F1} Nm) 仍未達目標 ({1:F1} Nm)！\n\n系統已安全停機保護，請確認負載端出力能力與設定值。", actAbsTrq, targetTrq),
                                "S6 定錨未達標警告",
                                MessageBoxButtons.OK,
                                MessageBoxIcon.Warning
                            );
                            return;
                        }
                    }
                    // 子階段 3：加載 10 秒穩定確認  確定並記錄 [加載轉速錨點] 與 [加載轉矩錨點] ( 嚴禁未達標強制倒數)
                    else if (s6TrialStage == 3)
                    {
                        currentPhaseName = "加載定錨倒數";
                        s6TrialStageElapsedSec++;

                        double trqErr = targetTrq - actAbsTrq;
                        double spdSlip = targetSpd - actAbsSpd;

                        //  嚴格判定轉矩與轉速是否雙雙在合格容許帶內 (轉矩 ±5% 或 ±1.5Nm, 轉速 ±5% 或 ±15rpm)
                        bool isTrqReached = Math.Abs(trqErr) <= Math.Max(1.5, targetTrq * 0.05);
                        bool isSpdReached = Math.Abs(spdSlip) <= Math.Max(15.0, targetSpd * 0.05);
                        bool isLoadedAnchorReached = isTrqReached && isSpdReached;

                        if (isLoadedAnchorReached)
                        {
                            // 微調控制維持平衡
                            if (Math.Abs(trqErr) > 0.4)
                            {
                                s6AdaptedTorquePct += (trqErr > 0 ? 0.08 : -0.08);
                                s6AdaptedTorquePct = Math.Max(0.0, Math.Min(100.0, s6AdaptedTorquePct));
                                KebWriteParamWithDll(trqCom, trqBaud, trqNode, 0x0F12, (int)Math.Round(s6AdaptedTorquePct * 10.0));
                            }
                            if (Math.Abs(spdSlip) > 2.0)
                            {
                                s6CurrentSpeedCmd += (spdSlip > 0 ? 1.0 : -1.0);
                                KebWriteParam32(spdCom, spdBaud, spdNode, 0x0034, (int)s6CurrentSpeedCmd, "S6 定錨微調速度");
                            }

                            s6TrialTimer--;
                            string dwellText = string.Format("【自適應試運轉】加載真達標確認中 (已連續穩定，倒數 {0}s)...", Math.Max(0, s6TrialTimer));
                            lblDutyStatus.Text = dwellText;
                            lblDutyPhaseAction.Text = string.Format("實測 {0:F0} rpm | 轉矩 {1:F1} Nm (在合格帶內穩定確認中)", actAbsSpd, actAbsTrq);
                        }
                        else
                        {
                            //  鐵律：一旦轉矩或轉速偏離容許帶，倒數計時器立即重置回 10 秒！絕不允許累積拼湊！
                            s6TrialTimer = 10;

                            // 偏離較大時加大步長修正
                            double stepAdj = (Math.Abs(trqErr) > 4.0) ? 0.4 : 0.1;
                            s6AdaptedTorquePct += (trqErr > 0 ? stepAdj : -stepAdj);
                            s6AdaptedTorquePct = Math.Max(0.0, Math.Min(100.0, s6AdaptedTorquePct));
                            KebWriteParamWithDll(trqCom, trqBaud, trqNode, 0x0F12, (int)Math.Round(s6AdaptedTorquePct * 10.0));

                            if (Math.Abs(spdSlip) > 2.0)
                            {
                                s6CurrentSpeedCmd += (spdSlip > 0 ? 2.0 : -2.0);
                                KebWriteParam32(spdCom, spdBaud, spdNode, 0x0034, (int)s6CurrentSpeedCmd, "S6 定錨補差修正");
                            }

                            string waitText = string.Format("【自適應試運轉】轉矩/轉速偏離 (轉矩 {0:F1}/{1:F1} Nm, 轉速 {2:F0}/{3:F0} rpm)，重置 10s 倒數並調節中...", actAbsTrq, targetTrq, actAbsSpd, targetSpd);
                            lblDutyStatus.Text = waitText;
                            lblDutyPhaseAction.Text = "轉矩未穩定達標，重置倒數並持續動態平衡修正...";
                        }

                        // 同步主畫面速度與轉矩框
                        if (spdDrive == 2 && numHmiKebSpeed2 != null) numHmiKebSpeed2.Value = (decimal)s6CurrentSpeedCmd;
                        else if (spdDrive == 1 && numHmiKebSpeed1 != null) numHmiKebSpeed1.Value = (decimal)s6CurrentSpeedCmd;
                        if (trqDrive == 1 && numHmiKebTorque1 != null) numHmiKebTorque1.Value = (decimal)s6AdaptedTorquePct;
                        else if (trqDrive == 2 && numHmiKebTorque2 != null) numHmiKebTorque2.Value = (decimal)s6AdaptedTorquePct;

                        if (lblDutyMiniStatus != null) lblDutyMiniStatus.Text = lblDutyStatus.Text;
                        if (lblDutyMiniPhaseAction != null) lblDutyMiniPhaseAction.Text = lblDutyPhaseAction.Text;

                        //  唯有 s6TrialTimer <= 0（真正連續 10 秒在目標容許帶內），才宣告定錨完成！
                        if (s6TrialTimer <= 0)
                        {
                            // 記錄【加載轉速錨點】與【加載轉矩錨點】
                            s6AnchorLoadedSpeed = s6CurrentSpeedCmd;
                            s6AnchorLoadedTorquePct = s6AdaptedTorquePct;
                            s6HasLoadedAnchor = true;

                            if (numS6AnchorLoadedSpd != null) numS6AnchorLoadedSpd.Value = (decimal)s6AnchorLoadedSpeed;
                            if (numDutyMiniS6LoadedSpd != null) numDutyMiniS6LoadedSpd.Value = (decimal)s6AnchorLoadedSpeed;
                            int cs18Val = (int)Math.Round(s6AnchorLoadedTorquePct * 10.0);
                            if (numS6AnchorLoadedCs18 != null) numS6AnchorLoadedCs18.Value = cs18Val;
                            if (numDutyMiniS6LoadedCs18 != null) numDutyMiniS6LoadedCs18.Value = cs18Val;

                            UpdateS6AnchorStatusText();
                            s6TrialStage = 4; // 進入維持試運轉 T1 有載時間
                            s6CycleElapsedSec = 0; // 重設為 0，維持完整的 S6 設定 DUTY 時間 T1
                            s6TrialStageElapsedSec = 0;
                            WriteHmiLog("S6_ANCHOR", string.Format("【加載定錨完成】已真正連續達標穩定 10 秒 (實測 {0:F1} Nm)！確立 [加載轉速錨點] = {1:F0} rpm, [加載轉矩錨點] = {2:F1}%！開始維持 S6 設定 DUTY 時間 T1 ({3}s)...", actAbsTrq, s6AnchorLoadedSpeed, s6AnchorLoadedTorquePct, t1Sec));
                        }
                        else if (s6TrialStageElapsedSec >= 60)
                        {
                            // Stage 3 超時防護：超過 60 秒都無法完成連續 10 秒穩定
                            WriteHmiLog("S6_ERROR", string.Format("【[!] S6 穩定確認超時】加載已達目標附近但波動過大，無法在 60 秒內連續穩定 10 秒 (實測 {0:F1}/{1:F1} Nm)！", actAbsTrq, targetTrq));
                            StopDutyTest();
                            MessageBox.Show(
                                string.Format("【[!] S6 穩定確認超時】\n\n加載已接近目標 ({0:F1}/{1:F1} Nm)，但系統波動過大，60 秒內無法連續穩定 10 秒！\n\n系統已安全停機保護，避免記錄不穩定之偽錨點。", actAbsTrq, targetTrq),
                                "S6 穩定度未達標警告",
                                MessageBoxButtons.OK,
                                MessageBoxIcon.Warning
                            );
                            return;
                        }
                    }
                    // 子階段 4：維持試運轉完整的 S6 設定 DUTY 時間 T1  先減速回 [空載轉速錨點]  卸載
                    else if (s6TrialStage == 4)
                    {
                        currentPhaseName = "試運轉 T1 有載";
                        s6CycleElapsedSec++;

                        // 維持微調
                        double trqErr = targetTrq - actAbsTrq;
                        if (Math.Abs(trqErr) > 0.4)
                        {
                            s6AdaptedTorquePct += (trqErr > 0 ? 0.1 : -0.1);
                            s6AdaptedTorquePct = Math.Max(0.0, Math.Min(100.0, s6AdaptedTorquePct));
                            KebWriteParamWithDll(trqCom, trqBaud, trqNode, 0x0F12, (int)Math.Round(s6AdaptedTorquePct * 10));
                        }

                        //  T1 即將結束前 2 秒：先減速回 [空載轉速錨點]！
                        if (s6CycleElapsedSec >= t1Sec - 2 && s6CycleElapsedSec < t1Sec)
                        {
                            s6CurrentSpeedCmd = s6AnchorNoLoadSpeed;
                            KebWriteParam32(spdCom, spdBaud, spdNode, 0x0034, (int)s6CurrentSpeedCmd, "S6 先降回空載轉速");
                            if (spdDrive == 2 && numHmiKebSpeed2 != null) numHmiKebSpeed2.Value = (decimal)s6CurrentSpeedCmd;
                            else if (spdDrive == 1 && numHmiKebSpeed1 != null) numHmiKebSpeed1.Value = (decimal)s6CurrentSpeedCmd;
                            WriteHmiLog("S6_STAGE", string.Format("【試運轉 T1 結束前夕】轉速先減速回 [空載轉速錨點] ({0:F0} rpm)！", s6AnchorNoLoadSpeed));
                        }
                        // T1 時間到達：加載端卸載歸零，進入 T2 空載等待 (T - ED%)
                        else if (s6CycleElapsedSec >= t1Sec)
                        {
                            s6TrialStage = 5;
                            s6CycleElapsedSec = 0;
                            s6AdaptedTorquePct = 0.0;
                            KebWriteParamWithDll(trqCom, trqBaud, trqNode, 0x0F12, 0); // 加載端強制卸載歸零
                            if (trqDrive == 1 && numHmiKebTorque1 != null) numHmiKebTorque1.Value = 0;
                            else if (trqDrive == 2 && numHmiKebTorque2 != null) numHmiKebTorque2.Value = 0;
                            WriteHmiLog("S6_STAGE", string.Format("【試運轉進入 T2】加載端已完全卸載歸零，等待空載自冷時間 (T - ED%) 共 {0} 秒！", t2Sec));
                        }

                        string remT1Text = string.Format("【自適應試運轉】 T1 有載運轉中 (剩餘 {0}s)", Math.Max(0, t1Sec - s6CycleElapsedSec));
                        lblDutyStatus.Text = remT1Text;
                        lblDutyPhaseAction.Text = string.Format("【T1 有載】實測 {0:F1} Nm / 轉速 {1:F0} rpm (輸出 {2:F1}%)", actAbsTrq, actAbsSpd, s6AdaptedTorquePct);
                        if (lblDutyMiniStatus != null) lblDutyMiniStatus.Text = remT1Text;
                        if (lblDutyMiniPhaseAction != null) lblDutyMiniPhaseAction.Text = lblDutyPhaseAction.Text;
                    }
                    // 子階段 5：試運轉 T2 空載冷卻等待時間 (T - ED%)
                    else if (s6TrialStage == 5)
                    {
                        currentPhaseName = "試運轉 T2 空載";
                        s6CycleElapsedSec++;
                        KebWriteParamWithDll(trqCom, trqBaud, trqNode, 0x0F12, 0); // 加載端確保歸零

                        string remT2Text = string.Format("【自適應試運轉】 T2 空載自冷中 (剩餘 {0}s)", Math.Max(0, t2Sec - s6CycleElapsedSec));
                        lblDutyStatus.Text = remT2Text;
                        lblDutyPhaseAction.Text = string.Format("加載端已卸載歸零 0.0 Nm，轉速 {0:F0} rpm 自冷運轉中...", actAbsSpd);
                        if (lblDutyMiniStatus != null) lblDutyMiniStatus.Text = remT2Text;
                        if (lblDutyMiniPhaseAction != null) lblDutyMiniPhaseAction.Text = lblDutyPhaseAction.Text;

                        // 試運轉週期完全結束！正式開始正式第 1 週期！
                        if (s6CycleElapsedSec >= t2Sec)
                        {
                            s6DutyPhase = 1; // 切換至正式週期循環
                            s6FormalCycleIndex = 1;
                            s6CycleElapsedSec = 0;
                            WriteHmiLog("S6_STAGE", string.Format("【自適應試運轉圓滿結束】所有定錨數據確立！第二次 DUTY 正式開始第 1/{0} 週期！", totalFormalCycles));
                        }
                    }
                }
                // -------------------------------------------------------------
                // 【階段 1：正式測試週期循環】(直接套用定錨轉速與定錨轉矩)
                // -------------------------------------------------------------
                else if (s6DutyPhase == 1)
                {
                    dutyElapsedSec++;
                    if (dutyElapsedSec <= prgDuty.Maximum) prgDuty.Value = dutyElapsedSec;
                    if (prgDutyMini != null && dutyElapsedSec <= prgDutyMini.Maximum) prgDutyMini.Value = dutyElapsedSec;

                    // 每秒記錄溫度歷程至 s6TempHistory
                    if (s6TempHistory != null)
                    {
                        s6TempHistory.Add(new KeyValuePair<DateTime, double>(now, dutyMonitoredTemp));
                        DateTime expireS6 = now.AddMinutes(-45);
                        s6TempHistory.RemoveAll(x => x.Key < expireS6);
                    }

                    // 追蹤當前週期之最高溫度點與相對最低溫
                    if (dutyMonitoredTemp > s6CurrentCyclePeakTemp)
                        s6CurrentCyclePeakTemp = dutyMonitoredTemp;
                    if (dutyMonitoredTemp < s6CurrentCycleTroughTemp)
                        s6CurrentCycleTroughTemp = dutyMonitoredTemp;

                    //  S6 兩段式超溫防護監控 (警告 / 停機)
                    double warnThresh = (numS6WarnTemp != null) ? (double)numS6WarnTemp.Value : 90.0;
                    double tripThresh = (numS6TripTemp != null) ? (double)numS6TripTemp.Value : 105.0;

                    if (dutyMonitoredTemp >= tripThresh)
                    {
                        dutyTimer.Stop();
                        if (isManualRecording)
                        {
                            StopManualRecording(showPrompt: false);
                        }
                        WriteHmiLog("S6_OVERTEMP", string.Format("【[停機] S6 嚴重超溫保護】監控溫度達 {0:F1} ℃ >= 停機門檻 {1:F1} ℃，啟動安全停機！", dutyMonitoredTemp, tripThresh));
                        StartGradualAutoStop(spdDrive, trqDrive, "S6 嚴重超溫停機保護", () => {
                            ResetDutyTestToStandby();
                            lblDutyStatus.Text = string.Format("【[停機] 嚴重超溫停機】實測 {0:F1} ℃ >= 停機門檻 {1:F1} ℃！", dutyMonitoredTemp, tripThresh);
                            lblDutyPhaseAction.Text = "超溫停機保護已生效，雙機已降載停機。";
                            MessageBox.Show(string.Format("【S6 超溫安全停機保護】\r\n\r\n監控通道實測溫度已達 {0:F1} ℃，超過設定之停機門檻 {1:F1} ℃！\r\n系統已自動執行安全降載停機保護。", dutyMonitoredTemp, tripThresh), "S6 超溫安全停機", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        });
                        return;
                    }
                    else if (dutyMonitoredTemp >= warnThresh)
                    {
                        if (lblDutyTempRealtimeVal != null) lblDutyTempRealtimeVal.ForeColor = Color.Red;
                    }
                    else
                    {
                        if (lblDutyTempRealtimeVal != null) lblDutyTempRealtimeVal.ForeColor = Color.FromArgb(30, 64, 175);
                    }

                    // 【T1 有載運轉階段】
                    if (s6CycleElapsedSec < t1Sec)
                    {
                        currentPhaseName = "T1 有載";
                        // 第 0 秒：轉速直接加到 [加載轉速錨點]，轉矩直接加載到 [加載轉矩錨點]！
                        if (s6CycleElapsedSec == 0)
                        {
                            s6CurrentSpeedCmd = s6AnchorLoadedSpeed;
                            KebWriteParam32(spdCom, spdBaud, spdNode, 0x0034, (int)s6CurrentSpeedCmd, "S6 直接套用加載轉速錨點");

                            s6AdaptedTorquePct = s6AnchorLoadedTorquePct;
                            KebWriteParamWithDll(trqCom, trqBaud, trqNode, 0x0F12, (int)Math.Round(s6AdaptedTorquePct * 10));

                            if (spdDrive == 2 && numHmiKebSpeed2 != null) numHmiKebSpeed2.Value = (decimal)s6CurrentSpeedCmd;
                            else if (spdDrive == 1 && numHmiKebSpeed1 != null) numHmiKebSpeed1.Value = (decimal)s6CurrentSpeedCmd;
                            if (trqDrive == 1 && numHmiKebTorque1 != null) numHmiKebTorque1.Value = (decimal)s6AdaptedTorquePct;
                            else if (trqDrive == 2 && numHmiKebTorque2 != null) numHmiKebTorque2.Value = (decimal)s6AdaptedTorquePct;

                            WriteHmiLog("S6_STAGE", string.Format("[第 {0}/{1} 週期] T1 啟動：轉速直上加載錨點 {2:F0} rpm，加載端直上錨點 {3:F1}%！",
                                s6FormalCycleIndex, totalFormalCycles, s6AnchorLoadedSpeed, s6AnchorLoadedTorquePct));
                        }
                        else
                        {
                            // 1. 運轉期間微調閉迴路維持目標轉矩 (同動 LOCK 死區設定)
                            double trqDeadband = (trackingDeadband > 0) ? (double)trackingDeadband : 0.4;
                            double trqErr = targetTrq - actAbsTrq;
                            if (Math.Abs(trqErr) > trqDeadband)
                            {
                                double maxTrqStep = (trackingMaxDelta > 0) ? (double)trackingMaxDelta : 0.3;
                                double step = Math.Sign(trqErr) * Math.Min(Math.Max(0.05, Math.Abs(trqErr) * 0.1), maxTrqStep);
                                s6AdaptedTorquePct = Math.Max(0.0, Math.Min(100.0, s6AdaptedTorquePct + step));
                                KebWriteParamWithDll(trqCom, trqBaud, trqNode, 0x0F12, (int)Math.Round(s6AdaptedTorquePct * 10));
                                if (trqDrive == 1 && numHmiKebTorque1 != null) numHmiKebTorque1.Value = (decimal)s6AdaptedTorquePct;
                                else if (trqDrive == 2 && numHmiKebTorque2 != null) numHmiKebTorque2.Value = (decimal)s6AdaptedTorquePct;
                            }

                            // 2. 【待測端轉速平滑閉迴路追隨 (同動 LOCK 設定)】(T1 結束前2秒降速階段不追隨)
                            if (s6CycleElapsedSec < t1Sec - 2 && actAbsSpd >= targetSpd * 0.5 && targetSpd > 50.0)
                            {
                                double spdDeadband = (trackingSpeedDeadband > 0) ? (double)trackingSpeedDeadband : 3.0;
                                double spdErr = targetSpd - actAbsSpd;
                                if (Math.Abs(spdErr) > spdDeadband)
                                {
                                    double maxSpdStep = (trackingSpeedMaxDelta > 0) ? (double)trackingSpeedMaxDelta : 2.0;
                                    double step = Math.Sign(spdErr) * Math.Min(Math.Max(1.0, Math.Abs(spdErr) * 0.5), maxSpdStep * 2.0);
                                    double newSpdCmd = Math.Max(0.0, Math.Min(6000.0, s6CurrentSpeedCmd + step));
                                    if (newSpdCmd != s6CurrentSpeedCmd)
                                    {
                                        s6CurrentSpeedCmd = newSpdCmd;
                                        KebWriteParam32(spdCom, spdBaud, spdNode, 0x0034, (int)Math.Round(s6CurrentSpeedCmd), "S6 速度閉迴路補轉差 (SY52)");
                                        if (spdDrive == 2 && numHmiKebSpeed2 != null) numHmiKebSpeed2.Value = (decimal)s6CurrentSpeedCmd;
                                        else if (spdDrive == 1 && numHmiKebSpeed1 != null) numHmiKebSpeed1.Value = (decimal)s6CurrentSpeedCmd;
                                    }
                                }
                            }
                        }

                        //  T1 結束前 2 秒：先減速回 [空載轉速錨點]！
                        if (s6CycleElapsedSec >= t1Sec - 2 && s6CycleElapsedSec < t1Sec)
                        {
                            s6CurrentSpeedCmd = s6AnchorNoLoadSpeed;
                            KebWriteParam32(spdCom, spdBaud, spdNode, 0x0034, (int)s6CurrentSpeedCmd, "S6 先降回空載轉速");
                            if (spdDrive == 2 && numHmiKebSpeed2 != null) numHmiKebSpeed2.Value = (decimal)s6CurrentSpeedCmd;
                            else if (spdDrive == 1 && numHmiKebSpeed1 != null) numHmiKebSpeed1.Value = (decimal)s6CurrentSpeedCmd;
                        }

                        // ── S6 溫升斜率與預估幾次平衡即時演算 ──
                        double s6CycleMinVal = (numS6CycleMin != null) ? (double)numS6CycleMin.Value : 10.0;
                        double s6EffectiveSlope = 0.0;
                        if (s6PeakTempHistory.Count >= 2)
                        {
                            int nPeaks = s6PeakTempHistory.Count;
                            double lastPeakDiff = s6PeakTempHistory[nPeaks - 1] - s6PeakTempHistory[nPeaks - 2];
                            s6EffectiveSlope = Math.Max(0.0, lastPeakDiff / s6CycleMinVal);
                        }
                        else
                        {
                            s6EffectiveSlope = Math.Max(0.0, CalculateThermalSlopePerMin(s6TempHistory, 120));
                        }

                        int remCycles = 1;
                        int estBalanceCycle = s6FormalCycleIndex + 1;
                        if (s6EffectiveSlope > 0.0333)
                        {
                            double tauS6 = 30.0; // 分鐘
                            double remMin = tauS6 * Math.Log(s6EffectiveSlope / 0.03333);
                            remCycles = Math.Max(1, (int)Math.Ceiling(remMin / s6CycleMinVal));
                            estBalanceCycle = s6FormalCycleIndex + remCycles;
                        }

                        double curPeak = (s6CurrentCyclePeakTemp > -100) ? s6CurrentCyclePeakTemp : dutyMonitoredTemp;
                        string troughDisp = (s6LastCycleTroughTemp > 0) ? string.Format("{0:F1}℃", s6LastCycleTroughTemp) : string.Format("{0:F1}℃", dutyMonitoredTemp);
                        UpdateDynamicExtrapolationS6(targetTrq, cycleMin, edVal, curPeak, s6FormalCycleIndex, totalFormalCycles);

                        if (lblS6ThermalStatus != null)
                        {
                            if (s6IsVerifyingConfirmationCycle)
                            {
                                lblS6ThermalStatus.Text = string.Format("[覆核] [第 {0} 週期] 追加確認覆核中！最高: {1:F1}℃ | 冷卻最後: {2} (需溫差 <= 1.0℃)",
                                    s6FormalCycleIndex, curPeak, troughDisp);
                                lblS6ThermalStatus.ForeColor = Color.DarkCyan;
                            }
                            else
                            {
                                lblS6ThermalStatus.Text = string.Format("S6: 週期最高 {0:F1}℃ | 冷卻最後 {1} | 預估約 {2} 週期後平衡 (第 {3} 週期)",
                                    curPeak, troughDisp, remCycles, estBalanceCycle);
                                lblS6ThermalStatus.ForeColor = (remCycles <= 1) ? Color.Green : Color.DarkOrange;
                            }
                        }

                        if (lblThermalStatus != null)
                        {
                            if (s6IsVerifyingConfirmationCycle)
                            {
                                lblThermalStatus.Text = string.Format("S6: 追加第 {0} 週期進行熱平衡最終確認覆核 (需溫差 <= 1.0℃)", s6FormalCycleIndex);
                                lblThermalStatus.ForeColor = Color.DarkCyan;
                            }
                            else
                            {
                                lblThermalStatus.Text = string.Format("S6 溫升監控: 週期最高 {0:F1}℃, 冷卻最後 {1} | 預估第 {2} 週期平衡 (約 {3} 週期後)",
                                    curPeak, troughDisp, estBalanceCycle, remCycles);
                                lblThermalStatus.ForeColor = (remCycles <= 1) ? Color.Green : Color.FromArgb(30, 64, 175);
                            }
                        }

                        string formalT1Text = string.Format("[第 {0}/{1} 週期]  T1 有載 (剩餘 {2}s) | 峰值: {3:F1}℃", s6FormalCycleIndex, totalFormalCycles, t1Sec - s6CycleElapsedSec, curPeak);
                        lblDutyStatus.Text = formalT1Text;
                        lblDutyPhaseAction.Text = string.Format("【T1 有載】實測 {0:F1} Nm / 目標 {1:F1} Nm (給定 {2:F1}%) | 最高: {3:F1}℃", actAbsTrq, targetTrq, s6AdaptedTorquePct, curPeak);
                        if (lblDutyMiniStatus != null) lblDutyMiniStatus.Text = formalT1Text;
                        if (lblDutyMiniPhaseAction != null) lblDutyMiniPhaseAction.Text = lblDutyPhaseAction.Text;

                        s6CycleElapsedSec++;
                    }
                    // 【T2 空載自冷階段】(T - ED%)
                    else if (s6CycleElapsedSec >= t1Sec && s6CycleElapsedSec < totalCycleSec)
                    {
                        currentPhaseName = "T2 空載";
                        // 剛進入 T2：加載端完全卸載歸零
                        if (s6CycleElapsedSec == t1Sec)
                        {
                            s6AdaptedTorquePct = 0.0;
                            KebWriteParamWithDll(trqCom, trqBaud, trqNode, 0x0F12, 0);
                            if (trqDrive == 1 && numHmiKebTorque1 != null) numHmiKebTorque1.Value = 0;
                            else if (trqDrive == 2 && numHmiKebTorque2 != null) numHmiKebTorque2.Value = 0;
                            WriteHmiLog("S6_STAGE", string.Format("[第 {0}/{1} 週期] T1 完成(最高溫 {2:F1}℃)，加載端卸載歸零，進入 T2 空載自冷！", s6FormalCycleIndex, totalFormalCycles, s6CurrentCyclePeakTemp));
                        }

                        double curPeakT2 = (s6CurrentCyclePeakTemp > -100) ? s6CurrentCyclePeakTemp : dutyMonitoredTemp;
                        string formalT2Text = string.Format("[第 {0}/{1} 週期]  T2 空載自冷 (剩餘 {2}s) | 目前低溫: {3:F1}℃", s6FormalCycleIndex, totalFormalCycles, totalCycleSec - s6CycleElapsedSec, dutyMonitoredTemp);
                        UpdateDynamicExtrapolationS6(targetTrq, cycleMin, edVal, curPeakT2, s6FormalCycleIndex, totalFormalCycles);
                        lblDutyStatus.Text = formalT2Text;
                        lblDutyPhaseAction.Text = string.Format("【T2 空載】加載端歸零 0.0 Nm，轉速 {0:F0} rpm 自冷中... (即時冷溫: {1:F1}℃)", actAbsSpd, dutyMonitoredTemp);
                        if (lblDutyMiniStatus != null) lblDutyMiniStatus.Text = formalT2Text;
                        if (lblDutyMiniPhaseAction != null) lblDutyMiniPhaseAction.Text = lblDutyPhaseAction.Text;

                        s6CycleElapsedSec++;
                    }
                    // 【單一正式週期結束交替】
                    else if (s6CycleElapsedSec >= totalCycleSec)
                    {
                        // 【S6 熱平衡分析：結算當前週期最高溫點與冷卻最後低溫】
                        double finalCoolingTrough = dutyMonitoredTemp;
                        s6TroughTempHistory.Add(finalCoolingTrough);
                        s6LastCycleTroughTemp = finalCoolingTrough;

                        if (s6CurrentCyclePeakTemp > -100.0)
                        {
                            s6PeakTempHistory.Add(s6CurrentCyclePeakTemp);
                            s6LastCyclePeakTemp = s6CurrentCyclePeakTemp;
                            WriteHmiLog("S6_CYCLE_TEMP", string.Format("【S6 週期結算】第 {0} 週期最高溫: {1:F2} ℃ (T1), 冷卻最後低溫: {2:F2} ℃ (T2), 週期溫差幅: {3:F2} ℃ (已累積 {4} 週期)",
                                s6FormalCycleIndex, s6CurrentCyclePeakTemp, finalCoolingTrough, s6CurrentCyclePeakTemp - finalCoolingTrough, s6PeakTempHistory.Count));
                        }

                        //  判定穩定後多做一個週期再次確認機制
                        if (s6IsVerifyingConfirmationCycle)
                        {
                            int n = s6PeakTempHistory.Count;
                            double confirmPeak = s6PeakTempHistory[n - 1];
                            double prevPeak = s6PeakTempHistory[n - 2];
                            double confirmDiff = Math.Abs(confirmPeak - prevPeak);

                            if (confirmDiff <= 1.0)
                            {
                                //  再次確認成功！正式宣告熱平衡達成並停機 
                                s6ThermalBalanced = true;
                                s6IsVerifyingConfirmationCycle = false;

                                if (lblS6ThermalStatus != null)
                                {
                                    lblS6ThermalStatus.Text = string.Format(" S6 熱平衡確認通過 (追加週期溫差 {0:F2}℃ <= 1.0℃)！", confirmDiff);
                                    lblS6ThermalStatus.ForeColor = Color.Green;
                                }
                                WriteHmiLog("S6_THERMAL_BALANCED", string.Format("【S6 熱平衡追加確認成功】第 {0} 追加確認週期最高溫 {1:F2} ℃ (前週期 {2:F2} ℃，溫差 {3:F2} ℃ <= 1.0℃，冷卻最後低溫 {4:F2} ℃)，正式達標！",
                                    s6FormalCycleIndex, confirmPeak, prevPeak, confirmDiff, finalCoolingTrough));

                                dutyTimer.Stop();
                                if (isManualRecording)
                                {
                                    StopManualRecording(showPrompt: false);
                                }
                                StartGradualAutoStop(spdDrive, trqDrive, "S6熱平衡達標(含追加確認週期溫差<=1.0℃)", () => {
                                    ResetDutyTestToStandby();

                                    RecordAndExtrapolateS6(confirmPeak);
                                    string s6ReportPath = SaveDutyTestReportCsv("S6", showDialog: false);

                                    lblDutyStatus.Text = string.Format("[成功] S6 熱平衡達標且通過追加確認 (第{0}週期，溫差 {1:F2}℃<=1.0℃)！", s6FormalCycleIndex, confirmDiff);
                                    lblDutyPhaseAction.Text = "S6 熱平衡已確認達標，已自動平緩卸載並停機，報表已自動儲存。";
                                    if (lblDutyMiniStatus != null) lblDutyMiniStatus.Text = lblDutyStatus.Text;
                                    if (lblDutyMiniPhaseAction != null) lblDutyMiniPhaseAction.Text = lblDutyPhaseAction.Text;

                                    if (isBatchDutyActive)
                                    {
                                        AdvanceBatchAfterS6();
                                    }
                                    else
                                    {
                                        MessageBox.Show(string.Format("S6 週期工作制熱平衡已圓滿達標！\n\n已運轉週期: 第 {0} 週期 (含追加確認週期)\n確認週期最高溫: {1:F2}℃ (前一週期: {2:F2}℃, 溫差: {3:F2}℃ <= 1.0℃)\n冷卻最後低溫: {4:F2}℃\n\n系統已自動安全平滑卸載並停機，報表已自動儲存至:\n{5}",
                                            s6FormalCycleIndex, confirmPeak, prevPeak, confirmDiff, finalCoolingTrough, s6ReportPath ?? "預設目錄"), "S6 熱平衡達標·試驗完成", MessageBoxButtons.OK, MessageBoxIcon.Information);
                                    }
                                });
                                return;
                            }
                            else
                            {
                                //  再次確認未通過！繼續測試！
                                s6IsVerifyingConfirmationCycle = false;
                                if (s6FormalCycleIndex >= totalFormalCycles)
                                {
                                    totalFormalCycles = s6FormalCycleIndex + 3;
                                }

                                WriteHmiLog("S6_CONFIRM_FAIL", string.Format("【[!] S6 追加確認未通過】第 {0} 確認週期溫差 ({1:F2}℃ > 1.0℃)，取消確認，依規定自動繼續進行週期試驗！", s6FormalCycleIndex, confirmDiff));
                                if (lblS6ThermalStatus != null)
                                {
                                    lblS6ThermalStatus.Text = string.Format("[!] 追加確認未過 (溫差 {0:F1}℃ > 1.0℃)，繼續測試累積週期...", confirmDiff);
                                    lblS6ThermalStatus.ForeColor = Color.DarkOrange;
                                }
                            }
                        }
                        else if (!s6ThermalBalanced)
                        {
                            // 檢查 30 分鐘穩定判定 (例如 10 分鐘週期需要累積 3 週期)
                            double cycleMinVal = (numS6CycleMin != null) ? (double)numS6CycleMin.Value : 10.0;
                            int reqCycles30m = Math.Max(3, (int)Math.Ceiling(30.0 / cycleMinVal));
                            if (s6PeakTempHistory.Count >= reqCycles30m)
                            {
                                int n = s6PeakTempHistory.Count;
                                bool stable30m = true;
                                for (int i = n - reqCycles30m; i < n - 1; i++)
                                {
                                    if (Math.Abs(s6PeakTempHistory[i + 1] - s6PeakTempHistory[i]) > 1.0)
                                    {
                                        stable30m = false;
                                        break;
                                    }
                                }

                                if (stable30m)
                                {
                                    //  依要求：判定穩定後再多做一個週期來再次確認！
                                    s6IsVerifyingConfirmationCycle = true;
                                    s6ConfirmationCycleIndex = s6FormalCycleIndex + 1;
                                    if (totalFormalCycles < s6ConfirmationCycleIndex)
                                    {
                                        totalFormalCycles = s6ConfirmationCycleIndex;
                                    }

                                    WriteHmiLog("S6_CONFIRM_INIT", string.Format("【S6 初步達到 30 分鐘穩定】近 {0} 週期峰值溫差 <= 1.0℃！依規範多執行第 {1} 週期進行最終確認覆核...",
                                        reqCycles30m, s6ConfirmationCycleIndex));

                                    if (lblS6ThermalStatus != null)
                                    {
                                        lblS6ThermalStatus.Text = string.Format("[覆核] 30分已初步穩定！追加第 {0} 週期進行覆核確認 (溫差需 <= 1.0℃)", s6ConfirmationCycleIndex);
                                        lblS6ThermalStatus.ForeColor = Color.DarkCyan;
                                    }
                                }
                                else
                                {
                                    if (lblS6ThermalStatus != null)
                                    {
                                        double d1 = Math.Abs(s6PeakTempHistory[n - 1] - s6PeakTempHistory[n - 2]);
                                        lblS6ThermalStatus.Text = string.Format("S6 熱平衡: 比對中 (近週期差: {0:F1}℃ > 1.0℃)", d1);
                                        lblS6ThermalStatus.ForeColor = Color.DarkOrange;
                                    }
                                }
                            }
                            else
                            {
                                if (lblS6ThermalStatus != null)
                                {
                                    lblS6ThermalStatus.Text = string.Format("S6 熱平衡: 採樣累積中 ({0}/{1} 週期，達 30 分鐘穩定將啟動追加覆核)", s6PeakTempHistory.Count, reqCycles30m);
                                    lblS6ThermalStatus.ForeColor = Color.DarkOrange;
                                }
                            }
                        }

                        s6CurrentCyclePeakTemp = -999.0;
                        s6CurrentCycleTroughTemp = 999.0;

                        s6FormalCycleIndex++;
                        if (s6FormalCycleIndex > totalFormalCycles && !s6IsVerifyingConfirmationCycle) // 全部週期圓滿完成！
                        {
                            dutyTimer.Stop();
                            if (isManualRecording)
                            {
                                StopManualRecording(showPrompt: false);
                            }
                            StartGradualAutoStop(spdDrive, trqDrive, "S6週期試驗全部完成", () => {
                                ResetDutyTestToStandby();

                                RecordAndExtrapolateS6(s6CurrentCyclePeakTemp);
                                string s6ReportPath = SaveDutyTestReportCsv("S6", showDialog: false);

                                lblDutyStatus.Text = "[成功] S6 週期負載試驗全部完成！";
                                lblDutyPhaseAction.Text = "全週期試驗完畢，已平緩降載降速停機，報表已自動儲存。";
                                if (lblDutyMiniStatus != null) lblDutyMiniStatus.Text = lblDutyStatus.Text;
                                if (lblDutyMiniPhaseAction != null) lblDutyMiniPhaseAction.Text = lblDutyPhaseAction.Text;

                                WriteHmiLog("DUTY_COMPLETE", string.Format("【S6 測試圓滿完成】共完成 {0} 個完整週期！", totalFormalCycles));

                                if (isBatchDutyActive)
                                {
                                    AdvanceBatchAfterS6();
                                }
                                else
                                {
                                    MessageBox.Show(string.Format("S6 週期負載試驗（共 {0} 週期）已順利完成！\n已先降負載再降速平滑停機，報表已自動儲存至:\n{1}", totalFormalCycles, s6ReportPath ?? "預設目錄"), "試驗完成", MessageBoxButtons.OK, MessageBoxIcon.Information);
                                }
                            });
                            return;
                        }
                        else
                        {
                            // 步進至下一個正式週期
                            s6CycleElapsedSec = 0;
                            WriteHmiLog("S6_STAGE", string.Format("【S6 週期交替】進入第 {0}/{1} 週期！", s6FormalCycleIndex, totalFormalCycles));
                        }
                    }
                }

                if (pnlS6Diagram != null) pnlS6Diagram.Invalidate();
                if (pnlS6MiniDiagram != null) pnlS6MiniDiagram.Invalidate();
            }
            else if (dutyModeIdx == 0) // ================= S1 連續工作制 (熱平衡 30min溫差<1℃ 判定) =================
            {
                dutyElapsedSec++;
                if (lblExtrapolationSummary != null && (dutyElapsedSec % 5 == 0))
                {
                    lblExtrapolationSummary.Text = "極限推估: S1 連續工作制運轉中 (S2/S6 模式可進行極限能力動態回推)";
                    lblExtrapolationSummary.ForeColor = Color.FromArgb(100, 116, 139);
                }
                int elapsedMins = dutyElapsedSec / 60;
                int elapsedSecs = dutyElapsedSec % 60;
                if (prgDuty != null && dutyElapsedSec <= prgDuty.Maximum) prgDuty.Value = dutyElapsedSec;
                if (prgDutyMini != null && dutyElapsedSec <= prgDutyMini.Maximum) prgDutyMini.Value = dutyElapsedSec;

                currentPhaseName = "S1 連續";

                // 每秒記錄溫度快照至歷史佇列 (保留 45 分鐘供 30 分鐘溫差滑動視窗比對)
                if (s1TempHistory != null)
                {
                    s1TempHistory.Add(new KeyValuePair<DateTime, double[]>(now, chSnapshot));
                    DateTime expireTime = now.AddMinutes(-45);
                    s1TempHistory.RemoveAll(x => x.Key < expireTime);
                }

                // 【S1 補轉差 / 不補轉差選項判定】：
                bool enableSlipComp = (chkDutySlipComp == null || chkDutySlipComp.Checked);

                // 若使用者取消勾選「不補轉差 (自然轉差)」，才執行 40 筆取中間 30 筆平均採樣機制；
                // 若勾選「自動補轉差 (維持額定轉速)」，則全程維持閉迴路追隨，絕不跳出不補轉差字樣！
                if (!enableSlipComp && !s1NoSlipCompleted)
                {
                    bool isTrqNearTarget = Math.Abs(actAbsTrq - targetTrq) <= Math.Max(1.0, targetTrq * 0.06);
                    if (!isS1RecordingNoSlip)
                    {
                        if (isTrqNearTarget && actAbsSpd >= targetSpd * 0.7)
                        {
                            isS1RecordingNoSlip = true;
                            if (s1NoSlipBuffer == null) s1NoSlipBuffer = new List<S1NoSlipSample>();
                            s1NoSlipBuffer.Clear();
                            s1NoSlipSampleCount = 0;
                            WriteHmiLog("S1_NOSLIP", string.Format("【S1 扭矩穩定】開始執行「不補轉差」40筆連續採樣 (固定SY52={0:F0} rpm，加載={1:F1} Nm)...", targetSpd, targetTrq));
                        }
                    }

                    if (isS1RecordingNoSlip)
                    {
                        // 1. 固定待測端 SY52 為 targetSpd，嚴禁補轉差
                        s1LoadTracker.CurrentSpeedCmd = targetSpd;
                        s6CurrentSpeedCmd = targetSpd;
                        KebWriteParam32(spdCom, spdBaud, spdNode, 0x0034, (int)Math.Round(targetSpd), "S1不補轉差固定SY52");

                        // 2. 加載端維持微調追隨目標轉矩
                        double trqErr = targetTrq - actAbsTrq;
                        if (Math.Abs(trqErr) > 0.3)
                        {
                            s1LoadTracker.AdaptedTorquePct += (trqErr > 0 ? 0.08 : -0.08);
                            s1LoadTracker.AdaptedTorquePct = Math.Max(0.0, Math.Min(100.0, s1LoadTracker.AdaptedTorquePct));
                            KebWriteParamWithDll(trqCom, trqBaud, trqNode, 0x0F12, (int)Math.Round(s1LoadTracker.AdaptedTorquePct * 10.0));
                        }
                        s6AdaptedTorquePct = s1LoadTracker.AdaptedTorquePct;

                        // 3. 採集 1 筆完整電氣與機械量
                        double vSig = (actVoltageSigma > 10.0) ? actVoltageSigma : ((wtU1 + wtU2 + wtU3) / 3.0);
                        double iSig = (actCurrentSigma > 0.05) ? actCurrentSigma : ((wtI1 + wtI2 + wtI3) / 3.0);
                        double pKw = (actElecPower > 0.01) ? actElecPower : ((wtP1 + wtP2 + wtP3) / 1000.0);
                        double pf = (wtPFSig > 0.0) ? wtPFSig : 0.85;
                        double fHz = (actFrequency > 1.0) ? actFrequency : (wtFreqU > 1.0 ? wtFreqU : 50.0);

                        s1NoSlipBuffer.Add(new S1NoSlipSample
                        {
                            Time = now,
                            Speed = actAbsSpd,
                            Torque = actAbsTrq,
                            Voltage = vSig,
                            Current = iSig,
                            PowerKw = pKw,
                            PowerFactor = pf,
                            Frequency = fHz
                        });
                        s1NoSlipSampleCount = s1NoSlipBuffer.Count;

                        lblDutyStatus.Text = string.Format("【S1 不補轉差採樣中】已採集 {0}/40 筆 (實測: {1:F0} rpm, {2:F1} Nm)", s1NoSlipSampleCount, actAbsSpd, actAbsTrq);
                        lblDutyPhaseAction.Text = string.Format("【不補轉差】固定 SY52={0:F0} rpm，加載 {1:F1}%，自然轉差運轉中", targetSpd, s6AdaptedTorquePct);
                        if (lblDutyMiniStatus != null) lblDutyMiniStatus.Text = lblDutyStatus.Text;
                        if (lblDutyMiniPhaseAction != null) lblDutyMiniPhaseAction.Text = lblDutyPhaseAction.Text;

                        if (s1NoSlipBuffer.Count >= 40)
                        {
                            SaveS1NoSlipResultAndCsv(targetSpd, targetTrq);
                            isS1RecordingNoSlip = false;
                            s1NoSlipCompleted = true;
                        }
                    }
                }

                if (!isS1RecordingNoSlip)
                {
                    // 【全系統統一核心引擎：SY52 + CS18 雙閉環自適應加速定錨追隨】
                    string s1TrackStatus;
                    bool s1Converged = ExecuteUnifiedDualTrackingStep(
                        s1LoadTracker, spdDrive, trqDrive,
                        targetSpd, targetTrq, actAbsSpd, actAbsTrq,
                        out s1TrackStatus, "S1", enableSlipComp);

                    s6AdaptedTorquePct = s1LoadTracker.AdaptedTorquePct;
                    s6CurrentSpeedCmd = s1LoadTracker.CurrentSpeedCmd;

                    elapsedMins = dutyElapsedSec / 60;
                    elapsedSecs = dutyElapsedSec % 60;
                    string slipTag = enableSlipComp ? "" : " [自然轉差]";
                    lblDutyStatus.Text = string.Format("【S1 連續】已運轉: {0:D2}:{1:D2}{2} | 目標轉矩: {3:F1} Nm, 實測: {4:F1} Nm", elapsedMins, elapsedSecs, slipTag, targetTrq, actAbsTrq);
                    lblDutyPhaseAction.Text = string.Format("【恆定加載】加載輸出 {0:F1}%, 實測轉速 {1:F0} rpm ({2})", s6AdaptedTorquePct, actAbsSpd, enableSlipComp ? "自動補轉差維持額定轉速" : "固定頻率自然轉差");
                }

                if (lblDutyAbStatus != null)
                {
                    string spdName = (spdDrive == 1) ? "A" : "B";
                    string trqName = (trqDrive == 1) ? "A" : "B";
                    lblDutyAbStatus.Text = string.Format("【載台現況】待測({0}): {1:F0} rpm (給定 {2:F0}) | 加載({3}): {4:F1} Nm (給定 {5:F1}%)",
                        spdName, actAbsSpd, targetSpd, trqName, actAbsTrq, s6AdaptedTorquePct);
                }

                // 30min 溫差小於 1℃ 熱平衡判定 (若啟用)
                if (chkS1ThermalStop != null && chkS1ThermalStop.Checked)
                {
                    bool hasAnyChecked = false;
                    if (s1MonitoredChannels != null)
                    {
                        for (int i = 0; i < s1MonitoredChannels.Length; i++) { if (s1MonitoredChannels[i]) { hasAnyChecked = true; break; } }
                    }
                    if (!hasAnyChecked)
                    {
                        if (s1MonitoredChannels == null || s1MonitoredChannels.Length < 20) s1MonitoredChannels = new bool[20];
                        s1MonitoredChannels[0] = true;
                    }

                    DateTime thirtyMinAgo = now.AddMinutes(-30);

                    // 尋找 30 分鐘前（容許誤差 ±45 秒）的樣本
                    KeyValuePair<DateTime, double[]> baselineSample = default(KeyValuePair<DateTime, double[]>);
                    if (s1TempHistory != null && s1TempHistory.Count > 0)
                    {
                        var candidates = s1TempHistory.Where(x => Math.Abs((x.Key - thirtyMinAgo).TotalSeconds) <= 45).ToList();
                        if (candidates.Count > 0)
                        {
                            baselineSample = candidates.OrderBy(x => Math.Abs((x.Key - thirtyMinAgo).TotalSeconds)).First();
                        }
                    }

                    // ── S1 即時溫升斜率與預估完成時間演算 ──
                    // ── S1 即時溫升斜率與預估完成時間演算 ──
                    double s1Slope = CalculateS1ThermalSlopePerMin(s1TempHistory, s1MonitoredChannels, 120);
                    string s1EstText = "";
                    if (dutyElapsedSec < 60)
                    {
                        s1EstText = "預估達標: 採樣建立中...";
                    }
                    else if (s1Slope <= 0.0333)
                    {
                        if (dutyElapsedSec >= 1800)
                        {
                            s1EstText = "已達熱平衡 (30m溫差<1℃)";
                        }
                        else
                        {
                            int remTo30m = (1800 - dutyElapsedSec) / 60;
                            s1EstText = string.Format("斜率已達標，等待滿30分確認 (剩餘 {0} 分鐘)", Math.Max(1, remTo30m));
                        }
                    }
                    else
                    {
                        // 依一階熱模型 S(t) = S0 * exp(-t/tau)，熱平衡點 S_eq = 0.0333 ℃/min
                        double tau = 30.0; // 分鐘
                        double remMin = Math.Max(1.0, Math.Min(300.0, tau * Math.Log(s1Slope / 0.0333)));
                        DateTime estComplete = DateTime.Now.AddMinutes(remMin);
                        s1EstText = string.Format("預估達標: 約 {0:F0} 分鐘後 ({1:HH:mm})", remMin, estComplete);
                    }

                    // 計算最高溫通道從測試開始到現在的實測累計溫升 (取消無意義的 30m 虛擬推算值)
                    double s1InitialMaxTemp = (s1TempHistory.Count > 0 && s1TempHistory[0].Value != null && maxChIdx >= 0 && maxChIdx < 20) ? s1TempHistory[0].Value[maxChIdx] : dutyMonitoredTemp;
                    double s1CumulativeRise = Math.Max(0.0, dutyMonitoredTemp - s1InitialMaxTemp);

                    if (dutyElapsedSec < 1800 || baselineSample.Value == null)
                    {
                        string warmUpMsg = string.Format("S1 熱平衡: 採樣累積中 ({0:D2}:{1:D2}/30:00) | 溫升斜率: {2:+0.00;-0.00}℃/分 | 累計溫升: {3:+0.1;-0.1}℃ | {4}",
                            elapsedMins, elapsedSecs, s1Slope, s1CumulativeRise, s1EstText);
                        lblThermalStatus.Text = warmUpMsg;
                        lblThermalStatus.ForeColor = Color.DarkOrange;
                        if (lblS1ThermalStatus != null)
                        {
                            lblS1ThermalStatus.Text = string.Format("溫升斜率: {0:+0.00;-0.00}℃/分 | 累計溫升: {1:+0.1;-0.1}℃ | {2}", s1Slope, s1CumulativeRise, s1EstText);
                            lblS1ThermalStatus.ForeColor = (s1Slope <= 0.0333) ? Color.SeaGreen : Color.DarkOrange;
                        }
                    }
                    else
                    {
                        // 僅鎖定實測最高溫通道 (maxChIdx, dutyMonitoredTemp) 進行 30 分鐘熱平衡判定 (去除其他非最高溫次要通道干擾)
                        double tNowMax = dutyMonitoredTemp;
                        double tOldMax = (maxChIdx >= 0 && maxChIdx < baselineSample.Value.Length) ? baselineSample.Value[maxChIdx] : tNowMax;
                        double diffMax = Math.Abs(tNowMax - tOldMax);
                        bool isBalanced = (diffMax < 1.0);

                        string thermalSummary = string.Format("S1 熱平衡: 最高溫近30分溫差 = {0:F2}℃ (CH{1}, 門檻<1.0℃) | 溫升斜率: {2:+0.00;-0.00}℃/分 | {3}",
                            diffMax, maxChIdx + 1, s1Slope, isBalanced ? "最高溫已達熱平衡！" : s1EstText);
                        lblThermalStatus.Text = thermalSummary;
                        lblThermalStatus.ForeColor = isBalanced ? Color.SeaGreen : Color.DarkOrange;
                        if (lblS1ThermalStatus != null)
                        {
                            lblS1ThermalStatus.Text = string.Format("最高溫30分溫差: {0:F2}℃ (CH{1}) | 斜率: {2:+0.00;-0.00}℃/分 | {3}",
                                diffMax, maxChIdx + 1, s1Slope, isBalanced ? "最高溫已達平衡" : s1EstText);
                            lblS1ThermalStatus.ForeColor = isBalanced ? Color.SeaGreen : Color.DarkOrange;
                        }

                        if (isBalanced)
                        {
                            dutyTimer.Stop();
                            if (isManualRecording)
                            {
                                StopManualRecording(showPrompt: false);
                            }
                            StartGradualAutoStop(spdDrive, trqDrive, "S1熱平衡達標(最高溫30min溫差<1.0℃)", () => {
                                ResetDutyTestToStandby();

                                string s1ReportPath = SaveDutyTestReportCsv("S1", showDialog: false);

                                lblDutyStatus.Text = string.Format("[成功] S1 熱平衡達標！最高溫 CH{0} 近30分溫差 {1:F2}℃ (< 1.0℃)，試驗自動完成！", maxChIdx + 1, diffMax);
                                lblDutyPhaseAction.Text = string.Format("最高溫通道 (CH{0}: {1:F1}℃) 30 分鐘溫差僅 {2:F2}℃，已達穩定熱平衡，平穩停機完成。", maxChIdx + 1, tNowMax, diffMax);
                                if (lblDutyMiniStatus != null) lblDutyMiniStatus.Text = lblDutyStatus.Text;
                                if (lblDutyMiniPhaseAction != null) lblDutyMiniPhaseAction.Text = lblDutyPhaseAction.Text;

                                WriteHmiLog("DUTY_S1_BALANCE", string.Format("【S1 熱平衡達標自動停機】耗時 {0} 分鐘，最高溫 CH{1} ({2:F1}℃) 30 分鐘溫差僅 {3:F2}℃ (< 1.0℃)，正式宣告熱平衡！", elapsedMins, maxChIdx + 1, tNowMax, diffMax));

                                if (isBatchDutyActive)
                                {
                                    AdvanceBatchAfterS1();
                                }
                                else
                                {
                                    MessageBox.Show(string.Format("S1 連續工作制熱平衡已達標！\n\n已運轉時間: {0} 分鐘\n最高溫監控通道: CH{1} (實測: {2:F1}℃)\n近 30 分鐘溫差: {3:F2}℃ (< 1.0℃)\n\n最高溫已達穩定熱平衡，報表已自動儲存至:\n{4}", elapsedMins, maxChIdx + 1, tNowMax, diffMax, s1ReportPath ?? "預設目錄"), "S1 熱平衡達標·試驗完成", MessageBoxButtons.OK, MessageBoxIcon.Information);
                                }
                            });
                            return;
                        }
                    }
                }
                else
                {
                    lblThermalStatus.Text = "熱平衡判定: 已關閉";
                    lblThermalStatus.ForeColor = Color.Gray;
                    if (lblS1ThermalStatus != null) lblS1ThermalStatus.Text = "熱平衡判定: 已停用";
                }

                if (dutyElapsedSec >= dutyTotalSec)
                {
                    dutyTimer.Stop();
                    if (isManualRecording)
                    {
                        StopManualRecording(showPrompt: false);
                    }
                    StartGradualAutoStop(spdDrive, trqDrive, "S1達到最大運轉時間停機", () => {
                        ResetDutyTestToStandby();

                        lblDutyStatus.Text = "[完成] S1 連續工作制已達設定運轉上限！";
                        lblDutyPhaseAction.Text = "已達保護時限，已平緩降載降速停機。";
                        if (lblDutyMiniStatus != null) lblDutyMiniStatus.Text = lblDutyStatus.Text;
                        if (lblDutyMiniPhaseAction != null) lblDutyMiniPhaseAction.Text = lblDutyPhaseAction.Text;

                        MessageBox.Show("S1 連續工作制已達到安全運轉時限，已平滑停機！", "試驗完成", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    });
                    return;
                }
            }
            else if (dutyModeIdx == 1) // ================= S2 短時工作制 (含錨點測試狀態機) =================
            {
                if (isS2Calibrating)
                {
                    // 【S2 錨點測試流程】到達指定轉速與扭矩後穩定 10 秒  記住此錨點 (SY52 / CS18)  停機
                    currentPhaseName = "S2 錨點測試";

                    // 子階段 0：空載提速至使用者輸入之目標轉速
                    if (s2CalibStage == 0)
                    {
                        KebWriteParamWithDll(trqCom, trqBaud, trqNode, 0x0F12, 0); // 加載端保持 0 轉矩
                        bool isSpdClose = Math.Abs(actAbsSpd - targetSpd) <= Math.Max(20.0, targetSpd * 0.05);
                        if (isSpdClose)
                        {
                            s2CalibStage = 1;
                            WriteHmiLog("S2_ANCHOR", string.Format("【S2 錨點測試】轉速已接近目標 ({0:F0}/{1:F0} rpm)，加載端已於低速激磁就緒，開始平滑漸進加載！", actAbsSpd, targetSpd));
                        }
                        lblDutyStatus.Text = string.Format("【S2 錨點測試】空載提速中 (實測 {0:F0}/{1:F0} rpm)...", actAbsSpd, targetSpd);
                        lblDutyPhaseAction.Text = isLoadMotorPreEnergized
                            ? "待測端加速至設定目標轉速，加載端已激磁零轉矩熱備妥跟隨中..."
                            : "待測端加速中，等待達 60 rpm 加載端激磁預備...";
                    }
                    // 子階段 1：統一自適應定錨加速加載 (前 3 秒試探斜率 -> 依目標差距開出最大 5% 步長狂衝 -> 同動補轉差)
                    else if (s2CalibStage == 1)
                    {
                        string s2TrackStatus;
                        bool isConverged = ExecuteUnifiedDualTrackingStep(
                            s2LoadTracker, spdDrive, trqDrive,
                            targetSpd, targetTrq, actAbsSpd, actAbsTrq,
                            out s2TrackStatus, "S2校驗");

                        s6AdaptedTorquePct = s2LoadTracker.AdaptedTorquePct;
                        s6CurrentSpeedCmd = s2LoadTracker.CurrentSpeedCmd;

                        lblDutyStatus.Text = string.Format("【S2 錨點測試】{0}", s2TrackStatus);
                        lblDutyPhaseAction.Text = string.Format("自適應加載中：給定 {0:F1}%，待測端命令 {1:F0} rpm", s6AdaptedTorquePct, s6CurrentSpeedCmd);

                        // 雙達標收斂，進入 10 秒穩定確認倒數
                        if (isConverged)
                        {
                            s2CalibStage = 2;
                            s2CalibTimer = 10;
                            WriteHmiLog("S2_ANCHOR", string.Format("【S2 錨點測試】轉速與轉矩雙雙達標收斂 ({0:F1} Nm / {1:F0} rpm)，開始 10 秒穩定確認倒數！", actAbsTrq, actAbsSpd));
                        }
                    }
                    // 子階段 2：10 秒穩定確認  記住錨點並停機
                    else if (s2CalibStage == 2)
                    {
                        double trqErr = targetTrq - actAbsTrq;
                        double spdSlip = targetSpd - actAbsSpd;
                        if (Math.Abs(trqErr) > 0.4)
                        {
                            s6AdaptedTorquePct += (trqErr > 0 ? 0.1 : -0.1);
                            s6AdaptedTorquePct = Math.Max(0.0, Math.Min(100.0, s6AdaptedTorquePct));
                            KebWriteParamWithDll(trqCom, trqBaud, trqNode, 0x0F12, (int)Math.Round(s6AdaptedTorquePct * 10));
                        }
                        if (Math.Abs(spdSlip) > 2.0)
                        {
                            s6CurrentSpeedCmd += (spdSlip > 0 ? 1.0 : -1.0);
                            KebWriteParam32(spdCom, spdBaud, spdNode, 0x0034, (int)s6CurrentSpeedCmd, "S2 定錨微調");
                        }

                        // 同步主畫面
                        if (spdDrive == 2 && numHmiKebSpeed2 != null) numHmiKebSpeed2.Value = (decimal)s6CurrentSpeedCmd;
                        else if (spdDrive == 1 && numHmiKebSpeed1 != null) numHmiKebSpeed1.Value = (decimal)s6CurrentSpeedCmd;
                        if (trqDrive == 1 && numHmiKebTorque1 != null) numHmiKebTorque1.Value = (decimal)s6AdaptedTorquePct;
                        else if (trqDrive == 2 && numHmiKebTorque2 != null) numHmiKebTorque2.Value = (decimal)s6AdaptedTorquePct;

                        bool isTrqReached = Math.Abs(trqErr) <= Math.Max(1.0, targetTrq * 0.08);
                        bool isSpdReached = Math.Abs(spdSlip) <= Math.Max(20.0, targetSpd * 0.05);

                        if (isTrqReached && isSpdReached)
                        {
                            s2CalibTimer--;
                            lblDutyStatus.Text = string.Format("【S2 錨點測試】轉速與轉矩已到位，穩定 10 秒確認中 (倒數 {0}s)...", s2CalibTimer);
                            lblDutyPhaseAction.Text = string.Format("實測 {0:F0} rpm | 轉矩 {1:F1} Nm (穩定確認中)", actAbsSpd, actAbsTrq);
                        }
                        else
                        {
                            lblDutyStatus.Text = string.Format("【S2 錨點測試】轉矩或轉速調節中 (實測 {0:F0}rpm/{1:F1}Nm, 暫停倒數 {2}s)...", actAbsSpd, actAbsTrq, s2CalibTimer);
                            lblDutyPhaseAction.Text = "數值偏離容許帶，微調穩定中...";
                        }

                        if (s2CalibTimer <= 0)
                        {
                            // 成功記住此錨點！
                            s2AnchorSy52 = (int)Math.Round(s6CurrentSpeedCmd);
                            s2AnchorCs18 = (int)Math.Round(s6AdaptedTorquePct * 10);
                            if (numS2AnchorSy52 != null) numS2AnchorSy52.Value = s2AnchorSy52;
                            if (numS2AnchorCs18 != null) numS2AnchorCs18.Value = s2AnchorCs18;

                            dutyTimer.Stop();
                            if (isManualRecording)
                            {
                                StopManualRecording(showPrompt: false);
                            }
                            isS2Calibrating = false;

                            WriteHmiLog("S2_ANCHOR", string.Format("【S2 錨點測試成功確立】穩定滿 10 秒！已記錄錨點：SY52={0} rpm, CS18={1} ‰ ({2:F1}%)，平滑煞車停機！",
                                s2AnchorSy52, s2AnchorCs18, s2AnchorCs18 / 10.0));

                            StartGradualAutoStop(spdDrive, trqDrive, "S2錨點偵測成功停機", () => {
                                ResetDutyTestToStandby();

                                lblDutyStatus.Text = string.Format("[錨點已記錄] SY52={0} rpm, CS18={1} ‰", s2AnchorSy52, s2AnchorCs18);
                                lblDutyPhaseAction.Text = "錨點偵測成功並已停機！下次按下「開始」將直接套用此錨點運轉。";
                                if (lblDutyMiniStatus != null) lblDutyMiniStatus.Text = lblDutyStatus.Text;
                                if (lblDutyMiniPhaseAction != null) lblDutyMiniPhaseAction.Text = lblDutyPhaseAction.Text;

                                MessageBox.Show(
                                    string.Format("【S2 錨點測試完成】\n\n已成功捕捉 10 秒穩定錨點：\n速度控制數據 (SY52): {0} rpm\n扭力控制數據 (CS18): {1} ‰ ({2:F1}%)\n\n已自動煞車平穩停機！\n數值已記錄至介面輸入框中（亦可手動微調），下次按下「開始」將直接套用此錨點運轉！",
                                        s2AnchorSy52, s2AnchorCs18, s2AnchorCs18 / 10.0),
                                    "S2 錨點偵測成功",
                                    MessageBoxButtons.OK,
                                    MessageBoxIcon.Information
                                );
                            });
                            return;
                        }
                    }
                    if (lblDutyMiniStatus != null) lblDutyMiniStatus.Text = lblDutyStatus.Text;
                    if (lblDutyMiniPhaseAction != null) lblDutyMiniPhaseAction.Text = lblDutyPhaseAction.Text;
                }
                else
                {
                    // 【S2 正式短時工作制運轉】
                    dutyElapsedSec++;
                    if (dutyElapsedSec <= prgDuty.Maximum) prgDuty.Value = dutyElapsedSec;
                    if (prgDutyMini != null && dutyElapsedSec <= prgDutyMini.Maximum) prgDutyMini.Value = dutyElapsedSec;

                    currentPhaseName = "S2 短時";

                    // 【全系統統一核心引擎：SY52 + CS18 雙閉環微調維持】
                    string s2RunTrackStatus;
                    ExecuteUnifiedDualTrackingStep(
                        s2LoadTracker, spdDrive, trqDrive,
                        targetSpd, targetTrq, actAbsSpd, actAbsTrq,
                        out s2RunTrackStatus, "S2運轉");

                    s6AdaptedTorquePct = s2LoadTracker.AdaptedTorquePct;
                    s6CurrentSpeedCmd = s2LoadTracker.CurrentSpeedCmd;

                    int s2RemSec = Math.Max(0, dutyTotalSec - dutyElapsedSec);
                    int s2RemMin = s2RemSec / 60;
                    int s2RemS = s2RemSec % 60;
                    lblDutyStatus.Text = string.Format("【S2 短時】已耗時 {0}s / 剩餘 {1:D2}:{2:D2} | 目標 {3:F1} Nm, 實測 {4:F1} Nm",
                        dutyElapsedSec, s2RemMin, s2RemS, targetTrq, actAbsTrq);
                    lblDutyPhaseAction.Text = string.Format("【恆定加載】加載輸出 {0:F1}%, 實測轉速 {1:F0} rpm", s6AdaptedTorquePct, actAbsSpd);

                    // 溫度讀取與超溫安全停機
                    int chIdx = (cmbS2TempCh != null && cmbS2TempCh.SelectedIndex >= 0) ? cmbS2TempCh.SelectedIndex : 0;
                    double currentChTemp = (chIdx >= 0 && chIdx < chSnapshot.Length) ? chSnapshot[chIdx] : actTemp;
                    if (lblS2TempRealtime != null)
                        lblS2TempRealtime.Text = string.Format("實測: {0:F1} ℃", currentChTemp);

                    // 記錄 S2 溫度歷史 (保留 45 分鐘)
                    if (s2TempHistory != null)
                    {
                        s2TempHistory.Add(new KeyValuePair<DateTime, double>(now, currentChTemp));
                        DateTime expireS2 = now.AddMinutes(-45);
                        s2TempHistory.RemoveAll(x => x.Key < expireS2);
                    }

                    // ── S2 即時溫升斜率與到達時長終溫預估演算 ──
                    double s2Slope = CalculateThermalSlopePerMin(s2TempHistory, 120);
                    double targetDurMin = (numS2DurationMin != null) ? (double)numS2DurationMin.Value : 30.0;
                    double elapsedMin = dutyElapsedSec / 60.0;
                    double remMin = Math.Max(0.0, targetDurMin - elapsedMin);
                    double threshold = (numS2TempThreshold != null) ? (double)numS2TempThreshold.Value : 80.0;

                    // 依據與 S1 相同之一階熱動態衰減模型：
                    // deltaTRem = slope * tau * (1 - exp(-remMin / tau))
                    double tauS2 = 30.0; // 分鐘
                    double deltaTRem = (s2Slope > 0) ? (s2Slope * tauS2 * (1.0 - Math.Exp(-remMin / tauS2))) : 0.0;
                    double estFinalTemp = currentChTemp + deltaTRem;
                    bool willOverheat = (chkS2TempStop != null && chkS2TempStop.Checked && estFinalTemp >= threshold);

                    if (lblS2ThermalStatus != null)
                    {
                        if (dutyElapsedSec < 30)
                        {
                            lblS2ThermalStatus.Text = string.Format("S2 溫升斜率: 採樣中 ({0}s) | 時長 {1:F0}m 預估終溫: 計算中...", dutyElapsedSec, targetDurMin);
                            lblS2ThermalStatus.ForeColor = Color.FromArgb(30, 64, 175);
                        }
                        else
                        {
                            string hint = willOverheat ? string.Format(" [!][預估超溫! 閥值{0:F1}℃]", threshold) : string.Format(" (閥值{0:F1}℃·安全)", threshold);
                            lblS2ThermalStatus.Text = string.Format("溫升斜率: {0:+0.00;-0.00}℃/m | 時長 {1:F0}m 預估終溫: {2:F1}℃{3}",
                                s2Slope, targetDurMin, estFinalTemp, hint);
                            lblS2ThermalStatus.ForeColor = willOverheat ? Color.FromArgb(220, 38, 38) : Color.FromArgb(30, 64, 175);
                        }
                    }

                    if (lblThermalStatus != null)
                    {
                        if (dutyElapsedSec < 30)
                        {
                            lblThermalStatus.Text = string.Format("S2 溫升預估: 採樣中 ({0}s) | 目標時長: {1:F0} 分鐘", dutyElapsedSec, targetDurMin);
                        }
                        else
                        {
                            lblThermalStatus.Text = string.Format("S2 溫升預估: 斜率 {0:+0.00;-0.00}℃/m | 時長 {1:F0}m 預估終溫: {2:F1}℃ {3}",
                                s2Slope, targetDurMin, estFinalTemp, willOverheat ? "[!][預估超溫]" : "[安全]");
                            lblThermalStatus.ForeColor = willOverheat ? Color.FromArgb(220, 38, 38) : Color.FromArgb(30, 64, 175);
                        }
                    }

                    lblDutyPhaseAction.Text = string.Format("【恆定加載】加載輸出 {0:F1}%, 實測轉速 {1:F0} rpm | 預估終溫: {2:F1}℃{3}",
                        s6AdaptedTorquePct, actAbsSpd, estFinalTemp, willOverheat ? " [!]注意超溫" : "");

                    UpdateDynamicExtrapolationS2(targetTrq, targetDurMin, estFinalTemp, s2Slope, actAbsSpd, dutyElapsedSec);

                    if (chkS2TempStop != null && chkS2TempStop.Checked)
                    {
                        if (currentChTemp >= threshold)
                        {
                            dutyTimer.Stop();
                            if (isManualRecording)
                            {
                                StopManualRecording(showPrompt: false);
                            }
                            StartGradualAutoStop(spdDrive, trqDrive, string.Format("S2溫度超限保護 (CH{0}={1:F1}℃ >= {2:F1}℃)", chIdx + 1, currentChTemp, threshold), () => {
                                ResetDutyTestToStandby();

                                //  依要求：S2 完成一個執行流程後自動歸零錨點
                                s2AnchorSy52 = 0;
                                s2AnchorCs18 = 0;
                                if (numS2AnchorSy52 != null) numS2AnchorSy52.Value = 0;
                                if (numS2AnchorCs18 != null) numS2AnchorCs18.Value = 0;

                                string alertMsg = string.Format("[超溫保護] CH{0} 溫度 ({1:F1}℃) 超過閥值 ({2:F1}℃) 自動停機！", chIdx + 1, currentChTemp, threshold);
                                lblDutyStatus.Text = alertMsg;
                                lblDutyPhaseAction.Text = "達到設定超溫保護閥值，已平緩降載降速停機，錨點已自動歸零。";
                                if (lblDutyMiniStatus != null) lblDutyMiniStatus.Text = alertMsg;
                                if (lblDutyMiniPhaseAction != null) lblDutyMiniPhaseAction.Text = lblDutyPhaseAction.Text;

                                WriteHmiLog("DUTY_S2_OVERTEMP", string.Format("【S2 溫度超限停機】CH{0} 實測 {1:F1} ℃ 超過設定閥值 {2:F1} ℃，已平滑停機，錨點數據已歸零！", chIdx + 1, currentChTemp, threshold));

                                if (isBatchDutyActive)
                                {
                                    AbortBatchDuty(string.Format("S2 測試期間觸發超溫保護停機 (CH{0}: {1:F1}℃ >= {2:F1}℃)！連續排程已中止保護。", chIdx + 1, currentChTemp, threshold));
                                }
                                else
                                {
                                    MessageBox.Show(string.Format("S2 短時工作制測試觸發溫度保護停機！\n\n監控通道: CH{0}\n實測溫度: {1:F1} ℃\n設定閥值: {2:F1} ℃\n\n已自動平緩降載降速並停止運轉，S2 錨點數據已自動歸零。", chIdx + 1, currentChTemp, threshold), "溫度保護停機", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                                }
                            });
                            return;
                        }
                    }

                    // 截止時間判定
                    if (dutyElapsedSec >= dutyTotalSec)
                    {
                        dutyTimer.Stop();
                        if (isManualRecording)
                        {
                            StopManualRecording(showPrompt: false);
                        }
                        StartGradualAutoStop(spdDrive, trqDrive, "S2短時工作制時間截止", () => {
                            ResetDutyTestToStandby();

                            RecordAndExtrapolateS2();
                            string s2ReportPath = SaveDutyTestReportCsv("S2", showDialog: false);

                            //  依要求：S2 完成一個執行流程後自動歸零錨點
                            s2AnchorSy52 = 0;
                            s2AnchorCs18 = 0;
                            if (numS2AnchorSy52 != null) numS2AnchorSy52.Value = 0;
                            if (numS2AnchorCs18 != null) numS2AnchorCs18.Value = 0;

                            lblDutyStatus.Text = "[成功] S2 短時工作制試驗時間到期，圓滿完成！";
                            lblDutyPhaseAction.Text = "已達到設定之短時運轉時間，已平緩降載降速停機，錨點已自動歸零，報表已自動儲存。";
                            if (lblDutyMiniStatus != null) lblDutyMiniStatus.Text = lblDutyStatus.Text;
                            if (lblDutyMiniPhaseAction != null) lblDutyMiniPhaseAction.Text = lblDutyPhaseAction.Text;

                            WriteHmiLog("DUTY_S2_COMPLETE", string.Format("【S2 試驗完成】已達設定測試時間 {0} 分鐘，正常平緩結束停機，錨點數據已歸零！", dutyTotalSec / 60));

                            if (isBatchDutyActive)
                            {
                                int plan = GetBatchDutyPlan();
                                isBatchDutyActive = false;
                                batchDutyStage = 6;
                                int maxSteps = (plan == 0) ? 5 : 3;
                                if (prgBatchTotal != null) prgBatchTotal.Value = maxSteps;
                                string planName = (plan == 0) ? "S1、S6、S2 三大" : ((plan == 2) ? "S1、S2 兩項" : "S6、S2 兩項");
                                if (lblBatchProgressDetail != null)
                                {
                                    lblBatchProgressDetail.Text = string.Format("【連續排程圓滿結束】{0}工作制實驗全部測試完成！報表已全數自動存檔，可點擊「匯出報表」查看。", planName);
                                }
                                WriteHmiLog("BATCH_COMPLETE", string.Format("【連續排程全勝】{0}連續性全自動工作制測試全數完成！", planName));
                            }
                            else
                            {
                                MessageBox.Show(string.Format("S2 短時工作制試驗（共 {0} 分鐘）已順利完成！\n已平緩煞車停機，S2 錨點數據已自動歸零。\n報表已自動儲存至:\n{1}", dutyTotalSec / 60, s2ReportPath ?? "預設目錄"), "試驗完成", MessageBoxButtons.OK, MessageBoxIcon.Information);
                            }
                        });
                        return;
                    }
                }
            }

            // 定期採樣記錄 (每 5 秒，以標準時間戳與經過秒數雙軌儲存)
            if (dutyElapsedSec % 5 == 0)
            {
                double validSpeed = Math.Abs(actSpeed);
                double validTorque = Math.Abs(actTorque);
                double validMechPower = Math.Abs(actMechPower);
                double validEfficiency = Math.Abs(actEfficiency);

                int atIdx = FindAmbientTempChannelIndex();
                double tAmbient = (atIdx >= 0 && gbdChTemps != null && atIdx < gbdChTemps.Length && gbdChTemps[atIdx] > 0) ? gbdChTemps[atIdx] : GetNamedTempChannel("AT", -1, 25.0);
                double tCoil = GetCurrentPeakTemp();
                double tBearingF = GetNamedTempChannel("BEARINGF", -1, GetNamedTempChannel("FB", -1, 0.0));
                double tBearingR = GetNamedTempChannel("BEARINGR", -1, GetNamedTempChannel("BB", -1, 0.0));
                double tEncoder = GetNamedTempChannel("ENC", -1, 0.0);
                double tWaterIn = GetNamedTempChannel("WIN", -1, 0.0);
                double tWaterOut = GetNamedTempChannel("WOUT", -1, 0.0);

                dutyResults.Add(new string[] {
                    now.ToString("yyyy-MM-dd HH:mm:ss"),
                    dutyElapsedSec.ToString(),
                    currentPhaseName,
                    validSpeed.ToString("F1"),
                    actFrequency.ToString("F2"),
                    validTorque.ToString("F2"),
                    validMechPower.ToString("F2"),
                    validEfficiency.ToString("F1"),
                    (wtU1 > 0 ? wtU1 : (float)actVoltageSigma).ToString("F1"),
                    (wtU2 > 0 ? wtU2 : (float)actVoltageSigma).ToString("F1"),
                    (wtU3 > 0 ? wtU3 : (float)actVoltageSigma).ToString("F1"),
                    (wtI1 > 0 ? wtI1 : (float)actCurrentSigma).ToString("F2"),
                    (wtI2 > 0 ? wtI2 : (float)actCurrentSigma).ToString("F2"),
                    (wtI3 > 0 ? wtI3 : (float)actCurrentSigma).ToString("F2"),
                    actElecPower.ToString("F2"),
                    actPf.ToString("F3"),
                    tAmbient.ToString("F1"),
                    tCoil.ToString("F1"),
                    tBearingF.ToString("F1"),
                    tBearingR.ToString("F1"),
                    tEncoder.ToString("F1"),
                    tWaterIn.ToString("F1"),
                    tWaterOut.ToString("F1")
                });
            }

            // 每分鐘記錄一行至 dgvDuty (免存檔案，UI 即時監控，自動滾動與記憶體修剪)
            int dutyCurrentMinute = dutyElapsedSec / 60;
            if (dgvDuty != null && dutyElapsedSec > 0 && (dutyElapsedSec % 60 == 0 || dutyElapsedSec == 1) && dutyCurrentMinute != dutyLastMinuteLogged)
            {
                dutyLastMinuteLogged = dutyCurrentMinute;
                string timeStr = string.Format("{0:D2}:{1:D2}", dutyCurrentMinute, dutyElapsedSec % 60);

                // 計算過去一分鐘之均值 (若為第 1 秒則採用瞬時值)
                double avgSpd = dutyMinuteSampleCount > 0 ? (dutyMinuteSpdSum / dutyMinuteSampleCount) : actAbsSpd;
                double avgTrq = dutyMinuteSampleCount > 0 ? (dutyMinuteTrqSum / dutyMinuteSampleCount) : actAbsTrq;
                double pMech = (avgSpd * avgTrq) / 9549.0;

                double avgV = dutyMinuteSampleCount > 0 ? (dutyMinuteVoltSum / dutyMinuteSampleCount) : actVoltageSigma;
                double avgI = dutyMinuteSampleCount > 0 ? (dutyMinuteCurrSum / dutyMinuteSampleCount) : actCurrentSigma;
                double avgPelec = dutyMinuteSampleCount > 0 ? (dutyMinutePelecSum / dutyMinuteSampleCount) : actElecPower;
                double avgPf = dutyMinuteSampleCount > 0 ? (dutyMinutePfSum / dutyMinuteSampleCount) : actPf;

                string effStr = (actEfficiency > 0 && actEfficiency <= 100) ? actEfficiency.ToString("F1") : "--";
                if (avgPelec > 0.05 && pMech > 0.05)
                {
                    double calcEff = (pMech / avgPelec) * 100.0;
                    if (calcEff > 0 && calcEff <= 100.0) effStr = calcEff.ToString("F1");
                }

                string vStr = avgV > 0.1 ? avgV.ToString("F1") : "--";
                string iStr = avgI > 0.05 ? avgI.ToString("F2") : "--";
                string pElecStr = Math.Abs(avgPelec) > 0.01 ? avgPelec.ToString("F2") : "--";
                string pfStr = Math.Abs(avgPf) > 0.001 ? avgPf.ToString("F3") : "--";

                string tempStr = string.Format("{0:F1} (CH{1})", dutyMonitoredTemp, maxChIdx + 1);
                string thStr = "--";
                if (dutyModeIdx == 0)
                {
                    if (lblThermalStatus != null && !string.IsNullOrEmpty(lblThermalStatus.Text))
                        thStr = lblThermalStatus.Text.Replace("S1 熱平衡: ", "").Replace("熱平衡判定: ", "");
                }
                else if (dutyModeIdx == 2)
                {
                    thStr = (lblS6ThermalStatus != null ? lblS6ThermalStatus.Text : "--");
                }

                // 重設一分鐘累加器
                dutyMinuteVoltSum = 0.0;
                dutyMinuteCurrSum = 0.0;
                dutyMinutePelecSum = 0.0;
                dutyMinutePfSum = 0.0;
                dutyMinuteSpdSum = 0.0;
                dutyMinuteTrqSum = 0.0;
                dutyMinuteSampleCount = 0;

                dgvDuty.Rows.Add(
                    now.ToString("HH:mm:ss"),
                    timeStr,
                    currentPhaseName,
                    avgSpd.ToString("F0"),
                    avgTrq.ToString("F1"),
                    pMech.ToString("F2"),
                    vStr,
                    iStr,
                    pElecStr,
                    pfStr,
                    effStr,
                    tempStr,
                    thStr
                );

                if (dgvDuty.RowCount > 0)
                {
                    dgvDuty.FirstDisplayedScrollingRowIndex = dgvDuty.RowCount - 1;
                }

                // 記憶體防護：若行數超過 1500，批次修剪舊記錄
                if (dgvDuty.RowCount > 1500)
                {
                    for (int r = 0; r < 200 && dgvDuty.RowCount > 1000; r++)
                    {
                        dgvDuty.Rows.RemoveAt(0);
                    }
                }
            }
            // ── 遠端監看：每秒同步最新狀態標籤 ──
            if (lblDutyStatus != null)     { webRemoteStatusText = lblDutyStatus.Text; }
            if (lblDutyPhaseAction != null) { webRemotePhaseText  = lblDutyPhaseAction.Text; }
        }

        private void SaveS1NoSlipResultAndCsv(double targetSpd, double targetTrq)
        {
            try
            {
                if (s1NoSlipBuffer == null || s1NoSlipBuffer.Count < 40) return;

                // 去掉前 5 筆 (0..4) 與後 5 筆 (35..39)，取中間 30 筆 (5..34)
                var validSamples = s1NoSlipBuffer.Skip(5).Take(30).ToList();
                double avgSpd = validSamples.Average(s => s.Speed);
                double avgTrq = validSamples.Average(s => s.Torque);
                double avgV = validSamples.Average(s => s.Voltage);
                double avgI = validSamples.Average(s => s.Current);
                double avgP = validSamples.Average(s => s.PowerKw);
                double avgPf = validSamples.Average(s => s.PowerFactor);
                double avgFreq = validSamples.Average(s => s.Frequency);

                int poles = kebMotorPoles2 > 0 ? kebMotorPoles2 : 4;
                double syncSpd = 120.0 * avgFreq / poles;
                double slip = (syncSpd > 0 && avgSpd > 0) ? Math.Max(0.0, ((syncSpd - avgSpd) / syncSpd) * 100.0) : 0.0;

                string mName = !string.IsNullOrEmpty(motorModelName) ? motorModelName : "SVM100S";
                string logDir = GetMotorDedicatedLogDirectory(mName);

                string timeStr = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                // 統一名稱契約：[MotorModel]_[TestType]_[DutyOrCondition]_[Status]_[yyyyMMdd_HHmmss].csv
                string fileHist = Path.Combine(logDir, string.Format("{0}_Equiv_Rated_COMPLETED_{1}.csv", mName, timeStr));
                string fileLatest = Path.Combine(logDir, string.Format("{0}_Equiv_Rated_Latest.csv", mName));
                string fileLegacyLatest = Path.Combine(logDir, "S1_Rated_NoSlip_Latest.csv");
                string fileLegacyHist = Path.Combine(logDir, string.Format("S1_Rated_NoSlip_40pts_{0}.csv", timeStr));

                StringBuilder sb = new StringBuilder();
                sb.AppendLine("# ========================================================================================");
                sb.AppendLine("# Dynamometer HMI Test Telemetry Log (IEEE Std 112 / IEC 60034-2-1)");
                sb.AppendLine(string.Format("# MotorModel: {0}", mName));
                sb.AppendLine("# TestCategory: EquivalentCircuit_Rated");
                sb.AppendLine("# TestStatus: COMPLETED");
                sb.AppendLine(string.Format("# TotalSamples: {0} | TrimmedSamples: 30 (Removed first 5 and last 5)", s1NoSlipBuffer.Count));
                sb.AppendLine(string.Format("# AverageSpeed_rpm: {0:F2}", avgSpd));
                sb.AppendLine(string.Format("# AverageTorque_Nm: {0:F3}", avgTrq));
                sb.AppendLine(string.Format("# AverageVoltage_V: {0:F2}", avgV));
                sb.AppendLine(string.Format("# AverageCurrent_A: {0:F3}", avgI));
                sb.AppendLine(string.Format("# AveragePower_kW: {0:F4}", avgP));
                sb.AppendLine(string.Format("# AveragePowerFactor: {0:F4}", avgPf));
                sb.AppendLine(string.Format("# AverageFrequency_Hz: {0:F2}", avgFreq));
                sb.AppendLine(string.Format("# SyncSpeed_rpm: {0:F1}", syncSpd));
                sb.AppendLine(string.Format("# Slip_pct: {0:F2}", slip));
                sb.AppendLine("# MotorPoles: " + poles);
                sb.AppendLine("# TargetSpeed_rpm: " + targetSpd.ToString("F0"));
                sb.AppendLine("# TargetTorque_Nm: " + targetTrq.ToString("F1"));
                sb.AppendLine("# GeneratedTime: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                sb.AppendLine("# ========================================================================================");
                sb.AppendLine("Index,Timestamp,Speed_rpm,Torque_Nm,Voltage_V,Current_A,Power_kW,PowerFactor,Freq_Hz,Included_In_Average");

                for (int i = 0; i < s1NoSlipBuffer.Count; i++)
                {
                    var s = s1NoSlipBuffer[i];
                    bool included = (i >= 5 && i < 35);
                    sb.AppendLine(string.Format("{0},{1},{2:F2},{3:F3},{4:F2},{5:F3},{6:F4},{7:F4},{8:F2},{9}",
                        i + 1, s.Time.ToString("HH:mm:ss"), s.Speed, s.Torque, s.Voltage, s.Current, s.PowerKw, s.PowerFactor, s.Frequency, (included ? "Yes" : "No")));
                }

                string content = sb.ToString();
                File.WriteAllText(fileHist, content, Encoding.UTF8);
                File.WriteAllText(fileLatest, content, Encoding.UTF8);
                File.WriteAllText(fileLegacyLatest, content, Encoding.UTF8);
                File.WriteAllText(fileLegacyHist, content, Encoding.UTF8);

                WriteHmiLog("S1_NOSLIP", string.Format("【S1 不補轉差採樣完成】已保存標準等效額定檔：NN={0:F1}rpm, TN={1:F2}Nm, VN={2:F1}V, IN={3:F2}A, PN={4:F3}kW, sN={5:F2}% -> {6}",
                    avgSpd, avgTrq, avgV, avgI, avgP, slip, Path.GetFileName(fileLatest)));
            }
            catch (Exception ex)
            {
                WriteHmiLog("S1_ERR", "儲存 S1 不補轉差紀錄失敗: " + ex.Message);
            }
        }

        public string SaveDutyTestReportCsv(string dutyPrefix, bool showDialog = false, string statusTag = "COMPLETED")
        {
            try
            {
                string mName = !string.IsNullOrEmpty(motorModelName) ? motorModelName : "SVM100S";
                string logDir = GetMotorDedicatedLogDirectory(mName);
                if (!Directory.Exists(logDir)) Directory.CreateDirectory(logDir);

                string timeStamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                // 統一名稱契約：[MotorModel]_[TestType]_[DutyOrCondition]_[Status]_[yyyyMMdd_HHmmss].csv
                string formalDutyTag = dutyPrefix;
                if (dutyPrefix == "S1") formalDutyTag = "S1-Rated";
                else if (dutyPrefix == "S2") formalDutyTag = "S2-30min";
                else if (dutyPrefix == "S6") formalDutyTag = "S6-Cycle";

                string fileName = string.Format("{0}_Duty_{1}_{2}_{3}.csv", mName, formalDutyTag, statusTag, timeStamp);
                string path = Path.Combine(logDir, fileName);

                using (StreamWriter sw = new StreamWriter(path, false, Encoding.UTF8))
                {
                    // 自描述 Metadata 標頭
                    sw.WriteLine("# ========================================================================================");
                    sw.WriteLine("# Dynamometer HMI Test Telemetry Log (IEEE Std 112 / IEC 60034)");
                    sw.WriteLine(string.Format("# MotorModel: {0}", mName));
                    sw.WriteLine(string.Format("# TestCategory: DutyCycle ({0})", formalDutyTag));
                    sw.WriteLine(string.Format("# CoolingType: {0}", (HasWaterCoolingChannels() ? "Water" : "Air")));
                    sw.WriteLine(string.Format("# TestDuration_sec: {0}", dutyResults.Count * 5));
                    sw.WriteLine(string.Format("# TestStatus: {0}", statusTag));
                    sw.WriteLine(string.Format("# GeneratedTime: {0:yyyy-MM-dd HH:mm:ss}", DateTime.Now));

                    // 注入能力極限回推摘要 (依實測溫升回推 105℃ 極限扭矩或 DUTY 佔比)
                    if (dutyPrefix == "S2" && (hasS2TestResult || lastS2Torque > 0.1))
                    {
                        double bTrq = (lastS2Torque > 0.1) ? lastS2Torque : (double)numDutyTorque.Value;
                        double bDur = (lastS2DurationMin > 0.1) ? lastS2DurationMin : 30.0;
                        double bTemp = (lastS2FinalTemp > 0.1) ? lastS2FinalTemp : 90.0;
                        double bAmb = (lastS2AmbientTemp > 0.1) ? lastS2AmbientTemp : 25.0;
                        double bSpd = (lastS2Speed > 0.1) ? lastS2Speed : (double)numDutySpeed.Value;
                        var ext = CalcExtrapolatedS2Torque(bTrq, bDur, bTemp, bAmb, 105.0, bSpd);
                        sw.WriteLine("# ----------------------------------------------------------------------------------------");
                        sw.WriteLine("# [CAPABILITY_EXTRAPOLATION_S2]");
                        sw.WriteLine(string.Format("# BaseTorque_Nm: {0:F1}", ext.BaseTorque));
                        sw.WriteLine(string.Format("# Duration_min: {0:F1}", ext.DurationMin));
                        sw.WriteLine(string.Format("# FinalTemp_degC: {0:F1}", ext.FinalTemp));
                        sw.WriteLine(string.Format("# AmbientTemp_degC: {0:F1}", ext.AmbientTemp));
                        sw.WriteLine(string.Format("# TempRiseSlope_K_min: {0:F2}", ext.TempRiseSlope));
                        sw.WriteLine(string.Format("# TargetLimitTemp_degC: {0:F1}", ext.TargetTemp));
                        sw.WriteLine(string.Format("# ExtrapolatedMaxTorque_Nm: {0:F1} (Margin: +{1:F1}%)", ext.ExtrapolatedTorque, ext.TorqueIncreasePct));
                        sw.WriteLine(string.Format("# EstimatedMechPower_kW: {0:F2} (at {1:F0} rpm)", ext.ExtrapolatedMechPowerKw, ext.RatedSpeed));
                    }
                    else if (dutyPrefix == "S6" && (hasS6TestResult || lastS6Torque > 0.1))
                    {
                        double bTrq = (lastS6Torque > 0.1) ? lastS6Torque : (double)numDutyTorque.Value;
                        double bCyc = (lastS6CycleMin > 0.1) ? lastS6CycleMin : 10.0;
                        double bEd = (lastS6EdPct > 0.1) ? lastS6EdPct : 40.0;
                        double bTemp = (lastS6PeakTemp > 0.1) ? lastS6PeakTemp : 85.0;
                        double bAmb = (lastS6AmbientTemp > 0.1) ? lastS6AmbientTemp : 25.0;
                        var ext = CalcExtrapolatedS6Ed(bTrq, bCyc, bEd, bTemp, bAmb, 105.0);
                        sw.WriteLine("# ----------------------------------------------------------------------------------------");
                        sw.WriteLine("# [CAPABILITY_EXTRAPOLATION_S6]");
                        sw.WriteLine(string.Format("# BaseTorque_Nm: {0:F1}", ext.BaseTorque));
                        sw.WriteLine(string.Format("# CycleDuration_min: {0:F1}", ext.CycleMin));
                        sw.WriteLine(string.Format("# BaseED_pct: {0:F1}", ext.CurrentEdPct));
                        sw.WriteLine(string.Format("# PeakTemp_degC: {0:F1}", ext.PeakTemp));
                        sw.WriteLine(string.Format("# AmbientTemp_degC: {0:F1}", ext.AmbientTemp));
                        sw.WriteLine(string.Format("# TargetLimitTemp_degC: {0:F1}", ext.TargetTemp));
                        sw.WriteLine(string.Format("# ExtrapolatedMaxED_pct: {0:F1} (Margin: +{1:F1}%)", ext.ExtrapolatedEdPct, ext.EdIncreasePct));
                        sw.WriteLine(string.Format("# ExtrapolatedT1_Loaded_min: {0:F2}", ext.ExtrapolatedT1Min));
                        sw.WriteLine(string.Format("# ExtrapolatedT2_NoLoad_min: {0:F2}", ext.ExtrapolatedT2Min));
                    }
                    sw.WriteLine("# ========================================================================================");
                    sw.WriteLine("StandardTime,ElapsedSec,Phase,Speed_rpm,Frequency_Hz,Torque_Nm,MechPower_kW,Efficiency_pct,Voltage_U1_V,Voltage_U2_V,Voltage_U3_V,Current_I1_A,Current_I2_A,Current_I3_A,ElecPower_kW,PowerFactor,T_Ambient_C,T_Coil_C,T_BearingF_C,T_BearingR_C,T_Encoder_C,T_WaterIn_C,T_WaterOut_C");
                    foreach (var row in dutyResults)
                    {
                        sw.WriteLine(string.Join(",", row));
                    }
                }
                PurgeLocalLogs(false);
                WriteHmiLog("DUTY_EXPORT", string.Format("【{0} 報表自動存檔成功】已儲存至: {1} (狀態: {2} | 共 {3} 筆記錄)", dutyPrefix, path, statusTag, dutyResults.Count));
                if (showDialog)
                {
                    MessageBox.Show(string.Format("{0} 工作制報表已匯出至:\n{1}", dutyPrefix, path), "匯出成功", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                return path;
            }
            catch (Exception ex)
            {
                WriteHmiLog("DUTY_ERROR", string.Format("【{0} 自動存檔失敗】: {1}", dutyPrefix, ex.Message));
                if (showDialog)
                {
                    MessageBox.Show("匯出失敗: " + ex.Message, "錯誤");
                }
                return null;
            }
        }

        private void BtnExportDuty_Click(object sender, EventArgs e)
        {
            int modeIdx = (cmbDutyMode != null && cmbDutyMode.SelectedIndex >= 0) ? cmbDutyMode.SelectedIndex : 0;
            string prefix = (modeIdx == 0) ? "S1" : ((modeIdx == 1) ? "S2" : "S6");
            SaveDutyTestReportCsv(prefix, showDialog: true);
        }

        private double GetNamedTempChannel(string keyword, int fallbackChannelIndex, double defaultVal = 0.0)
        {
            if (gl820ChannelNames != null && gbdChTemps != null)
            {
                for (int i = 0; i < gl820ChannelNames.Length && i < gbdChTemps.Length && i < 20; i++)
                {
                    string name = gl820ChannelNames[i];
                    if (string.IsNullOrEmpty(name)) continue;
                    string clean = name.ToUpper().Trim().Replace("-", "").Replace("_", "").Replace(" ", "");
                    if (clean.Contains(keyword.ToUpper()))
                    {
                        double t = gbdChTemps[i];
                        if (t > -40.0 && t < 400.0 && Math.Abs(t) > 0.01) return t;
                    }
                }
            }
            if (gbdChTemps != null && fallbackChannelIndex >= 0 && fallbackChannelIndex < gbdChTemps.Length)
            {
                double t = gbdChTemps[fallbackChannelIndex];
                if (t > -40.0 && t < 400.0 && Math.Abs(t) > 0.01) return t;
            }
            return defaultVal;
        }

        private bool HasWaterCoolingChannels()
        {
            if (gl820ChannelNames == null) return false;
            for (int i = 0; i < gl820ChannelNames.Length && i < 20; i++)
            {
                string name = gl820ChannelNames[i];
                if (string.IsNullOrEmpty(name)) continue;
                string clean = name.ToUpper().Replace("-", "").Replace("_", "").Replace(" ", "");
                if (clean.Contains("WIN") || clean.Contains("WOUT")) return true;
            }
            return false;
        }

        private int FindAmbientTempChannelIndex()
        {
            if (gl820ChannelNames == null) return -1;
            for (int i = 0; i < gl820ChannelNames.Length && i < 20; i++)
            {
                string name = gl820ChannelNames[i];
                if (string.IsNullOrEmpty(name)) continue;
                string clean = name.ToUpper().Trim();
                if (clean == "AT" || clean.StartsWith("AT") || clean.Contains("AMBIENT") || clean.Contains("室溫"))
                {
                    return i;
                }
            }
            return -1;
        }

        private int GetBatchDutyPlan()
        {
            if (cmbBatchDutyPlan != null && cmbBatchDutyPlan.SelectedIndex >= 0)
                return cmbBatchDutyPlan.SelectedIndex;
            return 0; // 0: S1->S6->S2, 1: S1->S6, 2: S1->S2, 3: S6->S2
        }

        private double GetCurrentPeakTemp()
        {
            double maxT = -999.0;
            if (gbdChTemps != null)
            {
                for (int i = 0; i < gbdChTemps.Length && i < 20; i++)
                {
                    double t = gbdChTemps[i];
                    if (t > -40.0 && t < 400.0 && Math.Abs(t) > 0.01 && t > maxT)
                        maxT = t;
                }
            }
            if (maxT < -100.0) maxT = actTemp;
            return (maxT > -100.0) ? maxT : 25.0;
        }

        private void AdvanceBatchAfterS1()
        {
            int plan = GetBatchDutyPlan();
            double curTemp = GetCurrentPeakTemp();
            batchCoolingElapsedSec = 0;
            batchCoolingStableSec = 0;
            batchCoolingPrevTemp = 999.0;
            batchCoolingCheckTimer = 0;
            btnStopDuty.Enabled = true;
            dutyResults.Clear();
            dutyMinuteSampleCount = 0;

            if (plan == 2) // S1 -> S2 (兩項): 跳過 50℃，直接冷卻至 25℃/室溫 (Stage 4)
            {
                batchDutyStage = 4;
                if (prgBatchTotal != null) prgBatchTotal.Value = 2;
                if (lblBatchProgressDetail != null)
                {
                    lblBatchProgressDetail.Text = string.Format("【連續排程 2/3】S1 完成已自動存檔！冷卻等待中 (最高溫: {0:F1}℃ -> 目標待定，已冷卻 00:00，準備 S2 冷態測試)", curTemp);
                }
            }
            else // plan 0 (S1->S6->S2) 或 plan 1 (S1->S6): 冷卻至 <= 50℃ (Stage 2)
            {
                batchDutyStage = 2;
                int maxSteps = (plan == 0) ? 5 : 3;
                if (prgBatchTotal != null) prgBatchTotal.Value = 2;
                if (lblBatchProgressDetail != null)
                {
                    lblBatchProgressDetail.Text = string.Format("【連續排程 2/{0}】S1 完成已自動存檔！冷卻等待中 (最高溫: {1:F1}℃ -> 目標 <=50.0℃，已冷卻 00:00)", maxSteps, curTemp);
                }
            }
            if (dutyTimer != null && !dutyTimer.Enabled) dutyTimer.Start();
        }

        private void AdvanceBatchAfterS6()
        {
            int plan = GetBatchDutyPlan();
            double curTemp = GetCurrentPeakTemp();
            if (plan == 1) // S1 -> S6 (兩項): S6 結束即全排程圓滿完成！
            {
                isBatchDutyActive = false;
                batchDutyStage = 6;
                if (prgBatchTotal != null) prgBatchTotal.Value = 3;
                ResetDutyTestToStandby();
                if (lblBatchProgressDetail != null)
                {
                    lblBatchProgressDetail.Text = "【連續排程圓滿結束】S1、S6 兩項工作制實驗全部測試完成！報表已全數自動存檔，可點擊「匯出報表」查看。";
                }
                WriteHmiLog("BATCH_COMPLETE", "【連續排程全勝】S1 -> S6 連續性全自動工作制測試全數完成！");
                return;
            }

            // plan 0 (S1->S6->S2) 或 plan 3 (S6->S2): S6 結束後需冷卻至 25℃/室溫，接續 S2
            batchDutyStage = 4;
            batchCoolingElapsedSec = 0;
            batchCoolingStableSec = 0;
            batchCoolingPrevTemp = 999.0;
            batchCoolingCheckTimer = 0;
            int maxSteps = (plan == 0) ? 5 : 3;
            int stepIdx = (plan == 0) ? 4 : 2;
            if (prgBatchTotal != null) prgBatchTotal.Value = stepIdx;
            btnStopDuty.Enabled = true;
            if (lblBatchProgressDetail != null)
            {
                lblBatchProgressDetail.Text = string.Format("【連續排程 {0}/{1}】S6 完成已自動存檔！冷卻等待中 (最高溫: {2:F1}℃ -> 目標待定，已冷卻 00:00)", stepIdx, maxSteps, curTemp);
            }
            dutyResults.Clear();
            dutyMinuteSampleCount = 0;
            if (dutyTimer != null && !dutyTimer.Enabled) dutyTimer.Start();
        }

        private void AbortBatchDuty(string reason)
        {
            isBatchDutyActive = false;
            batchDutyStage = 0;
            ResetDutyTestToStandby();
            lblDutyStatus.Text = "狀態: 連續排程已中止";
            lblDutyPhaseAction.Text = "動作: " + reason;
            if (lblBatchProgressDetail != null)
            {
                lblBatchProgressDetail.Text = "【連續排程已中止】" + reason;
            }
            WriteHmiLog("BATCH_ABORT", "【連續排程中止警告】" + reason);
            int dutyRole = (cmbDutyRole != null && cmbDutyRole.SelectedIndex >= 0) ? cmbDutyRole.SelectedIndex : 1;
            int spdDrive = (dutyRole == 1) ? 2 : 1;
            int trqDrive = (spdDrive == 1) ? 2 : 1;
            StartGradualAutoStop(spdDrive, trqDrive, "連續排程中止: " + reason, null);
            MessageBox.Show(reason, "連續排程中止", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        private void ProcessBatchDutyCooling()
        {
            batchCoolingElapsedSec++;
            int m = batchCoolingElapsedSec / 60;
            int s = batchCoolingElapsedSec % 60;
            string timeStr = string.Format("{0:D2}:{1:D2}", m, s);

            // 1. 取得當前實測最高溫 (優先取被監控通道，若未配置則取全通道最大有效溫度)
            double curMaxTemp = -999.0;
            int maxCh = -1;
            if (s1MonitoredChannels != null)
            {
                for (int i = 0; i < s1MonitoredChannels.Length && i < 20; i++)
                {
                    if (s1MonitoredChannels[i] && gbdChTemps != null && i < gbdChTemps.Length)
                    {
                        double t = gbdChTemps[i];
                        if (t > -40.0 && t < 400.0 && Math.Abs(t) > 0.01)
                        {
                            if (t > curMaxTemp) { curMaxTemp = t; maxCh = i; }
                        }
                    }
                }
            }
            if (curMaxTemp < -100.0 && gbdChTemps != null)
            {
                for (int i = 0; i < gbdChTemps.Length && i < 20; i++)
                {
                    double t = gbdChTemps[i];
                    if (t > -40.0 && t < 400.0 && Math.Abs(t) > 0.01)
                    {
                        if (t > curMaxTemp) { curMaxTemp = t; maxCh = i; }
                    }
                }
            }
            if (curMaxTemp < -100.0) curMaxTemp = 25.0; // 兜底

            // ── 階段 2：S1 完成後，等待冷卻至 <= 50.0℃ ──
            if (batchDutyStage == 2)
            {
                int plan = GetBatchDutyPlan();
                int maxSteps = (plan == 0) ? 5 : 3;
                lblDutyStatus.Text = string.Format("[排程 2/{0}] S1 完成！等待降溫至 <= 50.0℃ (已冷卻 {1})", maxSteps, timeStr);
                lblDutyPhaseAction.Text = string.Format("目前最高溫 CH{0}: {1:F1}℃ -> 目標 <= 50.0℃", maxCh + 1, curMaxTemp);
                if (lblBatchProgressDetail != null)
                {
                    lblBatchProgressDetail.Text = string.Format("【連續排程 2/{0}】S1 完成已自動存檔！冷卻等待中 (最高溫 CH{1}: {2:F1}℃ -> 目標 <=50.0℃，已冷卻 {3})", maxSteps, maxCh + 1, curMaxTemp, timeStr);
                }

                if (curMaxTemp <= 50.0)
                {
                    WriteHmiLog("BATCH_DUTY", string.Format("【連續排程 S1 冷卻達標】最高溫 CH{0} 已降至 {1:F1}℃ (<= 50.0℃，耗時 {2})，自動啟動 S6 週期測試！", maxCh + 1, curMaxTemp, timeStr));
                    batchDutyStage = 3;
                    int nextStep = (plan == 0) ? 3 : 3;
                    if (prgBatchTotal != null) prgBatchTotal.Value = nextStep;
                    cmbDutyMode.SelectedIndex = 2; // 自動切換至 S6
                    StartDutyTestInternal(fromMini: false, isBatchTrigger: true);
                }
            }
            // ── 階段 4：冷卻至 25.0℃ 或 室溫 (AT) ──
            else if (batchDutyStage == 4)
            {
                int plan = GetBatchDutyPlan();
                int maxSteps = (plan == 0) ? 5 : 3;
                int stepIdx = (plan == 0) ? 4 : 2;
                string prevTestName = (plan == 2) ? "S1" : "S6";
                int nextStep = (plan == 0) ? 5 : 3;

                bool isWaterCooled = false;
                if (cmbBatchCoolingMode != null && cmbBatchCoolingMode.SelectedIndex == 1) isWaterCooled = true;
                else if (cmbBatchCoolingMode != null && cmbBatchCoolingMode.SelectedIndex == 2) isWaterCooled = false;
                else isWaterCooled = HasWaterCoolingChannels();

                if (isWaterCooled)
                {
                    lblDutyStatus.Text = string.Format("[排程 {0}/{1}] {2} 完成！水冷冰水機降溫中 (已冷卻 {3})", stepIdx, maxSteps, prevTestName, timeStr);
                    lblDutyPhaseAction.Text = string.Format("水冷最高溫 CH{0}: {1:F1}℃ -> 目標 <= 25.0℃", maxCh + 1, curMaxTemp);
                    if (lblBatchProgressDetail != null)
                    {
                        lblBatchProgressDetail.Text = string.Format("【連續排程 {0}/{1} (水冷)】{2} 完成已自動存檔！冰水機冷卻中 (最高溫: {3:F1}℃ -> 目標 <=25.0℃，已冷卻 {4})", stepIdx, maxSteps, prevTestName, curMaxTemp, timeStr);
                    }

                    if (curMaxTemp <= 25.0)
                    {
                        WriteHmiLog("BATCH_DUTY", string.Format("【連續排程 {0} 水冷冷卻達標】最高溫已降至 {1:F1}℃ (<= 25.0℃，耗時 {2})，自動啟動 S2 短時測試！", prevTestName, curMaxTemp, timeStr));
                        batchDutyStage = 5;
                        if (prgBatchTotal != null) prgBatchTotal.Value = nextStep;
                        cmbDutyMode.SelectedIndex = 1; // 自動切換至 S2
                        StartDutyTestInternal(fromMini: false, isBatchTrigger: true);
                    }
                }
                else
                {
                    int atIdx = FindAmbientTempChannelIndex();
                    double atTemp = (atIdx >= 0 && gbdChTemps != null && atIdx < gbdChTemps.Length) ? gbdChTemps[atIdx] : -999.0;

                    // 依使用者指示：室溫斷線時終止實驗！
                    if (atIdx < 0 || atTemp <= 0.0 || atTemp >= 80.0)
                    {
                        AbortBatchDuty(string.Format("氣冷室溫通道 (AT) 斷線或未檢測到有效數值 (實測: {0:F1}℃)，依安全規則終止連續實驗！", atTemp));
                        return;
                    }

                    double targetThresh = atTemp + 1.5;

                    batchCoolingCheckTimer++;
                    if (batchCoolingCheckTimer >= 60)
                    {
                        batchCoolingCheckTimer = 0;
                        if (batchCoolingPrevTemp < 900.0 && (batchCoolingPrevTemp - curMaxTemp) >= 0.2)
                        {
                            batchCoolingStableSec = 0;
                        }
                        batchCoolingPrevTemp = curMaxTemp;
                    }

                    string statusDetail = "";
                    if (curMaxTemp <= atTemp)
                    {
                        WriteHmiLog("BATCH_DUTY", string.Format("【連續排程 {0} 氣冷冷卻達標】最高溫 {1:F1}℃ 已完全降至室溫 AT ({2:F1}℃)，耗時 {3}，自動啟動 S2 短時測試！", prevTestName, curMaxTemp, atTemp, timeStr));
                        batchDutyStage = 5;
                        if (prgBatchTotal != null) prgBatchTotal.Value = nextStep;
                        cmbDutyMode.SelectedIndex = 1; // 自動切換至 S2
                        StartDutyTestInternal(fromMini: false, isBatchTrigger: true);
                        return;
                    }
                    else if (curMaxTemp <= targetThresh)
                    {
                        batchCoolingStableSec++;
                        int remainStableSec = Math.Max(0, 900 - batchCoolingStableSec);
                        statusDetail = string.Format("接近室溫 (溫差 {0:F2}℃ <= 1.5℃)，穩定維持中 (剩餘 {1}分{2}秒)", curMaxTemp - atTemp, remainStableSec / 60, remainStableSec % 60);

                        if (batchCoolingStableSec >= 900)
                        {
                            WriteHmiLog("BATCH_DUTY", string.Format("【連續排程 {0} 氣冷冷卻放行】最高溫 {1:F1}℃ 已接近室溫 AT ({2:F1}℃，溫差 {3:F2}℃ <= 1.5℃) 且持續 15 分鐘無持續下降，依規則放行！自動啟動 S2 短時測試！", prevTestName, curMaxTemp, atTemp, curMaxTemp - atTemp));
                            batchDutyStage = 5;
                            if (prgBatchTotal != null) prgBatchTotal.Value = nextStep;
                            cmbDutyMode.SelectedIndex = 1; // 自動切換至 S2
                            StartDutyTestInternal(fromMini: false, isBatchTrigger: true);
                            return;
                        }
                    }
                    else
                    {
                        batchCoolingStableSec = 0;
                        statusDetail = string.Format("自然/風扇降溫中 (溫差 {0:F1}℃，目標 <= {1:F1}℃)", curMaxTemp - atTemp, targetThresh);
                    }

                    lblDutyStatus.Text = string.Format("[排程 {0}/{1}] {2} 完成！氣冷降溫中 (已冷卻 {3})", stepIdx, maxSteps, prevTestName, timeStr);
                    lblDutyPhaseAction.Text = string.Format("最高溫: {0:F1}℃ | 室溫 AT: {1:F1}℃ | {2}", curMaxTemp, atTemp, statusDetail);
                    if (lblBatchProgressDetail != null)
                    {
                        lblBatchProgressDetail.Text = string.Format("【連續排程 {0}/{1} (氣冷)】最高溫: {2:F1}℃ | AT室溫: {3:F1}℃ | {4} | 已冷卻 {5}", stepIdx, maxSteps, curMaxTemp, atTemp, statusDetail, timeStr);
                    }
                }
            }
        }

        // =========================================================================
        // S2 短時工作制極限轉矩 / S6 週期工作制極限 DUTY 回推估算核心邏輯
        // (依據 IEEE Std 112 / IEC 60034 熱力學與發熱損耗模型)
        // =========================================================================

        private void UpdateDynamicExtrapolationS2(double baseTrq, double targetDurMin, double estFinalTemp, double slope, double actSpd, int elapsedSec)
        {
            if (lblExtrapolationSummary == null) return;
            if (elapsedSec < 30)
            {
                lblExtrapolationSummary.Text = string.Format("【S2 即時動態推估】溫升取樣累積中 ({0}s/30s)... 穩定後啟動動態極限預測", elapsedSec);
                lblExtrapolationSummary.ForeColor = Color.FromArgb(67, 56, 202);
                return;
            }
            int atIdx = FindAmbientTempChannelIndex();
            double amb = (atIdx >= 0 && gbdChTemps != null && atIdx < gbdChTemps.Length && gbdChTemps[atIdx] > 0) ? gbdChTemps[atIdx] : 25.0;
            double spd = (actSpd > 10.0) ? actSpd : (numDutySpeed != null ? (double)numDutySpeed.Value : 1500.0);
            double trq = (baseTrq > 0.1) ? baseTrq : 120.0;

            var res = CalcExtrapolatedS2Torque(trq, targetDurMin, estFinalTemp, amb, 105.0, spd);

            if (estFinalTemp <= 105.0)
            {
                double margin = 105.0 - estFinalTemp;
                lblExtrapolationSummary.Text = string.Format("【S2 即時動態推估】預估30分達 {0:F1}℃ (裕度+{1:F1}K) -> [建議上修] 目標105℃極限扭矩: {2:F1} Nm (+{3:F1}%) | 功率: {4:F2} kW (斜率 {5:F2} K/m)",
                    estFinalTemp, margin, res.ExtrapolatedTorque, res.TorqueIncreasePct, res.ExtrapolatedMechPowerKw, slope);
                lblExtrapolationSummary.ForeColor = Color.FromArgb(30, 64, 175);
            }
            else
            {
                double over = estFinalTemp - 105.0;
                lblExtrapolationSummary.Text = string.Format("【S2 即時動態推估】預估30分達 {0:F1}℃ (超溫-{1:F1}K) -> [必須下修] 為符合105℃限制，扭矩應調降至: {2:F1} Nm ({3:F1}%) | 功率: {4:F2} kW (斜率 {5:F2} K/m)",
                    estFinalTemp, over, res.ExtrapolatedTorque, res.TorqueIncreasePct, res.ExtrapolatedMechPowerKw, slope);
                lblExtrapolationSummary.ForeColor = Color.FromArgb(220, 38, 38);
            }
        }

        private void UpdateDynamicExtrapolationS6(double baseTrq, double cycleMin, double edVal, double curPeakTemp, int curCycle, int totalCycles)
        {
            if (lblExtrapolationSummary == null) return;
            if (curPeakTemp <= 20.0)
            {
                lblExtrapolationSummary.Text = string.Format("【S6 即時動態推估 第{0}/{1}週期】週期數據採樣中... 偵測到有效溫升後即時動態推估", curCycle, totalCycles);
                lblExtrapolationSummary.ForeColor = Color.FromArgb(67, 56, 202);
                return;
            }
            int atIdx = FindAmbientTempChannelIndex();
            double amb = (atIdx >= 0 && gbdChTemps != null && atIdx < gbdChTemps.Length && gbdChTemps[atIdx] > 0) ? gbdChTemps[atIdx] : 25.0;
            double trq = (baseTrq > 0.1) ? baseTrq : 120.0;

            var res = CalcExtrapolatedS6Ed(trq, cycleMin, edVal, curPeakTemp, amb, 105.0);

            if (curPeakTemp <= 105.0)
            {
                double margin = 105.0 - curPeakTemp;
                lblExtrapolationSummary.Text = string.Format("【S6 即時動態推估 第{0}/{1}週期】週期最高 {2:F1}℃ (裕度+{3:F1}K) -> [可上修] 目標105℃最高 DUTY: {4:F1}% (+{5:F1}%) | 週期分配: 有載 T1={6:F1}m, 空載 T2={7:F1}m",
                    curCycle, totalCycles, curPeakTemp, margin, res.ExtrapolatedEdPct, res.EdIncreasePct, res.ExtrapolatedT1Min, res.ExtrapolatedT2Min);
                lblExtrapolationSummary.ForeColor = Color.FromArgb(30, 64, 175);
            }
            else
            {
                double over = curPeakTemp - 105.0;
                lblExtrapolationSummary.Text = string.Format("【S6 即時動態推估 第{0}/{1}週期】週期最高 {2:F1}℃ (超溫-{3:F1}K) -> [需下修] 為防過熱 DUTY 應縮減至: {4:F1}% ({5:F1}%) | 週期分配: 有載 T1={6:F1}m, 空載 T2={7:F1}m",
                    curCycle, totalCycles, curPeakTemp, over, res.ExtrapolatedEdPct, res.EdIncreasePct, res.ExtrapolatedT1Min, res.ExtrapolatedT2Min);
                lblExtrapolationSummary.ForeColor = Color.FromArgb(220, 38, 38);
            }
        }

        private void RecordAndExtrapolateS2()
        {
            lastS2Torque = (numDutyTorque != null && numDutyTorque.Value > 0) ? (double)numDutyTorque.Value : (Math.Abs(actTorque) > 0.1 ? Math.Abs(actTorque) : 120.0);
            lastS2DurationMin = (numS2DurationMin != null) ? (double)numS2DurationMin.Value : 30.0;
            lastS2FinalTemp = GetCurrentPeakTemp();
            int atIdx = FindAmbientTempChannelIndex();
            lastS2AmbientTemp = (atIdx >= 0 && gbdChTemps != null && atIdx < gbdChTemps.Length && gbdChTemps[atIdx] > 0) ? gbdChTemps[atIdx] : 25.0;
            lastS2Speed = (Math.Abs(actSpeed) > 10.0) ? Math.Abs(actSpeed) : (numDutySpeed != null ? (double)numDutySpeed.Value : 1500.0);
            hasS2TestResult = true;

            var s2Ext = CalcExtrapolatedS2Torque(lastS2Torque, lastS2DurationMin, lastS2FinalTemp, lastS2AmbientTemp, 105.0, lastS2Speed);
            string actionTag = (lastS2FinalTemp <= 105.0) ? "[建議上修]" : "[必須下修]";
            string s2ExtSummary = string.Format("【S2 正式結算推估】實測 {0:F1}Nm ({1:F0}分, {2:F1}℃) -> {3} 目標 105℃ 極限扭矩: {4:F1} Nm ({5:+0.0;-0.0}%)，輸出功率: {6:F2} kW (斜率 {7:F2} K/m)",
                lastS2Torque, lastS2DurationMin, lastS2FinalTemp, actionTag, s2Ext.ExtrapolatedTorque, s2Ext.TorqueIncreasePct, s2Ext.ExtrapolatedMechPowerKw, s2Ext.TempRiseSlope);
            if (lblExtrapolationSummary != null)
            {
                lblExtrapolationSummary.Text = s2ExtSummary;
                lblExtrapolationSummary.ForeColor = (lastS2FinalTemp <= 105.0) ? Color.FromArgb(30, 64, 175) : Color.FromArgb(220, 38, 38);
            }
            WriteHmiLog("S2_EXTRAPOLATE", s2ExtSummary);
        }

        private void RecordAndExtrapolateS6(double peakTemp)
        {
            lastS6Torque = (numDutyTorque != null && numDutyTorque.Value > 0) ? (double)numDutyTorque.Value : (Math.Abs(actTorque) > 0.1 ? Math.Abs(actTorque) : 120.0);
            lastS6CycleMin = (numS6CycleMin != null) ? (double)numS6CycleMin.Value : 10.0;
            lastS6EdPct = (numS6Ed != null) ? (double)numS6Ed.Value : 40.0;
            lastS6PeakTemp = (peakTemp > -100.0) ? peakTemp : GetCurrentPeakTemp();
            int atIdx = FindAmbientTempChannelIndex();
            lastS6AmbientTemp = (atIdx >= 0 && gbdChTemps != null && atIdx < gbdChTemps.Length && gbdChTemps[atIdx] > 0) ? gbdChTemps[atIdx] : 25.0;
            lastS6Speed = (Math.Abs(actSpeed) > 10.0) ? Math.Abs(actSpeed) : (numDutySpeed != null ? (double)numDutySpeed.Value : 1500.0);
            hasS6TestResult = true;

            var s6Ext = CalcExtrapolatedS6Ed(lastS6Torque, lastS6CycleMin, lastS6EdPct, lastS6PeakTemp, lastS6AmbientTemp, 105.0);
            string actionTag = (lastS6PeakTemp <= 105.0) ? "[可上修]" : "[需下修]";
            string s6ExtSummary = string.Format("【S6 正式結算推估】實測 ED {0:F0}% ({1:F1}℃) -> {2} 目標 105℃ 容許最高 DUTY: {3:F1}% ({4:+0.0;-0.0}%) (有載T1={5:F1}m, 空載T2={6:F1}m)",
                lastS6EdPct, lastS6PeakTemp, actionTag, s6Ext.ExtrapolatedEdPct, s6Ext.EdIncreasePct, s6Ext.ExtrapolatedT1Min, s6Ext.ExtrapolatedT2Min);
            if (lblExtrapolationSummary != null)
            {
                lblExtrapolationSummary.Text = s6ExtSummary;
                lblExtrapolationSummary.ForeColor = (lastS6PeakTemp <= 105.0) ? Color.FromArgb(30, 64, 175) : Color.FromArgb(220, 38, 38);
            }
            WriteHmiLog("S6_EXTRAPOLATE", s6ExtSummary);
        }

        public static S2ExtrapolationResult CalcExtrapolatedS2Torque(double baseTrq, double durationMin, double finalTemp, double ambientTemp, double targetTemp, double ratedSpeedRpm)
        {
            S2ExtrapolationResult res = new S2ExtrapolationResult();
            res.BaseTorque = baseTrq;
            res.DurationMin = durationMin;
            res.FinalTemp = finalTemp;
            res.AmbientTemp = ambientTemp;
            res.TargetTemp = targetTemp;
            res.RatedSpeed = ratedSpeedRpm;

            double deltaT_base = Math.Max(1.0, finalTemp - ambientTemp);
            double deltaT_target = Math.Max(1.0, targetTemp - ambientTemp);

            res.TempRiseSlope = (durationMin > 0) ? (deltaT_base / durationMin) : 0.0;
            double trqRatio = Math.Sqrt(deltaT_target / deltaT_base);
            res.ExtrapolatedTorque = Math.Round(baseTrq * trqRatio, 1);
            res.TorqueIncreasePct = Math.Round((trqRatio - 1.0) * 100.0, 1);
            res.ExtrapolatedMechPowerKw = Math.Round((ratedSpeedRpm * res.ExtrapolatedTorque) / 9549.0, 2);

            return res;
        }

        public static S6ExtrapolationResult CalcExtrapolatedS6Ed(double baseTrq, double cycleMin, double currentEdPct, double peakTemp, double ambientTemp, double targetTemp)
        {
            S6ExtrapolationResult res = new S6ExtrapolationResult();
            res.BaseTorque = baseTrq;
            res.CycleMin = cycleMin;
            res.CurrentEdPct = currentEdPct;
            res.PeakTemp = peakTemp;
            res.AmbientTemp = ambientTemp;
            res.TargetTemp = targetTemp;

            double deltaT_base = Math.Max(1.0, peakTemp - ambientTemp);
            double deltaT_target = Math.Max(1.0, targetTemp - ambientTemp);

            double edRatio = deltaT_target / deltaT_base;
            double rawEd = currentEdPct * edRatio;
            res.ExtrapolatedEdPct = Math.Round(Math.Min(100.0, rawEd), 1);
            res.EdIncreasePct = Math.Round(res.ExtrapolatedEdPct - currentEdPct, 1);
            res.ExtrapolatedT1Min = Math.Round(cycleMin * (res.ExtrapolatedEdPct / 100.0), 2);
            res.ExtrapolatedT2Min = Math.Round(cycleMin - res.ExtrapolatedT1Min, 2);

            return res;
        }

        private void ShowDutyExtrapolationDialog()
        {
            try
            {
                Form dlg = new Form()
                {
                    Text = "馬達工作制極限能力回推估算器 (S2極限轉矩 / S6極限DUTY) - IEEE Std 112 / IEC 60034",
                    Size = new Size(820, 680),
                    StartPosition = FormStartPosition.CenterParent,
                    FormBorderStyle = FormBorderStyle.FixedDialog,
                    MaximizeBox = false,
                    MinimizeBox = false,
                    BackColor = Color.FromArgb(248, 250, 252),
                    Font = new Font("微軟正黑體", 10f)
                };

                TabControl tabs = new TabControl() { Dock = DockStyle.Fill, Font = new Font("微軟正黑體", 11f, FontStyle.Bold) };
                TabPage tpS2 = new TabPage(" [S2] 30min 極限轉矩回推 (105℃/自訂) ");
                TabPage tpS6 = new TabPage(" [S6] 週期 DUTY 佔比回推 (105℃/自訂) ");
                tpS2.BackColor = Color.White;
                tpS6.BackColor = Color.White;

                BuildS2ExtrapolationTab(tpS2);
                BuildS6ExtrapolationTab(tpS6);

                tabs.TabPages.Add(tpS2);
                tabs.TabPages.Add(tpS6);
                dlg.Controls.Add(tabs);
                dlg.ShowDialog(this);
            }
            catch (Exception ex)
            {
                MessageBox.Show("開啟能力回推估算器失敗: " + ex.Message, "錯誤", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void BuildS2ExtrapolationTab(TabPage tp)
        {
            TableLayoutPanel root = new TableLayoutPanel() { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Padding = new Padding(10) };
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 68f));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

            Panel pnlHeader = new Panel() { Dock = DockStyle.Fill, BackColor = Color.FromArgb(238, 242, 255), Padding = new Padding(8) };
            Label lHeadTitle = new Label() { Text = "[S2 短時工作制極限輸出轉矩回推估算] (IEEE Std 112 / IEC 60034)", Font = new Font("微軟正黑體", 11.5f, FontStyle.Bold), ForeColor = Color.FromArgb(49, 46, 129), Dock = DockStyle.Top, Height = 26 };
            Label lHeadDesc = new Label() { Text = "短時溫升與銅損平方律成正比 (ΔT ∝ T^2)。利用已測轉矩、運轉時長與實測溫升斜率，快速推算目標極限溫度 (如 105℃) 下可承受之最高極限輸出轉矩。", Font = new Font("微軟正黑體", 9.5f), ForeColor = Color.FromArgb(71, 85, 105), Dock = DockStyle.Fill };
            pnlHeader.Controls.Add(lHeadDesc);
            pnlHeader.Controls.Add(lHeadTitle);
            root.Controls.Add(pnlHeader, 0, 0);

            TableLayoutPanel body = new TableLayoutPanel() { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = new Padding(0, 8, 0, 0) };
            body.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 370f));
            body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

            Panel pnlLeft = new Panel() { Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(0, 0, 8, 0) };
            GroupBox grpBase = new GroupBox() { Text = "基準實測數據 (可手動輸入或一鍵載入)", Dock = DockStyle.Top, Height = 250, Font = new Font("微軟正黑體", 10.5f, FontStyle.Bold) };
            TableLayoutPanel tblIn = new TableLayoutPanel() { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 6, Padding = new Padding(6) };
            tblIn.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 160f));
            tblIn.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

            NumericUpDown numS2BaseTrq = new NumericUpDown() { Minimum = 0, Maximum = 10000, DecimalPlaces = 1, Value = (decimal)(lastS2Torque > 0.1 ? lastS2Torque : 120.0), Dock = DockStyle.Fill };
            NumericUpDown numS2Dur = new NumericUpDown() { Minimum = 1, Maximum = 600, DecimalPlaces = 0, Value = (decimal)(lastS2DurationMin > 0.1 ? lastS2DurationMin : 30.0), Dock = DockStyle.Fill };
            NumericUpDown numS2Temp = new NumericUpDown() { Minimum = 0, Maximum = 350, DecimalPlaces = 1, Value = (decimal)(lastS2FinalTemp > 0.1 ? lastS2FinalTemp : 90.0), Dock = DockStyle.Fill };
            NumericUpDown numS2Amb = new NumericUpDown() { Minimum = -20, Maximum = 100, DecimalPlaces = 1, Value = (decimal)(lastS2AmbientTemp > 0.1 ? lastS2AmbientTemp : 25.0), Dock = DockStyle.Fill };
            NumericUpDown numS2Spd = new NumericUpDown() { Minimum = 1, Maximum = 30000, DecimalPlaces = 0, Value = (decimal)(lastS2Speed > 0.1 ? lastS2Speed : 1500.0), Dock = DockStyle.Fill };
            Button btnLoadRecent = new Button() { Text = "[載入最近實測值]", Dock = DockStyle.Fill, BackColor = Color.FromArgb(224, 231, 255), Font = new Font("微軟正黑體", 9.5f, FontStyle.Bold) };

            tblIn.Controls.Add(new Label() { Text = "基準加載轉矩 (Nm):", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, 0);
            tblIn.Controls.Add(numS2BaseTrq, 1, 0);
            tblIn.Controls.Add(new Label() { Text = "基準運轉時間 (min):", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, 1);
            tblIn.Controls.Add(numS2Dur, 1, 1);
            tblIn.Controls.Add(new Label() { Text = "實測最高溫度 (℃):", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, 2);
            tblIn.Controls.Add(numS2Temp, 1, 2);
            tblIn.Controls.Add(new Label() { Text = "實測環境室溫 (℃):", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, 3);
            tblIn.Controls.Add(numS2Amb, 1, 3);
            tblIn.Controls.Add(new Label() { Text = "額定運轉轉速 (rpm):", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, 4);
            tblIn.Controls.Add(numS2Spd, 1, 4);
            tblIn.Controls.Add(btnLoadRecent, 1, 5);
            grpBase.Controls.Add(tblIn);

            GroupBox grpTarget = new GroupBox() { Text = "目標極限溫度設定 (℃)", Dock = DockStyle.Top, Height = 140, Font = new Font("微軟正黑體", 10.5f, FontStyle.Bold), Margin = new Padding(0, 10, 0, 0) };
            TableLayoutPanel tblTgt = new TableLayoutPanel() { Dock = DockStyle.Fill, ColumnCount = 4, RowCount = 2, Padding = new Padding(6) };
            NumericUpDown numS2TgtTemp = new NumericUpDown() { Minimum = 40, Maximum = 300, DecimalPlaces = 1, Value = 105.0m, Dock = DockStyle.Fill };
            Button b105 = new Button() { Text = "105℃", Dock = DockStyle.Fill, BackColor = Color.FromArgb(241, 245, 249) };
            Button b120 = new Button() { Text = "120℃", Dock = DockStyle.Fill, BackColor = Color.FromArgb(241, 245, 249) };
            Button b130 = new Button() { Text = "130℃", Dock = DockStyle.Fill, BackColor = Color.FromArgb(241, 245, 249) };
            Button b155 = new Button() { Text = "155℃", Dock = DockStyle.Fill, BackColor = Color.FromArgb(241, 245, 249) };
            b105.Click += (s, e) => { numS2TgtTemp.Value = 105.0m; };
            b120.Click += (s, e) => { numS2TgtTemp.Value = 120.0m; };
            b130.Click += (s, e) => { numS2TgtTemp.Value = 130.0m; };
            b155.Click += (s, e) => { numS2TgtTemp.Value = 155.0m; };

            tblTgt.Controls.Add(new Label() { Text = "自訂目標溫度 (℃):", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, 0);
            tblTgt.SetColumnSpan(tblTgt.Controls[tblTgt.Controls.Count - 1], 2);
            tblTgt.Controls.Add(numS2TgtTemp, 2, 0);
            tblTgt.SetColumnSpan(numS2TgtTemp, 2);
            tblTgt.Controls.Add(b105, 0, 1);
            tblTgt.Controls.Add(b120, 1, 1);
            tblTgt.Controls.Add(b130, 2, 1);
            tblTgt.Controls.Add(b155, 3, 1);
            grpTarget.Controls.Add(tblTgt);

            pnlLeft.Controls.Add(grpTarget);
            pnlLeft.Controls.Add(grpBase);
            body.Controls.Add(pnlLeft, 0, 0);

            GroupBox grpRes = new GroupBox() { Text = "極限能力回推結果 (即時動態計算)", Dock = DockStyle.Fill, Font = new Font("微軟正黑體", 11f, FontStyle.Bold), Padding = new Padding(12) };
            TableLayoutPanel tblRes = new TableLayoutPanel() { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 6 };
            tblRes.RowStyles.Add(new RowStyle(SizeType.Absolute, 36f));
            tblRes.RowStyles.Add(new RowStyle(SizeType.Absolute, 36f));
            tblRes.RowStyles.Add(new RowStyle(SizeType.Absolute, 70f));
            tblRes.RowStyles.Add(new RowStyle(SizeType.Absolute, 55f));
            tblRes.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            tblRes.RowStyles.Add(new RowStyle(SizeType.Absolute, 45f));

            Label lblSlope = new Label() { Text = "--", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, Font = new Font("微軟正黑體", 10.5f, FontStyle.Bold), ForeColor = Color.FromArgb(71, 85, 105) };
            Label lblTargetDelta = new Label() { Text = "--", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, Font = new Font("微軟正黑體", 10.5f, FontStyle.Bold), ForeColor = Color.FromArgb(71, 85, 105) };

            Panel pnlTrqCard = new Panel() { Dock = DockStyle.Fill, BackColor = Color.FromArgb(240, 253, 244), Padding = new Padding(6), BorderStyle = BorderStyle.FixedSingle };
            Label lblTrqResult = new Label() { Text = "推估極限轉矩: -- Nm", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, Font = new Font("微軟正黑體", 16f, FontStyle.Bold), ForeColor = Color.FromArgb(22, 101, 52) };
            pnlTrqCard.Controls.Add(lblTrqResult);

            Panel pnlPwrCard = new Panel() { Dock = DockStyle.Fill, BackColor = Color.FromArgb(240, 249, 255), Padding = new Padding(6), BorderStyle = BorderStyle.FixedSingle, Margin = new Padding(0, 4, 0, 4) };
            Label lblPwrResult = new Label() { Text = "估計機械輸出功率: -- kW", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, Font = new Font("微軟正黑體", 13f, FontStyle.Bold), ForeColor = Color.FromArgb(3, 105, 161) };
            pnlPwrCard.Controls.Add(lblPwrResult);

            TextBox txtFormula = new TextBox() { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, BackColor = Color.FromArgb(248, 250, 252), Font = new Font("Consolas", 9.5f), ScrollBars = ScrollBars.Vertical };
            Button btnCopy = new Button() { Text = "[複製 S2 推估報告文字]", Dock = DockStyle.Fill, BackColor = Color.FromArgb(79, 70, 229), ForeColor = Color.White, Font = new Font("微軟正黑體", 11f, FontStyle.Bold) };

            tblRes.Controls.Add(lblSlope, 0, 0);
            tblRes.Controls.Add(lblTargetDelta, 0, 1);
            tblRes.Controls.Add(pnlTrqCard, 0, 2);
            tblRes.Controls.Add(pnlPwrCard, 0, 3);
            tblRes.Controls.Add(txtFormula, 0, 4);
            tblRes.Controls.Add(btnCopy, 0, 5);
            grpRes.Controls.Add(tblRes);
            body.Controls.Add(grpRes, 1, 0);
            root.Controls.Add(body, 0, 1);
            tp.Controls.Add(root);

            Action updateS2Calc = () => {
                double bTrq = (double)numS2BaseTrq.Value;
                double bDur = (double)numS2Dur.Value;
                double bTemp = (double)numS2Temp.Value;
                double bAmb = (double)numS2Amb.Value;
                double bSpd = (double)numS2Spd.Value;
                double tTgt = (double)numS2TgtTemp.Value;

                var res = CalcExtrapolatedS2Torque(bTrq, bDur, bTemp, bAmb, tTgt, bSpd);
                double dtBase = Math.Max(0.1, bTemp - bAmb);
                double dtTgt = Math.Max(0.1, tTgt - bAmb);

                lblSlope.Text = string.Format("實測溫升: ΔT = {0:F1} K | 平均溫升斜率: {1:F2} K/min (30m累積溫升)", dtBase, res.TempRiseSlope);
                lblTargetDelta.Text = string.Format("目標允許溫升: ΔT_limit = {0:F1} K (目標 {1:F1}℃ - 室溫 {2:F1}℃)", dtTgt, tTgt, bAmb);

                lblTrqResult.Text = string.Format("推估極限轉矩: {0:F1} Nm (+{1:F1}%)", res.ExtrapolatedTorque, res.TorqueIncreasePct);
                lblPwrResult.Text = string.Format("估計機械輸出功率: {0:F2} kW (於 {1:F0} rpm)", res.ExtrapolatedMechPowerKw, bSpd);

                StringBuilder sbF = new StringBuilder();
                sbF.AppendLine("【S2 短時工作制極限轉矩計算公式與依據】");
                sbF.AppendLine("1. 依據標準: IEEE Std 112 / IEC 60034-1 短時發熱模型");
                sbF.AppendLine("2. 物理原理: 短時溫升主要由銅損產生 (P_loss ≈ k * T^2)，溫升與轉矩平方律成正比");
                sbF.AppendLine(string.Format("3. 轉矩換算比: sqrt(ΔT_target / ΔT_base) = sqrt({0:F1} / {1:F1}) = {2:F4}", dtTgt, dtBase, Math.Sqrt(dtTgt / dtBase)));
                sbF.AppendLine(string.Format("4. 極限轉矩: {0:F1} Nm * {1:F4} = {2:F1} Nm", bTrq, Math.Sqrt(dtTgt / dtBase), res.ExtrapolatedTorque));
                sbF.AppendLine(string.Format("5. 轉矩提升裕度: +{0:F1}%, 估計輸出功率: {1:F2} kW", res.TorqueIncreasePct, res.ExtrapolatedMechPowerKw));
                txtFormula.Text = sbF.ToString();
            };

            numS2BaseTrq.ValueChanged += (s, e) => updateS2Calc();
            numS2Dur.ValueChanged += (s, e) => updateS2Calc();
            numS2Temp.ValueChanged += (s, e) => updateS2Calc();
            numS2Amb.ValueChanged += (s, e) => updateS2Calc();
            numS2Spd.ValueChanged += (s, e) => updateS2Calc();
            numS2TgtTemp.ValueChanged += (s, e) => updateS2Calc();

            btnLoadRecent.Click += (s, e) => {
                if (lastS2Torque > 0.1) numS2BaseTrq.Value = (decimal)lastS2Torque;
                if (lastS2DurationMin > 0.1) numS2Dur.Value = (decimal)lastS2DurationMin;
                if (lastS2FinalTemp > 0.1) numS2Temp.Value = (decimal)lastS2FinalTemp;
                if (lastS2AmbientTemp > 0.1) numS2Amb.Value = (decimal)lastS2AmbientTemp;
                if (lastS2Speed > 0.1) numS2Spd.Value = (decimal)lastS2Speed;
                updateS2Calc();
            };

            btnCopy.Click += (s, e) => {
                try
                {
                    Clipboard.SetText(txtFormula.Text);
                    MessageBox.Show("S2 極限能力回推報告已成功複製至剪貼簿！", "複製成功", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                catch { }
            };

            updateS2Calc();
        }

        private void BuildS6ExtrapolationTab(TabPage tp)
        {
            TableLayoutPanel root = new TableLayoutPanel() { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Padding = new Padding(10) };
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 68f));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

            Panel pnlHeader = new Panel() { Dock = DockStyle.Fill, BackColor = Color.FromArgb(245, 243, 255), Padding = new Padding(8) };
            Label lHeadTitle = new Label() { Text = "[S6 週期工作制 DUTY (ED%) 負載時間佔比回推估算] (IEC 60034)", Font = new Font("微軟正黑體", 11.5f, FontStyle.Bold), ForeColor = Color.FromArgb(76, 29, 149), Dock = DockStyle.Top, Height = 26 };
            Label lHeadDesc = new Label() { Text = "週期工作制熱平衡溫升與平均發熱損耗成正比 (ΔT_ss ∝ P_avg ∝ ED%)。利用已測週期負載與平衡最高溫，推算目標極限溫度 (如 105℃) 下可再增加之最大 DUTY 時間佔比。", Font = new Font("微軟正黑體", 9.5f), ForeColor = Color.FromArgb(71, 85, 105), Dock = DockStyle.Fill };
            pnlHeader.Controls.Add(lHeadDesc);
            pnlHeader.Controls.Add(lHeadTitle);
            root.Controls.Add(pnlHeader, 0, 0);

            TableLayoutPanel body = new TableLayoutPanel() { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = new Padding(0, 8, 0, 0) };
            body.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 370f));
            body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

            Panel pnlLeft = new Panel() { Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(0, 0, 8, 0) };
            GroupBox grpBase = new GroupBox() { Text = "基準實測數據 (可手動輸入或一鍵載入)", Dock = DockStyle.Top, Height = 250, Font = new Font("微軟正黑體", 10.5f, FontStyle.Bold) };
            TableLayoutPanel tblIn = new TableLayoutPanel() { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 6, Padding = new Padding(6) };
            tblIn.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 160f));
            tblIn.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

            NumericUpDown numS6BaseTrq = new NumericUpDown() { Minimum = 0, Maximum = 10000, DecimalPlaces = 1, Value = (decimal)(lastS6Torque > 0.1 ? lastS6Torque : 120.0), Dock = DockStyle.Fill };
            NumericUpDown numS6Cycle = new NumericUpDown() { Minimum = 1, Maximum = 120, DecimalPlaces = 1, Value = (decimal)(lastS6CycleMin > 0.1 ? lastS6CycleMin : 10.0), Dock = DockStyle.Fill };
            NumericUpDown numS6BaseEd = new NumericUpDown() { Minimum = 1, Maximum = 99, DecimalPlaces = 1, Value = (decimal)(lastS6EdPct > 0.1 ? lastS6EdPct : 40.0), Dock = DockStyle.Fill };
            NumericUpDown numS6Temp = new NumericUpDown() { Minimum = 0, Maximum = 350, DecimalPlaces = 1, Value = (decimal)(lastS6PeakTemp > 0.1 ? lastS6PeakTemp : 85.0), Dock = DockStyle.Fill };
            NumericUpDown numS6Amb = new NumericUpDown() { Minimum = -20, Maximum = 100, DecimalPlaces = 1, Value = (decimal)(lastS6AmbientTemp > 0.1 ? lastS6AmbientTemp : 25.0), Dock = DockStyle.Fill };
            Button btnLoadRecent = new Button() { Text = "[載入最近實測值]", Dock = DockStyle.Fill, BackColor = Color.FromArgb(237, 233, 254), Font = new Font("微軟正黑體", 9.5f, FontStyle.Bold) };

            tblIn.Controls.Add(new Label() { Text = "週期加載轉矩 (Nm):", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, 0);
            tblIn.Controls.Add(numS6BaseTrq, 1, 0);
            tblIn.Controls.Add(new Label() { Text = "週期總時長 (min):", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, 1);
            tblIn.Controls.Add(numS6Cycle, 1, 1);
            tblIn.Controls.Add(new Label() { Text = "原負載佔比 ED (%):", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, 2);
            tblIn.Controls.Add(numS6BaseEd, 1, 2);
            tblIn.Controls.Add(new Label() { Text = "實測平衡最高溫 (℃):", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, 3);
            tblIn.Controls.Add(numS6Temp, 1, 3);
            tblIn.Controls.Add(new Label() { Text = "實測環境室溫 (℃):", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, 4);
            tblIn.Controls.Add(numS6Amb, 1, 4);
            tblIn.Controls.Add(btnLoadRecent, 1, 5);
            grpBase.Controls.Add(tblIn);

            GroupBox grpTarget = new GroupBox() { Text = "目標極限溫度設定 (℃)", Dock = DockStyle.Top, Height = 140, Font = new Font("微軟正黑體", 10.5f, FontStyle.Bold), Margin = new Padding(0, 10, 0, 0) };
            TableLayoutPanel tblTgt = new TableLayoutPanel() { Dock = DockStyle.Fill, ColumnCount = 4, RowCount = 2, Padding = new Padding(6) };
            NumericUpDown numS6TgtTemp = new NumericUpDown() { Minimum = 40, Maximum = 300, DecimalPlaces = 1, Value = 105.0m, Dock = DockStyle.Fill };
            Button b105 = new Button() { Text = "105℃", Dock = DockStyle.Fill, BackColor = Color.FromArgb(241, 245, 249) };
            Button b120 = new Button() { Text = "120℃", Dock = DockStyle.Fill, BackColor = Color.FromArgb(241, 245, 249) };
            Button b130 = new Button() { Text = "130℃", Dock = DockStyle.Fill, BackColor = Color.FromArgb(241, 245, 249) };
            Button b155 = new Button() { Text = "155℃", Dock = DockStyle.Fill, BackColor = Color.FromArgb(241, 245, 249) };
            b105.Click += (s, e) => { numS6TgtTemp.Value = 105.0m; };
            b120.Click += (s, e) => { numS6TgtTemp.Value = 120.0m; };
            b130.Click += (s, e) => { numS6TgtTemp.Value = 130.0m; };
            b155.Click += (s, e) => { numS6TgtTemp.Value = 155.0m; };

            tblTgt.Controls.Add(new Label() { Text = "自訂目標溫度 (℃):", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, 0);
            tblTgt.SetColumnSpan(tblTgt.Controls[tblTgt.Controls.Count - 1], 2);
            tblTgt.Controls.Add(numS6TgtTemp, 2, 0);
            tblTgt.SetColumnSpan(numS6TgtTemp, 2);
            tblTgt.Controls.Add(b105, 0, 1);
            tblTgt.Controls.Add(b120, 1, 1);
            tblTgt.Controls.Add(b130, 2, 1);
            tblTgt.Controls.Add(b155, 3, 1);
            grpTarget.Controls.Add(tblTgt);

            pnlLeft.Controls.Add(grpTarget);
            pnlLeft.Controls.Add(grpBase);
            body.Controls.Add(pnlLeft, 0, 0);

            GroupBox grpRes = new GroupBox() { Text = "極限能力回推結果 (即時動態計算)", Dock = DockStyle.Fill, Font = new Font("微軟正黑體", 11f, FontStyle.Bold), Padding = new Padding(12) };
            TableLayoutPanel tblRes = new TableLayoutPanel() { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 6 };
            tblRes.RowStyles.Add(new RowStyle(SizeType.Absolute, 36f));
            tblRes.RowStyles.Add(new RowStyle(SizeType.Absolute, 36f));
            tblRes.RowStyles.Add(new RowStyle(SizeType.Absolute, 70f));
            tblRes.RowStyles.Add(new RowStyle(SizeType.Absolute, 55f));
            tblRes.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            tblRes.RowStyles.Add(new RowStyle(SizeType.Absolute, 45f));

            Label lblDeltaT = new Label() { Text = "--", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, Font = new Font("微軟正黑體", 10.5f, FontStyle.Bold), ForeColor = Color.FromArgb(71, 85, 105) };
            Label lblTargetDelta = new Label() { Text = "--", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, Font = new Font("微軟正黑體", 10.5f, FontStyle.Bold), ForeColor = Color.FromArgb(71, 85, 105) };

            Panel pnlEdCard = new Panel() { Dock = DockStyle.Fill, BackColor = Color.FromArgb(240, 253, 244), Padding = new Padding(6), BorderStyle = BorderStyle.FixedSingle };
            Label lblEdResult = new Label() { Text = "推估容許最高 DUTY (ED%): -- %", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, Font = new Font("微軟正黑體", 16f, FontStyle.Bold), ForeColor = Color.FromArgb(22, 101, 52) };
            pnlEdCard.Controls.Add(lblEdResult);

            Panel pnlTimeCard = new Panel() { Dock = DockStyle.Fill, BackColor = Color.FromArgb(245, 243, 255), Padding = new Padding(6), BorderStyle = BorderStyle.FixedSingle, Margin = new Padding(0, 4, 0, 4) };
            Label lblTimeResult = new Label() { Text = "週期時間重配: T1=--m, T2=--m", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, Font = new Font("微軟正黑體", 12.5f, FontStyle.Bold), ForeColor = Color.FromArgb(109, 40, 217) };
            pnlTimeCard.Controls.Add(lblTimeResult);

            TextBox txtFormula = new TextBox() { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, BackColor = Color.FromArgb(248, 250, 252), Font = new Font("Consolas", 9.5f), ScrollBars = ScrollBars.Vertical };
            Button btnCopy = new Button() { Text = "[複製 S6 推估報告文字]", Dock = DockStyle.Fill, BackColor = Color.FromArgb(124, 58, 237), ForeColor = Color.White, Font = new Font("微軟正黑體", 11f, FontStyle.Bold) };

            tblRes.Controls.Add(lblDeltaT, 0, 0);
            tblRes.Controls.Add(lblTargetDelta, 0, 1);
            tblRes.Controls.Add(pnlEdCard, 0, 2);
            tblRes.Controls.Add(pnlTimeCard, 0, 3);
            tblRes.Controls.Add(txtFormula, 0, 4);
            tblRes.Controls.Add(btnCopy, 0, 5);
            grpRes.Controls.Add(tblRes);
            body.Controls.Add(grpRes, 1, 0);
            root.Controls.Add(body, 0, 1);
            tp.Controls.Add(root);

            Action updateS6Calc = () => {
                double bTrq = (double)numS6BaseTrq.Value;
                double bCyc = (double)numS6Cycle.Value;
                double bEd = (double)numS6BaseEd.Value;
                double bTemp = (double)numS6Temp.Value;
                double bAmb = (double)numS6Amb.Value;
                double tTgt = (double)numS6TgtTemp.Value;

                var res = CalcExtrapolatedS6Ed(bTrq, bCyc, bEd, bTemp, bAmb, tTgt);
                double dtBase = Math.Max(0.1, bTemp - bAmb);
                double dtTgt = Math.Max(0.1, tTgt - bAmb);

                lblDeltaT.Text = string.Format("基準熱平衡溫升: ΔT = {0:F1} K (實測 {1:F1}℃ - 室溫 {2:F1}℃)", dtBase, bTemp, bAmb);
                lblTargetDelta.Text = string.Format("目標允許溫升: ΔT_limit = {0:F1} K (裕度比: {1:F3})", dtTgt, dtTgt / dtBase);

                lblEdResult.Text = string.Format("推估容許最高 DUTY (ED%): {0:F1}% (+{1:F1}%)", res.ExtrapolatedEdPct, res.EdIncreasePct);
                lblTimeResult.Text = string.Format("在 {0:F1} 分鐘週期下: 有載 T1={1:F2}分 ({2}分{3:D2}秒), 空載自冷 T2={4:F2}分 ({5}分{6:D2}秒)",
                    bCyc, res.ExtrapolatedT1Min, (int)res.ExtrapolatedT1Min, (int)((res.ExtrapolatedT1Min - (int)res.ExtrapolatedT1Min) * 60),
                    res.ExtrapolatedT2Min, (int)res.ExtrapolatedT2Min, (int)((res.ExtrapolatedT2Min - (int)res.ExtrapolatedT2Min) * 60));

                StringBuilder sbF = new StringBuilder();
                sbF.AppendLine("【S6 週期工作制 DUTY 佔比回推計算公式與依據】");
                sbF.AppendLine("1. 依據標準: IEC 60034 週期負載平均等效發熱模型");
                sbF.AppendLine("2. 物理原理: 穩態熱平衡溫升與平均發熱損耗成正比 (ΔT_ss ∝ P_avg ∝ ED%)");
                sbF.AppendLine(string.Format("3. 容許熱量裕度比: ΔT_target / ΔT_base = {0:F1} / {1:F1} = {2:F4}", dtTgt, dtBase, dtTgt / dtBase));
                sbF.AppendLine(string.Format("4. 推估最高容許 ED: {0:F1}% * {1:F4} = {2:F1}%", bEd, dtTgt / dtBase, res.ExtrapolatedEdPct));
                sbF.AppendLine(string.Format("5. DUTY 提升幅度: +{0:F1}%", res.EdIncreasePct));
                sbF.AppendLine(string.Format("6. 週期時間分配: 有載 T1={0:F2} min, 空載自冷 T2={1:F2} min (總週期 {2:F1} min)", res.ExtrapolatedT1Min, res.ExtrapolatedT2Min, bCyc));
                txtFormula.Text = sbF.ToString();
            };

            numS6BaseTrq.ValueChanged += (s, e) => updateS6Calc();
            numS6Cycle.ValueChanged += (s, e) => updateS6Calc();
            numS6BaseEd.ValueChanged += (s, e) => updateS6Calc();
            numS6Temp.ValueChanged += (s, e) => updateS6Calc();
            numS6Amb.ValueChanged += (s, e) => updateS6Calc();
            numS6TgtTemp.ValueChanged += (s, e) => updateS6Calc();

            btnLoadRecent.Click += (s, e) => {
                if (lastS6Torque > 0.1) numS6BaseTrq.Value = (decimal)lastS6Torque;
                if (lastS6CycleMin > 0.1) numS6Cycle.Value = (decimal)lastS6CycleMin;
                if (lastS6EdPct > 0.1) numS6BaseEd.Value = (decimal)lastS6EdPct;
                if (lastS6PeakTemp > 0.1) numS6Temp.Value = (decimal)lastS6PeakTemp;
                if (lastS6AmbientTemp > 0.1) numS6Amb.Value = (decimal)lastS6AmbientTemp;
                updateS6Calc();
            };

            btnCopy.Click += (s, e) => {
                try
                {
                    Clipboard.SetText(txtFormula.Text);
                    MessageBox.Show("S6 極限能力回推報告已成功複製至剪貼簿！", "複製成功", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                catch { }
            };

            updateS6Calc();
        }

    }

    public class S2ExtrapolationResult
    {
        public double BaseTorque;
        public double DurationMin;
        public double FinalTemp;
        public double AmbientTemp;
        public double TargetTemp;
        public double RatedSpeed;
        public double TempRiseSlope;
        public double ExtrapolatedTorque;
        public double TorqueIncreasePct;
        public double ExtrapolatedMechPowerKw;
    }

    public class S6ExtrapolationResult
    {
        public double BaseTorque;
        public double CycleMin;
        public double CurrentEdPct;
        public double PeakTemp;
        public double AmbientTemp;
        public double TargetTemp;
        public double ExtrapolatedEdPct;
        public double EdIncreasePct;
        public double ExtrapolatedT1Min;
        public double ExtrapolatedT2Min;
    }

}
