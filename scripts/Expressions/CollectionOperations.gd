class_name CollectionOperations
extends RefCounted

class CustomValue:
	func size() -> int:
		return 4

var Values := [1, 2, 3]
var Entries : Dictionary[String, int]
var Custom : CustomValue

func inspect():
	var valueCount : int = Values.size()
	var entryCount : int = Entries.size()
	var valuesEmpty : bool = Values.is_empty()
	var entriesEmpty : bool = Entries.is_empty()
	var valuesPresent : bool = not Values.is_empty()

	var localValues := Values
	localValues.append(4)
	Values.clear()
	Entries.clear()
	Values.erase(2)
	Entries.erase("second")

	var customSize : int = Custom.size()

func contains(values: Array[int], entries: Dictionary[String, int]) -> bool:
	return values.has(1) and entries.has("first")
