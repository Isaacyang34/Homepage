extends Node2D
# 主關卡地圖完整邏輯 (MainLevel.gd)

@export var level_name: String = "青雲宗 - 外門靈山"
@export var qi_density: float = 1.5  # 環境靈氣濃度

@onready var player: CharacterBody2D = $Player
@onready var enemy_container: Node2D = $EnemyContainer
@onready var hud: Control = $HUD
@onready var portal_area: Area2D = $PortalArea

const ENEMY_SCENE = preload("res://src/entities/enemies/Enemy.gd")

func _ready() -> void:
	print("關卡載入成功：" + level_name)
	if CultivationManager:
		CultivationManager.env_qi_density = qi_density
	_spawn_initial_enemies()
	EventBus.enemy_died.connect(_on_enemy_died)

func _spawn_initial_enemies() -> void:
	var spawn_points = [
		Vector2(320, 80),
		Vector2(150, 200),
		Vector2(400, 60),
	]
	for sp in spawn_points:
		_spawn_enemy(sp)

func _spawn_enemy(pos: Vector2) -> void:
	var e = CharacterBody2D.new()
	e.set_script(ENEMY_SCENE)
	e.global_position = pos
	enemy_container.add_child(e)

func _on_enemy_died(enemy_name: String, _exp: float, stones: int) -> void:
	print("%s 被擊敗，獲得 %d 靈石" % [enemy_name, stones])
	# 延遲重生
	await get_tree().create_timer(12.0).timeout
	_spawn_enemy(Vector2(
		randf_range(60, 420),
		randf_range(40, 230)
	))
