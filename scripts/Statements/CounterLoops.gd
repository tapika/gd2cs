class_name CounterLoops
extends RefCounted

var Step := 1

func changing(values: Array[int]) -> int:
	var total := 0
	if true:
		var index : int = 0
		while index < values.size():
			# Recheck the changed count, including after continue.
			if index == 0:
				values.append(4)
				index += 1
				continue
			if index == 2:
				break
			total += values[index]
			index += 1
	return total

func stepping(limit: int, step: int) -> int:
	var total := 0
	if true:
		var index := limit
		while index >= 0:
			total += index
			index -= 2
	if true:
		var index : int = 0
		while index <= limit:
			total += index
			index += step
	var cursor := 0
	if true:
		cursor = limit
		while cursor > 0:
			total += cursor
			cursor -= 1
	return total + cursor

func nested(limit: int) -> int:
	var total := 0
	if true:
		var outer : int = 0
		while outer < limit:
			if true:
				var inner : int = 0
				while inner < 2:
					if inner == 0:
						inner += 1
						continue
					while total < 1:
						total += 1
						continue
					total += outer
					inner += 1
			if outer == 1:
				outer += 1
				continue
			total += 1
			outer += 1
	return total

func stop(limit: int) -> int:
	if true:
		var index : int = 0
		while index < limit:
			if index == 1:
				return index
			index += 1
	return -1

func shadowed() -> int:
	var total := 0
	if true:
		var index : int = 0
		while index < 3:
			var Step := 2
			total += Step
			if index == 0:
				index += self.Step
				continue
			index += self.Step
	return total

func ordinary(limit: int) -> int:
	var total := 0
	if true:
		var index : int
		while index < limit:
			# A different continue step must remain an ordinary while loop.
			if index == 0:
				index += 2
				continue
			total += index
			index += 1
	return total
