extends CharacterBody2D
# 修仙玩家完整版 (Player.gd) ─ 包含治療、悟道 Exp、統計與完整狀態機

enum State { IDLE, MOVE, ATTACK, MEDITATE, DASH, HURT, DIE }

@export var move_speed: float = 120.0
@export var dash_speed: float = 260.0

var current_hp: float = 100.0
var max_hp: float = 100.0
var exp: float = 0.0
var stones: int = 0
var kills: int = 0

var current_state: State = State.IDLE
var facing_direction: Vector2 = Vector2.DOWN
var is_invulnerable: bool = false
var hurt_timer: float = 0.0

const SWORD_QI_SCENE := preload("res://src/entities/projectiles/SwordQi.gd")

func _ready() -> void:
	add_to_group("player")
	current_hp = max_hp
	EventBus.player_hp_changed.emit(current_hp, max_hp)
	EventBus.tribulation_wave_triggered.connect(_on_tribulation_wave)
	EventBus.enemy_died.connect(_on_enemy_died)

func _physics_process(delta: float) -> void:
	hurt_timer = max(0.0, hurt_timer - delta)

	match current_state:
		State.IDLE, State.MOVE:
			_handle_movement()
			_check_actions()
		State.MEDITATE:
			velocity = Vector2.ZERO
			CultivationManager.absorb_qi(delta, true)
			var input_dir = Input.get_vector("ui_left","ui_right","ui_up","ui_down")
			if input_dir != Vector2.ZERO or Input.is_action_just_pressed("interact"):
				_change_state(State.IDLE)
		State.ATTACK, State.DASH, State.HURT:
			pass
		State.DIE:
			velocity = Vector2.ZERO

	move_and_slide()

func _handle_movement() -> void:
	var input_dir = Input.get_vector("ui_left","ui_right","ui_up","ui_down")
	if input_dir != Vector2.ZERO:
		velocity = input_dir * move_speed
		facing_direction = input_dir.normalized()
		_change_state(State.MOVE)
	else:
		velocity = Vector2.ZERO
		_change_state(State.IDLE)

func _check_actions() -> void:
	if Input.is_action_just_pressed("attack"):
		_perform_attack()
	elif Input.is_action_just_pressed("meditate"):
		_change_state(State.MEDITATE)
	elif Input.is_action_just_pressed("dash") and velocity != Vector2.ZERO:
		_perform_dash()

func _perform_attack() -> void:
	_change_state(State.ATTACK)
	velocity = Vector2.ZERO
	var sq = Area2D.new()
	sq.set_script(SWORD_QI_SCENE)
	sq.position = global_position + facing_direction * 16.0
	get_parent().add_child(sq)
	sq.call("set_direction", facing_direction)
	await get_tree().create_timer(0.3).timeout
	if current_state == State.ATTACK:
		_change_state(State.IDLE)

func _perform_dash() -> void:
	_change_state(State.DASH)
	velocity = facing_direction * dash_speed
	is_invulnerable = true
	await get_tree().create_timer(0.25).timeout
	is_invulnerable = false
	if current_state == State.DASH:
		_change_state(State.IDLE)

func take_damage(damage: float) -> void:
	if is_invulnerable or current_state == State.DIE:
		return
	var real_dmg = max(1.0, damage - _get_defense())
	current_hp = max(0.0, current_hp - real_dmg)
	hurt_timer = 0.2
	is_invulnerable = true
	await get_tree().create_timer(0.5).timeout
	is_invulnerable = false
	EventBus.player_hp_changed.emit(current_hp, max_hp)
	EventBus.entity_damaged.emit(self, real_dmg, false)
	if current_hp <= 0.0:
		_change_state(State.DIE)
		EventBus.player_died.emit()
		_handle_death()
	else:
		_change_state(State.HURT)
		await get_tree().create_timer(0.25).timeout
		if current_state == State.HURT:
			_change_state(State.IDLE)

func heal(amount: float) -> void:
	current_hp = min(max_hp, current_hp + amount)
	EventBus.player_hp_changed.emit(current_hp, max_hp)

func _get_defense() -> float:
	if CultivationManager:
		return CultivationManager.get_current_realm().get("def_bonus", 0.0)
	return 0.0

func _on_tribulation_wave(_wave: int, _total: int, damage: float) -> void:
	current_hp = max(5.0, current_hp - damage)
	EventBus.player_hp_changed.emit(current_hp, max_hp)

func _on_enemy_died(_name: String, exp_reward: float, stones_reward: int) -> void:
	exp += exp_reward
	stones += stones_reward
	kills += 1
	_check_realm_exp()

func _check_realm_exp() -> void:
	if not CultivationManager:
		return
	var r = CultivationManager.get_current_realm()
	if exp >= r.get("exp_to_next", 9999) and CultivationManager.can_breakthrough():
		CultivationManager.current_qi = CultivationManager.get_current_realm()["max_qi"]
		EventBus.player_qi_changed.emit(
			CultivationManager.current_qi,
			CultivationManager.get_current_realm()["max_qi"]
		)

func _handle_death() -> void:
	# 隕落處理：2秒後重生回練氣一重
	await get_tree().create_timer(2.0).timeout
	CultivationManager.current_realm_index = 0
	CultivationManager.current_qi = 0.0
	var r = CultivationManager.get_current_realm()
	max_hp = r["hp_bonus"]; current_hp = max_hp
	EventBus.player_hp_changed.emit(current_hp, max_hp)
	EventBus.realm_changed.emit(r["name"], 0)
	global_position = Vector2(240, 135)
	_change_state(State.IDLE)

func _change_state(new_state: State) -> void:
	if current_state != new_state:
		current_state = new_state
		EventBus.player_state_changed.emit(State.keys()[new_state])
