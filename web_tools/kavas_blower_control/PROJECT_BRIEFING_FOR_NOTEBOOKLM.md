# 📘 KAVAS 鼓風機 3 通道 ESP32 雲端監控與實體控制箱 - 專案全景簡報彙整 (NotebookLM 專用)

> 📌 **文件說明**：本文件專為 **Google NotebookLM** 設計，全盤收錄本專案之**工程背景、物理馬達模型、ESP32 3通道硬體架構、雲端資料管道、廣華電子 BOM 表、工規 IP68 實體配線與 3D 拆解模型**。直接匯入 NotebookLM 即可一鍵生成簡報大綱、語音簡報 (Audio Overview)、問答與簡報劇本！

---

## Executive Summary 1. 專案執行摘要 (Executive Summary)

* **專案名稱**：KAVAS 高速鼓風機 3 通道 ESP32 雲端監控與實體數據採集系統
* **核心目標**：即時監測廠房 3 台 4 極高速鼓風機（0 ~ 24,000 RPM），透過 RS485 (Modbus RTU) 採集轉速、頻率、電壓、電流、馬達溫度與 Hex 故障碼，並經由 Wi-Fi 上傳至 Google Sheets 雲端資料庫與 LINE Notify 告警系統。
* **線上即時儀表板**：[https://isaacyang34.github.io/Homepage/web_tools/kavas_blower_control/index.html](https://isaacyang34.github.io/Homepage/web_tools/kavas_blower_control/index.html)
* **3D 實體模型展示**：[https://isaacyang34.github.io/Homepage/web_tools/kavas_blower_control/3d_view.html](https://isaacyang34.github.io/Homepage/web_tools/kavas_blower_control/3d_view.html)

---

## ⚙️ 2. 馬達物理模型與弱磁區域控制 (Field Weakening Control)

* **馬達極數**：4 極馬達 ($p = 2$ 極對數)
* **頻率轉速公式**：$N = \frac{120 \times f}{p} = 30 \times f$
* **基頻常轉矩區 ($0 \sim 5,000\text{ RPM}$)**：
  - 頻率範圍：$0 \sim 166.7\text{ Hz}$
  - 電壓隨頻率成正比上升：$0 \sim 380\text{ V}$（維持固定 $V/f$ 比例，固定轉矩輸出）
* **弱磁高速區 ($5,000 \sim 24,000\text{ RPM}$)**：
  - 頻率範圍：$166.7 \sim 800.0\text{ Hz}$
  - 輸出電壓飽和封頂於 **$380\text{ V}$**，磁通隨頻率增加而衰減（Constant Power Region）。

---

## 🛠️ 3. 實體控制箱硬體 BOM 清單 (廣華電子商城 shop.cpu.com.tw)

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

---

## ⚡ 4. 實體控制箱工業配線邏輯 (Wiring Architecture)

1. **AC 市電輸入 (AC 110V/220V Input)**：
   市電火線/零線 (紅/白線) 從左側 **PG7 防水頭** 穿入控制盒，經過保險絲座，連接進入 **明緯 IRM-05-05 電源模組 (AC-IN)**。
2. **DC 5V 內部供電 (DC Power Distribution)**：
   明緯電源 DC-OUT 輸出 DC 5V (紅/黑線) 給 ESP32 開發板 `VIN` 以及 3 塊 MAX485 模組供電。
3. **3 通道獨立 RS485 出線 (3-Channel Modbus Output)**：
   控制箱內安裝 **3 塊獨立 MAX485 模組**，各自引出 3 條獨立的 RS485 雙絞屏蔽電纜，分別穿過右側 **3 個 PG9 防水接頭** 出線，對接現場 3 台鼓風機！最遠端 8 米處並聯 **120Ω 終端電阻** 消波防反射。

---

## 🌐 5. 雲端資料管道與前端 GBD 快取的圖表互動

* **Google Apps Script GET/POST API**：
  - `doPost(e)`：接收 ESP32 上傳的 JSON 數據寫入試算表，發生故障碼 (`fault_code != 0`) 時秒發 LINE Notify。
  - `doGet(e)`：回傳從 10:38 起至今的全量歷史數據列。
* **GBD 樣式滾輪無限放大縮小 (Chart.js Zoom & Pan)**：
  - 整合 `chartjs-plugin-zoom`，支援滑鼠滾輪沿 X 軸無限放大縮小觀看全量數據。
  - **自動分配 Y 軸 (Auto Allocation)**：完全移除手動 Y 軸下拉選單，Y 軸由 Chart.js 100% 根據實測數據動態 Auto-Scale。

---

## 📝 NotebookLM 簡報產出指南 (Prompt Guide for NotebookLM)

匯入本文件至 NotebookLM 後，可直接複製以下指令生成簡報：

1. **生成簡報大綱**：
   > 「請根據此專案文件，整理一份包含專案背景、4極馬達弱磁控制、3通道ESP32硬體BOM表與IP68實體配線的簡報簡報大綱 (Slide Outline)。」
2. **生成 2 分鐘語音簡報劇本 (Podcast/Briefing Script)**：
   > 「請以兩位工程師對話的口吻，錄製一段介紹 KAVAS 鼓風機 3 通道 ESP32 雲端監控與 3D 控制箱配線的 Audio Overview 劇本。」
