class_name BuiltInValues
extends Node3D

var Bounds : AABB
var BoundsList : Array[AABB]

var Origin := Vector3.ZERO
var Orientation := Quaternion.IDENTITY
var Pointer := Vector2.ZERO
var Unit := Vector2.ONE
var Transform := Transform3D.IDENTITY
const Tint := Color.RED
const ReadyNotification := Node.NOTIFICATION_READY

func staticValues() -> float:
	var direction := Vector2.RIGHT
	var origin := Vector3.ZERO
	return direction.x + origin.y + Origin.z

class StaticSource:
	var ZERO := 7

func shadow(Vector3: StaticSource) -> int:
	return Vector3.ZERO

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

func world() -> World3D:
	return get_world_3d()

func ownWorld() -> World3D:
	return self.get_world_3d()

func space(node: Node3D) -> PhysicsDirectSpaceState3D:
	return node.get_world_3d().direct_space_state

func loadTexture() -> Texture2D:
	var texture := load("res://icons/sample.svg") as Texture2D
	return texture

func castPosition(node: Node) -> Vector3:
	return (node as Node3D).position

func incompatibleCast() -> bool:
	return (RefCounted.new() as Node3D) == null

func returnedBounds() -> Vector3:
	var bounds := createBounds(Vector3.ZERO, Vector3(1.0, 2.0, 3.0))
	var end := bounds.end
	return bounds.get_center() + bounds.position + end

func createBounds(position: Vector3, size: Vector3) -> AABB:
	var value : AABB = AABB(position, size)
	return value

func boundsEnd(value: AABB) -> Vector3:
	return value.position + value.size

func firstBounds(values: Array[AABB]) -> AABB:
	return values[0]

func extractBounds(entries: Dictionary[String, Variant]) -> AABB:
	return entries["bounds"] as AABB
