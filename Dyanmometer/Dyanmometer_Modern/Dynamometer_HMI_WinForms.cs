using System;
using System.Collections.Generic;
using System.Linq;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.IO.Ports;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;
using System.Diagnostics;

[assembly: AssemblyTitle("Dynamometer HMI")]
[assembly: AssemblyDescription("Electric Motor Dynamometer Acquisition & Control System")]
[assembly: AssemblyConfiguration("")]
[assembly: AssemblyCompany("Dynamometer Lab")]
[assembly: AssemblyProduct("Dynamometer HMI")]
[assembly: AssemblyCopyright("Copyright © 2026")]
[assembly: AssemblyTrademark("")]
[assembly: AssemblyCulture("")]
[assembly: ComVisible(false)]
[assembly: AssemblyVersion("2.7.3.0")]
[assembly: AssemblyFileVersion("2.7.3.0")]

namespace DynamometerHMI
{
    // 馬達效率地圖測試點位資料結構
    public class EffTestPoint
    {
        public int Index { get; set; }
        public int SpeedIdx { get; set; }
        public int TorqueIdx { get; set; }
        public double TargetSpeed { get; set; }
        public double TargetTorque { get; set; }
        public double MeasuredSpeed { get; set; }
        public double MeasuredTorque { get; set; }
        public double MeasuredPowerIn { get; set; }
        public double MeasuredPowerOut { get; set; }
        public double Efficiency { get; set; }
        public bool IsTested { get; set; }
        public bool IsPass { get; set; }
    }

    public partial class MainForm : Form
    {
        // Windows 核心 DLL 搜尋路徑擴展接口 (支援將廠商驅動 DLL 移入 DLL/ 子資料夾)
        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        public static extern bool SetDllDirectory(string lpPathName);

        // 橫河 YOKOGAWA 官方原廠 tmctl.dll P/Invoke 接口 (wire=4 為 Ethernet LAN)
        [DllImport("tmctl.dll", EntryPoint = "TmcInitialize", CharSet = CharSet.Ansi)]
        public static extern int TmcInitialize(int wire, string address, ref int id);

        [DllImport("tmctl.dll", EntryPoint = "TmcFinish")]
        public static extern int TmcFinish(int id);

        [DllImport("tmctl.dll", EntryPoint = "TmcSend", CharSet = CharSet.Ansi)]
        public static extern int TmcSend(int id, string msg);

        [DllImport("tmctl.dll", EntryPoint = "TmcReceive", CharSet = CharSet.Ansi)]
        public static extern int TmcReceive(int id, StringBuilder buff, int maxlen, ref int readlen);

        // 核心狀態
        private bool isRunning = false;
        private bool isLocked = false;
        public double baselineTorque = 0.0;
        public bool hasBaseline = false;
        public Button btnLockTorque;
        public Panel pnlTorqueTuneBar = null;
        public NumericUpDown numCardDeadband = null;
        public decimal trackingMaxDelta = 0.1m; // 閉迴路轉矩單步最大調量預設 0.1%
        public decimal trackingDeadband = 0.05m; // 轉矩變化死區預設 0.05 Nm
        public int trackingFilterWindowMs = 500;
        public int trackingControlIntervalMs = 200;
        public decimal trackingSafetyThresh = 10.0m;
        public Label lblDbTrqHint = null;
        public double cs19TorqueRef1 = 0.0;
        public double cs19TorqueRef2 = 0.0;
        public bool hasReadCs19 = false;
        public double calculatedMinDeadbandTorque = 0.0; // 依據真實讀取之 cs.19 / 1000 計算
        public Label lblCs19Disp1 = null;
        public Label lblCs19Disp2 = null;
        private DateTime lastCs19PollTime = DateTime.MinValue;

        // 待測端轉速鎖 (Speed Lock - 補償感應馬達 V/F 轉差)
        public Button btnLockSpeed = null;
        public bool hasSpeedBaseline = false;
        public bool isSpeedTracking = false;
        public double baselineSpeed = 0.0;
        public double smoothedSpeed = 0.0;
        public NumericUpDown numCardSpeedDeadband = null;
        public decimal trackingSpeedDeadband = 1.0m; // 轉速死區預設 1.0 rpm
        public decimal trackingSpeedMaxDelta = 1.0m; // 轉速單步最大補償量預設 1.0 rpm
        public NumericUpDown numCardTargetTorque = null;
        public NumericUpDown numCardTargetSpeed = null;
        private Queue<KeyValuePair<DateTime, double>> speedFilterQueue = new Queue<KeyValuePair<DateTime, double>>();
        private DateTime lastSpeedClosedLoopActionTime = DateTime.MinValue;
        private DateTime lastTorqueLogDiagTime = DateTime.MinValue;
        private DateTime lastSpeedLogDiagTime = DateTime.MinValue;
        private decimal activeTrackingTorquePct = 0.0m; // 轉矩閉迴路獨立內部給定狀態 (絕不受外部手動改動干擾)
        private decimal activeTrackingSpeedRpm = 0.0m;  // 轉速閉迴路獨立內部給定狀態 (絕不受外部手動改動干擾)

        public int rawDataIntervalMs = 1000;
        private DateTime lastManualRecordWriteTime = DateTime.MinValue;
        private DateTime lastAutoRecordWriteTime = DateTime.MinValue;
        private RawDataSampleAccumulator manualSampleAccumulator = new RawDataSampleAccumulator();
        private RawDataSampleAccumulator autoSampleAccumulator = new RawDataSampleAccumulator();

        // 即時量測數值
        public double actSpeed = 0.0;
        public double actTorque = 0.0;
        public double actMechPower = 0.0;
        public double actElecPower = 0.0;
        public double actEfficiency = 0.0;
        public double actKt = 0.0;
        public double actCurrentSigma = 0.0;
        public double actVoltageSigma = 0.0;
        public double actPf = 0.0;
        public double actTemp = 0.0; // 預設 0.0，未連線不假造溫度
        public bool isSimMode = false;
        private double simTick = 0;

        public System.Windows.Forms.Timer mainTimer;
        private System.Windows.Forms.Timer motorTempTimer;
        private static readonly Font fontConsolas12B = new Font("Consolas", 12f, FontStyle.Bold);
        private static readonly Font fontMsJhengHei10B = new Font("微軟正黑體", 10.5f, FontStyle.Bold);
        private Random rand = new Random();

        // Win32 GDI & USER 原生資源監測 (100% 相容 Windows XP x86 / Win7 / Win10 / Win11)
        [DllImport("user32.dll")]
        public static extern uint GetGuiResources(IntPtr hProcess, uint uiFlags);

        // 系統健康與資源觀測指標 (Health & Resource Observability)
        public Label lblSystemHealth;
        public static uint healthGdiCount = 0;
        public static uint healthUserCount = 0;
        public static long healthWorkingSetMb = 0;
        public static long healthPrivateBytesMb = 0;
        public static long healthGcHeapMb = 0;
        public static int healthThreadCount = 0;
        public static int healthUiLagMs = 0;
        public static int healthHandledErrorsCount = 0;
        private DateTime lastMainTickTime = DateTime.MinValue;
        private DateTime lastHealthLogTime = DateTime.MinValue;
        private int healthTickCounter = 0;

        // 實體硬體連線物件
        private SerialPort spTorque;
        private int ykDeviceId = -1;
        private TcpClient tcpPower;
        private NetworkStream streamPower;
        private TcpClient tcpGbd;
        private NetworkStream streamGbd;
        private bool isGbdReconnecting = false;
        private DateTime lastGbdReconnectAttempt = DateTime.MinValue;

        private TabControl tabControl;
        public Label lblSafetyStatus;

        // =========================================================================
        private Label lblSpeedVal, lblTorqueVal, lblPowerVal, lblElecPowerVal, lblEffVal, lblKtVal, lblTempVal, lblBaseTorqueDisp, lblSmoothTorqueDisp;
        public Label lblMotorTempDisplay;
        public MotorTempTrendControl motorTempChart;
        public NumericUpDown numSafetyThresh, numMaxDelta, numFilterWindow, numControlInterval, numDeadband;
        public NumericUpDown numUiRefreshInterval;
        public Button btnToggleClosedLoop;
        public bool isClosedLoopTracking = false;
        private Queue<KeyValuePair<DateTime, double>> torqueFilterQueue = new Queue<KeyValuePair<DateTime, double>>();
        private DateTime lastClosedLoopActionTime = DateTime.MinValue;
        public double smoothedTorque = 0.0;
        private TrackBar trkSpeed, trkTorque;
        private Button btnStart, btnStop, btnLock, btnSnapshot, btnExportCsv;
        public CheckBox chkSimMode;
        private CheckBox chkAutoClamp;
        // 手動 RAW DATA 錄製狀態與原始電文緩存
        private Button btnRecordRaw, btnSnapshotRaw, btnRawConfig, btnOpenLogsFolder;
        private Button btnRecordRawTop;
        private bool isLayoutLoaded = false;
        public string motorModelName = "SVM100S"; // 預設馬達名稱
        public string rawDataSaveDirectory = "";  // 自訂儲存目錄
        public bool enableAutoRawCsv = true; // 軌道 1: 每日全參數遙測 CSV (Auto_Raw_Telemetry_*.csv 每秒寫入)
        public bool enableSystemEventLog = true; // 軌道 2: 系統事件與通訊狀態日誌 (hmi_telemetry.log 檔案儲存)
        public bool[] gl820ChannelMask = new bool[20] { true, true, true, true, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false }; // CH1~CH20 獨立勾選遮罩 (預設 CH1~4 勾選)
        public string[] gl820ChannelNames = new string[20] { "CH1", "CH2", "CH3", "CH4", "CH5", "CH6", "CH7", "CH8", "CH9", "CH10", "CH11", "CH12", "CH13", "CH14", "CH15", "CH16", "CH17", "CH18", "CH19", "CH20" }; // 自訂通道名稱
        public bool recordKebRuParams = false; // 是否記錄 A/B 載台 ru 參數 (預設關閉以保持清爽)
        public bool recordEfficiency = true; // 效率值與 Kt (必要欄位，永久記錄)
        public bool recordRawHex = true; // 原始 Hex/ASCII (必要欄位，永久記錄)
        private bool isManualRecording = false;
        private string manualRecordFilePath = "";
        private int manualRecordCount = 0;
        private StreamWriter manualRecordWriter = null;
        private readonly object manualRecordLock = new object();
        private FileStream manualGbdStream = null;
        private BinaryWriter manualGbdWriter = null;
        private string manualRecordGbdPath = "";
        private DateTime manualGbdStartTime = DateTime.MinValue;
        private DateTime manualRecordStartTime = DateTime.MinValue; // 錄製起始時間 (計算總錄製時長)
        private bool isAutoTriggeredRecording = false; // 是否為自動測試觸發之錄製
        private string autoRecordTestTag = ""; // 自動測試標籤名稱
        private volatile string lastRawKistler = "";
        private volatile string lastRawWt333eHex = "";
        private volatile string lastRawKebA = "";
        private volatile string lastRawKebB = "";
        private int autoTelemetryCounter = 0;

        private Button btnCalibrationSettings;
        public static double scaleU1 = 1.0;
        public static double scaleU2 = 1.0;
        public static double scaleU3 = 1.0;
        public static double scaleI1 = 1.0;
        public static double scaleI2 = 1.0;
        public static double scaleI3 = 1.0;
        public static double scaleTorque = 1.0;

        private DataGridView dgvTelemetry;
        private TextBox txtRealtimeLog;
        private ComboBox cmbTorquePort;
        private TextBox txtPowerMeterIp, txtGbdIp;

        // 分頁 2: T-N 曲線 (增加角色切換、手動定錨與自適應梯度傳承、多點自訂轉速扭力模式)
        private ComboBox cmbTnMode; // 0: 等間距梯度掃描, 1: 多點自訂轉速扭力測試
        private ComboBox cmbTnModeMini;
        private Panel pnlTnStepRamp; // 等間距模式面板
        private Panel pnlTnMultiPoint; // 多點自訂模式面板
        private Panel pnlTnActions; // 動作按鈕面板
        private DataGridView dgvTnMultiPoints; // 多點轉速扭力配置表格
        private Button btnTnAddPoint, btnTnRemovePoint, btnTnResetPoints;
        public class TnCustomPoint
        {
            public int Index { get; set; }
            public double TargetSpeed { get; set; }
            public double TargetTorque { get; set; }
            public string Status { get; set; }
            public List<double[]> Samples { get; set; } // [spd, trq, mechPwr, elecPwr, eff, volt, curr, pf]
            public double AvgSpeed { get; set; }
            public double AvgTorque { get; set; }
            public double AvgMechPower { get; set; }
            public double AvgEfficiency { get; set; }
            public TnCustomPoint(int idx, double spd, double trq)
            {
                Index = idx;
                TargetSpeed = spd;
                TargetTorque = trq;
                Status = "待命";
                Samples = new List<double[]>();
            }
        }
        private List<TnCustomPoint> tnCustomPoints = new List<TnCustomPoint>();
        private int tnMultiCurrentIndex = 0;
        private int tnMultiSubPhase = 0; // 0: 待測提速, 1: 平穩加載, 2: 穩定5s等待, 3: 擷取30s, 4: 換項降載25%, 5: 完成
        private int tnMultiStabilizeCounter = 5;
        private int tnMultiSampleCounter = 30;
        private int tnMultiTransitionWaitSec = 0;
        private double tnMultiLastTestedTorque = 0.0;
        private double tnMultiLastTestedAdaptedPct = 0.0;
        private int tnRampDownStep = 0; // 0: 無/就緒, 1..3: 分三次平穩降載階梯
        private double tnRampDownStartPct = 0.0;
        private double tnRampDownTargetPct = 0.0;

        private NumericUpDown numTnStartRpm, numTnStepRpm, numTnEndRpm, numTnTorque, numTnDwell;
        private Button btnStartTn, btnStopTn, btnExportTn, btnTnAnchor;
        private TextBox txtTnTag;
        private double tnAdaptedTorquePct = 0.0; // 自適應梯度加載轉矩給定 % (無定錨嚴格從 0.0% 起步)
        private bool tnHasAnchor = false;
        private ComboBox cmbTnRole; // 0: A待測(速度)/B加載(轉矩), 1: B待測(速度)/A加載(轉矩)
        private Label lblTnAnchorStatus;
        private ProgressBar prgTn;
        private Label lblTnStatus, lblTnCountdown;
        private DataGridView dgvTnPoints;
        private TnCurveChart tnChart;
        // 核心架構重構：單一實例複用模式 (Shared Single Trend Instance)
        // 溫度記錄分頁 (tabGbd) 專屬獨立完整 gbdTrendChart；所有測試分頁 (TN, Duty, NoLoad) 共用 sharedTestTempTrend
        public GbdTemperatureTrendControl sharedTestTempTrend;
        public GbdTemperatureTrendControl tnTempTrend { get { return sharedTestTempTrend; } set { sharedTestTempTrend = value; } }
        public GbdTemperatureTrendControl dutyTempTrend { get { return sharedTestTempTrend; } set { sharedTestTempTrend = value; } }
        public GbdTemperatureTrendControl noLoadTempTrend { get { return sharedTestTempTrend; } set { sharedTestTempTrend = value; } }
        public GroupBox grpTnTemp;
        public GroupBox grpDutyTemp;
        public bool[] tnMonitoredChannels = new bool[20] { true, true, true, true, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false };
        private Button btnTnSelectChannels;
        private Label lblTnSelectedChHint, lblTnTempRealtimeVal;
        public ComboBox cmbTnTimeSpan;
        public ComboBox cmbDutyTimeSpan;
        private double tnCurrentSpeedCmd = 0.0; // T-N 待測端閉迴路追隨目標轉速 (補轉差)
        private System.Windows.Forms.Timer tnTimer;
        private int tnCurrentStep = 0;
        private int tnDwellRemaining = 0;
        private int tnPhase = 0; // 0: 等待轉速與轉矩收斂達標, 1: 穩定持載倒數中
        private int tnConvergeTimeoutSec = 0; // 逼近超時累計秒數
        private bool tnStepSpeedReached = false; // 當前階梯轉速是否已首次到位鎖定
        private int tnTrqSustainedSec = 0; // 轉矩穩定達標持續秒數 (抗慣性衝擊)
        private List<string[]> tnResults = new List<string[]>();

        // 分頁 3: 工作制測試 (S1 / S2 / S6 週期圖示與 V/F 雙重定錨補差)
        private ComboBox cmbDutyMode, cmbDutyRole;
        private NumericUpDown numDutySpeed, numDutyTorque, numS6CycleMin, numS6Cycles, numS6Ed;
        private Button btnStartDuty, btnStopDuty, btnExportDuty;
        private Button btnS6AnchorNoLoad, btnS6AnchorLoaded;
        private Label lblS6AnchorStatus, lblS6CalcInfo, lblDutyPhaseAction;
        private Label lblS6CycleLabel, lblS6EdLabel, lblS6CyclesLabel;
        private GroupBox grpS6Diagram;
        private Panel pnlS6Diagram; // S6 T=T1+T2 動態週期與溫升圖示面板
        private ProgressBar prgDuty;
        private Label lblDutyStatus, lblThermalStatus;
        private System.Windows.Forms.Timer dutyTimer;
        private int dutyElapsedSec = 0, dutyTotalSec = 0;
        private List<string[]> dutyResults = new List<string[]>();

        // S1 / S2 專屬控制項與溫度判定欄位
        private CheckBox chkS1ThermalStop;
        private Button btnS1SelectChannels;
        private Label lblS1SelectedChHint, lblS1ThermalStatus;
        public bool[] s1MonitoredChannels = new bool[20] { true, true, true, true, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false };
        private List<KeyValuePair<DateTime, double[]>> s1TempHistory = new List<KeyValuePair<DateTime, double[]>>();

        private Label lblS2Duration, lblS2TempCh, lblS2TempThresh, lblS2TempRealtime, lblS2ThermalStatus;
        private NumericUpDown numS2DurationMin, numS2TempThreshold;
        private CheckBox chkS2TempStop;
        private ComboBox cmbS2TempCh;
        private List<KeyValuePair<DateTime, double>> s2TempHistory = new List<KeyValuePair<DateTime, double>>();

        // S2 錨點測試相關欄位 (速度控制數據 SY52 / 扭力控制數據 CS18)
        private Label lblKebAutoModeHint;
        private Label lblS2AnchorTitle, lblS2AnchorHint, lblS2AnchorSy52, lblS2AnchorCs18;
        private NumericUpDown numS2AnchorSy52, numS2AnchorCs18;
        private Button btnS2RecordAnchor, btnS2ResetAnchor;
        public int s2AnchorSy52 = 0; // 0 表示尚未定錨，需執行錨點偵測
        public int s2AnchorCs18 = 0; // 0 表示尚未定錨，需執行錨點偵測
        private bool isS2Calibrating = false; // 是否正處於 S2 錨點偵測校驗狀態中
        private int s2CalibStage = 0; // 0: 提速, 1: 加載補差調節, 2: 雙達標穩定 10 秒倒數
        private int s2CalibTimer = 10; // 穩定 10 秒倒數計時器
        private bool isLoadMotorPreEnergized = false; // 方案 B：加載端低速激磁預備旗標 (避免高速上電反轉矩)

        // S6 V/F 自適應試運轉定錨與正式週期狀態機變數 (含明確錨點數值輸入與重置)
        private NumericUpDown numS6AnchorNoLoadSpd, numS6AnchorLoadedSpd, numS6AnchorLoadedCs18;
        private Button btnS6ResetAnchor;
        private Label lblS6TempCh, lblS6TempChHint;
        private double s6AnchorNoLoadSpeed = 0;
        private double s6AnchorLoadedSpeed = 0;
        private double s6AnchorLoadedTorquePct = 0.0;
        private bool s6HasNoLoadAnchor = false;
        private bool s6HasLoadedAnchor = false;
        private int s6DutyPhase = 0; // 0: 自適應試運轉定錨階段, 1: 正式週期循環階段
        private int s6TrialStage = 0; // 0:空載提速, 1:空載10s穩定, 2:加載補轉差, 3:加載10s穩定, 4:剩餘T1持載, 5:T2卸載空載冷卻
        private int s6TrialTimer = 0; // 10 秒穩定倒數計時器
        private int s6TrialStageElapsedSec = 0; // 當前子階段已運轉秒數 (防卡死看門狗保護)
        private int s6FormalCycleIndex = 1; // 正式週期次數 (1..N)
        private double s6CurrentSpeedCmd = 1000;
        private bool s6SpeedReached = false;
        private double s6AdaptedTorquePct = 0.0;
        private int s6CycleElapsedSec = 0;

        // S6 自適應階梯探測與動態比例加載演算法變數 (3步0.1%斜率學習 + 階段性PID動態逼近)
        private int s6ProbeStep = 0; // 0..3 (0.1% 三次階躍探測), 4: 探測完成進入動態逼近
        private double s6ProbeTorqueBase = 0.0;
        private double s6ProbeTorque1 = 0.0;
        private double s6ProbeTorque2 = 0.0;
        private double s6ProbeTorque3 = 0.0;
        private double s6NmPerPointOnePct = 0.4; // 每 0.1% 對應之轉矩 (Nm / 0.1%)
        private int s6Stage2StableCounter = 0; // 進入 Stage 3 需連續 3 秒穩定

        // 綜合監控右下角小視窗控制項 (與大分頁 100% 雙向即時連動)
        private ComboBox cmbTnRoleMini;
        private NumericUpDown numTnMiniStart, numTnMiniStep, numTnMiniEnd, numTnMiniTrq, numTnMiniDwell;
        private Button btnTnMiniStart, btnTnMiniStop, btnTnMiniAnchor;
        private TextBox txtTnMiniTag;
        private Label lblTnMiniAnchorStatus, lblTnMiniStatus, lblTnMiniCountdown;
        private ProgressBar prgTnMini;

        private ComboBox cmbDutyMiniMode, cmbDutyMiniRole;
        private NumericUpDown numDutyMiniSpd, numDutyMiniTrq, numDutyMiniCycleMin, numDutyMiniCycles, numDutyMiniEd;
        private Button btnDutyMiniStart, btnDutyMiniStop, btnS6MiniAnchorNoLoad, btnS6MiniAnchorLoaded, btnS6MiniResetAnchor;
        private Label lblS6MiniAnchorStatus, lblDutyMiniStatus, lblDutyMiniPhaseAction;
        private TableLayoutPanel tblDutyMini;
        private Label lblDutyMiniCycleLabel, lblDutyMiniAnchorLabel, lblDutyMiniDiagLabel;
        private FlowLayoutPanel pnlDutyMiniCycle, pnlDutyMiniAnchors;
        private Panel pnlS6MiniDiagram;
        private ProgressBar prgDutyMini;

        // Mini 視窗 S2 / S6 專屬錨點鏡像控制項
        private Label lblDutyMiniS2AnchorLabel;
        private FlowLayoutPanel pnlDutyMiniS2Anchors;
        private NumericUpDown numDutyMiniS2Sy52, numDutyMiniS2Cs18;
        private Button btnDutyMiniS2RecordAnchor, btnDutyMiniS2ResetAnchor;
        private NumericUpDown numDutyMiniS6NoLoadSpd, numDutyMiniS6LoadedSpd, numDutyMiniS6LoadedCs18;
        private bool isSyncingTnControls = false;
        private bool isSyncingDutyControls = false;

        // T-N、DUTY、EFF MAP、NO-LOAD 二維排版拖曳與記憶分割容器 (支援全分頁持久化)
        public SplitContainer splitTnMain, splitTnBottom;
        public SplitContainer splitTnRight;
        public SplitContainer splitDuty, splitDutyTop;
        public SplitContainer splitEff;

        // 跨分頁版面分割條永久記憶字典 (避免隱藏分頁 Height/Width=0 導致已存設定被覆蓋抹除)
        private readonly Dictionary<string, int> layoutSplitters = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        private int loadedActiveTab = -1;

        // DUTY 分頁專屬即時溫度波形與 S6 熱平衡/兩段式超溫防護欄位 (已由 sharedTestTempTrend 統一代理)
        private Label lblDutyTempTrendTitle, lblDutyTempRealtimeVal;
        private Label lblDutyAllChTempsDisp;
        private List<double> s6PeakTempHistory = new List<double>();
        private List<double> s6TroughTempHistory = new List<double>();
        private double s6CurrentCyclePeakTemp = -999.0;
        private double s6CurrentCycleTroughTemp = 999.0;
        private double s6LastCyclePeakTemp = 0.0;
        private double s6LastCycleTroughTemp = 0.0;
        public bool s6ThermalBalanced = false;
        private bool s6IsVerifyingConfirmationCycle = false;
        private int s6ConfirmationCycleIndex = 0;
        private List<KeyValuePair<DateTime, double>> s6TempHistory = new List<KeyValuePair<DateTime, double>>();
        private Label lblS6ThermalStatus;
        private NumericUpDown numS6WarnTemp, numS6TripTemp;
        private ComboBox cmbS6OvertempAction, cmbS6TempCh;
        private Panel pnlS6Overtemp;
        private DataGridView dgvDuty;
        private int dutyLastMinuteLogged = -1;
        private Label lblDutyAbStatus;
        private Label lblS6NoLoadTitle, lblS6LoadedTitle, lblS6Cs18Title;

        // 即時變頻器輸出電流 (ru.15, 0x020F / 0.1 A) 與輸出頻率 (ru.03, 0x0203 / Hz)
        public double kebCurrent1 = 0.0, kebCurrent2 = 0.0;
        public double kebFrequency1 = 0.0, kebFrequency2 = 0.0;

        // 頻率診斷對比追蹤變數 (PowerMeter vs KEB RU 暫時性比對 LOG，問題解決後可一鍵關閉)
        public volatile int lastRawRu00_1 = 0, lastRawRu00_2 = 0;
        public volatile int lastRawRu03_1 = 0, lastRawRu03_2 = 0;
        public volatile int lastRawRu07_1 = 0, lastRawRu07_2 = 0;
        public volatile bool enableFreqCompareLog = true;
        private DateTime lastFreqLogTime = DateTime.MinValue;

        // dr 銘牌參數精確反算之馬達極數 (P = round(120 * dr.05 / dr.01))
        public volatile int kebMotorPoles1 = 4, kebMotorPoles2 = 4;
        public double kebDrSpeed1 = 1750.0, kebDrSpeed2 = 1750.0;
        public double kebDrFreq1 = 60.0, kebDrFreq2 = 60.0;

        /// <summary>
        /// 即時輸出頻率 (Hz)。
        /// <summary>
        /// 全系統即時電氣頻率採納 (報表、T-N曲線、雲端遙測之統一頻率來源)
        /// 1. 若 Yokogawa PowerMeter WT333E 連線正常且量測到有效電氣頻率 (U頻率 > 2Hz 或 I頻率 > 2Hz)，以高精度功率計為黃金基準
        /// 2. 若 WT333E 離線或未通電，優先採納當前待測端 KEB 驅動器 ru.03 輸出頻率 (kebFrequency2 或 kebFrequency1)
        /// 3. 備援採納另一側 KEB 輸出頻率
        /// </summary>
        public double actFrequency
        {
            get
            {
                // 首要黃金基準：Yokogawa PowerMeter WT333E 實測電氣基波頻率 (直接硬體 CT/PT 物理量測，無轉差與通訊縮放誤差)
                if (wtFreqU > 2.0f)
                    return wtFreqU;
                if (wtFreqI > 2.0f)
                    return wtFreqI;

                // 次要基準：待測端 KEB ru.03 輸出頻率 (已由 ConvertKebRu03ToFrequency 依據速度範圍精確換算)
                bool isDrive1Dut = (cmbTnRole != null && cmbTnRole.SelectedIndex == 0) ||
                                  (noLoadSpdDrive == 1 && isNoLoadRunning) ||
                                  (cmbDutyRole != null && cmbDutyRole.SelectedIndex == 0);

                double dutKebFreq = isDrive1Dut ? kebFrequency1 : kebFrequency2;
                if (Math.Abs(dutKebFreq) > 0.1)
                    return Math.Abs(dutKebFreq);

                double otherKebFreq = isDrive1Dut ? kebFrequency2 : kebFrequency1;
                if (Math.Abs(otherKebFreq) > 0.1)
                    return Math.Abs(otherKebFreq);

                return 0.0;
            }
        }

        /// <summary>
        /// 暫時性頻率比對診斷 (同時撈取 POWERMETER 與 KEB RU 參數比對，輸出至 LOG 觀察)
        /// 包含：報告寫入值、PowerMeter U/I頻率、KEB A/B ru.03原始整數與換算Hz、實測轉速與dr反算電氣頻率
        /// </summary>
        public void CheckAndLogFrequencyComparison(string tag = "TELEMETRY")
        {
            if (!enableFreqCompareLog) return;

            DateTime now = DateTime.Now;
            bool isMotorActive = (Math.Abs(actSpeed) > 20.0 || isRunning || isNoLoadRunning ||
                (dutyTimer != null && dutyTimer.Enabled) ||
                (tnTimer != null && tnTimer.Enabled) ||
                (effMapTimer != null && effMapTimer.Enabled));
            double intervalSec = isMotorActive ? 2.0 : 10.0;

            if (tag == "TELEMETRY" && (now - lastFreqLogTime).TotalSeconds < intervalSec) return;
            lastFreqLogTime = now;

            int dutDrive = 2; // 目前依現場硬體規範：僅 B 載台下轉速命令(待測端)
            try
            {
                if (cmbTnRole != null && cmbTnRole.SelectedIndex == 0) dutDrive = 1;
                else if (noLoadSpdDrive == 1 && isNoLoadRunning) dutDrive = 1;
                else if (cmbDutyRole != null && cmbDutyRole.SelectedIndex == 0) dutDrive = 1;
            }
            catch { }
            string dutDriveName = (dutDrive == 2) ? "B載台(待測-轉速控制)" : "A載台(加載)";

            // ★ 依 dr 參數精確反算極數與理論電氣頻率: P = round(120 * dr.05 / dr.01), f = P * n / 120
            int poles = (kebMotorPoles2 > 0) ? kebMotorPoles2 : 4;
            double drCalculatedFreq = Math.Abs(actSpeed) * poles / 120.0;

            string logMsg = string.Format(
                "【頻率比對診斷 - {0}】報告採納值={1:F2}Hz ({2}) | " +
                "PowerMeter[U頻率={3:F2}Hz, I頻率={4:F2}Hz] | " +
                "KEB_B待測[ru03_raw={5}, 換算={6:F2}Hz, ru07_spd={7}rpm, ru00={8}] | " +
                "KEB_A加載[ru03_raw={9}, 換算={10:F2}Hz, ru07_spd={11}rpm, ru00={12}] | " +
                "實測轉速={13:F1}rpm -> dr精確反算電氣頻率({14}極)={15:F2}Hz [dr01={16:F0}rpm, dr05={17:F1}Hz]",
                tag, actFrequency, dutDriveName,
                wtFreqU, wtFreqI,
                lastRawRu03_2, kebFrequency2, lastRawRu07_2, lastRawRu00_2,
                lastRawRu03_1, kebFrequency1, lastRawRu07_1, lastRawRu00_1,
                actSpeed, poles, drCalculatedFreq, kebDrSpeed2, kebDrFreq2
            );

            WriteHmiLog("FREQ_COMPARE", logMsg);
        }

        // DUTY 過電流保護變數 (S1 / S2 / S6)
        public decimal dutyOverCurrentPercent = 25m;      // 門檻百分比 (預設 25%)
        public decimal dutyOverCurrentDelaySec = 10m;      // 防抖判定持續時間 (預設 10s)
        public double dutyBaselineCurrentSigma = 0.0; // 基準 WT333E Sigma 電流 (A)
        public double dutyBaselineCurrentDrive = 0.0; // 基準 待測端驅動器電流 (A)
        public volatile bool isDutyBaselineEstablished = false; // 是否已完成採樣確立基準
        public DateTime dutyOverCurrentStartTime = DateTime.MinValue; // 過電流跳脫計時器
        private Queue<KeyValuePair<DateTime, double>> dutySigmaCurrentSamples = new Queue<KeyValuePair<DateTime, double>>();
        private Queue<KeyValuePair<DateTime, double>> dutyDriveCurrentSamples = new Queue<KeyValuePair<DateTime, double>>();
        private NumericUpDown numDutyOverCurrentPct, numDutyOverCurrentDelay;
        private Label lblDutyCurrentBaseline;

        // T-N 測試過電流保護變數 (依據 Kt 換算理論電流)
        public decimal tnMotorKt = 1.0m;                   // 轉矩常數 Kt (Nm/A, 預設 1.0)
        public decimal tnOverCurrentPercent = 25m;         // 門檻百分比 (預設 25%)
        public decimal tnOverCurrentDelaySec = 8m;         // 判定時間 (預設為穩定時間16s的一半 = 8s)
        public DateTime tnOverCurrentStartTime = DateTime.MinValue; // TN 過電流跳脫計時器
        private NumericUpDown numTnKt, numTnOverCurrentPct, numTnOverCurrentDelay;

        // 分頁 4: 效率地圖 (雙向梯度自動掃描與即時 2D 熱力圖採樣)
        private ComboBox cmbEffRole;
        private NumericUpDown numEffStartSpd, numEffStepSpd, numEffEndSpd;
        private NumericUpDown numEffStartTrq, numEffStepTrq, numEffEndTrq;
        private NumericUpDown numEffDwell;
        private Button btnStartEffMap, btnStopEffMap, btnGenEffMap, btnExportEffMap;
        private Label lblEffPointSummary, lblEffMapStatus, lblEffMapCountdown, lblEffMapPeak;
        private ProgressBar prgEffMap;
        private DataGridView dgvEffMap;
        private EfficiencyHeatmapControl effHeatmap;
        private System.Windows.Forms.Timer effMapTimer;
        private List<EffTestPoint> effPointList = new List<EffTestPoint>();
        private double[] effSpeedAxis = new double[] { 500, 1000, 1500, 2000, 2500, 3000, 3500, 4000 };
        private double[] effTorqueAxis = new double[] { 5, 10, 15, 20, 25, 30, 35, 40, 45, 50 };
        private double[,] effGridData = new double[10, 8];
        private int effCurrentPointIdx = 0;
        private int effPhase = 0; // 0: 升速到位與平穩加載逼近, 1: 穩定持載倒數
        private int effConvergeTimeoutSec = 0;
        private bool effStepSpeedReached = false;
        private int effTrqSustainedSec = 0;
        private int effDwellRemaining = 0;
        private double effAdaptedTorquePct = 0.0;
        private List<string[]> effResults = new List<string[]>();

        // 雙 KEB 變頻驅動控制
        private ComboBox cmbHmiKebPort1, cmbHmiKebBaud1, cmbHmiKebPort2, cmbHmiKebBaud2;
        private NumericUpDown numHmiKebNode1, numHmiKebNode2;
        private NumericUpDown numHmiKebSpeed1, numHmiKebTorque1, numHmiKebSpeed2, numHmiKebTorque2;
        private TrackBar trkHmiKebSpeed1, trkHmiKebTorque1, trkHmiKebSpeed2, trkHmiKebTorque2;
        private Label lblHmiKebStatus1, lblHmiKebStatus2;
        private Button btnHmiPortToggle1, btnHmiPortToggle2; // [Open] / [Close] 埠連接與釋放切換按鈕
        private Label lblKebModeParams1, lblKebModeParams2; // 下方空白處：即時模式內部控制變數狀態顯示
        private DataGridView dgvKebRu1, dgvKebRu2;
        private Button bHmiM1_1, bHmiM1_2, bHmiM1_3, bHmiM1_4;
        private Button bHmiM2_1, bHmiM2_2, bHmiM2_3, bHmiM2_4;
        private Control[] hmiSpdControls1, hmiTrqControls1, hmiSpdControls2, hmiTrqControls2;
        private Button btnRF1, btnRR1, btnStop1, btnReset1;
        private Button btnRF2, btnRR2, btnStop2, btnReset2;
        private TableLayoutPanel pnlRun1, pnlRun2;
        private SerialPort spKeb1, spKeb2;
        private TextBox txtHmiKebLog;
        private System.Windows.Forms.Timer tmrHmiKeb;
        private int hmiKebCount1 = 0, hmiKebCount2 = 0;
        private SplitContainer splitDrives, splitDrive1, splitDrive2, splitBottomHorizontal, splitMainVertical;
        public SplitContainer splitParam1, splitParam2; // Mode parameters 可調整高度分割器
        public int currentKebMode1 = 7, currentKebMode2 = 8; // 當前 KEB 模式 (7:數位速度, 8:數位轉矩, 9:類比速度, 10:類比轉矩)
        public int lastSy50Cmd1 = 0, lastSy50Cmd2 = 0; // 最新發送的 Sy50 控制字 (0:STOP, 4:RF正轉, 12:RR反轉, 2:RESET)
        public int lastRu00_1 = -1, lastRu00_2 = -1; // 追蹤 A/B 載台 ru.00 運轉狀態機 (nOP, LS, FAcc, FdEc, Fcon 等)
        public int lastKebFaultCode1 = 0, lastKebFaultCode2 = 0; // 追蹤 A/B 載台 ru.43 (0x022B) 硬體故障碼 (0=正常, >0=報警)
        private KebBackupParams kebBackup1 = new KebBackupParams();
        private KebBackupParams kebBackup2 = new KebBackupParams();

        // KEB 斷線緩衝與連鎖安全防護 (可於【🎯 追蹤 / 日誌】設定)
        public decimal disconnectBufferSeconds = 5.0m; // 斷線緩衝時間 (預設 5 秒，範圍 1~60 秒)
        public NumericUpDown numDisconnectBuffer;
        public decimal autoStopBrakeThresholdRpm = 500.0m; // 自動停機加載煞車卸載轉速門檻 (預設 500 rpm，可於【🎯 追蹤 / 日誌】設定)
        public NumericUpDown numBrakeThreshLog; // 日誌 Tab 工具列停機卸載門檻控制項
        public DateTime lastKebSuccessTime1 = DateTime.Now;
        public DateTime lastKebSuccessTime2 = DateTime.Now;
        public bool isKebReconnecting1 = false;
        public bool isKebReconnecting2 = false;
        public bool isKebTimeoutTriggered1 = false;
        public bool isKebTimeoutTriggered2 = false;

        // 全自動安全保護矩陣 (左邊勾選啟用，右邊量與時間雙參數)
        public bool enableProtTorqueLoss = true;
        public decimal protTorqueTimeoutSec = 1.5m; // 斷線時間
        public DateTime lastTorquePacketTime = DateTime.Now;

        public bool enableProtStall = true;
        public decimal protStallSpeedThreshold = 50.0m; // 堵轉速度量 (rpm)
        public decimal protStallDelaySec = 2.0m; // 堵轉判定時間 (秒)
        public DateTime stallStartTime = DateTime.MinValue;

        public bool enableProtOvertemp = true;
        public decimal protWarnTempThreshold = 85.0m; // 警告溫度量 (°C)
        public decimal protMaxTempThreshold = 105.0m; // 停機溫度量 (°C)
        public decimal protTempDelaySec = 3.0m; // 超溫持續時間 (秒)
        public DateTime tempTripStartTime = DateTime.MinValue;
        public bool tempWarnTriggered = false;

        public bool enableProtCurrentImbalance = true;
        public decimal protImbalancePercentThreshold = 25.0m; // 不平衡率門檻量 (%)
        public decimal protImbalanceDelaySec = 2.0m; // 不平衡持續時間 (秒)
        public DateTime imbalanceStartTime = DateTime.MinValue;
        public bool imbalanceWarnTriggered = false;

        public bool enableProtKebFault = true;

        // KEB 監控參數自訂項目結構 (支援使用者自由增減/刪除/還原)
        public class KebMonitorItem
        {
            public string Name { get; set; }
            public int Address { get; set; }
            public double Scale { get; set; }
            public string Unit { get; set; }
            public bool IsHex { get; set; }
            public bool IsStatus { get; set; }
            public bool IsNode { get; set; }

            public KebMonitorItem(string name, int addr, double scale, string unit, bool isHex = false, bool isStatus = false, bool isNode = false)
            {
                Name = name; Address = addr; Scale = scale; Unit = unit; IsHex = isHex; IsStatus = isStatus; IsNode = isNode;
            }
        }

        private List<KebMonitorItem> kebMonitorList1 = new List<KebMonitorItem>();
        private List<KebMonitorItem> kebMonitorList2 = new List<KebMonitorItem>();

        // 全設備一鍵連線與通訊狀態藥丸燈號 (Device Connection Status Pills)
        private Label lblPillTorque, lblPillPowerMeter, lblPillGbd, lblPillKeb1, lblPillKeb2;
        private Button btnMasterConnectAll, btnMasterDisconnectAll, btnFontCustomizer, btnDeviceSettings;
        public string torquePortName = "COM4";
        public int torqueBaudRate = 1000000;
        public string powerMeterIp = "192.168.0.11";
        public int powerMeterPort = 502;
        public string gbdIp = "192.168.0.3";
        public int gbdPort = 8023;
        public string kebPort1 = "COM1";
        public int kebBaud1 = 38400;
        public int kebNode1 = 1;
        public string kebPort2 = "COM2";
        public int kebBaud2 = 38400;
        public int kebNode2 = 1;
        private int failSafeHeartbeatMisses = 0;

        // 可自訂字體大小參數 (具備預設值與 INI 自動記憶功能)
        public float fontMetricValPt = 18f;
        public float fontMetricTitlePt = 10.5f;
        public float fontTelemetryGridPt = 9.5f;
        public float fontKebRuGridPt = 9.5f;
        public float fontKebNumericPt = 21f; // 轉速/轉矩大數值輸入框字體大小 (預設 21pt，高度與左右按鈕 100% 齊平)
        public float fontGbdGridPt = 9.5f;
        public float fontLogTextPt = 9f;
        private List<Label> listMetricValueLabels = new List<Label>();
        private List<Label> listMetricTitleLabels = new List<Label>();
        private List<Control> listKebControls = new List<Control>();
        private List<Control> listKebSafetyLockControls = new List<Control>();

        public void ApplyDeviceSettings()
        {
            try
            {
                if (lblPillTorque != null) lblPillTorque.Text = " 扭力: " + torquePortName + " ";
                if (lblPillPowerMeter != null) lblPillPowerMeter.Text = " WT333E: " + powerMeterPort + " ";
                if (lblPillGbd != null) lblPillGbd.Text = " GL820: " + gbdPort + " ";

                if (cmbHmiKebPort1 != null && cmbHmiKebPort1.Items.Contains(kebPort1)) cmbHmiKebPort1.SelectedItem = kebPort1;
                if (cmbHmiKebBaud1 != null && cmbHmiKebBaud1.Items.Contains(kebBaud1.ToString())) cmbHmiKebBaud1.SelectedItem = kebBaud1.ToString();
                if (numHmiKebNode1 != null) numHmiKebNode1.Value = kebNode1;

                if (cmbHmiKebPort2 != null && cmbHmiKebPort2.Items.Contains(kebPort2)) cmbHmiKebPort2.SelectedItem = kebPort2;
                if (cmbHmiKebBaud2 != null && cmbHmiKebBaud2.Items.Contains(kebBaud2.ToString())) cmbHmiKebBaud2.SelectedItem = kebBaud2.ToString();
                if (numHmiKebNode2 != null) numHmiKebNode2.Value = kebNode2;

                if (txtPowerMeterIp != null) txtPowerMeterIp.Text = powerMeterIp;
                if (txtGbdIp != null) txtGbdIp.Text = gbdIp;

                SaveLayoutConfig();
                WriteHmiLog("CONFIG", string.Format("[OK] 設備通訊設定已套用: 扭力計={0}, WT333E={1}:{2}, GL820={3}:{4}, KEB1={5}, KEB2={6}",
                    torquePortName, powerMeterIp, powerMeterPort, gbdIp, gbdPort, kebPort1, kebPort2));
            }
            catch {}
        }

        public void ApplyFontSizes()
        {
            try
            {
                foreach (var lbl in listMetricValueLabels)
                {
                    if (lbl != null) lbl.Font = new Font("Consolas", fontMetricValPt, FontStyle.Bold);
                }
                foreach (var lbl in listMetricTitleLabels)
                {
                    if (lbl != null) lbl.Font = new Font("微軟正黑體", fontMetricTitlePt, FontStyle.Bold);
                }
                foreach (var ctrl in listKebControls)
                {
                    if (ctrl != null)
                    {
                        if (ctrl == numHmiKebSpeed1 || ctrl == numHmiKebTorque1 || ctrl == numHmiKebSpeed2 || ctrl == numHmiKebTorque2)
                        {
                            ctrl.Font = new Font("Consolas", fontKebNumericPt, FontStyle.Bold);
                        }
                        else
                        {
                            FontStyle st = (ctrl.Font != null) ? ctrl.Font.Style : FontStyle.Regular;
                            string fam = (ctrl is NumericUpDown) ? "Consolas" : "微軟正黑體";
                            ctrl.Font = new Font(fam, fontKebRuGridPt, st);
                        }
                    }
                }
                if (dgvTelemetry != null)
                {
                    dgvTelemetry.Font = new Font("微軟正黑體", fontTelemetryGridPt, FontStyle.Regular);
                    dgvTelemetry.ColumnHeadersDefaultCellStyle.Font = new Font("微軟正黑體", fontTelemetryGridPt, FontStyle.Bold);
                    dgvTelemetry.RowTemplate.Height = (int)(fontTelemetryGridPt * 2.2) + 8;
                    foreach (DataGridViewRow r in dgvTelemetry.Rows)
                    {
                        r.Height = (int)(fontTelemetryGridPt * 2.2) + 8;
                    }
                }
                if (dgvKebRu1 != null) { dgvKebRu1.Font = new Font("Consolas", fontKebRuGridPt, FontStyle.Regular); dgvKebRu1.ColumnHeadersDefaultCellStyle.Font = new Font("微軟正黑體", fontKebRuGridPt, FontStyle.Bold); }
                if (dgvKebRu2 != null) { dgvKebRu2.Font = new Font("Consolas", fontKebRuGridPt, FontStyle.Regular); dgvKebRu2.ColumnHeadersDefaultCellStyle.Font = new Font("微軟正黑體", fontKebRuGridPt, FontStyle.Bold); }
                if (dgvGbdAll != null)
                {
                    dgvGbdAll.Font = new Font("Consolas", fontGbdGridPt, FontStyle.Regular);
                    dgvGbdAll.ColumnHeadersDefaultCellStyle.Font = new Font("微軟正黑體", fontGbdGridPt, FontStyle.Bold);
                }
                if (txtFullLog != null)
                {
                    txtFullLog.Font = new Font("Consolas", fontLogTextPt, FontStyle.Regular);
                }
            }
            catch {}
        }

        // 分頁 5: GBD 網路多通道溫度記錄器 (Graphtec GL820)
        private ComboBox cmbMotorTempCh;
        public double[] gbdChTemps = new double[20]; // 預設 0.0，未連線不填入假溫度
        private DataGridView dgvGbdAll;
        private GbdTemperatureTrendControl gbdTrendChart;
        public CheckBox[] chkGbdChannels = new CheckBox[20];
        public bool hasAutoDetectedGl820Channels = false;
        private int gbdAutoDetectCountdown = 2; // 連線後連續採樣 2 次確認數據穩定

        /// <summary>
        /// 依據實測溫度數據動態識別哪些通道為真實有效接線通道 (非 0x7FFF / -32768 / 0.0)
        /// </summary>
        public bool[] DetectActiveGbdChannels()
        {
            bool[] detected = new bool[20];
            if (gbdChTemps == null) return detected;
            for (int i = 0; i < Math.Min(20, gbdChTemps.Length); i++)
            {
                double val = gbdChTemps[i];
                // 正常熱電偶/PT100 實測溫度介於 -40℃ 至 350℃，且非 0.0、非 -999、非 999
                if (val > -40.0 && val < 350.0 && Math.Abs(val) > 0.05 && val != 999.0 && val != -999.0)
                {
                    detected[i] = true;
                }
            }
            return detected;
        }

        /// <summary>
        /// 將偵測到的有效溫度通道全域同步套用至 GL820、S1工作制、空載溫升與曲線圖
        /// </summary>
        public void ApplyDetectedGbdChannels(bool[] detected, bool updateS1 = true, bool updateNoLoad = true)
        {
            if (detected == null || detected.Length < 20) return;

            int activeCount = 0;
            List<string> chNames = new List<string>();
            for (int i = 0; i < 20; i++)
            {
                if (detected[i])
                {
                    activeCount++;
                    chNames.Add(string.Format("CH{0}", i + 1));
                }
            }

            if (activeCount == 0) return; // 無有效數據時不覆蓋既有設定

            // 1. 全域 GL820 勾選遮罩
            if (gl820ChannelMask == null || gl820ChannelMask.Length < 20) gl820ChannelMask = new bool[20];
            Array.Copy(detected, gl820ChannelMask, 20);

            // 2. GL820 分頁勾選框與波形圖
            if (chkGbdChannels != null)
            {
                for (int i = 0; i < 20 && i < chkGbdChannels.Length; i++)
                {
                    if (chkGbdChannels[i] != null && !chkGbdChannels[i].IsDisposed)
                    {
                        chkGbdChannels[i].Checked = detected[i];
                    }
                }
            }
            if (gbdTrendChart != null && !gbdTrendChart.IsDisposed)
            {
                gbdTrendChart.SetChannelVisibility(gl820ChannelMask);
            }

            // 3. S1 工作制選定通道
            if (updateS1)
            {
                if (s1MonitoredChannels == null || s1MonitoredChannels.Length < 20) s1MonitoredChannels = new bool[20];
                Array.Copy(detected, s1MonitoredChannels, 20);
                if (lblS1SelectedChHint != null && !lblS1SelectedChHint.IsDisposed)
                {
                    lblS1SelectedChHint.Text = string.Format("(實測自選 {0} 通道: {1})", activeCount, string.Join(",", chNames.ToArray()));
                }
                if (dutyTempTrend != null && !dutyTempTrend.IsDisposed)
                {
                    dutyTempTrend.SetChannelVisibility(s1MonitoredChannels);
                }
            }

            // 4. 空載溫升選定通道
            if (updateNoLoad)
            {
                if (noLoadMonitoredChannels == null || noLoadMonitoredChannels.Length < 20) noLoadMonitoredChannels = new bool[20];
                Array.Copy(detected, noLoadMonitoredChannels, 20);
                if (lblNoLoadSelectedChHint != null && !lblNoLoadSelectedChHint.IsDisposed)
                {
                    lblNoLoadSelectedChHint.Text = string.Format("(實測自選 {0} 通道: {1})", activeCount, string.Join(",", chNames.ToArray()));
                }
            }

            WriteHmiLog("GBD", string.Format("【GL820 智慧通道識別】已自動辨識實測有效通道: {0} (共 {1} 通道)，已自動配置監控！",
                string.Join(", ", chNames.ToArray()), activeCount));
        }

        private List<KeyValuePair<DateTime, double[]>> gbdHistory = new List<KeyValuePair<DateTime, double[]>>();
        private int periodicGcSecCounter = 0; // 長時間運行定時 GC 回收計數器 (秒)
        private TabPage tabGbd;
        private TabPage tabNoLoad;

        // 右下角多功能工作台 (Multi-View Workbench)
        private Panel pnlWorkbench;
        private Panel pnlWorkbenchContent;
        private Button[] btnWorkbenchTabs;
        private Control[] pnlWorkbenchViews;
        private int activeWorkbenchViewIdx = 0;
        private TorqueSpeedTrendControl trqSpdChart;
        private ComboBox cmbTrqSpdTimeWindow;
        private Button btnPauseTrqSpdChart;
        private Button btnClearTrqSpdChart;

        // 分頁 6: 系統運轉日誌專屬檢視與匯出
        private static MainForm instance;
        private Label lblMiniLogText;
        private TextBox txtFullLog;
        private TextBox txtKebConfigLog;
        private TabPage tabLog;
        private static readonly List<string> memoryLogs = new List<string>();

        // 頂部整合控制列元件 (提升為成員供 VIEWER 模式動態抽換)
        public Panel pnlTop;
        public Label lblAppTitle;
        public Button btnClosedLoopModal;

        [STAThread]
        public static void Main(string[] args)
        {
            // ★【開啟程序防殘留鐵律】：第一時間排查工作管理員，強制清除舊進程，釋放被鎖定之 COM 埠與網路連線
            PurgeOrphanedInstances();

            // 自動尋找並註冊 DLL 子目錄 (將所有廠商驅動 DLL 集中於 DLL/ 資料夾，保持主目錄極簡純淨)
            try
            {
                string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                string dllDir = Path.Combine(baseDir, "DLL");
                if (!Directory.Exists(dllDir))
                {
                    try
                    {
                        var parentDir = Directory.GetParent(baseDir.TrimEnd('\\', '/'));
                        if (parentDir != null)
                        {
                            string pDll = Path.Combine(parentDir.FullName, "DLL");
                            if (Directory.Exists(pDll)) dllDir = pDll;
                        }
                    }
                    catch { }
                }
                if (Directory.Exists(dllDir))
                {
                    SetDllDirectory(dllDir);
                    string pathEnv = Environment.GetEnvironmentVariable("PATH") ?? "";
                    if (!pathEnv.Contains(dllDir))
                    {
                        Environment.SetEnvironmentVariable("PATH", dllDir + ";" + pathEnv);
                    }
                }

                // ★ 託管組件自動解析 (支援從 DLL/ 子目錄載入 BouncyCastle.Crypto 等受控組件)
                AppDomain.CurrentDomain.AssemblyResolve += (sender, resolveArgs) =>
                {
                    try
                    {
                        string reqDll = new System.Reflection.AssemblyName(resolveArgs.Name).Name + ".dll";
                        string p1 = Path.Combine(baseDir, reqDll);
                        if (File.Exists(p1)) return System.Reflection.Assembly.LoadFrom(p1);
                        string p2 = Path.Combine(dllDir, reqDll);
                        if (File.Exists(p2)) return System.Reflection.Assembly.LoadFrom(p2);
                    }
                    catch { }
                    return null;
                };
            }
            catch { }

            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            AppDomain.CurrentDomain.UnhandledException += (s, e) => {
                if (e.ExceptionObject != null)
                {
                    string exStr = e.ExceptionObject.ToString();
                    // 攔截 Windows XP / .NET 4.0 序列埠 SafeHandle closed 非致命例外，避免應用程式 Crash 閃退
                    if (exStr.Contains("ObjectDisposedException") || exStr.Contains("Safe handle has been closed"))
                    {
                        return;
                    }
                }
                string crashPath = WriteCrashReport(e.ExceptionObject, "背景/非同步執行緒 (AppDomain.UnhandledException)", e.IsTerminating);
                ShowCrashDialog(crashPath, e.ExceptionObject, "背景/非同步執行緒 (AppDomain.UnhandledException)", e.IsTerminating);
            };
            Application.ThreadException += (s, e) => {
                if (e.Exception is ObjectDisposedException) return;
                string crashPath = WriteCrashReport(e.Exception, "UI 主執行緒 (Application.ThreadException)", false);
                ShowCrashDialog(crashPath, e.Exception, "UI 主執行緒 (Application.ThreadException)", false);
            };
            System.Threading.Tasks.TaskScheduler.UnobservedTaskException += (s, e) => {
                string crashPath = WriteCrashReport(e.Exception, "非同步工作 (TaskScheduler.UnobservedTaskException)", false);
                e.SetObserved();
            };

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            if (args != null && args.Length > 0)
            {
                string a0 = args[0];
                if (a0.Equals("--tester", StringComparison.OrdinalIgnoreCase) || a0.Equals("-t", StringComparison.OrdinalIgnoreCase) || a0.Equals("/tester", StringComparison.OrdinalIgnoreCase))
                {
                    Application.Run(new DynamometerDeviceTester.TesterForm());
                    return;
                }
                if (a0.Equals("--viewer", StringComparison.OrdinalIgnoreCase) || a0.Equals("-v", StringComparison.OrdinalIgnoreCase) || a0.Equals("/viewer", StringComparison.OrdinalIgnoreCase) || a0.Equals("--client", StringComparison.OrdinalIgnoreCase))
                {
                    string target = (args.Length > 1) ? args[1] : null;
                    Application.Run(new MainForm(true, target));
                    return;
                }
            }

            Application.Run(new MainForm(false, null));
        }

        /// <summary>
        /// 排查並清除殘留於工作管理員的舊進程，徹底釋放被鎖定之 COM 埠 (COM1~8) 與網路通訊 Socket
        /// </summary>
        private static void PurgeOrphanedInstances()
        {
            try
            {
                Process current = Process.GetCurrentProcess();
                string currentName = current.ProcessName;
                int currentId = current.Id;

                // 搜尋同名進程與歷史測試工具進程
                string[] targetNames = new string[] { currentName, "Dynamometer_HMI_Pro", "Dynamometer_Device_Tester_GUI" };
                List<int> checkedPids = new List<int>();
                checkedPids.Add(currentId);

                foreach (string name in targetNames)
                {
                    try
                    {
                        Process[] procs = Process.GetProcessesByName(name);
                        foreach (Process p in procs)
                        {
                            if (!checkedPids.Contains(p.Id))
                            {
                                checkedPids.Add(p.Id);
                                try
                                {
                                    p.Kill();
                                    p.WaitForExit(1000);
                                }
                                catch { }
                            }
                        }
                    }
                    catch { }
                }
            }
            catch { }
        }

        public MainForm() : this(false, null) { }

        public MainForm(bool viewerMode = false, string remoteUrl = null)
        {
            // 全域啟用 TLS 1.2 (3072) 與 TLS 1.1 (768)，確保 HTTPS / Firebase 雲端通訊相容
            try
            {
                ServicePointManager.SecurityProtocol = (SecurityProtocolType)3072 | (SecurityProtocolType)768 | SecurityProtocolType.Tls;
                ServicePointManager.ServerCertificateValidationCallback = (sender, cert, chain, sslPolicyErrors) => true;
                ServicePointManager.DefaultConnectionLimit = 32;
                ServicePointManager.Expect100Continue = false;
            }
            catch { }

            isViewerMode = viewerMode;
            if (!string.IsNullOrEmpty(remoteUrl)) viewerTargetUrl = remoteUrl;

            this.Text = (isViewerMode ? "Dynamometer HMI Pro [👀 遠端檢視端 - VIEWER Mode (純唯讀)]" : "Dynamometer HMI Pro [🎛️ 現場主控端 - Master Controller]") + " v" + APP_VERSION;
            this.Size = new Size(1600, 960);
            this.MinimumSize = new Size(1280, 800);
            this.StartPosition = FormStartPosition.CenterScreen;
            this.Font = new Font("微軟正黑體", 10f, FontStyle.Regular);
            this.BackColor = Color.FromArgb(240, 243, 246);

            // 根佈局容器
            TableLayoutPanel rootTable = new TableLayoutPanel()
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 2,
                Margin = new Padding(0),
                Padding = new Padding(0)
            };
            rootTable.RowStyles.Add(new RowStyle(SizeType.Absolute, 52f)); // 頂部整合快捷連線與狀態列
            rootTable.RowStyles.Add(new RowStyle(SizeType.Percent, 100f)); // 主要分頁內容

            // 頂部整合控制列 (1080p 優化)
            pnlTop = new Panel()
            {
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(15, 23, 42),
                Padding = new Padding(10, 8, 10, 8)
            };

            lblAppTitle = new Label()
            {
                Text = "馬達動力計測試平台",
                ForeColor = Color.White,
                Font = new Font("微軟正黑體", 13f, FontStyle.Bold),
                AutoSize = true,
                Location = new Point(10, 12)
            };

            // 主捲平滑追隨與常態日誌記錄設定彈窗按鈕
            btnClosedLoopModal = new Button()
            {
                Text = "🎯 追蹤 / 日誌",
                Location = new Point(220, 8),
                Size = new Size(115, 34),
                BackColor = Color.FromArgb(79, 70, 229),
                ForeColor = Color.White,
                Font = new Font("微軟正黑體", 9.5f, FontStyle.Bold),
                Cursor = Cursors.Hand
            };
            btnClosedLoopModal.Click += (s, e) => {
                using (ClosedLoopControlDialog dlg = new ClosedLoopControlDialog(this))
                {
                    dlg.ShowDialog(this);
                }
            };

            // 統一輪詢速度與主畫面刷新調節器 (移至【🎯 追蹤 / 日誌】彈窗內統一調整)
            numUnifiedInterval = new NumericUpDown() { Minimum = 10, Maximum = 5000, Value = 100, Increment = 10, Font = new Font("Consolas", 10f, FontStyle.Bold) };
            numUnifiedInterval.ValueChanged += (s, e) => {
                pollingIntervalMs = (int)numUnifiedInterval.Value;
                WriteHmiLog("CONFIG", "已更新全設備統一輪詢週期為: " + pollingIntervalMs + " ms");
            };

            numUiRefreshInterval = new NumericUpDown() { Minimum = 50, Maximum = 5000, Value = 500, Increment = 50, Font = new Font("Consolas", 10f, FontStyle.Bold) };
            numUiRefreshInterval.ValueChanged += (s, e) => {
                if (mainTimer != null) mainTimer.Interval = (int)numUiRefreshInterval.Value;
                WriteHmiLog("CONFIG", "已更新主畫面更新頻率為: " + numUiRefreshInterval.Value + " ms");
            };

            numKebPollingInterval = new NumericUpDown() { Minimum = 100, Maximum = 60000, Value = 1000, Increment = 100, Font = new Font("Consolas", 10f, FontStyle.Bold) };
            numKebPollingInterval.ValueChanged += (s, e) => {
                kebPollingIntervalMs = (int)numKebPollingInterval.Value;
                WriteHmiLog("CONFIG", "已更新 KEB 右側參數輪詢週期為: " + kebPollingIntervalMs + " ms");
                SaveLayoutConfig();
            };

            // 7. 手動 RAW DATA 錄製與設定快捷按鈕 (GDI+ 向量繪製磁片 ICON，完美相容 XP/Win7/10/11)
            ToolTip ttTop = new ToolTip();
            btnRecordRawTop = new Button()
            {
                Text = "",
                Image = CreateFloppyIconImage(36, 28, Color.White, false),
                ImageAlign = ContentAlignment.MiddleCenter,
                Location = new Point(350, 8),
                Size = new Size(42, 34),
                BackColor = Color.FromArgb(220, 38, 38),
                Cursor = Cursors.Hand
            };
            ttTop.SetToolTip(btnRecordRawTop, "RAW DATA 錄製：錄製中再按即停止；未錄製時按下進入設定");
            btnRecordRawTop.Click += (s, e) =>
            {
                if (isManualRecording)
                {
                    // 錄製中 → 直接停止
                    StopManualRecording();
                }
                else
                {
                    // 未錄製 → 開啟設定彈窗
                    using (var dlg = new RawDataConfigDialog(this))
                        dlg.ShowDialog(this);
                }
            };

            // 8. 字體大小客製化設定按鈕 (標準 Tahoma 字體 Aa，原生支援 XP)
            btnFontCustomizer = new Button()
            {
                Text = "Aa",
                Location = new Point(400, 8),
                Size = new Size(42, 34),
                BackColor = Color.FromArgb(139, 92, 246),
                ForeColor = Color.White,
                Font = new Font("Tahoma", 12f, FontStyle.Bold),
                Cursor = Cursors.Hand
            };
            ttTop.SetToolTip(btnFontCustomizer, "自訂全站字體大小 (字體客製化)");
            btnFontCustomizer.Click += (s, e) => {
                using (FontCustomizerDialog dlg = new FontCustomizerDialog(this))
                {
                    dlg.ShowDialog(this);
                }
            };

            // 9. 設備通訊設定按鈕 (GDI+ 向量繪製連接器/插頭 ICON，完美相容 XP)
            btnDeviceSettings = new Button()
            {
                Text = "",
                Image = CreatePlugIconImage(36, 28, Color.White),
                ImageAlign = ContentAlignment.MiddleCenter,
                Location = new Point(450, 8),
                Size = new Size(42, 34),
                BackColor = Color.FromArgb(14, 165, 233),
                Cursor = Cursors.Hand
            };
            ttTop.SetToolTip(btnDeviceSettings, "設備通訊設定與測試工具箱 (COM / IP)");
            btnDeviceSettings.Click += (s, e) => {
                bool wasRunning = isRunning;
                try
                {
                    // 1. 徹底暫停主畫面所有定時器與背景通訊 Worker
                    isRunning = false;
                    if (mainTimer != null) mainTimer.Stop();
                    if (motorTempTimer != null) motorTempTimer.Stop();
                    StopBackgroundWorker();

                    // 2. 徹底釋放主畫面佔用的所有實體 COM 埠與 Socket 連線
                    DisconnectHardware();
                    Thread.Sleep(150); // 保證 Windows 核心完全釋放 COM 控制代碼

                    // 3. 安全開啟獨立工具箱視窗
                    using (DynamometerDeviceTester.TesterForm dlg = new DynamometerDeviceTester.TesterForm(this))
                    {
                        dlg.ShowDialog(this);
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show("開啟通訊測試工具箱失敗: " + ex.Message, "錯誤", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
                finally
                {
                    // 4. 工具箱已安全關閉並釋放埠，主系統重新套用最新通訊設定並恢復背景輪詢
                    ApplyDeviceSettings();
                    if (mainTimer != null && !mainTimer.Enabled) mainTimer.Start();
                    if (motorTempTimer != null && !motorTempTimer.Enabled) motorTempTimer.Start();
                    if (wasRunning)
                    {
                        isRunning = true;
                        StartBackgroundWorker();
                        ConnectAllDevices();
                    }
                }
            };

            // 10. 儀表校正微調設定按鈕 (支援 WT333E 三相電壓/電流/扭力計比例微調)
            btnCalibrationSettings = new Button()
            {
                Text = "⚙️",
                Location = new Point(500, 8),
                Size = new Size(42, 34),
                BackColor = Color.FromArgb(16, 185, 129),
                ForeColor = Color.White,
                Font = new Font("微軟正黑體", 11f, FontStyle.Bold),
                Cursor = Cursors.Hand
            };
            ttTop.SetToolTip(btnCalibrationSettings, "儀表量測校正微調 (WT333E 三相電壓/電流與扭力計比例係數)");
            btnCalibrationSettings.Click += (s, e) => {
                using (CalibrationSettingsDialog dlg = new CalibrationSettingsDialog(this))
                {
                    dlg.ShowDialog(this);
                }
            };

            // 11. 崩潰/異常診斷日誌按鈕 (一鍵開啟最新崩潰報告或系統日誌)
            Button btnCrashLogs = new Button()
            {
                Text = "🚨 診斷 LOG",
                Location = new Point(548, 8),
                Size = new Size(110, 34),
                BackColor = Color.FromArgb(71, 85, 105),
                ForeColor = Color.White,
                Font = new Font("微軟正黑體", 9.5f, FontStyle.Bold),
                Cursor = Cursors.Hand
            };
            ttTop.SetToolTip(btnCrashLogs, "查看崩潰與系統異常日誌 (Crash & Error Diagnostic Log)");
            btnCrashLogs.Click += (s, e) => {
                try
                {
                    string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                    string logDir = Path.Combine(baseDir, "logs");
                    string lastCrash = Path.Combine(logDir, "Crash_Last_Exception.log");
                    string sysError = Path.Combine(logDir, "system_error.log");

                    if (File.Exists(lastCrash))
                    {
                        System.Diagnostics.Process.Start("notepad.exe", lastCrash);
                    }
                    else if (File.Exists(sysError))
                    {
                        System.Diagnostics.Process.Start("notepad.exe", sysError);
                    }
                    else
                    {
                        if (!Directory.Exists(logDir)) Directory.CreateDirectory(logDir);
                        MessageBox.Show("目前系統無任何崩潰或嚴重異常記錄！\n\n日誌存放目錄：\n" + logDir, "診斷日誌", MessageBoxButtons.OK, MessageBoxIcon.Information);
                        System.Diagnostics.Process.Start("explorer.exe", logDir);
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show("開啟診斷日誌失敗: " + ex.Message, "錯誤", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            };

            Button btnTopEstop = new Button()
            {
                Text = "🚨 緊急停機 (E-STOP / ESC)",
                Size = new Size(215, 34),
                BackColor = Color.FromArgb(220, 38, 38),
                ForeColor = Color.White,
                Font = new Font("微軟正黑體", 11f, FontStyle.Bold),
                Cursor = Cursors.Hand,
                FlatStyle = FlatStyle.Flat
            };
            btnTopEstop.FlatAppearance.BorderSize = 0;
            btnTopEstop.Click += (s, e) => TriggerGlobalEmergencyStop();

            // 頂部右側控制群 (緊急停機 + 遠端監看開關 + 網址顯示，靠右放)
            FlowLayoutPanel flpTopRight = BuildTopRightPanel(btnTopEstop);

            pnlTop.Controls.AddRange(new Control[] {
                lblAppTitle, btnClosedLoopModal,
                btnRecordRawTop, btnFontCustomizer, btnDeviceSettings, btnCalibrationSettings, btnCrashLogs,
                flpTopRight
            });
            rootTable.Controls.Add(pnlTop, 0, 0);

            // 核心分頁控制器 (7 大功能分頁 - 1080p 放大)
            tabControl = new TabControl()
            {
                Dock = DockStyle.Fill,
                Font = new Font("微軟正黑體", 11f, FontStyle.Bold),
                ItemSize = new Size(155, 38),
                SizeMode = TabSizeMode.Fixed,
                Margin = new Padding(4)
            };

            instance = this;
            // 核心單一趨勢圖實例：所有測試分頁 (TN / Duty / 空載) 共用此實例，僅依分頁動態掛載與切換通道遮罩
            sharedTestTempTrend = new GbdTemperatureTrendControl(this) { Dock = DockStyle.Fill };
            sharedTestTempTrend.TimeSpanChanged += (idx) => {
                if (cmbTnTimeSpan != null && cmbTnTimeSpan.SelectedIndex != idx)
                {
                    try { cmbTnTimeSpan.SelectedIndex = idx; } catch { }
                }
                if (cmbDutyTimeSpan != null && cmbDutyTimeSpan.SelectedIndex != idx)
                {
                    try { cmbDutyTimeSpan.SelectedIndex = idx; } catch { }
                }
            };

            TabPage tab1 = new TabPage("即時綜合監控") { BackColor = Color.White };
            BuildManualTab(tab1);
            tabControl.TabPages.Add(tab1);

            TabPage tab2 = new TabPage("多段 T-N 測試") { BackColor = Color.White };
            BuildTnTab(tab2);
            tabControl.TabPages.Add(tab2);

            TabPage tab3 = new TabPage("工作制測試 (Duty)") { BackColor = Color.White };
            BuildDutyTab(tab3);
            tabControl.TabPages.Add(tab3);

            TabPage tab4 = new TabPage("效率地圖 (Map)") { BackColor = Color.White };
            BuildEffMapTab(tab4);
            tabControl.TabPages.Add(tab4);

            tabNoLoad = new TabPage("空載溫升 (No-Load)") { BackColor = Color.White };
            BuildNoLoadTab(tabNoLoad);
            tabControl.TabPages.Add(tabNoLoad);

            tabGbd = new TabPage("溫度記錄器 (GL820)") { BackColor = Color.White };
            BuildGbdTab(tabGbd);
            tabControl.TabPages.Add(tabGbd);

            tabReport = new TabPage("📤 報告管理上傳") { BackColor = Color.White };
            BuildReportTab(tabReport);
            tabControl.TabPages.Add(tabReport);

            rootTable.Controls.Add(tabControl, 0, 1);
            this.Controls.Add(rootTable);

            // 雙向全息即時同步：切換分頁時，刷新圖解面板與模式排版，並依需求將共用溫度趨勢圖動態停泊 (Dynamic Re-Parenting)
            tabControl.SelectedIndexChanged += (s, e) => {
                if (tabControl.SelectedIndex == 1) // 切換至 T-N 曲線測試分頁
                {
                    AttachSharedTempTrendTo(grpTnTemp, tnMonitoredChannels);
                }
                else if (tabControl.SelectedIndex == 2) // 切換至 工作制測試分頁
                {
                    AttachSharedTempTrendTo(grpDutyTemp, null);
                    if (cmbDutyMode != null) UpdateDutyModeVisibility(cmbDutyMode.SelectedIndex);
                    if (pnlS6Diagram != null) { pnlS6Diagram.Invalidate(); pnlS6Diagram.Refresh(); }
                }
                else if (tabControl.SelectedIndex == 4) // 切換至 空載溫升分頁 (tabNoLoad)
                {
                    AttachSharedTempTrendTo(grpNoLoadChart, noLoadMonitoredChannels);
                    UpdateNoLoadChannelHint();
                }
                else if (tabControl.SelectedIndex == 0) // 切換回即時遙測總覽
                {
                    if (cmbDutyMiniMode != null) UpdateDutyModeVisibility(cmbDutyMiniMode.SelectedIndex);
                    if (pnlS6MiniDiagram != null) { pnlS6MiniDiagram.Invalidate(); pnlS6MiniDiagram.Refresh(); }
                }
                else if (tabControl.SelectedTab == tabReport) // 切換至 報告管理與雲端上傳分頁
                {
                    RefreshReportFileList();
                }

                // 立即安全還原該分頁之視窗分割條佈局 (非同步排入訊息隊列確保容器尺寸已由 GDI+ 完成計算排版)
                int currentTab = tabControl.SelectedIndex;
                this.BeginInvoke(new Action(() => {
                    ApplyTabSplitters(currentTab);
                    SaveLayoutConfig();
                }));
            };

            // 視窗大小/位置拖曳調整結束時自動儲存視窗座標與分割條
            this.ResizeEnd += (s, e) => SaveLayoutConfig();

            // 啟動初始無縫鏡像雙保險
            SyncAllTnControls(fromMiniToMain: false);
            SyncAllDutyControls(fromMiniToMain: false);

            // 啟用鍵盤全域監聽：按下 ESC 鍵立即觸發最高優先級 E-STOP 緊急停機
            this.KeyPreview = true;
            this.KeyDown += (s, e) => {
                if (e.KeyCode == Keys.Escape)
                {
                    TriggerGlobalEmergencyStop();
                }
            };

            // 自動記憶佈局、啟動即自動連線採樣並啟動背景輪詢 Worker (主控端) 或 啟動遠端遙測接收 (VIEWER 檢視端)
            this.Shown += (s, e) => {
                LoadLayoutConfig();
                PurgeLocalLogs(false);
                if (loadedActiveTab >= 0 && tabControl != null && loadedActiveTab < tabControl.TabPages.Count)
                {
                    try { tabControl.SelectedIndex = loadedActiveTab; } catch { }
                }
                this.BeginInvoke(new Action(() => {
                    ApplyTabSplitters(tabControl != null ? tabControl.SelectedIndex : 0);
                }));
                if (mainTimer != null && !mainTimer.Enabled) mainTimer.Start();
                if (isViewerMode)
                {
                    InitializeViewerMode();
                }
                else
                {
                    isRunning = true;
                    StartBackgroundWorker();
                    ConnectAllDevices();
                    // 極速非同步背景啟動 Wi-Fi 網卡鎖定與 Firebase 雲端推播 (放棄無法連通之本機 WebServer)
                    ThreadPool.QueueUserWorkItem(_ => {
                        StartCloudUploader();
                    });
                }
            };
            this.FormClosing += (s, e) => {
                // 1. 視覺秒隱藏：0 毫秒極速視覺反饋，視窗立刻從螢幕與工作列消失
                try { this.Hide(); } catch { }

                // 2. 立即終止所有運轉標誌與前景計時器
                isRunning = false;
                isWorkerRunning = false;
                if (motorTempTimer != null) { try { motorTempTimer.Stop(); } catch { } }
                if (mainTimer != null) { try { mainTimer.Stop(); } catch { } }
                if (noLoadTimer != null) { try { noLoadTimer.Stop(); } catch { } }
                if (isViewerMode) { try { StopViewerClientSync(); } catch { } }

                // 3. 同步安全持久化介面與視圖設定檔 (耗時 < 2ms，確保配置絕對不丟失)
                try { SaveLayoutConfig(); } catch { }

                // 4. 【雙重保險核心：強制自毀超時看門狗 (Watchdog)】
                // 給予背景執行緒最多 1.2 秒優雅關閉硬體與網路。若超時 (例如底層驅動 DLL 阻塞或 COM 埠卡死)，
                // 看門狗執行緒直接調用 Process.Kill() 強制自毀，絕對不允許程式變成隱形殭屍殘留於工作管理員！
                Thread exitWatchdog = new Thread(() => {
                    Thread.Sleep(1200);
                    try
                    {
                        Process.GetCurrentProcess().Kill();
                    }
                    catch { }
                }) { IsBackground = true, Name = "ExitWatchdog" };
                exitWatchdog.Start();

                // 5. 消除 WinForms 漫長的控制項逐一 Dispose 迴圈，將阻塞的 SerialPort/TCP 關閉丟至背景非同步執行
                e.Cancel = true;
                ThreadPool.QueueUserWorkItem(_ => {
                    try { if (isManualRecording) StopManualRecording(showPrompt: false); } catch { }
                    try { StopWebServer(); } catch { }       // 優雅關閉 Web Server
                    try { StopBackgroundWorker(); } catch { }
                    try { DisconnectHardware(); } catch { }
                    try { closechannels(); } catch { }
                    try { Environment.Exit(0); } catch { }
                    try { Process.GetCurrentProcess().Kill(); } catch { }
                });
            };

            mainTimer = new System.Windows.Forms.Timer();
            mainTimer.Interval = 500; // 預設 500ms 主畫面更新頻率 (耗時 < 1ms，零負擔)
            mainTimer.Tick += MainTimer_Tick;

            motorTempTimer = new System.Windows.Forms.Timer();
            motorTempTimer.Interval = 1000; // 預設 1 秒 (1Sec) 馬達溫度採樣與趨勢繪圖
            motorTempTimer.Tick += (s, e) => {
                bool isGbdOnline = (tcpGbd != null && tcpGbd.Connected);
                if (motorTempChart != null && !motorTempChart.IsDisposed)
                    motorTempChart.IsConnected = isGbdOnline;
                if (gbdTrendChart != null && !gbdTrendChart.IsDisposed)
                    gbdTrendChart.IsConnected = isGbdOnline;
                if (sharedTestTempTrend != null && !sharedTestTempTrend.IsDisposed)
                    sharedTestTempTrend.IsConnected = isGbdOnline;

                if (!isGbdOnline)
                {
                    if ((DateTime.Now - lastGbdReconnectAttempt).TotalSeconds >= 3.0)
                    {
                        lastGbdReconnectAttempt = DateTime.Now;
                        TryReconnectGbdAsync();
                    }

                    if (lblMotorTempDisplay != null && !lblMotorTempDisplay.IsDisposed)
                    {
                        lblMotorTempDisplay.Text = "設備未連線";
                        lblMotorTempDisplay.ForeColor = Color.FromArgb(239, 68, 68);
                        if (lblMotorTempDisplay.Font != fontMsJhengHei10B) lblMotorTempDisplay.Font = fontMsJhengHei10B;
                    }
                    if (motorTempChart != null && !motorTempChart.IsDisposed)
                    {
                        motorTempChart.ClearData();
                        motorTempChart.Invalidate();
                    }
                    if (gbdTrendChart != null && !gbdTrendChart.IsDisposed)
                    {
                        gbdTrendChart.ClearData();
                        gbdTrendChart.Invalidate();
                    }
                    if (sharedTestTempTrend != null && !sharedTestTempTrend.IsDisposed)
                    {
                        sharedTestTempTrend.ClearData();
                        sharedTestTempTrend.Invalidate();
                    }
                    return;
                }

                int selCh = (cmbMotorTempCh != null && cmbMotorTempCh.SelectedIndex >= 0) ? cmbMotorTempCh.SelectedIndex : 0;
                double t = (selCh >= 0 && selCh < gbdChTemps.Length) ? gbdChTemps[selCh] : actTemp;
                if (lblMotorTempDisplay != null && !lblMotorTempDisplay.IsDisposed)
                {
                    lblMotorTempDisplay.Text = (t > 0.0) ? string.Format("{0:F1} °C", t) : "--.- °C";
                    lblMotorTempDisplay.ForeColor = Color.FromArgb(239, 68, 68);
                    if (lblMotorTempDisplay.Font != fontConsolas12B) lblMotorTempDisplay.Font = fontConsolas12B;
                }
                if (motorTempChart != null && !motorTempChart.IsDisposed && t > 0.0)
                {
                    motorTempChart.ChannelIndex = selCh;
                    string cName = (gl820ChannelNames != null && selCh >= 0 && selCh < gl820ChannelNames.Length && !string.IsNullOrEmpty(gl820ChannelNames[selCh]))
                        ? gl820ChannelNames[selCh] : ("CH" + (selCh + 1));
                    motorTempChart.ChannelName = cName;
                    motorTempChart.AddSample(DateTime.Now, t);
                }
                if (gbdTrendChart != null && !gbdTrendChart.IsDisposed)
                {
                    gbdTrendChart.AddSample(DateTime.Now, gbdChTemps);
                }
                // 核心單一實例：所有測試分頁 (TN / Duty / 空載) 共享此實例
                if (sharedTestTempTrend != null && !sharedTestTempTrend.IsDisposed && gbdChTemps != null)
                {
                    bool isCustomTickRunning = (dutyTimer != null && dutyTimer.Enabled) || isNoLoadRunning;
                    if (!isCustomTickRunning)
                    {
                        sharedTestTempTrend.AddSample(DateTime.Now, gbdChTemps);
                    }
                    if (lblTnTempRealtimeVal != null && !lblTnTempRealtimeVal.IsDisposed)
                    {
                        double maxT = double.MinValue;
                        int maxCh = -1;
                        for (int i = 0; i < 20 && i < gbdChTemps.Length; i++)
                        {
                            if (tnMonitoredChannels != null && i < tnMonitoredChannels.Length && tnMonitoredChannels[i])
                            {
                                if (gbdChTemps[i] > maxT) { maxT = gbdChTemps[i]; maxCh = i + 1; }
                            }
                        }
                        if (maxCh > 0 && maxT > -40.0 && maxT < 400.0)
                            lblTnTempRealtimeVal.Text = string.Format("實測最高: CH{0} {1:F1} ℃", maxCh, maxT);
                        else
                            lblTnTempRealtimeVal.Text = "實測最高: --.- ℃";
                    }
                }
                gbdHistory.Add(new KeyValuePair<DateTime, double[]>(DateTime.Now, (double[])gbdChTemps.Clone()));
                // 長時間運行記憶體防護：批次修剪取代逐筆 RemoveAt(0)
                if (gbdHistory.Count > 1200) gbdHistory.RemoveRange(0, 200);

                // 長時間運行記憶體與 GDI 物件維護：每 2 分鐘 (120 秒) 執行一次深度垃圾回收與 Finalizer 釋放，根治 Win32 GDI 握把耗盡
                periodicGcSecCounter++;
                if (periodicGcSecCounter >= 120)
                {
                    periodicGcSecCounter = 0;
                    try
                    {
                        GC.Collect();
                        GC.WaitForPendingFinalizers();
                    }
                    catch { }
                }
            };
            motorTempTimer.Start();
        }

        /// <summary>
        /// 輕量化動態停泊：將唯一的共用測試溫度趨勢圖 (sharedTestTempTrend) 動態掛載至目前切換之測試容器
        /// 徹底避免在各分頁建立多套自繪控制項，大幅減輕 WinXP GDI/USER 佇列負荷與記憶體配置
        /// </summary>
        public void AttachSharedTempTrendTo(Control targetContainer, bool[] channelMask)
        {
            if (sharedTestTempTrend == null || targetContainer == null || targetContainer.IsDisposed) return;
            try
            {
                if (sharedTestTempTrend.Parent != targetContainer)
                {
                    targetContainer.SuspendLayout();
                    sharedTestTempTrend.Parent = targetContainer;
                    sharedTestTempTrend.Dock = DockStyle.Fill;
                    sharedTestTempTrend.SendToBack();
                    targetContainer.ResumeLayout(true);
                }
                if (channelMask != null)
                {
                    sharedTestTempTrend.SetChannelVisibility(channelMask);
                }
                sharedTestTempTrend.PositionTimeSpanToolbar();
                if (cmbTnTimeSpan != null && cmbTnTimeSpan.SelectedIndex != sharedTestTempTrend.CurrentTimeSpanIndex)
                {
                    try { cmbTnTimeSpan.SelectedIndex = sharedTestTempTrend.CurrentTimeSpanIndex; } catch { }
                }
                if (cmbDutyTimeSpan != null && cmbDutyTimeSpan.SelectedIndex != sharedTestTempTrend.CurrentTimeSpanIndex)
                {
                    try { cmbDutyTimeSpan.SelectedIndex = sharedTestTempTrend.CurrentTimeSpanIndex; } catch { }
                }
                if (sharedTestTempTrend.Visible)
                {
                    sharedTestTempTrend.Invalidate();
                }
            }
            catch (Exception ex)
            {
                WriteHmiLog("UI_ATTACH_ERR", "AttachSharedTempTrendTo 失敗: " + ex.Message);
            }
        }

        private Image CreateFloppyIconImage(int w, int h, Color color, bool isRec)
        {
            Bitmap bmp = new Bitmap(w, h);
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.Clear(Color.Transparent);

                int ox = (w - 20) / 2;
                int oy = (h - 20) / 2;

                using (Brush bBody = new SolidBrush(color))
                {
                    Point[] pts = new Point[] {
                        new Point(ox, oy),
                        new Point(ox + 16, oy),
                        new Point(ox + 20, oy + 4),
                        new Point(ox + 20, oy + 20),
                        new Point(ox, oy + 20)
                    };
                    g.FillPolygon(bBody, pts);
                }

                // Shutter
                using (Brush bShutter = new SolidBrush(Color.FromArgb(200, 210, 220)))
                {
                    g.FillRectangle(bShutter, ox + 4, oy + 1, 10, 7);
                }
                using (Brush bCutout = new SolidBrush(Color.FromArgb(40, 50, 60)))
                {
                    g.FillRectangle(bCutout, ox + 6, oy + 2, 3, 5);
                }

                // Label area
                using (Brush bLabel = new SolidBrush(isRec ? Color.FromArgb(254, 226, 226) : Color.White))
                {
                    g.FillRectangle(bLabel, ox + 3, oy + 9, 14, 10);
                }

                if (isRec)
                {
                    using (Brush bRed = new SolidBrush(Color.Red))
                    {
                        g.FillEllipse(bRed, ox + 7, oy + 11, 6, 6);
                    }
                }
                else
                {
                    using (Pen pLine = new Pen(Color.FromArgb(148, 163, 184), 1f))
                    {
                        g.DrawLine(pLine, ox + 5, oy + 12, ox + 15, oy + 12);
                        g.DrawLine(pLine, ox + 5, oy + 15, ox + 15, oy + 15);
                    }
                }
            }
            return bmp;
        }

        private Image CreatePlugIconImage(int w, int h, Color color)
        {
            Bitmap bmp = new Bitmap(w, h);
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.Clear(Color.Transparent);

                int ox = (w - 20) / 2;
                int oy = (h - 20) / 2;

                // 2 Metallic Prongs
                using (Brush bProng = new SolidBrush(Color.FromArgb(254, 240, 138)))
                {
                    g.FillRectangle(bProng, ox + 3, oy + 1, 3, 6);
                    g.FillRectangle(bProng, ox + 14, oy + 1, 3, 6);
                }

                // Main Plug Body
                using (Brush bPlug = new SolidBrush(color))
                {
                    g.FillRectangle(bPlug, ox + 1, oy + 6, 18, 9);
                    g.FillRectangle(bPlug, ox + 4, oy + 14, 12, 4);
                    g.FillRectangle(bPlug, ox + 7, oy + 17, 6, 3);
                }

                // Divider line
                using (Pen pDetail = new Pen(Color.FromArgb(100, 116, 139), 1f))
                {
                    g.DrawLine(pDetail, ox + 1, oy + 10, ox + 19, oy + 10);
                }
            }
            return bmp;
        }

        private Image CreateLockIconImage(int w, int h, Color color, bool isLocked)
        {
            Bitmap bmp = new Bitmap(w, h);
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.Clear(Color.Transparent);

                int ox = (w - 14) / 2;
                int oy = (h - 14) / 2;

                // 1. Shackle (鎖扣環)
                using (Pen pShackle = new Pen(color, 2f))
                {
                    if (isLocked)
                    {
                        // 扣合閉鎖狀態 (Closed U-shape shackle)
                        g.DrawArc(pShackle, ox + 3, oy + 1, 8, 8, 180, 180);
                        g.DrawLine(pShackle, ox + 3, oy + 5, ox + 3, oy + 7);
                        g.DrawLine(pShackle, ox + 11, oy + 5, ox + 11, oy + 7);
                    }
                    else
                    {
                        // 張開解鎖狀態 (Open shackle, raised on the right)
                        g.DrawArc(pShackle, ox + 1, oy - 1, 8, 8, 180, 180);
                        g.DrawLine(pShackle, ox + 1, oy + 3, ox + 1, oy + 7);
                        g.DrawLine(pShackle, ox + 9, oy + 3, ox + 9, oy + 4);
                    }
                }

                // 2. Lock Body (鎖身)
                using (Brush bBody = new SolidBrush(color))
                {
                    g.FillRectangle(bBody, ox + 2, oy + 6, 11, 8);
                }

                // 3. Keyhole (鎖孔)
                using (Brush bHole = new SolidBrush(Color.White))
                {
                    g.FillEllipse(bHole, ox + 6, oy + 8, 3, 3);
                    g.FillRectangle(bHole, ox + 7, oy + 10, 1, 2);
                }
            }
            return bmp;
        }

        private Label CreateStatusPill(string text, Point loc)
        {
            return new Label()
            {
                Text = text,
                Location = loc,
                AutoSize = true,
                ForeColor = Color.FromArgb(203, 213, 225),
                Font = new Font("微軟正黑體", 9f, FontStyle.Bold),
                BackColor = Color.FromArgb(30, 41, 59),
                Padding = new Padding(4, 2, 4, 2)
            };
        }

        // GDI+ 向量相機 ICON（完美相容 XP/Win7/10/11，無 Emoji）
        private Image CreateCameraIconImage(int w, int h, Color color)
        {
            Bitmap bmp = new Bitmap(w, h);
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.Clear(Color.Transparent);
                int ox = (w - 20) / 2;
                int oy = (h - 16) / 2;
                using (Pen p = new Pen(color, 1.8f))
                using (Brush b = new SolidBrush(color))
                {
                    // 相機機身
                    g.DrawRectangle(p, ox + 0, oy + 4, 20, 12);
                    // 鏡頭突起（頂部中間小矩形）
                    g.FillRectangle(b, ox + 7, oy + 1, 6, 4);
                    // 鏡頭圓（主體中央）
                    g.DrawEllipse(p, ox + 5, oy + 6, 10, 8);
                    // 閃光燈點（左上角小圓）
                    g.FillEllipse(b, ox + 2, oy + 6, 2, 2);
                }
            }
            return bmp;
        }

        // 截圖主畫面並另存為 PNG
        private void CaptureScreenshot()
        {
            try
            {
                string snapDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "screenshots");
                if (!Directory.Exists(snapDir)) Directory.CreateDirectory(snapDir);
                string snapFile = Path.Combine(snapDir, string.Format("Screenshot_{0}.png", DateTime.Now.ToString("yyyyMMdd_HHmmss")));

                Rectangle bounds = this.Bounds;
                using (Bitmap bmp = new Bitmap(bounds.Width, bounds.Height))
                using (Graphics g = Graphics.FromImage(bmp))
                {
                    g.CopyFromScreen(bounds.Location, Point.Empty, bounds.Size);
                    bmp.Save(snapFile, System.Drawing.Imaging.ImageFormat.Png);
                }
                WriteHmiLog("SCREENSHOT", "[截圖成功] " + Path.GetFileName(snapFile));
                MessageBox.Show("主畫面截圖已儲存至：\n" + snapFile, "截圖成功", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show("截圖失敗: " + ex.Message, "錯誤", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private string GetLayoutConfigPath()
        {
            return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "dynamometer_layout.ini");
        }

        public void SaveLayoutConfig()
        {
            if (!isLayoutLoaded) return; // 避免初始化階段觸發欄寬/分割條事件覆蓋已存設定
            try
            {
                // 先行讀取現有 INI 檔中未由 SaveLayoutConfig() 直接接管之其他區段 (例如 [ReportManager], [GitHub], [GoogleDrive])
                var unmanagedSections = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
                string currentIniPath = GetLayoutConfigPath();
                if (File.Exists(currentIniPath))
                {
                    var managedSecSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase) {
                        "Window", "Splitters", "Workbench", "DgvKebRu1", "DgvKebRu2", "DgvTelemetry",
                        "DgvTnMultiPoints", "DgvTnPoints", "DgvDuty", "DgvNoLoad", "DgvReports",
                        "Polling", "Gl820Names", "KebMonitors1", "KebMonitors2", "NoLoadTest", "Logging"
                    };
                    string curSec = null;
                    foreach (var rawLine in File.ReadAllLines(currentIniPath, Encoding.UTF8))
                    {
                        string tr = rawLine.Trim();
                        if (tr.StartsWith("[") && tr.EndsWith("]"))
                        {
                            string sName = tr.Substring(1, tr.Length - 2).Trim();
                            if (!managedSecSet.Contains(sName))
                            {
                                curSec = sName;
                                if (!unmanagedSections.ContainsKey(curSec))
                                {
                                    unmanagedSections[curSec] = new List<string>();
                                }
                            }
                            else
                            {
                                curSec = null;
                            }
                            continue;
                        }
                        if (curSec != null && !string.IsNullOrEmpty(tr))
                        {
                            unmanagedSections[curSec].Add(rawLine);
                        }
                    }
                }

                var sb = new StringBuilder();
                sb.AppendLine("[Window]");
                var bounds = (this.WindowState == FormWindowState.Normal) ? this.Bounds : this.RestoreBounds;
                sb.AppendLine("X=" + bounds.X);
                sb.AppendLine("Y=" + bounds.Y);
                sb.AppendLine("Width=" + bounds.Width);
                sb.AppendLine("Height=" + bounds.Height);
                sb.AppendLine("State=" + (int)this.WindowState);
                sb.AppendLine("ActiveTab=" + (tabControl != null ? tabControl.SelectedIndex : 0));

                // 立即更新目前處於活動渲染狀態 (寬高 > 0) 的分割容器位置至記憶字典 (非活動分頁保留既有字典值，杜絕被 0 抹除)
                if (splitMainVertical != null && splitMainVertical.Height > 0) layoutSplitters["MainVertical"] = splitMainVertical.SplitterDistance;
                if (splitDrives != null && splitDrives.Width > 0) layoutSplitters["Drives"] = splitDrives.SplitterDistance;
                if (splitDrive1 != null && splitDrive1.Width > 0) layoutSplitters["Drive1"] = splitDrive1.SplitterDistance;
                if (splitDrive2 != null && splitDrive2.Width > 0) layoutSplitters["Drive2"] = splitDrive2.SplitterDistance;
                if (splitBottomHorizontal != null && splitBottomHorizontal.Width > 0) layoutSplitters["Bottom"] = splitBottomHorizontal.SplitterDistance;
                if (splitParam1 != null && splitParam1.Height > 0) layoutSplitters["Param1"] = splitParam1.SplitterDistance;
                if (splitParam2 != null && splitParam2.Height > 0) layoutSplitters["Param2"] = splitParam2.SplitterDistance;

                if (splitTnMain != null && splitTnMain.Height > 0) layoutSplitters["TnMain"] = splitTnMain.SplitterDistance;
                if (splitTnBottom != null && splitTnBottom.Width > 0) layoutSplitters["TnBottom"] = splitTnBottom.SplitterDistance;
                if (splitTnRight != null && splitTnRight.Height > 0) layoutSplitters["TnRight"] = splitTnRight.SplitterDistance;

                if (splitDuty != null && splitDuty.Height > 0) layoutSplitters["DutyMain"] = splitDuty.SplitterDistance;
                if (splitDutyTop != null && splitDutyTop.Width > 0) layoutSplitters["DutyTop"] = splitDutyTop.SplitterDistance;

                if (splitEff != null && splitEff.Width > 0) layoutSplitters["EffMain"] = splitEff.SplitterDistance;

                if (splitNoLoadMain != null && splitNoLoadMain.Height > 0) layoutSplitters["NoLoadMain"] = splitNoLoadMain.SplitterDistance;
                if (splitNoLoadBottom != null && splitNoLoadBottom.Width > 0) layoutSplitters["NoLoadBottom"] = splitNoLoadBottom.SplitterDistance;

                sb.AppendLine("[Splitters]");
                foreach (var kvp in layoutSplitters)
                {
                    sb.AppendLine(kvp.Key + "=" + kvp.Value);
                }

                sb.AppendLine("[Workbench]");
                sb.AppendLine("ActiveView=" + activeWorkbenchViewIdx);

                SaveDgvColWidths(sb, "DgvKebRu1", dgvKebRu1);
                SaveDgvColWidths(sb, "DgvKebRu2", dgvKebRu2);
                SaveDgvColWidths(sb, "DgvTelemetry", dgvTelemetry);
                SaveDgvColWidths(sb, "DgvTnMultiPoints", dgvTnMultiPoints);
                SaveDgvColWidths(sb, "DgvTnPoints", dgvTnPoints);
                SaveDgvColWidths(sb, "DgvDuty", dgvDuty);
                SaveDgvColWidths(sb, "DgvNoLoad", dgvNoLoad);
                SaveDgvColWidths(sb, "DgvReports", dgvReports);

                if (numUnifiedInterval != null)
                {
                    sb.AppendLine("[Polling]");
                    sb.AppendLine("Interval=" + (int)numUnifiedInterval.Value);
                    sb.AppendLine("KebInterval=" + kebPollingIntervalMs);
                }

                if (numUiRefreshInterval != null)
                {
                    sb.AppendLine("[UI]");
                    sb.AppendLine("RefreshInterval=" + (int)numUiRefreshInterval.Value);
                }

                sb.AppendLine("[Safety]");
                sb.AppendLine("DisconnectBuffer=" + (int)disconnectBufferSeconds);
                sb.AppendLine("ProtTorqueLoss=" + (enableProtTorqueLoss ? "1" : "0"));
                sb.AppendLine("ProtTorqueTimeout=" + protTorqueTimeoutSec.ToString(System.Globalization.CultureInfo.InvariantCulture));
                sb.AppendLine("ProtStall=" + (enableProtStall ? "1" : "0"));
                sb.AppendLine("ProtStallSpeed=" + protStallSpeedThreshold.ToString(System.Globalization.CultureInfo.InvariantCulture));
                sb.AppendLine("ProtStallDelay=" + protStallDelaySec.ToString(System.Globalization.CultureInfo.InvariantCulture));
                sb.AppendLine("ProtOvertemp=" + (enableProtOvertemp ? "1" : "0"));
                sb.AppendLine("ProtWarnTemp=" + protWarnTempThreshold.ToString(System.Globalization.CultureInfo.InvariantCulture));
                sb.AppendLine("ProtMaxTemp=" + protMaxTempThreshold.ToString(System.Globalization.CultureInfo.InvariantCulture));
                sb.AppendLine("ProtTempDelay=" + protTempDelaySec.ToString(System.Globalization.CultureInfo.InvariantCulture));
                sb.AppendLine("ProtCurrentImbalance=" + (enableProtCurrentImbalance ? "1" : "0"));
                sb.AppendLine("ProtImbalancePercent=" + protImbalancePercentThreshold.ToString(System.Globalization.CultureInfo.InvariantCulture));
                sb.AppendLine("ProtImbalanceDelay=" + protImbalanceDelaySec.ToString(System.Globalization.CultureInfo.InvariantCulture));
                sb.AppendLine("ProtKebFault=" + (enableProtKebFault ? "1" : "0"));
                sb.AppendLine("AutoStopBrakeThresholdRpm=" + autoStopBrakeThresholdRpm.ToString(System.Globalization.CultureInfo.InvariantCulture));

                sb.AppendLine("[Tracking]");
                sb.AppendLine("MaxDelta=" + trackingMaxDelta.ToString("F1", System.Globalization.CultureInfo.InvariantCulture));
                sb.AppendLine("Deadband=" + trackingDeadband.ToString("F2", System.Globalization.CultureInfo.InvariantCulture));
                sb.AppendLine("SpeedDeadband=" + trackingSpeedDeadband.ToString("F1", System.Globalization.CultureInfo.InvariantCulture));
                sb.AppendLine("SpeedMaxDelta=" + trackingSpeedMaxDelta.ToString("F1", System.Globalization.CultureInfo.InvariantCulture));
                sb.AppendLine("FilterWindow=" + trackingFilterWindowMs);
                sb.AppendLine("ControlInterval=" + trackingControlIntervalMs);
                sb.AppendLine("SafetyThresh=" + trackingSafetyThresh.ToString("F0", System.Globalization.CultureInfo.InvariantCulture));

                sb.AppendLine("[Devices]");
                sb.AppendLine("TorquePort=" + torquePortName);
                sb.AppendLine("TorqueBaud=" + torqueBaudRate);
                sb.AppendLine("PowerMeterIp=" + powerMeterIp);
                sb.AppendLine("PowerMeterPort=" + powerMeterPort);
                sb.AppendLine("GbdIp=" + gbdIp);
                sb.AppendLine("GbdPort=" + gbdPort);
                sb.AppendLine("KebPort1=" + ((cmbHmiKebPort1 != null && cmbHmiKebPort1.SelectedItem != null) ? cmbHmiKebPort1.SelectedItem.ToString() : kebPort1));
                sb.AppendLine("KebBaud1=" + ((cmbHmiKebBaud1 != null && cmbHmiKebBaud1.SelectedItem != null) ? cmbHmiKebBaud1.SelectedItem.ToString() : kebBaud1.ToString()));
                sb.AppendLine("KebNode1=" + (numHmiKebNode1 != null ? (int)numHmiKebNode1.Value : kebNode1));
                sb.AppendLine("KebPort2=" + ((cmbHmiKebPort2 != null && cmbHmiKebPort2.SelectedItem != null) ? cmbHmiKebPort2.SelectedItem.ToString() : kebPort2));
                sb.AppendLine("KebBaud2=" + ((cmbHmiKebBaud2 != null && cmbHmiKebBaud2.SelectedItem != null) ? cmbHmiKebBaud2.SelectedItem.ToString() : kebBaud2.ToString()));
                sb.AppendLine("KebNode2=" + (numHmiKebNode2 != null ? (int)numHmiKebNode2.Value : kebNode2));

                sb.AppendLine("[Fonts]");
                sb.AppendLine("MetricValPt=" + fontMetricValPt.ToString(System.Globalization.CultureInfo.InvariantCulture));
                sb.AppendLine("MetricTitlePt=" + fontMetricTitlePt.ToString(System.Globalization.CultureInfo.InvariantCulture));
                sb.AppendLine("TelemetryGridPt=" + fontTelemetryGridPt.ToString(System.Globalization.CultureInfo.InvariantCulture));
                sb.AppendLine("KebRuGridPt=" + fontKebRuGridPt.ToString(System.Globalization.CultureInfo.InvariantCulture));
                sb.AppendLine("KebNumericPt=" + fontKebNumericPt.ToString(System.Globalization.CultureInfo.InvariantCulture));
                sb.AppendLine("[RawData]");
                sb.AppendLine("MotorName=" + (motorModelName ?? "SVM100S"));
                sb.AppendLine("SaveDirectory=" + (rawDataSaveDirectory ?? ""));
                sb.AppendLine("Gl820Mask=" + string.Join(",", gl820ChannelMask.Select(b => b ? "1" : "0")));
                sb.AppendLine("RecordKebRu=" + (recordKebRuParams ? 1 : 0));
                sb.AppendLine("RecordIntervalMs=" + rawDataIntervalMs);
                for (int i = 0; i < 20; i++)
                {
                    sb.AppendLine(string.Format("Gl820Name_CH{0}={1}", i + 1, (gl820ChannelNames != null && i < gl820ChannelNames.Length ? gl820ChannelNames[i] : ("CH" + (i + 1))).Replace("=", "_")));
                }

                // 儲存 A/B 載台自訂監控參數清單 (記憶功能)
                if (kebMonitorList1 != null)
                {
                    sb.AppendLine("[KebMonitors1]");
                    sb.AppendLine("Count=" + kebMonitorList1.Count);
                    for (int i = 0; i < kebMonitorList1.Count; i++)
                    {
                        var it = kebMonitorList1[i];
                        sb.AppendLine(string.Format("Item{0}={1}|{2:X4}|{3}|{4}|{5}|{6}|{7}",
                            i, it.Name, it.Address, it.Scale.ToString(System.Globalization.CultureInfo.InvariantCulture), it.Unit,
                            it.IsHex ? "1" : "0", it.IsStatus ? "1" : "0", it.IsNode ? "1" : "0"));
                    }
                }
                if (kebMonitorList2 != null)
                {
                    sb.AppendLine("[KebMonitors2]");
                    sb.AppendLine("Count=" + kebMonitorList2.Count);
                    for (int i = 0; i < kebMonitorList2.Count; i++)
                    {
                        var it = kebMonitorList2[i];
                        sb.AppendLine(string.Format("Item{0}={1}|{2:X4}|{3}|{4}|{5}|{6}|{7}",
                            i, it.Name, it.Address, it.Scale.ToString(System.Globalization.CultureInfo.InvariantCulture), it.Unit,
                            it.IsHex ? "1" : "0", it.IsStatus ? "1" : "0", it.IsNode ? "1" : "0"));
                    }
                }

                // 空載測試參數記憶
                sb.AppendLine("[NoLoadTest]");
                sb.AppendLine("DriveRole=" + (cmbNoLoadRole != null ? cmbNoLoadRole.SelectedIndex : 1));
                sb.AppendLine("DoRated=" + (chkNoLoadRatedTest != null && chkNoLoadRatedTest.Checked ? "1" : "0"));
                sb.AppendLine("RatedSpd=" + (numNoLoadRatedSpd != null ? (int)numNoLoadRatedSpd.Value : 1500));
                sb.AppendLine("DoMaxSpd=" + (chkNoLoadMaxSpdTest != null && chkNoLoadMaxSpdTest.Checked ? "1" : "0"));
                sb.AppendLine("MaxSpd=" + (numNoLoadMaxSpd != null ? (int)numNoLoadMaxSpd.Value : 3600));
                sb.AppendLine("StepSpd=" + (numNoLoadStepSpd != null ? (int)numNoLoadStepSpd.Value : 500));
                sb.AppendLine("DwellSec=" + (numNoLoadStepDwellSec != null ? (int)numNoLoadStepDwellSec.Value : 60));
                sb.AppendLine("TempLimit=" + (numNoLoadTempLimit != null ? numNoLoadTempLimit.Value.ToString(System.Globalization.CultureInfo.InvariantCulture) : "75.0"));
                sb.AppendLine("TimeTs=" + (numNoLoadTimeTs != null ? (int)numNoLoadTimeTs.Value : 30));
                sb.AppendLine("TimeTn=" + (numNoLoadTimeTn != null ? (int)numNoLoadTimeTn.Value : 60));
                if (noLoadMonitoredChannels != null)
                {
                    sb.AppendLine("Channels=" + string.Join(",", noLoadMonitoredChannels.Select(b => b ? "1" : "0")));
                }

                sb.AppendLine("[Logging]");
                sb.AppendLine("AutoRawCsv=" + (enableAutoRawCsv ? "1" : "0"));
                sb.AppendLine("SystemEventLog=" + (enableSystemEventLog ? "1" : "0"));
                sb.AppendLine("CloudLogMaxCount=" + cloudLogMaxHistoryCount);
                sb.AppendLine("CloudLogMaxDays=" + cloudLogMaxDays);
                sb.AppendLine("LocalLogMaxCount=" + localLogMaxHistoryCount);

                // 完整寫回所有未接管之外部模組區段 (包含 [ReportManager], [GitHub], [GoogleDrive] 等)
                foreach (var kvp in unmanagedSections)
                {
                    sb.AppendLine("[" + kvp.Key + "]");
                    foreach (var l in kvp.Value)
                    {
                        sb.AppendLine(l);
                    }
                }

                File.WriteAllText(GetLayoutConfigPath(), sb.ToString(), Encoding.UTF8);
            }
            catch {}
        }

        private void LoadLayoutConfig()
        {
            try
            {
                string path = GetLayoutConfigPath();
                if (!File.Exists(path)) return;

                var lines = File.ReadAllLines(path, Encoding.UTF8);
                string section = "";
                var map = new Dictionary<string, string>();

                foreach (var rawLine in lines)
                {
                    string line = rawLine.Trim();
                    if (string.IsNullOrEmpty(line) || line.StartsWith("#") || line.StartsWith(";")) continue;
                    if (line.StartsWith("[") && line.EndsWith("]"))
                    {
                        section = line.Substring(1, line.Length - 2).Trim();
                        continue;
                    }
                    int eq = line.IndexOf('=');
                    if (eq > 0)
                    {
                        string key = section + "." + line.Substring(0, eq).Trim();
                        string val = line.Substring(eq + 1).Trim();
                        map[key] = val;
                    }
                }

                // Logging 开关读取
                if (map.ContainsKey("Logging.AutoRawCsv")) enableAutoRawCsv = (map["Logging.AutoRawCsv"] == "1");
                if (map.ContainsKey("Logging.SystemEventLog")) enableSystemEventLog = (map["Logging.SystemEventLog"] == "1");

                // Window
                if (map.ContainsKey("Window.Width") && map.ContainsKey("Window.Height"))
                {
                    int w = int.Parse(map["Window.Width"]);
                    int h = int.Parse(map["Window.Height"]);
                    int x = map.ContainsKey("Window.X") ? int.Parse(map["Window.X"]) : this.Left;
                    int y = map.ContainsKey("Window.Y") ? int.Parse(map["Window.Y"]) : this.Top;

                    if (w >= 600 && h >= 400)
                    {
                        Rectangle targetRect = new Rectangle(x, y, w, h);
                        bool isVisibleOnAnyScreen = false;
                        foreach (var scr in Screen.AllScreens)
                        {
                            if (scr.WorkingArea.IntersectsWith(targetRect))
                            {
                                isVisibleOnAnyScreen = true;
                                break;
                            }
                        }

                        if (isVisibleOnAnyScreen)
                        {
                            this.StartPosition = FormStartPosition.Manual;
                            this.Bounds = targetRect;
                        }
                        else
                        {
                            this.StartPosition = FormStartPosition.CenterScreen;
                            this.Size = new Size(Math.Min(w, Screen.PrimaryScreen.WorkingArea.Width), Math.Min(h, Screen.PrimaryScreen.WorkingArea.Height));
                        }
                    }
                }
                if (map.ContainsKey("Window.State"))
                {
                    int st = int.Parse(map["Window.State"]);
                    if (st == (int)FormWindowState.Maximized) this.WindowState = FormWindowState.Maximized;
                }
                if (map.ContainsKey("Window.ActiveTab"))
                {
                    int at;
                    if (int.TryParse(map["Window.ActiveTab"], out at) && at >= 0)
                    {
                        loadedActiveTab = at;
                    }
                }

                // Polling Interval
                if (map.ContainsKey("Polling.Interval") && numUnifiedInterval != null)
                {
                    int iv = int.Parse(map["Polling.Interval"]);
                    if (iv >= 10 && iv <= 5000)
                    {
                        numUnifiedInterval.Value = iv;
                        pollingIntervalMs = iv;
                    }
                }
                if (map.ContainsKey("Polling.KebInterval"))
                {
                    int kiv;
                    if (int.TryParse(map["Polling.KebInterval"], out kiv) && kiv >= 100 && kiv <= 60000)
                    {
                        kebPollingIntervalMs = kiv;
                        if (numKebPollingInterval != null) numKebPollingInterval.Value = kiv;
                    }
                }

                // UI Refresh Interval
                if (map.ContainsKey("UI.RefreshInterval") && numUiRefreshInterval != null)
                {
                    int iv = int.Parse(map["UI.RefreshInterval"]);
                    if (iv >= 50 && iv <= 5000)
                    {
                        numUiRefreshInterval.Value = iv;
                        if (mainTimer != null) mainTimer.Interval = iv;
                    }
                }

                // Safety
                if (map.ContainsKey("Safety.DisconnectBuffer"))
                {
                    int db;
                    if (int.TryParse(map["Safety.DisconnectBuffer"], out db) && db >= 1 && db <= 60)
                    {
                        disconnectBufferSeconds = db;
                        if (numDisconnectBuffer != null) numDisconnectBuffer.Value = db;
                    }
                }
                if (map.ContainsKey("Safety.ProtTorqueLoss")) enableProtTorqueLoss = map["Safety.ProtTorqueLoss"] == "1";
                if (map.ContainsKey("Safety.ProtTorqueTimeout")) { decimal v; if (decimal.TryParse(map["Safety.ProtTorqueTimeout"], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out v)) protTorqueTimeoutSec = v; }
                if (map.ContainsKey("Safety.ProtStall")) enableProtStall = map["Safety.ProtStall"] == "1";
                if (map.ContainsKey("Safety.ProtStallSpeed")) { decimal v; if (decimal.TryParse(map["Safety.ProtStallSpeed"], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out v)) protStallSpeedThreshold = v; }
                if (map.ContainsKey("Safety.ProtStallDelay")) { decimal v; if (decimal.TryParse(map["Safety.ProtStallDelay"], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out v)) protStallDelaySec = v; }
                if (map.ContainsKey("Safety.ProtOvertemp")) enableProtOvertemp = map["Safety.ProtOvertemp"] == "1";
                if (map.ContainsKey("Safety.ProtWarnTemp")) { decimal v; if (decimal.TryParse(map["Safety.ProtWarnTemp"], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out v)) protWarnTempThreshold = v; }
                if (map.ContainsKey("Safety.ProtMaxTemp")) { decimal v; if (decimal.TryParse(map["Safety.ProtMaxTemp"], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out v)) protMaxTempThreshold = v; }
                if (map.ContainsKey("Safety.ProtTempDelay")) { decimal v; if (decimal.TryParse(map["Safety.ProtTempDelay"], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out v)) protTempDelaySec = v; }
                if (map.ContainsKey("Safety.ProtCurrentImbalance")) enableProtCurrentImbalance = map["Safety.ProtCurrentImbalance"] == "1";
                if (map.ContainsKey("Safety.ProtImbalancePercent")) { decimal v; if (decimal.TryParse(map["Safety.ProtImbalancePercent"], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out v)) protImbalancePercentThreshold = v; }
                if (map.ContainsKey("Safety.ProtImbalanceDelay")) { decimal v; if (decimal.TryParse(map["Safety.ProtImbalanceDelay"], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out v)) protImbalanceDelaySec = v; }
                if (map.ContainsKey("Safety.ProtKebFault")) enableProtKebFault = map["Safety.ProtKebFault"] == "1";
                if (map.ContainsKey("Safety.AutoStopBrakeThresholdRpm")) { decimal v; if (decimal.TryParse(map["Safety.AutoStopBrakeThresholdRpm"], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out v)) autoStopBrakeThresholdRpm = v; }

                // Closed Loop Tracking Config
                if (map.ContainsKey("Tracking.MaxDelta")) { decimal v; if (decimal.TryParse(map["Tracking.MaxDelta"], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out v)) trackingMaxDelta = v; }
                if (map.ContainsKey("Tracking.Deadband")) { decimal v; if (decimal.TryParse(map["Tracking.Deadband"], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out v)) trackingDeadband = v; }
                if (map.ContainsKey("Tracking.SpeedDeadband")) { decimal v; if (decimal.TryParse(map["Tracking.SpeedDeadband"], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out v)) trackingSpeedDeadband = v; }
                if (map.ContainsKey("Tracking.SpeedMaxDelta")) { decimal v; if (decimal.TryParse(map["Tracking.SpeedMaxDelta"], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out v)) trackingSpeedMaxDelta = v; }
                if (map.ContainsKey("Tracking.FilterWindow")) int.TryParse(map["Tracking.FilterWindow"], out trackingFilterWindowMs);
                if (map.ContainsKey("Tracking.ControlInterval")) int.TryParse(map["Tracking.ControlInterval"], out trackingControlIntervalMs);
                if (map.ContainsKey("Tracking.SafetyThresh")) { decimal v; if (decimal.TryParse(map["Tracking.SafetyThresh"], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out v)) trackingSafetyThresh = v; }

                // Fonts
                if (map.ContainsKey("Fonts.MetricValPt")) float.TryParse(map["Fonts.MetricValPt"], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out fontMetricValPt);
                if (map.ContainsKey("Fonts.MetricTitlePt")) float.TryParse(map["Fonts.MetricTitlePt"], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out fontMetricTitlePt);
                if (map.ContainsKey("Fonts.TelemetryGridPt")) float.TryParse(map["Fonts.TelemetryGridPt"], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out fontTelemetryGridPt);
                if (map.ContainsKey("Fonts.KebRuGridPt")) float.TryParse(map["Fonts.KebRuGridPt"], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out fontKebRuGridPt);
                if (map.ContainsKey("Fonts.KebNumericPt")) float.TryParse(map["Fonts.KebNumericPt"], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out fontKebNumericPt);
                if (map.ContainsKey("Fonts.GbdGridPt")) float.TryParse(map["Fonts.GbdGridPt"], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out fontGbdGridPt);
                if (map.ContainsKey("Fonts.LogTextPt")) float.TryParse(map["Fonts.LogTextPt"], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out fontLogTextPt);

                ApplyFontSizes();

                // Devices
                if (map.ContainsKey("Devices.TorquePort")) torquePortName = map["Devices.TorquePort"];
                if (map.ContainsKey("Devices.TorqueBaud")) int.TryParse(map["Devices.TorqueBaud"], out torqueBaudRate);
                if (map.ContainsKey("Devices.PowerMeterIp")) powerMeterIp = map["Devices.PowerMeterIp"];
                if (map.ContainsKey("Devices.PowerMeterPort")) int.TryParse(map["Devices.PowerMeterPort"], out powerMeterPort);
                if (map.ContainsKey("Devices.GbdIp")) gbdIp = map["Devices.GbdIp"];
                if (map.ContainsKey("Devices.GbdPort")) int.TryParse(map["Devices.GbdPort"], out gbdPort);
                if (map.ContainsKey("Devices.KebPort1")) kebPort1 = map["Devices.KebPort1"];
                if (map.ContainsKey("Devices.KebBaud1")) int.TryParse(map["Devices.KebBaud1"], out kebBaud1);
                if (map.ContainsKey("Devices.KebNode1")) int.TryParse(map["Devices.KebNode1"], out kebNode1);
                if (map.ContainsKey("Devices.KebPort2")) kebPort2 = map["Devices.KebPort2"];
                if (map.ContainsKey("Devices.KebBaud2")) int.TryParse(map["Devices.KebBaud2"], out kebBaud2);
                // RawData Config
                if (map.ContainsKey("RawData.MotorName") && !string.IsNullOrEmpty(map["RawData.MotorName"])) motorModelName = map["RawData.MotorName"];
                if (map.ContainsKey("RawData.SaveDirectory")) rawDataSaveDirectory = map["RawData.SaveDirectory"];
                if (map.ContainsKey("RawData.Gl820Mask"))
                {
                    var parts = map["RawData.Gl820Mask"].Split(',');
                    for (int i = 0; i < Math.Min(20, parts.Length); i++)
                    {
                        gl820ChannelMask[i] = parts[i].Trim() == "1";
                    }
                }
                for (int i = 0; i < 20; i++)
                {
                    string key = "RawData.Gl820Name_CH" + (i + 1);
                    if (map.ContainsKey(key) && !string.IsNullOrEmpty(map[key]))
                    {
                        string cleanLoaded = System.Text.RegularExpressions.Regex.Replace(map[key], @"[^a-zA-Z0-9_\-\.\s]", "").Trim();
                        gl820ChannelNames[i] = !string.IsNullOrEmpty(cleanLoaded) ? cleanLoaded : ("CH" + (i + 1));
                    }
                }
                if (dgvGbdAll != null && dgvGbdAll.Rows.Count >= 1 && dgvGbdAll.Columns.Count >= 20)
                {
                    for (int i = 0; i < 20; i++)
                    {
                        dgvGbdAll.Rows[0].Cells[i].Value = gl820ChannelNames[i];
                    }
                }
                if (map.ContainsKey("RawData.RecordKebRu")) recordKebRuParams = map["RawData.RecordKebRu"] == "1";
                if (map.ContainsKey("RawData.RecordIntervalMs")) int.TryParse(map["RawData.RecordIntervalMs"], out rawDataIntervalMs);

                // Splitters: 完整讀入字典，杜絕跨分頁未渲染容器被抹除
                foreach (var kvp in map)
                {
                    if (kvp.Key.StartsWith("Splitters.", StringComparison.OrdinalIgnoreCase))
                    {
                        string subKey = kvp.Key.Substring("Splitters.".Length);
                        int val;
                        if (int.TryParse(kvp.Value, out val) && val > 0)
                        {
                            layoutSplitters[subKey] = val;
                        }
                    }
                }
                ApplyTabSplitters(tabControl != null ? tabControl.SelectedIndex : 0);

                // 空載測試參數記憶還原 (NoLoadTest)
                if (map.ContainsKey("NoLoadTest.DriveRole") && cmbNoLoadRole != null)
                {
                    int r;
                    if (int.TryParse(map["NoLoadTest.DriveRole"], out r) && r >= 0 && r < cmbNoLoadRole.Items.Count)
                        cmbNoLoadRole.SelectedIndex = r;
                }
                if (map.ContainsKey("NoLoadTest.DoRated") && chkNoLoadRatedTest != null)
                {
                    chkNoLoadRatedTest.Checked = (map["NoLoadTest.DoRated"] == "1");
                    if (numNoLoadRatedSpd != null) numNoLoadRatedSpd.Enabled = chkNoLoadRatedTest.Checked;
                }
                if (map.ContainsKey("NoLoadTest.RatedSpd") && numNoLoadRatedSpd != null)
                {
                    decimal v;
                    if (decimal.TryParse(map["NoLoadTest.RatedSpd"], out v) && v >= numNoLoadRatedSpd.Minimum && v <= numNoLoadRatedSpd.Maximum)
                        numNoLoadRatedSpd.Value = v;
                }
                if (map.ContainsKey("NoLoadTest.DoMaxSpd") && chkNoLoadMaxSpdTest != null)
                {
                    chkNoLoadMaxSpdTest.Checked = (map["NoLoadTest.DoMaxSpd"] == "1");
                    if (numNoLoadMaxSpd != null) numNoLoadMaxSpd.Enabled = chkNoLoadMaxSpdTest.Checked;
                }
                if (map.ContainsKey("NoLoadTest.MaxSpd") && numNoLoadMaxSpd != null)
                {
                    decimal v;
                    if (decimal.TryParse(map["NoLoadTest.MaxSpd"], out v) && v >= numNoLoadMaxSpd.Minimum && v <= numNoLoadMaxSpd.Maximum)
                        numNoLoadMaxSpd.Value = v;
                }
                if (map.ContainsKey("NoLoadTest.StepSpd") && numNoLoadStepSpd != null)
                {
                    decimal v;
                    if (decimal.TryParse(map["NoLoadTest.StepSpd"], out v) && v >= numNoLoadStepSpd.Minimum && v <= numNoLoadStepSpd.Maximum)
                        numNoLoadStepSpd.Value = v;
                }
                if (map.ContainsKey("NoLoadTest.DwellSec") && numNoLoadStepDwellSec != null)
                {
                    decimal v;
                    if (decimal.TryParse(map["NoLoadTest.DwellSec"], out v) && v >= numNoLoadStepDwellSec.Minimum && v <= numNoLoadStepDwellSec.Maximum)
                        numNoLoadStepDwellSec.Value = v;
                }
                if (map.ContainsKey("NoLoadTest.TempLimit") && numNoLoadTempLimit != null)
                {
                    decimal v;
                    if (decimal.TryParse(map["NoLoadTest.TempLimit"], System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out v) && v >= numNoLoadTempLimit.Minimum && v <= numNoLoadTempLimit.Maximum)
                        numNoLoadTempLimit.Value = v;
                }
                if (map.ContainsKey("NoLoadTest.TimeTs") && numNoLoadTimeTs != null)
                {
                    decimal v;
                    if (decimal.TryParse(map["NoLoadTest.TimeTs"], out v) && v >= numNoLoadTimeTs.Minimum && v <= numNoLoadTimeTs.Maximum)
                        numNoLoadTimeTs.Value = v;
                }
                if (map.ContainsKey("NoLoadTest.TimeTn") && numNoLoadTimeTn != null)
                {
                    decimal v;
                    if (decimal.TryParse(map["NoLoadTest.TimeTn"], out v) && v >= numNoLoadTimeTn.Minimum && v <= numNoLoadTimeTn.Maximum)
                        numNoLoadTimeTn.Value = v;
                }
                if (map.ContainsKey("NoLoadTest.Channels") && noLoadMonitoredChannels != null)
                {
                    string[] parts = map["NoLoadTest.Channels"].Split(',');
                    for (int i = 0; i < Math.Min(parts.Length, noLoadMonitoredChannels.Length); i++)
                    {
                        noLoadMonitoredChannels[i] = (parts[i].Trim() == "1");
                    }
                    UpdateNoLoadChannelHint();
                }

                // Workbench 視圖記憶恢復
                if (map.ContainsKey("Workbench.ActiveView"))
                {
                    int v = int.Parse(map["Workbench.ActiveView"]);
                    SwitchWorkbenchView(v);
                }

                // DataGridView 欄寬記憶還原 (支援全分頁表格)
                LoadDgvColWidths(map, "DgvKebRu1", dgvKebRu1);
                LoadDgvColWidths(map, "DgvKebRu2", dgvKebRu2);
                LoadDgvColWidths(map, "DgvTelemetry", dgvTelemetry);
                LoadDgvColWidths(map, "DgvTnMultiPoints", dgvTnMultiPoints);
                LoadDgvColWidths(map, "DgvTnPoints", dgvTnPoints);
                LoadDgvColWidths(map, "DgvDuty", dgvDuty);
                LoadDgvColWidths(map, "DgvNoLoad", dgvNoLoad);
                LoadDgvColWidths(map, "DgvReports", dgvReports);

                // 載入 A/B 載台自訂監控參數清單 (記憶功能)
                if (map.ContainsKey("KebMonitors1.Count"))
                {
                    int c1 = 0;
                    if (int.TryParse(map["KebMonitors1.Count"], out c1) && c1 > 0)
                    {
                        var list1 = new List<KebMonitorItem>();
                        for (int i = 0; i < c1; i++)
                        {
                            string key = "KebMonitors1.Item" + i;
                            if (map.ContainsKey(key))
                            {
                                var parts = map[key].Split('|');
                                if (parts.Length >= 7)
                                {
                                    string name = parts[0];
                                    int addr = 0;
                                    try { addr = Convert.ToInt32(parts[1], 16); } catch {}
                                    double scale = 1.0;
                                    double.TryParse(parts[2], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out scale);
                                    string unit = parts[3];
                                    bool isHex = parts[4] == "1";
                                    bool isStatus = parts[5] == "1";
                                    bool isNode = (parts.Length >= 8) ? (parts[6] == "1") : (name.Contains("站號"));
                                    if (addr == 0x0F13 || addr == 0x0231) continue;
                                    if (addr == 0x0F12 && scale == 0.01) { scale = 0.1; unit = "%"; }
                                    if (addr == 0x0203 && (scale == 0.0001 || scale == 0.01)) { scale = 0.0125; }
                                    list1.Add(new KebMonitorItem(name, addr, scale, unit, isHex, isStatus, isNode));
                                }
                            }
                        }
                        if (list1.Count > 0)
                        {
                            if (!list1.Exists(x => x.Address == 0x0203))
                            {
                                list1.Insert(Math.Min(1, list1.Count), new KebMonitorItem("輸出頻率 (ru03)", 0x0203, 0.0125, "Hz"));
                            }
                            kebMonitorList1 = list1;
                            RebuildKebRuGridFromList(1);
                        }
                    }
                }

                if (map.ContainsKey("KebMonitors2.Count"))
                {
                    int c2 = 0;
                    if (int.TryParse(map["KebMonitors2.Count"], out c2) && c2 > 0)
                    {
                        var list2 = new List<KebMonitorItem>();
                        for (int i = 0; i < c2; i++)
                        {
                            string key = "KebMonitors2.Item" + i;
                            if (map.ContainsKey(key))
                            {
                                var parts = map[key].Split('|');
                                if (parts.Length >= 7)
                                {
                                    string name = parts[0];
                                    int addr = 0;
                                    try { addr = Convert.ToInt32(parts[1], 16); } catch {}
                                    double scale = 1.0;
                                    double.TryParse(parts[2], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out scale);
                                    string unit = parts[3];
                                    bool isHex = parts[4] == "1";
                                    bool isStatus = parts[5] == "1";
                                    bool isNode = (parts.Length >= 8) ? (parts[6] == "1") : (name.Contains("站號"));
                                    if (addr == 0x0F13 || addr == 0x0231) continue;
                                    if (addr == 0x0F12 && scale == 0.01) { scale = 0.1; unit = "%"; }
                                    if (addr == 0x0203 && (scale == 0.0001 || scale == 0.01)) { scale = 0.025; }
                                    list2.Add(new KebMonitorItem(name, addr, scale, unit, isHex, isStatus, isNode));
                                }
                            }
                        }
                        if (list2.Count > 0)
                        {
                            if (!list2.Exists(x => x.Address == 0x0203))
                            {
                                list2.Insert(Math.Min(1, list2.Count), new KebMonitorItem("輸出頻率 (ru03)", 0x0203, 0.025, "Hz"));
                            }
                            kebMonitorList2 = list2;
                            RebuildKebRuGridFromList(2);
                        }
                    }
                }

                // Logging Config (Cloud Retention)
                if (map.ContainsKey("Logging.CloudLogMaxCount"))
                {
                    int c;
                    if (int.TryParse(map["Logging.CloudLogMaxCount"], out c) && c >= 5 && c <= 500)
                        cloudLogMaxHistoryCount = c;
                }
                if (map.ContainsKey("Logging.CloudLogMaxDays"))
                {
                    int d;
                    if (int.TryParse(map["Logging.CloudLogMaxDays"], out d) && d >= 1 && d <= 90)
                        cloudLogMaxDays = d;
                }
                if (map.ContainsKey("Logging.LocalLogMaxCount"))
                {
                    int lc;
                    if (int.TryParse(map["Logging.LocalLogMaxCount"], out lc) && lc >= 5 && lc <= 500)
                        localLogMaxHistoryCount = lc;
                }
            }
            catch {}
            finally
            {
                isLayoutLoaded = true;
            }
        }

        public static void SafeSetupSplitContainer(SplitContainer split, int defaultDistance, int p1Min = 30, int p2Min = 30)
        {
            if (split == null) return;
            try
            {
                split.Panel1MinSize = p1Min;
                split.Panel2MinSize = p2Min;

                bool initialized = false;
                Action tryApplyDefault = () => {
                    if (initialized) return;
                    try
                    {
                        int total = (split.Orientation == Orientation.Horizontal) ? split.Height : split.Width;
                        if (total > (p1Min + p2Min + split.SplitterWidth))
                        {
                            int maxDist = total - p2Min - split.SplitterWidth;
                            int target = Math.Max(p1Min, Math.Min(maxDist, defaultDistance));
                            split.SplitterDistance = target;
                            initialized = true;
                        }
                    }
                    catch { }
                };

                int curTotal = (split.Orientation == Orientation.Horizontal) ? split.Height : split.Width;
                if (curTotal > (p1Min + p2Min + split.SplitterWidth))
                {
                    tryApplyDefault();
                }
                else
                {
                    EventHandler onSize = null;
                    onSize = (s, e) => {
                        tryApplyDefault();
                        if (initialized)
                        {
                            try { split.SizeChanged -= onSize; } catch { }
                        }
                    };
                    split.SizeChanged += onSize;
                }
            }
            catch { }
        }

        public bool ApplySplitterDistanceSafe(SplitContainer split, string key, int defaultFallback = -1, int p1Min = 50, int p2Min = 50)
        {
            if (split == null) return false;
            try
            {
                int total = (split.Orientation == Orientation.Horizontal) ? split.Height : split.Width;
                if (total <= (p1Min + p2Min + split.SplitterWidth))
                    return false; // 面板尚未完成排版或處於隱藏狀態 (Total <= 0)

                int desired;
                if (!layoutSplitters.TryGetValue(key, out desired))
                {
                    desired = (defaultFallback > 0) ? defaultFallback : split.SplitterDistance;
                }

                if (desired > 0)
                {
                    split.Panel1MinSize = p1Min;
                    split.Panel2MinSize = p2Min;
                    int maxDist = total - p2Min - split.SplitterWidth;
                    int clamped = Math.Max(p1Min, Math.Min(maxDist, desired));
                    if (split.SplitterDistance != clamped)
                    {
                        split.SplitterDistance = clamped;
                    }
                    layoutSplitters[key] = clamped;
                    return true;
                }
            }
            catch { }
            return false;
        }

        public void ApplyTabSplitters(int tabIndex)
        {
            try
            {
                if (tabIndex == 0) // 即時綜合監控
                {
                    ApplySplitterDistanceSafe(splitMainVertical, "MainVertical", 330, 80, 80);
                    ApplySplitterDistanceSafe(splitDrives, "Drives", 450, 80, 80);
                    ApplySplitterDistanceSafe(splitDrive1, "Drive1", 280, 80, 80);
                    ApplySplitterDistanceSafe(splitDrive2, "Drive2", 280, 80, 80);
                    ApplySplitterDistanceSafe(splitBottomHorizontal, "Bottom", 520, 80, 80);
                    ApplySplitterDistanceSafe(splitParam1, "Param1", 160, 50, 20);
                    ApplySplitterDistanceSafe(splitParam2, "Param2", 160, 50, 20);
                }
                else if (tabIndex == 1) // 多段 T-N 測試
                {
                    ApplySplitterDistanceSafe(splitTnMain, "TnMain", 190, 80, 80);
                    ApplySplitterDistanceSafe(splitTnBottom, "TnBottom", 600, 150, 150);
                    ApplySplitterDistanceSafe(splitTnRight, "TnRight", 260, 120, 120);
                }
                else if (tabIndex == 2) // 工作制測試 Duty
                {
                    ApplySplitterDistanceSafe(splitDuty, "DutyMain", 550, 150, 80);
                    ApplySplitterDistanceSafe(splitDutyTop, "DutyTop", 880, 200, 150);
                }
                else if (tabIndex == 3) // 效率地圖 Map
                {
                    ApplySplitterDistanceSafe(splitEff, "EffMain", 550, 150, 150);
                }
                else if (tabIndex == 4) // 空載溫升 No-Load
                {
                    ApplySplitterDistanceSafe(splitNoLoadMain, "NoLoadMain", 460, 200, 100);
                    ApplySplitterDistanceSafe(splitNoLoadBottom, "NoLoadBottom", 580, 200, 150);
                }
            }
            catch { }
        }

        private void SaveDgvColWidths(StringBuilder sb, string sectionName, DataGridView dgv)
        {
            if (dgv == null || dgv.Columns.Count == 0) return;
            sb.AppendLine("[" + sectionName + "]");
            for (int i = 0; i < dgv.Columns.Count; i++)
            {
                sb.AppendLine("Col" + i + "=" + dgv.Columns[i].Width);
            }
        }

        private void LoadDgvColWidths(Dictionary<string, string> map, string sectionName, DataGridView dgv)
        {
            if (dgv == null || dgv.Columns.Count == 0) return;
            for (int i = 0; i < dgv.Columns.Count; i++)
            {
                string key = sectionName + ".Col" + i;
                if (map.ContainsKey(key))
                {
                    int w;
                    if (int.TryParse(map[key], out w) && w >= 20 && w <= 1200)
                    {
                        try { dgv.Columns[i].Width = w; } catch { }
                    }
                }
            }
        }

        private List<KebMonitorItem> CreateDefaultKebMonitorList()
        {
            return new List<KebMonitorItem>()
            {
                new KebMonitorItem("實測轉速 (ru07)", 0x0207, 0.125, "rpm"),
                new KebMonitorItem("輸出頻率 (ru03)", 0x0203, 0.025, "Hz"),
                new KebMonitorItem("實測轉矩 (ru12)", 0x020C, 0.01,  "Nm"),
                new KebMonitorItem("輸出電流 (ru15)", 0x020F, 0.1,   "A"),
                new KebMonitorItem("轉矩命令 (ru11)", 0x020B, 0.01,  "Nm"),
                new KebMonitorItem("故障代碼 (ru43)", 0x022B, 1.0,   "", false, true, false),
                new KebMonitorItem("輸出電壓 (ru09)", 0x0209, 0.1,   "V"),
                new KebMonitorItem("直流母線 (ru18)", 0x0212, 1.0,   "V"),
                new KebMonitorItem("模組散熱 (ru20)", 0x0214, 1.0,   "°C"),
                new KebMonitorItem("狀態字元 (Sy51)", 0x0033, 1.0,   "", true, false, false),
                new KebMonitorItem("連線站號",        0x0000, 0.0,   "", false, false, true)
            };
        }

        private Control CreateKebRuPanelWithToolbar(int driveId, string valColTitle, out DataGridView targetDgv)
        {
            TableLayoutPanel tlpContainer = new TableLayoutPanel()
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 2,
                Margin = new Padding(0),
                Padding = new Padding(0),
                BackColor = Color.FromArgb(240, 243, 246)
            };
            tlpContainer.RowStyles.Add(new RowStyle(SizeType.Absolute, 36f)); // Row 0: 工具列
            tlpContainer.RowStyles.Add(new RowStyle(SizeType.Percent, 100f)); // Row 1: DataGridView

            // 頂部小工具列 (百分比分割確保按鈕完全可見與 100% 命中點擊)
            TableLayoutPanel pnlToolbar = new TableLayoutPanel()
            {
                Dock = DockStyle.Fill,
                ColumnCount = 3,
                RowCount = 1,
                Margin = new Padding(0),
                Padding = new Padding(1),
                BackColor = Color.FromArgb(232, 240, 250)
            };
            pnlToolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 34f));
            pnlToolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33f));
            pnlToolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33f));

            Button btnAdd = new Button()
            {
                Text = "➕ 新增項目",
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(16, 185, 129),
                ForeColor = Color.White,
                Font = new Font("微軟正黑體", 9f, FontStyle.Bold),
                Margin = new Padding(1),
                Cursor = Cursors.Hand
            };
            Button btnDel = new Button()
            {
                Text = "➖ 刪除選取",
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(239, 68, 68),
                ForeColor = Color.White,
                Font = new Font("微軟正黑體", 9f, FontStyle.Bold),
                Margin = new Padding(1),
                Cursor = Cursors.Hand
            };
            Button btnReset = new Button()
            {
                Text = "🔄 還原預設",
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(100, 116, 139),
                ForeColor = Color.White,
                Font = new Font("微軟正黑體", 9f, FontStyle.Bold),
                Margin = new Padding(1),
                Cursor = Cursors.Hand
            };

            pnlToolbar.Controls.Add(btnAdd, 0, 0);
            pnlToolbar.Controls.Add(btnDel, 1, 0);
            pnlToolbar.Controls.Add(btnReset, 2, 0);

            DataGridView dgv = new DataGridView()
            {
                Dock = DockStyle.Fill,
                BackgroundColor = Color.White,
                BorderStyle = BorderStyle.FixedSingle,
                RowHeadersVisible = false,
                AllowUserToAddRows = false,
                AllowUserToResizeRows = true,
                AllowUserToResizeColumns = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Both,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None,
                Font = new Font("微軟正黑體", 9.5f, FontStyle.Bold),
                ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle()
                {
                    BackColor = Color.FromArgb(220, 232, 245),
                    ForeColor = Color.FromArgb(15, 23, 42),
                    Font = new Font("微軟正黑體", 9.5f, FontStyle.Bold),
                    Alignment = DataGridViewContentAlignment.MiddleCenter
                },
                DefaultCellStyle = new DataGridViewCellStyle()
                {
                    Alignment = DataGridViewContentAlignment.MiddleCenter,
                    Font = new Font("微軟正黑體", 9.5f, FontStyle.Bold),
                    SelectionBackColor = Color.FromArgb(224, 242, 254),
                    SelectionForeColor = Color.Black
                },
                EnableHeadersVisualStyles = false
            };
            dgv.Columns.Add("Param", "項目 (ru/Sy/dr/oP)");
            dgv.Columns.Add("Val", valColTitle);
            dgv.Columns[0].Width = 135;
            dgv.Columns[1].Width = 145;
            dgv.ColumnHeadersHeight = 26;
            dgv.ColumnWidthChanged += (s, e) => SaveLayoutConfig();

            // 初始化預設項目清單
            if (driveId == 1)
            {
                kebMonitorList1 = CreateDefaultKebMonitorList();
                dgvKebRu1 = dgv;
            }
            else
            {
                kebMonitorList2 = CreateDefaultKebMonitorList();
                dgvKebRu2 = dgv;
            }
            targetDgv = dgv;

            // 綁定事件
            btnAdd.Click += (s, e) => ShowAddKebParamDialog(driveId);
            btnDel.Click += (s, e) => RemoveSelectedKebParam(driveId);
            btnReset.Click += (s, e) => ResetKebParamsToDefault(driveId);

            // 右鍵功能選單
            ContextMenuStrip cms = new ContextMenuStrip();
            cms.Items.Add("➕ 新增監控參數項目...", null, (s, e) => ShowAddKebParamDialog(driveId));
            cms.Items.Add("➖ 刪除目前選取項目", null, (s, e) => RemoveSelectedKebParam(driveId));
            cms.Items.Add(new ToolStripSeparator());
            cms.Items.Add("🔄 還原為原廠預設項目清單", null, (s, e) => ResetKebParamsToDefault(driveId));
            dgv.ContextMenuStrip = cms;

            RebuildKebRuGridFromList(driveId);

            tlpContainer.Controls.Add(pnlToolbar, 0, 0);
            tlpContainer.Controls.Add(dgv, 0, 1);
            return tlpContainer;
        }

        private void RebuildKebRuGridFromList(int driveId)
        {
            DataGridView dgv = (driveId == 1) ? dgvKebRu1 : dgvKebRu2;
            List<KebMonitorItem> list = (driveId == 1) ? kebMonitorList1 : kebMonitorList2;
            if (dgv == null || list == null) return;

            dgv.Rows.Clear();
            for (int i = 0; i < list.Count; i++)
            {
                var item = list[i];
                string initVal = "--";
                if (item.IsNode) initVal = (driveId == 1 && numHmiKebNode1 != null) ? ("Node " + (int)numHmiKebNode1.Value) : ("Node " + (driveId == 2 && numHmiKebNode2 != null ? (int)numHmiKebNode2.Value : 1));
                else if (item.IsStatus) initVal = "正常 (無異常)";
                else if (item.IsHex) initVal = "0x0000";
                else if (!string.IsNullOrEmpty(item.Unit)) initVal = "0.0 " + item.Unit;

                int rowIdx = dgv.Rows.Add(item.Name, initVal);
                dgv.Rows[rowIdx].Height = 24;
                dgv.Rows[rowIdx].Cells[0].Style.BackColor = Color.FromArgb(241, 245, 249);
                dgv.Rows[rowIdx].Cells[0].Style.ForeColor = Color.FromArgb(15, 23, 42);
                dgv.Rows[rowIdx].Cells[0].Style.Font = new Font("微軟正黑體", 9f, FontStyle.Bold);
                dgv.Rows[rowIdx].Cells[1].Style.BackColor = Color.White;
                dgv.Rows[rowIdx].Cells[1].Style.Font = new Font("Consolas", 10f, FontStyle.Bold);
                dgv.Rows[rowIdx].Cells[1].Style.ForeColor = (driveId == 1) ? Color.FromArgb(2, 132, 199) : Color.FromArgb(37, 99, 235);
            }
        }

        private void RemoveSelectedKebParam(int driveId)
        {
            DataGridView dgv = (driveId == 1) ? dgvKebRu1 : dgvKebRu2;
            List<KebMonitorItem> list = (driveId == 1) ? kebMonitorList1 : kebMonitorList2;
            if (dgv == null || list == null || list.Count == 0) return;

            int idx = (dgv.CurrentCell != null) ? dgv.CurrentCell.RowIndex : (list.Count - 1);
            if (idx >= 0 && idx < list.Count)
            {
                string name = list[idx].Name;
                list.RemoveAt(idx);
                RebuildKebRuGridFromList(driveId);
                SaveLayoutConfig(); // 自動記憶儲存至配置檔
                if (txtHmiKebLog != null) txtHmiKebLog.AppendText(string.Format("[載台 {0}] 已移除監控項目: {1}\r\n", driveId == 1 ? "A" : "B", name));
            }
        }

        private void ResetKebParamsToDefault(int driveId)
        {
            if (driveId == 1) kebMonitorList1 = CreateDefaultKebMonitorList();
            else kebMonitorList2 = CreateDefaultKebMonitorList();
            RebuildKebRuGridFromList(driveId);
            SaveLayoutConfig(); // 自動記憶儲存至配置檔
            if (txtHmiKebLog != null) txtHmiKebLog.AppendText(string.Format("[載台 {0}] 監控項目已重置為原廠標準清單 (10 項)。\r\n", driveId == 1 ? "A" : "B"));
        }

        private void ShowAddKebParamDialog(int driveId)
        {
            Form dlg = new Form()
            {
                Text = string.Format("➕ 新增【{0}載台】KEB 監控參數項目", driveId == 1 ? "A" : "B"),
                Size = new Size(460, 330),
                StartPosition = FormStartPosition.CenterParent,
                FormBorderStyle = FormBorderStyle.FixedDialog,
                MaximizeBox = false,
                MinimizeBox = false,
                BackColor = Color.FromArgb(240, 244, 248),
                Font = new Font("微軟正黑體", 9.5f)
            };

            TableLayoutPanel tlp = new TableLayoutPanel()
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 6,
                Padding = new Padding(12)
            };
            tlp.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110f));
            tlp.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

            // Row 0: 常用範本
            tlp.Controls.Add(new Label() { Text = "常用參數範本:", Anchor = AnchorStyles.Right, AutoSize = true, Font = new Font("微軟正黑體", 9f, FontStyle.Bold) }, 0, 0);
            ComboBox cmbTpl = new ComboBox() { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList, MaxDropDownItems = 25 };
            var templates = new[] {
                // --- 【ru 運轉即時監視群組 (依據 Combivis 實機校驗)】 ---
                new { Title = "【ru.00】變頻器運轉狀態 (0x0200 / HEX)",          Name = "運轉狀態 (ru00)", Addr = "0200", Scale = "1.0",    Unit = "",    IsHex = true,  IsStatus = false },
                new { Title = "【ru.01】設定轉速顯示 (0x0201 / 0.125 rpm)",      Name = "設定轉速 (ru01)", Addr = "0201", Scale = "0.125",  Unit = "rpm", IsHex = false, IsStatus = false },
                new { Title = "【ru.02】轉速斜坡輸出 (0x0202 / 0.125 rpm)",      Name = "斜坡轉速 (ru02)", Addr = "0202", Scale = "0.125",  Unit = "rpm", IsHex = false, IsStatus = false },
                new { Title = "【ru.03】實測輸出頻率 (0x0203 / 0.025 Hz)",       Name = "輸出頻率 (ru03)", Addr = "0203", Scale = "0.025",  Unit = "Hz",  IsHex = false, IsStatus = false },
                new { Title = "【ru.06】計算轉速反饋 (0x0206 / 0.125 rpm)",      Name = "計算轉速 (ru06)", Addr = "0206", Scale = "0.125",  Unit = "rpm", IsHex = false, IsStatus = false },
                new { Title = "【ru.07】實測轉速反饋 (0x0207 / 0.125 rpm)",      Name = "實測轉速 (ru07)", Addr = "0207", Scale = "0.125",  Unit = "rpm", IsHex = false, IsStatus = false },
                new { Title = "【ru.09】編碼器1轉速 (0x0209 / 0.125 rpm)",       Name = "編碼1轉速(ru09)", Addr = "0209", Scale = "0.125",  Unit = "rpm", IsHex = false, IsStatus = false },
                new { Title = "【ru.10】編碼器2轉速 (0x020A / 0.125 rpm)",       Name = "編碼2轉速(ru10)", Addr = "020A", Scale = "0.125",  Unit = "rpm", IsHex = false, IsStatus = false },
                new { Title = "【ru.11】設定轉矩顯示 (0x020B / 0.01 Nm)",        Name = "設定轉矩 (ru11)", Addr = "020B", Scale = "0.01",   Unit = "Nm",  IsHex = false, IsStatus = false },
                new { Title = "【ru.12】實測轉矩顯示 (0x020C / 0.01 Nm)",        Name = "實測轉矩 (ru12)", Addr = "020C", Scale = "0.01",   Unit = "Nm",  IsHex = false, IsStatus = false },
                new { Title = "【ru.13】實測負載率 (0x020D / 1.0 %)",            Name = "負載率 (ru13)",   Addr = "020D", Scale = "1.0",    Unit = "%",   IsHex = false, IsStatus = false },
                new { Title = "【ru.14】峰值負載率 (0x020E / 1.0 %)",            Name = "峰值負載 (ru14)", Addr = "020E", Scale = "1.0",    Unit = "%",   IsHex = false, IsStatus = false },
                new { Title = "【ru.15】視在輸出電流 (0x020F / 0.1 A)",          Name = "輸出電流 (ru15)", Addr = "020F", Scale = "0.1",    Unit = "A",   IsHex = false, IsStatus = false },
                new { Title = "【ru.16】峰值視在電流 (0x0210 / 0.1 A)",          Name = "峰值電流 (ru16)", Addr = "0210", Scale = "0.1",    Unit = "A",   IsHex = false, IsStatus = false },
                new { Title = "【ru.17】有效輸出電流 (0x0211 / 0.1 A)",          Name = "有效電流 (ru17)", Addr = "0211", Scale = "0.1",    Unit = "A",   IsHex = false, IsStatus = false },
                new { Title = "【ru.18】實測母線電壓 (0x0212 / 1.0 V)",          Name = "母線電壓 (ru18)", Addr = "0212", Scale = "1.0",    Unit = "V",   IsHex = false, IsStatus = false },
                new { Title = "【ru.19】峰值母線電壓 (0x0213 / 1.0 V)",          Name = "峰值母線 (ru19)", Addr = "0213", Scale = "1.0",    Unit = "V",   IsHex = false, IsStatus = false },
                new { Title = "【ru.20】實測輸出電壓 (0x0214 / 1.0 V)",          Name = "輸出電壓 (ru20)", Addr = "0214", Scale = "1.0",    Unit = "V",   IsHex = false, IsStatus = false },
                new { Title = "【ru.38】模組散熱溫度 (0x0226 / 1.0 °C)",         Name = "模組溫度 (ru38)", Addr = "0226", Scale = "1.0",    Unit = "°C",  IsHex = false, IsStatus = false },
                new { Title = "【ru.40】開機運轉時數 (0x0228 / 1.0 h)",          Name = "開機時數 (ru40)", Addr = "0228", Scale = "1.0",    Unit = "h",   IsHex = false, IsStatus = false },
                new { Title = "【ru.41】調變運轉時數 (0x0229 / 1.0 h)",          Name = "調變時數 (ru41)", Addr = "0229", Scale = "1.0",    Unit = "h",   IsHex = false, IsStatus = false },
                new { Title = "【ru.43】故障代碼 (0x022B / Status Text)",        Name = "故障代碼 (ru43)", Addr = "022B", Scale = "1.0",    Unit = "",    IsHex = false, IsStatus = true },
                new { Title = "【ru.47】電動轉矩極限 (0x022F / 0.01 Nm)",        Name = "電動極限 (ru47)", Addr = "022F", Scale = "0.01",   Unit = "Nm",  IsHex = false, IsStatus = false },
                new { Title = "【ru.48】發電轉矩極限 (0x0230 / 0.01 Nm)",        Name = "發電極限 (ru48)", Addr = "0230", Scale = "0.01",   Unit = "Nm",  IsHex = false, IsStatus = false },
                new { Title = "【ru.49】當前基準轉矩 (0x0231 / 0.01 Nm)",        Name = "基準轉矩 (ru49)", Addr = "0231", Scale = "0.01",   Unit = "Nm",  IsHex = false, IsStatus = false },

                // --- 【Sy 系統控制群組 (依據 Combivis 實機校驗)】 ---
                new { Title = "【Sy.02】變頻器機型 (0x0002 / HEX)",             Name = "機型代碼 (Sy02)", Addr = "0002", Scale = "1.0",    Unit = "",    IsHex = true,  IsStatus = false },
                new { Title = "【Sy.06】變頻器站號 (0x0006 / 1.0)",              Name = "通訊站號 (Sy06)", Addr = "0006", Scale = "1.0",    Unit = "",    IsHex = false, IsStatus = false },
                new { Title = "【Sy.07】通訊波特率 (0x0007 / 1.0)",              Name = "波特率 (Sy07)",   Addr = "0007", Scale = "1.0",    Unit = "",    IsHex = false, IsStatus = false },
                new { Title = "【Sy.50】過程控制字 (0x0032 / HEX)",              Name = "控制字元 (Sy50)", Addr = "0032", Scale = "1.0",    Unit = "",    IsHex = true,  IsStatus = false },
                new { Title = "【Sy.51】過程狀態字 (0x0033 / HEX)",              Name = "狀態字元 (Sy51)", Addr = "0033", Scale = "1.0",    Unit = "",    IsHex = true,  IsStatus = false },
                new { Title = "【Sy.52】轉速設定值 (0x0034 / 1 rpm)",            Name = "轉速設定 (Sy52)", Addr = "0034", Scale = "1.0",    Unit = "rpm", IsHex = false, IsStatus = false },
                new { Title = "【Sy.53】實測轉速值 (0x0035 / 1 rpm)",            Name = "實測轉速 (Sy53)", Addr = "0035", Scale = "1.0",    Unit = "rpm", IsHex = false, IsStatus = false },

                // --- 【oP 操作控制群組 (依據 Combivis 實機校驗)】 ---
                new { Title = "【oP.00】目標給定源 (0x0300 / 5=Sy52過程數據)",    Name = "速度源 (oP00)",   Addr = "0300", Scale = "1.0",    Unit = "",    IsHex = false, IsStatus = false },
                new { Title = "【oP.01】運轉方向源 (0x0301 / 7=ST端子, 8=Sy50)", Name = "方向源 (oP01)",   Addr = "0301", Scale = "1.0",    Unit = "",    IsHex = false, IsStatus = false },
                new { Title = "【oP.03】數位轉速設定 (0x0303 / 0.125 rpm)",      Name = "數位轉速 (oP03)", Addr = "0303", Scale = "0.125",  Unit = "rpm", IsHex = false, IsStatus = false },
                new { Title = "【oP.06】正轉最低轉速 (0x0306 / 0.125 rpm)",      Name = "最低轉速 (oP06)", Addr = "0306", Scale = "0.125",  Unit = "rpm", IsHex = false, IsStatus = false },
                new { Title = "【oP.10】正轉最高轉速 (0x030A / 0.125 rpm)",      Name = "最高轉速 (oP10)", Addr = "030A", Scale = "0.125",  Unit = "rpm", IsHex = false, IsStatus = false },
                new { Title = "【oP.14】絕對最大轉速 (0x030E / 0.125 rpm)",      Name = "絕對最高 (oP14)", Addr = "030E", Scale = "0.125",  Unit = "rpm", IsHex = false, IsStatus = false },
                new { Title = "【oP.28】正轉加速時間 (0x031C / 0.01 s)",          Name = "加速時間 (oP28)", Addr = "031C", Scale = "0.01",   Unit = "s",   IsHex = false, IsStatus = false },
                new { Title = "【oP.30】正轉減速時間 (0x031E / 0.01 s)",          Name = "減速時間 (oP30)", Addr = "031E", Scale = "0.01",   Unit = "s",   IsHex = false, IsStatus = false },
                new { Title = "【oP.40】最大輸出轉速 (0x0328 / 0.125 rpm)",      Name = "最大輸出 (oP40)", Addr = "0328", Scale = "0.125",  Unit = "rpm", IsHex = false, IsStatus = false },

                // --- 【cs 控制設定群組 (依據 Combivis 實機校驗)】 ---
                new { Title = "【cs.00】控制架構模式 (0x0F00 / 6=轉矩加載)",      Name = "控制模式 (cs00)", Addr = "0F00", Scale = "1.0",    Unit = "",    IsHex = false, IsStatus = false },
                new { Title = "【cs.04】轉速控制極限 (0x0F04 / 0.125 rpm)",      Name = "控制極限 (cs04)", Addr = "0F04", Scale = "0.125",  Unit = "rpm", IsHex = false, IsStatus = false },
                new { Title = "【cs.06】速度控制Kp (0x0F06 / 1.0)",              Name = "速度Kp (cs06)",   Addr = "0F06", Scale = "1.0",    Unit = "",    IsHex = false, IsStatus = false },
                new { Title = "【cs.09】速度控制Ki (0x0F09 / 1.0)",              Name = "速度Ki (cs09)",   Addr = "0F09", Scale = "1.0",    Unit = "",    IsHex = false, IsStatus = false },
                new { Title = "【cs.15】轉矩命令來源 (0x0F0F / 3=cs18數位)",     Name = "轉矩源 (cs15)",   Addr = "0F0F", Scale = "1.0",    Unit = "",    IsHex = false, IsStatus = false },
                new { Title = "【cs.18】數位轉矩設定 (0x0F12 / 0.1 %)",           Name = "數位轉矩 (cs18)", Addr = "0F12", Scale = "0.1",    Unit = "%",   IsHex = false, IsStatus = false },
                new { Title = "【cs.19】轉矩基準額定 (0x0F13 / 0.01 Nm)",        Name = "轉矩基準 (cs19)", Addr = "0F13", Scale = "0.01",   Unit = "Nm",  IsHex = false, IsStatus = false },

                // --- 【dr 馬達銘牌群組 (依據 Combivis 實機校驗)】 ---
                new { Title = "【dr.00】馬達額定電流 (0x0400 / 0.1 A)",          Name = "額定電流 (dr00)", Addr = "0400", Scale = "0.1",    Unit = "A",   IsHex = false, IsStatus = false },
                new { Title = "【dr.01】馬達額定轉速 (0x0401 / 1.0 rpm)",        Name = "額定轉速 (dr01)", Addr = "0401", Scale = "1.0",    Unit = "rpm", IsHex = false, IsStatus = false },
                new { Title = "【dr.02】馬達額定電壓 (0x0402 / 1.0 V)",          Name = "額定電壓 (dr02)", Addr = "0402", Scale = "1.0",    Unit = "V",   IsHex = false, IsStatus = false },
                new { Title = "【dr.03】馬達額定功率 (0x0403 / 0.01 kW)",        Name = "額定功率 (dr03)", Addr = "0403", Scale = "0.01",   Unit = "kW",  IsHex = false, IsStatus = false },
                new { Title = "【dr.04】馬達功率因數 (0x0404 / 0.01)",           Name = "功率因數 (dr04)", Addr = "0404", Scale = "0.01",   Unit = "",    IsHex = false, IsStatus = false },
                new { Title = "【dr.05】馬達額定頻率 (0x0405 / 0.1 Hz)",         Name = "額定頻率 (dr05)", Addr = "0405", Scale = "0.1",    Unit = "Hz",  IsHex = false, IsStatus = false },
                new { Title = "【dr.14】馬達額定轉矩 (0x040E / 0.01 Nm)",        Name = "額定轉矩 (dr14)", Addr = "040E", Scale = "0.01",   Unit = "Nm",  IsHex = false, IsStatus = false },
                new { Title = "【dr.15】最大轉矩極限 (0x040F / 0.01 Nm)",        Name = "最大轉矩 (dr15)", Addr = "040F", Scale = "0.01",   Unit = "Nm",  IsHex = false, IsStatus = false },
                new { Title = "【dr.16】最高補償轉矩 (0x0410 / 0.01 Nm)",        Name = "補償轉矩 (dr16)", Addr = "0410", Scale = "0.01",   Unit = "Nm",  IsHex = false, IsStatus = false },
                new { Title = "【dr.17】最高轉矩轉速 (0x0411 / 1.0 rpm)",        Name = "滿扭轉速 (dr17)", Addr = "0411", Scale = "1.0",    Unit = "rpm", IsHex = false, IsStatus = false },
                new { Title = "【dr.18】弱磁轉速設定 (0x0412 / 1.0 rpm)",        Name = "弱磁轉速 (dr18)", Addr = "0412", Scale = "1.0",    Unit = "rpm", IsHex = false, IsStatus = false },

                // --- 【uF 曲線群組 (依據 Combivis 實機校驗)】 ---
                new { Title = "【uF.00】額定頻率 (0x0500 / 0.0001 Hz)",          Name = "額定頻率 (uF00)", Addr = "0500", Scale = "0.0001", Unit = "Hz",  IsHex = false, IsStatus = false },
                new { Title = "【uF.01】轉矩提升 (0x0501 / 0.1 %)",              Name = "轉矩提升 (uF01)", Addr = "0501", Scale = "0.1",    Unit = "%",   IsHex = false, IsStatus = false },
                new { Title = "【uF.09】電壓穩定 (0x0509 / 1.0 V)",              Name = "電壓穩定 (uF09)", Addr = "0509", Scale = "1.0",    Unit = "V",   IsHex = false, IsStatus = false },
                new { Title = "【uF.11】載波頻率 (0x050B / 1.0 kHz)",            Name = "載波頻率 (uF11)", Addr = "050B", Scale = "1.0",    Unit = "kHz", IsHex = false, IsStatus = false },
                new { Title = "【uF.12】阻斷時間 (0x050C / 0.01 s)",             Name = "阻斷時間 (uF12)", Addr = "050C", Scale = "0.01",   Unit = "s",   IsHex = false, IsStatus = false },

                // --- 【ud 應用架構群組 (依據 Combivis 實機校驗)】 ---
                new { Title = "【ud.02】控制架構類型 (0x0802 / 1.0)",            Name = "控制架構 (ud02)", Addr = "0802", Scale = "1.0",    Unit = "",    IsHex = false, IsStatus = false },

                // --- 【An 類比訊號群組】 ---
                new { Title = "【An.00】AN1 類比輸入值 (0x0100 / 0.01 %)",       Name = "AN1輸入值 (An00)", Addr = "0100", Scale = "0.01",  Unit = "%",   IsHex = false, IsStatus = false },
                new { Title = "【An.01】AN1 類比輸入電壓 (0x0101 / 0.01 V)",     Name = "AN1電壓 (An01)",  Addr = "0101", Scale = "0.01",  Unit = "V",   IsHex = false, IsStatus = false },
                new { Title = "【An.10】AN2 類比輸入值 (0x010A / 0.01 %)",       Name = "AN2輸入值 (An10)", Addr = "010A", Scale = "0.01",  Unit = "%",   IsHex = false, IsStatus = false },
                new { Title = "【An.11】AN2 類比輸入電壓 (0x010B / 0.01 V)",     Name = "AN2電壓 (An11)",  Addr = "010B", Scale = "0.01",  Unit = "V",   IsHex = false, IsStatus = false },

                // --- 【Ec 編碼器群組】 ---
                new { Title = "【Ec.00】編碼器計數值 (0x0700 / INC)",           Name = "編碼計數 (Ec00)", Addr = "0700", Scale = "1.0",   Unit = "inc", IsHex = false, IsStatus = false },
                new { Title = "【Ec.01】實測轉速 (0x0701 / 0.125 rpm)",         Name = "編碼轉速 (Ec01)", Addr = "0701", Scale = "0.125", Unit = "rpm", IsHex = false, IsStatus = false },

                // --- 【自訂】 ---
                new { Title = "➕ 【自訂輸入任意參數 (位址/倍率/單位)】",       Name = "自訂參數",        Addr = "0200", Scale = "1.0",   Unit = "",    IsHex = false, IsStatus = false }
            };
            foreach (var t in templates) cmbTpl.Items.Add(t.Title);
            cmbTpl.SelectedIndex = 0;
            tlp.Controls.Add(cmbTpl, 1, 0);

            // Row 1: 項目名稱
            tlp.Controls.Add(new Label() { Text = "項目顯示名稱:", Anchor = AnchorStyles.Right, AutoSize = true }, 0, 1);
            TextBox txtName = new TextBox() { Text = templates[0].Name, Dock = DockStyle.Fill };
            tlp.Controls.Add(txtName, 1, 1);

            // Row 2: 16進位位址
            tlp.Controls.Add(new Label() { Text = "16進位位址 (Hex):", Anchor = AnchorStyles.Right, AutoSize = true }, 0, 2);
            TextBox txtAddr = new TextBox() { Text = templates[0].Addr, Dock = DockStyle.Fill, Font = new Font("Consolas", 10f, FontStyle.Bold) };
            tlp.Controls.Add(txtAddr, 1, 2);

            // Row 3: 數值倍率
            tlp.Controls.Add(new Label() { Text = "換算縮放倍率:", Anchor = AnchorStyles.Right, AutoSize = true }, 0, 3);
            TextBox txtScale = new TextBox() { Text = templates[0].Scale, Dock = DockStyle.Fill, Font = new Font("Consolas", 10f) };
            tlp.Controls.Add(txtScale, 1, 3);

            // Row 4: 物理單位
            tlp.Controls.Add(new Label() { Text = "物理量單位:", Anchor = AnchorStyles.Right, AutoSize = true }, 0, 4);
            TextBox txtUnit = new TextBox() { Text = templates[0].Unit, Dock = DockStyle.Fill };
            tlp.Controls.Add(txtUnit, 1, 4);

            cmbTpl.SelectedIndexChanged += (s, e) => {
                int idx = cmbTpl.SelectedIndex;
                if (idx >= 0 && idx < templates.Length)
                {
                    txtName.Text = templates[idx].Name;
                    txtAddr.Text = templates[idx].Addr;
                    txtScale.Text = templates[idx].Scale;
                    txtUnit.Text = templates[idx].Unit;
                }
            };

            // Row 5: 確定 / 取消按鈕
            FlowLayoutPanel flpBtns = new FlowLayoutPanel() { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft };
            Button btnCancel = new Button() { Text = "取消", Width = 80, Height = 32 };
            btnCancel.Click += (s, e) => dlg.Close();
            Button btnOk = new Button() { Text = "確定加入", Width = 95, Height = 32, BackColor = Color.FromArgb(0, 120, 215), ForeColor = Color.White, Font = new Font("微軟正黑體", 9.5f, FontStyle.Bold) };
            btnOk.Click += (s, e) => {
                string name = txtName.Text.Trim();
                if (string.IsNullOrEmpty(name)) { MessageBox.Show("請輸入項目名稱！"); return; }
                int addr = 0;
                try { addr = Convert.ToInt32(txtAddr.Text.Trim(), 16); } catch { MessageBox.Show("請輸入正確的 16 進位位址 (例如 0202)！"); return; }
                double scale = 1.0;
                double.TryParse(txtScale.Text.Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out scale);
                if (scale == 0) scale = 1.0;
                string unit = txtUnit.Text.Trim();

                bool isHex = (cmbTpl.SelectedIndex >= 0 && cmbTpl.SelectedIndex < templates.Length) ? templates[cmbTpl.SelectedIndex].IsHex : false;
                bool isStatus = (cmbTpl.SelectedIndex >= 0 && cmbTpl.SelectedIndex < templates.Length) ? templates[cmbTpl.SelectedIndex].IsStatus : false;

                var list = (driveId == 1) ? kebMonitorList1 : kebMonitorList2;
                list.Add(new KebMonitorItem(name, addr, scale, unit, isHex, isStatus));
                RebuildKebRuGridFromList(driveId);
                SaveLayoutConfig(); // 自動記憶儲存至配置檔
                if (txtHmiKebLog != null) txtHmiKebLog.AppendText(string.Format("[載台 {0}] 已成功加入監控項目: {1} (位址 0x{2:X4})\r\n", driveId == 1 ? "A" : "B", name, addr));
                dlg.Close();
            };
            flpBtns.Controls.Add(btnCancel);
            flpBtns.Controls.Add(btnOk);
            tlp.Controls.Add(flpBtns, 1, 5);

            dlg.Controls.Add(tlp);
            dlg.ShowDialog(this);
        }

        // =========================================================================
        // 分頁 1: 綜合監控與雙變頻控制主畫面 (Main Cockpit: 即時遙測 + 雙 KEB 控制 + 閉迴路平滑追隨)
        // =========================================================================
        private void BuildManualTab(TabPage tab)
        {
            TableLayoutPanel tableManual = new TableLayoutPanel()
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 3,
                BackColor = Color.FromArgb(240, 243, 246),
                Margin = new Padding(0),
                Padding = new Padding(2)
            };
            tableManual.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            tableManual.RowStyles.Add(new RowStyle(SizeType.Absolute, 120f)); // Row 0: 6 大核心即時量測指標 (容納死區與目標值)
            tableManual.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));  // Row 1: 上下可自由拖曳調整之核心工作區 (SplitContainer)
            tableManual.RowStyles.Add(new RowStyle(SizeType.Absolute, 34f));   // Row 2: 設備即時連線燈號列 + 截圖與緊急停機

            // -------------------------------------------------------------
            // Row 0: 頂部 6 大核心量測指標卡片 (轉速 / 轉矩 / 機械功率 / 輸入功率 / 效率 / Kt)
            // -------------------------------------------------------------
            TableLayoutPanel pnlCards = new TableLayoutPanel()
            {
                Dock = DockStyle.Fill,
                ColumnCount = 6,
                RowCount = 1,
                BackColor = Color.Transparent,
                Margin = new Padding(0)
            };
            for (int i = 0; i < 6; i++) pnlCards.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 16.666f));

            lblSpeedVal = CreateMetricCardInCell(pnlCards, "實測轉速 (Speed)", "0.0 rpm", Color.FromArgb(0, 180, 216), 0);
            lblTorqueVal = CreateMetricCardInCell(pnlCards, "實測轉矩 (Torque)", "0.0 Nm", Color.FromArgb(245, 158, 11), 1);
            lblPowerVal = CreateMetricCardInCell(pnlCards, "機械功率 (Power)", "0.00 kW", Color.FromArgb(16, 185, 129), 2);
            lblElecPowerVal = CreateMetricCardInCell(pnlCards, "輸入功率 (Input Pwr)", "0.00 kW", Color.FromArgb(14, 165, 233), 3);
            lblEffVal = CreateMetricCardInCell(pnlCards, "系統效率 (Eff.)", "0.0 %", Color.FromArgb(139, 92, 246), 4);
            lblKtVal = CreateMetricCardInCell(pnlCards, "扭力常數 (Kt)", "0.00", Color.FromArgb(234, 179, 8), 5);
            tableManual.Controls.Add(pnlCards, 0, 0);

            // -------------------------------------------------------------
            // 上半部：A/B 雙載台控制工作區 (左右雙機對稱面板，去除中間冗餘工具列)
            // -------------------------------------------------------------
            Panel pnlKebSection = new Panel() { Dock = DockStyle.Fill, Margin = new Padding(0) };

            // 雙機左右可自由拖曳調整寬度之分割控制器 (SplitContainer)
            splitDrives = new SplitContainer()
            {
                Dock = DockStyle.Fill,
                Orientation = Orientation.Vertical,
                SplitterWidth = 6,
                BackColor = Color.FromArgb(215, 225, 235), // 細緻垂直分割調整線
                Margin = new Padding(0)
            };
            splitDrives.Panel1.BackColor = Color.FromArgb(240, 243, 246);
            splitDrives.Panel1.AutoScroll = true;
            splitDrives.Panel2.BackColor = Color.FromArgb(240, 243, 246);
            splitDrives.Panel2.AutoScroll = true;

            // 左側：Drive 1 (A載台 加載端 COM1)
            GroupBox grpD1 = new GroupBox() { Text = "【A載台】(加載端 / 負載動力計 COM1)", Dock = DockStyle.Fill, Font = new Font("微軟正黑體", 9.5f, FontStyle.Bold) };
            
            TableLayoutPanel pnlLeft1 = new TableLayoutPanel() { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 7, Margin = new Padding(0) };
            pnlLeft1.RowStyles.Add(new RowStyle(SizeType.Absolute, 34f)); // Row 0: COM
            pnlLeft1.RowStyles.Add(new RowStyle(SizeType.Percent, 25f));  // Row 1: 4 Modes (平均放大)
            pnlLeft1.RowStyles.Add(new RowStyle(SizeType.Absolute, 18f)); // Row 2: 轉速標題
            pnlLeft1.RowStyles.Add(new RowStyle(SizeType.Percent, 25f));  // Row 3: 轉速按鈕列 (平均放大)
            pnlLeft1.RowStyles.Add(new RowStyle(SizeType.Absolute, 18f)); // Row 4: 轉矩標題
            pnlLeft1.RowStyles.Add(new RowStyle(SizeType.Percent, 25f));  // Row 5: 轉矩按鈕列 (平均放大)
            pnlLeft1.RowStyles.Add(new RowStyle(SizeType.Percent, 25f));  // Row 6: 正/反 (平均放大)

            Panel pnlConn1 = new Panel() { Dock = DockStyle.Fill, Margin = new Padding(0) };
            Label l1_1 = new Label() { Text = "COM:", Location = new Point(2, 8), AutoSize = true, Font = new Font("微軟正黑體", 9.5f, FontStyle.Bold) };
            cmbHmiKebPort1 = new ComboBox() { Location = new Point(48, 5), Width = 70, DropDownStyle = ComboBoxStyle.DropDownList, Font = new Font("微軟正黑體", 9.5f) };
            cmbHmiKebBaud1 = new ComboBox() { Location = new Point(122, 5), Width = 76, DropDownStyle = ComboBoxStyle.DropDownList, Font = new Font("微軟正黑體", 9.5f) };
            cmbHmiKebBaud1.Items.AddRange(new object[] { "9600", "19200", "38400", "57600", "115200" });
            cmbHmiKebBaud1.SelectedIndex = 0; // 預設 9600 bps (原廠 COMBIVIS / DIN66019-II 標準)

            Label l1_n = new Label() { Text = "站號:", Location = new Point(204, 8), AutoSize = true, Font = new Font("微軟正黑體", 9.5f, FontStyle.Bold) };
            numHmiKebNode1 = CreateNumericUpDown(new Point(244, 5), 44, 1, 239, 1);
            numHmiKebNode1.Font = new Font("Consolas", 10f, FontStyle.Bold);

            btnHmiPortToggle1 = new Button() { Text = "[Open]", Location = new Point(294, 3), Size = new Size(68, 28), BackColor = Color.FromArgb(16, 185, 129), ForeColor = Color.White, Font = new Font("Consolas", 10f, FontStyle.Bold), Cursor = Cursors.Hand };
            btnHmiPortToggle1.Click += (s, e) => {
                if (!isHmiKebOpen1)
                {
                    EnsureHmiKebOpen1();
                }
                else
                {
                    if (CheckConfirmDisconnectWithLock("A載台"))
                    {
                        CloseHmiKebPort1();
                    }
                }
            };

            lblHmiKebStatus1 = new Label() { Text = "狀態: 未連線", Location = new Point(368, 8), AutoSize = true, ForeColor = Color.Gray, Font = new Font("微軟正黑體", 9f) };
            pnlConn1.Controls.AddRange(new Control[] { l1_1, cmbHmiKebPort1, cmbHmiKebBaud1, l1_n, numHmiKebNode1, btnHmiPortToggle1, lblHmiKebStatus1 });
            pnlLeft1.Controls.Add(pnlConn1, 0, 0);

            TableLayoutPanel pnlModes1 = new TableLayoutPanel() { Dock = DockStyle.Fill, ColumnCount = 4, RowCount = 1, Margin = new Padding(0, 1, 0, 1) };
            for (int i = 0; i < 4; i++) pnlModes1.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25f));
            bHmiM1_1 = new Button() { Text = "數位轉速\n(半自動)", Dock = DockStyle.Fill, Margin = new Padding(1), Font = new Font("微軟正黑體", 9f, FontStyle.Bold), Cursor = Cursors.Hand };
            bHmiM1_1.Click += (s, e) => ApplyHmiKebInterlock(1, 7);
            bHmiM1_2 = new Button() { Text = "數位轉矩\n(半自動)", Dock = DockStyle.Fill, Margin = new Padding(1), Font = new Font("微軟正黑體", 9f, FontStyle.Bold), Cursor = Cursors.Hand };
            bHmiM1_2.Click += (s, e) => ApplyHmiKebInterlock(1, 8);
            bHmiM1_3 = new Button() { Text = "數位轉速\n(全自動)", Dock = DockStyle.Fill, Margin = new Padding(1), Font = new Font("微軟正黑體", 9f, FontStyle.Bold), Cursor = Cursors.Hand };
            bHmiM1_3.Click += (s, e) => ApplyHmiKebInterlock(1, 9);
            bHmiM1_4 = new Button() { Text = "數位轉矩\n(全自動)", Dock = DockStyle.Fill, Margin = new Padding(1), Font = new Font("微軟正黑體", 9f, FontStyle.Bold), Cursor = Cursors.Hand };
            bHmiM1_4.Click += (s, e) => ApplyHmiKebInterlock(1, 10);
            pnlModes1.Controls.AddRange(new Control[] { bHmiM1_1, bHmiM1_2, bHmiM1_3, bHmiM1_4 });
            pnlLeft1.Controls.Add(pnlModes1, 0, 1);

            Label lSpd1 = new Label() { Text = "轉速:", Font = new Font("微軟正黑體", 9f, FontStyle.Bold), ForeColor = Color.FromArgb(15, 23, 42), AutoSize = true, Margin = new Padding(2, 2, 0, 0) };
            pnlLeft1.Controls.Add(lSpd1, 0, 2);

            TableLayoutPanel pnlSpd1 = new TableLayoutPanel() { Dock = DockStyle.Fill, ColumnCount = 6, RowCount = 1, Margin = new Padding(0, 1, 0, 1) };
            pnlSpd1.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 13f)); // -100
            pnlSpd1.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 12f)); // -1
            pnlSpd1.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25f)); // numeric
            pnlSpd1.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 12f)); // +1
            pnlSpd1.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 13f)); // +100
            pnlSpd1.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25f)); // 寫入按鈕 (與上方模式按鈕 25% 完美對齊)

            Button bSD100_1 = new Button() { Text = "-100", Dock = DockStyle.Fill, Margin = new Padding(1), Font = new Font("微軟正黑體", 9f) };
            bSD100_1.Click += (s, e) => { if (numHmiKebSpeed1.Value >= 100) numHmiKebSpeed1.Value -= 100; };
            Button bSD1_1 = new Button() { Text = "-1", Dock = DockStyle.Fill, Margin = new Padding(1), Font = new Font("微軟正黑體", 9f) };
            bSD1_1.Click += (s, e) => {
                if (numHmiKebSpeed1.Value >= 1)
                {
                    numHmiKebSpeed1.Value -= 1;
                    if (!isHmiKebOpen1) return;
                    int speedRpm = (int)numHmiKebSpeed1.Value;
                    int comIdx = GetHmiKebComIdx(1), baudIdx = GetHmiKebBaudIdx(1), node = (int)numHmiKebNode1.Value;
                    KebWriteParam32(comIdx, baudIdx, node, 0x0034, speedRpm, string.Format("A載台微調速度 -1 rpm -> {0} rpm (Sy52)", speedRpm));
                    WriteHmiLog("USER_OP", string.Format("[A載台] 微調速度 -1 rpm: {0} rpm (Sy.52=0x0034) | 當前模式: {1}", speedRpm, GetKebModeName(currentKebMode1)));
                    UpdateHmiKebModeParamsDisplay(1);
                }
            };

            numHmiKebSpeed1 = CreateNumericUpDown(new Point(0, 0), 76, 0, 6000, 0);
            numHmiKebSpeed1.Dock = DockStyle.Fill;
            numHmiKebSpeed1.Font = new Font("Consolas", 10.5f, FontStyle.Bold);
            numHmiKebSpeed1.TextAlign = HorizontalAlignment.Center;
            numHmiKebSpeed1.ValueChanged += (s, e) => UpdateHmiKebModeParamsDisplay(1);

            Button bSI1_1 = new Button() { Text = "+1", Dock = DockStyle.Fill, Margin = new Padding(1), Font = new Font("微軟正黑體", 9f) };
            bSI1_1.Click += (s, e) => {
                if (numHmiKebSpeed1.Value <= 5999)
                {
                    numHmiKebSpeed1.Value += 1;
                    if (!isHmiKebOpen1) return;
                    int speedRpm = (int)numHmiKebSpeed1.Value;
                    int comIdx = GetHmiKebComIdx(1), baudIdx = GetHmiKebBaudIdx(1), node = (int)numHmiKebNode1.Value;
                    KebWriteParam32(comIdx, baudIdx, node, 0x0034, speedRpm, string.Format("A載台微調速度 +1 rpm -> {0} rpm (Sy52)", speedRpm));
                    WriteHmiLog("USER_OP", string.Format("[A載台] 微調速度 +1 rpm: {0} rpm (Sy.52=0x0034) | 當前模式: {1}", speedRpm, GetKebModeName(currentKebMode1)));
                    UpdateHmiKebModeParamsDisplay(1);
                }
            };
            Button bSI100_1 = new Button() { Text = "+100", Dock = DockStyle.Fill, Margin = new Padding(1), Font = new Font("微軟正黑體", 9f) };
            bSI100_1.Click += (s, e) => { if (numHmiKebSpeed1.Value <= 5900) numHmiKebSpeed1.Value += 100; };

            Button btnSetSpd1 = new Button() { Text = "寫入速度", Dock = DockStyle.Fill, Margin = new Padding(1), BackColor = Color.FromArgb(0, 180, 216), ForeColor = Color.White, Font = new Font("微軟正黑體", 10f, FontStyle.Bold), Cursor = Cursors.Hand };
            btnSetSpd1.Click += (s, e) => {
                if (!isHmiKebOpen1) { MessageBox.Show("A台 (COM1) 未連線！請先點擊 [Open]。", "A台未連線", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
                int comIdx = GetHmiKebComIdx(1);
                int baudIdx = GetHmiKebBaudIdx(1);
                int node = (int)numHmiKebNode1.Value;
                int speedRpm = (int)numHmiKebSpeed1.Value;
                KebWriteParam32(comIdx, baudIdx, node, 0x0034, speedRpm, string.Format("A載台速度設定 Sy.52={0} rpm", speedRpm));
                WriteHmiLog("USER_OP", string.Format("[A載台] 使用者寫入速度目標: {0} rpm (Sy.52=0x0034) | 當前模式: {1}", speedRpm, GetKebModeName(currentKebMode1)));
                UpdateHmiKebModeParamsDisplay(1);
            };
            pnlSpd1.Controls.Add(bSD100_1, 0, 0);
            pnlSpd1.Controls.Add(bSD1_1, 1, 0);
            pnlSpd1.Controls.Add(numHmiKebSpeed1, 2, 0);
            pnlSpd1.Controls.Add(bSI1_1, 3, 0);
            pnlSpd1.Controls.Add(bSI100_1, 4, 0);
            pnlSpd1.Controls.Add(btnSetSpd1, 5, 0);
            pnlLeft1.Controls.Add(pnlSpd1, 0, 3);

            Panel pnlTrqHeader1 = new Panel() { Dock = DockStyle.Fill, Height = 20, Margin = new Padding(0) };
            Label lTrq1 = new Label() { Text = "轉矩 (cs.18):", Dock = DockStyle.Left, AutoSize = true, Font = new Font("微軟正黑體", 9f, FontStyle.Bold), ForeColor = Color.FromArgb(15, 23, 42) };
            lblCs19Disp1 = new Label() { Text = "cs.19基準: -- Nm (待讀取)", Dock = DockStyle.Right, AutoSize = true, Font = new Font("Consolas", 8.5f, FontStyle.Bold), ForeColor = Color.FromArgb(140, 150, 165) };
            ToolTip ttCs19_1 = new ToolTip();
            ttCs19_1.SetToolTip(lblCs19Disp1, "連線後將自驅動器讀取 cs.19 (0x0F13)\n0.1% 單步物理極限 x = cs19 / 1000");
            pnlTrqHeader1.Controls.Add(lblCs19Disp1);
            pnlTrqHeader1.Controls.Add(lTrq1);
            pnlLeft1.Controls.Add(pnlTrqHeader1, 0, 4);

            TableLayoutPanel pnlTrq1 = new TableLayoutPanel() { Dock = DockStyle.Fill, ColumnCount = 6, RowCount = 1, Margin = new Padding(0, 1, 0, 1) };
            pnlTrq1.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 13f)); // -1%
            pnlTrq1.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 12f)); // -0.1
            pnlTrq1.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25f)); // numeric
            pnlTrq1.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 12f)); // +0.1
            pnlTrq1.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 13f)); // +1%
            pnlTrq1.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25f)); // 寫入按鈕 (與上方模式按鈕 25% 完美對齊)

            Button bTD1_1 = new Button() { Text = "-1%", Dock = DockStyle.Fill, Margin = new Padding(1), Font = new Font("微軟正黑體", 9f) };
            bTD1_1.Click += (s, e) => {
                if (numHmiKebTorque1.Value >= 1m)
                {
                    numHmiKebTorque1.Value -= 1m;
                    if (!isHmiKebOpen1) return;
                    double trqPct = (double)numHmiKebTorque1.Value;
                    int rawTrq = (int)Math.Round(trqPct * 10); // KEB cs.18 單位 0.1%/LSB: 1.0% = RAW 10
                    KebWriteParam32(GetHmiKebComIdx(1), GetHmiKebBaudIdx(1), (int)numHmiKebNode1.Value, 0x0F12, rawTrq, string.Format("A載台微調轉矩 -1% -> {0:F1}%", trqPct));
                    WriteHmiLog("USER_OP", string.Format("[A載台] 微調轉矩 -1% (直接生效): {0:F1} % (RAW={1}, cs.18)", trqPct, rawTrq));
                    UpdateHmiKebModeParamsDisplay(1);
                }
            };
            Button bTD01_1 = new Button() { Text = "-0.1", Dock = DockStyle.Fill, Margin = new Padding(1), Font = new Font("微軟正黑體", 9f) };
            bTD01_1.Click += (s, e) => {
                if (numHmiKebTorque1.Value >= 0.1m)
                {
                    numHmiKebTorque1.Value -= 0.1m;
                    if (!isHmiKebOpen1) return;
                    double trqPct = (double)numHmiKebTorque1.Value;
                    int rawTrq = (int)Math.Round(trqPct * 10); // KEB cs.18 單位 0.1%/LSB: 1.0% = RAW 10
                    KebWriteParam32(GetHmiKebComIdx(1), GetHmiKebBaudIdx(1), (int)numHmiKebNode1.Value, 0x0F12, rawTrq, string.Format("A載台微調轉矩 -0.1% -> {0:F1}%", trqPct));
                    WriteHmiLog("USER_OP", string.Format("[A載台] 微調轉矩 -0.1% (直接生效): {0:F1} % (RAW={1}, cs.18)", trqPct, rawTrq));
                    UpdateHmiKebModeParamsDisplay(1);
                }
            };

            numHmiKebTorque1 = CreateNumericUpDown(new Point(0, 0), 76, 0, 150, 0, 1, 0.1m);
            numHmiKebTorque1.Dock = DockStyle.Fill;
            numHmiKebTorque1.Font = new Font("Consolas", 10.5f, FontStyle.Bold);
            numHmiKebTorque1.TextAlign = HorizontalAlignment.Center;
            numHmiKebTorque1.ValueChanged += (s, e) => UpdateHmiKebModeParamsDisplay(1);

            Button bTI01_1 = new Button() { Text = "+0.1", Dock = DockStyle.Fill, Margin = new Padding(1), Font = new Font("微軟正黑體", 9f) };
            bTI01_1.Click += (s, e) => {
                if (numHmiKebTorque1.Value <= 149.9m)
                {
                    numHmiKebTorque1.Value += 0.1m;
                    if (!isHmiKebOpen1) return;
                    double trqPct = (double)numHmiKebTorque1.Value;
                    int rawTrq = (int)Math.Round(trqPct * 10); // KEB cs.18 單位 0.1%/LSB: 1.0% = RAW 10
                    KebWriteParam32(GetHmiKebComIdx(1), GetHmiKebBaudIdx(1), (int)numHmiKebNode1.Value, 0x0F12, rawTrq, string.Format("A載台微調轉矩 +0.1% -> {0:F1}%", trqPct));
                    WriteHmiLog("USER_OP", string.Format("[A載台] 微調轉矩 +0.1% (直接生效): {0:F1} % (RAW={1}, cs.18)", trqPct, rawTrq));
                    UpdateHmiKebModeParamsDisplay(1);
                }
            };
            Button bTI1_1 = new Button() { Text = "+1%", Dock = DockStyle.Fill, Margin = new Padding(1), Font = new Font("微軟正黑體", 9f) };
            bTI1_1.Click += (s, e) => {
                if (numHmiKebTorque1.Value <= 149m)
                {
                    numHmiKebTorque1.Value += 1m;
                    if (!isHmiKebOpen1) return;
                    double trqPct = (double)numHmiKebTorque1.Value;
                    int rawTrq = (int)Math.Round(trqPct * 10); // KEB cs.18 單位 0.1%/LSB: 1.0% = RAW 10
                    KebWriteParam32(GetHmiKebComIdx(1), GetHmiKebBaudIdx(1), (int)numHmiKebNode1.Value, 0x0F12, rawTrq, string.Format("A載台微調轉矩 +1% -> {0:F1}%", trqPct));
                    WriteHmiLog("USER_OP", string.Format("[A載台] 微調轉矩 +1% (直接生效): {0:F1} % (RAW={1}, cs.18)", trqPct, rawTrq));
                    UpdateHmiKebModeParamsDisplay(1);
                }
            };

            Button btnSetTrq1 = new Button() { Text = "寫入轉矩", Dock = DockStyle.Fill, Margin = new Padding(1), BackColor = Color.FromArgb(245, 158, 11), ForeColor = Color.White, Font = new Font("微軟正黑體", 10f, FontStyle.Bold), Cursor = Cursors.Hand };
            btnSetTrq1.Click += (s, e) => {
                if (!isHmiKebOpen1) { MessageBox.Show("A台 (COM1) 未連線！請先點擊 [Open]。", "A台未連線", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
                int comIdx = GetHmiKebComIdx(1);
                int baudIdx = GetHmiKebBaudIdx(1);
                double trqPct = (double)numHmiKebTorque1.Value;
                int rawTrq = (int)Math.Round(trqPct * 10); // KEB cs.18 單位 0.1%/LSB: 1.0% = RAW 10
                KebWriteParam32(comIdx, baudIdx, (int)numHmiKebNode1.Value, 0x0F12, rawTrq, string.Format("A載台轉矩設定 cs.18={0:F1} % (RAW={1})", trqPct, rawTrq));
                WriteHmiLog("USER_OP", string.Format("[A載台] 使用者寫入轉矩目標: {0:F1} % (RAW={1}, cs.18=0x0F12)", trqPct, rawTrq));
                UpdateHmiKebModeParamsDisplay(1);
            };
            pnlTrq1.Controls.Add(bTD1_1, 0, 0);
            pnlTrq1.Controls.Add(bTD01_1, 1, 0);
            pnlTrq1.Controls.Add(numHmiKebTorque1, 2, 0);
            pnlTrq1.Controls.Add(bTI01_1, 3, 0);
            pnlTrq1.Controls.Add(bTI1_1, 4, 0);
            pnlTrq1.Controls.Add(btnSetTrq1, 5, 0);
            pnlLeft1.Controls.Add(pnlTrq1, 0, 5);

            pnlRun1 = new TableLayoutPanel() { Dock = DockStyle.Fill, ColumnCount = 4, RowCount = 1, Margin = new Padding(0, 1, 0, 1) };
            pnlRun1.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 28f));
            pnlRun1.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 28f));
            pnlRun1.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 22f));
            pnlRun1.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 22f));
            btnRF1 = new Button() { Text = "正轉 (RUN)", Dock = DockStyle.Fill, Margin = new Padding(1), BackColor = Color.FromArgb(16, 185, 129), ForeColor = Color.White, Font = new Font("微軟正黑體", 9.5f, FontStyle.Bold), Cursor = Cursors.Hand };
            btnRF1.Click += (s, e) => {
                // RUN 按鈕：若斷線直接提示，不阻塞 UI 主執行緒
                if (!isHmiKebOpen1) { MessageBox.Show("A台 (COM1) 未連線！請先點擊 [Open] 建立連線。", "A台未連線", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
                string opName = (currentKebMode1 == 8 || currentKebMode1 == 10) ? "A載台加載端啟用加載 (LOAD)" : "A載台待測端正轉";
                int targetSpd = (numHmiKebSpeed1 != null) ? (int)numHmiKebSpeed1.Value : 0;
                double targetTrq = (numHmiKebTorque1 != null) ? (double)numHmiKebTorque1.Value : 0.0;
                int comIdx = GetHmiKebComIdx(1);
                int baudIdx = GetHmiKebBaudIdx(1);
                int nodeId = (int)numHmiKebNode1.Value;

                // 運轉前全自動狀態自檢與診斷紀錄
                int? curRu00 = KebReadParamWithDll(comIdx, baudIdx, nodeId, 0x0200);
                WriteHmiLog("RUN_DIAG", string.Format("[A載台 RUN前自檢] 模式={0} | 給定轉速={1} rpm | 給定轉矩={2:F2} Nm | 變頻器當前狀態={3}",
                    GetKebModeName(currentKebMode1), targetSpd, targetTrq, curRu00.HasValue ? DecodeKebRu00(curRu00.Value) : "未讀取到"));

                if (currentKebMode1 == 7 || currentKebMode1 == 9)
                {
                    if (targetSpd <= 0)
                    {
                        WriteHmiLog("WARN", "【⚠️ A載台目標轉速為 0 rpm】目前設定轉速為 0，變頻器將進入零速伺服鎖定而不旋轉！若需轉動請先調整轉速 (>0 rpm)。");
                    }
                    // 速度模式下確保轉矩限制 cs.18 不為 0 (預設 100.0% = 1000)
                    KebWriteParamWithDll(comIdx, baudIdx, nodeId, 0x0F12, 1000);
                }
                else if (currentKebMode1 == 8 || currentKebMode1 == 10)
                {
                    // 轉矩加載模式下：啟用加載時強制將畫面轉矩設定值寫入 cs.18
                    int rawTrq = (int)Math.Round(targetTrq * 10);
                    KebWriteParam32(comIdx, baudIdx, nodeId, 0x0F12, rawTrq, string.Format("A載台加載端啟用加載 cs.18={0:F1}%", targetTrq));
                    WriteHmiLog("USER_OP", string.Format("[A載台] 啟用加載寫入轉矩目標: {0:F1} % (RAW={1}, cs.18=0x0F12)", targetTrq, rawTrq));
                }

                if (curRu00.HasValue && curRu00.Value == 0)
                {
                    WriteHmiLog("WARN", "【⚠️ A載台硬體 ST 斷開中】變頻器目前顯示 nOP，硬體功率級未放行！請閉合機櫃實體 ST 端子開關。");
                }
                int? faultCode1 = KebReadParamWithDll(comIdx, baudIdx, nodeId, 0x022B);
                if (faultCode1.HasValue && faultCode1.Value != 0)
                {
                    WriteHmiLog("ALARM", string.Format("【🚨 A載台處於故障鎖定中】狀態={0}, 故障碼 ru.43=0x{1:X2} ({2})，請排除故障後點擊 [復歸] 按鈕！", curRu00.HasValue ? DecodeKebRu00(curRu00.Value) : "--", faultCode1.Value, DecodeKebFaultCode(faultCode1.Value)));
                }

                if (targetSpd >= 0)
                {
                    KebWriteParam32(comIdx, baudIdx, nodeId, 0x0034, targetSpd, "A載台速度給定 Sy.52");
                }
                SetHmiKebCommand(comIdx, baudIdx, nodeId, 4, opName); // 正轉 Sy50=4 (Bit2=1 RUN, Bit3=0 FOR)
            };
            btnRR1 = new Button() { Text = "反轉 (RUN)", Dock = DockStyle.Fill, Margin = new Padding(1), BackColor = Color.FromArgb(139, 92, 246), ForeColor = Color.White, Font = new Font("微軟正黑體", 9.5f, FontStyle.Bold), Cursor = Cursors.Hand };
            btnRR1.Click += (s, e) => {
                if (!isHmiKebOpen1) { MessageBox.Show("A台 (COM1) 未連線！請先點擊 [Open] 建立連線。", "A台未連線", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
                int targetSpd = (numHmiKebSpeed1 != null) ? (int)numHmiKebSpeed1.Value : 0;
                double targetTrq = (numHmiKebTorque1 != null) ? (double)numHmiKebTorque1.Value : 0.0;
                int comIdx = GetHmiKebComIdx(1);
                int baudIdx = GetHmiKebBaudIdx(1);
                int nodeId = (int)numHmiKebNode1.Value;

                int? curRu00 = KebReadParamWithDll(comIdx, baudIdx, nodeId, 0x0200);
                WriteHmiLog("RUN_DIAG", string.Format("[A載台 反轉RUN前自檢] 模式={0} | 給定轉速={1} rpm | 變頻器當前狀態={2}",
                    GetKebModeName(currentKebMode1), targetSpd, curRu00.HasValue ? DecodeKebRu00(curRu00.Value) : "未讀取到"));

                if (currentKebMode1 == 7 || currentKebMode1 == 9)
                {
                    KebWriteParamWithDll(comIdx, baudIdx, nodeId, 0x0F12, 1000);
                }
                else if (currentKebMode1 == 8 || currentKebMode1 == 10)
                {
                    int rawTrq = (int)Math.Round(targetTrq * 10);
                    KebWriteParam32(comIdx, baudIdx, nodeId, 0x0F12, rawTrq, string.Format("A載台反向加載 cs.18={0:F1}%", targetTrq));
                    WriteHmiLog("USER_OP", string.Format("[A載台] 反向加載寫入轉矩目標: {0:F1} % (RAW={1}, cs.18=0x0F12)", targetTrq, rawTrq));
                }

                if (targetSpd >= 0)
                {
                    KebWriteParam32(comIdx, baudIdx, nodeId, 0x0034, targetSpd, "A載台速度給定 Sy.52");
                }
                SetHmiKebCommand(comIdx, baudIdx, nodeId, 12, "A載台加載端反轉"); // 反轉 Sy50=12 (Bit2=1 RUN, Bit3=1 REV)
            };
            btnStop1 = new Button() { Text = "🛑 停機", Dock = DockStyle.Fill, Margin = new Padding(1), BackColor = Color.FromArgb(239, 68, 68), ForeColor = Color.White, Font = new Font("微軟正黑體", 9.5f, FontStyle.Bold), Cursor = Cursors.Hand };
            btnStop1.Click += (s, e) => {
                // 停機為安全指令，嘗試確保連線後才送；若已連線直接送
                bool isFullAuto = (currentKebMode1 == 9 || currentKebMode1 == 10 || currentKebMode2 == 9 || currentKebMode2 == 10);
                if (isFullAuto)
                {
                    ExecuteFullAutoGracefulStop("A載台點擊停機按鈕");
                }
                else if (isHmiKebOpen1)
                {
                    if (currentKebMode1 == 8 || currentKebMode1 == 10)
                    {
                        KebWriteParam32(GetHmiKebComIdx(1), GetHmiKebBaudIdx(1), (int)numHmiKebNode1.Value, 0x0F12, 0, "A載台卸載歸零 cs.18=0");
                    }
                    string opName = (currentKebMode1 == 8 || currentKebMode1 == 10) ? "A載台卸載停機" : "A載台停機";
                    SetHmiKebCommand(GetHmiKebComIdx(1), GetHmiKebBaudIdx(1), (int)numHmiKebNode1.Value, 0, opName);
                }
                else
                {
                    WriteHmiLog("WARN", "[A台停機] A台 COM1 已斷線，停機命令無法送達！");
                    if (txtHmiKebLog != null) txtHmiKebLog.AppendText("[警告] A台 (COM1) 斷線，停機指令無法送達，請手動切斷電源！\r\n");
                }
            };
            btnReset1 = new Button() { Text = "🔄 復歸", Dock = DockStyle.Fill, Margin = new Padding(1), BackColor = Color.FromArgb(245, 158, 11), ForeColor = Color.White, Font = new Font("微軟正黑體", 9.5f, FontStyle.Bold), Cursor = Cursors.Hand };
            btnReset1.Click += (s, e) => {
                if (!isHmiKebOpen1) { MessageBox.Show("A台 (COM1) 未連線！請先點擊 [Open] 建立連線。", "A台未連線", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
                SetHmiKebCommand(GetHmiKebComIdx(1), GetHmiKebBaudIdx(1), (int)numHmiKebNode1.Value, 2, "A載台故障復歸 (FAULT RESET)");
                WriteHmiLog("USER_OP", "[A載台] 使用者點擊故障復歸 (Sy50=2)");
            };
            pnlRun1.Controls.AddRange(new Control[] { btnRF1, btnRR1, btnStop1, btnReset1 });
            pnlLeft1.Controls.Add(pnlRun1, 0, 6);

            // 系統判斷可自訂調整高度容器
            Panel pnlParamsBox1 = new Panel()
            {
                Dock = DockStyle.Fill,
                Margin = new Padding(2),
                BackColor = Color.FromArgb(238, 245, 255),
                BorderStyle = BorderStyle.FixedSingle,
                Padding = new Padding(6, 4, 6, 4)
            };
            Label lTitleP1 = new Label()
            {
                Text = "控制架構與系統判斷:",
                Dock = DockStyle.Top,
                Height = 18,
                Font = new Font("微軟正黑體", 9f, FontStyle.Bold),
                ForeColor = Color.FromArgb(30, 64, 175)
            };
            lblKebModeParams1 = new Label()
            {
                Text = "🔍 系統判斷：【未連線 / 待開啟 COM 埠】",
                Dock = DockStyle.Fill,
                Font = new Font("微軟正黑體", 9.5f, FontStyle.Bold),
                ForeColor = Color.FromArgb(15, 23, 42),
                TextAlign = ContentAlignment.MiddleLeft
            };
            pnlParamsBox1.Controls.Add(lblKebModeParams1);
            pnlParamsBox1.Controls.Add(lTitleP1);

            splitParam1 = new SplitContainer()
            {
                Dock = DockStyle.Fill,
                Orientation = Orientation.Horizontal,
                SplitterWidth = 6,
                BackColor = Color.FromArgb(215, 225, 235),
                Margin = new Padding(0)
            };
            splitParam1.Panel1.BackColor = Color.FromArgb(240, 243, 246);
            splitParam1.Panel2.BackColor = Color.FromArgb(240, 243, 246);
            splitParam1.Panel1.Controls.Add(pnlLeft1);
            splitParam1.Panel2.Controls.Add(pnlParamsBox1);
            splitParam1.SplitterDistance = 245; // 預設高度
            splitParam1.SplitterMoved += (s, e) => SaveLayoutConfig();

            Control pnlRuContainer1 = CreateKebRuPanelWithToolbar(1, "A載台數值", out dgvKebRu1);

            splitDrive1 = new SplitContainer()
            {
                Dock = DockStyle.Fill,
                Orientation = Orientation.Vertical,
                SplitterWidth = 6,
                BackColor = Color.FromArgb(215, 225, 235),
                Margin = new Padding(0)
            };
            splitDrive1.Panel1.BackColor = Color.FromArgb(240, 243, 246);
            splitDrive1.Panel1.AutoScroll = true;
            splitDrive1.Panel2.BackColor = Color.FromArgb(240, 243, 246);
            splitDrive1.Panel2.AutoScroll = true;
            splitDrive1.Panel1.Controls.Add(splitParam1);
            splitDrive1.Panel2.Controls.Add(pnlRuContainer1);
            splitDrive1.SplitterDistance = 420;
            splitDrive1.SplitterMoved += (s, e) => SaveLayoutConfig();

            grpD1.Controls.Add(splitDrive1);
            splitDrives.Panel1.Controls.Add(grpD1);

            hmiSpdControls1 = new Control[] { bSD100_1, bSD1_1, numHmiKebSpeed1, bSI1_1, bSI100_1, btnSetSpd1 };
            hmiTrqControls1 = new Control[] { bTD1_1, bTD01_1, numHmiKebTorque1, bTI01_1, bTI1_1, btnSetTrq1 };

            // 右側：Drive 2 (B載台 待測端 COM2)
            GroupBox grpD2 = new GroupBox() { Text = "【B載台】(待測端 / 待測馬達 COM2)", Dock = DockStyle.Fill, Font = new Font("微軟正黑體", 9.5f, FontStyle.Bold) };
            
            TableLayoutPanel pnlLeft2 = new TableLayoutPanel() { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 7, Margin = new Padding(0) };
            pnlLeft2.RowStyles.Add(new RowStyle(SizeType.Absolute, 34f)); // Row 0: COM
            pnlLeft2.RowStyles.Add(new RowStyle(SizeType.Percent, 25f));  // Row 1: 4 Modes (平均放大)
            pnlLeft2.RowStyles.Add(new RowStyle(SizeType.Absolute, 18f)); // Row 2: 轉速標題
            pnlLeft2.RowStyles.Add(new RowStyle(SizeType.Percent, 25f));  // Row 3: 轉速按鈕列 (平均放大)
            pnlLeft2.RowStyles.Add(new RowStyle(SizeType.Absolute, 18f)); // Row 4: 轉矩標題
            pnlLeft2.RowStyles.Add(new RowStyle(SizeType.Percent, 25f));  // Row 5: 轉矩按鈕列 (平均放大)
            pnlLeft2.RowStyles.Add(new RowStyle(SizeType.Percent, 25f));  // Row 6: 正/反 (平均放大)

            Panel pnlConn2 = new Panel() { Dock = DockStyle.Fill, Margin = new Padding(0) };
            Label l2_1 = new Label() { Text = "COM:", Location = new Point(2, 8), AutoSize = true, Font = new Font("微軟正黑體", 9.5f, FontStyle.Bold) };
            cmbHmiKebPort2 = new ComboBox() { Location = new Point(48, 5), Width = 70, DropDownStyle = ComboBoxStyle.DropDownList, Font = new Font("微軟正黑體", 9.5f) };
            cmbHmiKebBaud2 = new ComboBox() { Location = new Point(122, 5), Width = 76, DropDownStyle = ComboBoxStyle.DropDownList, Font = new Font("微軟正黑體", 9.5f) };
            cmbHmiKebBaud2.Items.AddRange(new object[] { "9600", "19200", "38400", "57600", "115200" });
            cmbHmiKebBaud2.SelectedIndex = 0; // 預設 9600 bps (與 A 載台對齊標準)

            Label l2_n = new Label() { Text = "站號:", Location = new Point(204, 8), AutoSize = true, Font = new Font("微軟正黑體", 9.5f, FontStyle.Bold) };
            numHmiKebNode2 = CreateNumericUpDown(new Point(244, 5), 44, 1, 239, 1);
            numHmiKebNode2.Font = new Font("Consolas", 10f, FontStyle.Bold);

            btnHmiPortToggle2 = new Button() { Text = "[Open]", Location = new Point(294, 3), Size = new Size(68, 28), BackColor = Color.FromArgb(16, 185, 129), ForeColor = Color.White, Font = new Font("Consolas", 10f, FontStyle.Bold), Cursor = Cursors.Hand };
            btnHmiPortToggle2.Click += (s, e) => {
                if (!isHmiKebOpen2)
                {
                    EnsureHmiKebOpen2();
                }
                else
                {
                    if (CheckConfirmDisconnectWithLock("B載台"))
                    {
                        CloseHmiKebPort2();
                    }
                }
            };

            lblHmiKebStatus2 = new Label() { Text = "狀態: 未連線", Location = new Point(368, 8), AutoSize = true, ForeColor = Color.Gray, Font = new Font("微軟正黑體", 9f) };
            pnlConn2.Controls.AddRange(new Control[] { l2_1, cmbHmiKebPort2, cmbHmiKebBaud2, l2_n, numHmiKebNode2, btnHmiPortToggle2, lblHmiKebStatus2 });
            pnlLeft2.Controls.Add(pnlConn2, 0, 0);

            TableLayoutPanel pnlModes2 = new TableLayoutPanel() { Dock = DockStyle.Fill, ColumnCount = 4, RowCount = 1, Margin = new Padding(0, 1, 0, 1) };
            for (int i = 0; i < 4; i++) pnlModes2.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25f));
            bHmiM2_1 = new Button() { Text = "數位轉速\n(半自動)", Dock = DockStyle.Fill, Margin = new Padding(1), Font = new Font("微軟正黑體", 9f), Cursor = Cursors.Hand };
            bHmiM2_1.Click += (s, e) => ApplyHmiKebInterlock(2, 7);
            bHmiM2_2 = new Button() { Text = "數位轉矩\n(半自動)", Dock = DockStyle.Fill, Margin = new Padding(1), Font = new Font("微軟正黑體", 9f, FontStyle.Bold), Cursor = Cursors.Hand };
            bHmiM2_2.Click += (s, e) => ApplyHmiKebInterlock(2, 8);
            bHmiM2_3 = new Button() { Text = "數位轉速\n(全自動)", Dock = DockStyle.Fill, Margin = new Padding(1), Font = new Font("微軟正黑體", 9f, FontStyle.Bold), Cursor = Cursors.Hand };
            bHmiM2_3.Click += (s, e) => ApplyHmiKebInterlock(2, 9);
            bHmiM2_4 = new Button() { Text = "數位轉矩\n(全自動)", Dock = DockStyle.Fill, Margin = new Padding(1), Font = new Font("微軟正黑體", 9f, FontStyle.Bold), Cursor = Cursors.Hand };
            bHmiM2_4.Click += (s, e) => ApplyHmiKebInterlock(2, 10);
            pnlModes2.Controls.AddRange(new Control[] { bHmiM2_1, bHmiM2_2, bHmiM2_3, bHmiM2_4 });
            pnlLeft2.Controls.Add(pnlModes2, 0, 1);

            Label lSpd2 = new Label() { Text = "轉速:", Font = new Font("微軟正黑體", 9f, FontStyle.Bold), ForeColor = Color.FromArgb(15, 23, 42), AutoSize = true, Margin = new Padding(2, 2, 0, 0) };
            pnlLeft2.Controls.Add(lSpd2, 0, 2);

            TableLayoutPanel pnlSpd2 = new TableLayoutPanel() { Dock = DockStyle.Fill, ColumnCount = 6, RowCount = 1, Margin = new Padding(0, 1, 0, 1) };
            pnlSpd2.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 13f)); // -100
            pnlSpd2.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 12f)); // -1
            pnlSpd2.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25f)); // numeric
            pnlSpd2.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 12f)); // +1
            pnlSpd2.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 13f)); // +100
            pnlSpd2.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25f)); // 寫入按鈕

            Button bSD100_2 = new Button() { Text = "-100", Dock = DockStyle.Fill, Margin = new Padding(1), Font = new Font("微軟正黑體", 9f) };
            bSD100_2.Click += (s, e) => { if (numHmiKebSpeed2.Value >= 100) numHmiKebSpeed2.Value -= 100; };
            Button bSD1_2 = new Button() { Text = "-1", Dock = DockStyle.Fill, Margin = new Padding(1), Font = new Font("微軟正黑體", 9f) };
            bSD1_2.Click += (s, e) => {
                if (numHmiKebSpeed2.Value >= 1)
                {
                    numHmiKebSpeed2.Value -= 1;
                    if (!isHmiKebOpen2) return;
                    int speedRpm = (int)numHmiKebSpeed2.Value;
                    int comIdx = GetHmiKebComIdx(2), baudIdx = GetHmiKebBaudIdx(2), node = (int)numHmiKebNode2.Value;
                    KebWriteParam32(comIdx, baudIdx, node, 0x0034, speedRpm, string.Format("B載台微調速度 -1 rpm -> {0} rpm (Sy52)", speedRpm));
                    WriteHmiLog("USER_OP", string.Format("[B載台] 微調速度 -1 rpm: {0} rpm (Sy.52=0x0034) | 當前模式: {1}", speedRpm, GetKebModeName(currentKebMode2)));
                    UpdateHmiKebModeParamsDisplay(2);
                }
            };

            numHmiKebSpeed2 = CreateNumericUpDown(new Point(0, 0), 76, 0, 6000, 0);
            numHmiKebSpeed2.Dock = DockStyle.Fill;
            numHmiKebSpeed2.Font = new Font("Consolas", 14f, FontStyle.Bold);
            numHmiKebSpeed2.TextAlign = HorizontalAlignment.Center;
            numHmiKebSpeed2.ValueChanged += (s, e) => UpdateHmiKebModeParamsDisplay(2);

            Button bSI1_2 = new Button() { Text = "+1", Dock = DockStyle.Fill, Margin = new Padding(1), Font = new Font("微軟正黑體", 9f) };
            bSI1_2.Click += (s, e) => {
                if (numHmiKebSpeed2.Value <= 5999)
                {
                    numHmiKebSpeed2.Value += 1;
                    if (!isHmiKebOpen2) return;
                    int speedRpm = (int)numHmiKebSpeed2.Value;
                    int comIdx = GetHmiKebComIdx(2), baudIdx = GetHmiKebBaudIdx(2), node = (int)numHmiKebNode2.Value;
                    KebWriteParam32(comIdx, baudIdx, node, 0x0034, speedRpm, string.Format("B載台微調速度 +1 rpm -> {0} rpm (Sy52)", speedRpm));
                    WriteHmiLog("USER_OP", string.Format("[B載台] 微調速度 +1 rpm: {0} rpm (Sy.52=0x0034) | 當前模式: {1}", speedRpm, GetKebModeName(currentKebMode2)));
                    UpdateHmiKebModeParamsDisplay(2);
                }
            };
            Button bSI100_2 = new Button() { Text = "+100", Dock = DockStyle.Fill, Margin = new Padding(1), Font = new Font("微軟正黑體", 9f) };
            bSI100_2.Click += (s, e) => { if (numHmiKebSpeed2.Value <= 5900) numHmiKebSpeed2.Value += 100; };

            Button btnSetSpd2 = new Button() { Text = "寫入速度", Dock = DockStyle.Fill, Margin = new Padding(1), BackColor = Color.FromArgb(0, 180, 216), ForeColor = Color.White, Font = new Font("微軟正黑體", 10f, FontStyle.Bold), Cursor = Cursors.Hand };
            btnSetSpd2.Click += (s, e) => {
                if (!isHmiKebOpen2) { MessageBox.Show("B台 (COM2) 未連線！請先點擊 [Open]。", "B台未連線", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
                int comIdx = GetHmiKebComIdx(2);
                int baudIdx = GetHmiKebBaudIdx(2);
                int node = (int)numHmiKebNode2.Value;
                int speedRpm = (int)numHmiKebSpeed2.Value;
                KebWriteParam32(comIdx, baudIdx, node, 0x0034, speedRpm, string.Format("B載台速度設定 Sy.52={0} rpm", speedRpm));
                WriteHmiLog("USER_OP", string.Format("[B載台] 使用者寫入速度目標: {0} rpm (Sy.52=0x0034) | 當前模式: {1}", speedRpm, GetKebModeName(currentKebMode2)));
                UpdateHmiKebModeParamsDisplay(2);
            };
            pnlSpd2.Controls.Add(bSD100_2, 0, 0);
            pnlSpd2.Controls.Add(bSD1_2, 1, 0);
            pnlSpd2.Controls.Add(numHmiKebSpeed2, 2, 0);
            pnlSpd2.Controls.Add(bSI1_2, 3, 0);
            pnlSpd2.Controls.Add(bSI100_2, 4, 0);
            pnlSpd2.Controls.Add(btnSetSpd2, 5, 0);
            pnlLeft2.Controls.Add(pnlSpd2, 0, 3);

            Panel pnlTrqHeader2 = new Panel() { Dock = DockStyle.Fill, Height = 20, Margin = new Padding(0) };
            Label lTrq2 = new Label() { Text = "轉矩 (cs.18):", Dock = DockStyle.Left, AutoSize = true, Font = new Font("微軟正黑體", 9f, FontStyle.Bold), ForeColor = Color.FromArgb(15, 23, 42) };
            lblCs19Disp2 = new Label() { Text = "cs.19基準: -- Nm (待讀取)", Dock = DockStyle.Right, AutoSize = true, Font = new Font("Consolas", 8.5f, FontStyle.Bold), ForeColor = Color.FromArgb(140, 150, 165) };
            ToolTip ttCs19_2 = new ToolTip();
            ttCs19_2.SetToolTip(lblCs19Disp2, "連線後將自驅動器讀取 cs.19 (0x0F13)\n0.1% 單步物理極限 x = cs19 / 1000");
            pnlTrqHeader2.Controls.Add(lblCs19Disp2);
            pnlTrqHeader2.Controls.Add(lTrq2);
            pnlLeft2.Controls.Add(pnlTrqHeader2, 0, 4);

            TableLayoutPanel pnlTrq2 = new TableLayoutPanel() { Dock = DockStyle.Fill, ColumnCount = 6, RowCount = 1, Margin = new Padding(0, 1, 0, 1) };
            pnlTrq2.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 13f)); // -1%
            pnlTrq2.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 12f)); // -0.1
            pnlTrq2.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25f)); // numeric
            pnlTrq2.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 12f)); // +0.1
            pnlTrq2.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 13f)); // +1%
            pnlTrq2.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25f)); // 寫入按鈕 (與上方模式按鈕 25% 完美對齊)

            Button bTD1_2 = new Button() { Text = "-1%", Dock = DockStyle.Fill, Margin = new Padding(1), Font = new Font("微軟正黑體", 9f) };
            bTD1_2.Click += (s, e) => {
                if (numHmiKebTorque2.Value >= 1m)
                {
                    numHmiKebTorque2.Value -= 1m;
                    if (!isHmiKebOpen2) return;
                    double trqPct = (double)numHmiKebTorque2.Value;
                    int rawTrq = (int)Math.Round(trqPct * 10); // KEB cs.18 單位 0.1%/LSB: 1.0% = RAW 10
                    KebWriteParam32(GetHmiKebComIdx(2), GetHmiKebBaudIdx(2), (int)numHmiKebNode2.Value, 0x0F12, rawTrq, string.Format("B載台微調轉矩 -1% -> {0:F1}%", trqPct));
                    WriteHmiLog("USER_OP", string.Format("[B載台] 微調轉矩 -1% (直接生效): {0:F1} % (RAW={1}, cs.18)", trqPct, rawTrq));
                    UpdateHmiKebModeParamsDisplay(2);
                }
            };
            Button bTD01_2 = new Button() { Text = "-0.1", Dock = DockStyle.Fill, Margin = new Padding(1), Font = new Font("微軟正黑體", 9f) };
            bTD01_2.Click += (s, e) => {
                if (numHmiKebTorque2.Value >= 0.1m)
                {
                    numHmiKebTorque2.Value -= 0.1m;
                    if (!isHmiKebOpen2) return;
                    double trqPct = (double)numHmiKebTorque2.Value;
                    int rawTrq = (int)Math.Round(trqPct * 10); // KEB cs.18 單位 0.1%/LSB: 1.0% = RAW 10
                    KebWriteParam32(GetHmiKebComIdx(2), GetHmiKebBaudIdx(2), (int)numHmiKebNode2.Value, 0x0F12, rawTrq, string.Format("B載台微調轉矩 -0.1% -> {0:F1}%", trqPct));
                    WriteHmiLog("USER_OP", string.Format("[B載台] 微調轉矩 -0.1% (直接生效): {0:F1} % (RAW={1}, cs.18)", trqPct, rawTrq));
                    UpdateHmiKebModeParamsDisplay(2);
                }
            };

            numHmiKebTorque2 = CreateNumericUpDown(new Point(0, 0), 76, 0, 150, 0, 1, 0.1m);
            numHmiKebTorque2.Dock = DockStyle.Fill;
            numHmiKebTorque2.Font = new Font("Consolas", 10.5f, FontStyle.Bold);
            numHmiKebTorque2.TextAlign = HorizontalAlignment.Center;
            numHmiKebTorque2.ValueChanged += (s, e) => UpdateHmiKebModeParamsDisplay(2);

            Button bTI01_2 = new Button() { Text = "+0.1", Dock = DockStyle.Fill, Margin = new Padding(1), Font = new Font("微軟正黑體", 9f) };
            bTI01_2.Click += (s, e) => {
                if (numHmiKebTorque2.Value <= 149.9m)
                {
                    numHmiKebTorque2.Value += 0.1m;
                    if (!isHmiKebOpen2) return;
                    double trqPct = (double)numHmiKebTorque2.Value;
                    int rawTrq = (int)Math.Round(trqPct * 10); // KEB cs.18 單位 0.1%/LSB: 1.0% = RAW 10
                    KebWriteParam32(GetHmiKebComIdx(2), GetHmiKebBaudIdx(2), (int)numHmiKebNode2.Value, 0x0F12, rawTrq, string.Format("B載台微調轉矩 +0.1% -> {0:F1}%", trqPct));
                    WriteHmiLog("USER_OP", string.Format("[B載台] 微調轉矩 +0.1% (直接生效): {0:F1} % (RAW={1}, cs.18)", trqPct, rawTrq));
                    UpdateHmiKebModeParamsDisplay(2);
                }
            };
            Button bTI1_2 = new Button() { Text = "+1%", Dock = DockStyle.Fill, Margin = new Padding(1), Font = new Font("微軟正黑體", 9f) };
            bTI1_2.Click += (s, e) => {
                if (numHmiKebTorque2.Value <= 149m)
                {
                    numHmiKebTorque2.Value += 1m;
                    if (!isHmiKebOpen2) return;
                    double trqPct = (double)numHmiKebTorque2.Value;
                    int rawTrq = (int)Math.Round(trqPct * 10); // KEB cs.18 單位 0.1%/LSB: 1.0% = RAW 10
                    KebWriteParam32(GetHmiKebComIdx(2), GetHmiKebBaudIdx(2), (int)numHmiKebNode2.Value, 0x0F12, rawTrq, string.Format("B載台微調轉矩 +1% -> {0:F1}%", trqPct));
                    WriteHmiLog("USER_OP", string.Format("[B載台] 微調轉矩 +1% (直接生效): {0:F1} % (RAW={1}, cs.18)", trqPct, rawTrq));
                    UpdateHmiKebModeParamsDisplay(2);
                }
            };

            Button btnSetTrq2 = new Button() { Text = "寫入轉矩", Dock = DockStyle.Fill, Margin = new Padding(1), BackColor = Color.FromArgb(245, 158, 11), ForeColor = Color.White, Font = new Font("微軟正黑體", 10f, FontStyle.Bold), Cursor = Cursors.Hand };
            btnSetTrq2.Click += (s, e) => {
                if (!isHmiKebOpen2) { MessageBox.Show("B台 (COM2) 未連線！請先點擊 [Open]。", "B台未連線", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
                int comIdx = GetHmiKebComIdx(2);
                int baudIdx = GetHmiKebBaudIdx(2);
                double trqPct = (double)numHmiKebTorque2.Value;
                int rawTrq = (int)Math.Round(trqPct * 10); // KEB cs.18 單位 0.1%/LSB: 1.0% = RAW 10
                KebWriteParam32(comIdx, baudIdx, (int)numHmiKebNode2.Value, 0x0F12, rawTrq, string.Format("B載台轉矩設定 cs.18={0:F1} % (RAW={1})", trqPct, rawTrq));
                WriteHmiLog("USER_OP", string.Format("[B載台] 使用者寫入轉矩目標: {0:F1} % (RAW={1}, cs.18=0x0F12)", trqPct, rawTrq));
                UpdateHmiKebModeParamsDisplay(2);
            };
            pnlTrq2.Controls.Add(bTD1_2, 0, 0);
            pnlTrq2.Controls.Add(bTD01_2, 1, 0);
            pnlTrq2.Controls.Add(numHmiKebTorque2, 2, 0);
            pnlTrq2.Controls.Add(bTI01_2, 3, 0);
            pnlTrq2.Controls.Add(bTI1_2, 4, 0);
            pnlTrq2.Controls.Add(btnSetTrq2, 5, 0);
            pnlLeft2.Controls.Add(pnlTrq2, 0, 5);

            pnlRun2 = new TableLayoutPanel() { Dock = DockStyle.Fill, ColumnCount = 4, RowCount = 1, Margin = new Padding(0, 1, 0, 1) };
            pnlRun2.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 28f));
            pnlRun2.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 28f));
            pnlRun2.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 22f));
            pnlRun2.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 22f));
            btnRF2 = new Button() { Text = "正轉 (RUN)", Dock = DockStyle.Fill, Margin = new Padding(1), BackColor = Color.FromArgb(16, 185, 129), ForeColor = Color.White, Font = new Font("微軟正黑體", 9.5f, FontStyle.Bold), Cursor = Cursors.Hand };
            btnRF2.Click += (s, e) => {
                // RUN 按鈕：若斷線直接提示，不阻塞 UI 主執行緒
                if (!isHmiKebOpen2) { MessageBox.Show("B台 (COM2) 未連線！請先點擊 [Open] 建立連線。", "B台未連線", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
                string opName = (currentKebMode2 == 8 || currentKebMode2 == 10) ? "B載台加載端啟用加載 (LOAD)" : "B載台待測端正轉";
                int targetSpd = (numHmiKebSpeed2 != null) ? (int)numHmiKebSpeed2.Value : 0;
                double targetTrq = (numHmiKebTorque2 != null) ? (double)numHmiKebTorque2.Value : 0.0;
                int comIdx = GetHmiKebComIdx(2);
                int baudIdx = GetHmiKebBaudIdx(2);
                int nodeId = (int)numHmiKebNode2.Value;

                // 運轉前全自動狀態自檢與診斷紀錄
                int? curRu00 = KebReadParamWithDll(comIdx, baudIdx, nodeId, 0x0200);
                WriteHmiLog("RUN_DIAG", string.Format("[B載台 RUN前自檢] 模式={0} | 給定轉速={1} rpm | 給定轉矩={2:F2} Nm | 變頻器當前狀態={3}",
                    GetKebModeName(currentKebMode2), targetSpd, targetTrq, curRu00.HasValue ? DecodeKebRu00(curRu00.Value) : "未讀取到"));

                if (currentKebMode2 == 7 || currentKebMode2 == 9)
                {
                    if (targetSpd <= 0)
                    {
                        WriteHmiLog("WARN", "【⚠️ B載台目標轉速為 0 rpm】目前設定轉速為 0，變頻器將進入零速伺服鎖定而不旋轉！若需轉動請先調整轉速 (>0 rpm)。");
                    }
                    // 速度模式下確保轉矩限制 cs.18 不為 0 (預設 100.0% = 1000)
                    KebWriteParamWithDll(comIdx, baudIdx, nodeId, 0x0F12, 1000);
                }
                else if (currentKebMode2 == 8 || currentKebMode2 == 10)
                {
                    // 轉矩加載模式下：啟用加載時強制將畫面轉矩設定值寫入 cs.18
                    int rawTrq = (int)Math.Round(targetTrq * 10);
                    KebWriteParam32(comIdx, baudIdx, nodeId, 0x0F12, rawTrq, string.Format("B載台加載端啟用加載 cs.18={0:F1}%", targetTrq));
                    WriteHmiLog("USER_OP", string.Format("[B載台] 啟用加載寫入轉矩目標: {0:F1} % (RAW={1}, cs.18=0x0F12)", targetTrq, rawTrq));
                }

                if (curRu00.HasValue && curRu00.Value == 0)
                {
                    WriteHmiLog("WARN", "【⚠️ B載台硬體 ST 斷開中】變頻器目前顯示 nOP，硬體功率級未放行！請閉合機櫃實體 ST 端子開關。");
                }
                int? faultCode2 = KebReadParamWithDll(comIdx, baudIdx, nodeId, 0x022B);
                if (faultCode2.HasValue && faultCode2.Value != 0)
                {
                    WriteHmiLog("ALARM", string.Format("【🚨 B載台處於故障鎖定中】狀態={0}, 故障碼 ru.43=0x{1:X2} ({2})，請排除故障後點擊 [復歸] 按鈕！", curRu00.HasValue ? DecodeKebRu00(curRu00.Value) : "--", faultCode2.Value, DecodeKebFaultCode(faultCode2.Value)));
                }

                if (targetSpd >= 0)
                {
                    KebWriteParam32(comIdx, baudIdx, nodeId, 0x0034, targetSpd, "B載台速度給定 Sy.52");
                }
                SetHmiKebCommand(comIdx, baudIdx, nodeId, 4, opName); // 正轉 Sy50=4 (Bit2=1 RUN, Bit3=0 FOR)
            };
            btnRR2 = new Button() { Text = "反轉 (RUN)", Dock = DockStyle.Fill, Margin = new Padding(1), BackColor = Color.FromArgb(139, 92, 246), ForeColor = Color.White, Font = new Font("微軟正黑體", 9.5f, FontStyle.Bold), Cursor = Cursors.Hand };
            btnRR2.Click += (s, e) => {
                if (!isHmiKebOpen2) { MessageBox.Show("B台 (COM2) 未連線！請先點擊 [Open] 建立連線。", "B台未連線", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
                int targetSpd = (numHmiKebSpeed2 != null) ? (int)numHmiKebSpeed2.Value : 0;
                double targetTrq = (numHmiKebTorque2 != null) ? (double)numHmiKebTorque2.Value : 0.0;
                int comIdx = GetHmiKebComIdx(2);
                int baudIdx = GetHmiKebBaudIdx(2);
                int nodeId = (int)numHmiKebNode2.Value;

                int? curRu00 = KebReadParamWithDll(comIdx, baudIdx, nodeId, 0x0200);
                WriteHmiLog("RUN_DIAG", string.Format("[B載台 反轉RUN前自檢] 模式={0} | 給定轉速={1} rpm | 變頻器當前狀態={2}",
                    GetKebModeName(currentKebMode2), targetSpd, curRu00.HasValue ? DecodeKebRu00(curRu00.Value) : "未讀取到"));

                if (currentKebMode2 == 7 || currentKebMode2 == 9)
                {
                    KebWriteParamWithDll(comIdx, baudIdx, nodeId, 0x0F12, 1000);
                }
                else if (currentKebMode2 == 8 || currentKebMode2 == 10)
                {
                    int rawTrq = (int)Math.Round(targetTrq * 10);
                    KebWriteParam32(comIdx, baudIdx, nodeId, 0x0F12, rawTrq, string.Format("B載台反向加載 cs.18={0:F1}%", targetTrq));
                    WriteHmiLog("USER_OP", string.Format("[B載台] 反向加載寫入轉矩目標: {0:F1} % (RAW={1}, cs.18=0x0F12)", targetTrq, rawTrq));
                }

                if (targetSpd >= 0)
                {
                    KebWriteParam32(comIdx, baudIdx, nodeId, 0x0034, targetSpd, "B載台速度給定 Sy.52");
                }
                SetHmiKebCommand(comIdx, baudIdx, nodeId, 12, "B載台待測端反轉"); // 反轉 Sy50=12 (Bit2=1 RUN, Bit3=1 REV)
            };
            btnStop2 = new Button() { Text = "🛑 停機", Dock = DockStyle.Fill, Margin = new Padding(1), BackColor = Color.FromArgb(239, 68, 68), ForeColor = Color.White, Font = new Font("微軟正黑體", 9.5f, FontStyle.Bold), Cursor = Cursors.Hand };
            btnStop2.Click += (s, e) => {
                // 停機為安全指令，若已連線直接送，若斷線記警告
                bool isFullAuto = (currentKebMode1 == 9 || currentKebMode1 == 10 || currentKebMode2 == 9 || currentKebMode2 == 10);
                if (isFullAuto)
                {
                    ExecuteFullAutoGracefulStop("B載台點擊停機按鈕");
                }
                else if (isHmiKebOpen2)
                {
                    if (currentKebMode2 == 8 || currentKebMode2 == 10)
                    {
                        KebWriteParam32(GetHmiKebComIdx(2), GetHmiKebBaudIdx(2), (int)numHmiKebNode2.Value, 0x0F12, 0, "B載台卸載歸零 cs.18=0");
                    }
                    string opName = (currentKebMode2 == 8 || currentKebMode2 == 10) ? "B載台卸載停機" : "B載台停機";
                    SetHmiKebCommand(GetHmiKebComIdx(2), GetHmiKebBaudIdx(2), (int)numHmiKebNode2.Value, 0, opName);
                }
                else
                {
                    WriteHmiLog("WARN", "[B台停機] B台 COM2 已斷線，停機命令無法送達！");
                    if (txtHmiKebLog != null) txtHmiKebLog.AppendText("[警告] B台 (COM2) 斷線，停機指令無法送達，請手動切斷電源！\r\n");
                }
            };
            btnReset2 = new Button() { Text = "🔄 復歸", Dock = DockStyle.Fill, Margin = new Padding(1), BackColor = Color.FromArgb(245, 158, 11), ForeColor = Color.White, Font = new Font("微軟正黑體", 9.5f, FontStyle.Bold), Cursor = Cursors.Hand };
            btnReset2.Click += (s, e) => {
                if (!isHmiKebOpen2) { MessageBox.Show("B台 (COM2) 未連線！請先點擊 [Open] 建立連線。", "B台未連線", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
                SetHmiKebCommand(GetHmiKebComIdx(2), GetHmiKebBaudIdx(2), (int)numHmiKebNode2.Value, 2, "B載台故障復歸 (FAULT RESET)");
                WriteHmiLog("USER_OP", "[B載台] 使用者點擊故障復歸 (Sy50=2)");
            };
            pnlRun2.Controls.AddRange(new Control[] { btnRF2, btnRR2, btnStop2, btnReset2 });
            pnlLeft2.Controls.Add(pnlRun2, 0, 6);

            // 系統判斷可自訂調整高度容器
            Panel pnlParamsBox2 = new Panel()
            {
                Dock = DockStyle.Fill,
                Margin = new Padding(2),
                BackColor = Color.FromArgb(238, 245, 255),
                BorderStyle = BorderStyle.FixedSingle,
                Padding = new Padding(6, 4, 6, 4)
            };
            Label lTitleP2 = new Label()
            {
                Text = "控制架構與系統判斷:",
                Dock = DockStyle.Top,
                Height = 18,
                Font = new Font("微軟正黑體", 9f, FontStyle.Bold),
                ForeColor = Color.FromArgb(30, 64, 175)
            };
            lblKebModeParams2 = new Label()
            {
                Text = "🔍 系統判斷：【未連線 / 待開啟 COM 埠】",
                Dock = DockStyle.Fill,
                Font = new Font("微軟正黑體", 9.5f, FontStyle.Bold),
                ForeColor = Color.FromArgb(15, 23, 42),
                TextAlign = ContentAlignment.MiddleLeft
            };
            pnlParamsBox2.Controls.Add(lblKebModeParams2);
            pnlParamsBox2.Controls.Add(lTitleP2);

            splitParam2 = new SplitContainer()
            {
                Dock = DockStyle.Fill,
                Orientation = Orientation.Horizontal,
                SplitterWidth = 6,
                BackColor = Color.FromArgb(215, 225, 235),
                Margin = new Padding(0)
            };
            splitParam2.Panel1.BackColor = Color.FromArgb(240, 243, 246);
            splitParam2.Panel2.BackColor = Color.FromArgb(240, 243, 246);
            splitParam2.Panel1.Controls.Add(pnlLeft2);
            splitParam2.Panel2.Controls.Add(pnlParamsBox2);
            splitParam2.SplitterDistance = 245; // 預設高度
            splitParam2.SplitterMoved += (s, e) => SaveLayoutConfig();

            Control pnlRuContainer2 = CreateKebRuPanelWithToolbar(2, "B載台數值", out dgvKebRu2);

            splitDrive2 = new SplitContainer()
            {
                Dock = DockStyle.Fill,
                Orientation = Orientation.Vertical,
                SplitterWidth = 6,
                BackColor = Color.FromArgb(215, 225, 235),
                Margin = new Padding(0)
            };
            splitDrive2.Panel1.BackColor = Color.FromArgb(240, 243, 246);
            splitDrive2.Panel1.AutoScroll = true;
            splitDrive2.Panel2.BackColor = Color.FromArgb(240, 243, 246);
            splitDrive2.Panel2.AutoScroll = true;
            splitDrive2.Panel1.Controls.Add(splitParam2);
            splitDrive2.Panel2.Controls.Add(pnlRuContainer2);
            splitDrive2.SplitterDistance = 420;
            splitDrive2.SplitterMoved += (s, e) => SaveLayoutConfig();

            grpD2.Controls.Add(splitDrive2);
            splitDrives.Panel2.Controls.Add(grpD2);

            hmiSpdControls2 = new Control[] { bSD100_2, bSD1_2, numHmiKebSpeed2, bSI1_2, bSI100_2, btnSetSpd2 };
            hmiTrqControls2 = new Control[] { bTD1_2, bTD01_2, numHmiKebTorque2, bTI01_2, bTI1_2, btnSetTrq2 };

            listKebControls.AddRange(new Control[] {
                l1_1, cmbHmiKebPort1, cmbHmiKebBaud1, l1_n, numHmiKebNode1, btnHmiPortToggle1, lblHmiKebStatus1,
                bHmiM1_1, bHmiM1_2, bHmiM1_3, bHmiM1_4,
                lSpd1, bSD100_1, bSD1_1, numHmiKebSpeed1, bSI1_1, bSI100_1, btnSetSpd1,
                lTrq1, bTD1_1, bTD01_1, numHmiKebTorque1, bTI01_1, bTI1_1, btnSetTrq1,
                btnRF1, btnRR1,
                l2_1, cmbHmiKebPort2, cmbHmiKebBaud2, l2_n, numHmiKebNode2, btnHmiPortToggle2, lblHmiKebStatus2,
                bHmiM2_1, bHmiM2_2, bHmiM2_3, bHmiM2_4,
                lSpd2, bSD100_2, bSD1_2, numHmiKebSpeed2, bSI1_2, bSI100_2, btnSetSpd2,
                lTrq2, bTD1_2, bTD01_2, numHmiKebTorque2, bTI01_2, bTI1_2, btnSetTrq2,
                btnRF2, btnRR2
            });

            // 僅鎖定馬達實體驅動運轉動作，絕不鎖定整個載台 GroupBox、COM 連線與參數編輯工具列
            listKebSafetyLockControls.AddRange(new Control[] {
                btnRF1, btnRR1, btnSetSpd1, btnSetTrq1,
                btnRF2, btnRR2, btnSetSpd2, btnSetTrq2
            });

            splitDrives.SplitterMoved += (s, e) => SaveLayoutConfig();
            pnlKebSection.Controls.Add(splitDrives);

            // -------------------------------------------------------------
            // 下半部：橫河 WT333E 三相電氣量測表 + 馬達溫度即時監控與 1 秒趨勢圖 (左右分割)
            // -------------------------------------------------------------
            GroupBox grpGrid = new GroupBox() { Text = "橫河 WT333E 三相電氣遙測數據", Dock = DockStyle.Fill, Font = new Font("微軟正黑體", 9f, FontStyle.Bold) };
            dgvTelemetry = new DataGridView()
            {
                Dock = DockStyle.Fill,
                BackgroundColor = Color.White,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None,
                RowHeadersVisible = false,
                AllowUserToAddRows = false,
                AllowUserToResizeRows = true,       // 允許自由拉伸行高
                AllowUserToResizeColumns = true,    // 允許自由拉伸欄寬
                Font = new Font("微軟正黑體", 8.5f, FontStyle.Regular)
            };
            dgvTelemetry.Columns.Add("Param", "量測項目");
            dgvTelemetry.Columns.Add("U", "1 相 (Element 1)");
            dgvTelemetry.Columns.Add("V", "2 相 (Element 2)");
            dgvTelemetry.Columns.Add("W", "3 相 (Element 3)");
            dgvTelemetry.Columns.Add("Sigma", "Sigma 總合計 (Σ)");
            dgvTelemetry.Columns.Add("PF", "特性 / 特徵");

            dgvTelemetry.Columns[0].Width = 145;
            dgvTelemetry.Columns[1].Width = 100;
            dgvTelemetry.Columns[2].Width = 100;
            dgvTelemetry.Columns[3].Width = 100;
            dgvTelemetry.Columns[4].Width = 120;
            dgvTelemetry.Columns[5].Width = 100;
            dgvTelemetry.ColumnWidthChanged += (s, e) => SaveLayoutConfig();

            dgvTelemetry.Rows.Add("電壓 (Voltage U)", "0.00 V", "0.00 V", "0.00 V", "0.00 V", "3P3W");
            dgvTelemetry.Rows.Add("電流 (Current I)", "0.00 A", "0.00 A", "0.00 A", "0.00 A", "--");
            dgvTelemetry.Rows.Add("有功功率 (Active P)", "0.00 kW", "0.00 kW", "0.00 kW", "0.00 kW", "Active");
            dgvTelemetry.Rows.Add("視在功率 (Apparent S)", "0.00 kVA", "0.00 kVA", "0.00 kVA", "0.00 kVA", "Apparent");
            dgvTelemetry.Rows.Add("無功功率 (Reactive Q)", "0.00 kvar", "0.00 kvar", "0.00 kvar", "0.00 kvar", "Reactive");
            dgvTelemetry.Rows.Add("功率因數 (Power Factor)", "0.0000", "0.0000", "0.0000", "0.0000", "Lag (+)");
            dgvTelemetry.Rows.Add("相位角 (Phase Angle Φ)", "0.0°", "0.0°", "0.0°", "0.0°", "Phase");
            dgvTelemetry.Rows.Add("頻率 (Frequency f)", "0.00 Hz", "--", "--", "0.00 Hz", "Freq");
            grpGrid.Controls.Add(dgvTelemetry);

            // 馬達溫度即時監控與 1 秒趨勢圖面板
            GroupBox grpMotorTemp = new GroupBox()
            {
                Text = "馬達溫度即時監控",
                Dock = DockStyle.Fill,
                Font = new Font("微軟正黑體", 9f, FontStyle.Bold)
            };
            TableLayoutPanel tblTempInner = new TableLayoutPanel()
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 2,
                Margin = new Padding(0),
                Padding = new Padding(2)
            };
            tblTempInner.RowStyles.Add(new RowStyle(SizeType.Absolute, 32f)); // Top: Channel selection & Current Temp display
            tblTempInner.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));  // Bottom: 1-sec trend chart

            Panel pnlTempTop = new Panel() { Dock = DockStyle.Fill, Margin = new Padding(0) };

            Label lblTempTitle = new Label()
            {
                Text = "馬達溫度",
                Location = new Point(4, 6),
                AutoSize = true,
                Font = new Font("微軟正黑體", 9.5f, FontStyle.Bold),
                ForeColor = Color.FromArgb(15, 23, 42)
            };

            Label lblCh = new Label()
            {
                Text = "通道:",
                Location = new Point(74, 7),
                AutoSize = true,
                Font = new Font("微軟正黑體", 9f),
                ForeColor = Color.FromArgb(70, 80, 95)
            };

            cmbMotorTempCh = new ComboBox()
            {
                Location = new Point(110, 4),
                Width = 75,
                DropDownStyle = ComboBoxStyle.DropDownList,
                Font = new Font("微軟正黑體", 9f, FontStyle.Bold),
                BackColor = Color.White
            };
            for (int i = 1; i <= 20; i++) cmbMotorTempCh.Items.Add("CH " + i);
            cmbMotorTempCh.SelectedIndex = 0;
            cmbMotorTempCh.SelectedIndexChanged += (s, e) => {
                if (motorTempChart != null)
                {
                    int ch = cmbMotorTempCh.SelectedIndex;
                    motorTempChart.ChannelIndex = ch;
                    string cName = (gl820ChannelNames != null && ch >= 0 && ch < gl820ChannelNames.Length && !string.IsNullOrEmpty(gl820ChannelNames[ch]))
                        ? gl820ChannelNames[ch] : ("CH" + (ch + 1));
                    motorTempChart.ChannelName = cName;
                    motorTempChart.Invalidate();
                }
            };

            lblMotorTempDisplay = new Label()
            {
                Text = "設備未連線",
                Location = new Point(190, 4),
                Size = new Size(120, 24),
                Font = new Font("微軟正黑體", 10.5f, FontStyle.Bold),
                ForeColor = Color.FromArgb(239, 68, 68),
                TextAlign = ContentAlignment.MiddleLeft
            };

            pnlTempTop.Controls.AddRange(new Control[] { lblTempTitle, lblCh, cmbMotorTempCh, lblMotorTempDisplay });
            tblTempInner.Controls.Add(pnlTempTop, 0, 0);

            motorTempChart = new MotorTempTrendControl()
            {
                Dock = DockStyle.Fill,
                Font = new Font("微軟正黑體", 8.5f)
            };
            tblTempInner.Controls.Add(motorTempChart, 0, 1);
            grpMotorTemp.Controls.Add(tblTempInner);

            // 下半部左右水平分割 SplitContainer
            splitBottomHorizontal = new SplitContainer()
            {
                Dock = DockStyle.Fill,
                Orientation = Orientation.Vertical,
                SplitterWidth = 6,
                BackColor = Color.FromArgb(215, 225, 235),
                Margin = new Padding(0)
            };
            splitBottomHorizontal.Panel1.BackColor = Color.FromArgb(240, 243, 246);
            splitBottomHorizontal.Panel1.AutoScroll = true;
            splitBottomHorizontal.Panel2.BackColor = Color.FromArgb(240, 243, 246);
            splitBottomHorizontal.Panel2.AutoScroll = true;

            splitBottomHorizontal.Panel1.Controls.Add(grpGrid);
            SetupBottomRightWorkbench(grpMotorTemp);
            splitBottomHorizontal.Panel2.Controls.Add(pnlWorkbench);
            splitBottomHorizontal.SplitterDistance = 750;

            // -------------------------------------------------------------
            // 中間可上下自由拖曳調整高度的分割 Bar (SplitContainer)
            // -------------------------------------------------------------
            splitMainVertical = new SplitContainer()
            {
                Dock = DockStyle.Fill,
                Orientation = Orientation.Horizontal,
                SplitterWidth = 6,
                BackColor = Color.FromArgb(215, 225, 235), // 細緻水平分割調整線
                Margin = new Padding(0)
            };
            splitMainVertical.Panel1.BackColor = Color.FromArgb(240, 243, 246);
            splitMainVertical.Panel1.AutoScroll = true;
            splitMainVertical.Panel2.BackColor = Color.FromArgb(240, 243, 246);
            splitMainVertical.Panel2.AutoScroll = true;

            splitMainVertical.Panel1.Controls.Add(pnlKebSection);
            splitMainVertical.Panel2.Controls.Add(splitBottomHorizontal);
            splitMainVertical.SplitterDistance = 330;
            splitMainVertical.Panel1MinSize = 120;
            splitMainVertical.Panel2MinSize = 120;
            splitMainVertical.SplitterMoved += (s, e) => SaveLayoutConfig();
            splitBottomHorizontal.SplitterMoved += (s, e) => SaveLayoutConfig();

            tableManual.Controls.Add(splitMainVertical, 0, 1);

            // -------------------------------------------------------------
            // Row 2: 設備即時連線燈號狀態列 (Device Status Pills Strip) - 位於最新日誌上方
            // -------------------------------------------------------------
            // -------------------------------------------------------------
            // Row 2: 設備即時連線燈號狀態列 (Device Status Pills Strip) - 位於最新日誌上方
            // -------------------------------------------------------------
            TableLayoutPanel pnlDeviceStatusBar = new TableLayoutPanel()
            {
                Dock = DockStyle.Fill,
                ColumnCount = 3,
                RowCount = 1,
                BackColor = Color.FromArgb(15, 23, 42),
                Margin = new Padding(0, 2, 0, 0),
                Padding = new Padding(4, 2, 4, 2)
            };
            pnlDeviceStatusBar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            pnlDeviceStatusBar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            pnlDeviceStatusBar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

            FlowLayoutPanel flpPills = new FlowLayoutPanel() { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, AutoSize = true };
            lblPillTorque = CreateStatusPill("扭力計: " + torquePortName + " [斷線]", Point.Empty);
            lblPillPowerMeter = CreateStatusPill("WT333E: " + powerMeterPort + " [斷線]", Point.Empty);
            lblPillGbd = CreateStatusPill("GL820: " + gbdPort + " [斷線]", Point.Empty);
            lblPillKeb1 = CreateStatusPill("A載台: [斷線]", Point.Empty);
            lblPillKeb2 = CreateStatusPill("B載台: [斷線]", Point.Empty);
            lblSystemHealth = CreateStatusPill("資源: GDI -- | RAM --", Point.Empty);
            lblSystemHealth.ForeColor = Color.FromArgb(56, 189, 248);
            flpPills.Controls.AddRange(new Control[] { lblPillTorque, lblPillPowerMeter, lblPillGbd, lblPillKeb1, lblPillKeb2, lblSystemHealth });

            lblSafetyStatus = new Label()
            {
                Text = "[安全互鎖] 扭力計或 WT333E 未連線 - 載台強制鎖定",
                ForeColor = Color.FromArgb(239, 68, 68),
                Font = new Font("微軟正黑體", 9.5f, FontStyle.Bold),
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleCenter
            };

            FlowLayoutPanel flpRightActions = new FlowLayoutPanel()
            {
                Dock = DockStyle.Right,
                AutoSize = true,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                Margin = new Padding(0),
                Padding = new Padding(0)
            };

            btnRawConfig = new Button() { Text = " 錄製設定", Size = new Size(85, 28), Margin = new Padding(2, 0, 4, 0), BackColor = Color.FromArgb(79, 70, 229), ForeColor = Color.White, Font = new Font("微軟正黑體", 8.5f, FontStyle.Bold), Cursor = Cursors.Hand };
            btnRawConfig.Click += (s, e) => {
                using (var dlg = new RawDataConfigDialog(this))
                {
                    dlg.ShowDialog(this);
                }
            };

            // 截圖按鈕（相機 ICON，GDI+ 向量，完美相容 XP/Win7/10/11）- 放置於緊急停機按鈕左側
            btnSnapshotRaw = new Button()
            {
                Text = " 截圖",
                Image = CreateCameraIconImage(18, 18, Color.White),
                ImageAlign = ContentAlignment.MiddleLeft,
                TextAlign = ContentAlignment.MiddleRight,
                TextImageRelation = TextImageRelation.ImageBeforeText,
                Size = new Size(80, 28),
                Margin = new Padding(2, 0, 6, 0),
                BackColor = Color.FromArgb(245, 158, 11),
                ForeColor = Color.White,
                Font = new Font("微軟正黑體", 9f, FontStyle.Bold),
                Cursor = Cursors.Hand
            };
            btnSnapshotRaw.Click += (s, e) => CaptureScreenshot();

            Button btnGlobalEstop = new Button()
            {
                Text = "🚨 緊急停機 (E-STOP / ESC)",
                Size = new Size(210, 28),
                Margin = new Padding(2, 0, 0, 0),
                BackColor = Color.FromArgb(220, 38, 38),
                ForeColor = Color.White,
                Font = new Font("微軟正黑體", 9.5f, FontStyle.Bold),
                Cursor = Cursors.Hand
            };
            btnGlobalEstop.Click += (s, e) => TriggerGlobalEmergencyStop();

            flpRightActions.Controls.Add(btnRawConfig);
            flpRightActions.Controls.Add(btnSnapshotRaw);
            flpRightActions.Controls.Add(btnGlobalEstop);

            pnlDeviceStatusBar.Controls.Add(flpPills, 0, 0);
            pnlDeviceStatusBar.Controls.Add(lblSafetyStatus, 1, 0);
            pnlDeviceStatusBar.Controls.Add(flpRightActions, 2, 0);
            tableManual.Controls.Add(pnlDeviceStatusBar, 0, 2);

            tab.Controls.Add(tableManual);

            RefreshHmiKebPorts();
            InitHmiKebModeStyles();
        }

        private void InitHmiKebModeStyles()
        {
            currentKebMode1 = 7;
            currentKebMode2 = 8;
            SetHmiModeBtnStyle(bHmiM1_1, true, Color.FromArgb(16, 185, 129));
            SetHmiModeBtnStyle(bHmiM1_2, false, Color.FromArgb(0, 120, 215));
            SetHmiModeBtnStyle(bHmiM1_3, false, Color.FromArgb(217, 119, 6));
            SetHmiModeBtnStyle(bHmiM1_4, false, Color.FromArgb(139, 92, 246));

            SetHmiModeBtnStyle(bHmiM2_1, false, Color.FromArgb(16, 185, 129));
            SetHmiModeBtnStyle(bHmiM2_2, true, Color.FromArgb(0, 120, 215));
            SetHmiModeBtnStyle(bHmiM2_3, false, Color.FromArgb(217, 119, 6));
            SetHmiModeBtnStyle(bHmiM2_4, false, Color.FromArgb(139, 92, 246));

            UpdateHmiKebModeParamsDisplay(1, 7, 0, 100.0, 0);
            UpdateHmiKebModeParamsDisplay(2, 8, 0, 0.0, 0);
        }

        // =========================================================================
        //  右下角多功能工作台 (Multi-View Workbench) - 曲線/溫度/TN/S2S6/效率地圖
        // =========================================================================
        private void SetupBottomRightWorkbench(GroupBox origGrpMotorTemp)
        {
            pnlWorkbench = new Panel()
            {
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(240, 243, 246),
                Padding = new Padding(0)
            };

            // 頂部視圖切換標籤列 (高度 32px，高雅 5 分割 TableLayoutPanel)
            TableLayoutPanel tblTabs = new TableLayoutPanel()
            {
                Dock = DockStyle.Top,
                Height = 32,
                ColumnCount = 5,
                RowCount = 1,
                BackColor = Color.FromArgb(226, 232, 240),
                Margin = new Padding(0),
                Padding = new Padding(1)
            };
            for (int i = 0; i < 5; i++) tblTabs.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 20f));

            btnWorkbenchTabs = new Button[5];
            string[] tabTitles = new string[] { "動態曲線", "馬達溫度", "TN設定", "S2/S6", "效率圖" };
            for (int i = 0; i < 5; i++)
            {
                int idx = i;
                btnWorkbenchTabs[i] = new Button()
                {
                    Text = tabTitles[i],
                    Dock = DockStyle.Fill,
                    FlatStyle = FlatStyle.Flat,
                    Font = new Font("微軟正黑體", 8.5f, FontStyle.Bold),
                    Margin = new Padding(1),
                    Cursor = Cursors.Hand
                };
                btnWorkbenchTabs[i].FlatAppearance.BorderSize = 0;
                btnWorkbenchTabs[i].Click += (s, e) => SwitchWorkbenchView(idx);
                tblTabs.Controls.Add(btnWorkbenchTabs[i], i, 0);
            }

            // 中間主要內容容器
            pnlWorkbenchContent = new Panel()
            {
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(240, 243, 246),
                Padding = new Padding(2)
            };

            pnlWorkbenchViews = new Control[5];

            // -------------------------------------------------------------
            // View 0: 轉矩與轉速雙軸即時響應曲線 (Torque & Speed Curve)
            // -------------------------------------------------------------
            Panel pnlView0 = new Panel() { Dock = DockStyle.Fill, BackColor = Color.White };
            FlowLayoutPanel pnlV0Top = new FlowLayoutPanel() { Dock = DockStyle.Top, Height = 30, BackColor = Color.FromArgb(248, 250, 252), Padding = new Padding(2, 2, 2, 2), WrapContents = false };
            
            Label lblV0Win = new Label() { Text = "時窗:", AutoSize = true, Margin = new Padding(2, 6, 0, 0), Font = new Font("微軟正黑體", 8.5f), ForeColor = Color.FromArgb(70, 80, 95) };
            cmbTrqSpdTimeWindow = new ComboBox() { Width = 95, DropDownStyle = ComboBoxStyle.DropDownList, Font = new Font("微軟正黑體", 8.5f), Margin = new Padding(1, 3, 4, 0) };
            cmbTrqSpdTimeWindow.Items.AddRange(new object[] { "15秒(短距)", "30秒(標準)", "60秒(長程)" });
            cmbTrqSpdTimeWindow.SelectedIndex = 1;
            cmbTrqSpdTimeWindow.SelectedIndexChanged += (s, e) => {
                if (trqSpdChart != null)
                {
                    if (cmbTrqSpdTimeWindow.SelectedIndex == 0) trqSpdChart.MaxPoints = 75;
                    else if (cmbTrqSpdTimeWindow.SelectedIndex == 1) trqSpdChart.MaxPoints = 150;
                    else trqSpdChart.MaxPoints = 300;
                }
            };

            Label lblV0Scale = new Label() { Text = "刻度:", AutoSize = true, Margin = new Padding(4, 6, 0, 0), Font = new Font("微軟正黑體", 8.5f), ForeColor = Color.FromArgb(70, 80, 95) };
            ComboBox cmbTrqSpdScale = new ComboBox() { Width = 115, DropDownStyle = ComboBoxStyle.DropDownList, Font = new Font("微軟正黑體", 8.5f), Margin = new Padding(1, 3, 4, 0) };
            cmbTrqSpdScale.Items.AddRange(new object[] { "🌟 自動適應", "微量 (10Nm/500rpm)", "小量 (30Nm/1000rpm)", "標準 (60Nm/2000rpm)", "大量 (150Nm/3500rpm)", "✏️ 自訂上限..." });
            cmbTrqSpdScale.SelectedIndex = 0;
            cmbTrqSpdScale.SelectedIndexChanged += (s, e) => {
                if (trqSpdChart != null)
                {
                    if (cmbTrqSpdScale.SelectedIndex == 5)
                    {
                        Form dlg = new Form() { Text = "自訂動態曲線刻度上限", Width = 280, Height = 175, StartPosition = FormStartPosition.CenterParent, FormBorderStyle = FormBorderStyle.FixedDialog, MaximizeBox = false, MinimizeBox = false };
                        Label lt = new Label() { Text = "轉矩上限 (Nm):", Location = new Point(15, 18), AutoSize = true, Font = new Font("微軟正黑體", 9f) };
                        NumericUpDown nt = new NumericUpDown() { Location = new Point(130, 15), Width = 110, Minimum = 1, Maximum = 1000, Value = (decimal)trqSpdChart.CustomMaxTrq, DecimalPlaces = 1, Font = new Font("微軟正黑體", 9f) };
                        Label ls = new Label() { Text = "轉速上限 (rpm):", Location = new Point(15, 53), AutoSize = true, Font = new Font("微軟正黑體", 9f) };
                        NumericUpDown ns = new NumericUpDown() { Location = new Point(130, 50), Width = 110, Minimum = 50, Maximum = 10000, Value = (decimal)trqSpdChart.CustomMaxSpd, Font = new Font("微軟正黑體", 9f) };
                        Button btnOk = new Button() { Text = "確定", DialogResult = DialogResult.OK, Location = new Point(155, 92), Width = 85, Height = 28, BackColor = Color.FromArgb(0, 180, 216), ForeColor = Color.White, Font = new Font("微軟正黑體", 9f, FontStyle.Bold) };
                        dlg.Controls.AddRange(new Control[] { lt, nt, ls, ns, btnOk });
                        dlg.AcceptButton = btnOk;
                        if (dlg.ShowDialog(this) == DialogResult.OK)
                        {
                            trqSpdChart.CustomMaxTrq = (double)nt.Value;
                            trqSpdChart.CustomMaxSpd = (double)ns.Value;
                            trqSpdChart.ScaleMode = 5;
                        }
                    }
                    else
                    {
                        trqSpdChart.ScaleMode = cmbTrqSpdScale.SelectedIndex;
                    }
                    trqSpdChart.Invalidate();
                }
            };

            CheckBox chkAlignSpeed = new CheckBox() { Text = "🔄同向", Checked = true, AutoSize = true, Margin = new Padding(2, 6, 2, 0), Font = new Font("微軟正黑體", 8.5f), ForeColor = Color.FromArgb(70, 80, 95) };
            ToolTip ttAlign = new ToolTip();
            ttAlign.SetToolTip(chkAlignSpeed, "勾選時：自動將實測轉速與目標轉速同向對齊重疊（方便比對跟隨響應）；取消時：顯示原始物理正負符號");
            chkAlignSpeed.CheckedChanged += (s, e) => {
                if (trqSpdChart != null)
                {
                    trqSpdChart.AlignSpeedSign = chkAlignSpeed.Checked;
                    trqSpdChart.Invalidate();
                }
            };

            btnPauseTrqSpdChart = new Button() { Text = "⏸️ 暫停", Size = new Size(58, 24), FlatStyle = FlatStyle.Flat, Font = new Font("微軟正黑體", 8f), BackColor = Color.FromArgb(241, 245, 249), Margin = new Padding(2, 2, 2, 0) };
            btnPauseTrqSpdChart.Click += (s, e) => {
                if (trqSpdChart != null)
                {
                    trqSpdChart.IsPaused = !trqSpdChart.IsPaused;
                    btnPauseTrqSpdChart.Text = trqSpdChart.IsPaused ? "▶️ 繼續" : "⏸️ 暫停";
                    btnPauseTrqSpdChart.BackColor = trqSpdChart.IsPaused ? Color.FromArgb(254, 243, 199) : Color.FromArgb(241, 245, 249);
                }
            };

            btnClearTrqSpdChart = new Button() { Text = "🗑️ 清空", Size = new Size(56, 24), FlatStyle = FlatStyle.Flat, Font = new Font("微軟正黑體", 8f), BackColor = Color.FromArgb(241, 245, 249), Margin = new Padding(2, 2, 2, 0) };
            btnClearTrqSpdChart.Click += (s, e) => { if (trqSpdChart != null) trqSpdChart.ClearData(); };

            pnlV0Top.Controls.AddRange(new Control[] { lblV0Win, cmbTrqSpdTimeWindow, lblV0Scale, cmbTrqSpdScale, chkAlignSpeed, btnPauseTrqSpdChart, btnClearTrqSpdChart });
            trqSpdChart = new TorqueSpeedTrendControl() { Dock = DockStyle.Fill };
            pnlView0.Controls.Add(trqSpdChart);
            pnlView0.Controls.Add(pnlV0Top);
            pnlWorkbenchViews[0] = pnlView0;

            // -------------------------------------------------------------
            // View 1: 原馬達溫度即時監控 (100% 原汁原味)
            // -------------------------------------------------------------
            origGrpMotorTemp.Dock = DockStyle.Fill;
            pnlWorkbenchViews[1] = origGrpMotorTemp;

            // -------------------------------------------------------------
            // View 2: TN 特性測試條件設定面板 (小視窗完整版，與大分頁 100% 雙向即時連動)
            // -------------------------------------------------------------
            Panel pnlView2 = new Panel() { Dock = DockStyle.Fill, BackColor = Color.White, AutoScroll = true, Padding = new Padding(4) };
            GroupBox grpTnMini = new GroupBox() { Text = "⚡ T-N 轉矩轉速特性測試條件配置", Dock = DockStyle.Fill, Font = new Font("微軟正黑體", 9f, FontStyle.Bold), ForeColor = Color.FromArgb(15, 23, 42) };
            TableLayoutPanel tblTnMini = new TableLayoutPanel() { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 10, Padding = new Padding(2), AutoScroll = true };
            tblTnMini.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90f));
            tblTnMini.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            tblTnMini.RowStyles.Add(new RowStyle(SizeType.Absolute, 26f));
            tblTnMini.RowStyles.Add(new RowStyle(SizeType.Absolute, 26f));
            tblTnMini.RowStyles.Add(new RowStyle(SizeType.Absolute, 26f));
            tblTnMini.RowStyles.Add(new RowStyle(SizeType.Absolute, 26f));
            tblTnMini.RowStyles.Add(new RowStyle(SizeType.Absolute, 26f));
            tblTnMini.RowStyles.Add(new RowStyle(SizeType.Absolute, 26f));
            tblTnMini.RowStyles.Add(new RowStyle(SizeType.Absolute, 28f));
            tblTnMini.RowStyles.Add(new RowStyle(SizeType.Absolute, 26f));
            tblTnMini.RowStyles.Add(new RowStyle(SizeType.Absolute, 32f));
            tblTnMini.RowStyles.Add(new RowStyle(SizeType.Absolute, 26f));

            // Row 0: 測試配置 (A待測/B加載 vs B待測/A加載)
            Label lTnRole = new Label() { Text = "測試配置:", Anchor = AnchorStyles.Right, AutoSize = true, Font = new Font("微軟正黑體", 8.5f) };
            cmbTnRoleMini = new ComboBox() { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill, Font = new Font("微軟正黑體", 8.5f) };
            cmbTnRoleMini.Items.AddRange(new object[] { "A載台待測(速度) / B載台加載", "B載台待測(速度) / A載台加載" });
            cmbTnRoleMini.SelectedIndex = (cmbTnRole != null && cmbTnRole.SelectedIndex >= 0) ? cmbTnRole.SelectedIndex : 1; // 預設：B載台待測 / A載台加載
            cmbTnRoleMini.SelectedIndexChanged += (s, e) => {
                if (!isSyncingTnControls && cmbTnRole != null && cmbTnRole.SelectedIndex != cmbTnRoleMini.SelectedIndex)
                {
                    isSyncingTnControls = true;
                    cmbTnRole.SelectedIndex = cmbTnRoleMini.SelectedIndex;
                    isSyncingTnControls = false;
                }
            };

            // Row 1: 起始轉速 (預設 50 rpm)
            Label lTn1 = new Label() { Text = "起始(rpm):", Anchor = AnchorStyles.Right, AutoSize = true, Font = new Font("微軟正黑體", 8.5f) };
            numTnMiniStart = new NumericUpDown() { Minimum = 0, Maximum = 4000, Value = (numTnStartRpm != null && numTnStartRpm.Value > 0 ? numTnStartRpm.Value : 50), Dock = DockStyle.Fill, Font = new Font("微軟正黑體", 8.5f) };
            numTnMiniStart.ValueChanged += (s, e) => {
                if (!isSyncingTnControls && numTnStartRpm != null && numTnStartRpm.Value != numTnMiniStart.Value)
                {
                    isSyncingTnControls = true;
                    numTnStartRpm.Value = numTnMiniStart.Value;
                    isSyncingTnControls = false;
                }
            };

            // Row 2: 步階轉速
            Label lTn2 = new Label() { Text = "步階(rpm):", Anchor = AnchorStyles.Right, AutoSize = true, Font = new Font("微軟正黑體", 8.5f) };
            numTnMiniStep = new NumericUpDown() { Minimum = 1, Maximum = 1000, Value = (numTnStepRpm != null ? numTnStepRpm.Value : 50), Dock = DockStyle.Fill, Font = new Font("微軟正黑體", 8.5f) };
            numTnMiniStep.ValueChanged += (s, e) => {
                if (!isSyncingTnControls && numTnStepRpm != null && numTnStepRpm.Value != numTnMiniStep.Value)
                {
                    isSyncingTnControls = true;
                    numTnStepRpm.Value = numTnMiniStep.Value;
                    isSyncingTnControls = false;
                }
            };

            // Row 3: 結束轉速
            Label lTn3 = new Label() { Text = "結束(rpm):", Anchor = AnchorStyles.Right, AutoSize = true, Font = new Font("微軟正黑體", 8.5f) };
            numTnMiniEnd = new NumericUpDown() { Minimum = 0, Maximum = 4000, Value = (numTnEndRpm != null ? numTnEndRpm.Value : 300), Dock = DockStyle.Fill, Font = new Font("微軟正黑體", 8.5f) };
            numTnMiniEnd.ValueChanged += (s, e) => {
                if (!isSyncingTnControls && numTnEndRpm != null && numTnEndRpm.Value != numTnMiniEnd.Value)
                {
                    isSyncingTnControls = true;
                    numTnEndRpm.Value = numTnMiniEnd.Value;
                    isSyncingTnControls = false;
                }
            };

            // Row 4: 固定轉矩
            Label lTn4 = new Label() { Text = "目標轉矩(Nm):", Anchor = AnchorStyles.Right, AutoSize = true, Font = new Font("微軟正黑體", 8.5f) };
            numTnMiniTrq = new NumericUpDown() { Minimum = 0, Maximum = 500, Value = (numTnTorque != null ? numTnTorque.Value : 15), DecimalPlaces = 1, Dock = DockStyle.Fill, Font = new Font("微軟正黑體", 8.5f) };
            numTnMiniTrq.ValueChanged += (s, e) => {
                if (!isSyncingTnControls && numTnTorque != null && numTnTorque.Value != numTnMiniTrq.Value)
                {
                    isSyncingTnControls = true;
                    numTnTorque.Value = numTnMiniTrq.Value;
                    isSyncingTnControls = false;
                }
            };

            // Row 5: 穩定時間
            Label lTn5 = new Label() { Text = "穩定時間(秒):", Anchor = AnchorStyles.Right, AutoSize = true, Font = new Font("微軟正黑體", 8.5f) };
            numTnMiniDwell = new NumericUpDown() { Minimum = 1, Maximum = 3600, Value = (numTnDwell != null ? numTnDwell.Value : 16), Dock = DockStyle.Fill, Font = new Font("微軟正黑體", 8.5f) };
            numTnMiniDwell.ValueChanged += (s, e) => {
                if (!isSyncingTnControls && numTnDwell != null && numTnDwell.Value != numTnMiniDwell.Value)
                {
                    isSyncingTnControls = true;
                    numTnDwell.Value = numTnMiniDwell.Value;
                    isSyncingTnControls = false;
                }
            };

            // Row 6: 手動定錨加載基準點控制
            btnTnMiniAnchor = new Button() { Text = "📍 鎖定定錨基準", Dock = DockStyle.Fill, Height = 26, BackColor = Color.FromArgb(139, 92, 246), ForeColor = Color.White, Font = new Font("微軟正黑體", 8f, FontStyle.Bold) };
            btnTnMiniAnchor.Click += (s, e) => {
                int role = (cmbTnRoleMini != null && cmbTnRoleMini.SelectedIndex >= 0) ? cmbTnRoleMini.SelectedIndex : 1;
                int trqDriveId = (role == 1) ? 1 : 2; // B待測則A加載(1)，A待測則B加載(2)
                double curPct = (trqDriveId == 1 && numHmiKebTorque1 != null) ? (double)numHmiKebTorque1.Value : (numHmiKebTorque2 != null ? (double)numHmiKebTorque2.Value : 0.0);
                tnAdaptedTorquePct = Math.Max(0.0, curPct);
                tnHasAnchor = true;
                UpdateTnAnchorStatusText();
                if (txtHmiKebLog != null) txtHmiKebLog.AppendText(string.Format("[TN定錨] 已記憶負載基準點：{0:F1}% ({1:F2} Nm)\r\n", tnAdaptedTorquePct, actTorque));
            };
            lblTnMiniAnchorStatus = new Label() { Text = "定錨: 未設定 (從0%起步)", Anchor = AnchorStyles.Left, AutoSize = true, ForeColor = Color.FromArgb(100, 116, 139), Font = new Font("微軟正黑體", 8f) };

            // Row 7: 測試標記 (RAW DATA 記錄標記，大小視窗即時雙向鏡像)
            Label lTnMiniTag = new Label() { Text = "測試標記:", Anchor = AnchorStyles.Right, AutoSize = true, Font = new Font("微軟正黑體", 8.5f) };
            txtTnMiniTag = new TextBox() { Text = (txtTnTag != null && !string.IsNullOrEmpty(txtTnTag.Text)) ? txtTnTag.Text : "TN", Dock = DockStyle.Fill, Font = new Font("微軟正黑體", 8.5f) };
            txtTnMiniTag.TextChanged += (s, e) => {
                if (!isSyncingTnControls && txtTnTag != null && txtTnTag.Text != txtTnMiniTag.Text)
                {
                    isSyncingTnControls = true;
                    txtTnTag.Text = txtTnMiniTag.Text;
                    isSyncingTnControls = false;
                }
            };

            // Row 8: 操作按鈕
            FlowLayoutPanel pnlTnMiniBtns = new FlowLayoutPanel() { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, Margin = new Padding(0) };
            btnTnMiniStart = new Button() { Text = "▶️ 啟動測試", Size = new Size(82, 28), BackColor = Color.FromArgb(0, 180, 216), ForeColor = Color.White, Font = new Font("微軟正黑體", 8.5f, FontStyle.Bold) };
            btnTnMiniStart.Click += (s, e) => { BtnStartTn_Click(s, e); };
            btnTnMiniStop = new Button() { Text = "⏹️ 停止", Size = new Size(60, 28), BackColor = Color.FromArgb(239, 68, 68), ForeColor = Color.White, Font = new Font("微軟正黑體", 8.5f, FontStyle.Bold), Enabled = false };
            btnTnMiniStop.Click += (s, e) => { StopTnTest(); };
            Button btnTnMiniViewTab = new Button() { Text = "🔍 查看大圖", Size = new Size(82, 28), BackColor = Color.FromArgb(100, 116, 139), ForeColor = Color.White, Font = new Font("微軟正黑體", 8.5f) };
            btnTnMiniViewTab.Click += (s, e) => { if (tabControl != null && tabControl.TabPages.Count > 1) tabControl.SelectedIndex = 1; };
            pnlTnMiniBtns.Controls.AddRange(new Control[] { btnTnMiniStart, btnTnMiniStop, btnTnMiniViewTab });

            // Row 9: 狀態與倒數
            FlowLayoutPanel pnlTnMiniProgress = new FlowLayoutPanel() { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, Margin = new Padding(0) };
            lblTnMiniStatus = new Label() { Text = "待命準備中", AutoSize = true, Font = new Font("微軟正黑體", 8f, FontStyle.Bold) };
            lblTnMiniCountdown = new Label() { Text = "-- s", AutoSize = true, ForeColor = Color.DarkOrange, Font = new Font("微軟正黑體", 8.5f, FontStyle.Bold), Margin = new Padding(6, 0, 0, 0) };
            prgTnMini = new ProgressBar() { Size = new Size(95, 16), Margin = new Padding(6, 2, 0, 0) };
            pnlTnMiniProgress.Controls.AddRange(new Control[] { lblTnMiniStatus, lblTnMiniCountdown, prgTnMini });

            tblTnMini.Controls.Add(lTnRole, 0, 0); tblTnMini.Controls.Add(cmbTnRoleMini, 1, 0);
            tblTnMini.Controls.Add(lTn1, 0, 1); tblTnMini.Controls.Add(numTnMiniStart, 1, 1);
            tblTnMini.Controls.Add(lTn2, 0, 2); tblTnMini.Controls.Add(numTnMiniStep, 1, 2);
            tblTnMini.Controls.Add(lTn3, 0, 3); tblTnMini.Controls.Add(numTnMiniEnd, 1, 3);
            tblTnMini.Controls.Add(lTn4, 0, 4); tblTnMini.Controls.Add(numTnMiniTrq, 1, 4);
            tblTnMini.Controls.Add(lTn5, 0, 5); tblTnMini.Controls.Add(numTnMiniDwell, 1, 5);
            tblTnMini.Controls.Add(btnTnMiniAnchor, 0, 6); tblTnMini.Controls.Add(lblTnMiniAnchorStatus, 1, 6);
            tblTnMini.Controls.Add(lTnMiniTag, 0, 7); tblTnMini.Controls.Add(txtTnMiniTag, 1, 7);
            tblTnMini.Controls.Add(new Label() { Text = "控制:", Anchor = AnchorStyles.Right, AutoSize = true, Font = new Font("微軟正黑體", 8.5f) }, 0, 8);
            tblTnMini.Controls.Add(pnlTnMiniBtns, 1, 8);
            tblTnMini.Controls.Add(new Label() { Text = "進度:", Anchor = AnchorStyles.Right, AutoSize = true, Font = new Font("微軟正黑體", 8.5f) }, 0, 9);
            tblTnMini.Controls.Add(pnlTnMiniProgress, 1, 9);

            grpTnMini.Controls.Add(tblTnMini);
            pnlView2.Controls.Add(grpTnMini);
            pnlWorkbenchViews[2] = pnlView2;

            // -------------------------------------------------------------
            // View 3: 工作制試驗條件設定面板 (含 S6 向量圖解與雙重定錨，全連動)
            // -------------------------------------------------------------
            Panel pnlView3 = new Panel() { Dock = DockStyle.Fill, BackColor = Color.White, AutoScroll = true, Padding = new Padding(4) };
            GroupBox grpDutyMini = new GroupBox() { Text = "⏱️ 工作制試驗配置 (S1/S2 連續 / S6 週期)", Dock = DockStyle.Fill, Font = new Font("微軟正黑體", 9f, FontStyle.Bold), ForeColor = Color.FromArgb(15, 23, 42) };
            tblDutyMini = new TableLayoutPanel() { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 8, Padding = new Padding(2), AutoScroll = true };
            tblDutyMini.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90f));
            tblDutyMini.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            tblDutyMini.RowStyles.Add(new RowStyle(SizeType.Absolute, 28f)); // Row 0: 模式/載台
            tblDutyMini.RowStyles.Add(new RowStyle(SizeType.Absolute, 28f)); // Row 1: 轉速/轉矩
            tblDutyMini.RowStyles.Add(new RowStyle(SizeType.Absolute, 0f));  // Row 2: S2 錨點 (預設 S6 時為 0f)
            tblDutyMini.RowStyles.Add(new RowStyle(SizeType.Absolute, 28f)); // Row 3: S6 週期T/ED%
            tblDutyMini.RowStyles.Add(new RowStyle(SizeType.Absolute, 56f)); // Row 4: S6 雙重定錨 (含數據框與按鈕)
            tblDutyMini.RowStyles.Add(new RowStyle(SizeType.Absolute, 65f)); // Row 5: S6 週期圖解
            tblDutyMini.RowStyles.Add(new RowStyle(SizeType.Absolute, 32f)); // Row 6: 操作按鈕
            tblDutyMini.RowStyles.Add(new RowStyle(SizeType.Absolute, 26f)); // Row 7: 即時狀態與動作

            // Row 0: 工作制模式與待測端角色 (並排)
            Label lD1 = new Label() { Text = "模式/載台:", Anchor = AnchorStyles.Right, AutoSize = true, Font = new Font("微軟正黑體", 8.5f) };
            FlowLayoutPanel pnlDModeRole = new FlowLayoutPanel() { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, Margin = new Padding(0) };
            cmbDutyMiniMode = new ComboBox() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 115, Font = new Font("微軟正黑體", 8.5f) };
            cmbDutyMiniMode.Items.AddRange(new object[] { "S1 連續工作制", "S2 短時工作制", "S6 週期工作制" });
            cmbDutyMiniMode.SelectedIndex = (cmbDutyMode != null && cmbDutyMode.SelectedIndex >= 0) ? cmbDutyMode.SelectedIndex : 2; // 預設 S6
            cmbDutyMiniMode.SelectedIndexChanged += (s, e) => {
                if (!isSyncingDutyControls && cmbDutyMode != null && cmbDutyMode.SelectedIndex != cmbDutyMiniMode.SelectedIndex)
                {
                    isSyncingDutyControls = true;
                    cmbDutyMode.SelectedIndex = cmbDutyMiniMode.SelectedIndex;
                    isSyncingDutyControls = false;
                }
                UpdateDutyModeVisibility(cmbDutyMiniMode.SelectedIndex);
                if (pnlS6Diagram != null) pnlS6Diagram.Invalidate();
                if (pnlS6MiniDiagram != null) pnlS6MiniDiagram.Invalidate();
            };
            cmbDutyMiniRole = new ComboBox() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 135, Font = new Font("微軟正黑體", 8.5f) };
            cmbDutyMiniRole.Items.AddRange(new object[] { "A載台待測 / B加載", "B載台待測 / A加載" });
            cmbDutyMiniRole.SelectedIndex = (cmbDutyRole != null && cmbDutyRole.SelectedIndex >= 0) ? cmbDutyRole.SelectedIndex : 1; // 預設：B載台待測 / A載台加載
            cmbDutyMiniRole.SelectedIndexChanged += (s, e) => {
                if (!isSyncingDutyControls && cmbDutyRole != null && cmbDutyRole.SelectedIndex != cmbDutyMiniRole.SelectedIndex)
                {
                    isSyncingDutyControls = true;
                    cmbDutyRole.SelectedIndex = cmbDutyMiniRole.SelectedIndex;
                    isSyncingDutyControls = false;
                }
            };
            pnlDModeRole.Controls.AddRange(new Control[] { cmbDutyMiniMode, cmbDutyMiniRole });

            // Row 1: 試驗轉速與加載轉矩
            Label lD2 = new Label() { Text = "轉速/轉矩:", Anchor = AnchorStyles.Right, AutoSize = true, Font = new Font("微軟正黑體", 8.5f) };
            FlowLayoutPanel pnlDSpdTrq = new FlowLayoutPanel() { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, Margin = new Padding(0) };
            numDutyMiniSpd = new NumericUpDown() { Minimum = 0, Maximum = 4000, Value = (numDutySpeed != null ? numDutySpeed.Value : 1000), Width = 70, Font = new Font("微軟正黑體", 8.5f) };
            numDutyMiniSpd.ValueChanged += (s, e) => {
                if (!isSyncingDutyControls)
                {
                    isSyncingDutyControls = true;
                    if (numDutySpeed != null && numDutySpeed.Value != numDutyMiniSpd.Value)
                        numDutySpeed.Value = numDutyMiniSpd.Value;
                    if (numCardTargetSpeed != null && numCardTargetSpeed.Value != numDutyMiniSpd.Value)
                        numCardTargetSpeed.Value = Math.Min(numCardTargetSpeed.Maximum, Math.Max(numCardTargetSpeed.Minimum, numDutyMiniSpd.Value));
                    isSyncingDutyControls = false;
                }
            };
            Label lDUnit1 = new Label() { Text = "rpm /", AutoSize = true, Margin = new Padding(0, 4, 0, 0), Font = new Font("微軟正黑體", 8f) };
            numDutyMiniTrq = new NumericUpDown() { Minimum = 0, Maximum = 500, Value = (numDutyTorque != null ? numDutyTorque.Value : 15), DecimalPlaces = 1, Width = 60, Font = new Font("微軟正黑體", 8.5f) };
            numDutyMiniTrq.ValueChanged += (s, e) => {
                if (!isSyncingDutyControls)
                {
                    isSyncingDutyControls = true;
                    if (numDutyTorque != null && numDutyTorque.Value != numDutyMiniTrq.Value)
                        numDutyTorque.Value = numDutyMiniTrq.Value;
                    if (numCardTargetTorque != null && numCardTargetTorque.Value != numDutyMiniTrq.Value)
                        numCardTargetTorque.Value = Math.Min(numCardTargetTorque.Maximum, Math.Max(numCardTargetTorque.Minimum, numDutyMiniTrq.Value));
                    isSyncingDutyControls = false;
                }
                if (pnlS6Diagram != null) pnlS6Diagram.Invalidate();
                if (pnlS6MiniDiagram != null) pnlS6MiniDiagram.Invalidate();
            };
            Label lDUnit2 = new Label() { Text = "Nm", AutoSize = true, Margin = new Padding(0, 4, 0, 0), Font = new Font("微軟正黑體", 8f) };
            pnlDSpdTrq.Controls.AddRange(new Control[] { numDutyMiniSpd, lDUnit1, numDutyMiniTrq, lDUnit2 });

            // Row 2: S2 錨點測試控制項 (SY52 / CS18 / 記錄當前 / 歸零重置 - S1/S6 隱藏)
            lblDutyMiniS2AnchorLabel = new Label() { Text = "S2錨點:", Anchor = AnchorStyles.Right, AutoSize = true, Font = new Font("微軟正黑體", 8.5f), ForeColor = Color.FromArgb(30, 64, 175) };
            pnlDutyMiniS2Anchors = new FlowLayoutPanel() { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, Margin = new Padding(0) };
            numDutyMiniS2Sy52 = new NumericUpDown() { Minimum = 0, Maximum = 6000, Value = s2AnchorSy52, Increment = 50, Width = 62, Font = new Font("微軟正黑體", 8.5f) };
            numDutyMiniS2Sy52.ValueChanged += (s, e) => {
                s2AnchorSy52 = (int)numDutyMiniS2Sy52.Value;
                if (!isSyncingDutyControls && numS2AnchorSy52 != null && numS2AnchorSy52.Value != numDutyMiniS2Sy52.Value)
                {
                    isSyncingDutyControls = true;
                    numS2AnchorSy52.Value = numDutyMiniS2Sy52.Value;
                    isSyncingDutyControls = false;
                }
            };
            Label lS2SyUnit = new Label() { Text = "rpm", AutoSize = true, Margin = new Padding(0, 4, 2, 0), Font = new Font("微軟正黑體", 7.5f) };
            numDutyMiniS2Cs18 = new NumericUpDown() { Minimum = 0, Maximum = 1000, Value = s2AnchorCs18, Increment = 10, Width = 52, Font = new Font("微軟正黑體", 8.5f) };
            numDutyMiniS2Cs18.ValueChanged += (s, e) => {
                s2AnchorCs18 = (int)numDutyMiniS2Cs18.Value;
                if (!isSyncingDutyControls && numS2AnchorCs18 != null && numS2AnchorCs18.Value != numDutyMiniS2Cs18.Value)
                {
                    isSyncingDutyControls = true;
                    numS2AnchorCs18.Value = numDutyMiniS2Cs18.Value;
                    isSyncingDutyControls = false;
                }
            };
            Label lS2CsUnit = new Label() { Text = "‰", AutoSize = true, Margin = new Padding(0, 4, 2, 0), Font = new Font("微軟正黑體", 7.5f) };
            btnDutyMiniS2RecordAnchor = new Button() { Text = "📍記錄", Size = new Size(52, 24), BackColor = Color.FromArgb(16, 185, 129), ForeColor = Color.White, Font = new Font("微軟正黑體", 7.5f, FontStyle.Bold) };
            btnDutyMiniS2RecordAnchor.Click += (s, e) => { CaptureCurrentAnchorToS2(); };
            btnDutyMiniS2ResetAnchor = new Button() { Text = "↺歸零", Size = new Size(48, 24), BackColor = Color.FromArgb(239, 68, 68), ForeColor = Color.White, Font = new Font("微軟正黑體", 7.5f, FontStyle.Bold) };
            btnDutyMiniS2ResetAnchor.Click += (s, e) => { ResetS2Anchor(); };
            pnlDutyMiniS2Anchors.Controls.AddRange(new Control[] { numDutyMiniS2Sy52, lS2SyUnit, numDutyMiniS2Cs18, lS2CsUnit, btnDutyMiniS2RecordAnchor, btnDutyMiniS2ResetAnchor });

            // Row 3: S6 週期T (分) / ED% / 循環數 (S1/S2 隱藏)
            Label lD3 = new Label() { Text = "週期T/ED%:", Anchor = AnchorStyles.Right, AutoSize = true, Font = new Font("微軟正黑體", 8.5f) };
            lblDutyMiniCycleLabel = lD3;
            FlowLayoutPanel pnlDCycle = new FlowLayoutPanel() { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, Margin = new Padding(0) };
            pnlDutyMiniCycle = pnlDCycle;
            numDutyMiniCycleMin = new NumericUpDown() { Minimum = 1, Maximum = 60, Value = (numS6CycleMin != null ? numS6CycleMin.Value : 10), DecimalPlaces = 1, Increment = 0.5m, Width = 55, Font = new Font("微軟正黑體", 8.5f) };
            numDutyMiniCycleMin.ValueChanged += (s, e) => {
                if (!isSyncingDutyControls && numS6CycleMin != null && numS6CycleMin.Value != numDutyMiniCycleMin.Value)
                {
                    isSyncingDutyControls = true;
                    numS6CycleMin.Value = numDutyMiniCycleMin.Value;
                    isSyncingDutyControls = false;
                }
                UpdateS6CalcInfo();
                if (pnlS6Diagram != null) pnlS6Diagram.Invalidate();
                if (pnlS6MiniDiagram != null) pnlS6MiniDiagram.Invalidate();
            };
            Label lDMin = new Label() { Text = "分", AutoSize = true, Margin = new Padding(0, 4, 2, 0), Font = new Font("微軟正黑體", 8f) };
            numDutyMiniEd = new NumericUpDown() { Minimum = 1, Maximum = 100, Value = (numS6Ed != null ? numS6Ed.Value : 40), Width = 48, Font = new Font("微軟正黑體", 8.5f) };
            numDutyMiniEd.ValueChanged += (s, e) => {
                if (!isSyncingDutyControls && numS6Ed != null && numS6Ed.Value != numDutyMiniEd.Value)
                {
                    isSyncingDutyControls = true;
                    numS6Ed.Value = numDutyMiniEd.Value;
                    isSyncingDutyControls = false;
                }
                UpdateS6CalcInfo();
                if (pnlS6Diagram != null) pnlS6Diagram.Invalidate();
                if (pnlS6MiniDiagram != null) pnlS6MiniDiagram.Invalidate();
            };
            Label lDEdPct = new Label() { Text = "%", AutoSize = true, Margin = new Padding(0, 4, 2, 0), Font = new Font("微軟正黑體", 8f) };
            Label lDCyc = new Label() { Text = "x", AutoSize = true, Margin = new Padding(2, 4, 0, 0), Font = new Font("微軟正黑體", 8f) };
            numDutyMiniCycles = new NumericUpDown() { Minimum = 1, Maximum = 50, Value = (numS6Cycles != null ? numS6Cycles.Value : 3), Width = 45, Font = new Font("微軟正黑體", 8.5f) };
            numDutyMiniCycles.ValueChanged += (s, e) => {
                if (!isSyncingDutyControls && numS6Cycles != null && numS6Cycles.Value != numDutyMiniCycles.Value)
                {
                    isSyncingDutyControls = true;
                    numS6Cycles.Value = numDutyMiniCycles.Value;
                    isSyncingDutyControls = false;
                }
            };
            Label lDCycUnit = new Label() { Text = "週", AutoSize = true, Margin = new Padding(0, 4, 0, 0), Font = new Font("微軟正黑體", 8f) };
            pnlDCycle.Controls.AddRange(new Control[] { numDutyMiniCycleMin, lDMin, numDutyMiniEd, lDEdPct, lDCyc, numDutyMiniCycles, lDCycUnit });

            // Row 4: S6 雙重定錨控制 (含數據顯示輸入框與按鈕，S1/S2 隱藏)
            Label lDAnchor = new Label() { Text = "S6定錨:", Anchor = AnchorStyles.Right, AutoSize = true, Font = new Font("微軟正黑體", 8.5f) };
            lblDutyMiniAnchorLabel = lDAnchor;
            FlowLayoutPanel pnlDAnchors = new FlowLayoutPanel() { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, Margin = new Padding(0) };
            pnlDutyMiniAnchors = pnlDAnchors;

            numDutyMiniS6NoLoadSpd = new NumericUpDown() { Minimum = 0, Maximum = 6000, Value = (decimal)s6AnchorNoLoadSpeed, Increment = 50, Width = 55, Font = new Font("微軟正黑體", 8f) };
            numDutyMiniS6NoLoadSpd.ValueChanged += (s, e) => {
                s6AnchorNoLoadSpeed = (double)numDutyMiniS6NoLoadSpd.Value;
                s6HasNoLoadAnchor = (s6AnchorNoLoadSpeed > 0);
                if (!isSyncingDutyControls && numS6AnchorNoLoadSpd != null && numS6AnchorNoLoadSpd.Value != numDutyMiniS6NoLoadSpd.Value)
                {
                    isSyncingDutyControls = true;
                    numS6AnchorNoLoadSpd.Value = numDutyMiniS6NoLoadSpd.Value;
                    isSyncingDutyControls = false;
                }
                UpdateS6AnchorStatusText();
            };
            btnS6MiniAnchorNoLoad = new Button() { Text = "📍記空載", Size = new Size(62, 24), BackColor = Color.FromArgb(14, 165, 233), ForeColor = Color.White, Font = new Font("微軟正黑體", 7.5f, FontStyle.Bold) };
            btnS6MiniAnchorNoLoad.Click += (s, e) => { CaptureCurrentAnchorToS6NoLoad(); };

            numDutyMiniS6LoadedSpd = new NumericUpDown() { Minimum = 0, Maximum = 6000, Value = (decimal)s6AnchorLoadedSpeed, Increment = 50, Width = 55, Font = new Font("微軟正黑體", 8f) };
            numDutyMiniS6LoadedSpd.ValueChanged += (s, e) => {
                s6AnchorLoadedSpeed = (double)numDutyMiniS6LoadedSpd.Value;
                s6HasLoadedAnchor = (s6AnchorLoadedSpeed > 0 && s6AnchorLoadedTorquePct > 0);
                if (!isSyncingDutyControls && numS6AnchorLoadedSpd != null && numS6AnchorLoadedSpd.Value != numDutyMiniS6LoadedSpd.Value)
                {
                    isSyncingDutyControls = true;
                    numS6AnchorLoadedSpd.Value = numDutyMiniS6LoadedSpd.Value;
                    isSyncingDutyControls = false;
                }
                UpdateS6AnchorStatusText();
            };
            numDutyMiniS6LoadedCs18 = new NumericUpDown() { Minimum = 0, Maximum = 1000, Value = (decimal)Math.Round(s6AnchorLoadedTorquePct * 10), Increment = 10, Width = 48, Font = new Font("微軟正黑體", 8f) };
            numDutyMiniS6LoadedCs18.ValueChanged += (s, e) => {
                s6AnchorLoadedTorquePct = (double)numDutyMiniS6LoadedCs18.Value / 10.0;
                s6HasLoadedAnchor = (s6AnchorLoadedSpeed > 0 && s6AnchorLoadedTorquePct > 0);
                if (!isSyncingDutyControls && numS6AnchorLoadedCs18 != null && numS6AnchorLoadedCs18.Value != numDutyMiniS6LoadedCs18.Value)
                {
                    isSyncingDutyControls = true;
                    numS6AnchorLoadedCs18.Value = numDutyMiniS6LoadedCs18.Value;
                    isSyncingDutyControls = false;
                }
                UpdateS6AnchorStatusText();
            };
            btnS6MiniAnchorLoaded = new Button() { Text = "📍記加載", Size = new Size(62, 24), BackColor = Color.FromArgb(139, 92, 246), ForeColor = Color.White, Font = new Font("微軟正黑體", 7.5f, FontStyle.Bold) };
            btnS6MiniAnchorLoaded.Click += (s, e) => { CaptureCurrentAnchorToS6Loaded(); };

            btnS6MiniResetAnchor = new Button() { Text = "↺歸零", Size = new Size(48, 24), BackColor = Color.FromArgb(239, 68, 68), ForeColor = Color.White, Font = new Font("微軟正黑體", 7.5f, FontStyle.Bold) };
            btnS6MiniResetAnchor.Click += (s, e) => { ResetS6Anchors(); };

            lblS6MiniAnchorStatus = new Label() { Text = "空載=未定錨 | 加載=未定錨", AutoSize = true, Margin = new Padding(2, 4, 0, 0), ForeColor = Color.FromArgb(100, 116, 139), Font = new Font("微軟正黑體", 7.5f) };
            pnlDAnchors.Controls.AddRange(new Control[] { numDutyMiniS6NoLoadSpd, btnS6MiniAnchorNoLoad, numDutyMiniS6LoadedSpd, numDutyMiniS6LoadedCs18, btnS6MiniAnchorLoaded, btnS6MiniResetAnchor, lblS6MiniAnchorStatus });

            // Row 5: 操作按鈕
            FlowLayoutPanel pnlDutyMiniBtns = new FlowLayoutPanel() { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, Margin = new Padding(0) };
            btnDutyMiniStart = new Button() { Text = "▶️ 開始試驗", Size = new Size(82, 28), BackColor = Color.FromArgb(0, 180, 216), ForeColor = Color.White, Font = new Font("微軟正黑體", 8.5f, FontStyle.Bold) };
            btnDutyMiniStart.Click += (s, e) => { BtnStartDuty_Click(s, e); };
            btnDutyMiniStop = new Button() { Text = "⏹️ 停止", Size = new Size(60, 28), BackColor = Color.FromArgb(239, 68, 68), ForeColor = Color.White, Font = new Font("微軟正黑體", 8.5f, FontStyle.Bold), Enabled = false };
            btnDutyMiniStop.Click += (s, e) => { StopDutyTest(); };
            Button btnDutyMiniViewTab = new Button() { Text = "🔍 查看大圖", Size = new Size(82, 28), BackColor = Color.FromArgb(100, 116, 139), ForeColor = Color.White, Font = new Font("微軟正黑體", 8.5f) };
            btnDutyMiniViewTab.Click += (s, e) => { if (tabControl != null && tabControl.TabPages.Count > 2) tabControl.SelectedIndex = 2; };
            pnlDutyMiniBtns.Controls.AddRange(new Control[] { btnDutyMiniStart, btnDutyMiniStop, btnDutyMiniViewTab });

            // Row 6: 即時狀態與動作
            FlowLayoutPanel pnlDutyMiniProgress = new FlowLayoutPanel() { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, Margin = new Padding(0) };
            lblDutyMiniStatus = new Label() { Text = "待命準備中", AutoSize = true, Font = new Font("微軟正黑體", 8f, FontStyle.Bold) };
            lblDutyMiniPhaseAction = new Label() { Text = "--", AutoSize = true, ForeColor = Color.FromArgb(2, 132, 199), Font = new Font("微軟正黑體", 8f, FontStyle.Bold), Margin = new Padding(6, 0, 0, 0) };
            prgDutyMini = new ProgressBar() { Size = new Size(85, 16), Margin = new Padding(6, 2, 0, 0) };
            pnlDutyMiniProgress.Controls.AddRange(new Control[] { lblDutyMiniStatus, lblDutyMiniPhaseAction, prgDutyMini });

            tblDutyMini.Controls.Add(lD1, 0, 0); tblDutyMini.Controls.Add(pnlDModeRole, 1, 0);
            tblDutyMini.Controls.Add(lD2, 0, 1); tblDutyMini.Controls.Add(pnlDSpdTrq, 1, 1);
            tblDutyMini.Controls.Add(lblDutyMiniS2AnchorLabel, 0, 2); tblDutyMini.Controls.Add(pnlDutyMiniS2Anchors, 1, 2);
            tblDutyMini.Controls.Add(lD3, 0, 3); tblDutyMini.Controls.Add(pnlDCycle, 1, 3);
            tblDutyMini.Controls.Add(lDAnchor, 0, 4); tblDutyMini.Controls.Add(pnlDAnchors, 1, 4);
            tblDutyMini.Controls.Add(new Label() { Text = "控制:", Anchor = AnchorStyles.Right, AutoSize = true, Font = new Font("微軟正黑體", 8.5f) }, 0, 5);
            tblDutyMini.Controls.Add(pnlDutyMiniBtns, 1, 5);
            tblDutyMini.Controls.Add(new Label() { Text = "進度:", Anchor = AnchorStyles.Right, AutoSize = true, Font = new Font("微軟正黑體", 8.5f) }, 0, 6);
            tblDutyMini.Controls.Add(pnlDutyMiniProgress, 1, 6);

            grpDutyMini.Controls.Add(tblDutyMini);
            pnlView3.Controls.Add(grpDutyMini);
            pnlWorkbenchViews[3] = pnlView3;

            // -------------------------------------------------------------
            // View 4: 效率地圖設定面板
            // -------------------------------------------------------------
            Panel pnlView4 = new Panel() { Dock = DockStyle.Fill, BackColor = Color.White, AutoScroll = true, Padding = new Padding(8) };
            GroupBox grpEffMini = new GroupBox() { Text = "🗺️ 效率地圖 (Efficiency Map) 矩陣配置", Dock = DockStyle.Fill, Font = new Font("微軟正黑體", 9f, FontStyle.Bold), ForeColor = Color.FromArgb(15, 23, 42) };
            TableLayoutPanel tblEffMini = new TableLayoutPanel() { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Padding = new Padding(6) };
            tblEffMini.RowStyles.Add(new RowStyle(SizeType.Absolute, 45f));
            tblEffMini.RowStyles.Add(new RowStyle(SizeType.Absolute, 40f));
            tblEffMini.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

            Label lblEffHint = new Label() { Text = "自動依據轉速與轉矩網格建立馬達/驅動器全域效率等高線圖。", Dock = DockStyle.Fill, Font = new Font("微軟正黑體", 8.5f), ForeColor = Color.FromArgb(70, 80, 95) };
            FlowLayoutPanel pnlEffBtns = new FlowLayoutPanel() { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, Margin = new Padding(0) };
            Button btnEffMiniGen = new Button() { Text = "⚡ 產生測試矩陣", Size = new Size(110, 28), BackColor = Color.FromArgb(0, 180, 216), ForeColor = Color.White, Font = new Font("微軟正黑體", 8.5f, FontStyle.Bold) };
            btnEffMiniGen.Click += (s, e) => { BtnGenEffMap_Click(s, e); };
            Button btnEffMiniViewTab = new Button() { Text = "🔍 檢視完整效率地圖", Size = new Size(130, 28), BackColor = Color.FromArgb(100, 116, 139), ForeColor = Color.White, Font = new Font("微軟正黑體", 8.5f) };
            btnEffMiniViewTab.Click += (s, e) => { if (tabControl != null && tabControl.TabPages.Count > 3) tabControl.SelectedIndex = 3; };
            pnlEffBtns.Controls.AddRange(new Control[] { btnEffMiniGen, btnEffMiniViewTab });

            tblEffMini.Controls.Add(lblEffHint, 0, 0);
            tblEffMini.Controls.Add(pnlEffBtns, 0, 1);
            grpEffMini.Controls.Add(tblEffMini);
            pnlView4.Controls.Add(grpEffMini);
            pnlWorkbenchViews[4] = pnlView4;

            // 將 5 個 View 加入 pnlWorkbenchContent
            for (int i = 0; i < 5; i++)
            {
                pnlWorkbenchContent.Controls.Add(pnlWorkbenchViews[i]);
            }

            pnlWorkbench.Controls.Add(pnlWorkbenchContent);
            pnlWorkbench.Controls.Add(tblTabs);

            SwitchWorkbenchView(0); // 預設顯示 View 0: 轉矩與轉速即時動態曲線
        }

        private void SwitchWorkbenchView(int viewIdx)
        {
            if (viewIdx < 0 || viewIdx >= 5) viewIdx = 0;
            activeWorkbenchViewIdx = viewIdx;

            for (int i = 0; i < 5; i++)
            {
                if (btnWorkbenchTabs != null && btnWorkbenchTabs[i] != null)
                {
                    bool isSel = (i == viewIdx);
                    btnWorkbenchTabs[i].BackColor = isSel ? Color.FromArgb(0, 180, 216) : Color.FromArgb(226, 232, 240);
                    btnWorkbenchTabs[i].ForeColor = isSel ? Color.White : Color.FromArgb(71, 85, 105);
                }
                if (pnlWorkbenchViews != null && pnlWorkbenchViews[i] != null)
                {
                    pnlWorkbenchViews[i].Visible = (i == viewIdx);
                }
            }
        }

        private Label CreateMetricCardInCell(TableLayoutPanel parent, string title, string defaultVal, Color accent, int col)
        {
            Panel card = new Panel() { Dock = DockStyle.Fill, BackColor = Color.White, BorderStyle = BorderStyle.FixedSingle, Margin = new Padding(3) };
            Panel pnlTop = new Panel() { Dock = DockStyle.Top, Height = 28, BackColor = Color.Transparent };
            Label lt = new Label() { Text = title, Dock = DockStyle.Fill, ForeColor = Color.FromArgb(70, 80, 95), Font = new Font("微軟正黑體", (col == 0 || col == 1 ? 8.5f : fontMetricTitlePt), FontStyle.Bold), TextAlign = ContentAlignment.MiddleCenter };
            Label lv = new Label() { Text = defaultVal, Dock = DockStyle.Fill, ForeColor = accent, Font = new Font("Consolas", fontMetricValPt, FontStyle.Bold), TextAlign = ContentAlignment.MiddleCenter };

            if (col == 0) // 實測轉速卡片 (Speed): 待測端轉速鎖 (補償感應馬達 V/F 轉差)
            {
                TableLayoutPanel tblLeftBarSpd = new TableLayoutPanel()
                {
                    Dock = DockStyle.Left,
                    Width = 92,
                    ColumnCount = 1,
                    RowCount = 3,
                    BackColor = Color.Transparent,
                    Padding = new Padding(2, 2, 2, 2),
                    Margin = new Padding(0)
                };
                tblLeftBarSpd.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
                tblLeftBarSpd.RowStyles.Add(new RowStyle(SizeType.Absolute, 28f)); // Row 0: 按鈕鎖定高度 28px
                tblLeftBarSpd.RowStyles.Add(new RowStyle(SizeType.Absolute, 42f)); // Row 1: 轉速死區微調
                tblLeftBarSpd.RowStyles.Add(new RowStyle(SizeType.Absolute, 42f)); // Row 2: 目標轉速微調

                btnLockSpeed = new Button()
                {
                    Text = (hasSpeedBaseline ? " LOCK" : " UNLOCK"),
                    Image = CreateLockIconImage(13, 13, (hasSpeedBaseline ? Color.FromArgb(0, 120, 215) : Color.FromArgb(100, 116, 139)), hasSpeedBaseline),
                    ImageAlign = ContentAlignment.MiddleLeft,
                    TextAlign = ContentAlignment.MiddleRight,
                    TextImageRelation = TextImageRelation.ImageBeforeText,
                    Dock = DockStyle.Fill,
                    Margin = new Padding(0),
                    Padding = new Padding(0),
                    BackColor = (hasSpeedBaseline ? Color.FromArgb(224, 242, 254) : Color.FromArgb(241, 245, 249)),
                    ForeColor = (hasSpeedBaseline ? Color.FromArgb(0, 120, 215) : Color.FromArgb(100, 116, 139)),
                    Font = new Font("Tahoma", 7.5f, FontStyle.Bold),
                    Cursor = Cursors.Hand,
                    FlatStyle = FlatStyle.Flat
                };
                btnLockSpeed.FlatAppearance.BorderSize = 1;
                btnLockSpeed.FlatAppearance.BorderColor = (hasSpeedBaseline ? Color.FromArgb(2, 132, 199) : Color.FromArgb(203, 213, 225));

                ToolTip ttLockSpd = new ToolTip();
                ttLockSpd.SetToolTip(btnLockSpeed, "點擊一鍵啟閉【待測端轉速鎖】(自動補償感應馬達 V/F 轉差)");

                // 框框 1: 轉速死區微調框
                Panel pnlDbBoxSpd = new Panel() { Dock = DockStyle.Fill, Margin = new Padding(0), Padding = new Padding(0, 1, 0, 0), Visible = hasSpeedBaseline && isSpeedTracking };
                Label lblDbSpdHint = new Label() { Text = "⚡死區/誤差", Dock = DockStyle.Top, Height = 14, Font = new Font("微軟正黑體", 7.5f, FontStyle.Bold), ForeColor = Color.FromArgb(100, 116, 139), TextAlign = ContentAlignment.BottomLeft };
                numCardSpeedDeadband = new NumericUpDown()
                {
                    Dock = DockStyle.Top,
                    Height = 22,
                    Minimum = 0.1m,
                    Maximum = 50.0m,
                    DecimalPlaces = 1,
                    Increment = 0.5m,
                    Value = trackingSpeedDeadband,
                    Font = new Font("Consolas", 9.5f, FontStyle.Bold),
                    BackColor = Color.FromArgb(255, 255, 255)
                };
                numCardSpeedDeadband.ValueChanged += (s, e) => {
                    trackingSpeedDeadband = numCardSpeedDeadband.Value;
                    SaveLayoutConfig();
                };
                ttLockSpd.SetToolTip(numCardSpeedDeadband, "轉速閉迴路容許誤差死區 (rpm)，修改後自動儲存");
                pnlDbBoxSpd.Controls.Add(numCardSpeedDeadband);
                pnlDbBoxSpd.Controls.Add(lblDbSpdHint);

                // 框框 2: 目標轉速輸入框 (常態顯示，全系統與 S1/S2/S6/空載/LOCK 雙向貫通)
                Panel pnlTgtSpdBox = new Panel() { Dock = DockStyle.Fill, Margin = new Padding(0), Padding = new Padding(0, 1, 0, 0), Visible = true };
                Label lblTgtSpdHint = new Label() { Text = "🎯目標(rpm)", Dock = DockStyle.Top, Height = 14, Font = new Font("微軟正黑體", 7.5f, FontStyle.Bold), ForeColor = Color.FromArgb(0, 120, 215), TextAlign = ContentAlignment.BottomLeft };
                numCardTargetSpeed = new NumericUpDown()
                {
                    Dock = DockStyle.Top,
                    Height = 22,
                    Minimum = 0m,
                    Maximum = 6000m,
                    DecimalPlaces = 1,
                    Increment = 10m,
                    Value = (decimal)Math.Max(0.0, baselineSpeed),
                    Font = new Font("Consolas", 9.5f, FontStyle.Bold),
                    BackColor = Color.FromArgb(255, 255, 255)
                };
                numCardTargetSpeed.ValueChanged += (s, e) => {
                    baselineSpeed = (double)numCardTargetSpeed.Value;
                    if (dutyTimer != null && dutyTimer.Enabled && numDutySpeed != null && !isSyncingDutyControls && numCardTargetSpeed.Focused)
                    {
                        isSyncingDutyControls = true;
                        numDutySpeed.Value = Math.Min(numDutySpeed.Maximum, Math.Max(numDutySpeed.Minimum, numCardTargetSpeed.Value));
                        if (numDutyMiniSpd != null) numDutyMiniSpd.Value = numDutySpeed.Value;
                        isSyncingDutyControls = false;
                    }
                };
                ttLockSpd.SetToolTip(numCardTargetSpeed, "目標轉速 (rpm)，支援 S1/S2/S6、空載、LOCK 與手動模式直接調校");
                pnlTgtSpdBox.Controls.Add(numCardTargetSpeed);
                pnlTgtSpdBox.Controls.Add(lblTgtSpdHint);

                tblLeftBarSpd.Controls.Add(btnLockSpeed, 0, 0);
                tblLeftBarSpd.Controls.Add(pnlDbBoxSpd, 0, 1);
                tblLeftBarSpd.Controls.Add(pnlTgtSpdBox, 0, 2);

                btnLockSpeed.Click += (s, e) => {
                    hasSpeedBaseline = !hasSpeedBaseline;
                    isSpeedTracking = hasSpeedBaseline; // 啟閉轉速閉迴路追隨

                    if (hasSpeedBaseline)
                    {
                        var numSpdCtl = (currentKebMode2 == 7 || currentKebMode2 == 9) ? numHmiKebSpeed2 : numHmiKebSpeed1;
                        activeTrackingSpeedRpm = (numSpdCtl != null) ? numSpdCtl.Value : 0m;

                        double currentMeasuredSpeed = Math.Abs(actSpeed != 0.0 ? actSpeed : smoothedSpeed);
                        decimal targetSpd = (decimal)Math.Round(currentMeasuredSpeed, 1);
                        if (targetSpd < numCardTargetSpeed.Minimum) targetSpd = numCardTargetSpeed.Minimum;
                        if (targetSpd > numCardTargetSpeed.Maximum) targetSpd = numCardTargetSpeed.Maximum;
                        baselineSpeed = (double)targetSpd;
                        numCardTargetSpeed.Value = targetSpd;
                        numCardSpeedDeadband.Value = trackingSpeedDeadband;
                        pnlDbBoxSpd.Visible = true;
                        pnlTgtSpdBox.Visible = true;

                        btnLockSpeed.Image = CreateLockIconImage(14, 14, Color.FromArgb(0, 120, 215), true);
                        btnLockSpeed.Text = " LOCK";
                        btnLockSpeed.ForeColor = Color.FromArgb(0, 120, 215);
                        btnLockSpeed.BackColor = Color.FromArgb(224, 242, 254);
                        btnLockSpeed.FlatAppearance.BorderColor = Color.FromArgb(2, 132, 199);
                        ttLockSpd.SetToolTip(btnLockSpeed, string.Format("轉速鎖定追隨中！目標: {0:F1} rpm, 死區: {1:F1} rpm (手動控制已互鎖保護，請透過上方🎯目標調整)", baselineSpeed, trackingSpeedDeadband));
                        WriteHmiLog("CLOSED_LOOP", string.Format("【🌀 啟動轉速平滑追隨】已鎖定實測轉速 {0:F1} rpm 為目標 (初始給定 {1:F0} rpm，手動速度已互鎖，死區: {2:F1} rpm)！", baselineSpeed, activeTrackingSpeedRpm, trackingSpeedDeadband));
                    }
                    else
                    {
                        baselineSpeed = 0.0;
                        pnlDbBoxSpd.Visible = false;
                        // 保留目標轉速框常態可見，以供 S1/S2/S6、空載或手動狀態使用
                        pnlTgtSpdBox.Visible = true;

                        btnLockSpeed.Image = CreateLockIconImage(14, 14, Color.FromArgb(100, 116, 139), false);
                        btnLockSpeed.Text = " UNLOCK";
                        btnLockSpeed.ForeColor = Color.FromArgb(100, 116, 139);
                        btnLockSpeed.BackColor = Color.FromArgb(241, 245, 249);
                        btnLockSpeed.FlatAppearance.BorderColor = Color.FromArgb(203, 213, 225);
                        ttLockSpd.SetToolTip(btnLockSpeed, "點擊一鍵啟閉【待測端轉速鎖】(自動補償感應馬達 V/F 轉差)");
                        WriteHmiLog("CLOSED_LOOP", "【🌀 停止轉速平滑追隨】已解除轉速平滑追隨，恢復手動操作。");
                    }
                    UpdateHmiKebModeButtonsVisual();
                };

                Panel pnlRightMainSpd = new Panel() { Dock = DockStyle.Fill, BackColor = Color.Transparent };
                pnlTop.Controls.Add(lt);
                pnlRightMainSpd.Controls.Add(lv);
                pnlRightMainSpd.Controls.Add(pnlTop);

                card.Controls.Add(pnlRightMainSpd);
                card.Controls.Add(tblLeftBarSpd);
            }
            else if (col == 1) // 實測轉矩卡片 (Torque): 採用 TableLayoutPanel 鎖死按鈕在頂部絕對不位移，下方展開死區與目標微調
            {
                // 左側固定工具列 (採用 TableLayoutPanel，Row 0 鎖死按鈕永不移動，Row 1 死區，Row 2 目標)
                TableLayoutPanel tblLeftBar = new TableLayoutPanel()
                {
                    Dock = DockStyle.Left,
                    Width = 92,
                    ColumnCount = 1,
                    RowCount = 3,
                    BackColor = Color.Transparent,
                    Padding = new Padding(2, 2, 2, 2),
                    Margin = new Padding(0)
                };
                tblLeftBar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
                tblLeftBar.RowStyles.Add(new RowStyle(SizeType.Absolute, 28f)); // Row 0: 按鈕固定高度 28px，絕對不位移！
                tblLeftBar.RowStyles.Add(new RowStyle(SizeType.Absolute, 42f)); // Row 1: 專門展開死區參數微調
                tblLeftBar.RowStyles.Add(new RowStyle(SizeType.Absolute, 42f)); // Row 2: 目標轉矩微調

                btnLockTorque = new Button()
                {
                    Text = (hasBaseline ? " LOCK" : " UNLOCK"),
                    Image = CreateLockIconImage(13, 13, (hasBaseline ? Color.FromArgb(220, 38, 38) : Color.FromArgb(100, 116, 139)), hasBaseline),
                    ImageAlign = ContentAlignment.MiddleLeft,
                    TextAlign = ContentAlignment.MiddleRight,
                    TextImageRelation = TextImageRelation.ImageBeforeText,
                    Dock = DockStyle.Fill,
                    Margin = new Padding(0),
                    Padding = new Padding(0),
                    BackColor = (hasBaseline ? Color.FromArgb(254, 243, 199) : Color.FromArgb(241, 245, 249)),
                    ForeColor = (hasBaseline ? Color.FromArgb(220, 38, 38) : Color.FromArgb(100, 116, 139)),
                    Font = new Font("Tahoma", 7.5f, FontStyle.Bold),
                    Cursor = Cursors.Hand,
                    FlatStyle = FlatStyle.Flat
                };
                btnLockTorque.FlatAppearance.BorderSize = 1;
                btnLockTorque.FlatAppearance.BorderColor = (hasBaseline ? Color.FromArgb(245, 158, 11) : Color.FromArgb(203, 213, 225));

                ToolTip ttLock = new ToolTip();
                ttLock.SetToolTip(btnLockTorque, "點擊一鍵啟閉【閉迴路平滑追隨】控制 (下方展開死區與目標微調)");

                // 框框 1: 死區/誤差微調框 (嚴格位於 Row 1，按鈕正下方)
                Panel pnlDbBox = new Panel() { Dock = DockStyle.Fill, Margin = new Padding(0), Padding = new Padding(0, 1, 0, 0), Visible = hasBaseline && isClosedLoopTracking };
                lblDbTrqHint = new Label() { Text = string.Format("⚡死區(≥{0:F2})", calculatedMinDeadbandTorque), Dock = DockStyle.Top, Height = 14, Font = new Font("微軟正黑體", 7.5f, FontStyle.Bold), ForeColor = Color.FromArgb(100, 116, 139), TextAlign = ContentAlignment.BottomLeft };
                numCardDeadband = new NumericUpDown()
                {
                    Dock = DockStyle.Top,
                    Height = 22,
                    Minimum = 0.01m,
                    Maximum = 5.0m,
                    DecimalPlaces = 2,
                    Increment = 0.01m,
                    Value = Math.Max((decimal)calculatedMinDeadbandTorque, trackingDeadband),
                    Font = new Font("Consolas", 9.5f, FontStyle.Bold),
                    BackColor = Color.FromArgb(255, 255, 255)
                };
                numCardDeadband.ValueChanged += (s, e) => {
                    trackingDeadband = numCardDeadband.Value;
                    if (numDeadband != null && numDeadband.Value != numCardDeadband.Value) numDeadband.Value = numCardDeadband.Value;
                    SaveLayoutConfig();
                };
                ttLock.SetToolTip(numCardDeadband, string.Format("平滑追隨容許誤差死區 Deadband (Nm)\n依據 cs.19 基準換算：0.1% 單步物理極限 x = cs19/1000 = {0:F2} Nm\n死區必須大於等於此值才合理，否則系統在相鄰步進間跳動！", calculatedMinDeadbandTorque));
                pnlDbBox.Controls.Add(numCardDeadband);
                pnlDbBox.Controls.Add(lblDbTrqHint);

                // 框框 2: 目標轉矩輸入框 (常態顯示，全系統與 S1/S2/S6/空載/LOCK 雙向貫通)
                Panel pnlTgtTrqBox = new Panel() { Dock = DockStyle.Fill, Margin = new Padding(0), Padding = new Padding(0, 1, 0, 0), Visible = true };
                Label lblTgtTrqHint = new Label() { Text = "🎯目標(Nm)", Dock = DockStyle.Top, Height = 14, Font = new Font("微軟正黑體", 7.5f, FontStyle.Bold), ForeColor = Color.FromArgb(245, 158, 11), TextAlign = ContentAlignment.BottomLeft };
                numCardTargetTorque = new NumericUpDown()
                {
                    Dock = DockStyle.Top,
                    Height = 22,
                    Minimum = 0m,
                    Maximum = 500m,
                    DecimalPlaces = 2,
                    Increment = 0.05m,
                    Value = (decimal)Math.Max(0.0, baselineTorque),
                    Font = new Font("Consolas", 9.5f, FontStyle.Bold),
                    BackColor = Color.FromArgb(255, 255, 255)
                };
                numCardTargetTorque.ValueChanged += (s, e) => {
                    baselineTorque = (double)numCardTargetTorque.Value;
                    if (dutyTimer != null && dutyTimer.Enabled && numDutyTorque != null && !isSyncingDutyControls && numCardTargetTorque.Focused)
                    {
                        isSyncingDutyControls = true;
                        numDutyTorque.Value = Math.Min(numDutyTorque.Maximum, Math.Max(numDutyTorque.Minimum, numCardTargetTorque.Value));
                        if (numDutyMiniTrq != null) numDutyMiniTrq.Value = numDutyTorque.Value;
                        isSyncingDutyControls = false;
                    }
                };
                ttLock.SetToolTip(numCardTargetTorque, "目標轉矩 (Nm)，支援 S1/S2/S6、TN、LOCK 與手動模式直接調校");
                pnlTgtTrqBox.Controls.Add(numCardTargetTorque);
                pnlTgtTrqBox.Controls.Add(lblTgtTrqHint);

                tblLeftBar.Controls.Add(btnLockTorque, 0, 0);
                tblLeftBar.Controls.Add(pnlDbBox, 0, 1);
                tblLeftBar.Controls.Add(pnlTgtTrqBox, 0, 2);

                btnLockTorque.Click += (s, e) => {
                    hasBaseline = !hasBaseline;
                    isLocked = hasBaseline;
                    isClosedLoopTracking = hasBaseline; // 完全打通連動：LOCK 即啟動追隨，UNLOCK 即停止

                    if (hasBaseline)
                    {
                        var numTrqCtl = (currentKebMode1 == 8 || currentKebMode1 == 10) ? numHmiKebTorque1 : numHmiKebTorque2;
                        activeTrackingTorquePct = (numTrqCtl != null) ? numTrqCtl.Value : 0m;

                        double currentMeasuredTorque = Math.Abs(actTorque != 0.0 ? actTorque : smoothedTorque);
                        decimal targetTrq = (decimal)Math.Round(currentMeasuredTorque, 2);
                        if (targetTrq < numCardTargetTorque.Minimum) targetTrq = numCardTargetTorque.Minimum;
                        if (targetTrq > numCardTargetTorque.Maximum) targetTrq = numCardTargetTorque.Maximum;
                        baselineTorque = (double)targetTrq;
                        numCardTargetTorque.Value = targetTrq;
                        if (hasReadCs19 && calculatedMinDeadbandTorque > 0 && trackingDeadband < (decimal)calculatedMinDeadbandTorque)
                        {
                            trackingDeadband = (decimal)calculatedMinDeadbandTorque;
                        }
                        numCardDeadband.Value = trackingDeadband;
                        pnlDbBox.Visible = true;
                        pnlTgtTrqBox.Visible = true;

                        btnLockTorque.Image = CreateLockIconImage(14, 14, Color.FromArgb(220, 38, 38), true);
                        btnLockTorque.Text = " LOCK";
                        btnLockTorque.ForeColor = Color.FromArgb(220, 38, 38);
                        btnLockTorque.BackColor = Color.FromArgb(254, 243, 199);
                        btnLockTorque.FlatAppearance.BorderColor = Color.FromArgb(245, 158, 11);
                        ttLock.SetToolTip(btnLockTorque, string.Format("平滑追隨控制中！基準: {0:F2} Nm, 死區: {1:F2} Nm (手動控制已互鎖保護，請透過上方🎯目標調整)", baselineTorque, trackingDeadband));
                        WriteHmiLog("CLOSED_LOOP", string.Format("【🎯 啟動平滑追隨】已鎖定實測轉矩 {0:F2} Nm 為目標 (初始給定 {1:F1}%，手動轉矩已互鎖，死區: {2:F2} Nm)！", baselineTorque, activeTrackingTorquePct, trackingDeadband));
                    }
                    else
                    {
                        baselineTorque = 0.0;
                        pnlDbBox.Visible = false;
                        // 保留目標轉矩框常態可見，以供 S1/S2/S6、TN 或手動狀態使用
                        pnlTgtTrqBox.Visible = true;

                        btnLockTorque.Image = CreateLockIconImage(14, 14, Color.FromArgb(100, 116, 139), false);
                        btnLockTorque.Text = " UNLOCK";
                        btnLockTorque.ForeColor = Color.FromArgb(100, 116, 139);
                        btnLockTorque.BackColor = Color.FromArgb(241, 245, 249);
                        btnLockTorque.FlatAppearance.BorderColor = Color.FromArgb(203, 213, 225);
                        ttLock.SetToolTip(btnLockTorque, "點擊一鍵啟閉【閉迴路平滑追隨】控制 (下方展開死區與目標微調)");
                        WriteHmiLog("CLOSED_LOOP", "【🎯 停止平滑追隨】已解除平滑追隨控制，恢復手動操作。");
                    }
                    UpdateHmiKebModeButtonsVisual();
                };

                // 右側內容面板 (頂部標題 + 中央大字體數值)
                Panel pnlRightMain = new Panel() { Dock = DockStyle.Fill, BackColor = Color.Transparent };
                pnlTop.Controls.Add(lt);
                pnlRightMain.Controls.Add(lv);
                pnlRightMain.Controls.Add(pnlTop);

                card.Controls.Add(pnlRightMain);
                card.Controls.Add(tblLeftBar);
            }
            else
            {
                pnlTop.Controls.Add(lt);
                card.Controls.Add(lv);
                card.Controls.Add(pnlTop);
            }

            listMetricTitleLabels.Add(lt);
            listMetricValueLabels.Add(lv);
            parent.Controls.Add(card, col, 0);
            return lv;
        }

        private Label CreateMetricCard(Panel parent, string title, string defaultVal, Color accent, int xOffset)
        {
            Panel card = new Panel() { Size = new Size(118, 88), Location = new Point(xOffset, 2), BackColor = Color.White, BorderStyle = BorderStyle.FixedSingle };
            Label lt = new Label() { Text = title, Location = new Point(2, 4), Size = new Size(112, 30), ForeColor = Color.FromArgb(80, 90, 105), Font = new Font("微軟正黑體", 8.5f, FontStyle.Bold), TextAlign = ContentAlignment.MiddleCenter };
            Label lv = new Label() { Text = defaultVal, Location = new Point(2, 38), Size = new Size(112, 42), ForeColor = accent, Font = new Font("微軟正黑體", 11.5f, FontStyle.Bold), TextAlign = ContentAlignment.MiddleCenter };
            card.Controls.Add(lt);
            card.Controls.Add(lv);
            parent.Controls.Add(card);
            return lv;
        }



        // =========================================================================
        // 分頁 5: GBD 網路多通道溫度記錄器 (Graphtec GL820: 單行橫向 20 通道 + 自訂名稱)
        // =========================================================================
        private void BuildGbdTab(TabPage tab)
        {
            TableLayoutPanel tableGbd = new TableLayoutPanel()
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 3,
                BackColor = Color.FromArgb(240, 243, 246),
                Padding = new Padding(4),
                Margin = new Padding(0)
            };
            tableGbd.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            tableGbd.RowStyles.Add(new RowStyle(SizeType.Absolute, 45f));  // 頂部資訊與匯出列
            tableGbd.RowStyles.Add(new RowStyle(SizeType.Absolute, 115f)); // 20 通道單行橫向遙測與自訂名稱表
            tableGbd.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));  // 20 通道趨勢圖

            // 1. 頂部工具列
            Panel pnlTop = new Panel() { Dock = DockStyle.Fill, BackColor = Color.FromArgb(245, 248, 252), BorderStyle = BorderStyle.FixedSingle };

            Label lGbdTitle = new Label()
            {
                Text = "Graphtec GL820 網路 20 通道溫度記錄器 (TCP Port 8023) | 支援第一列點擊自訂通道名稱並連動 RAW DATA 欄位",
                Location = new Point(10, 12),
                AutoSize = true,
                Font = new Font("微軟正黑體", 9.5f, FontStyle.Bold),
                ForeColor = Color.FromArgb(20, 50, 100)
            };

            Button btnExportGbd = new Button()
            {
                Text = "匯出溫度歷程 (CSV)",
                Location = new Point(740, 7),
                Size = new Size(160, 30),
                BackColor = Color.FromArgb(0, 120, 215),
                ForeColor = Color.White,
                Font = new Font("微軟正黑體", 9f, FontStyle.Bold),
                Anchor = AnchorStyles.Top | AnchorStyles.Right
            };
            btnExportGbd.Click += BtnExportGbd_Click;

            Button btnClearGbd = new Button()
            {
                Text = "清除曲線",
                Location = new Point(910, 7),
                Size = new Size(85, 30),
                BackColor = Color.FromArgb(100, 116, 139),
                ForeColor = Color.White,
                Font = new Font("微軟正黑體", 9f, FontStyle.Bold),
                Anchor = AnchorStyles.Top | AnchorStyles.Right
            };
            btnClearGbd.Click += (s, e) => {
                if (gbdTrendChart != null) gbdTrendChart.ClearData();
                gbdHistory.Clear();
                gbdHistory.TrimExcess();
                GC.Collect();
            };

            pnlTop.Controls.AddRange(new Control[] { lGbdTitle, btnExportGbd, btnClearGbd });
            tableGbd.Controls.Add(pnlTop, 0, 0);

            // 2. 20 通道即時遙測表 (單行 20 欄橫向排列)
            GroupBox grpGrid = new GroupBox()
            {
                Text = "GL820 CH1 ~ CH20 橫向溫度矩陣 (第一列【測點名稱】可直接點選編輯（僅限英數），將自動儲存並寫入 RAW DATA)",
                Dock = DockStyle.Fill,
                Font = new Font("微軟正黑體", 9f, FontStyle.Bold)
            };
            dgvGbdAll = new DataGridView()
            {
                Dock = DockStyle.Fill,
                BackgroundColor = Color.White,
                BorderStyle = BorderStyle.Fixed3D,
                RowHeadersVisible = true,
                RowHeadersWidth = 120,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                AllowUserToResizeRows = false,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                Font = new Font("微軟正黑體", 9f, FontStyle.Regular),
                ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle()
                {
                    BackColor = Color.FromArgb(230, 238, 248),
                    Font = new Font("微軟正黑體", 8.5f, FontStyle.Bold),
                    Alignment = DataGridViewContentAlignment.MiddleCenter
                },
                DefaultCellStyle = new DataGridViewCellStyle()
                {
                    Alignment = DataGridViewContentAlignment.MiddleCenter,
                    Font = new Font("微軟正黑體", 9f, FontStyle.Bold)
                },
                EnableHeadersVisualStyles = false
            };

            for (int i = 1; i <= 20; i++)
            {
                var col = new DataGridViewTextBoxColumn();
                col.Name = "CH" + i;
                col.HeaderText = "CH " + i;
                col.SortMode = DataGridViewColumnSortMode.NotSortable;
                dgvGbdAll.Columns.Add(col);
            }

            int rName = dgvGbdAll.Rows.Add();
            dgvGbdAll.Rows[rName].HeaderCell.Value = "名稱";
            dgvGbdAll.Rows[rName].DefaultCellStyle.BackColor = Color.FromArgb(241, 245, 249);
            dgvGbdAll.Rows[rName].DefaultCellStyle.ForeColor = Color.FromArgb(30, 64, 175);
            dgvGbdAll.Rows[rName].DefaultCellStyle.Font = new Font("微軟正黑體", 8.5f, FontStyle.Regular);

            int rVal = dgvGbdAll.Rows.Add();
            dgvGbdAll.Rows[rVal].HeaderCell.Value = "°C";
            dgvGbdAll.Rows[rVal].ReadOnly = true;
            dgvGbdAll.Rows[rVal].DefaultCellStyle.BackColor = Color.White;
            dgvGbdAll.Rows[rVal].DefaultCellStyle.ForeColor = Color.FromArgb(15, 23, 42);
            dgvGbdAll.Rows[rVal].DefaultCellStyle.Font = new Font("Consolas", 10f, FontStyle.Bold);

            for (int i = 0; i < 20; i++)
            {
                dgvGbdAll.Rows[rName].Cells[i].Value = (gl820ChannelNames != null && i < gl820ChannelNames.Length) ? gl820ChannelNames[i] : ("CH" + (i + 1));
                dgvGbdAll.Rows[rVal].Cells[i].Value = "--.-";
            }

            // 限制輸入 CH 通道名稱時僅限英文輸入 (關閉中文輸入法並過濾按鍵)
            dgvGbdAll.EditingControlShowing += (s, e) => {
                TextBox tb = e.Control as TextBox;
                if (tb != null)
                {
                    tb.ImeMode = ImeMode.Disable;
                    tb.KeyPress -= GbdChannelName_KeyPress;
                    tb.KeyPress += GbdChannelName_KeyPress;
                }
            };

            dgvGbdAll.CellValueChanged += (s, e) => {
                if (e.RowIndex == 0 && e.ColumnIndex >= 0 && e.ColumnIndex < 20)
                {
                    object val = dgvGbdAll.Rows[0].Cells[e.ColumnIndex].Value;
                    string rawStr = val != null ? val.ToString().Trim() : "";
                    // 二次防護：過濾剪貼簿貼上之中文或非法符號，限制使用英數字元
                    string cleanStr = System.Text.RegularExpressions.Regex.Replace(rawStr, @"[^a-zA-Z0-9_\-\.\s]", "").Trim();
                    if (string.IsNullOrEmpty(cleanStr)) cleanStr = "CH" + (e.ColumnIndex + 1);

                    if (cleanStr != rawStr)
                    {
                        dgvGbdAll.Rows[0].Cells[e.ColumnIndex].Value = cleanStr;
                    }

                    gl820ChannelNames[e.ColumnIndex] = cleanStr;
                    SaveLayoutConfig();
                    if (gbdTrendChart != null) gbdTrendChart.Invalidate();
                }
            };

            grpGrid.Controls.Add(dgvGbdAll);
            tableGbd.Controls.Add(grpGrid, 0, 1);

            // 3. CH1~CH20 顯示勾選列 + 多通道溫度即時趨勢曲線圖
            TableLayoutPanel tableChartArea = new TableLayoutPanel()
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 2,
                Margin = new Padding(0)
            };
            tableChartArea.RowStyles.Add(new RowStyle(SizeType.Absolute, 30f)); // 通道勾選列（單排）
            tableChartArea.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));  // 趨勢圖

            // CH1~CH20 顯示勾選 — 使用 FlowLayoutPanel 一排顯示
            FlowLayoutPanel pnlChkRow = new FlowLayoutPanel()
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                BackColor = Color.FromArgb(240, 243, 246),
                Padding = new Padding(4, 3, 4, 3),
                AutoScroll = true
            };
            Label lblChkTitle = new Label()
            {
                Text = "顯示通道:",
                AutoSize = true,
                Font = new Font("微軟正黑體", 8.5f, FontStyle.Bold),
                ForeColor = Color.FromArgb(30, 64, 175),
                Margin = new Padding(0, 3, 6, 0)
            };
            pnlChkRow.Controls.Add(lblChkTitle);

            Button btnAutoGbd = new Button()
            {
                Text = "⚡ 依實測選取",
                AutoSize = true,
                Height = 25,
                BackColor = Color.FromArgb(59, 130, 246),
                ForeColor = Color.White,
                Font = new Font("微軟正黑體", 8f, FontStyle.Bold),
                Cursor = Cursors.Hand,
                Margin = new Padding(0, 1, 4, 0)
            };
            btnAutoGbd.Click += (s, e) =>
            {
                bool[] active = DetectActiveGbdChannels();
                if (active.Any(b => b))
                {
                    ApplyDetectedGbdChannels(active);
                }
                else
                {
                    MessageBox.Show("目前尚未連線至 GL820 或未收到有效溫度數據，無法偵測實測通道。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
            };
            Button btnAllGbd = new Button()
            {
                Text = "全選",
                Width = 44,
                Height = 25,
                BackColor = Color.FromArgb(241, 245, 249),
                Font = new Font("微軟正黑體", 8f),
                Cursor = Cursors.Hand,
                Margin = new Padding(0, 1, 3, 0)
            };
            btnAllGbd.Click += (s, e) =>
            {
                bool[] all = new bool[20]; for (int i = 0; i < 20; i++) all[i] = true;
                ApplyDetectedGbdChannels(all);
            };
            Button btnUncheckAllGbd = new Button()
            {
                Text = "全消",
                Width = 44,
                Height = 25,
                BackColor = Color.FromArgb(241, 245, 249),
                Font = new Font("微軟正黑體", 8f),
                Cursor = Cursors.Hand,
                Margin = new Padding(0, 1, 8, 0)
            };
            btnUncheckAllGbd.Click += (s, e) =>
            {
                bool[] none = new bool[20]; none[0] = true; // 最少保留 CH1
                ApplyDetectedGbdChannels(none);
            };
            pnlChkRow.Controls.AddRange(new Control[] { btnAutoGbd, btnAllGbd, btnUncheckAllGbd });

            // 建立 20 個 CheckBox，單排橫向
            if (chkGbdChannels == null || chkGbdChannels.Length < 20) chkGbdChannels = new CheckBox[20];
            for (int i = 0; i < 20; i++)
            {
                int idx = i;
                chkGbdChannels[i] = new CheckBox()
                {
                    Text = "CH" + (i + 1),
                    Checked = (gl820ChannelMask != null && i < gl820ChannelMask.Length) ? gl820ChannelMask[i] : (i < 4),
                    AutoSize = true,
                    Font = new Font("微軟正黑體", 8f),
                    ForeColor = Color.FromArgb(15, 23, 42),
                    Margin = new Padding(0, 3, 6, 0)
                };
                chkGbdChannels[i].CheckedChanged += (s, e) =>
                {
                    if (gl820ChannelMask != null && idx < gl820ChannelMask.Length)
                        gl820ChannelMask[idx] = chkGbdChannels[idx].Checked;
                    if (gbdTrendChart != null) gbdTrendChart.SetChannelVisibility(gl820ChannelMask);
                    SaveLayoutConfig();
                };
                pnlChkRow.Controls.Add(chkGbdChannels[i]);
            }
            tableChartArea.Controls.Add(pnlChkRow, 0, 0);

            GroupBox grpChart = new GroupBox() { Text = "GL820 全 20 通道溫度連續波形圖 (CH1 ~ CH20 0~120°C 即時繪製)", Dock = DockStyle.Fill, Font = new Font("微軟正黑體", 9f, FontStyle.Bold) };
            gbdTrendChart = new GbdTemperatureTrendControl(this) { Dock = DockStyle.Fill };
            if (gl820ChannelMask != null) gbdTrendChart.SetChannelVisibility(gl820ChannelMask);
            grpChart.Controls.Add(gbdTrendChart);
            tableChartArea.Controls.Add(grpChart, 0, 1);

            tableGbd.Controls.Add(tableChartArea, 0, 2);

            tab.Controls.Add(tableGbd);
        }

        private void BtnExportGbd_Click(object sender, EventArgs e)
        {
            try
            {
                SaveFileDialog sfd = new SaveFileDialog()
                {
                    Title = "匯出 GL820 多通道溫度歷程數據",
                    Filter = "CSV 試算表 (*.csv)|*.csv|文字檔 (*.txt)|*.txt|所有檔案 (*.*)|*.*",
                    FileName = string.Format("GL820_Temperature_Log_{0}.csv", DateTime.Now.ToString("yyyyMMdd_HHmmss"))
                };

                if (sfd.ShowDialog() == DialogResult.OK)
                {
                    using (StreamWriter sw = new StreamWriter(sfd.FileName, false, Encoding.UTF8))
                    {
                        StringBuilder sbH = new StringBuilder("Timestamp");
                        for (int i = 1; i <= 20; i++) sbH.Append(",CH" + i);
                        sw.WriteLine(sbH.ToString());

                        foreach (var item in gbdHistory)
                        {
                            StringBuilder sbRow = new StringBuilder(item.Key.ToString("yyyy-MM-dd HH:mm:ss.fff"));
                            for (int i = 0; i < 20; i++)
                            {
                                double v = (i < item.Value.Length) ? item.Value[i] : 0.0;
                                sbRow.Append(string.Format(",{0:F2}", v));
                            }
                            sw.WriteLine(sbRow.ToString());
                        }
                    }
                    MessageBox.Show("溫度數據已成功匯出至:\n" + sfd.FileName, "匯出成功", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("匯出失敗: " + ex.Message, "錯誤", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void GbdChannelName_KeyPress(object sender, KeyPressEventArgs e)
        {
            // 允許控制鍵（退格、刪除、複製貼上等）
            if (char.IsControl(e.KeyChar)) return;

            // 限制僅允許 ASCII 英文字母 (A-Z, a-z)、數字 (0-9)、底線 (_)、減號 (-)、小數點 (.) 與空格 ( )
            bool isEnglishOrCommon = (e.KeyChar >= 'a' && e.KeyChar <= 'z') ||
                                     (e.KeyChar >= 'A' && e.KeyChar <= 'Z') ||
                                     (e.KeyChar >= '0' && e.KeyChar <= '9') ||
                                     e.KeyChar == '_' || e.KeyChar == '-' || e.KeyChar == '.' || e.KeyChar == ' ';

            if (!isEnglishOrCommon)
            {
                e.Handled = true; // 攔截非英數字元（包含中文、全形字等）
            }
        }

        // =========================================================================
        // 核心架構升級：獨立背景非同步硬體通訊執行緒 (Background Telemetry Worker)
        // 徹底解除 UI 主執行緒阻塞，UI 保持 60 FPS 極速流暢，輪詢週期可自由即時調節！
        // =========================================================================
        private Thread telemetryWorkerThread;
        private volatile bool isWorkerRunning = false;
        public volatile int pollingIntervalMs = 100; // 預設 100ms (扭力計/功率分析儀高速採樣)
        public volatile int kebPollingIntervalMs = 1000; // KEB 變頻器獨立輪詢週期 (預設 1000ms / 1秒更新一次，大幅降低序列埠負擔)
        private long lastKebPollTime1 = 0;
        private long lastKebPollTime2 = 0;
        public NumericUpDown numUnifiedInterval;
        public NumericUpDown numKebPollingInterval;

        // WT333E 最新各相真實數值緩存 (電壓, 電流, 有功功率, 視在功率, 無功功率, 功率因數, 相位角, 頻率)
        private volatile float wtU1 = 0f, wtI1 = 0f, wtP1 = 0f, wtS1 = 0f, wtQ1 = 0f, wtPF1 = 1f, wtPhi1 = 0f, wtFreqU = 0f, wtFreqI = 0f;
        private volatile float wtU2 = 0f, wtI2 = 0f, wtP2 = 0f, wtS2 = 0f, wtQ2 = 0f, wtPF2 = 1f, wtPhi2 = 0f;
        private volatile float wtU3 = 0f, wtI3 = 0f, wtP3 = 0f, wtS3 = 0f, wtQ3 = 0f, wtPF3 = 1f, wtPhi3 = 0f;
        private volatile float wtSSig = 0f, wtQSig = 0f, wtPFSig = 1f, wtPhiSig = 0f;

        // KEB 雙載台快照快取 (Thread-Safe Snapshot)
        private class KebRuSnapshot
        {
            public int? ru07, ru12, ru15, ru11, ru43, ru09, ru18, ru20, ru00;
            public int node;
            public KebRuSnapshot(int? r07, int? r12, int? r15, int? r11, int? r43, int? r09, int? r18, int? r20, int? r00, int n)
            {
                ru07 = r07; ru12 = r12; ru15 = r15; ru11 = r11; ru43 = r43; ru09 = r09; ru18 = r18; ru20 = r20; ru00 = r00; node = n;
            }
        }
        private volatile KebRuSnapshot hmiKebRu1_Cache = null;
        private volatile KebRuSnapshot hmiKebRu2_Cache = null;

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= 0x02000000; // WS_EX_COMPOSITED (全視窗與子控制項硬體加速雙重緩衝)
                return cp;
            }
        }

        private void StartBackgroundWorker()
        {
            if (isWorkerRunning) return;
            isWorkerRunning = true;
            telemetryWorkerThread = new Thread(BackgroundTelemetryLoop);
            telemetryWorkerThread.IsBackground = true;
            telemetryWorkerThread.Priority = ThreadPriority.AboveNormal;
            telemetryWorkerThread.Start();
        }

        private void StopBackgroundWorker()
        {
            isWorkerRunning = false;
            try { if (isManualRecording) StopManualRecording(showPrompt: false); } catch { }
            try
            {
                if (telemetryWorkerThread != null && telemetryWorkerThread.IsAlive)
                {
                    if (!telemetryWorkerThread.Join(400))
                    {
                        try { telemetryWorkerThread.Abort(); } catch { }
                    }
                }
            }
            catch { }
            telemetryWorkerThread = null;
        }

        private void BackgroundTelemetryLoop()
        {
            while (isWorkerRunning)
            {
                try
                {
                    // 1. Kistler 4700B 扭力計
                    if (spTorque != null && spTorque.IsOpen)
                        {
                            try
                            {
                                spTorque.DiscardInBuffer();
                                byte[] cmd = Encoding.ASCII.GetBytes("MENU:DISP?\r\n");
                                spTorque.Write(cmd, 0, cmd.Length);
                                Thread.Sleep(80);
                                if (spTorque.BytesToRead > 0)
                                {
                                    string raw = spTorque.ReadExisting().Trim();
                                    lastRawKistler = raw;
                                    string decoded = DecodeHexToAscii(raw);

                                    Match mTorq = Regex.Match(decoded, @"Torque\s+([-\+]?\d+(\.\d+)?)");
                                    Match mSpd = Regex.Match(decoded, @"Speed\s+([-\+]?\d+(\.\d+)?)");
                                    Match mPwr = Regex.Match(decoded, @"Power\s+([-\+]?\d+(\.\d+)?)");

                                    if (mTorq.Success)
                                    {
                                        double.TryParse(mTorq.Groups[1].Value, out actTorque);
                                        actTorque *= scaleTorque; // 套用使用者自訂扭力校正比例係數
                                        lastTorquePacketTime = DateTime.Now;
                                    }
                                    if (mSpd.Success)
                                    {
                                        double.TryParse(mSpd.Groups[1].Value, out actSpeed);
                                        lastTorquePacketTime = DateTime.Now;
                                    }
                                    if (mPwr.Success) double.TryParse(mPwr.Groups[1].Value, out actMechPower);
                                }
                            }
                            catch { }
                        }

                        // 2. 橫河 WT333E (Modbus TCP 502 埠 FC04 輸入暫存器 - 官方 100/200/300/400 十進位分區讀取)
                        if (streamPower != null && tcpPower != null && tcpPower.Connected)
                        {
                            try
                            {
                                byte[] rawE1 = SendModbusReadBlock(streamPower, 100, 18);
                                byte[] rawE2 = SendModbusReadBlock(streamPower, 200, 14);
                                byte[] rawE3 = SendModbusReadBlock(streamPower, 300, 14);
                                byte[] rawSig = SendModbusReadBlock(streamPower, 400, 14);

                                if (rawE1 != null && rawE1.Length >= 9 + 12)
                                {
                                    lastRawWt333eHex = BitConverter.ToString(rawE1, 0, Math.Min(rawE1.Length, 32)).Replace("-", " ");
                                    int baseOffset = 9;

                                    float raw_u1 = SanitizeFloat(ParseModbusFloat(rawE1, baseOffset + 0), 0f, 1500f);
                                    float raw_i1 = SanitizeFloat(ParseModbusFloat(rawE1, baseOffset + 4), -500f, 500f);
                                    float raw_p1 = ParseModbusFloat(rawE1, baseOffset + 8);
                                    float raw_s1 = (rawE1.Length >= baseOffset + 16) ? ParseModbusFloat(rawE1, baseOffset + 12) : (raw_u1 * Math.Abs(raw_i1) / 1000f);
                                    float raw_q1 = (rawE1.Length >= baseOffset + 20) ? ParseModbusFloat(rawE1, baseOffset + 16) : 0f;
                                    float raw_pf1 = (rawE1.Length >= baseOffset + 24) ? ParseModbusFloat(rawE1, baseOffset + 20) : 1f;
                                    float raw_phi1 = (rawE1.Length >= baseOffset + 28) ? ParseModbusFloat(rawE1, baseOffset + 24) : 0f;
                                    float raw_freqU = (rawE1.Length >= baseOffset + 32) ? ParseModbusFloat(rawE1, baseOffset + 28) : 0f;
                                    float raw_freqI = (rawE1.Length >= baseOffset + 36) ? ParseModbusFloat(rawE1, baseOffset + 32) : 0f;

                                    float raw_u2 = 0f, raw_i2 = 0f, raw_p2 = 0f, raw_s2 = 0f, raw_q2 = 0f, raw_pf2 = 1f, raw_phi2 = 0f;
                                    if (rawE2 != null && rawE2.Length >= baseOffset + 12)
                                    {
                                        raw_u2 = SanitizeFloat(ParseModbusFloat(rawE2, baseOffset + 0), 0f, 1500f);
                                        raw_i2 = SanitizeFloat(ParseModbusFloat(rawE2, baseOffset + 4), -500f, 500f);
                                        raw_p2 = ParseModbusFloat(rawE2, baseOffset + 8);
                                        raw_s2 = (rawE2.Length >= baseOffset + 16) ? ParseModbusFloat(rawE2, baseOffset + 12) : (raw_u2 * Math.Abs(raw_i2) / 1000f);
                                        raw_q2 = (rawE2.Length >= baseOffset + 20) ? ParseModbusFloat(rawE2, baseOffset + 16) : 0f;
                                        raw_pf2 = (rawE2.Length >= baseOffset + 24) ? ParseModbusFloat(rawE2, baseOffset + 20) : 1f;
                                        raw_phi2 = (rawE2.Length >= baseOffset + 28) ? ParseModbusFloat(rawE2, baseOffset + 24) : 0f;
                                    }

                                    float raw_u3 = 0f, raw_i3 = 0f, raw_p3 = 0f, raw_s3 = 0f, raw_q3 = 0f, raw_pf3 = 1f, raw_phi3 = 0f;
                                    if (rawE3 != null && rawE3.Length >= baseOffset + 12)
                                    {
                                        raw_u3 = SanitizeFloat(ParseModbusFloat(rawE3, baseOffset + 0), 0f, 1500f);
                                        raw_i3 = SanitizeFloat(ParseModbusFloat(rawE3, baseOffset + 4), -500f, 500f);
                                        raw_p3 = ParseModbusFloat(rawE3, baseOffset + 8);
                                        raw_s3 = (rawE3.Length >= baseOffset + 16) ? ParseModbusFloat(rawE3, baseOffset + 12) : (raw_u3 * Math.Abs(raw_i3) / 1000f);
                                        raw_q3 = (rawE3.Length >= baseOffset + 20) ? ParseModbusFloat(rawE3, baseOffset + 16) : 0f;
                                        raw_pf3 = (rawE3.Length >= baseOffset + 24) ? ParseModbusFloat(rawE3, baseOffset + 20) : 1f;
                                        raw_phi3 = (rawE3.Length >= baseOffset + 28) ? ParseModbusFloat(rawE3, baseOffset + 24) : 0f;
                                    }

                                    float raw_uSig = 0f, raw_iSig = 0f, raw_pSig = 0f, raw_sSig = 0f, raw_qSig = 0f, raw_pfSig = 1f, raw_phiSig = 0f;
                                    if (rawSig != null && rawSig.Length >= baseOffset + 12)
                                    {
                                        // SIGMA (Reg 400): 14 regs = 7 floats = USig, ISig, PSig, SSig, QSig, PFSig, PhiSig
                                        // 對齊 WT333E_Tester_GUI.cs QueryFullPowerData() fs[0..6] 映射
                                        raw_uSig   = SanitizeFloat(ParseModbusFloat(rawSig, baseOffset + 0),  0f, 1500f);
                                        raw_iSig   = SanitizeFloat(ParseModbusFloat(rawSig, baseOffset + 4),  -500f, 500f);
                                        raw_pSig   = ParseModbusFloat(rawSig, baseOffset + 8);
                                        if (rawSig.Length >= baseOffset + 16)
                                            raw_sSig  = ParseModbusFloat(rawSig, baseOffset + 12); // SSig
                                        if (rawSig.Length >= baseOffset + 20)
                                            raw_qSig  = ParseModbusFloat(rawSig, baseOffset + 16); // QSig
                                        if (rawSig.Length >= baseOffset + 24)
                                            raw_pfSig = ParseModbusFloat(rawSig, baseOffset + 20); // PFSig
                                        if (rawSig.Length >= baseOffset + 28)
                                            raw_phiSig = ParseModbusFloat(rawSig, baseOffset + 24); // PhiSig
                                    }

                                    // 功率/視在功率/無功功率單位正規化 (W/VA/var -> kW/kVA/kvar)
                                    // 對齊 WT333E_Tester_GUI.cs NormalizePower() 邏輯：|val| >= 50 -> /1000
                                    if (Math.Abs(raw_p1)   >= 50f) raw_p1   /= 1000f;
                                    if (Math.Abs(raw_p2)   >= 50f) raw_p2   /= 1000f;
                                    if (Math.Abs(raw_p3)   >= 50f) raw_p3   /= 1000f;
                                    if (Math.Abs(raw_pSig) >= 50f) raw_pSig /= 1000f;
                                    if (Math.Abs(raw_s1)   >= 50f) raw_s1   /= 1000f;
                                    if (Math.Abs(raw_s2)   >= 50f) raw_s2   /= 1000f;
                                    if (Math.Abs(raw_s3)   >= 50f) raw_s3   /= 1000f;
                                    if (Math.Abs(raw_sSig) >= 50f) raw_sSig /= 1000f;
                                    if (Math.Abs(raw_q1)   >= 50f) raw_q1   /= 1000f;
                                    if (Math.Abs(raw_q2)   >= 50f) raw_q2   /= 1000f;
                                    if (Math.Abs(raw_q3)   >= 50f) raw_q3   /= 1000f;
                                    if (Math.Abs(raw_qSig) >= 50f) raw_qSig /= 1000f;

                                    // 套用使用者校正比例微調係數 (Scale Multiplier)
                                    float u1 = raw_u1 * (float)scaleU1;
                                    float u2 = raw_u2 * (float)scaleU2;
                                    float u3 = raw_u3 * (float)scaleU3;
                                    float i1 = raw_i1 * (float)scaleI1;
                                    float i2 = raw_i2 * (float)scaleI2;
                                    float i3 = raw_i3 * (float)scaleI3;

                                    float uSigma = (raw_uSig > 1f) ? raw_uSig : ((u2 > 1f && u3 > 1f) ? ((u1 + u2 + u3) / 3.0f) : u1);
                                    float iSigma = (raw_iSig > 0.05f) ? raw_iSig : ((i2 > 0.05f && i3 > 0.05f) ? ((i1 + i2 + i3) / 3.0f) : i1);
                                    double pSigma = (Math.Abs(raw_pSig) > 0.0001f) ? (double)raw_pSig : ((raw_p2 != 0f || raw_p3 != 0f) ? (raw_p1 + raw_p2 + raw_p3) : raw_p1);
                                    double realPf = (Math.Abs(raw_pfSig) > 0.001f && Math.Abs(raw_pfSig) <= 1.0f) ? (double)raw_pfSig : (double)raw_pf1;

                                    actVoltageSigma = SanitizeFloat(uSigma, 0f, 1500f);
                                    actCurrentSigma = SanitizeFloat(iSigma, 0f, 500f);
                                    actElecPower = pSigma;
                                    actPf = realPf;

                                    // WT333E 全相快取更新 (對齊 WT333E_Tester_GUI UpdateTableUI 8-row 矩陣)
                                    wtU1 = u1;       wtI1 = i1;       wtP1 = raw_p1;
                                    wtS1 = raw_s1;   wtQ1 = raw_q1;   wtPF1 = raw_pf1;   wtPhi1 = raw_phi1;
                                    wtFreqU = raw_freqU; wtFreqI = raw_freqI;
                                    wtU2 = u2;       wtI2 = i2;       wtP2 = raw_p2;
                                    wtS2 = raw_s2;   wtQ2 = raw_q2;   wtPF2 = raw_pf2;   wtPhi2 = raw_phi2;
                                    wtU3 = u3;       wtI3 = i3;       wtP3 = raw_p3;
                                    wtS3 = raw_s3;   wtQ3 = raw_q3;   wtPF3 = raw_pf3;   wtPhi3 = raw_phi3;
                                    wtSSig = raw_sSig; wtQSig = raw_qSig; wtPFSig = raw_pfSig; wtPhiSig = raw_phiSig;
                                }
                            }
                            catch { }
                        }
                        else if (ykDeviceId >= 0)
                        {
                            try
                            {
                                TmcSend(ykDeviceId, ":NUMeric:NORMal:VALue?");
                                StringBuilder sb = new StringBuilder(4096);
                                int readLen = 0;
                                int ret = TmcReceive(ykDeviceId, sb, 4096, ref readLen);
                                if (ret == 0 && sb.Length > 0)
                                {
                                    string[] tokens = sb.ToString().Split(new char[] { ',', ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                                    if (tokens.Length >= 4)
                                    {
                                        actVoltageSigma = ParseSci(tokens[0]);
                                        actCurrentSigma = ParseSci(tokens[1]);
                                        actElecPower = ParseSci(tokens[2]);
                                        actPf = ParseSci(tokens[3]);
                                        if (actElecPower > 1000.0) actElecPower /= 1000.0;
                                    }
                                    if (tokens.Length >= 8)
                                    {
                                        wtFreqU = (float)ParseSci(tokens[7]);
                                    }
                                    if (tokens.Length >= 9)
                                    {
                                        wtFreqI = (float)ParseSci(tokens[8]);
                                    }
                                }
                            }
                            catch { }
                        }

                        // 3. Graphtec GL820 溫度記錄器
                        if (tcpGbd != null && tcpGbd.Connected && streamGbd != null)
                        {
                            try
                            {
                                byte[] cmd = Encoding.ASCII.GetBytes(":MEAS:OUTP:ONE?\r\n");
                                streamGbd.Write(cmd, 0, cmd.Length);
                                Thread.Sleep(50);
                                if (!streamGbd.DataAvailable) Thread.Sleep(30);
                                if (streamGbd.DataAvailable)
                                {
                                    byte[] buf = new byte[2048];
                                    int r = streamGbd.Read(buf, 0, buf.Length);
                                    int hashIdx = -1;
                                    for (int i = 0; i < r; i++) { if (buf[i] == (byte)'#') { hashIdx = i; break; } }
                                    if (hashIdx >= 0 && hashIdx + 2 < r)
                                    {
                                        int numDigits = buf[hashIdx + 1] - '0';
                                        if (numDigits > 0 && hashIdx + 2 + numDigits <= r)
                                        {
                                            int dataStart = hashIdx + 2 + numDigits;
                                            int totalBytes = r - dataStart;
                                            int numChannels = Math.Min(totalBytes / 2, 20);
                                            for (int ch = 0; ch < numChannels; ch++)
                                            {
                                                int idx = dataStart + ch * 2;
                                                if (idx + 1 < r)
                                                {
                                                    short rawShort = (short)((buf[idx] << 8) | buf[idx + 1]);
                                                    double t = rawShort * 0.1;
                                                    if (rawShort != 0x7FFF && rawShort != -32768 && Math.Abs(t) < 500.0)
                                                    {
                                                        gbdChTemps[ch] = t;
                                                    }
                                                    else
                                                    {
                                                        gbdChTemps[ch] = 0.0;
                                                    }
                                                }
                                            }
                                            for (int ch = numChannels; ch < 20; ch++)
                                            {
                                                gbdChTemps[ch] = 0.0;
                                            }

                                            // ★ 智慧實測通道偵測：若剛連線尚未偵測，且收到穩定有效數據，自動配置有效通道
                                            if (!hasAutoDetectedGl820Channels)
                                            {
                                                bool[] active = DetectActiveGbdChannels();
                                                if (active.Any(b => b))
                                                {
                                                    gbdAutoDetectCountdown--;
                                                    if (gbdAutoDetectCountdown <= 0)
                                                    {
                                                        hasAutoDetectedGl820Channels = true;
                                                        if (this != null && !this.IsDisposed)
                                                        {
                                                            this.BeginInvoke(new Action(() => {
                                                                ApplyDetectedGbdChannels(active);
                                                            }));
                                                        }
                                                    }
                                                }
                                            }
                                        }
                                    }
                                }
                            }
                            catch { }
                        }

                        // --- 效率計算 (Efficiency Calculation) ---
                        // 物理定義：效率 = |輸出功率| / |輸入功率| × 100%
                        // 正向驅動：電功率輸入 > 0，機械功率輸出 > 0 → η = |Pmech| / |Pelec|
                        // 回生制動：機械功率為負（吸收），電功率回饋為負 → η = |Pelec| / |Pmech|
                        {
                            double absMech = Math.Abs(actMechPower);
                            double absElec = Math.Abs(actElecPower);
                            bool isMotorMode = actElecPower >= 0.0;   // 電→機 (正向驅動)
                            bool isRegenMode = actElecPower < -0.01;  // 機→電 (回生制動)

                            if (absElec > 0.01 && absMech > 0.001)
                            {
                                if (isMotorMode)
                                {
                                    // 正向驅動：η = Pmech / Pelec (機械輸出 / 電力輸入)
                                    actEfficiency = Math.Min(99.0, (absMech / absElec) * 100.0);
                                }
                                else if (isRegenMode)
                                {
                                    // 回生制動：η = |Pelec| / |Pmech| (電力回收 / 機械輸入)
                                    actEfficiency = Math.Min(99.0, (absElec / absMech) * 100.0);
                                }
                                else
                                {
                                    actEfficiency = 0.0;
                                }
                            }
                            else
                            {
                                actEfficiency = 0.0; // 功率過小（空轉 / 無負載）
                            }
                        }
                        actKt = (actCurrentSigma > 0.1 && actTorque > 0) ? actTorque / actCurrentSigma : 0.0;

                        // 4. KEB 雙載台非同步輪詢調度 (以 500ms ~ 1000ms 穩健節奏在背景輪流執行)
                        int nowTick = Environment.TickCount;
                        if (isHmiKebOpen1 && (nowTick - lastKebPollTime1 >= kebPollingIntervalMs || lastKebPollTime1 == 0))
                        {
                            lastKebPollTime1 = nowTick;
                            try { DoHmiKebQuery1(); } catch { }
                        }
                        if (isHmiKebOpen2 && (nowTick - lastKebPollTime2 >= kebPollingIntervalMs || lastKebPollTime2 == 0))
                        {
                            lastKebPollTime2 = nowTick;
                            try { DoHmiKebQuery2(); } catch { }
                        }

                    // -------------------------------------------------------------
                    // 雙軌日誌儲存引擎：採樣累積與算術平均 (Arithmetic Average) 存檔
                    // -------------------------------------------------------------
                    autoSampleAccumulator.AddSample(
                        actSpeed, actFrequency, actTorque, actMechPower, actElecPower, actEfficiency, actKt,
                        wtU1, wtI1, wtP1,
                        wtU2, wtI2, wtP2,
                        wtU3, wtI3, wtP3,
                        actVoltageSigma, actCurrentSigma, actPf, actTemp,
                        gbdChTemps, lastRawKebA, lastRawKebB);

                    if ((DateTime.Now - lastAutoRecordWriteTime).TotalMilliseconds >= 1000)
                    {
                        WriteAutoRawTelemetryCsv();
                        lastAutoRecordWriteTime = DateTime.Now;
                        autoSampleAccumulator.Clear();
                    }

                    if (isManualRecording && manualRecordWriter != null)
                    {
                        manualSampleAccumulator.AddSample(
                            actSpeed, actFrequency, actTorque, actMechPower, actElecPower, actEfficiency, actKt,
                            wtU1, wtI1, wtP1,
                            wtU2, wtI2, wtP2,
                            wtU3, wtI3, wtP3,
                            actVoltageSigma, actCurrentSigma, actPf, actTemp,
                            gbdChTemps, lastRawKebA, lastRawKebB);

                        if ((DateTime.Now - lastManualRecordWriteTime).TotalMilliseconds >= rawDataIntervalMs)
                        {
                            WriteManualRawTelemetryRow();
                            lastManualRecordWriteTime = DateTime.Now;
                            manualSampleAccumulator.Clear();
                        }
                    }

                    // 頻率診斷比對 LOG (依指示：同時撈取 POWERMETER 與 KEB RU 參數比對，暫存於 LOG 觀察)
                    CheckAndLogFrequencyComparison("TELEMETRY");
                }
                catch { }

                // 根據統一設定的週期進行精準休眠 (支援手動即時調整)
                int sleep = pollingIntervalMs;
                if (sleep < 10) sleep = 10;
                Thread.Sleep(sleep);
            }
        }

        // =========================================================================
        // UI 定時器刷新 (極速輕量化，耗時 < 1ms，保證 60 FPS 流暢度)
        // =========================================================================
        private void UpdateDeviceStatusPill(Label pill, string devName, string devPort, bool isOnline)
        {
            if (pill == null || pill.IsDisposed) return;
            if (isOnline)
            {
                pill.Text = string.Format("{0}: {1} [正常]", devName, devPort);
                pill.ForeColor = Color.FromArgb(74, 222, 128);
                pill.BackColor = Color.FromArgb(15, 60, 30);
            }
            else
            {
                pill.Text = string.Format("{0}: {1} [斷線]", devName, devPort);
                pill.ForeColor = Color.FromArgb(248, 113, 113);
                pill.BackColor = Color.FromArgb(80, 20, 20);
            }
        }

        private void UpdateSafetyInterlock(bool isTorqueOnline, bool isPowerOnline)
        {
            bool canOperate = isTorqueOnline && isPowerOnline;

            foreach (Control c in listKebSafetyLockControls)
            {
                if (c != null && !c.IsDisposed && c.Enabled != canOperate)
                {
                    c.Enabled = canOperate;
                }
            }

            if (btnStartTn != null && !btnStartTn.IsDisposed && btnStartTn.Enabled != canOperate)
            {
                btnStartTn.Enabled = canOperate;
            }
            if (btnStartDuty != null && !btnStartDuty.IsDisposed && btnStartDuty.Enabled != canOperate)
            {
                btnStartDuty.Enabled = canOperate;
            }

            if (lblSafetyStatus != null && !lblSafetyStatus.IsDisposed)
            {
                if (canOperate)
                {
                    lblSafetyStatus.Text = "[安全就緒] 載台允許操作";
                    lblSafetyStatus.ForeColor = Color.FromArgb(74, 222, 128);
                }
                else
                {
                    string reason = (!isTorqueOnline && !isPowerOnline) ? "扭力計與WT333E未連線" : (!isTorqueOnline ? "扭力計未連線" : "WT333E未連線");
                    lblSafetyStatus.Text = string.Format("[安全互鎖] {0} - 載台強制鎖定", reason);
                    lblSafetyStatus.ForeColor = Color.FromArgb(239, 68, 68);
                }
            }
        }

        private void MainTimer_Tick(object sender, EventArgs e)
        {
            DateTime now = DateTime.Now;

            // 1. 計算 UI 執行緒訊息排程反應抖動 (UI Thread Dispatch Lag / Jitter)
            if (lastMainTickTime != DateTime.MinValue)
            {
                double tickDelta = (now - lastMainTickTime).TotalMilliseconds;
                int expectedInterval = (mainTimer != null && mainTimer.Interval > 0) ? mainTimer.Interval : 500;
                healthUiLagMs = Math.Max(0, (int)(tickDelta - expectedInterval));
            }
            lastMainTickTime = now;

            // 2. 週期性採樣系統健康與資源指標 (每 2 秒採樣一次，零開銷)
            healthTickCounter++;
            if (healthTickCounter % 4 == 0)
            {
                try
                {
                    using (Process curProc = Process.GetCurrentProcess())
                    {
                        IntPtr hProc = curProc.Handle;
                        healthGdiCount = GetGuiResources(hProc, 0); // 0 = GR_GDIOBJECTS (上限 10,000)
                        healthUserCount = GetGuiResources(hProc, 1); // 1 = GR_USEROBJECTS (上限 10,000)
                        curProc.Refresh();
                        healthWorkingSetMb = curProc.WorkingSet64 / (1024 * 1024);
                        healthPrivateBytesMb = curProc.PrivateMemorySize64 / (1024 * 1024);
                        healthGcHeapMb = GC.GetTotalMemory(false) / (1024 * 1024);
                        healthThreadCount = curProc.Threads.Count;
                    }

                    if (lblSystemHealth != null && !lblSystemHealth.IsDisposed)
                    {
                        lblSystemHealth.Text = string.Format("資源: GDI {0} | RAM {1}M(GC {2}M) | 緒 {3} | 延 {4}ms",
                            healthGdiCount, healthWorkingSetMb, healthGcHeapMb, healthThreadCount, healthUiLagMs);

                        // 智慧預警燈號 (GDI > 3000 或 RAM > 600MB 或 延遲 > 300ms 預警黃燈；GDI > 7000 或 RAM > 1200MB 危險紅燈)
                        if (healthGdiCount > 7000 || healthWorkingSetMb > 1200 || healthUiLagMs > 1000)
                        {
                            lblSystemHealth.ForeColor = Color.FromArgb(248, 113, 113);
                            lblSystemHealth.BackColor = Color.FromArgb(80, 20, 20);
                        }
                        else if (healthGdiCount > 3000 || healthWorkingSetMb > 600 || healthUiLagMs > 300)
                        {
                            lblSystemHealth.ForeColor = Color.FromArgb(251, 191, 36);
                            lblSystemHealth.BackColor = Color.FromArgb(60, 50, 15);
                        }
                        else
                        {
                            lblSystemHealth.ForeColor = Color.FromArgb(56, 189, 248);
                            lblSystemHealth.BackColor = Color.FromArgb(15, 35, 55);
                        }
                    }
                }
                catch { }

                // 每 60 秒定期輸出一次結構化健康指標至單一整合日誌
                if ((now - lastHealthLogTime).TotalSeconds >= 60)
                {
                    lastHealthLogTime = now;
                    WriteHmiLog("HEALTH", string.Format("系統資源遙測: GDI={0}/10000, USER={1}, RAM={2}MB, PrivateBytes={3}MB, GC={4}MB, Threads={5}, UiLag={6}ms, HandledErr={7}",
                        healthGdiCount, healthUserCount, healthWorkingSetMb, healthPrivateBytesMb, healthGcHeapMb, healthThreadCount, healthUiLagMs, healthHandledErrorsCount));
                }
            }

            // 設備連線狀態指示與安全互鎖檢查 (純實體硬體狀態)
            bool isTorqueOnline = (spTorque != null && spTorque.IsOpen);
            bool isPowerOnline = (tcpPower != null && tcpPower.Connected) || ykDeviceId >= 0;
            bool isGbdOnline = (tcpGbd != null && tcpGbd.Connected);
            bool isKeb1Online = isHmiKebOpen1;
            bool isKeb2Online = isHmiKebOpen2;

            if (isGbdOnline)
            {
                int targetCh = (cmbMotorTempCh != null && cmbMotorTempCh.SelectedIndex >= 0) ? cmbMotorTempCh.SelectedIndex : 0;
                actTemp = (targetCh >= 0 && targetCh < 20) ? gbdChTemps[targetCh] : 0.0;
            }
            else
            {
                actTemp = 0.0;
            }

            string kebP1 = (cmbHmiKebPort1 != null && cmbHmiKebPort1.SelectedItem != null) ? cmbHmiKebPort1.SelectedItem.ToString() : "COM1";
            string kebP2 = (cmbHmiKebPort2 != null && cmbHmiKebPort2.SelectedItem != null) ? cmbHmiKebPort2.SelectedItem.ToString() : "COM2";
            UpdateDeviceStatusPill(lblPillTorque, "扭力計", torquePortName, isTorqueOnline);
            UpdateDeviceStatusPill(lblPillPowerMeter, "WT333E", powerMeterPort.ToString(), isPowerOnline);
            UpdateDeviceStatusPill(lblPillGbd, "GL820", gbdPort.ToString(), isGbdOnline);
            UpdateDeviceStatusPill(lblPillKeb1, "A載台", kebP1, isKeb1Online);
            UpdateDeviceStatusPill(lblPillKeb2, "B載台", kebP2, isKeb2Online);

            UpdateSafetyInterlock(isTorqueOnline, isPowerOnline);

            // 實體模式斷線安全聯鎖監控 (Fail-Safe Interlock)
            bool isSim = (chkSimMode != null && chkSimMode.Checked);
            if (isClosedLoopTracking && !isSim)
            {
                if (spTorque == null || !spTorque.IsOpen)
                {
                    TriggerDisconnectFailSafe("Kistler 扭力計");
                }
                else if ((tcpPower == null || !tcpPower.Connected) && ykDeviceId < 0)
                {
                    TriggerDisconnectFailSafe("WT333E 功率分析儀");
                }
            }

            // 全自動安全保護矩陣守護巡邏 (扭力計斷線/堵轉/超溫/過電流)
            CheckSafetyProtectionMatrix();

            // 1. 滑動平均窗口平滑濾波 (Moving Average Filtering - 轉矩與轉速雙通道)
            int filterWindowMs = (trackingFilterWindowMs > 0) ? trackingFilterWindowMs : 500;
            torqueFilterQueue.Enqueue(new KeyValuePair<DateTime, double>(now, actTorque));
            speedFilterQueue.Enqueue(new KeyValuePair<DateTime, double>(now, actSpeed));
            DateTime cutoff = now.AddMilliseconds(-filterWindowMs);
            while (torqueFilterQueue.Count > 0 && torqueFilterQueue.Peek().Key < cutoff)
            {
                torqueFilterQueue.Dequeue();
            }
            while (speedFilterQueue.Count > 0 && speedFilterQueue.Peek().Key < cutoff)
            {
                speedFilterQueue.Dequeue();
            }

            double sum = 0.0;
            foreach (var item in torqueFilterQueue) sum += item.Value;
            smoothedTorque = torqueFilterQueue.Count > 0 ? (sum / torqueFilterQueue.Count) : actTorque;

            double sumSpd = 0.0;
            foreach (var item in speedFilterQueue) sumSpd += item.Value;
            smoothedSpeed = speedFilterQueue.Count > 0 ? (sumSpd / speedFilterQueue.Count) : actSpeed;

            if (lblSmoothTorqueDisp != null)
            {
                lblSmoothTorqueDisp.Text = string.Format("{0:F2} Nm", smoothedTorque);
            }

            // 2-A. 加載端轉矩平滑閉迴路追隨 (Torque Lock)
            double tgtTorque = (numCardTargetTorque != null) ? (double)numCardTargetTorque.Value : 5.0;
            if (isClosedLoopTracking && hasBaseline)
            {
                int ctrlIntervalMs = (trackingControlIntervalMs > 0) ? trackingControlIntervalMs : 200;
                if ((now - lastClosedLoopActionTime).TotalMilliseconds >= ctrlIntervalMs)
                {
                    lastClosedLoopActionTime = now;
                    double error = baselineTorque - smoothedTorque;
                    double deadband = (double)trackingDeadband;

                    // 動態判定哪一側為加載端 (模式 8 或 10)
                    string trqDrive = (currentKebMode1 == 8 || currentKebMode1 == 10) ? "A載台" : ((currentKebMode2 == 8 || currentKebMode2 == 10) ? "B載台" : "無加載端");
                    NumericUpDown numTrqCtl = (trqDrive == "A載台") ? numHmiKebTorque1 : ((trqDrive == "B載台") ? numHmiKebTorque2 : null);
                    int comIdxTrq = (trqDrive == "A載台") ? GetHmiKebComIdx(1) : GetHmiKebComIdx(2);
                    int baudIdxTrq = (trqDrive == "A載台") ? GetHmiKebBaudIdx(1) : GetHmiKebBaudIdx(2);
                    int nodeTrq = (trqDrive == "A載台") ? (int)numHmiKebNode1.Value : (int)numHmiKebNode2.Value;

                    if (numTrqCtl != null)
                    {
                        if (Math.Abs(error) > deadband)
                        {
                            double stepPct = (double)trackingMaxDelta;
                            double stepDelta = Math.Sign(error) * Math.Max(0.1, Math.Min(Math.Abs(error) * 0.1, stepPct));
                            decimal newVal = Math.Max(0, Math.Min(150, activeTrackingTorquePct + (decimal)stepDelta));

                            if (newVal != activeTrackingTorquePct)
                            {
                                decimal oldVal = activeTrackingTorquePct;
                                activeTrackingTorquePct = newVal;
                                numTrqCtl.Value = newVal; // 同步回顯至畫面上 (僅作為狀態指示)
                                if (trqDrive == "A載台") EnsureHmiKebOpen1(); else EnsureHmiKebOpen2();
                                KebWriteParam32(comIdxTrq, baudIdxTrq, nodeTrq, 0x0F12, (int)Math.Round(newVal * 10), "轉矩平滑追隨 (cs18)");
                                WriteHmiLog("LOCK_DIAG", string.Format("【轉矩LOCK判斷】載台={0} | 目標={1:F2}Nm, 實測={2:F2}Nm (原始={3:F2}Nm) | 誤差={4:+0.00;-0.00}Nm (超出死區 ±{5:F2}Nm) | 判定: 實測偏{6}，需{7}加載 (步進 {8:+0.0;-0.0}%), 給定 {9:F1}% -> {10:F1}% (寫入 cs.18)",
                                    trqDrive, baselineTorque, smoothedTorque, actTorque, error, deadband,
                                    (error > 0 ? "低" : "高"), (error > 0 ? "【增加】" : "【減少】"), stepDelta, oldVal, newVal));
                            }
                        }
                        else if ((now - lastTorqueLogDiagTime).TotalSeconds >= 2.5)
                        {
                            lastTorqueLogDiagTime = now;
                            WriteHmiLog("LOCK_DIAG", string.Format("【轉矩LOCK判斷】載台={0} | 目標={1:F2}Nm, 實測={2:F2}Nm | 誤差={3:+0.00;-0.00}Nm (在死區 ±{4:F2}Nm 內) | 判定: 【穩定維持】保持當前加載 {5:F1}% 不調",
                                trqDrive, baselineTorque, smoothedTorque, error, deadband, activeTrackingTorquePct));
                        }
                    }
                }
            }

            // 2-B. 待測端轉速平滑閉迴路追隨 (Speed Lock - 自動補償感應馬達 V/F 轉差)
            if (isSpeedTracking && hasSpeedBaseline)
            {
                int ctrlIntervalMs = (trackingControlIntervalMs > 0) ? trackingControlIntervalMs : 200;
                if ((now - lastSpeedClosedLoopActionTime).TotalMilliseconds >= ctrlIntervalMs)
                {
                    lastSpeedClosedLoopActionTime = now;
                    double actAbsSpd = Math.Abs(smoothedSpeed);
                    double tgtAbsSpd = Math.Abs(baselineSpeed);
                    double spdError = tgtAbsSpd - actAbsSpd; // 速率低於目標時 spdError > 0 (升速補償)；速率高於目標時 spdError < 0 (降速)
                    double spdDeadband = (double)trackingSpeedDeadband;

                    // 動態判定哪一側為轉速驅動端 (模式 7 或 9)
                    string spdDrive = (currentKebMode2 == 7 || currentKebMode2 == 9) ? "B載台" : ((currentKebMode1 == 7 || currentKebMode1 == 9) ? "A載台" : "無驅動端");
                    NumericUpDown numSpdCtl = (spdDrive == "B載台") ? numHmiKebSpeed2 : ((spdDrive == "A載台") ? numHmiKebSpeed1 : null);
                    int comIdxSpd = (spdDrive == "B載台") ? GetHmiKebComIdx(2) : GetHmiKebComIdx(1);
                    int baudIdxSpd = (spdDrive == "B載台") ? GetHmiKebBaudIdx(2) : GetHmiKebBaudIdx(1);
                    int nodeSpd = (spdDrive == "B載台") ? (int)numHmiKebNode2.Value : (int)numHmiKebNode1.Value;

                    if (numSpdCtl != null)
                    {
                        if (Math.Abs(spdError) > spdDeadband)
                        {
                            double maxDelta = (double)trackingSpeedMaxDelta;
                            double stepDelta = Math.Sign(spdError) * Math.Max(1.0, Math.Min(Math.Abs(spdError) * 0.5, maxDelta));
                            decimal newSpd = Math.Max(0, Math.Min(6000, activeTrackingSpeedRpm + (decimal)stepDelta));

                            if (newSpd != activeTrackingSpeedRpm)
                            {
                                decimal oldSpd = activeTrackingSpeedRpm;
                                activeTrackingSpeedRpm = newSpd;
                                numSpdCtl.Value = newSpd; // 同步回顯至畫面上 (僅作為狀態指示)
                                if (spdDrive == "B載台") EnsureHmiKebOpen2(); else EnsureHmiKebOpen1();
                                KebWriteParam32(comIdxSpd, baudIdxSpd, nodeSpd, 0x0034, (int)newSpd, "轉速鎖轉差補償 (Sy.52)");
                                WriteHmiLog("LOCK_DIAG", string.Format("【轉速LOCK判斷】載台={0} | 目標速率={1:F1}rpm, 實測速率={2:F1}rpm (原始轉速={3:F1}rpm, 平滑={4:F1}rpm) | 速率偏差={5:+0.0;-0.0}rpm (超出死區 ±{6:F1}rpm) | 判定: 實測{7}，需{8} (步進 {9:+0.0;-0.0}rpm), 給定轉速 {10} -> {11} rpm (寫入 Sy.52)",
                                    spdDrive, tgtAbsSpd, actAbsSpd, actSpeed, smoothedSpeed, spdError, spdDeadband,
                                    (spdError > 0 ? "過慢" : "超速"), (spdError > 0 ? "【升速補償】" : "【降速調回】"), stepDelta, (int)oldSpd, (int)newSpd));
                            }
                        }
                        else if ((now - lastSpeedLogDiagTime).TotalSeconds >= 2.5)
                        {
                            lastSpeedLogDiagTime = now;
                            WriteHmiLog("LOCK_DIAG", string.Format("【轉速LOCK判斷】載台={0} | 目標速率={1:F1}rpm, 實測速率={2:F1}rpm (原始={3:F1}rpm) | 速率偏差={4:+0.0;-0.0}rpm (在死區 ±{5:F1}rpm 內) | 判定: 【精準鎖定】保持給定 {6} rpm 不調",
                                spdDrive, tgtAbsSpd, actAbsSpd, actSpeed, spdError, spdDeadband, (int)activeTrackingSpeedRpm));
                        }
                    }
                }
            }

            CheckSafetyProtection(tgtTorque, smoothedTorque);

            // 更新 6 大卡片
            if (lblSpeedVal != null) lblSpeedVal.Text = string.Format("{0:F1} rpm", actSpeed);
            if (lblTorqueVal != null) lblTorqueVal.Text = string.Format("{0:F2} Nm", actTorque);
            if (lblPowerVal != null) lblPowerVal.Text = string.Format("{0:F2} kW", actMechPower);
            if (lblElecPowerVal != null) lblElecPowerVal.Text = string.Format("{0:F2} kW", actElecPower);
            if (lblEffVal != null) lblEffVal.Text = string.Format("{0:F1} %", actEfficiency);
            if (lblKtVal != null) lblKtVal.Text = string.Format("{0:F2} Nm/A", actKt);
            if (lblTempVal != null) lblTempVal.Text = string.Format("{0:F1} °C", actTemp);

            // 更新電表網格 (8 大參數全面鏡像對齊)
            if (dgvTelemetry != null && dgvTelemetry.Rows.Count >= 8 && dgvTelemetry.Columns.Count >= 6)
            {
                // Row 0: 電壓
                dgvTelemetry.Rows[0].Cells[1].Value = string.Format("{0:F2} V", wtU1);
                dgvTelemetry.Rows[0].Cells[2].Value = string.Format("{0:F2} V", wtU2);
                dgvTelemetry.Rows[0].Cells[3].Value = string.Format("{0:F2} V", wtU3);
                dgvTelemetry.Rows[0].Cells[4].Value = string.Format("{0:F2} V", actVoltageSigma);
                dgvTelemetry.Rows[0].Cells[5].Value = "3P3W / 3P4W";

                // Row 1: 電流
                dgvTelemetry.Rows[1].Cells[1].Value = string.Format("{0:F3} A", wtI1);
                dgvTelemetry.Rows[1].Cells[2].Value = string.Format("{0:F3} A", wtI2);
                dgvTelemetry.Rows[1].Cells[3].Value = string.Format("{0:F3} A", wtI3);
                dgvTelemetry.Rows[1].Cells[4].Value = string.Format("{0:F3} A", actCurrentSigma);
                dgvTelemetry.Rows[1].Cells[5].Value = "--";

                // Row 2: 有功功率 P
                dgvTelemetry.Rows[2].Cells[1].Value = string.Format("{0:F3} kW", wtP1);
                dgvTelemetry.Rows[2].Cells[2].Value = string.Format("{0:F3} kW", wtP2);
                dgvTelemetry.Rows[2].Cells[3].Value = string.Format("{0:F3} kW", wtP3);
                dgvTelemetry.Rows[2].Cells[4].Value = string.Format("{0:F3} kW", actElecPower);
                dgvTelemetry.Rows[2].Cells[5].Value = "Active";

                // Row 3: 視在功率 S
                dgvTelemetry.Rows[3].Cells[1].Value = string.Format("{0:F3} kVA", wtS1);
                dgvTelemetry.Rows[3].Cells[2].Value = string.Format("{0:F3} kVA", wtS2);
                dgvTelemetry.Rows[3].Cells[3].Value = string.Format("{0:F3} kVA", wtS3);
                dgvTelemetry.Rows[3].Cells[4].Value = string.Format("{0:F3} kVA", wtSSig);
                dgvTelemetry.Rows[3].Cells[5].Value = "Apparent";

                // Row 4: 無功功率 Q
                dgvTelemetry.Rows[4].Cells[1].Value = string.Format("{0:F3} kvar", wtQ1);
                dgvTelemetry.Rows[4].Cells[2].Value = string.Format("{0:F3} kvar", wtQ2);
                dgvTelemetry.Rows[4].Cells[3].Value = string.Format("{0:F3} kvar", wtQ3);
                dgvTelemetry.Rows[4].Cells[4].Value = string.Format("{0:F3} kvar", wtQSig);
                dgvTelemetry.Rows[4].Cells[5].Value = "Reactive";

                // Row 5: 功率因數 PF
                dgvTelemetry.Rows[5].Cells[1].Value = string.Format("{0:F4}", wtPF1);
                dgvTelemetry.Rows[5].Cells[2].Value = string.Format("{0:F4}", wtPF2);
                dgvTelemetry.Rows[5].Cells[3].Value = string.Format("{0:F4}", wtPF3);
                dgvTelemetry.Rows[5].Cells[4].Value = string.Format("{0:F4}", actPf);
                dgvTelemetry.Rows[5].Cells[5].Value = actPf >= 0 ? "Lag (+)" : "Lead (-)";

                // Row 6: 相位角 Phi
                dgvTelemetry.Rows[6].Cells[1].Value = string.Format("{0:F1}°", wtPhi1);
                dgvTelemetry.Rows[6].Cells[2].Value = string.Format("{0:F1}°", wtPhi2);
                dgvTelemetry.Rows[6].Cells[3].Value = string.Format("{0:F1}°", wtPhi3);
                dgvTelemetry.Rows[6].Cells[4].Value = string.Format("{0:F1}°", wtPhiSig);
                dgvTelemetry.Rows[6].Cells[5].Value = "Phase";

                // Row 7: 頻率
                dgvTelemetry.Rows[7].Cells[1].Value = string.Format("{0:F2} Hz", wtFreqU);
                dgvTelemetry.Rows[7].Cells[2].Value = "--";
                dgvTelemetry.Rows[7].Cells[3].Value = "--";
                dgvTelemetry.Rows[7].Cells[4].Value = string.Format("{0:F2} Hz (I:{1:F1})", wtFreqU, wtFreqI);
                dgvTelemetry.Rows[7].Cells[5].Value = "Freq";
            }

            // 更新 KEB 變頻器右側數值網格 (由背景執行緒非同步快照極速更新)
            if (hmiKebRu1_Cache != null && dgvKebRu1 != null && dgvKebRu1.Rows.Count >= 10)
            {
                var c1 = hmiKebRu1_Cache;
                if (c1.ru07.HasValue) dgvKebRu1.Rows[0].Cells[1].Value = string.Format("{0:F1} rpm", c1.ru07.Value * 0.125);
                if (c1.ru12.HasValue) dgvKebRu1.Rows[1].Cells[1].Value = string.Format("{0:F2} Nm", c1.ru12.Value * 0.01);
                if (c1.ru15.HasValue) dgvKebRu1.Rows[2].Cells[1].Value = string.Format("{0:F2} A", c1.ru15.Value * 0.1);
                if (c1.ru11.HasValue) dgvKebRu1.Rows[3].Cells[1].Value = string.Format("{0:F2} Nm", c1.ru11.Value * 0.01);
                if (c1.ru43.HasValue)
                {
                    dgvKebRu1.Rows[4].Cells[1].Value = c1.ru43.Value == 0 ? "正常 (無異常)" : string.Format("故障 0x{0:X2}", c1.ru43.Value);
                    dgvKebRu1.Rows[4].Cells[1].Style.ForeColor = c1.ru43.Value == 0 ? Color.FromArgb(16, 185, 129) : Color.Red;
                }
                if (c1.ru09.HasValue) dgvKebRu1.Rows[5].Cells[1].Value = string.Format("{0:F1} V", c1.ru09.Value * 0.1);
                if (c1.ru18.HasValue) dgvKebRu1.Rows[6].Cells[1].Value = string.Format("{0:F0} V", (double)c1.ru18.Value);
                if (c1.ru20.HasValue) dgvKebRu1.Rows[7].Cells[1].Value = string.Format("{0:F1} °C", (double)c1.ru20.Value);
                if (c1.ru00.HasValue) dgvKebRu1.Rows[8].Cells[1].Value = string.Format("0x{0:X4}", c1.ru00.Value);
                dgvKebRu1.Rows[9].Cells[1].Value = "Node " + c1.node;
            }

            if (hmiKebRu2_Cache != null && dgvKebRu2 != null && dgvKebRu2.Rows.Count >= 10)
            {
                var c2 = hmiKebRu2_Cache;
                if (c2.ru07.HasValue) dgvKebRu2.Rows[0].Cells[1].Value = string.Format("{0:F1} rpm", c2.ru07.Value * 0.125);
                if (c2.ru12.HasValue) dgvKebRu2.Rows[1].Cells[1].Value = string.Format("{0:F2} Nm", c2.ru12.Value * 0.01);
                if (c2.ru15.HasValue) dgvKebRu2.Rows[2].Cells[1].Value = string.Format("{0:F2} A", c2.ru15.Value * 0.1);
                if (c2.ru11.HasValue) dgvKebRu2.Rows[3].Cells[1].Value = string.Format("{0:F2} Nm", c2.ru11.Value * 0.01);
                if (c2.ru43.HasValue)
                {
                    dgvKebRu2.Rows[4].Cells[1].Value = c2.ru43.Value == 0 ? "正常 (無異常)" : string.Format("故障 0x{0:X2}", c2.ru43.Value);
                    dgvKebRu2.Rows[4].Cells[1].Style.ForeColor = c2.ru43.Value == 0 ? Color.FromArgb(16, 185, 129) : Color.Red;
                }
                if (c2.ru09.HasValue) dgvKebRu2.Rows[5].Cells[1].Value = string.Format("{0:F1} V", c2.ru09.Value * 0.1);
                if (c2.ru18.HasValue) dgvKebRu2.Rows[6].Cells[1].Value = string.Format("{0:F0} V", (double)c2.ru18.Value);
                if (c2.ru20.HasValue) dgvKebRu2.Rows[7].Cells[1].Value = string.Format("{0:F1} °C", (double)c2.ru20.Value);
                if (c2.ru00.HasValue) dgvKebRu2.Rows[8].Cells[1].Value = string.Format("0x{0:X4}", c2.ru00.Value);
                dgvKebRu2.Rows[9].Cells[1].Value = "Node " + c2.node;
            }

            // 更新 Tab 5 GBD 表格 (單行橫向 20 通道)
            if (dgvGbdAll != null && dgvGbdAll.Rows.Count >= 2 && dgvGbdAll.Columns.Count >= 20)
            {
                int selCh = (cmbMotorTempCh != null && cmbMotorTempCh.SelectedIndex >= 0) ? cmbMotorTempCh.SelectedIndex : 0;
                for (int i = 0; i < 20; i++)
                {
                    if (!isGbdOnline)
                    {
                        dgvGbdAll.Rows[1].Cells[i].Value = "--.-";
                        dgvGbdAll.Rows[1].Cells[i].Style.ForeColor = Color.Gray;
                        dgvGbdAll.Rows[1].Cells[i].Style.BackColor = Color.FromArgb(248, 250, 252);
                    }
                    else
                    {
                        dgvGbdAll.Rows[1].Cells[i].Value = (gbdChTemps[i] > 0.0) ? string.Format("{0:F1}", gbdChTemps[i]) : "--.-";
                        if (i == selCh)
                        {
                            dgvGbdAll.Rows[1].Cells[i].Style.BackColor = Color.FromArgb(254, 243, 199);
                            dgvGbdAll.Rows[1].Cells[i].Style.ForeColor = Color.FromArgb(180, 83, 9);
                        }
                        else
                        {
                            dgvGbdAll.Rows[1].Cells[i].Style.BackColor = Color.White;
                            dgvGbdAll.Rows[1].Cells[i].Style.ForeColor = Color.FromArgb(15, 23, 42);
                        }
                    }
                }
            }

            // 右下角多功能工作台 - 轉矩與轉速即時動態曲線採樣 (轉為絕對值傳入，零負數區間，支援 S1/S2/S6/空載/TN/LOCK 全局目標)
            if (trqSpdChart != null && !trqSpdChart.IsDisposed)
            {
                double curTgtTorque = GetCurrentGlobalTargetTorque();
                double curTgtSpeed = GetCurrentGlobalTargetSpeed();
                bool isTracking = isClosedLoopTracking || isSpeedTracking || (dutyTimer != null && dutyTimer.Enabled) || isNoLoadRunning || (tnTimer != null && tnTimer.Enabled) || (effMapTimer != null && effMapTimer.Enabled);
                trqSpdChart.AddSample(now, Math.Abs(smoothedTorque), Math.Abs(curTgtTorque), Math.Abs(smoothedSpeed), Math.Abs(curTgtSpeed), isTracking);

                // 雙向連動：若處於自動測試期間且操作者未焦點於主卡片目標輸入框，同步更新卡片目標顯示
                if (isTracking)
                {
                    bool isDutyRunning = (dutyTimer != null && dutyTimer.Enabled);
                    if (isDutyRunning)
                    {
                        if (numDutySpeed != null && numCardTargetSpeed != null && !numCardTargetSpeed.Focused)
                        {
                            isSyncingDutyControls = true;
                            if (numCardTargetSpeed.Value != numDutySpeed.Value)
                                numCardTargetSpeed.Value = numDutySpeed.Value;
                            isSyncingDutyControls = false;
                        }
                        if (numDutyTorque != null && numCardTargetTorque != null && !numCardTargetTorque.Focused)
                        {
                            isSyncingDutyControls = true;
                            if (numCardTargetTorque.Value != numDutyTorque.Value)
                                numCardTargetTorque.Value = numDutyTorque.Value;
                            isSyncingDutyControls = false;
                        }
                    }
                    else
                    {
                        if (numCardTargetSpeed != null && !numCardTargetSpeed.Focused && curTgtSpeed > 0)
                        {
                            decimal dVal = (decimal)Math.Min((double)numCardTargetSpeed.Maximum, Math.Max((double)numCardTargetSpeed.Minimum, Math.Round(curTgtSpeed, 1)));
                            if (numCardTargetSpeed.Value != dVal) numCardTargetSpeed.Value = dVal;
                        }
                        if (numCardTargetTorque != null && !numCardTargetTorque.Focused && curTgtTorque >= 0)
                        {
                            decimal dVal = (decimal)Math.Min((double)numCardTargetTorque.Maximum, Math.Max((double)numCardTargetTorque.Minimum, Math.Round(curTgtTorque, 2)));
                            if (numCardTargetTorque.Value != dVal) numCardTargetTorque.Value = dVal;
                        }
                    }
                }
            }
            if (isRunning)
            {
                WriteHmiLog("TELEMETRY", string.Format("[A:{0} | B:{1}] Spd={2:F1}rpm, Torq={3:F2}Nm, SmoothTorq={4:F2}Nm, MechPwr={5:F2}kW, ElecPwr={6:F2}kW, Eff={7:F1}%, Volt={8:F1}V, Curr={9:F2}A, Temp={10:F1}C",
                    GetKebModeShortName(currentKebMode1), GetKebModeShortName(currentKebMode2),
                    actSpeed, actTorque, smoothedTorque, actMechPower, actElecPower, actEfficiency, actVoltageSigma, actCurrentSigma, actTemp));
            }
        }
        /// <summary>
        /// 獲取全系統當前目標轉速 (涵蓋 S1/S2/S6 工作制、空載溫升、TN 特性、效率圖譜、LOCK 平滑追隨與手動卡片設定)
        /// </summary>
        public double GetCurrentGlobalTargetSpeed()
        {
            // 1. 工作制 Duty (S1/S2/S6) - 嚴格回傳設定目標轉速
            if (dutyTimer != null && dutyTimer.Enabled)
            {
                return (numDutySpeed != null && numDutySpeed.Value > 0) ? (double)numDutySpeed.Value : 1500.0;
            }
            // 2. 空載溫升測試
            if (isNoLoadRunning)
            {
                return noLoadCurrentTargetSpd > 0 ? noLoadCurrentTargetSpd : ((numNoLoadRatedSpd != null) ? (double)numNoLoadRatedSpd.Value : 0.0);
            }
            // 3. TN 特性曲線測試
            if (tnTimer != null && tnTimer.Enabled)
            {
                return tnCurrentStep;
            }
            // 4. 效率圖譜測試
            if (effMapTimer != null && effMapTimer.Enabled)
            {
                if (effPointList != null && effCurrentPointIdx < effPointList.Count)
                    return effPointList[effCurrentPointIdx].TargetSpeed;
            }
            // 5. LOCK 轉速閉迴路平滑追隨
            if (hasSpeedBaseline && baselineSpeed > 0)
            {
                return baselineSpeed;
            }
            // 6. 主畫面卡片直接輸入之目標轉速
            if (numCardTargetSpeed != null && numCardTargetSpeed.Value > 0)
            {
                return (double)numCardTargetSpeed.Value;
            }
            return 0.0;
        }

        /// <summary>
        /// 獲取全系統當前目標轉矩 (涵蓋 S1/S2/S6 工作制、空載溫升、TN 特性、效率圖譜、LOCK 平滑追隨與手動卡片設定)
        /// </summary>
        public double GetCurrentGlobalTargetTorque()
        {
            // 1. 工作制 Duty (S1/S2/S6) - 嚴格回傳設定目標轉矩
            if (dutyTimer != null && dutyTimer.Enabled)
            {
                return (numDutyTorque != null && numDutyTorque.Value > 0) ? (double)numDutyTorque.Value : 15.0;
            }
            // 2. 空載溫升測試 (空載無加載端，目標轉矩為 0)
            if (isNoLoadRunning)
            {
                return 0.0;
            }
            // 3. TN 特性曲線測試
            if (tnTimer != null && tnTimer.Enabled)
            {
                return (numTnMiniTrq != null && numTnMiniTrq.Value > 0)
                    ? (double)numTnMiniTrq.Value
                    : ((numTnTorque != null && numTnTorque.Value > 0) ? (double)numTnTorque.Value : 15.0);
            }
            // 4. 效率圖譜測試
            if (effMapTimer != null && effMapTimer.Enabled)
            {
                if (effPointList != null && effCurrentPointIdx < effPointList.Count)
                    return effPointList[effCurrentPointIdx].TargetTorque;
            }
            // 5. LOCK 轉矩閉迴路平滑追隨
            if (hasBaseline && baselineTorque > 0)
            {
                return baselineTorque;
            }
            // 6. 主畫面卡片直接輸入之目標轉矩
            if (numCardTargetTorque != null && numCardTargetTorque.Value > 0)
            {
                return (double)numCardTargetTorque.Value;
            }
            return 0.0;
        }

        private void CheckSafetyProtection(double tgtTorque, double actTorq)
        {
            if (!hasBaseline) return;
            double threshPct = (numSafetyThresh != null) ? (double)numSafetyThresh.Value : 10.0;
            double diff = Math.Abs(actTorq - baselineTorque);
            double limit = baselineTorque * (threshPct / 100.0);

            if (diff > limit && limit > 0.1)
            {
                lblSafetyStatus.Text = string.Format("[警告] 閉迴路警告：平滑轉矩偏離基準 {0:F2} Nm (門檻: {1:F2} Nm)", diff, limit);
                lblSafetyStatus.ForeColor = Color.Red;
                WriteHmiLog("ALARM", string.Format("閉迴路安全警告: 平滑轉矩偏離基準 {0:F2} Nm (門檻: {1:F2} Nm)", diff, limit));
            }
            else
            {
                lblSafetyStatus.Text = "* 閉迴路安全防護：正常";
                lblSafetyStatus.ForeColor = Color.FromArgb(0, 240, 118);
            }
        }

        private void ResetSafetyAlarm()
        {
            lblSafetyStatus.Text = "* 閉迴路安全防護：正常";
            lblSafetyStatus.ForeColor = Color.FromArgb(0, 240, 118);
        }

        private void BtnStart_Click(object sender, EventArgs e)
        {
            if (isRunning) return;

            WriteHmiLog("SESSION", "=== 測試開始 (實體硬體模式) ===");
            ConnectAllDevices();
        }

        private void BtnStop_Click(object sender, EventArgs e)
        {
            isRunning = false;
            mainTimer.Stop();
            btnStart.Enabled = true;
            btnStop.Enabled = false;
            DisconnectAllDevices();
            WriteHmiLog("SESSION", "=== 測試停止 ===");
        }

        private void ConnectAllDevices()
        {
            ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    if (btnMasterConnectAll != null && !btnMasterConnectAll.IsDisposed)
                    {
                        btnMasterConnectAll.BeginInvoke(new Action(() => {
                            btnMasterConnectAll.Text = " 連線探測中...";
                            btnMasterConnectAll.Enabled = false;
                        }));
                    }

                    WriteHmiLog("CONNECT_ALL", "正在啟動全設備非同步連線 (Kistler 扭力計 / 橫河 WT333E / GL820 / A/B 載台驅動器)...");

                    // 1. Kistler 扭力計 (Serial)
                    string port = !string.IsNullOrEmpty(torquePortName) ? torquePortName : "COM4";
                    try
                    {
                        if (spTorque == null || !spTorque.IsOpen)
                        {
                            spTorque = new SerialPort(port, torqueBaudRate > 0 ? torqueBaudRate : 1000000, Parity.None, 8, StopBits.One);
                            spTorque.ReadTimeout = 500;
                            spTorque.WriteTimeout = 500;
                            spTorque.DtrEnable = true;
                            spTorque.RtsEnable = true;
                            spTorque.NewLine = "\r\n";
                            spTorque.Open();
                            if (lblPillTorque != null && !lblPillTorque.IsDisposed)
                            {
                                lblPillTorque.BeginInvoke(new Action(() => {
                                    lblPillTorque.Text = " 扭力: " + port + " ";
                                    lblPillTorque.ForeColor = Color.FromArgb(74, 222, 128);
                                }));
                            }
                            WriteHmiLog("TORQUE", "[OK] Kistler 扭力計已連線 (" + port + " @ " + (torqueBaudRate > 0 ? torqueBaudRate : 1000000) + ")");
                        }
                    }
                    catch {
                        if (lblPillTorque != null && !lblPillTorque.IsDisposed)
                        {
                            lblPillTorque.BeginInvoke(new Action(() => {
                                lblPillTorque.Text = " 扭力: " + port + " ";
                                lblPillTorque.ForeColor = Color.FromArgb(248, 113, 113);
                            }));
                        }
                    }

                    // 2. 橫河 WT333E (Modbus TCP 非同步短超時探測，支援雙網卡智慧綁定)
                    string wtIp = !string.IsNullOrEmpty(powerMeterIp) ? powerMeterIp : "192.168.0.11";
                    int wtPort = powerMeterPort > 0 ? powerMeterPort : 502;
                    try
                    {
                        if (tcpPower == null || !tcpPower.Connected)
                        {
                            TcpClient tc = NetworkHelper.CreateBoundTcpClient(wtIp);
                            IAsyncResult ar = tc.BeginConnect(wtIp, wtPort, null, null);
                            if (ar.AsyncWaitHandle.WaitOne(250) && tc.Connected)
                            {
                                tcpPower = tc;
                                streamPower = tcpPower.GetStream();
                                streamPower.ReadTimeout = 500;
                                streamPower.WriteTimeout = 500;
                                string bindStr = (tc.Client != null && tc.Client.LocalEndPoint != null) ? (" 網卡: " + tc.Client.LocalEndPoint.ToString()) : "";
                                if (lblPillPowerMeter != null && !lblPillPowerMeter.IsDisposed)
                                {
                                    lblPillPowerMeter.BeginInvoke(new Action(() => {
                                        lblPillPowerMeter.Text = " WT333E: " + wtPort + " ";
                                        lblPillPowerMeter.ForeColor = Color.FromArgb(74, 222, 128);
                                    }));
                                }
                                WriteHmiLog("POWER", "[OK] 橫河 WT333E 電表已連線 (" + wtIp + ":" + wtPort + bindStr + ")");
                            }
                            else
                            {
                                try { tc.Close(); } catch {}
                                if (lblPillPowerMeter != null && !lblPillPowerMeter.IsDisposed)
                                {
                                    lblPillPowerMeter.BeginInvoke(new Action(() => {
                                        lblPillPowerMeter.Text = " WT333E: " + wtPort + " ";
                                        lblPillPowerMeter.ForeColor = Color.FromArgb(248, 113, 113);
                                    }));
                                }
                            }
                        }
                    }
                    catch {
                        if (lblPillPowerMeter != null && !lblPillPowerMeter.IsDisposed)
                        {
                            lblPillPowerMeter.BeginInvoke(new Action(() => {
                                lblPillPowerMeter.Text = " WT333E: " + wtPort + " ";
                                lblPillPowerMeter.ForeColor = Color.FromArgb(248, 113, 113);
                            }));
                        }
                    }

                    // 3. GL820 溫度計 (TCP 非同步短超時探測，支援雙網卡智慧綁定)
                    string glIp = !string.IsNullOrEmpty(gbdIp) ? gbdIp : "192.168.0.3";
                    int glPort = gbdPort > 0 ? gbdPort : 8023;
                    try
                    {
                        if (tcpGbd == null || !tcpGbd.Connected)
                        {
                            TcpClient tcG = NetworkHelper.CreateBoundTcpClient(glIp);
                            IAsyncResult arG = tcG.BeginConnect(glIp, glPort, null, null);
                            if (arG.AsyncWaitHandle.WaitOne(800) && tcG.Connected)
                            {
                                tcpGbd = tcG;
                                streamGbd = tcpGbd.GetStream();
                                streamGbd.ReadTimeout = 500;
                                streamGbd.WriteTimeout = 500;
                                hasAutoDetectedGl820Channels = false;
                                gbdAutoDetectCountdown = 2;
                                string bindStrG = (tcG.Client != null && tcG.Client.LocalEndPoint != null) ? (" 網卡: " + tcG.Client.LocalEndPoint.ToString()) : "";
                                if (lblPillGbd != null && !lblPillGbd.IsDisposed)
                                {
                                    lblPillGbd.BeginInvoke(new Action(() => {
                                        lblPillGbd.Text = " GL820: " + glPort + " ";
                                        lblPillGbd.ForeColor = Color.FromArgb(74, 222, 128);
                                    }));
                                }
                                WriteHmiLog("GBD", "[OK] Graphtec GL820 溫度記錄器已連線 (" + glIp + ":" + glPort + bindStrG + ")");
                            }
                            else
                            {
                                try { tcG.Close(); } catch {}
                                if (lblPillGbd != null && !lblPillGbd.IsDisposed)
                                {
                                    lblPillGbd.BeginInvoke(new Action(() => {
                                        lblPillGbd.Text = " GL820: " + glPort + " ";
                                        lblPillGbd.ForeColor = Color.FromArgb(248, 113, 113);
                                    }));
                                }
                            }
                        }
                    }
                    catch {
                        if (lblPillGbd != null && !lblPillGbd.IsDisposed)
                        {
                            lblPillGbd.BeginInvoke(new Action(() => {
                                lblPillGbd.Text = " GL820: " + glPort + " ";
                                lblPillGbd.ForeColor = Color.FromArgb(248, 113, 113);
                            }));
                        }
                    }

                    // 4. KEB 驅動器 (由操作人員在 HMI 介面手動點擊 [Open] 按鈕進行連線握手，啟動時不自動佔用 COM 埠)
                    string p1 = (cmbHmiKebPort1 != null && cmbHmiKebPort1.SelectedItem != null) ? cmbHmiKebPort1.SelectedItem.ToString() : (!string.IsNullOrEmpty(kebPort1) ? kebPort1 : "COM1");
                    string p2 = (cmbHmiKebPort2 != null && cmbHmiKebPort2.SelectedItem != null) ? cmbHmiKebPort2.SelectedItem.ToString() : (!string.IsNullOrEmpty(kebPort2) ? kebPort2 : "COM2");
                    if (lblPillKeb1 != null && !lblPillKeb1.IsDisposed)
                    {
                        lblPillKeb1.BeginInvoke(new Action(() => {
                            lblPillKeb1.Text = isHmiKebOpen1 ? (" A載台: " + p1 + " ") : " A載台: 待連線 ";
                            lblPillKeb1.ForeColor = isHmiKebOpen1 ? Color.FromArgb(74, 222, 128) : Color.FromArgb(203, 213, 225);
                        }));
                    }
                    if (lblPillKeb2 != null && !lblPillKeb2.IsDisposed)
                    {
                        lblPillKeb2.BeginInvoke(new Action(() => {
                            lblPillKeb2.Text = isHmiKebOpen2 ? (" B載台: " + p2 + " ") : " B載台: 待連線 ";
                            lblPillKeb2.ForeColor = isHmiKebOpen2 ? Color.FromArgb(74, 222, 128) : Color.FromArgb(203, 213, 225);
                        }));
                    }

                    if (this != null && !this.IsDisposed)
                    {
                        this.BeginInvoke(new Action(() => {
                            if (btnMasterConnectAll != null) { btnMasterConnectAll.Text = " 設備連線"; btnMasterConnectAll.Enabled = true; }
                            if (btnStart != null) btnStart.Enabled = false;
                            if (btnStop != null) btnStop.Enabled = true;
                            if (mainTimer != null && !mainTimer.Enabled) mainTimer.Start();
                            isRunning = true;
                            StartBackgroundWorker();
                        }));
                    }
                }
                catch {}
            });
        }

        public void TryReconnectGbdAsync()
        {
            if (isGbdReconnecting) return;
            isGbdReconnecting = true;
            ThreadPool.QueueUserWorkItem(_ => {
                try
                {
                    string glIp = !string.IsNullOrEmpty(gbdIp) ? gbdIp : "192.168.0.3";
                    int glPort = gbdPort > 0 ? gbdPort : 8023;
                    if (tcpGbd != null && tcpGbd.Connected) return;

                    TcpClient tcG = NetworkHelper.CreateBoundTcpClient(glIp);
                    IAsyncResult arG = tcG.BeginConnect(glIp, glPort, null, null);
                    if (arG.AsyncWaitHandle.WaitOne(800) && tcG.Connected)
                    {
                        if (tcpGbd != null) { try { tcpGbd.Close(); } catch { } }
                        tcpGbd = tcG;
                        streamGbd = tcpGbd.GetStream();
                        streamGbd.ReadTimeout = 500;
                        streamGbd.WriteTimeout = 500;
                        hasAutoDetectedGl820Channels = false;
                        gbdAutoDetectCountdown = 2;
                        string bindStrG = (tcG.Client != null && tcG.Client.LocalEndPoint != null) ? (" 網卡: " + tcG.Client.LocalEndPoint.ToString()) : "";
                        if (lblPillGbd != null && !lblPillGbd.IsDisposed)
                        {
                            lblPillGbd.BeginInvoke(new Action(() => {
                                lblPillGbd.Text = " GL820: " + glPort + " ";
                                lblPillGbd.ForeColor = Color.FromArgb(74, 222, 128);
                            }));
                        }
                        WriteHmiLog("GBD", "[OK-重連] Graphtec GL820 溫度記錄器已自動連線 (" + glIp + ":" + glPort + bindStrG + ")");
                    }
                    else
                    {
                        try { tcG.Close(); } catch { }
                    }
                }
                catch { }
                finally
                {
                    isGbdReconnecting = false;
                }
            });
        }

        private void DisconnectAllDevices()
        {
            UnlockAllTrackingAndReset();
            isRunning = false;
            StopBackgroundWorker();
            DisconnectHardware();
            lblPillTorque.Text = " 扭力: " + (!string.IsNullOrEmpty(torquePortName) ? torquePortName : "COM4") + " "; lblPillTorque.ForeColor = Color.FromArgb(203, 213, 225);
            lblPillPowerMeter.Text = " WT333E: " + powerMeterPort + " "; lblPillPowerMeter.ForeColor = Color.FromArgb(203, 213, 225);
            lblPillGbd.Text = " GL820: " + gbdPort + " "; lblPillGbd.ForeColor = Color.FromArgb(203, 213, 225);
            lblPillKeb1.Text = " A載台: "; lblPillKeb1.ForeColor = Color.FromArgb(203, 213, 225);
            lblPillKeb2.Text = " B載台: "; lblPillKeb2.ForeColor = Color.FromArgb(203, 213, 225);
            lblSafetyStatus.Text = " 實體模式待命";
            lblSafetyStatus.ForeColor = Color.FromArgb(74, 222, 128);
            WriteHmiLog("DISCONNECT_ALL", " 全部設備已中斷連線");
        }

        private void TriggerDisconnectFailSafe(string deviceName)
        {
            if (isClosedLoopTracking || isSpeedTracking)
            {
                isClosedLoopTracking = false;
                isSpeedTracking = false;
                if (btnToggleClosedLoop != null) { btnToggleClosedLoop.Text = " 啟動閉迴路平滑追隨"; btnToggleClosedLoop.BackColor = Color.FromArgb(0, 180, 216); }
                if (btnLockTorque != null) { btnLockTorque.Text = " UNLOCK"; btnLockTorque.Image = CreateLockIconImage(14, 14, Color.FromArgb(100, 116, 139), false); btnLockTorque.ForeColor = Color.FromArgb(100, 116, 139); btnLockTorque.BackColor = Color.FromArgb(241, 245, 249); }
                if (btnLockSpeed != null) { btnLockSpeed.Text = " UNLOCK"; btnLockSpeed.Image = CreateLockIconImage(14, 14, Color.FromArgb(100, 116, 139), false); btnLockSpeed.ForeColor = Color.FromArgb(100, 116, 139); btnLockSpeed.BackColor = Color.FromArgb(241, 245, 249); }

                SetHmiKebCommand(GetHmiKebComIdx(2), GetHmiKebBaudIdx(2), (int)numHmiKebNode2.Value, 0, "B載台負載端 (緊急通訊中斷停機)");
                SetHmiKebCommand(GetHmiKebComIdx(1), GetHmiKebBaudIdx(1), (int)numHmiKebNode1.Value, 0, "A載台主動端 (緊急通訊中斷停機)");

                lblSafetyStatus.Text = string.Format(" [通訊中斷緊急聯鎖] {0} 連線中斷！已自動安全卸載停機！", deviceName);
                lblSafetyStatus.ForeColor = Color.FromArgb(239, 68, 68);
                WriteHmiLog("FAILSAFE", string.Format(" [緊急安全聯鎖] 檢測到關鍵設備 [{0}] 通訊中斷，已立即終止轉速與轉矩閉迴路並執行安全卸載停機！", deviceName));
                try { System.Media.SystemSounds.Exclamation.Play(); } catch { }
            }
        }

        private void CheckSafetyProtectionMatrix()
        {
            if (chkSimMode != null && chkSimMode.Checked) return; // 模擬模式略過實體硬體保護
            bool isAnyDriveRunning = (lastSy50Cmd1 == 4 || lastSy50Cmd1 == 12 || lastSy50Cmd2 == 4 || lastSy50Cmd2 == 12);
            bool isTestRunning = (dutyTimer != null && dutyTimer.Enabled)
                              || (tnTimer != null && tnTimer.Enabled)
                              || (effMapTimer != null && effMapTimer.Enabled)
                              || isNoLoadRunning
                              || isClosedLoopTracking
                              || isSpeedTracking;
            DateTime now = DateTime.Now;

            // 1. 扭力計斷線 / 反饋凍結保護 (僅在扭力計通訊已開啟且驅動器運轉中或自動測試進行中才守護，靜止停機狀態下不觸發)
            // ★ 空載測試特殊防護：使用者明確指示空載測試不依賴扭力計，嚴禁觸發逾時跳脫！
            if (enableProtTorqueLoss && (isAnyDriveRunning || isTestRunning) && !isNoLoadRunning)
            {
                if (spTorque != null && spTorque.IsOpen)
                {
                    if ((now - lastTorquePacketTime).TotalSeconds > (double)protTorqueTimeoutSec)
                    {
                        TriggerGlobalEmergencyStop();
                        WriteHmiLog("SAFETY_TRIP", string.Format("【🚨 安全保護跳脫】扭力計斷線或反饋逾時 (超過 {0:F1} 秒無數據)，已強制雙機急停！", protTorqueTimeoutSec));
                    }
                }
            }

            // 2. 機械堵轉 / 失速保護 (量: protStallSpeedThreshold, 時間: protStallDelaySec)
            // ★ 空載測試特殊防護：空載測試無轉速回授訊號 (理論上看不到實測轉速)，嚴禁誤判為堵轉失速！
            if (enableProtStall && isAnyDriveRunning && !isNoLoadRunning)
            {
                int cmdSpd1 = (numHmiKebSpeed1 != null) ? (int)numHmiKebSpeed1.Value : 0;
                int cmdSpd2 = (numHmiKebSpeed2 != null) ? (int)numHmiKebSpeed2.Value : 0;
                int maxTargetSpd = Math.Max(cmdSpd1, cmdSpd2);

                if (maxTargetSpd > 100 && Math.Abs(actSpeed) < (double)protStallSpeedThreshold)
                {
                    if (stallStartTime == DateTime.MinValue) stallStartTime = now;
                    else if ((now - stallStartTime).TotalSeconds >= (double)protStallDelaySec)
                    {
                        TriggerGlobalEmergencyStop();
                        stallStartTime = DateTime.MinValue;
                        WriteHmiLog("SAFETY_TRIP", string.Format("【🚨 安全保護跳脫】檢測到機械堵轉/失速 (目標轉速 {0} rpm，實測連續 {1:F1} 秒 < {2:F0} rpm)，已強制卸載停機！", maxTargetSpd, protStallDelaySec, protStallSpeedThreshold));
                    }
                }
                else
                {
                    stallStartTime = DateTime.MinValue;
                }
            }
            else
            {
                stallStartTime = DateTime.MinValue;
            }

            // 3. 馬達繞組與軸承溫度雙級保護 (警告溫度: protWarnTempThreshold, 停機溫度: protMaxTempThreshold, 持續時間: protTempDelaySec)
            if (enableProtOvertemp && (isAnyDriveRunning || isTestRunning))
            {
                double maxTemp = actTemp;
                if (gbdChTemps != null)
                {
                    for (int i = 0; i < 20; i++)
                    {
                        if (gl820ChannelMask != null && i < gl820ChannelMask.Length && gl820ChannelMask[i])
                        {
                            if (gbdChTemps[i] > maxTemp && gbdChTemps[i] < 999.0) maxTemp = gbdChTemps[i];
                        }
                    }
                }

                // 級別一：警告溫度 (Warning)
                if (maxTemp >= (double)protWarnTempThreshold && maxTemp < 999.0)
                {
                    if (!tempWarnTriggered)
                    {
                        tempWarnTriggered = true;
                        WriteHmiLog("WARN_TEMP", string.Format("【⚠️ 溫度預警】馬達檢測溫度達 {0:F1}°C，已超出警告閾值 ({1:F1}°C)！請密切注意冷卻散熱！", maxTemp, protWarnTempThreshold));
                        if (lblSafetyStatus != null)
                        {
                            lblSafetyStatus.Text = string.Format(" [⚠️ 溫度預警] 馬達溫度 {0:F1}°C 超出警告門檻 ({1:F1}°C)！", maxTemp, protWarnTempThreshold);
                            lblSafetyStatus.ForeColor = Color.FromArgb(245, 158, 11);
                        }
                    }
                }
                else if (maxTemp < (double)protWarnTempThreshold - 2.0)
                {
                    tempWarnTriggered = false;
                }

                // 級別二：停機溫度 (Trip Stop)
                if (maxTemp >= (double)protMaxTempThreshold && maxTemp < 999.0)
                {
                    if (tempTripStartTime == DateTime.MinValue) tempTripStartTime = now;
                    else if ((now - tempTripStartTime).TotalSeconds >= (double)protTempDelaySec)
                    {
                        TriggerGlobalEmergencyStop();
                        tempTripStartTime = DateTime.MinValue;
                        WriteHmiLog("SAFETY_TRIP", string.Format("【🚨 安全保護跳脫】馬達實測溫度連續 {0:F1} 秒達 {1:F1}°C，突破停機極限 ({2:F1}°C)，已強制卸載停機！", protTempDelaySec, maxTemp, protMaxTempThreshold));
                    }
                }
                else
                {
                    tempTripStartTime = DateTime.MinValue;
                }
            }
            else
            {
                tempTripStartTime = DateTime.MinValue;
            }

            // 4. WT333E 三相電流不平衡 / 欠相預警 (量: protImbalancePercentThreshold, 時間: protImbalanceDelaySec, 不強停僅警示)
            if (enableProtCurrentImbalance && isAnyDriveRunning)
            {
                float i1 = wtI1, i2 = wtI2, i3 = wtI3;
                float avgI = (i1 + i2 + i3) / 3.0f;
                if (avgI > 1.0f) // 避開待機或停機浮動微弱雜訊
                {
                    float maxI = Math.Max(i1, Math.Max(i2, i3));
                    float minI = Math.Min(i1, Math.Min(i2, i3));
                    float imbalanceRatio = ((maxI - minI) / avgI) * 100.0f;

                    if (imbalanceRatio >= (float)protImbalancePercentThreshold)
                    {
                        if (imbalanceStartTime == DateTime.MinValue) imbalanceStartTime = now;
                        else if ((now - imbalanceStartTime).TotalSeconds >= (double)protImbalanceDelaySec)
                        {
                            if (!imbalanceWarnTriggered)
                            {
                                imbalanceWarnTriggered = true;
                                WriteHmiLog("WARN_IMBALANCE", string.Format("【⚠️ 三相偏載預警】WT333E 實測三相電流 (U:{0:F1}A, V:{1:F1}A, W:{2:F1}A) 不平衡率達 {3:F1}% (門檻 {4:F0}%)！請留意接線或接觸端子！", i1, i2, i3, imbalanceRatio, protImbalancePercentThreshold));
                                if (lblSafetyStatus != null)
                                {
                                    lblSafetyStatus.Text = string.Format(" [⚠️ 三相偏載預警] 電流不平衡率 {0:F1}% (門檻 {1:F0}%)！", imbalanceRatio, protImbalancePercentThreshold);
                                    lblSafetyStatus.ForeColor = Color.FromArgb(245, 158, 11);
                                }
                            }
                        }
                    }
                    else if (imbalanceRatio < (float)protImbalancePercentThreshold - 5.0f)
                    {
                        imbalanceStartTime = DateTime.MinValue;
                        imbalanceWarnTriggered = false;
                    }
                }
                else
                {
                    imbalanceStartTime = DateTime.MinValue;
                    imbalanceWarnTriggered = false;
                }
            }
            else
            {
                imbalanceStartTime = DateTime.MinValue;
                imbalanceWarnTriggered = false;
            }
        }

        private void DisconnectHardware()
        {
            try { if (tmrHmiKeb != null) tmrHmiKeb.Stop(); } catch { }

            // ★【斷線前 / 關閉程式前 自動回寫原始參數鐵律】：
            // 若 A/B 載台仍處於連線狀態且有備份參數，在關閉 COM 埠前逐一安全回寫還原！
            try
            {
                if (isHmiKebOpen1) RestoreHmiKebInitialParams(1);
            }
            catch { }

            try
            {
                if (isHmiKebOpen2) RestoreHmiKebInitialParams(2);
            }
            catch { }

            try
            {
                if (spKeb1 != null)
                {
                    if (spKeb1.IsOpen) spKeb1.Close();
                    spKeb1.Dispose();
                }
            }
            catch { }
            spKeb1 = null;
            isHmiKebOpen1 = false;

            try
            {
                if (spKeb2 != null)
                {
                    if (spKeb2.IsOpen) spKeb2.Close();
                    spKeb2.Dispose();
                }
            }
            catch { }
            spKeb2 = null;
            isHmiKebOpen2 = false;

            try
            {
                if (spTorque != null)
                {
                    if (spTorque.IsOpen) spTorque.Close();
                    spTorque.Dispose();
                }
            }
            catch { }
            spTorque = null;

            try
            {
                if (streamPower != null) { streamPower.Close(); streamPower.Dispose(); }
            }
            catch { }
            streamPower = null;

            try
            {
                if (tcpPower != null) tcpPower.Close();
            }
            catch { }
            tcpPower = null;

            try
            {
                if (ykDeviceId >= 0) TmcFinish(ykDeviceId);
            }
            catch { }
            ykDeviceId = -1;

            try
            {
                if (streamGbd != null) { streamGbd.Close(); streamGbd.Dispose(); }
            }
            catch { }
            streamGbd = null;

            try
            {
                if (tcpGbd != null) tcpGbd.Close();
            }
            catch { }
            tcpGbd = null;
        }




        // =========================================================================
        // 安全工廠方法 (100% 杜絕 ArgumentOutOfRangeException)
        // =========================================================================
        private static NumericUpDown CreateNumericUpDown(Point loc, int width, decimal min, decimal max, decimal val, int dec = 0, decimal inc = 1)
        {
            NumericUpDown nud = new NumericUpDown();
            nud.Location = loc;
            nud.Width = width;
            nud.TextAlign = HorizontalAlignment.Right;
            nud.DecimalPlaces = dec;
            nud.Increment = inc;
            nud.Minimum = min;
            nud.Maximum = max;
            if (val < min) val = min;
            if (val > max) val = max;
            nud.Value = val;
            return nud;
        }

        private static TrackBar CreateTrackBar(Point loc, int width, int min, int max, int val, int tick = 10)
        {
            TrackBar trk = new TrackBar();
            trk.Location = loc;
            trk.Width = width;
            trk.Minimum = min;
            trk.Maximum = max;
            if (val < min) val = min;
            if (val > max) val = max;
            trk.Value = val;
            trk.TickFrequency = tick;
            return trk;
        }

        private static double ParseSci(string valStr)
        {
            double res = 0.0;
            if (double.TryParse(valStr, NumberStyles.Float | NumberStyles.AllowExponent, CultureInfo.InvariantCulture, out res))
                return res;
            return 0.0;
        }

        private static string DecodeHexToAscii(string hex)
        {
            try
            {
                if (hex.Contains("Torque") || hex.Contains("Sensor")) return hex;
                StringBuilder sb = new StringBuilder();
                for (int i = 0; i < hex.Length - 1; i += 2)
                {
                    string hs = hex.Substring(i, 2);
                    if (hs.Equals("FF", StringComparison.OrdinalIgnoreCase)) break;
                    byte b;
                    if (byte.TryParse(hs, NumberStyles.HexNumber, null, out b)) sb.Append((char)b);
                }
                string res = sb.ToString();
                return string.IsNullOrEmpty(res) ? hex : res;
            }
            catch { return hex; }
        }
    }
}
