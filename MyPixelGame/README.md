# MyPixelGame - 修仙主題像素動作 RPG (Action RPG)

本專案參考經典 [action-rpg](https://github.com/arthurazs/action-rpg) 專案架構，使用 **Godot 4 Engine (GDScript)** 開發，並結合**修仙世界觀**作為核心題材。

---

## 🎮 遊戲特色 (Gameplay Features)

- **修仙境界與打坐吐納**：從練氣期（一重至九重）突破至築基期，體驗累積靈氣與天劫降臨的快感。
- **即時動作戰鬥 (Action RPG)**：包含御劍術、劍氣揮砍、法術彈幕與閃避。
- **像素風視覺效果 (Pixel Art)**：480x270 經典像素視窗，輔以靈氣粒子與雷劫特效。
- **數據與事件驅動架構**：採用全域 EventBus 發佈/訂閱模式與 Resource 數據庫解耦。

---

## 📁 專案目錄結構 (Project Directory)

```
MyPixelGame/
├── assets/                  # 藝術與音效資源
│   ├── sprites/            # 角色、妖獸 Sprite Sheet
│   ├── tilesets/           # 地圖像素 Tilemap
│   ├── vfx/                # 雷劫特效、靈氣粒子效果
│   └── audio/              # 音效與背景音樂
├── docs/                    # 專案設計與數值規範文件
│   ├── cultivation_design.md# 修仙數值與天劫系統規範
│   └── structure_analysis.md# 架構分析報告
├── src/
│   ├── core/               # 全域核心 Autoload
│   │   ├── GameManager.gd  # 遊戲狀態與存讀檔
│   │   ├── AudioManager.gd # 音效播放器
│   │   └── EventBus.gd     # 全域事件總線
│   ├── data/               # 境界/功法/丹藥數據資源
│   ├── systems/            # 修練與天劫系統 (CultivationManager)
│   ├── entities/           # 實體物件
│   │   ├── player/         # 玩家 (狀態機、輸入、修練)
│   │   ├── enemies/        # 妖獸 AI 狀態機
│   │   ├── npcs/           # 長老 NPC 與聚靈陣互動點
│   │   └── projectiles/    # 劍氣與法術彈幕實體
│   ├── level/              # 地圖與關卡邏輯
│   └── ui/                 # 像素 UI 系統
│       ├── hud/            # 血條、靈力條 (MP)
│       ├── cultivation/    # 打坐與突破介面
│       ├── inventory/      # 儲物袋介面
│       └── dialogue/       # 對話視窗
└── project.godot            # Godot 專案設置檔
```

---

## ⚙️ 快速開始 (Quick Start)

1. 安裝 [Godot 4.x Engine](https://godotengine.org/)。
2. 啟動 Godot 引擎，選擇「Import」並選擇本資料夾下的 `project.godot` 檔案。
3. 點擊 `F5` 啟動專案（預設載入 `src/level/MainLevel.tscn`）。
