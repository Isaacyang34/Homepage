extends Node
# 遊戲總管理器 (GameManager)
# 負責暫停、遊戲狀態、關卡切換與存讀檔

var is_paused: bool = false
var current_level_path: String = "res://src/level/MainLevel.tscn"

# 玩家全域統計資產 (嚴格從 0 開始)
var qi_stones: int = 0  # 靈石數量
var defeated_enemies_count: int = 0  # 擊敗妖獸數量

const SAVE_PATH: String = "user://cultivation_save.json"

func _ready() -> void:
	EventBus.enemy_died.connect(_on_enemy_died)

func toggle_pause() -> void:
	is_paused = !is_paused
	get_tree().paused = is_paused
	EventBus.game_paused.emit(is_paused)

func add_qi_stones(amount: int) -> void:
	if amount > 0:
		qi_stones += amount

func _on_enemy_died(_enemy_name: String, _exp_reward: float, qi_stones_reward: int) -> void:
	defeated_enemies_count += 1
	add_qi_stones(qi_stones_reward)

func save_game(player_data: Dictionary) -> bool:
	var save_dict = {
		"qi_stones": qi_stones,
		"defeated_enemies_count": defeated_enemies_count,
		"player_data": player_data,
		"current_level": current_level_path,
		"save_time": Time.get_datetime_string_from_system()
	}
	var file = FileAccess.open(SAVE_PATH, FileAccess.WRITE)
	if file:
		var json_string = JSON.stringify(save_dict, "\t")
		file.store_string(json_string)
		file.close()
		return true
	return false

func load_game() -> Dictionary:
	if not FileAccess.file_exists(SAVE_PATH):
		return {}
	var file = FileAccess.open(SAVE_PATH, FileAccess.READ)
	if file:
		var json_string = file.get_as_text()
		file.close()
		var json = JSON.new()
		var parse_result = json.parse(json_string)
		if parse_result == OK:
			var data = json.get_data()
			qi_stones = data.get("qi_stones", 0)
			defeated_enemies_count = data.get("defeated_enemies_count", 0)
			return data
	return {}
