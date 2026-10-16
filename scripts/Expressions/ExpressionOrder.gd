class_name ExpressionOrder
extends RefCounted

var DefaultOrder := 0
var GroupedOrder := 0
var SignedOrder := 0

func evaluate(value: int):
	DefaultOrder = first(value) + second(value) * third(value)
	GroupedOrder = (first(value) + second(value)) * third(value)
	SignedOrder = -first(value) + +second(value)

func select(condition: bool, left: int, right: int) -> int:
	return left if condition else right

func compound(value: float, values: Array[int], node: Node3D, offset: Vector3):
	var total := 12.0
	total += value * 2.0
	total -= value
	total *= 2.0
	total /= 2.0
	DefaultOrder += 1
	values[nextIndex()] += 2
	node.position += offset

func nextIndex() -> int:
	return 0

func bitwise(flags: int, enabled: bool):
	var combined := flags | 2 ^ 4 & 1 + 2 * 3
	var inverted := ~flags
	var selected := combined & 7
	var grouped := flags & (1 | 2)
	var ordered := (flags & 1) + 2
	var compared := flags == 1 | 2
	var present := flags & 64 != 0
	if flags & 1:
		DefaultOrder = selected
	if enabled and flags & 2 or flags & 4:
		DefaultOrder = combined
	if not flags & 8:
		DefaultOrder = inverted
	while flags & 16:
		break
	if ~flags:
		DefaultOrder = ordered
	var chosen := 1 if flags & 32 else 0

func maskValue(flags: int) -> int:
	return flags & 3

func maskPresent(flags: int) -> bool:
	return flags & 1 != 0

func compareMask(flags: int, expected: int) -> bool:
	return flags & 3 == expected

func enumMask(flags: Control.SizeFlags) -> bool:
	return flags & Control.SIZE_EXPAND_FILL != 0

func multiline(value: int) -> int:
	var total := (
		value * 2
		+ value * 3
	)
	var partial := (value +
		value * 2 + value

		- 1)
	var nested := (

		(value
			+ 1) * 2
		+ value

	)
	return total + partial + nested
