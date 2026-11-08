class_name StaticApiShadow
extends RefCounted

class TranslationServer:
	static func set_locale(locale: String):
		return

class JavaScriptObject:
	var width := 1.0

	func measure() -> float:
		return width

func invoke():
	TranslationServer.set_locale("en")

func readScript(api: JavaScriptObject) -> float:
	return api.width + api.measure()
