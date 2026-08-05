extends CharacterBody2D
# 修仙 NPC 基底 (Npc.gd)
class_name Npc

@export var npc_name: String = "無名仙人"
@export var dialogues: Array[String] = []
@export var is_merchant: bool = false

var dialogue_index: int = 0
var player_nearby: bool = false

@onready var interact_area: Area2D = $InteractArea
@onready var label: Label = $NameLabel

func _ready() -> void:
	label.text = npc_name
	interact_area.body_entered.connect(_on_body_entered)
	interact_area.body_exited.connect(_on_body_exited)

func _input(event: InputEvent) -> void:
	if player_nearby and event.is_action_just_pressed("interact"):
		speak()

func speak() -> void:
	if dialogues.is_empty():
		return
	var line = dialogues[dialogue_index % dialogues.size()]
	EventBus.dialogue_triggered.emit(npc_name, line)
	dialogue_index = (dialogue_index + 1) % dialogues.size()

func _on_body_entered(body: Node2D) -> void:
	if body.is_in_group("player"):
		player_nearby = true

func _on_body_exited(body: Node2D) -> void:
	if body.is_in_group("player"):
		player_nearby = false
