class_name SignalConnections
extends RefCounted

var ActionButton : Button
var ChoiceButton : OptionButton
var ValueControl : SpinBox
var Selected : int
var Amount : float

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
	ValueControl.value_changed.connect(onValueChanged)
	ValueControl.value_changed.connect(func (value: float): onValueChanged(value))
	ValueControl.value_changed.disconnect(onValueChanged)

func onPressed():
	return

func onToggled(enabled: bool):
	return

func onSelected(index: int):
	# Generated parameters must not shadow existing locals.
	var index_ := 0
	Selected = index + index_

func onValueChanged(value: float):
	Amount = value

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
	var connector : Connector = control.item_selected
	connector.connect(callback)
