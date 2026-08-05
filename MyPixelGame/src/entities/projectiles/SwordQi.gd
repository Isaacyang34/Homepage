extends Area2D
# 劍氣彈幕實體 (SwordQi)

@export var speed: float = 300.0
@export var lifetime: float = 1.5
@export var damage: float = 25.0

var direction: Vector2 = Vector2.RIGHT

func _ready() -> void:
	body_entered.connect(_on_body_entered)
	var timer = get_tree().create_timer(lifetime)
	timer.timeout.connect(queue_free)

func _physics_process(delta: float) -> void:
	position += direction * speed * delta

func set_direction(dir: Vector2) -> void:
	direction = dir.normalized()
	rotation = direction.angle()

func _on_body_entered(body: Node2D) -> void:
	if body.is_in_group("enemies"):
		if body.has_method("take_damage"):
			body.take_damage(damage)
		EventBus.entity_damaged.emit(body, damage, false)
		queue_free()
	elif not body.is_in_group("player"):
		queue_free() # 撞擊牆壁/障礙物銷毀
