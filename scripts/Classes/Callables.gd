# A class comment for a non-trivial script.
# It also verifies multiline comments before the class declaration.
class_name Callables
extends RefCounted

var Value := 0

# Initializes the instance.
func _init(value: int, label: String):
	self.Value = value

	# Store the descriptive text too.
	self.Label = label

# Text declared between callables.
# Its position must be preserved.
var Label := ""

# Changes the numeric value.
func update(target: Callables, value: int):
	target.Value = value
	evaluate(value)

# State declared after a method.
var Enabled := true

func forward(target: Callables, value: int):
	var copy := Callables.new(
		value,
		target.Label
	)
	copy.update(
		self,
		value
	)
	apply(value,
		value,
		value, value)

func apply(
	first: int,
	second: int,
	third: int, fourth: int
):
	Value = first + second + third + fourth

static func evaluate(value: int) -> int:
	return value

# Handles cleanup.
func _exit_tree():
	Enabled = Enabled
