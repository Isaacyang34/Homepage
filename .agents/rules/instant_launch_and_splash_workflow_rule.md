# 桌面應用程式極速啟動與啟動畫面體驗規範 (Instant-Launch & Splash Screen Workflow Rule)

## 1. 核心物理限制與痛點
* **PyInstaller `--onefile` 解壓縮延遲**：
  * 單一可執行檔在啟動時，Windows 必須先將整個 Python 執行環境、DLL 與相依庫自解壓至 `%TEMP%\_MEIxxxxxx`。
  * 在解壓縮的 5~15 秒內，Python 代碼完全尚未執行，傳統作法下整個螢幕毫無反應，使用者因不知道程式是否啟動而容易重複連點，引發進程衝突或挫折感。

## 2. 啟動體驗三大鐵律 (Three Golden Rules for Startup UX)

### 鐵律一：零盲等原則 (Zero Blind Wait & Mandatory Splash Screen)
* **C 語言級 Bootloader 原生啟動畫面**：
  * 凡打包為獨立 `--onefile` 之桌面應用，**第一優先必須強制配置 `--splash <image>` 啟動畫面**。
  * 該 Splash Screen 由 PyInstaller C 語言 Bootloader 在解壓的第一毫秒由 Win32 原生彈出，顯示品牌視覺與「正在啟動環境，請稍候...」動態提示，從根本上杜絕黑箱盲等。

### 鐵律二：無縫過渡交接 (Seamless Handoff)
* 主視窗在 `__init__` 完成版面佈局並執行 `root.update()` 成功渲染出第一幀畫面後，始得調用 `pyi_splash.close()`。
* 嚴禁提早關閉導致畫面閃爍，確保使用者視覺從 Splash Screen 平滑無縫接軌至主應用程式。

### 鐵律三：延遲載入 (Lazy Import & Non-Blocking Initialization)
* 視窗程式主入口頂層僅允許 import 渲染介面必要之輕量庫（如 `tkinter`, `sys`, `os`）。
* 重量級模組（如大型通訊協議、加密庫、龐大資料處理庫）一律採延遲載入 (Lazy Load) 或於背景 Worker 執行緒中非同步初始化，確保主視窗以毫秒級速度呈現。
