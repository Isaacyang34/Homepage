# Dynamometer Project Workflow & Iteration Rules

本規範為動力計（Dynamometer）專案的永久強制工作流程規則，每次迭代與修改均須嚴格遵守。

## 1. 核心原始碼結構 (Source Code)
* 主控台 HMI 原始碼：`Dyanmometer/Dyanmometer_Modern/Dynamometer_HMI_WinForms.cs`
* 現場四合一測試工具箱原始碼：`Dyanmometer/Dyanmometer_Modern/tools/Dynamometer_Device_Tester_GUI.cs`

## 2. 標準迭代 5 步流程 (Strict 5-Step Pipeline)
每次進行程式碼修改或功能調整時，必須依序執行以下步驟：

1. **原始碼修改**：
   * 精準修改 `Dynamometer_HMI_WinForms.cs` 與 `Dynamometer_Device_Tester_GUI.cs`。
2. **聯合編譯 (Build)**：
   * 執行原生 64-bit C# 編譯器：
     `csc.exe /target:winexe /out:Dynamometer_HMI_Pro.exe /r:System.Windows.Forms.DataVisualization.dll Dynamometer_HMI_WinForms.cs tools\Dynamometer_Device_Tester_GUI.cs`
   * 產出無外部相依的可執行檔 `Dynamometer_HMI_Pro.exe`。
3. **封裝發布至 Release**：
   * 執行 `package_release.ps1`。
   * 自動同步至 `Release\Dynamometer_HMI_V2.3.0_Portable\` 與 `Release\Dynamometer_HMI_V2.4.0_Portable\`。
   * 確保所有驅動 DLL (`tmctl.dll`, `USBTMCAPI.dll` 等)、設定檔 (`dynamometer_layout.ini`) 與無黑框啟動器 (`啟動系統.vbs`) 均就緒。
4. **自動化驗證 (Verification)**：
   * 執行 `generate_snapshot.ps1` 產生實際渲染截圖，確認 UI 排版正常、繁體中文無亂碼、按鈕元件無衝突。
5. **回報與測試指示**：
   * 向使用者清楚報告修改內容，並提示直接由 `Release\Dynamometer_HMI_V2.3.0_Portable\啟動系統.vbs` 進行實機測試。
