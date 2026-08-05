extends Node2D
# 靈氣濃度感應區域 (QiDensityZone.gd)
# 玩家進入此區域時，CultivationManager 的 env_qi_density 會切換

@export var qi_density: float = 3.0  # 洞府聚靈陣：3.0x
@export var zone_name: String = "聚靈陣"

@onready var area: Area2D = $Area2D

var _original_density: float = 1.0

func _ready() -> void:
	area.body_entered.connect(_on_body_entered)
	area.body_exited.connect(_on_body_exited)

func _on_body_entered(body: Node2D) -> void:
	if body.is_in_group("player") and CultivationManager:
		_original_density = CultivationManager.env_qi_density
		CultivationManager.env_qi_density = qi_density
		EventBus.dialogue_triggered.emit(zone_name, "此地靈氣充沛（%0.1fx）！打坐效率大幅提升。" % qi_density)

func _on_body_exited(body: Node2D) -> void:
	if body.is_in_group("player") and CultivationManager:
		CultivationManager.env_qi_density = _original_density
