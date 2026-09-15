# 全專案通用 UI 設計與防裁切排版規範 (UI Layout Robustness & Anti-Clipping Rule)

當在任何專案（WinForms、WPF、Web/HTML/CSS、Qt、Python GUI）進行介面開發或排版時，必須強制遵守以下排版鐵律，以徹底防止「按鈕遺漏、元件被裁切、DPI 縮放破版」等問題。

---

## 1. 核心三大防護架構

### 鐵律一：嚴禁寫死靜態 Y 座標 (No Hardcoded Absolute Y Coordinates)
* **禁止**：使用靜態絕對座標計算（如 `Location = new Point(12, 570)`）。
* **規範**：強制採用「三段式容器分層佈局」：
  * **Top Header/Toolbar**：`Dock = DockStyle.Top`（固定高度）
  * **Bottom Actions (Run/Stop/Buttons)**：`Dock = DockStyle.Bottom`（固定高度，優先固定）
  * **Center Content (List/Logs/Canvas)**：`Dock = DockStyle.Fill`（自動吃滿剩餘高度，絕不推擠底部操作列）

### 鐵律二：雙保險滾動機制 (Mandatory AutoScroll Safety)
* 任何可能包含多個控制項的 Panel 或 Form，必須明確設定 `AutoScroll = true`。
* 確保在低解析度螢幕或 125%~175% 高 DPI 縮放環境下，元件永遠可透過滾動完整看見，絕不遺失。

### 鐵律三：按鈕陣列採用彈性網格 (TableLayoutPanel / CSS Grid / Flexbox)
* 操作按鈕群（如「開始/停止/設定」）必須封裝於網格容器中，採用百分比（Percent）分配寬度，嚴禁手動寫死按鈕 X 偏移量。
* 核心操作按鈕最小高度不得低於 `40px`，字體大小適中，維持最高操作辨識度。

---

## 2. 產出前自我幾何邊界檢查 (Geometric Verification Checklist)
在交付 UI 程式碼前，必須在心中執行幾何檢核：
1. **高度閉環檢查**：`TopHeight + BottomHeight + MinCenterHeight <= InitialWindowHeight`。
2. **縮放適應性**：將介面放置於 125% DPI 環境下，底部核心按鈕是否依然 100% 可見？
3. **無裁切保證**：是否有任何控制項被放置在 `Parent.Bottom` 之外的不可視座標？
