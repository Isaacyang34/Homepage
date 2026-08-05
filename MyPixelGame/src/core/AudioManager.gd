extends Node
# 音效與背景音樂管理器 (AudioManager)

@onready var bgm_player: AudioStreamPlayer = AudioStreamPlayer.new()
@onready var sfx_player: AudioStreamPlayer = AudioStreamPlayer.new()

var master_volume: float = 1.0
var bgm_volume: float = 0.8
var sfx_volume: float = 1.0

func _ready() -> void:
	add_child(bgm_player)
	add_child(sfx_player)
	bgm_player.bus = &"Master"
	sfx_player.bus = &"Master"

func play_bgm(stream: AudioStream) -> void:
	if bgm_player.stream == stream and bgm_player.playing:
		return
	bgm_player.stream = stream
	bgm_player.volume_db = linear_to_db(bgm_volume * master_volume)
	bgm_player.play()

func play_sfx(stream: AudioStream) -> void:
	if stream == null:
		return
	sfx_player.stream = stream
	sfx_player.volume_db = linear_to_db(sfx_volume * master_volume)
	sfx_player.play()

func stop_bgm() -> void:
	bgm_player.stop()
