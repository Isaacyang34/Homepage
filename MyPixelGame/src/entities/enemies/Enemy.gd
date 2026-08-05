extends CharacterBody2D
# 妖獸 AI 控制器 (Enemy.gd)

enum State { PATROL, CHASE, ATTACK, HURT, DIE }

@export var enemy_name: String = "赤焰野狼"
@export var move_speed: float = 60.0
@export var chase_speed: float = 90.0
@export var max_hp: float = 60.0
@export var attack_damage: float = 12.0
@export var qi_stones_reward: int = 15

var current_hp: float = 60.0
var current_state: State = State.PATROL
var target_player: Node2D = null

func _ready() -> void:
	add_to_group("enemies")
	current_hp = max_hp

func _physics_process(delta: float) -> void:
	match current_state:
		State.PATROL:
			_handle_patrol(delta)
		State.CHASE:
			_handle_chase(delta)

	move_and_slide()

func _handle_patrol(_delta: float) -> void:
	velocity = Vector2.ZERO
	# 偵測周圍玩家
	var players = get_tree().get_nodes_in_group("player")
	if players.size() > 0:
		var p = players[0] as Node2D
		if global_position.distance_to(p.global_position) < 120.0:
			target_player = p
			current_state = State.CHASE

func _handle_chase(_delta: float) -> void:
	if not is_instance_valid(target_player):
		current_state = State.PATROL
		return

	var dist = global_position.distance_to(target_player.global_position)
	if dist > 180.0:
		target_player = null
		current_state = State.PATROL
	else:
		var dir = (target_player.global_position - global_position).normalized()
		velocity = dir * chase_speed

		# 攻擊距離
		if dist < 20.0:
			_attack_player()

func _attack_player() -> void:
	if target_player and target_player.has_method("take_damage"):
		target_player.take_damage(attack_damage)

func take_damage(damage: float) -> void:
	if current_state == State.DIE:
		return
	current_hp -= damage
	current_state = State.HURT

	if current_hp <= 0.0:
		current_state = State.DIE
		EventBus.enemy_died.emit(enemy_name, 50.0, qi_stones_reward)
		queue_free()
	else:
		await get_tree().create_timer(0.2).timeout
		if current_state == State.HURT:
			current_state = State.CHASE
