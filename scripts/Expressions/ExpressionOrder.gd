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
