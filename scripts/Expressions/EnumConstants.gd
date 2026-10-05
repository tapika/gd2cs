class_name EnumConstants
extends RefCounted

func constants():
	var ignored := Control.MOUSE_FILTER_IGNORE
	var stopped := Control.MOUSE_FILTER_STOP
	var flags := Control.SIZE_EXPAND_FILL
	var alignment := HORIZONTAL_ALIGNMENT_CENTER
	var error := OK

func shadowed(Control: RefCounted):
	var ignored := Control.MOUSE_FILTER_IGNORE

func localShadowed():
	var Control := opaque()
	var ignored := Control.MOUSE_FILTER_IGNORE

func globalShadowed(HORIZONTAL_ALIGNMENT_CENTER: int):
	var alignment := HORIZONTAL_ALIGNMENT_CENTER

func opaque() -> RefCounted:
	return RefCounted.new()
