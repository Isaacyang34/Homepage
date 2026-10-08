# 工作區邊界物理閉鎖與嚴格禁止跨目錄溢出規範 (Zero Workspace Boundary Spillover & Explicit Path Authorization Rule)

## 1. 核心意圖與原則
* **工作區邊界唯一性**：當前專案工作區（`c:\Users\peter\OneDrive\Desktop\AI_Projects`）為 AI 唯一合法操作範圍。任何未經授權跨出該目錄的寫入、複製、移動或修改行為，均視為嚴重權限溢出與違規操作。
* **杜絕自作聰明同步 (Zero Implicit Auto-Sync)**：嚴格禁止 AI 擅自假設「使用者的測試目錄在哪裡」、「把手冊或檔案貼過去方便測試」等行為。系統路徑、桌面其他目錄、個人測試環境（如 `Desktop/Test` 等）屬於使用者獨立管理領域，AI 絕無權限自動介入。
* **日誌路徑反向污染防護 (Zero Diagnostic Path Bleed)**：使用者在回報問題時提供的日誌片段（例如包含 `Target dir: C:\Users\peter\OneDrive\Desktop\Test`），僅作為被動除錯診斷文字，嚴禁反向提取作為 AI 寫入或部署的目標路徑。

## 2. 嚴格執行規範
1. **工作區邊界物理閉鎖 (Boundary Lock)**：
   - 凡使用檔案工具（`write_to_file`、`replace_file_content`、`multi_replace_file_content`）或 Shell 指令（PowerShell `Copy-Item`、`Move-Item`、`New-Item`、`Set-Content`、`Out-File`、CMD `copy`、`move` 等）：
   - **目標路徑必須且絕對只能座落於工作區內部**：`c:\Users\peter\OneDrive\Desktop\AI_Projects` 及其子目錄。
2. **詢問先行，禁止代為做主 (Inquire First, Never Act Presumptuously - 核心原則)**：
   - **要不要溢出可以詢問，但嚴禁未問即動**：若 AI 在分析或排查過程中認為將檔案部署到外部特定目錄（如 `Desktop/Test`）能便利使用者，**第一動作必須是「先開口詢問」**。
   - 詢問時必須明確交代：
     - 目標外部絕對路徑。
     - 預計複製/同步的具體檔案或資料夾。
     - 此動作的目的與必要性。
   - **當且僅當使用者明確回答同意後，才可執行操作**；若使用者未回應或拒絕，嚴禁擅自動手。
3. **外部路徑操作唯一授權閉鎖 (Explicit Authorization Gate)**：
   - 若使用者在提示詞中**主動明確提出文字授權**（例如「請將最新 EXE 複製到 C:\Users\peter\OneDrive\Desktop\Test」），AI 始得針對該指定外部路徑執行單次操作。
   - 任何未獲授權之背景複製、靜默鏡像備份、跨目錄覆寫均一律嚴格禁止。
4. **產出物與文檔就地安置**：
   - 所有產出之架構分析文檔、外掛範例、設定檔或編譯打包檔案，一律依專案規範完整保留於工作區對應專案目錄下（例如 `TOOL_Stock_Portfolio_Tracker/plugins/`、`TOOL_Stock_Portfolio_Tracker/config/`）。
   - 完成後僅需向使用者回報工作區內部的完整路徑，由使用者自行決定如何取用或部署。

