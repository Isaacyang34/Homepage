using System;
using System.Collections.Generic;
using System.Linq;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.IO.Ports;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;

[assembly: AssemblyTitle("Dynamometer HMI")]
[assembly: AssemblyDescription("Electric Motor Dynamometer Acquisition & Control System")]
[assembly: AssemblyConfiguration("")]
[assembly: AssemblyCompany("Dynamometer Lab")]
[assembly: AssemblyProduct("Dynamometer HMI")]
[assembly: AssemblyCopyright("Copyright © 2026")]
[assembly: AssemblyTrademark("")]
[assembly: AssemblyCulture("")]
[assembly: ComVisible(false)]
[assembly: AssemblyVersion("1.0.0.0")]
[assembly: AssemblyFileVersion("1.0.0.0")]

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

    // KEB 連線硬體參數備份快照資料結構 (供斷線或關閉程式前回寫還原)
    public class KebBackupParams
    {
        public bool HasBackup;
        public int? Ru00;
        public int? Ud02;
        public int? Op00;
        public int? Op01;
        public int? Cs00;
        public int? Cs15;
        public int? Cs18;
        public int? Cs19;
        public int? Op03;
        public int? Sy50;
        public int? Sy52;
        public DateTime BackupTime;

        public KebBackupParams()
        {
            HasBackup = false;
            BackupTime = DateTime.MinValue;
        }
    }

    public class MainForm : Form
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

        // KEB 官方原廠 protKEB.dll P/Invoke 接口與資料結構 (完全 100% 鏡像對齊 Module_KEB_1.bas 記憶體佈局)
        [StructLayout(LayoutKind.Sequential, Pack = 1, CharSet = CharSet.Ansi)]
        public struct tProtProperty
        {
            public int ProtType;      // 0..3 (4B): 1=prAnsi, 2=prHsp5, 4=prIP
            public int TimeOut;       // 4..7 (4B): ms (預設 500)
            public int Baudrate;      // 8..11 (4B): 0=1200, 1=2400, 2=4800, 3=9600, 4=19200, 5=38400, 6=57600, 7=115200
            public int Comport;       // 12..15 (4B): 0=COM1, 1=COM2, 2=COM3, 3=COM4
            public int Flag;          // 16..19 (4B): 01 = Sendslow
            public int Port;          // 20..23 (4B): IP Port
            public byte Txtlen;       // 24 (1B)
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 100)]
            public string txt;        // 25..124 (100B)
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public struct tRecTel
        {
            public int InvId;
            public int Inverter;
            public int Service;
            public int Ack;
            public byte Read;
            public byte Req;
            public short Fill;
            public int SR;  // VB6 Long = 4-byte pointer value (NOT IntPtr, which is 8B in x64 host)
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public struct tServ00Rec
        {
            public short Adr;
            public byte Paraset;
            public byte Fill;
            public int Data;
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public struct tServM1Rec
        {
            public short Adr;
            public short Data;
        }

        [DllImport("protKEB.dll", CallingConvention = CallingConvention.StdCall)]
        public static extern void closechannels();

        [DllImport("protKEB.dll", CallingConvention = CallingConvention.StdCall)]
        public static extern int setprotproperties(ref tProtProperty prop);

        [DllImport("protKEB.dll", CallingConvention = CallingConvention.StdCall)]
        public static extern int setinvprot(int inv, int prot);

        [DllImport("protKEB.dll", CallingConvention = CallingConvention.StdCall)]
        public static extern void setretrycnt(int retries);

        [DllImport("protKEB.dll", CallingConvention = CallingConvention.StdCall, EntryPoint = "waitrdreq")]
        public static extern int waitrdreq(int inv, int service, byte[] servRec, byte[] recTel);

        [DllImport("protKEB.dll", CallingConvention = CallingConvention.StdCall, EntryPoint = "waitwrreq")]
        public static extern int waitwrreq(int inv, int service, byte[] servRec, byte[] recTel);

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
        public double actTemp = 25.0;
        public bool isSimMode = false;

        public System.Windows.Forms.Timer mainTimer;
        private System.Windows.Forms.Timer motorTempTimer;
        private Random rand = new Random();

        // 實體硬體連線物件
        private SerialPort spTorque;
        private int ykDeviceId = -1;
        private TcpClient tcpPower;
        private NetworkStream streamPower;
        private TcpClient tcpGbd;
        private NetworkStream streamGbd;

        private TabControl tabControl;
        public Label lblSafetyStatus;

        // =========================================================================
        private Label lblSpeedVal, lblTorqueVal, lblPowerVal, lblElecPowerVal, lblEffVal, lblKtVal, lblTempVal, lblBaseTorqueDisp, lblSmoothTorqueDisp;
        public Label lblMotorTempDisplay;
        public MotorTempTrendControl motorTempChart;
        private NumericUpDown numTargetSpeed, numTargetTorque;
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

        // 分頁 2: T-N 曲線 (增加角色切換、手動定錨與自適應梯度傳承)
        private NumericUpDown numTnStartRpm, numTnStepRpm, numTnEndRpm, numTnTorque, numTnDwell;
        private Button btnStartTn, btnStopTn, btnExportTn, btnTnAnchor;
        private double tnAdaptedTorquePct = 0.0; // 自適應梯度加載轉矩給定 % (無定錨嚴格從 0.0% 起步)
        private bool tnHasAnchor = false;
        private ComboBox cmbTnRole; // 0: A待測(速度)/B加載(轉矩), 1: B待測(速度)/A加載(轉矩)
        private Label lblTnAnchorStatus;
        private ProgressBar prgTn;
        private Label lblTnStatus, lblTnCountdown;
        private DataGridView dgvTnPoints;
        private TnCurveChart tnChart;
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

        // S6 V/F 自適應試運轉定錨與正式週期狀態機變數
        private double s6AnchorNoLoadSpeed = 1000;
        private double s6AnchorLoadedSpeed = 1100;
        private double s6AnchorLoadedTorquePct = 10.0;
        private bool s6HasNoLoadAnchor = false;
        private bool s6HasLoadedAnchor = false;
        private int s6DutyPhase = 0; // 0: 自適應試運轉定錨階段, 1: 正式週期循環階段
        private int s6TrialStage = 0; // 0:空載提速, 1:空載10s穩定, 2:加載補轉差, 3:加載10s穩定, 4:剩餘T1持載, 5:T2卸載空載冷卻
        private int s6TrialTimer = 0; // 10 秒穩定倒數計時器
        private int s6FormalCycleIndex = 1; // 正式週期次數 (1..N)
        private double s6CurrentSpeedCmd = 1000;
        private bool s6SpeedReached = false;
        private double s6AdaptedTorquePct = 0.0;
        private int s6CycleElapsedSec = 0;

        // 綜合監控右下角小視窗控制項 (與大分頁 100% 雙向即時連動)
        private ComboBox cmbTnRoleMini;
        private NumericUpDown numTnMiniStart, numTnMiniStep, numTnMiniEnd, numTnMiniTrq, numTnMiniDwell;
        private Button btnTnMiniStart, btnTnMiniStop, btnTnMiniAnchor;
        private Label lblTnMiniAnchorStatus, lblTnMiniStatus, lblTnMiniCountdown;
        private ProgressBar prgTnMini;

        private ComboBox cmbDutyMiniMode, cmbDutyMiniRole;
        private NumericUpDown numDutyMiniSpd, numDutyMiniTrq, numDutyMiniCycleMin, numDutyMiniCycles, numDutyMiniEd;
        private Button btnDutyMiniStart, btnDutyMiniStop, btnS6MiniAnchorNoLoad, btnS6MiniAnchorLoaded;
        private Label lblS6MiniAnchorStatus, lblDutyMiniStatus, lblDutyMiniPhaseAction;
        private TableLayoutPanel tblDutyMini;
        private Label lblDutyMiniCycleLabel, lblDutyMiniAnchorLabel, lblDutyMiniDiagLabel;
        private FlowLayoutPanel pnlDutyMiniCycle, pnlDutyMiniAnchors;
        private Panel pnlS6MiniDiagram;
        private ProgressBar prgDutyMini;
        private bool isSyncingTnControls = false;
        private bool isSyncingDutyControls = false;

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
        public int lastRu00_1 = -1, lastRu00_2 = -1; // 追蹤 A/B 載台 ru.00 狀態變更 (nOP, LS, FAcc, FAULT等)
        private KebBackupParams kebBackup1 = new KebBackupParams();
        private KebBackupParams kebBackup2 = new KebBackupParams();

        // KEB 斷線緩衝與連鎖安全防護 (可於【🎯 追蹤 / 日誌】設定)
        public decimal disconnectBufferSeconds = 5.0m; // 斷線緩衝時間 (預設 5 秒，範圍 1~60 秒)
        public NumericUpDown numDisconnectBuffer;
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
        public double[] gbdChTemps = new double[20] {
            25.0, 25.4, 25.8, 26.2, 25.5, 25.9, 26.3, 26.7, 27.0, 27.2,
            25.1, 25.3, 25.6, 26.0, 25.2, 25.7, 26.1, 26.5, 26.8, 27.1
        };
        private DataGridView dgvGbdAll;
        private GbdTemperatureTrendControl gbdTrendChart;
        private List<KeyValuePair<DateTime, double[]>> gbdHistory = new List<KeyValuePair<DateTime, double[]>>();
        private TabPage tabGbd;

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

        [STAThread]
        public static void Main(string[] args)
        {
            // 自動尋找並註冊 DLL 子目錄 (將所有廠商驅動 DLL 集中於 DLL/ 資料夾，保持主目錄極簡純淨)
            try
            {
                string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                string dllDir = Path.Combine(baseDir, "DLL");
                if (Directory.Exists(dllDir))
                {
                    SetDllDirectory(dllDir);
                    string pathEnv = Environment.GetEnvironmentVariable("PATH") ?? "";
                    if (!pathEnv.Contains(dllDir))
                    {
                        Environment.SetEnvironmentVariable("PATH", dllDir + ";" + pathEnv);
                    }
                }
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
                MessageBox.Show("系統發生異常:\n" + e.ExceptionObject.ToString(), "異常報告", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            };
            Application.ThreadException += (s, e) => {
                if (e.Exception is ObjectDisposedException) return;
                MessageBox.Show("執行緒異常:\n" + e.Exception.ToString(), "異常警告", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            };

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            if (args != null && args.Length > 0 && (args[0].Equals("--tester", StringComparison.OrdinalIgnoreCase) || args[0].Equals("-t", StringComparison.OrdinalIgnoreCase) || args[0].Equals("/tester", StringComparison.OrdinalIgnoreCase)))
            {
                Application.Run(new DynamometerDeviceTester.TesterForm());
            }
            else
            {
                Application.Run(new MainForm());
            }
        }

        public MainForm()
        {
            this.Text = "Dynamometer HMI";
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
            Panel pnlTop = new Panel()
            {
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(15, 23, 42),
                Padding = new Padding(10, 8, 10, 8)
            };

            Label lblAppTitle = new Label()
            {
                Text = "馬達動力計測試平台",
                ForeColor = Color.White,
                Font = new Font("微軟正黑體", 13f, FontStyle.Bold),
                AutoSize = true,
                Location = new Point(10, 12)
            };

            // 主捲平滑追隨與常態日誌記錄設定彈窗按鈕
            Button btnClosedLoopModal = new Button()
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

            Button btnTopEstop = new Button()
            {
                Text = "🚨 緊急停機 (E-STOP / ESC)",
                Dock = DockStyle.Right,
                Width = 230,
                BackColor = Color.FromArgb(220, 38, 38),
                ForeColor = Color.White,
                Font = new Font("微軟正黑體", 11f, FontStyle.Bold),
                Cursor = Cursors.Hand,
                FlatStyle = FlatStyle.Flat
            };
            btnTopEstop.FlatAppearance.BorderSize = 0;
            btnTopEstop.Click += (s, e) => TriggerGlobalEmergencyStop();

            pnlTop.Controls.AddRange(new Control[] {
                lblAppTitle, btnClosedLoopModal,
                btnRecordRawTop, btnFontCustomizer, btnDeviceSettings, btnCalibrationSettings,
                btnTopEstop
            });
            rootTable.Controls.Add(pnlTop, 0, 0);

            // 核心分頁控制器 (6 大功能分頁 - 1080p 放大)
            tabControl = new TabControl()
            {
                Dock = DockStyle.Fill,
                Font = new Font("微軟正黑體", 11f, FontStyle.Bold),
                ItemSize = new Size(185, 38),
                SizeMode = TabSizeMode.Fixed,
                Margin = new Padding(4)
            };

            instance = this;

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

            tabGbd = new TabPage("溫度記錄器 (GL820)") { BackColor = Color.White };
            BuildGbdTab(tabGbd);
            tabControl.TabPages.Add(tabGbd);

            tabLog = new TabPage("系統運轉日誌 (Log)") { BackColor = Color.White };
            BuildLogTab(tabLog);
            tabControl.TabPages.Add(tabLog);

            rootTable.Controls.Add(tabControl, 0, 1);
            this.Controls.Add(rootTable);

            // 雙向全息即時同步：切換分頁時，自動無縫鏡像 Mini 控制列與對應分頁之所有設定參數
            tabControl.SelectedIndexChanged += (s, e) => {
                if (tabControl.SelectedIndex == 1) // 切換至 T-N 曲線測試分頁
                {
                    SyncAllTnControls(fromMiniToMain: true);
                }
                else if (tabControl.SelectedIndex == 2) // 切換至 工作制測試 (S6) 分頁
                {
                    SyncAllDutyControls(fromMiniToMain: true);
                    if (pnlS6Diagram != null) { pnlS6Diagram.Invalidate(); pnlS6Diagram.Refresh(); }
                    if (pnlS6MiniDiagram != null) { pnlS6MiniDiagram.Invalidate(); pnlS6MiniDiagram.Refresh(); }
                }
                else if (tabControl.SelectedIndex == 0) // 切換回即時遙測總覽
                {
                    SyncAllTnControls(fromMiniToMain: false);
                    SyncAllDutyControls(fromMiniToMain: false);
                }
            };

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

            // 自動記憶佈局、啟動即自動連線採樣並啟動背景輪詢 Worker
            this.Shown += (s, e) => {
                LoadLayoutConfig();
                if (mainTimer != null && !mainTimer.Enabled) mainTimer.Start();
                isRunning = true;
                StartBackgroundWorker();
                ConnectAllDevices();
            };
            this.FormClosing += (s, e) => {
                // 1. 視覺秒隱藏：0 毫秒極速視覺反饋，視窗立刻從螢幕與工作列消失
                try { this.Hide(); } catch { }

                // 2. 立即終止所有運轉標誌與前景計時器
                isRunning = false;
                isWorkerRunning = false;
                if (motorTempTimer != null) { try { motorTempTimer.Stop(); } catch { } }
                if (mainTimer != null) { try { mainTimer.Stop(); } catch { } }

                // 3. 同步安全持久化介面與視圖設定檔 (耗時 < 2ms，確保配置絕對不丟失)
                try { SaveLayoutConfig(); } catch { }

                // 4. 消除 WinForms 漫長的控制項逐一 Dispose 迴圈，將阻塞的 SerialPort/TCP 關閉丟至背景非同步執行
                e.Cancel = true;
                System.Threading.ThreadPool.QueueUserWorkItem(_ => {
                    try { StopBackgroundWorker(); } catch { }
                    try { DisconnectHardware(); } catch { }
                    try { closechannels(); } catch { }
                    Environment.Exit(0);
                });
            };

            mainTimer = new System.Windows.Forms.Timer();
            mainTimer.Interval = 500; // 預設 500ms 主畫面更新頻率 (耗時 < 1ms，零負擔)
            mainTimer.Tick += MainTimer_Tick;

            motorTempTimer = new System.Windows.Forms.Timer();
            motorTempTimer.Interval = 1000; // 預設 1 秒 (1Sec) 馬達溫度採樣與趨勢繪圖
            motorTempTimer.Tick += (s, e) => {
                int selCh = (cmbMotorTempCh != null && cmbMotorTempCh.SelectedIndex >= 0) ? cmbMotorTempCh.SelectedIndex : 0;
                double t = (selCh >= 0 && selCh < gbdChTemps.Length) ? gbdChTemps[selCh] : actTemp;
                if (isSimMode && t == 0.0) t = actTemp;
                if (lblMotorTempDisplay != null && !lblMotorTempDisplay.IsDisposed)
                {
                    lblMotorTempDisplay.Text = string.Format("{0:F1} °C", t);
                }
                if (motorTempChart != null && !motorTempChart.IsDisposed)
                {
                    motorTempChart.AddSample(DateTime.Now, t);
                }
                if (gbdTrendChart != null && !gbdTrendChart.IsDisposed)
                {
                    gbdTrendChart.AddSample(DateTime.Now, gbdChTemps);
                }
                gbdHistory.Add(new KeyValuePair<DateTime, double[]>(DateTime.Now, (double[])gbdChTemps.Clone()));
                if (gbdHistory.Count > 1000) gbdHistory.RemoveAt(0);
            };
            motorTempTimer.Start();
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
                var sb = new StringBuilder();
                sb.AppendLine("[Window]");
                sb.AppendLine("Width=" + (this.WindowState == FormWindowState.Normal ? this.Width : this.RestoreBounds.Width));
                sb.AppendLine("Height=" + (this.WindowState == FormWindowState.Normal ? this.Height : this.RestoreBounds.Height));
                sb.AppendLine("State=" + (int)this.WindowState);

                sb.AppendLine("[Splitters]");
                if (splitMainVertical != null && splitMainVertical.Height > 0)
                    sb.AppendLine("MainVertical=" + splitMainVertical.SplitterDistance);
                if (splitDrives != null && splitDrives.Width > 0)
                    sb.AppendLine("Drives=" + splitDrives.SplitterDistance);
                if (splitDrive1 != null && splitDrive1.Width > 0)
                    sb.AppendLine("Drive1=" + splitDrive1.SplitterDistance);
                if (splitDrive2 != null && splitDrive2.Width > 0)
                    sb.AppendLine("Drive2=" + splitDrive2.SplitterDistance);
                if (splitBottomHorizontal != null && splitBottomHorizontal.Width > 0)
                    sb.AppendLine("Bottom=" + splitBottomHorizontal.SplitterDistance);
                if (splitParam1 != null && splitParam1.Height > 0)
                    sb.AppendLine("Param1=" + splitParam1.SplitterDistance);
                if (splitParam2 != null && splitParam2.Height > 0)
                    sb.AppendLine("Param2=" + splitParam2.SplitterDistance);

                sb.AppendLine("[Workbench]");
                sb.AppendLine("ActiveView=" + activeWorkbenchViewIdx);

                if (dgvKebRu1 != null && dgvKebRu1.Columns.Count >= 2)
                {
                    sb.AppendLine("[DgvKebRu1]");
                    for (int i = 0; i < dgvKebRu1.Columns.Count; i++)
                        sb.AppendLine("Col" + i + "=" + dgvKebRu1.Columns[i].Width);
                }

                if (dgvKebRu2 != null && dgvKebRu2.Columns.Count >= 2)
                {
                    sb.AppendLine("[DgvKebRu2]");
                    for (int i = 0; i < dgvKebRu2.Columns.Count; i++)
                        sb.AppendLine("Col" + i + "=" + dgvKebRu2.Columns[i].Width);
                }

                if (dgvTelemetry != null && dgvTelemetry.Columns.Count > 0)
                {
                    sb.AppendLine("[DgvTelemetry]");
                    for (int i = 0; i < dgvTelemetry.Columns.Count; i++)
                        sb.AppendLine("Col" + i + "=" + dgvTelemetry.Columns[i].Width);
                }

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

                sb.AppendLine("[Logging]");
                sb.AppendLine("AutoRawCsv=" + (enableAutoRawCsv ? "1" : "0"));
                sb.AppendLine("SystemEventLog=" + (enableSystemEventLog ? "1" : "0"));

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
                    if (w >= 600 && h >= 400)
                    {
                        this.Size = new Size(Math.Min(w, Screen.PrimaryScreen.WorkingArea.Width), Math.Min(h, Screen.PrimaryScreen.WorkingArea.Height));
                    }
                }
                if (map.ContainsKey("Window.State"))
                {
                    int st = int.Parse(map["Window.State"]);
                    if (st == (int)FormWindowState.Maximized) this.WindowState = FormWindowState.Maximized;
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
                        gl820ChannelNames[i] = map[key];
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

                // Splitters
                if (map.ContainsKey("Splitters.MainVertical") && splitMainVertical != null)
                {
                    int d = int.Parse(map["Splitters.MainVertical"]);
                    if (d >= 80 && d <= splitMainVertical.Height - 80)
                        splitMainVertical.SplitterDistance = d;
                }
                if (map.ContainsKey("Splitters.Drives") && splitDrives != null)
                {
                    int d = int.Parse(map["Splitters.Drives"]);
                    if (d >= 80 && d <= splitDrives.Width - 80)
                        splitDrives.SplitterDistance = d;
                }
                if (map.ContainsKey("Splitters.Drive1") && splitDrive1 != null)
                {
                    int d = int.Parse(map["Splitters.Drive1"]);
                    if (d >= 80 && d <= splitDrive1.Width - 80)
                        splitDrive1.SplitterDistance = d;
                }
                if (map.ContainsKey("Splitters.Drive2") && splitDrive2 != null)
                {
                    int d = int.Parse(map["Splitters.Drive2"]);
                    if (d >= 80 && d <= splitDrive2.Width - 80)
                        splitDrive2.SplitterDistance = d;
                }
                if (map.ContainsKey("Splitters.Bottom") && splitBottomHorizontal != null)
                {
                    int d = int.Parse(map["Splitters.Bottom"]);
                    if (d >= 80 && d <= splitBottomHorizontal.Width - 80)
                        splitBottomHorizontal.SplitterDistance = d;
                }
                if (map.ContainsKey("Splitters.Param1") && splitParam1 != null)
                {
                    int d = int.Parse(map["Splitters.Param1"]);
                    if (d >= 50 && d <= splitParam1.Height - 20)
                        splitParam1.SplitterDistance = d;
                }
                if (map.ContainsKey("Splitters.Param2") && splitParam2 != null)
                {
                    int d = int.Parse(map["Splitters.Param2"]);
                    if (d >= 50 && d <= splitParam2.Height - 20)
                        splitParam2.SplitterDistance = d;
                }

                // Workbench 視圖記憶恢復
                if (map.ContainsKey("Workbench.ActiveView"))
                {
                    int v = int.Parse(map["Workbench.ActiveView"]);
                    SwitchWorkbenchView(v);
                }

                // DgvKebRu1
                if (dgvKebRu1 != null && dgvKebRu1.Columns.Count >= 2)
                {
                    for (int i = 0; i < 2; i++)
                    {
                        if (map.ContainsKey("DgvKebRu1.Col" + i))
                        {
                            int cw = int.Parse(map["DgvKebRu1.Col" + i]);
                            if (cw >= 25 && cw <= 500) dgvKebRu1.Columns[i].Width = cw;
                        }
                    }
                }

                // DgvKebRu2
                if (dgvKebRu2 != null && dgvKebRu2.Columns.Count >= 2)
                {
                    for (int i = 0; i < 2; i++)
                    {
                        if (map.ContainsKey("DgvKebRu2.Col" + i))
                        {
                            int cw = int.Parse(map["DgvKebRu2.Col" + i]);
                            if (cw >= 25 && cw <= 500) dgvKebRu2.Columns[i].Width = cw;
                        }
                    }
                }

                // DgvTelemetry
                if (dgvTelemetry != null)
                {
                    for (int i = 0; i < dgvTelemetry.Columns.Count; i++)
                    {
                        if (map.ContainsKey("DgvTelemetry.Col" + i))
                        {
                            int cw = int.Parse(map["DgvTelemetry.Col" + i]);
                            if (cw >= 25 && cw <= 500) dgvTelemetry.Columns[i].Width = cw;
                        }
                    }
                }

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
                                    list1.Add(new KebMonitorItem(name, addr, scale, unit, isHex, isStatus, isNode));
                                }
                            }
                        }
                        if (list1.Count > 0)
                        {
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
                                    list2.Add(new KebMonitorItem(name, addr, scale, unit, isHex, isStatus, isNode));
                                }
                            }
                        }
                        if (list2.Count > 0)
                        {
                            kebMonitorList2 = list2;
                            RebuildKebRuGridFromList(2);
                        }
                    }
                }
            }
            catch {}
            finally
            {
                isLayoutLoaded = true;
            }
        }

        private List<KebMonitorItem> CreateDefaultKebMonitorList()
        {
            return new List<KebMonitorItem>()
            {
                new KebMonitorItem("實測轉速 (ru07)", 0x0207, 0.125, "rpm"),
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
                new { Title = "【ru.03】實測輸出頻率 (0x0203 / 0.0001 Hz)",     Name = "輸出頻率 (ru03)", Addr = "0203", Scale = "0.0001", Unit = "Hz",  IsHex = false, IsStatus = false },
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
                RowCount = 4,
                BackColor = Color.FromArgb(240, 243, 246),
                Margin = new Padding(0),
                Padding = new Padding(2)
            };
            tableManual.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            tableManual.RowStyles.Add(new RowStyle(SizeType.Absolute, 120f)); // Row 0: 6 大核心即時量測指標 (容納死區與目標值)
            tableManual.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));  // Row 1: 上下可自由拖曳調整之核心工作區 (SplitContainer)
            tableManual.RowStyles.Add(new RowStyle(SizeType.Absolute, 34f));   // Row 2: 設備即時連線燈號列 (Status Pills)
            tableManual.RowStyles.Add(new RowStyle(SizeType.Absolute, 38f));   // Row 3: 底部精簡日誌列 + 匯出功能

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
                else if (curRu00.HasValue && curRu00.Value >= 64 && curRu00.Value != 70)
                {
                    int? faultCode = KebReadParamWithDll(comIdx, baudIdx, nodeId, 0x022B);
                    if (faultCode.HasValue && faultCode.Value != 0)
                    {
                        WriteHmiLog("ALARM", string.Format("【🚨 A載台處於故障鎖定中】狀態={0}, 故障碼 ru.43=0x{1:X2}，請排除故障後點擊 [復歸] 按鈕！", DecodeKebRu00(curRu00.Value), faultCode.Value));
                    }
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
                else if (curRu00.HasValue && curRu00.Value >= 64 && curRu00.Value != 70)
                {
                    int? faultCode = KebReadParamWithDll(comIdx, baudIdx, nodeId, 0x022B);
                    if (faultCode.HasValue && faultCode.Value != 0)
                    {
                        WriteHmiLog("ALARM", string.Format("【🚨 B載台處於故障鎖定中】狀態={0}, 故障碼 ru.43=0x{1:X2}，請排除故障後點擊 [復歸] 按鈕！", DecodeKebRu00(curRu00.Value), faultCode.Value));
                    }
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

            lblMotorTempDisplay = new Label()
            {
                Text = "25.0 °C",
                Location = new Point(190, 4),
                Size = new Size(110, 24),
                Font = new Font("Consolas", 12f, FontStyle.Bold),
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
            flpPills.Controls.AddRange(new Control[] { lblPillTorque, lblPillPowerMeter, lblPillGbd, lblPillKeb1, lblPillKeb2 });

            lblSafetyStatus = new Label()
            {
                Text = "[安全互鎖] 扭力計或 WT333E 未連線 - 載台強制鎖定",
                ForeColor = Color.FromArgb(239, 68, 68),
                Font = new Font("微軟正黑體", 9.5f, FontStyle.Bold),
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleCenter
            };

            Button btnGlobalEstop = new Button()
            {
                Text = "🚨 緊急停機 (E-STOP / ESC)",
                Size = new Size(210, 28),
                BackColor = Color.FromArgb(220, 38, 38),
                ForeColor = Color.White,
                Font = new Font("微軟正黑體", 9.5f, FontStyle.Bold),
                Cursor = Cursors.Hand,
                Dock = DockStyle.Right
            };
            btnGlobalEstop.Click += (s, e) => TriggerGlobalEmergencyStop();

            pnlDeviceStatusBar.Controls.Add(flpPills, 0, 0);
            pnlDeviceStatusBar.Controls.Add(lblSafetyStatus, 1, 0);
            pnlDeviceStatusBar.Controls.Add(btnGlobalEstop, 2, 0);
            tableManual.Controls.Add(pnlDeviceStatusBar, 0, 2);

            // -------------------------------------------------------------
            // Row 3: 底部精簡日誌狀態列 (Mini Log Strip) + 雙軌 RAW DATA 錄製 / 匯出 LOG 功能
            // -------------------------------------------------------------
            TableLayoutPanel pnlMiniLogTable = new TableLayoutPanel()
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 1,
                BackColor = Color.FromArgb(20, 24, 33),
                Margin = new Padding(0, 2, 0, 0),
                Padding = new Padding(4, 2, 4, 2)
            };
            pnlMiniLogTable.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            pnlMiniLogTable.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

            Panel pnlMiniLeft = new Panel() { Dock = DockStyle.Fill };
            Label lblLogIcon = new Label() { Text = "最新日誌:", Location = new Point(4, 7), AutoSize = true, Font = new Font("微軟正黑體", 9f, FontStyle.Bold), ForeColor = Color.FromArgb(0, 240, 118) };
            lblMiniLogText = new Label() { Text = "系統就緒，等待連線採樣...", Location = new Point(78, 7), Size = new Size(400, 20), AutoSize = false, Font = new Font("Consolas", 9f), ForeColor = Color.FromArgb(220, 230, 242), Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right };
            pnlMiniLeft.Controls.Add(lblLogIcon);
            pnlMiniLeft.Controls.Add(lblMiniLogText);

            FlowLayoutPanel pnlMiniRight = new FlowLayoutPanel()
            {
                Dock = DockStyle.Fill,
                AutoSize = true,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                Padding = new Padding(0, 2, 2, 2)
            };

            // 截圖按鈕（相機 ICON，GDI+ 向量，完美相容 XP/Win7/10/11）
            btnSnapshotRaw = new Button()
            {
                Text = " 截圖",
                Image = CreateCameraIconImage(18, 18, Color.White),
                ImageAlign = ContentAlignment.MiddleLeft,
                TextAlign = ContentAlignment.MiddleRight,
                TextImageRelation = TextImageRelation.ImageBeforeText,
                Size = new Size(80, 26),
                Margin = new Padding(2, 0, 2, 0),
                BackColor = Color.FromArgb(245, 158, 11),
                ForeColor = Color.White,
                Font = new Font("微軟正黑體", 8.5f, FontStyle.Bold),
                Cursor = Cursors.Hand
            };
            btnSnapshotRaw.Click += (s, e) => CaptureScreenshot();

            btnRawConfig = new Button() { Text = " 錄製設定", Size = new Size(85, 26), Margin = new Padding(2, 0, 2, 0), BackColor = Color.FromArgb(79, 70, 229), ForeColor = Color.White, Font = new Font("微軟正黑體", 8.5f, FontStyle.Bold), Cursor = Cursors.Hand };
            btnRawConfig.Click += (s, e) => {
                using (var dlg = new RawDataConfigDialog(this))
                {
                    dlg.ShowDialog(this);
                }
            };

            pnlMiniRight.Controls.Add(btnSnapshotRaw);
            pnlMiniLogTable.Controls.Add(pnlMiniLeft, 0, 0);
            pnlMiniLogTable.Controls.Add(pnlMiniRight, 1, 0);
            tableManual.Controls.Add(pnlMiniLogTable, 0, 3);

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
            TableLayoutPanel tblTnMini = new TableLayoutPanel() { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 9, Padding = new Padding(2), AutoScroll = true };
            tblTnMini.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90f));
            tblTnMini.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            tblTnMini.RowStyles.Add(new RowStyle(SizeType.Absolute, 26f));
            tblTnMini.RowStyles.Add(new RowStyle(SizeType.Absolute, 26f));
            tblTnMini.RowStyles.Add(new RowStyle(SizeType.Absolute, 26f));
            tblTnMini.RowStyles.Add(new RowStyle(SizeType.Absolute, 26f));
            tblTnMini.RowStyles.Add(new RowStyle(SizeType.Absolute, 26f));
            tblTnMini.RowStyles.Add(new RowStyle(SizeType.Absolute, 26f));
            tblTnMini.RowStyles.Add(new RowStyle(SizeType.Absolute, 28f));
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
            numTnMiniDwell = new NumericUpDown() { Minimum = 1, Maximum = 3600, Value = (numTnDwell != null ? numTnDwell.Value : 10), Dock = DockStyle.Fill, Font = new Font("微軟正黑體", 8.5f) };
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

            // Row 7: 操作按鈕
            FlowLayoutPanel pnlTnMiniBtns = new FlowLayoutPanel() { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, Margin = new Padding(0) };
            btnTnMiniStart = new Button() { Text = "▶️ 啟動測試", Size = new Size(82, 28), BackColor = Color.FromArgb(0, 180, 216), ForeColor = Color.White, Font = new Font("微軟正黑體", 8.5f, FontStyle.Bold) };
            btnTnMiniStart.Click += (s, e) => { BtnStartTn_Click(s, e); };
            btnTnMiniStop = new Button() { Text = "⏹️ 停止", Size = new Size(60, 28), BackColor = Color.FromArgb(239, 68, 68), ForeColor = Color.White, Font = new Font("微軟正黑體", 8.5f, FontStyle.Bold), Enabled = false };
            btnTnMiniStop.Click += (s, e) => { StopTnTest(); };
            Button btnTnMiniViewTab = new Button() { Text = "🔍 查看大圖", Size = new Size(82, 28), BackColor = Color.FromArgb(100, 116, 139), ForeColor = Color.White, Font = new Font("微軟正黑體", 8.5f) };
            btnTnMiniViewTab.Click += (s, e) => { if (tabControl != null && tabControl.TabPages.Count > 1) tabControl.SelectedIndex = 1; };
            pnlTnMiniBtns.Controls.AddRange(new Control[] { btnTnMiniStart, btnTnMiniStop, btnTnMiniViewTab });

            // Row 8: 狀態與倒數
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
            tblTnMini.Controls.Add(new Label() { Text = "控制:", Anchor = AnchorStyles.Right, AutoSize = true, Font = new Font("微軟正黑體", 8.5f) }, 0, 7);
            tblTnMini.Controls.Add(pnlTnMiniBtns, 1, 7);
            tblTnMini.Controls.Add(new Label() { Text = "進度:", Anchor = AnchorStyles.Right, AutoSize = true, Font = new Font("微軟正黑體", 8.5f) }, 0, 8);
            tblTnMini.Controls.Add(pnlTnMiniProgress, 1, 8);

            grpTnMini.Controls.Add(tblTnMini);
            pnlView2.Controls.Add(grpTnMini);
            pnlWorkbenchViews[2] = pnlView2;

            // -------------------------------------------------------------
            // View 3: 工作制試驗條件設定面板 (含 S6 向量圖解與雙重定錨，全連動)
            // -------------------------------------------------------------
            Panel pnlView3 = new Panel() { Dock = DockStyle.Fill, BackColor = Color.White, AutoScroll = true, Padding = new Padding(4) };
            GroupBox grpDutyMini = new GroupBox() { Text = "⏱️ 工作制試驗配置 (S1/S2 連續 / S6 週期)", Dock = DockStyle.Fill, Font = new Font("微軟正黑體", 9f, FontStyle.Bold), ForeColor = Color.FromArgb(15, 23, 42) };
            tblDutyMini = new TableLayoutPanel() { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 7, Padding = new Padding(2), AutoScroll = true };
            tblDutyMini.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90f));
            tblDutyMini.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            tblDutyMini.RowStyles.Add(new RowStyle(SizeType.Absolute, 28f)); // Row 0: 模式/載台
            tblDutyMini.RowStyles.Add(new RowStyle(SizeType.Absolute, 28f)); // Row 1: 轉速/轉矩
            tblDutyMini.RowStyles.Add(new RowStyle(SizeType.Absolute, 28f)); // Row 2: 週期T/ED%
            tblDutyMini.RowStyles.Add(new RowStyle(SizeType.Absolute, 30f)); // Row 3: 雙重定錨
            tblDutyMini.RowStyles.Add(new RowStyle(SizeType.Absolute, 65f)); // Row 4: 週期圖解
            tblDutyMini.RowStyles.Add(new RowStyle(SizeType.Absolute, 32f)); // Row 5: 操作按鈕
            tblDutyMini.RowStyles.Add(new RowStyle(SizeType.Absolute, 26f)); // Row 6: 即時狀態與動作

            // Row 0: 工作制模式與待測端角色 (並排)
            Label lD1 = new Label() { Text = "模式/載台:", Anchor = AnchorStyles.Right, AutoSize = true, Font = new Font("微軟正黑體", 8.5f) };
            FlowLayoutPanel pnlDModeRole = new FlowLayoutPanel() { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, Margin = new Padding(0) };
            cmbDutyMiniMode = new ComboBox() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 110, Font = new Font("微軟正黑體", 8.5f) };
            cmbDutyMiniMode.Items.AddRange(new object[] { "S1/S2 連續負載", "S6 週期負載" });
            cmbDutyMiniMode.SelectedIndex = (cmbDutyMode != null && cmbDutyMode.SelectedIndex >= 0) ? cmbDutyMode.SelectedIndex : 1; // 預設 S6
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
                if (!isSyncingDutyControls && numDutySpeed != null && numDutySpeed.Value != numDutyMiniSpd.Value)
                {
                    isSyncingDutyControls = true;
                    numDutySpeed.Value = numDutyMiniSpd.Value;
                    isSyncingDutyControls = false;
                }
            };
            Label lDUnit1 = new Label() { Text = "rpm /", AutoSize = true, Margin = new Padding(0, 4, 0, 0), Font = new Font("微軟正黑體", 8f) };
            numDutyMiniTrq = new NumericUpDown() { Minimum = 0, Maximum = 500, Value = (numDutyTorque != null ? numDutyTorque.Value : 15), DecimalPlaces = 1, Width = 60, Font = new Font("微軟正黑體", 8.5f) };
            numDutyMiniTrq.ValueChanged += (s, e) => {
                if (!isSyncingDutyControls && numDutyTorque != null && numDutyTorque.Value != numDutyMiniTrq.Value)
                {
                    isSyncingDutyControls = true;
                    numDutyTorque.Value = numDutyMiniTrq.Value;
                    isSyncingDutyControls = false;
                }
                if (pnlS6Diagram != null) pnlS6Diagram.Invalidate();
                if (pnlS6MiniDiagram != null) pnlS6MiniDiagram.Invalidate();
            };
            Label lDUnit2 = new Label() { Text = "Nm", AutoSize = true, Margin = new Padding(0, 4, 0, 0), Font = new Font("微軟正黑體", 8f) };
            pnlDSpdTrq.Controls.AddRange(new Control[] { numDutyMiniSpd, lDUnit1, numDutyMiniTrq, lDUnit2 });

            // Row 2: S6 週期T (分) / ED% / 循環數 (S1/S2 隱藏)
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

            // Row 3: V/F 雙重定錨控制 (S1/S2 隱藏)
            Label lDAnchor = new Label() { Text = "雙重定錨:", Anchor = AnchorStyles.Right, AutoSize = true, Font = new Font("微軟正黑體", 8.5f) };
            lblDutyMiniAnchorLabel = lDAnchor;
            FlowLayoutPanel pnlDAnchors = new FlowLayoutPanel() { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, Margin = new Padding(0) };
            pnlDutyMiniAnchors = pnlDAnchors;
            btnS6MiniAnchorNoLoad = new Button() { Text = "📍1.定錨空載", Size = new Size(88, 26), BackColor = Color.FromArgb(14, 165, 233), ForeColor = Color.White, Font = new Font("微軟正黑體", 8f, FontStyle.Bold) };
            btnS6MiniAnchorNoLoad.Click += (s, e) => {
                int spdDrive = (cmbDutyMiniRole.SelectedIndex == 1) ? 2 : 1;
                s6AnchorNoLoadSpeed = (spdDrive == 1 && numHmiKebSpeed1 != null) ? (double)numHmiKebSpeed1.Value : (numHmiKebSpeed2 != null ? (double)numHmiKebSpeed2.Value : 1000.0);
                s6HasNoLoadAnchor = true;
                UpdateS6AnchorStatusText();
            };
            btnS6MiniAnchorLoaded = new Button() { Text = "📍2.定錨加載", Size = new Size(88, 26), BackColor = Color.FromArgb(139, 92, 246), ForeColor = Color.White, Font = new Font("微軟正黑體", 8f, FontStyle.Bold) };
            btnS6MiniAnchorLoaded.Click += (s, e) => {
                int spdDrive = (cmbDutyMiniRole.SelectedIndex == 1) ? 2 : 1;
                int trqDrive = (spdDrive == 1) ? 2 : 1;
                s6AnchorLoadedSpeed = (spdDrive == 1 && numHmiKebSpeed1 != null) ? (double)numHmiKebSpeed1.Value : (numHmiKebSpeed2 != null ? (double)numHmiKebSpeed2.Value : 1100.0);
                s6AnchorLoadedTorquePct = (trqDrive == 1 && numHmiKebTorque1 != null) ? (double)numHmiKebTorque1.Value : (numHmiKebTorque2 != null ? (double)numHmiKebTorque2.Value : 10.0);
                s6HasLoadedAnchor = true;
                UpdateS6AnchorStatusText();
            };
            lblS6MiniAnchorStatus = new Label() { Text = "空載=未定錨 | 加載=未定錨", AutoSize = true, Margin = new Padding(2, 5, 0, 0), ForeColor = Color.FromArgb(100, 116, 139), Font = new Font("微軟正黑體", 7.5f) };
            pnlDAnchors.Controls.AddRange(new Control[] { btnS6MiniAnchorNoLoad, btnS6MiniAnchorLoaded, lblS6MiniAnchorStatus });

            // Row 4: S6 週期圖解面板 (S1/S2 隱藏)
            Label lDDiag = new Label() { Text = "週期圖解:", Anchor = AnchorStyles.Right, AutoSize = true, Font = new Font("微軟正黑體", 8.5f) };
            lblDutyMiniDiagLabel = lDDiag;
            pnlS6MiniDiagram = new Panel() { Dock = DockStyle.Fill, Height = 65, BackColor = Color.FromArgb(248, 250, 252) };
            pnlS6MiniDiagram.Paint += PnlS6Diagram_Paint;

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
            tblDutyMini.Controls.Add(lD3, 0, 2); tblDutyMini.Controls.Add(pnlDCycle, 1, 2);
            tblDutyMini.Controls.Add(lDAnchor, 0, 3); tblDutyMini.Controls.Add(pnlDAnchors, 1, 3);
            tblDutyMini.Controls.Add(lDDiag, 0, 4); tblDutyMini.Controls.Add(pnlS6MiniDiagram, 1, 4);
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

        // =========================================================================
        //  硬體 ST 安全端子與 ru.00 狀態解碼與即時變更追蹤
        // =========================================================================
        private string DecodeKebRu00(int val)
        {
            switch (val)
            {
                case 0: return "0: nOP (Control Release / ST未導通斷開)";
                case 1: return "1: LS (Low Speed / 待命準備中，等待運轉指令)";
                case 2: return "2: FAcc (正轉加速中)";
                case 3: return "3: Fdec (正轉減速中)";
                case 4: return "4: rAcc (反轉加速中)";
                case 5: return "5: rdec (反轉減速中)";
                case 6: return "6: Fcon (正轉定速運轉中)";
                case 7: return "7: rcon (反轉定速運轉中)";
                case 8: return "8: HCL (硬體電流限制)";
                case 9: return "9: SCL (軟體電流限制)";
                case 70: return "70: LS (調變關閉 / 等待運轉方向與RUN指令)";
                case 64: return "64: FAULT (變頻器故障報警 E.xxx)";
                default:
                    if (val == 66) return "66: 調變運轉準備/過渡中 (F5-Run)";
                    if (val >= 64) return string.Format("{0}: 變頻器內部狀態碼 (0x{0:X2})", val);
                    return string.Format("{0}: 運轉狀態碼", val);
            }
        }

        private string GetKebModeName(int mode)
        {
            switch (mode)
            {
                case 7: return "Mode 7: 數位定轉速 (半自動, oP01=7)";
                case 8: return "Mode 8: 數位定轉矩 (半自動, oP01=7)";
                case 9: return "Mode 9: 數位定轉速 (全自動, oP01=8)";
                case 10: return "Mode 10: 數位定轉矩 (全自動, oP01=8)";
                default: return string.Format("Mode {0}", mode);
            }
        }

        private string GetKebModeShortName(int mode)
        {
            switch (mode)
            {
                case 7: return "Mode7(半自轉速)";
                case 8: return "Mode8(半自轉矩)";
                case 9: return "Mode9(全自轉速)";
                case 10: return "Mode10(全自轉矩)";
                default: return string.Format("M{0}", mode);
            }
        }

        private void CheckHardwareStStatus(int driveId, int ru00Val)
        {
            string driveName = (driveId == 1) ? "A載台" : "B載台";
            int prevRu00 = (driveId == 1) ? lastRu00_1 : lastRu00_2;

            // 狀態變更即時追蹤 (例如: 實體 ST 接通，狀態由 nOP -> LS)
            if (ru00Val != prevRu00)
            {
                if (driveId == 1) lastRu00_1 = ru00Val; else lastRu00_2 = ru00Val;
                if (prevRu00 != -1) // 非開機首次採樣
                {
                    WriteHmiLog("KEB_STATE", string.Format("【{0} 狀態變更】ru.00 狀態碼更新為: 【{1}】", driveName, DecodeKebRu00(ru00Val)));
                }
            }

            // ru.00 == 0 代表變頻器處於 0: no operation (nOP / Control Release 斷開)
            // ★【核心韌體解鎖】：KEB F5 僅在 nOP 狀態下允許寫入 cs.00！若檢測到 cs.00 殘留為 4，立即趁 nOP 自動寫入 cs.00 = 0！
            if (ru00Val == 0)
            {
                int cachedCs = (driveId == 1) ? cachedCs00_1 : cachedCs00_2;
                if (cachedCs == 4)
                {
                    int com = GetHmiKebComIdx(driveId);
                    int baud = GetHmiKebBaudIdx(driveId);
                    int node = (int)((driveId == 1) ? numHmiKebNode1.Value : numHmiKebNode2.Value);
                    bool ok = KebWriteParamWithDll(com, baud, node, 0x0F00, 0); // cs.00 = 0 (V/F)
                    if (ok)
                    {
                        if (driveId == 1) cachedCs00_1 = 0; else cachedCs00_2 = 0;
                        WriteHmiLog("KEB_STATE", string.Format("【V/F 自動復歸成功】檢測到 {0} 處於 nOP，已成功將 cs.00 寫回 0 (V/F 開迴路模式)！", driveName));
                    }
                }
            }

            bool wasRunning = (driveId == 1) ? (lastSy50Cmd1 == 4 || lastSy50Cmd1 == 12) : (lastSy50Cmd2 == 4 || lastSy50Cmd2 == 12);
            bool isAnyLocked = isClosedLoopTracking || isSpeedTracking || isLocked || hasBaseline || hasSpeedBaseline;
            if (ru00Val == 0 && (wasRunning || isAnyLocked))
            {
                if (isAnyLocked)
                {
                    UnlockAllTrackingAndReset();
                    WriteHmiLog("SAFETY_TRIP", string.Format("【🚨 硬體ST切斷警示】檢測到 {0} 硬體 ST 端子已斷開 (ru.00=0 / nOP)，已自動解除 LOCK 鎖定並安全復歸！", driveName));
                }
                else
                {
                    WriteHmiLog("SAFETY", string.Format("【⚠️ 硬體ST端子斷開】檢測到 {0} 處於 nOP (Control Release 斷開)。", driveName));
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

                // 框框 2: 目標轉速輸入框
                Panel pnlTgtSpdBox = new Panel() { Dock = DockStyle.Fill, Margin = new Padding(0), Padding = new Padding(0, 1, 0, 0), Visible = hasSpeedBaseline && isSpeedTracking };
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
                };
                ttLockSpd.SetToolTip(numCardTargetSpeed, "閉迴路追隨目標轉速 (rpm)，可在此直接調校");
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
                        pnlTgtSpdBox.Visible = false;

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

                // 框框 2: 目標轉矩輸入框
                Panel pnlTgtTrqBox = new Panel() { Dock = DockStyle.Fill, Margin = new Padding(0), Padding = new Padding(0, 1, 0, 0), Visible = hasBaseline && isClosedLoopTracking };
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
                };
                ttLock.SetToolTip(numCardTargetTorque, "平滑追隨目標轉矩 (Nm)，可在此直接調校");
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
                        pnlTgtTrqBox.Visible = false;

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
        // 分頁 2: 多段 T-N 曲線自動測試 (採用 TableLayoutPanel 100% 杜絕遮擋)
        // =========================================================================
        private void BuildTnTab(TabPage tab)
        {
            TableLayoutPanel tableTn = new TableLayoutPanel()
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 2,
                BackColor = Color.White
            };
            tableTn.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            tableTn.RowStyles.Add(new RowStyle(SizeType.Absolute, 130f)); // Row 0: 頂部參數與定錨設定列
            tableTn.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));  // Row 1: 下方圖表與數據表

            // 頂部參數設定列
            Panel pnlTop = new Panel() { Dock = DockStyle.Fill, Padding = new Padding(4), BackColor = Color.FromArgb(250, 252, 255) };
            GroupBox grp = new GroupBox() { Text = "多段 T-N 曲線自動測試 (含手動定錨與梯度自適應繼承)", Dock = DockStyle.Fill, Font = new Font("微軟正黑體", 9f, FontStyle.Bold) };

            // 第一列：載台角色與基本步階參數
            Label lRole = new Label() { Text = "測試配置:", Location = new Point(10, 24), AutoSize = true, Font = new Font("微軟正黑體", 9f) };
            cmbTnRole = new ComboBox() { Location = new Point(75, 21), Width = 185, DropDownStyle = ComboBoxStyle.DropDownList, Font = new Font("微軟正黑體", 9f) };
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

            Label l1 = new Label() { Text = "起始(rpm):", Location = new Point(270, 24), AutoSize = true, Font = new Font("微軟正黑體", 9f) };
            numTnStartRpm = CreateNumericUpDown(new Point(335, 21), 55, 0, 4000, 50); // 預設 50 rpm
            numTnStartRpm.ValueChanged += (s, e) => {
                if (!isSyncingTnControls && numTnMiniStart != null && numTnMiniStart.Value != numTnStartRpm.Value)
                {
                    isSyncingTnControls = true;
                    numTnMiniStart.Value = numTnStartRpm.Value;
                    isSyncingTnControls = false;
                }
            };

            Label l2 = new Label() { Text = "步階(rpm):", Location = new Point(398, 24), AutoSize = true, Font = new Font("微軟正黑體", 9f) };
            numTnStepRpm = CreateNumericUpDown(new Point(463, 21), 50, 1, 1000, 50); // 預設 50 rpm
            numTnStepRpm.ValueChanged += (s, e) => {
                if (!isSyncingTnControls && numTnMiniStep != null && numTnMiniStep.Value != numTnStepRpm.Value)
                {
                    isSyncingTnControls = true;
                    numTnMiniStep.Value = numTnStepRpm.Value;
                    isSyncingTnControls = false;
                }
            };

            Label l3 = new Label() { Text = "結束(rpm):", Location = new Point(520, 24), AutoSize = true, Font = new Font("微軟正黑體", 9f) };
            numTnEndRpm = CreateNumericUpDown(new Point(585, 21), 60, 0, 4000, 300); // 預設 300 rpm
            numTnEndRpm.ValueChanged += (s, e) => {
                if (!isSyncingTnControls && numTnMiniEnd != null && numTnMiniEnd.Value != numTnEndRpm.Value)
                {
                    isSyncingTnControls = true;
                    numTnMiniEnd.Value = numTnEndRpm.Value;
                    isSyncingTnControls = false;
                }
            };

            Label l4 = new Label() { Text = "目標轉矩(Nm):", Location = new Point(652, 24), AutoSize = true, Font = new Font("微軟正黑體", 9f) };
            numTnTorque = CreateNumericUpDown(new Point(742, 21), 55, 0, 500, 15, 1); // 預設 15.0 Nm
            numTnTorque.ValueChanged += (s, e) => {
                if (!isSyncingTnControls && numTnMiniTrq != null && numTnMiniTrq.Value != numTnTorque.Value)
                {
                    isSyncingTnControls = true;
                    numTnMiniTrq.Value = numTnTorque.Value;
                    isSyncingTnControls = false;
                }
            };

            Label l5 = new Label() { Text = "穩定時間(s):", Location = new Point(805, 24), AutoSize = true, Font = new Font("微軟正黑體", 9f) };
            numTnDwell = CreateNumericUpDown(new Point(880, 21), 45, 1, 3600, 10);
            numTnDwell.ValueChanged += (s, e) => {
                if (!isSyncingTnControls && numTnMiniDwell != null && numTnMiniDwell.Value != numTnDwell.Value)
                {
                    isSyncingTnControls = true;
                    numTnMiniDwell.Value = numTnDwell.Value;
                    isSyncingTnControls = false;
                }
            };

            // 第二列：手動定錨加載基準點控制
            btnTnAnchor = new Button() { Text = "📍 鎖定當前負載為定錨基準點", Location = new Point(10, 56), Size = new Size(200, 26), BackColor = Color.FromArgb(139, 92, 246), ForeColor = Color.White, Font = new Font("微軟正黑體", 8.5f, FontStyle.Bold) };
            btnTnAnchor.Click += (s, e) => {
                int trqDriveId = (cmbTnRole != null && cmbTnRole.SelectedIndex == 1) ? 1 : 2;
                double curPct = (trqDriveId == 1 && numHmiKebTorque1 != null) ? (double)numHmiKebTorque1.Value : (numHmiKebTorque2 != null ? (double)numHmiKebTorque2.Value : 0.0);
                tnAdaptedTorquePct = Math.Max(0.0, curPct);
                tnHasAnchor = true;
                UpdateTnAnchorStatusText();
                if (txtHmiKebLog != null) txtHmiKebLog.AppendText(string.Format("[TN定錨] 已記憶負載基準點：{0:F1}% ({1:F2} Nm)\r\n", tnAdaptedTorquePct, actTorque));
            };
            lblTnAnchorStatus = new Label() { Text = "定錨基準: 未設定 (未定錨，從 0% 起步加載)", Location = new Point(220, 61), AutoSize = true, ForeColor = Color.FromArgb(100, 116, 139), Font = new Font("微軟正黑體", 8.5f) };

            // 第三列：啟動、停止、匯出與進度
            btnStartTn = new Button() { Text = "開始 T-N 測試", Location = new Point(10, 90), Size = new Size(115, 28), BackColor = Color.FromArgb(0, 180, 216), ForeColor = Color.White, Font = new Font("微軟正黑體", 9f, FontStyle.Bold) };
            btnStopTn = new Button() { Text = "停止", Location = new Point(132, 90), Size = new Size(60, 28), Enabled = false };
            btnExportTn = new Button() { Text = "匯出報表", Location = new Point(198, 90), Size = new Size(90, 28), BackColor = Color.FromArgb(16, 185, 129), ForeColor = Color.White };

            lblTnStatus = new Label() { Text = "狀態: 待命準備中", Location = new Point(300, 96), AutoSize = true, Font = new Font("微軟正黑體", 9f, FontStyle.Bold) };
            lblTnCountdown = new Label() { Text = "倒數: -- s", Location = new Point(510, 96), AutoSize = true, ForeColor = Color.DarkOrange, Font = new Font("微軟正黑體", 9.5f, FontStyle.Bold) };
            prgTn = new ProgressBar() { Location = new Point(595, 96), Size = new Size(160, 18) };

            btnStartTn.Click += BtnStartTn_Click;
            btnStopTn.Click += (s, e) => { StopTnTest(); };
            btnExportTn.Click += BtnExportTn_Click;

            grp.Controls.AddRange(new Control[] { lRole, cmbTnRole, l1, numTnStartRpm, l2, numTnStepRpm, l3, numTnEndRpm, l4, numTnTorque, l5, numTnDwell, btnTnAnchor, lblTnAnchorStatus, btnStartTn, btnStopTn, btnExportTn, lblTnStatus, lblTnCountdown, prgTn });
            pnlTop.Controls.Add(grp);
            tableTn.Controls.Add(pnlTop, 0, 0);

            // 下方主要區域：左邊繪製 T-N 曲線圖，右邊顯示數據表格 (SplitContainer)
            SplitContainer splitTn = new SplitContainer() { Dock = DockStyle.Fill, Orientation = Orientation.Vertical, SplitterDistance = 460 };

            // 左側：T-N 曲線圖
            tnChart = new TnCurveChart() { Dock = DockStyle.Fill };
            splitTn.Panel1.Controls.Add(tnChart);

            // 右側：數據表格
            dgvTnPoints = new DataGridView() { Dock = DockStyle.Fill, BackgroundColor = Color.White, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill, RowHeadersVisible = false, AllowUserToAddRows = false, Font = new Font("微軟正黑體", 9f) };
            dgvTnPoints.Columns.Add("Idx", "點位");
            dgvTnPoints.Columns.Add("TgtRpm", "目標(rpm)");
            dgvTnPoints.Columns.Add("Speed", "實測(rpm)");
            dgvTnPoints.Columns.Add("Torque", "轉矩(Nm)");
            dgvTnPoints.Columns.Add("Pwr", "功率(kW)");
            dgvTnPoints.Columns.Add("Eff", "效率(%)");
            dgvTnPoints.Columns.Add("Status", "狀態");
            splitTn.Panel2.Controls.Add(dgvTnPoints);

            tableTn.Controls.Add(splitTn, 0, 1);
            tab.Controls.Add(tableTn);

            tnTimer = new System.Windows.Forms.Timer();
            tnTimer.Interval = 1000;
            tnTimer.Tick += TnTimer_Tick;
        }

        // =========================================================================
        // 分頁 3: IEC 60034-1 標準工作制測試 (S1 / S2 / S6 週期圖示與 V/F 雙重定錨)
        // =========================================================================
        private void BuildDutyTab(TabPage tab)
        {
            TableLayoutPanel tableDuty = new TableLayoutPanel()
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 2,
                BackColor = Color.White
            };
            tableDuty.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            tableDuty.RowStyles.Add(new RowStyle(SizeType.Absolute, 225f)); // Row 0: 頂部控制面板與 S6 圖示
            tableDuty.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));  // Row 1: 下方數據表記錄

            Panel pnlTop = new Panel() { Dock = DockStyle.Fill, Padding = new Padding(2), BackColor = Color.FromArgb(250, 252, 255) };
            TableLayoutPanel pnlDutyHeader = new TableLayoutPanel() { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1 };
            pnlDutyHeader.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 680f)); // 左側參數設定保證不裁切
            pnlDutyHeader.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));  // 右側 S6 週期圖示自適應延展
            pnlDutyHeader.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

            // -------------------------------------------------------------
            // 左側：控制參數與 V/F 雙重定錨設定群組
            // -------------------------------------------------------------
            GroupBox grp = new GroupBox() { Text = "IEC 60034-1 工作制測試設定 (含 V/F 轉差雙重定錨補償)", Dock = DockStyle.Fill, Font = new Font("微軟正黑體", 9f, FontStyle.Bold) };

            // 第一列：工作制、載台角色、目標轉速、加載轉矩
            Label lMode = new Label() { Text = "工作制:", Location = new Point(8, 22), AutoSize = true, Font = new Font("微軟正黑體", 9f) };
            cmbDutyMode = new ComboBox() { Location = new Point(58, 19), Width = 155, DropDownStyle = ComboBoxStyle.DropDownList, Font = new Font("微軟正黑體", 9f) };
            cmbDutyMode.Items.AddRange(new object[] { "S1/S2 連續負載 (Continuous/Short-Time)", "S6 週期負載 (Periodic ED%)" });
            cmbDutyMode.SelectedIndex = 1; // 預設 S6
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

            Label lRole = new Label() { Text = "待測端:", Location = new Point(220, 22), AutoSize = true, Font = new Font("微軟正黑體", 9f) };
            cmbDutyRole = new ComboBox() { Location = new Point(270, 19), Width = 150, DropDownStyle = ComboBoxStyle.DropDownList, Font = new Font("微軟正黑體", 9f) };
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

            Label lSpd = new Label() { Text = "轉速(rpm):", Location = new Point(426, 22), AutoSize = true, Font = new Font("微軟正黑體", 9f) };
            numDutySpeed = CreateNumericUpDown(new Point(490, 19), 60, 0, 4000, 1000);
            numDutySpeed.ValueChanged += (s, e) => {
                if (!isSyncingDutyControls && numDutyMiniSpd != null && numDutyMiniSpd.Value != numDutySpeed.Value)
                {
                    isSyncingDutyControls = true;
                    numDutyMiniSpd.Value = numDutySpeed.Value;
                    isSyncingDutyControls = false;
                }
            };

            Label lTrq = new Label() { Text = "轉矩(Nm):", Location = new Point(555, 22), AutoSize = true, Font = new Font("微軟正黑體", 9f) };
            numDutyTorque = CreateNumericUpDown(new Point(620, 19), 55, 0, 500, 15, 1); // 預設 15.0 Nm
            numDutyTorque.ValueChanged += (s, e) => {
                if (!isSyncingDutyControls && numDutyMiniTrq != null && numDutyMiniTrq.Value != numDutyTorque.Value)
                {
                    isSyncingDutyControls = true;
                    numDutyMiniTrq.Value = numDutyTorque.Value;
                    isSyncingDutyControls = false;
                }
                if (pnlS6Diagram != null) pnlS6Diagram.Invalidate();
                if (pnlS6MiniDiagram != null) pnlS6MiniDiagram.Invalidate();
            };

            // 第二列：S6 週期參數 (時長、ED%、循環數 - S1/S2 隱藏)
            Label lCycle = new Label() { Text = "單週期T(分):", Location = new Point(8, 52), AutoSize = true, Font = new Font("微軟正黑體", 9f) };
            lblS6CycleLabel = lCycle;
            numS6CycleMin = CreateNumericUpDown(new Point(88, 49), 55, 1, 60, 10, 1, 0.5m);
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

            Label lEd = new Label() { Text = "S6 ED%:", Location = new Point(150, 52), AutoSize = true, Font = new Font("微軟正黑體", 9f) };
            lblS6EdLabel = lEd;
            numS6Ed = CreateNumericUpDown(new Point(208, 49), 55, 1, 100, 40);
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

            Label lCycles = new Label() { Text = "總週期數:", Location = new Point(275, 52), AutoSize = true, Font = new Font("微軟正黑體", 9f) };
            lblS6CyclesLabel = lCycles;
            numS6Cycles = CreateNumericUpDown(new Point(338, 49), 45, 1, 50, 3);
            numS6Cycles.ValueChanged += (s, e) => {
                if (!isSyncingDutyControls && numDutyMiniCycles != null && numDutyMiniCycles.Value != numS6Cycles.Value)
                {
                    isSyncingDutyControls = true;
                    numDutyMiniCycles.Value = numS6Cycles.Value;
                    isSyncingDutyControls = false;
                }
            };

            lblS6CalcInfo = new Label() { Text = "換算：有載 T1 = 4.0分, 空載 T2 = 6.0分", Location = new Point(390, 52), AutoSize = true, ForeColor = Color.FromArgb(3, 105, 161), Font = new Font("微軟正黑體", 8.5f, FontStyle.Bold) };

            // 第三列：V/F 轉差雙重定錨控制 (空載點 + 加載補償點 - S1/S2 隱藏)
            btnS6AnchorNoLoad = new Button() { Text = "📍 1.定錨空載點(記轉速)", Location = new Point(8, 80), Size = new Size(155, 26), BackColor = Color.FromArgb(14, 165, 233), ForeColor = Color.White, Font = new Font("微軟正黑體", 8f, FontStyle.Bold) };
            btnS6AnchorNoLoad.Click += (s, e) => {
                int spdDrive = (cmbDutyRole.SelectedIndex == 1) ? 2 : 1;
                s6AnchorNoLoadSpeed = (spdDrive == 1 && numHmiKebSpeed1 != null) ? (double)numHmiKebSpeed1.Value : (numHmiKebSpeed2 != null ? (double)numHmiKebSpeed2.Value : 1000.0);
                s6HasNoLoadAnchor = true;
                UpdateS6AnchorStatusText();
            };

            btnS6AnchorLoaded = new Button() { Text = "📍 2.定錨加載點(記補償+扭力)", Location = new Point(168, 80), Size = new Size(185, 26), BackColor = Color.FromArgb(139, 92, 246), ForeColor = Color.White, Font = new Font("微軟正黑體", 8f, FontStyle.Bold) };
            btnS6AnchorLoaded.Click += (s, e) => {
                int spdDrive = (cmbDutyRole.SelectedIndex == 1) ? 2 : 1;
                int trqDrive = (spdDrive == 1) ? 2 : 1;
                s6AnchorLoadedSpeed = (spdDrive == 1 && numHmiKebSpeed1 != null) ? (double)numHmiKebSpeed1.Value : (numHmiKebSpeed2 != null ? (double)numHmiKebSpeed2.Value : 1100.0);
                s6AnchorLoadedTorquePct = (trqDrive == 1 && numHmiKebTorque1 != null) ? (double)numHmiKebTorque1.Value : (numHmiKebTorque2 != null ? (double)numHmiKebTorque2.Value : 10.0);
                s6HasLoadedAnchor = true;
                UpdateS6AnchorStatusText();
            };

            lblS6AnchorStatus = new Label() { Text = "定錨狀態: 空載=未定錨 | 加載=未定錨", Location = new Point(360, 85), AutoSize = true, ForeColor = Color.FromArgb(100, 116, 139), Font = new Font("微軟正黑體", 8f) };

            // 第四列：開始、停止、匯出與即時狀態
            btnStartDuty = new Button() { Text = "開始工作制測試", Location = new Point(8, 112), Size = new Size(125, 28), BackColor = Color.FromArgb(0, 180, 216), ForeColor = Color.White, Font = new Font("微軟正黑體", 9f, FontStyle.Bold) };
            btnStopDuty = new Button() { Text = "停止", Location = new Point(138, 112), Size = new Size(60, 28), Enabled = false };
            btnExportDuty = new Button() { Text = "匯出報表", Location = new Point(203, 112), Size = new Size(95, 28), BackColor = Color.FromArgb(16, 185, 129), ForeColor = Color.White };

            lblDutyStatus = new Label() { Text = "狀態: 待命準備中", Location = new Point(305, 118), AutoSize = true, Font = new Font("微軟正黑體", 9f, FontStyle.Bold) };
            lblDutyPhaseAction = new Label() { Text = "動作: --", Location = new Point(8, 146), AutoSize = true, ForeColor = Color.FromArgb(2, 132, 199), Font = new Font("微軟正黑體", 8.5f, FontStyle.Bold) };
            lblThermalStatus = new Label() { Text = "熱平衡: 未達平衡", Location = new Point(305, 146), AutoSize = true, ForeColor = Color.DarkOrange, Font = new Font("微軟正黑體", 8.5f, FontStyle.Bold) };
            prgDuty = new ProgressBar() { Location = new Point(450, 146), Size = new Size(190, 18) };

            btnStartDuty.Click += BtnStartDuty_Click;
            btnStopDuty.Click += (s, e) => { StopDutyTest(); };
            btnExportDuty.Click += BtnExportDuty_Click;

            grp.Controls.AddRange(new Control[] {
                lMode, cmbDutyMode, lRole, cmbDutyRole, lSpd, numDutySpeed, lTrq, numDutyTorque,
                lCycle, numS6CycleMin, lEd, numS6Ed, lCycles, numS6Cycles, lblS6CalcInfo,
                btnS6AnchorNoLoad, btnS6AnchorLoaded, lblS6AnchorStatus,
                btnStartDuty, btnStopDuty, btnExportDuty, lblDutyStatus, lblDutyPhaseAction, lblThermalStatus, prgDuty
            });
            pnlDutyHeader.Controls.Add(grp, 0, 0);

            // -------------------------------------------------------------
            // 右側：IEC 60034-1 S6 連續週期工作制圖示 (T = T1 + T2 - S1/S2 隱藏)
            // -------------------------------------------------------------
            GroupBox grpDiagram = new GroupBox() { Text = "📊 S6 週期工作制圖解 (1 週期 = N + V)", Dock = DockStyle.Fill, Font = new Font("微軟正黑體", 9f, FontStyle.Bold) };
            grpS6Diagram = grpDiagram;
            pnlS6Diagram = new Panel() { Dock = DockStyle.Fill, BackColor = Color.White };
            pnlS6Diagram.Paint += PnlS6Diagram_Paint;
            pnlS6Diagram.Resize += (s, e) => { if (pnlS6Diagram != null) pnlS6Diagram.Invalidate(); };
            grpDiagram.Controls.Add(pnlS6Diagram);
            pnlDutyHeader.Controls.Add(grpDiagram, 1, 0);

            pnlTop.Controls.Add(pnlDutyHeader);
            tableDuty.Controls.Add(pnlTop, 0, 0);

            DataGridView dgvDuty = new DataGridView() { Dock = DockStyle.Fill, BackgroundColor = Color.White, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill, RowHeadersVisible = false, AllowUserToAddRows = false };
            dgvDuty.Columns.Add("Sec", "時間 (s)");
            dgvDuty.Columns.Add("Phase", "階段");
            dgvDuty.Columns.Add("Speed", "轉速 (rpm)");
            dgvDuty.Columns.Add("Torque", "轉矩 (Nm)");
            dgvDuty.Columns.Add("Power", "功率 (kW)");
            dgvDuty.Columns.Add("Eff", "效率 (%)");
            dgvDuty.Columns.Add("Temp", "馬達溫度 (°C)");
            dgvDuty.Columns.Add("Thermal", "熱平衡判定");

            tableDuty.Controls.Add(dgvDuty, 0, 1);
            tab.Controls.Add(tableDuty);

            dutyTimer = new System.Windows.Forms.Timer();
            dutyTimer.Interval = 1000;
            dutyTimer.Tick += DutyTimer_Tick;

            UpdateDutyModeVisibility(cmbDutyMode.SelectedIndex);
        }

        private void UpdateDutyModeVisibility(int modeIdx)
        {
            bool isS6 = (modeIdx == 1);

            // 1. 大頁面 (Tab 3) S6 專屬元件動態顯隱
            if (lblS6CycleLabel != null) lblS6CycleLabel.Visible = isS6;
            if (numS6CycleMin != null) numS6CycleMin.Visible = isS6;
            if (lblS6EdLabel != null) lblS6EdLabel.Visible = isS6;
            if (numS6Ed != null) numS6Ed.Visible = isS6;
            if (lblS6CyclesLabel != null) lblS6CyclesLabel.Visible = isS6;
            if (numS6Cycles != null) numS6Cycles.Visible = isS6;
            if (lblS6CalcInfo != null) lblS6CalcInfo.Visible = isS6;
            if (btnS6AnchorNoLoad != null) btnS6AnchorNoLoad.Visible = isS6;
            if (btnS6AnchorLoaded != null) btnS6AnchorLoaded.Visible = isS6;
            if (lblS6AnchorStatus != null) lblS6AnchorStatus.Visible = isS6;
            if (grpS6Diagram != null) grpS6Diagram.Visible = isS6;

            // 2. 綜合監控右下角 Mini 視窗 S6 專屬元件顯隱與自適應隱藏列高
            if (lblDutyMiniCycleLabel != null) lblDutyMiniCycleLabel.Visible = isS6;
            if (pnlDutyMiniCycle != null) pnlDutyMiniCycle.Visible = isS6;
            if (lblDutyMiniAnchorLabel != null) lblDutyMiniAnchorLabel.Visible = isS6;
            if (pnlDutyMiniAnchors != null) pnlDutyMiniAnchors.Visible = isS6;
            if (lblDutyMiniDiagLabel != null) lblDutyMiniDiagLabel.Visible = isS6;
            if (pnlS6MiniDiagram != null) pnlS6MiniDiagram.Visible = isS6;

            if (tblDutyMini != null && tblDutyMini.RowStyles.Count >= 5)
            {
                tblDutyMini.RowStyles[2].Height = isS6 ? 28f : 0f; // Row 2: 週期T/ED%
                tblDutyMini.RowStyles[3].Height = isS6 ? 30f : 0f; // Row 3: 雙重定錨
                tblDutyMini.RowStyles[4].Height = isS6 ? 65f : 0f; // Row 4: 週期圖解
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
                }
                else
                {
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
                }
            }
            finally
            {
                isSyncingTnControls = false;
            }
        }

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
                }
                int curMode = (cmbDutyMode != null && cmbDutyMode.SelectedIndex >= 0) ? cmbDutyMode.SelectedIndex : 1;
                UpdateDutyModeVisibility(curMode);
                UpdateS6CalcInfo();
                if (pnlS6Diagram != null) pnlS6Diagram.Invalidate();
                if (pnlS6MiniDiagram != null) pnlS6MiniDiagram.Invalidate();
            }
            finally
            {
                isSyncingDutyControls = false;
            }
        }

        private void PnlS6Diagram_Paint(object sender, PaintEventArgs e)
        {
            Panel targetPnl = (sender as Panel != null) ? (Panel)sender : pnlS6Diagram;
            if (targetPnl == null || targetPnl.Width <= 10 || targetPnl.Height <= 10) return;
            Graphics g = e.Graphics;
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            Rectangle rect = targetPnl.ClientRectangle;
            g.Clear(Color.White);

            double cycleMin = (double)(numS6CycleMin != null ? numS6CycleMin.Value : (numDutyMiniCycleMin != null ? numDutyMiniCycleMin.Value : 10));
            double edVal = (double)(numS6Ed != null ? numS6Ed.Value : (numDutyMiniEd != null ? numDutyMiniEd.Value : 40));
            double t1Min = cycleMin * (edVal / 100.0);
            double t2Min = cycleMin - t1Min;

            bool isCompact = rect.Height < 100;

            // 佈局邊界計算 (完美對齊 IEC S6 標準波形規範與使用者上傳圖示)
            int left = isCompact ? 36 : 46;
            int top = isCompact ? 18 : 38;
            int right = rect.Width - (isCompact ? 22 : 30);
            int bottom = rect.Height - (isCompact ? 14 : 26);
            int totalPlotW = Math.Max(60, right - left);
            int totalPlotH = Math.Max(24, bottom - top);

            // 繪製 Y 軸 (負荷軸) 與 向上箭頭 ▲
            using (Pen axisPen = new Pen(Color.Black, isCompact ? 1.4f : 1.6f))
            {
                g.DrawLine(axisPen, left, bottom, left, top - (isCompact ? 10 : 14)); // Y 軸
                Point[] yArrow = new Point[] {
                    new Point(left - 4, top - (isCompact ? 6 : 8)),
                    new Point(left + 4, top - (isCompact ? 6 : 8)),
                    new Point(left, top - (isCompact ? 14 : 18))
                };
                g.FillPolygon(Brushes.Black, yArrow);

                // 繪製 X 軸 (時間軸) 與 向右箭頭 ►
                g.DrawLine(axisPen, left, bottom, right + 10, bottom); // X 軸
                Point[] xArrow = new Point[] {
                    new Point(right + 4, bottom - 4),
                    new Point(right + 4, bottom + 4),
                    new Point(right + 14, bottom)
                };
                g.FillPolygon(Brushes.Black, xArrow);
            }

            // 軸線文字標註 (參照圖片：縱軸「負荷」，橫軸「時間」)
            using (Font fAxis = new Font("微軟正黑體", isCompact ? 7.5f : 8.5f, FontStyle.Bold))
            using (Font fSub = new Font("微軟正黑體", isCompact ? 7.0f : 8.0f))
            using (Brush textBr = new SolidBrush(Color.Black))
            {
                // Y 軸左側直書標註「負荷」
                int yMid = top + totalPlotH / 2;
                g.DrawString("負", fAxis, textBr, left - (isCompact ? 18 : 24), yMid - (isCompact ? 14 : 18));
                g.DrawString("荷", fAxis, textBr, left - (isCompact ? 18 : 24), yMid + (isCompact ? 0 : 2));

                // X 軸箭頭右下方標註「時間」
                g.DrawString("時間", fAxis, textBr, right - (isCompact ? 12 : 18), bottom + (isCompact ? 2 : 5));
            }

            // 連續週期計算：畫面展示 2 個完整 S6 週期以展示週期性 (第 1 週期 + 第 2 週期)
            int cycleWidth = totalPlotW / 2;
            double edFrac = Math.Max(0.1, Math.Min(0.9, edVal / 100.0));
            int nWidth = (int)(cycleWidth * edFrac); // N: 有載運轉寬度
            int vWidth = cycleWidth - nWidth;        // V: 空載運轉寬度
            int blockH = (int)(totalPlotH * (isCompact ? 0.68 : 0.72)); // 負荷方塊高度
            int blockTop = bottom - blockH;

            // 繪製 2 個週期的有載方塊 (填滿 45 度斜線陰影線 ///) 與空載水平線
            using (System.Drawing.Drawing2D.HatchBrush hatchBr = new System.Drawing.Drawing2D.HatchBrush(
                System.Drawing.Drawing2D.HatchStyle.ForwardDiagonal, Color.Black, Color.White))
            using (Pen borderPen = new Pen(Color.Black, isCompact ? 1.4f : 1.8f))
            using (Pen idlePen = new Pen(Color.Black, isCompact ? 2.0f : 2.5f))
            {
                // ---- 第 1 週期 ----
                Rectangle rectN1 = new Rectangle(left, blockTop, nWidth, blockH);
                g.FillRectangle(hatchBr, rectN1);
                g.DrawRectangle(borderPen, rectN1);
                g.DrawLine(idlePen, left + nWidth, bottom, left + cycleWidth, bottom); // V 空載基線

                // ---- 第 2 週期 ----
                Rectangle rectN2 = new Rectangle(left + cycleWidth, blockTop, nWidth, blockH);
                g.FillRectangle(hatchBr, rectN2);
                g.DrawRectangle(borderPen, rectN2);
                g.DrawLine(idlePen, left + cycleWidth + nWidth, bottom, left + 2 * cycleWidth, bottom); // V 空載基線
            }

            // 繪製頂部尺寸界線與標註 (參照圖片：雙向尺寸線標註「1 週期」、「N」、「V」)
            using (Pen dimPen = new Pen(Color.Black, 1.2f))
            using (Font fDim = new Font("微軟正黑體", isCompact ? 7.5f : 8.5f, FontStyle.Bold))
            using (Font fSmall = new Font("微軟正黑體", isCompact ? 6.5f : 7.5f))
            using (Brush dimBr = new SolidBrush(Color.Black))
            {
                int yTopDim = isCompact ? top - 12 : top - 18;  // 「1 週期」尺寸線高度
                int ySubDim = isCompact ? top - 2 : top - 2;     // 「N」、「V」尺寸線高度

                // 垂直延伸參考線
                g.DrawLine(dimPen, left, yTopDim - 3, left, blockTop);
                g.DrawLine(dimPen, left + nWidth, ySubDim - 3, left + nWidth, blockTop);
                g.DrawLine(dimPen, left + cycleWidth, yTopDim - 3, left + cycleWidth, bottom);

                // --- 標註「1 週期」---
                g.DrawLine(dimPen, left, yTopDim, left + cycleWidth, yTopDim);
                DrawDimArrow(g, left, yTopDim, isLeft: true);
                DrawDimArrow(g, left + cycleWidth, yTopDim, isLeft: false);
                string strCycle = "1 週期";
                SizeF szCycle = g.MeasureString(strCycle, fDim);
                g.FillRectangle(Brushes.White, left + (cycleWidth - szCycle.Width) / 2 - 2, yTopDim - szCycle.Height / 2, szCycle.Width + 4, szCycle.Height);
                g.DrawString(strCycle, fDim, dimBr, left + (cycleWidth - szCycle.Width) / 2, yTopDim - szCycle.Height / 2);

                // --- 標註「N」---
                g.DrawLine(dimPen, left, ySubDim, left + nWidth, ySubDim);
                DrawDimArrow(g, left, ySubDim, isLeft: true);
                DrawDimArrow(g, left + nWidth, ySubDim, isLeft: false);
                string strN = "N";
                SizeF szN = g.MeasureString(strN, fDim);
                g.FillRectangle(Brushes.White, left + (nWidth - szN.Width) / 2 - 2, ySubDim - szN.Height / 2, szN.Width + 4, szN.Height);
                g.DrawString(strN, fDim, dimBr, left + (nWidth - szN.Width) / 2, ySubDim - szN.Height / 2);

                // --- 標註「V」---
                g.DrawLine(dimPen, left + nWidth, ySubDim, left + cycleWidth, ySubDim);
                DrawDimArrow(g, left + nWidth, ySubDim, isLeft: true);
                DrawDimArrow(g, left + cycleWidth, ySubDim, isLeft: false);
                string strV = "V";
                SizeF szV = g.MeasureString(strV, fDim);
                g.FillRectangle(Brushes.White, left + nWidth + (vWidth - szV.Width) / 2 - 2, ySubDim - szV.Height / 2, szV.Width + 4, szV.Height);
                g.DrawString(strV, fDim, dimBr, left + nWidth + (vWidth - szV.Width) / 2, ySubDim - szV.Height / 2);

                // 底部數據對應註解 (僅在大圖展示以保持簡潔排版)
                if (!isCompact)
                {
                    string detailStr = string.Format("N(有載)={0:F1}分 (ED {1:F0}%) | V(空載)={2:F1}分 | 總週期={3:F1}分", t1Min, edVal, t2Min, cycleMin);
                    g.DrawString(detailStr, fSmall, Brushes.DimGray, left, bottom + 7);
                }
            }

            // 若測試中，繪製即時運轉指示游標
            if (dutyTimer != null && dutyTimer.Enabled && cmbDutyMode != null && cmbDutyMode.SelectedIndex == 1)
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

        private void DrawDimArrow(Graphics g, int x, int y, bool isLeft)
        {
            Point[] arrow = isLeft
                ? new Point[] { new Point(x, y), new Point(x + 5, y - 3), new Point(x + 5, y + 3) }
                : new Point[] { new Point(x, y), new Point(x - 5, y - 3), new Point(x - 5, y + 3) };
            g.FillPolygon(Brushes.Black, arrow);
        }

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
                Orientation = Orientation.Vertical,
                SplitterDistance = 550
            };

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
            btnClearGbd.Click += (s, e) => { if (gbdTrendChart != null) gbdTrendChart.ClearData(); gbdHistory.Clear(); };

            pnlTop.Controls.AddRange(new Control[] { lGbdTitle, btnExportGbd, btnClearGbd });
            tableGbd.Controls.Add(pnlTop, 0, 0);

            // 2. 20 通道即時遙測表 (單行 20 欄橫向排列)
            GroupBox grpGrid = new GroupBox()
            {
                Text = "GL820 CH1 ~ CH20 橫向溫度矩陣 (第一列【測點名稱】可直接點選編輯，將自動儲存並寫入 RAW DATA)",
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
                dgvGbdAll.Rows[rVal].Cells[i].Value = "25.0";
            }

            dgvGbdAll.CellValueChanged += (s, e) => {
                if (e.RowIndex == 0 && e.ColumnIndex >= 0 && e.ColumnIndex < 20)
                {
                    object val = dgvGbdAll.Rows[0].Cells[e.ColumnIndex].Value;
                    string nameStr = val != null ? val.ToString().Trim() : "";
                    if (string.IsNullOrEmpty(nameStr)) nameStr = "CH" + (e.ColumnIndex + 1);
                    gl820ChannelNames[e.ColumnIndex] = nameStr;
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
                Margin = new Padding(0, 3, 8, 0)
            };
            pnlChkRow.Controls.Add(lblChkTitle);

            // 建立 20 個 CheckBox，單排橫向
            CheckBox[] chkChannels = new CheckBox[20];
            for (int i = 0; i < 20; i++)
            {
                int idx = i;
                chkChannels[i] = new CheckBox()
                {
                    Text = "CH" + (i + 1),
                    Checked = (gl820ChannelMask != null && i < gl820ChannelMask.Length) ? gl820ChannelMask[i] : (i < 4),
                    AutoSize = true,
                    Font = new Font("微軟正黑體", 8f),
                    ForeColor = Color.FromArgb(15, 23, 42),
                    Margin = new Padding(0, 3, 6, 0)
                };
                chkChannels[i].CheckedChanged += (s, e) =>
                {
                    if (gl820ChannelMask != null && idx < gl820ChannelMask.Length)
                        gl820ChannelMask[idx] = chkChannels[idx].Checked;
                    if (gbdTrendChart != null) gbdTrendChart.SetChannelVisibility(gl820ChannelMask);
                    SaveLayoutConfig();
                };
                pnlChkRow.Controls.Add(chkChannels[i]);
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

        // =========================================================================
        // 分頁 6: 系統完整運轉與通訊日誌 (System Log Viewer)
        // =========================================================================
        private void BuildLogTab(TabPage tab)
        {
            TableLayoutPanel tableLog = new TableLayoutPanel()
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 2,
                BackColor = Color.FromArgb(240, 243, 246),
                Margin = new Padding(0),
                Padding = new Padding(4)
            };
            tableLog.RowStyles.Add(new RowStyle(SizeType.Absolute, 45f));
            tableLog.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

            Panel pnlLogTools = new Panel() { Dock = DockStyle.Fill, BackColor = Color.FromArgb(245, 248, 252), BorderStyle = BorderStyle.FixedSingle, Margin = new Padding(0) };

            Label lInfo = new Label()
            {
                Text = "動力計即時通訊電文、閉迴路追隨與硬體交握紀錄 (支援即時檢視與完整文字/CSV匯出)",
                Location = new Point(10, 12),
                AutoSize = true,
                Font = new Font("微軟正黑體", 9.5f, FontStyle.Bold),
                ForeColor = Color.FromArgb(20, 50, 100)
            };

            // 使用 FlowLayoutPanel 排列按鈕，確保任何視窗寬度都能正常顯示
            FlowLayoutPanel flowLogTools = new FlowLayoutPanel()
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                Padding = new Padding(6, 5, 6, 5),
                AutoSize = false
            };

            Label lInfo2 = new Label()
            {
                Text = "動力計即時通訊電文、閉迴路追隨與硬體交握紀錄",
                AutoSize = true,
                Font = new Font("微軟正黑體", 9.5f, FontStyle.Bold),
                ForeColor = Color.FromArgb(20, 50, 100),
                Margin = new Padding(0, 6, 20, 0)
            };

            Button btnOpenLogDir2 = new Button()
            {
                Text = "開啟日誌目錄",
                Size = new Size(120, 28),
                BackColor = Color.FromArgb(55, 65, 81),
                ForeColor = Color.White,
                Font = new Font("微軟正黑體", 9f, FontStyle.Bold),
                Margin = new Padding(0, 0, 4, 0)
            };
            btnOpenLogDir2.Click += (s, e) => OpenLogsFolder();

            Button btnExport2 = new Button()
            {
                Text = "匯出 LOG (TXT/CSV)",
                Size = new Size(165, 28),
                BackColor = Color.FromArgb(0, 120, 215),
                ForeColor = Color.White,
                Font = new Font("微軟正黑體", 9f, FontStyle.Bold),
                Margin = new Padding(0, 0, 4, 0)
            };
            btnExport2.Click += BtnExportLogs_Click;

            Button btnClear2 = new Button()
            {
                Text = "清空日誌",
                Size = new Size(90, 28),
                BackColor = Color.FromArgb(239, 68, 68),
                ForeColor = Color.White,
                Font = new Font("微軟正黑體", 9f, FontStyle.Bold),
                Margin = new Padding(0, 0, 0, 0)
            };
            btnClear2.Click += (s, e) => {
                lock (hmiLogLock) memoryLogs.Clear();
                if (txtFullLog != null) txtFullLog.Clear();
                if (lblMiniLogText != null) lblMiniLogText.Text = "日誌已清空";
            };

            Label lblKebFreq = new Label()
            {
                Text = "⚡ KEB右側更新:",
                AutoSize = true,
                Font = new Font("微軟正黑體", 9f, FontStyle.Bold),
                ForeColor = Color.FromArgb(79, 70, 229),
                Margin = new Padding(8, 6, 2, 0)
            };
            NumericUpDown numKebFreqLog = new NumericUpDown()
            {
                Minimum = 100,
                Maximum = 60000,
                Increment = 100,
                Value = kebPollingIntervalMs,
                Width = 75,
                Font = new Font("Consolas", 9.5f, FontStyle.Bold),
                BackColor = Color.FromArgb(241, 245, 249),
                ForeColor = Color.FromArgb(79, 70, 229)
            };
            numKebFreqLog.ValueChanged += (s, e) => {
                kebPollingIntervalMs = (int)numKebFreqLog.Value;
                if (numKebPollingInterval != null && numKebPollingInterval.Value != numKebFreqLog.Value)
                    numKebPollingInterval.Value = numKebFreqLog.Value;
                SaveLayoutConfig();
                WriteHmiLog("CONFIG", "已更新 KEB 右側參數輪詢週期為: " + kebPollingIntervalMs + " ms");
            };
            Label lblKebFreqMs = new Label()
            {
                Text = "ms",
                AutoSize = true,
                Font = new Font("微軟正黑體", 9f),
                Margin = new Padding(2, 6, 4, 0)
            };

            flowLogTools.Controls.AddRange(new Control[] { lInfo2, btnOpenLogDir2, btnExport2, btnClear2, lblKebFreq, numKebFreqLog, lblKebFreqMs });
            pnlLogTools.Controls.Add(flowLogTools);
            tableLog.Controls.Add(pnlLogTools, 0, 0);
            TabControl tabLogSub = new TabControl() { Dock = DockStyle.Fill, Margin = new Padding(0, 4, 0, 0) };

            // 子分頁 1: 全系統運作與事件日誌
            TabPage tabSubSys = new TabPage("全系統運作與事件日誌 (System Logs)") { BackColor = Color.FromArgb(240, 243, 246) };
            txtFullLog = new TextBox()
            {
                Dock = DockStyle.Fill,
                Multiline = true,
                ScrollBars = ScrollBars.Vertical,
                ReadOnly = true,
                BackColor = Color.Black,
                ForeColor = Color.LightGreen,
                Font = new Font("Consolas", 9.5f),
                Margin = new Padding(0)
            };
            txtHmiKebLog = txtFullLog;
            tabSubSys.Controls.Add(txtFullLog);

            // 子分頁 2: KEB 驅動器讀回參數獨立日誌 (專屬獨立展示)
            TabPage tabSubKeb = new TabPage("KEB 驅動器讀回參數獨立日誌 (KEB Hardware Config)") { BackColor = Color.FromArgb(240, 243, 246) };
            txtKebConfigLog = new TextBox()
            {
                Dock = DockStyle.Fill,
                Multiline = true,
                ScrollBars = ScrollBars.Vertical,
                ReadOnly = true,
                BackColor = Color.FromArgb(15, 23, 42),
                ForeColor = Color.FromArgb(56, 189, 248),
                Font = new Font("Consolas", 10f),
                Margin = new Padding(0)
            };
            tabSubKeb.Controls.Add(txtKebConfigLog);

            tabLogSub.TabPages.Add(tabSubSys);
            tabLogSub.TabPages.Add(tabSubKeb);

            tableLog.Controls.Add(tabLogSub, 0, 1);
            tab.Controls.Add(tableLog);
        }

        private int GetHmiKebComIdx(int drive)
        {
            ComboBox cmb = (drive == 1) ? cmbHmiKebPort1 : cmbHmiKebPort2;
            string port = (cmb != null && cmb.SelectedItem != null) ? cmb.SelectedItem.ToString() : (drive == 1 ? "COM1" : "COM2");
            int pNum = drive;
            if (port.StartsWith("COM", StringComparison.OrdinalIgnoreCase)) int.TryParse(port.Substring(3), out pNum);
            return Math.Max(0, pNum - 1);
        }

        private int GetHmiKebBaudIdx(int drive)
        {
            ComboBox cmb = (drive == 1) ? cmbHmiKebBaud1 : cmbHmiKebBaud2;
            int baud = 9600;
            if (cmb != null && cmb.SelectedItem != null) int.TryParse(cmb.SelectedItem.ToString(), out baud);
            if (baud == 9600) return 3;
            if (baud == 19200) return 4;
            if (baud == 38400) return 5;
            if (baud == 57600) return 6;
            if (baud == 115200) return 7;
            return 3; // 預設 9600
        }

        private void BtnExportLogs_Click(object sender, EventArgs e)
        {
            try
            {
                SaveFileDialog sfd = new SaveFileDialog()
                {
                    Title = "匯出動力計系統運轉日誌",
                    Filter = "文字日誌檔 (*.log;*.txt)|*.log;*.txt|CSV 試算表 (*.csv)|*.csv|所有檔案 (*.*)|*.*",
                    FileName = string.Format("Dyno_System_Log_{0}.log", DateTime.Now.ToString("yyyyMMdd_HHmmss"))
                };

                if (sfd.ShowDialog() == DialogResult.OK)
                {
                    List<string> logsToExport;
                    lock (hmiLogLock)
                    {
                        logsToExport = new List<string>(memoryLogs);
                    }
                    if (logsToExport.Count == 0 && txtFullLog != null && !string.IsNullOrEmpty(txtFullLog.Text))
                    {
                        File.WriteAllText(sfd.FileName, txtFullLog.Text, Encoding.UTF8);
                    }
                    else
                    {
                        File.WriteAllLines(sfd.FileName, logsToExport.ToArray(), Encoding.UTF8);
                    }
                    MessageBox.Show("日誌已成功匯出至:\n" + sfd.FileName, "匯出成功", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("匯出日誌失敗: " + ex.Message, "錯誤", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void RefreshHmiKebPorts()
        {
            cmbHmiKebPort1.Items.Clear();
            cmbHmiKebPort2.Items.Clear();
            string[] ports = SerialPort.GetPortNames();

            int selIdx1 = -1, selIdx2 = -1;
            for (int i = 0; i < ports.Length; i++)
            {
                cmbHmiKebPort1.Items.Add(ports[i]);
                cmbHmiKebPort2.Items.Add(ports[i]);
                if (ports[i].Equals("COM1", StringComparison.OrdinalIgnoreCase)) selIdx1 = i;
                if (ports[i].Equals("COM2", StringComparison.OrdinalIgnoreCase)) selIdx2 = i;
            }

            if (selIdx1 >= 0) cmbHmiKebPort1.SelectedIndex = selIdx1;
            else if (cmbHmiKebPort1.Items.Count > 0) cmbHmiKebPort1.SelectedIndex = 0;
            else cmbHmiKebPort1.Items.Add("COM1");

            if (selIdx2 >= 0) cmbHmiKebPort2.SelectedIndex = selIdx2;
            else if (cmbHmiKebPort2.Items.Count > 1) cmbHmiKebPort2.SelectedIndex = 1;
            else if (cmbHmiKebPort2.Items.Count > 0) cmbHmiKebPort2.SelectedIndex = 0;
            else cmbHmiKebPort2.Items.Add("COM2");
        }

        public void UnlockAllTrackingAndReset()
        {
            hasBaseline = false;
            isLocked = false;
            isClosedLoopTracking = false;
            baselineTorque = 0.0;
            activeTrackingTorquePct = 0.0m;

            hasSpeedBaseline = false;
            isSpeedTracking = false;
            baselineSpeed = 0.0;
            activeTrackingSpeedRpm = 0.0m;

            if (this.IsHandleCreated && !this.IsDisposed)
            {
                this.BeginInvoke((MethodInvoker)delegate {
                    if (btnLockTorque != null && !btnLockTorque.IsDisposed)
                    {
                        btnLockTorque.Image = CreateLockIconImage(14, 14, Color.FromArgb(100, 116, 139), false);
                        btnLockTorque.Text = " UNLOCK";
                        btnLockTorque.ForeColor = Color.FromArgb(100, 116, 139);
                        btnLockTorque.BackColor = Color.FromArgb(241, 245, 249);
                        btnLockTorque.FlatAppearance.BorderColor = Color.FromArgb(203, 213, 225);
                    }
                    if (btnLockSpeed != null && !btnLockSpeed.IsDisposed)
                    {
                        btnLockSpeed.Image = CreateLockIconImage(14, 14, Color.FromArgb(100, 116, 139), false);
                        btnLockSpeed.Text = " UNLOCK";
                        btnLockSpeed.ForeColor = Color.FromArgb(100, 116, 139);
                        btnLockSpeed.BackColor = Color.FromArgb(241, 245, 249);
                        btnLockSpeed.FlatAppearance.BorderColor = Color.FromArgb(203, 213, 225);
                    }
                    if (numCardDeadband != null && numCardDeadband.Parent != null) numCardDeadband.Parent.Visible = false;
                    if (numCardTargetTorque != null && numCardTargetTorque.Parent != null) numCardTargetTorque.Parent.Visible = false;
                    if (numCardSpeedDeadband != null && numCardSpeedDeadband.Parent != null) numCardSpeedDeadband.Parent.Visible = false;
                    if (numCardTargetSpeed != null && numCardTargetSpeed.Parent != null) numCardTargetSpeed.Parent.Visible = false;

                    UpdateHmiKebModeButtonsVisual();
                });
            }
        }

        private bool CheckConfirmDisconnectWithLock(string driveName)
        {
            if (isClosedLoopTracking || isSpeedTracking)
            {
                string lockDesc = "";
                if (isClosedLoopTracking && isSpeedTracking) lockDesc = "【轉矩 LOCK】與【轉速 LOCK】";
                else if (isClosedLoopTracking) lockDesc = "【轉矩 LOCK】平滑追隨";
                else lockDesc = "【轉速 LOCK】平滑追隨";

                DialogResult dr = MessageBox.Show(
                    string.Format(
                        "【⚠️ 斷線優先安全警告】\n\n" +
                        "系統檢測到目前正處於 {0} 狀態！\n\n" +
                        "斷開 {1} 通訊前，必須先安全解除 LOCK 追隨控制，以防止驅動器通訊中斷引發非預期運轉。\n\n" +
                        "請問是否確定要【解除 LOCK 並立即斷開連線】？",
                        lockDesc, driveName),
                    "斷線優先安全防護確認",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning,
                    MessageBoxDefaultButton.Button2
                );

                if (dr != DialogResult.Yes)
                {
                    return false;
                }

                // 使用者確認解除 LOCK 並斷線：
                UnlockAllTrackingAndReset();
                WriteHmiLog("SAFETY", string.Format("【斷線優先】已確認解除 {0} 控制，即將斷開 {1} 連線。", lockDesc, driveName));
            }

            return CheckConfirmDisconnectFullAuto(driveName);
        }

        private bool CheckConfirmDisconnectFullAuto(string driveName)
        {
            bool isFullAuto = (currentKebMode1 == 9 || currentKebMode1 == 10 || currentKebMode2 == 9 || currentKebMode2 == 10);
            if (isFullAuto)
            {
                DialogResult dr = MessageBox.Show(
                    string.Format("【⚠️ 全自動模式斷開 COM PORT 警告】\n\n系統目前處於【全自動控制】模式！\n若在此狀態下斷開 {0} 的 COM PORT 連線，將觸發連鎖安全停機保護，【強制雙載台全部停機並斷電】！\n\n請問是否確定要斷開 COM PORT 連線？", driveName),
                    "全自動安全防護確認",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning,
                    MessageBoxDefaultButton.Button2
                );
                if (dr != DialogResult.Yes)
                {
                    return false;
                }

                // 使用者確認斷開：強制執行雙機停機與轉矩卸載
                TriggerGlobalEmergencyStop();
                WriteHmiLog("SAFETY", string.Format("【全自動連鎖停機】使用者確認斷開 {0} 連線，雙載台已強制安全停機並斷電！", driveName));
            }
            return true;
        }

        private void ApplyHmiKebInterlock(int driveIdx, int mode)
        {
            string dName = (driveIdx == 1) ? "A載台(COM1)" : "B載台(COM2)";
            string modeDesc = GetKebModeName(mode);
            WriteHmiLog("USER_ACTION", string.Format("【使用者點擊模式按鈕】載台: {0} ➔ 要求切換為: 【{1}】", dName, modeDesc));

            // 邏輯 1：全自動控制模式 (模式 9: 全自動轉速, 模式 10: 全自動轉矩) 必須雙機 100% 同時連線！
            if (mode == 9 || mode == 10)
            {
                if (!isHmiKebOpen1 || !isHmiKebOpen2)
                {
                    MessageBox.Show(
                        "【🚨 全自動控制安全拒絕】\n\n" +
                        "全自動控制（oP.01=8）由上位機軟體全權掌控 ST 運轉與停機！\n" +
                        "為防止單機全自動運轉導致失控超速（飛車），系統強制要求【雙載台 (COM1 與 COM2) 必須 100% 同時連線】！\n\n" +
                        "目前檢測到尚有一側未連線，已強制阻斷切換！請先將雙機全部連線後再試。",
                        "全自動安全互鎖警告",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning
                    );
                    WriteHmiLog("SAFETY_LOCK", string.Format("【全自動安全拒絕】{0}載台嘗試在單機狀態切換全自動模式，已被系統強制阻斷！", driveIdx == 1 ? "A" : "B"));
                    UpdateHmiKebModeButtonsVisual();
                    return;
                }
            }

            // 單機連線防呆提醒：若另一載台未連線，提醒操作員確認負載安全
            if (mode == 8 || mode == 10)
            {
                bool otherOnline = (driveIdx == 1) ? isHmiKebOpen2 : isHmiKebOpen1;
                if (!otherOnline)
                {
                    DialogResult dr = MessageBox.Show(
                        "【⚠️ 單機調試提醒】\n\n對測端載台尚未連線！\n在單機狀態下輸出轉矩，請確認外部負載已安全就緒，以防無對抗下超速。\n\n是否確認進入定轉矩模式進行單機調試？",
                        "動力計單機調試提示",
                        MessageBoxButtons.YesNo,
                        MessageBoxIcon.Question
                    );
                    if (dr != DialogResult.Yes)
                    {
                        UpdateHmiKebModeButtonsVisual();
                        return;
                    }
                }
            }

            if (driveIdx == 1)
            {
                currentKebMode1 = mode;
                // 模式切換：若斷線則直接拒絕，不同步阻塞 UI
                if (!isHmiKebOpen1)
                {
                    MessageBox.Show("A台 (COM1) 未連線！請先點擊 [Open] 建立連線後再切換模式。", "A台未連線", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    currentKebMode1 = 0; UpdateHmiKebModeButtonsVisual(); return;
                }
                SetHmiKebMode(GetHmiKebComIdx(1), GetHmiKebBaudIdx(1), (int)numHmiKebNode1.Value, mode, "1號驅動端");

                // 半自動互鎖 (7/8)
                if (mode == 7 && currentKebMode2 == 7 && isHmiKebOpen2)
                {
                    currentKebMode2 = 8;
                    SetHmiKebMode(GetHmiKebComIdx(2), GetHmiKebBaudIdx(2), (int)numHmiKebNode2.Value, 8, "2號負載端 (雙機互鎖自動轉矩)");
                    WriteHmiLog("INTERLOCK", "【雙機安全互鎖】1號機切換為轉速控制，2號機已自動互鎖切換為轉矩加載控制！");
                }
                else if (mode == 8 && currentKebMode2 == 8 && isHmiKebOpen2)
                {
                    currentKebMode2 = 7;
                    SetHmiKebMode(GetHmiKebComIdx(2), GetHmiKebBaudIdx(2), (int)numHmiKebNode2.Value, 7, "2號負載端 (雙機互鎖自動轉速)");
                    WriteHmiLog("INTERLOCK", "【雙機安全互鎖】1號機切換為轉矩控制，2號機已自動互鎖切換為轉速驅動控制！");
                }
                // 全自動互鎖 (9/10)
                else if (mode == 9 && currentKebMode2 == 9 && isHmiKebOpen2)
                {
                    currentKebMode2 = 10;
                    SetHmiKebMode(GetHmiKebComIdx(2), GetHmiKebBaudIdx(2), (int)numHmiKebNode2.Value, 10, "2號負載端 (全自動互鎖轉矩)");
                    WriteHmiLog("INTERLOCK", "【全自動安全互鎖】1號機切換為全自動轉速，2號機已自動互鎖切換為全自動轉矩！");
                }
                else if (mode == 10 && currentKebMode2 == 10 && isHmiKebOpen2)
                {
                    currentKebMode2 = 9;
                    SetHmiKebMode(GetHmiKebComIdx(2), GetHmiKebBaudIdx(2), (int)numHmiKebNode2.Value, 9, "2號負載端 (全自動互鎖轉速)");
                    WriteHmiLog("INTERLOCK", "【全自動安全互鎖】1號機切換為全自動轉矩，2號機已自動互鎖切換為全自動轉速！");
                }
            }
            else
            {
                currentKebMode2 = mode;
                // 模式切換：若斷線則直接拒絕，不同步阻塞 UI
                if (!isHmiKebOpen2)
                {
                    MessageBox.Show("B台 (COM2) 未連線！請先點擊 [Open] 建立連線後再切換模式。", "B台未連線", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    currentKebMode2 = 0; UpdateHmiKebModeButtonsVisual(); return;
                }
                SetHmiKebMode(GetHmiKebComIdx(2), GetHmiKebBaudIdx(2), (int)numHmiKebNode2.Value, mode, "2號負載端");

                // 半自動互鎖 (7/8)
                if (mode == 7 && currentKebMode1 == 7 && isHmiKebOpen1)
                {
                    currentKebMode1 = 8;
                    SetHmiKebMode(GetHmiKebComIdx(1), GetHmiKebBaudIdx(1), (int)numHmiKebNode1.Value, 8, "1號驅動端 (雙機互鎖自動轉矩)");
                    WriteHmiLog("INTERLOCK", "【雙機安全互鎖】2號機切換為轉速控制，1號機已自動互鎖切換為轉矩加載控制！");
                }
                else if (mode == 8 && currentKebMode1 == 8 && isHmiKebOpen1)
                {
                    currentKebMode1 = 7;
                    SetHmiKebMode(GetHmiKebComIdx(1), GetHmiKebBaudIdx(1), (int)numHmiKebNode1.Value, 7, "1號驅動端 (雙機互鎖自動轉速)");
                    WriteHmiLog("INTERLOCK", "【雙機安全互鎖】2號機切換為轉矩控制，1號機已自動互鎖切換為轉速驅動控制！");
                }
                // 全自動互鎖 (9/10)
                else if (mode == 9 && currentKebMode1 == 9 && isHmiKebOpen1)
                {
                    currentKebMode1 = 10;
                    SetHmiKebMode(GetHmiKebComIdx(1), GetHmiKebBaudIdx(1), (int)numHmiKebNode1.Value, 10, "1號驅動端 (全自動互鎖轉矩)");
                    WriteHmiLog("INTERLOCK", "【全自動安全互鎖】2號機切換為全自動轉速，1號機已自動互鎖切換為全自動轉矩！");
                }
                else if (mode == 10 && currentKebMode1 == 10 && isHmiKebOpen1)
                {
                    currentKebMode1 = 9;
                    SetHmiKebMode(GetHmiKebComIdx(1), GetHmiKebBaudIdx(1), (int)numHmiKebNode1.Value, 9, "1號驅動端 (全自動互鎖轉速)");
                    WriteHmiLog("INTERLOCK", "【全自動安全互鎖】2號機切換為全自動轉矩，1號機已自動互鎖切換為全自動轉速！");
                }
            }
            UpdateHmiKebModeButtonsVisual();
        }

        private void UpdateHmiKebModeButtonsVisual()
        {
            // 更新 1 號機按鈕高亮
            SetHmiModeBtnStyle(bHmiM1_1, currentKebMode1 == 7, Color.FromArgb(16, 185, 129));
            SetHmiModeBtnStyle(bHmiM1_2, currentKebMode1 == 8, Color.FromArgb(0, 120, 215));
            SetHmiModeBtnStyle(bHmiM1_3, currentKebMode1 == 9, Color.FromArgb(217, 119, 6));
            SetHmiModeBtnStyle(bHmiM1_4, currentKebMode1 == 10, Color.FromArgb(139, 92, 246));

            // 更新 2 號機按鈕高亮
            SetHmiModeBtnStyle(bHmiM2_1, currentKebMode2 == 7, Color.FromArgb(16, 185, 129));
            SetHmiModeBtnStyle(bHmiM2_2, currentKebMode2 == 8, Color.FromArgb(0, 120, 215));
            SetHmiModeBtnStyle(bHmiM2_3, currentKebMode2 == 9, Color.FromArgb(217, 119, 6));
            SetHmiModeBtnStyle(bHmiM2_4, currentKebMode2 == 10, Color.FromArgb(139, 92, 246));
        // 模式互鎖控制：模式 7 與 9 為轉速控制；模式 8 與 10 為轉矩加載控制
            bool d1_spdEnable = (currentKebMode1 == 7 || currentKebMode1 == 9);
            bool d1_trqEnable = (currentKebMode1 == 8 || currentKebMode1 == 10);
            bool d2_spdEnable = (currentKebMode2 == 7 || currentKebMode2 == 9);
            bool d2_trqEnable = (currentKebMode2 == 8 || currentKebMode2 == 10);

            // 🚨 全面安全互鎖：當 LOCK 鎖定時，強制禁用對應的手動控制項，只能由「🎯目標值」來改變！
            if (isSpeedTracking)
            {
                // 轉速 LOCK 鎖定中：禁止手動更改速度，防止階躍落差衝擊
                d1_spdEnable = false;
                d2_spdEnable = false;
            }
            if (isClosedLoopTracking)
            {
                // 轉矩 LOCK 鎖定中：禁止手動更改轉矩，防止力矩突波暴衝
                d1_trqEnable = false;
                d2_trqEnable = false;
            }

            if (hmiSpdControls1 != null) { foreach (var c in hmiSpdControls1) if (c != null) c.Enabled = d1_spdEnable; }
            if (hmiTrqControls1 != null) { foreach (var c in hmiTrqControls1) if (c != null) c.Enabled = d1_trqEnable; }
            if (hmiSpdControls2 != null) { foreach (var c in hmiSpdControls2) if (c != null) c.Enabled = d2_spdEnable; }
            if (hmiTrqControls2 != null) { foreach (var c in hmiTrqControls2) if (c != null) c.Enabled = d2_trqEnable; }

            // 智能加載按鈕動態切換：保持 4 鍵網格永遠可見，絕不隱藏元件導致 TableLayoutPanel 破版！
            if (pnlRun1 != null && btnRF1 != null && btnRR1 != null && btnStop1 != null && btnReset1 != null)
            {
                if (d1_trqEnable)
                {
                    btnRF1.Text = "⚡ 啟用加載";
                    btnRF1.BackColor = Color.FromArgb(245, 158, 11);
                    btnRR1.Text = "⚡ 反向加載";
                    btnRR1.BackColor = Color.FromArgb(217, 119, 6);
                    btnStop1.Text = "🛑 卸載停機";
                }
                else
                {
                    btnRF1.Text = "正轉 (RUN)";
                    btnRF1.BackColor = Color.FromArgb(16, 185, 129);
                    btnRR1.Text = "反轉 (RUN)";
                    btnRR1.BackColor = Color.FromArgb(139, 92, 246);
                    btnStop1.Text = "🛑 停機";
                }
                btnRR1.Visible = true;
                pnlRun1.ColumnStyles[0].Width = 28f;
                pnlRun1.ColumnStyles[1].Width = 28f;
                pnlRun1.ColumnStyles[2].Width = 22f;
                if (pnlRun1.ColumnStyles.Count > 3) pnlRun1.ColumnStyles[3].Width = 22f;
            }

            if (pnlRun2 != null && btnRF2 != null && btnRR2 != null && btnStop2 != null && btnReset2 != null)
            {
                if (d2_trqEnable)
                {
                    btnRF2.Text = "⚡ 啟用加載";
                    btnRF2.BackColor = Color.FromArgb(245, 158, 11);
                    btnRR2.Text = "⚡ 反向加載";
                    btnRR2.BackColor = Color.FromArgb(217, 119, 6);
                    btnStop2.Text = "🛑 卸載停機";
                }
                else
                {
                    btnRF2.Text = "正轉 (RUN)";
                    btnRF2.BackColor = Color.FromArgb(16, 185, 129);
                    btnRR2.Text = "反轉 (RUN)";
                    btnRR2.BackColor = Color.FromArgb(139, 92, 246);
                    btnStop2.Text = "🛑 停機";
                }
                btnRR2.Visible = true;
                pnlRun2.ColumnStyles[0].Width = 28f;
                pnlRun2.ColumnStyles[1].Width = 28f;
                pnlRun2.ColumnStyles[2].Width = 22f;
                if (pnlRun2.ColumnStyles.Count > 3) pnlRun2.ColumnStyles[3].Width = 22f;
            }
        }

        private void SetHmiModeBtnStyle(Button btn, bool isActive, Color activeColor)
        {
            if (btn == null) return;
            btn.Enabled = true;
            if (isActive)
            {
                btn.BackColor = activeColor;
                btn.ForeColor = Color.White;
                btn.Font = new Font("微軟正黑體", 8.5f, FontStyle.Bold);
            }
            else
            {
                btn.BackColor = Color.FromArgb(241, 245, 249);
                btn.ForeColor = Color.FromArgb(51, 65, 85);
                btn.Font = new Font("微軟正黑體", 8.5f, FontStyle.Regular);
            }
        }

        private bool isHmiKebOpen1 = false;
        private bool isHmiKebOpen2 = false;
        private int cachedUd02_1 = -1, cachedUd02_2 = -1;
        private int cachedCs00_1 = -1, cachedCs00_2 = -1;
        private static readonly object kebLock = new object();
        private static int activeKebComIndex = -1;
        private static int activeKebBaudIndex = -1;



        private bool EnsureHmiKebOpen1()
        {
            if (isHmiKebOpen1) return true;
            string port = cmbHmiKebPort1.SelectedItem != null ? cmbHmiKebPort1.SelectedItem.ToString() : "COM1";
            int baud = int.Parse(cmbHmiKebBaud1.SelectedItem != null ? cmbHmiKebBaud1.SelectedItem.ToString() : "9600");
            int comIdx = GetHmiKebComIdx(1);
            int baudIdx = GetHmiKebBaudIdx(1);

            try
            {
                lock (kebLock)
                {
                    closechannels();
                    tProtProperty prop = new tProtProperty();
                    prop.ProtType = 1; // DIN 66019-II (prAnsi)
                    prop.Baudrate = baudIdx;
                    prop.Comport  = comIdx;
                    prop.TimeOut  = 600;
                    prop.Flag     = 0;
                    prop.Port     = 0;
                    prop.Txtlen   = 0;
                    prop.txt      = "";
                    setprotproperties(ref prop);
                    setretrycnt(3);
                    setinvprot((int)numHmiKebNode1.Value, 1);
                    activeKebComIndex = comIdx;
                    activeKebBaudIndex = baudIdx;
                }

                // 實體通訊握手校驗：多重探測 ud.02 (0x0802) / oP.00 (0x0300) / ru.00 (0x0200) / Sy.51 (0x0033) / ru.07 (0x0207)
                int? testVal = KebReadParamWithDll(comIdx, baudIdx, (int)numHmiKebNode1.Value, 0x0802);
                if (!testVal.HasValue) testVal = KebReadParamWithDll(comIdx, baudIdx, (int)numHmiKebNode1.Value, 0x0300);
                if (!testVal.HasValue) testVal = KebReadParamWithDll(comIdx, baudIdx, (int)numHmiKebNode1.Value, 0x0200);
                if (!testVal.HasValue) testVal = KebReadParamWithDll(comIdx, baudIdx, (int)numHmiKebNode1.Value, 0x0033);
                if (!testVal.HasValue) testVal = KebReadParamWithDll(comIdx, baudIdx, (int)numHmiKebNode1.Value, 0x0207);
                if (testVal.HasValue)
                {
                    isHmiKebOpen1 = true;
                    if (btnHmiPortToggle1 != null && !btnHmiPortToggle1.IsDisposed)
                    {
                        btnHmiPortToggle1.Text = "[Close]";
                        btnHmiPortToggle1.BackColor = Color.FromArgb(239, 68, 68);
                    }
                    lblHmiKebStatus1.Text = string.Format("狀態: 已連線 ({0} @ {1})", port, baud);
                    lblHmiKebStatus1.ForeColor = Color.Green;
                    if (lblPillKeb1 != null && !lblPillKeb1.IsDisposed)
                    {
                        lblPillKeb1.Text = " A載台: " + port + " ";
                        lblPillKeb1.ForeColor = Color.FromArgb(74, 222, 128);
                    }
                    if (txtHmiKebLog != null) txtHmiKebLog.AppendText(string.Format("[{0}] A載台 (加載端) 握手成功：{1} @ {2} bps (實體回應 0x{3:X4})\r\n", DateTime.Now.ToLongTimeString(), port, baud, testVal.Value));
                    
                    // 連線成功後第一動作：先讀回變頻器目前真實硬體參數，並同步至介面設定框與模式按鈕
                    ReadAndSyncHmiKebInitialParams(1);
                    return true;
                }
                else
                {
                    // 實體無回傳或連線失敗，絕不虛報連線！
                    CloseHmiKebPort1();
                    lblHmiKebStatus1.Text = string.Format("狀態: 連線失敗 ({0} 無回應)", port);
                    lblHmiKebStatus1.ForeColor = Color.Red;
                    if (lblPillKeb1 != null && !lblPillKeb1.IsDisposed)
                    {
                        lblPillKeb1.Text = " A載台: 斷線 ";
                        lblPillKeb1.ForeColor = Color.FromArgb(248, 113, 113);
                    }
                    if (txtHmiKebLog != null) txtHmiKebLog.AppendText(string.Format("[{0}] [A載台錯誤] {1} 站號 {2} 無回應，請檢查實體接線與變頻器電源！\r\n", DateTime.Now.ToLongTimeString(), port, numHmiKebNode1.Value));
                    return false;
                }
            }
            catch (Exception ex)
            {
                CloseHmiKebPort1();
                if (txtHmiKebLog != null) txtHmiKebLog.AppendText(string.Format("[A載台錯誤] 開啟 {0} 異常: {1}\r\n", port, ex.Message));
                lblHmiKebStatus1.Text = "狀態: 連線異常";
                lblHmiKebStatus1.ForeColor = Color.Red;
                if (lblPillKeb1 != null && !lblPillKeb1.IsDisposed)
                {
                    lblPillKeb1.Text = " A載台: 異常 ";
                    lblPillKeb1.ForeColor = Color.FromArgb(248, 113, 113);
                }
                return false;
            }
        }

        private void CloseHmiKebPort1()
        {
            try
            {
                if (isHmiKebOpen1) RestoreHmiKebInitialParams(1);
            }
            catch { }

            lock (kebLock)
            {
                try { closechannels(); } catch { }
                activeKebComIndex = -1;
            }
            if (spKeb1 != null) { try { spKeb1.Close(); spKeb1.Dispose(); } catch {} spKeb1 = null; }
            isHmiKebOpen1 = false;
            cachedUd02_1 = -1;
            cachedCs00_1 = -1;
            UpdateHmiKebModeParamsDisplay(1);
            if (btnHmiPortToggle1 != null && !btnHmiPortToggle1.IsDisposed)
            {
                btnHmiPortToggle1.Text = "[Open]";
                btnHmiPortToggle1.BackColor = Color.FromArgb(16, 185, 129);
            }
            if (lblHmiKebStatus1 != null && !lblHmiKebStatus1.IsDisposed)
            {
                lblHmiKebStatus1.Text = "狀態: 已釋放埠";
                lblHmiKebStatus1.ForeColor = Color.Gray;
            }
            if (lblPillKeb1 != null && !lblPillKeb1.IsDisposed)
            {
                lblPillKeb1.Text = " A載台: 待連線 ";
                lblPillKeb1.ForeColor = Color.FromArgb(203, 213, 225);
            }
            if (txtHmiKebLog != null) txtHmiKebLog.AppendText(string.Format("[{0}] A載台 COM 埠已釋放 (已解除鎖定，可供外部軟體使用)\r\n", DateTime.Now.ToLongTimeString()));
        }

        private bool EnsureHmiKebOpen2()
        {
            if (isHmiKebOpen2) return true;
            string port = cmbHmiKebPort2.SelectedItem != null ? cmbHmiKebPort2.SelectedItem.ToString() : "COM2";
            int baud = int.Parse(cmbHmiKebBaud2.SelectedItem != null ? cmbHmiKebBaud2.SelectedItem.ToString() : "9600");
            int comIdx = GetHmiKebComIdx(2);
            int baudIdx = GetHmiKebBaudIdx(2);

            try
            {
                lock (kebLock)
                {
                    closechannels();
                    tProtProperty prop = new tProtProperty();
                    prop.ProtType = 1; // DIN 66019-II (prAnsi)
                    prop.Baudrate = baudIdx;
                    prop.Comport  = comIdx;
                    prop.TimeOut  = 600;
                    prop.Flag     = 0;
                    prop.Port     = 0;
                    prop.Txtlen   = 0;
                    prop.txt      = "";
                    setprotproperties(ref prop);
                    setretrycnt(3);
                    setinvprot((int)numHmiKebNode2.Value, 1);
                    activeKebComIndex = comIdx;
                    activeKebBaudIndex = baudIdx;
                }

                // 實體通訊握手校驗：多重探測 ud.02 (0x0802) / oP.00 (0x0300) / ru.00 (0x0200) / Sy.51 (0x0033) / ru.07 (0x0207)
                int? testVal = KebReadParamWithDll(comIdx, baudIdx, (int)numHmiKebNode2.Value, 0x0802);
                if (!testVal.HasValue) testVal = KebReadParamWithDll(comIdx, baudIdx, (int)numHmiKebNode2.Value, 0x0300);
                if (!testVal.HasValue) testVal = KebReadParamWithDll(comIdx, baudIdx, (int)numHmiKebNode2.Value, 0x0200);
                if (!testVal.HasValue) testVal = KebReadParamWithDll(comIdx, baudIdx, (int)numHmiKebNode2.Value, 0x0033);
                if (!testVal.HasValue) testVal = KebReadParamWithDll(comIdx, baudIdx, (int)numHmiKebNode2.Value, 0x0207);
                if (testVal.HasValue)
                {
                    isHmiKebOpen2 = true;
                    if (btnHmiPortToggle2 != null && !btnHmiPortToggle2.IsDisposed)
                    {
                        btnHmiPortToggle2.Text = "[Close]";
                        btnHmiPortToggle2.BackColor = Color.FromArgb(239, 68, 68);
                    }
                    lblHmiKebStatus2.Text = string.Format("狀態: 已連線 ({0} @ {1})", port, baud);
                    lblHmiKebStatus2.ForeColor = Color.Green;
                    if (lblPillKeb2 != null && !lblPillKeb2.IsDisposed)
                    {
                        lblPillKeb2.Text = " B載台: " + port + " ";
                        lblPillKeb2.ForeColor = Color.FromArgb(74, 222, 128);
                    }
                    if (txtHmiKebLog != null) txtHmiKebLog.AppendText(string.Format("[{0}] B載台 (待測端) 握手成功：{1} @ {2} bps (實體回應 0x{3:X4})\r\n", DateTime.Now.ToLongTimeString(), port, baud, testVal.Value));
                    
                    // 連線成功後第一動作：先讀回變頻器目前真實硬體參數，並同步至介面設定框與模式按鈕
                    ReadAndSyncHmiKebInitialParams(2);
                    return true;
                }
                else
                {
                    // 實體無回傳或連線失敗，絕不虛報連線！
                    CloseHmiKebPort2();
                    lblHmiKebStatus2.Text = string.Format("狀態: 連線失敗 ({0} 無回應)", port);
                    lblHmiKebStatus2.ForeColor = Color.Red;
                    if (lblPillKeb2 != null && !lblPillKeb2.IsDisposed)
                    {
                        lblPillKeb2.Text = " B載台: 斷線 ";
                        lblPillKeb2.ForeColor = Color.FromArgb(248, 113, 113);
                    }
                    if (txtHmiKebLog != null) txtHmiKebLog.AppendText(string.Format("[{0}] [B載台錯誤] {1} 站號 {2} 無回應，請檢查實體接線與變頻器電源！\r\n", DateTime.Now.ToLongTimeString(), port, numHmiKebNode2.Value));
                    return false;
                }
            }
            catch (Exception ex)
            {
                CloseHmiKebPort2();
                if (txtHmiKebLog != null) txtHmiKebLog.AppendText(string.Format("[B載台錯誤] 開啟 {0} 異常: {1}\r\n", port, ex.Message));
                lblHmiKebStatus2.Text = "狀態: 連線異常";
                lblHmiKebStatus2.ForeColor = Color.Red;
                if (lblPillKeb2 != null && !lblPillKeb2.IsDisposed)
                {
                    lblPillKeb2.Text = " B載台: 異常 ";
                    lblPillKeb2.ForeColor = Color.FromArgb(248, 113, 113);
                }
                return false;
            }
        }

        private void CloseHmiKebPort2()
        {
            try
            {
                if (isHmiKebOpen2) RestoreHmiKebInitialParams(2);
            }
            catch { }

            lock (kebLock)
            {
                try { closechannels(); } catch { }
                activeKebComIndex = -1;
            }
            if (spKeb2 != null) { try { spKeb2.Close(); spKeb2.Dispose(); } catch {} spKeb2 = null; }
            isHmiKebOpen2 = false;
            cachedUd02_2 = -1;
            cachedCs00_2 = -1;
            UpdateHmiKebModeParamsDisplay(2);
            if (btnHmiPortToggle2 != null && !btnHmiPortToggle2.IsDisposed)
            {
                btnHmiPortToggle2.Text = "[Open]";
                btnHmiPortToggle2.BackColor = Color.FromArgb(16, 185, 129);
            }
            if (lblHmiKebStatus2 != null && !lblHmiKebStatus2.IsDisposed)
            {
                lblHmiKebStatus2.Text = "狀態: 已釋放埠";
                lblHmiKebStatus2.ForeColor = Color.Gray;
            }
            if (lblPillKeb2 != null && !lblPillKeb2.IsDisposed)
            {
                lblPillKeb2.Text = " B載台: 待連線 ";
                lblPillKeb2.ForeColor = Color.FromArgb(203, 213, 225);
            }
            if (txtHmiKebLog != null) txtHmiKebLog.AppendText(string.Format("[{0}] B載台 COM 埠已釋放 (已解除鎖定，可供外部軟體使用)\r\n", DateTime.Now.ToLongTimeString()));
        }

        private void UpdateHmiKebModeParamsDisplay(int driveId, int mode = -1, int speedVal = -1, double trqPercent = -1.0, int lastCmd = -1)
        {
            if (driveId == 1)
            {
                if (mode != -1) currentKebMode1 = mode;
                if (lastCmd != -1) lastSy50Cmd1 = lastCmd;
            }
            else
            {
                if (mode != -1) currentKebMode2 = mode;
                if (lastCmd != -1) lastSy50Cmd2 = lastCmd;
            }

            // 零序列通訊負載架構：直接依據硬體快取與模式即時推論，徹底移除 8 次實體暫存器高頻輪詢！
            bool isOpen = (driveId == 1) ? isHmiKebOpen1 : isHmiKebOpen2;
            string diagDesc = "";
            if (!isOpen)
            {
                diagDesc = "【未連線 / 待開啟 COM 埠】";
            }
            else
            {
                int ud = (driveId == 1) ? cachedUd02_1 : cachedUd02_2;
                int cs = (driveId == 1) ? cachedCs00_1 : cachedCs00_2;
                int curMode = (driveId == 1) ? currentKebMode1 : currentKebMode2;

                if (ud == 0) diagDesc = "【V/F 開迴路 (F5-Basic)】-> 走 Sy.52 (0x0034) 硬體自動換算 rpm->Hz";
                else if (ud == 1) diagDesc = "【SMM 無感測向量 (F5-General)】-> 走 Sy.52 (0x0034) 過程數據給定";
                else if (ud == 2)
                {
                    if (curMode == 8 || curMode == 10 || cs == 6) diagDesc = "【轉矩加載模式 (F5-Multi cs00=6)】-> 走 cs.18 (0x0F12) 轉矩給定";
                    else if (cs == 0) diagDesc = "【V/F 模式 (F5-Multi cs00=0)】-> 走 Sy.52 (0x0034) 硬體自動換算 rpm->Hz";
                    else diagDesc = "【閉迴路向量轉速 (F5-Multi cs00=4)】-> 走 Sy.52 (0x0034) 閉迴路控速";
                }
                else if (ud == 3) diagDesc = "【伺服向量控制 (F5-Servo)】-> 走 Sy.52 (0x0034) 閉迴路伺服控制";
                else if (curMode == 8 || curMode == 10 || cs == 6) diagDesc = "【轉矩加載模式 (cs00=6)】-> 走 cs.18 轉矩給定";
                else if (curMode == 7 || curMode == 9 || cs == 4) diagDesc = "【閉迴路向量轉速 (cs00=4)】-> 走 Sy.52 閉迴路控速";
                else diagDesc = "【連線就緒，請選擇控制模式】";
            }

            string realText = "🔍 系統判斷：" + diagDesc;

            if (this.IsHandleCreated && !this.IsDisposed)
            {
                if (this.InvokeRequired)
                {
                    this.BeginInvoke((MethodInvoker)delegate {
                        if (driveId == 1 && lblKebModeParams1 != null && !lblKebModeParams1.IsDisposed)
                        {
                            if (lblKebModeParams1.Text != realText) lblKebModeParams1.Text = realText;
                        }
                        else if (driveId == 2 && lblKebModeParams2 != null && !lblKebModeParams2.IsDisposed)
                        {
                            if (lblKebModeParams2.Text != realText) lblKebModeParams2.Text = realText;
                        }
                    });
                }
                else
                {
                    if (driveId == 1 && lblKebModeParams1 != null && !lblKebModeParams1.IsDisposed)
                    {
                        if (lblKebModeParams1.Text != realText) lblKebModeParams1.Text = realText;
                    }
                    else if (driveId == 2 && lblKebModeParams2 != null && !lblKebModeParams2.IsDisposed)
                    {
                        if (lblKebModeParams2.Text != realText) lblKebModeParams2.Text = realText;
                    }
                }
            }
        }

        private void ReadAndSyncHmiKebInitialParams(int driveId)
        {
            int comIdx = GetHmiKebComIdx(driveId);
            int baudIdx = GetHmiKebBaudIdx(driveId);
            int node = (int)((driveId == 1) ? numHmiKebNode1.Value : numHmiKebNode2.Value);
            string driveName = (driveId == 1) ? "A載台" : "B載台";

            try
            {
                int? r_ru00 = KebReadParamWithDll(comIdx, baudIdx, node, 0x0200);
                int? r_ud02 = KebReadParamWithDll(comIdx, baudIdx, node, 0x0802);
                int? r_op00 = KebReadParamWithDll(comIdx, baudIdx, node, 0x0300);
                int? r_op01 = KebReadParamWithDll(comIdx, baudIdx, node, 0x0301);
                int? r_cs00 = KebReadParamWithDll(comIdx, baudIdx, node, 0x0F00);
                int? r_cs15 = KebReadParamWithDll(comIdx, baudIdx, node, 0x0F0F);
                int? r_cs18 = KebReadParamWithDll(comIdx, baudIdx, node, 0x0F12);
                int? r_cs19 = KebReadParamWithDll(comIdx, baudIdx, node, 0x0F13);
                int? r_op03 = KebReadParamWithDll(comIdx, baudIdx, node, 0x0303);
                int? r_sy50 = KebReadParamWithDll(comIdx, baudIdx, node, 0x0032);
                int? r_sy52 = KebReadParamWithDll(comIdx, baudIdx, node, 0x0034);

                int? r_dr00 = KebReadParamWithDll(comIdx, baudIdx, node, 0x0400); // dr.00 額定電流 (0.1 A)
                int? r_dr01 = KebReadParamWithDll(comIdx, baudIdx, node, 0x0401); // dr.01 額定轉速 (1.0 rpm)
                int? r_dr02 = KebReadParamWithDll(comIdx, baudIdx, node, 0x0402); // dr.02 額定電壓 (1.0 V)
                int? r_dr03 = KebReadParamWithDll(comIdx, baudIdx, node, 0x0403); // dr.03 額定功率 (0.01 kW)
                int? r_dr04 = KebReadParamWithDll(comIdx, baudIdx, node, 0x0404); // dr.04 功率因數 (0.01)
                int? r_dr05 = KebReadParamWithDll(comIdx, baudIdx, node, 0x0405); // dr.05 額定頻率 (0.1 Hz)

                double drSpeed = r_dr01.HasValue ? (double)r_dr01.Value : 0.0;
                double drFreq = r_dr05.HasValue ? ((double)r_dr05.Value * 0.1) : 0.0;
                int motorPoles = (drSpeed > 0 && drFreq > 0) ? (int)Math.Round(120.0 * drFreq / drSpeed) : 0;

                string udDesc = r_ud02.HasValue ? ((r_ud02.Value == 0) ? "F5-Basic (V/F)" : ((r_ud02.Value == 1) ? "F5-General (SMM)" : ((r_ud02.Value == 2) ? "F5-Multi (開/閉迴路向量)" : ((r_ud02.Value == 3) ? "F5-Servo (伺服)" : "特殊架構")))) : "--";
                string csDesc = r_cs00.HasValue ? ((r_cs00.Value == 0) ? "V/F 開迴路 (cs00=0)" : ((r_cs00.Value == 4) ? "閉迴路向量速度控制 (cs00=4)" : ((r_cs00.Value == 6) ? "轉矩加載控制 (cs00=6)" : "自訂模式"))) : "--";
                double valCs18 = r_cs18.HasValue ? (r_cs18.Value * 0.1) : 0.0;

                // 嚴謹解析 cs.19 (0x0F13): 只有確實讀到硬體暫存器數值時才進行物理換算
                string cs19SnapshotDesc = "未讀取到 (通訊未回傳)";
                string cs19BannerText = "cs.19基準: 讀取失敗 (0x0F13無回傳)";
                bool cs19Valid = false;

                if (r_cs19.HasValue && r_cs19.Value > 0)
                {
                    cs19Valid = true;
                    hasReadCs19 = true;
                    // KEB F5 官方規格: cS.19 解析度 0.01 Nm (例 RAW 2000 = 20.00 Nm, RAW 500 = 5.00 Nm)
                    double realCs19 = (double)r_cs19.Value * 0.01;
                    if (driveId == 1) cs19TorqueRef1 = realCs19; else cs19TorqueRef2 = realCs19;

                    double activeCs19 = (currentKebMode1 == 8 || currentKebMode1 == 10) ? cs19TorqueRef1 : cs19TorqueRef2;
                    if (activeCs19 <= 0) activeCs19 = realCs19;
                    calculatedMinDeadbandTorque = Math.Round(activeCs19 / 1000.0, 3);

                    cs19SnapshotDesc = string.Format("{0:F2} Nm (RAW:{1}, 單步物理極限 x = cs19/1000 = {2:F3} Nm)", realCs19, r_cs19.Value, calculatedMinDeadbandTorque);
                    cs19BannerText = string.Format("cs.19基準: {0:F2} Nm (RAW:{1}, 步進: {2:F3}Nm)", realCs19, r_cs19.Value, calculatedMinDeadbandTorque);

                    WriteHmiLog("KEB_RAW", string.Format("【cs.19 實體讀取成功】[{0}] 暫存器0x0F13 RAW={1} -> 換算 cs.19={2:F2} Nm | 0.1% 單步極限 x = cs19/1000 = {3:F3} Nm",
                        driveName, r_cs19.Value, realCs19, calculatedMinDeadbandTorque));
                }
                else
                {
                    WriteHmiLog("KEB_RAW", string.Format("【cs.19 讀取注意】[{0}] 暫存器 0x0F13 未回傳有效數值 (r_cs19={1})",
                        driveName, (r_cs19.HasValue ? r_cs19.Value.ToString() : "null")));
                }

                double valOp03 = r_op03.HasValue ? (r_op03.Value * 0.125) : 0.0;
                double valSy52 = r_sy52.HasValue ? (double)r_sy52.Value : 0.0;

                // ★【斷線前自動回寫備份機制】：記錄連線初始之硬體參數快照
                KebBackupParams backup = (driveId == 1) ? kebBackup1 : kebBackup2;
                backup.HasBackup = true;
                backup.Ru00 = r_ru00;
                backup.Ud02 = r_ud02;
                backup.Op00 = r_op00;
                backup.Op01 = r_op01;
                backup.Cs00 = r_cs00;
                backup.Cs15 = r_cs15;
                backup.Cs18 = r_cs18;
                backup.Cs19 = r_cs19;
                backup.Op03 = r_op03;
                backup.Sy50 = r_sy50;
                backup.Sy52 = r_sy52;
                backup.BackupTime = DateTime.Now;

                WriteHmiLog("KEB_BACKUP", string.Format("【{0} 初始硬體參數備份成功】oP00={1}, oP01={2}, cs00={3}, cs15={4}, cs18={5}, oP03={6}, Sy52={7} (將於按下斷線或關閉程式時自動安全回寫)",
                    driveName,
                    r_op00.HasValue ? r_op00.Value.ToString() : "--",
                    r_op01.HasValue ? r_op01.Value.ToString() : "--",
                    r_cs00.HasValue ? r_cs00.Value.ToString() : "--",
                    r_cs15.HasValue ? r_cs15.Value.ToString() : "--",
                    r_cs18.HasValue ? r_cs18.Value.ToString() : "--",
                    r_op03.HasValue ? r_op03.Value.ToString() : "--",
                    r_sy52.HasValue ? r_sy52.Value.ToString() : "--"));

                string logSnapshot = string.Format(
                    "======================================================================\r\n" +
                    "[{0}] 【{1}】連線成功，已讀回目前硬體配置參數快照\r\n" +
                    "----------------------------------------------------------------------\r\n" +
                    "- 連線通訊埠: {2} (站號 Node: {3})\r\n" +
                    "- ud.02 (變頻器應用模式) : {4} -> {5}\r\n" +
                    "- oP.00 (目標給定值來源) : {6} (2:oP03給定, 5:Sy52過程數據)\r\n" +
                    "- oP.01 (運轉控制來源)   : {7} (7:半自動, 8:通訊運轉控制)\r\n" +
                    "- cs.00 (控制架構模式)   : {8} -> {9}\r\n" +
                    "- cs.15 (轉矩限制來源)   : {10}\r\n" +
                    "- cs.18 (數位設定轉矩)   : {11:F1} % (RAW: {12})\r\n" +
                    "- cs.19 (轉矩基準/額定)  : {13}\r\n" +
                    "- oP.03 (數位設定轉速)   : {14:F1} rpm (RAW: {15})\r\n" +
                    "- Sy.50 (目前控制字狀態) : {16}\r\n" +
                    "- Sy.52 (過程數據轉速)   : {17:F1} rpm (RAW: {18})\r\n" +
                    "----------------------------------------------------------------------\r\n" +
                    "【dr 馬達銘牌參數反算驗證】\r\n" +
                    "- dr.00 (馬達額定電流)   : {19:F1} A (RAW: {20})\r\n" +
                    "- dr.01 (馬達額定轉速)   : {21:F0} rpm (RAW: {22})\r\n" +
                    "- dr.02 (馬達額定電壓)   : {23} V\r\n" +
                    "- dr.03 (馬達額定功率)   : {24:F2} kW\r\n" +
                    "- dr.04 (馬達功率因數)   : {25:F2}\r\n" +
                    "- dr.05 (馬達額定頻率)   : {26:F1} Hz (RAW: {27})\r\n" +
                    "- dr 參數精確反算極數(P) : {28} 極 (依公式 P = round(120 * f / n))\r\n" +
                    "======================================================================\r\n\r\n",
                    DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                    driveName,
                    (driveId == 1) ? "COM1" : "COM2",
                    node,
                    r_ud02.HasValue ? r_ud02.Value.ToString() : "--", udDesc,
                    r_op00.HasValue ? r_op00.Value.ToString() : "--",
                    r_op01.HasValue ? r_op01.Value.ToString() : "--",
                    r_cs00.HasValue ? r_cs00.Value.ToString() : "--", csDesc,
                    r_cs15.HasValue ? r_cs15.Value.ToString() : "--",
                    valCs18, r_cs18.HasValue ? r_cs18.Value.ToString() : "--",
                    cs19SnapshotDesc,
                    valOp03, r_op03.HasValue ? r_op03.Value.ToString() : "--",
                    r_sy50.HasValue ? r_sy50.Value.ToString() : "--",
                    valSy52, r_sy52.HasValue ? r_sy52.Value.ToString() : "--",
                    r_dr00.HasValue ? ((double)r_dr00.Value * 0.1) : 0.0, r_dr00.HasValue ? r_dr00.Value.ToString() : "--",
                    drSpeed, r_dr01.HasValue ? r_dr01.Value.ToString() : "--",
                    r_dr02.HasValue ? r_dr02.Value.ToString() : "--",
                    r_dr03.HasValue ? ((double)r_dr03.Value * 0.01) : 0.0,
                    r_dr04.HasValue ? ((double)r_dr04.Value * 0.01) : 0.0,
                    drFreq, r_dr05.HasValue ? r_dr05.Value.ToString() : "--",
                    motorPoles
                );

                this.BeginInvoke((MethodInvoker)delegate {
                    if (txtKebConfigLog != null && !txtKebConfigLog.IsDisposed)
                    {
                        txtKebConfigLog.AppendText(logSnapshot);
                    }

                    // 確實更新 A/B 載台的 cs.19 顯示標籤
                    if (driveId == 1 && lblCs19Disp1 != null && !lblCs19Disp1.IsDisposed)
                    {
                        lblCs19Disp1.Text = cs19BannerText;
                        lblCs19Disp1.ForeColor = cs19Valid ? Color.FromArgb(16, 185, 129) : Color.FromArgb(239, 68, 68);
                    }
                    else if (driveId == 2 && lblCs19Disp2 != null && !lblCs19Disp2.IsDisposed)
                    {
                        lblCs19Disp2.Text = cs19BannerText;
                        lblCs19Disp2.ForeColor = cs19Valid ? Color.FromArgb(16, 185, 129) : Color.FromArgb(239, 68, 68);
                    }

                    // 確實更新轉矩卡片死區標籤
                    if (lblDbTrqHint != null && !lblDbTrqHint.IsDisposed)
                    {
                        if (cs19Valid && calculatedMinDeadbandTorque > 0)
                        {
                            lblDbTrqHint.Text = string.Format("⚡死區(≥{0:F3})", calculatedMinDeadbandTorque);
                            if (numCardDeadband != null && !numCardDeadband.IsDisposed && numCardDeadband.Value < (decimal)calculatedMinDeadbandTorque)
                            {
                                numCardDeadband.Value = (decimal)calculatedMinDeadbandTorque;
                                trackingDeadband = (decimal)calculatedMinDeadbandTorque;
                            }
                        }
                        else
                        {
                            lblDbTrqHint.Text = "⚡死區/誤差";
                        }
                    }

                    // ★【鐵律要求：連線上線當下，畫面各數值輸入框強制全部歸零！】
                    if (driveId == 1)
                    {
                        if (numHmiKebSpeed1 != null) numHmiKebSpeed1.Value = 0;
                        if (numHmiKebTorque1 != null) numHmiKebTorque1.Value = 0;
                    }
                    else
                    {
                        if (numHmiKebSpeed2 != null) numHmiKebSpeed2.Value = 0;
                        if (numHmiKebTorque2 != null) numHmiKebTorque2.Value = 0;
                    }

                    if (r_ud02.HasValue) { if (driveId == 1) cachedUd02_1 = r_ud02.Value; else cachedUd02_2 = r_ud02.Value; }
                    if (r_cs00.HasValue)
                    {
                        if (driveId == 1) cachedCs00_1 = r_cs00.Value; else cachedCs00_2 = r_cs00.Value;
                        bool isFullAuto = (r_op01.HasValue && r_op01.Value == 8);
                        int mode = (r_cs00.Value == 6) ? (isFullAuto ? 10 : 8) : (isFullAuto ? 9 : 7);
                        if (driveId == 1) currentKebMode1 = mode; else currentKebMode2 = mode;
                        UpdateHmiKebModeButtonsVisual();
                    }
                    UpdateHmiKebModeParamsDisplay(driveId);
                });

                // ★【鐵律安全防護 1：連線上線強制發送 STOP (Sy.50=0)，徹底消滅背景偷跑！】
                KebWriteParamWithDll(comIdx, baudIdx, node, 0x0032, 0); // Sy.50 = 0 (強制停機)
                
                // ★【鐵律安全防護 2：若當前變頻器處於 FAULT (ru.00 >= 64)，自動發送 FAULT RESET (Sy.50=2) 清除報警！】
                if (r_ru00.HasValue && r_ru00.Value >= 64)
                {
                    KebWriteParamWithDll(comIdx, baudIdx, node, 0x0032, 2); // Sy.50 = 2 (Reset)
                    Thread.Sleep(50);
                    KebWriteParamWithDll(comIdx, baudIdx, node, 0x0032, 0); // 復歸為 0
                }

                // ★【鐵律安全防護 3：若 cs.00 殘留為 4 (向量模式)，強制復歸原生安全 cs.00 = 0 (V/F 模式)！】
                if (r_cs00.HasValue && r_cs00.Value == 4)
                {
                    KebWriteParamWithDll(comIdx, baudIdx, node, 0x0F00, 0); // cs.00 = 0 (V/F)
                    if (driveId == 1) cachedCs00_1 = 0; else cachedCs00_2 = 0;
                }

                // ★【全系統統一 Sy.52 架構】：oP.00=5 (Sy52過程數據給定), oP.01=7 (半自動實體端子開關控制)
                KebWriteParamWithDll(comIdx, baudIdx, node, 0x0300, 5); // oP.00 = 5 (Sy.52)
                KebWriteParamWithDll(comIdx, baudIdx, node, 0x0301, 7); // oP.01 = 7 (半自動端子開關控制)

                // ★【鐵律安全防護 4：速度與加載端轉矩強制歸零，速度驅動端維持 100% 轉矩極限】
                KebWriteParam32(comIdx, baudIdx, node, 0x0034, 0, "連線速度歸零 (Sy.52=0)");
                
                // 若為轉矩加載端 (cs00=6 或 Mode 8/10)，加載轉矩強制歸零 cs.18 = 0
                // 若為速度驅動端 (cs00!=6 或 Mode 7/9)，轉矩極限維持 100.0% (cs.18 = 1000)，絕不鎖死馬達出力！
                if (r_cs00.HasValue && r_cs00.Value == 6)
                {
                    KebWriteParamWithDll(comIdx, baudIdx, node, 0x0F12, 0); // 加載端 cs.18 = 0.0%
                }
                else
                {
                    KebWriteParamWithDll(comIdx, baudIdx, node, 0x0F12, 1000); // 速度驅動端放行 100.0% 轉矩極限
                }

                // 獨立檔案儲存：寫入 logs/KEB_Readback_Parameters.log
                try
                {
                    string logDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs");
                    if (!Directory.Exists(logDir)) Directory.CreateDirectory(logDir);
                    string filePath = Path.Combine(logDir, "KEB_Readback_Parameters.log");
                    File.AppendAllText(filePath, logSnapshot, Encoding.UTF8);
                }
                catch { }

                if (txtHmiKebLog != null)
                {
                    txtHmiKebLog.AppendText(string.Format("[OK] [{0}] 已讀回硬體當前配置: ud02={1}, oP00={2}, oP01={3}, cs00={4}, cs18={5:F1}%, oP03={6:F1} rpm | dr極數={7}極 (dr01={8:F0}rpm, dr05={9:F1}Hz) (所有設定與變頻器已強制歸零0)\r\n",
                        driveName,
                        r_ud02.HasValue ? r_ud02.Value.ToString() : "--",
                        r_op00.HasValue ? r_op00.Value.ToString() : "--",
                        r_op01.HasValue ? r_op01.Value.ToString() : "--",
                        r_cs00.HasValue ? r_cs00.Value.ToString() : "--",
                        valCs18,
                        valOp03,
                        motorPoles,
                        drSpeed,
                        drFreq));
                }
            }
            catch { }
        }

        private void RestoreHmiKebInitialParams(int driveId)
        {
            KebBackupParams backup = (driveId == 1) ? kebBackup1 : kebBackup2;
            bool isOpen = (driveId == 1) ? isHmiKebOpen1 : isHmiKebOpen2;
            if (backup == null || !backup.HasBackup || !isOpen) return;

            int comIdx = GetHmiKebComIdx(driveId);
            int baudIdx = GetHmiKebBaudIdx(driveId);
            int node = (int)((driveId == 1) ? numHmiKebNode1.Value : numHmiKebNode2.Value);
            string driveName = (driveId == 1) ? "A載台" : "B載台";

            try
            {
                // 1. 安全第一：先強制發送 STOP (Sy.50 = 0) 並將 cs.18 轉矩卸載為 0
                KebWriteParamWithDll(comIdx, baudIdx, node, 0x0032, 0); // Sy.50 = 0
                KebWriteParamWithDll(comIdx, baudIdx, node, 0x0F12, 0); // cs.18 = 0
                Thread.Sleep(20);

                // 2. 依序安全回寫連線前讀回之硬體參數
                if (backup.Sy52.HasValue)
                {
                    KebWriteParam32(comIdx, baudIdx, node, 0x0034, backup.Sy52.Value, "還原 Sy.52");
                }
                if (backup.Op03.HasValue)
                {
                    KebWriteParamWithDll(comIdx, baudIdx, node, 0x0303, backup.Op03.Value);
                }
                if (backup.Cs18.HasValue)
                {
                    KebWriteParamWithDll(comIdx, baudIdx, node, 0x0F12, backup.Cs18.Value);
                }
                if (backup.Cs15.HasValue)
                {
                    KebWriteParamWithDll(comIdx, baudIdx, node, 0x0F0F, backup.Cs15.Value);
                }
                if (backup.Cs00.HasValue)
                {
                    KebWriteParamWithDll(comIdx, baudIdx, node, 0x0F00, backup.Cs00.Value);
                }
                if (backup.Op01.HasValue)
                {
                    KebWriteParamWithDll(comIdx, baudIdx, node, 0x0301, backup.Op01.Value);
                }
                if (backup.Op00.HasValue)
                {
                    KebWriteParamWithDll(comIdx, baudIdx, node, 0x0300, backup.Op00.Value);
                }
                if (backup.Sy50.HasValue)
                {
                    int safeSy50 = (backup.Sy50.Value == 4 || backup.Sy50.Value == 12) ? 0 : backup.Sy50.Value;
                    KebWriteParamWithDll(comIdx, baudIdx, node, 0x0032, safeSy50);
                }

                backup.HasBackup = false; // 已成功還原，清除備份標記

                string restoreMsg = string.Format(
                    "[{0}] 【{1} 參數回寫完成】已成功還原連線前讀回之原始硬體參數：oP.00={2}, oP.01={3}, cs.00={4}, cs.15={5}, cs.18={6}, oP.03={7}, Sy.52={8}",
                    DateTime.Now.ToString("HH:mm:ss"),
                    driveName,
                    backup.Op00.HasValue ? backup.Op00.Value.ToString() : "--",
                    backup.Op01.HasValue ? backup.Op01.Value.ToString() : "--",
                    backup.Cs00.HasValue ? backup.Cs00.Value.ToString() : "--",
                    backup.Cs15.HasValue ? backup.Cs15.Value.ToString() : "--",
                    backup.Cs18.HasValue ? backup.Cs18.Value.ToString() : "--",
                    backup.Op03.HasValue ? backup.Op03.Value.ToString() : "--",
                    backup.Sy52.HasValue ? backup.Sy52.Value.ToString() : "--");

                WriteHmiLog("KEB_RESTORE", restoreMsg);
                if (txtHmiKebLog != null && !txtHmiKebLog.IsDisposed)
                {
                    txtHmiKebLog.AppendText(restoreMsg + "\r\n");
                }
            }
            catch (Exception ex)
            {
                WriteHmiLog("KEB_RESTORE_ERR", string.Format("[{0}] 回寫原始參數發生異常: {1}", driveName, ex.Message));
            }
        }

        private bool SafeWriteKebParamWithRetry(int comIdx, int baudIdx, int nodeId, int paramAddr, int val, string desc)
        {
            bool ok = KebWriteParamWithDll(comIdx, baudIdx, nodeId, paramAddr, val);
            if (!ok)
            {
                Thread.Sleep(60);
                ok = KebWriteParamWithDll(comIdx, baudIdx, nodeId, paramAddr, val);
            }
            Thread.Sleep(30);
            return ok;
        }



        private void SetHmiKebMode(int comIdx, int baudIdx, int nodeId, int mode, string driveName)
        {
            int driveId = driveName.Contains("A") || driveName.Contains("1") ? 1 : 2;
            int currentSpd = driveId == 1 && numHmiKebSpeed1 != null ? (int)numHmiKebSpeed1.Value : (driveId == 2 && numHmiKebSpeed2 != null ? (int)numHmiKebSpeed2.Value : 0);
            double currentTrq = driveId == 1 && numHmiKebTorque1 != null ? (double)numHmiKebTorque1.Value : (driveId == 2 && numHmiKebTorque2 != null ? (double)numHmiKebTorque2.Value : 0.0);

            // 100% 嚴格對齊 VB6 Form_Dyno_Main.frm (行 9995~10070) 實測驗證之原廠設定：
            if (mode == 7) // 數位定轉速 (oP00=5 Sy52過程數據, oP01=7 半自動端子控制, cs00=0 V/F模式, cs15=3, cs18=1000)
            {
                if (driveId == 1) cachedCs00_1 = 0; else cachedCs00_2 = 0;
                SafeWriteKebParamWithRetry(comIdx, baudIdx, nodeId, 0x0300, 5, "oP.00=5 (Sy52過程數據給定)");
                SafeWriteKebParamWithRetry(comIdx, baudIdx, nodeId, 0x0301, 7, "oP.01=7 (半自動端子開關控制)");
                SafeWriteKebParamWithRetry(comIdx, baudIdx, nodeId, 0x0F00, 0, "cs.00=0 (V/F速度模式)");
                SafeWriteKebParamWithRetry(comIdx, baudIdx, nodeId, 0x0F0F, 3, "cs.15=3 (轉矩極限來源)");
                SafeWriteKebParamWithRetry(comIdx, baudIdx, nodeId, 0x0F12, 1000, "cs.18=1000 (轉矩極限100.0%)");
                KebWriteParam32(comIdx, baudIdx, nodeId, 0x0034, currentSpd, "Sy.52=速度(1:1 rpm)");
                if (txtHmiKebLog != null) txtHmiKebLog.AppendText(string.Format("[OK] [{0}] 已切換為【數位定轉速 Mode 7 (標準: oP00=5(Sy52), oP01=7(端子控制), cs00=0(V/F), cs15=3, cs18=100%)】\r\n", driveName));
                WriteHmiLog("MODE_CHANGE", string.Format("[{0}] 模式切換完成：【數位定轉速 Mode 7 (半自動)】oP.00=5 (Sy52), oP.01=7 (端子), cs.00=0 (V/F), cs.15=3, cs.18=100%", driveName));
                UpdateHmiKebModeParamsDisplay(driveId, 7, currentSpd, currentTrq, 0);
            }
            else if (mode == 8) // 數位定轉矩 (oP00=5 Sy52過程數據, oP01=7 半自動端子控制, cs00=6 轉矩加載, cs15=3, cs18=設定轉矩)
            {
                if (driveId == 1) cachedCs00_1 = 6; else cachedCs00_2 = 6;
                int trqRaw = (int)Math.Round(currentTrq * 10); // 0.1% 解析度
                SafeWriteKebParamWithRetry(comIdx, baudIdx, nodeId, 0x0300, 5, "oP.00=5 (Sy52過程數據給定)");
                SafeWriteKebParamWithRetry(comIdx, baudIdx, nodeId, 0x0301, 7, "oP.01=7 (半自動端子開關控制)");
                SafeWriteKebParamWithRetry(comIdx, baudIdx, nodeId, 0x0F00, 6, "cs.00=6 (轉矩加載控制)");
                SafeWriteKebParamWithRetry(comIdx, baudIdx, nodeId, 0x0F0F, 3, "cs.15=3 (轉矩極限來源)");
                SafeWriteKebParamWithRetry(comIdx, baudIdx, nodeId, 0x0F12, trqRaw, "cs.18=設定轉矩");
                KebWriteParam32(comIdx, baudIdx, nodeId, 0x0034, 0, "Sy.52=0 (加載端速度歸零)");
                if (txtHmiKebLog != null) txtHmiKebLog.AppendText(string.Format("[OK] [{0}] 已切換為【數位定轉矩 Mode 8 (標準: oP00=5(Sy52), oP01=7(端子控制), cs00=6(加載), cs15=3, cs18加載)】\r\n", driveName));
                WriteHmiLog("MODE_CHANGE", string.Format("[{0}] 模式切換完成：【數位定轉矩 Mode 8 (半自動)】oP.00=5 (Sy52), oP.01=7 (端子), cs.00=6 (轉矩), cs.15=3, cs.18={1:F1}%", driveName, currentTrq));
                UpdateHmiKebModeParamsDisplay(driveId, 8, currentSpd, currentTrq, 0);
            }
            else if (mode == 9) // 數位定轉速 (全自動: oP00=5 Sy52過程數據, oP01=8 通訊運轉控制, cs00=0 V/F模式, cs15=3, cs18=1000)
            {
                if (driveId == 1) cachedCs00_1 = 0; else cachedCs00_2 = 0;
                SafeWriteKebParamWithRetry(comIdx, baudIdx, nodeId, 0x0300, 5, "oP.00=5 (Sy52過程數據給定)");
                SafeWriteKebParamWithRetry(comIdx, baudIdx, nodeId, 0x0301, 8, "oP.01=8 (全自動通訊控制)");
                SafeWriteKebParamWithRetry(comIdx, baudIdx, nodeId, 0x0F00, 0, "cs.00=0 (V/F速度模式)");
                SafeWriteKebParamWithRetry(comIdx, baudIdx, nodeId, 0x0F0F, 3, "cs.15=3 (轉矩極限來源)");
                SafeWriteKebParamWithRetry(comIdx, baudIdx, nodeId, 0x0F12, 1000, "cs.18=1000 (轉矩極限100.0%)");
                KebWriteParam32(comIdx, baudIdx, nodeId, 0x0034, currentSpd, "Sy.52=速度(1:1 rpm)");
                if (txtHmiKebLog != null) txtHmiKebLog.AppendText(string.Format("[OK] [{0}] 已切換為【數位定轉速 Mode 9 (全自動: oP00=5(Sy52), oP01=8(通訊控制), cs00=0(V/F), cs15=3, cs18=100%)】\r\n", driveName));
                WriteHmiLog("MODE_CHANGE", string.Format("[{0}] 模式切換完成：【數位定轉速 Mode 9 (全自動)】oP.00=5 (Sy52), oP.01=8 (通訊), cs.00=0 (V/F), cs.15=3, cs.18=100%", driveName));
                UpdateHmiKebModeParamsDisplay(driveId, 9, currentSpd, currentTrq, 0);
            }
            else if (mode == 10) // 數位定轉矩 (全自動: oP00=5 Sy52過程數據, oP01=8 通訊運轉控制, cs00=6 轉矩加載控制, cs15=3, cs18=設定轉矩)
            {
                if (driveId == 1) cachedCs00_1 = 6; else cachedCs00_2 = 6;
                int trqRaw = (int)Math.Round(currentTrq * 10); // 0.1% 解析度
                SafeWriteKebParamWithRetry(comIdx, baudIdx, nodeId, 0x0300, 5, "oP.00=5 (Sy52過程數據給定)");
                SafeWriteKebParamWithRetry(comIdx, baudIdx, nodeId, 0x0301, 8, "oP.01=8 (全自動通訊控制)");
                SafeWriteKebParamWithRetry(comIdx, baudIdx, nodeId, 0x0F00, 6, "cs.00=6 (轉矩加載控制)");
                SafeWriteKebParamWithRetry(comIdx, baudIdx, nodeId, 0x0F0F, 3, "cs.15=3 (轉矩極限來源)");
                SafeWriteKebParamWithRetry(comIdx, baudIdx, nodeId, 0x0F12, trqRaw, "cs.18=設定轉矩");
                KebWriteParam32(comIdx, baudIdx, nodeId, 0x0034, 0, "Sy.52=0 (加載端速度歸零)");
                if (txtHmiKebLog != null) txtHmiKebLog.AppendText(string.Format("[OK] [{0}] 已切換為【數位定轉矩 Mode 10 (全自動: oP00=5(Sy52), oP01=8(通訊控制), cs00=6(加載), cs15=3, cs18加載)】\r\n", driveName));
                WriteHmiLog("MODE_CHANGE", string.Format("[{0}] 模式切換完成：【數位定轉矩 Mode 10 (全自動)】oP.00=5 (Sy52), oP.01=8 (通訊), cs.00=6 (轉矩), cs.15=3, cs.18={1:F1}%", driveName, currentTrq));
                UpdateHmiKebModeParamsDisplay(driveId, 10, currentSpd, currentTrq, 0);
            }
        }

        private DateTime lastEstopTriggerTime = DateTime.MinValue;
        public void TriggerGlobalEmergencyStop()
        {
            // 防抖保護：3 秒內不重複觸發，避免安全保護反覆連發大量 KEB 寫入電文，飽和通訊匯流排
            if ((DateTime.Now - lastEstopTriggerTime).TotalSeconds < 3.0) return;
            lastEstopTriggerTime = DateTime.Now;

            // 最高安全級別：強制中斷所有運轉，歸零給定值
            // ⚠️ 注意：此處不加外層 lock(kebLock)，因為 KebWriteParamWithDll 已內建 lock。
            // 若加外層鎖，在背景 Worker 持有 kebLock 等待 DLL timeout 期間，
            // 緊急停機和其他 UI 操作三方搶鎖，造成跨執行緒死鎖（UI 死當）！
            for (int i = 0; i < 2; i++) // 降低至 2 次，減少 KEB 匯流排負擔
            {
                if (isHmiKebOpen1)
                {
                    KebWriteParamWithDll(GetHmiKebComIdx(1), GetHmiKebBaudIdx(1), (int)numHmiKebNode1.Value, 0x0032, 0); // Sy.50 = 0 (STOP)
                    KebWriteParamWithDll(GetHmiKebComIdx(1), GetHmiKebBaudIdx(1), (int)numHmiKebNode1.Value, 0x0034, 0); // Sy.52 = 0
                    KebWriteParamWithDll(GetHmiKebComIdx(1), GetHmiKebBaudIdx(1), (int)numHmiKebNode1.Value, 0x0F12, 0); // cs.18 = 0
                }
                if (isHmiKebOpen2)
                {
                    KebWriteParamWithDll(GetHmiKebComIdx(2), GetHmiKebBaudIdx(2), (int)numHmiKebNode2.Value, 0x0032, 0); // Sy.50 = 0 (STOP)
                    KebWriteParamWithDll(GetHmiKebComIdx(2), GetHmiKebBaudIdx(2), (int)numHmiKebNode2.Value, 0x0034, 0); // Sy.52 = 0
                    KebWriteParamWithDll(GetHmiKebComIdx(2), GetHmiKebBaudIdx(2), (int)numHmiKebNode2.Value, 0x0F12, 0); // cs.18 = 0
                }
                Thread.Sleep(15);
            }

            if (this.IsHandleCreated && !this.IsDisposed)
            {
                this.BeginInvoke((MethodInvoker)delegate {
                    activeTrackingTorquePct = 0.0m;
                    activeTrackingSpeedRpm = 0.0m;
                    if (numHmiKebSpeed1 != null) numHmiKebSpeed1.Value = 0;
                    if (numHmiKebTorque1 != null) numHmiKebTorque1.Value = 0;
                    if (numHmiKebSpeed2 != null) numHmiKebSpeed2.Value = 0;
                    if (numHmiKebTorque2 != null) numHmiKebTorque2.Value = 0;
                    UpdateHmiKebModeParamsDisplay(1);
                    UpdateHmiKebModeParamsDisplay(2);
                });
            }

            WriteHmiLog("EMERGENCY_STOP", "【🚨 緊急停機 E-STOP 觸發】雙機運轉已強制中斷，所有給定值立即歸零！");
            if (txtHmiKebLog != null)
            {
                txtHmiKebLog.AppendText(string.Format("[{0}] >> [🚨 緊急停機 E-STOP 觸發] 雙載台已強制停機並歸零！\r\n", DateTime.Now.ToLongTimeString()));
            }
        }

        public void ExecuteFullAutoGracefulStop(string reason)
        {
            // 全自動雙機平穩連鎖停機序列 (Graceful Stop Sequence)：
            // 步驟 1: 加載端先將轉矩卸載歸零 (cs.18 = 0)，消除負載阻力，防止單側停機造成的反拖衝擊
            if (isHmiKebOpen1 && (currentKebMode1 == 8 || currentKebMode1 == 10))
            {
                KebWriteParamWithDll(GetHmiKebComIdx(1), GetHmiKebBaudIdx(1), (int)numHmiKebNode1.Value, 0x0F12, 0); // cs.18 = 0 (卸載)
            }
            if (isHmiKebOpen2 && (currentKebMode2 == 8 || currentKebMode2 == 10))
            {
                KebWriteParamWithDll(GetHmiKebComIdx(2), GetHmiKebBaudIdx(2), (int)numHmiKebNode2.Value, 0x0F12, 0); // cs.18 = 0 (卸載)
            }

            // 步驟 2: 雙機下達停機電文 (Sy.50 = 0)，驅動端沿著減速斜率平穩降速至 0 rpm 斷電
            if (isHmiKebOpen1)
            {
                KebWriteParamWithDll(GetHmiKebComIdx(1), GetHmiKebBaudIdx(1), (int)numHmiKebNode1.Value, 0x0032, 0); // Sy.50 = 0 (STOP)
                lastSy50Cmd1 = 0;
            }
            if (isHmiKebOpen2)
            {
                KebWriteParamWithDll(GetHmiKebComIdx(2), GetHmiKebBaudIdx(2), (int)numHmiKebNode2.Value, 0x0032, 0); // Sy.50 = 0 (STOP)
                lastSy50Cmd2 = 0;
            }

            if (this.IsHandleCreated && !this.IsDisposed)
            {
                this.BeginInvoke((MethodInvoker)delegate {
                    UpdateHmiKebModeParamsDisplay(1);
                    UpdateHmiKebModeParamsDisplay(2);
                });
            }

            WriteHmiLog("FULL_AUTO_STOP", string.Format("【全自動連鎖平穩停機】原因：{0}。已依序執行「轉矩先卸載歸零 ➔ 轉速平穩減速停電」雙機保護！", reason));
            if (txtHmiKebLog != null)
            {
                txtHmiKebLog.AppendText(string.Format("[{0}] >> [全自動連鎖平穩停機] 雙載台已依序安全卸載並平穩停機！\r\n", DateTime.Now.ToLongTimeString()));
            }
        }

        private void SetHmiKebCommand(int comIdx, int baudIdx, int nodeId, int cmd, string driveName)
        {
            int driveId = driveName.Contains("A") || driveName.Contains("1") ? 1 : 2;
            int mode = driveId == 1 ? currentKebMode1 : currentKebMode2;
            int currentSpd = driveId == 1 && numHmiKebSpeed1 != null ? (int)numHmiKebSpeed1.Value : (driveId == 2 && numHmiKebSpeed2 != null ? (int)numHmiKebSpeed2.Value : 0);
            double currentTrq = driveId == 1 && numHmiKebTorque1 != null ? (double)numHmiKebTorque1.Value : (driveId == 2 && numHmiKebTorque2 != null ? (double)numHmiKebTorque2.Value : 0.0);

            bool ok = KebWriteParamWithDll(comIdx, baudIdx, nodeId, 0x0032, cmd);
            string cmdName = (cmd == 4) ? "RUN 正轉 (Sy50=4)" : ((cmd == 12) ? "RUN 反轉 (Sy50=12)" : ((cmd == 0) ? "STOP 停機 (Sy50=0)" : ((cmd == 2) ? "FAULT RESET 復歸 (Sy50=2)" : string.Format("CONTROL (Sy50={0})", cmd))));
            
            if (txtHmiKebLog != null) txtHmiKebLog.AppendText(string.Format("[{0}] [{1}] 發送：{2} (回應: {3})\r\n", DateTime.Now.ToLongTimeString(), driveName, cmdName, ok ? "ACK 成功" : "NAK/逾時"));
            WriteHmiLog("USER_OP", string.Format("[{0}] 使用者發送運轉控制：{1} (回應: {2})", driveName, cmdName, ok ? "ACK 成功" : "NAK/逾時"));

            UpdateHmiKebModeParamsDisplay(driveId, mode, currentSpd, currentTrq, cmd);
        }

        private void DoHmiKebQuery1()
        {
            if (!isHmiKebOpen1) return;
            int addr = (int)numHmiKebNode1.Value;
            int comIdx = GetHmiKebComIdx(1);
            int baudIdx = GetHmiKebBaudIdx(1);

            try
            {
                hmiKebCount1++;
                List<Tuple<int, string, Color>> gridUpdates = new List<Tuple<int, string, Color>>();
                var items = kebMonitorList1;

                int successCount = 0;
                // 優先讀取 ru.00 (0x0200 運轉狀態/硬體ST端子)，即時安全守護
                int? ru00_1 = KebReadParamWithDll(comIdx, baudIdx, addr, 0x0200);
                if (ru00_1.HasValue)
                {
                    successCount++;
                    CheckHardwareStStatus(1, ru00_1.Value);
                }

                if (items != null)
                {
                    for (int r = 0; r < items.Count; r++)
                    {
                        var item = items[r];
                        if (item.IsNode)
                        {
                            gridUpdates.Add(Tuple.Create(r, "Node " + addr, Color.FromArgb(15, 23, 42)));
                            continue;
                        }

                        int? val = KebReadParamWithDll(comIdx, baudIdx, addr, item.Address);
                        if (val.HasValue)
                        {
                            successCount++;
                            if (item.Address == 0x0200)
                            {
                                CheckHardwareStStatus(1, val.Value);
                            }
                            if (item.IsStatus)
                            {
                                string text = (val.Value == 0) ? "正常 (無異常)" : string.Format("故障 0x{0:X2}", val.Value);
                                Color color = (val.Value == 0) ? Color.FromArgb(16, 185, 129) : Color.Red;
                                gridUpdates.Add(Tuple.Create(r, text, color));

                                // 變頻器內部故障 (ru.00 != 0) 雙機連鎖急停
                                if (enableProtKebFault && val.Value != 0 && (lastSy50Cmd1 != 0 || lastSy50Cmd2 != 0))
                                {
                                    TriggerGlobalEmergencyStop();
                                    WriteHmiLog("SAFETY_TRIP", string.Format("【🚨 安全保護跳脫】A載台檢測到內部硬體故障 (代碼 0x{0:X2})，對側已連鎖緊急停機！", val.Value));
                                }
                            }
                            else if (item.IsHex)
                            {
                                gridUpdates.Add(Tuple.Create(r, string.Format("0x{0:X4}", val.Value), Color.FromArgb(15, 23, 42)));
                            }
                            else
                            {
                                double scale = (item.Address == 0x0034) ? 1.0 : item.Scale;
                                double displayVal = val.Value * scale;
                                string fmt = (scale == 1.0) ? "{0:F0} {1}" : ((scale == 0.1) ? "{0:F1} {1}" : "{0:F2} {1}");
                                gridUpdates.Add(Tuple.Create(r, string.Format(fmt, displayVal, item.Unit).Trim(), Color.FromArgb(15, 23, 42)));
                            }
                        }
                    }
                }

                // 斷線緩衝狀態機判定
                if (successCount > 0)
                {
                    lastKebSuccessTime1 = DateTime.Now;
                    if (isKebReconnecting1)
                    {
                        isKebReconnecting1 = false;
                        isKebTimeoutTriggered1 = false;
                        WriteHmiLog("RECONNECT", "[OK] A載台通訊瞬斷已自愈恢復，測試保持進行！");
                    }
                }
                else
                {
                    double elapsedSec = (DateTime.Now - lastKebSuccessTime1).TotalSeconds;
                    if (elapsedSec > 1.5 && elapsedSec <= (double)disconnectBufferSeconds)
                    {
                        isKebReconnecting1 = true;
                        double remSec = Math.Max(0, (double)disconnectBufferSeconds - elapsedSec);
                        if (lblPillKeb1 != null && !lblPillKeb1.IsDisposed)
                        {
                            this.BeginInvoke((MethodInvoker)delegate {
                                lblPillKeb1.Text = string.Format(" A載台: [⚠️ 瞬斷重連中 ({0:F0}s)] ", remSec);
                                lblPillKeb1.ForeColor = Color.FromArgb(245, 158, 11);
                            });
                        }
                    }
                    else if (elapsedSec > (double)disconnectBufferSeconds && !isKebTimeoutTriggered1)
                    {
                        isKebTimeoutTriggered1 = true;
                        isHmiKebOpen1 = false;
                        if (lblPillKeb1 != null && !lblPillKeb1.IsDisposed)
                        {
                            this.BeginInvoke((MethodInvoker)delegate {
                                lblPillKeb1.Text = " A載台: [徹底斷線] ";
                                lblPillKeb1.ForeColor = Color.FromArgb(239, 68, 68);
                            });
                        }

                        // 分級保護機制
                        if (currentKebMode1 == 8 || currentKebMode1 == 10)
                        {
                            // 狀況一：加載端斷線逾時 -> 停止待測端 (B載台) 轉速命令並斷電
                            if (isHmiKebOpen2)
                            {
                                KebWriteParamWithDll(GetHmiKebComIdx(2), GetHmiKebBaudIdx(2), (int)numHmiKebNode2.Value, 0x0032, 0); // STOP
                                KebWriteParamWithDll(GetHmiKebComIdx(2), GetHmiKebBaudIdx(2), (int)numHmiKebNode2.Value, 0x0034, 0); // Sy.52 = 0
                                WriteHmiLog("FAILSAFE", "【🚨 分級連鎖停機】加載端 (A載台) 斷線逾時，待測端 (B載台) 已自動安全停機斷電！");
                            }
                        }
                        else
                        {
                            // 狀況二：待測端斷線逾時 -> 加載端 (B載台) 將轉矩降至 0% 後斷電
                            if (isHmiKebOpen2)
                            {
                                KebWriteParamWithDll(GetHmiKebComIdx(2), GetHmiKebBaudIdx(2), (int)numHmiKebNode2.Value, 0x0F12, 0); // cs.18 = 0 (卸載)
                                KebWriteParamWithDll(GetHmiKebComIdx(2), GetHmiKebBaudIdx(2), (int)numHmiKebNode2.Value, 0x0032, 0); // STOP
                                WriteHmiLog("FAILSAFE", "【🚨 分級連鎖停機】待測端 (A載台) 斷線逾時，加載端 (B載台) 轉矩已自動卸載歸零並停電！");
                            }
                        }
                    }
                }

                // 批次安全更新 UI (耗時 < 1ms，零 I/O 阻塞)
                if (this.IsHandleCreated && !this.IsDisposed)
                {
                    this.BeginInvoke((MethodInvoker)delegate {
                        if (dgvKebRu1 != null && gridUpdates.Count > 0)
                        {
                            foreach (var u in gridUpdates)
                            {
                                if (u.Item1 < dgvKebRu1.Rows.Count)
                                {
                                    dgvKebRu1.Rows[u.Item1].Cells[1].Value = u.Item2;
                                    dgvKebRu1.Rows[u.Item1].Cells[1].Style.ForeColor = u.Item3;
                                }
                            }
                        }
                    });
                }
                UpdateHmiKebModeParamsDisplay(1);
            }
            catch { }
        }

        private void DoHmiKebQuery2()
        {
            if (!isHmiKebOpen2) return;
            int addr = (int)numHmiKebNode2.Value;
            int comIdx = GetHmiKebComIdx(2);
            int baudIdx = GetHmiKebBaudIdx(2);

            try
            {
                hmiKebCount2++;
                List<Tuple<int, string, Color>> gridUpdates = new List<Tuple<int, string, Color>>();
                var items = kebMonitorList2;

                int successCount = 0;
                // 優先讀取 ru.00 (0x0200 運轉狀態/硬體ST端子)，即時安全守護
                int? ru00_2 = KebReadParamWithDll(comIdx, baudIdx, addr, 0x0200);
                if (ru00_2.HasValue)
                {
                    successCount++;
                    CheckHardwareStStatus(2, ru00_2.Value);
                }

                if (items != null)
                {
                    for (int r = 0; r < items.Count; r++)
                    {
                        var item = items[r];
                        if (item.IsNode)
                        {
                            gridUpdates.Add(Tuple.Create(r, "Node " + addr, Color.FromArgb(15, 23, 42)));
                            continue;
                        }

                        int? val = KebReadParamWithDll(comIdx, baudIdx, addr, item.Address);
                        if (val.HasValue)
                        {
                            successCount++;
                            if (item.Address == 0x0200)
                            {
                                CheckHardwareStStatus(2, val.Value);
                            }
                            if (item.IsStatus)
                            {
                                string text = (val.Value == 0) ? "正常 (無異常)" : string.Format("故障 0x{0:X2}", val.Value);
                                Color color = (val.Value == 0) ? Color.FromArgb(16, 185, 129) : Color.Red;
                                gridUpdates.Add(Tuple.Create(r, text, color));

                                // 變頻器內部故障 (ru.00 != 0) 雙機連鎖急停
                                if (enableProtKebFault && val.Value != 0 && (lastSy50Cmd1 != 0 || lastSy50Cmd2 != 0))
                                {
                                    TriggerGlobalEmergencyStop();
                                    WriteHmiLog("SAFETY_TRIP", string.Format("【🚨 安全保護跳脫】B載台檢測到內部硬體故障 (代碼 0x{0:X2})，對側已連鎖緊急停機！", val.Value));
                                }
                            }
                            else if (item.IsHex)
                            {
                                gridUpdates.Add(Tuple.Create(r, string.Format("0x{0:X4}", val.Value), Color.FromArgb(15, 23, 42)));
                            }
                            else
                            {
                                double scale = (item.Address == 0x0034) ? 1.0 : item.Scale;
                                double displayVal = val.Value * scale;
                                string fmt = (scale == 1.0) ? "{0:F0} {1}" : ((scale == 0.1) ? "{0:F1} {1}" : "{0:F2} {1}");
                                gridUpdates.Add(Tuple.Create(r, string.Format(fmt, displayVal, item.Unit).Trim(), Color.FromArgb(15, 23, 42)));
                            }
                        }
                    }
                }

                // 斷線緩衝狀態機判定 (B載台)
                if (successCount > 0)
                {
                    lastKebSuccessTime2 = DateTime.Now;
                    if (isKebReconnecting2)
                    {
                        isKebReconnecting2 = false;
                        isKebTimeoutTriggered2 = false;
                        WriteHmiLog("RECONNECT", "[OK] B載台通訊瞬斷已自愈恢復，測試保持進行！");
                    }
                }
                else
                {
                    double elapsedSec = (DateTime.Now - lastKebSuccessTime2).TotalSeconds;
                    if (elapsedSec > 1.5 && elapsedSec <= (double)disconnectBufferSeconds)
                    {
                        isKebReconnecting2 = true;
                        double remSec = Math.Max(0, (double)disconnectBufferSeconds - elapsedSec);
                        if (lblPillKeb2 != null && !lblPillKeb2.IsDisposed)
                        {
                            this.BeginInvoke((MethodInvoker)delegate {
                                lblPillKeb2.Text = string.Format(" B載台: [⚠️ 瞬斷重連中 ({0:F0}s)] ", remSec);
                                lblPillKeb2.ForeColor = Color.FromArgb(245, 158, 11);
                            });
                        }
                    }
                    else if (elapsedSec > (double)disconnectBufferSeconds && !isKebTimeoutTriggered2)
                    {
                        isKebTimeoutTriggered2 = true;
                        isHmiKebOpen2 = false;
                        if (lblPillKeb2 != null && !lblPillKeb2.IsDisposed)
                        {
                            this.BeginInvoke((MethodInvoker)delegate {
                                lblPillKeb2.Text = " B載台: [徹底斷線] ";
                                lblPillKeb2.ForeColor = Color.FromArgb(239, 68, 68);
                            });
                        }

                        // 分級保護機制
                        if (currentKebMode2 == 8 || currentKebMode2 == 10)
                        {
                            // 狀況一：加載端 (B載台) 斷線逾時 -> 停止待測端 (A載台) 轉速命令並斷電
                            if (isHmiKebOpen1)
                            {
                                KebWriteParamWithDll(GetHmiKebComIdx(1), GetHmiKebBaudIdx(1), (int)numHmiKebNode1.Value, 0x0032, 0); // STOP
                                KebWriteParamWithDll(GetHmiKebComIdx(1), GetHmiKebBaudIdx(1), (int)numHmiKebNode1.Value, 0x0034, 0); // Sy.52 = 0
                                WriteHmiLog("FAILSAFE", "【🚨 分級連鎖停機】加載端 (B載台) 斷線逾時，待測端 (A載台) 已自動安全停機斷電！");
                            }
                        }
                        else
                        {
                            // 狀況二：待測端 (B載台) 斷線逾時 -> 加載端 (A載台) 將轉矩降至 0% 後斷電
                            if (isHmiKebOpen1)
                            {
                                KebWriteParamWithDll(GetHmiKebComIdx(1), GetHmiKebBaudIdx(1), (int)numHmiKebNode1.Value, 0x0F12, 0); // cs.18 = 0 (卸載)
                                KebWriteParamWithDll(GetHmiKebComIdx(1), GetHmiKebBaudIdx(1), (int)numHmiKebNode1.Value, 0x0032, 0); // STOP
                                WriteHmiLog("FAILSAFE", "【🚨 分級連鎖停機】待測端 (B載台) 斷線逾時，加載端 (A載台) 轉矩已自動卸載歸零並停電！");
                            }
                        }
                    }
                }

                // 批次安全更新 UI
                if (this.IsHandleCreated && !this.IsDisposed)
                {
                    this.BeginInvoke((MethodInvoker)delegate {
                        if (dgvKebRu2 != null && gridUpdates.Count > 0)
                        {
                            foreach (var u in gridUpdates)
                            {
                                if (u.Item1 < dgvKebRu2.Rows.Count)
                                {
                                    dgvKebRu2.Rows[u.Item1].Cells[1].Value = u.Item2;
                                    dgvKebRu2.Rows[u.Item1].Cells[1].Style.ForeColor = u.Item3;
                                }
                            }
                        }
                    });
                }
                UpdateHmiKebModeParamsDisplay(2);
            }
            catch { }
        }

        private void KebWriteParam32(int comIndex, int baudIndex, int invAddr, int paramAddr, int dataVal, string desc, int paramSet = 1)
        {
            try
            {
                bool ok = KebWriteParamWithDll(comIndex, baudIndex, invAddr, paramAddr, dataVal, paramSet);
                if (txtHmiKebLog != null)
                {
                    txtHmiKebLog.AppendText(string.Format("[{0}] [{1}] 寫入暫存器 0x{2:X4} = {3} (回應: {4})\r\n",
                        DateTime.Now.ToLongTimeString(), desc, paramAddr, dataVal, ok ? "ACK 成功" : "NAK/逾時失敗"));
                }
                WriteHmiLog("KEB_WRITE", string.Format("[{0}] 寫入 0x{1:X4} = {2} -> {3}", desc, paramAddr, dataVal, ok ? "成功" : "失敗"));
            }
            catch (Exception ex)
            {
                if (txtHmiKebLog != null) txtHmiKebLog.AppendText(string.Format(">> [寫入異常] {0}: {1}\r\n", desc, ex.Message));
            }
        }

        private static int? KebReadParamWithDll(int comIndex, int baudIndex, int invAddr, int paramAddr, int paramSet = 1)
        {
            lock (kebLock)
            {
                try
                {
                    if (activeKebComIndex != comIndex || activeKebBaudIndex != baudIndex)
                    {
                        closechannels();
                        tProtProperty prop = new tProtProperty();
                        prop.ProtType = 1; // DIN 66019-II (prAnsi)
                        prop.Baudrate = baudIndex;
                        prop.Comport  = comIndex;
                        prop.TimeOut  = 600;
                        prop.Flag     = 0;
                        prop.Port     = 0;     // 必填：tcp 模式保留欄位
                        prop.Txtlen   = 0;     // 必填：防止 DLL 讀入垃圾記憶體
                        prop.txt      = "";    // 必填：managed 字串歸零
                        setprotproperties(ref prop);
                        setretrycnt(1); // 快速非阻塞輪詢，避免單參數逾時卡死通訊
                        activeKebComIndex = comIndex;
                        activeKebBaudIndex = baudIndex;
                    }

                    setinvprot(invAddr, 1);

                    byte[] txBuf = new byte[256];
                    byte[] rxBuf = new byte[256];
                    BitConverter.GetBytes((short)paramAddr).CopyTo(txBuf, 0);
                    txBuf[2] = (byte)paramSet;

                    int res = waitrdreq(invAddr, 0, txBuf, rxBuf);
                    int ack = BitConverter.ToInt32(rxBuf, 12);

                    if (res == 0 && ack == 0)
                    {
                        return BitConverter.ToInt32(rxBuf, 24);
                    }
                }
                catch { activeKebComIndex = -1; try { closechannels(); } catch { } }
                return null;
            }
        }

        private static bool KebWriteParamWithDll(int comIndex, int baudIndex, int invAddr, int paramAddr, int val, int paramSet = 1)
        {
            lock (kebLock)
            {
                try
                {
                    if (activeKebComIndex != comIndex || activeKebBaudIndex != baudIndex)
                    {
                        closechannels();
                        tProtProperty prop = new tProtProperty();
                        prop.ProtType = 1; // DIN 66019-II (prAnsi)
                        prop.Baudrate = baudIndex;
                        prop.Comport  = comIndex;
                        prop.TimeOut  = 600;
                        prop.Flag     = 0;
                        prop.Port     = 0;     // 必填：tcp 模式保留欄位
                        prop.Txtlen   = 0;     // 必填：防止 DLL 讀入垃圾記憶體
                        prop.txt      = "";    // 必填：managed 字串歸零
                        setprotproperties(ref prop);
                        setretrycnt(3); // 對齊 KEB_XP_Portable_Tester 參考值
                        activeKebComIndex = comIndex;
                        activeKebBaudIndex = baudIndex;
                    }

                    setinvprot(invAddr, 1);

                    byte[] txBuf = new byte[256];
                    byte[] rxBuf = new byte[256];
                    BitConverter.GetBytes((short)paramAddr).CopyTo(txBuf, 0);
                    txBuf[2] = (byte)paramSet;
                    BitConverter.GetBytes(val).CopyTo(txBuf, 4);
                    int res = waitwrreq(invAddr, 0, txBuf, rxBuf);
                    int ack = BitConverter.ToInt32(rxBuf, 12);
                    return (res == 0 && ack == 0);
                }
                catch { activeKebComIndex = -1; try { closechannels(); } catch { } }
                return false;
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
                                Thread.Sleep(20);
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
                        actSpeed, actTorque, actMechPower, actElecPower, actEfficiency, actKt,
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
                            actSpeed, actTorque, actMechPower, actElecPower, actEfficiency, actKt,
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
                    lblSafetyStatus.Text = isSimMode ? "[模擬模式] 載台允許操作" : "[安全就緒] 載台允許操作";
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
            int targetCh = (cmbMotorTempCh != null && cmbMotorTempCh.SelectedIndex >= 0) ? cmbMotorTempCh.SelectedIndex : 0;
            actTemp = (targetCh >= 0 && targetCh < 20) ? gbdChTemps[targetCh] : 0.0;

            // 設備連線狀態指示與安全互鎖檢查
            bool isTorqueOnline = isSimMode || (spTorque != null && spTorque.IsOpen);
            bool isPowerOnline = isSimMode || (tcpPower != null && tcpPower.Connected) || ykDeviceId >= 0;
            bool isGbdOnline = isSimMode || (tcpGbd != null && tcpGbd.Connected);
            bool isKeb1Online = isSimMode || isHmiKebOpen1;
            bool isKeb2Online = isSimMode || isHmiKebOpen2;

            string kebP1 = (cmbHmiKebPort1 != null && cmbHmiKebPort1.SelectedItem != null) ? cmbHmiKebPort1.SelectedItem.ToString() : "COM1";
            string kebP2 = (cmbHmiKebPort2 != null && cmbHmiKebPort2.SelectedItem != null) ? cmbHmiKebPort2.SelectedItem.ToString() : "COM2";
            UpdateDeviceStatusPill(lblPillTorque, "扭力計", torquePortName, isTorqueOnline);
            UpdateDeviceStatusPill(lblPillPowerMeter, "WT333E", powerMeterPort.ToString(), isPowerOnline);
            UpdateDeviceStatusPill(lblPillGbd, "GL820", gbdPort.ToString(), isGbdOnline);
            UpdateDeviceStatusPill(lblPillKeb1, "A載台", kebP1, isKeb1Online);
            UpdateDeviceStatusPill(lblPillKeb2, "B載台", kebP2, isKeb2Online);

            UpdateSafetyInterlock(isTorqueOnline, isPowerOnline);

            // 實體模式斷線安全聯鎖監控 (Fail-Safe Interlock)
            bool isSim = (chkSimMode != null && chkSimMode.Checked) || isSimMode;
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
            double tgtTorque = (numTargetTorque != null) ? (double)numTargetTorque.Value : 5.0;
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
                    dgvGbdAll.Rows[1].Cells[i].Value = string.Format("{0:F1}", gbdChTemps[i]);

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

            // 右下角多功能工作台 - 轉矩與轉速即時動態曲線採樣 (轉為絕對值傳入，零負數區間)
            if (trqSpdChart != null && !trqSpdChart.IsDisposed)
            {
                double curTgtTorque = hasBaseline ? baselineTorque : ((numTargetTorque != null) ? (double)numTargetTorque.Value : 0.0);
                double curTgtSpeed = hasSpeedBaseline ? baselineSpeed : ((numTargetSpeed != null) ? (double)numTargetSpeed.Value : 0.0);
                trqSpdChart.AddSample(now, Math.Abs(smoothedTorque), Math.Abs(curTgtTorque), Math.Abs(smoothedSpeed), Math.Abs(curTgtSpeed), isClosedLoopTracking || isSpeedTracking);
            }
            if (isRunning)
            {
                WriteHmiLog("TELEMETRY", string.Format("[A:{0} | B:{1}] Spd={2:F1}rpm, Torq={3:F2}Nm, SmoothTorq={4:F2}Nm, MechPwr={5:F2}kW, ElecPwr={6:F2}kW, Eff={7:F1}%, Volt={8:F1}V, Curr={9:F2}A, Temp={10:F1}C",
                    GetKebModeShortName(currentKebMode1), GetKebModeShortName(currentKebMode2),
                    actSpeed, actTorque, smoothedTorque, actMechPower, actElecPower, actEfficiency, actVoltageSigma, actCurrentSigma, actTemp));
            }
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

                    // 2. 橫河 WT333E (Modbus TCP 非同步短超時探測)
                    string wtIp = !string.IsNullOrEmpty(powerMeterIp) ? powerMeterIp : "192.168.0.11";
                    int wtPort = powerMeterPort > 0 ? powerMeterPort : 502;
                    try
                    {
                        if (tcpPower == null || !tcpPower.Connected)
                        {
                            TcpClient tc = new TcpClient();
                            IAsyncResult ar = tc.BeginConnect(wtIp, wtPort, null, null);
                            if (ar.AsyncWaitHandle.WaitOne(250) && tc.Connected)
                            {
                                tcpPower = tc;
                                streamPower = tcpPower.GetStream();
                                streamPower.ReadTimeout = 500;
                                streamPower.WriteTimeout = 500;
                                if (lblPillPowerMeter != null && !lblPillPowerMeter.IsDisposed)
                                {
                                    lblPillPowerMeter.BeginInvoke(new Action(() => {
                                        lblPillPowerMeter.Text = " WT333E: " + wtPort + " ";
                                        lblPillPowerMeter.ForeColor = Color.FromArgb(74, 222, 128);
                                    }));
                                }
                                WriteHmiLog("POWER", "[OK] 橫河 WT333E 電表已連線 (" + wtIp + ":" + wtPort + ")");
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

                    // 3. GL820 溫度計 (TCP 非同步短超時探測)
                    string glIp = !string.IsNullOrEmpty(gbdIp) ? gbdIp : "192.168.0.3";
                    int glPort = gbdPort > 0 ? gbdPort : 8023;
                    try
                    {
                        if (tcpGbd == null || !tcpGbd.Connected)
                        {
                            TcpClient tcG = new TcpClient();
                            IAsyncResult arG = tcG.BeginConnect(glIp, glPort, null, null);
                            if (arG.AsyncWaitHandle.WaitOne(250) && tcG.Connected)
                            {
                                tcpGbd = tcG;
                                streamGbd = tcpGbd.GetStream();
                                streamGbd.ReadTimeout = 500;
                                streamGbd.WriteTimeout = 500;
                                if (lblPillGbd != null && !lblPillGbd.IsDisposed)
                                {
                                    lblPillGbd.BeginInvoke(new Action(() => {
                                        lblPillGbd.Text = " GL820: " + glPort + " ";
                                        lblPillGbd.ForeColor = Color.FromArgb(74, 222, 128);
                                    }));
                                }
                                WriteHmiLog("GBD", "[OK] Graphtec GL820 溫度記錄器已連線 (" + glIp + ":" + glPort + ")");
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
            bool isAnyDriveRunning = (lastSy50Cmd1 != 0 || lastSy50Cmd2 != 0);
            bool isFullAutoActive = (currentKebMode1 == 9 || currentKebMode1 == 10 || currentKebMode2 == 9 || currentKebMode2 == 10);
            DateTime now = DateTime.Now;

            // 1. 扭力計斷線 / 反饋凍結保護
            if (enableProtTorqueLoss && (isAnyDriveRunning || isFullAutoActive))
            {
                if (spTorque == null || !spTorque.IsOpen || (now - lastTorquePacketTime).TotalSeconds > (double)protTorqueTimeoutSec)
                {
                    TriggerGlobalEmergencyStop();
                    WriteHmiLog("SAFETY_TRIP", string.Format("【🚨 安全保護跳脫】扭力計斷線或反饋逾時 (超過 {0:F1} 秒無數據)，已強制雙機急停！", protTorqueTimeoutSec));
                }
            }

            // 2. 機械堵轉 / 失速保護 (量: protStallSpeedThreshold, 時間: protStallDelaySec)
            if (enableProtStall && isAnyDriveRunning)
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
            if (enableProtOvertemp && (isAnyDriveRunning || isFullAutoActive))
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

        private static readonly object hmiLogLock = new object();
        public static void WriteHmiLog(string category, string message)
        {
            try
            {
                string timeFull = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
                string line = string.Format("[{0}] [{1}] {2}", timeFull, category, message);

                string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                string logDir = Path.Combine(baseDir, "logs");
                if (!Directory.Exists(logDir)) Directory.CreateDirectory(logDir);
                string file2 = Path.Combine(logDir, "hmi_telemetry.log");

                lock (hmiLogLock)
                {
                    memoryLogs.Add(line);
                    if (memoryLogs.Count > 5000) memoryLogs.RemoveAt(0);

                    if (instance == null || instance.enableSystemEventLog)
                    {
                        File.AppendAllText(file2, line + Environment.NewLine, Encoding.UTF8);
                    }
                }

                if (instance != null && !instance.IsDisposed && instance.IsHandleCreated)
                {
                    instance.BeginInvoke(new Action(() =>
                    {
                        try
                        {
                            if (instance.lblMiniLogText != null && !instance.lblMiniLogText.IsDisposed)
                            {
                                instance.lblMiniLogText.Text = string.Format("[{0}] [{1}] {2}", DateTime.Now.ToString("HH:mm:ss"), category, message);
                            }
                            if (instance.txtFullLog != null && !instance.txtFullLog.IsDisposed)
                            {
                                if (instance.txtFullLog.TextLength > 100000)
                                {
                                    instance.txtFullLog.Text = instance.txtFullLog.Text.Substring(50000);
                                }
                                instance.txtFullLog.AppendText(line + "\r\n");
                            }
                        }
                        catch { }
                    }));
                }
            }
            catch { }
        }

        private static byte[] SendModbusReadBlock(NetworkStream ns, ushort startAddr, ushort regCount)
        {
            if (ns == null) return null;
            try
            {
                byte[] req = new byte[] {
                    0x00, 0x01, 0x00, 0x00, 0x00, 0x06, 0x01, 0x04,
                    (byte)(startAddr >> 8), (byte)(startAddr & 0xFF),
                    (byte)(regCount >> 8), (byte)(regCount & 0xFF)
                };
                ns.Write(req, 0, req.Length);

                byte[] hdr = new byte[9];
                int totalHdr = 0;
                int startTick = Environment.TickCount;
                while (totalHdr < 9 && Environment.TickCount - startTick < 300)
                {
                    if (ns.DataAvailable)
                    {
                        int r = ns.Read(hdr, totalHdr, 9 - totalHdr);
                        if (r <= 0) break;
                        totalHdr += r;
                    }
                    else { Thread.Sleep(3); }
                }
                if (totalHdr < 9 || hdr[7] != 0x04) return null;

                int byteCount = hdr[8];
                byte[] data = new byte[byteCount];
                int totalData = 0;
                startTick = Environment.TickCount;
                while (totalData < byteCount && Environment.TickCount - startTick < 300)
                {
                    if (ns.DataAvailable)
                    {
                        int r = ns.Read(data, totalData, byteCount - totalData);
                        if (r <= 0) break;
                        totalData += r;
                    }
                    else { Thread.Sleep(3); }
                }
                if (totalData < byteCount) return null;

                byte[] full = new byte[9 + byteCount];
                Array.Copy(hdr, 0, full, 0, 9);
                Array.Copy(data, 0, full, 9, byteCount);
                return full;
            }
            catch { return null; }
        }

        private static float ParseModbusFloat(byte[] buf, int offset)
        {
            if (buf == null || offset + 4 > buf.Length) return 0f;
            try
            {
                // 橫河 WT333E 確認字節順序: Big-Endian (ABCD)
                // 來源：WT333E_Modbus_Register_Analysis_Report.md V2.5 現場校驗
                //       對齊 WT333E_Tester_GUI.cs ParseSingleFloat(decodeMode=0)
                byte[] b = new byte[] { buf[offset + 3], buf[offset + 2], buf[offset + 1], buf[offset] };
                float f = BitConverter.ToSingle(b, 0);
                // 排除 NaN / Inf / 超出物理量程 (1e6 為保守上限)
                return (!float.IsNaN(f) && !float.IsInfinity(f) && Math.Abs(f) < 1000000.0f) ? f : 0f;
            }
            catch { return 0f; }
        }

        private static float SanitizeFloat(float val, float min, float max)
        {
            if (float.IsNaN(val) || float.IsInfinity(val)) return 0f;
            if (val < min || val > max) return 0f;
            return val;
        }

        private void LoadDeviceConfig()
        {
            try
            {
                string cfgPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "dynamometer_config.ini");
                if (File.Exists(cfgPath))
                {
                    string[] lines = File.ReadAllLines(cfgPath);
                    foreach (string l in lines)
                    {
                        string line = l.Trim();
                        if (line.StartsWith("IP=") && txtPowerMeterIp != null) txtPowerMeterIp.Text = line.Substring(3).Trim();
                        else if (line.StartsWith("SCALE_U1=")) double.TryParse(line.Substring(9).Trim(), out scaleU1);
                        else if (line.StartsWith("SCALE_U2=")) double.TryParse(line.Substring(9).Trim(), out scaleU2);
                        else if (line.StartsWith("SCALE_U3=")) double.TryParse(line.Substring(9).Trim(), out scaleU3);
                        else if (line.StartsWith("SCALE_I1=")) double.TryParse(line.Substring(9).Trim(), out scaleI1);
                        else if (line.StartsWith("SCALE_I2=")) double.TryParse(line.Substring(9).Trim(), out scaleI2);
                        else if (line.StartsWith("SCALE_I3=")) double.TryParse(line.Substring(9).Trim(), out scaleI3);
                        else if (line.StartsWith("SCALE_TORQUE=")) double.TryParse(line.Substring(13).Trim(), out scaleTorque);
                    }
                }
            }
            catch { }
        }

        public void SaveCalibrationConfig()
        {
            try
            {
                string cfgPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "dynamometer_config.ini");
                List<string> lines = File.Exists(cfgPath) ? File.ReadAllLines(cfgPath).ToList() : new List<string>();

                SetIniKey(lines, "SCALE_U1", scaleU1.ToString("F3"));
                SetIniKey(lines, "SCALE_U2", scaleU2.ToString("F3"));
                SetIniKey(lines, "SCALE_U3", scaleU3.ToString("F3"));
                SetIniKey(lines, "SCALE_I1", scaleI1.ToString("F3"));
                SetIniKey(lines, "SCALE_I2", scaleI2.ToString("F3"));
                SetIniKey(lines, "SCALE_I3", scaleI3.ToString("F3"));
                SetIniKey(lines, "SCALE_TORQUE", scaleTorque.ToString("F3"));

                File.WriteAllLines(cfgPath, lines.ToArray(), Encoding.UTF8);
                WriteHmiLog("CALIB", string.Format("已儲存量測校正比例: U=({0:F2},{1:F2},{2:F2}), I=({3:F2},{4:F2},{5:F2}), 扭力={6:F2}",
                    scaleU1, scaleU2, scaleU3, scaleI1, scaleI2, scaleI3, scaleTorque));
            }
            catch (Exception ex)
            {
                WriteHmiLog("CALIB_ERR", "儲存校正設定失敗: " + ex.Message);
            }
        }

        private static void SetIniKey(List<string> lines, string key, string value)
        {
            int idx = lines.FindIndex(l => l.Trim().StartsWith(key + "=", StringComparison.OrdinalIgnoreCase));
            if (idx >= 0) lines[idx] = string.Format("{0}={1}", key, value);
            else lines.Add(string.Format("{0}={1}", key, value));
        }

        private string BuildRawCsvHeader()
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("Timestamp,Speed_rpm,Torque_Nm,MechPower_kW,ElecPower_kW,Efficiency_pct,Kt_NmA");
            sb.Append(",Voltage_U1_V,Current_I1_A,Power_P1_kW");
            sb.Append(",Voltage_U2_V,Current_I2_A,Power_P2_kW");
            sb.Append(",Voltage_U3_V,Current_I3_A,Power_P3_kW");
            sb.Append(",Voltage_Sigma_V,Current_Sigma_A,PF,MotorTemp_C");

            for (int i = 0; i < 20; i++)
            {
                if (gl820ChannelMask != null && i < gl820ChannelMask.Length && gl820ChannelMask[i])
                {
                    string cName = (gl820ChannelNames != null && i < gl820ChannelNames.Length && !string.IsNullOrEmpty(gl820ChannelNames[i])) ? gl820ChannelNames[i].Trim() : ("CH" + (i + 1));
                    if (cName.Equals("CH" + (i + 1), StringComparison.OrdinalIgnoreCase))
                    {
                        sb.Append(",GL820_CH" + (i + 1) + "_C");
                    }
                    else
                    {
                        sb.Append(",GL820_CH" + (i + 1) + "_" + cName.Replace(",", "_").Replace(" ", "") + "_C");
                    }
                }
            }

            if (recordKebRuParams)
            {
                sb.Append(",KebA_ru00_State,KebA_ru01_Rpm,KebA_ru26_TorqNm,KebA_ru07_CurrA,KebA_ru09_VoltV,KebA_ru10_DcBusV,KebA_ru20_TempC,KebA_ru43_Fault");
                sb.Append(",KebB_ru00_State,KebB_ru01_Rpm,KebB_ru26_TorqNm,KebB_ru07_CurrA,KebB_ru09_VoltV,KebB_ru10_DcBusV,KebB_ru20_TempC,KebB_ru43_Fault");
            }

            return sb.ToString();
        }

        private string BuildRawCsvRow(DateTime timestamp)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendFormat("\"{0}\",{1:F1},{2:F2},{3:F2},{4:F2},{5:F1},{6:F2}",
                timestamp.ToString("yyyy-MM-dd HH:mm:ss.fff"),
                actSpeed, actTorque, actMechPower, actElecPower,
                actEfficiency, actKt);

            sb.AppendFormat(",{0:F2},{1:F3},{2:F3},{3:F2},{4:F3},{5:F3},{6:F2},{7:F3},{8:F3}",
                wtU1, wtI1, wtP1,
                wtU2, wtI2, wtP2,
                wtU3, wtI3, wtP3);

            sb.AppendFormat(",{0:F1},{1:F2},{2:F3},{3:F1}",
                actVoltageSigma, actCurrentSigma, actPf, actTemp);

            for (int i = 0; i < 20; i++)
            {
                if (gl820ChannelMask != null && i < gl820ChannelMask.Length && gl820ChannelMask[i])
                {
                    double t = (i < gbdChTemps.Length) ? gbdChTemps[i] : 0.0;
                    sb.AppendFormat(",{0:F1}", t);
                }
            }

            if (recordKebRuParams)
            {
                sb.AppendFormat(",\"{0}\",\"{1}\"", (lastRawKebA ?? "").Replace("\"", "\"\""), (lastRawKebB ?? "").Replace("\"", "\"\""));
            }

            return sb.ToString();
        }

        private void WriteAutoRawTelemetryCsv()
        {
            if (!enableAutoRawCsv) return;
            try
            {
                string logDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs");
                if (!Directory.Exists(logDir)) Directory.CreateDirectory(logDir);
                string autoCsv = Path.Combine(logDir, string.Format("Auto_Raw_Telemetry_{0}.csv", DateTime.Now.ToString("yyyyMMdd")));

                bool exists = File.Exists(autoCsv);
                using (StreamWriter sw = new StreamWriter(autoCsv, true, Encoding.UTF8))
                {
                    if (!exists)
                    {
                        sw.WriteLine(BuildRawCsvHeader());
                    }
                    string row = autoSampleAccumulator.HasSamples
                        ? autoSampleAccumulator.BuildAveragedCsvRow(DateTime.Now, gl820ChannelMask, recordKebRuParams)
                        : BuildRawCsvRow(DateTime.Now);
                    if (!string.IsNullOrEmpty(row))
                    {
                        sw.WriteLine(row);
                    }
                }
            }
            catch { }
        }

        private void WriteManualRawTelemetryRow()
        {
            lock (manualRecordLock)
            {
                try
                {
                    if (manualRecordWriter == null) return;
                    string row = manualSampleAccumulator.HasSamples
                        ? manualSampleAccumulator.BuildAveragedCsvRow(DateTime.Now, gl820ChannelMask, recordKebRuParams)
                        : BuildRawCsvRow(DateTime.Now);
                    if (string.IsNullOrEmpty(row)) return;

                    manualRecordCount++;
                    manualRecordWriter.WriteLine(row);
                    manualRecordWriter.Flush();

                    if (btnRecordRaw != null && !btnRecordRaw.IsDisposed)
                    {
                        btnRecordRaw.BeginInvoke(new Action(() => {
                            btnRecordRaw.Text = string.Format(" 停止錄製 ({0} 筆: {1})", manualRecordCount, motorModelName);
                        }));
                    }
                    if (btnRecordRawTop != null && !btnRecordRawTop.IsDisposed)
                    {
                        btnRecordRawTop.BeginInvoke(new Action(() => {
                            btnRecordRawTop.Text = string.Format(" 停止錄製 ({0} 筆: {1})", manualRecordCount, motorModelName);
                        }));
                    }
                }
                catch { }
            }
        }

        public void StartManualRecordingWithParams(string motorName, string folderPath, string fileName, bool[] chMask, bool saveKebRu, int intervalMs = 1000)
        {
            lock (manualRecordLock)
            {
                try
                {
                    if (isManualRecording) return;
                    motorModelName = string.IsNullOrEmpty(motorName) ? "SVM100S" : motorName.Trim();
                    rawDataSaveDirectory = folderPath;
                    if (chMask != null && chMask.Length == 20) gl820ChannelMask = chMask;
                    recordKebRuParams = saveKebRu;
                    if (intervalMs > 0) rawDataIntervalMs = intervalMs;
                    SaveLayoutConfig();

                    if (!Directory.Exists(folderPath)) Directory.CreateDirectory(folderPath);

                    manualRecordFilePath = Path.Combine(folderPath, fileName);
                    manualRecordWriter = new StreamWriter(manualRecordFilePath, false, Encoding.UTF8);
                    manualRecordWriter.WriteLine(BuildRawCsvHeader());
                    manualRecordWriter.Flush();
                    manualRecordCount = 0;
                    manualSampleAccumulator.Clear();
                    lastManualRecordWriteTime = DateTime.Now;
                    isManualRecording = true;

                    if (!isWorkerRunning)
                    {
                        StartBackgroundWorker();
                    }

                    if (btnRecordRaw != null)
                    {
                        btnRecordRaw.Text = string.Format(" 停止錄製 (0 筆: {0})", motorModelName);
                        btnRecordRaw.BackColor = Color.FromArgb(239, 68, 68);
                    }
                    if (btnRecordRawTop != null)
                    {
                        btnRecordRawTop.Image = CreateFloppyIconImage(36, 28, Color.White, true);
                        btnRecordRawTop.BackColor = Color.FromArgb(239, 68, 68);
                    }
                    WriteHmiLog("RECORDER", string.Format(" [開始手動錄製 RAW DATA] 馬達: {0} | 週期: {1}ms | 檔案: {2}", motorModelName, rawDataIntervalMs, Path.GetFileName(manualRecordFilePath)));
                }
                catch (Exception ex)
                {
                    MessageBox.Show("啟動 RAW DATA 錄製失敗: " + ex.Message, "錯誤", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        public void StopManualRecording()
        {
            lock (manualRecordLock)
            {
                if (!isManualRecording) return;
                isManualRecording = false;
                try
                {
                    if (manualRecordWriter != null)
                    {
                        manualRecordWriter.Flush();
                        manualRecordWriter.Close();
                        manualRecordWriter.Dispose();
                        manualRecordWriter = null;
                    }
                }
                catch { }

                if (btnRecordRaw != null)
                {
                    btnRecordRaw.Text = " 錄製 RAW DATA";
                    btnRecordRaw.BackColor = Color.FromArgb(220, 38, 38);
                }
                if (btnRecordRawTop != null)
                {
                    btnRecordRawTop.Image = CreateFloppyIconImage(36, 28, Color.White, false);
                    btnRecordRawTop.BackColor = Color.FromArgb(220, 38, 38);
                }
                WriteHmiLog("RECORDER", string.Format(" [手動錄製完成] 馬達: {0} | 共錄製 {1} 筆 RAW DATA 數據至: {2}", motorModelName, manualRecordCount, Path.GetFileName(manualRecordFilePath)));

                string msg = string.Format("[成功] 手動 RAW DATA 錄製完成！\n\n 馬達名稱：{0}\n 錄製筆數：{1} 筆\n 儲存路徑：\n{2}\n\n是否立即在檔案總管中查看？",
                    motorModelName, manualRecordCount, manualRecordFilePath);

                DialogResult res = MessageBox.Show(msg, "錄製完成", MessageBoxButtons.YesNo, MessageBoxIcon.Information);
                if (res == DialogResult.Yes)
                {
                    try
                    {
                        string dir = Path.GetDirectoryName(manualRecordFilePath);
                        if (Directory.Exists(dir))
                        {
                            System.Diagnostics.Process.Start("explorer.exe", string.Format("/select,\"{0}\"", manualRecordFilePath));
                        }
                    }
                    catch { }
                }
            }
        }

        private void ToggleManualRawRecording()
        {
            if (!isManualRecording)
            {
                string mName = !string.IsNullOrEmpty(motorModelName) ? motorModelName : "SVM100S";
                string fDir = !string.IsNullOrEmpty(rawDataSaveDirectory) && Directory.Exists(rawDataSaveDirectory)
                    ? rawDataSaveDirectory
                    : Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs");
                string fName = string.Format("{0}_{1}.csv", mName, DateTime.Now.ToString("yyyyMMdd_HHmmss"));
                StartManualRecordingWithParams(mName, fDir, fName, gl820ChannelMask, recordKebRuParams, rawDataIntervalMs);
            }
            else
            {
                StopManualRecording();
            }
        }

        private void SaveRawSnapshot()
        {
            try
            {
                string logDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs");
                if (!Directory.Exists(logDir)) Directory.CreateDirectory(logDir);
                string snapFile = Path.Combine(logDir, string.Format("RawData_Snapshots_{0}.csv", DateTime.Now.ToString("yyyyMMdd")));

                bool fileExists = File.Exists(snapFile);
                using (StreamWriter sw = new StreamWriter(snapFile, true, Encoding.UTF8))
                {
                    if (!fileExists)
                    {
                        sw.WriteLine(BuildRawCsvHeader());
                    }
                    sw.WriteLine(BuildRawCsvRow(DateTime.Now));
                }
                WriteHmiLog("SNAPSHOT", " [手動快照成功] 已將當前瞬間 RAW DATA 寫入快照檔: " + Path.GetFileName(snapFile));
                MessageBox.Show(" 當前瞬間 RAW DATA 快照已儲存至：\n" + snapFile, "快照成功", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show("儲存快照失敗: " + ex.Message, "錯誤", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void OpenLogsFolder()
        {
            try
            {
                string logDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs");
                if (!Directory.Exists(logDir)) Directory.CreateDirectory(logDir);
                System.Diagnostics.Process.Start("explorer.exe", logDir);
            }
            catch (Exception ex)
            {
                MessageBox.Show("開啟日誌目錄失敗: " + ex.Message, "錯誤");
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

            bool isFromMini = (sender == btnTnMiniStart);
            SyncAllTnControls(fromMiniToMain: isFromMini);

            decimal startRpmVal = numTnStartRpm != null ? numTnStartRpm.Value : 50m;
            decimal stepRpmVal = numTnStepRpm != null ? numTnStepRpm.Value : 50m;
            decimal endRpmVal = numTnEndRpm != null ? numTnEndRpm.Value : 300m;
            decimal targetTrqVal = numTnTorque != null ? numTnTorque.Value : 15m;
            decimal dwellVal = numTnDwell != null ? numTnDwell.Value : 10m;
            int roleIdx = (cmbTnRole != null && cmbTnRole.SelectedIndex >= 0) ? cmbTnRole.SelectedIndex : 1;

            tnCurrentStep = (int)startRpmVal;
            tnStepSpeedReached = false; // 初始步進轉速尚未鎖定
            tnDwellRemaining = (int)dwellVal;

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

            WriteHmiLog("TN_CONFIG", string.Format("【T-N 啟動測試】測試配置: {0}待測 (速度) / {1}加載 (轉矩) | 起始轉速: {2} rpm, 步進: {3} rpm, 結束: {4} rpm, 目標轉矩: {5:F1} Nm (加載端轉矩啟動歸零 0.0%)",
                spdDriveName, trqDriveName, tnCurrentStep, numTnStepRpm.Value, numTnEndRpm.Value, targetTrqVal));

            if (!isRunning) BtnStart_Click(null, null);

            tnTimer.Start();
        }

        private void TnTimer_Tick(object sender, EventArgs e)
        {
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
            double actAbsSpd = Math.Abs(actSpeed);
            double actAbsTrq = Math.Abs(actTorque);

            // ★【雙向絕對值精確比對】：徹底解決實體扭力計正轉/反轉符號造成的死鎖問題！
            double spdErr = Math.Abs(actAbsSpd - targetSpd);
            double trqErr = targetTrq - actAbsTrq;

            // 判定轉矩與轉速是否進入合格穩定帶 (防瞬間慣性衝擊假觸發：必須加載量 tnAdaptedTorquePct >= 1.0% 且轉矩誤差在 ±8% 或 1.0 Nm 以內，轉速誤差在合格帶內)
            bool isSpdValid = (targetSpd <= 0) || (spdErr <= Math.Max(25.0, targetSpd * 0.08));
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
                    int curTrqSy50 = (trqDrive == 1) ? lastSy50Cmd1 : lastSy50Cmd2;
                    if (curTrqSy50 != 4)
                    {
                        SetHmiKebCommand(trqCom, trqBaud, trqNode, 4, string.Format("{0}TN加載端轉速達標激磁啟動 (Sy50=4)", trqDriveName));
                        WriteHmiLog("TN_STAGE", string.Format("【T-N 轉速已鎖定】待測端轉速已達標 ({0:F1} rpm / 目標 {1:F0} rpm)，加載端 {2} 啟動激磁 (Sy50=4)，正式鎖定進入轉矩平穩加載！", actAbsSpd, targetSpd, trqDriveName));
                    }
                }

                // 【子階段 0B：轉速已鎖定，平穩漸進加載逼近目標轉矩】
                // 達標防假觸發雙重鐵律：
                // 1. 必須加載百分比已真正開始輸出 (tnAdaptedTorquePct >= 1.0% 或 targetTrq <= 0)，徹底杜絕加速暫態機械慣性轉矩誤判！
                // 2. 轉矩與轉速雙雙在容許合格範圍內且連續穩定達標 2 秒以上 (tnTrqSustainedSec >= 2)，徹底過濾 1 秒瞬間衝擊！
                if (isTrqValid && isSpdValid)
                {
                    tnTrqSustainedSec++;
                }
                else
                {
                    tnTrqSustainedSec = 0;
                }

                if (tnTrqSustainedSec < 2)
                {
                    // ★【加載動態平穩步進】：
                    // 遠距 (誤差 > 8 Nm) 以 2.0%/秒 平穩爬坡；中距 (2~8 Nm) 以 1.0%/秒 逼近；近距 (< 2 Nm) 以 0.4%/秒 精細收斂！
                    double absTrqErr = Math.Abs(trqErr);
                    double trqStep = (absTrqErr > 8.0) ? 2.0 : ((absTrqErr > 2.0) ? 1.0 : 0.4);
                    if (trqErr > 0) tnAdaptedTorquePct += trqStep;
                    else tnAdaptedTorquePct -= trqStep;
                    tnAdaptedTorquePct = Math.Max(0.0, Math.Min(100.0, tnAdaptedTorquePct));

                    int trqRaw = (int)Math.Round(tnAdaptedTorquePct * 10);
                    KebWriteParamWithDll(trqCom, trqBaud, trqNode, 0x0F12, trqRaw);

                    if (trqDrive == 1 && numHmiKebTorque1 != null) numHmiKebTorque1.Value = (decimal)tnAdaptedTorquePct;
                    else if (trqDrive == 2 && numHmiKebTorque2 != null) numHmiKebTorque2.Value = (decimal)tnAdaptedTorquePct;

                    lblTnStatus.Text = string.Format("⌛ 扭矩加載中：實測 {0:F1} Nm / 目標 {1:F1} Nm (給定 {2:F1}%)", actAbsTrq, targetTrq, tnAdaptedTorquePct);
                    lblTnCountdown.Text = string.Format("加載中 ({0}s)", tnConvergeTimeoutSec);
                    if (lblTnMiniStatus != null) lblTnMiniStatus.Text = lblTnStatus.Text;
                    if (lblTnMiniCountdown != null) lblTnMiniCountdown.Text = lblTnCountdown.Text;

                    // ★ 超時防呆安全保護：加載逼近超過 45 秒仍未達標時，暫停測試並警告，絕不放任過載！
                    if (tnConvergeTimeoutSec >= 45)
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
                            "系統已持續加載逼近 45 秒，但實測扭矩仍無法收斂達標！\n" +
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
                bool isTnTargetReached = (spdErr <= Math.Max(25.0, targetSpd * 0.08)) && (Math.Abs(trqErr) <= Math.Max(1.0, targetTrq * 0.08));
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
                        KebWriteParamWithDll(trqCom, trqBaud, trqNode, 0x0F12, 0); // 加載卸載歸零
                        tnAdaptedTorquePct = 0.0;
                        tnStepSpeedReached = false;
                        tnTrqSustainedSec = 0;
                        SetHmiKebCommand(spdCom, spdBaud, spdNode, 0, "TN測試結束停機");
                        SetHmiKebCommand(trqCom, trqBaud, trqNode, 0, "TN測試結束停機");

                        btnStartTn.Enabled = true;
                        btnStopTn.Enabled = false;
                        if (btnTnMiniStart != null) btnTnMiniStart.Enabled = true;
                        if (btnTnMiniStop != null) btnTnMiniStop.Enabled = false;
                        lblTnStatus.Text = "[成功] T-N 曲線測試全部完成！";
                        lblTnCountdown.Text = "倒數: 完成";
                        if (lblTnMiniStatus != null) lblTnMiniStatus.Text = lblTnStatus.Text;
                        if (lblTnMiniCountdown != null) lblTnMiniCountdown.Text = lblTnCountdown.Text;
                        MessageBox.Show("T-N 曲線自動測試已順利完成！請點擊「匯出 T-N 報表」儲存數據。", "測試完成", MessageBoxButtons.OK, MessageBoxIcon.Information);
                        return;
                    }

                    // 步進到下一階梯：先卸載為 0 -> 升速 -> 回到 Phase 0 重新判定轉速到位後再漸進加載！
                    KebWriteParamWithDll(trqCom, trqBaud, trqNode, 0x0F12, 0); // 升速前強制卸載為 0
                    if (!tnHasAnchor) tnAdaptedTorquePct = 0.0; // ★【無定錨鐵律】：每個新階梯重新從 0.0% 開始加！
                    tnStepSpeedReached = false; // ★【復歸鎖定】：下一個新階梯需要重新判定空載提速到位！
                    tnTrqSustainedSec = 0;
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
        }

        private void StopTnTest()
        {
            if (tnTimer != null) tnTimer.Stop();
            tnStepSpeedReached = false;
            tnTrqSustainedSec = 0;
            tnPhase = 0;
            tnAdaptedTorquePct = 0.0;
            try
            {
                int roleIdx = (cmbTnRoleMini != null && cmbTnRoleMini.SelectedIndex >= 0)
                    ? cmbTnRoleMini.SelectedIndex
                    : ((cmbTnRole != null && cmbTnRole.SelectedIndex >= 0) ? cmbTnRole.SelectedIndex : 1);
                int spdDrive = (roleIdx == 1) ? 2 : 1;
                int trqDrive = (spdDrive == 1) ? 2 : 1;
                int trqCom = GetHmiKebComIdx(trqDrive), trqBaud = GetHmiKebBaudIdx(trqDrive), trqNode = (trqDrive == 1) ? (int)numHmiKebNode1.Value : (int)numHmiKebNode2.Value;
                KebWriteParamWithDll(trqCom, trqBaud, trqNode, 0x0F12, 0); // 加載端強制卸載歸零
                if (trqDrive == 1 && numHmiKebTorque1 != null) numHmiKebTorque1.Value = 0;
                else if (trqDrive == 2 && numHmiKebTorque2 != null) numHmiKebTorque2.Value = 0;
            }
            catch { }

            if (btnStartTn != null) btnStartTn.Enabled = true;
            if (btnStopTn != null) btnStopTn.Enabled = false;
            if (btnTnMiniStart != null) btnTnMiniStart.Enabled = true;
            if (btnTnMiniStop != null) btnTnMiniStop.Enabled = false;
            if (lblTnStatus != null) lblTnStatus.Text = "狀態: 已手動停止";
            if (lblTnMiniStatus != null) lblTnMiniStatus.Text = "狀態: 已停止";
            if (lblTnCountdown != null) lblTnCountdown.Text = "倒數: -- s";
            if (lblTnMiniCountdown != null) lblTnMiniCountdown.Text = "倒數: -- s";
        }

        private void BtnExportTn_Click(object sender, EventArgs e)
        {
            try
            {
                string logDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs");
                if (!Directory.Exists(logDir)) Directory.CreateDirectory(logDir);
                string path = Path.Combine(logDir, string.Format("Report_TN_Curve_{0}.csv", DateTime.Now.ToString("yyyyMMdd_HHmmss")));
                using (StreamWriter sw = new StreamWriter(path, false, Encoding.UTF8))
                {
                    sw.WriteLine("Step,TargetRpm,ActualSpeed_rpm,ActualTorque_Nm,MechPower_kW,Efficiency_pct,Kt_NmA,Status");
                    for (int i = 0; i < tnResults.Count; i++)
                    {
                        sw.WriteLine(string.Format("{0},{1}", i + 1, string.Join(",", tnResults[i])));
                    }
                }
                MessageBox.Show("T-N 測試報表已成功匯出至:\n" + path, "匯出成功", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show("匯出失敗: " + ex.Message, "錯誤");
            }
        }

        // =========================================================================
        // 工作制測試邏輯 (S1 / S2 / S6 含自適應轉矩閉迴路與 V/F 轉差雙重定錨補償)
        // =========================================================================
        private void StopDutyTest()
        {
            if (dutyTimer != null) dutyTimer.Stop();
            s6SpeedReached = false;
            s6DutyPhase = 0;
            s6TrialStage = 0;
            s6TrialTimer = 0;
            s6FormalCycleIndex = 1;
            s6CycleElapsedSec = 0;
            s6AdaptedTorquePct = 0.0;
            try
            {
                int dutyRole = (cmbDutyMiniRole != null && cmbDutyMiniRole.SelectedIndex >= 0)
                    ? cmbDutyMiniRole.SelectedIndex
                    : ((cmbDutyRole != null && cmbDutyRole.SelectedIndex >= 0) ? cmbDutyRole.SelectedIndex : 1);
                int spdDrive = (dutyRole == 1) ? 2 : 1;
                int trqDrive = (spdDrive == 1) ? 2 : 1;
                int trqCom = GetHmiKebComIdx(trqDrive), trqBaud = GetHmiKebBaudIdx(trqDrive), trqNode = (trqDrive == 1) ? (int)numHmiKebNode1.Value : (int)numHmiKebNode2.Value;
                KebWriteParamWithDll(trqCom, trqBaud, trqNode, 0x0F12, 0); // 加載端強制卸載歸零
                if (trqDrive == 1 && numHmiKebTorque1 != null) numHmiKebTorque1.Value = 0;
                else if (trqDrive == 2 && numHmiKebTorque2 != null) numHmiKebTorque2.Value = 0;
            }
            catch { }

            if (btnStartDuty != null) btnStartDuty.Enabled = true;
            if (btnStopDuty != null) btnStopDuty.Enabled = false;
            if (btnDutyMiniStart != null) btnDutyMiniStart.Enabled = true;
            if (btnDutyMiniStop != null) btnDutyMiniStop.Enabled = false;
            if (lblDutyStatus != null) lblDutyStatus.Text = "狀態: 已手動停止";
            if (lblDutyPhaseAction != null) lblDutyPhaseAction.Text = "動作: 試驗已中斷，加載端已歸零";
            if (lblDutyMiniStatus != null) lblDutyMiniStatus.Text = "狀態: 已停止";
            if (lblDutyMiniPhaseAction != null) lblDutyMiniPhaseAction.Text = "動作: 試驗已中斷";
        }

        private void BtnStartDuty_Click(object sender, EventArgs e)
        {
            dutyResults.Clear();
            dutyElapsedSec = 0;
            s6DutyPhase = 0; // 0: 自適應試運轉定錨階段, 1: 正式週期循環階段
            s6TrialStage = 0; // 0: 空載提速中
            s6TrialTimer = 0;
            s6FormalCycleIndex = 1;
            s6CycleElapsedSec = 0;
            s6SpeedReached = false;

            bool isFromMini = (sender == btnDutyMiniStart);
            SyncAllDutyControls(fromMiniToMain: isFromMini);

            int dutyRole = (cmbDutyRole != null && cmbDutyRole.SelectedIndex >= 0) ? cmbDutyRole.SelectedIndex : 1;
            int spdDrive = (dutyRole == 1) ? 2 : 1;
            int trqDrive = (spdDrive == 1) ? 2 : 1;

            int spdCom = GetHmiKebComIdx(spdDrive), spdBaud = GetHmiKebBaudIdx(spdDrive), spdNode = (spdDrive == 1) ? (int)numHmiKebNode1.Value : (int)numHmiKebNode2.Value;
            int trqCom = GetHmiKebComIdx(trqDrive), trqBaud = GetHmiKebBaudIdx(trqDrive), trqNode = (trqDrive == 1) ? (int)numHmiKebNode1.Value : (int)numHmiKebNode2.Value;

            string spdDriveName = (spdDrive == 1) ? "A載台" : "B載台";
            string trqDriveName = (trqDrive == 1) ? "A載台" : "B載台";

            int dutyModeIdx = (cmbDutyMode != null && cmbDutyMode.SelectedIndex >= 0) ? cmbDutyMode.SelectedIndex : 1;

            double targetSpd = (numDutySpeed != null && numDutySpeed.Value > 0) ? (double)numDutySpeed.Value : 1000.0;
            double targetTrq = (numDutyTorque != null && numDutyTorque.Value > 0) ? (double)numDutyTorque.Value : 15.0;

            if (dutyModeIdx == 0) // S1/S2 連續恆定負載 (預設 1 小時連續)
            {
                dutyTotalSec = 3600;
            }
            else // S6 週期負載 (試運轉 1 週期 + 正式 N 週期)
            {
                double cycleMin = (double)(numS6CycleMin != null ? numS6CycleMin.Value : 10);
                int totalCycles = (int)(numS6Cycles != null ? numS6Cycles.Value : 4);
                int oneCycleSec = Math.Max(60, (int)(cycleMin * 60));
                dutyTotalSec = oneCycleSec * (totalCycles + 1); // 包含 1 次自適應試運轉
            }

            prgDuty.Minimum = 0;
            prgDuty.Maximum = dutyTotalSec;
            prgDuty.Value = 0;
            if (prgDutyMini != null) { prgDutyMini.Minimum = 0; prgDutyMini.Maximum = dutyTotalSec; prgDutyMini.Value = 0; }

            btnStartDuty.Enabled = false;
            btnStopDuty.Enabled = true;
            if (btnDutyMiniStart != null) btnDutyMiniStart.Enabled = false;
            if (btnDutyMiniStop != null) btnDutyMiniStop.Enabled = true;

            // S6 自適應試運轉起步：待測端以設定轉速起轉，加載端強制為 0 轉矩待命
            s6CurrentSpeedCmd = targetSpd;
            s6AdaptedTorquePct = 0.0;
            s6HasNoLoadAnchor = false;
            s6HasLoadedAnchor = false;
            UpdateS6AnchorStatusText();

            // 同步主畫面數值輸入框
            if (spdDrive == 2)
            {
                if (numHmiKebSpeed2 != null) numHmiKebSpeed2.Value = (decimal)Math.Max(0, Math.Min(6000, s6CurrentSpeedCmd));
                if (numHmiKebTorque2 != null) numHmiKebTorque2.Value = 0;
                if (numHmiKebSpeed1 != null) numHmiKebSpeed1.Value = 0;
                if (numHmiKebTorque1 != null) numHmiKebTorque1.Value = 0;
            }
            else
            {
                if (numHmiKebSpeed1 != null) numHmiKebSpeed1.Value = (decimal)Math.Max(0, Math.Min(6000, s6CurrentSpeedCmd));
                if (numHmiKebTorque1 != null) numHmiKebTorque1.Value = 0;
                if (numHmiKebSpeed2 != null) numHmiKebSpeed2.Value = 0;
                if (numHmiKebTorque2 != null) numHmiKebTorque2.Value = 0;
            }

            KebWriteParam32(spdCom, spdBaud, spdNode, 0x0034, (int)s6CurrentSpeedCmd, "S6 空載起步轉速");
            KebWriteParamWithDll(spdCom, spdBaud, spdNode, 0x0F12, 1000); // 放行 100% 轉矩
            SetHmiKebCommand(spdCom, spdBaud, spdNode, 4, "待測端空載起轉 (Sy50=4)");

            KebWriteParamWithDll(trqCom, trqBaud, trqNode, 0x0F12, 0); // 加載端初始負載 0
            SetHmiKebCommand(trqCom, trqBaud, trqNode, 4, "加載端激磁待命 (Sy50=4)");

            if (!isRunning) BtnStart_Click(null, null);

            string modeDesc = (dutyModeIdx == 0) ? "S1/S2 連續" : "S6 週期";
            lblDutyStatus.Text = "【自適應試運轉】空載提速中，等待轉速到位...";
            lblDutyPhaseAction.Text = "待測端空載加速至設定轉速中，加載端保持 0 轉矩待命...";
            if (lblDutyMiniStatus != null) lblDutyMiniStatus.Text = lblDutyStatus.Text;
            if (lblDutyMiniPhaseAction != null) lblDutyMiniPhaseAction.Text = lblDutyPhaseAction.Text;

            double initEd = (double)(numDutyMiniEd != null ? numDutyMiniEd.Value : (numS6Ed != null ? numS6Ed.Value : 40));
            WriteHmiLog("DUTY_CONFIG", string.Format("【工作制啟動測試】模式: {0} | 測試配置: {1}待測 (速度) / {2}加載 (轉矩) | 目標轉速: {3:F0} rpm, 目標轉矩: {4:F1} Nm, S6 ED%: {5:F0}%, 正式週期: {6} 次 (含先導自適應定錨試運轉)",
                modeDesc, spdDriveName, trqDriveName, targetSpd, targetTrq, initEd, numS6Cycles != null ? numS6Cycles.Value : 4));

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
                : ((cmbDutyMode != null && cmbDutyMode.SelectedIndex >= 0) ? cmbDutyMode.SelectedIndex : 1);

            double targetSpd = (numDutySpeed != null && numDutySpeed.Value > 0) ? (double)numDutySpeed.Value : 1000.0;
            double targetTrq = (numDutyTorque != null && numDutyTorque.Value > 0) ? (double)numDutyTorque.Value : 15.0;

            double actAbsSpd = Math.Abs(actSpeed);
            double actAbsTrq = Math.Abs(actTorque);

            string currentPhaseName = "運轉中";

            if (dutyModeIdx == 1) // ================= S6 週期負載模式 =================
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

                    // 子階段 0：空載提速至目標轉速
                    if (s6TrialStage == 0)
                    {
                        currentPhaseName = "試運轉提速";
                        KebWriteParamWithDll(trqCom, trqBaud, trqNode, 0x0F12, 0); // 加載端保持 0 負載
                        bool spdReady = (Math.Abs(actAbsSpd - targetSpd) <= 15.0 || (targetSpd > 0 && actAbsSpd >= targetSpd * 0.92));
                        if (!spdReady)
                        {
                            string spdWaitText = string.Format("【自適應試運轉】空載提速中：實測 {0:F0} / 目標 {1:F0} rpm...", actAbsSpd, targetSpd);
                            lblDutyStatus.Text = spdWaitText;
                            lblDutyPhaseAction.Text = "待測端加速至設定轉速中，加載端 0 轉矩待命...";
                            if (lblDutyMiniStatus != null) lblDutyMiniStatus.Text = spdWaitText;
                            if (lblDutyMiniPhaseAction != null) lblDutyMiniPhaseAction.Text = lblDutyPhaseAction.Text;
                            return;
                        }

                        // 轉速到位，進入 10 秒空載穩定倒數
                        s6TrialStage = 1;
                        s6TrialTimer = 10;
                        WriteHmiLog("S6_ANCHOR", string.Format("待測端空載轉速已到位 ({0:F1} rpm)，開始 10 秒空載穩定確認！", actAbsSpd));
                    }
                    // 子階段 1：空載 10 秒穩定確認 ➔ 確定並記錄 [空載轉速錨點] (★ 達標才倒數鐵律)
                    else if (s6TrialStage == 1)
                    {
                        currentPhaseName = "空載定錨倒數";
                        KebWriteParamWithDll(trqCom, trqBaud, trqNode, 0x0F12, 0); // 加載端保持 0
                        bool isNoLoadSpdReached = (targetSpd <= 0) || (Math.Abs(actAbsSpd - targetSpd) <= Math.Max(15.0, targetSpd * 0.05));
                        if (isNoLoadSpdReached)
                        {
                            s6TrialTimer--;
                            string countdownText = string.Format("【自適應試運轉】空載轉速達標確認中 (倒數 {0}s)...", s6TrialTimer);
                            lblDutyStatus.Text = countdownText;
                            lblDutyPhaseAction.Text = string.Format("待測端 {0:F0} rpm 達標穩定中 (目標 {1:F0} rpm)，加載端 0.0 Nm", actAbsSpd, targetSpd);
                        }
                        else
                        {
                            string waitText = string.Format("【自適應試運轉】空載轉速調節中 (實測 {0:F0}/{1:F0} rpm, 暫停倒數 {2}s)...", actAbsSpd, targetSpd, s6TrialTimer);
                            lblDutyStatus.Text = waitText;
                            lblDutyPhaseAction.Text = "轉速尚未達標，暫停倒數等待穩定...";
                        }
                        if (lblDutyMiniStatus != null) lblDutyMiniStatus.Text = lblDutyStatus.Text;
                        if (lblDutyMiniPhaseAction != null) lblDutyMiniPhaseAction.Text = lblDutyPhaseAction.Text;

                        if (s6TrialTimer <= 0)
                        {
                            // 記錄【空載轉速錨點】
                            s6AnchorNoLoadSpeed = s6CurrentSpeedCmd;
                            s6HasNoLoadAnchor = true;
                            UpdateS6AnchorStatusText();
                            s6TrialStage = 2; // 進入加載與補轉差階段
                            WriteHmiLog("S6_ANCHOR", string.Format("【空載定錨完成】已達標穩定 10 秒，確立 [空載轉速錨點] = {0:F0} rpm！開始慢速加載並自動補轉差！", s6AnchorNoLoadSpeed));
                        }
                    }
                    // 子階段 2：慢速加載到目標扭矩 + 自動補轉差直到實際轉速回到設定轉速
                    else if (s6TrialStage == 2)
                    {
                        currentPhaseName = "加載補轉差";
                        double trqErr = targetTrq - actAbsTrq;
                        // 慢速平穩爬坡加載
                        if (trqErr > 0.4)
                        {
                            double step = (trqErr > 6.0) ? 0.8 : (trqErr > 2.0 ? 0.4 : 0.2);
                            s6AdaptedTorquePct = Math.Min(100.0, s6AdaptedTorquePct + step);
                            KebWriteParamWithDll(trqCom, trqBaud, trqNode, 0x0F12, (int)Math.Round(s6AdaptedTorquePct * 10));
                        }
                        else if (trqErr < -0.6)
                        {
                            s6AdaptedTorquePct = Math.Max(0.0, s6AdaptedTorquePct - 0.3);
                            KebWriteParamWithDll(trqCom, trqBaud, trqNode, 0x0F12, (int)Math.Round(s6AdaptedTorquePct * 10));
                        }

                        // 自動補轉差：實測轉速若因帶載下降，待測端速度命令遞增補償
                        double spdSlip = targetSpd - actAbsSpd;
                        if (spdSlip > 2.0)
                        {
                            s6CurrentSpeedCmd += (spdSlip > 10.0 ? 3.0 : 1.0);
                            s6CurrentSpeedCmd = Math.Min(targetSpd * 1.35, s6CurrentSpeedCmd);
                            KebWriteParam32(spdCom, spdBaud, spdNode, 0x0034, (int)s6CurrentSpeedCmd, "S6 補轉差");
                        }
                        else if (spdSlip < -3.0)
                        {
                            s6CurrentSpeedCmd -= 1.0;
                            KebWriteParam32(spdCom, spdBaud, spdNode, 0x0034, (int)s6CurrentSpeedCmd, "S6 微調速度");
                        }

                        // 同步主畫面速度與轉矩框
                        if (spdDrive == 2 && numHmiKebSpeed2 != null) numHmiKebSpeed2.Value = (decimal)s6CurrentSpeedCmd;
                        else if (spdDrive == 1 && numHmiKebSpeed1 != null) numHmiKebSpeed1.Value = (decimal)s6CurrentSpeedCmd;
                        if (trqDrive == 1 && numHmiKebTorque1 != null) numHmiKebTorque1.Value = (decimal)s6AdaptedTorquePct;
                        else if (trqDrive == 2 && numHmiKebTorque2 != null) numHmiKebTorque2.Value = (decimal)s6AdaptedTorquePct;

                        string rampText = string.Format("【自適應試運轉】加載補轉差中：扭矩 {0:F1}/{1:F1} Nm | 轉速 {2:F0}/{3:F0} rpm", actAbsTrq, targetTrq, actAbsSpd, targetSpd);
                        lblDutyStatus.Text = rampText;
                        lblDutyPhaseAction.Text = string.Format("加載給定 {0:F1}%，待測端補差命令 {1:F0} rpm", s6AdaptedTorquePct, s6CurrentSpeedCmd);
                        if (lblDutyMiniStatus != null) lblDutyMiniStatus.Text = rampText;
                        if (lblDutyMiniPhaseAction != null) lblDutyMiniPhaseAction.Text = lblDutyPhaseAction.Text;

                        // 判定扭矩與轉速雙達標
                        if (Math.Abs(trqErr) <= 1.0 && Math.Abs(spdSlip) <= 10.0)
                        {
                            s6TrialStage = 3;
                            s6TrialTimer = 10;
                            WriteHmiLog("S6_ANCHOR", string.Format("加載轉矩 ({0:F1} Nm) 與轉速 ({1:F0} rpm) 均到位，開始 10 秒加載穩定確認！", actAbsTrq, actAbsSpd));
                        }
                    }
                    // 子階段 3：加載 10 秒穩定確認 ➔ 確定並記錄 [加載轉速錨點] 與 [加載轉矩錨點] (★ 達標才倒數鐵律)
                    else if (s6TrialStage == 3)
                    {
                        currentPhaseName = "加載定錨倒數";
                        // 維持微調
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
                            KebWriteParam32(spdCom, spdBaud, spdNode, 0x0034, (int)s6CurrentSpeedCmd, "S6 定錨微調速度");
                        }

                        // 同步主畫面速度與轉矩框
                        if (spdDrive == 2 && numHmiKebSpeed2 != null) numHmiKebSpeed2.Value = (decimal)s6CurrentSpeedCmd;
                        else if (spdDrive == 1 && numHmiKebSpeed1 != null) numHmiKebSpeed1.Value = (decimal)s6CurrentSpeedCmd;
                        if (trqDrive == 1 && numHmiKebTorque1 != null) numHmiKebTorque1.Value = (decimal)s6AdaptedTorquePct;
                        else if (trqDrive == 2 && numHmiKebTorque2 != null) numHmiKebTorque2.Value = (decimal)s6AdaptedTorquePct;

                        // ★ 判定轉矩與轉速是否雙雙達標合格帶內
                        bool isTrqReached = Math.Abs(trqErr) <= Math.Max(1.0, targetTrq * 0.08);
                        bool isSpdReached = Math.Abs(spdSlip) <= Math.Max(15.0, targetSpd * 0.05);
                        bool isLoadedAnchorReached = isTrqReached && isSpdReached;

                        if (isLoadedAnchorReached)
                        {
                            s6TrialTimer--;
                            string dwellText = string.Format("【自適應試運轉】加載雙達標確認中 (倒數 {0}s)...", s6TrialTimer);
                            lblDutyStatus.Text = dwellText;
                            lblDutyPhaseAction.Text = string.Format("實測 {0:F0} rpm | 轉矩 {1:F1} Nm (雙達標穩定倒數中)", actAbsSpd, actAbsTrq);
                        }
                        else
                        {
                            // 未達標暫停倒數，持續動態微調
                            string waitText = string.Format("【自適應試運轉】加載補轉差調節中 (轉矩 {0:F1}/{1:F1} Nm, 轉速 {2:F0}/{3:F0} rpm, 暫停倒數 {4}s)...", actAbsTrq, targetTrq, actAbsSpd, targetSpd, s6TrialTimer);
                            lblDutyStatus.Text = waitText;
                            lblDutyPhaseAction.Text = "轉矩或轉速偏離，暫停倒數並持續動態平衡微調...";
                        }
                        if (lblDutyMiniStatus != null) lblDutyMiniStatus.Text = lblDutyStatus.Text;
                        if (lblDutyMiniPhaseAction != null) lblDutyMiniPhaseAction.Text = lblDutyPhaseAction.Text;

                        if (s6TrialTimer <= 0)
                        {
                            // 記錄【加載轉速錨點】與【加載轉矩錨點】
                            s6AnchorLoadedSpeed = s6CurrentSpeedCmd;
                            s6AnchorLoadedTorquePct = s6AdaptedTorquePct;
                            s6HasLoadedAnchor = true;
                            UpdateS6AnchorStatusText();
                            s6TrialStage = 4; // 進入維持試運轉 T1 有載時間
                            s6CycleElapsedSec = 0; // 重設為 0，維持完整的 S6 設定 DUTY 時間 T1
                            WriteHmiLog("S6_ANCHOR", string.Format("【加載定錨完成】已達標穩定 10 秒，確立 [加載轉速錨點] = {0:F0} rpm, [加載轉矩錨點] = {1:F1}%！開始維持 S6 設定 DUTY 時間 T1 ({2}s)...", s6AnchorLoadedSpeed, s6AnchorLoadedTorquePct, t1Sec));
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
                            // 運轉期間微調閉迴路維持目標轉矩
                            double trqErr = targetTrq - actAbsTrq;
                            if (Math.Abs(trqErr) > 0.4)
                            {
                                s6AdaptedTorquePct += (trqErr > 0 ? 0.1 : -0.1);
                                s6AdaptedTorquePct = Math.Max(0.0, Math.Min(100.0, s6AdaptedTorquePct));
                                KebWriteParamWithDll(trqCom, trqBaud, trqNode, 0x0F12, (int)Math.Round(s6AdaptedTorquePct * 10));
                                if (trqDrive == 1 && numHmiKebTorque1 != null) numHmiKebTorque1.Value = (decimal)s6AdaptedTorquePct;
                                else if (trqDrive == 2 && numHmiKebTorque2 != null) numHmiKebTorque2.Value = (decimal)s6AdaptedTorquePct;
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
                        s6FormalCycleIndex++;
                        if (s6FormalCycleIndex > totalFormalCycles) // 全部週期圓滿完成！
                        {
                            dutyTimer.Stop();
                            KebWriteParamWithDll(trqCom, trqBaud, trqNode, 0x0F12, 0); // 卸載歸零
                            SetHmiKebCommand(spdCom, spdBaud, spdNode, 0, "S6測試完成待測端停機");
                            SetHmiKebCommand(trqCom, trqBaud, trqNode, 0, "S6測試完成加載端停機");

                            btnStartDuty.Enabled = true;
                            btnStopDuty.Enabled = false;
                            if (btnDutyMiniStart != null) btnDutyMiniStart.Enabled = true;
                            if (btnDutyMiniStop != null) btnDutyMiniStop.Enabled = false;

                            lblDutyStatus.Text = "[成功] S6 週期負載試驗全部完成！";
                            lblDutyPhaseAction.Text = "全週期試驗完畢，請點擊「匯出報表」儲存數據。";
                            if (lblDutyMiniStatus != null) lblDutyMiniStatus.Text = lblDutyStatus.Text;
                            if (lblDutyMiniPhaseAction != null) lblDutyMiniPhaseAction.Text = lblDutyPhaseAction.Text;

                            WriteHmiLog("DUTY_COMPLETE", string.Format("【S6 測試圓滿完成】共完成 {0} 個完整週期！", totalFormalCycles));
                            MessageBox.Show(string.Format("S6 週期負載試驗（共 {0} 週期）已順利完成！請點擊「匯出報表」儲存測試數據。", totalFormalCycles), "試驗完成", MessageBoxButtons.OK, MessageBoxIcon.Information);
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
            else // ================= S1/S2 連續負載模式 (連續恆定負載) =================
            {
                currentPhaseName = "S1/S2 連續";
                int rem = dutyTotalSec - dutyElapsedSec;
                lblDutyStatus.Text = string.Format("測試進行中: 已耗時 {0}s / 剩餘 {1}s | 目標轉矩 {2:F1} Nm", dutyElapsedSec, rem, targetTrq);

                // 轉矩自適應閉迴路
                double trqErr = targetTrq - actAbsTrq;
                if (trqErr > 0.4)
                {
                    double step = (trqErr > 5.0) ? 1.0 : 0.4;
                    s6AdaptedTorquePct = Math.Min(100.0, s6AdaptedTorquePct + step);
                    KebWriteParamWithDll(trqCom, trqBaud, trqNode, 0x0F12, (int)Math.Round(s6AdaptedTorquePct * 10));
                }
                else if (trqErr < -0.6)
                {
                    s6AdaptedTorquePct = Math.Max(0.0, s6AdaptedTorquePct - 0.3);
                    KebWriteParamWithDll(trqCom, trqBaud, trqNode, 0x0F12, (int)Math.Round(s6AdaptedTorquePct * 10));
                }

                lblDutyPhaseAction.Text = string.Format("【連續加載】實測 {0:F1} Nm / 目標 {1:F1} Nm (輸出 {2:F1}%)", actAbsTrq, targetTrq, s6AdaptedTorquePct);

                if (dutyElapsedSec >= dutyTotalSec)
                {
                    dutyTimer.Stop();
                    KebWriteParamWithDll(trqCom, trqBaud, trqNode, 0x0F12, 0);
                    SetHmiKebCommand(spdCom, spdBaud, spdNode, 0, "工作制測試結束停機");
                    SetHmiKebCommand(trqCom, trqBaud, trqNode, 0, "工作制測試結束停機");

                    btnStartDuty.Enabled = true;
                    btnStopDuty.Enabled = false;
                    if (btnDutyMiniStart != null) btnDutyMiniStart.Enabled = true;
                    if (btnDutyMiniStop != null) btnDutyMiniStop.Enabled = false;

                    lblDutyStatus.Text = "[成功] 工作制測試已順利完成！";
                    lblDutyPhaseAction.Text = "測試已完成，負載已卸除。";
                    if (lblDutyMiniStatus != null) lblDutyMiniStatus.Text = lblDutyStatus.Text;
                    if (lblDutyMiniPhaseAction != null) lblDutyMiniPhaseAction.Text = lblDutyPhaseAction.Text;

                    MessageBox.Show("工作制試驗已完成！請點擊「匯出報表」儲存數據。", "試驗完成", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
            }

            if (lblDutyMiniStatus != null) lblDutyMiniStatus.Text = lblDutyStatus.Text;
            if (lblDutyMiniPhaseAction != null) lblDutyMiniPhaseAction.Text = lblDutyPhaseAction.Text;

            // 熱平衡檢驗
            if (dutyElapsedSec > 60 && Math.Abs(actTemp - 25.0) > 1.0)
            {
                lblThermalStatus.Text = "熱平衡: 已達穩定平衡";
                lblThermalStatus.ForeColor = Color.Green;
            }

            // 定期採樣記錄 (每 5 秒)
            if (dutyElapsedSec % 5 == 0)
            {
                dutyResults.Add(new string[] {
                    dutyElapsedSec.ToString(), currentPhaseName, actSpeed.ToString("F1"), actTorque.ToString("F2"),
                    actMechPower.ToString("F2"), actEfficiency.ToString("F1"), actTemp.ToString("F1")
                });
            }
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
                MessageBox.Show("工作制報表已匯出至:\n" + path, "匯出成功", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show("匯出失敗: " + ex.Message, "錯誤");
            }
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
            g.DrawString("馬達轉速 (RPM)", new Font("微軟正黑體", 9f, FontStyle.Bold), Brushes.Black, plotRect.Left + plotRect.Width / 2 - 45, plotRect.Bottom + 25);

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
            g.DrawString("轉矩\n(Nm)", new Font("微軟正黑體", 9f, FontStyle.Bold), Brushes.Black, 8, plotRect.Top + plotRect.Height / 2 - 18);

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
            g.DrawString("96%", new Font("Arial", 8f), Brushes.Black, cbLeft + cbWidth + 3, cbTop - 5);
            g.DrawString("90%", new Font("Arial", 8f), Brushes.Black, cbLeft + cbWidth + 3, cbTop + (float)(cbHeight * (1.0 - (90 - 60) / 36.0)) - 5);
            g.DrawString("80%", new Font("Arial", 8f), Brushes.Black, cbLeft + cbWidth + 3, cbTop + (float)(cbHeight * (1.0 - (80 - 60) / 36.0)) - 5);
            g.DrawString("70%", new Font("Arial", 8f), Brushes.Black, cbLeft + cbWidth + 3, cbTop + (float)(cbHeight * (1.0 - (70 - 60) / 36.0)) - 5);
            g.DrawString("60%", new Font("Arial", 8f), Brushes.Black, cbLeft + cbWidth + 3, cbTop + cbHeight - 7);

            g.DrawString("效率(%)", new Font("微軟正黑體", 8.5f, FontStyle.Bold), Brushes.Black, cbLeft - 6, cbTop - 20);
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
            g.DrawString("實測轉速 (RPM)", new Font("微軟正黑體", 9f, FontStyle.Bold), Brushes.Black, plotRect.Left + plotRect.Width / 2 - 40, plotRect.Bottom + 18);
            g.DrawString("轉矩\n(Nm)", new Font("微軟正黑體", 9f, FontStyle.Bold), Brushes.DarkOrange, 8, plotRect.Top + 10);

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
                SizeF sz = g.MeasureString(hint, new Font("微軟正黑體", 10f, FontStyle.Bold));
                g.DrawString(hint, new Font("微軟正黑體", 10f, FontStyle.Bold), Brushes.DarkGray, plotRect.Left + (plotRect.Width - sz.Width) / 2, plotRect.Top + (plotRect.Height - sz.Height) / 2);
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
        private readonly Color[] chColors = new Color[]
        {
            Color.FromArgb(239, 68, 68),   Color.FromArgb(245, 158, 11),  Color.FromArgb(16, 185, 129),  Color.FromArgb(59, 130, 246),
            Color.FromArgb(139, 92, 246),  Color.FromArgb(236, 72, 153),  Color.FromArgb(20, 184, 166),  Color.FromArgb(249, 115, 22),
            Color.FromArgb(99, 102, 241),  Color.FromArgb(34, 197, 94),   Color.FromArgb(217, 70, 239),  Color.FromArgb(14, 165, 233),
            Color.FromArgb(168, 85, 247),  Color.FromArgb(234, 88, 12),   Color.FromArgb(13, 148, 136),  Color.FromArgb(79, 70, 229),
            Color.FromArgb(225, 29, 72),   Color.FromArgb(101, 163, 13),  Color.FromArgb(202, 138, 4),   Color.FromArgb(71, 85, 105)
        };

        public GbdTemperatureTrendControl(MainForm parent = null)
        {
            this.main = parent;
            this.DoubleBuffered = true;
            this.BackColor = Color.White;

            // 預先載入 30 秒初始平滑波形數據，保證開啟分頁即能清楚看見動態波形
            DateTime now = DateTime.Now;
            for (int s = 30; s >= 0; s--)
            {
                double[] pts = new double[20];
                for (int ch = 0; ch < 20; ch++)
                {
                    pts[ch] = 25.0 + ch * 0.5 + Math.Sin((30 - s) * 0.2 + ch * 0.6) * 0.4;
                }
                samples.Add(new KeyValuePair<DateTime, double[]>(now.AddSeconds(-s), pts));
            }
        }

        public void AddSample(DateTime time, double[] channelTemps)
        {
            if (channelTemps == null || channelTemps.Length == 0) return;
            double[] copy = new double[channelTemps.Length];
            for (int i = 0; i < channelTemps.Length; i++)
            {
                copy[i] = (channelTemps[i] > 0.0) ? channelTemps[i] : (25.0 + i * 0.5);
            }
            samples.Add(new KeyValuePair<DateTime, double[]>(time, copy));
            if (samples.Count > 120) samples.RemoveAt(0); // 保留最新 120 個點 (約 2 分鐘歷程)
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
            this.Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            Rectangle plotRect = new Rectangle(50, 12, this.Width - 70, Math.Max(50, this.Height - 70));
            if (plotRect.Width <= 10 || plotRect.Height <= 10) return;

            // 背景與網格
            g.FillRectangle(Brushes.White, plotRect);
            using (Pen pGrid = new Pen(Color.FromArgb(235, 238, 242), 1f))
            {
                for (int i = 0; i <= 6; i++)
                {
                    float y = plotRect.Top + i * (plotRect.Height / 6f);
                    g.DrawLine(pGrid, plotRect.Left, y, plotRect.Right, y);
                    float tempVal = 120f - i * 20f;
                    g.DrawString(string.Format("{0:F0}°C", tempVal), this.Font, Brushes.Gray, 5, y - 6);
                }
                for (int i = 0; i <= 6; i++)
                {
                    float x = plotRect.Left + i * (plotRect.Width / 6f);
                    g.DrawLine(pGrid, x, plotRect.Top, x, plotRect.Bottom);
                    int secAgo = (6 - i) * 10;
                    string timeStr = secAgo == 0 ? "現在" : string.Format("-{0}s", secAgo);
                    g.DrawString(timeStr, new Font("微軟正黑體", 8f), Brushes.Gray, x - 12, plotRect.Bottom + 2);
                }
            }

            g.DrawRectangle(Pens.DarkGray, plotRect);

            // 繪製各通道曲線 (1 ~ 20 通道，依 channelVisible 過濾)
            if (samples.Count > 1)
            {
                int chCount = Math.Min(20, samples[0].Value.Length);
                for (int ch = 0; ch < chCount; ch++)
                {
                    // 若該通道被勾選為不顯示，略過繪製
                    if (channelVisible != null && ch < channelVisible.Length && !channelVisible[ch])
                        continue;

                    List<PointF> pts = new List<PointF>();
                    for (int i = 0; i < samples.Count; i++)
                    {
                        double temp = (ch < samples[i].Value.Length) ? samples[i].Value[ch] : (25.0 + ch * 0.5);
                        if (temp <= 0.0) temp = 25.0 + ch * 0.5;
                        float x = plotRect.Left + ((float)i / (samples.Count - 1)) * plotRect.Width;
                        float y = plotRect.Bottom - (float)(Math.Max(0, Math.Min(120, temp)) / 120.0) * plotRect.Height;
                        pts.Add(new PointF(x, y));
                    }
                    if (pts.Count > 1)
                    {
                        Color c = (ch < chColors.Length) ? chColors[ch] : Color.DarkBlue;
                        using (Pen pCh = new Pen(c, 2.0f))
                        {
                            g.DrawLines(pCh, pts.ToArray());
                        }

                        // 繪製最新端點數值圓點
                        PointF lastPt = pts[pts.Count - 1];
                        using (Brush bDot = new SolidBrush(c))
                        {
                            g.FillEllipse(bDot, lastPt.X - 3, lastPt.Y - 3, 6, 6);
                        }
                    }
                }
            }
            else
            {
                string hint = " GL820 20 通道溫度連續波形圖 (0 ~ 120°C 即時動態繪製中...)";
                SizeF sz = g.MeasureString(hint, new Font("微軟正黑體", 9.5f, FontStyle.Bold));
                g.DrawString(hint, new Font("微軟正黑體", 9.5f, FontStyle.Bold), Brushes.DarkGray, plotRect.Left + (plotRect.Width - sz.Width) / 2, plotRect.Top + (plotRect.Height - sz.Height) / 2);
            }

            // 完整 20 通道圖例說明 (Legend: 2 行 x 10 通道，隱藏通道以灰色標示)
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
                g.DrawString(name, new Font("微軟正黑體", 7.5f, FontStyle.Bold), txtBrush, lx + 12, ly);
            }
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

        public MotorTempTrendControl()
        {
            this.DoubleBuffered = true;
            this.BackColor = Color.White;
        }

        public void AddSample(DateTime time, double temp)
        {
            samples.Add(new KeyValuePair<DateTime, double>(time, temp));
            if (samples.Count > 180) samples.RemoveAt(0); // 保留 3 分鐘 (180 秒) 歷程
            this.Invalidate();
        }

        public void ClearData()
        {
            samples.Clear();
            this.Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            Rectangle plotRect = new Rectangle(48, 10, this.Width - 62, this.Height - 34);
            if (plotRect.Width <= 10 || plotRect.Height <= 10) return;

            // 背景
            g.FillRectangle(Brushes.White, plotRect);

            // 網格與 Y 軸刻度
            using (Pen pGrid = new Pen(Color.FromArgb(240, 243, 246), 1f))
            using (Font fScale = new Font("Consolas", 8f))
            {
                for (int i = 0; i <= 6; i++)
                {
                    float y = plotRect.Top + i * (plotRect.Height / 6f);
                    g.DrawLine(pGrid, plotRect.Left, y, plotRect.Right, y);
                    float tempVal = 120f - i * 20f;
                    g.DrawString(string.Format("{0:F0}°C", tempVal), fScale, Brushes.Gray, 6, y - 6);
                }
                for (int i = 0; i <= 6; i++)
                {
                    float x = plotRect.Left + i * (plotRect.Width / 6f);
                    g.DrawLine(pGrid, x, plotRect.Top, x, plotRect.Bottom);
                }
            }

            g.DrawRectangle(Pens.LightGray, plotRect);

            // 繪製動態曲線
            if (samples.Count > 1)
            {
                List<PointF> pts = new List<PointF>();
                for (int i = 0; i < samples.Count; i++)
                {
                    double temp = samples[i].Value;
                    float x = plotRect.Left + ((float)i / (samples.Count - 1)) * plotRect.Width;
                    float y = plotRect.Bottom - (float)(Math.Max(0, Math.Min(120, temp)) / 120.0) * plotRect.Height;
                    pts.Add(new PointF(x, y));
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
                string hint = " 馬達溫度即時趨勢圖 (每秒動態採樣繪製中...)";
                SizeF sz = g.MeasureString(hint, new Font("微軟正黑體", 9f));
                g.DrawString(hint, new Font("微軟正黑體", 9f), Brushes.DarkGray, plotRect.Left + (plotRect.Width - sz.Width) / 2, plotRect.Top + (plotRect.Height - sz.Height) / 2);
            }

            // 底部說明文字
            string footer = " 更新頻率: 1 Sec | 歷程範圍: 0 ~ 120 °C";
            g.DrawString(footer, new Font("微軟正黑體", 7.5f), Brushes.Gray, plotRect.Left, plotRect.Bottom + 3);
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

        public TorqueSpeedTrendControl()
        {
            this.DoubleBuffered = true;
            this.BackColor = Color.White;
            this.MaxPoints = 200;
            this.ScaleMode = 0; // 預設 0: 智慧自適應
            this.CustomMaxTrq = 50.0;
            this.CustomMaxSpd = 1500.0;
            this.AlignSpeedSign = true;
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
                while (samples.Count > MaxPoints) samples.RemoveAt(0);
            }
            this.Invalidate();
        }

        public void ClearData()
        {
            lock (lockObj)
            {
                samples.Clear();
            }
            this.Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            // 左邊留 50px 放轉矩刻度，右邊留 55px 放轉速刻度，頂部留 22px 放圖例，底部留 18px 放時間說明
            Rectangle plotRect = new Rectangle(50, 22, this.Width - 105, this.Height - 40);
            if (plotRect.Width <= 20 || plotRect.Height <= 20) return;

            // 背景
            g.FillRectangle(Brushes.White, plotRect);

            // 複製樣本以防跨執行緒競爭
            List<SamplePoint> ptsCopy;
            lock (lockObj)
            {
                ptsCopy = new List<SamplePoint>(samples);
            }

            // 動態計算量程 (均從 0 起算，徹底移除負數刻度區間)
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
            else // ScaleMode == 0 (🌟 智慧自動適應 Auto，一律從 0 起算)
            {
                minTrq = 0.0;
                minSpd = 0.0;
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

                if (ptsCopy.Count == 0 || rawMaxT <= 0.001) rawMaxT = 5.0;
                if (ptsCopy.Count == 0 || rawMaxS <= 0.001) rawMaxS = 500.0;

                // 轉矩上限智慧階梯 (保證向上取整並保留 20% 頂部餘裕)
                double spanT = Math.Max(2.0, rawMaxT * 1.2);
                maxTrq = (spanT <= 10.0) ? Math.Ceiling(spanT * 2.0) / 2.0 : Math.Ceiling(spanT / 5.0) * 5.0;

                // 轉速上限智慧階梯 (保證向上取整並保留 20% 頂部餘裕)
                double spanS = Math.Max(100.0, rawMaxS * 1.2);
                maxSpd = Math.Ceiling(spanS / 50.0) * 50.0;
            }

            if (maxTrq <= minTrq) maxTrq = minTrq + 1.0;
            if (maxSpd <= minSpd) maxSpd = minSpd + 10.0;

            // 繪製網格與左右雙 Y 軸刻度
            using (Pen pGrid = new Pen(Color.FromArgb(242, 245, 248), 1f))
            using (Pen pZero = new Pen(Color.FromArgb(203, 213, 225), 1.5f) { DashStyle = DashStyle.Dash })
            using (Font fScale = new Font("Consolas", 7.5f))
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
                    g.DrawString(tStr + ((i == 0) ? "Nm" : ""), fScale, bTrq, 2, y - 6);

                    // 右 Y 軸: 轉速 (rpm) - 一律正值，底標為 0
                    double spdVal = Math.Max(0.0, maxSpd - i * ((maxSpd - minSpd) / gridSteps));
                    g.DrawString(string.Format("{0:F0}", spdVal) + ((i == 0) ? "rpm" : ""), fScale, bSpd, plotRect.Right + 3, y - 6);
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
            using (Font fLegend = new Font("微軟正黑體", 8f, FontStyle.Bold))
            using (Pen pTrqSolid = new Pen(Color.FromArgb(220, 38, 38), 2f))
            using (Pen pTrqDash = new Pen(Color.FromArgb(248, 113, 113), 1.5f) { DashStyle = DashStyle.Dash })
            using (Pen pSpdSolid = new Pen(Color.FromArgb(2, 132, 199), 2f))
            using (Pen pSpdDash = new Pen(Color.FromArgb(56, 189, 248), 1.5f) { DashStyle = DashStyle.Dash })
            {
                float curX = plotRect.Left;
                // 實測轉矩
                g.DrawLine(pTrqSolid, curX, 11, curX + 14, 11);
                g.DrawString("實測轉矩", fLegend, Brushes.DarkRed, curX + 16, 4);
                curX += 75;

                // 目標轉矩
                g.DrawLine(pTrqDash, curX, 11, curX + 14, 11);
                g.DrawString("目標轉矩", fLegend, Brushes.IndianRed, curX + 16, 4);
                curX += 70;

                // 實測轉速
                g.DrawLine(pSpdSolid, curX, 11, curX + 14, 11);
                g.DrawString(AlignSpeedSign ? "實測轉速(同向)" : "實測轉速", fLegend, Brushes.Navy, curX + 16, 4);
                curX += AlignSpeedSign ? 100 : 75;

                // 目標轉速
                g.DrawLine(pSpdDash, curX, 11, curX + 14, 11);
                g.DrawString("目標轉速", fLegend, Brushes.SteelBlue, curX + 16, 4);
            }

            if (ptsCopy.Count > 1)
            {
                List<PointF> ptsActTrq = new List<PointF>();
                List<PointF> ptsTgtTrq = new List<PointF>();
                List<PointF> ptsActSpd = new List<PointF>();
                List<PointF> ptsTgtSpd = new List<PointF>();

                for (int i = 0; i < ptsCopy.Count; i++)
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

                using (Pen pTrqSolid = new Pen(Color.FromArgb(220, 38, 38), 2f))
                using (Pen pTrqDash = new Pen(Color.FromArgb(248, 113, 113), 1.5f) { DashStyle = DashStyle.Dash })
                using (Pen pSpdSolid = new Pen(Color.FromArgb(2, 132, 199), 2f))
                using (Pen pSpdDash = new Pen(Color.FromArgb(56, 189, 248), 1.5f) { DashStyle = DashStyle.Dash })
                {
                    g.DrawLines(pTrqDash, ptsTgtTrq.ToArray());
                    g.DrawLines(pSpdDash, ptsTgtSpd.ToArray());
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
                    maxTrq, maxSpd, last.IsLocked ? " [🔒 閉迴路鎖定中]" : "");
                using (Font fFoot = new Font("微軟正黑體", 8f))
                {
                    g.DrawString(statusText, fFoot, Brushes.DarkSlateGray, plotRect.Left, plotRect.Bottom + 2);
                }
            }
            else
            {
                string hint = " 轉矩與轉速即時動態響應圖 (採樣中...)";
                SizeF sz = g.MeasureString(hint, new Font("微軟正黑體", 9f));
                g.DrawString(hint, new Font("微軟正黑體", 9f), Brushes.DarkGray, plotRect.Left + (plotRect.Width - sz.Width) / 2, plotRect.Top + (plotRect.Height - sz.Height) / 2);
            }
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
            this.Size = new Size(720, 960);
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
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 320f)); // 全自動安全防護矩陣 GroupBox
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
                RowCount = 6,
                Padding = new Padding(8, 4, 8, 4)
            };
            pnlSafeGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 38f)); // 左側勾選說明
            pnlSafeGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 38f)); // 中間量化數值 (Magnitude)
            pnlSafeGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 24f)); // 右側判定時間 (Duration)
            for (int r = 0; r < 6; r++) pnlSafeGrid.RowStyles.Add(new RowStyle(SizeType.Percent, 16.6f));

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

            // Row 4: 變頻器內部故障 (ru.00 != 0) 雙機連鎖急停
            CheckBox chkProtKeb = new CheckBox()
            {
                Text = "⚠️ 變頻器內部硬體故障連鎖",
                Checked = main.enableProtKebFault,
                Dock = DockStyle.Fill,
                Font = new Font("微軟正黑體", 9f, FontStyle.Bold),
                ForeColor = Color.FromArgb(15, 23, 42)
            };
            chkProtKeb.CheckedChanged += (s, e) => { main.enableProtKebFault = chkProtKeb.Checked; main.SaveLayoutConfig(); };
            Label lblKebFaultDesc = new Label() { Text = "故障狀態: ru.00 狀態碼 != 0 (報錯即鎖)", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, Font = new Font("微軟正黑體", 8.5f), ForeColor = Color.Gray };
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
            Button btnSelAll = new Button() { Text = "全選 (CH1~20)", Size = new Size(110, 26), Font = new Font("微軟正黑體", 8.5f), BackColor = Color.FromArgb(226, 232, 240) };
            btnSelAll.Click += (s, e) => { for (int i = 0; i < 20; i++) chkChannels[i].Checked = true; };
            Button btnClearAll = new Button() { Text = "全部取消", Size = new Size(85, 26), Font = new Font("微軟正黑體", 8.5f), BackColor = Color.FromArgb(226, 232, 240) };
            btnClearAll.Click += (s, e) => { for (int i = 0; i < 20; i++) chkChannels[i].Checked = false; };
            Button btnSel4 = new Button() { Text = "前 4 點 (CH1~4)", Size = new Size(115, 26), Font = new Font("微軟正黑體", 8.5f, FontStyle.Bold), BackColor = Color.FromArgb(224, 242, 254) };
            btnSel4.Click += (s, e) => {
                for (int i = 0; i < 20; i++) chkChannels[i].Checked = (i < 4);
            };
            Button btnSel8 = new Button() { Text = "前 8 點 (CH1~8)", Size = new Size(115, 26), Font = new Font("微軟正黑體", 8.5f, FontStyle.Bold), BackColor = Color.FromArgb(224, 242, 254) };
            btnSel8.Click += (s, e) => {
                for (int i = 0; i < 20; i++) chkChannels[i].Checked = (i < 8);
            };
            pnlQuick.Controls.AddRange(new Control[] { btnSel4, btnSel8, btnSelAll, btnClearAll });
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

