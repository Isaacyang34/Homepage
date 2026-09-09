# 動力計硬體通訊獨立測試指南 (Hardware Communication Testing Guide)

本指南提供各獨立設備通訊測試工具的使用說明，確保在正式執行主控測試前，各硬體連線、通訊協定與數值皆正常。

---

## 🛠️ 測試工具清單

| 設備項目 | 測試工具 (免安裝 C# 原生版) | 測試工具 (Python 版) | 支援協定 / 介面 |
| :--- | :--- | :--- | :--- |
| **🌐 功率分析儀** | `Test_PowerMeter_Ethernet.exe` | `test_power_meter.py` | 乙太網路 TCP/IP (Socket) / GPIB (PyVISA) |
| **🔧 USB 專用扭力計** | `Test_USB_TorqueMeter.exe` | `test_torque_meter.py` | USB 虛擬串列埠 (COM Port) / 4503A ASCII |
| **📡 GBD 網路溫度記錄器** | `Test_Graphtec_GBD_Ethernet.exe` | `test_gbd_logger.py` | 乙太網路 TCP/IP (Port 8023) 多通道熱電偶 |
| **⚙️ KEB 驅動器** | - | `test_keb_drive.py` | DIN 66019 / ANSI RS-232 |
| **🌡️ KTY84 溫度換算** | - | `test_kty84_temp.py` | 0~10V 類比電壓換算驗證 |

---

### 一、 功率分析儀 (Power Meter) 網路連線測試

#### 方法 1：使用 C# 免安裝執行檔 (推薦)
直接在檔案總管雙擊執行：
👉 `tools/Test_PowerMeter_Ethernet.exe`
- 依照提示輸入儀表 IP（例如 `192.168.1.100`）與 TCP 連接埠（例如 `10001`）。
- 程式將自動發送 `*IDN?` 取得儀表型號，並以 `:NUMeric:NORMal:VALue?` 即時輪詢三相電壓、電流、總功率與功因。

#### 方法 2：使用 Python 腳本
```bash
python tools/test_power_meter.py --mode ethernet --ip 192.168.1.100 --port 10001
```

---

### 二、 USB 專用扭力計 (Torque Meter) 測試

#### 方法 1：使用 C# 免安裝執行檔 (推薦)
直接雙擊執行：
👉 `tools/Test_USB_TorqueMeter.exe`
- 程式會自動列出系統所有偵測到的 COM 埠。
- 輸入 COM 埠編號（如 `COM3`）與鮑率（預設 `9600`）。
- 程式將每 200ms 發送 `MEAS:TORQ?` 指令並顯示即時扭力數值 (Nm)。

#### 方法 2：使用 Python 腳本
```bash
python tools/test_torque_meter.py -p COM3 -b 9600 -s 1.55
```

---

### 三、 GBD 網路多通道溫度記錄器 (Graphtec GL 系列) 測試

#### 方法 1：使用 C# 免安裝執行檔 (推薦)
直接雙擊執行：
👉 `tools/Test_Graphtec_GBD_Ethernet.exe`
- 輸入 GBD 溫度記錄器 IP（預設 `192.168.1.101`）與 Port（預設 `8023`）。
- 程式將發送 `:MEAS:ALL?` 讀取並即時顯示 CH1~CH20 完整多測點溫度數據（定子、前後軸承、環境等）。

#### 方法 2：使用 Python 腳本
```bash
python tools/test_gbd_logger.py --ip 192.168.1.101 --port 8023
```

---

### 四、 KEB 變頻負載驅動器測試

```bash
python tools/test_keb_drive.py -p COM1 -b 9600 -a 1
```
- 自動讀取參數：`ru01` (設定轉速)、`ru07` (實際轉速)、`ru15` (電流 A)、`ru18` (DC Bus 電壓)、`Sy51` (狀態字元)。
