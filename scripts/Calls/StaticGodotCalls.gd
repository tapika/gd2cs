class_name StaticGodotCalls
extends RefCounted

func changeLocale(locale: String) -> String:
	TranslationServer.set_locale(locale)
	return TranslationServer.get_locale()

func platformName() -> String:
	return OS.get_name()

func configureInput(enabled: bool) -> bool:
	Input.emulate_mouse_from_touch = enabled
	Input.emulate_touch_from_mouse = false
	return Input.emulate_mouse_from_touch

func configurePower(enabled: bool) -> bool:
	OS.low_processor_usage_mode = enabled
	return OS.low_processor_usage_mode

class CustomApi:
	var emulate_mouse_from_touch := false

	func set_locale(locale: String):
		return

func parameterShadowed(TranslationServer: CustomApi):
	TranslationServer.set_locale("en")

func localShadowed():
	var TranslationServer := CustomApi.new()
	TranslationServer.set_locale("en")

func inputShadowed(Input: CustomApi):
	Input.emulate_mouse_from_touch = true
