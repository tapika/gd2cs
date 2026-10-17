class_name Sorting
extends RefCounted

var Values : Array[int]
var Records : Array[Dictionary] #[String, int]

func reorder():
	Values.sort()
	Values.reverse()
	Values.shuffle()
	Values.sort_custom(before)
	Values.sort_custom(func (first: int, second: int) -> bool: return first > second)
	Values.sort_custom(
		before
	)
	Values.sort_custom(func (first: int, second: int) -> bool:
		# Keep a multiline predicate body in the model.
		return first < second
	)
	var record : Dictionary[String, int] = {"priority": 2}
	Records.append(record)
	Records.sort_custom(beforeRecord)
	Records.sort_custom(func (first: Dictionary[String, int], second: Dictionary[String, int]) -> bool: return first["priority"] > second["priority"])

func before(first: int, second: int) -> bool:
	return first < second

func beforeRecord(first: Dictionary[String, int], second: Dictionary[String, int]) -> bool:
	return first["priority"] < second["priority"]
