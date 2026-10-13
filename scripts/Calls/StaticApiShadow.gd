class_name StaticApiShadow
extends RefCounted

class TranslationServer:
	static func set_locale(locale: String):
		return

func invoke():
	TranslationServer.set_locale("en")
