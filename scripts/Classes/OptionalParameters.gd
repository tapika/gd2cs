class_name OptionalParameters
extends RefCounted

var Value := 0

func _init(value: int = 0):
	Value = value

func choose(callback: Callable = Callable()) -> Callable:
	return callback

func configure(
	count: int = 2, ratio: float = 1.5,
	enabled: bool = true, label: String = "sample",
	target: OptionalParameters = null, key: StringName = StringName(), path: NodePath = NodePath()
):
	Value = count

func inspect():
	var copy := OptionalParameters.new()
	choose()
	configure()
	configure(4, 2.0, false, "other", copy)
