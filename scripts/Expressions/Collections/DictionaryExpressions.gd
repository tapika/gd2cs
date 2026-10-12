class_name DictionaryExpressions
extends RefCounted

const Defaults : Dictionary = {"enabled": true}

var Empty := {}
var Inline := {"count": 2, "label": "value"}
var Values : Array[Dictionary]

func record(node: Node3D, size: Vector3):
	Values.append({
		"node": node,
		"size": size,
	})

func inspect():
	var compact := {"nested": {"enabled": true}, "offset": Vector3(1.0, 2.0, 3.0)}
	var mixed := {"first": 1,
		"second": 2, "third": 3,

		"last": 4
	}
	var empty := {

	}
