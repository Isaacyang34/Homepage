# 修仙主題像素動作 RPG (MyPixelGame) 目錄架構分析與增減建議

基於傳統 Godot 2D Action RPG 基礎架構，針對 **「修仙世界觀 (Cultivation World)」** 的遊戲機制需求，進行結構導入與增減評估。

---

## 📁 初始導入目錄架構 (已建置)

```
MyPixelGame/
├── assets/                  # 美術與音效資源
│   ├── sprites/            # 角色、怪物 Sprite Sheet
│   ├── tilesets/           # 地圖 Tilemap
│   └── audio/              # 8-bit / Chiptune 音效與 BGM
├── src/
│   ├── core/               # 全域核心系統
│   │   ├── GameManager.gd  # 遊戲狀態（Pause, Game Over, Level Switch）
│   │   ├── AudioManager.gd # 音效播放器
│   │   └── EventBus.gd     # 全域事件總線（發佈/訂閱模式）
│   ├── entities/           # 實體物件
│   │   ├── player/         # 玩家（Input, State Machine, Animation）
│   │   └── enemies/        # 敵人 AI
│   ├── level/              # 地圖與關卡邏輯
│   └── ui/                 # 像素風格 UI（血條、選單、對話框）
```

---

## 🔬 目錄結構增減分析與建議

修仙 Action RPG 與一般傳統 RPG 最大的差異在於：**數值體系複雜（境界/靈力/功法）、法術彈幕頻繁、具備打坐突破/天劫機制，以及豐富的靈藥法寶數據**。因此建議對基礎架構進行以下增減與擴充：

### ➕ 建議新增 (Additions)

| 新增目錄 / 檔案 | 建議內容與作用 | 理由與必要性 |
| :--- | :--- | :--- |
| **`src/data/`** | **修仙數據庫 (Custom Resources / JSON)**<br>• `realms_data.gd` (境界與突破條件)<br>• `skills_data.gd` (心法與法術招式)<br>• `items_data.gd` (丹藥/靈石/草藥)<br>• `treasures_data.gd` (飛劍/法寶) | 修仙遊戲高度依賴數據驅動，將數值與代碼解耦，利於後續擴充境界與招式。 |
| **`src/systems/`** | **修仙特色機制管理**<br>• `CultivationManager.gd` (修為累積、打坐、破境/天劫判定)<br>• `AlchemySystem.gd` (煉丹系統，擴充備用) | `GameManager` 應專注於遊戲流程與場景切換，將「修練/渡劫」獨立為系統模組維護更清晰。 |
| **`src/entities/projectiles/`** | **法術彈幕與劍氣實體**<br>• `SwordQi.tscn` / `.gd` (劍氣)<br>• `SpellBullet.tscn` / `.gd` (法術/火球/雷劫) | 修仙戰鬥大量依賴遠程劍氣與法術，獨立彈幕模組可供玩家與敵方共通調用。 |
| **`src/entities/npcs/`** | **NPC 與可互動物件**<br>• 宗門長老 / 傳功 NPC<br>• 靈氣聚靈陣 / 靈藥採集點 | 原架構僅有 `player` 與 `enemies`，缺少與場景 NPC 及修鏈資源互動的實體結構。 |
| **`assets/vfx/`** | **粒子與特效素材**<br>• 渡劫雷電、靈氣匯聚粒子、護體靈光 | 修仙動作遊戲對技能視覺效果要求高，建議與常規角色 Sheet 分開存放。 |

---

### ✏️ 建議結構調整 (Adjustments)

1. **`src/ui/` 細化層級**
   - **原結構**：`src/ui/` 包含所有 UI
   - **優化建議**：劃分為子目錄
     - `src/ui/hud/`：血條、靈力條 (MP)、當前境界標示
     - `src/ui/cultivation/`：突破破境介面、氣海打坐面板
     - `src/ui/inventory/`：儲物袋 (背包)、丹藥房、裝備面板
     - `src/ui/dialogue/`：NPC 劇情對話框

---

## 🎯 建議終極修仙專案目錄架構 (Recommended Structure)

```
MyPixelGame/
├── assets/
│   ├── sprites/            # 角色、怪物 Sprite Sheet
│   ├── tilesets/           # 地圖 Tilemap
│   ├── vfx/                # [新增] 靈氣、雷劫、劍氣粒子特效
│   └── audio/              # 音效與背景音樂
├── src/
│   ├── core/               # 全域核心系統
│   │   ├── GameManager.gd
│   │   ├── AudioManager.gd
│   │   └── EventBus.gd
│   ├── data/               # [新增] 修仙數據庫 (境界/功法/丹藥/法寶)
│   ├── systems/            # [新增] 修練與天劫系統 (CultivationManager)
│   ├── entities/
│   │   ├── player/         # 玩家
│   │   ├── enemies/        # 敵人/妖獸
│   │   ├── npcs/           # [新增] 長老/商人/聚靈陣互動點
│   │   └── projectiles/    # [新增] 劍氣/法術/雷劫彈幕實體
│   ├── level/              # 地圖關卡與靈氣區域
│   └── ui/
│       ├── hud/            # [調整] 血條/靈力條
│       ├── cultivation/    # [調整] 打坐突破介面
│       ├── inventory/      # [調整] 儲物袋介面
│       └── dialogue/       # [調整] 對話框
```
