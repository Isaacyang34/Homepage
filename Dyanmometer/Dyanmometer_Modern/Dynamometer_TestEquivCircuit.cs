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
        #region 等效電路控制項與狀態變數宣告

        public TabPage tabEquiv;
        private SplitContainer splitEquivMain;
        private SplitContainer splitEquivResults;

        // 馬達特徵指紋快取與狀態標籤
        private string cachedEquivDrFingerprint = "";
        private string cachedEquivMotorName = "";
        private Label lblEquivMotorStatus;
        private Button btnEquivRefreshFingerprint;
        private Button btnEquivClearAllData;

        // 項目 1: 空載運轉數據控制項
        private Label lblNoLoadItemStatus;
        private NumericUpDown numEquivV0;
        private NumericUpDown numEquivI0;
        private NumericUpDown numEquivP0;
        private NumericUpDown numEquivPf0;
        private NumericUpDown numEquivN0;
        private NumericUpDown numEquivF0;
        private Button btnEquivLoadNoLoadFromTest;
        private Button btnEquivCaptureLiveNoLoad;
        private bool isNoLoadDataReady = false;

        // 項目 2: 額定運轉數據控制項 (不補轉差)
        private Label lblRatedItemStatus;
        private NumericUpDown numEquivTn;
        private NumericUpDown numEquivNn;
        private NumericUpDown numEquivVn;
        private NumericUpDown numEquivIn;
        private NumericUpDown numEquivPn;
        private NumericUpDown numEquivPfn;
        private NumericUpDown numEquivSlip;
        private Button btnEquivLoadRatedFromTn;
        private Button btnEquivCaptureLiveRated;
        private bool isRatedDataReady = false;

        // 項目 3: 堵轉數據與 KEB uf09 調控控制項
        private Label lblLockedItemStatus;
        private ComboBox cmbEquivKebDrive;
        private Label lblEquivCurUf09;
        private NumericUpDown numEquivTargetUf09;
        private Button btnEquivReadUf09;
        private Button btnEquivWriteUf09;
        private Button btnEquivRevertUf09;
        private NumericUpDown numEquivVk;
        private NumericUpDown numEquivIk;
        private NumericUpDown numEquivPk;
        private NumericUpDown numEquivPfk;
        private Button btnEquivCaptureLiveLocked;
        private bool isLockedDataReady = false;
        private ComboBox cmbEquivLockedFreq;       // 測試頻率選擇 (8 種頻率模式)
        private Button btnEquivAutoTuneUf09;       // [AI] 單頻自適應測試按鈕
        private Button btnEquivSyncCheck;          // [同步檢查] 按鈕
        private Button btnEquivSweepAllFreq;       // [>>] 全頻自動掃描按鈕
        private Button btnEquivViewSweepResults;   // [表] 8頻記錄按鈕
        private Button btnEquivLockedStop;         // [■ 急停] 堵轉緊急停機按鈕
        private Label lblLockedProtStatus;         // [防護] 保護監控與閾值狀態指示
        private System.Windows.Forms.Timer tmrLockedWatchdog; // 堵轉看門狗與自適應調壓定時器
        private bool isLockedRotorActive = false;
        private bool isLockedTripShowing = false; // ★【防彈跳連發重入鎖】保證絕不重複彈出多個 MessageBox
        private int lockedOverCurrentTicks = 0;
        private int lockedSpeedAnomalyTicks = 0;
        private bool isAutoTuningUf09 = false;
        private int autoTuneStage = 0;             // 0:Idle, 1:Probe(5步1V), 2:HalfStep(1/2增量), 3:Verify, 4:BestFit(最小步階逼近)
        private int autoTuneProbeIndex = 0;
        private int autoTuneCurV = 10;
        private double autoTuneSlope = 0.5;
        private int autoTuneBestV = 10;
        private double autoTuneBestDiff = 9999.0;
        private int autoTuneTickCount = 0;
        private List<KeyValuePair<int, double>> autoTuneHistory = new List<KeyValuePair<int, double>>();
        private int originalUf09Val = 260; // 記錄原始 uf09 數值以供安全復歸

        // 8 個頻率點試驗資料模型與執行緒
        private System.Threading.Thread lockedSweepThread;
        private volatile bool isLockedSweepRunning = false;
        private List<LockedFreqSweepItem> lockedSweepItems = new List<LockedFreqSweepItem>();

        public class LockedSamplePoint
        {
            public double V;
            public double I;
            public double P;
            public double PF;
        }

        public class LockedFreqSweepItem
        {
            public int Index;            // 1 ~ 8
            public string FreqName;      // 例如 "(1) 額定頻率"
            public double FreqRatio;     // 1.0, 0.25, 0.30, 0.40, 0.50, 0.60, 2.0, 4.0
            public double TargetFreq;    // 實際目標頻率 (Hz)
            public int StartVoltage;     // 1/10 額定電壓 (V)
            public int EstimatedVoltage; // 5步1V估測目標電壓 (V)
            public int ConvergedUf09;    // 最終收斂 uf09 (V)
            public double AvgVk;         // 去高低各5筆後平均電壓 Vk (V)
            public double AvgIk;         // 去高低各5筆後平均電流 Ik (A)
            public double AvgPk;         // 去高低各5筆後平均功率 Pk (W)
            public double AvgPFk;        // 去高低各5筆後平均因數 PFk
            public double Xk_meas;       // 實測頻率漏抗 (Ω)
            public double Xk_ref;        // 折算回額定頻率漏抗 (Ω)
            public double Lk_mH;         // 換算漏電感 (mH)
            public bool IsCompleted;     // 是否已完成
            public DateTime TestTime;    // 測試時間
        }

        // 等效電路計算與結果呈現控制項
        private NumericUpDown numEquivStatorR1;
        private CheckBox chkAutoR1Distribute;
        private Button btnEquivCalculate;
        private Button btnEquivCopyResults;
        private Button btnEquivExportCsv;
        private DataGridView dgvEquivResults;
        private Panel pnlEquivDiagram;

        // 等效電路計算結果快取結構
        public class EquivCircuitResult
        {
            public double R1 = 0.0;       // 定子相電阻 (Ω)
            public double X1 = 0.0;       // 定子漏電抗 (Ω)
            public double Xm = 0.0;       // 激磁電抗 (Ω)
            public double Rc = 0.0;       // 鐵損等效電阻 (Ω)
            public double R2_prime = 0.0; // 轉子折算相電阻 (Ω)
            public double X2_prime = 0.0; // 轉子折算漏電抗 (Ω)
            public double Zk = 0.0;       // 堵轉阻抗 (Ω)
            public double Rk = 0.0;       // 堵轉電阻 (Ω)
            public double Xk = 0.0;       // 堵轉漏抗 (Ω)
            public double L1_mH = 0.0;       // 定子漏電感 (mH)
            public double Lm_mH = 0.0;       // 激磁電感 (mH)
            public double L2_prime_mH = 0.0; // 轉子折算漏電感 (mH)
            public double Lk_mH = 0.0;       // 堵轉漏電感 (mH)
            public double Z0 = 0.0;       // 空載阻抗 (Ω)
            public double RatedSlip = 0.0;// 額定轉差率 (%)
            public double T_start = 0.0;  // 推估啟動轉矩 (Nm)
            public double T_max = 0.0;    // 推估最大崩潰轉矩 (Nm)
            public double T_start_ratio = 0.0; // 啟動轉矩倍數 (Tst / Tn)
            public double T_max_ratio = 0.0;   // 崩潰轉矩倍數 (Tmax / Tn)
            public double I_start = 0.0;  // 推估啟動電流 (A)
            public double EstEff = 0.0;   // 額定效率 (%)
            public DateTime CalcTime = DateTime.Now;
        }
        private EquivCircuitResult lastEquivResult = null;

        #endregion

        #region 等效電路分頁建置 (UI Layout Robustness - WinXP 相容)

        private void BuildEquivCircuitTab(TabPage tab)
        {
            tab.BackColor = Color.FromArgb(248, 250, 252);

            // 主分割容器：上下兩段式自由拉伸佈局 (預設高度 340px)
            splitEquivMain = new SplitContainer()
            {
                Dock = DockStyle.Fill,
                Orientation = Orientation.Horizontal,
                BackColor = Color.FromArgb(226, 232, 240),
                SplitterWidth = 5
            };

            // 上半部容器：頂部狀態橫條 (44px) + 三大採樣數據卡片 (Dock: Fill)
            Panel pnlTopContainer = new Panel()
            {
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(248, 250, 252),
                Padding = new Padding(6, 4, 6, 2)
            };

            // 頂部狀態橫條：馬達指紋與同動記憶指示 (高度緊湊 44px)
            Panel pnlTopBar = new Panel()
            {
                Dock = DockStyle.Top,
                Height = 44,
                BackColor = Color.White,
                Padding = new Padding(10, 4, 10, 4),
                Margin = new Padding(0, 0, 0, 4)
            };
            pnlTopBar.Paint += (s, e) => {
                using (Pen p = new Pen(Color.FromArgb(226, 232, 240), 1))
                {
                    e.Graphics.DrawRectangle(p, 0, 0, pnlTopBar.Width - 1, pnlTopBar.Height - 1);
                }
            };

            TableLayoutPanel tlpTopBar = new TableLayoutPanel()
            {
                Dock = DockStyle.Fill,
                ColumnCount = 3,
                RowCount = 1
            };
            tlpTopBar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            tlpTopBar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 140f));
            tlpTopBar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 140f));

            lblEquivMotorStatus = new Label()
            {
                Text = "[連線] 待測馬達: 【" + (!string.IsNullOrEmpty(motorModelName) ? motorModelName : "SVM100S") + "】 | B載台 dr 狀態: 讀取中... | 一致性: [O] 同一馬達測試記憶中",
                Font = new Font("微軟正黑體", 10f, FontStyle.Bold),
                ForeColor = Color.FromArgb(15, 23, 42),
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft
            };

            btnEquivRefreshFingerprint = new Button()
            {
                Text = "[->] 刷新狀態",
                Font = new Font("微軟正黑體", 9f, FontStyle.Bold),
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(241, 245, 249),
                ForeColor = Color.FromArgb(30, 41, 59),
                FlatStyle = FlatStyle.Flat,
                Margin = new Padding(3)
            };
            btnEquivRefreshFingerprint.FlatAppearance.BorderColor = Color.FromArgb(203, 213, 225);
            btnEquivRefreshFingerprint.Click += (s, e) => RefreshEquivMotorStatus();

            btnEquivClearAllData = new Button()
            {
                Text = "[X] 清除重測",
                Font = new Font("微軟正黑體", 9f, FontStyle.Bold),
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(254, 242, 242),
                ForeColor = Color.FromArgb(220, 38, 38),
                FlatStyle = FlatStyle.Flat,
                Margin = new Padding(3)
            };
            btnEquivClearAllData.FlatAppearance.BorderColor = Color.FromArgb(252, 165, 165);
            btnEquivClearAllData.Click += (s, e) => {
                if (MessageBox.Show("確定要清除當前分頁已記憶的 1.空載、2.額定、3.堵轉 採樣數據嗎？\r\n(更換全新馬達測試時請確認清除)", "清除確認", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
                {
                    ResetEquivDataCards();
                    SaveLayoutConfig();
                }
            };

            tlpTopBar.Controls.Add(lblEquivMotorStatus, 0, 0);
            tlpTopBar.Controls.Add(btnEquivRefreshFingerprint, 1, 0);
            tlpTopBar.Controls.Add(btnEquivClearAllData, 2, 0);
            pnlTopBar.Controls.Add(tlpTopBar);

            // 中間卡片網格：三大測試數據卡片 (橫向三等分 TableLayoutPanel, Dock: Fill 自適應填滿剩餘高度)
            TableLayoutPanel tlpCards = new TableLayoutPanel()
            {
                Dock = DockStyle.Fill,
                ColumnCount = 3,
                RowCount = 1,
                Margin = new Padding(0, 4, 0, 0)
            };
            tlpCards.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33f));
            tlpCards.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33f));
            tlpCards.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.34f));

            // 建立卡片 1、卡片 2、卡片 3
            InitLockedSweepItems();
            Panel card1 = CreateNoLoadCard();
            Panel card2 = CreateRatedCard();
            Panel card3 = CreateLockedCard();

            tlpCards.Controls.Add(card1, 0, 0);
            tlpCards.Controls.Add(card2, 1, 0);
            tlpCards.Controls.Add(card3, 2, 0);

            pnlTopContainer.Controls.Add(tlpCards);
            pnlTopContainer.Controls.Add(pnlTopBar);

            // 下半部成果分析區：計算控制列 + 表格與電路圖圖解 (左右分割)
            Panel pnlBottomSection = CreateResultsSection();

            splitEquivMain.Panel1.Controls.Add(pnlTopContainer);
            splitEquivMain.Panel2.Controls.Add(pnlBottomSection);

            tab.Controls.Add(splitEquivMain);

            // 註冊安全分割條 (預設距離 340, Panel1 最少 150, Panel2 最少 150)
            SafeSetupSplitContainer(splitEquivMain, "EquivMain", 340, 150, 150);

            // 啟動堵轉測試看門狗與自適應調壓定時器
            if (tmrLockedWatchdog == null)
            {
                tmrLockedWatchdog = new System.Windows.Forms.Timer();
                tmrLockedWatchdog.Interval = 500;
                tmrLockedWatchdog.Tick += TmrLockedWatchdog_Tick;
                tmrLockedWatchdog.Start();
            }
        }

        #endregion

        #region 卡片 1: 空載運轉數據 (No-Load)

        private Panel CreateNoLoadCard()
        {
            Panel card = new Panel()
            {
                Dock = DockStyle.Fill,
                BackColor = Color.White,
                Margin = new Padding(4),
                Padding = new Padding(12)
            };
            card.Paint += (s, e) => {
                using (Pen p = new Pen(Color.FromArgb(226, 232, 240), 1))
                {
                    e.Graphics.DrawRectangle(p, 0, 0, card.Width - 1, card.Height - 1);
                }
            };

            TableLayoutPanel tlp = new TableLayoutPanel()
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 9
            };
            tlp.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 48f));
            tlp.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 52f));
            tlp.RowStyles.Add(new RowStyle(SizeType.Absolute, 24f)); // Row 0: Title
            tlp.RowStyles.Add(new RowStyle(SizeType.Absolute, 22f)); // Row 1: Status
            tlp.RowStyles.Add(new RowStyle(SizeType.Absolute, 26f)); // Row 2: V0
            tlp.RowStyles.Add(new RowStyle(SizeType.Absolute, 26f)); // Row 3: I0
            tlp.RowStyles.Add(new RowStyle(SizeType.Absolute, 26f)); // Row 4: P0
            tlp.RowStyles.Add(new RowStyle(SizeType.Absolute, 26f)); // Row 5: PF0
            tlp.RowStyles.Add(new RowStyle(SizeType.Absolute, 26f)); // Row 6: N0
            tlp.RowStyles.Add(new RowStyle(SizeType.Absolute, 26f)); // Row 7: f0
            tlp.RowStyles.Add(new RowStyle(SizeType.Absolute, 32f)); // Row 8: Buttons

            // 標題行
            Label lblTitle = new Label()
            {
                Text = "1. 空載運轉數據 (No-Load)",
                Font = new Font("微軟正黑體", 11f, FontStyle.Bold),
                ForeColor = Color.FromArgb(3, 105, 161),
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft
            };
            tlp.SetColumnSpan(lblTitle, 2);
            tlp.Controls.Add(lblTitle, 0, 0);

            // 狀態行
            lblNoLoadItemStatus = new Label()
            {
                Text = "[--] 待採樣 (可從空載測試載入或即時抓取)",
                Font = new Font("微軟正黑體", 9f, FontStyle.Bold),
                ForeColor = Color.FromArgb(100, 116, 139),
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft
            };
            tlp.SetColumnSpan(lblNoLoadItemStatus, 2);
            tlp.Controls.Add(lblNoLoadItemStatus, 0, 1);

            // 參數列 (預設值歸零，允許 f0 下限為 0)
            numEquivV0 = AddCardField(tlp, 2, "線電壓 V0 (V):", 0.0m, 1, 0, 1000);
            numEquivI0 = AddCardField(tlp, 3, "線電流 I0 (A):", 0.0m, 2, 0, 500);
            numEquivP0 = AddCardField(tlp, 4, "輸入功率 P0 (W):", 0.0m, 1, 0, 100000);
            numEquivPf0 = AddCardField(tlp, 5, "功率因數 PF0:", 0.0m, 3, 0, 1);
            numEquivN0 = AddCardField(tlp, 6, "實測轉速 N0 (rpm):", 0m, 0, 0, 15000);
            numEquivF0 = AddCardField(tlp, 7, "測試頻率 f0 (Hz):", 0.0m, 2, 0, 500);

            // 操作按鈕行
            TableLayoutPanel tlpBtns = new TableLayoutPanel()
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 1,
                Margin = new Padding(0, 6, 0, 0)
            };
            tlpBtns.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
            tlpBtns.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));

            btnEquivLoadNoLoadFromTest = new Button()
            {
                Text = "[檔案] 載入空載紀錄檔",
                Font = new Font("微軟正黑體", 9f, FontStyle.Bold),
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(240, 249, 255),
                ForeColor = Color.FromArgb(2, 132, 199),
                FlatStyle = FlatStyle.Flat
            };
            btnEquivLoadNoLoadFromTest.FlatAppearance.BorderColor = Color.FromArgb(186, 230, 253);
            btnEquivLoadNoLoadFromTest.Click += (s, e) => LoadNoLoadDataFromTestTab();

            btnEquivCaptureLiveNoLoad = new Button()
            {
                Text = "[寫入] 擷取即時數據",
                Font = new Font("微軟正黑體", 9f, FontStyle.Bold),
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(240, 253, 244),
                ForeColor = Color.FromArgb(22, 163, 74),
                FlatStyle = FlatStyle.Flat
            };
            btnEquivCaptureLiveNoLoad.FlatAppearance.BorderColor = Color.FromArgb(187, 247, 208);
            btnEquivCaptureLiveNoLoad.Click += (s, e) => CaptureLiveNoLoadData();

            tlpBtns.Controls.Add(btnEquivLoadNoLoadFromTest, 0, 0);
            tlpBtns.Controls.Add(btnEquivCaptureLiveNoLoad, 1, 0);

            tlp.SetColumnSpan(tlpBtns, 2);
            tlp.Controls.Add(tlpBtns, 0, 8);

            card.Controls.Add(tlp);
            return card;
        }

        #endregion

        #region 卡片 2: 額定運轉數據 (Rated Load - 不補轉差)

        private Panel CreateRatedCard()
        {
            Panel card = new Panel()
            {
                Dock = DockStyle.Fill,
                BackColor = Color.White,
                Margin = new Padding(4),
                Padding = new Padding(12)
            };
            card.Paint += (s, e) => {
                using (Pen p = new Pen(Color.FromArgb(226, 232, 240), 1))
                {
                    e.Graphics.DrawRectangle(p, 0, 0, card.Width - 1, card.Height - 1);
                }
            };

            TableLayoutPanel tlp = new TableLayoutPanel()
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 10
            };
            tlp.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 48f));
            tlp.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 52f));
            tlp.RowStyles.Add(new RowStyle(SizeType.Absolute, 24f)); // Row 0: Title
            tlp.RowStyles.Add(new RowStyle(SizeType.Absolute, 22f)); // Row 1: Status
            tlp.RowStyles.Add(new RowStyle(SizeType.Absolute, 26f)); // Row 2: TN
            tlp.RowStyles.Add(new RowStyle(SizeType.Absolute, 26f)); // Row 3: NN
            tlp.RowStyles.Add(new RowStyle(SizeType.Absolute, 26f)); // Row 4: VN
            tlp.RowStyles.Add(new RowStyle(SizeType.Absolute, 26f)); // Row 5: IN
            tlp.RowStyles.Add(new RowStyle(SizeType.Absolute, 26f)); // Row 6: PN
            tlp.RowStyles.Add(new RowStyle(SizeType.Absolute, 26f)); // Row 7: PFN
            tlp.RowStyles.Add(new RowStyle(SizeType.Absolute, 26f)); // Row 8: Slip
            tlp.RowStyles.Add(new RowStyle(SizeType.Absolute, 32f)); // Row 9: Buttons

            Label lblTitle = new Label()
            {
                Text = "2. 額定運轉數據 (不補轉差)",
                Font = new Font("微軟正黑體", 11f, FontStyle.Bold),
                ForeColor = Color.FromArgb(15, 118, 110),
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft
            };
            tlp.SetColumnSpan(lblTitle, 2);
            tlp.Controls.Add(lblTitle, 0, 0);

            lblRatedItemStatus = new Label()
            {
                Text = "[--] 待採樣 (可從 T-N 額定點載入或即時抓取)",
                Font = new Font("微軟正黑體", 9f, FontStyle.Bold),
                ForeColor = Color.FromArgb(100, 116, 139),
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft
            };
            tlp.SetColumnSpan(lblRatedItemStatus, 2);
            tlp.Controls.Add(lblRatedItemStatus, 0, 1);

            // 參數列 (預設值歸零)
            numEquivTn = AddCardField(tlp, 2, "額定轉矩 TN (Nm):", 0.0m, 2, 0, 2000);
            numEquivNn = AddCardField(tlp, 3, "實測轉速 NN (rpm):", 0m, 0, 0, 15000);
            numEquivVn = AddCardField(tlp, 4, "額定線壓 VN (V):", 0.0m, 1, 0, 1000);
            numEquivIn = AddCardField(tlp, 5, "額定線流 IN (A):", 0.0m, 2, 0, 500);
            numEquivPn = AddCardField(tlp, 6, "輸入電功率 (kW):", 0.0m, 3, 0, 500);
            numEquivPfn = AddCardField(tlp, 7, "功率因數 PFN:", 0.0m, 3, 0, 1);
            numEquivSlip = AddCardField(tlp, 8, "實測轉差率 s (%):", 0.0m, 2, 0, 100);

            // 操作按鈕行
            TableLayoutPanel tlpBtns = new TableLayoutPanel()
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 1,
                Margin = new Padding(0, 6, 0, 0)
            };
            tlpBtns.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
            tlpBtns.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));

            btnEquivLoadRatedFromTn = new Button()
            {
                Text = "[檔案] 載入 S1 不補轉差檔",
                Font = new Font("微軟正黑體", 9f, FontStyle.Bold),
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(240, 253, 250),
                ForeColor = Color.FromArgb(13, 148, 136),
                FlatStyle = FlatStyle.Flat
            };
            btnEquivLoadRatedFromTn.FlatAppearance.BorderColor = Color.FromArgb(153, 246, 228);
            btnEquivLoadRatedFromTn.Click += (s, e) => LoadRatedDataFromTnTab();

            btnEquivCaptureLiveRated = new Button()
            {
                Text = "[寫入] 擷取即時數據",
                Font = new Font("微軟正黑體", 9f, FontStyle.Bold),
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(240, 253, 244),
                ForeColor = Color.FromArgb(22, 163, 74),
                FlatStyle = FlatStyle.Flat
            };
            btnEquivCaptureLiveRated.FlatAppearance.BorderColor = Color.FromArgb(187, 247, 208);
            btnEquivCaptureLiveRated.Click += (s, e) => CaptureLiveRatedData();

            tlpBtns.Controls.Add(btnEquivLoadRatedFromTn, 0, 0);
            tlpBtns.Controls.Add(btnEquivCaptureLiveRated, 1, 0);

            tlp.SetColumnSpan(tlpBtns, 2);
            tlp.Controls.Add(tlpBtns, 0, 9);

            card.Controls.Add(tlp);
            return card;
        }

        #endregion

        #region 卡片 3: 堵轉數據與 KEB uf09 調控 (Locked-Rotor)

        private Panel CreateLockedCard()
        {
            Panel card = new Panel()
            {
                Dock = DockStyle.Fill,
                BackColor = Color.White,
                Margin = new Padding(4),
                Padding = new Padding(10, 8, 10, 8),
                AutoScroll = true
            };
            card.Paint += (s, e) => {
                using (Pen p = new Pen(Color.FromArgb(226, 232, 240), 1))
                {
                    e.Graphics.DrawRectangle(p, 0, 0, card.Width - 1, card.Height - 1);
                }
            };

            TableLayoutPanel tlp = new TableLayoutPanel()
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 11
            };
            tlp.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 46f));
            tlp.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 54f));
            tlp.RowStyles.Add(new RowStyle(SizeType.Absolute, 24f)); // Row 0: Title
            tlp.RowStyles.Add(new RowStyle(SizeType.Absolute, 22f)); // Row 1: Status
            tlp.RowStyles.Add(new RowStyle(SizeType.Absolute, 26f)); // Row 2: Freq & Drive
            tlp.RowStyles.Add(new RowStyle(SizeType.Absolute, 26f)); // Row 3: uf09 Controls
            tlp.RowStyles.Add(new RowStyle(SizeType.Absolute, 26f)); // Row 4: Multi-Freq Sweep Controls
            tlp.RowStyles.Add(new RowStyle(SizeType.Absolute, 24f)); // Row 5: Protection Status
            tlp.RowStyles.Add(new RowStyle(SizeType.Absolute, 26f)); // Row 6: Vk
            tlp.RowStyles.Add(new RowStyle(SizeType.Absolute, 26f)); // Row 7: Ik
            tlp.RowStyles.Add(new RowStyle(SizeType.Absolute, 26f)); // Row 8: Pk
            tlp.RowStyles.Add(new RowStyle(SizeType.Absolute, 26f)); // Row 9: PFk
            tlp.RowStyles.Add(new RowStyle(SizeType.Absolute, 32f)); // Row 10: Buttons

            // Row 0: 標題
            Label lblTitle = new Label()
            {
                Text = "3. 堵轉測試數據 (8頻率試驗與自適應調壓)",
                Font = new Font("微軟正黑體", 10.5f, FontStyle.Bold),
                ForeColor = Color.FromArgb(180, 83, 9),
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft
            };
            tlp.SetColumnSpan(lblTitle, 2);
            tlp.Controls.Add(lblTitle, 0, 0);

            // Row 1: 狀態指示
            lblLockedItemStatus = new Label()
            {
                Text = "[--] 待採樣 (請先機械鎖死並選擇單頻或全頻測試)",
                Font = new Font("微軟正黑體", 8.5f, FontStyle.Bold),
                ForeColor = Color.FromArgb(100, 116, 139),
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft
            };
            tlp.SetColumnSpan(lblLockedItemStatus, 2);
            tlp.Controls.Add(lblLockedItemStatus, 0, 1);

            // Row 2: 試驗頻率與載台選擇 (8種試驗頻率)
            TableLayoutPanel tlpFreqDrive = new TableLayoutPanel()
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 1,
                Margin = new Padding(0)
            };
            tlpFreqDrive.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 58f));
            tlpFreqDrive.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 42f));

            cmbEquivLockedFreq = new ComboBox()
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Dock = DockStyle.Fill,
                Font = new Font("微軟正黑體", 8.5f)
            };
            cmbEquivLockedFreq.Items.AddRange(new object[] {
                "(1) 額定頻率 [1.0x] (dr/uf同步)",
                "(2) 25% 額定頻率 [0.25x]",
                "(3) 30% 額定頻率 [0.30x]",
                "(4) 40% 額定頻率 [0.40x]",
                "(5) 50% 額定頻率 [0.50x]",
                "(6) 60% 額定頻率 [0.60x]",
                "(7) 2倍 額定頻率 [2.0x]",
                "(8) 4倍 額定頻率 [4.0x]"
            });
            cmbEquivLockedFreq.SelectedIndex = 0; // 預設 額定頻率
            cmbEquivLockedFreq.SelectedIndexChanged += (s, e) => {
                WriteHmiLog("EQUIV", string.Format("【等效電路】切換堵轉試驗頻率模式為: {0}", cmbEquivLockedFreq.SelectedItem));
            };

            cmbEquivKebDrive = new ComboBox()
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Dock = DockStyle.Fill,
                Font = new Font("微軟正黑體", 8.5f)
            };
            cmbEquivKebDrive.Items.AddRange(new object[] { "B載台 (待測)", "A載台" });
            cmbEquivKebDrive.SelectedIndex = 0;

            tlpFreqDrive.Controls.Add(cmbEquivLockedFreq, 0, 0);
            tlpFreqDrive.Controls.Add(cmbEquivKebDrive, 1, 0);
            tlp.SetColumnSpan(tlpFreqDrive, 2);
            tlp.Controls.Add(tlpFreqDrive, 0, 2);

            // Row 3: 目標 uf09 + 寫入 + 自適應追隨 (緊湊四欄式)
            TableLayoutPanel tlpUfCtrl = new TableLayoutPanel()
            {
                Dock = DockStyle.Fill,
                ColumnCount = 4,
                RowCount = 1,
                Margin = new Padding(0)
            };
            tlpUfCtrl.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 68f));
            tlpUfCtrl.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 55f));
            tlpUfCtrl.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 55f));
            tlpUfCtrl.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

            lblEquivCurUf09 = new Label()
            {
                Text = "uf09:--V",
                Font = new Font("Consolas", 8.5f, FontStyle.Bold),
                ForeColor = Color.FromArgb(180, 83, 9),
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleCenter
            };

            numEquivTargetUf09 = new NumericUpDown()
            {
                Minimum = 5,
                Maximum = 260,
                Value = 35,
                Increment = 1,
                Dock = DockStyle.Fill,
                Font = new Font("Consolas", 9f, FontStyle.Bold)
            };

            btnEquivWriteUf09 = new Button()
            {
                Text = "[寫入]",
                Font = new Font("微軟正黑體", 8.5f, FontStyle.Bold),
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(254, 243, 199),
                ForeColor = Color.FromArgb(180, 83, 9),
                FlatStyle = FlatStyle.Flat,
                Margin = new Padding(1)
            };
            btnEquivWriteUf09.FlatAppearance.BorderColor = Color.FromArgb(252, 211, 77);
            btnEquivWriteUf09.Click += (s, e) => WriteTargetUf09ToHardware();

            btnEquivAutoTuneUf09 = new Button()
            {
                Text = "[AI] 單頻測試",
                Font = new Font("微軟正黑體", 8.5f, FontStyle.Bold),
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(238, 242, 255),
                ForeColor = Color.FromArgb(79, 70, 229),
                FlatStyle = FlatStyle.Flat,
                Margin = new Padding(1)
            };
            btnEquivAutoTuneUf09.FlatAppearance.BorderColor = Color.FromArgb(199, 210, 254);
            btnEquivAutoTuneUf09.Click += (s, e) => StartLockedRotorTest(singleFreqMode: true);

            // 支援背景讀取
            btnEquivReadUf09 = new Button() { Visible = false };
            btnEquivReadUf09.Click += (s, e) => ReadCurrentUf09FromHardware();

            tlpUfCtrl.Controls.Add(lblEquivCurUf09, 0, 0);
            tlpUfCtrl.Controls.Add(numEquivTargetUf09, 1, 0);
            tlpUfCtrl.Controls.Add(btnEquivWriteUf09, 2, 0);
            tlpUfCtrl.Controls.Add(btnEquivAutoTuneUf09, 3, 0);

            tlp.SetColumnSpan(tlpUfCtrl, 2);
            tlp.Controls.Add(tlpUfCtrl, 0, 3);

            // Row 4: 8 頻率掃描操作工具列 (同步檢查 / 全頻掃描 / 8頻紀錄表)
            TableLayoutPanel tlpSweepBtns = new TableLayoutPanel()
            {
                Dock = DockStyle.Fill,
                ColumnCount = 3,
                RowCount = 1,
                Margin = new Padding(0)
            };
            tlpSweepBtns.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 34f));
            tlpSweepBtns.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 34f));
            tlpSweepBtns.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 32f));

            btnEquivSyncCheck = new Button()
            {
                Text = "[同步檢查]",
                Font = new Font("微軟正黑體", 8.5f, FontStyle.Bold),
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(241, 245, 249),
                ForeColor = Color.FromArgb(30, 41, 59),
                FlatStyle = FlatStyle.Flat,
                Margin = new Padding(1)
            };
            btnEquivSyncCheck.FlatAppearance.BorderColor = Color.FromArgb(203, 213, 225);
            btnEquivSyncCheck.Click += (s, e) => CheckAndCollectDrUfParams(showSuccessDialog: true);

            btnEquivSweepAllFreq = new Button()
            {
                Text = "[>>] 全頻掃描",
                Font = new Font("微軟正黑體", 8.5f, FontStyle.Bold),
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(241, 245, 249),
                ForeColor = Color.FromArgb(79, 70, 229),
                FlatStyle = FlatStyle.Flat,
                Margin = new Padding(1)
            };
            btnEquivSweepAllFreq.FlatAppearance.BorderColor = Color.FromArgb(199, 210, 254);
            btnEquivSweepAllFreq.Click += (s, e) => StartLockedRotorTest(singleFreqMode: false);

            btnEquivViewSweepResults = new Button()
            {
                Text = "[表] 8頻記錄",
                Font = new Font("微軟正黑體", 8.5f, FontStyle.Bold),
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(241, 245, 249),
                ForeColor = Color.FromArgb(16, 185, 129),
                FlatStyle = FlatStyle.Flat,
                Margin = new Padding(1)
            };
            btnEquivViewSweepResults.FlatAppearance.BorderColor = Color.FromArgb(167, 243, 208);
            btnEquivViewSweepResults.Click += (s, e) => ShowLockedSweepResultsDialog();

            tlpSweepBtns.Controls.Add(btnEquivSyncCheck, 0, 0);
            tlpSweepBtns.Controls.Add(btnEquivSweepAllFreq, 1, 0);
            tlpSweepBtns.Controls.Add(btnEquivViewSweepResults, 2, 0);

            tlp.SetColumnSpan(tlpSweepBtns, 2);
            tlp.Controls.Add(tlpSweepBtns, 0, 4);

            // Row 5: 即時保護指示橫條
            lblLockedProtStatus = new Label()
            {
                Text = "[防護] 實時防護: 監控中 (電流: 110% IN / 10s | 轉速: 5 rpm / 3s)",
                Font = new Font("微軟正黑體", 8f, FontStyle.Bold),
                ForeColor = Color.FromArgb(16, 185, 129),
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                BackColor = Color.FromArgb(248, 250, 252),
                Padding = new Padding(3, 1, 3, 1)
            };
            tlp.SetColumnSpan(lblLockedProtStatus, 2);
            tlp.Controls.Add(lblLockedProtStatus, 0, 5);

            // Row 6~9: 堵轉實測數據列 (預設值歸零)
            numEquivVk = AddCardField(tlp, 6, "堵轉電壓 Vk (V):", 0.0m, 1, 0, 500);
            numEquivIk = AddCardField(tlp, 7, "堵轉電流 Ik (A):", 0.0m, 2, 0, 500);
            numEquivPk = AddCardField(tlp, 8, "堵轉功率 Pk (W):", 0.0m, 1, 0, 50000);
            numEquivPfk = AddCardField(tlp, 9, "堵轉因數 PFk:", 0.0m, 3, 0, 1);

            // Row 10: 操作按鈕行 (三鍵式: 擷取 / 復歸 / 緊急停機)
            TableLayoutPanel tlpBtnsLocked = new TableLayoutPanel()
            {
                Dock = DockStyle.Fill,
                ColumnCount = 3,
                RowCount = 1,
                Margin = new Padding(0, 2, 0, 0)
            };
            tlpBtnsLocked.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 42f));
            tlpBtnsLocked.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 28f));
            tlpBtnsLocked.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 30f));

            btnEquivCaptureLiveLocked = new Button()
            {
                Text = "[*] 擷取即時",
                Font = new Font("微軟正黑體", 8.5f, FontStyle.Bold),
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(254, 243, 199),
                ForeColor = Color.FromArgb(180, 83, 9),
                FlatStyle = FlatStyle.Flat,
                Margin = new Padding(2)
            };
            btnEquivCaptureLiveLocked.FlatAppearance.BorderColor = Color.FromArgb(252, 211, 77);
            btnEquivCaptureLiveLocked.Click += (s, e) => CaptureLiveLockedData();

            btnEquivRevertUf09 = new Button()
            {
                Text = "復歸預設",
                Font = new Font("微軟正黑體", 8f),
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(241, 245, 249),
                Margin = new Padding(2)
            };
            btnEquivRevertUf09.Click += (s, e) => RevertUf09ToDefault();

            btnEquivLockedStop = new Button()
            {
                Text = "■ 急停",
                Font = new Font("微軟正黑體", 8.5f, FontStyle.Bold),
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(254, 242, 242),
                ForeColor = Color.FromArgb(220, 38, 38),
                FlatStyle = FlatStyle.Flat,
                Margin = new Padding(2)
            };
            btnEquivLockedStop.FlatAppearance.BorderColor = Color.FromArgb(254, 202, 202);
            btnEquivLockedStop.Click += (s, e) => {
                isLockedSweepRunning = false;
                isAutoTuningUf09 = false;
                isLockedRotorActive = false;
                TriggerLockedProtectionTrip("使用者手動點擊緊急停止", "操作者於等效電路堵轉面板主動點擊【■ 急停】按鈕。");
            };

            tlpBtnsLocked.Controls.Add(btnEquivCaptureLiveLocked, 0, 0);
            tlpBtnsLocked.Controls.Add(btnEquivRevertUf09, 1, 0);
            tlpBtnsLocked.Controls.Add(btnEquivLockedStop, 2, 0);

            tlp.SetColumnSpan(tlpBtnsLocked, 2);
            tlp.Controls.Add(tlpBtnsLocked, 0, 10);

            card.Controls.Add(tlp);
            return card;
        }

        private NumericUpDown AddCardField(TableLayoutPanel tlp, int row, string labelText, decimal defVal, int decimals, decimal min, decimal max)
        {
            Label lbl = new Label()
            {
                Text = labelText,
                Font = new Font("微軟正黑體", 9f),
                ForeColor = Color.FromArgb(51, 65, 85),
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleRight
            };
            NumericUpDown num = new NumericUpDown()
            {
                DecimalPlaces = decimals,
                Minimum = min,
                Maximum = max,
                Value = defVal,
                Increment = decimals > 0 ? (decimal)Math.Pow(10, -decimals) : 1m,
                Dock = DockStyle.Fill,
                Font = new Font("Consolas", 9.5f, FontStyle.Bold),
                TextAlign = HorizontalAlignment.Right
            };
            tlp.Controls.Add(lbl, 0, row);
            tlp.Controls.Add(num, 1, row);
            return num;
        }

        #endregion

        #region 成果分析區: 等效電路計算、表格矩陣與 GDI+ 電路圖解

        private Panel CreateResultsSection()
        {
            Panel pnl = new Panel()
            {
                Dock = DockStyle.Fill,
                BackColor = Color.White,
                Padding = new Padding(8, 6, 8, 6)
            };

            // 頂部操作工具列 (緊湊自適應寬度)
            TableLayoutPanel tlpToolbar = new TableLayoutPanel()
            {
                Dock = DockStyle.Top,
                Height = 38,
                ColumnCount = 6,
                RowCount = 1,
                Margin = new Padding(0, 0, 0, 4)
            };
            tlpToolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 155f));
            tlpToolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 100f));
            tlpToolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 160f));
            tlpToolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 175f));
            tlpToolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            tlpToolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 235f));

            Label lblR1Prompt = new Label()
            {
                Text = "定子冷態 R1 (Ω):",
                Font = new Font("微軟正黑體", 9f, FontStyle.Bold),
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleRight
            };
            numEquivStatorR1 = new NumericUpDown()
            {
                DecimalPlaces = 4,
                Minimum = 0.0001m,
                Maximum = 50m,
                Value = 0.1250m,
                Increment = 0.001m,
                Dock = DockStyle.Fill,
                Font = new Font("Consolas", 9.5f, FontStyle.Bold),
                TextAlign = HorizontalAlignment.Right
            };

            chkAutoR1Distribute = new CheckBox()
            {
                Text = "自動依堵轉 50% 分配",
                Checked = true,
                Font = new Font("微軟正黑體", 8.5f),
                Dock = DockStyle.Fill
            };
            chkAutoR1Distribute.CheckedChanged += (s, e) => {
                numEquivStatorR1.Enabled = !chkAutoR1Distribute.Checked;
            };

            btnEquivCalculate = new Button()
            {
                Text = "計算等效電路參數",
                Font = new Font("微軟正黑體", 9.5f, FontStyle.Bold),
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(16, 185, 129),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Margin = new Padding(2)
            };
            btnEquivCalculate.FlatAppearance.BorderSize = 0;
            btnEquivCalculate.Click += (s, e) => ExecuteEquivCircuitCalculation();

            FlowLayoutPanel flpExport = new FlowLayoutPanel()
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.RightToLeft,
                Margin = new Padding(0)
            };
            btnEquivExportCsv = new Button()
            {
                Text = "匯出 CSV",
                Font = new Font("微軟正黑體", 8.5f),
                Size = new Size(100, 30),
                BackColor = Color.FromArgb(241, 245, 249),
                Margin = new Padding(2)
            };
            btnEquivExportCsv.Click += (s, e) => ExportEquivCircuitCsv();

            btnEquivCopyResults = new Button()
            {
                Text = "複製參數",
                Font = new Font("微軟正黑體", 8.5f),
                Size = new Size(100, 30),
                BackColor = Color.FromArgb(241, 245, 249),
                Margin = new Padding(2)
            };
            btnEquivCopyResults.Click += (s, e) => CopyEquivResultsToClipboard();

            flpExport.Controls.Add(btnEquivExportCsv);
            flpExport.Controls.Add(btnEquivCopyResults);

            tlpToolbar.Controls.Add(lblR1Prompt, 0, 0);
            tlpToolbar.Controls.Add(numEquivStatorR1, 1, 0);
            tlpToolbar.Controls.Add(chkAutoR1Distribute, 2, 0);
            tlpToolbar.Controls.Add(btnEquivCalculate, 3, 0);
            tlpToolbar.Controls.Add(flpExport, 5, 0);

            // 分割檢視：左側參數數據 DataGridView，右側 GDI+ 等效電路架構繪製圖解 (左右分割條)
            splitEquivResults = new SplitContainer()
            {
                Dock = DockStyle.Fill,
                Orientation = Orientation.Vertical,
                BackColor = Color.FromArgb(226, 232, 240),
                SplitterWidth = 5
            };

            dgvEquivResults = new DataGridView()
            {
                Dock = DockStyle.Fill,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                ReadOnly = true,
                RowHeadersVisible = false,
                BackgroundColor = Color.White,
                BorderStyle = BorderStyle.Fixed3D,
                Font = new Font("微軟正黑體", 9f),
                SelectionMode = DataGridViewSelectionMode.FullRowSelect
            };
            dgvEquivResults.ColumnHeadersDefaultCellStyle.Font = new Font("微軟正黑體", 9f, FontStyle.Bold);
            dgvEquivResults.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(241, 245, 249);
            dgvEquivResults.Columns.Add("Param", "等效電路參數");
            dgvEquivResults.Columns.Add("Symbol", "符號");
            dgvEquivResults.Columns.Add("Value", "計算數值 (Ω/Nm/%)");
            dgvEquivResults.Columns.Add("Unit", "單位");
            dgvEquivResults.Columns.Add("Inductance", "換算電感 (mH)");
            dgvEquivResults.Columns.Add("Desc", "工程物理意義");

            dgvEquivResults.Columns["Param"].Width = 120;
            dgvEquivResults.Columns["Symbol"].Width = 60;
            dgvEquivResults.Columns["Value"].Width = 90;
            dgvEquivResults.Columns["Unit"].Width = 45;
            dgvEquivResults.Columns["Inductance"].Width = 100;
            dgvEquivResults.Columns["Desc"].AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;

            InitDefaultEquivResultsGrid();

            // 右側等效電路圖解面板
            pnlEquivDiagram = new Panel()
            {
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(248, 250, 252),
                BorderStyle = BorderStyle.FixedSingle
            };
            pnlEquivDiagram.Paint += DrawEquivalentCircuitDiagram;

            splitEquivResults.Panel1.Controls.Add(dgvEquivResults);
            splitEquivResults.Panel2.Controls.Add(pnlEquivDiagram);

            // 註冊安全分割條 (預設距離 620, Panel1 最少 200, Panel2 最少 200)
            SafeSetupSplitContainer(splitEquivResults, "EquivResults", 620, 200, 200);

            // 依序加入底層成果容器 (tlpToolbar 在上，splitEquivResults 在下 Fill)
            pnl.Controls.Add(splitEquivResults);
            pnl.Controls.Add(tlpToolbar);

            return pnl;
        }

        private void InitDefaultEquivResultsGrid()
        {
            if (dgvEquivResults == null) return;
            dgvEquivResults.Rows.Clear();
            dgvEquivResults.Rows.Add("定子相電阻", "R1", "--", "Ω", "--", "定子繞組有效相電阻");
            dgvEquivResults.Rows.Add("定子漏電抗", "X1", "--", "Ω", "--", "定子漏磁通等效相電抗/漏電感");
            dgvEquivResults.Rows.Add("激磁電抗", "Xm", "--", "Ω", "--", "氣隙主磁通等效激磁抗/激磁電感");
            dgvEquivResults.Rows.Add("鐵損電阻", "Rc", "--", "Ω", "--", "主磁通渦流與磁滯鐵耗");
            dgvEquivResults.Rows.Add("轉子折算電阻", "R2'", "--", "Ω", "--", "轉子繞組折算至定子端電阻");
            dgvEquivResults.Rows.Add("轉子折算漏抗", "X2'", "--", "Ω", "--", "轉子漏磁通折算等效電抗/漏電感");
            dgvEquivResults.Rows.Add("堵轉阻抗", "Zk", "--", "Ω", "--", "轉子鎖死時之短路等效總阻抗");
            dgvEquivResults.Rows.Add("堵轉總漏抗", "Xk", "--", "Ω", "--", "堵轉短路等效總漏抗/漏電感");
            dgvEquivResults.Rows.Add("額定運轉轉差率", "sN", "--", "%", "--", "實測額定負載不補轉差率");
            dgvEquivResults.Rows.Add("推估啟動轉矩", "Tst", "--", "Nm", "--", "全壓啟動初始瞬態轉矩估算");
            dgvEquivResults.Rows.Add("推估最大崩潰轉矩", "Tmax", "--", "Nm", "--", "等效電路推估之極限轉矩");
            dgvEquivResults.Rows.Add("額定預測效率", "η", "--", "%", "--", "由等效電路損耗推估之額定效率");
        }

        #endregion

        #region 數據載入與即時採樣實作

        // 1. 從馬達專屬資料夾或「空載測試」分頁載入
        private void LoadNoLoadDataFromTestTab()
        {
            try
            {
                string mName = !string.IsNullOrEmpty(motorModelName) ? motorModelName : "SVM100S";
                string motorDir = GetMotorDedicatedLogDirectory(mName);
                bool loadedFromFile = false;

                if (Directory.Exists(motorDir))
                {
                    // 搜尋 NoLoad 測試檔案或最新包含 NoLoad 關鍵字的 CSV
                    var files = Directory.GetFiles(motorDir, "*.csv")
                        .Where(f => f.IndexOf("NoLoad", StringComparison.OrdinalIgnoreCase) >= 0)
                        .OrderByDescending(f => File.GetLastWriteTime(f))
                        .ToList();

                    if (files.Count > 0)
                    {
                        string targetFile = files[0];
                        string[] lines = File.ReadAllLines(targetFile, Encoding.UTF8);

                        List<double[]> dataRows = new List<double[]>();
                        int headerIdx = -1;
                        Dictionary<string, int> colMap = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

                        for (int l = 0; l < lines.Length; l++)
                        {
                            string line = lines[l].Trim();
                            if (string.IsNullOrEmpty(line)) continue;
                            if (line.StartsWith("#")) continue;

                            if (headerIdx < 0 && line.Contains("Speed_rpm") && line.Contains("Voltage_Sigma_V"))
                            {
                                headerIdx = l;
                                string[] headers = line.Split(',');
                                for (int c = 0; c < headers.Length; c++)
                                {
                                    colMap[headers[c].Trim()] = c;
                                }
                                continue;
                            }

                            if (headerIdx >= 0)
                            {
                                string[] parts = line.Split(',');
                                if (parts.Length >= colMap.Count && colMap.ContainsKey("Speed_rpm"))
                                {
                                    try
                                    {
                                        double spd = double.Parse(parts[colMap["Speed_rpm"]]);
                                        double v = colMap.ContainsKey("Voltage_Sigma_V") ? double.Parse(parts[colMap["Voltage_Sigma_V"]]) : 0;
                                        double i = colMap.ContainsKey("Current_Sigma_A") ? double.Parse(parts[colMap["Current_Sigma_A"]]) : 0;
                                        double pKw = colMap.ContainsKey("ElecPower_kW") ? double.Parse(parts[colMap["ElecPower_kW"]]) : 0;
                                        double pf = colMap.ContainsKey("PF") ? double.Parse(parts[colMap["PF"]]) : 0.08;
                                        double freq = colMap.ContainsKey("Frequency_Hz") ? double.Parse(parts[colMap["Frequency_Hz"]]) : 50.0;
                                        if (spd > 10.0 && v > 10.0)
                                        {
                                            dataRows.Add(new double[] { spd, v, i, pKw * 1000.0, pf, freq });
                                        }
                                    }
                                    catch { }
                                }
                            }
                        }

                        if (dataRows.Count > 0)
                        {
                            int takeCount = Math.Min(10, dataRows.Count);
                            var stableRows = dataRows.Skip(dataRows.Count - takeCount).ToList();
                            double avgSpd = stableRows.Average(r => r[0]);
                            double avgV = stableRows.Average(r => r[1]);
                            double avgI = stableRows.Average(r => r[2]);
                            double avgP = stableRows.Average(r => r[3]);
                            double avgPf = stableRows.Average(r => r[4]);
                            double avgFreq = stableRows.Average(r => r[5]);

                            if (avgV > 0) numEquivV0.Value = (decimal)Math.Round(avgV, 1);
                            if (avgI > 0) numEquivI0.Value = (decimal)Math.Round(avgI, 2);
                            if (avgP > 0) numEquivP0.Value = (decimal)Math.Round(avgP, 1);
                            if (avgPf > 0) numEquivPf0.Value = (decimal)Math.Round(avgPf, 3);
                            if (avgSpd > 0) numEquivN0.Value = (decimal)Math.Round(avgSpd);
                            if (avgFreq > 0) numEquivF0.Value = (decimal)Math.Round(avgFreq, 2);

                            isNoLoadDataReady = true;
                            lblNoLoadItemStatus.Text = string.Format("[O] 已自檔案讀取: {0} ({1:HH:mm:ss})", Path.GetFileName(targetFile), DateTime.Now);
                            lblNoLoadItemStatus.ForeColor = Color.FromArgb(16, 185, 129);
                            WriteHmiLog("EQUIV", string.Format("【等效電路】成功自馬達檔案提取空載數據: V0={0:F1}V, I0={1:F2}A, P0={2:F1}W, N0={3:F0}rpm, F0={4:F1}Hz ({5})",
                                avgV, avgI, avgP, avgSpd, avgFreq, Path.GetFileName(targetFile)));
                            loadedFromFile = true;
                        }
                    }
                }

                if (!loadedFromFile)
                {
                    // 若無檔案，備援從 dgvNoLoad 載入
                    if (dgvNoLoad != null && dgvNoLoad.Rows.Count > 0)
                    {
                        DataGridViewRow targetRow = null;
                        for (int i = dgvNoLoad.Rows.Count - 1; i >= 0; i--)
                        {
                            var r = dgvNoLoad.Rows[i];
                            string ev = Convert.ToString(r.Cells["Event"].Value ?? "");
                            if (ev.Contains("達標") || ev.Contains("完成") || ev.Contains("額定") || i == dgvNoLoad.Rows.Count - 1)
                            {
                                targetRow = r;
                                break;
                            }
                        }

                        if (targetRow != null)
                        {
                            double spd = 0.0;
                            double.TryParse(Convert.ToString(targetRow.Cells["ActSpd"].Value ?? "0"), out spd);
                            if (spd > 0) numEquivN0.Value = (decimal)Math.Round(spd);
                        }

                        decimal ratedV = (lastB_Dr02.HasValue && lastB_Dr02.Value > 0) ? (decimal)lastB_Dr02.Value : 260m;
                        if (numEquivV0.Value <= 0 || numEquivV0.Value == 260m) numEquivV0.Value = ratedV;

                        isNoLoadDataReady = true;
                        lblNoLoadItemStatus.Text = "[O] 已載入空載分頁數據 (" + DateTime.Now.ToString("HH:mm:ss") + ")";
                        lblNoLoadItemStatus.ForeColor = Color.FromArgb(16, 185, 129);
                        WriteHmiLog("EQUIV", "【等效電路】已成功從空載測試分頁提取運轉數據！");
                    }
                    else
                    {
                        MessageBox.Show("於馬達資料夾中未找到空載紀錄檔 (NoLoad)，且空載測試分頁尚無數據！\r\n請先執行空載測試或直接手動輸入/即時採樣。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("從空載記錄載入數據失敗: " + ex.Message, "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        // 1. 擷取當前即時空載數據
        private void CaptureLiveNoLoadData()
        {
            try
            {
                double v = (actVoltageSigma > 10.0) ? actVoltageSigma : ((wtU1 + wtU2 + wtU3) / 3.0);
                double i = (actCurrentSigma > 0.05) ? actCurrentSigma : ((wtI1 + wtI2 + wtI3) / 3.0);
                double p = (actElecPower > 0.01) ? (actElecPower * 1000.0) : (wtP1 + wtP2 + wtP3);
                double pf = (wtPFSig > 0.0) ? wtPFSig : 0.08;
                double spd = Math.Abs(actSpeed);
                double f = (actFrequency > 1.0) ? actFrequency : (wtFreqU > 1.0 ? wtFreqU : 50.0);

                if (v > 0) numEquivV0.Value = (decimal)Math.Round(v, 1);
                if (i > 0) numEquivI0.Value = (decimal)Math.Round(i, 2);
                if (p > 0) numEquivP0.Value = (decimal)Math.Round(p, 1);
                if (pf > 0) numEquivPf0.Value = (decimal)Math.Round(pf, 3);
                if (spd > 0) numEquivN0.Value = (decimal)Math.Round(spd);
                if (f > 0) numEquivF0.Value = (decimal)Math.Round(f, 2);

                isNoLoadDataReady = true;
                lblNoLoadItemStatus.Text = "[O] 即時空載數據已採樣 (" + DateTime.Now.ToString("HH:mm:ss") + ")";
                lblNoLoadItemStatus.ForeColor = Color.FromArgb(16, 185, 129);
                WriteHmiLog("EQUIV", string.Format("【等效電路】即時空載採樣成功: V0={0:F1}V, I0={1:F2}A, P0={2:F1}W, N0={3:F0}rpm", v, i, p, spd));
            }
            catch (Exception ex)
            {
                MessageBox.Show("即時空載採樣失敗: " + ex.Message, "錯誤", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        // 2. 從「S1 不補轉差紀錄檔」或「T-N 分頁」載入額定點
        private void LoadRatedDataFromTnTab()
        {
            try
            {
                string mName = !string.IsNullOrEmpty(motorModelName) ? motorModelName : "SVM100S";
                string motorDir = GetMotorDedicatedLogDirectory(mName);
                bool loadedFromS1 = false;

                if (Directory.Exists(motorDir))
                {
                    string latestS1File = Path.Combine(motorDir, "S1_Rated_NoSlip_Latest.csv");
                    if (!File.Exists(latestS1File))
                    {
                        var files = Directory.GetFiles(motorDir, "S1_Rated_NoSlip_*.csv")
                            .OrderByDescending(f => File.GetLastWriteTime(f))
                            .ToList();
                        if (files.Count > 0) latestS1File = files[0];
                    }

                    if (File.Exists(latestS1File))
                    {
                        string[] lines = File.ReadAllLines(latestS1File, Encoding.UTF8);
                        double avgSpd = 0, avgTrq = 0, avgV = 0, avgI = 0, avgP = 0, avgPf = 0, slip = 0;

                        foreach (string l in lines)
                        {
                            string trimL = l.Trim();
                            if (!trimL.StartsWith("#")) continue;
                            if (trimL.StartsWith("# AverageSpeed_rpm:")) double.TryParse(trimL.Substring(19).Trim(), out avgSpd);
                            else if (trimL.StartsWith("# AverageTorque_Nm:")) double.TryParse(trimL.Substring(19).Trim(), out avgTrq);
                            else if (trimL.StartsWith("# AverageVoltage_V:")) double.TryParse(trimL.Substring(19).Trim(), out avgV);
                            else if (trimL.StartsWith("# AverageCurrent_A:")) double.TryParse(trimL.Substring(19).Trim(), out avgI);
                            else if (trimL.StartsWith("# AveragePower_kW:")) double.TryParse(trimL.Substring(18).Trim(), out avgP);
                            else if (trimL.StartsWith("# AveragePowerFactor:")) double.TryParse(trimL.Substring(21).Trim(), out avgPf);
                            else if (trimL.StartsWith("# Slip_pct:")) double.TryParse(trimL.Substring(11).Trim(), out slip);
                        }

                        if (avgSpd > 0 && avgTrq > 0)
                        {
                            numEquivNn.Value = (decimal)Math.Round(avgSpd);
                            numEquivTn.Value = (decimal)Math.Round(avgTrq, 2);
                            if (avgV > 0) numEquivVn.Value = (decimal)Math.Round(avgV, 1);
                            if (avgI > 0) numEquivIn.Value = (decimal)Math.Round(avgI, 2);
                            if (avgP > 0) numEquivPn.Value = (decimal)Math.Round(avgP, 3);
                            if (avgPf > 0) numEquivPfn.Value = (decimal)Math.Round(avgPf, 3);
                            if (slip > 0) numEquivSlip.Value = (decimal)Math.Round(slip, 2);

                            isRatedDataReady = true;
                            lblRatedItemStatus.Text = string.Format("[O] 已自 S1 不補轉差檔讀取 ({0:HH:mm:ss})", DateTime.Now);
                            lblRatedItemStatus.ForeColor = Color.FromArgb(16, 185, 129);
                            WriteHmiLog("EQUIV", string.Format("【等效電路】成功自 S1 紀錄檔提取額定數據 (30筆平均): TN={0:F2}Nm, NN={1:F0}rpm (不補轉差), VN={2:F1}V, IN={3:F2}A, sN={4:F2}% ({5})",
                                avgTrq, avgSpd, avgV, avgI, slip, Path.GetFileName(latestS1File)));
                            loadedFromS1 = true;
                        }
                    }
                }

                if (!loadedFromS1)
                {
                    // 備援從 T-N 分頁載入
                    bool found = false;
                    if (dgvTnPoints != null && dgvTnPoints.Rows.Count > 0)
                    {
                        var row = dgvTnPoints.Rows[dgvTnPoints.Rows.Count - 1];
                        double spd = 0, trq = 0, pwr = 0;
                        double.TryParse(Convert.ToString(row.Cells["Speed"].Value ?? "0"), out spd);
                        double.TryParse(Convert.ToString(row.Cells["Torque"].Value ?? "0"), out trq);
                        double.TryParse(Convert.ToString(row.Cells["Pwr"].Value ?? "0"), out pwr);

                        if (spd > 0 && trq > 0)
                        {
                            numEquivNn.Value = (decimal)Math.Round(spd);
                            numEquivTn.Value = (decimal)Math.Round(trq, 2);
                            if (pwr > 0) numEquivPn.Value = (decimal)Math.Round(pwr, 3);
                            found = true;
                        }
                    }

                    decimal ratedV = (lastB_Dr02.HasValue && lastB_Dr02.Value > 0) ? (decimal)lastB_Dr02.Value : 260m;
                    numEquivVn.Value = ratedV;

                    int poles = kebMotorPoles2 > 0 ? kebMotorPoles2 : 4;
                    double syncSpd = 120.0 * 50.0 / poles;
                    double actSpdVal = (double)numEquivNn.Value;
                    if (syncSpd > 0 && actSpdVal > 0)
                    {
                        double s = (syncSpd - actSpdVal) / syncSpd;
                        if (s > 0) numEquivSlip.Value = (decimal)Math.Round(s * 100.0, 2);
                    }

                    if (found)
                    {
                        isRatedDataReady = true;
                        lblRatedItemStatus.Text = "[O] 已載入 T-N 額定運轉數據 (" + DateTime.Now.ToString("HH:mm:ss") + ")";
                        lblRatedItemStatus.ForeColor = Color.FromArgb(16, 185, 129);
                        WriteHmiLog("EQUIV", "【等效電路】已成功從 T-N 分頁提取額定運轉數據！");
                    }
                    else
                    {
                        MessageBox.Show("於馬達資料夾中未找到 S1 不補轉差紀錄檔 (S1_Rated_NoSlip_Latest.csv)，且 T-N 分頁亦無數據！\r\n請先執行 S1 測試 (自動完成40筆採樣) 或直接手動輸入/即時採樣。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("從額定記錄載入失敗: " + ex.Message, "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        // 2. 擷取即時額定運轉數據 (不補轉差)
        private void CaptureLiveRatedData()
        {
            try
            {
                double v = (actVoltageSigma > 10.0) ? actVoltageSigma : ((wtU1 + wtU2 + wtU3) / 3.0);
                double i = (actCurrentSigma > 0.05) ? actCurrentSigma : ((wtI1 + wtI2 + wtI3) / 3.0);
                double pKw = (actElecPower > 0.01) ? actElecPower : ((wtP1 + wtP2 + wtP3) / 1000.0);
                double trq = Math.Abs(actTorque);
                double spd = Math.Abs(actSpeed);
                double pf = (wtPFSig > 0.0) ? wtPFSig : 0.86;

                if (trq > 0) numEquivTn.Value = (decimal)Math.Round(trq, 2);
                if (spd > 0) numEquivNn.Value = (decimal)Math.Round(spd);
                if (v > 0) numEquivVn.Value = (decimal)Math.Round(v, 1);
                if (i > 0) numEquivIn.Value = (decimal)Math.Round(i, 2);
                if (pKw > 0) numEquivPn.Value = (decimal)Math.Round(pKw, 3);
                if (pf > 0) numEquivPfn.Value = (decimal)Math.Round(pf, 3);

                int poles = kebMotorPoles2 > 0 ? kebMotorPoles2 : 4;
                double f = (actFrequency > 1.0) ? actFrequency : (wtFreqU > 1.0 ? wtFreqU : 50.0);
                double syncSpd = 120.0 * f / poles;
                if (syncSpd > 0 && spd > 0)
                {
                    double s = (syncSpd - spd) / syncSpd;
                    if (s > 0) numEquivSlip.Value = (decimal)Math.Round(s * 100.0, 2);
                }

                isRatedDataReady = true;
                lblRatedItemStatus.Text = "[O] 即時額定數據已採樣 (不補轉差) (" + DateTime.Now.ToString("HH:mm:ss") + ")";
                lblRatedItemStatus.ForeColor = Color.FromArgb(16, 185, 129);
                WriteHmiLog("EQUIV", string.Format("【等效電路】即時額定採樣成功: TN={0:F2}Nm, NN={1:F0}rpm (不補轉差), VN={2:F1}V, IN={3:F2}A", trq, spd, v, i));
            }
            catch (Exception ex)
            {
                MessageBox.Show("即時額定採樣失敗: " + ex.Message, "錯誤", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        // 取得有效原始或額定基準電壓 (優先順序: originalUf09Val >= 100 -> dr.02 -> VN -> V0 -> 220V)
        private int GetOriginalOrRatedUf09(int driveId)
        {
            try
            {
                // 1. 若原始記錄之 uf09 有效 (額定電壓通常 >= 100V)，優先採用
                if (originalUf09Val >= 100)
                {
                    return originalUf09Val;
                }

                // 2. 嘗試從變頻器讀取 dr.02 (銘牌額定電壓)
                try
                {
                    int com = GetHmiKebComIdx(driveId);
                    int baud = GetHmiKebBaudIdx(driveId);
                    int node = (driveId == 1) ? (int)numHmiKebNode1.Value : (int)numHmiKebNode2.Value;
                    int? r_dr02 = KebReadParamWithDll(com, baud, node, 0x0602, 0) ?? KebReadParamWithDll(com, baud, node, 0x0602, 1) ??
                                  KebReadParamWithDll(com, baud, node, 0x0402, 0) ?? KebReadParamWithDll(com, baud, node, 0x0402, 1);
                    if (r_dr02.HasValue && r_dr02.Value >= 100)
                    {
                        originalUf09Val = r_dr02.Value;
                        return r_dr02.Value;
                    }
                }
                catch { }

                // 3. 從卡片 2「額定線壓 VN」取用
                if (numEquivVn != null && numEquivVn.Value >= 100m)
                {
                    int vn = (int)Math.Round(numEquivVn.Value);
                    originalUf09Val = vn;
                    return vn;
                }

                // 4. 從卡片 1「空載線壓 V0」取用
                if (numEquivV0 != null && numEquivV0.Value >= 100m)
                {
                    int v0 = (int)Math.Round(numEquivV0.Value);
                    originalUf09Val = v0;
                    return v0;
                }
            }
            catch { }

            // 5. 終極保底標準電壓 220 V
            return 220;
        }

        // 集中安全自動復歸 uf.09 函式 (無論測試成功、失敗、跳脫或停止，均保證回寫額定值)
        private bool AutoRestoreUf09(string reason)
        {
            try
            {
                int driveId = (cmbEquivKebDrive != null && cmbEquivKebDrive.SelectedIndex == 1) ? 1 : 2;
                string dName = (driveId == 1) ? "A載台" : "B載台";
                int targetV = GetOriginalOrRatedUf09(driveId);

                bool ok = KebWriteUf09(driveId, targetV);
                WriteHmiLog("KEB_UF09", string.Format("【uf.09 安全復歸】{0} ({1}): 復歸目標={2}V, 結果={3}", dName, reason, targetV, (ok ? "成功" : "失敗")));

                Action updateUi = () => {
                    if (lblEquivCurUf09 != null)
                    {
                        lblEquivCurUf09.Text = string.Format("uf09: {0} V (已復歸)", targetV);
                        lblEquivCurUf09.ForeColor = ok ? Color.FromArgb(16, 185, 129) : Color.FromArgb(239, 68, 68);
                    }
                };

                if (this.InvokeRequired)
                {
                    this.BeginInvoke((MethodInvoker)(() => updateUi()));
                }
                else
                {
                    updateUi();
                }

                return ok;
            }
            catch (Exception ex)
            {
                WriteHmiLog("KEB_ERR", "AutoRestoreUf09 異常: " + ex.Message);
                return false;
            }
        }

        // 3. KEB uf09 讀取
        private void ReadCurrentUf09FromHardware()
        {
            try
            {
                int driveId = (cmbEquivKebDrive.SelectedIndex == 1) ? 1 : 2;
                int? val = KebReadUf09(driveId);
                if (val.HasValue)
                {
                    if (val.Value >= 100)
                    {
                        originalUf09Val = val.Value;
                    }
                    lblEquivCurUf09.Text = string.Format("uf09: {0} V", val.Value);
                    lblEquivCurUf09.ForeColor = Color.FromArgb(16, 185, 129);
                    WriteHmiLog("EQUIV", string.Format("【KEB uf09 讀回成功】{0} 目前輸出電壓值為 {1} V (記錄基準: {2} V)", (driveId == 1 ? "A載台" : "B載台"), val.Value, originalUf09Val));
                }
                else
                {
                    lblEquivCurUf09.Text = "uf09: 讀取超時";
                    lblEquivCurUf09.ForeColor = Color.FromArgb(239, 68, 68);
                    MessageBox.Show("讀取 KEB uf09 逾時！請確認變頻器連線是否正常。", "通訊提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("讀取 uf09 異常: " + ex.Message, "錯誤", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        // 3. KEB uf09 寫入目標降壓值
        private void WriteTargetUf09ToHardware()
        {
            try
            {
                int targetV = (int)numEquivTargetUf09.Value;
                int driveId = (cmbEquivKebDrive.SelectedIndex == 1) ? 1 : 2;
                string dName = (driveId == 1) ? "A載台" : "B載台";

                string confirmMsg = string.Format("【[寫入] 堵轉安全降壓防呆確認】\r\n\r\n您即將把 {0} 的 KEB uf09 寫入為 【{1} V】！\r\n\r\n※ 注意事項：\r\n1. 請務必確認待測馬達機構已「確實機械鎖死」！\r\n2. 降壓旨在讓堵轉電流接近額定電流，避免大電流跳脫或燒機。\r\n3. 測試完成後請務必點擊「復歸預設」！\r\n\r\n是否確定寫入？", dName, targetV);

                if (MessageBox.Show(confirmMsg, "寫入確認", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes)
                {
                    bool ok = KebWriteUf09(driveId, targetV);
                    if (ok)
                    {
                        if (targetV <= 60) isLockedRotorActive = true;
                        lblEquivCurUf09.Text = string.Format("uf09: {0} V (已降壓)", targetV);
                        lblEquivCurUf09.ForeColor = Color.FromArgb(180, 83, 9);
                        MessageBox.Show("已成功將 " + dName + " 的 uf09 設定為 " + targetV + " V！\r\n現在可以啟動馬達並進行堵轉測試。", "寫入成功", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    }
                    else
                    {
                        MessageBox.Show("寫入 " + dName + " 的 uf09 失敗，請確認驅動器處於 nOP 狀態或通訊線路連接！", "寫入失敗", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("寫入 uf09 異常: " + ex.Message, "錯誤", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        // 3. KEB uf09 復歸預設
        private void RevertUf09ToDefault()
        {
            try
            {
                int driveId = (cmbEquivKebDrive.SelectedIndex == 1) ? 1 : 2;
                string dName = (driveId == 1) ? "A載台" : "B載台";
                int defaultV = GetOriginalOrRatedUf09(driveId);

                isAutoTuningUf09 = false;
                isLockedRotorActive = false;
                lockedOverCurrentTicks = 0;
                lockedSpeedAnomalyTicks = 0;

                bool ok = AutoRestoreUf09("手動點擊復歸預設");
                if (ok)
                {
                    MessageBox.Show("已成功將 " + dName + " 的 uf09 復歸為 " + defaultV + " V！", "復歸完成", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                else
                {
                    MessageBox.Show("復歸 uf09 失敗，請手動確認通訊連線！", "警告", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("復歸 uf09 異常: " + ex.Message, "錯誤", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        // 3. 擷取即時堵轉數據
        private void CaptureLiveLockedData()
        {
            try
            {
                double v = (actVoltageSigma > 1.0) ? actVoltageSigma : ((wtU1 + wtU2 + wtU3) / 3.0);
                double i = (actCurrentSigma > 0.05) ? actCurrentSigma : ((wtI1 + wtI2 + wtI3) / 3.0);
                double p = (actElecPower > 0.001) ? (actElecPower * 1000.0) : (wtP1 + wtP2 + wtP3);
                double pf = (wtPFSig > 0.0) ? wtPFSig : 0.29;

                if (v > 0) numEquivVk.Value = (decimal)Math.Round(v, 1);
                if (i > 0) numEquivIk.Value = (decimal)Math.Round(i, 2);
                if (p > 0) numEquivPk.Value = (decimal)Math.Round(p, 1);
                if (pf > 0) numEquivPfk.Value = (decimal)Math.Round(pf, 3);

                isLockedDataReady = true;
                isAutoTuningUf09 = false;
                isLockedRotorActive = false;
                lockedOverCurrentTicks = 0;
                lockedSpeedAnomalyTicks = 0;
                lblLockedItemStatus.Text = "[O] 堵轉數據已採樣鎖定 (" + DateTime.Now.ToString("HH:mm:ss") + ")";
                lblLockedItemStatus.ForeColor = Color.FromArgb(16, 185, 129);
                WriteHmiLog("EQUIV", string.Format("【等效電路】堵轉數據採樣成功: Vk={0:F1}V, Ik={1:F2}A, Pk={2:F1}W, PFk={3:F3}", v, i, p, pf));
            }
            catch (Exception ex)
            {
                MessageBox.Show("即時堵轉採樣失敗: " + ex.Message, "錯誤", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        // ── 堵轉保護機制與 8 頻率自適應調壓掃描核心 ──────────────────────────

        private void InitLockedSweepItems()
        {
            if (lockedSweepItems == null) lockedSweepItems = new List<LockedFreqSweepItem>();
            lockedSweepItems.Clear();
            lockedSweepItems.Add(new LockedFreqSweepItem { Index = 1, FreqName = "(1) 額定頻率", FreqRatio = 1.0 });
            lockedSweepItems.Add(new LockedFreqSweepItem { Index = 2, FreqName = "(2) 25% 額定頻率", FreqRatio = 0.25 });
            lockedSweepItems.Add(new LockedFreqSweepItem { Index = 3, FreqName = "(3) 30% 額定頻率", FreqRatio = 0.30 });
            lockedSweepItems.Add(new LockedFreqSweepItem { Index = 4, FreqName = "(4) 40% 額定頻率", FreqRatio = 0.40 });
            lockedSweepItems.Add(new LockedFreqSweepItem { Index = 5, FreqName = "(5) 50% 額定頻率", FreqRatio = 0.50 });
            lockedSweepItems.Add(new LockedFreqSweepItem { Index = 6, FreqName = "(6) 60% 額定頻率", FreqRatio = 0.60 });
            lockedSweepItems.Add(new LockedFreqSweepItem { Index = 7, FreqName = "(7) 2倍 額定頻率", FreqRatio = 2.0 });
            lockedSweepItems.Add(new LockedFreqSweepItem { Index = 8, FreqName = "(8) 4倍 額定頻率", FreqRatio = 4.0 });
        }

        private double GetCurrentSample()
        {
            double curI = (actCurrentSigma > 0.05) ? actCurrentSigma : ((wtI1 + wtI2 + wtI3) / 3.0);
            if (curI <= 0.05 && lastB_Dr00.HasValue) curI = (double)lastB_Dr00.Value;
            return curI;
        }

        // (1) 額定頻率 dr 和 uf 參數蒐集與同步檢查
        private bool CheckAndCollectDrUfParams(bool showSuccessDialog = true)
        {
            try
            {
                int driveId = (cmbEquivKebDrive != null && cmbEquivKebDrive.SelectedIndex == 1) ? 1 : 2;
                int com = GetHmiKebComIdx(driveId);
                int baud = GetHmiKebBaudIdx(driveId);
                int node = (driveId == 1) ? (int)numHmiKebNode1.Value : (int)numHmiKebNode2.Value;
                string dName = (driveId == 1) ? "A載台" : "B載台";

                // 讀取 dr 銘牌參數 (dr00 電流, dr01 轉速, dr02 電壓, dr05 頻率)
                int? r_dr00 = KebReadParamWithDll(com, baud, node, 0x0600, 0) ?? KebReadParamWithDll(com, baud, node, 0x0600, 1) ??
                              KebReadParamWithDll(com, baud, node, 0x0400, 0) ?? KebReadParamWithDll(com, baud, node, 0x0400, 1);
                int? r_dr01 = KebReadParamWithDll(com, baud, node, 0x0601, 0) ?? KebReadParamWithDll(com, baud, node, 0x0601, 1) ??
                              KebReadParamWithDll(com, baud, node, 0x0401, 0) ?? KebReadParamWithDll(com, baud, node, 0x0401, 1);
                int? r_dr02 = KebReadParamWithDll(com, baud, node, 0x0602, 0) ?? KebReadParamWithDll(com, baud, node, 0x0602, 1) ??
                              KebReadParamWithDll(com, baud, node, 0x0402, 0) ?? KebReadParamWithDll(com, baud, node, 0x0402, 1);
                // 讀取 dr 銘牌參數 (優先讀取 0x0605，相容 0x0405)
                int? r_dr05_s0 = KebReadParamWithDll(com, baud, node, 0x0605, 0);
                int? r_dr05_s1 = KebReadParamWithDll(com, baud, node, 0x0605, 1);
                int? r_dr05_4_s0 = KebReadParamWithDll(com, baud, node, 0x0405, 0);
                int? r_dr05 = r_dr05_s0 ?? r_dr05_s1 ?? r_dr05_4_s0;

                // 讀取 uf 特性參數 (uf00 頻率, uf09 基準電壓)
                int? r_uf00_s1 = KebReadParamWithDll(com, baud, node, 0x0500, 1);
                int? r_uf00_s0 = KebReadParamWithDll(com, baud, node, 0x0500, 0);
                int? r_uf00 = r_uf00_s1 ?? r_uf00_s0;

                int? r_uf09_s1 = KebReadParamWithDll(com, baud, node, 0x0509, 1);
                int? r_uf09_s0 = KebReadParamWithDll(com, baud, node, 0x0509, 0);
                int? r_uf09 = r_uf09_s1 ?? r_uf09_s0;

                double drFreq = 0.0;
                if (r_dr05.HasValue)
                {
                    drFreq = ConvertKebDr05ToFrequency(r_dr05.Value);
                }

                double ufFreq = 0.0;
                if (r_uf00.HasValue)
                {
                    ufFreq = ConvertKebUf00ToFrequency(r_uf00.Value, drFreq);
                }

                int rawUf00Val = r_uf00.HasValue ? r_uf00.Value : 0;
                int rawDr05Val = r_dr05.HasValue ? r_dr05.Value : 0;

                string rawDiagStr = string.Format(
                    "【[RAW] KEB 原始暫存器 RAW 遙測分析】{0} (COM{1}/Baud{2}/Node{3}):\r\n" +
                    "  • uF.00 (0x0500): Raw={4} (HEX: 0x{4:X4}) [Set1={5}, Set0={6}]\r\n" +
                    "    -> 比例試算: x0.05={7:F2}Hz, x0.025={8:F2}Hz, x0.0125={9:F2}Hz, x0.1={10:F2}Hz | 最終解析={11:F2}Hz\r\n" +
                    "  • dr.05 (0x0605): Raw={12} (HEX: 0x{12:X4}) [Set0={13}, Set1={14}, 0x0405={15}] | 最終解析={16:F1}Hz",
                    dName, com, baud, node,
                    rawUf00Val, (r_uf00_s1.HasValue ? r_uf00_s1.Value.ToString() : "null"), (r_uf00_s0.HasValue ? r_uf00_s0.Value.ToString() : "null"),
                    rawUf00Val * 0.05, rawUf00Val * 0.025, rawUf00Val * 0.0125, rawUf00Val * 0.1, ufFreq,
                    rawDr05Val, (r_dr05_s0.HasValue ? r_dr05_s0.Value.ToString() : "null"), (r_dr05_s1.HasValue ? r_dr05_s1.Value.ToString() : "null"), (r_dr05_4_s0.HasValue ? r_dr05_4_s0.Value.ToString() : "null"), drFreq);

                WriteHmiLog("KEB_RAW_DUMP", rawDiagStr);

                double drVolt = r_dr02.HasValue ? r_dr02.Value : 0.0;
                double ufVolt = r_uf09.HasValue ? r_uf09.Value : 0.0;
                double drCurr = r_dr00.HasValue ? (r_dr00.Value * 0.1) : 0.0;

                if (r_uf09.HasValue && r_uf09.Value >= 100)
                {
                    originalUf09Val = r_uf09.Value;
                    if (lblEquivCurUf09 != null)
                    {
                        lblEquivCurUf09.Text = string.Format("uf09: {0} V", r_uf09.Value);
                        lblEquivCurUf09.ForeColor = Color.FromArgb(16, 185, 129);
                    }
                }
                else if (r_uf09.HasValue)
                {
                    if (lblEquivCurUf09 != null)
                    {
                        lblEquivCurUf09.Text = string.Format("uf09: {0} V (降壓狀態)", r_uf09.Value);
                        lblEquivCurUf09.ForeColor = Color.FromArgb(245, 158, 11);
                    }
                }

                if (r_dr02.HasValue && r_dr02.Value >= 100 && originalUf09Val < 100)
                {
                    originalUf09Val = r_dr02.Value;
                }

                // 同步比對檢查
                List<string> unSyncItems = new List<string>();
                if (drFreq > 0 && ufFreq > 0 && Math.Abs(drFreq - ufFreq) > 0.5)
                {
                    unSyncItems.Add(string.Format("• 額定頻率不同步: dr.05={0:F1} Hz vs uf.00={1:F1} Hz (相差 {2:F1} Hz)", drFreq, ufFreq, Math.Abs(drFreq - ufFreq)));
                }
                if (drVolt > 0 && ufVolt > 0 && Math.Abs(drVolt - ufVolt) > 5.0)
                {
                    unSyncItems.Add(string.Format("• 額定電壓不同步: dr.02={0:F0} V vs uf.09={1:F0} V (相差 {2:F0} V)", drVolt, ufVolt, Math.Abs(drVolt - ufVolt)));
                }

                WriteHmiLog("KEB_SYNC", string.Format("【dr/uf 參數蒐集】{0}: dr00={1:F1}A, dr01={2}rpm, dr02={3}V, dr05={4:F1}Hz | uf00={5:F1}Hz, uf09={6}V | 同步: {7}",
                    dName, drCurr, r_dr01.HasValue ? r_dr01.Value.ToString() : "--", drVolt, drFreq, ufFreq, ufVolt, (unSyncItems.Count == 0 ? "已同步" : "未同步")));

                string popupRawBox = string.Format(
                    "--------------------------------------------------\r\n" +
                    "【變頻器實測 RAW 暫存器數據】\r\n" +
                    "• uF.00 (0x0500): Raw={0} (0x{0:X4}) -> 解析: {1:F2} Hz\r\n" +
                    "  [各比例: x0.05={2:F1}Hz, x0.025={3:F1}Hz, x0.1={4:F1}Hz]\r\n" +
                    "• dr.05 (0x0605): Raw={5} (0x{5:X4}) -> 解析: {6:F1} Hz\r\n" +
                    "--------------------------------------------------",
                    rawUf00Val, ufFreq, rawUf00Val * 0.05, rawUf00Val * 0.025, rawUf00Val * 0.1, rawDr05Val, drFreq);

                if (unSyncItems.Count > 0)
                {
                    string warnMsg = string.Format("【[!] KEB 內部參數未同步提醒】\r\n\r\n" +
                        "偵測到 {0} 的 dr 銘牌參數與 uf 特性曲線參數未完全同步：\r\n\r\n" +
                        string.Join("\r\n", unSyncItems.ToArray()) + "\r\n\r\n" +
                        "• 實測 dr 參數: dr01={1}rpm, dr00={2:F1}A, dr02={3:F0}V, dr05={4:F1}Hz\r\n" +
                        "• 實測 uf 參數: uf00={5:F1}Hz, uf09={6:F0}V\r\n\r\n" +
                        popupRawBox + "\r\n\r\n" +
                        "※ 提醒：請確認變頻器內部設定是否正確！",
                        dName, r_dr01.HasValue ? r_dr01.Value.ToString() : "--", drCurr, drVolt, drFreq, ufFreq, ufVolt);

                    MessageBox.Show(warnMsg, "參數同步提醒", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return false;
                }
                else
                {
                    if (showSuccessDialog)
                    {
                        string okMsg = string.Format("【[O] KEB 參數蒐集與同步驗證正常】\r\n\r\n" +
                            "{0} 的 dr 與 uf 參數均已同步一致：\r\n" +
                            "• 額定頻率: {1:F1} Hz (dr.05={1:F1} Hz, uf.00={2:F1} Hz)\r\n" +
                            "• 額定電壓: {3:F0} V (dr.02={3:F0} V, uf.09={4:F0} V)\r\n" +
                            "• 額定電流: {5:F1} A (dr.00)\r\n" +
                            "• 額定轉速: {6} rpm (dr.01)\r\n\r\n" +
                            popupRawBox + "\r\n\r\n" +
                            "變頻器參數設定良好！",
                            dName, drFreq, ufFreq, drVolt, ufVolt, drCurr, r_dr01.HasValue ? r_dr01.Value.ToString() : "--");

                        MessageBox.Show(okMsg, "參數已同步", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    }
                    return true;
                }
            }
            catch (Exception ex)
            {
                WriteHmiLog("KEB_ERR", "CheckAndCollectDrUfParams 異常: " + ex.Message);
                MessageBox.Show("讀取 dr/uf 參數異常: " + ex.Message, "錯誤", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }
        }

        private void ToggleAutoTuneUf09()
        {
            if (isLockedSweepRunning)
            {
                StopLockedRotorSweep("使用者手動中止試驗");
            }
            else
            {
                StartLockedRotorTest(singleFreqMode: true);
            }
        }

        private void StartLockedRotorTest(bool singleFreqMode)
        {
            if (isLockedSweepRunning)
            {
                StopLockedRotorSweep("使用者手動中止試驗");
                return;
            }

            try
            {
                double inRated = (double)numEquivIn.Value;
                if (inRated <= 0.5)
                {
                    MessageBox.Show("請先確認卡片 2 中的「額定線流 IN」數值正確 (不可為 0)！", "參數提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                // 檢查軸轉速 (保護：轉速 > 5 rpm 嚴禁啟動堵轉)
                double actSpdVal = Math.Abs(actSpeed);
                if (actSpdVal > 5.0)
                {
                    MessageBox.Show(string.Format("[!] 偵測到目前軸轉速為 {0:F0} rpm (大於 5 rpm)！\r\n\r\n進行堵轉測試前，必須使用專用機械夾具將待測馬達「確實剛性鎖死」！", actSpdVal),
                        "安全閉鎖·禁止啟動", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }

                int driveId = (cmbEquivKebDrive.SelectedIndex == 1) ? 1 : 2;
                string dName = (driveId == 1) ? "A載台" : "B載台";

                // (1) 額定頻率時執行 dr 與 uf 參數蒐集與同步檢查
                int selectedFreqIdx = cmbEquivLockedFreq.SelectedIndex;
                if (selectedFreqIdx == 0 || !singleFreqMode)
                {
                    bool synced = CheckAndCollectDrUfParams(showSuccessDialog: false);
                    if (!synced)
                    {
                        if (MessageBox.Show("變頻器內部 dr 與 uf 參數未完全同步，是否仍要強制繼續進行堵轉試驗？", "未同步確認", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
                        {
                            return;
                        }
                    }
                }

                string modeStr = singleFreqMode ?
                    string.Format("【單頻測試: {0}】", cmbEquivLockedFreq.SelectedItem) :
                    "【全頻率 8 點自動掃描試驗】";

                string confirmMsg = string.Format("【[AI] 堵轉自動化試驗啟動確認】\r\n\r\n" +
                    "• 試驗模式: {0}\r\n" +
                    "• 目標載台: {1}\r\n" +
                    "• 額定電流 IN: {2:F2} A\r\n" +
                    "• 起始電壓: 1/10 額定電壓 (由 uf09 蒐集)\r\n" +
                    "• 調控策略: 1V 增幅 5 次估測目標 -> 最大 5V 梯度自適應逼近\r\n" +
                    "• 採樣規範: 達到額定電流後採樣 30 筆，去 5 高 5 低後平均記錄\r\n\r\n" +
                    "※ 請確認馬達機構已確實鎖死！是否立即開始？", modeStr, dName, inRated);

                if (MessageBox.Show(confirmMsg, "啟動確認", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                {
                    return;
                }

                // 初始化狀態
                isLockedSweepRunning = true;
                isAutoTuningUf09 = true;
                isLockedRotorActive = true;
                lockedOverCurrentTicks = 0;
                lockedSpeedAnomalyTicks = 0;

                btnEquivAutoTuneUf09.Text = "[停止] 中止試驗";
                btnEquivAutoTuneUf09.BackColor = Color.FromArgb(254, 226, 226);
                if (!singleFreqMode)
                {
                    btnEquivSweepAllFreq.Text = "[停止] 中止掃描";
                    btnEquivSweepAllFreq.BackColor = Color.FromArgb(254, 226, 226);
                }

                lockedSweepThread = new System.Threading.Thread(() => LockedSweepWorker(singleFreqMode, selectedFreqIdx))
                {
                    IsBackground = true,
                    Name = "LockedRotorSweepThread"
                };
                lockedSweepThread.Start();
            }
            catch (Exception ex)
            {
                MessageBox.Show("啟動試驗失敗: " + ex.Message, "錯誤", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void StopLockedRotorSweep(string reason)
        {
            isLockedSweepRunning = false;
            isAutoTuningUf09 = false;
            isLockedRotorActive = false;
            lockedOverCurrentTicks = 0;
            lockedSpeedAnomalyTicks = 0;

            if (btnEquivAutoTuneUf09 != null)
            {
                btnEquivAutoTuneUf09.Text = "[AI] 單頻測試";
                btnEquivAutoTuneUf09.BackColor = Color.FromArgb(238, 242, 255);
            }
            if (btnEquivSweepAllFreq != null)
            {
                btnEquivSweepAllFreq.Text = "[>>] 全頻掃描";
                btnEquivSweepAllFreq.BackColor = Color.FromArgb(241, 245, 249);
            }
            if (lblLockedItemStatus != null)
            {
                lblLockedItemStatus.Text = "[--] 堵轉試驗已停止: " + reason;
                lblLockedItemStatus.ForeColor = Color.Gray;
            }

            WriteHmiLog("EQUIV", "【堵轉試驗停止】" + reason);

            // ★ 無論成功或失敗，只要停止後立即改回原本數值，防止下次測試卡住
            AutoRestoreUf09("停止試驗強制復歸: " + reason);
        }

        private void LockedSweepWorker(bool singleFreqMode, int singleFreqIndex)
        {
            try
            {
                int driveId = (cmbEquivKebDrive.SelectedIndex == 1) ? 1 : 2;
                int com = GetHmiKebComIdx(driveId);
                int baud = GetHmiKebBaudIdx(driveId);
                int node = (driveId == 1) ? (int)numHmiKebNode1.Value : (int)numHmiKebNode2.Value;
                double inRated = (double)numEquivIn.Value;
                if (inRated <= 0.5) inRated = 32.3;
                double f0 = (numEquivF0.Value > 1.0m) ? (double)numEquivF0.Value : 50.0;

                // 讀取原始 uf09 基準電壓 (低於 100V 視為降壓狀態，不予採納為原始值)
                int? origVal = KebReadUf09(driveId);
                if (origVal.HasValue && origVal.Value >= 100) originalUf09Val = origVal.Value;
                int ratedVolt = GetOriginalOrRatedUf09(driveId);

                int startFreqIdx = singleFreqMode ? singleFreqIndex : 0;
                int endFreqIdx = singleFreqMode ? singleFreqIndex : 7;

                for (int idx = startFreqIdx; idx <= endFreqIdx; idx++)
                {
                    if (!isLockedSweepRunning) break;

                    var item = lockedSweepItems[idx];
                    double fRatio = item.FreqRatio;
                    double fTest = f0 * fRatio;
                    item.TargetFreq = fTest;

                    this.Invoke((MethodInvoker)delegate {
                        lblLockedItemStatus.Text = string.Format("[AI] 正在設定頻率: {0} ({1:F1} Hz)...", item.FreqName, fTest);
                        lblLockedItemStatus.ForeColor = Color.FromArgb(79, 70, 229);
                        cmbEquivLockedFreq.SelectedIndex = idx;
                    });

                    // 1. 起始安全電壓估算 (阻抗繼承法 或 頻率等比例折算法，避免低頻漏抗減半導致電流過大)
                    int startV;
                    if (idx > startFreqIdx && lockedSweepItems[idx - 1].IsCompleted && lockedSweepItems[idx - 1].Lk_mH > 0 && lockedSweepItems[idx - 1].AvgIk > 0.1)
                    {
                        // 阻抗繼承預估法：以前一頻率測得之 Lk 與 Rk 外推新頻率理論目標電壓，取 60% 作為起點
                        double prevLk = lockedSweepItems[idx - 1].Lk_mH / 1000.0;
                        double prevRk = lockedSweepItems[idx - 1].AvgPk / (3.0 * Math.Pow(lockedSweepItems[idx - 1].AvgIk, 2));
                        double omegaTest = 2.0 * Math.PI * fTest;
                        double estZk = Math.Sqrt(prevRk * prevRk + Math.Pow(omegaTest * prevLk, 2));
                        double estVline = (inRated * estZk) * Math.Sqrt(3.0);
                        startV = Math.Max(5, Math.Min((int)Math.Round(ratedVolt * 0.15), (int)Math.Round(estVline * 0.60)));
                        WriteHmiLog("EQUIV", string.Format("【阻抗繼承預估起步電壓】{0}: 前頻Lk={1:F3}mH, 理論Zk={2:F3}Ω, 起步電壓={3}V", item.FreqName, lockedSweepItems[idx - 1].Lk_mH, estZk, startV));
                    }
                    else
                    {
                        // 頻率等比例折算法：基頻為 10% 額定電壓，隨頻率倍率折算，最低保底 5V
                        double scaledRatio = Math.Min(1.0, Math.Max(0.2, fRatio));
                        startV = Math.Max(5, (int)Math.Round(ratedVolt * 0.10 * scaledRatio));
                    }

                    item.StartVoltage = startV;
                    int curV = startV;

                    // 步驟 A: 先將 uf.09 寫入安全起步電壓 (消除高壓切換過電流風險)
                    KebWriteUf09(driveId, curV);

                    // 步驟 B: 設定變頻器輸出頻率 (透過 Sy.52 與 oP.03 給定轉速 rpm = 120 * fTest / poles)
                    int poles = (kebMotorPoles2 > 0) ? kebMotorPoles2 : 4;
                    double targetRpm = (120.0 * fTest) / poles;
                    KebWriteParam32(com, baud, node, 0x0034, (int)Math.Round(targetRpm), string.Format("堵轉頻率 {0:F1}Hz (Sy.52)", fTest));
                    KebWriteParam32(com, baud, node, 0x0303, (int)Math.Round(targetRpm * 8), string.Format("堵轉轉速 (oP.03)", fTest));

                    this.Invoke((MethodInvoker)delegate {
                        lblEquivCurUf09.Text = string.Format("uf09: {0} V", curV);
                        lblLockedItemStatus.Text = string.Format("[AI] {0} ({1:F1}Hz) 起始電壓: {2}V, Sy.52={3:F0}rpm，握手鎖定中...", item.FreqName, fTest, startV, targetRpm);
                    });
                    System.Threading.Thread.Sleep(400); // 握手響應 400ms，無須多餘冷卻停頓

                    // 步驟 C: 初測電流安全檢驗 (若切換後電流偏大 > 80% IN，啟動反向階梯回退降壓)
                    double iInit = GetCurrentSample();
                    if (iInit > inRated * 0.80)
                    {
                        WriteHmiLog("EQUIV_WARN", string.Format("【起步電流偏大預警】{0} 初測電流 {1:F2}A 達額定 {2:F2}A 之 80%，啟動反向階梯回退降壓！", item.FreqName, iInit, inRated));
                        int rollbackCount = 0;
                        while (isLockedSweepRunning && curV > 5 && GetCurrentSample() > inRated * 0.70 && rollbackCount < 10)
                        {
                            rollbackCount++;
                            curV = Math.Max(5, curV - 2);
                            KebWriteUf09(driveId, curV);
                            this.Invoke((MethodInvoker)delegate {
                                lblEquivCurUf09.Text = string.Format("uf09: {0} V", curV);
                                lblLockedItemStatus.Text = string.Format("[AI] {0} 電流過大回退降壓: uf09={1}V, Ik={2:F2}A", item.FreqName, curV, GetCurrentSample());
                            });
                            System.Threading.Thread.Sleep(300);
                        }
                        iInit = GetCurrentSample();
                    }

                    // 2. 做 1V 增幅 5 次估測目標電流所需電壓
                    List<KeyValuePair<int, double>> probePts = new List<KeyValuePair<int, double>>();
                    probePts.Add(new KeyValuePair<int, double>(curV, iInit));

                    for (int step = 1; step <= 5; step++)
                    {
                        if (!isLockedSweepRunning) break;
                        curV += 1;
                        KebWriteUf09(driveId, curV);

                        this.Invoke((MethodInvoker)delegate {
                            lblEquivCurUf09.Text = string.Format("uf09: {0} V", curV);
                            lblLockedItemStatus.Text = string.Format("[AI] {0} 斜率探測 ({1}/5): uf09={2}V, 實測Ik={3:F2}A",
                                item.FreqName, step, curV, GetCurrentSample());
                        });
                        System.Threading.Thread.Sleep(1000);

                        double iSample = GetCurrentSample();
                        probePts.Add(new KeyValuePair<int, double>(curV, iSample));
                    }
                    if (!isLockedSweepRunning) break;

                    // 計算斜率 k = ΔI / ΔV
                    double dv = probePts[probePts.Count - 1].Key - probePts[0].Key;
                    double di = probePts[probePts.Count - 1].Value - probePts[0].Value;
                    double slope = (dv > 0 && di > 0.02) ? (di / dv) : 0.5;
                    if (slope < 0.05) slope = 0.05;

                    double iAfterProbe = probePts[probePts.Count - 1].Value;
                    int estV = curV + (int)Math.Round((inRated - iAfterProbe) / slope);
                    estV = Math.Max(5, Math.Min(220, estV));
                    item.EstimatedVoltage = estV;

                    WriteHmiLog("EQUIV", string.Format("【堵轉 5 步 1V 估測】{0}: 斜率 k={1:F3} A/V, 估測目標電壓={2}V (目前={3}V, Ik={4:F2}A, 目標={5:F2}A)",
                        item.FreqName, slope, estV, curV, iAfterProbe, inRated));

                    // 4. 以最大 5V 梯度自適應縮小增量，增加達到額定電流
                    int loopCount = 0;
                    int bestV = curV;
                    double bestDiff = 9999.0;

                    while (isLockedSweepRunning && loopCount < 30)
                    {
                        loopCount++;
                        double curI = GetCurrentSample();
                        double diff = Math.Abs(curI - inRated);
                        if (diff < bestDiff)
                        {
                            bestDiff = diff;
                            bestV = curV;
                        }

                        // 判斷是否收斂 (誤差在 0.3A 內或 1%)
                        if (diff <= Math.Max(0.3, inRated * 0.01))
                        {
                            break;
                        }

                        // 梯度計算：最大 5V，越接近目標電流縮小增量
                        double deltaI = inRated - curI;
                        int stepV = 0;
                        if (deltaI >= 5.0) stepV = 5;
                        else if (deltaI >= 3.0) stepV = 3;
                        else if (deltaI >= 1.0) stepV = 2;
                        else if (deltaI > 0.3) stepV = 1;
                        else if (deltaI <= -2.0) stepV = -2;
                        else if (deltaI < -0.3) stepV = -1;

                        if (stepV == 0) break;

                        curV += stepV;
                        curV = Math.Max(5, Math.Min(220, curV));
                        KebWriteUf09(driveId, curV);

                        this.Invoke((MethodInvoker)delegate {
                            lblEquivCurUf09.Text = string.Format("uf09: {0} V", curV);
                            lblLockedItemStatus.Text = string.Format("[AI] {0} 自適應逼近 (梯度 {1:+0;-0}V): uf09={2}V, Ik={3:F2}A (目標 {4:F2}A)",
                                item.FreqName, stepV, curV, curI, inRated);
                        });
                        System.Threading.Thread.Sleep(1000);
                    }

                    if (!isLockedSweepRunning) break;

                    // 若最後微幅超標或跳動，鎖定最接近額定之電壓
                    if (curV != bestV)
                    {
                        curV = bestV;
                        KebWriteUf09(driveId, curV);
                        System.Threading.Thread.Sleep(800);
                    }
                    item.ConvergedUf09 = curV;

                    // 5. 達到額定電流後，連續蒐集 30 筆資料，去掉最高最低各 5 筆後平均記錄
                    this.Invoke((MethodInvoker)delegate {
                        lblLockedItemStatus.Text = string.Format("[AI] {0} 已收斂至額定流 (uf09={1}V)！正在採樣 30 筆數據 (去5高5低)...", item.FreqName, curV);
                        lblLockedItemStatus.ForeColor = Color.FromArgb(16, 185, 129);
                    });

                    List<LockedSamplePoint> samples = new List<LockedSamplePoint>();
                    for (int s = 0; s < 30; s++)
                    {
                        if (!isLockedSweepRunning) break;
                        System.Threading.Thread.Sleep(100);
                        double v = (actVoltageSigma > 1.0) ? actVoltageSigma : ((wtU1 + wtU2 + wtU3) / 3.0);
                        double curI = (actCurrentSigma > 0.05) ? actCurrentSigma : ((wtI1 + wtI2 + wtI3) / 3.0);
                        double p = (actElecPower > 0.001) ? (actElecPower * 1000.0) : (wtP1 + wtP2 + wtP3);
                        double pf = (wtPFSig > 0.0) ? wtPFSig : 0.29;
                        samples.Add(new LockedSamplePoint { V = v, I = curI, P = p, PF = pf });
                    }

                    if (!isLockedSweepRunning) break;

                    if (samples.Count == 30)
                    {
                        // 排序並剔除最高最低各 5 筆
                        samples.Sort((a, b) => a.I.CompareTo(b.I));
                        double sumV = 0, sumI = 0, sumP = 0, sumPF = 0;
                        for (int s = 5; s < 25; s++)
                        {
                            sumV += samples[s].V;
                            sumI += samples[s].I;
                            sumP += samples[s].P;
                            sumPF += samples[s].PF;
                        }
                        double avgV = sumV / 20.0;
                        double avgI = sumI / 20.0;
                        double avgP = sumP / 20.0;
                        double avgPF = sumPF / 20.0;

                        item.AvgVk = avgV;
                        item.AvgIk = avgI;
                        item.AvgPk = avgP;
                        item.AvgPFk = avgPF;

                        // 物理漏抗與電感換算
                        double vPh = avgV / Math.Sqrt(3.0);
                        double iPh = avgI;
                        double pPh = avgP / 3.0;
                        double zk = (iPh > 0.01) ? (vPh / iPh) : 0.1;
                        double rk = (iPh > 0.01) ? (pPh / (iPh * iPh)) : 0.05;
                        double xk_sqr = (zk * zk) - (rk * rk);
                        double xk_meas = xk_sqr > 0 ? Math.Sqrt(xk_sqr) : 0.1;
                        double xk_ref = xk_meas / fRatio;
                        double omega_test = 2.0 * Math.PI * fTest;
                        double lk_mH = (xk_meas / omega_test) * 1000.0;

                        item.Xk_meas = xk_meas;
                        item.Xk_ref = xk_ref;
                        item.Lk_mH = lk_mH;
                        item.IsCompleted = true;
                        item.TestTime = DateTime.Now;

                        // 若為當前介面選中頻率，回填至卡片數值框
                        if (idx == cmbEquivLockedFreq.SelectedIndex)
                        {
                            this.Invoke((MethodInvoker)delegate {
                                numEquivVk.Value = (decimal)Math.Round(avgV, 1);
                                numEquivIk.Value = (decimal)Math.Round(avgI, 2);
                                numEquivPk.Value = (decimal)Math.Round(avgP, 1);
                                numEquivPfk.Value = (decimal)Math.Round(avgPF, 3);
                                isLockedDataReady = true;
                            });
                        }

                        WriteHmiLog("EQUIV_LOCKED", string.Format("【堵轉 30 筆採樣完成】{0} ({1:F1}Hz): 去5高5低平均 -> Vk={2:F1}V, Ik={3:F2}A, Pk={4:F1}W, PFk={5:F3} | Xk_meas={6:F4}Ω, Xk_ref={7:F4}Ω, Lk={8:F3}mH",
                            item.FreqName, fTest, avgV, avgI, avgP, avgPF, xk_meas, xk_ref, lk_mH));
                    }

                    // 單頻或各步完成後，退回安全起步電壓無縫切換下一頻率 (無需冷卻停頓)
                    if (!singleFreqMode && idx < endFreqIdx)
                    {
                        int nextStartV = Math.Max(5, (int)Math.Round(ratedVolt * 0.10 * Math.Min(1.0, Math.Max(0.2, lockedSweepItems[idx + 1].FreqRatio))));
                        KebWriteUf09(driveId, nextStartV);
                        this.Invoke((MethodInvoker)delegate {
                            lblEquivCurUf09.Text = string.Format("uf09: {0} V", nextStartV);
                            lblLockedItemStatus.Text = string.Format("[AI] {0} 完成，退回起步電壓 {1}V 直接推進下一頻率...", item.FreqName, nextStartV);
                        });
                        System.Threading.Thread.Sleep(300); // 僅需 300ms 暫態響應，零冷卻停頓
                    }
                }

                // 結束處置
                this.Invoke((MethodInvoker)delegate {
                    isLockedSweepRunning = false;
                    isAutoTuningUf09 = false;
                    isLockedRotorActive = false;
                    btnEquivAutoTuneUf09.Text = "[AI] 單頻測試";
                    btnEquivAutoTuneUf09.BackColor = Color.FromArgb(238, 242, 255);
                    btnEquivSweepAllFreq.Text = "[>>] 全頻掃描";
                    btnEquivSweepAllFreq.BackColor = Color.FromArgb(241, 245, 249);
                    lblLockedItemStatus.Text = "[O] 堵轉試驗完成 (30筆去極端值平均已記錄)";
                    lblLockedItemStatus.ForeColor = Color.FromArgb(16, 185, 129);
                });

                if (!singleFreqMode)
                {
                    this.Invoke((MethodInvoker)delegate {
                        MessageBox.Show("三相感應馬達 8 個頻率點堵轉試驗全數完成！\r\n\r\n所有頻率均已採樣 30 筆電氣數據並完成去 5 高 5 低平均運算。\r\n即將開啟 8 頻率完整成果紀錄表。",
                            "全頻掃描完成", MessageBoxButtons.OK, MessageBoxIcon.Information);
                        ShowLockedSweepResultsDialog();
                    });
                }
                else
                {
                    this.Invoke((MethodInvoker)delegate {
                        MessageBox.Show(string.Format("【{0}】堵轉自適應試驗完成！\r\n\r\n已成功採樣 30 筆電氣量並完成去極端值平均運算，數據已填入堵轉卡片。", lockedSweepItems[singleFreqIndex].FreqName),
                            "單頻試驗完成", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    });
                }
            }
            catch (Exception ex)
            {
                WriteHmiLog("KEB_ERR", "LockedSweepWorker 例外: " + ex.Message);
                this.Invoke((MethodInvoker)delegate {
                    StopLockedRotorSweep("試驗發生例外: " + ex.Message);
                });
            }
            finally
            {
                isLockedSweepRunning = false;
                isAutoTuningUf09 = false;
                isLockedRotorActive = false;
                AutoRestoreUf09("堵轉測試線程安全結束復歸");
            }
        }

        private void ShowLockedSweepResultsDialog()
        {
            Form dlg = new Form()
            {
                Text = "三相感應馬達 8 頻率堵轉測試紀錄表 (30筆去極端值平均)",
                Size = new Size(980, 520),
                MinimumSize = new Size(800, 400),
                StartPosition = FormStartPosition.CenterParent,
                BackColor = Color.White,
                Font = new Font("微軟正黑體", 9f)
            };

            // 頂部資訊橫條
            Panel pnlHeader = new Panel()
            {
                Dock = DockStyle.Top,
                Height = 46,
                BackColor = Color.FromArgb(248, 250, 252),
                Padding = new Padding(12, 6, 12, 6)
            };
            Label lblInfo = new Label()
            {
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                Font = new Font("微軟正黑體", 9.5f, FontStyle.Bold),
                ForeColor = Color.FromArgb(30, 41, 59),
                Text = string.Format("待測馬達: 【{0}】 | 額定電流 IN: {1:F2} A | 基準額定電壓: {2:F0} V | 採樣標準: 30筆採樣 (剔除最高5筆+最低5筆, 20筆平均)",
                    !string.IsNullOrEmpty(motorModelName) ? motorModelName : "SVM100S",
                    numEquivIn.Value,
                    originalUf09Val)
            };
            pnlHeader.Controls.Add(lblInfo);

            // 中間 DataGridView
            DataGridView dgv = new DataGridView()
            {
                Dock = DockStyle.Fill,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                ReadOnly = true,
                RowHeadersVisible = false,
                BackgroundColor = Color.White,
                BorderStyle = BorderStyle.None,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill
            };
            dgv.ColumnHeadersDefaultCellStyle.Font = new Font("微軟正黑體", 9f, FontStyle.Bold);
            dgv.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(241, 245, 249);

            dgv.Columns.Add("Index", "項次");
            dgv.Columns.Add("FreqName", "試驗頻率項目");
            dgv.Columns.Add("FreqRatio", "頻率比");
            dgv.Columns.Add("TargetFreq", "試驗頻率 (Hz)");
            dgv.Columns.Add("StartV", "起始電壓 (V)");
            dgv.Columns.Add("EstV", "估測目標 (V)");
            dgv.Columns.Add("Uf09", "收斂uf09 (V)");
            dgv.Columns.Add("AvgVk", "平均Vk (V)");
            dgv.Columns.Add("AvgIk", "平均Ik (A)");
            dgv.Columns.Add("AvgPk", "平均Pk (W)");
            dgv.Columns.Add("AvgPFk", "平均PFk");
            dgv.Columns.Add("Xk_ref", "折算漏抗 (Ω)");
            dgv.Columns.Add("Lk_mH", "漏電感 (mH)");
            dgv.Columns.Add("Status", "狀態");

            dgv.Columns["Index"].FillWeight = 40;
            dgv.Columns["FreqName"].FillWeight = 110;
            dgv.Columns["FreqRatio"].FillWeight = 55;
            dgv.Columns["TargetFreq"].FillWeight = 75;
            dgv.Columns["StartV"].FillWeight = 65;
            dgv.Columns["EstV"].FillWeight = 65;
            dgv.Columns["Uf09"].FillWeight = 65;
            dgv.Columns["AvgVk"].FillWeight = 65;
            dgv.Columns["AvgIk"].FillWeight = 65;
            dgv.Columns["AvgPk"].FillWeight = 75;
            dgv.Columns["AvgPFk"].FillWeight = 55;
            dgv.Columns["Xk_ref"].FillWeight = 75;
            dgv.Columns["Lk_mH"].FillWeight = 75;
            dgv.Columns["Status"].FillWeight = 60;

            foreach (var it in lockedSweepItems)
            {
                dgv.Rows.Add(
                    it.Index,
                    it.FreqName,
                    string.Format("{0:F2}x", it.FreqRatio),
                    string.Format("{0:F1}", it.TargetFreq),
                    it.StartVoltage > 0 ? it.StartVoltage.ToString() : "--",
                    it.EstimatedVoltage > 0 ? it.EstimatedVoltage.ToString() : "--",
                    it.ConvergedUf09 > 0 ? it.ConvergedUf09.ToString() : "--",
                    it.IsCompleted ? it.AvgVk.ToString("F1") : "--",
                    it.IsCompleted ? it.AvgIk.ToString("F2") : "--",
                    it.IsCompleted ? it.AvgPk.ToString("F1") : "--",
                    it.IsCompleted ? it.AvgPFk.ToString("F3") : "--",
                    it.IsCompleted ? it.Xk_ref.ToString("F4") : "--",
                    it.IsCompleted ? it.Lk_mH.ToString("F3") : "--",
                    it.IsCompleted ? "[O] 已完成" : "[--] 待測"
                );
            }

            // 底部按鈕列
            Panel pnlBottom = new Panel()
            {
                Dock = DockStyle.Bottom,
                Height = 48,
                BackColor = Color.FromArgb(248, 250, 252),
                Padding = new Padding(12, 8, 12, 8)
            };
            FlowLayoutPanel flpBtns = new FlowLayoutPanel()
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.RightToLeft
            };

            Button btnClose = new Button()
            {
                Text = "關閉",
                Size = new Size(80, 30),
                BackColor = Color.FromArgb(241, 245, 249)
            };
            btnClose.Click += (s, e) => dlg.Close();

            Button btnExportCsv = new Button()
            {
                Text = "匯出 CSV 報表",
                Size = new Size(110, 30),
                BackColor = Color.FromArgb(241, 245, 249)
            };
            btnExportCsv.Click += (s, e) => ExportLockedSweepCsv();

            Button btnApplySelected = new Button()
            {
                Text = "套用至等效電路",
                Size = new Size(120, 30),
                BackColor = Color.FromArgb(238, 242, 255),
                ForeColor = Color.FromArgb(79, 70, 229),
                Font = new Font("微軟正黑體", 9f, FontStyle.Bold)
            };
            btnApplySelected.Click += (s, e) => {
                if (dgv.SelectedRows.Count > 0)
                {
                    int rIdx = dgv.SelectedRows[0].Index;
                    if (rIdx >= 0 && rIdx < lockedSweepItems.Count)
                    {
                        var selItem = lockedSweepItems[rIdx];
                        if (selItem.IsCompleted)
                        {
                            cmbEquivLockedFreq.SelectedIndex = rIdx;
                            numEquivVk.Value = (decimal)Math.Round(selItem.AvgVk, 1);
                            numEquivIk.Value = (decimal)Math.Round(selItem.AvgIk, 2);
                            numEquivPk.Value = (decimal)Math.Round(selItem.AvgPk, 1);
                            numEquivPfk.Value = (decimal)Math.Round(selItem.AvgPFk, 3);
                            isLockedDataReady = true;
                            MessageBox.Show(string.Format("已成功套用【{0}】之堵轉實驗數據：\r\n• Vk = {1:F1} V\r\n• Ik = {2:F2} A\r\n• Pk = {3:F1} W\r\n• PFk = {4:F3}",
                                selItem.FreqName, selItem.AvgVk, selItem.AvgIk, selItem.AvgPk, selItem.AvgPFk),
                                "套用成功", MessageBoxButtons.OK, MessageBoxIcon.Information);
                            dlg.Close();
                        }
                        else
                        {
                            MessageBox.Show("所選項目尚未完成實驗採樣！", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        }
                    }
                }
            };

            flpBtns.Controls.Add(btnClose);
            flpBtns.Controls.Add(btnExportCsv);
            flpBtns.Controls.Add(btnApplySelected);
            pnlBottom.Controls.Add(flpBtns);

            dlg.Controls.Add(dgv);
            dlg.Controls.Add(pnlBottom);
            dlg.Controls.Add(pnlHeader);

            dlg.ShowDialog(this);
        }

        private void ExportLockedSweepCsv()
        {
            try
            {
                SaveFileDialog sfd = new SaveFileDialog()
                {
                    Filter = "CSV 檔案 (*.csv)|*.csv",
                    FileName = string.Format("LockedRotor_8Freq_Sweep_{0}_{1:yyyyMMdd_HHmmss}.csv",
                        !string.IsNullOrEmpty(motorModelName) ? motorModelName : "Motor", DateTime.Now)
                };
                if (sfd.ShowDialog() == DialogResult.OK)
                {
                    StringBuilder sb = new StringBuilder();
                    sb.AppendLine("三相感應馬達 8 頻率堵轉測試紀錄報表");
                    sb.AppendLine(string.Format("馬達型號,{0}", motorModelName));
                    sb.AppendLine(string.Format("額定電流 IN (A),{0}", numEquivIn.Value));
                    sb.AppendLine(string.Format("基準電壓 (V),{0}", originalUf09Val));
                    sb.AppendLine(string.Format("匯出時間,{0:yyyy-MM-dd HH:mm:ss}", DateTime.Now));
                    sb.AppendLine("採樣說明,達到額定電流後採樣30筆電氣量 剔除電流最高5筆與最低5筆 取中間20筆算術平均");
                    sb.AppendLine();
                    sb.AppendLine("項次,試驗頻率項目,頻率比,試驗頻率(Hz),起始電壓(V),估測目標電壓(V),收斂uf09(V),平均電壓Vk(V),平均電流Ik(A),平均功率Pk(W),平均功率因數PFk,實測漏抗Xk_meas(Ω),折算額定漏抗Xk_ref(Ω),換算漏電感Lk(mH),測試時間,狀態");

                    foreach (var it in lockedSweepItems)
                    {
                        sb.AppendLine(string.Format("{0},{1},{2:F2}x,{3:F1},{4},{5},{6},{7:F2},{8:F2},{9:F1},{10:F3},{11:F4},{12:F4},{13:F3},{14:yyyy-MM-dd HH:mm:ss},{15}",
                            it.Index,
                            it.FreqName,
                            it.FreqRatio,
                            it.TargetFreq,
                            it.StartVoltage,
                            it.EstimatedVoltage,
                            it.ConvergedUf09,
                            it.AvgVk,
                            it.AvgIk,
                            it.AvgPk,
                            it.AvgPFk,
                            it.Xk_meas,
                            it.Xk_ref,
                            it.Lk_mH,
                            it.TestTime,
                            it.IsCompleted ? "已完成" : "未完成"));
                    }

                    File.WriteAllText(sfd.FileName, sb.ToString(), Encoding.UTF8);
                    MessageBox.Show("8 頻率堵轉測試報表已成功匯出至：\r\n" + sfd.FileName, "匯出成功", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("匯出 CSV 失敗: " + ex.Message, "錯誤", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        // ── 堵轉看門狗：電流與時間保護 (110% IN / 10s) 及轉速防脫扣 (5rpm / 3s) ──────
        private void TmrLockedWatchdog_Tick(object sender, EventArgs e)
        {
            try
            {

                // ★【極重要鐵律】：堵轉看門狗保護 ONLY 在「正在執行堵轉自適應調壓 (isAutoTuningUf09)」或「明確啟動堵轉測試 (isLockedRotorActive)」時才生效！
                // 嚴禁在平時使用者只是查看等效電路分頁、或馬達正在運轉其他測試 (例如 S1 測試 1465 rpm) 時誤判！
                if (!isAutoTuningUf09 && !isLockedRotorActive)
                {
                    lockedOverCurrentTicks = 0;
                    lockedSpeedAnomalyTicks = 0;
                    if (lblLockedProtStatus != null && !lblLockedProtStatus.Text.Contains("警告") && !lblLockedProtStatus.Text.Contains("跳脫"))
                    {
                        lblLockedProtStatus.Text = "[防護] 堵轉防護待命 (未啟動堵轉測試)";
                        lblLockedProtStatus.ForeColor = Color.FromArgb(100, 116, 139);
                    }
                    return;
                }

                double inRated = (double)numEquivIn.Value;
                if (inRated <= 0.5) inRated = 32.3;
                double iThreshold = inRated * 1.10; // 額定電流 +10% 閥值

                // 實測電流
                double iCur = (actCurrentSigma > 0.05) ? actCurrentSigma : ((wtI1 + wtI2 + wtI3) / 3.0);
                if (iCur <= 0.05 && lastB_Dr00.HasValue) iCur = (double)lastB_Dr00.Value;

                // 實測轉速
                double spdCur = Math.Abs(actSpeed);
                if (spdCur <= 0.5 && lastB_Dr01.HasValue) spdCur = Math.Abs((double)lastB_Dr01.Value);

                // ── 1. 電流閥值超標且持續 10 秒保護 ──
                if (iCur > iThreshold)
                {
                    lockedOverCurrentTicks++;
                    double elapsedSec = lockedOverCurrentTicks * 0.5;
                    if (lblLockedProtStatus != null)
                    {
                        lblLockedProtStatus.Text = string.Format("[!]【電流超標預警】實測 {0:F1}A > 閥值 {1:F1}A！累計 {2:F1}s / 10.0s (達標將緊急跳脫)",
                            iCur, iThreshold, elapsedSec);
                        lblLockedProtStatus.ForeColor = Color.FromArgb(220, 38, 38);
                    }

                    if (lockedOverCurrentTicks >= 20) // 20 * 0.5s = 10.0s
                    {
                        TriggerLockedProtectionTrip(
                            "堵轉過電流超時保護 (Over-Current > 110% IN)",
                            string.Format("• 基準額定電流 IN = {0:F2} A\r\n• 過電流保護閥值 (110%) = {1:F2} A\r\n• 實測堵轉電流 = {2:F2} A (超標 {3:F2} A)\r\n• 累計超標時間 = {4:F1} 秒 (已達 10 秒跳脫上限)",
                            inRated, iThreshold, iCur, iCur - iThreshold, elapsedSec));
                        return;
                    }
                }
                else
                {
                    if (lockedOverCurrentTicks > 0) lockedOverCurrentTicks = Math.Max(0, lockedOverCurrentTicks - 1);
                }

                // ── 2. 轉速異常大於 5 rpm 且持續超過 3 秒保護 (治具脫扣防護) ──
                if (spdCur > 5.0)
                {
                    lockedSpeedAnomalyTicks++;
                    double elapsedSec = lockedSpeedAnomalyTicks * 0.5;
                    if (lblLockedProtStatus != null)
                    {
                        lblLockedProtStatus.Text = string.Format("[!]【轉速異常預警】轉速 {0:F0} rpm > 5 rpm！累計 {1:F1}s / 3.0s (判定治具脫扣)",
                            spdCur, elapsedSec);
                        lblLockedProtStatus.ForeColor = Color.FromArgb(220, 38, 38);
                    }

                    if (lockedSpeedAnomalyTicks >= 6) // 6 * 0.5s = 3.0s
                    {
                        TriggerLockedProtectionTrip(
                            "堵轉轉速異常跳脫 (治具脫扣/機械鎖死失效)",
                            string.Format("• 實測扭力計軸轉速 = {0:F0} rpm (容許上限 5 rpm)\r\n• 累計轉動時間 = {1:F1} 秒 (已達 3 秒跳脫上限)\r\n• 判定結果：待測馬達機械鎖死治具已脫扣或未能承受堵轉扭力！為防止飛脫、劇烈旋轉造成機件毀損，系統已執行緊急停機。",
                            spdCur, elapsedSec));
                        return;
                    }
                }
                else
                {
                    lockedSpeedAnomalyTicks = 0;
                }

                // 若一切正常且無警告，刷新指示為正常綠色
                if (lockedOverCurrentTicks == 0 && lockedSpeedAnomalyTicks == 0 && lblLockedProtStatus != null)
                {
                    lblLockedProtStatus.Text = string.Format("[防護] 實時防護監控中 | 電流閥值: {0:F1}A (110% IN, 0/10s) | 轉速閥值: 5 rpm (0/3s)", iThreshold);
                    lblLockedProtStatus.ForeColor = Color.FromArgb(16, 185, 129);
                }
            }
            catch (Exception ex)
            {
                WriteHmiLog("LOCKED_ERR", "TmrLockedWatchdog_Tick 例外: " + ex.Message);
            }
        }

        // ── 堵轉保護機制緊急跳脫處置函式 ──────────────────────────────
        private void TriggerLockedProtectionTrip(string title, string details)
        {
            if (isLockedTripShowing) return; // ★ 重入鎖保護：已在顯示對話框時嚴禁重複進入
            isLockedTripShowing = true;
            try
            {
                isAutoTuningUf09 = false;
                isLockedRotorActive = false;
                lockedOverCurrentTicks = 0;
                lockedSpeedAnomalyTicks = 0;

                if (btnEquivAutoTuneUf09 != null)
                {
                    btnEquivAutoTuneUf09.Text = "[AI] 自適應追隨額定流";
                    btnEquivAutoTuneUf09.BackColor = Color.FromArgb(238, 242, 255);
                }

                // 1. 立即切斷變頻器輸出 (Sy.50 = 0)
                int driveId = (cmbEquivKebDrive != null && cmbEquivKebDrive.SelectedIndex == 1) ? 1 : 2;
                int com = GetHmiKebComIdx(driveId);
                int baud = GetHmiKebBaudIdx(driveId);
                int node = (driveId == 1) ? (int)numHmiKebNode1.Value : (int)numHmiKebNode2.Value;
                KebWriteParamWithDll(com, baud, node, 0x0032, 0); // Sy.50 = 0 (強制停機切斷輸出激磁)

                // 2. KEB uf.09 立即自動恢復至額定基準電壓 (絕不殘留 0V 或低壓，防止下次測試卡住)
                int restoreV = GetOriginalOrRatedUf09(driveId);
                AutoRestoreUf09("保護跳脫後自動恢復額定基準電壓");

                // 3. 更新 UI 狀態
                if (lblLockedItemStatus != null)
                {
                    lblLockedItemStatus.Text = "[■ 急停] 保護機制已觸發跳脫: " + title;
                    lblLockedItemStatus.ForeColor = Color.FromArgb(220, 38, 38);
                }
                if (lblLockedProtStatus != null)
                {
                    lblLockedProtStatus.Text = "[■ 急停]【緊急停機】" + title;
                    lblLockedProtStatus.ForeColor = Color.FromArgb(220, 38, 38);
                }
                if (lblEquivCurUf09 != null)
                {
                    lblEquivCurUf09.Text = string.Format("uf09: {0} V (已復歸)", restoreV);
                    lblEquivCurUf09.ForeColor = Color.FromArgb(16, 185, 129);
                }

                // 4. 記錄 HMI 日誌
                WriteHmiLog("LOCKED_PROT_TRIP", string.Format("【[!] 堵轉保護緊急跳脫】{0} | 詳情: {1}", title, details.Replace("\r\n", " | ")));

                // 5. 彈跳警示對話框 (詳細告知使用者跳脫原因，非程式 BUG)
                string alertMsg = string.Format("[!]【堵轉安全防護機制緊急跳脫·非軟體異常】\r\n\r\n" +
                    "觸發保護類型：{0}\r\n\r\n" +
                    "【實測數據與觸發條件】：\r\n{1}\r\n\r\n" +
                    "【[防護] 系統已主動完成處置措施】：\r\n" +
                    "1. 已立即下達 Sy.50 = 0 切斷變頻器輸出 (停止定子激磁)\r\n" +
                    "2. KEB uf.09 輸出電壓已自動復歸為額定基準值 ({2} V)，防止下次測試卡死\r\n" +
                    "3. 自適應調壓程序已安全中止\r\n\r\n" +
                    "※ 說明：此為保護馬達不致過熱燒毀及防止治具脫扣之主動安全機制。\r\n" +
                    "請確認待測馬達冷卻狀況與機械鎖死治具後，再行測試。",
                    title, details, restoreV);

                MessageBox.Show(alertMsg, "[!] 堵轉安全防護跳脫", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            catch (Exception ex)
            {
                WriteHmiLog("LOCKED_ERR", "TriggerLockedProtectionTrip 例外: " + ex.Message);
            }
            finally
            {
                isLockedTripShowing = false;
            }
        }

        #endregion

        #region 等效電路演算法與結果計算核心 (IEEE Std 112)

        private void ExecuteEquivCircuitCalculation()
        {
            try
            {
                // 讀取輸入參數
                double V0 = (double)numEquivV0.Value;
                double I0 = (double)numEquivI0.Value;
                double P0 = (double)numEquivP0.Value;
                double f0 = (double)numEquivF0.Value;

                double VN = (double)numEquivVn.Value;
                double IN = (double)numEquivIn.Value;
                double TN = (double)numEquivTn.Value;
                double NN = (double)numEquivNn.Value;

                double Vk = (double)numEquivVk.Value;
                double Ik = (double)numEquivIk.Value;
                double Pk = (double)numEquivPk.Value;

                if (I0 <= 0 || Ik <= 0 || VN <= 0)
                {
                    MessageBox.Show("請確認空載電流 I0、堵轉電流 Ik 與額定電壓 VN 皆大於 0！", "數據不齊全", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                EquivCircuitResult res = new EquivCircuitResult();

                // 1. 堵轉實驗參數 (Locked-Rotor Test - 單相等效)
                // 線電壓轉換為相電壓 (Y 接): Vphase = V / sqrt(3), Iphase = I, Pphase = P / 3
                double Vphase_k = Vk / Math.Sqrt(3.0);
                double Iphase_k = Ik;
                double Pphase_k = Pk / 3.0;

                res.Zk = Vphase_k / Iphase_k;                    // 實測頻率下堵轉總阻抗
                res.Rk = Pphase_k / (Iphase_k * Iphase_k);       // 堵轉總電阻
                double xk_sqr = (res.Zk * res.Zk) - (res.Rk * res.Rk);
                double Xk_meas = xk_sqr > 0 ? Math.Sqrt(xk_sqr) : 0.1; // 實測頻率下堵轉漏抗

                // ── 8 頻率試驗漏抗折算 (IEEE Std 112 / IEC 60034-2-1) ──
                int freqMode = (cmbEquivLockedFreq != null) ? cmbEquivLockedFreq.SelectedIndex : 0;
                double fRatio = 1.0;
                switch (freqMode)
                {
                    case 0: fRatio = 1.0; break;   // (1) 額定頻率 [1.0x]
                    case 1: fRatio = 0.25; break;  // (2) 25% 額定頻率 [0.25x]
                    case 2: fRatio = 0.30; break;  // (3) 30% 額定頻率 [0.30x]
                    case 3: fRatio = 0.40; break;  // (4) 40% 額定頻率 [0.40x]
                    case 4: fRatio = 0.50; break;  // (5) 50% 額定頻率 [0.50x]
                    case 5: fRatio = 0.60; break;  // (6) 60% 額定頻率 [0.60x]
                    case 6: fRatio = 2.0; break;   // (7) 2倍 額定頻率 [2.0x]
                    case 7: fRatio = 4.0; break;   // (8) 4倍 額定頻率 [4.0x]
                    default: fRatio = 1.0; break;
                }

                double baseFreq = (f0 > 1.0) ? f0 : 50.0;
                double fTest = baseFreq * fRatio;      // 實際測試頻率 (如 25 Hz 或 12.5 Hz)

                // 折算回額定頻率 f0 下之堵轉總漏抗: Xk(f0) = Xk_meas * (f0 / fTest) = Xk_meas / fRatio
                res.Xk = Xk_meas / fRatio;

                // 換算堵轉漏電感 (mH): Lk = Xk_meas / (2 * pi * fTest) * 1000 (電感為物理固有量，不受折算影響)
                double omega_test = 2.0 * Math.PI * fTest;
                res.Lk_mH = (Xk_meas / omega_test) * 1000.0;

                // 定子與轉子電阻/漏抗分配 (標準 IEEE Std 112 推薦：50% / 50%)
                if (chkAutoR1Distribute.Checked)
                {
                    res.R1 = res.Rk * 0.5;
                    numEquivStatorR1.Value = (decimal)Math.Max(0.0001, Math.Round(res.R1, 4));
                }
                else
                {
                    res.R1 = (double)numEquivStatorR1.Value;
                }
                res.R2_prime = Math.Max(0.001, res.Rk - res.R1); // 轉子折算電阻
                res.X1 = res.Xk * 0.5;                           // 定子漏抗
                res.X2_prime = res.Xk * 0.5;                     // 轉子折算漏抗
                res.L1_mH = res.Lk_mH * 0.5;
                res.L2_prime_mH = res.Lk_mH * 0.5;

                // 2. 空載實驗參數 (No-Load Test)
                double Vphase_0 = V0 / Math.Sqrt(3.0);
                double Iphase_0 = I0;
                double Pphase_0 = P0 / 3.0;

                res.Z0 = Vphase_0 / Iphase_0;                    // 空載總阻抗
                double R0 = Pphase_0 / (Iphase_0 * Iphase_0);     // 空載等效電阻
                double x0_sqr = (res.Z0 * res.Z0) - (R0 * R0);
                double X0 = x0_sqr > 0 ? Math.Sqrt(x0_sqr) : res.Z0;

                res.Xm = Math.Max(0.1, X0 - res.X1);             // 激磁電抗

                // 鐵損電阻 Rc (並聯模型: Rc = Vphase_0^2 / P_core)
                double p_copper_stator_0 = 3.0 * (Iphase_0 * Iphase_0) * res.R1;
                double p_core = Math.Max(1.0, P0 - p_copper_stator_0);
                res.Rc = (3.0 * Vphase_0 * Vphase_0) / p_core;

                // 3. 額定特性與轉矩估算
                int poles = kebMotorPoles2 > 0 ? kebMotorPoles2 : 4;
                double syncSpd = 120.0 * f0 / poles;
                double omega_sync = 2.0 * Math.PI * syncSpd / 60.0;
                double Vphase_N = VN / Math.Sqrt(3.0);

                if (syncSpd > 0 && NN > 0)
                {
                    res.RatedSlip = ((syncSpd - NN) / syncSpd) * 100.0;
                }
                else
                {
                    res.RatedSlip = 2.5;
                }

                // 啟動特性 (s = 1.0): Z_start = (R1 + R2') + j(X1 + X2')
                double r_start = res.R1 + res.R2_prime;
                double x_start = res.X1 + res.X2_prime;
                double z_start = Math.Sqrt(r_start * r_start + x_start * x_start);
                res.I_start = Vphase_N / Math.Max(0.01, z_start);

                // 啟動轉矩 T_start = (3 / omega_sync) * I_start^2 * R2'
                res.T_start = (omega_sync > 0) ? ((3.0 / omega_sync) * (res.I_start * res.I_start) * res.R2_prime) : 0.0;

                // 最大崩潰轉矩 T_max
                double r1_sqr = res.R1 * res.R1;
                double x_leak_sqr = (res.X1 + res.X2_prime) * (res.X1 + res.X2_prime);
                double denom_tmax = 2.0 * omega_sync * (res.R1 + Math.Sqrt(r1_sqr + x_leak_sqr));
                res.T_max = (denom_tmax > 0) ? ((3.0 * Vphase_N * Vphase_N) / denom_tmax) : 0.0;

                if (TN > 0)
                {
                    res.T_start_ratio = res.T_start / TN;
                    res.T_max_ratio = res.T_max / TN;
                }

                // 額定效率推估
                double p_out_mech = TN * (2.0 * Math.PI * NN / 60.0);
                double p_elec_in = (double)numEquivPn.Value * 1000.0;
                if (p_elec_in > 0 && p_out_mech > 0)
                {
                    res.EstEff = (p_out_mech / p_elec_in) * 100.0;
                }
                else
                {
                    res.EstEff = 88.5;
                }

                // 換算電感 (mH): L = X / (2 * pi * f) * 1000
                double calcFreq = f0 > 1.0 ? f0 : 50.0;
                double omega_0 = 2.0 * Math.PI * calcFreq;
                if (res.Lk_mH <= 0.001) res.Lk_mH = (res.Xk / omega_0) * 1000.0;
                res.L1_mH = res.Lk_mH * 0.5;
                res.L2_prime_mH = res.Lk_mH * 0.5;
                res.Lm_mH = (res.Xm / omega_0) * 1000.0;

                lastEquivResult = res;

                // 更新結果 DataGridView
                UpdateEquivResultsGrid(res);

                // 重繪等效電路圖解面板
                if (pnlEquivDiagram != null) pnlEquivDiagram.Invalidate();

                SaveLayoutConfig();
                WriteHmiLog("EQUIV_CALC", string.Format("【等效電路計算成功】R1={0:F4}Ω, X1={1:F4}Ω({6:F2}mH), Xm={2:F2}Ω({7:F1}mH), R2'={3:F4}Ω, X2'={4:F4}Ω({8:F2}mH), Tmax={5:F1}Nm",
                    res.R1, res.X1, res.Xm, res.R2_prime, res.X2_prime, res.T_max, res.L1_mH, res.Lm_mH, res.L2_prime_mH));

                MessageBox.Show(string.Format("[成功] 三相感應馬達單相等效電路參數計算成功！\r\n\r\n• 定子電阻 R1 = {0:F4} Ω\r\n• 定子漏抗 X1 = {1:F4} Ω (L1 = {7:F3} mH)\r\n• 轉子折算電阻 R2' = {2:F4} Ω\r\n• 轉子折算漏抗 X2' = {3:F4} Ω (L2' = {8:F3} mH)\r\n• 激磁電抗 Xm = {4:F2} Ω (Lm = {9:F2} mH)\r\n• 最大崩潰轉矩 Tmax = {5:F1} Nm ({6:F2} 倍額定)",
                    res.R1, res.X1, res.R2_prime, res.X2_prime, res.Xm, res.T_max, res.T_max_ratio, res.L1_mH, res.L2_prime_mH, res.Lm_mH),
                    "計算完成", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show("計算等效電路參數失敗: " + ex.Message, "錯誤", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void UpdateEquivResultsGrid(EquivCircuitResult r)
        {
            if (dgvEquivResults == null || r == null) return;
            dgvEquivResults.Rows.Clear();
            dgvEquivResults.Rows.Add("定子相電阻", "R1", r.R1.ToString("F4"), "Ω", "--", "定子繞組有效相電阻");
            dgvEquivResults.Rows.Add("定子漏電抗", "X1", r.X1.ToString("F4"), "Ω", r.L1_mH.ToString("F3"), "定子漏磁通等效相電抗/漏電感");
            dgvEquivResults.Rows.Add("激磁電抗", "Xm", r.Xm.ToString("F2"), "Ω", r.Lm_mH.ToString("F1"), "氣隙主磁通等效激磁抗/激磁電感");
            dgvEquivResults.Rows.Add("鐵損電阻", "Rc", r.Rc.ToString("F1"), "Ω", "--", "主磁通渦流與磁滯鐵耗");
            dgvEquivResults.Rows.Add("轉子折算電阻", "R2'", r.R2_prime.ToString("F4"), "Ω", "--", "轉子繞組折算至定子端電阻");
            dgvEquivResults.Rows.Add("轉子折算漏抗", "X2'", r.X2_prime.ToString("F4"), "Ω", r.L2_prime_mH.ToString("F3"), "轉子漏磁通折算等效電抗/漏電感");
            dgvEquivResults.Rows.Add("堵轉阻抗", "Zk", r.Zk.ToString("F4"), "Ω", "--", "轉子鎖死時之短路等效總阻抗");
            dgvEquivResults.Rows.Add("堵轉總漏抗", "Xk", r.Xk.ToString("F4"), "Ω", r.Lk_mH.ToString("F3"), "堵轉短路等效總漏抗/漏電感");
            dgvEquivResults.Rows.Add("額定運轉轉差率", "sN", r.RatedSlip.ToString("F2"), "%", "--", "實測額定負載不補轉差率");
            dgvEquivResults.Rows.Add("推估啟動轉矩", "Tst", r.T_start.ToString("F1") + " (" + r.T_start_ratio.ToString("F2") + "x)", "Nm", "--", "全壓啟動初始瞬態轉矩估算");
            dgvEquivResults.Rows.Add("推估最大崩潰轉矩", "Tmax", r.T_max.ToString("F1") + " (" + r.T_max_ratio.ToString("F2") + "x)", "Nm", "--", "等效電路推估之極限轉矩");
            dgvEquivResults.Rows.Add("額定預測效率", "η", r.EstEff.ToString("F2"), "%", "--", "由等效電路損耗推估之額定效率");
        }

        #endregion

        #region GDI+ 專業向量等效電路圖解繪製

        private void DrawEquivalentCircuitDiagram(object sender, PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

            int w = pnlEquivDiagram.ClientSize.Width;
            int h = pnlEquivDiagram.ClientSize.Height;

            // 背景填色
            using (SolidBrush bgBrush = new SolidBrush(Color.FromArgb(248, 250, 252)))
            {
                g.FillRectangle(bgBrush, 0, 0, w, h);
            }

            // 標題與圖例
            using (Font fTitle = new Font("微軟正黑體", 10f, FontStyle.Bold))
            using (SolidBrush brText = new SolidBrush(Color.FromArgb(30, 41, 59)))
            {
                g.DrawString("[寫入] 三相感應電機單相 T 型等效電路圖解 (Per-Phase T-Equivalent Circuit)", fTitle, brText, 14, 12);
            }

            // 主迴路座標設定
            int yTop = 85;
            int yBot = h - 60;
            int xStart = 45;
            int xEnd = w - 45;
            if (xEnd <= xStart + 200) return;

            int xStatorR = xStart + (int)((xEnd - xStart) * 0.16);
            int xStatorX = xStart + (int)((xEnd - xStart) * 0.34);
            int xMagBranch = xStart + (int)((xEnd - xStart) * 0.50);
            int xRotorX = xStart + (int)((xEnd - xStart) * 0.68);
            int xRotorR = xStart + (int)((xEnd - xStart) * 0.86);

            using (Pen wirePen = new Pen(Color.FromArgb(71, 85, 105), 2.0f))
            using (Pen compPen = new Pen(Color.FromArgb(3, 105, 161), 2.0f))
            using (SolidBrush textBrush = new SolidBrush(Color.FromArgb(15, 23, 42)))
            using (SolidBrush valBrush = new SolidBrush(Color.FromArgb(2, 132, 199)))
            using (Font fLbl = new Font("微軟正黑體", 9f, FontStyle.Bold))
            using (Font fVal = new Font("Consolas", 9.5f, FontStyle.Bold))
            {
                // 1. 頂部線路與底層共同回流線
                g.DrawLine(wirePen, xStart, yTop, xEnd, yTop);
                g.DrawLine(wirePen, xStart, yBot, xEnd, yBot);

                // 電源輸入端標示 (V1,phase)
                g.FillEllipse(Brushes.White, xStart - 5, yTop - 5, 10, 10);
                g.DrawEllipse(wirePen, xStart - 5, yTop - 5, 10, 10);
                g.FillEllipse(Brushes.White, xStart - 5, yBot - 5, 10, 10);
                g.DrawEllipse(wirePen, xStart - 5, yBot - 5, 10, 10);
                g.DrawString("+ V1", fLbl, textBrush, xStart - 35, yTop - 8);
                g.DrawString("-", fLbl, textBrush, xStart - 25, yBot - 8);

                // 2. 定子元件: R1 (電阻符號)
                DrawResistorSymbol(g, compPen, xStatorR, yTop, true);
                g.DrawString("R1 (定子電阻)", fLbl, textBrush, xStatorR - 35, yTop - 42);
                string r1Str = lastEquivResult != null ? (lastEquivResult.R1.ToString("F4") + " Ω") : "--";
                g.DrawString(r1Str, fVal, valBrush, xStatorR - 25, yTop - 25);

                // 3. 定子元件: X1 (電感符號)
                DrawInductorSymbol(g, compPen, xStatorX, yTop, true);
                g.DrawString("X1 (定子漏抗)", fLbl, textBrush, xStatorX - 35, yTop - 42);
                string x1Str = lastEquivResult != null ? (lastEquivResult.X1.ToString("F4") + " Ω\r\n(" + lastEquivResult.L1_mH.ToString("F2") + "mH)") : "--";
                g.DrawString(x1Str, fVal, valBrush, xStatorX - 25, yTop - 25);

                // 4. 中間激磁分支 (並聯 Rc || Xm)
                g.DrawLine(wirePen, xMagBranch, yTop, xMagBranch, yTop + 25);
                g.DrawLine(wirePen, xMagBranch - 28, yTop + 25, xMagBranch + 28, yTop + 25);

                // 左支路: Rc
                int yMid = (yTop + yBot) / 2;
                g.DrawLine(wirePen, xMagBranch - 28, yTop + 25, xMagBranch - 28, yMid - 22);
                DrawResistorSymbol(g, compPen, xMagBranch - 28, yMid, false);
                g.DrawLine(wirePen, xMagBranch - 28, yMid + 22, xMagBranch - 28, yBot - 25);

                g.DrawString("Rc", fLbl, textBrush, xMagBranch - 65, yMid - 10);
                string rcStr = lastEquivResult != null ? (lastEquivResult.Rc > 0 ? (lastEquivResult.Rc.ToString("F0") + "Ω") : "--") : "--";
                g.DrawString(rcStr, fVal, valBrush, xMagBranch - 72, yMid + 6);

                // 右支路: Xm
                g.DrawLine(wirePen, xMagBranch + 28, yTop + 25, xMagBranch + 28, yMid - 22);
                DrawInductorSymbol(g, compPen, xMagBranch + 28, yMid, false);
                g.DrawLine(wirePen, xMagBranch + 28, yMid + 22, xMagBranch + 28, yBot - 25);

                g.DrawString("Xm", fLbl, textBrush, xMagBranch + 34, yMid - 10);
                string xmStr = lastEquivResult != null ? (lastEquivResult.Xm.ToString("F2") + "Ω\r\n(" + lastEquivResult.Lm_mH.ToString("F1") + "mH)") : "--";
                g.DrawString(xmStr, fVal, valBrush, xMagBranch + 34, yMid + 6);

                g.DrawLine(wirePen, xMagBranch - 28, yBot - 25, xMagBranch + 28, yBot - 25);
                g.DrawLine(wirePen, xMagBranch, yBot - 25, xMagBranch, yBot);

                // 5. 轉子元件: X2' (轉子折算漏抗)
                DrawInductorSymbol(g, compPen, xRotorX, yTop, true);
                g.DrawString("X2' (轉子漏抗)", fLbl, textBrush, xRotorX - 35, yTop - 42);
                string x2Str = lastEquivResult != null ? (lastEquivResult.X2_prime.ToString("F4") + " Ω\r\n(" + lastEquivResult.L2_prime_mH.ToString("F2") + "mH)") : "--";
                g.DrawString(x2Str, fVal, valBrush, xRotorX - 25, yTop - 25);

                // 6. 轉子負載: R2'/s (可變轉差負載電阻)
                DrawResistorSymbol(g, compPen, xRotorR, yTop, true);
                // 斜向箭頭表示隨轉差 s 可變
                using (Pen arrowPen = new Pen(Color.FromArgb(239, 68, 68), 1.8f))
                {
                    g.DrawLine(arrowPen, xRotorR - 16, yTop + 14, xRotorR + 16, yTop - 14);
                    g.DrawLine(arrowPen, xRotorR + 16, yTop - 14, xRotorR + 11, yTop - 14);
                    g.DrawLine(arrowPen, xRotorR + 16, yTop - 14, xRotorR + 16, yTop - 9);
                }
                g.DrawString("R2'/s (負載電阻)", fLbl, textBrush, xRotorR - 40, yTop - 42);
                string r2Str = lastEquivResult != null ? (lastEquivResult.R2_prime.ToString("F4") + " Ω") : "--";
                g.DrawString(r2Str, fVal, valBrush, xRotorR - 25, yTop - 25);

                // 右端閉合迴路
                g.DrawLine(wirePen, xEnd, yTop, xEnd, yBot);

                // 底部文字提示
                string motorHint = string.Format("★ 待測機種: {0} | 額定轉差: {1:F2}% | 激磁: {2:F2} Ω ({3:F1} mH) | 漏抗: X1={4:F3}mH, X2'={5:F3}mH",
                    motorModelName,
                    lastEquivResult != null ? lastEquivResult.RatedSlip : (double)numEquivSlip.Value,
                    lastEquivResult != null ? lastEquivResult.Xm : 0.0,
                    lastEquivResult != null ? lastEquivResult.Lm_mH : 0.0,
                    lastEquivResult != null ? lastEquivResult.L1_mH : 0.0,
                    lastEquivResult != null ? lastEquivResult.L2_prime_mH : 0.0);
                g.DrawString(motorHint, fLbl, Brushes.DimGray, 16, h - 28);
            }
        }

        private void DrawResistorSymbol(Graphics g, Pen pen, int cx, int cy, bool horizontal)
        {
            using (SolidBrush mask = new SolidBrush(Color.FromArgb(248, 250, 252)))
            {
                if (horizontal)
                {
                    g.FillRectangle(mask, cx - 18, cy - 8, 36, 16);
                    g.DrawRectangle(pen, cx - 18, cy - 8, 36, 16);
                }
                else
                {
                    g.FillRectangle(mask, cx - 8, cy - 18, 16, 36);
                    g.DrawRectangle(pen, cx - 8, cy - 18, 16, 36);
                }
            }
        }

        private void DrawInductorSymbol(Graphics g, Pen pen, int cx, int cy, bool horizontal)
        {
            using (SolidBrush mask = new SolidBrush(Color.FromArgb(248, 250, 252)))
            {
                if (horizontal)
                {
                    g.FillRectangle(mask, cx - 18, cy - 10, 36, 20);
                    // 繪製三個半圓弧表示電感
                    g.DrawArc(pen, cx - 18, cy - 8, 12, 16, 180, 180);
                    g.DrawArc(pen, cx - 6, cy - 8, 12, 16, 180, 180);
                    g.DrawArc(pen, cx + 6, cy - 8, 12, 16, 180, 180);
                }
                else
                {
                    g.FillRectangle(mask, cx - 10, cy - 18, 20, 36);
                    g.DrawArc(pen, cx - 8, cy - 18, 16, 12, 90, 180);
                    g.DrawArc(pen, cx - 8, cy - 6, 16, 12, 90, 180);
                    g.DrawArc(pen, cx - 8, cy + 6, 16, 12, 90, 180);
                }
            }
        }

        #endregion

        #region 馬達一致性記憶判定與 B載台 dr 異動偵測

        // 刷新馬達狀態列顯示
        public void RefreshEquivMotorStatus()
        {
            try
            {
                string curName = !string.IsNullOrEmpty(motorModelName) ? motorModelName : "SVM100S";
                string bDrSummary = FormatDrSummary(lastB_Dr00, lastB_Dr01, lastB_Dr02, lastB_Dr03, lastB_Dr04, lastB_Dr05);

                bool isSame = true;
                if (!string.IsNullOrEmpty(cachedEquivMotorName) && cachedEquivMotorName != curName) isSame = false;
                if (!string.IsNullOrEmpty(cachedEquivDrFingerprint) && !string.IsNullOrEmpty(lastKnownB_DrFingerprint) && cachedEquivDrFingerprint != lastKnownB_DrFingerprint) isSame = false;

                if (lblEquivMotorStatus != null)
                {
                    if (isSame)
                    {
                        if (isNoLoadDataReady && isRatedDataReady)
                        {
                            lblEquivMotorStatus.Text = string.Format("[連線] 待測馬達: 【{0}】 | B載台 dr: [{1}] | 一致性: [O] 同一馬達記憶中 (1,2項數據已保留)", curName, bDrSummary);
                            lblEquivMotorStatus.ForeColor = Color.FromArgb(15, 23, 42);
                        }
                        else if (!isNoLoadDataReady && !isRatedDataReady && !isLockedDataReady)
                        {
                            lblEquivMotorStatus.Text = string.Format("[連線] 待測馬達: 【{0}】 | B載台 dr: [{1}] | 一致性: [--] 數據已清空，待採樣 (1.空載 / 2.額定 / 3.堵轉)", curName, bDrSummary);
                            lblEquivMotorStatus.ForeColor = Color.FromArgb(100, 116, 139);
                        }
                        else
                        {
                            lblEquivMotorStatus.Text = string.Format("[連線] 待測馬達: 【{0}】 | B載台 dr: [{1}] | 一致性: [部分採樣] (空載:{2}, 額定:{3}, 堵轉:{4})",
                                curName, bDrSummary, (isNoLoadDataReady ? "已完成" : "待採樣"), (isRatedDataReady ? "已完成" : "待採樣"), (isLockedDataReady ? "已完成" : "待採樣"));
                            lblEquivMotorStatus.ForeColor = Color.FromArgb(30, 64, 175);
                        }
                    }
                    else
                    {
                        lblEquivMotorStatus.Text = string.Format("[!] 偵測到馬達變更: 【{0}】 (前次: {1}) | B載台 dr: [{2}] | 建議點擊右方清除數據", curName, cachedEquivMotorName, bDrSummary);
                        lblEquivMotorStatus.ForeColor = Color.FromArgb(180, 83, 9);
                    }
                }

                // 首次若無快取，進行記錄
                if (string.IsNullOrEmpty(cachedEquivMotorName)) cachedEquivMotorName = curName;
                if (string.IsNullOrEmpty(cachedEquivDrFingerprint) && !string.IsNullOrEmpty(lastKnownB_DrFingerprint)) cachedEquivDrFingerprint = lastKnownB_DrFingerprint;
            }
            catch { }
        }

        public void UpdateEquivMotorFingerprintUI()
        {
            RefreshEquivMotorStatus();
        }

        // 清除採樣重設
        private void ResetEquivDataCards()
        {
            // 1. 空載運轉數據 (No-Load) 全數歸零清空
            if (numEquivV0 != null) numEquivV0.Value = 0m;
            if (numEquivI0 != null) numEquivI0.Value = 0m;
            if (numEquivP0 != null) numEquivP0.Value = 0m;
            if (numEquivPf0 != null) numEquivPf0.Value = 0m;
            if (numEquivN0 != null) numEquivN0.Value = 0m;
            if (numEquivF0 != null) numEquivF0.Value = 0m;

            // 2. 額定運轉數據 (Rated Load) 全數歸零清空
            if (numEquivTn != null) numEquivTn.Value = 0m;
            if (numEquivNn != null) numEquivNn.Value = 0m;
            if (numEquivVn != null) numEquivVn.Value = 0m;
            if (numEquivIn != null) numEquivIn.Value = 0m;
            if (numEquivPn != null) numEquivPn.Value = 0m;
            if (numEquivPfn != null) numEquivPfn.Value = 0m;
            if (numEquivSlip != null) numEquivSlip.Value = 0m;

            // 3. 堵轉測試數據 (Locked-Rotor) 全數歸零清空
            if (numEquivVk != null) numEquivVk.Value = 0m;
            if (numEquivIk != null) numEquivIk.Value = 0m;
            if (numEquivPk != null) numEquivPk.Value = 0m;
            if (numEquivPfk != null) numEquivPfk.Value = 0m;

            isNoLoadDataReady = false;
            isRatedDataReady = false;
            isLockedDataReady = false;

            if (lblNoLoadItemStatus != null)
            {
                lblNoLoadItemStatus.Text = "[--] 待採樣 (可從空載測試載入或即時抓取)";
                lblNoLoadItemStatus.ForeColor = Color.FromArgb(100, 116, 139);
            }

            if (lblRatedItemStatus != null)
            {
                lblRatedItemStatus.Text = "[--] 待採樣 (可從 T-N 額定點載入或即時抓取)";
                lblRatedItemStatus.ForeColor = Color.FromArgb(100, 116, 139);
            }

            if (lblLockedItemStatus != null)
            {
                lblLockedItemStatus.Text = "[--] 待採樣 (請先降低 uf09 並鎖定轉子)";
                lblLockedItemStatus.ForeColor = Color.FromArgb(100, 116, 139);
            }

            cachedEquivMotorName = !string.IsNullOrEmpty(motorModelName) ? motorModelName : "SVM100S";
            cachedEquivDrFingerprint = lastKnownB_DrFingerprint;

            lastEquivResult = null;
            InitDefaultEquivResultsGrid();
            if (pnlEquivDiagram != null) pnlEquivDiagram.Invalidate();

            RefreshEquivMotorStatus();
            WriteHmiLog("EQUIV", "【等效電路】已清空所有 1.空載、2.額定、3.堵轉 採樣數據，數值已全數歸零，準備全新馬達測試。");
        }

        #endregion

        #region 結果複製與匯出 CSV

        private void CopyEquivResultsToClipboard()
        {
            try
            {
                StringBuilder sb = new StringBuilder();
                sb.AppendLine("=== 馬達單相等效電路計算結果 (IEEE Std 112) ===");
                sb.AppendLine("馬達名稱: " + motorModelName);
                sb.AppendLine("計算時間: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                sb.AppendLine("-------------------------------------------------");
                if (lastEquivResult != null)
                {
                    sb.AppendLine(string.Format("定子相電阻 R1: {0:F4} Ω", lastEquivResult.R1));
                    sb.AppendLine(string.Format("定子漏電抗 X1: {0:F4} Ω ({1:F3} mH)", lastEquivResult.X1, lastEquivResult.L1_mH));
                    sb.AppendLine(string.Format("激磁電抗 Xm: {0:F2} Ω ({1:F2} mH)", lastEquivResult.Xm, lastEquivResult.Lm_mH));
                    sb.AppendLine(string.Format("鐵損電阻 Rc: {0:F1} Ω", lastEquivResult.Rc));
                    sb.AppendLine(string.Format("轉子折算電阻 R2': {0:F4} Ω", lastEquivResult.R2_prime));
                    sb.AppendLine(string.Format("轉子折算漏抗 X2': {0:F4} Ω ({1:F3} mH)", lastEquivResult.X2_prime, lastEquivResult.L2_prime_mH));
                    sb.AppendLine(string.Format("堵轉總漏抗 Xk: {0:F4} Ω ({1:F3} mH)", lastEquivResult.Xk, lastEquivResult.Lk_mH));
                    sb.AppendLine(string.Format("額定運轉轉差率 sN: {0:F2} %", lastEquivResult.RatedSlip));
                    sb.AppendLine(string.Format("推估啟動轉矩 Tst: {0:F1} Nm ({1:F2}x TN)", lastEquivResult.T_start, lastEquivResult.T_start_ratio));
                    sb.AppendLine(string.Format("推估最大轉矩 Tmax: {0:F1} Nm ({1:F2}x TN)", lastEquivResult.T_max, lastEquivResult.T_max_ratio));
                }
                Clipboard.SetText(sb.ToString());
                MessageBox.Show("等效電路參數已成功複製至系統剪貼簿！", "複製成功", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show("複製失敗: " + ex.Message, "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void ExportEquivCircuitCsv()
        {
            try
            {
                using (SaveFileDialog sfd = new SaveFileDialog())
                {
                    sfd.Filter = "CSV 檔案 (*.csv)|*.csv";
                    sfd.FileName = string.Format("EquivCircuit_{0}_{1}.csv", motorModelName.Replace(" ", "_"), DateTime.Now.ToString("yyyyMMdd_HHmmss"));
                    sfd.InitialDirectory = GetMotorDedicatedLogDirectory(motorModelName);
                    if (sfd.ShowDialog() == DialogResult.OK)
                    {
                        StringBuilder sb = new StringBuilder();
                        sb.AppendLine("項目,符號,數值,單位,換算電感(mH),說明");
                        if (dgvEquivResults != null)
                        {
                            foreach (DataGridViewRow r in dgvEquivResults.Rows)
                            {
                                sb.AppendLine(string.Format("\"{0}\",\"{1}\",\"{2}\",\"{3}\",\"{4}\",\"{5}\"",
                                    r.Cells["Param"].Value, r.Cells["Symbol"].Value, r.Cells["Value"].Value, r.Cells["Unit"].Value, r.Cells["Inductance"].Value, r.Cells["Desc"].Value));
                            }
                        }
                        File.WriteAllText(sfd.FileName, sb.ToString(), Encoding.UTF8);
                        MessageBox.Show("等效電路報告已成功匯出至：\r\n" + sfd.FileName, "匯出成功", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("匯出失敗: " + ex.Message, "錯誤", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        #endregion

        #region INI 設定檔記憶讀寫擴充

        public void SaveEquivCircuitConfig(StringBuilder sb)
        {
            try
            {
                sb.AppendLine("[EquivCircuit]");
                sb.AppendLine("CachedFingerprint=" + (cachedEquivDrFingerprint ?? ""));
                sb.AppendLine("CachedMotorName=" + (cachedEquivMotorName ?? ""));
                sb.AppendLine("NoLoadReady=" + (isNoLoadDataReady ? "1" : "0"));
                sb.AppendLine("V0=" + (numEquivV0 != null ? numEquivV0.Value.ToString(System.Globalization.CultureInfo.InvariantCulture) : "0"));
                sb.AppendLine("I0=" + (numEquivI0 != null ? numEquivI0.Value.ToString(System.Globalization.CultureInfo.InvariantCulture) : "0"));
                sb.AppendLine("P0=" + (numEquivP0 != null ? numEquivP0.Value.ToString(System.Globalization.CultureInfo.InvariantCulture) : "0"));
                sb.AppendLine("Pf0=" + (numEquivPf0 != null ? numEquivPf0.Value.ToString(System.Globalization.CultureInfo.InvariantCulture) : "0"));
                sb.AppendLine("N0=" + (numEquivN0 != null ? numEquivN0.Value.ToString(System.Globalization.CultureInfo.InvariantCulture) : "0"));
                sb.AppendLine("F0=" + (numEquivF0 != null ? numEquivF0.Value.ToString(System.Globalization.CultureInfo.InvariantCulture) : "0"));

                sb.AppendLine("RatedReady=" + (isRatedDataReady ? "1" : "0"));
                sb.AppendLine("Tn=" + (numEquivTn != null ? numEquivTn.Value.ToString(System.Globalization.CultureInfo.InvariantCulture) : "0"));
                sb.AppendLine("Nn=" + (numEquivNn != null ? numEquivNn.Value.ToString(System.Globalization.CultureInfo.InvariantCulture) : "0"));
                sb.AppendLine("Vn=" + (numEquivVn != null ? numEquivVn.Value.ToString(System.Globalization.CultureInfo.InvariantCulture) : "0"));
                sb.AppendLine("In=" + (numEquivIn != null ? numEquivIn.Value.ToString(System.Globalization.CultureInfo.InvariantCulture) : "0"));
                sb.AppendLine("Pn=" + (numEquivPn != null ? numEquivPn.Value.ToString(System.Globalization.CultureInfo.InvariantCulture) : "0"));
                sb.AppendLine("Pfn=" + (numEquivPfn != null ? numEquivPfn.Value.ToString(System.Globalization.CultureInfo.InvariantCulture) : "0"));
                sb.AppendLine("Slip=" + (numEquivSlip != null ? numEquivSlip.Value.ToString(System.Globalization.CultureInfo.InvariantCulture) : "0"));

                sb.AppendLine("LockedReady=" + (isLockedDataReady ? "1" : "0"));
                sb.AppendLine("Vk=" + (numEquivVk != null ? numEquivVk.Value.ToString(System.Globalization.CultureInfo.InvariantCulture) : "0"));
                sb.AppendLine("Ik=" + (numEquivIk != null ? numEquivIk.Value.ToString(System.Globalization.CultureInfo.InvariantCulture) : "0"));
                sb.AppendLine("Pk=" + (numEquivPk != null ? numEquivPk.Value.ToString(System.Globalization.CultureInfo.InvariantCulture) : "0"));
                sb.AppendLine("Pfk=" + (numEquivPfk != null ? numEquivPfk.Value.ToString(System.Globalization.CultureInfo.InvariantCulture) : "0"));
                sb.AppendLine("TargetUf09=" + (numEquivTargetUf09 != null ? numEquivTargetUf09.Value.ToString(System.Globalization.CultureInfo.InvariantCulture) : "50"));
                sb.AppendLine("AutoR1=" + (chkAutoR1Distribute != null && chkAutoR1Distribute.Checked ? "1" : "0"));
                sb.AppendLine("StatorR1=" + (numEquivStatorR1 != null ? numEquivStatorR1.Value.ToString(System.Globalization.CultureInfo.InvariantCulture) : "0.125"));
            }
            catch { }
        }

        public void LoadEquivCircuitConfig(Dictionary<string, string> map)
        {
            try
            {
                if (map == null) return;
                if (map.ContainsKey("EquivCircuit.CachedFingerprint")) cachedEquivDrFingerprint = map["EquivCircuit.CachedFingerprint"];
                if (map.ContainsKey("EquivCircuit.CachedMotorName")) cachedEquivMotorName = map["EquivCircuit.CachedMotorName"];

                if (map.ContainsKey("EquivCircuit.NoLoadReady")) isNoLoadDataReady = (map["EquivCircuit.NoLoadReady"] == "1");
                if (map.ContainsKey("EquivCircuit.V0") && numEquivV0 != null) { decimal v0; if (decimal.TryParse(map["EquivCircuit.V0"], System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out v0)) numEquivV0.Value = v0; }
                if (map.ContainsKey("EquivCircuit.I0") && numEquivI0 != null) { decimal i0; if (decimal.TryParse(map["EquivCircuit.I0"], System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out i0)) numEquivI0.Value = i0; }
                if (map.ContainsKey("EquivCircuit.P0") && numEquivP0 != null) { decimal p0; if (decimal.TryParse(map["EquivCircuit.P0"], System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out p0)) numEquivP0.Value = p0; }
                if (map.ContainsKey("EquivCircuit.Pf0") && numEquivPf0 != null) { decimal pf0; if (decimal.TryParse(map["EquivCircuit.Pf0"], System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out pf0)) numEquivPf0.Value = pf0; }
                if (map.ContainsKey("EquivCircuit.N0") && numEquivN0 != null) { decimal n0; if (decimal.TryParse(map["EquivCircuit.N0"], System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out n0)) numEquivN0.Value = n0; }
                if (map.ContainsKey("EquivCircuit.F0") && numEquivF0 != null) { decimal f0; if (decimal.TryParse(map["EquivCircuit.F0"], System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out f0)) numEquivF0.Value = f0; }

                if (map.ContainsKey("EquivCircuit.RatedReady")) isRatedDataReady = (map["EquivCircuit.RatedReady"] == "1");
                if (map.ContainsKey("EquivCircuit.Tn") && numEquivTn != null) { decimal tn; if (decimal.TryParse(map["EquivCircuit.Tn"], System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out tn)) numEquivTn.Value = tn; }
                if (map.ContainsKey("EquivCircuit.Nn") && numEquivNn != null) { decimal nn; if (decimal.TryParse(map["EquivCircuit.Nn"], System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out nn)) numEquivNn.Value = nn; }
                if (map.ContainsKey("EquivCircuit.Vn") && numEquivVn != null) { decimal vn; if (decimal.TryParse(map["EquivCircuit.Vn"], System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out vn)) numEquivVn.Value = vn; }
                if (map.ContainsKey("EquivCircuit.In") && numEquivIn != null) { decimal inn; if (decimal.TryParse(map["EquivCircuit.In"], System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out inn)) numEquivIn.Value = inn; }
                if (map.ContainsKey("EquivCircuit.Pn") && numEquivPn != null) { decimal pn; if (decimal.TryParse(map["EquivCircuit.Pn"], System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out pn)) numEquivPn.Value = pn; }
                if (map.ContainsKey("EquivCircuit.Pfn") && numEquivPfn != null) { decimal pfn; if (decimal.TryParse(map["EquivCircuit.Pfn"], System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out pfn)) numEquivPfn.Value = pfn; }
                if (map.ContainsKey("EquivCircuit.Slip") && numEquivSlip != null) { decimal slip; if (decimal.TryParse(map["EquivCircuit.Slip"], System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out slip)) numEquivSlip.Value = slip; }

                if (map.ContainsKey("EquivCircuit.LockedReady")) isLockedDataReady = (map["EquivCircuit.LockedReady"] == "1");
                if (map.ContainsKey("EquivCircuit.Vk") && numEquivVk != null) { decimal vk; if (decimal.TryParse(map["EquivCircuit.Vk"], System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out vk)) numEquivVk.Value = vk; }
                if (map.ContainsKey("EquivCircuit.Ik") && numEquivIk != null) { decimal ik; if (decimal.TryParse(map["EquivCircuit.Ik"], System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out ik)) numEquivIk.Value = ik; }
                if (map.ContainsKey("EquivCircuit.Pk") && numEquivPk != null) { decimal pk; if (decimal.TryParse(map["EquivCircuit.Pk"], System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out pk)) numEquivPk.Value = pk; }
                if (map.ContainsKey("EquivCircuit.Pfk") && numEquivPfk != null) { decimal pfk; if (decimal.TryParse(map["EquivCircuit.Pfk"], System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out pfk)) numEquivPfk.Value = pfk; }
                if (map.ContainsKey("EquivCircuit.TargetUf09") && numEquivTargetUf09 != null) { decimal tuf09; if (decimal.TryParse(map["EquivCircuit.TargetUf09"], System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out tuf09)) numEquivTargetUf09.Value = tuf09; }
                if (map.ContainsKey("EquivCircuit.AutoR1") && chkAutoR1Distribute != null) chkAutoR1Distribute.Checked = (map["EquivCircuit.AutoR1"] == "1");
                if (map.ContainsKey("EquivCircuit.StatorR1") && numEquivStatorR1 != null) { decimal r1; if (decimal.TryParse(map["EquivCircuit.StatorR1"], System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out r1)) numEquivStatorR1.Value = r1; }

                // 更新狀態標籤
                if (lblNoLoadItemStatus != null)
                {
                    if (isNoLoadDataReady)
                    {
                        lblNoLoadItemStatus.Text = "[O] 已載入記憶空載數據";
                        lblNoLoadItemStatus.ForeColor = Color.FromArgb(16, 185, 129);
                    }
                    else
                    {
                        lblNoLoadItemStatus.Text = "[--] 待採樣 (可從空載測試載入或即時抓取)";
                        lblNoLoadItemStatus.ForeColor = Color.FromArgb(100, 116, 139);
                    }
                }
                if (lblRatedItemStatus != null)
                {
                    if (isRatedDataReady)
                    {
                        lblRatedItemStatus.Text = "[O] 已載入記憶額定數據";
                        lblRatedItemStatus.ForeColor = Color.FromArgb(16, 185, 129);
                    }
                    else
                    {
                        lblRatedItemStatus.Text = "[--] 待採樣 (可從 T-N 額定點載入或即時抓取)";
                        lblRatedItemStatus.ForeColor = Color.FromArgb(100, 116, 139);
                    }
                }
                if (lblLockedItemStatus != null)
                {
                    if (isLockedDataReady)
                    {
                        lblLockedItemStatus.Text = "[O] 已載入記憶堵轉數據";
                        lblLockedItemStatus.ForeColor = Color.FromArgb(16, 185, 129);
                    }
                    else
                    {
                        lblLockedItemStatus.Text = "[--] 待採樣 (請先降低 uf09 並鎖定轉子)";
                        lblLockedItemStatus.ForeColor = Color.FromArgb(100, 116, 139);
                    }
                }
                RefreshEquivMotorStatus();
            }
            catch { }
        }

        #endregion
    }
}
