# 版本迭代與更新觸發最高規範 (Version Iteration & Release Trigger Rule)

## 1. 核心原則 (Core Philosophy)
* **純外觀/ICON 修改不更動版本號**：
  * 若僅是更換圖標 (Icon)、微調配色 (Color Mode)、修正按鈕形狀/邊框或修飾文字排版等外觀層級微調，**嚴格禁止更動版本號**，保持原版本。
* **新增功能或頁面/分頁時始得更新版本**：
  * 當且僅當系統有實質的**「新增功能」**、**「新增獨立視窗/頁面 (Dialog / Window)」**、或**「新增功能分頁 (Tab)」**時，才觸發版本遞增。

## 2. 語意化版本號遞增規範 (Semantic Versioning Standard)
* 版本格式採三位語意化結構：`V<Major>.<Minor>.<Patch>` (例如 `V1.0.1` -> `V1.0.2`)。
* 嚴禁跳過位元（例如嚴禁從 `V1.0.1` 逕行跳至 `V1.1`）。
* 一般功能新增或新頁面引入，**一律遞增第三位版本號 (Patch / Feature increment)**。
