class_name ArrayExpressions
extends RefCounted

class ElementValue:
	var Number : int
	var Text : String

	func _init(number: int, text: String):
		Number = number
		Text = text

var InlineValues := [1, 2,
	3]

var MultilineValues := [
	ElementValue.new(10, "first"),
	ElementValue.new(20, "second"),
	ElementValue.new(30, "third")
]

func inlineValue(index: int) -> int:
	return InlineValues[index]

func inferredValue(index: int) -> float:
	var values := [4.0, 5.0, 6.0]
	return values[index]

func multilineValue(index: int) -> ElementValue:
	return MultilineValues[index]
