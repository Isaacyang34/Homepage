extends Node
# 背包系統 (InventoryManager.gd)

# 儲物袋：每格存 { item: ItemData, qty: int }
var slots: Array = []
const MAX_SLOTS: int = 30

func add_item(item: Resource, qty: int = 1) -> bool:
	# 先找已有的堆疊格
	for s in slots:
		if s["item"].item_id == item.item_id:
			s["qty"] += qty
			EventBus.item_collected.emit(item.item_id, qty)
			return true
	# 新格子
	if slots.size() >= MAX_SLOTS:
		return false
	slots.append({"item": item, "qty": qty})
	EventBus.item_collected.emit(item.item_id, qty)
	return true

func remove_item(item_id: String, qty: int = 1) -> bool:
	for i in range(slots.size()):
		if slots[i]["item"].item_id == item_id:
			slots[i]["qty"] -= qty
			if slots[i]["qty"] <= 0:
				slots.remove_at(i)
			return true
	return false

func has_item(item_id: String) -> bool:
	return slots.any(func(s): return s["item"].item_id == item_id)

func use_item(item_id: String, target: Node) -> bool:
	for s in slots:
		if s["item"].item_id == item_id:
			var item: Resource = s["item"]
			if item.hp_restore > 0 and target.has_method("heal"):
				target.heal(item.hp_restore)
			if item.qi_restore > 0 and CultivationManager:
				CultivationManager.current_qi = minf(
					CultivationManager.current_qi + item.qi_restore,
					CultivationManager.get_current_realm()["max_qi"]
				)
			remove_item(item_id)
			return true
	return false
