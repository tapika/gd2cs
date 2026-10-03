class_name LocalLambdas
extends RefCounted

var Value := 0

func evaluate(offset: int) -> Variant:
	var sum : Callable = func (left: int, right: int) -> int:
		# Read an enclosing parameter without changing it.
		var result := left + right + offset
		return result
	var store : Callable = func (value: int):
		self.Value = value
	var reset : Callable = func ():
		self.Value = 0
	var doubleValue := func (value: int) -> int: return value * 2
	reset.call()
	store.call(3)
	doubleValue.call(4)
	return sum.call(1, 2)
