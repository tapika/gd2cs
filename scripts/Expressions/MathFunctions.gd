class_name MathFunctions
extends RefCounted

const FullTurn := TAU
const HalfTurn := PI
const Infinity := INF
const Invalid := NAN

var PositiveLimit := INF
var NegativeLimit := -INF
var Missing := NAN

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
	Limited = clampf(float(value - 1.0), 0.0, 1.0)
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

func clampValues(value: float, index: int):
	var lower := 0
	var upper := 10
	var boundedIndex := clampi(index, lower, upper)
	var boundedValue := clampf(value, 0, 1)
	var widened := clampf(float(index), lower, upper)
	var narrowed := clampi(int(value), lower, upper)
	IntegerMinimum = clampi(boundedIndex, lower, upper)
	Limited = clampf(
		clampf(boundedValue, 0, 1),
		0.0, 1.0
	)

func constants() -> float:
	var turn := TAU
	var edge := -INF
	return turn + PI + edge

func shadow(TAU: float) -> float:
	var PI := TAU
	return PI

class LocalConstants:
	const INF := 2.0

	func read() -> float:
		return INF
