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

func stringify(value: RefCounted) -> String:
	return value.to_string()

func stringifyOptional(value: RefCounted) -> String:
	if value == null:
		return "<null>"
	return value.to_string()

func stringifyChain(value: RefCounted) -> String:
	var text := value.to_string()
	return text.to_upper()

func convert(value: Variant, entries: Dictionary[String, Variant], callback: Callable) -> String:
	var empty := str()
	var nil := str(null)
	var text := str(value)
	var entry := str(entries["value"])
	var called := str(callback.call())
	return str(empty, nil, text,
		entry, called, "text", 42, true)

func custom(source: LocalStrings, value: Variant) -> String:
	return source.str(value) + source.ToString(1)

class LocalStrings:
	func str(value: Variant) -> String:
		return "custom"

	func format(value: Variant) -> String:
		return str(value)

	func ToString(value: int) -> String:
		return "custom"
