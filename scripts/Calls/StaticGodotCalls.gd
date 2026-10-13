class_name StaticGodotCalls
extends RefCounted

func changeLocale(locale: String) -> String:
	TranslationServer.set_locale(locale)
	return TranslationServer.get_locale()

func platformName() -> String:
	return OS.get_name()

class CustomApi:
	func set_locale(locale: String):
		return

func parameterShadowed(TranslationServer: CustomApi):
	TranslationServer.set_locale("en")

func localShadowed():
	var TranslationServer := CustomApi.new()
	TranslationServer.set_locale("en")
