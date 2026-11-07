class_name Enums
extends RefCounted

# States retain implicit numbering after an explicit value.
enum TokenState {
	Empty,
	# Negative values and aliases are allowed.
	Pending = -2,
	Next,

	Parsed = 4,
	Complete,
	Alias = Parsed,
	Minimum = -2147483648,
	Maximum = 2147483647,
}

enum AccessMode {
	None, Read = 1,
	Write = 2, ReadWrite = Read | Write
}

const Initial := TokenState.Empty
var Current := TokenState.Parsed
var Unset : TokenState
var States : Array[TokenState]
var Phase := Entry.Phase.Ready
var Numeric := 4 as TokenState

func select(state: TokenState = TokenState.Empty) -> TokenState:
	var previous : TokenState
	var next := TokenState.Complete
	previous = Current
	if state == TokenState.Empty:
		Current = next
	else:
		Current = state
	return previous

func convert(value: int) -> TokenState:
	var state := value as TokenState
	var number := int(state)
	return number as TokenState

class Entry:
	# Nested enum names belong to their declaring class.
	enum Phase { Ready, Done = 3 }

	var State := Phase.Ready

	func advance(value: Phase) -> Phase:
		State = value
		return State
