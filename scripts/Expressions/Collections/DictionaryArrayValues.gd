class_name DictionaryArrayValues
extends RefCounted

# The hint supplies the element type of each array stored as a dictionary value.
var Groups : Dictionary[Vector2, Array] #[float]
var Counts : Dictionary[String, Array] #[int]

func add(key: Vector2, value: float):
	if not Groups.has(key):
		var values : Array[float]
		Groups[key] = values
	Groups[key].append(value)

func copy():
	var pending : Dictionary[String, Array] #[int]
	pending = Counts

func first(callback: Callable, key: Vector2) -> float:
	var groups := callback.call() as Dictionary[Vector2, Array] #[float]
	return groups[key][0]

func groups() -> Dictionary[Vector2, Array]: #[float]
	return Groups
