# Any2Pixel Pro - 智慧相簿分時轉運站 (支援 iPhone & Android)

> 專為 iPhone 及各廠牌 Android 手機原始畫質照片/影片串流傳輸至 Google Pixel 1 設計，實現 Google 相簿終生原畫質無限雲端備份。

---

## 專案亮點與核心架構

1. **多來源智慧自動偵測 (Universal Source Detection)**：
   - **iPhone 來源**：透過 `pymobiledevice3` 直連讀取 Apple DCIM 相簿。
   - **Android 來源**：自動透過 ADB 辨識非 Pixel 1 之來源手機（例如 Samsung、小米、OPPO、Sony、Vivo 等）。
   - **目標機隔離**：精確鎖定初代 Google Pixel 1 (`sailfish` / `marlin`) 作為無限上傳專用機。

2. **全自動無人值守排程 (Auto-Pilot / Watcher)**：
   - 具備 **Auto-Pilot 智慧監控器**，自動偵測來源手機與 Pixel 1 雙機連線狀態。
   - 支援自動備份、防溢位安全水位控管（低於 1.5GB 安全暫停）與全流程閉環監控。

3. **單向分時轉運與零硬碟負擔**：
   - 電腦僅作為高速轉運站 (Vault Buffer)，以單向排程將相片無損轉運至 Pixel 1。
   - 備份完畢並經使用者於 Pixel 執行「釋出裝置空間」後，即可接續處理下一批次。

4. **動態字級切換 (Accessibility Font Scaling)**：
   - UI 內建字體大小切換按鈕：標準 (100%) $\rightarrow$ 放大 (125%) $\rightarrow$ 特大 (145%)，大螢幕或高 DPI 螢幕清晰好讀。

5. **獨立免安裝綠色發布 (Portable Release V1.0.0)**：
   - 內置 Python 執行環境、ADB 驅動與全套依賴。
   - 融合 **相片鏡頭 + 無限大符號 ($\infty$) + 雙系統徽標** 專屬圖示。

---

## 快速開始與使用方式

### 方式 A：直接雙擊執行檔（最推薦）
- 開啟 `Any2Pixel_V1.0.0_Portable` 資料夾。
- 雙擊執行 **`Any2Pixel.exe`** 即可啟動視窗操作介面。

### 方式 B：一鍵批次啟動器
- 雙擊 **`Run_Any2Pixel.bat`** 啟動主程式。

---

## 手機端設定重點

### 1. 來源機端 (Source Device)
- **iPhone**：以傳輸線連接電腦，解鎖螢幕並在彈出「信任這部電腦」時點選**「信任」**。
- **Android 手機**：進入「設定」→「開發人員選項」→ 開啟**「USB 偵錯」**。

### 2. 目標機端 (Google Pixel 1)
1. 進入「設定」→「系統」→「開發人員選項」→ 開啟**「USB 偵錯」**。
2. 開啟 **Google 相簿 App** → 點選頭像 →「相簿設定」→「備份」→ 確認設定為**「原始畫質」**（享永久免費無限容量）。
3. 當傳輸批次完成且 Google 相簿備份完畢後，點選頭像 →**「釋出裝置空間」**清空實體容量，即可再次接上傳輸下一批！

---

## 版本資訊

- **版本**：`V1.0.0`
- **架構**：x64 Portable Standalone
- **支援系統**：Windows 10 / Windows 11
