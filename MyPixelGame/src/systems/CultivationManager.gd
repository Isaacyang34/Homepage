extends Node
# 修仙系統管理器 (CultivationManager)
# 負責修為累積、打坐吐納、突破判定與天劫雷罰機制

# 修仙境界架構資料
var realms: Array[Dictionary] = [
	{"name": "練氣期一重", "max_qi": 100.0, "hp_bonus": 100.0, "attack_bonus": 10.0, "def_bonus": 2.0, "base_success": 1.0, "requires_tribulation": false},
	{"name": "練氣期二重", "max_qi": 250.0, "hp_bonus": 130.0, "attack_bonus": 14.0, "def_bonus": 3.0, "base_success": 0.95, "requires_tribulation": false},
	{"name": "練氣期三重", "max_qi": 500.0, "hp_bonus": 170.0, "attack_bonus": 19.0, "def_bonus": 5.0, "base_success": 0.90, "requires_tribulation": false},
	{"name": "練氣期四重", "max_qi": 900.0, "hp_bonus": 220.0, "attack_bonus": 25.0, "def_bonus": 7.0, "base_success": 0.85, "requires_tribulation": false},
	{"name": "練氣期五重", "max_qi": 1500.0, "hp_bonus": 280.0, "attack_bonus": 32.0, "def_bonus": 10.0, "base_success": 0.80, "requires_tribulation": false},
	{"name": "練氣期六重", "max_qi": 2300.0, "hp_bonus": 350.0, "attack_bonus": 40.0, "def_bonus": 13.0, "base_success": 0.75, "requires_tribulation": false},
	{"name": "練氣期七重", "max_qi": 3300.0, "hp_bonus": 430.0, "attack_bonus": 50.0, "def_bonus": 17.0, "base_success": 0.70, "requires_tribulation": false},
	{"name": "練氣期八重", "max_qi": 4500.0, "hp_bonus": 520.0, "attack_bonus": 62.0, "def_bonus": 21.0, "base_success": 0.65, "requires_tribulation": false},
	{"name": "練氣期九重", "max_qi": 6000.0, "hp_bonus": 630.0, "attack_bonus": 76.0, "def_bonus": 26.0, "base_success": 0.60, "requires_tribulation": true}, # 突破至築基觸發天劫
	{"name": "築基期前期", "max_qi": 12000.0, "hp_bonus": 1000.0, "attack_bonus": 120.0, "def_bonus": 45.0, "base_success": 0.40, "requires_tribulation": false}
]

var current_realm_index: int = 0
var current_qi: float = 0.0
var env_qi_density: float = 1.0  # 環境靈氣濃度 (1.0x ~ 5.0x)

# 天劫狀態
var is_in_tribulation: bool = false
var tribulation_wave: int = 0
var total_tribulation_waves: int = 3
var tribulation_timer: Timer

func _ready() -> void:
	tribulation_timer = Timer.new()
	tribulation_timer.one_shot = true
	tribulation_timer.timeout.connect(_on_tribulation_timer_timeout)
	add_child(tribulation_timer)

func get_current_realm() -> Dictionary:
	return realms[current_realm_index]

func absorb_qi(delta: float, is_meditating: bool = false) -> void:
	if is_in_tribulation:
		return
	var base_rate: float = 2.0 if not is_meditating else 15.0
	var gained_qi: float = base_rate * env_qi_density * delta
	var max_qi: float = realms[current_realm_index]["max_qi"]
	current_qi = min(current_qi + gained_qi, max_qi)
	EventBus.player_qi_changed.emit(current_qi, max_qi)

func can_breakthrough() -> bool:
	return current_qi >= realms[current_realm_index]["max_qi"] and current_realm_index < realms.size() - 1

func attempt_breakthrough() -> void:
	if not can_breakthrough():
		EventBus.breakthrough_result.emit(false, "靈氣不足，無法嘗試突破！")
		return

	var realm = get_current_realm()
	EventBus.breakthrough_started.emit()

	if realm["requires_tribulation"]:
		start_tribulation()
	else:
		_process_breakthrough_chance(realm["base_success"])

func _process_breakthrough_chance(success_rate: float) -> void:
	var roll = randf()
	if roll <= success_rate:
		current_realm_index += 1
		current_qi = 0.0
		var new_realm = get_current_realm()
		EventBus.realm_changed.emit(new_realm["name"], current_realm_index)
		EventBus.breakthrough_result.emit(true, "破境成功！已晉升至 " + new_realm["name"])
	else:
		current_qi *= 0.7  # 突破失敗靈氣倒退 30%
		EventBus.breakthrough_result.emit(false, "心魔干擾，突破失敗！靈氣受損。")
	
	EventBus.player_qi_changed.emit(current_qi, realms[current_realm_index]["max_qi"])

# --- 天劫系統 ---
func start_tribulation() -> void:
	is_in_tribulation = true
	tribulation_wave = 0
	EventBus.breakthrough_result.emit(false, "感應天道，九天雷劫降臨！")
	tribulation_timer.start(2.0) # 2秒後降下第一波雷劫

func _on_tribulation_timer_timeout() -> void:
	if not is_in_tribulation:
		return

	tribulation_wave += 1
	var damage: float = 40.0 + tribulation_wave * 20.0  # 雷劫神聖傷害
	EventBus.tribulation_wave_triggered.emit(tribulation_wave, total_tribulation_waves, damage)

	if tribulation_wave < total_tribulation_waves:
		tribulation_timer.start(2.5) # 下一波雷劫間隔
	else:
		is_in_tribulation = false
		EventBus.tribulation_ended.emit(true)
		_process_breakthrough_chance(0.9)  # 成功扛過雷劫大幅提升突破率
