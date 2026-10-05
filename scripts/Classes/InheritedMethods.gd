class_name InheritedMethods
extends Node3D

func inheritedCalls(child: Node3D):
	tr("message")
	self.tr("message", "context")
	add_child(child)

# Unresolved calls intentionally test shadowing, static scope, and invalid argument counts.
func parameterShadowed(tr: Callable, Tr: Callable):
	tr("message")
	Tr("message")

func localShadowed():
	var tr := opaque()
	var Tr := tr
	tr("message")
	Tr("message")

func opaque() -> RefCounted:
	return RefCounted.new()

static func staticCalls():
	tr("message")
	Tr("message")

func incompatibleArgumentCount():
	tr()
	tr("message", "context", "extra")

class UserMethods:
	func tr(message: String) -> String:
		return message

	func Tr(message: String) -> String:
		return message

	func userCalls():
		tr("message")
		self.tr("message")
		Tr("message")
		self.Tr("message")
