class_name DictionaryExpressions
extends RefCounted

class Item:
	var Count : int

const Defaults : Dictionary[String, bool] = {"enabled": true}

var Empty : Dictionary[String, int] = {}
var Inline : Dictionary[String, Variant] = {"count": 2, "label": "value"}
var Nodes : Dictionary[String, Node3D]
var ObjectEntries : Array[Dictionary] #[String, Node3D]
var MixedEntries : Array[Dictionary] #[String, Variant]
var CustomEntries : Array[Dictionary] #[String, DictionaryExpressions.Item]
var Items : Array #[DictionaryExpressions.Item]

func record(node: Node3D, size: Vector3):
	var entry : Dictionary[String, Variant] = {
		"node": node,
		"size": size,
	}

func inspect():
	var pendingItems : Array #[DictionaryExpressions.Item]
	var pending : Array[Dictionary] #[String, Node3D]
	var pendingCounts : Dictionary[String, int]
	var compact : Dictionary[String, Variant] = {"enabled": true, "offset": Vector3(1.0, 2.0, 3.0)}
	var nested : Dictionary[String, Variant] = {"entry": compact}
	var mixed : Dictionary[String, int] = {"first": 1,
		"second": 2, "third": 3,

		"last": 4
	}
	var empty : Dictionary[String, int] = {

	}

func parentOf(entry: Dictionary[String, Variant]) -> Node:
	var item := entry["node"] as Node
	if is_instance_valid(item):
		return item.get_parent()
	return null

func offsetOf(entry: Dictionary[String, Variant]) -> Vector3:
	return entry["offset"] as Vector3

func inlineParent(entry: Dictionary[String, Variant]) -> Node:
	return (entry["node"] as Node).get_parent()

func valid(entry: Dictionary[String, Variant]) -> bool:
	return is_instance_valid(entry["node"] as Node)

func typedNode(entry: Dictionary[String, Node]) -> Node:
	return entry["node"]

func countOf(entry: Dictionary[String, int]) -> int:
	return entry["count"]

func store(entry: Dictionary[String, Variant], node: Node):
	entry["node"] = node

func hintedItems(callback: Callable) -> int:
	var items := callback.call() as Array #[DictionaryExpressions.Item]
	return items.size()

func mixedEntries() -> Array[Dictionary]: #[String, Variant]
	return MixedEntries

func items() -> Array: #[DictionaryExpressions.Item]
	return Items
