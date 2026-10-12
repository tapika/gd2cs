class_name DictionaryExpressions
extends RefCounted

class Item:
	var Count : int

const Defaults : Dictionary[String, bool] = {"enabled": true}

var Empty : Dictionary[String, int] = {}
var Inline : Dictionary[String, Variant] = {"count": 2, "label": "value"}
var Nodes : Dictionary[String, Node3D]
var ObjectEntries : Array[Dictionary] #[String, Node3D]
var MixedEntries : Array[Dictionary] #[String, Variant]
var CustomEntries : Array[Dictionary] #[String, DictionaryExpressions.Item]

func record(node: Node3D, size: Vector3):
	var entry : Dictionary[String, Variant] = {
		"node": node,
		"size": size,
	}

func inspect():
	var pending : Array[Dictionary] #[String, Node3D]
	var compact : Dictionary[String, Variant] = {"enabled": true, "offset": Vector3(1.0, 2.0, 3.0)}
	var nested : Dictionary[String, Variant] = {"entry": compact}
	var mixed : Dictionary[String, int] = {"first": 1,
		"second": 2, "third": 3,

		"last": 4
	}
	var empty : Dictionary[String, int] = {

	}
