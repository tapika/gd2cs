class_name LocalVariables
extends RefCounted

class Element:
	var Value : int

func evaluate(value: int) -> int:
	var pending : int
	var first : int = value
	var result := first
	return result

func inspect(value: LocalVariables.Element, values: Array[LocalVariables.Element], groups: Dictionary[String, Array[LocalVariables.Element]]):
	var selected : LocalVariables.Element
	selected = value
	var localValues : Array[LocalVariables.Element] = values
	var localGroups : Dictionary[String, Array[LocalVariables.Element]] = groups
	localValues.clear()
	localGroups.clear()
