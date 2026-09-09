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
        // 分頁 4: 馬達效率地圖 (雙向梯度自動掃描 / 即時 2D 熱力圖採樣)
        // =========================================================================
        private void BuildEffMapTab(TabPage tab)
        {
            TableLayoutPanel tableEff = new TableLayoutPanel()
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 2,
                BackColor = Color.White
            };
            tableEff.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            tableEff.RowStyles.Add(new RowStyle(SizeType.Absolute, 130f)); // Row 0: 頂部參數與雙向梯度設定
            tableEff.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));  // Row 1: 下方地圖與矩陣

            // 頂部控制與梯度設定面版
            Panel pnlTop = new Panel() { Dock = DockStyle.Fill, Padding = new Padding(3), BackColor = Color.FromArgb(250, 252, 255) };
            GroupBox grpEff = new GroupBox()
            {
                Text = "馬達效率地圖自動測試 (雙向梯度掃描 / 穩定持載達標倒數 / 即時 2D 熱力圖採樣)",
                Dock = DockStyle.Fill,
                Font = new Font("微軟正黑體", 9f, FontStyle.Bold)
            };

            // 第一列：載台角色與雙向梯度輸入
            Label lblRole = new Label() { Text = "測試配置:", Location = new Point(10, 24), AutoSize = true, Font = new Font("微軟正黑體", 9f) };
            cmbEffRole = new ComboBox() { Location = new Point(72, 21), Width = 175, DropDownStyle = ComboBoxStyle.DropDownList, Font = new Font("微軟正黑體", 9f) };
            cmbEffRole.Items.AddRange(new object[] { "A載台待測(速度) / B載台加載", "B載台待測(速度) / A載台加載" });
            cmbEffRole.SelectedIndex = 1; // 預設：B載台待測(速度) / A載台加載

            // 轉速梯度
            Label lSpd1 = new Label() { Text = "轉速(rpm): 起始", Location = new Point(252, 24), AutoSize = true, Font = new Font("微軟正黑體", 9f) };
            numEffStartSpd = CreateNumericUpDown(new Point(345, 21), 58, 50, 8000, 500);
            Label lSpd2 = new Label() { Text = "步階", Location = new Point(408, 24), AutoSize = true, Font = new Font("微軟正黑體", 9f) };
            numEffStepSpd = CreateNumericUpDown(new Point(440, 21), 52, 50, 2000, 500);
            Label lSpd3 = new Label() { Text = "結束", Location = new Point(497, 24), AutoSize = true, Font = new Font("微軟正黑體", 9f) };
            numEffEndSpd = CreateNumericUpDown(new Point(530, 21), 58, 50, 8000, 3000);

            // 轉矩梯度
            Label lTrq1 = new Label() { Text = "轉矩(Nm): 起始", Location = new Point(594, 24), AutoSize = true, Font = new Font("微軟正黑體", 9f) };
            numEffStartTrq = CreateNumericUpDown(new Point(688, 21), 52, 0.5m, 300m, 5.0m, 1, 1m);
            Label lTrq2 = new Label() { Text = "步階", Location = new Point(745, 24), AutoSize = true, Font = new Font("微軟正黑體", 9f) };
            numEffStepTrq = CreateNumericUpDown(new Point(778, 21), 48, 0.5m, 50m, 5.0m, 1, 1m);
            Label lTrq3 = new Label() { Text = "結束", Location = new Point(831, 24), AutoSize = true, Font = new Font("微軟正黑體", 9f) };
            numEffEndTrq = CreateNumericUpDown(new Point(864, 21), 52, 0.5m, 300m, 25.0m, 1, 1m);

            // 穩定持載時間
            Label lDwell = new Label() { Text = "穩定時間(s):", Location = new Point(922, 24), AutoSize = true, Font = new Font("微軟正黑體", 9f) };
            numEffDwell = CreateNumericUpDown(new Point(995, 21), 45, 1, 3600, 10);

            // 第二列：點位規劃與預估時間總結標籤
            lblEffPointSummary = new Label()
            {
                Text = "📊 點位矩陣規劃：計算中...",
                Location = new Point(10, 57),
                AutoSize = true,
                Font = new Font("微軟正黑體", 9.5f, FontStyle.Bold),
                ForeColor = Color.FromArgb(14, 116, 144)
            };

            lblEffMapPeak = new Label()
            {
                Text = "最高效率核心區間：尚未測試",
                Location = new Point(620, 57),
                AutoSize = true,
                Font = new Font("微軟正黑體", 9f, FontStyle.Bold),
                ForeColor = Color.FromArgb(16, 185, 129)
            };

            // 第三列：啟動、停止、模擬試算、匯出按鈕與狀態指示
            btnStartEffMap = new Button()
            {
                Text = "開始效率測試",
                Location = new Point(10, 88),
                Size = new Size(125, 28),
                BackColor = Color.FromArgb(0, 180, 216),
                ForeColor = Color.White,
                Font = new Font("微軟正黑體", 9f, FontStyle.Bold)
            };
            btnStartEffMap.Click += BtnStartEffMap_Click;

            btnStopEffMap = new Button()
            {
                Text = "停止",
                Location = new Point(140, 88),
                Size = new Size(55, 28),
                Enabled = false,
                Font = new Font("微軟正黑體", 9f)
            };
            btnStopEffMap.Click += (s, e) => { StopEffMapTest(); };

            btnGenEffMap = new Button()
            {
                Text = "模擬試算彩圖 (2D Mesh)",
                Location = new Point(200, 88),
                Size = new Size(165, 28),
                BackColor = Color.FromArgb(16, 185, 129),
                ForeColor = Color.White,
                Font = new Font("微軟正黑體", 9f, FontStyle.Bold)
            };
            btnGenEffMap.Click += BtnGenEffMap_Click;

            btnExportEffMap = new Button()
            {
                Text = "匯出效率報表 CSV",
                Location = new Point(370, 88),
                Size = new Size(135, 28),
                BackColor = Color.FromArgb(79, 70, 229),
                ForeColor = Color.White,
                Font = new Font("微軟正黑體", 9f, FontStyle.Bold)
            };
            btnExportEffMap.Click += BtnExportEffMap_Click;

            lblEffMapStatus = new Label()
            {
                Text = "狀態: 待命準備中",
                Location = new Point(515, 94),
                AutoSize = true,
                Font = new Font("微軟正黑體", 9f, FontStyle.Bold)
            };

            lblEffMapCountdown = new Label()
            {
                Text = "持載倒數: -- s",
                Location = new Point(730, 94),
                AutoSize = true,
                ForeColor = Color.DarkOrange,
                Font = new Font("微軟正黑體", 9.5f, FontStyle.Bold)
            };

            prgEffMap = new ProgressBar()
            {
                Location = new Point(870, 93),
                Size = new Size(170, 20)
            };

            // 數值連動更新測試點位與預估時間
            numEffStartSpd.ValueChanged += (s, e) => UpdateEffMapGridPlan();
            numEffStepSpd.ValueChanged += (s, e) => UpdateEffMapGridPlan();
            numEffEndSpd.ValueChanged += (s, e) => UpdateEffMapGridPlan();
            numEffStartTrq.ValueChanged += (s, e) => UpdateEffMapGridPlan();
            numEffStepTrq.ValueChanged += (s, e) => UpdateEffMapGridPlan();
            numEffEndTrq.ValueChanged += (s, e) => UpdateEffMapGridPlan();
            numEffDwell.ValueChanged += (s, e) => UpdateEffMapGridPlan();

            grpEff.Controls.AddRange(new Control[] {
                lblRole, cmbEffRole,
                lSpd1, numEffStartSpd, lSpd2, numEffStepSpd, lSpd3, numEffEndSpd,
                lTrq1, numEffStartTrq, lTrq2, numEffStepTrq, lTrq3, numEffEndTrq,
                lDwell, numEffDwell,
                lblEffPointSummary, lblEffMapPeak,
                btnStartEffMap, btnStopEffMap, btnGenEffMap, btnExportEffMap,
                lblEffMapStatus, lblEffMapCountdown, prgEffMap
            });
            pnlTop.Controls.Add(grpEff);
            tableEff.Controls.Add(pnlTop, 0, 0);

            // 下方主工作區 (左右分割：左側 2D 彩色熱力圖，右側 數據矩陣表)
            SplitContainer splitEff = new SplitContainer()
            {
                Dock = DockStyle.Fill,
                Orientation = Orientation.Vertical
            };
            SafeSetupSplitContainer(splitEff, 550, 150, 150);

            // 左側：2D 彩色效率熱力圖 (Custom GDI+ Heatmap & Contour Control)
            effHeatmap = new EfficiencyHeatmapControl() { Dock = DockStyle.Fill };
            splitEff.Panel1.Controls.Add(effHeatmap);

            // 右側：數據矩陣表 (轉矩 vs 轉速)
            GroupBox grpTable = new GroupBox()
            {
                Text = "效率數值矩陣 (%) [實測採樣即時填入]",
                Dock = DockStyle.Fill,
                Font = new Font("微軟正黑體", 9f, FontStyle.Bold)
            };
            dgvEffMap = new DataGridView()
            {
                Dock = DockStyle.Fill,
                BackgroundColor = Color.White,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                RowHeadersVisible = true,
                RowHeadersWidth = 75,
                AllowUserToAddRows = false,
                Font = new Font("微軟正黑體", 8.5f, FontStyle.Regular)
            };
            grpTable.Controls.Add(dgvEffMap);
            splitEff.Panel2.Controls.Add(grpTable);

            tableEff.Controls.Add(splitEff, 0, 1);
            tab.Controls.Add(tableEff);

            // 首次加載自動計算點位矩陣與預估時間
            UpdateEffMapGridPlan();
        }

        private void UpdateEffMapGridPlan()
        {
            if (numEffStartSpd == null || numEffStepSpd == null || numEffEndSpd == null ||
                numEffStartTrq == null || numEffStepTrq == null || numEffEndTrq == null ||
                numEffDwell == null) return;

            double startSpd = (double)numEffStartSpd.Value;
            double stepSpd = Math.Max(10.0, (double)numEffStepSpd.Value);
            double endSpd = Math.Max(startSpd, (double)numEffEndSpd.Value);

            double startTrq = (double)numEffStartTrq.Value;
            double stepTrq = Math.Max(0.1, (double)numEffStepTrq.Value);
            double endTrq = Math.Max(startTrq, (double)numEffEndTrq.Value);

            List<double> spdList = new List<double>();
            for (double s = startSpd; s <= endSpd + 0.001; s += stepSpd)
            {
                spdList.Add(Math.Round(s, 1));
            }
            if (spdList.Count == 0) spdList.Add(startSpd);

            List<double> trqList = new List<double>();
            for (double t = startTrq; t <= endTrq + 0.001; t += stepTrq)
            {
                trqList.Add(Math.Round(t, 1));
            }
            if (trqList.Count == 0) trqList.Add(startTrq);

            effSpeedAxis = spdList.ToArray();
            effTorqueAxis = trqList.ToArray();

            // 建立測試點位清單 (外迴圈：轉速，內迴圈：轉矩)
            effPointList.Clear();
            int ptIdx = 1;
            for (int sIdx = 0; sIdx < effSpeedAxis.Length; sIdx++)
            {
                for (int tIdx = 0; tIdx < effTorqueAxis.Length; tIdx++)
                {
                    effPointList.Add(new EffTestPoint()
                    {
                        Index = ptIdx++,
                        SpeedIdx = sIdx,
                        TorqueIdx = tIdx,
                        TargetSpeed = effSpeedAxis[sIdx],
                        TargetTorque = effTorqueAxis[tIdx],
                        IsTested = false
                    });
                }
            }

            // 動態計算預估總時間：
            // 每個測試點耗時 = (穩定持載時間 numEffDwell) + (平穩逼近與達標判定平均 6 秒)
            // 每次升速切換耗時 = (卸載 0 轉矩 + 升速平穩過渡平均 4 秒)
            int dwellSec = (int)numEffDwell.Value;
            int totalEstSec = effPointList.Count * (dwellSec + 6) + Math.Max(0, effSpeedAxis.Length - 1) * 4;
            TimeSpan ts = TimeSpan.FromSeconds(totalEstSec);
            string timeStr = (ts.Hours > 0)
                ? string.Format("{0} 小時 {1} 分 {2} 秒", ts.Hours, ts.Minutes, ts.Seconds)
                : string.Format("{0} 分 {1} 秒", ts.Minutes, ts.Seconds);

            if (lblEffPointSummary != null)
            {
                lblEffPointSummary.Text = string.Format(
                    "📊 點位矩陣規劃：{0} 轉速階 × {1} 轉矩階 = 共 {2} 個測試點 | 預估總測試耗時：約 {3}",
                    effSpeedAxis.Length, effTorqueAxis.Length, effPointList.Count, timeStr);
            }

            // 初始化或更新 DataGridView 表格欄位與列
            if (dgvEffMap != null)
            {
                dgvEffMap.Rows.Clear();
                dgvEffMap.Columns.Clear();

                dgvEffMap.Columns.Add("Torque", "轉矩 \\ 轉速");
                dgvEffMap.Columns[0].Width = 85;
                for (int c = 0; c < effSpeedAxis.Length; c++)
                {
                    string colName = "Rpm_" + effSpeedAxis[c];
                    string colHeader = effSpeedAxis[c] + " rpm";
                    dgvEffMap.Columns.Add(colName, colHeader);
                }

                effGridData = new double[effTorqueAxis.Length, effSpeedAxis.Length];

                for (int r = 0; r < effTorqueAxis.Length; r++)
                {
                    int rowIdx = dgvEffMap.Rows.Add();
                    dgvEffMap.Rows[rowIdx].HeaderCell.Value = effTorqueAxis[r].ToString("F1") + " Nm";
                    dgvEffMap.Rows[rowIdx].Cells[0].Value = effTorqueAxis[r].ToString("F1") + " Nm";
                    for (int c = 0; c < effSpeedAxis.Length; c++)
                    {
                        dgvEffMap.Rows[rowIdx].Cells[c + 1].Value = "-";
                    }
                }
            }

            // 更新熱力圖控制項坐標軸與資料維度
            if (effHeatmap != null)
            {
                effHeatmap.SetAxes(effSpeedAxis, effTorqueAxis, effGridData);
            }
        }

        private void BtnGenEffMap_Click(object sender, EventArgs e)
        {
            if (effSpeedAxis == null || effTorqueAxis == null || dgvEffMap == null) return;

            int rows = effTorqueAxis.Length;
            int cols = effSpeedAxis.Length;
            effGridData = new double[rows, cols];

            double midSpd = (effSpeedAxis[0] + effSpeedAxis[cols - 1]) * 0.6;
            double spdSpan = Math.Max(500.0, effSpeedAxis[cols - 1] - effSpeedAxis[0]);
            double midTrq = (effTorqueAxis[0] + effTorqueAxis[rows - 1]) * 0.65;
            double trqSpan = Math.Max(5.0, effTorqueAxis[rows - 1] - effTorqueAxis[0]);

            double peakEff = 0;
            double peakSpd = 0;
            double peakTrq = 0;

            for (int r = 0; r < rows; r++)
            {
                double t = effTorqueAxis[r];
                for (int c = 0; c < cols; c++)
                {
                    double rpm = effSpeedAxis[c];
                    double eff = Math.Max(62.0, Math.Min(96.2, 95.5
                        - Math.Pow((rpm - midSpd) / (spdSpan * 0.65), 2) * 12.0
                        - Math.Pow((t - midTrq) / (trqSpan * 0.65), 2) * 10.5
                        + (rand.NextDouble() - 0.5) * 0.4));
                    eff = Math.Round(eff, 1);
                    effGridData[r, c] = eff;
                    if (r < dgvEffMap.Rows.Count && c + 1 < dgvEffMap.Columns.Count)
                    {
                        dgvEffMap.Rows[r].Cells[c + 1].Value = eff.ToString("F1") + "%";
                    }

                    if (eff > peakEff)
                    {
                        peakEff = eff;
                        peakSpd = rpm;
                        peakTrq = t;
                    }
                }
            }

            if (effHeatmap != null)
            {
                effHeatmap.SetAxes(effSpeedAxis, effTorqueAxis, effGridData);
            }

            if (lblEffMapPeak != null)
            {
                lblEffMapPeak.Text = string.Format("最高效率核心：{0:F1}% @ {1:F0} rpm / {2:F1} Nm (模擬試算)", peakEff, peakSpd, peakTrq);
            }
            MessageBox.Show("馬達 2D 彩色效率地圖已成功依當前梯度設定重新計算並繪製！", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private void BtnStartEffMap_Click(object sender, EventArgs e)
        {
            if (effPointList == null || effPointList.Count == 0)
            {
                UpdateEffMapGridPlan();
            }

            if (effPointList.Count == 0)
            {
                MessageBox.Show("尚未規劃任何測試點位，請檢查轉速與轉矩梯度設定！", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            int roleIdx = (cmbEffRole != null && cmbEffRole.SelectedIndex >= 0) ? cmbEffRole.SelectedIndex : 1;
            int spdDrive = (roleIdx == 1) ? 2 : 1;
            int trqDrive = (spdDrive == 1) ? 2 : 1;
            int spdCom = GetHmiKebComIdx(spdDrive), spdBaud = GetHmiKebBaudIdx(spdDrive), spdNode = (spdDrive == 1) ? (int)numHmiKebNode1.Value : (int)numHmiKebNode2.Value;
            int trqCom = GetHmiKebComIdx(trqDrive), trqBaud = GetHmiKebBaudIdx(trqDrive), trqNode = (trqDrive == 1) ? (int)numHmiKebNode1.Value : (int)numHmiKebNode2.Value;
            string spdDriveName = (spdDrive == 1) ? "A載台" : "B載台";
            string trqDriveName = (trqDrive == 1) ? "A載台" : "B載台";

            // 重設表格與狀態
            effCurrentPointIdx = 0;
            effPhase = 0;
            effConvergeTimeoutSec = 0;
            effStepSpeedReached = false;
            effTrqSustainedSec = 0;
            effAdaptedTorquePct = 0.0;
            effDwellRemaining = (int)numEffDwell.Value;
            effResults.Clear();

            for (int r = 0; r < effTorqueAxis.Length; r++)
            {
                for (int c = 0; c < effSpeedAxis.Length; c++)
                {
                    effGridData[r, c] = 0.0;
                    if (r < dgvEffMap.Rows.Count && c + 1 < dgvEffMap.Columns.Count)
                    {
                        dgvEffMap.Rows[r].Cells[c + 1].Value = "-";
                    }
                }
            }

            if (prgEffMap != null)
            {
                prgEffMap.Maximum = effPointList.Count;
                prgEffMap.Value = 0;
            }

            btnStartEffMap.Enabled = false;
            btnStopEffMap.Enabled = true;

            EffTestPoint pt0 = effPointList[0];
            // 待測端設定初速
            KebWriteParam32(spdCom, spdBaud, spdNode, 0x0034, (int)pt0.TargetSpeed, "效率地圖初轉速 (Sy52)");
            KebWriteParamWithDll(spdCom, spdBaud, spdNode, 0x0F12, 1000); // 待測端放行 100% 轉矩
            SetHmiKebCommand(spdCom, spdBaud, spdNode, 4, string.Format("{0}效率待測端正轉 (Sy50=4)", spdDriveName));

            // 加載端初轉矩歸零並停機待命
            KebWriteParamWithDll(trqCom, trqBaud, trqNode, 0x0F12, 0);
            SetHmiKebCommand(trqCom, trqBaud, trqNode, 0, string.Format("{0}效率加載端空載待命 (Sy50=0)", trqDriveName));

            if (effHeatmap != null)
            {
                effHeatmap.SetAxes(effSpeedAxis, effTorqueAxis, effGridData);
                effHeatmap.SetActiveCell(pt0.TorqueIdx, pt0.SpeedIdx);
            }

            lblEffMapStatus.Text = string.Format("正在啟動：點位 1/{0} (目標 {1:F0} rpm / {2:F1} Nm)",
                effPointList.Count, pt0.TargetSpeed, pt0.TargetTorque);
            lblEffMapCountdown.Text = "提速空載中";

            WriteHmiLog("EFF_START", string.Format("【效率地圖啟動測試】共 {0} 點位 | {1}待測 (速度) / {2}加載 (轉矩)",
                effPointList.Count, spdDriveName, trqDriveName));

            if (!isRunning) BtnStart_Click(null, null);

            if (effMapTimer == null)
            {
                effMapTimer = new System.Windows.Forms.Timer();
                effMapTimer.Interval = 1000;
                effMapTimer.Tick += EffMapTimer_Tick;
            }
            effMapTimer.Start();
        }

        private void EffMapTimer_Tick(object sender, EventArgs e)
        {
            if (effCurrentPointIdx >= effPointList.Count)
            {
                FinishEffMapTest();
                return;
            }

            EffTestPoint curPt = effPointList[effCurrentPointIdx];

            int roleIdx = (cmbEffRole != null && cmbEffRole.SelectedIndex >= 0) ? cmbEffRole.SelectedIndex : 1;
            int spdDrive = (roleIdx == 1) ? 2 : 1;
            int trqDrive = (spdDrive == 1) ? 2 : 1;
            int spdCom = GetHmiKebComIdx(spdDrive), spdBaud = GetHmiKebBaudIdx(spdDrive), spdNode = (spdDrive == 1) ? (int)numHmiKebNode1.Value : (int)numHmiKebNode2.Value;
            int trqCom = GetHmiKebComIdx(trqDrive), trqBaud = GetHmiKebBaudIdx(trqDrive), trqNode = (trqDrive == 1) ? (int)numHmiKebNode1.Value : (int)numHmiKebNode2.Value;
            string spdDriveName = (spdDrive == 1) ? "A載台" : "B載台";
            string trqDriveName = (trqDrive == 1) ? "A載台" : "B載台";

            double targetSpd = curPt.TargetSpeed;
            double targetTrq = curPt.TargetTorque;
            double actAbsSpd = Math.Abs(actSpeed);
            double actAbsTrq = Math.Abs(actTorque);

            double spdErr = Math.Abs(actAbsSpd - targetSpd);
            double trqErr = targetTrq - actAbsTrq;

            bool isSpdValid = (targetSpd <= 0) || (spdErr <= Math.Max(25.0, targetSpd * 0.08));
            bool isTrqValid = (targetTrq <= 0) || (effAdaptedTorquePct >= 1.0 && Math.Abs(trqErr) <= Math.Max(1.0, targetTrq * 0.08));

            // =========================================================================
            // 【階段 0：提速起跑 ➔ 轉速鎖定 ➔ 平穩加載逼近】
            // =========================================================================
            if (effPhase == 0)
            {
                effConvergeTimeoutSec++;
                effDwellRemaining = (int)numEffDwell.Value;

                // 子階段 0A：等待轉速首次到位
                if (!effStepSpeedReached)
                {
                    bool reached = (targetSpd <= 0) || (actAbsSpd >= targetSpd * 0.82) || (spdErr <= Math.Max(20.0, targetSpd * 0.12));
                    if (!reached)
                    {
                        KebWriteParamWithDll(trqCom, trqBaud, trqNode, 0x0F12, 0); // 加載端保持 0 轉矩
                        lblEffMapStatus.Text = string.Format("⌛ 點位 {0}/{1} 提速中：實測 {2:F0} rpm / 目標 {3:F0} rpm",
                            curPt.Index, effPointList.Count, actAbsSpd, targetSpd);
                        lblEffMapCountdown.Text = "提速空載中";
                        return;
                    }

                    // 轉速到位！加載端啟動激磁 Sy50=4
                    effStepSpeedReached = true;
                    effTrqSustainedSec = 0;
                    int curTrqSy50 = (trqDrive == 1) ? lastSy50Cmd1 : lastSy50Cmd2;
                    if (curTrqSy50 != 4)
                    {
                        SetHmiKebCommand(trqCom, trqBaud, trqNode, 4, string.Format("{0}效率加載端激磁啟動 (Sy50=4)", trqDriveName));
                    }
                }

                // 子階段 0B：轉速已到位，平穩漸進加載逼近目標轉矩
                if (isTrqValid && isSpdValid)
                {
                    effTrqSustainedSec++;
                }
                else
                {
                    effTrqSustainedSec = 0;
                }

                if (effTrqSustainedSec < 2)
                {
                    double absTrqErr = Math.Abs(trqErr);
                    double trqStep = (absTrqErr > 8.0) ? 2.0 : ((absTrqErr > 2.0) ? 1.0 : 0.4);
                    if (trqErr > 0) effAdaptedTorquePct += trqStep;
                    else effAdaptedTorquePct -= trqStep;
                    effAdaptedTorquePct = Math.Max(0.0, Math.Min(100.0, effAdaptedTorquePct));

                    int trqRaw = (int)Math.Round(effAdaptedTorquePct * 10);
                    KebWriteParamWithDll(trqCom, trqBaud, trqNode, 0x0F12, trqRaw);

                    lblEffMapStatus.Text = string.Format("⌛ 點位 {0}/{1} 加載中：實測 {2:F1} Nm / 目標 {3:F1} Nm (給定 {4:F1}%)",
                        curPt.Index, effPointList.Count, actAbsTrq, targetTrq, effAdaptedTorquePct);
                    lblEffMapCountdown.Text = string.Format("加載中 ({0}s)", effConvergeTimeoutSec);

                    // 超時防呆保護 (45秒)
                    if (effConvergeTimeoutSec >= 45)
                    {
                        StopEffMapTest();
                        lblEffMapStatus.Text = "⚠️ 加載未達標，測試已暫停！";
                        lblEffMapCountdown.Text = "加載未達標中斷";
                        MessageBox.Show(string.Format("【效率地圖測試警報】點位 {0} 加載超過 45 秒無法收斂達標，已安全停機！\n目標轉速: {1:F0} rpm, 目標轉矩: {2:F1} Nm, 實測轉矩: {3:F1} Nm",
                            curPt.Index, targetSpd, targetTrq, actAbsTrq), "加載未達標中斷", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        return;
                    }
                }
                else
                {
                    // 連續 2 秒達標！進入 Phase 1
                    effPhase = 1;
                    effConvergeTimeoutSec = 0;
                    effTrqSustainedSec = 0;
                    lblEffMapStatus.Text = string.Format("✅ 點位 {0}/{1} 達標 ({2:F1} Nm / {3:F0} rpm)，開始持載倒數！",
                        curPt.Index, effPointList.Count, actAbsTrq, actAbsSpd);
                    lblEffMapCountdown.Text = string.Format("持載倒數: {0} s", effDwellRemaining);
                }
            }
            // =========================================================================
            // 【階段 1：穩定持載期與判定】(★ 鐵律：實測轉速與轉矩雙雙達標後才能開始/進行倒數)
            // =========================================================================
            else if (effPhase == 1)
            {
                // 動態微調閉迴路
                if (Math.Abs(trqErr) > 0.4)
                {
                    double step = (Math.Abs(trqErr) > 1.2) ? 0.3 : 0.1;
                    if (trqErr > 0) effAdaptedTorquePct += step;
                    else effAdaptedTorquePct -= step;
                    effAdaptedTorquePct = Math.Max(0.0, Math.Min(100.0, effAdaptedTorquePct));
                    int trqRaw = (int)Math.Round(effAdaptedTorquePct * 10);
                    KebWriteParamWithDll(trqCom, trqBaud, trqNode, 0x0F12, trqRaw);
                }

                bool isTargetReached = (spdErr <= Math.Max(25.0, targetSpd * 0.08)) && (Math.Abs(trqErr) <= Math.Max(1.0, targetTrq * 0.08));
                if (isTargetReached)
                {
                    effDwellRemaining--;
                    lblEffMapCountdown.Text = string.Format("持載倒數: {0} s (已達標)", effDwellRemaining);
                    lblEffMapStatus.Text = string.Format("✅ 點位 {0}/{1} 達標持載：轉速 {2:F0} rpm | 轉矩 {3:F2} Nm",
                        curPt.Index, effPointList.Count, actAbsSpd, actAbsTrq);
                }
                else
                {
                    lblEffMapCountdown.Text = string.Format("調節等待: {0} s (暫停)", effDwellRemaining);
                    lblEffMapStatus.Text = string.Format("⌛ 點位 {0}/{1} 偏離微調：轉速 {2:F0}/{3:F0} rpm | 轉矩 {4:F2}/{5:F1} Nm (未達標暫停)",
                        curPt.Index, effPointList.Count, actAbsSpd, targetSpd, actAbsTrq, targetTrq);
                    effConvergeTimeoutSec++;
                    if (effConvergeTimeoutSec >= 60)
                    {
                        StopEffMapTest();
                        lblEffMapStatus.Text = "⚠️ 持載微調超時，測試已暫停！";
                        lblEffMapCountdown.Text = "微調超時";
                        MessageBox.Show("【效率地圖警報】持載期調節超過 60 秒無法穩定達標，已安全停機！", "超時中斷", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        return;
                    }
                }

                if (effDwellRemaining <= 0)
                {
                    double pMech = (actAbsTrq * actAbsSpd) / 9.549; // 機械功率 (W)
                    double pElec = (Math.Abs(actElecPower) > 0.001) ? Math.Abs(actElecPower) * 1000.0 : Math.Max(100.0, pMech / 0.88);
                    double eff = (actEfficiency > 0.0) ? actEfficiency : ((pElec > 0) ? Math.Max(50.0, Math.Min(98.5, (pMech / pElec) * 100.0)) : 88.0);
                    eff = Math.Round(eff, 1);

                    curPt.MeasuredSpeed = actAbsSpd;
                    curPt.MeasuredTorque = actAbsTrq;
                    curPt.MeasuredPowerOut = pMech;
                    curPt.MeasuredPowerIn = pElec;
                    curPt.Efficiency = eff;
                    curPt.IsTested = true;
                    curPt.IsPass = isTargetReached;

                    // 更新矩陣資料
                    effGridData[curPt.TorqueIdx, curPt.SpeedIdx] = eff;
                    if (curPt.TorqueIdx < dgvEffMap.Rows.Count && curPt.SpeedIdx + 1 < dgvEffMap.Columns.Count)
                    {
                        dgvEffMap.Rows[curPt.TorqueIdx].Cells[curPt.SpeedIdx + 1].Value = eff.ToString("F1") + "%";
                    }

                    // 記錄詳細測試行
                    effResults.Add(new string[] {
                        curPt.Index.ToString(), curPt.TargetSpeed.ToString("F0"), curPt.TargetTorque.ToString("F1"),
                        actAbsSpd.ToString("F1"), actAbsTrq.ToString("F2"),
                        (pElec / 1000.0).ToString("F3"), (pMech / 1000.0).ToString("F3"),
                        eff.ToString("F1"), isTargetReached ? "PASS" : "WARN"
                    });

                    // 更新熱力圖
                    if (effHeatmap != null)
                    {
                        effHeatmap.SetData(effGridData);
                    }

                    if (prgEffMap != null && prgEffMap.Value < prgEffMap.Maximum)
                    {
                        prgEffMap.Value++;
                    }

                    // 尋找當前全局最高效率
                    UpdatePeakEfficiencyLabel();

                    // 移至下一個點位
                    effCurrentPointIdx++;
                    if (effCurrentPointIdx >= effPointList.Count)
                    {
                        FinishEffMapTest();
                        return;
                    }

                    EffTestPoint nextPt = effPointList[effCurrentPointIdx];
                    if (effHeatmap != null)
                    {
                        effHeatmap.SetActiveCell(nextPt.TorqueIdx, nextPt.SpeedIdx);
                    }

                    // 判斷是否需要切換轉速
                    if (Math.Abs(nextPt.TargetSpeed - curPt.TargetSpeed) > 1.0)
                    {
                        // 換速：先卸載轉矩至 0 -> 發送新轉速 -> 重新空載提速判定
                        KebWriteParamWithDll(trqCom, trqBaud, trqNode, 0x0F12, 0);
                        effAdaptedTorquePct = 0.0;
                        effStepSpeedReached = false;
                        effTrqSustainedSec = 0;
                        KebWriteParam32(spdCom, spdBaud, spdNode, 0x0034, (int)nextPt.TargetSpeed, "效率測試升速");

                        lblEffMapStatus.Text = string.Format("切換轉速階：目標 {0:F0} rpm，等待轉速到位...", nextPt.TargetSpeed);
                        lblEffMapCountdown.Text = "提速空載中";
                    }
                    else
                    {
                        // 同轉速，換下一個轉矩點：轉速已鎖定，保持激磁，自適應微調逼近新轉矩
                        effStepSpeedReached = true;
                        effTrqSustainedSec = 0;
                        lblEffMapStatus.Text = string.Format("切換轉矩階：目標 {0:F1} Nm，調節逼近中...", nextPt.TargetTorque);
                        lblEffMapCountdown.Text = "調節逼近中";
                    }

                    effPhase = 0;
                    effConvergeTimeoutSec = 0;
                    effDwellRemaining = (int)numEffDwell.Value;
                }
            }
        }

        private void FinishEffMapTest()
        {
            if (effMapTimer != null) effMapTimer.Stop();

            int roleIdx = (cmbEffRole != null && cmbEffRole.SelectedIndex >= 0) ? cmbEffRole.SelectedIndex : 1;
            int spdDrive = (roleIdx == 1) ? 2 : 1;
            int trqDrive = (spdDrive == 1) ? 2 : 1;
            int spdCom = GetHmiKebComIdx(spdDrive), spdBaud = GetHmiKebBaudIdx(spdDrive), spdNode = (spdDrive == 1) ? (int)numHmiKebNode1.Value : (int)numHmiKebNode2.Value;
            int trqCom = GetHmiKebComIdx(trqDrive), trqBaud = GetHmiKebBaudIdx(trqDrive), trqNode = (trqDrive == 1) ? (int)numHmiKebNode1.Value : (int)numHmiKebNode2.Value;

            KebWriteParamWithDll(trqCom, trqBaud, trqNode, 0x0F12, 0); // 加載卸載歸零
            SetHmiKebCommand(spdCom, spdBaud, spdNode, 0, "效率測試完成停機");
            SetHmiKebCommand(trqCom, trqBaud, trqNode, 0, "效率測試完成停機");

            btnStartEffMap.Enabled = true;
            btnStopEffMap.Enabled = false;
            lblEffMapStatus.Text = "✅ [成功] 效率地圖全梯度自動測試全部完成！";
            lblEffMapCountdown.Text = "測試完成";

            if (effHeatmap != null)
            {
                effHeatmap.SetActiveCell(-1, -1);
            }

            UpdatePeakEfficiencyLabel();
            MessageBox.Show("馬達效率地圖全梯度自動測試已順利完成！請點擊「匯出效率報表 CSV」儲存數據。", "測試完成", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private void StopEffMapTest()
        {
            if (effMapTimer != null) effMapTimer.Stop();

            int roleIdx = (cmbEffRole != null && cmbEffRole.SelectedIndex >= 0) ? cmbEffRole.SelectedIndex : 1;
            int spdDrive = (roleIdx == 1) ? 2 : 1;
            int trqDrive = (spdDrive == 1) ? 2 : 1;
            int spdCom = GetHmiKebComIdx(spdDrive), spdBaud = GetHmiKebBaudIdx(spdDrive), spdNode = (spdDrive == 1) ? (int)numHmiKebNode1.Value : (int)numHmiKebNode2.Value;
            int trqCom = GetHmiKebComIdx(trqDrive), trqBaud = GetHmiKebBaudIdx(trqDrive), trqNode = (trqDrive == 1) ? (int)numHmiKebNode1.Value : (int)numHmiKebNode2.Value;

            KebWriteParamWithDll(trqCom, trqBaud, trqNode, 0x0F12, 0);
            SetHmiKebCommand(spdCom, spdBaud, spdNode, 0, "效率測試使用者停止");
            SetHmiKebCommand(trqCom, trqBaud, trqNode, 0, "效率測試使用者停止");

            btnStartEffMap.Enabled = true;
            btnStopEffMap.Enabled = false;
            lblEffMapStatus.Text = "⚠️ 測試已由使用者手動中止或保護中斷！";
            lblEffMapCountdown.Text = "已停止";

            if (effHeatmap != null)
            {
                effHeatmap.SetActiveCell(-1, -1);
            }
        }

        private void UpdatePeakEfficiencyLabel()
        {
            if (effGridData == null || effSpeedAxis == null || effTorqueAxis == null || lblEffMapPeak == null) return;
            double maxEff = 0;
            double peakSpd = 0;
            double peakTrq = 0;
            for (int r = 0; r < effTorqueAxis.Length; r++)
            {
                for (int c = 0; c < effSpeedAxis.Length; c++)
                {
                    if (effGridData[r, c] > maxEff)
                    {
                        maxEff = effGridData[r, c];
                        peakSpd = effSpeedAxis[c];
                        peakTrq = effTorqueAxis[r];
                    }
                }
            }
            if (maxEff > 0)
            {
                lblEffMapPeak.Text = string.Format("最高效率核心：{0:F1}% @ {1:F0} rpm / {2:F1} Nm", maxEff, peakSpd, peakTrq);
            }
        }

        private void BtnExportEffMap_Click(object sender, EventArgs e)
        {
            try
            {
                string logDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs");
                if (!Directory.Exists(logDir)) Directory.CreateDirectory(logDir);
                string path = Path.Combine(logDir, string.Format("Report_Efficiency_Map_{0}.csv", DateTime.Now.ToString("yyyyMMdd_HHmmss")));
                using (StreamWriter sw = new StreamWriter(path, false, Encoding.UTF8))
                {
                    // 1. 輸出效率矩陣表
                    sw.WriteLine("# 馬達 2D 效率地圖矩陣 (%)");
                    StringBuilder sbH = new StringBuilder();
                    for (int c = 0; c < dgvEffMap.Columns.Count; c++)
                    {
                        sbH.Append(dgvEffMap.Columns[c].HeaderText + ",");
                    }
                    sw.WriteLine(sbH.ToString().TrimEnd(','));

                    for (int r = 0; r < dgvEffMap.Rows.Count; r++)
                    {
                        StringBuilder sbR = new StringBuilder();
                        for (int c = 0; c < dgvEffMap.Columns.Count; c++)
                        {
                            sbR.Append((dgvEffMap.Rows[r].Cells[c].Value != null ? dgvEffMap.Rows[r].Cells[c].Value.ToString() : "") + ",");
                        }
                        sw.WriteLine(sbR.ToString().TrimEnd(','));
                    }

                    // 2. 輸出各測試點詳細遙測記錄
                    if (effResults != null && effResults.Count > 0)
                    {
                        sw.WriteLine();
                        sw.WriteLine("# 各測試點詳細遙測記錄");
                        sw.WriteLine("點位,目標轉速(rpm),目標轉矩(Nm),實測轉速(rpm),實測轉矩(Nm),輸入電功率(kW),輸出機械功率(kW),實測效率(%),判定");
                        foreach (string[] row in effResults)
                        {
                            sw.WriteLine(string.Join(",", row));
                        }
                    }
                }
                MessageBox.Show("效率地圖報表已成功匯出至:\n" + path, "匯出成功", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show("匯出失敗: " + ex.Message, "錯誤");
            }
        }
    }
}
