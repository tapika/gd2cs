class_name SignalConnections
extends RefCounted

var ActionButton : Button
var ChoiceButton : OptionButton
var Selected : int

func wire():
	ActionButton.pressed.connect(onPressed)
	ActionButton.toggled.connect(onToggled)
	ChoiceButton.item_selected.connect(onSelected)
	ChoiceButton.item_selected.connect(func (index: int): onSelected(index))
	ActionButton.pressed.connect(
		onPressed
	)
	ActionButton.pressed.disconnect(onPressed)
	ChoiceButton.item_selected.disconnect(onSelected)
	ActionButton.toggled.disconnect(onToggled)

func onPressed():
	return

func onToggled(enabled: bool):
	return

func onSelected(index: int):
	# Generated parameters must not shadow existing locals.
	var index_ := 0
	Selected = index + index_

static func staticWire(button: Button):
	button.pressed.connect(onStaticPressed)
	button.pressed.disconnect(onStaticPressed)

static func onStaticPressed():
	return

class Connector:
	func connect(callback: Callable):
		return

class CustomControl:
	var item_selected : Connector

func unrelated(control: CustomControl, callback: Callable):
	control.item_selected.connect(callback)
