# Dynamometer 動力計軟體生成日誌與驗收報表修復規範書 (Dynamometer Core Fix Specification)

> **最高指導原則**：
> 報表問題**絕對不能在產出的 Excel 報告或規格書進行人工二手補丁**，必須從源頭——**Dynamometer 上位機核心源碼與日誌生成邏輯徹底根治修復**！
> 確保機台只要點擊「測試完成/匯出報表」，底層吐出的 CSV 原始日誌與自動填表數據即具備 100% 物理自洽性與 Excel 審查相容性。

---

## 1. 現象與致命根因分析 (Root Cause Matrix)
## 1. 使用者回報截圖與四大致命缺陷根因分析 (Root Cause Matrix)

| 編號 | 故障現象 (Symptom) | 影響報表項目 | 涉及源碼檔案與位置 | 致命根因 (Root Cause) |
| 編號 | 故障截圖與現象 (Symptom) | 影響報表項目與位置 | 涉及源碼檔案與邏輯 | 致命根因 (Root Cause) 與物理機制 |
|---|---|---|---|---|
| **#1** | **繞組電阻非實測，為反算填入 (瞎掰)** | 報告 Step 3、Step 13，規格書 `H83` | `Dynamometer_TestEquivCircuit.cs` (`ExportEquivCircuitReportCsv`)、產線冷態電阻量測環節 | **未讀取機台實際出廠檢驗標籤之實測數據**！現場膠帶標籤明確記載實測線電阻為 **`0.21 Ω, 0.21 Ω, 0.21 Ω`**（相電阻 $R_1 = 0.105\ \Omega$），但軟體卻在無微歐姆計實測通訊下，直接取堵轉等效電路反算值 `0.2100` 並當作線電阻填為 `0.42 Ω`，引發嚴重的物理量定義錯亂與反算造假。 |
| **#2** | **額定頻率誤記為 0.045 Hz** | 報告表頭 `R3`、規格書 `H62` | `Dynamometer_TestEquivCircuit.cs` (`ExportEquivCircuitReportCsv`) | 空載標頭輸出混淆：將堵轉極低頻掃頻條件 `F0` (0.045 Hz) 與電機額定同步頻率 $f_N$ 混淆，未輸出標準額定頻率 `fN=50.00 Hz`。 |
| **#3** | **轉速與功率帶負號** (例如 `-1500.82 rpm`, `-18.56 kW`) | 審查轉速判定 `Row 11, 77, 144` 全部變 **No** | `Dynamometer_TestDuty.cs` (`dutyResults.Add`), `Dynamometer_Telemetry.cs` | 動力計在負載吸收運轉時，轉矩感測器/編碼器方向為逆向，以帶負號數值 `actSpeed` 寫入日誌，未進行發電/負載方向標準化（`Math.Abs()`），觸發 `實測 - 目標 = -3000 rpm` 之審查巨幅誤差判退。 |
| **#4** | **S6 週期測試欄位錯位與缺項** (缺少三相電流分相與軸承溫) | 報告 Step 12 (`Row 58`) 電流平衡與溫升審查 | `Dynamometer_TestDuty.cs` (`SaveDutyTestReportCsv`) | `Report_Duty_S6_*.csv` 僅輸出整合值 `Current_Sigma_A`，未同步輸出分相電氣量 `I1, I2, I3` 與 GL820 軸承/編碼器各點多通道實測溫度，導致報表無法填入 Step 12 規範欄位。 |
| **#5** | **S2/S6 驗收目標轉矩與轉速遺漏** | 報告 Step 11/12 計算目標轉矩 `M45, N58` 歸零 | 報告範本表頭隱藏欄位 `Y3, Y5, AA3, AA5, AC3` | 範本未自動填入該電機額定之 150% S2 目標扭矩 ($1.5 \times T_N$) 與 200% S6 目標扭矩 ($2.0 \times T_N$)，使 Excel 後續過載判定公式無分子可算。 |
| **#6** | **Excel 2010 動態陣列公式相容性崩潰** | 審查頁面全部顯示 `#NAME?`，無法更新結果 | Excel 報表範本 (`自動審查` 工作表) | 範本內寫入了高版本 Excel 專用函數 `_xlfn.IFS` 與 `_xlfn.TEXTJOIN`。現役 XP / Win7 機台 Excel 2010 (14.0) 無原生支援，造成公式解析閉鎖。 |
| **#1** | **等效電路 R1 定子電阻審查判退 (No)**<br>*(未通過原因：R1與各項電阻平均值>3%)* | 測試報告 `自動審查` Row 177 / `電機廠報告與驗收規範` Step 3 (`L14:O15`) 與 Step 13 (`P70, R70`) | `Dynamometer_TestEquivCircuit.cs` (`ExportEquivCircuitReportCsv`)、微歐姆計實測通訊模組 | **相電阻與線電阻定義混淆 + 缺乏實測閉環**：<br>1. 電機接線為 **Y 接**，等效電路之定子電阻 $R_1$ 為**相電阻** ($R_{\text{phase}}$)。<br>2. 出廠檢驗實測三相冷態線電阻為 $R_{uv}=0.21, R_{vw}=0.21, R_{uw}=0.21\ \Omega$，平均線電阻 $R_{\text{line\_avg}} = 0.21\ \Omega$。<br>3. 物理公式：$R_1 = \frac{R_{\text{line\_avg}}}{2} = \mathbf{0.105\ \Omega}$。<br>4. 審查判定公式：`ABS((F7/2 - F177)/F177) <= 3%`，其中 `F7` 是 Step 3 實測線電阻平均值 ($R_{\text{line\_avg}}$)，`F177` 是 Step 13 填入的 $R_1$。軟體若未按出廠檢驗標籤或微歐姆計實測值填寫，或將線電阻誤填為相電阻，即觸發審查判退。 |
| **#2** | **100% 額定轉速平均電壓誤差顯示 26.923% (No)**<br>*(未通過原因：平均電壓誤差>5%)* | 測試報告 `自動審查` Row 33 / `電機廠報告與驗收規範` 表頭 `H3` 與 S1 實測 `Q34:S34` | `Dynamometer_TestEquivCircuit.cs`、`Dynamometer_ReportManager.cs` | **額定電壓 H3 寫死為 260V 降壓設定值**：<br>1. 審查公式為 `H33 = (AVERAGE(E33:G33) - H3) / H3`。<br>2. 現場實測線電壓為 $327.4\text{V} \sim 330.0\text{V}$。<br>3. 若上位機將表頭額定電壓 `H3` 填入驅動器參數原始預設值或安全低壓 **260V**（例如 `uf09=260`），則誤差算出：<br>$$\frac{330 - 260}{260} = \mathbf{26.923\%}$$<br>4. 該電機額定電壓 $V_N$ 實為 **330V**，表頭 `H3` 必須依馬達規格書與驅動器實際運行額定寫入 `330`，誤差即回歸 $0.00\%$ (Yes)。 |
| **#3** | **項次 15 激磁電流測試結果空白，顯示 -100% (No)** | 測試報告 `電機廠報告與驗收規範` `L81` (Step 15) | `Dynamometer_TestEquivCircuit.cs`、`Dynamometer_ReportManager.cs` | **只填表頭基準 N5，漏填 Step 15 測試結果 L81**：<br>1. 空載實驗額定電壓 327.7V 實測激磁電流 $I_0 = 16.13\text{ Arms}$。<br>2. 上位機產表時**僅將數值填入表頭 N5，卻漏填了 Step 15 的測試結果 L81**！<br>3. Excel 公式為 `N81 = (L81 - K81) / K81`（$K81 = N5 = 16.13$），因 $L81$ 為空 (0)，直接算出 **`-100%`**！ |
| **#4** | **冷卻液種類與物性參數未填**<br>*(水冷密度、比熱、黏度空白，散熱功率變 #VALUE!)* | 測試報告 `馬達溫升實驗表格` `B7:E7` | `Dynamometer_TestDuty.cs`、`Dynamometer_ReportManager.cs` | **軟體未將測試介面勾選之冷卻模式連動匯入報表**：<br>1. 動力計介面已有冷卻選擇並輸出 `# CoolingType: Water`。<br>2. 但產表模組漏寫了 `馬達溫升實驗表格` Row 7 的物性常數，導致後續冷卻能力 $kW$ 公式 `O15:O17` 缺乘數產生 `#VALUE!`。 |

---

## 2. 實測物理依據 (Verbatim Factory Test Record)

現場出廠紙箱與機殼檢驗膠帶標籤（影像檔 `S__93667333.jpg`）之**真實出廠檢驗實測物理量**如下：

1. **繞組冷態線電阻 (Step 3)**：
   * 標籤文字：`電阻: 0.21, 0.21, 0.21 (Ω)`
   * 三相平衡：$R_{uv} = 0.21\ \Omega, R_{vw} = 0.21\ \Omega, R_{uw} = 0.21\ \Omega$，平均值 $R_{\text{avg}} = 0.21\ \Omega$。
   * 對應等效電路定子相電阻：$R_1 = \frac{R_{\text{avg}}}{2} = \frac{0.21}{2} = \mathbf{0.105\ \Omega}$。
2. **繞組電感**：
2. **冷卻液種類與物性參數 (馬達溫升實驗表格 Row 7)**：
   * 測試條件：**水冷 (Water Cooled)**。
   * 密度：**0.98** $\text{g/cm}^3$。
   * 比熱：**4.184** $\text{J/g}\cdot\text{K}$。
   * 黏度：**0.893** $\text{mm}^2/\text{s}$。
3. **激磁電流實測 (Step 15, Row 81)**：
   * 空載實驗額定電壓 327.7V 實測激磁電流：$I_0 = \mathbf{16.13\ Arms}$。
4. **繞組電感**：
   * 標籤文字：`電感: 3.19, 3.19, 3.24 (mH)`
3. **繞組絕緣阻抗 (Step 4)**：
5. **繞組絕緣阻抗 (Step 4)**：
   * 標籤文字：`絕緣 > 6000 (MΩ)` (測試電壓 DC500V, 60s)
4. **繞組耐壓漏電流 (Step 5)**：
6. **繞組耐壓漏電流 (Step 5)**：
   * 標籤文字：`耐壓: 13.0 (mA)` (測試電壓 AC1800V, 60s)
5. **溫度感測器 (KTY84)**：
7. **溫度感測器 (KTY84)**：
   * 標籤文字：`KTY84: 0.59 (kΩ)`

---

## 3. Dynamometer 核心源碼修復規範 (Implementation Specs)

### 修復項目 1：微歐姆計實測輸入或標籤輸入界面與 R1 相電阻換算
* **檔案**：`Dyanmometer/Dyanmometer_Modern/Dynamometer_TestEquivCircuit.cs` 與 UI 設定模組
* **問題根因**：等效電路界面直接將 $R_1$ 硬填為 0.21，且無冷態三相線電阻實測輸入欄位。
### 修復項目 1：溫升報表自動注入冷卻液種類與物理常數
* **檔案**：`Dyanmometer/Dyanmometer_Modern/Dynamometer_TestDuty.cs` 與報表生成模組
* **修改規範**：
  1. 在測試設定或等效電路面板新增 **「Step 3 冷態三相線電阻量測值」** 輸入或儀器連線讀取（$R_{uv}, R_{vw}, R_{uw}$）。
  2. 定義換算閉環：
     * **線電阻實測平均值**：$R_{\text{line\_avg}} = \frac{R_{uv} + R_{vw} + R_{uw}}{3}$（實測為 $0.21\ \Omega$）。
     * **定子等效相電阻 $R_1$**：$R_1 = \frac{R_{\text{line\_avg}}}{2} = \mathbf{0.105\ \Omega}$。
  3. 當使用者啟用 `AutoR1Distribute` 或手動設定時，上位機自動將 $R_1$ 帶入 $0.105\ \Omega$，並將 $R_{uv}=0.21, R_{vw}=0.21, R_{uw}=0.21$ 寫入報表 Step 3，徹底終結反算瞎猜！
  上位機在匯出溫升試驗工作表時，依冷卻模式自動填入物性參數：
  ```csharp
  // 自動判斷冷卻條件並填寫馬達溫升實驗表格物性常數
  bool isWater = HasWaterCoolingChannels(); // 依據 WIN/WOUT 通道判斷或使用者選單
  if (isWater)
  {
      wsTemp.Cells["B7"].Value = "水";
      wsTemp.Cells["C7"].Value = 0.98;   // 密度 (g/cm3)
      wsTemp.Cells["D7"].Value = 4.184;  // 比熱 (J/gK)
      wsTemp.Cells["E7"].Value = 0.893;  // 黏度 (mm2/s)
  }
  else
  {
      wsTemp.Cells["B7"].Value = "空冷";
      // 空冷時免填水冷物性，後續流量欄位空白
  }
  ```

### 修復項目 2：等效電路與額定標頭頻率分離修復
* **檔案**：`Dyanmometer/Dyanmometer_Modern/Dynamometer_TestEquivCircuit.cs`
* **函式**：`ExportEquivCircuitReportCsv`
### 修復項目 2：項次 15 激磁電流自動映射賦值
* **檔案**：`Dyanmometer/Dyanmometer_Modern/Dynamometer_TestEquivCircuit.cs` 與產表模組
* **修改規範**：
  在 CSV 標頭 `# --- 輸入量測基準條件 ---` 區塊，清楚分離 **「額定電氣頻率 fN」** 與 **「量測掃頻頻率 F0」**：
  在產表填寫 Step 15（`Row 81`）時，嚴格將空載實測 $I_0$ 同步寫入測試結果 `L81`：
  ```csharp
  double ratedFreq = (numMotorRatedSpeed != null && numMotorRatedSpeed.Value > 0)
      ? Math.Round((double)numMotorRatedSpeed.Value * 4.0 / 120.0, 2) // 4極 1500rpm -> 50.00Hz
      : 50.0;
  
  sbAuto.AppendLine(string.Format("# Rated:  VN={0:F1} V, IN={1:F2} A, PN={2:F3} kW, TN={3:F2} Nm, NN={4:F0} rpm, fN={5:F2} Hz, sN={6:F2} %, PFN={7:F3}", 
      numEquivVn.Value, numEquivIn.Value, numEquivPn.Value, numEquivTn.Value, numEquivNn.Value, ratedFreq, numEquivSlip.Value, numEquivPfn.Value));
  wsMain.Cells["L81"].Value = Math.Round(noLoadI0, 2); // 填入 16.13 Arms
  ```

### 修復項目 3：工作制日誌 (S1/S2/S6) 轉速與機械功率正向化
### 修復項目 3：微歐姆計實測輸入與相電阻換算閉環
* **檔案**：`Dyanmometer/Dyanmometer_Modern/Dynamometer_TestEquivCircuit.cs` 與 UI 設定模組
* **修改規範**：
  1. 新增出廠標籤或微歐姆計實測三相線電阻輸入（$R_{uv}=0.21, R_{vw}=0.21, R_{uw}=0.21$）。
  2. 線電阻平均值 $R_{\text{line\_avg}} = 0.21\ \Omega$，定子等效相電阻自動連動：$R_1 = \frac{R_{\text{line\_avg}}}{2} = \mathbf{0.105\ \Omega}$。
  3. Step 3 填入線電阻實測值 $0.21\ \Omega$，Step 13 與規格書填入相電阻 $0.105\ \Omega$。

### 修復項目 4：額定電壓 H3 規範化為 330V，杜絕 26.923% 誤差
* **檔案**：`Dyanmometer/Dyanmometer_Modern/Dynamometer_ReportManager.cs`
* **修改規範**：
  表頭額定電壓 `H3` 必須與受測馬達規格書額定電壓 $V_N$ 嚴格一致（330V），嚴禁使用驅動器內部 260V 降壓設定值，使 `(330 - 330) / 330 = 0.00%` 通過審查。

### 修復項目 5：額定頻率與空載掃頻頻率標頭分離
* **檔案**：`Dyanmometer/Dyanmometer_Modern/Dynamometer_TestEquivCircuit.cs`
* **修改規範**：
  在 CSV 標頭明確輸出 `fN=50.00 Hz`，與堵轉掃頻 `F0=0.045` 分流。

### 修復項目 6：工作制日誌轉速與功率絕對值正向化
* **檔案**：`Dyanmometer/Dyanmometer_Modern/Dynamometer_TestDuty.cs`
* **函式**：`DutyTimer_Tick`
* **修改規範**：
  採樣點寫入 `dutyResults.Add` 時，轉速與功率強制取絕對值：
  ```csharp
  double validSpeed = Math.Abs(actSpeed);
  double validTorque = Math.Abs(actTorque);
  double validMechPower = Math.Abs(actMechPower);
  double validEfficiency = Math.Abs(actEfficiency);
  ```
  `dutyResults.Add` 強制加入 `Math.Abs()`，輸出正向轉速與功率。

### 修復項目 4：工作制日誌擴充三相電氣量與各測點溫度
### 修復項目 7：擴充工作制 CSV 欄位至驗收規格結構
* **檔案**：`Dyanmometer/Dyanmometer_Modern/Dynamometer_TestDuty.cs`
* **函式**：`SaveDutyTestReportCsv`
* **修改規範**：
  S1/S2/S6 匯出 CSV 標頭與欄位擴充完整分相電氣量與溫度通道：
  ```csv
  StandardTime,ElapsedSec,Phase,Speed_rpm,Frequency_Hz,Torque_Nm,MechPower_kW,Efficiency_pct,Voltage_U1_V,Voltage_U2_V,Voltage_U3_V,Current_I1_A,Current_I2_A,Current_I3_A,ElecPower_kW,PowerFactor,T_Ambient_C,T_Coil_C,T_BearingF_C,T_BearingR_C,T_Encoder_C,T_WaterIn_C,T_WaterOut_C
  ```
  S1/S2/S6 匯出 CSV 同時包含 `Current_I1_A, Current_I2_A, Current_I3_A` 與所有軸承溫度通道。

### 修復項目 5：報表輸出自動注入 S2/S6 額定過載基準
### 修復項目 8：報表隱藏表頭目標轉矩注入
* **檔案**：`Dyanmometer/Dyanmometer_Modern/Dynamometer_ReportManager.cs`
* **修改規範**：
  在自動生成或填入 Excel 範本時，表頭隱藏設定欄位必須同動賦值：
  ```csharp
  ws.Cells["Y3"].Value = ratedSpeed;               // S2 目標轉速 1500 rpm
  ws.Cells["Y5"].Value = Math.Round(ratedTorque * 1.5, 1); // S2 目標扭矩 177.0 Nm (150%)
  ws.Cells["AA3"].Value = ratedSpeed;              // S6 目標轉速 1500 rpm
  ws.Cells["AA5"].Value = Math.Round(ratedTorque * 2.0, 1); // S6 目標扭矩 236.0 Nm (200%)
  ws.Cells["AC3"].Value = 15;                      // S6 負荷時間率 15% ED
  ```
  表頭隱藏儲存格自動寫入 $Y3, Y5, AA3, AA5, AC3, AE3, AE5$。

### 修復項目 6：報表審查公式全面向下相容 Excel 2010
### 修復項目 9：審查公式全面相容 Excel 2010
* **修改規範**：
  在產表範本中將所有 `_xlfn.IFS` 改為巢狀 `IF(...)`，`_xlfn.TEXTJOIN` 改為字串串接符號 `&`，保留原始 `自動審查` 頁面並增加相容之 `自動審查_修復版`。
  將 `_xlfn.IFS` 轉換為巢狀 `IF`，`_xlfn.TEXTJOIN` 轉換為 `&`。

---

## 4. 驗收核對指標 (Verification Checklist)

1. **電阻真實性**：Step 3 三相線電阻實測值為 $0.21\ \Omega, 0.21\ \Omega, 0.21\ \Omega$（對齊出廠膠帶標籤），絕緣阻抗 $>6000\text{ M}\Omega$，耐壓漏電流 $13.0\text{ mA}$。
2. **等效電路 R1 相電阻物理自洽**：$R_1$ 必須等於相電阻 $\frac{R_{\text{line\_avg}}}{2} = 0.105\ \Omega$（與 Step 3 線電阻實測值嚴格對應），審查 Row 177 誤差為 $0.00\% \le 3\%$，判定 **Yes**。
3. **零負號與頻率 50Hz**：轉速為正數 $1501\text{ rpm}$，額定頻率為 $50.00\text{ Hz}$。
1. **溫升表格冷卻常數**：`馬達溫升實驗表格` `B7:E7` 必須完整顯示 `水`、`0.98`、`4.184`、`0.893`，冷卻散熱功率 $O15:O17$ 正確計算出數值（如 2.2 kW），絕無 `#VALUE!` 錯誤。
2. **激磁電流誤差歸零**：項次 15 `L81` 填入 `16.13`，誤差 `N81` 顯示 **`0.00%`**。
3. **電阻真實性與自洽**：Step 3 線電阻實測值為 $0.21\ \Omega, 0.21\ \Omega, 0.21\ \Omega$；Step 13 相電阻為 $0.105\ \Omega$；審查判定全綠 **Yes**。
4. **電壓誤差全綠**：表頭 `H3 = 330`，S1 額定電壓誤差為 $0.00\%$，審查判定全綠 **Yes**。
