class_name CollectionOperations
extends RefCounted

class CustomValue:
	func size() -> int:
		return 4

	func remove_at(index: int) -> int:
		return index

var Values := [1, 2, 3]
var CustomValues : Array[CustomValue]
var Entries : Dictionary[String, int]
var Custom : CustomValue

func inspect():
	var valueCount : int = Values.size()
	var entryCount : int = Entries.size()
	var valuesEmpty : bool = Values.is_empty()
	var entriesEmpty : bool = Entries.is_empty()
	var valuesPresent : bool = not Values.is_empty()

	var localValues := Values
	var selectedValue : CustomValue = CustomValues[0]
	var createdValue := CustomValue.new()
	localValues.append(4)
	localValues.remove_at(0)
	Values.clear()
	Entries.clear()
	Values.erase(2)
	Entries.erase("second")

	var customSize : int = Custom.size()
	Custom.remove_at(0)

func removeItem(values: Array[CustomValue], index: int):
	values.remove_at(index)

func contains(values: Array[int], entries: Dictionary[String, int]) -> bool:
	return values.has(1) and entries.has("first")
