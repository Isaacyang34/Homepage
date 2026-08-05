extends Node
# 全域事件總線 (EventBus)
# 採用發佈/訂閱模式解耦各系統之間的溝通

# --- 玩家狀態訊號 ---
signal player_hp_changed(current_hp: float, max_hp: float)
signal player_qi_changed(current_qi: float, max_qi: float)
signal player_died()
signal player_state_changed(new_state: String)

# --- 修仙與渡劫訊號 ---
signal realm_changed(new_realm_name: String, realm_index: int)
signal breakthrough_started()
signal breakthrough_result(success: bool, message: String)
signal tribulation_wave_triggered(wave_index: int, total_waves: int, damage: float)
signal tribulation_ended(success: bool)

# --- 戰鬥與掉落訊號 ---
signal entity_damaged(target: Node, damage: float, is_critical: bool)
signal enemy_died(enemy_name: String, exp_reward: float, qi_stones_reward: int)
signal item_collected(item_id: String, amount: int)

# --- 遊戲流程與介面訊號 ---
signal game_paused(is_paused: bool)
signal dialogue_triggered(speaker_name: String, dialogue_text: String)
