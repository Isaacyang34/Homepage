# TOOL_Stock_Portfolio_Tracker V1.2.0.0 跨平台全功能驗證測試報告
**涵蓋範疇：地端桌面版 (Tkinter GUI / app_core.pkg) 與 現代化網頁版 (Web Client)**  
**報告日期**：2026-11-03  
**版本版次**：地端原生版 `V1.2.0.0` (內核 `app_core.pkg` 396KB) | 網頁端版次 `V1.3.0`  
**測試結果**：全部 11 項測試通過 (11/11 PASS, 100%)  

---

## 1. 測試目標與架構總覽

本次功能升級全面對標專業財務分析平台之視覺呈現，並在**地端桌面版**與**網頁端**同步落地實作：

1. **個股基本面深度分析 & 價值河流圖 (支援全市場 1,000+ 檔標的任意輸入查詢)**：
   - **全面開放任意個股輸入**：選單解除 4 檔限制，支援直接輸入任何台股代碼 (如 2317, 2454, 2603) 或名稱 (如 鴻海, 聯發科, 長榮)，亦可從下拉選單瀏覽全市場 1,088 檔標的與庫存持股。
   - **本益比價值河流圖 (P/E River Valuation Bands)**：清楚標示左側 Y 軸金額、水平參考虛線網格、4 層漸層河流區間、右側倍數與實質價位標記、歷史收盤價亮白軌跡線與最新收盤價光暈焦點。
   - **近 6 個月營收與年增率 (YoY) 雙軸圖**：左軸營收 (億元 NTD)、右軸 YoY (%)、藍色長條柱標註月營收金額、金黃色折線標註年增率。
   - **獲利能力三率走勢圖**：標準百分比刻度 (0%~60%)，毛利率(黃)、營業利益率(藍)、稅後淨利率(綠) 三色折線與各節點數值。
   - **杜邦分析三因子拆解卡**：ROE、淨利率、總資產週轉率、權益乘數槓桿與專業診斷評語。
   - **財務安全性指標卡**：自由現金流、營業現金流、負債比率、流動比率、速動比率。
2. **盤後五維度綜合排名與市場環境層 (Market Regime & Funnel Screener)**：
   - 市場環境層：結合大盤季線 (MA60)、市場寬度、外資期貨淨留倉、選擇權 PCR 比率，動態判定「多頭擴張 / 震盪整理 / 空頭防禦」，並自適應調整評分權重。
   - 五階量化篩選漏斗：硬性濾網 -> 百分位排名 -> 環境動態調權 -> ATR 波動度倉位風控 -> 決策歸因。
3. **無縫雙向互通**：
   - 桌面端與網頁端持股清單與排行表點擊任意標的，一鍵直達個股深度分析對話框。

---

## 2. 測試矩陣與自動化執行結果

### 2.1 後端與演算法核心測試 (5/5 PASS)
| 編號 | 測試項目 | 測試標的 | 驗證標準 | 執行狀態 |
|---|---|---|---|---|
| **TC-01** | 本益比河流圖帶狀計算 | `StockFundamentalService.get_stock_profile` | 16x~32x 河流通道單調遞增，全市場代碼動態建模 | **PASS** (0.002s) |
| **TC-02** | 杜邦三因子拆解 | `StockFundamentalService` | $\text{ROE} = \text{淨利率} \times \text{週轉率} \times \text{權益乘數}$ 數值相符 | **PASS** (0.001s) |
| **TC-03** | 市場環境層與自適應調權 | `MarketRegimeEngine.evaluate_regime` | MA60+PCR 動態判定多頭/空頭，權重總和為 1.0 | **PASS** (0.001s) |
| **TC-04** | 五階漏斗篩選與 ATR 風控 | `FunnelQuantScreener.screen_stocks` | 排除不符資格股、百分位正規化、給予建議倉位%與歸因 | **PASS** (0.002s) |
| **TC-05** | 前端 HTML/JS 整合完整性 | `web/index.html`, `web/app.js` | 包含所有新增 ID、分頁按鈕、河流圖 Canvas 與點擊呼叫 | **PASS** (0.001s) |

### 2.2 本地桌面端 Tkinter GUI 自動化驗證 (3/3 PASS)
| 編號 | 測試情境 | 執行動作 | 預期成果 | 實測佐證截圖 | 狀態 |
|---|---|---|---|---|---|
| **DT-01** | 主視窗底部功能列 | 啟動 `PortfolioApp`，檢查底部按鈕容器 | 成功載入 `[基本面] 價值河流圖` 按鈕，點擊觸發分析對話框 | `screenshot_desktop_1_main_gui.png` (50,840 bytes) | **PASS** |
| **DT-02** | 綜合排行盤後分頁 | 切換至排行分頁 -> `[量化] 盤後五維度` | 頂部即時呈現市場環境卡片 (MA60, 寬度, PCR)，下方載入 6 檔漏斗候選股 | `screenshot_desktop_2_postmarket_rankings.png` (102,871 bytes) | **PASS** |
| **DT-03** | 桌面端基本面分析視窗 | 實例化 `FundamentalAnalysisDialog` | Tkinter Canvas 繪製專業清晰之 P/E 河流圖帶、營收雙軸圖、三率走勢圖與杜邦卡 | `screenshot_desktop_3_fundamental_river_bands.png` (102,961 bytes) | **PASS** |

### 2.3 網頁端 Headless Chrome 自動化驗證 (3/3 PASS)
| 編號 | 測試情境 | 檢驗條件 | 實測佐證截圖 | 狀態 |
|---|---|---|---|---|
| **WT-01** | 持股明細與深度分析按鈕 | 持股列表含可點擊標的與 `🔍 深度分析` 按鈕 | `screenshot_1_portfolio_with_analysis_btn.png` (154,635 bytes) | **PASS** |
| **WT-02** | 盤後量化排行與市場環境儀表 | 市場環境指針卡片、動態權重分布條、五階漏斗候選股表格 | `screenshot_2_postmarket_rankings_regime.png` (210,035 bytes) | **PASS** |
| **WT-03** | 網頁端本益比河流圖與杜邦卡 | Canvas 渲染 5 條河流帶、四季杜邦拆解卡、財務安全指標 | `screenshot_3_fundamental_river_bands.png` (208,610 bytes) | **PASS** |

---

## 3. 測試佐證截圖展示

### 3.1 本地端桌面軟體 (Tkinter GUI / app_core.pkg)

#### [地端實測 1] 主視窗介面與底部專屬入口
底部操作工具列成功整合 `[基本面] 價值河流圖` 按鈕。  
![桌面端主視窗截圖](/C:/Users/peter/.gemini/antigravity/brain/1565b6be-16f1-41ee-aafc-f593b83e1b39/screenshot_desktop_1_main_gui.png)

#### [地端實測 2] 盤後五維度綜合排名與市場環境層
展示頂部市場環境狀態指針（多頭擴張、MA60 站上、市場寬度 68%、PCR 118%），下方五階量化篩選漏斗候選清單。  
![桌面端盤後量化排行截圖](/C:/Users/peter/.gemini/antigravity/brain/1565b6be-16f1-41ee-aafc-f593b83e1b39/screenshot_desktop_2_postmarket_rankings.png)

#### [地端實測 3] 全市場支援之個股基本面深度分析 & 價值河流圖視窗 (台積電 2330.TW)
重構修復畫布幾何尺寸與標籤刻度，呈現高清晰度的河流圖通道（\$1,000 ~ \$3,500 金額刻度）、月營收 YoY 雙軸圖與獲利三率趨勢圖。  
![桌面端基本面分析視窗截圖](/C:/Users/peter/.gemini/antigravity/brain/1565b6be-16f1-41ee-aafc-f593b83e1b39/screenshot_desktop_3_fundamental_river_bands.png)

---

### 3.2 網頁端 (Web Client)

#### [網頁實測 1] 持股清單與深度分析操作入口
持股代碼支援直接點擊跳轉，操作欄新增獨立 `🔍 深度分析` 按鈕。  
![網頁端持股清單截圖](/C:/Users/peter/.gemini/antigravity/brain/1565b6be-16f1-41ee-aafc-f593b83e1b39/screenshot_1_portfolio_with_analysis_btn.png)

#### [網頁實測 2] 盤後量化排行與自適應環境調權
五階漏斗完整輸出「硬性濾網通過」、「動態調權綜合分」、「ATR 建議部位」與「核心入選歸因」。  
![網頁端盤後量化排行截圖](/C:/Users/peter/.gemini/antigravity/brain/1565b6be-16f1-41ee-aafc-f593b83e1b39/screenshot_2_postmarket_rankings_regime.png)

#### [網頁實測 3] 個股價值河流圖與杜邦分析 (2330 台積電)
Chart.js 即時渲染 16x~32x 本益比估值帶狀圖，搭配月營收 YoY 與營業利益率走勢。  
![網頁端價值河流圖截圖](/C:/Users/peter/.gemini/antigravity/brain/1565b6be-16f1-41ee-aafc-f593b83e1b39/screenshot_3_fundamental_river_bands.png)

---

## 4. 規範符合度檢查

1. **零第三方品牌詞彙原則**：全案代碼、UI 文案、註解與日誌 100% 杜絕任何第三方程式品牌名，一律使用「個股基本面深度分析 & 價值河流圖 (Stock Fundamental & River Valuation)」。
2. **WinForms / Tkinter GDI+ 禁 Emoji 鐵律 (Rule 9)**：已全面掃描並清除所有 `> 0xFFFF` 字元，按鈕與標籤全數使用 ASCII/BMP 安全字元。
3. **未經指示嚴禁擅自開啟瀏覽器鐵律 (Rule 8)**：網頁端測試 100% 由背景無介面 (Headless Chrome) 完成，無彈出任何視窗干擾使用者。
4. **地端驗證優先原則 (Rule 14)**：未執行任何未授權之 `git push`，所有二進位核心 (`app_core.pkg`) 與源碼皆在本地端完成編譯與雙端封裝。

---
**報告簽核**：全數測試通過，全市場個股分析功能已完全整合就緒。
