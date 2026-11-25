# 全專案通用 UI 設計與防裁切排版規範 (UI Layout Robustness & Anti-Clipping Rule)

在專案（WinForms、WPF、Web/HTML/CSS、Qt、Python GUI）進行介面開發與排版時，強制遵守以下排版鐵律，以根絕控制項覆蓋、內容被吃、邊界被裁、文字被截斷與 DPI 縮放破版問題。

---

## 1. 核心四大排版架構鐵律

### 鐵律一：根容器物理互斥鐵律 (Mandatory Root TableLayoutPanel / SplitContainer)
* **嚴格禁止**：在 `Form.Controls` 上掛載多個 `Dock` 控制項（例如同時使用 `Dock = Left`、`Dock = Bottom`、`Dock = Fill`）。
  * **物理破壞性**：WinForms 原生 Dock 在 Z-Order 上會產生浮層遮擋，`Dock = Fill` 的繪圖或內容區域會被 `Left` 或 `Bottom` 浮在上方遮蔽，導致下方的核心繪圖區或操作欄被硬生生蓋住！
* **強制規範**：
  * 凡具有「側邊欄 + 中央畫布/內容 + 底部/狀態列」的多區域介面，**第一行代碼強制宣告 `Root TableLayoutPanel` (或 `SplitContainer`) 作為頂層根網格**！
  * 範例結構：
    ```csharp
    TableLayoutPanel rootLayout = new TableLayoutPanel();
    rootLayout.Dock = DockStyle.Fill;
    rootLayout.ColumnCount = 2; // Col 0: 側邊欄 (固定寬度 330px), Col 1: 中央畫布 (100% Fill)
    rootLayout.RowCount = 2;    // Row 0: 工作區 (100% Fill), Row 1: 狀態列 (固定高度 175px)
    rootLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 330F));
    rootLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
    rootLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
    rootLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 175F));
    this.Controls.Add(rootLayout);

    rootLayout.Controls.Add(panelLeft, 0, 0);
    rootLayout.Controls.Add(canvasContainer, 1, 0);
    rootLayout.SetColumnSpan(panelBottom, 2);
    rootLayout.Controls.Add(panelBottom, 0, 1);
    ```
  * **效果**：所有區域在排版網格上互斥，元件在物理上絕不可能重疊遮擋，破版機率為 0%！

### 鐵律二：內部容器百分比網格 (全面廢除 Point(X, Y) 手動累加)
* **嚴格禁止**：
  * 手動撰寫絕對靜態座標計算（如 `Location = new Point(12, 570)`）。
  * 在迴圈或動態初始化時使用累加偏移量（如 `y += 105; Location = new Point(10, y)`）。
* **強制規範**：
  * 所有 GroupBox 與 Panel 內部一律強制使用 `TableLayoutPanel`（一個蘿蔔一個坑）或垂直 `FlowLayoutPanel`。
  * 操作按鈕若多個橫排，採用百分比（Percent）分配寬度，嚴格禁止手寫按鈕 X 偏移量。
  * 核心操作按鈕最小高度不得低於 `36px`，字體大小適中，維持最高操作辨識度。

### 鐵律三：文字防裁切與寬度保護保證 (Anti-Text-Clipping Rule)
* **CheckBox 與 Label**：
  * 若文字包含繁體中文字元超過 8 個字（例如 `[v] 標籤顯示 dBm 數值`），**嚴格禁止**與其他元件在同一列水平擠壓！
  * 必須配置為獨立單列 (Row) 或給予至少 `250px` 以上充足寬度，杜絕字尾被吃掉（如變成 `[v] 標籤顯示`）。
### 鐵律四：DataGridView 表格 100% 自由手動拖曳調整與佈局持久化記憶鐵律 (User-First Manual Resizing & INI Layout Persistence)
* **全面廢除粗暴的代碼覆寫**：
  * **嚴禁自作主張鎖死**：代碼中禁止使用會鎖死欄寬或每次切換頁面就抹除使用者拖曳成果的粗暴 AutoFit。
* **100% 自由手動拖曳**：
  * 所有 DataGridView 必須明確設置 AllowUserToResizeColumns = true，且所有 Column 皆設為 Resizable = DataGridViewTriState.True。
  * 使用者可以隨時用滑鼠按住任何一欄的右邊界，想拉大就拉大、想縮小就縮小，完全不受任何約束。
* **佈局自動持久化記憶 (layout.ini)**：
  * 監聽 ColumnWidthChanged 與 SplitterMoved 事件，並在 FormClosing 時自動將所有欄位寬度與面板分割位置寫入 layout.ini。
  * 程式啟動 (InitializeComponent / OnShown) 時自動載入使用者上次調整好的數據，確保使用者拉好的排版永久生效！

### 鐵律五：同畫面零冗餘控制項與單一職責入口鐵律 (Zero Redundant Buttons & Single Entry Point)
* **嚴格禁止重複入口**：在同一個可視畫面或同一個視窗層級中，**嚴禁針對同一功能、同一視圖切換或同一對話框提供多個重複按鈕**！
  * **禁止情境範例**：若畫面上方或內容區已存在分頁標籤（TabControl / 切換按鈕），**嚴格禁止在底部工具列、側邊欄或快捷列再額外放置一模一樣的功能按鈕**！
* **單一職責單一入口原則 (Single Source of Interaction)**：
  * 每項功能、切換或操作，在同一個視窗中**只允許保留唯一個最自然、最符合直覺的專屬入口**。
  * 杜絕多餘按鈕佔據寶貴的螢幕空間，消除視覺噪音，確保介面純粹專業。
* **交付前強制自審**：在任何介面程式碼提交或交付前，必須逐一掃描所有 Button 的 `command` / `Click` 處理函式，若存在兩個以上按鈕呼叫同一業務邏輯或開啟同一分頁，**必須立即無條件刪除冗餘按鈕**！

---

## 2. 規劃與交付前檢核清單 (Pre-Flight Verification Checklist)

在撰寫計畫或交付 UI 程式碼前，必須嚴格自檢：
1. **頂層根結構檢查**：是否使用了 `Root TableLayoutPanel` 或 `SplitContainer` 進行物理區域切割？（嚴禁在 Form 上直接掛多個 Dock！）
2. **零 Point(X,Y) 檢查**：程式碼中是否完全不存在 `Location = new Point(x, y)` 手動計算？
3. **文字完整性檢查**：所有 CheckBox 與按鈕文字在 1024x768 解析度下是否 100% 完整無截斷？
4. **表格縮水防護**：DataGridView 各欄位是否皆透過 `AutoFitColumns` 賦予真實測量之 `MinimumWidth` 保護？
5. **高度閉環檢查**：`TopHeight + BottomHeight + MinCenterHeight <= InitialWindowHeight`。
6. **零冗餘按鈕檢查**：檢查同一個畫面中是否有重複觸發同一功能或視圖切換的按鈕？（嚴禁分頁與工具列雙重按鈕！）
