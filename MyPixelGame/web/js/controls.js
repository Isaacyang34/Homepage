// ═══════════════════════════════════════════════════════════
// ⌨️ 全域按鍵控制對照表 (Centralized Keybindings Config)
// 徹底消除 F 鍵重疊問題，明確規範所有功能熱鍵
// ═══════════════════════════════════════════════════════════

const CONTROLS_CONFIG = {
  // ── 移動與角色動作 ──
  MOVE_UP:      { keys: ['w', 'ArrowUp'], desc: '向上移動' },
  MOVE_DOWN:    { keys: ['s', 'ArrowDown'], desc: '向下移動' },
  MOVE_LEFT:    { keys: ['a', 'ArrowLeft'], desc: '向左移動' },
  MOVE_RIGHT:   { keys: ['d', 'ArrowRight'], desc: '向右移動' },
  ATTACK:       { keys: ['j', ' '], desc: '攻擊 / 蓄力招式' },
  DASH:         { keys: ['Shift', 'l'], desc: '御劍閃避衝刺' },
  MEDITATE:     { keys: ['k'], desc: '靜心打坐吐納' },

  // ── 專屬互動與功能（嚴禁按鍵重疊）──
  INTERACT:     { keys: ['f'], desc: 'NPC 對話 / 互動 / 推進對話（專用 F 鍵）' },
  OPEN_FORGE:   { keys: ['g'], desc: '神兵煉器（專用 G 鍵，已移除重疊之 F 鍵）' },
  OPEN_ALCHEMY: { keys: ['v'], desc: '九轉煉丹（專用 V 鍵）' },

  // ── 介面與選單 ──
  OPEN_INV:     { keys: ['i', 'b', '7'], desc: '開啟儲物袋' },
  OPEN_CHAR:    { keys: ['c', 'u', '8'], desc: '開啟人物資訊 / 功法面板' },
  OPEN_MAP:     { keys: ['m', '9'], desc: '開啟界域全圖' },
  OPEN_HELP:    { keys: ['?'], desc: '開啟操作說明' },
  CLOSE_ALL:    { keys: ['Escape'], desc: '退出 / 關閉所有彈窗（ESC 鍵）' },

  // ── 快捷裝備與丹藥 ──
  WEAPON_1:     { keys: ['1'], desc: '快捷切換【精鋼長劍】 (近戰 1 格)' },
  WEAPON_2:     { keys: ['2'], desc: '快捷切換【赤焰長槍】 (貫穿 3 格)' },
  WEAPON_3:     { keys: ['3'], desc: '快捷切換【寒冰飛鏢】 (遠程 10 格)' },
  QUICK_HP:     { keys: ['4'], desc: '快捷服用金創丹 (恢復氣血)' },
  QUICK_QI:     { keys: ['5'], desc: '快捷服用聚靈丹 (恢復靈力)' },
  QUICK_BREAK:  { keys: ['6'], desc: '快捷服用破境丹 (提升經驗)' },
};

window.CONTROLS_CONFIG = CONTROLS_CONFIG;
