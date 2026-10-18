class_name NestedClasses
extends RefCounted

# Nested type comment.
# It has two comment lines.
class AABB:
	const Limit := 7
	var Count := 2
	var Label := "nested"
	static var SharedCount := 3
	var Values : Array[int]

var Values : Array #[NestedClasses.AABB]

func identity(value: NestedClasses.AABB) -> NestedClasses.AABB:
	return value

# User types must not be mistaken for the Godot built-in with the same spelling.
func create() -> AABB:
	return AABB.new()
