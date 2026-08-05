extends Control
# 修仙 NPC 對話控制器 (DialogueUI.gd)

@onready var panel: PanelContainer = $Panel
@onready var speaker_label: Label = $Panel/VBox/SpeakerLabel
@onready var text_label: Label = $Panel/VBox/TextLabel
@onready var next_btn: Button = $Panel/VBox/NextButton

var current_dialogues: Array[String] = []
var dialogue_idx: int = 0

func _ready() -> void:
	EventBus.dialogue_triggered.connect(_on_dialogue_triggered)
	panel.hide()
	next_btn.pressed.connect(_on_next_pressed)

func _on_dialogue_triggered(speaker: String, text: String) -> void:
	speaker_label.text = speaker
	text_label.text = text
	panel.show()

func _on_next_pressed() -> void:
	panel.hide()
