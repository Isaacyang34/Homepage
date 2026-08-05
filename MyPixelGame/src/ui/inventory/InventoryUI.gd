extends Control
# 修仙背包介面 (InventoryUI.gd)

@onready var grid: GridContainer = $Panel/Scroll/Grid
@onready var item_desc: Label = $Panel/DescLabel
@onready var close_btn: Button = $Panel/CloseButton
@onready var panel: PanelContainer = $Panel

var inv_manager: Node  # 指向 InventoryManager

func _ready() -> void:
	close_btn.pressed.connect(func(): panel.hide())
	EventBus.item_collected.connect(func(_id, _qty): refresh())
	panel.hide()

func open() -> void:
	refresh()
	panel.show()

func refresh() -> void:
	for child in grid.get_children():
		child.queue_free()

	if not inv_manager:
		return

	for slot in inv_manager.slots:
		var btn := Button.new()
		btn.text = "%s\n×%d" % [slot["item"].item_name, slot["qty"]]
		btn.tooltip_text = slot["item"].description
		btn.pressed.connect(func(): _use_slot(slot["item"].item_id))
		grid.add_child(btn)

func _use_slot(item_id: String) -> void:
	var player = get_tree().get_first_node_in_group("player")
	if player and inv_manager:
		inv_manager.use_item(item_id, player)
		refresh()
