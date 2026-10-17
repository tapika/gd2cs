class_name ArrayBuffers
extends RefCounted

var Points : Array[Vector2]
var Positions : Array[Vector3]
var Palette : Array[Color]

func apply(shape: Polygon2D, line: Line2D):
	Points.append(Vector2(1.0, 2.0))
	Positions.append(Vector3(1.0, 2.0, 3.0))
	Palette.append(Color(1.0, 0.5, 0.25))
	shape.polygon = PackedVector2Array(Points)
	line.points = PackedVector2Array([Vector2(0.0, 0.0),
		Vector2(3.0, 4.0)])

func positions() -> Variant:
	return PackedVector3Array(Positions)

func colors() -> Variant:
	return PackedColorArray(Palette)
