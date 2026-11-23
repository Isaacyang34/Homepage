# 向量檢索動態掛載與狀態持久化寫入鐵律 (Vector Retrieval & Memory Bank Rule)

## 1. 向量檢索與動態掛載原則 (Cursor / Windsurf Style)
- **拒絕盲目全盤掃描**：嚴禁在面對大型專案時無目的地反覆執行全局搜尋命令消耗大量工具調用與 Token。
- **優先使用語意向量檢索**：當需要定位跨檔案實作或特定功能代碼時，優先調用 `.agents/indexer/codebase_vector_engine.py` 進行 Top-K 檢索。
- **暫態掛載生命週期**：透過 Hook 掛載之代碼區塊應採用 `ephemeralMessage`，用畢即退，嚴禁長留 Context 導致窗口過早耗盡。

## 2. 狀態持久化與自動寫入鐵律 (Cline / Roo Code Style)
- **單一狀態真理之源**：工作區當前執行狀態統一存放於 `.agents/memory-bank/` 下：
  - `activeContext.md` (當前焦點與近期修改)
  - `progress.md` (專案進度清單)
  - `decisionLog.md` (架構決策)
  - `systemPatterns.md` (系統模式)
- **跨對話零記憶遺失**：每次啟動新會話或收到提問時，系統會自動載入 Memory Bank 最新摘要。
- **變更即落盤**：完成關鍵代碼修改或架構變更時，由 Hook 自動同步檔案變更至 `activeContext.md`，並主動於關鍵里程碑更新 `progress.md`。
