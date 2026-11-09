class_name MatchCases
extends RefCounted

enum TokenState { Ready, Active, Complete }

func describe(state: TokenState) -> String:
	match state:
		TokenState.Ready:
			# Return from the function, not just the matching branch.
			return "ready"
		TokenState.Active:
			return "active"
		_:
			return "complete"

func adjust(value: int) -> int:
	var result := 0
	match value:
		-1, 0:
			result = 1

		1:
			match result:
				0:
					result = 2
		# Unmatched values use the fallback.
		_:
			result = 3
	return result

func external(state: Enums.TokenState) -> int:
	match state:
		Enums.TokenState.Empty:
			return 0
		_:
			return 1

func scoped(value: int, enabled: bool) -> int:
	match value:
		0:
			var result := 1
			return result
		_:
			var result := 2
			if enabled:
				return result
			else:
				return 0

func skip(label: String):
	match label:
		"empty":
			# This branch intentionally does nothing.
			pass
		_:
			return

func count(limit: int) -> int:
	var total := 0
	if true:
		var index : int = 0
		while index < limit:
			match index:
				0:
					index += 1
					continue
				_:
					total += index
			index += 1
	return total
