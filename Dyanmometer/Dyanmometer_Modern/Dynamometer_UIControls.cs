using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.IO.Ports;
using System.Linq;
using System.Text;
using System.Windows.Forms;

namespace DynamometerHMI
{
    // =========================================================================
    // =========================================================================
    //  自訂 2D 馬達效率熱力地圖與等高線畫布 (動態雙向坐標軸 / 單元格數值標籤 / 即時測點追蹤)
    // =========================================================================
    public class EfficiencyHeatmapControl : UserControl
    {
        private double[,] data = null; // [Torques, Speeds]
        private double[] speedAxis = null;
        private double[] torqueAxis = null;
        private int activeRow = -1;
        private int activeCol = -1;
        private double minEff = 60.0;
        private double maxEff = 96.0;

        private static readonly Font fLabel9B = new Font("微軟正黑體", 9f, FontStyle.Bold);
        private static readonly Font fLegend8 = new Font("Arial", 8f);
        private static readonly Font fEffTitle85B = new Font("微軟正黑體", 8.5f, FontStyle.Bold);

        public EfficiencyHeatmapControl()
        {
            this.DoubleBuffered = true;
            this.BackColor = Color.White;
            this.Font = new Font("微軟正黑體", 9f, FontStyle.Regular);
        }

        public void SetAxes(double[] speeds, double[] torques, double[,] matrix)
        {
            this.speedAxis = speeds;
            this.torqueAxis = torques;
            this.data = matrix;
            this.Invalidate();
        }

        public void SetData(double[,] matrix)
        {
            this.data = matrix;
            this.Invalidate();
        }

        public void SetActiveCell(int row, int col)
        {
            this.activeRow = row;
            this.activeCol = col;
            this.Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            int left = 65;
            int top = 30;
            int right = this.Width - 95;
            int bottom = this.Height - 48;

            if (right <= left || bottom <= top) return;

            Rectangle plotRect = new Rectangle(left, top, right - left, bottom - top);

            int rows = (torqueAxis != null && torqueAxis.Length > 0) ? torqueAxis.Length : ((data != null) ? data.GetLength(0) : 10);
            int cols = (speedAxis != null && speedAxis.Length > 0) ? speedAxis.Length : ((data != null) ? data.GetLength(1) : 8);

            if (rows <= 0 || cols <= 0) return;

            float cellW = (float)plotRect.Width / cols;
            float cellH = (float)plotRect.Height / rows;

            // 1. 繪製熱力圖單元格與數值標籤
            using (Font fnCell = new Font("Arial", 8f, FontStyle.Bold))
            using (Font fnCellSmall = new Font("Arial", 7f, FontStyle.Regular))
            {
                for (int r = 0; r < rows; r++)
                {
                    for (int c = 0; c < cols; c++)
                    {
                        double eff = (data != null && r < data.GetLength(0) && c < data.GetLength(1)) ? data[r, c] : 0.0;

                        // Y 軸從下到上 (r=0 在底部, r=rows-1 在頂部)
                        float x = plotRect.Left + c * cellW;
                        float y = plotRect.Bottom - (r + 1) * cellH;
                        RectangleF cellRect = new RectangleF(x, y, cellW, cellH);

                        if (eff > 0.0)
                        {
                            Color col = GetJetColor(eff, minEff, maxEff);
                            using (SolidBrush sb = new SolidBrush(col))
                            {
                                g.FillRectangle(sb, x, y, cellW + 1, cellH + 1);
                            }

                            // 繪製單元格效率數值
                            if (cellW >= 26 && cellH >= 14)
                            {
                                string strVal = eff.ToString("F1");
                                SizeF sz = g.MeasureString(strVal, fnCell);
                                Brush txtBr = (col.R * 0.299 + col.G * 0.587 + col.B * 0.114 > 140) ? Brushes.Black : Brushes.White;
                                g.DrawString(strVal, fnCell, txtBr, x + (cellW - sz.Width) / 2, y + (cellH - sz.Height) / 2);
                            }
                        }
                        else
                        {
                            // 未測試單元格繪製淡灰色
                            using (SolidBrush sbEmpty = new SolidBrush(Color.FromArgb(248, 250, 252)))
                            {
                                g.FillRectangle(sbEmpty, x, y, cellW + 1, cellH + 1);
                            }
                            if (cellW >= 16 && cellH >= 12)
                            {
                                SizeF sz = g.MeasureString("-", fnCellSmall);
                                g.DrawString("-", fnCellSmall, Brushes.Silver, x + (cellW - sz.Width) / 2, y + (cellH - sz.Height) / 2);
                            }
                        }

                        // 網格細線
                        using (Pen pGrid = new Pen(Color.FromArgb(220, 225, 230), 1f))
                        {
                            g.DrawRectangle(pGrid, x, y, cellW, cellH);
                        }

                        // 若為當前正在測試中的點位，繪製醒目金色光暈與亮框
                        if (r == activeRow && c == activeCol)
                        {
                            using (Pen pActive = new Pen(Color.Gold, 2.8f))
                            {
                                g.DrawRectangle(pActive, x + 1, y + 1, cellW - 2, cellH - 2);
                            }
                            using (Pen pGlow = new Pen(Color.FromArgb(160, 255, 215, 0), 1.2f))
                            {
                                g.DrawRectangle(pGlow, x - 1, y - 1, cellW + 2, cellH + 2);
                            }
                        }
                    }
                }
            }

            // 2. 繪製等高線核心標註 (>94%)
            if (data != null)
            {
                using (Font fnCore = new Font("Arial", 8f, FontStyle.Bold))
                {
                    for (int r = 0; r < rows; r++)
                    {
                        for (int c = 0; c < cols; c++)
                        {
                            if (r < data.GetLength(0) && c < data.GetLength(1) && data[r, c] >= 94.0)
                            {
                                float cx = plotRect.Left + (c + 0.5f) * cellW;
                                float cy = plotRect.Bottom - (r + 0.5f) * cellH;
                                using (SolidBrush sbText = new SolidBrush(Color.White))
                                {
                                    g.DrawString("★", fnCore, sbText, cx - 6, cy - 6);
                                }
                            }
                        }
                    }
                }
            }

            // 3. 繪製座標軸外框
            using (Pen pAxis = new Pen(Color.Black, 1.5f))
            {
                g.DrawRectangle(pAxis, plotRect);
            }

            // X 軸刻度與標籤 (轉速)
            for (int c = 0; c < cols; c++)
            {
                float x = plotRect.Left + (c + 0.5f) * cellW;
                string lbl = (speedAxis != null && c < speedAxis.Length)
                    ? speedAxis[c].ToString("F0")
                    : ((c + 1) * 500).ToString();
                SizeF sz = g.MeasureString(lbl, this.Font);
                g.DrawString(lbl, this.Font, Brushes.Black, x - sz.Width / 2, plotRect.Bottom + 4);
                g.DrawLine(Pens.Black, x, plotRect.Bottom, x, plotRect.Bottom + 4);
            }
            g.DrawString("馬達轉速 (RPM)", fLabel9B, Brushes.Black, plotRect.Left + plotRect.Width / 2 - 45, plotRect.Bottom + 25);

            // Y 軸刻度與標籤 (轉矩)
            for (int r = 0; r < rows; r++)
            {
                float y = plotRect.Bottom - (r + 0.5f) * cellH;
                string lbl = (torqueAxis != null && r < torqueAxis.Length)
                    ? torqueAxis[r].ToString("F1")
                    : ((r + 1) * 5).ToString();
                SizeF sz = g.MeasureString(lbl, this.Font);
                g.DrawString(lbl, this.Font, Brushes.Black, plotRect.Left - sz.Width - 5, y - sz.Height / 2);
                g.DrawLine(Pens.Black, plotRect.Left - 4, y, plotRect.Left, y);
            }
            g.DrawString("轉矩\n(Nm)", fLabel9B, Brushes.Black, 8, plotRect.Top + plotRect.Height / 2 - 18);

            // 4. 右側 Colorbar 顏色圖例 (60% ~ 96%)
            int cbLeft = plotRect.Right + 18;
            int cbWidth = 18;
            int cbTop = plotRect.Top;
            int cbHeight = plotRect.Height;

            for (int y = 0; y < cbHeight; y++)
            {
                double ratio = 1.0 - (double)y / cbHeight;
                double effVal = minEff + ratio * (maxEff - minEff);
                Color col = GetJetColor(effVal, minEff, maxEff);
                using (Pen pen = new Pen(col))
                {
                    g.DrawLine(pen, cbLeft, cbTop + y, cbLeft + cbWidth, cbTop + y);
                }
            }
            g.DrawRectangle(Pens.Black, cbLeft, cbTop, cbWidth, cbHeight);

            // 圖例刻度標籤
            g.DrawString("96%", fLegend8, Brushes.Black, cbLeft + cbWidth + 3, cbTop - 5);
            g.DrawString("90%", fLegend8, Brushes.Black, cbLeft + cbWidth + 3, cbTop + (float)(cbHeight * (1.0 - (90 - 60) / 36.0)) - 5);
            g.DrawString("80%", fLegend8, Brushes.Black, cbLeft + cbWidth + 3, cbTop + (float)(cbHeight * (1.0 - (80 - 60) / 36.0)) - 5);
            g.DrawString("70%", fLegend8, Brushes.Black, cbLeft + cbWidth + 3, cbTop + (float)(cbHeight * (1.0 - (70 - 60) / 36.0)) - 5);
            g.DrawString("60%", fLegend8, Brushes.Black, cbLeft + cbWidth + 3, cbTop + cbHeight - 7);

            g.DrawString("效率(%)", fEffTitle85B, Brushes.Black, cbLeft - 6, cbTop - 20);
        }

        // 彩色漸層演算法 (Jet / Turbo Palette: 深藍 -> 藍 -> 青 -> 綠 -> 黃 -> 紅)
        private static Color GetJetColor(double val, double min, double max)
        {
            double norm = Math.Max(0.0, Math.Min(1.0, (val - min) / (max - min)));
            double four_val = 4.0 * norm;

            int r = (int)Math.Max(0, Math.Min(255, 255 * Math.Min(four_val - 1.5, -four_val + 4.5)));
            int g = (int)Math.Max(0, Math.Min(255, 255 * Math.Min(four_val - 0.5, -four_val + 3.5)));
            int b = (int)Math.Max(0, Math.Min(255, 255 * Math.Min(four_val + 0.5, -four_val + 2.5)));

            return Color.FromArgb(r, g, b);
        }
    }

    public class FontCustomizerDialog : Form
    {
        private MainForm main;
        private NumericUpDown numVal, numTitle, numKeb, numTelem, numKebNum, numParamHeight;

        public FontCustomizerDialog(MainForm mainForm)
        {
            this.main = mainForm;
            this.Text = " 綜合監控介面字體與載台高度自訂設定";
            this.Size = new Size(580, 480);
            this.StartPosition = FormStartPosition.CenterParent;
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.Font = new Font("微軟正黑體", 9.5f);
            this.BackColor = Color.FromArgb(248, 250, 252);

            TableLayoutPanel table = new TableLayoutPanel()
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 3,
                Padding = new Padding(12)
            };
            table.RowStyles.Add(new RowStyle(SizeType.Absolute, 36f));
            table.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            table.RowStyles.Add(new RowStyle(SizeType.Absolute, 50f));

            Label lblHdr = new Label()
            {
                Text = " 您可自訂調整主畫面各區塊字體大小與高度，點擊【 即時套用】預覽：",
                Font = new Font("微軟正黑體", 9.5f, FontStyle.Bold),
                ForeColor = Color.FromArgb(15, 23, 42),
                Dock = DockStyle.Fill
            };
            table.Controls.Add(lblHdr, 0, 0);

            GroupBox grp = new GroupBox()
            {
                Text = " 顯示區塊字體與高度設定 ",
                Dock = DockStyle.Fill,
                Font = new Font("微軟正黑體", 9.5f, FontStyle.Bold),
                Padding = new Padding(10)
            };

            TableLayoutPanel formGrid = new TableLayoutPanel()
            {
                Dock = DockStyle.Fill,
                ColumnCount = 3,
                RowCount = 6
            };
            formGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 55f));
            formGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 95f));
            formGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 70f));
            for (int i = 0; i < 6; i++) formGrid.RowStyles.Add(new RowStyle(SizeType.Percent, 16.66f));

            numVal = AddFontRow(formGrid, "1. 頂部 6 大核心量測數值字體", 10, 36, (decimal)main.fontMetricValPt, 0, "pt (級)");
            numTitle = AddFontRow(formGrid, "2. 頂部 6 大核心指標卡片標題字體", 8, 20, (decimal)main.fontMetricTitlePt, 1, "pt (級)");
            numKebNum = AddFontRow(formGrid, "3. A/B 載台轉速與轉矩輸入框字體 (倍增高度)", 9, 26, (decimal)main.fontKebNumericPt, 2, "pt (級)");
            numKeb = AddFontRow(formGrid, "4. A/B 載台控制按鈕與監控表格字體", 7, 18, (decimal)main.fontKebRuGridPt, 3, "pt (級)");
            numTelem = AddFontRow(formGrid, "5. 橫河 WT333E 三相電氣遙測表格字體", 7, 20, (decimal)main.fontTelemetryGridPt, 4, "pt (級)");
            
            int curH = (main.splitParam1 != null && main.splitParam1.SplitterDistance > 0) ? main.splitParam1.SplitterDistance : 275;
            numParamHeight = AddFontRow(formGrid, "6. A/B 載台控制面板高度 (自由上下拉伸)", 180, 450, curH, 5, "px (像素)", 10m, 0);

            grp.Controls.Add(formGrid);
            table.Controls.Add(grp, 0, 1);

            Panel pnlBtns = new Panel() { Dock = DockStyle.Fill };
            Button btnReset = new Button()
            {
                Text = " 恢復預設",
                Location = new Point(5, 8),
                Size = new Size(105, 34),
                BackColor = Color.FromArgb(226, 232, 240),
                Font = new Font("微軟正黑體", 9.5f)
            };
            btnReset.Click += (s, e) => {
                numVal.Value = 18m;
                numTitle.Value = 10.5m;
                numKebNum.Value = 21m;
                numKeb.Value = 9.5m;
                numTelem.Value = 9.5m;
                numParamHeight.Value = 275m;
                ApplyValues();
            };

            Button btnApply = new Button()
            {
                Text = " 即時套用",
                Location = new Point(255, 8),
                Size = new Size(125, 34),
                BackColor = Color.FromArgb(0, 180, 216),
                ForeColor = Color.White,
                Font = new Font("微軟正黑體", 9.5f, FontStyle.Bold)
            };
            btnApply.Click += (s, e) => ApplyValues();

            Button btnSave = new Button()
            {
                Text = " 儲存並關閉",
                Location = new Point(395, 8),
                Size = new Size(135, 34),
                BackColor = Color.FromArgb(16, 185, 129),
                ForeColor = Color.White,
                Font = new Font("微軟正黑體", 9.5f, FontStyle.Bold)
            };
            btnSave.Click += (s, e) => {
                ApplyValues();
                main.SaveLayoutConfig();
                this.Close();
            };

            pnlBtns.Controls.AddRange(new Control[] { btnReset, btnApply, btnSave });
            table.Controls.Add(pnlBtns, 0, 2);

            this.Controls.Add(table);
        }

        private NumericUpDown AddFontRow(TableLayoutPanel grid, string label, int min, int max, decimal val, int row, string unitText = "pt (級)", decimal step = 0.5m, int decimals = 1)
        {
            Label lbl = new Label()
            {
                Text = label,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                Font = new Font("微軟正黑體", 9f, FontStyle.Regular)
            };
            NumericUpDown num = new NumericUpDown()
            {
                Minimum = min,
                Maximum = max,
                DecimalPlaces = decimals,
                Increment = step,
                Value = Math.Max(min, Math.Min(max, val)),
                Dock = DockStyle.Fill,
                Font = new Font("Consolas", 10.5f, FontStyle.Bold),
                TextAlign = HorizontalAlignment.Center
            };
            num.ValueChanged += (s, e) => ApplyValues();
            Label lblUnit = new Label()
            {
                Text = unitText,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                ForeColor = Color.Gray
            };

            grid.Controls.Add(lbl, 0, row);
            grid.Controls.Add(num, 1, row);
            grid.Controls.Add(lblUnit, 2, row);
            return num;
        }

        private void ApplyValues()
        {
            if (main == null) return;
            main.fontMetricValPt = (float)numVal.Value;
            main.fontMetricTitlePt = (float)numTitle.Value;
            main.fontKebNumericPt = (float)numKebNum.Value;
            main.fontKebRuGridPt = (float)numKeb.Value;
            main.fontTelemetryGridPt = (float)numTelem.Value;
            
            int dist = (int)numParamHeight.Value;
            if (main.splitParam1 != null) main.splitParam1.SplitterDistance = dist;
            if (main.splitParam2 != null) main.splitParam2.SplitterDistance = dist;

            main.ApplyFontSizes();
        }
    }

    // =========================================================================
    //  自訂 T-N 特性曲線畫布 (TnCurveChart)
    // =========================================================================
    public class TnCurveChart : UserControl
    {
        private List<PointF> speedTorquePoints = new List<PointF>();
        private List<PointF> speedPowerPoints = new List<PointF>();

        private static readonly Font fLabel9B = new Font("微軟正黑體", 9f, FontStyle.Bold);
        private static readonly Font fHint10B = new Font("微軟正黑體", 10f, FontStyle.Bold);

        public TnCurveChart()
        {
            this.DoubleBuffered = true;
            this.BackColor = Color.White;
            this.Font = new Font("微軟正黑體", 9f, FontStyle.Regular);
        }

        public void AddPoint(double speed, double torque, double power)
        {
            speedTorquePoints.Add(new PointF((float)speed, (float)torque));
            speedPowerPoints.Add(new PointF((float)speed, (float)power));
            this.Invalidate();
        }

        public void ClearPoints()
        {
            speedTorquePoints.Clear();
            speedPowerPoints.Clear();
            this.Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            int left = 55;
            int top = 25;
            int right = this.Width - 30;
            int bottom = this.Height - 40;

            if (right <= left || bottom <= top) return;
            Rectangle plotRect = new Rectangle(left, top, right - left, bottom - top);

            // 網格線
            using (Pen pGrid = new Pen(Color.FromArgb(235, 238, 242), 1f))
            {
                for (int i = 0; i <= 8; i++)
                {
                    float x = plotRect.Left + i * (plotRect.Width / 8f);
                    g.DrawLine(pGrid, x, plotRect.Top, x, plotRect.Bottom);
                }
                for (int i = 0; i <= 5; i++)
                {
                    float y = plotRect.Top + i * (plotRect.Height / 5f);
                    g.DrawLine(pGrid, plotRect.Left, y, plotRect.Right, y);
                }
            }

            // 軸線
            g.DrawRectangle(Pens.DarkGray, plotRect);

            // 軸刻度標籤
            g.DrawString("0", this.Font, Brushes.Black, plotRect.Left - 15, plotRect.Bottom - 6);
            g.DrawString("4000 rpm", this.Font, Brushes.Black, plotRect.Right - 55, plotRect.Bottom + 5);
            g.DrawString("實測轉速 (RPM)", fLabel9B, Brushes.Black, plotRect.Left + plotRect.Width / 2 - 40, plotRect.Bottom + 18);
            g.DrawString("轉矩\n(Nm)", fLabel9B, Brushes.DarkOrange, 8, plotRect.Top + 10);

            // 繪製 T-N 實測點位與平滑曲線
            if (speedTorquePoints.Count > 0)
            {
                PointF[] screenPts = new PointF[speedTorquePoints.Count];
                for (int i = 0; i < speedTorquePoints.Count; i++)
                {
                    float sx = plotRect.Left + (speedTorquePoints[i].X / 4000f) * plotRect.Width;
                    float sy = plotRect.Bottom - (speedTorquePoints[i].Y / 100f) * plotRect.Height;
                    screenPts[i] = new PointF(sx, sy);
                }

                if (screenPts.Length > 1)
                {
                    using (Pen pCurve = new Pen(Color.FromArgb(245, 158, 11), 2.5f))
                    {
                        g.DrawLines(pCurve, screenPts);
                    }
                }

                foreach (var pt in screenPts)
                {
                    g.FillEllipse(Brushes.Red, pt.X - 4, pt.Y - 4, 8, 8);
                    g.DrawEllipse(Pens.Black, pt.X - 4, pt.Y - 4, 8, 8);
                }
            }
            else
            {
                // 空白提示文字
                string hint = " T-N 特性曲線即時繪圖區 (開始測試後自動繪製)";
                SizeF sz = g.MeasureString(hint, fHint10B);
                g.DrawString(hint, fHint10B, Brushes.DarkGray, plotRect.Left + (plotRect.Width - sz.Width) / 2, plotRect.Top + (plotRect.Height - sz.Height) / 2);
            }
        }
    }

    // =========================================================================
    // GL820 完整 20 通道溫度趨勢動態繪圖控制項 (支援 CH1~20 雙排圖例與自訂通道名稱)
    // =========================================================================
    public class GbdTemperatureTrendControl : Control
    {
        private MainForm main;
        private readonly List<KeyValuePair<DateTime, double[]>> samples = new List<KeyValuePair<DateTime, double[]>>();
        public bool IsConnected = false;
        private readonly Color[] chColors = new Color[]
        {
            Color.FromArgb(239, 68, 68),   Color.FromArgb(245, 158, 11),  Color.FromArgb(16, 185, 129),  Color.FromArgb(59, 130, 246),
            Color.FromArgb(139, 92, 246),  Color.FromArgb(236, 72, 153),  Color.FromArgb(20, 184, 166),  Color.FromArgb(249, 115, 22),
            Color.FromArgb(99, 102, 241),  Color.FromArgb(34, 197, 94),   Color.FromArgb(217, 70, 239),  Color.FromArgb(14, 165, 233),
            Color.FromArgb(168, 85, 247),  Color.FromArgb(234, 88, 12),   Color.FromArgb(13, 148, 136),  Color.FromArgb(79, 70, 229),
            Color.FromArgb(225, 29, 72),   Color.FromArgb(101, 163, 13),  Color.FromArgb(202, 138, 4),   Color.FromArgb(71, 85, 105)
        };

        private FlowLayoutPanel pnlTimeSpan;
        private Button btnTimeMinus;
        private Button btnTimePlus;
        private Label lblTimeSpan;

        private static readonly Font fontTick8 = new Font("微軟正黑體", 8f);
        private static readonly Font fontWarn12B = new Font("微軟正黑體", 12f, FontStyle.Bold);
        private static readonly Font fontSub95 = new Font("微軟正黑體", 9.5f, FontStyle.Regular);
        private static readonly Font fontTag8B = new Font("微軟正黑體", 8f, FontStyle.Bold);
        private static readonly Font fontNotice9B = new Font("微軟正黑體", 9f, FontStyle.Bold);
        private static readonly Font fontNotice95B = new Font("微軟正黑體", 9.5f, FontStyle.Bold);
        private static readonly Font fontLeg85B = new Font("微軟正黑體", 8.5f, FontStyle.Bold);
        private static readonly Font fontLeg75B = new Font("微軟正黑體", 7.5f, FontStyle.Bold);

        private static readonly int[] timeSpanSteps = new int[] { 30, 60, 120, 300, 600, 1800, 3600 };
        private static readonly string[] timeSpanNames = new string[] { "30秒", "1分鐘", "2分鐘", "5分鐘", "10分鐘", "30分鐘", "1小時" };
        private int currentTimeSpanIndex = 3; // 預設 5 分鐘
        public int CurrentTimeSpanSeconds { get { return timeSpanSteps[currentTimeSpanIndex]; } }

        public GbdTemperatureTrendControl(MainForm parent = null)
        {
            this.main = parent;
            this.DoubleBuffered = true;
            this.BackColor = Color.White;
            InitTimeSpanToolbar();
        }

        private void InitTimeSpanToolbar()
        {
            pnlTimeSpan = new FlowLayoutPanel()
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                FlowDirection = FlowDirection.LeftToRight,
                BackColor = Color.FromArgb(248, 250, 252),
                Margin = new Padding(0),
                Padding = new Padding(2)
            };

            btnTimeMinus = new Button()
            {
                Text = "➖",
                Size = new Size(24, 20),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.White,
                Cursor = Cursors.Hand,
                Font = new Font("微軟正黑體", 7.5f)
            };
            btnTimeMinus.FlatAppearance.BorderColor = Color.FromArgb(203, 213, 225);
            btnTimeMinus.Click += (s, e) => {
                if (currentTimeSpanIndex > 0)
                {
                    currentTimeSpanIndex--;
                    UpdateTimeSpanLabel();
                    this.Invalidate();
                }
            };

            lblTimeSpan = new Label()
            {
                Text = "⏱️ " + timeSpanNames[currentTimeSpanIndex],
                AutoSize = true,
                Font = new Font("微軟正黑體", 8f, FontStyle.Bold),
                ForeColor = Color.FromArgb(71, 85, 105),
                Cursor = Cursors.Hand,
                Margin = new Padding(2, 3, 2, 0)
            };
            lblTimeSpan.Click += (s, e) => {
                currentTimeSpanIndex = (currentTimeSpanIndex + 1) % timeSpanSteps.Length;
                UpdateTimeSpanLabel();
                this.Invalidate();
            };

            btnTimePlus = new Button()
            {
                Text = "➕",
                Size = new Size(24, 20),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.White,
                Cursor = Cursors.Hand,
                Font = new Font("微軟正黑體", 7.5f)
            };
            btnTimePlus.FlatAppearance.BorderColor = Color.FromArgb(203, 213, 225);
            btnTimePlus.Click += (s, e) => {
                if (currentTimeSpanIndex < timeSpanSteps.Length - 1)
                {
                    currentTimeSpanIndex++;
                    UpdateTimeSpanLabel();
                    this.Invalidate();
                }
            };

            pnlTimeSpan.Controls.AddRange(new Control[] { btnTimeMinus, lblTimeSpan, btnTimePlus });
            this.Controls.Add(pnlTimeSpan);
            PositionTimeSpanToolbar();
        }

        private void UpdateTimeSpanLabel()
        {
            if (lblTimeSpan != null)
            {
                lblTimeSpan.Text = "⏱️ " + timeSpanNames[currentTimeSpanIndex];
                PositionTimeSpanToolbar();
            }
        }

        private void PositionTimeSpanToolbar()
        {
            if (pnlTimeSpan != null)
            {
                pnlTimeSpan.Location = new Point(Math.Max(10, this.Width - pnlTimeSpan.PreferredSize.Width - 16), 2);
                pnlTimeSpan.BringToFront();
            }
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            PositionTimeSpanToolbar();
        }

        public void AddSample(DateTime time, double[] channelTemps)
        {
            if (!IsConnected || channelTemps == null || channelTemps.Length == 0) return;
            double[] copy = new double[channelTemps.Length];
            for (int i = 0; i < channelTemps.Length; i++)
            {
                copy[i] = (channelTemps[i] > 0.0) ? channelTemps[i] : 0.0;
            }
            samples.Add(new KeyValuePair<DateTime, double[]>(time, copy));
            // 長時間運行記憶體防護：批次修剪取代頻繁 RemoveAt(0)，大幅減少陣列搬移與 GC 開銷
            if (samples.Count > 3600 + 120)
            {
                samples.RemoveRange(0, 120);
            }
            this.Invalidate();
        }

        private bool[] channelVisible = null; // null = 全部顯示

        public void SetChannelVisibility(bool[] mask)
        {
            channelVisible = (mask != null) ? (bool[])mask.Clone() : null;
            this.Invalidate();
        }

        public void ClearData()
        {
            samples.Clear();
            samples.TrimExcess(); // 主動釋放內部陣列容量，歸還記憶體
            this.Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            try
            {
                Graphics g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;

                Rectangle plotRect = new Rectangle(50, 24, this.Width - 70, Math.Max(50, this.Height - 80));
                if (plotRect.Width <= 10 || plotRect.Height <= 10) return;

                // 背景與網格
                g.FillRectangle(Brushes.White, plotRect);

            // 統計可見區間內的資料與 Y 軸自動適應 (上下 25% 餘裕，不強制從 0 起算)
            DateTime now = (samples.Count > 0) ? samples[samples.Count - 1].Key : DateTime.Now;
            DateTime startTime = now.AddSeconds(-CurrentTimeSpanSeconds);

            double minTemp = double.MaxValue;
            double maxTemp = double.MinValue;
            int validPointsCount = 0;

            for (int i = 0; i < samples.Count; i++)
            {
                var s = samples[i];
                if (s.Key >= startTime)
                {
                    int chCount = Math.Min(20, s.Value.Length);
                    for (int ch = 0; ch < chCount; ch++)
                    {
                        if (channelVisible != null && ch < channelVisible.Length && !channelVisible[ch]) continue;
                        double val = s.Value[ch];
                        if (val > 0.0 && val < 500.0)
                        {
                            validPointsCount++;
                            if (val < minTemp) minTemp = val;
                            if (val > maxTemp) maxTemp = val;
                        }
                    }
                }
            }

            double plotMinY = 20.0, plotMaxY = 80.0;
            if (validPointsCount > 0 && minTemp != double.MaxValue)
            {
                double span = maxTemp - minTemp;
                if (span < 2.0)
                {
                    plotMinY = Math.Max(0.0, minTemp - 5.0);
                    plotMaxY = maxTemp + 5.0;
                }
                else
                {
                    double margin = span * 0.25; // 使用者要求：測定值上下各留 25% 餘裕自適應
                    plotMinY = Math.Max(0.0, minTemp - margin);
                    plotMaxY = maxTemp + margin;
                }
            }
            if (plotMaxY <= plotMinY) plotMaxY = plotMinY + 10.0;

            using (Pen pGrid = new Pen(Color.FromArgb(235, 238, 242), 1f))
            {
                int gridSteps = 5;
                for (int i = 0; i <= gridSteps; i++)
                {
                    float y = plotRect.Top + i * (plotRect.Height / (float)gridSteps);
                    g.DrawLine(pGrid, plotRect.Left, y, plotRect.Right, y);
                    double tempVal = plotMaxY - i * ((plotMaxY - plotMinY) / gridSteps);
                    string tStr = (plotMaxY - plotMinY < 25.0) ? string.Format("{0:F1}°C", tempVal) : string.Format("{0:F0}°C", tempVal);
                    g.DrawString(tStr, this.Font, Brushes.Gray, 5, y - 6);
                }
                int xSteps = 5;
                for (int i = 0; i <= xSteps; i++)
                {
                    float x = plotRect.Left + i * (plotRect.Width / (float)xSteps);
                    g.DrawLine(pGrid, x, plotRect.Top, x, plotRect.Bottom);
                    int secFromNow = (xSteps - i) * (CurrentTimeSpanSeconds / xSteps);
                    string timeStr = (secFromNow == 0) ? "現在" : (secFromNow >= 60 ? string.Format("-{0}m", secFromNow / 60) : string.Format("-{0}s", secFromNow));
                    g.DrawString(timeStr, fontTick8, Brushes.Gray, x - 12, plotRect.Bottom + 2);
                }
            }

            g.DrawRectangle(Pens.DarkGray, plotRect);

            if (!IsConnected)
            {
                using (SolidBrush bBg = new SolidBrush(Color.FromArgb(248, 250, 252)))
                {
                    g.FillRectangle(bBg, plotRect);
                }
                string warnText = "⚠️ 設備未連線";
                string subText = "Graphtec GL820 溫度記錄器未連線 (無即時數據，請檢查乙太網路或IP)";
                SizeF sz1 = g.MeasureString(warnText, fontWarn12B);
                SizeF sz2 = g.MeasureString(subText, fontSub95);
                float cy = plotRect.Top + (plotRect.Height - (sz1.Height + sz2.Height + 6)) / 2;
                g.DrawString(warnText, fontWarn12B, Brushes.Crimson, plotRect.Left + (plotRect.Width - sz1.Width) / 2, cy);
                g.DrawString(subText, fontSub95, Brushes.Gray, plotRect.Left + (plotRect.Width - sz2.Width) / 2, cy + sz1.Height + 6);
                return;
            }

            // 繪製各通道曲線 (1 ~ 20 通道，依 channelVisible 過濾)
            if (samples.Count > 1)
            {
                int chCount = Math.Min(20, samples[0].Value.Length);
                int drawnLineCount = 0;
                int singleDrawnCh = -1;
                double singleDrawnTemp = 0.0;

                for (int ch = 0; ch < chCount; ch++)
                {
                    // 若該通道被勾選為不顯示，略過繪製
                    if (channelVisible != null && ch < channelVisible.Length && !channelVisible[ch])
                        continue;

                    // 找出可見區間起始索引，避免遍歷無效歷史
                    int startIdx = 0;
                    while (startIdx < samples.Count && samples[startIdx].Key < startTime) startIdx++;
                    int visibleCount = samples.Count - startIdx;
                    // 降採樣步進：螢幕像素有限，當點數大於半個繪圖寬度時步進抽樣，避免數萬點 GDI 卡死與 GC 垃圾累積
                    int step = (visibleCount > plotRect.Width / 2 && plotRect.Width > 0) ? Math.Max(1, visibleCount / Math.Max(100, plotRect.Width / 2)) : 1;

                    List<PointF> pts = new List<PointF>(Math.Min(visibleCount + 2, 500));
                    for (int i = startIdx; i < samples.Count; i += step)
                    {
                        var s = samples[i];
                        double temp = (ch < s.Value.Length) ? s.Value[ch] : 0.0;
                        if (temp <= 0.0) continue;
                        double secOffset = (s.Key - startTime).TotalSeconds;
                        float x = plotRect.Left + (float)(Math.Max(0.0, Math.Min(CurrentTimeSpanSeconds, secOffset)) / CurrentTimeSpanSeconds) * plotRect.Width;
                        float y = plotRect.Bottom - (float)((temp - plotMinY) / (plotMaxY - plotMinY)) * plotRect.Height;
                        pts.Add(new PointF(x, Math.Max(plotRect.Top, Math.Min(plotRect.Bottom, y))));
                    }
                    // 關鍵保證：確保最後一個最新點 100% 被納入，維持即時波形與端點標籤精準度
                    if (samples.Count > 0 && (samples.Count - 1 - startIdx) % step != 0)
                    {
                        var s = samples[samples.Count - 1];
                        double temp = (ch < s.Value.Length) ? s.Value[ch] : 0.0;
                        if (temp > 0.0)
                        {
                            double secOffset = (s.Key - startTime).TotalSeconds;
                            float x = plotRect.Left + (float)(Math.Max(0.0, Math.Min(CurrentTimeSpanSeconds, secOffset)) / CurrentTimeSpanSeconds) * plotRect.Width;
                            float y = plotRect.Bottom - (float)((temp - plotMinY) / (plotMaxY - plotMinY)) * plotRect.Height;
                            pts.Add(new PointF(x, Math.Max(plotRect.Top, Math.Min(plotRect.Bottom, y))));
                        }
                    }
                    if (pts.Count > 1)
                    {
                        drawnLineCount++;
                        singleDrawnCh = ch;
                        double curLatest = (ch < samples[samples.Count - 1].Value.Length) ? samples[samples.Count - 1].Value[ch] : 0.0;
                        singleDrawnTemp = curLatest;

                        Color c = (ch < chColors.Length) ? chColors[ch] : Color.DarkBlue;
                        using (Pen pCh = new Pen(c, 2.0f))
                        {
                            g.DrawLines(pCh, pts.ToArray());
                        }

                        // 繪製最新端點數值圓點
                        PointF lastPt = pts[pts.Count - 1];
                        using (Brush bDot = new SolidBrush(c))
                        {
                            g.FillEllipse(bDot, lastPt.X - 4f, lastPt.Y - 4f, 8, 8);
                        }

                        // 端點高對比膠囊徽章標籤 (例如 "CH1 (前軸承): 42.5℃")
                        double latestTemp = (ch < samples[samples.Count - 1].Value.Length) ? samples[samples.Count - 1].Value[ch] : 0.0;
                        string cName = (main != null && main.gl820ChannelNames != null && ch < main.gl820ChannelNames.Length && !string.IsNullOrEmpty(main.gl820ChannelNames[ch])) ? main.gl820ChannelNames[ch] : ("CH" + (ch + 1));
                        string tag = (cName != "CH" + (ch + 1))
                            ? string.Format("CH{0} ({1}) {2:F1}℃", ch + 1, cName, latestTemp)
                            : string.Format("CH{0} {1:F1}℃", ch + 1, latestTemp);

                        SizeF sz = g.MeasureString(tag, fontTag8B);
                        float tx = Math.Min(plotRect.Right - sz.Width - 8, Math.Max(plotRect.Left + 8, lastPt.X - sz.Width - 8));
                        float ty = Math.Max(plotRect.Top + 4, Math.Min(plotRect.Bottom - sz.Height - 4, lastPt.Y - sz.Height / 2));
                        RectangleF badgeRect = new RectangleF(tx, ty, sz.Width + 6, sz.Height + 2);

                        using (Brush bBg = new SolidBrush(Color.FromArgb(235, 255, 255, 255)))
                        using (Pen pBorder = new Pen(c, 1.5f))
                        using (Brush bText = new SolidBrush(c))
                        {
                            g.FillRectangle(bBg, badgeRect);
                            g.DrawRectangle(pBorder, badgeRect.X, badgeRect.Y, badgeRect.Width, badgeRect.Height);
                            g.DrawString(tag, fontTag8B, bText, tx + 3, ty + 1);
                        }
                    }
                }

                // 若只有單一條曲線，在圖表左上方強烈提示，清楚解釋是哪個 CH
                if (drawnLineCount == 1)
                {
                    string cName = (main != null && main.gl820ChannelNames != null && singleDrawnCh < main.gl820ChannelNames.Length && !string.IsNullOrEmpty(main.gl820ChannelNames[singleDrawnCh]))
                        ? main.gl820ChannelNames[singleDrawnCh] : ("CH" + (singleDrawnCh + 1));
                    string singleNotice = string.Format("📌 目前僅顯示單一活躍通道：CH{0} [{1}] 即時溫度: {2:F1} ℃ (其餘通道未接線或 ≤0℃)", singleDrawnCh + 1, cName, singleDrawnTemp);

                    SizeF szN = g.MeasureString(singleNotice, fontNotice9B);
                    RectangleF rectN = new RectangleF(plotRect.Left + 8, plotRect.Top + 6, szN.Width + 12, szN.Height + 6);
                    using (SolidBrush bBgN = new SolidBrush(Color.FromArgb(240, 254, 243, 199))) // 琥珀色提示底
                    using (Pen pBorderN = new Pen(Color.FromArgb(245, 158, 11), 1.2f))
                    using (SolidBrush bTextN = new SolidBrush(Color.FromArgb(146, 64, 14)))
                    {
                        g.FillRectangle(bBgN, rectN);
                        g.DrawRectangle(pBorderN, rectN.X, rectN.Y, rectN.Width, rectN.Height);
                        g.DrawString(singleNotice, fontNotice9B, bTextN, rectN.X + 6, rectN.Y + 3);
                    }
                }
                else if (drawnLineCount == 0)
                {
                    string noDataNotice = "⚠️ 監控通道目前無溫度訊號 (所有選取通道溫度值均 ≤ 0℃ 或未接線)";
                    SizeF szN = g.MeasureString(noDataNotice, fontNotice95B);
                    g.DrawString(noDataNotice, fontNotice95B, Brushes.Crimson, plotRect.Left + (plotRect.Width - szN.Width) / 2, plotRect.Top + (plotRect.Height - szN.Height) / 2);
                }
            }
            else
            {
                string hint = " GL820 20 通道溫度動態波形圖 (實測上下25%自適應中...)";
                SizeF sz = g.MeasureString(hint, fontNotice95B);
                g.DrawString(hint, fontNotice95B, Brushes.DarkGray, plotRect.Left + (plotRect.Width - sz.Width) / 2, plotRect.Top + (plotRect.Height - sz.Height) / 2);
            }

            // 底部圖例說明 (Legend)
            List<int> targetChannels = new List<int>();
            if (channelVisible != null)
            {
                for (int i = 0; i < Math.Min(20, channelVisible.Length); i++)
                {
                    if (channelVisible[i]) targetChannels.Add(i);
                }
            }
            else
            {
                for (int i = 0; i < 20; i++) targetChannels.Add(i);
            }

            if (channelVisible != null && targetChannels.Count <= 8)
            {
                // 空載精簡圖例：為每個選取的通道繪製清晰的狀態膠囊
                int legendY = plotRect.Bottom + 12;
                int curX = plotRect.Left;
                var latestSample = (samples.Count > 0) ? samples[samples.Count - 1].Value : null;

                for (int k = 0; k < targetChannels.Count; k++)
                {
                    int ch = targetChannels[k];
                    Color c = (ch < chColors.Length) ? chColors[ch] : Color.DarkBlue;
                    string cName = (main != null && main.gl820ChannelNames != null && ch < main.gl820ChannelNames.Length && !string.IsNullOrEmpty(main.gl820ChannelNames[ch]))
                        ? main.gl820ChannelNames[ch] : ("CH" + (ch + 1));
                    double temp = (latestSample != null && ch < latestSample.Length) ? latestSample[ch] : 0.0;
                    string tempStr = (temp > 0.0) ? string.Format("{0:F1}℃", temp) : "無訊號(0℃)";

                    string itemText = string.Format("CH{0} {1}: {2}", ch + 1, cName, tempStr);
                    SizeF szItem = g.MeasureString(itemText, fontLeg85B);
                    float boxWidth = szItem.Width + 24;

                    // 換行防裁切
                    if (curX + boxWidth > plotRect.Right && k > 0)
                    {
                        curX = plotRect.Left;
                        legendY += 22;
                    }

                    // 色塊圓點或方塊
                    using (Brush b = new SolidBrush(c))
                    {
                        g.FillEllipse(b, curX, legendY + 3, 10, 10);
                    }
                    Brush txtBrush = (temp > 0.0) ? Brushes.Black : Brushes.Gray;
                    g.DrawString(itemText, fontLeg85B, txtBrush, curX + 14, legendY);

                    curX += (int)boxWidth + 12;
                }
            }
            else
            {
                // 完整 20 通道圖例 (GBD Tab 全通道總覽)
                int startX = plotRect.Left;
                for (int ch = 0; ch < 20; ch++)
                {
                    int row = ch / 10;
                    int col = ch % 10;
                    int colWidth = Math.Max(50, plotRect.Width / 10);
                    int lx = startX + col * colWidth;
                    int ly = plotRect.Bottom + 16 + row * 18;

                    bool visible = (channelVisible == null || ch >= channelVisible.Length || channelVisible[ch]);
                    Color c = visible ? ((ch < chColors.Length) ? chColors[ch] : Color.DarkBlue) : Color.LightGray;
                    using (Brush b = new SolidBrush(c))
                    {
                        g.FillRectangle(b, lx, ly + 2, 10, 10);
                    }

                    string name = (main != null && main.gl820ChannelNames != null && ch < main.gl820ChannelNames.Length && !string.IsNullOrEmpty(main.gl820ChannelNames[ch]))
                        ? main.gl820ChannelNames[ch]
                        : ("CH" + (ch + 1));
                    if (name.Length > 7) name = name.Substring(0, 6) + "..";
                    Brush txtBrush = visible ? Brushes.Black : Brushes.LightGray;
                    g.DrawString(name, fontLeg75B, txtBrush, lx + 12, ly);
                }
            }
            }
            catch { }
        }
    }

    // =========================================================================
    //  介面與字體大小獨立客製化調整對話框 (Font Settings Dialog)
    // =========================================================================
    public class FontSettingsDialog : Form
    {
        private MainForm main;
        private NumericUpDown numVal, numTitle, numTelem, numKeb;

        public FontSettingsDialog(MainForm parent)
        {
            this.main = parent;
            this.Text = "【綜合監控】主畫面字體與文字大小自訂設定";
            this.Size = new Size(540, 420);
            this.StartPosition = FormStartPosition.CenterParent;
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.Font = new Font("微軟正黑體", 9.5f);
            this.BackColor = Color.FromArgb(248, 250, 252);

            TableLayoutPanel table = new TableLayoutPanel()
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 3,
                Padding = new Padding(12)
            };
            table.RowStyles.Add(new RowStyle(SizeType.Absolute, 40f));
            table.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            table.RowStyles.Add(new RowStyle(SizeType.Absolute, 55f));

            Label lblHdr = new Label()
            {
                Text = " 您可單獨調整【綜合監控主畫面】各區塊文字大小，點擊【 即時套用】預覽：",
                Font = new Font("微軟正黑體", 9.5f, FontStyle.Bold),
                ForeColor = Color.FromArgb(15, 23, 42),
                Dock = DockStyle.Fill
            };
            table.Controls.Add(lblHdr, 0, 0);

            GroupBox grp = new GroupBox()
            {
                Text = " 綜合監控主畫面各顯示區塊字體大小 (點數 pt) ",
                Dock = DockStyle.Fill,
                Font = new Font("微軟正黑體", 9.5f, FontStyle.Bold),
                Padding = new Padding(12)
            };

            TableLayoutPanel formGrid = new TableLayoutPanel()
            {
                Dock = DockStyle.Fill,
                ColumnCount = 3,
                RowCount = 4
            };
            formGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 55f));
            formGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 85f));
            formGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 70f));
            for (int i = 0; i < 4; i++) formGrid.RowStyles.Add(new RowStyle(SizeType.Percent, 25f));

            numVal = AddFontRow(formGrid, "1. 頂部 6 大核心量測數值 (轉速/扭矩/功率/效率)", 10, 36, (decimal)main.fontMetricValPt, 0);
            numTitle = AddFontRow(formGrid, "2. 頂部 6 大核心指標卡片標題", 8, 20, (decimal)main.fontMetricTitlePt, 1);
            numKeb = AddFontRow(formGrid, "3. A/B 載台控制面板按鈕與參數表格字體", 7, 18, (decimal)main.fontKebRuGridPt, 2);
            numTelem = AddFontRow(formGrid, "4. 橫河 WT333E 三相電氣遙測表格字體", 7, 20, (decimal)main.fontTelemetryGridPt, 3);

            grp.Controls.Add(formGrid);
            table.Controls.Add(grp, 0, 1);

            Panel pnlBtns = new Panel() { Dock = DockStyle.Fill };
            Button btnReset = new Button()
            {
                Text = " 恢復預設",
                Location = new Point(5, 10),
                Size = new Size(100, 34),
                BackColor = Color.FromArgb(226, 232, 240),
                Font = new Font("微軟正黑體", 9.5f)
            };
            btnReset.Click += (s, e) => {
                numVal.Value = 18m;
                numTitle.Value = 10.5m;
                numKeb.Value = 8.5m;
                numTelem.Value = 9.5m;
                ApplyValues();
            };

            Button btnApply = new Button()
            {
                Text = " 即時套用",
                Location = new Point(235, 10),
                Size = new Size(115, 34),
                BackColor = Color.FromArgb(0, 180, 216),
                ForeColor = Color.White,
                Font = new Font("微軟正黑體", 9.5f, FontStyle.Bold)
            };
            btnApply.Click += (s, e) => ApplyValues();

            Button btnSave = new Button()
            {
                Text = " 儲存並關閉",
                Location = new Point(365, 10),
                Size = new Size(130, 34),
                BackColor = Color.FromArgb(16, 185, 129),
                ForeColor = Color.White,
                Font = new Font("微軟正黑體", 9.5f, FontStyle.Bold)
            };
            btnSave.Click += (s, e) => {
                ApplyValues();
                this.Close();
            };

            pnlBtns.Controls.AddRange(new Control[] { btnReset, btnApply, btnSave });
            table.Controls.Add(pnlBtns, 0, 2);

            this.Controls.Add(table);
        }

        private NumericUpDown AddFontRow(TableLayoutPanel grid, string label, int min, int max, decimal val, int row)
        {
            Label lbl = new Label()
            {
                Text = label,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                Font = new Font("微軟正黑體", 9f, FontStyle.Regular)
            };
            NumericUpDown num = new NumericUpDown()
            {
                Minimum = min,
                Maximum = max,
                DecimalPlaces = 1,
                Increment = 0.5m,
                Value = Math.Max(min, Math.Min(max, val)),
                Dock = DockStyle.Fill,
                Font = new Font("Consolas", 10f, FontStyle.Bold)
            };
            num.ValueChanged += (s, e) => ApplyValues();
            Label lblUnit = new Label()
            {
                Text = "pt (級)",
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                ForeColor = Color.Gray
            };

            grid.Controls.Add(lbl, 0, row);
            grid.Controls.Add(num, 1, row);
            grid.Controls.Add(lblUnit, 2, row);
            return num;
        }

        private void ApplyValues()
        {
            if (main == null) return;
            main.fontMetricValPt = (float)numVal.Value;
            main.fontMetricTitlePt = (float)numTitle.Value;
            main.fontTelemetryGridPt = (float)numTelem.Value;
            main.fontKebRuGridPt = (float)numKeb.Value;
            main.ApplyFontSizes();
        }
    }

    public class DeviceSettingsDialog : Form
    {
        private MainForm main;
        private ComboBox cmbTorquePort, cmbTorqueBaud;
        private TextBox txtPowerIp;
        private NumericUpDown numPowerPort;
        private TextBox txtGbdIp;
        private NumericUpDown numGbdPort;

        public DeviceSettingsDialog(MainForm mainForm)
        {
            this.main = mainForm;
            this.Text = " 設備通訊設定 (扭力計 / 電表 / 溫度計)";
            this.Size = new Size(540, 440);
            this.StartPosition = FormStartPosition.CenterParent;
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.BackColor = Color.FromArgb(248, 250, 252);
            this.Font = new Font("微軟正黑體", 9.5f, FontStyle.Regular);

            TableLayoutPanel root = new TableLayoutPanel()
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 5,
                Padding = new Padding(14)
            };
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 32f));  // 頂部說明
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 105f)); // 扭力計
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 105f)); // WT333E
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 105f)); // GL820
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 45f));  // 按鈕

            Label lblTitle = new Label()
            {
                Text = " 周邊量測儀器連線參數設定 (自動記憶)",
                Font = new Font("微軟正黑體", 11f, FontStyle.Bold),
                ForeColor = Color.FromArgb(15, 23, 42),
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft
            };
            root.Controls.Add(lblTitle, 0, 0);

            // 1. 扭力計 Group
            GroupBox grpTorque = new GroupBox()
            {
                Text = " Kistler 4700B 扭力計 (RS-232 / USB 串列埠)",
                Dock = DockStyle.Fill,
                Font = new Font("微軟正黑體", 9.5f, FontStyle.Bold),
                ForeColor = Color.FromArgb(30, 41, 59)
            };
            TableLayoutPanel pnlTorque = new TableLayoutPanel() { Dock = DockStyle.Fill, ColumnCount = 4, RowCount = 1, Padding = new Padding(6, 12, 6, 6) };
            pnlTorque.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 85f));
            pnlTorque.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
            pnlTorque.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 70f));
            pnlTorque.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));

            Label lT1 = new Label() { Text = "COM 埠:", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleRight, Font = new Font("微軟正黑體", 9f) };
            cmbTorquePort = CreatePortCombo(main.torquePortName);
            Label lT2 = new Label() { Text = "鮑率:", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleRight, Font = new Font("微軟正黑體", 9f) };
            cmbTorqueBaud = CreateBaudCombo(main.torqueBaudRate, new object[] { "9600", "19200", "38400", "57600", "115200", "1000000" });

            pnlTorque.Controls.AddRange(new Control[] { lT1, cmbTorquePort, lT2, cmbTorqueBaud });
            grpTorque.Controls.Add(pnlTorque);
            root.Controls.Add(grpTorque, 0, 1);

            // 2. WT333E Group
            GroupBox grpPower = new GroupBox()
            {
                Text = " 橫河 WT333E 功率分析儀 (Modbus TCP 乙太網路)",
                Dock = DockStyle.Fill,
                Font = new Font("微軟正黑體", 9.5f, FontStyle.Bold),
                ForeColor = Color.FromArgb(30, 41, 59)
            };
            TableLayoutPanel pnlPower = new TableLayoutPanel() { Dock = DockStyle.Fill, ColumnCount = 4, RowCount = 1, Padding = new Padding(6, 12, 6, 6) };
            pnlPower.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 85f));
            pnlPower.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
            pnlPower.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 70f));
            pnlPower.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));

            Label lP1 = new Label() { Text = "網路 IP:", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleRight, Font = new Font("微軟正黑體", 9f) };
            txtPowerIp = new TextBox() { Text = main.powerMeterIp, Dock = DockStyle.Fill, Font = new Font("Consolas", 10f, FontStyle.Bold) };
            Label lP2 = new Label() { Text = "TCP Port:", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleRight, Font = new Font("微軟正黑體", 9f) };
            numPowerPort = new NumericUpDown() { Minimum = 1, Maximum = 65535, Value = main.powerMeterPort, Dock = DockStyle.Fill, Font = new Font("Consolas", 10f, FontStyle.Bold) };

            pnlPower.Controls.AddRange(new Control[] { lP1, txtPowerIp, lP2, numPowerPort });
            grpPower.Controls.Add(pnlPower);
            root.Controls.Add(grpPower, 0, 2);

            // 3. GL820 Group
            GroupBox grpGbd = new GroupBox()
            {
                Text = " Graphtec GL820 溫度記錄器 (TCP 乙太網路)",
                Dock = DockStyle.Fill,
                Font = new Font("微軟正黑體", 9.5f, FontStyle.Bold),
                ForeColor = Color.FromArgb(30, 41, 59)
            };
            TableLayoutPanel pnlGbd = new TableLayoutPanel() { Dock = DockStyle.Fill, ColumnCount = 4, RowCount = 1, Padding = new Padding(6, 12, 6, 6) };
            pnlGbd.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 85f));
            pnlGbd.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
            pnlGbd.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 70f));
            pnlGbd.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));

            Label lG1 = new Label() { Text = "網路 IP:", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleRight, Font = new Font("微軟正黑體", 9f) };
            txtGbdIp = new TextBox() { Text = main.gbdIp, Dock = DockStyle.Fill, Font = new Font("Consolas", 10f, FontStyle.Bold) };
            Label lG2 = new Label() { Text = "TCP Port:", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleRight, Font = new Font("微軟正黑體", 9f) };
            numGbdPort = new NumericUpDown() { Minimum = 1, Maximum = 65535, Value = main.gbdPort, Dock = DockStyle.Fill, Font = new Font("Consolas", 10f, FontStyle.Bold) };

            pnlGbd.Controls.AddRange(new Control[] { lG1, txtGbdIp, lG2, numGbdPort });
            grpGbd.Controls.Add(pnlGbd);
            root.Controls.Add(grpGbd, 0, 3);

            // 4. 底部操作按鈕列
            Panel pnlBtns = new Panel() { Dock = DockStyle.Fill };
            Button btnSave = new Button()
            {
                Text = " 儲存並套用設定",
                Location = new Point(235, 6),
                Size = new Size(160, 34),
                BackColor = Color.FromArgb(16, 185, 129),
                ForeColor = Color.White,
                Font = new Font("微軟正黑體", 9.5f, FontStyle.Bold),
                Cursor = Cursors.Hand
            };
            btnSave.Click += (s, e) => {
                SaveAndApply();
                this.Close();
            };

            Button btnCancel = new Button()
            {
                Text = "[FAIL] 取消",
                Location = new Point(405, 6),
                Size = new Size(95, 34),
                BackColor = Color.FromArgb(226, 232, 240),
                Font = new Font("微軟正黑體", 9.5f),
                Cursor = Cursors.Hand
            };
            btnCancel.Click += (s, e) => this.Close();

            pnlBtns.Controls.AddRange(new Control[] { btnSave, btnCancel });
            root.Controls.Add(pnlBtns, 0, 4);

            this.Controls.Add(root);
        }

        private ComboBox CreatePortCombo(string selectedPort)
        {
            ComboBox cmb = new ComboBox() { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList, Font = new Font("Consolas", 10f, FontStyle.Bold) };
            List<string> ports = new List<string>(SerialPort.GetPortNames());
            for (int i = 1; i <= 16; i++)
            {
                string pName = "COM" + i;
                if (!ports.Contains(pName)) ports.Add(pName);
            }
            ports.Sort();
            cmb.Items.AddRange(ports.ToArray());
            if (cmb.Items.Contains(selectedPort)) cmb.SelectedItem = selectedPort;
            else if (cmb.Items.Count > 0) cmb.SelectedIndex = 0;
            return cmb;
        }

        private ComboBox CreateBaudCombo(int selectedBaud, object[] items)
        {
            ComboBox cmb = new ComboBox() { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList, Font = new Font("Consolas", 10f, FontStyle.Bold) };
            cmb.Items.AddRange(items);
            string sBaud = selectedBaud.ToString();
            if (cmb.Items.Contains(sBaud)) cmb.SelectedItem = sBaud;
            else if (cmb.Items.Count > 0) cmb.SelectedIndex = 0;
            return cmb;
        }

        private void SaveAndApply()
        {
            if (main == null) return;
            if (cmbTorquePort.SelectedItem != null) main.torquePortName = cmbTorquePort.SelectedItem.ToString();
            int tBaud;
            if (cmbTorqueBaud.SelectedItem != null && int.TryParse(cmbTorqueBaud.SelectedItem.ToString(), out tBaud))
                main.torqueBaudRate = tBaud;

            main.powerMeterIp = txtPowerIp.Text.Trim();
            main.powerMeterPort = (int)numPowerPort.Value;
            main.gbdIp = txtGbdIp.Text.Trim();
            main.gbdPort = (int)numGbdPort.Value;

            main.ApplyDeviceSettings();
        }
    }

    // =========================================================================
    //  馬達溫度 1 秒 (1Sec) 即時動態波形趨勢圖控制項
    // =========================================================================
    public class MotorTempTrendControl : Control
    {
        private readonly List<KeyValuePair<DateTime, double>> samples = new List<KeyValuePair<DateTime, double>>();
        public bool IsConnected = false;

        private FlowLayoutPanel pnlTimeSpan;
        private Button btnTimeMinus;
        private Button btnTimePlus;
        private Label lblTimeSpan;

        private static readonly Font fTime75 = new Font("微軟正黑體", 7.5f);
        private static readonly Font fWarn115B = new Font("微軟正黑體", 11.5f, FontStyle.Bold);
        private static readonly Font fSub9 = new Font("微軟正黑體", 9f, FontStyle.Regular);
        private static readonly Font fFootOffline75B = new Font("微軟正黑體", 7.5f, FontStyle.Bold);
        private static readonly Font fHint9 = new Font("微軟正黑體", 9f);
        private static readonly Font fScale8 = new Font("Consolas", 8f);

        private static readonly int[] timeSpanSteps = new int[] { 30, 60, 120, 300, 600, 1800, 3600 };
        private static readonly string[] timeSpanNames = new string[] { "30秒", "1分鐘", "2分鐘", "5分鐘", "10分鐘", "30分鐘", "1小時" };
        private int currentTimeSpanIndex = 3; // 預設 5 分鐘 (300 秒)
        public int CurrentTimeSpanSeconds { get { return timeSpanSteps[currentTimeSpanIndex]; } }

        public MotorTempTrendControl()
        {
            this.DoubleBuffered = true;
            this.BackColor = Color.White;
            InitTimeSpanToolbar();
        }

        private void InitTimeSpanToolbar()
        {
            pnlTimeSpan = new FlowLayoutPanel()
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                FlowDirection = FlowDirection.LeftToRight,
                BackColor = Color.FromArgb(248, 250, 252),
                Margin = new Padding(0),
                Padding = new Padding(2)
            };

            btnTimeMinus = new Button()
            {
                Text = "➖",
                Size = new Size(24, 20),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.White,
                Cursor = Cursors.Hand,
                Font = new Font("微軟正黑體", 7.5f)
            };
            btnTimeMinus.FlatAppearance.BorderColor = Color.FromArgb(203, 213, 225);
            btnTimeMinus.Click += (s, e) => {
                if (currentTimeSpanIndex > 0)
                {
                    currentTimeSpanIndex--;
                    UpdateTimeSpanLabel();
                    this.Invalidate();
                }
            };

            lblTimeSpan = new Label()
            {
                Text = "⏱️ " + timeSpanNames[currentTimeSpanIndex],
                AutoSize = true,
                Font = new Font("微軟正黑體", 8f, FontStyle.Bold),
                ForeColor = Color.FromArgb(71, 85, 105),
                Cursor = Cursors.Hand,
                Margin = new Padding(2, 3, 2, 0)
            };
            lblTimeSpan.Click += (s, e) => {
                currentTimeSpanIndex = (currentTimeSpanIndex + 1) % timeSpanSteps.Length;
                UpdateTimeSpanLabel();
                this.Invalidate();
            };

            btnTimePlus = new Button()
            {
                Text = "➕",
                Size = new Size(24, 20),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.White,
                Cursor = Cursors.Hand,
                Font = new Font("微軟正黑體", 7.5f)
            };
            btnTimePlus.FlatAppearance.BorderColor = Color.FromArgb(203, 213, 225);
            btnTimePlus.Click += (s, e) => {
                if (currentTimeSpanIndex < timeSpanSteps.Length - 1)
                {
                    currentTimeSpanIndex++;
                    UpdateTimeSpanLabel();
                    this.Invalidate();
                }
            };

            pnlTimeSpan.Controls.AddRange(new Control[] { btnTimeMinus, lblTimeSpan, btnTimePlus });
            this.Controls.Add(pnlTimeSpan);
            PositionTimeSpanToolbar();
        }

        private void UpdateTimeSpanLabel()
        {
            if (lblTimeSpan != null)
            {
                lblTimeSpan.Text = "⏱️ " + timeSpanNames[currentTimeSpanIndex];
                PositionTimeSpanToolbar();
            }
        }

        private void PositionTimeSpanToolbar()
        {
            if (pnlTimeSpan != null)
            {
                pnlTimeSpan.Location = new Point(Math.Max(10, this.Width - pnlTimeSpan.PreferredSize.Width - 16), 2);
                pnlTimeSpan.BringToFront();
            }
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            PositionTimeSpanToolbar();
        }

        public void AddSample(DateTime time, double temp)
        {
            if (!IsConnected || temp <= 0.0) return;
            samples.Add(new KeyValuePair<DateTime, double>(time, temp));
            // 長時間運行記憶體防護：批次修剪取代頻繁 RemoveAt(0)
            if (samples.Count > 3600 + 120)
            {
                samples.RemoveRange(0, 120);
            }
            this.Invalidate();
        }

        public void ClearData()
        {
            samples.Clear();
            samples.TrimExcess(); // 主動釋放內部陣列容量
            this.Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            try
            {
                Graphics g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;

                // 左側留 48px 放溫度刻度，頂部留 24px 放調節列，底部留 34px 放時間標籤與狀態
                Rectangle plotRect = new Rectangle(48, 24, this.Width - 62, Math.Max(40, this.Height - 58));
                if (plotRect.Width <= 10 || plotRect.Height <= 10) return;

                // 背景
                g.FillRectangle(Brushes.White, plotRect);

            // 找出可見區間內的資料點
            DateTime now = (samples.Count > 0) ? samples[samples.Count - 1].Key : DateTime.Now;
            DateTime startTime = now.AddSeconds(-CurrentTimeSpanSeconds);

            List<KeyValuePair<DateTime, double>> visiblePts = new List<KeyValuePair<DateTime, double>>();
            double minTemp = double.MaxValue;
            double maxTemp = double.MinValue;

            for (int i = 0; i < samples.Count; i++)
            {
                var pt = samples[i];
                if (pt.Key >= startTime && pt.Value > 0.0)
                {
                    visiblePts.Add(pt);
                    if (pt.Value < minTemp) minTemp = pt.Value;
                    if (pt.Value > maxTemp) maxTemp = pt.Value;
                }
            }

            // 上下 25% 自適應刻度 (若無數據或溫差微弱則保持預設 20~80℃ 或 min-5 ~ max+5)
            double plotMinY = 20.0, plotMaxY = 80.0;
            if (visiblePts.Count > 0 && minTemp != double.MaxValue)
            {
                double span = maxTemp - minTemp;
                if (span < 2.0)
                {
                    plotMinY = Math.Max(0.0, minTemp - 5.0);
                    plotMaxY = maxTemp + 5.0;
                }
                else
                {
                    double margin = span * 0.25; // 測定值上下各 25% 餘裕自適應
                    plotMinY = Math.Max(0.0, minTemp - margin);
                    plotMaxY = maxTemp + margin;
                }
            }
            if (plotMaxY <= plotMinY) plotMaxY = plotMinY + 10.0;

            // 網格與 Y 軸刻度
            using (Pen pGrid = new Pen(Color.FromArgb(240, 243, 246), 1f))
            {
                int gridSteps = 5;
                for (int i = 0; i <= gridSteps; i++)
                {
                    float y = plotRect.Top + i * (plotRect.Height / (float)gridSteps);
                    g.DrawLine(pGrid, plotRect.Left, y, plotRect.Right, y);
                    double tempVal = plotMaxY - i * ((plotMaxY - plotMinY) / gridSteps);
                    string tStr = (plotMaxY - plotMinY < 25.0) ? string.Format("{0:F1}°C", tempVal) : string.Format("{0:F0}°C", tempVal);
                    g.DrawString(tStr, fScale8, Brushes.Gray, 2, y - 6);
                }
                int xSteps = 5;
                for (int i = 0; i <= xSteps; i++)
                {
                    float x = plotRect.Left + i * (plotRect.Width / (float)xSteps);
                    g.DrawLine(pGrid, x, plotRect.Top, x, plotRect.Bottom);
                    int secFromNow = (xSteps - i) * (CurrentTimeSpanSeconds / xSteps);
                    string timeStr = (secFromNow == 0) ? "現在" : (secFromNow >= 60 ? string.Format("-{0}m", secFromNow / 60) : string.Format("-{0}s", secFromNow));
                    g.DrawString(timeStr, fTime75, Brushes.Gray, x - 10, plotRect.Bottom + 2);
                }
            }

            g.DrawRectangle(Pens.LightGray, plotRect);

            if (!IsConnected)
            {
                using (SolidBrush bBg = new SolidBrush(Color.FromArgb(248, 250, 252)))
                {
                    g.FillRectangle(bBg, plotRect);
                }
                string warnText = "⚠️ 設備未連線";
                string subText = "Graphtec 溫度記錄器離線 (無即時數據)";
                SizeF sz1 = g.MeasureString(warnText, fWarn115B);
                SizeF sz2 = g.MeasureString(subText, fSub9);
                float cy = plotRect.Top + (plotRect.Height - (sz1.Height + sz2.Height + 6)) / 2;
                g.DrawString(warnText, fWarn115B, Brushes.Crimson, plotRect.Left + (plotRect.Width - sz1.Width) / 2, cy);
                g.DrawString(subText, fSub9, Brushes.Gray, plotRect.Left + (plotRect.Width - sz2.Width) / 2, cy + sz1.Height + 6);
                string footerOffline = " 設備狀態: 🔴 設備未連線";
                g.DrawString(footerOffline, fFootOffline75B, Brushes.Crimson, plotRect.Left, plotRect.Bottom + 16);
                return;
            }

            // 繪製動態曲線
            if (visiblePts.Count > 1)
            {
                int step = (visiblePts.Count > plotRect.Width / 2 && plotRect.Width > 0) ? Math.Max(1, visiblePts.Count / Math.Max(100, plotRect.Width / 2)) : 1;
                List<PointF> pts = new List<PointF>(Math.Min(visiblePts.Count + 2, 500));
                for (int i = 0; i < visiblePts.Count; i += step)
                {
                    var pt = visiblePts[i];
                    double secOffset = (pt.Key - startTime).TotalSeconds;
                    float x = plotRect.Left + (float)(Math.Max(0.0, Math.Min(CurrentTimeSpanSeconds, secOffset)) / CurrentTimeSpanSeconds) * plotRect.Width;
                    float y = plotRect.Bottom - (float)((pt.Value - plotMinY) / (plotMaxY - plotMinY)) * plotRect.Height;
                    pts.Add(new PointF(x, Math.Max(plotRect.Top, Math.Min(plotRect.Bottom, y))));
                }
                // 保證納入最後一個最新端點
                if ((visiblePts.Count - 1) % step != 0)
                {
                    var pt = visiblePts[visiblePts.Count - 1];
                    double secOffset = (pt.Key - startTime).TotalSeconds;
                    float x = plotRect.Left + (float)(Math.Max(0.0, Math.Min(CurrentTimeSpanSeconds, secOffset)) / CurrentTimeSpanSeconds) * plotRect.Width;
                    float y = plotRect.Bottom - (float)((pt.Value - plotMinY) / (plotMaxY - plotMinY)) * plotRect.Height;
                    pts.Add(new PointF(x, Math.Max(plotRect.Top, Math.Min(plotRect.Bottom, y))));
                }

                // 漸層填色
                using (GraphicsPath path = new GraphicsPath())
                {
                    path.AddLine(pts[0].X, plotRect.Bottom, pts[0].X, pts[0].Y);
                    for (int i = 1; i < pts.Count; i++) path.AddLine(pts[i - 1], pts[i]);
                    path.AddLine(pts[pts.Count - 1].X, pts[pts.Count - 1].Y, pts[pts.Count - 1].X, plotRect.Bottom);
                    path.CloseFigure();
                    using (LinearGradientBrush lgb = new LinearGradientBrush(plotRect, Color.FromArgb(45, 239, 68, 68), Color.FromArgb(5, 239, 68, 68), LinearGradientMode.Vertical))
                    {
                        g.FillPath(lgb, path);
                    }
                }

                // 趨勢主線
                using (Pen pCurve = new Pen(Color.FromArgb(239, 68, 68), 2.2f))
                {
                    g.DrawLines(pCurve, pts.ToArray());
                }

                // 最新溫度點
                PointF lastPt = pts[pts.Count - 1];
                g.FillEllipse(Brushes.Red, lastPt.X - 3.5f, lastPt.Y - 3.5f, 7, 7);
                g.DrawEllipse(Pens.White, lastPt.X - 3.5f, lastPt.Y - 3.5f, 7, 7);
            }
            else
            {
                string hint = " 馬達溫度即時趨勢圖 (實測上下25%自適應繪製中...)";
                SizeF sz = g.MeasureString(hint, fHint9);
                g.DrawString(hint, fHint9, Brushes.DarkGray, plotRect.Left + (plotRect.Width - sz.Width) / 2, plotRect.Top + (plotRect.Height - sz.Height) / 2);
            }

            // 底部說明文字
            string footer = string.Format(" 時間跨度: {0} | 溫度範圍: {1:F1} ~ {2:F1} °C (實測上下25%自適應)", 
                timeSpanNames[currentTimeSpanIndex], plotMinY, plotMaxY);
            g.DrawString(footer, fTime75, Brushes.Gray, plotRect.Left, plotRect.Bottom + 16);
            }
            catch { }
        }
    }

    // =========================================================================
    //  轉矩與速度 雙軸即時動態響應曲線圖控制項 (Torque & Speed Real-time Dynamic Chart)
    // =========================================================================
    public class TorqueSpeedTrendControl : Control
    {
        public class SamplePoint
        {
            public DateTime Time;
            public double ActTorque;
            public double TgtTorque;
            public double ActSpeed;
            public double TgtSpeed;
            public bool IsLocked;
        }

        private readonly List<SamplePoint> samples = new List<SamplePoint>();
        private readonly object lockObj = new object();
        public bool IsPaused { get; set; }
        public int MaxPoints { get; set; }
        public int ScaleMode { get; set; } // 0: Auto (自適應), 1: 10Nm/500rpm, 2: 30Nm/1000rpm, 3: 60Nm/2000rpm, 4: 150Nm/3500rpm, 5: 自訂
        public double CustomMaxTrq { get; set; }
        public double CustomMaxSpd { get; set; }
        public bool AlignSpeedSign { get; set; }

        private static readonly Font fHint9 = new Font("微軟正黑體", 9f);
        private static readonly Font fFoot8 = new Font("微軟正黑體", 8f);
        private static readonly Font fLegend8B = new Font("微軟正黑體", 8f, FontStyle.Bold);
        private static readonly Font fScale75 = new Font("Consolas", 7.5f);

        private FlowLayoutPanel pnlPointsToolbar;
        private Button btnPointsMinus;
        private Button btnPointsPlus;
        private Label lblPointsSpan;

        private static readonly int[] pointsSteps = new int[] { 50, 100, 200, 500, 1000, 2000 };
        private static readonly string[] pointsNames = new string[] { "50點 (5秒)", "100點 (10秒)", "200點 (20秒)", "500點 (50秒)", "1000點 (1.5分)", "2000點 (3.3分)" };
        private int currentPointsIndex = 2; // 預設 200 點

        public TorqueSpeedTrendControl()
        {
            this.DoubleBuffered = true;
            this.BackColor = Color.White;
            this.MaxPoints = 200;
            this.ScaleMode = 0; // 預設 0: 智慧自適應
            this.CustomMaxTrq = 50.0;
            this.CustomMaxSpd = 1500.0;
            this.AlignSpeedSign = true;
            InitPointsToolbar();
        }

        private void InitPointsToolbar()
        {
            pnlPointsToolbar = new FlowLayoutPanel()
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                FlowDirection = FlowDirection.LeftToRight,
                BackColor = Color.FromArgb(248, 250, 252),
                Margin = new Padding(0),
                Padding = new Padding(2)
            };

            btnPointsMinus = new Button()
            {
                Text = "➖",
                Size = new Size(24, 20),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.White,
                Cursor = Cursors.Hand,
                Font = new Font("微軟正黑體", 7.5f)
            };
            btnPointsMinus.FlatAppearance.BorderColor = Color.FromArgb(203, 213, 225);
            btnPointsMinus.Click += (s, e) => {
                if (currentPointsIndex > 0)
                {
                    currentPointsIndex--;
                    this.MaxPoints = pointsSteps[currentPointsIndex];
                    UpdatePointsSpanLabel();
                    this.Invalidate();
                }
            };

            lblPointsSpan = new Label()
            {
                Text = "⏱️ " + pointsNames[currentPointsIndex],
                AutoSize = true,
                Font = new Font("微軟正黑體", 8f, FontStyle.Bold),
                ForeColor = Color.FromArgb(71, 85, 105),
                Cursor = Cursors.Hand,
                Margin = new Padding(2, 3, 2, 0)
            };
            lblPointsSpan.Click += (s, e) => {
                currentPointsIndex = (currentPointsIndex + 1) % pointsSteps.Length;
                this.MaxPoints = pointsSteps[currentPointsIndex];
                UpdatePointsSpanLabel();
                this.Invalidate();
            };

            btnPointsPlus = new Button()
            {
                Text = "➕",
                Size = new Size(24, 20),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.White,
                Cursor = Cursors.Hand,
                Font = new Font("微軟正黑體", 7.5f)
            };
            btnPointsPlus.FlatAppearance.BorderColor = Color.FromArgb(203, 213, 225);
            btnPointsPlus.Click += (s, e) => {
                if (currentPointsIndex < pointsSteps.Length - 1)
                {
                    currentPointsIndex++;
                    this.MaxPoints = pointsSteps[currentPointsIndex];
                    UpdatePointsSpanLabel();
                    this.Invalidate();
                }
            };

            pnlPointsToolbar.Controls.AddRange(new Control[] { btnPointsMinus, lblPointsSpan, btnPointsPlus });
            this.Controls.Add(pnlPointsToolbar);
            PositionPointsToolbar();
        }

        private void UpdatePointsSpanLabel()
        {
            if (lblPointsSpan != null)
            {
                lblPointsSpan.Text = "⏱️ " + pointsNames[currentPointsIndex];
                PositionPointsToolbar();
            }
        }

        private void PositionPointsToolbar()
        {
            if (pnlPointsToolbar != null)
            {
                pnlPointsToolbar.Location = new Point(Math.Max(10, this.Width - pnlPointsToolbar.PreferredSize.Width - 16), 2);
                pnlPointsToolbar.BringToFront();
            }
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            PositionPointsToolbar();
        }

        public void AddSample(DateTime time, double actTrq, double tgtTrq, double actSpd, double tgtSpd, bool isLocked)
        {
            if (IsPaused) return;
            lock (lockObj)
            {
                samples.Add(new SamplePoint
                {
                    Time = time,
                    ActTorque = Math.Abs(actTrq),
                    TgtTorque = Math.Abs(tgtTrq),
                    ActSpeed = Math.Abs(actSpd),
                    TgtSpeed = Math.Abs(tgtSpd),
                    IsLocked = isLocked
                });
                // 長時運轉批次修剪，避免連續呼叫 RemoveAt(0)
                if (samples.Count > MaxPoints + 30)
                {
                    samples.RemoveRange(0, samples.Count - MaxPoints);
                }
            }
            this.Invalidate();
        }

        public void ClearData()
        {
            lock (lockObj)
            {
                samples.Clear();
                samples.TrimExcess(); // 主動釋放內部陣列容量
            }
            this.Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            try
            {
                Graphics g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;

                // 左邊留 50px 放轉矩刻度，右邊留 68px 放轉速刻度(防止單位rpm被裁切)，頂部留 24px 放圖例，底部留 18px 放狀態列
                Rectangle plotRect = new Rectangle(50, 24, Math.Max(20, this.Width - 118), Math.Max(20, this.Height - 42));
                if (plotRect.Width <= 20 || plotRect.Height <= 20) return;

                // 背景
                g.FillRectangle(Brushes.White, plotRect);

            // 複製樣本以防跨執行緒競爭
            List<SamplePoint> ptsCopy;
            lock (lockObj)
            {
                ptsCopy = new List<SamplePoint>(samples);
            }

            // 動態計算量程 (工控 HMI 核心鐵律：Y 軸一律以 0 為原點基準，視覺線條與刻度 100% 吻合)
            double minTrq = 0.0, maxTrq = 10.0;
            double minSpd = 0.0, maxSpd = 500.0;

            if (ScaleMode == 1) { minTrq = 0.0; maxTrq = 10.0; minSpd = 0.0; maxSpd = 500.0; }
            else if (ScaleMode == 2) { minTrq = 0.0; maxTrq = 30.0; minSpd = 0.0; maxSpd = 1000.0; }
            else if (ScaleMode == 3) { minTrq = 0.0; maxTrq = 60.0; minSpd = 0.0; maxSpd = 2000.0; }
            else if (ScaleMode == 4) { minTrq = 0.0; maxTrq = 150.0; minSpd = 0.0; maxSpd = 3500.0; }
            else if (ScaleMode == 5)
            {
                double cTrq = Math.Max(1.0, CustomMaxTrq);
                double cSpd = Math.Max(50.0, CustomMaxSpd);
                minTrq = 0.0; maxTrq = cTrq;
                minSpd = 0.0; maxSpd = cSpd;
            }
            else // ScaleMode == 0 (🌟 智慧自動適應 Auto: 底標一律固定為 0，上限依實測與目標自動擴展至優雅整數階梯)
            {
                double rawMaxT = 0.0;
                double rawMaxS = 0.0;

                for (int i = 0; i < ptsCopy.Count; i++)
                {
                    var p = ptsCopy[i];
                    double aT = Math.Abs(p.ActTorque);
                    double tT = Math.Abs(p.TgtTorque);
                    if (aT > rawMaxT) rawMaxT = aT;
                    if (tT > rawMaxT) rawMaxT = tT;

                    double aS = Math.Abs(p.ActSpeed);
                    double tS = Math.Abs(p.TgtSpeed);
                    if (aS > rawMaxS) rawMaxS = aS;
                    if (tS > rawMaxS) rawMaxS = tS;
                }

                minTrq = 0.0;
                minSpd = 0.0;

                // 轉矩自適應上限取整 (留 20% 餘裕，對齊 10, 20, 30, 50, 100 Nm 階梯)
                if (rawMaxT <= 0.5) maxTrq = 10.0;
                else if (rawMaxT <= 8.0) maxTrq = 10.0;
                else if (rawMaxT <= 16.0) maxTrq = 20.0;
                else if (rawMaxT <= 25.0) maxTrq = 30.0;
                else if (rawMaxT <= 42.0) maxTrq = 50.0;
                else if (rawMaxT <= 85.0) maxTrq = 100.0;
                else maxTrq = Math.Ceiling(rawMaxT * 1.2 / 50.0) * 50.0;

                // 轉速自適應上限取整 (留 15% 餘裕，對齊 500, 1000, 1500, 2000, 2500, 3000, 4000 rpm 階梯)
                if (rawMaxS <= 100.0) maxSpd = 500.0;
                else if (rawMaxS <= 400.0) maxSpd = 500.0;
                else if (rawMaxS <= 850.0) maxSpd = 1000.0;
                else if (rawMaxS <= 1300.0) maxSpd = 1500.0;
                else if (rawMaxS <= 1750.0) maxSpd = 2000.0;
                else if (rawMaxS <= 2200.0) maxSpd = 2500.0;
                else if (rawMaxS <= 2700.0) maxSpd = 3000.0;
                else if (rawMaxS <= 3500.0) maxSpd = 4000.0;
                else maxSpd = Math.Ceiling(rawMaxS * 1.15 / 500.0) * 500.0;
            }

            if (maxTrq <= minTrq) maxTrq = minTrq + 1.0;
            if (maxSpd <= minSpd) maxSpd = minSpd + 10.0;

            // 繪製網格與左右雙 Y 軸刻度
            using (Pen pGrid = new Pen(Color.FromArgb(242, 245, 248), 1f))
            using (Pen pZero = new Pen(Color.FromArgb(203, 213, 225), 1.5f) { DashStyle = DashStyle.Dash })
            using (Brush bTrq = new SolidBrush(Color.FromArgb(220, 38, 38))) // 橘紅: 轉矩
            using (Brush bSpd = new SolidBrush(Color.FromArgb(2, 132, 199)))  // 湛藍: 轉速
            {
                int gridSteps = 5;
                for (int i = 0; i <= gridSteps; i++)
                {
                    float y = plotRect.Top + i * (plotRect.Height / (float)gridSteps);
                    g.DrawLine(pGrid, plotRect.Left, y, plotRect.Right, y);

                    // 左 Y 軸: 轉矩 (Nm) - 一律正值，底標為 0
                    double trqVal = Math.Max(0.0, maxTrq - i * ((maxTrq - minTrq) / gridSteps));
                    string tStr = (Math.Abs(maxTrq - minTrq) <= 10.0) ? string.Format("{0:F1}", trqVal) : string.Format("{0:F0}", trqVal);
                    g.DrawString(tStr + ((i == 0) ? "Nm" : ""), fScale75, bTrq, 2, y - 6);

                    // 右 Y 軸: 轉速 (rpm) - 一律正值，底標為 0
                    double spdVal = Math.Max(0.0, maxSpd - i * ((maxSpd - minSpd) / gridSteps));
                    g.DrawString(string.Format("{0:F0}", spdVal) + ((i == 0) ? "rpm" : ""), fScale75, bSpd, plotRect.Right + 3, y - 6);
                }

                // 垂直時間參考線
                for (int i = 0; i <= 6; i++)
                {
                    float x = plotRect.Left + i * (plotRect.Width / 6f);
                    g.DrawLine(pGrid, x, plotRect.Top, x, plotRect.Bottom);
                }
            }

            g.DrawRectangle(Pens.LightGray, plotRect);

            // 繪製頂部圖例
            using (Pen pTrqSolid = new Pen(Color.FromArgb(220, 38, 38), 2f))
            using (Pen pTrqDash = new Pen(Color.FromArgb(248, 113, 113), 1.5f) { DashStyle = DashStyle.Dash })
            using (Pen pSpdSolid = new Pen(Color.FromArgb(2, 132, 199), 2f))
            using (Pen pSpdDash = new Pen(Color.FromArgb(56, 189, 248), 1.5f) { DashStyle = DashStyle.Dash })
            {
                float curX = plotRect.Left;
                // 實測轉矩
                g.DrawLine(pTrqSolid, curX, 11, curX + 14, 11);
                g.DrawString("實測轉矩", fLegend8B, Brushes.DarkRed, curX + 16, 4);
                curX += 75;

                // 目標轉矩
                g.DrawLine(pTrqDash, curX, 11, curX + 14, 11);
                g.DrawString("目標轉矩", fLegend8B, Brushes.IndianRed, curX + 16, 4);
                curX += 70;

                // 實測轉速
                g.DrawLine(pSpdSolid, curX, 11, curX + 14, 11);
                g.DrawString(AlignSpeedSign ? "實測轉速(同向)" : "實測轉速", fLegend8B, Brushes.Navy, curX + 16, 4);
                curX += AlignSpeedSign ? 100 : 75;

                // 目標轉速
                g.DrawLine(pSpdDash, curX, 11, curX + 14, 11);
                g.DrawString("目標轉速", fLegend8B, Brushes.SteelBlue, curX + 16, 4);
            }

            if (ptsCopy.Count > 1)
            {
                int step = (ptsCopy.Count > plotRect.Width / 2 && plotRect.Width > 0) ? Math.Max(1, ptsCopy.Count / Math.Max(100, plotRect.Width / 2)) : 1;
                List<PointF> ptsActTrq = new List<PointF>(Math.Min(ptsCopy.Count + 2, 500));
                List<PointF> ptsTgtTrq = new List<PointF>(Math.Min(ptsCopy.Count + 2, 500));
                List<PointF> ptsActSpd = new List<PointF>(Math.Min(ptsCopy.Count + 2, 500));
                List<PointF> ptsTgtSpd = new List<PointF>(Math.Min(ptsCopy.Count + 2, 500));

                for (int i = 0; i < ptsCopy.Count; i += step)
                {
                    float x = plotRect.Left + ((float)i / (ptsCopy.Count - 1)) * plotRect.Width;

                    float yActTrq = plotRect.Bottom - (float)((Math.Abs(ptsCopy[i].ActTorque) - minTrq) / (maxTrq - minTrq)) * plotRect.Height;
                    float yTgtTrq = plotRect.Bottom - (float)((Math.Abs(ptsCopy[i].TgtTorque) - minTrq) / (maxTrq - minTrq)) * plotRect.Height;
                    ptsActTrq.Add(new PointF(x, Math.Max(plotRect.Top, Math.Min(plotRect.Bottom, yActTrq))));
                    ptsTgtTrq.Add(new PointF(x, Math.Max(plotRect.Top, Math.Min(plotRect.Bottom, yTgtTrq))));

                    float yActSpd = plotRect.Bottom - (float)((Math.Abs(ptsCopy[i].ActSpeed) - minSpd) / (maxSpd - minSpd)) * plotRect.Height;
                    float yTgtSpd = plotRect.Bottom - (float)((Math.Abs(ptsCopy[i].TgtSpeed) - minSpd) / (maxSpd - minSpd)) * plotRect.Height;
                    ptsActSpd.Add(new PointF(x, Math.Max(plotRect.Top, Math.Min(plotRect.Bottom, yActSpd))));
                    ptsTgtSpd.Add(new PointF(x, Math.Max(plotRect.Top, Math.Min(plotRect.Bottom, yTgtSpd))));
                }

                // 保證納入最後一個最新點
                if ((ptsCopy.Count - 1) % step != 0)
                {
                    int lastIdx = ptsCopy.Count - 1;
                    float x = plotRect.Right;
                    float yActTrq = plotRect.Bottom - (float)((Math.Abs(ptsCopy[lastIdx].ActTorque) - minTrq) / (maxTrq - minTrq)) * plotRect.Height;
                    float yTgtTrq = plotRect.Bottom - (float)((Math.Abs(ptsCopy[lastIdx].TgtTorque) - minTrq) / (maxTrq - minTrq)) * plotRect.Height;
                    ptsActTrq.Add(new PointF(x, Math.Max(plotRect.Top, Math.Min(plotRect.Bottom, yActTrq))));
                    ptsTgtTrq.Add(new PointF(x, Math.Max(plotRect.Top, Math.Min(plotRect.Bottom, yTgtTrq))));

                    float yActSpd = plotRect.Bottom - (float)((Math.Abs(ptsCopy[lastIdx].ActSpeed) - minSpd) / (maxSpd - minSpd)) * plotRect.Height;
                    float yTgtSpd = plotRect.Bottom - (float)((Math.Abs(ptsCopy[lastIdx].TgtSpeed) - minSpd) / (maxSpd - minSpd)) * plotRect.Height;
                    ptsActSpd.Add(new PointF(x, Math.Max(plotRect.Top, Math.Min(plotRect.Bottom, yActSpd))));
                    ptsTgtSpd.Add(new PointF(x, Math.Max(plotRect.Top, Math.Min(plotRect.Bottom, yTgtSpd))));
                }

                using (Pen pTrqSolid = new Pen(Color.FromArgb(220, 38, 38), 2f))
                using (Pen pTrqDash = new Pen(Color.FromArgb(248, 113, 113), 1.5f) { DashStyle = DashStyle.Dash })
                using (Pen pSpdSolid = new Pen(Color.FromArgb(2, 132, 199), 2f))
                using (Pen pSpdDash = new Pen(Color.FromArgb(56, 189, 248), 1.5f) { DashStyle = DashStyle.Dash })
                {
                    if (ptsCopy.Any(p => p.TgtTorque > 0.01))
                    {
                        g.DrawLines(pTrqDash, ptsTgtTrq.ToArray());
                    }
                    if (ptsCopy.Any(p => p.TgtSpeed > 0.1))
                    {
                        g.DrawLines(pSpdDash, ptsTgtSpd.ToArray());
                    }
                    g.DrawLines(pTrqSolid, ptsActTrq.ToArray());
                    g.DrawLines(pSpdSolid, ptsActSpd.ToArray());
                }

                var last = ptsCopy[ptsCopy.Count - 1];
                PointF lastTrqPt = ptsActTrq[ptsActTrq.Count - 1];
                PointF lastSpdPt = ptsActSpd[ptsActSpd.Count - 1];

                g.FillEllipse(Brushes.Red, lastTrqPt.X - 3f, lastTrqPt.Y - 3f, 6, 6);
                g.FillEllipse(Brushes.DeepSkyBlue, lastSpdPt.X - 3f, lastSpdPt.Y - 3f, 6, 6);

                string statusText = string.Format("實測力矩: {0:F2} Nm | 實測轉速: {1:F0} rpm | 刻度範圍: [0~{2:F1}Nm, 0~{3:F0}rpm]{4}", 
                    Math.Abs(last.ActTorque), Math.Abs(last.ActSpeed), 
                    maxTrq, maxSpd, last.IsLocked ? " [🎯 目標追蹤中]" : "");
                g.DrawString(statusText, fFoot8, Brushes.DarkSlateGray, plotRect.Left, plotRect.Bottom + 2);
            }
            else
            {
                string hint = " 轉矩與轉速即時動態響應圖 (採樣中...)";
                SizeF sz = g.MeasureString(hint, fHint9);
                g.DrawString(hint, fHint9, Brushes.DarkGray, plotRect.Left + (plotRect.Width - sz.Width) / 2, plotRect.Top + (plotRect.Height - sz.Height) / 2);
            }
            }
            catch { }
        }
    }
    public class ClosedLoopControlDialog : Form
    {
        private MainForm main;
        private Label lblBaseDisp, lblSmoothDisp, lblBaseSpeedDisp, lblSmoothSpeedDisp, lblIntegratedHint;
        private NumericUpDown numFilter, numInterval, numDead, numStep, numThresh;
        private Button btnToggle, btnSetBase;
        private CheckBox chkAutoCsv, chkSysLog;
        private System.Windows.Forms.Timer dlgTimer;

        public ClosedLoopControlDialog(MainForm mainForm)
        {
            this.main = mainForm;
            this.Text = "🎯 主捲平滑追隨、常態日誌與全自動安全防護矩陣設定";
            this.Size = new Size(720, 1000);
            this.StartPosition = FormStartPosition.CenterParent;
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.AutoScroll = true;
            this.BackColor = Color.FromArgb(248, 250, 252);
            this.Font = new Font("微軟正黑體", 9.5f);

            TableLayoutPanel root = new TableLayoutPanel()
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 5,
                Padding = new Padding(12),
                AutoScroll = true
            };
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 32f));  // 頂部說明
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 335f)); // 雙軸閉迴路平滑追隨防護控制 (轉矩 + 轉速)
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 185f)); // 背景常態日誌開關
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 365f)); // 全自動安全防護矩陣 GroupBox
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 45f));  // 底部按鈕

            Label lblTop = new Label()
            {
                Text = " 動力計雙軸平滑追隨閉迴路控制、日誌記錄與全自動安全防護矩陣設定",
                Font = new Font("微軟正黑體", 10.5f, FontStyle.Bold),
                ForeColor = Color.FromArgb(15, 23, 42),
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft
            };
            root.Controls.Add(lblTop, 0, 0);

            // 1. 雙軸平滑追隨 GroupBox (待測端轉速鎖 + 加載端轉矩鎖)
            GroupBox grpTracking = new GroupBox()
            {
                Text = "️ 雙軸閉迴路平滑追隨與安全防護 (待測端轉速鎖 + 加載端轉矩鎖)",
                Dock = DockStyle.Fill,
                Font = new Font("微軟正黑體", 9.5f, FontStyle.Bold),
                ForeColor = Color.FromArgb(30, 41, 59)
            };
            TableLayoutPanel pnlTrackGrid = new TableLayoutPanel()
            {
                Dock = DockStyle.Fill,
                ColumnCount = 4,
                RowCount = 7,
                Padding = new Padding(8, 8, 8, 8)
            };
            pnlTrackGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120f));
            pnlTrackGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
            pnlTrackGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120f));
            pnlTrackGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
            for (int r = 0; r < 7; r++) pnlTrackGrid.RowStyles.Add(new RowStyle(SizeType.Percent, 14.28f));

            // Row 0: 轉矩鎖定狀態 (加載端)
            Label lB1 = new Label() { Text = "鎖定基準轉矩:", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleRight, Font = new Font("微軟正黑體", 9f) };
            lblBaseDisp = new Label() { Text = (main.hasBaseline ? string.Format("{0:F2} Nm", main.baselineTorque) : "未設定"), Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, Font = new Font("Consolas", 10.5f, FontStyle.Bold), ForeColor = Color.FromArgb(2, 132, 199) };
            Label lB2 = new Label() { Text = "平滑即時轉矩:", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleRight, Font = new Font("微軟正黑體", 9f) };
            lblSmoothDisp = new Label() { Text = string.Format("{0:F2} Nm", main.smoothedTorque), Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, Font = new Font("Consolas", 10.5f, FontStyle.Bold), ForeColor = Color.FromArgb(16, 185, 129) };

            pnlTrackGrid.Controls.Add(lB1, 0, 0);
            pnlTrackGrid.Controls.Add(lblBaseDisp, 1, 0);
            pnlTrackGrid.Controls.Add(lB2, 2, 0);
            pnlTrackGrid.Controls.Add(lblSmoothDisp, 3, 0);

            // Row 1: 轉速鎖定狀態 (待測端 - 補償感應馬達 V/F 轉差)
            Label lS1 = new Label() { Text = "鎖定基準轉速:", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleRight, Font = new Font("微軟正黑體", 9f) };
            lblBaseSpeedDisp = new Label() { Text = (main.hasSpeedBaseline ? string.Format("{0:F1} rpm", main.baselineSpeed) : "未設定"), Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, Font = new Font("Consolas", 10.5f, FontStyle.Bold), ForeColor = Color.FromArgb(0, 120, 215) };
            Label lS2 = new Label() { Text = "平滑即時轉速:", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleRight, Font = new Font("微軟正黑體", 9f) };
            lblSmoothSpeedDisp = new Label() { Text = string.Format("{0:F1} rpm", main.smoothedSpeed), Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, Font = new Font("Consolas", 10.5f, FontStyle.Bold), ForeColor = Color.FromArgb(16, 185, 129) };

            pnlTrackGrid.Controls.Add(lS1, 0, 1);
            pnlTrackGrid.Controls.Add(lblBaseSpeedDisp, 1, 1);
            pnlTrackGrid.Controls.Add(lS2, 2, 1);
            pnlTrackGrid.Controls.Add(lblSmoothSpeedDisp, 3, 1);

            // Row 2: 時域共同參數 (濾波窗口 + 控制週期 + 智能連鎖)
            FlowLayoutPanel pnlF1 = new FlowLayoutPanel() { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, Margin = new Padding(0) };
            Label lF1 = new Label() { Text = "濾波窗口(ms):", AutoSize = true, Margin = new Padding(0, 5, 0, 0), Font = new Font("微軟正黑體", 9f) };
            CheckBox chkAutoLink = new CheckBox() { Text = "🔗連鎖", AutoSize = true, Font = new Font("微軟正黑體", 8f, FontStyle.Bold), ForeColor = Color.FromArgb(0, 120, 215), Margin = new Padding(0, 3, 2, 0) };
            chkAutoLink.Checked = (main.trackingFilterWindowMs == main.trackingControlIntervalMs);
            pnlF1.Controls.Add(lF1);
            pnlF1.Controls.Add(chkAutoLink);

            numFilter = new NumericUpDown() { Minimum = 50, Maximum = 60000, Increment = 50, Value = Math.Min(60000, main.trackingFilterWindowMs), Dock = DockStyle.Fill, Font = new Font("Consolas", 10f, FontStyle.Bold) };
            
            Label lF2 = new Label() { Text = "控制週期 (ms):", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleRight, Font = new Font("微軟正黑體", 9f) };
            numInterval = new NumericUpDown() { Minimum = 20, Maximum = 60000, Increment = 50, Value = Math.Min(60000, main.trackingControlIntervalMs), Dock = DockStyle.Fill, Font = new Font("Consolas", 10f, FontStyle.Bold) };

            bool isInternalUpdating = false;

            numInterval.ValueChanged += (s, e) => {
                main.trackingControlIntervalMs = (int)numInterval.Value;
                if (chkAutoLink.Checked)
                {
                    isInternalUpdating = true;
                    numFilter.Value = Math.Max(numFilter.Minimum, Math.Min(numFilter.Maximum, numInterval.Value));
                    main.trackingFilterWindowMs = (int)numFilter.Value;
                    isInternalUpdating = false;
                }
                main.SaveLayoutConfig();
            };

            numFilter.ValueChanged += (s, e) => {
                main.trackingFilterWindowMs = (int)numFilter.Value;
                if (!isInternalUpdating)
                {
                    // 若使用者手動輸入不同的數值，自動取消連鎖勾選，確保使用者可隨意自定義
                    if (numFilter.Value != numInterval.Value && chkAutoLink.Checked)
                    {
                        chkAutoLink.Checked = false;
                    }
                }
                main.SaveLayoutConfig();
            };

            chkAutoLink.CheckedChanged += (s, e) => {
                if (chkAutoLink.Checked)
                {
                    isInternalUpdating = true;
                    numFilter.Value = Math.Max(numFilter.Minimum, Math.Min(numFilter.Maximum, numInterval.Value));
                    main.trackingFilterWindowMs = (int)numFilter.Value;
                    isInternalUpdating = false;
                    main.SaveLayoutConfig();
                }
            };

            pnlTrackGrid.Controls.Add(pnlF1, 0, 2);
            pnlTrackGrid.Controls.Add(numFilter, 1, 2);
            pnlTrackGrid.Controls.Add(lF2, 2, 2);
            pnlTrackGrid.Controls.Add(numInterval, 3, 2);

            // Row 3: 加載端轉矩參數 (變化死區 + 單步調量)
            Label lD1 = new Label() { Text = "轉矩死區 (Nm):", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleRight, Font = new Font("微軟正黑體", 9f) };
            numDead = new NumericUpDown() { Minimum = 0.01m, Maximum = 10m, DecimalPlaces = 2, Increment = 0.01m, Value = main.trackingDeadband, Dock = DockStyle.Fill, Font = new Font("Consolas", 10f, FontStyle.Bold) };
            numDead.ValueChanged += (s, e) => {
                main.trackingDeadband = numDead.Value;
                if (main.numCardDeadband != null && main.numCardDeadband.Value != numDead.Value) main.numCardDeadband.Value = numDead.Value;
                main.SaveLayoutConfig();
            };

            Label lD2 = new Label() { Text = "轉矩單步調量(%):", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleRight, Font = new Font("微軟正黑體", 9f) };
            numStep = new NumericUpDown() { Minimum = 0.05m, Maximum = 10m, DecimalPlaces = 2, Increment = 0.05m, Value = main.trackingMaxDelta, Dock = DockStyle.Fill, Font = new Font("Consolas", 10f, FontStyle.Bold) };
            numStep.ValueChanged += (s, e) => {
                main.trackingMaxDelta = numStep.Value;
                main.SaveLayoutConfig();
            };

            pnlTrackGrid.Controls.Add(lD1, 0, 3);
            pnlTrackGrid.Controls.Add(numDead, 1, 3);
            pnlTrackGrid.Controls.Add(lD2, 2, 3);
            pnlTrackGrid.Controls.Add(numStep, 3, 3);

            // Row 4: 待測端轉速參數 (轉速死區 + 轉速單步調量)
            Label lSD1 = new Label() { Text = "轉速死區 (rpm):", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleRight, Font = new Font("微軟正黑體", 9f) };
            NumericUpDown numSpeedDead = new NumericUpDown() { Minimum = 0.1m, Maximum = 50.0m, DecimalPlaces = 1, Increment = 0.5m, Value = main.trackingSpeedDeadband, Dock = DockStyle.Fill, Font = new Font("Consolas", 10f, FontStyle.Bold) };
            numSpeedDead.ValueChanged += (s, e) => {
                main.trackingSpeedDeadband = numSpeedDead.Value;
                if (main.numCardSpeedDeadband != null && main.numCardSpeedDeadband.Value != numSpeedDead.Value) main.numCardSpeedDeadband.Value = numSpeedDead.Value;
                main.SaveLayoutConfig();
            };

            Label lSD2 = new Label() { Text = "轉速單步調量(rpm):", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleRight, Font = new Font("微軟正黑體", 9f) };
            NumericUpDown numSpeedStep = new NumericUpDown() { Minimum = 0.1m, Maximum = 50.0m, DecimalPlaces = 1, Increment = 0.5m, Value = main.trackingSpeedMaxDelta, Dock = DockStyle.Fill, Font = new Font("Consolas", 10f, FontStyle.Bold) };
            numSpeedStep.ValueChanged += (s, e) => {
                main.trackingSpeedMaxDelta = numSpeedStep.Value;
                main.SaveLayoutConfig();
            };

            pnlTrackGrid.Controls.Add(lSD1, 0, 4);
            pnlTrackGrid.Controls.Add(numSpeedDead, 1, 4);
            pnlTrackGrid.Controls.Add(lSD2, 2, 4);
            pnlTrackGrid.Controls.Add(numSpeedStep, 3, 4);

            // Row 5: 偏差門檻 + 快捷基準記錄
            Label lT1 = new Label() { Text = "偏差警示門檻(%):", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleRight, Font = new Font("微軟正黑體", 9f) };
            numThresh = new NumericUpDown() { Minimum = 1, Maximum = 50, Value = main.trackingSafetyThresh, Dock = DockStyle.Fill, Font = new Font("Consolas", 10f, FontStyle.Bold) };
            numThresh.ValueChanged += (s, e) => { main.trackingSafetyThresh = numThresh.Value; main.SaveLayoutConfig(); };

            FlowLayoutPanel flpRecBtns = new FlowLayoutPanel() { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, WrapContents = false };
            btnSetBase = new Button() { Text = "記錄轉矩基準", Size = new Size(110, 26), BackColor = Color.FromArgb(0, 180, 216), ForeColor = Color.White, Font = new Font("微軟正黑體", 8.5f, FontStyle.Bold), Cursor = Cursors.Hand };
            btnSetBase.Click += (s, e) => {
                main.baselineTorque = main.smoothedTorque;
                main.hasBaseline = true;
                lblBaseDisp.Text = string.Format("{0:F2} Nm", main.baselineTorque);
                MainForm.WriteHmiLog("CLOSED_LOOP", string.Format("已鎖定轉矩基準為: {0:F2} Nm", main.baselineTorque));
            };
            Button btnSetBaseSpeed = new Button() { Text = "記錄轉速基準", Size = new Size(110, 26), BackColor = Color.FromArgb(16, 185, 129), ForeColor = Color.White, Font = new Font("微軟正黑體", 8.5f, FontStyle.Bold), Cursor = Cursors.Hand, Margin = new Padding(4, 0, 0, 0) };
            btnSetBaseSpeed.Click += (s, e) => {
                main.baselineSpeed = main.smoothedSpeed;
                main.hasSpeedBaseline = true;
                lblBaseSpeedDisp.Text = string.Format("{0:F1} rpm", main.baselineSpeed);
                MainForm.WriteHmiLog("CLOSED_LOOP", string.Format("已鎖定轉速基準為: {0:F1} rpm", main.baselineSpeed));
            };
            flpRecBtns.Controls.AddRange(new Control[] { btnSetBase, btnSetBaseSpeed });

            pnlTrackGrid.Controls.Add(lT1, 0, 5);
            pnlTrackGrid.Controls.Add(numThresh, 1, 5);
            pnlTrackGrid.Controls.Add(flpRecBtns, 2, 5);
            pnlTrackGrid.SetColumnSpan(flpRecBtns, 2);

            // Row 6: 雙軸追隨狀態即時提示條
            lblIntegratedHint = new Label()
            {
                Text = "⚪【平滑追隨狀態】請透過主畫面【實測轉速】與【實測轉矩】卡片左上角 [LOCK] 一鍵啟閉並展開死區微調！",
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleCenter,
                Font = new Font("微軟正黑體", 9f, FontStyle.Bold),
                ForeColor = Color.FromArgb(70, 80, 95),
                BackColor = Color.FromArgb(248, 250, 252),
                BorderStyle = BorderStyle.FixedSingle
            };
            pnlTrackGrid.Controls.Add(lblIntegratedHint, 0, 6);
            pnlTrackGrid.SetColumnSpan(lblIntegratedHint, 4);

            grpTracking.Controls.Add(pnlTrackGrid);
            root.Controls.Add(grpTracking, 0, 1);

            // 2. 常態日誌記錄控制與通訊週期 GroupBox (軌道 1 & 軌道 2 開關 + 輪詢與刷新週期)
            GroupBox grpLogging = new GroupBox()
            {
                Text = "📋 背景常態日誌記錄與通訊週期設定",
                Dock = DockStyle.Fill,
                Font = new Font("微軟正黑體", 9.5f, FontStyle.Bold),
                ForeColor = Color.FromArgb(30, 41, 59)
            };
            TableLayoutPanel pnlLogGrid = new TableLayoutPanel()
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 4,
                Padding = new Padding(12, 6, 12, 6)
            };
            pnlLogGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, 34f)); // 軌道 1
            pnlLogGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, 34f)); // 軌道 2
            pnlLogGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, 38f)); // 輪詢與刷新
            pnlLogGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, 42f)); // 開啟目錄按鈕

            chkAutoCsv = new CheckBox()
            {
                Text = "📊 啟用軌道 1：每日全參數遙測 CSV (Auto_Raw_Telemetry_*.csv 每 1 秒自動寫入)",
                Checked = main.enableAutoRawCsv,
                Dock = DockStyle.Fill,
                Font = new Font("微軟正黑體", 9.5f, FontStyle.Bold),
                ForeColor = Color.FromArgb(15, 23, 42),
                Cursor = Cursors.Hand
            };
            chkAutoCsv.CheckedChanged += (s, e) => {
                main.enableAutoRawCsv = chkAutoCsv.Checked;
                main.SaveLayoutConfig();
                MainForm.WriteHmiLog("LOG_CONFIG", string.Format("軌道 1 自動遙測 CSV 記錄已{0}", main.enableAutoRawCsv ? "【開啟】" : "【關閉】"));
            };

            chkSysLog = new CheckBox()
            {
                Text = "📝 啟用軌道 2：系統事件與狀態日誌 (hmi_telemetry.log 檔案寫入儲存)",
                Checked = main.enableSystemEventLog,
                Dock = DockStyle.Fill,
                Font = new Font("微軟正黑體", 9.5f, FontStyle.Bold),
                ForeColor = Color.FromArgb(15, 23, 42),
                Cursor = Cursors.Hand
            };
            chkSysLog.CheckedChanged += (s, e) => {
                main.enableSystemEventLog = chkSysLog.Checked;
                main.SaveLayoutConfig();
                MainForm.WriteHmiLog("LOG_CONFIG", string.Format("軌道 2 系統事件日誌檔案儲存已{0}", main.enableSystemEventLog ? "【開啟】" : "【關閉】"));
            };

            // 輪詢週期與 UI 刷新頻率控制列
            FlowLayoutPanel flpIntervals = new FlowLayoutPanel() { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, Margin = new Padding(0) };
            
            Label lblPoll = new Label() { Text = "⏱️ 背景輪詢週期:", AutoSize = true, Margin = new Padding(0, 6, 4, 0), Font = new Font("微軟正黑體", 9.5f, FontStyle.Bold), ForeColor = Color.FromArgb(15, 23, 42) };
            NumericUpDown numPoll = new NumericUpDown() { Minimum = 10, Maximum = 5000, Increment = 10, Value = (main.numUnifiedInterval != null ? main.numUnifiedInterval.Value : 100), Width = 70, Font = new Font("Consolas", 10f, FontStyle.Bold), BackColor = Color.FromArgb(241, 245, 249), ForeColor = Color.FromArgb(16, 185, 129) };
            numPoll.ValueChanged += (s, e) => {
                if (main.numUnifiedInterval != null) main.numUnifiedInterval.Value = numPoll.Value;
                main.pollingIntervalMs = (int)numPoll.Value;
            };
            Label lblPollMs = new Label() { Text = "ms", AutoSize = true, Margin = new Padding(2, 6, 16, 0), Font = new Font("微軟正黑體", 9.5f) };

            Label lblRefresh = new Label() { Text = "🖥️ 主畫面刷新頻率:", AutoSize = true, Margin = new Padding(0, 6, 4, 0), Font = new Font("微軟正黑體", 9.5f, FontStyle.Bold), ForeColor = Color.FromArgb(15, 23, 42) };
            NumericUpDown numRefresh = new NumericUpDown() { Minimum = 50, Maximum = 5000, Increment = 50, Value = (main.numUiRefreshInterval != null ? main.numUiRefreshInterval.Value : 500), Width = 70, Font = new Font("Consolas", 10f, FontStyle.Bold), BackColor = Color.FromArgb(241, 245, 249), ForeColor = Color.FromArgb(2, 132, 199) };
            numRefresh.ValueChanged += (s, e) => {
                if (main.numUiRefreshInterval != null) main.numUiRefreshInterval.Value = numRefresh.Value;
                if (main.mainTimer != null) main.mainTimer.Interval = (int)numRefresh.Value;
            };
            Label lblRefreshMs = new Label() { Text = "ms", AutoSize = true, Margin = new Padding(2, 6, 12, 0), Font = new Font("微軟正黑體", 9.5f) };

            Label lblKebPoll = new Label() { Text = "⚡ KEB參數更新:", AutoSize = true, Margin = new Padding(0, 6, 2, 0), Font = new Font("微軟正黑體", 9.5f, FontStyle.Bold), ForeColor = Color.FromArgb(109, 40, 217) };
            NumericUpDown numKebPoll = new NumericUpDown() { Minimum = 100, Maximum = 60000, Increment = 100, Value = main.kebPollingIntervalMs, Width = 75, Font = new Font("Consolas", 10f, FontStyle.Bold), BackColor = Color.FromArgb(241, 245, 249), ForeColor = Color.FromArgb(109, 40, 217) };
            numKebPoll.ValueChanged += (s, e) => {
                main.kebPollingIntervalMs = (int)numKebPoll.Value;
                if (main.numKebPollingInterval != null) main.numKebPollingInterval.Value = numKebPoll.Value;
                main.SaveLayoutConfig();
                MainForm.WriteHmiLog("CONFIG", "已更新 KEB 右側參數輪詢週期為: " + main.kebPollingIntervalMs + " ms");
            };
            Label lblKebPollMs = new Label() { Text = "ms", AutoSize = true, Margin = new Padding(2, 6, 0, 0), Font = new Font("微軟正黑體", 9.5f) };

            flpIntervals.Controls.AddRange(new Control[] { lblPoll, numPoll, lblPollMs, lblRefresh, numRefresh, lblRefreshMs, lblKebPoll, numKebPoll, lblKebPollMs });

            FlowLayoutPanel flpLogAction = new FlowLayoutPanel() { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, Padding = new Padding(0, 2, 0, 0) };
            Button btnOpenLogs = new Button()
            {
                Text = " 📁 開啟日誌目錄 (logs/)",
                Size = new Size(180, 30),
                BackColor = Color.FromArgb(241, 245, 249),
                ForeColor = Color.FromArgb(15, 23, 42),
                Font = new Font("微軟正黑體", 9f, FontStyle.Bold),
                Cursor = Cursors.Hand
            };
            btnOpenLogs.Click += (s, e) => {
                string logDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs");
                if (!Directory.Exists(logDir)) Directory.CreateDirectory(logDir);
                try { System.Diagnostics.Process.Start("explorer.exe", logDir); } catch { }
            };
            flpLogAction.Controls.Add(btnOpenLogs);

            pnlLogGrid.Controls.Add(chkAutoCsv, 0, 0);
            pnlLogGrid.Controls.Add(chkSysLog, 0, 1);
            pnlLogGrid.Controls.Add(flpIntervals, 0, 2);
            pnlLogGrid.Controls.Add(flpLogAction, 0, 3);

            grpLogging.Controls.Add(pnlLogGrid);
            root.Controls.Add(grpLogging, 0, 2);

            // 3. 全自動安全防護矩陣 GroupBox (左側勾選 | 中間量化參數 | 右側時間延遲)
            GroupBox grpSafetyMatrix = new GroupBox()
            {
                Text = "🛡️ 全自動運行安全保護矩陣與失效防護機制 (左側勾選啟用 | 中間量化數值 | 右側判定時間)",
                Dock = DockStyle.Fill,
                Font = new Font("微軟正黑體", 9.5f, FontStyle.Bold),
                ForeColor = Color.FromArgb(30, 41, 59)
            };
            TableLayoutPanel pnlSafeGrid = new TableLayoutPanel()
            {
                Dock = DockStyle.Fill,
                ColumnCount = 3,
                RowCount = 7,
                Padding = new Padding(8, 4, 8, 4)
            };
            pnlSafeGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 38f)); // 左側勾選說明
            pnlSafeGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 38f)); // 中間量化數值 (Magnitude)
            pnlSafeGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 24f)); // 右側判定時間 (Duration)
            for (int r = 0; r < 7; r++) pnlSafeGrid.RowStyles.Add(new RowStyle(SizeType.Percent, 14.28f));

            // Row 0: 扭力計斷線與數據凍結保護
            CheckBox chkProtTorque = new CheckBox()
            {
                Text = "🚨 扭力計斷線與反饋凍結保護",
                Checked = main.enableProtTorqueLoss,
                Dock = DockStyle.Fill,
                Font = new Font("微軟正黑體", 9f, FontStyle.Bold),
                ForeColor = Color.FromArgb(220, 38, 38)
            };
            chkProtTorque.CheckedChanged += (s, e) => { main.enableProtTorqueLoss = chkProtTorque.Checked; main.SaveLayoutConfig(); };
            Label lblTorqueMag = new Label() { Text = "凍結判定: 轉速/扭力值完全無變動", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, Font = new Font("微軟正黑體", 8.5f), ForeColor = Color.Gray };
            FlowLayoutPanel flpTorqueTime = new FlowLayoutPanel() { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, WrapContents = false };
            NumericUpDown numProtTorque = new NumericUpDown() { Minimum = 0.5m, Maximum = 10.0m, Increment = 0.1m, DecimalPlaces = 1, Value = main.protTorqueTimeoutSec, Width = 55, Font = new Font("Consolas", 9.5f, FontStyle.Bold) };
            numProtTorque.ValueChanged += (s, e) => { main.protTorqueTimeoutSec = numProtTorque.Value; main.SaveLayoutConfig(); };
            flpTorqueTime.Controls.AddRange(new Control[] { numProtTorque, new Label() { Text = "秒 (逾時急停)", AutoSize = true, Margin = new Padding(2, 4, 0, 0), Font = new Font("微軟正黑體", 8.5f) } });
            pnlSafeGrid.Controls.Add(chkProtTorque, 0, 0);
            pnlSafeGrid.Controls.Add(lblTorqueMag, 1, 0);
            pnlSafeGrid.Controls.Add(flpTorqueTime, 2, 0);

            // Row 1: 機械堵轉與失速卡死保護
            CheckBox chkProtStall = new CheckBox()
            {
                Text = "⚙️ 機械堵轉/失速卡死保護",
                Checked = main.enableProtStall,
                Dock = DockStyle.Fill,
                Font = new Font("微軟正黑體", 9f, FontStyle.Bold),
                ForeColor = Color.FromArgb(194, 65, 12)
            };
            chkProtStall.CheckedChanged += (s, e) => { main.enableProtStall = chkProtStall.Checked; main.SaveLayoutConfig(); };
            FlowLayoutPanel flpStallMag = new FlowLayoutPanel() { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, WrapContents = false };
            flpStallMag.Controls.Add(new Label() { Text = "轉速門檻 <", AutoSize = true, Margin = new Padding(0, 4, 2, 0), Font = new Font("微軟正黑體", 8.5f) });
            NumericUpDown numProtStallSpd = new NumericUpDown() { Minimum = 10.0m, Maximum = 500.0m, Increment = 10.0m, Value = main.protStallSpeedThreshold, Width = 55, Font = new Font("Consolas", 9.5f, FontStyle.Bold) };
            numProtStallSpd.ValueChanged += (s, e) => { main.protStallSpeedThreshold = numProtStallSpd.Value; main.SaveLayoutConfig(); };
            flpStallMag.Controls.AddRange(new Control[] { numProtStallSpd, new Label() { Text = "rpm (給定>100)", AutoSize = true, Margin = new Padding(2, 4, 0, 0), Font = new Font("微軟正黑體", 8.5f) } });
            FlowLayoutPanel flpStallTime = new FlowLayoutPanel() { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, WrapContents = false };
            NumericUpDown numProtStall = new NumericUpDown() { Minimum = 0.5m, Maximum = 10.0m, Increment = 0.5m, DecimalPlaces = 1, Value = main.protStallDelaySec, Width = 55, Font = new Font("Consolas", 9.5f, FontStyle.Bold) };
            numProtStall.ValueChanged += (s, e) => { main.protStallDelaySec = numProtStall.Value; main.SaveLayoutConfig(); };
            flpStallTime.Controls.AddRange(new Control[] { numProtStall, new Label() { Text = "秒 (判定延遲)", AutoSize = true, Margin = new Padding(2, 4, 0, 0), Font = new Font("微軟正黑體", 8.5f) } });
            pnlSafeGrid.Controls.Add(chkProtStall, 0, 1);
            pnlSafeGrid.Controls.Add(flpStallMag, 1, 1);
            pnlSafeGrid.Controls.Add(flpStallTime, 2, 1);

            // Row 2: 繞組與軸承溫度雙級保護 (警告溫度 + 停機溫度)
            CheckBox chkProtOvertemp = new CheckBox()
            {
                Text = "🌡️ 馬達超溫兩級防護 (GL820)",
                Checked = main.enableProtOvertemp,
                Dock = DockStyle.Fill,
                Font = new Font("微軟正黑體", 9f, FontStyle.Bold),
                ForeColor = Color.FromArgb(180, 83, 9)
            };
            chkProtOvertemp.CheckedChanged += (s, e) => { main.enableProtOvertemp = chkProtOvertemp.Checked; main.SaveLayoutConfig(); };
            FlowLayoutPanel flpTempMag = new FlowLayoutPanel() { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, WrapContents = false };
            flpTempMag.Controls.Add(new Label() { Text = "警:", AutoSize = true, Margin = new Padding(0, 4, 1, 0), Font = new Font("微軟正黑體", 8.5f) });
            NumericUpDown numProtWarnTemp = new NumericUpDown() { Minimum = 40.0m, Maximum = 150.0m, Increment = 1.0m, DecimalPlaces = 1, Value = main.protWarnTempThreshold, Width = 55, Font = new Font("Consolas", 9f, FontStyle.Bold), ForeColor = Color.FromArgb(217, 119, 6) };
            numProtWarnTemp.ValueChanged += (s, e) => { main.protWarnTempThreshold = numProtWarnTemp.Value; main.SaveLayoutConfig(); };
            flpTempMag.Controls.AddRange(new Control[] { numProtWarnTemp, new Label() { Text = "°C | 停:", AutoSize = true, Margin = new Padding(1, 4, 1, 0), Font = new Font("微軟正黑體", 8.5f) } });
            NumericUpDown numProtTripTemp = new NumericUpDown() { Minimum = 50.0m, Maximum = 180.0m, Increment = 1.0m, DecimalPlaces = 1, Value = main.protMaxTempThreshold, Width = 55, Font = new Font("Consolas", 9f, FontStyle.Bold), ForeColor = Color.FromArgb(220, 38, 38) };
            numProtTripTemp.ValueChanged += (s, e) => { main.protMaxTempThreshold = numProtTripTemp.Value; main.SaveLayoutConfig(); };
            flpTempMag.Controls.AddRange(new Control[] { numProtTripTemp, new Label() { Text = "°C", AutoSize = true, Margin = new Padding(1, 4, 0, 0), Font = new Font("微軟正黑體", 8.5f) } });
            FlowLayoutPanel flpTempTime = new FlowLayoutPanel() { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, WrapContents = false };
            NumericUpDown numProtTempDelay = new NumericUpDown() { Minimum = 0.5m, Maximum = 10.0m, Increment = 0.5m, DecimalPlaces = 1, Value = main.protTempDelaySec, Width = 55, Font = new Font("Consolas", 9.5f, FontStyle.Bold) };
            numProtTempDelay.ValueChanged += (s, e) => { main.protTempDelaySec = numProtTempDelay.Value; main.SaveLayoutConfig(); };
            flpTempTime.Controls.AddRange(new Control[] { numProtTempDelay, new Label() { Text = "秒 (超溫持續)", AutoSize = true, Margin = new Padding(2, 4, 0, 0), Font = new Font("微軟正黑體", 8.5f) } });
            pnlSafeGrid.Controls.Add(chkProtOvertemp, 0, 2);
            pnlSafeGrid.Controls.Add(flpTempMag, 1, 2);
            pnlSafeGrid.Controls.Add(flpTempTime, 2, 2);

            // Row 3: WT333E 三相電流不平衡 / 欠相預警 (不強停，僅警示)
            CheckBox chkProtCurrent = new CheckBox()
            {
                Text = "⚡ WT333E 三相不平衡/欠相預警",
                Checked = main.enableProtCurrentImbalance,
                Dock = DockStyle.Fill,
                Font = new Font("微軟正黑體", 9f, FontStyle.Bold),
                ForeColor = Color.FromArgb(67, 56, 202)
            };
            chkProtCurrent.CheckedChanged += (s, e) => { main.enableProtCurrentImbalance = chkProtCurrent.Checked; main.SaveLayoutConfig(); };
            FlowLayoutPanel flpCurrentMag = new FlowLayoutPanel() { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, WrapContents = false };
            flpCurrentMag.Controls.Add(new Label() { Text = "不平衡率 >", AutoSize = true, Margin = new Padding(0, 4, 2, 0), Font = new Font("微軟正黑體", 8.5f) });
            NumericUpDown numProtImbalance = new NumericUpDown() { Minimum = 10.0m, Maximum = 80.0m, Increment = 5.0m, Value = main.protImbalancePercentThreshold, Width = 55, Font = new Font("Consolas", 9.5f, FontStyle.Bold) };
            numProtImbalance.ValueChanged += (s, e) => { main.protImbalancePercentThreshold = numProtImbalance.Value; main.SaveLayoutConfig(); };
            flpCurrentMag.Controls.AddRange(new Control[] { numProtImbalance, new Label() { Text = "% (I_avg>1A)", AutoSize = true, Margin = new Padding(2, 4, 0, 0), Font = new Font("微軟正黑體", 8.5f) } });
            FlowLayoutPanel flpCurrentTime = new FlowLayoutPanel() { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, WrapContents = false };
            NumericUpDown numProtImbDelay = new NumericUpDown() { Minimum = 0.5m, Maximum = 10.0m, Increment = 0.5m, DecimalPlaces = 1, Value = main.protImbalanceDelaySec, Width = 55, Font = new Font("Consolas", 9.5f, FontStyle.Bold) };
            numProtImbDelay.ValueChanged += (s, e) => { main.protImbalanceDelaySec = numProtImbDelay.Value; main.SaveLayoutConfig(); };
            flpCurrentTime.Controls.AddRange(new Control[] { numProtImbDelay, new Label() { Text = "秒 (防抖持續)", AutoSize = true, Margin = new Padding(2, 4, 0, 0), Font = new Font("微軟正黑體", 8.5f) } });
            pnlSafeGrid.Controls.Add(chkProtCurrent, 0, 3);
            pnlSafeGrid.Controls.Add(flpCurrentMag, 1, 3);
            pnlSafeGrid.Controls.Add(flpCurrentTime, 2, 3);

            // Row 4: 變頻器硬體故障 (ru.43 != 0) 雙機連鎖急停
            CheckBox chkProtKeb = new CheckBox()
            {
                Text = "⚠️ 變頻器內部硬體故障連鎖",
                Checked = main.enableProtKebFault,
                Dock = DockStyle.Fill,
                Font = new Font("微軟正黑體", 9f, FontStyle.Bold),
                ForeColor = Color.FromArgb(15, 23, 42)
            };
            chkProtKeb.CheckedChanged += (s, e) => { main.enableProtKebFault = chkProtKeb.Checked; main.SaveLayoutConfig(); };
            Label lblKebFaultDesc = new Label() { Text = "故障狀態: ru.43 故障碼 != 0 (E.xxx 報警即鎖)", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, Font = new Font("微軟正黑體", 8.5f), ForeColor = Color.Gray };
            Label lblKebFast = new Label() { Text = "即時連鎖 (0 ms)", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, Font = new Font("微軟正黑體", 9f, FontStyle.Bold), ForeColor = Color.FromArgb(16, 185, 129) };
            pnlSafeGrid.Controls.Add(chkProtKeb, 0, 4);
            pnlSafeGrid.Controls.Add(lblKebFaultDesc, 1, 4);
            pnlSafeGrid.Controls.Add(lblKebFast, 2, 4);

            // Row 5: KEB 通訊瞬斷容忍緩衝時間
            Label lblDiscTitle = new Label() { Text = "⏳ KEB 通訊瞬斷容忍緩衝保護", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, Font = new Font("微軟正黑體", 9f, FontStyle.Bold), ForeColor = Color.FromArgb(15, 23, 42) };
            Label lblDiscHint = new Label() { Text = "瞬斷保持運轉保住 S1，逾時分級停機", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, Font = new Font("微軟正黑體", 8.5f), ForeColor = Color.FromArgb(16, 185, 129) };
            FlowLayoutPanel flpDisc = new FlowLayoutPanel() { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, WrapContents = false };
            NumericUpDown numDisconnect = new NumericUpDown() { Minimum = 1, Maximum = 60, Increment = 1, Value = main.disconnectBufferSeconds, Width = 55, Font = new Font("Consolas", 9.5f, FontStyle.Bold), BackColor = Color.FromArgb(241, 245, 249), ForeColor = Color.FromArgb(220, 38, 38) };
            numDisconnect.ValueChanged += (s, e) => {
                main.disconnectBufferSeconds = numDisconnect.Value;
                main.SaveLayoutConfig();
                MainForm.WriteHmiLog("SAFETY_CONFIG", string.Format("已更新 KEB 斷線緩衝時間為: {0:F0} 秒", main.disconnectBufferSeconds));
            };
            main.numDisconnectBuffer = numDisconnect;
            flpDisc.Controls.AddRange(new Control[] { numDisconnect, new Label() { Text = "秒 (緩衝時間)", AutoSize = true, Margin = new Padding(2, 4, 0, 0), Font = new Font("微軟正黑體", 8.5f) } });
            pnlSafeGrid.Controls.Add(lblDiscTitle, 0, 5);
            pnlSafeGrid.Controls.Add(lblDiscHint, 1, 5);
            pnlSafeGrid.Controls.Add(flpDisc, 2, 5);

            // Row 6: 自動停機加載煞車卸載轉速門檻
            Label lblBrakeSpdTitle = new Label() { Text = "🛑 自動停機煞車卸載門檻", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, Font = new Font("微軟正黑體", 9f, FontStyle.Bold), ForeColor = Color.FromArgb(185, 28, 28) };
            Label lblBrakeSpdHint = new Label() { Text = "待測端先停機，加載端煞車至此轉速卸載停機", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, Font = new Font("微軟正黑體", 8.5f), ForeColor = Color.FromArgb(16, 185, 129) };
            FlowLayoutPanel flpBrakeSpd = new FlowLayoutPanel() { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, WrapContents = false };
            NumericUpDown numAutoStopBrakeRpm = new NumericUpDown() { Minimum = 10, Maximum = 5000, Increment = 50, Value = Math.Max(10, Math.Min(5000, main.autoStopBrakeThresholdRpm)), Width = 65, Font = new Font("Consolas", 9.5f, FontStyle.Bold), BackColor = Color.FromArgb(241, 245, 249), ForeColor = Color.FromArgb(185, 28, 28) };
            numAutoStopBrakeRpm.ValueChanged += (s, e) => {
                main.autoStopBrakeThresholdRpm = numAutoStopBrakeRpm.Value;
                if (main.numBrakeThreshLog != null && main.numBrakeThreshLog.Value != numAutoStopBrakeRpm.Value)
                    main.numBrakeThreshLog.Value = numAutoStopBrakeRpm.Value;
                main.SaveLayoutConfig();
                MainForm.WriteHmiLog("SAFETY_CONFIG", string.Format("已更新自動停機加載煞車卸載門檻為: {0:F0} rpm", main.autoStopBrakeThresholdRpm));
            };
            flpBrakeSpd.Controls.AddRange(new Control[] { numAutoStopBrakeRpm, new Label() { Text = "rpm (卸載轉速)", AutoSize = true, Margin = new Padding(2, 4, 0, 0), Font = new Font("微軟正黑體", 8.5f) } });
            pnlSafeGrid.Controls.Add(lblBrakeSpdTitle, 0, 6);
            pnlSafeGrid.Controls.Add(lblBrakeSpdHint, 1, 6);
            pnlSafeGrid.Controls.Add(flpBrakeSpd, 2, 6);

            grpSafetyMatrix.Controls.Add(pnlSafeGrid);
            root.Controls.Add(grpSafetyMatrix, 0, 3);

            // 4. 底部關閉按鈕
            Panel pnlBtns = new Panel() { Dock = DockStyle.Fill };
            Button btnClose = new Button()
            {
                Text = "[OK] 確定並關閉",
                Location = new Point(480, 6),
                Size = new Size(150, 32),
                BackColor = Color.FromArgb(16, 185, 129),
                ForeColor = Color.White,
                Font = new Font("微軟正黑體", 9.5f, FontStyle.Bold),
                Cursor = Cursors.Hand
            };
            btnClose.Click += (s, e) => this.Close();
            pnlBtns.Controls.Add(btnClose);
            root.Controls.Add(pnlBtns, 0, 4);

            this.Controls.Add(root);

            dlgTimer = new System.Windows.Forms.Timer() { Interval = 200 };
            dlgTimer.Tick += (s, e) => {
                if (lblSmoothDisp != null && !lblSmoothDisp.IsDisposed)
                    lblSmoothDisp.Text = string.Format("{0:F2} Nm", main.smoothedTorque);
                if (lblBaseDisp != null && !lblBaseDisp.IsDisposed)
                    lblBaseDisp.Text = main.hasBaseline ? string.Format("{0:F2} Nm", main.baselineTorque) : "未設定";
                if (lblSmoothSpeedDisp != null && !lblSmoothSpeedDisp.IsDisposed)
                    lblSmoothSpeedDisp.Text = string.Format("{0:F1} rpm", main.smoothedSpeed);
                if (lblBaseSpeedDisp != null && !lblBaseSpeedDisp.IsDisposed)
                    lblBaseSpeedDisp.Text = main.hasSpeedBaseline ? string.Format("{0:F1} rpm", main.baselineSpeed) : "未設定";

                if (lblIntegratedHint != null && !lblIntegratedHint.IsDisposed)
                {
                    string spdStatus = main.isSpeedTracking ? string.Format("🟢【轉速追隨中: {0:F1} rpm】", main.baselineSpeed) : "⚪【轉速待命中】";
                    string trqStatus = main.isClosedLoopTracking ? string.Format("🟢【轉矩追隨中: {0:F2} Nm】", main.baselineTorque) : "⚪【轉矩待命中】";
                    lblIntegratedHint.Text = string.Format("{0}  |  {1}", spdStatus, trqStatus);
                    lblIntegratedHint.ForeColor = (main.isSpeedTracking || main.isClosedLoopTracking) ? Color.FromArgb(16, 185, 129) : Color.FromArgb(100, 116, 139);
                }
            };
            dlgTimer.Start();

            this.FormClosed += (s, e) => {
                if (dlgTimer != null) { dlgTimer.Stop(); dlgTimer.Dispose(); }
            };
        }
    }

    // =========================================================================
    //  RAW DATA 採樣數據累積器 (用於設定時間窗口內進行算術平均 Arithmetic Mean 計算)
    // =========================================================================
    public class RawDataSampleAccumulator
    {
        private readonly List<double> speeds = new List<double>();
        private readonly List<double> torques = new List<double>();
        private readonly List<double> mechPowers = new List<double>();
        private readonly List<double> elecPowers = new List<double>();
        private readonly List<double> efficiencies = new List<double>();
        private readonly List<double> kts = new List<double>();

        private readonly List<double> u1s = new List<double>();
        private readonly List<double> i1s = new List<double>();
        private readonly List<double> p1s = new List<double>();
        private readonly List<double> u2s = new List<double>();
        private readonly List<double> i2s = new List<double>();
        private readonly List<double> p2s = new List<double>();
        private readonly List<double> u3s = new List<double>();
        private readonly List<double> i3s = new List<double>();
        private readonly List<double> p3s = new List<double>();

        private readonly List<double> voltageSigmas = new List<double>();
        private readonly List<double> currentSigmas = new List<double>();
        private readonly List<double> pfs = new List<double>();
        private readonly List<double> temps = new List<double>();
        private readonly List<double[]> gbdTemps = new List<double[]>();
        private string lastKebA = "";
        private string lastKebB = "";

        public void AddSample(
            double spd, double trq, double pMech, double pElec, double eff, double kt,
            double u1, double i1, double p1,
            double u2, double i2, double p2,
            double u3, double i3, double p3,
            double vSigma, double iSigma, double pf, double t,
            double[] gbd, string ka, string kb)
        {
            speeds.Add(spd);
            torques.Add(trq);
            mechPowers.Add(pMech);
            elecPowers.Add(pElec);
            efficiencies.Add(eff);
            kts.Add(kt);

            u1s.Add(u1); i1s.Add(i1); p1s.Add(p1);
            u2s.Add(u2); i2s.Add(i2); p2s.Add(p2);
            u3s.Add(u3); i3s.Add(i3); p3s.Add(p3);

            voltageSigmas.Add(vSigma);
            currentSigmas.Add(iSigma);
            pfs.Add(pf);
            temps.Add(t);
            if (gbd != null) gbdTemps.Add((double[])gbd.Clone());
            lastKebA = ka;
            lastKebB = kb;
        }

        public bool HasSamples { get { return speeds.Count > 0; } }
        public int Count { get { return speeds.Count; } }

        public string BuildAveragedCsvRow(DateTime timestamp, bool[] gl820ChannelMask, bool recordKebRu)
        {
            if (speeds.Count == 0) return "";
            double avgSpd = speeds.Average();
            double avgTrq = torques.Average();
            double avgPMech = mechPowers.Average();
            double avgPElec = elecPowers.Average();
            double avgEff = avgPElec > 0.01 ? Math.Min(99.9, (avgPMech / avgPElec) * 100.0) : (efficiencies.Count > 0 ? efficiencies.Average() : 0.0);
            double avgV = voltageSigmas.Average();
            double avgI = currentSigmas.Average();
            double avgKt = avgTrq > 0 && avgI > 0.05 ? avgTrq / avgI : (kts.Count > 0 ? kts.Average() : 0.0);

            double avgU1 = u1s.Count > 0 ? u1s.Average() : 0.0;
            double avgI1 = i1s.Count > 0 ? i1s.Average() : 0.0;
            double avgP1 = p1s.Count > 0 ? p1s.Average() : 0.0;

            double avgU2 = u2s.Count > 0 ? u2s.Average() : 0.0;
            double avgI2 = i2s.Count > 0 ? i2s.Average() : 0.0;
            double avgP2 = p2s.Count > 0 ? p2s.Average() : 0.0;

            double avgU3 = u3s.Count > 0 ? u3s.Average() : 0.0;
            double avgI3 = i3s.Count > 0 ? i3s.Average() : 0.0;
            double avgP3 = p3s.Count > 0 ? p3s.Average() : 0.0;

            double avgPf = pfs.Count > 0 ? pfs.Average() : 0.0;
            double avgT = temps.Count > 0 ? temps.Average() : 25.0;

            StringBuilder sb = new StringBuilder();
            sb.AppendFormat("\"{0}\",{1:F1},{2:F2},{3:F2},{4:F2},{5:F1},{6:F2}",
                timestamp.ToString("yyyy-MM-dd HH:mm:ss.fff"),
                avgSpd, avgTrq, avgPMech, avgPElec,
                avgEff, avgKt);

            sb.AppendFormat(",{0:F2},{1:F3},{2:F3},{3:F2},{4:F3},{5:F3},{6:F2},{7:F3},{8:F3}",
                avgU1, avgI1, avgP1,
                avgU2, avgI2, avgP2,
                avgU3, avgI3, avgP3);

            sb.AppendFormat(",{0:F1},{1:F2},{2:F3},{3:F1}",
                avgV, avgI, avgPf, avgT);

            for (int i = 0; i < 20; i++)
            {
                if (gl820ChannelMask != null && i < gl820ChannelMask.Length && gl820ChannelMask[i])
                {
                    double avgCh = 0.0;
                    if (gbdTemps.Count > 0)
                    {
                        double sum = 0;
                        int valid = 0;
                        for (int k = 0; k < gbdTemps.Count; k++)
                        {
                            if (i < gbdTemps[k].Length) { sum += gbdTemps[k][i]; valid++; }
                        }
                        avgCh = valid > 0 ? sum / valid : 0.0;
                    }
                    sb.AppendFormat(",{0:F1}", avgCh);
                }
            }

            if (recordKebRu)
            {
                sb.AppendFormat(",\"{0}\",\"{1}\"", (lastKebA ?? "").Replace("\"", "\"\""), (lastKebB ?? "").Replace("\"", "\"\""));
            }

            return sb.ToString();
        }

        public void Clear()
        {
            speeds.Clear();
            torques.Clear();
            mechPowers.Clear();
            elecPowers.Clear();
            efficiencies.Clear();
            kts.Clear();
            u1s.Clear(); i1s.Clear(); p1s.Clear();
            u2s.Clear(); i2s.Clear(); p2s.Clear();
            u3s.Clear(); i3s.Clear(); p3s.Clear();
            voltageSigmas.Clear();
            currentSigmas.Clear();
            pfs.Clear();
            temps.Clear();
            gbdTemps.Clear();
        }
    }

    // =========================================================================
    //  RAW DATA 錄製與命名設定彈窗 (RawDataConfigDialog: 支援 CH1~CH20 獨立勾選與時間間隔算術平均設定)
    // =========================================================================
    public class RawDataConfigDialog : Form
    {
        private MainForm main;
        private TextBox txtMotorName;
        private TextBox txtFileName;
        private TextBox txtFolderPath;
        private ComboBox cmbInterval;
        private CheckBox[] chkChannels = new CheckBox[20];
        private CheckBox chkKebRu;

        public RawDataConfigDialog(MainForm parent)
        {
            this.main = parent;
            this.Text = " RAW DATA 專屬錄製與通道設定";
            this.Size = new Size(630, 595);
            this.StartPosition = FormStartPosition.CenterParent;
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.BackColor = Color.FromArgb(248, 250, 252);
            this.Font = new Font("微軟正黑體", 9.5f);

            TableLayoutPanel root = new TableLayoutPanel()
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 5,
                Padding = new Padding(12)
            };
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 32f)); // Title
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 172f)); // Motor, Filename, Dir, Interval
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 185f)); // GL820 20 Channels
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 70f));  // ru Checkbox
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 55f));  // Buttons

            Label lblTitle = new Label()
            {
                Text = " 手動 RAW DATA 即時採樣記錄與通道設定",
                Font = new Font("微軟正黑體", 11.5f, FontStyle.Bold),
                ForeColor = Color.FromArgb(15, 23, 42),
                Dock = DockStyle.Fill
            };
            root.Controls.Add(lblTitle, 0, 0);

            // Group 1: 馬達名稱、儲存檔名與時間間隔算術平均設定
            GroupBox grpMotor = new GroupBox()
            {
                Text = " ️ 馬達型號、CSV 儲存檔名與紀錄時間間隔 (算術平均) ",
                Dock = DockStyle.Fill,
                Font = new Font("微軟正黑體", 9.5f, FontStyle.Bold)
            };
            TableLayoutPanel pnlMotorInner = new TableLayoutPanel()
            {
                Dock = DockStyle.Fill,
                ColumnCount = 3,
                RowCount = 4,
                Padding = new Padding(6, 6, 6, 4)
            };
            pnlMotorInner.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 95f));
            pnlMotorInner.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            pnlMotorInner.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 100f));
            pnlMotorInner.RowStyles.Add(new RowStyle(SizeType.Absolute, 32f));
            pnlMotorInner.RowStyles.Add(new RowStyle(SizeType.Absolute, 32f));
            pnlMotorInner.RowStyles.Add(new RowStyle(SizeType.Absolute, 32f));
            pnlMotorInner.RowStyles.Add(new RowStyle(SizeType.Absolute, 32f));

            Label lM1 = new Label() { Text = "馬達名稱:", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleRight, Font = new Font("微軟正黑體", 9.5f) };
            txtMotorName = new TextBox()
            {
                Text = !string.IsNullOrEmpty(main.motorModelName) ? main.motorModelName : "SVM100S",
                Dock = DockStyle.Fill,
                Font = new Font("Consolas", 10.5f, FontStyle.Bold)
            };
            Label lMHint = new Label() { Text = "(例如: SVM100S)", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, ForeColor = Color.Gray, Font = new Font("微軟正黑體", 8.5f) };
            pnlMotorInner.Controls.Add(lM1, 0, 0);
            pnlMotorInner.Controls.Add(txtMotorName, 1, 0);
            pnlMotorInner.Controls.Add(lMHint, 2, 0);

            Label lM2 = new Label() { Text = "儲存檔名:", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleRight, Font = new Font("微軟正黑體", 9.5f) };
            txtFileName = new TextBox()
            {
                Text = string.Format("{0}_{1}.csv", txtMotorName.Text.Trim(), DateTime.Now.ToString("yyyyMMdd_HHmm")),
                Dock = DockStyle.Fill,
                Font = new Font("Consolas", 10f, FontStyle.Bold)
            };
            Button btnRefreshName = new Button() { Text = " 重整時間", Dock = DockStyle.Fill, Font = new Font("微軟正黑體", 8.5f), BackColor = Color.FromArgb(226, 232, 240) };
            btnRefreshName.Click += (s, e) => {
                string mName = string.IsNullOrEmpty(txtMotorName.Text.Trim()) ? "SVM100S" : txtMotorName.Text.Trim();
                txtFileName.Text = string.Format("{0}_{1}.csv", mName, DateTime.Now.ToString("yyyyMMdd_HHmm"));
            };
            txtMotorName.TextChanged += (s, e) => {
                string mName = string.IsNullOrEmpty(txtMotorName.Text.Trim()) ? "SVM100S" : txtMotorName.Text.Trim();
                txtFileName.Text = string.Format("{0}_{1}.csv", mName, DateTime.Now.ToString("yyyyMMdd_HHmm"));
            };
            pnlMotorInner.Controls.Add(lM2, 0, 1);
            pnlMotorInner.Controls.Add(txtFileName, 1, 1);
            pnlMotorInner.Controls.Add(btnRefreshName, 2, 1);

            Label lM3 = new Label() { Text = "儲存目錄:", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleRight, Font = new Font("微軟正黑體", 9.5f) };
            string defaultLogDir = !string.IsNullOrEmpty(main.rawDataSaveDirectory) && Directory.Exists(main.rawDataSaveDirectory)
                ? main.rawDataSaveDirectory
                : Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs");
            txtFolderPath = new TextBox()
            {
                Text = defaultLogDir,
                Dock = DockStyle.Fill,
                Font = new Font("Consolas", 9f)
            };
            Button btnBrowse = new Button() { Text = " 瀏覽...", Dock = DockStyle.Fill, Font = new Font("微軟正黑體", 8.5f), BackColor = Color.FromArgb(226, 232, 240) };
            btnBrowse.Click += (s, e) => {
                using (FolderBrowserDialog fbd = new FolderBrowserDialog())
                {
                    fbd.SelectedPath = txtFolderPath.Text;
                    if (fbd.ShowDialog() == DialogResult.OK)
                    {
                        txtFolderPath.Text = fbd.SelectedPath;
                    }
                }
            };
            pnlMotorInner.Controls.Add(lM3, 0, 2);
            pnlMotorInner.Controls.Add(txtFolderPath, 1, 2);
            pnlMotorInner.Controls.Add(btnBrowse, 2, 2);

            Label lM4 = new Label() { Text = "紀錄間隔:", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleRight, Font = new Font("微軟正黑體", 9.5f, FontStyle.Bold), ForeColor = Color.FromArgb(2, 132, 199) };
            cmbInterval = new ComboBox()
            {
                Dock = DockStyle.Fill,
                DropDownStyle = ComboBoxStyle.DropDownList,
                Font = new Font("Consolas", 9.5f, FontStyle.Bold)
            };
            cmbInterval.Items.AddRange(new object[] {
                "100 ms (即時每輪採樣輸出)",
                "200 ms (每秒 5 筆平均)",
                "500 ms (每秒 2 筆平均)",
                "1000 ms (1.0秒/筆 算術平均 - 建議)",
                "2000 ms (2.0秒/筆 算術平均)",
                "5000 ms (5.0秒/筆 算術平均)",
                "10000 ms (10.0秒/筆 算術平均)"
            });
            if (main.rawDataIntervalMs <= 100) cmbInterval.SelectedIndex = 0;
            else if (main.rawDataIntervalMs <= 200) cmbInterval.SelectedIndex = 1;
            else if (main.rawDataIntervalMs <= 500) cmbInterval.SelectedIndex = 2;
            else if (main.rawDataIntervalMs <= 1000) cmbInterval.SelectedIndex = 3;
            else if (main.rawDataIntervalMs <= 2000) cmbInterval.SelectedIndex = 4;
            else if (main.rawDataIntervalMs <= 5000) cmbInterval.SelectedIndex = 5;
            else cmbInterval.SelectedIndex = 6;

            Label lM4Hint = new Label() { Text = "(算術平均寫入)", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, ForeColor = Color.FromArgb(16, 185, 129), Font = new Font("微軟正黑體", 8.5f, FontStyle.Bold) };
            pnlMotorInner.Controls.Add(lM4, 0, 3);
            pnlMotorInner.Controls.Add(cmbInterval, 1, 3);
            pnlMotorInner.Controls.Add(lM4Hint, 2, 3);

            grpMotor.Controls.Add(pnlMotorInner);
            root.Controls.Add(grpMotor, 0, 1);

            // Group 2: GL820 獨立 20 通道勾選
            GroupBox grpGbd = new GroupBox()
            {
                Text = "  GL820 溫度通道選擇 (CH1 ~ CH20 自由獨立勾選) ",
                Dock = DockStyle.Fill,
                Font = new Font("微軟正黑體", 9.5f, FontStyle.Bold)
            };
            TableLayoutPanel pnlGbdRoot = new TableLayoutPanel()
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 2,
                Padding = new Padding(4)
            };
            pnlGbdRoot.RowStyles.Add(new RowStyle(SizeType.Absolute, 32f)); // Quick Buttons
            pnlGbdRoot.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));  // 20 CheckBoxes Grid

            FlowLayoutPanel pnlQuick = new FlowLayoutPanel()
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                Margin = new Padding(0)
            };
            Button btnAutoDetect = new Button() { Text = "⚡ 依實測選取", Size = new Size(115, 26), Font = new Font("微軟正黑體", 8.5f, FontStyle.Bold), BackColor = Color.FromArgb(59, 130, 246), ForeColor = Color.White, Cursor = Cursors.Hand };
            btnAutoDetect.Click += (s, e) => {
                if (main != null)
                {
                    bool[] active = main.DetectActiveGbdChannels();
                    if (active.Any(b => b))
                    {
                        for (int i = 0; i < 20; i++) chkChannels[i].Checked = active[i];
                    }
                    else
                    {
                        MessageBox.Show("目前尚未連線至 GL820 或未收到有效溫度數據，已先勾選預設 CH1~4。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                        for (int i = 0; i < 20; i++) chkChannels[i].Checked = (i < 4);
                    }
                }
            };
            Button btnSelAll = new Button() { Text = "全選 (CH1~20)", Size = new Size(100, 26), Font = new Font("微軟正黑體", 8.5f), BackColor = Color.FromArgb(226, 232, 240) };
            btnSelAll.Click += (s, e) => { for (int i = 0; i < 20; i++) chkChannels[i].Checked = true; };
            Button btnClearAll = new Button() { Text = "全部取消", Size = new Size(75, 26), Font = new Font("微軟正黑體", 8.5f), BackColor = Color.FromArgb(226, 232, 240) };
            btnClearAll.Click += (s, e) => { for (int i = 0; i < 20; i++) chkChannels[i].Checked = false; };
            Button btnSel4 = new Button() { Text = "前 4 點 (CH1~4)", Size = new Size(110, 26), Font = new Font("微軟正黑體", 8.5f, FontStyle.Bold), BackColor = Color.FromArgb(224, 242, 254) };
            btnSel4.Click += (s, e) => {
                for (int i = 0; i < 20; i++) chkChannels[i].Checked = (i < 4);
            };
            Button btnSel8 = new Button() { Text = "前 8 點 (CH1~8)", Size = new Size(110, 26), Font = new Font("微軟正黑體", 8.5f, FontStyle.Bold), BackColor = Color.FromArgb(224, 242, 254) };
            btnSel8.Click += (s, e) => {
                for (int i = 0; i < 20; i++) chkChannels[i].Checked = (i < 8);
            };
            pnlQuick.Controls.AddRange(new Control[] { btnAutoDetect, btnSel4, btnSel8, btnSelAll, btnClearAll });
            pnlGbdRoot.Controls.Add(pnlQuick, 0, 0);

            TableLayoutPanel pnlGridCh = new TableLayoutPanel()
            {
                Dock = DockStyle.Fill,
                ColumnCount = 5,
                RowCount = 4,
                Margin = new Padding(0)
            };
            for (int c = 0; c < 5; c++) pnlGridCh.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 20f));
            for (int r = 0; r < 4; r++) pnlGridCh.RowStyles.Add(new RowStyle(SizeType.Percent, 25f));

            for (int i = 0; i < 20; i++)
            {
                string cName = (main.gl820ChannelNames != null && i < main.gl820ChannelNames.Length && !string.IsNullOrEmpty(main.gl820ChannelNames[i]))
                    ? main.gl820ChannelNames[i]
                    : ("CH" + (i + 1));
                chkChannels[i] = new CheckBox()
                {
                    Text = cName,
                    Checked = (main.gl820ChannelMask != null && i < main.gl820ChannelMask.Length) ? main.gl820ChannelMask[i] : (i < 4),
                    Dock = DockStyle.Fill,
                    Font = new Font("微軟正黑體", 9f, FontStyle.Bold),
                    ForeColor = Color.FromArgb(30, 41, 59)
                };
                int col = i % 5;
                int row = i / 5;
                pnlGridCh.Controls.Add(chkChannels[i], col, row);
            }
            pnlGbdRoot.Controls.Add(pnlGridCh, 0, 1);
            grpGbd.Controls.Add(pnlGbdRoot);
            root.Controls.Add(grpGbd, 0, 2);

            // Group 3: Options (ru)
            GroupBox grpCalc = new GroupBox()
            {
                Text = "  驅動器參數記錄 (選填) ",
                Dock = DockStyle.Fill,
                Font = new Font("微軟正黑體", 9f, FontStyle.Bold)
            };
            Panel pnlCalcInner = new Panel() { Dock = DockStyle.Fill };
            chkKebRu = new CheckBox()
            {
                Text = "[x] 儲存 A/B 載台 ru 參數 (ru00, ru01, ru26, ru07, ru09, ru10, ru20, ru43)",
                Checked = main.recordKebRuParams,
                Location = new Point(10, 18),
                AutoSize = true,
                Font = new Font("微軟正黑體", 9f)
            };
            pnlCalcInner.Controls.Add(chkKebRu);
            grpCalc.Controls.Add(pnlCalcInner);
            root.Controls.Add(grpCalc, 0, 3);

            // Buttons
            Panel pnlBtns = new Panel() { Dock = DockStyle.Fill };
            Button btnStartRecord = new Button()
            {
                Text = " 開始錄製 RAW DATA",
                Location = new Point(120, 8),
                Size = new Size(210, 34),
                BackColor = Color.FromArgb(16, 185, 129),
                ForeColor = Color.White,
                Font = new Font("微軟正黑體", 10.5f, FontStyle.Bold),
                Cursor = Cursors.Hand
            };
            btnStartRecord.Click += (s, e) => {
                string mName = string.IsNullOrEmpty(txtMotorName.Text.Trim()) ? "SVM100S" : txtMotorName.Text.Trim();
                string fName = string.IsNullOrEmpty(txtFileName.Text.Trim()) ? string.Format("{0}_{1}.csv", mName, DateTime.Now.ToString("yyyyMMdd_HHmm")) : txtFileName.Text.Trim();
                if (!fName.EndsWith(".csv", StringComparison.OrdinalIgnoreCase)) fName += ".csv";
                string fPath = string.IsNullOrEmpty(txtFolderPath.Text.Trim()) ? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs") : txtFolderPath.Text.Trim();

                bool[] mask = new bool[20];
                for (int i = 0; i < 20; i++) mask[i] = chkChannels[i].Checked;

                int intervalMs = 1000;
                if (cmbInterval.SelectedIndex == 0) intervalMs = 100;
                else if (cmbInterval.SelectedIndex == 1) intervalMs = 200;
                else if (cmbInterval.SelectedIndex == 2) intervalMs = 500;
                else if (cmbInterval.SelectedIndex == 3) intervalMs = 1000;
                else if (cmbInterval.SelectedIndex == 4) intervalMs = 2000;
                else if (cmbInterval.SelectedIndex == 5) intervalMs = 5000;
                else if (cmbInterval.SelectedIndex == 6) intervalMs = 10000;

                main.StartManualRecordingWithParams(mName, fPath, fName, mask, chkKebRu.Checked, intervalMs);
                this.Close();
            };

            Button btnSaveOnly = new Button()
            {
                Text = " 僅儲存設定",
                Location = new Point(340, 8),
                Size = new Size(130, 34),
                BackColor = Color.FromArgb(0, 120, 215),
                ForeColor = Color.White,
                Font = new Font("微軟正黑體", 9.5f, FontStyle.Bold),
                Cursor = Cursors.Hand
            };
            btnSaveOnly.Click += (s, e) => {
                main.motorModelName = string.IsNullOrEmpty(txtMotorName.Text.Trim()) ? "SVM100S" : txtMotorName.Text.Trim();
                main.rawDataSaveDirectory = txtFolderPath.Text.Trim();
                for (int i = 0; i < 20; i++) main.gl820ChannelMask[i] = chkChannels[i].Checked;
                main.recordKebRuParams = chkKebRu.Checked;

                int intervalMs = 1000;
                if (cmbInterval.SelectedIndex == 0) intervalMs = 100;
                else if (cmbInterval.SelectedIndex == 1) intervalMs = 200;
                else if (cmbInterval.SelectedIndex == 2) intervalMs = 500;
                else if (cmbInterval.SelectedIndex == 3) intervalMs = 1000;
                else if (cmbInterval.SelectedIndex == 4) intervalMs = 2000;
                else if (cmbInterval.SelectedIndex == 5) intervalMs = 5000;
                else if (cmbInterval.SelectedIndex == 6) intervalMs = 10000;
                main.rawDataIntervalMs = intervalMs;

                main.SaveLayoutConfig();
                MainForm.WriteHmiLog("CONFIG", string.Format("[OK] 已更新錄製設定: 馬達名稱={0}, 間隔={1}ms(算術平均), 勾選通道數={2}, 儲存ru={3}",
                    main.motorModelName, main.rawDataIntervalMs, main.gl820ChannelMask.Count(b => b), main.recordKebRuParams));
                this.Close();
            };

            Button btnCancel = new Button()
            {
                Text = "[FAIL] 取消",
                Location = new Point(480, 8),
                Size = new Size(90, 34),
                BackColor = Color.FromArgb(226, 232, 240),
                Font = new Font("微軟正黑體", 9.5f),
                Cursor = Cursors.Hand
            };
            btnCancel.Click += (s, e) => this.Close();

            pnlBtns.Controls.AddRange(new Control[] { btnStartRecord, btnSaveOnly, btnCancel });
            root.Controls.Add(pnlBtns, 0, 4);

            this.Controls.Add(root);
        }
    }

    // =========================================================================
    //  儀表量測校正微調視窗 (CalibrationSettingsDialog)
    //  支援 WT333E 三相電壓 (U1, U2, U3)、三相電流 (I1, I2, I3) 與 Kistler 扭力計比例係數
    // =========================================================================
    public class CalibrationSettingsDialog : Form
    {
        private MainForm main;
        private NumericUpDown numScaleU1, numScaleU2, numScaleU3;
        private NumericUpDown numScaleI1, numScaleI2, numScaleI3;
        private NumericUpDown numScaleTorque;

        public CalibrationSettingsDialog(MainForm mainForm)
        {
            this.main = mainForm;
            this.Text = "📐 儀表量測數值校正微調 (WT333E / Kistler 扭力計)";
            this.Size = new Size(580, 480);
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.StartPosition = FormStartPosition.CenterParent;
            this.BackColor = Color.FromArgb(248, 250, 252);
            this.Font = new Font("微軟正黑體", 9.5f);

            TableLayoutPanel root = new TableLayoutPanel()
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 3,
                Padding = new Padding(12)
            };
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 55f)); // WT333E 校正
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 30f)); // 扭力計校正
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 50f)); // 按鈕列

            // 1. WT333E 三相電壓與電流校正
            GroupBox grpWt = new GroupBox()
            {
                Text = "⚡ 橫河 WT333E 功率計 - 三相比例微調係數 (顯示值 = 讀取值 × 比例)",
                Dock = DockStyle.Fill,
                Font = new Font("微軟正黑體", 9.5f, FontStyle.Bold),
                ForeColor = Color.FromArgb(15, 23, 42)
            };

            TableLayoutPanel tblWt = new TableLayoutPanel()
            {
                Dock = DockStyle.Fill,
                ColumnCount = 3,
                RowCount = 4,
                Padding = new Padding(8)
            };
            tblWt.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.3f));
            tblWt.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.3f));
            tblWt.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.3f));

            Label lU1 = new Label() { Text = "U1 電壓比例:", AutoSize = true, Font = new Font("微軟正黑體", 9f) };
            Label lU2 = new Label() { Text = "U2 電壓比例:", AutoSize = true, Font = new Font("微軟正黑體", 9f) };
            Label lU3 = new Label() { Text = "U3 電壓比例:", AutoSize = true, Font = new Font("微軟正黑體", 9f) };
            numScaleU1 = CreateScaleNum((decimal)MainForm.scaleU1);
            numScaleU2 = CreateScaleNum((decimal)MainForm.scaleU2);
            numScaleU3 = CreateScaleNum((decimal)MainForm.scaleU3);

            Label lI1 = new Label() { Text = "I1 電流比例:", AutoSize = true, Font = new Font("微軟正黑體", 9f) };
            Label lI2 = new Label() { Text = "I2 電流比例:", AutoSize = true, Font = new Font("微軟正黑體", 9f) };
            Label lI3 = new Label() { Text = "I3 電流比例:", AutoSize = true, Font = new Font("微軟正黑體", 9f) };
            numScaleI1 = CreateScaleNum((decimal)MainForm.scaleI1);
            numScaleI2 = CreateScaleNum((decimal)MainForm.scaleI2);
            numScaleI3 = CreateScaleNum((decimal)MainForm.scaleI3);

            tblWt.Controls.Add(lU1, 0, 0); tblWt.Controls.Add(numScaleU1, 0, 1);
            tblWt.Controls.Add(lU2, 1, 0); tblWt.Controls.Add(numScaleU2, 1, 1);
            tblWt.Controls.Add(lU3, 2, 0); tblWt.Controls.Add(numScaleU3, 2, 1);

            tblWt.Controls.Add(lI1, 0, 2); tblWt.Controls.Add(numScaleI1, 0, 3);
            tblWt.Controls.Add(lI2, 1, 2); tblWt.Controls.Add(numScaleI2, 1, 3);
            tblWt.Controls.Add(lI3, 2, 2); tblWt.Controls.Add(numScaleI3, 2, 3);

            grpWt.Controls.Add(tblWt);
            root.Controls.Add(grpWt, 0, 0);

            // 2. Kistler 扭力計校正
            GroupBox grpTorque = new GroupBox()
            {
                Text = "🎯 Kistler 4700B 扭力計 - 轉矩比例微調係數",
                Dock = DockStyle.Fill,
                Font = new Font("微軟正黑體", 9.5f, FontStyle.Bold),
                ForeColor = Color.FromArgb(15, 23, 42)
            };
            Panel pnlTrq = new Panel() { Dock = DockStyle.Fill, Padding = new Padding(12, 8, 12, 8) };
            Label lTrq = new Label() { Text = "Torque 轉矩比例 (例如 1.100 即放大 10%):", Location = new Point(10, 15), AutoSize = true, Font = new Font("微軟正黑體", 9f) };
            numScaleTorque = CreateScaleNum((decimal)MainForm.scaleTorque);
            numScaleTorque.Location = new Point(310, 12);
            pnlTrq.Controls.AddRange(new Control[] { lTrq, numScaleTorque });
            grpTorque.Controls.Add(pnlTrq);
            root.Controls.Add(grpTorque, 0, 1);

            // 3. 底部按鈕列
            Panel pnlBtns = new Panel() { Dock = DockStyle.Fill };
            Button btnResetAll = new Button()
            {
                Text = "全部重設為 1.0",
                Location = new Point(10, 8),
                Size = new Size(130, 34),
                BackColor = Color.FromArgb(241, 245, 249),
                Font = new Font("微軟正黑體", 9f),
                Cursor = Cursors.Hand
            };
            btnResetAll.Click += (s, e) => {
                numScaleU1.Value = 1.000m; numScaleU2.Value = 1.000m; numScaleU3.Value = 1.000m;
                numScaleI1.Value = 1.000m; numScaleI2.Value = 1.000m; numScaleI3.Value = 1.000m;
                numScaleTorque.Value = 1.000m;
            };

            Button btnApplySave = new Button()
            {
                Text = "💾 儲存並套用微調",
                Location = new Point(300, 8),
                Size = new Size(150, 34),
                BackColor = Color.FromArgb(16, 185, 129),
                ForeColor = Color.White,
                Font = new Font("微軟正黑體", 9.5f, FontStyle.Bold),
                Cursor = Cursors.Hand
            };
            btnApplySave.Click += (s, e) => {
                MainForm.scaleU1 = (double)numScaleU1.Value;
                MainForm.scaleU2 = (double)numScaleU2.Value;
                MainForm.scaleU3 = (double)numScaleU3.Value;
                MainForm.scaleI1 = (double)numScaleI1.Value;
                MainForm.scaleI2 = (double)numScaleI2.Value;
                MainForm.scaleI3 = (double)numScaleI3.Value;
                MainForm.scaleTorque = (double)numScaleTorque.Value;

                main.SaveCalibrationConfig();
                MessageBox.Show("校正比例係數已儲存並即時套用！", "設定成功", MessageBoxButtons.OK, MessageBoxIcon.Information);
                this.Close();
            };

            Button btnClose = new Button()
            {
                Text = "關閉",
                Location = new Point(460, 8),
                Size = new Size(85, 34),
                BackColor = Color.FromArgb(226, 232, 240),
                Font = new Font("微軟正黑體", 9f),
                Cursor = Cursors.Hand
            };
            btnClose.Click += (s, e) => this.Close();

            pnlBtns.Controls.AddRange(new Control[] { btnResetAll, btnApplySave, btnClose });
            root.Controls.Add(pnlBtns, 0, 2);

            this.Controls.Add(root);
        }

        private NumericUpDown CreateScaleNum(decimal initialVal)
        {
            return new NumericUpDown()
            {
                DecimalPlaces = 3,
                Increment = 0.01m,
                Minimum = 0.001m,
                Maximum = 100.000m,
                Value = Math.Max(0.001m, Math.Min(100.000m, initialVal)),
                Width = 100,
                Font = new Font("Consolas", 10f, FontStyle.Bold),
                BackColor = Color.White,
                ForeColor = Color.FromArgb(30, 41, 59)
            };
        }
    }
}
