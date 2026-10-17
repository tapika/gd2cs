class_name ScalarConstants
extends RefCounted

# First scalar value.
const ValueA := 0.001
# Second scalar value.
# This comment spans two lines.
const ValueB := 3.0
const ValueC := 42
const ValueD := true
const ValueE := "text"
const ValueF := -2 + 3
const Values : Array[int] = [1, 2, 3]

# Built-in value constructors are inferred through the Godot type catalog.
const Tint := Color(1.0, 0.5, 0.25)
const Offset2 := Vector2(1.0, 2.0)
const Offset3 := Vector3(1.0, 2.0, 3.0)
const Copy3 := Offset3
const Rotation := Quaternion(0.0, 0.0, 0.0, 1.0)
const Bounds := AABB(Vector3(0.0, 0.0, 0.0), Vector3(1.0, 2.0, 3.0))

func component() -> float:
    return Tint.r
