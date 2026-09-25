class_name MathFunctions
extends RefCounted

var Limited := 0.0
var Ceiling := 0
var Rounded := 0
var Minimum := 0.0
var Maximum := 0.0
var Absolute := 0.0
var Floor := 0
var Root := 0.0
var Power := 0.0
var Sine := 0.0
var Interpolated := 0.0
var IntegerMinimum := 0
var IntegerMaximum := 0

func evaluate(value: float):
	Limited = clamp(float(value - 1.0), 0.0, 1.0)
	Ceiling = ceili(value)
	Rounded = roundi(value)
	Minimum = minf(value, 1.0)
	Maximum = maxf(value, 0.0)
	Absolute = absf(value)
	Floor = floori(value)
	Root = sqrt(value)
	Power = pow(value, 2.0)
	Sine = sin(value)
	Interpolated = lerpf(value, 1.0, 0.5)
	IntegerMinimum = mini(2, ceili(value))
	IntegerMaximum = maxi(2, ceili(value))
