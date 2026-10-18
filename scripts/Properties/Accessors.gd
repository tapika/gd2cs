class_name Accessors
extends RefCounted

var Stored : int
var Selected : Node3D
static var Shared := 0

# Supplies reusable receiver type information.
var TargetNode : Node3D:
	get:
		return Selected

var Count : int:
	# Read the separately named backing field.
	# Accessor comments retain their position.
	get:
		return Stored

	set(value):
		if value < 0:
			return
		Stored = value

var AssignedCount : int:
	set(incoming):
		# The setter parameter name survives backtranslation.
		Stored = incoming

static var SharedCount : int:
	get:
		return Shared
	set(value):
		Shared = value

func change(value: int):
	Count = value
	AssignedCount = Count
	TargetNode.position = Vector3.ZERO
	var position := TargetNode.position

func targetPosition(holder: Holder) -> Vector3:
	return holder.Target.position

class Holder:
	var Stored : int
	var Selected : Node3D

	var Target : Node3D:
		get:
			return Selected

	var Number : int:
		set(value):
			Stored = value
		get:
			return Stored
