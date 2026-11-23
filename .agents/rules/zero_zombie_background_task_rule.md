# 背景殭屍任務即時防呆與用畢即殺最高鐵律 (Zero-Zombie Background Task & Mandatory Clean-Kill Rule)

## 1. 核心致命痛點與現象 (The Zombie Task Dilemma)
* 在 Antigravity 代理人環境中調用 `run_command` 時，若指令執行時間略長、或 PowerShell 報錯但行程未完全退出，Antigravity 會將其轉入背景任務 (Background Task)。
* **「占著茅坑不拉屎」現象**：該任務實質已無作用（或已報錯崩潰），卻持續掛在系統背景，導致 UI 頂部永久顯示 `1 task running: >_ $dest = ...`。
* **次生災害**：
  1. 霸佔聊天介面頂部狀態列，干擾使用者注意力與體驗。
  2. 背景進程可能持續持有目標目錄或 `.exe` 的檔案鎖 (File Lock)，導致後續的編譯、刪除或覆寫直接報錯 `Cannot create a file when that file already exists`。

## 2. 三大強制執行鐵律 (Mandatory 3-Layer Enforcement)

### 鐵律一：短命令給足超時，嚴禁盲目轉入背景 (Mandatory Synchronous for Transient Commands)
* 凡是檔案移動 (`Move-Item`)、複製 (`Copy-Item`)、刪除 (`Remove-Item`)、目錄掃描 (`Get-ChildItem`)、語法檢查 (`py_compile`) 等短指令：
  * **`WaitMsBeforeAsync` 強制設置為 10000 (10秒)**，確保在同步區間內直接執行完畢並返回！
  * **嚴格禁止** 對瞬態檔案操作設置 500ms 等過短時間，導致本該 1 秒完成的指令被拋入背景任務！

### 鐵律二：Turn 前後主動巡檢與秒殺清除 (Pre/Post-Turn Task Audit & Instant Clean-Kill)
* **主動巡檢責任**：AI 在每次接收任務或輸出結論前後，若發現有任何非預期的背景任務正在掛起：
  1. **第一優先動作**：主動調用 `manage_task(Action='list')` 獲取當前任務清單。
  2. **秒殺斬斷**：對所有無實質運算、報錯殘留或非長期服務的任務，**立即調用 `manage_task(Action='kill', TaskId=...)` 強制終止刪除**！
* **零殘留指標**：除非使用者明確要求啟動背景守護行程（如本機 Web Server），**向使用者回報結論前，背景任務清單必須保證為 0 (No background tasks running)**！絕不讓使用者看到頂部出現 `1 task running` 而開口提醒！

### 鐵律三：建置與編譯任務的生命週期閉鎖 (Build Task Teardown)
* 執行 PyInstaller、C# csc 或大型測試時，即使在背景執行：
  * 當系統通知 `finished with result` 後，**必須在下一動作立即確認該 Task 是否已完全釋放**；
  * 若發現該 Task 仍處於 `RUNNING` 或卡死，立即調用 `manage_task(Action='kill')` 進行收尾撕毀 (Teardown)；
  * 同時主動檢查作業系統層級是否有殘留的懸空行程（如 `Get-Process -Name "*EV_CAN*" | Stop-Process -Force`），徹底杜絕檔案鎖與記憶體洩漏。

