class_name LocalLambdas
extends RefCounted

var Value := 0

func replace():
	var callback : Callable
	callback = Callable()
	callback = func (value: int):
		self.Value = value
	if not callback.is_null():
		callback.call(3)
	callback = Callable()
	if callback.is_null():
		self.Value = 0
	if not callback.is_valid():
		self.Value = 1

func predicate(callback: Callable, value: int) -> bool:
	return callback.is_valid() and callback.call(value)

func testValidity() -> bool:
	var callback := func (value: int) -> bool: return value > 0
	if callback.is_valid():
		return callback.call(1)
	return false

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
