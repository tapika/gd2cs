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

func inspectInput(event: InputEvent, enabled: bool):
	# Reserve the first generated pattern name to test collision handling.
	var eventTyped := 0
	accept(eventTyped)
	if event is InputEventKey and event.pressed and not event.echo:
		acceptKey(event)
	elif event is InputEventMouseButton and event.pressed:
		acceptPoint(event.position)
	else:
		acceptEvent(event)
	acceptEvent(event)

	if event is not InputEventKey:
		acceptEvent(event)
	else:
		acceptKey(event)
	if event is not InputEventKey or not event.pressed:
		acceptEvent(event)
	if event is InputEventKey or enabled:
		acceptEvent(event)

	if event is InputEventKey:
		if event.pressed:
			acceptKey(event)
	while event is InputEventKey and event.pressed:
		acceptKey(event)
		break
	var matched := event.pressed if event is InputEventKey else false
	acceptEvent(event)

func acceptKey(value: InputEventKey):
	return

func acceptPoint(value: Vector2):
	return

func acceptEvent(value: InputEvent):
	return
