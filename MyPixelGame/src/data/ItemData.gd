extends Resource
# 道具資料資源 (ItemData.gd)
class_name ItemData

enum ItemType { CONSUMABLE, MATERIAL, EQUIPMENT, SKILL_BOOK }

@export var item_id: String = ""
@export var item_name: String = ""
@export var icon: String = ""
@export var description: String = ""
@export var item_type: ItemType = ItemType.CONSUMABLE
@export var hp_restore: float = 0.0
@export var qi_restore: float = 0.0
@export var breakthrough_bonus: float = 0.0
