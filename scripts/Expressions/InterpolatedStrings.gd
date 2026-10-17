class_name InterpolatedStrings
extends RefCounted

func format(text: String, count: int, ratio: float) -> String:
	var single := "Value: %s" % [text]
	var multiple := "%s_%s" % [text, count + 1]
	var precise := "%s: %.2f" % [text, ratio]
	var fraction := "%.1f units" % [ratio]
	var adjusted := "%.1f" % [(ratio + 1.0)]
	var remainder := count % 3
	var percent := "%s: 100%%" % [text]
	var braces := "{%s}" % [text]
	var currency := "€%s" % [text]
	return precise
