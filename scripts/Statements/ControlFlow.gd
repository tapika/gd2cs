class_name ControlFlow
extends RefCounted

func process(values: Array[int], enabled: bool):
	for index in range(values.size()):
		accept(index)
	for index in range(1, values.size()):
		accept(index)

	# Iterate while preserving nested branches.
	for value in values:
		if value > 0 and enabled:
			accept(value)
		elif value == 0 or not enabled:
			continue
		else:
			break

	if enabled:
		accept(1)
	if not enabled:
		accept(0)

	while enabled:
		if values[0] < 10:
			enabled = false
		elif values[0] <= 20:
			continue
		elif values[0] >= 30:
			break
		elif values[0] != 25:
			accept(values[0])

func accept(value: int):
	value = value
