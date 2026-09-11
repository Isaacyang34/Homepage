using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace DynamometerHMI
{
    public partial class MainForm : Form
    {
        #region 等效電路控制項與狀態變數宣告

        public TabPage tabEquiv;

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
        private NumericUpDown numEquivPout;
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
        private int originalUf09Val = 260; // 記錄原始 uf09 數值以供安全復歸

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

            // 主容器：外層垂直 Dock Panel (啟用 AutoScroll 防止 DPI / 小螢幕裁切)
            Panel pnlMainScroll = new Panel()
            {
                Dock = DockStyle.Fill,
                AutoScroll = true,
                BackColor = Color.FromArgb(248, 250, 252),
                Padding = new Padding(8)
            };

            // 頂部狀態橫條：馬達指紋與同動記憶指示
            Panel pnlTopBar = new Panel()
            {
                Dock = DockStyle.Top,
                Height = 62,
                BackColor = Color.White,
                Padding = new Padding(10, 6, 10, 6),
                Margin = new Padding(0, 0, 0, 8)
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
            tlpTopBar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 72f));
            tlpTopBar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150f));
            tlpTopBar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 160f));

            lblEquivMotorStatus = new Label()
            {
                Text = "🔗 待測馬達: 【" + (!string.IsNullOrEmpty(motorModelName) ? motorModelName : "SVM100S") + "】 | B載台 dr 狀態: 讀取中... | 一致性: 🟢 同一馬達測試記憶中",
                Font = new Font("微軟正黑體", 10.5f, FontStyle.Bold),
                ForeColor = Color.FromArgb(15, 23, 42),
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft
            };

            btnEquivRefreshFingerprint = new Button()
            {
                Text = "🔄 刷新馬達狀態",
                Font = new Font("微軟正黑體", 9.5f, FontStyle.Bold),
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(241, 245, 249),
                ForeColor = Color.FromArgb(30, 41, 59),
                FlatStyle = FlatStyle.Flat,
                Margin = new Padding(4)
            };
            btnEquivRefreshFingerprint.FlatAppearance.BorderColor = Color.FromArgb(203, 213, 225);
            btnEquivRefreshFingerprint.Click += (s, e) => RefreshEquivMotorStatus();

            btnEquivClearAllData = new Button()
            {
                Text = "🗑️ 清除採樣重測",
                Font = new Font("微軟正黑體", 9.5f, FontStyle.Bold),
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(254, 242, 242),
                ForeColor = Color.FromArgb(220, 38, 38),
                FlatStyle = FlatStyle.Flat,
                Margin = new Padding(4)
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

            // 中間卡片網格：三大測試數據卡片 (橫向三等分 TableLayoutPanel)
            TableLayoutPanel tlpCards = new TableLayoutPanel()
            {
                Dock = DockStyle.Top,
                Height = 350,
                ColumnCount = 3,
                RowCount = 1,
                Margin = new Padding(0, 8, 0, 8)
            };
            tlpCards.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33f));
            tlpCards.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33f));
            tlpCards.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.34f));

            // 建立卡片 1、卡片 2、卡片 3
            Panel card1 = CreateNoLoadCard();
            Panel card2 = CreateRatedCard();
            Panel card3 = CreateLockedCard();

            tlpCards.Controls.Add(card1, 0, 0);
            tlpCards.Controls.Add(card2, 1, 0);
            tlpCards.Controls.Add(card3, 2, 0);

            // 底部成果分析區：計算控制列 + 表格與電路圖圖解 (兩欄分割)
            Panel pnlBottomSection = CreateResultsSection();

            // 依序由上至下置入主滾動容器
            pnlMainScroll.Controls.Add(pnlBottomSection);
            pnlMainScroll.Controls.Add(tlpCards);
            pnlMainScroll.Controls.Add(pnlTopBar);

            tab.Controls.Add(pnlMainScroll);
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
                Text = "⚪ 待採樣 (可從空載測試載入或即時抓取)",
                Font = new Font("微軟正黑體", 9f, FontStyle.Bold),
                ForeColor = Color.FromArgb(100, 116, 139),
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft
            };
            tlp.SetColumnSpan(lblNoLoadItemStatus, 2);
            tlp.Controls.Add(lblNoLoadItemStatus, 0, 1);

            // 參數列
            numEquivV0 = AddCardField(tlp, 2, "線電壓 V0 (V):", 260.0m, 1, 0, 1000);
            numEquivI0 = AddCardField(tlp, 3, "線電流 I0 (A):", 12.0m, 2, 0, 500);
            numEquivP0 = AddCardField(tlp, 4, "輸入功率 P0 (W):", 450.0m, 1, 0, 100000);
            numEquivPf0 = AddCardField(tlp, 5, "功率因數 PF0:", 0.08m, 3, 0, 1);
            numEquivN0 = AddCardField(tlp, 6, "實測轉速 N0 (rpm):", 1498m, 0, 0, 15000);
            numEquivF0 = AddCardField(tlp, 7, "測試頻率 f0 (Hz):", 50.0m, 2, 1, 500);

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
                Text = "📥 空載分頁載入",
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
                Text = "⚡ 擷取即時數據",
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
                RowCount = 9
            };
            tlp.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 48f));
            tlp.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 52f));

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
                Text = "⚪ 待採樣 (可從 T-N 額定點載入或即時抓取)",
                Font = new Font("微軟正黑體", 9f, FontStyle.Bold),
                ForeColor = Color.FromArgb(100, 116, 139),
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft
            };
            tlp.SetColumnSpan(lblRatedItemStatus, 2);
            tlp.Controls.Add(lblRatedItemStatus, 0, 1);

            numEquivTn = AddCardField(tlp, 2, "額定轉矩 TN (Nm):", 70.0m, 2, 0, 2000);
            numEquivNn = AddCardField(tlp, 3, "實測轉速 NN (rpm):", 1465m, 0, 0, 15000);
            numEquivVn = AddCardField(tlp, 4, "額定線壓 VN (V):", 260.0m, 1, 0, 1000);
            numEquivIn = AddCardField(tlp, 5, "額定線流 IN (A):", 32.3m, 2, 0, 500);
            numEquivPn = AddCardField(tlp, 6, "輸入電功率 (kW):", 12.8m, 3, 0, 500);
            numEquivPfn = AddCardField(tlp, 7, "功率因數 PFN:", 0.86m, 3, 0, 1);

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
                Text = "📥 T-N 額定點載入",
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
                Text = "⚡ 擷取即時數據",
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
            tlp.Controls.Add(tlpBtns, 0, 8);

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

            Label lblTitle = new Label()
            {
                Text = "3. 堵轉測試數據 (KEB uf09 降壓)",
                Font = new Font("微軟正黑體", 11f, FontStyle.Bold),
                ForeColor = Color.FromArgb(180, 83, 9),
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft
            };
            tlp.SetColumnSpan(lblTitle, 2);
            tlp.Controls.Add(lblTitle, 0, 0);

            lblLockedItemStatus = new Label()
            {
                Text = "⚪ 待採樣 (請先降低 uf09 並鎖定轉子)",
                Font = new Font("微軟正黑體", 9f, FontStyle.Bold),
                ForeColor = Color.FromArgb(100, 116, 139),
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft
            };
            tlp.SetColumnSpan(lblLockedItemStatus, 2);
            tlp.Controls.Add(lblLockedItemStatus, 0, 1);

            // KEB uf09 調控行 1: 選擇載台與讀取當前電壓
            TableLayoutPanel tlpUfCtrl1 = new TableLayoutPanel()
            {
                Dock = DockStyle.Fill,
                ColumnCount = 3,
                RowCount = 1
            };
            tlpUfCtrl1.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40f));
            tlpUfCtrl1.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 35f));
            tlpUfCtrl1.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25f));

            cmbEquivKebDrive = new ComboBox()
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Dock = DockStyle.Fill,
                Font = new Font("微軟正黑體", 9f)
            };
            cmbEquivKebDrive.Items.AddRange(new object[] { "B載台 (待測端)", "A載台" });
            cmbEquivKebDrive.SelectedIndex = 0;

            lblEquivCurUf09 = new Label()
            {
                Text = "uf09: -- V",
                Font = new Font("Consolas", 10f, FontStyle.Bold),
                ForeColor = Color.FromArgb(180, 83, 9),
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleCenter
            };

            btnEquivReadUf09 = new Button()
            {
                Text = "讀取",
                Font = new Font("微軟正黑體", 8.5f),
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(241, 245, 249)
            };
            btnEquivReadUf09.Click += (s, e) => ReadCurrentUf09FromHardware();

            tlpUfCtrl1.Controls.Add(cmbEquivKebDrive, 0, 0);
            tlpUfCtrl1.Controls.Add(lblEquivCurUf09, 1, 0);
            tlpUfCtrl1.Controls.Add(btnEquivReadUf09, 2, 0);

            tlp.SetColumnSpan(tlpUfCtrl1, 2);
            tlp.Controls.Add(tlpUfCtrl1, 0, 2);

            // KEB uf09 調控行 2: 目標電壓設定與寫入/復歸按鈕
            TableLayoutPanel tlpUfCtrl2 = new TableLayoutPanel()
            {
                Dock = DockStyle.Fill,
                ColumnCount = 3,
                RowCount = 1
            };
            tlpUfCtrl2.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 38f));
            tlpUfCtrl2.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 34f));
            tlpUfCtrl2.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 28f));

            numEquivTargetUf09 = new NumericUpDown()
            {
                Minimum = 10,
                Maximum = 260,
                Value = 50,
                Increment = 5,
                Dock = DockStyle.Fill,
                Font = new Font("Consolas", 10f, FontStyle.Bold)
            };

            btnEquivWriteUf09 = new Button()
            {
                Text = "⚡ 寫入目標",
                Font = new Font("微軟正黑體", 8.5f, FontStyle.Bold),
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(254, 243, 199),
                ForeColor = Color.FromArgb(180, 83, 9),
                FlatStyle = FlatStyle.Flat
            };
            btnEquivWriteUf09.FlatAppearance.BorderColor = Color.FromArgb(252, 211, 77);
            btnEquivWriteUf09.Click += (s, e) => WriteTargetUf09ToHardware();

            btnEquivRevertUf09 = new Button()
            {
                Text = "復歸預設",
                Font = new Font("微軟正黑體", 8.5f),
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(241, 245, 249)
            };
            btnEquivRevertUf09.Click += (s, e) => RevertUf09ToDefault();

            tlpUfCtrl2.Controls.Add(numEquivTargetUf09, 0, 0);
            tlpUfCtrl2.Controls.Add(btnEquivWriteUf09, 1, 0);
            tlpUfCtrl2.Controls.Add(btnEquivRevertUf09, 2, 0);

            tlp.SetColumnSpan(tlpUfCtrl2, 2);
            tlp.Controls.Add(tlpUfCtrl2, 0, 3);

            // 堵轉實測數據列
            numEquivVk = AddCardField(tlp, 4, "堵轉電壓 Vk (V):", 52.0m, 1, 0, 500);
            numEquivIk = AddCardField(tlp, 5, "堵轉電流 Ik (A):", 32.5m, 2, 0, 500);
            numEquivPk = AddCardField(tlp, 6, "堵轉功率 Pk (W):", 850.0m, 1, 0, 50000);
            numEquivPfk = AddCardField(tlp, 7, "堵轉因數 PFk:", 0.29m, 3, 0, 1);

            // 操作按鈕行
            btnEquivCaptureLiveLocked = new Button()
            {
                Text = "⚡ 擷取當前堵轉數據 (鎖定採樣)",
                Font = new Font("微軟正黑體", 9.5f, FontStyle.Bold),
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(254, 243, 199),
                ForeColor = Color.FromArgb(180, 83, 9),
                FlatStyle = FlatStyle.Flat,
                Margin = new Padding(0, 6, 0, 0)
            };
            btnEquivCaptureLiveLocked.FlatAppearance.BorderColor = Color.FromArgb(252, 211, 77);
            btnEquivCaptureLiveLocked.Click += (s, e) => CaptureLiveLockedData();

            tlp.SetColumnSpan(btnEquivCaptureLiveLocked, 2);
            tlp.Controls.Add(btnEquivCaptureLiveLocked, 0, 8);

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
                Dock = DockStyle.Top,
                Height = 420,
                BackColor = Color.White,
                Margin = new Padding(0, 8, 0, 8),
                Padding = new Padding(12)
            };
            pnl.Paint += (s, e) => {
                using (Pen p = new Pen(Color.FromArgb(226, 232, 240), 1))
                {
                    e.Graphics.DrawRectangle(p, 0, 0, pnl.Width - 1, pnl.Height - 1);
                }
            };

            // 頂部操作工具列
            TableLayoutPanel tlpToolbar = new TableLayoutPanel()
            {
                Dock = DockStyle.Top,
                Height = 44,
                ColumnCount = 6,
                RowCount = 1
            };
            tlpToolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 180f));
            tlpToolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110f));
            tlpToolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 180f));
            tlpToolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 190f));
            tlpToolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            tlpToolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 260f));

            Label lblR1Prompt = new Label()
            {
                Text = "定子冷態電阻 R1 (Ω):",
                Font = new Font("微軟正黑體", 9.5f, FontStyle.Bold),
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
                Font = new Font("Consolas", 10f, FontStyle.Bold),
                TextAlign = HorizontalAlignment.Right
            };

            chkAutoR1Distribute = new CheckBox()
            {
                Text = "自動依堵轉 50% 分配",
                Checked = true,
                Font = new Font("微軟正黑體", 9f),
                Dock = DockStyle.Fill
            };
            chkAutoR1Distribute.CheckedChanged += (s, e) => {
                numEquivStatorR1.Enabled = !chkAutoR1Distribute.Checked;
            };

            btnEquivCalculate = new Button()
            {
                Text = "🚀 計算等效電路參數",
                Font = new Font("微軟正黑體", 10.5f, FontStyle.Bold),
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(16, 185, 129),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat
            };
            btnEquivCalculate.FlatAppearance.BorderSize = 0;
            btnEquivCalculate.Click += (s, e) => ExecuteEquivCircuitCalculation();

            FlowLayoutPanel flpExport = new FlowLayoutPanel()
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.RightToLeft
            };
            btnEquivExportCsv = new Button()
            {
                Text = "📊 匯出 CSV",
                Font = new Font("微軟正黑體", 9f),
                Size = new Size(110, 34),
                BackColor = Color.FromArgb(241, 245, 249)
            };
            btnEquivExportCsv.Click += (s, e) => ExportEquivCircuitCsv();

            btnEquivCopyResults = new Button()
            {
                Text = "📋 複製參數",
                Font = new Font("微軟正黑體", 9f),
                Size = new Size(110, 34),
                BackColor = Color.FromArgb(241, 245, 249)
            };
            btnEquivCopyResults.Click += (s, e) => CopyEquivResultsToClipboard();

            flpExport.Controls.Add(btnEquivExportCsv);
            flpExport.Controls.Add(btnEquivCopyResults);

            tlpToolbar.Controls.Add(lblR1Prompt, 0, 0);
            tlpToolbar.Controls.Add(numEquivStatorR1, 1, 0);
            tlpToolbar.Controls.Add(chkAutoR1Distribute, 2, 0);
            tlpToolbar.Controls.Add(btnEquivCalculate, 3, 0);
            tlpToolbar.Controls.Add(flpExport, 5, 0);

            // 分割檢視：左側參數數據 DataGridView，右側 GDI+ 等效電路架構繪製圖解
            TableLayoutPanel tlpSplit = new TableLayoutPanel()
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 1,
                Margin = new Padding(0, 8, 0, 0)
            };
            tlpSplit.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 55f));
            tlpSplit.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 45f));

            dgvEquivResults = new DataGridView()
            {
                Dock = DockStyle.Fill,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                ReadOnly = true,
                RowHeadersVisible = false,
                BackgroundColor = Color.White,
                BorderStyle = BorderStyle.Fixed3D,
                Font = new Font("微軟正黑體", 9.5f),
                SelectionMode = DataGridViewSelectionMode.FullRowSelect
            };
            dgvEquivResults.ColumnHeadersDefaultCellStyle.Font = new Font("微軟正黑體", 9.5f, FontStyle.Bold);
            dgvEquivResults.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(241, 245, 249);
            dgvEquivResults.Columns.Add("Param", "等效電路參數");
            dgvEquivResults.Columns.Add("Symbol", "符號");
            dgvEquivResults.Columns.Add("Value", "計算數值");
            dgvEquivResults.Columns.Add("Unit", "單位");
            dgvEquivResults.Columns.Add("Desc", "工程物理意義");

            dgvEquivResults.Columns["Param"].Width = 140;
            dgvEquivResults.Columns["Symbol"].Width = 70;
            dgvEquivResults.Columns["Value"].Width = 100;
            dgvEquivResults.Columns["Unit"].Width = 60;
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

            tlpSplit.Controls.Add(dgvEquivResults, 0, 0);
            tlpSplit.Controls.Add(pnlEquivDiagram, 1, 0);

            pnl.Controls.Add(tlpSplit);
            pnl.Controls.Add(tlpToolbar);

            return pnl;
        }

        private void InitDefaultEquivResultsGrid()
        {
            if (dgvEquivResults == null) return;
            dgvEquivResults.Rows.Clear();
            dgvEquivResults.Rows.Add("定子相電阻", "R1", "--", "Ω", "定子繞組有效相電阻");
            dgvEquivResults.Rows.Add("定子漏電抗", "X1", "--", "Ω", "定子漏磁通等效相電抗");
            dgvEquivResults.Rows.Add("激磁電抗", "Xm", "--", "Ω", "氣隙主磁通等效激磁抗");
            dgvEquivResults.Rows.Add("鐵損電阻", "Rc", "--", "Ω", "主磁通渦流與磁滯鐵耗");
            dgvEquivResults.Rows.Add("轉子折算電阻", "R2'", "--", "Ω", "轉子繞組折算至定子端電阻");
            dgvEquivResults.Rows.Add("轉子折算漏抗", "X2'", "--", "Ω", "轉子漏磁通折算等效電抗");
            dgvEquivResults.Rows.Add("堵轉阻抗", "Zk", "--", "Ω", "轉子鎖死時之短路等效總阻抗");
            dgvEquivResults.Rows.Add("額定運轉轉差率", "sN", "--", "%", "實測額定負載不補轉差率");
            dgvEquivResults.Rows.Add("推估啟動轉矩", "Tst", "--", "Nm", "全壓啟動初始瞬態轉矩估算");
            dgvEquivResults.Rows.Add("推估最大崩潰轉矩", "Tmax", "--", "Nm", "等效電路推估之極限轉矩");
            dgvEquivResults.Rows.Add("額定預測效率", "η", "--", "%", "由等效電路損耗推估之額定效率");
        }

        #endregion

        #region 數據載入與即時採樣實作

        // 1. 從「空載測試」分頁載入
        private void LoadNoLoadDataFromTestTab()
        {
            try
            {
                if (dgvNoLoad != null && dgvNoLoad.Rows.Count > 0)
                {
                    // 尋找最後一筆或達標之穩定記錄
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
                }

                // 結合當前 B 載台 dr02 額定電壓或預設 260V
                decimal ratedV = (lastB_Dr02.HasValue && lastB_Dr02.Value > 0) ? (decimal)lastB_Dr02.Value : 260m;
                if (numEquivV0.Value <= 0 || numEquivV0.Value == 260m) numEquivV0.Value = ratedV;

                isNoLoadDataReady = true;
                lblNoLoadItemStatus.Text = "🟢 已載入空載測試數據 (" + DateTime.Now.ToString("HH:mm:ss") + ")";
                lblNoLoadItemStatus.ForeColor = Color.FromArgb(16, 185, 129);
                WriteHmiLog("EQUIV", "【等效電路】已成功從空載測試分頁提取運轉數據！");
            }
            catch (Exception ex)
            {
                MessageBox.Show("從空載分頁載入數據失敗: " + ex.Message, "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
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
                lblNoLoadItemStatus.Text = "🟢 即時空載數據已採樣 (" + DateTime.Now.ToString("HH:mm:ss") + ")";
                lblNoLoadItemStatus.ForeColor = Color.FromArgb(16, 185, 129);
                WriteHmiLog("EQUIV", string.Format("【等效電路】即時空載採樣成功: V0={0:F1}V, I0={1:F2}A, P0={2:F1}W, N0={3:F0}rpm", v, i, p, spd));
            }
            catch (Exception ex)
            {
                MessageBox.Show("即時空載採樣失敗: " + ex.Message, "錯誤", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        // 2. 從「T-N 特性測試」載入額定點
        private void LoadRatedDataFromTnTab()
        {
            try
            {
                bool found = false;
                if (dgvTnPoints != null && dgvTnPoints.Rows.Count > 0)
                {
                    // 優先搜尋最後一個點或最大轉矩點
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

                isRatedDataReady = true;
                lblRatedItemStatus.Text = "🟢 已載入 T-N 額定運轉數據 (" + DateTime.Now.ToString("HH:mm:ss") + ")";
                lblRatedItemStatus.ForeColor = Color.FromArgb(16, 185, 129);
                WriteHmiLog("EQUIV", "【等效電路】已成功從 T-N 分頁提取額定運轉數據！");
            }
            catch (Exception ex)
            {
                MessageBox.Show("從 T-N 額定點載入失敗: " + ex.Message, "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
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
                lblRatedItemStatus.Text = "🟢 即時額定數據已採樣 (不補轉差) (" + DateTime.Now.ToString("HH:mm:ss") + ")";
                lblRatedItemStatus.ForeColor = Color.FromArgb(16, 185, 129);
                WriteHmiLog("EQUIV", string.Format("【等效電路】即時額定採樣成功: TN={0:F2}Nm, NN={1:F0}rpm (不補轉差), VN={2:F1}V, IN={3:F2}A", trq, spd, v, i));
            }
            catch (Exception ex)
            {
                MessageBox.Show("即時額定採樣失敗: " + ex.Message, "錯誤", MessageBoxButtons.OK, MessageBoxIcon.Error);
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
                    originalUf09Val = val.Value;
                    lblEquivCurUf09.Text = string.Format("uf09: {0} V", val.Value);
                    lblEquivCurUf09.ForeColor = Color.FromArgb(16, 185, 129);
                    WriteHmiLog("EQUIV", string.Format("【KEB uf09 讀回成功】{0} 目前輸出電壓值為 {1} V", (driveId == 1 ? "A載台" : "B載台"), val.Value));
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

                string confirmMsg = string.Format("【⚡ 堵轉安全降壓防呆確認】\r\n\r\n您即將把 {0} 的 KEB uf09 寫入為 【{1} V】！\r\n\r\n※ 注意事項：\r\n1. 請務必確認待測馬達機構已「確實機械鎖死」！\r\n2. 降壓旨在讓堵轉電流接近額定電流，避免大電流跳脫或燒機。\r\n3. 測試完成後請務必點擊「復歸預設」！\r\n\r\n是否確定寫入？", dName, targetV);

                if (MessageBox.Show(confirmMsg, "寫入確認", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes)
                {
                    bool ok = KebWriteUf09(driveId, targetV);
                    if (ok)
                    {
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
                int defaultV = (originalUf09Val > 50) ? originalUf09Val : 260;

                bool ok = KebWriteUf09(driveId, defaultV);
                if (ok)
                {
                    lblEquivCurUf09.Text = string.Format("uf09: {0} V (正常)", defaultV);
                    lblEquivCurUf09.ForeColor = Color.FromArgb(16, 185, 129);
                    MessageBox.Show("已成功將 " + dName + " 的 uf09 復歸為 " + defaultV + " V！", "復歸完成", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                else
                {
                    MessageBox.Show("復歸 uf09 失敗，請手動確認！", "警告", MessageBoxButtons.OK, MessageBoxIcon.Warning);
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
                lblLockedItemStatus.Text = "🟢 堵轉數據已採樣鎖定 (" + DateTime.Now.ToString("HH:mm:ss") + ")";
                lblLockedItemStatus.ForeColor = Color.FromArgb(16, 185, 129);
                WriteHmiLog("EQUIV", string.Format("【等效電路】堵轉數據採樣成功: Vk={0:F1}V, Ik={1:F2}A, Pk={2:F1}W, PFk={3:F3}", v, i, p, pf));
            }
            catch (Exception ex)
            {
                MessageBox.Show("即時堵轉採樣失敗: " + ex.Message, "錯誤", MessageBoxButtons.OK, MessageBoxIcon.Error);
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

                res.Zk = Vphase_k / Iphase_k;                    // 堵轉總阻抗
                res.Rk = Pphase_k / (Iphase_k * Iphase_k);       // 堵轉總電阻
                double xk_sqr = (res.Zk * res.Zk) - (res.Rk * res.Rk);
                res.Xk = xk_sqr > 0 ? Math.Sqrt(xk_sqr) : 0.1;   // 堵轉總漏抗

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

                lastEquivResult = res;

                // 更新結果 DataGridView
                UpdateEquivResultsGrid(res);

                // 重繪等效電路圖解面板
                if (pnlEquivDiagram != null) pnlEquivDiagram.Invalidate();

                SaveLayoutConfig();
                WriteHmiLog("EQUIV_CALC", string.Format("【等效電路計算成功】R1={0:F4}Ω, X1={1:F4}Ω, Xm={2:F2}Ω, R2'={3:F4}Ω, X2'={4:F4}Ω, Tmax={5:F1}Nm",
                    res.R1, res.X1, res.Xm, res.R2_prime, res.X2_prime, res.T_max));

                MessageBox.Show(string.Format("🎉 三相感應馬達單相等效電路參數計算成功！\r\n\r\n• 定子電阻 R1 = {0:F4} Ω\r\n• 定子漏抗 X1 = {1:F4} Ω\r\n• 轉子折算電阻 R2' = {2:F4} Ω\r\n• 轉子折算漏抗 X2' = {3:F4} Ω\r\n• 激磁電抗 Xm = {4:F2} Ω\r\n• 最大崩潰轉矩 Tmax = {5:F1} Nm ({6:F2} 倍額定)",
                    res.R1, res.X1, res.R2_prime, res.X2_prime, res.Xm, res.T_max, res.T_max_ratio),
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
            dgvEquivResults.Rows.Add("定子相電阻", "R1", r.R1.ToString("F4"), "Ω", "定子繞組有效相電阻");
            dgvEquivResults.Rows.Add("定子漏電抗", "X1", r.X1.ToString("F4"), "Ω", "定子漏磁通等效相電抗");
            dgvEquivResults.Rows.Add("激磁電抗", "Xm", r.Xm.ToString("F2"), "Ω", "氣隙主磁通等效激磁抗");
            dgvEquivResults.Rows.Add("鐵損電阻", "Rc", r.Rc.ToString("F1"), "Ω", "主磁通渦流與磁滯鐵耗");
            dgvEquivResults.Rows.Add("轉子折算電阻", "R2'", r.R2_prime.ToString("F4"), "Ω", "轉子繞組折算至定子端電阻");
            dgvEquivResults.Rows.Add("轉子折算漏抗", "X2'", r.X2_prime.ToString("F4"), "Ω", "轉子漏磁通折算等效電抗");
            dgvEquivResults.Rows.Add("堵轉阻抗", "Zk", r.Zk.ToString("F4"), "Ω", "轉子鎖死時之短路等效總阻抗");
            dgvEquivResults.Rows.Add("額定運轉轉差率", "sN", r.RatedSlip.ToString("F2"), "%", "實測額定負載不補轉差率");
            dgvEquivResults.Rows.Add("推估啟動轉矩", "Tst", r.T_start.ToString("F1") + " (" + r.T_start_ratio.ToString("F2") + "x)", "Nm", "全壓啟動初始瞬態轉矩估算");
            dgvEquivResults.Rows.Add("推估最大崩潰轉矩", "Tmax", r.T_max.ToString("F1") + " (" + r.T_max_ratio.ToString("F2") + "x)", "Nm", "等效電路推估之極限轉矩");
            dgvEquivResults.Rows.Add("額定預測效率", "η", r.EstEff.ToString("F2"), "%", "由等效電路損耗推估之額定效率");
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
                g.DrawString("⚡ 三相感應電機單相 T 型等效電路圖解 (Per-Phase T-Equivalent Circuit)", fTitle, brText, 14, 12);
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
                string x1Str = lastEquivResult != null ? (lastEquivResult.X1.ToString("F4") + " Ω") : "--";
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
                string xmStr = lastEquivResult != null ? (lastEquivResult.Xm.ToString("F2") + "Ω") : "--";
                g.DrawString(xmStr, fVal, valBrush, xMagBranch + 34, yMid + 6);

                g.DrawLine(wirePen, xMagBranch - 28, yBot - 25, xMagBranch + 28, yBot - 25);
                g.DrawLine(wirePen, xMagBranch, yBot - 25, xMagBranch, yBot);

                // 5. 轉子元件: X2' (轉子折算漏抗)
                DrawInductorSymbol(g, compPen, xRotorX, yTop, true);
                g.DrawString("X2' (轉子漏抗)", fLbl, textBrush, xRotorX - 35, yTop - 42);
                string x2Str = lastEquivResult != null ? (lastEquivResult.X2_prime.ToString("F4") + " Ω") : "--";
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
                string motorHint = string.Format("★ 待測機種: {0} | 額定轉差: {1:F2}% | 功率因數: {2:F2} | 激磁抗: {3:F2} Ω",
                    motorModelName,
                    lastEquivResult != null ? lastEquivResult.RatedSlip : (double)numEquivSlip.Value,
                    (double)numEquivPfn.Value,
                    lastEquivResult != null ? lastEquivResult.Xm : 0.0);
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
                        lblEquivMotorStatus.Text = string.Format("🔗 待測馬達: 【{0}】 | B載台 dr: [{1}] | 一致性: 🟢 同一馬達記憶中 (1,2項數據已保留)", curName, bDrSummary);
                        lblEquivMotorStatus.ForeColor = Color.FromArgb(15, 23, 42);
                    }
                    else
                    {
                        lblEquivMotorStatus.Text = string.Format("⚠️ 偵測到馬達變更: 【{0}】 (前次: {1}) | B載台 dr: [{2}] | 建議點擊右方清除數據", curName, cachedEquivMotorName, bDrSummary);
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
            numEquivV0.Value = 260m; numEquivI0.Value = 12m; numEquivP0.Value = 450m; numEquivPf0.Value = 0.08m; numEquivN0.Value = 1498m; numEquivF0.Value = 50m;
            numEquivTn.Value = 70m; numEquivNn.Value = 1465m; numEquivVn.Value = 260m; numEquivIn.Value = 32.3m; numEquivPn.Value = 12.8m; numEquivPfn.Value = 0.86m; numEquivSlip.Value = 2.33m;
            numEquivVk.Value = 52m; numEquivIk.Value = 32.5m; numEquivPk.Value = 850m; numEquivPfk.Value = 0.29m;

            isNoLoadDataReady = false;
            isRatedDataReady = false;
            isLockedDataReady = false;

            lblNoLoadItemStatus.Text = "⚪ 待採樣 (可從空載測試載入或即時抓取)";
            lblNoLoadItemStatus.ForeColor = Color.FromArgb(100, 116, 139);

            lblRatedItemStatus.Text = "⚪ 待採樣 (可從 T-N 額定點載入或即時抓取)";
            lblRatedItemStatus.ForeColor = Color.FromArgb(100, 116, 139);

            lblLockedItemStatus.Text = "⚪ 待採樣 (請先降低 uf09 並鎖定轉子)";
            lblLockedItemStatus.ForeColor = Color.FromArgb(100, 116, 139);

            cachedEquivMotorName = !string.IsNullOrEmpty(motorModelName) ? motorModelName : "SVM100S";
            cachedEquivDrFingerprint = lastKnownB_DrFingerprint;

            lastEquivResult = null;
            InitDefaultEquivResultsGrid();
            if (pnlEquivDiagram != null) pnlEquivDiagram.Invalidate();

            RefreshEquivMotorStatus();
            WriteHmiLog("EQUIV", "【等效電路】已清空所有前次採樣數據，準備全新馬達測試。");
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
                    sb.AppendLine(string.Format("定子漏電抗 X1: {0:F4} Ω", lastEquivResult.X1));
                    sb.AppendLine(string.Format("激磁電抗 Xm: {0:F2} Ω", lastEquivResult.Xm));
                    sb.AppendLine(string.Format("鐵損電阻 Rc: {0:F1} Ω", lastEquivResult.Rc));
                    sb.AppendLine(string.Format("轉子折算電阻 R2': {0:F4} Ω", lastEquivResult.R2_prime));
                    sb.AppendLine(string.Format("轉子折算漏抗 X2': {0:F4} Ω", lastEquivResult.X2_prime));
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
                    if (sfd.ShowDialog() == DialogResult.OK)
                    {
                        StringBuilder sb = new StringBuilder();
                        sb.AppendLine("項目,符號,數值,單位,說明");
                        if (dgvEquivResults != null)
                        {
                            foreach (DataGridViewRow r in dgvEquivResults.Rows)
                            {
                                sb.AppendLine(string.Format("\"{0}\",\"{1}\",\"{2}\",\"{3}\",\"{4}\"",
                                    r.Cells["Param"].Value, r.Cells["Symbol"].Value, r.Cells["Value"].Value, r.Cells["Unit"].Value, r.Cells["Desc"].Value));
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
                sb.AppendLine("V0=" + (numEquivV0 != null ? numEquivV0.Value.ToString(System.Globalization.CultureInfo.InvariantCulture) : "260"));
                sb.AppendLine("I0=" + (numEquivI0 != null ? numEquivI0.Value.ToString(System.Globalization.CultureInfo.InvariantCulture) : "12"));
                sb.AppendLine("P0=" + (numEquivP0 != null ? numEquivP0.Value.ToString(System.Globalization.CultureInfo.InvariantCulture) : "450"));
                sb.AppendLine("Pf0=" + (numEquivPf0 != null ? numEquivPf0.Value.ToString(System.Globalization.CultureInfo.InvariantCulture) : "0.08"));
                sb.AppendLine("N0=" + (numEquivN0 != null ? numEquivN0.Value.ToString(System.Globalization.CultureInfo.InvariantCulture) : "1498"));
                sb.AppendLine("F0=" + (numEquivF0 != null ? numEquivF0.Value.ToString(System.Globalization.CultureInfo.InvariantCulture) : "50"));

                sb.AppendLine("RatedReady=" + (isRatedDataReady ? "1" : "0"));
                sb.AppendLine("Tn=" + (numEquivTn != null ? numEquivTn.Value.ToString(System.Globalization.CultureInfo.InvariantCulture) : "70"));
                sb.AppendLine("Nn=" + (numEquivNn != null ? numEquivNn.Value.ToString(System.Globalization.CultureInfo.InvariantCulture) : "1465"));
                sb.AppendLine("Vn=" + (numEquivVn != null ? numEquivVn.Value.ToString(System.Globalization.CultureInfo.InvariantCulture) : "260"));
                sb.AppendLine("In=" + (numEquivIn != null ? numEquivIn.Value.ToString(System.Globalization.CultureInfo.InvariantCulture) : "32.3"));
                sb.AppendLine("Pn=" + (numEquivPn != null ? numEquivPn.Value.ToString(System.Globalization.CultureInfo.InvariantCulture) : "12.8"));
                sb.AppendLine("Pfn=" + (numEquivPfn != null ? numEquivPfn.Value.ToString(System.Globalization.CultureInfo.InvariantCulture) : "0.86"));
                sb.AppendLine("Slip=" + (numEquivSlip != null ? numEquivSlip.Value.ToString(System.Globalization.CultureInfo.InvariantCulture) : "2.33"));

                sb.AppendLine("LockedReady=" + (isLockedDataReady ? "1" : "0"));
                sb.AppendLine("Vk=" + (numEquivVk != null ? numEquivVk.Value.ToString(System.Globalization.CultureInfo.InvariantCulture) : "52"));
                sb.AppendLine("Ik=" + (numEquivIk != null ? numEquivIk.Value.ToString(System.Globalization.CultureInfo.InvariantCulture) : "32.5"));
                sb.AppendLine("Pk=" + (numEquivPk != null ? numEquivPk.Value.ToString(System.Globalization.CultureInfo.InvariantCulture) : "850"));
                sb.AppendLine("Pfk=" + (numEquivPfk != null ? numEquivPfk.Value.ToString(System.Globalization.CultureInfo.InvariantCulture) : "0.29"));
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

                if (map.ContainsKey("EquivCircuit.NoLoadReady") && map["EquivCircuit.NoLoadReady"] == "1") isNoLoadDataReady = true;
                if (map.ContainsKey("EquivCircuit.V0") && numEquivV0 != null) { decimal v0; if (decimal.TryParse(map["EquivCircuit.V0"], System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out v0)) numEquivV0.Value = v0; }
                if (map.ContainsKey("EquivCircuit.I0") && numEquivI0 != null) { decimal i0; if (decimal.TryParse(map["EquivCircuit.I0"], System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out i0)) numEquivI0.Value = i0; }
                if (map.ContainsKey("EquivCircuit.P0") && numEquivP0 != null) { decimal p0; if (decimal.TryParse(map["EquivCircuit.P0"], System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out p0)) numEquivP0.Value = p0; }
                if (map.ContainsKey("EquivCircuit.Pf0") && numEquivPf0 != null) { decimal pf0; if (decimal.TryParse(map["EquivCircuit.Pf0"], System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out pf0)) numEquivPf0.Value = pf0; }
                if (map.ContainsKey("EquivCircuit.N0") && numEquivN0 != null) { decimal n0; if (decimal.TryParse(map["EquivCircuit.N0"], System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out n0)) numEquivN0.Value = n0; }
                if (map.ContainsKey("EquivCircuit.F0") && numEquivF0 != null) { decimal f0; if (decimal.TryParse(map["EquivCircuit.F0"], System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out f0)) numEquivF0.Value = f0; }

                if (map.ContainsKey("EquivCircuit.RatedReady") && map["EquivCircuit.RatedReady"] == "1") isRatedDataReady = true;
                if (map.ContainsKey("EquivCircuit.Tn") && numEquivTn != null) { decimal tn; if (decimal.TryParse(map["EquivCircuit.Tn"], System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out tn)) numEquivTn.Value = tn; }
                if (map.ContainsKey("EquivCircuit.Nn") && numEquivNn != null) { decimal nn; if (decimal.TryParse(map["EquivCircuit.Nn"], System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out nn)) numEquivNn.Value = nn; }
                if (map.ContainsKey("EquivCircuit.Vn") && numEquivVn != null) { decimal vn; if (decimal.TryParse(map["EquivCircuit.Vn"], System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out vn)) numEquivVn.Value = vn; }
                if (map.ContainsKey("EquivCircuit.In") && numEquivIn != null) { decimal inn; if (decimal.TryParse(map["EquivCircuit.In"], System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out inn)) numEquivIn.Value = inn; }
                if (map.ContainsKey("EquivCircuit.Pn") && numEquivPn != null) { decimal pn; if (decimal.TryParse(map["EquivCircuit.Pn"], System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out pn)) numEquivPn.Value = pn; }
                if (map.ContainsKey("EquivCircuit.Pfn") && numEquivPfn != null) { decimal pfn; if (decimal.TryParse(map["EquivCircuit.Pfn"], System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out pfn)) numEquivPfn.Value = pfn; }
                if (map.ContainsKey("EquivCircuit.Slip") && numEquivSlip != null) { decimal slip; if (decimal.TryParse(map["EquivCircuit.Slip"], System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out slip)) numEquivSlip.Value = slip; }

                if (map.ContainsKey("EquivCircuit.LockedReady") && map["EquivCircuit.LockedReady"] == "1") isLockedDataReady = true;
                if (map.ContainsKey("EquivCircuit.Vk") && numEquivVk != null) { decimal vk; if (decimal.TryParse(map["EquivCircuit.Vk"], System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out vk)) numEquivVk.Value = vk; }
                if (map.ContainsKey("EquivCircuit.Ik") && numEquivIk != null) { decimal ik; if (decimal.TryParse(map["EquivCircuit.Ik"], System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out ik)) numEquivIk.Value = ik; }
                if (map.ContainsKey("EquivCircuit.Pk") && numEquivPk != null) { decimal pk; if (decimal.TryParse(map["EquivCircuit.Pk"], System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out pk)) numEquivPk.Value = pk; }
                if (map.ContainsKey("EquivCircuit.Pfk") && numEquivPfk != null) { decimal pfk; if (decimal.TryParse(map["EquivCircuit.Pfk"], System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out pfk)) numEquivPfk.Value = pfk; }
                if (map.ContainsKey("EquivCircuit.TargetUf09") && numEquivTargetUf09 != null) { decimal tuf09; if (decimal.TryParse(map["EquivCircuit.TargetUf09"], System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out tuf09)) numEquivTargetUf09.Value = tuf09; }
                if (map.ContainsKey("EquivCircuit.AutoR1") && chkAutoR1Distribute != null) chkAutoR1Distribute.Checked = (map["EquivCircuit.AutoR1"] == "1");
                if (map.ContainsKey("EquivCircuit.StatorR1") && numEquivStatorR1 != null) { decimal r1; if (decimal.TryParse(map["EquivCircuit.StatorR1"], System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out r1)) numEquivStatorR1.Value = r1; }

                // 更新狀態標籤
                if (isNoLoadDataReady && lblNoLoadItemStatus != null)
                {
                    lblNoLoadItemStatus.Text = "🟢 已載入記憶空載數據";
                    lblNoLoadItemStatus.ForeColor = Color.FromArgb(16, 185, 129);
                }
                if (isRatedDataReady && lblRatedItemStatus != null)
                {
                    lblRatedItemStatus.Text = "🟢 已載入記憶額定數據";
                    lblRatedItemStatus.ForeColor = Color.FromArgb(16, 185, 129);
                }
                if (isLockedDataReady && lblLockedItemStatus != null)
                {
                    lblLockedItemStatus.Text = "🟢 已載入記憶堵轉數據";
                    lblLockedItemStatus.ForeColor = Color.FromArgb(16, 185, 129);
                }
                RefreshEquivMotorStatus();
            }
            catch { }
        }

        #endregion
    }
}
