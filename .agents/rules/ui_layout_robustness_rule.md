# 全專案通用 UI 設計與防裁切排版規範 (UI Layout Robustness & Anti-Clipping Rule)

當在任何專案（WinForms、WPF、Web/HTML/CSS、Qt、Python GUI）進行介面開發或排版時，必須強制遵守以下排版鐵律，以徹底防止「控制項互相覆蓋、中央畫布被遮蔽、元件被裁切、文字被吃字、DPI 縮放破版」等問題。

---

## 1. 核心三大防護架構

### 鐵律一：根容器物理互斥鐵律 (Mandatory Root TableLayoutPanel / SplitContainer)
* **嚴格禁止**：直接在 `Form.Controls` 上掛載多個 `Dock` 控制項（例如同時使用 `Dock = Left`、`Dock = Bottom`、`Dock = Fill`）。
  * **致命缺陷**：WinForms 原生 Dock 在 Z-Order 渲染上會產生浮層遮蔽，`Dock = Fill` 的畫布或內容區域會被後續的 `Left` 或 `Bottom` 面板直接壓在下方，造成左側與下方的內容（如格局圖房間、表格、比例尺）被硬生生遮擋覆蓋！
* **強制規範**：
  * 凡具有「側邊欄 + 中央內容/畫布 + 底部控制/狀態列」的多區域介面，**第一行代碼必須強制宣告 `Root TableLayoutPanel` (或 `SplitContainer`) 作為頂層全域根網格**！
  * 範例結構：
    ```csharp
    TableLayoutPanel rootLayout = new TableLayoutPanel();
    rootLayout.Dock = DockStyle.Fill;
    rootLayout.ColumnCount = 2; // Col 0: 側邊欄 (固定寬度 330px), Col 1: 中央畫布 (100% Fill)
    rootLayout.RowCount = 2;    // Row 0: 工作區 (100% Fill), Row 1: 底部列 (固定高度 175px)
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
  * **效果**：所有區域在幾何儲存格上完全互斥，側邊欄與底部列在物理上絕對不可能遮擋中央內容，遮蔽率為 0%！

### 鐵律二：內部容器百分比網格 (全面廢除 Point(X, Y) 累加)
* **嚴格禁止**：
  * 手動寫死靜態座標計算（如 `Location = new Point(12, 570)`）。
  * 在迴圈或初始化時使用累加偏移量（如 `y += 105; Location = new Point(10, y)`）。
* **強制規範**：
  * 所有 GroupBox 與 Panel 內部一律強制使用 `TableLayoutPanel`（一個蘿蔔一個坑）或垂直 `FlowLayoutPanel`。
  * 操作按鈕群必須封裝於網格容器中，採用百分比（Percent）分配寬度，嚴禁手動寫死按鈕 X 偏移量。
  * 核心操作按鈕最小高度不得低於 `36px`，字體大小適中，維持最高操作辨識度。

### 鐵律三：文字防裁切與寬度保護保證 (Anti-Text-Clipping Rule)
* **CheckBox 與 Label**：
  * 若文字包含繁體中文字元超過 8 個字（例如 `[v] 標籤顯示 dBm 數值`），**嚴格禁止**與其他元件在同一列水平擠壓！
  * 必須配置為獨立單列 (Row) 或給予至少 `250px` 以上充足寬度，杜絕字尾被吃掉（如變成 `[v] 標籤顯示`）。
* **DataGridView 表格**：
  * 嚴禁僅設定 `AutoSizeColumnsMode = Fill` 而不設底線！
  * 必須為每一個欄位明確指定 `FillWeight` 與 `MinimumWidth`（例如 `MinimumWidth = 80`），防止表格寬度不足時標題文字被擠壓縮排（如 `SSID 網...`）。

### 鐵律四：雙保險滾動機制 (Mandatory AutoScroll Safety)
* 任何可能包含多個控制項的 Panel 或 Form，必須明確設定 `AutoScroll = true`。
* 確保在低解析度螢幕或 125%~175% 高 DPI 縮放環境下，元件永遠可透過滾動完整看見，絕不遺失。

---

## 2. 規劃與交付前檢核清單 (Pre-Flight Verification Checklist)

在撰寫 `implementation_plan.md` 或交付 UI 程式碼前，必須嚴格自檢：
1. **頂層根結構檢查**：是否使用了 `Root TableLayoutPanel` 或 `SplitContainer` 進行物理區域切割？（嚴禁在 Form 上直接掛多個 Dock！）
2. **零 Point(X,Y) 檢查**：程式碼中是否完全不存在 `Location = new Point(x, y)` 手動計算？
3. **文字完整性檢查**：所有 CheckBox 與按鈕文字在 1024x768 解析度下是否 100% 完整無截斷？
4. **表格縮水防護**：DataGridView 各欄位是否皆有 `MinimumWidth` 保護？
5. **高度閉環檢查**：`TopHeight + BottomHeight + MinCenterHeight <= InitialWindowHeight`。
