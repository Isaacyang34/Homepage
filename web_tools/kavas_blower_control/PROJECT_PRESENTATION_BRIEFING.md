# 📊 KAVAS 高速鼓風機 3 通道 ESP32 雲端監控與實體控制箱 - 專案簡報與簡報導播簡報檔

本文件為 **KAVAS 鼓風機控制專案** 之完整簡報/投影片簡報檔（Presentation Pitch Deck），格式可以直接用於成果呈報、投影片製作與工程歸檔。

---

## 📽️ Slide 1: 專案背景與執行摘要 (Executive Summary)

* **專案目標**：針對廠房 3 台高速鼓風機（$0 \sim 24,000\text{ RPM}$）建立實體數據採集控制箱與雲端即時監控儀表板。
* **技術架構**：
  - **端側 MCU**：ESP32 雙核 Wi-Fi 開發板 + Jumper 硬體跳線開關 (GPIO 15)
  - **感測與通訊**：3 塊獨立 MAX485 通訊模組 + DHT11 溫濕度傳感器 + 【選配】Micro-SD 讀卡模組
  - **智慧採樣與黑盒子**：1Hz 輪詢 + 60s 環形佇列 + 開機衝擊採樣 + 故障前後 60 秒黑盒子快照 (共 120 秒秒級數據)
  - **雲端管道**：Google Apps Script API + 1個月全量試算表歷史數據庫 + LINE Notify 秒級告警
* **線上儀表板**：[https://isaacyang34.github.io/Homepage/web_tools/kavas_blower_control/index.html](https://isaacyang34.github.io/Homepage/web_tools/kavas_blower_control/index.html)
* **3D 實體模型展示**：[https://isaacyang34.github.io/Homepage/web_tools/kavas_blower_control/3d_view.html](https://isaacyang34.github.io/Homepage/web_tools/kavas_blower_control/3d_view.html)

---

## ⚙️ Slide 2: 4 極馬達物理與弱磁控制模型 (Motor Physics & Field Weakening)

* **馬達極數**：4 極馬達 ($p = 2$ 極對數)
* **頻率轉速公式**：$N = \frac{120 \times f}{p} = 30 \times f$
* **基頻常轉矩區 ($0 \sim 5,000\text{ RPM}$)**：
  - 頻率 $0 \sim 166.7\text{ Hz}$，輸出電壓隨頻率成正比上升至 $380\text{ V}$（維護固定 $V/f$ 比例）。
* **弱磁高速區 ($5,000 \sim 24,000\text{ RPM}$)**：
  - 頻率 $166.7 \sim 800.0\text{ Hz}$，輸出電壓飽和封頂於 **$380\text{ V}$**，磁通隨頻率增加而衰減（恒功率區）。

---

## 🛠️ Slide 3: 實體控制箱硬體 BOM 採購清單 (廣華電子 shop.cpu.com.tw)

| 項次 | 類別 | 零件名稱與詳細規格 | 建議數量 | 廣華電子商城直達連結 | 預估價格 |
| :---: | :---: | :--- | :---: | :--- | :---: |
| **1** | 主控板 | ESP32 雙核 Wi-Fi 開發板 (ESP-WROOM-32) | 1 板 | [ESP32 開發板連結](https://shop.cpu.com.tw/Search/advanced/4553503332/page/1/) | $180~$350 |
| **2** | 電源 | 明緯 IRM-05-05 (AC 85~264V 轉 DC 5V 1A) | 1 個 | [IRM-05-05 電源連結](https://shop.cpu.com.tw/Search/advanced/49524d2d30352d3035/page/1/) | $120~$180 |
| **3** | 通訊 | ZY-MAX485 TTL 轉 RS485 模組 | **3 個** | [MAX485 模組連結](https://shop.cpu.com.tw/Search/advanced/4d4158343835/page/1/) | $30~$89 /個 |
| **4** | 感測 | DHT11 數字溫溼度傳感器模組 | 1 組 | [DHT11 傳感器連結](https://shop.cpu.com.tw/Search/advanced/4448543131/page/1/) | **$40~$70** |
| **5** | 外殼 | IP68/IP65 塑膠防水防塵工程萬用控制盒 | 1 盒 | [IP65防水盒分類頁](https://shop.cpu.com.tw/cPath/2112) | $150~$320 |
| **6** | 防水頭 | PG7 (1個AC電源進線) / PG9 (3個485線出線) | **4 個** | [PG防水接頭連結](https://shop.cpu.com.tw/Search/advanced/504737/page/1/) | $15~$30 /個 |
| **7** | 固定 | M3 單頭/雙頭 PCB 隔離銅柱 + 螺絲包 | 1 包 | [M3隔離銅柱連結](https://shop.cpu.com.tw/Search/advanced/4d33/page/1/) | $25~$50 |
| **8** | 安規 | AC 玻璃保險絲座 (附 1A 保險絲) | 1 座 | [保險絲座連結](https://shop.cpu.com.tw/Search/advanced/46757365/page/1/) | $20~$45 |
| **9** | 線材 | RS485 工業級雙絞屏蔽電纜線 (24AWG 雙隔離) | **15~20米** | [銅編織隔離線分類頁](https://shop.cpu.com.tw/cPath/453) | $15~$25 /米 |
| **10**| 終端 | 120 歐姆 (120Ω) 1/4W 精密終端匹配電阻 | 3 顆 | [碳膜電阻分類頁](https://shop.cpu.com.tw/cPath/2252) | $2~$5 /顆 |
| **11**| 【選配】 | Micro-SD 卡 SPI 模組 + 8G/16G 卡 + Jumper 腳位 | **1 套 (選配)** | [Micro-SD 模組連結](https://shop.cpu.com.tw/Search/advanced/534420e6a8a1e7b584/page/1/) | **$80~$150** |

---

## ⚡ Slide 4: 智慧動態事件採樣與黑盒子快照架構 (Adaptive Event-Driven Logging)

* **1Hz 秒級輪詢 + 環形佇列 (Circular Buffer 60s)**：
  ESP32 每秒向變頻器輪詢一次，並將最新數據推入長度為 60 的記憶體環形佇列，隨時保存發生前 60 秒軌跡。
* **三大動態採樣策略**：
  - **開機衝擊**：電流突升時自動切換為 **1秒/筆** 高密度記錄，穩定後切回 60秒/筆。
  - **關機待機**：轉速為 0 時停止寫入，**僅每小時存 1 筆** (容量趨近 0 Bytes)。
  - **故障觸發黑盒子**：當 `fault_code != 0`，發出 **[前60秒] + [後60秒] 秒級黑盒子報告** (共 120 筆連續秒級數據)。
* **Jumper 硬體雙控開關 (GPIO 15)**：
  插上跳線帽 (GND) 啟用外接 SD 卡離線日誌；拔除跳線帽即切回內部 LittleFS 快取。

---

## 📡 Slide 5: 雲端連線、LINE 告警與儲存空間雙進度條監控 (Analytics & Storage Health)

* **儀表板儲存空間雙進度條**：
  即時顯示 **【內部 LittleFS 快閃記憶體】** 與 **【外接 Micro-SD 卡】** 已用容量 MB/GB、百分比與 Jumper 開關狀態。
* **LINE 秒級告警**：當觸發異常故障碼 (`fault_code != 0`) 時，Apps Script 秒發 LINE Notify 訊息至工程師群組。
* **GBD 樣式 X 軸全量歷史滾輪檢視**：
  - 整合 `chartjs-plugin-zoom`，滑鼠滾輪向後旋轉即可放大/縮小全量時間軸，檢視從開始記錄迄今的所有數據點。

---

## 🧊 Slide 6: Three.js 3D 實體模型與 0%~100% 爆炸拆解 (3D Interactive Demo)

* **工業級 3D 渲染**：包含金屬 ESP-WROOM-32 屏蔽蓋、30Pins 腳位、Jumper 跳線帽、明緯電源標籤引腳、MAX485 端子台與 Micro-SD 讀卡模組。
* **爆炸拆解動態 (0% ~ 100%)**：滑動滑桿即可將 IP68 防水蓋、外殼、主板、電源、SD模組與 3 塊 MAX485 向四周平滑張開拆解。
* **視角聚焦**：提供 `[ESP32主板]`、`[3路MAX485]`、`[明緯電源]`、`[SD與Jumper]`、`[DHT11]` 按鈕一鍵平滑 Fly-To 聚焦。

---

> 📝 **文件建立時間**：2026-08-18  
> 🔗 **GitHub 線上連結**：[https://github.com/Isaacyang34/Homepage/blob/gh-pages/web_tools/kavas_blower_control/PROJECT_PRESENTATION_BRIEFING.md](https://github.com/Isaacyang34/Homepage/blob/gh-pages/web_tools/kavas_blower_control/PROJECT_PRESENTATION_BRIEFING.md)
