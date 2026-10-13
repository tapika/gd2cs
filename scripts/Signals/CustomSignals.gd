class_name CustomSignals
extends RefCounted

# Registered with this exact name.
signal changed
var Count : int

func notify(count: int, label: String):
	changed.emit()
	payload_ready.emit(count, label)
	payload_ready.emit(count,
		label
	)

# Declaration order does not limit emission binding.
signal payload_ready(count: int, label: String)

class Receiver:
	# Signals also belong to nested classes.
	signal completed

	func emit(value: int):
		completed.emit()

func ordinary(changed: Receiver):
	changed.emit(1)
