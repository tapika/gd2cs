class_name StringOperations
extends RefCounted

func operations(text: String):
	var upper := text.to_upper()
	var lower := upper.to_lower()
	var contains := text.contains("part")
	var prefix := text.begins_with("pre")
	var suffix := text.ends_with("post")

func loop(values: Array[String]):
	for value in values:
		value.to_upper()

func literal() -> String:
	return "message".to_lower()

func chained(text: String) -> String:
	return text.to_upper().to_lower()
