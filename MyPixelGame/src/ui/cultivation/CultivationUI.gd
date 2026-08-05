extends Control
# 打坐突破介面 (CultivationUI.gd)

@onready var panel: PanelContainer = $Panel
@onready var realm_label: Label = $Panel/VBox/RealmLabel
@onready var progress_bar: ProgressBar = $Panel/VBox/QiBar
@onready var breakthrough_btn: Button = $Panel/VBox/BreakthroughButton
@onready var message_label: Label = $Panel/VBox/MessageLabel

func _ready() -> void:
	EventBus.player_qi_changed.connect(_on_qi_changed)
	EventBus.realm_changed.connect(_on_realm_changed)
	EventBus.breakthrough_result.connect(_on_breakthrough_result)
	EventBus.tribulation_wave_triggered.connect(_on_tribulation_wave)
	breakthrough_btn.pressed.connect(_on_breakthrough_pressed)

	if CultivationManager:
		var r = CultivationManager.get_current_realm()
		realm_label.text = r["name"]

func _on_qi_changed(current: float, max_val: float) -> void:
	progress_bar.max_value = max_val
	progress_bar.value = current
	breakthrough_btn.visible = CultivationManager and CultivationManager.can_breakthrough()

func _on_realm_changed(realm_name: String, _idx: int) -> void:
	realm_label.text = realm_name

func _on_breakthrough_pressed() -> void:
	if CultivationManager:
		CultivationManager.attempt_breakthrough()

func _on_breakthrough_result(success: bool, msg: String) -> void:
	message_label.text = msg
	message_label.modulate = Color.GREEN if success else Color.RED

func _on_tribulation_wave(wave: int, total: int, damage: float) -> void:
	message_label.text = "⚡ 天劫第 %d / %d 波！傷害 %.0f" % [wave, total, damage]
	message_label.modulate = Color.YELLOW
