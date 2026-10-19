class_name LocalVariables
extends RefCounted

class Element:
	var Value : int

func evaluate(value: int) -> int:
	var pending : int
	var pendingValues : Array[int]
	pendingValues.append(value)
	var first : int = value
	var result := first
	return result

func inspect(value: LocalVariables.Element, values: Array[LocalVariables.Element], groups: Dictionary[String, Array[LocalVariables.Element]]):
	var selected : LocalVariables.Element
	selected = value
	var localValues : Array[LocalVariables.Element] = values
	var localGroups : Dictionary[String, Array[LocalVariables.Element]] = groups
	var item : LocalVariables.Element = values[0]
	var supplied : LocalVariables.Element = LocalVariables.getElements(values)[0]
	var inferred := firstValue()
	localValues.clear()
	localGroups.clear()

func firstValue() -> int:
	return 1

static func getElements(values: Array[LocalVariables.Element]) -> Array: #[LocalVariables.Element]
	return values
