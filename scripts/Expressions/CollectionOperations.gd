class_name CollectionOperations
extends RefCounted

class CustomValue:
	func getValues() -> CustomValue:
		return self

	func values() -> CustomValue:
		return self

	func keys() -> CustomValue:
		return self

	func size() -> int:
		return 4

	func remove_at(index: int) -> int:
		return index

	func find(value: int) -> int:
		return value

	func IndexOf(value: int) -> int:
		return value

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
	var valueIndex : int = Values.find(2)

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
	Custom.find(1)
	Custom.IndexOf(1)

func locate(values: Array[String], value: String) -> int:
	var first := values.find(value)
	return values.find(value, first + 1)

func itemIndex(value: CustomValue) -> int:
	return CustomValues.find(value)

func removeItem(values: Array[CustomValue], index: int):
	values.remove_at(index)

func contains(values: Array[int], entries: Dictionary[String, int]) -> bool:
	return values.has(1) and entries.has("first")

func literalContains(value: int) -> bool:
	return [1, 2, 3].has(value)

func results() -> bool:
	var values := getValues()
	values.append(4)
	var entries := self.getEntries()
	var count := getValues().size()
	var empty := getEntries(1).is_empty()
	var explicitEmpty := self.getEntries().is_empty()
	var shared := CollectionOperations.staticValues()
	shared.append(5)
	var value := getVariantEntries()["count"] as int
	return entries.is_empty() and values.has(4)

func customResult(source: CustomValue) -> int:
	return source.getValues().size()

func dictionaryArrays(points: Dictionary[int, Vector2]):
	var values := points.values()
	var keys := points.keys()
	var first : Vector2 = values[0]
	var component := first.x
	var key : int = keys[0]
	var count := points.keys().size()
	var distance := points.values()[0].distance_to(points.values()[1])
	for point in points.values():
		component = point.y
	var names := Entries.keys()
	var valueKey : int = Entries["values"]
	var keyKey : int = Entries["keys"]
	var returned := getEntries().values()
	var custom := Custom.values().keys()

func entryKeys() -> Array[String]:
	return Entries.keys()

func entryValues() -> Array[int]:
	return Entries.values()

static func staticCount() -> int:
	return staticValues().size()

# Providers follow their callers so result binding cannot depend on declaration order.
func getValues() -> Array[int]:
	return Values

func getEntries(limit: int = 0) -> Dictionary[String, int]:
	return Entries

func getVariantEntries() -> Dictionary[String, Variant]:
	return {"count": 1}

static func staticValues() -> Array[int]:
	return [6]
