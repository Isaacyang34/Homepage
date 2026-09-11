using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;

namespace DynamometerHMI
{
    public partial class MainForm : Form
    {
        // =========================================================================
        // 分頁: 空載溫升測試 (No-Load Temperature Rise Test)
        // 監控軸承溫度、超溫降速、TN等待循環升速、30分鐘溫差<1.0℃熱平衡判定
        // =========================================================================

        #region 空載測試控制項與變數宣告

        // 主容器
        private SplitContainer splitNoLoadMain;
        private SplitContainer splitNoLoadBottom;

        // 設定控制項
        private ComboBox cmbNoLoadRole;
        public CheckBox chkNoLoadRatedTest;           // 勾選: 額定轉速溫升測試
        private NumericUpDown numNoLoadRatedSpd;      // 額定轉速 (rpm)
        public CheckBox chkNoLoadMaxSpdTest;          // 勾選: 最高轉速階梯測試
        private NumericUpDown numNoLoadMaxSpd;        // 最高轉速 (rpm)
        private NumericUpDown numNoLoadStepSpd;       // 升速梯度 (rpm)
        private NumericUpDown numNoLoadTempLimit;     // 軸承溫度閥值 (℃)
        private NumericUpDown numNoLoadTimeTs;        // 超溫持續判定時間 Ts (秒)
        private NumericUpDown numNoLoadTimeTn;        // 降速冷卻等待時間 Tn (秒)
        private NumericUpDown numNoLoadStepDwellSec;  // 中間階梯過渡時間 (秒)

        private Button btnNoLoadSelectChannels;
        private Label lblNoLoadSelectedChHint;
        private Button btnStartNoLoad;
        private Button btnStopNoLoad;

        // 即時狀態顯示標籤
        private Label lblNoLoadStateBadge;
        private Label lblNoLoadStatusText;
        private Label lblNoLoadTargetSpdDisp;
        private Label lblNoLoadActSpdDisp;
        private Label lblNoLoadMaxTempDisp;
        private Label lblNoLoadDeltaTDisp;
        private Label lblNoLoadCountdownDisp;
        private Label lblNoLoadElapsedDisp;
        private ProgressBar prgNoLoad;

        // 下方數據表與趨勢圖 (noLoadTempTrend 已由 sharedTestTempTrend 統一代理)
        private DataGridView dgvNoLoad;
        public GroupBox grpNoLoadChart;

        // 狀態機列舉
        public enum NoLoadState
        {
            Idle = 0,               // 待機
            RatedWarmup = 1,        // 額定轉速運轉與熱平衡比對 (Phase 1)
            AccelRamp = 2,          // 梯度加速中
            IntermediateRun = 3,    // 中間階梯轉速監控
            MaxSpeedRun = 4,        // 最高轉速運轉與最終熱平衡比對 (Phase 3)
            MaxSpeedPostHold = 5,   // 最高轉速達標後續 5 筆採樣驗證階段
            CoolingWait = 6,        // 超溫降速 (減梯度一半) 與 TN 冷卻等待
            Completed = 7           // 測試完成
        }

        // 狀態機運作變數
        public NoLoadState noLoadState = NoLoadState.Idle;
        public bool isNoLoadRunning = false;
        public double noLoadCurrentTargetSpd = 0.0;
        public int noLoadElapsedSec = 0;             // 測試總經過時間 (秒)
        public int noLoadStageElapsedSec = 0;        // 當前轉速階梯經過時間 (秒)
        public int noLoadOverTempDurationSec = 0;    // 連續超溫累計秒數
        public int noLoadCoolingTimerSec = 0;        // TN 冷卻等待倒數秒數
        public int noLoadCoolingRetryCount = 0;      // 超溫降速重試次數累計
        public int noLoadIntermediateDwellSec = 60;  // 中間階梯預設停留 60 秒觀察穩定度
        public int noLoadDwellTimerSec = 0;          // 中間階梯已停留秒數
        public int noLoadSpdDrive = 2;               // 待測端載台 ID (預設 B 載台)
        public int noLoadTrqDrive = 1;               // 加載端載台 ID (預設 A 載台空載跟隨)

        // 監控通道選取旗標 (20 通道，預設監控 CH1~CH4)
        public bool[] noLoadMonitoredChannels = new bool[20] {
            true, true, true, true, false, false, false, false, false, false,
            false, false, false, false, false, false, false, false, false, false
        };

        // 溫度歷史滑動佇列 (保存 45 分鐘快照，供 30 分鐘溫差計算)
        private List<KeyValuePair<DateTime, double[]>> noLoadTempHistory = new List<KeyValuePair<DateTime, double[]>>();

        // 專屬每秒狀態機計時器
        private Timer noLoadTimer;

        // 30 分鐘穩定達標後續記錄計數器 (1~5) 與階段名稱 ("額定" 或 "最高速")
        private int noLoadPostStableCounter = 0;
        private string noLoadPostStableStage = "";

        // 快取靜態字體 (避免動態 new Font 引發 GDI HFONT 洩漏與長時記憶體累積)
        private static readonly Font fontNoLoadMilestone = new Font("微軟正黑體", 10.5f, FontStyle.Bold);
        private static readonly Font fontNoLoadPostStable = new Font("微軟正黑體", 9.5f, FontStyle.Bold);

        #endregion

        #region 介面建置 (BuildNoLoadTab)

        public void BuildNoLoadTab(TabPage tab)
        {
            tab.Controls.Clear();
            tab.BackColor = Color.FromArgb(248, 250, 252);

            // 主分割容器 (上下分割：上方參數與狀態卡片，下方數據表與即時趨勢圖)
            splitNoLoadMain = new SplitContainer()
            {
                Dock = DockStyle.Fill,
                Orientation = Orientation.Horizontal,
                SplitterWidth = 8,
                BackColor = Color.FromArgb(203, 213, 225)
            };
            splitNoLoadMain.Panel1.AutoScroll = true;
            splitNoLoadMain.Panel1.BackColor = Color.FromArgb(248, 250, 252);
            splitNoLoadMain.Panel2.AutoScroll = true;
            splitNoLoadMain.Panel2.BackColor = Color.White;
            splitNoLoadMain.SplitterMoved += (s, e) => { layoutSplitters["NoLoadMain"] = splitNoLoadMain.SplitterDistance; SaveLayoutConfig(); };
            SafeSetupSplitContainer(splitNoLoadMain, 460, 250, 150);

            // -------------------------------------------------------------
            // 上半部：主參數面板 (採用 TableLayoutPanel 彈性排版，杜絕遮擋)
            // -------------------------------------------------------------
            TableLayoutPanel tblTop = new TableLayoutPanel()
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 1,
                Padding = new Padding(8),
                BackColor = Color.FromArgb(248, 250, 252)
            };
            tblTop.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 55f)); // 左側：設定控制項
            tblTop.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 45f)); // 右側：即時狀態儀表
            tblTop.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

            // 左側群組：參數設定區
            GroupBox grpParams = new GroupBox()
            {
                Text = "⚙️ 空載測試參數設定 (額定/最高轉速、溫度監控、升降速梯度)",
                Dock = DockStyle.Fill,
                Font = new Font("微軟正黑體", 12f, FontStyle.Bold),
                ForeColor = Color.FromArgb(15, 23, 42),
                BackColor = Color.White,
                Padding = new Padding(12)
            };

            TableLayoutPanel tblParamsInner = new TableLayoutPanel()
            {
                Dock = DockStyle.Fill,
                ColumnCount = 4,
                RowCount = 6,
                AutoScroll = true
            };
            tblParamsInner.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130f));
            tblParamsInner.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
            tblParamsInner.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130f));
            tblParamsInner.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));

            for (int r = 0; r < 6; r++)
                tblParamsInner.RowStyles.Add(new RowStyle(SizeType.Absolute, 42f));

            // 列 0: 待測端載台選擇 (空載測試無加載端)
            Label lblRole = new Label() { Text = "待測載台:", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleRight, Font = new Font("微軟正黑體", 10.5f, FontStyle.Bold) };
            cmbNoLoadRole = new ComboBox() { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList, Font = new Font("微軟正黑體", 11f) };
            cmbNoLoadRole.Items.AddRange(new object[] { "A 載台 (待測運轉)", "B 載台 (待測運轉)" });
            cmbNoLoadRole.SelectedIndex = 1; // 預設 B 載台待測
            cmbNoLoadRole.SelectedIndexChanged += (s, e) => SaveLayoutConfig();
            tblParamsInner.Controls.Add(lblRole, 0, 0);
            tblParamsInner.Controls.Add(cmbNoLoadRole, 1, 0);
            tblParamsInner.SetColumnSpan(cmbNoLoadRole, 3);

            // 列 1: 額定轉速 & 最高轉速 (可勾選只做其中一項或兩者都做，預設皆勾選)
            chkNoLoadRatedTest = new CheckBox()
            {
                Text = "額定轉速 (rpm):",
                Checked = true,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleRight,
                CheckAlign = ContentAlignment.MiddleLeft,
                Font = new Font("微軟正黑體", 10.5f, FontStyle.Bold),
                ForeColor = Color.FromArgb(30, 41, 59),
                Cursor = Cursors.Hand
            };
            numNoLoadRatedSpd = new NumericUpDown() { Dock = DockStyle.Fill, Minimum = 100, Maximum = 12000, Value = 1500, Increment = 100, Font = new Font("微軟正黑體", 11f, FontStyle.Bold) };
            numNoLoadRatedSpd.ValueChanged += (s, e) => SaveLayoutConfig();
            chkNoLoadRatedTest.CheckedChanged += (s, e) => {
                numNoLoadRatedSpd.Enabled = chkNoLoadRatedTest.Checked;
                SaveLayoutConfig();
            };

            chkNoLoadMaxSpdTest = new CheckBox()
            {
                Text = "最高轉速 (rpm):",
                Checked = true,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleRight,
                CheckAlign = ContentAlignment.MiddleLeft,
                Font = new Font("微軟正黑體", 10.5f, FontStyle.Bold),
                ForeColor = Color.FromArgb(30, 41, 59),
                Cursor = Cursors.Hand
            };
            numNoLoadMaxSpd = new NumericUpDown() { Dock = DockStyle.Fill, Minimum = 100, Maximum = 12000, Value = 3600, Increment = 100, Font = new Font("微軟正黑體", 11f, FontStyle.Bold) };
            numNoLoadMaxSpd.ValueChanged += (s, e) => SaveLayoutConfig();
            chkNoLoadMaxSpdTest.CheckedChanged += (s, e) => {
                numNoLoadMaxSpd.Enabled = chkNoLoadMaxSpdTest.Checked;
                SaveLayoutConfig();
            };

            var ttRated = new ToolTip();
            ttRated.SetToolTip(chkNoLoadRatedTest, "【額定轉速溫升測試 (Phase 1)】\n勾選以進行額定轉速持續運轉與 30 分鐘熱平衡判定。\n若取消勾選，將直接以設定轉速作為起始轉速進入階梯提速，跳過 Phase 1 熱平衡等待。");
            var ttMaxSpd = new ToolTip();
            ttMaxSpd.SetToolTip(chkNoLoadMaxSpdTest, "【最高轉速階梯測試 (Phase 2 & 3)】\n勾選以在額定溫升後依梯度加速至最高轉速並比對熱平衡。\n若取消勾選，則在額定轉速熱平衡達標後直接安全停機結案。");

            tblParamsInner.Controls.Add(chkNoLoadRatedTest, 0, 1);
            tblParamsInner.Controls.Add(numNoLoadRatedSpd, 1, 1);
            tblParamsInner.Controls.Add(chkNoLoadMaxSpdTest, 2, 1);
            tblParamsInner.Controls.Add(numNoLoadMaxSpd, 3, 1);

            // 列 2: 升速梯度 & 階梯停留時間 (階梯評估)
            Label lblStep = new Label() { Text = "升速梯度 (rpm):", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleRight, Font = new Font("微軟正黑體", 10.5f, FontStyle.Bold) };
            numNoLoadStepSpd = new NumericUpDown() { Dock = DockStyle.Fill, Minimum = 50, Maximum = 3000, Value = 500, Increment = 50, Font = new Font("微軟正黑體", 11f, FontStyle.Bold) };
            numNoLoadStepSpd.ValueChanged += (s, e) => SaveLayoutConfig();
            Label lblDwell = new Label() { Text = "階梯評估 (秒):", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleRight, Font = new Font("微軟正黑體", 10.5f, FontStyle.Bold) };
            numNoLoadStepDwellSec = new NumericUpDown() { Dock = DockStyle.Fill, Minimum = 10, Maximum = 1800, Value = 60, Increment = 10, Font = new Font("微軟正黑體", 11f, FontStyle.Bold) };
            numNoLoadStepDwellSec.ValueChanged += (s, e) => SaveLayoutConfig();
            var ttDwell = new ToolTip();
            ttDwell.SetToolTip(lblDwell, "【階梯評估時間 (秒)】\n額定轉速熱平衡後，每按梯度升速一個階梯時，在該轉速下『持溫停留評估穩定度』的秒數。\n在此期間若未超溫且運轉穩定，即判定評估通過，繼續提速至下一階梯。");
            ttDwell.SetToolTip(numNoLoadStepDwellSec, "【階梯評估時間 (秒)】\n額定轉速熱平衡後，每按梯度升速一個階梯時，在該轉速下『持溫停留評估穩定度』的秒數。\n在此期間若未超溫且運轉穩定，即判定評估通過，繼續提速至下一階梯。");
            tblParamsInner.Controls.Add(lblStep, 0, 2);
            tblParamsInner.Controls.Add(numNoLoadStepSpd, 1, 2);
            tblParamsInner.Controls.Add(lblDwell, 2, 2);
            tblParamsInner.Controls.Add(numNoLoadStepDwellSec, 3, 2);

            // 列 3: 溫度閥值 & 超溫時間 Ts
            Label lblTempLim = new Label() { Text = "溫度閥值 (℃):", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleRight, Font = new Font("微軟正黑體", 10.5f, FontStyle.Bold), ForeColor = Color.FromArgb(220, 38, 38) };
            numNoLoadTempLimit = new NumericUpDown() { Dock = DockStyle.Fill, Minimum = 20, Maximum = 180, DecimalPlaces = 1, Value = 75.0m, Increment = 1.0m, Font = new Font("微軟正黑體", 11f, FontStyle.Bold), ForeColor = Color.FromArgb(220, 38, 38) };
            numNoLoadTempLimit.ValueChanged += (s, e) => SaveLayoutConfig();
            Label lblTs = new Label() { Text = "超溫時間 Ts (s):", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleRight, Font = new Font("微軟正黑體", 10.5f, FontStyle.Bold), ForeColor = Color.FromArgb(220, 38, 38) };
            numNoLoadTimeTs = new NumericUpDown() { Dock = DockStyle.Fill, Minimum = 5, Maximum = 600, Value = 30, Increment = 5, Font = new Font("微軟正黑體", 11f, FontStyle.Bold), ForeColor = Color.FromArgb(220, 38, 38) };
            numNoLoadTimeTs.ValueChanged += (s, e) => SaveLayoutConfig();
            tblParamsInner.Controls.Add(lblTempLim, 0, 3);
            tblParamsInner.Controls.Add(numNoLoadTempLimit, 1, 3);
            tblParamsInner.Controls.Add(lblTs, 2, 3);
            tblParamsInner.Controls.Add(numNoLoadTimeTs, 3, 3);

            // 列 4: 冷卻等待時間 Tn & 通道選取
            Label lblTn = new Label() { Text = "降速冷卻 Tn (s):", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleRight, Font = new Font("微軟正黑體", 10.5f, FontStyle.Bold), ForeColor = Color.FromArgb(139, 92, 246) };
            numNoLoadTimeTn = new NumericUpDown() { Dock = DockStyle.Fill, Minimum = 10, Maximum = 1800, Value = 60, Increment = 10, Font = new Font("微軟正黑體", 11f, FontStyle.Bold), ForeColor = Color.FromArgb(139, 92, 246) };
            numNoLoadTimeTn.ValueChanged += (s, e) => SaveLayoutConfig();
            btnNoLoadSelectChannels = new Button()
            {
                Text = "🌡 選擇軸承監控通道...",
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(241, 245, 249),
                Font = new Font("微軟正黑體", 10.5f, FontStyle.Bold),
                Cursor = Cursors.Hand
            };
            btnNoLoadSelectChannels.Click += (s, e) => ShowNoLoadChannelSelectDialog();
            tblParamsInner.Controls.Add(lblTn, 0, 4);
            tblParamsInner.Controls.Add(numNoLoadTimeTn, 1, 4);
            tblParamsInner.Controls.Add(btnNoLoadSelectChannels, 2, 4);
            tblParamsInner.SetColumnSpan(btnNoLoadSelectChannels, 2);

            // 列 5: 通道提示與操作按鈕
            lblNoLoadSelectedChHint = new Label()
            {
                Text = "已選通道: CH1~CH4 (過溫超出 Ts 減速梯度一半，30min溫差<1℃完成)",
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                Font = new Font("微軟正黑體", 9.5f),
                ForeColor = Color.FromArgb(100, 116, 139)
            };
            UpdateNoLoadChannelHint();

            FlowLayoutPanel flpBtns = new FlowLayoutPanel()
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.RightToLeft,
                Margin = new Padding(0)
            };
            btnStopNoLoad = new Button()
            {
                Text = "⏹ 終止測試",
                Size = new Size(115, 36),
                BackColor = Color.FromArgb(239, 68, 68),
                ForeColor = Color.White,
                Font = new Font("微軟正黑體", 11f, FontStyle.Bold),
                Cursor = Cursors.Hand,
                Enabled = false
            };
            btnStopNoLoad.Click += (s, e) => StopNoLoadTest("使用者手動點擊終止");

            btnStartNoLoad = new Button()
            {
                Text = "▶ 開始空載測試",
                Size = new Size(135, 36),
                BackColor = Color.FromArgb(16, 185, 129),
                ForeColor = Color.White,
                Font = new Font("微軟正黑體", 11f, FontStyle.Bold),
                Cursor = Cursors.Hand
            };
            btnStartNoLoad.Click += (s, e) => StartNoLoadTest();

            flpBtns.Controls.AddRange(new Control[] { btnStopNoLoad, btnStartNoLoad });

            tblParamsInner.Controls.Add(lblNoLoadSelectedChHint, 0, 5);
            tblParamsInner.SetColumnSpan(lblNoLoadSelectedChHint, 2);
            tblParamsInner.Controls.Add(flpBtns, 2, 5);
            tblParamsInner.SetColumnSpan(flpBtns, 2);

            grpParams.Controls.Add(tblParamsInner);
            tblTop.Controls.Add(grpParams, 0, 0);

            // -------------------------------------------------------------
            // 右側群組：即時運轉監控與熱平衡狀態儀表
            // -------------------------------------------------------------
            GroupBox grpStatus = new GroupBox()
            {
                Text = "📊 即時運轉與熱平衡監控儀表 (30 分鐘溫差比對)",
                Dock = DockStyle.Fill,
                Font = new Font("微軟正黑體", 12f, FontStyle.Bold),
                ForeColor = Color.FromArgb(15, 23, 42),
                BackColor = Color.White,
                Padding = new Padding(12)
            };

            TableLayoutPanel tblStatusInner = new TableLayoutPanel()
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 6,
                AutoScroll = true
            };
            tblStatusInner.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
            tblStatusInner.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));

            tblStatusInner.RowStyles.Add(new RowStyle(SizeType.Absolute, 38f)); // 狀態徽章 + 總時間
            tblStatusInner.RowStyles.Add(new RowStyle(SizeType.Absolute, 70f)); // 目標轉速 + 實測轉速 (加大行距 1.5x)
            tblStatusInner.RowStyles.Add(new RowStyle(SizeType.Absolute, 70f)); // 軸承最高溫 + 30min 溫差 (加大行距 1.5x)
            tblStatusInner.RowStyles.Add(new RowStyle(SizeType.Absolute, 40f)); // 倒數計時提示
            tblStatusInner.RowStyles.Add(new RowStyle(SizeType.Absolute, 28f)); // 進度條
            tblStatusInner.RowStyles.Add(new RowStyle(SizeType.Percent, 100f)); // 詳細狀態文字

            // 0: 狀態徽章與時間
            lblNoLoadStateBadge = new Label()
            {
                Text = "【待機中】尚未開始",
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                Font = new Font("微軟正黑體", 11.5f, FontStyle.Bold),
                ForeColor = Color.FromArgb(100, 116, 139)
            };
            lblNoLoadElapsedDisp = new Label()
            {
                Text = "總時間: 00:00:00",
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleRight,
                Font = new Font("Consolas", 12f, FontStyle.Bold),
                ForeColor = Color.FromArgb(15, 23, 42)
            };
            tblStatusInner.Controls.Add(lblNoLoadStateBadge, 0, 0);
            tblStatusInner.Controls.Add(lblNoLoadElapsedDisp, 1, 0);

            // 1: 目標轉速與實測轉速卡片
            Panel pnlTarget = CreateStatCard("目標轉速", "0 rpm", Color.FromArgb(2, 132, 199), out lblNoLoadTargetSpdDisp);
            Panel pnlAct = CreateStatCard("實測轉速", "0 rpm", Color.FromArgb(16, 185, 129), out lblNoLoadActSpdDisp);
            tblStatusInner.Controls.Add(pnlTarget, 0, 1);
            tblStatusInner.Controls.Add(pnlAct, 1, 1);

            // 2: 軸承最高溫與 30min 溫差卡片
            Panel pnlTemp = CreateStatCard("軸承最高溫", "--.- ℃", Color.FromArgb(239, 68, 68), out lblNoLoadMaxTempDisp);
            Panel pnlDelta = CreateStatCard("30min 溫差", "採樣累積中...", Color.FromArgb(139, 92, 246), out lblNoLoadDeltaTDisp);
            tblStatusInner.Controls.Add(pnlTemp, 0, 2);
            tblStatusInner.Controls.Add(pnlDelta, 1, 2);

            // 3: 倒數計時與狀態提示
            lblNoLoadCountdownDisp = new Label()
            {
                Text = "過溫計時: 0s / 30s | 冷卻等待: 0s",
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                Font = new Font("微軟正黑體", 10.5f, FontStyle.Bold),
                ForeColor = Color.FromArgb(100, 116, 139)
            };
            tblStatusInner.Controls.Add(lblNoLoadCountdownDisp, 0, 3);
            tblStatusInner.SetColumnSpan(lblNoLoadCountdownDisp, 2);

            // 4: 進度條
            prgNoLoad = new ProgressBar()
            {
                Dock = DockStyle.Fill,
                Minimum = 0,
                Maximum = 1800,
                Value = 0
            };
            tblStatusInner.Controls.Add(prgNoLoad, 0, 4);
            tblStatusInner.SetColumnSpan(prgNoLoad, 2);

            // 5: 詳細說明文字條
            lblNoLoadStatusText = new Label()
            {
                Text = "說明: 測試流程為先測試額定轉速，達到溫度穩定 (30min溫差<1℃) 後再加速至最高轉速。\r\n若超出閥值持續 Ts 則依梯度之一半減速，等待 Tn 後再次升速，循環直到最高轉速穩定結案。",
                Dock = DockStyle.Fill,
                Font = new Font("微軟正黑體", 9.5f),
                ForeColor = Color.FromArgb(71, 85, 105),
                BackColor = Color.FromArgb(241, 245, 249),
                Padding = new Padding(6)
            };
            tblStatusInner.Controls.Add(lblNoLoadStatusText, 0, 5);
            tblStatusInner.SetColumnSpan(lblNoLoadStatusText, 2);

            grpStatus.Controls.Add(tblStatusInner);
            tblTop.Controls.Add(grpStatus, 1, 0);

            splitNoLoadMain.Panel1.Controls.Add(tblTop);

            // -------------------------------------------------------------
            // 下半部：即時遙測記錄表格 (DGV) 與 即時溫度趨勢圖 (MotorTempTrendControl)
            // -------------------------------------------------------------
            splitNoLoadBottom = new SplitContainer()
            {
                Dock = DockStyle.Fill,
                Orientation = Orientation.Vertical,
                SplitterWidth = 8,
                BackColor = Color.FromArgb(203, 213, 225)
            };
            splitNoLoadBottom.Panel1.AutoScroll = true;
            splitNoLoadBottom.Panel1.BackColor = Color.White;
            splitNoLoadBottom.Panel2.AutoScroll = true;
            splitNoLoadBottom.Panel2.BackColor = Color.White;
            splitNoLoadBottom.SplitterMoved += (s, e) => { layoutSplitters["NoLoadBottom"] = splitNoLoadBottom.SplitterDistance; SaveLayoutConfig(); };
            SafeSetupSplitContainer(splitNoLoadBottom, 580, 200, 200);

            // Panel1: 專屬即時溫度趨勢圖 (多通道各別彩色波形與端點即時標籤)
            grpNoLoadChart = new GroupBox()
            {
                Text = "📈 軸承溫度多通道即時趨勢圖 (選取通道獨立彩色曲線、端點標記與圖例)",
                Dock = DockStyle.Fill,
                Font = new Font("微軟正黑體", 11.5f, FontStyle.Bold),
                ForeColor = Color.FromArgb(15, 23, 42),
                Padding = new Padding(8)
            };
            // noLoadTempTrend 與 TN / Duty 共用 sharedTestTempTrend，切換至此分頁時由 AttachSharedTempTrendTo 動態掛載
            splitNoLoadBottom.Panel1.Controls.Add(grpNoLoadChart);
            UpdateNoLoadChannelHint();

            // Panel2: 實測記錄 DataGridView
            GroupBox grpTable = new GroupBox()
            {
                Text = "📋 測試階段記錄與熱平衡事件日誌",
                Dock = DockStyle.Fill,
                Font = new Font("微軟正黑體", 11.5f, FontStyle.Bold),
                ForeColor = Color.FromArgb(15, 23, 42),
                Padding = new Padding(8)
            };

            dgvNoLoad = new DataGridView()
            {
                Dock = DockStyle.Fill,
                ReadOnly = true,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                RowHeadersVisible = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                BackgroundColor = Color.White,
                BorderStyle = BorderStyle.None,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill
            };
            dgvNoLoad.ColumnWidthChanged += (s, e) => SaveLayoutConfig();
            dgvNoLoad.ColumnHeadersDefaultCellStyle.Font = new Font("微軟正黑體", 10.5f, FontStyle.Bold);
            dgvNoLoad.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(241, 245, 249);
            dgvNoLoad.DefaultCellStyle.Font = new Font("微軟正黑體", 10f);

            dgvNoLoad.Columns.Add("Time", "時間");
            dgvNoLoad.Columns.Add("Elapsed", "經過秒數");
            dgvNoLoad.Columns.Add("Phase", "階段");
            dgvNoLoad.Columns.Add("TargetSpd", "目標轉速(rpm)");
            dgvNoLoad.Columns.Add("ActSpd", "實測轉速(rpm)");
            dgvNoLoad.Columns.Add("MaxTemp", "軸承最高溫(℃)");
            dgvNoLoad.Columns.Add("DeltaT", "30min溫差(℃)");
            dgvNoLoad.Columns.Add("Event", "事件與判定");

            dgvNoLoad.Columns["Time"].FillWeight = 85;
            dgvNoLoad.Columns["Elapsed"].FillWeight = 80;
            dgvNoLoad.Columns["Phase"].FillWeight = 110;
            dgvNoLoad.Columns["TargetSpd"].FillWeight = 110;
            dgvNoLoad.Columns["ActSpd"].FillWeight = 110;
            dgvNoLoad.Columns["MaxTemp"].FillWeight = 115;
            dgvNoLoad.Columns["DeltaT"].FillWeight = 115;
            dgvNoLoad.Columns["Event"].FillWeight = 200;

            // 頂部操作工具列 (雙標記點聚焦與匯出日誌)
            Panel pnlTableTop = new Panel()
            {
                Dock = DockStyle.Top,
                Height = 36,
                Padding = new Padding(0, 0, 0, 6)
            };

            Button btnJumpRatedStable = new Button()
            {
                Text = "🎯 聚焦額定達標點",
                Dock = DockStyle.Right,
                Width = 165,
                Height = 30,
                Font = new Font("微軟正黑體", 9.5f, FontStyle.Bold),
                BackColor = Color.FromArgb(241, 245, 249),
                Cursor = Cursors.Hand
            };
            btnJumpRatedStable.Click += (s, e) => {
                if (dgvNoLoad == null || dgvNoLoad.Rows.Count == 0) return;
                for (int i = 0; i < dgvNoLoad.Rows.Count; i++)
                {
                    string ev = Convert.ToString(dgvNoLoad.Rows[i].Cells["Event"].Value ?? "");
                    if (ev.Contains("額定轉速 30min 熱平衡達標核心點") || ev.Contains("★額定達標★"))
                    {
                        dgvNoLoad.ClearSelection();
                        dgvNoLoad.Rows[i].Selected = true;
                        dgvNoLoad.FirstDisplayedScrollingRowIndex = Math.Max(0, i - 4);
                        return;
                    }
                }
                MessageBox.Show("目前尚未達到【額定轉速】30 分鐘熱平衡穩定達標點！", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
            };

            Button btnJumpMaxSpdStable = new Button()
            {
                Text = "🎯 聚焦最高速達標點",
                Dock = DockStyle.Right,
                Width = 175,
                Height = 30,
                Font = new Font("微軟正黑體", 9.5f, FontStyle.Bold),
                BackColor = Color.FromArgb(241, 245, 249),
                Cursor = Cursors.Hand
            };
            btnJumpMaxSpdStable.Click += (s, e) => {
                if (dgvNoLoad == null || dgvNoLoad.Rows.Count == 0) return;
                for (int i = 0; i < dgvNoLoad.Rows.Count; i++)
                {
                    string ev = Convert.ToString(dgvNoLoad.Rows[i].Cells["Event"].Value ?? "");
                    if (ev.Contains("最高轉速 30min 熱平衡達標核心點") || ev.Contains("★最高速達標★"))
                    {
                        dgvNoLoad.ClearSelection();
                        dgvNoLoad.Rows[i].Selected = true;
                        dgvNoLoad.FirstDisplayedScrollingRowIndex = Math.Max(0, i - 4);
                        return;
                    }
                }
                MessageBox.Show("目前尚未達到【最高轉速】30 分鐘熱平衡穩定達標點！", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
            };

            Button btnExportCsv = new Button()
            {
                Text = "📥 匯出空載日誌 (CSV)",
                Dock = DockStyle.Right,
                Width = 165,
                Height = 30,
                Font = new Font("微軟正黑體", 9.5f, FontStyle.Bold),
                BackColor = Color.FromArgb(241, 245, 249),
                Cursor = Cursors.Hand
            };
            btnExportCsv.Click += (s, e) => ExportNoLoadGridToCsv();

            pnlTableTop.Controls.Add(btnExportCsv);
            pnlTableTop.Controls.Add(btnJumpMaxSpdStable);
            pnlTableTop.Controls.Add(btnJumpRatedStable);

            grpTable.Controls.Add(dgvNoLoad);
            grpTable.Controls.Add(pnlTableTop);
            splitNoLoadBottom.Panel2.Controls.Add(grpTable);

            splitNoLoadMain.Panel2.Controls.Add(splitNoLoadBottom);
            tab.Controls.Add(splitNoLoadMain);

            // 初始化計時器
            noLoadTimer = new Timer() { Interval = 1000 };
            noLoadTimer.Tick += NoLoadTimer_Tick;
        }

        private Panel CreateStatCard(string title, string defaultVal, Color accentColor, out Label lblVal)
        {
            Panel pnl = new Panel()
            {
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(248, 250, 252),
                Padding = new Padding(10, 6, 10, 6),
                Margin = new Padding(4)
            };
            pnl.Paint += (s, e) => {
                using (Pen pen = new Pen(Color.FromArgb(226, 232, 240), 1))
                {
                    e.Graphics.DrawRectangle(pen, 0, 0, pnl.Width - 1, pnl.Height - 1);
                }
            };

            Label lTitle = new Label()
            {
                Text = title,
                Dock = DockStyle.Top,
                Height = 22,
                Font = new Font("微軟正黑體", 9.5f, FontStyle.Bold),
                ForeColor = Color.FromArgb(100, 116, 139)
            };

            lblVal = new Label()
            {
                Text = defaultVal,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                Font = new Font("Consolas", 15f, FontStyle.Bold),
                ForeColor = accentColor
            };

            pnl.Controls.Add(lblVal);
            pnl.Controls.Add(lTitle);
            return pnl;
        }

        #endregion

        #region 通道選擇對話框 (與 S1 相同規格)

        private void ShowNoLoadChannelSelectDialog()
        {
            Form dlg = new Form()
            {
                Text = "空載測試軸承溫度監控通道選取 (全選取通道 30min 溫差皆 < 1.0°C 才視為達標)",
                Width = 440,
                Height = 500,
                StartPosition = FormStartPosition.CenterParent,
                FormBorderStyle = FormBorderStyle.FixedDialog,
                MaximizeBox = false,
                MinimizeBox = false,
                ShowInTaskbar = false,
                Font = new Font("微軟正黑體", 9.5f)
            };

            Label lHint = new Label()
            {
                Text = "請勾選參與空載測試軸承溫升與熱平衡判定之通道：\r\n(若設備已連線，可點擊「⚡ 依實測選取」自動偵測真正有訊號的通道)",
                Dock = DockStyle.Top,
                Height = 46,
                Padding = new Padding(8),
                Font = new Font("微軟正黑體", 9f, FontStyle.Bold),
                ForeColor = Color.FromArgb(15, 23, 42)
            };

            CheckedListBox clb = new CheckedListBox()
            {
                Dock = DockStyle.Fill,
                CheckOnClick = true,
                Font = new Font("Consolas", 10f)
            };

            for (int i = 0; i < 20; i++)
            {
                string cName = (gl820ChannelNames != null && i < gl820ChannelNames.Length && !string.IsNullOrEmpty(gl820ChannelNames[i])) ? gl820ChannelNames[i] : ("CH" + (i + 1));
                bool isChecked = (noLoadMonitoredChannels != null && i < noLoadMonitoredChannels.Length) ? noLoadMonitoredChannels[i] : (i < 4);
                double curVal = (gbdChTemps != null && i < gbdChTemps.Length) ? gbdChTemps[i] : 0.0;
                string valHint = (curVal > -40.0 && curVal < 400.0 && Math.Abs(curVal) > 0.01) ? string.Format(" [{0:F1}℃]", curVal) : " [--.-]";
                clb.Items.Add(string.Format("CH{0,2}: {1,-10}{2}", i + 1, cName, valHint), isChecked);
            }

            FlowLayoutPanel pnlBtns = new FlowLayoutPanel()
            {
                Dock = DockStyle.Bottom,
                Height = 45,
                FlowDirection = FlowDirection.RightToLeft,
                Padding = new Padding(6)
            };
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
                for (int i = 0; i < 20; i++)
                {
                    noLoadMonitoredChannels[i] = clb.GetItemChecked(i);
                    if (noLoadMonitoredChannels[i]) cnt++;
                }
                if (cnt == 0)
                {
                    noLoadMonitoredChannels[0] = true; // 最少監控 CH1
                }
                UpdateNoLoadChannelHint();
                SaveLayoutConfig();
            }
        }

        private void UpdateNoLoadChannelHint()
        {
            if (lblNoLoadSelectedChHint == null) return;
            List<string> chs = new List<string>();
            for (int i = 0; i < 20; i++)
            {
                if (noLoadMonitoredChannels != null && i < noLoadMonitoredChannels.Length && noLoadMonitoredChannels[i])
                {
                    string cName = (gl820ChannelNames != null && i < gl820ChannelNames.Length && !string.IsNullOrEmpty(gl820ChannelNames[i])) ? gl820ChannelNames[i] : ("CH" + (i + 1));
                    chs.Add(string.Format("CH{0}({1})", i + 1, cName));
                }
            }
            lblNoLoadSelectedChHint.Text = "已選監控通道: " + (chs.Count > 0 ? string.Join(", ", chs) : "無 (預設CH1)");
            if (grpNoLoadChart != null)
            {
                grpNoLoadChart.Text = string.Format("📈 軸承溫度即時趨勢圖 [監控通道: {0}]", chs.Count > 0 ? string.Join(", ", chs) : "CH1");
            }
            if (noLoadTempTrend != null)
            {
                noLoadTempTrend.SetChannelVisibility(noLoadMonitoredChannels);
            }
        }

        #endregion

        #region 測試啟動與停止 (StartNoLoadTest / StopNoLoadTest)

        private void StartNoLoadTest()
        {
            if (isNoLoadRunning) return;

            // 1. 勾選與參數檢查
            if (!chkNoLoadRatedTest.Checked && !chkNoLoadMaxSpdTest.Checked)
            {
                MessageBox.Show("【啟動攔截】請至少勾選『額定轉速』或『最高轉速』其中一項測試！", "參數檢查", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            decimal ratedVal = numNoLoadRatedSpd.Value;
            decimal maxVal = numNoLoadMaxSpd.Value;
            if (chkNoLoadRatedTest.Checked && chkNoLoadMaxSpdTest.Checked && maxVal < ratedVal)
            {
                MessageBox.Show("最高轉速不得低於額定轉速，請檢查轉速設定！", "參數錯誤", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            // 2. 設備在線狀態防呆檢查 (使用者明確規範：空載測試僅需「待測端驅動器 + 功率表 + 溫度記錄器」3項在線即可運作)
            //    其餘儀器 (Kistler 扭力計、加載端驅動器) 連線與否及數據完全不阻擋啟動，亦不進行分析
            int roleIdx = cmbNoLoadRole.SelectedIndex;
            noLoadSpdDrive = (roleIdx == 0) ? 1 : 2; // 0: A載台待測, 1: B載台待測
            noLoadTrqDrive = 0;                      // 空載測試無加載端

            bool isDutDriveOnline = (noLoadSpdDrive == 1) ? isHmiKebOpen1 : isHmiKebOpen2;
            string dutDriveName = (noLoadSpdDrive == 1) ? "A載台驅動器 (待測端)" : "B載台驅動器 (待測端)";
            bool isPowerMeterOnline = (tcpPower != null && tcpPower.Connected);
            bool isGbdOnline = (tcpGbd != null && tcpGbd.Connected);

            List<string> missingDevices = new List<string>();
            if (!isDutDriveOnline) missingDevices.Add("❌ " + dutDriveName + " (通訊未開啟或未連線)");
            if (!isPowerMeterOnline) missingDevices.Add("❌ 橫河 WT333E 功率表 (未連線 Modbus TCP 192.168.0.11:502)");
            if (!isGbdOnline) missingDevices.Add("❌ Graphtec GL820 溫度記錄器 (未連線 TCP 192.168.0.3:8023)");

            if (missingDevices.Count > 0)
            {
                string msg = "【空載測試啟動攔截】以下必要核心設備尚未在線：\n\n" +
                             string.Join("\n", missingDevices.ToArray()) +
                             "\n\n※ 空載測試規則：僅需『待測端驅動器 + 功率表 + 溫度記錄器』在線即可執行，其餘設備(扭力計/加載端)不影響。請確認上述必要設備連線後再啟動！";
                MessageBox.Show(msg, "必要設備未在線 - 啟動攔截", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            // 檢查溫度數值防呆 (熱電偶必須有有效數據)
            double currentMaxBearing = -999.0;
            if (gbdChTemps != null && gbdChTemps.Length > 0)
            {
                for (int ch = 0; ch < Math.Min(20, gbdChTemps.Length); ch++)
                {
                    if (noLoadMonitoredChannels == null || (ch < noLoadMonitoredChannels.Length && noLoadMonitoredChannels[ch]))
                    {
                        if (gbdChTemps[ch] > currentMaxBearing && gbdChTemps[ch] < 999.0)
                            currentMaxBearing = gbdChTemps[ch];
                    }
                }
            }

            if (currentMaxBearing <= 0.0)
            {
                string warnMsg = string.Format("【安全啟動攔截】溫度記錄器 (GL820) 已連線，但監控通道目前無有效數據 (最高讀值: {0:F1}℃ <= 0℃)！\n\n請確認熱電偶 (TC) 接線正常或是否選取了正確的監控通道後再啟動。", currentMaxBearing);
                MessageBox.Show(warnMsg, "禁止啟動 - 溫度安全防呆", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            int spdCom = GetHmiKebComIdx(noLoadSpdDrive), spdBaud = GetHmiKebBaudIdx(noLoadSpdDrive);
            int spdNode = (noLoadSpdDrive == 1) ? (int)numHmiKebNode1.Value : (int)numHmiKebNode2.Value;
            string spdName = (noLoadSpdDrive == 1) ? "A載台待測" : "B載台待測";

            // 僅設定待測端變頻器：Mode 9 (全自動速度模式)
            SetHmiKebMode(spdCom, spdBaud, spdNode, 9, spdName + " (空載速度模式)");
            if (noLoadSpdDrive == 1) currentKebMode1 = 9; else currentKebMode2 = 9;
            UpdateHmiKebModeButtonsVisual();

            // 待測端放行 100% 轉矩極限 (cs.18 = 1000)，設定初始目標轉速 (Sy52)，下發啟轉運轉 (Sy50 = 4)
            noLoadCurrentTargetSpd = (double)ratedVal;
            KebWriteParamWithDll(spdCom, spdBaud, spdNode, 0x0F12, 1000);
            KebWriteParam32(spdCom, spdBaud, spdNode, 0x0034, (int)noLoadCurrentTargetSpd, spdName + " 空載轉速 (Sy52)");
            SetHmiKebCommand(spdCom, spdBaud, spdNode, 4, spdName + " 啟轉運轉 (Sy50=4)");

            // 同步主畫面顯示控制項
            if (noLoadSpdDrive == 2)
            {
                if (numHmiKebSpeed2 != null) numHmiKebSpeed2.Value = (decimal)Math.Max(0, Math.Min(6000, noLoadCurrentTargetSpd));
                if (numHmiKebTorque2 != null) numHmiKebTorque2.Value = 0;
            }
            else
            {
                if (numHmiKebSpeed1 != null) numHmiKebSpeed1.Value = (decimal)Math.Max(0, Math.Min(6000, noLoadCurrentTargetSpd));
                if (numHmiKebTorque1 != null) numHmiKebTorque1.Value = 0;
            }

            // 初始化狀態與計時
            isNoLoadRunning = true;
            noLoadCoolingRetryCount = 0;
            noLoadElapsedSec = 0;
            noLoadStageElapsedSec = 0;
            noLoadOverTempDurationSec = 0;
            noLoadCoolingTimerSec = 0;
            noLoadDwellTimerSec = 0;
            noLoadIntermediateDwellSec = (int)numNoLoadStepDwellSec.Value;
            noLoadPostStableCounter = 0;
            noLoadPostStableStage = "";

            if (noLoadTempHistory != null)
            {
                noLoadTempHistory.Clear();
                noLoadTempHistory.TrimExcess();
            }
            if (dgvNoLoad != null) dgvNoLoad.Rows.Clear();
            if (noLoadTempTrend != null) noLoadTempTrend.ClearData();

            // 判斷是否跳過額定轉速熱平衡 (若取消額定轉速，直接以設定轉速進入 Phase 2 階梯升速)
            if (!chkNoLoadRatedTest.Checked)
            {
                noLoadState = (noLoadCurrentTargetSpd >= (double)maxVal) ? NoLoadState.MaxSpeedRun : NoLoadState.IntermediateRun;
                string notice = string.Format("【跳過額定熱平衡】以起始轉速 {0:F0} rpm 直接進入提速階段！", noLoadCurrentTargetSpd);
                AddNoLoadLog(DateTime.Now.ToString("HH:mm:ss"), "0", "略過額定", noLoadCurrentTargetSpd, 0, currentMaxBearing, 0, notice);
                WriteHmiLog("NOLOAD_SKIP_RATED", notice);

                webRemoteStatusText = string.Format("【空載-階梯提速】目標 {0:F0} rpm (跳過額定熱平衡)", noLoadCurrentTargetSpd);
                webRemotePhaseText = "階梯提速中";
                lblNoLoadStateBadge.Text = string.Format("【Phase 2: 階梯提速】目標: {0:F0} rpm", noLoadCurrentTargetSpd);
                lblNoLoadStateBadge.ForeColor = Color.FromArgb(79, 70, 229);
            }
            else
            {
                noLoadState = NoLoadState.RatedWarmup;
                webRemoteStatusText = string.Format("【空載-額定轉速】目標 {0:F0} rpm (比對 30min 溫差 < 1℃)", noLoadCurrentTargetSpd);
                webRemotePhaseText = "額定轉速溫升中";
                lblNoLoadStateBadge.Text = "【Phase 1: 額定轉速熱平衡】運轉中...";
                lblNoLoadStateBadge.ForeColor = Color.FromArgb(2, 132, 199);
            }

            // 若尚未啟動硬體遙測輪詢，自動連鎖啟動
            if (!isRunning) BtnStart_Click(null, null);

            // 自動啟動 RAW DATA 遙測記錄
            StartAutoRawRecordingWithTag("NoLoad");

            btnStartNoLoad.Enabled = false;
            btnStopNoLoad.Enabled = true;

            lblNoLoadTargetSpdDisp.Text = string.Format("{0:F0} rpm", noLoadCurrentTargetSpd);

            AddNoLoadLog(DateTime.Now.ToString("HH:mm:ss"), "0", "啟動測試", noLoadCurrentTargetSpd, 0, currentMaxBearing, 0, "啟動空載測試成功！");
            WriteHmiLog("NOLOAD_START", string.Format("【開始空載測試】待測={0}, 執行額定={1}, 執行最高速={2}, 額定轉速={3:F0} rpm, 最高轉速={4:F0} rpm, 梯度={5:F0} rpm, 閥值={6:F1}℃(Ts={7}s, Tn={8}s)",
                spdName, chkNoLoadRatedTest.Checked, chkNoLoadMaxSpdTest.Checked, ratedVal, maxVal, numNoLoadStepSpd.Value, numNoLoadTempLimit.Value, numNoLoadTimeTs.Value, numNoLoadTimeTn.Value));

            if (noLoadTimer != null) noLoadTimer.Start();
        }

        private void StopNoLoadTest(string reason = null)
        {
            if (!isNoLoadRunning && noLoadState == NoLoadState.Idle) return;

            isNoLoadRunning = false;
            noLoadState = NoLoadState.Idle;
            if (noLoadTimer != null) noLoadTimer.Stop();

            // 停止 RAW DATA 記錄
            if (isManualRecording)
            {
                StopManualRecording(showPrompt: false);
            }

            string stopReason = string.IsNullOrEmpty(reason) ? "空載測試終止" : reason;
            lblNoLoadStateBadge.Text = "【測試停止】" + stopReason;
            lblNoLoadStateBadge.ForeColor = Color.FromArgb(239, 68, 68);

            // 同步遠端 Web Server 狀態
            webRemoteMode = "IDLE";
            webRemoteStatusText = "空載測試已停止";
            webRemotePhaseText = "待機";

            // 安全漸進平滑停機
            StartGradualAutoStop(noLoadSpdDrive, noLoadTrqDrive, stopReason, () => {
                btnStartNoLoad.Enabled = true;
                btnStopNoLoad.Enabled = false;
                lblNoLoadTargetSpdDisp.Text = "0 rpm";
                if (noLoadTempHistory != null)
                {
                    noLoadTempHistory.TrimExcess();
                }
                GC.Collect(1, GCCollectionMode.Optimized);
            });

            AddNoLoadLog(DateTime.Now.ToString("HH:mm:ss"), noLoadElapsedSec.ToString(), "停止", 0, 0, 0, 0, stopReason);
            WriteHmiLog("NOLOAD_STOP", "【空載測試停止】" + stopReason);
        }

        #endregion

        #region 狀態機每秒輪詢處理 (NoLoadTimer_Tick)

        private void NoLoadTimer_Tick(object sender, EventArgs e)
        {
            if (!isNoLoadRunning) return;
            try
            {
                noLoadElapsedSec++;
                noLoadStageElapsedSec++;

                DateTime now = DateTime.Now;
                int elapsedHours = noLoadElapsedSec / 3600;
                int elapsedMins = (noLoadElapsedSec % 3600) / 60;
                int elapsedSecs = noLoadElapsedSec % 60;
                lblNoLoadElapsedDisp.Text = string.Format("總時間: {0:D2}:{1:D2}:{2:D2}", elapsedHours, elapsedMins, elapsedSecs);

                // 1. 取得實測轉速 (空載無回授訊號時友善顯示無回授)
                double actSpd = Math.Abs(actSpeed != 0.0 ? actSpeed : smoothedSpeed);
                lblNoLoadActSpdDisp.Text = (actSpd > 0.0) ? string.Format("{0:F0} rpm", actSpd) : "-- rpm (無回授)";

                // 2. 採樣 20 通道溫度快照
                double[] chSnapshot = new double[20];
                if (gbdChTemps != null && gbdChTemps.Length >= 20)
                    Array.Copy(gbdChTemps, chSnapshot, 20);

                // 3. 計算已監控軸承通道之最高溫度
                double maxBearingTemp = -999.0;
                int maxBearingCh = -1;
                bool hasAnyChChecked = false;
                for (int ch = 0; ch < 20; ch++)
                {
                    if (noLoadMonitoredChannels != null && ch < noLoadMonitoredChannels.Length && noLoadMonitoredChannels[ch])
                    {
                        hasAnyChChecked = true;
                        double t = chSnapshot[ch];
                        if (t > maxBearingTemp && t < 999.0)
                        {
                            maxBearingTemp = t;
                            maxBearingCh = ch;
                        }
                    }
                }
                if (!hasAnyChChecked)
                {
                    maxBearingTemp = chSnapshot[0];
                    maxBearingCh = 0;
                }

                string maxTempStr = (maxBearingTemp > -100.0) ? string.Format("{0:F1} ℃ (CH{1})", maxBearingTemp, maxBearingCh + 1) : "--.- ℃";
                lblNoLoadMaxTempDisp.Text = maxTempStr;

                // 餵入多通道趨勢圖 (各選定通道獨立彩色曲線、端點即時標記與圖例)
                if (noLoadTempTrend != null && chSnapshot != null)
                {
                    noLoadTempTrend.AddSample(now, chSnapshot);
                }

                // 保存溫度歷史至滑動佇列 (保存 45 分鐘)
                if (noLoadTempHistory != null)
                {
                    noLoadTempHistory.Add(new KeyValuePair<DateTime, double[]>(now, chSnapshot));
                    DateTime expireTime = now.AddMinutes(-45);
                    noLoadTempHistory.RemoveAll(x => x.Key < expireTime);
                }

                // 4. 計算 30 分鐘溫差 (熱平衡判定依據) - 原生非 LINQ 高效掃描，零 Heap 額外配置
                DateTime thirtyMinAgo = now.AddMinutes(-30);
                KeyValuePair<DateTime, double[]> baselineSample = default(KeyValuePair<DateTime, double[]>);
                if (noLoadTempHistory != null && noLoadTempHistory.Count > 0)
                {
                    double bestDiffSec = 45.0;
                    for (int i = 0; i < noLoadTempHistory.Count; i++)
                    {
                        double diffSec = Math.Abs((noLoadTempHistory[i].Key - thirtyMinAgo).TotalSeconds);
                        if (diffSec <= bestDiffSec)
                        {
                            bestDiffSec = diffSec;
                            baselineSample = noLoadTempHistory[i];
                        }
                        else if (noLoadTempHistory[i].Key > thirtyMinAgo && diffSec > bestDiffSec)
                        {
                            break; // 歷史佇列依時間遞增，偏離後即可提前結束
                        }
                    }
                }

            int stageMins = noLoadStageElapsedSec / 60;
            int stageSecs = noLoadStageElapsedSec % 60;
            bool isThermalBalanced = false;
            double maxDeltaT = 0.0;
            int maxDeltaCh = -1;

            if (noLoadStageElapsedSec < 1800 || baselineSample.Value == null)
            {
                int warmupSec = Math.Min(1800, noLoadStageElapsedSec);
                lblNoLoadDeltaTDisp.Text = string.Format("累積中 ({0:D2}:{1:D2}/30:00)", stageMins, stageSecs);
                lblNoLoadDeltaTDisp.ForeColor = Color.FromArgb(139, 92, 246);
                prgNoLoad.Maximum = 1800;
                prgNoLoad.Value = warmupSec;
            }
            else
            {
                prgNoLoad.Value = 1800;
                isThermalBalanced = true;
                for (int ch = 0; ch < 20; ch++)
                {
                    if (noLoadMonitoredChannels != null && ch < noLoadMonitoredChannels.Length && noLoadMonitoredChannels[ch])
                    {
                        double tNow = chSnapshot[ch];
                        double tOld = baselineSample.Value[ch];
                        double diff = Math.Abs(tNow - tOld);
                        if (diff > maxDeltaT)
                        {
                            maxDeltaT = diff;
                            maxDeltaCh = ch;
                        }
                        if (diff >= 1.0)
                        {
                            isThermalBalanced = false;
                        }
                    }
                }

                lblNoLoadDeltaTDisp.Text = string.Format("最大ΔT: {0:F2}℃ (CH{1}) {2}", maxDeltaT, maxDeltaCh + 1, isThermalBalanced ? "★達標" : "比對中");
                lblNoLoadDeltaTDisp.ForeColor = isThermalBalanced ? Color.FromArgb(16, 185, 129) : Color.FromArgb(234, 88, 12);
            }

            // 5. 取得使用者設定閥值
            double tempLimit = (double)numNoLoadTempLimit.Value;
            int limitTs = (int)numNoLoadTimeTs.Value;
            int limitTn = (int)numNoLoadTimeTn.Value;
            double stepSpd = (double)numNoLoadStepSpd.Value;
            double ratedSpd = (double)numNoLoadRatedSpd.Value;
            double maxSpd = (double)numNoLoadMaxSpd.Value;

            int spdCom = GetHmiKebComIdx(noLoadSpdDrive), spdBaud = GetHmiKebBaudIdx(noLoadSpdDrive);
            int spdNode = (noLoadSpdDrive == 1) ? (int)numHmiKebNode1.Value : (int)numHmiKebNode2.Value;

            // =========================================================
            // 核心狀態機邏輯處理
            // =========================================================
            switch (noLoadState)
            {
                // -----------------------------------------------------
                // Phase 1: 額定轉速運轉與熱平衡比對
                // -----------------------------------------------------
                case NoLoadState.RatedWarmup:
                    lblNoLoadStateBadge.Text = string.Format("【Phase 1: 額定轉速熱平衡】目標: {0:F0} rpm", noLoadCurrentTargetSpd);
                    lblNoLoadStateBadge.ForeColor = Color.FromArgb(2, 132, 199);

                    // 檢查是否超出溫度閥值
                    if (maxBearingTemp >= tempLimit)
                    {
                        noLoadOverTempDurationSec++;
                        lblNoLoadCountdownDisp.Text = string.Format("🚨 溫度超溫警戒 ({0:F1}℃ >= {1:F1}℃)！持續時間: {2}s / {3}s",
                            maxBearingTemp, tempLimit, noLoadOverTempDurationSec, limitTs);
                        lblNoLoadCountdownDisp.ForeColor = Color.FromArgb(220, 38, 38);

                        // 連續超過 Ts 時間 ➔ 觸發過溫降速 (依梯度之一半減速)
                        if (noLoadOverTempDurationSec >= limitTs)
                        {
                            double decelStep = Math.Max(50.0, stepSpd / 2.0);
                            noLoadCurrentTargetSpd = Math.Max(100.0, noLoadCurrentTargetSpd - decelStep);
                            KebWriteParam32(spdCom, spdBaud, spdNode, 0x0034, (int)noLoadCurrentTargetSpd, "額定轉速超溫降速(梯度一半)");
                            lblNoLoadTargetSpdDisp.Text = string.Format("{0:F0} rpm", noLoadCurrentTargetSpd);

                            noLoadCoolingRetryCount++;
                            noLoadState = NoLoadState.CoolingWait;
                            noLoadCoolingTimerSec = limitTn;
                            noLoadOverTempDurationSec = 0;

                            string ev = string.Format("額定轉速超溫達 {0}s (第 {1} 次降速重試)，依梯度一半降速 -{2:F0} rpm ➔ {3:F0} rpm，等待 Tn={4}s 冷卻評估",
                                limitTs, noLoadCoolingRetryCount, decelStep, noLoadCurrentTargetSpd, limitTn);
                            AddNoLoadLog(now.ToString("HH:mm:ss"), noLoadElapsedSec.ToString(), "過溫降速", noLoadCurrentTargetSpd, actSpd, maxBearingTemp, maxDeltaT, ev);
                            WriteHmiLog("NOLOAD_OVERTEMP", "【額定轉速過溫降速】" + ev);
                            return;
                        }
                    }
                    else
                    {
                        noLoadOverTempDurationSec = 0;
                        lblNoLoadCountdownDisp.Text = string.Format("溫度正常 ({0:F1}℃ < {1:F1}℃) | 階段累積: {2:D2}:{3:D2} / 30:00",
                            maxBearingTemp, tempLimit, stageMins, stageSecs);
                        lblNoLoadCountdownDisp.ForeColor = Color.FromArgb(16, 185, 129);
                    }

                    // 熱平衡判定：滿 30 分鐘且各通道溫差 < 1.0℃ ➔ 額定轉速達成！(標記點 1)
                    if (isThermalBalanced)
                    {
                        MarkNoLoadPreStableRows("額定");
                        string ev = string.Format("★ 額定轉速熱平衡達標！30min最大溫差={0:F2}℃ (<1.0℃)", maxDeltaT);
                        AddNoLoadLog(now.ToString("HH:mm:ss"), noLoadElapsedSec.ToString(), "★額定達標★", noLoadCurrentTargetSpd, actSpd, maxBearingTemp, maxDeltaT, "🎯【★ 額定轉速 30min 熱平衡達標核心點 ★】" + ev);
                        WriteHmiLog("NOLOAD_RATED_PASS", ev);
                        WriteNoLoadCsvMilestoneMarker(now, "【標記點1】額定轉速熱平衡達標", maxDeltaT);
                        noLoadPostStableStage = "額定";
                        noLoadPostStableCounter = 1;

                        // 檢查是否只執行額定轉速測試
                        if (!chkNoLoadMaxSpdTest.Checked)
                        {
                            noLoadState = NoLoadState.Completed;
                            AddNoLoadLog(now.ToString("HH:mm:ss"), noLoadElapsedSec.ToString(), "測試完成", noLoadCurrentTargetSpd, actSpd, maxBearingTemp, maxDeltaT, "🎉 僅執行額定轉速溫升測試，已順利完成熱平衡，安全停機結案！");
                            WriteHmiLog("NOLOAD_COMPLETE", "【空載測試完成】額定轉速熱平衡達標，依設定結案停機。");
                            StopNoLoadTest("額定轉速溫升測試順利完成！");
                            return;
                        }

                        // 提速進入 Phase 2 (加速至最高轉速)
                        double nextSpd = Math.Min(maxSpd, noLoadCurrentTargetSpd + stepSpd);
                        noLoadCurrentTargetSpd = nextSpd;
                        KebWriteParam32(spdCom, spdBaud, spdNode, 0x0034, (int)noLoadCurrentTargetSpd, "梯度加速");
                        lblNoLoadTargetSpdDisp.Text = string.Format("{0:F0} rpm", noLoadCurrentTargetSpd);

                        noLoadStageElapsedSec = 0;
                        noLoadDwellTimerSec = 0;

                        if (noLoadCurrentTargetSpd >= maxSpd)
                        {
                            noLoadState = NoLoadState.MaxSpeedRun;
                            AddNoLoadLog(now.ToString("HH:mm:ss"), noLoadElapsedSec.ToString(), "最高轉速", noLoadCurrentTargetSpd, actSpd, maxBearingTemp, 0, "達到最高轉速，開始最終熱平衡監控！");
                        }
                        else
                        {
                            noLoadState = NoLoadState.IntermediateRun;
                            AddNoLoadLog(now.ToString("HH:mm:ss"), noLoadElapsedSec.ToString(), "階梯升速", noLoadCurrentTargetSpd, actSpd, maxBearingTemp, 0, string.Format("提速至階梯 {0:F0} rpm", noLoadCurrentTargetSpd));
                        }
                    }
                    break;

                // -----------------------------------------------------
                // 中間階梯轉速監控與過渡
                // -----------------------------------------------------
                case NoLoadState.IntermediateRun:
                    lblNoLoadStateBadge.Text = string.Format("【Phase 2: 階梯轉速運轉】目標: {0:F0} rpm", noLoadCurrentTargetSpd);
                    lblNoLoadStateBadge.ForeColor = Color.FromArgb(79, 70, 229);

                    // 檢查超溫
                    if (maxBearingTemp >= tempLimit)
                    {
                        noLoadOverTempDurationSec++;
                        lblNoLoadCountdownDisp.Text = string.Format("🚨 階梯超溫警戒 ({0:F1}℃ >= {1:F1}℃)！持續時間: {2}s / {3}s",
                            maxBearingTemp, tempLimit, noLoadOverTempDurationSec, limitTs);
                        lblNoLoadCountdownDisp.ForeColor = Color.FromArgb(220, 38, 38);

                        if (noLoadOverTempDurationSec >= limitTs)
                        {
                            double decelStep = Math.Max(50.0, stepSpd / 2.0);
                            noLoadCurrentTargetSpd = Math.Max(ratedSpd, noLoadCurrentTargetSpd - decelStep);
                            KebWriteParam32(spdCom, spdBaud, spdNode, 0x0034, (int)noLoadCurrentTargetSpd, "階梯超溫降速(梯度一半)");
                            lblNoLoadTargetSpdDisp.Text = string.Format("{0:F0} rpm", noLoadCurrentTargetSpd);

                            noLoadCoolingRetryCount++;
                            noLoadState = NoLoadState.CoolingWait;
                            noLoadCoolingTimerSec = limitTn;
                            noLoadOverTempDurationSec = 0;

                            string ev = string.Format("階梯轉速超溫達 {0}s (第 {1} 次降速重試)，依梯度一半降速 -{2:F0} rpm ➔ {3:F0} rpm，等待 Tn={4}s 冷卻評估",
                                limitTs, noLoadCoolingRetryCount, decelStep, noLoadCurrentTargetSpd, limitTn);
                            AddNoLoadLog(now.ToString("HH:mm:ss"), noLoadElapsedSec.ToString(), "階梯降速", noLoadCurrentTargetSpd, actSpd, maxBearingTemp, maxDeltaT, ev);
                            WriteHmiLog("NOLOAD_OVERTEMP", "【階梯超溫降速】" + ev);
                            return;
                        }
                    }
                    else
                    {
                        noLoadOverTempDurationSec = 0;
                    }

                    // 停留觀察時間倒數
                    noLoadDwellTimerSec++;
                    lblNoLoadCountdownDisp.Text = string.Format("階梯停留觀察中: {0}s / {1}s", noLoadDwellTimerSec, noLoadIntermediateDwellSec);
                    lblNoLoadCountdownDisp.ForeColor = Color.FromArgb(79, 70, 229);

                    if (noLoadDwellTimerSec >= noLoadIntermediateDwellSec)
                    {
                        noLoadDwellTimerSec = 0;
                        double nextSpd = Math.Min(maxSpd, noLoadCurrentTargetSpd + stepSpd);
                        noLoadCurrentTargetSpd = nextSpd;
                        KebWriteParam32(spdCom, spdBaud, spdNode, 0x0034, (int)noLoadCurrentTargetSpd, "階梯推進加速");
                        lblNoLoadTargetSpdDisp.Text = string.Format("{0:F0} rpm", noLoadCurrentTargetSpd);

                        if (noLoadCurrentTargetSpd >= maxSpd)
                        {
                            noLoadState = NoLoadState.MaxSpeedRun;
                            noLoadStageElapsedSec = 0;
                            AddNoLoadLog(now.ToString("HH:mm:ss"), noLoadElapsedSec.ToString(), "最高轉速", noLoadCurrentTargetSpd, actSpd, maxBearingTemp, 0, "達到最高轉速，開始最終熱平衡監控！");
                        }
                        else
                        {
                            AddNoLoadLog(now.ToString("HH:mm:ss"), noLoadElapsedSec.ToString(), "階梯升速", noLoadCurrentTargetSpd, actSpd, maxBearingTemp, 0, string.Format("提速至階梯 {0:F0} rpm", noLoadCurrentTargetSpd));
                        }
                    }
                    break;

                // -----------------------------------------------------
                // 超溫冷卻等待 (TN 倒數，支援隨時修改上限即時生效)
                // -----------------------------------------------------
                case NoLoadState.CoolingWait:
                    lblNoLoadStateBadge.Text = string.Format("【降速冷卻中】第 {0} 次超溫重試 | 等待 Tn={1}s", noLoadCoolingRetryCount, noLoadCoolingTimerSec);
                    lblNoLoadStateBadge.ForeColor = Color.FromArgb(234, 88, 12);

                    noLoadCoolingTimerSec--;
                    lblNoLoadCountdownDisp.Text = string.Format("❄️ 降速冷卻倒數 {0}s (目前: {1:F1}℃ / 閥值: {2:F1}℃，第 {3} 次重試，可隨時調整溫度上限即時生效)",
                        noLoadCoolingTimerSec, maxBearingTemp, tempLimit, noLoadCoolingRetryCount);
                    lblNoLoadCountdownDisp.ForeColor = Color.FromArgb(234, 88, 12);

                    // 若使用者即時將溫度上限調高 (目前溫度已低於新閥值 - 1.0℃) 且已冷卻至少 5 秒，允許提前結束冷卻恢復升速
                    bool userRaisedLimit = (maxBearingTemp <= tempLimit - 1.0) && (limitTn - noLoadCoolingTimerSec >= 5);

                    if (noLoadCoolingTimerSec <= 0 || userRaisedLimit)
                    {
                        string resumeMsg = userRaisedLimit
                            ? string.Format("檢測到溫度上限已調高即時生效 (目前 {0:F1}℃ < 閥值 {1:F1}℃)，提前結束冷卻，恢復運轉！", maxBearingTemp, tempLimit)
                            : "冷卻等待時間結束，恢復運轉！";

                        lblNoLoadCountdownDisp.Text = resumeMsg;
                        lblNoLoadCountdownDisp.ForeColor = Color.FromArgb(16, 185, 129);

                        if (noLoadCurrentTargetSpd >= maxSpd)
                            noLoadState = NoLoadState.MaxSpeedRun;
                        else if (noLoadCurrentTargetSpd <= ratedSpd && chkNoLoadRatedTest.Checked)
                            noLoadState = NoLoadState.RatedWarmup;
                        else
                            noLoadState = NoLoadState.IntermediateRun;

                        AddNoLoadLog(now.ToString("HH:mm:ss"), noLoadElapsedSec.ToString(), "冷卻結束", noLoadCurrentTargetSpd, actSpd, maxBearingTemp, maxDeltaT, resumeMsg);
                    }
                    break;

                // -----------------------------------------------------
                // Phase 3: 最高轉速運轉與最終熱平衡比對
                // -----------------------------------------------------
                case NoLoadState.MaxSpeedRun:
                    lblNoLoadStateBadge.Text = string.Format("【Phase 3: 最高轉速熱平衡】目標: {0:F0} rpm", noLoadCurrentTargetSpd);
                    lblNoLoadStateBadge.ForeColor = Color.FromArgb(16, 185, 129);

                    // 檢查最高轉速下的超溫保護
                    if (maxBearingTemp >= tempLimit)
                    {
                        noLoadOverTempDurationSec++;
                        lblNoLoadCountdownDisp.Text = string.Format("🚨 最高轉速超溫警戒 ({0:F1}℃ >= {1:F1}℃)！持續時間: {2}s / {3}s",
                            maxBearingTemp, tempLimit, noLoadOverTempDurationSec, limitTs);
                        lblNoLoadCountdownDisp.ForeColor = Color.FromArgb(220, 38, 38);

                        if (noLoadOverTempDurationSec >= limitTs)
                        {
                            double decelStep = Math.Max(50.0, stepSpd / 2.0);
                            noLoadCurrentTargetSpd = Math.Max(ratedSpd, noLoadCurrentTargetSpd - decelStep);
                            KebWriteParam32(spdCom, spdBaud, spdNode, 0x0034, (int)noLoadCurrentTargetSpd, "最高轉速超溫降速(梯度一半)");
                            lblNoLoadTargetSpdDisp.Text = string.Format("{0:F0} rpm", noLoadCurrentTargetSpd);

                            noLoadCoolingRetryCount++;
                            noLoadState = NoLoadState.CoolingWait;
                            noLoadCoolingTimerSec = limitTn;
                            noLoadOverTempDurationSec = 0;

                            string ev = string.Format("最高轉速超溫達 {0}s (第 {1} 次降速重試)，依梯度一半降速 -{2:F0} rpm ➔ {3:F0} rpm，等待 Tn={4}s 冷卻評估",
                                limitTs, noLoadCoolingRetryCount, decelStep, noLoadCurrentTargetSpd, limitTn);
                            AddNoLoadLog(now.ToString("HH:mm:ss"), noLoadElapsedSec.ToString(), "最高速過溫降速", noLoadCurrentTargetSpd, actSpd, maxBearingTemp, maxDeltaT, ev);
                            WriteHmiLog("NOLOAD_OVERTEMP", "【最高轉速過溫降速】" + ev);
                            return;
                        }
                    }
                    else
                    {
                        noLoadOverTempDurationSec = 0;
                        lblNoLoadCountdownDisp.Text = string.Format("最高轉速熱平衡監控中 | 階段累積: {0:D2}:{1:D2} / 30:00 (ΔT < 1.0℃ 結案)", stageMins, stageSecs);
                        lblNoLoadCountdownDisp.ForeColor = Color.FromArgb(16, 185, 129);
                    }

                    // 最終熱平衡判定：最高轉速滿 30 分鐘且各通道溫差 < 1.0℃ ➔ 標記點 2 達成！
                    if (isThermalBalanced)
                    {
                        MarkNoLoadPreStableRows("最高速");
                        string ev = string.Format("★ 最高轉速熱平衡達標！30min最大溫差={0:F2}℃ (<1.0℃)，保持運轉採樣穩定後 5 筆驗證數據！", maxDeltaT);
                        AddNoLoadLog(now.ToString("HH:mm:ss"), noLoadElapsedSec.ToString(), "★最高速達標★", noLoadCurrentTargetSpd, actSpd, maxBearingTemp, maxDeltaT, "🎯【★ 最高轉速 30min 熱平衡達標核心點 ★】" + ev);
                        WriteHmiLog("NOLOAD_MAXSPD_PASS", ev);
                        WriteNoLoadCsvMilestoneMarker(now, "【標記點2】最高轉速熱平衡達標", maxDeltaT);
                        noLoadPostStableStage = "最高速";
                        noLoadPostStableCounter = 1;

                        noLoadState = NoLoadState.MaxSpeedPostHold;
                        noLoadDwellTimerSec = 0;
                        lblNoLoadStateBadge.Text = "【標記點2: 最高速達標】採樣後續 5 筆確認數據中...";
                        lblNoLoadStateBadge.ForeColor = Color.FromArgb(16, 185, 129);
                    }
                    break;

                // -----------------------------------------------------
                // Phase 3 達標後續 5 筆採樣驗證階段
                // -----------------------------------------------------
                case NoLoadState.MaxSpeedPostHold:
                    lblNoLoadStateBadge.Text = string.Format("【最高速達標驗證】採樣後續數據 ({0}/5 筆)", Math.Max(0, noLoadPostStableCounter - 1));
                    lblNoLoadStateBadge.ForeColor = Color.FromArgb(16, 185, 129);
                    lblNoLoadCountdownDisp.Text = string.Format("最高轉速達標確認採樣中: 第 {0} / 5 筆 (目前溫差: {1:F2}℃)", Math.Min(5, noLoadPostStableCounter), maxDeltaT);
                    lblNoLoadCountdownDisp.ForeColor = Color.FromArgb(16, 185, 129);

                    noLoadDwellTimerSec++;
                    // 每 5 秒採樣 1 筆後續穩定樣本
                    if (noLoadDwellTimerSec >= 5)
                    {
                        noLoadDwellTimerSec = 0;
                        AddNoLoadLog(now.ToString("HH:mm:ss"), noLoadElapsedSec.ToString(), "最高速溫升", noLoadCurrentTargetSpd, actSpd, maxBearingTemp, maxDeltaT, "最高速熱平衡達標後續驗證樣本");

                        // 滿 5 筆採樣後結案
                        if (noLoadPostStableCounter > 5)
                        {
                            noLoadState = NoLoadState.Completed;
                            noLoadTimer.Stop();

                            string completeMsg = string.Format("🎉 空載溫升測試全流程圓滿完成！\r\n額定轉速與最高轉速 ({0:F0} rpm) 雙標記點皆達成 30 分鐘熱平衡 (實測最大ΔT = {1:F2}℃)，前後各 5 筆驗證數據已完整留存！",
                                maxSpd, maxDeltaT);
                            AddNoLoadLog(now.ToString("HH:mm:ss"), noLoadElapsedSec.ToString(), "測試完成", noLoadCurrentTargetSpd, actSpd, maxBearingTemp, maxDeltaT, "★雙標記點達成，空載測試圓滿完成！");
                            WriteHmiLog("NOLOAD_COMPLETE", completeMsg);

                            lblNoLoadStateBadge.Text = "【測試圓滿完成】雙標記點熱平衡達標！";
                            lblNoLoadStateBadge.ForeColor = Color.FromArgb(16, 185, 129);

                            if (isManualRecording)
                            {
                                StopManualRecording(showPrompt: false);
                            }

                            StartGradualAutoStop(noLoadSpdDrive, noLoadTrqDrive, "空載測試雙標記點達標完成", () => {
                                btnStartNoLoad.Enabled = true;
                                btnStopNoLoad.Enabled = false;
                                lblNoLoadTargetSpdDisp.Text = "0 rpm";
                                MessageBox.Show(completeMsg, "空載測試完成", MessageBoxButtons.OK, MessageBoxIcon.Information);
                            });
                        }
                    }
                    break;
            }

            // 定期記錄行至表格 (每 30 秒記錄一次定期樣本，非後續採樣驗證時)
            if (noLoadState != NoLoadState.MaxSpeedPostHold && noLoadElapsedSec % 30 == 0)
            {
                string pName = (noLoadState == NoLoadState.RatedWarmup) ? "額定溫升" :
                               ((noLoadState == NoLoadState.IntermediateRun) ? "階梯運轉" :
                               ((noLoadState == NoLoadState.CoolingWait) ? "降速冷卻" :
                               ((noLoadState == NoLoadState.MaxSpeedRun) ? "最高速溫升" : "運轉中")));
                AddNoLoadLog(now.ToString("HH:mm:ss"), noLoadElapsedSec.ToString(), pName, noLoadCurrentTargetSpd, actSpd, maxBearingTemp, maxDeltaT, "定期週期採樣");
            }

            // 同步遠端 Web Server
            string actSpdStr = (actSpd > 0.0) ? string.Format("{0:F0} rpm", actSpd) : "無回授";
            webRemoteStatusText = string.Format("【空載-{0}】目標 {1:F0} rpm (實測: {2}, 最高溫 {3:F1}℃)",
                (noLoadState == NoLoadState.RatedWarmup) ? "額定" : ((noLoadState == NoLoadState.MaxSpeedRun) ? "最高速" : "階梯"),
                noLoadCurrentTargetSpd, actSpdStr, maxBearingTemp);
            webRemotePhaseText = string.Format("{0} | ΔT={1:F2}℃",
                (noLoadState == NoLoadState.RatedWarmup) ? "額定熱平衡比對" : ((noLoadState == NoLoadState.CoolingWait) ? "超溫冷卻等待" : "最高速熱平衡比對"),
                maxDeltaT);
            }
            catch (Exception ex)
            {
                WriteHmiLog("NOLOAD_ERR", "【空載狀態機異常】" + ex.Message);
            }
        }

        private void MarkNoLoadPreStableRows(string stageTag)
        {
            if (dgvNoLoad == null || dgvNoLoad.IsDisposed || dgvNoLoad.Rows.Count == 0) return;
            try
            {
                if (dgvNoLoad.InvokeRequired)
                {
                    dgvNoLoad.BeginInvoke(new Action(() => MarkNoLoadPreStableRows(stageTag)));
                    return;
                }

                int totalRows = dgvNoLoad.Rows.Count;
                int markedCount = 0;
                // 從最後一筆往前回溯最多 5 筆前導資料行 (排除達標核心點)
                for (int i = totalRows - 1; i >= 0 && markedCount < 5; i--)
                {
                    DataGridViewRow row = dgvNoLoad.Rows[i];
                    string ev = Convert.ToString(row.Cells["Event"].Value ?? "");
                    if (ev.Contains("達標核心點") || ev.Contains("★額定達標★") || ev.Contains("★最高速達標★"))
                        continue;

                    markedCount++;
                    int offsetIdx = markedCount; // 1 = 達標前一筆, 5 = 達標前五筆
                    if (!ev.Contains("穩定前-"))
                    {
                        row.Cells["Event"].Value = string.Format("[{0}穩定前-{1}] {2}", stageTag, offsetIdx, ev);
                    }
                    // 暖琥珀金色背景與深琥珀粗體字
                    row.DefaultCellStyle.BackColor = Color.FromArgb(254, 243, 199);
                    row.DefaultCellStyle.ForeColor = Color.FromArgb(146, 64, 14);
                    row.DefaultCellStyle.Font = new Font("微軟正黑體", 9.5f, FontStyle.Bold);
                }
            }
            catch { }
        }

        private void WriteNoLoadCsvMilestoneMarker(DateTime now, string title, double maxDeltaT)
        {
            try
            {
                lock (manualRecordLock)
                {
                    if (manualRecordWriter != null && isManualRecording)
                    {
                        manualRecordWriter.WriteLine("# ========================================================================================");
                        manualRecordWriter.WriteLine(string.Format("# ★★★ [MILESTONE] 30MIN_THERMAL_BALANCED_ACHIEVED: {0} | ΔT={1:F2}℃ | Time={2:yyyy-MM-dd HH:mm:ss} ★★★", title, maxDeltaT, now));
                        manualRecordWriter.WriteLine("# ========================================================================================");
                        manualRecordWriter.Flush();
                    }
                }
            }
            catch { }
        }

        private void ExportNoLoadGridToCsv()
        {
            if (dgvNoLoad == null || dgvNoLoad.Rows.Count == 0)
            {
                MessageBox.Show("目前無任何空載測試數據可供匯出！", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            try
            {
                using (SaveFileDialog sfd = new SaveFileDialog())
                {
                    sfd.Filter = "CSV 日誌檔案 (*.csv)|*.csv|所有檔案 (*.*)|*.*";
                    sfd.FileName = string.Format("NoLoad_Test_Log_{0}.csv", DateTime.Now.ToString("yyyyMMdd_HHmmss"));
                    sfd.Title = "匯出空載溫升測試紀錄";

                    if (sfd.ShowDialog() == DialogResult.OK)
                    {
                        using (StreamWriter sw = new StreamWriter(sfd.FileName, false, Encoding.UTF8))
                        {
                            // 欄位標題
                            StringBuilder headerSb = new StringBuilder();
                            for (int i = 0; i < dgvNoLoad.Columns.Count; i++)
                            {
                                headerSb.Append("\"" + dgvNoLoad.Columns[i].HeaderText.Replace("\"", "\"\"") + "\"");
                                if (i < dgvNoLoad.Columns.Count - 1) headerSb.Append(",");
                            }
                            sw.WriteLine(headerSb.ToString());

                            // 逐行寫入
                            for (int r = 0; r < dgvNoLoad.Rows.Count; r++)
                            {
                                DataGridViewRow row = dgvNoLoad.Rows[r];
                                if (row.IsNewRow) continue;

                                StringBuilder rowSb = new StringBuilder();
                                for (int c = 0; c < dgvNoLoad.Columns.Count; c++)
                                {
                                    string val = Convert.ToString(row.Cells[c].Value ?? "");
                                    rowSb.Append("\"" + val.Replace("\"", "\"\"") + "\"");
                                    if (c < dgvNoLoad.Columns.Count - 1) rowSb.Append(",");
                                }
                                sw.WriteLine(rowSb.ToString());
                            }
                        }
                        MessageBox.Show("空載測試日誌已成功匯出至：\r\n" + sfd.FileName, "匯出成功", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("匯出 CSV 失敗: " + ex.Message, "錯誤", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void AddNoLoadLog(string timeStr, string elapsedStr, string phaseStr, double targetSpd, double actSpd, double maxTemp, double deltaT, string eventStr)
        {
            if (dgvNoLoad == null || dgvNoLoad.IsDisposed) return;
            try
            {
                if (dgvNoLoad.InvokeRequired)
                {
                    dgvNoLoad.BeginInvoke(new Action(() => AddNoLoadLog(timeStr, elapsedStr, phaseStr, targetSpd, actSpd, maxTemp, deltaT, eventStr)));
                    return;
                }

                bool isMilestone = eventStr.Contains("達標核心點") || phaseStr.Contains("★額定達標★") || phaseStr.Contains("★最高速達標★");
                bool isPostStable = false;
                int currentPostIdx = 0;

                if (!isMilestone && noLoadPostStableCounter >= 1 && noLoadPostStableCounter <= 5)
                {
                    isPostStable = true;
                    currentPostIdx = noLoadPostStableCounter;
                    string prefix = string.IsNullOrEmpty(noLoadPostStableStage) ? "" : noLoadPostStableStage;
                    eventStr = string.Format("[{0}穩定後+{1}] {2}", prefix, currentPostIdx, eventStr);
                    noLoadPostStableCounter++;
                }

                // 長時間運行記憶體防護：限制 dgvNoLoad 最大保留行數，避免 WinForms 表格記憶體膨脹與介面卡死
                // 當累積超過 3000 行時，自頂部批次移除最舊的 500 行，但永久保留 Milestone 達標里程碑與穩定後驗證行！
                if (dgvNoLoad.Rows.Count > 3000)
                {
                    int removeCount = 0;
                    int checkIdx = 0;
                    while (removeCount < 500 && checkIdx < dgvNoLoad.Rows.Count - 200)
                    {
                        DataGridViewRow r = dgvNoLoad.Rows[checkIdx];
                        string rEvent = (r.Cells.Count > 7 && r.Cells[7].Value != null) ? r.Cells[7].Value.ToString() : "";
                        string rPhase = (r.Cells.Count > 2 && r.Cells[2].Value != null) ? r.Cells[2].Value.ToString() : "";
                        bool isKept = rEvent.Contains("達標核心點") || rPhase.Contains("★額定達標★") || rPhase.Contains("★最高速達標★") || rEvent.Contains("穩定後");
                        if (!isKept)
                        {
                            dgvNoLoad.Rows.RemoveAt(checkIdx);
                            removeCount++;
                        }
                        else
                        {
                            checkIdx++;
                        }
                    }
                }

                int rowIdx = dgvNoLoad.Rows.Add(
                    timeStr,
                    elapsedStr,
                    phaseStr,
                    string.Format("{0:F0}", targetSpd),
                    (actSpd > 0.0) ? string.Format("{0:F0}", actSpd) : "-- (無回授)",
                    (maxTemp > -100.0) ? string.Format("{0:F1}", maxTemp) : "--.-",
                    (deltaT > 0.0) ? string.Format("{0:F2}", deltaT) : "--",
                    eventStr
                );

                if (rowIdx >= 0 && rowIdx < dgvNoLoad.Rows.Count)
                {
                    DataGridViewRow row = dgvNoLoad.Rows[rowIdx];
                    if (isMilestone)
                    {
                        row.DefaultCellStyle.BackColor = Color.FromArgb(209, 250, 229);
                        row.DefaultCellStyle.ForeColor = Color.FromArgb(6, 95, 70);
                        row.DefaultCellStyle.Font = fontNoLoadMilestone;
                    }
                    else if (isPostStable)
                    {
                        row.DefaultCellStyle.BackColor = Color.FromArgb(236, 253, 245);
                        row.DefaultCellStyle.ForeColor = Color.FromArgb(4, 120, 87);
                        row.DefaultCellStyle.Font = fontNoLoadPostStable;
                    }

                    // 自動滾動至最新列
                    dgvNoLoad.FirstDisplayedScrollingRowIndex = rowIdx;
                }
            }
            catch { }
        }

        #endregion
    }
}
