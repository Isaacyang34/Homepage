extends Control
# 像素風格修仙 HUD 介面控制器 (HUD.gd)

@onready var hp_bar: TextureProgressBar = $HPBar
@onready var qi_bar: TextureProgressBar = $QiBar
@onready var realm_label: Label = $RealmLabel
@onready var qi_stones_label: Label = $QiStonesLabel
@onready var status_label: Label = $StatusLabel
@onready var breakthrough_btn: Button = $BreakthroughButton

func _ready() -> void:
	EventBus.player_hp_changed.connect(_on_hp_changed)
	EventBus.player_qi_changed.connect(_on_qi_changed)
	EventBus.realm_changed.connect(_on_realm_changed)
	EventBus.player_state_changed.connect(_on_state_changed)
	EventBus.breakthrough_result.connect(_on_breakthrough_result)

	if CultivationManager:
		var r = CultivationManager.get_current_realm()
		realm_label.text = r["name"]

	breakthrough_btn.pressed.connect(_on_breakthrough_pressed)

func _on_hp_changed(current: float, max_val: float) -> void:
	if hp_bar:
		hp_bar.max_value = max_val
		hp_bar.value = current

func _on_qi_changed(current: float, max_val: float) -> void:
	if qi_bar:
		qi_bar.max_value = max_val
		qi_bar.value = current
	if breakthrough_btn and CultivationManager:
		breakthrough_btn.visible = CultivationManager.can_breakthrough()

func _on_realm_changed(new_name: String, _idx: int) -> void:
	if realm_label:
		realm_label.text = new_name

func _on_state_changed(state_str: String) -> void:
	if status_label:
		status_label.text = "狀態: " + state_str

func _on_breakthrough_pressed() -> void:
	if CultivationManager:
		CultivationManager.attempt_breakthrough()

func _on_breakthrough_result(_success: bool, msg: String) -> void:
	if status_label:
		status_label.text = msg
