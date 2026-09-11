using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace DynamometerHMI
{
    #region 1. 純 C# / .NET 4.0 零相依 PKZip 壓縮引擎 (100% 相容 Windows XP & 雲端)

    /// <summary>
    /// 標準 IEEE 802.3 CRC32 運算器
    /// </summary>
    public static class Crc32Helper
    {
        private static readonly uint[] Table;
        static Crc32Helper()
        {
            Table = new uint[256];
            for (uint i = 0; i < 256; i++)
            {
                uint entry = i;
                for (int j = 0; j < 8; j++)
                {
                    if ((entry & 1) == 1)
                        entry = (entry >> 1) ^ 0xEDB88320;
                    else
                        entry >>= 1;
                }
                Table[i] = entry;
            }
        }

        public static uint Compute(byte[] data)
        {
            if (data == null || data.Length == 0) return 0;
            uint crc = 0xFFFFFFFF;
            for (int i = 0; i < data.Length; i++)
            {
                byte index = (byte)((crc & 0xFF) ^ data[i]);
                crc = (crc >> 8) ^ Table[index];
            }
            return ~crc;
        }
    }

    /// <summary>
    /// 純 C# 輕量級 PKZip 檔案壓縮器 (.NET 4.0 相容，完全不依賴 .NET 4.5 System.IO.Compression.ZipFile)
    /// 產出標準 ZIP 封包，支援 UTF-8 中文檔名，100% 相容 Windows 原生解壓縮、WinRAR、7-Zip 與 Google Drive。
    /// </summary>
    public static class LightweightZipHelper
    {
        private class ZipEntryRecord
        {
            public string FileName;
            public uint Crc;
            public uint CompressedSize;
            public uint UncompressedSize;
            public uint LocalHeaderOffset;
            public ushort DosTime;
            public ushort DosDate;
        }

        public static void CreateZipArchive(string zipPath, IEnumerable<string> filePaths, Action<int, int, string> onProgress = null)
        {
            if (string.IsNullOrEmpty(zipPath)) throw new ArgumentNullException("zipPath");
            if (filePaths == null) throw new ArgumentNullException("filePaths");

            List<string> validFiles = new List<string>();
            foreach (string fp in filePaths)
            {
                if (File.Exists(fp)) validFiles.Add(fp);
            }

            if (validFiles.Count == 0) throw new InvalidOperationException("沒有任何有效的檔案可供壓縮！");

            string zipDir = Path.GetDirectoryName(zipPath);
            if (!string.IsNullOrEmpty(zipDir) && !Directory.Exists(zipDir))
            {
                Directory.CreateDirectory(zipDir);
            }

            List<ZipEntryRecord> entries = new List<ZipEntryRecord>();

            using (FileStream zipFs = new FileStream(zipPath, FileMode.Create, FileAccess.Write, FileShare.None))
            using (BinaryWriter bw = new BinaryWriter(zipFs))
            {
                for (int i = 0; i < validFiles.Count; i++)
                {
                    string filePath = validFiles[i];
                    string entryName = Path.GetFileName(filePath);
                    if (onProgress != null)
                    {
                        onProgress(i + 1, validFiles.Count, entryName);
                    }

                    byte[] rawBytes;
                    // 以 FileShare.ReadWrite 開啟，避免日誌正在寫入時發生鎖檔衝突
                    using (FileStream sourceFs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                    {
                        rawBytes = new byte[sourceFs.Length];
                        int bytesRead = 0;
                        while (bytesRead < rawBytes.Length)
                        {
                            int read = sourceFs.Read(rawBytes, bytesRead, rawBytes.Length - bytesRead);
                            if (read <= 0) break;
                            bytesRead += read;
                        }
                    }

                    uint crc = Crc32Helper.Compute(rawBytes);
                    uint uncompressedSize = (uint)rawBytes.Length;

                    // 使用 DeflateStream 進行壓縮
                    byte[] compressedBytes;
                    using (MemoryStream ms = new MemoryStream())
                    {
                        using (DeflateStream ds = new DeflateStream(ms, CompressionMode.Compress, true))
                        {
                            ds.Write(rawBytes, 0, rawBytes.Length);
                        }
                        compressedBytes = ms.ToArray();
                    }
                    uint compressedSize = (uint)compressedBytes.Length;

                    DateTime lastWrite = File.GetLastWriteTime(filePath);
                    ushort dosTime = (ushort)((lastWrite.Hour << 11) | (lastWrite.Minute << 5) | (lastWrite.Second / 2));
                    ushort dosDate = (ushort)(((lastWrite.Year - 1980) << 9) | (lastWrite.Month << 5) | lastWrite.Day);

                    byte[] nameBytes = Encoding.UTF8.GetBytes(entryName);

                    ZipEntryRecord entry = new ZipEntryRecord()
                    {
                        FileName = entryName,
                        Crc = crc,
                        CompressedSize = compressedSize,
                        UncompressedSize = uncompressedSize,
                        LocalHeaderOffset = (uint)zipFs.Position,
                        DosTime = dosTime,
                        DosDate = dosDate
                    };
                    entries.Add(entry);

                    // 1. 寫入 Local File Header
                    bw.Write((uint)0x04034b50); // Signature
                    bw.Write((ushort)20);        // Version needed (2.0)
                    bw.Write((ushort)0x0800);    // Bit flag: 0x0800 (Bit 11: UTF-8 檔名)
                    bw.Write((ushort)8);         // Compression method: 8 (Deflated)
                    bw.Write(dosTime);
                    bw.Write(dosDate);
                    bw.Write(crc);
                    bw.Write(compressedSize);
                    bw.Write(uncompressedSize);
                    bw.Write((ushort)nameBytes.Length);
                    bw.Write((ushort)0);         // Extra field length
                    bw.Write(nameBytes);
                    bw.Write(compressedBytes);
                }

                // 2. 寫入 Central Directory
                uint centralDirOffset = (uint)zipFs.Position;
                foreach (var entry in entries)
                {
                    byte[] nameBytes = Encoding.UTF8.GetBytes(entry.FileName);
                    bw.Write((uint)0x02014b50); // Central directory header signature
                    bw.Write((ushort)20);        // Version made by
                    bw.Write((ushort)20);        // Version needed
                    bw.Write((ushort)0x0800);    // Flags: UTF-8
                    bw.Write((ushort)8);         // Method: Deflated
                    bw.Write(entry.DosTime);
                    bw.Write(entry.DosDate);
                    bw.Write(entry.Crc);
                    bw.Write(entry.CompressedSize);
                    bw.Write(entry.UncompressedSize);
                    bw.Write((ushort)nameBytes.Length);
                    bw.Write((ushort)0);         // Extra field length
                    bw.Write((ushort)0);         // Comment length
                    bw.Write((ushort)0);         // Disk number start
                    bw.Write((ushort)0);         // Internal attributes
                    bw.Write((uint)0x00000020);  // External attributes (archive)
                    bw.Write(entry.LocalHeaderOffset);
                    bw.Write(nameBytes);
                }
                uint centralDirSize = (uint)zipFs.Position - centralDirOffset;

                // 3. 寫入 End of Central Directory (EOCD)
                bw.Write((uint)0x06054b50); // EOCD signature
                bw.Write((ushort)0);        // Number of this disk
                bw.Write((ushort)0);        // Disk with start of CD
                bw.Write((ushort)entries.Count); // Entries on this disk
                bw.Write((ushort)entries.Count); // Total entries
                bw.Write(centralDirSize);
                bw.Write(centralDirOffset);
                bw.Write((ushort)0);        // Comment length
            }
        }

        /// <summary>
        /// 自動探測本機 7-Zip 主程式 (7z.exe) 路徑 (支援 32-bit Windows XP 與登錄檔探測)
        /// </summary>
        public static string Find7ZipExecutable()
        {
            string[] candidatePaths = new string[]
            {
                @"C:\Program Files\7-Zip\7z.exe",
                @"C:\Program Files (x86)\7-Zip\7z.exe",
                @"D:\Program Files\7-Zip\7z.exe",
                @"D:\Program Files (x86)\7-Zip\7z.exe"
            };

            foreach (string p in candidatePaths)
            {
                if (File.Exists(p)) return p;
            }

            try
            {
                using (var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SOFTWARE\7-Zip"))
                {
                    if (key != null)
                    {
                        object pathObj = key.GetValue("Path");
                        if (pathObj != null)
                        {
                            string exePath = Path.Combine(pathObj.ToString(), "7z.exe");
                            if (File.Exists(exePath)) return exePath;
                        }
                    }
                }
            }
            catch { }

            return null;
        }

        /// <summary>
        /// 嘗試調用本機 7-Zip CLI 進行高效標準 ZIP 壓縮 (相容 UTF-8 與清單檔案)
        /// </summary>
        public static bool TryCompressWith7Zip(string zipPath, List<string> filePaths, out string errorMsg)
        {
            errorMsg = "";
            string sevenZipExe = Find7ZipExecutable();
            if (string.IsNullOrEmpty(sevenZipExe))
            {
                errorMsg = "找不到 7z.exe";
                return false;
            }

            string tempDir = Path.GetDirectoryName(zipPath);
            string listFile = Path.Combine(tempDir, "7z_list_" + Guid.NewGuid().ToString("N").Substring(0, 8) + ".txt");

            try
            {
                // 寫入清單檔案 (UTF-8)
                File.WriteAllLines(listFile, filePaths.ToArray(), Encoding.UTF8);

                if (File.Exists(zipPath)) File.Delete(zipPath);

                ProcessStartInfo psi = new ProcessStartInfo()
                {
                    FileName = sevenZipExe,
                    Arguments = string.Format("a -tzip \"{0}\" @\"{1}\" -y", zipPath, listFile),
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };

                using (Process proc = Process.Start(psi))
                {
                    string stdout = proc.StandardOutput.ReadToEnd();
                    string stderr = proc.StandardError.ReadToEnd();
                    proc.WaitForExit(30000);

                    if (proc.ExitCode == 0 && File.Exists(zipPath))
                    {
                        return true;
                    }
                    else
                    {
                        errorMsg = "7-Zip 退出碼: " + proc.ExitCode + " " + stderr;
                        return false;
                    }
                }
            }
            catch (Exception ex)
            {
                errorMsg = ex.Message;
                return false;
            }
            finally
            {
                try { if (File.Exists(listFile)) File.Delete(listFile); } catch { }
            }
        }
    }

    #endregion

    #region 2. 報告檔案資料項目結構

    public class ReportFileItem
    {
        public bool IsSelected { get; set; }
        public string FileName { get; set; }
        public string FileType { get; set; }
        public long SizeBytes { get; set; }
        public string SizeFormatted { get; set; }
        public DateTime LastModified { get; set; }
        public string FullPath { get; set; }

        public ReportFileItem(string path)
        {
            FullPath = path;
            FileName = Path.GetFileName(path);
            FileInfo fi = new FileInfo(path);
            SizeBytes = fi.Exists ? fi.Length : 0;
            LastModified = fi.Exists ? fi.LastWriteTime : DateTime.MinValue;

            if (SizeBytes < 1024) SizeFormatted = SizeBytes + " B";
            else if (SizeBytes < 1024 * 1024) SizeFormatted = (SizeBytes / 1024.0).ToString("F1") + " KB";
            else SizeFormatted = (SizeBytes / (1024.0 * 1024.0)).ToString("F2") + " MB";

            string ext = Path.GetExtension(path).ToLower();
            string upperName = FileName.ToUpper();

            if (upperName.Contains("RAW_TELEMETRY") || upperName.Contains("AUTO_RAW"))
                FileType = "📊 實測高頻遙測 (CSV)";
            else if (ext == ".gbd")
                FileType = "🌡️ 溫度多通道記錄 (GBD)";
            else if (ext == ".log")
                FileType = "📜 系統運轉/除錯日誌 (LOG)";
            else if (ext == ".csv")
                FileType = "📑 試驗匯總數據 (CSV)";
            else if (ext == ".zip")
                FileType = "📦 報告壓縮封裝 (ZIP)";
            else
                FileType = "📄 測試相關檔案 (" + ext + ")";
        }
    }

    #endregion

    #region 3. 主視窗 MainForm 報告管理與雲端上傳擴展

    public partial class MainForm
    {
        // 報告管理分頁控制項
        public TabPage tabReport;
        public DataGridView dgvReports;
        public List<ReportFileItem> reportFileList = new List<ReportFileItem>();

        // 頂部過濾與快選控制項
        public Button btnOpenLogDir;
        public Button btnRefreshReports;
        public Button btnSelectToday;
        public Button btnSelectLatestTest;
        public Button btnSelectAllReports;
        public Button btnDeselectAllReports;
        public ComboBox cmbReportFilter;

        // 底部壓縮與上傳控制項
        public TextBox txtReportZipName;
        public Label lblReportSelectionInfo;
        public ComboBox cmbUploadTarget;
        public Panel pnlTargetConfigContainer;

        // GitHub Release 設定控制項
        public TextBox txtGhOwner;
        public TextBox txtGhRepo;
        public TextBox txtGhTag;
        public TextBox txtGhToken;
        public Button btnGhTokenGuide;
        public Button btnGhOpenReleases;

        public TextBox txtGasWebhookUrl;
        public TextBox txtGasDriveFolder;
        public TextBox txtGasNotifyEmail;
        public TextBox txtNasTargetPath;
        public Button btnBrowseNas;
        public Button btnViewGasScript;
        public Button btnExecuteUpload;
        public ProgressBar prgReportTask;
        public Label lblReportStatus;
        public TextBox txtReportLogs;

        /// <summary>
        /// 建置「📤 報告管理與雲端上傳」完整分頁 UI (遵循 Rule 4 防裁切規範)
        /// </summary>
        public void BuildReportTab(TabPage tab)
        {
            tab.BackColor = Color.FromArgb(248, 250, 252);
            tab.Padding = new Padding(6);

            // 主垂直 TableLayoutPanel (Top: 工具列 48px, Fill: 檔案清單, Bottom: 壓縮與上傳控制 250px)
            TableLayoutPanel mainTable = new TableLayoutPanel()
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 3,
                Margin = new Padding(0),
                Padding = new Padding(0)
            };
            mainTable.RowStyles.Add(new RowStyle(SizeType.Absolute, 50f)); // 頂部工具列
            mainTable.RowStyles.Add(new RowStyle(SizeType.Percent, 100f)); // 中央表格
            mainTable.RowStyles.Add(new RowStyle(SizeType.Absolute, 270f)); // 底部配置與上傳控制

            // ==========================================
            // 1. 頂部工具列 (Top Toolbar)
            // ==========================================
            FlowLayoutPanel pnlTopBar = new FlowLayoutPanel()
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                AutoScroll = true,
                BackColor = Color.FromArgb(241, 245, 249),
                Padding = new Padding(6, 6, 6, 6)
            };

            btnOpenLogDir = new Button()
            {
                Text = "📂 開啟 LOG 資料夾",
                Size = new Size(160, 36),
                BackColor = Color.FromArgb(37, 99, 235),
                ForeColor = Color.White,
                Font = new Font("微軟正黑體", 10f, FontStyle.Bold),
                Cursor = Cursors.Hand,
                FlatStyle = FlatStyle.Flat
            };
            btnOpenLogDir.FlatAppearance.BorderSize = 0;
            btnOpenLogDir.Click += (s, e) => OpenLogDirectoryInExplorer();

            btnRefreshReports = new Button()
            {
                Text = "🔄 重新整理清單",
                Size = new Size(130, 36),
                BackColor = Color.FromArgb(71, 85, 105),
                ForeColor = Color.White,
                Font = new Font("微軟正黑體", 10f, FontStyle.Bold),
                Cursor = Cursors.Hand,
                FlatStyle = FlatStyle.Flat
            };
            btnRefreshReports.FlatAppearance.BorderSize = 0;
            btnRefreshReports.Click += (s, e) => RefreshReportFileList();

            btnSelectToday = new Button()
            {
                Text = "📅 選取今日報告",
                Size = new Size(130, 36),
                BackColor = Color.FromArgb(16, 185, 129),
                ForeColor = Color.White,
                Font = new Font("微軟正黑體", 10f, FontStyle.Bold),
                Cursor = Cursors.Hand,
                FlatStyle = FlatStyle.Flat
            };
            btnSelectToday.FlatAppearance.BorderSize = 0;
            btnSelectToday.Click += (s, e) => SelectTodayReports();

            btnSelectLatestTest = new Button()
            {
                Text = "⚡ 選取最新測試 Session",
                Size = new Size(185, 36),
                BackColor = Color.FromArgb(139, 92, 246),
                ForeColor = Color.White,
                Font = new Font("微軟正黑體", 10f, FontStyle.Bold),
                Cursor = Cursors.Hand,
                FlatStyle = FlatStyle.Flat
            };
            btnSelectLatestTest.FlatAppearance.BorderSize = 0;
            btnSelectLatestTest.Click += (s, e) => SelectLatestSessionReports();

            btnSelectAllReports = new Button()
            {
                Text = "☑️ 全選",
                Size = new Size(80, 36),
                BackColor = Color.White,
                ForeColor = Color.FromArgb(30, 41, 59),
                Font = new Font("微軟正黑體", 10f, FontStyle.Regular),
                Cursor = Cursors.Hand
            };
            btnSelectAllReports.Click += (s, e) => SetAllReportsSelection(true);

            btnDeselectAllReports = new Button()
            {
                Text = "⬜ 清除",
                Size = new Size(80, 36),
                BackColor = Color.White,
                ForeColor = Color.FromArgb(30, 41, 59),
                Font = new Font("微軟正黑體", 10f, FontStyle.Regular),
                Cursor = Cursors.Hand
            };
            btnDeselectAllReports.Click += (s, e) => SetAllReportsSelection(false);

            Label lblFilter = new Label()
            {
                Text = "  篩選:",
                AutoSize = true,
                Font = new Font("微軟正黑體", 10f, FontStyle.Bold),
                Margin = new Padding(8, 9, 2, 0)
            };

            cmbReportFilter = new ComboBox()
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Width = 140,
                Font = new Font("微軟正黑體", 10f, FontStyle.Regular),
                Margin = new Padding(4, 7, 4, 0)
            };
            cmbReportFilter.Items.AddRange(new object[] { "全部檔案 (*.*)", "僅 CSV 數據", "僅 GBD 溫度", "僅 LOG 日誌" });
            cmbReportFilter.SelectedIndex = 0;
            cmbReportFilter.SelectedIndexChanged += (s, e) => RefreshReportFileList();

            pnlTopBar.Controls.AddRange(new Control[] {
                btnOpenLogDir, btnRefreshReports, btnSelectToday, btnSelectLatestTest,
                btnSelectAllReports, btnDeselectAllReports,
                lblFilter, cmbReportFilter
            });
            mainTable.Controls.Add(pnlTopBar, 0, 0);

            // ==========================================
            // 2. 中央檔案清單表格 (Center DataGridView)
            // ==========================================
            dgvReports = new DataGridView()
            {
                Dock = DockStyle.Fill,
                BackgroundColor = Color.White,
                BorderStyle = BorderStyle.Fixed3D,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                AllowUserToResizeRows = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                MultiSelect = false,
                RowHeadersVisible = false,
                Font = new Font("微軟正黑體", 10f, FontStyle.Regular),
                EnableHeadersVisualStyles = false,
                AutoGenerateColumns = false
            };
            dgvReports.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(226, 232, 240);
            dgvReports.ColumnHeadersDefaultCellStyle.Font = new Font("微軟正黑體", 10f, FontStyle.Bold);
            dgvReports.ColumnHeadersHeight = 34;

            // 欄位設定
            DataGridViewCheckBoxColumn colCheck = new DataGridViewCheckBoxColumn()
            {
                HeaderText = "選取",
                Name = "colSelect",
                Width = 60,
                DataPropertyName = "IsSelected"
            };
            DataGridViewTextBoxColumn colName = new DataGridViewTextBoxColumn()
            {
                HeaderText = "檔案名稱",
                Name = "colFileName",
                Width = 420,
                ReadOnly = true,
                DataPropertyName = "FileName"
            };
            DataGridViewTextBoxColumn colType = new DataGridViewTextBoxColumn()
            {
                HeaderText = "類型說明",
                Name = "colFileType",
                Width = 220,
                ReadOnly = true,
                DataPropertyName = "FileType"
            };
            DataGridViewTextBoxColumn colSize = new DataGridViewTextBoxColumn()
            {
                HeaderText = "檔案大小",
                Name = "colSize",
                Width = 110,
                ReadOnly = true,
                DataPropertyName = "SizeFormatted"
            };
            DataGridViewTextBoxColumn colDate = new DataGridViewTextBoxColumn()
            {
                HeaderText = "最後修改時間",
                Name = "colLastModified",
                Width = 180,
                ReadOnly = true
            };
            DataGridViewTextBoxColumn colPath = new DataGridViewTextBoxColumn()
            {
                HeaderText = "完整路徑",
                Name = "colFullPath",
                Visible = false,
                DataPropertyName = "FullPath"
            };

            dgvReports.Columns.AddRange(new DataGridViewColumn[] { colCheck, colName, colType, colSize, colDate, colPath });
            dgvReports.ColumnWidthChanged += (s, e) => SaveLayoutConfig();

            // 儲存格點擊快速切換勾選
            dgvReports.CellContentClick += (s, e) => {
                if (e.RowIndex >= 0 && e.ColumnIndex == 0)
                {
                    dgvReports.CommitEdit(DataGridViewDataErrorContexts.Commit);
                    UpdateReportSelectionSummary();
                }
            };

            // 點擊整列切換勾選
            dgvReports.CellClick += (s, e) => {
                if (e.RowIndex >= 0 && e.ColumnIndex != 0)
                {
                    var row = dgvReports.Rows[e.RowIndex];
                    bool cur = Convert.ToBoolean(row.Cells["colSelect"].Value);
                    row.Cells["colSelect"].Value = !cur;
                    dgvReports.CommitEdit(DataGridViewDataErrorContexts.Commit);
                    UpdateReportSelectionSummary();
                }
            };

            // 雙擊開啟關聯軟體 (Excel / Notepad / GBD Viewer)
            dgvReports.CellDoubleClick += (s, e) => {
                if (e.RowIndex >= 0)
                {
                    string fullPath = Convert.ToString(dgvReports.Rows[e.RowIndex].Cells["colFullPath"].Value);
                    if (File.Exists(fullPath))
                    {
                        try { System.Diagnostics.Process.Start(fullPath); }
                        catch (Exception ex) { MessageBox.Show("開啟檔案失敗: " + ex.Message, "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
                    }
                }
            };

            mainTable.Controls.Add(dgvReports, 0, 1);

            // ==========================================
            // 3. 底部配置與上傳操作面板 (Bottom Docked Area)
            // ==========================================
            Panel pnlBottom = new Panel()
            {
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(241, 245, 249),
                Padding = new Padding(10, 8, 10, 8),
                AutoScroll = true
            };

            // 3.1 壓縮檔名與選取摘要 (水平列)
            Label lblZip = new Label() { Text = "📦 壓縮檔名:", AutoSize = true, Location = new Point(10, 12), Font = new Font("微軟正黑體", 10f, FontStyle.Bold) };
            txtReportZipName = new TextBox() { Location = new Point(105, 8), Size = new Size(380, 26), Font = new Font("Consolas", 10f, FontStyle.Regular) };

            lblReportSelectionInfo = new Label()
            {
                Text = "已選取: 0 個檔案 (共 0 KB)",
                AutoSize = true,
                Location = new Point(500, 12),
                Font = new Font("微軟正黑體", 10.5f, FontStyle.Bold),
                ForeColor = Color.FromArgb(30, 41, 59)
            };

            // 3.2 上傳目標選擇器
            Label lblTarget = new Label() { Text = "🌐 上傳目標:", AutoSize = true, Location = new Point(10, 48), Font = new Font("微軟正黑體", 10f, FontStyle.Bold) };
            cmbUploadTarget = new ComboBox()
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Location = new Point(105, 44),
                Size = new Size(380, 26),
                Font = new Font("微軟正黑體", 10f, FontStyle.Bold)
            };
            cmbUploadTarget.Items.AddRange(new object[] {
                "1. GitHub Release 雲端 (推薦 / 直通下載 / 支援至2GB)",
                "2. Google Drive 雲端 (GAS Webhook / 兼發 Email)",
                "3. Firebase 雲端中心 (現成即用 / 支援網頁儀表板下載)",
                "4. 區域網路 NAS / 本機共享資料夾",
                "5. 僅壓縮另存本機 ZIP (Save to Local)"
            });
            int savedTarget = 0;
            int.TryParse(LoadConfigKey("ReportManager", "TargetIndex", "0"), out savedTarget);
            if (savedTarget < 0 || savedTarget >= cmbUploadTarget.Items.Count) savedTarget = 0;
            cmbUploadTarget.SelectedIndex = savedTarget;

            // 3.3 動態設定容器面板 (依據上傳目標切換不同欄位)
            pnlTargetConfigContainer = new Panel()
            {
                Location = new Point(10, 78),
                Size = new Size(820, 74),
                BackColor = Color.FromArgb(226, 232, 240),
                BorderStyle = BorderStyle.FixedSingle
            };

            BuildTargetConfigPanels();
            cmbUploadTarget.SelectedIndexChanged += (s, e) => SwitchTargetConfigView(cmbUploadTarget.SelectedIndex);

            // 3.4 核心上傳按鈕與進度條
            btnExecuteUpload = new Button()
            {
                Text = "🚀 壓縮選取檔案並立即上傳",
                Location = new Point(840, 10),
                Size = new Size(260, 60),
                BackColor = Color.FromArgb(220, 38, 38),
                ForeColor = Color.White,
                Font = new Font("微軟正黑體", 12f, FontStyle.Bold),
                Cursor = Cursors.Hand,
                FlatStyle = FlatStyle.Flat
            };
            btnExecuteUpload.FlatAppearance.BorderSize = 0;
            btnExecuteUpload.Click += (s, e) => ExecuteCompressAndUpload();

            prgReportTask = new ProgressBar()
            {
                Location = new Point(840, 80),
                Size = new Size(260, 22),
                Style = ProgressBarStyle.Blocks,
                Value = 0
            };

            lblReportStatus = new Label()
            {
                Text = "就緒",
                Location = new Point(840, 106),
                Size = new Size(260, 42),
                Font = new Font("微軟正黑體", 9.5f, FontStyle.Regular),
                ForeColor = Color.FromArgb(51, 65, 85)
            };

            // 3.5 即時日誌視窗 (右側伸展)
            Label lblLogTitle = new Label() { Text = "即時作業歷程:", Location = new Point(1115, 10), AutoSize = true, Font = new Font("微軟正黑體", 9.5f, FontStyle.Bold) };
            txtReportLogs = new TextBox()
            {
                Location = new Point(1115, 32),
                Size = new Size(450, 120),
                Multiline = true,
                ScrollBars = ScrollBars.Vertical,
                ReadOnly = true,
                BackColor = Color.FromArgb(15, 23, 42),
                ForeColor = Color.FromArgb(226, 232, 240),
                Font = new Font("Consolas", 9f, FontStyle.Regular)
            };

            pnlBottom.Controls.AddRange(new Control[] {
                lblZip, txtReportZipName, lblReportSelectionInfo,
                lblTarget, cmbUploadTarget, pnlTargetConfigContainer,
                btnExecuteUpload, prgReportTask, lblReportStatus,
                lblLogTitle, txtReportLogs
            });

            mainTable.Controls.Add(pnlBottom, 0, 2);
            tab.Controls.Add(mainTable);

            // 初始掃描並更新
            RefreshReportFileList();
            SwitchTargetConfigView(0);
        }

        /// <summary>
        /// 建立動態設定面板元件
        /// </summary>
        private void BuildTargetConfigPanels()
        {
            pnlTargetConfigContainer.Controls.Clear();

            // === GitHub Release 設定元件 ===
            Label lblGhRepo = new Label() { Text = "儲存庫:", Location = new Point(8, 10), AutoSize = true, Font = new Font("微軟正黑體", 9.5f, FontStyle.Bold) };
            txtGhOwner = new TextBox() { Location = new Point(62, 8), Size = new Size(115, 24), Font = new Font("Consolas", 9f) };
            txtGhOwner.Text = LoadConfigKey("GitHub", "Owner", "Isaacyang34");

            Label lblSlash = new Label() { Text = "/", Location = new Point(180, 10), AutoSize = true, Font = new Font("微軟正黑體", 10f, FontStyle.Bold) };

            txtGhRepo = new TextBox() { Location = new Point(195, 8), Size = new Size(115, 24), Font = new Font("Consolas", 9f) };
            txtGhRepo.Text = LoadConfigKey("GitHub", "Repo", "Homepage");

            Label lblGhTag = new Label() { Text = "標籤(Tag):", Location = new Point(318, 10), AutoSize = true, Font = new Font("微軟正黑體", 9.5f, FontStyle.Bold) };
            txtGhTag = new TextBox() { Location = new Point(390, 8), Size = new Size(140, 24), Font = new Font("Consolas", 9f) };
            txtGhTag.Text = LoadConfigKey("GitHub", "ReleaseTag", "Reports-Archive");

            btnGhTokenGuide = new Button()
            {
                Text = "🔑 取得 Token 教學",
                Location = new Point(538, 6),
                Size = new Size(145, 28),
                BackColor = Color.FromArgb(37, 99, 235),
                ForeColor = Color.White,
                Font = new Font("微軟正黑體", 9f, FontStyle.Bold),
                Cursor = Cursors.Hand,
                FlatStyle = FlatStyle.Flat
            };
            btnGhTokenGuide.FlatAppearance.BorderSize = 0;
            btnGhTokenGuide.Click += (s, e) => ShowGitHubTokenGuideDialog();

            btnGhOpenReleases = new Button()
            {
                Text = "🔗 檢視 Releases 頁面",
                Location = new Point(690, 6),
                Size = new Size(145, 28),
                BackColor = Color.FromArgb(15, 118, 110),
                ForeColor = Color.White,
                Font = new Font("微軟正黑體", 9f, FontStyle.Bold),
                Cursor = Cursors.Hand,
                FlatStyle = FlatStyle.Flat
            };
            btnGhOpenReleases.FlatAppearance.BorderSize = 0;
            btnGhOpenReleases.Click += (s, e) => OpenGitHubReleasesInBrowser();

            Label lblGhToken = new Label() { Text = "權杖 (PAT):", Location = new Point(8, 42), AutoSize = true, Font = new Font("微軟正黑體", 9.5f, FontStyle.Bold) };
            txtGhToken = new TextBox() { Location = new Point(85, 40), Size = new Size(330, 24), Font = new Font("Consolas", 9f) };
            txtGhToken.Text = LoadConfigKey("GitHub", "Token", "");

            Label lblGhHint = new Label()
            {
                Text = "💡 支援單檔 2GB 直通下載！上傳後自動產生公開直連網址並複製至剪貼簿。",
                Location = new Point(422, 42),
                AutoSize = true,
                Font = new Font("微軟正黑體", 9f),
                ForeColor = Color.FromArgb(30, 41, 59)
            };

            // === Google Drive / GAS 設定元件 ===
            Label lblWebhook = new Label() { Text = "Webhook 網址:", Location = new Point(8, 10), AutoSize = true, Font = new Font("微軟正黑體", 9.5f, FontStyle.Bold) };
            txtGasWebhookUrl = new TextBox() { Location = new Point(110, 8), Size = new Size(420, 24), Font = new Font("Consolas", 9f, FontStyle.Regular) };
            txtGasWebhookUrl.Text = LoadConfigKey("GoogleDrive", "WebhookUrl", "https://script.google.com/macros/s/AKfycbxIsvMF2IuTszl-wlr1wLZmTEGqyuX-ANnmhyrZKerhP3hXa73PPfp3PrIVMH9I14EL/exec");

            btnViewGasScript = new Button()
            {
                Text = "📋 檢視 GAS 腳本範本與教學",
                Location = new Point(540, 6),
                Size = new Size(260, 28),
                BackColor = Color.FromArgb(15, 118, 110),
                ForeColor = Color.White,
                Font = new Font("微軟正黑體", 9f, FontStyle.Bold),
                Cursor = Cursors.Hand,
                FlatStyle = FlatStyle.Flat
            };
            btnViewGasScript.FlatAppearance.BorderSize = 0;
            btnViewGasScript.Click += (s, e) => ShowGasScriptTemplateDialog();

            Label lblDriveFolder = new Label() { Text = "目標資料夾:", Location = new Point(8, 42), AutoSize = true, Font = new Font("微軟正黑體", 9.5f, FontStyle.Regular) };
            txtGasDriveFolder = new TextBox() { Location = new Point(110, 40), Size = new Size(180, 24), Text = "Dynamometer_Reports", Font = new Font("微軟正黑體", 9f) };

            Label lblEmail = new Label() { Text = "自動轉發信箱 (可選):", Location = new Point(305, 42), AutoSize = true, Font = new Font("微軟正黑體", 9.5f, FontStyle.Regular) };
            txtGasNotifyEmail = new TextBox() { Location = new Point(445, 40), Size = new Size(250, 24), Font = new Font("Consolas", 9f) };
            txtGasNotifyEmail.Text = LoadConfigKey("GoogleDrive", "NotifyEmail", "");

            // === 區域網路 NAS 設定元件 ===
            Label lblNas = new Label() { Text = "NAS / 共享路徑:", Location = new Point(8, 22), AutoSize = true, Font = new Font("微軟正黑體", 9.5f, FontStyle.Bold) };
            txtNasTargetPath = new TextBox() { Location = new Point(125, 20), Size = new Size(530, 24), Font = new Font("Consolas", 9f) };
            txtNasTargetPath.Text = LoadConfigKey("ReportManager", "NasPath", @"\\192.168.0.100\Reports");

            btnBrowseNas = new Button()
            {
                Text = "瀏覽...",
                Location = new Point(665, 18),
                Size = new Size(80, 28),
                BackColor = Color.White,
                Font = new Font("微軟正黑體", 9f)
            };
            btnBrowseNas.Click += (s, e) => {
                using (FolderBrowserDialog fbd = new FolderBrowserDialog())
                {
                    fbd.Description = "選擇報告備份目的資料夾 (本地硬碟或網路磁碟)";
                    if (fbd.ShowDialog(this) == DialogResult.OK)
                    {
                        txtNasTargetPath.Text = fbd.SelectedPath;
                    }
                }
            };
        }

        /// <summary>
        /// 切換上傳目標專屬面板
        /// </summary>
        private void SwitchTargetConfigView(int targetIdx)
        {
            pnlTargetConfigContainer.Controls.Clear();
            if (targetIdx == 0) // GitHub Release 雲端
            {
                Label lblGhRepo = new Label() { Text = "儲存庫:", Location = new Point(8, 10), AutoSize = true, Font = new Font("微軟正黑體", 9.5f, FontStyle.Bold) };
                Label lblSlash = new Label() { Text = "/", Location = new Point(180, 10), AutoSize = true, Font = new Font("微軟正黑體", 10f, FontStyle.Bold) };
                Label lblGhTag = new Label() { Text = "標籤(Tag):", Location = new Point(318, 10), AutoSize = true, Font = new Font("微軟正黑體", 9.5f, FontStyle.Bold) };
                Label lblGhToken = new Label() { Text = "權杖 (PAT):", Location = new Point(8, 42), AutoSize = true, Font = new Font("微軟正黑體", 9.5f, FontStyle.Bold) };
                Label lblGhHint = new Label()
                {
                    Text = "💡 支援單檔 2GB 直通下載！上傳後自動產生公開直連網址並複製至剪貼簿。",
                    Location = new Point(422, 42),
                    AutoSize = true,
                    Font = new Font("微軟正黑體", 9f),
                    ForeColor = Color.FromArgb(30, 41, 59)
                };

                pnlTargetConfigContainer.Controls.AddRange(new Control[] {
                    lblGhRepo, txtGhOwner, lblSlash, txtGhRepo, lblGhTag, txtGhTag,
                    btnGhTokenGuide, btnGhOpenReleases,
                    lblGhToken, txtGhToken, lblGhHint
                });
            }
            else if (targetIdx == 1) // Google Drive via GAS Webhook
            {
                Label lblWebhook = new Label() { Text = "Webhook 網址:", Location = new Point(8, 10), AutoSize = true, Font = new Font("微軟正黑體", 9.5f, FontStyle.Bold) };
                Label lblDriveFolder = new Label() { Text = "雲端資料夾:", Location = new Point(8, 42), AutoSize = true, Font = new Font("微軟正黑體", 9.5f, FontStyle.Regular) };
                Label lblEmail = new Label() { Text = "自動轉發信箱:", Location = new Point(310, 42), AutoSize = true, Font = new Font("微軟正黑體", 9.5f, FontStyle.Regular) };

                pnlTargetConfigContainer.Controls.AddRange(new Control[] {
                    lblWebhook, txtGasWebhookUrl, btnViewGasScript,
                    lblDriveFolder, txtGasDriveFolder,
                    lblEmail, txtGasNotifyEmail
                });
            }
            else if (targetIdx == 2) // Firebase 雲端中心
            {
                Label lblFbHint = new Label()
                {
                    Text = "⚡ 現成 0 設定：利用 HMI 現有的 BouncyCastle TLS 1.2 連線引擎，將報告同步至雲端監控中心。\n遠端網頁 (WebMonitor.html) 或手機開啟即可即時接收並一鍵下載此測試 ZIP！",
                    Location = new Point(15, 14),
                    Size = new Size(780, 48),
                    Font = new Font("微軟正黑體", 10.5f, FontStyle.Regular),
                    ForeColor = Color.FromArgb(15, 23, 42)
                };
                pnlTargetConfigContainer.Controls.Add(lblFbHint);
            }
            else if (targetIdx == 3) // NAS
            {
                Label lblNas = new Label() { Text = "NAS / 共享路徑:", Location = new Point(8, 22), AutoSize = true, Font = new Font("微軟正黑體", 9.5f, FontStyle.Bold) };
                pnlTargetConfigContainer.Controls.AddRange(new Control[] { lblNas, txtNasTargetPath, btnBrowseNas });
            }
            else if (targetIdx == 4) // 本機 ZIP
            {
                Label lblLocalHint = new Label()
                {
                    Text = "💾 本機打包：僅在 logs/ 或您指定的資料夾產生標準 PKZip 壓縮檔，不執行外部網路傳輸。",
                    Location = new Point(15, 24),
                    Size = new Size(780, 30),
                    Font = new Font("微軟正黑體", 10.5f, FontStyle.Regular),
                    ForeColor = Color.FromArgb(51, 65, 85)
                };
                pnlTargetConfigContainer.Controls.Add(lblLocalHint);
            }
        }

        /// <summary>
        /// 開啟 logs 目錄
        /// </summary>
        public void OpenLogDirectoryInExplorer()
        {
            try
            {
                string logDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs");
                if (!Directory.Exists(logDir)) Directory.CreateDirectory(logDir);
                System.Diagnostics.Process.Start("explorer.exe", logDir);
                WriteReportLog("已開啟檔案總管瀏覽目錄: " + logDir);
            }
            catch (Exception ex)
            {
                MessageBox.Show("開啟目錄失敗: " + ex.Message, "錯誤", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        /// <summary>
        /// 重新整理 logs 檔案清單
        /// </summary>
        public void RefreshReportFileList()
        {
            try
            {
                string logDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs");
                if (!Directory.Exists(logDir)) Directory.CreateDirectory(logDir);

                reportFileList.Clear();
                dgvReports.Rows.Clear();

                DirectoryInfo di = new DirectoryInfo(logDir);
                FileInfo[] files = di.GetFiles("*.*", SearchOption.TopDirectoryOnly);

                // 依最後修改時間降冪排列 (最新的在最上面)
                Array.Sort(files, (a, b) => b.LastWriteTime.CompareTo(a.LastWriteTime));

                int filterIdx = cmbReportFilter != null ? cmbReportFilter.SelectedIndex : 0;

                foreach (FileInfo fi in files)
                {
                    string ext = fi.Extension.ToLower();
                    if (filterIdx == 1 && ext != ".csv") continue;
                    if (filterIdx == 2 && ext != ".gbd") continue;
                    if (filterIdx == 3 && ext != ".log") continue;

                    ReportFileItem item = new ReportFileItem(fi.FullName);
                    reportFileList.Add(item);

                    int rowIdx = dgvReports.Rows.Add(false, item.FileName, item.FileType, item.SizeFormatted, item.LastModified.ToString("yyyy-MM-dd HH:mm:ss"), item.FullPath);
                    dgvReports.Rows[rowIdx].Tag = item;
                }

                // 自動生成預設 ZIP 檔名
                string motorName = !string.IsNullOrEmpty(motorModelName) ? motorModelName.Trim() : "DUT";
                motorName = motorName.Replace(" ", "_").Replace("/", "_").Replace("\\", "_");
                txtReportZipName.Text = string.Format("Report_{0}_{1}.zip", motorName, DateTime.Now.ToString("yyyyMMdd_HHmmss"));

                UpdateReportSelectionSummary();
                WriteReportLog(string.Format("已掃描 logs/ 目錄，共載入 {0} 個檔案。", reportFileList.Count));
            }
            catch (Exception ex)
            {
                WriteReportLog("掃描檔案清單失敗: " + ex.Message);
            }
        }

        /// <summary>
        /// 選取今日產生的檔案
        /// </summary>
        public void SelectTodayReports()
        {
            DateTime today = DateTime.Today;
            int count = 0;
            foreach (DataGridViewRow row in dgvReports.Rows)
            {
                ReportFileItem item = row.Tag as ReportFileItem;
                if (item != null && item.LastModified.Date == today)
                {
                    row.Cells["colSelect"].Value = true;
                    item.IsSelected = true;
                    count++;
                }
                else
                {
                    row.Cells["colSelect"].Value = false;
                    if (item != null) item.IsSelected = false;
                }
            }
            dgvReports.CommitEdit(DataGridViewDataErrorContexts.Commit);
            UpdateReportSelectionSummary();
            WriteReportLog(string.Format("已自動選取今日 ({0}) 產生的 {1} 個檔案。", today.ToString("yyyy-MM-dd"), count));
        }

        /// <summary>
        /// 選取最新一次測試 Session 的相關檔案 (CSV + GBD + LOG)
        /// </summary>
        public void SelectLatestSessionReports()
        {
            if (dgvReports.Rows.Count == 0) return;

            // 尋找最新的實測檔案 (優先以 CSV 遙測或 GBD 為基準)
            DateTime latestTime = DateTime.MinValue;
            foreach (DataGridViewRow row in dgvReports.Rows)
            {
                ReportFileItem item = row.Tag as ReportFileItem;
                if (item != null && item.LastModified > latestTime)
                {
                    latestTime = item.LastModified;
                }
            }

            if (latestTime == DateTime.MinValue) return;

            // 勾選該最新檔案時間前後 45 分鐘內的所有檔案 (視為同一場次試驗)
            int count = 0;
            DateTime windowStart = latestTime.AddMinutes(-45);
            DateTime windowEnd = latestTime.AddMinutes(15);

            foreach (DataGridViewRow row in dgvReports.Rows)
            {
                ReportFileItem item = row.Tag as ReportFileItem;
                if (item != null && item.LastModified >= windowStart && item.LastModified <= windowEnd)
                {
                    row.Cells["colSelect"].Value = true;
                    item.IsSelected = true;
                    count++;
                }
                else
                {
                    row.Cells["colSelect"].Value = false;
                    if (item != null) item.IsSelected = false;
                }
            }
            dgvReports.CommitEdit(DataGridViewDataErrorContexts.Commit);
            UpdateReportSelectionSummary();
            WriteReportLog(string.Format("已自動識別並勾選最新測試場次 ({0} 附近) 的 {1} 個關聯檔案。", latestTime.ToString("yyyy-MM-dd HH:mm"), count));
        }

        /// <summary>
        /// 全選或全不選
        /// </summary>
        public void SetAllReportsSelection(bool select)
        {
            foreach (DataGridViewRow row in dgvReports.Rows)
            {
                row.Cells["colSelect"].Value = select;
                ReportFileItem item = row.Tag as ReportFileItem;
                if (item != null) item.IsSelected = select;
            }
            dgvReports.CommitEdit(DataGridViewDataErrorContexts.Commit);
            UpdateReportSelectionSummary();
        }

        /// <summary>
        /// 更新已選取項目數量與大小統計
        /// </summary>
        public void UpdateReportSelectionSummary()
        {
            int selectedCount = 0;
            long totalBytes = 0;
            string singleZipName = null;
            bool hasNonZip = false;

            foreach (DataGridViewRow row in dgvReports.Rows)
            {
                bool isSel = Convert.ToBoolean(row.Cells["colSelect"].Value);
                ReportFileItem item = row.Tag as ReportFileItem;
                if (item != null)
                {
                    item.IsSelected = isSel;
                    if (isSel)
                    {
                        selectedCount++;
                        totalBytes += item.SizeBytes;
                        if (item.FileName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                        {
                            singleZipName = item.FileName;
                        }
                        else
                        {
                            hasNonZip = true;
                        }
                    }
                }
            }

            // 若僅選取單一 ZIP 檔案，自動將目標封包名稱同步為該 ZIP 檔名
            if (selectedCount == 1 && !hasNonZip && !string.IsNullOrEmpty(singleZipName) && txtReportZipName != null)
            {
                txtReportZipName.Text = singleZipName;
            }

            string sizeStr;
            if (totalBytes < 1024) sizeStr = totalBytes + " B";
            else if (totalBytes < 1024 * 1024) sizeStr = (totalBytes / 1024.0).ToString("F1") + " KB";
            else sizeStr = (totalBytes / (1024.0 * 1024.0)).ToString("F2") + " MB";

            string desc = string.Format("已選取: {0} 個檔案 (總計: {1})", selectedCount, sizeStr);
            if (selectedCount > 0 && !hasNonZip)
            {
                desc += " [已是 ZIP 封包，上傳將跳過二次壓縮]";
            }
            lblReportSelectionInfo.Text = desc;
            if (selectedCount > 0)
            {
                lblReportSelectionInfo.ForeColor = Color.FromArgb(16, 185, 129);
            }
            else
            {
                lblReportSelectionInfo.ForeColor = Color.FromArgb(100, 116, 139);
            }
        }

        /// <summary>
        /// 執行壓縮與上傳動作 (非同步背景執行，UI 不凍結)
        /// 若檔案已全為 ZIP 封包，則跳過本機二次壓縮直接上傳；除非清單中包含非 ZIP 檔案。
        /// </summary>
        public void ExecuteCompressAndUpload()
        {
            List<string> selectedFiles = new List<string>();
            foreach (DataGridViewRow row in dgvReports.Rows)
            {
                if (Convert.ToBoolean(row.Cells["colSelect"].Value))
                {
                    string path = Convert.ToString(row.Cells["colFullPath"].Value);
                    if (File.Exists(path)) selectedFiles.Add(path);
                }
            }

            if (selectedFiles.Count == 0)
            {
                MessageBox.Show("請先在清單中勾選欲上傳的報告或日誌檔案！", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            string zipName = txtReportZipName.Text.Trim();
            if (string.IsNullOrEmpty(zipName)) zipName = "Report_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".zip";
            if (!zipName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)) zipName += ".zip";

            int targetMode = cmbUploadTarget.SelectedIndex;
            string ghOwner = txtGhOwner != null ? txtGhOwner.Text.Trim() : "Isaacyang34";
            string ghRepo = txtGhRepo != null ? txtGhRepo.Text.Trim() : "Homepage";
            string ghTag = txtGhTag != null ? txtGhTag.Text.Trim() : "Reports-Archive";
            string ghToken = txtGhToken != null ? txtGhToken.Text.Trim() : "";
            string gasUrl = txtGasWebhookUrl != null ? txtGasWebhookUrl.Text.Trim() : "";
            string gasFolder = txtGasDriveFolder != null ? txtGasDriveFolder.Text.Trim() : "Dynamometer_Reports";
            string gasEmail = txtGasNotifyEmail != null ? txtGasNotifyEmail.Text.Trim() : "";
            string nasPath = txtNasTargetPath != null ? txtNasTargetPath.Text.Trim() : "";

            if (targetMode == 0) // GitHub Release
            {
                if (string.IsNullOrEmpty(ghToken))
                {
                    MessageBox.Show("您選擇了 GitHub Release 雲端發布，請先輸入【權杖 (PAT)】！\n若尚未取得，可點擊【🔑 取得 Token 教學】依步驟建立。", "請輸入 GitHub Token", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                if (string.IsNullOrEmpty(ghOwner) || string.IsNullOrEmpty(ghRepo))
                {
                    MessageBox.Show("請填寫 GitHub 儲存庫擁有者與名稱！", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
            }
            else if (targetMode == 1 && string.IsNullOrEmpty(gasUrl)) // Google Drive
            {
                MessageBox.Show("您選擇了 Google Drive (GAS Webhook) 上傳，請先填入 Webhook 網址！\n若尚未部署，可點擊【📋 檢視 GAS 腳本範本與教學】進行取得。", "請輸入 Webhook", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            // 判斷選取清單是否包含非 ZIP 檔案 (如 CSV, LOG, XLSX)
            bool hasNonZip = false;
            foreach (string path in selectedFiles)
            {
                if (!path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                {
                    hasNonZip = true;
                    break;
                }
            }

            // 依指示：如果已經是 zip 檔就不用再壓縮一次，除非有包括到非 zip 檔
            bool needCompress = hasNonZip;

            // 鎖定按鈕防止連點
            btnExecuteUpload.Enabled = false;
            prgReportTask.Value = 10;
            if (needCompress)
            {
                lblReportStatus.Text = "⏳ 正在本機壓縮檔案中...";
                WriteReportLog(string.Format("選取項目包含非 ZIP 檔案，開始壓縮 {0} 個檔案為 {1}...", selectedFiles.Count, zipName));
            }
            else
            {
                lblReportStatus.Text = "⚡ 已是 ZIP 檔案，略過本機壓縮直接上傳...";
                WriteReportLog(string.Format("選取檔案均為 ZIP 封包 ({0} 個)，略過本機重複壓縮，直接執行上傳...", selectedFiles.Count));
            }

            // 保存使用者偏好設定
            SaveConfigKey("ReportManager", "TargetIndex", targetMode.ToString());
            SaveConfigKey("GitHub", "Owner", ghOwner);
            SaveConfigKey("GitHub", "Repo", ghRepo);
            SaveConfigKey("GitHub", "ReleaseTag", ghTag);
            SaveConfigKey("GitHub", "Token", ghToken);
            SaveConfigKey("GoogleDrive", "WebhookUrl", gasUrl);
            SaveConfigKey("GoogleDrive", "NotifyEmail", gasEmail);
            SaveConfigKey("ReportManager", "NasPath", nasPath);

            ThreadPool.QueueUserWorkItem(state => {
                try
                {
                    List<string> targetZipPaths = new List<string>();

                    if (needCompress)
                    {
                        string tempZipPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs", zipName);

                        // 步驟 1: 執行 ZIP 壓縮 (優先使用本機 7-Zip，若無則切換內建純 C# 引擎)
                        string sevenZipExe = LightweightZipHelper.Find7ZipExecutable();
                        bool used7z = false;

                        if (!string.IsNullOrEmpty(sevenZipExe))
                        {
                            this.BeginInvoke((Action)(() => {
                                lblReportStatus.Text = "⏳ 偵測到本機 7-Zip，正在調用 7z.exe 極速壓縮中...";
                                WriteReportLog("偵測到本機 7-Zip 引擎: " + sevenZipExe);
                                prgReportTask.Value = 25;
                            }));

                            string err7z;
                            if (LightweightZipHelper.TryCompressWith7Zip(tempZipPath, selectedFiles, out err7z))
                            {
                                used7z = true;
                                this.BeginInvoke((Action)(() => {
                                    WriteReportLog("✅ 7-Zip 引擎壓縮完成！");
                                }));
                            }
                            else
                            {
                                this.BeginInvoke((Action)(() => {
                                    WriteReportLog("⚠️ 7-Zip 呼叫未完成 (" + err7z + ")，無縫切換為內建 PKZip 引擎繼續壓縮...");
                                }));
                            }
                        }

                        if (!used7z)
                        {
                            LightweightZipHelper.CreateZipArchive(tempZipPath, selectedFiles, (curr, total, name) => {
                                this.BeginInvoke((Action)(() => {
                                    int pct = 10 + (int)((double)curr / total * 30);
                                    if (pct > 40) pct = 40;
                                    prgReportTask.Value = pct;
                                    lblReportStatus.Text = string.Format("壓縮中 ({0}/{1}): {2}", curr, total, name);
                                }));
                            });
                        }

                        FileInfo zipFi = new FileInfo(tempZipPath);
                        long zipSize = zipFi.Length;
                        string zipSizeStr = (zipSize / 1024.0).ToString("F1") + " KB";
                        this.BeginInvoke((Action)(() => {
                            prgReportTask.Value = 50;
                            lblReportStatus.Text = string.Format("壓縮完成: {0} ({1})", zipName, zipSizeStr);
                            WriteReportLog(string.Format("✅ 壓縮成功: {0} (大小: {1})", zipName, zipSizeStr));
                        }));

                        targetZipPaths.Add(tempZipPath);
                    }
                    else
                    {
                        // 已經是 ZIP 檔案，直接使用既有 ZIP 路徑
                        targetZipPaths.AddRange(selectedFiles);
                        this.BeginInvoke((Action)(() => {
                            prgReportTask.Value = 50;
                            lblReportStatus.Text = string.Format("已確認 {0} 個現有 ZIP 封包，直接啟動上傳...", targetZipPaths.Count);
                        }));
                    }

                    // 步驟 2: 依目標執行上傳
                    for (int fIdx = 0; fIdx < targetZipPaths.Count; fIdx++)
                    {
                        string currentZipPath = targetZipPaths[fIdx];
                        string currentZipName = Path.GetFileName(currentZipPath);
                        int progressBase = 50 + (int)((double)fIdx / targetZipPaths.Count * 45);

                        if (targetMode == 0) // GitHub Release
                        {
                            this.BeginInvoke((Action)(() => {
                                prgReportTask.Value = progressBase + 10;
                                lblReportStatus.Text = string.Format("🚀 正在連線 GitHub API 查詢 Release ({0})...", ghTag);
                                WriteReportLog(string.Format("正在透過 BouncyCastle TLS 1.2 查詢 GitHub Release: {0}/{1} (Tag: {2})...", ghOwner, ghRepo, ghTag));
                            }));

                            Dictionary<string, string> ghHeaders = new Dictionary<string, string>();
                            ghHeaders["Authorization"] = "token " + ghToken;
                            ghHeaders["Accept"] = "application/vnd.github.v3+json";

                            // 1. 查詢 Release 是否存在
                            string releaseTagUrl = string.Format("https://api.github.com/repos/{0}/{1}/releases/tags/{2}", ghOwner, ghRepo, Uri.EscapeDataString(ghTag));
                            int queryStatus;
                            string releaseResp = SendHttpRequest("GET", releaseTagUrl, null, ghHeaders, null, 30000, out queryStatus);

                            long releaseId = 0;
                            string uploadUrlPattern = "";

                            if (queryStatus == 200 && !string.IsNullOrEmpty(releaseResp))
                            {
                                releaseId = ExtractJsonLongField(releaseResp, "id");
                                uploadUrlPattern = ExtractJsonStringField(releaseResp, "upload_url");
                            }
                            else if (queryStatus == 404)
                            {
                                // 2. Release 不存在，自動呼叫 API 建立
                                this.BeginInvoke((Action)(() => {
                                    WriteReportLog(string.Format("Release 標籤 {0} 尚未建立，正在自動為儲存庫建立新 Release...", ghTag));
                                }));

                                string createReleaseUrl = string.Format("https://api.github.com/repos/{0}/{1}/releases", ghOwner, ghRepo);
                                StringBuilder sbCreate = new StringBuilder();
                                sbCreate.Append("{");
                                sbCreate.AppendFormat("\"tag_name\": \"{0}\",", EscapeJson(ghTag));
                                sbCreate.AppendFormat("\"name\": \"{0}\",", EscapeJson("測試報告歸檔 (" + ghTag + ")"));
                                sbCreate.AppendFormat("\"body\": \"{0}\",", EscapeJson("馬達動力計自動發布之測試報告封包與歷史數據存檔"));
                                sbCreate.Append("\"draft\": false,");
                                sbCreate.Append("\"prerelease\": false");
                                sbCreate.Append("}");

                                int createStatus;
                                string createResp = SendHttpRequest("POST", createReleaseUrl, sbCreate.ToString(), ghHeaders, null, 30000, out createStatus);
                                if ((createStatus == 200 || createStatus == 201) && !string.IsNullOrEmpty(createResp))
                                {
                                    releaseId = ExtractJsonLongField(createResp, "id");
                                    uploadUrlPattern = ExtractJsonStringField(createResp, "upload_url");
                                    this.BeginInvoke((Action)(() => {
                                        WriteReportLog(string.Format("✅ 成功建立 GitHub Release (ID: {0})！", releaseId));
                                    }));
                                }
                                else
                                {
                                    throw new Exception(string.Format("建立 GitHub Release 失敗 (HTTP {0}): {1}", createStatus, createResp));
                                }
                            }
                            else if (queryStatus == 401 || queryStatus == 403)
                            {
                                throw new Exception(string.Format("GitHub 認證失敗 (HTTP {0}): 請檢查 Token (PAT) 是否正確且具備 repo 權限！\n伺服器回應: {1}", queryStatus, releaseResp));
                            }
                            else
                            {
                                throw new Exception(string.Format("查詢 GitHub Release 失敗 (HTTP {0}): {1}", queryStatus, releaseResp));
                            }

                            if (releaseId <= 0)
                            {
                                throw new Exception("無法解析 GitHub Release ID！");
                            }

                            // 3. 檢查是否存在同名資產，若有則先刪除，避免 422 衝突
                            string assetsUrl = string.Format("https://api.github.com/repos/{0}/{1}/releases/{2}/assets", ghOwner, ghRepo, releaseId);
                            int assetsStatus;
                            string assetsResp = SendHttpRequest("GET", assetsUrl, null, ghHeaders, null, 30000, out assetsStatus);
                            if (assetsStatus == 200 && !string.IsNullOrEmpty(assetsResp))
                            {
                                long dupAssetId = FindAssetIdByName(assetsResp, currentZipName);
                                if (dupAssetId > 0)
                                {
                                    this.BeginInvoke((Action)(() => {
                                        WriteReportLog(string.Format("發現同名資產 (ID: {0})，正在清除舊版本以利覆蓋更新...", dupAssetId));
                                    }));
                                    string delAssetUrl = string.Format("https://api.github.com/repos/{0}/{1}/releases/assets/{2}", ghOwner, ghRepo, dupAssetId);
                                    int delStatus;
                                    SendHttpRequest("DELETE", delAssetUrl, null, ghHeaders, null, 20000, out delStatus);
                                }
                            }

                            // 4. 上傳資產檔案
                            this.BeginInvoke((Action)(() => {
                                prgReportTask.Value = progressBase + 20;
                                lblReportStatus.Text = string.Format("🚀 正在上傳 {0} 至 GitHub Release Assets...", currentZipName);
                                WriteReportLog(string.Format("正在發送二進位封包至 uploads.github.com (大小: {0:F1} KB)...", new FileInfo(currentZipPath).Length / 1024.0));
                            }));

                            string uploadUrl = string.Format("https://uploads.github.com/repos/{0}/{1}/releases/{2}/assets?name={3}",
                                ghOwner, ghRepo, releaseId, Uri.EscapeDataString(currentZipName));

                            byte[] zipBytes = File.ReadAllBytes(currentZipPath);
                            Dictionary<string, string> uploadHeaders = new Dictionary<string, string>();
                            uploadHeaders["Authorization"] = "token " + ghToken;
                            uploadHeaders["Accept"] = "application/vnd.github.v3+json";

                            string contentType = currentZipName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) ? "application/zip" : "application/octet-stream";

                            int uploadStatus;
                            string uploadResp = SendHttpRequestRaw("POST", uploadUrl, zipBytes, contentType, uploadHeaders, null, 180000, out uploadStatus);

                            if (uploadStatus != 200 && uploadStatus != 201)
                            {
                                throw new Exception(string.Format("上傳資產至 GitHub 失敗 (HTTP {0}): {1}", uploadStatus, uploadResp));
                            }

                            string downloadUrl = ExtractJsonStringField(uploadResp, "browser_download_url");
                            if (string.IsNullOrEmpty(downloadUrl))
                            {
                                downloadUrl = string.Format("https://github.com/{0}/{1}/releases/download/{2}/{3}", ghOwner, ghRepo, ghTag, currentZipName);
                            }

                            this.BeginInvoke((Action)(() => {
                                prgReportTask.Value = 100;
                                lblReportStatus.Text = "🎉 GitHub Release 上傳成功！";
                                WriteReportLog("🎉 GitHub Release 發布成功！直通下載網址: " + downloadUrl);

                                try
                                {
                                    Clipboard.SetText(downloadUrl);
                                    WriteReportLog("📋 直通下載網址已自動複製至剪貼簿！");
                                }
                                catch { }

                                string msg = string.Format("🎉 測試報告已成功發布至 GitHub Release！\n\n檔案名稱: {0}\n儲存庫: {1}/{2} (標籤: {3})\n\n直通下載網址 (已自動複製至剪貼簿):\n{4}\n\n(任何電腦或手機點擊此網址即可直接下載)",
                                    currentZipName, ghOwner, ghRepo, ghTag, downloadUrl);
                                MessageBox.Show(msg, "GitHub Release 發布成功", MessageBoxButtons.OK, MessageBoxIcon.Information);
                            }));
                        }
                        else if (targetMode == 1) // Google Apps Script Webhook
                        {
                            this.BeginInvoke((Action)(() => {
                                prgReportTask.Value = progressBase + 10;
                                lblReportStatus.Text = string.Format("🚀 正在發送 {0} 至 Google 雲端 Webhook...", currentZipName);
                                WriteReportLog(string.Format("正在透過 Managed TLS 1.2 發送 {0} 至 Google Apps Script...", currentZipName));
                            }));

                            byte[] zipBytes = File.ReadAllBytes(currentZipPath);
                            string base64Zip = Convert.ToBase64String(zipBytes);

                            // 建立 JSON 負載
                            StringBuilder sbJson = new StringBuilder();
                            sbJson.Append("{");
                            sbJson.AppendFormat("\"filename\": \"{0}\",", EscapeJson(currentZipName));
                            sbJson.AppendFormat("\"folder\": \"{0}\",", EscapeJson(gasFolder));
                            sbJson.AppendFormat("\"email\": \"{0}\",", EscapeJson(gasEmail));
                            sbJson.AppendFormat("\"size\": {0},", zipBytes.Length);
                            sbJson.AppendFormat("\"data\": \"{0}\"", base64Zip);
                            sbJson.Append("}");

                            int statusCode;
                            string resp = SendHttpRequest("POST", gasUrl, sbJson.ToString(), null, 60000, out statusCode);

                            bool isSuccess = (statusCode == 200 || statusCode == 302) ||
                                             (!string.IsNullOrEmpty(resp) && (resp.Contains("\"status\":\"success\"") || resp.Contains("drive.google.com")));

                            if (!isSuccess && statusCode >= 400)
                            {
                                throw new Exception(string.Format("Google 雲端回應 HTTP {0}: {1}", statusCode, resp));
                            }

                            // 嘗試解析 Google 雲端硬碟檔案網址
                            string fileUrl = "";
                            if (!string.IsNullOrEmpty(resp) && resp.Contains("fileUrl"))
                            {
                                int idx = resp.IndexOf("\"fileUrl\":");
                                if (idx >= 0)
                                {
                                    int startQuote = resp.IndexOf('"', idx + 10);
                                    if (startQuote >= 0)
                                    {
                                        int endQuote = resp.IndexOf('"', startQuote + 1);
                                        if (endQuote > startQuote)
                                        {
                                            fileUrl = resp.Substring(startQuote + 1, endQuote - startQuote - 1).Replace("\\/", "/");
                                        }
                                    }
                                }
                            }

                            this.BeginInvoke((Action)(() => {
                                prgReportTask.Value = 100;
                                lblReportStatus.Text = "🎉 Google Drive 上傳成功！";
                                WriteReportLog("🎉 Google Drive 回應: " + (string.IsNullOrEmpty(resp) ? "已接收並確認建檔 (HTTP " + statusCode + ")" : resp));
                                string msg = "🎉 測試報告已成功上傳至 Google 雲端硬碟！\n檔案名稱: " + currentZipName;
                                if (!string.IsNullOrEmpty(fileUrl))
                                {
                                    msg += "\n\n檔案連結:\n" + fileUrl;
                                }
                                if (!string.IsNullOrEmpty(gasEmail))
                                {
                                    msg += "\n\n同時已自動透過 Email 寄出附件至: " + gasEmail;
                                }
                                MessageBox.Show(msg, "上傳完成", MessageBoxButtons.OK, MessageBoxIcon.Information);
                            }));
                        }
                        else if (targetMode == 2) // Firebase 雲端中心
                        {
                            this.BeginInvoke((Action)(() => {
                                prgReportTask.Value = progressBase + 15;
                                lblReportStatus.Text = string.Format("🚀 正在同步 {0} 至 Firebase 雲端中心...", currentZipName);
                                WriteReportLog(string.Format("正在將報告 {0} 寫入 Firebase RTDB (/reports/latest.json)...", currentZipName));
                            }));

                            byte[] zipBytes = File.ReadAllBytes(currentZipPath);
                            string base64Zip = Convert.ToBase64String(zipBytes);

                            StringBuilder sbReportJson = new StringBuilder();
                            sbReportJson.Append("{");
                            sbReportJson.AppendFormat("\"filename\": \"{0}\",", EscapeJson(currentZipName));
                            sbReportJson.AppendFormat("\"timestamp\": \"{0}\",", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                            sbReportJson.AppendFormat("\"sizeBytes\": {0},", zipBytes.Length);
                            sbReportJson.AppendFormat("\"filesCount\": {0},", needCompress ? selectedFiles.Count : 1);
                            sbReportJson.AppendFormat("\"data\": \"{0}\"", base64Zip);
                            sbReportJson.Append("}");

                            string fbUrl = "https://dynamometer-live-default-rtdb.asia-southeast1.firebasedatabase.app/reports/latest.json";
                            int fbStatus;
                            SendHttpRequest("PUT", fbUrl, sbReportJson.ToString(), null, 40000, out fbStatus);

                            this.BeginInvoke((Action)(() => {
                                prgReportTask.Value = 100;
                                lblReportStatus.Text = "🎉 Firebase 雲端中心同步完成！";
                                WriteReportLog("🎉 報告已推送至 Firebase 雲端中心，WebMonitor.html 可立即下載！");
                                MessageBox.Show("🎉 測試報告已同步至 Firebase 雲端中心！\n\n遠端監看儀表板 (WebMonitor.html) 或手機端已可即時一鍵下載此 ZIP 封包。",
                                    "同步完成", MessageBoxButtons.OK, MessageBoxIcon.Information);
                            }));
                        }
                        else if (targetMode == 3) // NAS
                        {
                            this.BeginInvoke((Action)(() => {
                                prgReportTask.Value = progressBase + 20;
                                lblReportStatus.Text = string.Format("🚀 正在複製 {0} 至 NAS 共享目錄...", currentZipName);
                            }));

                            if (!Directory.Exists(nasPath)) Directory.CreateDirectory(nasPath);
                            string destZip = Path.Combine(nasPath, currentZipName);
                            File.Copy(currentZipPath, destZip, true);

                            this.BeginInvoke((Action)(() => {
                                prgReportTask.Value = 100;
                                lblReportStatus.Text = "🎉 已備份至 NAS 共享目錄！";
                                WriteReportLog("✅ 成功複製報告至: " + destZip);
                                MessageBox.Show("✅ 測試報告已成功備份至指定網路路徑：\n" + destZip, "備份成功", MessageBoxButtons.OK, MessageBoxIcon.Information);
                            }));
                        }
                        else if (targetMode == 4) // 本機 ZIP
                        {
                            this.BeginInvoke((Action)(() => {
                                prgReportTask.Value = 100;
                                lblReportStatus.Text = "🎉 本機 ZIP 準備完成！";
                                WriteReportLog("✅ 本機 ZIP: " + currentZipPath);
                                MessageBox.Show("✅ 測試報告已於 logs/ 目錄備妥：\n" + currentZipPath, "完成", MessageBoxButtons.OK, MessageBoxIcon.Information);
                            }));
                        }
                    }
                }
                catch (Exception ex)
                {
                    try { MainForm.WriteHmiLog("REPORT_ERR", "測試報告打包或上傳異常: " + ex.ToString()); } catch { }
                    this.BeginInvoke((Action)(() => {
                        prgReportTask.Value = 0;
                        lblReportStatus.Text = "❌ 作業失敗: " + ex.Message;
                        WriteReportLog("❌ 作業異常: " + ex.ToString());
                        MessageBox.Show("執行失敗: " + ex.Message, "錯誤", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }));
                }
                finally
                {
                    this.BeginInvoke((Action)(() => {
                        btnExecuteUpload.Enabled = true;
                        RefreshReportFileList();
                    }));
                }
            });
        }

        public void WriteReportLog(string message)
        {
            try { MainForm.WriteHmiLog("REPORT", message); } catch { }
            if (txtReportLogs == null || txtReportLogs.IsDisposed) return;
            string timeStr = DateTime.Now.ToString("HH:mm:ss");
            string line = string.Format("[{0}] {1}\r\n", timeStr, message);
            if (txtReportLogs.InvokeRequired)
            {
                txtReportLogs.BeginInvoke((Action)(() => {
                    txtReportLogs.AppendText(line);
                }));
            }
            else
            {
                txtReportLogs.AppendText(line);
            }
        }

        /// <summary>
        /// 顯示 Google Apps Script 範本與三步驟部署教學對話框
        /// </summary>
        public void ShowGasScriptTemplateDialog()
        {
            Form dlg = new Form()
            {
                Text = "📋 Google Apps Script (GAS) 雲端硬碟 Webhook 部署指南",
                Size = new Size(820, 640),
                StartPosition = FormStartPosition.CenterParent,
                Font = new Font("微軟正黑體", 10f, FontStyle.Regular),
                BackColor = Color.FromArgb(248, 250, 252)
            };

            TableLayoutPanel table = new TableLayoutPanel()
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 3,
                Padding = new Padding(12)
            };
            table.RowStyles.Add(new RowStyle(SizeType.Absolute, 100f));
            table.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            table.RowStyles.Add(new RowStyle(SizeType.Absolute, 46f));

            Label lblIntro = new Label()
            {
                Dock = DockStyle.Fill,
                Text = "【三步極速部署教學】 (免 OAuth2 認證、完全相容 Windows XP！)\n" +
                       "1. 請用電腦瀏覽器登入 Google 雲端硬碟，點擊左上角【新增】→【更多】→【Google Apps Script】。\n" +
                       "2. 將下方代碼完整複製並覆蓋編輯器所有內容，點擊右上角【部署】→【新增部署作業】。\n" +
                       "3. 種類選【網頁應用程式】，將『誰可以存取』設定為【所有人 (Anyone)】，點擊部署並複製 Webhook 網址貼回 HMI 即可！",
                Font = new Font("微軟正黑體", 9.5f, FontStyle.Regular),
                ForeColor = Color.FromArgb(30, 41, 59)
            };

            string scriptCode =
@"// ====================================================================
// 動力計測試報告 Google Drive & Email 自動轉發 Webhook 腳本
// ====================================================================
function doPost(e) {
  try {
    var body = JSON.parse(e.postData.contents);
    var bytes = Utilities.base64Decode(body.data);
    var blob = Utilities.newBlob(bytes, 'application/zip', body.filename);
    
    // 1. 自動存入 Google Drive 指定資料夾
    var folderName = body.folder || 'Dynamometer_Reports';
    var folders = DriveApp.getFoldersByName(folderName);
    var targetFolder = folders.hasNext() ? folders.next() : DriveApp.createFolder(folderName);
    var file = targetFolder.createFile(blob);
    
    // 2. 若有設定信箱，自動寄送 Email 夾帶 ZIP 附件
    if (body.email && body.email.indexOf('@') > 0) {
      GmailApp.sendEmail(
        body.email,
        '【動力計測試報告】' + body.filename,
        '您好：\n\n測試報告已自動產生並存入 Google 雲端硬碟。\n檔案網址: ' + file.getUrl() + '\n\n附件包含原始遙測 CSV、GBD 溫度與系統日誌。',
        { attachments: [blob], name: '動力計實驗室自動通報' }
      );
    }
    
    return ContentService.createTextOutput(JSON.stringify({
      status: 'success',
      filename: body.filename,
      fileUrl: file.getUrl()
    })).setMimeType(ContentService.MimeType.JSON);
    
  } catch (err) {
    return ContentService.createTextOutput(JSON.stringify({
      status: 'error',
      message: err.toString()
    })).setMimeType(ContentService.MimeType.JSON);
  }
}";

            TextBox txtCode = new TextBox()
            {
                Dock = DockStyle.Fill,
                Multiline = true,
                ScrollBars = ScrollBars.Both,
                Font = new Font("Consolas", 10f, FontStyle.Regular),
                BackColor = Color.FromArgb(15, 23, 42),
                ForeColor = Color.FromArgb(148, 163, 184),
                Text = scriptCode
            };

            FlowLayoutPanel pnlBtn = new FlowLayoutPanel()
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.RightToLeft
            };
            Button btnClose = new Button() { Text = "關閉", Size = new Size(100, 36), BackColor = Color.FromArgb(100, 116, 139), ForeColor = Color.White, Font = new Font("微軟正黑體", 10f) };
            btnClose.Click += (s, e) => dlg.Close();

            Button btnCopy = new Button() { Text = "📋 複製全篇腳本代碼", Size = new Size(180, 36), BackColor = Color.FromArgb(16, 185, 129), ForeColor = Color.White, Font = new Font("微軟正黑體", 10f, FontStyle.Bold) };
            btnCopy.Click += (s, e) => {
                Clipboard.SetText(scriptCode);
                MessageBox.Show("腳本代碼已成功複製至剪貼簿！", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
            };

            pnlBtn.Controls.AddRange(new Control[] { btnClose, btnCopy });

            table.Controls.Add(lblIntro, 0, 0);
            table.Controls.Add(txtCode, 0, 1);
            table.Controls.Add(pnlBtn, 0, 2);
            dlg.Controls.Add(table);

            dlg.ShowDialog(this);
        }

        private void OpenGitHubReleasesInBrowser()
        {
            try
            {
                string owner = txtGhOwner != null ? txtGhOwner.Text.Trim() : "Isaacyang34";
                string repo = txtGhRepo != null ? txtGhRepo.Text.Trim() : "Homepage";
                string url = string.Format("https://github.com/{0}/{1}/releases", owner, repo);
                System.Diagnostics.Process.Start(url);
            }
            catch (Exception ex)
            {
                MessageBox.Show("無法開啟瀏覽器: " + ex.Message, "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }

        private void ShowGitHubTokenGuideDialog()
        {
            Form dlg = new Form()
            {
                Text = "GitHub Personal Access Token (PAT) 取得教學 (30秒快速設定)",
                Size = new Size(680, 480),
                StartPosition = FormStartPosition.CenterParent,
                FormBorderStyle = FormBorderStyle.FixedDialog,
                MaximizeBox = false,
                MinimizeBox = false,
                BackColor = Color.FromArgb(248, 250, 252)
            };

            TableLayoutPanel table = new TableLayoutPanel()
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 2,
                Padding = new Padding(12)
            };
            table.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            table.RowStyles.Add(new RowStyle(SizeType.Absolute, 50f));

            TextBox txtGuide = new TextBox()
            {
                Dock = DockStyle.Fill,
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical,
                Font = new Font("微軟正黑體", 10f),
                BackColor = Color.White,
                Text = 
@"【GitHub Personal Access Token (PAT) 取得步驟】

GitHub Release 允許您將測試報告與壓縮封包直傳至雲端，任何人點擊連結皆可「直接下載」，免除 Google Drive 繁複的轉址與下載阻擋！

步驟 1：開啟 GitHub Token 設定網頁
點擊下方【🌐 開啟 GitHub Token 建立頁面】按鈕，瀏覽器將自動開啟 GitHub 設定頁面。
(網址: https://github.com/settings/tokens/new)

步驟 2：填寫 Token 資訊
1. Note (備註)：輸入 Dynamometer-HMI (或其他易辨識名稱)。
2. Expiration (有效期限)：建議選擇 90 days 或 No expiration (永不過期)。
3. Select scopes (權限勾選)：
   ✅ 勾選【repo】(包含 repo:status, repo_deployment, public_repo, repo:invite 等完整儲存庫讀寫權限)。
   (若您的儲存庫為公開 Public Repo，亦可僅勾選 public_repo)。

步驟 3：建立並複製 Token
點擊頁面最下方的綠色按鈕【Generate token】。
畫面將顯示一組綠色開頭為 ghp_xxxxxxxxxxxxxxxxxxxx 的字串。
點擊旁邊的複製按鈕，並貼回動力計 HMI 的【權杖 (PAT)】欄位中即可！

💡 系統會自動將 Token 儲存於 dynamometer_layout.ini，後續完全不需要重新輸入！"
            };

            FlowLayoutPanel pnlBtn = new FlowLayoutPanel()
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.RightToLeft
            };
            Button btnClose = new Button() { Text = "確定", Size = new Size(90, 34), BackColor = Color.FromArgb(100, 116, 139), ForeColor = Color.White, Font = new Font("微軟正黑體", 9.5f) };
            btnClose.Click += (s, e) => dlg.Close();

            Button btnOpenUrl = new Button() { Text = "🌐 開啟 GitHub Token 建立頁面", Size = new Size(220, 34), BackColor = Color.FromArgb(37, 99, 235), ForeColor = Color.White, Font = new Font("微軟正黑體", 9.5f, FontStyle.Bold) };
            btnOpenUrl.Click += (s, e) => {
                try { System.Diagnostics.Process.Start("https://github.com/settings/tokens/new?scopes=repo&description=Dynamometer-HMI"); } catch { }
            };

            pnlBtn.Controls.AddRange(new Control[] { btnClose, btnOpenUrl });

            table.Controls.Add(txtGuide, 0, 0);
            table.Controls.Add(pnlBtn, 0, 1);
            dlg.Controls.Add(table);
            dlg.ShowDialog(this);
        }

        private static string ExtractJsonStringField(string json, string fieldName)
        {
            if (string.IsNullOrEmpty(json)) return "";
            string search = "\"" + fieldName + "\":";
            int idx = json.IndexOf(search, StringComparison.OrdinalIgnoreCase);
            if (idx < 0) return "";
            int quoteStart = json.IndexOf('"', idx + search.Length);
            if (quoteStart < 0) return "";
            int quoteEnd = json.IndexOf('"', quoteStart + 1);
            while (quoteEnd > 0 && json[quoteEnd - 1] == '\\')
            {
                quoteEnd = json.IndexOf('"', quoteEnd + 1);
            }
            if (quoteEnd > quoteStart)
            {
                return json.Substring(quoteStart + 1, quoteEnd - quoteStart - 1).Replace("\\/", "/");
            }
            return "";
        }

        private static long ExtractJsonLongField(string json, string fieldName)
        {
            if (string.IsNullOrEmpty(json)) return 0;
            string search = "\"" + fieldName + "\":";
            int idx = json.IndexOf(search, StringComparison.OrdinalIgnoreCase);
            if (idx < 0) return 0;
            int start = idx + search.Length;
            while (start < json.Length && (json[start] == ' ' || json[start] == '\t')) start++;
            int end = start;
            while (end < json.Length && (char.IsDigit(json[end]) || json[end] == '-')) end++;
            if (end > start)
            {
                long val;
                if (long.TryParse(json.Substring(start, end - start), out val)) return val;
            }
            return 0;
        }

        private static long FindAssetIdByName(string assetsJson, string targetName)
        {
            if (string.IsNullOrEmpty(assetsJson) || string.IsNullOrEmpty(targetName)) return 0;
            string search = "\"" + targetName + "\"";
            int idx = assetsJson.IndexOf(search, StringComparison.OrdinalIgnoreCase);
            if (idx < 0) return 0;

            int objStart = assetsJson.LastIndexOf('{', idx);
            if (objStart >= 0)
            {
                int len = Math.Min(assetsJson.Length - objStart, idx - objStart + search.Length + 50);
                string sub = assetsJson.Substring(objStart, len);
                return ExtractJsonLongField(sub, "id");
            }
            return 0;
        }

        private string LoadConfigKey(string section, string key, string defaultValue)
        {
            try
            {
                string iniPath = GetLayoutConfigPath();
                if (!File.Exists(iniPath)) return defaultValue;
                string[] lines = File.ReadAllLines(iniPath);
                bool inSec = false;
                foreach (string line in lines)
                {
                    string trimmed = line.Trim();
                    if (trimmed.StartsWith("[") && trimmed.EndsWith("]"))
                    {
                        inSec = trimmed.Equals("[" + section + "]", StringComparison.OrdinalIgnoreCase);
                        continue;
                    }
                    if (inSec && trimmed.StartsWith(key + "=", StringComparison.OrdinalIgnoreCase))
                    {
                        return trimmed.Substring(key.Length + 1).Trim();
                    }
                }
            }
            catch { }
            return defaultValue;
        }

        private void SaveConfigKey(string section, string key, string value)
        {
            try
            {
                string iniPath = GetLayoutConfigPath();
                List<string> lines = File.Exists(iniPath) ? new List<string>(File.ReadAllLines(iniPath)) : new List<string>();

                int secIdx = -1;
                for (int i = 0; i < lines.Count; i++)
                {
                    if (lines[i].Trim().Equals("[" + section + "]", StringComparison.OrdinalIgnoreCase))
                    {
                        secIdx = i;
                        break;
                    }
                }

                if (secIdx == -1)
                {
                    lines.Add("");
                    lines.Add("[" + section + "]");
                    lines.Add(key + "=" + value);
                }
                else
                {
                    int keyIdx = -1;
                    for (int i = secIdx + 1; i < lines.Count; i++)
                    {
                        string trimmed = lines[i].Trim();
                        if (trimmed.StartsWith("[") && trimmed.EndsWith("]")) break;
                        if (trimmed.StartsWith(key + "=", StringComparison.OrdinalIgnoreCase))
                        {
                            keyIdx = i;
                            break;
                        }
                    }

                    if (keyIdx != -1)
                    {
                        lines[keyIdx] = key + "=" + value;
                    }
                    else
                    {
                        lines.Insert(secIdx + 1, key + "=" + value);
                    }
                }

                File.WriteAllLines(iniPath, lines.ToArray(), Encoding.UTF8);
            }
            catch { }
        }
    }

    #endregion
}
