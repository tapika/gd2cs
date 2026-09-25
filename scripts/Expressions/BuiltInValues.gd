class_name BuiltInValues
extends RefCounted

func create() -> Vector3:
	var point2 := Vector2(1.0, 2.0)
	var point3 := Vector3(1.0, 2.0, 3.0)
	var point4 := Vector4(1.0, 2.0, 3.0, 4.0)
	var tint := Color(0.1, 0.2, 0.3, 1.0)
	var rotation := Quaternion(0.0, 0.0, 0.0, 1.0)
	return point3

func withHeight(node: Node3D, height: float) -> Vector3:
	return Vector3(node.position.x, height, node.position.z)

func attach(parent: Node3D, child: Node3D):
	parent.add_child(child)
