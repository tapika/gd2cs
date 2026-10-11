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
