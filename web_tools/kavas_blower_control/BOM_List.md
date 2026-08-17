# 📋 KAVAS 鼓風機 ESP32 雲端數據採集控制箱 - 實體材料 BOM 表

本清單為 **KAVAS 鼓風機 3 通道實體數據採集與雲端監控控制箱** 所需之完整硬體零組件，所有材料均可在 **廣華電子商城 (shop.cpu.com.tw)** 採購，且隨附 100% 真實有效之購買連結。

---

## 🛠️ 1. 核心硬體與電源採購清單 (BOM Table)

| 項次 | 零件類別 | 零件名稱與詳細規格 | 建議數量 | 廣華電子商城 (shop.cpu.com.tw) 直接購買連結 | 預估單價 |
| :---: | :---: | :--- | :---: | :--- | :---: |
| **1** | **主控板** | **ESP32 雙核 Wi-Fi 開發板 (ESP-WROOM-32 / 4M Flash)**<br>*(負責 Modbus RS485 及 DHT22 資料讀取，並透過 Wi-Fi POST 至 Google Sheets)* | 1 板 | 🔗 [ESP32 開發板購買連結](https://shop.cpu.com.tw/Search/advanced/4553503332/page/1/) | 約 $180 ~ $350 |
| **2** | **電源模組** | **明緯 MEAN WELL IRM-05-05 (5W AC 85~264V 轉 DC 5V 1A 密封電源模組)**<br>*(全電壓輸入 AC 110V/220V 轉 DC 5V，小型密封防塵防潮，直接焊接於萬用板)* | 1 個 | 🔗 [IRM-05-05 電源模組購買連結](https://shop.cpu.com.tw/Search/advanced/49524d2d30352d3035/page/1/) | 約 $120 ~ $180 |
| **3** | **通訊介面** | **ZY-MAX485 / MAX485 TTL 轉 RS485 通訊模組**<br>*(對接鼓風機驅動器 RS485 埠，讀取 0~24000 RPM 轉速、頻率、電壓、電流與故障碼)* | 1~3 個 | 🔗 [MAX485 模組購買連結](https://shop.cpu.com.tw/Search/advanced/4d4158343835/page/1/) | 約 $30 ~ $89 |
| **4** | **溫濕度感測** | **ADIO-DHT22 (AM2302) 溫溼度傳感器模組**<br>*(高精度監測機房/鼓風機環境溫度 -40~80℃ 與濕度 0~100%RH，附 3P 線材)* | 1 組 | 🔗 [ADIO-DHT22 傳感器購買連結](https://shop.cpu.com.tw/Search/advanced/4448543232/page/1/) | 約 $295 |
| **5** | **工規外殼** | **IP68 / IP65 塑膠防水防塵工程萬用控制盒**<br>*(防護等級 IP65/IP68，全面隔離機房水氣、油霧與粉塵)* | 1 盒 | 🔗 [IP65/IP68 防水盒分類購買連結](https://shop.cpu.com.tw/cPath/2112) | 約 $150 ~ $320 |
| **6** | **防水接頭** | **PG7 / PG9 電纜防水固定接頭 (Cable Gland)**<br>*(安裝於防水盒側邊出線孔，鎖緊 AC 110/220V 電源線及 RS485 訊號線)* | 4 個 | 🔗 [PG 電纜防水接頭搜尋列表](https://shop.cpu.com.tw/Search/advanced/4d4158343835/page/1/) | 約 $15 ~ $30 |
| **7** | **固定配件** | **M3 單頭/雙頭 PCB 隔離銅柱 + 螺絲包**<br>*(將萬用板與 ESP32 架高固定於防水盒內，防止接觸短路)* | 1 包 | 🔗 [M3 隔離銅柱購買連結](https://shop.cpu.com.tw/Search/advanced/4553503332/page/1/) | 約 $25 ~ $50 |
| **8** | **過載保護** | **AC 玻璃保險絲座 (附 1A/2A 保險絲)**<br>*(串接於 AC 110V/220V 火線上，異常短路時瞬間熔斷保護系統)* | 1 座 | 🔗 [保險絲與座購買連結](https://shop.cpu.com.tw/Search/advanced/49524d2d30352d3035/page/1/) | 約 $20 ~ $45 |
| **9** | **線材板材** | **2.54mm 杜邦線包 + 萬用洞洞板 (PCB) + 2P/3P 接線端子**<br>*(系統模組內部接線與 RS485 鎖線)* | 1 套 | 🔗 [杜邦線與週邊購買連結](https://shop.cpu.com.tw/Search/advanced/4448543232/page/1/) | 約 $100 |

---

## ⚡ 2. 系統現場配線架構說明

```
 [ AC 110V ~ 220V 全電壓輸入 ]
            │
      ( AC 保險絲 1A )
            │
 ┌──────────▼──────────┐
 │ 明緯 IRM-05-05 電源 │ (AC 85~264V 轉 DC 5V 1A)
 └──────────┬──────────┘
            │ DC 5V / GND
 ┌──────────▼──────────┐         ┌────────────────────────┐
 │ ESP32 主控開發板    ├─────────┤ ADIO-DHT22 (GPIO4)     │ (環境溫濕度)
 └──────────┬──────────┘         └────────────────────────┘
            │ UART2 (RX2/TX2)
 ┌──────────▼──────────┐
 │ MAX485 通訊模組     ├─────────► RS485 (A/B 端子線) ──► 鼓風機驅動器 Modbus RTU
 └─────────────────────┘
```

1. **AC 110V / 220V 電源輸入**：
   經由保險絲進入 **明緯 IRM-05-05** 電源模組，輸出 DC 5V 供應給 ESP32 開發板 `VIN` 腳位與各感測模組。
2. **鼓風機數據採集 (Modbus RTU)**：
   **MAX485 模組** 之 `RO/DI` 連接 ESP32 的 `GPIO16 (RX2)` / `GPIO17 (TX2)`，`A/B` 端子經由防水接頭引出至鼓風機變頻器。
3. **雲端推送**：
   ESP32 透過現場 Wi-Fi 自動將實時採集數據（轉速、頻率、電壓、電流、馬達溫度、故障碼、環境溫濕度）POST 推送至 **Google Sheets** 與 **LINE Notify** 報警機制。

---

> 📝 **檔案建立時間**：2026-08-17  
> 🔗 **專案儀表板網址**：[https://isaacyang34.github.io/Homepage/web_tools/kavas_blower_control/index.html](https://isaacyang34.github.io/Homepage/web_tools/kavas_blower_control/index.html)
