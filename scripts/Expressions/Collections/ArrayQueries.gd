class_name ArrayQueries
extends RefCounted

class Stock:
	var Length : int

class Item:
	var Board : Stock

var Values : Array[int]
var Weights : Array[float]
var Items : Array[Item]

func evaluate() -> bool:
	var gd2csSum := 5
	var selected := Values.filter(func (value: int) -> bool: return value > 1)
	var mapped := selected.map(func (value: int) -> int: return value * 2)
	var hasLarge := mapped.any(func (value: int) -> bool: return value > 4)
	var allPositive := mapped.all(func (value: int) -> bool: return value > 0)
	var hasAny := not Values.is_empty()
	var count := Values.filter(func (value: int) -> bool: return value > 1).size()
	var rawTotal : int = Values.reduce(func (gd2csSum2: int, item: int) -> int: return gd2csSum2 + item, 0)
	var total : int = Values.reduce(func (gd2csSum2: int, value: int) -> int: return gd2csSum2 + value * 2, 0)
	var product : int = Values.reduce(func (accumulator: int, value: int) -> int: return accumulator * value, 1)
	var aggregate : int = Values.reduce(func (accumulator: int, value: int) -> int: return accumulator + value, 0)
	return hasLarge and allPositive and hasAny and count > 0 and rawTotal > 0 and total > product and gd2csSum == 5

func sumLengths() -> int:
	var total : int = Items.reduce(
		func (gd2csSum2: int, item: Item) -> int: return gd2csSum2 + item.Board.Length, 0
	)
	return total

func sumWeights() -> float:
	var total : float = Weights.reduce(func (gd2csSum2: float, value: float) -> float: return gd2csSum2 + value, 0.0)
	return total
