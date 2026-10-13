class_name SignalConnections
extends RefCounted

var ActionButton : Button
var ChoiceButton : OptionButton
var Selected : int

func wire():
	ActionButton.pressed.connect(onPressed)
	ChoiceButton.item_selected.connect(onSelected)
	ChoiceButton.item_selected.connect(func (index: int): onSelected(index))
	ActionButton.pressed.connect(
		onPressed
	)

func onPressed():
	return

func onSelected(index: int):
	Selected = index

func supplied(callback: Callable):
	ActionButton.pressed.connect(callback)

static func staticWire(button: Button):
	button.pressed.connect(onStaticPressed)

static func onStaticPressed():
	return

class Connector:
	func connect(callback: Callable):
		return

class CustomControl:
	var item_selected : Connector

func unrelated(control: CustomControl, callback: Callable):
	control.item_selected.connect(callback)
