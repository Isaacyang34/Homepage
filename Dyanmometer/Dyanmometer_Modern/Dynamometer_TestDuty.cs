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
            splitDuty.SplitterMoved += (s, e) => SaveLayoutConfig();
            SafeSetupSplitContainer(splitDuty, 550, 150, 100);

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
            splitDutyTop.SplitterMoved += (s, e) => SaveLayoutConfig();
            SafeSetupSplitContainer(splitDutyTop, 880, 250, 150);

            // -------------------------------------------------------------
            // 左側：控制參數、KEB全自動模式連鎖與 V/F 雙重定錨/S2錨點設定群組
            // -------------------------------------------------------------
            GroupBox grp = new GroupBox()
            {
                Text = "IEC 60034-1 工作制測試設定 (含 KEB 全自動連鎖與 S2/S6 錨點控制)",
                Dock = DockStyle.Fill,
                Font = new Font("微軟正黑體", 14f, FontStyle.Bold),
                BackColor = Color.FromArgb(248, 250, 252)
            };

            // 頂部全自動模式提醒條
            lblKebAutoModeHint = new Label()
            {
                Text = "⚡ 注意：DUTY 需在 KEB 全自動模式運作 (待測: Mode 9 速度 / 加載: Mode 10 轉矩)。點擊【開始】系統將自動切換！",
                Location = new Point(12, 30),
                AutoSize = true,
                Font = new Font("微軟正黑體", 11.5f, FontStyle.Bold),
                ForeColor = Color.FromArgb(194, 65, 12),
                BackColor = Color.FromArgb(254, 242, 242),
                BorderStyle = BorderStyle.FixedSingle,
                Padding = new Padding(4)
            };

            // 第一列 (Y = 66)：工作制、載台角色
            Label lMode = new Label() { Text = "工作制:", Location = new Point(12, 70), AutoSize = true, Font = new Font("微軟正黑體", 13f, FontStyle.Bold) };
            cmbDutyMode = new ComboBox() { Location = new Point(85, 66), Width = 260, DropDownStyle = ComboBoxStyle.DropDownList, Font = new Font("微軟正黑體", 12f) };
            cmbDutyMode.Items.AddRange(new object[] { "S1 連續工作制 (30分熱平衡判定)", "S2 短時工作制 (錨點測試/超溫停機)", "S6 週期工作制 (Periodic ED%)" });
            cmbDutyMode.SelectedIndex = 2; // 預設 S6
            cmbDutyMode.SelectedIndexChanged += (s, e) => {
                if (!isSyncingDutyControls && cmbDutyMiniMode != null && cmbDutyMiniMode.SelectedIndex != cmbDutyMode.SelectedIndex)
                {
                    isSyncingDutyControls = true;
                    cmbDutyMiniMode.SelectedIndex = cmbDutyMode.SelectedIndex;
                    isSyncingDutyControls = false;
                }
                UpdateDutyModeVisibility(cmbDutyMode.SelectedIndex);
                if (pnlS6Diagram != null) pnlS6Diagram.Invalidate();
                if (pnlS6MiniDiagram != null) pnlS6MiniDiagram.Invalidate();
            };

            Label lRole = new Label() { Text = "待測端:", Location = new Point(360, 70), AutoSize = true, Font = new Font("微軟正黑體", 13f, FontStyle.Bold) };
            cmbDutyRole = new ComboBox() { Location = new Point(430, 66), Width = 240, DropDownStyle = ComboBoxStyle.DropDownList, Font = new Font("微軟正黑體", 12f) };
            cmbDutyRole.Items.AddRange(new object[] { "A載台待測(速度) / B加載", "B載台待測(速度) / A加載" });
            cmbDutyRole.SelectedIndex = 1; // 預設：B載台待測(速度) / A加載
            cmbDutyRole.SelectedIndexChanged += (s, e) => {
                if (!isSyncingDutyControls && cmbDutyMiniRole != null && cmbDutyMiniRole.SelectedIndex != cmbDutyRole.SelectedIndex)
                {
                    isSyncingDutyControls = true;
                    cmbDutyMiniRole.SelectedIndex = cmbDutyRole.SelectedIndex;
                    isSyncingDutyControls = false;
                }
            };

            // 第二列 (Y = 106)：目標轉速、加載轉矩 + 模式專屬參數 (斷行到第二行，寬度充裕)
            Label lSpd = new Label() { Text = "轉速(rpm):", Location = new Point(12, 110), AutoSize = true, Font = new Font("微軟正黑體", 13f, FontStyle.Bold) };
            numDutySpeed = CreateNumericUpDown(new Point(110, 106), 105, 0, 4000, 1000);
            numDutySpeed.Font = new Font("微軟正黑體", 13f, FontStyle.Bold);
            numDutySpeed.ValueChanged += (s, e) => {
                if (!isSyncingDutyControls)
                {
                    isSyncingDutyControls = true;
                    if (numDutyMiniSpd != null && numDutyMiniSpd.Value != numDutySpeed.Value)
                        numDutyMiniSpd.Value = numDutySpeed.Value;
                    if (numCardTargetSpeed != null && numCardTargetSpeed.Value != numDutySpeed.Value)
                        numCardTargetSpeed.Value = Math.Min(numCardTargetSpeed.Maximum, Math.Max(numCardTargetSpeed.Minimum, numDutySpeed.Value));
                    if (numS6AnchorNoLoadSpd != null && !s6HasNoLoadAnchor)
                    {
                        numS6AnchorNoLoadSpd.Value = numDutySpeed.Value;
                        s6AnchorNoLoadSpeed = (double)numDutySpeed.Value;
                    }
                    isSyncingDutyControls = false;
                }
            };

            Label lTrq = new Label() { Text = "轉矩(Nm):", Location = new Point(230, 110), AutoSize = true, Font = new Font("微軟正黑體", 13f, FontStyle.Bold) };
            numDutyTorque = CreateNumericUpDown(new Point(325, 106), 95, 0, 500, 15, 1); // 預設 15.0 Nm
            numDutyTorque.Font = new Font("微軟正黑體", 13f, FontStyle.Bold);
            numDutyTorque.ValueChanged += (s, e) => {
                if (!isSyncingDutyControls)
                {
                    isSyncingDutyControls = true;
                    if (numDutyMiniTrq != null && numDutyMiniTrq.Value != numDutyTorque.Value)
                        numDutyMiniTrq.Value = numDutyTorque.Value;
                    if (numCardTargetTorque != null && numCardTargetTorque.Value != numDutyTorque.Value)
                        numCardTargetTorque.Value = Math.Min(numCardTargetTorque.Maximum, Math.Max(numCardTargetTorque.Minimum, numDutyTorque.Value));
                    isSyncingDutyControls = false;
                }
                if (pnlS6Diagram != null) pnlS6Diagram.Invalidate();
                if (pnlS6MiniDiagram != null) pnlS6MiniDiagram.Invalidate();
            };

            // S1 第二列右側：啟用熱平衡勾選
            chkS1ThermalStop = new CheckBox() { Text = "啟用 30分溫差 < 1.0°C 熱平衡自動停機", Location = new Point(445, 108), AutoSize = true, Font = new Font("微軟正黑體", 12.5f, FontStyle.Bold), Checked = true };
            chkS1ThermalStop.CheckedChanged += (s, e) => { btnS1SelectChannels.Enabled = chkS1ThermalStop.Checked; };

            // S2 第二列右側：時長與超溫停機勾選 (通道與閥值斷行至第三列)
            lblS2Duration = new Label() { Text = "S2 時長(分):", Location = new Point(445, 110), AutoSize = true, Font = new Font("微軟正黑體", 12.5f, FontStyle.Bold) };
            numS2DurationMin = CreateNumericUpDown(new Point(555, 106), 80, 1, 300, 30);
            numS2DurationMin.Font = new Font("微軟正黑體", 12f, FontStyle.Bold);

            chkS2TempStop = new CheckBox() { Text = "超溫停機", Location = new Point(650, 108), AutoSize = true, Font = new Font("微軟正黑體", 12f, FontStyle.Bold), Checked = true };

            // -------------------------------------------------------------
            // 第三列 (Y = 146)：S1 選擇通道 / S2 [通道:...] (斷行獨立一行) / S6 [週期T(分)] (斷行獨立一行)
            // -------------------------------------------------------------
            // S1 第三列：通道選擇按鈕與文字
            btnS1SelectChannels = new Button() { Text = "⚙ 選擇監測通道...", Location = new Point(12, 142), Size = new Size(165, 34), BackColor = Color.FromArgb(241, 245, 249), Font = new Font("微軟正黑體", 11.5f) };
            btnS1SelectChannels.Click += (s, e) => { ShowS1ChannelSelectDialog(); };
            lblS1SelectedChHint = new Label() { Text = "(已選 CH1~4，共 4 通道)", Location = new Point(185, 148), AutoSize = true, ForeColor = Color.FromArgb(70, 80, 95), Font = new Font("微軟正黑體", 11.5f) };

            // S2 第三列：[通道:...] 斷行獨立一行排版
            lblS2TempCh = new Label() { Text = "通道:", Location = new Point(12, 148), AutoSize = true, Font = new Font("微軟正黑體", 12f, FontStyle.Bold) };
            cmbS2TempCh = new ComboBox() { Location = new Point(65, 144), Width = 110, DropDownStyle = ComboBoxStyle.DropDownList, Font = new Font("微軟正黑體", 11f) };
            for (int i = 0; i < 20; i++)
            {
                string cName = (gl820ChannelNames != null && i < gl820ChannelNames.Length && !string.IsNullOrEmpty(gl820ChannelNames[i])) ? gl820ChannelNames[i] : ("CH" + (i + 1));
                cmbS2TempCh.Items.Add(string.Format("CH{0} ({1})", i + 1, cName));
            }
            if (cmbS2TempCh.Items.Count > 0) cmbS2TempCh.SelectedIndex = 0;

            lblS2TempThresh = new Label() { Text = "閥值(°C):", Location = new Point(190, 148), AutoSize = true, Font = new Font("微軟正黑體", 12f, FontStyle.Bold) };
            numS2TempThreshold = CreateNumericUpDown(new Point(270, 144), 75, 20, 150, 80, 1, 0.5m);
            numS2TempThreshold.Font = new Font("微軟正黑體", 12f, FontStyle.Bold);
            chkS2TempStop.CheckedChanged += (s, e) => {
                cmbS2TempCh.Enabled = chkS2TempStop.Checked;
                numS2TempThreshold.Enabled = chkS2TempStop.Checked;
            };

            // S6 第三列：[週期T(分)] 斷行獨立一行排版 (含 ED%、循環數與 T1/T2 預估)
            lblS6CycleLabel = new Label() { Text = "週期T(分):", Location = new Point(12, 148), AutoSize = true, Font = new Font("微軟正黑體", 12f, FontStyle.Bold) };
            numS6CycleMin = CreateNumericUpDown(new Point(105, 144), 75, 1, 60, 10, 1, 0.5m);
            numS6CycleMin.Font = new Font("微軟正黑體", 12f, FontStyle.Bold);
            numS6CycleMin.ValueChanged += (s, e) => {
                if (!isSyncingDutyControls && numDutyMiniCycleMin != null && numDutyMiniCycleMin.Value != numS6CycleMin.Value)
                {
                    isSyncingDutyControls = true;
                    numDutyMiniCycleMin.Value = numS6CycleMin.Value;
                    isSyncingDutyControls = false;
                }
                UpdateS6CalcInfo();
                if (pnlS6Diagram != null) pnlS6Diagram.Invalidate();
                if (pnlS6MiniDiagram != null) pnlS6MiniDiagram.Invalidate();
            };

            lblS6EdLabel = new Label() { Text = "ED%:", Location = new Point(190, 148), AutoSize = true, Font = new Font("微軟正黑體", 12f, FontStyle.Bold) };
            numS6Ed = CreateNumericUpDown(new Point(240, 144), 70, 1, 100, 40);
            numS6Ed.Font = new Font("微軟正黑體", 12f, FontStyle.Bold);
            numS6Ed.ValueChanged += (s, e) => {
                if (!isSyncingDutyControls && numDutyMiniEd != null && numDutyMiniEd.Value != numS6Ed.Value)
                {
                    isSyncingDutyControls = true;
                    numDutyMiniEd.Value = numS6Ed.Value;
                    isSyncingDutyControls = false;
                }
                UpdateS6CalcInfo();
                if (pnlS6Diagram != null) pnlS6Diagram.Invalidate();
                if (pnlS6MiniDiagram != null) pnlS6MiniDiagram.Invalidate();
            };

            lblS6CyclesLabel = new Label() { Text = "循環:", Location = new Point(320, 148), AutoSize = true, Font = new Font("微軟正黑體", 12f, FontStyle.Bold) };
            numS6Cycles = CreateNumericUpDown(new Point(370, 144), 65, 1, 50, 3);
            numS6Cycles.Font = new Font("微軟正黑體", 12f, FontStyle.Bold);
            numS6Cycles.ValueChanged += (s, e) => {
                if (!isSyncingDutyControls && numDutyMiniCycles != null && numDutyMiniCycles.Value != numS6Cycles.Value)
                {
                    isSyncingDutyControls = true;
                    numDutyMiniCycles.Value = numS6Cycles.Value;
                    isSyncingDutyControls = false;
                }
            };

            lblS6CalcInfo = new Label() { Text = "T1=4.0m, T2=6.0m", Location = new Point(450, 148), AutoSize = true, ForeColor = Color.FromArgb(3, 105, 161), Font = new Font("微軟正黑體", 11.5f, FontStyle.Bold) };

            // -------------------------------------------------------------
            // 第四列 (Y = 186)：S2 錨點控制項 / S6 [通道:...] (斷行獨立一行)
            // -------------------------------------------------------------
            // S2 錨點控制項
            lblS2AnchorTitle = new Label() { Text = "📍 S2 錨點：", Location = new Point(12, 188), AutoSize = true, Font = new Font("微軟正黑體", 12f, FontStyle.Bold), ForeColor = Color.FromArgb(30, 64, 175) };
            lblS2AnchorSy52 = new Label() { Text = "SY52(rpm):", Location = new Point(110, 188), AutoSize = true, Font = new Font("微軟正黑體", 11.5f, FontStyle.Bold) };
            numS2AnchorSy52 = CreateNumericUpDown(new Point(195, 184), 85, 0, 6000, s2AnchorSy52, 0, 50);
            numS2AnchorSy52.Font = new Font("微軟正黑體", 11.5f, FontStyle.Bold);
            numS2AnchorSy52.ValueChanged += (s, e) => {
                s2AnchorSy52 = (int)numS2AnchorSy52.Value;
                if (!isSyncingDutyControls && numDutyMiniS2Sy52 != null && numDutyMiniS2Sy52.Value != numS2AnchorSy52.Value)
                {
                    isSyncingDutyControls = true;
                    numDutyMiniS2Sy52.Value = numS2AnchorSy52.Value;
                    isSyncingDutyControls = false;
                }
            };

            lblS2AnchorCs18 = new Label() { Text = "CS18(‰):", Location = new Point(290, 188), AutoSize = true, Font = new Font("微軟正黑體", 11.5f, FontStyle.Bold) };
            numS2AnchorCs18 = CreateNumericUpDown(new Point(360, 184), 80, 0, 1000, s2AnchorCs18, 0, 10);
            numS2AnchorCs18.Font = new Font("微軟正黑體", 11.5f, FontStyle.Bold);
            numS2AnchorCs18.ValueChanged += (s, e) => {
                s2AnchorCs18 = (int)numS2AnchorCs18.Value;
                if (!isSyncingDutyControls && numDutyMiniS2Cs18 != null && numDutyMiniS2Cs18.Value != numS2AnchorCs18.Value)
                {
                    isSyncingDutyControls = true;
                    numDutyMiniS2Cs18.Value = numS2AnchorCs18.Value;
                    isSyncingDutyControls = false;
                }
            };

            btnS2RecordAnchor = new Button() { Text = "📍 記錄為錨點", Location = new Point(455, 182), Size = new Size(130, 34), BackColor = Color.FromArgb(16, 185, 129), ForeColor = Color.White, Font = new Font("微軟正黑體", 11f, FontStyle.Bold) };
            btnS2RecordAnchor.Click += (s, e) => { CaptureCurrentAnchorToS2(); };

            btnS2ResetAnchor = new Button() { Text = "↺ 歸零", Location = new Point(595, 182), Size = new Size(80, 34), BackColor = Color.FromArgb(239, 68, 68), ForeColor = Color.White, Font = new Font("微軟正黑體", 11f, FontStyle.Bold) };
            btnS2ResetAnchor.Click += (s, e) => { ResetS2Anchor(); };

            // S6 第四列：[通道:...] 斷行獨立一行排版
            lblS6TempCh = new Label() { Text = "通道:", Location = new Point(12, 188), AutoSize = true, Font = new Font("微軟正黑體", 12f, FontStyle.Bold) };
            cmbS6TempCh = new ComboBox() { Location = new Point(65, 184), Width = 85, DropDownStyle = ComboBoxStyle.DropDownList, Font = new Font("微軟正黑體", 11f) };
            for (int i = 1; i <= 20; i++) cmbS6TempCh.Items.Add("CH " + i);
            cmbS6TempCh.SelectedIndex = 0;
            cmbS6TempCh.SelectedIndexChanged += (s, e) => {
                if (cmbDutyMode != null && cmbDutyMode.SelectedIndex == 2 && lblDutyTempTrendTitle != null)
                    lblDutyTempTrendTitle.Text = string.Format("目前監測通道: S6 通道 {0} (每10分鐘最高溫熱平衡分析)", cmbS6TempCh.SelectedIndex + 1);
            };
            lblS6TempChHint = new Label() { Text = "(S6 每10分鐘最高溫熱平衡監測通道)", Location = new Point(160, 188), AutoSize = true, ForeColor = Color.FromArgb(70, 80, 95), Font = new Font("微軟正黑體", 11f, FontStyle.Bold) };

            // -------------------------------------------------------------
            // 第五列 (Y = 226)：S6 定錨按鈕與數值設定列 (空載SY52 / 加載SY52 / CS18)
            // -------------------------------------------------------------
            lblS6NoLoadTitle = new Label() { Text = "空載SY52:", Location = new Point(12, 228), AutoSize = true, Font = new Font("微軟正黑體", 11.5f, FontStyle.Bold) };
            numS6AnchorNoLoadSpd = CreateNumericUpDown(new Point(98, 224), 80, 0, 6000, 0, 0, 50);
            numS6AnchorNoLoadSpd.Font = new Font("微軟正黑體", 11.5f, FontStyle.Bold);
            numS6AnchorNoLoadSpd.ValueChanged += (s, e) => {
                s6AnchorNoLoadSpeed = (double)numS6AnchorNoLoadSpd.Value;
                s6HasNoLoadAnchor = (s6AnchorNoLoadSpeed > 0);
                if (!isSyncingDutyControls && numDutyMiniS6NoLoadSpd != null && numDutyMiniS6NoLoadSpd.Value != numS6AnchorNoLoadSpd.Value)
                {
                    isSyncingDutyControls = true;
                    numDutyMiniS6NoLoadSpd.Value = numS6AnchorNoLoadSpd.Value;
                    isSyncingDutyControls = false;
                }
                UpdateS6AnchorStatusText();
            };
            btnS6AnchorNoLoad = new Button() { Text = "📍 記空載 (V)", Location = new Point(185, 222), Size = new Size(110, 34), BackColor = Color.FromArgb(14, 165, 233), ForeColor = Color.White, Font = new Font("微軟正黑體", 11f, FontStyle.Bold) };
            btnS6AnchorNoLoad.Click += (s, e) => { CaptureCurrentAnchorToS6NoLoad(); };

            lblS6LoadedTitle = new Label() { Text = "加載SY52:", Location = new Point(305, 228), AutoSize = true, Font = new Font("微軟正黑體", 11.5f, FontStyle.Bold) };
            numS6AnchorLoadedSpd = CreateNumericUpDown(new Point(390, 224), 80, 0, 6000, 0, 0, 50);
            numS6AnchorLoadedSpd.Font = new Font("微軟正黑體", 11.5f, FontStyle.Bold);
            numS6AnchorLoadedSpd.ValueChanged += (s, e) => {
                s6AnchorLoadedSpeed = (double)numS6AnchorLoadedSpd.Value;
                s6HasLoadedAnchor = (s6AnchorLoadedSpeed > 0 && s6AnchorLoadedTorquePct > 0);
                if (!isSyncingDutyControls && numDutyMiniS6LoadedSpd != null && numDutyMiniS6LoadedSpd.Value != numS6AnchorLoadedSpd.Value)
                {
                    isSyncingDutyControls = true;
                    numDutyMiniS6LoadedSpd.Value = numS6AnchorLoadedSpd.Value;
                    isSyncingDutyControls = false;
                }
                UpdateS6AnchorStatusText();
            };

            lblS6Cs18Title = new Label() { Text = "CS18(‰):", Location = new Point(480, 228), AutoSize = true, Font = new Font("微軟正黑體", 11.5f, FontStyle.Bold) };
            numS6AnchorLoadedCs18 = CreateNumericUpDown(new Point(555, 224), 75, 0, 1000, 0, 0, 10);
            numS6AnchorLoadedCs18.Font = new Font("微軟正黑體", 11.5f, FontStyle.Bold);
            numS6AnchorLoadedCs18.ValueChanged += (s, e) => {
                s6AnchorLoadedTorquePct = (double)numS6AnchorLoadedCs18.Value / 10.0;
                s6HasLoadedAnchor = (s6AnchorLoadedSpeed > 0 && s6AnchorLoadedTorquePct > 0);
                if (!isSyncingDutyControls && numDutyMiniS6LoadedCs18 != null && numDutyMiniS6LoadedCs18.Value != numS6AnchorLoadedCs18.Value)
                {
                    isSyncingDutyControls = true;
                    numDutyMiniS6LoadedCs18.Value = numS6AnchorLoadedCs18.Value;
                    isSyncingDutyControls = false;
                }
                UpdateS6AnchorStatusText();
            };

            btnS6AnchorLoaded = new Button() { Text = "📍 記加載 (N)", Location = new Point(640, 222), Size = new Size(110, 34), BackColor = Color.FromArgb(139, 92, 246), ForeColor = Color.White, Font = new Font("微軟正黑體", 11f, FontStyle.Bold) };
            btnS6AnchorLoaded.Click += (s, e) => { CaptureCurrentAnchorToS6Loaded(); };

            btnS6ResetAnchor = new Button() { Text = "↺ 歸零", Location = new Point(760, 222), Size = new Size(80, 34), BackColor = Color.FromArgb(239, 68, 68), ForeColor = Color.White, Font = new Font("微軟正黑體", 11f, FontStyle.Bold) };
            btnS6ResetAnchor.Click += (s, e) => { ResetS6Anchors(); };

            // -------------------------------------------------------------
            // 第六列 (Y = 266)：定錨狀態 (橘色文字斷行獨立一行，醒目大氣)
            // -------------------------------------------------------------
            lblS6AnchorStatus = new Label() { Text = "定錨狀態: 空載=未定錨 | 加載=未定錨", Location = new Point(12, 266), AutoSize = true, ForeColor = Color.DarkOrange, Font = new Font("微軟正黑體", 12.5f, FontStyle.Bold) };

            // -------------------------------------------------------------
            // 第七列 (Y = 302)：【AB載台現況】獨立一行排版 (全模式可見)
            // -------------------------------------------------------------
            lblDutyAbStatus = new Label() { Text = "【AB載台現況】待命準備中", Location = new Point(12, 302), AutoSize = true, ForeColor = Color.FromArgb(3, 105, 161), Font = new Font("微軟正黑體", 12f, FontStyle.Bold) };

            // -------------------------------------------------------------
            // 第八列 (Y = 338)：DUTY 基準過電流跳脫保護控制項 (S1/S2/S6 通用)
            // -------------------------------------------------------------
            Label lblDutyOcTitle = new Label() { Text = "⚡過電流保護:", Location = new Point(12, 338), AutoSize = true, Font = new Font("微軟正黑體", 12f, FontStyle.Bold), ForeColor = Color.FromArgb(194, 65, 12) };
            Label lblDutyOcPct = new Label() { Text = "門檻:", Location = new Point(135, 340), AutoSize = true, Font = new Font("微軟正黑體", 11.5f, FontStyle.Bold) };
            numDutyOverCurrentPct = CreateNumericUpDown(new Point(180, 336), 60, 5, 100, dutyOverCurrentPercent, 0, 5);
            numDutyOverCurrentPct.Font = new Font("微軟正黑體", 11.5f);
            numDutyOverCurrentPct.ValueChanged += (s, e) => { dutyOverCurrentPercent = numDutyOverCurrentPct.Value; };
            Label lblDutyOcPctUnit = new Label() { Text = "%", Location = new Point(245, 340), AutoSize = true, Font = new Font("微軟正黑體", 11.5f, FontStyle.Bold) };

            Label lblDutyOcDelay = new Label() { Text = "持續:", Location = new Point(270, 340), AutoSize = true, Font = new Font("微軟正黑體", 11.5f, FontStyle.Bold) };
            numDutyOverCurrentDelay = CreateNumericUpDown(new Point(315, 336), 60, 1, 60, dutyOverCurrentDelaySec, 0, 1);
            numDutyOverCurrentDelay.Font = new Font("微軟正黑體", 11.5f);
            numDutyOverCurrentDelay.ValueChanged += (s, e) => { dutyOverCurrentDelaySec = numDutyOverCurrentDelay.Value; };
            Label lblDutyOcDelayUnit = new Label() { Text = "s", Location = new Point(380, 340), AutoSize = true, Font = new Font("微軟正黑體", 11.5f, FontStyle.Bold) };

            lblDutyCurrentBaseline = new Label() { Text = "基準: 尚未確立 (採樣中)", Location = new Point(410, 338), AutoSize = true, Font = new Font("微軟正黑體", 11.5f, FontStyle.Bold), ForeColor = Color.FromArgb(71, 85, 105) };

            // -------------------------------------------------------------
            // 第九列 (Y = 380)：操作按鈕群與進度條
            // -------------------------------------------------------------
            btnStartDuty = new Button() { Text = "▶ 開始工作制測試", Location = new Point(12, 376), Size = new Size(175, 40), BackColor = Color.FromArgb(0, 180, 216), ForeColor = Color.White, Font = new Font("微軟正黑體", 13.5f, FontStyle.Bold) };
            btnStopDuty = new Button() { Text = "⏹ 停止", Location = new Point(195, 376), Size = new Size(95, 40), Enabled = false, Font = new Font("微軟正黑體", 13.5f, FontStyle.Bold) };
            btnExportDuty = new Button() { Text = "💾 匯出報表", Location = new Point(298, 376), Size = new Size(125, 40), BackColor = Color.FromArgb(16, 185, 129), ForeColor = Color.White, Font = new Font("微軟正黑體", 13.5f, FontStyle.Bold) };
            prgDuty = new ProgressBar() { Location = new Point(435, 383), Size = new Size(280, 26) };

            btnStartDuty.Click += BtnStartDuty_Click;
            btnStopDuty.Click += (s, e) => { StopDutyTest(); };
            btnExportDuty.Click += BtnExportDuty_Click;

            // -------------------------------------------------------------
            // 第十列 (Y = 430)：已運轉狀態行 (獨立一行不擠壓)
            // -------------------------------------------------------------
            lblDutyStatus = new Label() { Text = "狀態: 待命準備中", Location = new Point(12, 430), AutoSize = true, Font = new Font("微軟正黑體", 13f, FontStyle.Bold), ForeColor = Color.FromArgb(15, 23, 42) };

            // -------------------------------------------------------------
            // 第十一列 (Y = 462)：動作說明行 (獨立一行)
            // -------------------------------------------------------------
            lblDutyPhaseAction = new Label() { Text = "動作: --", Location = new Point(12, 462), AutoSize = true, ForeColor = Color.FromArgb(2, 132, 199), Font = new Font("微軟正黑體", 12f, FontStyle.Bold) };

            // -------------------------------------------------------------
            // 第十二列 (Y = 494)：熱平衡判定行 (獨立一行醒目顯示)
            // -------------------------------------------------------------
            lblThermalStatus = new Label() { Text = "熱平衡: 未達平衡", Location = new Point(12, 494), AutoSize = true, ForeColor = Color.DarkOrange, Font = new Font("微軟正黑體", 12.5f, FontStyle.Bold) };

            grp.Controls.AddRange(new Control[] {
                lblKebAutoModeHint,
                lMode, cmbDutyMode, lRole, cmbDutyRole,
                lSpd, numDutySpeed, lTrq, numDutyTorque,
                chkS1ThermalStop, btnS1SelectChannels, lblS1SelectedChHint, lblDutyAbStatus,
                lblS2Duration, numS2DurationMin, chkS2TempStop, lblS2TempCh, cmbS2TempCh, lblS2TempThresh, numS2TempThreshold,
                lblS2AnchorTitle, lblS2AnchorSy52, numS2AnchorSy52, lblS2AnchorCs18, numS2AnchorCs18, btnS2RecordAnchor, btnS2ResetAnchor,
                lblS6CycleLabel, numS6CycleMin, lblS6EdLabel, numS6Ed, lblS6CyclesLabel, numS6Cycles, lblS6CalcInfo,
                lblS6TempCh, cmbS6TempCh, lblS6TempChHint,
                lblS6NoLoadTitle, numS6AnchorNoLoadSpd, btnS6AnchorNoLoad, lblS6LoadedTitle, numS6AnchorLoadedSpd, lblS6Cs18Title, numS6AnchorLoadedCs18, btnS6AnchorLoaded, btnS6ResetAnchor, lblS6AnchorStatus,
                lblDutyOcTitle, lblDutyOcPct, numDutyOverCurrentPct, lblDutyOcPctUnit, lblDutyOcDelay, numDutyOverCurrentDelay, lblDutyOcDelayUnit, lblDutyCurrentBaseline,
                btnStartDuty, btnStopDuty, btnExportDuty, prgDuty,
                lblDutyStatus, lblDutyPhaseAction, lblThermalStatus
            });
            splitDutyTop.Panel1.Controls.Add(grp);

            // -------------------------------------------------------------
            // 右側：專屬溫度監控與即時動態波形 (S1/S2/S6 模式自適應，含 S6 熱平衡與兩段超溫監控)
            // -------------------------------------------------------------
            grpDutyTemp = new GroupBox()
            {
                Text = "🌡️ 專屬溫度監控與即時動態波形",
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
            pnlTempHeader.Controls.AddRange(new Control[] { lblDutyTempTrendTitle, lblDutyTempRealtimeVal, lblS6ThermalStatus, lblDutyAllChTempsDisp });

            // 專屬動態溫度曲線：與 TN / 空載 共用 sharedTestTempTrend，當使用者切換至此分頁時由 AttachSharedTempTrendTo 動態掛載

            // 底部 S6 兩段式超溫防護列
            pnlS6Overtemp = new Panel() { Dock = DockStyle.Bottom, Height = 48, BackColor = Color.FromArgb(254, 242, 242), Padding = new Padding(4) };
            Label lWarn = new Label() { Text = "⚠️ 警告門檻:", Location = new Point(6, 13), AutoSize = true, Font = new Font("微軟正黑體", 11f, FontStyle.Bold), ForeColor = Color.DarkOrange };
            numS6WarnTemp = CreateNumericUpDown(new Point(105, 9), 65, 30, 200, 90, 0, 1);
            numS6WarnTemp.Font = new Font("微軟正黑體", 11f, FontStyle.Bold);

            Label lTrip = new Label() { Text = "🛑 停機門檻:", Location = new Point(175, 13), AutoSize = true, Font = new Font("微軟正黑體", 11f, FontStyle.Bold), ForeColor = Color.FromArgb(220, 38, 38) };
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
            dgvDuty.ColumnHeadersDefaultCellStyle.Font = new Font("微軟正黑體", 12f, FontStyle.Bold);
            dgvDuty.DefaultCellStyle.Font = new Font("微軟正黑體", 11.5f);
            dgvDuty.Columns.Add("Time", "時間 (分:秒)");
            dgvDuty.Columns.Add("Phase", "階段");
            dgvDuty.Columns.Add("Speed", "轉速 (rpm)");
            dgvDuty.Columns.Add("Torque", "轉矩 (Nm)");
            dgvDuty.Columns.Add("Power", "功率 (kW)");
            dgvDuty.Columns.Add("Eff", "效率 (%)");
            dgvDuty.Columns.Add("Temp", "馬達溫度 (°C)");
            dgvDuty.Columns.Add("Thermal", "熱平衡判定");

            splitDuty.Panel2.Controls.Add(dgvDuty);
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
                Text = "S1 熱平衡多通道選取 (全選取通道 30min 溫差皆 < 1.0°C 才停機)",
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
                Text = "請勾選需參與熱平衡判定 (30分鐘溫差<1.0°C) 與即時監控之通道：\n(若設備已連線，可點擊「⚡ 依實測選取」自動偵測真正有訊號的通道)",
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

        private void UpdateDutyModeVisibility(int modeIdx)
        {
            bool isS1 = (modeIdx == 0);
            bool isS2 = (modeIdx == 1);
            bool isS6 = (modeIdx == 2);

            // 1. S1 專屬元件顯隱
            if (chkS1ThermalStop != null) chkS1ThermalStop.Visible = isS1;
            if (btnS1SelectChannels != null) btnS1SelectChannels.Visible = isS1;
            if (lblS1SelectedChHint != null) lblS1SelectedChHint.Visible = isS1;
            if (lblS1ThermalStatus != null) lblS1ThermalStatus.Visible = isS1;
            if (lblDutyAbStatus != null) lblDutyAbStatus.Visible = (isS1 || isS2 || isS6);
            if (lblDutyAllChTempsDisp != null) lblDutyAllChTempsDisp.Visible = isS1;

            // 2. S2 專屬元件顯隱 (含 S2 錨點測試控制項)
            if (lblS2Duration != null) lblS2Duration.Visible = isS2;
            if (numS2DurationMin != null) numS2DurationMin.Visible = isS2;
            if (chkS2TempStop != null) chkS2TempStop.Visible = isS2;
            if (lblS2TempCh != null) lblS2TempCh.Visible = isS2;
            if (cmbS2TempCh != null) cmbS2TempCh.Visible = isS2;
            if (lblS2TempThresh != null) lblS2TempThresh.Visible = isS2;
            if (numS2TempThreshold != null) numS2TempThreshold.Visible = isS2;
            if (lblS2TempRealtime != null) lblS2TempRealtime.Visible = isS2;

            if (lblS2AnchorTitle != null) lblS2AnchorTitle.Visible = isS2;
            if (lblS2AnchorSy52 != null) lblS2AnchorSy52.Visible = isS2;
            if (numS2AnchorSy52 != null) numS2AnchorSy52.Visible = isS2;
            if (lblS2AnchorCs18 != null) lblS2AnchorCs18.Visible = isS2;
            if (numS2AnchorCs18 != null) numS2AnchorCs18.Visible = isS2;
            if (btnS2RecordAnchor != null) btnS2RecordAnchor.Visible = isS2;
            if (btnS2ResetAnchor != null) btnS2ResetAnchor.Visible = isS2;
            if (lblS2AnchorHint != null) lblS2AnchorHint.Visible = isS2;

            // 3. S6 專屬元件顯隱
            if (lblS6CycleLabel != null) lblS6CycleLabel.Visible = isS6;
            if (numS6CycleMin != null) numS6CycleMin.Visible = isS6;
            if (lblS6EdLabel != null) lblS6EdLabel.Visible = isS6;
            if (numS6Ed != null) numS6Ed.Visible = isS6;
            if (lblS6CyclesLabel != null) lblS6CyclesLabel.Visible = isS6;
            if (numS6Cycles != null) numS6Cycles.Visible = isS6;
            if (lblS6TempCh != null) lblS6TempCh.Visible = isS6;
            if (cmbS6TempCh != null) cmbS6TempCh.Visible = isS6;
            if (lblS6TempChHint != null) lblS6TempChHint.Visible = isS6;
            if (lblS6CalcInfo != null) lblS6CalcInfo.Visible = isS6;

            if (lblS6NoLoadTitle != null) lblS6NoLoadTitle.Visible = isS6;
            if (numS6AnchorNoLoadSpd != null) numS6AnchorNoLoadSpd.Visible = isS6;
            if (btnS6AnchorNoLoad != null) btnS6AnchorNoLoad.Visible = isS6;
            if (lblS6LoadedTitle != null) lblS6LoadedTitle.Visible = isS6;
            if (numS6AnchorLoadedSpd != null) numS6AnchorLoadedSpd.Visible = isS6;
            if (lblS6Cs18Title != null) lblS6Cs18Title.Visible = isS6;
            if (numS6AnchorLoadedCs18 != null) numS6AnchorLoadedCs18.Visible = isS6;
            if (btnS6AnchorLoaded != null) btnS6AnchorLoaded.Visible = isS6;
            if (btnS6ResetAnchor != null) btnS6ResetAnchor.Visible = isS6;
            if (lblS6AnchorStatus != null) lblS6AnchorStatus.Visible = isS6;

            // 右側溫度波形指示與 S6 專屬超溫/熱平衡面板顯隱
            if (lblS6ThermalStatus != null) lblS6ThermalStatus.Visible = isS6;
            if (pnlS6Overtemp != null) pnlS6Overtemp.Visible = isS6;

            if (lblDutyTempTrendTitle != null)
            {
                if (isS1)
                {
                    lblDutyTempTrendTitle.Text = "目前監控: S1 多通道最高溫 (熱平衡判定: 30min溫差<1.0℃)";
                }
                else if (isS2)
                {
                    int ch = (cmbS2TempCh != null && cmbS2TempCh.SelectedIndex >= 0) ? cmbS2TempCh.SelectedIndex + 1 : 1;
                    lblDutyTempTrendTitle.Text = string.Format("目前監控: S2 通道 {0} (純趨勢波形，不判定熱平衡)", ch);
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
            if (dutyTimer != null) dutyTimer.Stop();
            if (isManualRecording)
            {
                StopManualRecording(showPrompt: false);
            }
            isS2Calibrating = false;
            isLoadMotorPreEnergized = false;
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
                if (btnStartDuty != null) btnStartDuty.Enabled = true;
                if (btnStopDuty != null) btnStopDuty.Enabled = false;
                if (btnDutyMiniStart != null) btnDutyMiniStart.Enabled = true;
                if (btnDutyMiniStop != null) btnDutyMiniStop.Enabled = false;
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
            // 安全檢查：全自動控制要求雙機 100% 同時連線
            if (!isHmiKebOpen1 || !isHmiKebOpen2)
            {
                MessageBox.Show(
                    "【🚨 全自動控制安全拒絕】\n\n" +
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
            s6CurrentCyclePeakTemp = -999.0;
            s6ThermalBalanced = false;
            if (lblS6ThermalStatus != null)
            {
                lblS6ThermalStatus.Text = "S6 熱平衡: 監測中 (需連續 3 週期，達 30 分鐘峰值溫差 <= 1.0℃ 自動停機)";
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
            if (dgvDuty != null) dgvDuty.Rows.Clear();

            bool isFromMini = (sender == btnDutyMiniStart);
            SyncAllDutyControls(fromMiniToMain: isFromMini);

            int dutyRole = (cmbDutyRole != null && cmbDutyRole.SelectedIndex >= 0) ? cmbDutyRole.SelectedIndex : 1;
            int spdDrive = (dutyRole == 1) ? 2 : 1;
            int trqDrive = (spdDrive == 1) ? 2 : 1;

            int spdCom = GetHmiKebComIdx(spdDrive), spdBaud = GetHmiKebBaudIdx(spdDrive), spdNode = (spdDrive == 1) ? (int)numHmiKebNode1.Value : (int)numHmiKebNode2.Value;
            int trqCom = GetHmiKebComIdx(trqDrive), trqBaud = GetHmiKebBaudIdx(trqDrive), trqNode = (trqDrive == 1) ? (int)numHmiKebNode1.Value : (int)numHmiKebNode2.Value;

            string spdDriveName = (spdDrive == 1) ? "A載台" : "B載台";
            string trqDriveName = (trqDrive == 1) ? "A載台" : "B載台";

            // ★【啟動前變頻器狀態與故障防護校驗】：
            // 1. 檢查硬體 ST 安全端子 (ru.00 == 0 代表 Control Release 未閉合 / nOP)
            // 2. 檢查變頻器真實硬體故障碼 (ru.43 != 0 代表硬體報警)
            int? curRuSpd = KebReadParamWithDll(spdCom, spdBaud, spdNode, 0x0200);
            int? curRuTrq = KebReadParamWithDll(trqCom, trqBaud, trqNode, 0x0200);
            int? curFaultSpd = KebReadParamWithDll(spdCom, spdBaud, spdNode, 0x022B);
            int? curFaultTrq = KebReadParamWithDll(trqCom, trqBaud, trqNode, 0x022B);

            if (curRuSpd.HasValue && curRuSpd.Value == 0)
            {
                MessageBox.Show(string.Format("【🚨 硬體ST端子未閉合】\n\n{0} 目前處於 0: nOP (Control Release 斷開)！\n請先閉合機櫃實體 ST 端子開關以放行功率級。", spdDriveName),
                    "硬體安全端子未閉合", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            if (curRuTrq.HasValue && curRuTrq.Value == 0)
            {
                MessageBox.Show(string.Format("【🚨 硬體ST端子未閉合】\n\n{0} 目前處於 0: nOP (Control Release 斷開)！\n請先閉合機櫃實體 ST 端子開關以放行功率級。", trqDriveName),
                    "硬體安全端子未閉合", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            // 若任一側處於真實故障狀態 (ru.43 != 0)，嘗試自動發送 FAULT RESET (Sy.50=2 ➔ Sy.50=0)
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
                string faultMsg = (curFaultSpd.HasValue && curFaultSpd.Value != 0)
                    ? string.Format("{0} 故障未除 (ru.43={1}: {2})", spdDriveName, curFaultSpd.Value, DecodeKebFaultCode(curFaultSpd.Value))
                    : string.Format("{0} 故障未除 (ru.43={1}: {2})", trqDriveName, curFaultTrq.Value, DecodeKebFaultCode(curFaultTrq.Value));
                WriteHmiLog("DUTY_ABORT", string.Format("【啟動拒絕】{0}，自動復歸無效，終止啟動！", faultMsg));
                MessageBox.Show(
                    string.Format("【🚨 變頻器故障拒絕啟動】\n\n檢測到 {0}！\n系統已嘗試自動復歸但故障無法排除。\n請至變頻器檢查故障碼並排除硬體異常後再試。", faultMsg),
                    "變頻器故障拒絕啟動",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error
                );
                return;
            }

            // 啟動前刷新扭力計心跳時間，防止啟動瞬間因零速無封包觸發保護
            lastTorquePacketTime = DateTime.Now;

            // ★ 強制自動將雙機切換為全自動控制模式 (待測端 Mode 9 / 加載端 Mode 10)
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

            // ★ 自動開啟 RAW DATA 記錄 (檔名後綴: _S1, _S2, _S6)
            StartAutoRawRecordingWithTag(dutyTag);

            if (s1TempHistory != null) s1TempHistory.Clear();

            double targetSpd = (numDutySpeed != null && numDutySpeed.Value > 0) ? (double)numDutySpeed.Value : 1000.0;
            double targetTrq = (numDutyTorque != null && numDutyTorque.Value > 0) ? (double)numDutyTorque.Value : 15.0;

            if (dutyModeIdx == 0) // S1 連續工作制 (最長可運轉 20 小時，或依溫度 30 分鐘溫差 < 1.0℃ 自動停機)
            {
                dutyTotalSec = 72000;
                s1LoadTracker.Reset(targetSpd, 0.0);
                s6CurrentSpeedCmd = targetSpd;
                s6AdaptedTorquePct = 0.0;
            }
            else if (dutyModeIdx == 1) // S2 短時工作制 (含錨點測試機制)
            {
                int curSy52 = (numS2AnchorSy52 != null) ? (int)numS2AnchorSy52.Value : s2AnchorSy52;
                int curCs18 = (numS2AnchorCs18 != null) ? (int)numS2AnchorCs18.Value : s2AnchorCs18;

                if (curSy52 == 0 || curCs18 == 0)
                {
                    // 錨點數值為 0 ➔ 執行錨點偵測測試 (採用統一自適應加速定錨引擎)
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
                    // 錨點數值 > 0 ➔ 直接套用記錄/輸入之錨點運轉！
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

            // 發送指令
            KebWriteParam32(spdCom, spdBaud, spdNode, 0x0034, (int)s6CurrentSpeedCmd, "待測端目標轉速 SY52");
            KebWriteParamWithDll(spdCom, spdBaud, spdNode, 0x0F12, 1000); // 放行 100% 轉矩
            SetHmiKebCommand(spdCom, spdBaud, spdNode, 4, "待測端啟轉運轉 (Sy50=4)");

            // ★ 啟轉後短暫等待並校驗變頻器真實故障碼 ru.43 (0x022B)，若硬體跳脫則中止
            Thread.Sleep(80);
            int? postRunFault = KebReadParamWithDll(spdCom, spdBaud, spdNode, 0x022B);
            if (postRunFault.HasValue && postRunFault.Value != 0)
            {
                SetHmiKebCommand(spdCom, spdBaud, spdNode, 0, "啟轉故障停機");
                WriteHmiLog("DUTY_ABORT", string.Format("【🚨 啟轉失敗】{0} 啟轉後突發變頻器硬體故障 (ru.43={1}: {2})，已強制停機並終止測試！",
                    spdDriveName, postRunFault.Value, DecodeKebFaultCode(postRunFault.Value)));
                MessageBox.Show(
                    string.Format("【🚨 變頻器啟轉故障】\n\n{0} 下達運轉指令後檢測到故障報警 (ru.43={1}: {2})！\n測試已自動中止並強制停機。",
                        spdDriveName, postRunFault.Value, DecodeKebFaultCode(postRunFault.Value)),
                    "變頻器啟轉異常",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error
                );
                StopDutyTest();
                return;
            }

            // ★ 方案 B：加載端初始保持 0 轉矩待命 (Sy50=0，不於零速激磁耗能發熱)
            // 待測端轉速達到低速門檻 (>= 60 rpm) 時，再於背景自動上電激磁 (Sy50=4, CS18=0) 熱備妥
            isLoadMotorPreEnergized = false;
            KebWriteParamWithDll(trqCom, trqBaud, trqNode, 0x0F12, 0);
            SetHmiKebCommand(trqCom, trqBaud, trqNode, 0, "加載端待命 (Sy50=0, CS18=0)");

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

        private void DutyTimer_Tick(object sender, EventArgs e)
        {
            int dutyRole = (cmbDutyMiniRole != null && cmbDutyMiniRole.SelectedIndex >= 0)
                ? cmbDutyMiniRole.SelectedIndex
                : ((cmbDutyRole != null && cmbDutyRole.SelectedIndex >= 0) ? cmbDutyRole.SelectedIndex : 1);
            int spdDrive = (dutyRole == 1) ? 2 : 1;
            int trqDrive = (spdDrive == 1) ? 2 : 1;
            int spdCom = GetHmiKebComIdx(spdDrive), spdBaud = GetHmiKebBaudIdx(spdDrive), spdNode = (spdDrive == 1) ? (int)numHmiKebNode1.Value : (int)numHmiKebNode2.Value;
            int trqCom = GetHmiKebComIdx(trqDrive), trqBaud = GetHmiKebBaudIdx(trqDrive), trqNode = (trqDrive == 1) ? (int)numHmiKebNode1.Value : (int)numHmiKebNode2.Value;

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

            // ★【DUTY 變頻器硬體故障保護守護】：檢測任一載台是否處於真實硬體故障 (ru.43 != 0)
            if (lastKebFaultCode1 != 0 || lastKebFaultCode2 != 0)
            {
                int faultDrive = (lastKebFaultCode1 != 0) ? 1 : 2;
                int faultCode = (faultDrive == 1) ? lastKebFaultCode1 : lastKebFaultCode2;
                string faultDriveName = (faultDrive == 1) ? "A載台" : "B載台";
                WriteHmiLog("DUTY_ABORT", string.Format("【🚨 DUTY 測試異常中斷】檢測到 {0} 突發硬體故障報警 (ru.43={1}: {2})，已強制安全卸載停機！",
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

            // ★ 方案 B：加載端低速激磁預備 (Zero-Torque Hot-Standby)
            // 當待測端轉速達到低速門檻 (>= 60 rpm) 且加載端尚未上電時，自動執行 Sy50=4 但保持 CS18=0 (無故障即可)
            if (!isLoadMotorPreEnergized && actAbsSpd >= 60.0 && lastKebFaultCode1 == 0 && lastKebFaultCode2 == 0)
            {
                isLoadMotorPreEnergized = true;
                KebWriteParamWithDll(trqCom, trqBaud, trqNode, 0x0F12, 0); // 強制 CS18 = 0
                SetHmiKebCommand(trqCom, trqBaud, trqNode, 4, "加載端低速激磁預備 (Sy50=4, CS18=0)");
                WriteHmiLog("DUTY_STAGE", string.Format("【低速激磁預備 (方案B)】待測端轉速已達 {0:F0} rpm，加載端預先上電激磁 (Sy50=4, CS18=0) 平滑跟隨，徹底消除高速反轉矩衝擊！", actAbsSpd));
            }

            // ★【DUTY 基準電流採樣與全域過電流保護】
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

                    lblDutyPhaseAction.Text = string.Format("⚠️【電流過載警示】{0} (超限 {1:F0}/{2:F0}s)", tripDetail, overSec, reqDelay);
                    if (lblDutyMiniPhaseAction != null) lblDutyMiniPhaseAction.Text = lblDutyPhaseAction.Text;

                    if (overSec >= reqDelay)
                    {
                        dutyTimer.Stop();
                        if (isManualRecording) StopManualRecording(showPrompt: false);

                        string tripMsg = string.Format(
                            "【🚨 DUTY 過電流跳脫保護】\n\n" +
                            "實測電流超出基準負載 {0}% 連續達 {1:F0} 秒！\n" +
                            "觸發詳情：{2}\n\n" +
                            "系統已立即啟動平滑降載停機程序保護待測設備！",
                            (int)dutyOverCurrentPercent, reqDelay, tripDetail);
                        WriteHmiLog("OVER_CURRENT_TRIP", tripMsg);

                        StartGradualAutoStop(spdDrive, trqDrive, "DUTY 過電流保護跳脫", () => {
                            btnStartDuty.Enabled = true;
                            btnStopDuty.Enabled = false;
                            if (btnDutyMiniStart != null) btnDutyMiniStart.Enabled = true;
                            if (btnDutyMiniStop != null) btnDutyMiniStop.Enabled = false;
                            lblDutyStatus.Text = "【🚨 過電流保護跳脫】已安全停機！";
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
                    // 子階段 1：空載 10 秒穩定確認 ➔ 確定並記錄 [空載轉速錨點]
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
                            WriteHmiLog("S6_ERROR", string.Format("【🚨 S6 加載定錨超時報警】已調節 90 秒但轉矩仍無法達到目標 ({0:F1}/{1:F1} Nm)！請檢查負載側供電、制動器或目標值設定！", actAbsTrq, targetTrq));
                            StopDutyTest();
                            MessageBox.Show(
                                string.Format("【🚨 S6 加載定錨超時】\n\n系統加載調節已達 90 秒，但轉矩實測 ({0:F1} Nm) 仍未達目標 ({1:F1} Nm)！\n\n系統已安全停機保護，請確認負載端出力能力與設定值。", actAbsTrq, targetTrq),
                                "S6 定錨未達標警告",
                                MessageBoxButtons.OK,
                                MessageBoxIcon.Warning
                            );
                            return;
                        }
                    }
                    // 子階段 3：加載 10 秒穩定確認 ➔ 確定並記錄 [加載轉速錨點] 與 [加載轉矩錨點] (★ 嚴禁未達標強制倒數)
                    else if (s6TrialStage == 3)
                    {
                        currentPhaseName = "加載定錨倒數";
                        s6TrialStageElapsedSec++;

                        double trqErr = targetTrq - actAbsTrq;
                        double spdSlip = targetSpd - actAbsSpd;

                        // ★ 嚴格判定轉矩與轉速是否雙雙在合格容許帶內 (轉矩 ±5% 或 ±1.5Nm, 轉速 ±5% 或 ±15rpm)
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
                            // ★ 鐵律：一旦轉矩或轉速偏離容許帶，倒數計時器立即重置回 10 秒！絕不允許累積拼湊！
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

                        // ★ 唯有 s6TrialTimer <= 0（真正連續 10 秒在目標容許帶內），才宣告定錨完成！
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
                            WriteHmiLog("S6_ERROR", string.Format("【🚨 S6 穩定確認超時】加載已達目標附近但波動過大，無法在 60 秒內連續穩定 10 秒 (實測 {0:F1}/{1:F1} Nm)！", actAbsTrq, targetTrq));
                            StopDutyTest();
                            MessageBox.Show(
                                string.Format("【🚨 S6 穩定確認超時】\n\n加載已接近目標 ({0:F1}/{1:F1} Nm)，但系統波動過大，60 秒內無法連續穩定 10 秒！\n\n系統已安全停機保護，避免記錄不穩定之偽錨點。", actAbsTrq, targetTrq),
                                "S6 穩定度未達標警告",
                                MessageBoxButtons.OK,
                                MessageBoxIcon.Warning
                            );
                            return;
                        }
                    }
                    // 子階段 4：維持試運轉完整的 S6 設定 DUTY 時間 T1 ➔ 先減速回 [空載轉速錨點] ➔ 卸載
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

                        // ★ T1 即將結束前 2 秒：先減速回 [空載轉速錨點]！
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

                        string remT1Text = string.Format("【自適應試運轉】🔥 T1 有載運轉中 (剩餘 {0}s)", Math.Max(0, t1Sec - s6CycleElapsedSec));
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

                        string remT2Text = string.Format("【自適應試運轉】❄️ T2 空載自冷中 (剩餘 {0}s)", Math.Max(0, t2Sec - s6CycleElapsedSec));
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

                    // 追蹤當前週期之最高溫度點
                    if (dutyMonitoredTemp > s6CurrentCyclePeakTemp)
                        s6CurrentCyclePeakTemp = dutyMonitoredTemp;

                    // ★ S6 兩段式超溫防護監控 (警告 / 停機)
                    double warnThresh = (numS6WarnTemp != null) ? (double)numS6WarnTemp.Value : 90.0;
                    double tripThresh = (numS6TripTemp != null) ? (double)numS6TripTemp.Value : 105.0;

                    if (dutyMonitoredTemp >= tripThresh)
                    {
                        dutyTimer.Stop();
                        if (isManualRecording)
                        {
                            StopManualRecording(showPrompt: false);
                        }
                        WriteHmiLog("S6_OVERTEMP", string.Format("【🛑 S6 嚴重超溫保護】監控溫度達 {0:F1} ℃ >= 停機門檻 {1:F1} ℃，啟動安全停機！", dutyMonitoredTemp, tripThresh));
                        StartGradualAutoStop(spdDrive, trqDrive, "S6 嚴重超溫停機保護", () => {
                            btnStartDuty.Enabled = true;
                            btnStopDuty.Enabled = false;
                            if (btnDutyMiniStart != null) btnDutyMiniStart.Enabled = true;
                            if (btnDutyMiniStop != null) btnDutyMiniStop.Enabled = false;
                            lblDutyStatus.Text = string.Format("【🛑 嚴重超溫停機】實測 {0:F1} ℃ >= 停機門檻 {1:F1} ℃！", dutyMonitoredTemp, tripThresh);
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

                            // 2. ★【待測端轉速平滑閉迴路追隨 (同動 LOCK 設定)】(T1 結束前2秒降速階段不追隨)
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

                        // ★ T1 結束前 2 秒：先減速回 [空載轉速錨點]！
                        if (s6CycleElapsedSec >= t1Sec - 2 && s6CycleElapsedSec < t1Sec)
                        {
                            s6CurrentSpeedCmd = s6AnchorNoLoadSpeed;
                            KebWriteParam32(spdCom, spdBaud, spdNode, 0x0034, (int)s6CurrentSpeedCmd, "S6 先降回空載轉速");
                            if (spdDrive == 2 && numHmiKebSpeed2 != null) numHmiKebSpeed2.Value = (decimal)s6CurrentSpeedCmd;
                            else if (spdDrive == 1 && numHmiKebSpeed1 != null) numHmiKebSpeed1.Value = (decimal)s6CurrentSpeedCmd;
                        }

                        string formalT1Text = string.Format("[第 {0}/{1} 週期] 🔥 T1 有載運轉中 (剩餘 {2}s)", s6FormalCycleIndex, totalFormalCycles, t1Sec - s6CycleElapsedSec);
                        lblDutyStatus.Text = formalT1Text;
                        lblDutyPhaseAction.Text = string.Format("【T1 有載】實測 {0:F1} Nm / 目標 {1:F1} Nm (給定 {2:F1}%)", actAbsTrq, targetTrq, s6AdaptedTorquePct);
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
                            WriteHmiLog("S6_STAGE", string.Format("[第 {0}/{1} 週期] T1 完成，加載端卸載歸零，進入 T2 空載自冷！", s6FormalCycleIndex, totalFormalCycles));
                        }

                        string formalT2Text = string.Format("[第 {0}/{1} 週期] ❄️ T2 空載自冷中 (剩餘 {2}s)", s6FormalCycleIndex, totalFormalCycles, totalCycleSec - s6CycleElapsedSec);
                        lblDutyStatus.Text = formalT2Text;
                        lblDutyPhaseAction.Text = string.Format("【T2 空載】加載端歸零 0.0 Nm，轉速 {0:F0} rpm 自冷中...", actAbsSpd);
                        if (lblDutyMiniStatus != null) lblDutyMiniStatus.Text = formalT2Text;
                        if (lblDutyMiniPhaseAction != null) lblDutyMiniPhaseAction.Text = lblDutyPhaseAction.Text;

                        s6CycleElapsedSec++;
                    }
                    // 【單一正式週期結束交替】
                    else if (s6CycleElapsedSec >= totalCycleSec)
                    {
                        // ★【S6 熱平衡分析：結算當前週期最高溫點】
                        if (s6CurrentCyclePeakTemp > -100.0)
                        {
                            s6PeakTempHistory.Add(s6CurrentCyclePeakTemp);
                            WriteHmiLog("S6_PEAK_TEMP", string.Format("【S6 週期結算】第 {0} 週期最高溫為: {1:F2} ℃ (已累積 {2} 個週期高溫點)",
                                s6FormalCycleIndex, s6CurrentCyclePeakTemp, s6PeakTempHistory.Count));
                        }
                        s6CurrentCyclePeakTemp = -999.0;

                        // 判定是否達成熱平衡 (至少需連續 3 個週期，即 30 分鐘，連續兩段溫差均 <= 1.0℃)
                        if (!s6ThermalBalanced && s6PeakTempHistory.Count >= 3)
                        {
                            int n = s6PeakTempHistory.Count;
                            double p1 = s6PeakTempHistory[n - 3];
                            double p2 = s6PeakTempHistory[n - 2];
                            double p3 = s6PeakTempHistory[n - 1];

                            double diff1 = Math.Abs(p2 - p1);
                            double diff2 = Math.Abs(p3 - p2);

                            if (diff1 <= 1.0 && diff2 <= 1.0)
                            {
                                s6ThermalBalanced = true;
                                if (lblS6ThermalStatus != null)
                                {
                                    lblS6ThermalStatus.Text = string.Format("✅ S6 熱平衡已達成 (連續3週期峰值差: {0:F1}℃, {1:F1}℃ <= 1.0℃)", diff1, diff2);
                                    lblS6ThermalStatus.ForeColor = Color.Green;
                                }
                                WriteHmiLog("S6_THERMAL_BALANCED", string.Format("【S6 達成熱平衡自動停機】第 {0} 週期達成熱平衡！最近 3 週期最高溫分別為 {1:F2}, {2:F2}, {3:F2} ℃ (連續 30 分鐘峰值溫差 <= 1.0℃，觸發自動平緩停機)",
                                    s6FormalCycleIndex, p1, p2, p3));

                                // ★ 使用者明確指令：S6 若達成 30 分鐘峰值溫差小於 1 度，則停止 S6 測試！
                                dutyTimer.Stop();
                                if (isManualRecording)
                                {
                                    StopManualRecording(showPrompt: false);
                                }
                                StartGradualAutoStop(spdDrive, trqDrive, "S6熱平衡達標自動停機(30min峰值溫差<=1.0℃)", () => {
                                    btnStartDuty.Enabled = true;
                                    btnStopDuty.Enabled = false;
                                    if (btnDutyMiniStart != null) btnDutyMiniStart.Enabled = true;
                                    if (btnDutyMiniStop != null) btnDutyMiniStop.Enabled = false;

                                    lblDutyStatus.Text = string.Format("[成功] S6 熱平衡達標 (第{0}週期，30min峰值溫差<=1.0℃)，試驗自動完成！", s6FormalCycleIndex);
                                    lblDutyPhaseAction.Text = "S6 熱平衡達標，已自動平緩卸載並停機，請點擊「匯出報表」儲存數據。";
                                    if (lblDutyMiniStatus != null) lblDutyMiniStatus.Text = lblDutyStatus.Text;
                                    if (lblDutyMiniPhaseAction != null) lblDutyMiniPhaseAction.Text = lblDutyPhaseAction.Text;

                                    MessageBox.Show(string.Format("S6 週期工作制熱平衡已達標！\n\n已運轉週期: 第 {0} 週期\n最近 3 週期(30分鐘)最高溫: {1:F2}℃ -> {2:F2}℃ -> {3:F2}℃\n週期峰值溫差: {4:F1}℃, {5:F1}℃ <= 1.0℃\n\n系統已自動安全平滑卸載並停機，請點擊「匯出報表」儲存測試結果。",
                                        s6FormalCycleIndex, p1, p2, p3, diff1, diff2), "S6 熱平衡達標·試驗完成", MessageBoxButtons.OK, MessageBoxIcon.Information);
                                });
                                return;
                            }
                            else
                            {
                                if (lblS6ThermalStatus != null)
                                {
                                    lblS6ThermalStatus.Text = string.Format("S6 熱平衡: 比對中 (近3週期峰值差: {0:F1}℃, {1:F1}℃ > 1.0℃)", diff1, diff2);
                                    lblS6ThermalStatus.ForeColor = Color.DarkOrange;
                                }
                            }
                        }
                        else if (!s6ThermalBalanced)
                        {
                            if (lblS6ThermalStatus != null)
                            {
                                lblS6ThermalStatus.Text = string.Format("S6 熱平衡: 採樣累積中 ({0}/3 週期，達 30 分鐘峰值溫差 <= 1.0℃ 自動停機)", s6PeakTempHistory.Count);
                                lblS6ThermalStatus.ForeColor = Color.DarkOrange;
                            }
                        }

                        s6FormalCycleIndex++;
                        if (s6FormalCycleIndex > totalFormalCycles) // 全部週期圓滿完成！
                        {
                            dutyTimer.Stop();
                            if (isManualRecording)
                            {
                                StopManualRecording(showPrompt: false);
                            }
                            StartGradualAutoStop(spdDrive, trqDrive, "S6週期試驗全部完成", () => {
                                btnStartDuty.Enabled = true;
                                btnStopDuty.Enabled = false;
                                if (btnDutyMiniStart != null) btnDutyMiniStart.Enabled = true;
                                if (btnDutyMiniStop != null) btnDutyMiniStop.Enabled = false;

                                lblDutyStatus.Text = "[成功] S6 週期負載試驗全部完成！";
                                lblDutyPhaseAction.Text = "全週期試驗完畢，已平緩降載降速停機，請點擊「匯出報表」儲存數據。";
                                if (lblDutyMiniStatus != null) lblDutyMiniStatus.Text = lblDutyStatus.Text;
                                if (lblDutyMiniPhaseAction != null) lblDutyMiniPhaseAction.Text = lblDutyPhaseAction.Text;

                                WriteHmiLog("DUTY_COMPLETE", string.Format("【S6 測試圓滿完成】共完成 {0} 個完整週期！", totalFormalCycles));
                                MessageBox.Show(string.Format("S6 週期負載試驗（共 {0} 週期）已順利完成！\n已先降負載再降速平滑停機，請點擊「匯出報表」儲存測試數據。", totalFormalCycles), "試驗完成", MessageBoxButtons.OK, MessageBoxIcon.Information);
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
                if (dutyElapsedSec <= prgDuty.Maximum) prgDuty.Value = dutyElapsedSec;
                if (prgDutyMini != null && dutyElapsedSec <= prgDutyMini.Maximum) prgDutyMini.Value = dutyElapsedSec;

                currentPhaseName = "S1 連續";

                // 每秒記錄溫度快照至歷史佇列 (保留 45 分鐘供 30 分鐘溫差滑動視窗比對)
                if (s1TempHistory != null)
                {
                    s1TempHistory.Add(new KeyValuePair<DateTime, double[]>(now, chSnapshot));
                    DateTime expireTime = now.AddMinutes(-45);
                    s1TempHistory.RemoveAll(x => x.Key < expireTime);
                }

                // ★【全系統統一核心引擎：SY52 + CS18 雙閉環自適應加速定錨追隨】
                // 前 3 秒 0.1% 試探斜率 -> 依差距與斜率預估開出大步長 (最高 5.0%) 狂飆衝刺 -> SY52 同步補轉差 -> 雙達標後無縫持續加載
                string s1TrackStatus;
                bool s1Converged = ExecuteUnifiedDualTrackingStep(
                    s1LoadTracker, spdDrive, trqDrive,
                    targetSpd, targetTrq, actAbsSpd, actAbsTrq,
                    out s1TrackStatus, "S1");

                s6AdaptedTorquePct = s1LoadTracker.AdaptedTorquePct;
                s6CurrentSpeedCmd = s1LoadTracker.CurrentSpeedCmd;

                int elapsedMins = dutyElapsedSec / 60;
                int elapsedSecs = dutyElapsedSec % 60;
                lblDutyStatus.Text = string.Format("【S1 連續】已運轉: {0:D2}:{1:D2} | 目標轉矩: {2:F1} Nm, 實測: {3:F1} Nm", elapsedMins, elapsedSecs, targetTrq, actAbsTrq);
                lblDutyPhaseAction.Text = string.Format("【恆定加載】加載輸出 {0:F1}%, 實測轉速 {1:F0} rpm", s6AdaptedTorquePct, actAbsSpd);
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

                    if (dutyElapsedSec < 1800 || baselineSample.Value == null)
                    {
                        string warmUpMsg = string.Format("熱平衡判定: 採樣累積中 ({0:D2}:{1:D2}/30:00，滿 30 分鐘後開始溫差比對)", elapsedMins, elapsedSecs);
                        lblThermalStatus.Text = warmUpMsg;
                        lblThermalStatus.ForeColor = Color.DarkOrange;
                        if (lblS1ThermalStatus != null) lblS1ThermalStatus.Text = string.Format("熱平衡: 累積 {0}m / 需 30m 比對", elapsedMins);
                    }
                    else
                    {
                        bool allPass = true;
                        double maxDeltaT = 0.0;
                        int maxCh = -1;
                        List<string> chDetails = new List<string>();

                        for (int ch = 0; ch < 20; ch++)
                        {
                            if (s1MonitoredChannels[ch])
                            {
                                double tNow = chSnapshot[ch];
                                double tOld = baselineSample.Value[ch];
                                double diff = Math.Abs(tNow - tOld);
                                if (diff > maxDeltaT)
                                {
                                    maxDeltaT = diff;
                                    maxCh = ch;
                                }
                                chDetails.Add(string.Format("CH{0}:Δ{1:F1}℃", ch + 1, diff));
                                if (diff >= 1.0)
                                {
                                    allPass = false;
                                }
                            }
                        }

                        string thermalSummary = string.Format("熱平衡判定: 最大ΔT = {0:F2}℃ (CH{1}) | 判定: {2}",
                            maxDeltaT, maxCh + 1, allPass ? "★ 全通道已達標 (<1.0℃)" : "升溫中 (>=1.0℃)");
                        lblThermalStatus.Text = thermalSummary;
                        lblThermalStatus.ForeColor = allPass ? Color.SeaGreen : Color.DarkOrange;
                        if (lblS1ThermalStatus != null)
                            lblS1ThermalStatus.Text = string.Format("熱平衡: 最大ΔT={0:F1}℃ (CH{1}) {2}", maxDeltaT, maxCh + 1, allPass ? "★達標" : "比對中");

                        if (allPass)
                        {
                            dutyTimer.Stop();
                            if (isManualRecording)
                            {
                                StopManualRecording(showPrompt: false);
                            }
                            string chStr = string.Join(", ", chDetails);
                            StartGradualAutoStop(spdDrive, trqDrive, "S1熱平衡達標(30min溫差<1.0℃)", () => {
                                btnStartDuty.Enabled = true;
                                btnStopDuty.Enabled = false;
                                if (btnDutyMiniStart != null) btnDutyMiniStart.Enabled = true;
                                if (btnDutyMiniStop != null) btnDutyMiniStop.Enabled = false;

                                lblDutyStatus.Text = "[成功] S1 熱平衡達標 (30min溫差<1.0℃)，試驗自動完成！";
                                lblDutyPhaseAction.Text = "所有監控通道 30 分鐘溫差均小於 1℃，已平穩降載降速停機。";
                                if (lblDutyMiniStatus != null) lblDutyMiniStatus.Text = lblDutyStatus.Text;
                                if (lblDutyMiniPhaseAction != null) lblDutyMiniPhaseAction.Text = lblDutyPhaseAction.Text;

                                WriteHmiLog("DUTY_S1_BALANCE", string.Format("【S1 熱平衡達標自動停機】耗時 {0} 分鐘，所有勾選通道 30 分鐘溫差皆 < 1.0℃ ({1})！", elapsedMins, chStr));
                                MessageBox.Show(string.Format("S1 連續工作制熱平衡已達標！\n\n已運轉時間: {0} 分鐘\n各通道30min溫差: {1}\n\n已自動先降負載再降速平滑停機，請點擊「匯出報表」儲存數據。", elapsedMins, chStr), "熱平衡達標·試驗完成", MessageBoxButtons.OK, MessageBoxIcon.Information);
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
                        btnStartDuty.Enabled = true;
                        btnStopDuty.Enabled = false;
                        if (btnDutyMiniStart != null) btnDutyMiniStart.Enabled = true;
                        if (btnDutyMiniStop != null) btnDutyMiniStop.Enabled = false;

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
                    // 【S2 錨點測試流程】到達指定轉速與扭矩後穩定 10 秒 ➔ 記住此錨點 (SY52 / CS18) ➔ 停機
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
                    // 子階段 2：10 秒穩定確認 ➔ 記住錨點並停機
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
                                btnStartDuty.Enabled = true;
                                btnStopDuty.Enabled = false;
                                if (btnDutyMiniStart != null) btnDutyMiniStart.Enabled = true;
                                if (btnDutyMiniStop != null) btnDutyMiniStop.Enabled = false;

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

                    // ★【全系統統一核心引擎：SY52 + CS18 雙閉環微調維持】
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

                    if (chkS2TempStop != null && chkS2TempStop.Checked)
                    {
                        double threshold = (numS2TempThreshold != null) ? (double)numS2TempThreshold.Value : 80.0;
                        if (currentChTemp >= threshold)
                        {
                            dutyTimer.Stop();
                            if (isManualRecording)
                            {
                                StopManualRecording(showPrompt: false);
                            }
                            StartGradualAutoStop(spdDrive, trqDrive, string.Format("S2溫度超限保護 (CH{0}={1:F1}℃ >= {2:F1}℃)", chIdx + 1, currentChTemp, threshold), () => {
                                btnStartDuty.Enabled = true;
                                btnStopDuty.Enabled = false;
                                if (btnDutyMiniStart != null) btnDutyMiniStart.Enabled = true;
                                if (btnDutyMiniStop != null) btnDutyMiniStop.Enabled = false;

                                // ★ 依要求：S2 完成一個執行流程後自動歸零錨點
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
                                MessageBox.Show(string.Format("S2 短時工作制測試觸發溫度保護停機！\n\n監控通道: CH{0}\n實測溫度: {1:F1} ℃\n設定閥值: {2:F1} ℃\n\n已自動平緩降載降速並停止運轉，S2 錨點數據已自動歸零。", chIdx + 1, currentChTemp, threshold), "溫度保護停機", MessageBoxButtons.OK, MessageBoxIcon.Warning);
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
                            btnStartDuty.Enabled = true;
                            btnStopDuty.Enabled = false;
                            if (btnDutyMiniStart != null) btnDutyMiniStart.Enabled = true;
                            if (btnDutyMiniStop != null) btnDutyMiniStop.Enabled = false;

                            // ★ 依要求：S2 完成一個執行流程後自動歸零錨點
                            s2AnchorSy52 = 0;
                            s2AnchorCs18 = 0;
                            if (numS2AnchorSy52 != null) numS2AnchorSy52.Value = 0;
                            if (numS2AnchorCs18 != null) numS2AnchorCs18.Value = 0;

                            lblDutyStatus.Text = "[成功] S2 短時工作制試驗時間到期，圓滿完成！";
                            lblDutyPhaseAction.Text = "已達到設定之短時運轉時間，已平緩降載降速停機，錨點已自動歸零。";
                            if (lblDutyMiniStatus != null) lblDutyMiniStatus.Text = lblDutyStatus.Text;
                            if (lblDutyMiniPhaseAction != null) lblDutyMiniPhaseAction.Text = lblDutyPhaseAction.Text;

                            WriteHmiLog("DUTY_S2_COMPLETE", string.Format("【S2 試驗完成】已達設定測試時間 {0} 分鐘，正常平緩結束停機，錨點數據已歸零！", dutyTotalSec / 60));
                            MessageBox.Show(string.Format("S2 短時工作制試驗（共 {0} 分鐘）已順利完成！\n已平緩煞車停機，S2 錨點數據已自動歸零。\n請點擊「匯出報表」儲存數據。", dutyTotalSec / 60), "試驗完成", MessageBoxButtons.OK, MessageBoxIcon.Information);
                        });
                        return;
                    }
                }
            }

            // 定期採樣記錄 (每 5 秒)
            if (dutyElapsedSec % 5 == 0)
            {
                dutyResults.Add(new string[] {
                    dutyElapsedSec.ToString(), currentPhaseName, actSpeed.ToString("F1"), actTorque.ToString("F2"),
                    actMechPower.ToString("F2"), actEfficiency.ToString("F1"), actTemp.ToString("F1")
                });
            }

            // 每分鐘記錄一行至 dgvDuty (免存檔案，UI 即時監控，自動滾動與記憶體修剪)
            int dutyCurrentMinute = dutyElapsedSec / 60;
            if (dgvDuty != null && dutyElapsedSec > 0 && (dutyElapsedSec % 60 == 0 || dutyElapsedSec == 1) && dutyCurrentMinute != dutyLastMinuteLogged)
            {
                dutyLastMinuteLogged = dutyCurrentMinute;
                string timeStr = string.Format("{0:D2}:{1:D2}", dutyCurrentMinute, dutyElapsedSec % 60);
                double pMech = (actAbsSpd * actAbsTrq) / 9549.0;
                string effStr = (actEfficiency > 0 && actEfficiency <= 100) ? actEfficiency.ToString("F1") : "--";
                string tempStr = string.Format("{0:F1} (CH{1})", dutyMonitoredTemp, maxChIdx + 1);
                string thStr = (dutyModeIdx == 0) ? (lblThermalStatus != null ? lblThermalStatus.Text.Replace("熱平衡判定: ", "") : "--") : (dutyModeIdx == 2 ? (lblS6ThermalStatus != null ? lblS6ThermalStatus.Text : "--") : "--");

                dgvDuty.Rows.Add(timeStr, currentPhaseName, actAbsSpd.ToString("F0"), actAbsTrq.ToString("F1"), pMech.ToString("F2"), effStr, tempStr, thStr);

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

        private void BtnExportDuty_Click(object sender, EventArgs e)
        {
            try
            {
                string logDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs");
                if (!Directory.Exists(logDir)) Directory.CreateDirectory(logDir);
                string path = Path.Combine(logDir, string.Format("Report_Duty_Cycle_{0}.csv", DateTime.Now.ToString("yyyyMMdd_HHmmss")));
                using (StreamWriter sw = new StreamWriter(path, false, Encoding.UTF8))
                {
                    sw.WriteLine("TimeSec,Speed_rpm,Torque_Nm,MechPower_kW,Efficiency_pct,Temp_C");
                    foreach (var row in dutyResults)
                    {
                        sw.WriteLine(string.Join(",", row));
                    }
                }
                PurgeLocalLogs(false);
                MessageBox.Show("工作制報表已匯出至:\n" + path, "匯出成功", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show("匯出失敗: " + ex.Message, "錯誤");
            }
        }
    }
}
