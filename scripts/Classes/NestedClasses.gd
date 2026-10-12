class_name NestedClasses
extends RefCounted

# Nested type comment.
# It has two comment lines.
class InnerType:
	const Limit := 7
	var Count := 2
	var Label := "nested"
	static var SharedCount := 3
	var Values : Array[int]

var Values : Array[NestedClasses.InnerType]

func identity(value: NestedClasses.InnerType) -> NestedClasses.InnerType:
	return value
