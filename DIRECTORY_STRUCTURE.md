# 📁 X13 工作區資料夾與檔案分佈索引 (DIRECTORY_STRUCTURE.md)

> 📌 **說明**：本文件記錄 `X13` 工作區的完整目錄結構與檔案分佈。當目錄結構有異動或新增/調整專案時，將會同步更新此文件。
> **最後更新時間**：2026-08-18

---

## 🌳 完整目錄樹狀結構

```text
X13/
├── 🎮 godot_projects/             # Godot 遊戲開發與相關專案集中區
│   ├── Xiuxian_HD/                # 修仙奇緣 HD Godot 4 主專案 (含 project.godot, scenes, scripts)
│   ├── WujinHonghuang_PixelGame/   # 無盡洪荒像素風格專案
│   ├── Zelda_Godot/               # 薩爾達風格 Godot 遊戲專案
│   ├── WormsWMD_Godot/            # 百戰天蟲風格 Godot 遊戲專案
│   ├── godot_engine/              # Godot 引擎相關執行檔與組件
│   └── godot_demos/ / godot_project/ # Godot 範例與測試工程
│
├── 💼 other_projects/             # 其餘獨立開發專案 (Web, 數據, 工具)
│   ├── Flight_Price_Tracker/      # 機票價格追蹤系統
│   ├── Handwritten_Infographic_Demo/ # 手寫圖表 Demo 專案
│   ├── Jindian_Art_Catalog/       # 金點美工藝術目錄專案
│   ├── 金點美工/                   # 金點美工相關素材與資料
│   ├── MyPixelGame/               # 個人像素遊戲備份/測試專案 (含已下載之 Kenney 經典素材)
│   ├── GBD/                       # GBD 專案
│   ├── dashboard/                 # 控制台 / 儀表板專案
│   ├── deal_search/               # 優惠搜尋專案
│   └── excel/                     # Excel 處理與數據相關工具
│
├── 🌐 web_tools/                  # 單檔 HTML 獨立網頁工具庫
│   ├── badminton/                 # 羽球計分板工具 (BadmintonScoreboard, BLE 等)
│   ├── led_studio/                # LED 螢幕設計界面 (V2.0, V1.1.bak, LED_SPECS)
│   ├── motor_calculator/          # 馬達計算與等效電路繪製工具
│   ├── display_hardware/          # OLED 版面設計與 3D 顯示 UI 工具
│   ├── 慣量計算/                  # 多材質旋轉體轉動慣量與馬達動力學計算器 (慣量計算.html, 專案 JSON)
│   └── kavas_blower_control/      # KAVAS 鼓風機 ESP32 Wi-Fi 雲端 3通道數據採集與實體控制箱專案 (KAVAS手冊.pdf, KAVAS_ESP32_DataLogger_Design.md, PROJECT_PRESENTATION_BRIEFING.md, PROJECT_BRIEFING_FOR_NOTEBOOKLM.md, BOM_List.md, presentation.html, 3d_view.html, kavas_esp32_logger.ino, google_script.js, index.html, simulator.html)
│
├── 💻 system_tools/               # 系統工具、軟體安裝檔與大容量 ISO
│   ├── ubuntu-24.04.4-desktop-amd64.iso # Ubuntu 24.04 Desktop ISO (6.6GB)
│   ├── Display Driver Uninstaller_* # DDU 顯卡驅動徹底清除工具
│   ├── Rufus_* / rufus.ini / Rufus/ # Rufus 隨身碟開機檔製作工具
│   └── 散熱膏比較與選購建議 - Google Gemini.pdf # 硬體採購參考文件
│
├── 🛠️ dev_scripts/                # 開發輔助、下載與備份腳本
│   ├── download_*.ps1             # 各類資產與引擎自動下載 PowerShell 腳本
│   ├── manage-versions.ps1        # 版本控制管理腳本
│   ├── backup.bat                 # 自動備份批次檔
│   ├── find_dl.js / build_*.js    # Node.js 構建與下載輔助腳本
│   └── 程式結構.txt               # 程式結構備忘說明
│
├── ⚙️ 全域與設定
│   ├── .agents/                   # AGENTS.md 專案規範與規則
│   ├── .git/ / .github/           # Git 版本控制目錄
│   ├── index.html / page.html     # 工作區全站入口網頁
│   └── styles.css                 # 入口網頁樣式表
```

---

## 📌 異動維護規範
1. 新增專案或改變資料夾位置時，需同步更新本 Markdown 之樹狀圖與描述。
2. 保持根目錄淨空，新增檔案依屬性放入 `godot_projects/`、`other_projects/`、`web_tools/`、`system_tools/` 或 `dev_scripts/`。
