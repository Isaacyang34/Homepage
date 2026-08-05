extends CharacterBody2D
# 精英妖獸 Boss (EliteEnemy.gd) - 繼承一般 Enemy，強化 AI

extends "res://src/entities/enemies/Enemy.gd"

enum BossPhase { PHASE1, PHASE2 }
var phase: BossPhase = BossPhase.PHASE1
var skill_cooldown: float = 0.0

func _ready() -> void:
	super._ready()
	enemy_name = "玄鐵魔熊 · 首領"
	max_hp = 300.0
	current_hp = max_hp
	attack_damage = 25.0
	qi_stones_reward = 80

func _physics_process(delta: float) -> void:
	skill_cooldown = max(0.0, skill_cooldown - delta)
	# 切換 Phase
	if current_hp < max_hp * 0.5 and phase == BossPhase.PHASE1:
		phase = BossPhase.PHASE2
		move_speed *= 1.4
		EventBus.dialogue_triggered.emit(enemy_name, "!!嚎──！（狂暴！速度大幅提升！）")

	# Phase2 技能：範圍衝擊
	if phase == BossPhase.PHASE2 and skill_cooldown <= 0.0:
		_skill_slam()
		skill_cooldown = 4.0

	super._physics_process(delta)

func _skill_slam() -> void:
	if not is_instance_valid(target_player):
		return
	if global_position.distance_to(target_player.global_position) < 50.0:
		target_player.take_damage(attack_damage * 1.8)
		EventBus.dialogue_triggered.emit(enemy_name, "⚡ 震地掌！")
